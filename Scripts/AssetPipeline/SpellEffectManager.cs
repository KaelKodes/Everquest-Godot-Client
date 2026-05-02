using Godot;
using System;
using System.Collections.Generic;
using System.IO;

public partial class SpellEffectManager : RefCounted
{
    private static SpellEffectManager _instance;
    public static SpellEffectManager Instance => _instance ??= new SpellEffectManager();

    private Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _emitterLists = new Dictionary<string, List<string>>();

    public SpellEffectManager()
    {
        LoadEmittersManifest();
    }

    private void LoadEmittersManifest()
    {
        string path = "res://Data/emitters.json";
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PrintErr($"[SpellEffectManager] Could not find {path}");
            return;
        }

        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        string jsonStr = file.GetAsText();
        
        var json = new Json();
        var error = json.Parse(jsonStr);
        if (error != Error.Ok)
        {
            GD.PrintErr($"[SpellEffectManager] Failed to parse emitters.json");
            return;
        }

        var root = json.Data.AsGodotDictionary();
        foreach (var key in root.Keys)
        {
            string eddName = key.AsString();
            var list = root[key].AsGodotArray();
            var strList = new List<string>();
            foreach (var item in list)
            {
                strList.Add(item.AsString());
            }
            _emitterLists[eddName] = strList;
        }
        
        GD.Print($"[SpellEffectManager] Loaded {path} manifest.");
    }

    /// <summary>
    /// Gets a texture by filename (e.g. "Firesp501.dds"). 
    /// Searches both SpellEffects and ActorEffects directories.
    /// Returns a cached texture if already loaded.
    /// </summary>
    public Texture2D GetParticleTexture(string textureName)
    {
        if (string.IsNullOrEmpty(textureName)) return null;

        // Strip any path and lowercase for caching
        textureName = Path.GetFileName(textureName).ToLower();

        if (_textureCache.TryGetValue(textureName, out var cachedTex))
        {
            return cachedTex;
        }

        var config = EQAssetConfig.Instance;
        if (!config.IsConfigured)
        {
            GD.PrintErr("[SpellEffectManager] Cannot load texture - EQ path not configured.");
            return null;
        }

        string baseName = Path.GetFileNameWithoutExtension(textureName);
        
        // Potential paths to search
        string[] searchPaths = new[]
        {
            ProjectSettings.GlobalizePath("res://Data/ConvertedEffects/" + baseName + ".png"),
            Path.Combine(config.EQPath, "SpellEffects", textureName),
            Path.Combine(config.EQPath, "ActorEffects", textureName)
        };

        foreach (var p in searchPaths)
        {
            if (File.Exists(p))
            {
                try
                {
                    var image = Image.LoadFromFile(p);
                    if (image != null)
                    {
                        var tex = ImageTexture.CreateFromImage(image);
                        _textureCache[textureName] = tex;
                        return tex;
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[SpellEffectManager] Error loading {p}: {ex.Message}");
                }
            }
        }

        GD.PrintErr($"[SpellEffectManager] Could not find particle texture: {textureName} in SpellEffects or ActorEffects.");
        
        // Cache null to avoid repeatedly hitting the disk for missing files
        _textureCache[textureName] = null;
        return null;
    }

    /// <summary>
    /// For a given spell anim ID, try to get its primary texture.
    /// NOTE: This currently relies on the linear list from spellsnew.edd.
    /// </summary>
    public string GetPrimaryTextureForSpellAnim(int spellAnimId)
    {
        if (_emitterLists.TryGetValue("spellsnew.edd", out var list))
        {
            if (spellAnimId >= 0 && spellAnimId < list.Count)
            {
                return list[spellAnimId];
            }
        }
        return null;
    }
}
