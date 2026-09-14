using Godot;
namespace CozyCave;
public partial class Main
{
    private FontFile? minecraftFont;
    private FontFile? minecraftWorldFont;
    private FontFile MinecraftWorldFont()
    {
        if (minecraftWorldFont != null) return minecraftWorldFont;
        // Label3D insets glyph UVs by half a texel at each edge. A one-pixel
        // region collapses; two-pixel stems also clip. Include the transparent
        // neighboring columns without changing the glyph's advance or origin.
        // Use a separate cache: UI labels must retain their original font data.
        minecraftWorldFont = (FontFile)MinecraftFont().Duplicate(true);
        foreach (var size in minecraftWorldFont.GetSizeCacheList(0))
        foreach (var glyph in minecraftWorldFont.GetGlyphList(0, size))
        {
            var uv = minecraftWorldFont.GetGlyphUVRect(0, size, glyph);
            if (uv.Size.X < 1 || uv.Size.X > 2 || uv.Position.X < 1) continue;
            var extent = minecraftWorldFont.GetGlyphSize(0, size, glyph);
            var offset = minecraftWorldFont.GetGlyphOffset(0, size, glyph);
            uv.Position -= new Vector2(1, 0); uv.Size += new Vector2(2, 0);
            minecraftWorldFont.SetGlyphUVRect(0, size, glyph, uv);
            minecraftWorldFont.SetGlyphSize(0, size, glyph, extent + new Vector2(2, 0));
            minecraftWorldFont.SetGlyphOffset(0, size, glyph, offset - new Vector2(1, 0));
        }
        return minecraftWorldFont;
    }
    private FontFile MinecraftFont()
    {
        if(minecraftFont!=null) return minecraftFont;
        minecraftFont=GD.Load<FontFile>("res://Assets/Vanilla/font/minecraft.fnt");
        minecraftFont.Fallbacks = [new SystemFont {FontNames=["Segoe UI"]}]; return minecraftFont;
    }
    private static readonly Dictionary<string, string> BlockNames = new()
    { ["writable_book"]="Book and Quill", ["item_frame"]="Item Frame", ["dark_oak_log"]="Dark Oak Log", ["dark_oak_planks"]="Dark Oak Planks", ["spruce_planks"]="Spruce Planks", ["stone_bricks"]="Stone Bricks", ["cobblestone"]="Cobblestone", ["dark_oak_stairs"]="Dark Oak Stairs", ["spruce_slab"]="Spruce Slab", ["spruce_fence"]="Spruce Fence", ["spruce_trapdoor"]="Spruce Trapdoor", ["red_carpet"]="Red Carpet", ["brown_carpet"]="Brown Carpet", ["chain"]="Chain", ["campfire"]="Campfire", ["stone_brick_stairs"]="Stone Brick Stairs", ["stone"] = "Stone", ["moss"] = "Moss Block", ["planks"] = "Oak Planks", ["timber"] = "Oak Log", ["brick"] = "Bricks", ["leaves"] = "Oak Leaves", ["chest"] = "Chest", ["barrel"] = "Barrel", ["glass"] = "Glass", ["bookshelf"] = "Bookshelf", ["lantern"] = "Lantern" };
    private static string BlockName(string kind) => BlockNames.GetValueOrDefault(kind, kind);
    private static string VanillaSide(string kind) => kind switch
    { "moss" => "moss_block", "planks" or "oak" => "oak_planks", "timber" => "oak_log", "brick" => "bricks", "leaves" => "oak_leaves", "barrel" => "barrel_side", _ => kind };
    private readonly Dictionary<string, StandardMaterial3D> cubeMaterials = [];
    private readonly Dictionary<string, Texture2D> itemIcons = [];
    private Texture2D Vanilla(string path) => GD.Load<Texture2D>("res://Assets/Vanilla/" + path + ".png");
    private StandardMaterial3D CubeMaterial(string kind)
    {
        if (cubeMaterials.TryGetValue(kind, out var material)) return material;
        string side = VanillaSide(kind), top = kind switch { "timber" => "oak_log_top", "dark_oak_log"=>"dark_oak_log_top", "barrel" => "barrel_top", "bookshelf" => "oak_planks", _ => side };
        var atlas = Image.CreateEmpty(48, 16, false, Image.Format.Rgba8);
        using var sideImage = Vanilla("block/" + side).GetImage(); using var topImage = Vanilla("block/" + top).GetImage();
        sideImage.Convert(Image.Format.Rgba8); topImage.Convert(Image.Format.Rgba8);
        atlas.BlitRect(sideImage, new Rect2I(0, 0, 16, 16), Vector2I.Zero);
        atlas.BlitRect(topImage, new Rect2I(0, 0, 16, 16), new Vector2I(16, 0));
        atlas.BlitRect(topImage, new Rect2I(0, 0, 16, 16), new Vector2I(32, 0));
        material = new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(atlas), TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, Roughness = 1, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor };
        if (kind == "leaves") material.AlbedoColor = new Color("79a747");
        cubeMaterials[kind] = material; atlas.Dispose(); return material;
    }
    private ArrayMesh? voxelMesh, crackCubeMesh;
    private ArrayMesh VoxelMesh(bool atlas = true)
    {
        if (atlas && voxelMesh != null) return voxelMesh;
        if (!atlas && crackCubeMesh != null) return crackCubeMesh;
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, int column)
        {
            var points = new[] { a, b, c, d }; var uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
            foreach (int i in new[] { 0, 1, 2, 0, 2, 3 }) { st.SetNormal(normal); st.SetUV(atlas ? new Vector2((column + uv[i].X) / 3, uv[i].Y) : uv[i]); st.AddVertex(points[i]); }
        }
        Face(new(-.5f,-.5f,.5f),new(.5f,-.5f,.5f),new(.5f,.5f,.5f),new(-.5f,.5f,.5f),Vector3.Back,0);
        Face(new(.5f,-.5f,-.5f),new(-.5f,-.5f,-.5f),new(-.5f,.5f,-.5f),new(.5f,.5f,-.5f),Vector3.Forward,0);
        Face(new(.5f,-.5f,.5f),new(.5f,-.5f,-.5f),new(.5f,.5f,-.5f),new(.5f,.5f,.5f),Vector3.Right,0);
        Face(new(-.5f,-.5f,-.5f),new(-.5f,-.5f,.5f),new(-.5f,.5f,.5f),new(-.5f,.5f,-.5f),Vector3.Left,0);
        Face(new(-.5f,.5f,.5f),new(.5f,.5f,.5f),new(.5f,.5f,-.5f),new(-.5f,.5f,-.5f),Vector3.Up,1);
        Face(new(-.5f,-.5f,-.5f),new(.5f,-.5f,-.5f),new(.5f,-.5f,.5f),new(-.5f,-.5f,.5f),Vector3.Down,2);
        var mesh = st.Commit(); if(atlas) voxelMesh=mesh; else crackCubeMesh=mesh; return mesh;
    }
    private MeshInstance3D Voxel(Node parent, string kind, Vector3 at, float scale = 1)
    {
        var node = new MeshInstance3D { Mesh = VoxelMesh(), MaterialOverride = CubeMaterial(kind), Position = at, Scale = Vector3.One * scale }; parent.AddChild(node); return node;
    }
    private Texture2D ItemIcon(string kind)
    {
        if (itemIcons.TryGetValue(kind, out var icon)) return icon;
        if (kind is "lantern" or "item_frame" or "writable_book") return Vanilla("item/"+kind);
        // Render the same textured model used in the world into an isolated icon viewport.
        var viewport = new SubViewport { Size = new Vector2I(96, 96), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
        AddChild(viewport);
        var scene = new Node3D(); viewport.AddChild(scene);
        if (kind == "chest") ChestVisual(scene); else BlockVisual(scene, kind, Vector3.Zero);
        var cam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 1.65f, Position = new Vector3(2, 2, 3), Current = true }; scene.AddChild(cam); cam.LookAt(Vector3.Up * .5f);
        scene.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Transparent, AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .8f } });
        scene.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, -30, 0), LightEnergy = .65f });
        icon = viewport.GetTexture(); itemIcons[kind] = icon; return icon;
    }
    private Node3D ChestVisual(Node parent)
    {
        var root = new Node3D(); parent.AddChild(root);
        // Vanilla chest model: 14x10x14 base, 14x5x14 lid, 2x4x1 latch, in sixteenths.
        AtlasBox(root, new Vector3(0, 5f/16, 0), new Vector3(14,10,14), new Vector2(0,19));
        var lid = new Node3D { Position = new Vector3(0, 10f/16, -7f/16) }; root.AddChild(lid);
        AtlasBox(lid, new Vector3(0, 2.5f/16, 7f/16), new Vector3(14,5,14), Vector2.Zero);
        AtlasBox(lid, new Vector3(0, -1f/16, 14.5f/16), new Vector3(2,4,1), Vector2.Zero);
        return lid;
    }
    private void AtlasBox(Node parent, Vector3 at, Vector3 pixels, Vector2 offset, Texture2D? texture = null)
    {
        float w=pixels.X, h=pixels.Y, d=pixels.Z;
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        var half = pixels / 32;
        void Face(Vector3[] ps, Vector3 n, Rect2 uv)
        {
            var coords = new[] {new Vector2(uv.Position.X,uv.End.Y),uv.End,new Vector2(uv.End.X,uv.Position.Y),uv.Position};
            foreach(int i in new[]{0,1,2,0,2,3}) {st.SetNormal(n);st.SetUV((coords[i]+offset)/64);st.AddVertex(ps[i]*half);}
        }
        Face([new(-1,-1,1),new(1,-1,1),new(1,1,1),new(-1,1,1)],Vector3.Back,new(d,d,w,h));
        Face([new(1,-1,-1),new(-1,-1,-1),new(-1,1,-1),new(1,1,-1)],Vector3.Forward,new(2*d+w,d,w,h));
        Face([new(1,-1,1),new(1,-1,-1),new(1,1,-1),new(1,1,1)],Vector3.Right,new(d+w,d,d,h));
        Face([new(-1,-1,-1),new(-1,-1,1),new(-1,1,1),new(-1,1,-1)],Vector3.Left,new(0,d,d,h));
        Face([new(-1,1,1),new(1,1,1),new(1,1,-1),new(-1,1,-1)],Vector3.Up,new(d,0,w,d));
        Face([new(-1,-1,-1),new(1,-1,-1),new(1,-1,1),new(-1,-1,1)],Vector3.Down,new(d+w,0,w,d));
        parent.AddChild(new MeshInstance3D { Mesh=st.Commit(), Position=at, MaterialOverride=new StandardMaterial3D {AlbedoTexture=texture ?? Vanilla("entity/chest/normal"),TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest, CullMode=BaseMaterial3D.CullModeEnum.Disabled} });
    }

    private ArrayMesh? lanternMesh;
    private void LanternVisual(Node parent)
    {
        if(lanternMesh == null)
        {
            using var model=System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Vanilla/lantern.json"));
            var st=new SurfaceTool();st.Begin(Mesh.PrimitiveType.Triangles);
            foreach(var element in model.RootElement.GetProperty("elements").EnumerateArray())
            {
                float[] lo=element.GetProperty("from").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                float[] hi=element.GetProperty("to").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                bool rotated=element.TryGetProperty("rotation",out var rotation);
                float angle=rotated?Mathf.DegToRad(rotation.GetProperty("angle").GetSingle()):0;
                Vector3 origin=rotated?new Vector3(8,8,8):Vector3.Zero;
                foreach(var face in element.GetProperty("faces").EnumerateObject())
                {
                    var positions=face.Name switch
                    {
                        "south"=>new Vector3[]{new(lo[0],lo[1],hi[2]),new(hi[0],lo[1],hi[2]),new(hi[0],hi[1],hi[2]),new(lo[0],hi[1],hi[2])},
                        "north"=>new Vector3[]{new(hi[0],lo[1],lo[2]),new(lo[0],lo[1],lo[2]),new(lo[0],hi[1],lo[2]),new(hi[0],hi[1],lo[2])},
                        "east"=>new Vector3[]{new(hi[0],lo[1],hi[2]),new(hi[0],lo[1],lo[2]),new(hi[0],hi[1],lo[2]),new(hi[0],hi[1],hi[2])},
                        "west"=>new Vector3[]{new(lo[0],lo[1],lo[2]),new(lo[0],lo[1],hi[2]),new(lo[0],hi[1],hi[2]),new(lo[0],hi[1],lo[2])},
                        "up"=>new Vector3[]{new(lo[0],hi[1],hi[2]),new(hi[0],hi[1],hi[2]),new(hi[0],hi[1],lo[2]),new(lo[0],hi[1],lo[2])},
                        _=>new Vector3[]{new(lo[0],lo[1],lo[2]),new(hi[0],lo[1],lo[2]),new(hi[0],lo[1],hi[2]),new(lo[0],lo[1],hi[2])}
                    };
                    float[] uv=face.Value.GetProperty("uv").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                    var coords=new Vector2[]{new(uv[0],uv[3]),new(uv[2],uv[3]),new(uv[2],uv[1]),new(uv[0],uv[1])};
                    Vector3 normal=face.Name switch {"south"=>Vector3.Back,"north"=>Vector3.Forward,"east"=>Vector3.Right,"west"=>Vector3.Left,"up"=>Vector3.Up,_=>Vector3.Down};
                    foreach(int i in new[]{0,1,2,0,2,3})
                    {
                        var position=positions[i];if(rotated) position=(position-origin).Rotated(Vector3.Up,angle)+origin;
                        st.SetNormal(normal.Rotated(Vector3.Up,angle));st.SetUV(coords[i]/16);st.AddVertex((position-new Vector3(8,0,8))/16);
                    }
                }
            }
            lanternMesh=st.Commit();
        }
        parent.AddChild(new MeshInstance3D {Mesh=lanternMesh,MaterialOverride=ModelMaterial("block/lantern")});
    }
}
