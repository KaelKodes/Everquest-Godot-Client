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

        string baseName = Path.GetFileNameWithoutExtension(textureName);

        // 1) Prefer the bundled, pre-converted PNG inside the .pck. ResourceLoader.Load
        //    is the only reliable way to access this in packaged builds — File.Exists
        //    on a res:// disk path will return false because the original PNG is not
        //    written to disk, only the imported .ctex is bundled.
        //
        //    NOTE: this path is checked BEFORE the EQAssetConfig check so packaged
        //    clients that ship their own ConvertedEffects don't need a configured
        //    EQ install to render spell particles.
        string resPath = "res://Data/ConvertedEffects/" + baseName + ".png";
        if (Godot.ResourceLoader.Exists(resPath))
        {
            try
            {
                var tex = GD.Load<Texture2D>(resPath);
                if (tex != null)
                {
                    _textureCache[textureName] = tex;
                    return tex;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SpellEffectManager] Error loading {resPath}: {ex.Message}");
            }
        }

        // 2) Fall back to the original .dds in the configured EQ install. Use the
        //    Image instance loader (image.Load(path)) — the static Image.LoadFromFile
        //    only supports PNG/JPG/WEBP/SVG/TGA and returns Error 15 on .dds.
        var config = EQAssetConfig.Instance;
        if (!config.IsConfigured)
        {
            GD.PrintErr($"[SpellEffectManager] Could not find particle texture '{textureName}' in ConvertedEffects (EQ install path not configured for .dds fallback).");
            _textureCache[textureName] = null;
            return null;
        }

        string[] diskPaths = new[]
        {
            Path.Combine(config.EQPath, "SpellEffects", textureName),
            Path.Combine(config.EQPath, "ActorEffects", textureName)
        };

        foreach (var p in diskPaths)
        {
            if (File.Exists(p))
            {
                try
                {
                    var image = new Image();
                    if (image.Load(p) == Error.Ok)
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

        GD.PrintErr($"[SpellEffectManager] Could not find particle texture: {textureName} in ConvertedEffects, SpellEffects or ActorEffects.");

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
