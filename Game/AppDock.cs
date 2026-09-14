using Godot;
using System.Text.Json;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private record DesktopApp(string Id,string Name,string Path,bool Pinned,int Windows,string[]? WindowIds=null);
    private List<DesktopApp> desktopApps=[];
    private Control appDock=null!;
    private Button dockClock=null!;
    private float appRefresh;
    private uint desktopHelperPid;
    private bool appsOpen,showOpenApps;
    private int appPage;
    private HashSet<string>? appWindowFilter;
    private readonly Dictionary<string,Button> dockSystemButtons=[];
    private ColorRect dockBackdrop=null!;
    private StyleBoxTexture dockNormal=null!,dockHighlight=null!;
    private float DockScale => GetViewport().GetVisibleRect().Size.X / Math.Max(1,GetWindow().Size.X);
    private void BuildAppDock()
    {
        dockBackdrop=new ColorRect {Color=new Color("1f1c18"),MouseFilter=Control.MouseFilterEnum.Ignore};layer.AddChild(dockBackdrop);layer.MoveChild(dockBackdrop,0);
        appDock=new Control {Theme=uiTheme};layer.AddChild(appDock);
        BuildDesktopHotbar();
        StyleBoxTexture Face(string fill)
        {
            using var image=Image.CreateEmpty(8,8,false,Image.Format.Rgba8);image.Fill(new Color(fill));
            for(int i=0;i<8;i++)for(int j=0;j<2;j++){image.SetPixel(j,i,new Color("dadada"));image.SetPixel(i,j,new Color("dadada"));}
            for(int i=0;i<8;i++)for(int j=6;j<8;j++){image.SetPixel(j,i,new Color("303030"));image.SetPixel(i,j,new Color("303030"));}
            return new StyleBoxTexture {Texture=ImageTexture.CreateFromImage(image),TextureMarginLeft=2,TextureMarginRight=2,TextureMarginTop=2,TextureMarginBottom=2,ContentMarginLeft=4,ContentMarginRight=4};
        }
        dockNormal=Face("747474");dockHighlight=Face("9196a2");
        foreach(var (label,action) in new[]{("Search","search"),("Desktop","desktop"),("System","system"),("","notifications")})
        {
            var button=Button(label,()=>OpenDesktopSystem(action));button.TextureFilter=CanvasItem.TextureFilterEnum.Nearest;button.AddThemeFontOverride("font",GD.Load<FontFile>("res://Assets/Fonts/Pixel.ttf"));button.AddThemeFontSizeOverride("font_size",12);
            button.AddThemeStyleboxOverride("normal",dockNormal);button.AddThemeStyleboxOverride("hover",dockHighlight);button.AddThemeColorOverride("font_color",Colors.White);
            appDock.AddChild(button);dockSystemButtons[action]=button;
        }
        dockClock=dockSystemButtons["notifications"];
        bridge.Send(new {command="apps"});
    }
    private void UpdateAppDock(float delta)
    {
        var screen=GetViewport().GetVisibleRect().Size;float scale=DockScale,left=(screen.X-546*scale)/2,top=screen.Y-96*scale;
        dockBackdrop.Position=new Vector2(0,top);dockBackdrop.Size=new Vector2(screen.X,96*scale);dockBackdrop.Visible=panel==null;
        appDock.Visible=panel==null;
        void Place(string action,float x,float width)
        {
            var button=dockSystemButtons[action];button.Scale=Vector2.One*scale;button.Position=new Vector2(x,top+35*scale);button.Size=new Vector2(width,42);
            button.AddThemeStyleboxOverride("normal",desktopHotbarActive && desktopSystemSelection==action?dockHighlight:dockNormal);
        }
        Place("search",left-228*scale,100);Place("desktop",left-122*scale,112);
        Place("system",left+556*scale,92);Place("notifications",left+654*scale,146);
        dockClock.Text=DateTime.Now.ToString("h:mm tt\nMMM d");
        LayoutDesktopHotbar();SyncDock(delta);
        appRefresh+=delta;if(appRefresh>3) {appRefresh=0;bridge.Send(new {command="apps"});}
    }
    private void OpenDesktopSystem(string action)
    {
        ResetMouseActions();
        if(!bridge.Connected) {Toast("Open Cozy Cave through its Windows launcher to use system controls.");return;}
        backgroundApp=true;walking=false;Input.MouseMode=Input.MouseModeEnum.Visible;ClearCracks();
        if(desktopHelperPid!=0) WindowsAppControl.AllowSetForegroundWindow(desktopHelperPid);
        bridge.Send(new {command="system",action});
    }
    private void ReceiveApps(JsonElement payload)
    {
        desktopApps=payload.EnumerateArray().Select(a=>new DesktopApp(a.GetProperty("Id").GetString()!,a.GetProperty("Name").GetString()!,a.GetProperty("Path").GetString()!,a.GetProperty("Pinned").GetBoolean(),a.GetProperty("Windows").GetInt32(),a.TryGetProperty("WindowIds",out var ids)&&ids.ValueKind==JsonValueKind.Array?ids.EnumerateArray().Select(i=>i.GetString()!).ToArray():[])).ToList();
        RefreshDesktopHotbar();
    }
    private void ShowApps()
    {
        backgroundApp=false;var box=OpenPanel("App hotbar","Click an app to launch it or switch windows. The cave stays behind it.");appsOpen=true;
        var tabs=new HBoxContainer();box.AddChild(tabs);
        tabs.AddChild(Button("Pinned",()=>{appWindowFilter=null;showOpenApps=false;appPage=0;ShowApps();}));
        tabs.AddChild(Button("Open apps",()=>{appWindowFilter=null;showOpenApps=true;appPage=0;ShowApps();}));
        tabs.AddChild(Button("Refresh",()=>{bridge.Send(new {command="apps"});RefreshAppsSoon();}));
        tabs.AddChild(Button("+ Pin app",()=>PickFiles(paths=>{foreach(var p in paths.Where(p=>System.IO.File.Exists(p))) if(!state.Settings.PinnedApps.Contains(p)) state.Settings.PinnedApps.Add(p);Changed();ShowApps();})));
        tabs.AddChild(Button("+ Folder",()=>PickFolder(path=>{if(!state.Settings.PinnedApps.Contains(path)) state.Settings.PinnedApps.Add(path);Changed();RefreshDesktopHotbar();ShowApps();})));
        var bookmark=new LineEdit {PlaceholderText="Website bookmark: https://...",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};box.AddChild(bookmark);
        bookmark.TextSubmitted+=value=>{if(Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri) && uri.Scheme is "https" or "http") {if(!state.Settings.PinnedApps.Contains(uri.AbsoluteUri)) state.Settings.PinnedApps.Add(uri.AbsoluteUri);Changed();RefreshDesktopHotbar();ShowApps();}else Toast("Enter an https:// or http:// website address.");};
        var items=showOpenApps?desktopApps.Where(a=>!a.Pinned && (appWindowFilter==null||appWindowFilter.Contains(a.Id))).ToList():desktopApps.Where(a=>a.Pinned).Concat(state.Settings.PinnedApps.Select(p=>new DesktopApp(p,System.IO.Path.GetFileNameWithoutExtension(p),p,true,0))).DistinctBy(a=>a.Path,StringComparer.OrdinalIgnoreCase).ToList();
        int pages=Math.Max(1,(items.Count+8)/9);appPage=Math.Clamp(appPage,0,pages-1);
        var row=new HBoxContainer();row.AddThemeConstantOverride("separation",0);box.AddChild(row);
        foreach(var app in items.Skip(appPage*9).Take(9))
        {
            var slot=new ChestSlot {CustomMinimumSize=new Vector2(88,88),PixelScale=4,TooltipText=app.Name,Count=app.Windows,Running=app.Windows>0};row.AddChild(slot);
            if(app.Path.Length>0) {slot.Entry=new FileEntry(app.Path,app.Name,false,true);_ = LoadShellImage(slot,app.Path,viewGeneration);}
            else slot.Icon=ItemIcon("bookshelf");
            slot.Activated=(button,_,_)=>
            {
                if(button==MouseButton.Right && state.Settings.PinnedApps.Contains(app.Path)) {state.Settings.PinnedApps.Remove(app.Path);Changed();ShowApps();return;}
                if(button!=MouseButton.Left) return;
                if(app.Pinned && app.WindowIds is {Length:>1}) {appWindowFilter=app.WindowIds.ToHashSet();showOpenApps=true;appPage=0;ShowApps();return;}
                if(desktopHelperPid!=0) WindowsAppControl.AllowSetForegroundWindow(desktopHelperPid);
                ClosePanel();Release(false);
                if(app.Pinned && app.WindowIds is {Length:1}) {SwitchApp(app.WindowIds[0]);return;}
                if(app.Pinned) {try {OpenAppTarget(app.Path);} catch(Exception e){Toast(e.Message);}}
                else SwitchApp(app.Id);
            };
        }
        for(int i=Math.Min(9,Math.Max(0,items.Count-appPage*9));i<9;i++) row.AddChild(new ChestSlot {CustomMinimumSize=new Vector2(88,88),PixelScale=4});
        if(items.Count==0) box.AddChild(new Label {Text=showOpenApps?"No open windows reported. Use Refresh after opening an app.":"No taskbar shortcuts exposed by Windows. Use + Pin app to add launchers."});
        var pager=new HBoxContainer();box.AddChild(pager);
        var previous=Button("‹",()=>{appPage--;ShowApps();});previous.Disabled=appPage==0;pager.AddChild(previous);
        pager.AddChild(new Label {Text=$"{(showOpenApps?"Open windows":"Pinned apps")} · {appPage+1}/{pages}"});
        var next=Button("›",()=>{appPage++;ShowApps();});next.Disabled=appPage==pages-1;pager.AddChild(next);
        box.AddChild(new Label {Text="Open apps lists each window separately. Right-click a manually pinned app to unpin it here."});
    }
    private async void RefreshAppsSoon() {await ToSignal(GetTree().CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);if(appsOpen && panel!=null) ShowApps();}
    private void SwitchApp(string id)
    {
        // Execute in the renderer that received the user's click, which has foreground rights.
        if(!WindowsAppControl.Activate(id)) bridge.Send(new {command="activate-app",id});
    }
}
