using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace mono.Core;

internal static class DependencyCheckService
{
    private const string Tag = "DepCheck";

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mono");

    private static readonly string LogPath = Path.Combine(AppDataDir, "mono_debug.log");

    // Feature 2 (deferred): folder where on-demand portable copies of
    // node.exe / ffmpeg.exe would be placed. Checked ahead of PATH so a
    // successful auto-download wins over a system install.
    internal static readonly string LocalBinDir = Path.Combine(AppDataDir, "bin");

    // --- Session-scoped, in-memory availability cache -----------------------
    // Probed once per app session (eagerly at startup), cached for the process
    // lifetime, never persisted to SQLite.
    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private static bool _initialized;
    private static bool _nodeAvailable;
    private static bool _ffmpegAvailable;

    // Resolved executable to invoke (bare name = PATH-resolved, or absolute
    // path to a %AppData%/mono/bin copy). NodeAnalysisService / LufsService
    // read these so the actual invocation always matches what was probed.
    private static string _nodeExe = "node";
    private static string _ffmpegExe = "ffmpeg";

    // QA --no-node / --no-ffmpeg flags, captured at init so the install flow
    // can tell "genuinely missing, installable" from "QA-forced missing, do
    // not attempt". A missing dep that is force-suppressed makes the install
    // command stay disabled (clicking would be meaningless for QA).
    private static bool _nodeForceSuppressed;
    private static bool _ffmpegForceSuppressed;

    public static bool IsNodeAvailable => _nodeAvailable;
    public static bool IsFfmpegAvailable => _ffmpegAvailable;
    public static string NodeExe => _nodeExe;
    public static string FfmpegExe => _ffmpegExe;

    // True when at least one dep is missing AND none of the missing deps are
    // QA-force-suppressed - i.e. a click on the banner can actually install
    // something. Gates the install command's CanExecute.
    public static bool CanInstallMissing
    {
        get
        {
            bool nodeMissing = !_nodeAvailable;
            bool ffmpegMissing = !_ffmpegAvailable;
            if (!nodeMissing && !ffmpegMissing) return false;
            if (nodeMissing && _nodeForceSuppressed) return false;
            if (ffmpegMissing && _ffmpegForceSuppressed) return false;
            return true;
        }
    }

    // Feature 2: raised (off the UI thread) when an on-demand download flips a
    // previously-missing dependency to available mid-session. App.xaml.cs
    // subscribes and recomputes the missing-dependency banner on the
    // dispatcher so the notice clears/updates without an app restart.
    internal static event Action? AvailabilityChanged;

    /// <summary>
    /// Probe node and ffmpeg once per session and cache the result in memory.
    /// Must be called off the UI thread (Process spawning). Safe to call
    /// multiple times; only the first call performs the probe.
    /// </summary>
    public static async Task InitializeAsync(
        bool forceNoNode = false, bool forceNoFfmpeg = false,
        bool fakeMissingNode = false, bool fakeMissingFfmpeg = false)
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            Directory.CreateDirectory(AppDataDir);

            // forceNo* skips the probe AND marks the dep suppressed (install
            // disabled). fakeMissing* skips the probe but leaves the dep
            // installable, so the full click-to-install flow can be exercised
            // without uninstalling the real binary.
            (_nodeAvailable, _nodeExe) = (forceNoNode || fakeMissingNode)
                ? (false, "node")
                : await ProbeAsync("node", "-v", "node.exe");

            (_ffmpegAvailable, _ffmpegExe) = (forceNoFfmpeg || fakeMissingFfmpeg)
                ? (false, "ffmpeg")
                : await ProbeAsync("ffmpeg", "-version", "ffmpeg.exe");

            // Only the old forceNo* flags suppress install; fakeMissing* does not.
            _nodeForceSuppressed = forceNoNode;
            _ffmpegForceSuppressed = forceNoFfmpeg;

            Log($"Initialized - node={Status(_nodeAvailable)} ('{_nodeExe}'), " +
                $"ffmpeg={Status(_ffmpegAvailable)} ('{_ffmpegExe}')");

            if (forceNoNode)        Log("node forced MISSING + suppressed via --no-node (QA flag)");
            if (forceNoFfmpeg)      Log("ffmpeg forced MISSING + suppressed via --no-ffmpeg (QA flag)");
            if (fakeMissingNode)    Log("node fake-missing (installable) via --fake-missing-node (QA flag)");
            if (fakeMissingFfmpeg)  Log("ffmpeg fake-missing (installable) via --fake-missing-ffmpeg (QA flag)");

            _initialized = true;

            // Downloads are now USER-INITIATED only (a click on the banner),
            // never auto-fired here. The --no-node / --no-ffmpeg QA flags
            // additionally suppress any install attempt, so QA can simulate
            // "missing and not installable" - the banner shows the missing
            // state but the install command stays disabled (CanInstallMissing
            // == false). The --fake-missing-* variants instead report missing
            // while keeping install enabled, so the click-to-install flow can
            // be exercised without uninstalling the real binary. AvailabilityChanged
            // + BuildMissingDependencyNotice drive the banner after each
            // successful install (see below).
        }
        finally
        {
            _initLock.Release();
        }
    }

    // Orchestrates a user-initiated install of whichever deps are currently
    // missing (and not QA-suppressed), ONE AT A TIME (never parallel), behind
    // a single shared progress bar. progress carries both a human label
    // ("Installing node.exe... (1 of 2)") and a 0-100 percent for the current
    // dep, so the UI can show which file is downloading, not one undiff-
    // erentiated bar across both.
    //
    // Returns true only if every attempted dep installed; returns false on
    // the first failure (the chain stops - no in-session auto-retry per
    // spec). Throws OperationCanceledException if the user cancels; a dep
    // that completed before the cancel KEEPS its installed state, because its
    // OnDependencyInstalledAsync (re-probe + AvailabilityChanged) already ran.
    //
    // Runs as async I/O off the UI thread; never blocks playback. Stays on
    // the caller's sync context (no ConfigureAwait(false)) so the per-dep
    // label mutation and the Progress<int> proxy callback share one thread
    // (the UI thread), avoiding a race on currentLabel.
    internal static async Task<bool> InstallMissingDepsAsync(
        IProgress<InstallProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        // Ordered work list: node first, then ffmpeg (matches the "1 of 2" /
        // "2 of 2" example). A dep is included only if missing AND not QA-
        // suppressed; the install command's CanExecute already gates this, but
        // the re-check is defensive.
        var work = new List<(string name, string versionArg, string localFileName,
            Func<IProgress<int>?, CancellationToken, Task<bool>> download, bool isNode)>();

        if (!_nodeAvailable && !_nodeForceSuppressed)
            work.Add(("node", "-v", DependencyManifest.NodeLocalFileName,
                DependencyDownloadService.DownloadNodeAsync, true));
        if (!_ffmpegAvailable && !_ffmpegForceSuppressed)
            work.Add(("ffmpeg", "-version", DependencyManifest.FfmpegLocalFileName,
                DependencyDownloadService.DownloadFfmpegAsync, false));

        if (work.Count == 0) return true; // nothing to do (e.g. all present)

        // The download service reports percent-only (IProgress<int>); wrap it
        // so each tick is re-published with the current dep's label. Progress<T>
        // marshals callbacks to the captured (UI) context.
        string currentLabel = "";
        var percentProxy = new Progress<int>(pct =>
            progress?.Report(new InstallProgressUpdate(currentLabel, pct)));

        for (int i = 0; i < work.Count; i++)
        {
            var (name, versionArg, localFileName, download, isNode) = work[i];
            currentLabel = $"Installing {localFileName}... ({i + 1} of {work.Count})";
            progress?.Report(new InstallProgressUpdate(currentLabel, 0));

            bool ok = await download(percentProxy, cancellationToken);
            if (!ok) return false; // failure stops the chain; dep stays "missing"

            // Re-probe + flip cached state + raise AvailabilityChanged so the
            // banner recompute (and the partial-success path) sees this dep as
            // installed even if a later dep is cancelled or fails.
            await OnDependencyInstalledAsync(name, versionArg, localFileName, isNode);
        }
        return true;
    }

    // Re-probes just this dependency (cheap: `node -v` / `ffmpeg -version`
    // with a 5s timeout) and flips the cached state. The local bin copy, if
    // just installed, now wins over PATH via ProbeAsync's bin-first lookup.
    // Raises AvailabilityChanged so App.xaml.cs can refresh the banner.
    private static async Task OnDependencyInstalledAsync(
        string name, string versionArg, string localFileName, bool isNode)
    {
        try
        {
            var (ok, exe) = await ProbeAsync(name, versionArg, localFileName)
                .ConfigureAwait(false);
            if (isNode) { _nodeAvailable = ok; _nodeExe = exe; }
            else        { _ffmpegAvailable = ok; _ffmpegExe = exe; }
            Log($"post-download reprobe - {name}={Status(ok)} ('{exe}')");
            AvailabilityChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log($"post-download reprobe '{name}' threw - " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Single, user-facing notice describing which analyses are unavailable
    /// this session, or null if everything is available. Consumed by the UI
    /// (kept here to avoid Core -> ViewModels coupling).
    /// </summary>
    internal static string? BuildMissingDependencyNotice()
    {
        bool nodeMissing = !_nodeAvailable;
        bool ffmpegMissing = !_ffmpegAvailable;

        if (!nodeMissing && !ffmpegMissing) return null;

        // The "Click to install" CTA only appears when a click can actually
        // install (no QA suppression of the missing dep). When suppressed,
        // the banner is informational only and the install command stays
        // disabled - so the copy must not promise an action it won't perform.
        string cta = CanInstallMissing ? " Click to install." : "";

        if (nodeMissing && ffmpegMissing)
            return CanInstallMissing
                ? "2 dependencies missing \u2014 Click to install"
                : "BPM/Key (Node.js) and LUFS (ffmpeg) analysis unavailable.";
        if (nodeMissing)
            return "BPM/Key analysis unavailable - Node.js not found." + cta;
        return "LUFS analysis unavailable - ffmpeg not found." + cta;
    }

    // Probes a candidate (a local %AppData%/mono/bin copy if present, else the
    // bare PATH name) and returns (available, exeToInvoke).
    private static async Task<(bool ok, string exe)> ProbeAsync(
        string name, string versionArg, string localFileName)
    {
        string local = Path.Combine(LocalBinDir, localFileName);
        string exe = File.Exists(local) ? local : name;

        try
        {
            using var proc = Process.Start(new ProcessStartInfo(exe, versionArg)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Process.Start returned null");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                Log($"probe '{name}' ('{exe} {versionArg}'): timed out (>5s)");
                return (false, exe);
            }

            bool ok = proc.ExitCode == 0;
            Log($"probe '{name}' ('{exe} {versionArg}'): exit={proc.ExitCode} -> {(ok ? "ok" : "missing")}");
            return (ok, exe);
        }
        catch (Exception ex)
        {
            // Typically Win32Exception when the executable is not on PATH.
            Log($"probe '{name}' ('{exe} {versionArg}'): not runnable - {ex.GetType().Name}: {ex.Message}");
            return (false, exe);
        }
    }

    private static string Status(bool available) => available ? "ok" : "MISSING";

    private static void Log(string msg) =>
        File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [{Tag}] {msg}\n");

    // Feature 2 download pins (URL + sha256 + archive size per dependency) and
    // the licensing rationale live in DependencyManifest.cs - the single file
    // to edit when intentionally re-pinning.
}

// Carries both the human label ("Installing node.exe... (1 of 2)") and a 0-100
// percent for the currently-downloading dep, for the banner's single shared
// progress bar. Reported by DependencyCheckService.InstallMissingDepsAsync via
// IProgress<T>, which marshals to the UI thread for binding updates.
internal sealed record InstallProgressUpdate(string Label, int Percent);
