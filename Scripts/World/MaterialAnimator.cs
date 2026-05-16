using Godot;
using System.Collections.Generic;
using System.IO;
using System.Threading;

public partial class MaterialAnimator : Node
{
    private class AnimRecord
    {
        public BaseMaterial3D Material;
        public Texture2D[] Frames;
        public float DelaySec;
        public float Timer;
        public int CurrentFrame;
    }

    private readonly List<AnimRecord> _activeAnimations = new();
    private static int s_missingFrameWarnCount;

    public override void _Ready()
    {
        // Zone / UI code may pause the main tree; prop texture cycling must keep ticking.
        ProcessMode = ProcessModeEnum.Always;
    }

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
    /// Works for <see cref="StandardMaterial3D"/> and <see cref="ORMMaterial3D"/> (glTF imports often use ORM).
    /// </summary>
    public void RegisterMaterial(BaseMaterial3D mat, string[] textureNames, float delayMs, string texturesDir)
    {
        if (mat == null || textureNames == null || textureNames.Length == 0) return;

        // Don't register the same material twice
        foreach (var existing in _activeAnimations)
        {
            if (existing.Material == mat) return;
        }

        var loadedFrames = new List<Texture2D>();
        foreach (var texName in textureNames)
        {
            Texture2D tex = null;
            foreach (var dir in EnumerateTextureSearchDirs(texturesDir))
            {
                tex = LoadFrameTexture(dir, texName);
                if (tex != null) break;
            }
            if (tex != null)
                loadedFrames.Add(tex);
        }

        if (loadedFrames.Count > 0)
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
        }
        else if (Interlocked.Increment(ref s_missingFrameWarnCount) <= 12)
        {
            string matLabel = string.IsNullOrEmpty(mat.ResourceName) ? "(unnamed material)" : mat.ResourceName;
            GD.PrintErr(
                $"[MaterialAnimator] No frame PNGs loaded for '{matLabel}' (expected under Objects/Textures or Objects/textures near Lantern export). " +
                $"Tried frames: {string.Join(", ", textureNames)}. Update LanternExtractor or clear zone cache and re-extract.");
        }
    }

    public void ClearAll()
    {
        _activeAnimations.Clear();
    }

    private static IEnumerable<string> EnumerateTextureSearchDirs(string texturesDir)
    {
        if (string.IsNullOrWhiteSpace(texturesDir)) yield break;
        yield return texturesDir;

        // Parent is usually …/Objects — try lowercase "textures" (some exports / copies differ).
        string parent = Path.GetDirectoryName(texturesDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(parent)) yield break;
        yield return Path.Combine(parent, "textures");
    }

    private static Texture2D LoadFrameTexture(string texturesDir, string texName)
    {
        string path = Path.Combine(texturesDir, $"{texName}.png");
        var tex = EQAssetCache.Instance.LoadTexture(path);
        if (tex != null) return tex;

        if (!Directory.Exists(texturesDir)) return null;
        string lower = $"{texName}.png".ToLowerInvariant();
        foreach (var file in Directory.GetFiles(texturesDir, "*.png"))
        {
            if (Path.GetFileName(file).ToLowerInvariant() == lower)
                return EQAssetCache.Instance.LoadTexture(file);
        }
        return null;
    }
}
