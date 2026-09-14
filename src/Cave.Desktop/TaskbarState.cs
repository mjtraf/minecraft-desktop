using System.Runtime.InteropServices;
using System.Text.Json;
namespace Cave.Desktop;
internal static class TaskbarState
{
    private static void ShowTaskbars(bool show)=>Native.EnumWindows((window,_)=>{
        var name=new System.Text.StringBuilder(64);Native.GetClassName(window,name,64);
        if(name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")Native.ShowWindow(window,show?8:0);
        return true;
    },0);
    [StructLayout(LayoutKind.Sequential)] private struct Data {public uint size;public nint hwnd;public uint callback,edge;public Native.Rect rect;public nint param;}
    [DllImport("shell32.dll")] private static extern nuint SHAppBarMessage(uint command,ref Data data);
    private static Data Info()=>new(){size=(uint)Marshal.SizeOf<Data>(),hwnd=Native.FindWindow("Shell_TrayWnd",null)};
    internal static uint Read() {var d=Info();return (uint)SHAppBarMessage(4,ref d);}
    private static void Set(uint value) {var d=Info();d.param=(nint)value;SHAppBarMessage(10,ref d);}
    internal static void Enable(string recovery)
    {
        if(!File.Exists(recovery))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
            File.WriteAllText(recovery+".tmp",JsonSerializer.Serialize(new {state=Read(),visible=Native.IsWindowVisible(Native.FindWindow("Shell_TrayWnd",null))}));File.Move(recovery+".tmp",recovery,true);
            Set(Read()|1);
        }
    }
    internal static void Restore(string recovery)
    {
        if(!File.Exists(recovery)) return;
        using var document=JsonDocument.Parse(File.ReadAllText(recovery));
        ShowTaskbars(!document.RootElement.TryGetProperty("visible",out var visible) || visible.GetBoolean());
        Set(document.RootElement.GetProperty("state").GetUInt32());File.Delete(recovery);
    }
}
