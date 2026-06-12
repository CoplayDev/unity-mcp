"""
Procedural sound design for Mobile Frame: EXILE.

Everything here is synthesized from scratch with numpy — no sampled or
copyrighted audio. Mono, float32 in [-1, 1], at SR Hz.
"""
import numpy as np
from scipy.signal import lfilter

SR = 22050
rng = np.random.default_rng(7)


# ---------- primitives ----------

def silence(dur):
    return np.zeros(int(dur * SR), dtype=np.float32)


def t(dur):
    return np.linspace(0, dur, int(dur * SR), endpoint=False, dtype=np.float32)


def sine(freq, dur, phase=0.0):
    return np.sin(2 * np.pi * freq * t(dur) + phase).astype(np.float32)


def saw(freq, dur):
    x = t(dur) * freq
    return (2.0 * (x - np.floor(x + 0.5))).astype(np.float32)


def square(freq, dur, duty=0.5):
    x = (t(dur) * freq) % 1.0
    return np.where(x < duty, 1.0, -1.0).astype(np.float32)


def noise(dur):
    return rng.uniform(-1, 1, int(dur * SR)).astype(np.float32)


def env_adsr(sig, a=0.01, d=0.05, s=0.7, r=0.1):
    n = len(sig)
    e = np.ones(n, dtype=np.float32)
    ai, di, ri = int(a * SR), int(d * SR), int(r * SR)
    ai = min(ai, n)
    if ai:
        e[:ai] = np.linspace(0, 1, ai)
    if di and ai + di <= n:
        e[ai:ai + di] = np.linspace(1, s, di)
    e[ai + di:n - ri] = s
    if ri:
        e[n - ri:] = np.linspace(e[n - ri - 1] if n - ri - 1 >= 0 else s, 0, ri)
    return (sig * e).astype(np.float32)


def fade(sig, fin=0.01, fout=0.01):
    n = len(sig)
    out = sig.copy()
    fi, fo = int(fin * SR), int(fout * SR)
    if fi:
        out[:fi] *= np.linspace(0, 1, fi)
    if fo:
        out[-fo:] *= np.linspace(1, 0, fo)
    return out


# ---------- filters ----------

def lowpass(sig, cutoff):
    # one-pole IIR, vectorized via scipy lfilter
    a = float(np.exp(-2 * np.pi * cutoff / SR))
    return lfilter([1 - a], [1, -a], sig).astype(np.float32)


def highpass(sig, cutoff):
    return (sig - lowpass(sig, cutoff)).astype(np.float32)


def bandpass(sig, lo, hi):
    return lowpass(highpass(sig, lo), hi)


def reverb(sig, decay=0.4, mix=0.3, size=0.05):
    """Cheap Schroeder-ish: a few feedback combs + smear."""
    out = sig.copy()
    delays = [int(size * SR * k) for k in (1.0, 1.37, 1.81, 2.13)]
    wet = np.zeros_like(sig)
    for dl in delays:
        if dl < 1 or dl >= len(sig):
            continue
        buf = np.zeros_like(sig)
        buf[dl:] = sig[:-dl]
        g = decay
        acc = buf.copy()
        tap = buf
        for _ in range(4):
            tap = np.zeros_like(tap)
            tap[dl:] = acc[:-dl] * g
            acc = acc + tap
            g *= decay
        wet += acc / len(delays)
    wet = lowpass(wet, 4000)
    return ((1 - mix) * out + mix * wet).astype(np.float32)


def pitch_glide_noise(dur, lo, hi, blocks=24):
    """Filtered noise whose lowpass cutoff glides lo->hi (whoosh).

    Approximated as a sequence of constant-cutoff blocks so we can stay
    vectorized; with overlap-free concatenation the seam is inaudible under
    the envelope.
    """
    src = noise(dur)
    n = len(src)
    cuts = np.linspace(lo, hi, blocks)
    bounds = np.linspace(0, n, blocks + 1).astype(int)
    out = np.empty(n, dtype=np.float32)
    for k in range(blocks):
        s, e = bounds[k], bounds[k + 1]
        if e > s:
            out[s:e] = lowpass(src[s:e], cuts[k])
    return out


def norm(sig, peak=0.9):
    m = np.max(np.abs(sig)) + 1e-9
    return (sig * (peak / m)).astype(np.float32)


# ---------- named sound effects ----------

def bed_derelict(dur):
    """Dead colony: hollow wind + slow detuned drone + sparse metal creaks."""
    wind = lowpass(noise(dur), 220) * 0.5
    drone = (sine(46, dur) + 0.6 * sine(69.3, dur) + 0.3 * sine(92, dur)) * 0.15
    bed = wind + drone
    # sparse creaks
    out = bed.copy()
    n = len(out)
    for _ in range(max(1, int(dur / 3))):
        pos = rng.integers(0, max(1, n - SR))
        cd = rng.uniform(0.4, 0.9)
        creak = bandpass(noise(cd), 300, 1200)
        creak = env_adsr(creak, a=0.05, d=0.2, s=0.2, r=0.4) * 0.25
        end = min(n, pos + len(creak))
        out[pos:end] += creak[:end - pos]
    return norm(out, 0.55)


def bed_hangar(dur):
    hum = (sine(58, dur) + 0.5 * sine(87, dur) + 0.25 * sine(174, dur)) * 0.16
    air = lowpass(noise(dur), 600) * 0.12
    out = hum + air
    n = len(out)
    for _ in range(max(1, int(dur / 2.5))):
        pos = rng.integers(0, max(1, n - SR))
        drip = sine(rng.uniform(900, 1500), 0.25)
        drip = env_adsr(drip, a=0.001, d=0.08, s=0.0, r=0.16) * 0.2
        drip = reverb(drip, decay=0.5, mix=0.6, size=0.08)
        end = min(n, pos + len(drip))
        out[pos:end] += drip[:end - pos]
    return norm(out, 0.55)


def bed_cockpit(dur):
    hum = (sine(120, dur) + 0.4 * sine(240, dur)) * 0.1
    air = lowpass(noise(dur), 1500) * 0.05
    out = hum + air
    n = len(out)
    for _ in range(max(1, int(dur / 2))):
        pos = rng.integers(0, max(1, n - SR))
        blip = sine(rng.choice([1200, 1600, 2000]), 0.06)
        blip = env_adsr(blip, a=0.001, d=0.03, s=0.0, r=0.03) * 0.12
        end = min(n, pos + len(blip))
        out[pos:end] += blip[:end - pos]
    return norm(out, 0.5)


def bed_space(dur):
    rumble = lowpass(noise(dur), 90) * 0.5
    swell = (1 + 0.5 * np.sin(2 * np.pi * 0.1 * t(dur))).astype(np.float32)
    sub = sine(38, dur) * 0.2
    return norm((rumble * swell + sub), 0.5)


def alarm(dur=1.6):
    one = env_adsr(square(660, 0.18, 0.5), a=0.005, d=0.02, s=0.9, r=0.05) * 0.5
    two = env_adsr(square(880, 0.18, 0.5), a=0.005, d=0.02, s=0.9, r=0.05) * 0.5
    gap = silence(0.12)
    pattern = np.concatenate([one, gap, two, gap])
    reps = int(np.ceil(dur / (len(pattern) / SR)))
    sig = np.tile(pattern, reps)[:int(dur * SR)]
    return reverb(lowpass(sig, 4000), decay=0.3, mix=0.25)


def comm_open():
    blip = sine(1400, 0.05)
    blip = np.concatenate([blip, sine(2100, 0.05)])
    return env_adsr(blip, a=0.002, d=0.02, s=0.5, r=0.03) * 0.4


def comm_close():
    blip = np.concatenate([sine(2100, 0.05), sine(1200, 0.06)])
    return env_adsr(blip, a=0.002, d=0.02, s=0.4, r=0.04) * 0.35


def comm_static(dur=0.4):
    return bandpass(noise(dur), 800, 3000) * 0.18


def thruster(dur=1.2):
    whoosh = pitch_glide_noise(dur, 300, 2500) * 0.6
    rumble = lowpass(noise(dur), 120) * 0.4
    sig = fade(whoosh + rumble, 0.05, 0.3)
    return sig


def servo(dur=0.7):
    base = saw(rng.uniform(110, 150), dur) * 0.3
    base = lowpass(base, 1800)
    wob = (1 + 0.3 * np.sin(2 * np.pi * 18 * t(dur))).astype(np.float32)
    grind = bandpass(noise(dur), 200, 2500) * 0.12
    return fade(env_adsr(base * wob + grind, a=0.03, d=0.1, s=0.7, r=0.15), 0.02, 0.1)


def impact(dur=1.4):
    thud = sine(55, dur) * np.exp(-t(dur) * 6) * 0.9
    crack = bandpass(noise(0.25), 500, 4000) * np.exp(-t(0.25) * 25)
    sig = thud.copy()
    sig[:len(crack)] += crack
    return reverb(sig, decay=0.45, mix=0.3, size=0.06)


def explosion_distant(dur=2.0):
    boom = lowpass(noise(dur), 400) * np.exp(-t(dur) * 2.2)
    sub = sine(40, dur) * np.exp(-t(dur) * 3) * 0.7
    return reverb(norm(boom + sub, 0.8), decay=0.5, mix=0.4, size=0.1)


def boot_up():
    notes = [330, 415, 494, 659, 740]
    seq = []
    for i, f in enumerate(notes):
        b = sine(f, 0.12)
        b = env_adsr(b, a=0.005, d=0.05, s=0.4, r=0.06) * (0.25 + 0.03 * i)
        seq.append(b)
        seq.append(silence(0.04))
    seq.append(env_adsr(sine(988, 0.4), a=0.005, d=0.1, s=0.6, r=0.25) * 0.3)
    return reverb(np.concatenate(seq), decay=0.3, mix=0.3)


def targeting():
    ticks = []
    for _ in range(6):
        tk = env_adsr(sine(2600, 0.03), a=0.001, d=0.01, s=0.0, r=0.02) * 0.3
        ticks.append(tk)
        ticks.append(silence(0.07))
    lock = env_adsr(np.concatenate([sine(1800, 0.1), sine(2400, 0.18)]),
                    a=0.002, d=0.05, s=0.7, r=0.1) * 0.35
    return np.concatenate(ticks + [lock])


def power_down():
    n = int(0.9 * SR)
    f = np.linspace(400, 60, n)
    ph = np.cumsum(2 * np.pi * f / SR)
    sig = (np.sin(ph) * np.linspace(0.4, 0.0, n)).astype(np.float32)
    return lowpass(sig, 2000)


def heartbeat(dur=2.0):
    one = sine(50, 0.12) * np.exp(-t(0.12) * 18)
    beat = np.concatenate([one * 0.9, silence(0.18), one * 0.6, silence(0.4)])
    reps = int(np.ceil(dur / (len(beat) / SR)))
    return np.tile(beat, reps)[:int(dur * SR)] * 0.7


def _pad(freqs, dur, gain=0.2):
    """A warm detuned pad from stacked saws through a lowpass."""
    mix = np.zeros(int(dur * SR), dtype=np.float32)
    for f in freqs:
        for det in (-0.3, 0.0, 0.4):
            mix += saw(f * (1 + det / 100.0), dur)
    mix = lowpass(mix / (len(freqs) * 3), 1600)
    return fade(mix * gain, 0.4, 0.6)


def _pluck(freq, dur, gain=0.3):
    s = (0.6 * sine(freq, dur) + 0.4 * square(freq, dur, 0.4))
    s = env_adsr(s, a=0.005, d=0.12, s=0.3, r=max(0.05, dur * 0.4))
    return (s * gain).astype(np.float32)


def _melody(notes, gain=0.3):
    """notes: list of (freq_or_None, dur)."""
    parts = []
    for f, d in notes:
        parts.append(silence(d) if f is None else _pluck(f, d, gain))
    return np.concatenate(parts)


# A minor pentatonic-ish heroic motif. Original composition.
A2, E2, A3, C4, D4, E4, G4, A4, B4, C5, D5, E5 = (
    110.0, 82.4, 220.0, 261.6, 293.7, 329.6, 392.0, 440.0, 493.9, 523.3, 587.3, 659.3)


def intro_theme():
    dur = 13.0
    pad = (_pad([A3, C4, E4], 6.5, 0.16) )
    pad2 = _pad([G4 / 2, D4, G4], 6.5, 0.16)
    pad_full = np.concatenate([pad, pad2])
    bass = _melody([(A2, 1.5), (A2, 1.5), (E2, 1.5), (E2, 1.5),
                    (A2 * 0.9, 1.5), (A2 * 0.9, 1.5), (E2, 1.5), (E2, 1.0)], 0.5)
    lead = _melody([(None, 1.0), (A4, 0.5), (C5, 0.5), (E5, 1.0), (D5, 0.5),
                    (C5, 0.5), (B4, 1.5), (A4, 0.5), (B4, 0.5), (C5, 1.0),
                    (E5, 1.5), (D5, 1.0), (A4, 1.5)], 0.32)
    n = int(dur * SR)
    out = np.zeros(n, dtype=np.float32)
    for layer in (pad_full, bass, lead):
        m = min(n, len(layer))
        out[:m] += layer[:m]
    out += heartbeat(dur) * 0.25
    return reverb(norm(fade(out, 0.3, 1.2), 0.85), decay=0.35, mix=0.22)


def theme_sting():
    """Short heroic button for episode endings."""
    pad = _pad([A3, E4, A4], 3.2, 0.2)
    lead = _melody([(E5, 0.4), (D5, 0.4), (C5, 0.4), (E5, 1.4)], 0.34)
    bass = _melody([(A2, 1.6), (E2, 1.6)], 0.5)
    n = int(3.4 * SR)
    out = np.zeros(n, dtype=np.float32)
    for layer in (pad, bass, lead):
        m = min(n, len(layer))
        out[:m] += layer[:m]
    return reverb(norm(fade(out, 0.05, 1.0), 0.85), decay=0.4, mix=0.3)


# registry of one-shot effects (beds handled separately, they need a duration)
ONESHOTS = {
    "theme": intro_theme,
    "sting": theme_sting,
    "alarm": alarm,
    "comm_open": comm_open,
    "comm_close": comm_close,
    "comm_static": comm_static,
    "thruster": thruster,
    "servo": servo,
    "impact": impact,
    "explosion": explosion_distant,
    "bootup": boot_up,
    "targeting": targeting,
    "powerdown": power_down,
    "heartbeat": heartbeat,
}

BEDS = {
    "derelict": bed_derelict,
    "hangar": bed_hangar,
    "cockpit": bed_cockpit,
    "space": bed_space,
}


if __name__ == "__main__":
    # smoke test: render a montage so we can sanity-check the synth
    import wave
    parts = [bed_derelict(2), alarm(1.6), thruster(1.2), servo(0.7),
             impact(1.4), boot_up(), targeting(), explosion_distant(2)]
    mix = np.concatenate([fade(p, 0.02, 0.05) for p in parts])
    mix = norm(mix, 0.9)
    pcm = (mix * 32767).astype(np.int16)
    w = wave.open("/home/user/anime-podcast/build/_sfx_demo.wav", "w")
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes(pcm.tobytes()); w.close()
    print("wrote _sfx_demo.wav", len(mix) / SR, "sec")
