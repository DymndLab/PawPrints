using Blueprint.Storage;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using System.Diagnostics;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static ExportPreferences? exportPreferences;
    static Task<string?>? exportFolderTask;
    static bool exportSetupOpen;
    static string? exportFolderError;
    static readonly ImInputString exportFolderPath = new(4096);

    static void InitializeExportFolder()
    {
        exportPreferences = new(ProfileStorage.FromResolvedProfile(KSA.Constants.DocumentsFolderPath));
        exportFolder = null;
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
            SetExportFolder(chosen);
        }
        catch (Exception error)
        {
            exportFolderError = "Could not save the export folder. " + error.Message;
            exportSetupOpen = true;
        }
    }

    static void SetExportFolder(string folder)
    {
        exportFolder = exportPreferences!.Choose(folder);
        exportFolderError = null;
        exportSetupOpen = false;
        status = "Export folder saved.";
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
        if (!ImGui.IsPopupOpen(name))
        {
            exportFolderPath.SetValue((exportFolder ?? "").AsSpan());
            ImGui.OpenPopup(name);
        }
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
            if (OperatingSystem.IsLinux())
            {
                ImGui.InputText("Folder path"u8, exportFolderPath);
                if (PrintUiStyle.YellowButton("Use folder"))
                {
                    try
                    {
                        SetExportFolder(exportFolderPath.ToString());
                        ImGui.CloseCurrentPopup();
                    }
                    catch (Exception error) { exportFolderError = error.Message; }
                }
            }
            else if (PrintUiStyle.YellowButton("Choose folder..."))
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
    static void OpenDesktopPath(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }

        var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
        start.ArgumentList.Add(Path.GetFullPath(path));
        using var opener = Process.Start(start);
    }

    static void OpenExportFolder()
    {
        try
        {
            ExportPreferences.RequireExistingFolder(exportFolder!);
            OpenDesktopPath(exportFolder!);
        }
        catch (Exception error) { ReportFailure("Save failed: " + error.Message); }
    }

}
