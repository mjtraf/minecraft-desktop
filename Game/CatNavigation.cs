using Godot;
namespace CozyCave;
public partial class Main
{
    private readonly Queue<Vector3> catRoute=[];
    private Vector3 catRouteGoal;
    private float catStuck;
    private bool CatWalkable(Vector2I cell,out Vector3 foot)
    {
        foot=Vector3.Zero;if(cell.X< -13 || cell.X>10 || Math.Abs(cell.Y)>13) return false;
        var at=new Vector3(cell.X*.5f,0,cell.Y*.5f);
        var hit=GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(at+Vector3.Up*1.5f,at-Vector3.Up*.6f,1));
        if(hit.Count==0) return false;
        foot=hit["position"].AsVector3();
        if(foot.Y>.6f || foot.Y< -.2f || InWater(foot)) return false;
        var shape=new PhysicsShapeQueryParameters3D {Shape=new BoxShape3D {Size=new Vector3(.46f,.62f,.46f)},Transform=new Transform3D(Basis.Identity,foot+Vector3.Up*.36f),CollisionMask=1};
        return GetWorld3D().DirectSpaceState.IntersectShape(shape,1).Count==0;
    }
    private void PlanCatRoute()
    {
        catRoute.Clear();catRouteGoal=catDestination;
        var start=new Vector2I(Mathf.RoundToInt(cat.Position.X*2),Mathf.RoundToInt(cat.Position.Z*2));
        // Recover if a newly placed block encloses the companion. Only use a checked nearby empty cell.
        var occupied=new PhysicsShapeQueryParameters3D {Shape=new BoxShape3D {Size=new Vector3(.34f,.4f,.34f)},Transform=new Transform3D(Basis.Identity,cat.Position+Vector3.Up*.25f),CollisionMask=1};
        if(GetWorld3D().DirectSpaceState.IntersectShape(occupied,1).Count>0)
        {
            var candidates=Enumerable.Range(-3,7).SelectMany(x=>Enumerable.Range(-3,7).Select(z=>start+new Vector2I(x,z))).OrderBy(c=>(new Vector3(c.X*.5f,cat.Position.Y,c.Y*.5f)-cat.Position).LengthSquared());
            foreach(var cell in candidates) if(CatWalkable(cell,out var safe) && safe.DistanceTo(cat.Position)<1.6f) {cat.Position=safe+Vector3.Up*.02f;start=cell;break;}
        }
        var parents=new Dictionary<Vector2I,Vector2I>();var positions=new Dictionary<Vector2I,Vector3>{{start,cat.Position}};
        var queue=new Queue<Vector2I>();queue.Enqueue(start);var best=start;float distance=cat.Position.DistanceSquaredTo(catDestination);
        var checkedCells=new HashSet<Vector2I>{start};
        while(queue.Count>0 && positions.Count<260)
        {
            var current=queue.Dequeue();
            foreach(var offset in new[]{Vector2I.Left,Vector2I.Right,Vector2I.Up,Vector2I.Down})
            {
                var next=current+offset;if(!checkedCells.Add(next) || !CatWalkable(next,out var foot) || Math.Abs(foot.Y-positions[current].Y)>.25f) continue;
                parents[next]=current;positions[next]=foot;queue.Enqueue(next);
                float d=foot.DistanceSquaredTo(catDestination);if(d<distance) {distance=d;best=next;}
            }
            if(distance<.1f) break;
        }
        var route=new Stack<Vector3>();for(var point=best;point!=start;point=parents[point]) route.Push(positions[point]);
        foreach(var point in route) catRoute.Enqueue(point);
        if(catRoute.Count==0) {catDestination=new Vector3(-4+(float)random.NextDouble()*8,0,-3+(float)random.NextDouble()*7);catDecision=4;catRest=.5f;}
    }
}
