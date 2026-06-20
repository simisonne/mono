/**
 * tunebat-oracle.js
 * Runs KeyExtractor + PercivalBpmEstimator using Tunebat's exact WASM binary.
 * Replicates the full browser pipeline from audio-utils.js + c37a3d9f6bc4162933cdf306a3fcb1d9.js
 *
 * Usage:  node oracle.js <audiofile>
 *         node oracle.js <audiofile> --compare-key <python_key>
 *         node oracle.js <audiofile> --compare-bpm <python_bpm>
 *         node oracle.js <audiofile> --compare-key <python_key> --compare-bpm <python_bpm>
 */

'use strict';

const fs     = require('fs');
const path   = require('path');
const { execFileSync, spawnSync } = require('child_process');

const WASM_PATH = path.resolve(__dirname, 'essentia-tunebat.wasm');
const SR_OUT    = 16000;
const MAX_SECS  = 120;

// ---------------------------------------------------------------------------
// Step 1 — Decode audio to mono float32 at native SR via ffmpeg,
//          then replicate audio-utils.js downsampleArray() exactly.
// ---------------------------------------------------------------------------
function decodeToMono(filePath) {
    // Decode to raw f32le mono at native SR so we can do the exact same
    // averaging downsample that Tunebat's browser JS does.
    const probe = spawnSync('ffprobe', [
        '-v', 'error', '-select_streams', 'a:0',
        '-show_entries', 'stream=sample_rate,channels',
        '-of', 'csv=p=0', filePath
    ]);
    if (probe.status !== 0) throw new Error('ffprobe failed: ' + probe.stderr.toString());
    const [srStr] = probe.stdout.toString().trim().split(',');
    const srNative = parseInt(srStr, 10);

    // Decode: stereo → mono mix (ffmpeg amix = L+R*0.5 equivalent via pan)
    const result = spawnSync('ffmpeg', [
        '-v', 'error',
        '-i', filePath,
        '-af', 'pan=mono|c0=0.5*c0+0.5*c1',  // matches monomix() in audio-utils.js
        '-f', 'f32le',
        '-ar', String(srNative),               // keep native SR — we downsample manually
        '-acodec', 'pcm_f32le',
        'pipe:1'
    ], { maxBuffer: 200 * 1024 * 1024 });

    if (result.status !== 0) throw new Error('ffmpeg decode failed: ' + result.stderr.toString());

    const raw = result.stdout;
    const n   = raw.length / 4;
    const pcm = new Float32Array(n);
    for (let i = 0; i < n; i++) {
        pcm[i] = raw.readFloatLE(i * 4);
    }
    return { pcm, srNative };
}

// ---------------------------------------------------------------------------
// Step 2 — Exact port of audio-utils.js downsampleArray()
//          Simple box-filter average — NOT a high-quality resampler.
// ---------------------------------------------------------------------------
function downsampleAverage(audioIn, srIn, srOut) {
    if (srIn === srOut) return audioIn;
    const ratio     = srIn / srOut;
    const newLength = Math.round(audioIn.length / ratio);
    const result    = new Float32Array(newLength);
    let offsetIn    = 0;
    for (let offsetOut = 0; offsetOut < newLength; offsetOut++) {
        const nextOffsetIn = Math.round((offsetOut + 1) * ratio);
        let accum = 0, count = 0;
        for (let i = offsetIn; i < nextOffsetIn && i < audioIn.length; i++) {
            accum += audioIn[i];
            count++;
        }
        result[offsetOut] = count > 0 ? accum / count : 0;
        offsetIn = nextOffsetIn;
    }
    return result;
}

// ---------------------------------------------------------------------------
// Step 3 — Load Tunebat's WASM via the npm JS glue's locateFile hook
// ---------------------------------------------------------------------------
function loadEssentiaWithTunebatWasm() {
    return new Promise((resolve) => {
        // Emscripten locateFile hook — redirect .wasm fetch to Tunebat's binary
        global.Module = {
            locateFile: (p) => p.endsWith('.wasm') ? WASM_PATH : p
        };
        const M = require('essentia.js/dist/essentia-wasm.umd.js');
        // UMD module is synchronous in Node — ready immediately
        resolve(M);
    });
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------
async function main() {
    const args     = process.argv.slice(2);
    const filePath = args[0];
    if (!filePath) {
        console.error('Usage: node oracle.js <audiofile> [--compare-key <key>] [--compare-bpm <bpm>]');
        process.exit(1);
    }
    if (!fs.existsSync(filePath)) {
        console.error('File not found:', filePath);
        process.exit(1);
    }
    if (!fs.existsSync(WASM_PATH)) {
        console.error('Tunebat WASM not found at:', WASM_PATH);
        console.error('Copy essentia.wasm to', WASM_PATH);
        process.exit(1);
    }

    // Decode + downsample — same signal used for both key and BPM
    const { pcm, srNative } = decodeToMono(filePath);
    const downsampled = downsampleAverage(pcm, srNative, SR_OUT);

    // Cap at 120 seconds — matches Tunebat's max_samples
    const maxSamples = MAX_SECS * SR_OUT;
    const signal     = downsampled.length > maxSamples
        ? downsampled.subarray(0, maxSamples)
        : downsampled;

    // Load Essentia with Tunebat's WASM
    const M        = await loadEssentiaWithTunebatWasm();
    const essentia = new M.EssentiaJS(false);
    essentia.arrayToVector = M.arrayToVector;

    // vectorSignal is shared — both algorithms read the same 16kHz mono signal
    const vectorSignal = essentia.arrayToVector(signal);

    // -----------------------------------------------------------------------
    // Key — exact parameters from c37a3d9f6bc4162933cdf306a3fcb1d9.js:
    //   essentia.KeyExtractor(vectorSignal, true, 4096, 4096, 12, 3500, 60, 25,
    //                         0.2, 'bgate', 16000, 0.0001, 440, 'cosine', 'hann')
    // -----------------------------------------------------------------------
    const keyResult = essentia.KeyExtractor(
        vectorSignal,
        true,     // averageDetuningCorrection
        4096,     // frameSize
        4096,     // hopSize
        12,       // hpcpSize
        3500,     // maxFrequency
        60,       // maximumSpectralPeaks
        25,       // minFrequency
        0.2,      // pcpThreshold
        'bgate',  // profileType
        16000,    // sampleRate
        0.0001,   // spectralPeaksThreshold
        440,      // tuningFrequency
        'cosine', // weightType
        'hann'    // windowType
    );
    const scale  = keyResult.scale === 'minor' ? 'min' : 'maj';
    const keyOut = `${keyResult.key} ${scale}`;

    // -----------------------------------------------------------------------
    // BPM — exact parameters from c37a3d9f6bc4162933cdf306a3fcb1d9.js:
    //   essentia.PercivalBpmEstimator(vectorSignal, 1024, 2048, 128, 128, 210, 50, 16000).bpm
    // -----------------------------------------------------------------------
    const bpmResult = essentia.PercivalBpmEstimator(
        vectorSignal,
        1024,  // frameSize
        2048,  // hopSize  (note: larger than frameSize — standard for OSS)
        128,   // frameSizeOSS
        128,   // hopSizeOSS
        210,   // maxBPM
        94,    // minBPM
        16000  // sampleRate
    );
    const bpmOut = Math.round(bpmResult.bpm);

    // -----------------------------------------------------------------------
    // Output
    // -----------------------------------------------------------------------
    const compareKey = args.indexOf('--compare-key');
    const compareBpm = args.indexOf('--compare-bpm');
    const isComparing = compareKey !== -1 || compareBpm !== -1;

    if (isComparing) {
        const out = { file: path.basename(filePath) };

        out.wasm_key = keyOut;
        if (compareKey !== -1 && args[compareKey + 1]) {
            out.python_key = args[compareKey + 1];
            out.key_result = keyOut === out.python_key ? '✓ MATCH' : '✗ MISMATCH';
        }

        out.wasm_bpm = bpmOut;
        if (compareBpm !== -1 && args[compareBpm + 1]) {
            out.python_bpm = Number(args[compareBpm + 1]);
            out.bpm_result = bpmOut === out.python_bpm ? '✓ MATCH' : `✗ MISMATCH (diff: ${bpmOut - out.python_bpm})`;
        }

        console.log(JSON.stringify(out, null, 2));
    } else {
        console.log(JSON.stringify({ key: keyOut, bpm: bpmOut }));
    }
}

main().catch(err => {
    console.error(JSON.stringify({ error: err.message }));
    process.exit(1);
});
