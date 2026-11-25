using System.Globalization;
using System.Numerics;
namespace Blueprint.Rendering;

static class PerformancePainting
{

    public static string Value(double? value, string unit) => value is { } v && double.IsFinite(v) ? v.ToString("0.##", CultureInfo.InvariantCulture) + (unit.Length > 0 ? " " + unit : "") : "Unavailable";

    static string[] Wrap(string s, float width, int font) => AnnotationText.Wrap(s, Math.Max(4, (int)(width / NativeText.MonoWidth(font))), 512);

    static string AngleText(SpecMetric row) => Math.Abs((row.Vacuum ?? 0) - row.OtherAngle) < .001 ? $"+/-{row.Vacuum:0.#} deg" : $"Y +/-{row.Vacuum:0.#} / Z +/-{row.OtherAngle:0.#} deg";

    public static IEnumerable<(string Label, double? Value)> Environments(SpecMetric row, SpecBlock b)
    {
        if (!row.Environmental)
        {
            yield return ("", row.Vacuum);
            yield break;
        }
        if ((b.Environment & 1) != 0)
            yield return ("VAC", row.Vacuum);
        if ((b.Environment & 2) != 0)
            yield return ("ASL", row.Asl);
    }

    static float RowHeight(SpecMetric row, SpecBlock b, int font)
    {
        float width = b.Bounds.Width - 24;
        if (b.Format == 0)
        {
            int labelLines = Wrap(row.Label, width * .43f, font).Length;
            string value = row.Text ?? (row.Angle ? AngleText(row) : Value(row.Vacuum, row.Unit));
            int valueLines = row.Environmental ? Environments(row, b).Select(e => Wrap(Value(e.Value, row.Unit), width * .55f / (b.Environment == 3 ? 2 : 1), font).Length).DefaultIfEmpty(1).Max() : Wrap(value, width * .55f, font).Length;
            return Math.Max(labelLines, valueLines) * font * 1.3f + font * .5f;
        }
        if (row.Text != null || row.Angle)
            return font * (2 + Wrap(row.Text ?? AngleText(row), width, font).Length * 1.3f);
        return font * 1.4f + Environments(row, b).Count() * font * 3.5f;
    }

    sealed record Section(SpecMetric[] Rows, float Height, float TextWidth, bool Diagram = false);

    static IEnumerable<Section> Sections(PerformanceData data, SpecBlock b, int font)
    {
        var rows = SpecMetrics.Rows(data, b);
        float width = b.Bounds.Width - 24;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var angle = row.Key == "throttle" && i + 1 < rows.Length && rows[i + 1].Angle ? rows[i + 1] : row;
            if (b.Format != 0 && angle.Angle && angle.Text == null && (angle.Vacuum > 0 || angle.OtherAngle > 0))
            {
                SpecMetric[] group = row == angle ? [row] : [row, rows[++i]];
                float textWidth = width * .58f;
                float height = group.Sum(r => font * (2 + Wrap(r.Text ?? AngleText(r), textWidth, font).Length * 1.3f));
                yield return new(group, Math.Max(height, font * 4), textWidth, true);
            }
            else
                yield return new([row], RowHeight(row, b, font), width);
        }
    }

    public static float Height(PerformanceData data, SpecBlock b, int font, string? drawingTitle = null)
    {
        float h = 24;
        if (b.Shows(SpecField.Name))
            h += Wrap(SpecMetrics.Title(data, b, drawingTitle), b.Bounds.Width - 24, font + 2).Length * (font + 2) * 1.3f + 8;
        if (b.Reference)
            h += Wrap(SpecMetrics.Basis(data, b), b.Bounds.Width - 24, Math.Max(14, font - 4)).Length * Math.Max(14, font - 4) * 1.3f + 8;
        if (b.Format == 0 && SpecMetrics.Rows(data, b).Any(r => r.Environmental))
            h += font * 1.5f;
        h += Sections(data, b, font).Sum(s => s.Height);
        if (b.Reference)
            h += Wrap("VAC 0 Pa / ASL 101325 Pa / Earth g0. Captured loading. Stage estimates follow KSA plan; coasts excluded.", b.Bounds.Width - 24, Math.Max(12, font - 6)).Length * Math.Max(12, font - 6) * 1.3f + 12;
        return MathF.Ceiling(h);
    }

    public static int Font(PerformanceData data, SpecBlock b, string? drawingTitle = null)
    {
        for (int f = b.FontSize; f >= 14; f--)
            if (Height(data, b, f, drawingTitle) <= b.Bounds.Height)
                return f;
        return 0;
    }

    public static void Validate(RenderScene scene, IReadOnlyList<PartLabel> labels)
    {
        if (scene.Specs.Length == 0)
            return;
        var data = scene.Performance ?? throw new InvalidOperationException("Generate PawPrint to capture performance data.");
        var area = FrameLayout.Area(scene.Options);
        foreach (var b in scene.Specs)
        {
            if (b.Environment is < 1 or > 3 || !(scene.Options.FreeLayout ? CanvasBounds.Safe(b.Bounds) : PageLayout.Fits(b.Bounds, scene.Options)) || Font(data, b, scene.Options.TitleBlock?.Title) == 0)
                throw new InvalidOperationException("Enlarge the specification block or hide some fields to fit its content.");
            if (!scene.Options.FreeLayout && (scene.Specs.Any(o => o.Id != b.Id && o.Bounds.Overlaps(b.Bounds)) || scene.Details.Any(d => d.Definition.Bounds.Overlaps(b.Bounds)) || PrintTables.Panels(scene, labels).Any(t => t.Bounds.Overlaps(b.Bounds))))
                throw new InvalidOperationException("Keep specification blocks, tables and details clear of each other.");
            if (b.Leader && !b.Vehicle && !scene.Options.FreeLayout &&
                !scene.Parts.Any(p => p.NodeId == b.EnginePart && scene.Visible.ContainsKey((b.ViewId, p.Id))))
                throw new InvalidOperationException("The engine leader target is not visible in its view. Restore that view or disable the block leader before generating.");
            if (b.Stage >= 0 && !data.Stages.Any(s => s.Number == b.Stage))
                throw new InvalidOperationException("The selected performance stage is unavailable. Choose another scope.");
            if (b.SelectedEngines != null && b.SelectedEngines.Any(id => !data.Engines.Any(e => e.Id == id)))
                throw new InvalidOperationException("A selected engine is missing. Previous print retained.");
        }
    }

    public static double Maximum(PerformanceData data, SpecBlock block, SpecMetric metric, double value)
    {
        var custom = block.Styles?.FirstOrDefault(s => s.Key == metric.Key)?.Maximum ?? 0;
        if (custom > 0)
            return custom;
        double largest = metric.Key switch
        {
            "thrust" => data.Engines.Select(e => Math.Max(e.Thrust(false), e.Thrust(true)) / 1000).DefaultIfEmpty(0).Max(),
            "isp" => data.Engines.Select(e => Math.Max(e.Isp(false), e.Isp(true))).DefaultIfEmpty(0).Max(),
            "flow" => data.Engines.Select(e => Math.Max(e.FlowVac, e.FlowAsl)).DefaultIfEmpty(0).Max(),
            _ => 0
        };
        double n = Math.Max(value, largest);
        if (n <= 0)
            return 1;
        double power = Math.Pow(10, Math.Floor(Math.Log10(n)));
        return new[] { 1d, 2, 5, 10 }.Select(x => x * power).First(x => x >= n);
    }

    public static void DrawAngle(Canvas c, float x, float y, float width, float height, double degrees, string axis, int font)
    {
        // The real angle is preserved; only diagram size changes to fit beside text.
        float label = axis.Length > 0 ? font * 1.3f : 0;
        float r = Math.Min(font * 2.5f, Math.Min(width * .42f, (height - label - 8) / 2)), cx = x + width / 2, cy = y + label + (height - label) / 2;
        double a = Math.Clamp(degrees, 0, 180) * Math.PI / 180;
        Vector2 Point(double angle) => new(cx + (float)Math.Sin(angle) * r, cy + (float)Math.Cos(angle) * r);
        for (int i = 0; i < r; i += 12)
            c.Line((int)cx, (int)(cy + i), (int)cx, (int)(cy + Math.Min(i + 5, r)), .45f);
        foreach (double sign in new[] { -1d, 1d })
        {
            var p = Point(sign * a);
            c.Line((int)cx, (int)cy, (int)p.X, (int)p.Y, .95f);
        }
        for (int i = 0; i < 40; i++)
        {
            var p = Point(-a + 2 * a * i / 40);
            var q = Point(-a + 2 * a * (i + 1) / 40);
            c.Line((int)p.X, (int)p.Y, (int)q.X, (int)q.Y, 1);
        }
        if (axis.Length > 0)
            c.Text(axis, (int)(cx - axis.Length * NativeText.MonoWidth(font) / 2), (int)y, font, 1);
    }

    public static void Paint(Canvas canvas, RenderScene scene, IReadOnlyList<PartLabel> labels, List<(System.Numerics.Vector2 Start, System.Numerics.Vector2 End)>? leaderSegments = null)
    {
        Validate(scene, labels);
        if (scene.Specs.Length == 0)
            return;
        var data = scene.Performance!;
        var colors = SheetLayout.Colors(scene.Options.Theme);
        var paper = scene.Options.FreeLayout ? null : new Canvas(scene.Width, scene.Height, colors.Paper, colors.Ink, scene.Options);
        foreach (var b in scene.Specs)
        {
            canvas.HitTarget = new(DrawingObjectKind.Performance, b.Id);
            if (b.Fill)
                if (!scene.Options.FreeLayout) PageLayout.ClearPanel(canvas, paper!, b.Bounds, scene.Options);
            if (b.Border)
            {
                var r = b.Bounds;
                canvas.Line((int)r.X, (int)r.Y, (int)r.Right, (int)r.Y, .8f);
                canvas.Line((int)r.X, (int)r.Bottom, (int)r.Right, (int)r.Bottom, .8f);
                canvas.Line((int)r.X, (int)r.Y, (int)r.X, (int)r.Bottom, .8f);
                canvas.Line((int)r.Right, (int)r.Y, (int)r.Right, (int)r.Bottom, .8f);
            }
            int font = Font(data, b, scene.Options.TitleBlock?.Title);
            float x = b.Bounds.X + 12, ypos = b.Bounds.Y + 12, width = b.Bounds.Width - 24;
            void Text(string text, float xx, ref float yy, float w, int f)
            {
                foreach (var line in Wrap(text, w, f))
                {
                    canvas.Text(line, (int)xx, (int)yy, f, 1);
                    yy += f * 1.3f;
                }
            }
            if (b.Shows(SpecField.Name))
            {
                Text(SpecMetrics.Title(data, b, scene.Options.TitleBlock?.Title), x, ref ypos, width, font + 2);
                ypos += 8;
            }
            if (b.Reference)
            {
                Text(SpecMetrics.Basis(data, b), x, ref ypos, width, Math.Max(14, font - 4));
                ypos += 8;
            }
            var rows = SpecMetrics.Rows(data, b);
            if (b.Format == 0 && rows.Any(r => r.Environmental))
            {
                float xx = x + width * .45f;
                foreach (var env in Environments(new("", "", "", Environmental: true), b))
                {
                    canvas.Text(env.Label, (int)xx, (int)ypos, font, 1);
                    xx += width * .55f / (b.Environment == 3 ? 2 : 1);
                }
                ypos += font * 1.5f;
            }
            foreach (var section in Sections(data, b, font))
            {
                float next = ypos + section.Height;
                var row = section.Rows[0];
                if (section.Diagram)
                {
                    float yy = ypos;
                    foreach (var item in section.Rows)
                    {
                        canvas.Text(item.Label, (int)x, (int)yy, font, 1);
                        yy += font * 1.4f;
                        Text(item.Text ?? AngleText(item), x, ref yy, section.TextWidth, font);
                        yy += font * .6f;
                    }
                    var angle = section.Rows[^1];
                    float xx = x + section.TextWidth + 12, w = width - section.TextWidth - 12;
                    if (Math.Abs((angle.Vacuum ?? 0) - angle.OtherAngle) < .001)
                        DrawAngle(canvas, xx, ypos, w, section.Height, angle.Vacuum ?? 0, "", font);
                    else
                    {
                        DrawAngle(canvas, xx, ypos, w / 2, section.Height, angle.Vacuum ?? 0, "Y", font);
                        DrawAngle(canvas, xx + w / 2, ypos, w / 2, section.Height, angle.OtherAngle, "Z", font);
                    }
                    ypos = next;
                    continue;
                }
                if (b.Format == 0)
                {
                    float yy = ypos;
                    Text(row.Label, x, ref yy, width * .43f, font);
                    yy = ypos;
                    float xx = x + width * .45f;
                    if (row.Text != null || row.Angle)
                        Text(row.Text ?? AngleText(row), xx, ref yy, width * .55f, font);
                    else
                        foreach (var env in Environments(row, b))
                        {
                            yy = ypos;
                            float w = width * .55f / (row.Environmental && b.Environment == 3 ? 2 : 1);
                            Text(Value(env.Value, row.Unit), xx, ref yy, w, font);
                            xx += w;
                        }
                    canvas.Line((int)x, (int)(next - 3), (int)(x + width), (int)(next - 3), .18f);
                }
                else
                {
                    canvas.Text(row.Label, (int)x, (int)ypos, font, 1);
                    float yy = ypos + font * 1.4f;
                    if (row.Text != null || row.Angle)
                        Text(row.Text ?? AngleText(row), x, ref yy, width, font);
                    else
                        foreach (var env in Environments(row, b))
                        {
                            string text = (env.Label.Length > 0 ? env.Label + " " : "") + Value(env.Value, row.Unit);
                            int mode = b.Styles?.FirstOrDefault(s => s.Key == row.Key)?.Mode ?? b.BarMode;
                            double value = env.Value ?? 0, max = Maximum(data, b, row, value);
                            float barX = x, barWidth = width;
                            if (mode == 2 || env.Value == null || width - text.Length * NativeText.MonoWidth(font) - 16 < 80)
                                canvas.Text(text, (int)x, (int)yy, font, 1);
                            else
                            {
                                float textWidth = text.Length * NativeText.MonoWidth(font) + 16;
                                if (mode == 1)
                                {
                                    canvas.Text(text, (int)x, (int)yy, font, 1);
                                    barX += textWidth;
                                    barWidth -= textWidth;
                                }
                                int count = Math.Max(1, (int)(barWidth / 18));
                                float step = barWidth / count, filled = (float)Math.Clamp(value / max, 0, 1) * count;
                                float textStart = x + (width - textWidth) / 2, textEnd = textStart + textWidth;
                                for (int i = 0; i < count; i++)
                                {
                                    float bx = barX + i * step;
                                    if (mode == 0 && bx < textEnd && bx + step > textStart)
                                        continue;
                                    float a = i < filled ? .8f : .22f;
                                    int left = (int)bx, right = (int)(bx + step - 4), top = (int)yy, bottom = top + font;
                                    canvas.Line(left, top, right, top, a);
                                    canvas.Line(left, bottom, right, bottom, a);
                                    canvas.Line(left, top, left, bottom, a);
                                    canvas.Line(right, top, right, bottom, a);
                                    if (i < filled)
                                        canvas.Line(left, top, right, bottom, a);
                                }
                                if (mode == 0)
                                    canvas.Text(text, (int)textStart, (int)yy, font, 1);
                                canvas.Text("0 - " + Value(max, row.Unit), (int)barX, (int)(yy + font + 3), Math.Max(12, font - 7), .6f);
                            }
                            yy += font * 3.5f;
                        }
                }
                ypos = next;
            }
            if (b.Reference)
            {
                ypos += 8;
                Text("VAC 0 Pa / ASL 101325 Pa / Earth g0. Captured loading. Stage estimates follow KSA plan; coasts excluded.", x, ref ypos, width, Math.Max(12, font - 6));
            }
            if (b.Leader && !b.Vehicle)
            {
                var path = SpecLeaderRouting.Route(scene, b, labels);
                if (path.Length < 2)
                    continue;
                for (int i = 1; i < path.Length; i++)
                    leaderSegments?.Add((path[i - 1], path[i]));
                LeaderPainting.Paint(canvas, path, b.Line, b.Tip);
            }
        }
    }
}
