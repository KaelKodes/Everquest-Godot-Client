using Godot;
using System;
using System.Linq;
using System.Text.Json;

public partial class EntityCapsule : CharacterBody3D, ITargetable
{
    private Label3D _nameLabel;
    private MeshInstance3D _mesh;
    private MeshInstance3D _targetRing;
    private MeshInstance3D _facingArrow;
    private AnimationPlayer _animPlayer;
    private string _currentAnim = "";
    private readonly System.Collections.Generic.HashSet<string> _warnedAnims = new();
    private static readonly System.Collections.Generic.HashSet<int> _warnedRaces = new();

    // Torch / Light Source
    private OmniLight3D _torchLight;
    private bool _hasLightSource = false;
    private float _torchFlickerTimer = 0f;
    private static readonly Random _flickerRng = new Random();

    public string EntityName { get; private set; } = "Entity";
    public string EntityType { get; private set; } = "enemy";
    public int Gender { get; private set; } = 0; // 0=Male, 1=Female, 2=Neutral
    public int Face { get; private set; } = 0;
    
    // Performance Caches
    private Area3D _clickArea;
    private CollisionShape3D _clickShape;
    private float _boneRecalcTimer = 0f;
    private float _targetLabelY = 6.8f;
    private float _targetClickY = 3.1f;
    
    // Swimming State
    public bool IsInWater { get; private set; } = false;
    public void SetInWater(bool inWater)
    {
        IsInWater = inWater;
    }

    public override void _Ready()
    {
        _nameLabel = GetNodeOrNull<Label3D>("NameLabel");
        _mesh = GetNodeOrNull<MeshInstance3D>("Mesh");

        // Add collision shape — center at Y=0 so bottom is at Y=-1
        // Physics will lift the entity so capsule bottom sits on ground,
        // which raises the model (whose origin is at waist) to proper height
        if (GetNodeOrNull<CollisionShape3D>("Collision") == null) {
            var col = new CollisionShape3D {
                Name = "Collision",
                Shape = new CapsuleShape3D { Radius = 1.0f, Height = 4.7f },
                Position = new Vector3(0, 3.85f, 0) // Shifted up to make room for stair ray
            };
            AddChild(col);
        }

        // Separation ray acts as a "spring leg" to glide up stairs seamlessly
        if (GetNodeOrNull<CollisionShape3D>("StairRay") == null) {
            var ray = new CollisionShape3D {
                Name = "StairRay",
                Shape = new SeparationRayShape3D { Length = 1.5f },
                Position = new Vector3(0, 1.5f, 0),
                RotationDegrees = new Vector3(90, 0, 0) // Pointing straight down
            };
            AddChild(ray);
        }

        // Configure CharacterBody3D for stairs and curbs
        FloorSnapLength = 0.5f; // Helps stick to stairs going down
        FloorMaxAngle = Mathf.DegToRad(75f); // Allow climbing very steep inclines and stairs
        FloorBlockOnWall = false; // Don't get stuck on small vertical lips
        SafeMargin = 0.05f; // Crucial for preventing sticking on geometry seams and stair lips

        // Separate Area3D for mouse click targeting — covers the visible model
        if (GetNodeOrNull<Area3D>("ClickArea") == null) {
            _clickArea = new Area3D { Name = "ClickArea" };
            _clickArea.InputRayPickable = true;
            _clickShape = new CollisionShape3D {
                Name = "ClickShape",
                Shape = new CapsuleShape3D { Radius = 1.5f, Height = 6.2f },
                Position = new Vector3(0, 3.1f, 0) // Centered at 3.1 so bottom is at 0
            };
            _clickArea.AddChild(_clickShape);
            _clickArea.InputEvent += OnInputEvent;
            AddChild(_clickArea);
        } else {
            _clickArea = GetNodeOrNull<Area3D>("ClickArea");
            if (_clickArea != null) {
                _clickShape = _clickArea.GetNodeOrNull<CollisionShape3D>("ClickShape");
                _clickArea.InputEvent += OnInputEvent;
            }
        }

        if (_nameLabel == null) {
            _nameLabel = new Label3D {
                Name = "NameLabel",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Text = "Entity",
                FontSize = 24,
                OutlineSize = 6,
                Position = new Vector3(0, 6.8f, 0)
            };
            AddChild(_nameLabel);
        }
        
        if (_mesh == null) {
            _mesh = new MeshInstance3D {
                Name = "Mesh",
                Position = new Vector3(0, 3.1f, 0)
            };
            var capsule = new CapsuleMesh();
            
            var shader = GD.Load<Shader>("res://Scenes/CapsuleClothes.gdshader");
            if (shader != null) {
                var sm = new ShaderMaterial { Shader = shader };
            capsule.Material = sm;
            }
            _mesh.Mesh = capsule;
            AddChild(_mesh);
        }

        if (_facingArrow == null) {
            _facingArrow = new MeshInstance3D {
                Name = "FacingArrow",
                Position = new Vector3(0, 0.05f, -0.6f),
                Rotation = new Vector3(-Mathf.Pi / 2f, 0, 0) // Lay flat, point forward (-Z)
            };

            var prism = new PrismMesh {
                Size = new Vector3(0.5f, 0.5f, 0.1f)
            };
            var mat = new StandardMaterial3D {
                AlbedoColor = new Color(1f, 1f, 0f),
                EmissionEnabled = true,
                Emission = new Color(1f, 1f, 0f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            prism.Material = mat;
            _facingArrow.Mesh = prism;
            AddChild(_facingArrow);
        }
        
        // Initialize Audio Players
        if (GetNodeOrNull<AudioStreamPlayer3D>("VoicePlayer") == null)
        {
            var vp = new AudioStreamPlayer3D { Name = "VoicePlayer" };
            vp.MaxDistance = 80.0f;
            vp.UnitSize = 10.0f;
            AddChild(vp);
        }
        if (GetNodeOrNull<AudioStreamPlayer3D>("SfxPlayer") == null)
        {
            var sp = new AudioStreamPlayer3D { Name = "SfxPlayer" };
            sp.MaxDistance = 80.0f;
            sp.UnitSize = 10.0f;
            AddChild(sp);
        }
        
        // Click targeting is handled by the ClickArea Area3D (see above)
    }

    private void OnInputEvent(Node camera, InputEvent @event, Vector3 position, Vector3 normal, long shapeIdx)
    {
        if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
        {
            var wm = GetParent()?.GetParent() as WorldManager;
            if (wm != null)
            {
                wm.SetTarget(this);
            }
        }
    }

    // Chase AI
    public Node3D ChaseTarget { get; set; }
    public float? TargetYaw { get; set; }
    public Vector3? TargetPosition { get; set; }
    public float ChaseSpeed { get; set; } = 5f;
    public float ChaseStopDistance { get; set; } = 3f; // stop just inside melee range
    public bool IsAggro => ChaseTarget != null;

    // Set to true for the player capsule — WorldManager handles its physics
    public bool IsPlayerControlled { get; set; } = false;

    // Play a named animation if available, with cross-fade.
    // Falls back to alternative idle animations if the requested one is missing.
    public void PlayAnimation(string animName)
    {
        if (_animPlayer == null || _currentAnim == animName) return;
        if (IsQueuedForDeletion()) return;

        string resolved = animName;
        if (!_animPlayer.HasAnimation(resolved))
        {
            // Try fallback: for idle (p01), try p06, then any p-prefixed, then l09
            resolved = null;
            if (animName == "p01")
            {
                if (_animPlayer.HasAnimation("p06")) resolved = "p06";
                else
                {
                    foreach (var a in _animPlayer.GetAnimationList())
                    {
                        if (a.ToString().StartsWith("p")) { resolved = a; break; }
                    }
                    if (resolved == null && _animPlayer.HasAnimation("l09")) resolved = "l09";
                }
            }

            if (resolved == null)
            {
                // Try movement fallbacks
                if (animName == "l01" || animName == "l02")
                {
                    if (animName == "l01" && _animPlayer.HasAnimation("l02")) resolved = "l02";
                    else if (animName == "l02" && _animPlayer.HasAnimation("l01")) resolved = "l01";
                    else if (_animPlayer.HasAnimation("p01")) resolved = "p01";
                    else
                    {
                        foreach (var a in _animPlayer.GetAnimationList())
                        {
                            if (a.ToString().StartsWith("p")) { resolved = a; break; }
                        }
                    }
                }
            }

            if (resolved == null)
            {
                // Only warn once per entity per animation to avoid log spam
                if (_warnedAnims.Add(animName))
                {
                    var anims = _animPlayer.GetAnimationList();
                    GD.Print($"[ANIM] Animation '{animName}' not found on {EntityName}. Available: {string.Join(", ", anims)}");
                }
                return;
            }
        }

        // Set loop mode: death/social/cast anims play once, movement loops
        var anim = _animPlayer.GetAnimation(resolved);
        bool isCombatOrEmote = resolved.StartsWith("d") || resolved.StartsWith("s") || resolved.StartsWith("t") || resolved.StartsWith("c") || resolved == "p02" || resolved == "p05";
        
        if (isCombatOrEmote)
            anim.LoopMode = Animation.LoopModeEnum.None;
        else
            anim.LoopMode = Animation.LoopModeEnum.Linear;
        bool isImportantTrace = IsPlayerControlled || (ChaseTarget is EntityCapsule ec && ec.IsPlayerControlled);
        if (isImportantTrace)
        {
            GD.Print($"[ANIM_TRACE] {EntityName} playing '{resolved}' (req: {animName}) at speed {_animPlayer.SpeedScale:F2}");
        }

        if (isCombatOrEmote)
        {
            // EQ combat/hit animations are snappy. Don't crossfade, and force restart from frame 0
            _animPlayer.Stop(false);
            _animPlayer.Play(resolved);
        }
        else
        {
            _animPlayer.Play(resolved, 0.2); // 0.2s cross-fade for movement
        }
        
        _currentAnim = animName; // Track the *requested* name so we don't re-resolve every frame
    }

    // --- Animation state tracking ---
    private bool _isDead = false;
    private bool _isSitting = false;
    private double _emoteTimer = 0;
    private string _queuedEmote = null;
    private double _airborneTime = 0;

    // --- Auto-attack animation loop (client-driven, matches server combat tick) ---
    private bool _isAutoAttacking = false;
    private double _autoAttackTimer = 0;
    private float _autoAttackDelay = 3.0f; // effective delay in seconds: (dly/10)/hasteMod
    private string _autoAttackType = "1h_slashing"; // weapon skill name for animation selection

    private float _lastRotationY = 0f;

    // Called by WorldManager to update player animation based on movement
    public void UpdateAnimationFromVelocity(Vector3 velocity, bool isOnFloor = true, bool inCombat = false, bool isSprinting = false, bool isCrouching = false)
    {
        if (_animPlayer == null || _isDead || IsQueuedForDeletion()) return;

        // Calculate turning state before exiting for emote timers, so we track it accurately
        float angleDiff = Mathf.Abs(Mathf.AngleDifference(_lastRotationY, Rotation.Y));
        bool isTurning = angleDiff > 0.005f;
        _lastRotationY = Rotation.Y;

        // If playing a timed emote/cast/attack, let it finish
        if (_emoteTimer > 0) return;

        // Reset speed scale so combat speed modifiers don't leak into movement
        _animPlayer.SpeedScale = 1.0f;

        // If sitting, stay seated until movement breaks it
        float horizSpeed = new Vector2(velocity.X, velocity.Z).Length();
        if (_isSitting && horizSpeed < 0.5f && isOnFloor) return;
        if (_isSitting && horizSpeed > 0.5f) _isSitting = false;

        // Priority: water > jump/fall > run/walk > idle
        if (IsInWater)
        {
            if (horizSpeed > 0.5f || Mathf.Abs(velocity.Y) > 0.5f)
            {
                PlayAnimation("c10"); // swimming forwards (Swimming 1)
                _animPlayer.SpeedScale = Mathf.Clamp(new Vector3(velocity.X, velocity.Y, velocity.Z).Length() / 6.0f, 0.5f, 2.0f);
            }
            else
            {
                PlayAnimation("l09"); // treading water (Swimming Stationary)
                _animPlayer.SpeedScale = 1.0f;
            }
        }
        else if (!isOnFloor)
        {
            _airborneTime += 0.016; // ~1 frame at 60fps
            
            // Grace period: don't switch to jump anims for tiny terrain bumps
            if (_airborneTime < 0.1) return; // Keep playing whatever ground anim was active
            
            if (_airborneTime > 1.0 && velocity.Y < -5f)
            {
                PlayAnimation("l05"); // falling
            }
            else if (horizSpeed > 0.5f)
            {
                PlayAnimation("l03"); // running jump
            }
            else
            {
                PlayAnimation("l04"); // stationary jump
            }
            _animPlayer.SpeedScale = 1.0f;
        }
        else
        {
            _airborneTime = 0;
            if (isCrouching && horizSpeed > 0.5f)
            {
                // Duck walking — same pattern as sprint (l02)
                PlayAnimation("l06");
                _animPlayer.SpeedScale = Mathf.Clamp(horizSpeed / 4.0f, 0.5f, 1.5f);
            }
            else if (isCrouching)
            {
                // Crouch idle — hold last frame of l08
                if (_currentAnim != "l08_hold")
                {
                    if (_animPlayer.HasAnimation("l08"))
                    {
                        var crouchAnim = _animPlayer.GetAnimation("l08");
                        crouchAnim.LoopMode = Animation.LoopModeEnum.None;
                        _animPlayer.Play("l08");
                        _animPlayer.Seek(crouchAnim.Length, true);
                        _animPlayer.Pause();
                        _currentAnim = "l08_hold";
                    }
                }
            }
            else if (horizSpeed > 0.5f)
            {
                if (isSprinting)
                {
                    PlayAnimation("l02"); // running
                    _animPlayer.SpeedScale = 1.0f;
                }
                else
                {
                    PlayAnimation("l01"); // walking
                    _animPlayer.SpeedScale = 1.0f;
                }
            }
            else if (isTurning)
            {
                PlayAnimation("p03"); // shuffle rotate
                _animPlayer.SpeedScale = 1.0f;
            }
            else
            {
                PlayAnimation("p01"); // idle
                _animPlayer.SpeedScale = 1.0f;
            }
        }
    }

    // --- Public animation triggers for UI/server events ---

    public void PlayDeath()
    {
        _isDead = true;
        PlayAnimation("d05"); // death
        PlayVoice("die");
    }

    public void PlayDamage(bool heavy = false)
    {
        if (_isDead) return;
        
        // If we are actively swinging a weapon, don't let flinch interrupt it visually.
        // This prevents the player's attack from being hidden when the server and client
        // perfectly synchronize their combat rounds.
        if (_currentAnim.StartsWith("c") && _emoteTimer > 0)
        {
            PlayVoice("hit");
            return;
        }

        _emoteTimer = 0;
        _currentAnim = ""; // Force re-evaluation so the flinch anim plays immediately
        if (_animPlayer != null)
            _animPlayer.SpeedScale = 1.0f;

        if (_animPlayer != null)
        {
            string flinchAnim = (heavy && _animPlayer.HasAnimation("d02")) ? "d02" : "d01";
            if (_animPlayer.HasAnimation(flinchAnim))
            {
                _animPlayer.Play(flinchAnim, 0.1);
                _currentAnim = flinchAnim;
                _emoteTimer = _animPlayer.GetAnimation(flinchAnim).Length;
            }
            else
            {
                _emoteTimer = 0.5; // Fallback
            }
        }
        PlayVoice("hit");
    }

    public void PlaySit()
    {
        if (_isDead) return;
        _isSitting = true;
        PlayAnimation("p02"); // sit down
    }

    public void PlayLoot()
    {
        if (_isDead) return;
        _emoteTimer = 2.0;
        PlayAnimation("p05"); // loot/kneel
    }

    public void PlayEmote(string emote)
    {
        if (_isDead) return;

        // If we are currently rotating to face a target, queue the emote to play when finished
        if (ChaseTarget == null && TargetYaw.HasValue)
        {
            float currentRad = Mathf.DegToRad(RotationDegrees.Y);
            float targetRad = Mathf.DegToRad(TargetYaw.Value);
            if (Mathf.Abs(Mathf.AngleDifference(currentRad, targetRad)) > 0.05f)
            {
                _queuedEmote = emote;
                return;
            }
        }

        _emoteTimer = 2.5; // let emote play for 2.5 seconds
        string animCode = emote.ToLower() switch
        {
            "cheer" => "s01",
            "disappointed" => "s02",
            "wave" => "s03",
            "rude" => "s04",
            _ => null
        };
        // If no named match, try using the emote string as a raw animation code
        PlayAnimation(animCode ?? emote);
        
        // Emote sounds for combat/aggro
        if (emote == "aggro" || emote == "attack") PlayVoice("atk");
    }

    public void PlayCast(int castType = 1)
    {
        if (_isDead) return;
        _emoteTimer = 3.0; // casting time
        string animCode = castType switch
        {
            1 => "t04", // Cast 1
            2 => "t05", // Cast 2
            3 => "t06", // Cast 3
            _ => "t04"
        };
        PlayAnimation(animCode);
        PlayVoice("spl"); // Use spell voice/shout if exists
    }

    public void PlayAttack(string attackType = "slash", bool isHit = true)
    {
        if (_isDead) return;
        
        string animCode;
        switch (attackType.ToLower())
        {
            case "kick":
            case "kick!": animCode = "c01"; break; // Kick
            case "pierce":
            case "piercing":
            case "backstab":
            case "backstab!": animCode = "c02"; break; // 1H Pierce
            case "2h_slashing":
            case "2h": animCode = "c03"; break; // 2H Slash
            case "2h_blunt":
            case "blunt": animCode = "c04"; break; // 2H Blunt
            case "1h_slashing":
            case "slash": animCode = "c05"; break; // 1H Slash
            case "offhand": animCode = "c06"; break; // 1H Slash Offhand
            case "bash":
            case "bash!": animCode = "c07"; break; // Bash
            case "hand_to_hand":
            case "h2h": animCode = "c08"; break; // Hand to Hand Primary
            case "archery":
            case "bow": animCode = "c09"; break; // Archery
            case "round_kick":
            case "round kick": animCode = "c11"; break; // Roundhouse kick
            case "flying_kick":
            case "flying kick": animCode = "t07"; break; // Flying kick
            case "tiger_strike":
            case "tiger strike": animCode = "t08"; break; // Tiger strike
            case "dragon_punch":
            case "dragon punch": animCode = "t09"; break; // Dragon punch
            default: animCode = "c05"; break; // Default to 1H Slash
        }

        // Play at native 1.0x speed. The previous 0.5x hack was artificially slowing it down.
        if (_animPlayer != null)
            _animPlayer.SpeedScale = 1.0f;

        GD.Print($"[AUTOATK] PlayAnimation({animCode}) called. SpeedScale={_animPlayer?.SpeedScale}");
        PlayAnimation(animCode);
        
        if (_animPlayer != null)
        {
            if (_animPlayer.HasAnimation(animCode))
            {
                // Actual playback time = reported length / speedScale
                _emoteTimer = _animPlayer.GetAnimation(animCode).Length / _animPlayer.SpeedScale;
                GD.Print($"[AUTOATK] emoteTimer set to {_emoteTimer:F3} (Length was {_animPlayer.GetAnimation(animCode).Length:F3})");
            }
        }
        
        // Attack sound (grunt)
        PlayVoice("atk");
    }

    // --- Auto-attack loop control (called by WorldManager) ---

    /// <summary>
    /// Start the client-side auto-attack animation loop.
    /// delaySec = effective weapon delay in seconds: (dly/10)/hasteMod
    /// weaponType = weapon skill name (e.g. "1h_slashing", "piercing")
    /// </summary>
    public void StartAutoAttack(float delaySec, string weaponType)
    {
        _autoAttackDelay = Mathf.Max(0.5f, delaySec); // Floor at 0.5s to prevent spam
        _autoAttackType = weaponType ?? "1h_slashing";
        // Only reset the timer when first starting — don't interrupt mid-swing
        // on subsequent STATUS updates (which fire every server tick)
        if (!_isAutoAttacking)
        {
            _isAutoAttacking = true;
            _autoAttackTimer = 0; // First swing fires immediately
        }
    }

    /// <summary>Stop the auto-attack animation loop.</summary>
    public void StopAutoAttack()
    {
        _isAutoAttacking = false;
        _autoAttackTimer = 0;
    }

    public void PlayInstrument(bool stringed = true)
    {
        if (_isDead) return;
        _emoteTimer = 3.0;
        PlayAnimation(stringed ? "t02" : "t03"); // stringed or woodwind
    }

    public void Revive()
    {
        _isDead = false;
        _isSitting = false;
        PlayAnimation("p01"); // back to idle
    }

    // --- Audio Triggers ---
    private string _modelCode = "hum";

    /// <summary>Get the race-specific model code (e.g., "hum", "elf", "orc") for sound lookups.</summary>
    public string GetModelCode() => _modelCode;

    public void PlayVoice(string action)
    {
        var vp = GetNodeOrNull<AudioStreamPlayer3D>("VoicePlayer");
        if (vp == null) return;

        // Try race-specific first (e.g. "orc_die.wav" or "orc_hit.wav")
        // If that fails, try 3-letter race code + action (e.g. "huf_die.wav" or "aam_hit.wav")
        string[] attempts = {
            $"{_modelCode}_{action}.wav",
            $"{_modelCode.Substring(0, Math.Min(3, _modelCode.Length))}_{action}.wav",
            $"{(EntityType == "player" ? "hum" : "orc")}_{action}.wav" // Fallback to human or orc
        };

        foreach (var file in attempts)
        {
            var stream = EQAssetCache.Instance.GetSound(file);
            if (stream != null)
            {
                vp.Stream = stream;
                vp.Play();
                return;
            }
        }
    }

    public void PlaySfx(string soundName)
    {
        var sp = GetNodeOrNull<AudioStreamPlayer3D>("SfxPlayer");
        if (sp == null) return;

        var stream = EQAssetCache.Instance.GetSound(soundName);
        if (stream != null)
        {
            sp.Stream = stream;
            sp.Play();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsQueuedForDeletion()) return;

        // Tick down emote timer
        if (_emoteTimer > 0)
        {
            _emoteTimer -= delta;
            if (_emoteTimer <= 0)
            {
                _emoteTimer = 0;
                _currentAnim = ""; // force re-evaluation
                if (_animPlayer != null) _animPlayer.SpeedScale = 1.0f; // reset combat speed scaling
            }
        }

        // Auto-attack animation loop (player only)
        // Fires the full attack animation on a timer matching the server's combat tick.
        if (_isAutoAttacking && IsPlayerControlled && !_isDead)
        {
            _autoAttackTimer -= delta;
            if (_autoAttackTimer <= 0)
            {
                if (_emoteTimer <= 0)
                {
                    PlayAttack(_autoAttackType, true);
                    _autoAttackTimer = _autoAttackDelay; // Next swing after full delay
                }
            }
        }

        // Torch flicker effect — subtle energy variation for realism
        if (_torchLight != null && _hasLightSource)
        {
            _torchFlickerTimer += (float)delta;
            if (_torchFlickerTimer > 0.08f) // flicker ~12 times per second
            {
                _torchFlickerTimer = 0f;
                _torchLight.LightEnergy = 1.3f + (float)(_flickerRng.NextDouble() * 0.4 - 0.2);
            }
        }

        // Dynamically track Skeleton bone heights to adjust NameLabel and ClickArea
        // This solves issues where flying mobs (like bats) have rest-poses on the ground
        // but their active animations lift them 15+ units into the sky.
        if (_characterModel != null && _nameLabel != null)
        {
            _boneRecalcTimer += (float)delta;
            if (_boneRecalcTimer >= 0.2f) // Throttle expensive bone recalculations to 5 times per sec
            {
                _boneRecalcTimer = 0f;
                var skeleton = _cachedSkeleton;
                if (skeleton != null && skeleton.GetBoneCount() > 0)
                {
                    float maxY = -1000f;
                    float minY = 1000f;
                    for (int i = 0; i < skeleton.GetBoneCount(); i++)
                    {
                        var pose = skeleton.GetBoneGlobalPose(i);
                        if (pose.Origin.Y > maxY) maxY = pose.Origin.Y;
                        if (pose.Origin.Y < minY) minY = pose.Origin.Y;
                    }

                    // Add margin for actual mesh volume around the bone
                    maxY += 1.0f;
                    minY -= 1.0f;

                    float finalScale = _characterModel.Scale.Y;
                    float baseHeight = _characterModel.Position.Y;

                    float visualTopY = baseHeight + (maxY * finalScale);
                    float visualCenterY = baseHeight + (((maxY + minY) / 2f) * finalScale);
                    
                    _targetLabelY = Mathf.Max(6.8f, visualTopY + 0.6f);
                    _targetClickY = Mathf.Max(3.1f, visualCenterY);
                }
            }

            // Smoothly interpolate label and click capsule every frame (using cached targets)
            float currentLabelY = _nameLabel.Position.Y;
            _nameLabel.Position = new Vector3(0, Mathf.Lerp(currentLabelY, _targetLabelY, 5f * (float)delta), 0);

            if (_clickShape != null)
            {
                float currentClickY = _clickShape.Position.Y;
                _clickShape.Position = new Vector3(0, Mathf.Lerp(currentClickY, _targetClickY, 5f * (float)delta), 0);
            }
        }

        // Player movement is handled by WorldManager
        if (IsPlayerControlled) return;

        float gravity = 20f;
        var velocity = Velocity;

        // Dead NPCs don't move
        if (_isDead)
        {
            velocity.X = 0;
            velocity.Y = 0;
            velocity.Z = 0;
            Velocity = velocity;
            MoveAndSlide();
            return;
        }

        // Apply Server-Authoritative Y-Position or Gravity Fallback
        if (ChaseTarget == null && TargetPosition.HasValue)
        {
            if (IsInWater)
            {
                // Smooth approach to server Y only when swimming
                float yDist = TargetPosition.Value.Y - GlobalPosition.Y;
                if (Mathf.Abs(yDist) > 0.1f)
                    velocity.Y = Mathf.Sign(yDist) * Mathf.Min(Mathf.Abs(yDist) * 5f, 20f);
                else
                    velocity.Y = 0;
            }
            else if (!IsOnFloor())
            {
                velocity.Y -= gravity * (float)delta;
            }
        }
        else if (ChaseTarget is EntityCapsule ct && ct.IsInWater)
        {
            // If chasing a swimming target, predict their Y to follow them in 3D space
            float yDist = ct.GlobalPosition.Y - GlobalPosition.Y;
            if (Mathf.Abs(yDist) > 0.1f)
                velocity.Y = Mathf.Sign(yDist) * Mathf.Min(Mathf.Abs(yDist) * 3f, ChaseSpeed);
            else
                velocity.Y = 0;
        }
        else
        {
            // Fallback: normal Godot gravity if we aren't moving to a server point
            if (!IsOnFloor() && !IsInWater)
                velocity.Y -= gravity * (float)delta;
            else if (IsInWater && !IsOnFloor())
                velocity.Y = 0f; // Neutral buoyancy
            else
                velocity.Y = 0;
        }

        // No chase target — just stand in place or move to TargetPosition
        if (ChaseTarget == null)
        {
            if (TargetYaw.HasValue)
            {
                float currentYaw = RotationDegrees.Y;
                float targetYaw = TargetYaw.Value;
                // Use LerpAngle equivalent for degrees (Godot Mathf.LerpAngle uses radians, so we convert)
                float currentRad = Mathf.DegToRad(currentYaw);
                float targetRad = Mathf.DegToRad(targetYaw);
                
                if (Mathf.Abs(Mathf.AngleDifference(currentRad, targetRad)) < 0.05f)
                {
                    // Reached target
                    RotationDegrees = new Vector3(0, targetYaw, 0);
                    TargetYaw = null;
                    if (_queuedEmote != null)
                    {
                        string toPlay = _queuedEmote;
                        _queuedEmote = null;
                        PlayEmote(toPlay);
                    }
                }
                else
                {
                    float newRad = Mathf.LerpAngle(currentRad, targetRad, 5f * (float)delta);
                    RotationDegrees = new Vector3(0, Mathf.RadToDeg(newRad), 0);
                }
            }

            if (TargetPosition.HasValue)
            {
                Vector3 currentPos = GlobalPosition;
                Vector3 targetPos = TargetPosition.Value;
                
                Vector2 current2D = new Vector2(currentPos.X, currentPos.Z);
                Vector2 target2D = new Vector2(targetPos.X, targetPos.Z);
                float targetDist = current2D.DistanceTo(target2D);

                if (targetDist > 0.1f)
                {
                    Vector2 dir = (target2D - current2D).Normalized();
                    
                    // Dynamically scale speed to server tick rate (~5 updates per sec)
                    // This smooths out stutter-stepping by matching approach speed to arrival distance
                    float moveSpeed = Mathf.Clamp(targetDist * 5.0f, 2.0f, 25.0f);
                    
                    velocity.X = dir.X * moveSpeed;
                    velocity.Z = dir.Y * moveSpeed;

                    if (_emoteTimer <= 0)
                    {
                        PlayAnimation("l01"); // walk
                    }
                }
                else
                {
                    velocity.X = 0;
                    velocity.Z = 0;
                    TargetPosition = null;
                    if (_emoteTimer <= 0)
                    {
                        PlayAnimation("p01"); // idle
                    }
                }
            }
            else
            {
                velocity.X = 0;
                velocity.Z = 0;
                if (_emoteTimer <= 0)
                {
                    PlayAnimation("p01"); // idle
                }
            }

            Velocity = velocity;
            MoveAndSlide();
            return;
        }

        // Rotate to face ChaseTarget whether moving or stopped
        Vector3 faceDir = (ChaseTarget.GlobalPosition - GlobalPosition);
        faceDir.Y = 0;
        if (faceDir.LengthSquared() > 0.001f) {
            var targetTransform = GlobalTransform.LookingAt(GlobalPosition + faceDir.Normalized(), Vector3.Up);
            GlobalTransform = GlobalTransform.InterpolateWith(targetTransform, 10f * (float)delta);
        }

        // Chase logic — move toward target with physics
        float dist = GlobalPosition.DistanceTo(ChaseTarget.GlobalPosition);
        if (dist > ChaseStopDistance)
        {
            Vector3 dir = faceDir.Normalized();
            velocity.X = dir.X * ChaseSpeed;
            velocity.Z = dir.Z * ChaseSpeed;
            PlayAnimation("l02"); // run toward target
        }
        else
        {
            velocity.X = 0;
            velocity.Z = 0;
            if (_emoteTimer <= 0) {
                PlayAnimation("p01"); // idle in melee range
            }
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    // ── Race → Model mapping loaded from Data/race_models.json (generated from EQ client racedata.txt) ──
    private class RaceEntry { public string m { get; set; } public string f { get; set; } public float s { get; set; } = 1.0f; }
    private static System.Collections.Generic.Dictionary<int, RaceEntry> _raceData;
    private static bool _raceDataLoaded = false;

    private static void LoadRaceData()
    {
        if (_raceDataLoaded) return;
        _raceDataLoaded = true;
        _raceData = new System.Collections.Generic.Dictionary<int, RaceEntry>();

        string path = "res://Data/race_models.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr("[MODEL] race_models.json not found! Using empty race map.");
            return;
        }

        try
        {
            string json = FileAccess.Open(path, FileAccess.ModeFlags.Read).GetAsText();
            var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                int raceId = int.Parse(prop.Name);
                var entry = new RaceEntry
                {
                    m = prop.Value.GetProperty("m").GetString(),
                    f = prop.Value.GetProperty("f").GetString(),
                    s = (float)prop.Value.GetProperty("s").GetDouble()
                };
                _raceData[raceId] = entry;
            }
            GD.Print($"[MODEL] Loaded {_raceData.Count} race models from race_models.json");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MODEL] Failed to load race_models.json: {ex.Message}");
        }
    }

    // Compatibility wrapper for existing RaceModelMap usage
    private static readonly System.Collections.Generic.Dictionary<int, (string male, string female)> RaceModelMap = new();

    private static bool TryGetRaceModel(int race, out (string male, string female) codes)
    {
        LoadRaceData();
        if (_raceData != null && _raceData.TryGetValue(race, out var entry))
        {
            codes = (entry.m, entry.f);
            return true;
        }
        codes = ("hum", "huf");
        return false;
    }

    private static float GetRaceScale(int race)
    {
        LoadRaceData();
        if (_raceData != null && _raceData.TryGetValue(race, out var entry))
            return entry.s;
        return 1.0f; // Default to human scale
    }

    private Node3D _characterModel;
    private Skeleton3D _cachedSkeleton;

    public float EyeHeight { get; private set; } = 5.8f;
    public float OverheadHeight { get; private set; } = 7.0f;

    public void Setup(string name, string type, string appearanceJson = "", int race = 1, int gender = 0, int face = 0, string equipVisualsJson = "", float size = 6f)
    {
        // Setup nodes if Setup is called before _Ready
        if (_nameLabel == null || _mesh == null) _Ready();

        EntityName = name;
        EntityType = type;
        Gender = gender;
        Face = face;
        _nameLabel.Text = name;
        
        // Apply classic EQ color coding
        bool isPlayable = (race >= 1 && race <= 12) || race == 128 || race == 130 || race == 330 || race == 67 || race == 71 || race == 74 || race == 78;
        
        float baseMultiplier = 1.0f;
        string lowerName = name.ToLower();
        if (isPlayable || type == "player" || type == "npc" || lowerName.Contains("guard") || lowerName.Contains("sentinel") || lowerName.Contains("protector")) 
        {
            baseMultiplier = 1.5f;
        }
        
        // For non-player mobs, use their DB size normalized against the human default of 6.
        // We use a logarithmic dampening curve to prevent extreme sizes while preserving
        // relative differences. A size=60 shark won't be 10x bigger, just noticeably larger.
        float sizeMultiplier = 1.0f;
        if (type != "player")
        {
            float rawRatio = size / 6.0f;  // 6 = human default
            
            if (rawRatio > 1.0f)
            {
                // Log dampening for large mobs: log2(ratio) + 1
                sizeMultiplier = 1.0f + Mathf.Log(rawRatio) / Mathf.Log(2) * 0.3f;
            }
            else if (rawRatio < 1.0f && rawRatio > 0f)
            {
                // Mirror for small mobs
                sizeMultiplier = 1.0f + Mathf.Log(rawRatio) / Mathf.Log(2) * 0.2f;
            }
            
            // Hard clamp to prevent game-breaking sizes
            sizeMultiplier = Mathf.Clamp(sizeMultiplier, 0.6f, 2.0f);
        }
        
        float raceScale = GetRaceScale(race) * baseMultiplier * sizeMultiplier;
        EyeHeight = 5.8f * raceScale;
        OverheadHeight = 7.0f * raceScale;

        // Apply classic EQ color coding
        Color nameColor = new Color(1.0f, 1.0f, 1.0f); // Default white

        switch (type.ToLower())
        {
            case "enemy":
            case "mob":
                nameColor = new Color(1.0f, 0.2f, 0.2f); // Red
                break;
            case "player":
                nameColor = new Color(0.2f, 0.2f, 1.0f); // Blue
                break;
            case "npc":
                nameColor = new Color(1.0f, 1.0f, 0.2f); // Yellow
                break;
            case "pet":
                nameColor = new Color(0.2f, 1.0f, 0.2f); // Green — owned pet
                break;
            case "mining_node":
                nameColor = new Color(0.85f, 0.65f, 0.3f); // Gold/brown for ore
                break;
            case "corpse":
                nameColor = new Color(0.6f, 0.6f, 0.6f); // Grey
                break;
        }

        _nameLabel.Modulate = nameColor;
        
        if (_facingArrow != null && _facingArrow.Mesh is PrismMesh pm && pm.Material is StandardMaterial3D sam) {
            sam.AlbedoColor = nameColor;
            sam.Emission = nameColor;
        }

        // Mining nodes get a rock mesh instead of a character model
        if (type.ToLower() == "mining_node")
        {
            // Hide the default capsule and arrow
            if (_mesh != null) _mesh.Visible = false;
            if (_facingArrow != null) _facingArrow.Visible = false;

            // Create a rocky boulder visual
            var rockMesh = new MeshInstance3D();
            var sphere = new SphereMesh();
            sphere.Radius = 1.2f;
            sphere.Height = 1.8f;
            sphere.RadialSegments = 8;  // Low-poly for a rocky look
            sphere.Rings = 4;

            var rockMat = new StandardMaterial3D();
            rockMat.AlbedoColor = new Color(0.45f, 0.40f, 0.35f); // Dark grey-brown rock
            rockMat.Roughness = 0.95f;
            rockMat.Metallic = 0.15f;
            // Add subtle gold emission to hint at ore content
            rockMat.Emission = new Color(0.4f, 0.3f, 0.1f);
            rockMat.EmissionEnergyMultiplier = 0.15f;
            sphere.Material = rockMat;

            rockMesh.Mesh = sphere;
            rockMesh.Position = new Vector3(0, -1.5f, 0); // Sit on the ground
            AddChild(rockMesh);

            // Reposition name label lower (rocks are shorter than characters)
            if (_nameLabel != null)
                _nameLabel.Position = new Vector3(0, 1.0f, 0);

            // Resize click area for rock proportions
            var clickArea = GetNodeOrNull<Area3D>("ClickArea");
            if (clickArea != null)
            {
                var clickShape = clickArea.GetNodeOrNull<CollisionShape3D>("ClickShape");
                if (clickShape != null)
                {
                    clickShape.Position = new Vector3(0, 0.9f, 0);
                    clickShape.Shape = new SphereShape3D { Radius = 1.5f };
                }
            }

            return; // Don't try to load a character model for mining nodes
        }

        // Try to load actual character model GLB
        bool modelLoaded = false;
        if (TryGetRaceModel(race, out var codes))
        {
            string modelCode = (gender == 1) ? codes.female : codes.male;
            _modelCode = modelCode;
            
            string modelPath = $"res://Data/Characters/{modelCode}.glb";

            if (face > 0)
            {
                if (modelCode != "frm" && modelCode != "frf" && modelCode != "kem" && modelCode != "kef")
                {
                    string facePath = $"res://Data/Characters/{modelCode}_face{face}.glb";
                    if (ResourceLoader.Exists(facePath))
                    {
                        modelPath = facePath;
                    }
                }
            }

            string mergedPath = $"res://Data/Characters/{modelCode}_merged.glb";
            bool useMergedAnims = false;
            if (ResourceLoader.Exists(mergedPath))
            {
                useMergedAnims = true;
            }

            if (ResourceLoader.Exists(modelPath))
            {
                try
                {
                    var scene = GD.Load<PackedScene>(modelPath);
                    if (scene != null)
                    {
                        _characterModel = scene.Instantiate<Node3D>();

                        if (useMergedAnims)
                        {
                            var mergedScene = GD.Load<PackedScene>(mergedPath);
                            if (mergedScene != null)
                            {
                                var mergedInst = mergedScene.Instantiate<Node3D>();
                                var animPlayer = FindAnimationPlayer(mergedInst);
                                if (animPlayer != null)
                                {
                                    animPlayer.Owner = null;
                                    animPlayer.GetParent().RemoveChild(animPlayer);
                                    _characterModel.AddChild(animPlayer);
                                    animPlayer.Owner = _characterModel;

                                    // Fix broken track paths from the merged GLB
                                    Skeleton3D skeleton = FindSkeleton(_characterModel);
                                    if (skeleton != null)
                                    {
                                        animPlayer.RootNode = new NodePath("..");
                                        string skeletonPath = _characterModel.GetPathTo(skeleton);
                                        foreach (var animName in animPlayer.GetAnimationList())
                                        {
                                            var anim = animPlayer.GetAnimation(animName);
                                            for (int i = 0; i < anim.GetTrackCount(); i++)
                                            {
                                                string oldPath = anim.TrackGetPath(i).ToString();
                                                string subPath = oldPath.Contains(':') ? oldPath.Substring(oldPath.IndexOf(':')) : "";
                                                subPath = subPath.Replace("Clone of ", "");
                                                
                                                if (oldPath.Contains("Skeleton3D"))
                                                {
                                                    anim.TrackSetPath(i, skeletonPath + subPath);
                                                }
                                                else
                                                {
                                                    anim.TrackSetPath(i, "." + subPath);
                                                }
                                            }
                                        }
                                    }
                                }
                                mergedInst.QueueFree();
                            }
                        }

                        // Rotate model to face Godot's -Z forward
                        // LanternExtractor: EQ models face +X, need +90° Y
                        // EQSage exports (Iksar/Vah Shir): face opposite, need +270° Y
                        bool isFaceVariant = modelPath.Contains("_face");
                        float yRot = (race == 128 || race == 130) ? 270f : 90f;
                        _characterModel.RotationDegrees = new Vector3(0, yRot, 0);

                        float modelCorrection = 1.0f;
                        var rawAabb = GetCombinedAABB(_characterModel);
                        
                        if (isFaceVariant)
                        {
                            float modelHeight = rawAabb.Size.Y;
                            if (modelHeight > 10f)
                            {
                                modelCorrection = 6.2f / modelHeight;
                            }
                        }

                        float finalScaleY = raceScale * modelCorrection;
                        _characterModel.Scale = new Vector3(finalScaleY, finalScaleY, finalScaleY);
                        
                        // Shift model up so its lowest vertex sits exactly at Y=0 (the capsule floor)
                        // This handles humanoids (origin at waist) and creatures (origin at bottom) seamlessly.
                        _characterModel.Position = new Vector3(0, -rawAabb.Position.Y * finalScaleY, 0);

                        // Now that the model is positioned and scaled, measure its FINAL visual bounds
                        // so we can properly place the NameLabel and ClickArea (especially for flying bats)
                        var visualAabb = GetCombinedAABB(_characterModel);
                        // Convert visualAabb from local _characterModel space to EntityCapsule space
                        float visualCenterY = _characterModel.Position.Y + (visualAabb.Position.Y + visualAabb.Size.Y / 2f) * finalScaleY;
                        float visualTopY = _characterModel.Position.Y + (visualAabb.Position.Y + visualAabb.Size.Y) * finalScaleY;
                        float visualHeight = visualAabb.Size.Y * finalScaleY;

                        // Scale physics collision capsule to match race scale (always anchored to ground for gravity)
                        var col = GetNodeOrNull<CollisionShape3D>("Collision");
                        if (col != null) {
                            col.Position = new Vector3(0, 3.85f * raceScale, 0);
                            col.Shape = new CapsuleShape3D {
                                Radius = 1.0f * raceScale,
                                Height = 4.7f * raceScale
                            };
                        }

                        var stairRay = GetNodeOrNull<CollisionShape3D>("StairRay");
                        if (stairRay != null) {
                            stairRay.Position = new Vector3(0, 1.5f * raceScale, 0);
                            stairRay.Shape = new SeparationRayShape3D { Length = 1.5f * raceScale };
                        }
                        
                        // Position click targeting area at the VISUAL center of the model
                        var clickArea = GetNodeOrNull<Area3D>("ClickArea");
                        if (clickArea != null) {
                            var clickShape = clickArea.GetNodeOrNull<CollisionShape3D>("ClickShape");
                            if (clickShape != null) {
                                float clickHeight = Mathf.Max(6.2f * raceScale, visualHeight);
                                clickShape.Position = new Vector3(0, visualCenterY, 0);
                                clickShape.Shape = new CapsuleShape3D {
                                    Radius = 1.5f * raceScale,
                                    Height = clickHeight
                                };
                            }
                        }
                        AddChild(_characterModel);
                        // Hide placeholder visuals (capsule + facing arrow) — model has its own face
                        if (_mesh != null) _mesh.Visible = false;
                        if (_facingArrow != null) _facingArrow.Visible = false;
                        modelLoaded = true;

                        // Reposition name label above the VISUAL top of the model
                        if (_nameLabel != null)
                            _nameLabel.Position = new Vector3(0, visualTopY + 0.6f, 0);

                        // Fix materials: EQ textures are painted sprites, not metallic.
                        // Remove specular/metallic to prevent shiny sun/moon reflections.
                        FixCharacterMaterials(_characterModel);

                        // Cache the Skeleton3D to prevent expensive recursive lookups in _PhysicsProcess
                        _cachedSkeleton = FindSkeleton(_characterModel);

                        // Store AnimationPlayer reference and start idle/death
                        _animPlayer = FindAnimationPlayer(_characterModel);
                        if (type.ToLower() == "corpse")
                        {
                            PlayDeath();
                            // Stop the animation at the end so it stays dead
                            _animPlayer.Advance(_animPlayer.CurrentAnimationLength);
                            _animPlayer.Pause();
                        }
                        else
                        {
                            PlayAnimation("p01");
                        }

                        // Apply per-slot armor textures if provided
                        if (!string.IsNullOrEmpty(equipVisualsJson))
                        {
                            ApplyArmorTextures(_characterModel, modelCode, equipVisualsJson);
                            AttachWeapons(_characterModel, equipVisualsJson);
                        }
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[MODEL] Failed to load {modelPath}: {ex.Message}");
                }
            }
            else
            {
                GD.Print($"[MODEL] GLB not found at {modelPath} for '{name}' (race={race} gender={gender})");
            }
        }
        else
        {
            if (_warnedRaces.Add(race))
            {
                GD.Print($"[MODEL] No race mapping for '{name}' (race={race} gender={gender}), using capsule");
            }
        }

        if (!modelLoaded)
        {
            // Fall back to capsule with appearance shader
            ShaderMaterial mat = null;
            if (_mesh.MaterialOverride == null) {
                var prim = _mesh.Mesh as PrimitiveMesh;
                var orig = (prim != null) ? prim.Material as ShaderMaterial : null;
                if (orig != null) {
                    mat = (ShaderMaterial)orig.Duplicate();
                    _mesh.MaterialOverride = mat;
                }
            } else {
                mat = _mesh.MaterialOverride as ShaderMaterial;
            }

            // Apply appearance block
            if (mat != null && !string.IsNullOrEmpty(appearanceJson)) {
                try {
                    var app = JsonDocument.Parse(appearanceJson).RootElement;
                    
                    if (app.TryGetProperty("hat", out var h)) mat.SetShaderParameter("color_hat", new Color(h.GetString()));
                    if (app.TryGetProperty("skin", out var s)) mat.SetShaderParameter("color_skin", new Color(s.GetString()));
                    if (app.TryGetProperty("torso", out var t)) mat.SetShaderParameter("color_torso", new Color(t.GetString()));
                    if (app.TryGetProperty("legs", out var l)) mat.SetShaderParameter("color_legs", new Color(l.GetString()));
                    if (app.TryGetProperty("feet", out var f)) mat.SetShaderParameter("color_feet", new Color(f.GetString()));
                } catch (Exception ex) {
                    GD.PrintErr("Error parsing appearance JSON: " + ex.Message);
                }
            }
        }
    }

    // ═══════ Armor Texture Swap ═══════════════════════════════════════
    // Body part codes matching EQ texture naming convention
    private static readonly (string code, string jsonKey)[] ArmorSlots = {
        ("he", "head"), ("ch", "chest"), ("ua", "arms"),
        ("fa", "wrist"), ("hn", "hands"), ("lg", "legs"), ("ft", "feet")
    };

    private void ApplyArmorTextures(Node3D modelRoot, string raceCode, string armorMaterialsJson)
    {
        try
        {
            var mats = JsonDocument.Parse(armorMaterialsJson).RootElement;
            int swapped = 0;

            foreach (var (partCode, jsonKey) in ArmorSlots)
            {
                string matStr = null;
                if (mats.TryGetProperty(jsonKey, out var matVal))
                {
                    int material = matVal.GetInt32();
                    if (material > 0) matStr = material.ToString("D2");
                }
                ApplyPartTextures(modelRoot, raceCode, partCode, matStr, ref swapped);
            }

            // if (swapped > 0)
            //    // GD.Print($"[MODEL] Applied {swapped} armor texture swaps for {raceCode}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MODEL] Armor texture parse error: {ex.Message}");
        }
    }

    private void ApplyPartTextures(Node node, string raceCode, string partCode, string matStr, ref int swapped)
    {
        if (node is MeshInstance3D meshInst)
        {
            for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
            {
                var mat = meshInst.GetActiveMaterial(i);
                if (mat is not StandardMaterial3D stdMat) continue;

                string matName = stdMat.ResourceName;
                if (string.IsNullOrEmpty(matName)) continue;

                string partPrefix = $"{raceCode}{partCode}";
                int idx = matName.IndexOf(partPrefix, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                // Extract piece number from material name (last 2 digits)
                string afterPart = matName.Substring(idx + partPrefix.Length);
                string digits = "";
                foreach (char c in afterPart) { if (char.IsDigit(c)) digits += c; }
                if (digits.Length < 2) continue;
                string piece = digits.Substring(digits.Length - 2);

                if (matStr == null)
                {
                    // If unequipping head armor, check if we need to apply a face texture variant
                    if (partCode == "he" && Face > 0)
                    {
                        // Proceed to load face texture
                    }
                    else
                    {
                        meshInst.SetSurfaceOverrideMaterial(i, null);
                        swapped++;
                        continue;
                    }
                }

                string targetMatStr = matStr ?? "00";
                
                // If this is the base head texture (no helmet), apply the Face index to the piece number
                int pieceNum = int.Parse(piece);
                if (targetMatStr == "00" && partCode == "he" && Face > 0)
                {
                    pieceNum += Face * 10;
                }
                string targetPiece = pieceNum.ToString("D2");

                // Load armor texture: {race}{part}{material}{piece}.png
                string texFile = $"{raceCode}{partCode}{targetMatStr}{targetPiece}.png";
                string texPath = $"res://Data/Characters/Textures/{texFile}";

                if (!ResourceLoader.Exists(texPath)) continue;

                var armorTex = GD.Load<Texture2D>(texPath);
                if (armorTex == null) continue;

                var newMat = (StandardMaterial3D)stdMat.Duplicate();
                newMat.AlbedoTexture = armorTex;
                meshInst.SetSurfaceOverrideMaterial(i, newMat);
                swapped++;
            }
        }

        foreach (var child in node.GetChildren())
        {
            ApplyPartTextures(child, raceCode, partCode, matStr, ref swapped);
        }
    }

    private void ApplyLuclinSkinToNode(Node node, string raceCode, int face, ref int swapped)
    {
        if (node is MeshInstance3D meshInst)
        {
            int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 0;
            for (int i = 0; i < surfaceCount; i++)
            {
                var mat = meshInst.GetActiveMaterial(i);
                if (mat is not StandardMaterial3D stdMat) continue;

                string matName = stdMat.ResourceName;
                if (string.IsNullOrEmpty(matName))
                {
                    if (stdMat.AlbedoTexture != null)
                    {
                        matName = System.IO.Path.GetFileNameWithoutExtension(stdMat.AlbedoTexture.ResourcePath);
                        if (matName.StartsWith($"{raceCode}_"))
                        {
                            matName = matName.Substring(raceCode.Length + 1);
                        }
                    }
                }

                if (string.IsNullOrEmpty(matName)) continue;

                if (matName.StartsWith("d_"))
                {
                    matName = matName.Substring(2);
                }

                if ((raceCode == "kem" || raceCode == "kef") && matName.EndsWith("0001"))
                {
                    matName = matName.Substring(0, matName.Length - 4) + "sk01";
                }

                string texPath;
                if (raceCode == "kem" || raceCode == "kef")
                {
                    texPath = $"res://Data/Characters/{raceCode}_face{face}_{matName}.png";
                }
                else
                {
                    texPath = $"res://Data/Characters/{raceCode}_{face:D2}_{matName}.png";
                }
                if (!ResourceLoader.Exists(texPath))
                {
                    if (raceCode == "kem" || raceCode == "kef")
                    {
                        string fallbackMat = matName;
                        if (fallbackMat.EndsWith("sk01") || fallbackMat.EndsWith("sk02") || fallbackMat.EndsWith("sk03") || fallbackMat.EndsWith("sk04") || fallbackMat.EndsWith("sk05"))
                        {
                            fallbackMat = fallbackMat.Substring(0, fallbackMat.Length - 4) + "0001 (Base Color) image";
                            string fallbackPath = $"res://Data/Characters/{raceCode}_face{face}_{fallbackMat}.png";
                            if (ResourceLoader.Exists(fallbackPath))
                            {
                                texPath = fallbackPath;
                            }
                        }
                    }
                }

                if (!ResourceLoader.Exists(texPath)) continue;

                var faceTex = GD.Load<Texture2D>(texPath);
                if (faceTex == null) continue;

                var newMat = (StandardMaterial3D)stdMat.Duplicate();
                newMat.AlbedoTexture = faceTex;
                meshInst.SetSurfaceOverrideMaterial(i, newMat);
                swapped++;
            }
        }

        foreach (var child in node.GetChildren())
        {
            ApplyLuclinSkinToNode(child, raceCode, face, ref swapped);
        }
    }

    // ═══════ Runtime Equipment Visual Update ══════════════════════════
    /// <summary>
    /// Update equipment visuals on an already-loaded character model.
    /// Strips old weapon/shield attachments and re-applies from new JSON.
    /// </summary>
    public void UpdateEquipVisuals(string equipVisualsJson)
    {
        if (_characterModel == null) return;

        // Remove existing weapon bone attachments (they are named "attach_r_point", "attach_l_point", "attach_shield_point")
        var skeleton = _cachedSkeleton ?? FindSkeleton(_characterModel);
        if (skeleton != null)
        {
            var toRemove = new System.Collections.Generic.List<Node>();
            foreach (var child in skeleton.GetChildren())
            {
                if (child is BoneAttachment3D ba && ba.Name.ToString().Contains("attach_"))
                    toRemove.Add(child);
            }
            foreach (var node in toRemove)
            {
                node.QueueFree();
            }
        }

        // Determine model code for armor textures
        string modelCode = _modelCode; // Use the stored model code (e.g. frm, hum)
        if (string.IsNullOrEmpty(modelCode))
        {
            modelCode = _characterModel.Name.ToString().Replace("equip_", "");
        }
        
        string safeVisualsJson = string.IsNullOrEmpty(equipVisualsJson) ? "{}" : equipVisualsJson;
        
        if ((modelCode == "frm" || modelCode == "frf" || modelCode == "kem" || modelCode == "kef") && Face > 0)
        {
            int swapped = 0;
            ApplyLuclinSkinToNode(_characterModel, modelCode, Face, ref swapped);
        }

        ApplyArmorTextures(_characterModel, modelCode, safeVisualsJson);
        
        // Re-apply weapons if we have visual data
        if (!string.IsNullOrEmpty(equipVisualsJson))
        {
            AttachWeapons(_characterModel, equipVisualsJson);
        }
    }

    // ═══════ Weapon / Shield Attachment ═══════════════════════════════
    private void AttachWeapons(Node3D modelRoot, string equipVisualsJson)
    {
        try
        {
            var vis = JsonDocument.Parse(equipVisualsJson).RootElement;

            if (vis.TryGetProperty("primaryWeapon", out var pwProp))
            {
                string pw = pwProp.GetString();
                if (!string.IsNullOrEmpty(pw))
                    AttachEquipmentToPoint(modelRoot, pw, "r_point");
            }

            if (vis.TryGetProperty("secondaryWeapon", out var swProp))
            {
                string sw = swProp.GetString();
                if (!string.IsNullOrEmpty(sw))
                {
                    // Try shield_point first, fall back to l_point
                    if (!AttachEquipmentToPoint(modelRoot, sw, "shield_point"))
                        AttachEquipmentToPoint(modelRoot, sw, "l_point");
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MODEL] Weapon attach error: {ex.Message}");
        }
    }

    private bool AttachEquipmentToPoint(Node3D modelRoot, string idfile, string boneName)
    {
        // In Godot, bones are NOT child nodes — they live in Skeleton3D.
        // We must use BoneAttachment3D to parent objects to bones.
        var skeleton = _cachedSkeleton ?? FindSkeleton(modelRoot);
        if (skeleton == null)
        {
            GD.Print($"[MODEL] No Skeleton3D found for {idfile}");
            return false;
        }

        int boneIdx = skeleton.FindBone(boneName);
        if (boneIdx < 0)
        {
            // Some models (like Iksars from EQSage) have bones prefixed with "Clone of "
            boneIdx = skeleton.FindBone("Clone of " + boneName);
            if (boneIdx < 0)
            {
                GD.Print($"[MODEL] Bone '{boneName}' not found in skeleton for {idfile}");
                return false;
            }
            // Update boneName so BoneAttachment3D binds correctly
            boneName = "Clone of " + boneName;
        }

        // Load the equipment GLB
        string glbPath = $"res://Data/Equipment/{idfile}.glb";
        if (!ResourceLoader.Exists(glbPath))
        {
            GD.Print($"[MODEL] Equipment model not found: {glbPath}");
            return false;
        }

        try
        {
            var scene = GD.Load<PackedScene>(glbPath);
            if (scene == null) return false;

            var weaponModel = scene.Instantiate<Node3D>();
            weaponModel.Name = $"equip_{idfile}";

            // Create BoneAttachment3D to parent weapon to the bone
            var attachment = new BoneAttachment3D();
            attachment.Name = $"attach_{boneName}";
            attachment.BoneIdx = boneIdx;
            attachment.BoneName = boneName;
            skeleton.AddChild(attachment);
            attachment.AddChild(weaponModel);

            // Debug: log what's inside the weapon model and fix materials
            FixWeaponMaterials(weaponModel, idfile);

            // GD.Print($"[MODEL] Attached {idfile} to bone '{boneName}' (idx={boneIdx})");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MODEL] Failed to attach {idfile}: {ex.Message}");
            return false;
        }
    }

    private Aabb GetCombinedAABB(Node root)
    {
        Aabb combined = new Aabb();
        bool first = true;
        
        var meshes = new System.Collections.Generic.List<MeshInstance3D>();
        FindAllMeshes(root, meshes);
        
        foreach (var mi in meshes)
        {
            if (mi.Mesh != null)
            {
                var aabb = mi.GetAabb();
                // Transform the AABB by the MeshInstance3D's transform relative to the root node
                Transform3D transform = mi.Transform;
                var root3D = root as Node3D;
                
                if (root.IsInsideTree() && mi.IsInsideTree() && root3D != null) {
                    transform = root3D.GlobalTransform.AffineInverse() * mi.GlobalTransform;
                } else {
                    // Manually walk up the parents
                    var parent = mi.GetParent<Node3D>();
                    while (parent != null && parent != root) {
                        transform = parent.Transform * transform;
                        parent = parent.GetParent<Node3D>();
                    }
                }
                
                // Godot's AABB doesn't have a direct transform method that takes a Transform3D,
                // so we approximate by transforming the endpoints.
                var transformedAabb = transform * aabb;
                
                if (first)
                {
                    combined = transformedAabb;
                    first = false;
                }
                else
                {
                    combined = combined.Merge(transformedAabb);
                }
            }
        }
        return combined;
    }

    private void FindAllMeshes(Node node, System.Collections.Generic.List<MeshInstance3D> results)
    {
        if (node is MeshInstance3D mi)
            results.Add(mi);
            
        foreach (var child in node.GetChildren())
            FindAllMeshes(child, results);
    }

    private static Skeleton3D FindSkeleton(Node root)
    {
        if (root is Skeleton3D skel) return skel;
        foreach (var child in root.GetChildren())
        {
            var found = FindSkeleton(child);
            if (found != null) return found;
        }
        return null;
    }

    private void FixWeaponMaterials(Node node, string idfile, int depth = 0)
    {
        string indent = new string(' ', depth * 2);
        // GD.Print($"[MODEL] {indent}{node.GetType().Name}: '{node.Name}'");

        if (node is MeshInstance3D meshInst)
        {
            var mesh = meshInst.Mesh;
            // GD.Print($"[MODEL] {indent}  Mesh: {(mesh != null ? mesh.GetType().Name : "NULL")} surfaces={mesh?.GetSurfaceCount()}");
            meshInst.Visible = true;

            for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
            {
                var mat = meshInst.GetActiveMaterial(i);
                // GD.Print($"[MODEL] {indent}  Surface {i}: {mat?.GetType().Name} name={mat?.ResourceName}");

                if (mat is StandardMaterial3D stdMat)
                {
                    // Force visibility: no transparency, double-sided
                    var fixedMat = (StandardMaterial3D)stdMat.Duplicate();
                    fixedMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                    fixedMat.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
                    meshInst.SetSurfaceOverrideMaterial(i, fixedMat);
                    // GD.Print($"[MODEL] {indent}  Fixed material: cull=disabled, transparency=disabled, tex={fixedMat.AlbedoTexture?.ResourceName}");
                }
            }
        }

        foreach (var child in node.GetChildren())
        {
            FixWeaponMaterials(child, idfile, depth + 1);
        }
    }

    public void SetTargeted(bool targeted)
    {
        if (targeted)
        {
            if (_targetRing == null)
            {
                _targetRing = new MeshInstance3D { Name = "TargetRing" };
                var torus = new TorusMesh {
                    InnerRadius = 0.6f,
                    OuterRadius = 0.8f
                };
                var mat = new StandardMaterial3D {
                    AlbedoColor = new Color(1f, 0.3f, 0.3f, 0.8f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.2f, 0.2f),
                    EmissionEnergyMultiplier = 2f
                };
                torus.Material = mat;
                _targetRing.Mesh = torus;
                _targetRing.Position = new Vector3(0, 0.05f, 0);
                AddChild(_targetRing);
            }
            _targetRing.Visible = true;
        }
        else
        {
            if (_targetRing != null)
                _targetRing.Visible = false;
        }
    }

    public void SetSneakState(bool isSneaking, bool isLocalPlayer)
    {
        float alpha = 1.0f;
        if (isSneaking) {
            alpha = isLocalPlayer ? 0.6f : 0.15f;
        }

        if (_mesh != null) {
            ShaderMaterial mat = _mesh.MaterialOverride as ShaderMaterial;
            if (mat != null) {
                mat.SetShaderParameter("sneak_alpha", alpha);
            }
        }
        
        if (_nameLabel != null) {
            // Hide nametags of other sneaking characters
            if (isSneaking && !isLocalPlayer) {
                _nameLabel.Visible = false;
            } else {
                _nameLabel.Visible = true;
            }
        }
    }

    public void SetHideState(bool isHidden, bool isLocalPlayer)
    {
        float alpha = 1.0f;
        if (isHidden) {
            // Local: ghostly transparent. Remote: invisible (unless See Invis).
            alpha = isLocalPlayer ? 0.4f : 0.0f;
        }

        if (_mesh != null) {
            ShaderMaterial mat = _mesh.MaterialOverride as ShaderMaterial;
            if (mat != null) {
                mat.SetShaderParameter("sneak_alpha", alpha);
            }
        }

        if (_nameLabel != null) {
            if (isHidden && !isLocalPlayer) {
                _nameLabel.Visible = false;
            } else {
                _nameLabel.Visible = true;
            }
        }

        // Also hide the entire node for remote hidden players
        if (!isLocalPlayer) {
            Visible = !isHidden;
        }
    }

    // Recursively find an AnimationPlayer in the node tree
    private static AnimationPlayer FindAnimationPlayer(Node root)
    {
        if (root is AnimationPlayer ap) return ap;
        foreach (var child in root.GetChildren())
        {
            var found = FindAnimationPlayer(child);
            if (found != null) return found;
        }
        return null;
    }

    // ═══════ Light Source (Torch) ═════════════════════════════════════

    /// <summary>
    /// Show or hide a warm OmniLight3D on this entity to simulate an
    /// equipped torch, lantern, or lightstone. Called by WorldManager
    /// when the server vision payload includes hasLightSource.
    /// </summary>
    public void SetLightSource(bool hasLight)
    {
        _hasLightSource = hasLight;

        if (hasLight)
        {
            if (_torchLight == null)
            {
                _torchLight = new OmniLight3D
                {
                    Name = "TorchLight",
                    LightColor = new Color(1.0f, 0.8f, 0.45f),  // Warm torch orange
                    LightEnergy = 1.3f,
                    OmniRange = 12f,
                    OmniAttenuation = 1.4f,
                    ShadowEnabled = false,  // Performance: skip shadows for handheld lights
                    Position = new Vector3(0.4f, 1.6f, -0.3f),  // Right-hand height, slightly forward
                };
                AddChild(_torchLight);
            }
            _torchLight.Visible = true;
        }
        else
        {
            if (_torchLight != null)
                _torchLight.Visible = false;
        }
    }

    /// <summary>Show or hide this entity's overhead name label.</summary>
    public void SetNameVisible(bool visible)
    {
        if (_nameLabel != null)
            _nameLabel.Visible = visible;
    }

    /// <summary>Toggle visibility for first-person camera mode.</summary>
    public void SetFirstPersonMode(bool isFirstPerson)
    {
        if (_characterModel != null)
            _characterModel.Visible = !isFirstPerson;
        
        if (_mesh != null)
            _mesh.Visible = !isFirstPerson && _characterModel == null;
            
        if (_facingArrow != null)
            _facingArrow.Visible = !isFirstPerson && _characterModel == null;
    }

    // ═══════ Character Material Fix ═══════════════════════════════════

    /// <summary>
    /// Recursively fix all StandardMaterial3D surfaces on a loaded GLB
    /// character model so they don't reflect the sun/moon like chrome.
    /// EQ's original textures are hand-painted diffuse sprites — they
    /// should have zero metallic, low specular, and high roughness.
    /// </summary>
    private void FixCharacterMaterials(Node node)
    {
        if (node is MeshInstance3D meshInst)
        {
            for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
            {
                var mat = meshInst.GetActiveMaterial(i);
                if (mat is StandardMaterial3D stdMat)
                {
                    var fixedMat = (StandardMaterial3D)stdMat.Duplicate();
                    fixedMat.Metallic = 0.0f;
                    fixedMat.MetallicSpecular = 0.1f;
                    fixedMat.Roughness = 0.9f;
                    meshInst.SetSurfaceOverrideMaterial(i, fixedMat);
                }
            }
        }

        foreach (var child in node.GetChildren())
        {
            FixCharacterMaterials(child);
        }
    }

    public void SetInfravision(bool enabled)
    {
        if (_characterModel == null) return;
        SetInfravisionRecursive(_characterModel, enabled);
    }

    private void SetInfravisionRecursive(Node node, bool enabled)
    {
        if (node is MeshInstance3D meshInst)
        {
            for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
            {
                var mat = meshInst.GetSurfaceOverrideMaterial(i);
                if (mat is StandardMaterial3D stdMat)
                {
                    stdMat.EmissionEnabled = enabled;
                    if (enabled)
                    {
                        stdMat.Emission = new Color(0.8f, 0.1f, 0.05f); // Deep heat red
                        stdMat.EmissionEnergyMultiplier = 0.6f;
                    }
                }
            }
        }

        foreach (var child in node.GetChildren())
        {
            SetInfravisionRecursive(child, enabled);
        }
    }
}
