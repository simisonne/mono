import sys, json, numpy as np, madmom

def run(path):
    try:
        proc  = madmom.features.beats.DBNBeatTrackingProcessor(fps=100)
        act   = madmom.features.beats.RNNBeatProcessor()(path)
        beats = proc(act)
        if len(beats) > 1:
            tempo = 60.0 / np.mean(np.diff(beats))
            bpm   = int(round(tempo))
            if bpm <= 84:
                bpm *= 2
            print(json.dumps({"bpm": float(bpm)}))
        else:
            print(json.dumps({"bpm": 0.0}))
    except Exception as e:
        print(json.dumps({"bpm": 0.0, "error": str(e)}))

if __name__ == "__main__":
    run(sys.argv[1])
