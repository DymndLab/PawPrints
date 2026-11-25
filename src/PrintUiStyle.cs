using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace Blueprint;

static class PrintUiStyle
{


    public static void PushTabs()
    {
        ImGui.PushStyleColor(ImGuiCol.Tab, new float4(.10f, .17f, .25f, 1));
        ImGui.PushStyleColor(ImGuiCol.TabHovered, new float4(.21f, .38f, .57f, 1));
        ImGui.PushStyleColor(ImGuiCol.TabSelected, new float4(.16f, .29f, .45f, 1));
        ImGui.PushStyleColor(ImGuiCol.TabSelectedOverline, new float4(.35f, .56f, .78f, 1));
    }


    public static void PopTabs() => ImGui.PopStyleColor(4);


    public static bool YellowButton(string text, float2 size = default)
    {
        // Reuse KSA's warning/launch yellow, scoped to the current action.
        ImGui.PushStyleColor(ImGuiCol.Button, ConsoleStyle.Pending);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ConsoleStyle.Pending);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ConsoleStyle.Pending);
        ImGui.PushStyleColor(ImGuiCol.Text, ConsoleStyle.Panel);
        try
        {
            return ImGui.Button(text, size);
        }
        finally { ImGui.PopStyleColor(4); }
    }

    public static void SectionHeader(string text)
    {
        ImGui.Text(text);
        var start = ImGui.GetItemRectMin();
        var end = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        uint color = ImGui.GetColorU32(ImGuiCol.Text);
        // Add local weight without replacing or modifying KSA's shared font.
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), start + new float2(.6f, 0), color, text);
        draw.AddLine(new float2(start.X, end.Y + 1), new float2(end.X, end.Y + 1), color, 1);
        ImGui.Dummy(new float2(0, 3));
    }

    public static bool PrimaryButton(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new float4(.16f, .29f, .45f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new float4(.21f, .38f, .57f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new float4(.12f, .23f, .36f, 1));
        try
        {
            return ImGui.Button(text);
        }
        finally { ImGui.PopStyleColor(3); }
    }


    // Scope the blue navigation-button style to PawPrints without changing game widgets.
    public const string ReturnToVehicleLabel = "Return to Vehicle Editor";

    public static bool ReturnToVehicleButton(bool warning)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new float4(.16f, .29f, .45f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new float4(.21f, .38f, .57f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new float4(.12f, .23f, .36f, 1));
        ImGui.PushStyleColor(ImGuiCol.Text, warning ? ConsoleStyle.Pending : ConsoleStyle.TextPrimary);
        try
        {
            bool clicked = ImGui.Button(ReturnToVehicleLabel);
            if (warning && ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                try
                {
                    ImGui.Text("Returns to the Vehicle Editor and cancels the unfinished annotation."u8);
                }
                finally { ImGui.EndTooltip(); }
            }
            return clicked;
        }
        finally { ImGui.PopStyleColor(4); }
    }


    public static void ShowUnavailableCursor(bool disabled)
    {
        if (disabled && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetMouseCursor(ImGuiMouseCursor.NotAllowed);
    }

    public static void Help(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28);
        try
        {
            PrintUiText.Wrapped(text);
        }
        finally
        {
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    public static void Warning(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ConsoleStyle.Pending);
        try
        {
            PrintUiText.Wrapped(text);
        }
        finally { ImGui.PopStyleColor(); }
    }
}
