using System.Diagnostics;

namespace Cave.Core;

public interface IFileService
{
    IReadOnlyList<FileEntry> List(string folder);
    bool Exists(string path);
    void Open(string path);
    void Reveal(string path);
}
public sealed class FileService : IFileService
{
    public static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) is var full && full.EndsWith(':') ? full + Path.DirectorySeparatorChar : full;
    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    public IReadOnlyList<FileEntry> List(string folder) => Directory.EnumerateFileSystemEntries(folder)
        .Where(p => !Path.GetFileName(p).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
        .Select(p => new FileEntry(p, Path.GetFileName(p), Directory.Exists(p), true))
        .OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    public void Open(string path)
    {
        if (!Exists(path)) throw new FileNotFoundException("This item has moved or is unavailable. Relink it to continue.");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    public void Reveal(string path)
    {
        if (Directory.Exists(path)) Open(path);
        else if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
        else throw new FileNotFoundException("The original item is unavailable.");
    }
}
public sealed class LinkLibrary(CaveState state)
{
    public const string Inventory = "$inventory";
    public FileLink Add(string path, string chestId)
    {
        ValidateChest(chestId);
        path = FileService.Normalize(path);
        var existing = state.Links.FirstOrDefault(l => l.ChestId == chestId && string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        var link = new FileLink { Path = path, ChestId = chestId };
        state.Links.Add(link);
        return link;
    }
    public void Transfer(string id, string chestId)
    {
        ValidateChest(chestId);
        var link = state.Links.Single(l => l.Id == id);
        var duplicate = state.Links.FirstOrDefault(l => l.Id != id && l.ChestId == chestId && string.Equals(l.Path, link.Path, StringComparison.OrdinalIgnoreCase));
        if (duplicate != null) state.Links.Remove(link); else link.ChestId = chestId;
    }
    public void Remove(string id) => state.Links.RemoveAll(l => l.Id == id);
    public void RemoveChest(string id)
    {
        if (id == "inbox") throw new InvalidOperationException("Inbox is always available.");
        foreach (var link in state.Links.Where(l => l.ChestId == id).ToArray()) Transfer(link.Id, "inbox");
        var chest = state.Chests.Single(c => c.Id == id);
        ChestStorage.ReturnItems(state, chest);
        state.Chests.RemoveAll(c => c.Id == id);
    }
    public bool ScanDesktop(IEnumerable<string> roots)
    {
        bool changed = false;
        var seen = new HashSet<string>(state.SeenDesktop, StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(root))
                {
                    if (Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    var normalized = FileService.Normalize(path);
                    if (seen.Add(normalized))
                    {
                        state.SeenDesktop.Add(normalized);
                        if (!state.Links.Any(l => string.Equals(l.Path, normalized, StringComparison.OrdinalIgnoreCase))) Add(normalized, "inbox");
                        changed = true;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return changed;
    }
    private void ValidateChest(string id)
    {
        if (id != Inventory && !state.Chests.Any(c => c.Id == id)) throw new ArgumentException("Chest does not exist.");
        if (!ChestStorage.AcceptsFiles(state, id)) throw new InvalidOperationException("This is an item chest. Empty it before adding files.");
    }
}
public sealed class FolderWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> watchers = [];
    private int changed;
    public bool ConsumeChanges() => Interlocked.Exchange(ref changed, 0) != 0;
    public void Watch(IEnumerable<string> folders)
    {
        Dispose();
        foreach (var path in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(path)) continue;
                var watcher = new FileSystemWatcher(path) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
                watcher.Changed += OnChange; watcher.Created += OnChange; watcher.Deleted += OnChange;
                watcher.Renamed += (_, _) => Interlocked.Exchange(ref changed, 1);
                watcher.Error += (_, _) => Interlocked.Exchange(ref changed, 1);
                watcher.EnableRaisingEvents = true; watchers.Add(watcher);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
    }
    private void OnChange(object sender, FileSystemEventArgs e) => Interlocked.Exchange(ref changed, 1);
    public void Dispose() { foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear(); }
}
