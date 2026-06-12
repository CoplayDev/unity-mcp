"""
Download the neural voice cast (Piper / rhasspy voices) used by build_audio.py.

  python3 pipeline/fetch_voices.py

~500 MB total; not committed to the repo. Models land in ../voices/.
"""
import sys
import urllib.request
from pathlib import Path

BASE = "https://huggingface.co/rhasspy/piper-voices/resolve/main"
VOICES = Path(__file__).resolve().parent.parent / "voices"

# local name -> path under the piper-voices repo
CAST = {
    "ryan":    "en/en_US/ryan/high/en_US-ryan-high",
    "amy":     "en/en_US/amy/medium/en_US-amy-medium",
    "lessac":  "en/en_US/lessac/medium/en_US-lessac-medium",
    "joe":     "en/en_US/joe/medium/en_US-joe-medium",
    "kristin": "en/en_US/kristin/medium/en_US-kristin-medium",
    "alan":    "en/en_GB/alan/medium/en_GB-alan-medium",
    "cori":    "en/en_GB/cori/high/en_GB-cori-high",
}


def fetch(url, dest):
    if dest.exists() and dest.stat().st_size > 0:
        print(f"  have {dest.name}")
        return
    print(f"  get  {dest.name} ...", flush=True)
    urllib.request.urlretrieve(url, dest)


def main():
    VOICES.mkdir(parents=True, exist_ok=True)
    for name, path in CAST.items():
        fetch(f"{BASE}/{path}.onnx", VOICES / f"{name}.onnx")
        fetch(f"{BASE}/{path}.onnx.json", VOICES / f"{name}.onnx.json")
    print("done ->", VOICES)


if __name__ == "__main__":
    sys.exit(main())
