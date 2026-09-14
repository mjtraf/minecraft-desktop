using Godot;
using Cave.Core;
using System.Text.Json;
namespace CozyCave;
public partial class Main
{
    private string workbenchQuery="";
    private int workbenchPage;
    private readonly Dictionary<string,string> catalogTextures=[];
    private void LoadBlockCatalog()
    {
        using var json=JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Vanilla/block-catalog.json"));
        foreach(var entry in json.RootElement.EnumerateArray())
        {
            string id=entry.GetProperty("Id").GetString()!;
            if(BlockNames.ContainsKey(id) || new[]{"oak_planks","oak_log","bricks","oak_leaves","moss_block"}.Contains(id)) continue;
            BlockNames[id]=entry.GetProperty("Name").GetString()!;ShapedBlocks.Add(id);catalogTextures[id]=entry.GetProperty("Texture").GetString()!;
        }
    }
    private void ShowWorkbench()
    {
        var box=OpenPanel("Block catalog","Drag a block into your inventory or hotbar. Click a block to take 64.");
        ((PanelContainer)box.GetParent().GetParent()).CustomMinimumSize=new Vector2(660,0);
        var displays=new HFlowContainer();box.AddChild(displays);
        displays.AddChild(Button("TV · Black Concrete",()=>{AddScreenItem("tv");ShowWorkbench();}));
        displays.AddChild(Button("Computer · Black Concrete",()=>{AddScreenItem("desktop");ShowWorkbench();}));
        displays.AddChild(Button("Project Board · Bookshelf",()=>{AddProjectBoard();ShowWorkbench();}));
        var search=new LineEdit {Name="BlockSearch",Text=workbenchQuery,PlaceholderText="Search blocks...",ClearButtonEnabled=true};box.AddChild(search);
        var grid=new GridContainer {Name="BlockCatalog",Columns=9};grid.AddThemeConstantOverride("h_separation",0);grid.AddThemeConstantOverride("v_separation",0);box.AddChild(grid);
        var pager=new HBoxContainer();box.AddChild(pager);var count=new Label();Button? prev=null,next=null;
        var supplies=new List<ChestSlot>();var bindings=new List<ChestSlot>();
        void Refresh() {FillBuildingSlots(supplies,Refresh);for(int i=0;i<9;i++) BindHotbarSupply(bindings[i],i);RefreshHotbar();}
        void Fill()
        {
            foreach(var child in grid.GetChildren()) {grid.RemoveChild(child);child.QueueFree();}
            var items=BlockNames.Where(p=>p.Key!="writable_book" && (p.Key.Contains(workbenchQuery,StringComparison.OrdinalIgnoreCase)||p.Value.Contains(workbenchQuery,StringComparison.OrdinalIgnoreCase))).OrderBy(p=>p.Value).ToArray();
            int pages=Math.Max(1,(items.Length+26)/27);workbenchPage=Math.Clamp(workbenchPage,0,pages-1);count.Text=$"Page {workbenchPage+1} / {pages}";if(prev!=null) prev.Disabled=workbenchPage==0;if(next!=null) next.Disabled=workbenchPage==pages-1;
            foreach(var item in items.Skip(workbenchPage*27).Take(27))
            {
                string kind=item.Key;var slot=new ChestSlot {CustomMinimumSize=new Vector2(54,54),PixelScale=3,Icon=ItemIcon(kind),BuildKind=kind,BuildKey=kind,BuildDragKey="$catalog:"+kind,Container="$building",TooltipText=item.Value+"\n"+BlockBehavior(kind)};grid.AddChild(slot);
                slot.Activated=(button,_,_)=>{if(button==MouseButton.Left) {ReceiveBuildingDrag("$catalog:"+kind);Refresh();}};
            }
        }
        prev=Button("<",()=>{workbenchPage--;Fill();});next=Button(">",()=>{workbenchPage++;Fill();});pager.AddChild(prev);pager.AddChild(count);pager.AddChild(next);
        box.AddChild(new Label {Text="Inventory"});var inventory=new GridContainer {Columns=9};inventory.AddThemeConstantOverride("h_separation",0);inventory.AddThemeConstantOverride("v_separation",0);box.AddChild(inventory);
        for(int i=0;i<27;i++) {var slot=new ChestSlot {Name="CatalogInventory"+i,CustomMinimumSize=new Vector2(54,54),PixelScale=3,Container="$building"};inventory.AddChild(slot);supplies.Add(slot);}
        var bar=new HBoxContainer();bar.AddThemeConstantOverride("separation",0);box.AddChild(bar);
        for(int i=0;i<9;i++) {int index=i;var slot=new ChestSlot {Name="CatalogHotbar"+i,CustomMinimumSize=new Vector2(54,54),PixelScale=3,Container="$building"};bar.AddChild(slot);bindings.Add(slot);slot.BuildDropped=key=>{DropHotbarItem(key,index);Refresh();};}
        var footer=new HBoxContainer();box.AddChild(footer);footer.AddChild(Button("Inventory <",()=>{buildingPage=Math.Max(0,buildingPage-1);Refresh();}));footer.AddChild(Button("Inventory >",()=>{buildingPage=Math.Min((BuildingItems().Count-1)/27,buildingPage+1);Refresh();}));
        search.TextChanged+=q=>{workbenchQuery=q;workbenchPage=0;Fill();};Fill();Refresh();
    }    private static string BlockBehavior(string kind) => kind switch
    {
        "item_frame"=>"Place on a wall; right-click to choose a personal photo",
        "jukebox"=>"Right-click: music playlist; hold left-click to collect",
        "chest"=>"File storage; retains links when carried",
        "crafting_table"=>"Right-click: open block workbench",
        "spruce_trapdoor"=>"Right-click: open/close; collision follows state",
        "campfire"=>"Animated fire and light; survival interactions not implemented",
        _=>"Building model only; no redstone, crafting, drops or machine simulation"
    };
    private bool InteractBlock(string id)
    {
        var target=TargetShape(id);if(target==null) return false;
        if(SitOn(id))return true;
        if(target.Value.Kind=="writable_book") {ShowNotebook();return true;}
        if(target.Value.Kind=="jukebox") {ShowMusic();return true;}
        if(target.Value.Kind=="item_frame") {ShowPictureFrame(id);return true;}
        if(target.Value.Kind=="crafting_table") {ShowWorkbench();return true;}
        if(target.Value.Kind != "spruce_trapdoor") return false;
        var block=state.BuildingBlocks.FirstOrDefault(b=>"$block:"+b.Id==id);
        if(block==null && id.StartsWith("$terrain:"))
        {
            var t=target.Value;state.RemovedTerrain.Add(id[9..]);
            block=new BuildingBlock {Kind=t.Kind,X=(int)t.At.X,Y=(int)Mathf.Floor(t.At.Y),Z=(int)t.At.Z,Rotation=t.Rotation};state.BuildingBlocks.Add(block);
        }
        if(block==null) return false;
        block.Active=!block.Active;Changed();BuildWorld();PlayEffect(block.Active?"trapdoor_open":"trapdoor_close");return true;
    }
}
