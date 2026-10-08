using System;
using System.Collections.Generic;
using Quazitronic.Game;

namespace Quazitronic.Original;

/// <summary>
/// ORIGINAL mode's picture: the Oric version's screen recreated natively from its own graphics
/// data. A port of the original's render(), pf_rect, put_sprite, set_clip, panel and text-screen
/// code, drawing into an <see cref="OricScreen"/>. The camera moves in the original's 6-pixel /
/// 3-row steps, once per game frame.
/// </summary>
public sealed class OriginalRenderer
{
    private const int PfTop = 8, PfBot = 152, PanelY = 152, FieldY = PanelY + 16, BarY = PanelY + 29;
    private const int CStat = 4, CGbox = 18, CUnit = 28, CScore = 32, CBar = 3;
    private const int PfC0 = 2, PfC1 = 39;

    public OricScreen Screen { get; } = new();

    private int _deck = -1;
    private int _camX, _camY;
    private long _cameraTicks = -1;

    // ------------------------------------------------------------------ camera

    private DeckPicture Pic(Session s) => OriginalData.Decks[s.DeckIndex];

    /// <summary>Follows the player; snaps on a new deck (cam_follow).</summary>
    private void Follow(Session s, bool snap)
    {
        var pic = Pic(s);
        int tx = pic.X0 + s.PlayerX - s.PlayerY - 108;
        int ty = pic.Y0 + ((s.PlayerX + s.PlayerY) >> 1) - s.Deck.FloorZ(s.PlayerX, s.PlayerY) - 70;
        if (snap)
        {
            _camX = tx - tx % 6;
            _camY = ty - ty % 3;
        }
        else
        {
            if (tx > _camX + 6) _camX += 6;
            else if (tx < _camX - 6) _camX -= 6;
            if (ty > _camY + 4) _camY += 3;
            else if (ty < _camY - 4) _camY -= 3;
        }
        int xmax = (pic.Columns - 37) * 6, ymax = pic.Rows * 6 - (PfBot - PfTop);
        _camX = Math.Clamp(_camX, 0, Math.Max(0, xmax));
        _camY = Math.Clamp(_camY, 0, Math.Max(0, ymax));
    }

    /// <summary>Steps the camera once per elapsed game frame.</summary>
    public void Advance(Session s, long ticks)
    {
        if (s.DeckIndex != _deck || _cameraTicks < 0)
        {
            _deck = s.DeckIndex;
            Follow(s, true);
            _cameraTicks = ticks;
            return;
        }
        long n = Math.Min(ticks - _cameraTicks, 5);
        for (long i = 0; i < n; i++) Follow(s, false);
        _cameraTicks = ticks;
    }

    // ------------------------------------------------------------------ whole screens

    /// <summary>Draws whatever the game is showing: the deck, a text screen or the transfer battle.</summary>
    public void Draw(Session s, long ticks)
    {
        var sc = Screen;
        TextLines();
        Panel(s);
        switch (s.View)
        {
            case View.Deck:
                Advance(s, ticks);
                DeckView(s);
                break;
            case View.Briefing:
                FrameOpen();
                Logo(30);
                UnitLine(64, s.Opponent);
                FText(82, 6, "PREPARE TO ENGAGE");
                FText(94, 6, "SECURITY DEVICE");
                break;
            case View.Captured:
                FrameOpen();
                Logo(30);
                UnitLine(64, s.Opponent);
                FText(76, 6, "SECURITY CLASS");
                FText(88, 3, Droids.Security[s.Opponent]);
                FText(106, 2, "TRANSFER COMPLETE");
                break;
            case View.End:
                FrameOpen();
                Logo(30);
                if (s.Won)
                {
                    FText(64, 3, "THE SHIP IS SECURED");
                    FText(76, 6, "ALL DECKS CLEARED");
                }
                else
                {
                    FText(64, 3, "GAME OVER");
                    FText(76, 6, "INFLUENCE DEVICE DESTROYED");
                }
                FText(98, 2, "SCORE");
                FText(110, 2, (s.Score * 10).ToString("000000"));
                break;
            case View.Transfer:
                TransferScreen(s);
                break;
        }
        _ = sc;
    }

    /// <summary>The title screen with a menu in place of the tape's key list.</summary>
    public void DrawTitle(IReadOnlyList<string> menu, int selected, int best, bool blink)
    {
        TextLines();
        FrameOpen();
        Screen.Rows(OriginalData.Panel, PanelY);
        Field(CStat, 8, "");
        Field(CUnit, 2, "");
        Field(CScore, 6, Num(best / 10, 5) + "0");
        Logo(28);
        FText(54, 6, "ORIC ATMOS VERSION");
        FText(66, 6, "AFTER THE GRAFTGOLD GAME");
        int y = 84;
        for (int i = 0; i < menu.Count; i++, y += 10)
        {
            string text = i == selected ? (blink ? "> " + menu[i] + " <" : "  " + menu[i] + "  ") : menu[i];
            FText(y, (byte)(i == selected ? 3 : 2), text);
        }
    }

    /// <summary>Area of each title menu line in Oric pixels (for taps).</summary>
    public static (int Y, int Height) TitleLine(int index) => (84 + index * 10 - 1, 10);

    // ------------------------------------------------------------------ pieces

    private void TextLines()
    {
        // The three text lines under the HIRES picture: red paper, as the tape sets them.
        Screen.Rect(0, 200, 1, 24, 0x11);
        Screen.Rect(1, 200, 39, 24, 0x40);
    }

    private void FrameOpen()
    {
        Screen.Rect(0, 0, 1, PfTop, 0x11);
        Screen.Rect(1, 0, 39, PfTop, 0x40);
        Screen.Rows(OriginalData.Frame, PfTop);
    }

    private void FText(int y, byte ink, string s)
    {
        Screen.Rect(4, y, 1, 8, ink);
        Screen.Text(6 + (29 - s.Length) / 2, y, s);
    }

    private void Logo(int y)
    {
        Screen.Rect(4, y, 1, 18, 3);
        var logo = OriginalData.Logo;
        for (int r = 0; r < 18; r++)
            for (int c = 0; c < 26; c++)
                Screen[7 + c, y + r] = logo[r * 26 + c];
    }

    private void UnitLine(int y, int t) =>
        FText(y, 6, "UNIT.. " + (t != 0 ? Droids.Code[t] + " " : "") + Droids.Name[t]);

    private static string Num(int v, int digits)
    {
        var c = new char[digits];
        for (int i = digits - 1; i >= 0; i--) { c[i] = (char)('0' + v % 10); v /= 10; }
        return new string(c);
    }

    private void Field(int col, int n, string s)
    {
        for (int i = 0; i < n; i++) Screen.Char(col + i, FieldY, i < s.Length ? s[i] : ' ');
    }

    /// <summary>The red status panel and its fields (panel_update, stat, gbox).</summary>
    private void Panel(Session s)
    {
        Screen.Rows(OriginalData.Panel, PanelY);
        Field(CStat, 8, s.Status);
        Field(CUnit, 2, Droids.Code[s.PlayerType]);
        Field(CScore, 5, Num(s.Score, 5));
        Field(CScore + 5, 1, "0");
        int max = Droids.MaxHp[s.PlayerType];
        int n = s.PlayerHp * 10 / max;
        if (s.PlayerHp > 0 && n == 0) n = 1;
        Screen.Rect(CBar, BarY, 10, 3, 0x40);
        if (n > 0)
        {
            Screen.Rect(CBar, BarY, 1, 3, 0x13);
            if (n < 10) Screen.Rect(CBar + n, BarY, 1, 3, 0x11);
        }
        Screen.Rect(CGbox, PanelY + 15, 1, 10, (byte)(s.GrappleLit ? 0x17 : 0x15));
    }

    // ------------------------------------------------------------------ the deck

    private int OrgX(DeckPicture p) => p.X0 + 12 - _camX;
    private int OrgY(DeckPicture p) => p.Y0 + PfTop + 3 - _camY;

    private void DeckView(Session s)
    {
        var pic = Pic(s);
        var sc = Screen;
        sc.Rect(0, 0, 1, PfTop, 0x11);
        sc.Rect(1, 0, 39, PfTop, 0x40);
        sc.Rect(0, PfTop, 1, PfBot - PfTop, 0x11);
        sc.Rect(1, PfTop, 1, PfBot - PfTop, (byte)pic.Ink);
        sc.Rect(39, PfTop, 1, PfBot - PfTop, 0x11);

        // the playfield from the cell map (pf_all): column 2 holds the left cell's paper fix
        var planes = OriginalData.Charsets[pic.Style];
        int camC = _camX / 6, camCr = _camY / 6, camSub = _camY % 6;
        for (int y = PfTop; y < PfBot; y++)
        {
            int v = y - PfTop + camSub;
            int row = camCr + v / 6, sub = v % 6;
            sc[PfC0, y] = pic.LeftFix[pic.Cell(camC, row)];
            for (int c = PfC0 + 1; c < PfC1; c++)
                sc[c, y] = planes[sub * 256 + pic.Cell(camC + (c - PfC0), row)];
        }

        // the sprites, sorted by their bottom row
        int orgX = OrgX(pic), orgY = OrgY(pic);
        var q = new List<(int K, int X, int Y, int Z, int Sprite, bool Player)>();
        void Add(int x, int y, int z, int spr, bool player = false)
        {
            if (q.Count >= 23) return;
            int k = Math.Clamp(orgY + ((x + y) >> 1) - z, PfTop, PfBot);
            q.Add((k, x, y, z, spr, player));
        }
        for (int a = 0; a < s.DeckCount; a++)
            if (s.DroidAlive(a) && Session.CloseTo(s.DroidX[a], s.DroidY[a], s.PlayerX, s.PlayerY, 120))
                Add(s.DroidX[a], s.DroidY[a], s.Deck.FloorZ(s.DroidX[a], s.DroidY[a]), s.DroidType(a));
        if (s.PlayerVisible)
            Add(s.PlayerX, s.PlayerY, s.Deck.FloorZ(s.PlayerX, s.PlayerY), 0, true);
        for (int b = 0; b < Session.MaxBullets; b++)
            if (s.BulletOn[b]) Add(s.BulletX[b], s.BulletY[b], s.BulletZ[b] + 8, OriginalData.BulletSprite);
        for (int x = 0; x < Session.MaxBooms; x++)
            if (s.BoomTime[x] != 0)
                Add(s.BoomX[x], s.BoomY[x], s.BoomZ[x], (s.BoomTime[x] & 4) != 0 ? OriginalData.BoomSprite1 : OriginalData.BoomSprite0);
        // stable insertion sort, as the original
        for (int i = 1; i < q.Count; i++)
            for (int j = i; j > 0 && q[j - 1].K > q[j].K; j--)
                (q[j - 1], q[j]) = (q[j], q[j - 1]);
        foreach (var e in q)
            PutSprite(pic, orgX, orgY, e.X, e.Y, e.Z, e.Sprite, e.Player ? PfBot : Clip(s, pic, e.X, e.Y, e.Z));
        // the player once more on top, so a droid in front can never hide it
        foreach (var e in q)
            if (e.Player) { PutSprite(pic, orgX, orgY, e.X, e.Y, e.Z, e.Sprite, PfBot); break; }
    }

    /// <summary>The screen row below which a sprite is hidden by a taller tile in front (set_clip).</summary>
    private int Clip(Session s, DeckPicture pic, int x, int y, int z)
    {
        int i = (x & 0xFF) / 12, j = (y & 0xFF) / 12, best = PfBot;
        for (int k = 0; k < 3; k++)
        {
            int ti = i + (k != 1 ? 1 : 0), tj = j + (k != 0 ? 1 : 0);
            if (((ti | tj) & 0xF0) != 0) continue;
            byte t = s.Deck.Tile(ti, tj);
            int h = (t & 3) * 12 + ((t >> 2) == (int)TileKind.Wall ? 36 : 0);
            if (h <= z) continue;
            int c = pic.Y0 - _camY + PfTop + 6 + (ti + tj) * 6 - h;
            if (c < best) best = c;
        }
        return best < PfTop ? PfTop : best;
    }

    /// <summary>Draws a sprite with its bottom centre at world (x, y), z rows up, clipped (put_sprite).</summary>
    private void PutSprite(DeckPicture pic, int orgX, int orgY, int x, int y, int z, int spr, int clip)
    {
        var d = OriginalData.Sprites[spr];
        int h = d.Height, wid = d.Width;
        int sx = orgX + x - y - d.XOffset;
        int sy = orgY + ((x + y) >> 1) - z - h;
        int skip, row0;
        if (sy < PfTop)
        {
            skip = PfTop - sy;
            if (skip >= h) return;
            row0 = PfTop;
        }
        else
        {
            if (sy >= PfBot) return;
            skip = 0;
            row0 = sy;
        }
        int rows = h - skip;
        if (row0 + rows > PfBot) rows = PfBot - row0;
        if (row0 >= clip) return;
        if (row0 + rows >= clip) rows = clip - row0;
        int v = sx + 60;
        if (v < 0) return;
        int col = v / 6 - 10, phase = v % 6 >= 3 ? 1 : 0;
        if (col + wid < PfC0 + 1) return;
        int lskip = 0;
        if (col < PfC0) { lskip = PfC0 - col; col = PfC0; }
        else if (col >= PfC1) return;
        int cols = wid - lskip;
        if (col + cols > PfC1) cols = PfC1 - col;
        if (rows <= 0 || cols <= 0) return;
        Screen.Sprite(d, phase, col, row0, rows, cols, lskip + skip * wid);
    }

    // ------------------------------------------------------------------ the transfer battle

    private const int Tw0 = 6, Twl = 11, Tcc = 17;
    private static int WireY(int w) => 38 + (w << 3);
    private static readonly byte[] ArrowR = { 0x60, 0x70, 0x78, 0x7C, 0x7C, 0x78, 0x70, 0x60 };
    private static readonly byte[] ArrowL = { 0x41, 0x43, 0x47, 0x4F, 0x4F, 0x47, 0x43, 0x41 };
    private static readonly byte[] StackG = { 0x60, 0x78, 0x7E, 0x78, 0x60 };

    private void TransferScreen(Session s)
    {
        var sc = Screen;
        var tr = s.Transfer;
        sc.Rect(0, 0, 1, PfBot, 0x11);
        sc.Rect(1, 0, 1, PfBot, 0x03);
        sc.Rect(2, 0, 38, PfBot, 0x40);
        sc.Rect(20, PfTop, 1, PfBot - PfTop, 0x11);
        sc.Rect(21, PfTop, 1, PfBot - PfTop, 0x04);
        sc.Rect(33, PfTop, 1, PfBot - PfTop, 0x11);
        sc.Rect(Tcc, 19, 1, 122, 0x10);
        sc.Rect(4, 31, 1, 112, 0x5E);
        sc.Rect(34, 31, 1, 112, 0x5E);
        for (int w = 0; w < Transfer.Wires; w++)
        {
            Wire(tr, w, false);
            Wire(tr, w, true);
        }
        for (int w = 0; w < Transfer.Wires; w++)
        {
            Pulse(tr, w, false);
            Pulse(tr, w, true);
            Cell(tr, w);
        }
        // the two droids
        var me = OriginalData.Sprites[tr.PlayerType];
        sc.Sprite(me, 0, 3, 10, me.Height, me.Width, 0);
        var them = OriginalData.Sprites[tr.EnemyType];
        sc.Sprite(them, 0, 33, 10, them.Height, them.Width, 0);
        if (tr.ResultStep < 0) Arrow(3, tr.Cursor, false);
        Stack(2, tr.PlayerPulses);
        Stack(36, tr.EnemyPulses);
        // the border flashes in the result
        if (tr.FlashPhase >= 0)
            sc.Rect(0, PfTop, 1, PfBot - PfTop, (byte)((tr.FlashPhase & 1) != 0 ? 0x11 : (tr.ResultWin ? 0x13 : 0x10)));
    }

    private void Cell(Transfer tr, int w)
    {
        Screen.Rect(Tcc, WireY(w) - 3, 1, 7, (byte)(tr.Owner[w] != 0 ? 0x14 : 0x13));
        Screen.Rect(Tcc, 21, 1, 10, (byte)(tr.Count(1) > Transfer.Wires / 2 ? 0x14 : 0x13));
    }

    private void Wire(Transfer tr, int w, bool right)
    {
        var sc = Screen;
        int y = WireY(w), k = right ? tr.RightKind[w] : tr.LeftKind[w];
        sc.Rect(right ? 22 : Tw0, y - 3, Twl, 8, 0x40);
        if (right)
        {
            sc.Rect(20, y, 1, 2, 0x10);
            if (k == 1) { sc.Rect(20, y, 1, 2, 0x11); sc.Rect(28, y, 1, 2, 0x10); }
            if (k == 3) sc.Rect(27, y, 1, 2, 0x11);
        }
        else
        {
            sc.Rect(5, y, 1, 2, 0x10);
            if (k == 1) sc.Rect(10, y, 1, 2, 0x11);
            if (k == 3) { sc.Rect(5, y, 1, 2, 0x11); sc.Rect(12, y, 1, 2, 0x10); }
        }
        if (k == 2) sc.Rect(right ? 27 : 11, y - 2, 1, 13, 0x7E);
    }

    private int PulseCol(bool right, int offset) => right ? 32 - offset : Tw0 + offset;

    private void Dash(int col, int w)
    {
        Screen.Rect(col, WireY(w) - 3, 1, 8, 0x40);
        Screen.Rect(col, WireY(w), 1, 2, 0x7C);
    }

    /// <summary>A pulse travelling down a wire leaves the wire dashed (powered) behind it.</summary>
    private void Pulse(Transfer tr, int w, bool right)
    {
        int p = right ? tr.RightPulse[w] : tr.LeftPulse[w];
        bool lit = (right ? tr.RightLit[w] : tr.LeftLit[w]) != 0;
        int kind = right ? tr.RightKind[w] : tr.LeftKind[w];
        int end = Transfer.End(kind);
        if (p == 0)
        {
            if (!lit) return;
            for (int o = 0; o < end; o++) Dash(PulseCol(right, o), w);
            if (kind == 2) for (int o = 6; o < end; o++) Dash(PulseCol(right, o), w + 1);
            return;
        }
        for (int o = 0; o <= p - 3; o++)
        {
            Dash(PulseCol(right, o), w);
            if (kind == 2 && o >= 6) Dash(PulseCol(right, o), w + 1);
        }
        if (p >= 2)
        {
            Arrow(PulseCol(right, p - 2), w, right);
            if (kind == 2 && p - 1 > 6) Arrow(PulseCol(right, p - 2), w + 1, right);
        }
    }

    private void Arrow(int col, int w, bool right)
    {
        var a = right ? ArrowL : ArrowR;
        int y = WireY(w) - 3;
        for (int r = 0; r < 8; r++) Screen[col, y + r] = a[r];
    }

    private void Stack(int col, int n)
    {
        Screen.Rect(col, 36, 1, 56, 0x40);
        for (int k = 0; k < n && k < 7; k++)
            for (int r = 0; r < 5; r++)
                Screen[col, 36 + k * 8 + r] = StackG[r];
    }
}
