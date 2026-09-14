using Godot;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private async Task RunWorldInteractionTests(string output)
    {
        var results=new List<string>();void Check(bool ok,string label){results.Add((ok?"PASS ":"FAIL ")+label);GD.Print(results[^1]);}
        try
        {
            System.IO.Directory.CreateDirectory(output);await Frames(20);walking=false;hidden=false;
            Check(ProjectSettings.GetSetting("rendering/gl_compatibility/driver.windows").AsString()=="opengl3_angle","Windows capture uses Direct3D-backed ANGLE renderer");
            player.Position=new Vector3(30,14,26);UpdateRain(2);
            Check(rainCenter==new Vector2I(30,26) && rainColumnCount>100,"Rain follows player to opposite side of valley");
            int radius=state.ValleyRadius;state.ValleyRadius=80;player.Position=new Vector3(-64,25,40);UpdateRain(2);
            Check(outsideRain!.Multimesh.GetInstanceTransform(0).Origin.X< -48,"Rain covers expanded valley");state.ValleyRadius=radius;
            player.Position=new Vector3(0,1,5);UpdateRain(2);
            var mm=outsideRain!.Multimesh;bool dry=true;
            for(int i=0;i<mm.InstanceCount;i++){var t=mm.GetInstanceTransform(i);if(Math.Abs(t.Origin.X)<9 && Math.Abs(t.Origin.Z)<9)dry&=t.Origin.Y-t.Basis.Y.Length()/2>=10;}
            Check(dry,"Rain columns stop above cave roof");
            state.Supplies["stone"]=130;BuildingItems();int before=state.Supplies["stone"];
            ShowBuildingInventory();await Frames(6);
            var root=(GroundDropTarget)panel!;var payload=new Godot.Collections.Dictionary {{"buildkey","$hotbar:0"}};
            Check(!root._CanDropData(root.Inside!().GetCenter(),payload) && root._CanDropData(new Vector2(10,10),payload),"Only drops outside inventory UI throw items");
            Check(!root._CanDropData(new Vector2(10,10),new Godot.Collections.Dictionary {{"path","do-not-delete.txt"}}),"Real file drags cannot enter item disposal");
            // Use the drop handler directly for deterministic package checks; live
            // mouse injection can be displaced by activity in another application.
            root._DropData(new Vector2(10,100),payload);await Frames(8);
            var drop=state.Drops.LastOrDefault(d=>d.Kind=="stone");
            Check(drop is {Count:64} && state.Supplies["stone"]==before-64 && state.HotbarStacks[0]==null,"Outside UI drop removes one owned stack and spawns ground item");
            Check(drop!=null && store.Load().Drops.Any(d=>d.Id==drop.Id && d.Count==64),"Dropped stack count persists");
            if(drop!=null){CollectDrop(drop);Check(state.Supplies["stone"]==before,"Picking up dropped stack restores exact quantity");}
            ShowBuildingInventory();await Frames(4);var items=BuildingItems();int at=items.FindIndex(i=>i.Kind=="stone");
            Check(at>=0 && TossSupply("$slot:"+at),"Storage slots also throw owned stacks");
            var fixture=System.IO.Path.Combine(output,"source 日本語.txt");System.IO.File.WriteAllText(fixture,"Never delete me");
            var chest=new Chest("burn-fixture","Saved name",0,0){Carried=true};state.Chests.Add(chest);library.Add(fixture,chest.Id);BuildingItems();
            int chestSlot=BuildingItems().FindIndex(i=>i.Id==chest.Id);TossSupply("$slot:"+chestSlot);
            var packed=state.Drops.Single(d=>d.ItemId==chest.Id);CollectDrop(packed);
            Check(state.Chests.Contains(chest) && chest.Name=="Saved name" && state.Links.Any(l=>l.ChestId==chest.Id),"Toss and pickup preserve named chest and contents");
            BuildingItems();int chestBar=Array.IndexOf(state.Hotbar,chest.Id);if(chestBar>=0)TossSupply("$hotbar:"+chestBar);else TossSupply("$slot:"+BuildingItems().FindIndex(i=>i.Id==chest.Id));
            packed=state.Drops.Single(d=>d.ItemId==chest.Id);dropNodes[packed.Id].Body.Position=new Vector3(5,0,6);UpdateDrops(.02f);
            Check(!state.Drops.Contains(packed) && !state.Chests.Contains(chest),"Lava destroys dropped chest entity");
            Check(state.Links.Any(l=>l.Path==System.IO.Path.GetFullPath(fixture) && l.ChestId=="inbox") && System.IO.File.ReadAllText(fixture)=="Never delete me","Lava preserves source file and returns chest links to Inbox");
            ClosePanel();walking=false;player.Position=new Vector3(3,1,3);camera.LookAt(new Vector3(5,-.1f,6));await Frames(10);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"lava.png"));
            Check(Descendants(world).Any(n=>n.Name=="DisposalLava"),"Animated lava is installed left of desk");
        }
        catch(Exception e){results.Add("FAIL "+e);}
        System.IO.File.WriteAllLines(System.IO.Path.Combine(output,"results.txt"),results);GetTree().Quit(results.Any(r=>r.StartsWith("FAIL"))?1:0);
    }
}
