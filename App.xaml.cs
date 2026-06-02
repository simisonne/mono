using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using mono.Core;

namespace mono;

public partial class App : Application
{
    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FLASHW_STOP = 0;

    private static Mutex? _mutex;
    private const string MutexName = "mono_single_instance_9f3a";
    private const string PipeName = "mono_ipc_9f3a";

    public static ViewModels.MainViewModel ViewModel { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "mono — fatal error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            string? path = e.Args.Length > 0 ? e.Args[0] : null;
            TrySendToExistingInstance(path);
            Shutdown();
            return;
        }

        ViewModel = new ViewModels.MainViewModel();

        try
        {
            FileIconRegistryService.RegisterIfNeeded(ViewModel.Db);
        }
        catch
        {
            // Silently swallow registry access failures - will retry on next launch
        }

        var window = new MainWindow();
        window.Show();

        _ = Task.Run(StartPipeServer);

        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            ViewModel.OpenSingleFile(e.Args[0]);
    }

    private static void TrySendToExistingInstance(string? filePath)
    {
        if (filePath == null) return;
        try
        {
            using var client = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 2000);
            using var writer = new StreamWriter(client);
            writer.WriteLine(filePath);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IPC] Failed to send to existing instance: {ex.Message}");
        }
    }

    private static async Task StartPipeServer()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync();

                using var reader = new StreamReader(server);
                string? path = await reader.ReadLineAsync();

                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    Current.Dispatcher.Invoke(() =>
                    {
                        if (Current.MainWindow is MainWindow w)
                        {
                            w.WindowState = WindowState.Normal;
                            w.Activate();
                            w.Focusable = true;
                            w.Focus();

                            var info = new FLASHWINFO
                            {
                                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                                hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle,
                                dwFlags = FLASHW_STOP
                            };
                            FlashWindowEx(ref info);
                        }
                        ViewModel?.OpenSingleFile(path);
                    });

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var win = Application.Current.MainWindow;
                        if (win == null) return;
                        if (win.WindowState == WindowState.Minimized)
                            win.WindowState = WindowState.Normal;
                        win.Activate();
                        win.Topmost = true;
                        win.Topmost = false;
                        win.Focus();
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IPC] Pipe server error: {ex.Message}");
                await Task.Delay(500);
            }
        }
    }
}
