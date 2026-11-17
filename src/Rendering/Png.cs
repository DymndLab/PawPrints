using System.IO.Compression;
using System.Buffers.Binary;
namespace Blueprint.Rendering;

static class Png
{

    public static byte[] ReadOwn(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)), height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
        using var idat = new MemoryStream();
        for (int p = 8; p < bytes.Length;)
        {
            int n = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(p));
            if (System.Text.Encoding.ASCII.GetString(bytes, p + 4, 4) == "IDAT")
                idat.Write(bytes, p + 8, n);
            p += n + 12;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            if (z.ReadByte() != 0)
                throw new InvalidDataException("Only this renderer's filter-zero RGB images are supported");
            z.ReadExactly(rgb.AsSpan(y * width * 3, width * 3));
        }
        return rgb;
    }

    static uint Crc(ReadOnlySpan<byte> data)
    {
        uint c = 0xffffffff;
        foreach (byte b in data)
        {
            c ^= b;
            for (int i = 0; i < 8; i++)
                c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xffffffff;
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        byte[] len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var bytes = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(len, Crc(bytes));
        s.Write(len);
    }

    public static void Write(string path, int width, int height, byte[] rgb, int? pixelsPerMetre = null)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        Chunk(file, "IHDR", header);
        if (pixelsPerMetre is { } density)
        {
            if (density <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelsPerMetre));
            var physical = new byte[9];
            BinaryPrimitives.WriteInt32BigEndian(physical, density);
            BinaryPrimitives.WriteInt32BigEndian(physical.AsSpan(4), density);
            physical[8] = 1;
            Chunk(file, "pHYs", physical);
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgb, y * width * 3, width * 3);
            }
        Chunk(file, "IDAT", compressed.ToArray());
        Chunk(file, "IEND", []);
    }
}
