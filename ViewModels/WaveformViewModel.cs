using mono.Core;

namespace mono.ViewModels;

public sealed class WaveformViewModel
{
    private readonly WaveformService _service;
    private readonly AudioService _audio;
    private Action? _invalidateSurface;
    private CancellationTokenSource _cts = new();

    public float[] Peaks => _service.Peaks;
    public bool PeaksReady => _service.IsReady;

    private double _positionRatio;
    public double PositionRatio
    {
        get => _positionRatio;
        set
        {
            _positionRatio = value;
            _invalidateSurface?.Invoke();
        }
    }

    public WaveformViewModel(AudioService audio)
    {
        _audio = audio;
        _service = new WaveformService();
        _service.PeaksUpdated += () => _invalidateSurface?.Invoke();
    }

    public void SetInvalidateCallback(Action callback) => _invalidateSurface = callback;

    public async Task LoadAsync(string filePath)
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        _service.Reset();
        _invalidateSurface?.Invoke();
        try
        {
            await _service.BuildAsync(filePath, 1800, _cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    public void SeekTo(double ratio)
    {
        _audio.Seek(ratio);
        PositionRatio = ratio;
    }
}
