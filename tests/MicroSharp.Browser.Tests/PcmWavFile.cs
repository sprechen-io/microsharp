using System.Buffers.Binary;

namespace MicroSharp.Browser.Tests;

internal static class PcmWavFile
{
    public static void WriteSine(string path, int sampleRate, double frequencyHz, double seconds, double amplitude)
    {
        var sampleCount = checked((int)Math.Round(sampleRate * seconds));
        var data = new byte[sampleCount * sizeof(short)];
        for (var index = 0; index < sampleCount; index++)
        {
            var value = Math.Sin(2d * Math.PI * frequencyHz * index / sampleRate) * amplitude;
            var scaled = value < 0 ? value * 32768d : value * 32767d;
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(index * sizeof(short)), (short)scaled);
        }

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + data.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
    }
}
