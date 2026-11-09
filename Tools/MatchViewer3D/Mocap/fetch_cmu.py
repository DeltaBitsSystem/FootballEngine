"""Fetch the selected CMU trials and pin them by SHA-256.

The raw data stays out of git (it is large and only an input), so reproducibility comes from the selection list
plus the manifest: the first fetch records each file's hash, every later fetch must reproduce it exactly or fail.
Usage: python3 -I fetch_cmu.py [--verify-only]
"""

import hashlib
import json
import pathlib
import sys
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent
SOURCE = ROOT / "source"
MANIFEST = ROOT / "cmu-manifest.json"
BASE = "http://mocap.cs.cmu.edu/subjects"


def selection():
    trials = []
    for line in (ROOT / "cmu-selection.txt").read_text().splitlines():
        if not line.startswith("#"):
            trials.extend(line.split())
    return sorted(set(trials))


def files_for(trials):
    subjects = sorted({trial.split("_")[0] for trial in trials})
    return [f"{subject}/{subject}.asf" for subject in subjects] + [
        f"{trial.split('_')[0]}/{trial}.amc" for trial in trials
    ]


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    verify_only = "--verify-only" in sys.argv
    pinned = json.loads(MANIFEST.read_text())["files"] if MANIFEST.exists() else {}
    recorded = {}

    for relative in files_for(selection()):
        target = SOURCE / relative
        if not target.exists():
            if verify_only:
                raise SystemExit(f"missing {relative}")
            target.parent.mkdir(parents=True, exist_ok=True)
            with urllib.request.urlopen(f"{BASE}/{relative}", timeout=120) as response:
                data = response.read()
            if len(data) < 64 or data.lstrip().startswith(b"<"):
                raise SystemExit(f"{relative}: server returned no motion data")
            target.write_bytes(data)
        digest = sha256(target)
        if relative in pinned and pinned[relative] != digest:
            raise SystemExit(f"{relative}: hash {digest} does not match manifest {pinned[relative]}")
        recorded[relative] = digest

    manifest = {
        "source": "CMU Graphics Lab Motion Capture Database, http://mocap.cs.cmu.edu",
        "terms": "The motion capture data may be copied, modified, or redistributed without permission.",
        "acknowledgement": "The data used in this project was obtained from mocap.cs.cmu.edu. "
        "The database was created with funding from NSF EIA-0196217.",
        "files": recorded,
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n")
    print(f"{len(recorded)} files verified")


if __name__ == "__main__":
    main()
