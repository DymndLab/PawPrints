using Blueprint.Storage;

static class ProfileStorageChecks
{
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
            Check(restored.Data.Creator == author.Creator && restored.Data.CreatorInitials == "TA",
                "Drawn-by identity survives a new preference-store session");
            Check(restored.Data.Company == author.Company && restored.LogoFile == "company.png"
                && File.ReadAllText(Path.Combine(first.Logos, restored.LogoFile)) == "new logo",
                "Company and selected logo survive sessions without rewriting logo artwork");
            Check(restored.Data.InitialsStyle == "kristi" && restored.Data.CreatorInitialsSize == 88,
                "Author handwriting and size persist with the initials");
            Check(restored.Data.Approver == "Dymnd H. K." && restored.Data.ApproverInitials == "DHK",
                "Temporary approver edits cannot replace the next session defaults");
            Check(author.Approver == "Temporary Approver" && author.ApproverInitials == "XX",
                "Saving preferences leaves the current drawing approver editable and unchanged");
            Check(restored.Data.Title == "" && restored.Data.Revision == "0" && restored.Data.DrawingDate == "" && restored.Data.ApprovalDate == "",
                "Session title, revision and signing dates do not become cached identity defaults");
            File.WriteAllText(first.Preferences, System.Text.Json.JsonSerializer.Serialize(new TitlePreferences(author, "company.png")));
            Check(titleStore.Load() is { Data.Approver: "Dymnd H. K.", Data.ApproverInitials: "DHK", Data.CreatorInitials: "TA", LogoFile: "company.png" },
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
            titleStore.Save(author, "company.png", true);
            Check(new TitlePreferenceStore(first).Load()!.LivePreviewUpdates, "Live preview preference persists across sessions");
            File.WriteAllText(first.Preferences, System.Text.Json.JsonSerializer.Serialize(new
            {
                Data = author,
                LogoFile = "company.png"
            }));
            Check(!new TitlePreferenceStore(first).Load()!.LivePreviewUpdates, "Existing profiles default to manual preview updates");
            Console.WriteLine($"{passed} profile storage checks passed.");
        }
        finally
        {
            // This generated directory is the complete scope of this fixture.
            Directory.Delete(root, recursive: true);
        }
    }
}
