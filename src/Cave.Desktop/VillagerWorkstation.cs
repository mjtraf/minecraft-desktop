using System.Speech.Recognition;
using System.Text;
using System.Text.Json;
namespace Cave.Desktop;

internal sealed class VillagerMemory
{
    public string? ThreadId {get;set;}
    public string Folder {get;set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Cozy Cave Work");
    public string Transcript {get;set;}="";
}
internal sealed partial class VillagerWorkstation:Form
{
    private readonly VillagerSession session=new();
    private readonly Cave.Transport.TvFrameBuffer frames=new();
    private readonly Action<string,object?> send;
    private readonly Action returnToCave;
    private readonly FileStream sessionLease;
    private readonly RichTextBox transcript=new() {Dock=DockStyle.Fill,ReadOnly=true,BackColor=Color.FromArgb(27,29,25),ForeColor=Color.FromArgb(235,231,209),BorderStyle=BorderStyle.None,Font=new Font("Consolas",11)};
    private readonly TextBox input=new() {Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Font=new Font("Segoe UI",11),BackColor=Color.FromArgb(238,233,213)};
    private readonly Label status=new() {Dock=DockStyle.Fill,Text="Ready · Full local access",ForeColor=Color.White,TextAlign=ContentAlignment.MiddleLeft};
    private readonly System.Windows.Forms.Timer timer=new() {Interval=250};
    private VillagerMemory memory=new();
    private SpeechRecognitionEngine? speech;
    private readonly StringBuilder spoken=new();
    private bool closing,suspended,listening,dirty;
    private int ticks;
    private DateTime micStarted;
    private JsonElement? approval;
    private readonly Queue<JsonElement> approvalQueue=[];
    private readonly Button approve=new() {Text="Allow requested action",AutoSize=true,Visible=false},deny=new() {Text="Decline",AutoSize=true,Visible=false};
    internal string Channel=>frames.Name;
    internal int CapturedFrames {get;private set;}
    internal bool Working=>session.Busy;
    protected override bool ShowWithoutActivation=>true;
    private string MemoryPath=>Path.Combine(Program.DataPath,"villager-agent.json");
    internal VillagerWorkstation(Action<string,object?> send,Action returnToCave)
    {
        Directory.CreateDirectory(Program.DataPath);sessionLease=new FileStream(Path.Combine(Program.DataPath,"villager.session.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        this.send=send;this.returnToCave=returnToCave;Text="Minecraft Desktop — Villager workstation";FormBorderStyle=FormBorderStyle.None;ClientSize=new Size(1000,680);MinimumSize=new Size(760,520);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(65,51,36);KeyPreview=true;
        try{if(File.Exists(MemoryPath))memory=JsonSerializer.Deserialize<VillagerMemory>(File.ReadAllText(MemoryPath))??new();}catch{if(File.Exists(MemoryPath+".bak"))memory=JsonSerializer.Deserialize<VillagerMemory>(File.ReadAllText(MemoryPath+".bak"))??new();}
        session.ThreadId=memory.ThreadId;transcript.Text=memory.Transcript.Length>0?memory.Transcript:"Your villager's Codex workstation\n\nType a task below, then Send or Ctrl+Enter. Choose Project folder for code or documents.\nIn the cave, aim at your nearby villager and hold V to dictate.\n\nEscape returns to the cave; your task continues. Stop interrupts it.\n";
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(12)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,43));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,106));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));Controls.Add(layout);
        var bar=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(bar,0,0);
        void Button(string text,Action action){var button=new Button {Text=text,AutoSize=true,Height=32,BackColor=Color.FromArgb(200,195,180),ForeColor=Color.Black};button.Click+=(_,_)=>action();bar.Controls.Add(button);}
        Button("Send",()=>_=Submit(input.Text));Button("Stop",()=>_=Stop());Button("Project folder",ChooseFolder);Button("Open folder",()=>{Directory.CreateDirectory(memory.Folder);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(memory.Folder){UseShellExecute=true});});Button("Sign in",()=>_=Login());Button("Back to cave",Return);
        layout.Controls.Add(transcript,0,1);layout.Controls.Add(input,0,2);
        var footer=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};status.Width=430;status.Dock=DockStyle.None;status.Height=32;footer.Controls.Add(status);footer.Controls.Add(approve);footer.Controls.Add(deny);layout.Controls.Add(footer,0,3);
        approve.Click+=(_,_)=>_=Answer(true);deny.Click+=(_,_)=>_=Answer(false);
        input.KeyDown+=(_,e)=>{if(e.Control && e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;_=Submit(input.Text);}};
        KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){e.SuppressKeyPress=true;Return();}};
        FormClosing+=(_,e)=>{if(!closing){e.Cancel=true;Return();}};
        _=Handle;CreateControl();foreach(Control c in Controls)c.CreateControl();
        session.Event+=(kind,text)=>{if(!closing && IsHandleCreated)try{BeginInvoke(()=>Receive(kind,text));}catch(InvalidOperationException){}};
        ShowInTaskbar=false;Location=new Point(-20000,-20000);StartPosition=FormStartPosition.Manual;Show();PerformLayout();
        Resize+=(_,_)=>{if(WindowState==FormWindowState.Minimized){WindowState=FormWindowState.Normal;Return();}};
        timer.Tick+=(_,_)=>Tick();timer.Start();
    }
    private void Append(string text){transcript.AppendText(text);transcript.SelectionStart=transcript.TextLength;transcript.ScrollToCaret();dirty=true;}
    private void Report(string text){status.Text=text;send("villager-status",new {text,working=session.Busy,listening});}
    private void Receive(string kind,string text)
    {
        switch(kind)
        {
            case "delta":Append(text);break;
            case "output":Append(text);break;
            case "activity":Append("\n["+text+"]\n");break;
            case "thread":memory.ThreadId=text;SaveMemory();break;
            case "status":Report(text);if(!session.Busy){Append("\n");SaveMemory();}break;
            case "error":Append("\nError: "+text+"\n");Report("Needs attention — open workstation");break;
            case "approval":
                using(var doc=JsonDocument.Parse(text)){if(approval==null)approval=doc.RootElement.Clone();else approvalQueue.Enqueue(doc.RootElement.Clone());}
                Append("\nInput requested:\n"+text+"\n");ShowApproval();break;
        }
    }
    private void ShowApproval(){approve.Visible=deny.Visible=approval!=null;if(approval is {} request)approve.Text=request.GetProperty("method").GetString()!.Contains("requestUserInput")?"Answer questions":"Allow requested action";Report("Needs your input — open workstation");}
    private async Task Answer(bool allow)
    {
        if(approval is not {} request)return;
        string method=request.GetProperty("method").GetString()!;
        try
        {
            if(method.Contains("requestUserInput"))
            {
                var answers=new Dictionary<string,object>();
                foreach(var q in request.GetProperty("parameters").GetProperty("questions").EnumerateArray())
                {
                    string answer="";
                    if(allow)
                    {
                        var choices=q.TryGetProperty("options",out var options)?options.EnumerateArray().Select(o=>o.GetProperty("label").GetString()??"").ToArray():[];
                        var value=await PromptInWorld(q.GetProperty("header").GetString()??"Question",q.GetProperty("question").GetString()??"","",choices,q.TryGetProperty("isSecret",out var secret)&&secret.GetBoolean());
                        if(value==null)return;answer=value;
                    }
                    answers[q.GetProperty("id").GetString()!]=new {answers=new[]{answer}};
                }
                await session.Respond(request.GetProperty("id"),new {answers});
            }
            else if(method.Contains("requestApproval"))await session.Respond(request.GetProperty("id"),new {decision=allow?"accept":"decline"});
            else if(method.Contains("elicitation"))await session.Respond(request.GetProperty("id"),new {action="cancel"});
            else {Report("This prompt needs a supported response. Stop the task and clarify your instruction.");return;}
            approval=approvalQueue.TryDequeue(out var next)?next:null;if(approval!=null)ShowApproval();else{approve.Visible=deny.Visible=false;Report(allow?"Working":"Action declined");}
        }catch(Exception e){Report(e.Message);}
    }
    internal async Task Submit(string text)
    {
        text=text.Trim();if(text.Length==0)return;if(session.Busy){Report("Already working — Stop before sending another task");return;}
        try{Directory.CreateDirectory(memory.Folder);Append("\nYou: "+text+"\n\nVillager: ");input.Clear();Report("Connecting to Codex…");await session.Send(text,memory.Folder);Report("Working");SaveMemory();}
        catch(Exception e){Append("\n"+e.Message+"\n");input.Text=text;Report("Could not start — check sign-in and retry");}
    }
    private async Task Stop(){try{await session.Stop();Report("Stopping…");}catch(Exception e){Report(e.Message);}}
    private async Task Login(){try{await session.Login();Report("Complete sign-in in your browser");}catch(Exception e){Report(e.Message);}}
    private async void ChooseFolder()
    {
        if(session.Busy){Report("Stop or finish the task before changing project");return;}
        var path=await PromptInWorld("Project folder","Enter the full path to the folder the villager should work in.",memory.Folder);
        if(path==null)return;
        try{if(!Path.IsPathFullyQualified(path))throw new IOException("Enter a full folder path.");Directory.CreateDirectory(path);memory.Folder=Path.GetFullPath(path);SaveMemory();Report("Project folder updated");}
        catch(Exception e){Report("Project folder: "+e.Message);}
    }
    internal void OpenMonitor(){if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;ShowInTaskbar=true;if(Location.X< -10000){var area=Screen.PrimaryScreen!.WorkingArea;Location=new Point(area.X+Math.Max(0,(area.Width-Width)/2),area.Y+Math.Max(0,(area.Height-Height)/2));}Show();Activate();input.Focus();}
    internal void ParkMonitor(){ShowInTaskbar=false;Location=new Point(-20000,-20000);}
    private void Return(){EndInWorld();ParkMonitor();returnToCave();}
    internal void SetSuspended(bool value){suspended=value;if(value)CancelSpeech();}
    internal void StartSpeech()
    {
        if(listening || suspended)return;
        try
        {
            if(speech==null)
            {
                var recognizer=SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r=>r.Culture.TwoLetterISOLanguageName=="en")??SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault()??throw new InvalidOperationException("No Windows speech recognizer installed. Type at the workstation or install Windows speech recognition.");
                speech=new SpeechRecognitionEngine(recognizer);speech.LoadGrammar(new DictationGrammar());
                speech.SpeechRecognized+=(_,e)=>{if(e.Result.Confidence>=.35f)spoken.Append(e.Result.Text+" ");};
                speech.RecognizeCompleted+=(_,_)=>{if(closing)return;BeginInvoke(()=>{bool deliver=listening;listening=false;speech.SetInputToNull();string text=spoken.ToString().Trim();if(deliver && text.Length>0){input.Text=text;send("villager-dictation",new {text});Report("Review your voice request");}else Report("Ready — no speech captured");});};
            }
            spoken.Clear();speech.SetInputToDefaultAudioDevice();listening=true;micStarted=DateTime.UtcNow;speech.RecognizeAsync(RecognizeMode.Multiple);Report("Listening — release V to review");
        }catch(Exception e){listening=false;try{speech?.SetInputToNull();}catch{}Report("Microphone: "+e.Message);}
    }
    internal void StopSpeech(){if(listening)speech?.RecognizeAsyncStop();}
    internal void CancelSpeech(){listening=false;try{speech?.RecognizeAsyncCancel();}catch{} }
    private void SaveMemory()
    {
        try{Directory.CreateDirectory(Program.DataPath);memory.Transcript=transcript.Text;memory.ThreadId=session.ThreadId;File.WriteAllText(MemoryPath+".tmp",JsonSerializer.Serialize(memory));if(File.Exists(MemoryPath))File.Replace(MemoryPath+".tmp",MemoryPath,MemoryPath+".bak");else File.Move(MemoryPath+".tmp",MemoryPath);dirty=false;}catch(Exception e){Report("Agent history save failed: "+e.Message);}
    }
    private void Tick()
    {
        if(closing)return;if(listening && DateTime.UtcNow-micStarted>TimeSpan.FromSeconds(60))StopSpeech();
        if(++ticks%30==0 && dirty)SaveMemory();if(suspended)return;
        try
        {
            using var image=new Bitmap(Width,Height);DrawToBitmap(image,new Rectangle(Point.Empty,Size));
            DrawRemoteCaret(image);
            using var scaled=new Bitmap(image,new Size(1000,680));using var bytes=new MemoryStream();scaled.Save(bytes,System.Drawing.Imaging.ImageFormat.Jpeg);frames.Write(bytes.ToArray());CapturedFrames++;
        }catch(Exception e){if(ticks%40==0)Program.Log("Villager monitor: "+e.Message);}
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing && !closing){closing=true;timer.Stop();CancelSpeech();speech?.Dispose();SaveMemory();session.Dispose();frames.Dispose();timer.Dispose();sessionLease.Dispose();}
        base.Dispose(disposing);
    }
}
