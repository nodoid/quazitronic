namespace Orictron.Game;

/// <summary>Tile kinds, as in the original's deck generator.</summary>
public enum TileKind : byte
{
    Void = 0,
    Floor = 1,
    Wall = 2,
    /// <summary>The ⤢ step pad: the only way up or down one level.</summary>
    Pad = 3,
    Energiser = 4,
    Lift = 5,
    Console = 6,
}

internal readonly record struct RawDeck(int StartI, int StartJ, int Ink, int Void, int Style, byte[] Tiles);

/// <summary>
/// One deck of the ship: a 16 x 16 isometric tile grid. Each tile byte holds the kind (bits 2-4) and
/// height level 0-3 (bits 0-1). World coordinates are 12 units per tile (0-191), like the original;
/// levels are 12 units high and walls stand three levels tall.
/// </summary>
public sealed class Deck
{
    public const int Size = 16;
    public const int TileUnits = 12;
    public const int LevelHeight = 12;
    public const int WallLevels = 3;

    private readonly byte[] _tiles;

    internal Deck(int index, RawDeck raw)
    {
        Index = index;
        _tiles = raw.Tiles;
        StartI = raw.StartI;
        StartJ = raw.StartJ;
        OricInk = raw.Ink;
        OricVoid = raw.Void;
        Style = raw.Style;
    }

    public int Index { get; }
    /// <summary>The lift tile, where the player arrives.</summary>
    public int StartI { get; }
    public int StartJ { get; }
    /// <summary>The deck's Oric ink colour (7 white, 6 cyan, 4 blue, 2 green, 5 magenta, 3 yellow).</summary>
    public int OricInk { get; }
    /// <summary>Oric colour behind the deck (4 blue, 0 black).</summary>
    public int OricVoid { get; }
    /// <summary>0 = panelled walls, 1 = brick.</summary>
    public int Style { get; }

    public byte Tile(int i, int j) => (uint)i < Size && (uint)j < Size ? _tiles[(j << 4) | i] : (byte)0;
    public TileKind Kind(int i, int j) => (TileKind)(Tile(i, j) >> 2);
    public int Level(int i, int j) => Tile(i, j) & 3;

    /// <summary>The tile at world position (x, y). Coordinates wrap as bytes, as on the Oric.</summary>
    public byte TileAt(int x, int y)
    {
        int i = (x & 0xFF) / TileUnits, j = (y & 0xFF) / TileUnits;
        return i < Size && j < Size ? _tiles[(j << 4) | i] : (byte)0;
    }

    public static bool Walkable(TileKind k) => k is TileKind.Floor or TileKind.Pad or TileKind.Energiser or TileKind.Lift;
    public static bool Walkable(byte tile) => Walkable((TileKind)(tile >> 2));

    /// <summary>Height of the floor at a world position (0, 12, 24 or 36).</summary>
    public int FloorZ(int x, int y) => (TileAt(x, y) & 3) * LevelHeight;

    public int StartX => StartI * TileUnits + 6;
    public int StartY => StartJ * TileUnits + 6;
}

internal static partial class DeckData
{
    public const int Count = 6;
    // Lazy: static field order across the partial files is undefined.
    private static Deck[]? _decks;

    private static Deck[] BuildAll()
    {
        var d = new Deck[Raw.Length];
        for (int i = 0; i < d.Length; i++) d[i] = new Deck(i, Raw[i]);
        return d;
    }

    public static Deck Get(int index) => (_decks ??= BuildAll())[index];
}
