namespace Cave.Core;

// A display is a rectangular group of adjacent blocks with the same source and facing.
public static class ScreenLayout
{
    public static List<List<Decoration>> Groups(IEnumerable<Decoration> decorations)
    {
        var remaining = decorations.Where(d => !d.Carried && d.ScreenRole is "tv" or "desktop")
            .OrderBy(d => d.Y).ThenBy(d => d.X).ThenBy(d => d.Z).ToList();
        var result = new List<List<Decoration>>();
        while (remaining.Count > 0)
        {
            var first = remaining[0];
            var angle = first.Rotation * MathF.PI / 180;
            int dx = (int)MathF.Round(MathF.Cos(angle)), dz = -(int)MathF.Round(MathF.Sin(angle));
            Decoration? At(int x, int y) => remaining.FirstOrDefault(d => d.ScreenRole == first.ScreenRole && d.AgentId == first.AgentId &&
                Math.Abs(d.Rotation - first.Rotation) < .01f && Math.Abs(d.X - first.X - dx*x) < .01f &&
                Math.Abs(d.Z - first.Z - dz*x) < .01f && Math.Abs(d.Y - first.Y - y) < .01f);
            // Expand in both horizontal directions so either cardinal facing joins correctly.
            int left=0,right=0;while(left>-7 && At(left-1,0)!=null)left--;while(right<7 && At(right+1,0)!=null)right++;
            int height=1;
            while(height<8 && Enumerable.Range(left,right-left+1).All(x=>At(x,height)!=null))height++;
            var group=new List<Decoration>();
            for(int y=0;y<height;y++)for(int x=left;x<=right;x++)if(At(x,y) is {} block)group.Add(block);
            foreach(var block in group)remaining.Remove(block);
            result.Add(group);
        }
        return result;
    }
}
