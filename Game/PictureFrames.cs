using Godot;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private string? editingFrame;
    private FileDialog? pictureDialog;
    private void PickPicture(Action<string> selected, string? previousPath = null)
    {
        if (pictureDialog != null) return;
        bool resumeWalking=walking && panel==null && !tvFocused;
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var downloads = System.IO.Path.Combine(home, "Downloads");
        if (OperatingSystem.IsWindows())
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            if (key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") is string configured)
                downloads = System.Environment.ExpandEnvironmentVariables(configured);
        }
        // Avoid Explorer shell extensions / thumbnail providers while browsing photos.
        // The in-engine list reads directories directly and only decodes the selected image.
        var start = previousPath == null ? downloads : System.IO.Path.GetDirectoryName(previousPath);
        if (!System.IO.Directory.Exists(start)) start = home;
        var pickerTheme = (Theme)uiTheme.Duplicate();
        pickerTheme.SetColor("font_color", "Label", new Color("eee5cf"));
        var dialog = new FileDialog {
            Theme = pickerTheme, Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile, UseNativeDialog = false,
            DisplayMode = FileDialog.DisplayModeEnum.List, CurrentDir = start,
            Filters = ["*.png,*.jpg,*.jpeg,*.webp,*.bmp ; Pictures"],
            Exclusive = true, Title = "Choose a picture"
        };
        pictureDialog = dialog; walking=false;Input.MouseMode=Input.MouseModeEnum.Visible;AddChild(dialog);
        AddInteractionCursor(dialog,()=>softwareCursorActive && pictureDialog==dialog);
        var shortcuts = new HBoxContainer(); dialog.GetVBox().AddChild(shortcuts); dialog.GetVBox().MoveChild(shortcuts, 0);
        void Shortcut(string label, string path) => shortcuts.AddChild(Button(label, () => {
            if (System.IO.Directory.Exists(path)) dialog.CurrentDir = path;
            else Toast("That folder is currently unavailable.");
        }));
        Shortcut("Downloads", downloads);
        Shortcut("Pictures", System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures));
        Shortcut("Desktop", System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory));
        Shortcut("Home", home);
        void Finish() { pictureDialog = null; dialog.Hide(); dialog.QueueFree(); if(resumeWalking && !PointerInteractionOpen)StartWalking(); }
        dialog.FileSelected += path => { Finish(); selected(path); };
        dialog.Canceled += Finish;
        dialog.PopupCenteredRatio(.8f);
    }
    private readonly Dictionary<string,(DateTime Changed,Texture2D Texture)> photoCache=[];
    private Texture2D SupplyIcon(string kind,string? id) => kind=="item_frame" && state.Decorations.FirstOrDefault(d=>d.Id==id) is {} frame ? LoadPicture(frame.PicturePath)??ItemIcon(kind) : ItemIcon(kind);
    private static bool IsPicture(string path)=>new[]{".png",".jpg",".jpeg",".webp",".bmp"}.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());
    private Texture2D? LoadPicture(string? path)
    {
        if(path==null || !System.IO.File.Exists(path) || !IsPicture(path)) return null;
        try
        {
            var changed=System.IO.File.GetLastWriteTimeUtc(path);
            if(photoCache.TryGetValue(path,out var cached) && cached.Changed==changed)return cached.Texture;
            using var image=Image.LoadFromFile(path);if(image.IsEmpty()) return null;
            int longest=Math.Max(image.GetWidth(),image.GetHeight());
            if(longest>1024) image.Resize(Math.Max(1,image.GetWidth()*1024/longest),Math.Max(1,image.GetHeight()*1024/longest),Image.Interpolation.Lanczos);
            var texture=ImageTexture.CreateFromImage(image);if(photoCache.Count>=32)photoCache.Clear();photoCache[path]=(changed,texture);return texture;
        }
        catch(Exception e) {GD.PushWarning("Picture unavailable: "+e.Message);return null;}
    }
    private void CreatePictureVisual(Node3D node,Decoration frame)
    {
        BlockVisual(node,"item_frame",Vector3.Zero);
        var texture=LoadPicture(frame.PicturePath);
        if(texture==null) return;
        float aspect=texture.GetWidth()/(float)texture.GetHeight();
        var size=aspect>=1?new Vector2(.61f,.61f/aspect):new Vector2(.61f*aspect,.61f);
        node.AddChild(new MeshInstance3D {Name="Photo",Position=new Vector3(0,.5f,.42f),RotationDegrees=new Vector3(0,180,0),Mesh=new QuadMesh {Size=size},MaterialOverride=new StandardMaterial3D {AlbedoTexture=texture,CullMode=BaseMaterial3D.CullModeEnum.Disabled,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,Transparency=BaseMaterial3D.TransparencyEnum.AlphaScissor}});
    }
    private void CreateTabletopFrame(Node3D node,Decoration frame)
    {
        var face=new Node3D {Name="StandingFrame",Scale=Vector3.One*.8f,Position=new Vector3(0,-.1f,-.376f)};node.AddChild(face);CreatePictureVisual(face,frame);
        Box(node,new Vector3(0,.035f,.055f),new Vector3(.46f,.07f,.23f),TextureMaterial("birch_planks"));
    }
    private void FrameImage(string path)
    {
        var texture=LoadPicture(path);if(texture==null){Toast("Choose an available PNG, JPG, WEBP or BMP image.");return;}
        var frame=new Decoration("item_frame",0,0) {Carried=true,PicturePath=System.IO.Path.GetFullPath(path)};
        state.Decorations.Add(frame);ClearHeld();
        DropHotbarItem(frame.Id,state.SelectedSlot);Changed();SelectHotbar(state.SelectedSlot);
        Toast("Photo frame ready: aim at a wall or block top and right-click to place it. Your file stays in place.");
    }
    private void PickFrameImage()
    {
        var image=heldItem?.Entry??selectedEntry;
        if(image is {IsDirectory:false} && IsPicture(image.Path)) {FrameImage(image.Path);return;}
        PickPicture(FrameImage);
    }
    private void HandleFileDrop(string[] paths)
    {
        if(editingFrame!=null){SetFramePicture(editingFrame,paths.FirstOrDefault()??"");return;}
        if(openedChest!=null){try{foreach(var path in paths)library.Add(path,openedChest);Changed();ShowChest(openedChest);}catch(Exception e){Toast(e.Message);}return;}
        var picture=paths.FirstOrDefault(IsPicture);
        if(picture!=null){FrameImage(picture);return;}
        Toast("Drop an image to make a photo frame, or open a chest to add files.");
    }
    private void SetFramePicture(string id,string path)
    {
        var frame=state.Decorations.FirstOrDefault(d=>"$decor:"+d.Id==id && d.Kind=="item_frame");
        if(frame==null) return;
        if(LoadPicture(path)==null) {Toast("Choose an available PNG, JPG, WEBP or BMP image.");return;}
        frame.PicturePath=System.IO.Path.GetFullPath(path);Changed();RemovePlacedObject(id);CreateDecoration(frame);ShowPictureFrame(id);
    }
    private void ShowPictureFrame(string id)
    {
        var frame=state.Decorations.FirstOrDefault(d=>"$decor:"+d.Id==id && d.Kind=="item_frame");if(frame==null) return;
        var box=OpenPanel("Item frame","Choose a photo, or drag an image file into this screen. The original stays in place.");editingFrame=id;
        var preview=new TextureRect {Texture=LoadPicture(frame.PicturePath),CustomMinimumSize=new Vector2(600,320),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered};box.AddChild(preview);
        box.AddChild(new Label {Text=frame.PicturePath==null?"No picture selected":preview.Texture==null?"Picture unavailable. Choose it again to reconnect.":System.IO.Path.GetFileName(frame.PicturePath)});
        var row=new HBoxContainer();box.AddChild(row);
        row.AddChild(Button("Choose picture...",()=>PickPicture(path=>SetFramePicture(id,path),frame.PicturePath)));
        row.AddChild(Button("Refresh",()=>{RemovePlacedObject(id);CreateDecoration(frame);ShowPictureFrame(id);}));
        row.AddChild(Button("Remove picture",()=>{frame.PicturePath=null;Changed();RemovePlacedObject(id);CreateDecoration(frame);ShowPictureFrame(id);}));
        box.AddChild(new Label {Text="Aim at a block top to stand the frame up, or a wall to hang it. Hold left-click to collect it with its photo."});
    }
}

public partial class PictureDropButton:Button
{
    public Action<string>? FrameImage;
    public override bool _CanDropData(Vector2 at,Variant data)
    {
        if(data.VariantType!=Variant.Type.Dictionary)return false;var d=data.AsGodotDictionary();
        return d.ContainsKey("path") && new[]{".png",".jpg",".jpeg",".webp",".bmp"}.Contains(System.IO.Path.GetExtension(d["path"].AsString()).ToLowerInvariant());
    }
    public override void _DropData(Vector2 at,Variant data)=>FrameImage?.Invoke(data.AsGodotDictionary()["path"].AsString());
}
