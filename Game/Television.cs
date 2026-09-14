using Godot;
using System.Text.Json;
namespace CozyCave;
public partial class Main
{
    private Cave.Transport.TvFrameBuffer? tvFrames;
    private string tvChannel="",tvStatus="TV off — choose a YouTube video to begin.";
    private ImageTexture? tvTexture;
    private StandardMaterial3D? tvMaterial;
    private Label3D? tvOffLabel;
    private Label? tvStatusLabel;
    private TextureRect? tvPreview;
    private Button? tvPowerButton,tvPauseButton,tvMuteButton;
    private long tvRequest;
    private bool tvOn,tvPaused,tvVolumeDirty;
    private float tvVolumeTime,tvAimTime;
    private bool tvAiming;
    private bool tvFocused;
    private float tvPreviousFov;
    private Vector3 tvPreviousRotation,tvPreviousPosition;
    private void LeaveTelevision()
    {
        if(!tvFocused)return;
        CancelScreenPointer();
        if(activeScreen?.Role=="desktop")bridge.Send(new{command="villager",agentId=activeScreen.AgentId,action="leave"});
        tvFocused=false;camera.Fov=tvPreviousFov;camera.Rotation=tvPreviousRotation;camera.Position=tvPreviousPosition;
    }
    private void FocusTelevision()
    {
        if(tvFocused)return;
        tvPreviousFov=camera.Fov;tvPreviousRotation=camera.Rotation;tvPreviousPosition=camera.Position;
        activeScreen ??= SurfaceFor(hovered) ?? screenSurfaces.FirstOrDefault(s=>s.Role=="tv");
        if(activeScreen==null)return;
        camera.GlobalPosition=activeScreen.Node.ToGlobal(new Vector3(0,0,-Mathf.Clamp(activeScreen.Size.Y*1.2f,1.2f,4)));
        camera.LookAt(activeScreen.Node.GlobalPosition);
        float distance=camera.GlobalPosition.DistanceTo(activeScreen.Node.GlobalPosition);
        camera.Fov=Mathf.Clamp(Mathf.RadToDeg(2*Mathf.Atan(activeScreen.Size.Y*.7f/Math.Max(.1f,distance))),10,80);
        tvFocused=true;walking=false;Input.MouseMode=Input.MouseModeEnum.Visible;
        Input.WarpMouse(GetViewport().GetVisibleRect().Size/2);
        if(activeScreen.Role=="desktop")bridge.Send(new{command="villager",agentId=activeScreen.AgentId,action="open"});
        else if(!tvOn){tvOn=true;TvCommand("power");}
        Toast(activeScreen.Role=="desktop"?"Computer: click, type, select and scroll. Esc: walk.":"TV: click, type, scroll or drag. Esc: walk. Middle-click: power. Shift + right-click: settings.");
    }
    private bool HandleTvInput(InputEvent e)
    {
        if(!tvFocused)return false;
        if(e is InputEventKey key)
        {
            if(key.Keycode==Key.Escape)
            {
                if(key.Pressed){LeaveTelevision();StartWalking();}
            }
            else
            {
                int code=key.Keycode switch {
                    Key.Backspace=>8,Key.Tab=>9,Key.Enter or Key.KpEnter=>13,Key.Delete=>46,
                    Key.Left=>37,Key.Up=>38,Key.Right=>39,Key.Down=>40,Key.Home=>36,Key.End=>35,
                    Key.Pageup=>33,Key.Pagedown=>34,Key.Space=>32,
                    _ => (long)key.Keycode>=32 && (long)key.Keycode<=126?(int)key.Keycode:0
                };
                int modifiers=(key.AltPressed?1:0)|(key.CtrlPressed?2:0)|(key.MetaPressed?4:0)|(key.ShiftPressed?8:0);
                string text=key.Pressed && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed && key.Unicode>=32?char.ConvertFromUtf32((int)key.Unicode):"";
                if(key.Pressed && key.CtrlPressed && key.Keycode==Key.V)
                    bridge.Send(new{command=ScreenInputCommand,agentId=activeScreen?.AgentId,kind="text",text=DisplayServer.ClipboardGet()});
                else bridge.Send(new{command=ScreenInputCommand,agentId=activeScreen?.AgentId,kind="key",code,modifiers,text,pressed=key.Pressed});
            }
        }
        else if(e is InputEventMouseButton mouse)
        {
            if(activeScreen?.Role=="tv" && mouse.Pressed && mouse.ButtonIndex==MouseButton.Right && mouse.ShiftPressed){LeaveTelevision();ShowTelevision();}
            else if(!mouse.Pressed && mouse.ButtonIndex==MouseButton.Left)
                bridge.Send(new{command=ScreenPointerCommand,agentId=activeScreen?.AgentId,u=tvLastAim.X,v=tvLastAim.Y,click=false,phase="up"});
            else if(mouse.Pressed && TryTvAim(out var uv))
            {
                tvLastAim=uv;
                if(activeScreen?.Role=="tv" && mouse.ButtonIndex==MouseButton.Middle){tvOn=!tvOn;TvCommand("power");}
                if(mouse.ButtonIndex==MouseButton.Left)bridge.Send(new{command=ScreenPointerCommand,agentId=activeScreen?.AgentId,u=uv.X,v=uv.Y,click=false,phase="down"});
                if(mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
                    bridge.Send(new{command=ScreenInputCommand,agentId=activeScreen?.AgentId,kind="wheel",u=uv.X,v=uv.Y,delta=mouse.ButtonIndex==MouseButton.WheelUp?-160:160});
            }
        }
        GetViewport().SetInputAsHandled();return true;
    }
    private void CancelScreenPointer()=>bridge.Send(new{command=ScreenPointerCommand,agentId=activeScreen?.AgentId,u=-.05f,v=-.05f,click=false,phase=activeScreen?.Role=="desktop"?"cancel":"up"});
    private string ScreenPointerCommand=>activeScreen?.Role=="desktop" && tvFocused?"workstation-pointer":"tv-pointer";
    private string ScreenInputCommand=>activeScreen?.Role=="desktop" && tvFocused?"workstation-input":"tv-input";
    private Vector2 tvLastAim=new(-1,-1);
    private int tvPresentedFrames;
    private void BuildTelevision()
    {
        CreateInitialTvBlocks();
    }

    private void TvCommand(string action)
    {
        if(!bridge.Connected && !selfTesting){tvStatus="TV needs the Minecraft Desktop desktop launcher. Restart using Minecraft Desktop in Start.";RefreshTvControls();return;}
        bridge.Send(new{command="tv",action,requestId=++tvRequest,power=tvOn,muted=state.Settings.TvMuted,volume=state.Settings.TvVolume,url=state.Settings.TvUrl});
        RefreshTvControls();
    }
    private void LoadTvVideo(string value)
    {
        string? url=Cave.Core.YouTubeSource.Normalize(value);
        if(url==null){tvStatus="Paste a YouTube watch, Shorts, or youtu.be video link.";RefreshTvControls();return;}
        state.Settings.TvUrl=url;tvOn=true;tvPaused=false;tvStatus="Loading YouTube…";Changed();TvCommand("load");
    }
    private void ShowTelevision()
    {
        var box=OpenPanel("Cave TV","Your own YouTube player • audio controls affect only this TV");
        tvPreview=new TextureRect {Texture=tvOn?tvTexture:null,CustomMinimumSize=new Vector2(640,300),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=Control.MouseFilterEnum.Ignore};box.AddChild(tvPreview);
        tvStatusLabel=new Label {Text=tvStatus,AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new Vector2(900,42)};box.AddChild(tvStatusLabel);
        var row=new HBoxContainer();row.AddThemeConstantOverride("separation",12);box.AddChild(row);
        tvPowerButton=Button("",()=>{tvOn=!tvOn;tvStatus=tvOn?"Connecting to TV…":"TV off";TvCommand("power");});tvPowerButton.Name="TvPower";row.AddChild(tvPowerButton);
        tvPauseButton=Button("",()=>{if(tvOn){tvPaused=!tvPaused;TvCommand("pause");}});tvPauseButton.Name="TvPause";row.AddChild(tvPauseButton);
        tvMuteButton=Button("",()=>{state.Settings.TvMuted=!state.Settings.TvMuted;Changed();TvCommand("volume");});tvMuteButton.Name="TvMute";row.AddChild(tvMuteButton);
        row.AddChild(Button("-10 sec",()=>TvCommand("back")));row.AddChild(Button("+10 sec",()=>TvCommand("forward")));row.AddChild(Button("Next video",()=>TvCommand("next")));
        var volumeRow=new HBoxContainer();box.AddChild(volumeRow);
        var volumeLabel=new Label{Text=$"TV volume  {state.Settings.TvVolume}%",CustomMinimumSize=new Vector2(220,0)};volumeRow.AddChild(volumeLabel);
        var volume=new HSlider {Name="TvVolume",MinValue=0,MaxValue=100,Step=1,Value=Math.Clamp(state.Settings.TvVolume,0,100),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(0,32)};volumeRow.AddChild(volume);
        volume.ValueChanged+=value=>{state.Settings.TvVolume=(int)value;volumeLabel.Text=$"TV volume  {(int)value}%";tvVolumeDirty=true;tvVolumeTime=0;dirty=true;};
        box.AddChild(new Label{Text="YouTube video link"});
        var sourceRow=new HBoxContainer();box.AddChild(sourceRow);
        var address=new LineEdit {Name="TvAddress",Text=state.Settings.TvUrl,PlaceholderText="https://www.youtube.com/watch?v=…",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};sourceRow.AddChild(address);
        sourceRow.AddChild(Button("Play video",()=>LoadTvVideo(address.Text)));address.TextSubmitted+=LoadTvVideo;
        box.AddChild(Button("Open player window to browse YouTube",()=>{tvOn=true;TvCommand("browse");}));
        box.AddChild(new Label{Text="Close the player window to return here. TV starts off when Minecraft Desktop launches.",AutowrapMode=TextServer.AutowrapMode.WordSmart});
        RefreshTvControls();
    }
    private void RefreshTvControls()
    {
        RefreshScreenLabels();
        if(GodotObject.IsInstanceValid(tvStatusLabel))tvStatusLabel!.Text=tvStatus;
        if(GodotObject.IsInstanceValid(tvPowerButton))tvPowerButton!.Text=tvOn?"Turn off":"Turn on";
        if(GodotObject.IsInstanceValid(tvPauseButton)){tvPauseButton!.Text=tvPaused?"Play":"Pause";tvPauseButton.Disabled=!tvOn;}
        if(GodotObject.IsInstanceValid(tvMuteButton))tvMuteButton!.Text=state.Settings.TvMuted?"Unmute":"Mute";
        if(GodotObject.IsInstanceValid(tvPreview))tvPreview!.Texture=tvOn?tvTexture:null;
        if(GodotObject.IsInstanceValid(tvMaterial)){tvMaterial!.AlbedoTexture=tvOn?tvTexture:null;tvMaterial.AlbedoColor=tvOn?Colors.White:new Color("0c0e11");}
        if(GodotObject.IsInstanceValid(tvOffLabel))tvOffLabel!.Visible=!tvOn || tvTexture==null;
    }
    private void ReceiveTv(JsonElement p)
    {
        if(p.TryGetProperty("requestId",out var request) && request.GetInt64()<tvRequest)return;
        if(p.TryGetProperty("muted",out var silent) && state.Settings.TvMuted!=silent.GetBoolean()){state.Settings.TvMuted=silent.GetBoolean();dirty=true;}
        if(!tvVolumeDirty && p.TryGetProperty("volume",out var level) && state.Settings.TvVolume!=level.GetInt32()){state.Settings.TvVolume=Math.Clamp(level.GetInt32(),0,100);dirty=true;}
        if(p.TryGetProperty("url",out var url) && Cave.Core.YouTubeSource.Normalize(url.GetString()??"") is {} current && current!=state.Settings.TvUrl){state.Settings.TvUrl=current;dirty=true;}
        tvStatus=p.GetProperty("text").GetString()??"";tvOn=p.GetProperty("power").GetBoolean();tvPaused=p.GetProperty("paused").GetBoolean();RefreshTvControls();
    }
    private void ConnectTv(string channel)
    {
        if(channel==tvChannel)return;
        try {tvFrames?.Dispose();tvFrames=new Cave.Transport.TvFrameBuffer(channel);tvChannel=channel;}
        catch(Exception e){tvStatus="TV picture unavailable: "+e.Message;RefreshTvControls();}
    }
    private bool TryTvAim(out Vector2 uv)
    {
        uv=default;if(panel!=null || (!walking && !tvFocused))return false;
        var surface=tvFocused?activeScreen:SurfaceFor(hovered);
        return surface!=null && (surface.Role=="tv" || tvFocused) && TryScreenAim(surface,out uv);
    }

    private bool ClickTelevision()
    {
        if(!TryTvAim(out var uv))return false;
        bool wasOn=tvOn;FocusTelevision();
        if(wasOn)bridge.Send(new{command=ScreenPointerCommand,agentId=activeScreen?.AgentId,u=uv.X,v=uv.Y,click=true});return true;
    }
    private void UpdateTvAim(float delta)
    {
        tvAimTime+=delta;
        Vector2 uv=default;bool aiming=(tvOn || (tvFocused && activeScreen?.Role=="desktop")) && TryTvAim(out uv);
        if(aiming)
        {
            if(tvAimTime>=1f/30 && (!tvAiming || uv.DistanceSquaredTo(tvLastAim)>.000001f || tvAimTime>.5f))
            {bridge.Send(new{command=ScreenPointerCommand,agentId=activeScreen?.AgentId,u=uv.X,v=uv.Y,click=false});tvLastAim=uv;tvAimTime=0;}
        }
        else if(tvAiming)bridge.Send(new{command=ScreenPointerCommand,agentId=activeScreen?.AgentId,u=-.05f,v=-.05f,click=false});
        tvAiming=aiming;
    }
    private void UpdateTelevision(float delta)
    {
        if(tvVolumeDirty && (tvVolumeTime+=delta)>.15f){tvVolumeDirty=false;Changed();TvCommand("volume");}
        if(!tvOn || paused || locked || tvFrames==null)return;
        try
        {
            var bytes=tvFrames.Read();if(bytes==null)return;
            using var image=new Image();if(image.LoadJpgFromBuffer(bytes)!=Error.Ok)return;
            bool bind=tvTexture==null;
            if(tvTexture==null)tvTexture=ImageTexture.CreateFromImage(image);
            else if(tvTexture.GetWidth()!=image.GetWidth() || tvTexture.GetHeight()!=image.GetHeight())tvTexture.SetImage(image);
            else tvTexture.Update(image);
            tvPresentedFrames++;if(bind)RefreshTvControls();
        }
        catch(Exception e){tvStatus="TV picture interrupted: "+e.Message;tvFrames?.Dispose();tvFrames=null;tvChannel="";RefreshTvControls();}
    }
}
