using System.Runtime.InteropServices;
namespace Cave.Desktop;
internal sealed class WindowPreviews:Form
{
    private readonly List<nint> thumbnails=[];
    private AppWindows.Entry[] windows;
    private readonly Action<string>? closeWindow;
    private readonly System.Windows.Forms.Timer refresh=new(){Interval=250};
    private readonly Action<string> activate;
    private readonly Action dismiss;
    private int page;
    private bool chosen;
    internal int ThumbnailCount=>thumbnails.Count;
    [StructLayout(LayoutKind.Sequential)] private struct Properties {public uint flags;public Native.Rect destination,source;public byte opacity;[MarshalAs(UnmanagedType.Bool)] public bool visible;[MarshalAs(UnmanagedType.Bool)] public bool clientOnly;}
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination,nint source,out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail,ref Properties properties);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(nint thumbnail,out Size size);
    internal WindowPreviews(AppWindows.Entry[] windows,Action<string> activate,Action dismiss,Action<string>? closeWindow=null)
    {
        this.windows=windows;this.closeWindow=closeWindow;this.activate=activate;this.dismiss=dismiss;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;DoubleBuffered=true;BackColor=Color.FromArgb(198,198,198);KeyPreview=true;
        ClientSize=new Size(Math.Min(4,windows.Length)*236+16,204);
        Shown+=(_,_)=>{LoadThumbnails();refresh.Start();};
        refresh.Tick+=(_,_)=>{var live=this.windows.Where(w=>long.TryParse(w.Id,out var h)&&Native.IsWindow((nint)h)).ToArray();if(live.Length==this.windows.Length)return;this.windows=live;if(live.Length==0){Close();return;}page=Math.Clamp(page,0,(live.Length-1)/4);ClientSize=new Size(Math.Min(4,live.Length)*236+16,204);LoadThumbnails();};
    }
    protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x80;return p;}}
    protected override bool ShowWithoutActivation=>true;
    private void ClearThumbnails() {foreach(var t in thumbnails) DwmUnregisterThumbnail(t);thumbnails.Clear();}
    private void LoadThumbnails()
    {
        ClearThumbnails();
        for(int i=0;i<Math.Min(4,windows.Length-page*4);i++)
        {
            if(!long.TryParse(windows[page*4+i].Id,out long source) || !Native.IsWindow((nint)source)) continue;
            if(DwmRegisterThumbnail(Handle,(nint)source,out var thumbnail)!=0) continue;
            thumbnails.Add(thumbnail);DwmQueryThumbnailSourceSize(thumbnail,out var size);
            float fit=Math.Min(220f/Math.Max(1,size.Width),140f/Math.Max(1,size.Height));int w=(int)(size.Width*fit),h=(int)(size.Height*fit);int x=16+i*236+(220-w)/2,y=32+(140-h)/2;
            var p=new Properties {flags=1|4|8|16,destination=new Native.Rect {Left=x,Top=y,Right=x+w,Bottom=y+h},opacity=255,visible=true,clientOnly=false};DwmUpdateThumbnailProperties(thumbnail,ref p);
        }
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);ControlPaint.DrawBorder3D(e.Graphics,ClientRectangle,Border3DStyle.Raised);
        using var font=PixelTheme.Font(9);
        for(int i=0;i<Math.Min(4,windows.Length-page*4);i++)
        {
            var r=new Rectangle(12+i*236,28,228,148);e.Graphics.FillRectangle(Brushes.DimGray,r);ControlPaint.DrawBorder3D(e.Graphics,r,Border3DStyle.Sunken);
            TextRenderer.DrawText(e.Graphics,windows[page*4+i].Name,font,new Rectangle(16+i*236,6,188,20),Color.FromArgb(45,40,36),TextFormatFlags.EndEllipsis);
            if(closeWindow!=null) PixelTheme.Button(e.Graphics,CloseBounds(i),"x",CloseBounds(i).Contains(PointToClient(Cursor.Position)));
        }
        TextRenderer.DrawText(e.Graphics,$"<   {page+1}/{Math.Max(1,(windows.Length+3)/4)}   >",font,new Rectangle(12,180,Width-24,22),Color.Black);
    }
    internal static Rectangle CloseBounds(int index)=>new(208+index*236,4,28,24);
    protected override void OnMouseMove(MouseEventArgs e) {base.OnMouseMove(e);Invalidate();}
    protected override void OnMouseLeave(EventArgs e) {base.OnMouseLeave(e);Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.Button!=MouseButtons.Left) return;
        for(int slot=0;slot<Math.Min(4,windows.Length-page*4);slot++) if(closeWindow!=null && CloseBounds(slot).Contains(e.Location)) {closeWindow(windows[page*4+slot].Id);return;}
        if(e.Y>=177) {page=Math.Clamp(page+(e.X<60?-1:1),0,Math.Max(0,(windows.Length-1)/4));LoadThumbnails();return;}
        int i=(e.X-12)/236+page*4;if(e.X>=12 && e.Y>=28 && e.Y<177 && i>=0 && i<windows.Length) {chosen=true;activate(windows[i].Id);Close();}
    }
    protected override void OnDeactivate(EventArgs e) {base.OnDeactivate(e);if(!chosen) Close();}
    protected override void OnKeyDown(KeyEventArgs e) {base.OnKeyDown(e);if(e.KeyCode==Keys.Escape) Close();}
    protected override void OnFormClosed(FormClosedEventArgs e) {ClearThumbnails();base.OnFormClosed(e);if(!chosen) dismiss();}
    protected override void Dispose(bool disposing) {if(disposing) {refresh.Dispose();ClearThumbnails();}base.Dispose(disposing);}
}
