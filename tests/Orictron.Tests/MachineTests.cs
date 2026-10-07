using Orictron.Emulation;
using Orictron.Game;
using Xunit;

namespace Orictron.Tests;

/// <summary>The VIA, AY chip, keyboard matrix, TAP loader and the whole emulated machine running Orictron.</summary>
public sealed class MachineTests
{
    private static void AyWrite(OricMachine m, int reg, int val)
    {
        m.Write(0x030F, (byte)reg); m.Write(0x030C, 0xFF); m.Write(0x030C, 0xDD);
        m.Write(0x030F, (byte)val); m.Write(0x030C, 0xFD); m.Write(0x030C, 0xDD);
    }

    [Fact]
    public void TapeHeaderIsReadCorrectly()
    {
        var tap = TapFile.LoadEmbedded();
        Assert.True(tap.IsBasic);
        Assert.True(tap.AutoRun);
        Assert.Equal(0x0501, tap.Start);
        Assert.Equal(0x83D7, tap.End);
        Assert.Equal(0x050D, tap.EntryPoint); // CALL #50D in the BASIC stub
        Assert.Equal(tap.End - tap.Start + 1, tap.Data.Length);
    }

    [Fact]
    public void NotATapeIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => TapFile.Parse(new byte[] { 1, 2, 3, 4 }));
        Assert.Throws<InvalidDataException>(() => TapFile.Parse(new byte[] { 0x16, 0x16, 0x24, 0, 0 }));
    }

    [Fact]
    public void ViaTimer1FreeRunsAndFlags()
    {
        var m = new OricMachine();
        m.Write(0x030B, 0x40);            // continuous
        m.Write(0x0304, 0x0E); m.Write(0x0305, 0x4E); // 19982
        Assert.Equal(0, m.Via.Ifr & Via6522.IrqT1);
        m.Via.Tick(20000);
        Assert.NotEqual(0, m.Via.Ifr & Via6522.IrqT1);
        m.Read(0x0304);                    // reading the low counter acknowledges
        Assert.Equal(0, m.Via.Ifr & Via6522.IrqT1);
        m.Via.Tick(20000);
        Assert.NotEqual(0, m.Via.Ifr & Via6522.IrqT1);
    }

    [Fact]
    public void ViaTimer2IsAOneShotStopwatch()
    {
        var m = new OricMachine();
        m.Write(0x0308, 0xFF); m.Write(0x0309, 0xFF);
        m.Via.Tick(40000);
        Assert.Equal((0xFFFF - 40000) >> 8, m.Read(0x0309));
        Assert.Equal(0, m.Via.Ifr & Via6522.IrqT2);
        m.Via.Tick(30000);
        Assert.NotEqual(0, m.Via.Ifr & Via6522.IrqT2);
        m.Write(0x0309, 0xFF);             // reload clears it
        Assert.Equal(0, m.Via.Ifr & Via6522.IrqT2);
    }

    [Fact]
    public void AyRegistersAreWrittenThroughTheVia()
    {
        var m = new OricMachine();
        AyWrite(m, 7, 0x3E);
        AyWrite(m, 8, 12);
        Assert.Equal(0x3E, m.Ay.Registers[7]);
        Assert.Equal(12, m.Ay.Registers[8]);
    }

    [Fact]
    public void AyToneHasTheRightPitch()
    {
        var m = new OricMachine(44100);
        AyWrite(m, 0, 100);   // period 100: 1 MHz / 16 / 100 = 625 Hz
        AyWrite(m, 1, 0);
        AyWrite(m, 7, 0x3E);  // tone A only
        AyWrite(m, 8, 15);
        m.Ay.RenderUntil(m.Cycles + OricMachine.CpuHz);
        var buf = new float[m.Ay.Queued];
        m.Ay.MixInto(buf, 1);
        int crossings = 0;
        for (int i = 4000; i < buf.Length; i++)
            if ((buf[i - 1] < 0) != (buf[i] < 0)) crossings++;
        double hz = crossings / 2.0 / ((buf.Length - 4000) / 44100.0);
        Assert.InRange(hz, 600, 650);
    }

    [Fact]
    public void SilentAtVolumeZero()
    {
        var m = new OricMachine(44100);
        AyWrite(m, 0, 100);
        AyWrite(m, 7, 0x3E);
        AyWrite(m, 8, 0);
        m.Ay.RenderUntil(m.Cycles + OricMachine.CpuHz / 4);
        var buf = new float[m.Ay.Queued];
        m.Ay.MixInto(buf, 1);
        Assert.All(buf, v => Assert.InRange(v, -0.001f, 0.001f));
    }

    [Fact]
    public void KeyboardMatrixAnswersOnPortB()
    {
        var m = new OricMachine();
        m.Write(0x0302, 0xF7);             // PB3 input
        m.SetKey(OricKey.Space, true);     // row 4, column 0
        AyWrite(m, 14, 0xFE);              // select column 0
        m.Write(0x0300, 4);
        Assert.NotEqual(0, m.Read(0x0300) & 0x08);
        AyWrite(m, 14, 0xFD);              // a different column
        Assert.Equal(0, m.Read(0x0300) & 0x08);
        m.Write(0x0300, 3);                // a different row
        AyWrite(m, 14, 0xFE);
        Assert.Equal(0, m.Read(0x0300) & 0x08);
    }

    [Fact]
    public void RomAreaIgnoresWrites()
    {
        var m = new OricMachine();
        m.Boot(TapFile.LoadEmbedded());
        m.Write(0xC000, 0x12);
        Assert.Equal(0x60, m.Read(0xC000));
    }

    [Fact]
    public void OrictronBootsToItsTitleScreen()
    {
        var m = new OricMachine();
        m.Boot(TapFile.LoadEmbedded());
        m.Run(3 * OricMachine.CpuHz);
        var px = new uint[OricMachine.ScreenWidth * OricMachine.ScreenHeight];
        m.RenderScreen(px);
        m.RenderScreen(px);
        Assert.Equal(0, m.RomCalls);
        Assert.Equal(0, m.Cpu.IllegalOpcodes);
        Assert.Contains(px, p => p == OricMachine.Palette[4]);      // the blue frame
        Assert.Contains(px, p => p == OricMachine.Palette[3]);      // the yellow logo
        Assert.Equal(0x1E, m.Ram[0xBFDF]);                           // HIRES on
        Assert.True(m.Ay.Queued > 0);
    }

    [Fact]
    public void TheEnhancedGameUsesTheTapesOwnFirstDeck()
    {
        var m = new OricMachine();
        m.Boot(TapFile.LoadEmbedded());
        m.Run(2 * OricMachine.CpuHz);
        m.SetKey(OricKey.Space, true);
        m.Run(OricMachine.CpuHz / 5);
        m.SetKey(OricKey.Space, false);
        m.Run(2 * OricMachine.CpuHz);
        // The game unpacks the deck to $9100: header, then the 16 x 16 tiles.
        var deck = DeckData.Get(0);
        Assert.Equal(16, m.Ram[0x9100]);
        Assert.Equal(deck.StartI, m.Ram[0x9100 + 10]);
        Assert.Equal(deck.StartJ, m.Ram[0x9100 + 11]);
        Assert.Equal(deck.OricInk, m.Ram[0x9100 + 8]);
        for (int j = 0; j < Deck.Size; j++)
            for (int i = 0; i < Deck.Size; i++)
                Assert.Equal(m.Ram[0x9110 + (j << 4 | i)], deck.Tile(i, j));
    }

    [Fact]
    public void ScreenRendersSerialAttributes()
    {
        var m = new OricMachine();
        m.Boot(TapFile.LoadEmbedded());
        m.Ram[0xBFDF] = 0x1E;              // HIRES
        var px = new uint[OricMachine.ScreenWidth * OricMachine.ScreenHeight];
        m.RenderScreen(px);                // latch the mode
        m.Ram[0xA000] = 0x11;              // paper red
        m.Ram[0xA001] = 0x03;              // ink yellow
        m.Ram[0xA002] = 0x7F;              // six ink pixels
        m.Ram[0xA003] = 0xC0;              // inverse, no pixels: paper red inverted = cyan
        m.RenderScreen(px);
        Assert.Equal(OricMachine.Palette[1], px[0]);
        Assert.Equal(OricMachine.Palette[1], px[6]);
        Assert.Equal(OricMachine.Palette[3], px[12]);
        Assert.Equal(OricMachine.Palette[6], px[18]);
    }
}
