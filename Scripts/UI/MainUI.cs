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
	private Button _campBtn;
	private RichTextLabel _invSkillsText;    // skills tab in inventory
	private bool _draggingInventory = false;
	private Vector2 _dragOffset;
	
	// Action & Buff Containers
	private VBoxContainer _actionBar;
	private HBoxContainer _buffBar;
	private RichTextLabel _combatLog;
	private Texture2D _spellGemTexture;
	private Button[] _spellSlotButtons = new Button[8];
	private Label[] _spellSlotLabels = new Label[8]; // Spell name overlay
	
	// Hotbar System
	private HotbarManager _hotbarManager;
	
	// Inventory & Equipment Window
	private Control _inventoryWindow;
	private Button[] _invSlots = new Button[10]; // 10 general inventory slots
	private GridContainer _slotsGrid;        // container for inventory slot buttons
	private VBoxContainer _equipGrid;        // equipment slot container
	private RichTextLabel _invStatsText;     // stats panel in inventory
	private readonly Dictionary<string, Button> _equipSlots = new();

	// Item Interaction System
	private JsonElement? _heldItem = null;
	private int _heldFromSlotId = -1;
	private Label _cursorLabel;
	private Panel _itemDetailPopup;
	private Button _autoEquipBtn;
	private double _rightClickTimer = -1;
	private Button _rightClickTarget = null;
	private JsonElement? _rightClickItemData = null;
	private readonly Dictionary<Button, JsonElement> _slotItemData = new(); // map button â†’ item JSON

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
	private string _activeMerchantId = null; // Track open merchant for sell transactions

	// Merchant sort/filter state
	private struct MerchantItem
	{
		public string Name;
		public string ItemKey;
		public int Price;
		public string PriceText;
		public string StatsStr;
		public int ScrollLevel;
		public int ItemType;
		public int Classes;
		public int RecLevel;
		public string NpcId;
	}
	private List<MerchantItem> _merchantItems = new List<MerchantItem>();
	private int _merchantPlayerClassBitmask = 65535;
	private int _merchantPlayerLevel = 60;
	private string _merchantSortMode = "name";
	private bool _merchantShowUsable = false;
	private HBoxContainer _merchantSortBar = null;

	// Chat input
	private LineEdit _chatInput;
	private string _lastWhisperSender = "";
	private bool _chatInputFocused = false;
	private bool _autoFight = false;
	private bool _isSitting = false;
	private bool _isSelfTargeted = false;
	private Spellbook _spellbookUI;
	private string _pendingMemorizeSpellKey = null; // Set when player clicks a spell in the book
	private string _pendingMemorizeSpellName = null;
	private bool _isOutOfRange = false;

	// Action tab state (upgraded ActionBarWindow)
	private Button[] _actionTabButtons;
	private GridContainer _actionGrid;
	private int _actionCurrentTab = 1; // 0=Socials, 1=Abilities, 2=Skills
	private Window _actionBarWindow;
	private int _socialPage = 0;
	private HBoxContainer _socialNavRow;
	private Label _socialPageLabel;
	
	private CanvasLayer _loadingLayer;
	private ColorRect _loadingOverlay;
	private Label _loadingLabel;
	private ProgressBar _loadingBar;
	private Label _flavorLabel;
	private bool _isInitialLoadPending = true;
	private float _pendingSpawnX = 0;
	private float _pendingSpawnY = 0;
	private float _pendingSpawnZ = 0;

	// Spellbook state â€” tracks what's memorized in each slot
	internal string _visionModeName = "Normal Vision";
	private struct MemorizedSpell
	{
		public int SpellId;
		public string Name;
		public int ManaCost;
		public float CastTime;
		public float CooldownRemaining;
		public string Description;
	}
	private MemorizedSpell[] _spells = new MemorizedSpell[8];
	private double _currentMana = 0;
	private double _currentHp = 0;
	private double _maxHp = 0;
	private string _charName;
	private int _charLevel = 1;

	// All known (scribed) spells — for the right-click memorize picker
	private struct KnownSpell
	{
		public int SpellId;
		public string SpellKey;
		public string Name;
		public int ManaCost;
		public float CastTime;
		public string Effect; // heal, dd, dot, buff, debuff, root, snare, cure, info
		public int Level;
		public string Description;
	}
	private List<KnownSpell> _knownSpells = new List<KnownSpell>();

	// Cast bar state
	private bool _isCasting = false;
	private float _castTimeTotal = 0;
	private float _castTimeElapsed = 0;
	private string _castingSpellName = "";
	private ProgressBar _castBar;
	private Label _castBarLabel;
	private Panel _castBarPanel;

	// Options state
	private bool _showPlayerName = false;
	private bool _dynamicShadows = true;
	private Panel _optionsPanel;

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
	private List<string> _availableSkills = new List<string>();

	// ── Companion Window (Pet / future Mercenary) ──
	private Window _companionWindow;
	private Label _companionNameLabel;
	private Label _companionStateLabel;
	private ProgressBar _companionHpBar;
	private Label _companionHpLabel;
	private bool _hasPet = false;

	public override void _Ready()
	{
		// Use the global networking singleton
		_client = GameClient.Instance;
		_client.CharacterStatusReceived += OnCharacterStatusReceived;
		_client.SpellbookUpdated += OnSpellbookUpdated;
		_client.SpellbookFullReceived += OnSpellbookFullReceived;
		_client.CombatLogReceived += OnCombatLogReceived;
		_client.BuffsUpdated += OnBuffsUpdated;
		_client.InventoryUpdated += OnInventoryUpdated;
		_client.ZoneStateReceived += OnZoneStateReceived;
		_client.EnvironmentUpdated += OnEnvironmentUpdated;
		_client.EntitySneakReceived += OnEntitySneakReceived;
		_client.EntityHideReceived += OnEntityHideReceived;
		_client.SneakResultReceived += OnSneakResultReceived;
		_client.HideResultReceived += OnHideResultReceived;
		_client.SneakBrokenReceived += OnSneakBrokenReceived;
		_client.HideBrokenReceived += OnHideBrokenReceived;
		_client.NpcSayReceived += OnNpcSayReceived;
		_client.MerchantOpened += OnMerchantOpened;
		_client.TrainerOpened += OnTrainerOpened;
		_client.BankOpened += OnBankOpened;
		_client.ChatReceived += OnChatReceived;
		_client.CampComplete += OnCampComplete;
		_client.MessageReceived += OnGenericMessage;

		// Wire up chat input
		_chatInput = GetNode<LineEdit>("%ChatInput");
		if (_chatInput != null)
		{
			_chatInput.TextSubmitted += OnChatSubmitted;
			_chatInput.FocusEntered += () => _chatInputFocused = true;
			_chatInput.FocusExited += () => _chatInputFocused = false;
		}

		// Create Windows (hidden by default)
		_inventoryWindow = _inventoryWindowScene.Instantiate<Control>();
		AddChild(_inventoryWindow);
		_inventoryWindow.Hide();
		
		_slotsGrid = _inventoryWindow.GetNode<GridContainer>("MainVBox/SlotsSection/SlotsGrid");
		_equipGrid = _inventoryWindow.GetNode<VBoxContainer>("MainVBox/ContentHBox/EquipPanel/EquipScroll/EquipGrid");
		_invStatsText = _inventoryWindow.GetNode<RichTextLabel>("MainVBox/ContentHBox/StatsPanel/StatsScroll/StatsText");
		BuildInventorySlots();
		
		// Close / Done buttons
		_inventoryWindow.GetNode<Button>("MainVBox/TitleBar/HBox/CloseBtn").Pressed += () => { _inventoryWindow.Hide(); _activeMerchantId = null; };
		_inventoryWindow.GetNode<Button>("MainVBox/ButtonBar/DoneBtn").Pressed += () => { _inventoryWindow.Hide(); _activeMerchantId = null; };
		_inventoryWindow.GetNode<Button>("MainVBox/ButtonBar/DestroyBtn").Pressed += () => {
			Log("SYSTEM", "[color=yellow]Click the X on an item to destroy it.[/color]");
		};
		
		// Build equipment paperdoll slots
		BuildEquipmentGrid();
		BuildAutoEquipSlot();
		BuildSkillsTab();
		SetupInventoryDrag();
		BuildCursorLabel();
		BuildItemDetailPopup();

		// Merchant Window
		_merchantWindow = _merchantWindowScene.Instantiate<Control>();
		AddChild(_merchantWindow);
		_merchantWindow.Hide();
		_merchantItemList = _merchantWindow.GetNode<VBoxContainer>("VBox/Scroll/ItemList");
		_merchantTitle = _merchantWindow.GetNode<Label>("VBox/Title");
		_merchantWindow.GetNode<Button>("VBox/CloseBtn").Pressed += () => {
			_merchantWindow.Hide();
			_activeMerchantId = null;
			// Refresh inventory to hide sell buttons
			if (!string.IsNullOrEmpty(_client.LastInventoryPayload))
				OnInventoryUpdated(_client.LastInventoryPayload);
		};

		// Link HUD UI nodes
		_hpBar = GetNode<ProgressBar>("%HPBar");
		_hpLabel = GetNode<Label>("%HPLabel");
		
		_manaBar = GetNode<ProgressBar>("%ManaBar");
		_manaLabel = GetNode<Label>("%ManaLabel");
		_playerNameLabel = GetNode<Label>("%PlayerName");
		
		_sitStandBtn = GetNodeOrNull<Button>("%SitStandBtn");
		_autoFightBtn = GetNodeOrNull<Button>("%AutoFightBtn");
		_campBtn = GetNodeOrNull<Button>("%CampBtn");
		// Stats panel removed â€” now embedded in inventory window
		
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
							// Note: auto-fight does NOT auto-send ATTACK_TARGET here.
							// Aggro only happens via explicit attack or when a swing lands in range.
							
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
		_loadingLabel.Text = "Loading...";
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

		// Wire spell bar slot buttons — rebuild as styled slots matching hotbar aesthetic
		// Remove original scene buttons and create new styled ones
		var existingSlots = new List<Node>();
		foreach (var child in _actionBar.GetChildren())
		{
			if (child is Button) existingSlots.Add(child);
		}
		foreach (var old in existingSlots) old.QueueFree();

		// Rebuild 8 spell gem slots with hotbar-style panels
		for (int i = 0; i < 8; i++)
		{
			int slotIndex = i; // capture for closure

			// Slot container panel (matches hotbar styling)
			var slotPanel = new Panel();
			slotPanel.Name = $"Slot{i + 1}";
			slotPanel.CustomMinimumSize = new Vector2(0, 40);
			slotPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

			var panelStyle = new StyleBoxFlat();
			panelStyle.BgColor = new Color(0.1f, 0.1f, 0.12f, 0.85f);
			panelStyle.BorderWidthLeft = 1; panelStyle.BorderWidthTop = 1;
			panelStyle.BorderWidthRight = 1; panelStyle.BorderWidthBottom = 1;
			panelStyle.BorderColor = new Color(0.4f, 0.35f, 0.2f, 0.7f);
			panelStyle.CornerRadiusTopLeft = 3; panelStyle.CornerRadiusTopRight = 3;
			panelStyle.CornerRadiusBottomLeft = 3; panelStyle.CornerRadiusBottomRight = 3;
			slotPanel.AddThemeStyleboxOverride("panel", panelStyle);

			// Invisible button overlay for click handling
			var slotBtn = new Button();
			slotBtn.Flat = true;
			slotBtn.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			slotBtn.ClipText = true;
			slotBtn.AddThemeFontSizeOverride("font_size", 1); // Tiny — we use a label for text
			slotBtn.AddThemeColorOverride("font_color", new Color(0, 0, 0, 0)); // Invisible text

			// Hover style — gold highlight
			var hoverStyle = new StyleBoxFlat();
			hoverStyle.BgColor = new Color(0.2f, 0.2f, 0.25f, 0.5f);
			hoverStyle.BorderWidthLeft = 1; hoverStyle.BorderWidthTop = 1;
			hoverStyle.BorderWidthRight = 1; hoverStyle.BorderWidthBottom = 1;
			hoverStyle.BorderColor = new Color(0.8f, 0.7f, 0.3f, 0.9f);
			hoverStyle.CornerRadiusTopLeft = 3; hoverStyle.CornerRadiusTopRight = 3;
			hoverStyle.CornerRadiusBottomLeft = 3; hoverStyle.CornerRadiusBottomRight = 3;
			slotBtn.AddThemeStyleboxOverride("hover", hoverStyle);

			// Normal + pressed: transparent
			var normalStyle = new StyleBoxFlat();
			normalStyle.BgColor = new Color(0, 0, 0, 0);
			slotBtn.AddThemeStyleboxOverride("normal", normalStyle);
			slotBtn.AddThemeStyleboxOverride("pressed", normalStyle);

			// Disabled style — dimmed
			var disabledStyle = new StyleBoxFlat();
			disabledStyle.BgColor = new Color(0.05f, 0.05f, 0.07f, 0.6f);
			slotBtn.AddThemeStyleboxOverride("disabled", disabledStyle);

			slotBtn.GuiInput += (ev) => {
				if (ev is not InputEventMouseButton mb || !mb.Pressed) return;
				if (mb.ButtonIndex == MouseButton.Left)
				{
					if (_pendingMemorizeSpellKey != null)
					{
						if (!_isSitting)
						{
							Log("SYSTEM", "You must be sitting to memorize spells.");
							_pendingMemorizeSpellKey = null;
							_pendingMemorizeSpellName = null;
							return;
						}
						_client.SendRaw($"{{\"type\":\"MEMORIZE_SPELL\",\"spellKey\":\"{_pendingMemorizeSpellKey}\",\"slot\":{slotIndex}}}");
						Log("SYSTEM", $"[color=cyan]Memorizing {_pendingMemorizeSpellName} in gem {slotIndex + 1}...[/color]");
						_pendingMemorizeSpellKey = null;
						_pendingMemorizeSpellName = null;
						return;
					}
					OnSpellSlotPressed(slotIndex);
				}
				else if (mb.ButtonIndex == MouseButton.Middle)
				{
					if (_spells[slotIndex].SpellId > 0 && _hotbarManager != null)
						_hotbarManager.StartSpellDrag(slotIndex, _spells[slotIndex].Name);
				}
				else if (mb.ButtonIndex == MouseButton.Right)
				{
					if (_spells[slotIndex].SpellId <= 0)
						ShowSpellMemorizePicker(slotIndex, slotBtn);
					else
						ShowSpellSlotContextMenu(slotIndex, slotBtn);
				}
			};

			slotPanel.AddChild(slotBtn);

			// Spell name label — centered, wraps
			var nameLabel = new Label();
			nameLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			nameLabel.OffsetLeft = 4; nameLabel.OffsetRight = -18;
			nameLabel.OffsetTop = 2; nameLabel.OffsetBottom = -2;
			nameLabel.AddThemeFontSizeOverride("font_size", 11);
			nameLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 1.0f));
			nameLabel.HorizontalAlignment = HorizontalAlignment.Left;
			nameLabel.VerticalAlignment = VerticalAlignment.Center;
			nameLabel.ClipText = true;
			nameLabel.Text = $"Gem {i + 1}";
			nameLabel.MouseFilter = MouseFilterEnum.Ignore;
			slotPanel.AddChild(nameLabel);

			// Slot number label — centered over the icon area (right side)
			var numLabel = new Label();
			numLabel.Text = (i + 1).ToString();
			numLabel.AnchorLeft = 1; numLabel.AnchorTop = 0;
			numLabel.AnchorRight = 1; numLabel.AnchorBottom = 1;
			numLabel.OffsetLeft = -28; numLabel.OffsetRight = 0;
			numLabel.AddThemeFontSizeOverride("font_size", 9);
			numLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.95f, 0.7f, 0.85f));
			numLabel.HorizontalAlignment = HorizontalAlignment.Center;
			numLabel.VerticalAlignment = VerticalAlignment.Center;
			numLabel.MouseFilter = MouseFilterEnum.Ignore;
			slotPanel.AddChild(numLabel);

			_actionBar.AddChild(slotPanel);
			_spellSlotButtons[i] = slotBtn;
			_spellSlotLabels[i] = nameLabel;
		}

		// ── Create Cast Bar (hidden by default) ──
		_castBarPanel = new Panel();
		_castBarPanel.CustomMinimumSize = new Vector2(250, 28);
		_castBarPanel.AnchorLeft = 0.5f;
		_castBarPanel.AnchorRight = 0.5f;
		_castBarPanel.AnchorTop = 0.55f;
		_castBarPanel.AnchorBottom = 0.55f;
		_castBarPanel.OffsetLeft = -125;
		_castBarPanel.OffsetRight = 125;
		_castBarPanel.OffsetTop = -14;
		_castBarPanel.OffsetBottom = 14;
		_castBarPanel.MouseFilter = Control.MouseFilterEnum.Stop; // Capture clicks for dragging

		// Make cast bar draggable
		bool castBarDragging = false;
		Vector2 castBarDragOffset = Vector2.Zero;
		_castBarPanel.GuiInput += (ev) =>
		{
			if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
			{
				if (mb.Pressed) { castBarDragging = true; castBarDragOffset = mb.GlobalPosition - _castBarPanel.GlobalPosition; }
				else castBarDragging = false;
			}
			else if (ev is InputEventMouseMotion mm && castBarDragging)
			{
				_castBarPanel.AnchorLeft = 0; _castBarPanel.AnchorRight = 0;
				_castBarPanel.AnchorTop = 0; _castBarPanel.AnchorBottom = 0;
				_castBarPanel.GlobalPosition = mm.GlobalPosition - castBarDragOffset;
				_castBarPanel.Size = new Vector2(250, 28);
			}
		};
		var castPanelStyle = new StyleBoxFlat();
		castPanelStyle.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
		castPanelStyle.BorderWidthBottom = 1;
		castPanelStyle.BorderWidthTop = 1;
		castPanelStyle.BorderWidthLeft = 1;
		castPanelStyle.BorderWidthRight = 1;
		castPanelStyle.BorderColor = new Color(0.6f, 0.5f, 0.2f, 0.9f);
		castPanelStyle.CornerRadiusBottomLeft = 3;
		castPanelStyle.CornerRadiusBottomRight = 3;
		castPanelStyle.CornerRadiusTopLeft = 3;
		castPanelStyle.CornerRadiusTopRight = 3;
		_castBarPanel.AddThemeStyleboxOverride("panel", castPanelStyle);

		_castBar = new ProgressBar();
		_castBar.AnchorRight = 1;
		_castBar.AnchorBottom = 1;
		_castBar.OffsetLeft = 4;
		_castBar.OffsetRight = -4;
		_castBar.OffsetTop = 4;
		_castBar.OffsetBottom = -4;
		_castBar.MinValue = 0;
		_castBar.MaxValue = 100;
		_castBar.Value = 0;
		_castBar.ShowPercentage = false;
		_castBar.MouseFilter = Control.MouseFilterEnum.Pass; // Let clicks pass through to panel for dragging
		var castFillStyle = new StyleBoxFlat();
		castFillStyle.BgColor = new Color(0.85f, 0.65f, 0.15f, 0.9f);
		castFillStyle.CornerRadiusBottomLeft = 2;
		castFillStyle.CornerRadiusBottomRight = 2;
		castFillStyle.CornerRadiusTopLeft = 2;
		castFillStyle.CornerRadiusTopRight = 2;
		_castBar.AddThemeStyleboxOverride("fill", castFillStyle);
		var castBgStyle = new StyleBoxFlat();
		castBgStyle.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.8f);
		_castBar.AddThemeStyleboxOverride("background", castBgStyle);
		_castBarPanel.AddChild(_castBar);

		_castBarLabel = new Label();
		_castBarLabel.AnchorRight = 1;
		_castBarLabel.AnchorBottom = 1;
		_castBarLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_castBarLabel.VerticalAlignment = VerticalAlignment.Center;
		_castBarLabel.AddThemeFontSizeOverride("font_size", 12);
		_castBarLabel.AddThemeColorOverride("font_color", Colors.White);
		_castBarLabel.MouseFilter = Control.MouseFilterEnum.Pass; // Let clicks pass through to panel for dragging
		_castBarPanel.AddChild(_castBarLabel);

		AddChild(_castBarPanel);
		_castBarPanel.Hide();

		// Connect buttons
		if (_sitStandBtn != null) _sitStandBtn.Pressed += OnSitStandPressed;
		if (_autoFightBtn != null) _autoFightBtn.Pressed += OnAutoFightPressed;
		if (_bagsBtn != null) _bagsBtn.Pressed += () => _inventoryWindow.Visible = !_inventoryWindow.Visible;

		// Wire ABILITIES button on Simple Panel to toggle ActionBarWindow
		var abilitiesBtn = GetNodeOrNull<Button>("MenuWindow/VBox/AbilitiesBtn");
		if (abilitiesBtn != null) abilitiesBtn.Pressed += () => ToggleActionBarWindow();

		// Wire SPELLS button on Simple Panel to toggle Spellbook
		var spellsBtn = GetNodeOrNull<Button>("MenuWindow/VBox/SpellsBtn");
		if (spellsBtn != null) spellsBtn.Pressed += () => ToggleSpellbook();

		// Wire OPTIONS button to toggle options panel
		var optionsBtn = GetNodeOrNull<Button>("MenuWindow/VBox/OptionsBtn");
		if (optionsBtn != null) optionsBtn.Pressed += () => ToggleOptionsPanel();

		// Upgrade existing ActionBarWindow with title + 3 tabs
		_actionBarWindow = GetNodeOrNull<Window>("ActionBarWindow");
		if (_actionBarWindow != null)
		{
			// Make it taller to fit header + tabs above the grid
			_actionBarWindow.Size = new Vector2I(200, 260);
			_actionBarWindow.Title = "";
			
			var grid = _actionBarWindow.GetNodeOrNull<GridContainer>("AbilitiesGrid");
			if (grid != null)
			{
				// Shift grid down to make room for header + tabs + social nav
				grid.OffsetTop = 60;
			}
			
			// Add "Actions" header label (centered)
			var titleLabel = new Label();
			titleLabel.Text = "Actions";
			titleLabel.Position = new Vector2(10, 6);
			titleLabel.Size = new Vector2(180, 20);
			titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			titleLabel.AddThemeFontSizeOverride("font_size", 13);
			titleLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.4f));
			_actionBarWindow.AddChild(titleLabel);
			
			// Add 3 tab buttons
			var tabRow = new HBoxContainer();
			tabRow.Position = new Vector2(10, 28);
			tabRow.Size = new Vector2(180, 26);
			tabRow.AddThemeConstantOverride("separation", 2);
			_actionBarWindow.AddChild(tabRow);
			
			string[] tabNames = { "Socials", "Abilities", "Skills" };
			Button[] tabBtns = new Button[3];
			for (int t = 0; t < 3; t++)
			{
				int tabIdx = t;
				var tabBtn = new Button();
				tabBtn.Text = tabNames[t];
				tabBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				tabBtn.CustomMinimumSize = new Vector2(55, 24);
				tabBtn.AddThemeFontSizeOverride("font_size", 11);
				tabBtn.Pressed += () => SwitchActionTab(tabIdx, tabBtns, grid);
				tabRow.AddChild(tabBtn);
				tabBtns[t] = tabBtn;
			}
			
			// Social page navigation (< Page# >) — only visible on Socials tab
			_socialNavRow = new HBoxContainer();
			_socialNavRow.Position = new Vector2(10, 54);
			_socialNavRow.Size = new Vector2(180, 22);
			_socialNavRow.AddThemeConstantOverride("separation", 4);
			_socialNavRow.Alignment = BoxContainer.AlignmentMode.Center;
			_socialNavRow.Visible = false; // hidden until Socials tab
			_actionBarWindow.AddChild(_socialNavRow);
			
			var prevSocialBtn = new Button();
			prevSocialBtn.Text = "<";
			prevSocialBtn.CustomMinimumSize = new Vector2(24, 20);
			prevSocialBtn.AddThemeFontSizeOverride("font_size", 11);
			prevSocialBtn.Pressed += () => { ChangeSocialPage(-1); SwitchActionTab(0, _actionTabButtons, _actionGrid); };
			_socialNavRow.AddChild(prevSocialBtn);
			
			_socialPageLabel = new Label();
			_socialPageLabel.Text = "1";
			_socialPageLabel.CustomMinimumSize = new Vector2(20, 0);
			_socialPageLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_socialPageLabel.AddThemeFontSizeOverride("font_size", 12);
			_socialPageLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
			_socialNavRow.AddChild(_socialPageLabel);
			
			var nextSocialBtn = new Button();
			nextSocialBtn.Text = ">";
			nextSocialBtn.CustomMinimumSize = new Vector2(24, 20);
			nextSocialBtn.AddThemeFontSizeOverride("font_size", 11);
			nextSocialBtn.Pressed += () => { ChangeSocialPage(1); SwitchActionTab(0, _actionTabButtons, _actionGrid); };
			_socialNavRow.AddChild(nextSocialBtn);
			
			// Wire Pressed handlers on grid buttons
			for (int bi = 0; bi < 8; bi++)
			{
				int btnIdx = bi; // capture for closure
				var btn = grid.GetChildOrNull<Button>(bi);
				if (btn != null)
				{
					btn.GuiInput += (ev) => OnActionGridSlotInput(ev, btnIdx, btn);
				}
			}

			// Start on Abilities tab
			_actionTabButtons = tabBtns;
			_actionGrid = grid;
			_actionCurrentTab = 1;
			StyleActionTabs(tabBtns, 1);
			SwitchActionTab(1, tabBtns, grid);
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

			wm.HideToggled += (isHiding) => {
				_client.SendRaw($"{{\"type\": \"USE_HIDE\", \"hiding\": {isHiding.ToString().ToLower()}}}");
			};
			
			wm.ZoneLineCrossed += (targetZoneId) => {
				_client.SendRaw($"{{\"type\": \"ZONE\", \"zoneId\": \"{targetZoneId}\"}}");
			};
			
			wm.TargetChanged += (name, type) => {
				if (wm.CurrentTargetId != null)
				{
					// Self-target: show player's own info in target window
				if (wm.CurrentTargetId == "Player")
					{
						_isSelfTargeted = true;
						_targetWindow.Visible = true;
						string charName = _charName ?? "You";
						_targetNameLabel.Text = $"{charName} (Lv {_charLevel})";
						_targetHpBar.MaxValue = _maxHp;
						_targetHpBar.Value = Math.Max(0, _currentHp);
						double pct = _maxHp > 0 ? (_currentHp / (double)_maxHp * 100) : 100;
						_targetHpLabel.Text = $"{pct:F0}%";
					}
					else
					{
						_isSelfTargeted = false;
						_client.SendRaw($"{{\"type\": \"SET_TARGET\", \"targetId\": \"{wm.CurrentTargetId}\"}}");
					}
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

		// ── Hotbar System ──
		_hotbarManager = new HotbarManager();
		_hotbarManager.Name = "HotbarManager";
		AddChild(_hotbarManager);

		// Wire spell data callbacks so hotbar buttons can fire spells
		_hotbarManager.GetSpellIdForSlot = (slot) => (slot >= 0 && slot < 8) ? _spells[slot].SpellId : -1;
		_hotbarManager.GetSpellNameForSlot = (slot) => (slot >= 0 && slot < 8) ? _spells[slot].Name : "";
		_hotbarManager.GetTargetName = () => _targetNameLabel?.Text ?? "";
		_hotbarManager.EquipItemById = (itemId) => {
			_client.SendRaw($"{{\"type\": \"AUTO_EQUIP\", \"itemId\": {itemId}}}");
		};

		// Wire ActionPanel ability/skill activation signals
		// ActionPanel is now a persistent node handled by ActionPanel.cs

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

		// Create Spellbook UI (hidden by default) — must be created BEFORE catch-up
		_spellbookUI = new Spellbook();
		_spellbookUI.Name = "SpellbookUI";
		_spellbookUI.Visible = false;
		_spellbookUI.ZIndex = 55;
		AddChild(_spellbookUI);
		_spellbookUI.SpellSelectedForMemorize += OnSpellSelectedForMemorize;
		_spellbookUI.SpellbookClosed += () => { _pendingMemorizeSpellKey = null; _pendingMemorizeSpellName = null; };

		// NOW catch up on the full spellbook (after UI exists)
		if (!string.IsNullOrEmpty(_client.LastSpellbookFullPayload))
		{
			GD.Print("[UI] Caught up on cached Spellbook Full.");
			OnSpellbookFullReceived(_client.LastSpellbookFullPayload);
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
			// Enter key: toggle chat input focus
			if (k.Keycode == Key.Enter || k.Keycode == Key.KpEnter)
			{
				if (_chatInput != null)
				{
					if (!_chatInputFocused)
					{
						_chatInput.GrabFocus();
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (string.IsNullOrEmpty(_chatInput.Text))
					{
						// Empty enter = unfocus
						_chatInput.ReleaseFocus();
						GetViewport().SetInputAsHandled();
						return;
					}
					// If there's text, TextSubmitted will handle it
				}
			}

			// Escape key: unfocus chat
			if (k.Keycode == Key.Escape && _chatInputFocused)
			{
				_chatInput.ReleaseFocus();
				GetViewport().SetInputAsHandled();
				return;
			}

			// Suppress all game hotkeys while chat is focused
			if (_chatInputFocused) return;
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
			else if (k.Keycode == Key.Escape)
			{
				// ESC drops the current target
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null)
				{
					wm.ClearTarget();
				}
				_client.SendRaw("{\"type\": \"CLEAR_TARGET\"}");
				_isSelfTargeted = false;
				if (_targetWindow != null) _targetWindow.Visible = false;
			}
			// Auto-stand when pressing movement or jump keys while sitting
			else if (_isSitting && (k.Keycode == Key.W || k.Keycode == Key.S || k.Keycode == Key.Space))
			{
				_client.SendRaw("{\"type\": \"STAND\"}");
				_isSitting = false;
				if (_sitStandBtn != null) _sitStandBtn.Text = "Sit";
			}
		}
	}

	public override void _ExitTree()
	{
		// Disconnect all GameClient signal handlers to prevent
		// "Cannot access a disposed object" errors when switching characters
		if (_client != null)
		{
			_client.CharacterStatusReceived -= OnCharacterStatusReceived;
			_client.SpellbookUpdated -= OnSpellbookUpdated;
			_client.SpellbookFullReceived -= OnSpellbookFullReceived;
			_client.CombatLogReceived -= OnCombatLogReceived;
			_client.BuffsUpdated -= OnBuffsUpdated;
			_client.InventoryUpdated -= OnInventoryUpdated;
			_client.ZoneStateReceived -= OnZoneStateReceived;
			_client.EnvironmentUpdated -= OnEnvironmentUpdated;
			_client.EntitySneakReceived -= OnEntitySneakReceived;
			_client.EntityHideReceived -= OnEntityHideReceived;
			_client.SneakResultReceived -= OnSneakResultReceived;
			_client.HideResultReceived -= OnHideResultReceived;
			_client.SneakBrokenReceived -= OnSneakBrokenReceived;
			_client.HideBrokenReceived -= OnHideBrokenReceived;
			_client.NpcSayReceived -= OnNpcSayReceived;
			_client.MerchantOpened -= OnMerchantOpened;
			_client.TrainerOpened -= OnTrainerOpened;
			_client.BankOpened -= OnBankOpened;
			_client.ChatReceived -= OnChatReceived;
			_client.CampComplete -= OnCampComplete;
			_client.MessageReceived -= OnGenericMessage;
		}
		base._ExitTree();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
		{
			// Clear session-scoped EQ asset cache
			EQAssetCache.Instance.ClearCache();
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

			var slotBtn = _spellSlotButtons[i];
			if (_spells[i].SpellId > 0)
			{
				bool canCast = _currentMana >= _spells[i].ManaCost 
					&& _spells[i].CooldownRemaining <= 0
					&& !_isSitting
					&& !_isCasting;
				slotBtn.Disabled = !canCast;

				// Show cooldown remaining via a subtle text overlay or just rely on tooltip
				string descTip = !string.IsNullOrEmpty(_spells[i].Description) ? $"\n{_spells[i].Description}" : "";
				if (_spells[i].CooldownRemaining > 0)
				{
					slotBtn.TooltipText = $"{_spells[i].Name} (Cooldown: {_spells[i].CooldownRemaining:F1}s)\n[{_spells[i].ManaCost}m] Cast: {_spells[i].CastTime:F1}s{descTip}";
				}
				else
				{
					slotBtn.TooltipText = $"{_spells[i].Name} [{_spells[i].ManaCost}m] Cast: {_spells[i].CastTime:F1}s{descTip}";
				}
			}
		}

		// Tick cast bar
		if (_isCasting)
		{
			_castTimeElapsed += dt;
			if (_castTimeTotal > 0)
				_castBar.Value = (_castTimeElapsed / _castTimeTotal) * 100.0;
			_castBarLabel.Text = $"{_castingSpellName} ({(_castTimeTotal - _castTimeElapsed):F1}s)";
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

		// Tick local ability cooldowns
		var keys = new List<string>(_localAbilityCooldowns.Keys);
		foreach (var k in keys)
		{
			if (_localAbilityCooldowns[k] > 0)
			{
				_localAbilityCooldowns[k] -= dt;
				if (_localAbilityCooldowns[k] < 0) _localAbilityCooldowns[k] = 0;
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

		// ── Item Interaction: cursor label follows mouse ──
		if (_cursorLabel != null && _cursorLabel.Visible) {
			_cursorLabel.GlobalPosition = GetGlobalMousePosition() + new Vector2(12, 12);
		}

		// ── Right-click hold timer for item detail popup ──
		if (_rightClickTimer >= 0) {
			_rightClickTimer += delta;
			if (_rightClickTimer >= 1.0 && _rightClickItemData.HasValue) {
				ShowItemDetail(_rightClickItemData.Value, GetGlobalMousePosition());
				_rightClickTimer = -1;
				_rightClickTarget = null;
				_rightClickItemData = null;
			}
		}

		// ── Escape to cancel held item ──
		if (Input.IsActionJustPressed("ui_cancel") && _heldItem.HasValue) {
			CancelHeldItem();
		}

	}

	// â”€â”€â”€ Spell System â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

	/// <summary>Handles ability activation from the new ActionPanel (by name instead of slot index).</summary>

	/// <summary>Handles skill activation from the new ActionPanel (utility skills like Hide, Sneak, etc.).</summary>

	// ── Action Tab Switching (for the upgraded ActionBarWindow) ──────

	// Non-slotable skills that shouldn't appear in the Skills tab

	/// <summary>Shorten skill/ability names for the grid buttons.</summary>








	// ── Spell Bar Right-Click Menus ─────────────────────────────────

	/// <summary>Show a categorized spell picker for memorizing into an empty gem slot.</summary>

	/// <summary>Show context menu when right-clicking a filled spell gem.</summary>



	/// <summary>
	/// Handle cast events (CAST_START, CAST_COMPLETE, CAST_INTERRUPTED)
	/// </summary>
	private void OnGenericMessage(string type, string json)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			switch (type)
			{
				case "CAST_START":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					_castingSpellName = root.TryGetProperty("spellName", out var n) ? n.GetString() : "Casting...";
					_castTimeTotal = root.TryGetProperty("castTime", out var ct) ? (float)ct.GetDouble() : 1.5f;
					_castTimeElapsed = 0;
					_isCasting = true;
					_castBar.Value = 0;
					_castBarLabel.Text = $"{_castingSpellName} ({_castTimeTotal:F1}s)";
					_castBarPanel.Show();

					// Trigger casting animation on 3D model
					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null) 
					{
						wm.SetPlayerCasting(true);
						wm.TriggerEntityAction("You", "cast");
					}
					break;
				}
				case "CAST_COMPLETE":
				{
					_isCasting = false;
					_castBar.Value = 100;
					_castBarPanel.Hide();

					// Stop casting animation
					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null) wm.SetPlayerCasting(false);
					break;
				}
				case "CAST_INTERRUPTED":
				{
					_isCasting = false;
					_castBarLabel.Text = "Interrupted!";
					// Flash red briefly then hide
					var fillStyle = _castBar.GetThemeStylebox("fill") as StyleBoxFlat;
					if (fillStyle != null) fillStyle.BgColor = new Color(0.9f, 0.2f, 0.2f, 0.9f);
					// Hide after a short delay
					GetTree().CreateTimer(0.8).Timeout += () => {
						_castBarPanel.Hide();
						// Restore fill color
						if (fillStyle != null) fillStyle.BgColor = new Color(0.85f, 0.65f, 0.15f, 0.9f);
					};

					// Stop casting animation
					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null) wm.SetPlayerCasting(false);
					break;
				}
				case "EMOTE":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					string charName = root.TryGetProperty("charName", out var cn) ? cn.GetString() : "Someone";
					string emote = root.TryGetProperty("emote", out var em) ? em.GetString() : "";
					string anim = root.TryGetProperty("anim", out var an) && an.ValueKind == JsonValueKind.String ? an.GetString() : null;

					// Display emote text
					Log("EMOTE", $"{charName} {emote}s.");

					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null)
					{
						var entity = wm.GetEntityByName(charName);
						if (entity != null)
						{
							entity.PlayEmote(emote);
						}
						// Fallback to player if it was a player emote
						else if (!string.IsNullOrEmpty(anim) && (charName == _charName || string.IsNullOrEmpty(_charName)))
						{
							wm.PlayPlayerAnimation(anim);
						}
					}
					break;
				}
				case "NODE_DESTROYED":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					string nodeId = root.TryGetProperty("nodeId", out var nid) ? nid.GetString() : null;
					if (!string.IsNullOrEmpty(nodeId))
					{
						var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
						if (wm != null) wm.RemoveEntity(nodeId);
					}
					break;
				}
				case "TARGET_UPDATE":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					if (root.TryGetProperty("target", out var tgt))
					{
						string tName = tgt.TryGetProperty("name", out var tn) ? tn.GetString() : "Unknown";
						int tHp = tgt.TryGetProperty("hp", out var th) ? th.GetInt32() : 0;
						int tMaxHp = tgt.TryGetProperty("maxHp", out var tmh) ? tmh.GetInt32() : 1;
						int tLevel = tgt.TryGetProperty("level", out var tl) ? tl.GetInt32() : 0;
						string tType = tgt.TryGetProperty("type", out var tt) ? tt.GetString() : "";

						_targetWindow.Visible = true;
						_targetNameLabel.Text = tType == "mining_node" ? $"{tName} (T{tLevel})" : $"{tName} (Lv {tLevel})";
						_targetHpBar.MaxValue = tMaxHp;
						_targetHpBar.Value = Math.Max(0, tHp);
						double pct = tMaxHp > 0 ? ((double)tHp / tMaxHp * 100) : 100;
						_targetHpLabel.Text = $"{pct:F0}%";
					}
					break;
				}
			}
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Cast event error: {ex.Message}"); }
	}

	/// <summary>
	/// Expected JSON: { "type": "SPELLBOOK_UPDATE", "spells": [ 
	///   { "slot": 0, "spellId": 1, "name": "Minor Healing", "manaCost": 10, "castTime": 2.0, "icon": "heal" },
	///   ...
	/// ]}
	/// Empty slots are either absent or have spellId = 0.
	/// </summary>

	/// <summary>Handle the full spellbook payload (all scribed spells with book positions).</summary>

	/// <summary>Called when the player left-clicks a spell in the spellbook UI.</summary>

	/// <summary>Toggle the spellbook UI. Must be sitting to open.</summary>

	// ── Options Panel ───────────────────────────────────────────────

	private void ToggleOptionsPanel()
	{
		if (_optionsPanel == null)
			BuildOptionsPanel();

		_optionsPanel.Visible = !_optionsPanel.Visible;
	}

	private void BuildOptionsPanel()
	{
		_optionsPanel = new Panel();
		_optionsPanel.CustomMinimumSize = new Vector2(240, 160);
		_optionsPanel.Size = new Vector2(240, 160);

		// Center on screen
		var screenSize = GetViewport().GetVisibleRect().Size;
		_optionsPanel.GlobalPosition = (screenSize - _optionsPanel.Size) / 2;

		// EQ-style dark panel
		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.95f);
		panelStyle.BorderWidthLeft = 2; panelStyle.BorderWidthTop = 2;
		panelStyle.BorderWidthRight = 2; panelStyle.BorderWidthBottom = 2;
		panelStyle.BorderColor = new Color(0.6f, 0.5f, 0.2f, 1.0f);
		panelStyle.CornerRadiusTopLeft = 3; panelStyle.CornerRadiusTopRight = 3;
		panelStyle.CornerRadiusBottomLeft = 3; panelStyle.CornerRadiusBottomRight = 3;
		panelStyle.ContentMarginLeft = 8; panelStyle.ContentMarginRight = 8;
		panelStyle.ContentMarginTop = 6; panelStyle.ContentMarginBottom = 6;
		_optionsPanel.AddThemeStyleboxOverride("panel", panelStyle);
		_optionsPanel.MouseFilter = Control.MouseFilterEnum.Stop;

		var vbox = new VBoxContainer();
		vbox.SetAnchorsPreset(LayoutPreset.FullRect);
		vbox.OffsetLeft = 8; vbox.OffsetRight = -8;
		vbox.OffsetTop = 6; vbox.OffsetBottom = -6;
		vbox.AddThemeConstantOverride("separation", 6);
		_optionsPanel.AddChild(vbox);

		// Title row with close button
		var titleRow = new HBoxContainer();
		var titleLabel = new Label();
		titleLabel.Text = "Options";
		titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleLabel.AddThemeFontSizeOverride("font_size", 14);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.4f));
		titleRow.AddChild(titleLabel);

		var closeBtn = new Button();
		closeBtn.Text = "✕";
		closeBtn.CustomMinimumSize = new Vector2(22, 22);
		closeBtn.AddThemeFontSizeOverride("font_size", 11);
		closeBtn.Pressed += () => _optionsPanel.Visible = false;
		titleRow.AddChild(closeBtn);
		vbox.AddChild(titleRow);

		// Separator
		vbox.AddChild(new HSeparator());

		// Show Player Name checkbox
		var nameCheck = new CheckBox();
		nameCheck.Text = "Show Player Name";
		nameCheck.ButtonPressed = _showPlayerName;
		nameCheck.AddThemeFontSizeOverride("font_size", 12);
		nameCheck.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		nameCheck.Toggled += (toggled) =>
		{
			_showPlayerName = toggled;
			var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
			if (wm != null) wm.SetPlayerNameVisible(_showPlayerName);
		};
		vbox.AddChild(nameCheck);

		// Dynamic Shadows checkbox
		var shadowCheck = new CheckBox();
		shadowCheck.Text = "Dynamic Shadows";
		shadowCheck.ButtonPressed = _dynamicShadows;
		shadowCheck.AddThemeFontSizeOverride("font_size", 12);
		shadowCheck.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		shadowCheck.Toggled += (toggled) =>
		{
			_dynamicShadows = toggled;
			var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
			if (wm != null) wm.SetDynamicShadows(_dynamicShadows);
		};
		vbox.AddChild(shadowCheck);

		// Make the panel draggable
		bool optsDragging = false;
		Vector2 optsDragOffset = Vector2.Zero;
		_optionsPanel.GuiInput += (ev) =>
		{
			if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
			{
				if (mb.Pressed) { optsDragging = true; optsDragOffset = mb.GlobalPosition - _optionsPanel.GlobalPosition; }
				else optsDragging = false;
			}
			else if (ev is InputEventMouseMotion mm && optsDragging)
			{
				_optionsPanel.GlobalPosition = mm.GlobalPosition - optsDragOffset;
			}
		};

		AddChild(_optionsPanel);
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

	// â”€â”€â”€ 3D World Integration â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
	private async void OnZoneStateReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			var dict = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement;
			
			if (dict.TryGetProperty("entities", out var entitiesArray))
			{
				// ZONE_STATE entities received — sync silently
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null)
				{
					if (_isInitialLoadPending && _flavorLabel != null) 
						_flavorLabel.Text = "Building Zone Geometry...";

					// Load terrain FIRST so entities have ground to stand on
					if (_isInitialLoadPending)
					{
						// On-demand EQ asset extraction if configured
						if (EQAssetConfig.Instance.IsConfigured && !EQAssetCache.Instance.HasZone(_currentZoneId))
						{
							_flavorLabel.Text = "Extracting Zone Assets...";
							await ToSignal(GetTree().CreateTimer(0.1f), SceneTreeTimer.SignalName.Timeout);
							
							var extractor = LanternExtractorRunner.Instance;
							if (extractor.IsAvailable)
							{
								bool extracted = await extractor.ExtractZone(_currentZoneId);
								if (extracted)
									GD.Print($"[UI] Zone '{_currentZoneId}' extracted successfully.");
								else
									GD.PrintErr($"[UI] Zone extraction failed for '{_currentZoneId}', will use fallback.");
							}
						}

						_flavorLabel.Text = "Building Zone Geometry...";
						await ToSignal(GetTree().CreateTimer(0.1f), SceneTreeTimer.SignalName.Timeout);
						wm.LoadZoneMap(_currentZoneId);
						wm.PlayZoneMusic(_currentZoneId);
						
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
						wm.TeleportPlayer(_pendingSpawnX, _pendingSpawnZ, _pendingSpawnY);
						_isInitialLoadPending = false;
						if (_loadingLayer != null) 
						{
							_loadingLayer.Hide();
						}
					}
					
						if (dict.TryGetProperty("vision", out var visionDict))
					{
						int worldHour = visionDict.TryGetProperty("worldHour", out var wh) ? wh.GetInt32() : 12;
						int dawn = visionDict.TryGetProperty("dawn", out var d) ? d.GetInt32() : 6;
						int dusk = visionDict.TryGetProperty("dusk", out var dk) ? dk.GetInt32() : 18;
						
						string drinalPhase = "Full";
						if (visionDict.TryGetProperty("moons", out var mDict) && mDict.TryGetProperty("drinal", out var dDict) && dDict.TryGetProperty("phase", out var pProp))
						{
							drinalPhase = pProp.GetString();
						}
						
						wm.UpdateEnvironmentTime(worldHour, dawn, dusk, drinalPhase, true); // true = initial load
					
						// Set indoor/outdoor from server vision data (replaces hardcoded zone list)
						bool isOutdoor = visionDict.TryGetProperty("isOutdoor", out var ooProp) && ooProp.GetBoolean();
						wm.SetIndoorZone(!isOutdoor);

						// Wire up player light source from server vision data
						bool hasLight = visionDict.TryGetProperty("hasLightSource", out var hlProp) && hlProp.GetBoolean();
						wm.SetPlayerLightSource(hasLight);
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








	// â”€â”€â”€ Combat Log â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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




	// ── Chat System ─────────────────────────────────────────────────────







	// ═══════════════════════════════════════════════════════════════
	//  TRAINER WINDOW
	// ═══════════════════════════════════════════════════════════════


	// ═══════════════════════════════════════════════════════════════
	//  COMPANION WINDOW (Pet / future Mercenary)
	// ═══════════════════════════════════════════════════════════════




	// â”€â”€â”€ Buff System â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
	/// <summary>
	/// Expected JSON: { "type": "BUFFS_UPDATE", "buffs": [
	///   { "name": "Spirit of Wolf", "duration": 36.0, "maxDuration": 60.0, "beneficial": true },
	///   { "name": "Poison", "duration": 12.0, "maxDuration": 18.0, "beneficial": false },
	///   ...
	/// ]}
	/// </summary>

	// â”€â”€â”€ Sit / Stand / Combat â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€



	// â”€â”€â”€ Status Handling â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€



	// â”€â”€â”€ Inventory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

	/// <summary>
	/// Create 10 identical slot buttons in the inventory grid (2 columns, 5 rows).
	/// </summary>

	// ─── Auto-Equip Slot ────────────────────────────────────────────

	// ─── Cursor Label (floating item name on cursor) ────────────────

	// ─── Item Detail Popup ──────────────────────────────────────────

	// ─── Slot Input Handling (all slots use this) ───────────────────







	/// <summary>
	/// Build the equipment paperdoll grid programmatically.
	/// Creates labeled slot rows arranged in pairs for a compact paperdoll look.
	/// </summary>



	/// <summary>
	/// Update the inventory window's built-in stats panel with current character data.
	/// Called from OnCharacterStatusReceived.
	/// </summary>


	/// <summary>
	/// Format copper into EQ-style pp/gp/sp/cp string.
	/// </summary>

	// â”€â”€â”€ Logging â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€


	// â”€â”€â”€ Status Bars â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€





}
