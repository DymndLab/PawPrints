using Blueprint.Rendering;

static class CompanyLogoChecks
{

    public static void Run(Action<bool, string> check, string output, bool portable = false)
    {
        // A StringBuilder field cannot be marshalled inside OPENFILENAMEW.
        // Verify the real declaration before a user ever opens the native dialog.
        int openFileSize = System.Runtime.InteropServices.Marshal.SizeOf<Blueprint.LogoPicker.OpenFile>();
        check(openFileSize == (IntPtr.Size == 8 ? 152 : 88), "Logo dialog structure marshals with the native Windows layout");
        string dir = Path.Combine(output, "company-logo");
        Directory.CreateDirectory(dir);
        check(CompanyLogo.UsesThemeInk([0, 0, 0, 255, 0, 0, 0, 1, 40, 80, 120, 0]) && CompanyLogo.UsesThemeInk([255, 255, 255, 255, 255, 255, 255, 100]), "Single black or white RGB uses theme ink regardless of alpha; transparent RGB is ignored");
        check(!CompanyLogo.UsesThemeInk([0, 0, 0, 255, 1, 0, 0, 1]) && !CompanyLogo.UsesThemeInk([0, 0, 0, 255, 255, 255, 255, 255]) && !CompanyLogo.UsesThemeInk([255, 254, 255, 255]) && !CompanyLogo.UsesThemeInk([20, 70, 100, 0]), "Even one faint second RGB code preserves original colours; coloured or empty logos are not recoloured");
        foreach (int theme in Enumerable.Range(0, 4))
            foreach (byte ink in new byte[] { 0, 255 })
            {
                var colors = SheetLayout.Colors(theme);
                var paper = new Canvas(1, 1, colors.Paper, colors.Ink, new(Theme: theme, Grid: false));
                var prior = paper.Pixels.ToArray();
                CompanyLogo.Draw(paper, new(1, 1, [ink, ink, ink, 128]), new(0, 0, 1, 1));
                check(Math.Abs(paper.Pixels[0] - (colors.Ink.X * 128 + prior[0] * 127) / 255) <= 1 && Math.Abs(paper.Pixels[1] - (colors.Ink.Y * 128 + prior[1] * 127) / 255) <= 1 && Math.Abs(paper.Pixels[2] - (colors.Ink.Z * 128 + prior[2] * 127) / 255) <= 1, $"Theme {theme}, source {ink}: monochrome logo adopts text ink with original alpha");
            }
        var dual = new Canvas(2, 1, new(20, 40, 60), new(100, 150, 200), new(Grid: false));
        CompanyLogo.Draw(dual, new(2, 1, [0, 0, 0, 255, 255, 255, 255, 255]), new(0, 0, 2, 1));
        check(dual.Pixels.SequenceEqual(new byte[] { 0, 0, 0, 255, 255, 255 }), "Two-colour black/white logo remains black/white instead of taking theme ink");
        var original = Enumerable.Repeat((byte)255, 2048 * 4).ToArray();
        original[^4] = 254;
        original[^1] = 1;
        string path = Path.Combine(dir, "two-colour-before-resize.png");
        File.WriteAllBytes(path, PngFixture.Rgba(2048, 1, original));
        var decoded = CompanyLogo.Load(path);
        check(decoded.Width == 1024 && decoded.UseThemeInk == false, "Decode classifies a faint second source colour before downsampling can erase it");
        Array.Fill(original, (byte)255);
        File.WriteAllBytes(path, PngFixture.Rgba(2048, 1, original));
        check(CompanyLogo.Load(path).UseThemeInk == true, "Decoded single-white PNG retains theme classification after resizing");
        if (!portable)
            ImageDecodingChecks.Run(check);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<TitleBlockData>("{\"Creator\":\"Test\"}")!;
        check(legacy.InitialsStyle == InitialsFont.Face && InitialsFont.Normalize("missing") == InitialsFont.Face, "Older title settings and unknown styles use Reenie Beanie");
        check(legacy.ApproverInitialsStyle == InitialsFont.ApproverFace && InitialsFont.Normalize("missing", InitialsFont.ApproverFace) == "Zeyada", "Missing or invalid approver style defaults independently to Zeyada");
        var separate = new TitleBlockData(CreatorInitials: "Yn", InitialsStyle: "Kristi", ApproverInitialsStyle: "Mr Dafoe");
        var separateRead = System.Text.Json.JsonSerializer.Deserialize<TitleBlockData>(System.Text.Json.JsonSerializer.Serialize(separate))!;
        var independent = new Canvas(2400, 3000, new(255, 255, 255), new(0, 0, 0), new PrintOptions(Grid: false));
        CompanyTitleBlock.DrawCompact(independent, "Test", new PrintOptions(TitleBlock: separateRead));
        check(independent.Runs.Any(r => r.Text == "Yn" && r.Face == "Kristi") && independent.Runs.Any(r => r.Text == TitleBlockData.DefaultApproverInitials && r.Face == "Mr Dafoe"), "Creator and approver selections persist and render independently");
        foreach (string face in InitialsFont.Names)
        {
            var cell = new Box(170, 105, 100, 38);
            var c = new Canvas(440, 260, new(255, 255, 255), new(0, 0, 0), new(Grid: false));
            InitialsFont.Draw(c, "Yn", cell, face, 100);
            NativeText.Apply(c.Pixels, 440, 260, c.Runs);
            check(Enumerable.Range(0, 440 * 260).All(i => c.Pixels[i * 3] >= 245 || cell.Contains(new(i % 440, i / 440))),
                face + " at 100 percent fits the initials cell");
            var enlarged = new Canvas(440, 260, new(255, 255, 255), new(0, 0, 0), new(Grid: false));
            InitialsFont.Draw(enlarged, "Yn", cell, face, 300);
            NativeText.Apply(enlarged.Pixels, 440, 260, enlarged.Runs);
            check(Enumerable.Range(0, 440 * 260).Any(i => enlarged.Pixels[i * 3] < 245 && !cell.Contains(new(i % 440, i / 440))),
                face + " at 300 percent paints outside the initials cell");
            var saved = new TitleBlockData(CreatorInitials: "Yn", InitialsStyle: face);
            var restored = System.Text.Json.JsonSerializer.Deserialize<TitleBlockData>(System.Text.Json.JsonSerializer.Serialize(saved))!;
            var sample = new Canvas(400, 100, new(255, 255, 255), new(0, 0, 0), new PrintOptions(Grid: false));
            InitialsFont.Draw(sample, "Yn", new(10, 10, 300, 46), restored.InitialsStyle);
            NativeText.Apply(sample.Pixels, 400, 100, sample.Runs);
            var ys = Enumerable.Range(0, 100).Where(y => Enumerable.Range(0, 400).Any(x => sample.Pixels[(y * 400 + x) * 3] < 200)).ToArray();
            check(restored.InitialsStyle == face && sample.Runs.Single().Text == "Yn" && sample.Runs.Single().Face == face && ys.Length > 35 && ys.First() >= 10 && ys.Last() < 56, face + ": bundled face, saved choice, case and near-full ink height survive rendering");
        }
        var initialsSamples = InitialsFont.Names.Select(face => InitialsFont.SampleInk("XYZ", face)).ToArray();
        check(initialsSamples.All(sample => sample.Length == InitialsFont.SampleWidth * InitialsFont.SampleHeight && sample.Any(ink => ink > 150)) &&
            initialsSamples.Select(Convert.ToHexString).Distinct().Count() == InitialsFont.Names.Length,
            "All six selectable handwriting previews show distinct rendered glyphs");
        check(!initialsSamples[0].SequenceEqual(InitialsFont.SampleInk("ABC", InitialsFont.Names[0])),
            "The handwriting preview renders the entered initials");
        var narrowSamples = InitialsFont.Names.Select(face => InitialsFont.SampleInk("tas", face, 46, 28)).ToArray();
        check(narrowSamples.All(sample => sample.Length == 46 * 28 && sample.Any(ink => ink > 128) &&
            sample.Any(ink => ink is > 0 and < 128)) &&
            narrowSamples.Select(Convert.ToHexString).Distinct().Count() == InitialsFont.Names.Length,
            "Small lowercase samples preserve each bundled face with antialiased edges");
        var pixels = new byte[400 * 240 * 4];
        for (int y = 25; y < 215; y++)
            for (int x = 28; x < 372; x++)
                if (Math.Abs(x - 100) < 5 || Math.Abs(y - 120) < 5 || x > 210 && y > 70 && y < 170)
                {
                    int i = (y * 400 + x) * 4;
                    pixels[i] = 53;
                    pixels[i + 1] = 132;
                    pixels[i + 2] = 176;
                    pixels[i + 3] = 255;
                }
        string png = Path.Combine(dir, "EXAMPLELOGO.png");
        File.WriteAllBytes(png, PngFixture.Rgba(400, 240, pixels));
        var a = CompanyLogo.Load(png);
        check(a.Width == 400 && a.Height == 240 && a.Rgba.SequenceEqual(pixels), "Windows PNG decoding preserves exact RGBA values");
        check(a.Rgba[3] == 0 && a.Rgba.Where((_, i) => i % 4 == 3).Any(v => v == 255), "Logo transparency and opaque marks survive decoding");
        var wide = CompanyLogo.Fit(a, new(10, 20, 300, 100));
        check(wide.Height == 100 && Math.Abs(wide.Width / wide.Height - 400f / 240) < .001 && wide.X >= 10 && wide.Right <= 310, "Logo fitting preserves aspect ratio and contains the entire image");
        var imported = CompanyLogo.Import(png, Path.Combine(dir, "library"));
        var duplicate = CompanyLogo.Import(png, Path.Combine(dir, "library"));
        check(imported.Path == duplicate.Path && File.ReadAllBytes(imported.Path).SequenceEqual(File.ReadAllBytes(png)), "Import keeps exact original bytes and deduplicates without overwriting");
        string bad = Path.Combine(dir, "bad.png");
        File.WriteAllText(bad, "not an image");
        try
        {
            CompanyLogo.Load(bad);
            throw new Exception("Corrupt logo accepted");
        }
        catch (InvalidDataException) { check(true, "Corrupt logo reports a load failure"); }
        string hugePath = Path.Combine(dir, "oversized.png");
        File.WriteAllBytes(hugePath, PngFixture.Rgba(8193, 1, new byte[8193 * 4]));
        try
        {
            CompanyLogo.Load(hugePath);
            throw new Exception("Oversized logo accepted");
        }
        catch (InvalidDataException) { check(true, "Logo dimensions are bounded before full decoding"); }
        string large = Path.Combine(dir, "too-large.png");
        using (var f = File.Create(large))
            f.SetLength(CompanyLogo.MaxFileBytes + 1);
        try
        {
            CompanyLogo.Load(large);
            throw new Exception("Oversized file accepted");
        }
        catch (InvalidDataException) { check(true, "Logo file size is bounded before reading"); }
        var black = new Canvas(20, 20, new(10, 20, 30), new(255, 255, 255), new(Grid: false));
        var before = (byte[])black.Pixels.Clone();
        CompanyLogo.Draw(black, new(1, 1, [200, 100, 50, 0]), new(0, 0, 20, 20));
        check(black.Pixels.SequenceEqual(before), "Transparent logo pixels preserve the paper");
        CompanyLogo.Draw(black, new(1, 1, [200, 100, 50, 128]), new(0, 0, 20, 20));
        check(Math.Abs(black.Pixels[0] - (200 * 128f + before[0] * 127) / 255) <= 1 && Math.Abs(black.Pixels[1] - (100 * 128f + before[1] * 127) / 255) <= 1, "Semitransparent logo pixels blend onto the selected paper");
        var title = new TitleBlockData("Example Aerospace Lab", "Your name", "YN", "2026-11-12", ApprovalDate: "2026-11-12", Logo: a);
        foreach (int theme in new[] { 0, 1, 2, 3 })
        {
            var options = new PrintOptions(Theme: theme, TitleBlock: title);
            var colors = SheetLayout.Colors(theme);
            var c = new Canvas(2400, 3000, colors.Paper, colors.Ink, options);
            PageLayout.Decorate(c, "Lander 1", options);
            NativeText.Apply(c.Pixels, 2400, 3000, c.Runs);
            string name = new[] { "blue", "white", "cream", "charcoal" }[theme];
            var crop = new byte[2400 * 720 * 3];
            Array.Copy(c.Pixels, (3000 - 760) * 2400 * 3, crop, 0, crop.Length);
            string target = Path.Combine(dir, "title-" + name + "-" + Guid.NewGuid().ToString("N") + ".png");
            Png.Write(target, 2400, 720, crop);
            File.Copy(target, Path.Combine(dir, "title-" + name + ".png"), true);
            check(c.Runs.Any(r => r.Text == "EXAMPLE AEROSPACE LAB") && c.Runs.Any(r => r.Text == TitleBlockData.DefaultApproverInitials && r.Face == InitialsFont.ApproverFace) && string.Concat(c.Runs.Where(r => r.Y == CompanyTitleBlock.CreditTop(options)).Select(r => r.Text)).Contains("N' DESIGN"), name + ": organization, cursive initials and selected footer appear together");
            var sheet = new Sheet("Lander 1", options, c.Pixels, Sheet.MakePreview(c.Pixels), []);
            // Use a distinct date so the assertion cannot match the existing date row.
            string paintedDate = c.Runs.Single(r => r.Text.StartsWith("EXPORTED ")).Text[9..];
            string nextDate = DateOnly.ParseExact(paintedDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var updated = CompanyTitleBlock.StampExportDate(sheet, nextDate);
            check(!updated.SequenceEqual(sheet.Rgb) && Enumerable.Range(0, updated.Length).All(i => updated[i] == sheet.Rgb[i] || CompanyTitleBlock.DateArea(options).Contains(new(i / 3 % 2400, i / 3 / 2400))), name + ": export date changes without repainting craft or annotations above the date cell");
        }
        check(PrintIdentity.CreditSpans(2).Where(s => s.Outline).Select(s => s.Text).SequenceEqual(new[] { "Dy", "M", "N", "D" }), "Selected N-apostrophe footer keeps apostrophe filled");
    }
}
