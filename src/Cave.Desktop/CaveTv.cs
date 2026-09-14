using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace Cave.Desktop;

internal sealed class TvPlayerWindow:Form
{
    internal Action? ReturnToCave;
    internal bool ClosingForReal;
    protected override bool ShowWithoutActivation=>true;
    internal TvPlayerWindow()
    {
        Text="Cozy Cave TV — close this window to return to the cave";ShowInTaskbar=false;
        StartPosition=FormStartPosition.Manual;ClientSize=new Size(960,540);Location=new Point(-20000,-20000);
        FormClosing+=(_,e)=>{if(ClosingForReal)return;e.Cancel=true;Location=new Point(-20000,-20000);ReturnToCave?.Invoke();};
    }
}
internal sealed class CaveTv:IDisposable
{
    private readonly TvPlayerWindow form=new();
    private readonly WebView2 browser=new(){Dock=DockStyle.Fill,DefaultBackgroundColor=Color.FromArgb(12,14,17)};
    private readonly System.Windows.Forms.Timer timer=new(){Interval=1000};
    private readonly Cave.Transport.TvFrameBuffer frames=new();
    private readonly Action<string,object?> send;
    private Task? initialization;
    private bool documentReady;
    private bool busy,disposed,power,muted,paused,suspended,browsing;
    private int volume=40;
    private bool streaming,pointerDown;
    private CoreWebView2DevToolsProtocolEventReceiver? frameEvents;
    private readonly SemaphoreSlim streamGate=new(1),pointerGate=new(1);
    private double viewportWidth=960,viewportHeight=540;
    private long controlVersion,requestId;
    private string source="";
    internal string Channel=>frames.Name;
    internal bool Powered=>power;
    internal Form PlayerWindow=>form;
    internal CoreWebView2? Core=>browser.CoreWebView2;
    internal int CapturedFrames {get;private set;}
    internal CaveTv(Action<string,object?> send)
    {
        this.send=send;form.Controls.Add(browser);form.ReturnToCave=()=>{browsing=false;_ = Apply();send("tv-return",null);};
        timer.Tick+=async(_,_)=>await PollStatus();timer.Start();
    }
    private Task Ensure()=>initialization??=Initialize();
    private async Task Initialize()
    {
        Program.Log("TV initialize start");form.Show();
        var options=new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows");
        var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Program.DataPath,"tv-profile"),options);
        Program.Log("TV environment ready");await browser.EnsureCoreWebView2Async(env);
        Program.Log("TV controller ready");
        if(disposed)return;
        browser.CoreWebView2.Settings.AreDevToolsEnabled=false;
        browser.CoreWebView2.Settings.IsStatusBarEnabled=false;
        browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled=true;
        browser.CoreWebView2.NavigationStarting+=(_,_)=>{documentReady=false;_ = SyncStream();};
        browser.CoreWebView2.NavigationCompleted+=async(_,e)=>
        {
            if(!e.IsSuccess){Report("Could not load YouTube. Check your connection or open the player window.");return;}
            documentReady=true;await Apply();
        };
        browser.CoreWebView2.NewWindowRequested+=(_,e)=>{e.Handled=true;if(Uri.TryCreate(e.Uri,UriKind.Absolute,out var u)&&u.Scheme=="https")browser.CoreWebView2.Navigate(e.Uri);};
        browser.CoreWebView2.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
        browser.CoreWebView2.ProcessFailed+=(_,_)=>{power=false;Report("The TV player stopped. Restart Cozy Cave to reconnect.");};
        browser.CoreWebView2.IsMuted=true;
        frameEvents=Core!.GetDevToolsProtocolEventReceiver("Page.screencastFrame");
        frameEvents.DevToolsProtocolEventReceived+=OnFrame;
        await Core.CallDevToolsProtocolMethodAsync("Page.enable","{}");
    }
    private void Report(string text)=>send("tv-status",new{text,power,paused,volume,muted,requestId,url=Cave.Core.YouTubeSource.Normalize(Core?.Source??"")??source});
    internal async Task Handle(JsonElement message)
    {
        try
        {
            requestId=message.TryGetProperty("requestId",out var incoming)?incoming.GetInt64():requestId+1;
            long version=++controlVersion;
            string action=message.GetProperty("action").GetString()??"";
            bool requested=message.GetProperty("power").GetBoolean();
            string url=message.GetProperty("url").GetString()??"";
            string? normalized=Cave.Core.YouTubeSource.Normalize(url);
            if(action=="load" && normalized==null){Report("Paste a YouTube video link (https://youtube.com/watch?v=…).");return;}
            power=requested;muted=message.GetProperty("muted").GetBoolean();volume=Math.Clamp(message.GetProperty("volume").GetInt32(),0,100);
            if(!power)
            {
                paused=false;if(Core!=null)Core.IsMuted=true;
                await Apply();Report("TV off");return;
            }
            await Ensure();if(disposed || !power || version!=controlVersion)return;
            if(action=="load" || (source.Length==0 && normalized!=null))
            {
                if(normalized==null){Report("Choose a YouTube video to start watching.");return;}
                source=normalized;paused=false;Core!.Navigate(source);Report("Loading YouTube…");
            }
            if(action=="browse" && source.Length==0){source="https://www.youtube.com";Core!.Navigate(source);}
            if(action=="power" && source.Length==0){source="https://www.youtube.com";Core!.Navigate(source);}
            if(action=="pause")paused=!paused;
            if(action=="next")await Core!.ExecuteScriptAsync("document.querySelector('.ytp-next-button')?.click()");
            if(action is "back" or "forward")await Core!.ExecuteScriptAsync("(()=>{const v=document.querySelector('video');if(v)v.currentTime=Math.max(0,v.currentTime+"+(action=="back"?"-10":"10")+");})()");
            if(action=="browse")
            {
                browsing=true;var area=Screen.PrimaryScreen!.WorkingArea;form.Location=new Point(area.X+(area.Width-form.Width)/2,area.Y+(area.Height-form.Height)/2);form.Activate();
            }
            await Apply();if(browsing && power && !suspended)Core!.IsMuted=false;
        }
        catch(Exception e){Report("TV unavailable: "+e.Message);Program.Log("TV: "+e);}
    }
    internal void SetSuspended(bool value)
    {
        if(suspended==value)return;suspended=value;if(value && Core!=null)Core.IsMuted=true;_ = Apply();
    }
    private async Task Apply()
    {
        if(disposed || Core==null)return;
        try
        {
            Core.IsMuted=!power || muted || suspended;
            var prefs=JsonSerializer.Serialize(new{playing=power&&!paused&&!suspended,muted,volume=volume/100.0,videoOnly=false});
            await Core.ExecuteScriptAsync("""
                (()=>{
                  const p=PREFS;
                  window.caveTvPreferences=p;
                  if(!window.caveTvApply){
                    window.caveTvApply=()=>{
                      const p=window.caveTvPreferences,v=document.querySelector('video'),player=document.getElementById('movie_player');
                      let style=document.getElementById('cozy-tv-style');
                      if(!style){style=document.createElement('style');style.id='cozy-tv-style';document.head.appendChild(style);}
                      const common='html,body{overflow:hidden!important;background:#0c0e11!important} ';
                      style.textContent=p.videoOnly&&v?common+(player?
                        'body *:not(#movie_player):not(#movie_player *){visibility:hidden!important} #movie_player{visibility:visible!important;position:fixed!important;inset:0!important;width:100vw!important;height:100vh!important;z-index:2147483647!important;background:#0c0e11!important} #movie_player video{position:absolute!important;width:100%!important;height:100%!important;top:0!important;left:0!important;object-fit:contain!important} #movie_player .html5-video-container{width:100%!important;height:100%!important} .ytp-chrome-bottom{width:calc(100% - 24px)!important;left:12px!important;bottom:0!important}':
                        'body *{visibility:hidden!important} video{visibility:visible!important;position:fixed!important;inset:0!important;width:100vw!important;height:100vh!important;object-fit:contain!important;z-index:2147483647!important}') : '';
                      if(v && window.caveTvVideo!==v){window.caveTvVideo=v;v.volume=p.volume;v.muted=p.muted;v.addEventListener('loadedmetadata',()=>setTimeout(()=>{v.volume=window.caveTvPreferences.volume;v.muted=window.caveTvPreferences.muted;},250),{once:true});if(p.playing)v.play().catch(()=>{});else v.pause();}
                    };
                    window.caveTvTimer=setInterval(window.caveTvApply,500);
                  }
                  window.caveTvApply();
                  const v=document.querySelector('video');if(v){v.volume=p.volume;v.muted=p.muted;if(p.playing){if(v.paused)v.play().catch(()=>{});}else v.pause();}
                })()
                """.Replace("PREFS",prefs));
            await SyncStream();
        }
        catch(Exception e) when(e is InvalidOperationException or System.Runtime.InteropServices.COMException){Program.Log("TV controls: "+e.Message);}
    }
    private async Task SyncStream()
    {
        await streamGate.WaitAsync();
        try
        {
            if(disposed || Core==null)return;
            bool wanted=power&&!suspended&&documentReady;
            if(wanted==streaming)return;
            await Core.CallDevToolsProtocolMethodAsync(wanted?"Page.startScreencast":"Page.stopScreencast",wanted?"{\"format\":\"jpeg\",\"quality\":85,\"maxWidth\":960,\"maxHeight\":540,\"everyNthFrame\":1}":"{}");
            streaming=wanted;
        }
        finally{streamGate.Release();}
    }
    private async void OnFrame(object? sender,CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if(disposed)return;
        try
        {
            using var doc=JsonDocument.Parse(e.ParameterObjectAsJson);var p=doc.RootElement;
            if(power&&!suspended){frames.Write(Convert.FromBase64String(p.GetProperty("data").GetString()!));CapturedFrames++;}
            await Core!.CallDevToolsProtocolMethodAsync("Page.screencastFrameAck",JsonSerializer.Serialize(new{sessionId=p.GetProperty("sessionId").GetInt32()}));
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException or FormatException){if(!disposed)Program.Log("TV stream: "+ex.Message);}
    }
    internal async Task Pointer(double u,double v,bool click,string phase="")
    {
        if(disposed || !power || suspended || browsing || !documentReady || Core==null || !double.IsFinite(u) || !double.IsFinite(v))return;
        if((click || phase=="down") && (u<0 || u>1 || v<0 || v>1))return;
        if(click || phase.Length>0)await pointerGate.WaitAsync();else if(!pointerGate.Wait(0))return;
        try
        {
            if(disposed || !power || suspended || browsing)return;
            if(click || phase=="down"){++controlVersion;Core!.IsMuted=false;}
            double x=Math.Clamp(u,-.1,1.1)*viewportWidth,y=Math.Clamp(v,-.1,1.1)*viewportHeight;
            await Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type="mouseMoved",x,y,button=pointerDown?"left":"none",buttons=pointerDown?1:0}));
            if(click || phase=="down")
            {
                pointerDown=true;
                await Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type="mousePressed",x,y,button="left",buttons=1,clickCount=1}));
            }
            if(click || phase=="up")
            {
                pointerDown=false;
                await Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type="mouseReleased",x,y,button="left",buttons=0,clickCount=1}));
                _ = PollStatus();
            }
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.Runtime.InteropServices.COMException){if(!disposed)Program.Log("TV pointer: "+ex.Message);}
        finally{pointerGate.Release();}
    }
    internal async Task Input(JsonElement message)
    {
        if(disposed || !power || suspended || browsing || !documentReady || Core==null)return;
        await pointerGate.WaitAsync();
        try
        {
            if(disposed || !power || suspended || !documentReady || Core==null)return;
            ++controlVersion;
            string kind=message.GetProperty("kind").GetString()??"";
            if(kind=="wheel")
            {
                double u=message.GetProperty("u").GetDouble(),v=message.GetProperty("v").GetDouble();
                if(!double.IsFinite(u)||!double.IsFinite(v)||u<0||u>1||v<0||v>1)return;
                await Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type="mouseWheel",x=u*viewportWidth,y=v*viewportHeight,deltaX=0,deltaY=Math.Clamp(message.GetProperty("delta").GetDouble(),-1200,1200)}));
            }
            else if(kind=="text")
            {
                // Paste only into an editable field, never into browser chrome or the desktop.
                string editable=await Core.ExecuteScriptAsync("!!(document.activeElement?.matches('input,textarea')||document.activeElement?.isContentEditable)");
                if(editable=="true")await Core.CallDevToolsProtocolMethodAsync("Input.insertText",JsonSerializer.Serialize(new{text=message.GetProperty("text").GetString()??""}));
            }
            else if(kind=="key")
            {
                int code=message.GetProperty("code").GetInt32(),modifiers=message.GetProperty("modifiers").GetInt32();
                bool pressed=message.GetProperty("pressed").GetBoolean();
                string text=message.GetProperty("text").GetString()??"";
                if(pressed && code==13)text="\r";
                string key=code switch {8=>"Backspace",9=>"Tab",13=>"Enter",46=>"Delete",37=>"ArrowLeft",38=>"ArrowUp",39=>"ArrowRight",40=>"ArrowDown",36=>"Home",35=>"End",33=>"PageUp",34=>"PageDown",_=>text.Length>0?text:code>=32&&code<=126?((char)code).ToString():""};
                await Core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",JsonSerializer.Serialize(new{type=pressed?(text.Length>0?"keyDown":"rawKeyDown"):"keyUp",key,windowsVirtualKeyCode=code,modifiers,text=pressed?text:""}));
            }
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.Runtime.InteropServices.COMException){if(!disposed)Program.Log("TV input: "+ex.Message);}
        finally{pointerGate.Release();}
    }
    private async Task PollStatus()
    {
        if(disposed || busy || !power || suspended || !documentReady || Core==null)return;busy=true;
        long version=controlVersion;
        try
        {
            string encoded=await Core.ExecuteScriptAsync("JSON.stringify({title:document.title,video:!!document.querySelector('video'),paused:document.querySelector('video')?.paused,volume:document.querySelector('video')?.volume,muted:document.querySelector('video')?.muted,w:innerWidth,h:innerHeight,error:document.querySelector('.ytp-error-content-wrap')?.innerText||''})");
            if(version!=controlVersion)return;
            var inner=JsonSerializer.Deserialize<string>(encoded);if(inner==null)return;
            using var doc=JsonDocument.Parse(inner);var value=doc.RootElement;
            viewportWidth=value.GetProperty("w").GetDouble();viewportHeight=value.GetProperty("h").GetDouble();
            if(value.GetProperty("video").GetBoolean() && power&&!suspended){paused=value.GetProperty("paused").GetBoolean();muted=value.GetProperty("muted").GetBoolean();volume=(int)Math.Round(value.GetProperty("volume").GetDouble()*100);}
            string title=value.GetProperty("title").GetString()??"YouTube",error=value.GetProperty("error").GetString()??"";
            Report(error.Length>0?error:value.GetProperty("video").GetBoolean()?title:"Open player window to choose a video or finish YouTube setup.");
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException){if(!disposed)Program.Log("TV status: "+ex.Message);}
        finally{busy=false;}
    }
    internal async Task LoadFixture(string html)
    {
        await Ensure();source="fixture";power=true;muted=true;Core!.NavigateToString(html);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;if(frameEvents!=null)frameEvents.DevToolsProtocolEventReceived-=OnFrame;timer.Stop();timer.Dispose();
        if(Core!=null)Core.IsMuted=true;form.ClosingForReal=true;browser.Dispose();form.Dispose();frames.Dispose();
    }
}
