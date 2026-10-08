using System;

namespace Quazitronic.Original;

/// <summary>A pre-shifted Oric sprite: rows, width in screen bytes, centre x offset, and for each of the
/// two phases (0 and 3 pixels shifted) the data bytes followed by the mask bytes (bit set = keep background).</summary>
internal readonly record struct SpriteData(int Height, int Width, int XOffset, byte[] Phase0, byte[] Phase1)
{
    public byte[] Phase(int p) => p == 0 ? Phase0 : Phase1;
}

/// <summary>One deck as the Oric draws it: a map of 1-byte x 6-row cells over the deck's planar charset,
/// plus the paper attribute the leftmost visible column needs for each cell.</summary>
internal readonly record struct DeckPicture(int Columns, int Rows, int X0, int Y0, int Ink, int Void, int Style, byte[] LeftFix, byte[] Cells)
{
    public byte Cell(int col, int row) => (uint)col < Columns && (uint)row < Rows ? Cells[row * Columns + col] : (byte)0;
}

internal static partial class OriginalData
{
    public const int BoomSprite0 = 9, BoomSprite1 = 10, BulletSprite = 11;

    private static byte[] Unpack(string b64) => Convert.FromBase64String(b64);
}
