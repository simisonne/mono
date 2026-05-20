using System.Diagnostics;
using System.IO;
using System.Windows;

namespace mono.Core;

public class AnalysisService
{
    private readonly FingerprintService _fp = new();
    private readonly PythonSidecarService _sidecar;
    private readonly LufsService _lufs = new();
    private readonly LibraryDb _db;

    private CancellationTokenSource _cts = new();

    private double? _partialBpm;
    private string? _partialKey;
    private double? _partialLufs;

    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "mono_debug.log");

    private static void Log(string msg) =>
        File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");

    public event Action<AnalysisResult>? AnalysisComplete;

    public AnalysisService(LibraryDb db)
    {
        _db = db;
        _sidecar = new PythonSidecarService();
        _ = _sidecar.EnsureVenvAsync();
    }

    public void Analyze(string filePath)
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _ = RunAsync(filePath, _cts.Token);
    }

    private async Task RunAsync(string filePath, CancellationToken ct)
    {
        try
        {
            Log($"[Analysis] Starting analysis for: {filePath}");

            string? fingerprint;
            TrackAnalysis? cached;
            (fingerprint, cached) = await Task.Run(() =>
            {
                var fp = _fp.GetFingerprintAsync(filePath, ct).GetAwaiter().GetResult();
                var ca = _db.GetAnalysis(filePath, fp);
                return (fp, ca);
            });
            if (cached != null && cached.Bpm > 0)
            {
                Log($"[Analysis] Cache hit: BPM={cached.Bpm}, Key={cached.MusicalKey}, LUFS={cached.Lufs}");
                AnalysisComplete?.Invoke(new AnalysisResult
                {
                    Bpm = cached.Bpm,
                    Key = cached.MusicalKey,
                    Lufs = cached.Lufs,
                    FromCache = true
                });
                return;
            }

            Log("[Analysis] No cache — launching BPM + Key + LUFS in parallel");

            _partialBpm = null;
            _partialKey = null;
            _partialLufs = null;

            var bpmTask = _sidecar.GetBpmAsync(filePath, ct);
            var keyTask = _sidecar.GetKeyAsync(filePath, ct);
            var lufsTask = _lufs.MeasureLufsAsync(filePath, ct);

            _ = bpmTask.ContinueWith(t =>
            {
                if (ct.IsCancellationRequested || t.IsFaulted) return;
                _partialBpm = t.Result;
                Application.Current?.Dispatcher.InvokeAsync(() =>
                    AnalysisComplete?.Invoke(new AnalysisResult
                    {
                        Bpm      = _partialBpm  ?? 0,
                        Key      = _partialKey  ?? "",
                        Lufs     = _partialLufs ?? 0,
                        FromCache = false
                    }));
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

            _ = keyTask.ContinueWith(t =>
            {
                if (ct.IsCancellationRequested || t.IsFaulted) return;
                _partialKey = t.Result;
                Application.Current?.Dispatcher.InvokeAsync(() =>
                    AnalysisComplete?.Invoke(new AnalysisResult
                    {
                        Bpm      = _partialBpm  ?? 0,
                        Key      = _partialKey  ?? "",
                        Lufs     = _partialLufs ?? 0,
                        FromCache = false
                    }));
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

            _ = lufsTask.ContinueWith(t =>
            {
                if (ct.IsCancellationRequested || t.IsFaulted) return;
                FirePartialUpdate(null, null, t.Result);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

            await Task.WhenAll(bpmTask, keyTask, lufsTask);

            if (ct.IsCancellationRequested) return;

            Log($"[Analysis] All tasks done — BPM={bpmTask.Result}, Key={keyTask.Result}, LUFS={lufsTask.Result}");
            _db.SaveAnalysis(filePath, fingerprint,
                bpmTask.Result, keyTask.Result, lufsTask.Result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"[Analysis] Error: {ex}");
        }
    }

    private void FirePartialUpdate(double? bpm, string? key, double? lufs)
    {
        if (bpm.HasValue)  _partialBpm  = bpm;
        if (key != null)   _partialKey  = key;
        if (lufs.HasValue) _partialLufs = lufs;

        Application.Current?.Dispatcher.InvokeAsync(() =>
            AnalysisComplete?.Invoke(new AnalysisResult
            {
                Bpm  = _partialBpm  ?? 0,
                Key  = _partialKey  ?? "",
                Lufs = _partialLufs ?? 0,
                FromCache = false
            }));
    }
}

public class AnalysisResult
{
    public double Bpm { get; set; }
    public string Key { get; set; } = "";
    public double Lufs { get; set; }
    public bool FromCache { get; set; }
}
