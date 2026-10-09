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
/// Turns device tilt into a virtual analogue stick. Level is the device lying flat, face up, like a
/// marble-rolling game. Tipping the top edge down (away from the player) gives +Y (up the screen),
/// tipping the right side down gives +X (right).
/// </summary>
public sealed class TiltController
{
    /// <summary>Tilt (radians, about 20 degrees) for full deflection.</summary>
    public const float FullAngle = 0.35f;
    /// <summary>Small tilts are ignored so a slightly unsteady hand doesn't drift.</summary>
    public const float DeadZone = 0.2f;

    private Vector3 _screenUp = Vector3.UnitX;
    private Vector3 _screenRight = -Vector3.UnitY;

    public Vector2 Stick { get; private set; }

    /// <summary>Which way up the screen is. Landscape left (device top to the left) puts the device's
    /// +X edge at the top of the screen; landscape right is the other way round.</summary>
    public DisplayOrientation Orientation
    {
        set
        {
            _screenUp = value == DisplayOrientation.LandscapeRight ? -Vector3.UnitX : Vector3.UnitX;
            // right x up = out of the screen (+Z), so right = up x Z.
            _screenRight = Vector3.Cross(_screenUp, Vector3.UnitZ);
        }
    }

    /// <param name="g">Gravity in the device frame.</param>
    public Vector2 Update(Vector3 g)
    {
        float y = Shape(-Pitch(g) / FullAngle);
        float x = Shape(Roll(g) / FullAngle);
        Stick = new Vector2(x, y);
        return Stick;
    }

    private static float Shape(float v)
    {
        float a = MathF.Abs(v);
        if (a < DeadZone) return 0;
        return MathF.Sign(v) * MathF.Min(1, (a - DeadZone) / (1 - DeadZone));
    }

    /// <summary>Tip of the screen from flat: 0 face up, positive with the top edge raised (towards the player).</summary>
    private float Pitch(Vector3 g) => MathF.Atan2(-Vector3.Dot(g, _screenUp), -g.Z);

    /// <summary>Sideways tip: positive when the right-hand side of the screen dips.</summary>
    private float Roll(Vector3 g) => MathF.Asin(Math.Clamp(Vector3.Dot(g, _screenRight), -1f, 1f));
}
