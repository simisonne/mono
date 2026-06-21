namespace mono.Core;

// Feature 2: single source of truth for the on-demand dependency downloads.
// To re-pin = edit the three fields per dep here (URL + sha256 + archive size),
// nothing else. Both URLs point at IMMUTABLE release artifacts, never a
// floating "latest" alias (BtbN ships a moving "latest" tag that would silently
// break the pin's purpose).
//
// LICENSING (preserved from the Feature 1 rationale):
//  mono is MIT. node/ffmpeg are invoked as SEPARATE PROCESSES at arm's length
//  (argv/stderr) and downloaded ON DEMAND (not redistributed in our release
//  zip), so we avoid GPL *distribution* obligations.
//   * ffmpeg: BtbN LGPL static build. gyan.dev is REJECTED (every gyan ffmpeg
//     build is GPLv3; only its unrelated "tools" package is LGPL). The
//     ebur128/LUFS use case needs no GPL-only codecs, so LGPL keeps mono
//     copyleft-free. DO NOT switch to a GPL/full build without a deliberate
//     decision.
//   * node: standalone win-x64 node.exe; npm/corepack/etc. are discarded.
internal static class DependencyManifest
{
    // --- ffmpeg: BtbN/FFmpeg-Builds, win64-lgpl static ----------------------
    // Tag   autobuild-2026-06-19-23-17  (build N-125119-g4bbb7d9b99)
    // Asset ffmpeg-N-125119-g4bbb7d9b99-win64-lgpl.zip  (NOT -shared, NOT -gpl)
    // sha256 taken from the release's own checksums.sha256 asset.
    public const string FfmpegDownloadUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/" +
        "autobuild-2026-06-19-23-17/" +
        "ffmpeg-N-125119-g4bbb7d9b99-win64-lgpl.zip";
    public const string FfmpegSha256 =
        "c696f0b89bcfaf8b9ae12f4530f11243167050d504ceeed4db6458a861b4e5c8";
    public const long FfmpegArchiveSizeBytes = 145_210_198;
    public const string FfmpegLocalFileName = "ffmpeg.exe";

    // --- node: official Node.js LTS "Krypton", win-x64 standalone ----------
    // Pin   v24.17.0 (Active LTS, a security release) - NOT "latest".
    // We extract only node.exe; npm/corepack/etc. are discarded.
    // sha256 taken from nodejs.org/dist/v24.17.0/SHASUMS256.txt
    public const string NodeDownloadUrl =
        "https://nodejs.org/dist/v24.17.0/node-v24.17.0-win-x64.zip";
    public const string NodeSha256 =
        "f2aa33b35b75aca5f3f7b85675a6f6423201053e9381911e64961f3bda2528ab";
    public const long NodeArchiveSizeBytes = 36_948_900;
    public const string NodeLocalFileName = "node.exe";
}
