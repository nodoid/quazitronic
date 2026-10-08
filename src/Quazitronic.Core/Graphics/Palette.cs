using Microsoft.Xna.Framework;

namespace Quazitronic.Graphics;

/// <summary>The Oric's eight colours, and the enhanced game's colour schemes.</summary>
public static class Palette
{
    public static readonly Color[] Oric =
    {
        new(0, 0, 0), new(255, 0, 0), new(0, 255, 0), new(255, 255, 0),
        new(0, 0, 255), new(255, 0, 255), new(0, 255, 255), new(255, 255, 255),
    };

    public static Color OricBlack => Oric[0];
    public static Color OricRed => Oric[1];
    public static Color OricGreen => Oric[2];
    public static Color OricYellow => Oric[3];
    public static Color OricBlue => Oric[4];
    public static Color OricMagenta => Oric[5];
    public static Color OricCyan => Oric[6];
    public static Color OricWhite => Oric[7];

    // Enhanced UI colours, after the original's status panel.
    public static readonly Color Gold = new(255, 214, 64);
    public static readonly Color Amber = new(255, 150, 40);
    public static readonly Color Sky = new(110, 220, 255);
    public static readonly Color Ice = new(200, 240, 255);
    public static readonly Color Violet = new(200, 90, 255);
    public static readonly Color Mint = new(90, 255, 160);
    public static readonly Color Alarm = new(255, 70, 60);
    public static readonly Color PanelRed = new(150, 18, 26);
    public static readonly Color Ink = new(8, 10, 22);

    /// <summary>The player's side in the transfer battle (yellow) and the droid's (blue).</summary>
    public static readonly Color PlayerSide = new(255, 210, 40);
    public static readonly Color DroidSide = new(60, 130, 255);
}

/// <summary>
/// The look of one deck in the enhanced game, derived from its Oric ink colour: tinted metal for the
/// structure and a saturated accent for lights, pads and trims.
/// </summary>
public readonly record struct DeckTheme(Color Metal, Color Trim, Color Accent, Color Void, Color Nebula)
{
    public static DeckTheme For(int oricInk) => oricInk switch
    {
        6 => new(new Color(170, 215, 220), new Color(90, 130, 140), new Color(60, 240, 255), new Color(2, 8, 24), new Color(20, 70, 120)),
        4 => new(new Color(150, 165, 230), new Color(70, 80, 140), new Color(90, 130, 255), new Color(2, 2, 14), new Color(40, 30, 110)),
        2 => new(new Color(170, 220, 180), new Color(80, 130, 95), new Color(80, 255, 140), new Color(2, 10, 16), new Color(20, 80, 90)),
        5 => new(new Color(215, 170, 225), new Color(130, 80, 140), new Color(255, 90, 240), new Color(10, 2, 22), new Color(90, 30, 120)),
        3 => new(new Color(230, 215, 165), new Color(140, 120, 70), new Color(255, 200, 50), new Color(12, 6, 18), new Color(110, 60, 40)),
        _ => new(new Color(215, 220, 232), new Color(120, 126, 145), new Color(150, 220, 255), new Color(3, 6, 22), new Color(30, 50, 130)),
    };
}
