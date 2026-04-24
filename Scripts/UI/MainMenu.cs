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

    // ── Login Panel Nodes ─────────────────────────────────────────
    private LineEdit _usernameInput;
    private LineEdit _passwordInput;
    private Button _loginButton;
    private Button _createAccountButton;
    private Label _loginStatusLabel;

    // ── Character Select Panel Nodes ──────────────────────────────
    private VBoxContainer _charListContainer;
    private Button _enterWorldButton;
    private Button _newCharButton;
    private Button _deleteCharButton;
    private Label _charSelectHeader;
    private string _selectedCharName = null;

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

    // Networking
    private bool _waitingForLogin = false;

    // Race data — all 15 classic EQ races
    private static readonly string[] RaceNames = {
        "Human", "Barbarian", "Erudite", "Wood Elf", "High Elf",
        "Dark Elf", "Half Elf", "Dwarf", "Troll", "Ogre",
        "Halfling", "Gnome", "Iksar", "Vah Shir", "Froglok"
    };
    private static readonly int[] RaceIds = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 128, 130, 330 };

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

    // ═══════════════════════════════════════════════════════════════
    //  READY
    // ═══════════════════════════════════════════════════════════════

    public override async void _Ready()
    {
        // ── Title Panel ──
        _titlePanel = GetNode<Control>("TitlePanel");

        // ── Build Login Panel programmatically ──
        BuildLoginPanel();

        // ── Build Character Select Panel programmatically ──
        BuildCharSelectPanel();

        // ── Build Create Panel programmatically (replacing .tscn version) ──
        BuildCreatePanel();

        // Hide all panels initially except login
        _createPanel.Hide();
        _charSelectPanel.Hide();
        _loginPanel.Hide();

        // ── Start server & connect ──
        _loginStatusLabel.Text = "Starting local server...";

        GameState.StartServer();
        await ToSignal(GetTree().CreateTimer(0.5f), "timeout");

        _loginStatusLabel.Text = "Connecting...";
        GameClient.Instance.ConnectToServer();
        
        // Listen to signals
        GameClient.Instance.Connected += OnClientConnected;
        GameClient.Instance.Disconnected += OnClientDisconnected;
        GameClient.Instance.AccountOkReceived += OnAccountOkReceived;
        GameClient.Instance.CharacterCreated += OnCharacterCreated;
        GameClient.Instance.CharacterDeleted += OnCharacterDeleted;
        GameClient.Instance.CharCreateDataReceived += OnCharCreateDataReceived;
        GameClient.Instance.LoginOkReceived += OnLoginOkReceived;
        GameClient.Instance.MessageReceived += OnClientMessageReceived;
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
        loginStyle.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.92f);
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

        // Username
        var userLabel = new Label();
        userLabel.Text = "Account Name";
        userLabel.AddThemeFontSizeOverride("font_size", 13);
        userLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        vbox.AddChild(userLabel);

        _usernameInput = new LineEdit();
        _usernameInput.PlaceholderText = "Enter account name...";
        _usernameInput.MaxLength = 30;
        _usernameInput.AddThemeFontSizeOverride("font_size", 15);
        vbox.AddChild(_usernameInput);

        // Password
        var passLabel = new Label();
        passLabel.Text = "Password";
        passLabel.AddThemeFontSizeOverride("font_size", 13);
        passLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f, 1f));
        vbox.AddChild(passLabel);

        _passwordInput = new LineEdit();
        _passwordInput.PlaceholderText = "Enter password...";
        _passwordInput.Secret = true;
        _passwordInput.AddThemeFontSizeOverride("font_size", 15);
        vbox.AddChild(_passwordInput);

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

        // Status
        _loginStatusLabel = new Label();
        _loginStatusLabel.Text = "";
        _loginStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _loginStatusLabel.AddThemeFontSizeOverride("font_size", 12);
        _loginStatusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f, 1f));
        vbox.AddChild(_loginStatusLabel);

        (_loginPanel as PanelContainer).AddChild(vbox);
        AddChild(_loginPanel);
    }

    // ═══════════════════════════════════════════════════════════════
    //  BUILD CHARACTER SELECT PANEL
    // ═══════════════════════════════════════════════════════════════

    private void BuildCharSelectPanel()
    {
        _charSelectPanel = new PanelContainer();
        _charSelectPanel.Name = "CharSelectPanel";
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.92f);
        style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = 2;
        style.BorderColor = new Color(0.7f, 0.55f, 0.2f, 0.8f);
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 6;
        style.ShadowColor = new Color(0, 0, 0, 0.6f);
        style.ShadowSize = 12;
        (_charSelectPanel as PanelContainer).AddThemeStyleboxOverride("panel", style);

        _charSelectPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _charSelectPanel.OffsetLeft = -240;
        _charSelectPanel.OffsetRight = 240;
        _charSelectPanel.OffsetTop = -220;
        _charSelectPanel.OffsetBottom = 220;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        vbox.OffsetLeft = 20; vbox.OffsetTop = 20; vbox.OffsetRight = -20; vbox.OffsetBottom = -20;
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);

        // Header
        _charSelectHeader = new Label();
        _charSelectHeader.Text = "Select Character";
        _charSelectHeader.HorizontalAlignment = HorizontalAlignment.Center;
        _charSelectHeader.AddThemeFontSizeOverride("font_size", 22);
        _charSelectHeader.AddThemeColorOverride("font_color", new Color(0.85f, 0.7f, 0.25f, 1f));
        vbox.AddChild(_charSelectHeader);

        // Scrollable character list
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.CustomMinimumSize = new Vector2(0, 280);

        _charListContainer = new VBoxContainer();
        _charListContainer.AddThemeConstantOverride("separation", 6);
        _charListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_charListContainer);
        vbox.AddChild(scroll);

        // Enter world button
        _enterWorldButton = new Button();
        _enterWorldButton.Text = "Enter World";
        _enterWorldButton.CustomMinimumSize = new Vector2(0, 44);
        _enterWorldButton.AddThemeFontSizeOverride("font_size", 18);
        _enterWorldButton.Disabled = true;
        _enterWorldButton.Pressed += OnEnterWorldPressed;
        vbox.AddChild(_enterWorldButton);

        // New character button
        _newCharButton = new Button();
        _newCharButton.Text = "Create New Character";
        _newCharButton.CustomMinimumSize = new Vector2(0, 36);
        _newCharButton.AddThemeFontSizeOverride("font_size", 14);
        _newCharButton.Pressed += () => ShowPanel("create");
        vbox.AddChild(_newCharButton);

        // Delete character button
        _deleteCharButton = new Button();
        _deleteCharButton.Text = "Delete Character";
        _deleteCharButton.CustomMinimumSize = new Vector2(0, 36);
        _deleteCharButton.AddThemeFontSizeOverride("font_size", 14);
        _deleteCharButton.AddThemeColorOverride("font_color", new Color(0.8f, 0.3f, 0.3f, 1f));
        _deleteCharButton.Disabled = true;
        _deleteCharButton.Pressed += OnDeleteCharPressed;
        vbox.AddChild(_deleteCharButton);

        (_charSelectPanel as PanelContainer).AddChild(vbox);
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
        style.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.92f);
        style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = 2;
        style.BorderColor = new Color(0.7f, 0.55f, 0.2f, 0.8f);
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 6;
        style.ShadowColor = new Color(0, 0, 0, 0.6f);
        style.ShadowSize = 12;
        (_createPanel as PanelContainer).AddThemeStyleboxOverride("panel", style);

        _createPanel.SetAnchorsPreset(LayoutPreset.Center);
        _createPanel.OffsetLeft = -310;
        _createPanel.OffsetRight = 310;
        _createPanel.OffsetTop = -300;
        _createPanel.OffsetBottom = 300;

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

        // ── Two-Column Layout: Left = Selections, Right = Description ──
        var hSplit = new HBoxContainer();
        hSplit.AddThemeConstantOverride("separation", 16);
        hSplit.SizeFlagsVertical = SizeFlags.ExpandFill;

        // LEFT COLUMN
        var leftCol = new VBoxContainer();
        leftCol.AddThemeConstantOverride("separation", 8);
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftCol.CustomMinimumSize = new Vector2(260, 0);

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
        statsStyle.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.9f);
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

            // Stat name
            var nameLabel = new Label();
            nameLabel.Text = StatNames[i];
            nameLabel.CustomMinimumSize = new Vector2(35, 0);
            nameLabel.AddThemeFontSizeOverride("font_size", 12);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f, 1f));
            statsGrid.AddChild(nameLabel);

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

        // RIGHT COLUMN — Class description
        var rightCol = new VBoxContainer();
        rightCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var descPanel = new PanelContainer();
        descPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        var descStyle = new StyleBoxFlat();
        descStyle.BgColor = new Color(0.03f, 0.03f, 0.05f, 0.9f);
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
                _classSelect.Selected = 0;
                OnClassSelected(0);
            }
            else
            {
                _classDescription.Clear();
                _classDescription.AppendText("[color=#888888]No classes available for this race.[/color]");
            }

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

        // Update HP/Mana preview (approximate)
        int sta = _baseStats[1] + _allocStats[1];
        _previewHp.Text = (sta + 10).ToString(); // Simplified L1 HP preview
        int wis = _baseStats[4] + _allocStats[4];
        int intel = _baseStats[5] + _allocStats[5];
        bool hasMana = _totalPool > 0; // Classes with alloc points typically have mana (rough heuristic)
        // Check if current class is a non-mana class
        int curClassId = _currentClassIds.Length > 0 && _classSelect.Selected >= 0 && _classSelect.Selected < _currentClassIds.Length
            ? _currentClassIds[_classSelect.Selected] : 0;
        bool isMelee = curClassId == 1 || curClassId == 7 || curClassId == 9 || curClassId == 16; // war/monk/rog/ber
        _previewMana.Text = isMelee ? "—" : Math.Max(wis, intel).ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    //  LOGIN ACTIONS
    // ═══════════════════════════════════════════════════════════════

    private void OnLoginPressed()
    {
        string username = _usernameInput.Text.Trim();
        string password = _passwordInput.Text;

        if (username.Length < 2) { _loginStatusLabel.Text = "Name too short."; return; }
        if (password.Length < 4) { _loginStatusLabel.Text = "Password too short (min 4)."; return; }

        _loginStatusLabel.Text = "Logging in...";
        _loginButton.Disabled = true;
        _createAccountButton.Disabled = true;

        GameClient.Instance.SendMessage("LOGIN_ACCOUNT", new { username, password });
        GD.Print($"[MENU] Sent LOGIN_ACCOUNT: {username}");
    }

    private void OnCreateAccountPressed()
    {
        string username = _usernameInput.Text.Trim();
        string password = _passwordInput.Text;

        if (username.Length < 2) { _loginStatusLabel.Text = "Name too short."; return; }
        if (password.Length < 4) { _loginStatusLabel.Text = "Password too short (min 4)."; return; }

        _loginStatusLabel.Text = "Creating account...";
        _loginButton.Disabled = true;
        _createAccountButton.Disabled = true;

        GameClient.Instance.SendMessage("CREATE_ACCOUNT", new { username, password });
        GD.Print($"[MENU] Sent CREATE_ACCOUNT: {username}");
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

        _statusLabel.Text = "Creating character...";
        _playButton.Disabled = true;

        // Send stat allocations
        var statsPayload = $"\"stats\": {{\"str\": {_allocStats[0]}, \"sta\": {_allocStats[1]}, \"agi\": {_allocStats[2]}, \"dex\": {_allocStats[3]}, \"wis\": {_allocStats[4]}, \"int\": {_allocStats[5]}, \"cha\": {_allocStats[6]}}}";
        var json = $"{{\"type\": \"CREATE_CHARACTER\", \"name\": \"{name}\", \"class\": \"{classKey}\", \"race\": \"{race}\", \"deity\": {deity}, {statsPayload}}}";
        GameClient.Instance.SendRaw(json);
        GD.Print($"[MENU] Sent CREATE_CHARACTER: {name} ({classKey}/{race}) deity={deity}");
    }

    private void PopulateCharacterList(JsonElement characters)
    {
        // Clear existing
        foreach (Node child in _charListContainer.GetChildren())
            child.QueueFree();

        _selectedCharName = null;
        _enterWorldButton.Disabled = true;

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

            var btn = new Button();
            btn.Text = $"  {charName}  —  Lv{level} {Capitalize(race)} {Capitalize(className)}";
            btn.CustomMinimumSize = new Vector2(0, 40);
            btn.AddThemeFontSizeOverride("font_size", 14);
            btn.Alignment = HorizontalAlignment.Left;
            btn.ToggleMode = true;
            
            // Capture for lambda
            string capturedName = charName;
            btn.Pressed += () =>
            {
                // Deselect all others
                foreach (Node child in _charListContainer.GetChildren())
                {
                    if (child is Button b && b != btn) b.ButtonPressed = false;
                }
                _selectedCharName = capturedName;
                _enterWorldButton.Disabled = false;
                _enterWorldButton.Text = $"Enter World as {capturedName}";
                _deleteCharButton.Disabled = false;
            };

            _charListContainer.AddChild(btn);
        }
    }

    private static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0]) + s.Substring(1);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SIGNAL HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private void OnClientConnected()
    {
        if (!IsInstanceValid(this)) return;
        _loginStatusLabel.Text = "Connected. Login or create an account.";
        _loginButton.Disabled = false;
        _createAccountButton.Disabled = false;

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

            // Auto-select the new character button
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
        if (type == "ERROR")
        {
            try
            {
                using var doc = JsonDocument.Parse(msg);
                string errMsg = doc.RootElement.GetProperty("message").GetString();

                // Show error on whichever panel is visible
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

    public override void _Process(double delta)
    {
        // WebSocket polling handled by GameClient singleton
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            GameState.StopServer();
            GetTree().Quit();
        }
    }

    private void TransitionToGame(string characterName)
    {
        GameState.CharacterName = characterName;
        GetTree().ChangeSceneToFile("res://Scenes/MainUI.tscn");
    }
}
