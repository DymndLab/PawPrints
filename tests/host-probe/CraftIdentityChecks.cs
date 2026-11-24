using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

static class CraftIdentityChecks
{
    public static void Run(Assembly host, Assembly mod, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var partType = host.GetType("KSA.Part", true)!;
        var templateType = host.GetType("KSA.PartTemplate", true)!;
        var connectorType = partType.GetNestedType("Connector")!;
        var connectionType = partType.GetNestedType("Connection")!;
        var moduleType = host.GetType("KSA.ModuleList", true)!;
        var listType = typeof(List<>).MakeGenericType(partType);
        object Part(uint id, string name)
        {
            var part = RuntimeHelpers.GetUninitializedObject(partType);
            var template = RuntimeHelpers.GetUninitializedObject(templateType);
            templateType.GetProperty("Id", flags)!.SetValue(template, name);
            partType.GetField("Template")!.SetValue(part, template);
            partType.GetField("InstanceId")!.SetValue(part, id);
            partType.GetField("Modules")!.SetValue(part, Activator.CreateInstance(moduleType, new object[] { false }));
            partType.GetField("TreeChildren")!.SetValue(part, Activator.CreateInstance(listType));
            partType.GetField("Connectors")!.SetValue(part, Array.CreateInstance(connectorType, 0));
            return part;
        }
        object Connector(object part, string id, bool interior = false)
        {
            var connector = RuntimeHelpers.GetUninitializedObject(connectorType);
            connectorType.GetField("Id")!.SetValue(connector, id);
            connectorType.GetField("Parent")!.SetValue(connector, part);
            if (interior)
            {
                var field = connectorType.GetField("Flags")!;
                field.SetValue(connector, Enum.Parse(field.FieldType, "Internal"));
            }
            return connector;
        }
        var cargo = Part(10, "fixture.cargo");
        var capsule = Part(20, "fixture.capsule");
        var tank = Part(30, "fixture.tank");
        var sub = Part(40, "fixture.subpart");
        partType.GetField("PartParent")!.SetValue(sub, cargo);
        var subparts = (IList)Activator.CreateInstance(listType)!;
        subparts.Add(sub);
        partType.GetField("_subParts", flags)!.SetValue(cargo, subparts);
        var top = Connector(cargo, "top");
        var bottom = Connector(cargo, "bottom");
        var internalConnector = Connector(cargo, "inside", true);
        var connect = connectionType.GetMethod("Connect", flags)!;
        check((bool)connect.Invoke(null, [top, Connector(capsule, "bottom")])!
            && (bool)connect.Invoke(null, [bottom, Connector(tank, "top")])!, "Native fixture has independent joints at both cargo ends");
        connect.Invoke(null, [internalConnector, sub]);
        var roots = Array.CreateInstance(partType, 3);
        roots.SetValue(cargo, 0); roots.SetValue(capsule, 1); roots.SetValue(tank, 2);
        var linkType = host.GetType("KSA.FuelLink", true)!;
        var flowType = host.GetType("KSA.FuelLinkFlow", true)!;
        var fuel = Array.CreateInstance(linkType, 1);
        fuel.SetValue(Activator.CreateInstance(linkType, capsule, tank, Enum.Parse(flowType, "AToB")), 0);
        var bindingType = mod.GetType("Blueprint.Rendering.SourceBinding", true)!;
        var binding = Activator.CreateInstance(bindingType, Guid.NewGuid(), 1L)!;
        var take = mod.GetType("Blueprint.CraftIdentityCapture", true)!.GetMethod("TakeParts", flags)!;
        var snapshot = take.Invoke(null, [roots, fuel, binding])!;
        var nodes = ((IEnumerable)snapshot.GetType().GetProperty("Parts")!.GetValue(snapshot)!).Cast<object>().ToArray();
        var links = ((IEnumerable)snapshot.GetType().GetProperty("Links")!.GetValue(snapshot)!).Cast<object>().ToArray();
        object Get(object obj, string name) => obj.GetType().GetProperty(name)!.GetValue(obj)!;
        var joints = links.Where(l => Get(l, "Kind").ToString() == "Structural").ToArray();
        check(nodes.Length == 4 && joints.Length == 3, "Production identity capture includes subparts and deduplicates native joint visits");
        var keys = joints.Select(l => Get(l, "Key")).ToArray();
        var connectorIds = keys.SelectMany(k => new[] { Get(Get(k, "A"), "Connector"), Get(Get(k, "B"), "Connector") }).Cast<string>().ToArray();
        check(connectorIds.Contains("top") && connectorIds.Contains("bottom") && joints.Count(l => (bool)Get(l, "Internal")) == 1,
            "Detached joints retain endpoint names and internal-connector flags");
        check(links.Count(l => Get(l, "Kind").ToString() == "Ownership") == 1 && links.Count(l => Get(l, "Kind").ToString() == "Fuel") == 1,
            "Production snapshot separates ownership from joints and fuel links");
        var resolve = snapshot.GetType().GetMethod("ResolveJoint")!;
        check(keys.All(key => resolve.Invoke(snapshot, [binding, key])!.ToString() == "Exact"),
            "Captured native connector pairs resolve unambiguously");
        connectorType.GetField("Id")!.SetValue(top, "edited-after-capture");
        check(!connectorIds.Contains("edited-after-capture"), "Snapshot connector strings are detached from subsequent native edits");
        var reference = host.GetType("KSA.PartInstance", true)!;
        check(reference.GetField("GlobalInstanceId")!.GetCustomAttribute<XmlIgnoreAttribute>() != null
            && reference.GetField("LocalInstanceId")!.GetCustomAttribute<XmlAttributeAttribute>() != null,
            "Native save contract excludes runtime identity and serializes only local traversal identity");
        // Use an isolated part with no native templates/modules to serialize. No
        // game startup, asset loading, profile writes or native ID counter edits.
        var single = Part(99, "fixture.single");
        var serialize = partType.GetMethod("GetReferenceWithChildren", flags, null,
            [typeof(uint).MakeByRefType(), reference, typeof(bool)], null)!;
        object?[] firstArgs = [1u, null, false], secondArgs = [500u, null, false];
        var first = serialize.Invoke(single, firstArgs)!;
        var second = serialize.Invoke(single, secondArgs)!;
        check((uint)reference.GetField("LocalInstanceId")!.GetValue(first)! == 1
            && (uint)reference.GetField("LocalInstanceId")!.GetValue(second)! == 500
            && (uint)reference.GetField("GlobalInstanceId")!.GetValue(first)! == 99,
            "Actual native serialization assigns different save-local IDs to the same runtime part");
        var bindingFor = take.DeclaringType!.GetMethod("BindingFor", flags)!;
        var originalBinding = bindingFor.Invoke(null, [roots])!;
        var retainedRoots = Array.CreateInstance(partType, 2);
        retainedRoots.SetValue(tank, 0); retainedRoots.SetValue(Part(50, "fixture.new"), 1);
        var retainedBinding = bindingFor.Invoke(null, [retainedRoots])!;
        check(Equals(Get(originalBinding, "Session"), Get(retainedBinding, "Session")) &&
            (long)Get(retainedBinding, "Revision") > (long)Get(originalBinding, "Revision"),
            "PartTree replacement with surviving native parts retains source session and advances revision");
        var reloadedRoots = Array.CreateInstance(partType, 1);
        reloadedRoots.SetValue(Part(30, "fixture.tank"), 0);
        var reloadedBinding = bindingFor.Invoke(null, [reloadedRoots])!;
        check(!Equals(Get(retainedBinding, "Session"), Get(reloadedBinding, "Session")),
            "New native parts cannot inherit source identity from reused numeric IDs and names");
    }
}
