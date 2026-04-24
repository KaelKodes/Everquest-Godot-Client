using Godot;
using System;
using System.Linq;
using System.Text.Json;

public partial class EntityCapsule : CharacterBody3D
{
    private Label3D _nameLabel;
    private MeshInstance3D _mesh;
    private MeshInstance3D _targetRing;
    private MeshInstance3D _facingArrow;
    private AnimationPlayer _animPlayer;
    private string _currentAnim = "";

    public string EntityName { get; private set; } = "Entity";
    public string EntityType { get; private set; } = "enemy";

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
                Shape = new CapsuleShape3D { Radius = 0.5f, Height = 2.0f },
                Position = new Vector3(0, -2.1f, 0)
            };
            AddChild(col);
        }

        if (_nameLabel == null) {
            _nameLabel = new Label3D {
                Name = "NameLabel",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Text = "Entity",
                FontSize = 24,
                OutlineSize = 6,
                Position = new Vector3(0, 2.3f, 0)
            };
            AddChild(_nameLabel);
        }
        
        if (_mesh == null) {
            _mesh = new MeshInstance3D {
                Name = "Mesh",
                Position = new Vector3(0, 1.0f, 0)
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
        
        // Listen for standard Godot 3D mouse pick clicks
        this.InputEvent += OnInputEvent;
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
    public float ChaseSpeed { get; set; } = 5f;
    public float ChaseStopDistance { get; set; } = 3f; // stop just inside melee range
    public bool IsAggro => ChaseTarget != null;

    // Set to true for the player capsule — WorldManager handles its physics
    public bool IsPlayerControlled { get; set; } = false;

    // Play a named animation if available, with cross-fade
    public void PlayAnimation(string animName)
    {
        if (_animPlayer == null || _currentAnim == animName) return;
        if (_animPlayer.HasAnimation(animName))
        {
            // Set loop mode: death/social/cast anims play once, movement loops
            var anim = _animPlayer.GetAnimation(animName);
            if (animName.StartsWith("d") || animName.StartsWith("s") || animName.StartsWith("t"))
                anim.LoopMode = Animation.LoopModeEnum.None;
            else
                anim.LoopMode = Animation.LoopModeEnum.Linear;

            _animPlayer.Play(animName, 0.2); // 0.2s cross-fade
            _currentAnim = animName;
        }
    }

    // --- Animation state tracking ---
    private bool _isDead = false;
    private bool _isSitting = false;
    private double _emoteTimer = 0;
    private string _queuedEmote = null;
    private double _airborneTime = 0;

    // Called by WorldManager to update player animation based on movement
    public void UpdateAnimationFromVelocity(Vector3 velocity, bool isOnFloor = true, bool inCombat = false, bool isSprinting = false)
    {
        if (_animPlayer == null || _isDead) return;

        // If playing a timed emote/cast, let it finish
        if (_emoteTimer > 0) return;

        // If sitting, stay seated until movement breaks it
        float horizSpeed = new Vector2(velocity.X, velocity.Z).Length();
        if (_isSitting && horizSpeed < 0.5f && isOnFloor) return;
        if (_isSitting && horizSpeed > 0.5f) _isSitting = false;

        // Priority: jump/fall > run/walk > combat > idle
        if (!isOnFloor)
        {
            _airborneTime += 0.016; // ~1 frame at 60fps
            if (_airborneTime > 1.0 && velocity.Y < -5f)
            {
                // Been airborne > 1 second — real fall
                PlayAnimation("l05"); // falling
            }
            else if (horizSpeed > 0.5f)
            {
                PlayAnimation("l03"); // running jump (holds through arc)
            }
            else
            {
                PlayAnimation("l04"); // stationary jump (holds through arc)
            }
            _animPlayer.SpeedScale = 1.0f;
        }
        else
        {
            _airborneTime = 0;
            if (horizSpeed > 0.5f)
            {
                if (isSprinting)
                {
                    PlayAnimation("l02"); // running
                    _animPlayer.SpeedScale = Mathf.Clamp(horizSpeed / 30.0f, 0.8f, 1.5f);
                }
                else
                {
                    PlayAnimation("l01"); // walking
                    _animPlayer.SpeedScale = Mathf.Clamp(horizSpeed / 5.0f, 0.5f, 2.0f);
                }
            }
            else if (inCombat)
            {
                PlayAnimation("c05"); // 1h slash (default melee)
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
    }

    public void PlayDamage(bool heavy = false)
    {
        if (_isDead) return;
        // Quick flinch — play damage then resume (don't change _currentAnim tracking)
        if (_animPlayer != null && _animPlayer.HasAnimation(heavy ? "d02" : "d01"))
        {
            _animPlayer.Play(heavy ? "d02" : "d01", 0.1);
            _currentAnim = heavy ? "d02" : "d01";
        }
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
        _emoteTimer = 2.5; // let emote play for 2.5 seconds
        string animCode = emote.ToLower() switch
        {
            "cheer" => "s01",
            "disappointed" => "s02",
            "wave" => "s03",
            "rude" => "s04",
            "kick" => "t07",      // flying kick
            "tigerstrike" => "t08",
            "dragonpunch" => "t09",
            _ => null
        };
        if (animCode != null) PlayAnimation(animCode);
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

    public override void _PhysicsProcess(double delta)
    {
        // Tick down emote timer
        if (_emoteTimer > 0)
        {
            _emoteTimer -= delta;
            if (_emoteTimer <= 0)
            {
                _emoteTimer = 0;
                _currentAnim = ""; // force re-evaluation
            }
        }

        // Player movement is handled by WorldManager
        if (IsPlayerControlled) return;

        float gravity = 20f;
        var velocity = Velocity;

        // Apply gravity
        if (!IsOnFloor())
            velocity.Y -= gravity * (float)delta;
        else
            velocity.Y = 0;

        // Dead NPCs don't move
        if (_isDead)
        {
            velocity.X = 0;
            velocity.Z = 0;
            Velocity = velocity;
            MoveAndSlide();
            return;
        }

        // No chase target — just stand in place with gravity
        if (ChaseTarget == null)
        {
            velocity.X = 0;
            velocity.Z = 0;
            PlayAnimation("p01"); // idle
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
            PlayAnimation("c05"); // 1h slash attack in melee range
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    // EQ Race ID → model code mapping (classic races + NPC guard/citizen variants)
    private static readonly System.Collections.Generic.Dictionary<int, (string male, string female)> RaceModelMap = new()
    {
        // Classic player races (1-12)
        { 1, ("hum", "huf") },   // Human
        { 2, ("bam", "baf") },   // Barbarian
        { 3, ("erm", "erf") },   // Erudite
        { 4, ("elm", "elf") },   // Wood Elf
        { 5, ("him", "hif") },   // High Elf
        { 6, ("dam", "daf") },   // Dark Elf
        { 7, ("ham", "haf") },   // Half Elf
        { 8, ("dwm", "dwf") },   // Dwarf
        { 9, ("trm", "trf") },   // Troll
        { 10, ("ogm", "ogf") },  // Ogre
        { 11, ("hom", "hof") },  // Halfling
        { 12, ("gnm", "gnf") },  // Gnome
        { 60, ("ske", "ske") },  // Skeleton
        { 128, ("ikm", "ikf") }, // Iksar
        // City guard/citizen races → mapped to their racial equivalents
        { 44, ("hum", "huf") },  // Freeport Guards → Human
        { 55, ("hum", "huf") },  // Human Beggar → Human
        { 67, ("hum", "huf") },  // Highpass Citizen → Human
        { 71, ("hum", "huf") },  // Qeynos Citizen → Human
        { 77, ("dam", "daf") },  // Neriak Citizen → Dark Elf
        { 78, ("erm", "erf") },  // Erudite Citizen → Erudite
        { 81, ("hom", "hof") },  // Rivervale Citizen → Halfling
        { 88, ("gnm", "gnf") },  // Clockwork Gnome → Gnome
        { 90, ("bam", "baf") },  // Halas Citizen → Barbarian
        { 92, ("trm", "trf") },  // Grobb Citizen → Troll
        { 93, ("ogm", "ogf") },  // Oggok Citizen → Ogre
        { 94, ("dwm", "dwf") },  // Kaladim Citizen → Dwarf
        { 106, ("him", "hif") }, // Felguard → High Elf (Felwithe guards)
        { 112, ("elm", "elf") }, // Fayguard → Wood Elf (Kelethin guards)
        { 139, ("ikm", "ikf") }, // Iksar Citizen → Iksar
    };

    // Race-specific scale multipliers (human = 1.0 baseline)
    // Based on canonical EQ race sizes normalized to human size (~6)
    private static float GetRaceScale(int race)
    {
        return race switch
        {
            1 => 1.0f,    // Human
            2 => 1.17f,   // Barbarian (tall/beefy)
            3 => 1.0f,    // Erudite (human-sized)
            4 => 0.88f,   // Wood Elf (slightly shorter)
            5 => 1.0f,    // High Elf (human-sized)
            6 => 0.88f,   // Dark Elf (slightly shorter)
            7 => 0.92f,   // Half Elf (between human/elf)
            8 => 0.67f,   // Dwarf (short/stocky)
            9 => 1.33f,   // Troll (tall)
            10 => 1.5f,   // Ogre (huge)
            11 => 0.58f,  // Halfling (small)
            12 => 0.5f,   // Gnome (smallest)
            128 => 1.0f,  // Iksar (human-sized)
            // City guards/citizens inherit their racial scale
            44 => 1.0f,   // Freeport Guards
            55 => 1.0f,   // Human Beggar
            67 => 1.0f,   // Highpass Citizen
            71 => 1.0f,   // Qeynos Citizen
            77 => 0.88f,  // Neriak Citizen (Dark Elf)
            78 => 1.0f,   // Erudite Citizen
            81 => 0.58f,  // Rivervale Citizen (Halfling)
            88 => 0.5f,   // Clockwork Gnome
            90 => 1.17f,  // Halas Citizen (Barbarian)
            92 => 1.33f,  // Grobb Citizen (Troll)
            93 => 1.5f,   // Oggok Citizen (Ogre)
            94 => 0.67f,  // Kaladim Citizen (Dwarf)
            106 => 1.0f,  // Felguard (High Elf)
            112 => 0.88f, // Fayguard (Wood Elf)
            139 => 1.0f,  // Iksar Citizen
            _ => 1.0f,    // Default to human scale
        };
    }

    private Node3D _characterModel;

    public void Setup(string name, string type, string appearanceJson = "", int race = 1, int gender = 0)
    {
        // Setup nodes if Setup is called before _Ready
        if (_nameLabel == null || _mesh == null) _Ready();

        EntityName = name;
        EntityType = type;
        _nameLabel.Text = name;

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
        }

        _nameLabel.Modulate = nameColor;
        
        if (_facingArrow != null && _facingArrow.Mesh is PrismMesh pm && pm.Material is StandardMaterial3D sam) {
            sam.AlbedoColor = nameColor;
            sam.Emission = nameColor;
        }

        // Try to load actual character model GLB
        bool modelLoaded = false;
        if (RaceModelMap.TryGetValue(race, out var codes))
        {
            string modelCode = (gender == 1) ? codes.female : codes.male;
            string modelPath = $"res://Data/Characters/{modelCode}.glb";

            if (ResourceLoader.Exists(modelPath))
            {
                try
                {
                    var scene = GD.Load<PackedScene>(modelPath);
                    if (scene != null)
                    {
                        _characterModel = scene.Instantiate<Node3D>();
                        // Rotate +90° Y: EQ models face +X, Godot expects -Z forward
                        _characterModel.RotationDegrees = new Vector3(0, 90f, 0);
                        _characterModel.Position = new Vector3(0, 0, 0);
                        // Apply race-specific scale (human = 1.0 baseline)
                        float raceScale = GetRaceScale(race);
                        _characterModel.Scale = new Vector3(raceScale, raceScale, raceScale);
                        // Scale collision capsule to match model proportions
                        var col = GetNodeOrNull<CollisionShape3D>("Collision");
                        if (col != null) {
                            col.Position = new Vector3(0, -2.1f * raceScale, 0);
                            col.Shape = new CapsuleShape3D {
                                Radius = 0.5f * raceScale,
                                Height = 2.0f * raceScale
                            };
                        }
                        AddChild(_characterModel);
                        // Hide placeholder visuals (capsule + facing arrow) — model has its own face
                        if (_mesh != null) _mesh.Visible = false;
                        if (_facingArrow != null) _facingArrow.Visible = false;
                        modelLoaded = true;


                        // Store AnimationPlayer reference and start idle
                        _animPlayer = FindAnimationPlayer(_characterModel);
                        PlayAnimation("p01");
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
            GD.Print($"[MODEL] No race mapping for '{name}' (race={race} gender={gender}), using capsule");
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
}
