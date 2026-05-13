using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Reads LanternExtractor's object_instances.txt and places extracted
/// GLB objects into the zone scene at their correct positions.
/// </summary>
public partial class ZoneObjectPlacer : RefCounted
{
    // Cache loaded object scenes to avoid re-loading duplicates (e.g., 50 trees)
    private readonly Dictionary<string, PackedScene> _objectSceneCache = new();
    /// <summary>Parsed MaterialLists/*.txt per object model — used after Instantiate so animated materials are the live instance materials.</summary>
    private readonly Dictionary<string, Dictionary<string, (string[] frames, float delay)>> _objectAnimDataCache = new();

    /// <summary>PEQ classic <c>IT66</c> style names → RoF2 <c>it10800</c> basenames from <c>Data/classic_tradeskill_mesh_redirect.json</c>.</summary>
    private static Dictionary<string, List<string>> _classicMeshRedirects;
    private static bool _classicMeshRedirectsLoaded;
    
    // Graphics Settings
    public bool ShadowsEnabled { get; set; } = true;
    public bool DisableLights { get; set; } = false;
    public MaterialAnimator Animator { get; set; }
    
    private JsonElement _lightConfigs;

    public ZoneObjectPlacer()
    {
        ReloadLightConfig();
    }

    public void ReloadLightConfig()
    {
        string path = "Data/object_lights.json";
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                _lightConfigs = doc.RootElement.Clone();
            }
            catch (Exception e)
            {
                GD.PrintErr($"Failed to load object_lights.json: {e.Message}");
            }
        }
        else
        {
            using var doc = JsonDocument.Parse(@"
{
  ""models"": {},
  ""campfire"":   { ""energy"": 18.0, ""range"": 20.0, ""position"": [0.0, 1.0, 0.0], ""color"": [1.00, 0.62, 0.32], ""sound"": ""fire1.wav"", ""soundVolume"": 0.60 },
  ""brazier"":    { ""energy"": 16.0, ""range"": 16.0, ""position"": [0.0, 1.6, 0.0], ""color"": [1.00, 0.58, 0.28], ""sound"": ""fire1.wav"", ""soundVolume"": 0.55 },
  ""torch"":      { ""energy"": 13.0, ""range"": 11.0, ""position"": [0.0, 1.4, 0.0], ""color"": [1.00, 0.64, 0.36], ""sound"": ""torch_lp.wav"", ""soundVolume"": 0.45 },
  ""lantern"":    { ""energy"": 10.0, ""range"": 10.0, ""position"": [0.0, 1.2, 0.0], ""color"": [1.00, 0.73, 0.48], ""sound"": ""torch_lp.wav"", ""soundVolume"": 0.35 },
  ""forge"":      { ""energy"": 20.0, ""range"": 18.0, ""position"": [0.0, 1.2, 0.0], ""color"": [1.00, 0.50, 0.22], ""sound"": ""fire1.wav"", ""soundVolume"": 0.70 },
  ""candelabra"": { ""energy"": 8.0,  ""range"": 8.5,  ""position"": [0.0, 1.0, 0.0], ""color"": [1.00, 0.78, 0.58], ""sound"": ""torch_lp.wav"", ""soundVolume"": 0.25 },
  ""chandelier"": { ""energy"": 12.0, ""range"": 13.0, ""position"": [0.0, 1.8, 0.0], ""color"": [1.00, 0.74, 0.52], ""sound"": ""torch_lp.wav"", ""soundVolume"": 0.40 },
  ""otherfire"":  { ""energy"": 12.0, ""range"": 12.0, ""position"": [0.0, 1.0, 0.0], ""color"": [1.00, 0.60, 0.30], ""sound"": ""fire1.wav"", ""soundVolume"": 0.45 }
}
");
            _lightConfigs = doc.RootElement.Clone();
            GD.Print("[ObjectPlacer] Data/object_lights.json not found; using built-in light defaults.");
        }
    }

    public static Vector3 EQToGodot(float x, float y, float z)
    {
        // Reverting to the mapping that matches the Zone Root Basis (Z, Y, -X)
        // With Z as EQ Height mapping to Godot X? No, let's keep it consistent.
        return new Vector3(-z, y, -x);
    }
    
    /// <summary>
    /// Place all objects defined in a zone's object_instances.txt.
    /// Returns the container node with all placed objects.
    /// </summary>
    public Node3D PlaceObjects(string zoneId, string cachePath, Node3D parent)
    {
        string instancesFile = Path.Combine(cachePath, "Zone", "object_instances.txt");
        string objectsDir = Path.Combine(cachePath, "Objects");

        if (!File.Exists(instancesFile))
        {
            GD.Print($"[ObjectPlacer] No object_instances.txt for zone '{zoneId}'");
            return null;
        }

        if (!Directory.Exists(objectsDir))
        {
            GD.Print($"[ObjectPlacer] No Objects directory for zone '{zoneId}'");
            return null;
        }

        var container = new Node3D();
        container.Name = "ZoneObjects";

        int placed = 0;
        int skipped = 0;
        var missingModels = new HashSet<string>();
        var lines = File.ReadAllLines(instancesFile);
        var pendingLights = new System.Collections.Generic.List<(Node3D Instance, string ModelName)>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

            var parts = line.Split(',');
            if (parts.Length < 10) continue;

            try
            {
                string modelName = parts[0].Trim();
                float posX = float.Parse(parts[1], CultureInfo.InvariantCulture);
                float posY = float.Parse(parts[2], CultureInfo.InvariantCulture);
                float posZ = float.Parse(parts[3], CultureInfo.InvariantCulture);
                float rotX = float.Parse(parts[4], CultureInfo.InvariantCulture);
                float rotY = float.Parse(parts[5], CultureInfo.InvariantCulture);
                float rotZ = float.Parse(parts[6], CultureInfo.InvariantCulture);
                float scaleX = float.Parse(parts[7], CultureInfo.InvariantCulture);
                float scaleY = float.Parse(parts[8], CultureInfo.InvariantCulture);
                float scaleZ = float.Parse(parts[9], CultureInfo.InvariantCulture);

                // Load the GLB model for this object
                var scene = GetObjectScene(modelName, objectsDir);
                if (scene == null)
                {
                    missingModels.Add(modelName);
                    skipped++;
                    continue;
                }

                // Instantiate and position
                var instance = scene.Instantiate<Node3D>();
                RegisterInstanceMaterialAnimations(instance, modelName, objectsDir);
                instance.Name = $"{modelName}_{placed}";

                // Use the previous working mapping
                Vector3 worldPos = EQToGodot(posX, posY, posZ);
                instance.Position = worldPos;
                
                // Positional axes were mapped as (-Z, Y, -X), which is a coordinate reflection.
                // Because space is reflected, we must invert all Euler angles (-rotX, -rotY, -rotZ).
                // The -90 Yaw offset aligns the objects to the physical base swap of the X and Z axes.
                instance.Rotation = new Vector3(
                    Mathf.DegToRad(-rotX), 
                    Mathf.DegToRad(-rotY - 90f), 
                    Mathf.DegToRad(-rotZ)
                );
                instance.Scale = new Vector3(scaleX, scaleY, scaleZ);

                container.AddChild(instance);
                pendingLights.Add((instance, modelName));
                placed++;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ObjectPlacer] Error placing object: {ex.Message}");
                skipped++;
            }
        }

        // Parent under zone geometry FIRST so GlobalTransform / ToGlobal match in-world coords.
        parent.AddChild(container);
        foreach (var (inst, mdl) in pendingLights)
            AddLightIfSource(inst, mdl, container, 0f, 0f, 0f);

        GD.Print($"[ObjectPlacer] Placed {placed} objects in '{zoneId}' ({skipped} skipped)");
        if (missingModels.Count > 0)
        {
            var sample = new List<string>(missingModels);
            sample.Sort(StringComparer.OrdinalIgnoreCase);
            int show = Math.Min(40, sample.Count);
            GD.Print($"[ObjectPlacer] Missing GLB under Objects/ ({missingModels.Count} unique): {string.Join(", ", sample.GetRange(0, show))}" +
                     (sample.Count > show ? $" … (+{sample.Count - show} more)" : ""));
        }

        return container;
    }

    /// <summary>
    /// Strips EQEmu/Lantern suffixes (e.g. <c>_ACTORDEF</c>) from mesh ids so they match on-disk GLB names.
    /// </summary>
    public static string NormalizeObjectMeshModelName(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return "";
        string n = modelName.Trim();
        const string suf = "_ACTORDEF";
        if (n.Length > suf.Length && n.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
            n = n[..^suf.Length];
        return n;
    }

    /// <summary>Player-facing name for known tradeskill placeables (PEQ classic <c>IT*</c> or RoF2 <c>it*</c> meshes).</summary>
    public static string GetTradeskillStationDisplayName(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return "World object";
        string k = NormalizeObjectMeshModelName(modelName).Trim();
        k = k.Replace(".glb", "", StringComparison.OrdinalIgnoreCase)
            .Replace(".eqg", "", StringComparison.OrdinalIgnoreCase)
            .Replace(".s3d", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
        return k switch
        {
            "it66" or "it10801" => "Forge",
            "it69" or "it10803" => "Oven",
            "it70" or "it11340" => "Brew Barrel",
            "it73" or "it10804" => "Kiln",
            "it74" or "it10800" => "Pottery Wheel",
            "it128" or "it10802" => "Loom",
            _ => FallbackWorldObjectLabel(NormalizeObjectMeshModelName(modelName)),
        };
    }

    private static string FallbackWorldObjectLabel(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return "World object";
        string s = normalized.Replace('_', ' ').Trim();
        if (s.Length == 0) return "World object";
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
    }

    /// <summary>Stable cache key for object GLBs (eqsage uses lowercase <c>itNNNNN.glb</c>).</summary>
    private static string GetObjectModelCacheKey(string modelName)
    {
        string n = NormalizeObjectMeshModelName(modelName);
        return string.IsNullOrEmpty(n) ? "" : n.ToLowerInvariant();
    }

    private static void EnsureClassicMeshRedirectsLoaded()
    {
        if (_classicMeshRedirectsLoaded) return;
        _classicMeshRedirectsLoaded = true;
        _classicMeshRedirects = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        const string path = "Data/classic_tradeskill_mesh_redirect.json";
        if (!File.Exists(path))
            return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Length > 0 && prop.Name[0] == '_')
                    continue;
                string key = NormalizeObjectMeshModelName(prop.Name);
                if (string.IsNullOrEmpty(key))
                    continue;
                var targets = new List<string>();
                switch (prop.Value.ValueKind)
                {
                    case JsonValueKind.String:
                    {
                        string s = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) targets.Add(s.Trim());
                        break;
                    }
                    case JsonValueKind.Array:
                    {
                        foreach (var el in prop.Value.EnumerateArray())
                        {
                            if (el.ValueKind != JsonValueKind.String) continue;
                            string s = el.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) targets.Add(s.Trim());
                        }
                        break;
                    }
                    default:
                        continue;
                }
                if (targets.Count > 0)
                    _classicMeshRedirects[key] = targets;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ObjectPlacer] Could not load {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Basename candidates for <c>.glb</c> lookup. EQEmu uses <c>IT10804</c>; RoF2 eqsage exports <c>it10804.glb</c>.
    /// </summary>
    private static List<string> BuildGlbBaseNameCandidates(string modelName)
    {
        EnsureClassicMeshRedirectsLoaded();
        var list = new List<string>();
        void Add(string b)
        {
            if (string.IsNullOrEmpty(b)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], b, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(b);
        }

        string n = NormalizeObjectMeshModelName(modelName);
        if (_classicMeshRedirects != null && _classicMeshRedirects.TryGetValue(n, out var redirected))
        {
            foreach (string r in redirected)
                Add(r);
        }

        Add(n);
        // Numeric tradeskill / item meshes: prefer lowercase it* to match eqsage/objects.
        if (n.Length >= 3 && n.StartsWith("IT", StringComparison.OrdinalIgnoreCase) && char.IsDigit(n[2]))
            Add("it" + n[2..]);
        Add(n.ToLowerInvariant());
        return list;
    }

    /// <summary>IT66 / IT10804 style meshes (digits only after IT).</summary>
    private static bool IsTradeskillNumericItMesh(string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName) || normalizedName.Length < 3) return false;
        if (!normalizedName.StartsWith("IT", StringComparison.OrdinalIgnoreCase)) return false;
        for (int i = 2; i < normalizedName.Length; i++)
        {
            if (!char.IsDigit(normalizedName[i])) return false;
        }
        return true;
    }

    private static void AddUniqueGlbPath(List<string> list, string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        foreach (string e in list)
        {
            if (string.Equals(e, path, StringComparison.OrdinalIgnoreCase)) return;
        }
        list.Add(path);
    }

    private static void CollectGlbPathsFromDir(string dir, string modelName, List<string> outList)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        foreach (string baseName in BuildGlbBaseNameCandidates(modelName))
        {
            string p = Path.Combine(dir, $"{baseName}.glb");
            if (File.Exists(p)) AddUniqueGlbPath(outList, p);
            p = FindFileCaseInsensitive(dir, $"{baseName}.glb");
            if (p != null) AddUniqueGlbPath(outList, p);
        }
    }

    /// <summary>
    /// Numeric IT* meshes: prefer RoF2 eqsage folder over zone Objects/ (zone copy can be corrupt or wrong casing).
    /// </summary>
    private static List<string> BuildOrderedGlbPaths(string objectsDir, string modelName)
    {
        var list = new List<string>();
        string n = NormalizeObjectMeshModelName(modelName);
        string sharedObj = EQAssetCache.Instance.GetSharedTradeskillObjectsDir();
        string sharedRoot = Path.Combine(EQAssetCache.Instance.CacheRoot, "shared", "tradeskill_objects");

        if (IsTradeskillNumericItMesh(n))
        {
            CollectGlbPathsFromDir(sharedObj, modelName, list);
            CollectGlbPathsFromDir(sharedRoot, modelName, list);
            CollectGlbPathsFromDir(objectsDir, modelName, list);
        }
        else
        {
            CollectGlbPathsFromDir(objectsDir, modelName, list);
            CollectGlbPathsFromDir(sharedObj, modelName, list);
            CollectGlbPathsFromDir(sharedRoot, modelName, list);
        }

        return list;
    }

    private static uint GltfChunkType(string tag4)
    {
        return (uint)(tag4[0] | (tag4[1] << 8) | (tag4[2] << 16) | (tag4[3] << 24));
    }

    private static bool TryReadGlbJsonAndBin(byte[] glb, out ReadOnlySpan<byte> jsonUtf8, out ReadOnlySpan<byte> binSpan)
    {
        jsonUtf8 = default;
        binSpan = default;
        if (glb == null || glb.Length < 20) return false;
        if (glb[0] != (byte)'g' || glb[1] != (byte)'l' || glb[2] != (byte)'T' || glb[3] != (byte)'F') return false;
        if (BitConverter.ToUInt32(glb, 4) != 2u) return false;
        int pos = 12;
        uint jsonLen = BitConverter.ToUInt32(glb, pos); pos += 4;
        uint jsonType = BitConverter.ToUInt32(glb, pos); pos += 4;
        if (jsonType != GltfChunkType("JSON")) return false;
        if (pos + jsonLen > glb.Length) return false;
        jsonUtf8 = glb.AsSpan(pos, (int)jsonLen);
        pos += (int)jsonLen;
        if (pos + 8 <= glb.Length)
        {
            uint binLen = BitConverter.ToUInt32(glb, pos); pos += 4;
            uint binType = BitConverter.ToUInt32(glb, pos); pos += 4;
            if (binType == GltfChunkType("BIN\0") && pos + binLen <= glb.Length && binLen > 0)
                binSpan = glb.AsSpan(pos, (int)binLen);
        }
        return true;
    }

    private static byte[] EncodeGlb2(ReadOnlySpan<byte> jsonUtf8Unpadded, ReadOnlySpan<byte> binSpan)
    {
        int jPad = (4 - (jsonUtf8Unpadded.Length % 4)) % 4;
        int jsonChunkLen = jsonUtf8Unpadded.Length + jPad;
        int binTotal = binSpan.IsEmpty ? 0 : 8 + binSpan.Length;
        int total = 12 + 8 + jsonChunkLen + binTotal;
        var buf = new byte[total];
        int w = 0;
        buf[w++] = (byte)'g'; buf[w++] = (byte)'l'; buf[w++] = (byte)'T'; buf[w++] = (byte)'F';
        BitConverter.TryWriteBytes(buf.AsSpan(w), 2u); w += 4;
        BitConverter.TryWriteBytes(buf.AsSpan(w), (uint)total); w += 4;
        BitConverter.TryWriteBytes(buf.AsSpan(w), (uint)jsonChunkLen); w += 4;
        BitConverter.TryWriteBytes(buf.AsSpan(w), GltfChunkType("JSON")); w += 4;
        jsonUtf8Unpadded.CopyTo(buf.AsSpan(w)); w += jsonUtf8Unpadded.Length;
        for (int z = 0; z < jPad; z++) buf[w++] = 0x20;
        if (!binSpan.IsEmpty)
        {
            BitConverter.TryWriteBytes(buf.AsSpan(w), (uint)binSpan.Length); w += 4;
            BitConverter.TryWriteBytes(buf.AsSpan(w), GltfChunkType("BIN\0")); w += 4;
            binSpan.CopyTo(buf.AsSpan(w));
        }
        return buf;
    }

    private static bool ImageJsonObjectIsValid(JsonObject o)
    {
        if (o["uri"] is JsonValue jv)
        {
            try
            {
                string s = jv.GetValue<string>();
                if (!string.IsNullOrEmpty(s)) return true;
            }
            catch { /* ignore */ }
        }
        if (o["bufferView"] is JsonValue bv)
        {
            try
            {
                if (bv.TryGetValue<int>(out int idx) && idx >= 0) return true;
            }
            catch { /* ignore */ }
        }
        return false;
    }

    private static void StripMaterialTextureRefs(JsonObject mat, ref bool changed)
    {
        ReadOnlySpan<string> topKeys = new[]
        {
            "normalTexture", "occlusionTexture", "emissiveTexture", "clearcoat", "sheen", "specular",
            "transmission", "volume", "ior", "anisotropy", "iridescence"
        };
        foreach (string k in topKeys)
        {
            if (mat.Remove(k)) changed = true;
        }
        if (mat["pbrMetallicRoughness"] is JsonObject pbr)
        {
            if (pbr.Remove("baseColorTexture")) changed = true;
            if (pbr.Remove("metallicRoughnessTexture")) changed = true;
        }
    }

    private static void StripAllMaterialTextureRefs(JsonObject rootObj, ref bool changed)
    {
        if (rootObj["materials"] is not JsonArray mats) return;
        foreach (JsonNode m in mats)
        {
            if (m is JsonObject mo) StripMaterialTextureRefs(mo, ref changed);
        }
    }

    private static void ClampTextureSourcesToImages(JsonObject rootObj, int imageCount, ref bool changed)
    {
        if (imageCount <= 0 || rootObj["textures"] is not JsonArray texs) return;
        foreach (JsonNode t in texs)
        {
            if (t is not JsonObject to || to["source"] is not JsonValue sv) continue;
            if (!sv.TryGetValue<int>(out int src)) continue;
            if (src < 0 || src >= imageCount)
            {
                to["source"] = 0;
                changed = true;
            }
        }
    }

    private static int ReadGltfAccessorCount(JsonObject acc)
    {
        if (acc["count"] is JsonValue jv && jv.TryGetValue(out int c)) return c;
        return 0;
    }

    private static string GetGltfAccessorType(JsonObject acc)
    {
        if (acc["type"] is JsonValue jv)
        {
            try
            {
                string s = jv.GetValue<string>();
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch { /* ignore */ }
        }
        return "VEC3";
    }

    private static int GetGltfAccessorComponentType(JsonObject acc)
    {
        if (acc["componentType"] is JsonValue jv && jv.TryGetValue(out int ct)) return ct;
        return 5126;
    }

    private static int GltfAccessorTypeComponentCount(string type)
    {
        return type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            "MAT2" => 4,
            "MAT3" => 9,
            "MAT4" => 16,
            _ => 3,
        };
    }

    private static int GltfBytesPerComponentType(int componentType)
    {
        return componentType switch
        {
            5120 or 5121 => 1,
            5122 or 5123 => 2,
            5125 or 5126 => 4,
            _ => 4,
        };
    }

    private static int GltfAccessorElementByteSize(JsonObject acc)
    {
        string type = GetGltfAccessorType(acc);
        int ct = GetGltfAccessorComponentType(acc);
        int comps = GltfAccessorTypeComponentCount(type);
        int bpc = GltfBytesPerComponentType(ct);
        return Math.Max(1, comps * bpc);
    }

    private static void PadGltfBinListToAlignment(List<byte> bin, int alignment)
    {
        if (alignment <= 1) return;
        while (bin.Count % alignment != 0) bin.Add(0);
    }

    private static void EnsureRootBuffer0ByteLength(JsonObject rootObj, int byteLength)
    {
        if (rootObj["buffers"] is not JsonArray bufs)
        {
            rootObj["buffers"] = new JsonArray { new JsonObject { ["byteLength"] = byteLength } };
            return;
        }
        if (bufs.Count == 0)
        {
            bufs.Add(new JsonObject { ["byteLength"] = byteLength });
            return;
        }
        if (bufs[0] is JsonObject b0)
            b0["byteLength"] = byteLength;
        else
            bufs.Insert(0, new JsonObject { ["byteLength"] = byteLength });
    }

    /// <summary>
    /// Godot rejects accessors with count 0. Append minimal zero-filled BIN data and new bufferViews so counts are 1.
    /// </summary>
    private static void FixZeroCountAccessors(JsonObject rootObj, ref byte[] binBytes, ref bool changed)
    {
        if (rootObj["accessors"] is not JsonArray accessors) return;
        var bin = new List<byte>(binBytes != null && binBytes.Length > 0 ? binBytes.Length + 64 : 64);
        if (binBytes != null && binBytes.Length > 0) bin.AddRange(binBytes);

        JsonArray bvs = rootObj["bufferViews"] as JsonArray;
        if (bvs == null)
        {
            bvs = new JsonArray();
            rootObj["bufferViews"] = bvs;
        }

        for (int i = 0; i < accessors.Count; i++)
        {
            if (accessors[i] is not JsonObject acc) continue;
            if (ReadGltfAccessorCount(acc) > 0) continue;

            PadGltfBinListToAlignment(bin, 4);
            int byteOff = bin.Count;
            int elemSize = GltfAccessorElementByteSize(acc);
            for (int z = 0; z < elemSize; z++) bin.Add(0);

            int bvIndex = bvs.Count;
            bvs.Add(new JsonObject
            {
                ["buffer"] = 0,
                ["byteOffset"] = byteOff,
                ["byteLength"] = elemSize,
            });

            acc["bufferView"] = bvIndex;
            acc["byteOffset"] = 0;
            acc["count"] = 1;
            acc.Remove("sparse");
            acc.Remove("max");
            acc.Remove("min");

            string ty = GetGltfAccessorType(acc);
            if (ty == "VEC3")
            {
                acc["min"] = new JsonArray { 0, 0, 0 };
                acc["max"] = new JsonArray { 0, 0, 0 };
            }

            changed = true;
        }

        binBytes = bin.ToArray();
        EnsureRootBuffer0ByteLength(rootObj, binBytes.Length);
    }

    /// <summary>
    /// Godot warns when the default <c>scene</c> index is omitted; set <c>scene</c> to 0 if <c>scenes</c> is non-empty.
    /// </summary>
    private static void EnsureGltfDefaultSceneIndex(JsonObject rootObj, ref bool changed)
    {
        if (rootObj["scenes"] is not JsonArray scenes || scenes.Count == 0)
            return;

        int current = -1;
        if (rootObj["scene"] is JsonValue sv && sv.TryGetValue(out int idx))
            current = idx;

        if (current < 0 || current >= scenes.Count)
        {
            rootObj["scene"] = 0;
            changed = true;
        }
    }

    /// <summary>
    /// Fix Lantern/eqsage glTF JSON (invalid images, skins, bad texture indices) and optionally strip material
    /// texture links for RoF2 exports that confuse Godot's glTF loader. Returns false only if GLB/JSON cannot be read.
    /// </summary>
    private static bool TryMitigateLanternGltf(byte[] glbBytes, bool stripMaterialTextures, out byte[] mitigated)
    {
        mitigated = null;
        if (!TryReadGlbJsonAndBin(glbBytes, out ReadOnlySpan<byte> jsonSpan, out ReadOnlySpan<byte> binSpan)) return false;
        string jsonStr = Encoding.UTF8.GetString(jsonSpan).TrimEnd('\0', ' ');
        JsonNode root;
        try
        {
            root = JsonNode.Parse(jsonStr);
        }
        catch
        {
            return false;
        }
        if (root is not JsonObject rootObj) return false;

        byte[] binWorking;
        if (binSpan.IsEmpty)
            binWorking = Array.Empty<byte>();
        else
        {
            binWorking = new byte[binSpan.Length];
            binSpan.CopyTo(binWorking);
        }

        bool changed = false;
        const string PngDataUri =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        if (rootObj.Remove("extensions")) changed = true;
        if (rootObj.Remove("extensionsUsed")) changed = true;
        if (rootObj.Remove("extensionsRequired")) changed = true;

        JsonArray images = rootObj["images"] as JsonArray;
        if (images == null || images.Count == 0)
        {
            rootObj["images"] = new JsonArray { new JsonObject { ["uri"] = PngDataUri } };
            changed = true;
            images = rootObj["images"] as JsonArray;
        }
        else
        {
            for (int i = 0; i < images.Count; i++)
            {
                JsonNode img = images[i];
                bool needReplace = img is null || img.GetValueKind() == JsonValueKind.Null;
                if (!needReplace && img is JsonObject imgObj)
                    needReplace = !ImageJsonObjectIsValid(imgObj);
                else if (!needReplace)
                    needReplace = true;
                if (!needReplace) continue;
                images[i] = new JsonObject { ["uri"] = PngDataUri };
                changed = true;
            }
        }

        int imageCount = (rootObj["images"] as JsonArray)?.Count ?? 0;
        ClampTextureSourcesToImages(rootObj, imageCount, ref changed);

        if (rootObj.Remove("skins")) changed = true;

        if (rootObj["nodes"] is JsonArray nodes)
        {
            foreach (JsonNode node in nodes)
            {
                if (node is not JsonObject no) continue;
                if (no.Remove("skin")) changed = true;
                if (no.Remove("skeleton")) changed = true;
            }
        }

        if (rootObj["meshes"] is JsonArray meshes)
        {
            foreach (JsonNode mesh in meshes)
            {
                if (mesh is not JsonObject mo || mo["primitives"] is not JsonArray prims) continue;
                foreach (JsonNode prim in prims)
                {
                    if (prim is not JsonObject po || po["attributes"] is not JsonObject attrs) continue;
                    if (attrs.Remove("JOINTS_0")) changed = true;
                    if (attrs.Remove("WEIGHTS_0")) changed = true;
                }
            }
        }

        FixZeroCountAccessors(rootObj, ref binWorking, ref changed);

        EnsureGltfDefaultSceneIndex(rootObj, ref changed);

        if (stripMaterialTextures)
        {
            StripAllMaterialTextureRefs(rootObj, ref changed);
            // RoF2/Lantern exports often trip Godot's glTF loader on subtle JSON quirks; always re-encode so we never
            // AppendFromBuffer the raw file for these paths (still one parse pass — no AppendFromFile spam).
            changed = true;
        }

        if (!changed)
            return false;
        try
        {
            string newJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            mitigated = EncodeGlb2(Encoding.UTF8.GetBytes(newJson), binWorking);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Single Godot parse pass: avoid AppendFromFile on broken GLBs (each failure spams the debugger).</summary>
    private static byte[] BuildGlbBufferForImport(byte[] raw, string glbPathForHeuristics)
    {
        bool eqsageLike = glbPathForHeuristics.IndexOf("eqsage", StringComparison.OrdinalIgnoreCase) >= 0
            || glbPathForHeuristics.IndexOf("tradeskill_objects", StringComparison.OrdinalIgnoreCase) >= 0;
        if (TryMitigateLanternGltf(raw, eqsageLike, out byte[] mitigated))
            return mitigated;
        return raw;
    }

    /// <summary>
    /// Load an object's GLB scene, caching for reuse.
    /// </summary>
    public PackedScene GetObjectScene(string modelName, string objectsDir)
    {
        string cacheKey = GetObjectModelCacheKey(modelName);
        if (string.IsNullOrEmpty(cacheKey))
            return null;

        if (_objectSceneCache.TryGetValue(cacheKey, out var cached))
            return cached;

        List<string> orderedPaths = BuildOrderedGlbPaths(objectsDir, modelName);
        if (orderedPaths.Count == 0)
        {
            _objectSceneCache[cacheKey] = null;
            return null;
        }

        try
        {
            foreach (string glbPath in orderedPaths)
            {
                string importBase = Path.GetDirectoryName(glbPath);
                if (string.IsNullOrEmpty(importBase))
                    importBase = objectsDir;
                string importBaseGodot = importBase.Replace('\\', '/').TrimEnd('/');
                string glbPathGodot = glbPath.Replace('\\', '/');

                byte[] raw = File.ReadAllBytes(glbPath);
                byte[] glbForImport = BuildGlbBufferForImport(raw, glbPath);
                var gltfDoc = new GltfDocument();
                var gltfState = new GltfState();
                gltfState.BasePath = importBaseGodot;
                Error err = gltfDoc.AppendFromBuffer(glbForImport, importBaseGodot, gltfState);

                if (err != Error.Ok)
                    continue;

                Node scene = gltfDoc.GenerateScene(gltfState);
                if (scene == null)
                    continue;

                SanitizeGltfMaterialsMissingTextures(scene);
                int meshCountFixed = 0;
                int surfaceCountFixed = 0;
                WorldManager.BakeFlippedNormalsRecursive(scene, ref meshCountFixed, ref surfaceCountFixed);

                bool forceSolid = cacheKey.Contains("step") || cacheKey.Contains("ele") || cacheKey.Contains("ramp") ||
                                  cacheKey.Contains("plat") || cacheKey.Contains("lift") || cacheKey.Contains("bridge");
                GenerateCollisionRecursive(scene, scene, forceSolid);

                var packed = new PackedScene();
                packed.Pack(scene);
                scene.QueueFree();

                _objectSceneCache[cacheKey] = packed;
                if (orderedPaths.Count > 1 && !string.Equals(orderedPaths[0], glbPath, StringComparison.OrdinalIgnoreCase))
                    GD.Print($"[ObjectPlacer] Loaded '{modelName}' from {glbPath} (not first search candidate).");

                return packed;
            }

            string resolved = NormalizeObjectMeshModelName(modelName);
            GD.PrintErr($"[ObjectPlacer] Failed to load GLB '{modelName}' (resolved '{resolved}'): no usable file among {orderedPaths.Count} path(s). Last tried: {Path.GetFileName(orderedPaths[^1])}.");
            _objectSceneCache[cacheKey] = null;
            return null;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ObjectPlacer] Error loading '{modelName}': {ex.Message}");
            _objectSceneCache[cacheKey] = null;
            return null;
        }
    }

    /// <summary>Neutralize StandardMaterials that lost textures during glTF import (exporter quirks).</summary>
    public static void SanitizeGltfMaterialsOnRoot(Node root) => SanitizeGltfMaterialsMissingTextures(root);

    private static void SanitizeGltfMaterialsMissingTextures(Node root)
    {
        foreach (Node node in root.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            if (node is not MeshInstance3D mi || mi.Mesh == null)
                continue;
            var mesh = mi.Mesh;
            int count = mesh.GetSurfaceCount();
            for (int i = 0; i < count; i++)
            {
                Material mat = mi.GetActiveMaterial(i);
                if (mat is not StandardMaterial3D sm)
                    continue;
                if (sm.AlbedoTexture != null || sm.NormalTexture != null || sm.RoughnessTexture != null ||
                    sm.MetallicTexture != null || sm.AOTexture != null)
                    continue;
                var dup = (StandardMaterial3D)sm.Duplicate();
                dup.AlbedoColor = new Color(0.55f, 0.52f, 0.48f);
                dup.Roughness = 0.85f;
                dup.Metallic = 0f;
                mi.SetSurfaceOverrideMaterial(i, dup);
            }
        }
    }

    /// <summary>Clear the object scene cache (call on zone change).</summary>
    public void ClearCache()
    {
        _objectSceneCache.Clear();
        _objectAnimDataCache.Clear();
        _classicMeshRedirectsLoaded = false;
        _classicMeshRedirects = null;
    }

    /// <summary>
    /// Registers MaterialList texture-frame cycling on <paramref name="instance"/> materials.
    /// Must run after <see cref="PackedScene.Instantiate{T}"/> so references match the live meshes (template scenes are packed then freed).
    /// </summary>
    public void RegisterInstanceMaterialAnimations(Node3D instance, string modelName, string objectsDir)
    {
        if (Animator == null || instance == null) return;
        if (!TryGetObjectAnimData(modelName, objectsDir, out var animData)) return;
        string texturesDir = Path.Combine(objectsDir, "Textures");
        RegisterAnimationsRecursive(instance, animData, texturesDir);
    }

    private bool TryGetObjectAnimData(string modelName, string objectsDir, out Dictionary<string, (string[] frames, float delay)> animData)
    {
        string cacheKey = GetObjectModelCacheKey(modelName);
        if (string.IsNullOrEmpty(cacheKey))
        {
            animData = null;
            return false;
        }

        if (_objectAnimDataCache.TryGetValue(cacheKey, out animData))
            return animData != null && animData.Count > 0;

        string matListDir = Path.Combine(objectsDir, "MaterialLists");
        string matListFile = null;
        foreach (string baseName in BuildGlbBaseNameCandidates(modelName))
        {
            matListFile = Path.Combine(matListDir, $"{baseName}.txt");
            if (File.Exists(matListFile)) break;
            matListFile = FindFileCaseInsensitive(matListDir, $"{baseName}.txt");
            if (matListFile != null) break;
        }

        animData = (matListFile != null && File.Exists(matListFile))
            ? ParseMaterialList(matListFile)
            : new Dictionary<string, (string[] frames, float delay)>();
        _objectAnimDataCache[cacheKey] = animData;
        return animData.Count > 0;
    }

    /// <summary>Find a file case-insensitively in a directory.</summary>
    private static string FindFileCaseInsensitive(string dir, string filename)
    {
        if (!Directory.Exists(dir)) return null;
        string lower = filename.ToLower();
        foreach (var file in Directory.GetFiles(dir))
        {
            if (Path.GetFileName(file).ToLower() == lower)
                return file;
        }
        return null;
    }

    /// <summary>
    /// Adds an OmniLight3D to objects that act as light sources.
    /// </summary>
    public void AddLightIfSource(Node3D instance, string modelName, Node3D container, float worldX, float worldY, float worldZ)
    {
        string lowerName = modelName.ToLower();
        
        string configKey = null;
        if (lowerName.Contains("cfi") || lowerName.Contains("campfire")) configKey = "campfire";
        // "brazier" covers faybrazier; "braz" covers kelbraz and similar without the full word.
        else if (lowerName.Contains("bfi") || lowerName.Contains("brazier") || lowerName.Contains("braz")) configKey = "brazier";
        // Floor / fire grates (e.g. grate) — tune per-zone in object_lights.json models.grate if needed.
        else if (lowerName.Contains("grate")) configKey = "otherfire";
        else if (lowerName.Contains("tfi") || lowerName.Contains("torch") || lowerName.Contains("sconce") || lowerName.Contains("wfi")) configKey = "torch";
        else if (lowerName.Contains("lfi") || lowerName.Contains("lantern") || lowerName.Contains("lamp") || lowerName.Contains("mistlamp") || lowerName.Contains("ogglantern")) configKey = "lantern";
        else if (lowerName.Contains("ffi") || lowerName.Contains("forge")) configKey = "forge";
        else if (lowerName.Contains("candle") || lowerName.Contains("cndl") || lowerName.Contains("candel") || lowerName.Contains("candelabra")) configKey = "candelabra";
        else if (lowerName.Contains("chandelier") || lowerName.Contains("chndlr") || lowerName.Contains("chand")) configKey = "chandelier";
        else if (lowerName.Contains("fire") || lowerName.EndsWith("fi") || lowerName.Contains("fi_act") || lowerName.StartsWith("pfi")) configKey = "otherfire";

        JsonElement typeConfig = default;
        bool hasTypeConfig = configKey != null &&
                             _lightConfigs.ValueKind != JsonValueKind.Undefined &&
                             _lightConfigs.TryGetProperty(configKey, out typeConfig);

        JsonElement modelConfig = default;
        bool hasModelConfig = TryGetModelLightConfig(lowerName, out modelConfig);

        if (hasTypeConfig || hasModelConfig)
        {
            string soundName = GetConfigString(modelConfig, "sound", typeConfig, null);
            if (!string.IsNullOrEmpty(soundName))
            {
                float vol = GetConfigFloat(modelConfig, "soundVolume", typeConfig, 1.0f);
                AttachAudio(instance, soundName, vol);
            }
            
            if (!DisableLights)
            {
                var light = new OmniLight3D();
                light.Name = $"{instance.Name}_Omni";

                light.LightEnergy = GetConfigFloat(modelConfig, "energy", typeConfig, 25.0f);
                light.OmniRange = GetConfigFloat(modelConfig, "range", typeConfig, 10.0f);

                Vector3 localOffset = Vector3.Zero;
                if (TryGetConfigArray3(modelConfig, "position", typeConfig, out var pos))
                    localOffset = new Vector3(pos[0].GetSingle(), pos[1].GetSingle(), pos[2].GetSingle());

                if (TryGetConfigArray3(modelConfig, "color", typeConfig, out var color))
                    light.LightColor = new Color(color[0].GetSingle(), color[1].GetSingle(), color[2].GetSingle());

                // Torch/lantern shadows were globally disabled, which is why standing between
                // two torches only produced one "real" shadow source (usually the sun/moon).
                // Keep this tied to runtime graphics settings.
                bool castShadows = ShadowsEnabled;
                if (TryGetConfigBool(modelConfig, "castShadows", typeConfig, out var castShadowsProp) &&
                    castShadowsProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    castShadows = castShadowsProp.GetBoolean() && ShadowsEnabled;
                }
                light.ShadowEnabled = castShadows;
                // Omni cube shadows are heavier, but they are much more stable for nearby paired
                // torch setups (doorways, halls) than dual-paraboloid and reduce one-sided misses.
                light.OmniShadowMode = OmniLight3D.ShadowMode.Cube;
                light.ShadowBias = 0.05f;
                light.ShadowNormalBias = 1.0f;

                // Omni under a MeshInstance3D root only lights that mesh sensibly on one local axis (inverted
                // spill on the player, etc.). Parent to the same node that holds the object instance instead.
                container.AddChild(light);
                light.GlobalPosition = ComputeWorldLightAnchor(instance, configKey) + new Vector3(localOffset.X, localOffset.Y, localOffset.Z);

                if (castShadows)
                {
                    // By default, the source model itself (flame cards, helper nodes, torch mesh)
                    // should NOT shadow its own light; this is the common cause of "half-omni"
                    // artifacts. Allow opt-in per type/model via sourceCastsShadow=true.
                    bool sourceCastsShadow = GetConfigBoolValue(modelConfig, "sourceCastsShadow", typeConfig, false);
                    if (!sourceCastsShadow)
                    {
                        DisableShadowCastingRecursive(instance);
                    }
                    else
                    {
                        DisableFlameLikeShadowCastersRecursive(instance);
                        DisableNearLightOriginShadowCastersRecursive(instance, light.GlobalPosition, GetSelfOcclusionRadius(configKey));
                    }
                }

                bool pairedOmni = GetConfigBoolValue(modelConfig, "pairedOmni", typeConfig, false);
                if (castShadows && pairedOmni)
                {
                    // Mitigate hemispheric omni-shadow seam artifacts by adding a second omni
                    // with opposite orientation and split energy.
                    light.LightEnergy *= 0.5f;

                    var lightB = new OmniLight3D();
                    lightB.Name = $"{instance.Name}_OmniB";
                    lightB.LightColor = light.LightColor;
                    lightB.LightEnergy = light.LightEnergy;
                    lightB.OmniRange = light.OmniRange;
                    lightB.ShadowEnabled = true;
                    lightB.OmniShadowMode = light.OmniShadowMode;
                    lightB.ShadowBias = light.ShadowBias;
                    lightB.ShadowNormalBias = light.ShadowNormalBias;
                    lightB.RotationDegrees = new Vector3(0f, 180f, 0f);
                    container.AddChild(lightB);
                    lightB.GlobalPosition = light.GlobalPosition;
                }
            }
        }
    }

    private static float GetSelfOcclusionRadius(string configKey)
    {
        return configKey switch
        {
            "torch" => 0.95f,
            "lantern" => 0.85f,
            "chandelier" => 1.1f,
            "campfire" => 0.8f,
            "brazier" => 0.9f,
            "forge" => 1.0f,
            "candelabra" => 0.75f,
            _ => 0.85f
        };
    }

    private bool TryGetModelLightConfig(string lowerModelName, out JsonElement modelConfig)
    {
        modelConfig = default;
        if (_lightConfigs.ValueKind == JsonValueKind.Undefined)
            return false;

        if (!_lightConfigs.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Object)
            return false;

        return models.TryGetProperty(lowerModelName, out modelConfig);
    }

    private static float GetConfigFloat(JsonElement primary, string key, JsonElement fallback, float defaultValue)
    {
        if (primary.ValueKind != JsonValueKind.Undefined &&
            primary.TryGetProperty(key, out var pVal) &&
            pVal.ValueKind == JsonValueKind.Number)
        {
            return pVal.GetSingle();
        }

        if (fallback.ValueKind != JsonValueKind.Undefined &&
            fallback.TryGetProperty(key, out var fVal) &&
            fVal.ValueKind == JsonValueKind.Number)
        {
            return fVal.GetSingle();
        }

        return defaultValue;
    }

    private static string GetConfigString(JsonElement primary, string key, JsonElement fallback, string defaultValue)
    {
        if (primary.ValueKind != JsonValueKind.Undefined &&
            primary.TryGetProperty(key, out var pVal) &&
            pVal.ValueKind == JsonValueKind.String)
        {
            return pVal.GetString();
        }

        if (fallback.ValueKind != JsonValueKind.Undefined &&
            fallback.TryGetProperty(key, out var fVal) &&
            fVal.ValueKind == JsonValueKind.String)
        {
            return fVal.GetString();
        }

        return defaultValue;
    }

    private static bool TryGetConfigArray3(JsonElement primary, string key, JsonElement fallback, out JsonElement arr)
    {
        arr = default;
        if (primary.ValueKind != JsonValueKind.Undefined &&
            primary.TryGetProperty(key, out var pVal) &&
            pVal.ValueKind == JsonValueKind.Array &&
            pVal.GetArrayLength() >= 3)
        {
            arr = pVal;
            return true;
        }

        if (fallback.ValueKind != JsonValueKind.Undefined &&
            fallback.TryGetProperty(key, out var fVal) &&
            fVal.ValueKind == JsonValueKind.Array &&
            fVal.GetArrayLength() >= 3)
        {
            arr = fVal;
            return true;
        }

        return false;
    }

    private static bool TryGetConfigBool(JsonElement primary, string key, JsonElement fallback, out JsonElement value)
    {
        value = default;
        if (primary.ValueKind != JsonValueKind.Undefined &&
            primary.TryGetProperty(key, out var pVal) &&
            pVal.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = pVal;
            return true;
        }

        if (fallback.ValueKind != JsonValueKind.Undefined &&
            fallback.TryGetProperty(key, out var fVal) &&
            fVal.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = fVal;
            return true;
        }

        return false;
    }

    private static bool GetConfigBoolValue(JsonElement primary, string key, JsonElement fallback, bool defaultValue)
    {
        if (TryGetConfigBool(primary, key, fallback, out var value))
            return value.GetBoolean();
        return defaultValue;
    }

    private static Vector3 ComputeWorldLightAnchor(Node3D instance, string configKey)
    {
        // Use mesh bounds (world-space) so wall torches anchor near the flame region,
        // not at arbitrary/potentially inverted local origins.
        if (TryGetWorldAabb(instance, out var worldAabb))
        {
            float x = worldAabb.Position.X + worldAabb.Size.X * 0.5f;
            float z = worldAabb.Position.Z + worldAabb.Size.Z * 0.5f;
            float yBias = configKey switch
            {
                "torch" => 0.82f,
                "lantern" => 0.72f,
                "chandelier" => 0.74f,
                "campfire" => 0.45f,
                "brazier" => 0.56f,
                "forge" => 0.52f,
                "candelabra" => 0.66f,
                _ => 0.62f
            };
            float y = worldAabb.Position.Y + worldAabb.Size.Y * yBias;
            return new Vector3(x, y, z);
        }

        return instance.GlobalPosition;
    }

    private static bool TryGetWorldAabb(Node node, out Aabb worldAabb)
    {
        bool first = true;
        worldAabb = default;
        VisitWorldAabb(node, ref first, ref worldAabb);
        return !first;
    }

    private static void VisitWorldAabb(Node node, ref bool first, ref Aabb worldAabb)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            var aabb = mi.GlobalTransform * mi.Mesh.GetAabb();
            if (first)
            {
                worldAabb = aabb;
                first = false;
            }
            else
            {
                worldAabb = worldAabb.Merge(aabb);
            }
        }

        foreach (Node child in node.GetChildren())
            VisitWorldAabb(child, ref first, ref worldAabb);
    }

    private static void DisableFlameLikeShadowCastersRecursive(Node node)
    {
        if (node is MeshInstance3D meshInst && MeshLooksLikeFlameShadowCaster(meshInst))
        {
            meshInst.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        foreach (Node child in node.GetChildren())
            DisableFlameLikeShadowCastersRecursive(child);
    }

    private static bool MeshLooksLikeFlameShadowCaster(MeshInstance3D meshInst)
    {
        string nodeName = meshInst.Name.ToString().ToLowerInvariant();
        bool flameName =
            nodeName.Contains("flame") || nodeName.Contains("fire") ||
            nodeName.Contains("glow") || nodeName.Contains("fx") ||
            nodeName.Contains("particle");

        int transparentOrEmissive = 0;
        int opaque = 0;

        int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : meshInst.GetSurfaceOverrideMaterialCount();
        for (int i = 0; i < surfaceCount; i++)
        {
            var mat = meshInst.GetActiveMaterial(i) as BaseMaterial3D;
            if (mat == null) continue;

            bool isTransparent = mat.Transparency != BaseMaterial3D.TransparencyEnum.Disabled;
            bool isEmissive = mat is StandardMaterial3D std && std.EmissionEnabled;
            string matName = (mat.ResourceName ?? string.Empty).ToLowerInvariant();
            bool flameMatName = matName.Contains("flame") || matName.Contains("fire") || matName.Contains("glow");

            if (isTransparent || isEmissive || flameMatName) transparentOrEmissive++;
            else opaque++;
        }

        // Only disable if this mesh is very likely VFX/flame cards, not structural torch holder geometry.
        return flameName || (transparentOrEmissive > 0 && opaque == 0);
    }

    private static void DisableNearLightOriginShadowCastersRecursive(Node node, Vector3 lightPos, float radius)
    {
        if (node is MeshInstance3D meshInst && meshInst.Mesh != null)
        {
            var worldAabb = meshInst.GlobalTransform * meshInst.Mesh.GetAabb();
            Vector3 center = worldAabb.Position + worldAabb.Size * 0.5f;
            float halfDiag = worldAabb.Size.Length() * 0.5f;
            float dist = center.DistanceTo(lightPos);

            // Any mesh overlapping / very near the light source should not cast shadows
            // back into its own omni. This removes the "mystery wedge" artifacts.
            if (dist <= radius + halfDiag)
            {
                meshInst.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
        }

        foreach (Node child in node.GetChildren())
            DisableNearLightOriginShadowCastersRecursive(child, lightPos, radius);
    }

    private static void DisableShadowCastingRecursive(Node node)
    {
        if (node is MeshInstance3D meshInst)
            meshInst.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        foreach (Node child in node.GetChildren())
            DisableShadowCastingRecursive(child);
    }
    private void AttachAudio(Node3D instance, string soundName, float volumeMultiplier = 1.0f)
    {
        var audioStream = EQAssetCache.Instance.GetSound(soundName, loop: true);
        if (audioStream != null)
        {
            var player = new AudioStreamPlayer3D();
            player.Name = "AmbientAudio";
            player.Stream = audioStream;
            
            // Set 3D audio properties
            player.MaxDistance = 50.0f; // Distance where sound fades to zero
            player.UnitSize = 10.0f; // Distance where sound begins to fade
            player.AttenuationFilterCutoffHz = 20500.0f;
            player.VolumeDb = Mathf.LinearToDb(volumeMultiplier);
            player.Autoplay = true;
            
            instance.AddChild(player);
        }
    }

    /// <summary>
    /// Reads light_instances.txt to place baked zone lighting (illuminates wall torches, etc.)
    /// </summary>
    public Node3D PlaceLights(string zoneId, string cachePath, Node3D parent)
    {
        if (parent == null) return null;
        if (DisableLights) return null;

        string lightsFile = Path.Combine(cachePath, "Zone", "light_instances.txt");
        if (!File.Exists(lightsFile))
        {
            GD.Print($"[ObjectPlacer] No light_instances.txt for zone '{zoneId}'");
            return null;
        }

        var old = parent.GetNodeOrNull<Node3D>("ZoneBakedLights");
        if (old != null)
        {
            old.GetParent()?.RemoveChild(old);
            old.QueueFree();
        }

        var container = new Node3D { Name = "ZoneBakedLights" };
        parent.AddChild(container);

        int placed = 0;
        int skipped = 0;

        // Lantern format:
        // PosX, PosY, PosZ, Radius, ColorR, ColorG, ColorB
        // Example:
        // 108.684586,6.903559,-29.038883,70,0.509804,0.509804,0.1176471
        foreach (string raw in File.ReadAllLines(lightsFile))
        {
            string line = raw?.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 7)
            {
                skipped++;
                continue;
            }

            try
            {
                float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
                float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
                float z = float.Parse(parts[2], CultureInfo.InvariantCulture);
                float radius = float.Parse(parts[3], CultureInfo.InvariantCulture);
                float r = float.Parse(parts[4], CultureInfo.InvariantCulture);
                float g = float.Parse(parts[5], CultureInfo.InvariantCulture);
                float b = float.Parse(parts[6], CultureInfo.InvariantCulture);

                var light = new OmniLight3D
                {
                    Name = $"Light_{placed}",
                    LightColor = new Color(
                        Mathf.Clamp(r, 0f, 1f),
                        Mathf.Clamp(g, 0f, 1f),
                        Mathf.Clamp(b, 0f, 1f)),
                    // Baked zone lights are numerous; keep no-shadows for stability/perf.
                    ShadowEnabled = false,
                    // Radius in file already maps well to in-world zone scale.
                    OmniRange = Mathf.Max(2f, radius),
                    // Slight boost so baked EQ lights read against dark ambient.
                    LightEnergy = 1.35f,
                    OmniAttenuation = 1.0f
                };

                // Use local position assignment (safe even when parent is not yet in-tree),
                // avoids Node3D global-transform warnings during async menu backdrop loads.
                light.Position = EQToGodot(x, y, z);
                container.AddChild(light);
                placed++;
            }
            catch
            {
                skipped++;
            }
        }

        GD.Print($"[ObjectPlacer] Placed {placed} baked lights in '{zoneId}' ({skipped} skipped).");
        return container;
    }

    /// <summary>
    /// Debug helper: spawn one sample of each configured light type so artists can
    /// tune energy/range/offset quickly in-game.
    /// </summary>
    public Node3D SpawnLightTuningGallery(Node3D parent, Vector3 worldOrigin)
    {
        if (parent == null)
            return null;

        var old = parent.GetNodeOrNull<Node3D>("LightTuningGallery");
        if (old != null)
        {
            old.GetParent()?.RemoveChild(old);
            old.QueueFree();
        }

        var gallery = new Node3D { Name = "LightTuningGallery" };
        parent.AddChild(gallery);

        var keys = GetConfiguredLightTypeKeys();
        const float spacing = 10f;
        const int columns = 4;
        int i = 0;

        foreach (string key in keys)
        {
            if (!_lightConfigs.TryGetProperty(key, out var cfg) || cfg.ValueKind != JsonValueKind.Object)
                continue;

            var stand = new Node3D { Name = $"LightType_{key}" };
            gallery.AddChild(stand);

            int row = i / columns;
            int col = i % columns;
            float x = (col - (columns - 1) * 0.5f) * spacing;
            float z = row * spacing;
            stand.GlobalPosition = worldOrigin + new Vector3(x, 0f, z);

            var omni = new OmniLight3D { Name = "SampleOmni" };
            omni.LightEnergy = GetConfigFloat(cfg, "energy", default, 12.0f);
            omni.OmniRange = GetConfigFloat(cfg, "range", default, 10.0f);
            if (TryGetConfigArray3(cfg, "color", default, out var color))
                omni.LightColor = new Color(color[0].GetSingle(), color[1].GetSingle(), color[2].GetSingle());

            bool cast = ShadowsEnabled;
            if (TryGetConfigBool(cfg, "castShadows", default, out var castProp))
                cast = castProp.GetBoolean() && ShadowsEnabled;
            omni.ShadowEnabled = cast;
            omni.OmniShadowMode = OmniLight3D.ShadowMode.Cube;
            omni.ShadowBias = 0.05f;
            omni.ShadowNormalBias = 1.0f;

            Vector3 offset = new Vector3(0f, 2.0f, 0f);
            if (TryGetConfigArray3(cfg, "position", default, out var pos))
                offset = new Vector3(pos[0].GetSingle(), pos[1].GetSingle(), pos[2].GetSingle());
            // Keep sample lights high enough above uneven terrain so the ground itself
            // doesn't intersect the source and fake a "half-omni" look.
            if (offset.Y < 1.8f) offset.Y = 1.8f;
            omni.Position = offset;
            stand.AddChild(omni);

            var label = new Label3D
            {
                Name = "Label",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 20,
                OutlineSize = 5,
                Text = $"{key}\nE:{omni.LightEnergy:F1} R:{omni.OmniRange:F1}",
                Position = new Vector3(0f, 4.0f, 0f)
            };
            label.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            stand.AddChild(label);

            i++;
        }

        GD.Print($"[ObjectPlacer] Spawned light tuning gallery ({i} sample lights).");
        return gallery;
    }

    private List<string> GetConfiguredLightTypeKeys()
    {
        var keys = new List<string>();
        if (_lightConfigs.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in _lightConfigs.EnumerateObject())
            {
                if (prop.NameEquals("models")) continue;
                if (prop.Value.ValueKind == JsonValueKind.Object)
                    keys.Add(prop.Name);
            }
        }

        if (keys.Count == 0)
        {
            keys.AddRange(new[]
            {
                "campfire", "brazier", "torch", "lantern",
                "forge", "candelabra", "chandelier", "otherfire"
            });
        }

        keys.Sort(StringComparer.OrdinalIgnoreCase);
        return keys;
    }

    private void GenerateCollisionRecursive(Node node, Node root, bool forceSolid = false)
    {
        if (node is MeshInstance3D meshInst && meshInst.Mesh != null)
        {
            bool hasCollision = false;
            foreach (Node child in meshInst.GetChildren())
            {
                if (child is StaticBody3D body)
                {
                    hasCollision = true;
                    body.InputRayPickable = false;
                }
            }

            if (!hasCollision)
            {
                if (meshInst.Mesh is ArrayMesh arrayMesh)
                {
                    var validFaces = new System.Collections.Generic.List<Vector3>();

                    for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
                    {
                        var mat = meshInst.GetActiveMaterial(i);
                        bool isNoCollide = false;
                        if (mat != null && !forceSolid)
                        {
                            var stdMat = mat as BaseMaterial3D;
                            if (stdMat != null && stdMat.Transparency != BaseMaterial3D.TransparencyEnum.Disabled)
                            {
                                isNoCollide = true; // Skip all transparent materials (vines, leaves, cobwebs)
                            }
                            else if (!string.IsNullOrEmpty(mat.ResourceName))
                            {
                                string n = mat.ResourceName.ToLower();
                                if (n.Contains("invisible") || n == "inv" || n.StartsWith("inv_") || n.Contains("collide") || n.Contains("boundary") || n.Contains("vine") || n.Contains("leaf") || n.Contains("plant") || n.Contains("fern") || n.Contains("bush") || n.Contains("web") || n.Contains("door"))
                                {
                                    isNoCollide = true;
                                }
                            }
                        }

                        if (!isNoCollide)
                        {
                            var arrays = arrayMesh.SurfaceGetArrays(i);
                            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                            var indicesObj = arrays[(int)Mesh.ArrayType.Index];

                            if (indicesObj.VariantType != Variant.Type.Nil)
                            {
                                var indices = indicesObj.AsInt32Array();
                                for (int j = 0; j < indices.Length; j++)
                                {
                                    validFaces.Add(vertices[indices[j]]);
                                }
                            }
                            else
                            {
                                for (int j = 0; j < vertices.Length; j++)
                                {
                                    validFaces.Add(vertices[j]);
                                }
                            }
                        }
                    }

                    if (validFaces.Count > 0)
                    {
                        var colShape = new CollisionShape3D();
                        var concaveShape = new ConcavePolygonShape3D();
                        concaveShape.SetFaces(validFaces.ToArray());
                        colShape.Shape = concaveShape;

                        var staticBody = new StaticBody3D { InputRayPickable = false };
                        meshInst.AddChild(staticBody);
                        staticBody.AddChild(colShape);
                        
                        // Critical for PackedScene.Pack() to keep these nodes!
                        staticBody.Owner = root;
                        colShape.Owner = root;
                    }
                }
                else
                {
                    meshInst.CreateTrimeshCollision();
                    foreach (Node child in meshInst.GetChildren())
                    {
                        if (child is StaticBody3D body && body.Owner == null)
                        {
                            body.Owner = root;
                            body.InputRayPickable = false;
                            foreach (Node shape in body.GetChildren()) {
                                shape.Owner = root;
                            }
                        }
                    }
                }
            }
        }
        foreach (Node child in node.GetChildren())
        {
            GenerateCollisionRecursive(child, root, forceSolid);
        }
    }
    public Dictionary<string, (string[] frames, float delay)> ParseMaterialList(string file)
    {
        var data = new Dictionary<string, (string[] frames, float delay)>();
        foreach (var line in File.ReadAllLines(file))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            var parts = line.Split(',');
            // Format: Index, MaterialName:Frames..., DelayMs
            // Example: 1,tau_fire1:fire1:fire2:fire3:fire4,200
            if (parts.Length >= 3)
            {
                float delay = float.Parse(parts[2], CultureInfo.InvariantCulture);
                if (delay > 0)
                {
                    string[] mats = parts[1].Split(':');
                    string matName = mats[0]; // Lantern uses the first as the material name
                    
                    // The textures are the remaining elements
                    if (mats.Length > 1)
                    {
                        string[] frames = new string[mats.Length - 1];
                        Array.Copy(mats, 1, frames, 0, mats.Length - 1);
                        data[matName] = (frames, delay);
                    }
                }
            }
        }
        return data;
    }

    public void RegisterAnimationsRecursive(Node node, Dictionary<string, (string[] frames, float delay)> animData, string texturesDir)
    {
        if (node is MeshInstance3D meshInst)
        {
            // GD.Print($"[Anim Debug] Inspecting MeshInstance3D: {meshInst.Name}");
            for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
            {
                var mat = meshInst.GetSurfaceOverrideMaterial(i);
                // GD.Print($"[Anim Debug] Override Mat {i}: {mat?.GetType().Name} - Name: {mat?.ResourceName}");
                if (mat is StandardMaterial3D stdMat)
                {
                    if (stdMat.ResourceName != null && animData.TryGetValue(stdMat.ResourceName, out var anim))
                        Animator.RegisterMaterial(stdMat, anim.frames, anim.delay, texturesDir);
                }
            }
            if (meshInst.Mesh != null)
            {
                // GD.Print($"[Anim Debug] Mesh has {meshInst.Mesh.GetSurfaceCount()} surfaces.");
                for (int i = 0; i < meshInst.Mesh.GetSurfaceCount(); i++)
                {
                    var mat = meshInst.Mesh.SurfaceGetMaterial(i);
                    // GD.Print($"[Anim Debug] Surface Mat {i}: {mat?.GetType().Name} - Name: {mat?.ResourceName}");
                    if (mat is StandardMaterial3D stdMat)
                    {
                        if (stdMat.ResourceName != null && animData.TryGetValue(stdMat.ResourceName, out var anim))
                        {
                            Animator.RegisterMaterial(stdMat, anim.frames, anim.delay, texturesDir);
                        }
                        else if (stdMat.ResourceName != null && animData.Count > 0)
                        {
                            foreach (var kvp in animData)
                            {
                                if (kvp.Key.Equals(stdMat.ResourceName, StringComparison.OrdinalIgnoreCase))
                                {
                                    // GD.Print($"[Anim Debug] Case mismatch fixed! {stdMat.ResourceName} matched {kvp.Key}");
                                    Animator.RegisterMaterial(stdMat, kvp.Value.frames, kvp.Value.delay, texturesDir);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
        foreach (Node child in node.GetChildren())
            RegisterAnimationsRecursive(child, animData, texturesDir);
    }
}

