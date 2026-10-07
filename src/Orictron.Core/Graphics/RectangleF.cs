using Microsoft.Xna.Framework;

namespace Orictron.Graphics;

/// <summary>Float rectangle in virtual pixels.</summary>
public readonly record struct RectangleF(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public Vector2 Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(Vector2 p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;
    public RectangleF Inflate(float d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);
}
