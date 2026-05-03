using Godot;
using System;
using System.Collections.Generic;

public partial class WorldManager : Node3D
{
    public void FreezeForZoneLoad()
    {
        _teleportSettling = true;
        if (_playerCapsule != null)
            _playerCapsule.Velocity = Vector3.Zero;
        GD.Print("[WORLD] Player frozen for zone load.");
    }
    public void ClearWorld()
    {
        foreach (var child in _spawnsContainer.GetChildren())
        {
            if (child != _playerCapsule && GodotObject.IsInstanceValid(child))
            {
                child.GetParent()?.RemoveChild(child);
                child.QueueFree();
            }
        }
        foreach (var child in _doorsContainer.GetChildren())
        {
            if (GodotObject.IsInstanceValid(child))
            {
                child.GetParent()?.RemoveChild(child);
                child.QueueFree();
            }
        }
        _spawnedDoors.Clear();
        _activeEntities.Clear();
        _currentTarget = null; // Clear target on world change
        if (_playerCapsule != null)
        {
            _activeEntities["player_self"] = _playerCapsule;
        }
        _spawnCounter = 0;
        _enemyTargetIndex = -1;
        _friendlyTargetIndex = -1;
    }
    public void RebuildZoneBoundaries(System.Text.Json.JsonElement mapSizeElement, System.Text.Json.JsonElement zoneLines, System.Text.Json.JsonElement centerOffsetElement)
    {
        // Delete the static legacy CSG Floor if it exists at the root level
        var rootFloor = GetNodeOrNull<Node3D>("Floor");
        if (rootFloor != null)
        {
            rootFloor.QueueFree();
        }

        // Clear old boundaries surgically
        foreach (var child in _boundariesContainer.GetChildren())
        {
            string name = child.Name;
            if (name == "VisualFloor" || name == "PhysicalFloor" || name == "Floor" || name.StartsWith("Wall_") || name.StartsWith("ZoneLine_"))
            {
                child.GetParent()?.RemoveChild(child);
                child.QueueFree();
            }
        }

        // Map dimensions
        float mapWidth = 400f;
        float mapLength = 400f;
        if (mapSizeElement.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            mapWidth = mapSizeElement.TryGetProperty("width", out var wElem) ? wElem.GetSingle() : 400f;
            mapLength = mapSizeElement.TryGetProperty("length", out var lElem) ? lElem.GetSingle() : 400f;
        }
        
        // Center offset
        float offsetX = 0f;
        float offsetZ = 0f;
        if (centerOffsetElement.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            float rawOffsetX = centerOffsetElement.TryGetProperty("x", out var cxv) ? cxv.GetSingle() : 0f;
            float rawOffsetZ = centerOffsetElement.TryGetProperty("y", out var czv) ? czv.GetSingle() : 0f;
            
            // Map EQ Coords (+X=West, +Y=North) to Godot Coords (-X=West, -Z=North)
            offsetX = -rawOffsetX;
            offsetZ = -rawOffsetZ;
        }
        
        float halfWidth = mapWidth / 2f;
        float halfLength = mapLength / 2f;
        float wallHeight = 50f;
        float wallThick = 10f;

        // Delete the static legacy CSG Floor if it exists so we can generate real meshes
        var csgFloor = GetNodeOrNull<CsgBox3D>("Floor");
        if (csgFloor != null)
        {
            csgFloor.QueueFree();
        }
        
        // Only build the fallback visual floor if there's NO GLB zone geometry loaded
        bool hasGlbGeometry = _zoneGeometryContainer != null && _zoneGeometryContainer.GetChildCount() > 0;
        if (!hasGlbGeometry)
        {
            var floorMeshInstance = new MeshInstance3D { Name = "VisualFloor" };
            var boxMesh = new BoxMesh { Size = new Vector3(mapWidth, 1f, mapLength) };
            var grassMat = new StandardMaterial3D {
                AlbedoColor = new Color(0.2f, 0.4f, 0.15f), // Grass Green
                Roughness = 0.8f
            };
            boxMesh.Material = grassMat;
            floorMeshInstance.Mesh = boxMesh;
            floorMeshInstance.Position = new Vector3(offsetX, -0.5f, offsetZ); 
            _boundariesContainer.AddChild(floorMeshInstance);
            
            // Build instant mathematical Physics floor padded generously below walls
            var staticFloor = new StaticBody3D { Name = "PhysicalFloor" };
            var floorCol = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(mapWidth + 100f, 10f, mapLength + 100f) } };
            staticFloor.AddChild(floorCol);
            staticFloor.Position = new Vector3(offsetX, -5f, offsetZ); 
            _boundariesContainer.AddChild(staticFloor);

            // Define the 4 cardinal edges relative to the centerOffset
            var edges = new[] {
                new { name = "North",     pos = new Vector3(offsetX, 0, offsetZ - halfLength - wallThick/2), size = new Vector3(mapWidth + wallThick*2, wallHeight, wallThick) },
                new { name = "South",     pos = new Vector3(offsetX, 0, offsetZ + halfLength + wallThick/2),  size = new Vector3(mapWidth + wallThick*2, wallHeight, wallThick) },
                new { name = "East",      pos = new Vector3(offsetX + halfWidth + wallThick/2, 0, offsetZ),  size = new Vector3(wallThick, wallHeight, mapLength) },
                new { name = "West",      pos = new Vector3(offsetX - halfWidth - wallThick/2, 0, offsetZ), size = new Vector3(wallThick, wallHeight, mapLength) }
            };

            // Create solid invisible walls
            foreach (var edge in edges)
            {
                var staticBody = new StaticBody3D { Name = $"Wall_{edge.name}" };
                var col = new CollisionShape3D();
                var box = new BoxShape3D { Size = edge.size };
                col.Shape = box;
                staticBody.AddChild(col);
                staticBody.Position = edge.pos + new Vector3(0, wallHeight/2f, 0); // rest on floor
                _boundariesContainer.AddChild(staticBody);
            }
        }

        // Add Topographical Zone triggers
        if (zoneLines.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var zl in zoneLines.EnumerateArray())
            {
                string targetZone = zl.TryGetProperty("target", out var tz) ? tz.GetString() : "unknown";
                
                // PoK books are interactive objects, not walk-in spatial triggers.
                // Creating a giant trigger box for them blocks the zone (especially Kelethin).
                if (targetZone.ToLower() == "poknowledge") continue;
                
                // Check if this is a coordinate-based zone point (from DB) or edge-based (legacy)
                bool hasCoords = zl.TryGetProperty("x", out _);
                
                var triggerArea = new Area3D { Name = $"ZoneLine_{targetZone}" };
                var col = new CollisionShape3D();
                
                Vector3 triggerPos;
                
                if (hasCoords)
                {
                    Vector3 triggerSize;

                    // Check for BSP-derived precise trigger bounds from S3D client data
                    bool hasBspMin = zl.TryGetProperty("bspMin", out var bspMinProp);
                    bool hasBspMax = zl.TryGetProperty("bspMax", out var bspMaxProp);
                    bool hasBsp = hasBspMin && hasBspMax;

                    if (hasBsp)
                    {
                        // BSP AABB: exact trigger volume from original EQ client files
                        // EQ coords: x=east/west, y=north/south, z=height
                        // Godot coords: X=-EQ.x, Y=EQ.z, Z=-EQ.y (negated X and Y, Z→Y)
                        float eqMinX = bspMinProp.TryGetProperty("x", out var mnx) ? mnx.GetSingle() : 0;
                        float eqMinY = bspMinProp.TryGetProperty("y", out var mny) ? mny.GetSingle() : 0;
                        float eqMinZ = bspMinProp.TryGetProperty("z", out var mnz) ? mnz.GetSingle() : 0;
                        float eqMaxX = bspMaxProp.TryGetProperty("x", out var mxx) ? mxx.GetSingle() : 0;
                        float eqMaxY = bspMaxProp.TryGetProperty("y", out var mxy) ? mxy.GetSingle() : 0;
                        float eqMaxZ = bspMaxProp.TryGetProperty("z", out var mxz) ? mxz.GetSingle() : 0;

                        // Convert EQ AABB to Godot AABB
                        float godotMinX = -eqMaxX;  // Godot X = -EQ.X (flip)
                        float godotMaxX = -eqMinX;
                        float godotMinY = eqMinZ;   // Godot Y = EQ.Z (height)
                        float godotMaxY = eqMaxZ;
                        float godotMinZ = -eqMaxY;  // Godot Z = -EQ.Y (flip)
                        float godotMaxZ = -eqMinY;

                        float sizeX = godotMaxX - godotMinX;
                        float sizeY = godotMaxY - godotMinY;
                        float sizeZ = godotMaxZ - godotMinZ;

                        triggerSize = new Vector3(sizeX, sizeY, sizeZ);
                        triggerPos = new Vector3(
                            (godotMinX + godotMaxX) / 2f,
                            (godotMinY + godotMaxY) / 2f,
                            (godotMinZ + godotMaxZ) / 2f
                        );

                        GD.Print($"[WORLD] BSP ZoneLine → {targetZone}: pos=({triggerPos.X:F1},{triggerPos.Y:F1},{triggerPos.Z:F1}), size=({sizeX:F1},{sizeY:F1},{sizeZ:F1})");
                    }
                    else
                    {
                        // Fallback: use center + size from DB zone_points
                        float zpX = zl.TryGetProperty("x", out var zpxp) ? zpxp.GetSingle() : 0;
                        float zpY = zl.TryGetProperty("y", out var zpyp) ? zpyp.GetSingle() : 0;
                        string orient = zl.TryGetProperty("orientation", out var op) ? op.GetString() : "ns";
                        float trigWidth = zl.TryGetProperty("width", out var twp) ? twp.GetSingle() : 50f;
                        float trigLength = zl.TryGetProperty("length", out var tlp) ? tlp.GetSingle() : 100f;
                        
                        float gx = -zpX;
                        float gz = -zpY;
                        
                        if (orient == "ew")
                            triggerSize = new Vector3(trigLength, wallHeight, trigWidth);
                        else
                            triggerSize = new Vector3(trigWidth, wallHeight, trigLength);
                        
                        triggerPos = new Vector3(gx, wallHeight / 2f, gz);
                    }

                    col.Shape = new BoxShape3D { Size = triggerSize };
                    
                    // Debug visualizer
                    var debugMesh = new CsgBox3D { Size = triggerSize };
                    var debugMat = new StandardMaterial3D {
                        AlbedoColor = new Color(0, 0, 1, 0.3f),
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
                    };
                    debugMesh.Material = debugMat;
                    triggerArea.AddChild(debugMesh);
                }
                else
                {
                    // Legacy edge-based zone line
                    string edge = zl.TryGetProperty("edge", out var ge) ? ge.GetString() : "north";
                    float width = zl.TryGetProperty("width", out var ww) ? ww.GetSingle() : 200f;
                    float rawOffset = zl.TryGetProperty("offset", out var oo) ? oo.GetSingle() : 0f;
                    float offset = -rawOffset;
                    float triggerDepth = 20f;

                    Vector3 triggerSize;
                    if (edge == "north") {
                        triggerSize = new Vector3(width, wallHeight, triggerDepth);
                        triggerPos = new Vector3(offsetX + offset, wallHeight/2f, offsetZ - halfLength + triggerDepth/2);
                    } else if (edge == "south") {
                        triggerSize = new Vector3(width, wallHeight, triggerDepth);
                        triggerPos = new Vector3(offsetX + offset, wallHeight/2f, offsetZ + halfLength - triggerDepth/2);
                    } else if (edge == "east") {
                        triggerSize = new Vector3(triggerDepth, wallHeight, width);
                        triggerPos = new Vector3(offsetX + halfWidth - triggerDepth/2, wallHeight/2f, offsetZ + offset);
                    } else { // west
                        triggerSize = new Vector3(triggerDepth, wallHeight, width);
                        triggerPos = new Vector3(offsetX - halfWidth + triggerDepth/2, wallHeight/2f, offsetZ + offset);
                    }

                    col.Shape = new BoxShape3D { Size = triggerSize };

                    // Debug visualizer
                    var debugMesh = new CsgBox3D { Size = triggerSize };
                    var debugMat = new StandardMaterial3D {
                        AlbedoColor = new Color(0, 0, 1, 0.3f),
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
                    };
                    debugMesh.Material = debugMat;
                    triggerArea.AddChild(debugMesh);
                }

                triggerArea.AddChild(col);
                triggerArea.Position = triggerPos;

                // Connect signal
                float targetX = zl.TryGetProperty("targetX", out var outTx) ? outTx.GetSingle() : 0f;
                float targetY = zl.TryGetProperty("targetY", out var outTy) ? outTy.GetSingle() : 0f;
                float targetZ = zl.TryGetProperty("targetZ", out var outTz) ? outTz.GetSingle() : 0f;

                triggerArea.BodyEntered += (body) => {
                    if (body == _playerCapsule && _zoneImmunityTimer <= 0)
                    {
                        GD.Print($"[WORLD] Player crossed Zone Line: {targetZone} (Target: {targetX},{targetY},{targetZ})");
                        CallDeferred(MethodName.EmitSignal, SignalName.ZoneLineCrossed, targetZone, targetX, targetY, targetZ);
                    }
                };

                _boundariesContainer.AddChild(triggerArea);
            }
        }
    }
    public void LoadZoneMap(string zoneId)
    {
        ClearWorld();
        
        _currentZoneId = zoneId;
        ApplyTimeOfDayVisuals(); // Instantly apply indoor/outdoor sky logic

        if (_zoneGeometryContainer == null)
        {
            _zoneGeometryContainer = new Node3D { Name = "ZoneGeometry" };
            AddChild(_zoneGeometryContainer);
        }
        else
        {
            foreach (var child in _zoneGeometryContainer.GetChildren())
            {
                child.GetParent()?.RemoveChild(child);
                child.QueueFree();
            }
        }

        // Clean up previous zone objects
        if (_zoneObjectsContainer != null && GodotObject.IsInstanceValid(_zoneObjectsContainer))
        {
            _zoneObjectsContainer.QueueFree();
            _zoneObjectsContainer = null;
        }
        _objectPlacer?.ClearCache();

        if (_materialAnimator == null)
        {
            _materialAnimator = new MaterialAnimator { Name = "MaterialAnimator" };
            AddChild(_materialAnimator);
        }
        _materialAnimator.ClearAll();

        var cache = EQAssetCache.Instance;

        // Try extracted EQ asset cache (from player's EQ install)
        if (cache.HasZone(zoneId))
        {
            string cachedGlb = cache.GetZoneGlbPath(zoneId);
            if (cachedGlb != null)
            {
                GD.Print($"[WORLD] Loading zone from asset cache: {cachedGlb}");
                if (LoadZoneGlbFromDisk(zoneId, cachedGlb))
                {
                    // Clean up fallback VisualFloor since we have real geometry
                    var vf = _boundariesContainer?.GetNodeOrNull<Node3D>("VisualFloor");
                    if (vf != null)
                    {
                        _boundariesContainer.RemoveChild(vf);
                        vf.QueueFree();
                    }

                    // Forcefully clean up the legacy CSG Floor just in case RebuildZoneBoundaries missed it
                    var rootFloor = GetNodeOrNull<Node3D>("Floor");
                    if (rootFloor != null)
                    {
                        RemoveChild(rootFloor);
                        rootFloor.QueueFree();
                    }

                    // Place zone objects (trees, buildings, torches, etc.)
                    PlaceZoneObjects(zoneId, cache.GetZonePath(zoneId));
                    // Play zone music
                    PlayZoneMusic(zoneId);
                    return;
                }
            }
        }

        // --- Fallback: Brewall line-based map ---
        LoadZoneBrewall(zoneId);
        PlayZoneMusic(zoneId);
    }
    private bool LoadZoneGlbFromDisk(string zoneId, string absolutePath)
    {
        try
        {
            var gltfDoc = new GltfDocument();
            var gltfState = new GltfState();

            var err = gltfDoc.AppendFromFile(absolutePath, gltfState);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[WORLD] GLTF parse error for cached GLB: {err}");
                return false;
            }

            Node scene = gltfDoc.GenerateScene(gltfState);
            if (scene == null)
            {
                GD.PrintErr("[WORLD] GLTF generated null scene from cache");
                return false;
            }

            // Lantern bakes a mesh transform of scale=(-0.1,-0.1,-0.1) rot=(180,0,0)
            // which maps local vertices (x,y,z) → (-0.1x, 0.1y, 0.1z).
            // The bundled EQ Advanced Maps GLBs use world coords where:
            //   world.X = -local.Z,  world.Y = local.Y,  world.Z = -local.X
            // So the parent transform must map the mesh output to match.
            // Parent basis: (u,v,w) → (-10w, 10v, 10u) compensates the 0.1 scale
            // and swaps/negates X↔Z to match the bundled coordinate space.
            var zoneRoot = new Node3D { Name = $"GLB_{zoneId}" };
            zoneRoot.Transform = new Transform3D(
                new Vector3(0, 0, 10),    // X basis: parent X comes from 10 * child Z
                new Vector3(0, 10, 0),    // Y basis: parent Y comes from 10 * child Y
                new Vector3(-10, 0, 0),   // Z basis: parent Z comes from -10 * child X
                Vector3.Zero
            );
            zoneRoot.AddChild(scene);
            _zoneGeometryContainer.AddChild(zoneRoot);

            // Register Animated Materials for the zone
            string cachePath = EQAssetCache.Instance.GetZonePath(zoneId);
            string zoneMatList = System.IO.Path.Combine(cachePath, "Zone", "MaterialLists", $"{zoneId}.txt");
            Dictionary<string, (string[] frames, float delay)> animData = null;
            if (System.IO.File.Exists(zoneMatList))
            {
                animData = ParseMaterialList(zoneMatList);
            }

            // Fix Materials: Strip Unshaded and apply proper roughness so clustered lighting works.
            FixZoneMaterials(zoneRoot, animData);

            if (animData != null && animData.Count > 0)
            {
                string texturesDir = System.IO.Path.Combine(cachePath, "Zone", "Textures");
                RegisterAnimationsRecursive(scene, animData, texturesDir);
            }

            // Generate collision for walkable geometry
            int meshCount = 0;
            int collisionCount = 0;
            GenerateCollisionRecursive(scene, ref meshCount, ref collisionCount, null, animData);

            // Calculate bounds for boundary walls
            var aabb = CalculateWorldAabb(zoneRoot);
            if (aabb.Size.Length() > 0)
            {
                float pad = 50f;
                float mapWidth = aabb.Size.X + pad * 2;
                float mapLength = aabb.Size.Z + pad * 2;
                float centerX = aabb.Position.X + aabb.Size.X / 2f;
                float centerZ = aabb.Position.Z + aabb.Size.Z / 2f;

                GD.Print($"[WORLD] Cached GLB bounds: pos=({aabb.Position}), size=({aabb.Size})");
                RebuildBoundaryWallsOnly(centerX, centerZ, mapWidth, mapLength, aabb.Position.Y - 10f);
            }

            GD.Print($"[WORLD] Cached GLB loaded: {meshCount} meshes, {collisionCount} collision shapes for {zoneId}");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[WORLD] Cached GLB load exception: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }
    private void PlaceZoneObjects(string zoneId, string cachePath)
    {
        _objectPlacer ??= new ZoneObjectPlacer();
        _objectPlacer.ShadowsEnabled = DynamicShadowsEnabled;
        _objectPlacer.Animator = _materialAnimator;

        if (_zoneGeometryContainer == null) return;

        _zoneObjectsContainer = _objectPlacer.PlaceObjects(zoneId, cachePath, _zoneGeometryContainer);
        _objectPlacer.PlaceLights(zoneId, cachePath, _zoneGeometryContainer);
    }
    public void PlayZoneMusic(string zoneId)
    {
        if (_musicPlayer == null)
        {
            _musicPlayer = new ZoneMusicPlayer();
            _musicPlayer.Name = "ZoneMusicPlayer";
            AddChild(_musicPlayer);
        }

        _musicPlayer.PlayZoneMusic(zoneId);
    }

    public void PlayZoneAmbience(string trackName)
    {
        if (_musicPlayer == null)
        {
            _musicPlayer = new ZoneMusicPlayer();
            _musicPlayer.Name = "ZoneMusicPlayer";
            AddChild(_musicPlayer);
        }

        _musicPlayer.PlayZoneAmbience(trackName);
    }
    private void GenerateCollisionRecursive(Node node, ref int meshCount, ref int collisionCount, AnimatableBody3D targetBody = null, Dictionary<string, (string[] frames, float delay)> animData = null)
    {
        if (node is MeshInstance3D meshInst && meshInst.Mesh != null)
        {
            // Check if this mesh contains ANY liquid surfaces
            bool hasSolid = false;
            var faces = new System.Collections.Generic.List<Vector3>();
            var liquidFaces = new System.Collections.Generic.List<Vector3>();

            if (meshInst.Mesh is ArrayMesh arrayMesh)
            {
                for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
                {
                    // Use GetActiveMaterial to catch overrides from the GLTF importer
                    var mat = meshInst.GetActiveMaterial(i);
                    bool isLiquid = mat != null && IsLiquidMaterial(mat, animData);
                    
                    if (!isLiquid) hasSolid = true;
                    
                    var arrays = arrayMesh.SurfaceGetArrays(i);
                    var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var indicesObj = arrays[(int)Mesh.ArrayType.Index];
                    
                    if (indicesObj.VariantType != Variant.Type.Nil)
                    {
                        var indices = indicesObj.AsInt32Array();
                        for (int j = 0; j < indices.Length; j++)
                        {
                            if (isLiquid) liquidFaces.Add(vertices[indices[j]]);
                            else faces.Add(vertices[indices[j]]);
                        }
                    }
                    else
                    {
                        for (int j = 0; j < vertices.Length; j++)
                        {
                            if (isLiquid) liquidFaces.Add(vertices[j]);
                            else faces.Add(vertices[j]);
                        }
                    }
                }
            }
            else
            {
                // Fallback for non-ArrayMesh (unlikely from GLTF)
                hasSolid = true;
                // We'd have to just use the whole mesh, but GLTF is always ArrayMesh
            }
            
            if (hasSolid && faces.Count > 0)
            {
                meshCount++;
                collisionCount++;

                var shape = new ConcavePolygonShape3D();
                shape.SetFaces(faces.ToArray());

                var colShape = new CollisionShape3D { Shape = shape };
                var staticBody = new StaticBody3D();
                staticBody.AddChild(colShape);
                
                if (targetBody != null)
                {
                    // Move collision shape directly to the target AnimatableBody3D
                    Transform3D relativeTransform = targetBody.GlobalTransform.AffineInverse() * meshInst.GlobalTransform;
                    staticBody.RemoveChild(colShape);
                    targetBody.AddChild(colShape);
                    colShape.Transform = relativeTransform;
                    staticBody.QueueFree(); // Don't need the static body
                }
                else
                {
                    staticBody.InputRayPickable = false;
                    meshInst.AddChild(staticBody);
                }
            }
            
            if (liquidFaces.Count > 0)
            {
                var shape = new ConcavePolygonShape3D();
                shape.SetFaces(liquidFaces.ToArray());

                var colShape = new CollisionShape3D { Shape = shape };
                var waterArea = new Area3D();
                waterArea.Name = "WaterArea";
                
                waterArea.AddChild(colShape);
                meshInst.AddChild(waterArea);
                
                waterArea.BodyEntered += (Node3D body) => {
                    if (body is EntityCapsule ec) ec.SetInWater(true);
                };
                waterArea.BodyExited += (Node3D body) => {
                    if (body is EntityCapsule ec) ec.SetInWater(false);
                };
            }
        }

        foreach (var child in node.GetChildren())
        {
            GenerateCollisionRecursive(child, ref meshCount, ref collisionCount, targetBody, animData);
        }
    }

    private bool IsLiquidMaterial(Material material, Dictionary<string, (string[] frames, float delay)> animData = null)
    {
        if (material == null) return false;
        
        // 1. Check material name (resource name)
        string matName = material.ResourceName;
        if (!string.IsNullOrEmpty(matName))
        {
            if (IsLiquidName(matName.ToLower())) return true;
        }

        // 2. Check texture path (often more reliable for GLTF imports)
        if (material is StandardMaterial3D stdMat && stdMat.AlbedoTexture != null)
        {
            string texPath = stdMat.AlbedoTexture.ResourcePath.ToLower();
            if (IsLiquidName(texPath)) return true;
        }
        
        // 3. Check animated frames
        if (!string.IsNullOrEmpty(matName) && animData != null && animData.TryGetValue(matName, out var anim))
        {
            foreach (var frame in anim.frames)
            {
                if (IsLiquidName(frame.ToLower())) return true;
            }
        }
        
        return false;
    }

    private bool IsLiquidName(string name)
    {
        // Many variations of water and lava
        return name.Contains("water") || name.Contains("wawa") || name.Contains("fwater") || 
               name.Contains("swater") || name.Contains("lava") || name.Contains("slime") ||
               name.Contains("liquid") || name.Contains("pool") || name.Contains("p_water") ||
               name == "ow1" || name == "w1" || name == "fw1" || name == "sw1" || 
               name.StartsWith("falls") || name == "t50_w1" || name == "d_ow1" ||
               name.Contains("pok_water") || name.Contains("pok_pool");
    }


    private void FixZoneMaterials(Node node, Dictionary<string, (string[] frames, float delay)> animData = null)
    {
        if (node is MeshInstance3D meshInst)
        {
            for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
            {
                var mat = meshInst.GetSurfaceOverrideMaterial(i) as StandardMaterial3D;
                if (mat != null)
                {
                    mat.ShadingMode = StandardMaterial3D.ShadingModeEnum.PerPixel;
                    mat.Roughness = 1.0f;
                    mat.Metallic = 0.0f;
                    mat.MetallicSpecular = 0.0f;
                    
                    if (IsLiquidMaterial(mat, animData))
                    {
                        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                        
                        string n = mat.ResourceName.ToLower();
                        if (n.Contains("lava")) {
                            mat.AlbedoColor = new Color(0.8f, 0.2f, 0.0f, 0.85f);
                            mat.EmissionEnabled = true;
                            mat.Emission = new Color(0.8f, 0.2f, 0.0f);
                            mat.EmissionEnergyMultiplier = 2.0f;
                        } else if (n.Contains("slime")) {
                            mat.AlbedoColor = new Color(0.1f, 0.8f, 0.1f, 0.75f);
                        } else {
                            // Water
                            mat.AlbedoColor = new Color(0.1f, 0.3f, 0.6f, 0.65f);
                        }
                    }
                    else
                    {
                        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                    }
                    
                    // UNIVERSAL BACKLIGHT FIX: 
                    // This forces the polygon to receive light from BOTH SIDES.
                    // This fixes the "half-black room" issue caused by flipped normals in the map.
                    mat.BacklightEnabled = true;
                    mat.Backlight = new Color(1, 1, 1);
                }
            }

            if (meshInst.Mesh != null)
            {
                for (int i = 0; i < meshInst.Mesh.GetSurfaceCount(); i++)
                {
                    var mat = meshInst.Mesh.SurfaceGetMaterial(i) as StandardMaterial3D;
                    if (mat != null)
                    {
                    var newMat = mat.Duplicate(true) as StandardMaterial3D;
                    newMat.ShadingMode = StandardMaterial3D.ShadingModeEnum.PerPixel;
                    newMat.Roughness = 1.0f;
                    newMat.Metallic = 0.0f;
                    newMat.MetallicSpecular = 0.0f;
                    
                    if (IsLiquidMaterial(newMat, animData))
                    {
                        newMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                        newMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                        
                        string n = newMat.ResourceName.ToLower();
                        if (n.Contains("lava")) {
                            newMat.AlbedoColor = new Color(0.8f, 0.2f, 0.0f, 0.85f);
                            newMat.EmissionEnabled = true;
                            newMat.Emission = new Color(0.8f, 0.2f, 0.0f);
                            newMat.EmissionEnergyMultiplier = 2.0f;
                        } else if (n.Contains("slime")) {
                            newMat.AlbedoColor = new Color(0.1f, 0.8f, 0.1f, 0.75f);
                        } else {
                            // Water
                            newMat.AlbedoColor = new Color(0.1f, 0.3f, 0.6f, 0.65f);
                        }
                    }
                    // We purposefully DO NOT disable culling for solid materials anymore!
                    // Disabling culling on solids caused inward-facing developer textures (like zone lines or "GOTO" walls)
                    // to become visible from the outside, appearing inside-out. Default Godot culling (Back) hides them properly.
                    
                    // Apply backlight to surface overrides too
                    newMat.BacklightEnabled = true;
                    newMat.Backlight = new Color(1, 1, 1);
                    
                    meshInst.SetSurfaceOverrideMaterial(i, newMat);
                    }
                }
            }
        }

        foreach (var child in node.GetChildren())
        {
            FixZoneMaterials(child, animData);
        }
    }
    private void CalculateAabbRecursive(Node node, ref Aabb combined, ref bool first)
    {
        if (node is MeshInstance3D meshInst && meshInst.Mesh != null)
        {
            var worldAabb = meshInst.GlobalTransform * meshInst.Mesh.GetAabb();
            if (first)
            {
                combined = worldAabb;
                first = false;
            }
            else
            {
                combined = combined.Merge(worldAabb);
            }
        }
        foreach (var child in node.GetChildren())
        {
            CalculateAabbRecursive(child, ref combined, ref first);
        }
    }
    private void RebuildBoundaryWallsOnly(float centerX, float centerZ, float mapWidth, float mapLength, float floorY)
    {
        // Remove old floor/boundary elements
        foreach (var child in _boundariesContainer.GetChildren())
        {
            string name = child.Name;
            if (name == "VisualFloor" || name == "PhysicalFloor" || name.StartsWith("Wall_"))
            {
                child.GetParent()?.RemoveChild(child);
                child.QueueFree();
            }
        }

        // Physics-only floor as a safety net below the zone geometry
        var staticFloor = new StaticBody3D { Name = "PhysicalFloor" };
        var floorCol = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(mapWidth + 400f, 2f, mapLength + 400f) } };
        staticFloor.AddChild(floorCol);
        staticFloor.Position = new Vector3(centerX, floorY, centerZ);
        _boundariesContainer.AddChild(staticFloor);

        // Invisible boundary walls
        float wallHeight = 100f;
        float wallThick = 10f;
        float halfW = mapWidth / 2f;
        float halfL = mapLength / 2f;

        var edges = new[] {
            new { name = "North", pos = new Vector3(centerX, 0, centerZ - halfL - wallThick/2), size = new Vector3(mapWidth + wallThick*2, wallHeight, wallThick) },
            new { name = "South", pos = new Vector3(centerX, 0, centerZ + halfL + wallThick/2), size = new Vector3(mapWidth + wallThick*2, wallHeight, wallThick) },
            new { name = "East",  pos = new Vector3(centerX + halfW + wallThick/2, 0, centerZ), size = new Vector3(wallThick, wallHeight, mapLength) },
            new { name = "West",  pos = new Vector3(centerX - halfW - wallThick/2, 0, centerZ), size = new Vector3(wallThick, wallHeight, mapLength) }
        };

        foreach (var edge in edges)
        {
            var staticBody = new StaticBody3D { Name = $"Wall_{edge.name}" };
            var col = new CollisionShape3D { Shape = new BoxShape3D { Size = edge.size } };
            staticBody.AddChild(col);
            staticBody.Position = edge.pos + new Vector3(0, wallHeight / 2f, 0);
            _boundariesContainer.AddChild(staticBody);
        }
    }
    private void LoadZoneBrewall(string zoneId)
    {
        string path = $"res://Data/Maps/{zoneId}_map.json";
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.Print($"[WORLD] No map geometry found for {zoneId} at {path}");
            return;
        }

        GD.Print($"[WORLD] Loading Brewall map geometry for {zoneId}...");
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        string json = file.GetAsText();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            // --- Read bounds and rebuild floor/boundaries to match map geometry ---
            if (root.TryGetProperty("bounds", out var boundsEl))
            {
                float bMinX = boundsEl.GetProperty("minX").GetSingle();
                float bMaxX = boundsEl.GetProperty("maxX").GetSingle();
                float bMinY = boundsEl.GetProperty("minY").GetSingle();
                float bMaxY = boundsEl.GetProperty("maxY").GetSingle();
                
                // Convert EQ coords to Godot: negate both axes
                float gMinX = -bMaxX;
                float gMaxX = -bMinX;
                float gMinZ = -bMaxY;
                float gMaxZ = -bMinY;
                
                float pad = 100f; // Extra padding around the geometry
                float mapWidth = (gMaxX - gMinX) + pad * 2;
                float mapLength = (gMaxZ - gMinZ) + pad * 2;
                float centerX = (gMinX + gMaxX) / 2f;
                float centerZ = (gMinZ + gMaxZ) / 2f;
                
                GD.Print($"[WORLD] Map bounds (Godot): X({gMinX} to {gMaxX}), Z({gMinZ} to {gMaxZ}), Center({centerX}, {centerZ}), Size({mapWidth}x{mapLength})");
                
                // Rebuild floor and boundaries to fit the map geometry
                RebuildFloorForMap(centerX, centerZ, mapWidth, mapLength);
            }

            if (root.TryGetProperty("categorizedLines", out var catLines))
            {
                // Define materials for each category
                var wallMat = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.5f, 0.5f) }; // Stone Grey
                var pathMat = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.5f, 0.35f) }; // Brown/Dirt
                var waterMat = new StandardMaterial3D { 
                    AlbedoColor = new Color(0.1f, 0.3f, 0.8f, 0.6f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha 
                };
                var dangerMat = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.15f, 0.1f) }; // Red
                var otherMat = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.4f, 0.3f) }; // Muted olive

                // Category → (material, wallHeight, wallThickness)
                var categories = new (string name, StandardMaterial3D mat, float height, float thickness)[] {
                    ("walls",  wallMat,  5f, 0.8f),
                    ("paths",  pathMat,  0.5f, 2.0f),
                    ("water",  waterMat, 0.3f, 3.0f),
                    ("danger", dangerMat, 3f, 1.0f),
                    ("other",  otherMat, 2f, 0.6f),
                };

                int totalBuilt = 0;
                foreach (var (catName, mat, wallHeight, wallThickness) in categories)
                {
                    if (!catLines.TryGetProperty(catName, out var segments)) continue;
                    
                    int catCount = 0;
                    foreach (var seg in segments.EnumerateArray())
                    {
                        var startArr = seg.GetProperty("start");
                        var endArr = seg.GetProperty("end");

                        float x1 = -startArr[0].GetSingle();
                        float z1 = -startArr[1].GetSingle();
                        float y1 = startArr[2].GetSingle(); // Height

                        float x2 = -endArr[0].GetSingle();
                        float z2 = -endArr[1].GetSingle();
                        float y2 = endArr[2].GetSingle();

                        Vector3 startPos = new Vector3(x1, y1, z1);
                        Vector3 endPos = new Vector3(x2, y2, z2);

                        Vector3 midPoint = (startPos + endPos) / 2f;
                        float length = new Vector2(startPos.X, startPos.Z).DistanceTo(new Vector2(endPos.X, endPos.Z));
                        
                        // Skip zero-length segments in XZ plane to prevent LookAt errors
                        if (length < 0.01f) continue;

                        var staticBody = new StaticBody3D();
                        
                        // Only add collision for walls and danger (not paths/water/other)
                        if (catName == "walls" || catName == "danger")
                        {
                            var col = new CollisionShape3D();
                            var shape = new BoxShape3D { Size = new Vector3(wallThickness, wallHeight, length) };
                            col.Shape = shape;
                            staticBody.AddChild(col);
                        }
                        
                        var meshInst = new MeshInstance3D();
                        var mesh = new BoxMesh { Size = new Vector3(wallThickness, wallHeight, length), Material = mat };
                        meshInst.Mesh = mesh;

                        staticBody.AddChild(meshInst);

                        // Position at midpoint; for walls raise above ground, for paths/water sit at ground level
                        float yOffset = (catName == "walls" || catName == "danger") ? (wallHeight / 2f) : 0.1f;
                        staticBody.Position = new Vector3(midPoint.X, midPoint.Y + yOffset, midPoint.Z);
                        
                        // Orient the segment along the line direction
                        staticBody.LookAtFromPosition(staticBody.Position, new Vector3(endPos.X, staticBody.Position.Y, endPos.Z), Vector3.Up);

                        _zoneGeometryContainer.AddChild(staticBody);
                        catCount++;
                    }
                    if (catCount > 0)
                        GD.Print($"[WORLD] Built {catCount} {catName} segments for {zoneId}.");
                    totalBuilt += catCount;
                }
                GD.Print($"[WORLD] Total: {totalBuilt} segments built for {zoneId}.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[WORLD] Error parsing map JSON: {ex.Message}");
        }
    }
    private void RebuildFloorForMap(float centerX, float centerZ, float mapWidth, float mapLength)
    {
        // Remove old floor/boundaries created by RebuildZoneBoundaries
        foreach (var child in _boundariesContainer.GetChildren())
        {
            string name = child.Name;
            // Only replace the floor elements, keep zone line triggers
            if (name == "VisualFloor" || name == "PhysicalFloor" || 
                name.StartsWith("Wall_"))
            {
                child.GetParent()?.RemoveChild(child);
                child.QueueFree();
            }
        }

        // Only build the fallback visual floor if there's NO GLB zone geometry loaded
        bool hasGlbGeometry = _zoneGeometryContainer != null && _zoneGeometryContainer.GetChildCount() > 0;
        if (!hasGlbGeometry)
        {
            var floorMeshInstance = new MeshInstance3D { Name = "VisualFloor" };
            var boxMesh = new BoxMesh { Size = new Vector3(mapWidth, 1f, mapLength) };
            var grassMat = new StandardMaterial3D {
                AlbedoColor = new Color(0.2f, 0.4f, 0.15f),
                Roughness = 0.8f
            };
            boxMesh.Material = grassMat;
            floorMeshInstance.Mesh = boxMesh;
            floorMeshInstance.Position = new Vector3(centerX, -0.5f, centerZ);
            _boundariesContainer.AddChild(floorMeshInstance);
        }
        
        // Build physical floor with generous padding
        var staticFloor = new StaticBody3D { Name = "PhysicalFloor" };
        var floorCol = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(mapWidth + 200f, 10f, mapLength + 200f) } };
        staticFloor.AddChild(floorCol);
        staticFloor.Position = new Vector3(centerX, -5f, centerZ);
        _boundariesContainer.AddChild(staticFloor);

        // Invisible boundary walls
        float wallHeight = 50f;
        float wallThick = 10f;
        float halfW = mapWidth / 2f;
        float halfL = mapLength / 2f;

        var edges = new[] {
            new { name = "North", pos = new Vector3(centerX, 0, centerZ - halfL - wallThick/2), size = new Vector3(mapWidth + wallThick*2, wallHeight, wallThick) },
            new { name = "South", pos = new Vector3(centerX, 0, centerZ + halfL + wallThick/2), size = new Vector3(mapWidth + wallThick*2, wallHeight, wallThick) },
            new { name = "East",  pos = new Vector3(centerX + halfW + wallThick/2, 0, centerZ), size = new Vector3(wallThick, wallHeight, mapLength) },
            new { name = "West",  pos = new Vector3(centerX - halfW - wallThick/2, 0, centerZ), size = new Vector3(wallThick, wallHeight, mapLength) }
        };

        foreach (var edge in edges)
        {
            var staticBody = new StaticBody3D { Name = $"Wall_{edge.name}" };
            var col = new CollisionShape3D();
            var box = new BoxShape3D { Size = edge.size };
            col.Shape = box;
            staticBody.AddChild(col);
            staticBody.Position = edge.pos + new Vector3(0, wallHeight/2f, 0);
            _boundariesContainer.AddChild(staticBody);
        }

        GD.Print($"[WORLD] Rebuilt floor/boundaries for map geometry: center=({centerX},{centerZ}), size=({mapWidth}x{mapLength})");
    }
    private void RegisterAnimationsRecursive(Node node, Dictionary<string, (string[] frames, float delay)> animData, string texturesDir)
    {
        if (node is MeshInstance3D meshInst)
        {
            for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (meshInst.GetSurfaceOverrideMaterial(i) is StandardMaterial3D mat)
                {
                    if (mat.ResourceName != null && animData.TryGetValue(mat.ResourceName, out var anim))
                    {
                        _materialAnimator.RegisterMaterial(mat, anim.frames, anim.delay, texturesDir);
                    }
                    else if (mat.ResourceName != null)
                    {
                        foreach (var kvp in animData)
                        {
                            if (kvp.Key.Equals(mat.ResourceName, StringComparison.OrdinalIgnoreCase))
                            {
                                _materialAnimator.RegisterMaterial(mat, kvp.Value.frames, kvp.Value.delay, texturesDir);
                                break;
                            }
                        }
                    }
                }
            }
            if (meshInst.Mesh != null)
            {
                for (int i = 0; i < meshInst.Mesh.GetSurfaceCount(); i++)
                {
                    if (meshInst.Mesh.SurfaceGetMaterial(i) is StandardMaterial3D mat)
                    {
                        if (mat.ResourceName != null && animData.TryGetValue(mat.ResourceName, out var anim))
                        {
                            _materialAnimator.RegisterMaterial(mat, anim.frames, anim.delay, texturesDir);
                        }
                        else if (mat.ResourceName != null)
                        {
                            foreach (var kvp in animData)
                            {
                                if (kvp.Key.Equals(mat.ResourceName, StringComparison.OrdinalIgnoreCase))
                                {
                                    _materialAnimator.RegisterMaterial(mat, kvp.Value.frames, kvp.Value.delay, texturesDir);
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
