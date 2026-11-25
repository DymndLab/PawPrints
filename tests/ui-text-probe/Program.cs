using Blueprint;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using System.Runtime.InteropServices;

// A headless UI context exercises the installed native library. It neither
// starts KSA nor attaches to its process; no game/profile files are changed.
string game = Path.GetFullPath(args[0]);
NativeLibrary.SetDllImportResolver(typeof(ImGui).Assembly, (name, assembly, path) =>
    name.Contains("imgui", StringComparison.OrdinalIgnoreCase)
        ? NativeLibrary.Load(Path.Combine(game, "imgui.dll")) : IntPtr.Zero);

var context = ImGui.CreateContext(default);
try
{
    var io = ImGui.GetIO();
    io.IniFilename = default;
    io.DisplaySize = new float2(1600, 1000);
    io.DeltaTime = 1f / 60;
    io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
    io.Fonts.AddFontDefault();
    ImGui.NewFrame();
    ImGui.SetNextWindowPos(new float2(10, 10));
    ImGui.SetNextWindowSize(new float2(1500, 950));
    ImGui.Begin("Text regression"u8);
    string[] cases =
    [
        "Print at 100% without stretching.", "100%", "%", "%%", "%s", "%n", "%q",
        "Tank 20% / path 50% complete", "Pressure 100% \u2014 \u65e5\u672c\u8a9e"
    ];
    foreach (string text in cases)
    {
        // Explicit diagnostic mode only: the original native call is expected
        // to terminate THIS process with 0xc0000409 on the first fixture.
        if (args.Contains("--unsafe"))
            ImGui.TextWrapped(text);
        else
            PrintUiText.Wrapped(text);
        var actual = ImGui.GetItemRectSize();
        var expected = ImGui.CalcTextSize(text);
        if (Math.Abs(actual.X - expected.X) > 1 || Math.Abs(actual.Y - expected.Y) > 1)
            throw new Exception("Wrapped text no longer matches the literal input.");
        PrintUiText.Tooltip(text);
    }
    ImGui.End();
    ImGui.Render();
    Console.WriteLine($"PASS {cases.Length} native wrapped-text/tooltip cases, with exact literal text dimensions.");

    var focus = new PrintWorkspaceFocus();
    void FocusFrame(float2 size, bool otherFocus, bool expectedFocus)
    {
        ImGui.NewFrame();
        if (otherFocus)
            ImGui.SetNextWindowFocus();
        ImGui.Begin("Native editor panel"u8);
        ImGui.End();
        focus.Prepare(size);
        ImGui.Begin("Print workspace"u8);
        if (ImGui.IsWindowFocused() != expectedFocus)
            throw new Exception("Print workspace focus routing failed.");
        ImGui.End();
        ImGui.Render();
    }
    FocusFrame(new float2(1440, 1000), true, true);
    FocusFrame(new float2(1440, 1000), true, true);
    FocusFrame(new float2(1440, 1000), true, false);
    FocusFrame(new float2(3440, 1440), true, true);
    FocusFrame(new float2(1440, 1000), true, true);
    focus.Request();
    FocusFrame(new float2(1440, 1000), true, true);
    FocusFrame(new float2(1440, 1000), true, true);
    FocusFrame(new float2(1440, 1000), true, false);
    ImGui.NewFrame();
    ImGui.Begin("Print workspace"u8);
    ImGui.OpenPopup("Test dialog"u8);
    if (!ImGui.BeginPopup("Test dialog"u8))
        throw new Exception("Focus fixture popup did not open.");
    ImGui.EndPopup();
    ImGui.End();
    ImGui.Render();
    ImGui.NewFrame();
    focus.Request();
    focus.Prepare(new float2(1600, 1000));
    ImGui.Begin("Print workspace"u8);
    if (ImGui.IsWindowFocused())
        throw new Exception("Resize stole focus from an open popup.");
    if (!ImGui.BeginPopup("Test dialog"u8))
        throw new Exception("Resize dismissed the popup.");
    ImGui.CloseCurrentPopup();
    ImGui.EndPopup();
    ImGui.End();
    ImGui.Render();
    FocusFrame(new float2(1600, 1000), true, true);
    // Native settings/save windows are not popups and are submitted before the
    // editor. They must remain available even with a pending resize/reopen focus.
    foreach (var size in new[] { new float2(1600, 1000), new float2(3440, 1440), new float2(1440, 1000) })
    {
        ImGui.NewFrame();
        ImGui.SetNextWindowFocus();
        ImGui.Begin("Native settings window"u8);
        PrintUiText.Wrapped("Settings remain interactive");
        ImGui.End();
        if (focus.Prepare(size, nativeDialogOpen: true))
            throw new Exception("Print workspace covered a native settings/save window.");
        ImGui.Begin("Native settings window"u8);
        if (!ImGui.IsWindowFocused())
            throw new Exception("Print workspace stole native settings/save input focus.");
        ImGui.End();
        ImGui.Render();
    }
    FocusFrame(new float2(1440, 1000), true, true);
    FocusFrame(new float2(1440, 1000), true, true);
    FocusFrame(new float2(1440, 1000), true, false);
    Console.WriteLine("PASS native settings/save-window suspension across resize and bounded workspace restoration.");
    Console.WriteLine("PASS native workspace entry, resize/restore, reopen, popup deferral and non-stealing steady-state focus.");
}
finally
{
    ImGui.DestroyContext(context);
}

// Log output is bounded, flushed while running and omits exception messages.
string logs = Path.Combine(Path.GetTempPath(), "PawPrints-diagnostics-probe-" + Guid.NewGuid());
PrintDiagnostics.Start(logs, "test");
PrintDiagnostics.State("settings.tab", "Title Block");
PrintDiagnostics.State("settings.tab", "Title Block");
try { throw new InvalidOperationException("PRIVATE-TEXT-DO-NOT-LOG"); }
catch (Exception error) { PrintDiagnostics.Error("probe", error); }
string live;
using (var reader = new StreamReader(new FileStream(Path.Combine(logs, PrintDiagnostics.LogFileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
    live = reader.ReadToEnd();
if (live.Contains("PRIVATE-TEXT") || !live.Contains("InvalidOperationException")
    || live.Split("settings.tab=Title Block").Length != 2)
    throw new Exception("Diagnostic privacy, flushing or deduplication failed.");
for (int i = 0; i < 900; i++)
    PrintDiagnostics.Record("probe " + new string('x', 4000));
PrintDiagnostics.Stop();
var files = new DirectoryInfo(logs).GetFiles();
string LogName(int age) => age == 0 ? PrintDiagnostics.LogFileName : $"{PrintDiagnostics.LogFileName}.{age}";
var retained = Enumerable.Range(0, 6).Select(LogName).ToHashSet();
if (!retained.SetEquals(files.Select(f => f.Name)) || files.Any(f => f.Length > 530000))
    throw new Exception("Size rotation must retain the current log and five bounded history files.");

for (int i = 0; i < 7; i++)
{
    PrintDiagnostics.Start(logs, $"restart-{i}");
    PrintDiagnostics.Stop();
}
if (!retained.SetEquals(new DirectoryInfo(logs).EnumerateFiles().Select(f => f.Name)) ||
    Enumerable.Range(0, 6).Any(age =>
        !File.ReadLines(Path.Combine(logs, LogName(age))).First().Contains($"version=restart-{6 - age} ", StringComparison.Ordinal)))
    throw new Exception("Session rotation must retain the five most recent prior logs in order.");
// An unwritable destination must neither throw nor disrupt other UI work.
PrintDiagnostics.Start(Path.Combine(logs, PrintDiagnostics.LogFileName, "invalid"), "test");
PrintDiagnostics.Record("ignored-unwritable");
PrintDiagnostics.Stop();
Console.WriteLine("PASS local diagnostic flushing, privacy, deduplication, rotation and unwritable-path handling.");
