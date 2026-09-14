using Godot;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private readonly WaterFlow water = new();
    private Node3D? waterVisual;
    private float waterTick;
    private ShaderMaterial? flowingWaterMaterial;
    private readonly QuadMesh waterSide = new() { Size = Vector2.One };
    private bool WaterSolid(WaterCell cell, HashSet<WaterCell> occupied) => cell.Y < -1 || occupied.Contains(cell);
    private HashSet<WaterCell> WaterObstacles()
    {
        var occupied = terrain.Values.Select(t => new WaterCell((int)Mathf.Round(t.Position.X), (int)Mathf.Floor(t.Position.Y), (int)Mathf.Round(t.Position.Z))).ToHashSet();
        foreach(var b in state.BuildingBlocks) occupied.Add(new(b.X,b.Y,b.Z));
        foreach(var c in state.Chests.Where(c=>!c.Carried)) occupied.Add(new((int)c.X,(int)c.Y,(int)c.Z));
        foreach(var d in state.Decorations.Where(d=>!d.Carried)) occupied.Add(new((int)d.X,(int)d.Y,(int)d.Z));
        occupied.Add(new(5,-1,6)); // The lava source is a separate fluid.
        return occupied;
    }
    private void InitializeWater()
    {
        if(proof) return;
        for(int z=-7;z<=7;z++) water.Sources.Add(new(6,-1,z));
        var occupied=WaterObstacles();water.Step(c=>WaterSolid(c,occupied));
        waterVisual=null; DrawWater();
    }
    private void UpdateWater(float delta)
    {
        if(proof || paused || locked || hidden) return;
        waterTick+=delta;if(waterTick<.2f) return;waterTick=0;
        var occupied=WaterObstacles();
        if(water.Step(c=>WaterSolid(c,occupied))) DrawWater();
    }
    private bool InWater(Vector3 at) => water.Cells.ContainsKey(new((int)Mathf.Round(at.X),(int)Mathf.Floor(at.Y),(int)Mathf.Round(at.Z)));
    private void DrawWater()
    {
        if(waterVisual!=null && GodotObject.IsInstanceValid(waterVisual)) {waterVisual.GetParent()?.RemoveChild(waterVisual);waterVisual.QueueFree();}
        waterVisual=new Node3D {Name="FlowingStream"};world.AddChild(waterVisual);
        flowingWaterMaterial ??= new ShaderMaterial {Shader=new Shader {Code="""
            shader_type spatial;
            render_mode blend_mix, depth_draw_never, cull_disabled;
            uniform sampler2D water_atlas : source_color, filter_nearest, repeat_disable;
            uniform float frame_count=32.0;
            void fragment() {
                float frame=floor(mod(TIME*10.0,frame_count));
                vec2 uv=fract(UV+vec2(0.0,-TIME*0.35));
                ALBEDO=texture(water_atlas,vec2(uv.x,(frame+uv.y)/frame_count)).rgb*vec3(0.247,0.463,0.894);
                ALPHA=0.68;ROUGHNESS=1.0;SPECULAR=0.0;
            }
            """}};
        var tex=Vanilla("block/water_flow");flowingWaterMaterial.SetShaderParameter("water_atlas",tex);
        flowingWaterMaterial.SetShaderParameter("frame_count",tex.GetHeight()/(float)tex.GetWidth());
        foreach(var (cell,level) in water.Cells)
        {
            float height=level==0?.875f:(8-level)/9f;
            var center=new Vector3(cell.X,cell.Y,cell.Z);
            bool source=water.Sources.Contains(cell);
            waterVisual.AddChild(new MeshInstance3D {Mesh=waterPlane,Position=center+Vector3.Up*height,MaterialOverride=source?waterMaterial:flowingWaterMaterial});
            foreach(var (dx,dz,angle) in new[]{(0,1,0f),(1,0,Mathf.Pi/2),(0,-1,Mathf.Pi),(-1,0,-Mathf.Pi/2)})
            {
                var neighbor=cell with {X=cell.X+dx,Z=cell.Z+dz};
                if(water.Cells.TryGetValue(neighbor,out int other) && other<=level) continue;
                waterVisual.AddChild(new MeshInstance3D {Mesh=waterSide,Position=center+new Vector3(dx*.5f,height/2,dz*.5f),Scale=new Vector3(1,height,1),Rotation=new Vector3(0,angle,0),MaterialOverride=flowingWaterMaterial});
            }
        }
    }
}
