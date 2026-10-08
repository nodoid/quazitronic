using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Quazitronic.Audio;
using Quazitronic.Game;
using Quazitronic.Graphics;
using Quazitronic.Input;

namespace Quazitronic.Screens;

/// <summary>How to play: five pages, in the enhanced or the Oric look.</summary>
public sealed class InstructionsScreen : Screen
{
    private sealed record Page(string Title, Func<QuazitronicGame, string[]> Lines, bool Droids = false);

    private static readonly Page[] Pages =
    {
        new("THE MISSION", _ => new[]
        {
            "You are an influence device, a small robot sent aboard a ship overrun by rogue droids.",
            "",
            "Six decks, linked by lifts, hold 52 droids between them. Clear every deck to secure the ship.",
            "",
            "Shoot droids, ram them, or grapple one and win the transfer battle to take over its body. A bigger host is faster, tougher and hits harder - but it slowly burns out.",
            "",
            "If your host is destroyed you are thrown out as the influence device. Lose that, and the game is over.",
        }),
        new("CONTROLS", g => g.IsMobile ? new[]
        {
            g.UseTilt ? "TILT the device to move: tip the top edge away to go up the screen, towards you to go down, left or right to go across. However you hold it when play starts counts as level."
                      : "Use the D-PAD to move (tilt can be turned on in the menu).",
            "",
            "FIRE shoots in the direction you last moved.",
            "GRAB grapples a droid you are touching. Or hold FIRE while standing still, then run into a droid.",
            "LIFT appears on a lift hatch: ride to the next deck.",
            "II pauses the game.",
            "",
            "In a transfer battle, tilt up and down to choose a wire and press FIRE - or just tap a wire.",
        } : new[]
        {
            "ARROWS or Q A O P    move (screen directions)",
            "SPACE                fire",
            "T or RETURN          grapple a droid you touch",
            "Hold SPACE still     grapple, then run into one",
            "L                    ride the lift",
            "ESC                  pause / quit",
            "F11                  full screen",
            "G                    enhanced or original look",
            "",
            "Gamepad: stick or D-pad moves, A fires, X grapples, Y rides the lift, START pauses.",
            "",
            "Transfer battle: UP and DOWN pick a wire, FIRE sends a pulse.",
        }),
        new("THE DROIDS", _ => Array.Empty<string>(), Droids: true),
        new("TRANSFER", _ => new[]
        {
            "Grappling starts a transfer battle for control of the droid.",
            "",
            "Your side is YELLOW (left), the droid's is BLUE (right). Each side's wires feed a column of 13 cells in the middle.",
            "",
            "Fire a pulse down a wire: when it reaches the middle its cell turns your colour for a few seconds. Some wires are dead ends; some split to feed two cells.",
            "",
            "Pulses are limited, so time them. Hold more cells than the droid when the clock reaches zero and its body is yours. A draw is a DEADLOCK and you fight again.",
            "",
            "Lose, and you are thrown off - losing your host, or half your energy.",
        }),
        new("DECKS AND TIPS", _ => new[]
        {
            "Arrow pads are the only way up or down one level.",
            "Glowing ENERGISER pads recharge you.",
            "Clear a deck, then take the LIFT - it is where you arrived.",
            "",
            "Messengers and workers are harmless; sentries stand still and shoot. The higher the class number, the faster and deadlier the droid. The X9 command unit waits on the last deck.",
            "",
            "Points: destroy a droid for its value, capture it for double, plus 500 for each deck cleared.",
            "",
            "GRAPHICS on the title (or G, or the pause menu) switches between ENHANCED and the ORIGINAL Oric look and sound - even mid-game.",
        }),
    };

    private int _page;

    internal int StartPage { get => _page; init => _page = value; }
    private float _time;
    private float _slide;

    public InstructionsScreen(QuazitronicGame game) : base(game) { }

    public override void Update(float dt)
    {
        _time += dt;
        _slide = Math.Max(0, _slide - dt * 6);
        var input = Game.Input;
        float w = Game.VirtualWidth, h = QuazitronicGame.VirtualHeight;
        bool next = input.Right || input.Confirm || input.Tapped(new RectangleF(w - 40, h - 26, 40, 26));
        bool prev = input.Left || input.Tapped(new RectangleF(0, h - 26, 40, 26));
        bool back = input.Back || input.Tapped(new RectangleF(w / 2 - 30, h - 26, 60, 26));
        if (back || (next && _page == Pages.Length - 1))
        {
            Game.Sounds.Play(Sfx.MenuBack, 0.6f);
            Game.ChangeScreen(new IntroScreen(Game));
            return;
        }
        if (next) { _page++; _slide = 1; Game.Sounds.Play(Sfx.MenuMove, 0.6f); }
        if (prev && _page > 0) { _page--; _slide = 1; Game.Sounds.Play(Sfx.MenuMove, 0.6f); }
    }

    /// <summary>Word-wraps to a width in characters.</summary>
    public static List<string> Wrap(string text, int chars)
    {
        var lines = new List<string>();
        if (text.Length == 0) { lines.Add(""); return lines; }
        if (text.Contains("  ")) { lines.Add(text); return lines; } // pre-formatted table row
        string line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > chars)
            {
                lines.Add(line);
                line = word;
            }
            else line = line.Length == 0 ? word : line + " " + word;
        }
        lines.Add(line);
        return lines;
    }

    public override void Draw(Gfx g)
    {
        var page = Pages[_page];
        float w = g.Width, h = g.Height, cx = w / 2;
        bool enh = Game.Enhanced;
        float pw = Math.Min(w - 12, 330);
        var panel = new RectangleF(cx - pw / 2, 6, pw, h - 34);
        if (enh)
        {
            g.Begin();
            g.Gradient(0, 0, w, h, new Color(8, 10, 32), new Color(30, 8, 50), 24);
            g.Additive();
            g.GlowAt(new Vector2(cx, 40), 200, Palette.Violet * 0.12f);
            g.Alpha();
            g.Panel(panel, Palette.Sky, 0.85f, 10);
            g.GlowText(page.Title, cx, 14, Palette.Gold, 1.3f, 0.8f);
        }
        else
        {
            g.Pixelated();
            g.Rect(0, 0, w, h, Palette.OricRed);
            g.Rect(panel, Palette.OricBlue);
            g.Rect(panel.X + 6, panel.Y + 22, panel.Width - 12, panel.Height - 28, Palette.OricBlack);
            g.PixelTextCentred(page.Title, cx, 12, Palette.OricYellow);
        }

        float textX = panel.X + 12 - _slide * 30;
        float y = 34;
        if (page.Droids) DrawDroids(g, panel, enh);
        else
        {
            int chars = (int)((panel.Width - 24) / 6);
            foreach (var para in page.Lines(Game))
                foreach (var line in Wrap(para, chars))
                {
                    if (enh) g.Text(line, textX, y, new Color(215, 228, 250) * (1 - _slide));
                    else g.PixelText(line, MathF.Round(textX), y, Palette.OricGreen);
                    y += line.Length == 0 ? 5 : 10;
                }
        }

        // page dots and navigation
        float by = h - 20;
        for (int i = 0; i < Pages.Length; i++)
        {
            var c = i == _page ? (enh ? Palette.Gold : Palette.OricYellow) : (enh ? Color.White * 0.3f : Palette.OricWhite);
            if (enh) g.RoundRect(new RectangleF(cx - Pages.Length * 6 + i * 12 + 2, by + 4, 8, 4), 2, c);
            else g.Rect(cx - Pages.Length * 6 + i * 12 + 2, by + 4, 8, 4, c);
        }
        string prevLabel = _page > 0 ? "< BACK" : "", nextLabel = _page < Pages.Length - 1 ? "NEXT >" : "DONE >";
        if (enh)
        {
            g.Text(prevLabel, 8, by + 2, Palette.Sky);
            g.TextRight(nextLabel, w - 8, by + 2, Palette.Sky);
        }
        else
        {
            g.PixelText(prevLabel, 8, by + 2, Palette.OricWhite);
            g.PixelText(nextLabel, w - 8 - Gfx.TextWidth(nextLabel), by + 2, Palette.OricWhite);
            g.Smooth();
        }
    }

    private void DrawDroids(Gfx g, RectangleF panel, bool enh)
    {
        // a 3 x 3 grid: portrait (or Oric-style code box), code, name, stats
        float cw = panel.Width / 3, ch = (panel.Height - 34) / 3;
        for (int t = 0; t < 9; t++)
        {
            float x = panel.X + (t % 3) * cw, y = panel.Y + 26 + (t / 3) * ch;
            string name = t == 0 ? "YOU" : Droids.Code[t];
            string role = Droids.Name[t];
            string stats = Droids.Damage[t] == 0 ? "UNARMED" : "GUN " + Droids.Damage[t];
            stats += "  ARMOUR " + Droids.MaxHp[t];
            if (enh)
            {
                g.DroidPortrait(t, new Vector2(x + 20, y + ch * 0.62f), 1.25f, _time + t);
                g.Begin();
                g.Text(name, x + 38, y + 4, IsoRenderer.ClassColour[t]);
                foreach (var (line, k) in Lines(role, (int)((cw - 40) / 5)))
                    g.Text(line, x + 38, y + 14 + k * 8, Palette.Ice, 0.8f);
                g.Text(stats, x + 38, y + ch - 12, Palette.Ice * 0.6f, 0.65f);
            }
            else
            {
                g.Pixelated();
                g.Rect(x + 6, y + 4, 22, 22, Palette.OricWhite);
                g.PixelTextCentred(name, x + 17, y + 11, Palette.OricBlack);
                g.PixelText(role.Length > (int)((cw - 34) / 6) ? role.Split(' ')[0] : role, x + 32, y + 6, Palette.OricCyan);
                g.PixelText(Droids.Damage[t] == 0 ? "UNARMED" : "GUN " + Droids.Damage[t], x + 32, y + 16, Palette.OricWhite);
            }
        }
    }

    private static IEnumerable<(string, int)> Lines(string text, int chars)
    {
        int k = 0;
        foreach (var l in Wrap(text, chars)) yield return (l, k++);
    }
}
