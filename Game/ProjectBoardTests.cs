using Godot;
using Cave.Core;
using System.Text.Json;
namespace CozyCave;
public partial class Main
{
    private async Task RunProjectBoardTests(string output)
    {
        var checks=new List<string>();void Check(bool ok,string text){checks.Add((ok?"PASS ":"FAIL ")+text);GD.Print(checks[^1]);}
        try
        {
            System.IO.Directory.CreateDirectory(output);await Frames(25);
            var board=state.Decorations.Single(d=>d.ProjectBoard);Check(board.Carried&&board.Kind=="bookshelf","Project board starts as a real bookshelf item in inventory");
            var project=new StudioProject{Name="Minecraft Desktop",Goal="Build a useful creative project studio with clear assignments and reviewable results.",Milestone="Complete one small improvement through the project board.",Folder=store.DirectoryPath};
            if(bridge.Connected)
            {
                ShowProjectBoard();for(int i=0;i<180&&studioProjects.Count==0;i++)await Frames(1);
                Check(studioProjects.Count==1&&studioProjects[0].Name=="Minecraft Desktop","Windows helper seeds Minecraft Desktop as the first project");project=studioProjects[0];ClosePanelAndResume();
            }
            else ReceiveProjects(JsonSerializer.SerializeToElement(new{projects=new[]{project},activeProjectId=(string?)null,activeAgentId=(string?)null}));
            _UnhandledInput(new InputEventKey{Keycode=Key.P,Pressed=true});await Frames(4);
            Check(projectBoardOpen&&panel!=null&&!walking,"P opens the project board without walking to a block");
            studioError="";RenderProjectBoard();await Frames(3);
            Check(Descendants(panel!).OfType<Button>().Single(b=>b.Text=="Ask lead for a plan").Disabled,"Project cannot recruit villagers until a team is explicitly assigned");
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"overview.png"));
            Descendants(panel!).OfType<Button>().Single(b=>b.Text=="Goal & team").EmitSignal(Godot.Button.SignalName.Pressed);await Frames(3);
            Check(projectEditing&&projectDraft!=project,"Project settings edit a draft without silently changing saved state");
            var member=Descendants(panel!).OfType<CheckButton>().First();member.ButtonPressed=true;await Frames(2);
            Check(projectDraft!.Team.Count==1&&projectDraft.Lead==projectDraft.Team[0],"Team selection also supplies an explicit lead");
            var nameInput=Descendants(panel!).OfType<LineEdit>().First(field=>field.Text=="Minecraft Desktop");nameInput.Text="Creative Studio";nameInput.EmitSignal(LineEdit.SignalName.TextChanged,nameInput.Text);
            Check(project.Name=="Minecraft Desktop","Typing a new name does not mutate the stored project");
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"settings.png"));
            Descendants(panel!).OfType<Button>().Single(b=>b.Text=="Cancel").EmitSignal(Godot.Button.SignalName.Pressed);await Frames(2);
            Check(!projectEditing&&studioProjects.Single().Team.Count==0,"Cancel discards unsaved team changes");
            if(bridge.Connected)
            {
                Descendants(panel!).OfType<Button>().Single(b=>b.Text=="Goal & team").EmitSignal(Godot.Button.SignalName.Pressed);await Frames(2);
                Descendants(panel!).OfType<CheckButton>().First().ButtonPressed=true;
                Descendants(panel!).OfType<Button>().Single(b=>b.Text=="Save project").EmitSignal(Godot.Button.SignalName.Pressed);
                for(int i=0;i<180&&projectEditing;i++)await Frames(1);
                Check(!projectEditing && studioProjects.Single().Team.Count==1,"Assigned team saves through IPC and refreshes the board");
                var path=System.IO.Path.Combine(store.DirectoryPath,"desktop-helper","projects.json");
                var saved=JsonSerializer.Deserialize<StudioState>(System.IO.File.ReadAllText(path),ProjectRules.Json)!;
                Check(saved.Projects.Single().Team.Count==1&&saved.Projects.Single().Folder==project.Folder,"Project folder and team persist in the helper save");
            }
            _Input(new InputEventKey{Keycode=Key.Escape,Pressed=true});Check(panel==null&&walking,"Escape closes the board and resumes walking");
            board.Carried=false;board.X=20;board.Y=1;board.Z=0;CreateDecoration(board);Check(IsProjectBoard("$decor:"+board.Id),"Placed bookshelf opens the same project board");
            Save();Check(store.Load().Decorations.Single(d=>d.Id==board.Id).ProjectBoard,"Project board identity survives save and reload");
        }
        catch(Exception e){checks.Add("FAIL "+e);}
        System.IO.File.WriteAllLines(System.IO.Path.Combine(output,"results.txt"),checks);GetTree().Quit();
    }
}
