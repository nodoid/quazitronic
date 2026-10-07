using System;

namespace Orictron.Emulation;

/// <summary>Keys on the Oric Atmos keyboard matrix that Orictron reads (row = PB0-2, column = AY port bit).</summary>
public enum OricKey
{
    Space, Up, Down, Left, Right, Q, A, O, P, T, L, Return, Escape,
}

/// <summary>
/// An Oric Atmos with just what a ROM-less machine-code game needs: 64K RAM, the 6502, the VIA,
/// the AY-3-8912 (with the keyboard on its I/O port) and the ULA picture (HIRES and text lines with
/// serial attributes). Orictron disables interrupts and never calls the ROM, so no ROM image is
/// needed: the ROM area reads as RTS instructions and ignores writes.
/// </summary>
public sealed class OricMachine : IBus
{
    public const int CpuHz = 1_000_000;
    public const int ScreenWidth = 240;
    public const int ScreenHeight = 224;

    /// <summary>The Oric's eight colours (0 black … 7 white) as packed RGBA (little-endian ABGR).</summary>
    public static readonly uint[] Palette =
    {
        0xFF000000, 0xFF0000FF, 0xFF00FF00, 0xFF00FFFF,
        0xFFFF0000, 0xFFFF00FF, 0xFFFFFF00, 0xFFFFFFFF,
    };

    private static readonly (int Row, int Col)[] KeyMatrix =
    {
        (4, 0), (4, 3), (4, 6), (4, 5), (4, 7), (1, 6), (6, 5), (5, 2), (5, 3), (1, 1), (7, 1), (7, 5), (1, 5),
    };

    public readonly byte[] Ram = new byte[65536];
    private readonly byte[] _keyRows = new byte[8];
    private bool _hires;

    public OricMachine(int sampleRate = 44100)
    {
        Ay = new Ay38912(sampleRate);
        Via = new Via6522(Ay, KeySense);
        Cpu = new Cpu6502(this);
    }

    public Cpu6502 Cpu { get; }
    public Via6522 Via { get; }
    public Ay38912 Ay { get; }
    public TapFile? Tape { get; private set; }

    /// <summary>Executed instructions that landed in the (absent) ROM - should stay 0.</summary>
    public int RomCalls { get; private set; }

    public long Cycles => Cpu.Cycles;

    /// <summary>Clears the machine, loads the tape into memory and starts it, as CLOAD"" with autorun would.</summary>
    public void Boot(TapFile tape)
    {
        Array.Clear(Ram);
        for (int a = 0xC000; a < 0x10000; a++) Ram[a] = 0x60; // RTS
        Array.Clear(_keyRows);
        Ay.Reset();
        Tape = tape;
        Array.Copy(tape.Data, 0, Ram, tape.Start, tape.Data.Length);
        Cpu.Reset(tape.EntryPoint);
        Cpu.Cycles = 0;
        RomCalls = 0;
        _hires = false;
    }

    /// <summary>Runs the CPU for <paramref name="cycles"/> cycles of emulated time.</summary>
    public void Run(long cycles)
    {
        long target = Cpu.Cycles + cycles;
        while (Cpu.Cycles < target)
        {
            if (Cpu.PC >= 0xC000) RomCalls++;
            int c = Cpu.Step();
            Via.Tick(c);
            if (Via.IrqLine) Cpu.Irq();
        }
        Ay.RenderUntil(Cpu.Cycles);
    }

    public void SetKey(OricKey key, bool down)
    {
        var (row, col) = KeyMatrix[(int)key];
        if (down) _keyRows[row] |= (byte)(1 << col);
        else _keyRows[row] &= (byte)~(1 << col);
    }

    public void ReleaseAllKeys() => Array.Clear(_keyRows);

    private bool KeySense(byte orb)
    {
        int row = orb & 7;
        int cols = ~Ay.PortA & 0xFF;
        return (_keyRows[row] & cols) != 0;
    }

    public byte Read(ushort address)
    {
        if ((address & 0xFF00) == 0x0300) return Via.Read(address & 15);
        return Ram[address];
    }

    public void Write(ushort address, byte value)
    {
        if ((address & 0xFF00) == 0x0300)
        {
            Via.Write(address & 15, value, Cpu.Cycles);
            return;
        }
        if (address >= 0xC000) return;
        Ram[address] = value;
    }

    /// <summary>
    /// Draws the ULA picture into <paramref name="pixels"/> (240 x 224, packed RGBA): 200 HIRES
    /// lines and 3 text lines in HIRES mode, or 28 text lines in text mode. Serial attributes
    /// (ink, paper, video mode) and inverse video are honoured.
    /// </summary>
    public void RenderScreen(uint[] pixels)
    {
        // The video-mode attribute is latched: the last one in a frame applies from the next frame.
        bool hires = _hires;
        bool nextMode = hires;
        for (int y = 0; y < ScreenHeight; y++)
        {
            int ink = 7, paper = 0;
            int o = y * ScreenWidth;
            bool hiresLine = hires && y < 200;
            for (int c = 0; c < 40; c++)
            {
                byte b;
                int pattern;
                if (hiresLine)
                {
                    b = Ram[0xA000 + y * 40 + c];
                    pattern = b & 0x3F;
                }
                else
                {
                    int textRow, charRow, charset;
                    if (hires)
                    {
                        textRow = (y - 200) / 8;
                        charRow = (y - 200) % 8;
                        b = Ram[0xBF68 + textRow * 40 + c];
                        charset = 0x9800;
                    }
                    else
                    {
                        textRow = y / 8;
                        charRow = y % 8;
                        b = Ram[0xBB80 + textRow * 40 + c];
                        charset = 0xB400;
                    }
                    pattern = Ram[charset + (b & 0x7F) * 8 + charRow] & 0x3F;
                }
                int inv = (b & 0x80) != 0 ? 7 : 0;
                if ((b & 0x60) == 0)
                {
                    int v = b & 0x1F;
                    if (v < 8) ink = v;
                    else if (v >= 16 && v < 24) paper = v - 16;
                    else if (v >= 24) nextMode = (v & 4) != 0;
                    uint col = Palette[paper ^ inv];
                    for (int p = 0; p < 6; p++) pixels[o + c * 6 + p] = col;
                    continue;
                }
                uint on = Palette[ink ^ inv], off = Palette[paper ^ inv];
                for (int p = 0; p < 6; p++)
                    pixels[o + c * 6 + p] = (pattern & (0x20 >> p)) != 0 ? on : off;
            }
        }
        _hires = nextMode;
    }
}
