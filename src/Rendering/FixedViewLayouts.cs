using System.Numerics;
namespace Blueprint.Rendering;

// Values describe camera directions; box identity is independent and survives
// blanks, repeated directions and changing another box's camera.
sealed record FixedViewLayout(int Template = 0, int A = 3, int B = 4, int C = 1, int D = 2, int E = -1, int F = -1,
    float TopShare = .5f, int HoldA = -1, int HoldB = -1, int HoldC = -1, int HoldD = -1, int HoldE = -1, int HoldF = -1, bool AutoRows = true)
{
    public int[] Assignments => [A, B, C, D, E, F];
    public int[] FittingViews => [A >= 0 ? A : HoldA, B >= 0 ? B : HoldB, C >= 0 ? C : HoldC, D >= 0 ? D : HoldD, E >= 0 ? E : HoldE, F >= 0 ? F : HoldF];


    public FixedViewLayout ClearBlankFits() => this with { HoldA = -1, HoldB = -1, HoldC = -1, HoldD = -1, HoldE = -1, HoldF = -1 };


    public FixedViewLayout Set(int box, int view)
    {
        if (box is < 0 or > 5 || view is < -1 or > 4)
            throw new ArgumentOutOfRangeException();
        int hold = view < 0 ? FittingViews[box] : -1;
        return box switch
        {
            0 => this with { A = view, HoldA = hold },
            1 => this with { B = view, HoldB = hold },
            2 => this with { C = view, HoldC = hold },
            3 => this with { D = view, HoldD = hold },
            4 => this with { E = view, HoldE = hold },
            _ => this with { F = view, HoldF = hold }
        };
    }
}
static class FixedViewLayouts
{
    public static readonly string[] Names = ["Four boxes / 2 x 2", "One box", "Two across", "Two down", "Three across", "Three down", "Three above / one wide below", "Three above / one centered below", "Two above / one wide below", "One wide above / two below", "Six boxes / 3 x 2", "Six boxes / 2 x 3"];


    public static bool CanSplit(int template) => template is 0 or 3 or 6 or 7 or 8 or 9 or 10;


    public static Box[] Cells(FixedViewLayout layout, Box area) => Cells(layout.Template, area, 40, layout.TopShare);


    public static Box[] Cells(int template, Box area, float gap = 40, float topShare = .5f)
    {
        Box[] Grid(int cols, int rows)
        {
            float w = (area.Width - gap * (cols - 1)) / cols, h = (area.Height - gap * (rows - 1)) / rows;
            if (rows == 2 && Math.Abs(topShare - .5f) > .00001f)
            {
                float available = area.Height - gap, min = Math.Min(180, available * .5f);
                int top = (int)Math.Clamp(available * Math.Clamp(topShare, .1f, .9f), min, available - min);
                return Enumerable.Range(0, cols * rows).Select(i => new Box((int)(area.X + i % cols * (w + gap)), (int)(i < cols ? area.Y : area.Y + top + gap), (int)w, (int)(i < cols ? top : available - top))).ToArray();
            }
            return Enumerable.Range(0, cols * rows).Select(i => new Box((int)(area.X + i % cols * (w + gap)), (int)(area.Y + i / cols * (h + gap)), (int)w, (int)h)).ToArray();
        }
        if (template is 6 or 7)
        {
            var grid = Grid(3, 2);
            return [.. grid.Take(3), template == 6 ? new(area.X, grid[3].Y, area.Width, grid[3].Height) : grid[4]];
        }
        if (template is 8 or 9)
        {
            var grid = Grid(2, 2);
            return template == 8 ? [grid[0], grid[1], new(area.X, grid[2].Y, area.Width, grid[2].Height)] : [new(area.X, area.Y, area.Width, grid[0].Height), grid[2], grid[3]];
        }
        return template switch
        {
            0 => Grid(2, 2),
            1 => Grid(1, 1),
            2 => Grid(2, 1),
            3 => Grid(1, 2),
            4 => Grid(3, 1),
            5 => Grid(1, 3),
            10 => Grid(3, 2),
            11 => Grid(2, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(template))
        };
    }


    public static int[] Cameras(PrintOptions options)
    {
        var layout = options.Boxes!;
        int n = Cells(layout.Template, PageLayout.Drawing(options)).Length;
        return layout.Assignments.Take(n).Where(id => id >= 0).ToArray();
    }


    public static Box? EmptyFrame(PrintOptions options, Vector2 point)
    {
        if (options.Boxes is not { } layout)
            return null;
        var cells = Cells(layout, PageLayout.Drawing(options));
        for (int i = 0; i < cells.Length; i++)
            if (layout.Assignments[i] < 0 && cells[i].Contains(point))
                return cells[i];
        return null;
    }


    public static ViewRegion[] Plan(Vector3[] points, Vector3 center, PrintOptions options, float fraction)
    {
        var layout = options.Boxes!;
        var cells = Cells(layout, PageLayout.Drawing(options));
        // Fit only assigned cameras. Blanking remembers that box's previous
        // envelope until the user explicitly refits, preserving other views.
        var extents = Extents(points, center);
        float scale = RawFit(layout, cells, extents, options.Captions) * fraction;
        scale = PrintScale.FitScale(scale, options);
        return cells.Select((cell, index) => (cell, index, camera: layout.Assignments[index])).Where(p => p.camera >= 0).Select(p =>
        {
            if (p.camera > 4)
                throw new InvalidDataException("Unknown box view.");
            var cell = p.cell with
            {
                Height = p.cell.Height - (options.Captions ? ViewCaption.Band : 0)
            };
            if (cell.Width < 64 || cell.Height < 64)
                throw new InvalidOperationException("The selected box layout is too small for this paper.");
            var b = SheetLayout.Basis(p.camera);
            return new ViewRegion(100 + p.index, SheetLayout.ViewName(p.camera), cell, new((int)cell.Width, (int)cell.Height, scale, center, b.Right, b.Up, b.Toward), p.camera);
        }).ToArray();
    }


    static (float X, float Y)[] Extents(Vector3[] points, Vector3 center) =>
        Enumerable.Range(0, 5).Select(camera =>
        {
            var basis = SheetLayout.Basis(camera);
            return (X: points.Max(v => Math.Abs(Vector3.Dot(v - center, basis.Right))) * 2, Y: points.Max(v => Math.Abs(Vector3.Dot(v - center, basis.Up))) * 2);
        }).ToArray();


    static float RawFit(FixedViewLayout layout, Box[] cells, (float X, float Y)[] extents, bool captions)
    {
        var views = layout.FittingViews;
        return cells.Select((cell, i) => (cell, view: views[i])).Where(p => p.view >= 0).Select(p =>
            Math.Min((p.cell.Width - 32) / Math.Max(.001f, extents[p.view].X), (p.cell.Height - (captions ? ViewCaption.Band : 0) - 32) / Math.Max(.001f, extents[p.view].Y))).DefaultIfEmpty(1).Min();
    }


    public static PrintOptions Resolve(Capture capture, PrintOptions options) => options.Boxes?.AutoRows == true
        ? options with
        {
            Boxes = FitRows(capture, options, false)
        } : options;


    public static FixedViewLayout FitRows(Capture capture, PrintOptions options, bool clearBlankFits = true)
    {
        var layout = clearBlankFits ? options.Boxes!.ClearBlankFits() : options.Boxes!;
        if (!CanSplit(layout.Template))
            return layout;
        var points = capture.Meshes.SelectMany(m => m.Vertices).Select(ExplodedGeometry.World).ToArray();
        var center = (points.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min) + points.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max)) * .5f;
        var extents = Extents(points, center);
        var area = PageLayout.Drawing(options);
        var original = Cells(layout.Template, area);
        float topY = 0, bottomY = 0;
        var fitting = layout.FittingViews;
        for (int i = 0; i < original.Length; i++)
            if (fitting[i] >= 0)
            {
                float y = extents[fitting[i]].Y;
                if (original[i].Y == area.Y)
                    topY = Math.Max(topY, y);
                else
                    bottomY = Math.Max(bottomY, y);
            }
        float preferred = topY + bottomY > 0 ? topY / (topY + bottomY) : .5f, best = -1;
        var result = layout;
        for (int percent = 10; percent <= 90; percent++)
        {
            var candidate = layout with
            {
                TopShare = percent / 100f
            };
            float score = RawFit(candidate, Cells(candidate, area), extents, options.Captions);
            if (score > best + 1e-5f || Math.Abs(score - best) <= 1e-5f && Math.Abs(candidate.TopShare - preferred) < Math.Abs(result.TopShare - preferred))
            {
                best = score;
                result = candidate;
            }
        }
        return result;
    }
}
