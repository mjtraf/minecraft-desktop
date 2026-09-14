using Godot;
namespace CozyCave;

// Rendered with the scene instead of relying on the Windows hardware cursor,
// which recording overlays can hide. This control never consumes mouse input.
public partial class InteractionCursor:Control
{
    public Func<bool>? Active;
    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;TextureFilter=TextureFilterEnum.Nearest;}
    public override void _Process(double delta)
    {
        var viewport=GetViewport();var at=viewport.GetMousePosition();
        Visible=Active?.Invoke()==true && viewport.GetVisibleRect().HasPoint(at);
        Position=at;QueueRedraw();
    }
    public override void _Draw()
    {
        if(Input.GetCurrentCursorShape()==Input.CursorShape.Ibeam)
        {
            DrawRect(new Rect2(1,0,11,3),Colors.Black);DrawRect(new Rect2(5,0,3,22),Colors.Black);DrawRect(new Rect2(1,19,11,3),Colors.Black);
            DrawLine(new Vector2(2,1),new Vector2(10,1),Colors.White);DrawLine(new Vector2(6,1),new Vector2(6,20),Colors.White);DrawLine(new Vector2(2,20),new Vector2(10,20),Colors.White);return;
        }
        DrawColoredPolygon([new(0,0),new(0,23),new(6,17),new(11,27),new(16,25),new(11,15),new(20,15)],Colors.Black);
        DrawColoredPolygon([new(2,4),new(2,18),new(7,13),new(12,24),new(13,23),new(8,13),new(15,13)],Colors.White);
    }
}
public partial class Main
{
    private InteractionCursor? interactionCursor;
    private ImageTexture? emptyCursor;
    private bool softwareCursorActive;
    private InteractionCursor AddInteractionCursor(Node parent,Func<bool> active)
    {
        var canvas=new CanvasLayer {Layer=1000};parent.AddChild(canvas);
        var cursor=new InteractionCursor {Active=active};canvas.AddChild(cursor);return cursor;
    }
    private void UpdateInteractionCursor()
    {
        bool active=PointerInteractionOpen && Input.MouseMode==Input.MouseModeEnum.Visible &&
            (GetWindow().HasFocus() || pictureDialog?.HasFocus()==true || selfTesting);
        if(active==softwareCursorActive)return;
        softwareCursorActive=active;
        if(active && emptyCursor==null){using var image=Image.CreateEmpty(2,2,false,Image.Format.Rgba8);image.Fill(Colors.Transparent);emptyCursor=ImageTexture.CreateFromImage(image);}
        // Override only Godot's cursors. Other Windows apps keep their own cursor.
        foreach(var shape in Enum.GetValues<Input.CursorShape>())
            Input.SetCustomMouseCursor(active?emptyCursor:null,shape);
    }
}
