using Godot;
using System.Text.Json;
namespace CozyCave;
public partial class Main
{
    private static readonly HashSet<string> ShapedBlocks = ["item_frame","dark_oak_stairs","spruce_slab","spruce_trapdoor","spruce_fence","red_carpet","brown_carpet","chain","campfire","stone_brick_stairs","spruce_trapdoor_closed","campfire_off"];
    private readonly Dictionary<string,ArrayMesh> shapedMeshes=[];
    private readonly Dictionary<string,Material> modelMaterials=[];
    private readonly Dictionary<string,List<(Vector3 At,Vector3 Size)>> modelBoxes=[];
    private readonly Dictionary<string,float> terrainRotations=[];
    private Material ModelMaterial(string path)
    {
        if(modelMaterials.TryGetValue(path,out var material)) return material;
        var texture=Vanilla(path);
        if(texture.GetHeight()>texture.GetWidth())
        {
            var animated=new ShaderMaterial {Shader=new Shader {Code="""
                shader_type spatial;
                render_mode cull_disabled;
                uniform sampler2D atlas : source_color, filter_nearest, repeat_disable;
                uniform float frames=32.0;
                uniform float glow=0.0;
                uniform float fps=10.0;
                uniform float unlit=0.0;
                void fragment(){
                    vec4 c=texture(atlas,vec2(UV.x,(floor(mod(TIME*fps,frames))+UV.y)/frames));
                    if(c.a<0.1) discard;
                    ALBEDO=c.rgb*(1.0-unlit); EMISSION=c.rgb*(glow+unlit); ROUGHNESS=1.0;
                }
                """}};
            animated.SetShaderParameter("atlas",texture);animated.SetShaderParameter("frames",texture.GetHeight()/(float)texture.GetWidth());
            animated.SetShaderParameter("fps",path=="block/lantern"?2.5f:10f);
            animated.SetShaderParameter("unlit",path=="block/lantern"?1f:0f);
            animated.SetShaderParameter("glow",path.Contains("fire")?.3f:0f);material=animated;
        }
        else material=new StandardMaterial3D {AlbedoTexture=texture,AlbedoColor=path.EndsWith("_leaves")?new Color("79a747"):Colors.White,TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest,Transparency=BaseMaterial3D.TransparencyEnum.AlphaScissor,CullMode=BaseMaterial3D.CullModeEnum.Disabled,Roughness=1};
        modelMaterials[path]=material;return material;
    }
    private ArrayMesh ShapedMesh(string kind)
    {
        if(shapedMeshes.TryGetValue(kind,out var mesh)) return mesh;
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Vanilla/models/"+kind+".json"));
        var surfaces=new Dictionary<string,SurfaceTool>();var boxes=new List<(Vector3,Vector3)>();
        Vector3 V(JsonElement element) {var a=element.EnumerateArray().Select(v=>v.GetSingle()).ToArray();return new(a[0],a[1],a[2]);}
        foreach(var element in document.RootElement.GetProperty("elements").EnumerateArray())
        {
            var lo=V(element.GetProperty("from"));var hi=V(element.GetProperty("to"));
            bool rotated=element.TryGetProperty("rotation",out var rotation);
            var axis=rotated?rotation.GetProperty("axis").GetString() switch {"x"=>Vector3.Right,"z"=>Vector3.Back,_=>Vector3.Up}:Vector3.Up;
            var origin=rotated?V(rotation.GetProperty("origin")):Vector3.Zero;
            float angle=rotated?Mathf.DegToRad(rotation.GetProperty("angle").GetSingle()):0;
            bool rescale=rotated && rotation.TryGetProperty("rescale",out var r) && r.GetBoolean();
            // Chain planes and flames have no solid volume. Campfires collide with their logs.
            if((hi-lo).X>0 && (hi-lo).Y>0 && (hi-lo).Z>0 && !(kind=="campfire" && hi.Y>7))
            {
                var corners=new List<Vector3>();
                foreach(float x in new[]{lo.X,hi.X}) foreach(float y in new[]{lo.Y,hi.Y}) foreach(float z in new[]{lo.Z,hi.Z})
                {
                    var p=new Vector3(x,y,z)-origin;
                    if(rescale) p*=axis+(Vector3.One-axis)/Mathf.Cos(angle);
                    corners.Add(p.Rotated(axis,angle)+origin);
                }
                var min=new Vector3(corners.Min(p=>p.X),corners.Min(p=>p.Y),corners.Min(p=>p.Z));
                var max=new Vector3(corners.Max(p=>p.X),corners.Max(p=>p.Y),corners.Max(p=>p.Z));
                boxes.Add((((min+max)/2-new Vector3(8,0,8))/16,(max-min)/16));
            }
            foreach(var face in element.GetProperty("faces").EnumerateObject())
            {
                string texture=face.Value.GetProperty("texture").GetString()!;
                if(!surfaces.TryGetValue(texture,out var st)) {st=new SurfaceTool();st.Begin(Mesh.PrimitiveType.Triangles);st.SetMaterial(ModelMaterial(texture));surfaces[texture]=st;}
                var points=face.Name switch
                {
                    "south"=>new Vector3[]{new(lo.X,lo.Y,hi.Z),new(hi.X,lo.Y,hi.Z),new(hi.X,hi.Y,hi.Z),new(lo.X,hi.Y,hi.Z)},
                    "north"=>new Vector3[]{new(hi.X,lo.Y,lo.Z),new(lo.X,lo.Y,lo.Z),new(lo.X,hi.Y,lo.Z),new(hi.X,hi.Y,lo.Z)},
                    "east"=>new Vector3[]{new(hi.X,lo.Y,hi.Z),new(hi.X,lo.Y,lo.Z),new(hi.X,hi.Y,lo.Z),new(hi.X,hi.Y,hi.Z)},
                    "west"=>new Vector3[]{new(lo.X,lo.Y,lo.Z),new(lo.X,lo.Y,hi.Z),new(lo.X,hi.Y,hi.Z),new(lo.X,hi.Y,lo.Z)},
                    "up"=>new Vector3[]{new(lo.X,hi.Y,hi.Z),new(hi.X,hi.Y,hi.Z),new(hi.X,hi.Y,lo.Z),new(lo.X,hi.Y,lo.Z)},
                    _=>new Vector3[]{new(lo.X,lo.Y,lo.Z),new(hi.X,lo.Y,lo.Z),new(hi.X,lo.Y,hi.Z),new(lo.X,lo.Y,hi.Z)}
                };
                var n=face.Name switch {"south"=>Vector3.Back,"north"=>Vector3.Forward,"east"=>Vector3.Right,"west"=>Vector3.Left,"up"=>Vector3.Up,_=>Vector3.Down};
                var uv=face.Value.GetProperty("uv").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                var coords=new[]{new Vector2(uv[0],uv[3]),new Vector2(uv[2],uv[3]),new Vector2(uv[2],uv[1]),new Vector2(uv[0],uv[1])};
                int turn=face.Value.TryGetProperty("rotation",out var frot)?frot.GetInt32()/90:0;
                foreach(int i in new[]{0,1,2,0,2,3})
                {
                    var p=points[i]-origin;
                    if(rescale) p*=axis+(Vector3.One-axis)/Mathf.Cos(angle);
                    p=p.Rotated(axis,angle)+origin;
                    st.SetNormal(n.Rotated(axis,angle));st.SetUV(coords[(i+turn)%4]/16);st.AddVertex((p-new Vector3(8,0,8))/16);
                }
            }
        }
        mesh=new ArrayMesh();foreach(var st in surfaces.Values) {st.Commit(mesh);st.Dispose();}
        modelBoxes[kind]=boxes;shapedMeshes[kind]=mesh;return mesh;
    }
    private void BlockVisual(Node parent,string kind,Vector3 bottom,float scale=1)
    {
        if(ShapedBlocks.Contains(kind)) parent.AddChild(new MeshInstance3D {Mesh=ShapedMesh(kind),Position=bottom,Scale=Vector3.One*scale});
        else Voxel(parent,kind,bottom+Vector3.Up*.5f*scale,scale);
    }
    private void BlockCollision(Node parent,string kind,string id)
    {
        if(!ShapedBlocks.Contains(kind)) {Collider(parent,Vector3.Up*.5f,Vector3.One,id);return;}
        ShapedMesh(kind);
        if(kind=="chain") {Collider(parent,Vector3.Up*.5f,new Vector3(.18f,1,.18f),id);return;}
        foreach(var (at,size) in modelBoxes[kind]) Collider(parent,at,size,id);
    }
    private bool Furnish(string kind,int x,int y,int z,float rotation=0)
    {
        // Never put new furniture through the user's chests, decorations, or placed blocks.
        if(state.Chests.Any(c=>!c.Carried && Math.Abs(c.X-x)<.9 && Math.Abs(c.Z-z)<.9 && Math.Abs(c.Y-y)<1.5)
           || state.Decorations.Any(d=>!d.Carried && Math.Abs(d.X-x)<.9 && Math.Abs(d.Z-z)<.9 && Math.Abs(d.Y-y)<1)
           || state.BuildingBlocks.Any(b=>b.X==x && b.Y==y && b.Z==z)) return false;
        string key=FormattableString.Invariant($"{x},{y+.5f},{z}");
        if(state.RemovedTerrain.Contains(key) || terrain.ContainsKey(key)) return false;
        terrain[key]=(kind,new Vector3(x,y+.5f,z));terrainRotations[key]=rotation;
        var node=new Node3D {Position=new Vector3(x,y,z),RotationDegrees=new Vector3(0,rotation,0)};world.AddChild(node);
        BlockVisual(node,kind,Vector3.Zero);BlockCollision(node,kind,"$terrain:"+key);
        if(kind=="campfire") node.AddChild(new OmniLight3D {Position=new Vector3(0,.7f,.25f),LightColor=new Color("ffaf59"),LightEnergy=1.15f,OmniRange=7,ShadowEnabled=true});
        return true;
    }
}
