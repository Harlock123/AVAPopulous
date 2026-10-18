#!/usr/bin/env python3
"""Synthesises AVAPopulous's sound effects and tunes into ../src/AVAPopulous/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices. The title plays
Greensleeves, the traditional English air, on a plucked lute voice; the pastoral tune in play is a new composition.
The two words are spoken by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPopulous", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def pluck(seconds, note, gain=0.6):
    """A plucked string: bright at first, dying away."""
    f = hz(note)
    return mix(tone(seconds, f, kind="tri", attack=0.002, release=1.0, gain=gain), tone(min(seconds, 0.1), f * 2, kind="pulse", attack=0.001, release=1.0, gain=gain * 0.22))


def play(bpm, notes, gain=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        out.extend(silence(beats * beat) if note == "-" else pluck(beats * beat, note, gain))
    return out


def bass(bpm, notes):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        out.extend(silence(beats * beat) if note == "-" else tone(beats * beat, hz(note), kind="tri", release=0.5, gain=0.5))
    return out


def greensleeves():
    """Greensleeves (traditional): verse and refrain, in A minor, with a bass under it, in quavers of 6/8."""
    verse_a = [("A4", 1), ("C5", 2), ("D5", 1), ("E5", 1.5), ("F5", 0.5), ("E5", 1), ("D5", 2), ("B4", 1), ("G4", 1.5), ("A4", 0.5), ("B4", 1),
               ("C5", 2), ("A4", 1), ("A4", 1.5), ("G#4", 0.5), ("A4", 1), ("B4", 2), ("G#4", 1), ("E4", 2)]
    verse_b = [("A4", 1), ("C5", 2), ("D5", 1), ("E5", 1.5), ("F5", 0.5), ("E5", 1), ("D5", 2), ("B4", 1), ("G4", 1.5), ("A4", 0.5), ("B4", 1),
               ("C5", 1.5), ("B4", 0.5), ("A4", 1), ("G#4", 1.5), ("F#4", 0.5), ("G#4", 1), ("A4", 3), ("A4", 2)]
    refrain_a = [("G5", 1), ("G5", 3), ("G5", 1.5), ("F#5", 0.5), ("E5", 1), ("D5", 2), ("B4", 1), ("G4", 1.5), ("A4", 0.5), ("B4", 1),
                 ("C5", 2), ("A4", 1), ("A4", 1.5), ("G#4", 0.5), ("A4", 1), ("B4", 2), ("G#4", 1), ("E4", 2)]
    refrain_b = [("G5", 1), ("G5", 3), ("G5", 1.5), ("F#5", 0.5), ("E5", 1), ("D5", 2), ("B4", 1), ("G4", 1.5), ("A4", 0.5), ("B4", 1),
                 ("C5", 1.5), ("B4", 0.5), ("A4", 1), ("G#4", 1.5), ("F#4", 0.5), ("G#4", 1), ("A4", 3), ("A4", 3)]
    lead = verse_a + verse_b + refrain_a + refrain_b
    total = sum(b for _, b in lead)
    chords = ["A2", "G2", "A2", "E2", "A2", "G2", "A2", "A2", "C3", "G2", "A2", "E2", "C3", "G2", "A2", "A2"]
    low = []
    for c in chords:
        low += [(c, 3), (c, 3)]
    # Fit the bass to the tune's length (the pick-up notes make it a beat out).
    low_total = sum(b for _, b in low)
    low = [("-", 1)] + low[:-1] + [(low[-1][0], low[-1][1] + total - low_total)]
    return lead, low


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The land: up a step, down a step.
    write("Raise.wav", tone(0.06, 300, 600, "square", attack=0.002, release=0.8, gain=0.5), 0.12)
    write("Lower.wav", tone(0.06, 500, 250, "square", attack=0.002, release=0.8, gain=0.5), 0.12)
    # The people: a town built (hammering), a town grown (a little tune), a fight, a town burnt, a drowning.
    write("Build.wav", cat(*[cat(tone(0.03, 1400, 700, "noise", attack=0.001, release=1.0), silence(0.06)) for _ in range(3)]), 0.2)
    write("Grow.wav", cat(*[pluck(0.08, n) for n in ["C5", "E5", "G5", "C6"]]), 0.2)
    write("Fight.wav", mix(tone(0.12, 3000, 1500, "noise", attack=0.001, release=1.0), tone(0.08, 900, 600, "square", attack=0.001, release=1.0, gain=0.4)), 0.2)
    write("Burn.wav", cat(*[tone(0.04, 1200 + RNG.uniform(-400, 400), 300, "noise", attack=0.002, release=1.0, gain=RNG.uniform(0.4, 1.0)) for _ in range(10)]), 0.25)
    write("Drown.wav", cat(*[tone(0.05, 600 - i * 40, 400 - i * 30, "sine", attack=0.005, release=1.0) for i in range(8)]), 0.25)
    # The powers.
    write("Magnet.wav", mix(*[tone(0.9, hz(n), kind="sine", attack=0.005, release=1.0, vibrato=0.01, vib_rate=6, gain=0.5) for n in ["E5", "B5", "E6"]]), 0.3)
    write("Quake.wav", mix(tone(1.6, 60, 40, "noise", attack=0.05, release=0.8), tone(1.6, 45, 35, "square", attack=0.05, release=0.8, vibrato=0.2, vib_rate=11, gain=0.5)), 0.5)
    write("SwampSound.wav", cat(*[tone(0.09, 180 + i * 15, 90, "sine", attack=0.005, release=1.0) for i in range(9)]), 0.35)
    write("KnightSound.wav", cat(*[tone(d, hz(n), kind="square", release=0.4, gain=0.6) for n, d in [("C5", 0.12), ("C5", 0.12), ("C5", 0.12), ("G5", 0.5)]]), 0.3)
    write("VolcanoSound.wav", cat(mix(tone(0.5, 120, 40, "noise", attack=0.002, release=0.6), tone(0.5, 70, 30, "square", attack=0.002, release=0.6, gain=0.6)),
                                  tone(1.2, 400, 80, "noise", attack=0.01, release=1.0, gain=0.6)), 0.55)
    write("FloodSound.wav", tone(2.0, 400, 2500, "noise", attack=0.3, release=0.7), 0.3)
    write("ArmageddonSound.wav", mix(tone(2.5, 55, 50, "saw", attack=0.1, release=0.6, vibrato=0.03, vib_rate=3),
                                     *[tone(2.5, hz(n), kind="sine", attack=0.005, release=1.0, gain=0.4) for n in ["A3", "E4", "A4"]]), 0.5)
    write("Win.wav", cat(*[pluck(0.1, n) for n in ["A4", "C#5", "E5", "A5"]]), 0.3)
    write("Lose.wav", cat(*[pluck(0.16, n) for n in ["E5", "C5", "A4", "E4"]]), 0.3)
    write("Refused.wav", tone(0.18, 110, kind="square", release=0.6, gain=0.5), 0.18)
    for name, words, pitch in [("SayVictory", "V IH1 K T ER IY !", 105), ("SayDefeat", "D IH F IY1 T !", 95)]:
        write(name + ".wav", mix(say(words, rate=RATE, pitch=pitch, speed=0.9, size=1.0), [s * 0.3 for s in say(words, rate=RATE, pitch=pitch * 1.5, speed=0.9, size=1.0, seed=11)]), 0.5)

    # In play: a pastoral tune of its own, on the lute over a soft bass.
    q = 1
    tune_ = [("D5", q), ("F#5", q), ("A5", 2), ("G5", q), ("F#5", q), ("E5", 2),
             ("D5", q), ("E5", q), ("F#5", q), ("D5", q), ("B4", 2), ("A4", 2),
             ("D5", q), ("F#5", q), ("A5", q), ("B5", q), ("A5", q), ("F#5", q), ("G5", 2),
             ("F#5", q), ("E5", q), ("D5", q), ("E5", q), ("D5", 4)]
    low = [("D3", 4), ("A2", 4), ("B2", 4), ("A2", 4), ("D3", 4), ("G2", 4), ("A2", 4), ("D3", 4)]
    write("Music.wav", mix(play(104, tune_ + tune_), bass(104, low + low)), 0.24)
    # The title: Greensleeves.
    lead, low = greensleeves()
    write("TitleTune.wav", cat(mix(play(200, lead), bass(200, low)), silence(2.0)), 0.3)
    write("Victory.wav", mix(play(120, [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 1.5), ("C#6", 0.5), ("D6", 2.5)]), bass(120, [("D3", 2), ("A2", 1), ("D3", 3)])), 0.4)
    write("GameOver.wav", mix(play(66, [("E5", 1), ("C5", 1), ("B4", 1), ("G#4", 1), ("A4", 3)]), bass(66, [("A2", 2), ("E2", 2), ("A2", 3)])), 0.42)


if __name__ == "__main__":
    main()
