# mono

Lightweight WPF audio player for music producers. Displays a real-time waveform alongside technical metadata: BPM, musical key, and LUFS loudness. With drag-to-reorder playlist and cover art extraction.

![mono screenshot](https://github.com/user-attachments/assets/9b9cec5a-29ca-4704-8a87-d1eb27e93560)

---

## Features

- **Waveform display** — 1800-peak resolution, rendered via SkiaSharp; click anywhere to seek
- **Automatic analysis** — BPM (essentia), musical key (essentia), and LUFS (ffmpeg ebur128) run in parallel with incremental results
- **Playlist** — drag-to-reorder, context menu (play / remove / show in folder), seamless auto-advance
- **Cover art** — embedded artwork extracted from tags via TagLibSharp; layout adapts when no art is present
- **Persistent cache** — acoustic fingerprint deduplication (Chromaprint) with SQLite storage; analysis results, play counts, and volume persist across sessions and survive file moves
- **Single-instance** — named Mutex + named pipe IPC; a second launch forwards its file path to the running instance and activates the window
- **Drag-and-drop** — drop files onto the window to add them to the playlist
- **Supported formats** — MP3, WAV, FLAC
- **Borderless chrome** — custom title bar, fixed-height layout, minimalist design

---

## Download

Pre-built binaries are available on the [Releases page](https://github.com/simisonne/mono/releases).

Extract the zip and run `mono.exe`. No installer required.

### Requirements

- Windows 10/11 x64
- [ffmpeg](https://ffmpeg.org/download.html) on system PATH — required for LUFS loudness analysis

---

## Building from source

```
git clone https://github.com/simisonne/mono.git
cd mono
dotnet build
```

**Prerequisites:** .NET 8 SDK, Windows 10/11 x64

---

## Third-party licenses

| Component | License |
|---|---|
| **BASS.dll** (Un4seen Developments) | Free for non-commercial use; [commercial license](https://www.un4seen.com) required otherwise |
| **fpcalc / Chromaprint** | LGPL v2.1 |
| **ffmpeg** | LGPL v2.1 / GPL v2 (build-dependent) |

---

## For contributors

<details>
<summary>Stack</summary>

| Layer | Technology |
|---|---|
| UI framework | WPF (.NET 8, C# 12) |
| Audio playback | ManagedBass (BASS.dll) |
| Waveform rendering | SkiaSharp GPU canvas |
| Metadata | TagLibSharp |
| Database | SQLite via Dapper |
| Icons | MahApps.Metro.IconPacks.Lucide |
| BPM analysis | essentia |
| Key analysis | essentia |
| LUFS measurement | ffmpeg ebur128 filter |
| Fingerprinting | fpcalc (Chromaprint) |

</details>

<details>
<summary>Architecture</summary>

MVVM three-tier layout:

```
Views            MainWindow.xaml, WaveformView.xaml, PlaylistDock.xaml
ViewModels       MainViewModel, WaveformViewModel
Services         AudioService, WaveformService, AnalysisService,
                 LufsService, FingerprintService,
                 LibraryDb, PlaylistQueue, FileIconRegistryService
Models           TrackItem
Database         SQLite — %APPDATA%\mono\library.db
```

Single-instance enforcement via `Mutex` (`mono_single_instance_9f3a`) and named pipe IPC (`mono_ipc_9f3a`). A second instance forwards its file path to the primary instance and exits. The primary instance restores its window from minimized state and brings it to the foreground.

Analysis results are keyed by acoustic fingerprint, not file path, so cached data survives file renames and moves.

</details>

<details>
<summary>Project structure</summary>

```
App.xaml.cs                   Entry point — single-instance IPC, file-arg handling
MainWindow.xaml(.cs)          Borderless shell — title bar, track info, waveform row,
                              transport controls, playlist dock
Views/
  WaveformView.xaml(.cs)      SkiaSharp waveform canvas with click-to-seek
  PlaylistDock.xaml(.cs)      ListView playlist with drag-reorder adorner
ViewModels/
  MainViewModel.cs            Central state — playback, queue, analysis, cover art
  WaveformViewModel.cs        Peak data, position tracking, seek commands
Core/
  AudioService.cs             ManagedBass playback engine
  WaveformService.cs          Decode-stream peak extraction (1800 buckets)
  AnalysisService.cs          Orchestrates BPM + key + LUFS in parallel
  LufsService.cs              ffmpeg ebur128 wrapper
  FingerprintService.cs       Chromaprint fpcalc wrapper
  LibraryDb.cs                SQLite persistence (Dapper) — schema migration,
                              fingerprint-based cache, play counts, settings
  PlaylistQueue.cs            ObservableCollection playlist state machine
  FileIconRegistryService.cs  HKCU file-icon registration (one-time)
Converters/                   6 XAML value converters
Models/TrackItem.cs           Track data model
Assets/
  Binaries/                   fpcalc.exe, analyze_bpm.exe, analyze_key.exe
  Fonts/                      Inter, DM Sans (variable)
  Icons/                      Application and file-type icons
  Scripts/                    Python analysis source, venv setup script
```

</details>

<details>
<summary>NuGet packages</summary>

```
ManagedBass                    4.0.2
Dapper                         2.1.72
Microsoft.Data.Sqlite          10.0.7
TagLibSharp                    2.3.0
SkiaSharp.Views.WPF            3.119.2
MahApps.Metro.IconPacks.Lucide 6.2.1
```

</details>
