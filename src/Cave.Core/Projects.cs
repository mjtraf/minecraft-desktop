using System.Text.Json;
namespace Cave.Core;

public sealed class StudioProject
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public int Revision {get;set;}
    public string Name {get;set;}="New project";
    public string Goal {get;set;}="";
    public string Folder {get;set;}="";
    public string Milestone {get;set;}="";
    public List<string> Team {get;set;}=[];
    public string Lead {get;set;}="";
    public string Status {get;set;}="Draft";
    public string Question {get;set;}="";
    public List<ProjectAssignment> Tasks {get;set;}=[];
    public List<ProjectJournalEntry> Journal {get;set;}=[];
    public List<string> Outputs {get;set;}=[];
    public string Review {get;set;}="";
}
public sealed class ProjectAssignment
{
    public string Id {get;set;}="";
    public string AgentId {get;set;}="";
    public string Title {get;set;}="";
    public string Instructions {get;set;}="";
    public List<string> DependsOn {get;set;}=[];
    public string Status {get;set;}="Queued";
    public string Result {get;set;}="";
}
public sealed record ProjectJournalEntry(DateTimeOffset At,string Text);
public sealed class ProjectPlan {public string Milestone {get;set;}="";public List<ProjectAssignment> Tasks {get;set;}=[];}
public sealed class ProjectResult {public string Summary {get;set;}="";public List<string> Outputs {get;set;}=[];}
public sealed class StudioState {public int Version {get;set;}=1;public List<StudioProject> Projects {get;set;}=[];}

public static class ProjectRules
{
    public static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
    public static void Validate(StudioProject project,IEnumerable<string> available)
    {
        if(project.Name.Trim().Length is <1 or >100 || project.Goal.Trim().Length is <1 or >12000)throw new InvalidOperationException("Enter a project name and goal.");
        if(!Path.IsPathFullyQualified(project.Folder)||!Directory.Exists(project.Folder))throw new InvalidOperationException("Choose an existing project folder.");
        if(project.Team.Count is <1 or >8 || project.Team.Distinct().Count()!=project.Team.Count || project.Team.Except(available).Any() || !project.Team.Contains(project.Lead))throw new InvalidOperationException("Assign a team and choose its lead.");
    }
    public static ProjectPlan ReadPlan(string text,StudioProject project)
    {
        var plan=JsonSerializer.Deserialize<ProjectPlan>(text,Json)??throw new InvalidDataException("The lead did not return a plan.");
        if(plan.Tasks is null || plan.Tasks.Count is <1 or >12 || string.IsNullOrWhiteSpace(plan.Milestone))throw new InvalidDataException("A plan needs a milestone and 1–12 assignments.");
        var ids=plan.Tasks.Select(t=>t.Id).ToHashSet(StringComparer.Ordinal);
        if(ids.Count!=plan.Tasks.Count || plan.Tasks.Any(t=>string.IsNullOrWhiteSpace(t.Id)||t.Id.Length>64||!project.Team.Contains(t.AgentId)||string.IsNullOrWhiteSpace(t.Title)||string.IsNullOrWhiteSpace(t.Instructions)||t.Instructions.Length>16000||t.DependsOn==null||t.DependsOn.Any(d=>d==t.Id||!ids.Contains(d))))throw new InvalidDataException("The plan contains an invalid task, dependency, or unassigned villager.");
        var done=new HashSet<string>();
        while(done.Count<ids.Count){var next=plan.Tasks.FirstOrDefault(t=>!done.Contains(t.Id)&&t.DependsOn.All(done.Contains));if(next==null)throw new InvalidDataException("The plan has circular dependencies.");done.Add(next.Id);}
        foreach(var task in plan.Tasks){task.Status="Queued";task.Result="";}return plan;
    }
    public static ProjectAssignment? Next(StudioProject project)=>project.Tasks.FirstOrDefault(t=>t.Status!="Done"&&t.DependsOn.All(id=>project.Tasks.Any(d=>d.Id==id&&d.Status=="Done")));
    public static List<string> ExistingOutputs(string folder,IEnumerable<string> paths)=>paths.Take(100).Select(path=>
    {try{return Path.GetFullPath(Path.IsPathFullyQualified(path)?path:Path.Combine(folder,path));}catch{return "";}}).Where(path=>File.Exists(path)||Directory.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public static void Record(StudioProject p,string text){p.Journal.Add(new(DateTimeOffset.Now,text));p.Revision++;}
}
public sealed class ProjectStore(string directory)
{
    public string PathName=>Path.Combine(directory,"projects.json");
    public StudioState Load(string initialFolder="")
    {
        Directory.CreateDirectory(directory);
        if(!File.Exists(PathName)&&!File.Exists(PathName+".bak"))
        {
            var state=new StudioState();var first=new StudioProject{Name="Minecraft Desktop",Goal="Build a useful creative project studio inside Minecraft Desktop, with clear team assignments, reviewable results, and reliable progress between sessions.",Folder=initialFolder,Milestone="Complete one small improvement through the project board."};ProjectRules.Record(first,"Project created. Choose the next improvement and assign your team.");state.Projects.Add(first);Save(state);return state;
        }
        foreach(var path in new[]{PathName,PathName+".bak"})
        {
            try
            {
                var state=JsonSerializer.Deserialize<StudioState>(File.ReadAllText(path),ProjectRules.Json)??throw new InvalidDataException();
                if(state.Version!=1)throw new NotSupportedException("This project save needs a newer version of Minecraft Desktop.");
                if(state.Projects==null || state.Projects.Any(p=>p.Tasks==null||p.Team==null||p.Journal==null||p.Outputs==null))throw new InvalidDataException();
                foreach(var p in state.Projects.Where(p=>p.Status is "Planning" or "Running" or "Reviewing" or "Stopping" or "Needs input"))
                {p.Status=p.Tasks.Count==0?"Draft":"Paused";foreach(var t in p.Tasks.Where(t=>t.Status=="Working"))t.Status="Interrupted";ProjectRules.Record(p,"Session ended before completion. Review the files, then resume when ready.");}
                if(path!=PathName && File.Exists(PathName))File.Move(PathName,PathName+".damaged-"+DateTime.UtcNow.Ticks);
                Save(state);return state;
            }
            catch(Exception e)when(e is IOException or JsonException){ }
        }
        throw new InvalidDataException("Projects and their backup could not be read. Both files have been preserved.");
    }
    public void Save(StudioState state)
    {
        Directory.CreateDirectory(directory);using(var stream=new FileStream(PathName+".tmp",FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,state,ProjectRules.Json);stream.Flush(true);}
        if(File.Exists(PathName))File.Replace(PathName+".tmp",PathName,PathName+".bak");else File.Move(PathName+".tmp",PathName);
    }
}
public interface IProjectAgent
{
    Task<string> Execute(string agentId,string projectId,string folder,string prompt,string phase,CancellationToken cancel);
}
public sealed class ProjectRunner(StudioState state,ProjectStore store,IProjectAgent agent)
{
    private CancellationTokenSource? cancellation;
    private string statusBeforeQuestion="Planning";
    public bool Busy=>cancellation!=null;
    public string? ActiveProjectId {get;private set;}
    public string? ActiveAgentId {get;private set;}
    public event Action? Changed;
    private void Save(){store.Save(state);Changed?.Invoke();}
    public void Notify(string id,string status)
    {
        if(!Busy||id!=ActiveAgentId)return;var p=state.Projects.Single(x=>x.Id==ActiveProjectId);
        if(p.Status=="Stopping")return;
        if(status.Contains("Needs your input",StringComparison.OrdinalIgnoreCase)){if(p.Status!="Needs input")statusBeforeQuestion=p.Status;p.Question=status;p.Status="Needs input";Save();}
        else if(p.Status=="Needs input" && status.StartsWith("Working",StringComparison.OrdinalIgnoreCase)){p.Question="";p.Status=statusBeforeQuestion;Save();}
    }
    public void Pause(string id)
    {
        if(id!=ActiveProjectId)return;var p=state.Projects.Single(p=>p.Id==id);p.Status="Stopping";p.Question="";ProjectRules.Record(p,"Stop requested. Waiting for the active villager to finish stopping.");Save();cancellation?.Cancel();
    }
    public async Task Start(StudioProject p,bool plan,IEnumerable<string> available)
    {
        if(Busy)throw new InvalidOperationException("Another project is working. Stop it or wait for it to finish.");
        ProjectRules.Validate(p,available);if(!plan && p.Tasks.Count==0)throw new InvalidOperationException("Ask your lead for a plan first.");
        if(!plan)ProjectRules.ReadPlan(JsonSerializer.Serialize(new ProjectPlan{Milestone=p.Milestone,Tasks=p.Tasks},ProjectRules.Json),p);
        cancellation=new();var token=cancellation.Token;ActiveProjectId=p.Id;p.Question="";
        try
        {
            if(plan)
            {
                p.Status="Planning";ProjectRules.Record(p,"Lead is proposing a plan.");Save();ActiveAgentId=p.Lead;
                string reply=await agent.Execute(p.Lead,p.Id,p.Folder,$"Project: {p.Name}\nGoal: {p.Goal}\nNext milestone: {p.Milestone}\nAssigned agent IDs: {string.Join(", ",p.Team)}\nPropose a small achievable milestone and 1–12 bounded tasks. Use ONLY these agent IDs. Do not perform the tasks. Return JSON: {{\"milestone\":\"...\",\"tasks\":[{{\"id\":\"task1\",\"agentId\":\"...\",\"title\":\"...\",\"instructions\":\"...\",\"dependsOn\":[]}}]}}","plan",token);
                token.ThrowIfCancellationRequested();var parsed=ProjectRules.ReadPlan(reply,p);p.Tasks=parsed.Tasks;p.Milestone=parsed.Milestone;p.Status="Ready";p.Question="";p.Review="";ProjectRules.Record(p,"Plan ready to review. Start the team when the assignments look right.");
            }
            else
            {
                p.Status="Running";ProjectRules.Record(p,"Team started. Assignments run one at a time in the shared project folder.");Save();
                while(p.Tasks.Any(t=>t.Status!="Done"))
                {
                    token.ThrowIfCancellationRequested();var task=ProjectRules.Next(p)??throw new InvalidOperationException("No task can run until its dependencies are resolved.");
                    bool retry=task.Status=="Interrupted";ActiveAgentId=task.AgentId;task.Status="Working";p.Status="Running";ProjectRules.Record(p,$"Started: {task.Title}");Save();
                    var handoff=string.Join("\n\n",p.Tasks.Where(t=>task.DependsOn.Contains(t.Id)).Select(t=>$"{t.Title}: {t.Result}"));
                    string reply=await agent.Execute(task.AgentId,p.Id,p.Folder,$"Project: {p.Name}\nGoal: {p.Goal}\nMilestone: {p.Milestone}\nYour assignment: {task.Title}\n{task.Instructions}\nCompleted dependency handoffs:\n{handoff}\n{(retry?"The previous attempt was interrupted. Inspect existing changes before retrying and do not blindly repeat external actions.":"")} Complete only this assignment. Do not delegate to other agents. Return JSON with summary (what changed, checks and remaining issues) and outputs (paths to actual files/folders created or updated).","task",token);
                    token.ThrowIfCancellationRequested();var result=JsonSerializer.Deserialize<ProjectResult>(reply,ProjectRules.Json)??throw new InvalidDataException("The villager did not return a result.");
                    if(string.IsNullOrWhiteSpace(result.Summary)||result.Outputs==null)throw new InvalidDataException("The result was incomplete. Inspect the workstation before retrying.");
                    p.Question="";p.Status="Running";task.Result=result.Summary;task.Status="Done";p.Outputs=ProjectRules.ExistingOutputs(p.Folder,p.Outputs.Concat(result.Outputs));ProjectRules.Record(p,$"Finished: {task.Title}\n{result.Summary}");Save();
                }
                p.Status="Reviewing";ActiveAgentId=p.Lead;ProjectRules.Record(p,"Lead is reviewing the combined work.");Save();
                string review=await agent.Execute(p.Lead,p.Id,p.Folder,$"Review this completed milestone against the goal without changing files. Project: {p.Name}\nGoal: {p.Goal}\nMilestone: {p.Milestone}\nResults:\n{string.Join("\n\n",p.Tasks.Select(t=>t.Title+": "+t.Result))}\nCheck the actual outputs. Return JSON with summary (what works, limitations, questions and next steps) and outputs (paths to reviewed files).","review",token);
                token.ThrowIfCancellationRequested();var reviewed=JsonSerializer.Deserialize<ProjectResult>(review,ProjectRules.Json)??throw new InvalidDataException("No review returned.");
                p.Review=reviewed.Summary;p.Outputs=ProjectRules.ExistingOutputs(p.Folder,p.Outputs.Concat(reviewed.Outputs??[]));p.Status="Review ready";p.Question="";ProjectRules.Record(p,"Combined review ready.\n"+p.Review);
            }
        }
        catch(OperationCanceledException){p.Status=p.Tasks.Count==0?"Draft":"Paused";foreach(var t in p.Tasks.Where(t=>t.Status=="Working"))t.Status="Interrupted";ProjectRules.Record(p,"Paused. Completed assignments are preserved.");}
        catch(Exception e){p.Status="Blocked";foreach(var t in p.Tasks.Where(t=>t.Status=="Working"))t.Status="Interrupted";p.Question=e.Message;ProjectRules.Record(p,"Needs attention: "+e.Message);}
        finally{cancellation.Dispose();cancellation=null;ActiveProjectId=ActiveAgentId=null;Save();}
    }
}
