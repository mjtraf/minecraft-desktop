using Godot;
namespace CozyCave;
public partial class Main
{
    private string? sittingOn;
    private Vector3 standingPosition;
    private Node3D? seatedBody;
    private float seatedLookYaw;
    private readonly List<CollisionShape3D> seatedShapes=[];
    private bool SeatClear(Vector3 at,float yaw,string id)
    {
        var basis=new Basis(Vector3.Up,yaw);
        foreach(var part in new[]{(new Vector3(0,.94f,0),new Vector3(.98f,.76f,.26f)),(new Vector3(0,.64f,-.30f),new Vector3(.52f,.24f,.76f)),(new Vector3(0,1.43f,-.35f),new Vector3(.35f,.25f,.35f))})
        {
            var query=new PhysicsShapeQueryParameters3D {Shape=new BoxShape3D {Size=part.Item2},Transform=new Transform3D(basis,at+basis*part.Item1),CollisionMask=1,Exclude=[player.GetRid()]};
            foreach(var hit in GetWorld3D().DirectSpaceState.IntersectShape(query,32))
                if(hit["collider"].AsGodotObject() is not Node node || !node.HasMeta("item") || node.GetMeta("item").AsString()!=id){return false;}
        }
        return true;
    }
    private bool StandingClear(Vector3 at)
    {
        var query=new PhysicsShapeQueryParameters3D {Shape=new CapsuleShape3D {Radius=.28f,Height=1.75f},Transform=new Transform3D(Basis.Identity,at+Vector3.Up*.9f),CollisionMask=1,Exclude=[player.GetRid()]};
        return WithinWorld(at) && GetWorld3D().DirectSpaceState.IntersectShape(query,1).Count==0;
    }
    private bool SitOn(string id)
    {
        if(TargetShape(id) is not {} seat || !seat.Kind.EndsWith("_stairs"))return false;
        if(sittingOn==id){StandUp();return true;}
        float yaw=Mathf.DegToRad(seat.Rotation+90);var basis=new Basis(Vector3.Up,yaw);
        var at=seat.At-Vector3.Up*.5f-basis.Z*.25f;
        if(!SeatClear(at,yaw,id)){Toast("This seat needs room for your body and legs.");return true;}
        if(sittingOn!=null && !StandUp())return true;
        standingPosition=player.Position;sittingOn=id;player.Position=at;player.Rotation=new Vector3(0,yaw,0);seatedLookYaw=0;
        player.Velocity=Vector3.Zero;camera.Position=new Vector3(0,1.43f,-.35f);pitch=0;camera.Rotation=Vector3.Zero;
        BuildSeatedBody();ResetMouseActions();Save();return true;
    }
    private void BuildSeatedBody()
    {
        seatedBody=new Node3D {Name="SeatedPlayerBody"};player.AddChild(seatedBody);var skin=Vanilla("entity/player/steve");
        AtlasBox(seatedBody,new Vector3(0,.94f,0),new Vector3(8,12,4),new Vector2(16,16),skin);
        AtlasBox(seatedBody,new Vector3(-.375f,.94f,0),new Vector3(4,12,4),new Vector2(40,16),skin);
        AtlasBox(seatedBody,new Vector3(.375f,.94f,0),new Vector3(4,12,4),new Vector2(32,48),skin);
        foreach(var (x,uv) in new[]{(-.125f,new Vector2(0,16)),(.125f,new Vector2(16,48))})
        {var leg=new Node3D {Position=new Vector3(x,.64f,-.30f),RotationDegrees=new Vector3(90,0,0)};seatedBody.AddChild(leg);AtlasBox(leg,Vector3.Zero,new Vector3(4,12,4),uv,skin);}
        foreach(var part in new[]{(new Vector3(0,.94f,0),new Vector3(.98f,.76f,.26f)),(new Vector3(0,.64f,-.30f),new Vector3(.52f,.24f,.76f))})
        {var shape=new CollisionShape3D {Shape=new BoxShape3D {Size=part.Item2},Position=part.Item1};player.AddChild(shape);seatedShapes.Add(shape);}
    }
    private bool StandUp()
    {
        if(sittingOn==null)return true;
        var candidates=new List<Vector3>{standingPosition};
        for(int radius=1;radius<=2;radius++)foreach(var dir in new[]{Vector3.Forward,Vector3.Back,Vector3.Left,Vector3.Right})
        {
            var near=player.Position+dir*radius;var ray=PhysicsRayQueryParameters3D.Create(near+Vector3.Up*1.5f,near-Vector3.Up*2);ray.CollisionMask=1;ray.Exclude=[player.GetRid()];
            var hit=GetWorld3D().DirectSpaceState.IntersectRay(ray);if(hit.Count>0 && hit["normal"].AsVector3().Y>.8f)candidates.Add(hit["position"].AsVector3()+Vector3.Up*.02f);
        }
        var clear=candidates.Where(StandingClear).ToArray();if(clear.Length==0){Toast("Make room beside the seat before standing.");return false;}
        sittingOn=null;player.Position=clear[0];player.Rotation=new Vector3(0,player.Rotation.Y+seatedLookYaw,0);seatedLookYaw=0;
        if(seatedBody!=null){seatedBody.QueueFree();seatedBody=null;}foreach(var shape in seatedShapes)shape.QueueFree();seatedShapes.Clear();
        player.Velocity=Vector3.Zero;camera.Position=new Vector3(0,1.62f,0);camera.Rotation=new Vector3(pitch,0,0);
        ResetMouseActions();Save();return true;
    }
    private bool UpdateSitting()
    {
        if(sittingOn==null)return false;
        bool move=walking && panel==null && !tvFocused && new[]{Key.W,Key.A,Key.S,Key.D,Key.Space,Key.Shift}.Any(Input.IsPhysicalKeyPressed);
        if(move || TargetShape(sittingOn)==null){if(StandUp())return false;}
        player.Velocity=Vector3.Zero;camera.Position=new Vector3(0,1.43f,-.35f);return true;
    }
}
