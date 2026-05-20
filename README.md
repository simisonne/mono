# mono

Lightweight, waveform-first, producer-centric Windows desktop audio player.

Status: active development (mid-alpha)

## Requirements

### Running the app (download the release)
- Windows 10/11 x64
- ffmpeg on your system PATH — https://ffmpeg.org/download.html

### Building from source
- .NET 8 SDK
- ffmpeg on PATH (same as above)
- Python 3.9 — run `Assets/Scripts/setup_venv.bat` once after cloning

## Stack

| Layer | Technology | Reason |
|---|---|---|
| UI framework | WPF (.NET 8, C#) | Native Windows, best BASS.dll integration |
| Audio engine | ManagedBass (BASS.dll wrapper) | Battle-tested, low latency |
| Waveform rendering | SkiaSharp.Views.WPF (GPU canvas) | Direct2D-backed, manual invalidation |
| Metadata parsing | TagLibSharp | ID3, FLAC, WAV tag extraction + cover art |
| Database | SQLite via Dapper | Play counts, analysis cache, settings |
| Icons | MahApps.Metro.IconPacks.Lucide | Consistent Lucide icon set |
| Typography | Inter + DM Sans (variable fonts) | Loaded from bundled TTF resources |
| Analysis — BPM | Python sidecar (madmom via venv) | `analyze_bpm.py` → JSON `{ "bpm": ... }` |
| Analysis — Key | Python sidecar (via venv) | `analyze_key.py` → JSON `{ "key": ... }` |
| Analysis — LUFS | ffmpeg ebur128 filter | `ffmpeg -i ... -filter:a ebur128 -f null -` |
| Fingerprinting | fpcalc (Chromaprint) | Acoustic fingerprint for cache dedup |
| Target OS | Windows only | No cross-platform plans at this stage |

## Third-party licenses

- **BASS.dll** (audio engine by Un4seen) — free for non-commercial use.
  Commercial use requires a license: https://www.un4seen.com
- **fpcalc / Chromaprint** — LGPL v2.1
- **ffmpeg** — LGPL v2.1 / GPL v2 depending on build

## Architecture overview

Three-tier:

```
UI Layer       -> MainWindow, WaveformView, PlaylistDock (WPF UserControls)
Services Layer -> AudioService, WaveformService, AnalysisService, LufsService,
                  FingerprintService, PythonSidecarService, LibraryDb
Data Layer     -> in-memory PlaylistQueue (ObservableCollection) + SQLite store
```

Single-instance enforcement via Mutex + named pipe IPC. Second instance forwards file path to the primary instance and exits.

## Project structure

```
mono/
├── App.xaml / App.xaml.cs                 # Color tokens, converters, fonts; single-instance IPC, global error handler
├── MainWindow.xaml / .xaml.cs             # Shell: title bar, cover art placeholder, waveform, transport, volume, playlist toggle
├── Core/
│   ├── AudioService.cs                    # BASS.dll wrapper: Init, Load, Play, Pause, Stop, Seek, SetVolume
│   ├── WaveformService.cs                 # Async peak buffer decode (separate BASS decode channel)
│   ├── PlaylistQueue.cs                   # ObservableCollection<TrackItem>, CurrentIndex, Next/Previous
│   ├── LibraryDb.cs                       # SQLite: tracks table (analysis cache, play counts), settings table, schema migration
│   ├── AnalysisService.cs                 # Orchestrates BPM + Key + LUFS in parallel, progressive partial results
│   ├── FingerprintService.cs              # fpcalc.exe → Chromaprint fingerprint
│   ├── LufsService.cs                     # ffmpeg ebur128 integrated loudness measurement
│   └── PythonSidecarService.cs            # Manages Python venv lifecycle, runs analyze_bpm.py / analyze_key.py
├── Models/
│   └── TrackItem.cs                       # Path, Title, Artist, Duration, Format, SampleRate, BitDepth, Bitrate, PlayCount
├── ViewModels/
│   ├── MainViewModel.cs                   # Owns AudioService, PlaylistQueue, WaveformViewModel, AnalysisService
│   └── WaveformViewModel.cs               # Owns WaveformService, PositionRatio, LoadAsync, SeekTo
├── Views/
│   ├── WaveformView.xaml/.xaml.cs         # SKElement canvas, OnPaintSurface, click-to-seek
│   └── PlaylistDock.xaml/.xaml.cs         # Toggleable panel, ListView, context menus (Play / Remove / Show in Folder)
├── Converters/
│   ├── FormatBadgeConverter.cs            # Format + sample rate + bit depth + bitrate → badge string
│   ├── TitleDisplayConverter.cs           # Artist + Title → display string
│   ├── SampleRateConverter.cs             # Sample rate → display format
│   ├── StringToVisibilityConverter.cs     # Non-empty string → Visible
│   ├── PositionToOffsetConverter.cs       # Position ratio → pixel offset
│   └── ItemIndexConverter.cs              # ListViewItem → 1-based index
└── Assets/
    ├── Binaries/
    │   ├── fpcalc.exe                     # Chromaprint fingerprinting binary
    │   └── keyfinder-cli-1.2.0/           # KeyFinder CLI (bundled, reserved for future use)
    ├── Fonts/
    │   ├── Inter-VariableFont_opsz,wght.ttf
    │   ├── Inter-Italic-VariableFont_opsz,wght.ttf
    │   ├── DMSans-VariableFont_opsz,wght.ttf
    │   └── DMSans-Italic-VariableFont_opsz,wght.ttf
    ├── Icons/
    │   └── mono.ico                       # Application icon
    └── Scripts/
        ├── analyze_bpm.py                 # BPM analysis (madmom)
        ├── analyze_key.py                 # Musical key analysis
        ├── analyze.py                     # Combined analysis (reserved)
        ├── setup_venv.bat                 # Auto-setup Python venv with dependencies
        └── venv/                          # Python virtual environment (gitignored)
```

## Color tokens

Defined in `App.xaml` as `SolidColorBrush` resources.

| Token | Hex | Usage |
|---|---|---|
| Background | `#f2f2f2` | Default window background |
| Surface | `#ffffff` | Title bar, track info row |
| SurfaceAlt | `#e4e4e4` | Panels, waveform bg, hover states |
| Border | `#cccccc` | Borders, dividers, slider track |
| TextPrimary | `#1c1c1c` | Near-black — titles, icons |
| TextSecondary | `#666666` | Muted gray — metadata, timestamps |
| Accent | `#7c3aed` | Deep violet — playhead, played waveform, active track, buttons |
| WaveformUnplayed | `#c8c8c8` | Muted — unplayed waveform bars |
| WaveformPlayed | `#7c3aed` | Same as Accent — swept portion |
| Needle | `#1c1c1c` | Hard black playhead line |

## Waveform rendering

- `WaveformService` decodes using a **separate** BASS decode channel (`BassFlags.Decode | BassFlags.Float`). This channel never touches playback. It is `StreamFree()`'d after `BuildAsync` completes.
- Resolution: 1800 peak buckets per track.
- Fires `PeaksUpdated` every 10% of decode progress (progressive rendering).
- `InvalidateSurface` called manually via callback — no render loop.
- `InvalidateSurface` must always be called from the UI thread (marshal via `Dispatcher` if needed).

## Audio engine

- `Bass.Init()` called once before any `Load()` call.
- Channel handle (`_handle`) checked: if `0`, playback calls are no-ops.
- `Seek(double ratio)` converts ratio to byte offset via `ChannelSeconds2Bytes`.
- `SetVolume(double ratio)` sets per-channel volume via `ChannelSetAttribute`.
- `IsTrackFinished()` returns `true` when position reaches 1.0 (end of track).
- Supported formats (Phase 1): WAV, MP3, FLAC via built-in BASS decoders.
- No BASS plugins loaded yet.

## Analysis pipeline

- `AnalysisService` orchestrates BPM, musical key, and LUFS measurement in **parallel** when a track is loaded.
- **Fingerprinting**: `FingerprintService` runs `fpcalc.exe` (Chromaprint) to generate an acoustic fingerprint.
- **Cache lookup**: `LibraryDb.GetAnalysis()` checks for cached results by path, then by fingerprint (handles moved/renamed files).
- **BPM**: `PythonSidecarService` runs `analyze_bpm.py` in a managed venv. Returns JSON `{ "bpm": <float> }`.
- **Key**: `PythonSidecarService` runs `analyze_key.py` in the same venv. Returns JSON `{ "key": "<string>" }`.
- **LUFS**: `LufsService` shells out to `ffmpeg -filter:a ebur128` and parses the integrated loudness from stderr.
- **Progressive updates**: Each analyzer fires `AnalysisComplete` independently as it finishes, so the UI badge updates incrementally.
- Results persisted to SQLite (`tracks.bpm`, `tracks.musicalKey`, `tracks.lufs`, `tracks.fingerprint`, `tracks.analysisVersion`).
- Analysis is cancelled and restarted when a new track is loaded.

## Playlist rules

- `PlaylistQueue` is the single source of truth for queue state.
- Active queue: in-memory `ObservableCollection<TrackItem>`.
- Drag-and-drop **appends** to existing queue — does not replace.
- If queue was empty before drop, auto-play first dropped track.
- If queue had tracks, append only — do not interrupt current playback.
- Auto-advance: `PlaybackEnded` event fires `PlayTrackAtIndex(next)`.
- Next/Previous wrap around (circular navigation).
- Index numbers in UI re-number after removal.

## Database (SQLite)

- Location: `%AppData%/mono/library.db`
- **Table: `tracks`** — `path TEXT PRIMARY KEY`, `playCount INTEGER DEFAULT 0`, `lastPlayed TEXT`, `fingerprint TEXT`, `bpm REAL DEFAULT 0`, `musicalKey TEXT DEFAULT ''`, `lufs REAL DEFAULT 0`, `analysisVersion INTEGER DEFAULT 0`
- **Table: `settings`** — `key TEXT PRIMARY KEY`, `value TEXT NOT NULL`
- Schema migrations run via `MigrateSchema()` on startup (ALTER TABLE for new columns).
- Dapper for all queries — no Entity Framework.
- Play count increments on every `PlayTrack` call.
- Volume persisted in settings table.

## Single-instance & IPC

- `Mutex` (`mono_single_instance_9f3a`) prevents multiple instances.
- Named pipe server (`mono_ipc_9f3a`) runs in background.
- Second instance sends file path via pipe, primary instance activates window and opens the file.
- `FlashWindowEx` used to stop any active taskbar flashing when restoring.

## UI/UX rules

- Window: `WindowChrome`-based chrome (not `WindowStyle=None`), `AllowsTransparency=False`, thin black border.
- Window drag: `MouseLeftButtonDown` on title row calls `DragMove()`.
- Title bar: Lucide `AudioWaveform` icon + "mono" label (DM Sans SemiBold), minimize and close buttons.
- Close button: Lucide X icon, calls `Application.Current.Shutdown()`.
- Minimize button: Lucide Minus icon, sets `WindowState.Minimized`.
- `ResizeMode="CanResize"` — `WM_SIZING` hook prevents vertical resize (height locked), enforces min width 330px.
- Transport buttons: Lucide icons (SkipBack, Play/Pause, Square, SkipForward), 32px bordered buttons with hover/pressed states.
- Volume: Lucide Volume2/VolumeX icon + flat slider (0–1), click-to-mute, persisted to database.
- Playlist button: Lucide List icon with count badge (appears when queue ≥ 2 tracks).
- Playlist dock: toggled via button, `MaxHeight=220`, `Visibility=Collapsed` when hidden.
- Format badge: `"WAV · 44.1kHz · 24bit"` style in Consolas 10px.
- Analysis badge: `"−14.2 LUFS · Am · 128 BPM"` displayed in track info row with tooltip.
- Active playlist track: `Background=#ede9fb`, index replaced with `▶` in Accent color.
- Context menu: Play / Remove from list / (separator) / Show in Folder.

## Completed sessions

### Session 1 — Project scaffold
`AudioService`, `PlaylistQueue`, `LibraryDb`, `TrackItem`, `MainViewModel`, `MainWindow` shell with drag-and-drop and transport controls.

### Session 2 — SkiaSharp waveform
`WaveformService`, `WaveformViewModel`, `WaveformView`, `Seek` wired, progressive peak rendering.

### Session 3 — Bug fixes
Window width reduced to 440px, drag-and-drop play fixed, transport button padding fixed, close button added.

### Session 4 — Playlist dock
`PlaylistDock` UserControl, double-click to play, right-click context menu (Play / Remove / Show in Folder), auto-advance, append-on-drop, metadata wired to track info row.

### Session 5 — Analysis pipeline
`AnalysisService`, `FingerprintService`, `LufsService`, `PythonSidecarService` — parallel BPM + Key + LUFS analysis with progressive UI updates. Acoustic fingerprinting for cache dedup. Analysis badge in track info row. `LibraryDb` schema extended with fingerprint, bpm, musicalKey, lufs columns.

### Session 6 — UI refinement & single instance
Replaced borderless window with `WindowChrome`. Added minimize button. Volume control with mute toggle and persistence. Playlist badge counter. Lucide icon set (MahApps.Metro.IconPacks.Lucide). Custom fonts (Inter + DM Sans). Cover art placeholder column. Single-instance enforcement via Mutex + named pipe IPC with "Open With" file forwarding. `WM_SIZING` hook for height-lock. Value converters for format badge, title display, sample rate.

## Upcoming

- Cover art display in track info row (from TagLib embedded image)
- Play count display in playlist rows
- Playlist export: `.m3u` and `.pls`
- "Open With" Windows Explorer registry entry (command-line arg handling exists, needs registry integration)

## NuGet packages

```
ManagedBass              4.0.2
Dapper                   2.1.72
Microsoft.Data.Sqlite   10.0.7
TagLibSharp              2.3.0
SkiaSharp.Views.WPF      3.119.2
MahApps.Metro.IconPacks.Lucide  6.2.1
OpenTK                   3.3.1       (NU1701 — cosmetic, no runtime effect)
OpenTK.GLWpfControl      3.3.0       (NU1701 — cosmetic, no runtime effect)
```

## Known warnings (non-blocking)

`NU1701` — OpenTK, OpenTK.GLWpfControl, SkiaSharp.Views.WPF restored against .NETFramework fallback targets instead of `net8.0-windows`. These are cosmetic warnings and do not affect runtime behavior. Do not attempt to suppress or fix without a concrete reason.

## Runtime dependencies

The following must be available at runtime (bundled in `Assets/Binaries/` or expected on PATH):

| Dependency | Purpose | Bundled |
|---|---|---|
| `bass.dll` | Audio playback engine | Yes (project root) |
| `Assets/Binaries/fpcalc.exe` | Chromaprint acoustic fingerprinting | Yes |
| `ffmpeg` | LUFS measurement via ebur128 filter | No — must be on PATH |
| Python 3.x | BPM and key analysis sidecar (via venv) | No — must be installed for `setup_venv.bat` |
