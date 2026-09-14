using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
namespace Cave.Desktop;

internal sealed class VillagerSession:IDisposable
{
    private Process? server;
    private readonly ConcurrentDictionary<int,TaskCompletionSource<JsonElement>> requests=[];
    private readonly SemaphoreSlim writeGate=new(1),startGate=new(1);
    private int sequence;
    private bool disposed;
    internal string? ThreadId,TurnId;
    internal bool Busy {get;private set;}
    internal event Action<string,string>? Event;
    internal static string FindCodex()
    {
        foreach(string path in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        {var exe=Path.Combine(path,"codex.exe");if(File.Exists(exe))return exe;}
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        return Directory.Exists(root)?Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()??throw new FileNotFoundException("Install Codex before starting the villager."):throw new FileNotFoundException("Codex was not found.");
    }
    internal async Task Ensure()
    {
        await startGate.WaitAsync();try
        {
            if(server is {HasExited:false})return;
            if(disposed)throw new ObjectDisposedException(nameof(VillagerSession));
            server=new Process {StartInfo=new ProcessStartInfo(FindCodex()) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
            server.StartInfo.ArgumentList.Add("app-server");server.StartInfo.ArgumentList.Add("--stdio");server.Start();
            _=Pump(server);_=DrainErrors(server);
            await Request("initialize",new {clientInfo=new {name="cozy_cave_villager",title="Cozy Cave Villager",version="1.0.0"}});
            await Write(new {method="initialized"});
        }finally{startGate.Release();}
    }
    private static async Task DrainErrors(Process process){try{while(await process.StandardError.ReadLineAsync()!=null){}}catch{} }
    private async Task Pump(Process process)
    {
        try
        {
            while(await process.StandardOutput.ReadLineAsync() is {} line)
            {
                using var doc=JsonDocument.Parse(line);var root=doc.RootElement;
                if(root.TryGetProperty("id",out var id) && !root.TryGetProperty("method",out _) && id.TryGetInt32(out int key) && requests.TryRemove(key,out var waiter))
                {if(root.TryGetProperty("error",out var error))waiter.TrySetException(new InvalidOperationException(error.GetProperty("message").GetString()));else waiter.TrySetResult(root.GetProperty("result").Clone());continue;}
                if(!root.TryGetProperty("method",out var method))continue;
                string name=method.GetString()!;var p=root.TryGetProperty("params",out var payload)?payload:default;
                if(root.TryGetProperty("id",out var requestId))
                {
                    // No silent approval of unrecognized prompts. The user can answer in the workstation.
                    Event?.Invoke("approval",JsonSerializer.Serialize(new {id=requestId.Clone(),method=name,parameters=p.Clone()}));continue;
                }
                if(name=="item/agentMessage/delta")Event?.Invoke("delta",p.GetProperty("delta").GetString()??"");
                else if(name=="turn/started") {TurnId=p.GetProperty("turn").GetProperty("id").GetString();Busy=true;Event?.Invoke("status","Working");}
                else if(name=="turn/completed")
                {
                    var turn=p.GetProperty("turn");Busy=false;TurnId=null;
                    string status=turn.GetProperty("status").GetString()??"completed";
                    if(turn.TryGetProperty("error",out var err) && err.ValueKind==JsonValueKind.Object)Event?.Invoke("error",err.ToString());
                    Event?.Invoke("status",status=="completed"?"Finished":status);
                }
                else if(name=="item/started" && p.TryGetProperty("item",out var item))
                {
                    string type=item.GetProperty("type").GetString()??"";
                    if(type=="commandExecution")Event?.Invoke("activity",item.TryGetProperty("command",out var command)?command.ToString():"Running command");
                    else if(type is "webSearch" or "fileChange" or "mcpToolCall")Event?.Invoke("activity",type);
                }
                else if(name=="item/commandExecution/outputDelta")Event?.Invoke("output",p.GetProperty("delta").GetString()??"");
                else if(name=="account/login/completed")Event?.Invoke("status",p.GetProperty("success").GetBoolean()?"Signed in":"Sign-in failed");
                else if(name=="error")Event?.Invoke("error",p.ToString());
            }
        }
        catch(Exception e){if(!disposed)Event?.Invoke("error",e.Message);}
        finally {Busy=false;foreach(var pair in requests)if(requests.TryRemove(pair.Key,out var waiting))waiting.TrySetException(new IOException("Codex connection closed."));if(!disposed)Event?.Invoke("status","Disconnected — send to reconnect");}
    }
    private async Task Write(object message)
    {
        await writeGate.WaitAsync();try{await server!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));await server.StandardInput.FlushAsync();}finally{writeGate.Release();}
    }
    internal async Task<JsonElement> Request(string method,object? parameters=null)
    {
        int id=Interlocked.Increment(ref sequence);var waiter=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);requests[id]=waiter;
        try{await Write(new {id,method,@params=parameters??new {}});return await waiter.Task.WaitAsync(TimeSpan.FromSeconds(60));}finally{requests.TryRemove(id,out _);}
    }
    internal async Task Login()
    {
        await Ensure();var login=await Request("account/login/start",new {type="chatgpt"});
        if(login.TryGetProperty("authUrl",out var url))Process.Start(new ProcessStartInfo(url.GetString()!){UseShellExecute=true});
    }
    internal async Task Send(string text,string folder)
    {
        if(Busy)throw new InvalidOperationException("Let the current task finish, or press Stop before giving a new instruction.");
        Busy=true;
        try
        {
            await Ensure();
            string instructions="You are the user's villager assistant in Cozy Cave. Work on coding, research and documents as requested. You have the user's authorization for full local desktop access within their task. Ask before unrelated destructive actions or sending messages to others. Use the existing tools and skills. For Windows desktop control, execute the local helper with --agent-desktop followed by a JSON request FILE path. The helper executable is "+Environment.ProcessPath+". Actions: screenshot (writes a PNG at output and returns virtual-screen bounds), click (x,y, button left/right, double boolean), move (x,y), scroll (delta), text (text), key (keys in SendKeys notation). Read the result from the request file path plus .result.json. Coordinates are actual virtual desktop pixels. Read a screenshot before deciding where to click. This controls the user's real desktop. The workstation and cave can lose focus during these actions. Do not change Cozy Cave files unless asked. Explain progress in plain text; do not pretend an operation succeeded.";
            var options=new {cwd=folder,sandbox="danger-full-access",approvalPolicy="on-request",developerInstructions=instructions};
            JsonElement result;
            if(ThreadId==null)result=await Request("thread/start",options);
            else result=await Request("thread/resume",new {threadId=ThreadId,cwd=folder,sandbox="danger-full-access",approvalPolicy="on-request",developerInstructions=instructions});
            ThreadId=result.GetProperty("thread").GetProperty("id").GetString();Event?.Invoke("thread",ThreadId!);
            var started=await Request("turn/start",new {threadId=ThreadId,input=new[]{new {type="text",text}}});
            TurnId=started.GetProperty("turn").GetProperty("id").GetString();
        }catch{Busy=false;throw;}
    }
    internal async Task Stop(){if(ThreadId!=null && TurnId!=null)await Request("turn/interrupt",new {threadId=ThreadId,turnId=TurnId});}
    internal Task Respond(JsonElement id,object result)=>Write(new {id,result});
    public void Dispose(){disposed=true;try{server?.StandardInput.Close();if(server is {HasExited:false})server.Kill(true);}catch{}server?.Dispose();}
}
