using System.Numerics;
namespace Blueprint.Rendering;

readonly record struct NozzleGimbal(double VacuumThrust, double Y, double Z)
{

    // Identify the main nozzle by nominal full-throttle vacuum thrust, not
    // animation range or current firing state. Keep both axes from that nozzle;
    // a fixed main nozzle must not inherit an auxiliary nozzle's movement.
    public static NozzleGimbal Primary(IEnumerable<NozzleGimbal> nozzles) => nozzles
        .Where(n => double.IsFinite(n.VacuumThrust) && n.VacuumThrust > 0)
        .OrderByDescending(n => n.VacuumThrust).FirstOrDefault();
}

sealed record EngineRating(string Id, uint Part, string Name, string Template, int Sequence, Vector3 Vacuum, Vector3 SeaLevel, double FlowVac, double FlowAsl, double MinimumThrottle, string Propellants, double GimbalY, double GimbalZ, double? DryMass)
{

    public double Thrust(bool asl) => (asl ? SeaLevel : Vacuum).Length();

    public double Isp(bool asl) => PerformanceData.Isp(Thrust(asl), asl ? FlowAsl : FlowVac);
    public string Signature => $"{Template}/{Vacuum.Length():G5}/{SeaLevel.Length():G5}/{FlowVac:R}/{FlowAsl:R}/{Propellants}/{MinimumThrottle:R}/{GimbalY:R}/{GimbalZ:R}";
}
sealed record PropellantAmount(string Name, double Mass);
sealed record StageRating(int Number, string Environment, double Wet, double Dry, double Fuel, double DeltaV, double BurnTime, string[] Engines, int Parts, PropellantAmount[] Loading);
sealed record PerformanceData(EngineRating[] Engines, double WetMass, double DryMass, double FuelMass, int PartCount, Vector3 Size, PropellantAmount[] Loading, StageRating[] Stages, string? EstimateUnavailable = null, string? CraftName = null)
{

    public static double Isp(double thrust, double flow) => flow > 0 ? thrust / (flow * 9.80665) : 0;

    public static double Twr(double thrust, double mass) => mass > 0 ? thrust / (mass * 9.80665) : 0;
}
[Flags]
enum SpecField : long
{
    Name = 1, Quantity = 2, Thrust = 4, Isp = 8, Propellants = 16, Throttle = 32, Gimbal = 64, Flow = 128, DryMass = 256, LoadedMass = 512, FuelMass = 1024, Twr = 2048, BurnTime = 4096, DeltaV = 8192, Dimensions = 16384, Loading = 32768, PartCount = 65536
}
sealed record MetricStyle(string Key, int Mode, double Maximum = 0);
sealed record SpecBlock(int Id, Box Bounds, bool Vehicle = false, uint? EnginePart = null, int ViewId = 0, bool Identical = true, bool Combined = false,
    int Stage = -1, string[]? SelectedEngines = null, int Format = 0, int Environment = 3, int BarMode = 1, SpecField Fields = SpecField.Name | SpecField.Quantity | SpecField.Thrust | SpecField.Isp | SpecField.Propellants | SpecField.Throttle | SpecField.Gimbal,
    int FontSize = 24, bool Border = true, bool Fill = true, bool Leader = false, LeaderLine Line = LeaderLine.Dotted, LeaderTip Tip = LeaderTip.Dot, MetricStyle[]? Styles = null, bool Reference = true, string? VehicleName = null)
{

    public bool Shows(SpecField field) => (Fields & field) != 0;
}
sealed record SpecMetric(string Key, string Label, string Unit, double? Vacuum = null, double? Asl = null, string? Text = null, bool Environmental = false, bool Angle = false, double OtherAngle = 0);

static class SpecMetrics
{

    public static EngineRating[] Engines(PerformanceData data, SpecBlock block)
    {
        if (block.Vehicle)
        {
            var stage = data.Stages.FirstOrDefault(s => s.Number == block.Stage);
            var selected = stage?.Engines ?? block.SelectedEngines;
            return selected == null ? data.Engines : data.Engines.Where(e => selected.Contains(e.Id)).ToArray();
        }
        var own = data.Engines.Where(e => e.Part == block.EnginePart).ToArray();
        if (own.Length == 0)
            throw new InvalidOperationException("An engine specification target is no longer present. Previous print retained.");
        // A multi-controller part is treated as one hardware assembly; only
        // single-controller types are automatically grouped across the vehicle.
        return block.Identical && own.Length == 1 ? data.Engines.Where(e => e.Signature == own[0].Signature).ToArray() : own;
    }

    // Null follows the drawing title; an explicit override belongs to this block.
    public static string VehicleName(PerformanceData data, SpecBlock block, string? drawingTitle = null) =>
        (block.VehicleName ?? (string.IsNullOrWhiteSpace(drawingTitle) ? data.CraftName : drawingTitle) ?? "").Trim();

    public static string Title(PerformanceData data, SpecBlock block, string? drawingTitle = null)
    {
        if (!block.Vehicle)
            return Engines(data, block)[0].Name.ToUpperInvariant();
        string name = VehicleName(data, block, drawingTitle);
        return "VEHICLE PERFORMANCE" + (name.Length == 0 ? "" : " / " + name.ToUpperInvariant());
    }

    public static string Basis(PerformanceData data, SpecBlock block)
    {
        if (!block.Vehicle)
            return (block.Combined ? "GROUP TOTAL" : "PER ENGINE") + " / FULL THROTTLE";
        if (block.Stage >= 0)
            return "KSA STAGE " + block.Stage + " / INITIAL ACTIVE ENGINES";
        return block.SelectedEngines == null ? "ALL INSTALLED ENGINES / CAPTURED VEHICLE MASS" : "SELECTED ENGINES / CAPTURED VEHICLE MASS";
    }

    public static SpecMetric[] Rows(PerformanceData data, SpecBlock b)
    {
        var engines = Engines(data, b);
        var stage = data.Stages.FirstOrDefault(s => s.Number == b.Stage);
        var rated = b.Vehicle || b.Combined ? engines : engines.Where(e => e.Part == b.EnginePart).ToArray();
        double vac, asl;
        if (b.Vehicle)
        {
            vac = rated.Aggregate(Vector3.Zero, (v, e) => v + e.Vacuum).Length();
            asl = rated.Aggregate(Vector3.Zero, (v, e) => v + e.SeaLevel).Length();
        }
        else
        {
            vac = rated.Sum(e => e.Thrust(false));
            asl = rated.Sum(e => e.Thrust(true));
        }
        double flowV = rated.Sum(e => e.FlowVac), flowA = rated.Sum(e => e.FlowAsl);
        double wet = stage?.Wet ?? data.WetMass, dry = stage?.Dry ?? data.DryMass, fuel = stage?.Fuel ?? data.FuelMass;
        var rows = new List<SpecMetric>();
        void Add(SpecField f, SpecMetric row)
        {
            if (b.Shows(f))
                rows.Add(row);
        }
        Add(SpecField.Quantity, new("qty", "ENGINES", "", engines.Select(e => e.Part).Distinct().Count()));
        Add(SpecField.Thrust, new("thrust", "THRUST", "kN", vac / 1000, asl / 1000, Environmental: true));
        Add(SpecField.Isp, new("isp", "SPECIFIC IMPULSE", "s", PerformanceData.Isp(vac, flowV), PerformanceData.Isp(asl, flowA), Environmental: true));
        Add(SpecField.Propellants, new("propellants", "PROPELLANTS", "", Text: rated.Length == 0 ? "None selected" : string.Join(" / ", rated.Select(e => e.Propellants).Distinct())));
        Add(SpecField.Throttle, new("throttle", "THROTTLE", "", Text: rated.Length == 0 ? "Unavailable" : rated.Select(e => e.MinimumThrottle).Distinct().Count() > 1 ? "Varies by engine" : rated[0].MinimumThrottle >= .999 ? "Fixed thrust" : $"{rated[0].MinimumThrottle * 100:0.#}-100%"));
        bool sameGimbal = rated.Select(e => (e.GimbalY, e.GimbalZ)).Distinct().Count() <= 1;
        Add(SpecField.Gimbal, new("gimbal", "GIMBAL", "deg", rated.FirstOrDefault()?.GimbalY ?? 0, Text: sameGimbal ? null : "Varies by engine", Angle: sameGimbal, OtherAngle: rated.FirstOrDefault()?.GimbalZ ?? 0));
        Add(SpecField.Flow, new("flow", "PROPELLANT FLOW", "kg/s", flowV, flowA, Environmental: true));
        Add(SpecField.DryMass, new("dry", "DRY MASS", "kg", b.Vehicle ? dry : rated.All(e => e.DryMass.HasValue) ? rated.Select(e => e.DryMass!.Value).Sum() : null, Text: !b.Vehicle && rated.Any(e => !e.DryMass.HasValue) ? "Unavailable (assembly boundary)" : null));
        if (b.Vehicle)
        {
            Add(SpecField.LoadedMass, new("wet", "LOADED MASS", "kg", wet));
            Add(SpecField.FuelMass, new("fuel", stage != null ? "PROPELLANT MASS" : "STORED SUBSTANCE MASS", "kg", fuel));
            Add(SpecField.Twr, new("twr", "TWR / EARTH g0", "", PerformanceData.Twr(vac, wet), PerformanceData.Twr(asl, wet), Environmental: true));
            var stages = stage != null ? [stage] : b.Stage < 0 && b.SelectedEngines == null ? data.Stages : [];
            string env = stage?.Environment ?? (stages.Select(s => s.Environment).Distinct().Count() == 1 ? stages[0].Environment : "MIXED KSA PLAN");
            string? unavailable = data.EstimateUnavailable ?? (stages.Length == 0 ? "Unavailable: no staged fuel-feed estimate" : null);
            Add(SpecField.BurnTime, new("burn", "EST. BURN / " + env, "s", unavailable == null ? stages.Sum(s => s.BurnTime) : null, Text: unavailable));
            Add(SpecField.DeltaV, new("dv", "EST. DELTA-V / " + env, "m/s", unavailable == null ? stages.Sum(s => s.DeltaV) : null, Text: unavailable));
            Add(SpecField.Dimensions, new("dimensions", "H / W / D", "", Text: $"{data.Size.Z:0.##} / {data.Size.X:0.##} / {data.Size.Y:0.##} m (craft)"));
            Add(SpecField.PartCount, new("parts", "PARTS", "", stage?.Parts ?? data.PartCount));
            if (b.Shows(SpecField.Loading))
                foreach (var p in stage?.Loading ?? data.Loading)
                    rows.Add(new("load:" + p.Name, p.Name, "kg", p.Mass));
        }
        return rows.ToArray();
    }
}
