using System.Windows;
using ManagedBass;

namespace mono.Core;

public class WaveformService
{
    public float[] Peaks { get; private set; } = Array.Empty<float>();
    public bool IsReady { get; private set; }
    public event Action? PeaksUpdated;

    public async Task BuildAsync(string filePath, int resolution = 1800, CancellationToken ct = default)
    {
        IsReady = false;

        await Task.Run(() =>
        {
            int decodeHandle = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
            if (decodeHandle == 0) return;

            try
            {
                long lengthBytes = Bass.ChannelGetLength(decodeHandle);
                long totalSamples = lengthBytes / 4;

                if (totalSamples <= 0) return;

                double bucketSize = (double)totalSamples / resolution;
                float[] peaks = new float[resolution];

                float[] buffer = new float[4096];
                long samplesProcessed = 0;
                int lastProgressPercent = 0;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    int bytesRead = Bass.ChannelGetData(decodeHandle, buffer, 4096 * 4);
                    if (bytesRead <= 0) break;

                    int floatsRead = bytesRead / 4;

                    for (int i = 0; i < floatsRead; i++)
                    {
                        float absVal = Math.Abs(buffer[i]);
                        int bucket = (int)(samplesProcessed / bucketSize);
                        if (bucket >= resolution) bucket = resolution - 1;
                        if (absVal > peaks[bucket])
                            peaks[bucket] = absVal;
                        samplesProcessed++;
                    }

                    int progressPercent = (int)(samplesProcessed * 100 / totalSamples);
                    if (progressPercent >= lastProgressPercent + 10)
                    {
                        lastProgressPercent = progressPercent;
                        Peaks = peaks;
                        Application.Current.Dispatcher.InvokeAsync(() => PeaksUpdated?.Invoke());
                    }
                }

                Peaks = peaks;
                IsReady = true;
                Application.Current.Dispatcher.InvokeAsync(() => PeaksUpdated?.Invoke());
            }
            finally
            {
                Bass.StreamFree(decodeHandle);
            }
        }, ct);
    }

    public void Reset()
    {
        Peaks = Array.Empty<float>();
        IsReady = false;
    }
}
