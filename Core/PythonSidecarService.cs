using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace mono.Core;

public class PythonSidecarService
{
    private static readonly string ScriptsDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Assets", "Scripts");

    private static readonly string VenvPython = Path.Combine(
        ScriptsDir, "venv", "Scripts", "python.exe");

    private static readonly string BpmScript = Path.Combine(
        ScriptsDir, "analyze_bpm.py");

    private static readonly string KeyScript = Path.Combine(
        ScriptsDir, "analyze_key.py");

    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "mono_debug.log");

    private static void Log(string msg) =>
        File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");

    private readonly SemaphoreSlim _setupLock = new(1, 1);

    public async Task<bool> IsReadyAsync()
    {
        if (!File.Exists(VenvPython)) return false;
        try
        {
            var psi = new ProcessStartInfo(VenvPython, "-c \"import numpy\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi)!;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await proc.WaitForExitAsync(cts.Token);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureVenvAsync()
    {
        await _setupLock.WaitAsync();
        try
        {
            if (await IsReadyAsync()) return;

            var bat = Path.Combine(ScriptsDir, "setup_venv.bat");
            if (!File.Exists(bat))
            {
                Log("[Sidecar] setup_venv.bat not found");
                return;
            }

            Log("[Sidecar] Setting up Python venv...");
            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi)!;
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            string stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            Log($"[Sidecar] Setup exit code: {proc.ExitCode}");
            if (!string.IsNullOrEmpty(stderr))
                Log($"[Sidecar] Setup stderr: {stderr}");
            Log($"[Sidecar] IsReady after setup: {await IsReadyAsync()}");
        }
        finally
        {
            _setupLock.Release();
        }
    }

    public async Task<double> GetBpmAsync(string filePath,
        CancellationToken ct = default)
    {
        string stdout = await RunScriptAsync(BpmScript, filePath, ct);
        if (string.IsNullOrWhiteSpace(stdout)) return 0;
        var doc = JsonDocument.Parse(stdout.Trim());
        return doc.RootElement.TryGetProperty("bpm", out var b)
            ? b.GetDouble() : 0;
    }

    public async Task<string> GetKeyAsync(string filePath,
        CancellationToken ct = default)
    {
        string stdout = await RunScriptAsync(KeyScript, filePath, ct);
        if (string.IsNullOrWhiteSpace(stdout)) return "";
        var doc = JsonDocument.Parse(stdout.Trim());
        return doc.RootElement.TryGetProperty("key", out var k)
            ? k.GetString() ?? "" : "";
    }

    private async Task<string> RunScriptAsync(string script,
        string filePath, CancellationToken ct)
    {
        if (!await IsReadyAsync()) return "";
        try
        {
            var psi = new ProcessStartInfo(
                VenvPython, $"\"{script}\" \"{filePath}\"")
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
                Debug.WriteLine($"[Sidecar] {Path.GetFileName(script)} stderr: {stderr}");
            return stdout;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sidecar] {Path.GetFileName(script)} error: {ex.Message}");
            return "";
        }
    }
}
