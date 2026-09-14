namespace Cave.Desktop;
internal sealed class AppWindowMenu:Form
{
    internal AppWindowMenu(int count,Action close,Action dismiss)
    {
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
        StartPosition=FormStartPosition.Manual;BackColor=Color.FromArgb(198,198,198);
        ClientSize=new Size(256,56);KeyPreview=true;
        if(count>0)
        {
            var button=PixelTheme.Action(count==1?"Close window":"Close all windows",()=>{Close();close();});
            button.Location=new Point(5,7);Controls.Add(button);
        }
        else Controls.Add(new Label {Text="No open windows",Font=PixelTheme.Font(9),AutoSize=false,Bounds=new Rectangle(12,14,232,30)});
        Deactivate+=(_,_)=>Close();KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape)Close();};
        FormClosed+=(_,_)=>dismiss();
    }
    protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x80;return p;}}
}
