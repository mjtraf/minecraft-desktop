using System.Text.Json;
using Cave.Core;
namespace Cave.Desktop;
internal sealed partial class DesktopContext
{
    private ProjectStore? projectStore;
    private StudioState? studio;
    private ProjectRunner? projectRunner;
    private sealed class StudioAgent(DesktopContext owner):IProjectAgent
    {
        public Task<string> Execute(string agentId,string projectId,string folder,string prompt,string phase,CancellationToken cancel)
        {
            if(!owner.workstations.TryGetValue(agentId,out var station))throw new InvalidOperationException("The assigned villager is unavailable.");
            if(owner.workstations.Values.Any(w=>w.Working))throw new InvalidOperationException("A villager is already working outside this project. Finish or stop that task first.");
            return station.RunProject(projectId,folder,prompt,phase,cancel);
        }
    }
    private string StudioFolder()
    {
        if(launchArgs.Contains("--test-data"))return Path.GetDirectoryName(Program.DataPath)!;
        foreach(var start in new[]{AppContext.BaseDirectory,Environment.CurrentDirectory})
            for(var at=new DirectoryInfo(start);at!=null;at=at.Parent)
                if(File.Exists(Path.Combine(at.FullName,"project.godot"))&&File.Exists(Path.Combine(at.FullName,"CozyCave.csproj")))return at.FullName;
        return "";
    }
    private void EnsureStudio()
    {
        if(studio!=null)return;projectStore=new ProjectStore(Program.DataPath);studio=projectStore.Load(StudioFolder());projectRunner=new ProjectRunner(studio,projectStore,new StudioAgent(this));projectRunner.Changed+=SendProjects;
    }
    private void SendProjects(){if(studio!=null)Send("projects-state",new {projects=studio.Projects,activeProjectId=projectRunner?.ActiveProjectId,activeAgentId=projectRunner?.ActiveAgentId});}
    private async void HandleProjects(JsonElement message)
    {
        try
        {
            EnsureStudio();string action=message.GetProperty("action").GetString()!;
            if(action=="list"){SendProjects();return;}
            string id=message.TryGetProperty("id",out var identity)?identity.GetString()??"":"";
            var p=studio!.Projects.FirstOrDefault(p=>p.Id==id);
            if(action=="create")
            {
                p=new StudioProject();ProjectRules.Record(p,"Project created. Set its goal, folder and team.");studio.Projects.Add(p);projectStore!.Save(studio);SendProjects();Send("project-selected",new{id=p.Id});return;
            }
            if(p==null)throw new InvalidOperationException("Project not found.");
            if(action=="stop"){projectRunner!.Pause(id);return;}
            if(action is "plan" or "run")
            {
                if(workstations.Values.Any(w=>w.Working))throw new InvalidOperationException("Finish or stop the current villager task before starting a project team.");
                await projectRunner!.Start(p,action=="plan",workstations.Keys);return;
            }
            if(action=="redirect")
            {
                string text=message.GetProperty("text").GetString()?.Trim()??"";
                if(text.Length is <1 or >12000)throw new InvalidOperationException("Enter a short direction for the active assignment.");
                if(projectRunner!.ActiveProjectId!=id || projectRunner.ActiveAgentId is not {} target || !workstations.TryGetValue(target,out var station))throw new InvalidOperationException("No assignment is currently running.");
                await station.RedirectProject(id,text);ProjectRules.Record(p,"Direction to "+station.AgentName+": "+text);projectStore!.Save(studio!);SendProjects();return;
            }
            if(action=="complete")
            {
                if(projectRunner!.ActiveProjectId==id||p.Status!="Review ready")throw new InvalidOperationException("Finish and review the milestone first.");
                p.Status="Complete";ProjectRules.Record(p,"Milestone marked complete: "+p.Milestone);projectStore!.Save(studio!);SendProjects();return;
            }
            if(action=="note")
            {
                string note=message.GetProperty("text").GetString()?.Trim()??"";if(note.Length is <1 or >12000)throw new InvalidOperationException("Write a note of up to 12,000 characters.");ProjectRules.Record(p,note);projectStore!.Save(studio);SendProjects();return;
            }
            if(action=="open-output")
            {
                string path=message.GetProperty("path").GetString()??"";
                if(!p.Outputs.Contains(path) && path!=p.Folder)throw new InvalidOperationException("This file is not a project output.");
                if(!File.Exists(path)&&!Directory.Exists(path))throw new FileNotFoundException("This output is no longer available. Check its location in Explorer.");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});return;
            }
            if(action=="save")
            {
                if(projectRunner!.ActiveProjectId==id)throw new InvalidOperationException("Stop the project before changing its goal or team.");
                var draft=message.GetProperty("project").Deserialize<StudioProject>(ProjectRules.Json)??throw new InvalidDataException();
                if(draft.Revision!=p.Revision)throw new InvalidOperationException("This project changed while you were editing. Reopen its settings before saving.");
                if(string.IsNullOrWhiteSpace(draft.Name)||draft.Name.Length>100||draft.Goal.Length>12000||draft.Milestone.Length>2000)throw new InvalidOperationException("Use a short project name and goal.");
                if(draft.Team.Distinct().Count()!=draft.Team.Count||draft.Team.Count>8||draft.Team.Except(workstations.Keys).Any()||draft.Team.Count>0&&!draft.Team.Contains(draft.Lead))throw new InvalidOperationException("Select a lead from the assigned team.");
                if(draft.Folder.Length>0&&(!Path.IsPathFullyQualified(draft.Folder)||!Directory.Exists(draft.Folder)))throw new InvalidOperationException("Choose an existing folder.");
                bool changed=p.Goal!=draft.Goal||p.Folder!=draft.Folder||p.Lead!=draft.Lead||!p.Team.SequenceEqual(draft.Team)||p.Milestone!=draft.Milestone;
                if(changed && p.Tasks.Count>0){ProjectRules.Record(p,"Previous plan archived.\n"+string.Join("\n",p.Tasks.Select(t=>$"{t.Title} [{t.Status}] {t.Result}")));p.Tasks=[];p.Status="Draft";p.Review="";}
                p.Name=draft.Name.Trim();p.Goal=draft.Goal.Trim();p.Folder=draft.Folder;p.Milestone=draft.Milestone.Trim();p.Team=draft.Team;p.Lead=draft.Lead;ProjectRules.Record(p,"Project settings saved.");projectStore!.Save(studio);SendProjects();return;
            }
        }
        catch(Exception e){Send("project-error",new{text=e.Message});SendProjects();}
    }
}
