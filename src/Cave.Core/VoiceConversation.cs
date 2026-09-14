using System.Text.RegularExpressions;
namespace Cave.Core;

// Recipient focus is temporary; agent conversation history remains in its workstation.
public sealed class VoiceConversation
{
    private string? id;
    private DateTime lastActivity;
    public string? Active(IEnumerable<AgentProfile> agents,DateTime now)
    {
        if(now-lastActivity>=TimeSpan.FromMinutes(2) || !agents.Any(a=>a.Id==id))id=null;
        return id;
    }
    public void Select(string agentId,DateTime now){id=agentId;lastActivity=now;}
    public void Touch(DateTime now){if(id!=null)lastActivity=now;}
    public void End()=>id=null;
    public (string? Id,string Prompt) Address(IEnumerable<AgentProfile> agents,string text,string? nearby,DateTime now)=>
        AgentRegistry.Address(agents,text,Active(agents,now)??nearby);
    public static bool IsGoodbye(string text)
    {
        var words=Regex.Replace(text.ToLowerInvariant().Replace("’","'").Replace("'",""),@"[^\p{L}\p{N}]+"," ").Trim();
        return words is "thanks thats all" or "thank you thats all" or "thats all" or "end conversation" or "goodbye";
    }
}
