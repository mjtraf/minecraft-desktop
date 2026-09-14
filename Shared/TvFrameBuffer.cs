using System.IO.MemoryMappedFiles;
namespace Cave.Transport;
// One latest compressed frame, not a queue: a slow reader never delays the helper.
public sealed class TvFrameBuffer:IDisposable
{
    private const int Capacity=4*1024*1024;
    private readonly MemoryMappedFile map;
    private readonly MemoryMappedViewAccessor view;
    private readonly Mutex mutex;
    private long seen;
    public string Name {get;}
    public TvFrameBuffer(string? name=null)
    {
        Name=name??"Local\\CozyCaveTV-"+Guid.NewGuid().ToString("N");
        if(!Name.StartsWith("Local\\CozyCaveTV-",StringComparison.Ordinal))throw new ArgumentException("Invalid TV frame channel");
        map=name==null?MemoryMappedFile.CreateNew(Name,Capacity):MemoryMappedFile.OpenExisting(Name);
        view=map.CreateViewAccessor();mutex=new Mutex(false,Name+"-lock");
    }
    private bool Lock(){try{return mutex.WaitOne(0);}catch(AbandonedMutexException){return true;}}
    public void Write(byte[] bytes)
    {
        if(bytes.Length>Capacity-16 || !Lock())return;
        try {view.WriteArray(16,bytes,0,bytes.Length);view.Write(8,bytes.Length);view.Write(0,view.ReadInt64(0)+1);}
        finally{mutex.ReleaseMutex();}
    }
    public byte[]? Read()
    {
        if(!Lock())return null;
        try {long seq=view.ReadInt64(0);if(seq==seen)return null;int size=view.ReadInt32(8);if(size<=0 || size>Capacity-16)return null;var bytes=new byte[size];view.ReadArray(16,bytes,0,size);seen=seq;return bytes;}
        finally{mutex.ReleaseMutex();}
    }
    public void Dispose(){view.Dispose();map.Dispose();mutex.Dispose();}
}
