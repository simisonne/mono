namespace mono.Models;

public class TrackItem
{
    public string Path { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string Format { get; set; } = string.Empty;
    public int SampleRate { get; set; }
    public int BitDepth { get; set; }
    public int Bitrate { get; set; }
    public int PlayCount { get; set; }
}
