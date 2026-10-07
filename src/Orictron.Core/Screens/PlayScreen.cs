using System;
using Microsoft.Xna.Framework;
using Orictron.Audio;
using Orictron.Game;
using Orictron.Graphics;
using Orictron.Input;
using Orictron.Original;
using Orictron.Persistence;

namespace Orictron.Screens;

/// <summary>
/// The game, in either look. ENHANCED: the 3D deck, a glass status panel after the original's three
/// capsules, the transfer battle with glowing wires, synthesised sound and music. ORIGINAL: the Oric
/// version's own screen and sound, recreated. The look can be switched at any time (G, or the pause
/// menu). Touch and tilt controls on phones and tablets.
/// </summary>
public sealed class PlayScreen : Screen
{
    private const float HudHeight = 30;

    private readonly Session _session;
    private readonly DeckView _view;
    private readonly PauseMenu _pause;
    private readonly bool _autoplay;
    private int _rank = -2;
    private ScoreEntry? _entry;
    private float _endTime;
    private float _time;
    private View _lastView = View.Deck;
    private int _tapWire = -1;
    private readonly OriginalRenderer _original = new();
    private View _soundView = View.Deck;
    private bool _wasEnhanced;

    public PlayScreen(OrictronGame game, int seed, bool autoplay = false) : base(game)
    {
        _autoplay = autoplay;
        _session = new Session(seed, demo: autoplay);
        _view = new DeckView(_session) { OnTick = OnTick };
        _pause = new PauseMenu(game, Resume, Quit, StyleChanged);
        _wasEnhanced = game.Enhanced;
    }

    internal Session Session => _session;
    /// <summary>Hide the DEMO caption (store capture).</summary>
    internal bool ShowDemoLabel { get; set; } = true;
    /// <summary>Show the phone buttons even while the autopilot plays (store capture).</summary>
    internal bool ShowTouchControls { get; set; }
    internal DeckView DeckViewer => _view;
    private bool HudOnTop => Game.IsMobile;

    /// <summary>After every simulation frame: the frame's sounds, in the current look's style.</summary>
    private void OnTick(Session s)
    {
        var chip = Game.Chip;
        chip.Frame();
        // The original silences everything when a transfer begins and on the end screen.
        if (s.View != _soundView && s.View is View.Briefing or View.End) chip.Off();
        _soundView = s.View;
        if (Game.Enhanced) Game.Sounds.PlayCues(s.Cues);
        else chip.Play(s.Cues);
    }

    private void StyleChanged()
    {
        Game.Chip.Off();
        UpdateMusic();
        _wasEnhanced = Game.Enhanced;
    }

    private void UpdateMusic()
    {
        if (Game.Enhanced && _session.View is View.Deck) Game.Music.Play(Song.Deck);
        else Game.Music.Stop();
    }

    public override void Enter()
    {
        UpdateMusic();
        Game.TiltActive = true;
        Game.Tilt.Reset();
        Game.TiltSensor?.Start();
        if (!_autoplay)
        {
            Game.Save.GamesPlayed++;
            Game.PersistSave();
        }
    }

    public override void Leave()
    {
        Game.Chip.Off();
        Game.TiltActive = false;
        Game.TiltSensor?.Stop();
    }

    public override void OnDeactivated()
    {
        if (_autoplay) return;
        // Bank the score so far: a phone may close a backgrounded app without warning.
        // (OrictronGame writes the save file straight after this.)
        BankScore();
        if (!_session.Finished) _pause.Show();
    }

    /// <summary>Puts the current score in the best-scores table (or updates the entry this game already has there).</summary>
    private void BankScore()
    {
        if (_autoplay || _session.Score <= 0) return;
        Game.Save.Record(ref _entry, new ScoreEntry
        {
            Score = _session.Score * 10,
            Deck = _session.DeckIndex + 1,
            Secured = _session.Won,
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
        });
    }

    private void Resume()
    {
        Game.Tilt.Reset(); // whatever angle the device is at now counts as level
    }

    private void Quit()
    {
        RecordScore();
        Game.ChangeScreen(new IntroScreen(Game));
    }

    /// <summary>The game is over (or abandoned): record its final score and write the save file now.</summary>
    private void RecordScore()
    {
        if (_autoplay || _rank != -2) return;
        _rank = _session.Score > 0
            ? Game.Save.Record(ref _entry, new ScoreEntry
            {
                Score = _session.Score * 10,
                Deck = _session.DeckIndex + 1,
                Secured = _session.Won,
                Date = DateTime.Now.ToString("yyyy-MM-dd"),
            })
            : -1;
        Game.PersistSave();
    }

    private Controls ReadControls()
    {
        var input = Game.Input;
        var c = new Controls
        {
            Up = input.Held(Pad.Up),
            Down = input.Held(Pad.Down),
            Left = input.Held(Pad.Left),
            Right = input.Held(Pad.Right),
            Fire = input.Held(Pad.Fire),
            Grapple = input.Held(Pad.Grapple),
            Lift = input.Held(Pad.Lift),
        };
        // Touch: tapping a wire in the transfer battle selects it and fires.
        if (_tapWire >= 0 && _session.View == Orictron.Game.View.Transfer)
        {
            _session.Transfer.Cursor = _tapWire;
            c.Fire = true;
            c.Up = c.Down = false;
            _tapWire = -1;
        }
        return c;
    }

    public override void Update(float dt)
    {
        _time += dt;
        var input = Game.Input;
        PublishTouchButtons();
        if (_pause.Open)
        {
            _pause.Update(Game, dt);
            return;
        }
        if (!_autoplay && (input.Pressed(Pad.Pause) || input.Back) && !_session.Finished)
        {
            Game.Sounds.Play(Sfx.MenuBack, 0.5f);
            _pause.Show();
            return;
        }
        // G switches the look at any time on a computer.
        if (!_autoplay && input.Key(Microsoft.Xna.Framework.Input.Keys.G))
        {
            Game.SetEnhanced(!Game.Enhanced);
            Game.Sounds.Play(Sfx.MenuMove, 0.5f);
        }
        if (Game.Enhanced != _wasEnhanced) StyleChanged();
        if (_session.View == Orictron.Game.View.Transfer && input.Taps.Count > 0)
            foreach (var t in input.Taps)
            {
                int w = WireAt(t.Position);
                if (w >= 0) _tapWire = w;
            }

        _view.TopMargin = HudOnTop ? HudHeight : 0;
        _view.BottomMargin = HudOnTop ? 0 : HudHeight;
        _view.Zoom = Math.Max(1.1f, Game.VirtualWidth / 330f);
        _view.Update(dt, ReadControls);

        if (_session.View != _lastView)
        {
            UpdateMusic();
            _lastView = _session.View;
        }

        if (_session.View == Orictron.Game.View.End)
        {
            RecordScore();
            _endTime += dt;
            // the original holds the end screen for 6 s; let a press cut it short after a moment
            if (_endTime > 1.5f && (input.AnyContinue || input.Pressed(Pad.Fire)))
                while (!_session.Finished) _session.Tick(default);
        }
        if (_session.Finished || _session.Abandoned)
        {
            RecordScore();
            Game.ChangeScreen(new IntroScreen(Game) { LastScoreRank = _rank, LastScore = _session.Score * 10 });
        }
    }

    // ------------------------------------------------------------------ touch

    private void PublishTouchButtons()
    {
        var input = Game.Input;
        input.TouchButtons.Clear();
        if (!Game.IsMobile || (_autoplay && !ShowTouchControls)) return;
        float w = Game.VirtualWidth, h = OrictronGame.VirtualHeight;
        input.TouchButtons.Add(new TouchButton(Pad.Pause, new RectangleF(4, HudOnTop && Game.Enhanced ? HudHeight + 4 : 4, 24, 20), "II"));
        if (_pause.Open) return;
        var v = _session.View;
        if (v is Orictron.Game.View.Deck or Orictron.Game.View.Transfer or Orictron.Game.View.Briefing)
            input.TouchButtons.Add(new TouchButton(Pad.Fire, new RectangleF(w - 62, h - 62, 56, 56), "FIRE"));
        if (v == Orictron.Game.View.Deck)
        {
            input.TouchButtons.Add(new TouchButton(Pad.Grapple, new RectangleF(w - 108, h - 46, 42, 40), "GRAB"));
            bool onLift = (_session.Deck.TileAt(_session.PlayerX, _session.PlayerY) >> 2) == (int)TileKind.Lift;
            if (onLift) input.TouchButtons.Add(new TouchButton(Pad.Lift, new RectangleF(w - 62, h - 108, 56, 40), "LIFT"));
        }
        if (!Game.UseTilt && v is Orictron.Game.View.Deck or Orictron.Game.View.Transfer)
            TouchUi.AddDpad(input, new Vector2(44, h - 44), 78);
    }

    // ------------------------------------------------------------------ transfer layout

    private float CellX => Game.VirtualWidth / 2f;
    private float WireY(int w) => (HudOnTop ? HudHeight + 4 : 0) + 46 + w * 11.5f;
    private float WireLength => Math.Min(118, CellX - 34);
    private float StepLen => WireLength / Transfer.Length;

    /// <summary>Where the Oric screen sits in ORIGINAL mode (whole Oric pixels, centred).</summary>
    private RectangleF OricRect => new(MathF.Round((Game.VirtualWidth - OricScreen.Width) / 2f), 0, OricScreen.Width, OricScreen.Height);

    private int WireAt(Vector2 p)
    {
        if (!Game.Enhanced)
        {
            // the original's wires are 8 rows apart from row 38, on the left half of the screen
            var r = OricRect;
            if (p.X > r.X + 17 * 6 || p.X < r.X) return -1;
            int w = (int)MathF.Floor((p.Y - r.Y - 34) / 8);
            return w >= 0 && w < Transfer.Wires ? w : -1;
        }
        if (p.X > CellX) return -1;
        for (int w = 0; w < Transfer.Wires; w++)
            if (Math.Abs(p.Y - WireY(w)) < 6.2f) return w;
        return -1;
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        if (!Game.Enhanced)
        {
            DrawOriginal(g);
            return;
        }
        var v = _session.View;
        if (v == Orictron.Game.View.Transfer) DrawTransfer(g);
        else
        {
            _view.Draw(g, v == Orictron.Game.View.Deck ? 0 : 0.55f);
            if (v != Orictron.Game.View.Deck) DrawTextScreen(g, v);
        }
        DrawHud(g);
        TouchUi.Draw(g, Game.Input, true);
        if (_autoplay && ShowDemoLabel)
        {
            float a = 0.6f + 0.4f * MathF.Sin(_time * 3);
            g.TextCentred("DEMO", g.Width / 2f, HudOnTop ? HudHeight + 6 : 6, Palette.Gold * a);
        }
        _pause.Draw(g, true, _time);
    }

    /// <summary>ORIGINAL look: the Oric screen, recreated, with whole pixels.</summary>
    private void DrawOriginal(Gfx g)
    {
        _original.Draw(_session, _view.Ticks);
        g.Begin(null, Microsoft.Xna.Framework.Graphics.SamplerState.PointClamp);
        g.Rect(0, 0, g.Width, g.Height, new Color(12, 12, 16));
        g.DrawOric(_original.Screen, OricRect);
        TouchUi.Draw(g, Game.Input, false);
        if (_autoplay && ShowDemoLabel && ((int)(_time * 2) & 1) == 0)
            g.PixelTextCentred("DEMO", g.Width / 2f, 206, Palette.OricYellow);
        _pause.Draw(g, false, _time);
        g.Smooth();
    }

    private void DrawHud(Gfx g)
    {
        var s = _session;
        float w = g.Width;
        float y = HudOnTop ? 2 : g.Height - HudHeight + 2;
        float h = HudHeight - 4;
        float mid = 66, gap = 4;
        float side = Math.Min(150, (w - 6 - 2 * gap - mid) / 2);
        float x0 = (w - (side * 2 + mid + gap * 2)) / 2;
        var left = new RectangleF(x0, y, side, h);
        var centre = new RectangleF(x0 + side + gap, y, mid, h);
        var right = new RectangleF(x0 + side + gap + mid + gap, y, side, h);

        // left capsule: status word and energy bar
        g.Panel(left, Palette.Gold * 0.8f, 0.78f, 8);
        string status = s.Status == "DEMO" && !ShowDemoLabel ? "MOBILE" : s.Status;
        bool alarm = status is "FAILED" or "EJECTED" or "DEADLOCK";
        g.Text(status, left.X + 8, y + 4, alarm ? Palette.Alarm : Palette.Gold);
        int max = Droids.MaxHp[s.PlayerType];
        float frac = Math.Clamp(s.PlayerHp / (float)max, 0, 1);
        var bar = new RectangleF(left.X + 8, y + 15, left.Width - 16, 5);
        g.RoundRect(bar, 2, new Color(40, 30, 10));
        int cells = 10;
        float cw = bar.Width / cells;
        for (int i = 0; i < cells; i++)
        {
            float f = Math.Clamp(frac * cells - i, 0, 1);
            if (f <= 0) break;
            var col = frac < 0.3f ? Color.Lerp(Palette.Alarm, Palette.Gold, frac / 0.3f) : Color.Lerp(Palette.Gold, Palette.Mint, (frac - 0.3f) / 0.7f * 0.4f);
            if (frac < 0.3f) col *= 0.7f + 0.3f * MathF.Sin(_time * 10);
            g.RoundRect(new RectangleF(bar.X + i * cw + 0.4f, bar.Y + 0.4f, (cw - 0.8f) * f, bar.Height - 0.8f), 1.5f, col);
        }

        // centre capsule: deck number and droids left on it; lit while grappling
        g.Panel(centre, s.GrappleLit ? Color.White : Palette.Violet * 0.8f, s.GrappleLit ? 0.6f : 0.78f, 8);
        if (s.GrappleLit) g.RoundRect(centre.Inflate(-1), 7, Color.White * (0.15f + 0.1f * MathF.Sin(_time * 12)));
        g.TextCentred("DECK " + (s.DeckIndex + 1), centre.Center.X, y + 4, Palette.Violet);
        g.TextCentred(s.DroidsLeftOnDeck + " LEFT", centre.Center.X, y + 14, Palette.Mint, 0.85f);

        // right capsule: the host's class code and the score
        g.Panel(right, Palette.Sky * 0.8f, 0.78f, 8);
        string unit = s.PlayerType == 0 ? "ID" : Droids.Code[s.PlayerType];
        var unitBox = new RectangleF(right.X + 6, y + 4, 22, h - 8);
        g.RoundRect(unitBox, 4, IsoRenderer.ClassColour[s.PlayerType] * 0.85f);
        g.TextCentred(unit, unitBox.Center.X, unitBox.Center.Y - 4, Palette.Ink, 1, false);
        string score = (s.Score * 10).ToString("000000");
        g.TextRight(score, right.Right - 8, y + 4, Palette.Sky, 1.2f);
        if (Game.Save.Best > 0 && right.Width > 90)
            g.TextRight("HI " + Math.Max(Game.Save.Best, s.Score * 10).ToString("000000"), right.Right - 8, y + 15, Palette.Ice * 0.7f, 0.75f);
    }

    private void DrawTextScreen(Gfx g, View v)
    {
        var s = _session;
        float cx = g.Width / 2f;
        float top = HudOnTop ? HudHeight + 8 : 10;
        var panel = new RectangleF(cx - 110, top, 220, 180);
        g.Panel(panel, v == Orictron.Game.View.End && !s.Won ? Palette.Alarm : Palette.Sky, 0.86f, 10);
        g.DrawLogo(cx, top + 8, 20);
        switch (v)
        {
            case Orictron.Game.View.Briefing:
            {
                g.TextCentred("UNIT.. " + Droids.Describe(s.Opponent), cx, top + 38, Palette.Sky);
                g.GlowText("PREPARE TO ENGAGE", cx, top + 52, Palette.Gold, 1, 0.8f);
                g.TextCentred("SECURITY DEVICE", cx, top + 64, Palette.Sky);
                g.DroidPortrait(s.PlayerType, new Vector2(cx - 50, top + 120), 2.4f, _time);
                g.DroidPortrait(s.Opponent, new Vector2(cx + 50, top + 120), 2.4f, _time);
                g.GlowText("VS", cx, top + 106, Palette.Alarm, 1.6f, 1);
                g.TextCentred("FIRE TO START", cx, top + 160, Color.White * (0.5f + 0.5f * MathF.Sin(_time * 5)), 0.85f);
                break;
            }
            case Orictron.Game.View.Captured:
            {
                g.TextCentred("UNIT.. " + Droids.Describe(s.Opponent), cx, top + 38, Palette.Sky);
                g.TextCentred("SECURITY CLASS", cx, top + 54, Palette.Sky);
                g.GlowText(Droids.Security[s.Opponent], cx, top + 66, Palette.Gold, 1.4f, 0.9f);
                g.DroidPortrait(s.Opponent, new Vector2(cx, top + 132), 2.8f, _time, 0.25f + 0.25f * MathF.Sin(_time * 8));
                g.GlowText("TRANSFER COMPLETE", cx, top + 158, Palette.Mint, 1, 0.9f);
                break;
            }
            case Orictron.Game.View.End:
            {
                if (s.Won)
                {
                    g.GlowText("THE SHIP IS SECURED", cx, top + 42, Palette.Gold, 1.2f, 1);
                    g.TextCentred("ALL DECKS CLEARED", cx, top + 58, Palette.Sky);
                }
                else
                {
                    g.GlowText("GAME OVER", cx, top + 40, Palette.Alarm, 1.8f, 1);
                    g.TextCentred("INFLUENCE DEVICE DESTROYED", cx, top + 60, Palette.Sky);
                }
                g.TextCentred("SCORE", cx, top + 84, Palette.Ice);
                g.GlowText((s.Score * 10).ToString("000000"), cx, top + 96, Palette.Gold, 2, 0.8f);
                if (_rank == 0) g.GlowText("NEW HIGH SCORE!", cx, top + 124, Palette.Mint, 1.2f, 1);
                else if (_rank > 0) g.TextCentred("BEST SCORES: #" + (_rank + 1), cx, top + 124, Palette.Mint);
                g.TextCentred("DECK " + (s.DeckIndex + 1) + "  -  " + s.DroidsLeft + " DROIDS LEFT", cx, top + 142, Palette.Ice * 0.8f, 0.85f);
                break;
            }
        }
    }

    private void DrawTransfer(Gfx g)
    {
        var s = _session;
        var tr = s.Transfer;
        float w = g.Width, hgt = g.Height;
        float cx = CellX;
        float fracStep = ((s.Frame & 1) == 1 ? 0 : 0.5f) + _view.Alpha * 0.5f;

        // backdrop: the two sides in their colours over a circuit-board grid
        g.Begin();
        g.Gradient(0, 0, w, hgt, new Color(10, 8, 20), new Color(4, 4, 10), 16);
        g.Rect(0, 0, cx, hgt, Palette.PlayerSide * 0.05f);
        g.Rect(cx, 0, w - cx, hgt, Palette.DroidSide * 0.07f);
        for (float gx = 0; gx < w; gx += 16) g.Rect(gx, 0, 0.5f, hgt, Color.White * 0.03f);
        for (float gy = 0; gy < hgt; gy += 16) g.Rect(0, gy, w, 0.5f, Color.White * 0.03f);
        // border flash in the result animation
        if (tr.FlashPhase >= 0 && (tr.FlashPhase & 1) == 0)
        {
            var fc = tr.ResultWin ? Palette.PlayerSide : Palette.Alarm;
            g.Frame(0, 0, w, hgt, 4, fc * 0.9f);
        }

        float len = WireLength, step = StepLen;
        float top = HudOnTop ? HudHeight : 0;
        // header
        g.GlowText("TRANSFER", cx, top + 6, Color.White, 1.2f, 0.6f);
        float secs = tr.Seconds;
        g.TextCentred(secs.ToString("00"), cx, top + 18, secs < 20 ? Palette.Alarm : Palette.Ice, 1);

        // the "who's ahead" block on top of the cell column
        int mine = tr.Count(0), theirs = Transfer.Wires - mine;
        var lead = mine > theirs ? Palette.PlayerSide : Palette.DroidSide;
        var leadBox = new RectangleF(cx - 10, WireY(0) - 22, 20, 10);
        g.RoundRect(leadBox.Inflate(1), 4, Color.White * 0.5f);
        g.RoundRect(leadBox, 4, lead);
        g.Additive();
        g.GlowAt(leadBox.Center, 22, lead * 0.5f);
        g.Alpha();

        for (int wi = 0; wi < Transfer.Wires; wi++)
        {
            float y = WireY(wi);
            DrawWire(g, 0, wi, y, cx - 10 - len, step, tr.LeftKind[wi], tr.LeftLit[wi] != 0, tr.LeftPulse[wi], fracStep, Palette.PlayerSide);
            DrawWire(g, 1, wi, y, cx + 10 + len, -step, tr.RightKind[wi], tr.RightLit[wi] != 0, tr.RightPulse[wi], fracStep, Palette.DroidSide);
            // the cell
            var col = tr.Owner[wi] == 0 ? Palette.PlayerSide : Palette.DroidSide;
            bool held = tr.Hold[wi] > 0;
            var cell = new RectangleF(cx - 8, y - 4.5f, 16, 9);
            g.RoundRect(cell.Inflate(1), 4, Color.White * 0.35f);
            g.RoundRect(cell, 4, col * (held ? 1f : 0.7f));
            if (held)
            {
                g.Additive();
                g.GlowAt(cell.Center, 16, col * (0.4f + 0.2f * MathF.Sin(_time * 10 + wi)));
                g.Alpha();
            }
        }

        // cursor
        if (tr.ResultStep < 0)
        {
            float cy = WireY(tr.Cursor);
            float ax = cx - 10 - len - 12 + MathF.Sin(_time * 8) * 1.5f;
            g.Additive();
            g.GlowAt(new Vector2(ax, cy), 12, Palette.PlayerSide * 0.6f);
            g.Alpha();
            g.TextCentred("→", ax, cy - 4, Palette.PlayerSide, 1.4f);
        }

        // pulses left on each side
        DrawStack(g, cx - 10 - len - 26, tr.PlayerPulses, Palette.PlayerSide, false);
        DrawStack(g, cx + 10 + len + 26, tr.EnemyPulses, Palette.DroidSide, true);

        // the two droids
        g.DroidPortrait(tr.PlayerType, new Vector2(Math.Max(22, cx - len - 30), top + 22), 1.4f, _time);
        g.DroidPortrait(tr.EnemyType, new Vector2(Math.Min(w - 22, cx + len + 30), top + 22), 1.4f, _time);
        g.Begin();
        g.TextCentred(mine.ToString(), cx - 30, top + 18, Palette.PlayerSide);
        g.TextCentred(theirs.ToString(), cx + 30, top + 18, Palette.DroidSide);
    }

    private void DrawWire(Gfx g, int side, int wire, float y, float x0, float step, int kind, bool lit, int pulse, float frac, Color col)
    {
        // x of progress p (0 = wire start, 11 = the cell)
        float X(float p) => x0 + step * p;
        float start = kind == 3 ? 6 : 0;
        float end = kind == 1 ? Transfer.DeadEndLength + 1 : Transfer.Length;
        var dark = new Color(70, 70, 90);
        var lineCol = lit ? col : dark;
        g.Line(new Vector2(X(start), y), new Vector2(X(end), y), lit ? 1.8f : 1.2f, lineCol);
        if (lit)
        {
            g.Additive();
            g.Line(new Vector2(X(start), y), new Vector2(X(end), y), 4, col * 0.25f);
            g.Alpha();
        }
        if (kind == 1)
        {
            // dead end: a blocking cap
            g.Rect(X(end) - 1.5f, y - 4, 3, 8, Palette.Alarm * 0.9f);
        }
        if (kind == 2)
        {
            // joins the wire below at its middle
            g.Line(new Vector2(X(6), y), new Vector2(X(6), WireY(wire + 1)), 1.5f, lineCol);
            g.Rect(X(6) - 1.5f, y - 1.5f, 3, 3, lineCol);
        }
        if (kind == 3)
            g.Rect(X(6) - 1.5f, y - 1.5f, 3, 3, lineCol);
        // the socket at the start of the wire
        if (kind != 3) g.RoundRect(new RectangleF(X(0) - 3, y - 3, 6, 6), 2, lit ? col : dark);

        if (pulse > 0)
        {
            float p = pulse - 1 + frac;
            DrawPulse(g, X(p), y, col, step > 0);
            if (kind == 2 && p > 6) DrawPulse(g, X(p), WireY(wire + 1), col, step > 0);
        }
    }

    private void DrawPulse(Gfx g, float x, float y, Color col, bool right)
    {
        g.Additive();
        g.GlowAt(new Vector2(x, y), 10, col * 0.9f);
        g.GlowAt(new Vector2(x, y), 4, Color.White);
        g.Alpha();
        g.TextCentred(right ? "→" : "←", x, y - 4, Color.White, 1);
    }

    private void DrawStack(Gfx g, float x, int n, Color col, bool right)
    {
        for (int k = 0; k < n; k++)
        {
            float y = WireY(0) + k * 14;
            g.RoundRect(new RectangleF(x - 7, y - 5, 14, 10), 3, col * 0.25f);
            g.TextCentred(right ? "←" : "→", x, y - 4, col);
        }
    }
}
