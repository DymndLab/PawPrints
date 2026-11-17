using Blueprint.Rendering;

static class ImageDecodingChecks
{
    public static void Run(Action<bool, string> check)
    {
        void Decode(byte[] encoded, byte[] expected, string name)
            => check(WindowsPngDecoder.Decode(encoded).Rgba.SequenceEqual(expected), name);
        void Reject(byte[] encoded, string name)
        {
            try
            {
                WindowsPngDecoder.Decode(encoded);
            }
            catch (InvalidDataException) { check(true, name); return; }
            check(false, name);
        }
        Decode(PngFixture.Encode(2, 1, 8, 2, [0, 255, 0, 0, 0, 255, 0]),
            [255, 0, 0, 255, 0, 255, 0, 255], "RGB PNG gains opaque alpha");
        Decode(PngFixture.Encode(2, 1, 1, 3, [0, 64], [255, 0, 0, 0, 255, 0], [0, 128]),
            [255, 0, 0, 0, 0, 255, 0, 128], "Indexed PNG preserves palette transparency");
        Decode(PngFixture.Encode(2, 1, 16, 0, [0, 0, 0, 255, 255]),
            [0, 0, 0, 255, 255, 255, 255, 255], "16-bit grayscale PNG converts to RGBA");
        Decode(PngFixture.Encode(2, 1, 8, 4, [0, 40, 128, 120, 255]),
            [40, 40, 40, 128, 120, 120, 120, 255], "Grayscale PNG retains alpha");
        byte[] rgba = Enumerable.Range(0, 13 * 9 * 4).Select(i => (byte)(i % 256)).ToArray();
        byte[] interlaced = PngFixture.Rgba(13, 9, rgba, true);
        Decode(interlaced, rgba, "Adam7 interlaced PNG preserves every pixel");
        // Sub, Up, Average and Paeth filters, including reconstructed prior rows.
        Decode(PngFixture.Encode(2, 4, 8, 2,
            [1, 10, 20, 30, 30, 30, 30, 2, 0, 0, 0, 0, 0, 0,
             3, 5, 10, 15, 15, 15, 15, 4, 0, 0, 0, 0, 0, 0]),
            Enumerable.Range(0, 4).SelectMany(_ => new byte[] { 10, 20, 30, 255, 40, 50, 60, 255 }).ToArray(),
            "All PNG prediction filters reconstruct rows");
        Reject(interlaced[..^5], "Truncated PNG is rejected");
        var corrupt = (byte[])interlaced.Clone();
        corrupt[45] ^= 1;
        Reject(corrupt, "PNG checksum mismatch is rejected");
        Reject(PngFixture.Encode(1, 1, 8, 6, [0, 0, 0, 0, 255], animated: true), "Animated PNG is rejected");
        Reject(PngFixture.Encode(8192, 8192, 8, 6, []), "PNG pixel limit checked before native allocation");
        Reject(PngFixture.Encode(4, 4, 8, 6, [0]), "Incomplete decompressed pixels are rejected");
        Reject("RIFFxxxxWEBP"u8.ToArray(), "Other image formats disguised as PNG are rejected");
        check(!CompanyLogo.Supported("badge.webp") && CompanyLogo.Supported("badge.PNG"), "Logo picker accepts only PNG extensions");
        Exception? threadFailure = null;
        var sta = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < 30; i++)
                    if (!WindowsPngDecoder.Decode(interlaced).Rgba.SequenceEqual(rgba))
                        throw new Exception("STA decode changed pixels");
            }
            catch (Exception ex) { threadFailure = ex; }
        });
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("PNG logo checks require Windows.");
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();
        check(threadFailure == null, "PNG decoder balances COM ownership in an existing STA apartment");
        check(Enumerable.Range(0, 12).AsParallel().All(_ => WindowsPngDecoder.Decode(interlaced).Rgba.SequenceEqual(rgba)),
            "Concurrent worker PNG decoding uses independent COM objects");

        byte[] transparent = ImageResampling.ResizeRgba([255, 0, 0, 255, 0, 0, 255, 0], 2, 1, 1, 1);
        check(transparent.SequenceEqual(new byte[] { 255, 0, 0, 128 }), "Logo resampling cannot bleed hidden transparent colour into its edges");
        byte[] constant = Enumerable.Repeat((byte)137, 31 * 19 * 3).ToArray();
        check(ImageResampling.ResizeRgb(constant, 31, 19, 7, 5).All(v => v == 137), "Desktop downsampling preserves constant colour including edges");
        check(ImageResampling.ResizeRgb(constant, 31, 19, 63, 42).All(v => v == 137), "Desktop upsampling preserves constant colour including edges");
        check(ReferenceEquals(constant, ImageResampling.ResizeRgb(constant, 31, 19, 31, 19)), "Same-size export retains its original pixels");
        var stripes = Enumerable.Range(0, 64 * 16 * 3).Select(i => (byte)((i / 3 % 64 % 2) * 255)).ToArray();
        byte[] reduced = ImageResampling.ResizeRgb(stripes, 64, 16, 8, 2);
        check(reduced.All(v => v is >= 120 and <= 135), "Fine desktop lines are averaged when downsampling rather than disappearing");
    }
}
