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
    public Vector3 PlayerPosition => _playerCapsule?.GlobalPosition ?? Vector3.Zero;
    private ITargetable _currentTarget;
    private Dictionary<string, Node3D> _activeEntities = new Dictionary<string, Node3D>();
    private Dictionary<string, Node3D> _spawnedDoors = new Dictionary<string, Node3D>();
    private Node3D _doorsContainer;
    private MaterialAnimator _materialAnimator;
    private string _currentVisionStyle = "normal";
    private GpuParticles3D _weatherParticles;
    private string _currentWeatherEffect = "none";

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
    
    // Bind Sight (SPA 73) — camera override
    private bool _bindSightActive = false;
    private Vector3 _bindSightPosition = Vector3.Zero;

    // Signal for UI target updates
    [Signal] public delegate void TargetChangedEventHandler(string name, string type);
    [Signal] public delegate void TargetClearedEventHandler();
    
    // Server Syncing
    [Signal] public delegate void PlayerMovedEventHandler(float x, float y, float z);
    [Signal] public delegate void ZoneLineCrossedEventHandler(string targetZoneId, float targetX, float targetY, float targetZ);
    [Signal] public delegate void SneakToggledEventHandler(bool isSneaking);
    [Signal] public delegate void HideToggledEventHandler(bool isHiding);
    [Signal] public delegate void SyncProgressEventHandler(int current, int total);
    private Vector3 _lastSentPos = Vector3.Zero;
    private double _posSyncTimer = 0.0;
    private Node3D _boundariesContainer;
    private bool _isAutoRunning = false;
    public void SetAutoRun(bool val) => _isAutoRunning = val;
    private bool _isSneaking = false;
    private bool _isHiding = false;
    public bool PlayerHasStealthSkill { get; set; } = false;
    public float PlayerSwimmingSkill { get; set; } = 0f;
    private double _swimTimer = 0;
    private bool _isCrouching = false;
    private bool _playerInCombat = false;
    private double _zoneImmunityTimer = 0.0; // Seconds of immunity after teleport
    private float _teleportFreezeTimer = 0f;
    private bool _teleportSettling = false; // Freeze player physics until terrain collision is confirmed
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
    public string CurrentTargetId => GodotObject.IsInstanceValid(_currentTarget as Node) ? ((Node)_currentTarget).Name : null;

    // Range constants (in world units)
    public const float MELEE_RANGE = 5f;
    public const float BOW_RANGE = 30f;
    public const float SPELL_RANGE_DEFAULT = 20f;

    /// <summary>Returns distance from player to current target, or -1 if no target.</summary>
    public float GetDistanceToTarget()
    {
        if (_playerCapsule == null || !GodotObject.IsInstanceValid(_currentTarget as Node)) return -1f;
        
        // Ignore Y distance for combat/interaction so flying mobs and tall mobs are hittable
        Vector3 p1 = _playerCapsule.GlobalPosition;
        Vector3 p2 = _currentTarget.GlobalPosition;
        p1.Y = 0;
        p2.Y = 0;
        
        return p1.DistanceTo(p2);
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
        
        _doorsContainer = new Node3D { Name = "Doors" };
        AddChild(_doorsContainer);

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
        _camera.Far = 50000f; // Ensure celestial bodies and far terrain are visible
        _camera.Near = 0.5f; // Improve depth precision for the larger far plane
        
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
        _moonSprite.PixelSize = 5.0f; // Large scale for the sky at distance
        _moonSprite.Position = new Vector3(0, 0, -15000f); // Positioned far in the world space
        _moon.AddChild(_moonSprite);

        // Load Moon Phase Shader
        _moonMaterial = new ShaderMaterial();
        _moonMaterial.Shader = GD.Load<Shader>("res://Assets/Shaders/MoonPhase.gdshader");
        _moonMaterial.SetShaderParameter("moon_texture", _moonSprite.Texture);
        _moonSprite.MaterialOverride = _moonMaterial;

        // Initialize Audio Subsystems
        _musicPlayer = new ZoneMusicPlayer();
        AddChild(_musicPlayer);

        GD.Print("[WORLD] 3D World Initialized");

        // Player is now spawned lazily via SpawnPlayer() after the world is ready
        UpdateCamera();
    }

    public float SpeedModifier { get; set; } = 1.0f;

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
        string cleanName = name.Replace("_", " ");
        foreach (var entity in _activeEntities.Values)
        {
            if (entity is EntityCapsule cap && (cap.EntityName == name || cap.EntityName == cleanName))
                return cap;
        }
        return null;
    }

    public EntityCapsule TargetEntityByPartialName(string partialName)
    {
        if (string.IsNullOrWhiteSpace(partialName)) return null;
        string lower = partialName.ToLower();
        
        EntityCapsule closest = null;
        float closestDist = float.MaxValue;
        
        foreach (var entity in _activeEntities.Values)
        {
            if (entity is EntityCapsule cap && cap != _playerCapsule)
            {
                if (cap.EntityName.ToLower().Contains(lower) || cap.Name.ToString().ToLower().Contains(lower))
                {
                    float dist = _playerCapsule.GlobalPosition.DistanceTo(cap.GlobalPosition);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = cap;
                    }
                }
            }
        }
        
        if (closest != null)
        {
            SetTarget(closest);
        }
        return closest;
    }

    public EntityCapsule GetEntityById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id == "You" || id == "player_self" || (_playerCapsule != null && id == $"player_{_playerCapsule.Name}")) 
            return _playerCapsule;
        if (_activeEntities.TryGetValue(id, out Node3D entity) && entity is EntityCapsule cap)
            return cap;
        return null;
    }

    public void TriggerEntityAction(string name, string action)
    {
        var entity = GetEntityByName(name);
        if (entity != null)
        {
            TriggerEntityActionInternal(entity, action);
        }
    }

    public void TriggerEntityActionById(string id, string action)
    {
        var entity = GetEntityById(id);
        if (entity != null)
        {
            TriggerEntityActionInternal(entity, action);
        }
    }

    public void ReloadLightTuning()
    {
        if (_objectPlacer != null)
        {
            _objectPlacer.ReloadLightConfig();
        }
    }

    public void SetEntityAsLightModel(string entityId, string modelName)
    {
        var capsule = GetEntityById(entityId);
        if (capsule != null && _objectPlacer != null)
        {
            string objectsDir = $"{EQAssetCache.Instance.CacheRoot}/zones/{_currentZoneId.ToLower()}/Objects";
            var scene = _objectPlacer.GetObjectScene(modelName, objectsDir);
            
            if (scene != null)
            {
                // Clear existing visual
                var oldMesh = capsule.GetNodeOrNull<Node3D>("Mesh");
                if (oldMesh != null)
                {
                    capsule.RemoveChild(oldMesh);
                    oldMesh.QueueFree();
                }

                // Add the new model
                var instance = scene.Instantiate<Node3D>();
                instance.Name = "Mesh";
                capsule.AddChild(instance);
                
                // Clear existing light if we didn't just remove it with the old mesh
                var oldLight = capsule.GetNodeOrNull<OmniLight3D>("TunerLight");
                if (oldLight != null)
                {
                    capsule.RemoveChild(oldLight);
                    oldLight.QueueFree();
                }
                
                // The light requires a parent container, we pass the capsule as container
                _objectPlacer.AddLightIfSource(instance, modelName, capsule, capsule.Position.X, capsule.Position.Y, capsule.Position.Z);
            }
            else
            {
                GD.Print($"[WorldManager] Failed to load light model {modelName} for Tuner.");
            }
        }
    }

    private void TriggerEntityActionInternal(EntityCapsule entity, string action)
    {
        if (action == "hit") entity.PlayDamage(false);
        else if (action == "hit_heavy") entity.PlayDamage(true);
        else if (action == "miss") entity.PlaySfx("aam_hit.wav"); // Placeholder for miss/whoosh sound
        else if (action == "cast") entity.PlayEmote("t04");
        else if (action.StartsWith("cast:"))
        {
            if (int.TryParse(action.Split(':')[1], out int castType))
            {
                string animCode = castType switch { 1 => "t04", 2 => "t05", 3 => "t06", _ => "t04" };
                entity.PlayEmote(animCode);
            }
            else
                entity.PlayEmote("t04");
        }
        else if (action == "die") entity.PlayDeath();
        else if (action == "fizzle") entity.PlaySfx("fizzle.wav");
        else if (action.StartsWith("attack:"))
        {
            string type = action.Split(':')[1];
            entity.PlayAttack(type);
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
        // 0 (Midnight): Sun is straight UP from under ground (90 degrees)
        // 6 (Dawn): Sun is at horizon (0 degrees)
        // 12 (Noon): Sun is straight DOWN from sky (-90 degrees)
        // 18 (Dusk): Sun is at horizon (-180 degrees)
        
        float sunPitchDegrees = 90f - (progress * 360f); 
        _sun.RotationDegrees = new Vector3(sunPitchDegrees, -45f, 0f);
        
        // Moon is opposite of sun
        float moonPitchDegrees = sunPitchDegrees - 180f;
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
                Color currentTop = nightTopColor.Lerp(dayTopColor, sunIntensity);
                Color currentHorizon = nightHorizonColor.Lerp(dayHorizonColor, sunIntensity);
                skyMat.SkyTopColor = currentTop;
                skyMat.SkyHorizonColor = currentHorizon;
                
                if (_environment != null && _environment.Environment != null)
                {
                    _environment.Environment.FogEnabled = true;
                    _environment.Environment.FogMode = Godot.Environment.FogModeEnum.Exponential;
                    _environment.Environment.FogDensity = 0.001f; // Classic depth haze, heavily reduced for daytime visibility
                    _environment.Environment.FogLightColor = currentHorizon;
                    _environment.Environment.FogSunScatter = 0.1f;
                }
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
                
                if (_environment != null && _environment.Environment != null)
                {
                    _environment.Environment.FogEnabled = true;
                    _environment.Environment.FogMode = Godot.Environment.FogModeEnum.Exponential;
                    _environment.Environment.FogDensity = 0.003f; // Slightly thicker at night
                    _environment.Environment.FogLightColor = nightHorizonColor;
                    _environment.Environment.FogSunScatter = 0.0f;
                }
            }
        }
    }

    public void SetWeatherEffect(string effect)
    {
        if (_currentWeatherEffect == effect) return;
        _currentWeatherEffect = effect;

        if (_weatherParticles == null)
        {
            _weatherParticles = new GpuParticles3D();
            _weatherParticles.Name = "WeatherParticles";
            _weatherParticles.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            _weatherParticles.Emitting = false;
            
            // Attach to camera so it follows player view
            if (_camera != null)
            {
                _camera.AddChild(_weatherParticles);
            }
            else
            {
                AddChild(_weatherParticles);
            }
        }

        if (effect == "none" || effect == "overcast" || effect == "fog" || string.IsNullOrEmpty(effect))
        {
            _weatherParticles.Emitting = false;
            return;
        }

        _weatherParticles.Emitting = true;
        _weatherParticles.Position = new Vector3(0, 15f, -10f); // Default above
        _weatherParticles.Amount = 1000;
        _weatherParticles.Lifetime = 2.0f;
        _weatherParticles.VisibilityAabb = new Aabb(new Vector3(-40, -40, -40), new Vector3(80, 80, 80));

        var material = new ParticleProcessMaterial();
        material.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        material.EmissionBoxExtents = new Vector3(25, 2, 25); // spawn over a wide area above camera

        var drawPass = new QuadMesh();
        drawPass.Size = new Vector2(0.05f, 0.5f); // Rain drop shape
        
        var spatialMat = new StandardMaterial3D();
        spatialMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        spatialMat.AlbedoColor = new Color(0.8f, 0.9f, 1.0f, 0.6f);
        spatialMat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        spatialMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;

        if (effect.Contains("rain"))
        {
            material.Direction = new Vector3(0, -1, 0);
            material.Spread = 2f;
            material.InitialVelocityMin = 25f;
            material.InitialVelocityMax = 35f;
            material.Gravity = new Vector3(0, -9.8f, 0);
            
            if (effect == "rain_light") { _weatherParticles.Amount = 500; }
            else if (effect == "rain_heavy") 
            { 
                _weatherParticles.Amount = 3000; 
                material.InitialVelocityMin = 35f; 
                material.InitialVelocityMax = 45f; 
                material.Direction = new Vector3(0.1f, -1f, 0f); 
            }
        }
        else if (effect.Contains("snow") || effect == "blizzard")
        {
            drawPass.Size = new Vector2(0.08f, 0.08f); // Snow flake shape
            spatialMat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 0.8f);
            
            material.Direction = new Vector3(0, -1, 0);
            material.Spread = 10f;
            material.InitialVelocityMin = 3f;
            material.InitialVelocityMax = 6f;
            
            // Add some drift/swirl
            material.Gravity = new Vector3(1f, -2f, 1f); 
            
            if (effect == "snow_light") { _weatherParticles.Amount = 500; _weatherParticles.Lifetime = 3.0f; }
            else if (effect == "snow") { _weatherParticles.Amount = 2000; _weatherParticles.Lifetime = 3.0f; }
            else if (effect == "blizzard") 
            { 
                _weatherParticles.Amount = 5000; 
                _weatherParticles.Lifetime = 3.0f;
                material.InitialVelocityMin = 15f; 
                material.InitialVelocityMax = 25f; 
                material.Direction = new Vector3(0.5f, -1f, 0f); 
                material.Gravity = new Vector3(5f, -5f, 0f);
            }
        }
        else if (effect == "dust")
        {
            drawPass.Size = new Vector2(0.2f, 0.2f);
            spatialMat.AlbedoColor = new Color(0.8f, 0.7f, 0.5f, 0.2f);
            
            material.Direction = new Vector3(1, 0, 0); // Blow horizontally
            material.Spread = 15f;
            material.InitialVelocityMin = 15f;
            material.InitialVelocityMax = 25f;
            material.Gravity = new Vector3(0, 0, 0);
            
            _weatherParticles.Amount = 3000;
            _weatherParticles.Position = new Vector3(-25, 0, -10f); // Spawning from side instead of above
            material.EmissionBoxExtents = new Vector3(2, 10, 25); 
        }

        drawPass.Material = spatialMat;
        _weatherParticles.ProcessMaterial = material;
        _weatherParticles.DrawPass1 = drawPass;
    }

    public override void _Process(double delta)
    {
        // Celestial bodies should be anchored to the camera to prevent parallax and clipping,
        // but their rotation makes them orbit the world.
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

        // While teleport is settling, freeze the player at their placed height.
        // This prevents gravity from pulling them through the map before
        // zone geometry collision shapes are registered in the physics engine.
        if (_teleportSettling && _playerCapsule != null)
        {
            _teleportFreezeTimer -= (float)delta;
            if (_teleportFreezeTimer <= 0)
            {
                _teleportSettling = false;
                GD.Print("[WORLD] Teleport settle complete — releasing player.");
            }
            else
            {
                _playerCapsule.Velocity = Vector3.Zero;
                return; // Skip all movement/gravity until terrain is confirmed
            }
        }

        var velocity = _playerCapsule.Velocity;
        float baseSpeed = 10.0f * SpeedModifier;
        if (_isCrouching) baseSpeed = 5.0f * SpeedModifier;

        float speed = baseSpeed;

        // Apply Swimming speed multiplier
        if (_playerCapsule.IsInWater)
        {
            speed *= (1.0f + (PlayerSwimmingSkill / 200f) * 0.5f);
        }
        else if (Input.IsPhysicalKeyPressed(Key.Shift) || _isAutoRunning)
        {
            speed = baseSpeed * 2.15f; // Sprint multiplier
        }

        bool rightClickHeld = Input.IsMouseButtonPressed(MouseButton.Right);
        if (_flyMode) speed *= 5f; // Admin fly mode gets 5x speed
        float gravity = 50f;

        bool isTyping = MainUI.Instance != null && MainUI.Instance.IsChatFocused;
        
        // Manual movement cancels autorun
        if (!isTyping && (Input.IsActionPressed("move_forward") || Input.IsActionPressed("move_back"))) {
            _isAutoRunning = false;
        }

        if (CurrentCameraMode == CameraMode.Drive)
        {
            // --- Drive Mode ---
            if (rightClickHeld)
            {
                // Right-Click Held: Strafe behavior
                Vector3 inputDir = Vector3.Zero;
                if (!isTyping)
                {
                    if (Input.IsActionPressed("move_forward")) inputDir.Z -= 1;
                    if (Input.IsActionPressed("move_back"))    inputDir.Z += 1;
                    if (Input.IsActionPressed("move_left"))    inputDir.X -= 1;
                    if (Input.IsActionPressed("move_right"))   inputDir.X += 1;
                }

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
                if (!isTyping)
                {
                    if (Input.IsActionPressed("move_left")) turnAmount += 1;
                    if (Input.IsActionPressed("move_right")) turnAmount -= 1;
                }

                float turnSpeed = 3f;
                _playerCapsule.RotateY(turnAmount * turnSpeed * (float)delta);

                float forwardInput = 0;
                if (_isAutoRunning) forwardInput += 1;
                if (!isTyping)
                {
                    if (Input.IsActionPressed("move_forward")) forwardInput += 1;
                    if (Input.IsActionPressed("move_back"))    forwardInput -= 1;
                }

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
            if (!isTyping)
            {
                if (Input.IsActionPressed("move_forward")) inputDir.Z -= 1;
                if (Input.IsActionPressed("move_back"))    inputDir.Z += 1;
                if (Input.IsActionPressed("move_left"))    inputDir.X -= 1;
                if (Input.IsActionPressed("move_right"))   inputDir.X += 1;
            }

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
            if (!isTyping)
            {
                if (Input.IsPhysicalKeyPressed(Key.Space)) velocity.Y = speed;
                if (Input.IsPhysicalKeyPressed(Key.C) || Input.IsPhysicalKeyPressed(Key.Ctrl)) velocity.Y = -speed;
            }
        }
        else if (_playerCapsule.IsInWater)
        {
            // Water mode: Space = up, Ctrl = down, neutral buoyancy
            velocity.Y = 0; // neutral buoyancy
            bool isMovingInWater = false;
            
            if (!isTyping)
            {
                if (Input.IsPhysicalKeyPressed(Key.Space)) { velocity.Y = speed; isMovingInWater = true; }
                if (Input.IsPhysicalKeyPressed(Key.Ctrl)) { velocity.Y = -speed; isMovingInWater = true; }
            }
            
            if (velocity.X != 0 || velocity.Z != 0) isMovingInWater = true;
            
            // Skill up ticks
            if (isMovingInWater)
            {
                _swimTimer += delta;
                if (_swimTimer >= 3.0)
                {
                    _swimTimer = 0;
                    MainUI.Instance?.GetClient()?.SendRaw("{\"type\": \"SWIM_TICK\"}");
                }
            }
            else
            {
                _swimTimer = 0;
                // Slowly sink if not moving (optional EQ mechanic, disabled for pure neutral)
            }
        }
        else if (!_playerCapsule.IsOnFloor())
        {
            velocity.Y -= gravity * (float)delta;
        }
        else
        {
            velocity.Y = 0;
            if (!isTyping && Input.IsPhysicalKeyPressed(Key.Space))
            {
                velocity.Y = 30.0f; // Jump force
                PlayFootstep("jmp");
                
                var client = GetNodeOrNull<GameClient>("/root/GameClient");
                if (client != null) client.SendRaw("{\"type\": \"JUMP\"}");
                
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

        // Sync position to server if moved > 0.1 units, throttled to 1 update per second
        _posSyncTimer += delta;
        if (_posSyncTimer >= 1.0)
        {
            if (_playerCapsule.GlobalPosition.DistanceTo(_lastSentPos) > 0.1f)
            {
                _lastSentPos = _playerCapsule.GlobalPosition;
                EmitSignal(SignalName.PlayerMoved, _lastSentPos.X, _lastSentPos.Y, _lastSentPos.Z);
            }
            _posSyncTimer = 0.0;
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
        if (@event is InputEventMouseButton mouseBtnEvent && mouseBtnEvent.Pressed)
        {
            // Clicking anywhere in the 3D world releases chat focus
            MainUI.Instance?.ReleaseChatFocus();

            // Manual raycast to bypass Godot's flaky viewport picking signals
            if (_camera != null)
            {
                var spaceState = GetWorld3D().DirectSpaceState;
                var mousePos = mouseBtnEvent.Position;
                var rayOrigin = _camera.ProjectRayOrigin(mousePos);
                var rayEnd = rayOrigin + _camera.ProjectRayNormal(mousePos) * 2000f;

                var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
                query.CollideWithAreas = true;
                query.CollideWithBodies = true;

                var result = spaceState.IntersectRay(query);
                if (result.Count > 0)
                {
                    var collider = result["collider"].As<Node>();
                    
                    // Walk up the tree to find an interactable entity
                    var current = collider;
                    while (current != null && current != this)
                    {
                        if (current is DoorEntity door)
                        {
                            door.OnInputEvent(_camera, mouseBtnEvent, Vector3.Zero, Vector3.Zero, 0);
                            break; // Stop propagating once handled
                        }
                        else if (current is EntityCapsule capsule && current != _playerCapsule)
                        {
                            // Trigger target for entity
                            if (mouseBtnEvent.ButtonIndex == MouseButton.Left)
                            {
                                SetTarget(capsule);

                                // Check if holding an item to initiate trade
                                var heldItem = MainUI.Instance?.GetHeldItem();
                                if (heldItem.HasValue)
                                {
                                    string npcId = capsule.Name.ToString().Replace("mob_", "");
                                    MainUI.Instance?.OpenGiveNPCWindow(npcId, capsule.EntityName);
                                }
                            }
                            else if (mouseBtnEvent.ButtonIndex == MouseButton.Right)
                            {
                                SetTarget(capsule);
                                var client = GetNodeOrNull<GameClient>("/root/GameClient");
                                if (client != null)
                                {
                                    var dict = new { targetId = capsule.Name.ToString() };
                                    client.SendMessage("RIGHT_CLICK", dict);
                                }
                            }
                            break;
                        }
                        current = current.GetParent();
                    }
                }
            }
        }

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
                _cameraDistance = Mathf.Max(0f, _cameraDistance - 1f);
            else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
                _cameraDistance = Mathf.Min(25f, _cameraDistance + 1f);
        }
    }

    // --- Targeting System ---


    /// <summary>
    /// Called by MainUI when the server rejects a sneak/hide attempt or breaks stealth.
    /// Only clears the skill state flags — does NOT cancel the crouch animation.
    /// Crouching is a visual/movement state and is independent of the sneak skill.
    /// </summary>


    public void SetTarget(ITargetable target)
    {
        // Deselect old
        if (_currentTarget != null && GodotObject.IsInstanceValid(_currentTarget as Node))
            _currentTarget.SetTargeted(false);

        _currentTarget = target;

        if (_currentTarget != null && GodotObject.IsInstanceValid(_currentTarget as Node))
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

        bool isFirstPerson = _cameraDistance < 0.5f;
        float heightOffset = isFirstPerson ? _playerCapsule.EyeHeight : _playerCapsule.OverheadHeight;

        if (_bindSightActive)
        {
            // Bind Sight: camera tracks the scouted target instead of the player
            _cameraArm.GlobalPosition = _cameraArm.GlobalPosition.Lerp(_bindSightPosition, 0.15f);
        }
        else
        {
            _cameraArm.GlobalPosition = _playerCapsule.GlobalPosition + new Vector3(0, heightOffset, 0);
        }
        _cameraArm.Rotation = new Vector3(
            Mathf.DegToRad(_cameraPitch),
            Mathf.DegToRad(_cameraYaw),
            0
        );
        _cameraArm.SpringLength = _cameraDistance;
        _camera.Position = Vector3.Zero;

        _playerCapsule.SetFirstPersonMode(isFirstPerson);
    }

    // --- Entity Management ---

    /// <summary>
    /// Immediately freeze the player's physics so gravity can't pull them
    /// through the map while zone geometry loads. Released by TeleportPlayer's
    /// deferred re-snap after terrain collision is confirmed.
    /// </summary>












    /// <summary>Show or hide the player's torch/light source OmniLight3D.</summary>

    /// <summary>Show or hide the player's overhead name label.</summary>

    /// <summary>Set the 3D camera Field of View.</summary>
    public void SetCameraFOV(float fov)
    {
        if (_camera != null)
        {
            _camera.Fov = fov;
        }
    }

    /// <summary>Override camera position for Bind Sight (SPA 73).</summary>
    public void SetBindSightPosition(float x, float y, float z)
    {
        _bindSightActive = true;
        _bindSightPosition = new Vector3(x, y, z);
    }

    /// <summary>Clear Bind Sight and return camera to player.</summary>
    public void ClearBindSight()
    {
        _bindSightActive = false;
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





    /// <summary>
    /// Loads a zone GLB from an absolute filesystem path (extracted from player's EQ install).
    /// Uses GltfDocument.AppendFromFile for direct disk loading.
    /// LanternExtractor GLBs are already in EQ coordinate space — no rotation needed.
    /// </summary>

    /// <summary>
    /// Place zone objects (trees, buildings, torches, etc.) from extracted data.
    /// </summary>

    /// <summary>
    /// Start playing zone-appropriate background music.
    /// </summary>

    /// <summary>
    /// Recursively walks the scene tree and creates trimesh (ConcavePolygon) collision
    /// for every MeshInstance3D found. This gives pixel-perfect collision matching the zone geometry.
    /// </summary>

    /// <summary>
    /// Recursively strips Unshaded flags and sets roughness for proper lighting.
    /// </summary>

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


    /// <summary>
    /// Creates only invisible boundary walls and a physics-only floor (no visual green floor)
    /// since the GLB zone model provides the real visual terrain.
    /// </summary>

    /// <summary>
    /// Legacy Brewall line-based zone loading. Used as fallback when no GLB is available.
    /// </summary>

    /// <summary>
    /// Rebuilds the visual and physical floor to match the map geometry bounds.
    /// Called after loading map data so the floor covers the entire zone drawing.
    /// </summary>
    
    
    // Player appearance (set before spawning)
    private int _playerRace = 1;
    private int _playerGender = 0;
    private int _playerFace = 0;
    private string _playerEquipVisuals = "";








    /// <summary>Play a named animation on the player capsule (for emotes like wave s03).</summary>

    public void TriggerCombatAnimation(string sourceName, string type, bool isHit)
    {
        // Player attack animations are now driven by the client-side auto-attack timer
        // (StartPlayerAutoAttack/StopPlayerAutoAttack). Only NPC swings are event-driven.
        if (sourceName != "You")
        {
            // Fallback for name-based combat animation triggering (deprecated)
            foreach (var node in _activeEntities.Values)
            {
                if (node is EntityCapsule mobCapsule && mobCapsule.EntityName == sourceName)
                {
                    mobCapsule.PlayAttack(type, isHit);
                    break; 
                }
            }
        }
    }

    public void TriggerCombatAnimationById(string sourceId, string type, bool isHit)
    {
        var entity = GetEntityById(sourceId);
        if (entity != null && entity != _playerCapsule)
        {
            entity.PlayAttack(type, isHit);
        }
    }

    // --- Player Auto-Attack Animation Loop ---

    /// <summary>
    /// Start the client-side auto-attack animation loop on the player capsule.
    /// Called by MainUI when autoFight becomes true.
    /// </summary>
    public void StartPlayerAutoAttack(float delaySec, string weaponType)
    {
        if (_playerCapsule != null)
            _playerCapsule.StartAutoAttack(delaySec, weaponType);
    }

    /// <summary>
    /// Stop the client-side auto-attack animation loop on the player capsule.
    /// Called by MainUI when autoFight becomes false.
    /// </summary>
    public void StopPlayerAutoAttack()
    {
        if (_playerCapsule != null)
            _playerCapsule.StopAutoAttack();
    }

    // ── Footstep Sound System ───────────────────────────────────


    private Dictionary<string, (string[] frames, float delay)> ParseMaterialList(string file)
    {
        var data = new Dictionary<string, (string[] frames, float delay)>();
        foreach (var line in System.IO.File.ReadAllLines(file))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            var parts = line.Split(',');
            if (parts.Length >= 3)
            {
                float delay = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                if (delay > 0)
                {
                    string[] mats = parts[1].Split(':');
                    string matName = mats[0];
                    if (mats.Length > 1)
                    {
                        string[] frames = new string[mats.Length - 1];
                        Array.Copy(mats, 1, frames, 0, mats.Length - 1);
                        data[matName] = (frames, delay);
                    }
                }
            }
        }
        return data;
    }

}

