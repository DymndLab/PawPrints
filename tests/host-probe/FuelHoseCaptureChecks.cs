using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

static class FuelHoseCaptureChecks
{
    public static void Run(Assembly host, Assembly mod, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var partType = host.GetType("KSA.Part", true)!;
        var treeType = host.GetType("KSA.PartTree", true)!;
        var listType = host.GetType("KSA.FuelLinkList", true)!;
        var linkType = host.GetType("KSA.FuelLink", true)!;
        var flowType = host.GetType("KSA.FuelLinkFlow", true)!;
        var tubeType = mod.GetType("Blueprint.Rendering.FuelHoseGeometry", true)!;
        check(Equals(listType.GetField("HOSE_RADIUS")!.GetRawConstantValue(), tubeType.GetField("Radius")!.GetRawConstantValue())
            && Equals(listType.GetField("HOSE_RADIAL_SEGMENTS")!.GetRawConstantValue(), tubeType.GetField("RadialSegments")!.GetRawConstantValue()),
            "Captured hose radius and radial tessellation match actual host constants");
        var tree = RuntimeHelpers.GetUninitializedObject(treeType);
        var links = Activator.CreateInstance(listType, tree)!;
        treeType.GetField("FuelLinks")!.SetValue(tree, links);
        var nativeLinks = (IList)listType.GetField("_links", flags)!.GetValue(links)!;
        var positionType = partType.GetField("_positionVehicleAsmb", flags)!.FieldType;
        object Position(double x, double y, double z) => Activator.CreateInstance(positionType, x, y, z)!;
        object Part(uint id, double x, double y, double z)
        {
            var part = RuntimeHelpers.GetUninitializedObject(partType);
            partType.GetField("InstanceId")!.SetValue(part, id);
            partType.GetField("Modules")!.SetValue(part, Activator.CreateInstance(host.GetType("KSA.ModuleList", true)!, new object[] { false }));
            partType.GetField("_positionVehicleAsmb", flags)!.SetValue(part, Position(x, y, z));
            var matrixField = partType.GetField("_matrixAsmb2VehicleAsmb", flags)!;
            matrixField.SetValue(part, Activator.CreateInstance(matrixField.FieldType,
                new object[] { 1d, 0d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 0d, 1d, 0d, x, y, z, 1d }));
            var rotation = partType.GetField("_asmb2VehicleAsmb", flags)!;
            rotation.SetValue(part, Activator.CreateInstance(rotation.FieldType, 0d, 0d, 0d, 1d));
            return part;
        }
        var a = Part(1, 0, 0, 0);
        var b = Part(2, 2, 0, 0);
        var link = Activator.CreateInstance(linkType, a, b, Enum.Parse(flowType, "AToB"))!;
        linkType.GetField("Enabled")!.SetValue(link, false);
        var userNodes = (IList)linkType.GetField("UserNodeOffsetsPartA")!.GetValue(link)!;
        userNodes.Add(Position(1, 0, 1));
        nativeLinks.Add(link);
        var liveRoute = linkType.GetField("Route")!.GetValue(link)!;
        var count = liveRoute.GetType().GetProperty("WaypointCount")!;
        var take = mod.GetType("Blueprint.FuelHoseCapture", true)!.GetMethod("Take")!;
        var result = (Array)take.Invoke(null, [tree, 1000000])!;
        var item = (ITuple)result.GetValue(0)!;
        var mesh = item[1]!;
        var vertices = (double[][])mesh.GetType().GetProperty("Vertices")!.GetValue(mesh)!;
        check(result.Length == 1 && ReferenceEquals(item[0], a) && vertices.Length >= 24 * 16,
            "Production capture reads the native fuel-link collection and includes a disabled-flow hose");
        double[] RingCenter(int start) => Enumerable.Range(0, 3).Select(axis => vertices.Skip(start).Take(16).Average(p => p[axis])).ToArray();
        var first = RingCenter(0);
        var last = RingCenter(vertices.Length - 16);
        check(first.All(v => Math.Abs(v) < 1e-6) && Math.Abs(last[0] - 2) < 1e-6
            && Math.Abs(last[1]) < 1e-6 && Math.Abs(last[2]) < 1e-6 && vertices.Max(p => p[2]) > .9,
            "Native route sampling retains both endpoints and the user-authored bend in vehicle coordinates");
        check((int)count.GetValue(liveRoute)! == 0 && (bool)listType.GetField("_routesDirty", flags)!.GetValue(links)!
            && userNodes.Count == 1 && !(bool)linkType.GetField("Enabled")!.GetValue(link)!,
            "Capture leaves live route caches, dirty state, user nodes and enabled state unchanged");
        var repeat = (Array)take.Invoke(null, [tree, 1000000])!;
        var secondMesh = ((ITuple)repeat.GetValue(0)!)[1]!;
        var secondVertices = (double[][])mesh.GetType().GetProperty("Vertices")!.GetValue(secondMesh)!;
        check(vertices.Zip(secondVertices).All(p => p.First.SequenceEqual(p.Second)) && !ReferenceEquals(vertices, secondVertices),
            "Repeated native hose capture produces independent deterministic snapshots");
        bool budgetRejected = false;
        try { take.Invoke(null, [tree, vertices.Length - 1]); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { budgetRejected = true; }
        check(budgetRejected, "Production hose capture enforces the remaining craft vertex budget");
        // Real FuelPort route endpoints use connector tips and lead-outs, not
        // the containing part's origin. Pre-resolve a test connector without UI.
        var portType = host.GetType("KSA.FuelPort", true)!;
        var port = Activator.CreateInstance(portType, flags, null, new object[] { "fixture" }, null)!;
        portType.GetProperty("Parent")!.SetValue(port, a);
        var connector = Part(3, 0, 0, .25);
        portType.GetField("_connector", flags)!.SetValue(port, connector);
        var tipField = portType.GetField("ConnectorTipConnectorAsmb")!;
        tipField.SetValue(port, Activator.CreateInstance(tipField.FieldType, 0f, 0f, .25f));
        portType.GetField("ConnectorLeadOut")!.SetValue(port, .15f);
        var modules = partType.GetField("Modules")!.GetValue(a)!;
        modules.GetType().GetMethods().Single(m => m.Name == "Add" && m.IsGenericMethodDefinition)
            .MakeGenericMethod(portType).Invoke(modules, [port]);
        var withPort = (Array)take.Invoke(null, [tree, 1000000])!;
        var portMesh = ((ITuple)withPort.GetValue(0)!)[1]!;
        var portVertices = (double[][])mesh.GetType().GetProperty("Vertices")!.GetValue(portMesh)!;
        check(Math.Abs(portVertices.Take(16).Average(p => p[2]) - .5) < 1e-6
            && portVertices[16 * 2][2] > portVertices[0][2],
            "Production capture starts at the native connector tip and follows its pinned lead-out");
        check(((uint[])mesh.GetType().GetProperty("ConnectedParts")!.GetValue(portMesh)!).Contains(3u),
            "Captured hose pose dependencies include movable connector subparts");
        nativeLinks.Clear();
        check(((Array)take.Invoke(null, [tree, 0])!).Length == 0, "A craft without fuel links adds no hose geometry");
    }
}
