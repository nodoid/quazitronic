using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework.Audio;
using Orictron.Emulation;

namespace Orictron.Audio;

/// <summary>
/// One streaming software mixer for everything the game plays: the emulated AY-3-8912 in ORIGINAL
/// mode, and the synthesised effects and music of the enhanced game. It renders exactly as many
/// samples as game time has passed, so the store-video capture can record the soundtrack
/// sample-for-sample by rendering offline instead of to the device.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public const int Rate = Synth.Rate;
    private const int MaxPending = 5;

    private sealed class Voice
    {
        public float[] Samples = Array.Empty<float>();
        public double Position;
        public double Step = 1;
        public float Gain;
        public float Pan; // reserved (mono output)
        public bool Loop;
        public int Id;
        public float FadeOut = -1;
    }

    private readonly List<Voice> _voices = new();
    private DynamicSoundEffectInstance? _out;
    private float[] _mix = new float[4096];
    private byte[] _pcm = new byte[8192];
    private double _owed;
    private int _nextId = 1;
    private float _limiter = 1f;

    public bool Available { get; private set; }
    public bool Muted { get; set; }
    public float MasterVolume { get; set; } = 0.9f;
    /// <summary>The emulated sound chip, mixed in while set (ORIGINAL mode).</summary>
    public Ay38912? Chip { get; set; }
    public float ChipGain { get; set; } = 0.8f;

    /// <summary>When set, audio is rendered into this sink instead of the sound card (store-video capture).</summary>
    public Action<ReadOnlySpan<float>>? OfflineSink { get; set; }

    public void Start()
    {
        try
        {
            _out = new DynamicSoundEffectInstance(Rate, AudioChannels.Mono);
            _out.Play();
            Available = true;
        }
        catch (Exception ex) when (ex is NoAudioHardwareException or InvalidOperationException or PlatformNotSupportedException or TypeInitializationException or DllNotFoundException)
        {
            Debug.WriteLine("Orictron: no audio: " + ex.Message);
            Available = false;
        }
    }

    /// <summary>Plays a one-shot sample. Pitch is in octaves (0 = as recorded). Returns a handle for <see cref="Stop"/>.</summary>
    public int Play(float[] samples, float volume = 1f, float pitch = 0f, bool loop = false)
    {
        if (samples.Length == 0) return 0;
        // A burst of identical sounds in one frame is just louder: cap the voice count.
        if (_voices.Count > 40) _voices.RemoveAt(0);
        var v = new Voice
        {
            Samples = samples,
            Gain = volume,
            Step = Math.Pow(2, pitch),
            Loop = loop,
            Id = _nextId++,
        };
        _voices.Add(v);
        return v.Id;
    }

    public bool IsPlaying(int id) => _voices.Exists(v => v.Id == id && v.FadeOut < 0);

    /// <summary>Stops a voice, fading it over a few milliseconds to avoid a click.</summary>
    public void Stop(int id, float fadeSeconds = 0.02f)
    {
        foreach (var v in _voices)
            if (v.Id == id && v.FadeOut < 0) v.FadeOut = Math.Max(1, fadeSeconds * Rate);
    }

    public void SetVolume(int id, float volume)
    {
        foreach (var v in _voices)
            if (v.Id == id) v.Gain = volume;
    }

    public void StopAll()
    {
        foreach (var v in _voices) if (v.FadeOut < 0) v.FadeOut = Rate * 0.02f;
    }

    /// <summary>Mixes and outputs the audio for <paramref name="seconds"/> of game time.</summary>
    public void Update(float seconds)
    {
        _owed += seconds * Rate;
        int n = (int)_owed;
        if (n <= 0) return;
        _owed -= n;

        if (OfflineSink == null && _out != null)
        {
            // Too far ahead of the sound card (a hitch on resume): drop this slice to keep latency low.
            if (_out.PendingBufferCount >= MaxPending)
            {
                Render(n, discard: true);
                return;
            }
            // Starved: pad with a short cushion of silence first.
            if (_out.PendingBufferCount == 0)
            {
                Array.Clear(_pcm, 0, 1470 * 2);
                _out.SubmitBuffer(_pcm, 0, 1470 * 2);
            }
        }

        var mix = Render(n, discard: false);
        if (OfflineSink != null)
        {
            OfflineSink(mix);
            return;
        }
        if (_out == null) return;
        if (_pcm.Length < n * 2) _pcm = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            short s = (short)(mix[i] * short.MaxValue);
            _pcm[i * 2] = (byte)s;
            _pcm[i * 2 + 1] = (byte)(s >> 8);
        }
        _out.SubmitBuffer(_pcm, 0, n * 2);
    }

    private ReadOnlySpan<float> Render(int n, bool discard)
    {
        if (_mix.Length < n) _mix = new float[n];
        var mix = _mix.AsSpan(0, n);
        mix.Clear();

        if (Chip != null)
        {
            Chip.MixInto(mix, ChipGain);
            // If emulation got ahead (e.g. after a pause), don't let the backlog become latency.
            if (Chip.Queued > Rate / 10) Chip.Drop(Chip.Queued - Rate / 20);
        }

        for (int k = _voices.Count - 1; k >= 0; k--)
        {
            var v = _voices[k];
            bool done = false;
            for (int i = 0; i < n; i++)
            {
                int p = (int)v.Position;
                if (p >= v.Samples.Length)
                {
                    if (!v.Loop) { done = true; break; }
                    v.Position -= v.Samples.Length;
                    p = (int)v.Position;
                }
                float g = v.Gain;
                if (v.FadeOut >= 0)
                {
                    v.FadeOut -= 1;
                    if (v.FadeOut <= 0) { done = true; break; }
                    g *= Math.Min(1f, v.FadeOut / (Rate * 0.02f));
                }
                mix[i] += v.Samples[p] * g;
                v.Position += v.Step;
            }
            if (done) _voices.RemoveAt(k);
        }

        float master = Muted || discard ? 0f : MasterVolume;
        for (int i = 0; i < n; i++)
        {
            // Gentle peak limiter, then soft clip.
            float x = mix[i] * master;
            float a = Math.Abs(x) * _limiter;
            if (a > 0.95f) _limiter = 0.95f / Math.Abs(x);
            else _limiter = Math.Min(1f, _limiter + 0.00002f);
            x *= _limiter;
            mix[i] = MathF.Tanh(x);
        }
        return mix;
    }

    public void Dispose()
    {
        _out?.Stop();
        _out?.Dispose();
        _out = null;
    }
}
