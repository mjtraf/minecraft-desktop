using Cave.Core;
using Godot;
namespace CozyCave;
public partial class Main
{
    private async Task RunVillagerTests(string output)
    {
        var results=new List<string>();void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);GD.Print(results[^1]);}
        try
        {
            System.IO.Directory.CreateDirectory(output);await Frames(30);var a=agents[AgentRegistry.LegacyId];
            Check(a.Body!=null,"Villager and workstation fit in existing cave without replacing contents");
            if(a.Body==null)throw new Exception("Villager could not spawn");
            var font=MinecraftWorldFont();var glyph=font.GetGlyphIndex(8,'i',0);
            Check(font.GetGlyphUVRect(0,new Vector2I(8,0),glyph).Size.X>=3,"World font retains a drawable region for narrow i glyph");
            Check(font.GetStringSize("Villager Right-click",fontSize:25)==MinecraftFont().GetStringSize("Villager Right-click",fontSize:25),"World font padding preserves text advance and UI font spacing");
            if(bridge.Connected){for(int i=0;i<180 && workstationTexture==null;i++)await Frames(1);Check(workstationConnected && workstationTexture!=null,"Helper streams the live workstation into the in-world monitor");workstationTexture?.GetImage().SavePng(System.IO.Path.Combine(output,"monitor-live.png"));}
            Check(a.Legs.Count==2 && Descendants(a.Head).OfType<MeshInstance3D>().Count()==2,"Vanilla-textured villager has animated legs, head and nose");
            Check(a.Body.CollisionLayer==1 && a.Body.CollisionMask==5,"Villager collides with world and player");
            player.Position=a.Body.Position+new Vector3(0,0,2.5f);walking=true;hovered="$villager";
            Check(CanTalkToVillager(),"Nearby targeted villager accepts hold-to-talk");hovered="$workstation";Check(!CanTalkToVillager(),"Looking away does not address the villager");hovered="$villager";player.Position+=new Vector3(0,0,5);Check(!CanTalkToVillager(),"Distant villager cannot receive voice");
            player.Position=new Vector3(-2,.1f,-5);walking=false;a.Working=true;a.RouteDelay=0;
            for(int i=0;i<500 && !a.Seated;i++)await Frames(1);
            Check(a.Seated,"Assigned villager walks to workstation and sits");
            Check(a.Legs.All(l=>l.RotationDegrees.X==-90),"Working pose bends legs into seated position");
            var position=a.Body.Position;await Frames(12);Check(a.Body.Position==position,"Seated worker remains stable without sliding through desk");
            player.Position=workstationAt+new Vector3(2,1,-4);camera.LookAt(workstationAt+new Vector3(0,1.25f,-.7f));await Frames(5);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"villager-desk.png"));
            player.Position=workstationAt+new Vector3(-2,1,2);camera.LookAt(a.Body.GlobalPosition+Vector3.Up*1.4f);await Frames(5);GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"villager-front.png"));
            a.Working=false;await Frames(10);Check(!a.Seated,"Finished villager stands when aisle is clear");
            Vector3? spot=null;
            for(int x=-5;x<=5 && spot==null;x++)for(int z=-5;z<=5 && spot==null;z++)
                if(new Vector3(x,0,z).DistanceTo(workstationAt)>4 && VillagerWalkable(a,new Vector2I(x*2,z*2),0,out var floor))spot=floor;
            if(spot is not {} clear)throw new Exception("No second workstation test space");
            var table=new Decoration("spruce_planks",clear.X,clear.Z){Y=clear.Y};state.Decorations.Add(table);CreateDecoration(table);
            var computer=new Decoration("black_concrete",clear.X,clear.Z){Y=clear.Y+1,ScreenRole="desktop"};state.Decorations.Add(computer);CreateDecoration(computer);RebuildScreenSurfaces();await Frames(8);
            var robin=Agent(computer.AgentId!);robin.Profile.Name="Robin";
            Check(robin.Profile.Id!=a.Profile.Id && robin.Body!=null,"Placing a separate computer creates a separate villager");
            if(bridge.Connected)
            {
                for(int i=0;i<180 && (robin.Texture==null || a.Texture==null);i++)await Frames(1);
                Check(robin.Connected && a.Connected && robin.Texture!=null && a.Texture!=null && robin.Frames!=a.Frames,"Two computers receive independent live helper frame streams");
            }
            Check(robin.Material!=a.Material && SurfaceFor("$decor:"+computer.Id)?.AgentId==robin.Profile.Id,"Computer screens bind to their own agent and texture");
            var extension=new Decoration("black_concrete",clear.X+1,clear.Z){Y=clear.Y+1,ScreenRole="desktop"};state.Decorations.Add(extension);CreateDecoration(extension);RebuildScreenSurfaces();
            Check(extension.AgentId==robin.Profile.Id && state.Agents.Count==2,"Fresh adjacent screen expands a workstation without spawning another villager");
            var body=robin.Body;computer.Carried=true;extension.Carried=true;RebuildScreenSurfaces();
            Check(agents[robin.Profile.Id].Body==body && !robin.HasScreen,"Collecting a computer preserves its named villager and identity");
            hovered="$villager:"+robin.Profile.Id;OpenVillagerMonitor();Check(!tvFocused && activeScreen==null,"An unplaced computer never redirects interaction to another agent");hovered=null;
            computer.Carried=false;extension.Carried=false;RebuildScreenSurfaces();
            Check(robin.HasScreen && agents[robin.Profile.Id].Body==body,"Replacing a computer reconnects the existing villager");
            ReceiveVillager(System.Text.Json.JsonSerializer.SerializeToElement(new{agentId=robin.Profile.Id,text="Needs your input — open workstation",working=true,listening=false}));
            Check(robin.NeedsInput && !a.NeedsInput && robin.Wave>0,"Only the addressed villager responds to its agent's question");
            walking=true;backgroundApp=false;robin.Profile.ApproachForQuestions=true;player.Position=a.Body!.Position;robin.RouteDelay=0;UpdateVillager(.1f);
            Check(robin.Route.Count>0 || robin.Body!.Position.DistanceTo(player.Position)<=2.6f,"Question approach plans a walk toward the player with personal space");
            walking=false;backgroundApp=true;robin.RouteDelay=0;UpdateVillager(.1f);
            Check(robin.Route.Count==0 && panel==null,"A question never follows into another app or opens an intrusive panel");
            walking=true;backgroundApp=false;robin.Profile.ApproachForQuestions=false;robin.RouteDelay=0;UpdateVillager(.1f);
            Check(robin.Route.Count==0,"Stay-at-desk preference disables approach");
            ReceiveVillager(System.Text.Json.JsonSerializer.SerializeToElement(new{agentId=robin.Profile.Id,text="Finished",working=false,listening=false}));
            Check(!robin.NeedsInput && robin.Wave>0,"Completion uses a quiet gesture rather than approaching");
            hovered=null;player.Position=new Vector3(0,1,5);walking=true;
            Check(HandleVillagerInput(new InputEventKey{Keycode=Key.V,Pressed=true}) && voiceHeld && voiceTarget==null,"Hold-to-talk starts away from a targeted villager");
            HandleVillagerInput(new InputEventKey{Keycode=Key.V,Pressed=false});ReviewVillagerVoice("Robin, write a checklist");
            var picker=Descendants(panel!).OfType<OptionButton>().Single();var prompt=Descendants(panel!).OfType<TextEdit>().Single();
            Check(state.Agents[picker.Selected].Id==robin.Profile.Id && prompt.Text=="write a checklist","Distant named voice selects the intended villager for review");ClosePanelAndResume();
            hovered="$villager:"+robin.Profile.Id;walking=true;mouseActionsReady=true;
            _UnhandledInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true});
            Check(panel!=null && Descendants(panel).OfType<LineEdit>().Single().Text==robin.Profile.Name && !tvFocused,"Clicking a villager opens its name instead of the computer");
            ClosePanelAndResume();
            voicePending=true;voiceHeld=true;voiceRecorder=robin.Profile.Id;voiceTarget=robin.Profile.Id;
            ShowVoiceError(System.Text.Json.JsonSerializer.SerializeToElement(new{agentId=robin.Profile.Id,text="No microphone audio was detected."}));
            Check(panel!=null && !voiceHeld && !voicePending && Descendants(panel).OfType<Button>().Any(b=>b.Text=="Windows sound settings"),"Voice failures show a recoverable microphone message instead of hanging");
            Descendants(panel!).OfType<Button>().Single(b=>b.Text=="Type request instead").EmitSignal(Godot.Button.SignalName.Pressed);
            Check(state.Agents[Descendants(panel!).OfType<OptionButton>().Single().Selected].Id==robin.Profile.Id,"Typing after microphone failure preserves the selected villager");ClosePanelAndResume();
            Changed();Check(!dirty && store.Load().WorkstationPosition.SequenceEqual(state.WorkstationPosition),"Workstation position persists in saved world");
            Check(GD.Load<AudioStream>("res://Assets/Vanilla/villager_idle1.ogg")!=null,"Original villager acknowledgement sound loads");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(output,"results.txt"),results);
        }catch(Exception e){System.IO.File.WriteAllText(System.IO.Path.Combine(output,"failure.txt"),e.ToString());}
        GetTree().Quit();
    }
}
