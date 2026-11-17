using Brutal.ImGuiApi;
using System.Runtime.CompilerServices;

namespace Blueprint;

// These bindings forward their sole string argument to native printf-style
// functions. Escape every percent sign, including those in user text, before
// crossing that boundary. ImGui.Text itself uses TextUnformatted and is safe.
static class PrintUiText
{
    internal static string LiteralFormat(string text) => text.Replace("%", "%%", StringComparison.Ordinal);

    public static void Wrapped(string text, [CallerMemberName] string caller = "")
    {
        if (text.Contains('%'))
            PrintDiagnostics.Once("ui.literal-percent.wrapped caller=" + caller);
        ImGui.TextWrapped(LiteralFormat(text));
    }

    public static void Tooltip(string text, [CallerMemberName] string caller = "")
    {
        if (text.Contains('%'))
            PrintDiagnostics.Once("ui.literal-percent.tooltip caller=" + caller);
        ImGui.SetTooltip(LiteralFormat(text));
    }
}
