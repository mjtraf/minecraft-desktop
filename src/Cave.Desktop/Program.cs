using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Win32;

namespace Cave.Desktop;
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if(args.Contains("--agent-desktop")) {ApplicationConfiguration.Initialize();AgentDesktop.Run(args[Array.IndexOf(args,"--agent-desktop")+1]);return;}
        if(args.Contains("--villager-test")) {testData=Path.GetFullPath(args[Array.IndexOf(args,"--villager-test")+1]);ApplicationConfiguration.Initialize();VillagerTests.Run(testData);return;}
        if(args.Contains("--search-test")) {ApplicationConfiguration.Initialize();SearchTests.Run(args[Array.IndexOf(args,"--search-test")+1]);return;}
        if(args.Contains("--tv-test")) {var output=args[Array.IndexOf(args,"--tv-test")+1];testData=Path.Combine(output,"profile");ApplicationConfiguration.Initialize();TvTests.Run(output,args.Contains("--youtube"));return;}
        if(args.Contains("--border-test")) {ApplicationConfiguration.Initialize();BorderTests.Run(args[Array.IndexOf(args,"--border-test")+1]);return;}
        if(args.Contains("--dock-test")) {ApplicationConfiguration.Initialize();DockTests.Run(args[Array.IndexOf(args,"--dock-test")+1]);return;}
        if(args.Contains("--app-window-fixture"))
        {
            ApplicationConfiguration.Initialize();using var form=new Form {Text="Minecraft Desktop app fixture",Width=500,Height=300};
            using var reveal=new System.Windows.Forms.Timer {Interval=150};reveal.Tick+=(_,_)=>{Native.ShowWindow(form.Handle,5);form.Activate();reveal.Stop();};reveal.Start();Application.Run(form);return;
        }
        if (args.FirstOrDefault() == "--watchdog")
        {
            try { Process.GetProcessById(int.Parse(args[1])).WaitForExit(); } catch (ArgumentException) { }
            Recover(args[2], true); return;
        }
        int testIndex=Array.IndexOf(args,"--test-data");
        if(testIndex>=0 && testIndex+1<args.Length) testData=Path.Combine(Path.GetFullPath(args[testIndex+1]),"desktop-helper");
        if (args.FirstOrDefault() == "--desktop-state")
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(new { iconsVisible = Native.IsWindowVisible(Native.Icons()), taskbarVisible = Native.IsWindowVisible(Native.FindWindow("Shell_TrayWnd", null)) })); return;
        }
        if (args.FirstOrDefault() == "--restore") { Recover(RecoveryPath); return; }
        if (args.FirstOrDefault() == "--quit") { Recover(RecoveryPath, true); return; }
        using var mutex = new Mutex(true, "Local\\CaveDesktop-" + Environment.UserName + (testData==null?"":"-"+Path.GetFileName(Path.GetDirectoryName(testData))), out bool owned);
        if (!owned) return;
        ApplicationConfiguration.Initialize();
        Recover(RecoveryPath);
        Application.Run(new DesktopContext(args));
    }
    private static string? testData;
    internal static string DataPath => testData ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CozyCave");
    internal static string RecoveryPath => Path.Combine(DataPath, "desktop-recovery.json");
    internal static void Recover(string path, bool closeRenderer = false)
    {
        try {TaskbarState.Restore(Path.Combine(Path.GetDirectoryName(path)!,"taskbar-state.json"));}
        catch(Exception e) {Log("Taskbar recovery: "+e.Message);}
        try
        {
            if (!File.Exists(path)) return;
            using var state = JsonDocument.Parse(File.ReadAllText(path));
            Native.ShowWindow(Native.Icons(), state.RootElement.GetProperty("iconsVisible").GetBoolean() ? 5 : 0);
            if (closeRenderer && state.RootElement.TryGetProperty("rendererHwnd", out var rendererHwnd) && state.RootElement.TryGetProperty("rendererPid", out var rendererPid))
            {
                var hwnd = (nint)rendererHwnd.GetInt64(); Native.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == rendererPid.GetUInt32()) Native.PostMessage(hwnd, 0x0010, 0, 0);
            }
            File.Delete(path);
        }
        catch (Exception e) { Log("Recovery: " + e.Message); }
    }
    internal static void Log(string text)
    {
        try { Directory.CreateDirectory(DataPath); File.AppendAllText(Path.Combine(DataPath, "desktop.log"), DateTime.Now.ToString("O") + " " + text + Environment.NewLine); } catch { }
    }
}
internal sealed class DesktopContext : ApplicationContext
{
    private CaveTv? television;
    private VillagerWorkstation? villagerWorkstation;
    private readonly RecoveryDispatcher dispatcher = new() { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Opacity = 0 };
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 600 };
    private readonly NamedPipeServerStream pipe;
    private Cave.Transport.PipeOutbox? writer;
    private Process? renderer;
    private nint window, host;
    private bool active, paused, locked, restored, closing, iconsVisible, background;
    private string monitor = "";
    private DateTime entered;
    private readonly string[] launchArgs;
    private readonly bool windowed;
    private readonly DateTime started = DateTime.UtcNow;
    private readonly int smokeSeconds;
    private int testStep;
    private readonly Stopwatch latencyClock=new();
    private readonly List<long> latencySamples=[];
    private bool latencyExpected;
    internal DesktopContext(string[] args)
    {
        launchArgs = args; windowed = args.Contains("--windowed");
        smokeSeconds = GetArg("--smoke-seconds") is { } seconds ? int.Parse(seconds) : 0;
        _ = dispatcher.Handle;dispatcher.Recover=Restore;
        if(!Native.RegisterHotKey(dispatcher.Handle,0xCA,0x4003,0x7B)) Program.Log("Recovery shortcut unavailable; tray Restore remains available.");
        tray = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application, Text = "Minecraft Desktop", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Enter cave", null, (_, _) => Enter());
        menu.Items.Add("Pause / resume", null, (_, _) => { paused = !paused; Send("pause", new { value = paused }); });
        var dockMenu=new ToolStripMenuItem("Persistent hotbar") {Checked=true,CheckOnClick=true};
        dockMenu.CheckedChanged+=(_,_)=>{dockEnabled=dockMenu.Checked;if(!dockEnabled) {previews?.Close();dock?.ReleaseBar();TaskbarState.Restore(TaskbarRecovery);}};menu.Items.Add(dockMenu);
        var borderPreference=Path.Combine(Program.DataPath,"window-borders.json");
        try {if(File.Exists(borderPreference))bordersEnabled=JsonSerializer.Deserialize<bool>(File.ReadAllText(borderPreference));}catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException) {Program.Log("Border preference: "+e.Message);}
        appBorders.Enabled=bordersEnabled;
        var borderMenu=new ToolStripMenuItem("Minecraft app borders") {Checked=bordersEnabled,CheckOnClick=true};
        borderMenu.CheckedChanged+=(_,_)=>{bordersEnabled=borderMenu.Checked;appBorders.Enabled=bordersEnabled && !locked && !paused && !restored;if(!appBorders.Enabled)appBorders.HideAll();
            try {Directory.CreateDirectory(Program.DataPath);File.WriteAllText(borderPreference+".tmp",JsonSerializer.Serialize(bordersEnabled));File.Move(borderPreference+".tmp",borderPreference,true);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){Program.Log("Border preference save: "+e.Message);}
        };menu.Items.Add(borderMenu);
        menu.Items.Add("Restore desktop", null, (_, _) => Restore());
        menu.Items.Add("Attach cave to desktop", null, (_, _) => { restored = false; Attach(); });
        if(args.Contains("--tv-smoke")){bordersEnabled=false;appBorders.Enabled=false;}
        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = StartupEnabled() };
        startup.CheckedChanged += (_, _) => SetStartup(startup.Checked); menu.Items.Add(startup);
        menu.Items.Add("Quit", null, (_, _) => Close()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Enter();
        var pipeName = "CozyCave-" + Guid.NewGuid().ToString("N");
        pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _ = Listen();
        try
        {
            var engine = GetArg("--engine"); var project = GetArg("--project");
            var exe = engine ?? Path.Combine(AppContext.BaseDirectory, "Cave.exe");
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = project ?? AppContext.BaseDirectory };
            if (engine != null && project != null) { psi.ArgumentList.Add("--path"); psi.ArgumentList.Add(project); }
            psi.ArgumentList.Add("--"); psi.ArgumentList.Add("--pipe"); psi.ArgumentList.Add(pipeName);
            if (args.Contains("--proof")) psi.ArgumentList.Add("--proof");
            foreach (var flag in new[] { "--capture", "--test-data", "--tv-smoke" })
                if (GetArg(flag) is { } value) { psi.ArgumentList.Add(flag); psi.ArgumentList.Add(value); }
            if(GetArg("--villager-scene-test") is {} villagerTest){psi.ArgumentList.Add("--villager-test");psi.ArgumentList.Add(villagerTest);}
            renderer = Process.Start(psi)!;
            Program.Log("Renderer started pid=" + renderer.Id);
            var watch = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            watch.ArgumentList.Add("--watchdog"); watch.ArgumentList.Add(Environment.ProcessId.ToString()); watch.ArgumentList.Add(Program.RecoveryPath);
            Process.Start(watch);
        }
        catch (Exception e) { Program.Log(e.ToString()); MessageBox.Show(e.Message, "Minecraft Desktop could not start"); dispatcher.BeginInvoke(Close); }
        timer.Tick += (_, _) => Tick(); timer.Start();
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }
    private string? GetArg(string key) { int i = Array.IndexOf(launchArgs, key); return i >= 0 && i + 1 < launchArgs.Length ? launchArgs[i + 1] : null; }
    private Screen Screen => System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == monitor) ?? System.Windows.Forms.Screen.PrimaryScreen!;
    private async Task Listen()
    {
        try
        {
            await pipe.WaitForConnectionAsync();
            writer = new Cave.Transport.PipeOutbox(pipe);
            using var reader = new StreamReader(pipe);
            while (await reader.ReadLineAsync() is { } line)
            {
                var doc = JsonDocument.Parse(line); var message = doc.RootElement.Clone(); doc.Dispose();
                dispatcher.BeginInvoke(() => Message(message));
            }
        }
        catch (Exception e) { if (!closing) Program.Log("Pipe: " + e.Message); }
        finally {if(!closing && !dispatcher.IsDisposed) dispatcher.BeginInvoke(()=>{if(!closing) Restore();});}
    }
    private readonly AppWindows appWindows=new();
    private AppWindows.Entry[] cachedApps=[];
    private bool refreshingApps;
    private async void RefreshApps()
    {
        if(refreshingApps || closing) return;refreshingApps=true;
        try {var entries=await Task.Run(()=>appWindows.List(window));if(!closing) {cachedApps=entries;appBorders.Update(entries,window);Send("apps",entries);}}
        catch(Exception e) {Program.Log("App refresh: "+e.Message);}
        finally {refreshingApps=false;}
    }
    private readonly AppWindowBorders appBorders=new();
    private bool bordersEnabled=true;
    private DesktopDock? dock;
    private WindowPreviews? previews;
    private DateTime previewOpened;
    private bool previewExplicit;
    private bool dockEnabled=true;
    private Form? systemPopup;
    private DateTime searchLaunchUntil;
    private bool searchLaunchPending;
    private string TaskbarRecovery=>Path.Combine(Program.DataPath,"taskbar-state.json");
    private void OpenSystem(string action)
    {
        if(action=="desktop") {Enter();return;}
        if(action=="search")
        {
            if(searchLaunchPending)return;
            searchLaunchPending=true;
            systemPopup?.Dispose();systemPopup=null;Background();
            searchLaunchUntil=DateTime.UtcNow.AddSeconds(2);
            _ = LaunchSearch();return;
        }
        if(action=="system")
        {
            systemPopup?.Dispose();systemPopup=new SystemPanel(Restore,a=>{if(a=="desktop") Enter();else SystemHub.Action(a);});
            systemPopup.StartPosition=FormStartPosition.Manual;var area=Screen.WorkingArea;
            systemPopup.Location=new Point(Math.Max(area.Left,Math.Min(area.Right-systemPopup.Width,area.Left+(area.Width-systemPopup.Width)/2)),Math.Max(area.Top,area.Bottom-systemPopup.Height-6));
            systemPopup.Show();systemPopup.Activate();return;
        }
        SystemHub.Action(action);
    }
    private async Task LaunchSearch()
    {
        try
        {
        // Coalesce launch requests and wait for the initiating gesture to release.
        await Task.Delay(200);
        for(int i=0;i<40 && new[]{0x01,0x02,0x11,0x12,0x10,0x5B,0x5C}.Any(k=>(Native.GetAsyncKeyState(k)&0x8000)!=0);i++)await Task.Delay(50);
        if(closing || new[]{0x01,0x02,0x11,0x12,0x10,0x5B,0x5C}.Any(k=>(Native.GetAsyncKeyState(k)&0x8000)!=0))return;
        var foreground=Native.GetForegroundWindow();Native.GetWindowThreadProcessId(foreground,out var pid);
        try {if(Process.GetProcessById((int)pid).ProcessName.Equals("SearchHost",StringComparison.OrdinalIgnoreCase))return;}catch(ArgumentException){}
        // Hand input to Explorer before invoking its flyout, rather than sending
        // a system chord while Godot still owns the foreground/captured input.
        var taskbar=Native.FindWindow("Shell_TrayWnd",null);
        if(taskbar!=0){Native.ShowWindow(taskbar,5);Native.SetForegroundWindow(taskbar);}
        await Task.Delay(120);if(closing)return;
        SystemHub.Action("windows-search");Program.Log("Windows Search launched via Win+S");
        }
        finally {searchLaunchPending=false;}
    }
    private void DockAction(string action,int index)
    {
        if(action is "search" or "system" or "desktop" or "notifications") {OpenSystem(action);return;}
        if(action=="context" && dock is {Apps:true}) {ShowAppMenu(dock.Slots.ElementAtOrDefault(index)?.Windows??[]);return;}
        if(action=="hide") {dockEnabled=false;dock?.ReleaseBar();TaskbarState.Restore(TaskbarRecovery);return;}
        if(action=="activate" && dock is {Apps:true} && dock.Slots.ElementAtOrDefault(index)?.Windows is {Length:1} ids)
        {
            Background();if(!appWindows.Activate(ids[0])) Send("status",new{text="That window is no longer available."});return;
        }
        if(action=="activate" && dock is {Apps:false}) Enter();
        Send("dock-action",new{action,index});
    }
    private void ShowPreviews(string[] ids,bool focus=false)
    {
        if(systemPopup is {IsDisposed:false,Visible:true}) return;
        var entries=cachedApps.Where(a=>!a.Pinned && ids.Contains(a.Id)).ToArray();if(entries.Length==0) return;
        previews?.Dispose();previews=new WindowPreviews(entries,id=>{Background();appWindows.Activate(id);},()=>{if(Native.GetForegroundWindow()==window) Send("dock-action",new{action="dismiss",index=0});},CloseAppWindow);
        var area=Screen.WorkingArea;previews.StartPosition=FormStartPosition.Manual;
        previews.Location=new Point(area.Left+(area.Width-previews.Width)/2,(dock?.Visible==true?dock.Top:area.Bottom)-previews.Height-6);
        previewOpened=DateTime.UtcNow;previewExplicit=focus;previews.Show();if(focus) previews.Activate();
    }

    private void CloseAppWindow(string id)
    {
        if(!appWindows.CloseWindow(id)) Send("status",new{text="That window is unavailable or Windows refused the close request."});
        RefreshApps();
    }
    private async void CloseApps(string[] ids)
    {
        foreach(var id in ids.Distinct()) CloseAppWindow(id);
        // WM_CLOSE is asynchronous; refresh after applications have processed it.
        await Task.Delay(350);if(!closing) RefreshApps();
        await Task.Delay(900);if(!closing) RefreshApps();
    }
    private void ShowAppMenu(string[] ids)
    {
        previews?.Close();systemPopup?.Dispose();
        var menu=new AppWindowMenu(ids.Length,()=>CloseApps(ids),()=>{if(Native.GetForegroundWindow()==window) Send("dock-action",new{action="dismiss",index=0});});
        systemPopup=menu;var area=System.Windows.Forms.Screen.FromPoint(Cursor.Position).WorkingArea;
        menu.Location=new Point(Math.Clamp(Cursor.Position.X-menu.Width/2,area.Left,Math.Max(area.Left,area.Right-menu.Width)),Math.Clamp(Cursor.Position.Y-menu.Height-8,area.Top,Math.Max(area.Top,area.Bottom-menu.Height)));
        menu.Show();menu.Activate();
    }
    private void Message(JsonElement message)
    {
        var cmd = message.GetProperty("command").GetString();
        switch (cmd)
        {
            case "hello":
                window = (nint)message.GetProperty("hwnd").GetInt64();
                Send("desktop-process",new {pid=Environment.ProcessId});
                monitor = message.GetProperty("monitor").GetString() ?? "";
                Send("monitors", new { names = System.Windows.Forms.Screen.AllScreens.Select(s => s.DeviceName).ToArray() });
                if (!windowed) Attach();
                else Send("status", new { text = "Windowed mode • Desktop unchanged" });
                break;
            case "villager":
                try {
                villagerWorkstation??=new VillagerWorkstation(Send,()=>{Send("villager-return",null);Enter();});
                Send("villager-ready",new{channel=villagerWorkstation.Channel});
                switch(message.GetProperty("action").GetString()){case "open":Background();villagerWorkstation.OpenMonitor();break;case "send":_ = villagerWorkstation.Submit(message.GetProperty("text").GetString()??"");break;case "mic-start":villagerWorkstation.StartSpeech();break;case "mic-stop":villagerWorkstation.StopSpeech();break;case "mic-cancel":villagerWorkstation.CancelSpeech();break;}
                }catch(Exception e){Send("villager-status",new{text="Workstation unavailable: "+e.Message,working=false,listening=false});}
                break;
            case "tv-pointer": if(television!=null)_ = television.Pointer(message.GetProperty("u").GetDouble(),message.GetProperty("v").GetDouble(),message.GetProperty("click").GetBoolean(),message.TryGetProperty("phase",out var phase)?phase.GetString()??"":"");break;
            case "tv-input": if(television!=null)_ = television.Input(message.Clone());break;
            case "tv": if(message.GetProperty("action").GetString()=="browse")Background();television??=new CaveTv(Send);Send("tv-ready",new{channel=television.Channel});_ = television.Handle(message);break;
            case "system": OpenSystem(message.GetProperty("action").GetString()!);break;
            case "dock-state":
                if(launchArgs.Contains("--tv-smoke"))break;
                dock??=new DesktopDock(DockAction,ShowPreviews);dock.UpdateState(message);break;
            case "close-apps": CloseApps(message.GetProperty("ids").EnumerateArray().Select(x=>x.GetString()!).ToArray());break;
            case "app-context": ShowAppMenu(message.GetProperty("ids").EnumerateArray().Select(x=>x.GetString()!).ToArray());break;
            case "previews": ShowPreviews(message.GetProperty("ids").EnumerateArray().Select(x=>x.GetString()!).ToArray(),true);break;
            case "enter": Enter(); break;
            case "idle": if (!windowed && !restored) Attach(); break;
            case "background": Background(); break;
            case "focus-return": var focused=Native.GetForegroundWindow();if(focused==window || (Native.GetParent(window)!=0 && Native.Class(focused) is "Progman" or "WorkerW")) Enter();break;
            case "apps": RefreshApps(); break;
            case "activate-app": Background(); if(!appWindows.Activate(message.GetProperty("id").GetString()!)) Send("status",new {text="Could not switch to that window. Refresh Open apps and try again."}); break;
            case "monitor": monitor = message.GetProperty("value").GetString() ?? ""; Attach(); break;
            case "quit": Close(); break;
            case "restore": Restore(); break;
        }
    }
    private void Send(string command, object? payload = null)
    {
        try { writer?.Send(new { command, payload }); } catch (Exception e) when (e is IOException or ObjectDisposedException) { }
    }
    private void Attach()
    {
        if (window == 0 || !Native.IsWindow(window) || windowed || restored) return;
        active = false; background = false;
        host = Native.DesktopHost();
        if (host == 0) { FailAttach("Windows desktop host was not found."); return; }
        var style = (long)Native.GetStyle(window, -16);
        Native.SetStyle(window, -16, (nint)((style & ~0x80000000L & ~0x00C40000L) | 0x40000000L));
        Native.SetParent(window, host);
        if (Native.GetParent(window) != host) { FailAttach("Windows rejected desktop attachment."); return; }
        var bounds = Screen.Bounds; var origin = new Point(bounds.X, bounds.Y); Native.ScreenToClient(host, ref origin);
        Native.SetWindowPos(window, 0, origin.X, origin.Y, bounds.Width, bounds.Height, 0x0030 | 0x0040);
        if (!File.Exists(Program.RecoveryPath))
        {
            iconsVisible = Native.IsWindowVisible(Native.Icons());
            Directory.CreateDirectory(Program.DataPath);
            File.WriteAllText(Program.RecoveryPath, JsonSerializer.Serialize(new { iconsVisible, rendererHwnd = (long)window, rendererPid = renderer?.Id ?? 0 }));
        }
        Native.ShowWindow(Native.Icons(), 0);
        Send("attached");
        Program.Log($"Attached hwnd={window} host={host} class={Native.Class(host)} bounds={bounds}");
    }
    private void Enter()
    {
        if(DateTime.UtcNow<searchLaunchUntil)return;
        if (window == 0 || !Native.IsWindow(window)) return;
        background=false;dock?.Hide();
        if(active && Native.GetParent(window)==0 && Native.GetForegroundWindow()==window) { Send("entered"); return; }
        if (!windowed && Native.GetParent(window)!=0)
        {
            Native.SetParent(window, 0);
            var style = (long)Native.GetStyle(window, -16);
            Native.SetStyle(window, -16, (nint)((style & ~0x40000000L) | 0x80000000L));
            var r = Screen.WorkingArea;if(dock?.Registered==true) r.Height=Math.Max(r.Height,dock.Bottom-r.Top);
            Native.SetWindowPos(window, 0, r.X, r.Y, r.Width, r.Height, 0x0060);
        }
        Native.ShowWindow(window, 5); Native.SetForegroundWindow(window);
        active = true; entered = DateTime.UtcNow; Send("entered");
        Program.Log("Entered foreground=" + Native.GetForegroundWindow());
    }
    private void Background()
    {
        // Keep the existing renderer window in place behind the newly focused app.
        // Reparenting here caused the visible disappearance during app launches.
        active=false; background=true; Send("release");
        if(Native.IsIconic(window)) Native.ShowWindow(window,4); // Restore without activation.
        Program.Log("Background retained parent="+Native.GetParent(window)+" visible="+Native.IsWindowVisible(window));
    }
    private void FailAttach(string reason)
    {
        Restore(); Native.ShowWindow(window, 5);
        Send("status", new { text = reason + " Using windowed diagnostic mode." });
        Program.Log(reason);
    }
    private void Restore()
    {
        appBorders.HideAll();systemPopup?.Close();dock?.ReleaseBar();previews?.Close();
        active = false; background=false; restored = true; Send("release");
        if (Native.IsWindow(window))
        {
            Native.SetParent(window, 0);
            Native.SetStyle(window, -16, (nint)(((long)Native.GetStyle(window, -16) & ~0x40000000L) | 0x00CF0000L));
            Native.SetWindowPos(window, 0, Screen.WorkingArea.X + 60, Screen.WorkingArea.Y + 60, 1120, 720, 0x0030);
        }
        Program.Recover(Program.RecoveryPath);
        Send("status", new { text = "Desktop restored • Cave is in a regular window" });
    }
    private Process? appFixture;
    private string? appFixtureWindow;
    private Form? backgroundTestWindow;
    private nint backgroundTestParent;
    private void Tick()
    {
        television?.SetSuspended(locked || paused || restored);villagerWorkstation?.SetSuspended(locked || paused || restored);
        if (closing) return;
        if (renderer?.HasExited == true) { Close(); return; }
        if (smokeSeconds > 0 && (DateTime.UtcNow - started).TotalSeconds > smokeSeconds) { Close(); return; }
        if (window == 0) return;
        if(launchArgs.Contains("--dock-latency-test") && dock is {Slots.Length:9})
        {
            timer.Interval=20;
            Send("visibility",new{hidden=true,locked=false});
            if(latencyClock.IsRunning && dock.Apps==latencyExpected) {latencySamples.Add(latencyClock.ElapsedMilliseconds);latencyClock.Reset();}
            if(latencySamples.Count==20) {Program.Log("CHECK hiddenDockRoundTrips="+latencySamples.Count+" maxMs="+latencySamples.Max()+" averageMs="+latencySamples.Average());Close();return;}
            if(!latencyClock.IsRunning) {latencyExpected=!dock.Apps;latencyClock.Start();DockAction("toggle",0);}
            return;
        }
        if(launchArgs.Contains("--dock-integration-test"))
        {
            if(dock==null) return;
            testStep++;
            if(testStep==2)
            {
                Program.Log("CHECK sharedNineSlots="+(dock.Slots.Length==9));
                Program.Log("CHECK sharedIcons="+dock.Slots.Any(s=>s.Icon!=null));
                using var image=new Bitmap(dock.Width,dock.Height);dock.DrawToBitmap(image,dock.ClientRectangle);image.Save(Path.Combine(Program.DataPath,"shared-dock.png"));
                DockAction("toggle",0);
            }
            if(testStep==5) {Program.Log("CHECK sharedAppMode="+dock.Apps);DockAction("toggle",0);}
            if(testStep==8) {Program.Log("CHECK sharedBlockMode="+!dock.Apps);Close();return;}
        }
        if(launchArgs.Contains("--background-test"))
        {
            testStep++;
            if(testStep==2) Enter();
            if(testStep==3)
            {
                backgroundTestParent=Native.GetParent(window);
                backgroundTestWindow=new Form {Text="Minecraft Desktop external app test",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(Screen.WorkingArea.X+100,Screen.WorkingArea.Y+100,500,350)};
                backgroundTestWindow.Show();backgroundTestWindow.Activate();Background();
                if(launchArgs.Contains("--apps-test")) appFixture=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--app-window-fixture") {UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden});
            }
            if(testStep==5)
            {
                if(launchArgs.Contains("--apps-test") && !appWindows.List(window).Any(a=>a.Name=="Minecraft Desktop app fixture") && (DateTime.UtcNow-started).TotalSeconds<30) {testStep--;return;}
                Program.Log("CHECK backgroundVisible="+(Native.IsWindowVisible(window)&&!Native.IsIconic(window)));
                Program.Log("CHECK backgroundParentStable="+(backgroundTestParent==0&&Native.GetParent(window)==backgroundTestParent));
                Program.Log("CHECK externalForeground="+(Native.GetForegroundWindow()==backgroundTestWindow!.Handle));
                Program.Log("CHECK backgroundMode="+background);
                if(launchArgs.Contains("--apps-test"))
                {
                    var apps=appWindows.List(window);var fixture=apps.FirstOrDefault(a=>a.Name=="Minecraft Desktop app fixture");appFixtureWindow=fixture?.Id;
                    Program.Log("CHECK appWindowListed="+(fixture!=null));
                    Program.Log("CHECK appWindowActivated="+(fixture!=null && appWindows.Activate(fixture.Id)));
                    Program.Log("CHECK appWindowForeground="+(fixture!=null && Native.GetForegroundWindow()==(nint)long.Parse(fixture.Id)));
                }
            }
            if(testStep==6) {if(appFixtureWindow!=null) Native.PostMessage((nint)long.Parse(appFixtureWindow),0x10,0,0);backgroundTestWindow?.Close();if(launchArgs.Contains("--focus-test")) Native.SetForegroundWindow(window);else Enter();}
            if(testStep==7) {if(launchArgs.Contains("--focus-test")) Program.Log("CHECK automaticFocusResume="+active);Program.Log("CHECK resumeTopLevel="+(Native.GetParent(window)==0));Attach();}
            if(testStep==8) {Program.Log("CHECK explicitDesktopReturn="+(Native.GetParent(window)==host));Close();return;}
        }
        if (launchArgs.Contains("--integration-test"))
        {
            testStep++;
            if (testStep == 2) Program.Log("CHECK attachment=" + (Native.GetParent(window) == host) + " iconsHidden=" + !Native.IsWindowVisible(Native.Icons()));
            if (testStep == 3)
            {
                // Send a click through the renderer's real Windows message queue.
                Native.PostMessage(window, 0x0201, 1, (nint)((350 << 16) | 350));
                Native.PostMessage(window, 0x0202, 0, (nint)((350 << 16) | 350));
            }
            if (testStep == 4) Program.Log("CHECK interactiveTopLevel=" + (Native.GetParent(window) == 0) + " foreground=" + (Native.GetForegroundWindow() == window));
            if (testStep == 5) Enter();
            if (testStep == 6)
            {
                Program.Log("CHECK repeatedEnterStable="+(Native.GetForegroundWindow()==window && Native.GetParent(window)==0));
                Native.PostMessage(window, 0x0100, 0x1B, 1);
            }
            if (testStep == 8) { Native.PostMessage(window, 0x0101, 0x1B, 1); }
            if (testStep == 9) { Program.Log("CHECK reattached=" + (Native.GetParent(window) == host)); Restore(); }
            if (testStep == 10) Program.Log("CHECK iconsRestored=" + (Native.IsWindowVisible(Native.Icons()) == iconsVisible));
            if (testStep == 11) { Close(); return; }
        }
        appBorders.Enabled=bordersEnabled && !locked && !paused && !restored;
        var fg = Native.GetForegroundWindow();
        if(dock!=null)
        {
            if(dockEnabled && !windowed && !restored && !locked && !paused)
            {
                dock.Reserve(Screen);if(dock.Registered) TaskbarState.Enable(TaskbarRecovery);
                bool full=fg!=window && fg!=dock.Handle && Native.GetWindowRect(fg,out var fullscreen) && fullscreen.Left<=Screen.Bounds.Left && fullscreen.Top<=Screen.Bounds.Top && fullscreen.Right>=Screen.Bounds.Right && fullscreen.Bottom>=Screen.Bounds.Bottom;
                if(fg!=window && Native.GetParent(fg)!=window && !full) {if(!dock.Visible) dock.Show();}else dock.Hide();
                if(full) previews?.Close();
            }
            else {dock.ReleaseBar();previews?.Close();TaskbarState.Restore(TaskbarRecovery);}
        }
        if(!previewExplicit && previews is {IsDisposed:false} && (DateTime.UtcNow-previewOpened).TotalSeconds>1.5 && !previews.Bounds.Contains(Cursor.Position) && !(dock?.Bounds.Contains(Cursor.Position)??false)) previews.Close();

        if(background && fg==window && !paused && !locked && !restored) Enter();
        if (active && fg != window && Native.GetParent(fg) != window && (DateTime.UtcNow - entered).TotalSeconds > 1)
        { Background(); }
        if (!windowed && !restored && !active && !background && (!Native.IsWindow(host) || Native.GetParent(window) != host)) Attach();
        var r = Screen.WorkingArea;
        bool covered = fg != window && fg != host && !Native.IsIconic(fg) && Native.GetWindowRect(fg, out var rect)
            && rect.Left <= r.Left && rect.Top <= r.Top && rect.Right >= r.Right && rect.Bottom >= r.Bottom;
        Send("visibility", new { hidden = covered && !active, locked });
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    { locked = e.Reason == SessionSwitchReason.SessionLock; if (locked) {appBorders.HideAll();Send("release");} }
    private void DisplayChanged(object? sender, EventArgs e)
    { dispatcher.BeginInvoke(() => { if (!active) Attach(); Send("monitors", new { names = System.Windows.Forms.Screen.AllScreens.Select(s => s.DeviceName).ToArray() }); }); }
    private static bool StartupEnabled()
    { using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"); return key?.GetValue("CozyCave") != null; }
    private void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        if (enabled) key.SetValue("CozyCave", "\"" + Environment.ProcessPath + "\" " + string.Join(" ", launchArgs.Select(a => "\"" + a.Replace("\"", "") + "\"")));
        else key.DeleteValue("CozyCave", false);
    }
    private void Close()
    {
        if (closing) return; closing = true;
        timer.Stop();villagerWorkstation?.Dispose();television?.Dispose();appBorders.Dispose();Native.UnregisterHotKey(dispatcher.Handle,0xCA);systemPopup?.Dispose();previews?.Dispose();dock?.Dispose(); backgroundTestWindow?.Dispose(); Send("quit");
        Program.Recover(Program.RecoveryPath);
        if (window != 0) Native.PostMessage(window, 0x0010, 0, 0);
        tray.Visible = false; tray.Dispose(); pipe.Dispose();
        SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        Program.Log("Closed; desktop restored"); ExitThread();
    }
}
