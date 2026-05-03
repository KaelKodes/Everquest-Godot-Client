using Godot;
using System;
using System.Collections.Generic;

public partial class WorldManager : Node3D
{
    public void ProcessMobMove(System.Text.Json.JsonElement ent)
    {
        try
        {
            string id = ent.GetProperty("id").GetString();
            float rawX = ent.TryGetProperty("x", out var xProp) ? (float)xProp.GetDouble() : 0f;
            float rawY = ent.TryGetProperty("y", out var yProp) ? (float)yProp.GetDouble() : 0f;
            float rawZ = ent.TryGetProperty("z", out var zProp) ? (float)zProp.GetDouble() : 0f;
            float rawHeading = ent.TryGetProperty("heading", out var hProp) ? (float)hProp.GetDouble() : 0f;

            float x = -rawX;
            float z = -rawY;
            float y = rawZ;
            float godotYaw = (rawHeading / 512f) * 360f;

            if (_activeEntities.TryGetValue(id, out Node3D entNode) && entNode is EntityCapsule ec)
            {
                if (ec.ChaseTarget == null)
                {
                    ec.TargetYaw = godotYaw;
                    ec.TargetPosition = new Vector3(x, y, z);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[WORLD] ProcessMobMove parsing error: {ex.Message}");
        }
    }
    public void SpawnEntityAt(string id, string name, string type, Vector3 pos, string appearanceJson = "", int race = 1, int gender = 0, int face = 0, string equipVisualsJson = "", float headingYaw = 0f, float size = 6f)
    {
        if (_activeEntities.ContainsKey(id)) return;

        var instance = new EntityCapsule();
        instance.Position = pos;
        instance.Name = id;

        _spawnsContainer.AddChild(instance);
        instance.RotationDegrees = new Vector3(0, headingYaw, 0);
        _activeEntities[id] = instance;

        try {
            instance.Setup(name, type, appearanceJson, race, gender, face, equipVisualsJson, size);
            // Apply current vision effects
            if (_currentVisionStyle == "infravision")
                instance.SetInfravision(true);
        } catch (Exception ex) {
            GD.PrintErr($"[WORLD] Failed Setup on '{name}': {ex.Message}");
        }
    }
    public void SyncLiveMobs(System.Text.Json.JsonElement entitiesArray, bool isDelta = false, System.Text.Json.JsonElement removedArray = default)
    {
        int count = entitiesArray.GetArrayLength();
        // Sync entities silently

        var existingIds = new HashSet<string>();
        foreach (var kvp in _activeEntities)
        {
            if (kvp.Value is EntityCapsule ec && ec != _playerCapsule)
                existingIds.Add(kvp.Key);
        }

        var incomingIds = new HashSet<string>();

        int total = entitiesArray.GetArrayLength();
        int i = 0;
        foreach (var ent in entitiesArray.EnumerateArray())
        {
            EmitSignal(SignalName.SyncProgress, ++i, total);
            string id = ent.GetProperty("id").GetString();
            string name = ent.TryGetProperty("name", out var nProp) ? nProp.GetString() : "Unknown";
            string type = ent.TryGetProperty("type", out var tProp) ? tProp.GetString() : "enemy";
            float rawX = ent.TryGetProperty("x", out var xProp) ? (float)xProp.GetDouble() : 0f;
            float rawY = ent.TryGetProperty("y", out var yProp) ? (float)yProp.GetDouble() : 0f;
            float rawZ = ent.TryGetProperty("z", out var zProp) ? (float)zProp.GetDouble() : 0f;
            
            // Map EQ Coords (+X=West, +Y=North) to Godot Coords (-X=West, -Z=North)
            // EQ Z (height) maps to Godot Y (height)
            float x = -rawX;
            float z = -rawY;
            float y = rawZ;  // EQ Z = Godot Y (height)
            string appearance = ent.TryGetProperty("appearance", out var aProp) ? aProp.ToString() : "";
            int race = ent.TryGetProperty("race", out var rProp) ? rProp.GetInt32() : 1;
            int gender = ent.TryGetProperty("gender", out var gProp) ? gProp.GetInt32() : 0;
            int face = ent.TryGetProperty("face", out var fProp) ? fProp.GetInt32() : 0;
            bool sneaking = ent.TryGetProperty("sneaking", out var sProp) && sProp.GetBoolean();
            bool hidden = ent.TryGetProperty("hidden", out var hidProp) && hidProp.GetBoolean();
            string equipVis = ent.TryGetProperty("equipVisuals", out var evProp) && evProp.ValueKind != System.Text.Json.JsonValueKind.Null ? evProp.GetRawText() : "";
            float rawHeading = ent.TryGetProperty("heading", out var hProp) ? (float)hProp.GetDouble() : 0f;

            // EQEmu headings are 0-512 where 0=North, 128=West, 256=South, 384=East
            // Live entities are perfectly mapped to Godot's space (-X=West, -Z=North), 
            // which exactly matches Godot's Yaw behavior (0=-Z, 90=-X).
            // No reflection or offsets are needed!
            float size = ent.TryGetProperty("size", out var sizeProp) ? (float)sizeProp.GetDouble() : 6f;
            float godotYaw = (rawHeading / 512f) * 360f;

            incomingIds.Add(id);

            if (!existingIds.Contains(id))
            {
                // GD.Print($"[WORLD] Spawning '{name}' (race={race} gender={gender} face={face}) at server coords: {rawX}, {rawY}, {rawZ}");
                // Use the Godot-mapped coordinates (x, y, z) calculated above
                SpawnEntityAt(id, name, type, new Vector3(x, y, z), appearance, race, gender, face, equipVis, godotYaw, size);
            }
            else
            {
                // Update existing entity
                if (_activeEntities.TryGetValue(id, out Node3D entNode) && entNode is EntityCapsule ec)
                {
                    // Only snap rotation if they aren't actively running/chasing
                    if (ec.ChaseTarget == null)
                    {
                        Vector3 newPos = new Vector3(x, y, z);
                        float dist = ec.GlobalPosition.DistanceTo(newPos);
                        
                        if (dist > 15.0f)
                        {
                            // Teleport/snap if we are way out of sync
                            ec.GlobalPosition = newPos;
                            ec.TargetPosition = null;
                            ec.TargetYaw = godotYaw;
                        }
                        else if (dist > 0.5f)
                        {
                            // Interpolate if we are somewhat out of sync
                            ec.TargetYaw = godotYaw;
                            ec.TargetPosition = newPos;
                        }
                        else
                        {
                            // We are close enough, don't trigger movement
                            ec.TargetYaw = godotYaw;
                        }
                    }
                }
            }
            
            UpdateEntitySneak(id, sneaking);
            UpdateEntityHide(id, hidden);
        }

        // Remove entities that no longer exist on the server
        if (isDelta)
        {
            if (removedArray.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var removedId in removedArray.EnumerateArray())
                {
                    RemoveEntity(removedId.GetString());
                }
            }
        }
        else
        {
            foreach (var oldId in existingIds)
            {
                if (!incomingIds.Contains(oldId))
                {
                    RemoveEntity(oldId);
                }
            }
        }
    }
    public void UpdateEntitySneak(string id, bool sneaking)
    {
        if (_activeEntities.TryGetValue(id, out Node3D entity) && entity is EntityCapsule ec)
        {
            ec.SetSneakState(sneaking, false);
        }
    }
    public void UpdateEntityHide(string id, bool hidden)
    {
        if (_activeEntities.TryGetValue(id, out Node3D entity) && entity is EntityCapsule ec)
        {
            ec.SetHideState(hidden, false);
        }
    }
    public void SetPlayerLightSource(bool hasLight)
    {
        if (_playerCapsule != null)
            _playerCapsule.SetLightSource(hasLight);
    }
    public void SetPlayerNameVisible(bool visible)
    {
        if (_playerCapsule != null)
            _playerCapsule.SetNameVisible(visible);
    }
    public void UpdatePlayerEquipVisuals(string equipVisualsJson)
    {
        if (_playerCapsule != null)
            _playerCapsule.UpdateEquipVisuals(equipVisualsJson);
    }
    public void UpdateEntityEquipVisuals(string id, string equipVisualsJson)
    {
        if (_activeEntities.TryGetValue(id, out Node3D entity) && entity is EntityCapsule ec)
        {
            ec.UpdateEquipVisuals(equipVisualsJson);
        }
    }
    public void SpawnEntity(string id, string name, string type, string appearanceJson = "")
    {
        float radius = 8f + _spawnCounter * 4f;
        float angle = _spawnCounter * (Mathf.Pi * 2f / 6f);
        Vector3 pos = new Vector3(Mathf.Cos(angle) * radius, 2f, Mathf.Sin(angle) * radius);
        _spawnCounter++;
        SpawnEntityAt(id, name, type, pos, appearanceJson);
    }
    public void RemoveEntity(string id)
    {
        if (_activeEntities.TryGetValue(id, out Node3D entity))
        {
            if (GodotObject.IsInstanceValid(_currentTarget as Node) && entity == (_currentTarget as Node))
                _currentTarget = null;

            if (GodotObject.IsInstanceValid(entity))
            {
                entity.SetProcess(false);
                entity.SetPhysicsProcess(false);
                // Also recursively disable collision shapes if any, but queue_free is usually fine
                entity.QueueFree();
            }
            _activeEntities.Remove(id);
        }
    }
    public void SetLocalTargetViaId(string targetId)
    {
        if (string.IsNullOrEmpty(targetId)) return;
        if (_activeEntities.TryGetValue(targetId, out Node3D tgt) || _activeEntities.TryGetValue($"mob_{targetId}", out tgt))
        {
            if (tgt is EntityCapsule activeEc)
            {
                SetTarget(activeEc);
            }
        }
    }
    public void SetCombatTarget(string targetId)
    {
        // Server is authoritative for movement; no client-side ChaseTarget overrides.

        _playerInCombat = !string.IsNullOrEmpty(targetId);
        if (string.IsNullOrEmpty(targetId)) return;

        EntityCapsule activeEc = null;

        // For local tests where 'mob_' was stripped off by server, try both
        if (_activeEntities.TryGetValue(targetId, out Node3D tgt) || _activeEntities.TryGetValue($"mob_{targetId}", out tgt))
        {
            activeEc = tgt as EntityCapsule;
        }
        else if (_currentTarget != null && _currentTarget is EntityCapsule)
        {
            // Fallback: Use what the user clicked locally, since that's what triggered combat
            activeEc = _currentTarget as EntityCapsule;
        }
        else
        {
            // Fallback: Pick the closest local entity (simulating the server's handleStartCombat logic)
            float closestDist = float.MaxValue;
            foreach (var kvp in _activeEntities)
            {
                if (kvp.Value is EntityCapsule candidate && candidate != _playerCapsule)
                {
                    float dist = candidate.GlobalPosition.DistanceTo(_playerCapsule.GlobalPosition);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        activeEc = candidate;
                    }
                }
            }
        }

        if (activeEc != null)
        {
            if (_currentTarget != activeEc)
            {
                SetTarget(activeEc);
            }
        }
    }

    public void SpawnSpellAnimation(System.Text.Json.JsonElement animDict)
    {
        try
        {
            string casterId = animDict.TryGetProperty("casterId", out var cProp) ? cProp.GetString() : "";
            string targetId = animDict.TryGetProperty("targetId", out var tProp) ? tProp.GetString() : "";
            int spellAnimId = animDict.TryGetProperty("spellAnimId", out var aProp) ? aProp.GetInt32() : -1;
            bool isAura = animDict.TryGetProperty("isAura", out var rProp) && rProp.GetBoolean();

            if (spellAnimId == -1) return;

            Node3D targetEntity = null;
            if (_activeEntities.TryGetValue(targetId, out Node3D tgt)) {
                targetEntity = tgt;
            } else if (_activeEntities.TryGetValue($"mob_{targetId}", out tgt)) {
                targetEntity = tgt;
            } else if (_playerCapsule != null && _playerCapsule.Name == targetId) {
                targetEntity = _playerCapsule;
            }

            if (targetEntity == null) return;

            string scenePath = isAura ? "res://Scenes/Archetype_Aura.tscn" : "res://Scenes/Archetype_Burst.tscn";
            var packedScene = GD.Load<PackedScene>(scenePath);
            if (packedScene == null)
            {
                GD.PrintErr($"[WORLD] Could not load packed scene: {scenePath}");
                return;
            }

            var particles = packedScene.Instantiate<EQParticleSystem>();
            if (particles == null) return;
            
            targetEntity.AddChild(particles);
            
            // Set position locally so it appears roughly around chest height
            particles.Position = Vector3.Up * 1.0f;
            
            particles.PlayAnim(spellAnimId);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[WORLD] SpawnSpellAnimation error: {ex.Message}");
        }
    }
}
