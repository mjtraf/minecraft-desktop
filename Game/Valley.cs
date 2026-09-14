using Godot;
namespace CozyCave;
public partial class Main
{
    private const int ValleyBottom=-32, ValleyTop=96, ChunkWidth=16;
    private readonly Dictionary<Vector2I,Node3D> valleyChunks=[];
    private readonly HashSet<Vector3I> valleyRemoved=[];
    private readonly Dictionary<Vector3I,string> valleyTrees=[];
    private Node3D? valleyRoot;
    private Vector2I lastValleyChunk=new(int.MaxValue,int.MaxValue);
    private bool flying;
    private static readonly Vector3I[] ValleyNeighbors=[Vector3I.Right,Vector3I.Left,Vector3I.Up,Vector3I.Down,Vector3I.Back,Vector3I.Forward];
    private int WorldRadius=>Math.Clamp(state.ValleyRadius,32,96);
    private bool WithinWorld(Vector3 at)=>Math.Abs(at.X)<=WorldRadius && Math.Abs(at.Z)<=WorldRadius && at.Y>=ValleyBottom && at.Y<=ValleyTop;
    private static Vector2I ValleyChunk(Vector3 at)=>new(Mathf.FloorToInt(at.X/ChunkWidth),Mathf.FloorToInt(at.Z/ChunkWidth));
    private static string ValleyId(Vector3I cell)=>$"$valley:{cell.X},{cell.Y},{cell.Z}";
    private static bool ParseValley(string id,out Vector3I cell)
    {
        cell=default;if(!id.StartsWith("$valley:"))return false;var parts=id[8..].Split(',');
        if(parts.Length!=3 || !int.TryParse(parts[0],out int x)||!int.TryParse(parts[1],out int y)||!int.TryParse(parts[2],out int z))return false;
        cell=new(x,y,z);return true;
    }
    private static int GroundHeight(int x,int z)
    {
        int rim=Math.Max(Math.Abs(x),Math.Abs(z));
        if(rim<=9)return 10;
        if(rim<=14)return -1;
        return z< -14?Math.Clamp(ValleyHeight(x,z),-12,18):(int)MathF.Floor(1+3*MathF.Sin(x*.09f)*MathF.Sin(z*.08f));
    }
    private string? ValleyKind(Vector3I p)
    {
        if(Math.Abs(p.X)>WorldRadius || Math.Abs(p.Z)>WorldRadius || p.Y<ValleyBottom-1 || p.Y>ValleyTop)return null;
        if(p.Y==ValleyBottom-1)return "bedrock";
        if(valleyRemoved.Contains(p))return null;
        if(Math.Abs(p.X)<=9 && Math.Abs(p.Z)<=9 && p.Y>=-1 && p.Y<10)return null;
        int height=GroundHeight(p.X,p.Z);
        if(p.Y<=height)return p.Y==height?"moss":p.Y>=height-2?"dirt":"stone";
        return valleyTrees.GetValueOrDefault(p);
    }
    private void EnsureValley(Vector3 at,bool immediate=false)
    {
        if(proof)return;
        if(valleyRoot==null)
        {
            valleyRoot=new Node3D {Name="ExpandableValley"};AddChild(valleyRoot);
            foreach(var id in state.RemovedTerrain)if(ParseValley(id,out var cell))valleyRemoved.Add(cell);
            for(int x=-88;x<=88;x+=11)for(int z=-88;z<=88;z+=11)
            {
                if(Math.Max(Math.Abs(x),Math.Abs(z))<17)continue;
                int ground=GroundHeight(x,z);
                for(int y=1;y<=5;y++)valleyTrees[new(x,ground+y,z)]="timber";
                for(int dx=-2;dx<=2;dx++)for(int dz=-2;dz<=2;dz++)for(int y=4;y<=6;y++)
                    if(Math.Abs(dx)+Math.Abs(dz)<4 && (dx!=0||dz!=0||y==6))valleyTrees.TryAdd(new(x+dx,ground+y,z+dz),"leaves");
            }
        }
        var center=ValleyChunk(at);
        if(immediate)for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)BuildValleyChunk(center+new Vector2I(x,z));
        if(center!=lastValleyChunk)
        {
            foreach(var key in valleyChunks.Keys.Where(k=>Math.Abs(k.X-center.X)>5 || Math.Abs(k.Y-center.Y)>5).ToArray())RemoveValleyChunk(key);
            lastValleyChunk=center;
        }
        // Stream a bounded number of sections per frame rather than rebuilding the world.
        int built=0;
        for(int radius=0;radius<=4;radius++)for(int x=-radius;x<=radius;x++)for(int z=-radius;z<=radius;z++)
        {
            var key=center+new Vector2I(x,z);
            if(!valleyChunks.ContainsKey(key)) {BuildValleyChunk(key);if(++built>=2)return;}
        }
    }
    private void RemoveValleyChunk(Vector2I key)
    {
        if(valleyChunks.Remove(key,out var node)){node.GetParent().RemoveChild(node);node.QueueFree();}
    }
    private void BuildValleyChunk(Vector2I key)
    {
        if(valleyChunks.ContainsKey(key) || valleyRoot==null)return;
        var node=new Node3D {Name=$"Section_{key.X}_{key.Y}"};valleyRoot.AddChild(node);valleyChunks[key]=node;
        var visible=new Dictionary<string,List<Transform3D>>();var faces=new List<Vector3>();var cubeFaces=VoxelMesh().GetFaces();
        for(int x=key.X*16;x<key.X*16+16;x++)for(int z=key.Y*16;z<key.Y*16+16;z++)
        {
            if(Math.Abs(x)>WorldRadius||Math.Abs(z)>WorldRadius)continue;
            for(int y=ValleyBottom-1;y<=GroundHeight(x,z)+6;y++)
            {
                var cell=new Vector3I(x,y,z);var kind=ValleyKind(cell);if(kind==null || !ValleyNeighbors.Any(d=>ValleyKind(cell+d)==null))continue;
                var position=new Vector3(x,y+.5f,z);
                if(!visible.TryGetValue(kind,out var transforms))visible[kind]=transforms=[];
                transforms.Add(new Transform3D(Basis.Identity,position));
                // One collision mesh per section instead of thousands of physics bodies.
                foreach(var vertex in cubeFaces)faces.Add(vertex+position);
            }
        }
        foreach(var (kind,transforms) in visible)
        {
            var mm=new MultiMesh {TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=VoxelMesh(),InstanceCount=transforms.Count};
            for(int i=0;i<transforms.Count;i++)mm.SetInstanceTransform(i,transforms[i]);
            node.AddChild(new MultiMeshInstance3D {Multimesh=mm,MaterialOverride=CubeMaterial(kind)});
        }
        if(faces.Count>0)
        {
            var body=new StaticBody3D();body.SetMeta("item","$valley");node.AddChild(body);
            body.AddChild(new CollisionShape3D {Shape=new ConcavePolygonShape3D {Data=faces.ToArray(),BackfaceCollision=true}});
        }
    }
    private string? RayItem(Godot.Collections.Dictionary hit)
    {
        if(hit.Count==0 || hit["collider"].AsGodotObject() is not Node body || !body.HasMeta("item"))return null;
        string id=body.GetMeta("item").AsString();if(id!="$valley")return id;
        var p=hit["position"].AsVector3()-hit["normal"].AsVector3()*.02f;
        return ValleyId(new(Mathf.RoundToInt(p.X),Mathf.FloorToInt(p.Y),Mathf.RoundToInt(p.Z)));
    }
    private void MineValley(Vector3I cell)
    {
        valleyRemoved.Add(cell);state.RemovedTerrain.Add(ValleyId(cell));
        foreach(var key in ValleyNeighbors.Append(Vector3I.Zero).Select(d=>ValleyChunk(cell+d)).Distinct())
            if(valleyChunks.ContainsKey(key)){RemoveValleyChunk(key);BuildValleyChunk(key);}
    }
    private void ExpandValley()
    {
        if(WorldRadius>=96){Toast("The valley is at its maximum size.");return;}
        state.ValleyRadius=WorldRadius+16;Changed();
        if(valleyRoot!=null){RemoveChild(valleyRoot);valleyRoot.QueueFree();valleyRoot=null;}valleyChunks.Clear();valleyTrees.Clear();
        EnsureValley(player.Position,true);ShowSettings();
    }
    private void ToggleFlight()
    {
        if(!StandUp())return;
        flying=!flying;state.Flying=flying;player.Velocity=Vector3.Zero;Changed();
        Toast(flying?"Flight on: Space up | Ctrl down | Shift faster | Home return":"Flight off: Space jump | G fly | Home return");
    }
    private void ReturnHome()
    {
        if(!StandUp())return;
        ClosePanel();CancelPlacement();flying=true;state.Flying=true;EnsureValley(new Vector3(0,2,4),true);
        var home=new Vector3(0,2,4);
        for(int y=2;y<ValleyTop-2;y++)
        {
            home.Y=y;var query=new PhysicsShapeQueryParameters3D {Shape=new BoxShape3D {Size=new Vector3(.6f,1.8f,.6f)},Transform=new Transform3D(Basis.Identity,home+Vector3.Up*.9f),CollisionMask=1};
            if(GetWorld3D().DirectSpaceState.IntersectShape(query,1).Count==0)break;
        }
        player.Position=home;player.Velocity=Vector3.Zero;
        EnsureValley(player.Position,true);Enter();Changed();Toast("Home | G to land | Space up | Ctrl down");
    }
}
