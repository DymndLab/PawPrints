using System.Diagnostics;
using Blueprint.Storage;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    enum ProfileAction { None, Import, Export }
    static ProfileAction profileAction;
    static string? profileMessage;
    static bool profileExtraLogos, profileTemplates;
    static bool profileDialogRequested;
    static readonly ImInputString profileFile = new(4096);
    static ProfileBundle? profileImport;
    static Task<(bool Exported, ProfileBundle? Import)>? profileTask;
    static NewDrawingDraft? profileTaskOwner;
    static ProfileAction profileTaskAction;

    static void ResetProfileTransfer()
    {
        profileAction = ProfileAction.None;
        profileDialogRequested = false;
        profileImport = null;
        profileTaskOwner = null;
        profileMessage = null;
    }

    static void OpenProfileDialog(ProfileAction action)
    {
        profileAction = action;
        profileDialogRequested = true;
        profileImport = null;
        profileMessage = null;
        profileFile.SetValue("".AsSpan());
        profileExtraLogos = profileTemplates = false;
    }

    static void DrawProfileDialogs(VehicleEditor editor)
    {
        const string id = "PawPrints profile transfer";
        if (profileAction == ProfileAction.None)
        {
            if (ImGui.BeginPopupModal(id, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            return;
        }
        if (profileDialogRequested)
        {
            ImGui.OpenPopup(id);
            profileDialogRequested = false;
        }
        CenterDrawingDialog();
        ImGui.SetNextWindowSize(new float2(Math.Min(540, ImGui.GetWindowSize().X - 28), 0), ImGuiCond.Always);
        if (!ImGui.BeginPopupModal(id, ImGuiWindowFlags.AlwaysAutoResize)) return;
        try
        {
            bool exporting = profileAction == ProfileAction.Export;
            var storage = ProfileStorage.FromResolvedProfile(KSA.Constants.DocumentsFolderPath);
            if (profileImport is { } bundle)
            {
                PrintUiText.Wrapped($"Import into {Path.GetFileName(Path.TrimEndingDirectorySeparator(storage.Root))}?");
                PrintUiText.Wrapped(storage.Root);
                var settings = bundle.Settings;
                var paper = settings.Drawing ?? NewDrawingDefaults.Initial;
                var title = settings.Data;
                PrintUiText.Wrapped($"Drawn by {title.Creator} · {title.Company}");
                PrintUiText.Wrapped($"{paper.Width} × {paper.Height} · {(settings.ProjectionAngle == 3 ? "Third-angle" : "First-angle")}");
                PrintUiText.Wrapped($"{bundle.Logos.Length} logos · {bundle.Templates.Length} layout templates");
                PrintUiText.Wrapped("Apply uses these settings for new drawings in this profile.");
                if (PrintUiStyle.PrimaryButton("Apply profile"))
                {
                    try
                    {
                        profileDefaults = new ProfileTransfer(storage).Apply(bundle);
                        RefreshLogos();
                        BeginDrawingDraft(editor);
                        profileMessage = "Profile imported.";
                        ImGui.CloseCurrentPopup();
                        return;
                    }
                    catch (Exception error) { profileMessage = "Could not import profile: " + error.Message; }
                }
                ImGui.SameLine();
            }
            else
            {
                ImGui.Text(exporting ? "Export profile" : "Import profile");
                ImGui.BeginDisabled(profileTask != null);
                if (exporting)
                {
                    ImGui.Checkbox("Include saved layout templates", ref profileTemplates);
                    ImGui.Checkbox("Include additional logos", ref profileExtraLogos);
                }
                if (OperatingSystem.IsWindows())
                {
                    if (ImGui.Button(exporting ? "Choose destination…" : "Choose profile file…"))
                        RunProfileTransfer(storage, LogoPicker.ChooseProfile(Process.GetCurrentProcess().MainWindowHandle, exporting));
                }
                else
                {
                    SetupInput("Profile file path", profileFile);
                    string path = profileFile.ToString();
                    ImGui.BeginDisabled(!Path.IsPathFullyQualified(path));
                    if (ImGui.Button(exporting ? "Export" : "Read profile"))
                        RunProfileTransfer(storage, Task.FromResult<string?>(path));
                    ImGui.EndDisabled();
                }
                ImGui.EndDisabled();
                if (profileTask != null) ImGui.Text(exporting ? "Exporting…" : "Reading profile…");
            }
            if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                ResetProfileTransfer();
                ImGui.CloseCurrentPopup();
            }
            if (profileMessage != null) PrintUiText.Wrapped(profileMessage);
        }
        finally { ImGui.EndPopup(); }
    }

    static void RunProfileTransfer(ProfileStorage storage, Task<string?> picker)
    {
        bool extraLogos = profileExtraLogos, templates = profileTemplates;
        var settings = profileDefaults;
        var action = profileTaskAction = profileAction;
        profileTaskOwner = newDrawing;
        profileMessage = null;
        profileTask = Task.Run<(bool, ProfileBundle?)>(async () =>
        {
            string? path = await picker;
            if (path == null) return default;
            if (action == ProfileAction.Import) return (false, ProfileTransfer.Read(path));
            if (!path.EndsWith(".pawprofile", StringComparison.OrdinalIgnoreCase)) path += ".pawprofile";
            new ProfileTransfer(storage).Export(path, settings, extraLogos, templates);
            return (true, null);
        });
    }

    static void PollProfileTransfer()
    {
        if (profileTask?.IsCompleted != true) return;
        var task = profileTask;
        profileTask = null;
        bool current = ReferenceEquals(profileTaskOwner, newDrawing) && profileTaskAction == profileAction;
        profileTaskOwner = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            if (!current) return;
            profileImport = result.Import;
            if (result.Exported) profileMessage = "Profile exported.";
        }
        catch (Exception error)
        {
            if (current) profileMessage = "Profile transfer failed: " + error.Message;
        }
    }
}
