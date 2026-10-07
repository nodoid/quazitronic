using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Orictron.Graphics;

/// <summary>
/// An Oric-style 6x8 character set (5x7 glyphs plus spacing, 40 columns across 240 pixels),
/// generated at start-up, with the French and Swedish accented letters the original games and translations need.
/// </summary>
public sealed class BitmapFont
{
    public const int CharWidth = 6;
    public const int CharHeight = 8;

    // Column-major 5x7 glyphs for ASCII 0x20..0x7E; bit 0 is the top row.
    private static readonly byte[] Ascii =
    {
        0x00,0x00,0x00,0x00,0x00, 0x00,0x00,0x5F,0x00,0x00, 0x00,0x07,0x00,0x07,0x00, 0x14,0x7F,0x14,0x7F,0x14,
        0x24,0x2A,0x7F,0x2A,0x12, 0x23,0x13,0x08,0x64,0x62, 0x36,0x49,0x55,0x22,0x50, 0x00,0x05,0x03,0x00,0x00,
        0x00,0x1C,0x22,0x41,0x00, 0x00,0x41,0x22,0x1C,0x00, 0x08,0x2A,0x1C,0x2A,0x08, 0x08,0x08,0x3E,0x08,0x08,
        0x00,0x50,0x30,0x00,0x00, 0x08,0x08,0x08,0x08,0x08, 0x00,0x60,0x60,0x00,0x00, 0x20,0x10,0x08,0x04,0x02,
        0x3E,0x51,0x49,0x45,0x3E, 0x00,0x42,0x7F,0x40,0x00, 0x42,0x61,0x51,0x49,0x46, 0x21,0x41,0x45,0x4B,0x31,
        0x18,0x14,0x12,0x7F,0x10, 0x27,0x45,0x45,0x45,0x39, 0x3C,0x4A,0x49,0x49,0x30, 0x01,0x71,0x09,0x05,0x03,
        0x36,0x49,0x49,0x49,0x36, 0x06,0x49,0x49,0x29,0x1E, 0x00,0x36,0x36,0x00,0x00, 0x00,0x56,0x36,0x00,0x00,
        0x08,0x14,0x22,0x41,0x00, 0x14,0x14,0x14,0x14,0x14, 0x00,0x41,0x22,0x14,0x08, 0x02,0x01,0x51,0x09,0x06,
        0x32,0x49,0x79,0x41,0x3E, 0x7E,0x11,0x11,0x11,0x7E, 0x7F,0x49,0x49,0x49,0x36, 0x3E,0x41,0x41,0x41,0x22,
        0x7F,0x41,0x41,0x22,0x1C, 0x7F,0x49,0x49,0x49,0x41, 0x7F,0x09,0x09,0x01,0x01, 0x3E,0x41,0x41,0x51,0x32,
        0x7F,0x08,0x08,0x08,0x7F, 0x00,0x41,0x7F,0x41,0x00, 0x20,0x40,0x41,0x3F,0x01, 0x7F,0x08,0x14,0x22,0x41,
        0x7F,0x40,0x40,0x40,0x40, 0x7F,0x02,0x04,0x02,0x7F, 0x7F,0x04,0x08,0x10,0x7F, 0x3E,0x41,0x41,0x41,0x3E,
        0x7F,0x09,0x09,0x09,0x06, 0x3E,0x41,0x51,0x21,0x5E, 0x7F,0x09,0x19,0x29,0x46, 0x46,0x49,0x49,0x49,0x31,
        0x01,0x01,0x7F,0x01,0x01, 0x3F,0x40,0x40,0x40,0x3F, 0x1F,0x20,0x40,0x20,0x1F, 0x7F,0x20,0x18,0x20,0x7F,
        0x63,0x14,0x08,0x14,0x63, 0x03,0x04,0x78,0x04,0x03, 0x61,0x51,0x49,0x45,0x43, 0x00,0x7F,0x41,0x41,0x00,
        0x02,0x04,0x08,0x10,0x20, 0x00,0x41,0x41,0x7F,0x00, 0x04,0x02,0x01,0x02,0x04, 0x40,0x40,0x40,0x40,0x40,
        0x00,0x01,0x02,0x04,0x00, 0x20,0x54,0x54,0x54,0x78, 0x7F,0x48,0x44,0x44,0x38, 0x38,0x44,0x44,0x44,0x20,
        0x38,0x44,0x44,0x48,0x7F, 0x38,0x54,0x54,0x54,0x18, 0x08,0x7E,0x09,0x01,0x02, 0x08,0x14,0x54,0x54,0x3C,
        0x7F,0x08,0x04,0x04,0x78, 0x00,0x44,0x7D,0x40,0x00, 0x20,0x40,0x44,0x3D,0x00, 0x00,0x7F,0x10,0x28,0x44,
        0x00,0x41,0x7F,0x40,0x00, 0x7C,0x04,0x18,0x04,0x78, 0x7C,0x08,0x04,0x04,0x78, 0x38,0x44,0x44,0x44,0x38,
        0x7C,0x14,0x14,0x14,0x08, 0x08,0x14,0x14,0x18,0x7C, 0x7C,0x08,0x04,0x04,0x08, 0x48,0x54,0x54,0x54,0x20,
        0x04,0x3F,0x44,0x40,0x20, 0x3C,0x40,0x40,0x20,0x7C, 0x1C,0x20,0x40,0x20,0x1C, 0x3C,0x40,0x30,0x40,0x3C,
        0x44,0x28,0x10,0x28,0x44, 0x0C,0x50,0x50,0x50,0x3C, 0x44,0x64,0x54,0x4C,0x44, 0x00,0x08,0x36,0x41,0x00,
        0x00,0x00,0x7F,0x00,0x00, 0x00,0x41,0x36,0x08,0x00, 0x08,0x04,0x08,0x10,0x08,
    };

    private static readonly byte[] Acute = { 0x00, 0x00, 0x02, 0x01, 0x00 };
    private static readonly byte[] Grave = { 0x00, 0x01, 0x02, 0x00, 0x00 };
    private static readonly byte[] Circumflex = { 0x00, 0x02, 0x01, 0x02, 0x00 };
    private static readonly byte[] Diaeresis = { 0x00, 0x01, 0x00, 0x01, 0x00 };
    private static readonly byte[] Cedilla = { 0x00, 0x00, 0x80, 0x80, 0x00 };
    private static readonly byte[] Ring = { 0x00, 0x02, 0x05, 0x02, 0x00 };
    private static readonly byte[] DotlessI = { 0x00, 0x44, 0x7C, 0x40, 0x00 };

    private readonly Texture2D _atlas;
    private readonly Texture2D _smooth;
    /// <summary>Upscale factor of the smoothed atlas (Scale2x applied twice).</summary>
    public const int SmoothFactor = 4;
    // Transparent gutters between smoothed glyphs so linear filtering never picks up a neighbour.
    private const int SmoothGutter = 2;
    private const int SmoothStride = CharWidth * SmoothFactor + 2 * SmoothGutter;
    private readonly Dictionary<char, int> _index = new();

    public BitmapFont(GraphicsDevice device)
    {
        var glyphs = new List<byte[]>();
        void Add(char c, byte[] cols)
        {
            _index[c] = glyphs.Count;
            glyphs.Add(cols);
        }

        for (int c = 0x20; c <= 0x7E; c++)
            Add((char)c, Slice(c));

        AddAccented('é', 'e', Acute); AddAccented('è', 'e', Grave); AddAccented('ê', 'e', Circumflex);
        AddAccented('ë', 'e', Diaeresis); AddAccented('à', 'a', Grave); AddAccented('â', 'a', Circumflex);
        AddAccented('ù', 'u', Grave); AddAccented('û', 'u', Circumflex); AddAccented('ô', 'o', Circumflex);
        AddAccented('ç', 'c', Cedilla); AddAccented('Ç', 'C', Cedilla);
        AddAccented('ä', 'a', Diaeresis); AddAccented('ö', 'o', Diaeresis); AddAccented('ü', 'u', Diaeresis);
        AddAccented('å', 'a', Ring);
        // Swedish capitals: a shortened letter leaves room for the dots/ring.
        Add('Ä', new byte[] { 0x78, 0x15, 0x14, 0x15, 0x78 });
        Add('Å', new byte[] { 0x78, 0x16, 0x15, 0x16, 0x78 });
        Add('Ö', new byte[] { 0x38, 0x45, 0x44, 0x45, 0x38 });
        Add('Ü', new byte[] { 0x3D, 0x40, 0x40, 0x40, 0x3D });
        Add('×', new byte[] { 0x22, 0x14, 0x08, 0x14, 0x22 });
        Add('•', new byte[] { 0x00, 0x1C, 0x1C, 0x1C, 0x00 });
        Add('î', Merge(DotlessI, Circumflex));
        Add('ï', Merge(DotlessI, Diaeresis));
        // Capitals have no room above for an accent; Oric software simply dropped them.
        foreach (var (accented, plain) in new[] { ('É', 'E'), ('È', 'E'), ('Ê', 'E'), ('À', 'A'), ('Ô', 'O'), ('Î', 'I'), ('Ï', 'I'), ('Ù', 'U') })
            Add(accented, Slice(plain));
        Add('←', new byte[] { 0x08, 0x1C, 0x2A, 0x08, 0x08 });
        Add('→', new byte[] { 0x08, 0x08, 0x2A, 0x1C, 0x08 });
        Add('↑', new byte[] { 0x04, 0x02, 0x7F, 0x02, 0x04 });
        Add('↓', new byte[] { 0x10, 0x20, 0x7F, 0x20, 0x10 });
        Add('♥', new byte[] { 0x0C, 0x1E, 0x3C, 0x1E, 0x0C });
        Add('©', new byte[] { 0x3E, 0x5D, 0x55, 0x41, 0x3E });

        var pixels = new Color[glyphs.Count * CharWidth * CharHeight];
        int atlasWidth = glyphs.Count * CharWidth;
        for (int g = 0; g < glyphs.Count; g++)
            for (int col = 0; col < 5; col++)
                for (int row = 0; row < CharHeight; row++)
                    if ((glyphs[g][col] & (1 << row)) != 0)
                        pixels[row * atlasWidth + g * CharWidth + col] = Color.White;

        _atlas = new Texture2D(device, atlasWidth, CharHeight);
        _atlas.SetData(pixels);
        _smooth = BuildSmoothAtlas(device, glyphs);

        void AddAccented(char c, char baseChar, byte[] accent) => Add(c, Merge(Slice(baseChar), accent));
    }

    /// <summary>
    /// The same glyphs smoothed with Scale2x twice (4x), so the enhanced look's text keeps the Oric
    /// letter shapes but with diagonal strokes instead of stair steps.
    /// </summary>
    private static Texture2D BuildSmoothAtlas(GraphicsDevice device, List<byte[]> glyphs)
    {
        int cw = CharWidth * SmoothFactor, chh = CharHeight * SmoothFactor;
        int width = glyphs.Count * SmoothStride;
        var data = new Color[width * chh];
        for (int g = 0; g < glyphs.Count; g++)
        {
            var bits = new bool[CharWidth, CharHeight];
            for (int col = 0; col < 5; col++)
                for (int row = 0; row < CharHeight; row++)
                    bits[col, row] = (glyphs[g][col] & (1 << row)) != 0;
            var big = Scale2x(Scale2x(bits));
            for (int x = 0; x < cw; x++)
                for (int y = 0; y < chh; y++)
                    if (big[x, y]) data[y * width + g * SmoothStride + SmoothGutter + x] = Color.White;
        }
        var tex = new Texture2D(device, width, chh);
        tex.SetData(data);
        return tex;
    }

    public static bool[,] Scale2x(bool[,] src)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var dst = new bool[w * 2, h * 2];
        bool At(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && src[x, y];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                bool p = src[x, y], a = At(x, y - 1), b = At(x + 1, y), c = At(x - 1, y), d = At(x, y + 1);
                dst[2 * x, 2 * y] = c == a && c != d && a != b ? a : p;
                dst[2 * x + 1, 2 * y] = a == b && a != c && b != d ? b : p;
                dst[2 * x, 2 * y + 1] = d == c && d != b && c != a ? c : p;
                dst[2 * x + 1, 2 * y + 1] = b == d && b != a && d != c ? d : p;
            }
        return dst;
    }

    /// <summary>Smoothed text: each character is 6 x 8 virtual pixels at scale 1 (drawn with linear filtering).</summary>
    public void DrawSmooth(SpriteBatch sb, string text, Vector2 position, Color color, float scale = 1f)
    {
        float cx = position.X;
        int cw = CharWidth * SmoothFactor, chh = CharHeight * SmoothFactor;
        float s = scale / SmoothFactor;
        foreach (char ch in text)
        {
            if (ch != ' ')
            {
                if (!_index.TryGetValue(ch, out int g)) _index.TryGetValue('?', out g);
                sb.Draw(_smooth, new Vector2(cx, position.Y), new Rectangle(g * SmoothStride + SmoothGutter, 0, cw, chh), color, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
            }
            cx += CharWidth * scale;
        }
    }

    /// <summary>One smoothed glyph's source in the smooth atlas (for building textures such as the logo).</summary>
    public Texture2D SmoothAtlas => _smooth;

    public Rectangle SmoothGlyph(char ch)
    {
        if (!_index.TryGetValue(ch, out int g)) _index.TryGetValue('?', out g);
        return new Rectangle(g * SmoothStride + SmoothGutter, 0, CharWidth * SmoothFactor, CharHeight * SmoothFactor);
    }

    /// <summary>The raw 5x7 columns of a glyph (bit 0 = top row).</summary>
    public static byte[] Columns(char c) => c >= 0x20 && c <= 0x7E ? Slice(c) : Slice('?');

    private static byte[] Slice(int c)
    {
        var cols = new byte[5];
        System.Array.Copy(Ascii, (c - 0x20) * 5, cols, 0, 5);
        return cols;
    }

    private static byte[] Merge(byte[] a, byte[] b)
    {
        var r = new byte[5];
        for (int i = 0; i < 5; i++) r[i] = (byte)(a[i] | b[i]);
        return r;
    }

    public bool HasGlyph(char c) => _index.ContainsKey(c);

    public static int Measure(string text, int scale = 1) => text.Length * CharWidth * scale;

    public void Draw(SpriteBatch sb, string text, float x, float y, Color color, int scale = 1, int clipWidth = 240)
    {
        float cx = x;
        foreach (char ch in text)
        {
            // Skip spaces and anything off-screen (the credits ticker is far wider than the screen).
            if (ch != ' ' && cx > -CharWidth * scale && cx < clipWidth)
            {
                if (!_index.TryGetValue(ch, out int g)) _index.TryGetValue('?', out g);
                // Sub-pixel position so scrolling text glides rather than stepping a whole Oric pixel.
                sb.Draw(_atlas, new Vector2(cx, y), new Rectangle(g * CharWidth, 0, CharWidth, CharHeight),
                    color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
            cx += CharWidth * scale;
        }
    }

    /// <summary>One glyph with independent horizontal/vertical scale (for the rotating credits drum).</summary>
    public void DrawGlyph(SpriteBatch sb, char ch, Vector2 position, Color color, Vector2 scale)
    {
        if (ch == ' ') return;
        if (!_index.TryGetValue(ch, out int g)) _index.TryGetValue('?', out g);
        sb.Draw(_atlas, position, new Rectangle(g * CharWidth, 0, CharWidth, CharHeight), color, 0f,
            new Vector2(CharWidth / 2f, CharHeight / 2f), scale, SpriteEffects.None, 0f);
    }
}
