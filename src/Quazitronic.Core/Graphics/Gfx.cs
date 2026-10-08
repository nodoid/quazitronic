using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Quazitronic.Graphics;

/// <summary>
/// 2D drawing in virtual pixels (the screen is 224 high, 240-540 wide) on top of a render target
/// that is a whole multiple of that size. Text comes in the Oric's own pixel font (original look)
/// or the same letters smoothed (enhanced look).
/// </summary>
public sealed class Gfx
{
    private readonly SpriteBatch _sb;
    private readonly Texture2D _pixel;
    private bool _begun;
    private BlendState _blend = BlendState.AlphaBlend;
    private SamplerState _sampler = SamplerState.LinearClamp;

    public Gfx(GraphicsDevice device, BitmapFont font)
    {
        Device = device;
        Font = font;
        _sb = new SpriteBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
        Rounded = TextureFactory.CreateRoundedRect(device);
        Glow = TextureFactory.CreateGlow(device);
        Atlas = TextureFactory.CreateAtlas(device, font);
        Logo = TextureFactory.CreateLogo(device, "ORICTRON");
        Iso = new IsoRenderer(device, Atlas);
    }

    public GraphicsDevice Device { get; }
    public BitmapFont Font { get; }
    public SpriteBatch Batch => _sb;
    public Texture2D Pixel => _pixel;
    public Texture2D Rounded { get; }
    public Texture2D Glow { get; }
    public Texture2D Atlas { get; }
    public Texture2D Logo { get; }
    public IsoRenderer Iso { get; }

    public int Width { get; private set; } = 320;
    public int Height => 224;
    /// <summary>Device pixels per virtual pixel.</summary>
    public int Scale { get; private set; } = 1;
    public float Time { get; set; }
    public RenderTarget2D? Target { get; private set; }

    public void BeginFrame(RenderTarget2D target, int virtualWidth, int scale)
    {
        Target = target;
        Width = virtualWidth;
        Scale = scale;
    }

    private Matrix Transform => Matrix.CreateScale(Scale, Scale, 1);

    /// <summary>Starts (or restarts) the sprite batch with the given blending.</summary>
    public void Begin(BlendState? blend = null, SamplerState? sampler = null)
    {
        if (_begun) _sb.End();
        _blend = blend ?? BlendState.AlphaBlend;
        _sampler = sampler ?? SamplerState.LinearClamp;
        _sb.Begin(SpriteSortMode.Deferred, _blend, _sampler, DepthStencilState.None, RasterizerState.CullNone, null, Transform);
        _begun = true;
    }

    public void End()
    {
        if (_begun) _sb.End();
        _begun = false;
    }

    private void Ensure()
    {
        if (!_begun) Begin();
    }

    public void Additive() => Begin(IsoRenderer.Glow, _sampler);
    public void Alpha() => Begin(BlendState.AlphaBlend, _sampler);
    public void Pixelated() => Begin(_blend, SamplerState.PointClamp);
    public void Smooth() => Begin(_blend, SamplerState.LinearClamp);

    // ---------------------------------------------------------------- shapes

    public void Rect(float x, float y, float w, float h, Color c)
    {
        Ensure();
        _sb.Draw(_pixel, new Vector2(x, y), null, c, 0, Vector2.Zero, new Vector2(w, h), SpriteEffects.None, 0);
    }

    public void Rect(RectangleF r, Color c) => Rect(r.X, r.Y, r.Width, r.Height, c);

    public void Frame(float x, float y, float w, float h, float t, Color c)
    {
        Rect(x, y, w, t, c);
        Rect(x, y + h - t, w, t, c);
        Rect(x, y + t, t, h - 2 * t, c);
        Rect(x + w - t, y + t, t, h - 2 * t, c);
    }

    public void Line(Vector2 a, Vector2 b, float width, Color c)
    {
        Ensure();
        var d = b - a;
        float len = d.Length();
        if (len < 0.001f) return;
        _sb.Draw(_pixel, a, null, c, MathF.Atan2(d.Y, d.X), new Vector2(0, 0.5f), new Vector2(len, width), SpriteEffects.None, 0);
    }

    /// <summary>Vertical gradient (top to bottom).</summary>
    public void Gradient(float x, float y, float w, float h, Color top, Color bottom, int steps = 32)
    {
        float sh = h / steps;
        for (int i = 0; i < steps; i++)
            Rect(x, y + i * sh, w, sh + 0.05f, Color.Lerp(top, bottom, (i + 0.5f) / steps));
    }

    /// <summary>Rounded rectangle (nine-slice); radius in virtual pixels.</summary>
    public void RoundRect(RectangleF r, float radius, Color c)
    {
        Ensure();
        radius = MathF.Min(radius, MathF.Min(r.Width, r.Height) / 2);
        const int s = 16, n = 64;
        float k = radius / s;
        var srcs = new[] { 0, s, n - s, n };
        var xs = new[] { r.X, r.X + radius, r.Right - radius, r.Right };
        var ys = new[] { r.Y, r.Y + radius, r.Bottom - radius, r.Bottom };
        for (int iy = 0; iy < 3; iy++)
            for (int ix = 0; ix < 3; ix++)
            {
                var src = new Rectangle(srcs[ix], srcs[iy], srcs[ix + 1] - srcs[ix], srcs[iy + 1] - srcs[iy]);
                float dw = xs[ix + 1] - xs[ix], dh = ys[iy + 1] - ys[iy];
                if (dw <= 0 || dh <= 0) continue;
                _sb.Draw(Rounded, new Vector2(xs[ix], ys[iy]), src, c, 0, Vector2.Zero, new Vector2(dw / src.Width, dh / src.Height), SpriteEffects.None, 0);
            }
        _ = k;
    }

    /// <summary>A glass panel: dark translucent fill, a lit rim and a soft top sheen.</summary>
    public void Panel(RectangleF r, Color rim, float alpha = 0.82f, float radius = 6)
    {
        RoundRect(r.Inflate(1.2f), radius + 1.2f, rim * 0.9f);
        RoundRect(r, radius, new Color(6, 8, 20) * alpha);
        RoundRect(new RectangleF(r.X + 2, r.Y + 2, r.Width - 4, MathF.Min(r.Height * 0.4f, 14)), radius - 2, Color.White * 0.06f);
    }

    public void GlowAt(Vector2 centre, float radius, Color c)
    {
        Ensure();
        _sb.Draw(Glow, centre, null, c, 0, new Vector2(Glow.Width / 2f), radius * 2 / Glow.Width, SpriteEffects.None, 0);
    }

    public void AtlasSprite(AtlasCell cell, Vector2 centre, Vector2 size, Color c, float rotation = 0)
    {
        Ensure();
        int i = (int)cell;
        var src = new Rectangle((i % TextureFactory.AtlasCells) * TextureFactory.Cell, (i / TextureFactory.AtlasCells) * TextureFactory.Cell, TextureFactory.Cell, TextureFactory.Cell);
        _sb.Draw(Atlas, centre, src, c, rotation, new Vector2(TextureFactory.Cell / 2f), size / TextureFactory.Cell, SpriteEffects.None, 0);
    }

    public void Texture(Texture2D tex, RectangleF dest, Color c)
    {
        Ensure();
        _sb.Draw(tex, new Vector2(dest.X, dest.Y), null, c, 0, Vector2.Zero, new Vector2(dest.Width / tex.Width, dest.Height / tex.Height), SpriteEffects.None, 0);
    }

    // ---------------------------------------------------------------- text

    public static float TextWidth(string s, float scale = 1) => s.Length * BitmapFont.CharWidth * scale;

    /// <summary>Oric pixel-font text at a whole-number scale (draw inside <see cref="Pixelated"/>).</summary>
    public void PixelText(string s, float x, float y, Color c, int scale = 1)
    {
        Ensure();
        Font.Draw(_sb, s, x, y, c, scale, Width + 64);
    }

    public void PixelTextCentred(string s, float cx, float y, Color c, int scale = 1) =>
        PixelText(s, MathF.Round(cx - TextWidth(s, scale) / 2), y, c, scale);

    /// <summary>Smoothed text with an optional drop shadow.</summary>
    public void Text(string s, float x, float y, Color c, float scale = 1, bool shadow = true)
    {
        Ensure();
        if (shadow) Font.DrawSmooth(_sb, s, new Vector2(x + 0.6f * scale, y + 0.6f * scale), Color.Black * (c.A / 255f) * 0.8f, scale);
        Font.DrawSmooth(_sb, s, new Vector2(x, y), c, scale);
    }

    public void TextCentred(string s, float cx, float y, Color c, float scale = 1, bool shadow = true) =>
        Text(s, cx - TextWidth(s, scale) / 2, y, c, scale, shadow);

    public void TextRight(string s, float right, float y, Color c, float scale = 1, bool shadow = true) =>
        Text(s, right - TextWidth(s, scale), y, c, scale, shadow);

    /// <summary>Glowing smoothed text: a soft halo then the letters.</summary>
    public void GlowText(string s, float cx, float y, Color c, float scale = 1, float glow = 0.5f)
    {
        float x = cx - TextWidth(s, scale) / 2;
        Ensure();
        var halo = c * (glow * 0.22f);
        foreach (var o in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1), new Vector2(-0.7f, -0.7f), new Vector2(0.7f, 0.7f), new Vector2(0.7f, -0.7f), new Vector2(-0.7f, 0.7f) })
            Font.DrawSmooth(_sb, s, new Vector2(x, y) + o * scale * 0.9f, halo, scale);
        Font.DrawSmooth(_sb, s, new Vector2(x, y), c, scale);
    }

    /// <summary>A 3D droid of the given class standing at a virtual-screen position (menus, the transfer battle).</summary>
    public void DroidPortrait(int type, Vector2 screen, float zoom, float time, float flash = 0)
    {
        End();
        var origin = new Vector3(0, 0, 0);
        Iso.SetCamera(IsoRenderer.ToIso(origin + new Vector3(0, 0, 8)), screen, zoom, Width, Height);
        Iso.BeginDynamic();
        Iso.Detail = zoom * Scale > 8 ? 3 : 2;
        Iso.AddDroid(origin, type, time, flash);
        Iso.Detail = 1;
        Iso.DrawDynamicOnly();
    }

    private Texture2D? _oricTexture;
    private readonly uint[] _oricPixels = new uint[Original.OricScreen.Width * Original.OricScreen.Height];

    /// <summary>Shows an Oric-format screen with hard pixels in <paramref name="dest"/>.</summary>
    public void DrawOric(Original.OricScreen screen, RectangleF dest)
    {
        screen.Decode(_oricPixels);
        End();
        _oricTexture ??= new Texture2D(Device, Original.OricScreen.Width, Original.OricScreen.Height);
        Device.Textures[0] = null;
        _oricTexture.SetData(_oricPixels);
        Begin(BlendState.Opaque, SamplerState.PointClamp);
        _sb.Draw(_oricTexture, new Vector2(dest.X, dest.Y), null, Color.White, 0, Vector2.Zero,
            new Vector2(dest.Width / Original.OricScreen.Width, dest.Height / Original.OricScreen.Height), SpriteEffects.None, 0);
        Begin(BlendState.AlphaBlend, SamplerState.PointClamp);
    }

    /// <summary>The enhanced ORICTRON logo, centred, <paramref name="height"/> virtual pixels tall.</summary>
    public void DrawLogo(float cx, float y, float height, float alpha = 1)
    {
        Ensure();
        float s = height / Logo.Height;
        _sb.Draw(Logo, new Vector2(cx - Logo.Width * s / 2, y), null, Color.White * alpha, 0, Vector2.Zero, s, SpriteEffects.None, 0);
    }
}
