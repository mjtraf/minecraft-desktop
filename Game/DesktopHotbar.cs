using Godot;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private bool desktopHotbarActive;
    private HotbarFrame? desktopHotbar;
    private readonly List<ChestSlot> desktopSlots=[];
    private List<DesktopApp> quickApps=[];
    private int desktopSelection;
    private string? desktopSystemSelection;
    private void MoveDesktopSelection(int delta)
    {
        int count=quickApps.Count;
        int position=desktopSystemSelection switch {"search"=>0,"desktop"=>1,"system"=>count+2,"notifications"=>count+3,_=>desktopSelection+2};
        position=((position+delta)%(count+4)+count+4)%(count+4);
        desktopSystemSelection=position==0?"search":position==1?"desktop":position==count+2?"system":position==count+3?"notifications":null;
        if(desktopSystemSelection==null)desktopSelection=position-2;
        RefreshDesktopHotbar();
    }
    private void BuildDesktopHotbar()
    {
        desktopHotbar=new HotbarFrame {Texture=Vanilla("gui/sprites/hud/hotbar"),Selection=Vanilla("gui/sprites/hud/hotbar_selection"),MouseFilter=Control.MouseFilterEnum.Ignore};layer.AddChild(desktopHotbar);
        for(int i=0;i<9;i++)
        {
            int index=i;var slot=new ChestSlot {Ghost=true,PixelScale=3,Position=new Vector2((3+i*20)*3,3),Size=new Vector2(54,54)};desktopHotbar.AddChild(slot);desktopSlots.Add(slot);
            slot.MouseEntered+=()=>{var app=quickApps.ElementAtOrDefault(desktopSelection/9*9+index);if(desktopHotbarActive && Input.MouseMode!=Input.MouseModeEnum.Captured && app?.WindowIds is {Length:>1}) ShowGroupedPreviews(app);};
            slot.Activated=(button,_,_)=>{if(button==MouseButton.Right) {SelectDesktopSlot(desktopSelection/9*9+index);ShowDesktopAppMenu();return;}if(button==MouseButton.Left) {desktopHotbarActive=true;SelectDesktopSlot(desktopSelection/9*9+index);UseDesktopSlot();}};
        }
        RefreshDesktopHotbar();
    }
    private void ToggleDesktopHotbar()
    {
        ResetMouseActions();
        desktopHotbarActive=!desktopHotbarActive;
        if(desktopHotbarActive) {CancelPlacement();ClearCracks();bridge.Send(new{command="apps"});}
        else SelectHotbar(state.SelectedSlot);
        RefreshDesktopHotbar();RefreshHotbar();
    }
    private void SelectDesktopSlot(int index)
    {
        desktopSystemSelection=null;int count=Math.Max(1,quickApps.Count);desktopSelection=(index%count+count)%count;RefreshDesktopHotbar();
    }
    private void RefreshDesktopHotbar()
    {
        if(desktopHotbar==null) return;
        string? selected=quickApps.ElementAtOrDefault(desktopSelection)?.Id;
        var running=desktopApps.Where(a=>!a.Pinned).GroupBy(a=>a.Path.Length>0?a.Path:a.Id,StringComparer.OrdinalIgnoreCase).Select(g=>new DesktopApp("group:"+g.Key,g.First().Path.Length>0?System.IO.Path.GetFileNameWithoutExtension(g.First().Path):g.First().Name,g.First().Path,false,g.Count(),g.Select(a=>a.Id).ToArray())).ToList();
        var pins=desktopApps.Where(a=>a.Pinned).Where(p=>!running.Any(g=>g.WindowIds!.Intersect(p.WindowIds??[]).Any()));
        quickApps=running.Concat(pins).Concat(state.Settings.PinnedApps.Where(p=>!running.Any(g=>string.Equals(g.Path,p,StringComparison.OrdinalIgnoreCase))).Select(p=>new DesktopApp(p,System.IO.Path.GetFileNameWithoutExtension(p),p,true,0))).DistinctBy(a=>a.Id).ToList();
        if(selected!=null && quickApps.FindIndex(a=>a.Id==selected) is >=0 and var found) desktopSelection=found;
        desktopSelection=Math.Clamp(desktopSelection,0,Math.Max(0,quickApps.Count-1));
        int page=desktopSelection/9;
        for(int i=0;i<9;i++)
        {
            var slot=desktopSlots[i];var app=quickApps.ElementAtOrDefault(page*9+i);
            if(slot.TooltipText!=(app?.Name??"Empty app slot") || (slot.Entry!=null && slot.Entry.Path!=app?.Path)) {slot.Icon=null;slot.Entry=null;}
            slot.TooltipText=app?.Name??"Empty app slot";slot.Running=app?.Windows>0;slot.Count=app?.Windows>1?app.Windows:0;slot.BuildKey=null;
            if(app!=null && app.Path.Length>0 && !Uri.IsWellFormedUriString(app.Path,UriKind.Absolute)) {slot.Entry=new FileEntry(app.Path,app.Name,false,true);if(slot.Icon==null) _=LoadShellImage(slot,app.Path,viewGeneration);}
            else if(app!=null && System.IO.File.Exists(app.Path)) {slot.Entry=new FileEntry(app.Path,app.Name,false,true);if(slot.Icon==null) _=LoadShellImage(slot,app.Path,viewGeneration);}
            else if(app!=null) slot.Icon=ItemIcon("bookshelf");
            slot.QueueRedraw();
        }
        desktopHotbar.Selected=desktopHotbarActive && desktopSystemSelection==null?desktopSelection%9:-1;desktopHotbar.Modulate=desktopHotbarActive?Colors.White:new Color(.7f,.7f,.7f);desktopHotbar.QueueRedraw();
    }
    private void LayoutDesktopHotbar()
    {
        if(desktopHotbar==null || hotbar==null || equippedLabel==null) return;
        desktopHotbar.Visible=panel==null && desktopHotbarActive;desktopHotbar.Size=hotbar.Size;desktopHotbar.Scale=hotbar.Scale;desktopHotbar.Position=hotbar.Position;
        hotbar.Selected=desktopHotbarActive?-1:state.SelectedSlot;hotbar.Modulate=desktopHotbarActive?new Color(.7f,.7f,.7f):Colors.White;hotbar.QueueRedraw();
        equippedLabel.Scale=Vector2.One*DockScale;equippedLabel.Position=new Vector2(0,GetViewport().GetVisibleRect().Size.Y-93*DockScale);equippedLabel.Size=new Vector2(GetWindow().Size.X,22);
        equippedLabel.Text=desktopHotbarActive?$"Apps {desktopSelection/9+1}: {desktopSystemSelection switch {"search"=>"Search","desktop"=>"Desktop","system"=>"System","notifications"=>"Time & notifications",_=>quickApps.ElementAtOrDefault(desktopSelection)?.Name??"E to add shortcuts"}}   |   Wheel / 1-9 select   |   Enter: open":$"{ResolveSupply(state.Hotbar[state.SelectedSlot])?.Name??"Blocks"}{(placingKind=="writable_book"?" | Right-click surface: place | Away: read":"")} | `: apps";
    }
    private void ShowGroupedPreviews(DesktopApp app)
    {
        if(bridge.Connected)
        {
            Input.MouseMode=Input.MouseModeEnum.Visible;walking=false;ClearCracks();
            if(desktopHelperPid!=0) WindowsAppControl.AllowSetForegroundWindow(desktopHelperPid);
            bridge.Send(new {command="previews",ids=app.WindowIds});
        }
        else
        {
            var box=OpenPanel(app.Name,"Choose a window");
            foreach(var id in app.WindowIds??[]) {var window=desktopApps.FirstOrDefault(a=>a.Id==id);box.AddChild(Button(window?.Name??id,()=>{ClosePanel();Release(false);SwitchApp(id);}));}
        }
    }
    private void ShowDesktopAppMenu()
    {
        if(desktopSystemSelection!=null){OpenDesktopSystem(desktopSystemSelection);return;}
        var app=quickApps.ElementAtOrDefault(desktopSelection);if(app==null)return;
        // Keep this menu in the renderer: a native popup competes with automatic
        // focus return and can lose activation between mouse-down and mouse-up.
        var ids=(app.WindowIds??[]).Distinct().ToArray();
        ClosePanel(false);backgroundApp=false;walking=false;Input.MouseMode=Input.MouseModeEnum.Visible;ClearCracks();
        var root=new Control {Name="AppContextMenu",Theme=uiTheme,MouseFilter=Control.MouseFilterEnum.Stop};
        layer.AddChild(root);root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);panel=root;
        root.GuiInput+=e=>{if(e is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left or MouseButton.Right}) {GetViewport().SetInputAsHandled();ClosePanelAndResume();}};
        var frame=new PanelContainer {MouseFilter=Control.MouseFilterEnum.Stop,CustomMinimumSize=new Vector2(300,0)};root.AddChild(frame);
        var margin=new MarginContainer();foreach(var side in new[]{"left","right","top","bottom"}) margin.AddThemeConstantOverride("margin_"+side,8);frame.AddChild(margin);
        var box=new VBoxContainer();box.AddThemeConstantOverride("separation",8);margin.AddChild(box);
        var title=new Label {Text=app.Name,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis,CustomMinimumSize=new Vector2(280,0)};title.AddThemeFontOverride("font",MinecraftFont());title.AddThemeFontSizeOverride("font_size",18);title.AddThemeColorOverride("font_color",new Color("303030"));box.AddChild(title);
        var close=Button(ids.Length>1?"Close all windows":"Close window",()=>
        {
            // Snapshot IDs from the clicked slot, even if discovery reorders it.
            bridge.Send(new{command="close-apps",ids});
            ClosePanelAndResume();
        });
        close.Name="CloseAppWindows";close.CustomMinimumSize=new Vector2(280,44);close.Disabled=ids.Length==0 || (!bridge.Connected && !selfTesting);
        close.AddThemeFontOverride("font",MinecraftFont());close.AddThemeFontSizeOverride("font_size",18);box.AddChild(close);
        if(ids.Length==0) box.AddChild(new Label {Text="This shortcut has no open windows."});
        var screen=GetViewport().GetVisibleRect().Size;var pointer=GetViewport().GetMousePosition();var extent=frame.GetCombinedMinimumSize();
        frame.Position=new Vector2(Mathf.Clamp(pointer.X-extent.X/2,8,Mathf.Max(8,screen.X-extent.X-8)),Mathf.Clamp(pointer.Y-extent.Y-12,8,Mathf.Max(8,screen.Y-extent.Y-8)));
        close.GrabFocus();
    }
    private void OpenAppTarget(string path)
    {
        if(Uri.TryCreate(path,UriKind.Absolute,out var uri) && uri.Scheme is "https" or "http")
        {
            var error=OS.ShellOpen(path);
            if(error!=Error.Ok) throw new InvalidOperationException("Windows could not open this bookmark.");
        }
        else files.Open(path);
    }
    private void UseDesktopSlot()
    {
        if(desktopSystemSelection!=null){OpenDesktopSystem(desktopSystemSelection);return;}
        var app=quickApps.ElementAtOrDefault(desktopSelection);if(app==null) {Toast("Press E to add apps or bookmarks.");return;}
        if(app.WindowIds is {Length:>1}) {ShowGroupedPreviews(app);return;}
        if(desktopHelperPid!=0) WindowsAppControl.AllowSetForegroundWindow(desktopHelperPid);
        ClosePanel();Release(false);
        if(!app.Pinned) SwitchApp(app.WindowIds?.FirstOrDefault()??app.Id);
        else if(app.WindowIds is {Length:>0}) SwitchApp(app.WindowIds[0]);
        else try {OpenAppTarget(app.Path);} catch(Exception e) {Toast(e.Message);}
    }
}
