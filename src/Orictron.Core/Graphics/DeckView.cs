using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orictron.Audio;
using Orictron.Game;

namespace Orictron.Graphics;

/// <summary>
/// Runs a <see cref="Session"/> at the original's 25 frames per second and draws its deck with the
/// enhanced renderer at the display's rate, interpolating every moving thing between frames. The
/// camera glides after the player instead of jumping in 6-pixel steps.
/// </summary>
public sealed class DeckView
{
    public const float Tick = 1f / Session.FramesPerSecond;

    private readonly Particles _particles = new();
    private readonly float[] _droidFlash = new float[Session.MaxDroids];
    private float _playerFlash;
    private float _accum;
    private Vector2 _cam;
    private bool _camSet;
    private int _deckShown = -1;
    private float _time;

    public DeckView(Session session) => Session = session;

    public Session Session { get; private set; }
    public float Zoom { get; set; } = 1.1f;
    /// <summary>Virtual pixels at the bottom covered by the HUD (the camera centres above it).</summary>
    public float BottomMargin { get; set; }
    /// <summary>Virtual pixels at the top covered by the HUD.</summary>
    public float TopMargin { get; set; }
    public Sounds? Sounds { get; set; }
    /// <summary>Fraction (0..1) of the way from the last frame to the next.</summary>
    public float Alpha => _accum / Tick;

    public void Reset(Session s)
    {
        Session = s;
        _particles.Clear();
        _camSet = false;
        _accum = 0;
        _deckShown = -1;
    }

    /// <summary>Advances real time; ticks the session as often as due with the given controls.</summary>
    public void Update(float dt, Func<Controls> controls)
    {
        _time += dt;
        _accum += dt;
        int guard = 0;
        while (_accum >= Tick && guard++ < 5)
        {
            _accum -= Tick;
            Step(controls());
        }
        if (_accum > Tick) _accum = Tick;
        _particles.Update(dt);
        for (int i = 0; i < _droidFlash.Length; i++) _droidFlash[i] = Math.Max(0, _droidFlash[i] - dt * 6);
        _playerFlash = Math.Max(0, _playerFlash - dt * 5);
    }

    /// <summary>One simulation frame, with its effects.</summary>
    public void Step(in Controls c)
    {
        var s = Session;
        int deckBefore = s.DeckIndex;
        s.Tick(c);
        var theme = DeckTheme.For(s.Deck.OricInk);
        foreach (var b in s.Booms)
            _particles.Explosion(new Vector3(b.X, b.Y, b.Z), theme.Accent, b.Big);
        foreach (var h in s.Hits)
        {
            if (h.Droid >= 0) _droidFlash[h.Droid] = 1;
            else _playerFlash = 1;
            _particles.Sparks(new Vector3(h.X, h.Y, h.Z), h.Droid >= 0 ? Palette.Sky : Palette.Amber);
        }
        foreach (var cue in s.Cues)
        {
            if (cue.Cue == Cue.Charge && (s.Frame & 3) == 0)
                _particles.Beam(PlayerWorld(1), theme.Accent, 4);
        }
        if (s.DeckIndex != deckBefore)
        {
            _particles.Clear();
            _camSet = false;
            _particles.Beam(PlayerWorld(1), Color.White, 50);
        }
        Sounds?.PlayCues(s.Cues);
    }

    private static float Lerp(int a, int b, float t) => Math.Abs(b - a) > 24 ? b : a + (b - a) * t;

    private Vector3 PlayerWorld(float t)
    {
        var s = Session;
        float x = Lerp(s.PrevPlayerX, s.PlayerX, t), y = Lerp(s.PrevPlayerY, s.PlayerY, t);
        float z = MathHelper.Lerp(s.Deck.FloorZ(s.PrevPlayerX, s.PrevPlayerY), s.Deck.FloorZ(s.PlayerX, s.PlayerY), t);
        return new Vector3(x, y, z);
    }

    /// <summary>Where the player is on the virtual screen (for touch UI and effects).</summary>
    public Vector2 PlayerScreen(IsoRenderer iso) => iso.WorldToScreen(PlayerWorld(Alpha) + new Vector3(0, 0, 8));

    private void UpdateCamera(float vw, float vh, float dt)
    {
        var s = Session;
        var target = IsoRenderer.ToIso(PlayerWorld(Alpha)) + new Vector2(0, -6);
        var b = IsoRenderer.DeckBounds(s.Deck);
        float viewW = vw / Zoom, viewH = (vh - BottomMargin - TopMargin) / Zoom;
        Vector2 Clamp(Vector2 c)
        {
            const float m = 18;
            c.X = b.Width + 2 * m <= viewW ? b.Center.X : Math.Clamp(c.X, b.X - m + viewW / 2, b.Right + m - viewW / 2);
            c.Y = b.Height + 2 * m <= viewH ? b.Center.Y : Math.Clamp(c.Y, b.Y - m + viewH / 2, b.Bottom + m - viewH / 2);
            return c;
        }
        target = Clamp(target);
        if (!_camSet) { _cam = target; _camSet = true; }
        else _cam += (target - _cam) * (1 - MathF.Exp(-dt * 5.5f));
    }

    private float _lastDraw;

    /// <summary>Draws the void, the deck and everything on it, filling the screen.</summary>
    public void Draw(Gfx g, float dim = 0)
    {
        var s = Session;
        var iso = g.Iso;
        float dt = Math.Clamp(_time - _lastDraw, 0, 0.1f);
        _lastDraw = _time;
        if (_deckShown != s.DeckIndex)
        {
            iso.BuildDeck(s.Deck);
            _deckShown = s.DeckIndex;
        }
        var theme = iso.Theme;
        float vw = g.Width, vh = g.Height;
        UpdateCamera(vw, vh, dt);

        DrawVoid(g, theme);
        g.End();

        iso.SetCamera(_cam, new Vector2(vw / 2, TopMargin + (vh - BottomMargin - TopMargin) / 2), Zoom, vw, vh);
        iso.BeginDynamic();
        float t = Alpha;
        float time = _time;

        // droids
        for (int a = 0; a < s.DeckCount; a++)
        {
            if (!s.DroidAlive(a)) continue;
            float x = Lerp(s.PrevDroidX[a], s.DroidX[a], t), y = Lerp(s.PrevDroidY[a], s.DroidY[a], t);
            float z = MathHelper.Lerp(s.Deck.FloorZ(s.PrevDroidX[a], s.PrevDroidY[a]), s.Deck.FloorZ(s.DroidX[a], s.DroidY[a]), t);
            var p = new Vector3(x, y, z);
            iso.AddShadow(p, 7.5f);
            iso.AddDroid(p, s.DroidType(a), time, _droidFlash[a]);
        }

        // player
        var pw = PlayerWorld(t);
        bool showPlayer = s.PlayerVisible || s.Flash == 0;
        if (s.PlayerHp > 0 || !s.GameOver)
        {
            if (showPlayer || ((int)(time * 12) & 1) == 0)
            {
                iso.AddShadow(pw, 7.5f, 0.7f);
                iso.AddDroid(pw, 0, time, Math.Max(_playerFlash, s.Flash > 0 && s.FlashKind == 2 ? 0.6f : 0));
                iso.AddGhost(pw, time);
            }
            // the host the player has taken over is shown as a ring of its class colour
            if (s.PlayerType != 0)
                iso.AddFloorRing(pw, 10 + MathF.Sin(time * 4) * 0.8f, IsoRenderer.ClassColour[s.PlayerType] * 0.8f);
            if (s.GrappleLit && s.View == View.Deck)
            {
                float pulse = 0.6f + 0.4f * MathF.Sin(time * 14);
                iso.AddFloorRing(pw, 14 + 2 * MathF.Sin(time * 7), Palette.Gold * pulse);
                iso.AddGlow(pw + new Vector3(0, 0, 8), 34, Palette.Gold * (0.25f * pulse));
            }
        }

        // bullets
        for (int k = 0; k < Session.MaxBullets; k++)
        {
            if (!s.BulletOn[k]) continue;
            float x = Lerp(s.PrevBulletX[k], s.BulletX[k], t), y = Lerp(s.PrevBulletY[k], s.BulletY[k], t);
            var p = new Vector3(x, y, s.BulletZ[k] + 7);
            var dir = new Vector3(s.BulletDx[k], s.BulletDy[k], 0);
            bool mine = s.BulletOwner[k] == 0;
            var col = mine ? new Color(120, 230, 255) : new Color(255, 120, 60);
            iso.AddBolt(p - dir * 2.2f, p, 5, col);
            iso.AddBolt(p - dir * 1.2f, p, 2.2f, Color.White);
            iso.AddGlow(p, 16, col * 0.5f);
            iso.AddShadow(p - new Vector3(0, 0, 7), 2.5f, 0.35f);
        }

        // lights on the deck that react to the player
        var tile = s.Deck.TileAt(s.PlayerX, s.PlayerY) >> 2;
        if (tile == (int)TileKind.Lift && s.View == View.Deck)
            iso.AddGlow(pw + new Vector3(0, 0, 3), 40 + 6 * MathF.Sin(time * 6), theme.Accent * 0.35f);

        _particles.Draw(iso, time);
        iso.Draw(0.75f + 0.25f * MathF.Sin(time * 2.2f));

        if (dim > 0) g.Rect(0, 0, vw, vh, Color.Black * dim);
    }

    private void DrawVoid(Gfx g, DeckTheme theme)
    {
        float vw = g.Width, vh = g.Height;
        g.Begin();
        g.Gradient(0, 0, vw, vh, theme.Void, Color.Lerp(theme.Void, theme.Nebula, 0.35f), 24);
        // nebula clouds and parallax stars
        g.Additive();
        var p = -_cam * 0.25f;
        for (int k = 0; k < 6; k++)
        {
            float x = ((k * 173.3f + p.X * 0.6f) % (vw + 200) + vw + 200) % (vw + 200) - 100;
            float y = (k * 61.7f) % vh + p.Y * 0.3f % 40;
            g.GlowAt(new Vector2(x, y), 90 + k * 13, theme.Nebula * 0.18f);
        }
        for (int layer = 0; layer < 2; layer++)
        {
            float par = layer == 0 ? 0.15f : 0.35f;
            for (int k = 0; k < 70; k++)
            {
                uint h = (uint)(k * 2654435761u + layer * 97u);
                float sx = (h % 1000) / 1000f * (vw + 64), sy = ((h >> 10) % 1000) / 1000f * (vh + 64);
                float x = ((sx - _cam.X * par) % (vw + 64) + vw + 64) % (vw + 64) - 32;
                float y = ((sy - _cam.Y * par) % (vh + 64) + vh + 64) % (vh + 64) - 32;
                float tw = 0.5f + 0.5f * MathF.Sin(_time * (1 + k % 3) + k);
                float size = layer == 0 ? 1.1f : 1.8f;
                g.GlowAt(new Vector2(x, y), size * (0.8f + tw * 0.5f), Color.White * (0.5f + 0.5f * tw));
            }
        }
        g.Alpha();
    }
}
