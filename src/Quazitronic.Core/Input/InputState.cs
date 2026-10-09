using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Quazitronic.Graphics;

namespace Quazitronic.Input;

/// <summary>Logical game buttons. Directions are screen-relative, as in the original.</summary>
public enum Pad
{
    Up,
    Down,
    Left,
    Right,
    Fire,
    Grapple,
    Lift,
    Pause,
}

/// <summary>A press on the screen in virtual coordinates.</summary>
public readonly record struct Tap(Vector2 Position);

/// <summary>An on-screen button for touch play; screens publish their layout every frame.</summary>
public readonly record struct TouchButton(Pad Pad, RectangleF Area, string Label);

/// <summary>
/// Per-frame snapshot of every input device, reduced to <see cref="Pad"/> actions, menu actions and taps.
/// Keys follow the original: arrows or Q A O P to move, SPACE fire, T or RETURN grapple, L lift, ESC.
/// </summary>
public sealed class InputState
{
    private const int PadCount = 8;
    private KeyboardState _kb, _prevKb;
    private GamePadState _gp, _prevGp;
    private MouseState _mouse, _prevMouse;
    private readonly bool[] _touchHeld = new bool[PadCount];
    private readonly bool[] _prevTouchHeld = new bool[PadCount];
    private readonly bool[] _forced = new bool[PadCount];
    private readonly bool[] _prevForced = new bool[PadCount];
    private readonly bool[] _tiltHeld = new bool[PadCount];
    private readonly bool[] _prevTiltHeld = new bool[PadCount];
    private bool _touchSeen;

    /// <summary>The device-tilt stick while tilt steering is active (null otherwise).</summary>
    public Vector2? Tilt { get; private set; }

    /// <summary>On the deck the directions run diagonally on screen (see Session), so tilt is turned
    /// 45 degrees to match: the droid still goes the way the device is tipped. Elsewhere (the transfer
    /// battle) tilt is screen up/down/left/right.</summary>
    public bool TiltDiagonal { get; set; }

    /// <summary>Feeds this frame's tilt stick (or null when tilt isn't in use). Directions count as
    /// held past 0.5 deflection and released below 0.3, so a wobble doesn't re-trigger.</summary>
    public void SetTilt(Vector2? tilt)
    {
        Array.Copy(_tiltHeld, _prevTiltHeld, PadCount);
        Tilt = tilt;
        if (tilt is not { } t)
        {
            Array.Clear(_tiltHeld);
            return;
        }
        static bool Hyst(bool was, float v) => was ? v > 0.3f : v > 0.5f;
        // Up is screen up-right, right is down-right.
        if (TiltDiagonal) t = new Vector2(t.X - t.Y, t.X + t.Y) * MathF.Sqrt(0.5f);
        _tiltHeld[(int)Pad.Right] = Hyst(_tiltHeld[(int)Pad.Right], t.X);
        _tiltHeld[(int)Pad.Left] = Hyst(_tiltHeld[(int)Pad.Left], -t.X);
        _tiltHeld[(int)Pad.Up] = Hyst(_tiltHeld[(int)Pad.Up], t.Y);
        _tiltHeld[(int)Pad.Down] = Hyst(_tiltHeld[(int)Pad.Down], -t.Y);
    }

    public List<Tap> Taps { get; } = new();
    public List<Vector2> Touches { get; } = new();
    public List<TouchButton> TouchButtons { get; } = new();
    public Vector2 MousePosition { get; private set; }
    public bool MouseMoved { get; private set; }
    public bool IsMobileLayout { get; set; }
    public bool IsTouchDevice => _touchSeen || IsMobileLayout;

    // ---- menu actions ----
    public bool Up => Pressed(Pad.Up);
    public bool Down => Pressed(Pad.Down);
    public bool Left => Pressed(Pad.Left);
    public bool Right => Pressed(Pad.Right);
    public bool Confirm => Key(Keys.Enter) || Key(Keys.Space) || Button(Buttons.A) || Button(Buttons.Start);
    public bool Back => Key(Keys.Escape) || Key(Keys.Back) || Button(Buttons.Back) || Button(Buttons.B);
    public bool AnyTap => Taps.Count > 0;
    public bool AnyContinue => Confirm || AnyTap || Back;
    public bool AnyKey => _kb.GetPressedKeyCount() > 0 && _prevKb.GetPressedKeyCount() == 0;
    public bool ToggleFullScreen =>
        Key(Keys.F11) || (Key(Keys.Enter) && (_kb.IsKeyDown(Keys.LeftAlt) || _kb.IsKeyDown(Keys.RightAlt)));

    /// <summary>The capture tool can hold buttons.</summary>
    internal void Force(Pad pad, bool held) => _forced[(int)pad] = held;

    internal void ClearForced() => Array.Clear(_forced);

    public bool Held(Pad pad)
    {
        if (_forced[(int)pad] || _touchHeld[(int)pad] || _tiltHeld[(int)pad]) return true;
        return KeyHeld(_kb, pad) || PadHeld(_gp, pad);
    }

    private bool WasHeld(Pad pad)
    {
        if (_prevForced[(int)pad] || _prevTouchHeld[(int)pad] || _prevTiltHeld[(int)pad]) return true;
        return KeyHeld(_prevKb, pad) || PadHeld(_prevGp, pad);
    }

    private static bool KeyHeld(KeyboardState kb, Pad pad) => pad switch
    {
        Pad.Up => kb.IsKeyDown(Keys.Up) || kb.IsKeyDown(Keys.Q),
        Pad.Down => kb.IsKeyDown(Keys.Down) || kb.IsKeyDown(Keys.A),
        Pad.Left => kb.IsKeyDown(Keys.Left) || kb.IsKeyDown(Keys.O),
        Pad.Right => kb.IsKeyDown(Keys.Right) || kb.IsKeyDown(Keys.P),
        Pad.Fire => kb.IsKeyDown(Keys.Space) || kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl),
        Pad.Grapple => kb.IsKeyDown(Keys.T) || kb.IsKeyDown(Keys.Enter),
        Pad.Lift => kb.IsKeyDown(Keys.L),
        Pad.Pause => kb.IsKeyDown(Keys.Escape),
        _ => false,
    };

    private static bool PadHeld(GamePadState gp, Pad pad) => pad switch
    {
        Pad.Up => gp.IsButtonDown(Buttons.DPadUp) || gp.ThumbSticks.Left.Y > 0.5f,
        Pad.Down => gp.IsButtonDown(Buttons.DPadDown) || gp.ThumbSticks.Left.Y < -0.5f,
        Pad.Left => gp.IsButtonDown(Buttons.DPadLeft) || gp.ThumbSticks.Left.X < -0.5f,
        Pad.Right => gp.IsButtonDown(Buttons.DPadRight) || gp.ThumbSticks.Left.X > 0.5f,
        Pad.Fire => gp.IsButtonDown(Buttons.A) || gp.IsButtonDown(Buttons.RightTrigger),
        Pad.Grapple => gp.IsButtonDown(Buttons.X) || gp.IsButtonDown(Buttons.LeftTrigger),
        Pad.Lift => gp.IsButtonDown(Buttons.Y),
        Pad.Pause => gp.IsButtonDown(Buttons.Start) || gp.IsButtonDown(Buttons.Back),
        _ => false,
    };

    /// <summary>True on the frame the button goes down.</summary>
    public bool Pressed(Pad pad) => Held(pad) && !WasHeld(pad);

    public void Update(Func<Vector2, Vector2> screenToVirtual)
    {
        _prevKb = _kb;
        _prevGp = _gp;
        _prevMouse = _mouse;
        Array.Copy(_touchHeld, _prevTouchHeld, PadCount);
        Array.Copy(_forced, _prevForced, PadCount);
        _kb = Keyboard.GetState();
        _gp = GamePad.GetState(PlayerIndex.One);
        _mouse = Mouse.GetState();
        Taps.Clear();
        Touches.Clear();
        Array.Clear(_touchHeld);

        foreach (var touch in TouchPanel.GetState())
        {
            _touchSeen = true;
            var p = screenToVirtual(touch.Position);
            if (touch.State is TouchLocationState.Pressed or TouchLocationState.Moved)
            {
                Touches.Add(p);
                foreach (var b in TouchButtons)
                    if (b.Area.Contains(p)) _touchHeld[(int)b.Pad] = true;
            }
            if (touch.State == TouchLocationState.Pressed) Taps.Add(new Tap(p));
        }

        var mp = screenToVirtual(new Vector2(_mouse.X, _mouse.Y));
        MouseMoved = mp != MousePosition && (_mouse.X != _prevMouse.X || _mouse.Y != _prevMouse.Y);
        MousePosition = mp;
        if (!_touchSeen)
        {
            if (_mouse.LeftButton == ButtonState.Pressed)
            {
                Touches.Add(mp);
                foreach (var b in TouchButtons)
                    if (b.Area.Contains(mp)) _touchHeld[(int)b.Pad] = true;
            }
            if (_mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
                Taps.Add(new Tap(mp));
        }
    }

    public bool Key(Keys k) => _kb.IsKeyDown(k) && !_prevKb.IsKeyDown(k);
    public bool KeyDown(Keys k) => _kb.IsKeyDown(k);
    private bool Button(Buttons b) => _gp.IsButtonDown(b) && !_prevGp.IsButtonDown(b);

    /// <summary>Was any tap inside <paramref name="area"/> this frame?</summary>
    public bool Tapped(RectangleF area)
    {
        foreach (var t in Taps)
            if (area.Contains(t.Position)) return true;
        return false;
    }
}
