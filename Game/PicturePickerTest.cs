using Godot;
namespace CozyCave;
public partial class Main
{
    private async Task RunPicturePickerTest(string output)
    {
        var results = new List<string>();
        void Check(bool ok, string label) => results.Add((ok ? "PASS " : "FAIL ") + label);
        try
        {
            System.IO.Directory.CreateDirectory(output);
            await Frames(10);
            void LateEnter() => bridge.Messages.Enqueue(System.Text.Json.JsonSerializer.SerializeToElement(new{command="entered"}));
            bool PointerVisible()=>Input.MouseMode==Input.MouseModeEnum.Visible && !walking;
            desktopHotbarActive=false;BeginPlacement("stone",null);
            FocusTelevision();LateEnter();await Frames(4);
            Check(tvFocused && PointerVisible(),"Late desktop enter cannot capture TV cursor in blocks mode");
            Check(softwareCursorActive && interactionCursor is {Visible:true,MouseFilter:Control.MouseFilterEnum.Ignore},"TV cursor is rendered in scene without intercepting clicks");
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"tv-cursor.png"));
            var tvRotation=camera.Rotation;
            _UnhandledInput(new InputEventMouseMotion {Relative=new Vector2(30,20)});
            Check(camera.Rotation==tvRotation,"Moving TV cursor does not rotate the player view");
            CaveFocusLost();ResumeCaveFocus();LateEnter();await Frames(4);
            Check(tvFocused && PointerVisible(),"Focus loss and return retain TV interaction and visible cursor");
            Enter();StartWalking();Check(tvFocused && PointerVisible(),"All walking entry paths respect TV interaction");
            HandleTvInput(new InputEventKey {Keycode=Key.Escape,Pressed=true});
            Check(!tvFocused && walking && Input.MouseMode==Input.MouseModeEnum.Captured,"Escape explicitly returns from TV to walking");
            var frame=new Cave.Core.Decoration("item_frame",0,0);state.Decorations.Add(frame);ShowPictureFrame("$decor:"+frame.Id);
            var originalPanel = panel;LateEnter();await Frames(4);
            Check(editingFrame!=null && PointerVisible(),"Picture frame panel keeps cursor visible after desktop enter");
            string? selected = null;
            PickPicture(p => selected = p);
            await Frames(15);
            Check(pictureDialog is {Visible:true, UseNativeDialog:false}, "Picture browser opens without Windows shell");
            LateEnter();CaveFocusLost();ResumeCaveFocus();await Frames(4);
            Check(pictureDialog is {Visible:true} && PointerVisible(),"Picture chooser retains cursor through focus and enter messages");
            Check(softwareCursorActive && Descendants(pictureDialog!).OfType<InteractionCursor>().Any(c=>c.Visible),"Picture chooser renders its own cursor");
            var downloads = pictureDialog!.CurrentDir;
            Check(System.IO.Directory.Exists(downloads), "Initial Downloads directory is available");
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output,"picker.png"));
            Input.ParseInputEvent(new InputEventKey {Keycode=Key.Escape,Pressed=true});
            await Frames(4);
            Input.ParseInputEvent(new InputEventKey {Keycode=Key.Escape,Pressed=false});
            await Frames(4);
            Check(pictureDialog==null && panel==originalPanel && !walking && selected==null, "Escape cancels only picker, retaining frame panel");
            var path=System.IO.Path.Combine(output,"photo 日本語.png");
            using(var img=Image.CreateEmpty(8,8,false,Image.Format.Rgb8)) {img.Fill(Colors.Red);img.SavePng(path);}
            var before=System.IO.File.ReadAllBytes(path);
            PickPicture(p=>selected=p,path);await Frames(4);
            pictureDialog!.EmitSignal(FileDialog.SignalName.FileSelected,path);await Frames(3);
            Check(selected==path && pictureDialog==null && LoadPicture(path)!=null,"Unicode picture selection loads successfully");
            Check(before.SequenceEqual(System.IO.File.ReadAllBytes(path)),"Source image remains unchanged");
            ClosePanel();StartWalking();PickPicture(_=>{});LateEnter();await Frames(4);
            Check(panel==null && pictureDialog is {Visible:true} && PointerVisible(),"Standalone picture chooser also blocks mouse capture");
            pictureDialog!.EmitSignal(FileDialog.SignalName.Canceled);await Frames(2);
            Check(walking && Input.MouseMode==Input.MouseModeEnum.Captured,"Walking resumes when chooser is closed");
            await Frames(3);Check(!softwareCursorActive && interactionCursor is {Visible:false},"Walking restores default cursors and hides software pointer");
        }
        catch(Exception e) {results.Add("FAIL "+e);}
        System.IO.File.WriteAllLines(System.IO.Path.Combine(output,"results.txt"),results);
        GetTree().Quit(results.Any(r=>r.StartsWith("FAIL"))?1:0);
    }
}
