using System.Runtime.InteropServices;
using System.Text.Json;
namespace Cave.Desktop;
internal static class AgentDesktop
{
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    internal static void Run(string requestPath)
    {
        try
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(requestPath));var p=doc.RootElement;string action=p.GetProperty("action").GetString()!;
            object result;
            switch(action)
            {
                case "screenshot":
                    var bounds=SystemInformation.VirtualScreen;string path=Path.GetFullPath(p.GetProperty("output").GetString()!);
                    using(var image=new Bitmap(bounds.Width,bounds.Height)) {using var graphics=Graphics.FromImage(image);graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}
                    result=new {path,bounds.X,bounds.Y,bounds.Width,bounds.Height};break;
                case "move":case "click":
                    int x=p.GetProperty("x").GetInt32(),y=p.GetProperty("y").GetInt32();if(!SystemInformation.VirtualScreen.Contains(x,y))throw new ArgumentException("Coordinates outside virtual screen");SetCursorPos(x,y);
                    if(action=="click"){bool right=p.TryGetProperty("button",out var b)&&b.GetString()=="right";int n=p.TryGetProperty("double",out var d)&&d.GetBoolean()?2:1;for(int i=0;i<n;i++){mouse_event(right?8u:2u,0,0,0,0);mouse_event(right?16u:4u,0,0,0,0);}}
                    result=new {ok=true};break;
                case "scroll":mouse_event(0x800,0,0,unchecked((uint)p.GetProperty("delta").GetInt32()),0);result=new {ok=true};break;
                case "key":SendKeys.SendWait(p.GetProperty("keys").GetString()!);result=new {ok=true};break;
                case "text":
                    // Paste preserves Unicode. Keep the previous clipboard when it has not changed during the paste.
                    var old=Clipboard.GetDataObject();string text=p.GetProperty("text").GetString()!;Clipboard.SetText(text);SendKeys.SendWait("^v");Thread.Sleep(150);
                    if(Clipboard.ContainsText() && Clipboard.GetText()==text && old!=null)try{Clipboard.SetDataObject(old,true);}catch{}
                    result=new {ok=true};break;
                default:throw new ArgumentException("Unknown desktop action");
            }
            File.WriteAllText(requestPath+".result.json",JsonSerializer.Serialize(result));
        }catch(Exception e){File.WriteAllText(requestPath+".result.json",JsonSerializer.Serialize(new {error=e.Message}));Environment.ExitCode=1;}
    }
}
