using System;
using System.Threading.Tasks;

namespace Quazitronic.Audio;

public enum Song { Title, Deck }

/// <summary>
/// The enhanced game's music, composed as note lists and rendered with <see cref="Synth"/> into seamless
/// loops: a driving synth theme for the title, and a darker, quieter loop for the decks.
/// Rendering takes a moment, so it runs in the background and the music starts when it's ready.
/// </summary>
public sealed class Music
{
    private readonly AudioEngine _engine;
    private readonly Task<float[]> _title;
    private readonly Task<float[]> _deck;
    private int _voice;
    private Song? _want;
    private Song? _playing;

    public Music(AudioEngine engine)
    {
        _engine = engine;
        _title = Task.Run(() => Render(Song.Title));
        _deck = Task.Run(() => Render(Song.Deck));
    }

    public bool Enabled { get; set; } = true;
    public float Volume { get; set; } = 0.5f;
    public Song? Current => _want;

    public void Play(Song song)
    {
        _want = song;
        Update();
    }

    public void Stop()
    {
        _want = null;
        Update();
    }

    /// <summary>Call every frame: starts the wanted song once rendered, stops it when not wanted.</summary>
    public void Update()
    {
        Song? target = Enabled ? _want : null;
        if (target == _playing) return;
        if (_voice != 0) _engine.Stop(_voice, 0.4f);
        _voice = 0;
        _playing = null;
        if (target is not { } s) return;
        var task = s == Song.Title ? _title : _deck;
        if (!task.IsCompleted) return; // try again next frame
        _voice = _engine.Play(task.Result, Volume * (s == Song.Deck ? 0.7f : 1f), loop: true);
        _playing = s;
    }

    /// <summary>The rendered loop (for the capture tool's soundtrack and the tests).</summary>
    public static float[] Render(Song song) => song == Song.Title ? TitleTheme() : DeckTheme();

    // ---------------------------------------------------------------- composition

    private sealed class Track
    {
        public readonly float[] Buffer;
        public readonly int Length;
        private readonly double _beat;
        private readonly Random _rng = new(1986);

        public Track(double bpm, int bars)
        {
            _beat = 60.0 / bpm;
            Length = Synth.Samples(bars * 4 * _beat);
            Buffer = new float[Length + Synth.Samples(2)];
        }

        public void Note(double beat, Voice v) => Synth.Add(Buffer, v, _rng, Synth.Samples(beat * _beat));

        public double Seconds(double beats) => beats * _beat;

        /// <summary>Folds the tail past the loop point back onto the start, for a seamless loop.</summary>
        public float[] Loop(float gain)
        {
            var o = new float[Length];
            for (int i = 0; i < Buffer.Length; i++) o[i % Length] += Buffer[i];
            for (int i = 0; i < o.Length; i++) o[i] = MathF.Tanh(o[i] * gain);
            return o;
        }
    }

    private static double Hz(string n) => Synth.NoteHz(n);

    private static float[] TitleTheme()
    {
        var t = new Track(126, 16);
        string[][] chords =
        {
            new[] { "A2", "A3", "C4", "E4" }, new[] { "F2", "F3", "A3", "C4" },
            new[] { "C3", "C4", "E4", "G4" }, new[] { "G2", "G3", "B3", "D4" },
        };
        for (int bar = 0; bar < 16; bar++)
        {
            var ch = chords[bar % 4];
            double b0 = bar * 4;
            // Bass: driving eighths with an octave jump.
            for (int k = 0; k < 8; k++)
            {
                double f = Hz(ch[0]) * (k % 4 == 3 ? 2 : 1);
                t.Note(b0 + k * 0.5, new Voice(Wave.Saw, f, f, t.Seconds(0.4), 0.22, Attack: 0.003, Decay: 0.1, Sustain: 0.5, Release: 0.05, Cutoff: 900, CutoffEnd: 300));
            }
            // Arpeggio: sixteenths over the chord, from bar 2.
            if (bar >= 2)
                for (int k = 0; k < 16; k++)
                {
                    double f = Hz(ch[1 + k % 3]) * (k % 8 >= 4 ? 2 : 1);
                    t.Note(b0 + k * 0.25, new Voice(Wave.Pulse, f, f, t.Seconds(0.18), 0.08, Duty: 0.25, Attack: 0.002, Decay: 0.08, Sustain: 0.4, Release: 0.06, Cutoff: 3500));
                }
            // Pad: two detuned saws, slow attack.
            for (int v = 1; v < 4; v++)
            {
                double f = Hz(ch[v]);
                t.Note(b0, new Voice(Wave.Saw, f * 1.003, f * 1.003, t.Seconds(3.8), 0.035, Attack: 0.5, Decay: 0.5, Sustain: 0.8, Release: 0.4, Cutoff: 1400));
                t.Note(b0, new Voice(Wave.Saw, f * 0.997, f * 0.997, t.Seconds(3.8), 0.035, Attack: 0.5, Decay: 0.5, Sustain: 0.8, Release: 0.4, Cutoff: 1400));
            }
            // Drums from bar 4: kick on 1 and 3, snare on 2 and 4, eighth hats.
            if (bar >= 4)
            {
                for (int k = 0; k < 4; k++)
                {
                    if (k % 2 == 0)
                        t.Note(b0 + k, new Voice(Wave.Sine, 140, 42, 0.12, 0.55, Attack: 0.001, Decay: 0.08, Sustain: 0.3, Release: 0.08));
                    else
                    {
                        t.Note(b0 + k, new Voice(Wave.Noise, 0, 0, 0.09, 0.22, Attack: 0.001, Decay: 0.05, Sustain: 0.3, Release: 0.08, Cutoff: 5000));
                        t.Note(b0 + k, new Voice(Wave.Triangle, 220, 160, 0.06, 0.15, Release: 0.04));
                    }
                }
                for (int k = 0; k < 8; k++)
                    t.Note(b0 + k * 0.5 + 0.25, new Voice(Wave.Noise, 0, 0, 0.015, 0.05 + (k % 2) * 0.02, Attack: 0.001, Release: 0.03));
            }
        }
        // Lead melody over bars 8-15.
        string[] melody =
        {
            "E5", "-", "D5", "C5", "-", "B4", "C5", "-", "A4", "-", "-", "-", "C5", "D5", "E5", "-",
            "F5", "-", "E5", "D5", "-", "C5", "A4", "-", "C5", "-", "-", "-", "-", "-", "-", "-",
            "E5", "-", "G5", "-", "E5", "D5", "C5", "-", "D5", "-", "-", "-", "B4", "C5", "D5", "-",
            "E5", "-", "D5", "C5", "B4", "-", "A4", "-", "A4", "-", "-", "-", "-", "-", "-", "-",
        };
        for (int i = 0; i < melody.Length; i++)
        {
            if (melody[i] == "-") continue;
            int len = 1;
            while (i + len < melody.Length && melody[i + len] == "-") len++;
            double f = Hz(melody[i]);
            t.Note(32 + i * 0.5, new Voice(Wave.Pulse, f, f, t.Seconds(len * 0.5) * 0.9, 0.11, Duty: 0.4, Attack: 0.01, Decay: 0.1, Sustain: 0.7, Release: 0.15, VibratoHz: 5.5, VibratoDepth: 0.008, Cutoff: 4500));
            t.Note(32 + i * 0.5, new Voice(Wave.Sine, f * 2, f * 2, t.Seconds(len * 0.5) * 0.9, 0.04, Attack: 0.01, Release: 0.15));
        }
        var dry = t.Loop(1.2f);
        return LoopEcho(dry, t.Seconds(0.75), 0.3, 0.25);
    }

    private static float[] DeckTheme()
    {
        var t = new Track(96, 8);
        string[] roots = { "D2", "D2", "Bb1", "C2", "D2", "D2", "Bb1", "A1" };
        for (int bar = 0; bar < 8; bar++)
        {
            double b0 = bar * 4;
            double r = Hz(roots[bar]);
            // Low pulse: a heartbeat on the root.
            for (int k = 0; k < 4; k++)
                t.Note(b0 + k, new Voice(Wave.Triangle, r * 2, r * 2, t.Seconds(0.3), 0.18, Attack: 0.005, Decay: 0.2, Sustain: 0.3, Release: 0.2));
            // Drone pad.
            foreach (double m in new[] { 1.0, 1.5, 2.0 })
                t.Note(b0, new Voice(Wave.Saw, r * 2 * m * 1.002, r * 2 * m, t.Seconds(4), 0.03, Attack: 1.0, Sustain: 0.9, Release: 0.8, Cutoff: 500, CutoffEnd: 900));
            // Sparse computer bleeps.
            int seed = bar * 7 + 3;
            for (int k = 0; k < 3; k++)
            {
                double f = Hz(new[] { "A5", "D6", "F5", "E6", "C6" }[(seed + k * 3) % 5]);
                t.Note(b0 + 0.5 + k * 1.25, new Voice(Wave.Sine, f, f, 0.05, 0.05, Release: 0.15));
            }
            // Soft hats.
            for (int k = 0; k < 8; k++)
                t.Note(b0 + k * 0.5, new Voice(Wave.Noise, 0, 0, 0.01, 0.025, Release: 0.03));
        }
        var dry = t.Loop(1.1f);
        return LoopEcho(dry, t.Seconds(0.75), 0.4, 0.35);
    }

    /// <summary>Feedback echo applied around the loop, so the repeats wrap seamlessly.</summary>
    private static float[] LoopEcho(float[] x, double delaySeconds, double feedback, double mix)
    {
        int d = Synth.Samples(delaySeconds);
        int n = x.Length;
        var wet = new float[n];
        for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < n; i++)
            {
                int j = (i - d + n) % n;
                wet[i] = (float)((x[j] + wet[j]) * feedback);
            }
        var o = new float[n];
        for (int i = 0; i < n; i++) o[i] = MathF.Tanh(x[i] + (float)(wet[i] * mix));
        return o;
    }
}
