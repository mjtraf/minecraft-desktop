using System.Text.RegularExpressions;
namespace Cave.Core;

public static class AgentRegistry
{
    public const string LegacyId = "legacy";
    public static bool ValidId(string id) => id == LegacyId || Guid.TryParseExact(id, "N", out _);
    public static void Migrate(CaveState state)
    {
        if(state.AgentsMigrated)return;
        state.AgentsMigrated=true;
        state.Agents.Add(new AgentProfile {Id=LegacyId,Name="Villager",Position=state.VillagerPosition.ToArray()});
        foreach(var block in state.Decorations.Where(d=>d.ScreenRole=="desktop"))block.AgentId=LegacyId;
    }
    public static AgentProfile Attach(CaveState state, Decoration block)
    {
        if(block.AgentId is {} id && state.Agents.FirstOrDefault(a=>a.Id==id) is {} existing)return existing;
        // Only a fresh screen adopts a neighbour. Existing agents never merge or lose history.
        var angle=block.Rotation*MathF.PI/180;float dx=MathF.Cos(angle),dz=-MathF.Sin(angle);
        var neighbours=state.Decorations.Where(d=>d!=block && !d.Carried && d.ScreenRole=="desktop" && d.AgentId!=null && Math.Abs(d.Rotation-block.Rotation)<.01f)
            .Where(d=>{float x=d.X-block.X,y=d.Y-block.Y,z=d.Z-block.Z;return Math.Abs(x*dz-z*dx)<.01f && Math.Abs(Math.Abs(x*dx+z*dz)+Math.Abs(y)-1)<.01f;})
            .Select(d=>d.AgentId).Distinct().ToArray();
        var agent=neighbours.Length==1?state.Agents.FirstOrDefault(a=>a.Id==neighbours[0]):null;
        if(agent==null){int number=1;while(state.Agents.Any(a=>a.Name.Equals("Villager "+number,StringComparison.OrdinalIgnoreCase)))number++;agent=new AgentProfile{Name="Villager "+number};state.Agents.Add(agent);}
        block.AgentId=agent.Id;return agent;
    }
    public static bool CanName(IEnumerable<AgentProfile> agents,string id,string name) =>
        name.Trim().Length is >=2 and <=32 && name.Any(char.IsLetter) && name.All(c=>char.IsLetterOrDigit(c)||c==' '||c=='-'||c==(char)39) &&
        !agents.Any(a=>a.Id!=id && a.Name.Equals(name.Trim(),StringComparison.OrdinalIgnoreCase));
    public static (string? Id,string Prompt) Address(IEnumerable<AgentProfile> agents,string text,string? fallback=null)
    {
        text=text.Trim();
        foreach(var agent in agents.OrderByDescending(a=>a.Name.Length))
        {
            var match=Regex.Match(text,@"^(?:(?:hey|hello|hi|okay|ok)[,\s]+)?"+Regex.Escape(agent.Name)+@"(?=$|[\s,:.!?])[,\s:!?.]*",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            if(match.Success)return(agent.Id,text[match.Length..].Trim());
        }
        return(fallback,text);
    }
}
