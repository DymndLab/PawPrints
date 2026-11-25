using Blueprint.Storage;
using Blueprint.Rendering;
using System.Text.Json;

static class ProfileStorageChecks
{
    static void CheckTransfer(string root, Action<bool, string> check)
    {
        var from = new ProfileStorage(Path.Combine(root, "transfer-from"));
        var to = new ProfileStorage(Path.Combine(root, "transfer-to"));
        var preferences = new TitlePreferences(
            new(Company: "Orbit Paperworks", Creator: "Casey Example", CreatorInitials: "CE", Title: "Only this sheet"),
            "brand.png", true, false, 1, 1, true,
            new(DrawingOutput.Image, 1080, 1920, Theme: 2, Grid: false, Zones: false, Wear: 4),
            false, LeaderLine.Dashed, LeaderTip.OutlineTriangle);
        new TitlePreferenceStore(from).Save(preferences);
        var selected = PngFixture.Rgba(16, 16, Enumerable.Repeat(new byte[] { 22, 88, 176, 255 }, 256).SelectMany(p => p).ToArray());
        var different = PngFixture.Rgba(16, 16, Enumerable.Repeat(new byte[] { 188, 77, 33, 255 }, 256).SelectMany(p => p).ToArray());
        File.WriteAllBytes(Path.Combine(from.Logos, "brand.png"), selected);
        File.WriteAllBytes(Path.Combine(from.Logos, "extra.PNG"), different);
        var originalTemplate = StarterDrawings.All[0].Template;
        new LayoutTemplateStore(from).Save("My orthographic", originalTemplate);

        var original = preferences with { Data = new(Company: "Destination"), ProjectionAngle = 3 };
        new TitlePreferenceStore(to).Save(original);
        File.WriteAllBytes(Path.Combine(to.Logos, "brand.png"), different);
        new LayoutTemplateStore(to).Save("My orthographic", StarterDrawings.All[1].Template);
        string localFile = Path.Combine(to.Root, "PawPrints", "export-settings.json");
        File.WriteAllText(localFile, "local path");
        string drawing = Path.Combine(to.Root, "drawing.pawprint");
        File.WriteAllText(drawing, "existing drawing");
        var sourceSettings = new TitlePreferenceStore(from).Load()!;
        var transfer = new ProfileTransfer(from);
        string minimal = Path.Combine(root, "settings-only.pawprofile");
        transfer.Export(minimal, sourceSettings, extraLogos: false, templates: false);
        var small = ProfileTransfer.Read(minimal);
        check(small.Settings.Data.Title == "" && small.Logos.Length == 1 &&
            small.Logos[0].Png.SequenceEqual(selected) && small.Templates.Length == 0,
            "Settings-only export includes the selected logo and leaves drawing-specific data out");
        string complete = Path.Combine(root, "complete.pawprofile");
        transfer.Export(complete, sourceSettings, extraLogos: true, templates: true);
        var bundle = ProfileTransfer.Read(complete);
        check(bundle.Logos.Length == 2 && bundle.Templates.Length == 1 && bundle.Settings == sourceSettings,
            "Profile round trip retains paper, size, projection, persistent editor preferences and optional libraries");

        var destinationTransfer = new ProfileTransfer(to);
        var merged = destinationTransfer.Apply(bundle);
        check(merged.LogoFile == "brand (2).png" && File.ReadAllBytes(Path.Combine(to.Logos, merged.LogoFile)).SequenceEqual(selected) &&
            File.ReadAllBytes(Path.Combine(to.Logos, "brand.png")).SequenceEqual(different),
            "Profile import remaps a conflicting logo without changing the existing file");
        check(new LayoutTemplateStore(to).ReadAll().Select(t => t.Name).SequenceEqual(new[] { "My orthographic", "My orthographic (2)" }) &&
            new TitlePreferenceStore(to).Load() == merged && File.ReadAllText(localFile) == "local path" &&
            File.ReadAllText(drawing) == "existing drawing" && new TitlePreferenceStore(from).Load() == sourceSettings,
            "Import adds library entries, applies destination defaults, and preserves local settings, drawings and the source");
        check(destinationTransfer.Apply(bundle) == merged && new LayoutTemplateStore(to).ReadAll().Count == 2 &&
            Directory.GetFiles(to.Logos).Count(CompanyLogo.Supported) == 3, "Reimporting a profile reuses even previously renamed assets");

        string malformed = Path.Combine(root, "invalid.pawprofile");
        File.WriteAllBytes(malformed, JsonSerializer.SerializeToUtf8Bytes(bundle with
        {
            Logos = [new("../brand.png", selected)]
        }));
        bool rejected = false;
        try { ProfileTransfer.Read(malformed); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && new TitlePreferenceStore(to).Load() == merged, "A profile with an unsafe logo name cannot change saved defaults");
        File.WriteAllBytes(malformed, JsonSerializer.SerializeToUtf8Bytes(bundle with { Logos = [] }));
        rejected = false;
        try { ProfileTransfer.Read(malformed); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "A bundle referencing an omitted logo is invalid");

        var blocked = new ProfileStorage(Path.Combine(root, "transfer-blocked"));
        Directory.CreateDirectory(blocked.Preferences);
        rejected = false;
        try { new ProfileTransfer(blocked).Apply(bundle); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { rejected = true; }
        check(rejected && Directory.Exists(blocked.Preferences) &&
            !Directory.GetFiles(blocked.Logos).Any() && new LayoutTemplateStore(blocked).ReadAll().Count == 0,
            "A failed settings commit removes newly copied assets and keeps the destination intact");
    }

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "pawprints-storage-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        int passed = 0;
        void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
            passed++;
            Console.WriteLine("PASS " + message);
        }
        try
        {
            string mod = Path.Combine(root, "shared-mod");
            string legacy = Path.Combine(mod, "Logos");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "title-settings.json"), "legacy defaults");
            File.WriteAllText(Path.Combine(legacy, "company.png"), "original logo");
            File.WriteAllText(Path.Combine(legacy, "badge.WEBP"), "second logo");
            File.WriteAllText(Path.Combine(legacy, "private.txt"), "not a logo");
            var first = ProfileStorage.FromResolvedProfile(Path.Combine(root, "first profile"));
            var second = ProfileStorage.FromResolvedProfile(Path.Combine(root, "second profile"));
            Check(first.Root != second.Root && first.Logos != second.Logos, "Profiles have separate writable logo folders");
            Check(first.Exports == Path.Combine(first.Root, "Exports", "PawPrints"), "Export path follows resolved profile");
            Check(first.PrepareLogoLibrary(mod).Count == 0, "Legacy import succeeds");
            Check(File.ReadAllText(first.Preferences) == "legacy defaults", "Legacy title defaults copied");
            Check(File.Exists(Path.Combine(first.Logos, "company.png")) && File.Exists(Path.Combine(first.Logos, "badge.WEBP")), "Legacy logo originals are preserved case-insensitively");
            Check(!File.Exists(Path.Combine(first.Logos, "private.txt")), "Unrelated files are not copied");
            Check(File.Exists(Path.Combine(legacy, "company.png")), "Original logo remains in place");
            File.WriteAllText(first.Preferences, "new defaults");
            File.WriteAllText(Path.Combine(first.Logos, "company.png"), "new logo");
            first.PrepareLogoLibrary(mod);
            Check(File.ReadAllText(first.Preferences) == "new defaults", "Repeat import retains newer preferences");
            Check(File.ReadAllText(Path.Combine(first.Logos, "company.png")) == "new logo", "Repeat import retains newer logo");
            second.PrepareLogoLibrary(mod);
            Check(File.ReadAllText(second.Preferences) == "legacy defaults", "Another profile does not inherit the first profile edits");
            File.WriteAllText(Path.Combine(legacy, "title-settings.json"), new string('x', 65537));
            var third = ProfileStorage.FromResolvedProfile(Path.Combine(root, "third profile"));
            Check(third.PrepareLogoLibrary(mod).Count == 1 && !File.Exists(third.Preferences), "Oversized legacy settings warn without being imported");
            Check(File.Exists(Path.Combine(third.Logos, "company.png")), "One failed import does not block other logos");
            Directory.Delete(mod, recursive: true);
            Check(File.ReadAllText(first.Preferences) == "new defaults" && File.Exists(Path.Combine(first.Logos, "company.png")), "Replacing or removing the installed mod leaves user data intact");
            Check(first.PrepareLogoLibrary(mod).Count == 0, "Missing legacy folder is harmless");
            bool rejected = false;
            try
            {
                ProfileStorage.FromResolvedProfile(" ");
            }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Missing profile cannot silently become the working directory");
            var destinations = new ExportPreferences(first);
            Check(destinations.Load() == null, "First use has no implicitly selected export destination");
            string prints = Path.Combine(root, "Chosen prints with spaces");
            Directory.CreateDirectory(prints);
            Check(destinations.Choose(prints) == prints, "User can choose an existing export folder outside the profile");
            Check(new ExportPreferences(first).Load() == prints, "Export choice persists across new settings objects/sessions");
            Check(new ExportPreferences(second).Load() == null, "Export choice is isolated per profile");
            string oldSettings = File.ReadAllText(destinations.FilePath);
            string missing = Path.Combine(root, "Missing prints");
            rejected = false;
            try
            {
                destinations.Choose(missing);
            }
            catch (DirectoryNotFoundException) { rejected = true; }
            Check(rejected && !Directory.Exists(missing) && File.ReadAllText(destinations.FilePath) == oldSettings,
                "Missing selection is rejected without creating it or replacing the saved choice");
            rejected = false;
            try
            {
                destinations.Choose("relative-folder");
            }
            catch (ArgumentException) { rejected = true; }
            Check(rejected && destinations.Load() == prints, "Relative selection cannot change the saved destination");
            Directory.Delete(prints);
            Check(destinations.Load() == prints, "A disappeared destination remains saved for an explicit error");
            var blank = new Blueprint.Rendering.Sheet("folder-test", new(), [], [], []);
            rejected = false;
            try
            {
                Blueprint.Rendering.PrintFiles.Save(blank, prints, requireExistingFolder: true);
            }
            catch (DirectoryNotFoundException error) { rejected = error.Message.Contains(prints); }
            Check(rejected && !Directory.Exists(prints), "Runtime PNG save reports the missing path and does not recreate it");
            string replacement = Path.Combine(root, "Replacement prints");
            Directory.CreateDirectory(replacement);
            destinations.Choose(replacement);
            Check(destinations.Load() == replacement, "User can recover by selecting a replacement folder");
            var printable = blank with
            {
                Rgb = new byte[Blueprint.Rendering.Sheet.Width * Blueprint.Rendering.Sheet.Height * 3]
            };
            string firstPrint = Blueprint.Rendering.PrintFiles.Save(printable, replacement, requireExistingFolder: true);
            string nextPrint = Blueprint.Rendering.PrintFiles.Save(printable, replacement, requireExistingFolder: true);
            Check(Path.GetFileName(firstPrint).StartsWith("PawPrints-", StringComparison.Ordinal), "New PNGs use the PawPrints filename prefix");
            Check(firstPrint != nextPrint && File.Exists(firstPrint) && File.Exists(nextPrint),
                "Selected existing destination receives distinct PNGs without overwrites");
            Check(Blueprint.Rendering.Png.ReadOwn(firstPrint).Length == printable.Rgb.Length
                && !Directory.GetFiles(replacement, "*.partial").Any(),
                "Selected-folder PNG is readable and atomic save leaves no partial file");
            var prepared = Blueprint.Rendering.FinalPng.Prepare(printable, null, false, "2026-11-24", 0, default);
            using (var pending = prepared.Stage(replacement, "collision", default))
            {
                File.WriteAllText(pending.Target, "existing");
                try { pending.Publish(); throw new Exception("PNG overwrite accepted"); }
                catch (IOException) { }
                Check(File.ReadAllText(pending.Target) == "existing", "Final PNG cannot overwrite a file created before publication");
            }
            try { prepared.Stage(replacement, "cancel", new CancellationToken(true)); throw new Exception("Cancelled save completed"); }
            catch (OperationCanceledException) { }
            Check(!Directory.GetFiles(replacement, "*.partial").Any(), "Cancellation and publish collision clean their partial PNG");
            var smaller = prepared.Smaller(prepared.Encoded.Count / 2, default);
            Check(smaller.Size.Width < prepared.Size.Width && smaller.Encoded.Count <= prepared.Encoded.Count / 2 &&
                prepared.Size == printable.Options.OutputPage, "Smaller copy measures the encoded result and preserves original dimensions");
            Check(Blueprint.Rendering.FinalPng.Is4K(new(3840, 2160)) && Blueprint.Rendering.FinalPng.Is4K(new(2160, 3840)) &&
                !Blueprint.Rendering.FinalPng.Is4K(new(2880, 2880)), "Detailed export recognizes both 4K orientations");
            var portraitOptions = new Blueprint.Rendering.PrintOptions(Orientation: 3, CustomWidth: 2160, CustomHeight: 3840,
                Grid: false, Zones: false, TitleBlockVisible: false);
            var portrait = Blueprint.Rendering.BlankDrawing.Create("Portrait", portraitOptions, default);
            var project = Blueprint.Rendering.DrawingProject.Capture(portrait, new(portrait.Scene!), null);
            var opened = Blueprint.Rendering.DrawingProjectCodec.Read(Blueprint.Rendering.DrawingProjectCodec.Write(project)).Render(null, null, null, default);
            var portraitPng = Blueprint.Rendering.FinalPng.Prepare(opened, null, true, "2026-11-24", 0, default);
            using (var pending = portraitPng.Stage(replacement, "portrait", default))
            {
                string file = pending.Publish();
                Check(portraitPng.Size == new Blueprint.Rendering.PageSize(2160, 3840) &&
                    Blueprint.Rendering.Png.ReadOwn(file).AsSpan().SequenceEqual(portraitPng.Rgb) &&
                    new FileInfo(file).Length == portraitPng.Encoded.Count, "Reopened portrait project exports exact 4K preview pixels and measured bytes");
            }
            try { Blueprint.Rendering.FinalPng.Prepare(printable, null, false, "", 2L << 30, default); throw new Exception("Export budget bypassed"); }
            catch (InvalidOperationException error) when (error.Message.Contains("limit")) { }
            Check(!Directory.GetFiles(Path.GetDirectoryName(destinations.FilePath)!, "*.tmp").Any(),
                "Saving folder preference leaves no temporary settings file");
            File.WriteAllText(destinations.FilePath, "{broken");
            rejected = false;
            try
            {
                destinations.Load();
            }
            catch (System.Text.Json.JsonException) { rejected = true; }
            Check(rejected, "Corrupt preferences report an error rather than choosing a fallback destination");
            var titleStore = new TitlePreferenceStore(first);
            var author = new Blueprint.Rendering.TitleBlockData(Company: "Example Flightworks", Creator: "Test Author",
                CreatorInitials: "TA", DrawingDate: "2026-01-01", Approver: "Temporary Approver", ApproverInitials: "XX",
                ApprovalDate: "2026-01-01", Title: "SESSION DRAWING", Revision: "12", InitialsStyle: "kristi",
                CreatorInitialsSize: 88);
            titleStore.Save(author, "company.png");
            var restored = new TitlePreferenceStore(first).Load()!;
            Check(!restored.DetailedExport, "Detailed export defaults off");
            titleStore.Save(author, "company.png", detailedExport: true);
            Check(new TitlePreferenceStore(first).Load()!.DetailedExport, "Detailed export preference survives a profile reload");
            Check(restored.Data.Creator == author.Creator && restored.Data.CreatorInitials == "TA",
                "Drawn-by identity survives a new preference-store session");
            Check(restored.Data.Company == author.Company && restored.LogoFile == "company.png"
                && File.ReadAllText(Path.Combine(first.Logos, restored.LogoFile)) == "new logo",
                "Company and selected logo survive sessions without rewriting logo artwork");
            Check(restored.Data.InitialsStyle == "kristi" && restored.Data.CreatorInitialsSize == 88,
                "Author handwriting and size persist with the initials");
            Check(restored.Data.Approver == TitleBlockData.DefaultApprover && restored.Data.ApproverInitials == TitleBlockData.DefaultApproverInitials,
                "Temporary approver edits cannot replace the next session defaults");
            Check(author.Approver == "Temporary Approver" && author.ApproverInitials == "XX",
                "Saving preferences leaves the current drawing approver editable and unchanged");
            Check(restored.Data.Title == "" && restored.Data.Revision == "0" && restored.Data.DrawingDate == "" && restored.Data.ApprovalDate == "",
                "Session title, revision and signing dates do not become cached identity defaults");
            File.WriteAllText(first.Preferences, System.Text.Json.JsonSerializer.Serialize(new TitlePreferences(author, "company.png")));
            Check(titleStore.Load() is { Data.Approver: TitleBlockData.DefaultApprover, Data.ApproverInitials: TitleBlockData.DefaultApproverInitials, Data.CreatorInitials: "TA", LogoFile: "company.png" },
                "Older title-settings files retain author/company values while resetting stored approver identity");
            titleStore.Save(author with
            {
                Creator = "",
                CreatorInitials = "",
                Company = ""
            }, "");
            Check(titleStore.Load() is { Data.Creator: "", Data.CreatorInitials: "", Data.Company: "", LogoFile: "" },
                "Clearing cached author and company values is retained on the next session");
            titleStore.Save(author, "../outside.png");
            Check(titleStore.Load()!.LogoFile == "", "Cached logo choice cannot escape the profile library");
            Check(!Directory.GetFiles(first.Logos, "*.tmp").Any(), "Atomic title preference saves leave no temporary files");
            titleStore.Save(author, "company.png", true, projectionAngle: 1);
            Check(new TitlePreferenceStore(first).Load() is { LivePreviewUpdates: true, ProjectionAngle: 1 },
                "Live preview and first-angle projection preferences persist across sessions");
            File.WriteAllText(first.Preferences, System.Text.Json.JsonSerializer.Serialize(new
            {
                Data = author,
                LogoFile = "company.png"
            }));
            Check(new TitlePreferenceStore(first).Load() is { LivePreviewUpdates: false, ProjectionAngle: 3 },
                "Existing profiles default to manual preview updates and third-angle projection");
            if (OperatingSystem.IsWindows()
#if PAWPRINTS_PORTABLE_RENDERING
                || PortableRendering.Active
#endif
            )
                CheckTransfer(root, Check);
            Console.WriteLine($"{passed} profile storage checks passed.");
        }
        finally
        {
            // This generated directory is the complete scope of this fixture.
            Directory.Delete(root, recursive: true);
        }
    }
}
