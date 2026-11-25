using System.Collections.Immutable;

namespace Blueprint.Rendering;

readonly record struct SetupSlot(int Camera, int Cell, bool Detached = false, ViewAxes? Axes = null);

// Editing this small layout never touches the current drawing or the live vehicle.
sealed record SetupViewGrid(int Columns, int Rows, int Angle, int? Primary, ImmutableArray<SetupSlot> Slots)
{
    public static SetupViewGrid Empty(int angle) => new(2, 2, angle, null, []);
    static readonly (int Columns, int Rows)[] automaticSizes = [(2, 2), (3, 2), (2, 3), (3, 3), (4, 3)];
    static readonly int[] orthoOrder = [1, 2, 6, 3, 7, 5];
    static readonly int[,] directions = BuildDirections();
    static int[,] BuildDirections()
    {
        var result = new int[8, 8];
        foreach (int camera in orthoOrder)
        {
            var source = new DrawingView(0, camera, 0, 0);
            foreach (int other in orthoOrder) result[camera, other] = -1;
            for (int d = 0; d < 4; d++) result[camera, DrawingPlacements.Roll(source, source, d, 3).Camera] = d;
        }
        return result;
    }

    static bool Linked(SetupSlot slot) => StarterDrawings.Ortho(slot.Camera) && !slot.Detached;
    static int FaceDirection(int root, int camera, int angle)
    {
        int direction = directions[root, camera];
        return direction < 0 || angle == 3 ? direction : DrawingPlacements.OppositeDirection(direction);
    }

    // A direct face shares the correct axis. The opposite face can sit beyond
    // its neighbour on that axis and still share the primary's physical scale.
    static bool OnAxis(int dx, int dy, int direction, int root) => direction switch
    {
        0 => dx > 0 && dy == 0, 1 => dy < 0 && dx == 0,
        2 => dx < 0 && dy == 0, 3 => dy > 0 && dx == 0,
        _ => dx != 0 && dy == 0 || root is 3 or 7 && dx == 0 && dy != 0
    };

    static bool Fits(SetupSlot[] slots, int columns, int angle, int root)
    {
        var anchor = slots.First(s => s.Camera == root);
        Span<int> direct = stackalloc int[4];
        int farDirection = -1, farDistance = 0;
        foreach (var slot in slots)
        {
            if (slot.Camera == root || !Linked(slot)) continue;
            int dx = slot.Cell % columns - anchor.Cell % columns, dy = slot.Cell / columns - anchor.Cell / columns;
            int side = FaceDirection(root, slot.Camera, angle);
            if (!OnAxis(dx, dy, side, root)) return false;
            int distance = Math.Abs(dx) + Math.Abs(dy);
            if (side >= 0) direct[side] = distance;
            else { farDirection = dx > 0 ? 0 : dy < 0 ? 1 : dx < 0 ? 2 : 3; farDistance = distance; }
        }
        return farDirection < 0 || farDistance > direct[farDirection];
    }

    public SetupViewGrid? Arrange(int[] cameras, int columns, int rows, int angle, int? preferred)
    {
        var old = Slots.ToDictionary(s => s.Camera);
        var slots = cameras.Select(camera => old.TryGetValue(camera, out var slot) ? slot : new SetupSlot(camera, -1)).ToArray();
        if (preferred is int chosen)
        {
            int index = Array.FindIndex(slots, s => s.Camera == chosen);
            if (index >= 0 && slots[index].Detached) slots[index] = new(chosen, slots[index].Cell);
        }
        if (columns > 0) return Pack(slots, columns, rows, angle, preferred);
        foreach (var size in automaticSizes)
        {
            var next = Pack(slots, size.Columns, size.Rows, angle, preferred);
            if (next != null) return next;
        }
        return null;
    }

    SetupViewGrid? Pack(SetupSlot[] requested, int columns, int rows, int angle, int? preferred)
    {
        int capacity = columns * rows;
        if (requested.Length > capacity) return null;
        var slots = requested.ToArray();
        var linked = Enumerable.Range(0, slots.Length).Where(i => Linked(slots[i])).ToArray();
        var loose = Enumerable.Range(0, slots.Length).Where(i => !Linked(slots[i])).ToArray();
        var roots = linked.OrderBy(i => Array.IndexOf(orthoOrder, slots[i].Camera))
            .OrderByDescending(i => linked.Count(j => directions[slots[i].Camera, slots[j].Camera] >= 0))
            .Where(i => preferred == null || preferred == slots[i].Camera).ToArray();
        SetupViewGrid? best = null;
        int bestCost = int.MaxValue;

        int Distance(int a, int b, int oldColumns) =>
            Math.Abs(a % oldColumns - b % columns) + Math.Abs(a / oldColumns - b / columns);
        int Shift(int i, int cell) => requested[i].Cell < 0 ? 0 : Distance(requested[i].Cell, cell, Columns);

        void Finish(int? root, int occupied, int cost)
        {
            if (cost >= bestCost || root is int camera && !Fits(slots, columns, angle, camera)) return;
            foreach (int i in loose) slots[i] = requested[i] with { Cell = -1 };
            // Reserve every surviving independent view's cell before filling any vacancies.
            foreach (int i in loose)
            {
                int from = requested[i].Cell;
                if (from < 0 || from % Columns >= columns || from / Columns >= rows) continue;
                int cell = from % Columns + from / Columns * columns;
                if ((occupied & (1 << cell)) != 0) continue;
                slots[i] = slots[i] with { Cell = cell };
                occupied |= 1 << cell;
            }
            foreach (int i in loose)
            {
                if (slots[i].Cell >= 0) continue;
                int cell = Enumerable.Range(0, capacity).Where(k => (occupied & (1 << k)) == 0).MinBy(k => Shift(i, k));
                cost += 20 * Shift(i, cell);
                slots[i] = slots[i] with { Cell = cell };
                occupied |= 1 << cell;
            }
            if (cost >= bestCost) return;
            bestCost = cost;
            best = new(columns, rows, angle, root, [.. slots]);
        }

        if (linked.Length == 0) Finish(null, 0, 0);
        foreach (int rootIndex in roots)
        {
            int root = slots[rootIndex].Camera;
            var children = linked.Where(i => i != rootIndex).ToArray();
            void Place(int index, int anchor, int occupied, int cost)
            {
                if (cost >= bestCost) return;
                if (index == children.Length) { Finish(root, occupied, cost); return; }
                int i = children[index], side = FaceDirection(root, slots[i].Camera, angle);
                for (int cell = 0; cell < capacity; cell++)
                {
                    if ((occupied & (1 << cell)) != 0) continue;
                    int dx = cell % columns - anchor % columns, dy = cell / columns - anchor / columns;
                    if (!OnAxis(dx, dy, side, root)) continue;
                    slots[i] = slots[i] with { Cell = cell };
                    Place(index + 1, anchor, occupied | (1 << cell), cost + 30 * Shift(i, cell) + Math.Abs(dx) + Math.Abs(dy));
                }
            }
            for (int anchor = 0; anchor < capacity; anchor++)
            {
                slots[rootIndex] = slots[rootIndex] with { Cell = anchor };
                Place(0, anchor, 1 << anchor, 60 * Shift(rootIndex, anchor));
            }
        }
        return best;
    }

    public int IndexAt(int value, bool byCamera = false)
    {
        for (int i = 0; i < Slots.Length; i++)
            if ((byCamera ? Slots[i].Camera : Slots[i].Cell) == value) return i;
        return -1;
    }

    public SetupViewGrid? Move(int camera, int destination, out bool needsUnlink)
    {
        needsUnlink = false;
        int index = IndexAt(camera, byCamera: true);
        if (index < 0 || destination < 0 || destination >= Columns * Rows) return null;
        var source = Slots[index];
        if (source.Cell == destination) return this;
        int occupant = IndexAt(destination);
        var moved = Slots.ToArray();
        if (!Linked(source))
        {
            if (occupant >= 0 && Linked(Slots[occupant])) return null;
            if (occupant >= 0) moved[occupant] = moved[occupant] with { Cell = source.Cell };
            moved[index] = source with { Cell = destination };
        }
        else if (camera == Primary)
        {
            int dx = destination % Columns - source.Cell % Columns, dy = destination / Columns - source.Cell / Columns;
            for (int i = 0; i < moved.Length; i++)
            {
                if (!Linked(moved[i])) continue;
                int x = moved[i].Cell % Columns + dx, y = moved[i].Cell / Columns + dy;
                if (x < 0 || x >= Columns || y < 0 || y >= Rows) return null;
                moved[i] = moved[i] with { Cell = x + y * Columns };
            }
            if (moved.Select(s => s.Cell).Distinct().Count() != moved.Length) return null;
        }
        else
        {
            if (occupant >= 0 && Linked(Slots[occupant])) return null;
            moved[index] = source with { Cell = destination };
            if (!Fits(moved, Columns, Angle, Primary!.Value))
            {
                needsUnlink = true;
                moved[index] = moved[index] with { Detached = true, Axes = Axes(source) };
            }
            else if (occupant >= 0) return null;
            if (occupant >= 0) moved[occupant] = moved[occupant] with { Cell = source.Cell };
        }
        return this with { Slots = [.. moved] };
    }

    internal IEnumerable<(SetupSlot From, SetupSlot To)> ProjectionGuides()
    {
        if (Primary is not int camera) yield break;
        var root = Slots[IndexAt(camera, byCamera: true)];
        int Direction(SetupSlot slot)
        {
            int dx = slot.Cell % Columns - root.Cell % Columns;
            if (dx != 0) return dx < 0 ? 2 : 0;
            return slot.Cell < root.Cell ? 1 : 3;
        }
        int Distance(SetupSlot slot) => Math.Abs(slot.Cell % Columns - root.Cell % Columns) +
            Math.Abs(slot.Cell / Columns - root.Cell / Columns);

        // Chain views on the same ray so an opposite face beyond another view
        // has a clear guide through the gaps without crossing the nearer silhouette.
        foreach (var ray in Slots.Where(s => s.Camera != camera && Linked(s)).GroupBy(Direction))
        {
            var from = root;
            foreach (var to in ray.OrderBy(Distance))
            {
                yield return (from, to);
                from = to;
            }
        }
    }

    DrawingView? Projection(SetupSlot slot)
    {
        if (Primary is not int root || !Linked(slot) || root == slot.Camera) return null;
        int d = FaceDirection(root, slot.Camera, Angle);
        if (d < 0) return null;
        return DrawingPlacements.Roll(new(0, slot.Camera, 0, 0), new(0, root, 0, 0), d, Angle);
    }

    public ViewAxes? Axes(SetupSlot slot) => slot.Axes ?? Projection(slot)?.Axes;

    public LayoutTemplate ToTemplate()
    {
        int Id(int camera) => camera == 8 ? 1001 : 100 + camera;
        int? group = Primary is int root && Slots.Count(Linked) > 1 ? Id(root) : null;
        var views = Slots.Select(slot =>
        {
            bool member = group.HasValue && Linked(slot);
            var roll = Projection(slot);
            var box = new Box((slot.Cell % Columns + .04f) / Columns, (slot.Cell / Columns + .04f) / Rows, .92f / Columns, .92f / Rows);
            return new ViewDefinition(Id(slot.Camera), slot.Camera == 8 ? 4 : slot.Camera, box, 100,
                Parent: member && slot.Camera != Primary ? group : null, Group: member ? group : null,
                Exploded: slot.Camera == 8, Axes: slot.Axes ?? roll?.Axes, Alignment: roll?.Alignment ?? 0,
                RollDirection: roll?.RollDirection ?? -1, ProjectionAngle: Angle);
        }).ToImmutableArray();
        ImmutableArray<ProjectionGroup> groups = group is int id
            ? [new(id, id, [.. views.Where(v => v.Group == id).Select(v => v.Id)], 100)] : [];
        return new(1, "pawprints.layout", new(views, groups, [.. views.Select(v => new DrawingObject(DrawingObjectKind.View, v.Id))]));
    }
}
