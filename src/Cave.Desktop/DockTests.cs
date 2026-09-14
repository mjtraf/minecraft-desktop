using System.Text.Json;
using System.Diagnostics;
namespace Cave.Desktop;
internal static class DockTests
{
    private static uint GetPid(nint h) {Native.GetWindowThreadProcessId(h,out var pid);return pid;}
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();
        void Check(bool ok,string label)=>results.Add((ok?"PASS ":"FAIL ")+label);
        var screen=Screen.PrimaryScreen!;var original=screen.WorkingArea;
        var recovery=Path.Combine(output,"taskbar-state.json");var taskbarState=TaskbarState.Read();
        using var first=new Form {Text="Preview fixture A",BackColor=Color.Teal,Bounds=new Rectangle(original.Left+40,original.Top+40,400,260),StartPosition=FormStartPosition.Manual};
        using var second=new Form {Text="Preview fixture B",BackColor=Color.SandyBrown,Bounds=new Rectangle(original.Left+80,original.Top+80,380,240),StartPosition=FormStartPosition.Manual};
        first.Controls.Add(new Label {Text="WINDOW A",Font=new Font("Consolas",24),AutoSize=true});second.Controls.Add(new Label {Text="WINDOW B",Font=new Font("Consolas",24),AutoSize=true});
        string? dockAction=null;int closeRequests=0,activations=0;bool cancelClose=true;
        second.FormClosing+=(_,e)=>{if(cancelClose)e.Cancel=true;};
        using var fixture=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--app-window-fixture"){UseShellExecute=false});
        var appWindows=new AppWindows();string? fixtureId=null;
        using var dock=new DesktopDock((a,i)=>dockAction=a,(ids,focus)=>{});WindowPreviews? preview=null;
        using var timer=new System.Windows.Forms.Timer {Interval=600};int step=0;
        first.Shown+=(_,_)=>{second.Show();dock.Reserve(screen);dock.Show();timer.Start();};
        timer.Tick+=(_,_)=>
        {
            try
            {
                step++;
                if(step==1)
                {
                    Native.ShowWindow(first.Handle,5);Native.ShowWindow(second.Handle,5);
                    var slots=Enumerable.Range(0,9).Select(i=>new{name=i==0?"Two windows":"Empty",icon=(string?)null,count=i==0?2:0,windows=i==0?new[]{first.Handle.ToString(),second.Handle.ToString()}:Array.Empty<string>()});
                    dock.UpdateState(JsonSerializer.SerializeToElement(new{apps=false,selected=0,page=1,slots}));
                    Check(dock.Apps,"Native desktop stays in apps mode even when cave uses blocks");
                    Check(dock.Registered,"Native hotbar registers as appbar");
                    Check(Screen.FromHandle(dock.Handle).WorkingArea.Bottom<=original.Bottom-90,"Dock reserves space from maximized apps");
                    fixtureId=appWindows.List(0).FirstOrDefault(a=>long.TryParse(a.Id,out var h)&&GetPid((nint)h)==fixture!.Id)?.Id;
                    preview=new WindowPreviews([new(first.Handle.ToString(),first.Text,"",false,1),new(second.Handle.ToString(),second.Text,"",false,1)],_=>activations++,()=>{},id=>{closeRequests++;Native.PostMessage((nint)long.Parse(id),0x10,0,0);});
                    preview.StartPosition=FormStartPosition.Manual;preview.Location=new Point(original.Left+(original.Width-preview.Width)/2,dock.Top-preview.Height-8);preview.Show();
                }
                if(step==3)
                {
                    Check(preview?.ThumbnailCount==2,"Both native live window thumbnails register");
                    if(preview!=null) {using var image=new Bitmap(preview.Width,preview.Height);using var g=Graphics.FromImage(image);g.CopyFromScreen(preview.Location,Point.Empty,preview.Size);image.Save(Path.Combine(output,"previews.png"));}
                    using(var image=new Bitmap(dock.Width,dock.Height)) {dock.DrawToBitmap(image,dock.ClientRectangle);image.Save(Path.Combine(output,"dock.png"));}
                    var close=WindowPreviews.CloseBounds(1);Native.PostMessage(preview!.Handle,0x201,1,(nint)(((close.Top+10)<<16)|(close.Left+10)));
                }
                if(step==4)
                {
                    Check(closeRequests==1 && activations==0 && !second.IsDisposed,"Preview X requests close without activating another window; cancellation preserved");
                    cancelClose=false;var close=WindowPreviews.CloseBounds(1);Native.PostMessage(preview!.Handle,0x201,1,(nint)(((close.Top+10)<<16)|(close.Left+10)));
                }
                if(step==5)
                {
                    Check(second.IsDisposed && !first.IsDisposed,"Preview X closes only its selected window");
                    Check(preview?.ThumbnailCount==1,"Preview removes closed window and refreshes remaining thumbnail");
                    preview?.Close();
                    Native.PostMessage(dock.Handle,0x204,2,(nint)((50<<16)|((dock.ClientSize.Width-546)/2+20)));
                    Check(fixtureId!=null && appWindows.CloseWindow(fixtureId),"Validated external window accepts normal close request");
                    Check(!appWindows.CloseWindow("invalid"),"Unknown window close safely rejected");
                }
                if(step==6)
                {
                    Check(dockAction=="context","Right-click app slot dispatches context menu");
                    Check(fixture!.HasExited,"External fixture exits through WM_CLOSE");
                    dock.ReleaseBar();
                }
                if(step==7)
                {
                    Check(Screen.FromHandle(first.Handle).WorkingArea==original,"Closing dock restores desktop work area");
                    TaskbarState.Enable(recovery);
                    Check((TaskbarState.Read()&1)!=0 && File.Exists(recovery),"Taskbar auto-hide enabled with recovery record");
                    TaskbarState.Restore(recovery);
                    Check(TaskbarState.Read()==taskbarState && !File.Exists(recovery),"Exact previous taskbar setting restored");
                    using(var panel=new SystemPanel(()=>{})) {panel.Show();using var image=new Bitmap(panel.Width,panel.Height);panel.DrawToBitmap(image,panel.ClientRectangle);image.Save(Path.Combine(output,"system.png"));panel.Close();}
                    using(var search=new AppSearch()) {search.CreateControl();using var image=new Bitmap(search.Width,search.Height);search.DrawToBitmap(image,search.ClientRectangle);image.Save(Path.Combine(output,"search.png"));}
                    Check(AppSearch.Discover().All(a=>File.Exists(a.Path)),"App search returns existing local shortcuts");
                    timer.Stop();File.WriteAllLines(Path.Combine(output,"results.txt"),results);first.Close();
                }
            }
            catch(Exception e) {results.Add("FAIL "+e);File.WriteAllLines(Path.Combine(output,"results.txt"),results);timer.Stop();dock.ReleaseBar();preview?.Close();first.Close();}
        };
        try {Application.Run(first);}finally {if(fixtureId!=null && !fixture!.HasExited) appWindows.CloseWindow(fixtureId);preview?.Dispose();dock.ReleaseBar();TaskbarState.Restore(recovery);}
    }
}
