namespace Cave.Core;

public readonly record struct WaterCell(int X, int Y, int Z);

// One cellular update per water tick. Sources persist, streams thin with distance,
// gravity wins over sideways spread, and disconnected flow drains away.
public sealed class WaterFlow
{
    public Dictionary<WaterCell, int> Cells { get; private set; } = [];
    public HashSet<WaterCell> Sources { get; } = [];
    public bool Step(Func<WaterCell, bool> solid)
    {
        var next = new Dictionary<WaterCell, int>();
        bool Valid(WaterCell c) => c.X is >= -9 and <= 9 && c.Z is >= -9 and <= 9 && c.Y is >= -1 and <= 5 && !solid(c);
        void Put(WaterCell c, int level)
        { if (Valid(c) && (!next.TryGetValue(c, out int old) || level < old)) next[c] = level; }
        foreach (var source in Sources) Put(source, 0);
        foreach (var (cell, level) in Cells)
        {
            if (!Valid(cell)) continue;
            var below = cell with { Y = cell.Y - 1 };
            if (Valid(below)) { Put(below, 0); continue; }
            if (level >= 7) continue;
            Put(cell with { X = cell.X - 1 }, level + 1);
            Put(cell with { X = cell.X + 1 }, level + 1);
            Put(cell with { Z = cell.Z - 1 }, level + 1);
            Put(cell with { Z = cell.Z + 1 }, level + 1);
        }
        bool changed = Cells.Count != next.Count || Cells.Any(p => !next.TryGetValue(p.Key, out int level) || level != p.Value);
        Cells = next; return changed;
    }
}
