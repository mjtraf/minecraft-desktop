using System.Text.Json;
namespace Cave.Desktop;
internal static class AgentWorkstationTests
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);using var host=new Form{Width=1,Height=1,ShowInTaskbar=false,Opacity=0};
        host.Shown+=async(_,_)=>
        {
            var results=new List<string>();void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);File.WriteAllLines(Path.Combine(output,"results.txt"),results);}
            IEnumerable<Control> Children(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
            void Click(VillagerWorkstation station,Control control){var point=station.PointToClient(control.PointToScreen(new Point(control.Width/2,control.Height/2)));station.RemotePointer(point.X/(double)station.Width,point.Y/(double)station.Height,"down");station.RemotePointer(point.X/(double)station.Width,point.Y/(double)station.Height,"up");}
            try
            {
                string first=Guid.NewGuid().ToString("N"),second=Guid.NewGuid().ToString("N");
                var alex=new VillagerWorkstation((_,_)=>{},()=>{},first,"Alex");var robin=new VillagerWorkstation((_,_)=>{},()=>{},second,"Robin");
                try
                {
                    alex.OpenInWorld();await Task.Delay(80);
                    Check(Children(alex).OfType<Label>().Any(l=>l.Text.Contains("Name your villager")),"First open requests a name inside the captured workstation");
                    async Task SavePrompt(string? text=null)
                    {
                        if(text!=null)Children(alex).OfType<TextBox>().Single(t=>!t.Multiline).Text=text;
                        Click(alex,Children(alex).OfType<Button>().Single(b=>b.Text=="Save"));await Task.Delay(100);
                    }
                    await SavePrompt("Alex");await SavePrompt(Path.Combine(output,"alex-project"));await SavePrompt("Stay at desk");
                    Check(!Children(alex).OfType<Button>().Any(b=>b.Text=="Save") && Directory.Exists(Path.Combine(output,"alex-project")),"Name, project and approach preference configure without a native dialog");
                    alex.BeginInWorld();robin.BeginInWorld();
                    alex.RemoteInput(JsonSerializer.SerializeToElement(new{kind="text",text="Only Alex sees this"}));
                    robin.RemoteInput(JsonSerializer.SerializeToElement(new{kind="text",text="Only Robin sees this"}));
                    Check(Children(alex).OfType<TextBox>().Single().Text=="Only Alex sees this" && Children(robin).OfType<TextBox>().Single().Text=="Only Robin sees this","Two workstations accept independent keyboard input");
                    Check(alex.Channel!=robin.Channel,"Each computer has its own live frame channel");
                    var memories=new[]{alex,robin}.Select(s=>(VillagerMemory)typeof(VillagerWorkstation).GetField("memory",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(s)!).ToArray();
                    Check(memories[0].Folder!=memories[1].Folder,"New agents start with separate project folders");
                    Check(!memories[0].ApproachForQuestions && memories[0].Configured,"Approach preference is stored per villager");
                    var sessions=new[]{alex,robin}.Select(s=>(VillagerSession)typeof(VillagerWorkstation).GetField("session",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(s)!).ToArray();sessions[0].ThreadId="fixture-alex";sessions[1].ThreadId="fixture-robin";
                    Children(alex).OfType<RichTextBox>().Single().AppendText("Alex history marker");Children(robin).OfType<RichTextBox>().Single().AppendText("Robin history marker");
                    await Task.Delay(400);Check(alex.CapturedFrames>1 && robin.CapturedFrames>1,"Independent displays stream concurrently");
                    Check(alex.Left< -10000 && robin.Left< -10000 && !alex.ShowInTaskbar && !robin.ShowInTaskbar,"Agent displays stay in-world without activating desktop windows");
                }
                finally{alex.Dispose();robin.Dispose();}
                using var reopenedAlex=new VillagerWorkstation((_,_)=>{},()=>{},first);using var reopenedRobin=new VillagerWorkstation((_,_)=>{},()=>{},second);
                var firstMemory=JsonSerializer.Deserialize<VillagerMemory>(File.ReadAllText(Path.Combine(Program.DataPath,"agents",first,"villager-agent.json")))!;
                var secondMemory=JsonSerializer.Deserialize<VillagerMemory>(File.ReadAllText(Path.Combine(Program.DataPath,"agents",second,"villager-agent.json")))!;
                Check(firstMemory.ThreadId=="fixture-alex" && secondMemory.ThreadId=="fixture-robin","Each agent resumes its own saved thread identity");
                Check(firstMemory.Transcript.Contains("Alex history marker") && !firstMemory.Transcript.Contains("Robin history marker") && secondMemory.Transcript.Contains("Robin history marker"),"Agent transcripts remain isolated across restart");
                Check(reopenedAlex.AgentName=="Alex" && reopenedRobin.AgentName=="Robin","Named villagers survive workstation restart");
                Check(!File.Exists(Path.Combine(Program.DataPath,"villager-agent.json")),"Creating new agents never overwrites the original agent history");
            }
            catch(Exception e){Check(false,e.ToString());}
            host.Close();
        };Application.Run(host);
    }
}
