using System.Text.Json;
namespace Cave.Desktop;
internal static class MemoryRecoveryTests
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool value,string name){checks.Add((value?"PASS ":"FAIL ")+name);}
        try
        {
            string id=Guid.NewGuid().ToString("N"),folder=Path.Combine(Program.DataPath,"agents",id);Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"villager-agent.json");
            var saved=new VillagerMemory{Name="Recovery test",Configured=true,ThreadId=Guid.NewGuid().ToString(),Transcript="You: Keep my conversation.\nVillager: Working on the same task.\n",WasWorking=true};
            string json=JsonSerializer.Serialize(saved);File.WriteAllText(path,json);
            using(var station=new VillagerWorkstation((_,_)=>{},()=>{},id))
                Check(station.RestoredThreadId==saved.ThreadId && station.ConversationText.Contains("Keep my conversation") && station.ConversationText.Contains("interrupted"),"Startup restores the saved task ID, transcript, and interrupted-work message");
            using(var station=new VillagerWorkstation((_,_)=>{},()=>{},id))
                Check(station.RestoredThreadId==saved.ThreadId && station.ConversationText.Contains("Keep my conversation"),"Conversation survives another close and reopen");
            File.WriteAllText(path+".bak",json);File.WriteAllText(path,"{broken");
            using(var station=new VillagerWorkstation((_,_)=>{},()=>{},id))Check(station.RestoredThreadId==saved.ThreadId,"Corrupt primary save recovers its original thread from backup");
            Check(JsonSerializer.Deserialize<VillagerMemory>(File.ReadAllText(path+".bak"))!.ThreadId==saved.ThreadId,"Saving recovered history preserves the good backup");
            string missing=Path.Combine(folder,"missing.json");File.WriteAllText(missing+".bak",json);
            Check(VillagerMemoryStore.Load(missing,new()).Memory.ThreadId==saved.ThreadId,"Missing primary save also recovers from backup");
            File.WriteAllText(path,"null");File.WriteAllText(path+".bak","broken");bool rejected=false;
            try{using var station=new VillagerWorkstation((_,_)=>{},()=>{},id);}catch(IOException){rejected=true;}
            Check(rejected && File.ReadAllText(path)=="null","Unreadable saves cannot silently become blank conversations");
            File.WriteAllText(path,json);using(var station=new VillagerWorkstation((_,_)=>{},()=>{},id))Check(station.RestoredThreadId==saved.ThreadId,"Failed restoration releases the session lock for retry");
        }
        catch(Exception e){checks.Add("FAIL "+e);}
        File.WriteAllLines(Path.Combine(output,"results.txt"),checks);
    }
}
