namespace Cave.Core;
public static class PlacementFacing
{
    // Godot yaw 0 looks north (-Z). Vanilla stairs point east in their base model;
    // chests have their latch on +Z and should face back toward the player.
    public static float Rotation(string kind, float yawDegrees, float manualTurn = 0)
    {
        float snapped = MathF.Round(yawDegrees / 90, MidpointRounding.AwayFromZero) * 90;
        float rotation = snapped + (kind.EndsWith("_stairs", StringComparison.Ordinal) ? 90 : 0) + manualTurn;
        return (rotation % 360 + 360) % 360;
    }
}
