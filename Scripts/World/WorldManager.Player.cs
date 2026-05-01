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
        float y = rawZ + 3.1f; // Capsule origin is center (3.1f), so feet touch rawZ exactly

        GD.Print($"[WORLD] Spawning Player at Godot({x:F1}, {y:F1}, {z:F1}) (race={_playerRace} gender={_playerGender} face={_playerFace})");

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
    }
    public void TeleportPlayer(float rawX, float rawY, float rawZ = 0f)
    {
        // Map EQ Coords to Godot Coords — same conversion used for mobs/NPCs
        float x = -rawX;
        float z = -rawY;
        float y = rawZ + 3.1f; // Capsule origin is center (3.1f), so feet touch rawZ exactly

        GD.Print($"[WORLD] TeleportPlayer: EQ({rawX}, {rawY}, {rawZ}) → Godot({x:F1}, {y:F1}, {z:F1})");

        if (_playerCapsule == null)
        {
            SpawnPlayer(rawX, rawY, rawZ);
        }

        _playerCapsule.GlobalPosition = new Vector3(x, y, z);
        _playerCapsule.Velocity = Vector3.Zero;
        _lastSentPos = new Vector3(x, y, z);
        EmitSignal(SignalName.PlayerMoved, x, y, z);

        // Grant zone immunity so we don't instantly trigger a nearby zoneline
        _zoneImmunityTimer = 5.0;

        // Keep player physics frozen to prevent gravity-induced clipping through 
        // geometry that may still be registering in the physics engine.
        _teleportSettling = true;
        _teleportFreezeTimer = 1.0f; // Freeze for 1 real second

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
    public void SetPlayerCasting(bool casting)
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
