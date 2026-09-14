namespace Cave.Core;

public sealed class CaveState
{
    public bool ScreenBlocksCreated { get; set; }
    public bool ComputerBlockCreated { get; set; }
    public float[] VillagerPosition {get;set;}=[3,0,-.5f];
    public float[] WorkstationPosition {get;set;}=[3,0,2];
    public int RoomRevision {get;set;}
    public int ValleyRadius {get;set;}=48;
    public bool Flying {get;set;}
    public int Version { get; set; } = 7;
    public bool NotebookItemCreated {get;set;}
    public bool HotbarOwnsStacks {get;set;}
    public string?[] HotbarStacks {get;set;}=new string?[9];
    public List<string?> BuildingSlots {get;set;}=[];
    public List<NotePage> Notebook {get;set;}=[new()];
    public bool CatSitting { get; set; }
    public float[] CatPosition { get; set; } = [-3,1,2];
    public List<Chest> Chests { get; set; } = [];
    public List<FileLink> Links { get; set; } = [];
    public List<Decoration> Decorations { get; set; } = [];
    public List<DroppedBlock> Drops { get; set; } = [];
    public string?[] Hotbar { get; set; } = ["stone", "moss", "planks", "timber", "brick", "leaves", "chest", "lantern", "barrel"];
    public int SelectedSlot { get; set; }
    public List<BuildingBlock> BuildingBlocks { get; set; } = [];
    public HashSet<string> RemovedTerrain { get; set; } = [];
    public Dictionary<string, int> Supplies { get; set; } = new()
    { ["dark_oak_log"] = 32, ["dark_oak_planks"] = 32, ["spruce_planks"] = 32, ["stone_bricks"] = 32, ["cobblestone"] = 32, ["dark_oak_stairs"] = 32, ["spruce_slab"] = 32, ["spruce_trapdoor"] = 32, ["spruce_fence"] = 32, ["red_carpet"] = 32, ["brown_carpet"] = 32, ["chain"] = 32, ["campfire"] = 32, ["stone_brick_stairs"] = 32, ["stone"] = 64, ["moss"] = 64, ["planks"] = 64, ["timber"] = 64, ["brick"] = 64, ["leaves"] = 64, ["chest"] = 16, ["lantern"] = 16, ["barrel"] = 16, ["glass"] = 64 };
    public List<string> SeenDesktop { get; set; } = [];
    public Preferences Settings { get; set; } = new();
    public float[] Player { get; set; } = [0, 1, 5];
    public float Yaw { get; set; }
    public static CaveState Create() => new()
    {
        Chests = [new("inbox", "Inbox", -6, -4), new("projects", "Projects", -3, -6),
            new("pictures", "Pictures", 1, -6), new("documents", "Documents", 5, -5),
            new("favorites", "Favorites", -6, 1), new("archive", "Archive", 5, 1)],
        Decorations = [new("moss", -4, -5), new("lantern", 3, -5), new("barrel", -6, 4)]
    };
}
public sealed class Chest
{
    public List<ChestStack?> Items { get; set; } = [];
    public bool Carried { get; set; }
    public float Y { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Chest";
    public float X { get; set; }
    public float Z { get; set; }
    public float Rotation { get; set; }
    public Chest() { }
    public Chest(string id, string name, float x, float z) => (Id, Name, X, Z) = (id, name, x, z);
}
public sealed class ChestStack
{
    public string Kind { get; set; } = "stone";
    public string? ItemId { get; set; }
    public int Count { get; set; } = 1;
}
public sealed class Decoration
{
    public string? ScreenRole { get; set; }
    public bool TabletopFrame {get;set;}
    public string? PicturePath { get; set; }
    public bool Carried { get; set; }
    public float Y { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "plant";
    public float X { get; set; }
    public float Z { get; set; }
    public float Rotation { get; set; }
    public Decoration() { }
    public Decoration(string kind, float x, float z) => (Kind, X, Z) = (kind, x, z);
}
public sealed class FileLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string ChestId { get; set; } = "inbox";
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
}
public sealed class Preferences
{
    public string TvUrl {get;set;}="";
    public int TvVolume {get;set;}=40;
    public bool TvMuted {get;set;}
    public List<string> PinnedApps { get; set; } = [];
    public float Sensitivity { get; set; } = 0.0022f;
    public float UiScale { get; set; } = 1;
    public bool ViewBobbing { get; set; }
    public float EffectsVolume { get; set; } = 0.45f;
    public float MusicVolume { get; set; } = 0.3f;
    public bool MusicEnabled { get; set; }
    public List<string> Playlist { get; set; } = [];
    public string Monitor { get; set; } = "";
}
public record FileEntry(string Path, string Name, bool IsDirectory, bool Available);

public sealed class BuildingBlock
{
    public bool Active { get; set; } = true;
    public float Rotation { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "stone";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public sealed class DroppedBlock
{
    public int Count { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "stone";
    public string? ItemId { get; set; }
    public float[] Position { get; set; } = [0, 1, 0];
}

public sealed class NotePage
{
    public string Title {get;set;}="Quick notes";
    public string Text {get;set;}="";
    public List<NoteTask> Tasks {get;set;}=[];
}
public sealed class NoteTask
{
    public string Text {get;set;}="";
    public bool Done {get;set;}
}
