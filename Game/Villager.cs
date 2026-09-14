using Godot;
using System.Text.Json;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private sealed class VillagerActor(AgentProfile profile)
    {
        public AgentProfile Profile=profile;
        public CharacterBody3D? Body;
        public Node3D Pose=null!,Head=null!,Arms=null!;
        public readonly List<Node3D> Legs=[];
        public Label3D Label=null!;
        public AudioStreamPlayer3D Voice=null!;
        public StandardMaterial3D Material=new(){AlbedoColor=new Color("18231b"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
        public Cave.Transport.TvFrameBuffer? Frames;
        public ImageTexture? Texture;
        public bool Working,Seated,Connected,NeedsInput,HasScreen;
        public string Status="Ready";
        public float Time,Idle=8,RouteDelay,Retry,Wave,NotifyCooldown;
        public Vector3 Station,Seat,Approach;
        public float Facing;
        public readonly Queue<Vector3> Route=[];
    }
    private readonly Dictionary<string,VillagerActor> agents=[];
    private bool villagersBuilt,voiceHeld,voicePending;
    private string? voiceRecorder,voiceTarget;
    private Vector3 workstationAt;
    private float villagerSyncDelay;
    private ImageTexture? workstationTexture=>agents.GetValueOrDefault(AgentRegistry.LegacyId)?.Texture;
    private bool workstationConnected=>agents.GetValueOrDefault(AgentRegistry.LegacyId)?.Connected==true;
    private async void BuildVillager()
    {
        if(proof)return;await Frames(3);AgentRegistry.Migrate(state);
        var saved=state.WorkstationPosition;workstationAt=new Vector3(saved[0],saved[1],saved[2]);
        bool ClearStation(Vector3 at)
        {
            var shape=new PhysicsShapeQueryParameters3D {Shape=new BoxShape3D {Size=new Vector3(1.4f,2.3f,2.3f)},Transform=new Transform3D(Basis.Identity,at+new Vector3(0,1.2f,-.6f)),CollisionMask=1};
            return GetWorld3D().DirectSpaceState.IntersectShape(shape,64).All(hit=>
                hit["collider"].AsGodotObject() is Node body && body.HasMeta("item") &&
                ScreenBlock(body.GetMeta("item").AsString()) is {ScreenRole:"desktop"} screen &&
                screen.X==at.X && screen.Z==at.Z && screen.Y==at.Y+1);
        }
        if(!state.ComputerBlockCreated && !ClearStation(workstationAt))
        {
            var candidates=Enumerable.Range(1,5).SelectMany(x=>Enumerable.Range(-5,11).Select(z=>new Vector3(x,0,z))).OrderBy(at=>at.DistanceSquaredTo(workstationAt));
            foreach(var at in candidates)if(ClearStation(at)){workstationAt=at;break;}
            if(!ClearStation(workstationAt)){Toast("The villager needs a clear two-block space for a workstation.");return;}
        }
        state.WorkstationPosition=[workstationAt.X,workstationAt.Y,workstationAt.Z];
        // Retain the original desk. Additional workstations use the blocks the player places.
        var desk=new Node3D{Name="VillagerWorkstation",Position=workstationAt};AddChild(desk);
        Box(desk,new Vector3(0,.5f,0),Vector3.One,TextureMaterial("spruce_planks"),true,"$workstation");
        Box(desk,new Vector3(0,.25f,-1),new Vector3(1,.5f,1),TextureMaterial("dark_oak_planks"),true,"$workstation");
        Box(desk,new Vector3(0,.75f,-1.25f),new Vector3(1,.5f,.5f),TextureMaterial("dark_oak_planks"),true,"$workstation");
        if(!state.ComputerBlockCreated)
        {
            state.ComputerBlockCreated=true;
            var block=new Decoration("black_concrete",workstationAt.X,workstationAt.Z){Y=workstationAt.Y+1,ScreenRole="desktop",AgentId=AgentRegistry.LegacyId};
            state.Decorations.Add(block);CreateDecoration(block);
        }
        villagersBuilt=true;RebuildScreenSurfaces();Changed();
    }
    private VillagerActor Agent(string id)
    {
        if(agents.TryGetValue(id,out var a))return a;
        var profile=state.Agents.First(p=>p.Id==id);a=new VillagerActor(profile);agents.Add(id,a);return a;
    }
    private void SyncVillagers()
    {
        if(!villagersBuilt)return;
        foreach(var profile in state.Agents)
        {
            var a=Agent(profile.Id);var screen=state.Decorations.FirstOrDefault(d=>!d.Carried && d.ScreenRole=="desktop" && d.AgentId==profile.Id);
            a.HasScreen=screen!=null;
            if(screen==null){a.Route.Clear();a.Seated=false;continue;}
            var station=new Vector3(screen.X,screen.Y-1,screen.Z);float facing=Mathf.DegToRad(screen.Rotation);
            if(station!=a.Station || facing!=a.Facing){a.Seated=false;a.Route.Clear();a.RouteDelay=0;}
            a.Station=station;a.Facing=facing;var basis=new Basis(Vector3.Up,facing);
            a.Seat=station+basis*new Vector3(0,.5f,-.8f);a.Approach=station+basis*new Vector3(0,0,-2);
            if(VillagerWalkable(a,new Vector2I(Mathf.RoundToInt(a.Approach.X*2),Mathf.RoundToInt(a.Approach.Z*2)),station.Y+1,out var approachFloor))a.Approach.Y=approachFloor.Y;
            if(a.Body!=null)continue;
            if(profile.Id!=AgentRegistry.LegacyId && !HasAgentChair(a))
            {
                var chair=station+basis*new Vector3(0,0,-1);
                if(VillagerWalkable(a,new Vector2I(Mathf.RoundToInt(chair.X*2),Mathf.RoundToInt(chair.Z*2)),chair.Y,out var floor) && Math.Abs(floor.Y-chair.Y)<.15f)
                {var seat=new BuildingBlock{Kind="dark_oak_stairs",X=Mathf.RoundToInt(chair.X),Y=Mathf.RoundToInt(chair.Y),Z=Mathf.RoundToInt(chair.Z),Rotation=(screen.Rotation+90)%360};state.BuildingBlocks.Add(seat);CreateBuildingBlock(seat);dirty=true;}
            }
            var spawn=profile.Position is {Length:3} p?new Vector3(p[0],p[1],p[2]):a.Approach;
            if(!VillagerWalkable(a,new Vector2I(Mathf.RoundToInt(spawn.X*2),Mathf.RoundToInt(spawn.Z*2)),spawn.Y,out spawn))
            {
                bool found=false;
                for(int radius=1;radius<=6 && !found;radius++)for(int x=-radius;x<=radius && !found;x++)for(int z=-radius;z<=radius && !found;z++)
                    if(VillagerWalkable(a,new Vector2I(Mathf.RoundToInt(a.Approach.X*2)+x,Mathf.RoundToInt(a.Approach.Z*2)+z),a.Approach.Y,out var foot)){spawn=foot;found=true;}
                if(!found)continue; // Retry after nearby blocks change; never spawn inside a wall.
            }
            SpawnVillager(a,spawn);
        }
    }
    private void SpawnVillager(VillagerActor a,Vector3 spawn)
    {
        a.Body=new CharacterBody3D {Name="Villager",Position=spawn+Vector3.Up*.02f,CollisionLayer=1,CollisionMask=1|4,FloorSnapLength=.2f};a.Body.SetMeta("item","$villager:"+a.Profile.Id);AddChild(a.Body);
        a.Body.AddChild(new CollisionShape3D {Position=new Vector3(0,.95f,0),Shape=new CapsuleShape3D {Radius=.3f,Height=1.9f}});
        a.Pose=new Node3D();a.Body.AddChild(a.Pose);var texture=Vanilla("entity/villager/villager");
        CatBox(a.Pose,new Vector3(0,1.125f,0),new Vector3(8,12,6),new Vector2(16,20),texture);
        var clothing=Vanilla("entity/villager/type/plains");var robe=new Node3D {Scale=Vector3.One*1.01f};a.Pose.AddChild(robe);
        CatBox(robe,new Vector3(0,1.125f,0),new Vector3(8,12,6),new Vector2(16,20),clothing);
        a.Head=new Node3D {Position=new Vector3(0,1.5f,0)};a.Pose.AddChild(a.Head);
        CatBox(a.Head,new Vector3(0,.3125f,0),new Vector3(8,10,8),Vector2.Zero,texture);
        CatBox(a.Head,new Vector3(0,.125f,.3125f),new Vector3(2,4,2),new Vector2(24,0),texture);
        a.Arms=new Node3D {Position=new Vector3(0,1.16f,.22f)};a.Pose.AddChild(a.Arms);
        CatBox(a.Arms,Vector3.Zero,new Vector3(8,4,4),new Vector2(40,38),texture);
        foreach(float x in new[]{-.125f,.125f}){var leg=new Node3D {Position=new Vector3(x,.75f,0)};a.Pose.AddChild(leg);CatBox(leg,new Vector3(0,-.375f,0),new Vector3(4,12,4),new Vector2(0,22),texture);a.Legs.Add(leg);}
        a.Label=new Label3D {Text=a.Profile.Name,Position=new Vector3(0,2.25f,0),Font=MinecraftWorldFont(),TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest,FontSize=25,PixelSize=.006f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=false};a.Body.AddChild(a.Label);
        a.Voice=new AudioStreamPlayer3D {UnitSize=2,MaxDistance=10,AttenuationFilterCutoffHz=20500};a.Body.AddChild(a.Voice);
        a.Profile.Position=[spawn.X,spawn.Y,spawn.Z];
    }
    private bool VillagerWalkable(VillagerActor a,Vector2I cell,float height,out Vector3 foot)
    {
        foot=Vector3.Zero;if(Math.Abs(cell.X)>state.ValleyRadius*2 || Math.Abs(cell.Y)>state.ValleyRadius*2)return false;
        var at=new Vector3(cell.X*.5f,height,cell.Y*.5f);
        var excludes=new Godot.Collections.Array<Rid>(agents.Values.Where(v=>v.Body!=null).Select(v=>v.Body!.GetRid()));
        var ray=PhysicsRayQueryParameters3D.Create(at+Vector3.Up*.65f,at-Vector3.Up*.9f,1);ray.Exclude=excludes;
        var hit=GetWorld3D().DirectSpaceState.IntersectRay(ray);if(hit.Count==0)return false;
        foot=hit["position"].AsVector3();if(InWater(foot))return false;
        var shape=new PhysicsShapeQueryParameters3D{Shape=new CapsuleShape3D{Radius=.31f,Height=1.95f},Transform=new Transform3D(Basis.Identity,foot+Vector3.Up*1.02f),CollisionMask=1,Exclude=excludes};
        return GetWorld3D().DirectSpaceState.IntersectShape(shape,1).Count==0;
    }
    private void PlanVillager(VillagerActor a,Vector3 goal)
    {
        if(a.Body==null)return;a.Route.Clear();
        var start=new Vector2I(Mathf.RoundToInt(a.Body.Position.X*2),Mathf.RoundToInt(a.Body.Position.Z*2));var queue=new Queue<Vector2I>();queue.Enqueue(start);
        var parents=new Dictionary<Vector2I,Vector2I>();var positions=new Dictionary<Vector2I,Vector3>{{start,a.Body.Position}};var seen=new HashSet<Vector2I>{start};var best=start;float distance=a.Body.Position.DistanceSquaredTo(goal);
        while(queue.Count>0 && seen.Count<600)
        {
            var cell=queue.Dequeue();foreach(var offset in new[]{Vector2I.Left,Vector2I.Right,Vector2I.Up,Vector2I.Down})
            {var next=cell+offset;if(!seen.Add(next)||!VillagerWalkable(a,next,positions[cell].Y,out var at))continue;parents[next]=cell;positions[next]=at;queue.Enqueue(next);float d=at.DistanceSquaredTo(goal);if(d<distance){distance=d;best=next;}}
            if(distance<.09f)break;
        }
        var route=new Stack<Vector3>();for(var cell=best;cell!=start;cell=parents[cell])route.Push(positions[cell]);foreach(var at in route)a.Route.Enqueue(at);
    }
    private bool HasAgentChair(VillagerActor a)
    {
        if(a.Profile.Id==AgentRegistry.LegacyId && a.Station.DistanceTo(workstationAt)<.1f)return true;
        var at=a.Station+new Basis(Vector3.Up,a.Facing)*new Vector3(0,0,-1);
        return state.Decorations.Any(d=>!d.Carried && d.Kind.EndsWith("_stairs") && new Vector3(d.X,d.Y,d.Z).DistanceTo(at)<.2f)
            ||state.BuildingBlocks.Any(d=>d.Kind.EndsWith("_stairs") && new Vector3(d.X,d.Y,d.Z).DistanceTo(at)<.2f);
    }
    private void UpdateVillager(float dt)
    {
        if(paused||locked){foreach(var a in agents.Values)if(a.Body!=null)a.Voice.StreamPaused=true;return;}
        foreach(var a in agents.Values)UpdateVillager(a,Math.Min(dt,.1f));
    }
    private void UpdateVillager(VillagerActor a,float dt)
    {
        if(a.Body==null)return;a.Voice.StreamPaused=false;a.Time+=dt;a.RouteDelay-=dt;a.NotifyCooldown-=dt;
        bool approach=a.NeedsInput && a.Profile.ApproachForQuestions && walking && !backgroundApp && panel==null && !tvFocused;
        bool atDesk=a.Working && !a.NeedsInput && a.HasScreen;
        if(a.Seated && !atDesk)
        {
            if(VillagerWalkable(a,new Vector2I(Mathf.RoundToInt(a.Approach.X*2),Mathf.RoundToInt(a.Approach.Z*2)),a.Station.Y,out var foot))
            {a.Seated=false;a.Body.Position=foot+Vector3.Up*.02f;foreach(var leg in a.Legs)leg.Rotation=Vector3.Zero;}
        }
        if(!a.Seated)
        {
            if(a.RouteDelay<=0)
            {
                if(approach)
                {
                    var away=a.Body.Position-player.Position;away.Y=0;if(away.Length()<.01f)away=Vector3.Forward;
                    if(away.Length()>2.5f)PlanVillager(a,player.Position+away.Normalized()*2.5f);else a.Route.Clear();
                }
                else if(atDesk)PlanVillager(a,a.Approach);
                else if((a.Idle-=2)<=0 && a.HasScreen){a.Idle=8+(float)random.NextDouble()*6;PlanVillager(a,a.Approach+new Vector3((float)random.NextDouble()*3-1.5f,0,(float)random.NextDouble()*2-1));}
                else a.Route.Clear();
                a.RouteDelay=2+(float)random.NextDouble()*.5f;
            }
            // Never follow into an application or obstruct the player's personal space.
            if(a.NeedsInput && !approach)a.Route.Clear();
            if(approach && a.Body.Position.DistanceTo(player.Position)<2.4f)a.Route.Clear();
            var velocity=a.Body.Velocity;velocity.Y=a.Body.IsOnFloor()?-.1f:velocity.Y-20*dt;velocity.X=velocity.Z=0;
            if(a.Route.TryPeek(out var target)){var dir=target-a.Body.Position;dir.Y=0;if(dir.Length()<.12f)a.Route.Dequeue();else{dir=dir.Normalized();velocity.X=dir.X*1.3f;velocity.Z=dir.Z*1.3f;if(target.Y>a.Body.Position.Y+.15f && a.Body.IsOnFloor())velocity.Y=4; a.Body.Rotation=new Vector3(0,Mathf.Atan2(dir.X,dir.Z),0);}}
            a.Body.Velocity=velocity;a.Body.MoveAndSlide();for(int i=0;i<a.Legs.Count;i++)a.Legs[i].Rotation=new Vector3(new Vector2(velocity.X,velocity.Z).LengthSquared()>.1f?Mathf.Sin(a.Time*5+i*Mathf.Pi)*.45f:0,0,0);
            a.Profile.Position=[a.Body.Position.X,a.Body.Position.Y,a.Body.Position.Z];
            if(atDesk && a.Body.Position.DistanceTo(a.Approach)<.4f && HasAgentChair(a))
            {a.Seated=true;a.Body.Position=a.Seat;a.Body.Rotation=new Vector3(0,a.Facing,0);a.Body.Velocity=Vector3.Zero;a.Route.Clear();foreach(var leg in a.Legs)leg.RotationDegrees=new Vector3(-90,0,0);}
        }
        var toward=player.GlobalPosition-a.Body.GlobalPosition;float yaw=Mathf.Wrap(Mathf.Atan2(toward.X,toward.Z)-a.Body.Rotation.Y,-Mathf.Pi,Mathf.Pi);a.Head.Rotation=new Vector3(0,Mathf.Clamp(yaw,-.8f,.8f),0);
        a.Wave=Math.Max(0,a.Wave-dt);a.Arms.Rotation=new Vector3(a.Wave>0?-.35f:0,0,a.Wave>0?Mathf.Sin(a.Time*8)*.2f:0);
        a.Label.Text=a.Profile.Name+"\n"+(voiceHeld && voiceTarget==a.Profile.Id?"Listening…":a.NeedsInput?"Needs your answer":a.Working?(a.Seated?"Working…":"Heading to computer…"):a.Status);
    }
    private string? TargetVillagerId()=>hovered=="$villager"||hovered=="$workstation"?AgentRegistry.LegacyId:hovered?.StartsWith("$villager:")==true?hovered[10..]:null;
    private bool CanTalkToVillager()=>TargetVillagerId() is {} id && agents.TryGetValue(id,out var a) && a.Body!=null && walking && panel==null && !paused && !locked && player.GlobalPosition.DistanceTo(a.Body.GlobalPosition)<4 && hovered!="$workstation";
    private void VillagerHmm(VillagerActor a)
    {
        if(a.Body==null||paused||locked)return;a.Voice.Stream=GD.Load<AudioStream>($"res://Assets/Vanilla/villager_idle{random.Next(1,4)}.ogg");a.Voice.VolumeDb=Mathf.LinearToDb(Math.Max(.0001f,state.Settings.EffectsVolume*.5f));a.Voice.Play();
    }
    private bool HandleVillagerInput(InputEvent e)
    {
        if(e is not InputEventKey key||key.Keycode!=Key.V||key.Echo)return false;
        if(key.Pressed && walking && !PointerInteractionOpen && !paused && !locked && agents.Count>0)
        {
            voiceTarget=CanTalkToVillager()?TargetVillagerId():null;voiceRecorder=voiceTarget??agents.Keys.First();voiceHeld=true;voicePending=true;
            bridge.Send(new{command="villager",agentId=voiceRecorder,action="mic-start"});Toast("Hold V: say a villager's name, then your request. Release to review.");GetViewport().SetInputAsHandled();return true;
        }
        if(!key.Pressed && voiceHeld){voiceHeld=false;bridge.Send(new{command="villager",agentId=voiceRecorder,action="mic-stop"});GetViewport().SetInputAsHandled();return true;}return false;
    }
    private void CancelVillagerSpeech(){if(!voiceHeld && !voicePending)return;voiceHeld=false;voicePending=false;bridge.Send(new{command="villager",agentId=voiceRecorder,action="mic-cancel"});voiceRecorder=voiceTarget=null;}
    private string? pendingVillagerRenameId,pendingVillagerRename;
    private void ShowVillagerName(string id)
    {
        if(!agents.TryGetValue(id,out var actor))return;
        var box=OpenPanel("Name villager");
        var entry=new LineEdit{Text=actor.Profile.Name,MaxLength=32,CustomMinimumSize=new Vector2(480,44)};box.AddChild(entry);
        var message=new Label{Text="Use a unique name you can say when holding V."};box.AddChild(message);
        void Rename()
        {
            string name=entry.Text.Trim();
            if(!AgentRegistry.CanName(state.Agents,id,name)){message.Text="Choose a unique name of 2–32 letters or numbers.";return;}
            if(!bridge.Connected){message.Text="The workstation is disconnected. Try again when connected.";return;}
            pendingVillagerRenameId=id;pendingVillagerRename=name;bridge.Send(new{command="villager",agentId=id,action="rename",name});
            message.Text="Name sent. Close this panel to return to the cave.";
        }
        entry.TextSubmitted+=_=>Rename();
        var row=new HBoxContainer();box.AddChild(row);row.AddChild(Button("Save name",Rename));row.AddChild(Button("Cancel",ClosePanelAndResume));
        entry.GrabFocus();entry.SelectAll();
    }
    private void OpenVillagerMonitor()
    {
        CancelVillagerSpeech();ClosePanel();
        if(TargetVillagerId() is {} target)
        {
            activeScreen=screenSurfaces.FirstOrDefault(s=>s.Role=="desktop" && s.AgentId==target);
            if(activeScreen==null){Toast("Place this villager's computer block to open its workstation.");return;}
        }
        if(activeScreen?.Role!="desktop")activeScreen=screenSurfaces.Where(s=>s.Role=="desktop").OrderBy(s=>s.Node.GlobalPosition.DistanceSquaredTo(camera.GlobalPosition)).FirstOrDefault();
        if(activeScreen==null){Toast("Place this villager's computer block to open its workstation.");return;}
        if(camera.GlobalPosition.DistanceTo(activeScreen.Node.GlobalPosition)>6){Toast("Move closer to the villager's computer screen.");return;}FocusTelevision();
    }
    private string MessageAgent(JsonElement p)=>p.TryGetProperty("agentId",out var id)?id.GetString()??AgentRegistry.LegacyId:AgentRegistry.LegacyId;
    private void ReceiveVillagerReady(JsonElement p)
    {
        if(!agents.TryGetValue(MessageAgent(p),out var a))return;
        if(!a.Connected){a.Frames?.Dispose();a.Frames=new Cave.Transport.TvFrameBuffer(p.GetProperty("channel").GetString()!);a.Connected=true;}
    }
    private void ReceiveVillagerProfile(JsonElement p)
    {
        if(!agents.TryGetValue(MessageAgent(p),out var a))return;
        string name=p.GetProperty("name").GetString()??a.Profile.Name;
        if(pendingVillagerRenameId==a.Profile.Id && pendingVillagerRename==name){pendingVillagerRenameId=pendingVillagerRename=null;ClosePanelAndResume();Toast("Villager renamed to "+name);}
        bool approach=p.GetProperty("approach").GetBoolean();
        if(name==a.Profile.Name && approach==a.Profile.ApproachForQuestions)return;
        a.Profile.Name=name;a.Profile.ApproachForQuestions=approach;Changed();
    }
    private void ReceiveVillager(JsonElement p)
    {
        if(!agents.TryGetValue(MessageAgent(p),out var a))return;
        bool wasWorking=a.Working,wasNeeding=a.NeedsInput;a.Working=p.GetProperty("working").GetBoolean();string text=p.GetProperty("text").GetString()??"Ready";
        a.NeedsInput=text.Contains("Needs your input",StringComparison.OrdinalIgnoreCase)||text.Contains("Needs attention",StringComparison.OrdinalIgnoreCase);
        a.Status=text.Length>65?text[..65]+"…":text;
        if((wasWorking && !a.Working)||(!wasNeeding && a.NeedsInput)){a.Wave=4;if(a.NotifyCooldown<=0){VillagerHmm(a);a.NotifyCooldown=10;}a.RouteDelay=0;}
        // Background progress never opens a panel or steals focus.
    }
    private void ShowVoiceError(JsonElement p)
    {
        if(!voicePending || MessageAgent(p)!=voiceRecorder)return;
        voiceHeld=false;voicePending=false;
        var box=OpenPanel("Voice input");box.AddChild(new Label{Text=p.GetProperty("text").GetString()??"No speech captured.",AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new Vector2(600,0)});
        box.AddChild(Button("Type request instead",()=>{voicePending=true;ReviewVillagerVoice("");}));
        box.AddChild(Button("Windows sound settings",()=>{ClosePanel();OpenDesktopSystem("sound");}));
        box.AddChild(Button("Microphone permissions",()=>{ClosePanel();OpenDesktopSystem("microphone");}));
        box.AddChild(Button("Close and try again",ClosePanelAndResume));
    }
    private void ReviewVillagerVoice(string text)
    {
        if(!voicePending)return;voicePending=false;
        var addressed=AgentRegistry.Address(state.Agents,text,voiceTarget);voiceTarget=null;
        var box=OpenPanel("Voice request",addressed.Id==null?"Choose who you meant, then review your request.":"Review the villager and request before sending.");
        var recipients=new OptionButton();foreach(var profile in state.Agents)recipients.AddItem(profile.Name);box.AddChild(recipients);
        recipients.Select(addressed.Id==null?-1:state.Agents.FindIndex(a=>a.Id==addressed.Id));
        var entry=new TextEdit{Text=addressed.Prompt,CustomMinimumSize=new Vector2(640,160),WrapMode=TextEdit.LineWrappingMode.Boundary};box.AddChild(entry);
        box.AddChild(Button("Send task",()=>{if(recipients.Selected<0){Toast("Choose a villager first.");return;}var prompt=entry.Text.Trim();if(prompt.Length==0)return;var id=state.Agents[recipients.Selected].Id;bridge.Send(new{command="villager",agentId=id,action="send",text=prompt});ClosePanelAndResume();if(agents.TryGetValue(id,out var a))VillagerHmm(a);}));
        box.AddChild(Button("Cancel",ClosePanelAndResume));
    }
    private void UpdateWorkstation(float dt)
    {
        if(paused||locked)return;
        if((villagerSyncDelay-=dt)<=0){villagerSyncDelay=5;if(agents.Values.Any(a=>a.Body==null && a.HasScreen))SyncVillagers();}
        foreach(var a in agents.Values)
        {
            if(!a.Connected && (a.Retry-=dt)<=0){a.Retry=5;if(bridge.Connected)bridge.Send(new{command="villager",agentId=a.Profile.Id,name=a.Profile.Name,action="connect"});}
            if(a.Frames==null)continue;
            try{var bytes=a.Frames.Read();if(bytes==null)continue;using var image=new Image();if(image.LoadJpgFromBuffer(bytes)!=Error.Ok)continue;if(a.Texture==null)a.Texture=ImageTexture.CreateFromImage(image);else a.Texture.Update(image);a.Material.AlbedoTexture=a.Texture;a.Material.AlbedoColor=Colors.White;RefreshScreenLabels();}
            catch{a.Frames?.Dispose();a.Frames=null;a.Connected=false;}
        }
    }
}
