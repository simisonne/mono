using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using mono.Core;
using mono.Models;

namespace mono.ViewModels;

// Backs the single combined dependency banner row. Hidden = no banner.
// Missing = amber "click to install" (disabled when QA-suppressed).
// Installing = progress bar + per-dep label; the row's X means cancel.
// Success = green confirmation, auto-dismissed after a few seconds.
public enum DependencyBannerState { Hidden, Missing, Installing, Success }

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioService _audio;
    private readonly PlaylistQueue _queue;
    private readonly LibraryDb _db;
    private readonly AnalysisService _analysis;

    public WaveformViewModel WaveformVM { get; }

    private TrackItem? _currentTrack;
    private double _positionRatio;
    private TimeSpan _elapsed;
    private bool _isPlaying;
    private int _currentQueueIndex = -1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrackItem? CurrentTrack
    {
        get => _currentTrack;
        set { _currentTrack = value; OnPropertyChanged(); }
    }

    public double PositionRatio
    {
        get => _positionRatio;
        set { _positionRatio = value; OnPropertyChanged(); }
    }

    public TimeSpan Elapsed
    {
        get => _elapsed;
        set { _elapsed = value; OnPropertyChanged(); }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); }
    }

    public int CurrentQueueIndex
    {
        get => _currentQueueIndex;
        set { _currentQueueIndex = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TrackItem> Queue => _queue.Queue;
    public LibraryDb Db => _db;

    public string PositionDisplay { get; private set; } = "-:-- / -:--";

    public string AnalysisBadge { get; private set; } = "";

    // --- Dependency banner (Feature 2: explicit click-to-install) ----------
    // The banner is a single combined row whose look is derived from
    // BannerState (Hidden/Missing/Installing/Success) via XAML DataTriggers.
    // Cancel-mid-sequence and partial success are NOT a separate state: they
    // collapse back into Missing (recomputed by BuildMissingDependencyNotice,
    // the same source of truth used since Feature 1).
    private DependencyBannerState _bannerState = DependencyBannerState.Hidden;
    private bool _bannerDismissed;              // session-scoped hide of Missing
    private bool _installing;                   // sole double-click guard
    private int _installProgress;
    private string _installLabel = "";
    private CancellationTokenSource? _installCts;
    private DispatcherTimer? _successTimer;

    public DependencyBannerState BannerState
    {
        get => _bannerState;
        private set
        {
            if (_bannerState == value) return;
            _bannerState = value;
            OnPropertyChanged(nameof(BannerState));
            OnPropertyChanged(nameof(DependencyNoticeVisibility));
            // Re-evaluate the install command's CanExecute (enabled only in
            // Missing + installable + not-installing).
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string DependencyNotice { get; private set; } = "";

    // Bound directly (no converter) like CoverArtVisibility. Collapsed when
    // Hidden (covers both "everything present" and "user dismissed Missing").
    public Visibility DependencyNoticeVisibility =>
        BannerState == DependencyBannerState.Hidden
            ? Visibility.Collapsed
            : Visibility.Visible;

    public int InstallProgress
    {
        get => _installProgress;
        private set { _installProgress = value; OnPropertyChanged(); }
    }

    public string InstallLabel
    {
        get => _installLabel;
        private set { _installLabel = value; OnPropertyChanged(); }
    }

    public ImageSource? CoverArtSource { get; private set; }

    public Visibility CoverArtVisibility => CoverArtSource != null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PrevCommand { get; }
    public ICommand PlayTrackAtIndexCommand { get; }
    public ICommand ClearQueueCommand { get; }
    public ICommand RemoveTrackCommand { get; }
    public ICommand ShowInFolderCommand { get; }
    public ICommand InstallDependenciesCommand { get; }
    public ICommand DependencyXCommand { get; }

    public MainViewModel()
    {
        _audio = new AudioService();
        _queue = new PlaylistQueue();
        _db = new LibraryDb();
        WaveformVM = new WaveformViewModel(_audio);
        _analysis = new AnalysisService(_db);
        _analysis.AnalysisComplete += OnAnalysisComplete;

        _audio.PositionChanged += () =>
        {
            PositionRatio = _audio.CurrentPosition;
            Elapsed = _audio.Elapsed;
            WaveformVM.PositionRatio = _audio.CurrentPosition;
            UpdatePositionDisplay();
        };

        _audio.PlaybackEnded += () =>
        {
            IsPlaying = false;
            PlayTrackAtIndex(_queue.CurrentIndex + 1);
        };

        PlayPauseCommand = new RelayCommand(_ => TogglePlayPause());
        StopCommand = new RelayCommand(_ => StopCore());
        NextCommand = new RelayCommand(_ => NextCore());
        PrevCommand = new RelayCommand(_ => PrevCore());
        PlayTrackAtIndexCommand = new RelayCommand(p => PlayTrackAtIndex((int)p!));
        ClearQueueCommand = new RelayCommand(_ => ClearQueue());
        RemoveTrackCommand = new RelayCommand(p => RemoveTrack((TrackItem)p!));
        ShowInFolderCommand = new RelayCommand(p => ShowInFolder((TrackItem)p!));
        // Click-to-install: enabled only in the Missing state, when the
        // service confirms something is installable, and never during an
        // in-flight install (the sole double-click guard).
        InstallDependenciesCommand = new RelayCommand(
            p => { _ = ExecuteInstallAsync(); },
            () => BannerState == DependencyBannerState.Missing
                  && DependencyCheckService.CanInstallMissing
                  && !_installing);
        // The row's X: cancels an in-flight download, otherwise dismisses the
        // banner for the session (Missing) or clears it (Success).
        DependencyXCommand = new RelayCommand(_ => ExecuteDependencyX());
    }

    public void OpenSingleFile(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext != ".mp3" && ext != ".wav" && ext != ".flac" && ext != ".m4a") return;

        var track = BuildTrackItem(path);
        if (track == null) return;

        _queue.Clear();
        _queue.Add(track);
        _queue.SetCurrent(0);
        _ = PlayTrackAsync(track);
    }

    public void HandleFileDrop(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop)) return;
        string[] files = (string[])data.GetData(DataFormats.FileDrop);
        string[] extensions = [".wav", ".mp3", ".flac", ".m4a"];

        bool wasEmpty = _queue.Queue.Count == 0;

        foreach (string file in files)
        {
            string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (!extensions.Contains(ext)) continue;

            TrackItem track = BuildTrackItem(file);
            _queue.Add(track);
        }

        if (wasEmpty && _queue.Queue.Count > 0)
        {
            _queue.ResetIndex();
            _ = PlayTrackAsync(_queue.CurrentTrack!);
        }
    }

    private TrackItem BuildTrackItem(string path)
    {
        var track = new TrackItem
        {
            Path = path,
            Title = System.IO.Path.GetFileNameWithoutExtension(path),
            Artist = string.Empty,
            Format = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
            PlayCount = _db.GetPlayCount(path)
        };

        try
        {
            using var tfile = TagLib.File.Create(path);
            var tag = tfile.Tag;
            track.Title = string.IsNullOrWhiteSpace(tag.Title) ? track.Title : tag.Title;
            track.Artist = string.IsNullOrWhiteSpace(tag.FirstPerformer) ? string.Empty : tag.FirstPerformer;
            track.Duration = tfile.Properties.Duration;

            if (tfile.Properties.AudioSampleRate > 0)
                track.SampleRate = tfile.Properties.AudioSampleRate;

            track.BitDepth = tfile.Properties.BitsPerSample;
            track.Bitrate = tfile.Properties.AudioBitrate;
        }
        catch
        {
            track.Duration = TimeSpan.Zero;
        }

        return track;
    }

    private void TogglePlayPause()
    {
        if (_audio.IsPlaying)
        {
            _audio.Pause();
        }
        else
        {
            if (_audio.IsTrackFinished())
                _audio.Seek(0.0);
            _audio.Play();
        }
        IsPlaying = _audio.IsPlaying;
    }

    private void StopCore()
    {
        _audio.Stop();
        IsPlaying = false;
        PositionRatio = 0;
        Elapsed = TimeSpan.Zero;
        PositionDisplay = "-:-- / -:--";
        OnPropertyChanged(nameof(PositionDisplay));
    }

    private void NextCore()
    {
        var next = _queue.Next();
        if (next != null)
            _ = PlayTrackAsync(next);
    }

    private void PrevCore()
    {
        var prev = _queue.Previous();
        if (prev != null)
            _ = PlayTrackAsync(prev);
    }

    private async Task PlayTrackAsync(TrackItem track)
    {
        CurrentTrack = track;
        CurrentQueueIndex = _queue.CurrentIndex;
        if (_audio.Load(track.Path))
        {
            _audio.Play();
            IsPlaying = true;
            _db.IncrementPlayCount(track.Path);
            track.PlayCount = _db.GetPlayCount(track.Path);

            AnalysisBadge = "";
            OnPropertyChanged(nameof(AnalysisBadge));
            _analysis.Analyze(track.Path);

            var coverArtTask = Task.Run(() =>
            {
                try
                {
                    using var tagFile = TagLib.File.Create(track.Path);
                    var pic = tagFile.Tag.Pictures?.FirstOrDefault();
                    if (pic == null) return null;
                    using var ms = new MemoryStream(pic.Data.Data);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 160;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return (ImageSource?)bmp;
                }
                catch { return null; }
            });

            var waveformTask = WaveformVM.LoadAsync(track.Path);

            var coverArtSource = await coverArtTask;
            CoverArtSource = coverArtSource;
            OnPropertyChanged(nameof(CoverArtSource));
            OnPropertyChanged(nameof(CoverArtVisibility));
            Application.Current.MainWindow.MinWidth = CoverArtSource != null ? 410 : 330;

            await waveformTask;
        }
    }

    public void PlayTrackAtIndex(int index)
    {
        if (index < 0 || index >= _queue.Queue.Count)
        {
            StopCore();
            return;
        }
        _queue.SetCurrent(index);
        _ = PlayTrackAsync(_queue.CurrentTrack!);
    }

    public void ClearQueue()
    {
        _queue.Clear();
        CurrentQueueIndex = -1;
    }

    public void MoveInQueue(int oldIndex, int newIndex)
    {
        _queue.Move(oldIndex, newIndex);
        CurrentQueueIndex = _queue.CurrentIndex;
    }

    public void RemoveTrack(TrackItem item)
    {
        int removedIndex = _queue.Queue.IndexOf(item);
        if (removedIndex < 0) return;

        bool wasCurrent = removedIndex == _queue.CurrentIndex;
        _queue.Remove(item);

        if (_queue.Queue.Count == 0)
        {
            StopCore();
            CurrentTrack = null;
            CurrentQueueIndex = -1;
        }
        else if (wasCurrent)
        {
            PlayTrackAtIndex(Math.Min(removedIndex, _queue.Queue.Count - 1));
        }
        else
        {
            CurrentQueueIndex = _queue.CurrentIndex;
        }
    }

    public void ShowInFolder(TrackItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"")
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void UpdatePositionDisplay()
    {
        if (_currentTrack == null)
        {
            if (PositionDisplay != "-:-- / -:--")
            {
                PositionDisplay = "-:-- / -:--";
                OnPropertyChanged(nameof(PositionDisplay));
            }
            return;
        }
        double pos = _audio.GetPositionSeconds();
        double dur = _audio.GetDurationSeconds();
        string display = $"{FormatTime(pos)} / {FormatTime(dur)}";
        if (PositionDisplay != display)
        {
            PositionDisplay = display;
            OnPropertyChanged(nameof(PositionDisplay));
        }
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0) return "-:--";
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
    }

    public void SetVolume(double ratio) => _audio.SetVolume(ratio);

    private void OnAnalysisComplete(AnalysisResult result)
    {
        string bpmStr = result.Bpm > 0
            ? $"{result.Bpm:F0} BPM" : "";
        string keyStr = result.Key.Length > 0
            ? result.Key : "";
        string lufsStr = result.Lufs != 0
            ? $"{result.Lufs:F1} LUFS" : "";

        var parts = new[] { lufsStr, keyStr, bpmStr }
            .Where(s => s.Length > 0);
        AnalysisBadge = string.Join(" \u00b7 ", parts);
        OnPropertyChanged(nameof(AnalysisBadge));
    }

    // Recompute the banner from the service's current availability. Called at
    // startup and on every AvailabilityChanged. It never fights an active
    // install (the install drives its own Installing->Success/Missing
    // transition) and never force-hides a Success that's still on screen
    // (its auto-dismiss timer / the X handle that).
    public void RefreshDependencyBanner()
    {
        if (BannerState == DependencyBannerState.Installing) return;

        string? notice = DependencyCheckService.BuildMissingDependencyNotice();

        if (string.IsNullOrEmpty(notice))
        {
            if (BannerState != DependencyBannerState.Success)
                BannerState = DependencyBannerState.Hidden;
            return;
        }

        // A previously-dismissed Missing notice stays hidden for the session.
        if (_bannerDismissed)
        {
            BannerState = DependencyBannerState.Hidden;
            return;
        }

        DependencyNotice = notice;
        OnPropertyChanged(nameof(DependencyNotice));
        BannerState = DependencyBannerState.Missing;
    }

    // User clicked the banner: install whatever is missing, sequentially,
    // behind one shared progress bar. _installing is reset in finally on
    // EVERY exit path (success, download-failure, cancel) so the row can
    // never get stuck disabled.
    private async Task ExecuteInstallAsync()
    {
        if (_installing) return;
        _installing = true;
        BannerState = DependencyBannerState.Installing;
        CommandManager.InvalidateRequerySuggested();

        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();

        var progress = new Progress<InstallProgressUpdate>(OnInstallProgress);

        bool allSucceeded;
        bool cancelled = false;
        try
        {
            allSucceeded = await DependencyCheckService.InstallMissingDepsAsync(
                progress, _installCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Only a user cancel (the banner's X) escapes InstallMissingDepsAsync:
            // the download's 15m timeout is swallowed inside and returns false.
            // So this is an intentional abort, not a failure.
            allSucceeded = false;
            cancelled = true;
        }
        finally
        {
            _installing = false;
            _installCts?.Dispose();
            _installCts = null;
            CommandManager.InvalidateRequerySuggested();
        }

        // Full success only if the run succeeded end-to-end AND nothing is
        // still missing (a QA-suppressed dep would keep the notice non-null,
        // so we don't claim success in that case).
        if (allSucceeded
            && DependencyCheckService.BuildMissingDependencyNotice() == null)
        {
            ShowSuccess();
        }
        else if (cancelled)
        {
            // User cancelled: not a failure, and not a re-show. The dep may
            // still be genuinely missing, but the user chose not to install
            // the managed copy this session, so hide the banner for the
            // session (Hidden, not Missing) and don't nag. Next launch's
            // startup probe runs fresh and re-shows it if still missing.
            // Must NOT route through RefreshDependencyBanner(): it early-
            // returns while BannerState == Installing (still the case here,
            // since ExecuteDependencyX only cancels the CTS) - that
            // short-circuit is exactly the stuck-state bug this fixes.
            _bannerDismissed = true;
            BannerState = DependencyBannerState.Hidden;
        }
        else
        {
            // Partial / failure -> recompute the missing state. A dep that
            // finished before the stop has already flipped via
            // AvailabilityChanged, so the notice lists only what's still gone.
            RefreshDependencyBanner();
        }
    }

    private void OnInstallProgress(InstallProgressUpdate update)
    {
        InstallLabel = update.Label;
        InstallProgress = update.Percent;
    }

    private void ShowSuccess()
    {
        DependencyNotice = "Dependencies installed successfully.";
        OnPropertyChanged(nameof(DependencyNotice));
        BannerState = DependencyBannerState.Success;

        _successTimer?.Stop();
        _successTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _successTimer.Tick += (_, _) =>
        {
            _successTimer?.Stop();
            _successTimer = null;
            if (BannerState == DependencyBannerState.Success)
                BannerState = DependencyBannerState.Hidden;
        };
        _successTimer.Start();
    }

    private void ExecuteDependencyX()
    {
        if (BannerState == DependencyBannerState.Installing)
        {
            // Real cancel: aborts the in-flight HttpClient request (the token
            // is honored inside the read/write loop), the partial .tmp is
            // deleted by the download's finally, and the banner reverts to
            // Missing via ExecuteInstallAsync's catch/finally path.
            _installCts?.Cancel();
            return;
        }

        _successTimer?.Stop();
        _successTimer = null;
        if (BannerState == DependencyBannerState.Missing)
            _bannerDismissed = true; // hide for the session
        BannerState = DependencyBannerState.Hidden;
    }

    public void Dispose()
    {
        _audio.Dispose();
        _db.Dispose();
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<bool>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add    { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action<object?> execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}
