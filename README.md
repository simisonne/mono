# mono

Lightweight audio player for music producers that shows the waveform and other info about the files played.
Currently shows LUFS, Key, and BPM.

![mono screenshot](https://github.com/user-attachments/assets/9b9cec5a-29ca-4704-8a87-d1eb27e93560)

---

## Features

- Waveform display with click to skip
- Automatic BPM, musical key, and LUFS analysis
- Playlist with drag to reorder and seamless transitions
- Embedded cover art display (or just white space if no cover art)
- Play count tracking and analysis caching across sessions
- Supports MP3, WAV, and FLAC
- Nice small window, lightweight, minimalist

---

## Download

Find the latest release from the [Releases page](https://github.com/simisonne/mono/releases).

Extract the zip and run `mono.exe`. No installer needed.

### Requirements

- Windows 10/11 x64
- [ffmpeg](https://ffmpeg.org/download.html) on system PATH needed for loudness (LUFS) analysis
- For BPM and key analysis: run `Assets/Scripts/setup_venv.bat` once after extracting. This will sets up a local Python environment automatically. Python 3.9 must be installed, due to the bpm and key detection (still working on that..)

> BPM and key analysis are optional. The player works without them, it just won't show the badges.

---

## Building from source

```
git clone https://github.com/simisonne/mono.git
cd mono
dotnet build
```

Then run `Assets/Scripts/setup_venv.bat` once for BPM and key analysis support.

**Requirements:** .NET 8 SDK, ffmpeg on PATH, Python 3.9

---

## Third-party licenses

- **BASS.dll** — audio engine by Un4seen. Free for non-commercial use. Commercial use requires a license: https://www.un4seen.com
- **fpcalc / Chromaprint** — LGPL v2.1
- **ffmpeg** — LGPL v2.1 / GPL v2 depending on build

---

## For contributors

<details>
<summary>Stack</summary>

| Layer | Technology |
|---|---|
| UI | WPF (.NET 8, C#) |
| Audio | ManagedBass (BASS.dll) |
| Waveform | SkiaSharp GPU canvas |
| Metadata | TagLibSharp |
| Database | SQLite via Dapper |
| Icons | MahApps.Metro.IconPacks.Lucide |
| BPM analysis | Python sidecar — madmom DBN |
| Key analysis | Python sidecar — librosa chroma_cens |
| LUFS | ffmpeg ebur128 filter |
| Fingerprinting | fpcalc (Chromaprint) |

</details>

<details>
<summary>Architecture</summary>

Three-tier layout:

```
UI Layer       → MainWindow, WaveformView, PlaylistDock
Services Layer → AudioService, WaveformService, AnalysisService,
                 LufsService, FingerprintService, PythonSidecarService, LibraryDb
Data Layer     → PlaylistQueue (ObservableCollection) + SQLite
```

Single-instance enforcement via Mutex + named pipe IPC. A second instance forwards its file path to the running instance and exits.

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

---

*mono is in active development. it might break idk*