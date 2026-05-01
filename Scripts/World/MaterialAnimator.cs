using Godot;
using System.Collections.Generic;

public partial class MaterialAnimator : Node
{
    private class AnimRecord
    {
        public StandardMaterial3D Material;
        public Texture2D[] Frames;
        public float DelaySec;
        public float Timer;
        public int CurrentFrame;
    }

    private readonly List<AnimRecord> _activeAnimations = new();

    public override void _Process(double delta)
    {
        float fDelta = (float)delta;
        foreach (var record in _activeAnimations)
        {
            if (record.Material == null || !GodotObject.IsInstanceValid(record.Material) || record.Frames.Length == 0)
                continue;

            record.Timer += fDelta;
            if (record.Timer >= record.DelaySec)
            {
                record.Timer = 0f;
                record.CurrentFrame = (record.CurrentFrame + 1) % record.Frames.Length;
                record.Material.AlbedoTexture = record.Frames[record.CurrentFrame];
            }
        }
    }

    /// <summary>
    /// Registers a material for animation. Finds and loads the texture frames from the specified directory.
    /// </summary>
    public void RegisterMaterial(StandardMaterial3D mat, string[] textureNames, float delayMs, string texturesDir)
    {
        // Don't register the same material twice
        foreach (var existing in _activeAnimations)
        {
            if (existing.Material == mat) return;
        }

        var loadedFrames = new List<Texture2D>();
        foreach (var texName in textureNames)
        {
            // The texture frames are stored as PNGs
            string path = System.IO.Path.Combine(texturesDir, $"{texName}.png");
            var tex = EQAssetCache.Instance.LoadTexture(path);
            if (tex != null)
            {
                loadedFrames.Add(tex);
            }
        }

        if (loadedFrames.Count > 1)
        {
            _activeAnimations.Add(new AnimRecord
            {
                Material = mat,
                Frames = loadedFrames.ToArray(),
                DelaySec = delayMs / 1000f,
                Timer = 0f,
                CurrentFrame = 0
            });
            // Immediately apply the first frame to sync it
            mat.AlbedoTexture = loadedFrames[0];
            
            // GD.Print($"[MaterialAnimator] Registered animation for {mat.ResourceName} with {loadedFrames.Count} frames.");
        }
    }

    public void ClearAll()
    {
        _activeAnimations.Clear();
    }
}
