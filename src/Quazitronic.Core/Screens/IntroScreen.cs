using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quazitronic.Audio;
using Quazitronic.Game;
using Quazitronic.Graphics;

namespace Quazitronic.Screens;

/// <summary>
/// The title. ENHANCED look: the glowing logo over a live demo game on the 3D deck. ORIGINAL look:
/// the Oric's blue framed screen with the outlined ORICTRON logo. Either way the menu offers PLAY,
/// INSTRUCTIONS and the switch between the enhanced remake and the original, and the credits line
/// scrolls left to right along the bottom.
/// </summary>
public sealed class IntroScreen : Screen
{
    private readonly MenuList _menu = new();
    private readonly CreditsScroller _credits = new();
    private DeckView? _demo;
    private float _time;
    private float _demoTime;

    public IntroScreen(QuazitronicGame game) : base(game)
    {
        _menu.Add(new MenuList.Item(() => "PLAY", Play));
        _menu.Add(new MenuList.Item(() => "INSTRUCTIONS", () => Game.ChangeScreen(new InstructionsScreen(Game))));
        _menu.Add(new MenuList.Item(() => "GRAPHICS: " + (Game.Enhanced ? "ENHANCED" : "ORIGINAL"), Change: _ => ToggleStyle()));
        _menu.Add(new MenuList.Item(() => "SOUND: " + (Game.Save.Sound ? "ON" : "OFF"), Change: _ => Game.SetSound(!Game.Save.Sound)));
        _menu.Add(new MenuList.Item(() => "MUSIC: " + (Game.Save.Music ? "ON" : "OFF"), Change: _ => Game.SetMusic(!Game.Save.Music), Visible: () => Game.Enhanced));
        _menu.Add(new MenuList.Item(() => "CONTROLS: " + (Game.Save.TiltControls ? "TILT" : "D-PAD"),
            Change: _ => Game.SetTiltControls(!Game.Save.TiltControls), Visible: () => Game.HasTiltOption));
        _menu.Add(new MenuList.Item(() => "QUIT", () => Game.Exit(), Visible: () => Game.CanQuit));
    }

    /// <summary>The last game's score and its place in the best-scores table (-1 none), shown once.</summary>
    public int LastScoreRank { get; init; } = -2;
    public int LastScore { get; init; }

    public override void Enter()
    {
        UpdateMusic();
    }

    private void UpdateMusic()
    {
        if (Game.Enhanced) Game.Music.Play(Song.Title);
        else Game.Music.Stop();
    }

    private void ToggleStyle()
    {
        Game.SetEnhanced(!Game.Enhanced);
        UpdateMusic();
    }

    private void Play()
    {
        Game.ChangeScreen(new PlayScreen(Game, Game.Random.Next(1, 65535)));
    }

    public override void Update(float dt)
    {
        _time += dt;
        _credits.Update(dt, Game.VirtualWidth);
        if (Game.Input.Key(Keys.G)) { ToggleStyle(); Game.Sounds.Play(Sfx.MenuMove); }
        _menu.Update(Game, dt);
        if (Game.Enhanced)
        {
            _demoTime += dt;
            // A fresh demo when the last one ends - or after three minutes, in case the autopilot gets stuck.
            if (_demo == null || _demo.Session.Finished || _demoTime > 180)
            {
                _demo = new DeckView(new Session(Game.Random.Next(1, 65535), demo: true));
                _demoTime = 0;
            }
            _demo.Zoom = Math.Max(1.2f, Game.VirtualWidth / 300f);
            _demo.Update(dt, () => default);
        }
    }

    public override void Draw(Gfx g)
    {
        if (Game.Enhanced) DrawEnhanced(g);
        else DrawOriginal(g);
    }

    private void DrawEnhanced(Gfx g)
    {
        float w = g.Width, h = g.Height, cx = w / 2;
        _demo?.Draw(g, 0.5f);
        g.Begin();
        // vignette
        g.Gradient(0, 0, w, 60, new Color(0, 0, 0, 200), new Color(0, 0, 0, 0), 12);
        g.Gradient(0, h - 50, w, 50, new Color(0, 0, 0, 0), new Color(0, 0, 0, 200), 12);

        float logoH = 34 + 2 * MathF.Sin(_time * 1.3f);
        g.DrawLogo(cx, 10, logoH);
        g.TextCentred("THE ORIC ATMOS QUAZATRON", cx, 50, Palette.Ice * 0.85f, 0.85f);

        var panel = new RectangleF(cx - 82, 64, 164, 6 + _menuCount * 13);
        g.Panel(panel, Palette.Gold * 0.7f, 0.72f, 8);
        _menu.DrawEnhanced(g, cx, 70, 150, _time);

        if (Game.Save.Best > 0)
            g.TextRight("HI " + Game.Save.Best.ToString("000000"), w - 6, 4, Palette.Sky, 0.85f);
        if (LastScoreRank > -2 && LastScore > 0 && _time < 12)
        {
            string msg = LastScoreRank == 0 ? "NEW HIGH SCORE " + LastScore.ToString("000000") : "LAST SCORE " + LastScore.ToString("000000");
            g.GlowText(msg, cx, panel.Bottom + 6, LastScoreRank == 0 ? Palette.Mint : Palette.Ice, 0.9f, 0.6f);
        }
        _credits.DrawEnhanced(g, h - 11, w);
    }

    private int _menuCount => 4 + (Game.Enhanced ? 1 : 0) + (Game.HasTiltOption ? 1 : 0) + (Game.CanQuit ? 1 : 0);

    private readonly Original.OriginalRenderer _oric = new();

    /// <summary>ORIGINAL look: the Oric version's own title screen, recreated, with the menu in it.</summary>
    private void DrawOriginal(Gfx g)
    {
        float w = g.Width, h = g.Height;
        float ox = MathF.Round((w - Original.OricScreen.Width) / 2);
        var labels = _menu.Labels();
        _oric.DrawTitle(labels, _menu.Selected, Game.Save.Best, ((int)(_time * 3) & 1) == 0);
        g.Begin(null, SamplerState.PointClamp);
        g.Rect(0, 0, w, h, new Color(12, 12, 16));
        g.DrawOric(_oric.Screen, new RectangleF(ox, 0, Original.OricScreen.Width, Original.OricScreen.Height));
        var areas = new System.Collections.Generic.List<RectangleF>();
        for (int i = 0; i < labels.Count; i++)
        {
            var (y, lh) = Original.OriginalRenderer.TitleLine(i);
            areas.Add(new RectangleF(ox + 30, y, 180, lh));
        }
        _menu.SetAreas(areas);
        // the credits run along the red text lines under the picture, across the whole display
        _credits.DrawOriginal(g, 208, w);
        g.Smooth();
    }

}
