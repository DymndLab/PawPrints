namespace Blueprint.Rendering;

// Evaluate the actual key event, not ImGui's delayed frame state. GLFW uses
// Shift=1, Control=2, Alt=4, Super=8; ignore CapsLock/NumLock for Ctrl+Alt+P.
static class ExportShortcut
{


    public static bool Matches(bool isP, bool isPress, int modifiers, bool editorOpen, bool blocked)
        => isP && isPress && editorOpen && !blocked && ((modifiers & 15) == 6);
}
