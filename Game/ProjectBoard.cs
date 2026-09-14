using Godot;
using Cave.Core;
using System.Text.Json;
namespace CozyCave;
public partial class Main
{
    private List<StudioProject> studioProjects=[];
    private string? studioSelection,studioActiveProject,studioActiveAgent;
    private string studioError="",studioNote="",studioDirection="";
    private int studioTab,studioJournalPage;
    private bool projectBoardOpen,projectEditing,projectSavePending;
    private StudioProject? projectDraft;
    private void AddProjectBoard(bool equip=true)
    {
        var block=new Decoration("bookshelf",0,0){ProjectBoard=true,Carried=true};state.Decorations.Add(block);
        if(equip)AutoAssignHotbar(block.Id);Changed();RefreshHotbar();
    }
    private void ProjectBoardVisual(Node3D node)
    {
        node.AddChild(new Label3D{Text="Projects",Font=MinecraftWorldFont(),FontSize=24,PixelSize=.005f,Position=new Vector3(0,.6f,-.51f),RotationDegrees=new Vector3(0,180,0),TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest});
    }
    private bool IsProjectBoard(string? id)=>id?.StartsWith("$decor:")==true && state.Decorations.Any(d=>d.Id==id[7..]&&d.ProjectBoard&&!d.Carried);
    private void StudioCommand(string action,object? payload=null)
    {
        if(!bridge.Connected){studioError="Open Minecraft Desktop through its Windows launcher to use projects.";RenderProjectBoard();return;}
        var fields=payload==null?new Dictionary<string,JsonElement>():JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(JsonSerializer.Serialize(payload))!;
        fields["command"]=JsonSerializer.SerializeToElement("projects");fields["action"]=JsonSerializer.SerializeToElement(action);fields["id"]=JsonSerializer.SerializeToElement(studioSelection??"");bridge.Send(fields);
    }
    private void ReceiveProjects(JsonElement p)
    {
        studioProjects=p.GetProperty("projects").Deserialize<List<StudioProject>>(ProjectRules.Json)??[];
        studioActiveProject=p.GetProperty("activeProjectId").GetString();studioActiveAgent=p.GetProperty("activeAgentId").GetString();
        if(projectSavePending && projectDraft!=null && studioProjects.FirstOrDefault(p=>p.Id==studioSelection)?.Revision>projectDraft.Revision){projectSavePending=false;projectEditing=false;projectDraft=null;studioError="";}
        if(!studioProjects.Any(x=>x.Id==studioSelection))studioSelection=studioProjects.FirstOrDefault()?.Id;
        if(projectBoardOpen && !projectEditing && GetViewport().GuiGetFocusOwner() is not (TextEdit or LineEdit))RenderProjectBoard();
    }
    private void ShowProjectBoard(){studioError="";projectEditing=false;RenderProjectBoard();StudioCommand("list");}
    private Label ProjectText(string text,int size=18)
    {
        var label=new Label{Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};label.AddThemeFontSizeOverride("font_size",size);return label;
    }
    private string TeamName(string id)=>state.Agents.FirstOrDefault(a=>a.Id==id)?.Name??"Unavailable villager";
    private void RenderProjectBoard()
    {
        var box=OpenPanel("Projects");projectBoardOpen=true;
        var frame=(PanelContainer)box.GetParent().GetParent();frame.CustomMinimumSize=new Vector2(Mathf.Min(1040,GetViewport().GetVisibleRect().Size.X-32),0);
        var top=new HBoxContainer();box.AddChild(top);var selector=new OptionButton{ClipText=true,CustomMinimumSize=new Vector2(240,0),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};top.AddChild(selector);
        foreach(var p in studioProjects)selector.AddItem(p.Name);selector.Select(studioProjects.FindIndex(p=>p.Id==studioSelection));
        selector.ItemSelected+=i=>{studioSelection=studioProjects[(int)i].Id;studioTab=0;studioJournalPage=0;projectEditing=false;studioNote="";RenderProjectBoard();};
        top.AddChild(Button("New project",()=>StudioCommand("create")));top.AddChild(Button("Refresh",()=>{projectEditing=false;StudioCommand("list");}));
        if(studioError.Length>0){var error=ProjectText(studioError);error.AddThemeColorOverride("font_color",new Color("8e2525"));box.AddChild(error);}
        var project=studioProjects.FirstOrDefault(p=>p.Id==studioSelection);
        if(project==null){box.AddChild(ProjectText(bridge.Connected?"Loading your projects…":"Use the Windows launcher to connect your project studio."));return;}
        if(projectEditing){RenderProjectSettings(box,projectDraft!);return;}
        var status=ProjectText(project.Status+(studioActiveProject==project.Id&&studioActiveAgent!=null?" · "+TeamName(studioActiveAgent):""),20);box.AddChild(status);
        var actions=new HFlowContainer();box.AddChild(actions);
        var settings=Button("Goal & team",()=>{projectDraft=JsonSerializer.Deserialize<StudioProject>(JsonSerializer.Serialize(project),ProjectRules.Json);projectEditing=true;RenderProjectBoard();});settings.Disabled=studioActiveProject==project.Id;actions.AddChild(settings);
        actions.AddChild(Button("Open folder",()=>StudioCommand("open-output",new{path=project.Folder})));
        if(studioActiveProject==project.Id)
        {
            var stop=Button("Stop team",()=>StudioCommand("stop"));stop.Disabled=project.Status=="Stopping";actions.AddChild(stop);
            if(studioActiveAgent!=null){string id=studioActiveAgent;actions.AddChild(Button("View workstation",()=>ViewProjectWorkstation(id)));}
        }
        else
        {
            var plan=Button(project.Tasks.Count>0?"Propose a new plan":"Ask lead for a plan",()=>{studioError="";StudioCommand("plan");});plan.Disabled=project.Team.Count==0||project.Goal.Length==0||project.Folder.Length==0||studioActiveProject!=null;actions.AddChild(plan);
            if(project.Status=="Review ready")actions.AddChild(Button("Mark complete",()=>StudioCommand("complete")));
            if(project.Tasks.Count>0 && project.Status!="Complete"){var run=Button(project.Tasks.Any(t=>t.Status=="Done")?"Resume / review":"Start team",()=>{studioError="";StudioCommand("run");});run.Disabled=studioActiveProject!=null;actions.AddChild(run);}
        }
        if(studioActiveProject==project.Id && project.Status!="Stopping")
        {
            var direction=new TextEdit{Text=studioDirection,PlaceholderText="Clarify or redirect the active assignment…",CustomMinimumSize=new Vector2(0,70),WrapMode=TextEdit.LineWrappingMode.Boundary};box.AddChild(direction);direction.TextChanged+=()=>studioDirection=direction.Text;
            box.AddChild(Button("Send direction",()=>{if(studioDirection.Trim().Length==0)return;string text=studioDirection;studioDirection="";GetViewport().GuiReleaseFocus();StudioCommand("redirect",new{text});}));
        }
        var tabs=new HBoxContainer();box.AddChild(tabs);string[] names=["Overview","Assignments","Journal","Outputs"];
        for(int i=0;i<names.Length;i++){int tab=i;var button=Button(names[i],()=>{studioTab=tab;RenderProjectBoard();});button.Disabled=studioTab==i;tabs.AddChild(button);}
        if(project.Question.Length>0)box.AddChild(ProjectText("Needs your attention: "+project.Question));
        if(studioTab==0)
        {
            box.AddChild(ProjectText("Next milestone",20));box.AddChild(ProjectText(project.Milestone.Length>0?project.Milestone:"Choose a small outcome you can test."));
            box.AddChild(ProjectText("Goal",20));box.AddChild(ProjectText(project.Goal));
            box.AddChild(ProjectText(project.Team.Count==0?"Choose Goal & team to assign villagers. Only those villagers will receive work.":"Team: "+string.Join(", ",project.Team.Select(TeamName))+" · Lead: "+TeamName(project.Lead)));
            if(project.Review.Length>0){box.AddChild(ProjectText("Combined review",20));box.AddChild(ProjectText(project.Review));}
        }
        else if(studioTab==1)
        {
            if(project.Tasks.Count==0)box.AddChild(ProjectText("Your lead will propose bounded assignments here. Review the plan before starting the team."));
            foreach(var task in project.Tasks)
            {
                box.AddChild(new HSeparator());box.AddChild(ProjectText(task.Title+" · "+task.Status,20));
                var row=new HBoxContainer();box.AddChild(row);row.AddChild(ProjectText(TeamName(task.AgentId)+(task.DependsOn.Count>0?" · After "+string.Join(", ",task.DependsOn):"")));
                string id=task.AgentId;row.AddChild(Button("Workstation",()=>ViewProjectWorkstation(id)));box.AddChild(ProjectText(task.Instructions));
                if(task.Result.Length>0)box.AddChild(ProjectText(task.Result));
            }
        }
        else if(studioTab==2)
        {
            var note=new TextEdit{Text=studioNote,PlaceholderText="A decision, idea, or milestone worth remembering…",CustomMinimumSize=new Vector2(0,95),WrapMode=TextEdit.LineWrappingMode.Boundary};box.AddChild(note);note.TextChanged+=()=>studioNote=note.Text;
            box.AddChild(Button("Save note",()=>{if(studioNote.Trim().Length==0)return;var text=studioNote;studioNote="";GetViewport().GuiReleaseFocus();StudioCommand("note",new{text});}));
            var pages=Math.Max(1,(project.Journal.Count+49)/50);studioJournalPage=Math.Clamp(studioJournalPage,0,pages-1);
            var pager=new HBoxContainer();box.AddChild(pager);var older=Button("Older",()=>{studioJournalPage++;RenderProjectBoard();});older.Disabled=studioJournalPage>=pages-1;pager.AddChild(older);pager.AddChild(ProjectText($"Journal {studioJournalPage+1}/{pages}"));var newer=Button("Newer",()=>{studioJournalPage--;RenderProjectBoard();});newer.Disabled=studioJournalPage==0;pager.AddChild(newer);
            foreach(var entry in project.Journal.AsEnumerable().Reverse().Skip(studioJournalPage*50).Take(50)){box.AddChild(new HSeparator());box.AddChild(ProjectText(entry.At.ToLocalTime().ToString("MMM d · h:mm tt"),16));box.AddChild(ProjectText(entry.Text));}
        }
        else
        {
            if(project.Outputs.Count==0)box.AddChild(ProjectText("Completed assignments add links to their output files here. Nothing is moved from your project folder."));
            foreach(var path in project.Outputs){var row=new HBoxContainer();box.AddChild(row);row.AddChild(ProjectText(path));row.AddChild(Button("Open",()=>StudioCommand("open-output",new{path})));}
        }
    }
    private void RenderProjectSettings(VBoxContainer box,StudioProject draft)
    {
        void Field(string label,string value,Action<string> changed)
        {box.AddChild(ProjectText(label,16));var input=new LineEdit{Text=value,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};input.TextChanged+=value=>changed(value);box.AddChild(input);}
        Field("Project name",draft.Name,v=>draft.Name=v);
        box.AddChild(ProjectText("What do you want to create?",16));var goal=new TextEdit{Text=draft.Goal,CustomMinimumSize=new Vector2(0,90),WrapMode=TextEdit.LineWrappingMode.Boundary};goal.TextChanged+=()=>draft.Goal=goal.Text;box.AddChild(goal);
        Field("Next milestone",draft.Milestone,v=>draft.Milestone=v);
        var folderRow=new HBoxContainer();box.AddChild(ProjectText("Project folder",16));box.AddChild(folderRow);var folder=new LineEdit{Text=draft.Folder,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};folder.TextChanged+=v=>draft.Folder=v;folderRow.AddChild(folder);folderRow.AddChild(Button("Browse",()=>PickFolder(path=>{draft.Folder=path;if(GodotObject.IsInstanceValid(folder))folder.Text=path;})));
        box.AddChild(ProjectText("Assigned team",20));var team=new HFlowContainer();box.AddChild(team);
        var lead=new OptionButton{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        void RefreshLead(){lead.Clear();foreach(var id in draft.Team)lead.AddItem(TeamName(id));if(!draft.Team.Contains(draft.Lead))draft.Lead=draft.Team.FirstOrDefault()??"";lead.Select(draft.Team.IndexOf(draft.Lead));}
        foreach(var profile in state.Agents)
        {var choice=new CheckButton{Text=profile.Name,ButtonPressed=draft.Team.Contains(profile.Id)};choice.AddThemeColorOverride("font_color",new Color("303030"));choice.AddThemeColorOverride("font_hover_color",new Color("303030"));team.AddChild(choice);choice.Toggled+=selected=>{if(selected&&!draft.Team.Contains(profile.Id))draft.Team.Add(profile.Id);else if(!selected)draft.Team.Remove(profile.Id);RefreshLead();};}
        if(state.Agents.Count==0)box.AddChild(ProjectText("Place a computer to create a villager first."));
        box.AddChild(ProjectText("Lead villager",16));box.AddChild(lead);lead.ItemSelected+=index=>draft.Lead=draft.Team[(int)index];RefreshLead();
        box.AddChild(ProjectText("Changing the goal, folder, milestone or team archives the old plan in the journal.",16));
        var buttons=new HBoxContainer();box.AddChild(buttons);buttons.AddChild(Button("Save project",()=>{GetViewport().GuiReleaseFocus();if(projectSavePending)return;projectSavePending=true;StudioCommand("save",new{project=draft});}));buttons.AddChild(Button("Cancel",()=>{projectEditing=false;RenderProjectBoard();}));
    }
    private void ViewProjectWorkstation(string id)
    {
        var surface=screenSurfaces.FirstOrDefault(s=>s.Role=="desktop"&&s.AgentId==id);
        if(surface==null){studioError="Place this villager's computer to view its workstation.";RenderProjectBoard();return;}
        ClosePanel();activeScreen=surface;FocusTelevision();
    }
}
