using BCnEncoder.Decoder;
using BCnEncoder.Shared;
namespace Blueprint.Rendering;

// Vulkan format IDs are serialized values, so this worker-side type needs no game assembly.
sealed record RawTexture(TextureInfo Info, int Format, byte[] Data)
{

    public static bool Supported(int format) => format is 37 or 43 or 44 or 50 or 131 or 132 or 133 or 134 or 137 or 138 or 139 or 141 or 145 or 146;

    public Texture Decode(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int count = checked(Info.Width * Info.Height);
        var bgra = new byte[checked(count * 4)];
        if (Format is 37 or 43 or 44 or 50)
        {
            if (Data.Length < bgra.Length)
                throw new InvalidDataException("Truncated material.");
            for (int i = 0; i < count; i++)
            {
                int q = i * 4;
                bool bgr = Format is 44 or 50;
                bgra[q] = Data[q + (bgr ? 0 : 2)];
                bgra[q + 1] = Data[q + 1];
                bgra[q + 2] = Data[q + (bgr ? 2 : 0)];
                bgra[q + 3] = Data[q + 3];
            }
        }
        else
        {
            var format = Format switch
            {
                131 or 132 => CompressionFormat.Bc1,
                133 or 134 => CompressionFormat.Bc1WithAlpha,
                137 or 138 => CompressionFormat.Bc3,
                139 => CompressionFormat.Bc4,
                141 => CompressionFormat.Bc5,
                145 or 146 => CompressionFormat.Bc7,
                _ => throw new NotSupportedException($"Unsupported texture format {Format}")
            };
            var decoder = new BcDecoder();
            decoder.Options.IsParallel = false;
            var pixels = decoder.DecodeRawAsync(Data, format, Info.Width, Info.Height, token).GetAwaiter().GetResult();
            for (int i = 0; i < count; i++)
            {
                int q = i * 4;
                bgra[q] = pixels[i].b;
                bgra[q + 1] = pixels[i].g;
                bgra[q + 2] = pixels[i].r;
                bgra[q + 3] = pixels[i].a;
            }
        }
        token.ThrowIfCancellationRequested();
        return new(Info, bgra);
    }
}
