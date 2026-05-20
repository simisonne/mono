import sys
import json
import numpy as np
import librosa
import madmom

def analyze(path):
    result = {"bpm": 0.0, "key": ""}

    try:
        proc = madmom.features.beats.DBNBeatTrackingProcessor(fps=100)
        act  = madmom.features.beats.RNNBeatProcessor()(path)
        beats = proc(act)
        if len(beats) > 1:
            tempo = 60.0 / np.mean(np.diff(beats))
            bpm = int(round(tempo))
            if bpm <= 84:
                bpm = bpm * 2
            result["bpm"] = float(bpm)
    except Exception as e:
        result["bpm_error"] = str(e)

    try:
        y, sr = librosa.load(path, mono=True)
        y, _ = librosa.effects.trim(y)
        chroma = librosa.feature.chroma_cens(y=y, sr=sr)
        chroma_mean = np.mean(chroma, axis=1)

        notes = ['C','Db','D','Eb','E','F','F#','G','Ab','A','Bb','B']
        major = [6.35,2.23,3.48,2.33,4.38,4.09,2.52,5.19,2.39,3.66,2.29,2.88]
        minor = [6.33,2.68,3.52,5.38,2.60,3.53,2.54,4.75,3.98,2.69,3.34,3.17]

        best, bestKey, bestMode = -999.0, 0, 'maj'
        for i in range(12):
            sm  = float(np.corrcoef(chroma_mean, np.roll(major, i))[0,1])
            smn = float(np.corrcoef(chroma_mean, np.roll(minor, i))[0,1])
            if sm  > best: best, bestKey, bestMode = sm,  i, 'maj'
            if smn > best: best, bestKey, bestMode = smn, i, 'min'

        result["key"] = f"{notes[bestKey]} {bestMode}"
    except Exception as e:
        result["key_error"] = str(e)

    print(json.dumps(result))

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(json.dumps({"error": "no path provided"}))
        sys.exit(1)
    analyze(sys.argv[1])
