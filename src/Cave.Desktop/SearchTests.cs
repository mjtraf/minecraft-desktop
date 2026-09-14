using System.Diagnostics;
namespace Cave.Desktop;
internal static class SearchTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();
        void Check(bool ok,string text)=>results.Add((ok?"PASS ":"FAIL ")+text);
        using var host=new Form {Text="Cozy Cave search check",TopMost=true,Width=360,Height=160,StartPosition=FormStartPosition.Manual,Location=new Point(Screen.PrimaryScreen!.WorkingArea.Right-390,100)};
        var recovery=Path.Combine(output,"taskbar-recovery.json");
        host.Shown+=async(_,_)=>
        {
            try
            {
                await Task.Delay(500);using var panel=new SystemPanel(()=>{});panel.Show();panel.Activate();await Task.Delay(400);
                var popupCursor=Cursor.Position;Cursor.Position=new Point(host.Right-50,host.Top+75);mouse_event(2,0,0,0,0);mouse_event(4,0,0,0,0);await Task.Delay(400);Cursor.Position=popupCursor;Check(panel.IsDisposed,"System popup closes when another window gains focus");panel.Close();
                var taskbar=Native.FindWindow("Shell_TrayWnd",null);Native.GetWindowRect(taskbar,out var before);
                TaskbarState.Enable(recovery);
                SystemHub.Action("windows-search");await Task.Delay(2200);
                var fg=Native.GetForegroundWindow();Native.GetWindowThreadProcessId(fg,out var pid);
                string process=Process.GetProcessById((int)pid).ProcessName;
                results.Add("Foreground process: "+process+" handle="+fg);
                Check(process.Equals("SearchHost",StringComparison.OrdinalIgnoreCase),"Search button launches actual Windows Search");
                Native.GetWindowRect(taskbar,out var after);
                results.Add($"Taskbar before {before.Top}..{before.Bottom}; search {after.Top}..{after.Bottom}");
                Check(Native.IsWindowVisible(taskbar),"Taskbar owner stays alive so its Search popup can render");
                using(var screenshot=new Bitmap(Screen.PrimaryScreen!.Bounds.Width,Screen.PrimaryScreen.Bounds.Height))
                {using var g=Graphics.FromImage(screenshot);g.CopyFromScreen(Point.Empty,Point.Empty,screenshot.Size);screenshot.Save(Path.Combine(output,"search-visible.png"));}
                var cursor=Cursor.Position;Cursor.Position=new Point(host.Right-50,host.Top+75);mouse_event(2,0,0,0,0);mouse_event(4,0,0,0,0);await Task.Delay(600);Cursor.Position=cursor;
                Check(Native.GetForegroundWindow()!=fg,"Click-away focus leaves Windows Search");
            }
            catch(Exception e){results.Add("FAIL "+e);}
            finally{TaskbarState.Restore(recovery);Check(Native.IsWindowVisible(Native.FindWindow("Shell_TrayWnd",null)),"Taskbar visibility restored after test");File.WriteAllLines(Path.Combine(output,"results.txt"),results);host.Close();}
        };
        Application.Run(host);
    }
}
