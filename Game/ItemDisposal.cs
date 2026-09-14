using Godot;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private bool TossSupply(string source)
    {
        BuildingItems();bool bar=source.StartsWith("$hotbar:");
        if(!bar && !source.StartsWith("$slot:"))return false;
        if(!int.TryParse(source[(bar?8:6)..],out int index) || index<0 || index>=(bar?9:state.BuildingSlots.Count))return false;
        string? token=bar?state.HotbarStacks[index]:state.BuildingSlots[index];if(token==null)return false;
        int split=token.LastIndexOf(':');string key=token[..split];int stack=int.Parse(token[(split+1)..]);
        if(ResolveSupply(key) is not {} item)return false;
        int count=item.Id==null?Math.Min(64,state.Supplies.GetValueOrDefault(item.Kind)-64*stack):1;if(count<=0)return false;
        if(bar)state.HotbarStacks[index]=null;else state.BuildingSlots[index]=null;
        if(item.Id==null)
        {
            state.Supplies[item.Kind]-=count;
            string? Shift(string? t) {if(t==null || !t.StartsWith(key+":"))return t;int n=int.Parse(t[(key.Length+1)..]);return n>stack?key+":"+(n-1):t;}
            for(int i=0;i<9;i++)state.HotbarStacks[i]=Shift(state.HotbarStacks[i]);
            for(int i=0;i<state.BuildingSlots.Count;i++)state.BuildingSlots[i]=Shift(state.BuildingSlots[i]);
        }
        // Throw from the eye, so a nearby wall cannot spawn the item on its other side.
        var at=camera.GlobalPosition;var forward=-camera.GlobalBasis.Z;
        var drop=new DroppedBlock {Kind=item.Kind,ItemId=item.Id,Count=count,Position=[at.X,at.Y,at.Z]};
        state.Drops.Add(drop);SpawnDrop(drop);var node=dropNodes[drop.Id];node.Body.LinearVelocity=forward*4+Vector3.Up;dropNodes[drop.Id]=(node.Body,node.Visual,-.9f);
        UpdateHotbarKeys();CancelPlacement();RefreshHotbar();Changed();Save();return true;
    }
    private static bool IsLavaFloor(int x,int z)=>Math.Abs(x-5)+Math.Abs(z-6)==1;
    private static bool InDisposalLava(Vector3 at)=>Math.Abs(at.X-5)<.5f && Math.Abs(at.Z-6)<.5f && at.Y<.08f && at.Y>-.95f;
    private void BuildDisposalLava()
    {
        TerrainBlock("stone_bricks",new Vector3(5,-1.5f,6));
        TerrainBlock("stone_bricks",new Vector3(6,-.5f,6));
        var material=new ShaderMaterial {Shader=new Shader {Code="""
            shader_type spatial;
            render_mode unshaded;
            uniform sampler2D atlas : source_color, filter_nearest;
            uniform float frames=38.0;
            void fragment(){vec3 c=texture(atlas,vec2(UV.x,(floor(mod(TIME*5.0,frames))+UV.y)/frames)).rgb;ALBEDO=c;}
            """}};
        var texture=Vanilla("block/lava_still");material.SetShaderParameter("atlas",texture);material.SetShaderParameter("frames",texture.GetHeight()/(float)texture.GetWidth());
        world.AddChild(new MeshInstance3D {Name="DisposalLava",Position=new Vector3(5,-.08f,6),Mesh=new PlaneMesh {Size=Vector2.One},MaterialOverride=material});
        world.AddChild(new OmniLight3D {Position=new Vector3(5,.35f,6),LightColor=new Color("ff7b20"),LightEnergy=1.2f,OmniRange=4});
    }
    private void DestroyDrop(DroppedBlock drop)
    {
        if(!state.Drops.Remove(drop))return;
        if(dropNodes.Remove(drop.Id,out var node))node.Body.QueueFree();
        // A chest's file links survive in Inbox. No file service delete is invoked.
        if(drop.ItemId is {} id)
        {
            if(state.Chests.FirstOrDefault(c=>c.Id==id) is {} contents)ChestStorage.ReturnItems(state,contents);
            foreach(var link in state.Links.Where(l=>l.ChestId==id))link.ChestId="inbox";
            if(id=="inbox") {var inbox=state.Chests.FirstOrDefault(c=>c.Id==id);if(inbox!=null)inbox.Carried=false;BuildWorld();}
            else state.Chests.RemoveAll(c=>c.Id==id);
            state.Decorations.RemoveAll(d=>d.Id==id);
        }
        EmitBlockDust("stone",new Vector3(5,.1f,6),true);PlayEffect("place");Changed();Save();RefreshHotbar();
    }
}
