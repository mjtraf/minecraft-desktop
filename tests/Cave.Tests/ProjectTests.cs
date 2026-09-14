using Cave.Core;
using System.Text.Json;
internal static class ProjectTests
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var directory=Path.Combine(root,"studio");Directory.CreateDirectory(directory);string output=Path.Combine(directory,"result.txt");File.WriteAllText(output,"fixture");
        var store=new ProjectStore(directory);var state=store.Load(directory);var project=state.Projects.Single();project.Team=["alex","robin"];project.Lead="alex";
        var agent=new FixtureAgent(output);var runner=new ProjectRunner(state,store,agent);
        await runner.Start(project,true,project.Team);
        check(project.Status=="Ready"&&project.Tasks.Count==2,"Lead creates a reviewable plan without starting workers");
        agent.OnReview=()=>{runner.Notify("alex","Needs your input: clarify review");runner.Notify("alex","Personal submission blocked");check(project.Status=="Needs input","Unrelated status does not dismiss a project question");runner.Notify("alex","Working");check(project.Status=="Reviewing"&&project.Question=="","Answering a review question restores the review phase");};
        await runner.Start(project,false,project.Team);agent.OnReview=null;
        check(project.Status=="Review ready"&&project.Tasks.All(t=>t.Status=="Done"),"Project runs assignments and finishes with a combined review");
        check(agent.Seen.SequenceEqual(new[]{"alex:plan","robin:task","alex:task","alex:review"}),"Only assigned teammates run, in dependency order");
        check(agent.HandoffSeen,"Completed work is handed to the dependent teammate");
        check(project.Outputs.SequenceEqual(new[]{output}) && File.ReadAllText(output)=="fixture","Outputs reference real files without moving or rewriting them");
        check(project.Journal.Count>=7,"Milestones, handoffs and review persist in the journal");
        var loaded=store.Load();check(loaded.Projects.Single().Review==project.Review&&loaded.Projects.Single().Team.SequenceEqual(project.Team),"Team and combined review survive restart");
        bool refused=false;try{ProjectRules.ReadPlan("""{"milestone":"Test","tasks":[{"id":"a","agentId":"outsider","title":"No","instructions":"No","dependsOn":[]}]}""",project);}catch(InvalidDataException){refused=true;}
        check(refused,"Lead cannot assign work to a villager outside the team");
        refused=false;try{ProjectRules.ReadPlan("""{"milestone":"Test","tasks":[{"id":"a","agentId":"alex","title":"A","instructions":"A","dependsOn":["b"]},{"id":"b","agentId":"robin","title":"B","instructions":"B","dependsOn":["a"]}]}""",project);}catch(InvalidDataException){refused=true;}
        check(refused,"Circular plans cannot run");
        project.Tasks[1].Status="Queued";agent.Wait=true;var running=runner.Start(project,false,project.Team);await agent.Started.Task;
        refused=false;try{await runner.Start(project,false,project.Team);}catch(InvalidOperationException){refused=true;}
        check(refused,"A second run cannot race the active project");
        runner.Pause(project.Id);await running;check(project.Status=="Paused"&&project.Tasks[0].Status=="Done"&&project.Tasks[1].Status=="Interrupted","Stop preserves finished work and marks interrupted assignments");
        agent.Wait=false;await runner.Start(project,false,project.Team);check(project.Status=="Review ready"&&agent.Seen.Count(s=>s=="robin:task")==1,"Resume skips assignments that already finished");
        project.Status="Running";project.Tasks[1].Status="Working";store.Save(state);var recovered=store.Load().Projects.Single();
        check(recovered.Status=="Paused"&&recovered.Tasks[1].Status=="Interrupted","Restart never silently relaunches unfinished work");
        store.Save(new StudioState{Projects=[recovered]});File.WriteAllText(store.PathName,"invalid");var backup=store.Load();check(backup.Projects.Count==1&&Directory.GetFiles(directory,"projects.json.damaged-*").Length==1,"Damaged project state is preserved while recovering its backup");
        refused=false;try{ProjectRules.Validate(project,["alex"]);}catch(InvalidOperationException){refused=true;}check(refused,"Unavailable teammates block execution rather than being replaced");
    }
    private sealed class FixtureAgent(string output):IProjectAgent
    {
        public Action? OnReview;public readonly List<string> Seen=[];public bool HandoffSeen,Wait;public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> Execute(string id,string projectId,string folder,string prompt,string phase,CancellationToken cancel)
        {
            Seen.Add(id+":"+phase);if(phase=="review")OnReview?.Invoke();
            if(Wait){Started.TrySetResult();await Task.Delay(Timeout.Infinite,cancel);}
            if(phase=="plan")return """{"milestone":"A small prototype","tasks":[{"id":"research","agentId":"robin","title":"Research","instructions":"Research the design","dependsOn":[]},{"id":"build","agentId":"alex","title":"Build","instructions":"Build the prototype","dependsOn":["research"]}]}""";
            if(prompt.Contains("Research completed"))HandoffSeen=true;
            return JsonSerializer.Serialize(new{summary=id=="robin"?"Research completed":"Prototype checked",outputs=new[]{output,"does-not-exist"}});
        }
    }
}
