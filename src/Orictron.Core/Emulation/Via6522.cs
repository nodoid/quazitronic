using System;

namespace Orictron.Emulation;

/// <summary>
/// The Oric's 6522 VIA: two timers, the interrupt flag register, port B (keyboard row select
/// and the PB3 key-sense input) and port A plus CA2/CB2, which together drive the AY sound chip's
/// bus (CA2 = BC1, CB2 = BDIR).
/// </summary>
public sealed class Via6522
{
    public const byte IrqT1 = 0x40, IrqT2 = 0x20;

    private readonly Ay38912 _ay;
    private readonly Func<byte, bool> _keySense;

    public Via6522(Ay38912 ay, Func<byte, bool> keySense)
    {
        _ay = ay;
        _keySense = keySense;
    }

    public byte Orb, Ora, Ddrb, Ddra, Acr, Pcr, Ifr, Ier;
    public int T1Counter = 0xFFFF, T2Counter = 0xFFFF;
    public ushort T1Latch = 0xFFFF;
    public byte T2LatchLow = 0xFF;
    private bool _t1Armed, _t2Armed = true; // T2 runs from power-on

    /// <summary>True while an enabled interrupt is pending.</summary>
    public bool IrqLine => (Ifr & Ier & 0x7F) != 0;

    /// <summary>Advances both timers by <paramref name="cycles"/> (1 MHz).</summary>
    public void Tick(int cycles)
    {
        T1Counter -= cycles;
        while (T1Counter < 0)
        {
            if (_t1Armed) Ifr |= IrqT1;
            if ((Acr & 0x40) != 0)
            {
                // Free-running: reload from the latch (period latch + 2).
                T1Counter += T1Latch + 2;
            }
            else
            {
                _t1Armed = false;
                T1Counter += 0x10000;
            }
        }
        T2Counter -= cycles;
        if (T2Counter < 0)
        {
            if (_t2Armed) Ifr |= IrqT2;
            _t2Armed = false;
            T2Counter &= 0xFFFF; // keeps counting down through $FFFF
        }
    }

    public byte Read(int reg)
    {
        switch (reg & 15)
        {
            case 0:
            {
                byte input = (byte)(_keySense(Orb) ? 0x08 : 0x00);
                return (byte)((Orb & Ddrb) | (input & ~Ddrb));
            }
            case 1:
            case 15:
                return Ora;
            case 2: return Ddrb;
            case 3: return Ddra;
            case 4: Ifr &= unchecked((byte)~IrqT1); return (byte)T1Counter;
            case 5: return (byte)(T1Counter >> 8);
            case 6: return (byte)T1Latch;
            case 7: return (byte)(T1Latch >> 8);
            case 8: Ifr &= unchecked((byte)~IrqT2); return (byte)T2Counter;
            case 9: return (byte)(T2Counter >> 8);
            case 11: return Acr;
            case 12: return Pcr;
            case 13: return (byte)(Ifr | (IrqLine ? 0x80 : 0));
            case 14: return (byte)(Ier | 0x80);
            default: return 0;
        }
    }

    public void Write(int reg, byte value, long cycle)
    {
        switch (reg & 15)
        {
            case 0: Orb = value; break;
            case 1:
            case 15:
                Ora = value;
                AyBus(cycle);
                break;
            case 2: Ddrb = value; break;
            case 3: Ddra = value; break;
            case 4:
            case 6:
                T1Latch = (ushort)((T1Latch & 0xFF00) | value);
                break;
            case 5:
                T1Latch = (ushort)((T1Latch & 0x00FF) | (value << 8));
                T1Counter = T1Latch;
                Ifr &= unchecked((byte)~IrqT1);
                _t1Armed = true;
                break;
            case 7:
                T1Latch = (ushort)((T1Latch & 0x00FF) | (value << 8));
                Ifr &= unchecked((byte)~IrqT1);
                break;
            case 8: T2LatchLow = value; break;
            case 9:
                T2Counter = (value << 8) | T2LatchLow;
                Ifr &= unchecked((byte)~IrqT2);
                _t2Armed = true;
                break;
            case 11: Acr = value; break;
            case 12:
                Pcr = value;
                AyBus(cycle);
                break;
            case 13: Ifr &= (byte)~(value & 0x7F); break;
            case 14:
                if ((value & 0x80) != 0) Ier |= (byte)(value & 0x7F);
                else Ier &= (byte)~(value & 0x7F);
                break;
        }
    }

    /// <summary>CA2 (BC1) and CB2 (BDIR) in "manual output" modes select the AY bus function.</summary>
    private void AyBus(long cycle)
    {
        bool bc1 = (Pcr & 0x0E) == 0x0E;
        bool bdir = (Pcr & 0xE0) == 0xE0;
        if (bdir && bc1) _ay.LatchAddress(Ora);
        else if (bdir) _ay.WriteData(Ora, cycle);
    }
}
