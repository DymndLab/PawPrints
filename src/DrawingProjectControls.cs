using Blueprint.Rendering;
using Blueprint.Storage;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static Guid savedProjectDocument;
    static long savedProjectRevision;
    static string? savedProjectName, pendingProjectOpen;
    static string[]? projectsToOpen;
    static string? projectToOpen;
    static bool DrawingHasUnsavedChanges => document != null &&
        (document.Id != savedProjectDocument || document.Revision != savedProjectRevision || !PrintIsCurrent);

    static ProfileStorage DrawingProfile() => ProfileStorage.FromResolvedProfile(KSA.Constants.DocumentsFolderPath);

    static void MarkProjectSaved(string name)
    {
        savedProjectDocument = document!.Id;
        savedProjectRevision = document.Revision;
        savedProjectName = name;
    }

    static bool SaveDrawingProject()
    {
        if (busy || sheet == null || document == null || !PrintIsCurrent || ExportRefreshBlock != null)
            return false;
        try
        {
            var logo = document.Scene.Options.TitleBlock?.Logo;
            string? file = logo != null && knownLogoNames.TryGetValue(logo, out var known) ? known : null;
            var project = DrawingProject.Capture(sheet, document, file);
            var name = new DrawingProjectStore(DrawingProfile()).Save(project,
                savedProjectDocument == document.Id ? savedProjectName : null);
            MarkProjectSaved(name);
            AddActivity("project.saved", "Editable drawing saved.", notify: true);
            return true;
        }
        catch (Exception e) { ReportFailure("Could not save drawing: " + e.Message); return false; }
    }

    static void RequestOpenDrawing()
    {
        projectToOpen = null;
        try { projectsToOpen = new DrawingProjectStore(DrawingProfile()).List(); }
        catch (Exception e) { ReportFailure("Could not list drawings: " + e.Message); }
    }

    static void OpenDrawingProject(VehicleEditor editor, string name)
    {
        try
        {
            var profile = DrawingProfile();
            var project = new DrawingProjectStore(profile).Read(name);
            var revision = ObserveCraft(editor);
            PoseSource? source = null;
            CapturedCraft? capture = null;
            if (project.Source is { } binding)
            {
                if (acceptedCraft == revision && ReferenceEquals(sourceEditor, editor))
                    source = sheet?.PoseSource;
                if (source?.Assembly.Topology?.Binding != binding && project.Vehicle == null)
                    throw new InvalidOperationException("This early preview file needs its original capture. Re-save it with vehicle matching data from that capture.");
                if (source == null)
                {
                    if (revision is not { PartCount: > 0 })
                        throw new InvalidOperationException("Load the vehicle for this editable drawing first.");
                    capture = CraftCapture.TakeForDrawing(editor, project.Content.Options.Page, sheet, basePrint);
                    if (ObserveCraft(editor) != revision)
                        throw new InvalidOperationException("The vehicle changed during capture. Open the drawing again.");
                }
            }
            string? logoPath = project.Logo is { } logo ? System.IO.Path.Combine(profile.Logos, logo.FileName) : null;
            var model = sheet?.Scene?.KittenModel;
            string? kittenPath = model == null && project.Content.Kittens.Any(k => k.Kind == ScaleReferenceKind.Kitten)
                ? ModLibrary.Get<Gltf2Reference>("KittenGlb").Source?.Value?.ModPath : null;
            renderKeepsDocument = false;
            renderAdoptsHistory = true;
            progress = "Opening editable drawing";
            sourceEditor = editor;
            sourceCraft = editor.EditingSpace.Id;
            observedCraft = revision;
            capturedCraft = capture == null ? null : revision;
            StartRender(token =>
            {
                var detached = source;
                if (capture != null)
                {
                    var maps = new Dictionary<string, Texture>();
                    foreach (var entry in capture.Textures)
                    {
                        token.ThrowIfCancellationRequested();
                        maps.Add(entry.Key, entry.Value.Decode(token));
                    }
                    detached = new(capture.Assembly!, capture.Poses, capture.Geometry.Meshes.Length,
                        CraftCapture.LabelParts(capture), maps, capture.Performance) { DetachedGeometryBytes = capture.DetachedGeometryBytes };
                }
                var image = logoPath == null ? null : CompanyLogo.Load(logoPath);
                var kitten = model ?? (kittenPath == null ? null : KittenModel.Load(kittenPath));
                return project.Render(detached, image, kitten, token);
            });
            pendingProjectOpen = name;
            pendingProjectLogo = project.Logo?.FileName;
        }
        catch (Exception e) { capturedCraft = null; ReportFailure("Could not open drawing: " + e.Message); }
    }

    static string? pendingProjectLogo;

    static void DrawOpenDrawing(VehicleEditor editor)
    {
        const string popup = "Open editable drawing";
        if (projectsToOpen == null) return;
        if (!ImGui.IsPopupOpen(popup)) ImGui.OpenPopup(popup);
        CenterDrawingDialog();
        ImGui.SetNextWindowSize(new float2(Math.Min(590, ImGui.GetWindowSize().X - 32), 0), ImGuiCond.Always);
        if (!ImGui.BeginPopupModal(popup, ImGuiWindowFlags.AlwaysAutoResize)) return;
        try
        {
            annotationKeysActive = false;
            if (DrawingHasUnsavedChanges)
            {
                PrintUiText.Wrapped("The current drawing has unsaved changes. Opening another drawing replaces it after loading succeeds.");
                ImGui.BeginDisabled(!PrintIsCurrent || ExportRefreshBlock != null);
                if (ImGui.Button("Save current drawing")) SaveDrawingProject();
                ImGui.EndDisabled();
            }
            if (projectsToOpen.Length == 0) PrintUiText.Wrapped("No editable drawings saved in this profile.");
            ImGui.BeginChild("Saved drawings", new float2(0, 200));
            try
            {
                foreach (string name in projectsToOpen)
                    if (ImGui.Selectable(name, projectToOpen == name)) projectToOpen = name;
            }
            finally { ImGui.EndChild(); }
            ImGui.BeginDisabled(projectToOpen == null);
            if (PrintUiStyle.PrimaryButton(DrawingHasUnsavedChanges ? "Open without saving" : "Open drawing") && projectToOpen is { } selected)
            {
                OpenDrawingProject(editor, selected);
                projectsToOpen = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                projectsToOpen = null;
                ImGui.CloseCurrentPopup();
            }
        }
        finally { ImGui.EndPopup(); }
    }
}
