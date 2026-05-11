using Godot;
using System;
using System.Collections.Generic;

public partial class WorldManager : Node3D
{
    // Set true for the duration of LoadZoneGlbFromDisk when we have baked corrected normals
    // into the zone meshes. Tells FixZoneMaterials to skip the legacy "BacklightEnabled"
    // back-face fill hack (which only existed to compensate for the inverted normals).
    private bool _zoneNormalsCorrectedThisLoad = false;

    /// <summary>
    /// True while the currently loaded zone uses the LanternExtractor-normal-flip lighting
    /// fix (see <see cref="BakeFlippedNormalsRecursive"/>). Read by EntityCapsule so that
    /// character / weapon meshes loaded into this zone get the same correction — otherwise
    /// the world looks correct but every character is still lit from the wrong side.
    /// </summary>
    public static bool LightFixActiveForCurrentZone { get; private set; } = false;

    public void FreezeForZoneLoad()
    {
        _teleportSettling = true;
        _loadingInitialZone = true;
        _teleportSafetyTimer = 0.0;
        if (_playerCapsule != null)
            _playerCapsule.Velocity = Vector3.Zero;
        GD.Print("[WORLD] Player frozen for zone load.");
    }

    /// <summary>
    /// After the world and collision are ready, nudge horizontal EQ position (X/Y) to match the
    /// last authoritative TeleportPlayer. Vertical (EQ Z) is left to <see cref="FinalizeTeleport"/> raycast
    /// plus authoritative clamp so we do not fight the +5 spawn boost before the floor snap.
    /// </summary>
    public void RefineSpawnPlacementToAuthoritativeEq()
    {
        if (!_authSpawnEqValid || _playerCapsule == null || !GodotObject.IsInstanceValid(_playerCapsule))
            return;

        // Client "EQ feet" from Godot: EQ.X = -godot.X, EQ.Y = -godot.Z, EQ.Z = godot.Y
        float curEqX = -_playerCapsule.GlobalPosition.X;
        float curEqY = -_playerCapsule.GlobalPosition.Z;

        float dEqX = _authSpawnEqX - curEqX;
        float dEqY = _authSpawnEqY - curEqY;

        const float Epsilon = 0.002f;
        if (Mathf.Abs(dEqX) < Epsilon && Mathf.Abs(dEqY) < Epsilon)
            return;

        // Horizontal only: godot += (-dEqX, 0, -dEqY) — same XZ map as TeleportPlayer; Y handled in FinalizeTeleport.
        Vector3 correction = new Vector3(-dEqX, 0f, -dEqY);
        const float MaxCorrection = 50f;
        if (correction.Length() > MaxCorrection)
        {
            GD.PrintErr($"[WORLD] RefineSpawn: horizontal correction {correction.Length():F1} clamped to {MaxCorrection} (auth EQ {_authSpawnEqX:F1},{_authSpawnEqY:F1} vs cur {curEqX:F1},{curEqY:F1})");
            correction = correction.Normalized() * MaxCorrection;
        }

        _playerCapsule.GlobalPosition += correction;
        _lastSentPos = _playerCapsule.GlobalPosition;
        GD.Print($"[WORLD] RefineSpawn: applied EQ horizontal delta ({dEqX:F3},{dEqY:F3}) → Godot Δ({correction.X:F3},{correction.Y:F3},{correction.Z:F3})");
    }

    /// <summary>Authoritative EQ alignment + floor raycast + unfreeze (use after a short post-teleport delay if not in initial zone load).</summary>
    public void FinishTeleportPlacement()
    {
        RefineSpawnPlacementToAuthoritativeEq();
        FinalizeTeleport();
    }

    public void FinalizeTeleport()
    {
        _loadingInitialZone = false;
        if (_playerCapsule == null)
        {
            _teleportSettling = false;
            return;
        }

        // Perform one final aggressive snap to floor
        var spaceState = GetWorld3D().DirectSpaceState;
        // Search from 150 units above to 150 units below to catch all elevation discrepancies
        var from = _playerCapsule.GlobalPosition + new Vector3(0, 150, 0);
        var to = _playerCapsule.GlobalPosition + new Vector3(0, -150, 0); 
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollisionMask = 1;
        
        var result = spaceState.IntersectRay(query);
        bool positioned = false;
        if (result.Count > 0)
        {
            Vector3 hitPos = (Vector3)result["position"];
            Vector3 hitNormal = result.ContainsKey("normal") ? (Vector3)result["normal"] : Vector3.Up;
            float floorY = hitPos.Y + 0.1f;
            // Stale zone_point / DB Z often places feet *below* walkable collision. A downward ray then finds
            // the real floor *above* authority; the old abs()-based rule always forced bad DB Z and caused fall-through.
            // Prefer raycast when the hit is an upward-facing walkable surface; only fall back to authority for
            // steep disagreements with ceilings / wrong layers (see hitNormal).
            const float MaxAuthVsRaycast = 4f;
            const float MaxSnapDownFromAuth = 85f;
            if (_authSpawnEqValid && Mathf.Abs(floorY - _authSpawnEqZ) > MaxAuthVsRaycast)
            {
                bool upwardFacing = hitNormal.Y > 0.35f;
                bool raycastWellAboveAuth = floorY > _authSpawnEqZ + MaxAuthVsRaycast;
                bool raycastFarBelowAuth = floorY < _authSpawnEqZ - MaxSnapDownFromAuth;

                if (upwardFacing && raycastWellAboveAuth && !raycastFarBelowAuth)
                {
                    GD.Print($"[WORLD] FinalizeTeleport: raycast Y {floorY:F2} vs authoritative EQ.Z {_authSpawnEqZ:F2} (Δ={floorY - _authSpawnEqZ:F2}) — using raycast (walkable mesh; likely stale Z).");
                }
                else if (!upwardFacing || raycastFarBelowAuth)
                {
                    GD.Print($"[WORLD] FinalizeTeleport: raycast Y {floorY:F2} vs authoritative EQ.Z {_authSpawnEqZ:F2}, normalY={hitNormal.Y:F2} — using server height.");
                    floorY = _authSpawnEqZ + 0.1f;
                }
            }

            _playerCapsule.GlobalPosition = new Vector3(_playerCapsule.GlobalPosition.X, floorY, _playerCapsule.GlobalPosition.Z);
            _lastSentPos = _playerCapsule.GlobalPosition;
            GD.Print($"[WORLD] Finalized teleport snap (feet Y={floorY:F2})");
            positioned = true;
        }
        else
        {
            GD.PrintErr("[WORLD] FinalizeTeleport: No floor detected! Using authoritative height if available.");
            if (_authSpawnEqValid)
            {
                _playerCapsule.GlobalPosition = new Vector3(
                    _playerCapsule.GlobalPosition.X,
                    _authSpawnEqZ + 0.1f,
                    _playerCapsule.GlobalPosition.Z);
                _lastSentPos = _playerCapsule.GlobalPosition;
                positioned = true;
            }
        }

        if (positioned)
        {
            _playerCapsule.Velocity = Vector3.Zero;
            float currentHeading = (_playerCapsule.Rotation.Y / (Mathf.Pi * 2.0f)) * 512.0f;
            if (currentHeading < 0) currentHeading += 512.0f;
            EmitSignal(SignalName.PlayerMoved, -_playerCapsule.GlobalPosition.X, -_playerCapsule.GlobalPosition.Z, _playerCapsule.GlobalPosition.Y, currentHeading);
        }

        _teleportSettling = false;
        _teleportSafetyTimer = 0.0;
        _playerCapsule.Velocity = Vector3.Zero;

        // Player capsule was created after the initial SyncLiveMobs flush; emit
        // any accumulated counters (player + late equip swaps) as a follow-up
        // summary so the initial-load LIGHT-FIX output is fully consolidated.
        EntityCapsule.FlushLightFixSummary();
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
                        // Fallback: use center + size from DB zone_points (EQ coords: Z = height → Godot Y).
                        // Previously Y was forced to wallHeight/2, so triggers sat at the wrong elevation vs mesh/DB.
                        float zpX = zl.TryGetProperty("x", out var zpxp) ? zpxp.GetSingle() : 0;
                        float zpY = zl.TryGetProperty("y", out var zpyp) ? zpyp.GetSingle() : 0;
                        float zpZ = zl.TryGetProperty("z", out var zpzp) ? zpzp.GetSingle() : 0f;
                        string orient = zl.TryGetProperty("orientation", out var op) ? op.GetString() : "ns";
                        float trigWidth = zl.TryGetProperty("width", out var twp) ? twp.GetSingle() : 50f;
                        float trigLength = zl.TryGetProperty("length", out var tlp) ? tlp.GetSingle() : 100f;
                        float trigH = zl.TryGetProperty("triggerHeight", out var thp) ? thp.GetSingle() : wallHeight;
                        
                        float gx = -zpX;
                        float gz = -zpY;
                        
                        if (orient == "ew")
                            triggerSize = new Vector3(trigLength, trigH, trigWidth);
                        else
                            triggerSize = new Vector3(trigWidth, trigH, trigLength);
                        
                        triggerPos = new Vector3(gx, zpZ, gz);
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
        // Reset light-fix state before every load so failed GLB loads / fallback maps
        // cannot leak the previous zone's setting into this one.
        LightFixActiveForCurrentZone = false;
        _zoneNormalsCorrectedThisLoad = false;
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

        // If we had GLB from the *previous* zone, RebuildZoneBoundaries skipped creating a
        // PhysicalFloor for the *new* zoneId — then we clear GLB above and may end with no
        // terrain (e.g. cshome has no .s3d / no Brewall JSON). Add a walkable slab so
        // FinalizeTeleport and NPCs are not left in the void.
        bool hasTerrainMesh = _zoneGeometryContainer != null && _zoneGeometryContainer.GetChildCount() > 0;
        var physFloor = _boundariesContainer?.GetNodeOrNull<StaticBody3D>("PhysicalFloor");
        if (!hasTerrainMesh && physFloor == null)
        {
            float fallbackSize = (zoneId.ToLower() == "cshome") ? 4000f : 2000f;
            GD.PrintErr($"[WORLD] No GLB/Brewall terrain for '{zoneId}' — adding emergency floor ({fallbackSize}x{fallbackSize}) (link EQ + extract, or add Data/Maps/{zoneId}_map.json).");
            RebuildFloorForMap(0f, 0f, fallbackSize, fallbackSize);
        }

        PlayZoneMusic(zoneId);
    }
    private bool LoadZoneGlbFromDisk(string zoneId, string absolutePath)
    {
        _zoneNormalsCorrectedThisLoad = false;
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

            // === LIGHTING FIX (zone normal correction) ===
            // LanternExtractor bakes a node transform with scale=(-0.1,-0.1,-0.1) (negative
            // uniform scale = a reflection). Combined with the parent transform we apply
            // above, the overall determinant is negative — so Godot's normal transform
            // (M^-T) flips every vertex normal in world space. That is the real reason
            // omni / point lights have always looked wrong on zone geometry: they compute
            // diffuse against an inverted surface normal. The legacy workaround was
            // BacklightEnabled in FixZoneMaterials, which only papered over directional
            // sun lighting and actively fights local point lights.
            //
            // Fix: walk the freshly-loaded zone scene and bake corrected normals (and
            // matching reversed winding) directly into the mesh data. After this, each
            // surface's stored normals point outward in world space, so omnis behave
            // correctly and the BacklightEnabled hack can be turned off.
            //
            // LanternExtractor's baked negative-scale transform is not zone-specific:
            // apply the normal correction to every extracted GLB zone.
            bool useLightFix = true;
            LightFixActiveForCurrentZone = useLightFix;
            if (useLightFix)
            {
                int meshCountFixed = 0;
                int surfaceCountFixed = 0;
                BakeFlippedNormalsRecursive(scene, ref meshCountFixed, ref surfaceCountFixed, recomputeNormals: true);
                _zoneNormalsCorrectedThisLoad = true;
                GD.Print($"[WORLD][LIGHT-FIX] '{zoneId}': flipped outward normals on {surfaceCountFixed} zone surface(s) across {meshCountFixed} mesh(es). Backlight hack disabled for this zone.");
            }

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
        // World runtime should spawn local object lights (torches/lanterns/fires).
        // Main menu preview can still override this independently.
        _objectPlacer.DisableLights = false;
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
    private void GenerateCollisionRecursive(Node node, ref int meshCount, ref int collisionCount, AnimatableBody3D targetBody = null, Dictionary<string, (string[] frames, float delay)> animData = null, bool isInteractiveDoor = false)
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
                    
                    bool isNoCollide = false;
                    if (mat != null && !string.IsNullOrEmpty(mat.ResourceName)) {
                        string n = mat.ResourceName.ToLower();
                        if (n.Contains("invisible") || n == "inv" || n.StartsWith("inv_") || n.Contains("collide") || n.Contains("boundary") || n.Contains("vine") || n.Contains("leaf") || n.Contains("plant") || n.Contains("fern") || n.Contains("bush") || n.Contains("web")) {
                            isNoCollide = true;
                        }
                        if (!isInteractiveDoor && n.Contains("door")) {
                            isNoCollide = true;
                        }
                    }
                    if (!isInteractiveDoor && (node.Name.ToString().ToLower().Contains("door") || node.Name.ToString().ToLower().Contains("invisible"))) {
                        isNoCollide = true;
                    }

                    if (!isLiquid && !isNoCollide) hasSolid = true;
                    
                    var arrays = arrayMesh.SurfaceGetArrays(i);
                    var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    
                    if (!isLiquid && !isNoCollide) {
                        bool nearCleric = false;
                        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                        foreach(var v in vertices) {
                            if (v.X < min.X) min.X = v.X;
                            if (v.Y < min.Y) min.Y = v.Y;
                            if (v.Z < min.Z) min.Z = v.Z;
                            if (v.X > max.X) max.X = v.X;
                            if (v.Y > max.Y) max.Y = v.Y;
                            if (v.Z > max.Z) max.Z = v.Z;
                        }
                        
                        Aabb bounds = new Aabb(min, max - min);
                        // Player is at (513, -4, -14.4). Expand box slightly
                        Aabb targetBox = new Aabb(new Vector3(512f, -8f, -16f), new Vector3(2f, 8f, 4f));
                        if (bounds.Intersects(targetBox)) {
                            nearCleric = true;
                        }
                        
                        if (nearCleric) {
                            string mName = mat != null ? mat.ResourceName : "null";
                            Godot.GD.Print($"[DEBUG] Solid mesh AABB intersects player at cleric door! Node: {node.Name}, Material: {mName}");
                        }
                    }

                    var indicesObj = arrays[(int)Mesh.ArrayType.Index];
                    
                    if (indicesObj.VariantType != Variant.Type.Nil)
                    {
                        var indices = indicesObj.AsInt32Array();
                        for (int j = 0; j < indices.Length; j++)
                        {
                            if (isNoCollide) continue;
                            if (isLiquid) liquidFaces.Add(vertices[indices[j]]);
                            else faces.Add(vertices[indices[j]]);
                        }
                    }
                    else
                    {
                        for (int j = 0; j < vertices.Length; j++)
                        {
                            if (isNoCollide) continue;
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
            GenerateCollisionRecursive(child, ref meshCount, ref collisionCount, targetBody, animData, isInteractiveDoor);
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
                    
                    // Legacy "fake fill on back-faces" hack to compensate for the inverted zone
                    // normals coming out of LanternExtractor. When we have already baked outward
                    // normals into the mesh data (see BakeFlippedNormalsRecursive), this hack is
                    // wrong — it adds a constant secondary light contribution that fights every
                    // local omni / spot light. Skip it when normals are corrected.
                    if (_zoneNormalsCorrectedThisLoad)
                    {
                        mat.BacklightEnabled = false;
                    }
                    else
                    {
                        mat.BacklightEnabled = true;
                        mat.Backlight = new Color(0.32f, 0.32f, 0.32f);
                    }

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
                    
                    if (_zoneNormalsCorrectedThisLoad)
                    {
                        newMat.BacklightEnabled = false;
                    }
                    else
                    {
                        newMat.BacklightEnabled = true;
                        newMat.Backlight = new Color(0.32f, 0.32f, 0.32f);
                    }

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

    /// <summary>
    /// Walk a freshly-loaded zone scene and overwrite each MeshInstance3D's mesh with a
    /// version whose vertex normals have been negated.
    ///
    /// Why: LanternExtractor zone GLBs ship with a baked node transform of
    /// scale=(-0.1,-0.1,-0.1) — a reflection (negative determinant). Godot's renderer
    /// auto-flips cull order for negative-determinant transforms (so triangles still
    /// render the correct side; that's why the world has always been *visible*), but
    /// it does NOT auto-correct normals. It still transforms vertex normals by M^-T,
    /// which inverts them in world space. That is exactly why:
    ///   - The pre-existing FixZoneMaterials code had to set BacklightEnabled and
    ///     comment "flipped zone normals" to avoid the world looking pitch black.
    ///   - Local omni / spot lights light the wrong side of the player and reflect
    ///     fill onto nearby objects (the surface normals they're computing diffuse
    ///     against are pointing the wrong way).
    ///
    /// Fix (minimal, surgical): negate every stored normal. The renderer's M^-T then
    /// produces a world-space normal that matches the surface's geometric outward
    /// direction. Do NOT touch winding — Godot already handles cull flipping for the
    /// negative-scale transform; reversing winding here would double-flip and turn
    /// every front-face into a culled back-face (i.e. invisible floors / inside-out world).
    /// Tangent .W (handedness) gets flipped too so that any material using a normal
    /// map keeps a consistent TBN basis.
    /// </summary>
    public const string LightFixFlipMetaKey = "_lightfix_normals_flipped";

    public static void BakeFlippedNormalsRecursive(Node node, ref int meshCountFixed, ref int surfaceCountFixed, bool recomputeNormals = false)
    {
        if (node is MeshInstance3D meshInst && meshInst.Mesh != null)
        {
            // Per-node idempotency: if we've already flipped this MeshInstance3D, skip.
            // Re-applying would double-flip and put the lighting back to broken.
            if (!meshInst.HasMeta(LightFixFlipMetaKey))
            {
                var src = meshInst.Mesh;
                int surfaceCount = src.GetSurfaceCount();
                if (surfaceCount > 0)
                {
                    var dst = new ArrayMesh();
                    for (int s = 0; s < surfaceCount; s++)
                    {
                        var arrays = src.SurfaceGetArrays(s);

                        // Rebuild normals from triangle geometry first. Some Lantern-exported
                        // surfaces carry broken/misaligned normal streams (especially terrain),
                        // which causes omni lights to look hemispheric on the ground only.
                        // Recomputing from vertices+winding gives consistent per-vertex normals.
                        if (recomputeNormals && 0 < arrays.Count && arrays[0].VariantType == Variant.Type.PackedVector3Array)
                        {
                            var vertices = (Vector3[])arrays[0];
                            int[] indices = null;
                            if (12 < arrays.Count && arrays[12].VariantType == Variant.Type.PackedInt32Array)
                                indices = (int[])arrays[12];
                            arrays[1] = RecalculateNormals(vertices, indices);
                        }

                        // Negate normals after recompute (or import) to compensate the negative-
                        // determinant transform chain from Lantern export + zone basis mapping.
                        if (1 < arrays.Count && arrays[1].VariantType == Variant.Type.PackedVector3Array)
                        {
                            var normals = (Vector3[])arrays[1];
                            for (int i = 0; i < normals.Length; i++)
                                normals[i] = -normals[i];
                            arrays[1] = normals;
                        }

                        // Flip tangent handedness only — XYZ stay the same, only .W sign
                        // changes — so normal-mapped TBN remains consistent with the
                        // negated normals. (Godot ARRAY_TANGENT = 2, PackedFloat32Array,
                        // 4 floats per vertex: x,y,z,w.)
                        if (2 < arrays.Count && arrays[2].VariantType == Variant.Type.PackedFloat32Array)
                        {
                            var tangents = (float[])arrays[2];
                            for (int i = 3; i < tangents.Length; i += 4)
                                tangents[i] = -tangents[i];
                            arrays[2] = tangents;
                        }

                        // IMPORTANT: do NOT reverse triangle winding. Godot auto-flips cull
                        // order for negative-determinant node transforms, so the original
                        // winding still produces correct front/back face culling. Reversing
                        // winding here would invert visibility — floors disappear, you see
                        // through the world, etc.

                        var mat = src.SurfaceGetMaterial(s);
                        dst.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                        if (mat != null) dst.SurfaceSetMaterial(s, mat);
                        surfaceCountFixed++;
                    }

                    meshInst.Mesh = dst;
                    meshInst.SetMeta(LightFixFlipMetaKey, true);
                    meshCountFixed++;
                }
            }
        }
        foreach (var child in node.GetChildren())
        {
            BakeFlippedNormalsRecursive(child, ref meshCountFixed, ref surfaceCountFixed, recomputeNormals);
        }
    }

    private static Vector3[] RecalculateNormals(Vector3[] vertices, int[] indices)
    {
        if (vertices == null || vertices.Length == 0)
            return vertices ?? Array.Empty<Vector3>();

        var normals = new Vector3[vertices.Length];

        if (indices != null && indices.Length >= 3)
        {
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];
                if ((uint)i0 >= (uint)vertices.Length || (uint)i1 >= (uint)vertices.Length || (uint)i2 >= (uint)vertices.Length)
                    continue;

                Vector3 e1 = vertices[i1] - vertices[i0];
                Vector3 e2 = vertices[i2] - vertices[i0];
                Vector3 face = e1.Cross(e2);
                normals[i0] += face;
                normals[i1] += face;
                normals[i2] += face;
            }
        }
        else
        {
            for (int i = 0; i + 2 < vertices.Length; i += 3)
            {
                Vector3 e1 = vertices[i + 1] - vertices[i];
                Vector3 e2 = vertices[i + 2] - vertices[i];
                Vector3 face = e1.Cross(e2);
                normals[i] += face;
                normals[i + 1] += face;
                normals[i + 2] += face;
            }
        }

        for (int i = 0; i < normals.Length; i++)
        {
            if (normals[i].LengthSquared() > 0.000001f)
                normals[i] = normals[i].Normalized();
            else
                normals[i] = Vector3.Up;
        }

        return normals;
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
