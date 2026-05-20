import sys, json, numpy as np, librosa

def run(path):
    try:
        y, sr = librosa.load(path, mono=True)
        y, _  = librosa.effects.trim(y)
        chroma      = librosa.feature.chroma_cens(y=y, sr=sr)
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
        print(json.dumps({"key": f"{notes[bestKey]} {bestMode}"}))
    except Exception as e:
        print(json.dumps({"key": "", "error": str(e)}))

if __name__ == "__main__":
    run(sys.argv[1])
