namespace Cave.Core;

public static class RoomRemodel
{
    public static bool Apply(CaveState state)
    {
        if(state.RoomRevision>=1)return false;
        // Retain identities, file assignments, carried objects, photos and notes.
        int index=0;
        foreach(var chest in state.Chests.Where(c=>!c.Carried))
        {
            chest.X=4+(index/7)*3;chest.Z=-6+(index%7)*2;chest.Y=0;chest.Rotation=270;index++;
        }
        int photo=0;
        foreach(var d in state.Decorations.Where(d=>!d.Carried))
        {
            if(d.Kind=="item_frame") {d.TabletopFrame=true;d.X=1+(photo%3);d.Z=6+photo/3;d.Y=1.02f;d.Rotation=0;photo++;}
            else if(d.Kind=="writable_book") {d.X=2;d.Z=6;d.Y=1.02f;}
            else if(d.Kind=="moss") {d.X=-6;d.Z=-7;d.Y=0;}
            else if(d.Kind=="lantern") {d.X=3;d.Z=6;d.Y=1.02f;}
            else {d.X=7;d.Z=6;d.Y=0;}
        }
        // Recover indoor construction as usable supplies; preserve outdoor builds.
        foreach(var b in state.BuildingBlocks.Where(b=>Math.Abs(b.X)<=9 && Math.Abs(b.Z)<=9 && b.Y>=0 && b.Y<=10).ToArray())
        {state.Supplies[b.Kind]=state.Supplies.GetValueOrDefault(b.Kind)+1;state.BuildingBlocks.Remove(b);}
        state.RemovedTerrain.RemoveWhere(id=>!id.StartsWith("$valley:"));
        state.Player=[1,0.1f,3];state.Yaw=0.95f;state.Flying=false;
        state.RoomRevision=1;return true;
    }
}
