using System.Runtime.InteropServices;
using System.Text.Json;
namespace Cave.Desktop;

// Operate the same native controls that are captured into the in-world monitor.
// No OS mouse/keyboard injection or foreground-window activation is involved.
internal sealed partial class VillagerWorkstation
{
    private Control? remoteTarget, pressedControl;
    private TextBoxBase? dragText;
    private int dragAnchor;
    private bool inWorld;
    private Panel? inlinePrompt;
    private TaskCompletionSource<string?>? promptResult;
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd,uint message,nint wParam,nint lParam);
    internal void BeginInWorld()
    {
        ParkMonitor();inWorld=true;remoteTarget=input;timer.Interval=40;
    }
    internal void EndInWorld(){inWorld=false;dragText=null;pressedControl=null;timer.Interval=250;}
    private Point RemotePoint(double u,double v)=>new((int)Math.Round(u*ClientSize.Width),(int)Math.Round(v*ClientSize.Height));
    private Control? ControlAt(Point point)
    {
        if(!ClientRectangle.Contains(point))return null;
        Control current=this;
        while(current.GetChildAtPoint(point,GetChildAtPointSkip.Invisible|GetChildAtPointSkip.Disabled) is {} child)
        {point=new Point(point.X-child.Left,point.Y-child.Top);current=child;}
        return current==this?null:current;
    }
    internal void RemotePointer(double u,double v,string phase)
    {
        if(phase=="cancel"){dragText=null;pressedControl=null;return;}
        if(!inWorld || suspended)return;
        var point=RemotePoint(u,v);var control=ControlAt(point);
        if(phase=="down")
        {
            pressedControl=control;remoteTarget=control;
            if(control is TextBoxBase text)
            {
                dragText=text;dragAnchor=text.GetCharIndexFromPosition(text.PointToClient(PointToScreen(point)));
                text.Select(dragAnchor,0);
            }
        }
        else if(phase=="up")
        {
            var pressed=pressedControl;pressedControl=null;dragText=null;
            if(pressed==control && control is Button button)button.PerformClick();
        }
        else if(dragText is {} selection)
        {
            int end=selection.GetCharIndexFromPosition(selection.PointToClient(PointToScreen(point)));
            selection.Select(Math.Min(dragAnchor,end),Math.Abs(end-dragAnchor));
        }
    }
    internal void RemoteInput(JsonElement message)
    {
        if(!inWorld || suspended)return;
        string kind=message.GetProperty("kind").GetString()??"";
        if(kind=="wheel")
        {
            var point=RemotePoint(message.GetProperty("u").GetDouble(),message.GetProperty("v").GetDouble());
            if(ControlAt(point) is TextBoxBase area)
            {
                int delta=message.GetProperty("delta").GetInt32();
                SendMessage(area.Handle,0x00B6,0,delta>0?3:-3); // EM_LINESCROLL
            }
            return;
        }
        if(kind=="text")
        {
            if(remoteTarget is TextBoxBase{ReadOnly:false} edit)edit.SelectedText=message.GetProperty("text").GetString()??"";
            return;
        }
        if(!message.GetProperty("pressed").GetBoolean())return;
        int code=message.GetProperty("code").GetInt32(),mods=message.GetProperty("modifiers").GetInt32();
        bool ctrl=(mods&2)!=0,shift=(mods&8)!=0;
        if(code==9){CycleRemoteFocus(shift);return;}
        if(remoteTarget is Button button && code is 13 or 32){button.PerformClick();return;}
        if(remoteTarget is not TextBoxBase box)return;
        if(ctrl && code==13 && box==input){_=Submit(input.Text);return;}
        if(ctrl && code==65){box.SelectAll();return;}
        if(ctrl && code is 67 or 88)
        {
            if(box.SelectionLength>0)Clipboard.SetText(box.SelectedText);
            if(code==88 && !box.ReadOnly)box.SelectedText="";
            return;
        }
        if(ctrl && code==90 && !box.ReadOnly){box.Undo();return;}
        if(code is 37 or 38 or 39 or 40 or 36 or 35 or 33 or 34)
        {
            int start=box.SelectionStart,end=start+box.SelectionLength;
            int caret=shift?(dragAnchor==start?end:start):code==37?start:end;
            if(!shift || box.SelectionLength==0)dragAnchor=caret;
            int next=caret;
            if(code==37)next=ctrl?WordEdge(box.Text,caret,-1):ScalarEdge(box.Text,caret,-1);
            if(code==39)next=ctrl?WordEdge(box.Text,caret,1):ScalarEdge(box.Text,caret,1);
            int line=box.GetLineFromCharIndex(caret),lineStart=box.GetFirstCharIndexFromLine(line);
            if(code is 38 or 40 or 33 or 34)
            {
                int targetLine=Math.Clamp(line+(code is 38 or 33?-1:1)*(code is 33 or 34?12:1),0,Math.Max(0,box.Lines.Length-1));
                int targetStart=box.GetFirstCharIndexFromLine(targetLine);
                next=targetStart<0?box.TextLength:Math.Min(targetStart+Math.Max(0,caret-lineStart),targetStart+(targetLine<box.Lines.Length?box.Lines[targetLine].Length:0));
            }
            if(code==36)next=ctrl?0:Math.Max(0,lineStart);
            if(code==35)next=ctrl?box.TextLength:Math.Max(0,lineStart)+(line<box.Lines.Length?box.Lines[line].Length:0);
            if(!shift && box.SelectionLength>0 && code is 37 or 39)next=code==37?start:end;
            box.Select(shift?Math.Min(dragAnchor,next):next,shift?Math.Abs(next-dragAnchor):0);box.ScrollToCaret();return;
        }
        if(box.ReadOnly)return;
        if(code is 8 or 46)
        {
            if(box.SelectionLength==0)
            {
                int at=box.SelectionStart,next=ctrl?WordEdge(box.Text,at,code==8?-1:1):ScalarEdge(box.Text,at,code==8?-1:1);
                box.Select(Math.Min(at,next),Math.Abs(at-next));
            }
            box.SelectedText="";return;
        }
        if(code==13){box.SelectedText=Environment.NewLine;return;}
        if(!ctrl && (mods&5)==0 && message.TryGetProperty("text",out var text))box.SelectedText=text.GetString()??"";
    }
    private static int ScalarEdge(string text,int at,int step)
    {
        int next=Math.Clamp(at+step,0,text.Length);
        if(next>0 && next<text.Length && char.IsLowSurrogate(text[next]) && char.IsHighSurrogate(text[next-1]))next+=step;
        return Math.Clamp(next,0,text.Length);
    }
    private static int WordEdge(string text,int at,int step)
    {
        int next=ScalarEdge(text,at,step);
        while(next>0 && next<text.Length && !char.IsWhiteSpace(text[step<0?next-1:next]))next=ScalarEdge(text,next,step);
        return next;
    }
    private void CycleRemoteFocus(bool reverse)
    {
        IEnumerable<Control> Walk(Control parent)
        {foreach(Control child in parent.Controls){if(!child.Visible || !child.Enabled)continue;if(child is Button or TextBoxBase)yield return child;foreach(var nested in Walk(child))yield return nested;}}
        var controls=Walk(inlinePrompt??(Control)this).ToList();if(controls.Count==0)return;
        int at=controls.IndexOf(remoteTarget!);remoteTarget=controls[(at+(reverse?-1:1)+controls.Count)%controls.Count];
    }
    private void DrawRemoteCaret(Bitmap image)
    {
        if(inWorld && remoteTarget is Button button){using var g=Graphics.FromImage(image);using var p=new Pen(Color.FromArgb(255,224,144),2);g.DrawRectangle(p,new Rectangle(PointToClient(button.PointToScreen(Point.Empty)),button.Size));}
        if(!inWorld || remoteTarget is not TextBoxBase box || box.ReadOnly || box.SelectionLength>0 || Environment.TickCount64%1000>500)return;
        var at=PointToClient(box.PointToScreen(box.GetPositionFromCharIndex(box.SelectionStart)));
        using var graphics=Graphics.FromImage(image);using var pen=new Pen(Color.Black,2);
        graphics.SetClip(new Rectangle(PointToClient(box.PointToScreen(Point.Empty)),box.ClientSize));graphics.DrawLine(pen,at.X,at.Y,at.X,at.Y+box.Font.Height);
    }
    private Task<string?> PromptInWorld(string title,string text,string initial,IEnumerable<string>? choices=null,bool secret=false)
    {
        promptResult?.TrySetResult(null);inlinePrompt?.Dispose();
        var result=promptResult=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var panel=inlinePrompt=new Panel{Bounds=new Rectangle(20,65,960,560),BackColor=Color.FromArgb(198,193,178),Padding=new Padding(16)};Controls.Add(panel);panel.BringToFront();
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};panel.Controls.Add(layout);
        layout.Controls.Add(new Label{Text=title+"\n\n"+text,Width=890,Height=110,ForeColor=Color.Black});
        var edit=new TextBox{Text=initial,Width=890,UseSystemPasswordChar=secret};layout.Controls.Add(edit);
        foreach(var value in choices??[]){var option=new Button{Text=value,AutoSize=true};option.Click+=(_,_)=>edit.Text=value;layout.Controls.Add(option);}
        void Finish(string? answer){inlinePrompt=null;panel.Dispose();remoteTarget=input;promptResult=null;result.TrySetResult(answer);}
        var ok=new Button{Text="Save",AutoSize=true};ok.Click+=(_,_)=>Finish(edit.Text);layout.Controls.Add(ok);
        var cancel=new Button{Text="Cancel",AutoSize=true};cancel.Click+=(_,_)=>Finish(null);layout.Controls.Add(cancel);remoteTarget=edit;
        return result.Task;
    }
}
