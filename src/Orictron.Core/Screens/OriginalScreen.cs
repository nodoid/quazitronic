using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Orictron.Emulation;
using Orictron.Graphics;
using Orictron.Input;

namespace Orictron.Screens;

/// <summary>
/// ORIGINAL mode: the Oric Atmos tape itself, running in the built-in emulator at the Oric's 1 MHz,
/// with its own title, demo, sound chip and timing. Keys, gamepad, tilt and touch buttons are mapped
/// onto the Oric keyboard: arrows (or tilt) move, FIRE is SPACE, GRAB is T, LIFT is L.
/// </summary>
public sealed class OriginalScreen : Screen
{
    private readonly OricMachine _machine = new(Audio.AudioEngine.Rate);
    private readonly uint[] _pixels = new uint[OricMachine.ScreenWidth * OricMachine.ScreenHeight];
    private readonly PauseMenu _pause;
    private Texture2D? _screen;
    private float _time;
    private float _spaceTap;
    private float _escape;
    private long _owed;

    public OriginalScreen(OrictronGame game) : base(game)
    {
        _pause = new PauseMenu(game, () => game.Tilt.Reset(), () => game.ChangeScreen(new IntroScreen(game)),
            abandon: () => _escape = 0.3f);
    }

    internal OricMachine Machine => _machine;

    public override void Enter()
    {
        _machine.Boot(TapFile.LoadEmbedded());
        Game.Music.Stop();
        Game.Audio.StopAll();
        Game.Audio.Chip = _machine.Ay;
        Game.TiltActive = true;
        Game.Tilt.Reset();
        Game.TiltSensor?.Start();
    }

    public override void Leave()
    {
        Game.Audio.Chip = null;
        Game.TiltActive = false;
        Game.TiltSensor?.Stop();
        _screen?.Dispose();
    }

    public override void OnDeactivated() => _pause.Show();

    private RectangleF ScreenRect(float vw) => new(MathF.Round((vw - 240) / 2), 0, 240, 224);

    public override void Update(float dt)
    {
        _time += dt;
        var input = Game.Input;
        PublishTouchButtons();
        if (_pause.Open)
        {
            _pause.Update(Game, dt);
            _machine.ReleaseAllKeys();
            _machine.Ay.Flush(_machine.Cycles);
            return;
        }
        if (input.Pressed(Pad.Pause) || input.Back)
        {
            _pause.Show();
            return;
        }

        // A tap on the Oric's screen presses SPACE (to start from its title screen).
        var sr = ScreenRect(Game.VirtualWidth);
        foreach (var t in input.Taps)
        {
            bool onButton = false;
            foreach (var b in input.TouchButtons) if (b.Area.Contains(t.Position)) onButton = true;
            if (!onButton && sr.Contains(t.Position)) _spaceTap = 0.2f;
        }
        _spaceTap = Math.Max(0, _spaceTap - dt);
        _escape = Math.Max(0, _escape - dt);

        _machine.SetKey(OricKey.Up, input.Held(Pad.Up));
        _machine.SetKey(OricKey.Down, input.Held(Pad.Down));
        _machine.SetKey(OricKey.Left, input.Held(Pad.Left));
        _machine.SetKey(OricKey.Right, input.Held(Pad.Right));
        _machine.SetKey(OricKey.Space, input.Held(Pad.Fire) || _spaceTap > 0);
        _machine.SetKey(OricKey.T, input.Held(Pad.Grapple));
        _machine.SetKey(OricKey.L, input.Held(Pad.Lift));
        _machine.SetKey(OricKey.Escape, _escape > 0);

        // Run the Oric for exactly the time that passed (1 MHz).
        _owed += (long)(Math.Min(dt, 0.1f) * OricMachine.CpuHz);
        if (_owed > 0)
        {
            _machine.Run(_owed);
            _owed = 0;
        }
    }

    private void PublishTouchButtons()
    {
        var input = Game.Input;
        input.TouchButtons.Clear();
        if (!Game.IsMobile) return;
        float w = Game.VirtualWidth, h = OrictronGame.VirtualHeight;
        input.TouchButtons.Add(new TouchButton(Pad.Pause, new RectangleF(4, 4, 24, 20), "II"));
        if (_pause.Open) return;
        input.TouchButtons.Add(new TouchButton(Pad.Fire, new RectangleF(w - 58, h - 58, 54, 54), "FIRE"));
        input.TouchButtons.Add(new TouchButton(Pad.Grapple, new RectangleF(w - 58, h - 104, 54, 40), "GRAB"));
        input.TouchButtons.Add(new TouchButton(Pad.Lift, new RectangleF(w - 58, h - 150, 54, 40), "LIFT"));
        if (!Game.UseTilt) TouchUi.AddDpad(input, new Vector2(42, h - 44), 76);
    }

    public override void Draw(Gfx g)
    {
        _machine.RenderScreen(_pixels);
        _screen ??= new Texture2D(g.Device, OricMachine.ScreenWidth, OricMachine.ScreenHeight);
        g.Device.Textures[0] = null;
        _screen.SetData(_pixels);
        g.Pixelated();
        g.Rect(0, 0, g.Width, g.Height, new Color(12, 12, 16));
        g.Texture(_screen, ScreenRect(g.Width), Color.White);
        TouchUi.Draw(g, Game.Input, false);
        _pause.Draw(g, false, _time);
        g.Smooth();
    }
}
