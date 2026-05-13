using Godot;
using System.Text.Json;

public partial class WorldManager : Node3D
{
    /// <summary>
    /// Spawns PEQ <c>object</c> rows (crafting stations, etc.) using the same EQ→Godot transform as doors.
    /// </summary>
    public void ProcessWorldObjects(JsonElement worldObjectsArray)
    {
        if (worldObjectsArray.ValueKind != JsonValueKind.Array)
            return;

        int total = worldObjectsArray.GetArrayLength();
        if (total == 0)
            return;

        GD.Print($"[WORLD] Processing {total} PEQ world object(s)...");

        if (_objectPlacer == null)
        {
            _objectPlacer = new ZoneObjectPlacer();
            _objectPlacer.ShadowsEnabled = DynamicShadowsEnabled;
            _objectPlacer.Animator = _materialAnimator;
        }

        string objectsDir = $"{EQAssetCache.Instance.CacheRoot}/zones/{_currentZoneId.ToLower()}/Objects";

        int placed = 0;
        foreach (var element in worldObjectsArray.EnumerateArray())
        {
            if (!element.TryGetProperty("id", out var idEl))
                continue;

            string objId = idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : idEl.GetInt32().ToString();
            if (string.IsNullOrEmpty(objId) || _spawnedWorldObjects.ContainsKey(objId))
                continue;

            string modelName = element.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrEmpty(modelName))
                continue;

            float rawX = element.TryGetProperty("pos_x", out var xp) ? xp.GetSingle() : 0f;
            float rawY = element.TryGetProperty("pos_y", out var yp) ? yp.GetSingle() : 0f;
            float rawZ = element.TryGetProperty("pos_z", out var zp) ? zp.GetSingle() : 0f;
            float heading = element.TryGetProperty("heading", out var hp) ? hp.GetSingle() : 0f;

            var scene = _objectPlacer.GetObjectScene(modelName, objectsDir);
            if (scene == null)
            {
                string resolved = ZoneObjectPlacer.NormalizeObjectMeshModelName(modelName);
                string warnKey = string.IsNullOrEmpty(resolved) ? modelName : resolved.ToLowerInvariant();
                if (_worldObjectSceneLoadWarnedOnce.Add(warnKey))
                    GD.PrintErr($"[WORLD] Failed to load world object '{modelName}' (resolved '{resolved}'). No usable GLB in zone Objects/ or eqsage/objects — for RoF2 use lowercase it####.glb names and set tradeskill objects dir in EQ config.");
                continue;
            }

            string displayName = element.TryGetProperty("displayName", out var dispEl)
                && dispEl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(dispEl.GetString())
                ? dispEl.GetString()!.Trim()
                : ZoneObjectPlacer.GetTradeskillStationDisplayName(modelName);

            var entity = new WorldObjectEntity { Name = $"worldobj_{objId}" };
            _worldObjectsContainer.AddChild(entity);

            float gx = -rawX;
            float gy = rawZ;
            float gz = -rawY;
            entity.Position = new Vector3(gx, gy, gz);

            float godotYaw = (heading / 512f) * 360f;
            entity.RotationDegrees = new Vector3(0, godotYaw - 90f, 0);

            float size = element.TryGetProperty("size", out var sp) ? sp.GetSingle() : 100f;
            float scaleMult = size > 0 ? size / 100f : 1f;
            entity.Scale = new Vector3(scaleMult, scaleMult, scaleMult);

            var meshNode = scene.Instantiate<Node3D>();
            entity.AddChild(meshNode);

            _objectPlacer.RegisterInstanceMaterialAnimations(meshNode, modelName, objectsDir);
            _objectPlacer.AddLightIfSource(meshNode, modelName, _worldObjectsContainer, gx, gy, gz);

            int meshCount = 0;
            int collisionCount = 0;
            GenerateCollisionRecursive(meshNode, ref meshCount, ref collisionCount, null, null, false);

            Aabb aabb = CalculateWorldAabb(entity);
            entity.Setup(displayName, objId, aabb);
            ConnectWorldObjectInputsRecursive(meshNode, entity);

            FixZoneMaterials(entity);

            _spawnedWorldObjects[objId] = entity;
            placed++;
        }

        if (placed > 0)
            GD.Print($"[WORLD] Placed {placed} world object mesh(es) from database.");
    }

    private void ConnectWorldObjectInputsRecursive(Node node, WorldObjectEntity worldObj)
    {
        if (node is CollisionObject3D collisionObject)
        {
            collisionObject.InputEvent += (Node camera, InputEvent @event, Vector3 position, Vector3 normal, long shapeIdx) =>
            {
                if (@event is InputEventMouseButton mb && mb.Pressed
                    && (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right))
                    SetTarget(worldObj);
            };
        }
        foreach (Node child in node.GetChildren())
            ConnectWorldObjectInputsRecursive(child, worldObj);
    }
}
