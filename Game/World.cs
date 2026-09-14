using Godot;

namespace CozyCave;
public partial class Main
{
    private readonly Dictionary<Vector3, BoxShape3D> collisionShapes = [];
    private Godot.Environment? caveEnvironment;
    private readonly Dictionary<string, Node3D> placedNodes = [];
    private readonly Dictionary<string, Node3D> lids = [];
    private readonly Dictionary<string, Node3D> chestNodes = [];
    private readonly Dictionary<string, StandardMaterial3D> materials = [];
    private readonly Dictionary<string, List<Transform3D>> blocks = [];
    private readonly Random random = new(87);
    private string? highlightedChest;
    private StandardMaterial3D Material(string hex) => new() { AlbedoColor = new Color(hex), Roughness = 0.95f };
    private StandardMaterial3D TextureMaterial(string name)
    {
        if (materials.TryGetValue(name, out var material)) return material;
        var path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "CozyCave", "packs", "Textures", name + ".png");
        Texture2D texture = name == "water" ? new AtlasTexture { Atlas=Vanilla("block/water_still"), Region=new Rect2(0,0,16,16) } : System.IO.File.Exists(path) ? ImageTexture.CreateFromImage(Image.LoadFromFile(path)) : BlockNames.ContainsKey(name) && name != "chest" && name != "lantern" ? Vanilla("block/" + VanillaSide(name)) : GD.Load<Texture2D>("res://Assets/Textures/" + name + ".png");
        material = new StandardMaterial3D { AlbedoTexture = texture, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, Roughness = 0.95f };
        if(name == "water") material.AlbedoColor=new Color("3f76e4");
        materials[name] = material; return material;
    }
    private MeshInstance3D Box(Node parent, Vector3 at, Vector3 size, Material material, bool collision = false, string? id = null)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = at }; parent.AddChild(mesh);
        if (collision) Collider(parent, at, size, id);
        return mesh;
    }
    private void Collider(Node parent, Vector3 at, Vector3 size, string? id = null)
    {
        var body = new StaticBody3D { Position = at };
        if(!collisionShapes.TryGetValue(size,out var shape)) {shape=new BoxShape3D {Size=size};collisionShapes[size]=shape;}
        body.AddChild(new CollisionShape3D { Shape = shape });
        if (id != null) body.SetMeta("item", id); parent.AddChild(body);
    }
    private void Block(string material, Vector3 position, Vector3? scale = null)
    {
        if (!blocks.ContainsKey(material)) blocks[material] = [];
        blocks[material].Add(new Transform3D(Basis.Identity.Scaled(scale ?? Vector3.One), position));
    }
    private void FlushBlocks()
    {
        foreach (var (name, transforms) in blocks)
        {
            var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = BlockNames.ContainsKey(name) ? VoxelMesh() : new BoxMesh(), InstanceCount = transforms.Count };
            for (int i = 0; i < transforms.Count; i++) multimesh.SetInstanceTransform(i, transforms[i]);
            world.AddChild(new MultiMeshInstance3D { Multimesh = multimesh, MaterialOverride = BlockNames.ContainsKey(name) ? CubeMaterial(name) : TextureMaterial(name) });
        }
        blocks.Clear();
    }
    private void BuildWorld()
    {
        var random = new Random(87);
        if (world != null) { RemoveChild(world); world.QueueFree(); }
        lids.Clear(); chestNodes.Clear(); placedNodes.Clear(); blocks.Clear(); terrain.Clear(); terrainRotations.Clear(); world = new Node3D(); AddChild(world);
        var env = caveEnvironment ??= new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("263748"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color("bba990"), AmbientLightEnergy = .20f, TonemapMode = Godot.Environment.ToneMapper.Filmic, FogEnabled = true, FogLightColor = new Color("263748"), FogDensity = .0025f };
        world.AddChild(new WorldEnvironment { Environment = env });
        world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-65, -25, 0), LightColor = new Color("a6b9d4"), LightEnergy = .10f });
        if(proof) Collider(world,new Vector3(0,-1.5f,0),new Vector3(21,1,21));
        for(int x=-9;x<=9;x++) for(int z=-9;z<=9;z++)
        {
            if((proof || x!=6 || Math.Abs(z)>7) && (proof || x!=5 || z!=6)) TerrainBlock(IsLavaFloor(x,z)?"stone_bricks":Math.Abs(x)<9 && Math.Abs(z)<9 ? (x%4==0?"dark_oak_planks":"spruce_planks") : "stone",new Vector3(x,-.5f,z));
            bool rim=Math.Abs(x)==9 || Math.Abs(z)==9;
            if(rim) for(int y=0;y<9;y++)
            {
                bool window=z==-9 && x>=-6 && x<=2 && y<8;
                bool door=x==9 && z>=-4 && z<=-2 && y<3;
                if(!window && !door) TerrainBlock((x+z+y)%5==0?"cobblestone":"stone_bricks",new Vector3(x,y+.5f,z));
            }
            if(!proof) TerrainBlock("dark_oak_planks",new Vector3(x,9.5f,z));
        }
        if(!proof)
        {
            foreach(int z in new[]{-8,-4,4,8})
            {
                foreach(int x in new[]{-8,8}) for(int y=0;y<9;y++) Furnish("dark_oak_log",x,y,z);
                for(int x=-8;x<=8;x++) Furnish("dark_oak_log",x,8,z);
            }
            BuildWaterAndLookout(); BuildCozyInterior(); BuildWritingDesk(); BuildDisposalLava(); BuildTelevision();
            Lantern(new Vector3(8,2.6f,-3),true);
            Furnish("jukebox",3,0,3);
            foreach (var decoration in state.Decorations.Where(d => !d.Carried)) CreateDecoration(decoration);
        }
        if(!proof) {TerrainBlock("spruce_planks",new Vector3(6,-.5f,-3));EnsureValley(player==null?new Vector3(state.Player[0],state.Player[1],state.Player[2]):player.Position,valleyRoot==null);}
        FlushBlocks();
        foreach (var chest in (proof ? state.Chests.Take(1) : state.Chests).Where(c => !c.Carried)) CreateChest(chest);
        foreach (var b in state.BuildingBlocks) CreateBuildingBlock(b);
        InitializeWater();
    }
    private void RemovePlacedObject(string id)
    {
        if(chestNodes.Remove(id,out var chest)) {chest.GetParent().RemoveChild(chest);chest.QueueFree();lids.Remove(id);}
        if(placedNodes.Remove(id,out var node)) {node.GetParent().RemoveChild(node);node.QueueFree();}
    }
    private void CreateBuildingBlock(Cave.Core.BuildingBlock b)
    {
        var node=new Node3D {Position=new Vector3(b.X,b.Y,b.Z)};world.AddChild(node);placedNodes["$block:"+b.Id]=node;
        node.RotationDegrees=new Vector3(0,b.Rotation,0); string visual=b.Kind=="spruce_trapdoor"&&!b.Active?"spruce_trapdoor_closed":b.Kind=="campfire"&&!b.Active?"campfire_off":b.Kind;
        BlockVisual(node,visual,Vector3.Zero);BlockCollision(node,visual,"$block:"+b.Id);
        if(b.Kind=="campfire" && b.Active) node.AddChild(new OmniLight3D {Position=Vector3.Up*.7f,LightColor=new Color("ffaf59"),LightEnergy=1.15f,OmniRange=7});
    }
    private void CreateChest(Cave.Core.Chest chest)
    {
        var node = new Node3D { Position = new Vector3(chest.X, chest.Y, chest.Z), RotationDegrees = new Vector3(0, chest.Rotation, 0) }; world.AddChild(node); chestNodes[chest.Id] = node;
        lids[chest.Id] = ChestVisual(node);
        Collider(node, new Vector3(0, 7.5f/16, 0), new Vector3(14,15,14)/16, chest.Id);
        Sign(node, chest.Name, new Vector3(0, 1.25f, 0));
        if (highlightedChest == chest.Id) node.AddChild(new OmniLight3D { Position = new Vector3(0, 1.4f, 0), LightColor = new Color("9cd7a1"), LightEnergy = 2, OmniRange = 3 });
    }
    private void Sign(Node parent, string text, Vector3 at)
    { parent.AddChild(new Label3D { Text = text, Position = at, FontSize = 34, PixelSize = .0058f, Modulate = new Color("f0dfb6"), OutlineModulate = new Color("30281e"), OutlineSize = 5, NoDepthTest = false }); }
    private void Lantern(Vector3 at, bool small)
    {
        var node = new Node3D { Position = at }; world.AddChild(node);
        LanternVisual(node);
        node.AddChild(new OmniLight3D { Position=Vector3.Up*.25f, LightColor = new Color("ffcc83"), LightEnergy = 1.05f, OmniRange = 6, OmniAttenuation = 1.2f });
    }
    private void CreateDecoration(Cave.Core.Decoration d)
    {
        if(d.Kind=="writable_book") {d.X=Mathf.Round(d.X);d.Z=Mathf.Round(d.Z);}
        var node = new Node3D { Position = new Vector3(d.X, d.Y, d.Z), RotationDegrees = new Vector3(0, d.Rotation, 0) }; world.AddChild(node);
        string id = "$decor:" + d.Id; placedNodes[id]=node;
        if(d.Kind=="writable_book") {node.Name="DeskNotebook";BookVisual(node);Collider(node,new Vector3(0,.09f,0),new Vector3(.55f,.18f,.68f),id);}
        else if (d.Kind == "item_frame") {if(d.TabletopFrame) {CreateTabletopFrame(node,d);Collider(node,new Vector3(0,.32f,0),new Vector3(.66f,.64f,.3f),id);} else {CreatePictureVisual(node,d);BlockCollision(node,"item_frame",id);}}
        else if (d.Kind == "lantern")
        {
            LanternVisual(node);node.AddChild(new OmniLight3D {Position=Vector3.Up*.3f,LightColor=new Color("ffcc83"),LightEnergy=1.8f,OmniRange=6});
            Collider(node, new Vector3(0,.3f,0), new Vector3(.375f,.6f,.375f), id);
        }
        else
        {
            Voxel(node, d.Kind, Vector3.Up * .5f);
            Collider(node, Vector3.Up * .5f, Vector3.One, id);
        }
    }

    private void AnimateChests(float delta)
    { foreach (var (id, lid) in lids) lid.Rotation = new Vector3(Mathf.LerpAngle(lid.Rotation.X, openedChest == id ? -1.25f : 0, Math.Min(1, delta * 9)), 0, 0); }
}
