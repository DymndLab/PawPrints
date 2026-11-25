using System.Security.Cryptography;

namespace Blueprint.Rendering;

sealed record LogoImage(int Width, int Height, byte[] Rgba, bool? UseThemeInk = null);
sealed record TitleBlockData(string Company = "", string Creator = "", string CreatorInitials = "", string DrawingDate = "",
    string Approver = TitleBlockData.DefaultApprover, string ApproverInitials = TitleBlockData.DefaultApproverInitials, string ApprovalDate = "", int Paws = 4, LogoImage? Logo = null, string InitialsStyle = InitialsFont.Face, string ApproverInitialsStyle = InitialsFont.ApproverFace,
    string Title = "", string Subtitle = "GENERAL ARRANGEMENT", string Revision = "0", string Scale = "", int CreatorInitialsSize = 100, int ApproverInitialsSize = 92)
{
    public const string DefaultApprover = "Lou Brickant";
    public const string DefaultApproverInitials = "LOB";
}

static class CompanyLogo
{
    public const int MaxFileBytes = 16 * 1024 * 1024;


    public static bool Supported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png";


    public static bool UsesThemeInk(ReadOnlySpan<byte> rgba)
    {
        int first = -1;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0)
                continue;
            int color = (rgba[i] << 16) | (rgba[i + 1] << 8) | rgba[i + 2];
            if (color != 0 && color != 0xFFFFFF)
                return false;
            if (first >= 0 && color != first)
                return false;
            first = color;
        }
        return first >= 0;
    }


    public static LogoImage Load(string path)
        => Decode(Read(path));


    internal static byte[] Read(string path)
    {
        if (!Supported(path))
            throw new InvalidDataException("Choose a PNG logo.");
        using var file = File.OpenRead(path);
        if (file.Length > MaxFileBytes)
            throw new InvalidDataException("Logo must be 16 MB or smaller.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        return bytes;
    }


    internal static LogoImage Decode(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxFileBytes) throw new InvalidDataException("Logo must be 16 MB or smaller.");
#if PAWPRINTS_PORTABLE_RENDERING
        var decoded = PortableRendering.Active ? PortableRendering.DecodePng(bytes) : WindowsPngDecoder.Decode(bytes);
#else
        var decoded = WindowsPngDecoder.Decode(bytes);
#endif
        // Classify original RGB codes before resizing can erase a faint colour.
        bool themeInk = UsesThemeInk(decoded.Rgba);
        double fit = Math.Min(1, 1024d / Math.Max(decoded.Width, decoded.Height));
        int width = Math.Max(1, (int)Math.Round(decoded.Width * fit));
        int height = Math.Max(1, (int)Math.Round(decoded.Height * fit));
        return new(width, height, ImageResampling.ResizeRgba(decoded.Rgba,
            decoded.Width, decoded.Height, width, height), themeInk);
    }


    public static (string Path, LogoImage Image) Import(string path, string folder)
    {
        // Decode before admitting the file to the library. A duplicate content
        // hash reuses its existing file; the user's originals are never replaced.
        byte[] bytes = Read(path);
        var image = Decode(bytes);
        Directory.CreateDirectory(folder);
        string stem = PrintFiles.SafeStem(Path.GetFileNameWithoutExtension(path));
        string target = Path.Combine(folder, stem + "-" + Convert.ToHexString(SHA256.HashData(bytes))[..12] + Path.GetExtension(path).ToLowerInvariant());
        if (!File.Exists(target))
        {
            using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            file.Write(bytes);
        }
        return (target, image);
    }


    public static Box Fit(LogoImage image, Box area)
    {
        float scale = Math.Min(area.Width / image.Width, area.Height / image.Height);
        float w = image.Width * scale, h = image.Height * scale;
        return new(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }


    public static void Draw(Canvas canvas, LogoImage image, Box area)
    {
        bool themeInk = image.UseThemeInk ?? UsesThemeInk(image.Rgba);
        var fit = Fit(image, area);
        int x0 = (int)Math.Ceiling(fit.X), y0 = (int)Math.Ceiling(fit.Y), x1 = (int)Math.Floor(fit.Right), y1 = (int)Math.Floor(fit.Bottom);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                float sx = Math.Clamp((x + .5f - fit.X) * image.Width / fit.Width - .5f, 0, image.Width - 1), sy = Math.Clamp((y + .5f - fit.Y) * image.Height / fit.Height - .5f, 0, image.Height - 1);
                int ix = (int)sx, iy = (int)sy;
                float fx = sx - ix, fy = sy - iy, a = 0, r = 0, g = 0, b = 0;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        float weight = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy);
                        int q = (Math.Min(iy + j, image.Height - 1) * image.Width + Math.Min(ix + i, image.Width - 1)) * 4;
                        float alpha = image.Rgba[q + 3] / 255f * weight;
                        a += alpha;
                        r += image.Rgba[q] * alpha;
                        g += image.Rgba[q + 1] * alpha;
                        b += image.Rgba[q + 2] * alpha;
                    }
                if (a > 0)
                {
                    if (themeInk)
                        canvas.Ink(x, y, a);
                    else
                        canvas.Color(x, y, new(r / a, g / a, b / a), a);
                }
            }
    }
}
