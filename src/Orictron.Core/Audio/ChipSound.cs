using System;
using System.Collections.Generic;
using Orictron.Game;

namespace Orictron.Audio;

/// <summary>
/// ORIGINAL mode's sound: the Oric version's effects, recreated. The game's own sound code is ported
/// as it was (a falling laser tone on voice A, noise bursts on voice B, blips on voice C, each fading
/// one step per frame), and played by a small three-voice square-wave and noise synthesiser with the
/// logarithmic volume steps and pitch formula of the Oric's sound chip.
/// </summary>
public sealed class ChipSound
{
    private const double ChipClock = 1_000_000;
    // Volume steps 0-15, normalised.
    private static readonly float[] Levels =
    {
        0f, 0.0137f, 0.0205f, 0.0291f, 0.0423f, 0.0618f, 0.0847f, 0.1369f,
        0.1691f, 0.2647f, 0.3527f, 0.4499f, 0.5704f, 0.6873f, 0.8482f, 1f,
    };

    // The game's sound state (main.c: sa_vol, sa_per, sb_vol, sc_vol, sc_per and the noise period).
    private int _aVol, _aPer, _bVol, _cVol, _cPer, _noisePer = 20;

    // Synth state.
    private readonly int _rate;
    private double _phaseA, _phaseC, _noiseClock;
    private int _lfsr = 1;
    private bool _noise;
    private float _dc;

    public ChipSound(int sampleRate = AudioEngine.Rate) => _rate = sampleRate;

    /// <summary>Voice A's period, voice C's period and the three volumes (for the tests).</summary>
    public (int APeriod, int AVolume, int BVolume, int CPeriod, int CVolume) State => (_aPer, _aVol, _bVol, _cPer, _cVol);

    // ---------------------------------------------------------------- the game's sound code

    public void Shot() { _aPer = 40; _aVol = 12; }
    public void Boom() { _noisePer = 28; _bVol = 15; }
    public void Ram() { _noisePer = 8; _bVol = 9; }
    public void Blip(int period) { _cPer = period & 0xFF; _cVol = 10; }

    public void Off() => _aVol = _bVol = _cVol = 0;

    /// <summary>Once per game frame, before the frame's own sounds (snd_update in wait_frame).</summary>
    public void Frame()
    {
        if (_aVol > 0) { _aVol--; _aPer = (_aPer + 12) & 0xFF; }
        if (_bVol > 0) _bVol--;
        if (_cVol > 0) _cVol--;
    }

    /// <summary>Plays one game frame's cues the way the original's sfx_* calls did.</summary>
    public void Play(IReadOnlyList<CueEvent> cues)
    {
        foreach (var c in cues)
        {
            switch (c.Cue)
            {
                case Cue.Shot: Shot(); break;
                case Cue.Boom: Boom(); break;
                case Cue.Ram: Ram(); break;
                case Cue.EnemyShot: Blip(200); break;
                case Cue.Lift: Blip(60); break;
                case Cue.Blip:
                case Cue.Charge:
                case Cue.Flash:
                case Cue.PulseFire:
                case Cue.EnemyPulse:
                case Cue.CellTaken:
                case Cue.EnemyCellTaken:
                    Blip(c.Param);
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- synthesis

    /// <summary>Adds <paramref name="dest"/>.Length samples of the current sound into the buffer.</summary>
    public void MixInto(Span<float> dest, float gain)
    {
        double fA = ChipClock / 16 / Math.Max(1, _aPer), fC = ChipClock / 16 / Math.Max(1, _cPer);
        double fN = ChipClock / 16 / Math.Max(1, _noisePer & 0x1F);
        float vA = Levels[Math.Clamp(_aVol, 0, 15)], vB = Levels[Math.Clamp(_bVol, 0, 15)], vC = Levels[Math.Clamp(_cVol, 0, 15)];
        const int Sub = 4; // supersample the square edges a little
        for (int i = 0; i < dest.Length; i++)
        {
            float acc = 0;
            for (int k = 0; k < Sub; k++)
            {
                _phaseA += fA / (_rate * Sub);
                _phaseC += fC / (_rate * Sub);
                _noiseClock += fN / (_rate * Sub);
                while (_noiseClock >= 1)
                {
                    _noiseClock -= 1;
                    int bit = (_lfsr ^ (_lfsr >> 3)) & 1;
                    _lfsr = (_lfsr >> 1) | (bit << 16);
                    _noise = (_lfsr & 1) != 0;
                }
                _phaseA -= Math.Floor(_phaseA);
                _phaseC -= Math.Floor(_phaseC);
                if (_phaseA < 0.5) acc += vA;
                if (_noise) acc += vB;
                if (_phaseC < 0.5) acc += vC;
            }
            float v = acc / Sub / 3f;
            _dc += (v - _dc) * 0.0015f;
            dest[i] += (v - _dc) * 1.6f * gain;
        }
    }
}
