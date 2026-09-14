using Godot;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private ChestStack? ReadOwnedStack(string source)
    {
        BuildingItems(); bool bar=source.StartsWith("$hotbar:");
        if(!bar && !source.StartsWith("$slot:"))return null;
        if(!int.TryParse(source[(bar?8:6)..],out int index) || index<0 || index>=(bar?9:state.BuildingSlots.Count))return null;
        string? token=bar?state.HotbarStacks[index]:state.BuildingSlots[index];if(token==null)return null;
        int split=token.LastIndexOf(':');string key=token[..split];int stack=int.Parse(token[(split+1)..]);
        if(ResolveSupply(key) is not {} item)return null;
        int count=item.Id==null?Math.Min(64,state.Supplies.GetValueOrDefault(item.Kind)-64*stack):1;
        return count>0?new ChestStack {Kind=item.Kind,ItemId=item.Id,Count=count}:null;
    }
    private void RemoveOwnedStack(string source,ChestStack item)
    {
        bool bar=source.StartsWith("$hotbar:");int index=int.Parse(source[(bar?8:6)..]);
        string token=(bar?state.HotbarStacks[index]:state.BuildingSlots[index])!;
        int split=token.LastIndexOf(':');string key=token[..split];int stack=int.Parse(token[(split+1)..]);
        if(bar)state.HotbarStacks[index]=null;else state.BuildingSlots[index]=null;
        if(item.ItemId==null)
        {
            state.Supplies[item.Kind]-=item.Count;
            string? Shift(string? t) {if(t==null || !t.StartsWith(key+":"))return t;int n=int.Parse(t[(key.Length+1)..]);return n>stack?key+":"+(n-1):t;}
            for(int i=0;i<9;i++)state.HotbarStacks[i]=Shift(state.HotbarStacks[i]);
            for(int i=0;i<state.BuildingSlots.Count;i++)state.BuildingSlots[i]=Shift(state.BuildingSlots[i]);
        }
        UpdateHotbarKeys();
    }
    private void StoreChestStack(string id,int index,string source)
    {
        try
        {
            var chest=state.Chests.Single(c=>c.Id==id);if(index<0 || index>=27)return;
            while(chest.Items.Count<=index)chest.Items.Add(null);
            if(source.StartsWith("$chest:"))
            {
                var parts=source.Split(':');if(parts.Length!=3 || parts[1]!=id || !int.TryParse(parts[2],out int from) || from<0 || from>=chest.Items.Count)return;
                (chest.Items[from],chest.Items[index])=(chest.Items[index],chest.Items[from]);
            }
            else
            {
                var item=ReadOwnedStack(source);if(item==null)return;ChestStorage.ValidateItem(state,chest,item);
                var previous=chest.Items[index];RemoveOwnedStack(source,item);chest.Items[index]=item;
                if(previous?.ItemId==null && previous!=null)state.Supplies[previous.Kind]=state.Supplies.GetValueOrDefault(previous.Kind)+previous.Count;
                if(previous!=null)PlaceReturnedStack(previous,source);
            }
            CancelPlacement();BuildingItems();Changed();RefreshHotbar();ShowItemChest(id);
        }catch(Exception e){Toast(e.Message);}
    }
    private void TakeChestStack(string id,int index,string? target=null)
    {
        try
        {
            var chest=state.Chests.Single(c=>c.Id==id);if(index<0 || index>=chest.Items.Count || chest.Items[index] is not {} item)return;
            ChestStack? exchange=target==null?null:ReadOwnedStack(target);
            if(exchange!=null){ChestStorage.ValidateItem(state,chest,exchange);RemoveOwnedStack(target!,exchange);}
            chest.Items[index]=exchange;
            if(item.ItemId==null)state.Supplies[item.Kind]=state.Supplies.GetValueOrDefault(item.Kind)+item.Count;
            BuildingItems();
            if(target!=null)
            {
                PlaceReturnedStack(item,target);
            }
            Changed();RefreshHotbar();ShowItemChest(id);
        }catch(Exception e){Toast(e.Message);}
    }
    private void PlaceReturnedStack(ChestStack item,string target)
    {
        var items=BuildingItems();string key=item.ItemId??item.Kind;
        int from=items.FindLastIndex(i=>(i.Id??i.Kind)==key);
        string source=from>=0?"$slot:"+from:"$hotbar:"+Array.LastIndexOf(state.Hotbar,key);
        if(target.StartsWith("$hotbar:"))DropHotbarItem(source,int.Parse(target[8..]));
        else DropBuildingItem(source,int.Parse(target[6..]));
    }
    private void ShowItemChest(string id)
    {
        var chest=state.Chests.Single(c=>c.Id==id);
        if(!ChestStorage.AcceptsItems(state,id)){Toast("This chest holds files. Empty it first to store Minecraft items.");return;}
        bool fresh=openedChest!=id;ClosePanel(false);openedChest=id;folderPath=null;walking=false;backgroundApp=false;Input.MouseMode=Input.MouseModeEnum.Visible;
        if(bridge.Connected)bridge.Send(new {command="enter"});if(fresh)PlayEffect("open");
        var root=new Control {Theme=uiTheme};layer.AddChild(root);root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);panel=root;
        var shade=new ColorRect {Color=new Color(0,0,0,.58f)};root.AddChild(shade);shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center=new CenterContainer();root.AddChild(center);center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var stack=new VBoxContainer();center.AddChild(stack);float s=Mathf.Clamp(Mathf.Floor((GetViewport().GetVisibleRect().Size.Y-150)/166),2,4);
        var frame=new ChestFrame {PixelScale=s,CustomMinimumSize=new Vector2(176,166)*s};stack.AddChild(frame);
        var title=new LineEdit {Name="ChestName",Text=chest.Name,Position=new Vector2(8,3)*s,Size=new Vector2(150,12)*s,MaxLength=24};frame.AddChild(title);
        title.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());title.AddThemeFontOverride("font",MinecraftFont());title.AddThemeFontSizeOverride("font_size",(int)(8*s));title.AddThemeColorOverride("font_color",new Color("404040"));
        title.TextSubmitted+=v=>{RenameChest(id,v);title.ReleaseFocus();};title.FocusExited+=()=>RenameChest(id,title.Text);
        for(int i=0;i<27;i++)
        {
            int index=i;var slot=new ChestSlot {Name="ItemChestSlot"+i,Container="$building",Position=new Vector2(8+i%9*18,18+i/9*18)*s,Size=Vector2.One*18*s,PixelScale=s};frame.AddChild(slot);
            var item=i<chest.Items.Count?chest.Items[i]:null;
            if(item!=null){slot.Icon=SupplyIcon(item.Kind,item.ItemId);slot.Count=item.Count;slot.BuildKey=item.ItemId??item.Kind;slot.BuildDragKey="$chest:"+id+":"+i;slot.TooltipText=state.Chests.FirstOrDefault(c=>c.Id==item.ItemId)?.Name??BlockName(item.Kind);}
            slot.BuildDropped=source=>StoreChestStack(id,index,source);slot.Activated=(button,_,_)=>{if(button==MouseButton.Left)TakeChestStack(id,index);};
        }
        var label=new Label {Text="Inventory",Position=new Vector2(8,73)*s};label.AddThemeFontOverride("font",MinecraftFont());label.AddThemeFontSizeOverride("font_size",(int)(8*s));label.AddThemeColorOverride("font_color",new Color("404040"));frame.AddChild(label);
        var items=BuildingItems();var rows=new List<ChestSlot>();
        for(int i=0;i<27;i++){var slot=new ChestSlot {Name="ChestInventory"+i,Position=new Vector2(8+i%9*18,84+i/9*18)*s,Size=Vector2.One*18*s,PixelScale=s};frame.AddChild(slot);rows.Add(slot);}
        FillBuildingSlots(rows);
        void BindTransfer(ChestSlot slot,string target,Action<string> normal)
        {
            slot.BuildDropped=source=>{if(source.StartsWith("$chest:"+id+":"))TakeChestStack(id,int.Parse(source[(id.Length+8)..]),target);else{normal(source);ShowItemChest(id);}};
            slot.Activated=(b,_,_)=>{if(b!=MouseButton.Left)return;int empty=chest.Items.FindIndex(x=>x==null);if(empty<0)empty=chest.Items.Count;if(empty>=27){Toast("Chest is full.");return;}StoreChestStack(id,empty,target);};
        }
        for(int i=0;i<27;i++){int index=buildingPage*27+i;BindTransfer(rows[i],"$slot:"+index,source=>DropBuildingItem(source,index));}
        for(int i=0;i<9;i++){int index=i;var slot=new ChestSlot {Name="ChestHotbar"+i,Container="$building",Position=new Vector2(8+i*18,142)*s,Size=Vector2.One*18*s,PixelScale=s};frame.AddChild(slot);BindHotbarSupply(slot,i);BindTransfer(slot,"$hotbar:"+i,source=>DropHotbarItem(source,index));}
        var footer=new HBoxContainer();stack.AddChild(footer);
        footer.AddChild(Button("< Inventory",()=>{buildingPage=Math.Max(0,buildingPage-1);ShowItemChest(id);}));
        var pager=new Label {Text=$"{buildingPage+1}/{Math.Max(1,(items.Count+26)/27)}",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,HorizontalAlignment=HorizontalAlignment.Center};pager.AddThemeColorOverride("font_color",Colors.White);footer.AddChild(pager);
        footer.AddChild(Button("Inventory >",()=>{buildingPage=Math.Min(Math.Max(0,(items.Count-1)/27),buildingPage+1);ShowItemChest(id);}));
        if(!ChestStorage.HasItems(chest))footer.AddChild(Button("Store files",()=>ShowChestPage(id,null,0)));
        var help=new Label {Text="Drag stacks to swap · Click to transfer · This chest stores Minecraft items",HorizontalAlignment=HorizontalAlignment.Center};help.AddThemeColorOverride("font_color",Colors.White);stack.AddChild(help);
        statusLabel.Visible=false;hudToolbar.Visible=false;
    }
}
