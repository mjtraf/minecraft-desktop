using Godot;
using Cave.Core;
namespace CozyCave;

public partial class Main
{
    private sealed record ScreenSurface(List<Decoration> Blocks, Node3D Node, Vector2 Size)
    {
        public string? AgentId => Blocks[0].AgentId;
        public string Role => Blocks[0].ScreenRole!;
    }
    private readonly List<ScreenSurface> screenSurfaces=[];
    private Node3D? screensRoot;
    private ScreenSurface? activeScreen;
    private string? screenPress;
    private float screenPressTime;
    private void UpdateFocusedScreenCamera()
    {
        if(!tvFocused || activeScreen==null)return;
        if(!GodotObject.IsInstanceValid(activeScreen.Node)){LeaveTelevision();return;}
        camera.GlobalPosition=activeScreen.Node.ToGlobal(new Vector3(0,0,-Mathf.Clamp(activeScreen.Size.Y*1.2f,1.2f,4)));
        camera.LookAt(activeScreen.Node.GlobalPosition);
    }
    private Decoration? ScreenBlock(string? id)=>id!=null && id.StartsWith("$decor:")
        ?state.Decorations.FirstOrDefault(d=>d.Id==id[7..] && !d.Carried && d.ScreenRole!=null):null;
    private ScreenSurface? SurfaceFor(string? id)=>ScreenBlock(id) is {} block
        ?screenSurfaces.FirstOrDefault(s=>s.Blocks.Contains(block)):null;
    private void AddScreenItem(string role)
    {
        var block=new Decoration("black_concrete",0,0){ScreenRole=role,Carried=true};
        state.Decorations.Add(block);AutoAssignHotbar(block.Id);Changed();RefreshHotbar();
    }
    private void CreateInitialTvBlocks()
    {
        if(state.ScreenBlocksCreated)return;
        state.ScreenBlocksCreated=true;
        for(int x=1;x<=3;x++)for(int y=2;y<=3;y++)
        {
            bool occupied=state.BuildingBlocks.Any(b=>b.X==x && b.Y==y && b.Z==7)
                ||state.Decorations.Any(d=>!d.Carried && d.X==x && d.Y==y && d.Z==7)
                ||state.Chests.Any(c=>!c.Carried && c.X==x && c.Y==y && c.Z==7);
            state.Decorations.Add(new Decoration("black_concrete",x,7){Y=y,ScreenRole="tv",Carried=occupied});
        }
        dirty=true;
    }
    private void RebuildScreenSurfaces()
    {
        AgentRegistry.Migrate(state);
        foreach(var block in state.Decorations.Where(d=>!d.Carried && d.ScreenRole=="desktop"))AgentRegistry.Attach(state,block);
        if(tvFocused)LeaveTelevision();
        if(screensRoot!=null && GodotObject.IsInstanceValid(screensRoot)){screensRoot.GetParent()?.RemoveChild(screensRoot);screensRoot.QueueFree();}
        screensRoot=new Node3D{Name="DisplaySurfaces"};world.AddChild(screensRoot);screenSurfaces.Clear();activeScreen=null;
        tvMaterial ??=new StandardMaterial3D{ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};

        foreach(var group in ScreenLayout.Groups(state.Decorations))
        {
            var first=group[0];var basis=new Basis(Vector3.Up,Mathf.DegToRad(first.Rotation));
            var positions=group.Select(d=>new Vector3(d.X,d.Y+.5f,d.Z)).ToArray();
            var center=positions.Aggregate(Vector3.Zero,(a,b)=>a+b)/positions.Length;
            var local=positions.Select(p=>basis.Inverse()*(p-center)).ToArray();
            var size=new Vector2(local.Max(p=>p.X)-local.Min(p=>p.X)+.96f,local.Max(p=>p.Y)-local.Min(p=>p.Y)+.96f);
            float aspect=first.ScreenRole=="tv"?16f/9:1000f/680;
            if(size.X/size.Y>aspect)size.X=size.Y*aspect;else size.Y=size.X/aspect;
            var node=new Node3D{Position=center+basis*new Vector3(0,0,-.506f),Basis=basis};screensRoot.AddChild(node);
            node.AddChild(new MeshInstance3D{RotationDegrees=new Vector3(0,180,0),Mesh=new QuadMesh{Size=size},MaterialOverride=first.ScreenRole=="tv"?tvMaterial:Agent(first.AgentId!).Material});
            var label=new Label3D{Text=first.ScreenRole=="tv"?"TV\nClick to turn on":"Computer",Font=MinecraftWorldFont(),FontSize=24,PixelSize=.003f,RotationDegrees=new Vector3(0,180,0),Position=new Vector3(0,0,-.005f),TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest,OutlineSize=0};
            node.AddChild(label);
            screenSurfaces.Add(new ScreenSurface(group,node,size));
        }
        SyncVillagers();RefreshTvControls();
    }
    private void RefreshScreenLabels()
    {
        foreach(var surface in screenSurfaces)foreach(var label in surface.Node.GetChildren().OfType<Label3D>())
            label.Visible=surface.Role=="tv"?(!tvOn || tvTexture==null):!agents.TryGetValue(surface.AgentId??"",out var a)||a.Texture==null;
    }
    private bool TryScreenAim(ScreenSurface surface,out Vector2 uv)
    {
        uv=default;
        if(!GodotObject.IsInstanceValid(surface.Node))return false;
        var origin=camera.GlobalPosition;
        var direction=tvFocused?camera.ProjectRayNormal(GetViewport().GetMousePosition()):-camera.GlobalBasis.Z;
        var localOrigin=surface.Node.ToLocal(origin);var localDirection=surface.Node.GlobalBasis.Inverse()*direction;
        if(localDirection.Z<=.0001f)return false;
        float distance=-localOrigin.Z/localDirection.Z;if(distance<0 || distance>6)return false;
        var at=localOrigin+localDirection*distance;
        uv=new Vector2(.5f-at.X/surface.Size.X,.5f-at.Y/surface.Size.Y);
        if(uv.X<0 || uv.X>1 || uv.Y<0 || uv.Y>1)return false;
        var query=PhysicsRayQueryParameters3D.Create(origin,origin+direction*6);query.Exclude=[player.GetRid()];
        return SurfaceFor(RayItem(GetWorld3D().DirectSpaceState.IntersectRay(query)))==surface;
    }
    private bool HandleScreenRelease(InputEvent e)
    {
        if(e is not InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false} || screenPress==null)return false;
        string id=screenPress;screenPress=null;
        bool click=screenPressTime<.25f && walking && hovered==id && panel==null && !paused && !locked;
        miningHeld=false;breakTime=0;breakingId=null;ClearCracks();
        if(click && SurfaceFor(id) is {} surface){activeScreen=surface;if(surface.Role=="tv")FocusTelevision();else OpenVillagerMonitor();}
        GetViewport().SetInputAsHandled();return true;
    }
}
