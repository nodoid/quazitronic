namespace Orictron.Emulation;

/// <summary>Memory and I/O as the CPU sees it.</summary>
public interface IBus
{
    byte Read(ushort address);
    void Write(ushort address, byte value);
}

/// <summary>
/// An NMOS 6502 (the Oric's CPU): every documented opcode, decimal mode, and cycle counts
/// including the page-crossing and taken-branch penalties, so code that times itself against the
/// VIA (as Orictron does) runs at the right speed. Undocumented opcodes act as NOPs of the usual length.
/// </summary>
public sealed class Cpu6502
{
    public const byte FlagC = 0x01, FlagZ = 0x02, FlagI = 0x04, FlagD = 0x08, FlagB = 0x10, FlagU = 0x20, FlagV = 0x40, FlagN = 0x80;

    private readonly IBus _bus;

    public Cpu6502(IBus bus) => _bus = bus;

    public byte A, X, Y, SP = 0xFD, P = FlagU | FlagI;
    public ushort PC;
    /// <summary>Total cycles executed since power-on.</summary>
    public long Cycles;
    /// <summary>Opcodes the CPU didn't recognise (should stay 0 for cc65 code).</summary>
    public int IllegalOpcodes;

    public bool GetFlag(byte f) => (P & f) != 0;
    private void SetFlag(byte f, bool on) => P = on ? (byte)(P | f) : (byte)(P & ~f);
    private void Nz(byte v) { SetFlag(FlagZ, v == 0); SetFlag(FlagN, (v & 0x80) != 0); }

    private byte Rd(int a) => _bus.Read((ushort)a);
    private void Wr(int a, byte v) => _bus.Write((ushort)a, v);
    private ushort Rd16(int a) => (ushort)(Rd(a) | (Rd((a + 1) & 0xFFFF) << 8));
    private byte Fetch() => Rd(PC++);
    private ushort Fetch16() { ushort v = Rd16(PC); PC += 2; return v; }
    private void Push(byte v) { Wr(0x100 | SP, v); SP--; }
    private byte Pull() { SP++; return Rd(0x100 | SP); }

    public void Reset(ushort pc)
    {
        PC = pc;
        SP = 0xFD;
        P = FlagU | FlagI;
    }

    /// <summary>Raises a maskable interrupt (ignored while I is set).</summary>
    public bool Irq()
    {
        if (GetFlag(FlagI)) return false;
        Push((byte)(PC >> 8)); Push((byte)PC); Push((byte)((P | FlagU) & ~FlagB));
        SetFlag(FlagI, true);
        PC = Rd16(0xFFFE);
        Cycles += 7;
        return true;
    }

    // ---- addressing modes (return the effective address; extra cycle on page cross when asked) ----
    private int Zp() => Fetch();
    private int Zpx() => (Fetch() + X) & 0xFF;
    private int Zpy() => (Fetch() + Y) & 0xFF;
    private int Abs() => Fetch16();
    private int Abx(bool penalty) { int b = Fetch16(); int a = (b + X) & 0xFFFF; if (penalty && ((a ^ b) & 0xFF00) != 0) Cycles++; return a; }
    private int Aby(bool penalty) { int b = Fetch16(); int a = (b + Y) & 0xFFFF; if (penalty && ((a ^ b) & 0xFF00) != 0) Cycles++; return a; }
    private int Izx() { int z = (Fetch() + X) & 0xFF; return Rd(z) | (Rd((z + 1) & 0xFF) << 8); }
    private int Izy(bool penalty)
    {
        int z = Fetch();
        int b = Rd(z) | (Rd((z + 1) & 0xFF) << 8);
        int a = (b + Y) & 0xFFFF;
        if (penalty && ((a ^ b) & 0xFF00) != 0) Cycles++;
        return a;
    }

    // ---- ALU ----
    private void Adc(byte m)
    {
        int c = P & FlagC;
        if (GetFlag(FlagD))
        {
            int lo = (A & 0x0F) + (m & 0x0F) + c;
            int hi = (A >> 4) + (m >> 4);
            if (lo > 9) { lo += 6; hi++; }
            SetFlag(FlagZ, ((A + m + c) & 0xFF) == 0);
            SetFlag(FlagN, (hi & 0x08) != 0);
            SetFlag(FlagV, ((~(A ^ m) & (A ^ (hi << 4))) & 0x80) != 0);
            if (hi > 9) hi += 6;
            SetFlag(FlagC, hi > 15);
            A = (byte)(((hi << 4) | (lo & 0x0F)) & 0xFF);
            return;
        }
        int r = A + m + c;
        SetFlag(FlagC, r > 0xFF);
        SetFlag(FlagV, ((~(A ^ m) & (A ^ r)) & 0x80) != 0);
        A = (byte)r;
        Nz(A);
    }

    private void Sbc(byte m)
    {
        int borrow = 1 - (P & FlagC);
        int r = A - m - borrow;
        if (GetFlag(FlagD))
        {
            int lo = (A & 0x0F) - (m & 0x0F) - borrow;
            int hi = (A >> 4) - (m >> 4);
            if (lo < 0) { lo -= 6; hi--; }
            if (hi < 0) hi -= 6;
            SetFlag(FlagC, r >= 0);
            SetFlag(FlagV, (((A ^ m) & (A ^ r)) & 0x80) != 0);
            Nz((byte)r);
            A = (byte)(((hi << 4) | (lo & 0x0F)) & 0xFF);
            return;
        }
        SetFlag(FlagC, r >= 0);
        SetFlag(FlagV, (((A ^ m) & (A ^ r)) & 0x80) != 0);
        A = (byte)r;
        Nz(A);
    }

    private void Cmp(byte reg, byte m)
    {
        int r = reg - m;
        SetFlag(FlagC, reg >= m);
        Nz((byte)r);
    }

    private byte Asl(byte v) { SetFlag(FlagC, (v & 0x80) != 0); v <<= 1; Nz(v); return v; }
    private byte Lsr(byte v) { SetFlag(FlagC, (v & 1) != 0); v >>= 1; Nz(v); return v; }
    private byte Rol(byte v) { int c = P & FlagC; SetFlag(FlagC, (v & 0x80) != 0); v = (byte)((v << 1) | c); Nz(v); return v; }
    private byte Ror(byte v) { int c = P & FlagC; SetFlag(FlagC, (v & 1) != 0); v = (byte)((v >> 1) | (c << 7)); Nz(v); return v; }

    private void Bit(byte m)
    {
        SetFlag(FlagZ, (A & m) == 0);
        SetFlag(FlagN, (m & 0x80) != 0);
        SetFlag(FlagV, (m & 0x40) != 0);
    }

    private void Branch(bool take)
    {
        sbyte off = (sbyte)Fetch();
        if (!take) return;
        int target = (PC + off) & 0xFFFF;
        Cycles += ((target ^ PC) & 0xFF00) != 0 ? 2 : 1;
        PC = (ushort)target;
    }

    private byte Inc(byte v) { v++; Nz(v); return v; }
    private byte Dec(byte v) { v--; Nz(v); return v; }

    // Base cycle counts per opcode (documented ones; others default to 2).
    private static readonly byte[] BaseCycles =
    {
        7,6,2,8,3,3,5,5,3,2,2,2,4,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
        6,6,2,8,3,3,5,5,4,2,2,2,4,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
        6,6,2,8,3,3,5,5,3,2,2,2,3,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
        6,6,2,8,3,3,5,5,4,2,2,2,5,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
        2,6,2,6,3,3,3,3,2,2,2,2,4,4,4,4, 2,6,2,6,4,4,4,4,2,5,2,5,5,5,5,5,
        2,6,2,6,3,3,3,3,2,2,2,2,4,4,4,4, 2,5,2,5,4,4,4,4,2,4,2,4,4,4,4,4,
        2,6,2,8,3,3,5,5,2,2,2,2,4,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
        2,6,2,8,3,3,5,5,2,2,2,2,4,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
    };

    /// <summary>Executes one instruction; returns the cycles it took.</summary>
    public int Step()
    {
        long start = Cycles;
        byte op = Fetch();
        Cycles += BaseCycles[op];
        switch (op)
        {
            // LDA
            case 0xA9: A = Fetch(); Nz(A); break;
            case 0xA5: A = Rd(Zp()); Nz(A); break;
            case 0xB5: A = Rd(Zpx()); Nz(A); break;
            case 0xAD: A = Rd(Abs()); Nz(A); break;
            case 0xBD: A = Rd(Abx(true)); Nz(A); break;
            case 0xB9: A = Rd(Aby(true)); Nz(A); break;
            case 0xA1: A = Rd(Izx()); Nz(A); break;
            case 0xB1: A = Rd(Izy(true)); Nz(A); break;
            // LDX
            case 0xA2: X = Fetch(); Nz(X); break;
            case 0xA6: X = Rd(Zp()); Nz(X); break;
            case 0xB6: X = Rd(Zpy()); Nz(X); break;
            case 0xAE: X = Rd(Abs()); Nz(X); break;
            case 0xBE: X = Rd(Aby(true)); Nz(X); break;
            // LDY
            case 0xA0: Y = Fetch(); Nz(Y); break;
            case 0xA4: Y = Rd(Zp()); Nz(Y); break;
            case 0xB4: Y = Rd(Zpx()); Nz(Y); break;
            case 0xAC: Y = Rd(Abs()); Nz(Y); break;
            case 0xBC: Y = Rd(Abx(true)); Nz(Y); break;
            // STA
            case 0x85: Wr(Zp(), A); break;
            case 0x95: Wr(Zpx(), A); break;
            case 0x8D: Wr(Abs(), A); break;
            case 0x9D: Wr(Abx(false), A); break;
            case 0x99: Wr(Aby(false), A); break;
            case 0x81: Wr(Izx(), A); break;
            case 0x91: Wr(Izy(false), A); break;
            // STX / STY
            case 0x86: Wr(Zp(), X); break;
            case 0x96: Wr(Zpy(), X); break;
            case 0x8E: Wr(Abs(), X); break;
            case 0x84: Wr(Zp(), Y); break;
            case 0x94: Wr(Zpx(), Y); break;
            case 0x8C: Wr(Abs(), Y); break;
            // transfers
            case 0xAA: X = A; Nz(X); break;
            case 0xA8: Y = A; Nz(Y); break;
            case 0x8A: A = X; Nz(A); break;
            case 0x98: A = Y; Nz(A); break;
            case 0xBA: X = SP; Nz(X); break;
            case 0x9A: SP = X; break;
            // stack
            case 0x48: Push(A); break;
            case 0x08: Push((byte)(P | FlagB | FlagU)); break;
            case 0x68: A = Pull(); Nz(A); break;
            case 0x28: P = (byte)((Pull() & ~FlagB) | FlagU); break;
            // ADC
            case 0x69: Adc(Fetch()); break;
            case 0x65: Adc(Rd(Zp())); break;
            case 0x75: Adc(Rd(Zpx())); break;
            case 0x6D: Adc(Rd(Abs())); break;
            case 0x7D: Adc(Rd(Abx(true))); break;
            case 0x79: Adc(Rd(Aby(true))); break;
            case 0x61: Adc(Rd(Izx())); break;
            case 0x71: Adc(Rd(Izy(true))); break;
            // SBC
            case 0xE9: Sbc(Fetch()); break;
            case 0xE5: Sbc(Rd(Zp())); break;
            case 0xF5: Sbc(Rd(Zpx())); break;
            case 0xED: Sbc(Rd(Abs())); break;
            case 0xFD: Sbc(Rd(Abx(true))); break;
            case 0xF9: Sbc(Rd(Aby(true))); break;
            case 0xE1: Sbc(Rd(Izx())); break;
            case 0xF1: Sbc(Rd(Izy(true))); break;
            // AND
            case 0x29: A &= Fetch(); Nz(A); break;
            case 0x25: A &= Rd(Zp()); Nz(A); break;
            case 0x35: A &= Rd(Zpx()); Nz(A); break;
            case 0x2D: A &= Rd(Abs()); Nz(A); break;
            case 0x3D: A &= Rd(Abx(true)); Nz(A); break;
            case 0x39: A &= Rd(Aby(true)); Nz(A); break;
            case 0x21: A &= Rd(Izx()); Nz(A); break;
            case 0x31: A &= Rd(Izy(true)); Nz(A); break;
            // ORA
            case 0x09: A |= Fetch(); Nz(A); break;
            case 0x05: A |= Rd(Zp()); Nz(A); break;
            case 0x15: A |= Rd(Zpx()); Nz(A); break;
            case 0x0D: A |= Rd(Abs()); Nz(A); break;
            case 0x1D: A |= Rd(Abx(true)); Nz(A); break;
            case 0x19: A |= Rd(Aby(true)); Nz(A); break;
            case 0x01: A |= Rd(Izx()); Nz(A); break;
            case 0x11: A |= Rd(Izy(true)); Nz(A); break;
            // EOR
            case 0x49: A ^= Fetch(); Nz(A); break;
            case 0x45: A ^= Rd(Zp()); Nz(A); break;
            case 0x55: A ^= Rd(Zpx()); Nz(A); break;
            case 0x4D: A ^= Rd(Abs()); Nz(A); break;
            case 0x5D: A ^= Rd(Abx(true)); Nz(A); break;
            case 0x59: A ^= Rd(Aby(true)); Nz(A); break;
            case 0x41: A ^= Rd(Izx()); Nz(A); break;
            case 0x51: A ^= Rd(Izy(true)); Nz(A); break;
            // CMP / CPX / CPY
            case 0xC9: Cmp(A, Fetch()); break;
            case 0xC5: Cmp(A, Rd(Zp())); break;
            case 0xD5: Cmp(A, Rd(Zpx())); break;
            case 0xCD: Cmp(A, Rd(Abs())); break;
            case 0xDD: Cmp(A, Rd(Abx(true))); break;
            case 0xD9: Cmp(A, Rd(Aby(true))); break;
            case 0xC1: Cmp(A, Rd(Izx())); break;
            case 0xD1: Cmp(A, Rd(Izy(true))); break;
            case 0xE0: Cmp(X, Fetch()); break;
            case 0xE4: Cmp(X, Rd(Zp())); break;
            case 0xEC: Cmp(X, Rd(Abs())); break;
            case 0xC0: Cmp(Y, Fetch()); break;
            case 0xC4: Cmp(Y, Rd(Zp())); break;
            case 0xCC: Cmp(Y, Rd(Abs())); break;
            // BIT
            case 0x24: Bit(Rd(Zp())); break;
            case 0x2C: Bit(Rd(Abs())); break;
            // INC / DEC
            case 0xE6: { int a = Zp(); Wr(a, Inc(Rd(a))); }; break;
            case 0xF6: { int a = Zpx(); Wr(a, Inc(Rd(a))); }; break;
            case 0xEE: { int a = Abs(); Wr(a, Inc(Rd(a))); }; break;
            case 0xFE: { int a = Abx(false); Wr(a, Inc(Rd(a))); }; break;
            case 0xC6: { int a = Zp(); Wr(a, Dec(Rd(a))); }; break;
            case 0xD6: { int a = Zpx(); Wr(a, Dec(Rd(a))); }; break;
            case 0xCE: { int a = Abs(); Wr(a, Dec(Rd(a))); }; break;
            case 0xDE: { int a = Abx(false); Wr(a, Dec(Rd(a))); }; break;
            case 0xE8: X++; Nz(X); break;
            case 0xC8: Y++; Nz(Y); break;
            case 0xCA: X--; Nz(X); break;
            case 0x88: Y--; Nz(Y); break;
            // shifts
            case 0x0A: A = Asl(A); break;
            case 0x06: { int a = Zp(); Wr(a, Asl(Rd(a))); }; break;
            case 0x16: { int a = Zpx(); Wr(a, Asl(Rd(a))); }; break;
            case 0x0E: { int a = Abs(); Wr(a, Asl(Rd(a))); }; break;
            case 0x1E: { int a = Abx(false); Wr(a, Asl(Rd(a))); }; break;
            case 0x4A: A = Lsr(A); break;
            case 0x46: { int a = Zp(); Wr(a, Lsr(Rd(a))); }; break;
            case 0x56: { int a = Zpx(); Wr(a, Lsr(Rd(a))); }; break;
            case 0x4E: { int a = Abs(); Wr(a, Lsr(Rd(a))); }; break;
            case 0x5E: { int a = Abx(false); Wr(a, Lsr(Rd(a))); }; break;
            case 0x2A: A = Rol(A); break;
            case 0x26: { int a = Zp(); Wr(a, Rol(Rd(a))); }; break;
            case 0x36: { int a = Zpx(); Wr(a, Rol(Rd(a))); }; break;
            case 0x2E: { int a = Abs(); Wr(a, Rol(Rd(a))); }; break;
            case 0x3E: { int a = Abx(false); Wr(a, Rol(Rd(a))); }; break;
            case 0x6A: A = Ror(A); break;
            case 0x66: { int a = Zp(); Wr(a, Ror(Rd(a))); }; break;
            case 0x76: { int a = Zpx(); Wr(a, Ror(Rd(a))); }; break;
            case 0x6E: { int a = Abs(); Wr(a, Ror(Rd(a))); }; break;
            case 0x7E: { int a = Abx(false); Wr(a, Ror(Rd(a))); }; break;
            // jumps
            case 0x4C: PC = Fetch16(); break;
            case 0x6C:
            {
                // The NMOS indirect-jump bug: the high byte comes from the same page.
                ushort ptr = Fetch16();
                PC = (ushort)(Rd(ptr) | (Rd((ptr & 0xFF00) | ((ptr + 1) & 0xFF)) << 8));
                break;
            }
            case 0x20:
            {
                ushort target = Fetch16();
                ushort ret = (ushort)(PC - 1);
                Push((byte)(ret >> 8)); Push((byte)ret);
                PC = target;
                break;
            }
            case 0x60: { int lo = Pull(); int hi = Pull(); PC = (ushort)(((hi << 8) | lo) + 1); break; }
            case 0x40:
            {
                P = (byte)((Pull() & ~FlagB) | FlagU);
                int lo = Pull(); int hi = Pull();
                PC = (ushort)((hi << 8) | lo);
                break;
            }
            case 0x00:
            {
                PC++;
                Push((byte)(PC >> 8)); Push((byte)PC); Push((byte)(P | FlagB | FlagU));
                SetFlag(FlagI, true);
                PC = Rd16(0xFFFE);
                break;
            }
            // branches
            case 0x10: Branch(!GetFlag(FlagN)); break;
            case 0x30: Branch(GetFlag(FlagN)); break;
            case 0x50: Branch(!GetFlag(FlagV)); break;
            case 0x70: Branch(GetFlag(FlagV)); break;
            case 0x90: Branch(!GetFlag(FlagC)); break;
            case 0xB0: Branch(GetFlag(FlagC)); break;
            case 0xD0: Branch(!GetFlag(FlagZ)); break;
            case 0xF0: Branch(GetFlag(FlagZ)); break;
            // flags
            case 0x18: SetFlag(FlagC, false); break;
            case 0x38: SetFlag(FlagC, true); break;
            case 0x58: SetFlag(FlagI, false); break;
            case 0x78: SetFlag(FlagI, true); break;
            case 0xB8: SetFlag(FlagV, false); break;
            case 0xD8: SetFlag(FlagD, false); break;
            case 0xF8: SetFlag(FlagD, true); break;
            case 0xEA: break;
            default:
                IllegalOpcodes++;
                SkipIllegal(op);
                break;
        }
        return (int)(Cycles - start);
    }

    /// <summary>Steps over an undocumented opcode using its usual operand length.</summary>
    private void SkipIllegal(byte op)
    {
        int mode = op & 0x1F;
        int len = mode switch
        {
            0x00 or 0x02 or 0x04 or 0x06 or 0x09 or 0x0B or 0x01 or 0x03 or 0x05 or 0x07 or 0x10 or 0x11 or 0x13 or 0x14 or 0x15 or 0x16 or 0x17 => 1,
            0x0C or 0x0D or 0x0E or 0x0F or 0x19 or 0x1B or 0x1C or 0x1D or 0x1E or 0x1F => 2,
            _ => 0,
        };
        PC = (ushort)(PC + len);
    }
}
