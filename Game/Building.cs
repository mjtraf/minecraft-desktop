using Godot;
using Cave.Core;

namespace CozyCave;
public partial class Main
{
    private readonly Dictionary<string, (string Kind, Vector3 Position)> terrain = [];
    private string? breakingId;
    private float breakTime;
    private int buildingPage;
    private void TerrainBlock(string kind, Vector3 position)
    {
        string key = FormattableString.Invariant($"{position.X},{position.Y},{position.Z}");
        if (state.RemovedTerrain.Contains(key)) return;
        terrain[key] = (kind, position);
        Block(kind, position);
        Collider(world, position, Vector3.One, "$terrain:" + key);
    }
    private bool CanPickUp(string? id) => id != null && ((ParseValley(id,out var cell) && ValleyKind(cell) is not (null or "bedrock")) || terrain.ContainsKey(id.StartsWith("$terrain:") ? id[9..] : "") || state.Chests.Any(c => c.Id == id && !c.Carried) || state.Decorations.Any(d => "$decor:" + d.Id == id && !d.Carried) || state.BuildingBlocks.Any(b => "$block:" + b.Id == id));
    private void UpdateBreaking(float delta, bool pressed)
    {
        if (!pressed || !walking || panel != null || paused || locked || hidden || !CanPickUp(hovered)) { breakingId = null; breakTime = 0; ClearCracks(); return; }
        if (breakingId != hovered) { breakingId = hovered; breakTime = 0; }
        breakTime += delta;
        float duration = hovered!.StartsWith("$terrain:") || hovered.StartsWith("$block:") || hovered.StartsWith("$valley:") ? .85f : 1.2f;
        ShowCracks(hovered, breakTime / duration);
        miningDust += delta; SwingHand();
        if (miningDust > .18f && TargetShape(hovered) is {} dust) { EmitBlockDust(dust.Kind,dust.At,false); miningDust=0; }
        if (breakTime >= duration) { PickUpObject(hovered); breakingId = null; breakTime = 0; hovered = null; ClearCracks(); }
    }
    private void PickUpObject(string id)
    {
        if (TargetShape(id) is not {} target) return;
        if(ParseValley(id,out var valleyCell))
        {
            if(target.Kind=="bedrock")return;MineValley(valleyCell);
            var mined=new DroppedBlock {Kind=target.Kind,Position=[target.At.X,target.At.Y,target.At.Z]};
            state.Drops.Add(mined);SpawnDrop(mined);Changed();EmitBlockDust(target.Kind,target.At,true);PlayEffect("place");RefreshHotbar();return;
        }
        if(target.Kind=="jukebox") {music.Stop();playingPath=null;state.Settings.MusicEnabled=false;}
        string? itemId=null;
        if (state.Chests.FirstOrDefault(c => c.Id == id && !c.Carried) is { } chest) { chest.Carried = true; itemId=chest.Id; }
        else if (state.Decorations.FirstOrDefault(d => "$decor:" + d.Id == id && !d.Carried) is { } decor) { decor.Carried = true; itemId=decor.Id; if(decor.ScreenRole=="tv" && !state.Decorations.Any(d=>d.ScreenRole=="tv" && !d.Carried)){tvOn=false;TvCommand("power");} }
        else if (state.BuildingBlocks.FirstOrDefault(b => "$block:" + b.Id == id) is { } block) state.BuildingBlocks.Remove(block);
        else if (id.StartsWith("$terrain:")) state.RemovedTerrain.Add(id[9..]);
        else return;
        var drop=new DroppedBlock { Kind=target.Kind, ItemId=itemId, Position=[target.At.X,target.At.Y,target.At.Z] };
        state.Drops.Add(drop); SpawnDrop(drop); Changed(); BuildWorld(); EmitBlockDust(target.Kind,target.At,true); PlayEffect("place"); RefreshHotbar();
    }

    private List<(string Kind, string? Id, string Name, int Count)> BuildingItems()=>OrderBuildingItems(SupplyItems());
    private List<(string Kind, string? Id, string Name, int Count)> SupplyItems()
    {
        var items = new List<(string Kind,string? Id,string Name,int Count)>();
        foreach(var p in state.Supplies.Where(p=>p.Value>0 && BlockNames.ContainsKey(p.Key)))
            for(int left=p.Value;left>0;left-=64) items.Add((p.Key,null,BlockName(p.Key),Math.Min(left,64)));
        items.AddRange(state.Chests.Where(c => c.Carried && !ChestStorage.IsStored(state,c.Id) && !state.Drops.Any(d => d.ItemId == c.Id)).Select(c => ("chest", (string?)c.Id, c.Name + " · " + state.Links.Count(l => l.ChestId == c.Id) + " file links", 1)));
        items.AddRange(state.Decorations.Where(d => d.Carried && !ChestStorage.IsStored(state,d.Id) && !state.Drops.Any(drop => drop.ItemId == d.Id)).Select(d => (d.Kind, (string?)d.Id, d.ProjectBoard?"Project Board (Bookshelf)":d.ScreenRole!=null?(d.ScreenRole=="tv"?"TV":"Computer")+" (Black Concrete)":d.Kind=="item_frame" && d.PicturePath!=null?System.IO.Path.GetFileName(d.PicturePath)+" (photo frame)":d.Kind + " (picked up)", 1)));
        return items;
    }
    private void FillBuildingSlots(IReadOnlyList<ChestSlot> slots,Action? refresh=null)
    {
        var items = BuildingItems(); int pages = Math.Max(1, (items.Count + slots.Count - 1) / slots.Count);
        buildingPage = Math.Clamp(buildingPage, 0, pages - 1);
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i]; slot.Container = "$building"; slot.Activated = null; slot.Transferred = null;
            slot.Icon=null; slot.BuildKind=null; slot.BuildKey=null; slot.BuildDragKey=null; slot.Count=0; slot.TooltipText="Empty slot"; slot.QueueRedraw();
            int index = buildingPage * slots.Count + i;
            int target=index;slot.BuildDropped=key=>{DropBuildingItem(key,target);if(refresh!=null) refresh();else {FillBuildingSlots(slots);RefreshHotbar();}};
            if (index >= items.Count || items[index].Count==0) continue;
            var item = items[index];
            slot.Icon = SupplyIcon(item.Kind,item.Id); slot.BuildKind = item.Kind; slot.Count = item.Count; slot.BuildKey = item.Id ?? item.Kind;slot.BuildDragKey="$slot:"+index;
            slot.TooltipText = item.Name + "\nClick to equip · Right-click a block face to place";
            slot.Activated = (button, _, _) => { if (button == MouseButton.Left) { ClearHeld(); AssignSelected(item.Id ?? item.Kind); } };
            slot.QueueRedraw();
        }
    }
    private void RenameChest(string id, string value)
    {
        var name = value.Trim(); if (name.Length == 0) return;
        var chest = state.Chests.Single(c => c.Id == id);
        name = name.Length > 24 ? name[..24] : name;
        if (chest.Name == name) return;
        chest.Name = name;
        Changed(); BuildWorld();
    }
}
