using System.Text.Json;
using System.Security.Cryptography;

namespace Cave.Core;

public sealed class StateStore(string directory)
{
    public string DirectoryPath { get; } = directory;
    public string? RecoveryMessage { get; private set; }
    private string StatePath => Path.Combine(DirectoryPath, "state.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private string? loadedHash;
    private bool loaded;
    private DateTime historySaved;
    private static string Fingerprint(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public IDisposable AcquireSession()
    {
        Directory.CreateDirectory(DirectoryPath);
        return new FileStream(Path.Combine(DirectoryPath,"world.session.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
    private void Snapshot(string path)
    {
        if(!File.Exists(path))return;
        try
        {
            var history=Path.Combine(DirectoryPath,"history");Directory.CreateDirectory(history);
            File.Copy(path,Path.Combine(history,$"world-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.json"));historySaved=DateTime.UtcNow;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {RecoveryMessage="Your world loaded, but a history snapshot could not be created: "+e.Message;}
    }
    public CaveState Load()
    {
        if (!File.Exists(StatePath) && !File.Exists(StatePath + ".bak")) {loaded=true;loadedHash=null;return CaveState.Create();}
        foreach (var path in new[] { StatePath, StatePath + ".bak" })
        {
            try
            {
                var bytes=File.ReadAllBytes(path);
                var state = JsonSerializer.Deserialize<CaveState>(bytes) ?? throw new InvalidDataException();
                if (state.Version is not (1 or 2 or 3 or 4 or 5 or 6)) throw new NotSupportedException($"Save version {state.Version} is not supported. Your save has not been changed.");
                if (state.Chests is null || state.Links is null || state.Settings is null || state.Decorations is null || state.SeenDesktop is null
                    || state.BuildingBlocks is null || state.RemovedTerrain is null || state.Supplies is null
                    || !state.Chests.Any(c => c.Id == "inbox") || state.Player is not { Length: 3 }) throw new InvalidDataException();
                if (path.EndsWith(".bak"))
                {
                    RecoveryMessage = "Recovered your organization from the last backup.";
                    if (File.Exists(StatePath)) File.Move(StatePath, StatePath + ".damaged-" + DateTime.UtcNow.Ticks);
                }
                if (state.Supplies.Remove("crate", out var crates)) state.Supplies["barrel"] = state.Supplies.GetValueOrDefault("barrel") + crates;
                if (state.Supplies.Remove("plant", out var plants)) state.Supplies["moss"] = state.Supplies.GetValueOrDefault("moss") + plants;
                foreach (var d in state.Decorations) d.Kind = d.Kind switch { "crate" => "barrel", "plant" => "moss", _ => d.Kind };
                if (state.Drops is null || state.Hotbar is not { Length: 9 }) throw new InvalidDataException();
                state.SelectedSlot = Math.Clamp(state.SelectedSlot, 0, 8);
                if(state.Version < 4) state.Supplies.TryAdd("glass",64);
                if(state.Version < 5) foreach(var kind in new[]{"dark_oak_log","dark_oak_planks","spruce_planks","stone_bricks","cobblestone","dark_oak_stairs","spruce_slab","spruce_trapdoor","spruce_fence","red_carpet","brown_carpet","chain","campfire","stone_brick_stairs"}) state.Supplies.TryAdd(kind,32);
                state.Version = 6;
                loaded=true;loadedHash=path==StatePath?Convert.ToHexString(SHA256.HashData(bytes)):null;
                if(historySaved==default)Snapshot(path);
                return state;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        throw new InvalidDataException("The save and backup could not be read. They have been preserved. Restore a valid state.json before continuing.");
    }
    public void Save(CaveState state)
    {
        Directory.CreateDirectory(DirectoryPath);
        // A stale renderer must never replace a newer world's save.
        string? diskHash=File.Exists(StatePath)?Fingerprint(StatePath):null;
        if(loaded && diskHash!=loadedHash)
        {
            var conflict=Path.Combine(DirectoryPath,$"unsaved-session-{Environment.ProcessId}.json");
            File.WriteAllText(conflict+".tmp",JsonSerializer.Serialize(state,Json));File.Move(conflict+".tmp",conflict,true);
            throw new IOException("Another session changed this world. Your edits were preserved in "+conflict+". Restart to load the latest world.");
        }
        if(DateTime.UtcNow-historySaved>TimeSpan.FromMinutes(5))Snapshot(StatePath);
        var temp = StatePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(stream, state, Json); stream.Flush(true); }
        if (File.Exists(StatePath)) File.Replace(temp, StatePath, StatePath + ".bak");
        else File.Move(temp, StatePath);
        loaded=true;loadedHash=Fingerprint(StatePath);
    }
}
