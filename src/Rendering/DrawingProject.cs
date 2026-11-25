using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blueprint.Rendering;

sealed record ProjectLogo(string FileName, string Digest, bool? UseThemeInk);
sealed record DrawingProject(int Version, string Kind, string Title, SourceBinding? Source, DrawingContent Content, ProjectLogo? Logo, ProjectVehicleIdentity? Vehicle = null)
{
    public static DrawingProject Capture(Sheet sheet, AnnotationDocument document, string? logoName)
    {
        var content = document.Snapshot();
        var title = content.Options.TitleBlock;
        var logo = title?.Logo;
        var source = sheet.PoseSource?.Assembly.Topology?.Binding;
        if (sheet.PoseSource != null && source == null)
            throw new InvalidOperationException("Refresh the drawing to capture its vehicle identity before saving.");
        if (logo != null && string.IsNullOrEmpty(logoName))
            throw new InvalidOperationException("Select the drawing's logo from the profile library before saving.");
        return new(1, "pawprints.project", sheet.Title, source,
            content with { Options = content.Options with { Drawing = null, TitleBlock = title == null ? null : title with { Logo = null } } },
            logo == null ? null : new(logoName!, LogoDigest(logo), logo.UseThemeInk),
            sheet.PoseSource == null ? null : ProjectCraftIdentity.Capture(sheet.PoseSource.Assembly, document.Scene, content));
    }

    internal static string LogoDigest(LogoImage logo)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(logo.Width));
        hash.AppendData(BitConverter.GetBytes(logo.Height));
        hash.AppendData(logo.Rgba);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public Sheet Render(PoseSource? source, LogoImage? logo, KittenModel? kitten, CancellationToken token)
    {
        var content = Content;
        ProjectLabelAddress[]? labelAddresses = null;
        if (Source is { } binding && source?.Assembly.Topology?.Binding != binding)
        {
            if (source == null)
                throw new InvalidOperationException("Load the vehicle for this editable drawing first.");
            if (Vehicle == null)
                throw new InvalidOperationException("This early preview file needs its original capture. Re-save it with vehicle matching data from that capture.");
            (content, labelAddresses) = ProjectCraftIdentity.Rebind(content, Vehicle, binding, source);
        }
        if (Logo is { } asset && (logo == null || LogoDigest(logo) != asset.Digest))
            throw new InvalidOperationException("The drawing's logo is missing or changed in the profile library.");
        if (content.Kittens.Any(k => k.Kind == ScaleReferenceKind.Kitten) && kitten == null)
            throw new InvalidOperationException("The KSA kitten model required by the drawing is unavailable.");
        var options = content.Options with { Drawing = content.Drawing };
        options = options with { ProjectionAngle = options.DrawingAngle };
        if (Logo != null)
            options = options with { TitleBlock = options.TitleBlock! with { Logo = logo! with { UseThemeInk = Logo.UseThemeInk } } };
        return RenderContent(Title, content, options, Source == null ? null : source, kitten, labelAddresses, token);
    }

    internal static Sheet RenderContent(string title, DrawingContent content, PrintOptions options, PoseSource? source,
        KittenModel? kitten, ProjectLabelAddress[]? labelAddresses, CancellationToken token)
    {
        Sheet main;
        if (source == null)
            main = BlankDrawing.Create(title, options, token, composeWear: false);
        else
        {
            PartPoses.Validate(source!, content.Poses, content.Drawing!.Views.Select(v => v.Id).Concat(content.Details.Select(d => d.ViewId)));
            main = PosePainting.RenderMain(source!, title, options, [], content.Poses, token, composeAnnotations: false);
        }
        var drawing = content.Details.Length == 0 ? main
            : ExplodedPainting.Compose(main, main, content.Details, [], token, preserveDisplayedContent: false);
        var scene = (content with { Options = options, Details = drawing.Details }).ApplyTo(drawing.Scene!).WithKittens(content.Kittens, kitten);
        var labels = content.Labels;
        if (labelAddresses != null)
        {
            var partIds = scene.Parts.ToLookup(p => (p.NodeId, p.DetailOnly));
            int PartId(ProjectLabelAddress address)
            {
                var matches = partIds[((uint?)address.Part, address.DetailOnly)];
                return matches.Count() == 1 ? matches.Single().Id
                    : throw new InvalidOperationException("A saved label has no unique printable part on this vehicle.");
            }
            var targetIds = labelAddresses.ToDictionary(l => l.LabelPart, PartId);
            labels = labels.Select(l => l with { PartId = targetIds[l.PartId] }).ToArray();
        }
        foreach (var label in labels)
            if (!scene.Parts.Any(p => p.Id == label.PartId))
                throw new InvalidDataException("A saved annotation refers to a missing part.");
        return AnnotationPainting.Compose(drawing with { Options = options, Scene = scene }, labels, token);
    }
}

static class DrawingProjectCodec
{
    static readonly JsonSerializerOptions json = new()
    {
        IncludeFields = true, IgnoreReadOnlyProperties = true, MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Write(DrawingProject project)
    {
        Validate(project);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(project, json);
        RequireSize(bytes.Length);
        return bytes;
    }

    public static DrawingProject Read(byte[] bytes)
    {
        RequireSize(bytes.Length);
        try
        {
            var project = JsonSerializer.Deserialize<DrawingProject>(bytes, json);
            Validate(project);
            return project!;
        }
        catch (JsonException e) { throw new InvalidDataException("Invalid drawing project data.", e); }
    }

    public static void RequireSize(long length)
    {
        if (length is <= 0 or > DrawingSchemas.MaxBytes)
            throw new InvalidDataException("Drawing projects must be 2 MB or smaller.");
    }

    static void Validate(DrawingProject? p)
    {
        if (p is not { Version: 1, Kind: "pawprints.project", Content: { Options: { } o, Drawing: { } d } c } ||
            string.IsNullOrWhiteSpace(p.Title) || p.Title.Length > 256 ||
            o.ProjectionAngle is not (null or 1 or 3) || !Enum.IsDefined(o.OutputTarget) || !o.FreeLayout || o.Drawing != null || o.TitleBlock?.Logo != null)
            throw new InvalidDataException("Unsupported or incomplete drawing project.");
        _ = PageSize.Custom(o.OutputPage.Width, o.OutputPage.Height);
        _ = PageSize.Custom(o.Page.Width, o.Page.Height);
        if (p.Source is { } source && (source.Session == Guid.Empty || source.Revision < 1))
            throw new InvalidDataException("Invalid vehicle binding.");
        if (p.Logo is { } logo && (o.TitleBlock == null || string.IsNullOrEmpty(logo.FileName) ||
            logo.FileName.IndexOfAny(['/','\\',':']) >= 0 || !CompanyLogo.Supported(logo.FileName) ||
            logo.Digest is not { Length: 64 } || !logo.Digest.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Invalid profile logo reference.");
        if (o.TitleBlock is { } title && (!InitialsFont.Names.Contains(title.InitialsStyle) || !InitialsFont.Names.Contains(title.ApproverInitialsStyle)))
            throw new InvalidDataException("A saved initials font is unavailable.");
        if (!Entries(c.WearMarks, WearPlacement.Limit) || !c.WearMarks.All(WearPlacement.Valid))
            throw new InvalidDataException("Invalid placed wear marks.");
        if (c.Tables == null || d.Views == null || d.Views.Length > DrawingPlacements.Limit ||
            d.Layers is { Length: > 4096 } ||
            !Entries(c.Labels, AnnotationDocument.Limit) || !Entries(c.Details, ExplodedPainting.Limit) ||
            !Entries(c.FreeText, 4096) || !Entries(c.Captions, DrawingPlacements.Limit) ||
            !Entries(c.Kittens, 4) || !Entries(c.Specs, 4096) || !Entries(c.Poses, 100000) ||
            !Entries(c.Dimensions, DrawingPlacements.Limit) || d.Views.Any(v => v == null))
            throw new InvalidDataException("Invalid drawing project collections.");
        static bool Entries<T>(T[] values, int limit) => values != null && values.Length <= limit && values.All(v => v != null);
        if (p.Source == null && (d.Views.Length != 0 || c.Details.Length != 0 || c.Labels.Length != 0 || c.Poses.Length != 0 ||
            c.Dimensions.Length != 0 || c.Specs.Length != 0 || o.PartsTable || o.FlagTable))
            throw new InvalidDataException("A drawing with craft content needs a vehicle binding.");
        if (c.Details.Any(detail => detail.SourceIssue != DetailSourceIssue.None))
            throw new InvalidDataException("Resolve the affected exploded views before saving.");
        var ids = d.Views.Select(v => v.Id).Concat(c.Details.Select(v => v.ViewId)).ToHashSet();
        if (ids.Count != d.Views.Length + c.Details.Length || c.Labels.Any(l => !ids.Contains(l.ViewId)) ||
            c.Labels.Select(l => l.Id).Distinct().Count() != c.Labels.Length ||
            d.Views.Any(v => v.Id is < 0 or >= 1000 || v.Camera is < 0 or > 7 ||
                v.Alignment is < 0 or > 2 || v.RollDirection is < -1 or > 7 || v.ProjectionAngle is not (1 or 3) ||
                v.Ratio is { } ratio && !ValidRatio(ratio) || !float.IsFinite(v.X + v.Y)) ||
            !ValidRatio(d.Ratio) || c.Details.Any(v => v.Id < 0 || v.Id > ExplodedPainting.Limit ||
                v.View is < 0 or > 7 || v.Caption == null || v.Sizing is { } sizing && !ValidRatio(sizing.Ratio)))
            throw new InvalidDataException("Invalid drawing view or annotation identities.");
        if (p.Vehicle is { } vehicle)
        {
            static bool Key(string s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);
            if (p.Source == null || !Key(vehicle.Fingerprint) || !Entries(vehicle.Parts, 100000) ||
                !Entries(vehicle.Labels, AnnotationDocument.Limit) ||
                vehicle.Parts.Any(part => string.IsNullOrEmpty(part.Template) || !Key(part.Key)) ||
                vehicle.Parts.Select(part => part.Part).Distinct().Count() != vehicle.Parts.Length ||
                vehicle.Labels.Select(label => label.LabelPart).Distinct().Count() != vehicle.Labels.Length ||
                !vehicle.Labels.Select(label => label.LabelPart).ToHashSet().SetEquals(c.Labels.Select(label => label.PartId)) ||
                !vehicle.Parts.Select(part => part.Part).ToHashSet().SetEquals(ProjectCraftIdentity.References(c, vehicle.Labels)))
                throw new InvalidDataException("Invalid saved vehicle identity.");
        }
        static bool ValidRatio(float ratio) => float.IsFinite(ratio) && ratio is >= .01f and <= 1000000;
        var views = d.Views.ToDictionary(v => v.Id);
        foreach (var view in d.Views)
        {
            var current = view;
            int remaining = views.Count;
            while (current.Parent is { } parent)
                if (--remaining < 0 || !views.TryGetValue(parent, out current))
                    throw new InvalidDataException("Invalid projection links.");
        }
        if (c.FreeText.Length > FreeTextPainting.Limit || c.FreeText.Any(t =>
            t.Text is not { Length: > 0 and <= FreeTextPainting.TextLimit } || t.FontSize is < 12 or > 144))
            throw new InvalidDataException("Invalid drawing text.");
        foreach (var box in c.FreeText.Select(t => t.Bounds).Concat(c.Details.Select(v => v.Bounds))
                     .Concat(c.Specs.Select(s => s.Bounds)).Concat(new[] { c.Tables.Parts, c.Tables.Flagnotes }.OfType<Box>()))
            CanvasBounds.Require(box);
    }
}
