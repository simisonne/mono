using System.Diagnostics;
using System.IO;

namespace mono.Core;

public class FingerprintService
{
    private static readonly string FpcalcPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Assets", "Binaries", "fpcalc.exe");

    public async Task<string?> GetFingerprintAsync(string filePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(FpcalcPath)) return null;

        try
        {
            var psi = new ProcessStartInfo(FpcalcPath,
                $"-plain \"{filePath}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            string output = await proc.StandardOutput
                .ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return output.Trim().Length > 0 ? output.Trim() : null;
        }
        catch { return null; }
    }
}
