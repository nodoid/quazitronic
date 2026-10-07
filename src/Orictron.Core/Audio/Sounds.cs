using System;
using System.Collections.Generic;
using Orictron.Game;

namespace Orictron.Audio;

/// <summary>Sounds of the enhanced game and the menus.</summary>
public enum Sfx
{
    MenuMove,
    MenuSelect,
    MenuBack,
    Shot,
    EnemyShot,
    Boom,
    BigBoom,
    Ram,
    Bell,
    Lift,
    Charge,
    Grapple,
    PulseFire,
    EnemyPulse,
    CellTaken,
    EnemyCellTaken,
    TransferWin,
    TransferLose,
    Deadlock,
    Ejected,
    Cleared,
    GameOver,
    ShipSecured,
    Spark,
}

/// <summary>
/// The enhanced sound set: layered oscillators, filter sweeps, noise and echo, synthesised at
/// start-up (no audio files ship). The original's AY blips keep their pitch: a cue's tone period
/// becomes the pitch of a soft bell.
/// </summary>
public sealed class Sounds
{
    private readonly Dictionary<Sfx, float[]> _fx = new();
    private readonly AudioEngine _engine;

    public Sounds(AudioEngine engine)
    {
        _engine = engine;
        foreach (var kv in Synthesize()) _fx[kv.Key] = kv.Value;
    }

    public bool Enabled { get; set; } = true;

    /// <summary>Raised for each sound played (tests and the capture tool listen).</summary>
    public event Action<Sfx>? Played;

    public void Play(Sfx s, float volume = 1f, float pitch = 0f)
    {
        Played?.Invoke(s);
        if (!Enabled) return;
        _engine.Play(_fx[s], volume, pitch);
    }

    /// <summary>A bell at the pitch of the original's AY tone period (f = 1 MHz / 16 / period).</summary>
    public void Blip(int period, float volume = 0.5f)
    {
        double hz = 1_000_000.0 / 16 / Math.Max(4, period);
        Play(Sfx.Bell, volume, (float)Math.Log2(hz / 880.0));
    }

    /// <summary>Turns the simulation's cues into sound.</summary>
    public void PlayCues(IReadOnlyList<CueEvent> cues)
    {
        int bells = 0;
        foreach (var c in cues)
        {
            switch (c.Cue)
            {
                case Cue.Shot: Play(Sfx.Shot, 0.55f); break;
                case Cue.EnemyShot: Play(Sfx.EnemyShot, 0.45f); break;
                case Cue.Boom: Play(Sfx.Boom, 0.8f); break;
                case Cue.Ram: Play(Sfx.Ram, 0.8f); break;
                case Cue.Blip:
                case Cue.Flash:
                    if (bells++ < 2) Blip(c.Param, c.Cue == Cue.Flash ? 0.25f : 0.45f);
                    break;
                case Cue.Lift: Play(Sfx.Lift, 0.7f); break;
                case Cue.Charge: Play(Sfx.Charge, 0.3f, (c.Param - 40) / 40f); break;
                case Cue.Grapple: Play(Sfx.Grapple, 0.6f); break;
                case Cue.PulseFire: Play(Sfx.PulseFire, 0.6f); break;
                case Cue.EnemyPulse: Play(Sfx.EnemyPulse, 0.5f); break;
                case Cue.CellTaken: Play(Sfx.CellTaken, 0.55f); break;
                case Cue.EnemyCellTaken: Play(Sfx.EnemyCellTaken, 0.5f); break;
                case Cue.TransferWin: Play(Sfx.TransferWin, 0.7f); break;
                case Cue.TransferLose: Play(Sfx.TransferLose, 0.7f); break;
                case Cue.Deadlock: Play(Sfx.Deadlock, 0.6f); break;
                case Cue.Ejected: Play(Sfx.Ejected, 0.7f); break;
                case Cue.Cleared: Play(Sfx.Cleared, 0.7f); break;
                case Cue.GameOver: Play(Sfx.GameOver, 0.7f); break;
                case Cue.ShipSecured: Play(Sfx.ShipSecured, 0.75f); break;
            }
        }
    }

    public static Dictionary<Sfx, float[]> Synthesize()
    {
        var d = new Dictionary<Sfx, float[]>();
        static float[] Space(float[] x, double delay = 0.11, double fb = 0.32, double mix = 0.35) =>
            Synth.SoftClip(Synth.Echo(x, delay, fb, mix, delay * 4));

        d[Sfx.MenuMove] = Synth.Render(
            new Voice(Wave.Triangle, 1320, 1320, 0.03, 0.35, Release: 0.05),
            new Voice(Wave.Sine, 2640, 2640, 0.02, 0.12, Release: 0.04));
        d[Sfx.MenuSelect] = Space(Synth.Render(
            new Voice(Wave.Pulse, 660, 660, 0.06, 0.22, Duty: 0.25, Release: 0.05, Cutoff: 4000),
            new Voice(Wave.Pulse, 990, 990, 0.08, 0.22, Duty: 0.25, Release: 0.1, Delay: 0.06, Cutoff: 4000),
            new Voice(Wave.Sine, 1980, 1980, 0.06, 0.1, Release: 0.15, Delay: 0.06)));
        d[Sfx.MenuBack] = Synth.Render(
            new Voice(Wave.Triangle, 700, 420, 0.09, 0.35, Release: 0.06));

        // Player laser: a bright pulse sweep with a noise click and a sub "thwip".
        d[Sfx.Shot] = Space(Synth.Render(
            new Voice(Wave.Pulse, 2200, 380, 0.13, 0.3, Duty: 0.18, Release: 0.04, Cutoff: 7000, CutoffEnd: 1500),
            new Voice(Wave.Saw, 1100, 190, 0.13, 0.16, Release: 0.04, Cutoff: 3000, CutoffEnd: 800),
            new Voice(Wave.Noise, 0, 0, 0.015, 0.25, Release: 0.02, Cutoff: 6000),
            new Voice(Wave.Sine, 220, 70, 0.08, 0.3, Release: 0.03)), 0.07, 0.25, 0.25);
        d[Sfx.EnemyShot] = Space(Synth.Render(
            new Voice(Wave.Saw, 900, 210, 0.16, 0.28, Release: 0.05, Cutoff: 2600, CutoffEnd: 500),
            new Voice(Wave.Square, 450, 120, 0.16, 0.12, Release: 0.05, Cutoff: 1800),
            new Voice(Wave.Noise, 0, 0, 0.02, 0.15, Release: 0.02, Cutoff: 3000)), 0.09, 0.22, 0.25);

        // Explosions: filtered noise falling away, a sub thump and crackle; echo for the hull.
        d[Sfx.Boom] = Space(Synth.Render(
            new Voice(Wave.Noise, 0, 0, 0.55, 0.6, Attack: 0.002, Decay: 0.3, Sustain: 0.35, Release: 0.4, Cutoff: 5200, CutoffEnd: 180),
            new Voice(Wave.Sine, 110, 34, 0.45, 0.65, Attack: 0.002, Decay: 0.2, Sustain: 0.5, Release: 0.3),
            new Voice(Wave.Noise, 0, 0, 0.3, 0.25, Release: 0.2, NoiseHold: 40, Delay: 0.05, Cutoff: 2500)), 0.14, 0.3, 0.3);
        d[Sfx.BigBoom] = Space(Synth.Render(
            new Voice(Wave.Noise, 0, 0, 0.9, 0.7, Attack: 0.002, Decay: 0.4, Sustain: 0.4, Release: 0.7, Cutoff: 6000, CutoffEnd: 120),
            new Voice(Wave.Sine, 90, 28, 0.8, 0.8, Attack: 0.002, Decay: 0.3, Sustain: 0.5, Release: 0.5),
            new Voice(Wave.Noise, 0, 0, 0.6, 0.3, Release: 0.4, NoiseHold: 60, Delay: 0.1, Cutoff: 1800)), 0.18, 0.35, 0.35);

        // Ramming: a metallic clang - inharmonic sines over a noise hit.
        d[Sfx.Ram] = Space(Synth.Render(
            new Voice(Wave.Noise, 0, 0, 0.03, 0.5, Release: 0.04, Cutoff: 4000),
            new Voice(Wave.Square, 190, 120, 0.06, 0.25, Release: 0.08, Cutoff: 1500),
            new Voice(Wave.Sine, 523, 518, 0.05, 0.25, Release: 0.45),
            new Voice(Wave.Sine, 1381, 1370, 0.04, 0.12, Release: 0.3),
            new Voice(Wave.Sine, 2237, 2230, 0.03, 0.06, Release: 0.2)), 0.06, 0.3, 0.3);

        // A soft bell at 880 Hz, re-pitched for the original's blips.
        d[Sfx.Bell] = Synth.Render(
            new Voice(Wave.Sine, 880, 880, 0.02, 0.4, Attack: 0.002, Release: 0.16),
            new Voice(Wave.Triangle, 1760, 1760, 0.01, 0.12, Attack: 0.002, Release: 0.08),
            new Voice(Wave.Sine, 2640, 2640, 0.01, 0.05, Release: 0.05));

        d[Sfx.Lift] = Space(Synth.Render(
            new Voice(Wave.Noise, 0, 0, 0.8, 0.22, Attack: 0.2, Sustain: 0.8, Release: 0.3, Cutoff: 300, CutoffEnd: 3500),
            new Voice(Wave.Sine, 160, 640, 0.8, 0.3, Attack: 0.1, Sustain: 0.8, Release: 0.3),
            new Voice(Wave.Triangle, 320, 1280, 0.8, 0.12, Attack: 0.1, Sustain: 0.8, Release: 0.3),
            new Voice(Wave.Sine, 1568, 1568, 0.08, 0.2, Release: 0.5, Delay: 0.75),
            new Voice(Wave.Sine, 2093, 2093, 0.08, 0.15, Release: 0.6, Delay: 0.85)), 0.15, 0.35, 0.3);

        d[Sfx.Charge] = Synth.Render(
            new Voice(Wave.Sine, 660, 990, 0.05, 0.25, Release: 0.06),
            new Voice(Wave.Triangle, 1320, 1980, 0.04, 0.08, Release: 0.04));

        // Grapple: an electric buzz with a rising filter.
        d[Sfx.Grapple] = Space(Synth.Render(
            new Voice(Wave.Saw, 110, 116, 0.45, 0.3, Attack: 0.01, Sustain: 0.8, Release: 0.15, VibratoHz: 30, VibratoDepth: 0.04, Cutoff: 400, CutoffEnd: 3200),
            new Voice(Wave.Saw, 165, 170, 0.45, 0.18, Attack: 0.01, Sustain: 0.8, Release: 0.15, VibratoHz: 23, VibratoDepth: 0.05, Cutoff: 600, CutoffEnd: 4000),
            new Voice(Wave.Noise, 0, 0, 0.45, 0.06, Release: 0.1, NoiseHold: 3, Cutoff: 5000)), 0.1, 0.3, 0.25);

        d[Sfx.PulseFire] = Space(Synth.Render(
            new Voice(Wave.Pulse, 400, 1600, 0.1, 0.25, Duty: 0.3, Release: 0.06, Cutoff: 6000),
            new Voice(Wave.Sine, 800, 3200, 0.1, 0.15, Release: 0.06)), 0.08, 0.3, 0.3);
        d[Sfx.EnemyPulse] = Space(Synth.Render(
            new Voice(Wave.Pulse, 900, 300, 0.12, 0.22, Duty: 0.4, Release: 0.06, Cutoff: 3500),
            new Voice(Wave.Saw, 450, 150, 0.12, 0.1, Release: 0.06, Cutoff: 2000)), 0.08, 0.3, 0.3);
        d[Sfx.CellTaken] = Space(Synth.Render(
            new Voice(Wave.Triangle, Synth.NoteHz("E6"), Synth.NoteHz("E6"), 0.05, 0.3, Release: 0.2),
            new Voice(Wave.Sine, Synth.NoteHz("B6"), Synth.NoteHz("B6"), 0.05, 0.2, Release: 0.25, Delay: 0.04)));
        d[Sfx.EnemyCellTaken] = Space(Synth.Render(
            new Voice(Wave.Square, Synth.NoteHz("C4"), Synth.NoteHz("C4"), 0.06, 0.18, Release: 0.15, Cutoff: 1500),
            new Voice(Wave.Square, Synth.NoteHz("Eb4"), Synth.NoteHz("Eb4"), 0.06, 0.14, Release: 0.2, Delay: 0.05, Cutoff: 1500)));

        d[Sfx.TransferWin] = Space(Arpeggio(new[] { "C5", "E5", "G5", "C6", "E6", "G6" }, 0.07, Wave.Pulse, 0.22), 0.15, 0.35, 0.35);
        d[Sfx.TransferLose] = Space(Synth.Render(
            new Voice(Wave.Saw, 440, 55, 1.0, 0.3, Sustain: 0.8, Release: 0.3, Cutoff: 3000, CutoffEnd: 200),
            new Voice(Wave.Square, 330, 41, 1.0, 0.15, Sustain: 0.8, Release: 0.3, Cutoff: 2000, CutoffEnd: 150),
            new Voice(Wave.Noise, 0, 0, 0.7, 0.2, Release: 0.4, Delay: 0.3, Cutoff: 1500, CutoffEnd: 100)), 0.15, 0.3, 0.3);
        d[Sfx.Deadlock] = Space(Synth.Concat(
            Synth.Render(new Voice(Wave.Square, 880, 880, 0.12, 0.2, Release: 0.03, Cutoff: 3000)),
            Synth.Render(new Voice(Wave.Square, 660, 660, 0.12, 0.2, Release: 0.03, Cutoff: 3000)),
            Synth.Render(new Voice(Wave.Square, 880, 880, 0.12, 0.2, Release: 0.03, Cutoff: 3000)),
            Synth.Render(new Voice(Wave.Square, 660, 660, 0.12, 0.2, Release: 0.06, Cutoff: 3000))));
        d[Sfx.Ejected] = Space(Synth.Render(
            new Voice(Wave.Saw, 1200, 300, 0.25, 0.25, Release: 0.05, Cutoff: 3500, VibratoHz: 12, VibratoDepth: 0.06),
            new Voice(Wave.Saw, 1200, 300, 0.25, 0.25, Release: 0.1, Delay: 0.3, Cutoff: 3500, VibratoHz: 12, VibratoDepth: 0.06)));
        d[Sfx.Cleared] = Space(Arpeggio(new[] { "G5", "C6", "E6", "G6" }, 0.09, Wave.Triangle, 0.3), 0.15, 0.35, 0.35);
        d[Sfx.GameOver] = Space(Arpeggio(new[] { "E5", "C5", "A4", "F4", "D4", "A3" }, 0.16, Wave.Square, 0.18, 2200), 0.2, 0.35, 0.35);
        d[Sfx.ShipSecured] = Space(Synth.Concat(
            Arpeggio(new[] { "C5", "E5", "G5", "C6" }, 0.1, Wave.Pulse, 0.22),
            Arpeggio(new[] { "D5", "F#5", "A5", "D6" }, 0.1, Wave.Pulse, 0.22),
            Synth.Render(
                new Voice(Wave.Pulse, Synth.NoteHz("E6"), Synth.NoteHz("E6"), 0.6, 0.22, Release: 0.6, Duty: 0.3, VibratoHz: 6, VibratoDepth: 0.01, Cutoff: 5000),
                new Voice(Wave.Triangle, Synth.NoteHz("C5"), Synth.NoteHz("C5"), 0.6, 0.25, Release: 0.6))), 0.18, 0.35, 0.35);
        d[Sfx.Spark] = Synth.Render(
            new Voice(Wave.Noise, 0, 0, 0.02, 0.3, Release: 0.03, Cutoff: 8000),
            new Voice(Wave.Sine, 3000, 1800, 0.02, 0.1, Release: 0.03));
        return d;
    }

    private static float[] Arpeggio(string[] notes, double step, Wave wave, double amp, double cutoff = 6000)
    {
        var voices = new List<Voice>();
        for (int i = 0; i < notes.Length; i++)
        {
            double f = Synth.NoteHz(notes[i]);
            bool last = i == notes.Length - 1;
            voices.Add(new Voice(wave, f, f, last ? step * 3 : step, amp, Duty: 0.3, Release: last ? 0.4 : 0.05, Delay: i * step, Cutoff: cutoff));
            voices.Add(new Voice(Wave.Sine, f * 2, f * 2, step, amp * 0.3, Release: 0.08, Delay: i * step));
        }
        return Synth.Render(voices.ToArray());
    }
}
