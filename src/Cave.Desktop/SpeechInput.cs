using System.Speech.Recognition;
namespace Cave.Desktop;
internal sealed partial class VillagerWorkstation
{
    private int micPeak,micLevel;
    private DateTime? micStopped;
    private DateTime micUpdate;
    private void SpeechUi(SpeechRecognitionEngine source,Action action)
    {
        if(closing)return;
        try{BeginInvoke(()=>{if(!closing && speech==source)action();});}catch(InvalidOperationException){}
    }
    internal void StartSpeech(string? fixture=null)
    {
        if(listening)return;
        if(suspended){send("villager-voice-error",new{text="The microphone is paused while the session is locked."});return;}
        try
        {
            var recognizer=SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r=>r.Culture.TwoLetterISOLanguageName=="en")??SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault()??throw new InvalidOperationException("No Windows speech recognizer is installed.");
            var engine=speech=new SpeechRecognitionEngine(recognizer);
            engine.LoadGrammar(new DictationGrammar());
            engine.AudioLevelUpdated+=(_,e)=>SpeechUi(engine,()=>{micLevel=e.AudioLevel;micPeak=Math.Max(micPeak,micLevel);});
            // Every recognized phrase is reviewed by the user before it can become a task.
            engine.SpeechRecognized+=(_,e)=>SpeechUi(engine,()=>spoken.Append(e.Result.Text+" "));
            engine.RecognizeCompleted+=(_,e)=>SpeechUi(engine,()=>FinishSpeech(e.Error?.Message));
            spoken.Clear();micPeak=micLevel=0;micStopped=null;micUpdate=DateTime.MinValue;
            if(fixture==null)engine.SetInputToDefaultAudioDevice();else engine.SetInputToWaveFile(fixture);
            listening=true;micStarted=DateTime.UtcNow;engine.RecognizeAsync(RecognizeMode.Multiple);
            Report("Listening — release V to review");
        }
        catch(Exception e){FinishSpeech("Microphone could not start: "+e.Message);}
    }
    internal void StopSpeech()
    {
        if(!listening || micStopped!=null)return;
        micStopped=DateTime.UtcNow;Report("Processing your voice…");
        send("villager-voice-progress",new{text="Processing your voice…"});
        try{speech?.RecognizeAsyncStop();}catch(Exception e){FinishSpeech(e.Message);}
    }
    private void FinishSpeech(string? error)
    {
        string text=spoken.ToString().Trim();bool captured=micPeak>0;
        CancelSpeech();
        if(error==null && text.Length>0)
        {input.Text=text;send("villager-dictation",new{text});Report("Review your voice request");return;}
        string reason=error??(captured?"The microphone picked up sound, but Windows could not recognize the words. Hold V, speak a short sentence, then release it. You can also type your request.":"No microphone audio was detected. Check your default input device, mute switch, and Windows microphone permissions.");
        send("villager-voice-error",new{text=reason});Report("Voice input needs attention");
    }
    internal void CancelSpeech()
    {
        listening=false;micStopped=null;var old=speech;speech=null;
        if(old==null)return;
        try{old.RecognizeAsyncCancel();}catch{}
        old.Dispose();
    }
    private void UpdateSpeech()
    {
        if(!listening)return;
        if(micStopped is {} stopped)
        {if(DateTime.UtcNow-stopped>TimeSpan.FromSeconds(8))FinishSpeech("Speech recognition did not finish. Try again or check your Windows input device.");return;}
        if(DateTime.UtcNow-micStarted>TimeSpan.FromSeconds(60)){StopSpeech();return;}
        if(DateTime.UtcNow-micUpdate<TimeSpan.FromMilliseconds(350))return;
        micUpdate=DateTime.UtcNow;
        send("villager-voice-progress",new{text=micLevel>0?$"Listening · microphone {micLevel}% · Release V to review":"Listening · waiting for microphone audio · Release V to review"});
    }
}
