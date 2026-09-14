using Godot;
namespace CozyCave;
public partial class Main
{
    private ShaderMaterial? waterMaterial, rainMaterial;
    private int windowGlassCount, rainColumnCount;
    private MultiMeshInstance3D? outsideRain;
    private Vector2I rainCenter=new(int.MaxValue,int.MaxValue);
    private float rainRefresh;
    private readonly PlaneMesh waterPlane = new() {Size=Vector2.One};
    private readonly QuadMesh rainPlane = new() {Size=new Vector2(1.5f,1)};
    private void BuildWaterAndLookout()
    {
        waterMaterial ??= new ShaderMaterial { Shader=new Shader { Code="""
            shader_type spatial;
            render_mode blend_mix, depth_draw_never, cull_back;
            uniform sampler2D water_atlas : source_color, filter_nearest, repeat_disable;
            uniform float frame_count = 32.0;
            void fragment() {
                float frame = floor(mod(TIME * 10.0, frame_count));
                vec2 tile = vec2(clamp(UV.x,0.001,0.999),(frame + clamp(UV.y,0.001,0.999))/frame_count);
                ALBEDO = texture(water_atlas,tile).rgb * vec3(0.247,0.463,0.894);
                ALPHA = 0.68;
                ROUGHNESS = 1.0;
                SPECULAR = 0.0;
            }
            """ } };
        var waterTexture=Vanilla("block/water_still");waterMaterial.SetShaderParameter("water_atlas",waterTexture);
        waterMaterial.SetShaderParameter("frame_count",waterTexture.GetHeight()/(float)waterTexture.GetWidth());
        for(int z=-7;z<=7;z++)
        {
            Voxel(world,"stone",new Vector3(6,-1.5f,z));

        }
        windowGlassCount=0;
        for(int x=-6;x<=2;x++) for(int y=0;y<8;y++)
        {
            if(y==4 || x==-3) TerrainBlock("dark_oak_log",new Vector3(x,y+.5f,-9));
            else { TerrainBlock("glass",new Vector3(x,y+.5f,-9));windowGlassCount++; }
        }
        // Actual voxel landscape beyond the glass; the rain geometry is entirely outside the cave.

        rainMaterial ??= new ShaderMaterial {Shader=new Shader {Code="""
            shader_type spatial;
            render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
            uniform sampler2D rain_texture : source_color, filter_nearest, repeat_enable;
            uniform float phase = 0.0;
            varying float height;
            void vertex(){height=(MODEL_MATRIX*vec4(VERTEX,1.0)).y;}
            void fragment() {
                vec4 streak=texture(rain_texture,vec2(UV.x,-height/3.0-TIME*1.35+phase));
                ALBEDO=streak.rgb*vec3(0.7,0.78,0.9);
                ALPHA=streak.a*0.3;
            }
            """}};
        rainMaterial.SetShaderParameter("rain_texture",Vanilla("environment/rain"));
        outsideRain=new MultiMeshInstance3D {Name="OutsideRain",MaterialOverride=rainMaterial,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
        world.AddChild(outsideRain);rainCenter=new(int.MaxValue,int.MaxValue);rainRefresh=0;
    }
    private void UpdateRain(float delta)
    {
        if(outsideRain==null || !IsInstanceValid(outsideRain))return;
        rainRefresh-=delta;var center=new Vector2I(Mathf.RoundToInt(player.Position.X/2)*2,Mathf.RoundToInt(player.Position.Z/2)*2);
        if(center==rainCenter && rainRefresh>0)return;
        rainCenter=center;rainRefresh=1;
        var roofs=new Dictionary<Vector2I,float>();
        void Roof(float x,float z,float top) {var cell=new Vector2I(Mathf.RoundToInt(x),Mathf.RoundToInt(z));roofs[cell]=Math.Max(roofs.GetValueOrDefault(cell,ValleyBottom),top);}
        foreach(var t in terrain.Values)Roof(t.Position.X,t.Position.Z,t.Position.Y+.5f);
        foreach(var b in state.BuildingBlocks)Roof(b.X,b.Z,b.Y+1);
        foreach(var c in state.Chests.Where(c=>!c.Carried))Roof(c.X,c.Z,c.Y+1);
        foreach(var t in valleyTrees)if(!valleyRemoved.Contains(t.Key))Roof(t.Key.X,t.Key.Z,t.Key.Y+1);
        var transforms=new List<Transform3D>();
        for(int x=center.X-26;x<=center.X+26;x+=2)for(int z=center.Y-26;z<=center.Y+26;z+=2)
        {
            if(Math.Abs(x)>WorldRadius || Math.Abs(z)>WorldRadius)continue;
            int ground=GroundHeight(x,z);while(ground>ValleyBottom && ValleyKind(new Vector3I(x,ground,z))==null)ground--;
            float bottom=Math.Max(ground+1,roofs.GetValueOrDefault(new(x,z),ValleyBottom));
            float top=Math.Max(bottom+14,player.Position.Y+18),height=top-bottom;
            var at=new Vector3(x,(top+bottom)/2,z);
            transforms.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(1,height,1)),at));
            transforms.Add(new Transform3D(new Basis(Vector3.Up,Mathf.Pi/2).Scaled(new Vector3(1,height,1)),at));
        }
        var rain=new MultiMesh {TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=rainPlane,InstanceCount=transforms.Count};
        for(int i=0;i<transforms.Count;i++)rain.SetInstanceTransform(i,transforms[i]);
        outsideRain.Multimesh=rain;rainColumnCount=transforms.Count/2;

    }
}
