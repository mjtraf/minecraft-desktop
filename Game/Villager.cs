using Godot;
using System.Text.Json;
namespace CozyCave;
public partial class Main
{
    private CharacterBody3D? villager;
    private Node3D villagerPose=null!,villagerHead=null!;
    private readonly List<Node3D> villagerLegs=[];
    private AudioStreamPlayer3D villagerVoice=null!;
    private Label3D villagerLabel=null!;
    private StandardMaterial3D workstationMaterial=null!;
    private Cave.Transport.TvFrameBuffer? workstationFrames;
    private ImageTexture? workstationTexture;
    private bool villagerWorking,villagerSeated,villagerTalking,workstationConnected;
    private string villagerStatus="Ready";
    private float villagerTime,villagerIdle=8,villagerRouteDelay,workstationRetry;
    private readonly Queue<Vector3> villagerRoute=[];
    private Vector3 workstationAt,villagerGoal;
    private async void BuildVillager()
    {
        if(proof)return;await Frames(3);
        var saved=state.WorkstationPosition;workstationAt=saved is {Length:3}?new Vector3(saved[0],saved[1],saved[2]):new Vector3(3,0,2);
        // Never replace a user's placed chest, block or picture to make room for the desk.
        bool ClearStation(Vector3 at)
        {
            var shape=new PhysicsShapeQueryParameters3D {Shape=new BoxShape3D {Size=new Vector3(1.4f,2.3f,2.3f)},Transform=new Transform3D(Basis.Identity,at+new Vector3(0,1.2f,-.6f)),CollisionMask=1};
            return GetWorld3D().DirectSpaceState.IntersectShape(shape,64).All(hit=>
                hit["collider"].AsGodotObject() is Node body && body.HasMeta("item") &&
                ScreenBlock(body.GetMeta("item").AsString()) is {ScreenRole:"desktop"} screen &&
                screen.X==at.X && screen.Z==at.Z && screen.Y==at.Y+1);
        }
        if(!ClearStation(workstationAt))
        {
            var candidates=Enumerable.Range(1,5).SelectMany(x=>Enumerable.Range(-5,11).Select(z=>new Vector3(x,0,z))).OrderBy(at=>at.DistanceSquaredTo(workstationAt));
            foreach(var at in candidates)if(ClearStation(at)){workstationAt=at;break;}
            if(!ClearStation(workstationAt)){Toast("The villager needs a clear two-block space for a workstation.");return;}
        }
        state.WorkstationPosition=[workstationAt.X,workstationAt.Y,workstationAt.Z];
        var desk=new Node3D {Name="VillagerWorkstation",Position=workstationAt};AddChild(desk);
        Box(desk,new Vector3(0,.5f,0),Vector3.One,TextureMaterial("spruce_planks"),true,"$workstation");
        if(!state.ComputerBlockCreated)
        {
            state.ComputerBlockCreated=true;
            var computer=new Cave.Core.Decoration("black_concrete",workstationAt.X,workstationAt.Z){Y=workstationAt.Y+1,ScreenRole="desktop"};
            state.Decorations.Add(computer);CreateDecoration(computer);RebuildScreenSurfaces();
        }
        // Stair chair: lower half plus a raised rear half, using the vanilla planks texture.
        Box(desk,new Vector3(0,.25f,-1),new Vector3(1,.5f,1),TextureMaterial("dark_oak_planks"),true,"$workstation");
        Box(desk,new Vector3(0,.75f,-1.25f),new Vector3(1,.5f,.5f),TextureMaterial("dark_oak_planks"),true,"$workstation");
        var p=state.VillagerPosition;var spawn=p is {Length:3}?new Vector3(p[0],p[1],p[2]):workstationAt+new Vector3(1.5f,0,-1);
        if(!VillagerWalkable(new Vector2I(Mathf.RoundToInt(spawn.X*2),Mathf.RoundToInt(spawn.Z*2)),out spawn))
        {
            bool found=false;for(int x=-4;x<=4 && !found;x++)for(int z=-4;z<=4 && !found;z++)if(VillagerWalkable(new Vector2I(Mathf.RoundToInt(workstationAt.X*2)+x,Mathf.RoundToInt(workstationAt.Z*2)+z),out var at)){spawn=at;found=true;}
        }
        villager=new CharacterBody3D {Name="Villager",Position=spawn+Vector3.Up*.02f,CollisionLayer=1,CollisionMask=1|4,FloorSnapLength=.2f};villager.SetMeta("item","$villager");AddChild(villager);
        villager.AddChild(new CollisionShape3D {Position=new Vector3(0,.95f,0),Shape=new CapsuleShape3D {Radius=.3f,Height=1.9f}});
        villagerPose=new Node3D();villager.AddChild(villagerPose);var texture=Vanilla("entity/villager/villager");
        CatBox(villagerPose,new Vector3(0,1.125f,0),new Vector3(8,12,6),new Vector2(16,20),texture);
        var clothing=Vanilla("entity/villager/type/plains");var robe=new Node3D {Scale=Vector3.One*1.01f};villagerPose.AddChild(robe);
        CatBox(robe,new Vector3(0,1.125f,0),new Vector3(8,12,6),new Vector2(16,20),clothing);
        villagerHead=new Node3D {Position=new Vector3(0,1.5f,0)};villagerPose.AddChild(villagerHead);
        CatBox(villagerHead,new Vector3(0,.3125f,0),new Vector3(8,10,8),Vector2.Zero,texture);
        CatBox(villagerHead,new Vector3(0,.125f,.3125f),new Vector3(2,4,2),new Vector2(24,0),texture);
        CatBox(villagerPose,new Vector3(0,1.16f,.22f),new Vector3(8,4,4),new Vector2(40,38),texture);
        foreach(float x in new[]{-.125f,.125f}){var leg=new Node3D {Position=new Vector3(x,.75f,0)};villagerPose.AddChild(leg);CatBox(leg,new Vector3(0,-.375f,0),new Vector3(4,12,4),new Vector2(0,22),texture);villagerLegs.Add(leg);}
        villagerLabel=new Label3D {Text="Villager",Position=new Vector3(0,2.25f,0),Font=MinecraftWorldFont(),TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest,FontSize=25,PixelSize=.006f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=false};villager.AddChild(villagerLabel);
        villagerVoice=new AudioStreamPlayer3D {UnitSize=2,MaxDistance=10,AttenuationFilterCutoffHz=20500};villager.AddChild(villagerVoice);
        villagerGoal=spawn;Changed();
    }
    private bool VillagerWalkable(Vector2I cell,out Vector3 foot)
    {
        foot=Vector3.Zero;if(Math.Abs(cell.X)>18 || Math.Abs(cell.Y)>16)return false;
        var at=new Vector3(cell.X*.5f,0,cell.Y*.5f);var hit=GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(at+Vector3.Up*.9f,at-Vector3.Up*.5f,1));if(hit.Count==0)return false;
        foot=hit["position"].AsVector3();if(foot.Y>.25f || foot.Y<-.15f || InWater(foot))return false;
        var shape=new PhysicsShapeQueryParameters3D {Shape=new CapsuleShape3D {Radius=.31f,Height=1.95f},Transform=new Transform3D(Basis.Identity,foot+Vector3.Up*1.02f),CollisionMask=1};if(villager!=null)shape.Exclude=[villager.GetRid()];
        return GetWorld3D().DirectSpaceState.IntersectShape(shape,1).Count==0;
    }
    private void PlanVillager(Vector3 goal)
    {
        if(villager==null)return;villagerRoute.Clear();villagerGoal=goal;
        var start=new Vector2I(Mathf.RoundToInt(villager.Position.X*2),Mathf.RoundToInt(villager.Position.Z*2));var queue=new Queue<Vector2I>();queue.Enqueue(start);
        var parents=new Dictionary<Vector2I,Vector2I>();var positions=new Dictionary<Vector2I,Vector3>{{start,villager.Position}};var seen=new HashSet<Vector2I>{start};var best=start;float distance=villager.Position.DistanceSquaredTo(goal);
        while(queue.Count>0 && seen.Count<900)
        {
            var cell=queue.Dequeue();foreach(var offset in new[]{Vector2I.Left,Vector2I.Right,Vector2I.Up,Vector2I.Down})
            {var next=cell+offset;if(!seen.Add(next) || !VillagerWalkable(next,out var at))continue;parents[next]=cell;positions[next]=at;queue.Enqueue(next);float d=at.DistanceSquaredTo(goal);if(d<distance){distance=d;best=next;}}
            if(distance<.09f)break;
        }
        var route=new Stack<Vector3>();for(var cell=best;cell!=start;cell=parents[cell])route.Push(positions[cell]);foreach(var at in route)villagerRoute.Enqueue(at);
    }
    private void UpdateVillager(float dt)
    {
        if(villager==null)return;villagerVoice.StreamPaused=paused||locked;if(paused||locked)return;dt=Math.Min(dt,.1f);villagerTime+=dt;villagerRouteDelay-=dt;
        var approach=workstationAt+new Vector3(0,0,-2);
        if(villagerWorking && !villagerSeated && villagerRouteDelay<=0){PlanVillager(approach);villagerRouteDelay=2;}
        if(villagerWorking && !villagerSeated && villager.Position.DistanceTo(approach)<.35f)
        {villagerSeated=true;villager.Position=workstationAt+new Vector3(0,.5f,-.8f);villager.Rotation=Vector3.Zero;villager.Velocity=Vector3.Zero;villagerRoute.Clear();foreach(var leg in villagerLegs)leg.RotationDegrees=new Vector3(-90,0,0);}
        if(villagerSeated && !villagerWorking)
        {if(VillagerWalkable(new Vector2I(Mathf.RoundToInt(approach.X*2),Mathf.RoundToInt(approach.Z*2)),out var at)){villagerSeated=false;villager.Position=at+Vector3.Up*.02f;foreach(var leg in villagerLegs)leg.Rotation=Vector3.Zero;}}
        if(!villagerSeated)
        {
            if(!villagerWorking && (villagerIdle-=dt)<=0){villagerIdle=8+(float)random.NextDouble()*6;PlanVillager(workstationAt+new Vector3((float)random.NextDouble()*4-2,0,-2-(float)random.NextDouble()*2));}
            var velocity=villager.Velocity;velocity.Y=villager.IsOnFloor()?-.1f:velocity.Y-20*dt;velocity.X=velocity.Z=0;
            if(villagerRoute.TryPeek(out var target)){var dir=target-villager.Position;dir.Y=0;if(dir.Length()<.12f)villagerRoute.Dequeue();else{dir=dir.Normalized();velocity.X=dir.X*1.3f;velocity.Z=dir.Z*1.3f;villager.Rotation=new Vector3(0,Mathf.Atan2(dir.X,dir.Z),0);}}
            villager.Velocity=velocity;villager.MoveAndSlide();for(int i=0;i<villagerLegs.Count;i++)villagerLegs[i].Rotation=new Vector3(velocity.LengthSquared()>.1f?Mathf.Sin(villagerTime*5+i*Mathf.Pi)*.45f:0,0,0);
            state.VillagerPosition=[villager.Position.X,villager.Position.Y,villager.Position.Z];
        }
        var toward=player.GlobalPosition-villager.GlobalPosition;float headYaw=Mathf.Wrap(Mathf.Atan2(toward.X,toward.Z)-villager.Rotation.Y,-Mathf.Pi,Mathf.Pi);villagerHead.Rotation=new Vector3(0,Mathf.Clamp(headYaw,-.8f,.8f),0);
        villagerLabel.Text="Villager\n"+(villagerTalking?"Listening…":villagerWorking?(villagerSeated?"Working…":"Heading to desk…"):villagerStatus);
    }
    private bool CanTalkToVillager()=>villager!=null && walking && panel==null && !paused && !locked && player.GlobalPosition.DistanceTo(villager.GlobalPosition)<4 && hovered=="$villager";
    private void VillagerHmm(){if(villager==null || paused || locked)return;villagerVoice.Stream=GD.Load<AudioStream>($"res://Assets/Vanilla/villager_idle{random.Next(1,4)}.ogg");villagerVoice.VolumeDb=Mathf.LinearToDb(Math.Max(.0001f,state.Settings.EffectsVolume*.65f));villagerVoice.Play();}
    private bool HandleVillagerInput(InputEvent e)
    {
        if(e is not InputEventKey key || key.Keycode!=Key.V || key.Echo)return false;
        if(key.Pressed && CanTalkToVillager()){villagerTalking=true;VillagerHmm();bridge.Send(new {command="villager",action="mic-start"});Toast("Listening to you · Release V to review");GetViewport().SetInputAsHandled();return true;}
        if(!key.Pressed && villagerTalking){villagerTalking=false;bridge.Send(new {command="villager",action="mic-stop"});GetViewport().SetInputAsHandled();return true;}return false;
    }
    private void CancelVillagerSpeech(){if(!villagerTalking)return;villagerTalking=false;bridge.Send(new {command="villager",action="mic-cancel"});}
    private void OpenVillagerMonitor()
    {
        CancelVillagerSpeech();ClosePanel();
        if(activeScreen?.Role!="desktop")activeScreen=screenSurfaces.Where(s=>s.Role=="desktop").OrderBy(s=>s.Node.GlobalPosition.DistanceSquaredTo(camera.GlobalPosition)).FirstOrDefault();
        if(activeScreen==null){Toast("Place a computer block from the workbench to use the workstation.");return;}
        if(camera.GlobalPosition.DistanceTo(activeScreen.Node.GlobalPosition)>6){Toast("Move closer to a computer screen.");return;}
        FocusTelevision();
    }
    private void ReceiveVillager(JsonElement p)
    {
        string text=p.GetProperty("text").GetString()??"Ready";bool wasWorking=villagerWorking;villagerWorking=p.GetProperty("working").GetBoolean();villagerStatus=text.Length>65?text[..65]+"…":text;
        if(wasWorking!=villagerWorking)VillagerHmm();if(!villagerWorking)Toast(text);
    }
    private void ReviewVillagerVoice(string text)
    {
        var box=OpenPanel("Tell your villager", "Review what the microphone heard.");var entry=new TextEdit {Text=text,CustomMinimumSize=new Vector2(640,160),WrapMode=TextEdit.LineWrappingMode.Boundary};box.AddChild(entry);
        box.AddChild(Button("Send task",()=>{var prompt=entry.Text.Trim();if(prompt.Length==0)return;bridge.Send(new {command="villager",action="send",text=prompt});ClosePanelAndResume();VillagerHmm();}));
        box.AddChild(Button("Open workstation",OpenVillagerMonitor));
    }
    private void UpdateWorkstation(float dt)
    {
        if(villager==null || paused || locked)return;if(!workstationConnected && (workstationRetry-=dt)<=0){workstationRetry=5;if(bridge.Connected)bridge.Send(new {command="villager",action="connect"});}
        if(workstationFrames==null)return;
        try{var bytes=workstationFrames.Read();if(bytes==null)return;using var image=new Image();if(image.LoadJpgFromBuffer(bytes)!=Error.Ok)return;if(workstationTexture==null)workstationTexture=ImageTexture.CreateFromImage(image);else workstationTexture.Update(image);workstationMaterial.AlbedoTexture=workstationTexture;workstationMaterial.AlbedoColor=Colors.White;RefreshScreenLabels();}catch{workstationFrames?.Dispose();workstationFrames=null;workstationConnected=false;}
    }
}
