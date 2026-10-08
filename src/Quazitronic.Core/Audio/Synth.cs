using System;

namespace Quazitronic.Audio;

public enum Wave { Square, Pulse, Triangle, Saw, Sine, Noise }

/// <summary>
/// One synthesised note or effect layer. Frequencies slide exponentially from <see cref="From"/> to
/// <see cref="To"/>; <see cref="Cutoff"/> (Hz, 0 = off) runs a resonant-free low-pass over it.
/// </summary>
public sealed record Voice(
    Wave Wave,
    double From,
    double To,
    double Seconds,
    double Amp = 0.3,
    double Attack = 0.004,
    double Decay = 0.05,
    double Sustain = 0.7,
    double Release = 0.05,
    double Duty = 0.5,
    double VibratoHz = 0,
    double VibratoDepth = 0,
    double Cutoff = 0,
    double CutoffEnd = -1,
    double Delay = 0,
    /// <summary>Hold the noise sample for this many samples (AY-style grainy noise when large).</summary>
    int NoiseHold = 1);

/// <summary>Tiny offline synthesiser: renders <see cref="Voice"/>s into mono float buffers.</summary>
public static class Synth
{
    public const int Rate = 44100;

    public static int Samples(double seconds) => (int)Math.Ceiling(seconds * Rate);

    /// <summary>Renders voices (each with its own start delay) mixed into one buffer.</summary>
    public static float[] Render(params Voice[] voices)
    {
        double end = 0;
        foreach (var v in voices) end = Math.Max(end, v.Delay + v.Seconds + v.Release);
        var buf = new float[Samples(end) + 1];
        var rng = new Random(1983);
        foreach (var v in voices) Add(buf, v, rng);
        return buf;
    }

    public static void Add(float[] buf, Voice v, Random rng, int offset = -1)
    {
        int start = offset >= 0 ? offset : Samples(v.Delay);
        int n = Samples(v.Seconds + v.Release);
        int held = Samples(v.Seconds);
        double phase = 0;
        double lp = 0;
        float noise = 0;
        double ratio = v.From > 0 ? v.To / v.From : 1;
        double cutEnd = v.CutoffEnd < 0 ? v.Cutoff : v.CutoffEnd;
        for (int i = 0; i < n && start + i < buf.Length; i++)
        {
            double t = (double)i / Rate;
            double u = Math.Min(1, (double)i / Math.Max(1, held));
            double f = v.From * Math.Pow(ratio, u);
            if (v.VibratoDepth > 0 && t > 0.06) f *= 1 + v.VibratoDepth * Math.Sin(2 * Math.PI * v.VibratoHz * t);
            phase += f / Rate;
            double p = phase - Math.Floor(phase);
            double s;
            switch (v.Wave)
            {
                case Wave.Square: s = p < 0.5 ? 1 : -1; break;
                case Wave.Pulse: s = p < v.Duty ? 1 : -1; break;
                case Wave.Triangle: s = 4 * Math.Abs(p - 0.5) - 1; break;
                case Wave.Saw: s = 2 * p - 1; break;
                case Wave.Sine: s = Math.Sin(2 * Math.PI * p); break;
                default:
                    if (i % Math.Max(1, v.NoiseHold) == 0) noise = (float)(rng.NextDouble() * 2 - 1);
                    s = noise;
                    break;
            }
            if (v.Cutoff > 0)
            {
                double c = v.Cutoff + (cutEnd - v.Cutoff) * u;
                double a = 1 - Math.Exp(-2 * Math.PI * c / Rate);
                lp += a * (s - lp);
                s = lp;
            }
            buf[start + i] += (float)(s * v.Amp * Envelope(i, held, v));
        }
    }

    private static double Envelope(int i, int held, Voice v)
    {
        double t = (double)i / Rate;
        double held_t = (double)held / Rate;
        if (t < v.Attack) return t / v.Attack;
        if (t < held_t)
        {
            double d = t - v.Attack;
            return d < v.Decay ? 1 - (1 - v.Sustain) * d / v.Decay : v.Sustain;
        }
        double level = held_t - v.Attack < v.Decay ? 1 - (1 - v.Sustain) * (held_t - v.Attack) / v.Decay : v.Sustain;
        return v.Release <= 0 ? 0 : Math.Max(0, level * (1 - (t - held_t) / v.Release));
    }

    /// <summary>Feedback echo: adds space to enhanced sounds.</summary>
    public static float[] Echo(float[] dry, double delaySeconds, double feedback, double mix, double tailSeconds = 0)
    {
        int d = Samples(delaySeconds);
        var buf = new float[dry.Length + Samples(tailSeconds)];
        Array.Copy(dry, buf, dry.Length);
        var wet = new float[buf.Length];
        for (int i = d; i < buf.Length; i++)
            wet[i] = (float)((buf[i - d] + wet[i - d]) * feedback);
        for (int i = 0; i < buf.Length; i++) buf[i] += (float)(wet[i] * mix);
        return buf;
    }

    /// <summary>Gentle saturation so layered sounds never clip harshly.</summary>
    public static float[] SoftClip(float[] buf, float drive = 1f)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = MathF.Tanh(buf[i] * drive);
        return buf;
    }

    public static float[] Concat(params float[][] parts)
    {
        int n = 0;
        foreach (var p in parts) n += p.Length;
        var buf = new float[n];
        int o = 0;
        foreach (var p in parts) { p.CopyTo(buf, o); o += p.Length; }
        return buf;
    }

    public static float[] Silence(double seconds) => new float[Samples(seconds)];

    /// <summary>MIDI-style note name ("C4", "F#5", "Bb3") to Hz.</summary>
    public static double NoteHz(string name)
    {
        int i = 0;
        int semitone = char.ToUpperInvariant(name[i++]) switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11,
            _ => throw new FormatException(name),
        };
        if (i < name.Length && name[i] == '#') { semitone++; i++; }
        else if (i < name.Length && name[i] == 'b') { semitone--; i++; }
        int octave = int.Parse(name.AsSpan(i), System.Globalization.CultureInfo.InvariantCulture);
        int midi = (octave + 1) * 12 + semitone;
        return 440.0 * Math.Pow(2, (midi - 69) / 12.0);
    }

    public static byte[] ToPcm16(float[] samples, float gain = 1f)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(Math.Clamp(samples[i] * gain, -1f, 1f) * short.MaxValue);
            bytes[i * 2] = (byte)s;
            bytes[i * 2 + 1] = (byte)(s >> 8);
        }
        return bytes;
    }
}
