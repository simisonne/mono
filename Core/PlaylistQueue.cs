using System.Collections.ObjectModel;
using mono.Models;

namespace mono.Core;

public class PlaylistQueue
{
    public ObservableCollection<TrackItem> Queue { get; } = new();
    public int CurrentIndex { get; private set; } = -1;

    public TrackItem? CurrentTrack =>
        CurrentIndex >= 0 && CurrentIndex < Queue.Count
            ? Queue[CurrentIndex]
            : null;

    public void Add(TrackItem track)
    {
        Queue.Add(track);
    }

    public void Clear()
    {
        Queue.Clear();
        CurrentIndex = -1;
    }

    public TrackItem? Next()
    {
        if (Queue.Count == 0) return null;
        CurrentIndex = CurrentIndex + 1 < Queue.Count ? CurrentIndex + 1 : 0;
        return CurrentTrack;
    }

    public TrackItem? Previous()
    {
        if (Queue.Count == 0) return null;
        CurrentIndex = CurrentIndex - 1 >= 0 ? CurrentIndex - 1 : Queue.Count - 1;
        return CurrentTrack;
    }

    public void Remove(TrackItem item)
    {
        int idx = Queue.IndexOf(item);
        if (idx < 0) return;
        Queue.RemoveAt(idx);
        if (Queue.Count == 0)
            CurrentIndex = -1;
        else if (idx < CurrentIndex)
            CurrentIndex--;
        else if (idx == CurrentIndex)
            CurrentIndex = Math.Min(CurrentIndex, Queue.Count - 1);
    }

    public void SetCurrent(int index)
    {
        if (index >= 0 && index < Queue.Count)
            CurrentIndex = index;
    }

    public void ResetIndex()
    {
        CurrentIndex = Queue.Count > 0 ? 0 : -1;
    }

    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex) return;
        if (oldIndex < 0 || oldIndex >= Queue.Count) return;
        if (newIndex < 0 || newIndex >= Queue.Count) return;
        Queue.Move(oldIndex, newIndex);
        if (CurrentIndex == oldIndex) CurrentIndex = newIndex;
        else if (oldIndex < CurrentIndex && newIndex >= CurrentIndex) CurrentIndex--;
        else if (oldIndex > CurrentIndex && newIndex <= CurrentIndex) CurrentIndex++;
    }
}
