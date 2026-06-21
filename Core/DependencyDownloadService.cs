using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;

namespace mono.Core;

// Feature 2: on-demand download of node.exe / ffmpeg.exe into
// %AppData%/mono/bin. Triggered ONLY by an explicit user click on the
// missing-dependency banner (DependencyCheckService.InstallMissingDepsAsync
// orchestrates the sequence); never auto-fired at startup. Reports real
// download percentage via IProgress<int> (derived from the already-fetched
// Content-Length, no extra network round-trip) and is cancellable mid-stream
// via CancellationToken, which aborts the in-flight HttpClient request.
//
// Silent fail on error: any non-cancel failure (network / hash mismatch /
// size mismatch / disk) is logged via the established Log() convention and
// the dependency stays "missing"; the banner reverts to its clickable
// missing-state row so the user can retry. User-cancellation is logged
// distinctly and is NOT a failure.
//
// Flow per dependency:
//   stream -> temp .zip  (ResponseHeadersRead, never loads whole archive in LOH)
//   verify Content-Length (if reported) == pinned size
//   verify downloaded file size == pinned size
//   verify sha256(file)    == pinned sha256
//   open zip, find entry by LEAF filename, extract -> temp .exe
//   atomic File.Move into %AppData%/mono/bin (same volume -> atomic on NTFS)
//
// The caller drives this as async I/O off the UI thread (never blocks
// playback). Cancellation is honored INSIDE the read/write loop - both
// ReadAsync and WriteAsync take the token, so cancel aborts the in-flight
// HTTP stream immediately rather than waiting for the current chunk to drain.
internal static class DependencyDownloadService
{
    private const string Tag = "DepDownload";

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mono");
    private static readonly string LogPath =
        Path.Combine(AppDataDir, "mono_debug.log");

    // One shared client. No global Timeout: a 145 MiB archive on a slow link
    // can exceed the 100s default, so the per-request CTS governs instead.
    // HTTP/1.1 for max CDN/proxy compatibility across the GitHub redirect chain.
    private static readonly HttpClient Http = new HttpClient();

    // No per-session dedup flags: downloads are now exclusively user-clicked,
    // and the install command's IsInstallInProgress guard prevents a
    // double-fire from a double-click. Removing the old attempt-once flags
    // also lets a user legitimately retry after a cancel/failure.

    public static Task<bool> DownloadNodeAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
        => DownloadAsync(
            "node",
            DependencyManifest.NodeDownloadUrl,
            DependencyManifest.NodeSha256,
            DependencyManifest.NodeArchiveSizeBytes,
            DependencyManifest.NodeLocalFileName,
            progress,
            cancellationToken);

    public static Task<bool> DownloadFfmpegAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
        => DownloadAsync(
            "ffmpeg",
            DependencyManifest.FfmpegDownloadUrl,
            DependencyManifest.FfmpegSha256,
            DependencyManifest.FfmpegArchiveSizeBytes,
            DependencyManifest.FfmpegLocalFileName,
            progress,
            cancellationToken);

    // Returns true only when the binary landed in binDir and passed every
    // check. Throws OperationCanceledException ONLY for a user-initiated
    // cancel (so the orchestrator can treat it as "not a failure"); a 15m
    // timeout is a normal failure that returns false. Never throws for other
    // errors: every other failure path logs and returns false.
    private static async Task<bool> DownloadAsync(
        string name, string url, string expectedSha256,
        long expectedSize, string leafFileName,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        string binDir = DependencyCheckService.LocalBinDir;
        Directory.CreateDirectory(binDir);

        // Temp files live UNDER binDir so the final rename is same-volume
        // (atomic on NTFS). Guid suffix avoids collisions across attempts.
        string zipTemp  = Path.Combine(binDir, $".{leafFileName}.{Guid.NewGuid():N}.zip.tmp");
        string? exeTemp = Path.Combine(binDir, $".{leafFileName}.{Guid.NewGuid():N}.exe.tmp");
        string exeFinal = Path.Combine(binDir, leafFileName);

        Log($"{name}: starting download -> {url}");
        try
        {
            // Link the caller's (user-cancel) token with a 15m hard timeout.
            // The user token wins the cancel semantics; the timeout is a
            // distinct, logged-as-failure path.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);
            CancellationToken token = linked.Token;

            using var resp = await Http.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            // Content-Length from the headers we already fetched - reused as
            // the progress denominator (no second network round-trip).
            long? reported = resp.Content.Headers.ContentLength;
            long total = reported ?? expectedSize;

            if (reported is long cl && cl != expectedSize)
                throw new InvalidDataException(
                    $"server Content-Length {cl} != pinned {expectedSize}");

            // Manual buffered copy so we can report percentage AND honor
            // cancellation inside the read/write cycle. ReadAsync/WriteAsync
            // both receive the token, so a cancel aborts the in-flight HTTP
            // stream mid-chunk rather than after the current buffer drains.
            // Buffer size matches the previous CopyToAsync default (80 KiB).
            long read = 0;
            byte[] buffer = new byte[81920];
            int lastReportedPercent = -1;
            await using (var src = await resp.Content
                .ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var dst = new FileStream(zipTemp, FileMode.Create,
                FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            {
                int n;
                while ((n = await src.ReadAsync(
                    buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(
                        buffer, 0, n, token).ConfigureAwait(false);
                    read += n;

                    if (total > 0 && progress != null)
                    {
                        int pct = (int)(read * 100 / total);
                        if (pct > 100) pct = 100; // guard a misreported Content-Length
                        if (pct != lastReportedPercent)
                        {
                            progress.Report(pct);
                            lastReportedPercent = pct;
                        }
                    }
                }
            }
            progress?.Report(100);

            // --- verify the downloaded archive on disk -----------------------
            long actualSize = new FileInfo(zipTemp).Length;
            if (actualSize != expectedSize)
                throw new InvalidDataException(
                    $"downloaded size {actualSize} != pinned {expectedSize}");

            string actualSha;
            await using (var fs = File.OpenRead(zipTemp))
                actualSha = Convert.ToHexString(
                    await SHA256.HashDataAsync(fs, token).ConfigureAwait(false));

            if (!string.Equals(actualSha, expectedSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"sha256 mismatch: got {actualSha}, want {expectedSha256}");

            // --- extract the single target entry by LEAF filename -----------
            // Lookup by leaf (not full path) so we don't pin the versioned
            // folder name inside the zip; e.g. node-v24.17.0-win-x64\node.exe
            // or ffmpeg-...-win64-lgpl\bin\ffmpeg.exe.
            using (var zip = new ZipArchive(
                File.OpenRead(zipTemp), ZipArchiveMode.Read))
            {
                var entry = zip.Entries.FirstOrDefault(e =>
                        string.Equals(e.Name, leafFileName,
                            StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException(
                        $"zip contains no entry named '{leafFileName}'");
                entry.ExtractToFile(exeTemp, overwrite: true);
            }

            // Atomic on NTFS: exeTemp and exeFinal share a volume (both under
            // binDir). overwrite:true replaces any stale/partial prior copy.
            File.Move(exeTemp, exeFinal, overwrite: true);
            exeTemp = null; // signal "consumed" so the finally won't delete it

            Log($"{name}: installed '{exeFinal}' " +
                $"(sha256 ok, {actualSize} archive bytes)");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User cancelled via the banner's X. Distinct from a timeout:
            // cancellation is an intentional abort, not a failure.
            Log($"cancelled by user: {name}");
            throw;
        }
        catch (OperationCanceledException)
        {
            Log($"{name}: download timed out (>15m)");
            return false;
        }
        catch (Exception ex)
        {
            Log($"{name}: download FAILED - {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (zipTemp != null && File.Exists(zipTemp)) File.Delete(zipTemp); }
            catch { }
            try { if (exeTemp != null && File.Exists(exeTemp)) File.Delete(exeTemp); }
            catch { }
        }
    }

    private static void Log(string msg) =>
        File.AppendAllText(LogPath,
            $"[{DateTime.Now:HH:mm:ss.fff}] [{Tag}] {msg}\n");
}
