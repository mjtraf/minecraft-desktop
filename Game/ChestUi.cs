using Godot;
using Cave.Core;

namespace CozyCave;
public partial class Main
{
    private readonly WindowsShellImages shellImages = new();
    private (FileEntry Entry, string? Link, string Container)? heldItem;
    private ChestSlot? heldCursor;
    private int carryingPage, chestLoadRequest;
    private string chestQuery="";
    private readonly Stack<(string? Folder,int Page)> chestBack=new(), chestForward=new();
    private int shellImagesLoaded;
    private readonly HashSet<string> thumbnailRequested = new(StringComparer.OrdinalIgnoreCase);

    private void ShowChestPage(string id, string? folder, int page, bool recordHistory=true)
    {
        if(id!=LinkLibrary.Inventory && state.Chests.FirstOrDefault(c=>c.Id==id) is {} typed && ChestStorage.HasItems(typed)){ShowItemChest(id);return;}
        backgroundApp=false; bool newChest = openedChest != id;
        if(newChest) {chestBack.Clear();chestForward.Clear();chestQuery="";}
        else if(folder!=folderPath) {if(recordHistory) {chestBack.Push((folderPath,pageIndex));chestForward.Clear();} chestQuery="";}
        ClosePanel(false); walking = false; Input.MouseMode = Input.MouseModeEnum.Visible;
        if (bridge.Connected) bridge.Send(new { command = "enter" });
        openedChest = id; folderPath = folder; pageIndex = Math.Max(0, page);
        if (newChest) PlayEffect("open");
        watcher.Watch(folder == null ? desktopRoots.Concat(state.Links.Where(l => l.ChestId == id).Select(l => System.IO.Path.GetDirectoryName(l.Path) ?? "")) : desktopRoots.Append(folder));
        string name = id == LinkLibrary.Inventory ? "Inventory" : state.Chests.FirstOrDefault(c => c.Id == id)?.Name ?? "Chest";
        var root = new Control { Theme = uiTheme }; layer.AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); panel = root;
        var shade = new ColorRect { Color = new Color(0, 0, 0, .58f), MouseFilter = Control.MouseFilterEnum.Stop }; root.AddChild(shade); shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer(); root.AddChild(center); center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 10); center.AddChild(stack);
        float s = Mathf.Clamp(Mathf.Floor((GetViewport().GetVisibleRect().Size.Y - 220) / 166), 2, 4);
        var navigation=new HBoxContainer();stack.AddChild(navigation);
        void History(bool forward)
        {
            var from=forward?chestForward:chestBack;var to=forward?chestBack:chestForward;
            if(from.Count==0) return;to.Push((folderPath,pageIndex));var target=from.Pop();ShowChestPage(id,target.Folder,target.Page,false);
        }
        var back=Button("←",()=>History(false));back.Name="FolderBack";back.Disabled=chestBack.Count==0;back.TooltipText="Back";navigation.AddChild(back);
        var ahead=Button("→",()=>History(true));ahead.Name="FolderForward";ahead.Disabled=chestForward.Count==0;ahead.TooltipText="Forward";navigation.AddChild(ahead);
        var up=Button("↑",()=>{var parent=folder==null?null:System.IO.Directory.GetParent(folder);ShowChestPage(id,parent?.FullName,0);});up.Disabled=folder==null;up.TooltipText="Parent folder";navigation.AddChild(up);
        navigation.AddChild(Button("Chest",()=>ShowChestPage(id,null,0)));
        var address=new LineEdit {Name="FolderAddress",Text=folder??"",PlaceholderText="Paste a folder or file path…",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(250,0)};navigation.AddChild(address);
        address.TextSubmitted+=value=>
        {
            try
            {
                string path=System.Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                if(System.IO.Directory.Exists(path)) ShowChestPage(id,System.IO.Path.GetFullPath(path),0);
                else if(System.IO.File.Exists(path))
                {
                    ShowChestPage(id,System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)),0);
                    var fileSearch=Descendants(panel!).OfType<LineEdit>().Single(e=>e.Name=="ChestSearch");fileSearch.Text=System.IO.Path.GetFileName(path);fileSearch.EmitSignal(LineEdit.SignalName.TextChanged,fileSearch.Text);
                }
                else Toast("That file or folder is unavailable.");
            } catch(Exception e) {Toast(e.Message);}
        };
        navigation.AddChild(Button("Browse…",()=>PickFolder(path=>ShowChestPage(id,path,0))));
        var searchRow=new HBoxContainer();stack.AddChild(searchRow);
        var search=new LineEdit {Name="ChestSearch",Text=chestQuery,PlaceholderText=folder==null?"Search this chest (all pages)…":"Search this folder…",ClearButtonEnabled=true,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};searchRow.AddChild(search);
        searchRow.AddChild(Button("All chests",ShowSearch));
        if(folder!=null) searchRow.AddChild(Button("Link this folder",()=>{library.Add(folder,id);Changed();Toast("Folder linked to "+name);}));
        var frame = new ChestFrame { PixelScale = s, CustomMinimumSize = new Vector2(176 * s, 166 * s) }; stack.AddChild(frame);
        var pixelFont = GD.Load<FontFile>("res://Assets/Vanilla/font/minecraft.fnt");
        Label Caption(string text, float x, float y)
        {
            var l = new Label { Text = text, Position = new Vector2(x * s, y * s), MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", pixelFont); l.AddThemeFontSizeOverride("font_size", (int)(8 * s)); l.AddThemeColorOverride("font_color", new Color("404040")); frame.AddChild(l); return l;
        }
        if (folder == null && id != LinkLibrary.Inventory)
        {
            var title = new LineEdit { Name = "ChestName", Text = name, MaxLength = 24, Position = new Vector2(8 * s, 3 * s), Size = new Vector2(88 * s, 12 * s), TooltipText = "Click to rename this chest · Enter to save" };
            title.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            title.AddThemeStyleboxOverride("focus", new StyleBoxFlat { BgColor = new Color("d6d6d6") });
            title.AddThemeFontOverride("font", pixelFont); title.AddThemeFontSizeOverride("font_size", (int)(8 * s));
            title.AddThemeColorOverride("font_color", new Color("404040"));
            void CommitName() { RenameChest(id, title.Text); title.Text = state.Chests.Single(c => c.Id == id).Name; }
            title.TextSubmitted += _ => { CommitName(); title.ReleaseFocus(); };
            title.FocusExited += CommitName;
            frame.AddChild(title);
        }
        else Caption(folder == null ? "Carried files" : Short(System.IO.Path.GetFileName(folder), 12), 8, 6);
        Caption("Carried files", 8, 73);
        var topSlots = new List<ChestSlot>(); var bottomSlots = new List<ChestSlot>();
        for (int i = 0; i < 27; i++) topSlots.Add(AddChestSlot(frame, new Vector2(8 + i % 9 * 18, 18 + i / 9 * 18) * s, s, id));
        for (int i = 0; i < 36; i++) bottomSlots.Add(AddChestSlot(frame, new Vector2(8 + i % 9 * 18, i < 27 ? 84 + i / 9 * 18 : 142) * s, s, LinkLibrary.Inventory));
        var footer = new HBoxContainer(); stack.AddChild(footer);
        if(id!=LinkLibrary.Inventory && ChestStorage.AcceptsItems(state,id))footer.AddChild(Button("Store items",()=>ShowItemChest(id)));
        footer.AddChild(Button("+ Files", () => PickFiles(paths => { foreach (var path in paths) library.Add(path, id); Changed(); ShowChestPage(id, folder, pageIndex); })));
        footer.AddChild(Button("+ Folder", () => PickFolder(path => { library.Add(path, id); Changed(); ShowChestPage(id, folder, pageIndex); })));
        footer.AddChild(Button(id == LinkLibrary.Inventory ? "Blocks" : "Carried files", () => { if (id == LinkLibrary.Inventory) ShowBuildingInventory(); else ShowChest(LinkLibrary.Inventory); }));
        footer.AddChild(Button("Blocks", ShowBuildingInventory));
        var framePhoto=new PictureDropButton {Name="FrameImageDrop",Text="Frame image",TooltipText="Drag a photo here to make a frame, or click to choose an image.",FrameImage=FrameImage};framePhoto.Pressed+=PickFrameImage;footer.AddChild(framePhoto);
        footer.AddChild(Button("Options", () => { if (id == LinkLibrary.Inventory) ShowSettings(); else ShowChestOptions(id); }));
        Button PageArrow(string text,float x,string nodeName,Action action)
        {
            var button=Button(text,action);button.Name=nodeName;button.Position=new Vector2(x*s,3*s);button.CustomMinimumSize=Vector2.Zero;button.Size=new Vector2(15*s,12*s);
            button.AddThemeFontOverride("font",pixelFont);button.AddThemeFontSizeOverride("font_size",(int)(8*s));
            foreach(var style in new[]{"normal","hover","pressed","disabled"}) button.AddThemeStyleboxOverride(style,new StyleBoxFlat {BgColor=new Color(style=="hover"?"e5e5e5":"a8a8a8")});
            button.AddThemeColorOverride("font_color",new Color("303030"));button.AddThemeColorOverride("font_disabled_color",new Color("888888"));frame.AddChild(button);return button;
        }
        var previous=PageArrow("<",99,"ChestPreviousPage",()=>ShowChestPage(id,folder,pageIndex-1));previous.TooltipText="Previous page";
        var pageIndicator=Caption("…",116,5);pageIndicator.Name="ChestPageNumber";pageIndicator.Size=new Vector2(32*s,10*s);pageIndicator.HorizontalAlignment=HorizontalAlignment.Center;pageIndicator.ClipText=true;
        var count = new Label { Text = "Loading…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Center }; footer.AddChild(count);
        var next=PageArrow(">",153,"ChestNextPage",()=>ShowChestPage(id,folder,pageIndex+1));next.TooltipText="Next page";
        foreach (var action in footer.GetChildren().OfType<Button>())
        {
            action.AddThemeStyleboxOverride("normal", Style("737373", "333333"));
            action.AddThemeStyleboxOverride("hover", Style("85859c", "dedede"));
            action.AddThemeStyleboxOverride("pressed", Style("545454", "262626"));
            action.AddThemeFontOverride("font", pixelFont); action.AddThemeFontSizeOverride("font_size", 12);
        }
        selectionLabel = new Label { Text = "Drag between chest and Carried files · Shift-click to transfer · Double-click to open", HorizontalAlignment = HorizontalAlignment.Center };
        selectionLabel.AddThemeFontSizeOverride("font_size", 12); stack.AddChild(selectionLabel);
        int generation = viewGeneration;
        _ = FillChestSlots(topSlots, id, folder, generation, count, previous, next, pageIndicator);
        search.TextChanged+=query=>{chestQuery=query;pageIndex=0;ClearHeld();_ = FillChestSlots(topSlots,id,folder,generation,count,previous,next,pageIndicator);};
        var carried = state.Links.Where(l => l.ChestId == LinkLibrary.Inventory).ToArray();
        int carryingPages = Math.Max(1, (carried.Length + 35) / 36); carryingPage = Math.Clamp(carryingPage, 0, carryingPages - 1);
        for (int i = 0; i < 36; i++)
        {
            int index = carryingPage * 36 + i;
            if (index < carried.Length)
            {
                var link = carried[index]; BindSlot(bottomSlots[i], new FileEntry(link.Path, link.Name, System.IO.Directory.Exists(link.Path), files.Exists(link.Path)), link.Id, generation);
            }
        }
        if (carryingPages > 1)
        {
            var pager = Button($"Inventory {carryingPage + 1}/{carryingPages} ›", () => { carryingPage = (carryingPage + 1) % carryingPages; ShowChestPage(id, folder, pageIndex); });
            pager.Position = new Vector2(95 * s, 71 * s); pager.AddThemeFontSizeOverride("font_size", 12); frame.AddChild(pager);
        }
        statusLabel.Visible = false;
        hudToolbar.Visible = false;
        SetHeldCursor();
    }
    private ChestSlot AddChestSlot(Control frame, Vector2 at, float scale, string container)
    {
        var slot = new ChestSlot { Position = at, Size = Vector2.One * (18 * scale), PixelScale = scale, Container = container };
        frame.AddChild(slot);
        slot.Activated = (button, doubleClick, shift) =>
        {
            if (button == MouseButton.Right && slot.Entry != null) { SelectSlot(slot); ShowFileActions(slot); return; }
            if (button != MouseButton.Left) return;
            if (doubleClick && slot.Entry != null) { ClearHeld(); SelectSlot(slot); OpenSelected(); return; }
            if (shift && slot.Entry != null)
            {
                if (slot.Container == LinkLibrary.Inventory && openedChest != LinkLibrary.Inventory) TransferSlot(slot.Entry, slot.LinkId, openedChest!);
                else if (slot.Container == LinkLibrary.Inventory) ChooseDestination(slot.Entry.Path, slot.LinkId);
                else TransferSlot(slot.Entry, slot.LinkId, LinkLibrary.Inventory);
                return;
            }
            if (heldItem is { } held)
            {
                if (held.Container != slot.Container) TransferSlot(held.Entry, held.Link, slot.Container);
                else ClearHeld();
                return;
            }
            if (slot.Entry != null) { SelectSlot(slot); heldItem = (slot.Entry, slot.LinkId, slot.Container); SetHeldCursor(); }
        };
        slot.Transferred = data => TransferSlot(new FileEntry(data["path"].AsString(), System.IO.Path.GetFileName(data["path"].AsString()), System.IO.Directory.Exists(data["path"].AsString()), files.Exists(data["path"].AsString())), data["link"].AsString() is { Length: > 0 } link ? link : null, container);
        return slot;
    }
    private void SelectSlot(ChestSlot slot)
    {
        selectedEntry = slot.Entry; selectedLink = slot.LinkId;
        if (selectionLabel != null && slot.Entry != null) selectionLabel.Text = slot.Entry.Name;
    }
    private void TransferSlot(FileEntry entry, string? link, string destination)
    {
        if (link != null) library.Transfer(link, destination); else library.Add(entry.Path, destination);
        ClearHeld(); Changed(); ShowChestPage(openedChest!, folderPath, pageIndex);
    }
    private void ShowFileActions(ChestSlot slot)
    {
        ClearHeld(); var menu = new PopupMenu(); layer.AddChild(menu);
        foreach (var text in new[] { "Open", "Show in Explorer", slot.Container == LinkLibrary.Inventory ? "Store in chest…" : "Carry", "Link to chest…", "Remove link", "Relink…" }) menu.AddItem(text);
        if(slot.Entry is {IsDirectory:false} && IsPicture(slot.Entry.Path))menu.AddItem("Frame image",6);
        menu.IdPressed += option =>
        {
            try
            {
                switch (option)
                {
                    case 0: OpenSelected(); break;
                    case 1: files.Reveal(slot.Entry!.Path); break;
                    case 2: if (slot.Container == LinkLibrary.Inventory) ChooseDestination(slot.Entry!.Path, slot.LinkId); else TransferSlot(slot.Entry!, slot.LinkId, LinkLibrary.Inventory); break;
                    case 3: ChooseDestination(slot.Entry!.Path, null); break;
                    case 4: if (slot.LinkId != null) { library.Remove(slot.LinkId); Changed(); ShowChestPage(openedChest!, folderPath, pageIndex); } else Toast("Only links can be removed. The original files stay in place."); break;
                    case 5: RelinkSelected(); break;
                    case 6: FrameImage(slot.Entry!.Path); break;
                }
            }
            catch (Exception e) { Toast(e.Message); }
        };
        menu.PopupHide += () => menu.QueueFree(); menu.Position = GetViewport().GuiEmbedSubwindows ? (Vector2I)GetViewport().GetMousePosition() : DisplayServer.MouseGetPosition(); menu.Popup();
    }
    private async Task FillChestSlots(List<ChestSlot> slots, string id, string? folder, int generation, Label count, Button previous, Button next, Label pageIndicator)
    {
        int request=++chestLoadRequest;string query=chestQuery.Trim();
        var links = state.Links.Where(l => l.ChestId == id).Select(l => (l.Path, l.Id)).ToArray();
        try
        {
            var entries = await Task.Run(() => folder != null ? files.List(folder).Select(e => (Entry: e, Id: (string?)null)).ToArray()
                : links.Select(l => (Entry: new FileEntry(l.Path, System.IO.Path.GetFileName(l.Path.TrimEnd('\\')), System.IO.Directory.Exists(l.Path), files.Exists(l.Path)), Id: (string?)l.Id)).ToArray());
            if (generation != viewGeneration || request!=chestLoadRequest) return;
            entries=entries.Where(e=>e.Entry.Name.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach(var slot in slots) {slot.Entry=null;slot.LinkId=null;slot.Icon=null;slot.TooltipText="";slot.QueueRedraw();}
            int pages = Math.Max(1, (entries.Length + PageSize - 1) / PageSize); pageIndex = Math.Clamp(pageIndex, 0, pages - 1);
            pageIndicator.Text=$"{pageIndex+1}/{pages}";pageIndicator.TooltipText=$"Page {pageIndex+1} of {pages}";
            count.Text = $"{entries.Length} {(query.Length==0?"items":"matches")} · {pageIndex + 1}/{pages}";
            count.TooltipText = $"Page {pageIndex+1} of {pages}. Showing {Math.Min(entries.Length,pageIndex*PageSize+1)}–{Math.Min(entries.Length,(pageIndex+1)*PageSize)} of {entries.Length}. Use the arrows for more files."; previous.Disabled = pageIndex == 0; next.Disabled = pageIndex == pages - 1;
            int index = 0; foreach (var (entry, link) in entries.Skip(pageIndex * PageSize).Take(PageSize)) BindSlot(slots[index++], entry, link, generation);
        }
        catch (Exception e) { if (generation == viewGeneration) { count.Text = "Unavailable"; Toast(e.Message); } }
    }
    private void BindSlot(ChestSlot slot, FileEntry entry, string? link, int generation)
    {
        slot.Entry = entry; slot.LinkId = link; slot.TooltipText = entry.Name + (entry.Available ? "" : "\nUnavailable · Right-click to relink") + "\n" + entry.Path;
        slot.QueueRedraw(); _ = LoadShellImage(slot, entry.Path, generation);
    }
    private async Task LoadShellImage(ChestSlot slot, string path, int generation)
    {
        try
        {
            if (!thumbnails.TryGetValue(path, out var texture))
            {
                var pixels = await shellImages.Get(path);
                if (pixels == null) { GD.PushWarning("Windows image unavailable: " + path); return; }
                using var image = Image.CreateFromData(pixels.Width, pixels.Height, false, Image.Format.Rgba8, pixels.Rgba);
                texture = ImageTexture.CreateFromImage(image);
                if (thumbnails.Count > 256) thumbnails.Clear(); thumbnails[path] = texture; shellImagesLoaded++;
            }
            if (generation == viewGeneration && GodotObject.IsInstanceValid(slot) && slot.Entry?.Path==path) { slot.Icon = texture; slot.QueueRedraw(); }
            if (thumbnailRequested.Add(path)) _ = RefineThumbnail(slot, path, generation);
        }
        catch (Exception e) { GD.PushWarning("Windows image: " + e.Message); }
    }
    private async Task RefineThumbnail(ChestSlot slot, string path, int generation)
    {
        try
        {
            var pixels = await shellImages.Get(path, thumbnail: true);
            if(pixels==null) return;
            using var image=Image.CreateFromData(pixels.Width,pixels.Height,false,Image.Format.Rgba8,pixels.Rgba);
            var texture=ImageTexture.CreateFromImage(image);thumbnails[path]=texture;
            if(generation==viewGeneration && GodotObject.IsInstanceValid(slot) && slot.Entry?.Path==path) {slot.Icon=texture;slot.QueueRedraw();}
        }
        catch(Exception e) { GD.PushWarning("Windows thumbnail: "+e.Message); }
    }
    private void ClearHeld() { heldItem = null; if (heldCursor != null) { heldCursor.QueueFree(); heldCursor = null; } }
    private void SetHeldCursor()
    {
        if (heldCursor != null) { heldCursor.QueueFree(); heldCursor = null; }
        if (heldItem is not { } held) return;
        heldCursor = new ChestSlot { Entry = held.Entry, Size = new Vector2(56, 56), Ghost = true, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 20 };
        if (thumbnails.TryGetValue(held.Entry.Path, out var icon)) heldCursor.Icon = icon;
        layer.AddChild(heldCursor);
    }
}

public partial class ChestFrame : Control
{
    public float PixelScale = 4;
    public override void _Draw()
    {
        float s = PixelScale; var size = new Vector2(176 * s, 166 * s);
        DrawRect(new Rect2(Vector2.Zero, size), new Color("161616"));
        DrawRect(new Rect2(Vector2.One * s, size - Vector2.One * 2 * s), new Color("555555"));
        DrawRect(new Rect2(Vector2.One * 2 * s, size - Vector2.One * 4 * s), new Color("f9f9f9"));
        DrawRect(new Rect2(Vector2.One * 3 * s, size - Vector2.One * 6 * s), new Color("c6c6c6"));
        DrawRect(new Rect2(new Vector2(3 * s, size.Y - 3 * s), new Vector2(size.X - 5 * s, s)), new Color("555555"));
        DrawRect(new Rect2(new Vector2(size.X - 3 * s, 3 * s), new Vector2(s, size.Y - 5 * s)), new Color("555555"));
    }
}
public partial class ChestSlot : Control
{
    public FileEntry? Entry;
    public string? LinkId;
    public string Container = "";
    public float PixelScale = 4;
    public Texture2D? Icon;
    public bool Ghost, Running;
    public string? BuildKind, BuildKey, BuildDragKey;
    private bool buildPressed, buildDragged;
    public Action<string>? BuildDropped;
    public int Count;
    private bool hover;
    private FontFile? countFont;
    public Action<MouseButton, bool, bool>? Activated;
    public Action<Godot.Collections.Dictionary>? Transferred;
    public override void _Ready()
    { MouseEntered += () => { hover = true; QueueRedraw(); }; MouseExited += () => { hover = false; QueueRedraw(); }; }
    public override void _Draw()
    {
        TextureFilter=TextureFilterEnum.Nearest;
        float s = PixelScale;
        if (!Ghost)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color("8b8b8b"));
            DrawRect(new Rect2(0, 0, Size.X, s), new Color("373737")); DrawRect(new Rect2(0, 0, s, Size.Y), new Color("373737"));
            DrawRect(new Rect2(s, Size.Y - s, Size.X - s, s), new Color("f5f5f5")); DrawRect(new Rect2(Size.X - s, s, s, Size.Y - s), new Color("f5f5f5"));
            if (hover) DrawRect(new Rect2(Vector2.One * s, Size - Vector2.One * 2 * s), new Color(1, 1, 1, .35f));
        }
        if (Icon != null)
        {
            var available = Size - Vector2.One * (Ghost ? 0 : 2 * s);
            float fit = Math.Min(available.X / Icon.GetWidth(), available.Y / Icon.GetHeight());
            var drawSize = Icon.GetSize() * fit;
            DrawTextureRect(Icon, new Rect2((Size - drawSize) / 2, drawSize), false);
        }
        else if (Entry != null)
        {
            var at = Size * .2f;
            DrawRect(new Rect2(at, Size * .6f), new Color(Entry.IsDirectory ? "d9b45d" : "e9e8e1"));
            if (Entry.IsDirectory) DrawRect(new Rect2(at.X, at.Y - s, Size.X * .27f, 2*s), new Color("e7c575"));
            else DrawLine(at + new Vector2(2*s,3*s), at + new Vector2(8*s,3*s), new Color("77899d"), s);
        }
        if(Running) DrawRect(new Rect2(6,Size.Y-7,Size.X-12,3),new Color("8ed487"));
        if (Count > 1)
        {
            var font=countFont ??= GD.Load<FontFile>("res://Assets/Vanilla/font/minecraft.fnt");var text=Count.ToString();int fontSize=(int)(8*s);
            var at=new Vector2(Size.X-font.GetStringSize(text,fontSize:fontSize).X-2*s,Size.Y-2*s);
            DrawString(font,at+Vector2.One*s,text,fontSize:fontSize,modulate:new Color("222222"));
            DrawString(font,at,text,fontSize:fontSize,modulate:new Color("fff5dc"));
        }
        if (Entry is { Available: false }) DrawLine(new Vector2(8, Size.Y - 8), new Vector2(Size.X - 8, 8), new Color("b75252"), 3);
    }
    public override void _GuiInput(InputEvent e)
    {
        if(Container=="$building" && e is InputEventMouseButton b && b.ButtonIndex==MouseButton.Left) {if(b.Pressed) {buildPressed=true;buildDragged=false;}else {if(buildPressed && !buildDragged) Activated?.Invoke(b.ButtonIndex,b.DoubleClick,b.ShiftPressed);buildPressed=false;}AcceptEvent();return;}
        if (e is InputEventMouseButton { Pressed: true } m) { Activated?.Invoke(m.ButtonIndex, m.DoubleClick, m.ShiftPressed); AcceptEvent(); }
    }
    public override Variant _GetDragData(Vector2 at)
    {
        if (BuildKey != null)
        {
            buildDragged=true;
            var blockPreview = new TextureRect { Texture=Icon,CustomMinimumSize=new Vector2(56,56),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered };
            SetDragPreview(blockPreview); return new Godot.Collections.Dictionary { ["buildkey"]=BuildDragKey??BuildKey };
        }
        if (Entry == null) return default;
        var preview = new TextureRect { Texture = Icon, CustomMinimumSize = new Vector2(56, 56), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        SetDragPreview(preview);
        return new Godot.Collections.Dictionary { ["path"] = Entry.Path, ["link"] = LinkId ?? "", ["container"] = Container };
    }
    public override bool _CanDropData(Vector2 at, Variant data) => data.VariantType == Variant.Type.Dictionary && ((BuildDropped != null && data.AsGodotDictionary().ContainsKey("buildkey")) || (Container != "$building" && data.AsGodotDictionary().ContainsKey("path") && data.AsGodotDictionary()["container"].AsString() != Container));
    public override void _DropData(Vector2 at, Variant data) { var d=data.AsGodotDictionary(); if(d.ContainsKey("buildkey")) BuildDropped?.Invoke(d["buildkey"].AsString()); else Transferred?.Invoke(d); }
    public override GodotObject _MakeCustomTooltip(string text)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("170c20f5"), BorderColor = new Color("3c196b"), BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 });
        var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", 17); label.AddThemeColorOverride("font_color", new Color("eee5fa")); panel.AddChild(label); return panel;
    }
}
