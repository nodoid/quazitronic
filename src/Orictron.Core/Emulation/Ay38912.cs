using System;

namespace Orictron.Emulation;

/// <summary>
/// The General Instrument AY-3-8912 sound chip at the Oric's 1 MHz clock: three square-wave tone
/// channels, a noise generator, the envelope generator and the logarithmic volume DAC. Register 14
/// is its I/O port, which the Oric uses to drive the keyboard columns.
/// <para>
/// Writes are time-stamped in CPU cycles, and the chip renders the audio up to each write before
/// applying it, so sounds come out sample-accurate however the emulator is scheduled.
/// </para>
/// </summary>
public sealed class Ay38912
{
    public const int Clock = 1_000_000;

    // Measured AY DAC levels (normalised).
    private static readonly float[] Levels =
    {
        0f, 0.0137f, 0.0205f, 0.0291f, 0.0423f, 0.0618f, 0.0847f, 0.1369f,
        0.1691f, 0.2647f, 0.3527f, 0.4499f, 0.5704f, 0.6873f, 0.8482f, 1f,
    };

    public readonly byte[] Registers = new byte[16];
    private int _address;

    // Generator state, clocked at Clock / 8.
    private readonly int[] _toneCount = new int[3];
    private readonly bool[] _toneOut = new bool[3];
    private int _noiseCount;
    private bool _noisePrescale;
    private int _lfsr = 1;
    private bool _noiseOut;
    private int _envCount;
    private int _envStep;
    private bool _envHolding;
    private bool _envAttack, _envAlternate, _envHold, _envContinue;

    // Output: rendered samples waiting for the mixer, at SampleRate.
    private readonly float[] _queue = new float[1 << 16];
    private int _qHead, _qCount;
    private long _cycle;
    private double _cyclesPerSample;
    private double _sampleClock;
    private double _subAccumulator;
    private float _dc;

    public Ay38912(int sampleRate = 44100) => SampleRate = sampleRate;

    public int SampleRate
    {
        get => (int)(Clock / _cyclesPerSample);
        set => _cyclesPerSample = Clock / (double)value;
    }

    /// <summary>Samples waiting to be mixed.</summary>
    public int Queued => _qCount;

    /// <summary>Raised for each register write (register, value) - the capture tool and tests listen.</summary>
    public event Action<int, byte>? RegisterWritten;

    public void LatchAddress(byte value) => _address = value & 0x0F;

    public void WriteData(byte value, long cycle)
    {
        RenderUntil(cycle);
        int r = _address;
        Registers[r] = value;
        if (r == 13)
        {
            int shape = value & 0x0F;
            _envContinue = (shape & 8) != 0;
            _envAttack = (shape & 4) != 0;
            _envAlternate = (shape & 2) != 0;
            _envHold = (shape & 1) != 0;
            _envStep = 0;
            _envCount = 0;
            _envHolding = false;
        }
        RegisterWritten?.Invoke(r, value);
    }

    /// <summary>The port A output byte (register 14) - the Oric keyboard's column mask.</summary>
    public byte PortA => Registers[14];

    private int TonePeriod(int ch)
    {
        int p = Registers[ch * 2] | ((Registers[ch * 2 + 1] & 0x0F) << 8);
        return p == 0 ? 1 : p;
    }

    private int EnvelopeLevel()
    {
        int v = _envStep & 15;
        bool up = _envAttack;
        if (_envHolding)
        {
            // Hold at the final level: shapes 9/F end low, B/D end high (A-C alternate patterns).
            return _envFinalHigh ? 15 : 0;
        }
        if (_envAlternate && ((_envStep >> 4) & 1) == 1) up = !up;
        return up ? v : 15 - v;
    }

    private bool _envFinalHigh;

    private void ClockEnvelope()
    {
        if (_envHolding) return;
        _envStep++;
        if (_envStep >= 16)
        {
            if (!_envContinue)
            {
                _envHolding = true;
                _envFinalHigh = false;
            }
            else if (_envHold)
            {
                _envHolding = true;
                bool up = _envAttack;
                if (_envAlternate) up = !up;
                _envFinalHigh = up;
            }
            else if (!_envAlternate)
            {
                _envStep = 0;
            }
            else
            {
                _envStep &= 31;
            }
        }
    }

    /// <summary>One tick of the chip at Clock / 8; returns the mixed output (0..1).</summary>
    private float Tick()
    {
        for (int ch = 0; ch < 3; ch++)
        {
            if (++_toneCount[ch] >= TonePeriod(ch))
            {
                _toneCount[ch] = 0;
                _toneOut[ch] = !_toneOut[ch];
            }
        }
        // Noise runs at half the tone rate.
        _noisePrescale = !_noisePrescale;
        if (_noisePrescale)
        {
            int np = Registers[6] & 0x1F;
            if (np == 0) np = 1;
            if (++_noiseCount >= np)
            {
                _noiseCount = 0;
                int bit = (_lfsr ^ (_lfsr >> 3)) & 1;
                _lfsr = (_lfsr >> 1) | (bit << 16);
                _noiseOut = (_lfsr & 1) != 0;
            }
        }
        int ep = Registers[11] | (Registers[12] << 8);
        if (ep == 0) ep = 1;
        // An envelope step lasts 256 clocks per period unit = 32 ticks here.
        if (++_envCount >= ep * 32)
        {
            _envCount = 0;
            ClockEnvelope();
        }

        byte mixer = Registers[7];
        float sum = 0;
        for (int ch = 0; ch < 3; ch++)
        {
            bool toneOn = (mixer & (1 << ch)) == 0;
            bool noiseOn = (mixer & (8 << ch)) == 0;
            bool high = (!toneOn || _toneOut[ch]) && (!noiseOn || _noiseOut);
            if (!high) continue;
            int vol = Registers[8 + ch];
            int level = (vol & 0x10) != 0 ? EnvelopeLevel() : vol & 0x0F;
            sum += Levels[level];
        }
        return sum / 3f;
    }

    /// <summary>Renders samples covering the chip's time up to <paramref name="cycle"/>.</summary>
    public void RenderUntil(long cycle)
    {
        if (cycle <= _cycle) return;
        long ticks = (cycle - _cycle) / 8;
        if (ticks <= 0) return;
        _cycle += ticks * 8;
        double ticksPerSample = _cyclesPerSample / 8;
        for (long t = 0; t < ticks; t++)
        {
            _subAccumulator += Tick();
            _sampleClock += 1;
            if (_sampleClock >= ticksPerSample)
            {
                // Box-filter the ticks in this sample (an anti-aliasing average), then remove DC.
                float v = (float)(_subAccumulator / _sampleClock);
                _sampleClock -= ticksPerSample;
                _subAccumulator = 0;
                _dc += (v - _dc) * 0.0015f;
                Enqueue((v - _dc) * 1.6f);
            }
        }
    }

    private void Enqueue(float v)
    {
        if (_qCount == _queue.Length) { _qHead = (_qHead + 1) & (_queue.Length - 1); _qCount--; }
        _queue[(_qHead + _qCount) & (_queue.Length - 1)] = v;
        _qCount++;
    }

    /// <summary>Takes up to <paramref name="count"/> rendered samples, adding them into <paramref name="dest"/>.</summary>
    public int MixInto(Span<float> dest, float gain)
    {
        int n = Math.Min(dest.Length, _qCount);
        for (int i = 0; i < n; i++)
        {
            dest[i] += _queue[_qHead] * gain;
            _qHead = (_qHead + 1) & (_queue.Length - 1);
        }
        _qCount -= n;
        return n;
    }

    /// <summary>Discards the oldest <paramref name="n"/> queued samples.</summary>
    public void Drop(int n)
    {
        n = Math.Min(n, _qCount);
        _qHead = (_qHead + n) & (_queue.Length - 1);
        _qCount -= n;
    }

    /// <summary>Drops queued audio (after a pause, so sound doesn't arrive late).</summary>
    public void Flush(long cycle)
    {
        _qCount = 0;
        _cycle = cycle;
    }

    public void Reset()
    {
        Array.Clear(Registers);
        Registers[7] = 0xFF;
        _qCount = 0;
        _cycle = 0;
    }
}
