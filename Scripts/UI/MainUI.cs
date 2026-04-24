using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class MainUI : Control
{
	private GameClient _client;
	
	// Status Bars
	private ProgressBar _hpBar;
	private Label _hpLabel;
	private ProgressBar _manaBar;
	private Label _manaLabel;
	private Label _playerNameLabel;
	private Button _sitStandBtn;
	private Button _autoFightBtn;
	private Button _bagsBtn;
	private Button _lookBtn;
	private Button _campBtn;
	private RichTextLabel _statsText;
	private RichTextLabel _skillsText;
	
	// Action & Buff Containers
	private VBoxContainer _actionBar;
	private HBoxContainer _buffBar;
	private RichTextLabel _combatLog;
	private Texture2D _spellGemTexture;
	
	// Inventory & Equipment Window
	private Control _inventoryWindow;
	private VBoxContainer _inventoryList;
	private GridContainer _equipmentGrid;

	// Target Frame
	private Window _targetWindow;
	private Label _targetNameLabel;
	private ProgressBar _targetHpBar;
	private Label _targetHpLabel;
	
	// Extended Target Frame
	private Window _targetListWindow;
	private Button[] _extendedTargetBtns = new Button[10];

	// XP / Level / Zone
	private ProgressBar _xpBar;
	private Label _xpLabel;
	private Label _levelLabel;
	private Label _zoneLabel;
	private HBoxContainer _zoneConnections;
	private string _currentZoneId = "";
	
	// Map
	private MudMap _mudMap;
	private RichTextLabel _locationLabel;
	private Control _mapPanel;

	// Scenes
	private PackedScene _buffIconScene = GD.Load<PackedScene>("res://Scenes/BuffIcon.tscn");
	private PackedScene _inventoryItemScene = GD.Load<PackedScene>("res://Scenes/UI/InventoryItem.tscn");
	private PackedScene _inventoryWindowScene = GD.Load<PackedScene>("res://Scenes/UI/InventoryWindow.tscn");
	private PackedScene _merchantWindowScene = GD.Load<PackedScene>("res://Scenes/UI/MerchantWindow.tscn");

	private Control _merchantWindow;
	private VBoxContainer _merchantItemList;
	private Label _merchantTitle;
	private bool _autoFight = false;
	private bool _isSitting = false;
	private bool _isOutOfRange = false;
	
	private CanvasLayer _loadingLayer;
	private ColorRect _loadingOverlay;
	private Label _loadingLabel;
	private ProgressBar _loadingBar;
	private Label _flavorLabel;
	private bool _isInitialLoadPending = true;
	private float _pendingSpawnX = 0;
	private float _pendingSpawnZ = 0;

	// Spellbook state — tracks what's memorized in each slot
	private struct MemorizedSpell
	{
		public int SpellId;
		public string Name;
		public int ManaCost;
		public float CastTime;
		public float CooldownRemaining;
	}
	private MemorizedSpell[] _spells = new MemorizedSpell[8];
	private double _currentMana = 0;

	// Buff tracking for duration ticking
	private class ActiveBuff
	{
		public string Name;
		public float DurationMax;
		public float DurationRemaining;
		public Panel IconNode;
	}
	private List<ActiveBuff> _activeBuffs = new List<ActiveBuff>();

	private int _copper = 0;

	// Combat log history cap
	private const int MaxLogLines = 200;
	private int _logLineCount = 0;

	private Dictionary<string, double> _localAbilityCooldowns = new Dictionary<string, double>();
	private List<string> _availableAbilities = new List<string>();

	public override void _Ready()
	{
		// Use the global networking singleton
		_client = GameClient.Instance;
		_client.CharacterStatusReceived += OnCharacterStatusReceived;
		_client.SpellbookUpdated += OnSpellbookUpdated;
		_client.CombatLogReceived += OnCombatLogReceived;
		_client.BuffsUpdated += OnBuffsUpdated;
		_client.InventoryUpdated += OnInventoryUpdated;
		_client.ZoneStateReceived += OnZoneStateReceived;
		_client.EntitySneakReceived += OnEntitySneakReceived;
		_client.NpcSayReceived += OnNpcSayReceived;
		_client.MerchantOpened += OnMerchantOpened;
		_client.TrainerOpened += OnTrainerOpened;
		_client.BankOpened += OnBankOpened;

		// Create Windows (hidden by default)
		_inventoryWindow = _inventoryWindowScene.Instantiate<Control>();
		AddChild(_inventoryWindow);
		_inventoryWindow.Hide();
		
		_inventoryList = _inventoryWindow.GetNode<VBoxContainer>("TabContainer/Inventory/VBox");
		_equipmentGrid = _inventoryWindow.GetNode<GridContainer>("TabContainer/Equipment/Grid");

		// Merchant Window
		_merchantWindow = _merchantWindowScene.Instantiate<Control>();
		AddChild(_merchantWindow);
		_merchantWindow.Hide();
		_merchantItemList = _merchantWindow.GetNode<VBoxContainer>("VBox/Scroll/ItemList");
		_merchantTitle = _merchantWindow.GetNode<Label>("VBox/Title");
		_merchantWindow.GetNode<Button>("VBox/CloseBtn").Pressed += () => _merchantWindow.Hide();

		// Link HUD UI nodes
		_hpBar = GetNode<ProgressBar>("%HPBar");
		_hpLabel = GetNode<Label>("%HPLabel");
		
		_manaBar = GetNode<ProgressBar>("%ManaBar");
		_manaLabel = GetNode<Label>("%ManaLabel");
		_playerNameLabel = GetNode<Label>("%PlayerName");
		
		_sitStandBtn = GetNodeOrNull<Button>("%SitStandBtn");
		_autoFightBtn = GetNodeOrNull<Button>("%AutoFightBtn");
		_lookBtn = GetNodeOrNull<Button>("%LookBtn");
		_campBtn = GetNodeOrNull<Button>("%CampBtn");
		_statsText = GetNode<RichTextLabel>("%StatsText");
		_statsText.BbcodeEnabled = true;
		
		// Build HBox layout in StatsWindow: stats left, skills right
		var statsWindow = _statsText.GetParent();
		if (statsWindow != null)
		{
			// Reparent StatsText into an HBox
			var hbox = new HBoxContainer();
			hbox.Name = "StatsHBox";
			hbox.SetAnchorsPreset(LayoutPreset.FullRect);
			hbox.AddThemeConstantOverride("separation", 10);
			
			statsWindow.RemoveChild(_statsText);
			hbox.AddChild(_statsText);
			_statsText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			
			_skillsText = new RichTextLabel();
			_skillsText.Name = "SkillsText";
			_skillsText.BbcodeEnabled = true;
			_skillsText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_skillsText.AddThemeColorOverride("default_color", new Color(0.8f, 0.7f, 0.5f, 1f));
			_skillsText.AddThemeFontSizeOverride("normal_font_size", 12);
			hbox.AddChild(_skillsText);
			
			statsWindow.AddChild(hbox);
		}
		
		if (_campBtn != null) _campBtn.Pressed += OnCampPressed;
		
		_actionBar = GetNodeOrNull<VBoxContainer>("%SpellBar") ?? GetNodeOrNull<VBoxContainer>("SpellBarWindow/SpellBar");
		_buffBar = GetNode<HBoxContainer>("%BuffBar");
		_combatLog = GetNode<RichTextLabel>("%CombatLog");
		_combatLog.BbcodeEnabled = true;
		_combatLog.MetaClicked += OnMetaClicked;

		// Target Frame
		_targetWindow = GetNode<Window>("%TargetWindow");
		_targetNameLabel = GetNode<Label>("%TargetName");
		_targetHpBar = GetNode<ProgressBar>("%TargetHPBar");
		_targetHpLabel = GetNode<Label>("%TargetHPLabel");

		// Extended Target List
		_targetListWindow = GetNodeOrNull<Window>("%TargetListWindow") ?? GetNodeOrNull<Window>("TargetListWindow");
		if (_targetListWindow != null)
		{
			GD.Print("[UI] Found TargetListWindow! Binding 10 slots...");
			for (int i = 0; i < 10; i++)
			{
				_extendedTargetBtns[i] = _targetListWindow.GetNodeOrNull<Button>($"VBoxContainer/Target{i + 1}");
				if (_extendedTargetBtns[i] != null)
				{
					_extendedTargetBtns[i].Hide(); // Hide by default
					
					var btn = _extendedTargetBtns[i];
					btn.Pressed += () => {
						if (btn.HasMeta("targetId"))
						{
							string tId = btn.GetMeta("targetId").AsString();
							// Set backend explicitly
							_client.SendRaw($"{{\"type\": \"SET_TARGET\", \"targetId\": \"{tId}\"}}");
							
							// Auto fight hook logic
							if (_autoFight)
							{
								_client.SendRaw($"{{\"type\": \"ATTACK_TARGET\", \"targetId\": \"{tId}\"}}");
							}
							
							// We can also tell the WorldManager to physically light up the mob natively
							var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
							if (wm != null)
							{
								wm.SetLocalTargetViaId(tId);
							}
						}
					};
				}
			}
		}
		else
		{
			GD.PrintErr("[UI] ERROR: Could not locate TargetListWindow inside _Ready()");
		}

		// XP / Level / Zone
		_xpBar = GetNode<ProgressBar>("%XPBar");
		_xpLabel = GetNode<Label>("%XPLabel");
		_levelLabel = GetNode<Label>("%LevelLabel");
		_zoneLabel = GetNode<Label>("%ZoneLabel");
		_zoneConnections = GetNode<HBoxContainer>("%ZoneConnections");
		
		// Map Setup
		_locationLabel = GetNode<RichTextLabel>("%LocationLabel");
		_locationLabel.BbcodeEnabled = true;
		_mapPanel = GetNode<Control>("ViewPortPanel/SubViewportContainer");
		var mapImage = _mapPanel.GetNodeOrNull<TextureRect>("MapImage");
		if (mapImage != null) mapImage.Hide();
		
		_mudMap = new MudMap();
		_mudMap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_mudMap.SizeFlagsVertical = SizeFlags.ExpandFill;
		_mapPanel.AddChild(_mudMap);
		_mudMap.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_mudMap.Hide(); // Map starts closed by default

		// Optional Navigation container for floating text
		var navContainer = new Control();
		navContainer.Name = "NavContainer";
		navContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		navContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
		_mapPanel.AddChild(navContainer);
		
		_locationLabel.MoveToFront();
		
		// Add Bags Button directly mapping to the user's InventoryBtn
		_bagsBtn = GetNodeOrNull<Button>("MenuWindow/VBox/InventoryBtn");
		
		// Wire Map Toggle Button internally
		var mapBtn = GetNodeOrNull<Button>("MenuWindow/VBox/MapBtn");
		if (mapBtn != null) mapBtn.Pressed += () => _mudMap.Visible = !_mudMap.Visible;

		// Create Programmatic Loading Screen on a Global Canvas Layer
		_loadingLayer = new CanvasLayer();
		_loadingLayer.Name = "LoadingLayer";
		_loadingLayer.Layer = 100; // Over everything
		AddChild(_loadingLayer);

		_loadingOverlay = new ColorRect();
		_loadingOverlay.Name = "LoadingOverlay";
		_loadingOverlay.Color = new Color(0, 0, 0, 1); // Solid black
		_loadingOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
		_loadingLayer.AddChild(_loadingOverlay);
		
		var vbox = new VBoxContainer();
		vbox.Name = "CenteredBox";
		vbox.SetAnchorsPreset(LayoutPreset.Center); // Center centered
		vbox.GrowHorizontal = GrowDirection.Both;
		vbox.GrowVertical = GrowDirection.Both;
		vbox.AddThemeConstantOverride("separation", 20);
		_loadingOverlay.AddChild(vbox);

		_loadingLabel = new Label();
		_loadingLabel.Text = "HYDRATING WORLD...";
		_loadingLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_loadingLabel.AddThemeFontSizeOverride("font_size", 28);
		_loadingLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.7f, 1.0f));
		vbox.AddChild(_loadingLabel);
		
		_loadingBar = new ProgressBar();
		_loadingBar.CustomMinimumSize = new Vector2(400, 30);
		_loadingBar.ShowPercentage = true;
		
		// Add a simple flat style to ensure it's visible
		var sb = new StyleBoxFlat();
		sb.BgColor = new Color(0.2f, 0.2f, 0.2f);
		sb.CornerRadiusTopLeft = 4;
		sb.CornerRadiusTopRight = 4;
		sb.CornerRadiusBottomLeft = 4;
		sb.CornerRadiusBottomRight = 4;
		_loadingBar.AddThemeStyleboxOverride("background", sb);
		
		var sf = new StyleBoxFlat();
		sf.BgColor = new Color(0.2f, 0.6f, 1.0f); // Bright blue
		sf.CornerRadiusTopLeft = 4;
		sf.CornerRadiusTopRight = 4;
		sf.CornerRadiusBottomLeft = 4;
		sf.CornerRadiusBottomRight = 4;
		_loadingBar.AddThemeStyleboxOverride("fill", sf);
		
		vbox.AddChild(_loadingBar);

		_flavorLabel = new Label();
		_flavorLabel.Text = "Waiting for server...";
		_flavorLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_flavorLabel.AddThemeFontSizeOverride("font_size", 16);
		_flavorLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		vbox.AddChild(_flavorLabel);
		
		_loadingLayer.Show();
		
		// Connect progress signal from WorldManager
		var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
		if (wm != null)
		{
			wm.SyncProgress += (cur, tot) => {
				_loadingBar.MaxValue = tot;
				_loadingBar.Value = cur;
			};
		}
		
		// Sense Heading is now handled as a server ability in slots 2-7

		// Load the classic EQ spell gems sprite sheet
		_spellGemTexture = GD.Load<Texture2D>("res://Assets/UI/ClassicUI/gemicons01.tga");

		// Wire spell bar slot buttons to cast
		for (int i = 0; i < 8; i++)
		{
			int slotIndex = i; // capture for closure
			var slotBtn = _actionBar.GetNode<Button>($"Slot{i + 1}");
			slotBtn.Pressed += () => OnSpellSlotPressed(slotIndex);
		}

		// Connect buttons
		if (_sitStandBtn != null) _sitStandBtn.Pressed += OnSitStandPressed;
		if (_autoFightBtn != null) _autoFightBtn.Pressed += OnAutoFightPressed;
		if (_lookBtn != null) _lookBtn.Pressed += OnLookPressed;
		if (_bagsBtn != null) _bagsBtn.Pressed += () => _inventoryWindow.Visible = !_inventoryWindow.Visible;

		var abilitiesGrid = GetNode<GridContainer>("%AbilitiesGrid");
		for (int i = 0; i < 8; i++) {
			int slotIndex = i;
			var btn = abilitiesGrid.GetNodeOrNull<Button>($"BtnAbility{i + 1}");
			if (btn != null) btn.Pressed += () => OnAbilityPressed(slotIndex);
		}
		
		wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
		if (wm != null)
		{
			wm.PlayerMoved += (x, z) => {
				// Send raw payload to server natively mapping Godot Z to Server Y, negating Godot's axes back to EQ's axes
				// Godot (-X = East, -Z = North). EQ (-X = East, +Y = North).
				_client.SendRaw($"{{\"type\": \"UPDATE_POS\", \"x\": {-x}, \"y\": {-z}}}");
			};
			
			wm.SneakToggled += (isSneaking) => {
				_client.SendRaw($"{{\"type\": \"UPDATE_SNEAK\", \"sneaking\": {isSneaking.ToString().ToLower()}}}");
			};
			
			wm.ZoneLineCrossed += (targetZoneId) => {
				_client.SendRaw($"{{\"type\": \"ZONE\", \"zoneId\": \"{targetZoneId}\"}}");
			};
			
			wm.TargetChanged += (name, type) => {
				if (wm.CurrentTargetId != null)
				{
					_client.SendRaw($"{{\"type\": \"SET_TARGET\", \"targetId\": \"{wm.CurrentTargetId}\"}}");
				}
			};
			wm.TargetCleared += () => {
				_client.SendRaw("{\"type\": \"CLEAR_TARGET\"}");
			};
		}
		
		// Unhide windows dynamically at runtime that were hidden in the editor to prevent positioning bugs
		foreach (Node child in GetChildren())
		{
			if (child is Window win && win.Name != "TargetWindow")
			{
				win.Visible = true;
			}
		}

		Log("SYSTEM", "Initialized EQMUD Client...");
		GD.Print("[UI] MainUI Ready with Inventory & Combat Log enabled!");
		
		// Catch-up on missed signals during scene transition
		if (!string.IsNullOrEmpty(_client.LastStatusPayload))
		{
			GD.Print("[UI] Caught up on cached character status.");
			OnCharacterStatusReceived(_client.LastStatusPayload);
		}
		if (!string.IsNullOrEmpty(_client.LastSpellbookPayload))
		{
			GD.Print("[UI] Caught up on cached Spellbook.");
			OnSpellbookUpdated(_client.LastSpellbookPayload);
		}
		if (!string.IsNullOrEmpty(_client.LastInventoryPayload))
		{
			GD.Print("[UI] Caught up on cached Inventory.");
			OnInventoryUpdated(_client.LastInventoryPayload);
		}
		if (!string.IsNullOrEmpty(_client.LastBuffsPayload))
		{
			GD.Print("[UI] Caught up on cached Buffs.");
			OnBuffsUpdated(_client.LastBuffsPayload);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey k && k.Pressed && !k.Echo)
		{
			if (k.Keycode == Key.H)
			{
				// Key H is our Hail key
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null && wm.CurrentTargetId != null)
				{
					_client.SendRaw($"{{\"type\": \"HAIL\", \"targetId\": \"{wm.CurrentTargetId}\"}}");
				}
				else
				{
					_client.SendRaw("{\"type\": \"HAIL\"}");
				}
			}
			else if (k.Keycode == Key.M)
			{
				// Key M toggles the Map overlay
				if (_mudMap != null)
				{
					_mudMap.Visible = !_mudMap.Visible;
					GD.Print($"[UI] Map toggled: {_mudMap.Visible}");
					if (_mudMap.Visible) {
						// Request fresh status to update map position immediately
						_client.SendRaw("{\"type\": \"LOOK\"}");
					}
				}
			}
			else if (k.Keycode == Key.I || k.Keycode == Key.B)
			{
				// Key I or B toggles Inventory
				if (_inventoryWindow != null)
				{
					_inventoryWindow.Visible = !_inventoryWindow.Visible;
					GD.Print($"[UI] Inventory toggled: {_inventoryWindow.Visible}");
				}
			}
		}
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
		{
			GameState.StopServer();
			GetTree().Quit();
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		// Tick spell cooldowns and update button states
		for (int i = 0; i < 8; i++)
		{
			if (_spells[i].CooldownRemaining > 0)
			{
				_spells[i].CooldownRemaining -= dt;
				if (_spells[i].CooldownRemaining < 0) _spells[i].CooldownRemaining = 0;
			}

			var slotBtn = _actionBar.GetNode<Button>($"Slot{i + 1}");
			if (_spells[i].SpellId > 0)
			{
				bool canCast = _currentMana >= _spells[i].ManaCost 
					&& _spells[i].CooldownRemaining <= 0
					&& !_isSitting;
				slotBtn.Disabled = !canCast;

				// Show cooldown remaining via a subtle text overlay or just rely on tooltip
				if (_spells[i].CooldownRemaining > 0)
				{
					slotBtn.TooltipText = $"{_spells[i].Name} ({_spells[i].CooldownRemaining:F1}s)";
				}
				else
				{
					slotBtn.TooltipText = $"{_spells[i].Name} [{_spells[i].ManaCost}m]";
				}
			}
		}

		// Tick buff durations
		for (int i = _activeBuffs.Count - 1; i >= 0; i--)
		{
			var buff = _activeBuffs[i];
			buff.DurationRemaining -= dt;

			if (buff.DurationRemaining <= 0)
			{
				buff.IconNode.QueueFree();
				_activeBuffs.RemoveAt(i);
				continue;
			}

			var durationBar = buff.IconNode.GetNode<ProgressBar>("Duration");
			if (buff.DurationMax > 0)
				durationBar.Value = (buff.DurationRemaining / buff.DurationMax) * 100.0;
		}

		// Tick local abilities
		var abilitiesGrid = GetNodeOrNull<GridContainer>("%AbilitiesGrid");
		if (abilitiesGrid != null)
		{
			var keys = new List<string>(_localAbilityCooldowns.Keys);
			foreach (var k in keys)
			{
				if (_localAbilityCooldowns[k] > 0)
				{
					_localAbilityCooldowns[k] -= dt;
					if (_localAbilityCooldowns[k] < 0) _localAbilityCooldowns[k] = 0;
				}
			}

			for (int j = 0; j < 8; j++)
			{
				var btn = abilitiesGrid.GetNodeOrNull<Button>($"BtnAbility{j + 1}");
				if (btn != null)
				{
					btn.Visible = true; // Always visible
					btn.ClipText = true; // Text shrinks, button stays same size
					btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

					// Slot 1 = Attack (fixed), Slot 8 = Look (fixed)
					if (j == 0)
					{
						btn.Text = _autoFight ? "Stop" : "Attack";
						btn.Disabled = false;
					}
					else if (j == 7)
					{
						btn.Text = "Look";
						btn.Disabled = false;
					}
					else
					{
						// Slots 2-7 (index 1-6) map to _availableAbilities[0-5]
						int abilIndex = j - 1;
						if (abilIndex < _availableAbilities.Count)
						{
							string abil = _availableAbilities[abilIndex];

							double cd = 0;
							_localAbilityCooldowns.TryGetValue(abil.ToLower(), out cd);

							if (cd > 0)
							{
								btn.Disabled = true;
								btn.Text = $"{abil} ({cd:F1}s)";
							}
							else
							{
								btn.Disabled = false;
								btn.Text = abil;
							}
						}
						else
						{
							btn.Text = $"{j + 1}";
							btn.Disabled = true;
						}
					}
				}
			}
		}

		// Sync distance to server
		var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
		if (wm != null && wm.CurrentTargetId != null && _autoFight)
		{
			bool currentlyOutOfRange = !wm.IsTargetInRange(WorldManager.MELEE_RANGE);
			if (currentlyOutOfRange != _isOutOfRange)
			{
				_isOutOfRange = currentlyOutOfRange;
				_client.SendRaw($"{{\"type\":\"UPDATE_RANGE\",\"outOfRange\":{_isOutOfRange.ToString().ToLower()}}}");
			}
		}
		else if (_isOutOfRange) // reset if targeting is lost or combat stopped
		{
			_isOutOfRange = false;
			_client.SendRaw("{\"type\":\"UPDATE_RANGE\",\"outOfRange\":false}");
		}

	}

	// ─── Spell System ───────────────────────────────────────────────
	private void OnAbilityPressed(int index)
	{
		// Slot 0 = Attack toggle
		if (index == 0)
		{
			if (_autoFight)
			{
				_client.SendRaw("{\"type\":\"STOP_COMBAT\"}");
			}
			else
			{
				// Check melee range before engaging
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null && !wm.IsTargetInRange(WorldManager.MELEE_RANGE))
				{
					Log("COMBAT", "Your target is too far away!");
					return;
				}
				if (wm != null && wm.CurrentTargetId != null)
				{
					_client.SendRaw($"{{\"type\":\"ATTACK_TARGET\", \"targetId\": \"{wm.CurrentTargetId}\"}}");
				}
				else
				{
					_client.SendRaw("{\"type\":\"START_COMBAT\"}");
				}
			}
			return;
		}
		// Slot 7 = Look
		if (index == 7)
		{
			OnLookPressed();
			return;
		}
		// Slots 1-6 map to _availableAbilities[0-5]
		int abilIndex = index - 1;
		if (abilIndex < 0 || abilIndex >= _availableAbilities.Count) return;
		string ability = _availableAbilities[abilIndex].ToLower();

		// Melee abilities (kick, bash, taunt, etc.) require melee range
		var wmCheck = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
		if (wmCheck != null && !wmCheck.IsTargetInRange(WorldManager.MELEE_RANGE))
		{
			Log("COMBAT", "Your target is too far away!");
			return;
		}

		_client.SendRaw($"{{\"type\":\"ABILITY\",\"ability\":\"{ability}\"}}");
	}

	private void OnSpellSlotPressed(int slotIndex)
	{
		var spell = _spells[slotIndex];
		if (spell.SpellId <= 0) return;
		if (spell.CooldownRemaining > 0) return;
		if (_currentMana < spell.ManaCost) 
		{
			Log("SYSTEM", "Insufficient mana.");
			return;
		}
		if (_isSitting)
		{
			Log("SYSTEM", "You must stand before casting.");
			return;
		}

		_client.SendRaw($"{{\"type\": \"CAST_SPELL\", \"spellId\": {spell.SpellId}, \"slot\": {slotIndex}}}");
		
		// Start local cooldown (cast time acts as a GCD)
		_spells[slotIndex].CooldownRemaining = spell.CastTime > 0 ? spell.CastTime : 1.5f;
		
		Log("SPELL", $"Casting {spell.Name}...");
	}

	/// <summary>
	/// Expected JSON: { "type": "SPELLBOOK_UPDATE", "spells": [ 
	///   { "slot": 0, "spellId": 1, "name": "Minor Healing", "manaCost": 10, "castTime": 2.0, "icon": "heal" },
	///   ...
	/// ]}
	/// Empty slots are either absent or have spellId = 0.
	/// </summary>
	private void OnSpellbookUpdated(Variant data)
	{
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			if (!root.TryGetProperty("spells", out var spells)) return;

			// Reset all slots
			for (int i = 0; i < 8; i++)
			{
				_spells[i] = new MemorizedSpell();
				var slotBtn = _actionBar.GetNode<Button>($"Slot{i + 1}");
				slotBtn.Text = "";
				slotBtn.TooltipText = $"Empty Action Slot {i + 1}";
				slotBtn.Disabled = true;
				slotBtn.Icon = null;
			}

			// Populate from server data
			foreach (var spell in spells.EnumerateArray())
			{
				int slot = spell.GetProperty("slot").GetInt32();
				if (slot < 0 || slot >= 8) continue;

				int spellId = spell.GetProperty("spellId").GetInt32();
				if (spellId <= 0) continue;

				string name = spell.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : $"Spell #{spellId}";
				int manaCost = spell.TryGetProperty("manaCost", out var manaProp) ? manaProp.GetInt32() : 0;
				float castTime = spell.TryGetProperty("castTime", out var ctProp) ? (float)ctProp.GetDouble() : 1.5f;

				_spells[slot] = new MemorizedSpell
				{
					SpellId = spellId,
					Name = name,
					ManaCost = manaCost,
					CastTime = castTime,
					CooldownRemaining = 0
				};

				var slotBtn = _actionBar.GetNode<Button>($"Slot{slot + 1}");
				slotBtn.Text = "";
				slotBtn.TooltipText = $"{name} [{manaCost}m]";
				slotBtn.Disabled = _currentMana < manaCost;
				
				var atlas = new AtlasTexture();
				atlas.Atlas = _spellGemTexture;
				atlas.Region = GetSpellIconRect(name);
				slotBtn.Icon = atlas;
				slotBtn.ExpandIcon = false;
				slotBtn.IconAlignment = HorizontalAlignment.Center;
			}

			GD.Print("[UI] Spellbook updated.");
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Spellbook Error: {ex.Message}"); }
	}

	private Rect2 GetSpellIconRect(string spellName)
	{
		// Classic EQ sprite sheets are mapped strictly to 24x24 grid blocks
		// We manually specify known classic mappings for the base spells.
		switch (spellName.ToLower())
		{
			case "bind wound": return new Rect2(0, 0, 24, 24);
			case "sense heading": return new Rect2(24, 0, 24, 24);
			case "minor healing": return new Rect2(48, 0, 24, 24);
			case "cure poison": return new Rect2(72, 0, 24, 24);
			case "light healing": return new Rect2(96, 0, 24, 24);
			case "holy armor": return new Rect2(120, 0, 24, 24);
			// row 2
			case "shock of lightning": return new Rect2(0, 24, 24, 24);
			case "frost bolt": return new Rect2(24, 24, 24, 24);
			case "root": return new Rect2(48, 24, 24, 24);
			case "fire bolt": return new Rect2(72, 24, 24, 24);
			// Fallback generic spell icon
			default: return new Rect2(96, 24, 24, 24);
		}
	}

	// ─── 3D World Integration ───────────────────────────────────────
	private async void OnZoneStateReceived(Variant data)
	{
		try
		{
			var dict = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement;
			
			if (dict.TryGetProperty("entities", out var entitiesArray))
			{
				GD.Print($"[UI] ZONE_STATE received with {entitiesArray.GetArrayLength()} entities.");
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null)
				{
					if (_isInitialLoadPending && _flavorLabel != null) 
						_flavorLabel.Text = "Building Zone Geometry...";

					// Load terrain FIRST so entities have ground to stand on
					if (_isInitialLoadPending)
					{
						await ToSignal(GetTree().CreateTimer(0.1f), SceneTreeTimer.SignalName.Timeout);
						wm.LoadZoneMap(_currentZoneId);
						
						_flavorLabel.Text = "Populating World Spawns...";
						await ToSignal(GetTree().CreateTimer(0.3f), SceneTreeTimer.SignalName.Timeout);
					}

					// Now spawn/sync entities ON TOP of the loaded terrain
					wm.SyncLiveMobs(entitiesArray);
					
					if (_isInitialLoadPending)
					{
						_flavorLabel.Text = "Teaching snakes to kick...";
						await ToSignal(GetTree().CreateTimer(0.3f), SceneTreeTimer.SignalName.Timeout);
						
						_flavorLabel.Text = "Finalizing world data...";
						await ToSignal(GetTree().CreateTimer(0.3f), SceneTreeTimer.SignalName.Timeout);

						_flavorLabel.Text = "Placing Character...";
						await ToSignal(GetTree().CreateTimer(0.2f), SceneTreeTimer.SignalName.Timeout);

						GD.Print("[UI] Initial entities hydrated. Spawning player...");
						wm.TeleportPlayer(_pendingSpawnX, _pendingSpawnZ);
						_isInitialLoadPending = false;
						if (_loadingLayer != null) 
						{
							_loadingLayer.Hide();
						}
					}
				}
			}
			else
			{
				GD.Print("[UI] ZONE_STATE received but no 'entities' array found.");
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] OnZoneStateReceived error: {ex.Message}");
		}
	}

	private void OnEntitySneakReceived(Variant data)
	{
		try
		{
			var dict = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement;
			string id = dict.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
			bool sneaking = dict.TryGetProperty("sneaking", out var sProp) && sProp.GetBoolean();
			
			if (id != null)
			{
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null)
				{
					wm.UpdateEntitySneak(id, sneaking);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] OnEntitySneakReceived error: {ex.Message}");
		}
	}

	// ─── Combat Log ─────────────────────────────────────────────────
	/// <summary>
	/// Expected JSON: { "type": "COMBAT_LOG", "events": [
	///   { "event": "MELEE_HIT", "source": "You", "target": "a fire beetle", "damage": 12 },
	///   { "event": "MELEE_MISS", "source": "a fire beetle", "target": "You" },
	///   { "event": "SPELL_DAMAGE", "source": "You", "target": "a fire beetle", "spell": "Shock of Lightning", "damage": 30 },
	///   { "event": "SPELL_HEAL", "source": "You", "target": "You", "spell": "Minor Healing", "amount": 20 },
	///   { "event": "DOT_TICK", "source": "You", "target": "a fire beetle", "spell": "Poison", "damage": 5 },
	///   { "event": "DEATH", "who": "a fire beetle" },
	///   { "event": "XP_GAIN", "amount": 50 },
	///   { "event": "LOOT", "item": "Fire Beetle Eye", "source": "a fire beetle" },
	///   { "event": "LEVEL_UP", "level": 5 },
	///   { "event": "FIZZLE", "spell": "Shock of Lightning" },
	///   { "event": "RESIST", "target": "a fire beetle", "spell": "Root" },
	///   { "event": "MESSAGE", "text": "You feel yourself getting better." }
	/// ]}
	/// </summary>
	private void OnCombatLogReceived(Variant data)
	{
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			if (!root.TryGetProperty("events", out var events)) return;

			foreach (var evt in events.EnumerateArray())
			{
				string eventType = evt.GetProperty("event").GetString();
				FormatAndLogEvent(evt, eventType);
			}
		}
		catch (Exception ex) { GD.PrintErr($"[UI] CombatLog Error: {ex.Message}"); }
	}

	private void FormatAndLogEvent(JsonElement evt, string eventType)
	{
		switch (eventType)
		{
			case "MELEE_HIT":
			{
				string src = evt.GetProperty("source").GetString();
				string tgt = evt.GetProperty("target").GetString();
				int dmg = evt.GetProperty("damage").GetInt32();
				if (src == "You")
					Log("HIT", $"You hit {tgt} for {dmg} points of damage.");
				else
					Log("HIT_TAKEN", $"{src} hits YOU for {dmg} points of damage.");
				break;
			}
			case "MELEE_MISS":
			{
				string src = evt.GetProperty("source").GetString();
				string tgt = evt.GetProperty("target").GetString();
				if (src == "You")
					Log("MISS", $"You try to hit {tgt}, but miss!");
				else
					Log("MISS", $"{src} tries to hit YOU, but misses!");
				break;
			}
			case "SPELL_DAMAGE":
			{
				string src = evt.GetProperty("source").GetString();
				string tgt = evt.GetProperty("target").GetString();
				string spell = evt.GetProperty("spell").GetString();
				int dmg = evt.GetProperty("damage").GetInt32();
				Log("SPELL", $"{src} hit {tgt} for {dmg} points of non-melee damage. ({spell})");
				break;
			}
			case "SPELL_HEAL":
			{
				string src = evt.GetProperty("source").GetString();
				string tgt = evt.GetProperty("target").GetString();
				string spell = evt.GetProperty("spell").GetString();
				int amt = evt.GetProperty("amount").GetInt32();
				Log("HEAL", $"{spell} heals {tgt} for {amt} hit points.");
				break;
			}
			case "DOT_TICK":
			{
				string tgt = evt.GetProperty("target").GetString();
				string spell = evt.GetProperty("spell").GetString();
				int dmg = evt.GetProperty("damage").GetInt32();
				Log("DOT", $"{tgt} has taken {dmg} damage from {spell}.");
				break;
			}
			case "DEATH":
			{
				string who = evt.GetProperty("who").GetString();
				Log("DEATH", $"{who} has been slain!");
				break;
			}
			case "XP_GAIN":
			{
				int amt = evt.GetProperty("amount").GetInt32();
				Log("XP", $"You gained {amt} experience!");
				break;
			}
			case "LOOT":
			{
				string item = evt.GetProperty("item").GetString();
				string from = evt.TryGetProperty("source", out var src) ? src.GetString() : "a corpse";
				Log("LOOT", $"You loot {item} from {from}.");
				break;
			}
			case "LEVEL_UP":
			{
				int lvl = evt.GetProperty("level").GetInt32();
				Log("DING", $"You have reached level {lvl}! Congratulations!");
				break;
			}
			case "FIZZLE":
			{
				string spell = evt.GetProperty("spell").GetString();
				Log("FIZZLE", $"Your {spell} spell fizzles!");
				break;
			}
			case "RESIST":
			{
				string tgt = evt.GetProperty("target").GetString();
				string spell = evt.GetProperty("spell").GetString();
				Log("RESIST", $"{tgt} resisted your {spell}!");
				break;
			}
			case "MESSAGE":
			{
				string text = evt.GetProperty("text").GetString();
				GD.Print($"[CLIENT DEBUG] Received MESSAGE: {text}");
				Log("SYSTEM", text);
				break;
			}
			case "NPC_SAY":
			{
				string npcName = evt.GetProperty("npcName").GetString();
				string text = evt.GetProperty("text").GetString();
				// Meta-clickable keywords might be in the text as [keyword]
				// We format them on the client for the RichTextLabel
				LogNPC(npcName, text);
				break;
			}
			default:
				GD.Print($"[UI] Unhandled combat event: {eventType}");
				break;
		}
	}

	private void OnNpcSayReceived(Variant data)
	{
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			string npcName = root.GetProperty("npcName").GetString();
			string text = root.GetProperty("text").GetString();
			LogNPC(npcName, text);
		}
		catch (Exception ex) { GD.PrintErr($"[UI] NPC_SAY Error: {ex.Message}"); }
	}

	private void OnMetaClicked(Variant meta)
	{
		string keyword = meta.ToString();
		GD.Print($"[UI] Meta clicked: {keyword}");
		_client.SendRaw($"{{\"type\": \"SAY\", \"text\": \"{keyword}\"}}");
	}

	private void OnMerchantOpened(Variant data)
	{
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			string npcName = root.GetProperty("npcName").GetString();
			string npcId = root.GetProperty("npcId").GetString();
			var items = root.GetProperty("items");

			_merchantTitle.Text = $"{npcName} ({_copper} cp)";
			
			// Clear old items
			foreach (Node child in _merchantItemList.GetChildren()) child.QueueFree();

			foreach (var item in items.EnumerateArray())
			{
				string iName = item.GetProperty("name").GetString();
				string iKey = item.GetProperty("itemKey").GetString();
				double price = item.GetProperty("price").GetDouble();

				var row = new HBoxContainer();
				var label = new Label { Text = $"{iName} - {price}cp", SizeFlagsHorizontal = SizeFlags.ExpandFill };
				var buyBtn = new Button { Text = "Buy" };
				
				buyBtn.Pressed += () => {
					_client.SendRaw($"{{\"type\": \"BUY\", \"npcId\": \"{npcId}\", \"itemKey\": \"{iKey}\"}}");
				};

				row.AddChild(label);
				row.AddChild(buyBtn);
				_merchantItemList.AddChild(row);
			}

			_merchantWindow.Show();
			Log("SYSTEM", $"[color=cyan]{npcName} opens their shop...[/color]");
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Merchant Error: {ex.Message}"); }
	}

	private void OnTrainerOpened(Variant data) { Log("SYSTEM", "Trainer window opened (Placeholder)"); }
	private void OnBankOpened(Variant data) { Log("SYSTEM", "Bank window opened (Placeholder)"); }

	// ─── Buff System ────────────────────────────────────────────────
	/// <summary>
	/// Expected JSON: { "type": "BUFFS_UPDATE", "buffs": [
	///   { "name": "Spirit of Wolf", "duration": 36.0, "maxDuration": 60.0, "beneficial": true },
	///   { "name": "Poison", "duration": 12.0, "maxDuration": 18.0, "beneficial": false },
	///   ...
	/// ]}
	/// </summary>
	private void OnBuffsUpdated(Variant data)
	{
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			if (!root.TryGetProperty("buffs", out var buffs)) return;

			// Clear existing buff icons
			foreach (var existing in _activeBuffs)
				existing.IconNode.QueueFree();
			_activeBuffs.Clear();

			foreach (var buff in buffs.EnumerateArray())
			{
				string name = buff.GetProperty("name").GetString();
				float duration = buff.TryGetProperty("duration", out var durProp) ? (float)durProp.GetDouble() : 0f;
				float maxDuration = buff.TryGetProperty("maxDuration", out var maxDurProp) ? (float)maxDurProp.GetDouble() : duration;
				bool beneficial = buff.TryGetProperty("beneficial", out var benProp) ? benProp.GetBoolean() : true;

				// Instantiate icon from scene
				var icon = _buffIconScene.Instantiate<Panel>();
				_buffBar.AddChild(icon);

				// Set label text (abbreviate long names)
				var label = icon.GetNode<Label>("Label");
				label.Text = name.Length > 5 ? name[..5] : name;
				label.TooltipText = name; // Full name on hover

				// Color code: beneficial = blue border, harmful = red border
				if (!beneficial)
				{
					var style = new StyleBoxFlat();
					style.BgColor = new Color(0.15f, 0.08f, 0.08f, 0.9f);
					style.BorderWidthLeft = 1;
					style.BorderWidthTop = 1;
					style.BorderWidthRight = 1;
					style.BorderWidthBottom = 1;
					style.BorderColor = new Color(1f, 0.3f, 0.3f, 1f);
					style.CornerRadiusTopLeft = 2;
					style.CornerRadiusTopRight = 2;
					style.CornerRadiusBottomRight = 2;
					style.CornerRadiusBottomLeft = 2;
					icon.AddThemeStyleboxOverride("panel", style);
					label.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.7f, 1f));
				}

				// Set initial duration bar
				var durationBar = icon.GetNode<ProgressBar>("Duration");
				if (maxDuration > 0)
					durationBar.Value = (duration / maxDuration) * 100.0;
				else
					durationBar.Value = 100.0;

				_activeBuffs.Add(new ActiveBuff
				{
					Name = name,
					DurationMax = maxDuration,
					DurationRemaining = duration,
					IconNode = icon
				});
			}

			GD.Print($"[UI] Buffs updated: {_activeBuffs.Count} active.");
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Buff Error: {ex.Message}"); }
	}

	// ─── Sit / Stand / Combat ───────────────────────────────────────
	private void OnSitStandPressed()
	{
		if (_isSitting)
			_client.SendRaw("{\"type\": \"STAND\"}");
		else
			_client.SendRaw("{\"type\": \"SIT\"}");
	}

	private void OnAutoFightPressed()
	{
		if (_autoFight)
		{
			_client.SendRaw("{\"type\": \"STOP_COMBAT\"}");
		}
		else
		{
			var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
			if (wm != null && wm.CurrentTargetId != null)
			{
				_client.SendRaw($"{{\"type\":\"ATTACK_TARGET\", \"targetId\": \"{wm.CurrentTargetId}\"}}");
			}
			else
			{
				_client.SendRaw("{\"type\": \"START_COMBAT\"}");
			}
		}
	}

	private void OnLookPressed()
	{
		GD.Print("[CLIENT DEBUG] 'Look' Button hit! Transmitting LOOK.");
		_client.SendRaw("{\"type\": \"LOOK\"}");
	}

	private void OnCampPressed()
	{
		_client.SendRaw("{\"type\": \"CAMP\"}");
	}

	// ─── Status Handling ────────────────────────────────────────────
	private void OnCharacterStatusReceived(Variant data)
	{
		try 
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			var character = root.GetProperty("character");

			UpdateBars(character);
			UpdateStatsUI(character);
			
			// Sit/Stand state
			if (character.TryGetProperty("state", out var stateProp))
			{
				string state = stateProp.GetString();
				_isSitting = (state == "medding"); 
				if (_sitStandBtn != null) _sitStandBtn.Text = _isSitting ? "Stand" : "Sit";
			}

			// Auto-fight state (use autoFight flag, not inCombat)
			if (character.TryGetProperty("autoFight", out var autoFightProp))
			{
				_autoFight = autoFightProp.GetBoolean();
				if (_autoFightBtn != null) _autoFightBtn.Text = _autoFight ? "Stop Combat" : "Start Combat";
			}

			// Target frame
			if (character.TryGetProperty("target", out var targetProp) && targetProp.ValueKind != JsonValueKind.Null)
			{
				_targetWindow.Visible = true;
				string targetName = targetProp.GetProperty("name").GetString();
				double targetHp = targetProp.GetProperty("hp").GetDouble();
				double targetMaxHp = targetProp.GetProperty("maxHp").GetDouble();
				int targetLevel = targetProp.TryGetProperty("level", out var lvlProp) ? lvlProp.GetInt32() : 0;
				string targetId = targetProp.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;

				_targetNameLabel.Text = targetLevel > 0 ? $"{targetName} (Lv {targetLevel})" : targetName;
				_targetHpBar.MaxValue = targetMaxHp;
				_targetHpBar.Value = Math.Max(0, targetHp);
				double pct = targetMaxHp > 0 ? (targetHp / targetMaxHp * 100) : 0;
				_targetHpLabel.Text = $"{pct:F0}%";

				// Give the target a ChaseAI instruction in the 3D world ONLY if we are in combat
				bool inCombat = character.TryGetProperty("inCombat", out var icProp) && icProp.GetBoolean();
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null)
				{
					if (targetId != null && inCombat)
						wm.SetCombatTarget(targetId);
					else
						wm.SetCombatTarget(null);
				}
			}
			else
			{
				_targetWindow.Visible = false;
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null) wm.SetCombatTarget(null);
			}
			// Extended Targets
			if (character.TryGetProperty("extendedTargets", out var extArr) && extArr.ValueKind == JsonValueKind.Array)
			{
				int i = 0;
				foreach (var extMob in extArr.EnumerateArray())
				{
					if (i >= 10) break; // Hardcap at 10 buttons
					
					string mId = extMob.GetProperty("id").GetString();
					string mName = extMob.GetProperty("name").GetString();
					double mHp = extMob.GetProperty("hp").GetDouble();
					double mMax = extMob.GetProperty("maxHp").GetDouble();
					
					double pct = mMax > 0 ? (mHp / mMax * 100) : 0;
					
					if (_extendedTargetBtns[i] != null)
					{
						// GD.Print($"[EXT] Assigning slot {i} to {mName}");
						_extendedTargetBtns[i].Text = $"{mName} [{pct:F0}%]";
						_extendedTargetBtns[i].Show();
						
						// Safety cleanup old signals by re-assigning (this avoids memory leak accumulation)
						var btn = _extendedTargetBtns[i];
						btn.SetMeta("targetId", mId);
					}
					else
					{
						GD.PrintErr($"[EXT ERROR] Slot {i} button was strictly null!");
					}
					i++;
				}
				
				// Hide remaining unused buttons
				for (int j = i; j < 10; j++)
				{
					if (_extendedTargetBtns[j] != null)
					{
						_extendedTargetBtns[j].Hide();
					}
				}
			}
			else
			{
				for (int j = 0; j < 10; j++)
				{
					if (_extendedTargetBtns[j] != null) _extendedTargetBtns[j].Hide();
				}
			}

			// Abilities
			if (character.TryGetProperty("availableAbilities", out var availArr) && availArr.ValueKind == JsonValueKind.Array)
			{
				_availableAbilities.Clear();
				foreach (var abil in availArr.EnumerateArray())
				{
					string aName = abil.GetString();
					// Format to title case
					if (!string.IsNullOrEmpty(aName))
					{
						aName = char.ToUpper(aName[0]) + aName.Substring(1);
						_availableAbilities.Add(aName);
					}
				}
			}

			if (character.TryGetProperty("abilityCooldowns", out var cdsVar) && cdsVar.ValueKind == JsonValueKind.Object)
			{
				foreach (var prop in cdsVar.EnumerateObject())
				{
					_localAbilityCooldowns[prop.Name] = prop.Value.GetDouble();
				}
			}

			// XP / Level / Zone
			if (character.TryGetProperty("level", out var levelProp))
			{
				int level = levelProp.GetInt32();
				_levelLabel.Text = $"Level {level}";
			}

			if (character.TryGetProperty("experience", out var xpProp) &&
				character.TryGetProperty("nextLevelXp", out var nextXpProp))
			{
				double xp = xpProp.GetDouble();
				double nextXp = nextXpProp.GetDouble();
				if (nextXp > 0)
				{
					_xpBar.MaxValue = nextXp;
					_xpBar.Value = xp;
					double xpPct = xp / nextXp * 100;
					_xpLabel.Text = $"EXP: {xpPct:F1}%";
				}
			}

			if (character.TryGetProperty("zone", out var zoneProp))
			{
				_zoneLabel.Text = zoneProp.GetString();
				string locText = $"[center][b]{zoneProp.GetString()}[/b][/center]";
				if (character.TryGetProperty("roomName", out var roomProp) && roomProp.ValueKind != JsonValueKind.Null) {
					locText = $"[center][b]{zoneProp.GetString()}[/b]\n[color=white]{roomProp.GetString()}[/color][/center]";
				}
				_locationLabel.Text = locText;
			}

			// Topographical data for Map Radar
			float px = character.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : 0f;
			float py = character.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : 0f;
			Vector2 playerPos = new Vector2(px, py);
			
			Vector2 mSize = new Vector2(400, 400);
			if (character.TryGetProperty("mapSize", out var ms) && ms.ValueKind == JsonValueKind.Object)
			{
				mSize.X = ms.TryGetProperty("width", out var mw) ? mw.GetSingle() : 400f;
				mSize.Y = ms.TryGetProperty("length", out var ml) ? ml.GetSingle() : 400f;
			}
			
			Vector2 cOff = Vector2.Zero;
			if (character.TryGetProperty("centerOffset", out var co) && co.ValueKind == JsonValueKind.Object)
			{
				cOff.X = co.TryGetProperty("x", out var ox) ? ox.GetSingle() : 0f;
				cOff.Y = co.TryGetProperty("y", out var oy) ? oy.GetSingle() : 0f;
			}
			
			JsonElement zoneLines = character.TryGetProperty("zoneLines", out var zl) ? zl : default;

			if (_mudMap != null && _mapPanel.Visible) {
				// We no longer need mapData or roomId, replacing them with a null literal and "" string
				_mudMap.UpdateMap(default, "", playerPos, mSize, cOff, zoneLines);
			}


			// Zone connections — rebuild buttons only when zone changes
			if (character.TryGetProperty("zoneId", out var zoneIdProp))
			{
				string zoneId = zoneIdProp.GetString();
				if (zoneId != _currentZoneId)
				{
					_currentZoneId = zoneId;
					RebuildZoneData(character);
				}
			}

			// Topographical Zone Entry Spawning
			if (character.TryGetProperty("spawnPos", out var spawnProp) && spawnProp.ValueKind == JsonValueKind.Object)
			{
				_pendingSpawnX = spawnProp.TryGetProperty("x", out var xp) ? xp.GetSingle() : 0f;
				_pendingSpawnZ = spawnProp.TryGetProperty("y", out var yp) ? yp.GetSingle() : 0f;
				_isInitialLoadPending = true;
				
				if (_loadingLayer != null) 
				{
					_loadingLayer.Show();
					_flavorLabel.Text = "Constructing Terrain...";
					_loadingBar.Value = 10;
				}
				
				GD.Print($"[UI] Server requested spawn at {_pendingSpawnX}, {_pendingSpawnZ}. Delaying until entities ready...");
			}
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Status Error: {ex.Message}"); }
	}

	private void RebuildZoneData(JsonElement character)
	{
		// Clear old buttons
		foreach (Node child in _zoneConnections.GetChildren()) child.QueueFree();

		if (!character.TryGetProperty("connections", out var connections)) return;

		foreach (var conn in connections.EnumerateArray())
		{
			string targetZoneId = conn.GetString();
			// Format zone ID into display name: "west_karana" → "West Karana"
			string displayName = FormatZoneName(targetZoneId);

			var btn = new Button
			{
				Text = $"→ {displayName}",
				CustomMinimumSize = new Vector2(0, 28),
			};
			btn.AddThemeFontSizeOverride("font_size", 11);
			btn.Pressed += () =>
			{
				_client.SendRaw($"{{\"type\": \"ZONE\", \"zoneId\": \"{targetZoneId}\"}}");
			};
			_zoneConnections.AddChild(btn);
		}
		
		var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
		if (wm != null)
		{
			wm.ClearWorld(); // This purges all old enemies
			
			JsonElement mapSize = default;
			JsonElement zoneLines = default;
			JsonElement centerOffset = default;
			
			if (character.TryGetProperty("mapSize", out var ms)) mapSize = ms;
			if (character.TryGetProperty("zoneLines", out var zl)) zoneLines = zl;
			if (character.TryGetProperty("centerOffset", out var co)) centerOffset = co;
			
			wm.RebuildZoneBoundaries(mapSize, zoneLines, centerOffset);
		}
	}

	private string FormatZoneName(string zoneId)
	{
		var parts = zoneId.Split('_');
		for (int i = 0; i < parts.Length; i++)
		{
			if (parts[i].Length > 0)
				parts[i] = char.ToUpper(parts[i][0]) + parts[i][1..];
		}
		return string.Join(" ", parts);
	}

	// ─── Inventory ──────────────────────────────────────────────────
	private void OnInventoryUpdated(Variant data)
	{
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			
			if (!root.TryGetProperty("inventory", out var inventory)) return;

			// Update Log if message provided
			if (root.TryGetProperty("message", out var msg)) Log("SYSTEM", msg.GetString());

			// Clear Inventory List
			foreach (Node child in _inventoryList.GetChildren()) child.QueueFree();
			
			// Clear Equipment Button Texts and disconnect old handlers
			foreach (var slot in new[] { "Head", "Chest", "Arms", "Hands", "Primary", "Secondary" }) {
				var slotBtn = _equipmentGrid.GetNode<Button>($"{slot}/Item");
				slotBtn.Text = "Empty";
				foreach (var conn in slotBtn.GetSignalConnectionList("pressed"))
					slotBtn.Disconnect("pressed", (Callable)conn["callable"]);
			}

			foreach (var item in inventory.EnumerateArray())
			{
				int item_id = item.GetProperty("item_id").GetInt32();
				string name = item.GetProperty("itemName").GetString();
				int equipped = item.GetProperty("equipped").GetInt32();
				int slot = item.GetProperty("slot").GetInt32();

				// Build stat summary for the item
				string statText = BuildItemStatText(item);

				if (equipped == 1) {
					string slotName = MapSlotToName(slot);
					if (slotName != null)
					{
						var slotBtn = _equipmentGrid.GetNode<Button>($"{slotName}/Item");
						slotBtn.Text = name;
						// Wire unequip on click
						int capturedId = item_id;
						// Disconnect any previous handler by replacing the button's connections
						foreach (var conn in slotBtn.GetSignalConnectionList("pressed"))
							slotBtn.Disconnect("pressed", (Callable)conn["callable"]);
						slotBtn.Pressed += () => _client.SendRaw($"{{\"type\": \"UNEQUIP_ITEM\", \"itemId\": {capturedId}}}");
					}
				} else {
					var itemUI = _inventoryItemScene.Instantiate<Panel>();
					_inventoryList.AddChild(itemUI);
					itemUI.GetNode<Label>("HBox/Info/ItemName").Text = name;
					itemUI.GetNode<Label>("HBox/Info/Stats").Text = statText;
					
					var equipBtn = itemUI.GetNode<Button>("HBox/EquipBtn");
					int targetSlot = item.TryGetProperty("slot", out var slotProp) ? slotProp.GetInt32() : 13;
					equipBtn.Pressed += () => _client.SendRaw($"{{\"type\": \"EQUIP_ITEM\", \"itemId\": {item_id}, \"slot\": {targetSlot}}}");
				}
			}
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Inv Error: {ex.Message}"); }
	}

	private string BuildItemStatText(JsonElement item)
	{
		var parts = new List<string>();
		if (item.TryGetProperty("damage", out var dmg) && dmg.GetInt32() > 0) parts.Add($"Dmg: {dmg.GetInt32()}");
		if (item.TryGetProperty("delay", out var delay) && delay.GetInt32() > 0) parts.Add($"Delay: {delay.GetInt32()}");
		if (item.TryGetProperty("ac", out var ac) && ac.GetInt32() > 0) parts.Add($"AC: {ac.GetInt32()}");
		if (item.TryGetProperty("hp", out var hp) && hp.GetInt32() > 0) parts.Add($"HP: +{hp.GetInt32()}");
		if (item.TryGetProperty("mana", out var mana) && mana.GetInt32() > 0) parts.Add($"Mana: +{mana.GetInt32()}");
		if (item.TryGetProperty("str", out var str) && str.GetInt32() != 0) parts.Add($"STR: +{str.GetInt32()}");
		if (item.TryGetProperty("sta", out var sta) && sta.GetInt32() != 0) parts.Add($"STA: +{sta.GetInt32()}");
		if (item.TryGetProperty("agi", out var agi) && agi.GetInt32() != 0) parts.Add($"AGI: +{agi.GetInt32()}");
		if (item.TryGetProperty("dex", out var dex) && dex.GetInt32() != 0) parts.Add($"DEX: +{dex.GetInt32()}");
		if (item.TryGetProperty("wis", out var wis) && wis.GetInt32() != 0) parts.Add($"WIS: +{wis.GetInt32()}");
		if (item.TryGetProperty("int", out var intel) && intel.GetInt32() != 0) parts.Add($"INT: +{intel.GetInt32()}");
		if (item.TryGetProperty("cha", out var cha) && cha.GetInt32() != 0) parts.Add($"CHA: +{cha.GetInt32()}");
		return parts.Count > 0 ? string.Join(" | ", parts) : "";
	}

	private string MapSlotToName(int slot) {
		if (slot == 2) return "Head";
		if (slot == 7) return "Arms";
		if (slot == 12) return "Hands";
		if (slot == 13) return "Primary";
		if (slot == 14) return "Secondary";
		if (slot == 17) return "Chest";
		return null;
	}

	// ─── Logging ────────────────────────────────────────────────────
	private void Log(string type, string message)
	{
		// Trim old lines if we're getting too long
		if (_logLineCount > MaxLogLines)
		{
			_combatLog.Clear();
			_logLineCount = 0;
			_combatLog.AppendText("[color=gray]--- log trimmed ---[/color]\n");
		}

		string color = type switch
		{
			"MISS"      => "#888888",   // Gray
			"HIT"       => "#55cc55",   // Green — your hits
			"HIT_TAKEN" => "#cc5555",   // Red — damage taken
			"SPELL"     => "#55cccc",   // Cyan
			"HEAL"      => "#88ee88",   // Light green
			"DOT"       => "#cccc55",   // Yellow
			"DEATH"     => "#ff4444",   // Bright red
			"XP"        => "#ddaa44",   // Gold
			"LOOT"      => "#ddaa44",   // Gold
			"DING"      => "#ffdd00",   // Bright gold
			"FIZZLE"    => "#aa55aa",   // Purple
			"RESIST"    => "#aa55aa",   // Purple
			"SYSTEM"    => "#dd8833",   // Orange
			_           => "#cccccc",   // Default light gray
		};

		_combatLog.AppendText($"[color={color}]{message}[/color]\n");
		_logLineCount++;
	}

	private void LogNPC(string npcName, string text)
	{
		if (_logLineCount > MaxLogLines)
		{
			_combatLog.Clear();
			_logLineCount = 0;
			_combatLog.AppendText("[color=gray]--- log trimmed ---[/color]\n");
		}

		// Replace [keyword] with [url=keyword][color=blue][b]keyword[/b][/color][/url]
		string formattedText = text;
		var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\[([^\]]+)\]");
		foreach (System.Text.RegularExpressions.Match match in matches)
		{
			string keyword = match.Groups[1].Value;
			string replacement = $"[url={keyword}][color=blue][b]{keyword}[/b][/color][/url]";
			formattedText = formattedText.Replace(match.Value, replacement);
		}

		_combatLog.AppendText($"[color=lightblue]{npcName} says, '{formattedText}'[/color]\n");
		_logLineCount++;
	}

	// ─── Status Bars ────────────────────────────────────────────────
	private void UpdateBars(JsonElement source)
	{
		double hp = 0, maxHp = 0, mana = 0, maxMana = 0;
		bool hasHp = false, hasMana = false;

		if (source.TryGetProperty("playerHp", out var php)) { hp = php.GetDouble(); hasHp = true; }
		else if (source.TryGetProperty("hp", out var h2)) { hp = h2.GetDouble(); hasHp = true; }

		if (source.TryGetProperty("playerMaxHp", out var pmhp)) { maxHp = pmhp.GetDouble(); hasHp = true; }
		else if (source.TryGetProperty("maxHp", out var mh2)) { maxHp = mh2.GetDouble(); hasHp = true; }

		if (source.TryGetProperty("playerMana", out var pman)) { mana = pman.GetDouble(); hasMana = true; }
		else if (source.TryGetProperty("mana", out var m2)) { mana = m2.GetDouble(); hasMana = true; }

		if (source.TryGetProperty("playerMaxMana", out var pmman)) { maxMana = pmman.GetDouble(); hasMana = true; }
		else if (source.TryGetProperty("maxMana", out var mm2)) { maxMana = mm2.GetDouble(); hasMana = true; }

		if (source.TryGetProperty("copper", out var cpProp)) { _copper = cpProp.GetInt32(); }

		if (hasHp && maxHp > 0)
		{
			_hpBar.MaxValue = maxHp;
			_hpBar.Value = hp;
			_hpLabel.Text = $"HP: {hp}/{maxHp}";
		}
		if (hasMana && maxMana > 0)
		{
			_manaBar.Visible = true;
			_manaBar.MaxValue = maxMana;
			_manaBar.Value = mana;
			_manaLabel.Text = $"MANA: {mana}/{maxMana}";
			_currentMana = mana;
		}
		else if (hasMana && maxMana <= 0)
		{
			_manaBar.Visible = false;
			_currentMana = 0;
		}
	}

	private void UpdateStatsUI(JsonElement source)
	{
		if (_statsText == null) return;
		
		if (source.TryGetProperty("name", out var nProp)) _playerNameLabel.Text = nProp.GetString();
		
		int lvl = source.TryGetProperty("level", out var lProp) ? lProp.GetInt32() : 1;
		int str = source.TryGetProperty("str", out var strProp) ? strProp.GetInt32() : 0;
		int sta = source.TryGetProperty("sta", out var staProp) ? staProp.GetInt32() : 0;
		int agi = source.TryGetProperty("agi", out var agiProp) ? agiProp.GetInt32() : 0;
		int dex = source.TryGetProperty("dex", out var dexProp) ? dexProp.GetInt32() : 0;
		int wis = source.TryGetProperty("wis", out var wisProp) ? wisProp.GetInt32() : 0;
		int intel = source.TryGetProperty("intel", out var intProp) ? intProp.GetInt32() : 0;
		int cha = source.TryGetProperty("cha", out var chaProp) ? chaProp.GetInt32() : 0;
		int ac = source.TryGetProperty("ac", out var acProp) ? acProp.GetInt32() : 0;

		_statsText.Text = $"[center]Stats[/center]\n" +
						  $"Str : [{str}]\n" +
						  $"Sta : [{sta}]\n" +
						  $"Agi : [{agi}]\n" +
						  $"Dex : [{dex}]\n" +
						  $"Wis : [{wis}]\n" +
						  $"Int : [{intel}]\n" +
						  $"Cha : [{cha}]\n" +
						  $"AC  : [{ac}]\n" +
						  $"\n" +
						  $"Money: [{_copper} cp]\n" +
						  $"Lvl : [{lvl}]";
		
		// Update Skills panel
		if (_skillsText != null && source.TryGetProperty("skills", out var skillsProp) && skillsProp.ValueKind == JsonValueKind.Object)
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine("[center]Skills[/center]");
			foreach (var skill in skillsProp.EnumerateObject())
			{
				string skillName = FormatSkillName(skill.Name);
				int val = skill.Value.GetInt32();
				sb.AppendLine($"{skillName}: {val}");
			}
			_skillsText.Text = sb.ToString();
		}
	}
	
	private string FormatSkillName(string key)
	{
		// "1h_slashing" → "1H Slash", "defense" → "Defense"
		var parts = key.Split('_');
		for (int i = 0; i < parts.Length; i++)
		{
			if (parts[i].Length > 0)
				parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
		}
		return string.Join(" ", parts);
	}
}
