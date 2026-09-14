using Godot;
namespace CozyCave;
public partial class Main
{
    // The room is cut into a high ledge; the middle falls away into a wooded valley.
    private static int ValleyHeight(int x,int z)
    {
        float distance=-z-11;
        float valley=Math.Max(0,1-Math.Abs(x)/15f)*Math.Min(7,distance*.5f);
        float ridges=Math.Max(0,Math.Abs(x)-12)*.55f;
        return (int)MathF.Floor(-1-valley+ridges+MathF.Sin(x*.35f+z*.2f)*.7f);
    }
    private void BuildCozyInterior()
    {
        Furnish("crafting_table",3,0,-2);
        // Tall double-tier rain windows, heavy lintels, and a low window seat.
        foreach(int x in new[]{-7,3}) for(int y=0;y<9;y++) Furnish("dark_oak_log",x,y,-8);
        for(int x=-7;x<=3;x++) foreach(int y in new[]{4,8}) Furnish("dark_oak_log",x,y,-8);
        for(int x=-5;x<=1;x++) Furnish("spruce_slab",x,0,-8);
        foreach(int x in new[]{-6,2}) {Furnish("barrel",x,0,-8);Furnish("leaves",x,1,-8);}
        // Three-block firebox and a stepped stone chimney rising to the rafters.
        for(int z=-2;z<=2;z++)
        {
            Furnish("stone_bricks",-8,0,z);
            Furnish("stone_brick_stairs",-7,0,z,180);
            if(Math.Abs(z)==2) for(int y=1;y<=3;y++) Furnish("stone_bricks",-8,y,z);
            else {Furnish("campfire",-8,1,z);Furnish("spruce_fence",-7,1,z);}
            Furnish("dark_oak_log",-8,3,z);
            Furnish("spruce_slab",-7,3,z);
        }
        for(int y=4;y<9;y++) for(int z=-1;z<=1;z++) Furnish((y+z)%3==0?"cobblestone":"stone_bricks",-8,y,z);
        foreach(int z in new[]{-2,2}) Furnish("stone_brick_stairs",-8,4,z,180);
        // Floor-to-mantel bookcases and log-framed upper stone panels.
        foreach(int z in new[]{-6,-5,-4,4,5,6})
        {
            for(int y=0;y<3;y++) Furnish("bookshelf",-8,y,z);
            Furnish("spruce_slab",-7,3,z);
            for(int y=4;y<8;y++) Furnish((z+y)%4==0?"cobblestone":"stone_bricks",-8,y,z);
        }
        foreach(int z in new[]{-7,-3,3,7}) for(int y=0;y<9;y++) Furnish("dark_oak_log",-8,y,z);
        for(int z=-7;z<=7;z++) Furnish("dark_oak_log",-8,4,z);
        foreach(int z in new[]{-5,5})
        {
            Furnish("chain",-7,7,z);Furnish("chain",-7,6,z);Lantern(new Vector3(-7,5.45f,z),true);
            Lantern(new Vector3(-7,3.5f,z),true);
        }
        // Large patterned wool rug and two stair-built sofas, all regular blocks.
        for(int x=-6;x<=-1;x++) for(int z=-3;z<=4;z++)
            if(!(z==0 && (x==-4 || x==-3))) Furnish(x==-6 || x==-1 || z==-3 || z==4 || (x+z)%5==0?"brown_carpet":"red_carpet",x,0,z);
        for(int z=-2;z<=3;z++) Furnish("dark_oak_stairs",0,0,z,0);
        for(int x=-5;x<=-2;x++) Furnish("dark_oak_stairs",x,0,5,270);
        foreach(int z in new[]{-3,4}) Furnish("dark_oak_planks",0,0,z);
        foreach(int x in new[]{-6,-1}) Furnish("dark_oak_planks",x,0,5);
        Furnish("spruce_planks",-4,0,0);Furnish("spruce_planks",-3,0,0);
        Lantern(new Vector3(-4,1,0),true);
        // Suspended wooden chandelier and individual hanging lanterns.
        for(int y=6;y<9;y++) Furnish("chain",-3,y,0);
        Furnish("spruce_fence",-3,5,0);
        foreach(var arm in new[]{new Vector2I(-4,0),new Vector2I(-2,0),new Vector2I(-3,-1),new Vector2I(-3,1)})
        {Furnish("spruce_fence",arm.X,5,arm.Y,arm.Y==0?90:0);Lantern(new Vector3(arm.X,4.4375f,arm.Y),true);}
        foreach(int x in new[]{-5,1}) {Furnish("chain",x,7,-7);Furnish("chain",x,6,-7);Lantern(new Vector3(x,5.45f,-7),true);}
        // A quiet storage aisle opposite the hearth; chests retain their identities.
        foreach(int z in new[]{-6,0,6}) Lantern(new Vector3(7,3.5f,z),true);
    }
}
