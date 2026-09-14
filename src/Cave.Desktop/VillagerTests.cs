using System.Diagnostics;
using System.Speech.Recognition;
using System.Text.Json;
namespace Cave.Desktop;
internal static class VillagerTests
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);using var harness=new Form {Width=1,Height=1,ShowInTaskbar=false,Opacity=0};
        harness.Shown+=async(_,_)=>
        {
            var checks=new List<string>();void Check(bool ok,string text){checks.Add((ok?"PASS ":"FAIL ")+text);File.WriteAllLines(Path.Combine(output,"results.txt"),checks);}
            try
            {
                using var station=new VillagerWorkstation((_,_)=>{},()=>{});station.OpenMonitor();await Task.Delay(1200);
                Check(station.CapturedFrames>0,"Live workstation renders native controls into shared frames");
                using(var channel=new Cave.Transport.TvFrameBuffer(station.Channel)){var bytes=channel.Read();Check(bytes is {Length:>1000},"Renderer receives a nonempty live monitor image");if(bytes!=null)File.WriteAllBytes(Path.Combine(output,"monitor.jpg"),bytes);}
                station.ParkMonitor();int before=station.CapturedFrames;await Task.Delay(800);Check(station.CapturedFrames>before,"Monitor keeps updating with native window closed to background");
                station.SetSuspended(true);before=station.CapturedFrames;await Task.Delay(600);Check(station.CapturedFrames==before,"Session suspension stops monitor capture");station.SetSuspended(false);
                Check(SpeechRecognitionEngine.InstalledRecognizers().Count>0,"Windows has an installed speech recognizer");
                var wav=Path.Combine(output,"dictation-fixture.wav");using(var synth=new System.Speech.Synthesis.SpeechSynthesizer()){synth.SetOutputToWaveFile(wav);synth.Speak("Please write a new document about the weather.");}
                using(var recognizer=new SpeechRecognitionEngine()){recognizer.LoadGrammar(new DictationGrammar());recognizer.SetInputToWaveFile(wav);var heard=recognizer.Recognize();Check(heard!=null && heard.Text.Length>5,"Local dictation converts a speech fixture into readable text without using the microphone");}
                using var session=new VillagerSession();string reply="";session.Event+=(kind,text)=>{if(kind=="delta")reply+=text;if(kind=="error")File.AppendAllText(Path.Combine(output,"agent-errors.txt"),text+"\n");};
                await session.Ensure();var account=await session.Request("account/read");Check(account.TryGetProperty("account",out var a)&&a.ValueKind==JsonValueKind.Object,"Codex account is available without exporting credentials");
                await session.Send("Reply exactly: workstation test ready. Do not use any tools or modify any files.",output);
                var watch=Stopwatch.StartNew();while(session.Busy && watch.Elapsed<TimeSpan.FromSeconds(120))await Task.Delay(250);
                Check(!session.Busy && reply.Contains("workstation test ready",StringComparison.OrdinalIgnoreCase),"Real Codex task streams a completed response");
                string? thread=session.ThreadId;session.Dispose();
                using var resumed=new VillagerSession {ThreadId=thread};string again="";resumed.Event+=(kind,text)=>{if(kind=="delta")again+=text;};
                await resumed.Send("Repeat the exact phrase from your previous response. Do not use tools.",output);watch.Restart();while(resumed.Busy && watch.Elapsed<TimeSpan.FromSeconds(120))await Task.Delay(250);
                Check(!resumed.Busy && again.Contains("workstation test ready",StringComparison.OrdinalIgnoreCase),"A new Codex process resumes the villager's conversation");
                await resumed.Send("Create exactly one new file, capability-test.md, in the current project folder, containing the line: Villager document tools work. Do not modify anything else.",output);watch.Restart();while(resumed.Busy && watch.Elapsed<TimeSpan.FromSeconds(120))await Task.Delay(250);
                Check(!resumed.Busy && File.Exists(Path.Combine(output,"capability-test.md")) && File.ReadAllText(Path.Combine(output,"capability-test.md")).Contains("Villager document tools work"),"Real Codex tool execution creates a document in the assigned project");
            }
            catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());}
            finally{harness.Close();}
        };
        Application.Run(harness);
    }
}
