using Godot;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private async Task RunScreenTests(string output)
    {
        var results=new List<string>();void Check(bool ok,string name){results.Add((ok?"PASS ":"FAIL ")+name);GD.Print(results[^1]);}
        try
        {
            System.IO.Directory.CreateDirectory(output);await Frames(20);
            var block=state.Decorations.First(d=>d.ScreenRole=="tv" && !d.Carried);var id="$decor:"+block.Id;
            Check(screenSurfaces.Any(s=>s.Role=="tv" && s.Blocks.Count==6),"Legacy TV becomes six joined concrete display blocks");
            Check(CanPickUp(id) && TargetShape(id)?.Kind=="black_concrete","Screens use normal block collision and mining targets");
            player.Position=new Vector3(2,1,3);camera.Position=Vector3.Up*1.62f;
            camera.LookAt(SurfaceFor(id)!.Node.GlobalPosition);walking=true;hovered=id;await Frames(3);hovered=id;
            camera.LookAt(SurfaceFor(id)!.Node.GlobalPosition);
            Check(TryScreenAim(SurfaceFor(id)!,out var center) && center.DistanceTo(new Vector2(.5f,.5f))<.03f,"Screen hit maps into normalized browser coordinates");
            screenPress=id;screenPressTime=.1f;HandleScreenRelease(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
            Check(tvFocused && !block.Carried,"Short click zooms into the existing screen without mining");
            LeaveTelevision();walking=true;hovered=id;screenPress=id;screenPressTime=.5f;HandleScreenRelease(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
            Check(!tvFocused && !block.Carried,"Cancelled hold does not send a browser click");
            hovered=id;UpdateBreaking(1.3f,true);
            Check(block.Carried && state.Drops.Any(d=>d.ItemId==block.Id),"Holding to mine returns a physical display block");
            var drop=state.Drops.Single(d=>d.ItemId==block.Id);CollectDrop(drop);
            Check(SupplyItems().Any(i=>i.Id==block.Id),"Collected screen appears in inventory with its identity");
            placingKind=block.Kind;movingId=block.Id;placementPosition=new Vector3(20,1,0);placementValid=true;placementTurn=90;player.RotationDegrees=Vector3.Zero;Place();
            Check(!block.Carried && block.X==20 && block.ScreenRole=="tv","Placed screen retains its display source");
            Check(SurfaceFor(id)!=null,"Placed screen receives a live surface at its new position");
            var save=store.Load();Check(save.Decorations.Any(d=>d.Id==block.Id && d.X==20 && d.ScreenRole=="tv"),"Screen relocation persists in the save");
            var computer=state.Decorations.First(d=>d.ScreenRole=="desktop" && !d.Carried);var computerId="$decor:"+computer.Id;
            activeScreen=SurfaceFor(computerId);player.Position=activeScreen!.Node.GlobalPosition-new Vector3(0,1,2);walking=true;hovered=computerId;
            var oldPosition=camera.Position;OpenVillagerMonitor();
            Check(tvFocused && ScreenInputCommand=="workstation-input" && !walking,"Computer zooms into its in-world display and routes input to the workstation");
            Check(TryScreenAim(activeScreen!,out _),"Zoomed computer screen accepts pointer targeting");
            var obstruction=new StaticBody3D();world.AddChild(obstruction);obstruction.GlobalPosition=camera.GlobalPosition-camera.GlobalBasis.Z*.3f;
            obstruction.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new Vector3(3,3,.1f)}});await Frames(3);
            bool corners=true;
            foreach(var expected in new[]{new Vector2(.1f,.1f),new Vector2(.9f,.1f),new Vector2(.1f,.9f),new Vector2(.9f,.9f)})
            {
                var point=activeScreen!.Node.ToGlobal(new Vector3((.5f-expected.X)*activeScreen.Size.X,(.5f-expected.Y)*activeScreen.Size.Y,0));
                var cursor=camera.UnprojectPosition(point);
                corners &= TryScreenAim(activeScreen,out var actual,cursor)&&actual.DistanceTo(expected)<.01f;
            }
            Check(corners,"Focused computer maps all corners even when a villager or furniture crosses the view");
            obstruction.QueueFree();
            var clickPosition=camera.UnprojectPosition(activeScreen!.Node.ToGlobal(new Vector3(-activeScreen.Size.X*.25f,activeScreen.Size.Y*.25f,0)));
            tvLastAim=new Vector2(.1f,.9f);HandleTvInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=clickPosition});
            Check(tvLastAim.DistanceTo(new Vector2(.75f,.25f))<.01f && tvFocused,"Release uses the current event position and keeps the computer open");
            HandleTvInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=new Vector2(-100,-100)});
            Check(tvLastAim.X<0 && tvFocused,"Releasing outside the screen cannot activate a stale button");
            if(bridge.Connected)
            {
                for(int i=0;i<180 && workstationTexture==null;i++)await Frames(1);
                bridge.Send(new{command="workstation-input",kind="text",text="Summarize this project and suggest the next improvement."});
                await Frames(35);
                Check(workstationConnected && workstationTexture!=null,"The in-world computer receives the actual helper workstation feed");
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"computer-focused.png"));
            }
            HandleTvInput(new InputEventKey{Keycode=Key.Escape,Pressed=true});
            Check(!tvFocused && walking && camera.Position==oldPosition,"Escape restores walking and the previous camera position");
            PickUpObject(computerId);Check(computer.Carried && state.Drops.Any(d=>d.ItemId==computer.Id),"Computer block can be mined without losing its identity");
            player.Position=new Vector3(2,1,3);camera.LookAt(new Vector3(2,3,6.49f));await Frames(4);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"screen-blocks.png"));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(output,"results.txt"),results);
        }
        catch(Exception e){System.IO.File.WriteAllText(System.IO.Path.Combine(output,"failure.txt"),e.ToString());}
        GetTree().Quit();
    }
}
