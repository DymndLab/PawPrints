namespace Blueprint.Rendering;

sealed record FinalPng(PageSize Size, int Density, byte[] Rgb, ArraySegment<byte> Encoded)
{
    internal static bool Is4K(PageSize size) => size is (3840, 2160) or (2160, 3840);

    internal static FinalPng Prepare(Sheet accepted, DrawingContent? content, bool detailed, string date,
        long retained, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var size = accepted.Options.OutputPage;
        if (detailed && !Is4K(size)) throw new InvalidOperationException("Choose a 4K output for Detailed export.");
        if (accepted.Details.Any(d => d.SourceIssue != DetailSourceIssue.None))
            throw new InvalidOperationException("Resolve the affected exploded views before exporting.");
        bool redraw = detailed && accepted.PoseSource != null;
        long estimate = checked(24L * RenderMemory.RasterPixels(size.Width, size.Height) +
            (redraw ? RenderMemory.CandidatePixels(accepted.Options.Page) + RenderMemory.GeometryLimit :
                8L * accepted.PixelWidth * accepted.PixelHeight));
        if (RenderMemory.PeakOverBudget(estimate, retained) is { } over)
            throw new InvalidOperationException($"PNG export requires an estimated {over.Used:N0} bytes (limit {over.Limit:N0}). Reduce the output size or turn off Detailed export.");

        var sheet = accepted;
        if (redraw)
        {
            if (content == null) throw new InvalidOperationException("Generate the drawing before exporting.");
            var options = accepted.Options with { Drawing = content.Drawing, Detailed = true };
            sheet = DrawingProject.RenderContent(accepted.Title, content, options, accepted.PoseSource,
                accepted.Scene?.KittenModel, null, token);
        }
        RenderProgress.Report("Preparing final pixels");
        token.ThrowIfCancellationRequested();
        var rgb = ImageResampling.ResizeRgb(CompanyTitleBlock.StampExportDate(sheet, date, token),
            sheet.PixelWidth, sheet.PixelHeight, size.Width, size.Height, token);
        int density = (int)Math.Round(PrintScale.Density(sheet.Options) * (double)size.Width / sheet.PixelWidth);
        return Encode(size, rgb, density, token);
    }

    static FinalPng Encode(PageSize size, byte[] rgb, int density, CancellationToken token)
    {
        RenderProgress.Report("Encoding PNG");
        using var stream = new MemoryStream();
        Png.Encode(stream, size.Width, size.Height, rgb, density, token);
        if (!stream.TryGetBuffer(out var bytes)) throw new InvalidOperationException("PNG buffer unavailable.");
        return new(size, density, rgb, bytes);
    }

    internal FinalPng Smaller(long target, CancellationToken token, long retained = 0)
    {
        if (target < 1) throw new ArgumentOutOfRangeException(nameof(target));
        if (RenderMemory.PeakOverBudget(24L * Size.Width * Size.Height, retained) is { } over)
            throw new InvalidOperationException($"Smaller copy requires an estimated {over.Used:N0} bytes (limit {over.Limit:N0}).");
        var candidate = this;
        for (int i = 0; i < 4 && candidate.Encoded.Count > target; i++)
        {
            token.ThrowIfCancellationRequested();
            double ratio = Math.Min(.85, .9 * Math.Sqrt((double)target / candidate.Encoded.Count));
            int width = Math.Max(1, (int)(candidate.Size.Width * ratio));
            int height = Math.Max(1, (int)Math.Round((double)Size.Height * width / Size.Width));
            var rgb = ImageResampling.ResizeRgb(Rgb, Size.Width, Size.Height, width, height, token);
            candidate = Encode(new(width, height), rgb, Math.Max(1, (int)Math.Round((double)Density * width / Size.Width)), token);
        }
        return candidate;
    }

    internal PendingPng Stage(string folder, string title, CancellationToken token)
    {
        Blueprint.Storage.ExportPreferences.RequireExistingFolder(folder);
        string stem = $"PawPrints-{PrintFiles.SafeStem(title)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var pending = new PendingPng(Path.Combine(folder, stem + ".partial"), Path.Combine(folder, stem + ".png"));
        using var output = new FileStream(pending.Temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            var bytes = Encoded.AsSpan();
            while (!bytes.IsEmpty)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(bytes.Length, 64 * 1024);
                output.Write(bytes[..count]);
                bytes = bytes[count..];
            }
            output.Dispose();
            return pending;
        }
        catch { output.Dispose(); pending.Dispose(); throw; }
    }
}

sealed record PendingPng(string Temp, string Target) : IDisposable
{
    public string Publish()
    {
        File.Move(Temp, Target, false);
        return Target;
    }

    public void Dispose() => File.Delete(Temp);
}
