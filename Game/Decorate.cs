using Godot;
using Cave.Core;

namespace CozyCave;
public partial class Main
{
    private string? placingKind, movingId;
    private float placementRotation;
    private float placementTurn;
    private Vector3 placementPosition;
    private bool placementValid,placementTabletopFrame;
    private static bool IsBlock(string kind) => BlockNames.ContainsKey(kind) && kind is not ("chest" or "lantern" or "item_frame" or "writable_book");
    private void ShowDecorate() => ShowBuildingInventory();
    private void BeginPlacement(string kind, string? id)
    {
        CancelPlacement(); ClosePanel(); placingKind = kind; movingId = id;
        placementTurn=0;placementTabletopFrame=false;
        placementRotation=PlacementFacing.Rotation(kind,player.RotationDegrees.Y);
        RefreshHand(); Enter();
    }
    private void CancelPlacement()
    {
        placingKind = null; movingId = null;
        RefreshHand();
    }
    private void UpdatePlacement()
    {
        if (placingKind == null || panel != null || !walking) return;
        placementRotation=PlacementFacing.Rotation(placingKind,player.RotationDegrees.Y,placementTurn);
        var query = PhysicsRayQueryParameters3D.Create(camera.GlobalPosition, camera.GlobalPosition - camera.GlobalBasis.Z * 5);
        query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        placementValid = false;
        if (hit.Count > 0)
        {
            var at = hit["position"].AsVector3() + hit["normal"].AsVector3() * .51f;
            placementPosition = new Vector3(Mathf.Round(at.X), Mathf.Floor(at.Y), Mathf.Round(at.Z));
            if(RayItem(hit) is {} supportId && TargetShape(supportId) is {} support)
            {
                var normal=hit["normal"].AsVector3();
                placementPosition=new Vector3(Mathf.Round(support.At.X),Mathf.Floor(support.At.Y),Mathf.Round(support.At.Z))+new Vector3(Mathf.Round(normal.X),Mathf.Round(normal.Y),Mathf.Round(normal.Z));
            }
            var size = placingKind == "chest" ? new Vector3(.88f, .88f, .88f) : Vector3.One * .98f;
            var shape = new PhysicsShapeQueryParameters3D { Shape = new BoxShape3D { Size = size }, Transform = new Transform3D(Basis.Identity, placementPosition + Vector3.Up * (size.Y / 2 + .015f)), CollisionMask = uint.MaxValue };
            placementValid = WithinWorld(placementPosition) && GetWorld3D().DirectSpaceState.IntersectShape(shape, 1).Count == 0;
            if(placingKind=="writable_book") {var surface=hit["position"].AsVector3();placementPosition=new Vector3(Mathf.Round(surface.X),surface.Y+.02f,Mathf.Round(surface.Z));placementValid=WithinWorld(placementPosition) && hit["normal"].AsVector3().Y>.9f;}
            if(placingKind=="item_frame")
            {
                var normal=hit["normal"].AsVector3();placementTabletopFrame=normal.Y>.9f;
                if(placementTabletopFrame)
                {
                    var surface=hit["position"].AsVector3();placementPosition=new Vector3(Mathf.Round(surface.X),surface.Y+.02f,Mathf.Round(surface.Z));
                    placementRotation=PlacementFacing.Rotation("item_frame",player.RotationDegrees.Y,placementTurn+180);
                    var frameShape=new PhysicsShapeQueryParameters3D {Shape=new BoxShape3D {Size=new Vector3(.66f,.64f,.3f)},Transform=new Transform3D(new Basis(Vector3.Up,Mathf.DegToRad(placementRotation)),placementPosition+Vector3.Up*.32f),CollisionMask=1|4};
                    placementValid=WithinWorld(placementPosition) && GetWorld3D().DirectSpaceState.IntersectShape(frameShape,1).Count==0;
                }
                else {placementValid &= Math.Abs(normal.Y)<.1f;placementRotation=Mathf.RadToDeg(Mathf.Atan2(-normal.X,-normal.Z));}
            }
        }

    }
    private void Place(bool refreshRay = false)
    {
        if(refreshRay) UpdatePlacement();
        if (!placementValid || placingKind == null || !WithinWorld(placementPosition)) return;
        string kind = placingKind;
        if(kind!="item_frame") placementRotation=PlacementFacing.Rotation(kind,player.RotationDegrees.Y,placementTurn);
        if (movingId == null && state.Supplies.GetValueOrDefault(kind) <= 0) { Toast("No " + kind + " left in inventory."); CancelPlacement(); return; }
        if (kind == "chest")
        {
            var c = movingId == null ? new Chest(Guid.NewGuid().ToString("N"), "New chest", 0, 0) : state.Chests.Single(c => c.Id == movingId);
            if (movingId == null) state.Chests.Add(c);
            c.X = placementPosition.X; c.Y = placementPosition.Y; c.Z = placementPosition.Z; c.Rotation = placementRotation; c.Carried = false; RemovePlacedObject(c.Id); CreateChest(c);
        }
        else if (IsBlock(kind) && movingId == null) { var b=new BuildingBlock { Kind = kind, Active=kind!="spruce_trapdoor", Rotation=placementRotation, X = (int)placementPosition.X, Y = (int)placementPosition.Y, Z = (int)placementPosition.Z }; state.BuildingBlocks.Add(b); CreateBuildingBlock(b); }
        else
        {
            var d = movingId == null ? new Decoration(kind, 0, 0) : state.Decorations.Single(d => d.Id == movingId);
            if (movingId == null) state.Decorations.Add(d);
            if(kind=="item_frame")d.TabletopFrame=placementTabletopFrame;
            d.X = placementPosition.X; d.Y = placementPosition.Y; d.Z = placementPosition.Z; d.Rotation = placementRotation; d.Carried = false; RemovePlacedObject("$decor:"+d.Id); CreateDecoration(d);
        }
        if (movingId == null) state.Supplies[kind]--;
        bool repeat = movingId == null && state.Supplies.GetValueOrDefault(kind) > 0;
        Changed(); CancelPlacement(); PlayEffect("place"); RefreshHotbar(); SwingHand();
        if (repeat) {float turn=placementTurn;BeginPlacement(kind, null);placementTurn=turn;}
    }
}
