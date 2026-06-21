using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace mono.Core;

public class LufsService
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mono", "mono_debug.log");

    private static void Log(string msg) =>
        File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [Lufs] {msg}\n");

    public async Task<double> MeasureLufsAsync(string filePath,
        CancellationToken ct = default)
    {
        Log($"MeasureLufsAsync called for: {filePath}");

        try
        {
            var psi = new ProcessStartInfo(DependencyCheckService.FfmpegExe,
                $"-i \"{filePath}\" -filter:a ebur128=peak=true " +
                $"-f null -")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Log($"Launching: {psi.FileName} {psi.Arguments}");

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                Log("ERROR: Process.Start returned null");
                return 0;
            }

            string stderr = await proc.StandardError
                .ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            Log($"Process exited, code: {proc.ExitCode}");

            var matches = Regex.Matches(
                stderr,
                @"I:\s*([-\d.]+)\s*LUFS");
            var match = matches.Count > 0 ? matches[matches.Count - 1] : null;
            if (match != null &&
                double.TryParse(match.Groups[1].Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out double lufs))
            {
                double rounded = Math.Round(lufs, 1);
                Log($"Parsed LUFS={rounded}");
                return rounded;
            }

            Log("No integrated-LUFS line found in ffmpeg output");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex}");
        }
        return 0;
    }
}
