using System.Runtime.InteropServices;
using System.Text.Json;
using System.Drawing.Drawing2D;
namespace Cave.Desktop;
internal sealed class DesktopDock:Form
{
    internal record Slot(string Name,Image? Icon,int Count,string[] Windows);
    private Slot[] slots=[];
    private readonly Action<string,int> dispatch;
    private readonly Action<string[],bool> previews;
    private readonly System.Windows.Forms.Timer hoverTimer=new(){Interval=150};
    private readonly Image art,selection;
    private bool registered,settingBounds,apps;
    private int selected,page=1,hover=-1,hoverTicks;
    private string? systemSelection;
    private int clockMinute=-1;
    private Screen targetScreen=Screen.PrimaryScreen!;
    internal bool Registered=>registered;
    internal bool Apps=>apps;
    internal Slot[] Slots=>slots;
    [StructLayout(LayoutKind.Sequential)] private struct AppbarData {public uint cbSize;public nint hWnd;public uint callback,edge;public Native.Rect rect;public nint param;}
    [DllImport("shell32.dll")] private static extern nuint SHAppBarMessage(uint message,ref AppbarData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterWindowMessage(string value);
    private readonly uint callback=RegisterWindowMessage("CozyCaveDockAppbar");
    internal DesktopDock(Action<string,int> dispatch,Action<string[],bool> previews)
    {
        this.dispatch=dispatch;this.previews=previews;
        Text="Cozy Cave hotbar";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;DoubleBuffered=true;KeyPreview=true;BackColor=Color.FromArgb(31,28,24);
        Image Asset(string name) {using var stream=typeof(DesktopDock).Assembly.GetManifestResourceStream(name)!;using var loaded=Image.FromStream(stream);return new Bitmap(loaded);}
        art=Asset("dock.hotbar.png");selection=Asset("dock.selection.png");
        hoverTimer.Tick+=(_,_)=>
        {
            if(DateTime.Now.Minute!=clockMinute) {clockMinute=DateTime.Now.Minute;Invalidate();}
            if(!Visible) return;int index=SlotAt(PointToClient(Cursor.Position));
            if(index!=hover) {hover=index;hoverTicks=0;}
            if(index>=0 && ++hoverTicks==3 && apps && slots.ElementAtOrDefault(index)?.Windows is {Length:>1} ids) previews(ids,false);
        };hoverTimer.Start();
    }
    protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x80;return p;}}
    protected override bool ShowWithoutActivation=>true;
    private AppbarData Data()=>new(){cbSize=(uint)Marshal.SizeOf<AppbarData>(),hWnd=Handle,callback=callback,edge=3};
    internal void Reserve(Screen screen)
    {
        bool changed=targetScreen.DeviceName!=screen.DeviceName || targetScreen.Bounds!=screen.Bounds;targetScreen=screen;
        if(!registered) {var data=Data();if(SHAppBarMessage(0,ref data)==0) {Program.Log("Dock appbar registration failed");return;}registered=true;changed=true;}
        if(changed) PositionBar();
    }
    private void PositionBar()
    {
        if(!registered || settingBounds) return;settingBounds=true;
        try
        {
            var r=targetScreen.Bounds;var data=Data();data.rect=new Native.Rect{Left=r.Left,Top=r.Top,Right=r.Right,Bottom=r.Bottom};
            SHAppBarMessage(2,ref data);data.rect.Top=data.rect.Bottom-96;SHAppBarMessage(3,ref data);
            Bounds=Rectangle.FromLTRB(data.rect.Left,data.rect.Top,data.rect.Right,data.rect.Bottom);
        }
        finally {settingBounds=false;}
    }
    internal void ReleaseBar()
    {
        if(registered) {var data=Data();SHAppBarMessage(1,ref data);registered=false;}Hide();
    }
    internal void UpdateState(JsonElement state)
    {
        apps=true;systemSelection=state.TryGetProperty("system",out var system)?system.GetString():null;selected=state.GetProperty("selected").GetInt32();page=state.GetProperty("page").GetInt32();
        foreach(var slot in slots) slot.Icon?.Dispose();
        slots=state.GetProperty("slots").EnumerateArray().Select(s=>
        {
            Image? icon=null;
            try {if(s.TryGetProperty("icon",out var p) && p.ValueKind==JsonValueKind.String) {using var stream=new MemoryStream(Convert.FromBase64String(p.GetString()!));using var image=Image.FromStream(stream);icon=new Bitmap(image);}} catch(ArgumentException) {} catch(FormatException) {}
            return new Slot(s.GetProperty("name").GetString()??"",icon,s.GetProperty("count").GetInt32(),s.GetProperty("windows").EnumerateArray().Select(x=>x.GetString()!).ToArray());
        }).ToArray();Invalidate();
    }
    private Rectangle Bar=>new((ClientSize.Width-546)/2,27,546,66);
    private Rectangle SearchButton=>new(Math.Max(8,Bar.Left-228),35,100,42);
    private Rectangle DesktopButton=>new(Math.Max(114,Bar.Left-122),35,112,42);
    private Rectangle SystemButton=>new(Bar.Right+10,35,92,42);
    private Rectangle ClockButton=>new(Bar.Right+108,35,146,42);
    private int SlotAt(Point p)=>Bar.Contains(p)?Math.Clamp((p.X-Bar.Left-3)/60,0,8):-1;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
        g.DrawImage(art,Bar);if(selected>=0)g.DrawImage(selection,new Rectangle(Bar.Left+selected*60-3,Bar.Top-3,72,72));
        using var text=new SolidBrush(Color.FromArgb(240,229,206));using var font=PixelTheme.Font(9);
        string label=$"Apps {page}: "+(systemSelection switch {"search"=>"Search","desktop"=>"Desktop","system"=>"System","notifications"=>"Time & notifications",_=>slots.ElementAtOrDefault(selected)?.Name??""})+"   |   Wheel / 1-9 select   |   Enter: open";
        TextRenderer.DrawText(g,label,font,new Rectangle(0,3,Width,22),Color.Wheat,TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis);
        PixelTheme.Button(g,SearchButton,"Search",systemSelection=="search");PixelTheme.Button(g,DesktopButton,"Desktop",systemSelection=="desktop");
        PixelTheme.Button(g,SystemButton,"System",systemSelection=="system");PixelTheme.Button(g,ClockButton,DateTime.Now.ToString("h:mm tt\nMMM d"),systemSelection=="notifications");
        for(int i=0;i<slots.Length && i<9;i++)
        {
            var slot=slots[i];int x=Bar.Left+9+i*60;
            if(slot.Icon!=null) g.DrawImage(slot.Icon,new Rectangle(x,Bar.Top+9,48,48));
            if(slot.Count>1) {string n=slot.Count.ToString();var size=g.MeasureString(n,font);g.DrawString(n,font,Brushes.Black,x+49-size.Width,Bar.Top+47);g.DrawString(n,font,text,x+48-size.Width,Bar.Top+46);}
            if(apps && slot.Windows.Length>0) g.FillRectangle(Brushes.LightGreen,x+6,Bar.Bottom-7,36,3);
        }
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);Activate();
        if(SearchButton.Contains(e.Location)) {dispatch("search",0);return;}
        if(DesktopButton.Contains(e.Location)) {dispatch("desktop",0);return;}
        if(SystemButton.Contains(e.Location)) {dispatch("system",0);return;}
        if(ClockButton.Contains(e.Location)) {dispatch("notifications",0);return;}
        int index=SlotAt(e.Location);if(index<0) return;selected=index;Invalidate();dispatch("select",index);
        if(e.Button==MouseButtons.Right && apps) dispatch("context",index);
        else if(e.Button==MouseButtons.Left) ActivateSlot(index);
    }
    private void ActivateSlot(int index)
    {
        if(apps && slots.ElementAtOrDefault(index)?.Windows is {Length:>1} ids) previews(ids,true);
        else dispatch("activate",index);
    }
    protected override void OnMouseWheel(MouseEventArgs e) {base.OnMouseWheel(e);dispatch("wheel",e.Delta>0?-1:1);}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.KeyCode>=Keys.D1 && e.KeyCode<=Keys.D9) {selected=e.KeyCode-Keys.D1;dispatch("select",selected);Invalidate();e.SuppressKeyPress=true;}
        else if(e.KeyCode==Keys.Enter) {ActivateSlot(selected);e.SuppressKeyPress=true;}
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==callback && m.WParam==1) PositionBar();
        base.WndProc(ref m);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if(e.CloseReason==CloseReason.UserClosing) {e.Cancel=true;dispatch("hide",0);return;}base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing) {ReleaseBar();hoverTimer.Dispose();foreach(var slot in slots) slot.Icon?.Dispose();art.Dispose();selection.Dispose();}base.Dispose(disposing);
    }
}
