using System;
using Microsoft.Xna.Framework;

namespace Quazitronic.Input;

/// <summary>
/// Supplies the device's gravity vector, in units of g, in the device's own frame using
/// Apple's convention: +X right, +Y towards the top edge (in the device's natural orientation),
/// +Z out of the screen. A phone standing upright reads (0, -1, 0); lying face up reads (0, 0, -1).
/// Implemented per platform (CoreMotion on iOS, SensorManager on Android).
/// </summary>
public interface ITiltSensor
{
    bool IsAvailable { get; }
    void Start();
    void Stop();
    bool TryRead(out Vector3 gravity);
}

/// <summary>
/// Turns device tilt into a virtual analogue stick. "Level" is however the player is holding the
/// device when <see cref="Calibrate"/> is called (start of play, and on resuming). Tipping the top
/// edge away gives +Y (forward / up), tipping the right side down gives +X (right).
/// </summary>
public sealed class TiltController
{
    /// <summary>Tilt (radians, about 20 degrees) for full deflection.</summary>
    public const float FullAngle = 0.35f;
    /// <summary>Small tilts are ignored so a slightly unsteady hand doesn't drift.</summary>
    public const float DeadZone = 0.12f;

    private Vector3 _screenUp = Vector3.UnitX;
    private Vector3 _screenRight = -Vector3.UnitY;
    private float _neutralPitch, _neutralRoll;
    private bool _calibrated;

    public bool IsCalibrated => _calibrated;
    public Vector2 Stick { get; private set; }

    public void Reset() => _calibrated = false;

    public void Calibrate(Vector3 g)
    {
        // Held tilted towards the player, gravity runs down the screen, so the screen's "up" is the
        // device axis gravity mostly lies along, reversed. That copes with either landscape
        // orientation and with tablets whose natural orientation is landscape.
        if (MathF.Abs(g.X) >= 0.15f || MathF.Abs(g.Y) >= 0.15f)
        {
            _screenUp = MathF.Abs(g.X) >= MathF.Abs(g.Y)
                ? new Vector3(-MathF.Sign(g.X), 0, 0)
                : new Vector3(0, -MathF.Sign(g.Y), 0);
            // right x up = out of the screen (+Z), so right = up x Z.
            _screenRight = Vector3.Cross(_screenUp, Vector3.UnitZ);
        }
        _neutralPitch = Pitch(g);
        _neutralRoll = Roll(g);
        _calibrated = true;
        Stick = Vector2.Zero;
    }

    public Vector2 Update(Vector3 g)
    {
        if (!_calibrated) Calibrate(g);
        float y = Shape((_neutralPitch - Pitch(g)) / FullAngle);
        float x = Shape((Roll(g) - _neutralRoll) / FullAngle);
        Stick = new Vector2(x, y);
        return Stick;
    }

    private static float Shape(float v)
    {
        float a = MathF.Abs(v);
        if (a < DeadZone) return 0;
        return MathF.Sign(v) * MathF.Min(1, (a - DeadZone) / (1 - DeadZone));
    }

    /// <summary>Angle of the screen from flat (0 = face up, pi/2 = upright facing the player).</summary>
    private float Pitch(Vector3 g) => MathF.Atan2(-Vector3.Dot(g, _screenUp), -g.Z);

    /// <summary>Sideways tip: positive when the right-hand side of the screen dips.</summary>
    private float Roll(Vector3 g) => MathF.Asin(Math.Clamp(Vector3.Dot(g, _screenRight), -1f, 1f));
}
