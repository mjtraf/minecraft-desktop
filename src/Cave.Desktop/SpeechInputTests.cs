using System.Text.Json;
using System.Speech.Synthesis;
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
                string wav=Path.Combine(output,"speech.wav");using(var synth=new SpeechSynthesizer()){synth.SetOutputToWaveFile(wav);synth.Speak("Hello. Please write a new document about the weather.");}
                station.StartSpeech(wav);
                for(int i=0;i<100 && !messages.Any(m=>m.Command is "villager-dictation" or "villager-voice-error");i++)await Task.Delay(100);
                Check(messages.Any(m=>m.Command=="villager-dictation"&&m.Text.Length>5),"Recorded speech travels through the actual workstation recognition and review path");
                messages.Clear();station.StartSpeech(Path.Combine(output,"missing.wav"));
                Check(messages.Any(m=>m.Command=="villager-voice-error"),"Input failure produces a visible error instead of silent Ready status");
                messages.Clear();station.StartSpeech(wav);station.CancelSpeech();await Task.Delay(500);
                Check(!messages.Any(m=>m.Command=="villager-dictation"),"Cancelled recording cannot deliver a stale voice request");
                messages.Clear();station.StartSpeech(wav);
                for(int i=0;i<100 && !messages.Any(m=>m.Command is "villager-dictation" or "villager-voice-error");i++)await Task.Delay(100);
                Check(messages.Any(m=>m.Command=="villager-dictation"),"Recording works again after cancellation and input failure");
            }
            catch(Exception e){results.Add("FAIL "+e);}
            File.WriteAllLines(Path.Combine(output,"results.txt"),results);host.Close();
        };Application.Run(host);
    }
}
