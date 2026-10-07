using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Orictron.Graphics;

/// <summary>Cells of the enhanced deck's texture atlas (8 x 8 cells of 128 px).</summary>
public enum AtlasCell
{
    FloorPanel, FloorTread, WallPanel, WallBrick, StepPanel, StepBrick, Slab, WallTop,
    Pad, Energiser, Lift, ConsoleScreen, White, Glow, Ring, Arrow,
    Shadow, Spark, Label0, Label1, Label2, Label3, Label4, Label5,
    Label6, Label7, Label8, ConsoleSide, Body, Visor, Shock, Bolt,
    Smoke, Stripe,
}

/// <summary>
/// Draws every texture the enhanced game uses, in code, at start-up: the deck atlas (metal plates,
/// panelled and brick walls, pads, energisers, lift hatches, consoles), glows and sparks, droid
/// class labels, and the ORICTRON logo.
/// </summary>
public static class TextureFactory
{
    public const int Cell = 128;
    public const int AtlasCells = 8;
    public const int AtlasSize = Cell * AtlasCells;

    /// <summary>UV rectangle of a cell, inset half a texel so linear filtering never bleeds.</summary>
    public static Vector4 Uv(AtlasCell c)
    {
        int i = (int)c;
        float x = (i % AtlasCells) * Cell, y = (i / AtlasCells) * Cell;
        float inset = 2.5f; // generous: MSAA extrapolates coordinates past triangle edges
        return new Vector4((x + inset) / AtlasSize, (y + inset) / AtlasSize, (x + Cell - inset) / AtlasSize, (y + Cell - inset) / AtlasSize);
    }

    /// <summary>Float RGBA canvas with anti-aliased primitives.</summary>
    private sealed class Canvas
    {
        public readonly int W, H;
        public readonly Vector4[] P;

        public Canvas(int w, int h, Vector4 fill = default)
        {
            W = w; H = h;
            P = new Vector4[w * h];
            if (fill != default) Array.Fill(P, fill);
        }

        public void Blend(int x, int y, Vector4 c, float a)
        {
            if ((uint)x >= W || (uint)y >= H || a <= 0) return;
            a = Math.Min(1, a) * c.W;
            ref var d = ref P[y * W + x];
            d = new Vector4(d.X + (c.X - d.X) * a, d.Y + (c.Y - d.Y) * a, d.Z + (c.Z - d.Z) * a, d.W + (1 - d.W) * a);
        }

        public void Set(int x, int y, Vector4 c)
        {
            if ((uint)x < W && (uint)y < H) P[y * W + x] = c;
        }

        public void Rect(float x0, float y0, float x1, float y1, Vector4 c)
        {
            for (int y = (int)Math.Floor(y0); y < Math.Ceiling(y1); y++)
                for (int x = (int)Math.Floor(x0); x < Math.Ceiling(x1); x++)
                {
                    float cx = Math.Clamp(Math.Min(x + 1, x1) - Math.Max(x, x0), 0, 1);
                    float cy = Math.Clamp(Math.Min(y + 1, y1) - Math.Max(y, y0), 0, 1);
                    Blend(x, y, c, cx * cy);
                }
        }

        public void Disc(float cx, float cy, float r, Vector4 c)
        {
            for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++)
                for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++)
                {
                    float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    Blend(x, y, c, Math.Clamp(r - d + 0.5f, 0, 1));
                }
        }

        public void Ring(float cx, float cy, float r, float width, Vector4 c)
        {
            for (int y = (int)(cy - r - width); y <= cy + r + width; y++)
                for (int x = (int)(cx - r - width); x <= cx + r + width; x++)
                {
                    float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    Blend(x, y, c, Math.Clamp(width / 2 - Math.Abs(d - r) + 0.5f, 0, 1));
                }
        }

        public void Line(float x0, float y0, float x1, float y1, float width, Vector4 c)
        {
            float minx = Math.Min(x0, x1) - width, maxx = Math.Max(x0, x1) + width;
            float miny = Math.Min(y0, y1) - width, maxy = Math.Max(y0, y1) + width;
            float dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
            for (int y = (int)miny; y <= maxy; y++)
                for (int x = (int)minx; x <= maxx; x++)
                {
                    float px = x + 0.5f - x0, py = y + 0.5f - y0;
                    float t = len2 > 0 ? Math.Clamp((px * dx + py * dy) / len2, 0, 1) : 0;
                    float ex = px - t * dx, ey = py - t * dy;
                    float d = MathF.Sqrt(ex * ex + ey * ey);
                    Blend(x, y, c, Math.Clamp(width / 2 - d + 0.5f, 0, 1));
                }
        }

        public void Polygon(Vector2[] pts, Vector4 c)
        {
            // 4x4 supersampled point-in-polygon fill.
            float minx = float.MaxValue, miny = float.MaxValue, maxx = float.MinValue, maxy = float.MinValue;
            foreach (var p in pts) { minx = Math.Min(minx, p.X); miny = Math.Min(miny, p.Y); maxx = Math.Max(maxx, p.X); maxy = Math.Max(maxy, p.Y); }
            for (int y = (int)miny; y <= maxy; y++)
                for (int x = (int)minx; x <= maxx; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (Inside(pts, x + (sx + 0.5f) / 4, y + (sy + 0.5f) / 4)) hits++;
                    Blend(x, y, c, hits / 16f);
                }
        }

        private static bool Inside(Vector2[] pts, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                if ((pts[i].Y > y) != (pts[j].Y > y) && x < (pts[j].X - pts[i].X) * (y - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X)
                    inside = !inside;
            return inside;
        }

        public void CopyTo(Color[] dest, int destWidth, int ox, int oy, bool premultiply)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var v = Vector4.Clamp(P[y * W + x], Vector4.Zero, Vector4.One);
                    if (premultiply) v = new Vector4(v.X * v.W, v.Y * v.W, v.Z * v.W, v.W);
                    dest[(oy + y) * destWidth + ox + x] = new Color(v);
                }
        }
    }

    private static Vector4 Grey(float v, float a = 1) => new(v, v, v, a);

    private static float Hash(int x, int y, int s)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    // ------------------------------------------------------------------ atlas

    public static Texture2D CreateAtlas(GraphicsDevice device, BitmapFont font)
    {
        var data = new Color[AtlasSize * AtlasSize];
        void Put(AtlasCell cell, Canvas c, bool premul = true)
        {
            int i = (int)cell;
            c.CopyTo(data, AtlasSize, (i % AtlasCells) * Cell, (i / AtlasCells) * Cell, premul);
        }
        const int N = Cell;

        // Floor: brushed plate in four sub-panels with bevelled seams and corner rivets.
        {
            var c = new Canvas(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float v = 0.80f + (Hash(x, y / 3, 1) - 0.5f) * 0.05f + (Hash(x / 9, y, 2) - 0.5f) * 0.04f;
                    c.Set(x, y, Grey(v));
                }
            for (int k = 0; k <= 2; k++)
            {
                float p = k * (N - 1) / 2f;
                c.Rect(p - 2, 0, p + 1, N, Grey(0.45f));
                c.Rect(0, p - 2, N, p + 1, Grey(0.45f));
                c.Rect(p + 1, 0, p + 3, N, Grey(0.95f, 0.6f));
                c.Rect(0, p + 1, N, p + 3, Grey(0.95f, 0.6f));
            }
            foreach (float rx in new[] { 10f, 54f, 74f, 118f })
                foreach (float ry in new[] { 10f, 54f, 74f, 118f })
                {
                    c.Disc(rx + 1, ry + 1, 3.2f, Grey(0.35f));
                    c.Disc(rx, ry, 2.8f, Grey(0.98f));
                }
            Put(AtlasCell.FloorPanel, c);
        }
        // Floor: diamond tread plate.
        {
            var c = new Canvas(N, N, Grey(0.66f));
            for (int y = 0; y < N; y += 16)
                for (int x = 0; x < N; x += 16)
                {
                    bool alt = ((x + y) / 16 & 1) == 0;
                    float cx = x + 8, cy = y + 8;
                    if (alt) c.Line(cx - 4, cy + 4, cx + 4, cy - 4, 4, Grey(0.92f));
                    else c.Line(cx - 4, cy - 4, cx + 4, cy + 4, 4, Grey(0.92f));
                }
            c.Rect(0, 0, N, 3, Grey(0.4f)); c.Rect(0, 0, 3, N, Grey(0.4f));
            c.Rect(0, N - 3, N, N, Grey(0.95f)); c.Rect(N - 3, 0, N, N, Grey(0.95f));
            Put(AtlasCell.FloorTread, c);
        }
        // Wall face (one level high): vertical panels with rivet strips, as on the Spectrum.
        {
            var c = new Canvas(N, N, Grey(0.78f));
            for (int k = 0; k < 4; k++)
            {
                float x0 = k * 32;
                c.Rect(x0, 0, x0 + 3, N, Grey(0.30f));
                c.Rect(x0 + 3, 0, x0 + 6, N, Grey(0.98f));
                for (int y = 8; y < N; y += 18) c.Disc(x0 + 22, y, 2.5f, Grey(0.45f));
                for (int y = 0; y < N; y++) c.Blend((int)x0 + 28, y, Grey(0.55f), 0.6f);
            }
            c.Rect(0, 0, N, 5, Grey(1f));
            c.Rect(0, N - 6, N, N, Grey(0.4f));
            Put(AtlasCell.WallPanel, c);
        }
        // Wall face: brick courses with offset joints.
        {
            var c = new Canvas(N, N, Grey(0.42f));
            for (int row = 0; row < 4; row++)
            {
                float y0 = row * 32 + 2, y1 = row * 32 + 30;
                float off = row % 2 == 0 ? 0 : 21;
                for (float x = -42 + off; x < N; x += 42)
                {
                    c.Rect(Math.Max(0, x + 2), y0, Math.Min(N, x + 40), y1, Grey(0.80f + (Hash((int)x, row, 3) - 0.5f) * 0.12f));
                    c.Rect(Math.Max(0, x + 2), y0, Math.Min(N, x + 40), y0 + 3, Grey(0.95f, 0.7f));
                }
            }
            Put(AtlasCell.WallBrick, c);
        }
        // Step face: a dark band with rivets and a bright lip.
        {
            var c = new Canvas(N, N, Grey(0.26f));
            c.Rect(0, 0, N, 18, Grey(0.95f));
            c.Rect(0, 18, N, 26, Grey(0.12f));
            for (int x = 16; x < N; x += 32)
                for (int y = 50; y < N; y += 40)
                {
                    c.Disc(x + 1, y + 1, 4, Grey(0.05f));
                    c.Disc(x, y, 3.5f, Grey(0.85f));
                }
            Put(AtlasCell.StepPanel, c);
        }
        {
            var c = new Canvas(N, N, Grey(0.30f));
            c.Rect(0, 0, N, 14, Grey(0.92f));
            for (int row = 0; row < 3; row++)
                for (float x = (row % 2) * 16 - 32; x < N; x += 32)
                    c.Rect(Math.Max(0, x + 2), 20 + row * 36, Math.Min(N, x + 30), 52 + row * 36, Grey(0.55f));
            Put(AtlasCell.StepBrick, c);
        }
        // Slab edge: a white lip, a dark band with bolts, then the underside fading away.
        {
            var c = new Canvas(N, N);
            for (int y = 0; y < N; y++)
            {
                float v = y < 14 ? 0.98f : y < 70 ? 0.18f : 0.12f * (1 - (y - 70) / 58f) + 0.03f;
                for (int x = 0; x < N; x++) c.Set(x, y, Grey(v));
            }
            for (int x = 21; x < N; x += 43)
            {
                c.Disc(x, 42, 6, Grey(0.6f));
                c.Disc(x - 1, 41, 3, Grey(1f));
            }
            Put(AtlasCell.Slab, c);
        }
        // Wall top: grating.
        {
            var c = new Canvas(N, N, Grey(0.5f));
            for (int k = 0; k < N; k += 16)
            {
                c.Rect(k, 0, k + 3, N, Grey(0.85f));
                c.Rect(0, k, N, k + 3, Grey(0.85f));
            }
            c.Rect(0, 0, N, 6, Grey(1f)); c.Rect(0, 0, 6, N, Grey(1f));
            Put(AtlasCell.WallTop, c);
        }
        // Step pad: a light plate with a dark rim and the double arrow along the tile's v axis.
        {
            var c = new Canvas(N, N, Grey(0.2f));
            c.Rect(10, 10, N - 10, N - 10, Grey(0.92f));
            DoubleArrow(c, Grey(0.12f), 1f);
            Put(AtlasCell.Pad, c);
        }
        // Energiser: dark plate with a bright concentric ring.
        {
            var c = new Canvas(N, N, Grey(0.16f));
            c.Ring(64, 64, 44, 12, Grey(0.95f));
            c.Ring(64, 64, 24, 4, Grey(0.6f));
            c.Disc(64, 64, 8, Grey(0.85f));
            Put(AtlasCell.Energiser, c);
        }
        // Lift hatch: rings and iris blades.
        {
            var c = new Canvas(N, N, Grey(0.1f));
            c.Ring(64, 64, 54, 6, Grey(0.95f));
            c.Ring(64, 64, 44, 4, Grey(0.4f));
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.PI / 4;
                c.Line(64 + MathF.Cos(a) * 10, 64 + MathF.Sin(a) * 10, 64 + MathF.Cos(a + 0.6f) * 40, 64 + MathF.Sin(a + 0.6f) * 40, 4, Grey(0.75f));
            }
            c.Disc(64, 64, 9, Grey(0.85f));
            Put(AtlasCell.Lift, c);
        }
        // Console screen: dark glass with lines of green text.
        {
            var c = new Canvas(N, N, new Vector4(0.02f, 0.08f, 0.05f, 1));
            for (int row = 0; row < 9; row++)
                for (int k = 0; k < 14; k++)
                    if (Hash(k, row, 5) > 0.35f)
                        c.Rect(10 + k * 8, 12 + row * 12, 16 + k * 8, 18 + row * 12, new Vector4(0.3f, 1f, 0.5f, 0.9f));
            c.Rect(0, 0, N, 5, Grey(0.6f)); c.Rect(0, N - 5, N, N, Grey(0.3f));
            Put(AtlasCell.ConsoleScreen, c);
        }
        Put(AtlasCell.White, new Canvas(N, N, Grey(1)));
        // Glows (premultiplied, for additive blending).
        {
            var c = new Canvas(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64, 64)) / 64f;
                    float a = MathF.Exp(-d * d * 5.5f) * Math.Clamp((1 - d) * 4, 0, 1);
                    c.Set(x, y, new Vector4(1, 1, 1, a));
                }
            Put(AtlasCell.Glow, c);
        }
        {
            var c = new Canvas(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64, 64)) / 64f;
                    float a = MathF.Exp(-MathF.Pow((d - 0.72f) / 0.12f, 2));
                    c.Set(x, y, new Vector4(1, 1, 1, a));
                }
            Put(AtlasCell.Ring, c);
        }
        {
            var c = new Canvas(N, N);
            DoubleArrow(c, Grey(1f), 1f);
            Put(AtlasCell.Arrow, c);
        }
        {
            var c = new Canvas(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64, 64)) / 64f;
                    c.Set(x, y, new Vector4(0, 0, 0, Math.Clamp(1.15f - d * 1.15f, 0, 1) * 0.85f));
                }
            Put(AtlasCell.Shadow, c, premul: false);
        }
        {
            var c = new Canvas(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = Math.Abs(x + 0.5f - 64) / 64f, dy = Math.Abs(y + 0.5f - 64) / 64f;
                    float a = Math.Max(MathF.Exp(-dx * 40) * MathF.Exp(-dy * 3), MathF.Exp(-dy * 40) * MathF.Exp(-dx * 3));
                    a = Math.Max(a, MathF.Exp(-(dx * dx + dy * dy) * 30));
                    c.Set(x, y, new Vector4(1, 1, 1, Math.Clamp(a, 0, 1)));
                }
            Put(AtlasCell.Spark, c);
        }
        // Droid class labels: dark code on a clear background (drawn over the body).
        for (int t = 0; t < 9; t++)
        {
            var c = new Canvas(N, N);
            string code = Game.Droids.Code[t];
            if (code.Length > 0) DrawCode(c, code);
            Put(AtlasCell.Label0 + t, c, premul: false);
        }
        {
            var c = new Canvas(N, N, Grey(0.7f));
            c.Rect(0, 0, N, 10, Grey(0.95f));
            c.Rect(0, 60, N, 64, Grey(0.4f));
            Put(AtlasCell.ConsoleSide, c);
        }
        // Droid body: horizontal seams and a band.
        {
            var c = new Canvas(N, N, Grey(0.92f));
            c.Rect(0, 18, N, 22, Grey(0.6f));
            c.Rect(0, 104, N, 108, Grey(0.6f));
            for (int x = 0; x < N; x += 16) c.Rect(x, 108, x + 2, N, Grey(0.7f));
            Put(AtlasCell.Body, c);
        }
        {
            var c = new Canvas(N, N, Grey(0.08f));
            c.Rect(0, 40, N, 52, Grey(0.5f));
            Put(AtlasCell.Visor, c);
        }
        {
            var c = new Canvas(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64, 64)) / 64f;
                    float a = d > 1 ? 0 : MathF.Pow(d, 6) * Math.Clamp((1 - d) * 12, 0, 1);
                    c.Set(x, y, new Vector4(1, 1, 1, a));
                }
            Put(AtlasCell.Shock, c);
        }
        {
            var c = new Canvas(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f - 64) / 64f, dy = (y + 0.5f - 64) / 64f;
                    float a = MathF.Exp(-dx * dx * 2.2f - dy * dy * 26f);
                    c.Set(x, y, new Vector4(1, 1, 1, a));
                }
            Put(AtlasCell.Bolt, c);
        }
        {
            var c = new Canvas(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64, 64)) / 64f;
                    float n = 0.7f + 0.3f * Hash(x / 6, y / 6, 9);
                    c.Set(x, y, new Vector4(1, 1, 1, Math.Clamp(1 - d, 0, 1) * n));
                }
            Put(AtlasCell.Smoke, c, premul: false);
        }
        {
            var c = new Canvas(N, N, Grey(1f));
            for (int x = 0; x < N; x += 32) c.Rect(x, 0, x + 16, N, Grey(0.55f));
            Put(AtlasCell.Stripe, c);
        }

        var tex = new Texture2D(device, AtlasSize, AtlasSize);
        tex.SetData(data);
        return tex;
    }

    /// <summary>The ⤢ pad arrow: a thick double-headed arrow along the tile's v axis.</summary>
    private static void DoubleArrow(Canvas c, Vector4 col, float scale)
    {
        c.Line(64, 26, 64, 102, 13 * scale, col);
        c.Polygon(new[] { new Vector2(64, 10), new Vector2(88, 40), new Vector2(40, 40) }, col);
        c.Polygon(new[] { new Vector2(64, 118), new Vector2(40, 88), new Vector2(88, 88) }, col);
    }

    private static void DrawCode(Canvas c, string code)
    {
        // The font's 5x7 glyphs, smoothed with Scale2x, 7 px per font pixel.
        const int px = 7;
        int width = code.Length * 6 - 1;
        float ox = 64 - width * px / 2f, oy = 64 - 7 * px / 2f;
        for (int k = 0; k < code.Length; k++)
        {
            var cols = BitmapFont.Columns(code[k]);
            var bits = new bool[5, 7];
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 7; y++)
                    bits[x, y] = (cols[x] & (1 << y)) != 0;
            var big = BitmapFont.Scale2x(bits);
            for (int x = 0; x < 10; x++)
                for (int y = 0; y < 14; y++)
                    if (big[x, y])
                        c.Rect(ox + (k * 6) * px + x * px / 2f, oy + y * px / 2f, ox + (k * 6) * px + (x + 1) * px / 2f, oy + (y + 1) * px / 2f, new Vector4(0.05f, 0.06f, 0.12f, 1));
        }
    }

    // ------------------------------------------------------------------ logo

    /// <summary>
    /// The enhanced ORICTRON logo: the Oric font's letters built from rounded blocks, with a gold
    /// gradient, a bevel highlight, a dark outline and a soft outer glow.
    /// </summary>
    public static Texture2D CreateLogo(GraphicsDevice device, string text, int px = 14)
    {
        int pad = px * 3;
        int w = (text.Length * 6 - 1) * px + pad * 2, h = 7 * px + pad * 2;
        var mask = new float[w * h];
        for (int k = 0; k < text.Length; k++)
        {
            var cols = BitmapFont.Columns(text[k]);
            for (int cx = 0; cx < 5; cx++)
                for (int cy = 0; cy < 7; cy++)
                {
                    if ((cols[cx] & (1 << cy)) == 0) continue;
                    float x0 = pad + (k * 6 + cx) * px, y0 = pad + cy * px;
                    bool L = cx > 0 && (cols[cx - 1] & (1 << cy)) != 0, R = cx < 4 && (cols[cx + 1] & (1 << cy)) != 0;
                    bool U = cy > 0 && (cols[cx] & (1 << (cy - 1))) != 0, D = cy < 6 && (cols[cx] & (1 << (cy + 1))) != 0;
                    float r = px * 0.42f;
                    for (int y = (int)y0; y < y0 + px; y++)
                        for (int x = (int)x0; x < x0 + px; x++)
                        {
                            float lx = x + 0.5f - x0, ly = y + 0.5f - y0;
                            // Round only the corners that have no neighbour on either side.
                            float qx = lx < px / 2f ? (L ? px : lx) : (R ? px : px - lx);
                            float qy = ly < px / 2f ? (U ? px : ly) : (D ? px : px - ly);
                            float a = 1;
                            if (qx < r && qy < r)
                            {
                                float d = MathF.Sqrt((r - qx) * (r - qx) + (r - qy) * (r - qy));
                                a = Math.Clamp(r - d + 0.5f, 0, 1);
                            }
                            mask[y * w + x] = Math.Max(mask[y * w + x], a);
                        }
                }
        }
        float At(int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? 0 : mask[y * w + x];
        var outline = Blur(mask, w, h, px / 5);
        var glow = Blur(mask, w, h, px);
        var data = new Color[w * h];
        var top = new Vector3(1f, 0.97f, 0.7f);
        var mid = new Vector3(1f, 0.74f, 0.12f);
        var bottom = new Vector3(0.85f, 0.25f, 0.05f);
        for (int y = 0; y < h; y++)
        {
            float t = Math.Clamp((y - pad) / (7f * px), 0, 1);
            var fill = t < 0.5f ? Vector3.Lerp(top, mid, t * 2) : Vector3.Lerp(mid, bottom, (t - 0.5f) * 2);
            // A horizon line across the letters, like chrome logos of the day.
            if (Math.Abs(t - 0.52f) < 0.025f) fill *= 0.55f;
            for (int x = 0; x < w; x++)
            {
                float m = mask[y * w + x];
                float bevel = At(x - 2, y - 2) - At(x + 2, y + 2);
                var col = fill * (1 + 0.35f * bevel);
                float o = Math.Clamp(outline[y * w + x] * 3f, 0, 1);
                float g = Math.Clamp(glow[y * w + x] * 1.4f, 0, 1);
                var glowCol = new Vector3(1f, 0.35f, 0.1f);
                // Composite: glow, then dark outline, then the fill.
                var c = glowCol * g * 0.8f;
                float a = g * 0.8f;
                c = Vector3.Lerp(c, new Vector3(0.12f, 0.02f, 0.02f), o);
                a = a + (1 - a) * o;
                c = Vector3.Lerp(c, col, m);
                a = a + (1 - a) * m;
                // Premultiplied alpha.
                data[y * w + x] = new Color(new Vector4(Vector3.Clamp(c, Vector3.Zero, Vector3.One) * Math.Min(1, a) / Math.Max(a, 1e-4f) * a, a));
            }
        }
        var tex = new Texture2D(device, w, h);
        tex.SetData(data);
        return tex;
    }

    private static float[] Blur(float[] src, int w, int h, int r)
    {
        if (r <= 0) return (float[])src.Clone();
        var tmp = new float[w * h];
        var dst = new float[w * h];
        for (int pass = 0; pass < 2; pass++)
        {
            var from = pass == 0 ? src : dst;
            for (int y = 0; y < h; y++)
            {
                float sum = 0;
                for (int x = -r; x <= r; x++) sum += x >= 0 && x < w ? from[y * w + x] : 0;
                for (int x = 0; x < w; x++)
                {
                    tmp[y * w + x] = sum / (2 * r + 1);
                    int add = x + r + 1, rem = x - r;
                    if (add < w) sum += from[y * w + add];
                    if (rem >= 0) sum -= from[y * w + rem];
                }
            }
            for (int x = 0; x < w; x++)
            {
                float sum = 0;
                for (int y = -r; y <= r; y++) sum += y >= 0 && y < h ? tmp[y * w + x] : 0;
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = sum / (2 * r + 1);
                    int add = y + r + 1, rem = y - r;
                    if (add < h) sum += tmp[add * w + x];
                    if (rem >= 0) sum -= tmp[rem * w + x];
                }
            }
        }
        return dst;
    }

    // ------------------------------------------------------------------ UI pieces

    /// <summary>A 64 x 64 rounded rectangle (radius 16) for nine-slice glass panels; premultiplied white.</summary>
    public static Texture2D CreateRoundedRect(GraphicsDevice device)
    {
        const int n = 64, r = 16;
        var c = new Canvas(n, n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float qx = Math.Min(x + 0.5f, n - x - 0.5f), qy = Math.Min(y + 0.5f, n - y - 0.5f);
                float a = 1;
                if (qx < r && qy < r) a = Math.Clamp(r - MathF.Sqrt((r - qx) * (r - qx) + (r - qy) * (r - qy)) + 0.5f, 0, 1);
                c.Set(x, y, new Vector4(1, 1, 1, a));
            }
        var data = new Color[n * n];
        c.CopyTo(data, n, 0, 0, true);
        var tex = new Texture2D(device, n, n);
        tex.SetData(data);
        return tex;
    }

    /// <summary>A soft radial glow (premultiplied white).</summary>
    public static Texture2D CreateGlow(GraphicsDevice device, int n = 64)
    {
        var data = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f)) / (n / 2f);
                float a = MathF.Exp(-d * d * 4.5f) * Math.Clamp((1 - d) * 5, 0, 1);
                data[y * n + x] = new Color(a, a, a, a);
            }
        var tex = new Texture2D(device, n, n);
        tex.SetData(data);
        return tex;
    }
}
