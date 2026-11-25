using Blueprint.Rendering;
using KSA;
using V = System.Numerics.Vector3;
namespace Blueprint;

static class PerformanceCapture
{


    public static PerformanceData Take(PartTree tree, AssemblyCapture assembly, PartSupport support)
    {
        var engines = new List<EngineRating>();
        foreach (var engine in tree.Modules.Get<EngineController>())
        {
            RocketControllerData.ComputeFromRockets(engine.Rockets.AsSpan(), 0, 101325, 1, out var vacuum, out var asl);
            var owner = engine.Parent.StructuralPart;
            var nozzles = new List<NozzleGimbal>();
            var propellants = new List<string>();
            // KSA calculates conditions for all cores of a rocket together.
            // The result slots follow rocket.Cores, as in the native aggregate.
            var conditions = new RocketCoreConditions[Rocket.FindCoreCountMax(engine.Rockets.AsSpan())];
            foreach (var rocket in engine.Rockets)
            {
                rocket.ComputeCoreConditions(1f, conditions);
                var cores = rocket.Cores;
                for (int coreIndex = 0; coreIndex < cores.Length; coreIndex++)
                {
                    var core = cores[coreIndex];
                    propellants.AddRange(core.Reaction.ReactantPhases.Select(p => p.Name));
                    foreach (var nozzle in core.Nozzles)
                    {
                        var gimbal = nozzle.Parent.GimbalAsmb;
                        nozzles.Add(new(nozzle.ComputePerformance(in conditions[coreIndex], 0).GetTotalThrust(),
                            gimbal == null ? 0 : (double)gimbal.AxisY.MaxAngle * 180 / Math.PI,
                            gimbal == null ? 0 : (double)gimbal.AxisZ.MaxAngle * 180 / Math.PI));
                    }
                }
            }
            var main = NozzleGimbal.Primary(nozzles);
            double mass = 0;
            foreach (var m in owner.SubtreeModules.Get<InertMass>())
                if (m.Parent.StructuralPart == owner)
                    mass += m.MassPropertiesAsmb.Props.Mass;
            bool oneController = owner.SubtreeModules.Get<EngineController>().ToArray().Count(e => e.Parent.StructuralPart == owner) == 1;
            string prop = string.Join(" + ", propellants.Distinct());
            engines.Add(new(engine.Parent.InstanceId + "/" + engine.TemplateId, owner.InstanceId, PartNames.Display(owner.Template.Id, owner.Template.DisplayName, support), owner.Template.Id, engine.Sequence,
                new(vacuum.ThrustMax.X, vacuum.ThrustMax.Y, vacuum.ThrustMax.Z), new(asl.ThrustMax.X, asl.ThrustMax.Y, asl.ThrustMax.Z), vacuum.MassFlowRateMax, asl.MassFlowRateMax, engine.MinimumThrottle, prop, main.Y, main.Z, oneController ? mass : null));
        }
        double dry = tree.ComputeInertMassPropertiesAsmb().Props.Mass, fuel = tree.ComputePropellantMassPropertiesAsmb().Props.Mass;
        var loading = new Dictionary<string, double>();
        foreach (var mole in tree.Modules.Get<Mole>())
        {
            string name = mole.SubstancePhase.Name;
            double mass = tree.Moles.States[mole.StatesIdx].Mass;
            if (mass > 0)
                loading[name] = loading.GetValueOrDefault(name) + mass;
        }
        V min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = 0; i < assembly.Geometry.Meshes.Length; i++)
            if (!assembly.Internal[i])
                foreach (var point in assembly.Geometry.Meshes[i].WorldVertices())
                {
                    min = V.Min(min, point);
                    max = V.Max(max, point);
                }
        V size = max - min;
        var stages = new List<StageRating>();
        string? unavailable = tree.PerformanceSequences.IsDirty ? "Unavailable: KSA stage estimate is still updating" : null;
        if (unavailable == null)
        {
            var sequences = tree.SequenceList.Sequences;
            var performance = tree.PerformanceSequences.PerformanceSequences;
            for (int i = 0; i < Math.Min(sequences.Length, performance.Length); i++)
            {
                var p = performance[i];
                var phaseParts = p.PhaseEngineParts?.FirstOrDefault();
                var ids = engines.Where(e => phaseParts?.Any(part => part.StructuralPart.InstanceId == e.Part) == true).Select(e => e.Id).ToArray();
                var props = (p.Propellants ?? []).GroupBy(x => (x.TankPart.InstanceId, x.ReactantName)).Select(g => g.First()).GroupBy(x => x.ReactantName).Select(g => new PropellantAmount(g.Key, g.Sum(x => x.StartMass))).ToArray();
                stages.Add(new(sequences[i].Number, sequences[i].Environment == PerformanceEnvironment.Vacuum ? "VAC" : "ASL", p.WetMass, p.InertMass, p.FuelMass, p.DeltaV, p.Phases?.Sum(x => x.Duration) ?? 0, ids, p.AttachedParts?.Select(part => part.StructuralPart).Distinct().Count() ?? 0, props));
            }
        }
        return new(engines.ToArray(), dry + fuel, dry, fuel, tree.Parts.ToArray().Select(part => part.StructuralPart).Distinct().Count(), size, loading.Select(p => new PropellantAmount(p.Key, p.Value)).ToArray(), stages.ToArray(), unavailable);
    }
}
