namespace Cave.Core;

public static class ChestStorage
{
    public static bool HasItems(Chest chest) => chest.Items.Any(s => s != null);
    public static bool AcceptsFiles(CaveState state, string id) => id == LinkLibrary.Inventory || !HasItems(state.Chests.Single(c => c.Id == id));
    public static bool AcceptsItems(CaveState state, string id) => id != "inbox" && !state.Links.Any(l => l.ChestId == id);
    public static bool IsStored(CaveState state, string id) => state.Chests.Any(c => c.Items.Any(s => s?.ItemId == id));
    public static void ValidateItem(CaveState state, Chest target, ChestStack stack)
    {
        if (!AcceptsItems(state, target.Id)) throw new InvalidOperationException("This is a file chest. Empty it before storing Minecraft items. Inbox always holds files.");
        if (stack.Count < 1 || stack.Count > 64) throw new InvalidOperationException("Invalid item stack.");
        var visited = new HashSet<string>();
        bool Contains(string id)
        {
            if (id == target.Id) return true;
            if (!visited.Add(id)) return false;
            return state.Chests.FirstOrDefault(c => c.Id == id)?.Items.Any(s => s?.ItemId is {} child && Contains(child)) == true;
        }
        if (stack.ItemId is {} item && Contains(item)) throw new InvalidOperationException("A chest cannot contain itself or a chest that contains it.");
    }
    public static void ReturnItems(CaveState state, Chest chest)
    {
        foreach (var stack in chest.Items.OfType<ChestStack>())
            if (stack.ItemId == null) state.Supplies[stack.Kind] = state.Supplies.GetValueOrDefault(stack.Kind) + stack.Count;
        chest.Items.Clear(); // Identified chests/decorations are already carried; clearing releases them.
    }
}
