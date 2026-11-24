using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blueprint.Rendering;

enum DrawingOutput { Image, DesktopBackground }
enum DrawingObjectKind { View, Label, Text, Dimension, Performance, ScaleReference, PartsTable, FlagTable }
readonly record struct DrawingObject(DrawingObjectKind Kind, int Id);
sealed record OutputDefinition(DrawingOutput Target, int Width, int Height, int CanvasWidth, int CanvasHeight);
sealed record AssetReference(string Sha256, int Width, int Height);
sealed record AppearanceDefinition(int Theme, bool Grid, int Emphasis, int Wear, bool Zones,
    bool TitleBlockVisible, TitleBlockData? Title, AssetReference? Logo);
sealed record ScopeDefinition(uint? Root, ImmutableArray<JointKey> Cuts, ImmutableArray<uint> Removed,
    ImmutableArray<uint>? VisibleParts = null);
sealed record ViewDefinition(int Id, int Camera, Box Bounds, float Ratio, int? Parent = null,
    int? Group = null, bool Exploded = false, ViewAxes? Axes = null, ScopeDefinition? Scope = null,
    bool NeedsScopeBinding = false);
sealed record ProjectionGroup(int Id, int Primary, ImmutableArray<int> Members, float Ratio);
sealed record DrawingLayout(ImmutableArray<ViewDefinition> Views, ImmutableArray<ProjectionGroup> Groups,
    ImmutableArray<DrawingObject> Layers);
sealed record PartBinding(int LabelPart, uint RuntimePart, string Template);
sealed record DrawingDefinition(Guid Id, OutputDefinition Output, AppearanceDefinition Appearance,
    DrawingLayout Layout, SourceBinding? Source, ImmutableArray<PartLabel> Labels,
    ImmutableArray<FreeTextBox> Text, ImmutableArray<HeightDimension> Dimensions,
    ImmutableArray<SpecBlock> Performance, ImmutableArray<PartPose> Poses, ImmutableArray<PartBinding> Parts);

// Transient selection/gestures are deliberately absent from the saved definition.
sealed record DrawingSelection(DrawingObject? Target = null);
sealed record DrawingGesture(RenderRevision BaseRevision, DrawingObject Target, Box Preview);
sealed record DrawingNotice(string Code, string Message);
sealed class DrawingNotices
{
    readonly Queue<DrawingNotice> entries = new();
    public IReadOnlyList<DrawingNotice> Entries => entries.ToArray();
    public void Add(DrawingNotice notice)
    {
        if (entries.LastOrDefault() == notice) return;
        entries.Enqueue(notice);
        while (entries.Count > 64) entries.Dequeue();
    }
}

static class DrawingDefinitions
{
    static readonly ConditionalWeakTable<LogoImage, AssetReference> assets = new();

    // Adapter over the accepted legacy renderer. No geometry/raster is copied;
    // per-group edits and applying these definitions arrive in S02/S03.
    public static DrawingDefinition FromAccepted(Sheet sheet, AnnotationDocument document)
    {
        var content = document.Snapshot();
        var drawing = content.Drawing ?? DrawingPlacements.FromScene(document.Scene);
        var views = new List<ViewDefinition>();
        var groups = new List<ProjectionGroup>();
        var byId = drawing.Views.ToDictionary(v => v.Id);
        int Root(DrawingView view)
        {
            var seen = new HashSet<int>();
            while (view.Parent is { } parent && byId.TryGetValue(parent, out var next))
            {
                if (!seen.Add(view.Id)) throw new InvalidDataException("Cyclic projection links.");
                view = next;
            }
            return view.Id;
        }
        var roots = drawing.Views.ToDictionary(v => v.Id, Root);
        foreach (var members in drawing.Views.GroupBy(v => roots[v.Id]))
            if (members.Count() > 1)
                groups.Add(new(members.Key, members.Key, members.Select(v => v.Id).ToImmutableArray(), ProjectionGroups.Ratio(drawing, members.Key)));
        foreach (var view in drawing.Views)
        {
            var bounds = document.Scene.Views.FirstOrDefault(v => v.Id == view.Id)?.Bounds ?? new Box(view.X, view.Y, 0, 0);
            views.Add(new(view.Id, view.Camera, bounds, ProjectionGroups.Ratio(drawing, view.Id), view.Parent,
                groups.Any(g => g.Id == roots[view.Id]) ? roots[view.Id] : null, Axes: view.Axes));
        }
        foreach (var detail in content.Details)
            views.Add(new(detail.ViewId, detail.View, detail.Bounds, detail.Sizing?.Ratio ?? drawing.Ratio,
                Exploded: true, Scope: new(detail.Seed ?? detail.Scope, [], [])));
        var layers = views.Select(v => new DrawingObject(DrawingObjectKind.View, v.Id))
            .Concat(content.Labels.Select(l => new DrawingObject(DrawingObjectKind.Label, l.Id)))
            .Concat(content.FreeText.Select(t => new DrawingObject(DrawingObjectKind.Text, t.Id)))
            .Concat(content.Dimensions.Select(d => new DrawingObject(DrawingObjectKind.Dimension, d.ViewId)))
            .Concat(content.Specs.Select(s => new DrawingObject(DrawingObjectKind.Performance, s.Id)))
            .Concat(content.Kittens.Select(k => new DrawingObject(DrawingObjectKind.ScaleReference, k.Id))).ToList();
        if (content.Options.PartsTable) layers.Add(new(DrawingObjectKind.PartsTable, 0));
        if (content.Options.FlagTable) layers.Add(new(DrawingObjectKind.FlagTable, 0));
        var options = content.Options;
        var logo = options.TitleBlock?.Logo;
        var asset = logo == null ? null : assets.GetValue(logo, image =>
            new(Convert.ToHexString(SHA256.HashData(image.Rgba)), image.Width, image.Height));
        return new(document.Id, new(options.OutputTarget, options.OutputPage.Width, options.OutputPage.Height, options.Page.Width, options.Page.Height),
            new(options.Theme, options.Grid, options.Emphasis, options.Archive ? options.WearLevel : 0,
                options.Zones, true, options.TitleBlock is { } title ? title with { Logo = null } : null, asset),
            new(views.ToImmutableArray(), groups.ToImmutableArray(), CanvasObjects.All(document.Scene, content.Labels).ToImmutableArray()),
            sheet.PoseSource?.Assembly.Topology?.Binding, content.Labels.ToImmutableArray(),
            content.FreeText.ToImmutableArray(), content.Dimensions.ToImmutableArray(),
            content.Specs.ToImmutableArray(), content.Poses.ToImmutableArray(),
            document.Scene.Parts.Where(p => p.NodeId.HasValue)
                .Select(p => new PartBinding(p.Id, p.NodeId!.Value, p.TemplateId)).ToImmutableArray());
    }

    public static LayoutTemplate ToTemplate(DrawingDefinition drawing)
    {
        // Template positions are relative to the output. No source IDs, labels,
        // appearance, asset/identity or output settings enter this schema.
        var views = drawing.Layout.Views.Select(v => v with
        {
            Bounds = new(v.Bounds.X / drawing.Output.CanvasWidth, v.Bounds.Y / drawing.Output.CanvasHeight,
                v.Bounds.Width / drawing.Output.CanvasWidth, v.Bounds.Height / drawing.Output.CanvasHeight),
            NeedsScopeBinding = v.Scope != null, Scope = null
        }).ToImmutableArray();
        return new(1, "pawprints.layout", new(views, drawing.Layout.Groups,
            drawing.Layout.Layers.Where(l => l.Kind == DrawingObjectKind.View).ToImmutableArray()));
    }
}

// Internal schema drafts, not user Save/Open. Full migration of legacy detail
// parameters, captions, scale references and missing assets is an S07 gate.
sealed record DrawingProjectDraft(int Version, string Kind, DrawingDefinition Drawing);
sealed record LayoutTemplate(int Version, string Kind, DrawingLayout Layout);
static class DrawingSchemas
{
    public const int MaxBytes = 2 * 1024 * 1024;
    static readonly JsonSerializerOptions json = new()
    {
        IncludeFields = true, IgnoreReadOnlyProperties = true, MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] WriteDrawing(DrawingDefinition drawing)
    {
        Validate(drawing);
        return Bounded(JsonSerializer.SerializeToUtf8Bytes(new DrawingProjectDraft(1, "pawprints.drawing-draft", drawing), json));
    }
    public static DrawingDefinition ReadDrawing(byte[] bytes)
    {
        var file = Read<DrawingProjectDraft>(bytes);
        if (file.Version != 1 || file.Kind != "pawprints.drawing-draft") throw new InvalidDataException("Unsupported drawing schema.");
        Validate(file.Drawing);
        return file.Drawing;
    }
    public static byte[] WriteTemplate(LayoutTemplate template)
    {
        ValidateTemplate(template);
        return Bounded(JsonSerializer.SerializeToUtf8Bytes(template, json));
    }
    public static LayoutTemplate ReadTemplate(byte[] bytes)
    {
        var template = Read<LayoutTemplate>(bytes);
        ValidateTemplate(template);
        return template;
    }
    static byte[] Bounded(byte[] bytes) => bytes.Length is > 0 and <= MaxBytes ? bytes
        : throw new InvalidDataException("Drawing definition exceeds its byte budget.");
    static T Read<T>(byte[] bytes)
    {
        try { return JsonSerializer.Deserialize<T>(Bounded(bytes), json) ?? throw new InvalidDataException("Empty definition."); }
        catch (JsonException e) { throw new InvalidDataException("Invalid definition format.", e); }
    }

    static void Validate(DrawingDefinition d)
    {
        if (d == null || d.Id == Guid.Empty || d.Output == null || !Enum.IsDefined(d.Output.Target) || d.Appearance == null)
            throw new InvalidDataException("Incomplete drawing definition.");
        _ = PageSize.Custom(d.Output.Width, d.Output.Height);
        _ = PageSize.Custom(d.Output.CanvasWidth, d.Output.CanvasHeight);
        if (d.Appearance.Title?.Logo != null) throw new InvalidDataException("Use an asset reference, not inline logo pixels.");
        if (d.Source is { } source && (source.Session == Guid.Empty || source.Revision < 1))
            throw new InvalidDataException("Invalid source binding.");
        if (d.Labels.IsDefault || d.Text.IsDefault || d.Dimensions.IsDefault || d.Performance.IsDefault || d.Poses.IsDefault || d.Parts.IsDefault ||
            d.Labels.Length > AnnotationDocument.Limit || d.Text.Length > 4096 || d.Dimensions.Length > 4096 || d.Performance.Length > 4096 || d.Poses.Length > 100000)
            throw new InvalidDataException("Invalid drawing content counts.");
        ValidateLayout(d.Layout, false);
    }
    static void ValidateTemplate(LayoutTemplate template)
    {
        if (template.Version != 1 || template.Kind != "pawprints.layout") throw new InvalidDataException("Unsupported template schema.");
        ValidateLayout(template.Layout, true);
    }
    static void ValidateLayout(DrawingLayout layout, bool template)
    {
        if (layout == null || layout.Views.IsDefault || layout.Groups.IsDefault || layout.Layers.IsDefault ||
            layout.Views.Length > 128 || layout.Groups.Length > 128 || layout.Layers.Length > 16384)
            throw new InvalidDataException("Invalid layout counts.");
        var views = new Dictionary<int, ViewDefinition>();
        foreach (var v in layout.Views)
        {
            if (v == null || !views.TryAdd(v.Id, v) || !float.IsFinite(v.Ratio) || v.Ratio <= 0 ||
                !float.IsFinite(v.Bounds.X) || !float.IsFinite(v.Bounds.Y) || !float.IsFinite(v.Bounds.Width) ||
                !float.IsFinite(v.Bounds.Height) || v.Bounds.Width < 0 || v.Bounds.Height < 0 || v.Camera is < 0 or > 7)
                throw new InvalidDataException("Invalid view definition.");
            if (template && v.Scope != null) throw new InvalidDataException("Templates cannot carry craft-bound scopes.");
            if (v.Scope is { } scope && (scope.Cuts.IsDefault || scope.Removed.IsDefault ||
                scope.Cuts.Length > 100000 || scope.Removed.Length > 100000 || scope.VisibleParts is { Length: > 100000 }))
                throw new InvalidDataException("Invalid scope definition.");
        }
        var groups = new HashSet<int>();
        var grouped = new HashSet<int>();
        foreach (var g in layout.Groups)
        {
            if (g == null || !groups.Add(g.Id) || g.Members.IsDefaultOrEmpty || !g.Members.Contains(g.Primary) ||
                !float.IsFinite(g.Ratio) || g.Ratio <= 0)
                throw new InvalidDataException("Invalid projection group.");
            foreach (int id in g.Members)
                if (!grouped.Add(id) || !views.TryGetValue(id, out var view) || view.Group != g.Id || view.Exploded || view.Ratio != g.Ratio)
                    throw new InvalidDataException("Inconsistent projection membership.");
        }
        foreach (var v in views.Values)
        {
            if (v.Group != null && !grouped.Contains(v.Id)) throw new InvalidDataException("Missing projection group.");
            var visited = new HashSet<int> { v.Id };
            var current = v;
            while (current.Parent is { } parent)
            {
                if (!visited.Add(parent) || !views.TryGetValue(parent, out var parentView) || parentView.Group != v.Group)
                    throw new InvalidDataException("Missing or cyclic projection parent.");
                current = parentView;
            }
        }
        if (layout.Layers.Distinct().Count() != layout.Layers.Length || layout.Layers.Any(l => !Enum.IsDefined(l.Kind) ||
            l.Kind == DrawingObjectKind.View && !views.ContainsKey(l.Id) || template && l.Kind != DrawingObjectKind.View))
            throw new InvalidDataException("Invalid layer order.");
    }
}
