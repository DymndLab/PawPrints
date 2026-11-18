using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Blueprint;

// Native editor windows can reappear above the print when a viewport is resized.
// Restore focus on entry/resize and the following frame, since the native
// editor can restore its panels after the viewport transition. Never keep
// forcing focus in steady state or over a popup.
internal sealed class PrintWorkspaceFocus
{
    float2 size;
    int pending;

    public void Request() => pending = 2;

    public bool Prepare(float2 viewportSize, bool nativeDialogOpen = false)
    {
        // Settings and save/load are ordinary native windows, not ImGui popups.
        // KSA draws them before the editor. Yield the workspace until they close
        // so it cannot cover them or accept input intended for those windows.
        if (nativeDialogOpen)
        {
            Request();
            return false;
        }
        if (!size.Equals(viewportSize))
        {
            size = viewportSize;
            Request();
        }
        if (pending == 0 || ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel))
            return true;
        ImGui.SetNextWindowFocus();
        pending--;
        return true;
    }
}
