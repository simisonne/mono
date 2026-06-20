using System.IO;
using System.Windows.Threading;
using ManagedBass;

namespace mono.Core;

public sealed class AudioService : IDisposable
{
    private static readonly string LogPath = Path.Combine(
        @"C:\Users\Maild\Documents\Coding\mono media player\test", "mono_debug.log");

    private int _handle;
    private readonly DispatcherTimer _timer;
    private double _volume = 1.0;

    public double CurrentPosition { get; private set; }
    public TimeSpan Duration { get; private set; }
    public TimeSpan Elapsed { get; private set; }
    public bool IsPlaying { get; private set; }

    public event Action? PositionChanged;
    public event Action? PlaybackEnded;

    private bool _initialized;

    private static void Log(string msg)
    {
        File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
    }

    public AudioService()
    {
        Log("=== AudioService created ===");
        try
        {
            _initialized = Bass.Init();
            Log($"Init result: {_initialized}, Error: {Bass.LastError}");
        }
        catch (Exception ex)
        {
            Log($"Init exception: {ex.Message}");
            _initialized = false;
        }
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTimerTick;
    }

    public bool Load(string path)
    {
        Log($"Load called: {path}");
        if (!_initialized)
        {
            Log("Load aborted — not initialized");
            return false;
        }
        if (_handle != 0)
        {
            _timer.Stop();
            IsPlaying = false;
            Bass.StreamFree(_handle);
            _handle = 0;
        }
        _handle = Bass.CreateStream(path);
        Log($"CreateStream handle: {_handle}, Error: {Bass.LastError}");
        if (_handle == 0)
            return false;

        Bass.ChannelSetAttribute(_handle, ChannelAttribute.Volume, _volume);

        long lengthBytes = Bass.ChannelGetLength(_handle);
        double lengthSeconds = Bass.ChannelBytes2Seconds(_handle, lengthBytes);
        Duration = TimeSpan.FromSeconds(lengthSeconds);
        CurrentPosition = 0;
        Elapsed = TimeSpan.Zero;
        Log($"Duration: {Duration}");
        return true;
    }

    public void Play()
    {
        Log($"Play called — handle: {_handle}, initialized: {_initialized}");
        if (_handle == 0 || !_initialized) return;
        bool result = Bass.ChannelPlay(_handle);
        Log($"ChannelPlay result: {result}, Error: {Bass.LastError}");
        IsPlaying = true;
        _timer.Start();
    }

    public void Pause()
    {
        if (_handle == 0) return;
        Bass.ChannelPause(_handle);
        IsPlaying = false;
        _timer.Stop();
    }

    public void Stop()
    {
        if (_handle == 0) return;
        Bass.ChannelStop(_handle);
        Bass.ChannelSetPosition(_handle, 0, PositionFlags.Bytes);
        IsPlaying = false;
        CurrentPosition = 0;
        Elapsed = TimeSpan.Zero;
        _timer.Stop();
        PositionChanged?.Invoke();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_handle == 0) return;

        long posBytes = Bass.ChannelGetPosition(_handle);
        double posSeconds = Bass.ChannelBytes2Seconds(_handle, posBytes);
        double totalSeconds = Duration.TotalSeconds;
        CurrentPosition = totalSeconds > 0 ? posSeconds / totalSeconds : 0;
        Elapsed = TimeSpan.FromSeconds(posSeconds);
        PositionChanged?.Invoke();

        if (CurrentPosition >= 1.0)
        {
            IsPlaying = false;
            _timer.Stop();
            PlaybackEnded?.Invoke();
        }
    }

    public void Seek(double ratio)
    {
        if (_handle == 0) return;
        long lengthBytes = Bass.ChannelGetLength(_handle);
        double lengthSeconds = Bass.ChannelBytes2Seconds(_handle, lengthBytes);
        long targetBytes = Bass.ChannelSeconds2Bytes(_handle, ratio * lengthSeconds);
        Bass.ChannelSetPosition(_handle, targetBytes);
    }

    public void SetVolume(double ratio)
    {
        _volume = ratio;
        if (_handle != 0)
            Bass.ChannelSetAttribute(_handle, ChannelAttribute.Volume, ratio);
    }

    public double GetPositionSeconds()
    {
        if (_handle == 0) return 0;
        long posBytes = Bass.ChannelGetPosition(_handle);
        return Bass.ChannelBytes2Seconds(_handle, posBytes);
    }

    public double GetDurationSeconds()
    {
        if (_handle == 0) return 0;
        long lengthBytes = Bass.ChannelGetLength(_handle);
        return Bass.ChannelBytes2Seconds(_handle, lengthBytes);
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_handle != 0)
        {
            Bass.StreamFree(_handle);
            _handle = 0;
        }
        if (_initialized)
            Bass.Free();
    }

    public bool IsTrackFinished()
    {
        return _handle != 0 && Bass.ChannelIsActive(_handle) == PlaybackState.Stopped;
    }
}
