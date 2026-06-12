"""
Build the three style mixes for Mobile Frame: EXILE.

  python3 pipeline/build_audio.py

Outputs to web/audio/*.mp3 and web/data/episodes.json (transcripts + cues).
"""
import json
import os
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np
from scipy.signal import resample_poly

sys.path.insert(0, str(Path(__file__).parent))
import sfx  # noqa: E402
from sfx import SR  # noqa: E402
import script_data as S  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
VOICES = ROOT / "voices"
BUILD = ROOT / "build"
AUDIO_OUT = ROOT / "web" / "audio"
DATA_OUT = ROOT / "web" / "data"
for d in (BUILD, AUDIO_OUT, DATA_OUT):
    d.mkdir(parents=True, exist_ok=True)

import imageio_ffmpeg
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
PIPER = "piper"

GAP = 0.32          # gap after a spoken line
ACT_GAP = 0.28      # gap after narration
BED_GAIN = {"pure": 0.34, "described": 0.24, "recap": 0.0}
DUCK = 0.42         # bed multiplier under voices


# ---------------- low level audio io ----------------

def read_wav_f32(path):
    with wave.open(str(path), "rb") as w:
        rate = w.getframerate()
        n = w.getnframes()
        ch = w.getnchannels()
        raw = w.readframes(n)
    a = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0
    if ch == 2:
        a = a.reshape(-1, 2).mean(axis=1)
    if rate != SR:
        a = resample_poly(a, SR, rate).astype(np.float32)
    return a


def write_wav_f32(path, sig):
    pcm = np.clip(sig, -1, 1)
    pcm = (pcm * 32767).astype(np.int16)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def encode_mp3(wav_path, mp3_path):
    subprocess.run(
        [FFMPEG, "-y", "-loglevel", "error", "-i", str(wav_path),
         "-codec:a", "libmp3lame", "-b:a", "128k", str(mp3_path)],
        check=True)


# ---------------- voice rendering ----------------

_voice_cache = {}

def render_voice(voice, text, ls):
    key = (voice, ls, text)
    if key in _voice_cache:
        return _voice_cache[key]
    model = VOICES / f"{voice}.onnx"
    tmp = BUILD / "_line.wav"
    proc = subprocess.run(
        [PIPER, "-m", str(model), "--length_scale", str(ls),
         "--sentence_silence", "0.18", "-f", str(tmp)],
        input=text.encode("utf-8"),
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if proc.returncode != 0 or not tmp.exists():
        raise RuntimeError(f"piper failed for {voice}: {text[:40]}")
    sig = read_wav_f32(tmp)
    # trim leading/trailing near-silence, then normalize the line
    sig = _trim(sig)
    peak = np.max(np.abs(sig)) + 1e-9
    sig = (sig * (0.72 / peak)).astype(np.float32)
    _voice_cache[key] = sig
    return sig


def _trim(sig, thresh=0.01, pad=0.04):
    idx = np.where(np.abs(sig) > thresh)[0]
    if len(idx) == 0:
        return sig
    p = int(pad * SR)
    a = max(0, idx[0] - p)
    b = min(len(sig), idx[-1] + p)
    return sig[a:b]


# ---------------- voice processing ----------------

def proc_robot(sig):
    """Synthetic A.I. voice: ring-mod + metallic comb + bandpass."""
    n = len(sig)
    tt = np.arange(n) / SR
    carrier = np.sin(2 * np.pi * 50 * tt).astype(np.float32)
    wet = sig * (0.55 + 0.45 * carrier)
    out = 0.5 * sig + 0.5 * wet
    d = int(0.003 * SR)
    comb = np.zeros_like(out)
    comb[d:] = out[:-d]
    out = out + 0.22 * comb
    out = sfx.bandpass(out, 180, 3800)
    return _renorm(out, 0.72)


def proc_comm(sig):
    """Radio / comms: band-limited + soft clip + faint static bed."""
    bp = sfx.bandpass(sig, 350, 3000)
    crunch = np.tanh(bp * 2.2) * 0.5
    out = 0.85 * crunch
    static = sfx.comm_static(len(sig) / SR) * 0.10
    m = min(len(out), len(static))
    out[:m] += static[:m]
    return _renorm(out, 0.7)


def _renorm(sig, peak=0.72):
    m = np.max(np.abs(sig)) + 1e-9
    return (sig * (peak / m)).astype(np.float32)


# ---------------- one-shot sfx ----------------

_fx_cache = {}

def get_fx(name):
    if name not in _fx_cache:
        _fx_cache[name] = sfx.ONESHOTS[name]()
    return _fx_cache[name]


# ---------------- timeline mixer ----------------

class Timeline:
    def __init__(self, style):
        self.style = style
        self.events = []     # (offset_samples, signal, gain)
        self.voice_spans = []  # (start_s, end_s) for ducking
        self.beds = []       # (start_s, end_s, bedname)
        self.cues = []       # transcript cues
        self.cursor = 0.0    # seconds
        self._bed_open = None

    def _place(self, sig, at_s, gain=1.0):
        off = int(at_s * SR)
        self.events.append((off, sig, gain))
        return off

    def add_voice(self, sig, who, text, gain=1.0, gap=GAP):
        start = self.cursor
        self._place(sig, start, gain)
        dur = len(sig) / SR
        end = start + dur
        self.voice_spans.append((start, end))
        self.cues.append({"t": round(start, 2), "end": round(end, 2),
                          "who": who, "name": S.NAMES.get(who, who),
                          "text": text})
        self.cursor = end + gap

    def add_fx(self, name, gain=0.7, at=None, advance=0.0):
        sig = get_fx(name) * gain
        at = self.cursor if at is None else at
        self._place(sig, at)
        if advance:
            self.cursor = max(self.cursor, at + len(sig) / SR * advance)

    def add_fx_list(self, fx_list, anchor):
        """fx attached to a line: placed relative to the line's start."""
        for f in fx_list:
            self.add_fx(f["name"], f.get("gain", 0.7), at=anchor + f.get("pre", 0.0))

    def pause(self, dur):
        self.cursor += dur

    def open_bed(self, name):
        self.close_bed()
        self._bed_open = (self.cursor, name)

    def close_bed(self):
        if self._bed_open:
            start, name = self._bed_open
            self.beds.append((start, self.cursor, name))
            self._bed_open = None

    def render(self):
        self.close_bed()
        total_s = self.cursor + 1.0
        total = int(total_s * SR)
        master = np.zeros(total, dtype=np.float32)

        # voice + fx events
        for off, sig, gain in self.events:
            end = off + len(sig)
            if end > len(master):
                master = np.concatenate(
                    [master, np.zeros(end - len(master), dtype=np.float32)])
            master[off:end] += sig * gain

        # ducking envelope (1.0 -> DUCK under voices), smoothed
        duck_env = np.ones(len(master), dtype=np.float32)
        for s, e in self.voice_spans:
            a = max(0, int((s - 0.12) * SR))
            b = min(len(master), int((e + 0.25) * SR))
            duck_env[a:b] = DUCK
        duck_env = sfx.lowpass(duck_env, 8.0)  # smooth transitions

        # ambient beds
        bg = BED_GAIN[self.style]
        if bg > 0:
            for s, e, name in self.beds:
                seg = max(0.2, e - s)
                bed = sfx.BEDS[name](seg) * bg
                a = int(s * SR)
                b = min(len(master), a + len(bed))
                env = duck_env[a:b]
                master[a:b] += bed[:b - a] * env

        return _limit(master)


def _limit(sig, ceiling=0.95):
    m = np.max(np.abs(sig)) + 1e-9
    if m > ceiling:
        sig = sig * (ceiling / m)
    # gentle tanh safety
    return np.tanh(sig * 1.05).astype(np.float32) * 0.96


# ---------------- style builders ----------------

def render_say(ev):
    role = S.ROLES[ev["who"]]
    ls = ev["ls"] or role["ls"]
    sig = render_voice(role["voice"], ev["text"], ls).copy()
    if role.get("robot"):
        sig = proc_robot(sig)
    if role.get("comm"):
        sig = proc_comm(sig)
    return sig


def build_scene(style):
    tl = Timeline(style)
    # episode opens with the main theme
    tl.add_fx("theme", 0.6)
    tl.pause(get_fx("theme").shape[0] / SR * 0.82)
    for ev in S.SCENE:
        t = ev["t"]
        if t == "loc":
            tl.open_bed(ev["bed"])
        elif t == "fx":
            tl.add_fx(ev["name"], ev.get("gain", 0.7), at=tl.cursor + ev.get("pre", 0.0))
            tl.pause(0.35)
        elif t == "pause":
            tl.pause(ev["dur"])
        elif t == "act":
            if style == "described":
                sig = render_voice(S.ROLES["NARRATOR"]["voice"], ev["text"],
                                   S.ROLES["NARRATOR"]["ls"])
                anchor = tl.cursor
                tl.add_fx_list(ev["fx"], anchor)
                tl.add_voice(sig, "NARRATOR", ev["text"], gap=ACT_GAP)
            else:  # pure: no narration, but let the sound design breathe
                anchor = tl.cursor
                tl.add_fx_list(ev["fx"], anchor)
                if ev["fx"]:
                    longest = max((len(get_fx(f["name"])) / SR + f.get("pre", 0)
                                   for f in ev["fx"]), default=0)
                    tl.pause(min(2.2, longest * 0.7 + 0.25))
        elif t == "say":
            sig = render_say(ev)
            anchor = tl.cursor
            tl.add_fx_list(ev["fx"], anchor)
            tl.add_voice(sig, ev["who"], ev["text"])
    return tl


def build_recap():
    tl = Timeline("recap")
    # render scene first so clip refs are available
    clips = {}
    for ev in S.SCENE:
        if ev.get("t") == "say" and ev.get("id"):
            clips[ev["id"]] = (render_say(ev), ev["who"], ev["text"])
    for ev in S.RECAP:
        t = ev["t"]
        if t == "fx":
            tl.add_fx(ev["name"], ev.get("gain", 0.7), at=tl.cursor + ev.get("pre", 0.0))
            tl.pause(get_fx(ev["name"]).shape[0] / SR * (0.8 if ev["name"] == "theme" else 0.5))
        elif t == "say":
            sig = render_say(ev)
            tl.add_voice(sig, ev["who"], ev["text"])
        elif t == "clip":
            sig, who, text = clips[ev["ref"]]
            # frame the clip with a soft comm blip so it reads as an inset
            tl.add_fx("comm_open", 0.25)
            tl.pause(0.12)
            tl.add_voice(sig.copy(), who, text, gap=0.45)
    return tl


# ---------------- main ----------------

STYLES = {
    "pure": {
        "id": "pure",
        "label": "Pure Cut",
        "tag": "Dialogue + sound design, zero narration.",
        "blurb": "The scene, raw. Voices, comms, mecha, and ambient sound only — "
                 "like watching with your eyes closed. Most immersive, asks the "
                 "most of your imagination.",
        "build": lambda: build_scene("pure"),
    },
    "described": {
        "id": "described",
        "label": "Audio-Described",
        "tag": "A narrator paints the action between the lines.",
        "blurb": "Every visual beat is described by a narrator, the way audio "
                 "description works for film. You will never lose the plot — "
                 "ideal for a long ride where you can't glance at a screen.",
        "build": lambda: build_scene("described"),
    },
    "recap": {
        "id": "recap",
        "label": "Recap Hosts",
        "tag": "Two hosts react and riff, with clips dropped in.",
        "blurb": "A podcast ABOUT the episode — two hosts recap, joke, and pull "
                 "in real clips. Lightest and most fun, least faithful. Great "
                 "for catching up without committing.",
        "build": build_recap,
    },
}


def main():
    manifest = {"episode": S.EPISODE, "roles": S.NAMES, "styles": []}
    for sid in ("pure", "described", "recap"):
        spec = STYLES[sid]
        print(f"[build] {spec['label']} ...", flush=True)
        tl = spec["build"]()
        master = tl.render()
        wav = BUILD / f"exile-ep1-{sid}.wav"
        mp3 = AUDIO_OUT / f"exile-ep1-{sid}.mp3"
        write_wav_f32(wav, master)
        encode_mp3(wav, mp3)
        dur = len(master) / SR
        size_kb = mp3.stat().st_size // 1024
        print(f"        {dur:5.1f}s  {size_kb}KB  -> {mp3.name}", flush=True)
        manifest["styles"].append({
            "id": spec["id"], "label": spec["label"], "tag": spec["tag"],
            "blurb": spec["blurb"], "audio": f"audio/{mp3.name}",
            "duration": round(dur, 2), "cues": tl.cues,
        })
    out = DATA_OUT / "episodes.json"
    out.write_text(json.dumps(manifest, indent=2))
    print(f"[build] wrote {out}")


if __name__ == "__main__":
    main()
