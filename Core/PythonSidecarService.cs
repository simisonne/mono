using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace mono.Core;

public class PythonSidecarService
{
    private static readonly string ScriptsDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Assets", "Scripts");

    private static readonly string BinariesDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Assets", "Binaries");

    private static readonly string BpmExe = Path.Combine(BinariesDir, "analyze_bpm.exe");

    private static readonly string KeyExe = Path.Combine(BinariesDir, "analyze_key.exe");

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mono", "mono_debug.log");

    private static void Log(string msg) =>
        File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");

    public Task<bool> IsReadyAsync() =>
        Task.FromResult(File.Exists(BpmExe) && File.Exists(KeyExe));

    public async Task<double> GetBpmAsync(string filePath,
        CancellationToken ct = default)
    {
        string stdout = await RunScriptAsync(BpmExe, filePath, ct);
        if (string.IsNullOrWhiteSpace(stdout)) return 0;
        var doc = JsonDocument.Parse(stdout.Trim());
        return doc.RootElement.TryGetProperty("bpm", out var b)
            ? b.GetDouble() : 0;
    }

    public async Task<string> GetKeyAsync(string filePath,
        CancellationToken ct = default)
    {
        string stdout = await RunScriptAsync(KeyExe, filePath, ct);
        if (string.IsNullOrWhiteSpace(stdout)) return "";
        var doc = JsonDocument.Parse(stdout.Trim());
        return doc.RootElement.TryGetProperty("key", out var k)
            ? k.GetString() ?? "" : "";
    }

    private async Task<string> RunScriptAsync(string exe,
        string filePath, CancellationToken ct)
    {
        if (!await IsReadyAsync()) return "";
        try
        {
            var psi = new ProcessStartInfo(exe, $"\"{filePath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi)!;
            string stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            string stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (!string.IsNullOrEmpty(stderr))
                Debug.WriteLine($"[Sidecar] {Path.GetFileName(exe)} stderr: {stderr}");
            return stdout;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sidecar] {Path.GetFileName(exe)} error: {ex.Message}");
            return "";
        }
    }
}
