using Godot;
using System;

public partial class TestAnimPaths : SceneTree
{
    public override void _Initialize()
    {
        var gltfDoc = new GltfDocument();
        var gltfState = new GltfState();
        using var file = FileAccess.Open("d:/Kael Kodes/EQMUD/eqmud/Data/Characters/kem_face1.glb", FileAccess.ModeFlags.Read);
        byte[] glbBytes = file.GetBuffer((long)file.GetLength());
        gltfDoc.AppendFromBuffer(glbBytes, "", gltfState);
        Node scene = gltfDoc.GenerateScene(gltfState);
        
        Skeleton3D skeleton = FindSkeleton(scene);
        if (skeleton != null)
        {
            GD.Print("SKELETON FOUND AT: " + scene.GetPathTo(skeleton));
            for (int i = 0; i < Math.Min(10, skeleton.GetBoneCount()); i++)
            {
                GD.Print("BONE: " + skeleton.GetBoneName(i));
            }
        }
        else
        {
            GD.Print("NO SKELETON FOUND!");
        }
        Quit();
    }

    private Skeleton3D FindSkeleton(Node node)
    {
        if (node is Skeleton3D skel) return skel;
        foreach (var child in node.GetChildren())
        {
            var result = FindSkeleton(child);
            if (result != null) return result;
        }
        return null;
    }
}
