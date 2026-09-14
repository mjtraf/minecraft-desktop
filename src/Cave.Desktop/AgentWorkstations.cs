using System.Text.Json;
using Cave.Core;
namespace Cave.Desktop;
internal sealed partial class DesktopContext
{
    private static string AgentId(JsonElement message)=>message.TryGetProperty("agentId",out var id)&&id.ValueKind==JsonValueKind.String?id.GetString()!:AgentRegistry.LegacyId;
    private void SendAgent(string id,string command,object? payload)
    {
        var fields=payload==null?new Dictionary<string,JsonElement>():JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(JsonSerializer.Serialize(payload))!;
        fields["agentId"]=JsonSerializer.SerializeToElement(id);Send(command,fields);
    }
    private void HandleAgent(JsonElement message)
    {
        string id=AgentId(message);if(!AgentRegistry.ValidId(id))return;
        try
        {
            if(!workstations.TryGetValue(id,out var station))
            {
                string name=message.TryGetProperty("name",out var n)?n.GetString()??"Villager":"Villager";
                station=new VillagerWorkstation((command,payload)=>SendAgent(id,command,payload),()=>{SendAgent(id,"villager-return",null);Enter();},id,name,
                    candidate=>!workstations.Any(pair=>pair.Key!=id && pair.Value.AgentName.Equals(candidate,StringComparison.OrdinalIgnoreCase)));
                station.ProjectWorkActive=()=>projectRunner?.Busy==true;station.ProjectStatus+=status=>projectRunner?.Notify(id,status);
                workstations.Add(id,station);
            }
            SendAgent(id,"villager-ready",new{channel=station.Channel});station.ReportProfile();
            switch(message.GetProperty("action").GetString())
            {
                case "open":foreach(var other in workstations.Values.Where(w=>w!=station))other.EndInWorld();station.OpenInWorld();break;
                case "leave":station.EndInWorld();break;
                case "send":_=station.Submit(message.GetProperty("text").GetString()??"");break;
                case "mic-start":foreach(var other in workstations.Values.Where(w=>w!=station))other.CancelSpeech();station.StartSpeech();break;
                case "mic-stop":station.StopSpeech();break;
                case "mic-cancel":station.CancelSpeech();break;
            }
        }
        catch(Exception e){SendAgent(id,"villager-status",new{text="Workstation unavailable: "+e.Message,working=false,listening=false});}
    }
}
