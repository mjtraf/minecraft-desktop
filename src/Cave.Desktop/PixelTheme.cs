using System.Drawing.Text;
using System.Runtime.InteropServices;
namespace Cave.Desktop;
internal static class PixelTheme
{
    private static readonly PrivateFontCollection fonts=new();
    private static readonly nint data;
    [DllImport("gdi32.dll")] private static extern nint AddFontMemResourceEx(nint font,uint size,nint reserved,ref uint count);
    static PixelTheme()
    {
        using var stream=typeof(PixelTheme).Assembly.GetManifestResourceStream("dock.pixel.ttf")!;using var memory=new MemoryStream();stream.CopyTo(memory);var bytes=memory.ToArray();data=Marshal.AllocCoTaskMem(bytes.Length);Marshal.Copy(bytes,0,data,bytes.Length);fonts.AddMemoryFont(data,bytes.Length);
        uint count=0;AddFontMemResourceEx(data,(uint)bytes.Length,0,ref count);
    }
    internal static Font Font(float size=10)=>new(fonts.Families[0],size,FontStyle.Regular,GraphicsUnit.Point);
    internal static void Button(Graphics g,Rectangle r,string label,bool hover=false)
    {
        using var fill=new SolidBrush(hover?Color.FromArgb(145,150,162):Color.FromArgb(116,116,116));g.FillRectangle(fill,r);
        using var light=new Pen(Color.FromArgb(218,218,218),2);using var dark=new Pen(Color.FromArgb(48,48,48),2);
        g.DrawLine(light,r.Left+1,r.Bottom-2,r.Left+1,r.Top+1);g.DrawLine(light,r.Left+1,r.Top+1,r.Right-2,r.Top+1);
        g.DrawLine(dark,r.Right-1,r.Top,r.Right-1,r.Bottom-1);g.DrawLine(dark,r.Left,r.Bottom-1,r.Right,r.Bottom-1);
        using var font=Font(9);TextRenderer.DrawText(g,label,font,r,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
    internal static Button Action(string text,Action action)
    {
        var b=new PixelButton {Text=text,Height=42,Width=246,Margin=new Padding(5)};b.Click+=(_,_)=>action();return b;
    }
}
internal sealed class PixelButton:Button
{
    private bool hover;
    internal PixelButton() {SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Font=PixelTheme.Font(9);Cursor=Cursors.Hand;}
    protected override void OnMouseEnter(EventArgs e) {hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e) {hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e) {PixelTheme.Button(e.Graphics,ClientRectangle,Text,hover||Focused);}
}
internal sealed class RecoveryDispatcher:Form
{
    internal Action? Recover;
    protected override void WndProc(ref Message m) {if(m.Msg==0x312 && m.WParam==0xCA) Recover?.Invoke();base.WndProc(ref m);}
}
