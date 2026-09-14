using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
namespace Cave.Desktop;

// A hollow, non-activating owned window. The app's styles, parent and client area
// are never changed; its native controls remain available underneath the opening.
internal sealed class AppWindowBorder:Form
{
    private readonly nint target;
    private readonly uint processId;
    private readonly Image oak;
    private int edge=7;
    internal nint Target=>target;
    internal Rectangle ClientOpening=>new(edge,edge,Math.Max(1,Width-edge*2),Math.Max(1,Height-edge*2));
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd,uint command);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd,int attribute,out int value,int size);
    internal AppWindowBorder(nint target,string title)
    {
        this.target=target;Native.GetWindowThreadProcessId(target,out processId);
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;DoubleBuffered=true;AutoScaleMode=AutoScaleMode.None;
        StartPosition=FormStartPosition.Manual;Text="Cozy Cave app border";BackColor=Color.FromArgb(49,34,22);
        using var stream=typeof(AppWindowBorder).Assembly.GetManifestResourceStream("border.oak.png")!;using var source=Image.FromStream(stream);oak=new Bitmap(source);
        _=Handle;Native.SetStyle(Handle,-8,target);
    }
    protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
    protected override bool ShowWithoutActivation=>true;
    internal bool TargetAlive
    {
        get {if(!Native.IsWindow(target))return false;Native.GetWindowThreadProcessId(target,out uint pid);return pid==processId;}
    }
    internal static bool CanFrame(nint hwnd)
    {
        if(!Native.IsWindow(hwnd)||!Native.IsWindowVisible(hwnd)||IsZoomed(hwnd)||Native.IsIconic(hwnd))return false;
        if(DwmGetWindowAttribute(hwnd,14,out int cloaked,4)==0 && cloaked!=0)return false;
        if(!Native.GetWindowRect(hwnd,out var r))return false;
        var screen=Screen.FromHandle(hwnd).Bounds;
        return r.Right-r.Left>=160 && r.Bottom-r.Top>=90 && !(r.Left<=screen.Left && r.Top<=screen.Top && r.Right>=screen.Right && r.Bottom>=screen.Bottom);
    }
    internal void Track(string? title=null)
    {
        if(IsDisposed)return;
        if(!TargetAlive||!CanFrame(target)){Hide();return;}
        // Modal dialogs should appear without decoration competing for input.
        if(!Native.GetWindowRect(target,out var r)){Hide();return;}
        int dpi=(int)GetDpiForWindow(target);edge=Math.Max(6,7*dpi/96);
        var desired=Rectangle.FromLTRB(r.Left-edge,r.Top-edge,r.Right+edge,r.Bottom+edge);
        if(Bounds!=desired)
        {
            Bounds=desired;var opening=ClientOpening;var region=new Region(ClientRectangle);region.Exclude(opening);var old=Region;Region=region;old?.Dispose();
        }
        if(!Visible)Show();
        var previous=GetWindow(target,3);if(previous!=Handle)Native.SetWindowPos(Handle,previous,0,0,0,0,0x0013);
        // Ownership keeps the decoration with its app in the normal z-order.
        // No HWND_TOPMOST, activation, or modification of the target window.
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
        int tile=Math.Max(32,edge*6);for(int x=0;x<Width;x+=tile)for(int y=0;y<Height;y+=tile)g.DrawImage(oak,new Rectangle(x,y,tile,tile));
        using var light=new Pen(Color.FromArgb(129,98,63),2);using var dark=new Pen(Color.FromArgb(31,24,19),2);
        g.DrawRectangle(light,1,1,Width-3,Height-3);g.DrawRectangle(dark,ClientOpening);
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x21){m.Result=3;return;} // MA_NOACTIVATE
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing){if(disposing)oak.Dispose();base.Dispose(disposing);}
}
internal sealed class AppWindowBorders:IDisposable
{
    private readonly Dictionary<nint,AppWindowBorder> borders=[];
    private readonly Dictionary<nint,string> titles=[];
    private readonly System.Windows.Forms.Timer timer=new(){Interval=33};
    private nint cave;
    internal bool Enabled {get;set;}=true;
    internal AppWindowBorders(){timer.Tick+=(_,_)=>Tick();timer.Start();}
    internal void Update(AppWindows.Entry[] entries,nint cave)
    {
        this.cave=cave;titles.Clear();
        foreach(var e in entries.Where(e=>!e.Pinned))if(long.TryParse(e.Id,out long h))titles[(nint)h]=e.Name;
        Tick();
    }
    private void Tick()
    {
        foreach(var key in borders.Keys.ToArray())
        {
            var frame=borders[key];if(frame.IsDisposed||!frame.TargetAlive||!titles.ContainsKey(key)){frame.Dispose();borders.Remove(key);continue;}
            if(Enabled)frame.Track(titles[key]);else frame.Hide();
        }
        if(!Enabled)return;
        foreach(var (hwnd,title) in titles)
        {
            if(hwnd==cave||borders.ContainsKey(hwnd)||!AppWindowBorder.CanFrame(hwnd))continue;
            var frame=new AppWindowBorder(hwnd,title);borders[hwnd]=frame;frame.Track();
        }
    }
    internal void HideAll(){Enabled=false;foreach(var frame in borders.Values)frame.Hide();}
    public void Dispose(){timer.Dispose();foreach(var frame in borders.Values)frame.Dispose();borders.Clear();}
}
