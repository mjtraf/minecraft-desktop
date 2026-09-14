using System.Text.Json;
namespace Cave.Desktop;
internal sealed partial class VillagerWorkstation
{
    private string? activeProjectId;
    private TaskCompletionSource<string>? projectCompletion;
    internal Func<bool>? ProjectWorkActive;
    internal event Action<string>? ProjectStatus;
    internal async Task<string> RunProject(string projectId,string folder,string prompt,string phase,CancellationToken cancel)
    {
        if(Working || projectCompletion!=null)throw new InvalidOperationException(AgentName+" is already working. Stop or finish that task first.");
        cancel.ThrowIfCancellationRequested();activeProjectId=projectId;memory.ProjectThreads??=[];session.ThreadId=memory.ProjectThreads.GetValueOrDefault(projectId);
        var completion=projectCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Append("\nProject assignment: "+prompt+"\n\n" );Report("Working on project");
            await session.Send(prompt,folder,ProjectSchema(phase),phase!="task",true);
            using var registration=cancel.Register(()=>{if(!closing)try{BeginInvoke(()=>_=Stop());}catch(InvalidOperationException){}});
            return await completion.Task;
        }
        finally
        {
            SaveMemory();activeProjectId=null;projectCompletion=null;session.ThreadId=memory.ThreadId;
        }
    }
    internal async Task RedirectProject(string projectId,string text)
    {
        if(activeProjectId!=projectId||!session.Busy||session.ThreadId==null||session.TurnId==null)throw new InvalidOperationException("That assignment has finished. Update the next milestone instead.");
        await session.Request("turn/steer",new{threadId=session.ThreadId,expectedTurnId=session.TurnId,input=new[]{new{type="text",text}}});
        Append("\nYour direction: "+text+"\n");SaveMemory();
    }
    private void CompleteProjectTurn(string payload)
    {
        if(projectCompletion==null)return;using var doc=JsonDocument.Parse(payload);var p=doc.RootElement;
        string status=p.GetProperty("status").GetString()??"failed";
        if(status=="completed")projectCompletion.TrySetResult(p.GetProperty("text").GetString()??"");
        else if(status=="interrupted")projectCompletion.TrySetCanceled();else projectCompletion.TrySetException(new IOException("Agent turn ended: "+status));
    }
    private static JsonElement ProjectSchema(string phase)
    {
        const string result="""{"type":"object","properties":{"summary":{"type":"string"},"outputs":{"type":"array","items":{"type":"string"}}},"required":["summary","outputs"],"additionalProperties":false}""";
        const string plan="""{"type":"object","properties":{"milestone":{"type":"string"},"tasks":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"agentId":{"type":"string"},"title":{"type":"string"},"instructions":{"type":"string"},"dependsOn":{"type":"array","items":{"type":"string"}}},"required":["id","agentId","title","instructions","dependsOn"],"additionalProperties":false}}},"required":["milestone","tasks"],"additionalProperties":false}""";
        return JsonSerializer.Deserialize<JsonElement>(phase=="plan"?plan:result);
    }
}
