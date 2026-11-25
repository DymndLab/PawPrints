using System.Buffers.Binary;
using System.IO.Compression;

namespace Blueprint.Rendering;

static class LogoPng
{
    static readonly uint[] CrcTable = MakeCrcTable();

    public static (int Width, int Height) Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > CompanyLogo.MaxFileBytes)
            throw new InvalidDataException("Logo must be 16 MB or smaller.");
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature) ||
            !bytes.Slice(12, 4).SequenceEqual("IHDR"u8) || BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) != 13)
            throw new InvalidDataException("Choose a valid PNG logo.");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);
        if (width == 0 || height == 0 || width > 8192 || height > 8192 || (ulong)width * height > 16_777_216)
            throw new InvalidDataException("Logo must be at most 8192 pixels per side and 16 megapixels.");
        int depth = bytes[24], colour = bytes[25];
        int channels = colour switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => 0
        };
        bool validDepth = colour switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            _ => depth is 8 or 16,
        };
        if (channels == 0 || !validDepth || bytes[26] != 0 || bytes[27] != 0 || bytes[28] > 1)
            throw new InvalidDataException("Unsupported PNG header.");
        using var compressed = new MemoryStream();
        bool data = false, end = false;
        for (int offset = 8; offset < bytes.Length;)
        {
            if (bytes.Length - offset < 12)
                throw new InvalidDataException("PNG image is incomplete.");
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            if (length > bytes.Length - offset - 12)
                throw new InvalidDataException("PNG image is incomplete.");
            var type = bytes.Slice(offset + 4, 4);
            if (type.SequenceEqual("acTL"u8))
                throw new InvalidDataException("Choose a still PNG logo, not an animated image.");
            if (offset != 8 && type.SequenceEqual("IHDR"u8))
                throw new InvalidDataException("PNG contains duplicate headers.");
            uint crc = uint.MaxValue;
            foreach (byte value in bytes.Slice(offset + 4, (int)length + 4))
                crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 8 + (int)length)..]))
                throw new InvalidDataException("PNG image is damaged.");
            if (type.SequenceEqual("IDAT"u8))
            {
                data = true;
                compressed.Write(bytes.Slice(offset + 8, (int)length));
            }
            offset += (int)length + 12;
            if (type.SequenceEqual("IEND"u8))
            {
                end = length == 0 && offset == bytes.Length;
                break;
            }
        }
        if (!data || !end)
            throw new InvalidDataException("PNG image is incomplete.");
        // WIC tolerates some incomplete pixel streams by filling missing rows.
        // Check the exact decompressed length without allocating a second image.
        int[][] passes = bytes[28] == 1
            ? [[0, 0, 8, 8], [4, 0, 8, 8], [0, 4, 4, 8], [2, 0, 4, 4], [0, 2, 2, 4], [1, 0, 2, 2], [0, 1, 1, 2]]
            : [[0, 0, 1, 1]];
        long expected = 0;
        foreach (var pass in passes)
        {
            long columns = Math.Max(0, ((long)width - pass[0] + pass[2] - 1) / pass[2]);
            long rows = Math.Max(0, ((long)height - pass[1] + pass[3] - 1) / pass[3]);
            if (columns > 0)
                expected += rows * (1 + (columns * channels * depth + 7) / 8);
        }
        compressed.Position = 0;
        using var inflated = new ZLibStream(compressed, CompressionMode.Decompress);
        Span<byte> buffer = stackalloc byte[8192];
        long actual = 0;
        int read;
        while ((read = inflated.Read(buffer)) != 0)
        {
            actual += read;
            if (actual > expected)
                throw new InvalidDataException("PNG contains excess pixel data.");
        }
        if (actual != expected)
            throw new InvalidDataException("PNG image is incomplete.");
        return ((int)width, (int)height);
    }

    static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
                value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320);
            table[i] = value;
        }
        return table;
    }
}
