using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using mono.Core;
using mono.Models;

namespace mono.ViewModels;

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
    }

    public void OpenSingleFile(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext != ".mp3" && ext != ".wav" && ext != ".flac") return;

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
        string[] extensions = [".wav", ".mp3", ".flac"];

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

    public void Dispose()
    {
        _audio.Dispose();
        _db.Dispose();
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public event EventHandler? CanExecuteChanged
        {
            add    { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
        public RelayCommand(Action<object?> execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}
