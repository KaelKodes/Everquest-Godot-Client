using Godot;
public partial class DdsTest : SceneTree
{
    public override void _Initialize()
    {
        var img = Image.LoadFromFile(@"D:\everquest_rof2\everquest_rof2\Resources\Sky\Satellite-Moon.dds");
        if (img == null || img.IsEmpty()) GD.PrintErr("[TEST] Failed to load DDS.");
        else GD.Print($"[TEST] DDS loaded successfully: {img.GetWidth()}x{img.GetHeight()}");
        Quit();
    }
}
