using System;
using System.IO;
using System.IO.Compression;

namespace Orictron.Tests;

/// <summary>Minimal PNG encoder for packed RGBA pixels (test screenshots).</summary>
public static class PngWriter
{
    public static void Write(string path, uint[] rgba, int width, int height, int scale = 1)
    {
        using var f = File.Create(path);
        Write(f, rgba, width, height, scale);
    }

    public static void Write(Stream output, uint[] rgba, int width, int height, int scale = 1)
    {
        int w = width * scale, h = height * scale;
        var raw = new byte[(w * 3 + 1) * h];
        int o = 0;
        for (int y = 0; y < h; y++)
        {
            raw[o++] = 0;
            int sy = y / scale;
            for (int x = 0; x < w; x++)
            {
                uint p = rgba[sy * width + x / scale];
                raw[o++] = (byte)p;
                raw[o++] = (byte)(p >> 8);
                raw[o++] = (byte)(p >> 16);
            }
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, (uint)w);
        WriteBe(ihdr, 4, (uint)h);
        ihdr[8] = 8; ihdr[9] = 2;
        Chunk(output, "IHDR", ihdr);
        Chunk(output, "IDAT", ms.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
    }

    private static void WriteBe(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, (uint)data.Length);
        s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        data.CopyTo(td, 4);
        s.Write(td);
        var crc = new byte[4];
        WriteBe(crc, 0, Crc(td));
        s.Write(crc);
    }

    private static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }
}
