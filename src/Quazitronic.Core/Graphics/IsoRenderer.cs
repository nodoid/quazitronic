using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quazitronic.Game;

namespace Quazitronic.Graphics;

/// <summary>
/// The enhanced game's renderer. It keeps the original's isometric projection (screen x = wx - wy,
/// y = (wx + wy) / 2 - z, in Oric pixels) but draws real geometry with a depth buffer: every deck
/// tile is a lit, textured column (slab, steps, walls), droids are 3D models with floating lids,
/// and lights, shots and explosions glow additively. Vertices stay in world units; one matrix maps
/// them to the screen, so the camera scrolls smoothly at any resolution.
/// </summary>
public sealed class IsoRenderer
{
    public const float SlabDepth = 18;
    private const float DepthScale = 1 / 4000f;

    // Light from the upper left of the screen, a little towards the viewer.
    private static readonly Vector3 LightDir = Vector3.Normalize(new Vector3(0.15f, 0.75f, 1.0f));
    private static readonly Vector3 ViewDir = Vector3.Normalize(new Vector3(1, 1, 1));

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _fx;
    private readonly Texture2D _atlas;

    private VertexBuffer? _deckVb, _glowVb;
    private int _deckTris, _glowTris;
    private Deck? _builtDeck;
    private DeckTheme _theme;

    private VertexPositionColorTexture[] _opaque = new VertexPositionColorTexture[16384];
    private VertexPositionColorTexture[] _alpha = new VertexPositionColorTexture[8192];
    private VertexPositionColorTexture[] _add = new VertexPositionColorTexture[8192];
    private VertexPositionColorTexture[] _ghost = new VertexPositionColorTexture[4096];
    private int _nOpaque, _nAlpha, _nAdd, _nGhost;

    /// <summary>Additive blending for premultiplied glow textures.</summary>
    public static readonly BlendState Glow = new()
    {
        ColorSourceBlend = Blend.One, AlphaSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One, AlphaDestinationBlend = Blend.One,
    };

    private static readonly DepthStencilState DepthReadOnly = new() { DepthBufferEnable = true, DepthBufferWriteEnable = false };
    private static readonly DepthStencilState Hidden = new() { DepthBufferEnable = true, DepthBufferWriteEnable = false, DepthBufferFunction = CompareFunction.Greater };

    public IsoRenderer(GraphicsDevice device, Texture2D atlas)
    {
        _device = device;
        _atlas = atlas;
        _fx = new BasicEffect(device)
        {
            TextureEnabled = true,
            VertexColorEnabled = true,
            LightingEnabled = false,
            Texture = atlas,
            World = Matrix.Identity,
            View = Matrix.Identity,
        };
    }

    public DeckTheme Theme => _theme;

    /// <summary>Mesh smoothness multiplier (2-3 for big portraits and artwork).</summary>
    public int Detail { get; set; } = 1;

    // ------------------------------------------------------------------ projection

    public static Vector2 ToIso(Vector3 w) => new(w.X - w.Y, (w.X + w.Y) / 2 - w.Z);

    /// <summary>World offset that moves a point by (dx, dy) on screen without changing its depth.</summary>
    public static Vector3 ScreenOffset(float dx, float dy)
    {
        float s = dy / 1.5f;
        return new Vector3((dx + s) / 2, (s - dx) / 2, -s);
    }

    /// <summary>
    /// Maps world space so that iso point <paramref name="isoCentre"/> lands at virtual pixel
    /// <paramref name="screenCentre"/> of a <paramref name="vw"/> x <paramref name="vh"/> screen.
    /// </summary>
    public static Matrix Projection(Vector2 isoCentre, Vector2 screenCentre, float zoom, float vw, float vh)
    {
        float a = zoom * 2 / vw, c = zoom * 2 / vh;
        float bx = (screenCentre.X - isoCentre.X * zoom) * 2 / vw - 1;
        float by = 1 - (screenCentre.Y - isoCentre.Y * zoom) * 2 / vh;
        return new Matrix(
            a, -c / 2, -DepthScale, 0,
            -a, -c / 2, -DepthScale, 0,
            0, c, -DepthScale, 0,
            bx, by, 0.5f, 1);
    }

    private Matrix _proj;
    private Vector2 _isoCentre, _screenCentre;
    private float _zoom = 1;

    public void SetCamera(Vector2 isoCentre, Vector2 screenCentre, float zoom, float vw, float vh)
    {
        _isoCentre = isoCentre;
        _screenCentre = screenCentre;
        _zoom = zoom;
        _proj = Projection(isoCentre, screenCentre, zoom, vw, vh);
    }

    /// <summary>Virtual-screen position of a world point under the current camera.</summary>
    public Vector2 WorldToScreen(Vector3 w) => (ToIso(w) - _isoCentre) * _zoom + _screenCentre;

    // ------------------------------------------------------------------ deck geometry

    private static float Light(Vector3 n) => 0.42f + 0.62f * Math.Max(0, Vector3.Dot(n, LightDir));

    private static Color Shade(Color c, float k) => new((int)Math.Min(255, c.R * k), (int)Math.Min(255, c.G * k), (int)Math.Min(255, c.B * k), c.A);

    private sealed class Builder
    {
        public readonly List<VertexPositionColorTexture> V = new();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd, Vector4 uv)
        {
            // a-b-c-d in order, uv: a=(u0,v0) b=(u1,v0) c=(u1,v1) d=(u0,v1)
            var ta = new Vector2(uv.X, uv.Y); var tb = new Vector2(uv.Z, uv.Y);
            var tc = new Vector2(uv.Z, uv.W); var td = new Vector2(uv.X, uv.W);
            V.Add(new(a, ca, ta)); V.Add(new(b, cb, tb)); V.Add(new(c, cc, tc));
            V.Add(new(a, ca, ta)); V.Add(new(c, cc, tc)); V.Add(new(d, cd, td));
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector4 uv) => Quad(a, b, c, d, col, col, col, col, uv);
    }

    private static float TopOf(Deck d, int i, int j)
    {
        var k = d.Kind(i, j);
        if (k == TileKind.Void) return float.NegativeInfinity;
        return d.Level(i, j) * Deck.LevelHeight + (k == TileKind.Wall ? Deck.WallLevels * Deck.LevelHeight : 0);
    }

    /// <summary>Builds the static geometry for a deck (called when the deck changes).</summary>
    public void BuildDeck(Deck deck)
    {
        if (_builtDeck == deck) return;
        _builtDeck = deck;
        _theme = DeckTheme.For(deck.OricInk);
        var b = new Builder();
        var glow = new Builder();
        var metal = _theme.Metal;
        var trim = _theme.Trim;
        var accent = _theme.Accent;
        const float T = Deck.TileUnits;

        for (int j = 0; j < Deck.Size; j++)
            for (int i = 0; i < Deck.Size; i++)
            {
                var kind = deck.Kind(i, j);
                if (kind == TileKind.Void) continue;
                int level = deck.Level(i, j);
                float floorZ = level * Deck.LevelHeight;
                float top = TopOf(deck, i, j);
                float x0 = i * T, x1 = x0 + T, y0 = j * T, y1 = y0 + T;

                // --- top face, with corner occlusion where a taller neighbour stands behind it
                AtlasCell cell = kind switch
                {
                    TileKind.Wall => AtlasCell.WallTop,
                    TileKind.Pad => AtlasCell.Pad,
                    TileKind.Energiser => AtlasCell.Energiser,
                    TileKind.Lift => AtlasCell.Lift,
                    _ => deck.Style == 1 ? AtlasCell.FloorTread : AtlasCell.FloorPanel,
                };
                Color baseCol = kind switch
                {
                    TileKind.Wall => Shade(trim, 1.25f),
                    TileKind.Energiser => Color.Lerp(metal, accent, 0.35f),
                    TileKind.Lift => Color.Lerp(trim, accent, 0.25f),
                    TileKind.Pad => Color.Lerp(metal, Color.White, 0.25f),
                    _ => deck.Style == 1 && ((i + j) & 1) == 1 ? Shade(metal, 0.86f) : metal,
                };
                float topLight = Light(Vector3.UnitZ);
                Color Corner(int di, int dj)
                {
                    // corner (i+di, j+dj): neighbours sharing it
                    float occ = 1;
                    for (int a = -1; a <= 0; a++)
                        for (int c = -1; c <= 0; c++)
                        {
                            int ni = i + di + a, nj = j + dj + c;
                            if (ni == i && nj == j) continue;
                            if (TopOf(deck, ni, nj) > top + 0.1f) occ = Math.Min(occ, 0.62f);
                        }
                    return Shade(baseCol, topLight * occ);
                }
                b.Quad(new(x0, y0, top), new(x1, y0, top), new(x1, y1, top), new(x0, y1, top),
                    Corner(0, 0), Corner(1, 0), Corner(1, 1), Corner(0, 1), TextureFactory.Uv(cell));

                // --- the two visible side faces (+x shows lower right, +y lower left)
                for (int side = 0; side < 2; side++)
                {
                    int ni = side == 0 ? i + 1 : i, nj = side == 0 ? j : j + 1;
                    float nTop = TopOf(deck, ni, nj);
                    bool voidBelow = float.IsNegativeInfinity(nTop);
                    float bottom = voidBelow ? -SlabDepth : nTop;
                    if (bottom >= top) continue;
                    Vector3 n = side == 0 ? Vector3.UnitX : Vector3.UnitY;
                    float lk = Light(n);
                    // face corners along the edge
                    Vector3 e0 = side == 0 ? new Vector3(x1, y0, 0) : new Vector3(x0, y1, 0);
                    Vector3 e1 = side == 0 ? new Vector3(x1, y1, 0) : new Vector3(x1, y1, 0);
                    void Face(float zLo, float zHi, AtlasCell c, Color col, float darkBottom = 0.72f)
                    {
                        if (zHi <= bottom + 0.01f) return;
                        float lo = Math.Max(zLo, bottom);
                        var uv = TextureFactory.Uv(c);
                        // crop the texture's v range to the visible part
                        float vFrac = (zHi - lo) / (zHi - zLo);
                        uv.W = uv.Y + (uv.W - uv.Y) * vFrac;
                        var top_ = Shade(col, lk);
                        var bot = Shade(col, lk * (darkBottom + (1 - darkBottom) * (1 - vFrac)));
                        b.Quad(e0 + new Vector3(0, 0, zHi), e1 + new Vector3(0, 0, zHi), e1 + new Vector3(0, 0, lo), e0 + new Vector3(0, 0, lo),
                            top_, top_, bot, bot, uv);
                    }
                    if (voidBelow) Face(-SlabDepth, 0, AtlasCell.Slab, Shade(metal, 1.05f), 0.4f);
                    for (int l = 0; l < level; l++)
                        Face(l * Deck.LevelHeight, (l + 1) * Deck.LevelHeight, deck.Style == 1 ? AtlasCell.StepBrick : AtlasCell.StepPanel, metal, 0.85f);
                    if (kind == TileKind.Wall)
                    {
                        for (int l = 0; l < Deck.WallLevels; l++)
                        {
                            float z0 = floorZ + l * Deck.LevelHeight;
                            Face(z0, z0 + Deck.LevelHeight, deck.Style == 1 ? AtlasCell.WallBrick : AtlasCell.WallPanel, metal, l == 0 ? 0.7f : 0.9f);
                        }
                        // a lit trim strip just under the wall top
                        float zs = top - 3;
                        if (zs > bottom)
                        {
                            var off = n * 0.15f;
                            glow.Quad(e0 + new Vector3(0, 0, zs + 1.2f) + off, e1 + new Vector3(0, 0, zs + 1.2f) + off,
                                e1 + new Vector3(0, 0, zs) + off, e0 + new Vector3(0, 0, zs) + off, accent * 0.55f, TextureFactory.Uv(AtlasCell.White));
                        }
                    }
                    // the drips hanging under the deck's edge, as on the Spectrum
                    if (voidBelow)
                        for (int k = 0; k < 3; k++)
                        {
                            float t0 = (k * 4 + 1.2f) / T, t1 = (k * 4 + 2.8f) / T;
                            var p0 = Vector3.Lerp(e0, e1, t0) + n * 0.05f;
                            var p1 = Vector3.Lerp(e0, e1, t1) + n * 0.05f;
                            float len = 6 + ((i * 7 + j * 3 + k) % 3) * 2;
                            b.Quad(p0 + new Vector3(0, 0, -SlabDepth), p1 + new Vector3(0, 0, -SlabDepth),
                                p1 + new Vector3(0, 0, -SlabDepth - len), p0 + new Vector3(0, 0, -SlabDepth - len),
                                Shade(trim, lk * 0.8f), Shade(trim, lk * 0.8f), Shade(trim, 0.15f), Shade(trim, 0.15f), TextureFactory.Uv(AtlasCell.White));
                        }
                }

                // --- consoles: a cabinet with screens on its two visible faces
                if (kind == TileKind.Console)
                {
                    float cx = x0 + 6, cy = y0 + 6, h = 10, r = 3.6f;
                    var c0 = new Vector3(cx - r, cy - r, floorZ);
                    var cabinet = Shade(trim, 1.1f);
                    b.Quad(new(cx - r, cy - r, floorZ + h), new(cx + r, cy - r, floorZ + h), new(cx + r, cy + r, floorZ + h), new(cx - r, cy + r, floorZ + h),
                        Shade(cabinet, topLight), TextureFactory.Uv(AtlasCell.ConsoleSide));
                    b.Quad(new(cx + r, cy - r, floorZ + h), new(cx + r, cy + r, floorZ + h), new(cx + r, cy + r, floorZ), new(cx + r, cy - r, floorZ),
                        Shade(Color.White, Light(Vector3.UnitX)), TextureFactory.Uv(AtlasCell.ConsoleScreen));
                    b.Quad(new(cx - r, cy + r, floorZ + h), new(cx + r, cy + r, floorZ + h), new(cx + r, cy + r, floorZ), new(cx - r, cy + r, floorZ),
                        Shade(Color.White, Light(Vector3.UnitY)), TextureFactory.Uv(AtlasCell.ConsoleScreen));
                    _ = c0;
                    var g = new Color(60, 255, 120) * 0.35f;
                    glow.Quad(new(cx + r + 0.2f, cy - r, floorZ + h), new(cx + r + 0.2f, cy + r, floorZ + h), new(cx + r + 0.2f, cy + r, floorZ), new(cx + r + 0.2f, cy - r, floorZ),
                        g, TextureFactory.Uv(AtlasCell.Glow));
                }

                // --- glowing details
                float gz = top + 0.25f;
                if (kind == TileKind.Pad)
                    glow.Quad(new(x0 + 1, y0 + 1, gz), new(x1 - 1, y0 + 1, gz), new(x1 - 1, y1 - 1, gz), new(x0 + 1, y1 - 1, gz), accent * 0.5f, TextureFactory.Uv(AtlasCell.Arrow));
                if (kind == TileKind.Energiser)
                    glow.Quad(new(x0 - 2, y0 - 2, gz), new(x1 + 2, y0 - 2, gz), new(x1 + 2, y1 + 2, gz), new(x0 - 2, y1 + 2, gz), accent * 0.9f, TextureFactory.Uv(AtlasCell.Ring));
                if (kind == TileKind.Lift)
                    glow.Quad(new(x0 - 1, y0 - 1, gz), new(x1 + 1, y0 - 1, gz), new(x1 + 1, y1 + 1, gz), new(x0 - 1, y1 + 1, gz), Color.Lerp(accent, Color.White, 0.4f) * 0.8f, TextureFactory.Uv(AtlasCell.Ring));
            }

        _deckVb?.Dispose();
        _glowVb?.Dispose();
        _deckVb = Upload(b.V, out _deckTris);
        _glowVb = Upload(glow.V, out _glowTris);
    }

    private VertexBuffer? Upload(List<VertexPositionColorTexture> v, out int tris)
    {
        tris = v.Count / 3;
        if (v.Count == 0) return null;
        var vb = new VertexBuffer(_device, VertexPositionColorTexture.VertexDeclaration, v.Count, BufferUsage.WriteOnly);
        vb.SetData(v.ToArray());
        return vb;
    }

    /// <summary>Iso-space bounds of the built deck (for camera limits).</summary>
    public static RectangleF DeckBounds(Deck deck)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int j = 0; j < Deck.Size; j++)
            for (int i = 0; i < Deck.Size; i++)
            {
                float top = TopOf(deck, i, j);
                if (float.IsNegativeInfinity(top)) continue;
                for (int c = 0; c < 4; c++)
                {
                    var w = new Vector3((i + (c & 1)) * 12, (j + (c >> 1)) * 12, 0);
                    var a = ToIso(w + new Vector3(0, 0, top));
                    var bb = ToIso(w + new Vector3(0, 0, -SlabDepth - 10));
                    minX = Math.Min(minX, a.X); maxX = Math.Max(maxX, a.X);
                    minY = Math.Min(minY, a.Y); maxY = Math.Max(maxY, bb.Y);
                }
            }
        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    // ------------------------------------------------------------------ dynamic geometry

    public void BeginDynamic() => _nOpaque = _nAlpha = _nAdd = _nGhost = 0;

    private static void Push(ref VertexPositionColorTexture[] arr, ref int n, in VertexPositionColorTexture v)
    {
        if (n == arr.Length) Array.Resize(ref arr, arr.Length * 2);
        arr[n++] = v;
    }

    private enum Layer { Opaque, Alpha, Add, Ghost }

    private void Tri(Layer l, in VertexPositionColorTexture a, in VertexPositionColorTexture b, in VertexPositionColorTexture c)
    {
        switch (l)
        {
            case Layer.Opaque: Push(ref _opaque, ref _nOpaque, a); Push(ref _opaque, ref _nOpaque, b); Push(ref _opaque, ref _nOpaque, c); break;
            case Layer.Alpha: Push(ref _alpha, ref _nAlpha, a); Push(ref _alpha, ref _nAlpha, b); Push(ref _alpha, ref _nAlpha, c); break;
            case Layer.Add: Push(ref _add, ref _nAdd, a); Push(ref _add, ref _nAdd, b); Push(ref _add, ref _nAdd, c); break;
            default: Push(ref _ghost, ref _nGhost, a); Push(ref _ghost, ref _nGhost, b); Push(ref _ghost, ref _nGhost, c); break;
        }
    }

    private void Quad(Layer l, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector4 uv)
    {
        var va = new VertexPositionColorTexture(a, col, new Vector2(uv.X, uv.Y));
        var vb = new VertexPositionColorTexture(b, col, new Vector2(uv.Z, uv.Y));
        var vc = new VertexPositionColorTexture(c, col, new Vector2(uv.Z, uv.W));
        var vd = new VertexPositionColorTexture(d, col, new Vector2(uv.X, uv.W));
        Tri(l, va, vb, vc);
        Tri(l, va, vc, vd);
    }

    /// <summary>A camera-facing sprite centred on a world point, <paramref name="size"/> virtual pixels across.</summary>
    private void Billboard(Layer l, Vector3 p, float w, float h, Color col, AtlasCell cell, float rotation = 0)
    {
        var uv = TextureFactory.Uv(cell);
        float c = MathF.Cos(rotation), s = MathF.Sin(rotation);
        Vector3 Corner(float dx, float dy) => p + ScreenOffset(dx * c - dy * s, dx * s + dy * c);
        Quad(l, Corner(-w / 2, -h / 2), Corner(w / 2, -h / 2), Corner(w / 2, h / 2), Corner(-w / 2, h / 2), col, uv);
    }

    /// <summary>A sprite lying flat on the floor (shadows, rings), <paramref name="r"/> world units in radius.</summary>
    private void FloorDecal(Layer l, Vector3 p, float r, Color col, AtlasCell cell)
    {
        Quad(l, p + new Vector3(-r, -r, 0), p + new Vector3(r, -r, 0), p + new Vector3(r, r, 0), p + new Vector3(-r, r, 0), col, TextureFactory.Uv(cell));
    }

    private static Color Lit(Color albedo, Vector3 n, float spec = 0.45f, float rim = 0.3f)
    {
        float diff = 0.36f + 0.7f * Math.Max(0, Vector3.Dot(n, LightDir));
        var h = Vector3.Normalize(LightDir + ViewDir);
        float sp = MathF.Pow(Math.Max(0, Vector3.Dot(n, h)), 28) * spec;
        float rm = MathF.Pow(1 - Math.Clamp(Vector3.Dot(n, ViewDir), 0, 1), 2) * rim;
        var c = albedo.ToVector3() * diff + new Vector3(sp + rm * 0.6f, sp + rm * 0.8f, sp + rm);
        return new Color(Vector3.Clamp(c, Vector3.Zero, Vector3.One));
    }

    /// <summary>A lathed surface between two rings (radius, z) around a vertical axis.</summary>
    private void Band(Layer l, Vector3 c, float r0, float z0, float r1, float z1, Color albedo, AtlasCell cell, int seg = 18, float spec = 0.45f)
    {
        float slope = (r0 - r1) / Math.Max(0.01f, z1 - z0);
        float nz = slope / MathF.Sqrt(1 + slope * slope);
        Band(l, c, r0, z0, nz, r1, z1, nz, albedo, cell, seg, spec);
    }

    /// <summary>A lathed band with explicit normal tilts at each ring (smooth spheres).</summary>
    private void Band(Layer l, Vector3 c, float r0, float z0, float nz0, float r1, float z1, float nz1, Color albedo, AtlasCell cell, int seg = 18, float spec = 0.45f)
    {
        var uv = TextureFactory.Uv(cell);
        if (cell == AtlasCell.White)
        {
            // A flat colour: one texel, so MSAA's extrapolated coordinates can't reach a neighbour.
            float cu = (uv.X + uv.Z) / 2, cv = (uv.Y + uv.W) / 2;
            uv = new Vector4(cu, cv, cu, cv);
        }
        seg *= Detail;
        float nr0 = MathF.Sqrt(Math.Max(0, 1 - nz0 * nz0)), nr1 = MathF.Sqrt(Math.Max(0, 1 - nz1 * nz1));
        for (int k = 0; k < seg; k++)
        {
            float a0 = k * MathF.Tau / seg, a1 = (k + 1) * MathF.Tau / seg;
            // only the half facing the viewer is visible (normals towards +x+y)
            float mid = (a0 + a1) / 2;
            if (MathF.Cos(mid) + MathF.Sin(mid) < -0.5f) continue;
            var n00 = new Vector3(MathF.Cos(a0) * nr0, MathF.Sin(a0) * nr0, nz0);
            var n10 = new Vector3(MathF.Cos(a1) * nr0, MathF.Sin(a1) * nr0, nz0);
            var n01 = new Vector3(MathF.Cos(a0) * nr1, MathF.Sin(a0) * nr1, nz1);
            var n11 = new Vector3(MathF.Cos(a1) * nr1, MathF.Sin(a1) * nr1, nz1);
            var p00 = c + new Vector3(MathF.Cos(a0) * r0, MathF.Sin(a0) * r0, z0);
            var p10 = c + new Vector3(MathF.Cos(a1) * r0, MathF.Sin(a1) * r0, z0);
            var p01 = c + new Vector3(MathF.Cos(a0) * r1, MathF.Sin(a0) * r1, z1);
            var p11 = c + new Vector3(MathF.Cos(a1) * r1, MathF.Sin(a1) * r1, z1);
            float u0 = uv.X + (uv.Z - uv.X) * k / seg, u1 = uv.X + (uv.Z - uv.X) * (k + 1) / seg;
            var v00 = new VertexPositionColorTexture(p00, Lit(albedo, n00, spec), new Vector2(u0, uv.W));
            var v10 = new VertexPositionColorTexture(p10, Lit(albedo, n10, spec), new Vector2(u1, uv.W));
            var v01 = new VertexPositionColorTexture(p01, Lit(albedo, n01, spec), new Vector2(u0, uv.Y));
            var v11 = new VertexPositionColorTexture(p11, Lit(albedo, n11, spec), new Vector2(u1, uv.Y));
            Tri(l, v00, v10, v11);
            Tri(l, v00, v11, v01);
        }
    }

    /// <summary>A flat disc cap facing up.</summary>
    private void Cap(Layer l, Vector3 c, float r, float z, Color albedo, AtlasCell cell = AtlasCell.White, int seg = 18)
    {
        var uv = TextureFactory.Uv(cell);
        if (cell == AtlasCell.White)
        {
            float cu = (uv.X + uv.Z) / 2, cv = (uv.Y + uv.W) / 2;
            uv = new Vector4(cu, cv, cu, cv);
        }
        seg *= Detail;
        var col = Lit(albedo, Vector3.UnitZ);
        var centre = new VertexPositionColorTexture(c + new Vector3(0, 0, z), col, new Vector2((uv.X + uv.Z) / 2, (uv.Y + uv.W) / 2));
        for (int k = 0; k < seg; k++)
        {
            float a0 = k * MathF.Tau / seg, a1 = (k + 1) * MathF.Tau / seg;
            var p0 = c + new Vector3(MathF.Cos(a0) * r, MathF.Sin(a0) * r, z);
            var p1 = c + new Vector3(MathF.Cos(a1) * r, MathF.Sin(a1) * r, z);
            var t0 = new Vector2(uv.X + (uv.Z - uv.X) * (0.5f + MathF.Cos(a0) / 2), uv.Y + (uv.W - uv.Y) * (0.5f + MathF.Sin(a0) / 2));
            var t1 = new Vector2(uv.X + (uv.Z - uv.X) * (0.5f + MathF.Cos(a1) / 2), uv.Y + (uv.W - uv.Y) * (0.5f + MathF.Sin(a1) / 2));
            Tri(l, centre, new VertexPositionColorTexture(p0, col, t0), new VertexPositionColorTexture(p1, col, t1));
        }
    }

    /// <summary>Colour of each class's band, from the messengers (green) up to the command unit (gold).</summary>
    public static readonly Color[] ClassColour =
    {
        new(90, 230, 255), new(90, 230, 120), new(70, 210, 230), new(240, 220, 80), new(255, 150, 50),
        new(255, 70, 70), new(190, 110, 255), new(230, 40, 90), new(255, 200, 40),
    };

    /// <summary>Adds a droid model standing at world position p. <paramref name="flash"/> 0..1 whitens it (hit).</summary>
    public void AddDroid(Vector3 p, int type, float time, float flash = 0, bool ghost = false, float scale = 1)
    {
        var l = Layer.Opaque;
        var band = ClassColour[type];
        var body = Color.Lerp(new Color(215, 222, 235), Color.White, flash);
        float s = scale;
        float bob = MathF.Sin(time * 3.1f + p.X * 0.13f + p.Y * 0.07f) * 0.8f * s;
        if (type == 0)
        {
            // The influence device: a polished sphere with a dark visor, its dome lid hovering above.
            var c = p + new Vector3(0, 0, 0);
            float r = 5.6f * s, zc = 6.2f * s;
            int rings = 8 * Detail;
            for (int k = 0; k < rings; k++)
            {
                float t0 = -MathF.PI / 2 + k * MathF.PI / rings, t1 = -MathF.PI / 2 + (k + 1) * MathF.PI / rings; // identical at shared seams
                bool visor = k * 8 / rings == 4;
                Band(l, c, MathF.Cos(t0) * r, zc + MathF.Sin(t0) * r, MathF.Sin(t0), MathF.Cos(t1) * r, zc + MathF.Sin(t1) * r, MathF.Sin(t1),
                    visor ? Color.Lerp(new Color(20, 30, 60), Color.White, flash) : body, AtlasCell.White, 20, visor ? 0.9f : 0.6f);
            }
            float lz = zc + r + 1.6f * s + bob;
            Band(l, c, 4.2f * s, lz, 3.4f * s, lz + 1.4f * s, Color.Lerp(band, Color.White, 0.3f), AtlasCell.White);
            Band(l, c, 3.4f * s, lz + 1.4f * s, 1.6f * s, lz + 2.6f * s, Color.Lerp(band, Color.White, 0.3f), AtlasCell.White);
            Cap(l, c, 1.6f * s, lz + 2.6f * s, Color.White);
            if (!ghost) AddGlow(c + new Vector3(0, 0, lz + 0.5f * s), 10 * s, band * 0.35f);
            return;
        }
        var cc = p;
        float rb = 5.3f * s;
        // body: bevelled foot, cylinder with the class band, bevelled shoulder, top cap
        Band(l, cc, 4.2f * s, 0, rb, 1.6f * s, Shade(body, 0.8f), AtlasCell.White);
        Band(l, cc, rb, 1.6f * s, rb, 3.6f * s, body, AtlasCell.Body);
        Band(l, cc, rb, 3.6f * s, rb, 9.2f * s, body, AtlasCell.White);
        Band(l, cc, rb, 9.2f * s, rb, 10.4f * s, Color.Lerp(band, Color.White, flash), AtlasCell.White, 18, 0.7f);
        Band(l, cc, rb, 10.4f * s, 4.4f * s, 11.6f * s, body, AtlasCell.White);
        Cap(l, cc, 4.4f * s, 11.6f * s, Shade(body, 0.9f));
        // the class code on its front
        if (!ghost)
        {
            var front = cc + new Vector3(rb * 0.72f, rb * 0.72f, 6.2f * s) + new Vector3(3f) * s; // nudged along the view axis so the body never cuts it
            Billboard(Layer.Alpha, front, 9.5f * s, 9.5f * s, Color.White, AtlasCell.Label0 + type);
        }
        // the floating lid
        float z = 13.2f * s + bob;
        var lidCol = Color.Lerp(Color.Lerp(body, band, 0.35f), Color.White, flash);
        switch (Droids.Lid[type])
        {
            case LidShape.Flat:
                Band(l, cc, 5.6f * s, z, 5.6f * s, z + 1.2f * s, lidCol, AtlasCell.White);
                Cap(l, cc, 5.6f * s, z + 1.2f * s, lidCol);
                break;
            case LidShape.Antenna:
                Band(l, cc, 5.0f * s, z, 4.2f * s, z + 1.6f * s, lidCol, AtlasCell.White);
                Cap(l, cc, 4.2f * s, z + 1.6f * s, lidCol);
                Band(l, cc, 0.7f * s, z + 1.6f * s, 0.5f * s, z + 6.5f * s, Color.Silver, AtlasCell.White, 6);
                if (!ghost) AddGlow(cc + new Vector3(0, 0, z + 7f * s), 3.2f * s, band * (0.6f + 0.4f * MathF.Sin(time * 8)));
                break;
            case LidShape.Crown:
                Band(l, cc, 5.4f * s, z, 5.4f * s, z + 1.6f * s, lidCol, AtlasCell.Stripe);
                Cap(l, cc, 5.4f * s, z + 1.6f * s, Shade(lidCol, 0.85f));
                for (int k = 0; k < 6; k++)
                {
                    float a = k * MathF.Tau / 6 + time * 0.8f;
                    var tip = cc + new Vector3(MathF.Cos(a) * 4.6f * s, MathF.Sin(a) * 4.6f * s, z + 1.6f * s);
                    Band(l, tip, 0.9f * s, 0, 0.1f, 2.6f * s, lidCol, AtlasCell.White, 5);
                }
                break;
            case LidShape.Big:
                Band(l, cc, 6.4f * s, z, 6.4f * s, z + 1.8f * s, lidCol, AtlasCell.Stripe);
                Band(l, cc, 6.4f * s, z + 1.8f * s, 4.8f * s, z + 2.8f * s, lidCol, AtlasCell.White);
                Cap(l, cc, 4.8f * s, z + 2.8f * s, Shade(lidCol, 0.9f));
                break;
            default:
                Band(l, cc, 5.0f * s, z, 3.0f * s, z + 2.4f * s, lidCol, AtlasCell.White);
                Cap(l, cc, 3.0f * s, z + 2.4f * s, lidCol);
                break;
        }
    }

    /// <summary>The player seen through walls: an outline-coloured silhouette where it is hidden.</summary>
    public void AddGhost(Vector3 p, float time)
    {
        int start = _nOpaque;
        AddDroid(p, 0, time, ghost: true);
        // move what was just added to the ghost layer, tinted
        for (int k = start; k < _nOpaque; k++)
        {
            var v = _opaque[k];
            v.Color = new Color(80, 220, 255) * 0.45f;
            Push(ref _ghost, ref _nGhost, v);
        }
        _nOpaque = start;
    }

    /// <summary>A lit box (menus and artwork): textured top, shaded visible sides.</summary>
    public void AddBlock(Vector3 min, Vector3 max, AtlasCell top, AtlasCell side, Color colour)
    {
        var l = Layer.Opaque;
        Quad(l, new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z), Shade(colour, Light(Vector3.UnitZ)), TextureFactory.Uv(top));
        Quad(l, new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(max.X, max.Y, min.Z), new(max.X, min.Y, min.Z), Shade(colour, Light(Vector3.UnitX)), TextureFactory.Uv(side));
        Quad(l, new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z), Shade(colour, Light(Vector3.UnitY)), TextureFactory.Uv(side));
    }

    public void AddShadow(Vector3 p, float radius, float alpha = 0.6f) =>
        FloorDecal(Layer.Alpha, p + new Vector3(0, 0, 0.2f), radius, Color.White * alpha, AtlasCell.Shadow);

    public void AddGlow(Vector3 p, float size, Color c) => Billboard(Layer.Add, p, size, size, c, AtlasCell.Glow);

    public void AddSprite(Vector3 p, float w, float h, Color c, AtlasCell cell, float rotation = 0, bool additive = true) =>
        Billboard(additive ? Layer.Add : Layer.Alpha, p, w, h, c, cell, rotation);

    public void AddFloorRing(Vector3 p, float radius, Color c, AtlasCell cell = AtlasCell.Ring) =>
        FloorDecal(Layer.Add, p + new Vector3(0, 0, 0.3f), radius, c, cell);

    /// <summary>A glowing bolt from a to b (world), <paramref name="width"/> virtual pixels thick.</summary>
    public void AddBolt(Vector3 a, Vector3 b, float width, Color c)
    {
        var sa = ToIso(a); var sb = ToIso(b);
        var d = sb - sa;
        float len = Math.Max(d.Length(), 0.01f);
        var dir = d / len;
        var nrm = new Vector2(-dir.Y, dir.X) * width / 2;
        var mid = (a + b) / 2;
        var ext = dir * (len / 2 + width);
        Vector3 W(Vector2 o) => mid + ScreenOffset(o.X, o.Y);
        Quad(Layer.Add, W(-ext - nrm), W(ext - nrm), W(ext + nrm), W(-ext + nrm), c, TextureFactory.Uv(AtlasCell.Bolt));
    }

    // ------------------------------------------------------------------ drawing

    private void Apply(BlendState blend, DepthStencilState depth)
    {
        _device.BlendState = blend;
        _device.DepthStencilState = depth;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.SamplerStates[0] = SamplerState.LinearClamp;
        _fx.Projection = _proj;
        _fx.DiffuseColor = Vector3.One;
        _fx.Alpha = 1;
        _fx.CurrentTechnique.Passes[0].Apply();
    }

    private void DrawArray(VertexPositionColorTexture[] v, int n)
    {
        const int chunk = 30000;
        for (int o = 0; o < n; o += chunk)
        {
            int c = Math.Min(chunk, n - o);
            _device.DrawUserPrimitives(PrimitiveType.TriangleList, v, o, c / 3);
        }
    }

    /// <summary>Draws the built deck, then the dynamic geometry added since <see cref="BeginDynamic"/>.</summary>
    public void Draw(float glowPulse)
    {
        Apply(BlendState.Opaque, DepthStencilState.Default);
        if (_deckVb != null)
        {
            _device.SetVertexBuffer(_deckVb);
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _deckTris);
        }
        if (_nOpaque > 0) DrawArray(_opaque, _nOpaque);

        if (_nAlpha > 0)
        {
            Apply(BlendState.NonPremultiplied, DepthReadOnly);
            DrawArray(_alpha, _nAlpha);
        }
        if (_nGhost > 0)
        {
            Apply(Glow, Hidden);
            DrawArray(_ghost, _nGhost);
        }
        Apply(Glow, DepthReadOnly);
        if (_glowVb != null)
        {
            _fx.DiffuseColor = new Vector3(glowPulse);
            _fx.CurrentTechnique.Passes[0].Apply();
            _device.SetVertexBuffer(_glowVb);
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _glowTris);
            _fx.DiffuseColor = Vector3.One;
            _fx.CurrentTechnique.Passes[0].Apply();
        }
        if (_nAdd > 0) DrawArray(_add, _nAdd);
        _device.SetVertexBuffer(null);
        _device.DepthStencilState = DepthStencilState.None;
        _device.BlendState = BlendState.AlphaBlend;
    }

    /// <summary>Draws only the dynamic geometry (droid portraits in menus), clearing depth first.</summary>
    public void DrawDynamicOnly()
    {
        _device.Clear(ClearOptions.DepthBuffer, Color.Black, 1, 0);
        Apply(BlendState.Opaque, DepthStencilState.Default);
        if (_nOpaque > 0) DrawArray(_opaque, _nOpaque);
        if (_nAlpha > 0) { Apply(BlendState.NonPremultiplied, DepthReadOnly); DrawArray(_alpha, _nAlpha); }
        if (_nAdd > 0) { Apply(Glow, DepthReadOnly); DrawArray(_add, _nAdd); }
        _device.DepthStencilState = DepthStencilState.None;
        _device.BlendState = BlendState.AlphaBlend;
    }
}
