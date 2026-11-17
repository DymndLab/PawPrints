using Blueprint.Storage;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using System.Diagnostics;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static ExportPreferences? exportPreferences;
    static Task<string?>? exportFolderTask;
    static bool exportSetupOpen, exportPrompted;
    static string? exportFolderError;

    static void InitializeExportFolder()
    {
        exportPreferences = new(ProfileStorage.FromResolvedProfile(KSA.Constants.DocumentsFolderPath));
        exportFolder = null;
        exportPrompted = false;
        exportSetupOpen = false;
        exportFolderError = null;
        try
        {
            exportFolder = exportPreferences.Load();
        }
        catch (Exception error)
        {
            exportFolderError = "Could not read your saved export folder. " + error.Message;
            Console.WriteLine("[Paw Prints] " + exportFolderError);
        }
    }

    static void ChooseExportFolder()
    {
        if (exportFolderTask != null)
            return;
        exportFolderError = null;
        try
        {
            exportFolderTask = ExportFolderPicker.Choose(Process.GetCurrentProcess().MainWindowHandle);
        }
        catch (Exception error) { exportFolderError = error.Message; }
    }

    static void PollExportFolder()
    {
        if (exportFolderTask?.IsCompleted != true)
            return;
        var completed = exportFolderTask;
        exportFolderTask = null;
        try
        {
            var chosen = completed.GetAwaiter().GetResult();
            if (chosen == null)
                return;
            exportFolder = exportPreferences!.Choose(chosen);
            exportFolderError = null;
            exportSetupOpen = false;
            status = "Export folder saved.";
        }
        catch (Exception error)
        {
            exportFolderError = "Could not save the export folder. " + error.Message;
            exportSetupOpen = true;
        }
    }

    static void DrawExportFolderDialog()
    {
        const string name = "Choose an export folder";
        if (!exportSetupOpen)
        {
            if (ImGui.BeginPopupModal(name, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            return;
        }
        if (warningNotice != null)
            return;
        if (!ImGui.IsPopupOpen(name))
            ImGui.OpenPopup(name);
        ImGui.SetNextWindowSize(new float2(520, 0), ImGuiCond.Always);
        if (!ImGui.BeginPopupModal(name, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        try
        {
            partMenuOpen = true;
            PrintUiText.Wrapped("Choose where to save your PNG prints. PawPrints will remember this folder.");
            if (exportFolder != null)
                PrintUiText.Wrapped(exportFolder);
            if (exportFolderError != null)
                PrintUiStyle.Warning(exportFolderError);
            ImGui.BeginDisabled(exportFolderTask != null);
            if (PrintUiStyle.YellowButton("Choose folder..."))
                ChooseExportFolder();
            ImGui.SameLine();
            if (ImGui.Button("Later"))
            {
                exportSetupOpen = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
        }
        finally { ImGui.EndPopup(); }
    }

    static void DrawExportFolderActions()
    {
        ImGui.BeginDisabled(ControlsLocked);
        if (PrintUiStyle.YellowButton(exportFolder == null ? "Choose export folder..." : "Change export folder..."))
            exportSetupOpen = true;
        ImGui.BeginDisabled(exportFolder == null);
        if (PrintUiStyle.YellowButton("View exported drawings"))
            OpenExportFolder();
        ImGui.EndDisabled();
        ImGui.EndDisabled();
    }
    static void OpenExportFolder()
    {
        try
        {
            ExportPreferences.RequireExistingFolder(exportFolder!);
            Process.Start(new ProcessStartInfo(exportFolder!) { UseShellExecute = true });
        }
        catch (Exception error) { ReportFailure("Save failed: " + error.Message); }
    }

}
