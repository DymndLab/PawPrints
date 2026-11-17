using Brutal.ImGuiApi;
using Brutal.Numerics;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static string? warningNotice;

    static bool FailureStatus(string text) => text.StartsWith("Preview failed:", StringComparison.Ordinal)
        || text.StartsWith("Invalid page size:", StringComparison.Ordinal)
        || text.StartsWith("Save failed:", StringComparison.Ordinal) || text.StartsWith("Export unavailable:", StringComparison.Ordinal);

    static void ReportFailure(string message)
    {
        PrintDiagnostics.Record("warning.shown");
        status = message;
        warningNotice = message;
    }

    static void DrawWarningDialog(bool nested = false)
    {
        if (warningNotice == null || !nested && (poseDialog || viewSizeDialog || textDraft != null || specDraft != null || detailScopeOpen || flagnotePlacement != null))
            return;
        if (!ImGui.IsPopupOpen("Paw Prints warning"u8))
            ImGui.OpenPopup("Paw Prints warning"u8);
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + ImGui.GetWindowSize() * .5f, ImGuiCond.Appearing, new float2(.5f, .5f));
        ImGui.SetNextWindowSize(new float2(520, 0), ImGuiCond.Always);
        if (!ImGui.BeginPopupModal("Paw Prints warning"u8, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        try
        {
            partMenuOpen = true;
            PrintUiStyle.Warning(warningNotice);
            if (sheet != null && (warningNotice.StartsWith("Preview failed:", StringComparison.Ordinal) || warningNotice.StartsWith("Invalid page size:", StringComparison.Ordinal)))
                PrintUiText.Wrapped("Your previous print has been kept.");
            if (warningNotice.StartsWith("Save failed:", StringComparison.Ordinal))
            {
                if (ImGui.Button("Choose export folder..."))
                {
                    warningNotice = null;
                    exportSetupOpen = true;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
            }
            if (PrintUiStyle.PrimaryButton("OK"))
            {
                warningNotice = null;
                ImGui.CloseCurrentPopup();
            }
        }
        finally { ImGui.EndPopup(); }
    }
}
