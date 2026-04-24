using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class WorldManager : Node3D
{
    private Node3D _spawnsContainer;
    private Node3D _cameraArm;
    private Camera3D _camera;
    private EntityCapsule _playerCapsule;
    private EntityCapsule _currentTarget;
    private Dictionary<string, Node3D> _activeEntities = new Dictionary<string, Node3D>();

    private int _spawnCounter = 0;
    private int _enemyTargetIndex = -1;
    private int _friendlyTargetIndex = -1;

    public enum CameraMode { FreeLook, Drive }
    public CameraMode CurrentCameraMode { get; private set; } = CameraMode.Drive;

    // Camera settings
    private float _cameraDistance = 10f;
    private float _cameraPitch = -30f;
    private float _cameraYaw = 0f;

    // Signal for UI target updates
    [Signal] public delegate void TargetChangedEventHandler(string name, string type);
    [Signal] public delegate void TargetClearedEventHandler();
    
    // Server Syncing
    [Signal] public delegate void PlayerMovedEventHandler(float x, float z);
    [Signal] public delegate void ZoneLineCrossedEventHandler(string targetZoneId);
    [Signal] public delegate void SneakToggledEventHandler(bool isSneaking);
    [Signal] public delegate void SyncProgressEventHandler(int current, int total);
    private Vector3 _lastSentPos = Vector3.Zero;
    private Node3D _boundariesContainer;
    private bool _isAutoRunning = false;
    private bool _isSneaking = false;
    private bool _playerInCombat = false;
    private double _zoneImmunityTimer = 0.0; // Seconds of immunity after teleport
    private Node3D _zoneGeometryContainer;
    private bool _flyMode = false; // Admin fly mode (F5 toggle)
    private bool _f5Held = false;
    private bool _f6Held = false;

    // Expose the current target ID for the attack system
    public string CurrentTargetId => GodotObject.IsInstanceValid(_currentTarget) ? _currentTarget.Name : null;

    // Range constants (in world units)
    public const float MELEE_RANGE = 4f;
    public const float BOW_RANGE = 30f;
    public const float SPELL_RANGE_DEFAULT = 20f;

    /// <summary>Returns distance from player to current target, or -1 if no target.</summary>
    public float GetDistanceToTarget()
    {
        if (_playerCapsule == null || !GodotObject.IsInstanceValid(_currentTarget)) return -1f;
        return _playerCapsule.GlobalPosition.DistanceTo(_currentTarget.GlobalPosition);
    }

    /// <summary>Check if the current target is within the given range.</summary>
    public bool IsTargetInRange(float range)
    {
        float dist = GetDistanceToTarget();
        return dist >= 0 && dist <= range;
    }

    public override void _Ready()
    {
        _spawnsContainer = GetNode<Node3D>("Spawns");
        _cameraArm = GetNode<Node3D>("CameraArm");
        _camera = GetNode<Camera3D>("CameraArm/Camera3D");

        _boundariesContainer = new Node3D { Name = "Boundaries" };
        AddChild(_boundariesContainer);

        GD.Print("[WORLD] 3D World Initialized");

        // Player is now spawned lazily via SpawnPlayer() after the world is ready
        UpdateCamera();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_playerCapsule == null) return;

        // Tick down zone immunity timer
        if (_zoneImmunityTimer > 0)
            _zoneImmunityTimer -= delta;

        // --- WASD Movement & Turning ---
        float baseSpeed = 8f;
        if (_isSneaking) baseSpeed = 4f;
        else if (Input.IsPhysicalKeyPressed(Key.Shift) || _isAutoRunning) baseSpeed = 40f;
        
        float speed = baseSpeed;
        float gravity = 50f;
        Vector3 velocity = _playerCapsule.Velocity;
        bool rightClickHeld = Input.IsMouseButtonPressed(MouseButton.Right);
        
        // Manual movement cancels autorun
        if (Input.IsActionPressed("move_forward") || Input.IsActionPressed("move_back")) {
            _isAutoRunning = false;
        }

        if (CurrentCameraMode == CameraMode.Drive)
        {
            // --- Drive Mode ---
            if (rightClickHeld)
            {
                // Right-Click Held: Strafe behavior
                Vector3 inputDir = Vector3.Zero;
                if (Input.IsActionPressed("move_forward")) inputDir.Z -= 1;
                if (Input.IsActionPressed("move_back"))    inputDir.Z += 1;
                if (Input.IsActionPressed("move_left"))    inputDir.X -= 1;
                if (Input.IsActionPressed("move_right"))   inputDir.X += 1;

                if (inputDir != Vector3.Zero)
                {
                    inputDir = inputDir.Normalized();
                    Vector3 forward = -_playerCapsule.GlobalTransform.Basis.Z;
                    Vector3 right = _playerCapsule.GlobalTransform.Basis.X;
                    Vector3 moveDir = forward * -inputDir.Z + right * inputDir.X;
                    velocity.X = moveDir.X * speed;
                    velocity.Z = moveDir.Z * speed;
                }
                else
                {
                    velocity.X = 0;
                    velocity.Z = 0;
                }
            }
            else
            {
                // Normal Drive Mode: A/D to turn, W/S to move
                float turnAmount = 0f;
                if (Input.IsActionPressed("move_left")) turnAmount += 1;
                if (Input.IsActionPressed("move_right")) turnAmount -= 1;

                float turnSpeed = 3f;
                _playerCapsule.RotateY(turnAmount * turnSpeed * (float)delta);

                float forwardInput = 0;
                if (_isAutoRunning) forwardInput += 1;
                if (Input.IsActionPressed("move_forward")) forwardInput += 1;
                if (Input.IsActionPressed("move_back"))    forwardInput -= 1;

                if (forwardInput != 0)
                {
                    Vector3 forwardVec = -_playerCapsule.GlobalTransform.Basis.Z;
                    velocity.X = forwardVec.X * speed * forwardInput;
                    velocity.Z = forwardVec.Z * speed * forwardInput;
                }
                else
                {
                    velocity.X = 0;
                    velocity.Z = 0;
                }

                // Snap camera yaw to match player facing direction
                _cameraYaw = Mathf.RadToDeg(_playerCapsule.Rotation.Y);
            }
        }
        else
        {
            // --- Free Look Mode ---
            Vector3 inputDir = Vector3.Zero;
            if (_isAutoRunning) inputDir.Z -= 1;
            if (Input.IsActionPressed("move_forward")) inputDir.Z -= 1;
            if (Input.IsActionPressed("move_back"))    inputDir.Z += 1;
            if (Input.IsActionPressed("move_left"))    inputDir.X -= 1;
            if (Input.IsActionPressed("move_right"))   inputDir.X += 1;

            if (inputDir != Vector3.Zero)
            {
                inputDir = inputDir.Normalized();
                
                // Natively rotate the input vector around the Up axis relative to the camera's yaw
                inputDir = inputDir.Rotated(Vector3.Up, Mathf.DegToRad(_cameraYaw));

                if (!rightClickHeld)
                {
                    var targetTransform = _playerCapsule.GlobalTransform.LookingAt(_playerCapsule.GlobalPosition + inputDir, Vector3.Up);
                    _playerCapsule.GlobalTransform = _playerCapsule.GlobalTransform.InterpolateWith(targetTransform, 15f * (float)delta);
                }
            }

            velocity.X = inputDir.X * speed;
            velocity.Z = inputDir.Z * speed;
        }

        // Apply gravity & Jump (or fly mode)
        if (_flyMode)
        {
            // Fly mode: Space = up, Ctrl = down, no gravity
            velocity.Y = 0;
            if (Input.IsPhysicalKeyPressed(Key.Space)) velocity.Y = speed;
            if (Input.IsPhysicalKeyPressed(Key.Ctrl)) velocity.Y = -speed;
        }
        else if (!_playerCapsule.IsOnFloor())
        {
            velocity.Y -= gravity * (float)delta;
        }
        else
        {
            velocity.Y = 0;
            if (Input.IsPhysicalKeyPressed(Key.Space)) {
                velocity.Y = 18.0f; // Jump force
            }
        }

        _playerCapsule.Velocity = velocity;
        _playerCapsule.MoveAndSlide();

        // Update player animation based on movement, jump, and combat state
        bool isSprinting = Input.IsPhysicalKeyPressed(Key.Shift) || _isAutoRunning;
        _playerCapsule.UpdateAnimationFromVelocity(
            _playerCapsule.Velocity,
            _playerCapsule.IsOnFloor(),
            _playerInCombat,
            isSprinting
        );

        // Sync position to server if moved > 0.1 units
        if (_playerCapsule.GlobalPosition.DistanceTo(_lastSentPos) > 0.1f)
        {
            _lastSentPos = _playerCapsule.GlobalPosition;
            EmitSignal(SignalName.PlayerMoved, _lastSentPos.X, _lastSentPos.Z);
        }

        // --- Debug Admin Tools ---
        if (Input.IsPhysicalKeyPressed(Key.F5) && !_f5Held)
        {
            _f5Held = true;
            _flyMode = !_flyMode;
            GD.Print($"[WORLD] Fly mode: {(_flyMode ? "ON" : "OFF")}");
        }
        if (!Input.IsPhysicalKeyPressed(Key.F5)) _f5Held = false;

        if (Input.IsPhysicalKeyPressed(Key.F6) && !_f6Held)
        {
            _f6Held = true;
            var gPos = _playerCapsule.GlobalPosition;
            // Back-convert Godot → EQ: EQ.X = -Godot.X, EQ.Y = -Godot.Z, EQ.Z = Godot.Y
            float eqX = -gPos.X;
            float eqY = -gPos.Z;
            float eqZ = gPos.Y;
            GD.Print($"[LOC] Godot: ({gPos.X:F1}, {gPos.Y:F1}, {gPos.Z:F1}) | EQ: ({eqX:F1}, {eqY:F1}, {eqZ:F1})");
        }
        if (!Input.IsPhysicalKeyPressed(Key.F6)) _f6Held = false;

        // --- Targeting Keybinds (polled every physics frame) ---
        if (Input.IsActionJustPressed("target_self"))
            TargetSelf();
        if (Input.IsActionJustPressed("target_next_enemy"))
            CycleTarget("enemy", 1);
        if (Input.IsActionJustPressed("target_prev_enemy"))
            CycleTarget("enemy", -1);
        if (Input.IsActionJustPressed("target_next_friendly"))
            CycleTarget("friendly", 1);
        if (Input.IsActionJustPressed("target_prev_friendly"))
            CycleTarget("friendly", -1);

        UpdateCamera();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            // Toggle Sneak
            if (keyEvent.Keycode == Key.Ctrl)
            {
                _isSneaking = !_isSneaking;
                _playerCapsule.SetSneakState(_isSneaking, true);
                EmitSignal(SignalName.SneakToggled, _isSneaking);
            }
            // Toggle AutoRun
            if (keyEvent.Keycode == Key.Numlock)
            {
                _isAutoRunning = !_isAutoRunning;
            }
            
            // Toggle camera mode with Keypad *
            if (keyEvent.Keycode == Key.KpMultiply)
            {
                CurrentCameraMode = CurrentCameraMode == CameraMode.FreeLook ? CameraMode.Drive : CameraMode.FreeLook;
                GD.Print($"[WORLD] Camera Mode changed to {CurrentCameraMode}");
            }
        }

        // Camera Rotation and Steering
        if (@event is InputEventMouseMotion mouseMotion)
        {
            if (Input.IsMouseButtonPressed(MouseButton.Right))
            {
                if (CurrentCameraMode == CameraMode.Drive)
                {
                    // Drive mode: Right click steers the player
                    _playerCapsule.RotateY(-mouseMotion.Relative.X * 0.005f);
                    _cameraYaw = Mathf.RadToDeg(_playerCapsule.Rotation.Y);

                    _cameraPitch -= mouseMotion.Relative.Y * 0.3f;
                    _cameraPitch = Mathf.Clamp(_cameraPitch, -80f, 60f);
                }
                else
                {
                    // Free Look mode: Right click orbits camera
                    _cameraYaw -= mouseMotion.Relative.X * 0.3f;
                    _cameraPitch -= mouseMotion.Relative.Y * 0.3f;
                    _cameraPitch = Mathf.Clamp(_cameraPitch, -80f, 60f);
                }
            }
            else if (Input.IsMouseButtonPressed(MouseButton.Left))
            {
                // Left click: orbit camera around character without turning them
                _cameraYaw -= mouseMotion.Relative.X * 0.3f;
                _cameraPitch -= mouseMotion.Relative.Y * 0.3f;
                _cameraPitch = Mathf.Clamp(_cameraPitch, -80f, 60f);
            }
        }

        // Scroll to zoom
        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.WheelUp)
                _cameraDistance = Mathf.Max(3f, _cameraDistance - 1f);
            else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
                _cameraDistance = Mathf.Min(25f, _cameraDistance + 1f);
        }
    }

    // --- Targeting System ---

    private void TargetSelf()
    {
        SetTarget(_playerCapsule);
        GD.Print("[WORLD] Targeted self.");
    }

    private void CycleTarget(string category, int direction)
    {
        List<EntityCapsule> candidates = new List<EntityCapsule>();

        foreach (var kvp in _activeEntities)
        {
            if (kvp.Value is EntityCapsule ec && ec != _playerCapsule)
            {
                if (category == "enemy" && (ec.EntityType == "enemy" || ec.EntityType == "mob"))
                    candidates.Add(ec);
                else if (category == "friendly" && (ec.EntityType == "player" || ec.EntityType == "npc"))
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

    public void SetTarget(EntityCapsule target)
    {
        // Deselect old
        if (_currentTarget != null)
            _currentTarget.SetTargeted(false);

        _currentTarget = target;

        if (_currentTarget != null)
        {
            _currentTarget.SetTargeted(true);
            EmitSignal(SignalName.TargetChanged, target.EntityName, target.EntityType);
            GD.Print($"[WORLD] Targeted: {target.EntityName}");
        }
        else
        {
            EmitSignal(SignalName.TargetCleared);
        }
    }

    private void UpdateCamera()
    {
        if (_playerCapsule == null || _cameraArm == null || _camera == null) return;

        _cameraArm.GlobalPosition = _playerCapsule.GlobalPosition + new Vector3(0, 1.5f, 0);
        _cameraArm.Rotation = new Vector3(
            Mathf.DegToRad(_cameraPitch),
            Mathf.DegToRad(_cameraYaw),
            0
        );
        _camera.Position = new Vector3(0, 0, _cameraDistance);
    }

    // --- Entity Management ---

    public void ClearWorld()
    {
        foreach (var child in _spawnsContainer.GetChildren())
        {
            if (child != _playerCapsule)
                child.QueueFree();
        }
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

    public void SpawnEntityAt(string id, string name, string type, Vector3 pos, string appearanceJson = "", int race = 1, int gender = 0)
    {
        if (_activeEntities.ContainsKey(id)) return;

        var instance = new EntityCapsule();
        instance.Position = pos;
        instance.Name = id;

        _spawnsContainer.AddChild(instance);
        _activeEntities[id] = instance;

        try {
            instance.Setup(name, type, appearanceJson, race, gender);
        } catch (Exception ex) {
            GD.PrintErr($"[WORLD] Failed Setup on '{name}': {ex.Message}");
        }
    }

    public void SyncLiveMobs(System.Text.Json.JsonElement entitiesArray)
    {
        int count = entitiesArray.GetArrayLength();
        GD.Print($"[WORLD] SyncLiveMobs called with {count} entities");

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
            bool sneaking = ent.TryGetProperty("sneaking", out var sProp) && sProp.GetBoolean();

            incomingIds.Add(id);

            if (!existingIds.Contains(id))
            {
                GD.Print($"[WORLD] Spawning '{name}' (race={race} gender={gender}) at server coords: {x}, {y}, {z}");
                SpawnEntityAt(id, name, type, new Vector3(x, y, z), appearance, race, gender);
            }
            
            UpdateEntitySneak(id, sneaking);
        }

        // Remove entities that no longer exist on the server
        foreach (var oldId in existingIds)
        {
            if (!incomingIds.Contains(oldId))
            {
                RemoveEntity(oldId);
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
            if (entity == _currentTarget)
                _currentTarget = null;
                
            entity.QueueFree();
            _activeEntities.Remove(id);
        }
    }

    public void RebuildZoneBoundaries(System.Text.Json.JsonElement mapSizeElement, System.Text.Json.JsonElement zoneLines, System.Text.Json.JsonElement centerOffsetElement)
    {
        // Clear old boundaries
        foreach (var child in _boundariesContainer.GetChildren())
        {
            child.QueueFree();
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
        
        // Build the dynamic visual green floor using true geometry
        var floorMeshInstance = new MeshInstance3D { Name = "VisualFloor" };
        var boxMesh = new BoxMesh { Size = new Vector3(mapWidth, 1f, mapLength) };
        var grassMat = new StandardMaterial3D {
            AlbedoColor = new Color(0.2f, 0.4f, 0.15f), // Grass Green
            Roughness = 0.8f
        };
        boxMesh.Material = grassMat;
        floorMeshInstance.Mesh = boxMesh;
        // Positioned at the center of the coordinate span
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

        // Add Topographical Zone triggers
        if (zoneLines.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var zl in zoneLines.EnumerateArray())
            {
                string targetZone = zl.TryGetProperty("target", out var tz) ? tz.GetString() : "unknown";
                
                // Check if this is a coordinate-based zone point (from DB) or edge-based (legacy)
                bool hasCoords = zl.TryGetProperty("x", out var xProp) && xProp.GetSingle() != 0;
                
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
                triggerArea.BodyEntered += (body) => {
                    if (body == _playerCapsule && _zoneImmunityTimer <= 0)
                    {
                        GD.Print($"[WORLD] Player crossed Zone Line: {targetZone}");
                        EmitSignal(SignalName.ZoneLineCrossed, targetZone);
                    }
                };

                _boundariesContainer.AddChild(triggerArea);
            }
        }
    }

    public void LoadZoneMap(string zoneId)
    {
        if (_zoneGeometryContainer == null)
        {
            _zoneGeometryContainer = new Node3D { Name = "ZoneGeometry" };
            AddChild(_zoneGeometryContainer);
        }
        else
        {
            foreach (var child in _zoneGeometryContainer.GetChildren())
            {
                child.QueueFree();
            }
        }

        // --- Try GLB first (real 3D zone geometry) ---
        string glbPath = $"res://Data/Maps/{zoneId}_raw.glb";
        if (Godot.FileAccess.FileExists(glbPath))
        {
            GD.Print($"[WORLD] Found GLB zone model: {glbPath}");
            if (LoadZoneGlb(zoneId, glbPath))
                return; // Success — skip Brewall fallback
            GD.PrintErr($"[WORLD] GLB load failed, falling back to Brewall lines...");
        }

        // --- Fallback: Brewall line-based map ---
        LoadZoneBrewall(zoneId);
    }

    /// <summary>
    /// Loads a real 3D zone model from a GLB file (sourced from EQ Advanced Maps CDN).
    /// The GLB is in Three.js coordinate space; we rotate -90° around Y to match our Godot mapping.
    /// Generates trimesh collision from all mesh surfaces for accurate walkable geometry.
    /// </summary>
    private bool LoadZoneGlb(string zoneId, string glbPath)
    {
        try
        {
            var gltfDoc = new GltfDocument();
            var gltfState = new GltfState();

            // Load the GLB file bytes
            using var file = Godot.FileAccess.Open(glbPath, Godot.FileAccess.ModeFlags.Read);
            byte[] glbBytes = file.GetBuffer((long)file.GetLength());

            var err = gltfDoc.AppendFromBuffer(glbBytes, "", gltfState);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[WORLD] GLTF parse error: {err}");
                return false;
            }

            Node scene = gltfDoc.GenerateScene(gltfState);
            if (scene == null)
            {
                GD.PrintErr("[WORLD] GLTF generated null scene");
                return false;
            }

            // The GLB is in Three.js space (EQ.y*-1, EQ.z, EQ.x).
            // Our Godot uses (-EQ.x, EQ.z, -EQ.y).
            // Transform: Godot.X = -Three.Z, Godot.Y = Three.Y, Godot.Z = Three.X
            // This equals a -90° rotation around the Y axis.
            var zoneRoot = new Node3D { Name = $"GLB_{zoneId}" };
            zoneRoot.RotationDegrees = new Vector3(0, -90, 0);
            zoneRoot.AddChild(scene);
            _zoneGeometryContainer.AddChild(zoneRoot);

            // Walk the scene tree and generate trimesh collision for every MeshInstance3D
            int meshCount = 0;
            int collisionCount = 0;
            GenerateCollisionRecursive(scene, ref meshCount, ref collisionCount);

            // Calculate bounds from the zone geometry for floor/boundaries
            var aabb = CalculateWorldAabb(zoneRoot);
            if (aabb.Size.Length() > 0)
            {
                float pad = 50f;
                float mapWidth = aabb.Size.X + pad * 2;
                float mapLength = aabb.Size.Z + pad * 2;
                float centerX = aabb.Position.X + aabb.Size.X / 2f;
                float centerZ = aabb.Position.Z + aabb.Size.Z / 2f;

                GD.Print($"[WORLD] GLB bounds: pos=({aabb.Position}), size=({aabb.Size})");
                GD.Print($"[WORLD] Floor center=({centerX},{centerZ}), size=({mapWidth}x{mapLength})");
                
                // Remove the old flat floor since we have real geometry now.
                // Only rebuild invisible boundary walls (no visual floor — the GLB IS the floor).
                RebuildBoundaryWallsOnly(centerX, centerZ, mapWidth, mapLength, aabb.Position.Y - 10f);
            }

            GD.Print($"[WORLD] GLB loaded: {meshCount} meshes, {collisionCount} collision shapes for {zoneId}");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[WORLD] GLB load exception: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Recursively walks the scene tree and creates trimesh (ConcavePolygon) collision
    /// for every MeshInstance3D found. This gives pixel-perfect collision matching the zone geometry.
    /// </summary>
    private void GenerateCollisionRecursive(Node node, ref int meshCount, ref int collisionCount)
    {
        if (node is MeshInstance3D meshInst && meshInst.Mesh != null)
        {
            meshCount++;
            meshInst.CreateTrimeshCollision();
            collisionCount++;
        }

        foreach (var child in node.GetChildren())
        {
            GenerateCollisionRecursive(child, ref meshCount, ref collisionCount);
        }
    }

    /// <summary>
    /// Calculates the world-space AABB encompassing all MeshInstance3D nodes under a parent.
    /// </summary>
    private Aabb CalculateWorldAabb(Node3D root)
    {
        Aabb combined = new Aabb();
        bool first = true;
        CalculateAabbRecursive(root, ref combined, ref first);
        return combined;
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

    /// <summary>
    /// Creates only invisible boundary walls and a physics-only floor (no visual green floor)
    /// since the GLB zone model provides the real visual terrain.
    /// </summary>
    private void RebuildBoundaryWallsOnly(float centerX, float centerZ, float mapWidth, float mapLength, float floorY)
    {
        // Remove old floor/boundary elements
        foreach (var child in _boundariesContainer.GetChildren())
        {
            string name = child.Name;
            if (name == "VisualFloor" || name == "PhysicalFloor" || name.StartsWith("Wall_"))
            {
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

    /// <summary>
    /// Legacy Brewall line-based zone loading. Used as fallback when no GLB is available.
    /// </summary>
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

    /// <summary>
    /// Rebuilds the visual and physical floor to match the map geometry bounds.
    /// Called after loading map data so the floor covers the entire zone drawing.
    /// </summary>
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
                child.QueueFree();
            }
        }

        // Build the dynamic visual green floor
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
    
    public void SpawnPlayer(float rawX, float rawY)
    {
        if (_playerCapsule != null) return;

        // Map EQ Coords (+X=West, +Y=North) to Godot Coords (-X=West, -Z=North)
        float x = -rawX;
        float z = -rawY;

        GD.Print($"[WORLD] Spawning Player at {x}, {z}");
        _playerCapsule = new EntityCapsule();
        _playerCapsule.Name = "Player";
        _playerCapsule.Position = new Vector3(x, 2.0f, z);
        _spawnsContainer.AddChild(_playerCapsule);
        _playerCapsule.Setup("You", "player");
        _playerCapsule.IsPlayerControlled = true;
        _activeEntities["player_self"] = _playerCapsule;
    }

    public void TeleportPlayer(float rawX, float rawY)
    {
        // Map EQ Coords to Godot Coords
        float x = -rawX;
        float z = -rawY;

        if (_playerCapsule == null)
        {
            SpawnPlayer(rawX, rawY);
        }

        // Drop them from slightly higher to guarantee they hit the instant floor securely
        float safeDropHeight = 3.0f;
        _playerCapsule.GlobalPosition = new Vector3(x, safeDropHeight, z);
        _playerCapsule.Velocity = Vector3.Zero;
        _lastSentPos = new Vector3(x, safeDropHeight, z);
        EmitSignal(SignalName.PlayerMoved, x, z); // Force sync

        // Grant zone immunity so we don't instantly trigger a nearby zoneline
        _zoneImmunityTimer = 5.0; // Seconds of immunity after zone-in (prevents re-trigger)
    }

    public void SetCombatTarget(string targetId)
    {
        // First disconnect everyone from chasing the player
        foreach (var kvp in _activeEntities)
        {
            if (kvp.Value is EntityCapsule ec && ec.ChaseTarget == _playerCapsule)
                ec.ChaseTarget = null;
        }

        _playerInCombat = !string.IsNullOrEmpty(targetId);
        if (string.IsNullOrEmpty(targetId)) return;

        EntityCapsule activeEc = null;

        // For local tests where 'mob_' was stripped off by server, try both
        if (_activeEntities.TryGetValue(targetId, out Node3D tgt) || _activeEntities.TryGetValue($"mob_{targetId}", out tgt))
        {
            activeEc = tgt as EntityCapsule;
        }
        else if (_currentTarget != null)
        {
            // Fallback: Use what the user clicked locally, since that's what triggered combat
            activeEc = _currentTarget;
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
            activeEc.ChaseTarget = _playerCapsule;
            if (_currentTarget != activeEc)
            {
                SetTarget(activeEc);
            }
        }
    }

    public void SetPlayerSitting(bool sitting)
    {
        if (_playerCapsule == null) return;
        if (sitting)
            _playerCapsule.PlaySit();
        else
            _playerCapsule.Revive(); // resets _isSitting and returns to idle
    }
}
