using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text;
using System.Text.RegularExpressions;

namespace Cave.Desktop;

// Greedy Parakeet decoding adapted from transcribe-rs (MIT); see docs/VOICE-LICENSES.txt.
// All villagers share one CPU model. Audio stays in memory in this process.
internal sealed class ParakeetSpeech : IDisposable
{
    private static readonly SemaphoreSlim gate=new(1,1);
    private static ParakeetSpeech? shared;
    private static DateTime lastUsed;
    private static readonly System.Threading.Timer idle=new(_=>UnloadIdle(),null,60000,60000);
    private readonly InferenceSession encoder,decoder,preprocessor;
    private readonly string[] vocabulary;
    private readonly int blank;
    internal static string ModelDirectory()
    {
        string local=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CozyCave","models","parakeet-tdt-0.6b-v3-int8");
        string handy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"com.pais.handy","models","parakeet-tdt-0.6b-v3-int8");
        foreach(string path in new[]{local,handy})
            if(new[]{"encoder-model.int8.onnx","decoder_joint-model.int8.onnx","nemo128.onnx","vocab.txt"}.All(f=>File.Exists(Path.Combine(path,f))))return path;
        throw new FileNotFoundException("Parakeet voice model is missing. Download Parakeet V3 in Handy, or run scripts/setup-voice.ps1, then try again.");
    }
    private ParakeetSpeech(string directory)
    {
        using var options=new SessionOptions{IntraOpNumThreads=2,InterOpNumThreads=1,ExecutionMode=ExecutionMode.ORT_SEQUENTIAL};
        options.AddSessionConfigEntry("session.intra_op.allow_spinning","0");options.AddSessionConfigEntry("session.inter_op.allow_spinning","0");
        var loaded=new List<InferenceSession>();
        try
        {
            InferenceSession Load(string file){var s=new InferenceSession(Path.Combine(directory,file),options);loaded.Add(s);return s;}
            encoder=Load("encoder-model.int8.onnx");decoder=Load("decoder_joint-model.int8.onnx");preprocessor=Load("nemo128.onnx");
            var words=File.ReadAllLines(Path.Combine(directory,"vocab.txt")).Select(line=>{int split=line.LastIndexOf(' ');return(Token:line[..split].Replace('▁',' '),Id:int.Parse(line[(split+1)..]));}).ToArray();
            vocabulary=new string[words.Max(w=>w.Id)+1];foreach(var word in words)vocabulary[word.Id]=word.Token;
            blank=Array.IndexOf(vocabulary,"<blk>");if(blank<0)throw new InvalidDataException("Parakeet vocabulary has no blank token.");
        }
        catch{foreach(var session in loaded)session.Dispose();throw;}
    }
    internal static async Task<string> Transcribe(float[] samples,CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            return await Task.Run(()=>
            {
                cancellation.ThrowIfCancellationRequested();shared??=new ParakeetSpeech(ModelDirectory());
                cancellation.ThrowIfCancellationRequested();return shared.Decode(samples,cancellation);
            },cancellation).ConfigureAwait(false);
        }
        finally{lastUsed=DateTime.UtcNow;gate.Release();}
    }
    private static void UnloadIdle()
    {
        if(!gate.Wait(0))return;
        try{if(shared!=null && DateTime.UtcNow-lastUsed>TimeSpan.FromMinutes(5)){shared.Dispose();shared=null;}}
        finally{gate.Release();}
    }
    private static NamedOnnxValue Value<T>(string name,T[] data,params int[] shape)=>NamedOnnxValue.CreateFromTensor(name,new DenseTensor<T>(data,shape));
    private static Tensor<T> Tensor<T>(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output,string name)=>output.First(o=>o.Name==name).AsTensor<T>();
    private string Decode(float[] samples,CancellationToken cancellation)
    {
        // Handy's engine pads the start by 250 ms to protect the first word.
        var padded=new float[samples.Length+4000];samples.CopyTo(padded,4000);
        using var run=new RunOptions();using var registration=cancellation.Register(()=>run.Terminate=true);
        using var features=preprocessor.Run(new[]{Value("waveforms",padded,1,padded.Length),Value("waveforms_lens",new[]{(long)padded.Length},1)},preprocessor.OutputMetadata.Keys.ToArray(),run);
        using var encoded=encoder.Run(new[]{NamedOnnxValue.CreateFromTensor("audio_signal",Tensor<float>(features,"features")),NamedOnnxValue.CreateFromTensor("length",Tensor<long>(features,"features_lens"))},encoder.OutputMetadata.Keys.ToArray(),run);
        var encoding=Tensor<float>(encoded,"outputs");int channels=encoding.Dimensions[1],frames=encoding.Dimensions[2];
        int length=checked((int)Tensor<long>(encoded,"encoded_lengths").ToArray()[0]);
        int[] shape1=decoder.InputMetadata["input_states_1"].Dimensions.Select(d=>d<1?1:d).ToArray();
        int[] shape2=decoder.InputMetadata["input_states_2"].Dimensions.Select(d=>d<1?1:d).ToArray();
        float[] state1=new float[shape1.Aggregate(1,(a,b)=>a*b)],state2=new float[shape2.Aggregate(1,(a,b)=>a*b)];
        int previous=blank,t=0,emitted=0;var text=new StringBuilder();var step=new float[channels];var flat=encoding.ToArray();
        while(t<Math.Min(length,frames))
        {
            cancellation.ThrowIfCancellationRequested();for(int c=0;c<channels;c++)step[c]=flat[c*frames+t];
            using var decoded=decoder.Run(new[]{Value("encoder_outputs",step,1,channels,1),Value("targets",new[]{previous},1,1),Value("target_length",new[]{1},1),Value("input_states_1",state1,shape1),Value("input_states_2",state2,shape2)},decoder.OutputMetadata.Keys.ToArray(),run);
            var logits=Tensor<float>(decoded,"outputs").ToArray();int token=0;
            for(int i=1;i<vocabulary.Length;i++)if(logits[i]>=logits[token])token=i;
            if(token!=blank)
            {
                previous=token;state1=Tensor<float>(decoded,"output_states_1").ToArray();state2=Tensor<float>(decoded,"output_states_2").ToArray();
                string word=vocabulary[token];if(!word.StartsWith('<'))text.Append(word);emitted++;
            }
            if(token==blank || emitted==10){t++;emitted=0;}
        }
        return Regex.Replace(text.ToString(),@"\A\s|\s\B|(\s)\b",m=>m.Groups[1].Success?" ":"").Trim();
    }
    public void Dispose(){encoder.Dispose();decoder.Dispose();preprocessor.Dispose();}
}
