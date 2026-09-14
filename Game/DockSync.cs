using Godot;
namespace CozyCave;
public partial class Main
{
    private float dockSyncTime;
    private string lastDockState="";
    private readonly Dictionary<Texture2D,string> dockIconCache=[];
    private string? DockIcon(Texture2D? texture)
    {
        if(texture==null) return null;
        if(dockIconCache.TryGetValue(texture,out var encoded)) return encoded;
        using var image=texture.GetImage();if(image==null || image.IsEmpty()) return null;
        image.Resize(48,48,Image.Interpolation.Nearest);encoded=Convert.ToBase64String(image.SavePngToBuffer());
        // Viewport icons may still be empty on their first frame; retry on next sync.
        if(image.GetUsedRect().HasArea()) {if(dockIconCache.Count>256) dockIconCache.Clear();dockIconCache[texture]=encoded;}
        return encoded;
    }
    private void SyncDock(float dt)
    {
        if(!bridge.Connected) return;dockSyncTime+=dt;if(dockSyncTime<.05f) return;dockSyncTime=0;
        var slots=Enumerable.Range(0,9).Select(i=>
        {
            var app=quickApps.ElementAtOrDefault(desktopSelection/9*9+i);
            return new {name=app?.Name??"Empty",icon=DockIcon(desktopSlots[i].Icon),count=app?.Windows??0,windows=app?.WindowIds??[]};
        }).ToArray();
        string stateJson=System.Text.Json.JsonSerializer.Serialize(new {command="dock-state",apps=true,selected=desktopSystemSelection==null?desktopSelection%9:-1,system=desktopSystemSelection,page=desktopSelection/9+1,slots});
        if(stateJson!=lastDockState) {lastDockState=stateJson;bridge.Send(System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(stateJson));}
    }
    private void HandleDock(System.Text.Json.JsonElement p)
    {
        string action=p.GetProperty("action").GetString()!;
        int index=p.TryGetProperty("index",out var value)?value.GetInt32():0;
        if(action=="select") SelectDesktopSlot(desktopSelection/9*9+index);
        else if(action=="wheel") MoveDesktopSelection(index);
        else if(action=="activate") {if(desktopSystemSelection==null)SelectDesktopSlot(desktopSelection/9*9+index);UseDesktopSlot();}
        else if(action=="dismiss") {if(panel==null && !backgroundApp) Enter();}
        dockSyncTime=1;lastDockState="";
    }
}
