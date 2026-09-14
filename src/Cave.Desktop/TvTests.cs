using System.Text.Json;
namespace Cave.Desktop;
internal static class TvTests
{
    internal static void Run(string output,bool youtube)
    {
        Directory.CreateDirectory(output);var results=new List<string>();
        void Check(bool ok,string text)=>results.Add((ok?"PASS ":"FAIL ")+text);
        using var host=new Form{ShowInTaskbar=false,Opacity=0,Width=1,Height=1};
        using var tv=new CaveTv((command,payload)=>{if(command=="tv-status")File.AppendAllText(Path.Combine(output,"status.txt"),JsonSerializer.Serialize(payload)+"\n");});
        JsonElement Cmd(string action,bool power=true,int volume=40,bool muted=true)=>JsonSerializer.SerializeToElement(new{action,power,volume,muted,url="https://www.youtube.com/watch?v=aqz-KE-bpKQ"});
        host.Shown+=async(_,_)=>
        {
            try
            {
                if(youtube)await tv.Handle(Cmd("load"));
                else await tv.LoadFixture("""
                  <html><head><title>TV moving video fixture</title><style>canvas{display:none}video{position:fixed;inset:0;width:100vw;height:100vh}#movie_player{width:100vw;height:100vh}</style></head><body style="margin:0;background:#071421">
                  <div id="movie_player"><canvas width="960" height="540" id="c"></canvas><video autoplay muted playsinline onclick="this.paused?this.play():this.pause()"></video><button id="skip" style="position:absolute;right:20px;bottom:100px;width:160px;height:60px;z-index:99" onclick="window.testSkipped=true">Skip ad</button></div>
                  <script>const c=document.querySelector('canvas'),g=c.getContext('2d');let n=0;setInterval(()=>{g.fillStyle='#204567';g.fillRect(0,0,960,540);g.fillStyle='#ffbb66';g.fillRect((n++*12)%850,100,110,300);g.fillStyle='white';g.font='48px sans-serif';g.fillText('LIVE TV '+n,60,70);},16);document.querySelector('video').srcObject=c.captureStream(60);</script></body></html>
                  """);
                await Task.Delay(youtube?18000:2500);
                using var reader=new Cave.Transport.TvFrameBuffer(tv.Channel);
                var first=reader.Read();Check(first is {Length:>1000},"TV produces compressed frames through local shared memory");
                if(first!=null)File.WriteAllBytes(Path.Combine(output,"tv-frame.jpg"),first);
                await Task.Delay(700);var second=reader.Read();Check(second!=null && first!=null && !first.SequenceEqual(second),"Offscreen video continues producing changing frames");
                if(youtube)
                {
                    var probe=await tv.Core!.ExecuteScriptAsync("JSON.stringify({video:!!document.querySelector('video'),time:document.querySelector('video')?.currentTime,ready:document.querySelector('video')?.readyState,error:document.querySelector('.ytp-error-content-wrap')?.innerText||'',title:document.title})");
                    File.WriteAllText(Path.Combine(output,"youtube-probe.json"),probe);
                    using var value=JsonDocument.Parse(JsonSerializer.Deserialize<string>(probe)!);Check(value.RootElement.GetProperty("ready").GetInt32()>=2,"YouTube video has decoded media");
                }
                int rateStart=tv.CapturedFrames;var rateClock=System.Diagnostics.Stopwatch.StartNew();await Task.Delay(5000);results.Add($"Measured TV frames/sec: {(tv.CapturedFrames-rateStart)/rateClock.Elapsed.TotalSeconds:F1}");
                if(!youtube)
                {
                    await tv.Pointer(.5,.5,true);await Task.Delay(750);Check(await tv.Core!.ExecuteScriptAsync("document.querySelector('video').paused")=="true","Direct screen click pauses and stays paused");
                    await tv.Pointer(.5,.5,true);await Task.Delay(750);Check(await tv.Core.ExecuteScriptAsync("document.querySelector('video').paused")=="false","Second direct screen click resumes playback");
                    await tv.Pointer(.9,.76,true);Check(await tv.Core.ExecuteScriptAsync("window.testSkipped===true")=="true","Direct pointer hits visible Skip ad fixture button");
                    await tv.Core.ExecuteScriptAsync("window.testSkipped=false;document.getElementById('skip').disabled=true");await tv.Pointer(.9,.76,true);Check(await tv.Core.ExecuteScriptAsync("window.testSkipped===false")=="true","Disabled Skip ad button cannot be activated");
                }
                if(!youtube)
                {
                    await tv.Core!.ExecuteScriptAsync("document.body.insertAdjacentHTML('beforeend',`<form id='searchForm' style='position:fixed;top:0;left:0;z-index:200' onsubmit='event.preventDefault();window.submitted=true'><input id='search' style='width:400px;height:45px'></form>`)");
                    await tv.Pointer(.15,.04,true);
                    await tv.Input(JsonSerializer.SerializeToElement(new{kind="key",code=67,modifiers=0,text="c",pressed=true}));
                    await tv.Input(JsonSerializer.SerializeToElement(new{kind="text",text="ozy 日本語"}));
                    Check(await tv.Core.ExecuteScriptAsync("document.getElementById('search').value==='cozy 日本語'")=="true","Screen keyboard and Unicode paste reach focused search input");
                    await tv.Input(JsonSerializer.SerializeToElement(new{kind="key",code=8,modifiers=0,text="",pressed=true}));
                    Check(await tv.Core.ExecuteScriptAsync("document.getElementById('search').value==='cozy 日本'")=="true","Backspace edits the focused field");
                    await tv.Input(JsonSerializer.SerializeToElement(new{kind="key",code=13,modifiers=0,text="",pressed=true}));
                    Check(await tv.Core.ExecuteScriptAsync("window.submitted===true")=="true","Enter submits search form");
                    await tv.Core.ExecuteScriptAsync("document.body.style.height='2400px';document.getElementById('searchForm').remove()");
                    await tv.Input(JsonSerializer.SerializeToElement(new{kind="wheel",u=.5,v=.5,delta=400}));await Task.Delay(500);
                    Check(await tv.Core.ExecuteScriptAsync("scrollY>0")=="true","Screen mouse wheel scrolls page");
                    await tv.Core.ExecuteScriptAsync("window.scrollTo(0,0);document.body.style.height='540px';document.body.insertAdjacentHTML('beforeend',`<input id='range' type='range' min='0' max='100' value='0' style='position:fixed;top:0;left:0;width:500px;height:40px;z-index:300'>`)");
                    await Task.Delay(200);
                    await tv.Pointer(.02,.04,false,"down");await tv.Pointer(.45,.04,false);await tv.Pointer(.45,.04,false,"up");
                    await Task.Delay(200);results.Add("Range value: "+await tv.Core.ExecuteScriptAsync("document.getElementById('range').value"));
                    Check(await tv.Core.ExecuteScriptAsync("Number(document.getElementById('range').value)>70")=="true","Press, drag and release controls a slider");
                    await tv.Core.ExecuteScriptAsync("document.getElementById('range').remove()");
                }
                await tv.Handle(Cmd("volume",volume:27));
                Check(await tv.Core!.ExecuteScriptAsync("document.querySelector('video').volume")=="0.27","TV volume updates the video element");
                await tv.Handle(Cmd("volume",muted:false));Check(!tv.Core.IsMuted,"Unmute affects only TV browser");
                await tv.Handle(Cmd("volume",muted:true));Check(tv.Core.IsMuted,"Mute silences the TV browser");
                await tv.Handle(Cmd("pause"));Check(await tv.Core.ExecuteScriptAsync("document.querySelector('video').paused")=="true","Pause control pauses media");
                await tv.Handle(Cmd("pause"));await Task.Delay(300);Check(await tv.Core.ExecuteScriptAsync("document.querySelector('video').paused")=="false","Play control resumes media");
                tv.SetSuspended(true);await Task.Delay(200);Check(tv.Core.IsMuted && await tv.Core.ExecuteScriptAsync("document.querySelector('video').paused")=="true","Session pause silences and pauses TV");tv.SetSuspended(false);
                if(youtube)
                {
                    await tv.Handle(Cmd("browse"));Check(tv.PlayerWindow.Left>=0,"Browse opens the TV player on screen");tv.PlayerWindow.Close();await Task.Delay(500);Check(tv.PlayerWindow.Left<0,"Closing browser returns playback to the cave");
                    var searchProbe=await tv.Core.ExecuteScriptAsync("JSON.stringify((()=>{const e=document.querySelector('input[name=search_query]');if(!e)return null;const r=e.getBoundingClientRect();return {u:(r.x+r.width/2)/innerWidth,v:(r.y+r.height/2)/innerHeight};})())");
                    using(var searchDoc=JsonDocument.Parse(JsonSerializer.Deserialize<string>(searchProbe)!))
                    {
                        var pos=searchDoc.RootElement;
                        Check(pos.ValueKind==JsonValueKind.Object,"Full YouTube page exposes its search box on the TV");
                        if(pos.ValueKind==JsonValueKind.Object)
                        {
                            await tv.Pointer(pos.GetProperty("u").GetDouble(),pos.GetProperty("v").GetDouble(),true);
                            await tv.Input(JsonSerializer.SerializeToElement(new{kind="text",text="cozy rainy cave"}));
                            await tv.Input(JsonSerializer.SerializeToElement(new{kind="key",code=13,modifiers=0,text="",pressed=true}));
                            await Task.Delay(5000);
                            Check(tv.Core.Source.Contains("results?search_query="),"Typing and Enter navigate to actual YouTube search results");
                            int beforeSearchFrames=tv.CapturedFrames;
                            await tv.Input(JsonSerializer.SerializeToElement(new{kind="wheel",u=.5,v=.5,delta=500}));await Task.Delay(750);
                            Check(await tv.Core.ExecuteScriptAsync("scrollY>0")=="true" && tv.CapturedFrames>beforeSearchFrames,"YouTube results scroll and continue streaming on the TV");
                        }
                    }
                    await tv.Handle(JsonSerializer.SerializeToElement(new{action="load",power=true,volume=30,muted=true,url="https://www.youtube.com/watch?v=M7lc1UVf-VE"}));await Task.Delay(12000);
                    Check(tv.Core.Source.Contains("M7lc1UVf-VE") && await tv.Core.ExecuteScriptAsync("document.querySelector('video')?.readyState>=2")=="true","Changing the YouTube link loads a second playable video");
                }
                await tv.Handle(Cmd("power",false));await Task.Delay(200);int count=tv.CapturedFrames;await Task.Delay(350);
                Check(tv.Core.IsMuted && count==tv.CapturedFrames,"Power off stops capture and silences playback");
            }
            catch(Exception e){results.Add("FAIL "+e);}
            finally{File.WriteAllLines(Path.Combine(output,"results.txt"),results);host.Close();}
        };
        Application.Run(host);
    }
}
