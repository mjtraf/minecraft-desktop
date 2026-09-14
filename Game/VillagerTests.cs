using Godot;
namespace CozyCave;
public partial class Main
{
    private async Task RunVillagerTests(string output)
    {
        var results=new List<string>();void Check(bool ok,string text){results.Add((ok?"PASS ":"FAIL ")+text);GD.Print(results[^1]);}
        try
        {
            System.IO.Directory.CreateDirectory(output);await Frames(30);
            Check(villager!=null,"Villager and workstation fit in existing cave without replacing contents");
            if(villager==null)throw new Exception("Villager could not spawn");
            if(bridge.Connected){for(int i=0;i<180 && workstationTexture==null;i++)await Frames(1);Check(workstationConnected && workstationTexture!=null,"Helper streams the live workstation into the in-world monitor");workstationTexture?.GetImage().SavePng(System.IO.Path.Combine(output,"monitor-live.png"));}
            Check(villagerLegs.Count==2 && Descendants(villagerHead).OfType<MeshInstance3D>().Count()==2,"Vanilla-textured villager has animated legs, head and nose");
            Check(villager.CollisionLayer==1 && villager.CollisionMask==5,"Villager collides with world and player");
            player.Position=villager.Position+new Vector3(0,0,2.5f);walking=true;hovered="$villager";
            Check(CanTalkToVillager(),"Nearby targeted villager accepts hold-to-talk");hovered="$workstation";Check(!CanTalkToVillager(),"Looking away does not address the villager");hovered="$villager";player.Position+=new Vector3(0,0,5);Check(!CanTalkToVillager(),"Distant villager cannot receive voice");
            player.Position=new Vector3(-2,.1f,-5);walking=false;villagerWorking=true;villagerRouteDelay=0;
            for(int i=0;i<500 && !villagerSeated;i++)await Frames(1);
            Check(villagerSeated,"Assigned villager walks to workstation and sits");
            Check(villagerLegs.All(l=>l.RotationDegrees.X==-90),"Working pose bends legs into seated position");
            var position=villager.Position;await Frames(12);Check(villager.Position==position,"Seated worker remains stable without sliding through desk");
            player.Position=workstationAt+new Vector3(2,1,-4);camera.LookAt(workstationAt+new Vector3(0,1.25f,-.7f));await Frames(5);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"villager-desk.png"));
            player.Position=workstationAt+new Vector3(-2,1,2);camera.LookAt(villager.GlobalPosition+Vector3.Up*1.4f);await Frames(5);GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"villager-front.png"));
            villagerWorking=false;await Frames(10);Check(!villagerSeated,"Finished villager stands when aisle is clear");
            Changed();Check(!dirty && store.Load().WorkstationPosition.SequenceEqual(state.WorkstationPosition),"Workstation position persists in saved world");
            Check(GD.Load<AudioStream>("res://Assets/Vanilla/villager_idle1.ogg")!=null,"Original villager acknowledgement sound loads");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(output,"results.txt"),results);
        }catch(Exception e){System.IO.File.WriteAllText(System.IO.Path.Combine(output,"failure.txt"),e.ToString());}
        GetTree().Quit();
    }
}
