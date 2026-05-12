using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

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

        return container;
    }

    /// <summary>
    /// Load an object's GLB scene, caching for reuse.
    /// </summary>
    public PackedScene GetObjectScene(string modelName, string objectsDir)
    {
        if (_objectSceneCache.TryGetValue(modelName, out var cached))
            return cached;

        string glbPath = Path.Combine(objectsDir, $"{modelName}.glb");
        if (!File.Exists(glbPath))
        {
            // Try case-insensitive match
            glbPath = FindFileCaseInsensitive(objectsDir, $"{modelName}.glb");
            if (glbPath == null)
            {
                _objectSceneCache[modelName] = null; // Cache the miss too
                return null;
            }
        }

        try
        {
            // Load GLB at runtime using GltfDocument
            var gltfDoc = new GltfDocument();
            var gltfState = new GltfState();
            
            var err = gltfDoc.AppendFromFile(glbPath, gltfState);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[ObjectPlacer] Failed to load GLB '{modelName}': {err}");
                _objectSceneCache[modelName] = null;
                return null;
            }

            var scene = gltfDoc.GenerateScene(gltfState);
            if (scene == null)
            {
                _objectSceneCache[modelName] = null;
                return null;
            }

            // Lantern object GLBs are exported with the same negative-scale basis issue as
            // zones/characters, so flip baked normals once here to make omni/spot lighting
            // behave consistently across props.
            int meshCountFixed = 0;
            int surfaceCountFixed = 0;
            WorldManager.BakeFlippedNormalsRecursive(scene, ref meshCountFixed, ref surfaceCountFixed);

            bool forceSolid = modelName.ToLower().Contains("step") || modelName.ToLower().Contains("ele") || modelName.ToLower().Contains("ramp") || modelName.ToLower().Contains("plat") || modelName.ToLower().Contains("lift") || modelName.ToLower().Contains("bridge");
            // Generate collision so that objects (e.g. ramps) are solid
            GenerateCollisionRecursive(scene, scene, forceSolid);

            // Animated materials are registered per Instantiate() — the template scene is Packed then freed;
            // registering here would target materials that are not used by packed instances (export builds finalize frees reliably).

            // Pack it so we can instantiate multiple copies efficiently
            var packed = new PackedScene();
            packed.Pack(scene);
            scene.QueueFree(); // Free the template node

            _objectSceneCache[modelName] = packed;
            return packed;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ObjectPlacer] Error loading '{modelName}': {ex.Message}");
            _objectSceneCache[modelName] = null;
            return null;
        }
    }

    /// <summary>Clear the object scene cache (call on zone change).</summary>
    public void ClearCache()
    {
        _objectSceneCache.Clear();
        _objectAnimDataCache.Clear();
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
        if (_objectAnimDataCache.TryGetValue(modelName, out animData))
            return animData != null && animData.Count > 0;

        string matListDir = Path.Combine(objectsDir, "MaterialLists");
        string matListFile = Path.Combine(matListDir, $"{modelName}.txt");
        if (!File.Exists(matListFile))
            matListFile = FindFileCaseInsensitive(matListDir, $"{modelName}.txt");

        animData = (matListFile != null && File.Exists(matListFile))
            ? ParseMaterialList(matListFile)
            : new Dictionary<string, (string[] frames, float delay)>();
        _objectAnimDataCache[modelName] = animData;
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

