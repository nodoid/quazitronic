using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Orictron.Graphics;

/// <summary>World-space particles for explosions, sparks and teleport shimmer (enhanced look only).</summary>
public sealed class Particles
{
    private enum Kind { Spark, Ember, Smoke, Flash, Shock }

    private struct P
    {
        public Kind Kind;
        public Vector3 Pos, Vel;
        public float Life, MaxLife, Size;
        public Color Colour;
        public float Spin;
    }

    private readonly List<P> _ps = new();
    private readonly Random _rng;

    public Particles(int seed = 7) => _rng = new Random(seed);

    public int Count => _ps.Count;

    public void Clear() => _ps.Clear();

    private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    private Vector3 RandomDir(float up)
    {
        float a = R(0, MathF.Tau);
        float e = R(-0.2f, 1f) * up;
        var v = new Vector3(MathF.Cos(a), MathF.Sin(a), e);
        return Vector3.Normalize(v);
    }

    public void Explosion(Vector3 at, Color tint, bool big)
    {
        int n = big ? 70 : 42;
        var core = at + new Vector3(0, 0, 6);
        _ps.Add(new P { Kind = Kind.Flash, Pos = core, Life = big ? 0.5f : 0.35f, MaxLife = big ? 0.5f : 0.35f, Size = big ? 80 : 56, Colour = new Color(255, 230, 170) });
        _ps.Add(new P { Kind = Kind.Shock, Pos = at + new Vector3(0, 0, 0.5f), Life = 0.45f, MaxLife = 0.45f, Size = big ? 30 : 20, Colour = tint });
        for (int i = 0; i < n; i++)
        {
            var d = RandomDir(1.3f);
            float sp = R(20, big ? 95 : 70);
            _ps.Add(new P
            {
                Kind = i % 3 == 0 ? Kind.Ember : Kind.Spark,
                Pos = core,
                Vel = d * sp,
                Life = R(0.35f, 0.9f),
                MaxLife = 0.9f,
                Size = R(2.5f, 5f),
                Colour = i % 4 == 0 ? Color.White : Color.Lerp(new Color(255, 200, 80), tint, R(0, 0.6f)),
                Spin = R(-6, 6),
            });
        }
        for (int i = 0; i < (big ? 14 : 8); i++)
        {
            _ps.Add(new P
            {
                Kind = Kind.Smoke,
                Pos = core + RandomDir(0.5f) * R(0, 4),
                Vel = RandomDir(0.8f) * R(4, 14) + new Vector3(0, 0, R(6, 16)),
                Life = R(0.8f, 1.6f),
                MaxLife = 1.6f,
                Size = R(10, 20),
                Colour = new Color(40, 36, 46),
                Spin = R(-1, 1),
            });
        }
    }

    public void Sparks(Vector3 at, Color tint, int n = 10)
    {
        for (int i = 0; i < n; i++)
            _ps.Add(new P
            {
                Kind = Kind.Spark,
                Pos = at + new Vector3(0, 0, 7),
                Vel = RandomDir(1f) * R(25, 60),
                Life = R(0.15f, 0.4f),
                MaxLife = 0.4f,
                Size = R(2, 3.5f),
                Colour = Color.Lerp(Color.White, tint, R(0.2f, 0.8f)),
            });
    }

    /// <summary>A column of rising light (lift rides and captures).</summary>
    public void Beam(Vector3 at, Color tint, int n = 30)
    {
        for (int i = 0; i < n; i++)
            _ps.Add(new P
            {
                Kind = Kind.Ember,
                Pos = at + new Vector3(R(-6, 6), R(-6, 6), R(0, 4)),
                Vel = new Vector3(0, 0, R(20, 60)),
                Life = R(0.4f, 1.0f),
                MaxLife = 1.0f,
                Size = R(2, 4),
                Colour = tint,
            });
    }

    public void Update(float dt)
    {
        for (int i = _ps.Count - 1; i >= 0; i--)
        {
            var p = _ps[i];
            p.Life -= dt;
            if (p.Life <= 0) { _ps.RemoveAt(i); continue; }
            switch (p.Kind)
            {
                case Kind.Spark:
                case Kind.Ember:
                    p.Vel.Z -= 90 * dt;
                    p.Vel *= 1 - 1.2f * dt;
                    break;
                case Kind.Smoke:
                    p.Vel *= 1 - 1.5f * dt;
                    p.Size += 10 * dt;
                    break;
            }
            p.Pos += p.Vel * dt;
            _ps[i] = p;
        }
    }

    public void Draw(IsoRenderer iso, float time)
    {
        foreach (var p in _ps)
        {
            float t = p.Life / p.MaxLife;
            switch (p.Kind)
            {
                case Kind.Spark:
                {
                    var tail = p.Pos - p.Vel * 0.035f;
                    iso.AddBolt(tail, p.Pos, p.Size * 0.7f, p.Colour * Math.Min(1, t * 2));
                    break;
                }
                case Kind.Ember:
                    iso.AddGlow(p.Pos, p.Size * 2, p.Colour * Math.Min(1, t * 1.5f));
                    break;
                case Kind.Smoke:
                    iso.AddSprite(p.Pos, p.Size, p.Size, p.Colour * (0.55f * MathF.Min(1, t * 2) * MathF.Min(1, (1 - t) * 6)), AtlasCell.Smoke, p.Spin * time, additive: false);
                    break;
                case Kind.Flash:
                    iso.AddGlow(p.Pos, p.Size * (1.2f - t * 0.4f), p.Colour * t);
                    break;
                case Kind.Shock:
                    iso.AddFloorRing(p.Pos, p.Size * (1.3f - t), p.Colour * t, AtlasCell.Shock);
                    break;
            }
        }
    }
}
