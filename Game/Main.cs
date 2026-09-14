using Godot;
using Cave.Core;

namespace CozyCave;
public partial class Main : Node3D
{
    private CaveState state = null!;
    private StateStore store = null!;
    private IDisposable? worldSession;
    private bool saveErrorReported;
    private LinkLibrary library = null!;
    private readonly FileService files = new();
    private readonly FolderWatcher watcher = new();
    private readonly DesktopBridge bridge = new();
    private CharacterBody3D player = null!;
    private Camera3D camera = null!;
    private Node3D world = null!;
    private CanvasLayer layer = null!;
    private Label hint = null!, statusLabel = null!;
    private Control? panel;
    private string? openedChest;
    private bool walking, paused, hidden, locked, proof, selfTesting;
    private bool manualDesktopRelease;
    private bool mouseActionsReady, miningHeld;
    private void ResetMouseActions(){screenPress=null;mouseActionsReady=false;miningHeld=false;breakTime=0;breakingId=null;ClearCracks();swingTime=0;}
    private void PollMouseActions(bool left,bool right) {if(!left)miningHeld=false;if(!left && !right)mouseActionsReady=true;}
    private float pitch, scanTime, saveTime, stepTime, bobTime;
    private string status = "Click to enter • WASD to walk • Right-click a chest • Hold Escape returns to Windows";
    private string[] desktopRoots = [];
    private string[] monitorNames = [];
    private bool dirty, backgroundApp;
    private bool escapeDown, escapeCanRelease;
    private float escapeHold;
    private int enterRequests;
    private string? hovered;
    public override void _Ready()
    {
        DisplayServer.WindowSetTitle("Minecraft Desktop");
        var args = OS.GetCmdlineUserArgs(); proof = args.Contains("--proof"); selfTesting=args.Contains("--screen-test") || args.Contains("--villager-test") || args.Contains("--chest-test") || args.Contains("--save-reopen") || args.Contains("--save-audit") || args.Contains("--world-test") || args.Contains("--dock-ui-test") || args.Contains("--self-test") || args.Contains("--tv-smoke") || args.Contains("--picture-test");
        var data = args.Contains("--test-data") ? args[Array.IndexOf(args, "--test-data") + 1] : System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "CozyCave");
        store = new StateStore(data);
        try { worldSession=store.AcquireSession();state = store.Load();GD.Print("World loaded: "+store.DirectoryPath); }
        catch (Exception e) { GD.PushError(e.Message); OS.Alert("The world could not be opened. It may already be running. Your save was not changed.\n"+e.Message,"Minecraft Desktop save protection");GetTree().Quit(1); return; }
        if(!proof && state.RoomRevision<1)
        {
            var previous=System.IO.Path.Combine(data,"state.json");
            if(System.IO.File.Exists(previous) && !System.IO.File.Exists(System.IO.Path.Combine(data,"state.before-cozy-remodel.json"))) System.IO.File.Copy(previous,System.IO.Path.Combine(data,"state.before-cozy-remodel.json"),false);
            RoomRemodel.Apply(state);store.Save(state);
        }
        library = new LinkLibrary(state);
        desktopRoots = args.Contains("--test-data") ? [] : [System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory), System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonDesktopDirectory)];
        dirty = library.ScanDesktop(desktopRoots); watcher.Watch(desktopRoots);
        LoadBlockCatalog(); BuildWorld();
        player = new CharacterBody3D { Position = new Vector3(state.Player[0], Math.Clamp(state.Player[1],ValleyBottom,ValleyTop-2), state.Player[2]) };
        flying=state.Flying;
        player.CollisionLayer = 4; player.CollisionMask = 1;
        player.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.28f, Height = 1.75f }, Position = new Vector3(0, 0.9f, 0) });
        AddChild(player); player.Rotation = new Vector3(0, state.Yaw, 0);
        camera = new Camera3D { Position = new Vector3(0, 1.62f, 0), Fov = 75, Near = 0.05f, Far = 150 };
        player.AddChild(camera);
        BuildHud(); BuildHotbar(); BuildAppDock(); SetupAudio(); BuildCat(); BuildVillager();
        interactionCursor=AddInteractionCursor(this,()=>softwareCursorActive && pictureDialog==null);
        foreach (var drop in state.Drops) SpawnDrop(drop);
        GetWindow().ContentScaleSize = new Vector2I((int)(1440 / state.Settings.UiScale), (int)(900 / state.Settings.UiScale));
        GetWindow().FilesDropped += HandleFileDrop;
        GetWindow().FocusExited += () => { if(!selfTesting) CaveFocusLost(); };
        GetWindow().FocusEntered += () => {if(!selfTesting) ResumeCaveFocus();};
        GetTree().AutoAcceptQuit = false;
        GetWindow().CloseRequested += Quit;
        if (store.RecoveryMessage != null) Toast(store.RecoveryMessage);
        int i = Array.IndexOf(args, "--pipe");
        if (i >= 0) _ = bridge.Connect(args[i + 1], DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle), state.Settings.Monitor);
        if (args.Contains("--capture")) _ = CaptureAfterFrames(args[Array.IndexOf(args, "--capture") + 1]);
        if(args.Contains("--tv-smoke")) _ = RunTvSmoke(args[Array.IndexOf(args,"--tv-smoke")+1]);
        if (args.Contains("--self-test")) _ = RunSmokeTests(args[Array.IndexOf(args, "--self-test") + 1]);
        if (args.Contains("--dock-ui-test")) _ = RunDockUiTest(args[Array.IndexOf(args,"--dock-ui-test")+1]);
        if(args.Contains("--save-reopen")) _ = RunSaveReopenTest(args[Array.IndexOf(args,"--save-reopen")+1],args.Contains("--verify-save"),false);
        if(args.Contains("--screen-test")) _ = RunScreenTests(args[Array.IndexOf(args,"--screen-test")+1]);
        if(args.Contains("--villager-test")) _ = RunVillagerTests(args[Array.IndexOf(args,"--villager-test")+1]);
        if(args.Contains("--chest-test")) _ = RunChestStorageTest(args[Array.IndexOf(args,"--chest-test")+1],args.Contains("--verify-save"));
        if(args.Contains("--save-audit")) _ = RunSaveReopenTest(args[Array.IndexOf(args,"--save-audit")+1],false,true);
        if(args.Contains("--world-test")) _ = RunWorldInteractionTests(args[Array.IndexOf(args,"--world-test")+1]);
        if (args.Contains("--picture-test")) _ = RunPicturePickerTest(args[Array.IndexOf(args, "--picture-test") + 1]);
    }
    private async Task CaptureAfterFrames(string path)
    {
        for (int i = 0; i < 50; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(path);
        Toast("Screenshot saved");
    }
    public override void _Input(InputEvent e)
    {
        if(HandleScreenRelease(e))return;
        if(e is InputEventMouseButton {ButtonIndex:MouseButton.Left,Pressed:false})miningHeld=false;
        if(HandleVillagerInput(e))return;
        if(HandleTvInput(e))return;
        if (pictureDialog != null) return; // The modal picker owns Escape and keyboard navigation.
        if (buildingInventoryOpen && e is InputEventKey {Pressed:true, Echo:false, Keycode:Key.E}) { ClosePanelAndResume(); GetViewport().SetInputAsHandled(); return; }
        if (e is InputEventKey { Keycode: Key.Escape } escape)
        {
            if (!escape.Echo)
            {
                if(escape.Pressed)
                {
                    escapeDown=true;escapeHold=0;escapeCanRelease=panel==null && walking;
                    if(panel!=null) {CancelPlacement();ClosePanelAndResume();}
                }
                else {escapeDown=false;escapeHold=0;escapeCanRelease=false;}
            }
            GetViewport().SetInputAsHandled();
        }
    }
    private void UpdateEscape(float delta)
    {
        if(!escapeDown || !escapeCanRelease || !walking || panel!=null || paused || locked) {escapeHold=0;return;}
        escapeHold+=delta;
        hint.Text=$"Hold Esc to return to Windows  {Math.Min(100,(int)(escapeHold/0.85f*100))}%";
        if(escapeHold>=.85f) {escapeCanRelease=false;escapeDown=false;escapeHold=0;Release();}
    }

    private void ClosePanelAndResume()
    {
        ClosePanel();
        // Closing an inventory stays in the focused cave. Only a subsequent
        // Escape while walking sends the desktop helper an idle request.
        StartWalking();
        Save();
    }
    private void Changed() { dirty = true; Save(); }
    private void Save()
    {
        if (state == null) return;
        FlushNotebookEdits();
        if (player != null) { var savedPosition=sittingOn!=null?standingPosition:player.Position;state.Player = [savedPosition.X,savedPosition.Y,savedPosition.Z]; state.Yaw = player.Rotation.Y; }
        try { store.Save(state); dirty = false;saveErrorReported=false; } catch (Exception e) { Toast("Could not save: " + e.Message); dirty = true;if(!saveErrorReported){saveErrorReported=true;GD.PushError(e.Message);OS.Alert("Your world could not be saved.\n"+e.Message,"Minecraft Desktop save problem");} }
    }
    private void Toast(string text)
    {
        status = text;
        if (statusLabel != null) statusLabel.Text = text;
        if (selectionLabel != null) selectionLabel.Text = text;
    }
    private void Quit() { Save(); bridge.Send(new { command = "quit" }); Input.MouseMode = Input.MouseModeEnum.Visible; GetTree().Quit(); }
    private void Enter()
    {
        if (PointerInteractionOpen || walking) return;
        manualDesktopRelease=false;backgroundApp=false;hidden=false;RenderingServer.SetRenderLoopEnabled(true);Engine.MaxFps=60;StartWalking();enterRequests++;
        if (bridge.Connected) bridge.Send(new { command = "enter" });
    }
    private void ResumeCaveFocus()
    {
        ResetMouseActions();
        hidden=false;scanTime=0;
        if(paused || locked) return;
        RenderingServer.SetRenderLoopEnabled(true);Engine.MaxFps=60;
        if(manualDesktopRelease) return;
        backgroundApp=false;StartWalking();
        if(bridge.Connected) bridge.Send(new{command="focus-return"});
    }
    private bool PointerInteractionOpen => panel!=null || pictureDialog!=null || tvFocused;
    private void CaveFocusLost()
    {
        CancelVillagerSpeech();
        ResetMouseActions();
        // Losing focus releases a held browser button, but does not close the TV.
        if(tvFocused)CancelScreenPointer();
        escapeDown=false;escapeCanRelease=false;escapeHold=0;
        if(walking)Release(false);
        else Input.MouseMode=Input.MouseModeEnum.Visible;
    }
    private void StartWalking() { if(!walking)ResetMouseActions(); if(PointerInteractionOpen) {walking=false;Input.MouseMode=Input.MouseModeEnum.Visible;return;} GetViewport().GuiReleaseFocus(); walking = true; if(!desktopHotbarActive && placingKind == null && ResolveSupply(state.Hotbar[state.SelectedSlot]) is {} equipped) { placingKind=equipped.Kind;movingId=equipped.Id;RefreshHand(); } Input.MouseMode = Input.MouseModeEnum.Captured; Toast("WASD move   Space jump   G fly   Ctrl descend   Home return   Right-click open   E inventory   Tab settings   Hold Escape to return"); }
    private void Release(bool returnToDesktop = true)
    {
        ResetMouseActions();
        escapeDown=false;escapeCanRelease=false;escapeHold=0;
        manualDesktopRelease=returnToDesktop;backgroundApp=!returnToDesktop; walking = false; Input.MouseMode = Input.MouseModeEnum.Visible;
        bridge.Send(new { command = returnToDesktop ? "idle" : "background" }); Save();
    }
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            if(panel==null && key.CtrlPressed && key.Keycode==Key.Space) {OpenDesktopSystem("search");return;}
            if(panel==null && key.PhysicalKeycode==Key.Quoteleft) {ToggleDesktopHotbar();GetViewport().SetInputAsHandled();return;}
            if(panel==null && desktopHotbarActive && key.Keycode==Key.Enter) {UseDesktopSlot();return;}
            if(panel==null && key.Keycode==Key.G) {ToggleFlight();return;}
            if(key.Keycode==Key.Home) {ReturnHome();return;}
            if (key.Keycode == Key.R && placingKind != null) { placementTurn = (placementTurn + 90) % 360; return; }
            if (walking && key.Keycode >= Key.Key1 && key.Keycode <= Key.Key9)
            {
                if(desktopHotbarActive) SelectDesktopSlot(desktopSelection/9*9+(int)key.Keycode-(int)Key.Key1);else SelectHotbar((int)key.Keycode - (int)Key.Key1);
                return;
            }
            if (key.Keycode == Key.F2) {ShowApps();return;}
            if (key.Keycode == Key.F3) {ShowWorkbench();return;}
            if (key.Keycode == Key.Tab) { ShowSettings(); GetViewport().SetInputAsHandled(); return; }
            if (key.Keycode == Key.E) {if(desktopHotbarActive) ShowApps();else ShowBuildingInventory(); return; }
            if (key.Keycode == Key.F) { ShowSearch(); return; }
            if (key.Keycode == Key.B) { ShowDecorate(); return; }
        }
        if (e is InputEventMouseMotion motion && walking && panel == null)
        {
            if(sittingOn!=null)seatedLookYaw=Mathf.Clamp(seatedLookYaw-motion.Relative.X*state.Settings.Sensitivity,-Mathf.DegToRad(80),Mathf.DegToRad(80));
            else player.RotateY(-motion.Relative.X * state.Settings.Sensitivity);
            pitch = Mathf.Clamp(pitch - motion.Relative.Y * state.Settings.Sensitivity, -1.45f, 1.45f);
            camera.Rotation = new Vector3(pitch, sittingOn!=null?seatedLookYaw:0, 0);
        }
        if (e is InputEventMouseButton { Pressed: true } mouse && panel == null)
        {
            if (walking && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) { if(desktopHotbarActive) MoveDesktopSelection(mouse.ButtonIndex==MouseButton.WheelUp?-1:1);else SelectHotbar(state.SelectedSlot + (mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1)); return; }
            if(mouse.ButtonIndex is MouseButton.Left or MouseButton.Right && !mouseActionsReady) {GetViewport().SetInputAsHandled();return;}
            if(walking && ((hovered=="$workstation" && mouse.ButtonIndex is MouseButton.Left or MouseButton.Right) || (hovered=="$villager" && mouse.ButtonIndex==MouseButton.Right))){OpenVillagerMonitor();return;}
            if(walking && SurfaceFor(hovered) is {} display && mouse.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                if(mouse.ButtonIndex==MouseButton.Left){screenPress=hovered;screenPressTime=0;miningHeld=true;return;}
                if(!mouse.ShiftPressed){activeScreen=display;if(display.Role=="tv")FocusTelevision();else OpenVillagerMonitor();return;}
            }
            if(walking && !desktopHotbarActive && mouse.ButtonIndex==MouseButton.Left) {miningHeld=true;}
            if(walking && hovered=="$tv" && mouse.ButtonIndex==MouseButton.Right){if(mouse.ShiftPressed)ShowTelevision();else FocusTelevision();return;}
            if(walking && desktopHotbarActive && mouse.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                if(mouse.ButtonIndex==MouseButton.Left) UseDesktopSlot();else ShowDesktopAppMenu();
                GetViewport().SetInputAsHandled();return;
            }
            if(walking && placingKind=="writable_book" && mouse.ButtonIndex==MouseButton.Right)
            {
                UpdatePlacement();
                if(placementValid) Place();
                else if(!mouse.ShiftPressed) ShowNotebook();
                else Toast("Aim at the top of the desk or another surface to place the book.");
                return;
            }
            if (walking && mouse.ButtonIndex == MouseButton.Right && !mouse.ShiftPressed && hovered != null)
            {
                if (hovered == "$notebook") {ShowNotebook();return;}
                if (hovered == "$cat") {ToggleCatSit();return;}
                if (hovered == "$workbench") {ShowWorkbench();return;}
                if (InteractBlock(hovered)) return;
                if (hovered == "$jukebox") { ShowMusic(); return; }
                if (state.Chests.Any(c => c.Id == hovered)) { ShowChest(hovered); return; }
            }
            if (walking && placingKind != null && mouse.ButtonIndex == MouseButton.Right) { Place(true); return; }
            if (!walking && mouse.ButtonIndex == MouseButton.Left) Enter();
            else if (walking && mouse.ButtonIndex == MouseButton.Right && hovered != null)
            { if (hovered == "$jukebox") ShowMusic(); else if (state.Chests.Any(c => c.Id == hovered)) ShowChest(hovered); }
        }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (player == null || paused || locked) return;
        float dt = (float)delta; UpdateCat(dt); UpdateVillager(dt);
        if(UpdateSitting())return;
        var velocity = player.Velocity;
        if(flying)velocity.Y=0;else if (!player.IsOnFloor()) velocity.Y -= 24 * dt; else velocity.Y = -0.1f;
        if (walking && panel == null)
        {
            var input = new Vector3((Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0), 0,
                (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0));
            var direction = player.Basis * input.Normalized(); var speed = Input.IsPhysicalKeyPressed(Key.Shift) ? 6.2f : 4.3f;
            if(flying)speed=Input.IsPhysicalKeyPressed(Key.Shift)?14:7;
            else if(InWater(player.Position)) speed*=.55f;
            velocity.X = direction.X * speed; velocity.Z = direction.Z * speed;
            if(flying)velocity.Y=((Input.IsPhysicalKeyPressed(Key.Space)?1:0)-(Input.IsPhysicalKeyPressed(Key.Ctrl)?1:0))*speed;
            else if (Input.IsPhysicalKeyPressed(Key.Space) && player.IsOnFloor()) velocity.Y = 7.8f;
            if (!flying && input.LengthSquared() > 0 && player.IsOnFloor())
            {
                stepTime += dt; bobTime += dt * speed * 2;
                if (stepTime > (speed > 5 ? 0.3f : 0.43f)) { PlayEffect("step"); stepTime = 0; }
            }
        }
        else { velocity.X = 0; velocity.Z = 0; }
        player.Velocity = velocity; player.MoveAndSlide();
        player.Position=new Vector3(Mathf.Clamp(player.Position.X,-WorldRadius+.5f,WorldRadius-.5f),Math.Min(player.Position.Y,ValleyTop-2),Mathf.Clamp(player.Position.Z,-WorldRadius+.5f,WorldRadius-.5f));
        if (player.Position.Y < ValleyBottom-4) ReturnHome();
        if(!tvFocused)camera.Position = new Vector3(0, 1.62f + (state.Settings.ViewBobbing && walking ? Mathf.Sin(bobTime) * 0.035f : 0), 0);
    }
    public override void _Process(double delta)
    {
        if (state == null || player == null) return;
        if(!paused && !locked)EnsureValley(player.Position);
        while (bridge.Messages.TryDequeue(out var message))
        {
            var command = message.GetProperty("command").GetString();
            var p = message.TryGetProperty("payload", out var payload) ? payload : default;
            switch (command)
            {
                case "villager-ready": if(!workstationConnected){workstationFrames=new Cave.Transport.TvFrameBuffer(p.GetProperty("channel").GetString()!);workstationConnected=true;}break;
                case "villager-status": ReceiveVillager(p);break;
                case "villager-dictation": ReviewVillagerVoice(p.GetProperty("text").GetString()??"");break;
                case "villager-return": LeaveTelevision();backgroundApp=false;StartWalking();break;
                case "tv-ready": ConnectTv(p.GetProperty("channel").GetString()!);break;
                case "tv-status": ReceiveTv(p);break;
                case "tv-return": backgroundApp=false;ShowTelevision();break;
                case "apps": ReceiveApps(p); break;
                case "dock-action": HandleDock(p); break;
                case "desktop-process": desktopHelperPid=p.GetProperty("pid").GetUInt32(); break;
                case "entered": if(backgroundApp) break; if(PointerInteractionOpen) {walking=false;Input.MouseMode=Input.MouseModeEnum.Visible;break;} DisplayServer.WindowMoveToForeground(); StartWalking(); break;
                case "attached": walking = false; Input.MouseMode = Input.MouseModeEnum.Visible; Toast("Click the cave to enter • Hold Escape returns to Windows"); break;
                case "release": escapeDown=false;escapeCanRelease=false;escapeHold=0; walking = false; Input.MouseMode = Input.MouseModeEnum.Visible; break;
                case "visibility": hidden = (selfTesting || !GetWindow().HasFocus()) && p.GetProperty("hidden").GetBoolean(); locked = p.GetProperty("locked").GetBoolean(); break;
                case "pause": paused = p.GetProperty("value").GetBoolean(); if (paused) Release(); break;
                case "status": Toast(p.GetProperty("text").GetString()!); break;
                case "monitors": monitorNames = p.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).ToArray(); break;
                case "quit": Save(); GetTree().Quit(); break;
                case "disconnected": Input.MouseMode=Input.MouseModeEnum.Visible; Save(); GetTree().Quit(); break;
            }
        }
        Engine.MaxFps = locked || paused ? 10 : hidden ? 30 : 60;
        RenderingServer.SetRenderLoopEnabled(!(locked || paused));
        UpdateWorkstation((float)delta); UpdateTelevision((float)delta); UpdateNotebook((float)delta); UpdateAudio(); UpdateRainAudio((float)delta); UpdateWater((float)delta); UpdateAppDock((float)delta);
        scanTime += (float)delta; saveTime += (float)delta;
        if (scanTime > 2)
        {
            scanTime = 0; bool change = watcher.ConsumeChanges();
            if (library.ScanDesktop(desktopRoots)) { Changed(); change = true; }
            if (change && openedChest != null) RefreshChest();
        }
        if (saveTime > 15) { saveTime = 0; Save(); }
        hovered = null;
        var query = PhysicsRayQueryParameters3D.Create(camera.GlobalPosition, camera.GlobalPosition - camera.GlobalBasis.Z * 4);
        query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        UpdateFocusedScreenCamera();hovered=RayItem(hit);UpdateTvAim((float)delta);
        hint.Text = tvFocused || panel != null ? "" : hovered == "$tv" ? (walking?"+":"TV  ·  Right-click for controls") : hovered == "$notebook" ? "NOTES & CHECKLISTS  -  Right-click" : hovered == "$jukebox" ? "JUKEBOX  ·  Right-click" : hovered != null && state.Chests.FirstOrDefault(c => c.Id == hovered) is { } chest ? chest.Name.ToUpperInvariant() + "  ·  Right-click to open" : walking ? "+" : "";
        AnimateChests((float)delta);
        UpdateDrops(Math.Min((float)delta,.1f)); UpdateHand((float)delta);
        UpdatePlacement();
        if(walking && panel==null && (Math.Abs(player.Position.X)>WorldRadius-1 || Math.Abs(player.Position.Z)>WorldRadius-1))hint.Text="Valley border | Tab: expand valley";
        PollMouseActions((WindowsAppControl.GetAsyncKeyState(1)&0x8000)!=0,(WindowsAppControl.GetAsyncKeyState(2)&0x8000)!=0);
        if(screenPress!=null)screenPressTime+=(float)delta;
        UpdateBreaking(Math.Min((float)delta, .1f), !tvFocused && (!desktopHotbarActive || screenPress!=null) && miningHeld && (screenPress==null || screenPressTime>=.25f));
        UpdateEscape(Math.Min((float)delta,.1f));
        UpdateInteractionCursor();UpdateRain((float)delta);
        if (heldCursor != null)
        {
            heldCursor.Position = GetViewport().GetMousePosition() - heldCursor.Size / 2;
            if (heldCursor.Icon == null && heldItem is { } held && thumbnails.TryGetValue(held.Entry.Path, out var icon)) { heldCursor.Icon = icon; heldCursor.QueueRedraw(); }
        }
    }
    public override void _ExitTree() { worldSession?.Dispose(); workstationFrames?.Dispose(); tvFrames?.Dispose(); watcher.Dispose(); bridge.Dispose(); shellImages.Dispose(); }
}
