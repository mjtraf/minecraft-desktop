using Godot;
using Cave.Core;
namespace CozyCave;
public partial class Main
{
    private bool notebookOpen,notebookChecklist;
    private NotePage? activeNotePage;
    private TextEdit? activeNoteText;
    private LineEdit? activeNoteTitle;
    private readonly List<(NoteTask Task,LineEdit Text,CheckBox Check)> activeNoteTasks=[];
    private void FlushNotebookEdits()
    {
        if(!notebookOpen || activeNotePage==null)return;
        if(activeNoteText!=null && IsInstanceValid(activeNoteText))activeNotePage.Text=activeNoteText.Text;
        if(activeNoteTitle!=null && IsInstanceValid(activeNoteTitle))activeNotePage.Title=activeNoteTitle.Text;
        foreach(var row in activeNoteTasks)if(IsInstanceValid(row.Text) && IsInstanceValid(row.Check)){row.Task.Text=row.Text.Text;row.Task.Done=row.Check.ButtonPressed;}
    }
    private int notebookPage;
    private float notebookSaveDelay;
    private Label? notebookStatus;
    private void BuildWritingDesk()
    {
        Furnish("barrel",1,0,6);Furnish("barrel",3,0,6);
        Furnish("spruce_planks",2,0,6);
        Furnish("dark_oak_stairs",2,0,5,90);
        Lantern(new Vector3(3,1,6),true);
        if(!state.NotebookItemCreated)
        {
            state.NotebookItemCreated=true;state.Decorations.Add(new Decoration("writable_book",2,6){Id="desk-notebook",Y=1.02f});Changed();
        }
    }
    private void BookVisual(Node3D node)
    {
        var cover=Material("6b3324");var pages=Material("eadcb3");var edge=Material("ae9a72");
        Box(node,new Vector3(0,.015f,0),new Vector3(.55f,.03f,.68f),cover);
        Box(node,new Vector3(.014f,.075f,0),new Vector3(.48f,.09f,.60f),pages);
        Box(node,new Vector3(0,.135f,0),new Vector3(.55f,.03f,.68f),cover);
        Box(node,new Vector3(-.258f,.077f,0),new Vector3(.04f,.13f,.68f),cover);
        foreach(float y in new[]{.047f,.071f,.095f}) Box(node,new Vector3(.259f,y,0),new Vector3(.008f,.008f,.59f),edge);
        // The original item artwork remains on the cover; the body has real depth.
        node.AddChild(new MeshInstance3D {Mesh=new QuadMesh {Size=new Vector2(.49f,.60f)},Position=new Vector3(0,.152f,0),RotationDegrees=new Vector3(-90,0,0),MaterialOverride=new StandardMaterial3D {AlbedoTexture=Vanilla("item/writable_book"),TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest,Transparency=BaseMaterial3D.TransparencyEnum.AlphaScissor,CullMode=BaseMaterial3D.CullModeEnum.Disabled}});
        var quill=new Node3D {Position=new Vector3(.12f,.18f,-.05f),RotationDegrees=new Vector3(0,-32,-12)};node.AddChild(quill);
        Box(quill,Vector3.Zero,new Vector3(.016f,.018f,.42f),Material("d8c597"));
        for(int i=0;i<5;i++) Box(quill,new Vector3(0,.014f,-.04f-i*.035f),new Vector3(.075f-i*.01f,.024f,.06f),Material("fff1cf"));
    }    private void NoteChanged()
    {
        Changed();notebookSaveDelay=.65f;if(notebookStatus!=null) notebookStatus.Text="Saving...";
    }
    private void UpdateNotebook(float dt)
    {
        if(!notebookOpen || notebookSaveDelay<=0) return;
        notebookSaveDelay-=dt;if(notebookSaveDelay>0) return;
        Save();if(notebookStatus!=null) notebookStatus.Text=dirty?"Save failed - try closing again":"Saved locally";
    }
    private void ShowNotebook()
    {
        backgroundApp=false;ClosePanel(false);walking=false;Input.MouseMode=Input.MouseModeEnum.Visible;
        if(bridge.Connected) bridge.Send(new{command="enter"});
        state.Notebook ??=[];if(state.Notebook.Count==0) state.Notebook.Add(new());
        notebookPage=Math.Clamp(notebookPage,0,state.Notebook.Count-1);var page=state.Notebook[notebookPage];
        notebookOpen=true;activeNotePage=page;activeNoteText=null;activeNoteTitle=null;activeNoteTasks.Clear();
        var root=new Control {Theme=uiTheme};layer.AddChild(root);root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);panel=root;
        var shade=new ColorRect {Color=new Color(0,0,0,.6f)};root.AddChild(shade);shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center=new CenterContainer();root.AddChild(center);center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        float scale=Mathf.Min(1,(GetViewport().GetVisibleRect().Size.Y-50)/546);
        var layout=new HBoxContainer();layout.AddThemeConstantOverride("separation",20);center.AddChild(layout);
        var wrapper=new Control {CustomMinimumSize=new Vector2(444,546)*scale};layout.AddChild(wrapper);
        var sheet=new Control {Size=new Vector2(444,546),Scale=Vector2.One*scale};wrapper.AddChild(sheet);
        sheet.AddChild(new TextureRect {Texture=new AtlasTexture {Atlas=Vanilla("gui/book"),Region=new Rect2(20,0,148,182)},Size=new Vector2(444,546),TextureFilter=Control.TextureFilterEnum.Nearest,MouseFilter=Control.MouseFilterEnum.Ignore});
        var title=new LineEdit {Name="NoteTitle",Text=page.Title,Position=new Vector2(43,32),Size=new Vector2(348,38),MaxLength=80};sheet.AddChild(title);activeNoteTitle=title;
        title.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());title.AddThemeColorOverride("font_color",new Color("43301e"));title.TextChanged+=s=>{page.Title=s;NoteChanged();};
        if(!notebookChecklist)
        {
            var text=new TextEdit {Name="NoteText",Text=page.Text,PlaceholderText="Write something to remember...",Position=new Vector2(43,86),Size=new Vector2(348,404),WrapMode=TextEdit.LineWrappingMode.Boundary};sheet.AddChild(text);activeNoteText=text;
            text.AddThemeColorOverride("font_placeholder_color",new Color("857460"));text.AddThemeFontOverride("font",MinecraftFont());text.AddThemeFontSizeOverride("font_size",18);text.AddThemeColorOverride("font_color",new Color("43301e"));text.AddThemeColorOverride("caret_color",new Color("43301e"));text.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());text.AddThemeStyleboxOverride("focus",new StyleBoxEmpty());
            text.TextChanged+=()=>{page.Text=text.Text;NoteChanged();};
        }
        else
        {
            var scroll=new ScrollContainer {Position=new Vector2(40,86),Size=new Vector2(355,404),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};sheet.AddChild(scroll);var list=new VBoxContainer {SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};scroll.AddChild(list);
            foreach(var task in page.Tasks)
            {
                var row=new HBoxContainer();list.AddChild(row);
                var check=new CheckBox {Name="NoteCheck",ButtonPressed=task.Done,TooltipText="Mark complete"};row.AddChild(check);check.Toggled+=done=>{task.Done=done;NoteChanged();};
                var entry=new LineEdit {Text=task.Text,PlaceholderText="Task",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(190,36)};row.AddChild(entry);activeNoteTasks.Add((task,entry,check));
                entry.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());entry.AddThemeColorOverride("font_color",new Color("43301e"));entry.TextChanged+=s=>{task.Text=s;NoteChanged();};
                var remove=Button("x",()=>{page.Tasks.Remove(task);NoteChanged();ShowNotebook();});remove.TooltipText="Remove task";row.AddChild(remove);
            }
        }
        var tools=new VBoxContainer {CustomMinimumSize=new Vector2(190,0)};tools.AddThemeConstantOverride("separation",12);layout.AddChild(tools);
        var heading=new Label {Text="Book & Quill"};heading.AddThemeColorOverride("font_color",Colors.White);tools.AddChild(heading);
        var notes=Button("Notes",()=>{notebookChecklist=false;ShowNotebook();});notes.Disabled=!notebookChecklist;notes.AddThemeStyleboxOverride("disabled",Style("b0b0b0","eeeeee"));notes.AddThemeColorOverride("font_disabled_color",new Color("383838"));tools.AddChild(notes);
        var checklist=Button("Checklist",()=>{notebookChecklist=true;ShowNotebook();});checklist.Disabled=notebookChecklist;checklist.AddThemeStyleboxOverride("disabled",Style("b0b0b0","eeeeee"));checklist.AddThemeColorOverride("font_disabled_color",new Color("383838"));tools.AddChild(checklist);
        if(notebookChecklist) tools.AddChild(Button("+ Add task",()=>{page.Tasks.Add(new());NoteChanged();ShowNotebook();}));
        var space=new Control {SizeFlagsVertical=Control.SizeFlags.ExpandFill};tools.AddChild(space);
        var pageLabel=new Label {Text=$"Page {notebookPage+1} of {state.Notebook.Count}"};pageLabel.AddThemeColorOverride("font_color",Colors.White);tools.AddChild(pageLabel);
        var navigation=new HBoxContainer();tools.AddChild(navigation);
        var back=Button("<",()=>{notebookPage--;ShowNotebook();});back.Disabled=notebookPage==0;navigation.AddChild(back);
        var next=Button(">",()=>{notebookPage++;ShowNotebook();});next.Disabled=notebookPage==state.Notebook.Count-1;navigation.AddChild(next);
        tools.AddChild(Button("+ Page",()=>{state.Notebook.Add(new());notebookPage=state.Notebook.Count-1;NoteChanged();ShowNotebook();}));
        tools.AddChild(Button("Done",ClosePanelAndResume));
        notebookStatus=new Label {Text=dirty?"Unsaved changes":"Saved locally"};notebookStatus.AddThemeColorOverride("font_color",Colors.White);tools.AddChild(notebookStatus);
    }
}
