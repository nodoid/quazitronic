using Orictron.Emulation;
using Xunit;

namespace Orictron.Tests;

/// <summary>The 6502 core: flags, arithmetic (binary and decimal), addressing quirks and cycle counts.</summary>
public sealed class CpuTests
{
    private sealed class Ram : IBus
    {
        public readonly byte[] M = new byte[65536];
        public byte Read(ushort a) => M[a];
        public void Write(ushort a, byte v) => M[a] = v;
    }

    private static (Cpu6502 cpu, Ram ram) Load(params byte[] code)
    {
        var ram = new Ram();
        code.CopyTo(ram.M, 0x0200);
        var cpu = new Cpu6502(ram);
        cpu.Reset(0x0200);
        return (cpu, ram);
    }

    private static void Run(Cpu6502 cpu, int steps)
    {
        for (int i = 0; i < steps; i++) cpu.Step();
    }

    [Fact]
    public void LoadSetsZeroAndNegative()
    {
        var (cpu, _) = Load(0xA9, 0x00, 0xA9, 0x80);
        cpu.Step();
        Assert.True(cpu.GetFlag(Cpu6502.FlagZ));
        cpu.Step();
        Assert.True(cpu.GetFlag(Cpu6502.FlagN));
        Assert.False(cpu.GetFlag(Cpu6502.FlagZ));
    }

    [Theory]
    [InlineData(0x50, 0x10, false, 0x60, false, false)]
    [InlineData(0x50, 0x50, false, 0xA0, false, true)]  // positive + positive = negative: overflow
    [InlineData(0xFF, 0x01, false, 0x00, true, false)]
    [InlineData(0x80, 0x80, false, 0x00, true, true)]
    [InlineData(0x01, 0x01, true, 0x03, false, false)]  // carry in
    public void AddWithCarry(byte a, byte m, bool carryIn, byte result, bool carry, bool overflow)
    {
        var (cpu, _) = Load(carryIn ? (byte)0x38 : (byte)0x18, 0xA9, a, 0x69, m);
        Run(cpu, 3);
        Assert.Equal(result, cpu.A);
        Assert.Equal(carry, cpu.GetFlag(Cpu6502.FlagC));
        Assert.Equal(overflow, cpu.GetFlag(Cpu6502.FlagV));
    }

    [Theory]
    [InlineData(0x50, 0x10, 0x40, true)]
    [InlineData(0x10, 0x20, 0xF0, false)] // borrow
    public void SubtractWithCarry(byte a, byte m, byte result, bool carry)
    {
        var (cpu, _) = Load(0x38, 0xA9, a, 0xE9, m);
        Run(cpu, 3);
        Assert.Equal(result, cpu.A);
        Assert.Equal(carry, cpu.GetFlag(Cpu6502.FlagC));
    }

    [Fact]
    public void DecimalModeAddsInBcd()
    {
        var (cpu, _) = Load(0xF8, 0x18, 0xA9, 0x19, 0x69, 0x28, 0xD8);
        Run(cpu, 4);
        Assert.Equal(0x47, cpu.A);
        var (cpu2, _) = Load(0xF8, 0x18, 0xA9, 0x99, 0x69, 0x01);
        Run(cpu2, 4);
        Assert.Equal(0x00, cpu2.A);
        Assert.True(cpu2.GetFlag(Cpu6502.FlagC));
    }

    [Fact]
    public void DecimalModeSubtractsInBcd()
    {
        var (cpu, _) = Load(0xF8, 0x38, 0xA9, 0x42, 0xE9, 0x13);
        Run(cpu, 4);
        Assert.Equal(0x29, cpu.A);
    }

    [Fact]
    public void SubroutineCallAndReturn()
    {
        // JSR $0210 ; LDX #1 ; ... $0210: LDA #7 ; RTS
        var (cpu, ram) = Load(0x20, 0x10, 0x02, 0xA2, 0x01);
        ram.M[0x0210] = 0xA9; ram.M[0x0211] = 0x07; ram.M[0x0212] = 0x60;
        Run(cpu, 4);
        Assert.Equal(7, cpu.A);
        Assert.Equal(1, cpu.X);
        Assert.Equal(0x0205, cpu.PC);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void IndirectJumpWrapsWithinThePage()
    {
        // JMP ($02FF): the NMOS chip takes the high byte from $0200, not $0300.
        var (cpu, ram) = Load(0x6C, 0xFF, 0x02);
        ram.M[0x02FF] = 0x34;
        ram.M[0x0300] = 0x99;
        ram.M[0x0200] = 0x6C; // also the opcode byte - the high byte the bug reads
        cpu.Step();
        Assert.Equal(0x6C34, cpu.PC);
    }

    [Fact]
    public void BranchCyclesIncludeTakenAndPageCrossPenalties()
    {
        var (cpu, _) = Load(0xA9, 0x00, 0xF0, 0x02, 0xEA, 0xEA, 0xD0, 0x00);
        cpu.Step();
        Assert.Equal(3, cpu.Step()); // BEQ taken, same page
        Assert.Equal(2, cpu.Step()); // BNE not taken
        var (c2, ram) = Load();
        c2.Reset(0x02F0);
        ram.M[0x02F0] = 0xA9; ram.M[0x02F1] = 0x00; ram.M[0x02F2] = 0xF0; ram.M[0x02F3] = 0x20;
        c2.Step();
        Assert.Equal(4, c2.Step()); // taken across into $03xx
    }

    [Fact]
    public void IndexedReadsPayForPageCrossing()
    {
        var (cpu, _) = Load(0xA2, 0x01, 0xBD, 0xFF, 0x10, 0xBD, 0x00, 0x10);
        cpu.Step();
        Assert.Equal(5, cpu.Step()); // LDA $10FF,X crosses
        Assert.Equal(4, cpu.Step());
    }

    [Fact]
    public void StackPushPullAndFlags()
    {
        // LDA #$C3 PHA LDA #0 PLA ; SEC PHP CLC PLP
        var (cpu, _) = Load(0xA9, 0xC3, 0x48, 0xA9, 0x00, 0x68, 0x38, 0x08, 0x18, 0x28);
        Run(cpu, 4);
        Assert.Equal(0xC3, cpu.A);
        Assert.True(cpu.GetFlag(Cpu6502.FlagN));
        Run(cpu, 4);
        Assert.True(cpu.GetFlag(Cpu6502.FlagC));
    }

    [Fact]
    public void ShiftsAndRotatesCarryThrough()
    {
        // LDA #$81 ASL -> $02 C=1 ; ROL -> $05 C=0 ; LSR -> $02 C=1 ; ROR -> $81
        var (cpu, _) = Load(0xA9, 0x81, 0x0A, 0x2A, 0x4A, 0x6A);
        Run(cpu, 2);
        Assert.Equal(0x02, cpu.A); Assert.True(cpu.GetFlag(Cpu6502.FlagC));
        cpu.Step();
        Assert.Equal(0x05, cpu.A); Assert.False(cpu.GetFlag(Cpu6502.FlagC));
        cpu.Step();
        Assert.Equal(0x02, cpu.A); Assert.True(cpu.GetFlag(Cpu6502.FlagC));
        cpu.Step();
        Assert.Equal(0x81, cpu.A);
    }

    [Fact]
    public void MemoryIncrementDecrementAndCompare()
    {
        // INC $10 ; INC $10 ; DEC $10 ; LDA #1 ; CMP $10
        var (cpu, ram) = Load(0xE6, 0x10, 0xE6, 0x10, 0xC6, 0x10, 0xA9, 0x01, 0xC5, 0x10);
        Run(cpu, 5);
        Assert.Equal(1, ram.M[0x10]);
        Assert.True(cpu.GetFlag(Cpu6502.FlagZ));
        Assert.True(cpu.GetFlag(Cpu6502.FlagC));
    }

    [Fact]
    public void BitTestsCopyTopBits()
    {
        var (cpu, ram) = Load(0xA9, 0x01, 0x24, 0x10);
        ram.M[0x10] = 0xC0;
        Run(cpu, 2);
        Assert.True(cpu.GetFlag(Cpu6502.FlagN));
        Assert.True(cpu.GetFlag(Cpu6502.FlagV));
        Assert.True(cpu.GetFlag(Cpu6502.FlagZ));
    }

    [Fact]
    public void IndirectIndexedAddressing()
    {
        // LDY #2 ; LDA ($20),Y with ($20) = $1234 -> reads $1236
        var (cpu, ram) = Load(0xA0, 0x02, 0xB1, 0x20);
        ram.M[0x20] = 0x34; ram.M[0x21] = 0x12; ram.M[0x1236] = 0x5A;
        Run(cpu, 2);
        Assert.Equal(0x5A, cpu.A);
    }
}
