using System.Text.Json;
using Cave.Core;
namespace Cave.Desktop;
internal static class ProjectAgentTests
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);using var host=new Form{Width=1,Height=1,ShowInTaskbar=false,Opacity=0};
        host.Shown+=async(_,_)=>
        {
            var results=new List<string>();void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);File.WriteAllLines(Path.Combine(output,"results.txt"),results);}
            try
            {
                using var station=new VillagerWorkstation((_,_)=>{},()=>{});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(75));
                string project=Guid.NewGuid().ToString("N");
                var text=await station.RunProject(project,output,"This is a connection check. Do not use tools, read files, edit files or delegate. Return summary exactly 'studio adapter connected' and outputs [].","review",timeout.Token).WaitAsync(TimeSpan.FromSeconds(85));
                var result=JsonSerializer.Deserialize<ProjectResult>(text,ProjectRules.Json)!;Check(result.Summary=="studio adapter connected","Live Codex returns the structured project result through App Server");
                var memory=JsonSerializer.Deserialize<VillagerMemory>(File.ReadAllText(Path.Combine(Program.DataPath,"villager-agent.json")))!;
                Check(memory.ThreadId==null && memory.ProjectThreads.ContainsKey(project),"Project context is saved separately from the villager's personal conversation");
                string thread=memory.ProjectThreads[project];
                await station.RunProject(project,output,"Again, do not use tools or edit files. Return summary exactly 'resumed' and outputs [].","review",timeout.Token).WaitAsync(TimeSpan.FromSeconds(85));
                memory=JsonSerializer.Deserialize<VillagerMemory>(File.ReadAllText(Path.Combine(Program.DataPath,"villager-agent.json")))!;
                Check(memory.ProjectThreads[project]==thread && memory.ThreadId==null,"Later assignments resume the same project thread without replacing personal history");
            }
            catch(Exception e){Check(false,e.ToString());}
            host.Close();
        };Application.Run(host);
    }
}
