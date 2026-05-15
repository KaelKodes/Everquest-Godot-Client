using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class MainMenu : Control
{
    // ── Panel References ──────────────────────────────────────────
    private Control _titlePanel;
    private Control _loginPanel;
    private Control _charSelectPanel;
    private Control _createPanel;
    private Label _statusLabel;

    // ── 3D Menu Backdrop ──────────────────────────────────────────
    private SubViewportContainer _backdropContainer;
    private SubViewport _backdropViewport;
    private Node3D _backdropRoot;
    private Camera3D _backdropCamera;
    private string _currentBackdropZone = null;

    // ── Login Panel Nodes ─────────────────────────────────────────
    private LineEdit _usernameInput;
    private LineEdit _passwordInput;
    private Button _loginButton;
    private Button _createAccountButton;
    private Label _loginStatusLabel;
    private Label _loginServerLabel;
    private Button _loginBackToServerSelectButton;
    private Button _eqDirButton;
    private CheckBox _rememberCheckbox;
    private LineEdit _serverAddressInput; // Removed from UI, kept as internal ref
    private string _currentServerName = "ALPHA";
    private static readonly Dictionary<string, string> ServerConfigs = new() {
        { "ALPHA", "ws://localhost:3005" }
    };
    private string _pendingAction = null;

    // ── Character Select Panel Nodes ──────────────────────────────
    private VBoxContainer _charListContainer;
    private Button _enterWorldButton;
    private Button _newCharButton;
    private Button _deleteCharButton;
    private Button _returnHomeButton;
    private Button _quitButton;
    private CheckBox _resetUICheckbox;
    private Label _charSelectHeader;
    private string _selectedCharName = null;

    // Character Select 3D Preview
    private SubViewportContainer _charSelectPreviewContainer;
    private SubViewport _charSelectPreviewViewport;
    private Node3D _charSelectPreviewRoot;
    private Node3D _charSelectPreviewModel;
    private Camera3D _charSelectPreviewCamera;
    private int _selectedCharRaceId = 1;
    private int _selectedCharGender = 0;
    private int _selectedCharFace = 0;

    // ── Create Panel Nodes ───────────────────────────────────────
    private LineEdit _nameInput;
    private OptionButton _raceSelect;
    private OptionButton _classSelect;
    private OptionButton _deitySelect;
    private Button _playButton;
    private RichTextLabel _classDescription;
    private Button _backToSelectButton;

    // Stat allocation UI
    private Label _previewStr, _previewSta, _previewAgi, _previewDex, _previewWis, _previewInt, _previewCha;
    private Label _previewHp, _previewMana;
    private Label _pointsRemainingLabel;
    private Button[] _statPlusBtns = new Button[7];
    private Button[] _statMinusBtns = new Button[7];

    // ── Character Creation Data (from DB) ────────────────────────
    private JsonElement _currentCreateData; // Full CHAR_CREATE_DATA response
    private int _currentRaceId = 1;
    private int[] _currentClassIds = new int[0];
    private int[] _currentDeityIds = new int[0];

    // Stat allocation tracking
    private int[] _baseStats = new int[7];   // base_str, base_sta, base_agi, base_dex, base_wis, base_int, base_cha
    private int[] _allocStats = new int[7];  // player-allocated bonus points
    private int _totalPool = 0;              // total distributable points

    // Appearance customization
    private int _selectedGender = 0;         // 0=Male, 1=Female
    private int _selectedFace = 0;           // Face index (0-based)
    private int _faceCountMale = 1;          // Faces available for male
    private int _faceCountFemale = 1;        // Faces available for female
    private Button _genderMaleBtn, _genderFemaleBtn;
    private Button _facePrevBtn, _faceNextBtn;
    private Label _faceLabel;

    // Armor material preview
    private int _selectedArmorMaterial = 0;
    private Button _armorPrevBtn, _armorNextBtn;
    private Label _armorLabel;
    private static readonly string[] ArmorMaterialNames = { "Cloth", "Leather", "Chain", "Plate" };

    // 3D Model Preview
    private SubViewportContainer _previewContainer;
    private SubViewport _previewViewport;
    private Node3D _previewRoot;
    private Node3D _previewModel;
    private Camera3D _previewCamera;
    private float _previewRotation = 0f;
    private float _previewZoom = 12.0f;
    private float _previewZoomMin = 2.0f;
    private float _previewZoomMax = 28.0f;
    private float _previewCamHeight = 1.0f;
    private float _previewLookAtY = 0.7f;
    private bool _previewAutoRotate = true;
    private CheckBox _rotateCheckbox;

    // Networking
    private bool _waitingForLogin = false;

    // Race data — Iksar hidden from creation until full-body skin / face pipeline matches EQ (face slot swaps whole skin).
    private static readonly string[] RaceNames = {
        "Human", "Barbarian", "Erudite", "Wood Elf", "High Elf",
        "Dark Elf", "Half Elf", "Dwarf", "Troll", "Ogre",
        "Halfling", "Gnome"
    };
    private static readonly int[] RaceIds = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

    // All 16 classes (display names → keys)
    private static readonly Dictionary<int, string> ClassDisplayNames = new() {
        {1, "Warrior"}, {2, "Cleric"}, {3, "Paladin"}, {4, "Ranger"},
        {5, "Shadow Knight"}, {6, "Druid"}, {7, "Monk"}, {8, "Bard"},
        {9, "Rogue"}, {10, "Shaman"}, {11, "Necromancer"}, {12, "Wizard"},
        {13, "Magician"}, {14, "Enchanter"}, {15, "Beastlord"}, {16, "Berserker"}
    };
    private static readonly Dictionary<int, string> ClassKeys = new() {
        {1, "warrior"}, {2, "cleric"}, {3, "paladin"}, {4, "ranger"},
        {5, "shadow_knight"}, {6, "druid"}, {7, "monk"}, {8, "bard"},
        {9, "rogue"}, {10, "shaman"}, {11, "necromancer"}, {12, "wizard"},
        {13, "magician"}, {14, "enchanter"}, {15, "beastlord"}, {16, "berserker"}
    };

    // Class descriptions
    private static readonly Dictionary<int, string> ClassDescriptions = new() {
        {1, "[color=#dd8833]WARRIOR[/color]\n\nMasters of melee combat. Warriors boast the highest HP and can wear any armor. They lack magic but make up for it with raw power and durability.\n\n[color=#888888]• No mana pool\n• Highest base HP\n• Can use all weapons and armor\n• Abilities: Kick, Bash, Taunt[/color]"},
        {2, "[color=#dd8833]CLERIC[/color]\n\nDivine healers and protectors. Clerics keep groups alive with powerful healing spells and protective buffs.\n\n[color=#888888]• Moderate HP, moderate mana\n• Best healing spells in the game\n• Protective buffs (AC, HP)\n• Can wear plate armor[/color]"},
        {3, "[color=#dd8833]PALADIN[/color]\n\nHoly knights with divine magic. Paladins are resilient tanks who can heal and buff allies while wielding holy swords.\n\n[color=#888888]• Can cast Cleric spells\n• Strong against undead\n• Lay on Hands ability\n• Can wear plate armor[/color]"},
        {4, "[color=#dd8833]RANGER[/color]\n\nMasters of archery and nature magic. Rangers excel at ranged combat, tracking, and wilderness survival.\n\n[color=#888888]• Can cast Druid spells\n• Specializes in bows\n• Tracking ability\n• Can dual wield[/color]"},
        {5, "[color=#dd8833]SHADOW KNIGHT[/color]\n\nDark knights empowered by dark magic. Shadow Knights use disease, poison, and lifetaps to drain their foes.\n\n[color=#888888]• Can cast Necromancer spells\n• Harm Touch ability\n• Lifetaps and DoTs\n• Can wear plate armor[/color]"},
        {6, "[color=#dd8833]DRUID[/color]\n\nWardens of nature. Druids are excellent soloers who rely on damage shields, rooting, and potent DoTs.\n\n[color=#888888]• Ports and Runspeed buffs\n• Strong DoT spells\n• Animal charming\n• Moderate healing[/color]"},
        {7, "[color=#dd8833]MONK[/color]\n\nMartial arts masters. Monks forgo heavy weapons and armor in favor of raw hand-to-hand combat and evasion.\n\n[color=#888888]• No mana pool\n• Feign Death ability\n• Exceptional dodge and block\n• Restricted to leather/cloth[/color]"},
        {8, "[color=#dd8833]BARD[/color]\n\nMusical jacks-of-all-trades. Bards weave magical songs to increase run speed, regenerate mana, or charm enemies.\n\n[color=#888888]• Sings continually active songs\n• Unmatched runspeed\n• Group wide buffs\n• Capable melee fighters[/color]"},
        {9, "[color=#dd8833]ROGUE[/color]\n\nSneaky assassins who strike from the shadows. Rogues deal high burst damage with backstab.\n\n[color=#888888]• No mana pool\n• High AGI and DEX\n• Backstab for burst damage\n• Sneak and Hide[/color]"},
        {10, "[color=#dd8833]SHAMAN[/color]\n\nTribal spiritualists. Shamans are masters of buffing, debuffing, and regeneration.\n\n[color=#888888]• Best buffs in the game (SOW)\n• Cannibalize magic\n• Slow and debuffs\n• Can wear chain armor[/color]"},
        {11, "[color=#dd8833]NECROMANCER[/color]\n\nMasters of death and disease. Necromancers summon undead pets and drain life from their foes.\n\n[color=#888888]• Undead Pet summoning\n• Feign Death and Lifetaps\n• Best soloing capabilities\n• Cloth armor only[/color]"},
        {12, "[color=#dd8833]WIZARD[/color]\n\nMasters of destructive magic. Wizards deal devastating ranged damage but are fragile.\n\n[color=#888888]• Lowest HP, highest mana\n• Powerful direct damage spells\n• Teleportation abilities\n• Cloth armor only[/color]"},
        {13, "[color=#dd8833]MAGICIAN[/color]\n\nElemental summoners. Magicians summon weapons, armor, and powerful elemental pets.\n\n[color=#888888]• Summons Fire/Earth/Water/Air pets\n• Summons items and food\n• Strong direct damage spells\n• Cloth armor only[/color]"},
        {14, "[color=#dd8833]ENCHANTER[/color]\n\nMasters of the mind. Enchanters manipulate enemy aggression, slow attacks, and provide mana regeneration.\n\n[color=#888888]• Mana Regen buffs (Breeze/Clarity)\n• Mesmerize and Charm\n• Illusions\n• Cloth armor only[/color]"},
        {15, "[color=#dd8833]BEASTLORD[/color]\n\nSpiritual warriors who bond with primal warder pets. Beastlords combine martial arts with shamanic magic.\n\n[color=#888888]• Permanent warder pet\n• Primal magic buffs\n• Moderate melee damage\n• Can wear leather armor[/color]"},
        {16, "[color=#dd8833]BERSERKER[/color]\n\nFrenzied warriors who enter a battle rage. Berserkers deal massive damage with two-handed weapons.\n\n[color=#888888]• Frenzy abilities\n• Thrown weapon specialization\n• High damage output\n• Can wear chain armor[/color]"},
    };

    // Deity display names
    private static readonly Dictionary<int, string> DeityNames = new() {
        {201, "Bertoxxulous"}, {202, "Brell Serilis"}, {203, "Cazic-Thule"}, {204, "Erollisi Marr"},
        {205, "Bristlebane"}, {206, "Innoruuk"}, {207, "Karana"}, {208, "Mithaniel Marr"},
        {209, "Prexus"}, {210, "Quellious"}, {211, "Rallos Zek"}, {212, "Rodcet Nife"},
        {213, "Solusek Ro"}, {214, "The Tribunal"}, {215, "Tunare"}, {216, "Veeshan"},
        {396, "Agnostic"}
    };

    // Stat labels for iteration
    private static readonly string[] StatNames = { "STR", "STA", "AGI", "DEX", "WIS", "INT", "CHA" };

    // Stat descriptions shown when clicking a stat name (sourced from P99 wiki + Bonzz)
    private static readonly Dictionary<int, string> StatDescriptions = new() {
        {0, "[color=#dd8833]STRENGTH (STR)[/color]\n\nAffects: Attack Power, Weight Limit, Bow Damage, Shield AC\nHard Cap: 255\n\nStrength determines how much you can carry and influences maximum and average melee and bow damage. It also improves the AC bonus granted while using a shield. A valuable stat for melee and tank characters.\n\n[color=#888888]• Increases max and average melee hit\n• Determines carry weight capacity\n• Improves shield AC bonus\n• Influences offensive skill learning speed\n• Overweight = encumbered (snared)[/color]"},
        {1, "[color=#dd8833]STAMINA (STA)[/color]\n\nAffects: Hit Points, HP Regen, Endurance, Breath\nHard Cap: 255\n\nStamina directly affects how many hit points you have and your HP regeneration rate. Tanks (Warriors, Shadow Knights, Paladins) gain the most HP per point. HP per STA scales with level.\n\n[color=#888888]• Primary HP stat — essential for tanks\n• Increases HP regeneration\n• Minor HP benefit for non-tank classes\n• Affects breath duration underwater\n• Rule of thumb: 1 AC ≈ 6 HP[/color]"},
        {2, "[color=#dd8833]AGILITY (AGI)[/color]\n\nAffects: Avoidance AC, Dodge, Defense, Endurance, Fall Damage\nHard Cap: 255\n\nAgility influences your ability to avoid being hit and reduces falling damage. Below 75 AGI, you take a massive AC penalty. Also increases your Endurance pool and regen.\n\n[color=#888888]• Critical threshold at 75 AGI\n• Below 75: huge AC penalty (~45 AC lost)\n• Affects Dodge/Defense/Parry skill rates\n• Reduces falling damage\n• Below 25% HP, AGI takes a penalty[/color]"},
        {3, "[color=#dd8833]DEXTERITY (DEX)[/color]\n\nAffects: Procs, Crits, Riposte, Block, Parry, Ranged Damage\nHard Cap: 255\n\nDexterity determines how quickly you learn weapon skills, how often weapons proc, and improves your chance to riposte, block, and parry attacks. Also increases ranged attack damage.\n\n[color=#888888]• Increases weapon proc rate\n• Improves riposte/block/parry chance\n• Warrior/Ranger crit chance\n• Increases ranged (bow/throwing) damage\n• Bard song fizzle reduction[/color]"},
        {4, "[color=#dd8833]WISDOM (WIS)[/color]\n\nAffects: Mana pool for Priests, Mana Regen\nHard Cap: 255 | Soft Cap: 200\n\nWisdom raises maximum mana, mana regeneration, and magic skill-up speed for priest classes: Cleric, Druid, Shaman, Paladin, and Ranger.\n\n[color=#888888]• Primary mana stat for priest classes\n• Cleric, Druid, Shaman, Paladin, Ranger\n• Diminishing returns above 200\n• Also affects mana regen rate\n• Affects skill-up rate if > INT[/color]"},
        {5, "[color=#dd8833]INTELLIGENCE (INT)[/color]\n\nAffects: Mana pool for Casters, Mana Regen\nHard Cap: 255 | Soft Cap: 200\n\nIntelligence raises maximum mana, mana regeneration, and spell skill-up speed for caster classes: Necromancer, Magician, Enchanter, Wizard, Shadow Knight, and Bard.\n\n[color=#888888]• Primary mana stat for caster classes\n• Necro, Mage, Enchanter, Wizard, SK, Bard\n• Diminishing returns above 200\n• Also affects mana regen rate\n• Affects skill-up rate if > WIS[/color]"},
        {6, "[color=#dd8833]CHARISMA (CHA)[/color]\n\nAffects: Charm duration, Vendor prices, Lull, Memory Blur\nHard Cap: 255 | Soft Cap: 200\n\nCharisma affects merchant buy/sell prices (caps around 104-125), Charm spell duration for Enchanters and Bards, Lull aggro checks, and memory blur success. Does NOT affect Druid/Necro charm.\n\n[color=#888888]• Better vendor prices (caps ~104-125 CHA)\n• Longer Charm duration (ENC/BRD only)\n• Improved Lull success rate\n• Better memory blur chance\n• 5% chance per tick for charm to break regardless[/color]"},
    };

    // ═══════════════════════════════════════════════════════════════
    //  READY
    // ═══════════════════════════════════════════════════════════════

    public override async void _Ready()
    {
        // ── Title Panel ──
        _titlePanel = GetNode<Control>("TitlePanel");

        // ── Build Server Select Panel programmatically ──
        BuildServerSelectPanel();

        // ── Build Login Panel programmatically ──
        BuildLoginPanel();

        // ── Build Character Select Panel programmatically ──
        BuildCharSelectPanel();

        // ── Build Create Panel programmatically (replacing .tscn version) ──
        BuildCreatePanel();

        // ── Build 3D Backdrop ──
        BuildMenuBackdrop();
        _ = LoadBackdropZone("mistmoore", new Vector3(2.1690567f, -194.32549f, -204.49268f), new Vector3(15.469843f, 178.62137f, 0f));

        // Hide all panels initially — ShowPanel() picks the right one below
        _createPanel.Hide();
        _charSelectPanel.Hide();
        _loginPanel.Hide();
        if (_serverSelectPanel != null) _serverSelectPanel.Hide();

        // Listen to signals
        GameClient.Instance.Connected += OnClientConnected;
        GameClient.Instance.Disconnected += OnClientDisconnected;
        GameClient.Instance.AccountOkReceived += OnAccountOkReceived;
        GameClient.Instance.CharacterCreated += OnCharacterCreated;
        GameClient.Instance.CharacterDeleted += OnCharacterDeleted;
        GameClient.Instance.CharCreateDataReceived += OnCharCreateDataReceived;
        GameClient.Instance.LoginOkReceived += OnLoginOkReceived;
        GameClient.Instance.MessageReceived += OnClientMessageReceived;

        // Check if we're creating a student (skip server select — already connected)
        if (GameState.IsCreatingStudent)
        {
            GD.Print("[MENU] Entering Student Creation mode.");
            _nameInput.Text = GameState.PendingStudentName;
            _playButton.Text = "Hire Student";
            
            // Pre-select race
            for (int i = 0; i < RaceIds.Length; i++) {
                if (RaceIds[i] == GameState.PendingStudentRaceId) {
                    _raceSelect.Select(i);
                    break;
                }
            }
            _raceSelect.Disabled = true;
            
            ShowPanel("create");
            return;
        }

        // Check if we're returning from camp (already connected + authenticated → skip server select)
        if (GameClient.Instance.IsSocketConnected && !string.IsNullOrEmpty(GameState.AccountName))
        {
            GD.Print("[MENU] Returning from camp — re-requesting character list.");
            // Re-login with saved credentials to refresh character list
            LoadSavedCredentials();
            string username = _usernameInput.Text.Trim();
            string password = _passwordInput.Text;
            if (username.Length >= 2 && password.Length >= 4)
            {
                if (!EQAssetConfig.Instance.IsConfigured)
                {
                    _loginStatusLabel.Text = "Link your EverQuest installation before logging in.";
                    ShowPanel("login");
                    return;
                }
                GameState.AccountPassword = password;
                GameClient.Instance.SendMessage("LOGIN_ACCOUNT", new { username, password });
            }
            else
            {
                // Fallback: show login screen
                ShowPanel("login");
            }
            return;
        }

        // ── Fresh start: server select first, then login on the chosen server ──
        _loginStatusLabel.Text = "Enter credentials.";
        // Pre-fill saved values so credentials/server address are ready when
        // the user reaches the login panel.
        LoadSavedCredentials();
        ShowPanel("serverselect");
    }

    public override void _ExitTree()
    {
        if (GameClient.Instance != null)
        {
            GameClient.Instance.Connected -= OnClientConnected;
            GameClient.Instance.Disconnected -= OnClientDisconnected;
            GameClient.Instance.AccountOkReceived -= OnAccountOkReceived;
            GameClient.Instance.CharacterCreated -= OnCharacterCreated;
            GameClient.Instance.CharacterDeleted -= OnCharacterDeleted;
            GameClient.Instance.CharCreateDataReceived -= OnCharCreateDataReceived;
            GameClient.Instance.LoginOkReceived -= OnLoginOkReceived;
            GameClient.Instance.MessageReceived -= OnClientMessageReceived;
        }
        ShutdownServerProbes();
        base._ExitTree();
    }

    // ═══════════════════════════════════════════════════════════════
    //  BUILD LOGIN PANEL
    // ═══════════════════════════════════════════════════════════════

    private void BuildLoginPanel()
    {
        _loginPanel = new PanelContainer();
        _loginPanel.Name = "LoginPanel";
        var loginStyle = new StyleBoxFlat();
        loginStyle.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.25f);
        loginStyle.BorderWidthLeft = loginStyle.BorderWidthTop = loginStyle.BorderWidthRight = loginStyle.BorderWidthBottom = 2;
        loginStyle.BorderColor = new Color(0.7f, 0.55f, 0.2f, 0.8f);
        loginStyle.CornerRadiusTopLeft = loginStyle.CornerRadiusTopRight = loginStyle.CornerRadiusBottomLeft = loginStyle.CornerRadiusBottomRight = 6;
        loginStyle.ShadowColor = new Color(0, 0, 0, 0.6f);
        loginStyle.ShadowSize = 12;
        (_loginPanel as PanelContainer).AddThemeStyleboxOverride("panel", loginStyle);

        _loginPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _loginPanel.OffsetLeft = -200;
        _loginPanel.OffsetRight = 200;
        _loginPanel.OffsetTop = -150;
        _loginPanel.OffsetBottom = 150;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        vbox.OffsetLeft = 24; vbox.OffsetTop = 24; vbox.OffsetRight = -24; vbox.OffsetBottom = -24;
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);

        // Header
        var header = new Label();
        header.Text = "Account Login";
        header.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddThemeFontSizeOverride("font_size", 22);
        header.AddThemeColorOverride("font_color", new Color(0.85f, 0.7f, 0.25f, 1f));
        vbox.AddChild(header);

        // Selected-server breadcrumb — updated by ShowPanel("login")
        _loginServerLabel = new Label();
        _loginServerLabel.Text = "";
        _loginServerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _loginServerLabel.AddThemeFontSizeOverride("font_size", 12);
        _loginServerLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.7f, 0.55f, 1f));
        vbox.AddChild(_loginServerLabel);

        // "← Server Select" link button so users can swap servers without restarting.
        _loginBackToServerSelectButton = new Button();
        _loginBackToServerSelectButton.Text = "← Server Select";
        _loginBackToServerSelectButton.Flat = true;
        _loginBackToServerSelectButton.AddThemeFontSizeOverride("font_size", 11);
        _loginBackToServerSelectButton.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.7f, 1f));
        _loginBackToServerSelectButton.Pressed += OnBackToServerSelectPressed;
        vbox.AddChild(_loginBackToServerSelectButton);

        // Username
        var userLabel = new Label();
        userLabel.Text = "Account Name";
        userLabel.HorizontalAlignment = HorizontalAlignment.Center;
        userLabel.AddThemeFontSizeOverride("font_size", 13);
        userLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        vbox.AddChild(userLabel);

        _usernameInput = new LineEdit();
        _usernameInput.PlaceholderText = "Enter account name...";
        _usernameInput.Alignment = HorizontalAlignment.Center;
        _usernameInput.MaxLength = 30;
        _usernameInput.AddThemeFontSizeOverride("font_size", 15);
        vbox.AddChild(_usernameInput);

        // Password
        var passLabel = new Label();
        passLabel.Text = "Password";
        passLabel.HorizontalAlignment = HorizontalAlignment.Center;
        passLabel.AddThemeFontSizeOverride("font_size", 13);
        passLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        vbox.AddChild(passLabel);

        _passwordInput = new LineEdit();
        _passwordInput.PlaceholderText = "Enter password...";
        _passwordInput.Alignment = HorizontalAlignment.Center;
        _passwordInput.Secret = true;
        _passwordInput.AddThemeFontSizeOverride("font_size", 15);
        vbox.AddChild(_passwordInput);

        // Remember Me checkbox
        _rememberCheckbox = new CheckBox();
        _rememberCheckbox.Text = "Remember Me";
        _rememberCheckbox.Alignment = HorizontalAlignment.Center;
        _rememberCheckbox.AddThemeFontSizeOverride("font_size", 13);
        _rememberCheckbox.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        vbox.AddChild(_rememberCheckbox);

        // Server Address — chosen on the Server Select screen now, so the
        // visible row is hidden. We keep the LineEdit around because the
        // existing connection logic (ValidateInputs / InitiateConnection)
        // reads its .Text to pass the URL through.
        var serverLabel = new Label();
        serverLabel.Text = "Server Address";
        serverLabel.HorizontalAlignment = HorizontalAlignment.Center;
        serverLabel.AddThemeFontSizeOverride("font_size", 13);
        serverLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        serverLabel.Visible = false;
        vbox.AddChild(serverLabel);

        _serverAddressInput = new LineEdit();
        _serverAddressInput.PlaceholderText = "ws://localhost:3005";
        _serverAddressInput.Text = "ws://localhost:3005";
        _serverAddressInput.Alignment = HorizontalAlignment.Center;
        _serverAddressInput.AddThemeFontSizeOverride("font_size", 15);
        _serverAddressInput.Visible = false;
        vbox.AddChild(_serverAddressInput);

        // Login button
        _loginButton = new Button();
        _loginButton.Text = "Login";
        _loginButton.CustomMinimumSize = new Vector2(0, 44);
        _loginButton.AddThemeFontSizeOverride("font_size", 18);
        _loginButton.Pressed += OnLoginPressed;
        vbox.AddChild(_loginButton);

        // Create account button
        _createAccountButton = new Button();
        _createAccountButton.Text = "Create New Account";
        _createAccountButton.CustomMinimumSize = new Vector2(0, 36);
        _createAccountButton.AddThemeFontSizeOverride("font_size", 14);
        _createAccountButton.Pressed += OnCreateAccountPressed;
        vbox.AddChild(_createAccountButton);

        // ── EQ Directory button ──
        _eqDirButton = new Button();
        _eqDirButton.CustomMinimumSize = new Vector2(0, 32);
        _eqDirButton.AddThemeFontSizeOverride("font_size", 12);
        UpdateEQButtonText();
        _eqDirButton.Pressed += () => ShowEQSetupDialog();
        vbox.AddChild(_eqDirButton);

        // Status
        _loginStatusLabel = new Label();
        _loginStatusLabel.Text = "";
        _loginStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _loginStatusLabel.AddThemeFontSizeOverride("font_size", 12);
        _loginStatusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f, 1f));
        vbox.AddChild(_loginStatusLabel);

        (_loginPanel as PanelContainer).AddChild(vbox);
        AddChild(_loginPanel);

        // Load saved credentials
        LoadSavedCredentials();
    }

    // ── EQ Directory helpers ──

    private const string EqLegalDisclaimerText =
        "EQ.gd is a fan-made, non-profit emulator-style client. It does not ship EverQuest game data. " +
        "You must own and install your own legal copy of EverQuest (for example via Steam or Daybreak). " +
        "Please support the original developers at everquest.com.";

    private void UpdateEQButtonText(Button btn = null)
    {
        var target = btn ?? _eqDirButton;
        if (target == null) return;
        if (EQAssetConfig.Instance.IsConfigured)
        {
            target.Text = "⚙ EQ Linked ✓";
            target.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 0.4f, 1f));
        }
        else
        {
            target.Text = "⚙ Link EQ Directory";
            target.AddThemeColorOverride("font_color", new Color(0.9f, 0.7f, 0.2f, 1f));
        }
    }

    private void PopupFolderPicker(Window host, string title, Action<string> onSelected)
    {
        var fd = new FileDialog();
        fd.FileMode = FileDialog.FileModeEnum.OpenDir;
        fd.Access = FileDialog.AccessEnum.Filesystem;
        fd.Title = title;
        fd.Exclusive = false;
        host.AddChild(fd);
        fd.PopupCentered(new Vector2I(880, 620));
        fd.DirSelected += (dir) =>
        {
            onSelected?.Invoke(dir);
            if (GodotObject.IsInstanceValid(fd))
                fd.QueueFree();
        };
        fd.Canceled += () =>
        {
            if (GodotObject.IsInstanceValid(fd))
                fd.QueueFree();
        };
    }

    /// <summary>Shared path row + link/unlink used by the login settings dialog and the mandatory post-server-select gate.</summary>
    /// <param name="fileDialogParent">Parent for folder picker (avoids exclusive-window conflict with this dialog).</param>
    /// <param name="includeTradeskillOptions">When false (first-launch gate), only EQ install path is shown — tradeskill/EQ Sage is optional in settings.</param>
    private void AddEverQuestLinkControls(VBoxContainer vbox, Button eqDirButtonForUpdates, Action onLinkStateChanged, Window fileDialogParent = null, bool includeTradeskillOptions = true)
    {
        var statusLabel = new Label();
        statusLabel.AddThemeFontSizeOverride("font_size", 13);
        statusLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        if (EQAssetConfig.Instance.IsConfigured)
        {
            statusLabel.Text = $"✓ EQ Linked: {EQAssetConfig.Instance.EQPath}";
            statusLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 0.4f, 1f));
        }
        else
        {
            statusLabel.Text = "Link your EverQuest installation to enable 3D zones, objects, and music.\nYou can download EQ free from Steam or daybreakgames.com.";
            statusLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.5f, 1f));
        }
        vbox.AddChild(statusLabel);

        var pathRow = new HBoxContainer();
        pathRow.AddThemeConstantOverride("separation", 6);
        var pathInput = new LineEdit();
        pathInput.PlaceholderText = "C:\\EverQuest or D:\\EQ...";
        pathInput.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pathInput.Text = EQAssetConfig.Instance.EQPath;
        pathInput.AddThemeFontSizeOverride("font_size", 13);
        pathRow.AddChild(pathInput);

        var eqBrowse = new Button();
        eqBrowse.Text = "Browse…";
        eqBrowse.CustomMinimumSize = new Vector2(88, 32);
        eqBrowse.AddThemeFontSizeOverride("font_size", 13);
        eqBrowse.Pressed += () =>
        {
            Window host = fileDialogParent ?? GetWindow();
            PopupFolderPicker(host, "Select your EverQuest installation folder", dir =>
            {
                pathInput.Text = dir;
            });
        };
        pathRow.AddChild(eqBrowse);
        vbox.AddChild(pathRow);

        var resultLabel = new Label();
        resultLabel.AddThemeFontSizeOverride("font_size", 12);
        resultLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(resultLabel);

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);

        var linkBtn = new Button();
        linkBtn.Text = "Link";
        linkBtn.CustomMinimumSize = new Vector2(80, 32);
        linkBtn.AddThemeFontSizeOverride("font_size", 14);
        linkBtn.Pressed += () =>
        {
            string path = pathInput.Text.Trim();
            if (EQAssetConfig.Instance.SetPath(path))
            {
                resultLabel.Text = "✓ EQ directory linked successfully!";
                resultLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 0.4f, 1f));
                statusLabel.Text = $"✓ EQ Linked: {EQAssetConfig.Instance.EQPath}";
                statusLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 0.4f, 1f));
                UpdateEQButtonText(eqDirButtonForUpdates);

                var (zones, chars, music) = EQAssetConfig.Instance.GetAssetSummary();
                resultLabel.Text += $"\nFound: {zones} zones, {chars} character sets, {music} music files";
                string tsDir = EQAssetConfig.Instance.GetResolvedTradeskillObjectsDir();
                if (!string.IsNullOrEmpty(tsDir))
                    resultLabel.Text += $"\nTradeskill objects: {tsDir}";
            }
            else
            {
                resultLabel.Text = "✗ Invalid EQ directory. Missing required files (eqgame.exe, gfaydark.s3d, global_chr.s3d)";
                resultLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f, 1f));
            }
            onLinkStateChanged?.Invoke();
        };
        btnRow.AddChild(linkBtn);

        var unlinkBtn = new Button();
        unlinkBtn.Text = "Unlink";
        unlinkBtn.CustomMinimumSize = new Vector2(80, 32);
        unlinkBtn.AddThemeFontSizeOverride("font_size", 14);
        unlinkBtn.AddThemeColorOverride("font_color", new Color(0.8f, 0.3f, 0.3f, 1f));
        unlinkBtn.Pressed += () =>
        {
            EQAssetConfig.Instance.Unlink();
            pathInput.Text = "";
            resultLabel.Text = "EQ directory unlinked.";
            resultLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f, 1f));
            statusLabel.Text = "No EQ installation linked.";
            statusLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.5f, 1f));
            UpdateEQButtonText(eqDirButtonForUpdates);
            onLinkStateChanged?.Invoke();
        };
        btnRow.AddChild(unlinkBtn);

        vbox.AddChild(btnRow);

        if (!includeTradeskillOptions)
            return;

        var tsHead = new Label();
        tsHead.Text = "Crafting stations (optional)";
        tsHead.AddThemeFontSizeOverride("font_size", 12);
        tsHead.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.65f, 1f));
        vbox.AddChild(tsHead);

        var tsHint = new Label();
        tsHint.Text = "Only needed for some crafting-station meshes. If you use EQ Sage later, exports can go under <linked EQ>\\eqsage\\objects and are picked up automatically. You can also browse to any folder of IT*.glb here.";
        tsHint.AutowrapMode = TextServer.AutowrapMode.Word;
        tsHint.AddThemeFontSizeOverride("font_size", 11);
        tsHint.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f, 1f));
        vbox.AddChild(tsHint);

        var tsRow = new HBoxContainer();
        tsRow.AddThemeConstantOverride("separation", 6);
        var tsInput = new LineEdit();
        tsInput.PlaceholderText = "Browse or paste path…";
        tsInput.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        tsInput.Text = EQAssetConfig.Instance.TradeskillObjectsObjectsDir;
        tsInput.AddThemeFontSizeOverride("font_size", 12);
        tsRow.AddChild(tsInput);

        var tsBrowse = new Button();
        tsBrowse.Text = "Browse…";
        tsBrowse.CustomMinimumSize = new Vector2(88, 30);
        tsBrowse.AddThemeFontSizeOverride("font_size", 12);
        tsBrowse.Pressed += () =>
        {
            Window host = fileDialogParent ?? GetWindow();
            PopupFolderPicker(host, "Folder containing IT*.glb (tradeskill stations)", dir =>
            {
                tsInput.Text = dir;
            });
        };
        tsRow.AddChild(tsBrowse);
        vbox.AddChild(tsRow);

        var tsBtnRow = new HBoxContainer();
        tsBtnRow.AddThemeConstantOverride("separation", 8);

        var tsResult = new Label();
        tsResult.AddThemeFontSizeOverride("font_size", 11);
        tsResult.AutowrapMode = TextServer.AutowrapMode.Word;

        var tsApply = new Button();
        tsApply.Text = "Apply tradeskill folder";
        tsApply.CustomMinimumSize = new Vector2(0, 28);
        tsApply.AddThemeFontSizeOverride("font_size", 12);
        tsApply.Pressed += () =>
        {
            string t = tsInput.Text.Trim();
            if (string.IsNullOrEmpty(t))
            {
                EQAssetConfig.Instance.SetTradeskillObjectsObjectsDir("");
                tsResult.Text = "Cleared tradeskill folder setting.";
                tsResult.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f, 1f));
                return;
            }

            if (EQAssetConfig.Instance.SetTradeskillObjectsObjectsDir(t))
            {
                tsResult.Text = "✓ Saved. IT* props load from this folder.";
                tsResult.AddThemeColorOverride("font_color", new Color(0.4f, 0.75f, 0.45f, 1f));
                tsInput.Text = EQAssetConfig.Instance.TradeskillObjectsObjectsDir;
            }
            else
            {
                tsResult.Text = "✗ Folder not found or invalid.";
                tsResult.AddThemeColorOverride("font_color", new Color(0.9f, 0.35f, 0.3f, 1f));
            }
        };
        tsBtnRow.AddChild(tsApply);

        var tsClear = new Button();
        tsClear.Text = "Clear";
        tsClear.CustomMinimumSize = new Vector2(72, 28);
        tsClear.AddThemeFontSizeOverride("font_size", 12);
        tsClear.Pressed += () =>
        {
            tsInput.Text = "";
            EQAssetConfig.Instance.SetTradeskillObjectsObjectsDir("");
            tsResult.Text = "Cleared tradeskill folder setting.";
            tsResult.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f, 1f));
        };
        tsBtnRow.AddChild(tsClear);
        vbox.AddChild(tsBtnRow);
        vbox.AddChild(tsResult);
    }

    private void ShowEQSetupDialog()
    {
        var dialog = new AcceptDialog();
        dialog.Title = "EverQuest Directory";
        dialog.Size = new Vector2I(540, 460);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        AddEverQuestLinkControls(vbox, _eqDirButton, null, dialog);

        dialog.AddChild(vbox);
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>After the player picks a server: block account login until a valid EQ install path is saved.</summary>
    private void ShowMandatoryEverQuestLinkGate()
    {
        var win = new Window();
        win.Title = "Link Your EverQuest Copy";
        win.Unresizable = true;
        win.Size = new Vector2I(580, 420);
        win.PopupWindow = true;
        win.Exclusive = true;

        var outer = new MarginContainer();
        outer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("margin_left", 16);
        outer.AddThemeConstantOverride("margin_top", 14);
        outer.AddThemeConstantOverride("margin_right", 16);
        outer.AddThemeConstantOverride("margin_bottom", 14);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var disclaimer = new Label();
        disclaimer.Text = EqLegalDisclaimerText;
        disclaimer.AutowrapMode = TextServer.AutowrapMode.Word;
        disclaimer.AddThemeFontSizeOverride("font_size", 12);
        disclaimer.AddThemeColorOverride("font_color", new Color(0.75f, 0.72f, 0.62f, 1f));
        vbox.AddChild(disclaimer);

        var continueBtn = new Button();
        continueBtn.Text = "Continue to Login";
        continueBtn.CustomMinimumSize = new Vector2(0, 38);
        continueBtn.AddThemeFontSizeOverride("font_size", 15);
        continueBtn.Disabled = !EQAssetConfig.Instance.IsConfigured;

        AddEverQuestLinkControls(vbox, _eqDirButton, () =>
        {
            continueBtn.Disabled = !EQAssetConfig.Instance.IsConfigured;
        }, win, includeTradeskillOptions: false);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 14);

        var backBtn = new Button();
        backBtn.Text = "← Server Select";
        backBtn.CustomMinimumSize = new Vector2(140, 36);
        backBtn.AddThemeFontSizeOverride("font_size", 13);
        backBtn.Pressed += () =>
        {
            win.QueueFree();
            ShowPanel("serverselect");
        };
        footer.AddChild(backBtn);

        var footerSpacer = new Control();
        footerSpacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.AddChild(footerSpacer);

        continueBtn.Pressed += () =>
        {
            if (!EQAssetConfig.Instance.IsConfigured) return;
            win.QueueFree();
            ShowPanel("login");
        };
        footer.AddChild(continueBtn);

        vbox.AddChild(footer);

        outer.AddChild(vbox);
        win.AddChild(outer);

        void onClose()
        {
            if (!GodotObject.IsInstanceValid(win)) return;
            win.QueueFree();
            ShowPanel("serverselect");
        }

        win.CloseRequested += onClose;

        AddChild(win);
        win.PopupCentered();
    }

    // ═══════════════════════════════════════════════════════════════
    //  BUILD CHARACTER SELECT PANEL
    // ═══════════════════════════════════════════════════════════════

    private void BuildCharSelectPanel()
    {
        _charSelectPanel = new PanelContainer();
        _charSelectPanel.Name = "CharSelectPanel";
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.25f);
        style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = 2;
        style.BorderColor = new Color(0.7f, 0.55f, 0.2f, 0.4f);
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 6;
        style.ShadowColor = new Color(0, 0, 0, 0.6f);
        style.ShadowSize = 12;
        (_charSelectPanel as PanelContainer).AddThemeStyleboxOverride("panel", style);

        _charSelectPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _charSelectPanel.OffsetLeft = -400;
        _charSelectPanel.OffsetRight = 400;
        _charSelectPanel.OffsetTop = -300;
        _charSelectPanel.OffsetBottom = 300;

        var hSplit = new HBoxContainer();
        hSplit.AddThemeConstantOverride("separation", 20);
        hSplit.OffsetLeft = 20; hSplit.OffsetTop = 20; hSplit.OffsetRight = -20; hSplit.OffsetBottom = -20;
        hSplit.SetAnchorsPreset(LayoutPreset.FullRect);

        // ════════ LEFT COLUMN: Characters and Buttons ════════
        var leftCol = new VBoxContainer();
        leftCol.AddThemeConstantOverride("separation", 10);
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftCol.SizeFlagsStretchRatio = 0.4f;
        leftCol.CustomMinimumSize = new Vector2(250, 0);

        _charSelectHeader = new Label();
        _charSelectHeader.Text = "Characters";
        _charSelectHeader.HorizontalAlignment = HorizontalAlignment.Center;
        _charSelectHeader.AddThemeFontSizeOverride("font_size", 22);
        _charSelectHeader.AddThemeColorOverride("font_color", new Color(0.85f, 0.7f, 0.25f, 1f));
        leftCol.AddChild(_charSelectHeader);

        // Scrollable character list
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;

        _charListContainer = new VBoxContainer();
        _charListContainer.AddThemeConstantOverride("separation", 6);
        _charListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_charListContainer);
        leftCol.AddChild(scroll);

        // Separator
        leftCol.AddChild(new HSeparator());

        // Buttons
        _newCharButton = new Button { Text = "Create Character" };
        _newCharButton.CustomMinimumSize = new Vector2(0, 36);
        _newCharButton.AddThemeFontSizeOverride("font_size", 14);
        _newCharButton.Pressed += () => ShowPanel("create");
        leftCol.AddChild(_newCharButton);

        _deleteCharButton = new Button { Text = "Delete Character", Disabled = true };
        _deleteCharButton.CustomMinimumSize = new Vector2(0, 36);
        _deleteCharButton.AddThemeFontSizeOverride("font_size", 14);
        _deleteCharButton.AddThemeColorOverride("font_color", new Color(0.8f, 0.3f, 0.3f, 1f));
        _deleteCharButton.Pressed += OnDeleteCharPressed;
        leftCol.AddChild(_deleteCharButton);

        /*
        _returnHomeButton = new Button { Text = "Return Home" };
        _returnHomeButton.CustomMinimumSize = new Vector2(0, 36);
        _returnHomeButton.AddThemeFontSizeOverride("font_size", 14);
        _returnHomeButton.Pressed += () => { GameClient.Instance.DisconnectFromServer(); ShowPanel("login"); };
        leftCol.AddChild(_returnHomeButton);
        */

        var resetUIRow = new HBoxContainer();
        resetUIRow.Alignment = BoxContainer.AlignmentMode.Center;
        _resetUICheckbox = new CheckBox { Text = "Reset UI?" };
        _resetUICheckbox.AddThemeFontSizeOverride("font_size", 13);
        resetUIRow.AddChild(_resetUICheckbox);
        leftCol.AddChild(resetUIRow);

        _enterWorldButton = new Button { Text = "Enter", Disabled = true };
        _enterWorldButton.CustomMinimumSize = new Vector2(0, 44);
        _enterWorldButton.AddThemeFontSizeOverride("font_size", 18);
        _enterWorldButton.Pressed += OnEnterWorldPressed;
        leftCol.AddChild(_enterWorldButton);

        hSplit.AddChild(leftCol);

        // ════════ RIGHT COLUMN: 3D Preview ════════
        var rightCol = new VBoxContainer();
        rightCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightCol.SizeFlagsStretchRatio = 0.6f;

        _charSelectPreviewContainer = new SubViewportContainer();
        _charSelectPreviewContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _charSelectPreviewContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _charSelectPreviewContainer.Stretch = true;
        
        var previewStyle = new StyleBoxFlat();
        previewStyle.BgColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
        previewStyle.BorderWidthLeft = previewStyle.BorderWidthTop = previewStyle.BorderWidthRight = previewStyle.BorderWidthBottom = 0;
        previewStyle.BorderColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
        previewStyle.CornerRadiusTopLeft = previewStyle.CornerRadiusTopRight = previewStyle.CornerRadiusBottomLeft = previewStyle.CornerRadiusBottomRight = 4;
        
        var bgPanel = new PanelContainer();
        bgPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        bgPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bgPanel.AddThemeStyleboxOverride("panel", previewStyle);

        _charSelectPreviewViewport = new SubViewport();
        _charSelectPreviewViewport.Size = new Vector2I(400, 500);
        _charSelectPreviewViewport.TransparentBg = true;
        _charSelectPreviewViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _charSelectPreviewViewport.OwnWorld3D = true;

        _charSelectPreviewCamera = new Camera3D();
        _charSelectPreviewCamera.Position = new Vector3(0, 1.0f, 10.0f);
        _charSelectPreviewCamera.RotationDegrees = new Vector3(-4f, 0, 0);
        _charSelectPreviewCamera.Fov = 30f;
        _charSelectPreviewViewport.AddChild(_charSelectPreviewCamera);

        // Character-select preview light rig (independent from world/menu backdrop lights).
        var previewKeyLight = new DirectionalLight3D();
        previewKeyLight.Name = "CharSelectSun";
        previewKeyLight.RotationDegrees = new Vector3(-30, -45, 0);
        previewKeyLight.LightEnergy = 0.85f;
        previewKeyLight.LightColor = new Color(1.00f, 0.90f, 0.78f);
        previewKeyLight.LightSpecular = 0.12f;
        previewKeyLight.ShadowEnabled = false;
        _charSelectPreviewViewport.AddChild(previewKeyLight);

        var previewFillLight = new DirectionalLight3D();
        previewFillLight.Name = "CharSelectFillLight";
        previewFillLight.RotationDegrees = new Vector3(-15, 135, 0);
        previewFillLight.LightEnergy = 0.22f;
        previewFillLight.LightColor = new Color(0.72f, 0.80f, 0.95f);
        previewFillLight.LightSpecular = 0.05f;
        previewFillLight.ShadowEnabled = false;
        _charSelectPreviewViewport.AddChild(previewFillLight);

        var previewRimLight = new DirectionalLight3D();
        previewRimLight.Name = "CharSelectRimLight";
        previewRimLight.RotationDegrees = new Vector3(-10, 180, 0);
        previewRimLight.LightEnergy = 0.08f;
        previewRimLight.LightColor = new Color(0.95f, 0.90f, 0.82f);
        previewRimLight.LightSpecular = 0.03f;
        previewRimLight.ShadowEnabled = false;
        _charSelectPreviewViewport.AddChild(previewRimLight);

        _charSelectPreviewRoot = new Node3D();
        _charSelectPreviewRoot.Name = "CharSelectPreviewRoot";
        _charSelectPreviewRoot.Rotation = new Vector3(0, -Mathf.Pi / 2f, 0); // Face front
        _charSelectPreviewViewport.AddChild(_charSelectPreviewRoot);

        _charSelectPreviewContainer.AddChild(_charSelectPreviewViewport);
        bgPanel.AddChild(_charSelectPreviewContainer);
        rightCol.AddChild(bgPanel);

        var quitRow = new HBoxContainer();
        quitRow.Alignment = BoxContainer.AlignmentMode.End;
        _quitButton = new Button { Text = "Quit" };
        _quitButton.CustomMinimumSize = new Vector2(100, 36);
        _quitButton.AddThemeFontSizeOverride("font_size", 14);
        _quitButton.Pressed += () => GetTree().Quit();
        quitRow.AddChild(_quitButton);
        rightCol.AddChild(quitRow);

        hSplit.AddChild(rightCol);

        (_charSelectPanel as PanelContainer).AddChild(hSplit);
        AddChild(_charSelectPanel);
    }

    // ═══════════════════════════════════════════════════════════════
    //  BUILD CREATE CHARACTER PANEL (Fully Programmatic)
    // ═══════════════════════════════════════════════════════════════

    private void BuildCreatePanel()
    {
        // Remove the old .tscn CreatePanel if it exists
        var oldPanel = GetNodeOrNull<Control>("CreatePanel");
        if (oldPanel != null) oldPanel.QueueFree();

        _createPanel = new PanelContainer();
        _createPanel.Name = "CreatePanelNew";
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.25f);
        style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = 2;
        style.BorderColor = new Color(0.7f, 0.55f, 0.2f, 0.4f);
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 6;
        style.ShadowColor = new Color(0, 0, 0, 0.6f);
        style.ShadowSize = 12;
        (_createPanel as PanelContainer).AddThemeStyleboxOverride("panel", style);

        _createPanel.SetAnchorsPreset(LayoutPreset.Center);
        _createPanel.OffsetLeft = -480;
        _createPanel.OffsetRight = 480;
        _createPanel.OffsetTop = -340;
        _createPanel.OffsetBottom = 340;

        var mainVbox = new VBoxContainer();
        mainVbox.AddThemeConstantOverride("separation", 8);
        mainVbox.OffsetLeft = 20; mainVbox.OffsetTop = 16; mainVbox.OffsetRight = -20; mainVbox.OffsetBottom = -16;
        mainVbox.SetAnchorsPreset(LayoutPreset.FullRect);

        // Back button
        _backToSelectButton = new Button();
        _backToSelectButton.Text = "← Back";
        _backToSelectButton.CustomMinimumSize = new Vector2(80, 28);
        _backToSelectButton.AddThemeFontSizeOverride("font_size", 12);
        _backToSelectButton.Pressed += () => ShowPanel("charselect");
        mainVbox.AddChild(_backToSelectButton);

        // Header
        var header = new Label();
        header.Text = "Create Your Character";
        header.HorizontalAlignment = HorizontalAlignment.Center;
        header.AddThemeFontSizeOverride("font_size", 22);
        header.AddThemeColorOverride("font_color", new Color(0.85f, 0.7f, 0.25f, 1f));
        mainVbox.AddChild(header);

        // ── Three-Column Layout: Left = Preview, Center = Selections, Right = Stats/Desc ──
        var hSplit = new HBoxContainer();
        hSplit.AddThemeConstantOverride("separation", 12);
        hSplit.SizeFlagsVertical = SizeFlags.ExpandFill;

        // ════════ LEFT COLUMN: 3D Preview + Gender/Face ════════
        var previewCol = new VBoxContainer();
        previewCol.AddThemeConstantOverride("separation", 6);
        previewCol.CustomMinimumSize = new Vector2(240, 0);

        // 3D Model Preview using SubViewport
        _previewContainer = new SubViewportContainer();
        _previewContainer.CustomMinimumSize = new Vector2(230, 320);
        _previewContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _previewContainer.Stretch = true;
        var previewStyle = new StyleBoxFlat();
        previewStyle.BgColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
        previewStyle.BorderWidthLeft = previewStyle.BorderWidthTop = previewStyle.BorderWidthRight = previewStyle.BorderWidthBottom = 0;
        previewStyle.BorderColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
        previewStyle.CornerRadiusTopLeft = previewStyle.CornerRadiusTopRight = previewStyle.CornerRadiusBottomLeft = previewStyle.CornerRadiusBottomRight = 4;

        _previewViewport = new SubViewport();
        _previewViewport.Size = new Vector2I(230, 320);
        _previewViewport.TransparentBg = true;
        _previewViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _previewViewport.OwnWorld3D = true;

        // Camera
        _previewCamera = new Camera3D();
        _previewCamera.Position = new Vector3(0, 1.0f, 12.0f);
        _previewCamera.RotationDegrees = new Vector3(-4f, 0, 0); // Slight downward angle
        _previewCamera.Fov = 30f;
        _previewViewport.AddChild(_previewCamera);

        // Create-character preview light rig ("sun" + fill), independent from world lighting.
        var createPreviewSun = new DirectionalLight3D();
        createPreviewSun.Name = "CreatePreviewSun";
        createPreviewSun.RotationDegrees = new Vector3(-30, -45, 0);
        createPreviewSun.LightEnergy = 0.85f;
        createPreviewSun.LightColor = new Color(1.00f, 0.90f, 0.78f);
        createPreviewSun.LightSpecular = 0.12f;
        createPreviewSun.ShadowEnabled = false;
        _previewViewport.AddChild(createPreviewSun);

        var createPreviewFill = new DirectionalLight3D();
        createPreviewFill.Name = "CreatePreviewFill";
        createPreviewFill.RotationDegrees = new Vector3(-15, 135, 0);
        createPreviewFill.LightEnergy = 0.22f;
        createPreviewFill.LightColor = new Color(0.72f, 0.80f, 0.95f);
        createPreviewFill.LightSpecular = 0.05f;
        createPreviewFill.ShadowEnabled = false;
        _previewViewport.AddChild(createPreviewFill);

        // Model root (spins)
        _previewRoot = new Node3D();
        _previewRoot.Name = "PreviewRoot";
        _previewViewport.AddChild(_previewRoot);

        _previewContainer.AddChild(_previewViewport);
        previewCol.AddChild(_previewContainer);

        // Gender toggle
        var genderRow = new HBoxContainer();
        genderRow.AddThemeConstantOverride("separation", 4);
        var genderLabel = new Label();
        genderLabel.Text = "Gender";
        genderLabel.CustomMinimumSize = new Vector2(50, 0);
        genderLabel.AddThemeFontSizeOverride("font_size", 12);
        genderLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        genderRow.AddChild(genderLabel);
        _genderMaleBtn = new Button { Text = "Male", ToggleMode = true, ButtonPressed = true };
        _genderMaleBtn.CustomMinimumSize = new Vector2(70, 28);
        _genderMaleBtn.AddThemeFontSizeOverride("font_size", 12);
        _genderMaleBtn.Pressed += () => { _selectedGender = 0; _genderMaleBtn.ButtonPressed = true; _genderFemaleBtn.ButtonPressed = false; _selectedFace = 0; UpdatePreviewModel(); };
        genderRow.AddChild(_genderMaleBtn);
        _genderFemaleBtn = new Button { Text = "Female", ToggleMode = true };
        _genderFemaleBtn.CustomMinimumSize = new Vector2(70, 28);
        _genderFemaleBtn.AddThemeFontSizeOverride("font_size", 12);
        _genderFemaleBtn.Pressed += () => { _selectedGender = 1; _genderFemaleBtn.ButtonPressed = true; _genderMaleBtn.ButtonPressed = false; _selectedFace = 0; UpdatePreviewModel(); };
        genderRow.AddChild(_genderFemaleBtn);
        previewCol.AddChild(genderRow);

        // Face selector
        var faceRow = new HBoxContainer();
        faceRow.AddThemeConstantOverride("separation", 4);
        var faceLabelTitle = new Label();
        faceLabelTitle.Text = "Face";
        faceLabelTitle.CustomMinimumSize = new Vector2(50, 0);
        faceLabelTitle.AddThemeFontSizeOverride("font_size", 12);
        faceLabelTitle.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        faceRow.AddChild(faceLabelTitle);
        _facePrevBtn = new Button { Text = "◀" };
        _facePrevBtn.CustomMinimumSize = new Vector2(32, 28);
        _facePrevBtn.AddThemeFontSizeOverride("font_size", 14);
        _facePrevBtn.Pressed += () => { int max = (_selectedGender == 0) ? _faceCountMale : _faceCountFemale; _selectedFace = (_selectedFace - 1 + max) % max; UpdatePreviewModel(); };
        faceRow.AddChild(_facePrevBtn);
        _faceLabel = new Label();
        _faceLabel.Text = "1 / 1";
        _faceLabel.CustomMinimumSize = new Vector2(60, 0);
        _faceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _faceLabel.AddThemeFontSizeOverride("font_size", 13);
        faceRow.AddChild(_faceLabel);
        _faceNextBtn = new Button { Text = "▶" };
        _faceNextBtn.CustomMinimumSize = new Vector2(32, 28);
        _faceNextBtn.AddThemeFontSizeOverride("font_size", 14);
        _faceNextBtn.Pressed += () => { int max = (_selectedGender == 0) ? _faceCountMale : _faceCountFemale; _selectedFace = (_selectedFace + 1) % max; UpdatePreviewModel(); };
        faceRow.AddChild(_faceNextBtn);
        previewCol.AddChild(faceRow);

        // Armor material selector
        var armorRow = new HBoxContainer();
        armorRow.AddThemeConstantOverride("separation", 4);
        var armorTitle = new Label();
        armorTitle.Text = "Armor";
        armorTitle.CustomMinimumSize = new Vector2(50, 0);
        armorTitle.AddThemeFontSizeOverride("font_size", 12);
        armorTitle.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        armorRow.AddChild(armorTitle);
        _armorPrevBtn = new Button { Text = "◀" };
        _armorPrevBtn.CustomMinimumSize = new Vector2(32, 28);
        _armorPrevBtn.AddThemeFontSizeOverride("font_size", 14);
        _armorPrevBtn.Pressed += () => { _selectedArmorMaterial = (_selectedArmorMaterial - 1 + ArmorMaterialNames.Length) % ArmorMaterialNames.Length; UpdatePreviewModel(); };
        armorRow.AddChild(_armorPrevBtn);
        _armorLabel = new Label();
        _armorLabel.Text = "Cloth";
        _armorLabel.CustomMinimumSize = new Vector2(70, 0);
        _armorLabel.HorizontalAlignment = HorizontalAlignment.Center;


        _armorLabel.AddThemeFontSizeOverride("font_size", 13);
        armorRow.AddChild(_armorLabel);
        _armorNextBtn = new Button { Text = "▶" };
        _armorNextBtn.CustomMinimumSize = new Vector2(32, 28);
        _armorNextBtn.AddThemeFontSizeOverride("font_size", 14);
        _armorNextBtn.Pressed += () => { _selectedArmorMaterial = (_selectedArmorMaterial + 1) % ArmorMaterialNames.Length; UpdatePreviewModel(); };
        armorRow.AddChild(_armorNextBtn);
        previewCol.AddChild(armorRow);

        // Rotate toggle
        _rotateCheckbox = new CheckBox();
        _rotateCheckbox.Text = "Rotate";
        _rotateCheckbox.ButtonPressed = true;
        _rotateCheckbox.AddThemeFontSizeOverride("font_size", 12);
        _rotateCheckbox.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        _rotateCheckbox.Toggled += (on) => { _previewAutoRotate = on; if (!on) { _previewRotation = -Mathf.Pi / 2f; _previewRoot.Rotation = new Vector3(0, -Mathf.Pi / 2f, 0); } };
        previewCol.AddChild(_rotateCheckbox);

        hSplit.AddChild(previewCol);

        // ════════ CENTER COLUMN: Name/Race/Class/Deity ════════
        var leftCol = new VBoxContainer();
        leftCol.AddThemeConstantOverride("separation", 8);
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftCol.CustomMinimumSize = new Vector2(220, 0);

        // Name
        leftCol.AddChild(MakeFieldRow("Name", out _nameInput));
        _nameInput.PlaceholderText = "Enter character name...";
        _nameInput.MaxLength = 20;
        _nameInput.TextChanged += _ => ValidateCreateForm();

        // Race (drives everything)
        leftCol.AddChild(MakeDropdownRow("Race", out _raceSelect));
        foreach (var race in RaceNames) _raceSelect.AddItem(race);
        _raceSelect.ItemSelected += OnRaceSelected;

        // Class (populated from DB)
        leftCol.AddChild(MakeDropdownRow("Class", out _classSelect));
        _classSelect.AddItem("Select a race first...");
        _classSelect.Disabled = true;
        _classSelect.ItemSelected += OnClassSelected;

        // Deity (populated from DB)
        leftCol.AddChild(MakeDropdownRow("Deity", out _deitySelect));
        _deitySelect.AddItem("Select a class first...");
        _deitySelect.Disabled = true;

        // ── Stats Panel with +/- allocation ──
        var statsPanel = new PanelContainer();
        var statsStyle = new StyleBoxFlat();
        statsStyle.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.25f);
        statsStyle.BorderWidthLeft = statsStyle.BorderWidthTop = statsStyle.BorderWidthRight = statsStyle.BorderWidthBottom = 1;
        statsStyle.BorderColor = new Color(0.5f, 0.4f, 0.15f, 0.6f);
        statsStyle.CornerRadiusTopLeft = statsStyle.CornerRadiusTopRight = statsStyle.CornerRadiusBottomLeft = statsStyle.CornerRadiusBottomRight = 4;
        statsPanel.AddThemeStyleboxOverride("panel", statsStyle);

        var statsVbox = new VBoxContainer();
        statsVbox.AddThemeConstantOverride("separation", 2);

        // Points remaining header
        var statsHeaderRow = new HBoxContainer();
        var statsHeaderLabel = new Label();
        statsHeaderLabel.Text = "BASE STATS";
        statsHeaderLabel.AddThemeFontSizeOverride("font_size", 11);
        statsHeaderLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.55f, 0.2f, 0.8f));
        statsHeaderRow.AddChild(statsHeaderLabel);

        var spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        statsHeaderRow.AddChild(spacer);

        _pointsRemainingLabel = new Label();
        _pointsRemainingLabel.Text = "Points: 0";
        _pointsRemainingLabel.AddThemeFontSizeOverride("font_size", 11);
        _pointsRemainingLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.8f, 0.4f, 1f));
        statsHeaderRow.AddChild(_pointsRemainingLabel);
        statsVbox.AddChild(statsHeaderRow);

        // Stat rows with +/- buttons
        Label[] statLabels = { null, null, null, null, null, null, null };
        var statsGrid = new GridContainer();
        statsGrid.Columns = 5; // Name, Minus, Value, Plus, Alloc
        statsGrid.AddThemeConstantOverride("h_separation", 4);
        statsGrid.AddThemeConstantOverride("v_separation", 1);

        for (int i = 0; i < 7; i++)
        {
            int idx = i; // capture for closure

            // Stat name (clickable — shows info in description panel)
            var nameBtn = new Button();
            nameBtn.Text = StatNames[i];
            nameBtn.Flat = true;
            nameBtn.CustomMinimumSize = new Vector2(35, 0);
            nameBtn.AddThemeFontSizeOverride("font_size", 12);
            nameBtn.AddThemeColorOverride("font_color", new Color(0.65f, 0.6f, 0.45f, 1f));
            nameBtn.AddThemeColorOverride("font_hover_color", new Color(0.9f, 0.75f, 0.3f, 1f));
            nameBtn.MouseDefaultCursorShape = CursorShape.PointingHand;
            nameBtn.Pressed += () => {
                if (StatDescriptions.TryGetValue(idx, out string desc))
                {
                    _classDescription.Clear();
                    _classDescription.AppendText(desc);
                }
            };
            statsGrid.AddChild(nameBtn);

            // Minus button
            _statMinusBtns[i] = new Button();
            _statMinusBtns[i].Text = "-";
            _statMinusBtns[i].CustomMinimumSize = new Vector2(24, 22);
            _statMinusBtns[i].AddThemeFontSizeOverride("font_size", 12);
            _statMinusBtns[i].Pressed += () => AdjustStat(idx, -1);
            statsGrid.AddChild(_statMinusBtns[i]);

            // Value label
            statLabels[i] = new Label();
            statLabels[i].Text = "0";
            statLabels[i].CustomMinimumSize = new Vector2(35, 0);
            statLabels[i].HorizontalAlignment = HorizontalAlignment.Center;
            statLabels[i].AddThemeFontSizeOverride("font_size", 13);
            statsGrid.AddChild(statLabels[i]);

            // Plus button
            _statPlusBtns[i] = new Button();
            _statPlusBtns[i].Text = "+";
            _statPlusBtns[i].CustomMinimumSize = new Vector2(24, 22);
            _statPlusBtns[i].AddThemeFontSizeOverride("font_size", 12);
            _statPlusBtns[i].Pressed += () => AdjustStat(idx, 1);
            statsGrid.AddChild(_statPlusBtns[i]);

            // Allocated indicator
            var allocLabel = new Label();
            allocLabel.Text = "";
            allocLabel.CustomMinimumSize = new Vector2(30, 0);
            allocLabel.AddThemeFontSizeOverride("font_size", 10);
            allocLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.7f, 1f, 0.7f));
            statsGrid.AddChild(allocLabel);
        }

        _previewStr = statLabels[0]; _previewSta = statLabels[1]; _previewAgi = statLabels[2];
        _previewDex = statLabels[3]; _previewWis = statLabels[4]; _previewInt = statLabels[5];
        _previewCha = statLabels[6];

        statsVbox.AddChild(statsGrid);

        // HP/Mana row
        var hpManaRow = new HBoxContainer();
        hpManaRow.AddThemeConstantOverride("separation", 12);

        var hpLabel = new Label(); hpLabel.Text = "HP"; hpLabel.AddThemeFontSizeOverride("font_size", 12);
        hpLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.15f, 0.15f, 1f));
        hpManaRow.AddChild(hpLabel);
        _previewHp = new Label(); _previewHp.Text = "0"; _previewHp.AddThemeFontSizeOverride("font_size", 12);
        _previewHp.AddThemeColorOverride("font_color", new Color(0.8f, 0.3f, 0.3f, 1f));
        hpManaRow.AddChild(_previewHp);

        var manaLabel = new Label(); manaLabel.Text = "MANA"; manaLabel.AddThemeFontSizeOverride("font_size", 12);
        manaLabel.AddThemeColorOverride("font_color", new Color(0.15f, 0.15f, 0.6f, 1f));
        hpManaRow.AddChild(manaLabel);
        _previewMana = new Label(); _previewMana.Text = "0"; _previewMana.AddThemeFontSizeOverride("font_size", 12);
        _previewMana.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.9f, 1f));
        hpManaRow.AddChild(_previewMana);

        statsVbox.AddChild(hpManaRow);
        statsPanel.AddChild(statsVbox);
        leftCol.AddChild(statsPanel);

        hSplit.AddChild(leftCol);

        // ════════ RIGHT COLUMN: Stats + Class description ════════
        var rightCol = new VBoxContainer();
        rightCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var descPanel = new PanelContainer();
        descPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        var descStyle = new StyleBoxFlat();
        descStyle.BgColor = new Color(0.03f, 0.03f, 0.05f, 0.25f);
        descStyle.BorderWidthLeft = descStyle.BorderWidthTop = descStyle.BorderWidthRight = descStyle.BorderWidthBottom = 1;
        descStyle.BorderColor = new Color(0.3f, 0.3f, 0.35f, 1f);
        descStyle.CornerRadiusTopLeft = descStyle.CornerRadiusTopRight = descStyle.CornerRadiusBottomLeft = descStyle.CornerRadiusBottomRight = 4;
        descPanel.AddThemeStyleboxOverride("panel", descStyle);

        _classDescription = new RichTextLabel();
        _classDescription.BbcodeEnabled = true;
        _classDescription.AddThemeFontSizeOverride("normal_font_size", 12);
        _classDescription.Text = "Select a race and class to see details.";
        _classDescription.SizeFlagsVertical = SizeFlags.ExpandFill;
        descPanel.AddChild(_classDescription);

        rightCol.AddChild(descPanel);
        hSplit.AddChild(rightCol);

        mainVbox.AddChild(hSplit);

        // Create button
        _playButton = new Button();
        _playButton.Text = "Create Character";
        _playButton.CustomMinimumSize = new Vector2(0, 44);
        _playButton.AddThemeFontSizeOverride("font_size", 18);
        _playButton.Disabled = true;
        _playButton.Pressed += OnCreateCharPressed;
        mainVbox.AddChild(_playButton);

        // Status
        _statusLabel = new Label();
        _statusLabel.Text = "";
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statusLabel.AddThemeFontSizeOverride("font_size", 12);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f, 1f));
        mainVbox.AddChild(_statusLabel);

        (_createPanel as PanelContainer).AddChild(mainVbox);
        AddChild(_createPanel);

        // Set defaults
        _raceSelect.Selected = 0;
    }

    // ═══════════════════════════════════════════════════════════════
    //  BUILD MENU BACKDROP (3D ENVIRONMENT)
    // ═══════════════════════════════════════════════════════════════

    private void BuildMenuBackdrop()
    {
        _backdropContainer = new SubViewportContainer();
        _backdropContainer.Name = "BackdropContainer";
        _backdropContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdropContainer.SetAnchor(Side.Right, 1.0f);
        _backdropContainer.SetAnchor(Side.Bottom, 1.0f);
        _backdropContainer.OffsetRight = 0;
        _backdropContainer.OffsetBottom = 0;
        _backdropContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _backdropContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _backdropContainer.Stretch = true;
        
        // Ensure it sits at the very back behind everything else
        AddChild(_backdropContainer);
        MoveChild(_backdropContainer, 0);

        _backdropViewport = new SubViewport();
        _backdropViewport.TransparentBg = false;
        _backdropViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _backdropViewport.OwnWorld3D = true;
        _backdropContainer.AddChild(_backdropViewport);

        _backdropRoot = new Node3D();
        _backdropRoot.Name = "BackdropRoot";
        _backdropViewport.AddChild(_backdropRoot);

        _backdropCamera = new Camera3D();
        _backdropCamera.Name = "BackdropCamera";
        _backdropCamera.Far = 1000f;
        _backdropCamera.Fov = 45f;
        _backdropRoot.AddChild(_backdropCamera);

        // ── The Blood/Pale Moon ──
        var moon = new MeshInstance3D();
        moon.Name = "Moon";
        var sphere = new SphereMesh();
        sphere.Radius = 30f;
        sphere.Height = 60f;
        sphere.RadialSegments = 32;
        sphere.Rings = 16;
        moon.Mesh = sphere;

        var moonMat = new StandardMaterial3D();
        moonMat.ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded;
        moonMat.AlbedoColor = new Color(0.95f, 0.9f, 0.75f); // Pale yellowish white
        moonMat.EmissionEnabled = true;
        moonMat.Emission = new Color(0.6f, 0.5f, 0.4f);
        moonMat.EmissionEnergyMultiplier = 1.5f;
        moon.MaterialOverride = moonMat;

        // Position closer so it pierces the fog
        moon.Position = new Vector3(40f, -80f, 100f);
        _backdropRoot.AddChild(moon);

        var dirLight = new DirectionalLight3D();
        dirLight.RotationDegrees = new Vector3(-30, 0, 0); // Shines FROM the moon (+Z) TOWARDS the camera (-Z)
        dirLight.LightEnergy = 0.8f;
        dirLight.ShadowEnabled = true; // Fixes the weird lighting bleeding through walls
        dirLight.DirectionalShadowMaxDistance = 1000f; // Prevent shadows from disappearing when far away
        dirLight.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly; // Stop Godot from rendering a duplicate sun in the sky!
        _backdropRoot.AddChild(dirLight);

        // Fill light has been completely removed to allow for pure, dramatic silhouettes against the moon.

        // Optional: Fog/Environment settings
        var env = new Godot.Environment();
        env.BackgroundMode = Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.15f, 0.05f, 0.08f); // Dark red/purple moody background
        env.FogEnabled = true;
        env.FogDensity = 0.008f; // Thinner fog so the moon and background are more visible
        env.FogLightColor = new Color(0.05f, 0.05f, 0.08f);
        
        _backdropCamera.Environment = env;

        // Hide the original black ColorRect background if it exists
        var bg = GetNodeOrNull<ColorRect>("Background");
        if (bg != null) bg.Hide();
    }

    /// <summary>True if this menu control and backdrop nodes are still in the tree (async loads must bail after <c>await</c>).</summary>
    private bool MenuBackdropAlive()
    {
        return GodotObject.IsInstanceValid(this)
            && IsInsideTree()
            && _backdropRoot != null && GodotObject.IsInstanceValid(_backdropRoot)
            && _backdropCamera != null && GodotObject.IsInstanceValid(_backdropCamera);
    }

    /// <summary>
    /// Loads a zone .glb directly from cache to serve as the menu backdrop.
    /// Provide Godot world coordinates for the camera.
    /// </summary>
    public async System.Threading.Tasks.Task LoadBackdropZone(string zoneId, Vector3 cameraPosition, Vector3 cameraRotationDegrees)
    {
        if (!MenuBackdropAlive())
            return;

        // Clean up old backdrop mesh
        var oldMesh = _backdropRoot.GetNodeOrNull<Node3D>($"GLB_{_currentBackdropZone}");
        if (oldMesh != null) oldMesh.QueueFree();

        _currentBackdropZone = zoneId;
        
        if (EQAssetConfig.Instance.IsConfigured && !EQAssetCache.Instance.HasZone(zoneId))
        {
            var extractor = LanternExtractorRunner.Instance;
            if (extractor.IsAvailable)
            {
                GD.Print($"[MENU] Extracting backdrop zone {zoneId}...");
                bool extracted = await extractor.ExtractZone(zoneId);
                if (!MenuBackdropAlive())
                    return;
                if (!extracted)
                {
                    GD.PrintErr($"[MENU] Failed to extract backdrop zone {zoneId}.");
                    return;
                }
            }
        }
        
        if (!MenuBackdropAlive())
            return;

        string glbPath = EQAssetCache.Instance.GetZoneGlbPath(zoneId);
        if (string.IsNullOrEmpty(glbPath) || !System.IO.File.Exists(glbPath))
        {
            GD.PrintErr($"[MENU] Backdrop zone {zoneId} not found in cache.");
            return;
        }

        try
        {
            var gltfDoc = new Godot.GltfDocument();
            var gltfState = new Godot.GltfState();

            var err = gltfDoc.AppendFromFile(glbPath, gltfState);
            if (err != Godot.Error.Ok)
            {
                GD.PrintErr($"[MENU] GLTF parse error for backdrop: {err}");
                return;
            }

            if (!MenuBackdropAlive())
                return;

            Node scene = gltfDoc.GenerateScene(gltfState);
            if (scene == null) return;

            if (!MenuBackdropAlive())
            {
                scene.QueueFree();
                return;
            }

            var zoneRoot = new Node3D { Name = $"GLB_{zoneId}" };
            zoneRoot.Transform = new Transform3D(
                new Vector3(0, 0, 10),
                new Vector3(0, 10, 0),
                new Vector3(-10, 0, 0),
                Vector3.Zero
            );
            zoneRoot.AddChild(scene);
            if (!MenuBackdropAlive())
            {
                zoneRoot.QueueFree();
                return;
            }

            _backdropRoot.AddChild(zoneRoot);

            _backdropCamera.Position = cameraPosition;
            _backdropCamera.RotationDegrees = cameraRotationDegrees;
            
            // Note: We will apply FixBackdropMaterials to the entire _backdropRoot later
            
            string cachePath = EQAssetCache.Instance.GetZonePath(zoneId);
            
            // Set up material animator for fire, water, lava, etc.
            var materialAnimator = new MaterialAnimator { Name = "MenuMaterialAnimator" };
            _backdropRoot.AddChild(materialAnimator);

            var objectPlacer = new ZoneObjectPlacer();
            // Keep character-select backdrop ambience (torches/lanterns/fires) enabled.
            objectPlacer.DisableLights = false;
            objectPlacer.ShadowsEnabled = true;
            objectPlacer.Animator = materialAnimator;
            
            // Animate main zone terrain
            string zoneMatList = System.IO.Path.Combine(cachePath, "Zone", "MaterialLists", $"{zoneId}.txt");
            if (System.IO.File.Exists(zoneMatList))
            {
                var animData = objectPlacer.ParseMaterialList(zoneMatList);
                if (animData != null && animData.Count > 0)
                {
                    string texturesDir = System.IO.Path.Combine(cachePath, "Zone", "Textures");
                    objectPlacer.RegisterAnimationsRecursive(zoneRoot, animData, texturesDir);
                }
            }

            // Place sub-objects like trees, portcullises, braziers, etc. (they will animate automatically via objectPlacer)
            objectPlacer.PlaceObjects(zoneId, cachePath, _backdropRoot);
            objectPlacer.PlaceLights(zoneId, cachePath, _backdropRoot);

            // Apply standard zone material fixes to EVERYTHING (zone mesh + objects)
            FixBackdropMaterials(_backdropRoot);

            GD.Print($"[MENU] Loaded backdrop zone: {zoneId} (with objects)");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MENU] Backdrop load exception: {ex.Message}");
        }
    }

    private void FixBackdropMaterials(Node node)
    {
        if (node is MeshInstance3D meshInst)
        {
            for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (meshInst.GetSurfaceOverrideMaterial(i) is StandardMaterial3D mat)
                {
                    mat.SpecularMode = StandardMaterial3D.SpecularModeEnum.Disabled; // Prevent weird white glowing
                }
            }

            if (meshInst.Mesh != null)
            {
                for (int i = 0; i < meshInst.Mesh.GetSurfaceCount(); i++)
                {
                    if (meshInst.Mesh.SurfaceGetMaterial(i) is StandardMaterial3D mat)
                    {
                        mat.SpecularMode = StandardMaterial3D.SpecularModeEnum.Disabled; // Prevent weird white glowing
                    }
                }
            }
        }

        foreach (var child in node.GetChildren())
        {
            FixBackdropMaterials(child);
        }
    }

    // Helper: labeled LineEdit row
    private HBoxContainer MakeFieldRow(string label, out LineEdit lineEdit)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var lbl = new Label();
        lbl.Text = label;
        lbl.CustomMinimumSize = new Vector2(50, 0);
        lbl.AddThemeFontSizeOverride("font_size", 14);
        lbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        lbl.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(lbl);
        lineEdit = new LineEdit();
        lineEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        lineEdit.AddThemeFontSizeOverride("font_size", 15);
        row.AddChild(lineEdit);
        return row;
    }

    // Helper: labeled OptionButton row
    private HBoxContainer MakeDropdownRow(string label, out OptionButton dropdown)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var lbl = new Label();
        lbl.Text = label;
        lbl.CustomMinimumSize = new Vector2(50, 0);
        lbl.AddThemeFontSizeOverride("font_size", 14);
        lbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        lbl.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(lbl);
        dropdown = new OptionButton();
        dropdown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        dropdown.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(dropdown);
        return row;
    }

    // ═══════════════════════════════════════════════════════════════
    //  PANEL MANAGEMENT
    // ═══════════════════════════════════════════════════════════════

    private void ShowPanel(string panel)
    {
        _loginPanel.Visible = panel == "login";
        _charSelectPanel.Visible = panel == "charselect";
        _createPanel.Visible = panel == "create";
        if (_serverSelectPanel != null) _serverSelectPanel.Visible = panel == "serverselect";

        // Start / stop server probes alongside the server-select panel
        if (panel == "serverselect")
        {
            StartServerProbes();

            // Pre-select whatever the user picked last time so the Connect
            // button enables itself as soon as its probe goes green.
            string lastUrl = _serverAddressInput != null ? _serverAddressInput.Text.Trim() : "";
            PreselectServerByUrl(lastUrl);
        }
        else
        {
            ShutdownServerProbes();
        }

        if (panel == "login" && _loginServerLabel != null)
        {
            string name = !string.IsNullOrEmpty(GameState.ServerName) ? GameState.ServerName : "Server";
            string url = _serverAddressInput != null ? _serverAddressInput.Text.Trim() : "";
            _loginServerLabel.Text = string.IsNullOrEmpty(url) ? name : $"{name}  ({url})";
        }

        // Request char create data when entering create panel
        if (panel == "create" && GameClient.Instance.IsSocketConnected)
        {
            RequestCharCreateData();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  CHARACTER CREATION — CASCADING SELECTIONS
    // ═══════════════════════════════════════════════════════════════

    private void RequestCharCreateData()
    {
        int raceId = RaceIds[_raceSelect.Selected];
        _currentRaceId = raceId;
        GameClient.Instance.SendMessage("REQUEST_CHAR_CREATE_DATA", new { raceId });
        _classSelect.Clear();
        _classSelect.AddItem("Loading classes...");
        _classSelect.Disabled = true;
        _deitySelect.Clear();
        _deitySelect.AddItem("Select a class first...");
        _deitySelect.Disabled = true;
    }

    private void OnRaceSelected(long index)
    {
        if (!GameClient.Instance.IsSocketConnected) return;
        RequestCharCreateData();
    }

    private void OnCharCreateDataReceived(Variant data)
    {
        if (!IsInstanceValid(this)) return;
        try
        {
            var root = JsonDocument.Parse(data.ToString()).RootElement;
            _currentCreateData = root;

            _classSelect.Clear();
            var classes = root.GetProperty("classes");
            _currentClassIds = new int[classes.GetArrayLength()];

            int i = 0;
            foreach (var cls in classes.EnumerateArray())
            {
                int classId = cls.GetProperty("classId").GetInt32();
                string displayName = ClassDisplayNames.ContainsKey(classId) ? ClassDisplayNames[classId] : $"Class {classId}";
                _classSelect.AddItem(displayName);
                _currentClassIds[i] = classId;
                i++;
            }

            _classSelect.Disabled = _currentClassIds.Length == 0;
            if (_currentClassIds.Length > 0)
            {
                if (GameState.IsCreatingStudent)
                {
                    int forcedIdx = 0;
                    for (int idx = 0; idx < _currentClassIds.Length; idx++) {
                        if (_currentClassIds[idx] == GameState.PendingStudentClassId) {
                            forcedIdx = idx;
                            break;
                        }
                    }
                    _classSelect.Selected = forcedIdx;
                    _classSelect.Disabled = true;
                    OnClassSelected(forcedIdx);
                }
                else
                {
                    _classSelect.Selected = 0;
                    OnClassSelected(0);
                }
            }
            else
            {
                _classDescription.Clear();
                _classDescription.AppendText("[color=#888888]No classes available for this race.[/color]");
            }

            // Extract face variant counts
            _faceCountMale = root.TryGetProperty("faceCountMale", out var fcm) ? fcm.GetInt32() : 1;
            _faceCountFemale = root.TryGetProperty("faceCountFemale", out var fcf) ? fcf.GetInt32() : 1;
            _selectedFace = 0;
            UpdatePreviewModel();

            ValidateCreateForm();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MENU] CharCreateData parse error: {ex.Message}");
        }
    }

    private void OnClassSelected(long index)
    {
        int i = (int)index;
        if (i < 0 || i >= _currentClassIds.Length) return;

        int classId = _currentClassIds[i];

        // Update class description
        _classDescription.Clear();
        if (ClassDescriptions.ContainsKey(classId))
            _classDescription.AppendText(ClassDescriptions[classId]);
        else
            _classDescription.AppendText($"[color=#888888]Class {classId}[/color]");

        // Populate deity list from the class data
        try
        {
            var classes = _currentCreateData.GetProperty("classes");
            foreach (var cls in classes.EnumerateArray())
            {
                if (cls.GetProperty("classId").GetInt32() == classId)
                {
                    // Get deities
                    var deityNames = cls.GetProperty("deityNames");
                    _deitySelect.Clear();
                    _currentDeityIds = new int[deityNames.GetArrayLength()];
                    int di = 0;
                    foreach (var d in deityNames.EnumerateArray())
                    {
                        _currentDeityIds[di] = d.GetProperty("id").GetInt32();
                        _deitySelect.AddItem(d.GetProperty("name").GetString());
                        di++;
                    }
                    _deitySelect.Disabled = _currentDeityIds.Length == 0;
                    if (_currentDeityIds.Length > 0) _deitySelect.Selected = 0;

                    // Load stat allocation data
                    var alloc = cls.GetProperty("allocation");
                    _baseStats[0] = alloc.GetProperty("base_str").GetInt32();
                    _baseStats[1] = alloc.GetProperty("base_sta").GetInt32();
                    _baseStats[2] = alloc.GetProperty("base_agi").GetInt32();
                    _baseStats[3] = alloc.GetProperty("base_dex").GetInt32();
                    _baseStats[4] = alloc.GetProperty("base_wis").GetInt32();
                    _baseStats[5] = alloc.GetProperty("base_int").GetInt32();
                    _baseStats[6] = alloc.GetProperty("base_cha").GetInt32();

                    // Calculate total pool from DB default allocations
                    _totalPool = (alloc.GetProperty("alloc_str").GetInt32()) +
                                 (alloc.GetProperty("alloc_sta").GetInt32()) +
                                 (alloc.GetProperty("alloc_agi").GetInt32()) +
                                 (alloc.GetProperty("alloc_dex").GetInt32()) +
                                 (alloc.GetProperty("alloc_int").GetInt32()) +
                                 (alloc.GetProperty("alloc_wis").GetInt32()) +
                                 (alloc.GetProperty("alloc_cha").GetInt32());

                    // Reset allocated points to zero
                    for (int s = 0; s < 7; s++) _allocStats[s] = 0;

                    UpdateStatDisplay();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MENU] Class selection error: {ex.Message}");
        }

        ValidateCreateForm();
    }

    // ═══════════════════════════════════════════════════════════════
    //  STAT POINT ALLOCATION
    // ═══════════════════════════════════════════════════════════════

    private void AdjustStat(int statIndex, int delta)
    {
        int currentSpent = 0;
        for (int i = 0; i < 7; i++) currentSpent += _allocStats[i];

        if (delta > 0 && currentSpent >= _totalPool) return; // No points left
        if (delta < 0 && _allocStats[statIndex] <= 0) return; // Can't go below 0

        _allocStats[statIndex] += delta;
        UpdateStatDisplay();
    }

    private void UpdateStatDisplay()
    {
        Label[] labels = { _previewStr, _previewSta, _previewAgi, _previewDex, _previewWis, _previewInt, _previewCha };

        int spent = 0;
        for (int i = 0; i < 7; i++)
        {
            int total = _baseStats[i] + _allocStats[i];
            labels[i].Text = total.ToString();
            spent += _allocStats[i];

            // Color: green if player added points, white for base, dim for low
            if (_allocStats[i] > 0)
                labels[i].AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
            else if (total >= 85)
                labels[i].AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            else
                labels[i].AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        }

        int remaining = _totalPool - spent;
        _pointsRemainingLabel.Text = $"Points: {remaining}";
        _pointsRemainingLabel.AddThemeColorOverride("font_color",
            remaining > 0 ? new Color(0.4f, 0.8f, 0.4f, 1f) : new Color(0.7f, 0.7f, 0.7f, 1f));

        // Enable/disable +/- buttons
        for (int i = 0; i < 7; i++)
        {
            _statPlusBtns[i].Disabled = remaining <= 0;
            _statMinusBtns[i].Disabled = _allocStats[i] <= 0;
        }

        int previewLevel = GameState.IsCreatingStudent ? Math.Max(1, GameState.PendingStudentLevel) : 1;
        int sta = _baseStats[1] + _allocStats[1];
        int wis = _baseStats[4] + _allocStats[4];
        int intel = _baseStats[5] + _allocStats[5];

        int curClassId = _currentClassIds.Length > 0 && _classSelect.Selected >= 0 && _classSelect.Selected < _currentClassIds.Length
            ? _currentClassIds[_classSelect.Selected] : 1;
        string classKey = ClassKeys.TryGetValue(curClassId, out string ck) ? ck : "warrior";

        _previewHp.Text = CharacterStatsPreview.CalcMaxHp(classKey, previewLevel, sta).ToString();
        int previewMana = CharacterStatsPreview.CalcMaxMana(classKey, previewLevel, wis, intel);
        _previewMana.Text = previewMana > 0 ? previewMana.ToString() : "—";
    }

    // ═══════════════════════════════════════════════════════════════
    //  LOGIN ACTIONS
    // ═══════════════════════════════════════════════════════════════

    private async void OnLoginPressed()
    {
        if (ValidateInputs())
        {
            _pendingAction = "login";
            await InitiateConnection();
        }
    }

    private void OnBackToServerSelectPressed()
    {
        // Tear down any in-flight connection so probes against the current
        // server URL don't fight with an open session.
        GameClient.Instance.DisconnectFromServer();
        _pendingAction = null;
        _loginButton.Disabled = false;
        _createAccountButton.Disabled = false;
        _loginStatusLabel.Text = "";
        ShowPanel("serverselect");
    }

    private async void OnCreateAccountPressed()
    {
        if (ValidateInputs())
        {
            _pendingAction = "create";
            await InitiateConnection();
        }
    }

    private bool ValidateInputs()
    {
        string username = _usernameInput.Text.Trim();
        string password = _passwordInput.Text;
        string serverUrl = _serverAddressInput.Text.Trim();

        if (username.Length < 2) { _loginStatusLabel.Text = "Name too short."; return false; }
        if (password.Length < 4) { _loginStatusLabel.Text = "Password too short (min 4)."; return false; }
        if (string.IsNullOrEmpty(serverUrl)) { _loginStatusLabel.Text = "Server address required."; return false; }
        if (!EQAssetConfig.Instance.IsConfigured)
        {
            _loginStatusLabel.Text = "Link your EverQuest installation before logging in.";
            return false;
        }
        return true;
    }

    private async System.Threading.Tasks.Task InitiateConnection()
    {
        _loginButton.Disabled = true;
        _createAccountButton.Disabled = true;

        GameClient.Instance.ServerUrl = _serverAddressInput.Text.Trim();

        if (!GameClient.Instance.IsSocketConnected)
        {
            _loginStatusLabel.Text = "Connecting to server...";
            GameClient.Instance.ConnectToServer();
        }
        else
        {
            ProcessPendingAction();
        }
    }

    private void ProcessPendingAction()
    {
        string username = _usernameInput.Text.Trim();
        string password = _passwordInput.Text;

        if (_pendingAction == "login")
        {
            _loginStatusLabel.Text = "Logging in...";
            GameState.AccountPassword = password;
            GameClient.Instance.SendMessage("LOGIN_ACCOUNT", new { username, password });
            GD.Print($"[MENU] Sent LOGIN_ACCOUNT: {username}");
        }
        else if (_pendingAction == "create")
        {
            _loginStatusLabel.Text = "Creating account...";
            GameClient.Instance.SendMessage("CREATE_ACCOUNT", new { username, password });
            GD.Print($"[MENU] Sent CREATE_ACCOUNT: {username}");
        }

        _pendingAction = null;

        if (_rememberCheckbox.ButtonPressed)
            SaveCredentials(username, password);
        else
            ClearSavedCredentials();
    }

    // ═══════════════════════════════════════════════════════════════
    //  CHARACTER SELECT ACTIONS
    // ═══════════════════════════════════════════════════════════════

    private void OnEnterWorldPressed()
    {
        if (_selectedCharName == null) return;
        
        GameClient.Instance.SendMessage("SELECT_CHARACTER", new { name = _selectedCharName });
        _enterWorldButton.Disabled = true;
        _enterWorldButton.Text = "Entering World...";
        GD.Print($"[MENU] Sent SELECT_CHARACTER: {_selectedCharName}");
    }

    private void OnDeleteCharPressed()
    {
        if (_selectedCharName == null) return;
        _deleteCharButton.Disabled = true;
        _deleteCharButton.Text = "Deleting...";
        GameClient.Instance.SendMessage("DELETE_CHARACTER", new { name = _selectedCharName });
        GD.Print($"[MENU] Sent DELETE_CHARACTER: {_selectedCharName}");
    }

    private void OnCharacterDeleted(Variant data)
    {
        if (!IsInstanceValid(this)) return;
        try
        {
            var root = JsonDocument.Parse(data.ToString()).RootElement;
            string deletedName = root.GetProperty("name").GetString();
            GD.Print($"[MENU] Character '{deletedName}' deleted.");

            _selectedCharName = null;
            _enterWorldButton.Disabled = true;
            _deleteCharButton.Disabled = true;
            _deleteCharButton.Text = "Delete Character";

            if (root.TryGetProperty("characters", out JsonElement chars))
            {
                PopulateCharacterList(chars);
                _charSelectHeader.Text = $"Characters ({chars.GetArrayLength()})";
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MENU] CharacterDeleted parse error: {ex.Message}");
        }
    }

    private void OnCreateCharPressed()
    {
        string name = _nameInput.Text.Trim();
        int raceIdx = _raceSelect.Selected;
        int classIdx = _classSelect.Selected;
        string race = RaceNames[raceIdx].ToLower().Replace(" ", "_");
        int classId = _currentClassIds[classIdx];
        string classKey = ClassKeys.ContainsKey(classId) ? ClassKeys[classId] : "warrior";
        int deity = (_currentDeityIds.Length > 0 && _deitySelect.Selected >= 0 && _deitySelect.Selected < _currentDeityIds.Length)
            ? _currentDeityIds[_deitySelect.Selected]
            : 396;

        _statusLabel.Text = GameState.IsCreatingStudent ? "Hiring student..." : "Creating character...";
        _playButton.Disabled = true;

        // Send stat allocations + appearance
        var statsPayload = $"\"stats\": {{\"str\": {_allocStats[0]}, \"sta\": {_allocStats[1]}, \"agi\": {_allocStats[2]}, \"dex\": {_allocStats[3]}, \"wis\": {_allocStats[4]}, \"int\": {_allocStats[5]}, \"cha\": {_allocStats[6]}}}";
        
        if (GameState.IsCreatingStudent)
        {
            var json = $"{{\"type\": \"HIRE_STUDENT\", \"name\": \"{name}\", \"raceId\": {RaceIds[raceIdx]}, \"classId\": {classId}, \"level\": {GameState.PendingStudentLevel}, \"deity\": {deity}, \"gender\": {_selectedGender}, \"face\": {_selectedFace}, {statsPayload}}}";
            GameClient.Instance.SendRaw(json);
            GD.Print($"[MENU] Sent HIRE_STUDENT: {name} (ClassId={classId}/RaceId={RaceIds[raceIdx]}) gender={_selectedGender} face={_selectedFace} deity={deity}");
        }
        else
        {
            var json = $"{{\"type\": \"CREATE_CHARACTER\", \"name\": \"{name}\", \"class\": \"{classKey}\", \"race\": \"{race}\", \"deity\": {deity}, \"gender\": {_selectedGender}, \"face\": {_selectedFace}, {statsPayload}}}";
            GameClient.Instance.SendRaw(json);
            GD.Print($"[MENU] Sent CREATE_CHARACTER: {name} ({classKey}/{race}) gender={_selectedGender} face={_selectedFace} deity={deity}");
        }
    }

    private int GetDragItem111ClassIconIndex(int classId)
    {
        return classId switch {
            1 => 27, // Warrior
            2 => 17, // Cleric
            3 => 16, // Paladin
            4 => 11, // Ranger
            5 => 23, // Shadow Knight
            6 => 21, // Druid
            7 => 15, // Monk
            8 => 4,  // Bard
            9 => 30, // Rogue
            10 => 31, // Shaman
            11 => 9,  // Necromancer
            12 => 22, // Wizard
            13 => 10, // Magician
            14 => 28, // Enchanter
            15 => 29, // Beastlord
            16 => 5,  // Berserker
            _ => 27
        };
    }

    private void PopulateCharacterList(JsonElement characters)
    {
        // Clear existing
        foreach (Node child in _charListContainer.GetChildren())
            child.QueueFree();

        _selectedCharName = null;
        _enterWorldButton.Disabled = true;
        _deleteCharButton.Disabled = true;

        if (_charSelectPreviewModel != null)
        {
            _charSelectPreviewModel.QueueFree();
            _charSelectPreviewModel = null;
        }

        if (characters.GetArrayLength() == 0)
        {
            var empty = new Label();
            empty.Text = "No characters yet. Create one!";
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f, 1f));
            empty.AddThemeFontSizeOverride("font_size", 13);
            _charListContainer.AddChild(empty);
            return;
        }

        foreach (var ch in characters.EnumerateArray())
        {
            string charName = ch.GetProperty("name").GetString();
            string className = ch.GetProperty("className").GetString();
            string race = ch.GetProperty("race").GetString();
            int level = ch.GetProperty("level").GetInt32();
            
            int raceId = ch.TryGetProperty("raceId", out var rProp) ? rProp.GetInt32() : 1;
            int gender = ch.TryGetProperty("gender", out var gProp) ? gProp.GetInt32() : 0;
            int face = ch.TryGetProperty("face", out var fProp) ? fProp.GetInt32() : 0;

            // Resolve Class ID
            int classId = 1;
            foreach (var kvp in ClassKeys)
            {
                if (kvp.Value.Equals(className, StringComparison.OrdinalIgnoreCase))
                {
                    classId = kvp.Key;
                    break;
                }
            }

            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(0, 50);
            btn.ToggleMode = true;
            btn.FocusMode = FocusModeEnum.None;

            var hbox = new HBoxContainer();
            hbox.SetAnchorsPreset(LayoutPreset.FullRect);
            hbox.AddThemeConstantOverride("separation", 10);
            
            // Margin for padding
            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddThemeConstantOverride("margin_top", 4);
            margin.AddThemeConstantOverride("margin_bottom", 4);
            margin.AddThemeConstantOverride("margin_right", 8);
            hbox.AddChild(margin);

            // Class Icon
            var iconRect = new TextureRect();
            iconRect.CustomMinimumSize = new Vector2(40, 40);
            iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            int localIndex = GetDragItem111ClassIconIndex(classId);
            iconRect.Texture = IconManager.Instance.GetItemIcon(4460 + localIndex);
            margin.AddChild(iconRect);

            // Text Info
            var vboxInfo = new VBoxContainer();
            vboxInfo.Alignment = BoxContainer.AlignmentMode.Center;
            vboxInfo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            vboxInfo.AddThemeConstantOverride("separation", -2);

            var nameLabel = new Label();
            nameLabel.Text = charName;
            nameLabel.AddThemeFontSizeOverride("font_size", 16);
            nameLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.6f, 1f));
            vboxInfo.AddChild(nameLabel);

            var classLabel = new Label();
            classLabel.Text = $"Lv{level} {Capitalize(race)} {Capitalize(className)}";
            classLabel.AddThemeFontSizeOverride("font_size", 12);
            classLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f, 1f));
            vboxInfo.AddChild(classLabel);

            hbox.AddChild(vboxInfo);
            btn.AddChild(hbox);

            // Capture for lambda
            string capturedName = charName;
            int capRaceId = raceId;
            int capGender = gender;
            int capFace = face;

            btn.Pressed += () =>
            {
                // Deselect all others
                foreach (Node child in _charListContainer.GetChildren())
                {
                    if (child is Button b && b != btn) b.ButtonPressed = false;
                }
                _selectedCharName = capturedName;
                _enterWorldButton.Disabled = false;
                _deleteCharButton.Disabled = false;
                
                UpdateCharSelectPreviewModel(capRaceId, capGender, capFace);
            };

            _charListContainer.AddChild(btn);
        }
    }

    private void UpdateCharSelectPreviewModel(int raceId, int gender, int face)
    {
        if (_charSelectPreviewRoot == null) return;

        if (_charSelectPreviewModel != null)
        {
            _charSelectPreviewModel.QueueFree();
            _charSelectPreviewModel = null;
        }

        if (!RaceModelCodes.TryGetValue(raceId, out string[] codes)) return;
        
        string modelCode = (gender == 0) ? codes[0] : codes[1];
        
        string modelPath;
        if (face > 0)
        {
            string facePath = $"res://Data/Characters/{modelCode}_face{face}.glb";
            modelPath = ResourceLoader.Exists(facePath) ? facePath : $"res://Data/Characters/{modelCode}.glb";
        }
        else
        {
            modelPath = $"res://Data/Characters/{modelCode}.glb";
        }

        if (!ResourceLoader.Exists(modelPath))
        {
            GD.Print($"[MENU] CharSelect preview model not found: {modelPath}");
            return;
        }

        try
        {
            // Use the imported PackedScene (works in both editor and packaged builds — raw
            // .glb source files are not bundled in the .pck, only the imported .scn is).
            var packed = GD.Load<PackedScene>(modelPath);
            if (packed == null)
            {
                GD.PrintErr($"[MENU] CharSelect failed to load PackedScene: {modelPath}");
                return;
            }
            Node3D scene = packed.Instantiate<Node3D>();
            if (scene == null) return;

            _charSelectPreviewModel = new Node3D();
            _charSelectPreviewModel.AddChild(scene);
            _charSelectPreviewRoot.AddChild(_charSelectPreviewModel);

            float scale = 1.0f;
            float yRotation = 0f;
            float camHeight = 1.0f;
            float lookAtY = 0.7f;
            float zoom = 12.0f;

            if (raceId == 9 || raceId == 10) scale = 0.7f;
            else if (raceId == 2) scale = 0.85f;
            else if (raceId == 8)
            {
                zoom = 18.0f;
                lookAtY = 0.52f;
                camHeight = 0.88f;
            }
            else if (raceId == 11 || raceId == 12)
            {
                scale = 1.0f;
                zoom = 19.0f;
                lookAtY = 0.5f;
                camHeight = 0.85f;
            }

            if (raceId == 128)
            {
                yRotation = Mathf.Pi;
                camHeight = 5.0f;
                lookAtY = 3.5f;
                zoom = 24.0f;
            }

            _charSelectPreviewModel.Scale = Vector3.One * scale;
            _charSelectPreviewModel.RotationDegrees = new Vector3(0, Mathf.RadToDeg(yRotation), 0);

            if (_charSelectPreviewCamera != null)
            {
                _charSelectPreviewCamera.Position = new Vector3(0, camHeight, zoom);
                _charSelectPreviewCamera.LookAtFromPosition(new Vector3(0, camHeight, zoom), new Vector3(0, lookAtY, 0), Vector3.Up);
            }

            var animPlayer = FindPreviewAnimationPlayer(scene);
            if (animPlayer != null)
            {
                string idleAnim = null;
                if (animPlayer.HasAnimation("p01")) idleAnim = "p01";
                else
                {
                    foreach (var animName in animPlayer.GetAnimationList())
                    {
                        if (animName.StartsWith("p")) { idleAnim = animName; break; }
                    }
                    if (idleAnim == null && animPlayer.GetAnimationList().Length > 0)
                        idleAnim = animPlayer.GetAnimationList()[0];
                }
                if (idleAnim != null) animPlayer.Play(idleAnim);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MENU] CharSelect Preview error: {ex.Message}");
        }
    }

    private static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = s.Replace("_", " ");
        return char.ToUpper(s[0]) + s.Substring(1);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SIGNAL HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private void OnClientConnected()
    {
        if (!IsInstanceValid(this)) return;
        _loginStatusLabel.Text = "Connected.";
        _loginButton.Disabled = false;
        _createAccountButton.Disabled = false;

        if (_pendingAction != null)
            ProcessPendingAction();
        else
            ShowPanel("login");
        GD.Print("[MENU] Connected to server.");
    }

    private void OnClientDisconnected()
    {
        if (!IsInstanceValid(this)) return;
        _loginStatusLabel.Text = "Disconnected. Restart to try again.";
        _loginButton.Disabled = true;
        _createAccountButton.Disabled = true;
    }

    private void OnAccountOkReceived(Variant data)
    {
        if (!IsInstanceValid(this)) return;
        try
        {
            var root = JsonDocument.Parse(data.ToString()).RootElement;
            string accountName = root.GetProperty("accountName").GetString();
            var characters = root.GetProperty("characters");

            GameState.AccountName = accountName;
            _charSelectHeader.Text = $"Characters — {accountName}";

            PopulateCharacterList(characters);
            ShowPanel("charselect");
            GD.Print($"[MENU] Account '{accountName}' authenticated. {characters.GetArrayLength()} characters.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MENU] AccountOk parse error: {ex.Message}");
        }
    }

    private void OnCharacterCreated(Variant data)
    {
        if (!IsInstanceValid(this)) return;
        try
        {
            var root = JsonDocument.Parse(data.ToString()).RootElement;
            string newName = root.GetProperty("name").GetString();
            var characters = root.GetProperty("characters");

            PopulateCharacterList(characters);
            _selectedCharName = newName;
            _enterWorldButton.Disabled = false;
            _enterWorldButton.Text = $"Enter World as {newName}";

            foreach (Node child in _charListContainer.GetChildren())
            {
                if (child is Button b && b.Text.Contains(newName))
                    b.ButtonPressed = true;
            }

            ShowPanel("charselect");
            GD.Print($"[MENU] Character '{newName}' created. Returned to select.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MENU] CharacterCreated parse error: {ex.Message}");
        }
    }

    private void OnLoginOkReceived(Variant data)
    {
        if (!IsInstanceValid(this)) return;
        try
        {
            var root = JsonDocument.Parse(data.ToString()).RootElement;
            string charName = root.GetProperty("character").GetProperty("name").GetString();
            GD.Print($"[MENU] Login OK — entering world as {charName}.");
            TransitionToGame(charName);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MENU] LoginOk parse error: {ex.Message}");
        }
    }

    private void OnClientMessageReceived(string type, string msg)
    {
        if (!IsInstanceValid(this)) return;
        if (type == "WELCOME") GD.Print("[MENU] Server welcomed us.");
        if (type == "HIRE_STUDENT_SUCCESS")
        {
            GD.Print("[MENU] Hire student successful! Transitioning back to game.");
            GameState.IsCreatingStudent = false;
            GetTree().CallDeferred("change_scene_to_file", "res://Scenes/MainUI.tscn");
            return;
        }
        if (type == "ERROR")
        {
            try
            {
                using var doc = JsonDocument.Parse(msg);
                string errMsg = doc.RootElement.GetProperty("message").GetString();

                if (_loginPanel.Visible)
                {
                    _loginStatusLabel.Text = $"Error: {errMsg}";
                    _loginButton.Disabled = false;
                    _createAccountButton.Disabled = false;
                }
                else if (_createPanel.Visible)
                {
                    _statusLabel.Text = $"Error: {errMsg}";
                    _playButton.Disabled = false;
                }
                else if (_charSelectPanel.Visible)
                {
                    _enterWorldButton.Text = $"Error: {errMsg}";
                    _enterWorldButton.Disabled = false;
                }
            }
            catch {}
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private void ValidateCreateForm()
    {
        string name = _nameInput.Text.Trim();
        bool valid = GameClient.Instance.IsSocketConnected
            && name.Length >= 2 && name.Length <= 20
            && _currentClassIds.Length > 0
            && _currentDeityIds.Length > 0;
        _playButton.Disabled = !valid;
    }

    // ═══════════════════════════════════════════════════════════════
    //  3D MODEL PREVIEW
    // ═══════════════════════════════════════════════════════════════

    private static readonly Dictionary<int, string[]> RaceModelCodes = new() {
        {1, new[]{"hum","huf"}}, {2, new[]{"bam","baf"}}, {3, new[]{"erm","erf"}},
        {4, new[]{"elm","elf"}}, {5, new[]{"him","hif"}}, {6, new[]{"dam","daf"}},
        {7, new[]{"ham","haf"}}, {8, new[]{"dwm","dwf"}}, {9, new[]{"trm","trf"}},
        {10, new[]{"ogm","ogf"}}, {11, new[]{"hom","hof"}}, {12, new[]{"gnm","gnf"}},
        {128, new[]{"ikm","ikf"}}, {130, new[]{"kem","kef"}}, {330, new[]{"frm","frf"}}
    };

    private void UpdatePreviewModel()
    {
        if (_previewRoot == null) return;

        if (_previewModel != null)
        {
            _previewModel.QueueFree();
            _previewModel = null;
        }

        int raceId = RaceIds[_raceSelect.Selected];
        int maxFaces = (_selectedGender == 0) ? _faceCountMale : _faceCountFemale;
        if (_selectedFace >= maxFaces) _selectedFace = 0;

        if (_faceLabel != null)
            _faceLabel.Text = $"{_selectedFace + 1} / {maxFaces}";

        if (_armorLabel != null)
            _armorLabel.Text = ArmorMaterialNames[_selectedArmorMaterial];

        if (!RaceModelCodes.ContainsKey(raceId)) return;
        string modelCode = RaceModelCodes[raceId][_selectedGender == 0 ? 0 : 1];

        string modelPath = $"res://Data/Characters/{modelCode}.glb";

        if (_selectedFace > 0)
        {
            if (modelCode != "frm" && modelCode != "frf" && modelCode != "kem" && modelCode != "kef")
            {
                string facePath = $"res://Data/Characters/{modelCode}_face{_selectedFace}.glb";
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

        if (!ResourceLoader.Exists(modelPath))
        {
            GD.Print($"[MENU] Preview model not found: {modelPath}");
            return;
        }

        try
        {
            // Use the imported PackedScene (works in both editor and packaged builds — raw
            // .glb source files are not bundled in the .pck, only the imported .scn is).
            var packed = GD.Load<PackedScene>(modelPath);
            if (packed == null)
            {
                GD.PrintErr($"[MENU] CharCreate failed to load PackedScene: {modelPath}");
                return;
            }
            Node3D scene = packed.Instantiate<Node3D>();
            if (scene == null) return;

            if (useMergedAnims)
            {
                var mergedPacked = GD.Load<PackedScene>(mergedPath);
                if (mergedPacked != null)
                {
                    Node3D mergedScene = mergedPacked.Instantiate<Node3D>();
                    AnimationPlayer mergedAnim = FindPreviewAnimationPlayer(mergedScene);
                    if (mergedAnim != null)
                    {
                        mergedAnim.Owner = null;
                        mergedAnim.GetParent().RemoveChild(mergedAnim);
                        scene.AddChild(mergedAnim);
                        mergedAnim.Owner = scene;

                        // Fix broken track paths from the merged GLB
                        Skeleton3D skeleton = FindSkeleton(scene);
                        if (skeleton != null)
                        {
                            mergedAnim.RootNode = new NodePath("..");
                            string skeletonPath = scene.GetPathTo(skeleton);
                            foreach (var animName in mergedAnim.GetAnimationList())
                            {
                                var anim = mergedAnim.GetAnimation(animName);
                                for (int i = 0; i < anim.GetTrackCount(); i++)
                                {
                                    string oldPath = anim.TrackGetPath(i).ToString();
                                    string subPath = oldPath.Contains(':') ? oldPath.Substring(oldPath.IndexOf(':')) : "";
                                    subPath = subPath.Replace("Clone of ", "");
                                    
                                    string newPath = "";
                                    if (oldPath.Contains("Skeleton3D"))
                                    {
                                        newPath = skeletonPath + subPath;
                                    }
                                    else
                                    {
                                        newPath = "." + subPath;
                                    }
                                    anim.TrackSetPath(i, newPath);
                                    if (animName == "p01" && i < 5) GD.Print($"[DEBUG] Track {i} Old: {oldPath} -> New: {newPath}");
                                }
                            }
                        }

                        GD.Print($"[MENU] Extracted AnimationPlayer from {mergedPath} and attached to {modelPath}");
                    }
                    mergedScene.QueueFree();
                }
            }

            _previewModel = new Node3D();
            _previewModel.AddChild(scene);
            _previewRoot.AddChild(_previewModel);

            float scale = 1.0f;
            float yRotation = 0f;
            _previewCamHeight = 1.0f;
            _previewLookAtY = 0.7f;
            _previewZoom = 12.0f;
            _previewZoomMin = 2.0f;
            _previewZoomMax = 28.0f;

            if (raceId == 9 || raceId == 10) scale = 0.7f;
            else if (raceId == 2) scale = 0.85f;
            else if (raceId == 8)
            {
                _previewZoom = 18.0f;
                _previewLookAtY = 0.52f;
                _previewCamHeight = 0.88f;
                _previewZoomMax = 36.0f;
            }
            else if (raceId == 11 || raceId == 12)
            {
                scale = 1.0f;
                _previewZoom = 19.0f;
                _previewLookAtY = 0.5f;
                _previewCamHeight = 0.85f;
                _previewZoomMax = 36.0f;
            }

            if (raceId == 128)
            {
                yRotation = Mathf.Pi;
                _previewCamHeight = 5.0f;
                _previewLookAtY = 3.5f;
                _previewZoom = 24.0f;
            }
            else if (raceId == 130)
            {
                yRotation = 0f; // 0 degrees
                _previewCamHeight = 5.0f;
                _previewLookAtY = 3.5f;
                _previewZoom = 28.0f;
            }

            _previewModel.Scale = Vector3.One * scale;
            _previewModel.RotationDegrees = new Vector3(0, Mathf.RadToDeg(yRotation), 0);

            var animPlayer = FindPreviewAnimationPlayer(scene);
            if (animPlayer != null)
            {
                GD.Print($"[MENU] Found AnimationPlayer for {modelCode}");
                string idleAnim = null;
                var animList = animPlayer.GetAnimationList();
                GD.Print($"[MENU] Animation list: {(animList.Length > 0 ? string.Join(", ", animList) : "EMPTY")}");
                
                if (animPlayer.HasAnimation("p01"))
                    idleAnim = "p01";
                else
                {
                    foreach (var animName in animList)
                    {
                        if (animName.StartsWith("p") && animName != "pos")
                        {
                            idleAnim = animName;
                            break;
                        }
                    }
                    if (idleAnim == null && animList.Length > 0)
                        idleAnim = animList[0];
                }
                if (idleAnim != null)
                {
                    animPlayer.Play(idleAnim);
                    GD.Print($"[MENU] Preview playing animation: {idleAnim}");
                }
                else
                {
                    GD.Print($"[MENU] Could not determine idleAnim for {modelCode}");
                }
            }
            else
            {
                GD.Print($"[MENU] No AnimationPlayer found in scene for {modelCode}!");
                PrintSceneTree(scene, "");
            }

            GD.Print($"[MENU] Preview loaded: {modelPath}");

            if (_selectedArmorMaterial > 0)
            {
                ApplyArmorTextures(scene, modelCode, _selectedArmorMaterial);
            }
            if (_selectedFace > 0)
            {
                ApplyFaceTexture(scene, modelCode, _selectedFace);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MENU] Preview load error: {ex.Message}");
        }
    }

    private static void PrintSceneTree(Node node, string indent)
    {
        GD.Print($"{indent}- {node.Name} ({node.GetType().Name})");
        foreach (var child in node.GetChildren())
        {
            PrintSceneTree(child, indent + "  ");
        }
    }

    private static Skeleton3D FindSkeleton(Node node)
    {
        if (node is Skeleton3D skel) return skel;
        foreach (var child in node.GetChildren())
        {
            var result = FindSkeleton(child);
            if (result != null) return result;
        }
        return null;
    }

    private static AnimationPlayer FindPreviewAnimationPlayer(Node root)
    {
        if (root is AnimationPlayer ap) return ap;
        foreach (var child in root.GetChildren())
        {
            var found = FindPreviewAnimationPlayer(child);
            if (found != null) return found;
        }
        return null;
    }

    private static readonly string[] ArmorBodyParts = { "ch", "lg", "ft", "ua", "fa", "hn" };

    private void ApplyArmorTextures(Node modelRoot, string raceCode, int material)
    {
        string matStr = material.ToString("D2");
        int swapped = 0;

        ApplyArmorToNode(modelRoot, raceCode, matStr, ref swapped);
        GD.Print($"[MENU] Armor material {material}: swapped {swapped} textures on {raceCode}");
    }

    private void ApplyArmorToNode(Node node, string raceCode, string matStr, ref int swapped)
    {
        if (node is MeshInstance3D meshInst)
        {
            for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
            {
                var mat = meshInst.GetActiveMaterial(i);
                if (mat is not StandardMaterial3D stdMat) continue;

                string matName = stdMat.ResourceName;
                if (string.IsNullOrEmpty(matName)) continue;

                foreach (var part in ArmorBodyParts)
                {
                    string partPrefix = $"{raceCode}{part}";
                    int idx = matName.IndexOf(partPrefix, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) continue;

                    string afterPart = matName.Substring(idx + partPrefix.Length);
                    string digits = "";
                    foreach (char c in afterPart) { if (char.IsDigit(c)) digits += c; }
                    if (digits.Length < 2) break;
                    string piece = digits.Substring(digits.Length - 2);

                    string texFile = $"{raceCode}{part}{matStr}{piece}.png";
                    string texPath = $"res://Data/Characters/Textures/{texFile}";

                    if (!ResourceLoader.Exists(texPath)) break;

                    var armorTex = GD.Load<Texture2D>(texPath);
                    if (armorTex == null) break;

                    var newMat = (StandardMaterial3D)stdMat.Duplicate();
                    newMat.AlbedoTexture = armorTex;
                    meshInst.SetSurfaceOverrideMaterial(i, newMat);
                    swapped++;
                    break;
                }
            }
        }

        foreach (var child in node.GetChildren())
        {
            ApplyArmorToNode(child, raceCode, matStr, ref swapped);
        }
    }

    private void ApplyFaceTexture(Node modelRoot, string raceCode, int face)
    {
        int swapped = 0;
        GD.Print($"[FACE-SWAP] ApplyFaceTexture called for raceCode: {raceCode}, face: {face}");
        
        if (raceCode == "frm" || raceCode == "frf" || raceCode == "kem" || raceCode == "kef")
        {
            GD.Print($"[FACE-SWAP] Routing to ApplyLuclinSkinToNode");
            ApplyLuclinSkinToNode(modelRoot, raceCode, face, ref swapped);
        }
        else
        {
            ApplyFaceToNode(modelRoot, raceCode, face, ref swapped);
        }
        GD.Print($"[FACE-SWAP] Completed. Swapped {swapped} textures.");
    }

    private void ApplyLuclinSkinToNode(Node node, string raceCode, int face, ref int swapped)
    {
        if (node is MeshInstance3D meshInst)
        {
            int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 0;
            int overrideCount = meshInst.GetSurfaceOverrideMaterialCount();
            GD.Print($"[LUCLIN-SKIN] Found MeshInstance: {node.Name}. SurfaceCount: {surfaceCount}, OverrideCount: {overrideCount}");
            
            for (int i = 0; i < surfaceCount; i++)
            {
                var mat = meshInst.GetActiveMaterial(i);
                if (mat == null)
                {
                    GD.Print($"[LUCLIN-SKIN] Surface {i} on {node.Name} has null ActiveMaterial");
                    continue;
                }
                
                if (mat is not StandardMaterial3D stdMat)
                {
                    GD.Print($"[LUCLIN-SKIN] Surface {i} on {node.Name} material is not StandardMaterial3D, it is {mat.GetType().Name}");
                    continue;
                }

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

                if (string.IsNullOrEmpty(matName))
                {
                    GD.Print($"[LUCLIN-SKIN] Surface {i} on {node.Name} has empty ResourceName and no AlbedoTexture!");
                    continue;
                }

                if (matName.StartsWith("d_"))
                {
                    matName = matName.Substring(2);
                }

                if ((raceCode == "kem" || raceCode == "kef") && matName.EndsWith("0001"))
                {
                    // Vah Shir merged GLB uses cloth armor (0001) as default materials.
                    // We must map these back to skin materials (sk01) to show proper naked tiger stripes!
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

                if (!ResourceLoader.Exists(texPath))
                {
                    GD.Print($"[LUCLIN-SKIN] TexPath not found: {texPath}");
                    continue;
                }

                var faceTex = GD.Load<Texture2D>(texPath);
                if (faceTex == null)
                {
                    GD.Print($"[LUCLIN-SKIN] Failed to load Texture2D from: {texPath}");
                    continue;
                }

                var newMat = (StandardMaterial3D)stdMat.Duplicate();
                newMat.AlbedoTexture = faceTex;
                meshInst.SetSurfaceOverrideMaterial(i, newMat);
                swapped++;
                GD.Print($"[FROG-SKIN] Successfully swapped {matName} to {texPath}");
            }
        }

        foreach (var child in node.GetChildren())
        {
            ApplyLuclinSkinToNode(child, raceCode, face, ref swapped);
        }
    }

    private void ApplyFaceToNode(Node node, string raceCode, int face, ref int swapped)
    {
        if (node is MeshInstance3D meshInst)
        {
            for (int i = 0; i < meshInst.GetSurfaceOverrideMaterialCount(); i++)
            {
                var mat = meshInst.GetActiveMaterial(i);
                if (mat is not StandardMaterial3D stdMat) continue;

                string matName = stdMat.ResourceName;
                if (string.IsNullOrEmpty(matName)) continue;

                string partPrefix = $"{raceCode}he";
                int idx = matName.IndexOf(partPrefix, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                string afterPart = matName.Substring(idx + partPrefix.Length);
                string digits = "";
                foreach (char c in afterPart) { if (char.IsDigit(c)) digits += c; }
                if (digits.Length < 2) continue;
                string piece = digits.Substring(digits.Length - 2);

                int pieceNum = int.Parse(piece);
                pieceNum += face * 10;
                string targetPiece = pieceNum.ToString("D2");

                string texFile = $"{raceCode}he00{targetPiece}.png";
                string texPath = $"res://Data/Characters/Textures/{texFile}";

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
            ApplyFaceToNode(child, raceCode, face, ref swapped);
        }
    }

    private float _charSelectPreviewRotation = -Mathf.Pi / 2f;
    private bool _adminCameraMode = false;
    private float _adminPitch = 0f;
    private float _adminYaw = 0f;

    public override void _Process(double delta)
    {
        // Keep the server-select probes ticking while that panel is visible.
        PollServerProbes();

        if (_adminCameraMode && _backdropCamera != null)
        {
            float speed = 20.0f;
            if (Input.IsKeyPressed(Key.Shift)) speed *= 3f;
            
            Vector3 velocity = Vector3.Zero;
            if (Input.IsKeyPressed(Key.W)) velocity += -_backdropCamera.GlobalTransform.Basis.Z;
            if (Input.IsKeyPressed(Key.S)) velocity += _backdropCamera.GlobalTransform.Basis.Z;
            if (Input.IsKeyPressed(Key.A)) velocity += -_backdropCamera.GlobalTransform.Basis.X;
            if (Input.IsKeyPressed(Key.D)) velocity += _backdropCamera.GlobalTransform.Basis.X;
            if (Input.IsKeyPressed(Key.E)) velocity += Vector3.Up;
            if (Input.IsKeyPressed(Key.Q)) velocity += Vector3.Down;
            
            if (velocity != Vector3.Zero)
            {
                _backdropCamera.GlobalPosition += velocity.Normalized() * speed * (float)delta;
            }
        }

        if (_previewRoot != null && _createPanel != null && _createPanel.Visible)
        {
            if (_previewAutoRotate)
            {
                _previewRotation += (float)delta * 0.5f;
                _previewRoot.Rotation = new Vector3(0, _previewRotation, 0);
            }

            if (_previewCamera != null && _previewCamera.IsInsideTree())
            {
                _previewCamera.Position = new Vector3(0, _previewCamHeight, _previewZoom);
                _previewCamera.LookAt(new Vector3(0, _previewLookAtY, 0), Vector3.Up);
            }
        }

        if (_charSelectPreviewRoot != null && _charSelectPanel != null && _charSelectPanel.Visible)
        {
            _charSelectPreviewRotation += (float)delta * 0.5f;
            _charSelectPreviewRoot.Rotation = new Vector3(0, _charSelectPreviewRotation, 0);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey ek && ek.Pressed && !ek.Echo)
        {
            if (GetViewport().GuiGetFocusOwner() is Godot.LineEdit) return;

            if (ek.Keycode == Key.Key1)
            {
                if (_loginPanel != null) _loginPanel.Visible = !_loginPanel.Visible;
                GetViewport().SetInputAsHandled();
            }
            else if (ek.Keycode == Key.Key2)
            {
                _adminCameraMode = !_adminCameraMode;
                Input.MouseMode = _adminCameraMode ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                
                if (_adminCameraMode && _backdropCamera != null)
                {
                    _adminPitch = _backdropCamera.Rotation.X;
                    _adminYaw = _backdropCamera.Rotation.Y;
                }
                
                GetViewport().SetInputAsHandled();
            }
            else if (ek.Keycode == Key.Key3)
            {
                if (_backdropCamera != null)
                {
                    GD.Print($"[ADMIN] Camera Pos: {_backdropCamera.GlobalPosition}, Rot: {_backdropCamera.GlobalRotationDegrees}, FOV: {_backdropCamera.Fov}");
                }
                GetViewport().SetInputAsHandled();
            }
            else if (ek.Keycode == Key.Key4)
            {
                if (_backdropCamera != null)
                {
                    _backdropCamera.Fov = _backdropCamera.Fov >= 105f ? 45f : (_backdropCamera.Fov + 15f);
                    GD.Print($"[ADMIN] Camera FOV set to: {_backdropCamera.Fov}");
                }
                GetViewport().SetInputAsHandled();
            }
        }

        if (_adminCameraMode && @event is InputEventMouseMotion mm && _backdropCamera != null)
        {
            float sensitivity = 0.005f;
            _adminYaw -= mm.Relative.X * sensitivity;
            _adminPitch -= mm.Relative.Y * sensitivity;
            _adminPitch = Mathf.Clamp(_adminPitch, -Mathf.Pi/2f, Mathf.Pi/2f);
            
            _backdropCamera.Rotation = new Vector3(_adminPitch, _adminYaw, 0);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_createPanel != null && _createPanel.Visible && @event is InputEventMouseButton mb)
        {
            if (_previewContainer != null && _previewContainer.GetGlobalRect().HasPoint(mb.GlobalPosition))
            {
                if (mb.ButtonIndex == MouseButton.WheelUp)
                {
                    _previewZoom = Mathf.Max(_previewZoomMin, _previewZoom - 0.5f);
                    GetViewport().SetInputAsHandled();
                }
                else if (mb.ButtonIndex == MouseButton.WheelDown)
                {
                    _previewZoom = Mathf.Min(_previewZoomMax, _previewZoom + 0.5f);
                    GetViewport().SetInputAsHandled();
                }
            }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            GetTree().Quit();
        }
    }

    private void TransitionToGame(string characterName)
    {
        GameState.CharacterName = characterName;
        GetTree().ChangeSceneToFile("res://Scenes/MainUI.tscn");
    }

    private const string CredentialsPath = "user://login.cfg";

    private void SaveCredentials(string username, string password)
    {
        var config = new ConfigFile();
        config.SetValue("login", "username", username);
        config.SetValue("login", "password", password);
        config.SetValue("login", "remember", true);
        config.SetValue("server", "address", _serverAddressInput.Text.Trim());
        config.Save(CredentialsPath);
        GD.Print("[MENU] Credentials and server settings saved");
    }

    private void ClearSavedCredentials()
    {
        var config = new ConfigFile();
        if (config.Load(CredentialsPath) == Error.Ok)
        {
            config.SetValue("login", "remember", false);
            config.SetValue("server", "address", _serverAddressInput.Text.Trim());
            config.Save(CredentialsPath);
        }
    }

    private void LoadSavedCredentials()
    {
        var config = new ConfigFile();
        if (config.Load(CredentialsPath) != Error.Ok) return;

        string serverAddr = (string)config.GetValue("server", "address", "ws://localhost:3005");
        if (_serverAddressInput != null) _serverAddressInput.Text = serverAddr;

        bool remember = (bool)config.GetValue("login", "remember", false);
        if (!remember) return;

        string user = (string)config.GetValue("login", "username", "");
        string pass = (string)config.GetValue("login", "password", "");

        if (user.Length > 0 && _usernameInput != null && _passwordInput != null)
        {
            _usernameInput.Text = user;
            _passwordInput.Text = pass;
            if (_rememberCheckbox != null) _rememberCheckbox.ButtonPressed = true;
            GD.Print($"[MENU] Loaded saved credentials for: {user}");
        }
    }
}
