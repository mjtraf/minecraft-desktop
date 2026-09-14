using System.Diagnostics;
using System.Text.Json;
using System.Speech.Synthesis;
using NAudio.Wave;
namespace Cave.Desktop;
internal static class SpeechInputTests
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);using var host=new Form{Width=1,Height=1,ShowInTaskbar=false,Opacity=0};
        host.Shown+=async(_,_)=>
        {
            var results=new List<string>();void Check(bool ok,string text)=>results.Add((ok?"PASS ":"FAIL ")+text);
            try
            {
                var messages=new List<(string Command,string Text)>();
                using var station=new VillagerWorkstation((command,payload)=>{var json=JsonSerializer.SerializeToElement(payload);messages.Add((command,json.TryGetProperty("text",out var text)?text.GetString()??"":""));},()=>{});
                async Task Wait(){for(int i=0;i<900 && !messages.Any(m=>m.Command is "villager-dictation" or "villager-voice-error");i++)await Task.Delay(100);}
                string wav=Path.Combine(output,"speech.wav");using(var synth=new SpeechSynthesizer()){synth.SetOutputToWaveFile(wav);synth.Speak("Hello. Please write a new document about the weather.");}
                int ticks=0;using var timer=new System.Windows.Forms.Timer{Interval=50};timer.Tick+=(_,_)=>ticks++;timer.Start();
                var watch=Stopwatch.StartNew();station.StartSpeech(wav);await Wait();
                string text=messages.FirstOrDefault(m=>m.Command=="villager-dictation").Text??"";
                Check(new[]{"hello","write","document","weather"}.All(w=>text.Contains(w,StringComparison.OrdinalIgnoreCase)),"Parakeet transcribes the fixture accurately through the workstation review path");
                results.Add($"Cold transcription: {watch.Elapsed.TotalSeconds:F2}s; text: {text}");
                Check(ticks>2,"Model loading and inference keep the UI message loop responsive");
                messages.Clear();station.StartSpeech(Path.Combine(output,"missing.wav"));
                Check(messages.Any(m=>m.Command=="villager-voice-error"),"Input failure produces a visible error instead of silent Ready status");
                messages.Clear();station.StartSpeech(wav);station.CancelSpeech();await Task.Delay(500);
                Check(!messages.Any(m=>m.Command=="villager-dictation"),"Cancelled recording cannot deliver a stale voice request");
                messages.Clear();watch.Restart();station.StartSpeech(wav);station.StartSpeech(wav);await Wait();await Task.Delay(300);
                Check(messages.Count(m=>m.Command=="villager-dictation")==1,"Recording works after cancellation and duplicate starts cannot submit twice");
                results.Add($"Warm transcription and review: {watch.Elapsed.TotalSeconds:F2}s");
                string silence=Path.Combine(output,"silence.wav");using(var writer=new WaveFileWriter(silence,new WaveFormat(16000,16,1)))writer.Write(new byte[32000],0,32000);
                messages.Clear();station.StartSpeech(silence);await Wait();
                Check(messages.Any(m=>m.Command=="villager-voice-error")&&!messages.Any(m=>m.Command=="villager-dictation"),"Silent recordings show recovery instead of an invented task");
                messages.Clear();station.StartSpeech(wav);station.SetSuspended(true);await Task.Delay(500);
                Check(!messages.Any(m=>m.Command=="villager-dictation"),"Session lock cancels pending transcription");
                station.SetSuspended(false);messages.Clear();station.StartSpeech(wav);await Wait();
                Check(messages.Any(m=>m.Command=="villager-dictation"),"Voice works again after unlocking");
                results.Add($"Working set after tests: {Process.GetCurrentProcess().WorkingSet64/1048576} MiB");
                messages.Clear();station.ProjectWorkActive=()=>true;await station.Submit("Keep this voice request",true);
                Check(messages.Any(m=>m.Command=="villager-request-error"),"Rejected automatic voice requests return an actionable error to the game");
            }
            catch(Exception e){results.Add("FAIL "+e);}
            File.WriteAllLines(Path.Combine(output,"results.txt"),results);host.Close();
        };Application.Run(host);
    }
}
