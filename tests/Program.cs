// Default checks use synthetic data and run without KSA or private captures.
int checks = 0;
void Check(bool valid, string message)
{
    if (!valid)
        throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
    checks++;
}
string output = Path.GetFullPath("tests/output");
Directory.CreateDirectory(output);
var suites = new Dictionary<string, Action>
{
    ["groups"] = () => GroupChecks.Run(Check),
    ["foundation"] = () => FoundationChecks.Run(Check),
    ["startup"] = () => StartupChecks.Run(Check, output),
    ["capture-stats"] = () => CaptureStatsChecks.Run(Check),
    ["curvature"] = () => CurvatureChecks.Run(Check),
    ["part-support"] = () => PartSupportChecks.Run(Check, output),
    ["templates"] = () => TemplateChecks.Run(Check),
    ["input"] = () => InputChecks.Run(Check),
    ["live-preview"] = () => PreviewUpdateChecks.Run(Check),
    ["generation-recovery"] = () => GenerationRecoveryChecks.Run(Check),
    ["fuel-hoses"] = () => FuelHoseChecks.Run(Check, output),
    ["panels"] = () => PanelChecks.Run(Check, output),
    ["border-layout"] = () => BorderLayoutChecks.Run(Check, output),
    ["free-text"] = () => FreeTextChecks.Run(Check),
    ["drawing-placement"] = () => DrawingPlacementChecks.Run(Check, output),
    ["performance"] = () => PerformanceChecks.Run(Check, output),
    ["poses"] = () => PoseChecks.Run(Check),
    ["scale-references"] = () => ScaleReferenceChecks.Run(Check, output),
    ["exploded"] = () => ExplodedChecks.Run(Check, output),
    ["logos"] = () => CompanyLogoChecks.Run(Check, output),
    ["profile-storage"] = ProfileStorageChecks.Run
};
if (args.Length == 0)
{
    foreach (var suite in suites)
    {
        Console.WriteLine("SUITE " + suite.Key);
        suite.Value();
    }
}
else if (args.Length == 1 && suites.TryGetValue(args[0].TrimStart('-'), out var run))
    run();
#if PAWPRINTS_PORTABLE_RENDERING
else if (args.Length == 2 && args[0] == "--portable-rendering")
{
    Blueprint.Rendering.PortableRendering.Initialize(args[1]);
    ImageDecodingChecks.Run(Check, Blueprint.Rendering.PortableRendering.DecodePng);
    CompanyLogoChecks.Run(Check, output, portable: true);
    StartupChecks.Run(Check, output);
    GroupChecks.Run(Check);
    ProfileStorageChecks.Run();
}
#endif
else if (args.Length == 1 && args[0] == "--wear-review")
    WearReview.Write(output);
else if (args.Length == 2 && args[0] == "--fixtures")
{
    string root = Path.GetFullPath(args[1]);
    if (!File.Exists(Path.Combine(root, "materials/lander-1.materials.json")))
        throw new FileNotFoundException("Fixture suite requires local material captures. See DEVELOPMENT.md.");
    FixtureChecks.Run(Check, root, output);
}
else if (args.Length == 2 && args[0] == "--kitten-references")
    KittenChecks.Run(Check, output, Path.GetFullPath(args[1]));
else if (args.Length is 2 or 3 && args[0] == "--native-spec-route")
    PerformanceChecks.NativeRoute(args[1], args.Contains("--expect-occluded"));
else
{
    Console.Error.WriteLine("Usage: Blueprint.Tests [--suite | --fixtures capture-root | --kitten-references model.gltf | --native-spec-route scene.json [--expect-occluded]]");
    Console.Error.WriteLine("Suites: " + string.Join(", ", suites.Keys));
    Environment.ExitCode = 2;
    return;
}
Console.WriteLine($"{checks} shared checks passed (storage suite reports its own count).");
