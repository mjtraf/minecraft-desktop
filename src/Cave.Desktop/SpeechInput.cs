using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace Cave.Desktop;
internal sealed partial class VillagerWorkstation
{
    private WaveInEvent? microphone;
    private CancellationTokenSource? speechCancellation;
    private Task? speechTask;
    private readonly List<float> microphoneSamples=new();
    private readonly object microphoneGate=new();
    private int micPeak,micLevel;
    private DateTime? micStopped;
    private DateTime micUpdate;
    private void SpeechUi(CancellationTokenSource source,Action action)
    {
        if(closing)return;
        try{BeginInvoke(()=>{if(!closing && speechCancellation==source)action();});}catch(InvalidOperationException){}
    }
    internal void StartSpeech(string? fixture=null)
    {
        if(listening)return;
        if(suspended){send("villager-voice-error",new{text="The microphone is paused while the session is locked."});return;}
        try
        {
            _=ParakeetSpeech.ModelDirectory();
            if(fixture==null)ParakeetSpeech.PrepareInBackground();
            var cancellation=speechCancellation=new CancellationTokenSource();
            lock(microphoneGate){microphoneSamples.Clear();micPeak=micLevel=0;}
            micStopped=null;micUpdate=DateTime.MinValue;listening=true;micStarted=DateTime.UtcNow;
            if(fixture!=null)
            {
                using var reader=new AudioFileReader(fixture);ISampleProvider source=reader;
                if(source.WaveFormat.Channels==2)source=source.ToMono();
                if(source.WaveFormat.Channels!=1)throw new InvalidDataException("Voice audio must be mono or stereo.");
                source=new WdlResamplingSampleProvider(source,16000);var buffer=new float[16000];int count;
                while((count=source.Read(buffer,0,buffer.Length))>0 && microphoneSamples.Count<960000)microphoneSamples.AddRange(buffer.Take(Math.Min(count,960000-microphoneSamples.Count)));
                micPeak=microphoneSamples.Any(s=>Math.Abs(s)>0.0001f)?100:0;
                StopSpeech();return;
            }
            var capture=microphone=new WaveInEvent{DeviceNumber=-1,WaveFormat=new WaveFormat(16000,16,1),BufferMilliseconds=50};
            capture.DataAvailable+=(_,e)=>
            {
                lock(microphoneGate)
                {
                    if(speechCancellation!=cancellation)return;
                    float peak=0;
                    for(int i=0;i+1<e.BytesRecorded && microphoneSamples.Count<960000;i+=2)
                    {float sample=(short)(e.Buffer[i]|e.Buffer[i+1]<<8)/32768f;microphoneSamples.Add(sample);peak=Math.Max(peak,Math.Abs(sample));}
                    micLevel=(int)(peak*100);micPeak=Math.Max(micPeak,micLevel);
                }
            };
            capture.RecordingStopped+=(_,e)=>SpeechUi(cancellation,()=>
            {
                if(e.Exception!=null){FinishSpeech("",e.Exception.Message);return;}
                if(micStopped==null)micStopped=DateTime.UtcNow;
                float[] samples;lock(microphoneGate)samples=microphoneSamples.ToArray();
                microphone?.Dispose();microphone=null;speechTask=RecognizeSpeech(samples,cancellation);
            });
            capture.StartRecording();Report("Listening — release V to send");
        }
        catch(Exception e){FinishSpeech("","Voice input could not start: "+e.Message);}
    }
    internal void StopSpeech()
    {
        if(!listening || micStopped!=null)return;
        micStopped=DateTime.UtcNow;Report("Transcribing locally…");send("villager-voice-progress",new{text="Transcribing locally…"});
        try
        {
            if(microphone!=null)microphone.StopRecording();
            else{float[] samples;lock(microphoneGate)samples=microphoneSamples.ToArray();speechTask=RecognizeSpeech(samples,speechCancellation!);}
        }
        catch(Exception e){FinishSpeech("",e.Message);}
    }
    private async Task RecognizeSpeech(float[] samples,CancellationTokenSource source)
    {
        try
        {
            if(samples.Length<1600 || !samples.Any(s=>Math.Abs(s)>0.0001f)){SpeechUi(source,()=>FinishSpeech("",null));return;}
            source.CancelAfter(TimeSpan.FromSeconds(90));
            string text=await ParakeetSpeech.Transcribe(samples,source.Token);
            SpeechUi(source,()=>FinishSpeech(text,null));
        }
        catch(Exception e)
        {
            SpeechUi(source,()=>FinishSpeech("",source.IsCancellationRequested?"Local transcription took too long. Try a shorter request.":"Local transcription failed: "+e.Message));
        }
    }
    private void FinishSpeech(string text,string? error)
    {
        bool captured=micPeak>0;CancelSpeech();
        if(error==null && text.Length>0){input.Text=text;send("villager-dictation",new{text});Report("Voice captured");return;}
        string reason=error??(captured?"No words were recognized. Hold V, speak, then release it. You can also type your request.":"No microphone audio was detected. Check your default input device, mute switch, and Windows microphone permissions.");
        send("villager-voice-error",new{text=reason});Report("Voice input needs attention");
    }
    internal void CancelSpeech()
    {
        listening=false;micStopped=null;
        CancellationTokenSource? old;lock(microphoneGate){old=speechCancellation;speechCancellation=null;microphoneSamples.Clear();}
        old?.Cancel();
        var capture=microphone;microphone=null;capture?.Dispose();
        var task=speechTask;speechTask=null;
        if(old!=null){if(task==null || task.IsCompleted)old.Dispose();else _=task.ContinueWith(_=>old.Dispose(),TaskScheduler.Default);}
    }
    private void UpdateSpeech()
    {
        if(!listening)return;
        if(micStopped is {} stopped)
        {if(DateTime.UtcNow-stopped>TimeSpan.FromSeconds(95))FinishSpeech("","Local transcription did not finish. Try again.");return;}
        if(DateTime.UtcNow-micStarted>TimeSpan.FromSeconds(60)){StopSpeech();return;}
        if(DateTime.UtcNow-micUpdate<TimeSpan.FromMilliseconds(350))return;
        micUpdate=DateTime.UtcNow;
        send("villager-voice-progress",new{text=micLevel>0?$"Listening · microphone {micLevel}% · Release V to send":"Listening · waiting for microphone audio · Release V to send"});
    }
}
