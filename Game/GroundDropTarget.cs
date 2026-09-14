using Godot;
namespace CozyCave;

public partial class GroundDropTarget:Control
{
    public Func<Rect2>? Inside;
    public Action<string>? Dropped;
    public override bool _CanDropData(Vector2 at,Variant data)
    {
        if(Inside?.Invoke().HasPoint(GetGlobalTransform()*at)!=false || data.VariantType!=Variant.Type.Dictionary)return false;
        var d=data.AsGodotDictionary();if(!d.ContainsKey("buildkey"))return false;
        string key=d["buildkey"].AsString();return key.StartsWith("$slot:") || key.StartsWith("$hotbar:");
    }
    public override void _DropData(Vector2 at,Variant data)
    {if(_CanDropData(at,data))Dropped?.Invoke(data.AsGodotDictionary()["buildkey"].AsString());}
}
