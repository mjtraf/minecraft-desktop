using Godot;
namespace CozyCave;
public partial class Main
{
    private async Task RunTvSmoke(string output)
    {
        var results=new List<string>();
        void Check(bool ok,string text)=>results.Add((ok?"PASS ":"FAIL ")+text);
        try
        {
            System.IO.Directory.CreateDirectory(output);
            for(int i=0;i<300 && !bridge.Connected;i++)await Frames(1);
            Check(bridge.Connected,"TV connects through the desktop helper");
            state.Settings.TvMuted=true;
            LoadTvVideo("https://youtu.be/aqz-KE-bpKQ");
            for(int i=0;i<1200 && tvTexture==null;i++)await Frames(1);
            await Frames(360);
            Check(tvTexture!=null && tvTexture.GetWidth()>500,"YouTube frames reach the Godot screen texture");
            player.Position=new Vector3(2,.05f,4);
            void AimTv(){player.Rotation=new Vector3(0,Mathf.Pi,0);camera.LookAt(new Vector3(2,3,7.4f));pitch=camera.Rotation.X;}
            AimTv();
            await Frames(6);StartWalking();await Frames(4);
            Check(hovered=="$tv","Desk TV can be targeted from the clear aisle");
            int frameStart=tvPresentedFrames;var clock=System.Diagnostics.Stopwatch.StartNew();await Frames(300);
            results.Add($"Displayed TV FPS: {(tvPresentedFrames-frameStart)/clock.Elapsed.TotalSeconds:F1}; cave FPS: {Engine.GetFramesPerSecond()}");
            AimTv();
            _UnhandledInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true});await Frames(100);
            Check(tvFocused && !walking && panel==null,"Clicking TV enters direct screen browsing without another panel");
            Check(camera.Fov<tvPreviousFov && TryTvAim(out _),"TV focus zooms the physical screen and keeps cursor targeting valid");
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"tv-focused.png"));
            HandleTvInput(new InputEventKey{Keycode=Key.E,Unicode='e',Pressed=true});await Frames(3);
            Check(panel==null && !buildingInventoryOpen,"Typing E into TV cannot open cave inventory");
            HandleTvInput(new InputEventKey{Keycode=Key.Escape,Pressed=true});await Frames(3);
            Check(!tvFocused && walking,"Escape releases TV input back to walking");
            AimTv();await Frames(2);
            layer.Visible=false;await Frames(3);GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"tv-in-cave.png"));layer.Visible=true;
            AimTv();await Frames(2);
            _UnhandledInput(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=true,ShiftPressed=true});await Frames(6);
            Check(panel!=null && Descendants(panel).OfType<Button>().Any(b=>b.Name=="TvPower"),"Shift-right-click opens optional TV settings");
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"tv-remote.png"));
            var mute=Descendants(panel!).OfType<Button>().Single(b=>b.Name=="TvMute");bool beforeMute=state.Settings.TvMuted;mute.EmitSignal(Godot.Button.SignalName.Pressed);await Frames(4);
            Check(state.Settings.TvMuted!=beforeMute,"Remote mute button toggles current TV mute");mute.EmitSignal(Godot.Button.SignalName.Pressed);
            var volume=Descendants(panel!).OfType<HSlider>().Single(s=>s.Name=="TvVolume");volume.Value=24;await Frames(20);
            Check(store.Load().Settings.TvVolume==24,"Remote volume persists after debounce");
            var power=Descendants(panel!).OfType<Button>().Single(b=>b.Name=="TvPower");power.EmitSignal(Godot.Button.SignalName.Pressed);await Frames(12);
            Check(!tvOn && tvMaterial!.AlbedoTexture==null,"Power off blacks out the wall screen");
            power.EmitSignal(Godot.Button.SignalName.Pressed);await Frames(30);Check(tvOn && tvMaterial!.AlbedoTexture!=null,"Power on restores the existing TV session");
            _Input(new InputEventKey{Keycode=Key.Escape,Pressed=true});await Frames(3);Check(panel==null && walking,"Escape closes the remote and returns to the cave");
            tvOn=false;TvCommand("power");await Frames(4);
        }
        catch(Exception e){results.Add("FAIL "+e);}
        System.IO.File.WriteAllLines(System.IO.Path.Combine(output,"results.txt"),results);Quit();
    }
}
