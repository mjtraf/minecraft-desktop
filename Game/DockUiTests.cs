using Godot;
namespace CozyCave;
public partial class Main
{
    private async Task RunDockUiTest(string output)
    {
        var results=new List<string>();void Check(bool ok,string text)=>results.Add((ok?"PASS ":"FAIL ")+text);
        try
        {
            System.IO.Directory.CreateDirectory(output);await Frames(15);
            Check(!ambience.Playing,"Synthetic hiss ambience is not playing");
            Check(rainClips.Length==8 && rainClips.All(c=>!c.Loop),"Eight original Minecraft rain clips play without looping");
            player.Position=new Vector3(0,1,5);Check(RainSheltered(),"Cave ceiling selects sheltered rain");
            player.Position=new Vector3(12,1,-5);EnsureValley(player.Position,true);await Frames(3);
            Check(!RainSheltered(),"Open valley selects normal rain");
            bool valid=true;for(int i=0;i<40;i++){int previous=lastRainClip;int next=PickRainClip(true);valid&=next<8 && next!=previous;}
            Check(valid,"Sheltered rain uses the normal Minecraft clips without immediate repeats");
            Check(rainVoices.All(v=>v.AttenuationFilterCutoffHz==20500 && v.AttenuationFilterDb==0),"Rain distance muffling is disabled on every voice");
            foreach(var voice in rainVoices)voice.Stop();
            var impact=new Vector3(0,10.5f,5);PlayRainAt(impact,true);var shelteredVoice=rainVoices.First(v=>v.Playing);
            Check(shelteredVoice.PitchScale==1,"Sheltered rain preserves normal pitch");
            player.Position=new Vector3(2,1,5);UpdateRain(2);Check(shelteredVoice.Position==impact,"Playing rain remains at its impact point when player moves");
            locked=true;UpdateAudio();await Frames(3);Check(rainVoices.All(v=>!v.Playing || v.StreamPaused),"Session lock pauses every playing rain voice");locked=false;UpdateAudio();
            foreach(var voice in rainVoices)voice.Stop();
            player.Position=new Vector3(0,1,5);rainShelterDelay=0;rainGain=.8f;rainSoundDelay=10;UpdateRainAudio(.1f);
            Check(rainGain>.2f && rainGain<.8f,"Entering shelter fades rain instead of abruptly changing it");
            for(int i=0;i<15;i++)UpdateRainAudio(.1f);UpdateAudio();float indoorGain=rainGain;
            Check(Mathf.IsEqualApprox(indoorGain,.2f),"Indoor rain remains audible at one quarter outdoor gain");
            player.Position=new Vector3(12,1,-5);rainShelterDelay=0;for(int i=0;i<15;i++)UpdateRainAudio(.1f);UpdateAudio();
            Check(Mathf.IsEqualApprox(rainGain,.8f) && rainVoices.All(v=>v.PitchScale==1),"Returning outdoors restores clear normal-pitch rain on all voices");
            player.Position=new Vector3(2,0,4);walking=true;
            var beforeSeat=player.Position;string seatId="$terrain:"+terrain.First(t=>t.Value.Kind=="dark_oak_stairs").Key;
            Check(InteractBlock(seatId) && sittingOn==seatId && camera.Position.Y<1.62f,"Right-click stair furniture sits at a lower eye height");
            var seatedPosition=player.Position;var bodyYaw=player.Rotation;var beforeLook=camera.Rotation;
            _UnhandledInput(new InputEventMouseMotion {Relative=new Vector2(35,-12)});
            Check(camera.Rotation!=beforeLook && player.Rotation==bodyYaw && player.Position==seatedPosition,"Seated head turns independently while body stays aligned to chair");
            _UnhandledInput(new InputEventMouseMotion {Relative=new Vector2(10000,0)});
            Check(Mathf.Abs(seatedLookYaw)<=Mathf.DegToRad(80)+.001f,"Seated head cannot turn through 360 degrees");
            Check(seatedBody!=null && Descendants(seatedBody).OfType<MeshInstance3D>().Count()==5 && seatedShapes.Count==2,"Seated torso arms and legs have visible meshes and collision shapes");
            seatedLookYaw=0;pitch=-1.1f;camera.Rotation=new Vector3(pitch,0,0);await Frames(4);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"seated-body.png"));
            _PhysicsProcess(.016);Check(player.Position==seatedPosition && player.Velocity==Vector3.Zero,"Sitting prevents gravity and sliding through furniture");
            Save();Check(store.Load().Player.SequenceEqual(new[]{beforeSeat.X,beforeSeat.Y,beforeSeat.Z}),"Save uses safe standing position rather than inside chair");
            CaveFocusLost();ResumeCaveFocus();Check(sittingOn==seatId,"Sitting survives switching to Windows apps and back");
            StandUp();Check(player.Position==beforeSeat && camera.Position.Y==1.62f,"Standing restores normal height and approach position");
            // Block a seat's leg room and verify seating refuses to clip into it.
            var shape=TargetShape(seatId)!.Value;float facing=Mathf.DegToRad(shape.Rotation+90);var forward=-new Basis(Vector3.Up,facing).Z;
            var obstruction=new StaticBody3D {Position=shape.At+forward*.8f};world.AddChild(obstruction);obstruction.AddChild(new CollisionShape3D {Shape=new BoxShape3D {Size=Vector3.One}});await Frames(3);
            SitOn(seatId);Check(sittingOn==null,"Seat refuses entry when another block occupies leg room");
            obstruction.QueueFree();await Frames(3);
            SitOn(seatId);
            var blockedExit=new StaticBody3D {Position=standingPosition+Vector3.Up*.9f};world.AddChild(blockedExit);blockedExit.AddChild(new CollisionShape3D {Shape=new BoxShape3D {Size=new Vector3(.8f,1.8f,.8f)}});await Frames(3);
            var oldExit=standingPosition;StandUp();Check(sittingOn!=null || (player.Position!=oldExit && StandingClear(player.Position)),"Standing avoids an exit blocked while seated");blockedExit.QueueFree();await Frames(3);StandUp();
            SitOn(seatId);ToggleFlight();Check(sittingOn==null,"Flight exits sitting cleanly");
            desktopApps=Enumerable.Range(0,12).Select(i=>new DesktopApp("test"+i,"App "+(i+1),"",false,1,["test"+i])).ToList();
            RefreshDesktopHotbar();desktopHotbarActive=true;StartWalking();SelectDesktopSlot(0);
            var seen=new HashSet<string>();for(int i=0;i<16;i++){MoveDesktopSelection(1);seen.Add(desktopSystemSelection??"app"+desktopSelection);}
            Check(seen.Count==16 && seen.Contains("search") && seen.Contains("system") && seen.Contains("notifications"),"Wheel visits every app, Search, Desktop, System and clock exactly once");
            Check(desktopSelection==0 && desktopSystemSelection==null,"Selection wraps back to original app");
            MoveDesktopSelection(-1);MoveDesktopSelection(-1);
            Check(desktopSystemSelection=="search","Reverse wheel reaches Search before first app");
            await Frames(3);GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"dock-search.png"));
            SelectDesktopSlot(11);MoveDesktopSelection(1);Check(desktopSystemSelection=="system","Wheel after last app selects System");MoveDesktopSelection(1);Check(desktopSystemSelection=="notifications","Next wheel selects time and notifications");
            await Frames(3);GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"dock-clock.png"));
            int supply=state.SelectedSlot;MoveDesktopSelection(1);Check(state.SelectedSlot==supply,"App/system navigation does not change building inventory");
            SelectDesktopSlot(9);Check(desktopSelection==9 && desktopSystemSelection==null,"App page selection exits system selection");
            Check(Mathf.Abs(hotbar!.Size.X*hotbar.Scale.X/DockScale-546)<.1f,"In-game dock uses same 546 physical pixel hotbar as native dock");
            // Exercise GUI routing: Space activates a focused Button before _UnhandledInput.
            int searchActivations=0;var searchButton=dockSystemButtons["search"];
            searchButton.Pressed+=()=>searchActivations++;
            walking=false;Input.MouseMode=Input.MouseModeEnum.Visible;await Frames(2);
            var searchPoint=searchButton.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseButton {Position=searchPoint,GlobalPosition=searchPoint,ButtonIndex=MouseButton.Left,Pressed=true},true);
            GetViewport().PushInput(new InputEventMouseButton {Position=searchPoint,GlobalPosition=searchPoint,ButtonIndex=MouseButton.Left,Pressed=false},true);await Frames(2);
            Check(searchActivations==1,"Search still opens from a dock button click");
            walking=false;ResumeCaveFocus();desktopSystemSelection="search";MoveDesktopSelection(2);
            int opened=searchActivations;
            for(int press=0;press<3;press++)
            {
                GetViewport().PushInput(new InputEventKey {Keycode=Key.Space,PhysicalKeycode=Key.Space,Pressed=true});
                GetViewport().PushInput(new InputEventKey {Keycode=Key.Space,PhysicalKeycode=Key.Space,Pressed=false});await Frames(1);
            }
            Check(searchActivations==opened,"Jump after Search dismissal and wheel selection never reopens Search");
            Check(GetViewport().GuiGetFocusOwner()==null,"Returning to walking releases GUI keyboard focus");
            desktopHotbarActive=true;desktopSystemSelection="search";walking=false;ResumeCaveFocus();status="Search dismissed";
            _UnhandledInput(new InputEventMouseButton {Pressed=true,ButtonIndex=MouseButton.Left});
            Check(status=="Search dismissed" && !mouseActionsReady,"Focus-return click is consumed without reopening selected Search");
            PollMouseActions(true,false);Check(!mouseActionsReady,"Held return click cannot rearm actions");
            PollMouseActions(false,false);Check(mouseActionsReady,"Releasing mouse rearms a deliberate new click");
            desktopHotbarActive=false;walking=true;miningHeld=true;ToggleDesktopHotbar();ToggleDesktopHotbar();
            Check(!miningHeld && !mouseActionsReady && swingTime==0,"Switching apps and blocks cancels swing and held mining");
            PollMouseActions(true,false);Check(!miningHeld,"Stale pressed state cannot start mining without a new press");
            PollMouseActions(false,false);camera.Rotation=Vector3.Zero;
            _UnhandledInput(new InputEventMouseButton {Pressed=true,ButtonIndex=MouseButton.Left});
            Check(miningHeld,"Fresh block-mode mouse press starts mining");
            _Input(new InputEventMouseButton {Pressed=false,ButtonIndex=MouseButton.Left});Check(!miningHeld,"Mouse release stops mining even before GUI routing");
            miningHeld=true;CaveFocusLost();ResumeCaveFocus();Check(!miningHeld && !mouseActionsReady,"App focus roundtrip clears held mining");

        }
        catch(Exception e){results.Add("FAIL "+e);}
        System.IO.File.WriteAllLines(System.IO.Path.Combine(output,"results.txt"),results);GetTree().Quit();
    }
}
