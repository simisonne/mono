using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace mono.Core;

public class LufsService
{
    public async Task<double> MeasureLufsAsync(string filePath,
        CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo("ffmpeg",
                $"-i \"{filePath}\" -filter:a ebur128=peak=true " +
                $"-f null -")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            string stderr = await proc.StandardError
                .ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            var matches = Regex.Matches(
                stderr,
                @"I:\s*([-\d.]+)\s*LUFS");
            var match = matches.Count > 0 ? matches[matches.Count - 1] : null;
            if (match != null &&
                double.TryParse(match.Groups[1].Value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out double lufs))
                return Math.Round(lufs, 1);
        }
        catch { }
        return 0;
    }
}
