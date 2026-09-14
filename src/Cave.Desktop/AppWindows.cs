using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace Cave.Desktop;
internal sealed class AppWindows
{
    internal record Entry(string Id,string Name,string Path,bool Pinned,int Windows,string[]? WindowIds=null);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,(nint Handle,uint Pid)> windows=new();
    private readonly Dictionary<string,string> shortcutTargets=new(StringComparer.OrdinalIgnoreCase);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(nint h,StringBuilder text,int length);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint h,uint command);
    internal Entry[] List(nint cave)
    {
        var result=new List<Entry>();
        string pins=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft","Internet Explorer","Quick Launch","User Pinned","TaskBar");
        try {foreach(var path in Directory.EnumerateFiles(pins,"*.lnk")) result.Add(new(path,Path.GetFileNameWithoutExtension(path),path,true,0));} catch(IOException) {} catch(UnauthorizedAccessException) {}
        Native.EnumWindows((h,_)=>
        {
            if(h==cave || !Native.IsWindowVisible(h) || GetWindow(h,4)!=0 || ((long)Native.GetStyle(h,-20)&0x80)!=0) return true;
            var title=new StringBuilder(512);GetWindowText(h,title,title.Capacity);if(title.Length==0) return true;
            Native.GetWindowThreadProcessId(h,out uint pid);
            if(pid==Environment.ProcessId || Native.Class(h) is "Progman" or "WorkerW" or "Shell_TrayWnd") return true;
            string path="";try {using var process=Process.GetProcessById((int)pid);path=process.MainModule?.FileName??"";} catch(Exception e) when(e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) {}
            string id=h.ToInt64().ToString();windows[id]=(h,pid);result.Add(new(id,title.ToString(),path,false,1));return true;
        },0);
        for(int i=0;i<result.Count;i++) if(result[i].Pinned)
        {
            var pin=result[i];
            if(!shortcutTargets.TryGetValue(pin.Path,out var target))
            {
                target="";object? shell=null,shortcut=null;
                try {var type=Type.GetTypeFromProgID("WScript.Shell");if(type!=null) {shell=Activator.CreateInstance(type);shortcut=((dynamic)shell!).CreateShortcut(pin.Path);target=((dynamic)shortcut).TargetPath as string??"";}}
                catch(Exception e) when(e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) {}
                finally {if(shortcut!=null) Marshal.ReleaseComObject(shortcut);if(shell!=null) Marshal.ReleaseComObject(shell);}
                shortcutTargets[pin.Path]=target;
            }
            var matches=target.Length==0?[]:result.Where(a=>!a.Pinned && string.Equals(a.Path,target,StringComparison.OrdinalIgnoreCase)).Select(a=>a.Id).ToArray();
            result[i]=pin with {Windows=matches.Length,WindowIds=matches};
        }
        return result.ToArray();
    }
    internal bool CloseWindow(string id)
    {
        if(!windows.TryGetValue(id,out var value) || !Native.IsWindow(value.Handle)) return false;
        Native.GetWindowThreadProcessId(value.Handle,out uint pid);if(pid!=value.Pid) return false;
        return Native.PostMessage(value.Handle,0x0010,0,0);
    }
    internal bool Activate(string id)
    {
        if(!windows.TryGetValue(id,out var value) || !Native.IsWindow(value.Handle)) return false;
        Native.GetWindowThreadProcessId(value.Handle,out uint pid);if(pid!=value.Pid) return false;
        if(Native.IsIconic(value.Handle)) Native.ShowWindow(value.Handle,9);
        return Native.SetForegroundWindow(value.Handle);
    }
}
