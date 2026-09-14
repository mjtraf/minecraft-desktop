using Godot;
using Cave.Core;

namespace CozyCave;
public partial class Main
{
    private Theme uiTheme = null!;
    private HBoxContainer hudToolbar = null!;
    private string? folderPath;
    private int pageIndex, viewGeneration;
    private FileEntry? selectedEntry;
    private string? selectedLink;
    private Label? selectionLabel;
    private readonly Dictionary<string, Texture2D> thumbnails = [];
    private const int PageSize = 27;
    private StyleBoxFlat Style(string color, string border, int width = 2)
    {
        return new StyleBoxFlat { BgColor = new Color(color), BorderColor = new Color(border), BorderWidthLeft = width, BorderWidthTop = width, BorderWidthRight = width, BorderWidthBottom = width, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8 };
    }
    private void BuildHud()
    {
        uiTheme = new Theme { DefaultFontSize = 18, DefaultFont = MinecraftFont() };
        uiTheme.SetColor("font_color", "Label", new Color("303030"));
        uiTheme.SetColor("font_color", "Button", new Color("eee5cf"));
        uiTheme.SetStylebox("panel", "PanelContainer", Style("c6c6c6", "373737", 3));
        uiTheme.SetStylebox("normal", "Button", Style("747474", "eeeeee"));
        uiTheme.SetStylebox("hover", "Button", Style("9196a2", "ffffff"));
        uiTheme.SetStylebox("pressed", "Button", Style("555555", "373737"));
        uiTheme.SetStylebox("focus", "Button", new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = Colors.White, BorderWidthBottom = 3 });
        uiTheme.SetStylebox("normal", "LineEdit", Style("202020", "a0a0a0"));
        uiTheme.SetColor("font_color", "LineEdit", new Color("eee5cf"));
        layer = new CanvasLayer(); AddChild(layer);
        hint = new Label { Theme = uiTheme, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        hint.AddThemeColorOverride("font_color", Colors.White); hint.AddThemeFontSizeOverride("font_size", 24); hint.AddThemeColorOverride("font_shadow_color", new Color("19201b")); hint.AddThemeConstantOverride("shadow_offset_x", 2); hint.AddThemeConstantOverride("shadow_offset_y", 2);
        layer.AddChild(hint); hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        statusLabel = new Label { Theme = uiTheme, Text = status, Position = new Vector2(24, 20), MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(1370, 50), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        statusLabel.AddThemeColorOverride("font_color", Colors.White); statusLabel.AddThemeFontSizeOverride("font_size", 15); layer.AddChild(statusLabel);
        var toolbar = hudToolbar = new HBoxContainer { Theme = uiTheme, Position = new Vector2(24, 830) }; layer.AddChild(toolbar);
        toolbar.AddChild(Button("E  Inventory", ShowBuildingInventory));
        toolbar.AddChild(Button("F  Find a file", ShowSearch)); toolbar.AddChild(Button("B  Decorate", ShowDecorate));
        toolbar.AddChild(Button("Tab  Settings", ShowSettings));
        GetViewport().SizeChanged += () => toolbar.Position = new Vector2(24, GetViewport().GetVisibleRect().Size.Y - 58);
        toolbar.Position = new Vector2(24, GetViewport().GetVisibleRect().Size.Y - 58);
    }
    private VBoxContainer OpenPanel(string title, string subtitle = "")
    {
        LeaveTelevision();
        ClosePanel(false); walking = false; Input.MouseMode = Input.MouseModeEnum.Visible;
        if (bridge.Connected) bridge.Send(new { command = "enter" });
        var scroll = new ScrollContainer { Theme = uiTheme }; layer.AddChild(scroll); scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); panel = scroll;
        var center = new CenterContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; scroll.AddChild(center);
        var bg = new PanelContainer { CustomMinimumSize = new Vector2(1100, 0) }; center.AddChild(bg);
        bg.AddThemeColorOverride("font_color", new Color("303030"));
        var margin = new MarginContainer(); foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 20); bg.AddChild(margin);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); margin.AddChild(box);
        var heading = new HBoxContainer(); box.AddChild(heading);
        var label = new Label { Text = title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; label.AddThemeFontSizeOverride("font_size", 28); heading.AddChild(label);
        heading.AddChild(Button("Close  Esc", ClosePanelAndResume));
        if (subtitle.Length > 0) { var sub = new Label { Text = subtitle }; sub.AddThemeFontSizeOverride("font_size", 15); sub.Modulate = new Color("505050"); box.AddChild(sub); }
        return box;
    }
    private Button Button(string text, Action action)
    {
        var button = new Button { Text = text, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        button.Pressed += () => { try { action(); } catch (Exception e) { Toast(e.Message); } }; return button;
    }
    private void ClosePanel(bool sound = true)
    {
        if(notebookOpen) {Save();notebookOpen=false;}
        buildingInventoryOpen = false; editingFrame=null; musicPanelOpen=false;
        appsOpen=false; ClearCracks();
        ClearHeld();
        if (statusLabel != null) statusLabel.Visible = true;
        if (hudToolbar != null) hudToolbar.Visible = true;
        if (panel != null) { panel.QueueFree(); panel = null; if (sound && openedChest != null) PlayEffect("close"); }
        viewGeneration++; openedChest = null; selectedEntry = null; selectedLink = null; selectionLabel = null;
        watcher.Watch(desktopRoots);
    }
    private void ShowChest(string id) => ShowChestPage(id, null, 0);
    private static string Short(string text, int length) => text.Length > length ? text[..(length - 1)] + "…" : text;
    private void OpenSelected()
    {
        if (selectedEntry == null) { Toast("Select an item first."); return; }
        try
        {
            if (selectedEntry.IsDirectory) ShowChestPage(openedChest ?? "inbox", selectedEntry.Path, 0);
            else { var path=selectedEntry.Path; ClosePanel(); Release(false); files.Open(path); }
        }
        catch (Exception e) { Toast(e.Message); }
    }
    private void RelinkSelected()
    {
        if (selectedLink == null) { Toast("Select a saved chest link to relink it."); return; }
        var link = state.Links.First(l => l.Id == selectedLink); var chest = openedChest!;
        var box = OpenPanel("Reconnect link", "Choose the current location of " + link.Name);
        void Done(string path) { link.Path = FileService.Normalize(path); Changed(); ShowChest(chest); }
        box.AddChild(Button("Choose file", () => PickFiles(paths => { if (paths.Length > 0) Done(paths[0]); })));
        box.AddChild(Button("Choose folder", () => PickFolder(Done)));
    }
    private void ChooseDestination(string path, string? moveId)
    {
        var box = OpenPanel(moveId == null ? "Link to a chest" : "Store carried item", System.IO.Path.GetFileName(path));
        foreach (var chest in state.Chests.Where(c=>ChestStorage.AcceptsFiles(state,c.Id)))
            box.AddChild(Button(chest.Name, () => { if (moveId == null) library.Add(path, chest.Id); else library.Transfer(moveId, chest.Id); Changed(); ShowChest(chest.Id); }));
    }
    private void RefreshChest() { thumbnails.Clear(); thumbnailRequested.Clear(); if (openedChest != null) ShowChestPage(openedChest, folderPath, pageIndex); }
    private void PickFiles(Action<string[]> callback)
    {
        var dialog = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFiles, UseNativeDialog = true, Title = "Choose files to link" }; AddChild(dialog);
        dialog.FilesSelected += paths => { callback(paths); dialog.QueueFree(); }; dialog.Canceled += () => dialog.QueueFree(); dialog.PopupCenteredRatio(.7f);
    }
    private void PickFolder(Action<string> callback)
    {
        var dialog = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenDir, UseNativeDialog = true, Title = "Choose a folder to link" }; AddChild(dialog);
        dialog.DirSelected += path => { callback(path); dialog.QueueFree(); }; dialog.Canceled += () => dialog.QueueFree(); dialog.PopupCenteredRatio(.7f);
    }
    private void ShowSettings()
    {
        var box = OpenPanel("Make yourself at home", "Escape closes this screen. Hold Escape for a moment while walking to return to Windows.");
        Slider(box, "Mouse sensitivity", .0005, .006, .0001, state.Settings.Sensitivity, v => state.Settings.Sensitivity = (float)v);
        Slider(box, "Interface scale", .8, 1.3, .1, state.Settings.UiScale, v => { state.Settings.UiScale = (float)v; GetWindow().ContentScaleSize = new Vector2I((int)(1440 / v), (int)(900 / v)); });
        Slider(box, "Effects and ambience", 0, 1, .05, state.Settings.EffectsVolume, v => state.Settings.EffectsVolume = (float)v);
        Slider(box, "Music volume", 0, 1, .05, state.Settings.MusicVolume, v => state.Settings.MusicVolume = (float)v);
        var bob = new CheckButton { Text = "Walking view bob", ButtonPressed = state.Settings.ViewBobbing }; bob.Toggled += value => { state.Settings.ViewBobbing = value; Changed(); }; box.AddChild(bob);
        if (monitorNames.Length > 0)
        {
            box.AddChild(new Label { Text = "Cave monitor" }); var monitor = new OptionButton();
            foreach (var name in monitorNames) monitor.AddItem(name);
            monitor.Selected = Math.Max(0, Array.IndexOf(monitorNames, state.Settings.Monitor));
            monitor.ItemSelected += index => { state.Settings.Monitor = monitorNames[index]; Changed(); bridge.Send(new { command = "monitor", value = state.Settings.Monitor }); }; box.AddChild(monitor);
        }
        box.AddChild(new Label { Text = "WASD walk · Space jump · Shift sprint · Right-click chest\nE inventory · F find a file · B decorate · 1–9 / wheel hotbar · Hold Esc returns to Windows", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        box.AddChild(new Label {Text=$"Valley: {WorldRadius*2+1} x {WorldRadius*2+1} blocks | Build Y {ValleyBottom} to {ValleyTop} | G flight, Space up, Ctrl down, Home return"});
        box.AddChild(Button("Return home",ReturnHome));
        box.AddChild(Button(flying?"Turn flight off (G)":"Turn flight on (G)",()=>{ToggleFlight();ShowSettings();}));
        var expand=Button("Expand valley by 16 blocks on every side",ExpandValley);expand.Disabled=WorldRadius>=96;box.AddChild(expand);
        box.AddChild(Button("Desk notebook",()=>ShowNotebook()));
        box.AddChild(Button("Desktop file check", ShowDesktopFiles));
        box.AddChild(Button("Jukebox", ShowMusic));
        box.AddChild(Button("Restore normal desktop", () => { ClosePanel(); bridge.Send(new { command = "restore" }); }));
        box.AddChild(Button("Save and quit", Quit));
    }
    private void Slider(VBoxContainer box, string name, double min, double max, double step, double value, Action<double> setter)
    {
        var row = new HBoxContainer(); box.AddChild(row);
        row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(230, 0) });
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(350, 30) }; row.AddChild(slider);
        slider.ValueChanged += v => { setter(v); Changed(); };
    }
    private void ShowDesktopFiles()
    {
        var box=OpenPanel("Your desktop files", "Every chest page shows up to 27 items. F searches all saved links, including other pages and packed chests.");
        foreach(var root in desktopRoots)
        {
            try
            {
                var entries=files.List(root);int linked=entries.Count(e=>state.Links.Any(l=>string.Equals(l.Path,e.Path,StringComparison.OrdinalIgnoreCase)));
                box.AddChild(new Label {Text=$"{root}\n{linked} of {entries.Count} desktop files/folders linked",AutowrapMode=TextServer.AutowrapMode.WordSmart});
            }
            catch(Exception e) {box.AddChild(new Label {Text="Could not read "+root+": "+e.Message});}
        }
        foreach(var group in state.Links.GroupBy(l=>l.ChestId))
        {
            var chest=state.Chests.FirstOrDefault(c=>c.Id==group.Key);string name=chest?.Name??"Carried files";
            string id=group.Key;box.AddChild(Button($"{name}{(chest?.Carried==true?" (packed)":"")} · {group.Count()} links · {Math.Max(1,(group.Count()+26)/27)} pages",()=>ShowChest(id)));
        }
        box.AddChild(Button($"Search all {state.Links.Count} saved links",ShowSearch));
    }
    private void ShowSearch()
    {
        var box = OpenPanel("Find something", "Search every saved chest link by name. Results include every page and packed chest.");
        var input = new LineEdit { PlaceholderText = "File or folder name…" }; box.AddChild(input);
        var results = new VBoxContainer(); box.AddChild(results);
        var pager=new HBoxContainer();box.AddChild(pager);
        int page=0;var count=new Label {SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,HorizontalAlignment=HorizontalAlignment.Center};
        Button? previous=null,next=null;
        void Search()
        {
            foreach (var child in results.GetChildren()) { results.RemoveChild(child); child.QueueFree(); }
            var matches=state.Links.Where(l=>l.Name.Contains(input.Text,StringComparison.OrdinalIgnoreCase)).ToArray();
            int pages=Math.Max(1,(matches.Length+9)/10);page=Math.Clamp(page,0,pages-1);
            count.Text=$"{matches.Length} matches · Page {page+1} of {pages}";
            if(previous!=null) previous.Disabled=page==0;if(next!=null) next.Disabled=page==pages-1;
            foreach (var link in matches.Skip(page*10).Take(10))
            {
                var chest=state.Chests.FirstOrDefault(c=>c.Id==link.ChestId);
                string name=chest?.Name??"Carried files";
                var row = new HBoxContainer(); results.AddChild(row);
                var open = Button(Short(link.Name, 65) + "  ·  " + name + (chest?.Carried==true?" (packed)":""), () => {
                    int index=state.Links.Where(l=>l.ChestId==link.ChestId).ToList().FindIndex(l=>l.Id==link.Id);
                    ShowChestPage(link.ChestId,null,Math.Max(0,index)/PageSize);
                }); open.TooltipText=link.Path;open.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(open);
                if(chest is {Carried:false}) row.AddChild(Button("Locate", () => { highlightedChest = link.ChestId; BuildWorld(); ClosePanel(); player.LookAt(new Vector3(chest.X,player.Position.Y,chest.Z));pitch=0;camera.Rotation=Vector3.Zero;Toast("Follow the green light to "+name);Enter(); }));
            }
        }
        previous=Button("Previous",()=>{page--;Search();});pager.AddChild(previous);pager.AddChild(count);
        next=Button("Next",()=>{page++;Search();});pager.AddChild(next);
        pager.AddChild(Button("Desktop file check",ShowDesktopFiles));
        input.TextChanged += _=>{page=0;Search();};Search();input.GrabFocus();
    }
    private void ShowChestOptions(string id)
    {
        var chest = state.Chests.Single(c => c.Id == id);
        var box = OpenPanel("Chest options", "Pick up a chest to carry its name and every file link together.");
        var name = new LineEdit { Text = chest.Name, MaxLength = 24 }; box.AddChild(name);
        box.AddChild(Button("Save name", () => { if (string.IsNullOrWhiteSpace(name.Text)) return; chest.Name = name.Text.Trim(); Changed(); BuildWorld(); ShowChest(id); }));
        box.AddChild(Button("Move chest", () => BeginPlacement("chest", id)));
        box.AddChild(Button("Rotate 90°", () => { chest.Rotation = (chest.Rotation + 90) % 360; Changed(); BuildWorld(); }));
        box.AddChild(Button("Pick up chest", () => { PickUpObject(id); ClosePanelAndResume(); }));
    }
}
