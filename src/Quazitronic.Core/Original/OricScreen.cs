using System;

namespace Quazitronic.Original;

/// <summary>
/// A picture in the Oric Atmos HIRES format: 224 rows of 40 bytes, each byte either six pixels
/// (bit 6 set; bit 7 = inverse) or a serial attribute that changes the ink, paper or style for the
/// rest of the row. ORIGINAL mode draws into it with the same operations the Oric game used, and
/// <see cref="Decode"/> turns it into colours exactly as the Oric's video chip shows it.
/// </summary>
public sealed class OricScreen
{
    public const int Width = 240, Height = 224, Stride = 40;

    /// <summary>The Oric's eight colours (0 black … 7 white), packed RGBA (little-endian ABGR).</summary>
    public static readonly uint[] Palette =
    {
        0xFF000000, 0xFF0000FF, 0xFF00FF00, 0xFF00FFFF,
        0xFFFF0000, 0xFFFF00FF, 0xFFFFFF00, 0xFFFFFFFF,
    };

    public readonly byte[] Bytes = new byte[Height * Stride];

    public byte this[int col, int row]
    {
        get => (uint)col < Stride && (uint)row < Height ? Bytes[row * Stride + col] : (byte)0;
        set { if ((uint)col < Stride && (uint)row < Height) Bytes[row * Stride + col] = value; }
    }

    public void Clear(byte value = 0x40) => Array.Fill(Bytes, value);

    /// <summary>Fills w bytes x h rows at byte column col, row y (the original's fill_rect).</summary>
    public void Rect(int col, int y, int w, int h, byte value)
    {
        for (int r = y; r < y + h; r++)
            for (int c = col; c < col + w; c++)
                this[c, r] = value;
    }

    /// <summary>Copies rows of 40 bytes (a whole-width image) starting at row y.</summary>
    public void Rows(byte[] image, int y)
    {
        int n = Math.Min(image.Length, (Height - y) * Stride);
        Array.Copy(image, 0, Bytes, y * Stride, n);
    }

    /// <summary>Draws a glyph of the original's 6 x 8 font at byte column col, row y (hchar).</summary>
    public void Char(int col, int y, char ch, byte inverse = 0)
    {
        int g = ch - 32;
        if (g < 0 || g >= 64) g = 0;
        for (int r = 0; r < 8; r++)
            this[col, y + r] = (byte)(OriginalData.Font[g * 8 + r] | 0x40 | inverse);
    }

    public void Text(int col, int y, string s)
    {
        for (int i = 0; i < s.Length; i++) Char(col + i, y, char.ToUpperInvariant(s[i]));
    }

    /// <summary>The masked sprite blit (spr_draw): attribute bytes on screen are left alone.</summary>
    internal void Sprite(SpriteData s, int phase, int col, int y, int rows, int cols, int offset)
    {
        var data = s.Phase(phase);
        int maskBase = s.Height * s.Width;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int o = offset + r * s.Width + c;
                int i = (y + r) * Stride + col + c;
                if ((uint)i >= Bytes.Length) continue;
                byte b = Bytes[i];
                if (b < 0x40) continue;
                Bytes[i] = (byte)((b & data[maskBase + o]) | data[o]);
            }
    }

    /// <summary>Turns the bytes into pixels the way the Oric's ULA does: each row starts white on black.</summary>
    public void Decode(uint[] pixels)
    {
        for (int y = 0; y < Height; y++)
        {
            int ink = 7, paper = 0;
            int o = y * Width;
            for (int c = 0; c < Stride; c++)
            {
                byte b = Bytes[y * Stride + c];
                int inv = (b & 0x80) != 0 ? 7 : 0;
                if ((b & 0x60) == 0)
                {
                    int v = b & 0x1F;
                    if (v < 8) ink = v;
                    else if (v >= 16 && v < 24) paper = v - 16;
                    uint col = Palette[paper ^ inv];
                    for (int p = 0; p < 6; p++) pixels[o + c * 6 + p] = col;
                    continue;
                }
                uint on = Palette[ink ^ inv], off = Palette[paper ^ inv];
                for (int p = 0; p < 6; p++)
                    pixels[o + c * 6 + p] = (b & (0x20 >> p)) != 0 ? on : off;
            }
        }
    }
}
