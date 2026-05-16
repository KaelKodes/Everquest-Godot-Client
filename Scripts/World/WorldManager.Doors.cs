using Godot;
using System;
using System.Collections.Generic;

public partial class WorldManager : Node3D
{
    public void ProcessDoors(System.Text.Json.JsonElement doorsArray)
    {
        int totalDoors = doorsArray.GetArrayLength();
        GD.Print($"[WORLD] Processing {totalDoors} doors...");

        // PlaceZoneObjects normally creates this when a zone GLB loads; extraction failure / fallback path can skip it.
        if (_objectPlacer == null)
        {
            _objectPlacer = new ZoneObjectPlacer();
            _objectPlacer.ShadowsEnabled = DynamicShadowsEnabled;
            _objectPlacer.Animator = _materialAnimator;
        }
        string objectsDir = $"{EQAssetCache.Instance.CacheRoot}/zones/{_currentZoneId.ToLower()}/Objects";

        foreach (var element in doorsArray.EnumerateArray())
        {
            string doorId = element.GetProperty("id").GetInt32().ToString();
            
            if (_spawnedDoors.ContainsKey(doorId)) continue; // Already spawned
            
            string modelName = element.GetProperty("name").GetString();
            int opentype = element.TryGetProperty("opentype", out var otp) ? otp.GetInt32() : 0;
            
            if (string.IsNullOrEmpty(modelName)) continue;
            
            int localDoorId = element.TryGetProperty("doorid", out var ldid) ? ldid.GetInt32() : 0;
            int triggerDoor = element.TryGetProperty("triggerdoor", out var td) ? td.GetInt32() : 0;
            
            float rawX = element.TryGetProperty("pos_x", out var xp) ? xp.GetSingle() : 0f;
            float rawY = element.TryGetProperty("pos_y", out var yp) ? yp.GetSingle() : 0f;
            float rawZ = element.TryGetProperty("pos_z", out var zp) ? zp.GetSingle() : 0f;
            float heading = element.TryGetProperty("heading", out var hp) ? hp.GetSingle() : 0f;
            int doorParam = element.TryGetProperty("door_param", out var dpp) ? dpp.GetInt32() : 0;
            
            // Get the PackedScene from ZoneObjectPlacer
            var scene = _objectPlacer.GetObjectScene(modelName, objectsDir);
            if (scene == null)
            {
                string resolved = ZoneObjectPlacer.NormalizeObjectMeshModelName(modelName);
                string warnKey = string.IsNullOrEmpty(resolved) ? modelName : resolved.ToLowerInvariant();
                if (_doorSceneLoadWarnedOnce.Add(warnKey))
                    GD.PrintErr($"[WORLD] Failed to load door scene: {modelName} (resolved '{resolved}') — missing GLB or glTF import failed; see ObjectPlacer log.");
                continue;
            }

            var doorMeshNode = scene.Instantiate<Node3D>();
            _objectPlacer.RegisterInstanceMaterialAnimations(doorMeshNode, modelName, objectsDir);

            // Invisible barrier doors usually have specific opentypes (53, 54, etc)
            // Elevators are 58, 59, so >= 54 is too broad and makes lifts invisible!
            if (opentype == 53 || opentype == 54)
            {
                doorMeshNode.Visible = false;
            }
            
            // Create our interactive DoorEntity wrapper
            var doorEntity = new DoorEntity();
            doorEntity.Name = $"Door_{doorId}_{modelName}";
            
            _doorsContainer.AddChild(doorEntity);

            // Map raw EQ DB coords to Godot (same mapping as entities/NPCs)
            float gx = -rawX;
            float gy = rawZ;   // EQ Z (height) → Godot Y
            float gz = -rawY;
            doorEntity.Position = new Vector3(gx, gy, gz);
            
            float godotYaw = (heading / 512f) * 360f;
            doorEntity.RotationDegrees = new Vector3(0, godotYaw - 90f, 0);

            float size = element.TryGetProperty("size", out var sp) ? sp.GetSingle() : 100f;
            float scaleMult = size / 100f;
            doorEntity.Scale = new Vector3(scaleMult, scaleMult, scaleMult);

            // Elevators need AnimatableBody3D so mesh + collision + rider sync. Portcullises tween the DoorEntity
            // root instead — AnimatableBody under a scaled Node3D often ignores Tween motion; double DOOR_STATE
            // bursts also cancel the tween before it moves.
            string upperName = modelName.ToUpper();
            bool isElevator = (upperName.Contains("LEVATOR") || upperName.Contains("ELESTEP") || upperName.Contains("LIFT") || upperName.Contains("PLATFORM"));
            
            // Buttons that call the elevator often share the name (e.g. lift_btn) but should NOT be unclickable!
            if (upperName.Contains("BTN") || upperName.Contains("BUTTON") || upperName.Contains("FELE"))
            {
                isElevator = false;
            }
            
            bool needsMovingPlatform = isElevator;
            AnimatableBody3D platformBody = null;
            
            if (needsMovingPlatform)
            {
                platformBody = new AnimatableBody3D { Name = "PlatformBody", SyncToPhysics = false };
                doorEntity.AddChild(platformBody);
                platformBody.AddChild(doorMeshNode);
            }
            else
            {
                doorEntity.AddChild(doorMeshNode);
            }

            // Generate collision for the door meshes
            int meshCount = 0;
            int collisionCount = 0;
            if (needsMovingPlatform)
            {
                GenerateCollisionRecursive(doorMeshNode, ref meshCount, ref collisionCount, platformBody, null, true);
            }
            else
            {
                GenerateCollisionRecursive(doorMeshNode, ref meshCount, ref collisionCount, null, null, true);
            }
            
            // Calculate AABB for the target ring sizing
            Aabb doorAabb = CalculateWorldAabb(doorEntity);
            bool invertState = element.TryGetProperty("invert_state", out var inv) && inv.GetInt32() == 1;
            doorEntity.Setup(modelName, doorId, localDoorId, triggerDoor, doorAabb, doorParam, invertState, opentype, platformBody);
            
            // Lifts can be targetable, because in Kelethin, the lift IS the button.

            // Connect input events from generated collision shapes to the DoorEntity
            ConnectDoorInputsRecursive(platformBody != null ? platformBody : doorMeshNode, doorEntity);
            
            FixZoneMaterials(doorEntity);
            
            _spawnedDoors[doorId] = doorEntity;
        }
    }
    public void ToggleDoor(string doorId, bool isOpen)
    {
        // Merge rapid open/close (duplicate clicks or server echo) — last state wins after a short quiet window.
        _doorNetworkStatePending[doorId] = isOpen;
        _doorNetworkStateApplyAtMsec[doorId] = Time.GetTicksMsec() + DoorNetworkDebounceMs;
    }

    private void FlushDebouncedDoorStates()
    {
        if (_doorNetworkStateApplyAtMsec.Count == 0) return;
        ulong now = Time.GetTicksMsec();
        List<string> ready = null;
        foreach (var kv in _doorNetworkStateApplyAtMsec)
        {
            if (now >= kv.Value)
                (ready ??= new List<string>()).Add(kv.Key);
        }
        if (ready == null) return;

        foreach (string doorId in ready)
        {
            _doorNetworkStateApplyAtMsec.Remove(doorId);
            if (!_doorNetworkStatePending.TryGetValue(doorId, out bool isOpen))
                continue;
            _doorNetworkStatePending.Remove(doorId);

            if (_spawnedDoors.TryGetValue(doorId, out Node3D doorNode) && doorNode is DoorEntity doorEntity)
                doorEntity.SetOpenState(isOpen);
            else
                GD.Print($"[WORLD] Ignored state change for unspawned/invisible door: {doorId}");
        }
    }
    private void ConnectDoorInputsRecursive(Node node, DoorEntity doorEntity)
    {
        if (node is CollisionObject3D collisionObject)
        {
            // Connect to Godot's input_event signal (works for both StaticBody3D and AnimatableBody3D)
            collisionObject.InputEvent += (Node camera, InputEvent @event, Vector3 position, Vector3 normal, long shapeIdx) => 
            {
                doorEntity.OnInputEvent(camera, @event, position, normal, shapeIdx);
            };
        }
        foreach (Node child in node.GetChildren())
        {
            ConnectDoorInputsRecursive(child, doorEntity);
        }
    }
    private void SetPickableRecursive(Node node, bool pickable)
    {
        if (node is CollisionObject3D collisionObject)
        {
            collisionObject.InputRayPickable = pickable;
        }

        foreach (var child in node.GetChildren())
        {
            SetPickableRecursive(child, pickable);
        }
    }
}
