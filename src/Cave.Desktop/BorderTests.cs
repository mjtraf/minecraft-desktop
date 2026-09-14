using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Cave.Desktop;
internal static class BorderTests
{
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);var results=new List<string>();void Check(bool ok,string label)=>results.Add((ok?"PASS ":"FAIL ")+label);
        using var fixture=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--app-window-fixture"){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden})!;
        using var host=new Form {ShowInTaskbar=false,Opacity=0,Width=1,Height=1};using var timer=new System.Windows.Forms.Timer {Interval=350};
        nint target=0;AppWindowBorder? border=null;nint style=0,parent=0;Rectangle saved=default;int step=0,tries=0;
        host.Shown+=(_,_)=>timer.Start();timer.Tick+=(_,_)=>
        {
            try
            {
                if(target==0)
                {
                    Native.EnumWindows((h,_)=>{Native.GetWindowThreadProcessId(h,out uint pid);if(pid==fixture.Id && Native.IsWindowVisible(h))target=h;return true;},0);
                    if(target==0){if(++tries>30)throw new Exception("Fixture did not open");return;}
                    style=Native.GetStyle(target,-16);parent=Native.GetParent(target);Native.GetWindowRect(target,out var r);saved=Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
                    border=new AppWindowBorder(target,"Minecraft border test");border.Track();
                }
                step++;
                if(step==1)
                {
                    Check(border!.Visible,"Windowed app receives border");
                    var opening=border.ClientOpening;
                    Check(opening.Top==opening.Left && border.Height-opening.Bottom==opening.Top && border.Width-opening.Right==opening.Left,"Decorative frame has four equal edges and no duplicate title bar");
                    Check(border.Controls.Count==0,"Decorative frame contains no window control buttons");
                    Check(!border.Region!.IsVisible(border.ClientOpening.Left+40,border.ClientOpening.Top+40),"App interior is a real region hole for native input");
                    Check(Native.GetStyle(target,-16)==style && Native.GetParent(target)==parent,"Border leaves app styles and parent unchanged");
                    Check(Native.GetStyle(border.Handle,-8)==target,"Decoration belongs to target in normal window z-order");

                }
                if(step==2)
                {
                    Check(WindowFromPoint(new Point(saved.Left+50,saved.Top+80))==target,"Pointer inside border reaches the actual app window");
                    using var shot=new Bitmap(border!.Width,border.Height);using var g=Graphics.FromImage(shot);g.CopyFromScreen(border.Location,Point.Empty,border.Size);shot.Save(Path.Combine(output,"window-border.png"));Native.ShowWindow(target,6);
                }
                if(step==3){border!.Track();Check(Native.IsIconic(target)&&!border.Visible,"Native minimize hides the decorative border");Native.ShowWindow(target,9);}
                if(step==4){border!.Track();Check(border.Visible,"Border returns after restore");Native.ShowWindow(target,3);}
                if(step==5){border!.Track();Check(!border.Visible && !AppWindowBorder.CanFrame(target),"Maximized app stays normal");Native.ShowWindow(target,9);}
                if(step==6)
                {
                    var area=Screen.FromHandle(target).Bounds;Native.SetStyle(target,-16,(nint)((long)style&~0x00CF0000L));Native.SetWindowPos(target,0,area.X,area.Y,area.Width,area.Height,0x0030);
                }
                if(step==7){border!.Track();Check(!border.Visible,"Borderless fullscreen app stays normal");Native.SetStyle(target,-16,style);Native.SetWindowPos(target,0,saved.X+50,saved.Y+60,saved.Width+80,saved.Height+30,0x0030);}
                if(step==8)
                {
                    border!.Track();Native.GetWindowRect(target,out var r);var inside=border.ClientOpening;
                    Check(border.Visible && border.Left+inside.Left==r.Left && border.Top+inside.Top==r.Top && inside.Width==r.Right-r.Left && inside.Height==r.Bottom-r.Top,"Frame tracks moved and resized window exactly");
                    border.Dispose();Check(Native.IsWindow(target)&&Native.GetStyle(target,-16)==style && Native.GetParent(target)==parent,"Removing border restores normal appearance without modifying app");
                    border=new AppWindowBorder(target,"Close test");border.Track();Native.PostMessage(target,0x10,0,0);
                }
                if(step==9){Check(!Native.IsWindow(target),"Native close closes target normally");timer.Stop();File.WriteAllLines(Path.Combine(output,"results.txt"),results);host.Close();}
            }
            catch(Exception e){results.Add("FAIL "+e);timer.Stop();File.WriteAllLines(Path.Combine(output,"results.txt"),results);host.Close();}
        };
        try{Application.Run(host);}finally{border?.Dispose();if(Native.IsWindow(target)){Native.SetStyle(target,-16,style);Native.PostMessage(target,0x10,0,0);}}
    }
}
