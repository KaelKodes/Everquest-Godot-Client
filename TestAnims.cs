using Godot;
using System;

public partial class TestAnims : SceneTree
{
    public override void _Initialize()
    {
        try
        {
            var gltfDoc = new GltfDocument();
            var gltfState = new GltfState();
            byte[] glbBytes = FileAccess.GetFileAsBytes("res://Data/Characters/frm.glb");
            var err = gltfDoc.AppendFromBuffer(glbBytes, "", gltfState);
            if (err != Error.Ok)
            {
                GD.Print("Failed to load frm.glb");
                Quit();
                return;
            }

            Node scene = gltfDoc.GenerateScene(gltfState);
            if (scene == null)
            {
                GD.Print("Scene is null");
                Quit();
                return;
            }

            AnimationPlayer ap = FindPreviewAnimationPlayer(scene);
            if (ap == null)
            {
                GD.Print("NO ANIMATION PLAYER FOUND IN FRM.GLB!");
            }
            else
            {
                GD.Print("Animations found in frm.glb:");
                foreach (string name in ap.GetAnimationList())
                {
                    GD.Print("  - " + name);
                }
            }

            // Also test materials
            GD.Print("Checking materials:");
            CheckMaterials(scene);
        }
        catch (Exception e)
        {
            GD.PrintErr(e);
        }

        Quit();
    }

    private AnimationPlayer FindPreviewAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer ap) return ap;
        foreach (var child in node.GetChildren())
        {
            var found = FindPreviewAnimationPlayer(child);
            if (found != null) return found;
        }
        return null;
    }

    private void CheckMaterials(Node node)
    {
        if (node is MeshInstance3D meshInst)
        {
            GD.Print($"MeshInstance: {node.Name}, Surfaces: {meshInst.GetSurfaceOverrideMaterialCount()}");
            for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
            {
                var mat = meshInst.GetActiveMaterial(i);
                if (mat == null)
                {
                    GD.Print($"  Surface {i} ActiveMaterial is NULL");
                }
                else
                {
                    string resName = mat.ResourceName;
                    GD.Print($"  Surface {i} Material Type: {mat.GetType().Name}, ResourceName: '{resName}'");
                }
            }
        }

        foreach (var child in node.GetChildren())
        {
            CheckMaterials(child);
        }
    }
}
