using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shapes;
using MahApps.Metro.IconPacks;

namespace mono;

public partial class MainWindow : Window
{
    private ViewModels.MainViewModel ViewModel => (ViewModels.MainViewModel)DataContext;

    private bool _isMuted = false;
    private double _volumeBeforeMute = 1.0;

    private const int WM_SIZING = 0x0214;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.ViewModel;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        App.ViewModel.Queue.CollectionChanged += OnQueueChanged;

        var savedVol = App.ViewModel.Db.GetSetting("volume");
        if (savedVol != null && double.TryParse(savedVol,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out double vol))
        {
            VolumeSlider.Value = vol;
            App.ViewModel.SetVolume(vol);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
        IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SIZING)
        {
            var rect = Marshal.PtrToStructure<RECT>(lParam);

            int edge = wParam.ToInt32();
            if (edge == WMSZ_TOP || edge == WMSZ_BOTTOM ||
                edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT ||
                edge == WMSZ_BOTTOMLEFT || edge == WMSZ_BOTTOMRIGHT)
            {
                rect.Bottom = rect.Top + (int)ActualHeight;
            }

            if (rect.Right - rect.Left < 330)
                rect.Right = rect.Left + 330;

            Marshal.StructureToPtr(rect, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsPlaying))
            PlayPauseIcon.Kind = ViewModel.IsPlaying
                ? PackIconLucideKind.Pause
                : PackIconLucideKind.Play;
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        App.ViewModel.HandleFileDrop(e.Data);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void VolumeSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var slider = sender is Slider s ? s
            : (sender as Border)?.Child as Slider;
        if (slider == null) return;

        if (e.OriginalSource is Ellipse) return;

        var track = slider.Template.FindName("PART_Track", slider) as Track;
        if (track == null) return;

        double ratio = track.ValueFromPoint(e.GetPosition(track));
        ratio = Math.Max(slider.Minimum, Math.Min(slider.Maximum, ratio));
        slider.Value = ratio;
        e.Handled = true;
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMuted)
        {
            _isMuted = false;
            VolumeSlider.Value = _volumeBeforeMute;
            App.ViewModel.SetVolume(_volumeBeforeMute);
            VolumeIcon.Kind = PackIconLucideKind.Volume2;
        }
        else
        {
            _isMuted = true;
            _volumeBeforeMute = VolumeSlider.Value > 0
                ? VolumeSlider.Value : 1.0;
            VolumeSlider.Value = 0;
            App.ViewModel.SetVolume(0);
            VolumeIcon.Kind = PackIconLucideKind.VolumeX;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double value = e.NewValue;
        App.ViewModel?.SetVolume(value);

        if (value <= 0)
        {
            VolumeIcon.Kind = PackIconLucideKind.VolumeX;
            _isMuted = true;
        }
        else
        {
            VolumeIcon.Kind = PackIconLucideKind.Volume2;
            _isMuted = false;
        }

        if (!_isMuted || value > 0)
            App.ViewModel?.Db.SaveSetting("volume",
                value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void PlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.ViewModel.Queue.Count == 0)
            return;

        PlaylistDockView.Visibility =
            PlaylistDockView.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            var vm = DataContext as ViewModels.MainViewModel;
            if (vm?.PlayPauseCommand?.CanExecute(null) == true)
            {
                vm.PlayPauseCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        int count = App.ViewModel.Queue.Count;

        if (count == 0)
            PlaylistDockView.Visibility = Visibility.Collapsed;

        if (count >= 2)
        {
            PlaylistBadgeText.Text = count > 99 ? "99+" : count.ToString();
            PlaylistBadge.Visibility = Visibility.Visible;
        }
        else
        {
            PlaylistBadge.Visibility = Visibility.Collapsed;
        }

        if (count == 2)
            PlaylistBadge.Visibility = Visibility.Visible;
    }
}
