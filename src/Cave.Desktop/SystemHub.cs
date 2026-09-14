using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Cave.Desktop;
internal static class SystemHub
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key,byte scan,uint flags,nuint extra);
    internal static void Key(byte key,bool windows=false)
    {
        if(windows) keybd_event(0x5B,0,0,0);keybd_event(key,0,0,0);keybd_event(key,0,2,0);if(windows) keybd_event(0x5B,0,2,0);
    }
    internal static void Open(string uri)=>Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});
    internal static void Action(string command)
    {
        switch(command)
        {
            case "desktop":Key(0x44,true);break;
            case "taskview":Key(9,true);break;
            case "notifications":Key(0x4E,true);break;
            case "quick":Key(0x41,true);break;
            case "tray":Key(0x42,true);break;
            case "windows-search":Key(0x53,true);break;
            case "start":Key(0x5B);break;
            case "vol-down":Key(0xAE);break;
            case "vol-up":Key(0xAF);break;
            case "mute":Key(0xAD);break;
            case "wifi":Open("ms-settings:network-wifi");break;
            case "bluetooth":Open("ms-settings:bluetooth");break;
            case "microphone":Open("ms-settings:privacy-microphone");break;
            case "sound":Open("ms-settings:sound");break;
            case "power":Open("ms-settings:powersleep");break;
            case "settings":Open("ms-settings:");break;
        }
    }
}
internal sealed class SystemPanel:Form
{
    private delegate nint MouseHook(int code,nint message,nint data);
    private MouseHook? outsideClickHook;
    private nint outsideClickHandle;
    [DllImport("user32.dll",SetLastError=true)] private static extern nint SetWindowsHookEx(int id,MouseHook hook,nint module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        outsideClickHook=(code,message,data)=>
        {
            // Focus can change during the desktop handoff without a user click.
            // Dismiss on a new outside press, never pointer motion or button release.
            if(code>=0 && (message==0x201 || message==0x204 || message==0x207) && !IsDisposed)
            {
                var point=new Point(Marshal.ReadInt32(data),Marshal.ReadInt32(data,4));
                if(!Bounds.Contains(point))BeginInvoke(()=>{if(!IsDisposed)Close();});
            }
            return CallNextHookEx(outsideClickHandle,code,message,data);
        };
        outsideClickHandle=SetWindowsHookEx(14,outsideClickHook,GetModuleHandle(null),0);
        if(outsideClickHandle==0)Program.Log("System outside-click hook unavailable: "+Marshal.GetLastWin32Error());
    }
    protected override void Dispose(bool disposing)
    {
        if(outsideClickHandle!=0){UnhookWindowsHookEx(outsideClickHandle);outsideClickHandle=0;}
        base.Dispose(disposing);
    }
    internal SystemPanel(Action restore,Action<string>? actionHandler=null)
    {
        Text="Minecraft Desktop system inventory";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;BackColor=Color.FromArgb(198,198,198);ClientSize=new Size(540,590);KeyPreview=true;
        var heading=new Label {Text="System inventory",Font=PixelTheme.Font(15),AutoSize=true,Location=new Point(18,15)};Controls.Add(heading);
        var status=SystemInformation.PowerStatus;
        string power=status.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery)?"Desktop power":$"Battery {status.BatteryLifePercent*100:0}%";
        Controls.Add(new Label {Text=DateTime.Now.ToString("ddd, MMM d  h:mm tt")+"  |  "+power,Font=PixelTheme.Font(9),AutoSize=true,Location=new Point(18,55)});
        var grid=new FlowLayoutPanel {Location=new Point(9,91),Size=new Size(522,430),AutoScroll=true};Controls.Add(grid);
        foreach(var item in new[]{("Volume -","vol-down"),("Volume +","vol-up"),("Mute / unmute","mute"),("Sound devices","sound"),("Wi-Fi","wifi"),("Bluetooth","bluetooth"),("Microphone settings","microphone"),("Quick settings","quick"),("Notifications","notifications"),("Task View","taskview"),("Show desktop","desktop"),("Windows tray","tray"),("Power settings","power"),("Windows settings","settings"),("Start / power","start")})
        {
            var (label,action)=item;grid.Controls.Add(PixelTheme.Action(label,()=>{if(action is not ("vol-down" or "vol-up" or "mute")) Close();if(actionHandler!=null) actionHandler(action);else SystemHub.Action(action);}));
        }
        grid.Controls.Add(PixelTheme.Action("Restore taskbar",()=>{Close();restore();}));
        var done=PixelTheme.Action("Done",Close);done.Location=new Point(145,531);Controls.Add(done);
    }
    protected override void OnPaint(PaintEventArgs e) {base.OnPaint(e);ControlPaint.DrawBorder3D(e.Graphics,ClientRectangle,Border3DStyle.Raised);}
    protected override void OnKeyDown(KeyEventArgs e) {if(e.KeyCode==Keys.Escape) Close();base.OnKeyDown(e);}
}
internal sealed class AppSearch:Form
{
    protected override void OnDeactivate(EventArgs e) {base.OnDeactivate(e);Close();}
    internal record App(string Name,string Path);
    private readonly TextBox search=new(){BorderStyle=BorderStyle.FixedSingle};
    private readonly FlowLayoutPanel results=new(){AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};
    private App[] apps=[];
    internal static App[] Discover()
    {
        var entries=new List<App>();
        foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Programs),Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)})
        {
            if(!Directory.Exists(root)) continue;var pending=new Stack<string>();pending.Push(root);
            while(pending.Count>0)
            {
                var dir=pending.Pop();
                try
                {
                    entries.AddRange(Directory.EnumerateFiles(dir,"*.lnk").Select(p=>new App(Path.GetFileNameWithoutExtension(p),p)));
                    foreach(var child in Directory.EnumerateDirectories(dir)) if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0) pending.Push(child);
                }
                catch(IOException) {} catch(UnauthorizedAccessException) {}
            }
        }
        return entries.DistinctBy(a=>a.Path,StringComparer.OrdinalIgnoreCase).OrderBy(a=>a.Name).ToArray();
    }
    internal AppSearch()
    {
        Text="Minecraft Desktop app search";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;BackColor=Color.FromArgb(198,198,198);ClientSize=new Size(550,580);KeyPreview=true;
        Controls.Add(new Label {Text="Find an app",Font=PixelTheme.Font(15),AutoSize=true,Location=new Point(18,16)});
        search.SetBounds(18,62,514,34);search.Font=PixelTheme.Font(12);search.BackColor=Color.FromArgb(65,65,65);search.ForeColor=Color.White;search.PlaceholderText="Search installed shortcuts...";Controls.Add(search);
        results.SetBounds(13,110,524,395);Controls.Add(results);
        var native=PixelTheme.Action("Windows search",()=>{Close();SystemHub.Action("windows-search");});native.Location=new Point(18,523);Controls.Add(native);
        var close=PixelTheme.Action("Done",Close);close.Location=new Point(281,523);Controls.Add(close);
        search.TextChanged+=(_,_)=>Fill();Shown+=async(_,_)=>{search.Focus();apps=await Task.Run(Discover);if(!IsDisposed) Fill();};
        search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter && results.Controls.OfType<Button>().FirstOrDefault() is {} first) {first.PerformClick();e.SuppressKeyPress=true;}};
    }
    private void Fill()
    {
        while(results.Controls.Count>0) {var c=results.Controls[0];results.Controls.Remove(c);c.Dispose();}
        var matching=apps.Where(a=>a.Name.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).Take(60).ToArray();
        foreach(var app in matching)
        {
            var button=PixelTheme.Action(app.Name,()=>{try {SystemHub.Open(app.Path);Close();}catch(Exception e){MessageBox.Show(this,e.Message,"Could not open app");}});button.Width=490;results.Controls.Add(button);
        }
        if(matching.Length==0) results.Controls.Add(new Label {Text="No matching shortcuts. Try Windows search.",AutoSize=true,Font=PixelTheme.Font(9)});
    }
    protected override void OnPaint(PaintEventArgs e) {base.OnPaint(e);ControlPaint.DrawBorder3D(e.Graphics,ClientRectangle,Border3DStyle.Raised);}
    protected override void OnKeyDown(KeyEventArgs e) {if(e.KeyCode==Keys.Escape) Close();base.OnKeyDown(e);}
}
