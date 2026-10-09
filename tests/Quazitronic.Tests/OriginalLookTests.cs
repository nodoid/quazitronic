using Quazitronic.Game;
using Quazitronic.Original;
using Xunit;

namespace Quazitronic.Tests;

/// <summary>ORIGINAL mode: the Oric version's screen, recreated natively from its own graphics data.</summary>
public sealed class OriginalLookTests
{
    private static uint[] Render(OricScreen s)
    {
        var px = new uint[OricScreen.Width * OricScreen.Height];
        s.Decode(px);
        return px;
    }

    [Fact]
    public void SerialAttributesDecodeAsOnTheOric()
    {
        var s = new OricScreen();
        s.Clear();
        s[0, 0] = 0x11;   // paper red
        s[1, 0] = 0x03;   // ink yellow
        s[2, 0] = 0x7F;   // six ink pixels
        s[3, 0] = 0xC0;   // inverse, no pixels: red paper inverted is cyan
        var px = Render(s);
        Assert.Equal(OricScreen.Palette[1], px[0]);
        Assert.Equal(OricScreen.Palette[1], px[6]);
        Assert.Equal(OricScreen.Palette[3], px[12]);
        Assert.Equal(OricScreen.Palette[6], px[18]);
        Assert.Equal(OricScreen.Palette[0], px[OricScreen.Width]); // each row starts black
    }

    [Fact]
    public void TheOriginalDataIsComplete()
    {
        Assert.Equal(64 * 8, OriginalData.Font.Length);
        Assert.Equal(12, OriginalData.Sprites.Length);
        foreach (var sp in OriginalData.Sprites)
        {
            Assert.Equal(sp.Height * sp.Width * 2, sp.Phase0.Length);
            Assert.Equal(sp.Height * sp.Width * 2, sp.Phase1.Length);
        }
        Assert.Equal(Session.Decks, OriginalData.Decks.Length);
        for (int d = 0; d < Session.Decks; d++)
        {
            var pic = OriginalData.Decks[d];
            Assert.Equal(pic.Columns * pic.Rows, pic.Cells.Length);
            Assert.Equal(DeckData.Get(d).OricInk, pic.Ink);
            Assert.InRange(pic.Style, 0, 1);
        }
        Assert.Equal(48 * 40, OriginalData.Panel.Length);
        Assert.Equal(144 * 40, OriginalData.Frame.Length);
        Assert.Equal(18 * 26, OriginalData.Logo.Length);
    }

    [Fact]
    public void TheDeckScreenHasThePlayfieldAndThePanel()
    {
        var s = new Session(7);
        s.Tick(default);
        var r = new OriginalRenderer();
        r.Draw(s, 1);
        var px = Render(r.Screen);
        var deckInk = OricScreen.Palette[DeckData.Get(0).OricInk];
        // red border columns, deck-coloured pixels in the playfield, the yellow status field with "DECK 1"
        Assert.Equal(OricScreen.Palette[1], px[20 * OricScreen.Width]);
        Assert.Contains(deckInk, px.AsSpan(8 * OricScreen.Width, 144 * OricScreen.Width).ToArray());
        Assert.Contains(OricScreen.Palette[3], px.AsSpan(152 * OricScreen.Width, 48 * OricScreen.Width).ToArray());
        Assert.Equal(0x40 | OriginalData.Font[('D' - 32) * 8 + 2], r.Screen[4, 168 + 2]);
    }

    [Fact]
    public void ThePlayerIsDrawnOnTheDeck()
    {
        var s = new Session(7);
        s.Tick(default);
        var withPlayer = new OriginalRenderer();
        withPlayer.Draw(s, 1);
        var before = (byte[])withPlayer.Screen.Bytes.Clone();
        for (int i = 0; i < 40; i++) s.Tick(new Controls { Right = true });
        withPlayer.Draw(s, 41);
        Assert.NotEqual(before, withPlayer.Screen.Bytes); // the picture follows the game
    }

    [Fact]
    public void TextScreensAndTheTransferBattleDraw()
    {
        var s = new Session(9);
        s.Tick(default);
        s.DroidX[0] = s.PlayerX + 10; s.DroidY[0] = s.PlayerY + 10;
        s.Tick(new Controls { Grapple = true });
        var r = new OriginalRenderer();
        var seen = new HashSet<View>();
        for (int i = 0; i < 400; i++)
        {
            s.Tick(default);
            r.Draw(s, i + 2);
            seen.Add(s.View);
            if (s.View == View.Briefing)
                Assert.Equal(0x40 | OriginalData.Font[('P' - 32) * 8 + 1], r.Screen[6 + (29 - 17) / 2, 82 + 1]); // PREPARE TO ENGAGE
        }
        Assert.Contains(View.Briefing, seen);
        Assert.Contains(View.Transfer, seen);
    }

    [Fact]
    public void TheTitleScreenShowsTheMenu()
    {
        var r = new OriginalRenderer();
        r.DrawTitle(new[] { "PLAY", "INSTRUCTIONS" }, 0, 12340, false);
        var (y, _) = OriginalRenderer.TitleLine(1);
        int col = 6 + (29 - "INSTRUCTIONS".Length) / 2;
        Assert.Equal(0x40 | OriginalData.Font[('I' - 32) * 8 + 3], r.Screen[col, y + 1 + 3]);
        string dir = Path.Combine(Path.GetTempPath(), "orictron-tests");
        Directory.CreateDirectory(dir);
        PngWriter.Write(Path.Combine(dir, "original-title.png"), Render(r.Screen), 240, 224, 3);
    }
}
