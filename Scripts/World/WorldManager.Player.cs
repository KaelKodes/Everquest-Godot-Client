using Godot;
using System;
using System.Collections.Generic;

public partial class WorldManager : Node3D
{
    private void TargetSelf()
    {
        SetTarget(_playerCapsule);
        GD.Print("[WORLD] Targeted self.");
    }
    public void CancelStealth(bool cancelSneak, bool cancelHide)
    {
        if (cancelSneak && _isSneaking)
        {
            _isSneaking = false;
            // Don't touch _isCrouching — player stays crouched, just loses stealth benefit
        }
        if (cancelHide && _isHiding)
        {
            _isHiding = false;
        }
    }
    private void CycleTarget(string category, int direction)
    {
        List<EntityCapsule> candidates = new List<EntityCapsule>();

        foreach (var kvp in _activeEntities)
        {
            if (kvp.Value is EntityCapsule ec && ec != _playerCapsule)
            {
                if (category == "enemy" && (ec.EntityType == "enemy" || ec.EntityType == "mob" || ec.EntityType == "mining_node"))
                    candidates.Add(ec);
                else if (category == "friendly" && (ec.EntityType == "player" || ec.EntityType == "npc" || ec.EntityType == "pet"))
                    candidates.Add(ec);
            }
        }

        if (candidates.Count == 0) return;

        // Sort by distance to player for consistent ordering
        candidates.Sort((a, b) =>
            a.GlobalPosition.DistanceTo(_playerCapsule.GlobalPosition)
            .CompareTo(b.GlobalPosition.DistanceTo(_playerCapsule.GlobalPosition))
        );

        // Find current index
        ref int idx = ref (category == "enemy" ? ref _enemyTargetIndex : ref _friendlyTargetIndex);

        idx += direction;
        if (idx >= candidates.Count) idx = 0;
        if (idx < 0) idx = candidates.Count - 1;

        SetTarget(candidates[idx]);
    }
    public void SetPlayerAppearance(int race, int gender, int face, string equipVisualsJson = "")
    {
        _playerRace = race;
        _playerGender = gender;
        _playerFace = face;
        _playerEquipVisuals = equipVisualsJson;
    }
    public void SpawnPlayer(float rawX, float rawY, float rawZ = 0f)
    {
        if (_playerCapsule != null) return;

        // Map EQ Coords to Godot Coords — same conversion used for mobs/NPCs
        float x = -rawX;
        float z = -rawY;
        float y = rawZ; // Godot Y at feet matches EQ Z

        // Give a moderate boost to initial height to prevent falling through floor
        // We use +5.0f (approx matching server boost) to ensure we're definitely above the floor.
        // The client-side snapping in _PhysicsProcess will bring us down to the floor precisely.
        y += 5.0f; 

        GD.Print($"[WORLD] Spawning Player at Godot({x:F1}, {y:F1}, {z:F1}) (EQ: {rawX:F1}, {rawY:F1}, {rawZ:F1})");

        _authSpawnEqX = rawX;
        _authSpawnEqY = rawY;
        _authSpawnEqZ = rawZ;
        _authSpawnEqValid = true;

        _playerCapsule = new EntityCapsule();
        _playerCapsule.Name = "Player";
        _playerCapsule.Position = new Vector3(x, y, z);
        _spawnsContainer.AddChild(_playerCapsule);
        _playerCapsule.Setup("You", "player", "", _playerRace, _playerGender, _playerFace, _playerEquipVisuals);
        _playerCapsule.IsPlayerControlled = true;
        _playerCapsule.SetNameVisible(false); // Hidden by default; toggled via Options panel
        _activeEntities["player_self"] = _playerCapsule;

        // Exclude the player from the SpringArm raycast so it doesn't zoom in instantly
        if (_cameraArm != null)
        {
            _cameraArm.AddExcludedObject(_playerCapsule.GetRid());
        }

        // Keep player physics frozen to prevent gravity-induced clipping through 
        // geometry that may still be registering in the physics engine.
        // This state remains until FinalizeTeleport is called by the UI loader.
        _teleportSettling = true;
        _teleportSafetyTimer = 0.0;
    }
    public void TeleportPlayer(float rawX, float rawY, float rawZ = 0f)
    {
        // If we are already in the world and NOT loading the initial zone, 
        // check if this is a "status sync" teleport (echo from server).
        // If it's the same position we just snapped to, ignore it to prevent freezing.
        if (!_loadingInitialZone && _playerCapsule != null)
        {
            float dx = Mathf.Abs(-rawX - _playerCapsule.GlobalPosition.X);
            float dz = Mathf.Abs(-rawY - _playerCapsule.GlobalPosition.Z);
            float dy = Mathf.Abs(rawZ - _playerCapsule.GlobalPosition.Y);
            
            // If horizontal movement is negligible and height is within safety boost range, ignore.
            if (dx < 0.1f && dz < 0.1f && dy < 5.1f)
            {
                GD.Print($"[WORLD] Ignoring redundant TeleportPlayer (likely server echo). EQ: {rawX}, {rawY}, {rawZ}");
                return;
            }
        }

        // If we are currently settling a teleport, ignore external height updates that reset Z to 0.
        // This prevents the "reset to safety height" runaway loop when the server sends a status update 
        // with the original (pre-snap) coordinates while we are still loading or just finished.
        if (_teleportSettling && _playerCapsule != null && rawZ == 0f && Mathf.Abs(_playerCapsule.GlobalPosition.Y) > 5.1f)
        {
            GD.Print($"[WORLD] Ignoring TeleportPlayer height reset to 0 while settling. Current Godot Y: {_playerCapsule.GlobalPosition.Y:F2}");
            // Still update horizontal if needed? Usually we want to stay pinned.
            return;
        }

        // Map EQ Coords to Godot Coords — same conversion used for mobs/NPCs
        float x = -rawX;
        float z = -rawY;
        float y = rawZ; // Godot Y at feet matches EQ Z

        // Give a moderate boost to height to prevent falling through floor
        // We use +5.0f (approx matching server boost) to ensure we're definitely above the floor.
        // The client-side snapping in _PhysicsProcess will bring us down to the floor precisely.
        y += 5.0f;

        GD.Print($"[WORLD] TeleportPlayer: EQ({rawX}, {rawY}, {rawZ}) → Godot({x:F1}, {y:F1}, {z:F1})");

        _authSpawnEqX = rawX;
        _authSpawnEqY = rawY;
        _authSpawnEqZ = rawZ;
        _authSpawnEqValid = true;

        if (_playerCapsule == null)
        {
            SpawnPlayer(rawX, rawY, rawZ);
        }

        _playerCapsule.GlobalPosition = new Vector3(x, y, z);
        _playerCapsule.Velocity = Vector3.Zero;
        _lastSentPos = new Vector3(x, y, z);
        
        // Signal with EQ coordinates: EQ.X = -Godot.X, EQ.Y = -Godot.Z, EQ.Z = Godot.Y (feet)
        // IMPORTANT: We send rawZ (the original EQ Z), NOT the boosted Y, to prevent runaway height loops.
        EmitSignal(SignalName.PlayerMoved, rawX, rawY, rawZ, 0f);

        // Grant zone immunity so we don't instantly trigger a nearby zoneline
        _zoneImmunityTimer = 5.0;

        // Keep player physics frozen to prevent gravity-induced clipping through 
        // geometry that may still be registering in the physics engine.
        // This state remains until FinalizeTeleport is called by the UI loader.
        _teleportSettling = true;
        _teleportSafetyTimer = 0.0;
        
        // Force camera to immediately snap to new position before loading screen hides
        UpdateCamera();
    }
    public void SetPlayerSitting(bool sitting)
    {
        if (_playerCapsule == null) return;
        if (sitting)
            _playerCapsule.PlaySit();
        else
            _playerCapsule.Revive(); // resets _isSitting and returns to idle
    }

    public void SetPlayerLevitating(bool levitating)
    {
        _playerLevitating = levitating;
        if (levitating)
            _fallTracking = false;
    }

    public void SetVisionStyle(string styleName)
    {
        _currentVisionStyle = styleName;
        var vm = GetNodeOrNull<VisionManager>("VisionManager");
        if (vm != null)
        {
            vm.SetVisionStyle(styleName);
        }

        // Apply heat signature to all entities if in infravision
        bool isInfra = styleName == "infravision";
        foreach (var entity in _activeEntities.Values)
        {
            if (entity is EntityCapsule ec)
            {
                ec.SetInfravision(isInfra);
            }
        }
        
        ApplyTimeOfDayVisuals();
    }
    public void SetPlayerCastingAnimation(bool casting)
    {
        if (_playerCapsule == null) return;
        if (casting)
            _playerCapsule.PlayAnimation("t04"); // casting animation
        else
            _playerCapsule.PlayAnimation("p01"); // return to idle
    }
    public void PlayPlayerAnimation(string animName)
    {
        if (_playerCapsule == null || string.IsNullOrEmpty(animName)) return;
        // Use PlayEmote to set the emote timer, preventing idle from overriding
        _playerCapsule.PlayEmote(animName);
    }
}
