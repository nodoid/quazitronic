using System;
using System.IO;

namespace Orictron.Emulation;

/// <summary>
/// An Oric cassette image (.tap): sync bytes ($16), $24, a 9-byte header (type, autorun, end and
/// start addresses), a zero-terminated file name, then the data. cc65 programs start with a one-line
/// BASIC stub ("CALL #50D"); <see cref="EntryPoint"/> finds that address.
/// </summary>
public sealed class TapFile
{
    public required string Name { get; init; }
    public required ushort Start { get; init; }
    public required ushort End { get; init; }
    public required bool IsBasic { get; init; }
    public required bool AutoRun { get; init; }
    public required byte[] Data { get; init; }

    /// <summary>Where to begin executing: the CALL target in a BASIC stub, else the start address.</summary>
    public ushort EntryPoint
    {
        get
        {
            if (!IsBasic) return Start;
            // Look for CALL (token $BF) followed by "#hex" in the first BASIC line.
            for (int i = 4; i < Math.Min(Data.Length - 2, 64); i++)
            {
                if (Data[i] != 0xBF) continue;
                int k = i + 1;
                while (k < Data.Length && Data[k] == ' ') k++;
                if (k < Data.Length && Data[k] == '#')
                {
                    int value = 0, digits = 0;
                    for (k++; k < Data.Length && Uri.IsHexDigit((char)Data[k]); k++, digits++)
                        value = value * 16 + Convert.ToInt32(((char)Data[k]).ToString(), 16);
                    if (digits > 0) return (ushort)value;
                }
            }
            return (ushort)(Start + 12);
        }
    }

    public static TapFile Parse(byte[] tap)
    {
        int i = 0;
        while (i < tap.Length && tap[i] == 0x16) i++;
        if (i == 0 || i >= tap.Length || tap[i] != 0x24) throw new InvalidDataException("Not an Oric .tap file (no sync).");
        i++;
        if (i + 9 > tap.Length) throw new InvalidDataException("Truncated .tap header.");
        byte type = tap[i + 2];
        byte autorun = tap[i + 3];
        ushort end = (ushort)((tap[i + 4] << 8) | tap[i + 5]);
        ushort start = (ushort)((tap[i + 6] << 8) | tap[i + 7]);
        i += 9;
        int nameStart = i;
        while (i < tap.Length && tap[i] != 0) i++;
        string name = System.Text.Encoding.ASCII.GetString(tap, nameStart, i - nameStart);
        i++;
        int length = end - start + 1;
        if (length <= 0 || i + length > tap.Length) throw new InvalidDataException("Truncated .tap data.");
        var data = new byte[length];
        Array.Copy(tap, i, data, 0, length);
        return new TapFile
        {
            Name = name,
            Start = start,
            End = end,
            IsBasic = type == 0x00,
            AutoRun = autorun != 0,
            Data = data,
        };
    }

    /// <summary>The Orictron tape built into the app.</summary>
    public static TapFile LoadEmbedded()
    {
        using var stream = typeof(TapFile).Assembly.GetManifestResourceStream("Orictron.orictron.tap")
                           ?? throw new FileNotFoundException("Embedded orictron.tap is missing.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray());
    }
}
