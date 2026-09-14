using System.Text.Json;
namespace Cave.Desktop;
internal static class WorkstationInputTests
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var harness=new Form{Width=1,Height=1,ShowInTaskbar=false,Opacity=0};
        harness.Shown+=async(_,_)=>
        {
            var results=new List<string>();void Check(bool ok,string name){results.Add((ok?"PASS ":"FAIL ")+name);File.WriteAllLines(Path.Combine(output,"results.txt"),results);}
            try
            {
                bool returned=false;using var station=new VillagerWorkstation((_,_)=>{},()=>returned=true);station.BeginInWorld();
                IEnumerable<Control> Children(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
                void Click(Control control)
                {
                    var point=station.PointToClient(control.PointToScreen(new Point(control.Width/2,control.Height/2)));
                    double u=point.X/(double)station.ClientSize.Width,v=point.Y/(double)station.ClientSize.Height;
                    station.RemotePointer(u,v,"down");station.RemotePointer(u,v,"up");
                }
                void Key(int code,int modifiers=0,string text="")=>station.RemoteInput(JsonSerializer.SerializeToElement(new{kind="key",code,modifiers,text,pressed=true}));
                void Text(string text)=>station.RemoteInput(JsonSerializer.SerializeToElement(new{kind="text",text}));
                var foreground=Native.GetForegroundWindow();var input=Children(station).OfType<TextBox>().Single();Click(input);
                Text("Hello 世界 🐈");Check(input.Text=="Hello 世界 🐈","In-world typing and Unicode paste reach the actual task input");
                Key(8);Check(input.Text=="Hello 世界 ","Backspace deletes a surrogate-pair character as one character");
                Key(65,2);Text("test");Check(input.Text=="test","Select-all and replacement edit the same native field");
                Key(37);Key(46);Check(input.Text=="tes","Arrow movement and delete work without native focus");
                Key(36);Key(39,8);Check(input.SelectedText=="t","Shift-arrow selection preserves the selection anchor");
                Key(35);Key(13);Text("next line");Check(input.Text.EndsWith("next line") && input.Lines.Length==2,"Multiline editing works inside the monitor");
                var transcript=Children(station).OfType<RichTextBox>().Single();string original=transcript.Text;Click(transcript);Text("must not edit");Check(transcript.Text==original,"Transcript remains read-only under remote input");
                Click(Children(station).OfType<Button>().Single(b=>b.Text=="Project folder"));
                Check(Children(station).OfType<Button>().Any(b=>b.Text=="Cancel"),"Project folder opens inside the captured workstation");
                Click(Children(station).OfType<Button>().Single(b=>b.Text=="Cancel"));await Task.Delay(100);
                Check(!Children(station).OfType<Button>().Any(b=>b.Text=="Cancel"),"Inline dialog can be dismissed with an in-world click");
                Check(Native.GetForegroundWindow()==foreground && station.Left< -10000 && !station.ShowInTaskbar,"Remote interaction never activates or displays an external workstation window");
                await Task.Delay(500);Check(station.CapturedFrames>2,"Focused workstation continues streaming its actual controls");
                using(var channel=new Cave.Transport.TvFrameBuffer(station.Channel)){var bytes=channel.Read();Check(bytes is {Length:>1000},"Interactive workstation produces readable screen frames");if(bytes!=null)File.WriteAllBytes(Path.Combine(output,"workstation.jpg"),bytes);}
                var back=Children(station).OfType<Button>().Single(b=>b.Text=="Back to cave");
                var backAt=station.PointToClient(back.PointToScreen(new Point(back.Width/2,back.Height/2)));
                station.RemotePointer(backAt.X/(double)station.Width,backAt.Y/(double)station.Height,"down");station.RemotePointer(-.05,-.05,"cancel");
                station.RemotePointer(backAt.X/(double)station.Width,backAt.Y/(double)station.Height,"up");Check(!returned,"Cancelled pointer press does not activate a button on focus loss");
                Click(back);Check(returned,"Back to cave leaves screen interaction");
            }
            catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());}
            harness.Close();
        };
        Application.Run(harness);
    }
}
