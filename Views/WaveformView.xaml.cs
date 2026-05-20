using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using mono.ViewModels;

namespace mono.Views;

public partial class WaveformView : UserControl
{
    private readonly SKPaint _unplayedPaint = new()
    {
        Color = SKColor.Parse("#c8c8c8"),
        IsAntialias = true
    };
    private readonly SKPaint _playedPaint = new()
    {
        Color = SKColor.Parse("#7c3aed"),
        IsAntialias = true
    };
    private readonly SKPaint _emptyLinePaint = new()
    {
        Color = SKColor.Parse("#c8c8c8"),
        StrokeWidth = 1,
        IsAntialias = true
    };

    public WaveformView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is WaveformViewModel vm)
            vm.SetInvalidateCallback(() => WaveCanvas.InvalidateVisual());
    }

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(SKColor.Parse("#f2f2f2"));

        var vm = DataContext as WaveformViewModel;
        var peaks = vm?.Peaks;

        if (peaks == null || peaks.Length == 0)
        {
            canvas.DrawLine(0, info.Height / 2f, info.Width, info.Height / 2f, _emptyLinePaint);
            return;
        }

        float width = info.Width;
        float height = info.Height;
        float centerY = height / 2f;
        int peakCount = peaks.Length;
        float strokeWidth = Math.Max(1f, width / peakCount * 0.7f);

        _unplayedPaint.StrokeWidth = strokeWidth;
        _playedPaint.StrokeWidth = strokeWidth;

        float playedWidth = (float)(vm!.PositionRatio) * width;

        for (int i = 0; i < peakCount; i++)
        {
            float x = (i / (float)peakCount) * width;
            float barH = peaks[i] * centerY * 0.92f;
            var paint = x < playedWidth ? _playedPaint : _unplayedPaint;
            canvas.DrawLine(x, centerY - barH, x, centerY + barH, paint);
        }
    }

    private void OnSeekClick(object sender, MouseButtonEventArgs e)
    {
        var vm = DataContext as WaveformViewModel;
        if (vm == null) return;
        double clickRatio = e.GetPosition(WaveCanvas).X / WaveCanvas.ActualWidth;
        vm.SeekTo(clickRatio);
    }
}
