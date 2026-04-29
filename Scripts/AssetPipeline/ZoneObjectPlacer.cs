using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

/// <summary>
/// Reads LanternExtractor's object_instances.txt and places extracted
/// GLB objects into the zone scene at their correct positions.
/// </summary>
public partial class ZoneObjectPlacer : RefCounted
{
    // Cache loaded object scenes to avoid re-loading duplicates (e.g., 50 trees)
    private readonly Dictionary<string, PackedScene> _objectSceneCache = new();
    
    // Graphics Settings
    public bool ShadowsEnabled { get; set; } = true;

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

                AddLightIfSource(instance, modelName, container, worldPos.X, worldPos.Y, worldPos.Z);

                container.AddChild(instance);
                placed++;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ObjectPlacer] Error placing object: {ex.Message}");
                skipped++;
            }
        }

        parent.AddChild(container);
        GD.Print($"[ObjectPlacer] Placed {placed} objects in '{zoneId}' ({skipped} skipped)");

        return container;
    }

    /// <summary>
    /// Load an object's GLB scene, caching for reuse.
    /// </summary>
    private PackedScene GetObjectScene(string modelName, string objectsDir)
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
    private void AddLightIfSource(Node3D instance, string modelName, Node3D container, float worldX, float worldY, float worldZ)
    {
        string lowerName = modelName.ToLower();
        
        bool isCampfire = lowerName.Contains("cfi") || lowerName.Contains("campfire");
        bool isBrazier = lowerName.Contains("bfi") || lowerName.Contains("brazier");
        bool isTorch = lowerName.Contains("tfi") || lowerName.Contains("torch") || lowerName.Contains("sconce") || lowerName.Contains("wfi");
        bool isLantern = lowerName.Contains("lfi") || lowerName.Contains("lantern") || lowerName.Contains("lamp");
        bool isForge = lowerName.Contains("ffi") || lowerName.Contains("forge");
        bool isCandelabra = lowerName.Contains("candle") || lowerName.Contains("cndl") || lowerName.Contains("candel") || lowerName.Contains("candelabra");
        bool isChandelier = lowerName.Contains("chandelier") || lowerName.Contains("chndlr") || lowerName.Contains("chand");
        bool isOtherFire = lowerName.Contains("fire") || lowerName.EndsWith("fi") || lowerName.Contains("fi_act") || lowerName.StartsWith("pfi");

        if (isCampfire || isBrazier || isTorch || isLantern || isForge || isCandelabra || isChandelier || isOtherFire)
        {
            var light = new OmniLight3D();
            
            if (isCampfire || isBrazier || isForge)
            {
                if (instance.HasNode("BrazierLight")) return;
                
                light.Name = "BrazierLight";
                light.LightEnergy = 10.0f; 
                light.OmniRange = 35.0f; 
                light.LightSize = 0.0f; 
                light.OmniAttenuation = 1.0f;
                light.LightColor = new Color(1.0f, 0.6f, 0.25f);
                light.Position = new Vector3(0, 7.5f, 0);
                light.ShadowEnabled = ShadowsEnabled;
                light.ShadowBias = 0.1f;
                instance.AddChild(light);
                
                AttachAudio(instance, "fire001_loop.wav");
            }
            else if (isTorch)
            {
                if (instance.HasNode("TorchLight")) return;
                light.Name = "TorchLight";
                light.LightEnergy = 6.0f; // BUMPED from 3.0
                light.OmniRange = 25.0f; // BUMPED from 15.0
                light.LightColor = new Color(1.0f, 0.7f, 0.3f);
                light.Position = new Vector3(0, 0.2f, 0);
                instance.AddChild(light);
                
                AttachAudio(instance, "fire001_loop.wav", 0.5f);
            }
            else if (isChandelier)
            {
                if (instance.HasNode("ChandelierLight")) return;
                light.Name = "ChandelierLight";
                light.LightEnergy = 6.0f;
                light.OmniRange = 30.0f;
                light.LightColor = new Color(1.0f, 0.8f, 0.45f);
                light.Position = new Vector3(0, -0.3f, 0);
                instance.AddChild(light);
            }
            else if (isCandelabra)
            {
                if (instance.HasNode("CandelabraLight")) return;
                light.Name = "CandelabraLight";
                light.LightEnergy = 5.0f;
                light.OmniRange = 20.0f;
                light.LightColor = new Color(1.0f, 0.8f, 0.4f);
                light.Position = new Vector3(0, 0.5f, 0);
                instance.AddChild(light);
            }
            else if (isLantern)
            {
                if (instance.HasNode("LanternLight")) return;
                light.Name = "LanternLight";
                light.LightEnergy = 5.0f; // BUMPED from 2.5
                light.OmniRange = 20.0f; // BUMPED from 12.0
                light.LightColor = new Color(1.0f, 0.75f, 0.35f);
                light.Position = new Vector3(0, 0.3f, 0);
                instance.AddChild(light);
            }
            else
            {
                if (instance.HasNode("GenericLight")) return;
                light.Name = "GenericLight";
                light.LightEnergy = 2.0f;
                light.OmniRange = 10.0f;
                light.LightColor = new Color(1.0f, 0.65f, 0.3f);
                light.Position = new Vector3(0, 0.5f, 0);
                instance.AddChild(light);
            }
            
            // CRITICAL FIX: Disable shadow casting on the object's geometry itself!
            // This prevents the brazier bowl, the torch stick, or the flame texture from blocking
            // the OmniLight3D and casting massive shadows onto the surrounding walls.
            // The dungeon walls behind it will STILL cast shadows to stop light bleed, which is perfect.
            DisableShadowsRecursive(instance);
        }
    }
    
    private void DisableShadowsRecursive(Node node)
    {
        if (node is GeometryInstance3D geom)
        {
            geom.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            
            // Also ensure the material is double-sided so the light can pass through "from the inside" 
            // of the bowl or flame and still illuminate the other side.
            if (geom is MeshInstance3D meshInst)
            {
                for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
                {
                    if (meshInst.GetSurfaceOverrideMaterial(i) is StandardMaterial3D mat)
                        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                }
                if (meshInst.Mesh != null)
                {
                    for (int i = 0; i < meshInst.Mesh.GetSurfaceCount(); i++)
                    {
                        if (meshInst.Mesh.SurfaceGetMaterial(i) is StandardMaterial3D mat)
                        {
                            var newMat = (StandardMaterial3D)mat.Duplicate();
                            newMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                            meshInst.SetSurfaceOverrideMaterial(i, newMat);
                        }
                    }
                }
            }
        }
        foreach (Node child in node.GetChildren())
        {
            DisableShadowsRecursive(child);
        }
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
        string lightsFile = Path.Combine(cachePath, "Zone", "light_instances.txt");
        if (!File.Exists(lightsFile)) return null;

        var container = new Node3D { Name = "ZoneLights" };
        int placed = 0;

        foreach (var line in File.ReadAllLines(lightsFile))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

            var parts = line.Split(',');
            if (parts.Length < 7) continue;

            try
            {
                float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
                float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
                float z = float.Parse(parts[2], CultureInfo.InvariantCulture);
                float radius = float.Parse(parts[3], CultureInfo.InvariantCulture);
                float r = float.Parse(parts[4], CultureInfo.InvariantCulture);
                float g = float.Parse(parts[5], CultureInfo.InvariantCulture);
                float b = float.Parse(parts[6], CultureInfo.InvariantCulture);

                var light = new OmniLight3D();
                light.Name = $"Light_{placed}";
                
                float worldX = -z;
                float worldY = y;
                float worldZ = -x;
                
                light.Position = new Vector3(worldX, worldY, worldZ);
                
                // EQ colors are often 0-1 range. Pure white lights look sterile —
                // tint them warm since most EQ light sources are fire-based.
                if (r > 0.9f && g > 0.9f && b > 0.9f)
                {
                    light.LightColor = new Color(1.0f, 0.75f, 0.4f); // Warm fire tint
                }
                else
                {
                    light.LightColor = new Color(r, g, b);
                }
                
                // EQ Radiuses are massive, which causes 100s of lights to overlap in Godot.
                // This overwhelms the Forward+ clustered renderer (max 512 per cluster), causing it to cull ALL lights.
                // We MUST clamp the radius strictly so they stay localized.
                float scaledRadius = radius * 0.2f;
                light.OmniRange = Mathf.Clamp(scaledRadius, 5.0f, 20.0f); 
                
                // Keep energy reasonable so it doesn't blow out performance
                light.LightEnergy = 2.0f; 
                light.ShadowEnabled = false;

                container.AddChild(light);
                placed++;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ObjectPlacer] Error placing light: {ex.Message}");
            }
        }

        parent.AddChild(container);
        GD.Print($"[ObjectPlacer] Placed {placed} static lights in '{zoneId}'");
        return container;
    }
}
