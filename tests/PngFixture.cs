using System.Buffers.Binary;
using System.IO.Compression;

// Synthetic PNGs keep decoder tests independent of any image-library package.
static class PngFixture
{
    public static byte[] Rgba(int width, int height, byte[] pixels, bool interlaced = false)
    {
        using var rows = new MemoryStream();
        int[][] passes = interlaced
            ? [[0, 0, 8, 8], [4, 0, 8, 8], [0, 4, 4, 8], [2, 0, 4, 4], [0, 2, 2, 4], [1, 0, 2, 2], [0, 1, 1, 2]]
            : [[0, 0, 1, 1]];
        foreach (var p in passes)
            if (p[0] < width)
                for (int y = p[1]; y < height; y += p[3])
                {
                    rows.WriteByte(0);
                    for (int x = p[0]; x < width; x += p[2])
                        rows.Write(pixels, (y * width + x) * 4, 4);
                }
        return Encode(width, height, 8, 6, rows.ToArray(), interlaced: interlaced);
    }

    public static byte[] Encode(int width, int height, byte depth, byte type, byte[] rows,
        byte[]? palette = null, byte[]? transparency = null, bool interlaced = false, bool animated = false)
    {
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = depth;
        header[9] = type;
        header[12] = interlaced ? (byte)1 : (byte)0;
        Chunk(png, "IHDR"u8, header);
        if (palette != null)
            Chunk(png, "PLTE"u8, palette);
        if (transparency != null)
            Chunk(png, "tRNS"u8, transparency);
        if (animated)
            Chunk(png, "acTL"u8, [0, 0, 0, 1, 0, 0, 0, 0]);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            zlib.Write(rows);
        Chunk(png, "IDAT"u8, compressed.ToArray());
        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in type.ToArray().Concat(data.ToArray()))
        {
            crc ^= value;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320 : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }
}
