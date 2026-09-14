using Godot;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private HotbarFrame? hotbar;
    private Label? equippedLabel;
    private readonly List<ChestSlot> hotbarSlots = [];
    private bool buildingInventoryOpen;
    private Node3D? hand;
    private float swingTime;
    private string? handKind = "uninitialized";
    private (string Kind,string? Id,string Name,int Count)? ResolveSupply(string? key)
    {
        if(key==null) return null;
        foreach(var item in SupplyItems()) if((item.Id??item.Kind)==key) return item;
        return null;
    }
    private void BuildHotbar()
    {
        hotbar=new HotbarFrame { Texture=Vanilla("gui/sprites/hud/hotbar"), Selection=Vanilla("gui/sprites/hud/hotbar_selection"), MouseFilter=Control.MouseFilterEnum.Ignore };
        layer.AddChild(hotbar);
        for(int i=0;i<9;i++)
        {
            var index=i; var slot=new ChestSlot { Ghost=true, PixelScale=3, Size=new Vector2(54,54), Position=new Vector2((3+i*20)*3,3), Container="$building" };
            slot.Activated=(button,_,_)=> {if(button==MouseButton.Left) SelectHotbar(index);};
            hotbar.AddChild(slot);hotbarSlots.Add(slot);
        }
        equippedLabel=new Label { HorizontalAlignment=HorizontalAlignment.Center, MouseFilter=Control.MouseFilterEnum.Ignore };
        equippedLabel.AddThemeFontOverride("font",GD.Load<FontFile>("res://Assets/Fonts/Pixel.ttf"));equippedLabel.AddThemeFontSizeOverride("font_size",12);
        equippedLabel.AddThemeColorOverride("font_shadow_color",new Color("222222"));equippedLabel.AddThemeConstantOverride("shadow_offset_x",2);equippedLabel.AddThemeConstantOverride("shadow_offset_y",2);
        layer.AddChild(equippedLabel);
        GetViewport().SizeChanged += LayoutHotbar; LayoutHotbar(); RefreshHotbar();
    }
    private void LayoutHotbar()
    {
        if(hotbar==null || equippedLabel==null) return;
        var size=GetViewport().GetVisibleRect().Size;
        hotbar.Size=new Vector2(182*3,22*3);hotbar.Scale=Vector2.One*DockScale;hotbar.Position=new Vector2((size.X-hotbar.Size.X*DockScale)/2,size.Y-69*DockScale);
        equippedLabel.Position=new Vector2(hotbar.Position.X-100,hotbar.Position.Y-30);equippedLabel.Size=new Vector2(hotbar.Size.X+200,24);
    }
    private void BindSupply(ChestSlot slot,string? key)
    {
        var item=ResolveSupply(key);slot.Icon=item is {} v ? SupplyIcon(v.Kind,v.Id):null;slot.BuildKind=item?.Kind;slot.BuildKey=key;slot.Count=item?.Count??0;slot.TooltipText=item?.Name??"Empty slot";slot.QueueRedraw();
    }
    private void RefreshHotbar()
    {
        if(hotbar==null) return;
        for(int i=0;i<9;i++) BindHotbarSupply(hotbarSlots[i],i);
        hotbar.Selected=state.SelectedSlot;hotbar.QueueRedraw();
        if(equippedLabel!=null) equippedLabel.Text=ResolveSupply(state.Hotbar[state.SelectedSlot])?.Name??"";
    }
    private void SelectHotbar(int index)
    {
        if(desktopHotbarActive) {desktopHotbarActive=false;RefreshDesktopHotbar();}
        state.SelectedSlot=(index+9)%9;
        var item=ResolveSupply(state.Hotbar[state.SelectedSlot]);
        if(item is {} v) BeginPlacement(v.Kind,v.Id); else {CancelPlacement();ClosePanel();Enter();}
        RefreshHotbar();Changed();
    }
    private void AssignSelected(string key)
    { DropHotbarItem(key,state.SelectedSlot);SelectHotbar(state.SelectedSlot); }
    private void AutoAssignHotbar(string key)
    {
        if(state.Hotbar.Contains(key)) return;
        int empty=Array.FindIndex(state.Hotbar,k=>ResolveSupply(k)==null);
        if(empty>=0) DropHotbarItem(key,empty);
    }
    private void RefreshHand()
    {
        string? visualKey=placingKind=="item_frame"?placingKind+":"+movingId:placingKind;
        if(camera==null || visualKey==handKind) return;
        handKind=visualKey;
        if(hand!=null) {hand.QueueFree();hand=null;}
        hand=new Node3D();camera.AddChild(hand);
        if(placingKind==null) { var arm=new Node3D {RotationDegrees=new Vector3(180,0,0)};hand.AddChild(arm);AtlasBox(arm,Vector3.Zero,new Vector3(4,12,4),new Vector2(40,16),Vanilla("entity/player/steve")); hand.Scale=Vector3.One*.5f; }
        else if(placingKind=="chest") {ChestVisual(hand);hand.Scale=Vector3.One*.3f;}
        else if(placingKind=="item_frame" && state.Decorations.FirstOrDefault(d=>d.Id==movingId) is {} photoFrame) {CreatePictureVisual(hand,photoFrame);hand.Scale=Vector3.One*.5f;}
        else if(placingKind=="writable_book") {BookVisual(hand);hand.Scale=Vector3.One*.65f;}
        else if(placingKind is "lantern") hand.AddChild(new Sprite3D {Texture=Vanilla("item/"+placingKind),PixelSize=.017f,TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest});
        else BlockVisual(hand,placingKind,-Vector3.Up*.13f,.26f);
        hand.Position=new Vector3(.43f,-.35f,-.65f);hand.RotationDegrees=new Vector3(0,placingKind=="item_frame"?155:-25,0);
    }
    private void SwingHand() {if(swingTime<=0) swingTime=.28f;}
    private void UpdateHand(float delta)
    {
        swingTime=Math.Max(0,swingTime-delta);
        if(hand!=null)
        {
            hand.Visible=walking && panel==null && !desktopHotbarActive && sittingOn==null;
            float amount=Mathf.Sin(swingTime/.28f*Mathf.Pi);
            hand.Position=new Vector3(.43f-amount*.2f,-.35f+amount*.1f,-.65f-amount*.14f);
            hand.RotationDegrees=new Vector3(-amount*35,(placingKind=="item_frame"?155:-25)-amount*20,-amount*30);
        }
        if(hotbar!=null) hotbar.Visible=panel==null && !desktopHotbarActive;
        if(equippedLabel!=null) equippedLabel.Visible=panel==null;
        hudToolbar.Visible=false;
    }
    private void ShowBuildingInventory()
    {
        ClosePanel(); walking=false;Input.MouseMode=Input.MouseModeEnum.Visible;
        if(bridge.Connected) bridge.Send(new{command="enter"});
        buildingInventoryOpen=true;
        var root=new GroundDropTarget {Theme=uiTheme,Dropped=key=>{if(TossSupply(key)) CallDeferred(nameof(ShowBuildingInventory));}};layer.AddChild(root);root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);panel=root;
        var shade=new ColorRect {Color=new Color(0,0,0,.58f),MouseFilter=Control.MouseFilterEnum.Ignore};root.AddChild(shade);shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        float s=Mathf.Clamp(Mathf.Floor((GetViewport().GetVisibleRect().Size.Y-100)/166),2,4);
        var center=new CenterContainer {MouseFilter=Control.MouseFilterEnum.Ignore};root.AddChild(center);center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var stack=new VBoxContainer {MouseFilter=Control.MouseFilterEnum.Stop};center.AddChild(stack);root.Inside=()=>stack.GetGlobalRect();
        var frame=new TextureRect {Texture=new AtlasTexture {Atlas=Vanilla("gui/container/inventory"),Region=new Rect2(0,0,176,166)},CustomMinimumSize=new Vector2(176*s,166*s),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,TextureFilter=Control.TextureFilterEnum.Nearest};stack.AddChild(frame);
        var font=MinecraftFont();
        var caption=new Label {Text="Crafting",Position=new Vector2(97*s,7*s),TooltipText="Crafting is not needed for desktop organization."};caption.AddThemeFontOverride("font",font);caption.AddThemeFontSizeOverride("font_size",(int)(8*s));caption.AddThemeColorOverride("font_color",new Color("404040"));frame.AddChild(caption);
        AddPlayerPreview(frame,s);
        var slots=new List<ChestSlot>();var bindings=new List<ChestSlot>();
        for(int i=0;i<27;i++) {var slot=new ChestSlot {Name="InventoryStorage"+i,Size=Vector2.One*18*s,Position=new Vector2(7+i%9*18,83+i/9*18)*s,PixelScale=s,Container="$building"};frame.AddChild(slot);slots.Add(slot);}

        for(int i=0;i<9;i++)
        {
            int index=i;var slot=new ChestSlot {Name="InventoryHotbar"+i,Size=Vector2.One*18*s,Position=new Vector2(7+i*18,141)*s,PixelScale=s,Container="$building"};frame.AddChild(slot);bindings.Add(slot);BindHotbarSupply(slot,i);
            slot.TooltipText += "\nHotbar "+(i+1)+" · Drop a block here to assign";
            slot.Activated=(button,_,_)=>{if(button==MouseButton.Left) SelectHotbar(index);};
            slot.BuildDropped=key=>{DropHotbarItem(key,index);RefreshHotbar();ShowBuildingInventory();};
        }
        void Refresh() {FillBuildingSlots(slots,Refresh);for(int i=0;i<9;i++) BindHotbarSupply(bindings[i],i);RefreshHotbar();}
        Refresh();
        int pages=Math.Max(1,(BuildingItems().Count+26)/27);
        var footer=new HBoxContainer();stack.AddChild(footer);
        footer.AddChild(Button("‹",()=>{buildingPage=Math.Max(0,buildingPage-1);ShowBuildingInventory();}));
        footer.AddChild(new Label {Text=$"{buildingPage+1}/{pages}",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,HorizontalAlignment=HorizontalAlignment.Center});
        footer.AddChild(Button("›",()=>{buildingPage=Math.Min(pages-1,buildingPage+1);ShowBuildingInventory();}));
        footer.AddChild(Button("Carried files",()=>ShowChest(LinkLibrary.Inventory)));
        var help=new Label {Text="Click to equip · Drag to rearrange � Drop outside to throw stack · Escape to close",HorizontalAlignment=HorizontalAlignment.Center};help.AddThemeFontSizeOverride("font_size",13);stack.AddChild(help);
        statusLabel.Visible=false;hudToolbar.Visible=false;
    }
    private void AddPlayerPreview(Control frame,float scale)
    {
        var viewport=new SubViewport {Size=new Vector2I(100,144),OwnWorld3D=true,TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Once};frame.AddChild(viewport);
        var actor=new Node3D();viewport.AddChild(actor);var skin=Vanilla("entity/player/steve");
        AtlasBox(actor,new Vector3(0,1.5f,0),new Vector3(8,8,8),Vector2.Zero,skin);
        AtlasBox(actor,new Vector3(0,.875f,0),new Vector3(8,12,4),new Vector2(16,16),skin);
        AtlasBox(actor,new Vector3(-.375f,.875f,0),new Vector3(4,12,4),new Vector2(40,16),skin);
        AtlasBox(actor,new Vector3(.375f,.875f,0),new Vector3(4,12,4),new Vector2(32,48),skin);
        AtlasBox(actor,new Vector3(-.125f,.125f,0),new Vector3(4,12,4),new Vector2(0,16),skin);
        AtlasBox(actor,new Vector3(.125f,.125f,0),new Vector3(4,12,4),new Vector2(16,48),skin);
        var cam=new Camera3D {Projection=Camera3D.ProjectionType.Orthogonal,Size=2.4f,Position=new Vector3(1,1.6f,5),Current=true};actor.AddChild(cam);cam.LookAt(new Vector3(0,.8f,0));actor.AddChild(new DirectionalLight3D {RotationDegrees=new Vector3(-25,-25,0),LightEnergy=1.5f});
        frame.AddChild(new TextureRect {Texture=viewport.GetTexture(),Position=new Vector2(26,8)*scale,Size=new Vector2(48,69)*scale,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter=Control.MouseFilterEnum.Ignore});
    }
}
public partial class HotbarFrame:Control
{
    public Texture2D? Texture,Selection;
    public int Selected;
    public override void _Draw()
    {
        TextureFilter=TextureFilterEnum.Nearest;
        if(Texture!=null) DrawTextureRect(Texture,new Rect2(Vector2.Zero,Size),false);
        if(Selection!=null && Selected>=0) DrawTextureRect(Selection,new Rect2(new Vector2(Selected*60-3,-3),new Vector2(72,72)),false);
    }
}
