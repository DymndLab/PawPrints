using System.Numerics;
using System.Text;
using System.Text.Json;
using Blueprint.Rendering;

static class PartSupportChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        CheckSolarFaces(check);
        var stock = PartSupport.Bundled;
        foreach (var (template, expected) in new[]
        {
            ("CoreSolarPanelA_CellA", "solar"), ("CoreSolarPanelA_CellAFoil", "solar"),
            ("CoreElectricalA_Subpart_SolarPanelB_CellA", "solar"), ("CoreCapsule_Cloth", "foil"), ("CoreFoilA", "foil"),
            ("UnknownMod_Panel", "structure"), ("unknown_foil", "structure")
        })
            check(stock.Surface(template) == expected, "Stock surface behavior: " + template);
        check(File.Exists(Path.Combine(AppContext.BaseDirectory, "part-support.json")), "External bundled definitions accompany the test build");
        foreach (var definitions in new[] { stock, PartSupport.Load(AppContext.BaseDirectory) })
        {
            foreach (string template in new[] { "TesseraPanelA_Subpart_WallCells", "TesseraPanelA_Subpart_HalfCells" })
                check(definitions.Surface(template) == "solar", "Tessera cell subpart receives solar treatment: " + template);
            foreach (string template in new[]
            {
                "TesseraPanelA_Subpart_WallFrame", "TesseraPanelA_Subpart_HalfFrame",
                "TesseraPanelA_Subpart_WallRadiator", "TesseraPanelA_Subpart_HalfSkin",
                "TesseraStructA_Prefab_SolarWallPanelA", "OtherMod_Subpart_WallCells"
            })
                check(definitions.Surface(template) == "structure", "Cell rules leave other surfaces structural: " + template);
        }
        check(PartNames.Display("CoreServiceModuleA_Prefab_SetA2DoubleA", null) == "Service Module / Set A2 Double A", "Stock service-module name retains its category and variant");
        check(PartNames.Display("CoreFuelTankA_Prefab_LF3W6HC", null) == "Fuel Tank / LF3W6HC", "Stock fuel-tank name retains its category and variant");
        const string customJson = """
        {
          "version": 1,
          "labelCategories": ["Fuel Tank", "Service Module"],
          "rules": [
            {"templateContains":"Foil", "surface":"foil", "fallbackLabel":"Blanket"},
            {"templatePrefix":"ExampleMod_", "surface":"structure"},
            {"templateId":"ExampleMod_HeliosCell", "surface":"solar", "fallbackLabel":"Helios array"},
            {"templateId":"ExampleMod_FoilCase", "surface":"structure"},
            {"templateId":"ExampleMod_FoilSkin", "fallbackLabel":"Insulated skin"}
          ]
        }
        """;
        var custom = Parse(customJson);
        check(custom.Surface("ExampleMod_HeliosCell") == "solar", "Exact mod ID overrides an earlier prefix rule");
        check(custom.Surface("ExampleMod_FoilCase") == "structure", "An exact rule can opt out of foil pattern treatment");
        check(custom.Surface("ExampleMod_FoilSkin") == "foil", "Label-only exact rule inherits a matching surface pattern");
        check(custom.FallbackLabel("ExampleMod_FoilCase") == "Blanket", "Surface-only exact rule inherits a matching fallback label");
        check(custom.Surface("ExampleMod_FoilLoose") == "foil", "Overlapping patterns follow file order");
        check(custom.Surface("ExampleMod_Other") == "structure" && custom.Surface("Unrelated_Panel") == "structure", "Prefix rules and unmatched mod parts have ordinary geometry");
        check(PartNames.Display("ExampleMod_HeliosCell", null, custom) == "Helios array", "Mod definition supplies a missing display name");
        check(PartNames.Display("ExampleMod_HeliosCell", "ExampleMod_HeliosCell", custom) == "Helios array", "An ID used as the native display name still permits fallback");
        check(PartNames.Display("ExampleMod_HeliosCell", "Helios VII / Array", custom) == "Helios VII / Array", "Native author-provided display name wins over configured fallback");
        check(PartNames.Display("ExampleMod_OtherPart42", null, custom) == "Example Mod Other Part 42", "Unconfigured names retain mechanical readable formatting");
        check(Parse("\ufeff" + customJson).Surface("ExampleMod_HeliosCell") == "solar", "UTF-8 BOM from text editors is accepted");
        check(Parse("{\"version\":1,/* notes */\"rules\":[],}").Surface("Anything") == "structure", "Comments and trailing commas are supported");
        foreach (string invalid in new[]
        {
            "{", "[]", "{\"version\":2,\"rules\":[]}", "{\"version\":\"1\",\"rules\":[]}",
            "{\"version\":1,\"rules\":null}", "{\"version\":1,\"rules\":[],\"typo\":1}",
            "{\"version\":1,\"version\":1,\"rules\":[]}",
            Rules("{\"templateContains\":\"\",\"surface\":\"foil\"}"),
            Rules("{\"templateId\":\"a\",\"templatePrefix\":\"b\",\"surface\":\"foil\"}"),
            Rules("{\"templateId\":\"a\",\"surface\":\"glitter\"}"),
            Rules("{\"templateId\":\"a\",\"surafce\":\"foil\"}"),
            Rules("{\"templateId\":\"a\"}"),
            Rules("{\"templateId\":\"a\",\"fallbackLabel\":null}"),
            Rules("{\"templateId\":\"a\",\"fallbackLabel\":\"line\\nline\"}"),
            Rules("{\"templateId\":\"a\",\"surface\":\"foil\"},{\"templateId\":\"a\",\"surface\":\"solar\"}")
        })
            check(Rejects(() => Parse(invalid)), "Reject malformed or ambiguous support definition");
        check(Rejects(() => Parse(new string(' ', PartSupport.MaxBytes + 1))), "Support file byte budget is enforced");
        string excessive = JsonSerializer.Serialize(new
        {
            version = 1,
            rules = Enumerable.Range(0, 2049).Select(i => new { templateId = "part" + i, surface = "foil" })
        });
        check(Rejects(() => Parse(excessive)), "Support rule count is bounded");
        string longLabel = JsonSerializer.Serialize(new
        {
            version = 1,
            rules = new[] { new { templateId = "part", fallbackLabel = new string('x', 161) } }
        });
        check(Rejects(() => Parse(longLabel)), "Configured labels have bounded length");

        string folder = Path.Combine(output, "part-support-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "part-support.json");
        try
        {
            check(PartSupport.Load(folder).Surface("CoreFoilA") == "foil", "Missing external file uses the embedded stock definitions");
            File.WriteAllText(file, customJson);
            var first = PartSupport.Load(folder);
            File.WriteAllText(file, customJson.Replace("Helios array", "Updated array"));
            var second = PartSupport.Load(folder);
            check(second.FallbackLabel("ExampleMod_HeliosCell") == "Updated array", "Reload picks up edited definitions without recompilation");
            check(first.FallbackLabel("ExampleMod_HeliosCell") == "Helios array", "Existing capture keeps its immutable support snapshot");
            File.WriteAllText(file, "{");
            check(Rejects(() => PartSupport.Load(folder)), "Invalid external file rejects the new capture");
            check(second.FallbackLabel("ExampleMod_HeliosCell") == "Updated array", "Failed reload does not mutate an accepted support snapshot");
            File.WriteAllBytes(file, new byte[PartSupport.MaxBytes + 1]);
            check(Rejects(() => PartSupport.Load(folder)), "Oversized external files are rejected before parsing");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    static void CheckSolarFaces(Action<bool, string> check)
    {
        double[][] uv = [[0, 0], [1, 0], [1, 1], [0, 1]];
        var positions = new List<double[]>();
        var normals = new List<double[]>();
        foreach (int sign in new[] { -1, 1 })
            foreach (var corner in uv)
            {
                positions.Add([corner[0] - .5, corner[1] - .5, sign * .04]);
                normals.Add([0, 0, sign]);
            }
        var mesh = new Mesh("panel", positions.ToArray(), [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7],
            positions.ToArray(), normals.ToArray(), [.. uv, .. uv], "solar", null, null);
        int InteriorInk(Mesh m, int side)
        {
            // The renderer maps source +Z to drawing +Y.
            var frame = new ProjectionFrame(128, 128, 100, Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, side * Vector3.UnitY);
            var pixels = new BlueprintRenderer(new([m]), new Dictionary<string, Texture>(), "panel", 1, default, frame)
                .RenderView(_ => { });
            int marks = 0;
            for (int y = 32; y < 96; y++)
                for (int x = 32; x < 96; x++)
                    if (pixels.Ink[y * 128 + x] > 0)
                        marks++;
            return marks;
        }
        var powered = mesh with { SolarDirection = Vector3.UnitZ };
        check(InteriorInk(powered, 1) > 0, "Selected cell face receives solar ink");
        check(InteriorInk(powered, -1) == 0, "Backing occludes the hatched face without retaining its ink");
        check(InteriorInk(mesh with { SolarDirection = Vector3.UnitX }, 1) == 0, "Faces perpendicular to the module axis stay clear");
        check(SolarSketch.SingleSidedAxis(mesh) == null, "Two-sided panel without a module has no inferred front");
        check(SolarSketch.SingleSidedAxis(mesh with { Indices = [4, 5, 6, 4, 6, 7] }) == Vector3.UnitZ,
            "Single-sided decorative panel supplies its own facing direction");
    }

    static PartSupport Parse(string text) => PartSupport.Parse(Encoding.UTF8.GetBytes(text));
    static string Rules(string entries) => "{\"version\":1,\"rules\":[" + entries + "]}";
    static bool Rejects(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception error) when (error is InvalidDataException or JsonException) { return true; }
    }
}
