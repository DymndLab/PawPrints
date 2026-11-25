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

    static readonly uint[] crcTable = BuildCrcTable();

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) == 0 ? value >> 1 : 0xedb88320 ^ (value >> 1);
            table[i] = value;
        }
        return table;
    }

    static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) crc = crcTable[(byte)(crc ^ b)] ^ (crc >> 8);
        return crc;
    }

    static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~UpdateCrc(UpdateCrc(uint.MaxValue, type), data));
        output.Write(number);
    }

    sealed class ImageChunks(Stream output) : Stream
    {
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> data) => Chunk(output, "IDAT"u8, data);
        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => output.Flush();
    }

    public static void Write(string path, int width, int height, byte[] rgb, int? pixelsPerMetre = null)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try { Encode(file, width, height, rgb, pixelsPerMetre, default); }
        catch { file.Dispose(); File.Delete(path); throw; }
    }

    internal static void Encode(Stream output, int width, int height, byte[] rgb, int? density, CancellationToken token)
    {
        if (width <= 0 || height <= 0 || (long)width * height != rgb.LongLength / 3 || rgb.Length % 3 != 0)
            throw new ArgumentException("Invalid PNG dimensions or RGB buffer.");
        if (density <= 0) throw new ArgumentOutOfRangeException(nameof(density));
        token.ThrowIfCancellationRequested();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        Chunk(output, "IHDR"u8, header);
        if (density is { } physicalDensity)
        {
            var physical = new byte[9];
            BinaryPrimitives.WriteInt32BigEndian(physical, physicalDensity);
            BinaryPrimitives.WriteInt32BigEndian(physical.AsSpan(4), physicalDensity);
            physical[8] = 1;
            Chunk(output, "pHYs"u8, physical);
        }
        using (var chunks = new BufferedStream(new ImageChunks(output), 64 * 1024))
        using (var z = new ZLibStream(chunks, CompressionLevel.Optimal, true))
        {
            var row = new byte[1 + checked(width * 3)];
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                rgb.AsSpan(y * (row.Length - 1), row.Length - 1).CopyTo(row.AsSpan(1));
                z.Write(row);
            }
        }
        token.ThrowIfCancellationRequested();
        Chunk(output, "IEND"u8, []);
    }
}
