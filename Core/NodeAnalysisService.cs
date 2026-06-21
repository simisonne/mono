using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mono.Core;

internal record NodeAnalysisResult(double Bpm, string Key);

internal static class NodeAnalysisService
{
    private static readonly string ScriptPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Binaries", "key", "oracle.js");

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mono", "mono_debug.log");

    private static void Log(string msg) =>
        File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [NodeAnalysis] {msg}\n");

    public static async Task<NodeAnalysisResult> GetAnalysisAsync(
        string audioPath, CancellationToken ct = default)
    {
        Log($"GetAnalysisAsync called for: {audioPath}");

        if (!File.Exists(ScriptPath))
        {
            Log($"ERROR: oracle.js not found at {ScriptPath}");
            throw new FileNotFoundException("oracle.js not found", ScriptPath);
        }

        var psi = new ProcessStartInfo
        {
            FileName               = DependencyCheckService.NodeExe,
            Arguments              = $"\"{ScriptPath}\" \"{audioPath}\"",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = Path.GetDirectoryName(ScriptPath)!
        };

        Log($"Launching: {psi.FileName} {psi.Arguments}");

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        Log($"Process started, PID: {proc.Id}");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = proc.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Log("Cancelled - killing oracle process");
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        Log($"Process exited, code: {proc.ExitCode}");
        Log($"stdout: {stdout.Trim()}");
        if (!string.IsNullOrEmpty(stderr))
            Log($"stderr: {stderr.Trim()}");

        if (proc.ExitCode != 0)
            throw new Exception($"oracle.js exited {proc.ExitCode}: {stderr.Trim()}");

        using var doc = JsonDocument.Parse(stdout.Trim());
        var root = doc.RootElement;

        double bpm = root.TryGetProperty("bpm", out var bpmEl) ? bpmEl.GetDouble() : 0;
        string key = root.TryGetProperty("key", out var keyEl) ? keyEl.GetString() ?? "" : "";

        if (bpm > 0 && bpm <= 84) bpm *= 2;

        Log($"Parsed - BPM={bpm}, Key={key}");
        return new NodeAnalysisResult(bpm, key);
    }
}
