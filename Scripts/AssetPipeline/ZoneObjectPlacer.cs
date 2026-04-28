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
    
    /// <summary>
    /// Place all objects defined in a zone's object_instances.txt.
    /// Returns the container node with all placed objects.
    /// </summary>
    public Node3D PlaceObjects(string zoneId, string cachePath, Node3D parent, bool isBundledMap = false)
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

                // LanternExtractor uses EQ coordinate system:
                // EQ: X=east/west, Y=height, Z=north/south
                // The Lantern GLB mesh has a baked transform of scale(-0.1) + rot(180,0,0).
                // The zone root compensates with a basis that maps:
                //   world.X = -local.Z,  world.Y = local.Y,  world.Z = -local.X
                // Object instance positions are in the SAME local coordinate space
                // as the mesh vertices, so we apply the same mapping here.
                float worldX, worldY, worldZ;
                if (isBundledMap)
                {
                    // Bundled maps use: Godot.X = -EQ_X, Godot.Z = -EQ_Y
                    worldX = -posX;
                    worldY = posY;
                    worldZ = -posZ;
                }
                else
                {
                    // LanternExtractor maps use: Godot.X = -EQ_Y, Godot.Z = -EQ_X
                    worldX = -posZ;
                    worldY = posY;
                    worldZ = -posX;
                }
                instance.Position = new Vector3(worldX, worldY, worldZ);
                instance.RotationDegrees = new Vector3(rotX, rotY, rotZ);
                instance.Scale = new Vector3(scaleX, scaleY, scaleZ);

                AddLightIfSource(instance, modelName, container);

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
    private void AddLightIfSource(Node3D instance, string modelName, Node3D container)
    {
        string lowerName = modelName.ToLower();
        
        bool isCampfire = lowerName.Contains("cfi") || lowerName.Contains("campfire");
        bool isBrazier = lowerName.Contains("bfi") || lowerName.Contains("brazier");
        bool isTorch = lowerName.Contains("tfi") || lowerName.Contains("torch") || lowerName.Contains("sconce") || lowerName.Contains("wfi");
        bool isLantern = lowerName.Contains("lfi") || lowerName.Contains("lantern") || lowerName.Contains("lamp");
        bool isForge = lowerName.Contains("ffi") || lowerName.Contains("forge");
        bool isOtherFire = lowerName.Contains("fire") || lowerName.EndsWith("fi") || lowerName.Contains("fi_act") || lowerName.StartsWith("pfi");

        if (isCampfire || isBrazier || isTorch || isLantern || isForge || isOtherFire)
        {
            var light = new OmniLight3D();
            
            // Shadows are disabled by default for performance
            light.ShadowEnabled = false; 

            if (isCampfire || isBrazier || isForge)
            {
                light.LightEnergy = 50.0f; // Boosted heavily for Godot 4 inverse square falloff
                light.OmniRange = 25.0f;
                // Add light DIRECTLY to the instance, so it inherently follows the object's exact world position and scale
                light.Position = new Vector3(0, 0.5f, 0); 
                
                AttachAudio(instance, "fire001_loop.wav");
            }
            else if (isTorch)
            {
                light.LightEnergy = 30.0f; // Boosted heavily
                light.OmniRange = 15.0f;
                light.Position = new Vector3(0, 0.2f, 0);
                
                AttachAudio(instance, "fire001_loop.wav", 0.5f);
            }
            else
            {
                light.LightEnergy = 25.0f; // Boosted heavily
                light.OmniRange = 10.0f;
                light.Position = new Vector3(0, 0.5f, 0);
            }

            // By attaching directly to the instance, the light "goes along for the ride" mathematically perfectly.
            instance.AddChild(light);
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
    public Node3D PlaceLights(string zoneId, string cachePath, Node3D parent, bool isBundledMap = false)
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
                
                float worldX, worldY, worldZ;
                if (isBundledMap)
                {
                    worldX = -x;
                    worldY = y;
                    worldZ = -z;
                }
                else
                {
                    worldX = -z;
                    worldY = y;
                    worldZ = -x;
                }
                
                light.Position = new Vector3(worldX, worldY, worldZ);
                
                // EQ colors are often 0-1 range
                light.LightColor = new Color(r, g, b);
                
                // EQ Radiuses are massive, which causes 100s of lights to overlap in Godot.
                // This overwhelms the Forward+ clustered renderer (max 512 per cluster), causing it to cull ALL lights.
                // We MUST clamp the radius strictly so they stay localized.
                float scaledRadius = radius * 0.2f;
                light.OmniRange = Mathf.Clamp(scaledRadius, 5.0f, 20.0f); 
                
                // Boost energy, but keep it reasonable so it doesn't blow out performance
                light.LightEnergy = 50.0f; 
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
