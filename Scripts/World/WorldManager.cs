using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class WorldManager : Node3D
{
    private Node3D _spawnsContainer;
    private SpringArm3D _cameraArm;
    private Camera3D _camera;
    private EntityCapsule _playerCapsule;
    private EntityCapsule _currentTarget;
    private Dictionary<string, Node3D> _activeEntities = new Dictionary<string, Node3D>();
    private string _currentVisionStyle = "normal";

    private int _spawnCounter = 0;
    private int _enemyTargetIndex = -1;
    private int _friendlyTargetIndex = -1;

    public enum CameraMode { FreeLook, Drive }
    public CameraMode CurrentCameraMode { get; private set; } = CameraMode.Drive;
    
    // Graphics Settings
    public bool DynamicShadowsEnabled { get; set; } = true;

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
    [Signal] public delegate void HideToggledEventHandler(bool isHiding);
    [Signal] public delegate void SyncProgressEventHandler(int current, int total);
    private Vector3 _lastSentPos = Vector3.Zero;
    private Node3D _boundariesContainer;
    private bool _isAutoRunning = false;
    private bool _isSneaking = false;
    private bool _isHiding = false;
    public bool PlayerHasStealthSkill { get; set; } = false;
    private bool _isCrouching = false;
    private bool _playerInCombat = false;
    private double _zoneImmunityTimer = 0.0; // Seconds of immunity after teleport
    private Node3D _zoneGeometryContainer;
    private Node3D _zoneObjectsContainer; // Placed zone objects (trees, buildings, etc.)
    private ZoneObjectPlacer _objectPlacer;
    private ZoneMusicPlayer _musicPlayer;
    /// <summary>Public accessor for the zone music player — used by Audio Player UI.</summary>
    public ZoneMusicPlayer MusicPlayer => _musicPlayer;
    private bool _flyMode = false; // Admin fly mode (F5 toggle)
    private bool _f5Held = false;
    private bool _f6Held = false;
    private bool _f4Held = false;

    // Footstep sound system
    private AudioStreamPlayer _footstepPlayer;
    private double _footstepTimer = 0;
    private double _footstepCadence = 0.45; // seconds between footsteps
    private string _lastFootstepState = "idle"; // idle, walk, run, swim

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

    // Day/Night Cycle
    public bool SmoothDayNightCycle { get; set; } = true;
    private DirectionalLight3D _sun;
    private DirectionalLight3D _moon;
    private Sprite3D _moonSprite;
    private ShaderMaterial _moonMaterial;
    private WorldEnvironment _environment;
    
    // Time state
    private float _currentWorldHour = 12f;
    private float _targetWorldHour = 12f;
    private int _dawnHour = 6;
    private int _duskHour = 18;
    private string _currentMoonPhase = "Full";
    private bool _timeInitialized = false;
    private string _currentZoneId = "";
    private bool _isIndoorZone = false;

    public override void _Ready()
    {
        _spawnsContainer = GetNode<Node3D>("Spawns");
        var oldArm = GetNode<Node3D>("CameraArm");
        _camera = oldArm.GetNode<Camera3D>("Camera3D");
        oldArm.RemoveChild(_camera);
        
        _cameraArm = new SpringArm3D();
        _cameraArm.Name = "CameraArm";
        _cameraArm.CollisionMask = 1; // Collide with environment
        _cameraArm.Shape = new SphereShape3D { Radius = 0.5f };
        _cameraArm.SpringLength = _cameraDistance;
        
        AddChild(_cameraArm);
        _cameraArm.AddChild(_camera);
        _camera.Position = Vector3.Zero; // SpringArm3D handles the offset
        
        oldArm.QueueFree();

        _boundariesContainer = new Node3D { Name = "Boundaries" };
        AddChild(_boundariesContainer);

        // Environment Setup
        _sun = GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        _environment = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");        // Spawn Moon
        _moon = new DirectionalLight3D { Name = "MoonLight" };
        _moon.LightColor = new Color(0.6f, 0.7f, 1.0f); // Pale blue
        _moon.LightEnergy = 0.0f;
        _moon.ShadowEnabled = false; // Disable shadows for moon for performance
        _moon.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly; // Don't draw a black disk in the sky
        AddChild(_moon);

        // Vision Manager
        var visionManager = new VisionManager();
        visionManager.Name = "VisionManager";
        AddChild(visionManager);

        // Spawn Moon Sprite3D
        _moonSprite = new Sprite3D { Name = "MoonSprite" };
        _moonSprite.Texture = GD.Load<Texture2D>("res://Assets/Textures/drinal_full_moon.jpg");
        _moonSprite.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        _moonSprite.PixelSize = 1.0f; // Large scale for the sky
        _moonSprite.Position = new Vector3(0, 0, -800f); // Far away
        _moon.AddChild(_moonSprite);

        // Load Moon Phase Shader
        _moonMaterial = new ShaderMaterial();
        _moonMaterial.Shader = GD.Load<Shader>("res://Assets/Shaders/MoonPhase.gdshader");
        _moonSprite.MaterialOverride = _moonMaterial;

        // Initialize Audio Subsystems
        _musicPlayer = new ZoneMusicPlayer();
        AddChild(_musicPlayer);

        GD.Print("[WORLD] 3D World Initialized");

        // Player is now spawned lazily via SpawnPlayer() after the world is ready
        UpdateCamera();
    }

    public void UpdateEnvironmentTime(int hour, int dawn, int dusk, string moonPhase = "Full", bool initialLoad = false)
    {
        _targetWorldHour = hour;
        _dawnHour = dawn;
        _duskHour = dusk;
        _currentMoonPhase = moonPhase;
        
        if (initialLoad || !_timeInitialized || !SmoothDayNightCycle)
        {
            _currentWorldHour = hour;
            _timeInitialized = true;
            ApplyTimeOfDayVisuals();
        }
    }

    public EntityCapsule GetEntityByName(string name)
    {
        if (name == "You") return _playerCapsule;
        foreach (var entity in _activeEntities.Values)
        {
            if (entity is EntityCapsule cap && cap.EntityName == name)
                return cap;
        }
        return null;
    }

    public void TriggerEntityAction(string name, string action)
    {
        var entity = GetEntityByName(name);
        if (entity != null)
        {
            if (action == "hit") entity.PlayDamage();
            else if (action == "miss") entity.PlaySfx("aam_hit.wav"); // Placeholder for miss/whoosh sound
            else if (action == "cast") entity.PlayCast();
            else if (action == "die") entity.PlayDeath();
            else if (action == "fizzle") entity.PlaySfx("fizzle.wav");
            else if (action.StartsWith("attack:"))
            {
                string type = action.Split(':')[1];
                entity.PlayAttack(type);
            }
        }
    }

    /// <summary>Set whether the current zone is indoor (driven by server ZONE_STATE data).</summary>
    public void SetIndoorZone(bool indoor)
    {
        _isIndoorZone = indoor;
        ApplyTimeOfDayVisuals();
    }

    private void ApplyTimeOfDayVisuals()
    {
        if (_sun == null || _moon == null) return;

        bool isIndoor = _isIndoorZone;

        if (isIndoor)
        {
            _sun.Visible = false;
            _moon.Visible = false;
            if (_moonSprite != null) _moonSprite.Visible = false;

            if (_environment != null && _environment.Environment != null)
            {
                float bgEnergy = 1.0f;
                float ambEnergy = 1.0f;
                
                if (_currentVisionStyle == "ultravision")
                {
                    bgEnergy = 2.0f;
                    ambEnergy = 15.0f; // Turn pitch black night into day (0.05 * 15 = 0.75 ambient)
                }
                else if (_currentVisionStyle == "infravision")
                {
                    bgEnergy = 1.0f;
                    ambEnergy = 6.0f; // Bright enough to see clearly but still dark
                }

                _environment.Environment.BackgroundMode = Godot.Environment.BGMode.Color;
                _environment.Environment.BackgroundColor = Colors.Black;
                _environment.Environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
                _environment.Environment.AmbientLightColor = new Color(0.05f, 0.05f, 0.05f); 
                _environment.Environment.AmbientLightEnergy = ambEnergy;
                _environment.Environment.BackgroundEnergyMultiplier = bgEnergy;
            }
            return;
        }

        _sun.Visible = true;
        _moon.Visible = true;
        if (_moonSprite != null) _moonSprite.Visible = true;

        // Progress 0 to 1 over 24 hours. 0 = midnight, 0.5 = noon
        float progress = _currentWorldHour / 24f;
        
        // Pitch mapping: 
        // 0 (Midnight): Sun is straight down (-90 degrees)
        // 6 (Dawn): Sun is at horizon (0 degrees)
        // 12 (Noon): Sun is straight up (90 degrees)
        // 18 (Dusk): Sun is at horizon (0 or 180 degrees)
        
        float sunPitchDegrees = (progress * 360f) - 90f; 
        _sun.RotationDegrees = new Vector3(sunPitchDegrees, -45f, 0f);
        
        // Moon is opposite of sun
        float moonPitchDegrees = sunPitchDegrees + 180f;
        _moon.RotationDegrees = new Vector3(moonPitchDegrees, -45f, 0f);

        // Update Moon Phase Shader
        if (_moonMaterial != null)
        {
            float phaseVal = 0.5f; // Full
            switch (_currentMoonPhase)
            {
                case "New": phaseVal = 0.0f; break;
                case "Waxing Crescent": phaseVal = 0.125f; break;
                case "First Quarter": phaseVal = 0.25f; break;
                case "Waxing Gibbous": phaseVal = 0.375f; break;
                case "Full": phaseVal = 0.5f; break;
                case "Waning Gibbous": phaseVal = 0.625f; break;
                case "Last Quarter": phaseVal = 0.75f; break;
                case "Waning Crescent": phaseVal = 0.875f; break;
            }
            _moonMaterial.SetShaderParameter("phase", phaseVal);
        }

        // Skybox Colors
        Color dayTopColor = new Color(0.2f, 0.4f, 0.6f);
        Color dayHorizonColor = new Color(0.6f, 0.7f, 0.8f);
        Color nightTopColor = new Color(0.01f, 0.02f, 0.05f);
        Color nightHorizonColor = new Color(0.05f, 0.1f, 0.15f);

        ProceduralSkyMaterial skyMat = null;
        if (_environment != null && _environment.Environment != null && _environment.Environment.Sky != null)
        {
            skyMat = _environment.Environment.Sky.SkyMaterial as ProceduralSkyMaterial;
        }

        // Lighting intensity
        float dayDuration = _duskHour - _dawnHour;
        float dayProgress = 0f;
        
        if (_currentWorldHour >= _dawnHour && _currentWorldHour <= _duskHour)
        {
            // Daytime
            dayProgress = (_currentWorldHour - _dawnHour) / dayDuration;
            
            // Parabola: peaks at 1.0 at noon (midday), 0 at dawn/dusk
            float sunIntensity = 1.0f - Mathf.Pow((dayProgress - 0.5f) * 2f, 2f);
            sunIntensity = Mathf.Clamp(sunIntensity, 0f, 1f);
            
            _sun.LightEnergy = sunIntensity;
            _moon.LightEnergy = 0f;
            
            // Adjust environment ambient/sky energy if it exists
            if (_environment != null && _environment.Environment != null)
            {
                float bgEnergy = Mathf.Max(0.2f, sunIntensity);
                float ambEnergy = Mathf.Max(0.1f, sunIntensity);
                
                if (_currentVisionStyle == "ultravision")
                {
                    bgEnergy = Mathf.Max(bgEnergy, 2.0f);
                    ambEnergy = Mathf.Max(ambEnergy, 4.0f);
                }
                else if (_currentVisionStyle == "infravision")
                {
                    bgEnergy = Mathf.Max(bgEnergy, 1.0f);
                    ambEnergy = Mathf.Max(ambEnergy, 1.5f);
                }
                
                _environment.Environment.BackgroundEnergyMultiplier = bgEnergy;
                _environment.Environment.AmbientLightEnergy = ambEnergy;
            }
            
            if (skyMat != null)
            {
                skyMat.SkyTopColor = nightTopColor.Lerp(dayTopColor, sunIntensity);
                skyMat.SkyHorizonColor = nightHorizonColor.Lerp(dayHorizonColor, sunIntensity);
            }
        }
        else
        {
            // Nighttime
            _sun.LightEnergy = 0f;
            
            // Moon peaks at midnight
            float nightProgress;
            if (_currentWorldHour > _duskHour)
                nightProgress = (_currentWorldHour - _duskHour) / (24f - _duskHour + _dawnHour);
            else
                nightProgress = (_currentWorldHour + (24f - _duskHour)) / (24f - _duskHour + _dawnHour);

            float moonIntensity = 1.0f - Mathf.Pow((nightProgress - 0.5f) * 2f, 2f);
            moonIntensity = Mathf.Clamp(moonIntensity, 0f, 1f);
            
            _moon.LightEnergy = moonIntensity * 0.3f; // Moon is max 30% as bright as sun
            
            if (_environment != null && _environment.Environment != null)
            {
                float bgEnergy = Mathf.Max(0.1f, moonIntensity * 0.3f);
                float ambEnergy = Mathf.Max(0.05f, moonIntensity * 0.3f);
                
                if (_currentVisionStyle == "ultravision")
                {
                    bgEnergy = Mathf.Max(bgEnergy, 2.0f);
                    ambEnergy = Mathf.Max(ambEnergy, 4.0f);
                }
                else if (_currentVisionStyle == "infravision")
                {
                    bgEnergy = Mathf.Max(bgEnergy, 1.0f);
                    ambEnergy = Mathf.Max(ambEnergy, 1.5f);
                }
                
                _environment.Environment.BackgroundEnergyMultiplier = bgEnergy;
                _environment.Environment.AmbientLightEnergy = ambEnergy;
            }
            
            if (skyMat != null)
            {
                skyMat.SkyTopColor = nightTopColor;
                skyMat.SkyHorizonColor = nightHorizonColor;
            }
        }
    }

    public override void _Process(double delta)
    {
        // Keep the celestial bodies infinitely far away by centering them on the camera
        if (_moon != null && _camera != null)
        {
            _moon.GlobalPosition = _camera.GlobalPosition;
        }

        if (_timeInitialized)
        {
            if (SmoothDayNightCycle)
            {
                // In EQEmu, 1 in-game hour passes every 90 seconds (72 minutes per real day).
                // But we don't need to guess perfectly; we just linearly interpolate towards _targetWorldHour.
                
                // If we're behind, move towards target
                float diff = _targetWorldHour - _currentWorldHour;
                
                // Handle midnight wrap-around (e.g., target is 0.1, current is 23.9)
                if (diff < -12f) diff += 24f;
                else if (diff > 12f) diff -= 24f;

                if (Mathf.Abs(diff) > 0.01f)
                {
                    // Move 1 hour per second in real time to catch up if we're far behind, 
                    // or just slowly drift if we're close. 
                    // Normally server ticks every hour, so diff is 1.0. 
                    // To make it smooth over the 90 real-seconds it takes for the server to tick an hour:
                    // 1 hour / 90 seconds = 0.011 hours per second.
                    float step = 0.012f * (float)delta;
                    
                    if (Mathf.Abs(diff) > 2.0f) 
                        step = 2.0f * (float)delta; // Snap faster if way out of sync
                        
                    _currentWorldHour += Mathf.Sign(diff) * step;
                    
                    if (_currentWorldHour >= 24f) _currentWorldHour -= 24f;
                    if (_currentWorldHour < 0f) _currentWorldHour += 24f;
                }
            }
            else
            {
                // Discrete ticks
                _currentWorldHour = _targetWorldHour;
            }

            ApplyTimeOfDayVisuals();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_playerCapsule == null) return;

        // Tick down zone immunity timer
        if (_zoneImmunityTimer > 0)
            _zoneImmunityTimer -= delta;

        // --- WASD Movement & Turning ---
        float baseSpeed = 8f;
        if (_isCrouching) baseSpeed = 4f;
        else if (Input.IsPhysicalKeyPressed(Key.Shift) || _isAutoRunning) baseSpeed = 40f;
        
        float speed = baseSpeed;
        if (_flyMode) speed *= 5f; // Admin fly mode gets 5x speed
        float gravity = 50f;
        float jumpVelocity = 15f;
        Vector3 velocity = _playerCapsule.Velocity;
        bool rightClickHeld = Input.IsMouseButtonPressed(MouseButton.Right);
        
        // Handle Gravity
        if (!_flyMode && !_playerCapsule.IsOnFloor())
        {
            velocity.Y -= gravity * (float)delta;
        }

        // Handle Jump
        if (Input.IsActionJustPressed("ui_accept") && _playerCapsule.IsOnFloor() && !_flyMode)
        {
            velocity.Y = jumpVelocity;
            PlayFootstep("jmp");
        }
        
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
            // Fly mode: Space = up, C = down, no gravity
            velocity.Y = 0;
            if (Input.IsPhysicalKeyPressed(Key.Space)) velocity.Y = speed;
            if (Input.IsPhysicalKeyPressed(Key.C)) velocity.Y = -speed;
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
                // Jumping cancels crouch/sneak/hide
                if (_isCrouching)
                {
                    _isCrouching = false;
                    _isSneaking = false;
                    _playerCapsule.SetSneakState(false, true);
                    EmitSignal(SignalName.SneakToggled, false);
                    if (_isHiding)
                    {
                        _isHiding = false;
                        EmitSignal(SignalName.HideToggled, false);
                    }
                }
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
            isSprinting && !_isCrouching,
            _isCrouching
        );

        // ── Footstep Sounds ──
        UpdateFootstepSounds(_playerCapsule.Velocity, _playerCapsule.IsOnFloor(), isSprinting, delta);

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
            GD.Print($"[WORLD] Fly mode: {(_flyMode ? "ON (noclip)" : "OFF")}");
            
            // Toggle collision — noclip while flying for easy environment inspection
            if (_playerCapsule != null)
            {
                _playerCapsule.CollisionLayer = _flyMode ? 0u : 1u;
                _playerCapsule.CollisionMask = _flyMode ? 0u : 1u;
            }
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

        // F4: Admin Succor — teleport to zone safe point via server
        if (Input.IsPhysicalKeyPressed(Key.F4) && !_f4Held)
        {
            _f4Held = true;
            GD.Print("[WORLD] Admin Succor requested (F4)");
            var client = GetNodeOrNull<GameClient>("/root/GameClient");
            if (client != null)
                client.SendRaw("{\"type\": \"SUCCOR\"}");
        }
        if (!Input.IsPhysicalKeyPressed(Key.F4)) _f4Held = false;

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
            // Toggle Crouch / Sneak
            if (keyEvent.Keycode == Key.Ctrl)
            {
                _isCrouching = !_isCrouching;

                // Tie into sneak
                _isSneaking = _isCrouching;
                _playerCapsule.SetSneakState(_isSneaking, true);
                EmitSignal(SignalName.SneakToggled, _isSneaking);

                // If entering crouch, also attempt Hide
                if (_isCrouching)
                {
                    _isHiding = true;
                    EmitSignal(SignalName.HideToggled, true);
                }
                else
                {
                    if (_isHiding)
                    {
                        _isHiding = false;
                        EmitSignal(SignalName.HideToggled, false);
                    }
                }
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

    /// <summary>
    /// Called by MainUI when the server rejects a sneak/hide attempt or breaks stealth.
    /// Only clears the skill state flags — does NOT cancel the crouch animation.
    /// Crouching is a visual/movement state and is independent of the sneak skill.
    /// </summary>
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

    /// <summary>Clears the current target (called by ESC key).</summary>
    public void ClearTarget()
    {
        SetTarget(null);
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
            if (child != _playerCapsule && GodotObject.IsInstanceValid(child))
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

    public void SpawnEntityAt(string id, string name, string type, Vector3 pos, string appearanceJson = "", int race = 1, int gender = 0, int face = 0, string equipVisualsJson = "")
    {
        if (_activeEntities.ContainsKey(id)) return;

        var instance = new EntityCapsule();
        instance.Position = pos;
        instance.Name = id;

        _spawnsContainer.AddChild(instance);
        _activeEntities[id] = instance;

        try {
            instance.Setup(name, type, appearanceJson, race, gender, face, equipVisualsJson);
            // Apply current vision effects
            if (_currentVisionStyle == "infravision")
                instance.SetInfravision(true);
        } catch (Exception ex) {
            GD.PrintErr($"[WORLD] Failed Setup on '{name}': {ex.Message}");
        }
    }

    public void SyncLiveMobs(System.Text.Json.JsonElement entitiesArray)
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
            string equipVis = ent.TryGetProperty("equipVisuals", out var evProp) ? evProp.ToString() : "";

            incomingIds.Add(id);

            if (!existingIds.Contains(id))
            {
                // GD.Print($"[WORLD] Spawning '{name}' (race={race} gender={gender} face={face}) at server coords: {rawX}, {rawY}, {rawZ}");
                // Use the Godot-mapped coordinates (x, y, z) calculated above
                SpawnEntityAt(id, name, type, new Vector3(x, y, z), appearance, race, gender, face, equipVis);
            }
            
            UpdateEntitySneak(id, sneaking);
            UpdateEntityHide(id, hidden);
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

    public void UpdateEntityHide(string id, bool hidden)
    {
        if (_activeEntities.TryGetValue(id, out Node3D entity) && entity is EntityCapsule ec)
        {
            ec.SetHideState(hidden, false);
        }
    }

    /// <summary>Show or hide the player's torch/light source OmniLight3D.</summary>
    public void SetPlayerLightSource(bool hasLight)
    {
        if (_playerCapsule != null)
            _playerCapsule.SetLightSource(hasLight);
    }

    /// <summary>Show or hide the player's overhead name label.</summary>
    public void SetPlayerNameVisible(bool visible)
    {
        if (_playerCapsule != null)
            _playerCapsule.SetNameVisible(visible);
    }

    /// <summary>Toggle dynamic shadows for object-attached lights (torches, braziers).</summary>
    public void SetDynamicShadows(bool enabled)
    {
        DynamicShadowsEnabled = enabled;
        
        if (_objectPlacer != null)
            _objectPlacer.ShadowsEnabled = enabled;
            
        // Recursively find all OmniLight3D nodes in the zone objects container and update them
        if (_zoneObjectsContainer != null)
        {
            UpdateShadowsRecursive(_zoneObjectsContainer, enabled);
        }
    }

    private void UpdateShadowsRecursive(Node node, bool shadowsEnabled)
    {
        if (node is OmniLight3D light && light.Name != null && !light.Name.ToString().StartsWith("Light_")) 
        {
            // Only toggle shadows on dynamic lights attached to objects, NOT the static baked ones ("Light_XX")
            light.ShadowEnabled = shadowsEnabled;
        }

        foreach (Node child in node.GetChildren())
        {
            UpdateShadowsRecursive(child, shadowsEnabled);
        }
    }

    /// <summary>Update the player's 3D equipment visuals (weapons, armor textures).</summary>
    public void UpdatePlayerEquipVisuals(string equipVisualsJson)
    {
        if (_playerCapsule != null)
            _playerCapsule.UpdateEquipVisuals(equipVisualsJson);
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

            if (GodotObject.IsInstanceValid(entity))
                entity.QueueFree();
            _activeEntities.Remove(id);
        }
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
                _boundariesContainer.RemoveChild(child);
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
        }
        
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

    /// <summary>
    /// Loads a zone GLB from an absolute filesystem path (extracted from player's EQ install).
    /// Uses GltfDocument.AppendFromFile for direct disk loading.
    /// LanternExtractor GLBs are already in EQ coordinate space — no rotation needed.
    /// </summary>
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

            // Fix Materials: Strip Unshaded and apply proper roughness so clustered lighting works.
            FixZoneMaterials(zoneRoot);

            // Generate collision for walkable geometry
            int meshCount = 0;
            int collisionCount = 0;
            GenerateCollisionRecursive(scene, ref meshCount, ref collisionCount);

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

    /// <summary>
    /// Place zone objects (trees, buildings, torches, etc.) from extracted data.
    /// </summary>
    private void PlaceZoneObjects(string zoneId, string cachePath)
    {
        _objectPlacer ??= new ZoneObjectPlacer();
        _objectPlacer.ShadowsEnabled = DynamicShadowsEnabled;

        if (_zoneGeometryContainer == null) return;

        _zoneObjectsContainer = _objectPlacer.PlaceObjects(zoneId, cachePath, _zoneGeometryContainer);
        _objectPlacer.PlaceLights(zoneId, cachePath, _zoneGeometryContainer);
    }

    /// <summary>
    /// Start playing zone-appropriate background music.
    /// </summary>
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
    /// Recursively strips Unshaded flags and sets roughness for proper lighting.
    /// </summary>
    private void FixZoneMaterials(Node node)
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
                    mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled; 
                    
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
                        newMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                        
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
            FixZoneMaterials(child);
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
    
    // Player appearance (set before spawning)
    private int _playerRace = 1;
    private int _playerGender = 0;
    private int _playerFace = 0;
    private string _playerEquipVisuals = "";

    public void SetPlayerAppearance(int race, int gender, int face, string equipVisualsJson = "")
    {
        _playerRace = race;
        _playerGender = gender;
        _playerFace = face;
        _playerEquipVisuals = equipVisualsJson;
    }

    public void SpawnPlayer(float rawX, float rawY)
    {
        if (_playerCapsule != null) return;

        // Map EQ Coords (+X=West, +Y=North) to Godot Coords (-X=West, -Z=North)
        float x = -rawX;
        float z = -rawY;

        GD.Print($"[WORLD] Spawning Player at {x}, {z} (race={_playerRace} gender={_playerGender} face={_playerFace})");

        // Raycast downward from high above to find terrain surface
        float spawnHeight = 2.0f; // Fallback
        var spaceState = GetWorld3D()?.DirectSpaceState;
        if (spaceState != null)
        {
            var rayFrom = new Vector3(x, 500f, z);
            var rayTo = new Vector3(x, -500f, z);
            var query = PhysicsRayQueryParameters3D.Create(rayFrom, rayTo);
            query.CollideWithBodies = true;
            query.CollideWithAreas = false;
            var result = spaceState.IntersectRay(query);
            if (result.Count > 0)
            {
                Vector3 hitPos = (Vector3)result["position"];
                spawnHeight = hitPos.Y + 10.0f; // Spawn well above terrain, gravity handles landing
                // GD.Print($"[WORLD] Raycast terrain hit at Y={hitPos.Y:F1}, spawning at Y={spawnHeight:F1}");
            }
            else
            {
                GD.Print($"[WORLD] No terrain hit at ({x:F1}, {z:F1}), using raw height {spawnHeight:F1}");
            }
        }

        _playerCapsule = new EntityCapsule();
        _playerCapsule.Name = "Player";
        _playerCapsule.Position = new Vector3(x, spawnHeight, z);
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
        // Map EQ Coords to Godot Coords
        float x = -rawX;
        float z = -rawY;
        float y = rawZ; // EQ Z (height) → Godot Y

        if (_playerCapsule == null)
        {
            SpawnPlayer(rawX, rawY);
        }

        // Start with the target height from zone_points, with a small bump
        float spawnHeight = (y != 0f) ? y + 2.0f : 3.0f;

        // Raycast downward from well above the target position to find the actual terrain surface.
        // This is critical for GLB-loaded zones where the mesh height may differ from DB target_z.
        var spaceState = GetWorld3D()?.DirectSpaceState;
        if (spaceState != null)
        {
            float rayStartY = spawnHeight + 500f; // Cast from high above
            var rayFrom = new Vector3(x, rayStartY, z);
            var rayTo = new Vector3(x, spawnHeight - 500f, z); // Cast far below too
            var query = PhysicsRayQueryParameters3D.Create(rayFrom, rayTo);
            query.CollideWithBodies = true;
            query.CollideWithAreas = false;
            // Exclude the player capsule from the raycast
            if (_playerCapsule != null)
            {
                query.Exclude = new Godot.Collections.Array<Rid> { _playerCapsule.GetRid() };
            }

            var result = spaceState.IntersectRay(query);
            if (result.Count > 0)
            {
                Vector3 hitPos = (Vector3)result["position"];
                spawnHeight = hitPos.Y + 10.0f; // Spawn well above terrain, gravity handles landing
                // GD.Print($"[WORLD] Raycast terrain hit at Y={hitPos.Y:F1}, spawning at Y={spawnHeight:F1}");
            }
            else
            {
                GD.Print($"[WORLD] No terrain hit at ({x:F1}, {z:F1}), using raw height {spawnHeight:F1}");
            }
        }

        _playerCapsule.GlobalPosition = new Vector3(x, spawnHeight, z);
        _playerCapsule.Velocity = Vector3.Zero;
        _lastSentPos = new Vector3(x, spawnHeight, z);
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
    }

    public void SetPlayerCasting(bool casting)
    {
        if (_playerCapsule == null) return;
        if (casting)
            _playerCapsule.PlayAnimation("t04"); // casting animation
        else
            _playerCapsule.PlayAnimation("p01"); // return to idle
    }

    /// <summary>Play a named animation on the player capsule (for emotes like wave s03).</summary>
    public void PlayPlayerAnimation(string animName)
    {
        if (_playerCapsule == null || string.IsNullOrEmpty(animName)) return;
        // Use PlayEmote to set the emote timer, preventing idle from overriding
        _playerCapsule.PlayEmote(animName);
    }

    // ── Footstep Sound System ───────────────────────────────────

    private void UpdateFootstepSounds(Vector3 vel, bool onFloor, bool sprinting, double delta)
    {
        // Determine movement state
        float horizSpeed = new Vector2(vel.X, vel.Z).Length();
        string newState;

        if (!onFloor && vel.Y < -2f)
            newState = "idle"; // Falling — no footsteps
        else if (horizSpeed < 0.5f)
            newState = "idle";
        else if (sprinting)
            newState = "run";
        else
            newState = "walk";

        // Create footstep player on first use
        if (_footstepPlayer == null && _playerCapsule != null)
        {
            _footstepPlayer = new AudioStreamPlayer();
            _footstepPlayer.Name = "FootstepPlayer";
            _playerCapsule.AddChild(_footstepPlayer);
        }

        // State change — reset timer
        if (newState != _lastFootstepState)
        {
            GD.Print($"[Footstep] State changed from '{_lastFootstepState}' to '{newState}' (speed={horizSpeed:F2}, onFloor={onFloor})");
            _lastFootstepState = newState;
            _footstepTimer = 0; // Play immediately on transition

            if (newState == "idle" && _footstepPlayer != null)
            {
                _footstepPlayer.Stop();
            }
        }

        if (newState == "idle") return;

        // Cadence: run = faster steps, walk = slower
        _footstepCadence = newState == "run" ? 0.32 : 0.50;

        _footstepTimer -= delta;
        if (_footstepTimer <= 0)
        {
            _footstepTimer = _footstepCadence;
            PlayFootstep(newState);
        }
    }

    private void PlayFootstep(string moveType)
    {
        if (_playerCapsule == null) return;

        // Get the player's model code for race-specific sounds
        string modelCode = _playerCapsule.GetModelCode();
        if (string.IsNullOrEmpty(modelCode)) modelCode = "hum";

        // Map moveType to sound suffix (EQ naming: hum_run.wav, hum_wlk.wav, hum_swm.wav, hum_atk.wav)
        string suffix = moveType switch
        {
            "run" => "_run.wav",
            "walk" => "_wlk.wav",
            "swim" => "_swm.wav",
            "jmp" => "_atk.wav", // Jump uses the vocal attack grunt
            _ => "_wlk.wav"
        };

        // Try race-specific first, then human fallback
        string soundFile = $"{modelCode}{suffix}";
        var stream = EQAssetCache.Instance.GetSound(soundFile);

        if (stream == null)
        {
            // Fallback: try 3-letter code
            string shortCode = modelCode.Substring(0, System.Math.Min(3, modelCode.Length));
            soundFile = $"{shortCode}{suffix}";
            stream = EQAssetCache.Instance.GetSound(soundFile);
        }

        if (stream == null)
        {
            // Final fallback: Use high-quality Dark Elf Male/Female audio as the universal humanoid default
            string dkPrefix = _playerCapsule.Gender == 1 ? "DKF_" : "DKM_";
            string dkSuffix = moveType switch
            {
                "run" => "Run.wav",
                "walk" => "Walk.wav",
                "swim" => "SwimFoward.wav",
                "jmp" => "Jump.wav",
                _ => "Walk.wav"
            };
            soundFile = $"{dkPrefix}{dkSuffix}";
            stream = EQAssetCache.Instance.GetSound(soundFile);
        }

        if (stream != null)
        {
            // Use the global UI sound player for local player footsteps/jumps
            // since we know its audio pipeline is 100% functional and local sounds
            // don't need 3D spatial attenuation.
            float sfxVol = _musicPlayer?.GetSfxVolume() ?? 0.8f;
            bool sfxMuted = _musicPlayer?.IsSfxMuted ?? false;
            
            if (!sfxMuted && sfxVol > 0f)
            {
                float finalVol = 2f; // Base volume (+2dB)
                
                // Silent if successfully sneaking or hiding
                if (_isSneaking || _isHiding)
                {
                    finalVol = -80f; // Godot's minimum dB (silent)
                }
                else if (_isCrouching)
                {
                    // If crouched but failed sneak check (or just crouching normally)
                    if (PlayerHasStealthSkill)
                        finalVol = -10f; // Even quieter if they have the skill but failed
                    else
                        finalVol = -4f;  // Quieter for normal crouch
                }

                if (finalVol > -80f)
                {
                    UISoundPlayer.Instance?.PlaySound(soundFile, finalVol);
                }
            }
        }
        else
        {
            GD.Print($"[Footstep] No sound found for model='{modelCode}' type='{moveType}'");
        }
    }
}


