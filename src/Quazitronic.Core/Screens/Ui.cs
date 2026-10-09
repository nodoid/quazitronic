using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Quazitronic.Audio;
using Quazitronic.Graphics;
using Quazitronic.Input;

namespace Quazitronic.Screens;

/// <summary>A vertical menu that works with keys, gamepad, mouse and touch, drawn in either look.</summary>
public sealed class MenuList
{
    public sealed record Item(Func<string> Label, Action? Activate = null, Action<int>? Change = null, Func<bool>? Visible = null);

    private readonly List<Item> _items = new();
    private readonly List<RectangleF> _areas = new();
    private float _flash;

    public int Selected { get; set; }
    public float LineHeight { get; set; } = 13;

    public void Add(Item item) => _items.Add(item);

    /// <summary>Items currently shown.</summary>
    public int Count => Shown().Count;

    /// <summary>The labels currently shown (for screens that draw the menu themselves).</summary>
    public List<string> Labels()
    {
        var l = new List<string>();
        foreach (var i in Shown()) l.Add(i.Label());
        return l;
    }

    /// <summary>Tap/hover areas when the screen draws the menu itself.</summary>
    public void SetAreas(IEnumerable<RectangleF> areas)
    {
        _areas.Clear();
        _areas.AddRange(areas);
    }

    private List<Item> Shown()
    {
        var l = new List<Item>();
        foreach (var i in _items) if (i.Visible?.Invoke() ?? true) l.Add(i);
        return l;
    }

    public void Update(QuazitronicGame game, float dt)
    {
        var shown = Shown();
        if (shown.Count == 0) return;
        Selected = Math.Clamp(Selected, 0, shown.Count - 1);
        _flash = Math.Max(0, _flash - dt);
        var input = game.Input;
        if (input.Up) { Selected = (Selected + shown.Count - 1) % shown.Count; game.Sounds.Play(Sfx.MenuMove, 0.5f); }
        if (input.Down) { Selected = (Selected + 1) % shown.Count; game.Sounds.Play(Sfx.MenuMove, 0.5f); }
        var item = shown[Selected];
        if (item.Change != null && (input.Left || input.Right))
        {
            item.Change(input.Left ? -1 : 1);
            game.Sounds.Play(Sfx.MenuMove, 0.6f);
        }
        else if (input.Confirm)
        {
            Fire(game, item);
        }
        // mouse hover and taps
        for (int i = 0; i < _areas.Count && i < shown.Count; i++)
        {
            if (input.MouseMoved && _areas[i].Contains(input.MousePosition)) Selected = i;
            if (input.Tapped(_areas[i]))
            {
                Selected = i;
                Fire(game, shown[i]);
                break;
            }
        }
    }

    private void Fire(QuazitronicGame game, Item item)
    {
        _flash = 0.15f;
        if (item.Activate != null) { game.Sounds.Play(Sfx.MenuSelect, 0.6f); item.Activate(); }
        else if (item.Change != null) { game.Sounds.Play(Sfx.MenuMove, 0.6f); item.Change(1); }
    }

    /// <summary>Enhanced look: glowing smoothed text, the selection in a glass bar.</summary>
    public void DrawEnhanced(Gfx g, float cx, float y, float width, float time)
    {
        var shown = Shown();
        _areas.Clear();
        for (int i = 0; i < shown.Count; i++)
        {
            string label = shown[i].Label();
            float ly = y + i * LineHeight;
            var area = new RectangleF(cx - width / 2, ly - 2.5f, width, LineHeight - 1);
            _areas.Add(area);
            bool sel = i == Selected;
            if (sel)
            {
                float pulse = 0.75f + 0.25f * MathF.Sin(time * 5);
                g.RoundRect(area, 5, new Color(255, 190, 60) * (0.22f * pulse + (_flash > 0 ? 0.3f : 0)));
                g.RoundRect(new RectangleF(area.X, area.Y, 3, area.Height), 1.5f, Palette.Gold * pulse);
                g.GlowText(label, cx, ly, Palette.Gold, 1, 0.9f);
            }
            else g.TextCentred(label, cx, ly, new Color(200, 215, 240));
        }
    }

    /// <summary>Original look: Oric pixel font, the selection in inverse video.</summary>
    public void DrawOriginal(Gfx g, float cx, float y, float time)
    {
        var shown = Shown();
        _areas.Clear();
        for (int i = 0; i < shown.Count; i++)
        {
            string label = shown[i].Label();
            float ly = y + i * LineHeight;
            float w = Gfx.TextWidth(label) + 12;
            var area = new RectangleF(MathF.Round(cx - w / 2), ly - 2, w, 12);
            _areas.Add(new RectangleF(cx - 90, ly - 2, 180, LineHeight));
            if (i == Selected)
            {
                g.Rect(area, Palette.OricCyan);
                g.PixelTextCentred(label, cx, ly, Palette.OricBlack);
            }
            else g.PixelTextCentred(label, cx, ly, Palette.OricGreen);
        }
    }
}

/// <summary>
/// The credits line at the bottom of the title: it scrolls from left to right, entering at the
/// left edge and leaving at the right, then starts again.
/// </summary>
public sealed class CreditsScroller
{
    public const string Text = "Written by PFJ, Based on the Oric port of the ZX Spectrum game by Hewson";
    public const float Speed = 34f; // virtual pixels per second

    private float _x = float.NaN;

    /// <summary>Left edge of the text for this frame.</summary>
    public float X => _x;

    public void Update(float dt, float screenWidth, float scale = 1)
    {
        float w = Gfx.TextWidth(Text, scale);
        if (float.IsNaN(_x)) _x = screenWidth * 0.5f - w * 0.5f; // start with it in view
        _x += Speed * dt;
        if (_x > screenWidth + 8) _x = -w - 8;
    }

    public void DrawEnhanced(Gfx g, float y, float width)
    {
        g.Gradient(0, y - 3, width, 14, new Color(0, 0, 0, 0), new Color(10, 8, 30) * 0.9f, 6);
        g.Rect(0, y - 3, width, 0.6f, Palette.Gold * 0.5f);
        g.Text(Text, _x, y, new Color(255, 225, 120));
    }

    public void DrawOriginal(Gfx g, float y, float width)
    {
        g.Rect(0, y - 8, width, 24, Palette.OricRed);
        g.PixelText(Text, MathF.Round(_x), y, Palette.OricYellow);
    }
}

/// <summary>Draws the touch buttons a screen has published, in either look.</summary>
public static class TouchUi
{
    public static void Draw(Gfx g, InputState input, bool enhanced)
    {
        foreach (var b in input.TouchButtons)
        {
            bool held = input.Held(b.Pad);
            var r = b.Area;
            if (enhanced)
            {
                float rad = Math.Min(r.Width, r.Height) / 2;
                var col = b.Pad switch
                {
                    Pad.Fire => Palette.Alarm,
                    Pad.Grapple => Palette.Gold,
                    Pad.Lift => Palette.Mint,
                    Pad.Pause => Palette.Ice,
                    _ => Palette.Sky,
                };
                g.RoundRect(r, rad, new Color(8, 10, 24) * (held ? 0.75f : 0.5f));
                g.RoundRect(r.Inflate(-1), rad - 1, col * (held ? 0.55f : 0.22f));
                g.TextCentred(b.Label, r.Center.X, r.Center.Y - 4, Color.White * (held ? 1 : 0.85f));
            }
            else
            {
                g.Rect(r, held ? Palette.OricWhite : Palette.OricBlue);
                g.Frame(r.X, r.Y, r.Width, r.Height, 1, Palette.OricWhite);
                g.PixelTextCentred(b.Label, r.Center.X, MathF.Round(r.Center.Y - 4), held ? Palette.OricBlack : Palette.OricWhite);
            }
        }
    }

    /// <summary>The on-screen D-pad (when tilt is off): four arrows around a centre, turned 45 degrees
    /// because the droid moves diagonally, as on the Spectrum.</summary>
    public static void AddDpad(InputState input, Vector2 centre, float size)
    {
        float s = size / 3, o = s * 0.75f;
        RectangleF At(float dx, float dy) => new(centre.X + dx - s / 2, centre.Y + dy - s / 2, s, s);
        input.TouchButtons.Add(new TouchButton(Pad.Up, At(o, -o), "↗"));
        input.TouchButtons.Add(new TouchButton(Pad.Right, At(o, o), "↘"));
        input.TouchButtons.Add(new TouchButton(Pad.Down, At(-o, o), "↙"));
        input.TouchButtons.Add(new TouchButton(Pad.Left, At(-o, -o), "↖"));
    }
}

/// <summary>The in-game pause menu shared by both play screens.</summary>
public sealed class PauseMenu
{
    private readonly MenuList _menu = new() { LineHeight = 14 };
    public bool Open { get; private set; }

    public PauseMenu(QuazitronicGame game, Action resume, Action quit, Action? graphicsChanged = null)
    {
        _menu.Add(new MenuList.Item(() => "RESUME", () => { Open = false; resume(); }));
        _menu.Add(new MenuList.Item(() => "GRAPHICS: " + (game.Enhanced ? "ENHANCED" : "ORIGINAL"),
            Change: _ => { game.SetEnhanced(!game.Enhanced); graphicsChanged?.Invoke(); }));
        _menu.Add(new MenuList.Item(() => "SOUND: " + (game.Save.Sound ? "ON" : "OFF"), Change: _ => game.SetSound(!game.Save.Sound)));
        _menu.Add(new MenuList.Item(() => "CONTROLS: " + (game.Save.TiltControls ? "TILT" : "D-PAD"),
            Change: _ => game.SetTiltControls(!game.Save.TiltControls), Visible: () => game.HasTiltOption));
        _menu.Add(new MenuList.Item(() => "QUIT TO TITLE", () => { Open = false; quit(); }));
    }

    public void Show()
    {
        Open = true;
        _menu.Selected = 0;
    }

    public void Update(QuazitronicGame game, float dt)
    {
        if (!Open) return;
        if (game.Input.Back || game.Input.Pressed(Pad.Pause)) { _menu.Selected = 0; Open = false; return; }
        _menu.Update(game, dt);
    }

    public void Draw(Gfx g, bool enhanced, float time)
    {
        if (!Open) return;
        float cx = g.Width / 2f;
        if (enhanced)
        {
            g.Rect(0, 0, g.Width, g.Height, new Color(0, 0, 10) * 0.6f);
            var r = new RectangleF(cx - 80, 62, 160, 42 + _menu.Count * _menu.LineHeight);
            g.Panel(r, Palette.Sky);
            g.GlowText("PAUSED", cx, 72, Palette.Sky, 1.5f, 0.8f);
            _menu.DrawEnhanced(g, cx, 96, 140, time);
        }
        else
        {
            var r = new RectangleF(MathF.Round(cx - 84), 60, 168, 42 + _menu.Count * _menu.LineHeight);
            g.Rect(r, Palette.OricBlack);
            g.Frame(r.X, r.Y, r.Width, r.Height, 2, Palette.OricWhite);
            g.PixelTextCentred("PAUSED", cx, 70, Palette.OricYellow);
            _menu.DrawOriginal(g, cx, 94, time);
        }
    }
}
