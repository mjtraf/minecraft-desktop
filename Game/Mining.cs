using Godot;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private readonly Dictionary<string, (RigidBody3D Body, Node3D Visual, float Age)> dropNodes = [];
    private MeshInstance3D? cracks;
    private ShaderMaterial[]? crackMaterials;
    private int crackStage = -1;
    private float miningDust;
    private (string Kind, Vector3 At, Vector3 Size, float Rotation)? TargetShape(string id)
    {
        if (state.Chests.FirstOrDefault(c => c.Id == id && !c.Carried) is {} c) return ("chest",new(c.X,c.Y+7.5f/16,c.Z),new Vector3(14,15,14)/16,c.Rotation);
        if (state.Decorations.FirstOrDefault(d => "$decor:"+d.Id == id && !d.Carried) is {} d) return d.Kind=="item_frame" && d.TabletopFrame ? (d.Kind,new(d.X,d.Y+.32f,d.Z),new Vector3(.66f,.64f,.3f),d.Rotation) : (d.Kind,new(d.X,d.Y+.5f,d.Z),Vector3.One,d.Rotation);
        if (state.BuildingBlocks.FirstOrDefault(b => "$block:"+b.Id == id) is {} b) return (b.Kind,new(b.X,b.Y+.5f,b.Z),Vector3.One,b.Rotation);
        if(ParseValley(id,out var cell) && ValleyKind(cell) is {} kind)return (kind,new Vector3(cell.X,cell.Y+.5f,cell.Z),Vector3.One,0);
        if (id.StartsWith("$terrain:") && terrain.TryGetValue(id[9..],out var t)) return (t.Kind,t.Position,Vector3.One,terrainRotations.GetValueOrDefault(id[9..]));
        return null;
    }
    private void ClearCracks()
    {
        if (cracks != null) cracks.Visible = false;
        crackStage = -1; miningDust = 0;
    }
    private void ShowCracks(string id, float progress)
    {
        if (TargetShape(id) is not {} target) return;
        if(crackMaterials==null)
        {
            var shader=new Shader {Code="shader_type spatial; render_mode unshaded, blend_mul, depth_draw_never, cull_disabled; uniform sampler2D crack_texture : filter_nearest; void fragment() { vec4 c=texture(crack_texture,UV); ALBEDO=mix(vec3(1.0),c.rgb,c.a); ALPHA=1.0; }"};
            crackMaterials=Enumerable.Range(0,10).Select(i=>{var m=new ShaderMaterial {Shader=shader};m.SetShaderParameter("crack_texture",Vanilla("block/destroy_stage_"+i));return m;}).ToArray();
        }
        if (cracks == null) { cracks=new MeshInstance3D { Mesh=VoxelMesh(false) }; AddChild(cracks); }
        crackStage=Math.Clamp((int)(progress*10),0,9);
        cracks.Visible=true; cracks.Position=target.At; cracks.Scale=target.Size+Vector3.One*.008f;
        string shapeKind=target.Kind=="spruce_trapdoor" && state.BuildingBlocks.Any(b=>"$block:"+b.Id==id && !b.Active)?"spruce_trapdoor_closed":target.Kind;
        if(ShapedBlocks.Contains(shapeKind) && !state.Decorations.Any(d=>"$decor:"+d.Id==id && d.TabletopFrame)) {cracks.Mesh=ShapedMesh(shapeKind);cracks.Position=target.At-Vector3.Up*.5f;cracks.Scale=Vector3.One*1.008f;}
        else cracks.Mesh=VoxelMesh(false);
        cracks.RotationDegrees=new Vector3(0,target.Rotation,0); cracks.MaterialOverride=crackMaterials[crackStage];
    }
    private void EmitBlockDust(string kind, Vector3 at, bool burst)
    {
        var particles=new CpuParticles3D { Position=at, Amount=burst?28:5, Lifetime=.45, OneShot=true, Explosiveness=1,
            Direction=Vector3.Up, Spread=120, InitialVelocityMin=.7f, InitialVelocityMax=burst?2.8f:1.2f,
            Gravity=new Vector3(0,-8,0), ScaleAmountMin=.5f, ScaleAmountMax=1,
            Mesh=new BoxMesh { Size=Vector3.One*.055f },
            MaterialOverride=catalogTextures.TryGetValue(kind,out var dustTexture)?ModelMaterial(dustTexture):TextureMaterial(kind=="writable_book"?"planks":ShapedBlocks.Contains(kind) ? (kind.Contains("stone")?"stone_bricks":"spruce_planks") : kind is "chest" or "lantern" ? "planks" : kind) };
        AddChild(particles); particles.Emitting=true; particles.Finished += () => particles.QueueFree();
    }
    private void SpawnDrop(DroppedBlock drop)
    {
        var body=new RigidBody3D { Position=new Vector3(drop.Position[0],drop.Position[1],drop.Position[2]), CollisionLayer=2, CollisionMask=1, Mass=.1f, LockRotation=true, LinearDamp=2 };
        body.AddChild(new CollisionShape3D { Shape=new SphereShape3D { Radius=.13f } }); AddChild(body);
        var visual=new Node3D();body.AddChild(visual);
        if (drop.Kind=="chest") { ChestVisual(visual); visual.Scale=Vector3.One*.3f; }
        else if(drop.Kind=="item_frame" && state.Decorations.FirstOrDefault(d=>d.Id==drop.ItemId) is {} photoFrame) {CreatePictureVisual(visual,photoFrame);visual.Scale=Vector3.One*.35f;}
        else if(drop.Kind=="writable_book") {BookVisual(visual);visual.Scale=Vector3.One*.55f;}
        else if (drop.Kind is "lantern")
        {
            visual.AddChild(new Sprite3D { Texture=Vanilla("item/"+drop.Kind), PixelSize=.02f, Billboard=BaseMaterial3D.BillboardModeEnum.Enabled, TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest });
        }
        else BlockVisual(visual,drop.Kind,-Vector3.Up*.125f,.25f);
        body.LinearVelocity=new Vector3(.35f,1.5f,.15f); dropNodes[drop.Id]=(body,visual,0);
    }
    private void UpdateDrops(float delta)
    {
        if (paused || locked || hidden) return;
        foreach(var drop in state.Drops.ToArray())
        {
            if (!dropNodes.TryGetValue(drop.Id,out var node)) { SpawnDrop(drop); node=dropNodes[drop.Id]; }
            node.Body.Freeze=node.Body.Position.DistanceTo(player.Position)>40;
            if(node.Body.Freeze)continue;
            float age=node.Age+delta;
            node.Visual.Rotation=new Vector3(0,age*1.8f,0);
            node.Visual.Position=new Vector3(0,.08f+Mathf.Sin(age*2.8f)*.05f,0);
            dropNodes[drop.Id]=(node.Body,node.Visual,age);
            var at=node.Body.Position; drop.Position=[at.X,at.Y,at.Z];
            if (at.Y < ValleyBottom-4) { node.Body.Position=new Vector3(at.X,1,at.Z); node.Body.LinearVelocity=Vector3.Zero; }
            if (InDisposalLava(at)) {DestroyDrop(drop);continue;}
            if (age>.6f && walking && panel==null && at.DistanceTo(player.Position+Vector3.Up*.65f)<1.45f) CollectDrop(drop);
        }
    }
    private void CollectDrop(DroppedBlock drop)
    {
        if (!state.Drops.Remove(drop)) return;
        if (drop.ItemId == null) state.Supplies[drop.Kind]=state.Supplies.GetValueOrDefault(drop.Kind)+Math.Clamp(drop.Count,1,64);
        // Named chests and decorations already retain their original ID; removing the drop makes them available in inventory.
        if(dropNodes.Remove(drop.Id,out var node)) node.Body.QueueFree();
        AutoAssignHotbar(drop.ItemId ?? drop.Kind); Changed(); RefreshHotbar(); PlayEffect("pickup");
    }
}
