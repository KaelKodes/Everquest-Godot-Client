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
	private ProgressBar _enduranceBar;
	private Label _enduranceLabel;
	private Label _playerNameLabel;
	private TextureRect _combatStatusIcon;
	private Texture2D _texRegenCombat;
	private Texture2D _texRegenCooldown;
	private Texture2D _texRegenIdle;
	private Texture2D _texRegenResting;

	private enum CombatRegenIconKind
	{
		Combat,
		Cooldown,
		/// <summary>Out of combat, rested regen gate active, standing.</summary>
		Idle,
		/// <summary>Out of combat, rested regen gate active, sitting (medding).</summary>
		Resting
	}

	/// <summary>Last combat / rested-regen flags from STATUS; used with <see cref="_isSitting"/> for regen icon.</summary>
	private bool _regenStatusInCombat;
	private bool _regenStatusRestedRegen = true;

	private Button _sitStandBtn;
	private Button _autoFightBtn;
	private Button _bagsBtn;
	private Button _campBtn;
	private RichTextLabel _invSkillsText;    // skills tab in inventory
	private bool _draggingInventory = false;
	private Vector2 _dragOffset;
	
	// Action & Buff Containers
	private VBoxContainer _actionBar;
	private Window _buffBarWindow;
	private Container _buffBar;
	private Window _songBarWindow;
	private Container _songBar;
	private Container _targetBuffBar;
	private RichTextLabel _combatLog;
	private Texture2D _spellGemTexture;
	private Button[] _spellSlotButtons = new Button[8];
	private Label[] _spellSlotLabels = new Label[8]; // Spell name overlay
	
	// Hotbar System
	private HotbarManager _hotbarManager;
	public HotbarManager HotbarManager => _hotbarManager;
	
	// Inventory & Equipment Window
	private Control _inventoryWindow;
	private Button[] _invSlots = new Button[8]; // 8 general inventory slots
	private GridContainer _slotsGrid;        // container for inventory slot buttons
	private Control _equipGrid;              // equipment slot container
	private RichTextLabel _invStatsText;     // stats panel in inventory
	private RichTextLabel _detailedStatsText;// detailed stats panel (left)
	private RichTextLabel _detailedStatsRight;// detailed stats panel (right)
	private Label[] _currencyLabels = new Label[4]; // pp, gp, sp, cp
	private SkillsWindow _skillsWindow;
	private VBoxContainer _skillsListContainer;
	private readonly Dictionary<string, Button> _equipSlots = new();

	// Item Interaction System
	private JsonElement? _heldItem = null;
	private int _heldFromSlotId = -1;
	private TextureRect _cursorIcon;
	private ItemDetailsWindow _itemDetailPopup;
	private SpellDetailsWindow _spellDetailPopup;
	private Button _autoEquipBtn;
	private double _rightClickTimer = -1;
	private Button _rightClickTarget = null;
	private JsonElement? _rightClickItemData = null;
	private int _rightClickSlotId = -1;
	private readonly Dictionary<Button, JsonElement> _slotItemData = new(); // map button â†’ item JSON

	private Window _splitStackWindow;
	private HSlider _splitStackSlider;
	private Label _splitStackHintLabel;
	private int _splitPendingFrom;
	private int _splitPendingTo;
	private int _splitHeldQuantity;

	// Target Frame
	private Window _targetWindow;
	private Label _targetNameLabel;
	private ProgressBar _targetHpBar;
	private Label _targetHpLabel;
	
	// Target's Target Frame
	private Window _targetsTargetWindow;
	private Label _targetsTargetNameLabel;
	private ProgressBar _targetsTargetHpBar;
	private Label _targetsTargetHpLabel;
	
	// Extended Target Frame
	private Window _targetListWindow;
	private TrackingWindow _trackingWindow;
	public void ClearTrackingWindow() { _trackingWindow = null; }
	private LootWindow _lootWindow;
	private BankWindow _bankWindow;
	private Button[] _extendedTargetBtns = new Button[10];

	// Group System
	private GroupWindow _groupWindow;
	private ConfirmationDialog _invitePopup;
	private bool _targetsTargetEnabled = true; // Master switch for Alt+U


	// XP / Level / Zone
	private ProgressBar _xpBar;
	private Label _xpLabel;
	private Label _levelLabel;
	private Label _zoneLabel;
	private HBoxContainer _zoneConnections;
	private string _currentZoneId = "";
	/// <summary>Optional .s3d basename from server STATUS when it differs from <see cref="_currentZoneId"/> (PEQ short_name).</summary>
	private string _lanternArchiveBase = null;
	
	// Map
	private MudMap _mudMap;
	private RichTextLabel _locationLabel;
	private Control _mapPanel;

	// Scenes
	private PackedScene _buffIconScene = GD.Load<PackedScene>("res://Scenes/BuffIcon.tscn");
	public PackedScene BuffIconScene => _buffIconScene;
	private PackedScene _inventoryItemScene = GD.Load<PackedScene>("res://Scenes/UI/InventoryItem.tscn");
	private PackedScene _inventoryWindowScene = GD.Load<PackedScene>("res://Scenes/UI/InventoryWindow.tscn");
	private PackedScene _merchantWindowScene = GD.Load<PackedScene>("res://Scenes/UI/MerchantWindow.tscn");
	private PackedScene _giveNPCWindowScene = GD.Load<PackedScene>("res://Scenes/UI/GiveNPCWindow.tscn");
	private PackedScene _skillsWindowScene = GD.Load<PackedScene>("res://Scenes/UI/SkillsWindow.tscn");
	private PackedScene _groupWindowScene = GD.Load<PackedScene>("res://Scenes/UI/GroupWindow.tscn");
	private PackedScene _melodyWindowScene = GD.Load<PackedScene>("res://Scenes/UI/MelodyWindow.tscn");
	private PackedScene _invitePopupScene = GD.Load<PackedScene>("res://Scenes/UI/GroupInvitePopup.tscn");
	
	private MelodyWindow _melodyWindow;


	private Control _merchantWindow;
	private Control _giveNPCWindow;
	private Label _giveNPCTitle;
	private Button[] _giveNPCSlots = new Button[4];
	private Button _giveNPCOk;
	private Button _giveNPCCancel;
	private string _giveNPCId;
	private Label _merchantTitle;
	private TabContainer _merchantTabs;
	private VBoxContainer _merchantTradeList;
	private VBoxContainer _merchantRecoverList;
	private LineEdit _merchantSearchInput;
	private TextureRect _merchantSlotRect;
	private Label _merchantSelectionName;
	private Label _merchantSelectionPrice;
	private Button _merchantActionBtn;
	private Button _merchantSellJunkBtn;

	// Track the selected item in the merchant slot
	private string _merchantSelectedItemId = null;
	private string _merchantSelectedItemKey = null;
	private int _merchantSelectedSlotId = -1;
	private int _merchantSelectedPrice = 0;
	private int _merchantSelectedBuybackId = -1;
	private string _merchantSelectedAction = ""; // "SELL", "BUY", "BUY_RECOVER"
	
	private string _activeMerchantId = null; // Track open merchant for sell transactions
	/// <summary>Merchant display name while shop is open — used to refresh the title when coin changes.</summary>
	private string _merchantOpenNpcName = "";

	private HBoxContainer _merchantQtyRow;
	private SpinBox _merchantQtySpin;
	private Button _merchantQtyMaxBtn;
	private int _merchantUnitPriceCp = 1;
	private int _merchantSellMaxQty = 1;
	private int _merchantSellUnitCp = 1;

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
		public int Icon;
		public int StackSize;
	}
	private List<MerchantItem> _merchantItems = new List<MerchantItem>();
	private int _merchantPlayerClassBitmask = 65535;
	private int _merchantPlayerLevel = 60;
	private string _merchantSortMode = "name";
	private bool _merchantShowUsable = false;
	private HBoxContainer _merchantSortBar = null;

	// Chat input
	private LineEdit _chatInput;
	private Control _chatWindow;
	private string _lastWhisperSender = "";
	private bool _chatInputFocused = false;
	public bool IsChatFocused => _chatInputFocused;

	/// <summary>GM modal dialogs (prompt windows) block world movement / combat keys while open.</summary>
	private int _gmWorldInputBlockDepth;
	public bool IsGmWorldInputBlocked => _gmWorldInputBlockDepth > 0;
	private void PushGmWorldInputBlock() => _gmWorldInputBlockDepth++;
	private void PopGmWorldInputBlock()
	{
		if (_gmWorldInputBlockDepth > 0) _gmWorldInputBlockDepth--;
	}
	
	public GameClient GetClient() => _client;
	
	public void ReleaseChatFocus()
	{
		if (_chatInput != null)
		{
			_chatInput.ReleaseFocus();
		}
		FocusMode = FocusModeEnum.All;
		GrabFocus();
	}
	
	public static MainUI Instance { get; private set; }
	private bool _autoFight = false;
	private bool _isSitting = false;
	private int _currentZoneNumericId = -1;
	private double _lastMainServerPosSync = 0;
	private bool _isSinging = false;
	private string _lastPlayerEquipVisuals = "";
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
	private HBoxContainer _socialNavRow;
	private Label _socialPageLabel;
	
	private CanvasLayer _loadingLayer;
	private ColorRect _loadingOverlay;
	private Label _loadingLabel;
	private ProgressBar _loadingBar;
	private Label _flavorLabel;
	private bool _isInitialLoadPending = true;
	/// <summary>When true, <c>spawnPos</c> from the server set <c>_pendingSpawn*</c>; follow-up STATUS without <c>spawnPos</c> must not clobber it (server clears pendingTeleport after first send).</summary>
	private bool _pendingSpawnLockedFromSpawnPos = false;
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
		public int MemIcon;
		public int Icon;
	}
	private MemorizedSpell[] _spells = new MemorizedSpell[8];
	private double _currentMana = 0;
	private double _maxMana = 0;
	private double _currentHp = 0;
	private double _maxHp = 0;
	private double _currentEndurance = 100;
	private string _charName;
	private List<string> _spellLoadouts = new List<string>();
	public int GetPlayerLevel() => _charLevel;
	private int _charLevel = 1;
	private int _raceId = 1;

	public int GetPlayerClassMask()
	{
		string c = _cls.ToLower();
		if (c == "warrior") return 1;
		if (c == "cleric") return 2;
		if (c == "paladin") return 4;
		if (c == "ranger") return 8;
		if (c == "shadow knight" || c == "shadow_knight") return 16;
		if (c == "druid") return 32;
		if (c == "monk") return 64;
		if (c == "bard") return 128;
		if (c == "rogue") return 256;
		if (c == "shaman") return 512;
		if (c == "necromancer") return 1024;
		if (c == "wizard") return 2048;
		if (c == "magician") return 4096;
		if (c == "enchanter") return 8192;
		if (c == "beastlord") return 16384;
		if (c == "berserker") return 32768;
		return 1;
	}

	public int GetPlayerRaceMask()
	{
		if (_raceId == 1) return 1;
		if (_raceId == 2) return 2;
		if (_raceId == 3) return 4;
		if (_raceId == 4) return 8;
		if (_raceId == 5) return 16;
		if (_raceId == 6) return 32;
		if (_raceId == 7) return 64;
		if (_raceId == 8) return 128;
		if (_raceId == 9) return 256;
		if (_raceId == 10) return 512;
		if (_raceId == 11) return 1024;
		if (_raceId == 12) return 2048;
		if (_raceId == 128) return 4096;
		if (_raceId == 130) return 8192;
		if (_raceId == 330) return 16384;
		return 1;
	}

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
		public int MemIcon;
		public int Icon;
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

	// Scroll scribing (server-timed)
	private bool _isScribing = false;
	private float _scribeTimeTotal = 0;
	private float _scribeTimeElapsed = 0;
	private string _scribingSpellName = "";
	private ProgressBar _scribeBar;
	private Label _scribeBarLabel;
	private Panel _scribeBarPanel;
	private int _scribeRightLastSlot = -1;
	private ulong _scribeRightLastMs = 0;

	// Spellbook -> spellbar memorize (server-timed; reuses the scribe progress bar UI).
	// Flow mirrors scribing: client sends BEGIN_MEMORIZE_SPELL, server validates and ticks
	// the timer (half of the scribe duration), then completes with MEMORIZE_COMPLETE and a
	// fresh SPELLBOOK_UPDATE whose gem carries a starting cooldown.
	private bool _isMemorizing = false;
	private float _memorizeTimeTotal = 0;
	private float _memorizeTimeElapsed = 0;
	private string _memorizingSpellName = "";

	// Options state
	private bool _showPlayerName = false;
	private bool _dynamicShadows = true;
	private bool _hasActiveMercenary = false;

	public string GetPlayerName() => _charName;
	private float _cameraFov = 75f;
	private int _antiAliasing = 0; // 0=Off, 1=2x, 2=4x, 3=8x
	private bool _vSync = true;
	private bool _fullscreen = false;
	private int _maxFps = 0; // 0 = unlimited
	
	private Panel _optionsPanel;
	private LineEdit _optDragItemPathEdit;
	private FileDialog _optDragItemFolderDialog;
	private CopyLayoutWindow _copyLayoutWindow;
	private AudioPlayerWindow _audioPlayerWindow;

	// Buff tracking for duration ticking
	public class ActiveBuff
	{
		public string Name;
		public float DurationMax;
		public float DurationRemaining;
		public Panel IconNode;
		public bool IsBeneficial;
		public int MemIcon;
		/// <summary>Classic spell icon id (spellsXX.tga). Used when <see cref="MemIcon"/> is unset.</summary>
		public int SpellIcon;
	}
	private List<ActiveBuff> _activeBuffs = new List<ActiveBuff>();
	private List<ActiveBuff> _activeSongBuffs = new List<ActiveBuff>();
	private List<ActiveBuff> _activeTargetBuffs = new List<ActiveBuff>();

	private PopupMenu _buffContextMenu;
	private ActiveBuff _contextMenuTargetBuff;

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
	private Label _companionTypeClassLabel;
	private Label _companionStateLabel;
	private ProgressBar _companionHpBar;
	private Label _companionHpLabel;
	private ProgressBar _companionMpBar;
	private Label _companionMpLabel;
	private ProgressBar _companionEndBar;
	private Label _companionEndLabel;
	private ProgressBar _companionHateBar;
	private Label _companionHateLabel;
	private TextureRect[] _companionBuffIcons = new TextureRect[24];
	private Button _companionGetLostBtn;
	private OptionButton _mercStanceDropdown;
	private bool _hasPet = false;

	// ── Mercenaries Manager ──
	private Window _mercenariesManagerWindow;
	private Button[] _mercSlotButtons = new Button[2];
	private Button _mercSwitchBtn;
	private Button _mercSuspendBtn;
	private Button _mercReleaseBtn;
	private Button _mercPlayAsBtn;
	private Button _mercReturnBtn;
	private int _selectedMercSlot = -1;
	private JsonElement _mercenariesData;

	public override void _Ready()
	{
		Instance = this;
		UILayoutManager.Initialize(GameState.CharacterName);
		
		// Use the global networking singleton
		_client = GameClient.Instance;
		_client.CharacterStatusReceived += OnCharacterStatusReceived;
		_client.SpellbookUpdated += OnSpellbookUpdated;
		_client.SpellbookFullReceived += OnSpellbookFullReceived;
		_client.SpellLoadoutsReceived += OnSpellLoadoutsReceived;
		_client.CombatLogReceived += OnCombatLogReceived;
		_client.BuffsUpdated += OnBuffsUpdated;
		_client.InventoryUpdated += OnInventoryUpdated;
		_client.PetInventoryUpdated += OnPetInventoryUpdated;
		_client.MercenariesUpdated += OnMercenariesUpdated;
		_client.HireStudentReceived += OnHireStudentReceived;
		_client.ZoneStateReceived += OnZoneStateReceived;
		_client.MobMoveReceived += OnMobMoveReceived;
		_client.EnvironmentUpdated += OnEnvironmentUpdated;
		_client.EntitySneakReceived += OnEntitySneakReceived;
		_client.EntityHideReceived += OnEntityHideReceived;
		_client.SneakResultReceived += OnSneakResultReceived;
		_client.HideResultReceived += OnHideResultReceived;
		_client.SneakBrokenReceived += OnSneakBrokenReceived;
		_client.SpellDetailsReceived += OnSpellDetailsReceived;
		_client.HideBrokenReceived += OnHideBrokenReceived;
		_client.NpcSayReceived += OnNpcSayReceived;
		_client.MerchantOpened += OnMerchantOpened;
		_client.MerchantOfferReceived += OnMerchantOfferReceived;
		_client.MerchantRecoverListReceived += OnMerchantRecoverListReceived;
		_client.TrainerOpened += OnTrainerOpened;
		_client.CloseUI += OnCloseUi;
		_client.BankOpened += OnBankOpened;
		_client.ChatReceived += OnChatReceived;
		_client.CampComplete += OnCampComplete;
		_client.DoorStateChanged += OnDoorStateChanged;
		_client.SpellAnimationReceived += OnSpellAnimationReceived;
		_client.ScribeScrollReceived += OnScribeScrollReceived;
		_client.MemorizeSpellReceived += OnMemorizeSpellReceived;
		_client.MessageReceived += OnGenericMessage;

		// ── Connection Recovery ──
		_client.Connected += OnClientConnected;
		_client.Disconnected += OnClientDisconnected;
		_client.RelayConnected += OnRelayConnected;
		_client.AccountOkReceived += OnAccountOkReceived;

		if (_client.IsSocketConnected)
		{
			_client.SendRaw("{\"type\":\"REQUEST_SYNC\"}");
		}

		// Buff Context Menu created lazily on first right-click (see EnsureBuffContextMenu)

		// Wire up chat input
		_chatWindow = GetNode<Control>("ChatWindow");
		
		_chatInput = GetNode<LineEdit>("%ChatInput");
		if (_chatInput != null)
		{
			_chatInput.TextSubmitted += OnChatSubmitted;
			_chatInput.FocusEntered += () => _chatInputFocused = true;
			_chatInput.FocusExited += () => _chatInputFocused = false;
			_chatInput.GuiInput += (ev) => {
				if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
				{
					if (_heldItem.HasValue)
					{
						string itemName = _heldItem.Value.GetProperty("itemName").GetString();
						int itemId = _heldItem.Value.GetProperty("item_id").GetInt32();
						string link = $"[url={{\"type\":\"item\",\"id\":{itemId}}}][color=#40a0ff][{itemName}][/color][/url]";
						_chatInput.Text += link;
						_chatInput.CaretColumn = _chatInput.Text.Length;
						CancelHeldItem();
						_chatInput.GrabFocus();
						GetViewport().SetInputAsHandled();
					}
					else if (_hotbarManager != null && _hotbarManager.IsDragging)
					{
						var dragData = _hotbarManager.DragData;
						if (dragData != null && dragData.Type == Hotbar.HotbuttonType.Spell)
						{
							string spellName = dragData.DisplayName;
							int spellId = _spells[dragData.SpellSlotIndex].SpellId;
							string link = $"[url={{\"type\":\"spell\",\"id\":{spellId}}}][color=#ffb000][{spellName}][/color][/url]";
							_chatInput.Text += link;
							_chatInput.CaretColumn = _chatInput.Text.Length;
							_hotbarManager.CancelDrag();
							_chatInput.GrabFocus();
							GetViewport().SetInputAsHandled();
						}
					}
				}
			};
		}

		// Create Windows (hidden by default)
		_inventoryWindow = _inventoryWindowScene.Instantiate<Control>();
		AddChild(_inventoryWindow);
		_inventoryWindow.Hide();
		
		_slotsGrid = _inventoryWindow.GetNode<GridContainer>("MainVBox/TabContainer/Inventory/RightColumn/GeneralSlotsGrid");
		_equipGrid = _inventoryWindow.GetNode<Control>("MainVBox/TabContainer/Inventory/PaperdollColumn/PaperdollPanel/EquipGrid");
		_invStatsText = _inventoryWindow.GetNode<RichTextLabel>("MainVBox/TabContainer/Inventory/StatsColumn/StatsScroll/StatsText");
		// Build two-column Stats tab: replace the single DetailedStatsText with an HBox containing left + right
		var existingStatsLabel = _inventoryWindow.GetNodeOrNull<RichTextLabel>("MainVBox/TabContainer/Stats/StatsVBox/DetailedStatsText");
		var statsParent = existingStatsLabel?.GetParent();
		if (existingStatsLabel != null && statsParent != null) {
			int idx = existingStatsLabel.GetIndex();
			statsParent.RemoveChild(existingStatsLabel);
			existingStatsLabel.QueueFree();

			var hbox = new HBoxContainer();
			hbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			hbox.SizeFlagsVertical = SizeFlags.ExpandFill;
			hbox.AddThemeConstantOverride("separation", 8);

			_detailedStatsText = new RichTextLabel();
			_detailedStatsText.BbcodeEnabled = true;
			_detailedStatsText.FitContent = true;
			_detailedStatsText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_detailedStatsText.SizeFlagsVertical = SizeFlags.ExpandFill;
			_detailedStatsText.AddThemeFontSizeOverride("normal_font_size", 11);
			_detailedStatsText.AddThemeFontSizeOverride("bold_font_size", 11);

			_detailedStatsRight = new RichTextLabel();
			_detailedStatsRight.BbcodeEnabled = true;
			_detailedStatsRight.FitContent = true;
			_detailedStatsRight.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_detailedStatsRight.SizeFlagsVertical = SizeFlags.ExpandFill;
			_detailedStatsRight.AddThemeFontSizeOverride("normal_font_size", 11);
			_detailedStatsRight.AddThemeFontSizeOverride("bold_font_size", 11);

			hbox.AddChild(_detailedStatsText);
			hbox.AddChild(_detailedStatsRight);
			statsParent.AddChild(hbox);
			statsParent.MoveChild(hbox, idx);
		}
		
		_currencyLabels[0] = _inventoryWindow.GetNode<Label>("MainVBox/TabContainer/Inventory/RightColumn/CurrencyPanel/VBox/Platinum/Label");
		_currencyLabels[1] = _inventoryWindow.GetNode<Label>("MainVBox/TabContainer/Inventory/RightColumn/CurrencyPanel/VBox/Gold/Label");
		_currencyLabels[2] = _inventoryWindow.GetNode<Label>("MainVBox/TabContainer/Inventory/RightColumn/CurrencyPanel/VBox/Silver/Label");
		_currencyLabels[3] = _inventoryWindow.GetNode<Label>("MainVBox/TabContainer/Inventory/RightColumn/CurrencyPanel/VBox/Copper/Label");

		// Add coin icons from dragitem5 (iconId 144=plat, 145=gold, 146=silver, 147=copper)
		int[] coinIconIds = { 144, 145, 146, 147 };
		for (int i = 0; i < 4; i++) {
			var coinRow = _currencyLabels[i]?.GetParent();
			if (coinRow == null) continue;
			var iconMgr = IconManager.Instance;
			if (iconMgr == null) continue;
			var coinTex = iconMgr.GetItemIcon(coinIconIds[i]);
			if (coinTex == null) continue;
			var coinIcon = new TextureRect();
			coinIcon.Texture = coinTex;
			coinIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			coinIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			coinIcon.CustomMinimumSize = new Vector2(20, 20);
			coinRow.AddChild(coinIcon);
			coinRow.MoveChild(coinIcon, 0); // icon before the label
		}
		
		_spellDetailPopup = new SpellDetailsWindow();
		AddChild(_spellDetailPopup);

		BuildInventorySlots();
		
		// Close / Done buttons
		_inventoryWindow.GetNode<Button>("MainVBox/TitleBar/HBox/CloseBtn").Pressed += HideMainInventoryWindow;
		_inventoryWindow.GetNode<Button>("MainVBox/ButtonBar/DoneBtn").Pressed += HideMainInventoryWindow;
		_inventoryWindow.GetNode<Button>("MainVBox/ButtonBar/DestroyBtn").Pressed += () => {
			Log("SYSTEM", "[color=yellow]Click the X on an item to destroy it.[/color]");
		};
		var tabContainer = _inventoryWindow.GetNode<TabContainer>("MainVBox/TabContainer");
		var destroyBtn = _inventoryWindow.GetNode<Button>("MainVBox/ButtonBar/DestroyBtn");
		tabContainer.TabChanged += (long tab) => {
			destroyBtn.Visible = (tab == 0); // Only show on Inventory tab
		};
		
		// Skills Window
		_skillsWindow = _skillsWindowScene.Instantiate<SkillsWindow>();
		AddChild(_skillsWindow);
		_skillsWindow.Hide();
		_skillsListContainer = _skillsWindow.GetNode<VBoxContainer>("VBox/Scroll/SkillList");
		_skillsWindow.GetNode<Button>("VBox/TitleBar/HBox/CloseBtn").Pressed += () => _skillsWindow.Hide();
		_skillsWindow.GetNode<Button>("VBox/ButtonBar/DoneBtn").Pressed += () => _skillsWindow.Hide();
		
		// Build equipment paperdoll slots
		BuildEquipmentGrid();
		BuildAutoEquipSlot();
		SetupInventoryDrag();
		BuildCursorIcon();
		BuildItemDetailPopup();

		// Merchant Window
		_merchantWindow = _merchantWindowScene.Instantiate<Control>();
		AddChild(_merchantWindow);
		_merchantWindow.Hide();
		ApplyWindowPos(_merchantWindow, "MerchantWindow", UILayoutManager.GetSection("Windows"));
		
		_merchantTitle = _merchantWindow.GetNode<Label>("VBox/Title");
		_merchantTabs = _merchantWindow.GetNode<TabContainer>("VBox/TabContainer");
		_merchantTradeList = _merchantWindow.GetNode<VBoxContainer>("VBox/TabContainer/Trade/Scroll/ItemList");
		_merchantRecoverList = _merchantWindow.GetNode<VBoxContainer>("VBox/TabContainer/Recover/Scroll/RecoverList");
		_merchantSearchInput = _merchantWindow.GetNode<LineEdit>("VBox/TabContainer/Trade/SearchBar");
		
		_merchantSlotRect = _merchantWindow.GetNode<TextureRect>("VBox/SelectionPanel/HBox/SlotRect");
		_merchantSelectionName = _merchantWindow.GetNode<Label>("VBox/SelectionPanel/HBox/VBox/SelectionName");
		_merchantSelectionPrice = _merchantWindow.GetNode<Label>("VBox/SelectionPanel/HBox/VBox/SelectionPrice");
		_merchantQtyRow = _merchantWindow.GetNode<HBoxContainer>("VBox/SelectionPanel/HBox/VBox/QtyRow");
		_merchantQtySpin = _merchantWindow.GetNode<SpinBox>("VBox/SelectionPanel/HBox/VBox/QtyRow/QtySpin");
		_merchantQtyMaxBtn = _merchantWindow.GetNode<Button>("VBox/SelectionPanel/HBox/VBox/QtyRow/QtyMaxBtn");
		_merchantActionBtn = _merchantWindow.GetNode<Button>("VBox/SelectionPanel/HBox/ActionBtn");
		_merchantSellJunkBtn = _merchantWindow.GetNode<Button>("VBox/BottomRow/SellJunkBtn");

		_merchantWindow.GetNode<Button>("VBox/BottomRow/CloseBtn").Pressed += () => {
			_merchantWindow.Hide();
			_activeMerchantId = null;
			_merchantOpenNpcName = "";
			// Refresh inventory to hide sell buttons
			if (!string.IsNullOrEmpty(_client.LastInventoryPayload))
				OnInventoryUpdated(_client.LastInventoryPayload);
		};

		// Give NPC Window
		_giveNPCWindow = _giveNPCWindowScene.Instantiate<Control>();
		AddChild(_giveNPCWindow);
		_giveNPCWindow.Hide();

		_giveNPCTitle = _giveNPCWindow.GetNode<Label>("VBox/Title");
		for (int i = 0; i < 4; i++)
		{
			int idx = i;
			_giveNPCSlots[i] = _giveNPCWindow.GetNode<Button>($"VBox/Slot{i + 1}");
			_giveNPCSlots[i].Pressed += () => OnGiveNPCSlotClicked(idx);
		}

		_giveNPCOk = _giveNPCWindow.GetNode<Button>("VBox/HBox/BtnOK");
		_giveNPCCancel = _giveNPCWindow.GetNode<Button>("VBox/HBox/BtnCancel");

		_giveNPCOk.Pressed += OnGiveNPCOk;
		_giveNPCCancel.Pressed += OnGiveNPCCancel;

		_hpBar = GetNode<ProgressBar>("%HPBar");
		_hpLabel = GetNode<Label>("%HPLabel");
		
		_manaBar = GetNode<ProgressBar>("%ManaBar");
		_manaLabel = GetNode<Label>("%ManaLabel");
		
		_enduranceBar = new ProgressBar();
		_enduranceBar.Name = "EnduranceBar";
		_enduranceBar.CustomMinimumSize = new Vector2(0, 25);
		_enduranceBar.ShowPercentage = false;
		
		var endurStyle = new StyleBoxFlat();
		endurStyle.BgColor = new Color(0.8f, 0.8f, 0.1f, 1);
		endurStyle.BorderWidthBottom = 1; endurStyle.BorderWidthTop = 1; endurStyle.BorderWidthLeft = 1; endurStyle.BorderWidthRight = 1;
		endurStyle.BorderColor = new Color(0.2f, 0.2f, 0, 1);
		_enduranceBar.AddThemeStyleboxOverride("fill", endurStyle);

		_enduranceLabel = new Label();
		_enduranceLabel.Name = "EnduranceLabel";
		_enduranceLabel.Text = "END: 100/100";
		_enduranceLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_enduranceLabel.VerticalAlignment = VerticalAlignment.Center;
		_enduranceLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_enduranceLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		_enduranceLabel.AddThemeConstantOverride("outline_size", 4);
		_enduranceLabel.AddThemeFontSizeOverride("font_size", 14);

		_enduranceBar.AddChild(_enduranceLabel);
		
		var statusVBox = GetNodeOrNull<VBoxContainer>("StatusWindow/VBox");
		if (statusVBox != null) {
			statusVBox.AddChild(_enduranceBar);
		}

		_playerNameLabel = GetNode<Label>("%PlayerName");

		_texRegenCombat = GD.Load<Texture2D>("res://Assets/UI/ClassicUI/CombatIcon.png");
		_texRegenCooldown = GD.Load<Texture2D>("res://Assets/UI/ClassicUI/CooldownIcon.png");
		_texRegenIdle = GD.Load<Texture2D>("res://Assets/UI/ClassicUI/IdleIcon.png");
		_texRegenResting = GD.Load<Texture2D>("res://Assets/UI/ClassicUI/RestedIcon.png");
		if (_texRegenIdle == null)
			GD.PushWarning("[UI] IdleIcon.png missing under Assets/UI/ClassicUI/ — Idle regen state will use cooldown art.");
		_combatStatusIcon = GetNodeOrNull<TextureRect>("%CombatStatusIcon");
		if (_combatStatusIcon != null)
		{
			var iconSize = new Vector2(18, 18);
			_combatStatusIcon.CustomMinimumSize = iconSize;
			_combatStatusIcon.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
			_combatStatusIcon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
			_combatStatusIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			UpdateCombatRegenStatusFromServer(false, true);
		}
		
		_sitStandBtn = GetNodeOrNull<Button>("%SitStandBtn");
		_autoFightBtn = GetNodeOrNull<Button>("%AutoFightBtn");
		_campBtn = GetNodeOrNull<Button>("%CampBtn");
		// Stats panel removed â€” now embedded in inventory window
		
		if (_campBtn != null) _campBtn.Pressed += OnCampPressed;
		
		_actionBar = GetNodeOrNull<VBoxContainer>("%SpellBar") ?? GetNodeOrNull<VBoxContainer>("SpellBarWindow/SpellBar");
		if (_actionBar != null)
		{
			_actionBar.MouseFilter = Control.MouseFilterEnum.Stop;
			_actionBar.GuiInput += (ev) => {
				if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
				{
					ShowSpellbarContextMenu(_actionBar);
				}
			};
		}
		_buffBarWindow = GetNodeOrNull<Window>("%BuffWindow");
		_buffBar = GetNodeOrNull<Container>("%BuffBar");

		_songBarWindow = GetNodeOrNull<Window>("%SongWindow");
		_songBar = GetNodeOrNull<Container>("%SongBar");
		_combatLog = GetNode<RichTextLabel>("%CombatLog");
		_combatLog.BbcodeEnabled = true;
		_combatLog.MetaClicked += OnMetaClicked;
		SetupChatTabs();

		// Target Frame
		_targetWindow = GetNode<Window>("%TargetWindow");
		_targetNameLabel = GetNode<Label>("%TargetName");
		_targetHpBar = GetNode<ProgressBar>("%TargetHPBar");
		_targetHpLabel = GetNode<Label>("%TargetHPLabel");
		_targetBuffBar = GetNodeOrNull<Container>("%TargetBuffBar");

		// Target's Target Frame
		_targetsTargetWindow = GetNode<Window>("%TargetsTargetWindow");
		_targetsTargetNameLabel = GetNode<Label>("%TargetTargetName");
		_targetsTargetHpBar = GetNode<ProgressBar>("%TargetTargetHPBar");
		_targetsTargetHpLabel = GetNode<Label>("%TargetTargetHPLabel");

		// Group Window
		_groupWindow = _groupWindowScene.Instantiate<GroupWindow>();
		AddChild(_groupWindow);
		_groupWindow.Hide();

		// Extended Target List
		_targetListWindow = GetNodeOrNull<Window>("%TargetListWindow") ?? GetNodeOrNull<Window>("TargetListWindow");
		if (_targetListWindow != null)
		{
			_targetListWindow.CloseRequested += () => _targetListWindow.Hide();
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
				// Progress is now managed manually in the loading pipeline
				// _loadingBar.MaxValue = tot;
				// _loadingBar.Value = cur;
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
						string keyToMemorize = _pendingMemorizeSpellKey;
						string nameToMemorize = _pendingMemorizeSpellName;
						_pendingMemorizeSpellKey = null;
						_pendingMemorizeSpellName = null;
						// Server resolves the spell from the key — works for any scribed
						// spell, including ones not currently on the bar.
						BeginMemorizeSpellTimed(slotIndex, keyToMemorize, nameToMemorize);
						return;
					}
					OnSpellSlotPressed(slotIndex);
				}
				else if (mb.ButtonIndex == MouseButton.Right)
				{
					if (mb.CtrlPressed)
					{
						if (_spells[slotIndex].SpellId > 0 && _hotbarManager != null)
							_hotbarManager.StartSpellDrag(slotIndex, _spells[slotIndex].Name);
						GetViewport().SetInputAsHandled();
						return;
					}

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
		_castBarPanel.ZIndex = 10;
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
			
			// Save on drag end
			if (ev is InputEventMouseButton mbEnd && mbEnd.ButtonIndex == MouseButton.Left && !mbEnd.Pressed)
			{
				SaveWindowPositions();
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

		// ── Scribe progress (below cast bar) ──
		_scribeBarPanel = new Panel();
		_scribeBarPanel.CustomMinimumSize = new Vector2(250, 26);
		_scribeBarPanel.AnchorLeft = 0.5f;
		_scribeBarPanel.AnchorRight = 0.5f;
		_scribeBarPanel.AnchorTop = 0.60f;
		_scribeBarPanel.AnchorBottom = 0.60f;
		_scribeBarPanel.OffsetLeft = -125;
		_scribeBarPanel.OffsetRight = 125;
		_scribeBarPanel.OffsetTop = -13;
		_scribeBarPanel.OffsetBottom = 13;
		_scribeBarPanel.ZIndex = 10;
		_scribeBarPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
		var scribePanelStyle = new StyleBoxFlat();
		scribePanelStyle.BgColor = new Color(0.08f, 0.12f, 0.18f, 0.88f);
		scribePanelStyle.SetBorderWidthAll(1);
		scribePanelStyle.BorderColor = new Color(0.35f, 0.55f, 0.85f, 0.9f);
		scribePanelStyle.CornerRadiusBottomLeft = 3;
		scribePanelStyle.CornerRadiusBottomRight = 3;
		scribePanelStyle.CornerRadiusTopLeft = 3;
		scribePanelStyle.CornerRadiusTopRight = 3;
		_scribeBarPanel.AddThemeStyleboxOverride("panel", scribePanelStyle);

		_scribeBar = new ProgressBar();
		_scribeBar.AnchorRight = 1;
		_scribeBar.AnchorBottom = 1;
		_scribeBar.OffsetLeft = 4;
		_scribeBar.OffsetRight = -4;
		_scribeBar.OffsetTop = 4;
		_scribeBar.OffsetBottom = -4;
		_scribeBar.MinValue = 0;
		_scribeBar.MaxValue = 100;
		_scribeBar.ShowPercentage = false;
		_scribeBar.MouseFilter = Control.MouseFilterEnum.Ignore;
		var scribeFill = new StyleBoxFlat();
		scribeFill.BgColor = new Color(0.25f, 0.45f, 0.85f, 0.92f);
		scribeFill.SetCornerRadiusAll(2);
		_scribeBar.AddThemeStyleboxOverride("fill", scribeFill);
		var scribeBg = new StyleBoxFlat();
		scribeBg.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.8f);
		_scribeBar.AddThemeStyleboxOverride("background", scribeBg);
		_scribeBarPanel.AddChild(_scribeBar);

		_scribeBarLabel = new Label();
		_scribeBarLabel.AnchorRight = 1;
		_scribeBarLabel.AnchorBottom = 1;
		_scribeBarLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_scribeBarLabel.VerticalAlignment = VerticalAlignment.Center;
		_scribeBarLabel.AddThemeFontSizeOverride("font_size", 11);
		_scribeBarLabel.AddThemeColorOverride("font_color", Colors.White);
		_scribeBarLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		_scribeBarPanel.AddChild(_scribeBarLabel);

		AddChild(_scribeBarPanel);
		_scribeBarPanel.Hide();

		// Connect buttons
		if (_sitStandBtn != null) _sitStandBtn.Pressed += OnSitStandPressed;
		if (_autoFightBtn != null) _autoFightBtn.Pressed += OnAutoFightPressed;
		if (_bagsBtn != null) _bagsBtn.Pressed += ToggleMainInventoryWindow;

		// Wire SPELLS button on Simple Panel to toggle Spellbook
		var spellsBtn = GetNodeOrNull<Button>("MenuWindow/VBox/SpellsBtn");
		if (spellsBtn != null) spellsBtn.Pressed += () => ToggleSpellbook();

		// Wire OPTIONS button to toggle options panel
		var optionsBtn = GetNodeOrNull<Button>("MenuWindow/VBox/OptionsBtn");
		if (optionsBtn != null) optionsBtn.Pressed += () => ToggleOptionsPanel();

		// Dynamically add buttons to the Simple Panel
		var menuVBoxNode = GetNodeOrNull<VBoxContainer>("MenuWindow/VBox");
		if (menuVBoxNode != null)
		{
			// Add Students Button
			var studentsBtn = new Button();
			studentsBtn.Text = "STUDENTS";
			studentsBtn.CustomMinimumSize = new Vector2(0, 30);
			menuVBoxNode.AddChild(studentsBtn);
			studentsBtn.Pressed += () => {
				EnsureMercenariesManagerWindow();
				_mercenariesManagerWindow.Visible = !_mercenariesManagerWindow.Visible;
			};

			RegisterGmToolsMenu(menuVBoxNode);
		}
		if (menuVBoxNode != null)
		{
			var devSpellsBtn = new Button();
			devSpellsBtn.Text = "DEV: SPELLS";
			devSpellsBtn.Name = "DevSpellsBtn";
			menuVBoxNode.AddChild(devSpellsBtn);
			menuVBoxNode.MoveChild(devSpellsBtn, menuVBoxNode.GetChildCount() - 1);
			devSpellsBtn.Hide(); // Hide from players, keep for devs

			var spellIconDebugger = new SpellIconDebugger();
			AddChild(spellIconDebugger);
			spellIconDebugger.Hide();
			
			devSpellsBtn.Pressed += () => spellIconDebugger.Visible = !spellIconDebugger.Visible;
		}

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
			prevSocialBtn.Pressed += () => { ChangeActionPage(-1); SwitchActionTab(_actionCurrentTab, _actionTabButtons, _actionGrid); };
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
			nextSocialBtn.Pressed += () => { ChangeActionPage(1); SwitchActionTab(_actionCurrentTab, _actionTabButtons, _actionGrid); };
			_socialNavRow.AddChild(nextSocialBtn);
			
			// Wire Pressed handlers on grid buttons
			for (int bi = 0; bi < 8; bi++)
			{
				int btnIdx = bi; // capture for closure
				var btn = grid.GetChildOrNull<Button>(bi);
				if (btn != null)
				{
					btn.GuiInput += (ev) => OnActionGridSlotInput(ev, btnIdx, btn);
					btn.ClipText = true;
					btn.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
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
			wm.PlayerMoved += (x, y, z, heading) => {
				HandlePlayerSync(x, y, z, heading);
			};

			var player = wm.GetPlayerCapsule();
			if (player != null)
			{
				player.LightSourceChanged += (hasLight) => {
					// Trigger a position sync (effectively a state sync) when light toggles
					// to ensure it happens immediately even if standing still.
					var gPos = player.GlobalPosition;
					float heading = (player.Rotation.Y / (Mathf.Pi * 2.0f)) * 512.0f;
					if (heading < 0) heading += 512.0f;
					HandlePlayerSync(-gPos.X, -gPos.Z, gPos.Y, heading);
				};
			}
			
			wm.SneakToggled += (isSneaking) => {
				_client.SendRaw($"{{\"type\": \"UPDATE_SNEAK\", \"sneaking\": {isSneaking.ToString().ToLower()}}}");
			};

			wm.HideToggled += (isHiding) => {
				_client.SendRaw($"{{\"type\": \"USE_HIDE\", \"hiding\": {isHiding.ToString().ToLower()}}}");
			};
			
			wm.ZoneLineCrossed += (targetZoneId, tx, ty, tz) => {
				if (targetZoneId == _currentZoneId) {
					// Intra-zone teleport (like Felwithe caster portals)
					wm.TeleportPlayer(tx, ty, tz);
					var settleTimer = GetTree().CreateTimer(0.12f);
					settleTimer.Timeout += () =>
					{
						var w2 = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
						if (w2 != null && IsInstanceValid(w2))
							w2.FinishTeleportPlacement();
					};
				} else {
					_client.SendRaw($"{{\"type\": \"ZONE\", \"zoneId\": \"{targetZoneId}\"}}");
				}
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
						string tid = wm.CurrentTargetId;
						bool isWorldObjTarget = !string.IsNullOrEmpty(tid) && tid.StartsWith("worldobj_", StringComparison.Ordinal);
						// Mobs rely on server TARGET_UPDATE. World objects use EntityName; match by id prefix too (Godot signal type quirks).
						if (isWorldObjTarget || (!string.IsNullOrEmpty(name) && string.Equals(type, "world_object", StringComparison.Ordinal)))
						{
							_targetWindow.Visible = true;
							_targetNameLabel.Text = string.IsNullOrEmpty(name) ? "World object" : name;
							_targetHpBar.MaxValue = 1;
							_targetHpBar.Value = 1;
							_targetHpLabel.Text = "100%";
							if (_targetsTargetWindow != null)
								_targetsTargetWindow.Visible = false;
							_activeTargetBuffs.Clear();
							if (_targetBuffBar != null)
								RenderBuffsToContainer(_targetBuffBar, _activeTargetBuffs);
						}
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
			if (child is Window win && win.Name != "TargetWindow" && !(child is SpellIconDebugger))
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
		_hotbarManager.ToggleAutoAttack = OnAutoFightPressed;

		var sm = _hotbarManager.GetSocialManager();
		
		_melodyWindow = _melodyWindowScene.Instantiate<MelodyWindow>();
		_melodyWindow.InjectDependencies(this, _hotbarManager);
		AddChild(_melodyWindow);
		_melodyWindow.Hide();
		if (sm != null)
		{
			sm.GetDoAbilityName = (slot) => {
				// Slots 1-6 map to Abilities tab (_assignedSkills 0-5)
				if (slot >= 1 && slot <= 6)
				{
					return _assignedSkills[slot - 1];
				}
				// Slots 7-10 map to Combat tab (_assignedAbilities 1-4)
				else if (slot >= 7 && slot <= 10)
				{
					return _assignedAbilities[slot - 6];
				}
				return "";
			};
			
			// Macro target substitution fallbacks (can be updated when server syncs more entity info)
			sm.GetCurrentTargetGenderSubjective = () => "It";
			sm.GetCurrentTargetGenderObjective = () => "It";
			sm.GetCurrentTargetGenderPossessive = () => "Its";
			sm.GetCurrentTargetRace = () => "Unknown";
			sm.GetPetName = () => ""; 
		}
		// Wire ActionPanel ability/skill activation signals
		// ActionPanel is now a persistent node handled by ActionPanel.cs


		// Global UI sound effects player (level dings, loot, combat impacts)
		var uiSfx = new UISoundPlayer();
		uiSfx.Name = "UISoundPlayer";
		AddChild(uiSfx);

		Log("SYSTEM", "Initialized EQMUD Client...");
		GD.Print("[UI] MainUI Ready with Inventory & Combat Log enabled!");

		
		// Catch-up on missed signals during scene transition
		if (!string.IsNullOrEmpty(_client.LastStatusPayload))
		{
			// GD.Print("[UI] Caught up on cached character status.");
			OnCharacterStatusReceived(_client.LastStatusPayload);
		}
		if (!string.IsNullOrEmpty(_client.LastSpellbookPayload))
		{
			// GD.Print("[UI] Caught up on cached Spellbook.");
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
			// GD.Print("[UI] Caught up on cached Spellbook Full.");
			OnSpellbookFullReceived(_client.LastSpellbookFullPayload);
		}
		if (!string.IsNullOrEmpty(_client.LastInventoryPayload))
		{
			// GD.Print("[UI] Caught up on cached Inventory.");
			OnInventoryUpdated(_client.LastInventoryPayload);
		}
		if (!string.IsNullOrEmpty(_client.LastZoneStatePayload))
		{
			// GD.Print("[UI] Caught up on cached Zone State.");
			OnZoneStateReceived(_client.LastZoneStatePayload);
		}
		if (!string.IsNullOrEmpty(_client.LastBuffsPayload))
		{
			// GD.Print("[UI] Caught up on cached Buffs.");
			OnBuffsUpdated(_client.LastBuffsPayload);
		}

		CallDeferred(nameof(ReloadUILayout));
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mbR && mbR.ButtonIndex == MouseButton.Right && !mbR.Pressed)
		{
			TryFinishBagRightClickToggle();
		}

		if (@event is InputEventKey k && k.Pressed && !k.Echo)
		{
			// Enter key: toggle chat input focus
			if (k.Keycode == Key.Enter || k.Keycode == Key.KpEnter)
			{
				if (_chatInput != null)
				{
					if (!_chatInputFocused)
					{
						// Check if running forward while opening chat
						if (Input.IsActionPressed("move_forward")) {
							var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
							if (wm != null) wm.SetAutoRun(true);
						}
						
						_chatInput.GrabFocus();
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (string.IsNullOrEmpty(_chatInput.Text))
					{
						// Empty enter = unfocus
						CallDeferred(MethodName.ReleaseChatFocus);
						GetViewport().SetInputAsHandled();
						return;
					}
					// If there's text, TextSubmitted will handle it
				}
			}

			// Escape key: unfocus chat
			if (k.Keycode == Key.Escape && _chatInputFocused)
			{
				ReleaseChatFocus();
				GetViewport().SetInputAsHandled();
				return;
			}

			// Suppress all game hotkeys while chat is focused
			if (_chatInputFocused) return;
			
			if (k.Keycode == Key.H && !k.AltPressed && !k.CtrlPressed && !k.ShiftPressed)
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
			else if (k.Keycode == Key.C && !k.AltPressed && !k.CtrlPressed && !k.ShiftPressed)
			{
				// Key C is Consider
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null && wm.CurrentTargetId != null)
				{
					_client.SendRaw($"{{\"type\": \"CONSIDER\", \"targetId\": \"{wm.CurrentTargetId}\"}}");
				}
			}
			else if (k.Keycode == Key.X && !k.AltPressed && !k.CtrlPressed && !k.ShiftPressed)
			{
				// Key X toggles sit/stand
				_client.SendRaw(_isSitting ? "{\"type\": \"STAND\"}" : "{\"type\": \"SIT\"}");
			}
			else if (k.Keycode == Key.M && !k.AltPressed && !k.CtrlPressed && !k.ShiftPressed)
			{
				// Key M toggles the Map overlay
				if (_mudMap != null)
				{
					_mudMap.Visible = !_mudMap.Visible;
					GD.Print($"[UI] Map toggled: {_mudMap.Visible}");
				}
			}
			else if (k.Keycode == Key.I && !k.AltPressed && !k.CtrlPressed && !k.ShiftPressed)
			{
				// Key I toggles Inventory
				if (_inventoryWindow != null)
				{
					ToggleMainInventoryWindow();
					GD.Print($"[UI] Inventory toggled: {_inventoryWindow.Visible}");
				}
			}
			else if (k.Keycode == Key.B && k.ShiftPressed)
			{
				ToggleAllBags();
			}
			else if (k.Keycode == Key.A && k.AltPressed)
			{
				ToggleActionBarWindow();
			}
			else if (k.Keycode == Key.T && k.AltPressed)
			{
				if (_targetListWindow != null)
				{
					_targetListWindow.Visible = !_targetListWindow.Visible;
					GD.Print($"[UI] Target List toggled: {_targetListWindow.Visible}");
				}
			}
			else if (k.Keycode == Key.U && k.AltPressed)
			{
				if (_targetsTargetWindow != null)
				{
					_targetsTargetEnabled = !_targetsTargetEnabled;
					_targetsTargetWindow.Visible = _targetsTargetEnabled; 
					GD.Print($"[UI] Targets Target master switch: {_targetsTargetEnabled}");
				}
			}
			else if (k.Keycode == Key.G && k.AltPressed)
			{
				if (_groupWindow != null)
				{
					_groupWindow.Visible = !_groupWindow.Visible;
					GD.Print($"[UI] Group Window toggled: {_groupWindow.Visible}");
				}
			}
			else if (k.Keycode == Key.M && k.AltPressed)
			{
				var menuWindow = GetNodeOrNull<Window>("MenuWindow");
				if (menuWindow != null) menuWindow.Visible = !menuWindow.Visible;
			}
			else if (k.Keycode == Key.B && k.AltPressed)
			{
				if (_isSitting) ToggleSpellbook();
				else Log("SYSTEM", "You must be sitting to open your spellbook.");
			}
			else if (k.Keycode == Key.C && k.AltPressed)
			{
				if (_chatWindow != null) _chatWindow.Visible = !_chatWindow.Visible;
			}
			else if (k.Keycode == Key.S && k.AltPressed)
			{
				if (_skillsWindow != null) _skillsWindow.Visible = !_skillsWindow.Visible;
				else Log("SYSTEM", "Skills popup not implemented yet.");
			}
			else if (k.Keycode == Key.O && k.AltPressed)
			{
				ToggleOptionsPanel();
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
				ApplyCombatRegenIcon();
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
			_client.SpellLoadoutsReceived -= OnSpellLoadoutsReceived;
			_client.CombatLogReceived -= OnCombatLogReceived;
			_client.BuffsUpdated -= OnBuffsUpdated;
			_client.InventoryUpdated -= OnInventoryUpdated;
			_client.ZoneStateReceived -= OnZoneStateReceived;
			_client.MobMoveReceived -= OnMobMoveReceived;
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
			_client.CloseUI -= OnCloseUi;
			_client.BankOpened -= OnBankOpened;
			_client.ChatReceived -= OnChatReceived;
			_client.CampComplete -= OnCampComplete;
			_client.SpellAnimationReceived -= OnSpellAnimationReceived;
			_client.ScribeScrollReceived -= OnScribeScrollReceived;
			_client.MemorizeSpellReceived -= OnMemorizeSpellReceived;
			_client.MessageReceived -= OnGenericMessage;
			_client.Connected -= OnClientConnected;
			_client.Disconnected -= OnClientDisconnected;
			_client.AccountOkReceived -= OnAccountOkReceived;
			_client.MercenariesUpdated -= OnMercenariesUpdated;
		}
		base._ExitTree();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
		{
			// Auto-save the character's layout when closing the game
			SaveFullLayout();

			// Clear session-scoped EQ asset cache
			EQAssetCache.Instance.ClearCache();
			GetTree().Quit();
		}
	}

	private void SetCombatRegenIcon(CombatRegenIconKind kind)
	{
		if (_combatStatusIcon == null) return;
		Texture2D tex = kind switch
		{
			CombatRegenIconKind.Combat => _texRegenCombat,
			CombatRegenIconKind.Cooldown => _texRegenCooldown,
			CombatRegenIconKind.Idle => _texRegenIdle ?? _texRegenCooldown,
			CombatRegenIconKind.Resting => _texRegenResting,
			_ => _texRegenCooldown
		};
		if (tex != null)
			_combatStatusIcon.Texture = tex;
		_combatStatusIcon.TooltipText = kind switch
		{
			CombatRegenIconKind.Combat => "In combat — standard regeneration.",
			CombatRegenIconKind.Cooldown => "Recovering — post-combat delay before elevated regen.",
			CombatRegenIconKind.Idle => "Idle — elevated out-of-combat regen (standing).",
			CombatRegenIconKind.Resting => "Resting — elevated out-of-combat regen (sitting).",
			_ => ""
		};
	}

	/// <summary>
	/// Stores server <c>inCombat</c> and <c>restedRegen</c>, then updates the status icon (Idle standing vs Resting sitting when regen gate is on).
	/// </summary>
	private void UpdateCombatRegenStatusFromServer(bool inCombat, bool restedRegen)
	{
		_regenStatusInCombat = inCombat;
		_regenStatusRestedRegen = restedRegen;
		ApplyCombatRegenIcon();
	}

	/// <summary>Re-applies regen icon from cached server flags and current <see cref="_isSitting"/> (e.g. after optimistic stand).</summary>
	private void ApplyCombatRegenIcon()
	{
		if (_combatStatusIcon == null) return;

		if (_regenStatusInCombat)
			SetCombatRegenIcon(CombatRegenIconKind.Combat);
		else if (!_regenStatusRestedRegen)
			SetCombatRegenIcon(CombatRegenIconKind.Cooldown);
		else if (_isSitting)
			SetCombatRegenIcon(CombatRegenIconKind.Resting);
		else
			SetCombatRegenIcon(CombatRegenIconKind.Idle);
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
					&& !_isCasting
					&& !_isSinging;
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

		if (_isScribing)
		{
			_scribeTimeElapsed += dt;
			if (_scribeTimeTotal > 0)
				_scribeBar.Value = Math.Min(100.0, (_scribeTimeElapsed / _scribeTimeTotal) * 100.0);
			float rem = Math.Max(0, _scribeTimeTotal - _scribeTimeElapsed);
			_scribeBarLabel.Text = $"Scribing: {_scribingSpellName} ({rem:F1}s)";
		}
		else if (_isMemorizing)
		{
			// Reuses the scribe progress bar — scribe and memorize both require sitting
			// and cannot happen simultaneously. Server-authoritative: MEMORIZE_COMPLETE
			// or MEMORIZE_CANCELLED clears _isMemorizing.
			_memorizeTimeElapsed += dt;
			if (_memorizeTimeTotal > 0)
				_scribeBar.Value = Math.Min(100.0, (_memorizeTimeElapsed / _memorizeTimeTotal) * 100.0);
			float rem = Math.Max(0, _memorizeTimeTotal - _memorizeTimeElapsed);
			_scribeBarLabel.Text = $"Memorizing: {_memorizingSpellName} ({rem:F1}s)";
		}

		// Tick buff durations
		var allBuffs = new List<ActiveBuff>(_activeBuffs);
		allBuffs.AddRange(_activeSongBuffs);
		
		foreach (var buff in allBuffs)
		{
			buff.DurationRemaining -= dt;
			if (buff.DurationRemaining < 0) buff.DurationRemaining = 0;

			if (buff.IconNode != null && GodotObject.IsInstanceValid(buff.IconNode))
			{
				var durationBar = buff.IconNode.GetNodeOrNull<ProgressBar>("Duration");
				if (durationBar != null)
				{
					if (buff.DurationMax > 0)
						durationBar.Value = (buff.DurationRemaining / buff.DurationMax) * 100.0;
					else
						durationBar.Value = 100.0;
				}
			}
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

		if (_cursorIcon != null && _cursorIcon.Visible) {
			_cursorIcon.GlobalPosition = GetGlobalMousePosition() + new Vector2(12, 12);
		}
		if (_actionCursorLabel != null && _actionCursorLabel.Visible) {
			_actionCursorLabel.GlobalPosition = GetGlobalMousePosition() + new Vector2(12, 12);
		}

		// ── Right-click hold timer for item detail popup ──
		if (_rightClickTimer >= 0) {
			_rightClickTimer += delta;
			if (_rightClickTimer >= 1.0 && _rightClickItemData.HasValue) {
				ShowItemDetail(_rightClickItemData.Value, GetGlobalMousePosition());
				_rightClickTimer = -1;
				_rightClickTarget = null;
				_rightClickItemData = null;
				_rightClickSlotId = -1;
			}
		}

		// ── Escape to cancel held item or hide popups ──
		if (Input.IsActionJustPressed("ui_cancel")) {
			if (_heldItem.HasValue) {
				PlayInvPutDownSound(_heldItem.Value);
				CancelHeldItem();
			}
			if (_itemDetailPopup.Visible) _itemDetailPopup.Visible = false;
			if (_spellDetailPopup.Visible) _spellDetailPopup.Visible = false;
		}

	}

	private void OnSpellDetailsReceived(Variant data)
	{
		string json = data.AsString();
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		if (root.TryGetProperty("spell", out var spellJson))
		{
			// Try to position near mouse, clamped to screen
			var pos = GetGlobalMousePosition();
			var viewportSize = GetViewportRect().Size;
			if (pos.X + 300 > viewportSize.X) pos.X = viewportSize.X - 300;
			if (pos.Y + 240 > viewportSize.Y) pos.Y = viewportSize.Y - 240;
			_spellDetailPopup.ShowSpell(spellJson, pos);
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



	/// <summary>Parse JSON number or numeric string (spell data sometimes serializes animType as a string).</summary>
	private static int GetJsonInt32(JsonElement el, int defaultValue)
	{
		return el.ValueKind switch
		{
			JsonValueKind.Number => el.TryGetInt32(out int i) ? i : defaultValue,
			JsonValueKind.String => int.TryParse(el.GetString(), out int j) ? j : defaultValue,
			_ => defaultValue
		};
	}

	private static float GetJsonSingle(JsonElement el, float defaultValue)
	{
		return el.ValueKind switch
		{
			JsonValueKind.Number => (float)el.GetDouble(),
			JsonValueKind.String => float.TryParse(el.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : defaultValue,
			_ => defaultValue
		};
	}

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
				case "GM_INVENTORY_VIEW":
					HandleGmInventoryViewMessage(json);
					break;
				case "MOB_VISUAL_UPDATE":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					string id = root.GetProperty("id").GetString();
					string equipVis = root.TryGetProperty("equipVisuals", out var evProp) && evProp.ValueKind != System.Text.Json.JsonValueKind.Null ? evProp.GetRawText() : "";
					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null)
					{
 					wm.UpdateEntityEquipVisuals(id, equipVis);
					}
					break;
				}
				case "SONG_ACTIVE":
				{
					GD.Print($"[UI] Received SONG_ACTIVE: {json}");
					_isSinging = true;
					break;
				}
				case "SONG_STOP":
				{
					GD.Print($"[UI] Received SONG_STOP");
					_isSinging = false;
					break;
				}
				case "CAST_START":
				{
					GD.Print($"[UI] Received CAST_START: {json}");
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					
					string casterId = root.TryGetProperty("casterId", out var cp) ? cp.GetString() : "player_self";
					string selfPlayerId = $"player_{GameClient.Instance.CharacterId}";
					bool isSelf = casterId == "player_self" || casterId == selfPlayerId;

					string spellName = root.TryGetProperty("spellName", out var n) ? n.GetString() : "Casting...";
					float castTime = root.TryGetProperty("castTime", out var ct) ? GetJsonSingle(ct, 1.5f) : 1.5f;

					if (isSelf)
					{
						_castingSpellName = spellName;
						_castTimeTotal = castTime;
						_castTimeElapsed = 0;
						_isCasting = true;
						_castBar.Value = 0;
						_castBarLabel.Text = $"{_castingSpellName} ({_castTimeTotal:F1}s)";
						_castBarPanel.Show();
						_castBarPanel.MoveToFront();
					}

					// Trigger casting animation on 3D model (for self or remote)
					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null) 
					{
						int animType = root.TryGetProperty("animType", out var at) ? GetJsonInt32(at, 44) : 44;
						int castType = animType switch
						{
							42 => 1, // Heal/Regen -> Cast 1
							43 => 2, // Buff/Enchant -> Cast 2
							44 => 3, // Nuke/Summon -> Cast 3
							_ => 1   // Default
						};
						
						if (isSelf)
						{
   				wm.SetPlayerCastingAnimation(true);
							wm.TriggerEntityAction("You", $"cast:{castType}");
						}
						else
						{
							var cap = wm.GetEntityById(casterId);
							if (cap != null)
							{
								cap.IsCasting = true;
								wm.TriggerEntityActionById(casterId, $"cast:{castType}");
							}
						}
					}
					break;
				}
				case "CAST_COMPLETE":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					string casterId = root.TryGetProperty("casterId", out var cp) ? cp.GetString() : "player_self";
					string selfPlayerId = $"player_{GameClient.Instance.CharacterId}";
					bool isSelf = casterId == "player_self" || casterId == selfPlayerId;

					if (isSelf)
					{
						_isCasting = false;
						_castBar.Value = 100;
						_castBarPanel.Hide();
					}

					// Stop casting animation
					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null)
					{
						if (isSelf)
  					wm.SetPlayerCastingAnimation(false);
						else
							wm.StopEntityCasting(casterId);
					}
					break;
				}
				case "CAST_INTERRUPTED":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					string casterId = root.TryGetProperty("casterId", out var cp) ? cp.GetString() : "player_self";
					string selfPlayerId = $"player_{GameClient.Instance.CharacterId}";
					bool isSelf = casterId == "player_self" || casterId == selfPlayerId;

					if (isSelf)
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
					}

					// Stop casting animation
					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null)
					{
						if (isSelf)
  					wm.SetPlayerCastingAnimation(false);
						else
							wm.StopEntityCasting(casterId);
					}
					break;
				}
				case "EMOTE":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					string charName = root.TryGetProperty("charName", out var cn) ? cn.GetString() : "Someone";
					string emote = root.TryGetProperty("emote", out var em) ? em.GetString() : "";
					string anim = root.TryGetProperty("anim", out var an) && an.ValueKind == JsonValueKind.String ? an.GetString() : null;

					// Determine if this is a casting animation (t04/t05/t06)
					bool isCastEmote = emote == "t04" || emote == "t05" || emote == "t06";

					// Display emote text (suppress raw anim codes)
					if (!isCastEmote)
						Log("EMOTE", $"{charName} {emote}s.");
					else
						Log("EMOTE", $"{charName} begins to cast a spell.");

					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null)
					{
						var entity = wm.GetEntityByName(charName);
						if (entity != null)
						{
							if (root.TryGetProperty("heading", out var hp) && hp.TryGetDouble(out double val))
							{
								float godotYaw = ((float)val / 512f) * 360f;
								entity.TargetYaw = godotYaw;
							}
							
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
				case "NPC_ANIM":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					string npcId = "";
					if (root.TryGetProperty("id", out var idProp))
					{
						npcId = idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt32().ToString() : idProp.GetString();
					}
					string animCode = null;
					if (root.TryGetProperty("anim", out var animProp))
					{
						if (animProp.ValueKind == JsonValueKind.String)
						{
							animCode = animProp.GetString();
						}
						else if (animProp.ValueKind == JsonValueKind.Number && animProp.TryGetInt32(out int animId))
						{
							animCode = animId switch
							{
								1 => "c01",
								2 => "c02",
								3 => "c03",
								4 => "c04",
								5 => "c05",
								6 => "c06",
								7 => "c07",
								8 => "c08",
								9 => "c09",
								10 => "c10",
								11 => "c11",
								12 => "d01",
								13 => "d02",
								14 => "d04",
								15 => "d05",
								16 => "l01",
								17 => "l02",
								18 => "l03",
								19 => "l04",
								20 => "l05",
								21 => "l06",
								22 => "l07",
								23 => "l08",
								24 => "l09",
								25 => "o01",
								26 => "p01",
								27 => "p02",
								28 => "p03",
								29 => "s03",
								30 => "p05",
								31 => "p06",
								35 => "s01",
								36 => "s02",
								38 => "s04",
								42 => "t04",
								43 => "t05",
								44 => "t06",
								45 => "t02",
								46 => "t03",
								47 => "t07",
								48 => "t08",
								49 => "t09",
								_ => null
							};
						}
					}

					if (!string.IsNullOrEmpty(npcId) && !string.IsNullOrEmpty(animCode))
					{
						var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
						if (wm != null)
						{
							var entity = wm.GetEntityById(npcId);
							if (entity != null)
								entity.PlayEmote(animCode);
						}
					}
					break;
				}
				case "BIND_SIGHT":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					bool active = root.TryGetProperty("active", out var aProp) && aProp.GetBoolean();
					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null)
					{
						if (active)
						{
							string targetName = root.TryGetProperty("targetName", out var tnProp) ? tnProp.GetString() : "unknown";
							float bsX = root.TryGetProperty("x", out var xp) ? (float)xp.GetDouble() : 0;
							float bsY = root.TryGetProperty("y", out var yp) ? (float)yp.GetDouble() : 0;
							float bsZ = root.TryGetProperty("z", out var zp) ? (float)zp.GetDouble() : 0;
							// Convert EQ coords to Godot coords: Godot.X = -EQ.X, Godot.Y = EQ.Z, Godot.Z = -EQ.Y
							wm.SetBindSightPosition(-bsX, bsZ + 5f, -bsY); // +5 for eye height
							Log("SYSTEM", $"[color=cyan]You see through {targetName}'s eyes.[/color]");
						}
						else
						{
							wm.ClearBindSight();
							Log("SYSTEM", "[color=cyan]Your vision returns to normal.[/color]");
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
				case "despawn":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					string entityId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
					if (!string.IsNullOrEmpty(entityId))
					{
						var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
						if (wm != null) wm.RemoveEntity(entityId);
					}
					break;
				}
				case "spawn":
				{
					// If the entity wasn't in ZONE_STATE, the server sends a generic "spawn".
					// Currently, gameEngine.js sends full entity state in "spawn"
					using var doc = JsonDocument.Parse(json);
					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null) {
						var singleEntityArrayDoc = JsonDocument.Parse("{\"entities\":[" + json + "]}");
						var entitiesArray = singleEntityArrayDoc.RootElement.GetProperty("entities");
						wm.SyncLiveMobs(entitiesArray);
					}
					break;
				}
				case "TARGET_UPDATE":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					if (root.TryGetProperty("target", out var tgt))
					{
						string tName = tgt.TryGetProperty("name", out var tn) && tn.ValueKind != JsonValueKind.Null ? tn.GetString() : "Unknown";
						int tHp = tgt.TryGetProperty("hp", out var th) && th.ValueKind != JsonValueKind.Null ? th.GetInt32() : 0;
						int tMaxHp = tgt.TryGetProperty("maxHp", out var tmh) && tmh.ValueKind != JsonValueKind.Null ? tmh.GetInt32() : 1;
						int tLevel = tgt.TryGetProperty("level", out var tl) && tl.ValueKind != JsonValueKind.Null ? tl.GetInt32() : 0;
						string tType = tgt.TryGetProperty("type", out var tt) && tt.ValueKind != JsonValueKind.Null ? tt.GetString() : "";

						_targetWindow.Visible = true;
						_targetNameLabel.Text = tType == "mining_node"
							? $"{tName} (T{tLevel})"
							: (tType == "world_object" ? tName : $"{tName} (Lv {tLevel})");
						if (tType == "world_object")
						{
							_targetHpBar.MaxValue = 1;
							_targetHpBar.Value = 1;
							_targetHpLabel.Text = "100%";
						}
						else
						{
							_targetHpBar.MaxValue = tMaxHp;
							_targetHpBar.Value = Math.Max(0, tHp);
							double pct = tMaxHp > 0 ? ((double)tHp / tMaxHp * 100) : 100;
							_targetHpLabel.Text = $"{pct:F0}%";
						}
						
						// --- Target Buffs ---
						if (_isSelfTargeted)
						{
							RenderBuffsToContainer(_targetBuffBar, _activeBuffs);
						}
						else if (tgt.TryGetProperty("buffs", out var buffsProp))
						{
							_activeTargetBuffs.Clear();
							foreach (var buff in buffsProp.EnumerateArray())
							{
								string bName = buff.GetProperty("name").GetString();
								float bDuration = buff.TryGetProperty("duration", out var bDurProp) ? (float)bDurProp.GetDouble() : 0f;
								float bMaxDuration = buff.TryGetProperty("maxDuration", out var bMaxDurProp) ? (float)bMaxDurProp.GetDouble() : bDuration;
								bool bBeneficial = buff.TryGetProperty("beneficial", out var bBenProp) ? bBenProp.GetBoolean() : true;
								int bMemIcon = buff.TryGetProperty("memIcon", out var bMemProp) ? bMemProp.GetInt32() : 0;
								int bSpellIcon = buff.TryGetProperty("icon", out var bIconProp) ? bIconProp.GetInt32() : 0;

								_activeTargetBuffs.Add(new ActiveBuff
								{
									Name = bName,
									DurationMax = bMaxDuration,
									DurationRemaining = bDuration,
									IsBeneficial = bBeneficial,
									MemIcon = bMemIcon,
									SpellIcon = bSpellIcon
								});
							}
							RenderBuffsToContainer(_targetBuffBar, _activeTargetBuffs);
						}

						// --- Target's Target Logic ---
						if (_targetsTargetEnabled && tgt.TryGetProperty("targetTarget", out var ttProp) && ttProp.ValueKind != JsonValueKind.Null)
						{
							_targetsTargetWindow.Visible = true;
							string ttName = ttProp.TryGetProperty("name", out var ttn) && ttn.ValueKind != JsonValueKind.Null ? ttn.GetString() : "Unknown";
							int ttHp = ttProp.TryGetProperty("hp", out var tth) && tth.ValueKind != JsonValueKind.Null ? tth.GetInt32() : 0;
							int ttMaxHp = ttProp.TryGetProperty("maxHp", out var ttmh) && ttmh.ValueKind != JsonValueKind.Null ? ttmh.GetInt32() : 1;
							
							_targetsTargetNameLabel.Text = ttName;
							_targetsTargetHpBar.MaxValue = ttMaxHp;
							_targetsTargetHpBar.Value = ttHp;
							
							double ttPct = ttMaxHp > 0 ? ((double)ttHp / ttMaxHp * 100) : 100;
							_targetsTargetHpLabel.Text = $"{ttPct:F0}%";
						}
						else
						{
							_targetsTargetWindow.Visible = false;
						}
						
						_groupWindow?.OnTargetChanged(tName, tType);
					}
					break;
				}
				case "GROUP_UPDATE":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					if (root.TryGetProperty("members", out var members))
					{
						root.TryGetProperty("roles", out var roles); // It's okay if this is undefined, UpdateGroup should handle it
						_groupWindow?.UpdateGroup(members, roles);
					}
					break;
				}
				case "GROUP_INVITE":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					string inviter = root.GetProperty("inviterName").GetString();
					
					// Lazy instance the popup
					if (_invitePopup == null)
					{
						_invitePopup = _invitePopupScene.Instantiate<ConfirmationDialog>();
						AddChild(_invitePopup);
						_invitePopup.Confirmed += () => SendNetworkMessage("GROUP_INVITE_RESPONSE", new Dictionary<string, object> { { "accepted", true } });
						_invitePopup.Canceled += () => SendNetworkMessage("GROUP_INVITE_RESPONSE", new Dictionary<string, object> { { "accepted", false } });
					}
					
					_invitePopup.DialogText = $"{inviter} has invited you to join their group.";
					_invitePopup.PopupCentered();
					break;
				}
				case "SET_TARGET":
				{
					// Handled by the server sending a TARGET_UPDATE shortly after, 
					// but we can log it here if needed.
					break;
				}
				case "SKILLS_UPDATE":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					if (root.TryGetProperty("skills", out var skillsProp) && _skillsWindow != null)
					{
						_skillsWindow.UpdateSkills(skillsProp, _charLevel);
					}
					break;
				}
				case "ITEM_DETAILS":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					if (root.TryGetProperty("item", out var itemJson))
					{
						var pos = GetGlobalMousePosition();
						var viewportSize = GetViewportRect().Size;
						if (pos.X + 300 > viewportSize.X) pos.X = viewportSize.X - 300;
						if (pos.Y + 400 > viewportSize.Y) pos.Y = viewportSize.Y - 400;
						_itemDetailPopup.ShowItem(itemJson, pos);
					}
					break;
				}
				case "OPEN_BANK":
				{
					if (_bankWindow == null || !IsInstanceValid(_bankWindow))
					{
						_bankWindow = new BankWindow();
						AddChild(_bankWindow);
					}
					_bankWindow.Show();
					_bankWindow.UpdateMoneyDisplay(_copper);
					// Request an inventory sync so bank slots populate
					_client.SendRaw("{\"type\": \"GET_INVENTORY\"}");
					break;
				}
				case "TRACKING_LIST":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					if (root.TryGetProperty("targets", out var targetsProp))
					{
						if (_trackingWindow == null || !IsInstanceValid(_trackingWindow))
						{
							_trackingWindow = new TrackingWindow();
							AddChild(_trackingWindow);
						}
						_trackingWindow.UpdateList(targetsProp);
 					_trackingWindow.Show();
					}
					break;
				}
				case "LOOT_CORPSE_OPEN":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					
					if (root.TryGetProperty("corpseId", out var cIdProp) && 
						root.TryGetProperty("corpseName", out var cNameProp) &&
						root.TryGetProperty("items", out var itemsProp))
					{
						string cId = cIdProp.GetString();
						string cName = cNameProp.GetString();
						var items = itemsProp.Deserialize<List<LootItemData>>();

						// Always create a fresh window: Init() builds the full node tree and must not run twice on the same instance.
						if (_lootWindow != null && IsInstanceValid(_lootWindow))
						{
							_lootWindow.QueueFree();
							_lootWindow = null;
						}
						_lootWindow = new LootWindow();
						AddChild(_lootWindow);
						_lootWindow.Init(cId, cName, items);
						_lootWindow.Show();
					}
					break;
				}
				case "LOOT_CORPSE_UPDATE":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					if (root.TryGetProperty("items", out var itemsProp))
					{
						var items = itemsProp.Deserialize<List<LootItemData>>();
						if (_lootWindow != null && IsInstanceValid(_lootWindow))
						{
							_lootWindow.UpdateItems(items);
						}
					}
					break;
				}
				case "LOOT_COIN":
				{
					// Play coin sound? 
					break;
				}
				case "MESSAGE":
				{
					using var doc = JsonDocument.Parse(json);
					var root = doc.RootElement;
					if (root.TryGetProperty("text", out var tp))
						Log("SYSTEM", tp.GetString());
					break;
				}
			}
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Message handler error ({type}): {ex.Message}"); }
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
		if (_optionsPanel.Visible && _optDragItemPathEdit != null)
		{
			ClientLocalSettings.Load();
			_optDragItemPathEdit.Text = ClientLocalSettings.DragItemIconsFolder;
		}
	}

	private void ApplyDragItemIconsFolder()
	{
		if (_optDragItemPathEdit == null) return;
		ClientLocalSettings.SaveDragItemIconsFolder(_optDragItemPathEdit.Text);
		IconManager.Instance?.InvalidateItemIconCaches();
		Log("SYSTEM", "[color=gray]Item icon folder saved. UI will reload icons from the new dragitem sheets.[/color]");
	}

	private void ToggleAudioPlayer()
	{
		if (_audioPlayerWindow == null)
		{
			_audioPlayerWindow = new AudioPlayerWindow();
			_audioPlayerWindow.Name = "AudioPlayerWindow";

			// Center on screen
			var screenSize = GetViewport().GetVisibleRect().Size;
			_audioPlayerWindow.GlobalPosition = new Vector2(
				(screenSize.X - 280) / 2f,
				(screenSize.Y - 310) / 2f
			);

			// Find the music player from WorldManager
			var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
			_audioPlayerWindow.Initialize(wm?.MusicPlayer);

			AddChild(_audioPlayerWindow);
		}

		_audioPlayerWindow.Visible = !_audioPlayerWindow.Visible;
	}

	public void TakeLootItem(string corpseId, int lootIndex)
	{
		SendNetworkMessage("TAKE_LOOT_ITEM", new Dictionary<string, object>
		{
			{ "corpseId", corpseId },
			{ "lootIndex", lootIndex }
		});
	}

	private void BuildOptionsPanel()
	{
		_optionsPanel = new Panel();
		_optionsPanel.CustomMinimumSize = new Vector2(320, 500);
		_optionsPanel.Size = new Vector2(320, 500);

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

		// ── EverQuest dragitem sheets (all item icons: inventory, loot, merchants, cursor, …) ──
		vbox.AddChild(new HSeparator());
		var dragHint = new Label();
		dragHint.Text = "Item icons: folder with dragitem1.dds through dragitem178.dds (your EQ client’s uifiles/default). Leave empty to use built-in copies under res://Assets/UI/ClassicUI/.";
		dragHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		dragHint.AddThemeFontSizeOverride("font_size", 11);
		dragHint.AddThemeColorOverride("font_color", new Color(0.75f, 0.72f, 0.6f));
		vbox.AddChild(dragHint);

		ClientLocalSettings.Load();
		var dragRow = new HBoxContainer();
		dragRow.AddThemeConstantOverride("separation", 6);
		_optDragItemPathEdit = new LineEdit();
		_optDragItemPathEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optDragItemPathEdit.Text = ClientLocalSettings.DragItemIconsFolder;
		_optDragItemPathEdit.PlaceholderText = @"D:/everquest_rof2/everquest_rof2/uifiles/default";
		_optDragItemPathEdit.AddThemeFontSizeOverride("font_size", 11);
		var dragBrowse = new Button { Text = "Browse…" };
		dragBrowse.Pressed += () => _optDragItemFolderDialog?.PopupCentered(new Vector2I(800, 480));
		var dragApply = new Button { Text = "Apply" };
		dragApply.Pressed += ApplyDragItemIconsFolder;
		dragRow.AddChild(_optDragItemPathEdit);
		dragRow.AddChild(dragBrowse);
		dragRow.AddChild(dragApply);
		vbox.AddChild(dragRow);

		_optDragItemFolderDialog = new FileDialog();
		_optDragItemFolderDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
		_optDragItemFolderDialog.Access = FileDialog.AccessEnum.Filesystem;
		_optDragItemFolderDialog.Title = "Select folder containing dragitem1.dds";
		_optDragItemFolderDialog.DirSelected += (dir) => { if (_optDragItemPathEdit != null) _optDragItemPathEdit.Text = dir; };
		AddChild(_optDragItemFolderDialog);

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

		// Separator
		vbox.AddChild(new HSeparator());

		// Fullscreen Checkbox
		var fullCheck = new CheckBox();
		fullCheck.Text = "Fullscreen Mode";
		fullCheck.ButtonPressed = _fullscreen;
		fullCheck.AddThemeFontSizeOverride("font_size", 12);
		fullCheck.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		fullCheck.Toggled += (toggled) =>
		{
			_fullscreen = toggled;
			DisplayServer.WindowSetMode(_fullscreen ? DisplayServer.WindowMode.ExclusiveFullscreen : DisplayServer.WindowMode.Windowed);
		};
		vbox.AddChild(fullCheck);

		// V-Sync Checkbox
		var vsyncCheck = new CheckBox();
		vsyncCheck.Text = "Vertical Sync";
		vsyncCheck.ButtonPressed = _vSync;
		vsyncCheck.AddThemeFontSizeOverride("font_size", 12);
		vsyncCheck.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		vsyncCheck.Toggled += (toggled) =>
		{
			_vSync = toggled;
			DisplayServer.WindowSetVsyncMode(_vSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
		};
		vbox.AddChild(vsyncCheck);

		// FPS Limit Dropdown
		var fpsBox = new HBoxContainer();
		var fpsLabel = new Label { Text = "FPS Limit:", CustomMinimumSize = new Vector2(100, 0) };
		fpsLabel.AddThemeFontSizeOverride("font_size", 12);
		fpsLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		var fpsOpt = new OptionButton();
		fpsOpt.AddItem("Unlimited", 0);
		fpsOpt.AddItem("60 FPS", 1);
		fpsOpt.AddItem("120 FPS", 2);
		fpsOpt.AddItem("144 FPS", 3);
		
		int fpsSelectedIdx = _maxFps switch { 60 => 1, 120 => 2, 144 => 3, _ => 0 };
		fpsOpt.Selected = fpsSelectedIdx;
		fpsOpt.ItemSelected += (idx) =>
		{
			_maxFps = (int)idx switch {
				1 => 60,
				2 => 120,
				3 => 144,
				_ => 0
			};
			Engine.MaxFps = _maxFps;
		};
		fpsBox.AddChild(fpsLabel);
		fpsBox.AddChild(fpsOpt);
		vbox.AddChild(fpsBox);

		// MSAA Dropdown
		var msaaBox = new HBoxContainer();
		var msaaLabel = new Label { Text = "Anti-Aliasing:", CustomMinimumSize = new Vector2(100, 0) };
		msaaLabel.AddThemeFontSizeOverride("font_size", 12);
		msaaLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		var msaaOpt = new OptionButton();
		msaaOpt.AddItem("Off", 0);
		msaaOpt.AddItem("2x MSAA", 1);
		msaaOpt.AddItem("4x MSAA", 2);
		msaaOpt.AddItem("8x MSAA", 3);
		msaaOpt.Selected = _antiAliasing;
		msaaOpt.ItemSelected += (idx) =>
		{
			_antiAliasing = (int)idx;
			GetViewport().Msaa3D = _antiAliasing switch {
				1 => Viewport.Msaa.Msaa2X,
				2 => Viewport.Msaa.Msaa4X,
				3 => Viewport.Msaa.Msaa8X,
				_ => Viewport.Msaa.Disabled
			};
		};
		msaaBox.AddChild(msaaLabel);
		msaaBox.AddChild(msaaOpt);
		vbox.AddChild(msaaBox);

		// Field of View Slider
		var fovBox = new VBoxContainer();
		var fovLabelBox = new HBoxContainer();
		var fovLabel = new Label { Text = "Field of View" };
		fovLabel.AddThemeFontSizeOverride("font_size", 12);
		fovLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
		var fovValue = new Label { Text = $"{_cameraFov:F0}°" };
		fovValue.AddThemeFontSizeOverride("font_size", 12);
		fovValue.AddThemeColorOverride("font_color", Colors.White);
		fovValue.SizeFlagsHorizontal = Control.SizeFlags.Expand | Control.SizeFlags.ShrinkEnd;
		fovLabelBox.AddChild(fovLabel);
		fovLabelBox.AddChild(fovValue);
		fovBox.AddChild(fovLabelBox);
		
		var fovSlider = new HSlider();
		fovSlider.MinValue = 60;
		fovSlider.MaxValue = 110;
		fovSlider.Value = _cameraFov;
		fovSlider.Step = 1;
		fovSlider.ValueChanged += (val) =>
		{
			_cameraFov = (float)val;
			fovValue.Text = $"{_cameraFov:F0}°";
			var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
			if (wm != null) wm.SetCameraFOV(_cameraFov);
		};
		fovBox.AddChild(fovSlider);
		vbox.AddChild(fovBox);

		// Separator
		vbox.AddChild(new HSeparator());

		// Copy Loadout Button
		var copyBtn = new Button();
		copyBtn.Text = "Copy Loadout";
		copyBtn.CustomMinimumSize = new Vector2(0, 24);
		copyBtn.Pressed += () => {
			if (_copyLayoutWindow == null) {
				_copyLayoutWindow = new CopyLayoutWindow();
				AddChild(_copyLayoutWindow);
			}
			_copyLayoutWindow.Visible = !_copyLayoutWindow.Visible;
		};
		vbox.AddChild(copyBtn);

		// Audio Player Button
		var audioBtn = new Button();
		audioBtn.Text = "Audio Player";
		audioBtn.CustomMinimumSize = new Vector2(0, 24);
		audioBtn.Pressed += () => ToggleAudioPlayer();
		vbox.AddChild(audioBtn);

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

			// Save on drag end
			if (ev is InputEventMouseButton mbEnd && mbEnd.ButtonIndex == MouseButton.Left && !mbEnd.Pressed)
			{
				SaveWindowPositions();
			}
		};

		AddChild(_optionsPanel);
		ApplyWindowPos(_optionsPanel, "OptionsPanel", UILayoutManager.GetSection("Windows"));
	}

	public void ReloadUILayout()
	{
		var video = UILayoutManager.GetSection("Video");
		if (video.TryGetValue("FOV", out Variant fov)) _cameraFov = fov.AsSingle();
		if (video.TryGetValue("MSAA", out Variant msaa)) _antiAliasing = msaa.AsInt32();
		if (video.TryGetValue("VSync", out Variant vsync)) _vSync = vsync.AsBool();
		if (video.TryGetValue("Fullscreen", out Variant full)) _fullscreen = full.AsBool();
		if (video.TryGetValue("MaxFPS", out Variant maxFps)) _maxFps = maxFps.AsInt32();
		
		// Apply immediately
		var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
		if (wm != null) wm.SetCameraFOV(_cameraFov);
		
		GetViewport().Msaa3D = _antiAliasing switch {
			1 => Viewport.Msaa.Msaa2X,
			2 => Viewport.Msaa.Msaa4X,
			3 => Viewport.Msaa.Msaa8X,
			_ => Viewport.Msaa.Disabled
		};
		DisplayServer.WindowSetMode(_fullscreen ? DisplayServer.WindowMode.ExclusiveFullscreen : DisplayServer.WindowMode.Windowed);
		DisplayServer.WindowSetVsyncMode(_vSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
		Engine.MaxFps = _maxFps;

		var windows = UILayoutManager.GetSection("Windows");
		if (_targetWindow != null) ApplyWindowPos(_targetWindow, "TargetWindow", windows);
		if (_targetListWindow != null) ApplyWindowPos(_targetListWindow, "TargetListWindow", windows);
		if (_targetsTargetWindow != null) ApplyWindowPos(_targetsTargetWindow, "TargetsTargetWindow", windows);
		if (_groupWindow != null) ApplyWindowPos(_groupWindow, "GroupWindow", windows);
		if (_melodyWindow != null) ApplyWindowPos(_melodyWindow, "MelodyWindow", windows);
		ApplyWindowPos(_castBarPanel, "CastBar", windows);
		if (_optionsPanel != null) ApplyWindowPos(_optionsPanel, "OptionsPanel", windows);
		if (_actionBarWindow != null) ApplyWindowPos(_actionBarWindow, "ActionBarWindow", windows);
		if (_inventoryWindow != null) ApplyWindowPos(_inventoryWindow, "InventoryWindow", windows);
		if (_chatWindow != null) ApplyWindowPos(_chatWindow, "ChatWindow", windows);
		if (_skillsWindow != null) ApplyWindowPos(_skillsWindow, "SkillsWindow", windows);
		
		var menuWindow = GetNodeOrNull<Window>("MenuWindow");
		if (menuWindow != null) ApplyWindowPos(menuWindow, "MenuWindow", windows);
		
		var spellsWindow = GetNodeOrNull<Window>("Spellbook");
		if (spellsWindow != null) ApplyWindowPos(spellsWindow, "Spellbook", windows);
		
		var spellBarWindow = GetNodeOrNull<Window>("SpellBarWindow");
		if (spellBarWindow != null) ApplyWindowPos(spellBarWindow, "SpellBarWindow", windows);
		
		var partyWindow = GetNodeOrNull<Window>("PartyWindow");
		if (partyWindow != null) ApplyWindowPos(partyWindow, "PartyWindow", windows);
		
		var statusWindow = GetNodeOrNull<Window>("StatusWindow");
		if (statusWindow != null) ApplyWindowPos(statusWindow, "StatusWindow", windows);
		
		if (_buffBarWindow != null) ApplyWindowPos(_buffBarWindow, "BuffWindow", windows);
		if (_songBarWindow != null) ApplyWindowPos(_songBarWindow, "SongWindow", windows);
		if (_copyLayoutWindow != null) ApplyWindowPos(_copyLayoutWindow, "CopyLayoutWindow", windows);
		if (_merchantWindow != null) ApplyWindowPos(_merchantWindow, "MerchantWindow", windows);
	}

	private void ApplyWindowPos(Node panel, string key, Godot.Collections.Dictionary windows)
	{
		if (panel == null) return;
		if (windows.TryGetValue(key, out Variant val))
		{
			var d = val.AsGodotDictionary();
			Vector2 viewportSize = GetViewportRect().Size;

			if (panel is Control c) {
				float x = d["x"].AsSingle();
				float y = d["y"].AsSingle();
				float w = d.ContainsKey("w") ? d["w"].AsSingle() : c.Size.X;
				float h = d.ContainsKey("h") ? d["h"].AsSingle() : c.Size.Y;

				// Clamp to viewport
				x = Mathf.Clamp(x, 0, Math.Max(0, viewportSize.X - w));
				y = Mathf.Clamp(y, 0, Math.Max(0, viewportSize.Y - h));

				c.GlobalPosition = new Vector2(x, y);
				if (d.ContainsKey("w") && d.ContainsKey("h")) {
					c.Size = new Vector2(w, h);
				}
			} else if (panel is Window w) {
				int x = d["x"].AsInt32();
				int y = d["y"].AsInt32();
				int winW = d.ContainsKey("w") ? d["w"].AsInt32() : w.Size.X;
				int winH = d.ContainsKey("h") ? d["h"].AsInt32() : w.Size.Y;

				// Clamp to viewport
				x = Mathf.Clamp(x, 0, Math.Max(0, (int)viewportSize.X - winW));
				y = Mathf.Clamp(y, 30, Math.Max(30, (int)viewportSize.Y - winH)); // 30 offset for titlebars

				w.Position = new Vector2I(x, y);
				if (d.ContainsKey("w") && d.ContainsKey("h")) {
					w.Size = new Vector2I(winW, winH);
				}
			}
		}
	}

	public void SaveWindowPositions()
	{
		var windows = UILayoutManager.GetSection("Windows");
		StoreWindowPos(_targetWindow, "TargetWindow", windows);
		StoreWindowPos(_targetListWindow, "TargetListWindow", windows);
		StoreWindowPos(_targetsTargetWindow, "TargetsTargetWindow", windows);
		StoreWindowPos(_groupWindow, "GroupWindow", windows);
		StoreWindowPos(_melodyWindow, "MelodyWindow", windows);
		StoreWindowPos(_castBarPanel, "CastBar", windows);
		StoreWindowPos(_optionsPanel, "OptionsPanel", windows);
		StoreWindowPos(_actionBarWindow, "ActionBarWindow", windows);
		StoreWindowPos(_inventoryWindow, "InventoryWindow", windows);
		StoreWindowPos(_chatWindow, "ChatWindow", windows);
		StoreWindowPos(_skillsWindow, "SkillsWindow", windows);
		
		var menuWindow = GetNodeOrNull<Window>("MenuWindow");
		if (menuWindow != null) StoreWindowPos(menuWindow, "MenuWindow", windows);
		
		var spellsWindow = GetNodeOrNull<Window>("Spellbook");
		if (spellsWindow != null) StoreWindowPos(spellsWindow, "Spellbook", windows);
		
		var spellBarWindow = GetNodeOrNull<Window>("SpellBarWindow");
		if (spellBarWindow != null) StoreWindowPos(spellBarWindow, "SpellBarWindow", windows);
		
		var partyWindow = GetNodeOrNull<Window>("PartyWindow");
		if (partyWindow != null) StoreWindowPos(partyWindow, "PartyWindow", windows);
		
		var statusWindow = GetNodeOrNull<Window>("StatusWindow");
		if (statusWindow != null) StoreWindowPos(statusWindow, "StatusWindow", windows);
		
		if (_buffBarWindow != null) StoreWindowPos(_buffBarWindow, "BuffWindow", windows);
		if (_songBarWindow != null) StoreWindowPos(_songBarWindow, "SongWindow", windows);
		if (_copyLayoutWindow != null) StoreWindowPos(_copyLayoutWindow, "CopyLayoutWindow", windows);
		if (_merchantWindow != null) StoreWindowPos(_merchantWindow, "MerchantWindow", windows);
		
		UILayoutManager.SetSection("Windows", windows);
		
		var video = UILayoutManager.GetSection("Video");
		video["FOV"] = _cameraFov;
		video["MSAA"] = _antiAliasing;
		video["VSync"] = _vSync;
		video["Fullscreen"] = _fullscreen;
		video["MaxFPS"] = _maxFps;
		UILayoutManager.SetSection("Video", video);

		if (!string.IsNullOrEmpty(GameState.CharacterName))
			UILayoutManager.SaveLayout(GameState.CharacterName);
	}

	public void SaveFullLayout()
	{
		SaveWindowPositions();
		if (_hotbarManager != null) _hotbarManager.SaveState();
	}

	private void StoreWindowPos(Node panel, string key, Godot.Collections.Dictionary windows)
	{
		if (panel == null) return;
		
		if (panel is Control ctrl) {
			windows[key] = new Godot.Collections.Dictionary { ["x"] = ctrl.GlobalPosition.X, ["y"] = ctrl.GlobalPosition.Y, ["w"] = ctrl.Size.X, ["h"] = ctrl.Size.Y };
		} else if (panel is Window win) {
			windows[key] = new Godot.Collections.Dictionary { ["x"] = win.Position.X, ["y"] = win.Position.Y, ["w"] = win.Size.X, ["h"] = win.Size.Y };
		}
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
	private void OnDoorStateChanged(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			using var doc = JsonDocument.Parse(data.AsString());
			var root = doc.RootElement;
			int doorId = root.GetProperty("doorId").GetInt32();
			bool isOpen = root.GetProperty("isOpen").GetBoolean();
			
			var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
			if (wm != null)
			{
				wm.ToggleDoor(doorId.ToString(), isOpen);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] Error parsing DOOR_STATE_CHANGE: {ex.Message}");
		}
	}

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
					if (_isInitialLoadPending)
					{
						// LOCK: Immediately consume the initial load pending flag to prevent parallel execution
						_isInitialLoadPending = false;
						_pendingSpawnLockedFromSpawnPos = false;

						_loadingBar.MaxValue = 100;
						_loadingBar.Value = 10;
						if (_flavorLabel != null) _flavorLabel.Text = "Checking zone assets...";
						await ToSignal(GetTree().CreateTimer(0.05f), SceneTreeTimer.SignalName.Timeout);

						// On-demand EQ asset extraction if configured
						if (EQAssetConfig.Instance.IsConfigured && !EQAssetCache.Instance.HasZone(_currentZoneId))
						{
							_loadingBar.Value = 20;
							if (_flavorLabel != null) _flavorLabel.Text = "Extracting Zone Assets...";
							await ToSignal(GetTree().CreateTimer(0.05f), SceneTreeTimer.SignalName.Timeout);
							
							var extractor = LanternExtractorRunner.Instance;
							if (extractor.IsAvailable)
							{
								bool extracted = await extractor.ExtractZone(_currentZoneId, _lanternArchiveBase);
								if (extracted)
									GD.Print($"[UI] Zone '{_currentZoneId}' extracted successfully.");
								else
									GD.PrintErr($"[UI] Zone extraction failed for '{_currentZoneId}', will use fallback.");
							}
						}

						_loadingBar.Value = 40;
						if (_flavorLabel != null) _flavorLabel.Text = "Building Zone Geometry...";
						// Freeze the player immediately so gravity can't pull them
						// through the map while zone geometry loads
						wm.FreezeForZoneLoad();
						await ToSignal(GetTree().CreateTimer(0.05f), SceneTreeTimer.SignalName.Timeout);
						
						wm.LoadZoneMap(_currentZoneId);
						wm.PlayZoneMusic(_currentZoneId);
						
						string ambienceTrack = _currentZoneId + "am";
						if (dict.TryGetProperty("ambience", out var ambVariant))
						{
							ambienceTrack = ambVariant.GetString();
						}
						wm.PlayZoneAmbience(ambienceTrack);
						
						_loadingBar.Value = 70;
						if (_flavorLabel != null) _flavorLabel.Text = "Loading doors...";
						await ToSignal(GetTree().CreateTimer(0.05f), SceneTreeTimer.SignalName.Timeout);
						
						if (dict.TryGetProperty("doors", out var doorsArray))
						{
							wm.ProcessDoors(doorsArray);
						}

						if (dict.TryGetProperty("worldObjects", out var worldObjectsArray))
						{
							wm.ProcessWorldObjects(worldObjectsArray);
						}

						_loadingBar.Value = 80;
						if (_flavorLabel != null) _flavorLabel.Text = "Generating Terrain Physics...";
						await ToSignal(GetTree().CreateTimer(0.05f), SceneTreeTimer.SignalName.Timeout);

						// GLB/Brewall geometry takes several physics frames to register collision shapes.
						// Without this, TeleportPlayer raycasts miss and Mobs fall through the floor!
						// We wait for 5 frames to be extra safe during initial load.
						for(int i = 0; i < 5; i++) {
							await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
						}

						_loadingBar.Value = 90;
						if (_flavorLabel != null) _flavorLabel.Text = "Spawning entities...";
						
						bool isDelta = dict.TryGetProperty("isDelta", out var dProp) && dProp.GetBoolean();
						JsonElement removedArr = default;
						if (isDelta) dict.TryGetProperty("removed", out removedArr);

						wm.SyncLiveMobs(entitiesArray, isDelta, removedArr);

						GD.Print("[UI] Initial entities hydrated. Spawning player...");
						wm.TeleportPlayer(_pendingSpawnX, _pendingSpawnY, _pendingSpawnZ);

						// CHECK: let physics register the capsule at the new XZ before comparing to authoritative EQ
						for (int settle = 0; settle < 2; settle++)
							await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
						
						_loadingBar.Value = 100;
						if (_flavorLabel != null) _flavorLabel.Text = $"Welcome to {_currentZoneId}!";
						await ToSignal(GetTree().CreateTimer(0.05f), SceneTreeTimer.SignalName.Timeout);

						// ADJUST (authoritative EQ vs client) + floor snap + RELEASE
						wm.FinishTeleportPlacement();

						if (_loadingLayer != null) 
						{
							_loadingLayer.Hide();
						}
					}
					else
					{
						// Periodic sync (not initial load)
						bool isDelta = dict.TryGetProperty("isDelta", out var dProp) && dProp.GetBoolean();
						JsonElement removedArr = default;
						if (isDelta) dict.TryGetProperty("removed", out removedArr);

						wm.SyncLiveMobs(entitiesArray, isDelta, removedArr);
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
					
						string weatherEffect = visionDict.TryGetProperty("weatherRenderEffect", out var wreProp) ? wreProp.GetString() : "none";
						wm.SetWeatherEffect(weatherEffect);

						// Set indoor/outdoor from server vision data (replaces hardcoded zone list)
						bool isOutdoor = visionDict.TryGetProperty("isOutdoor", out var ooProp) && ooProp.GetBoolean();
						wm.SetIndoorZone(!isOutdoor);

						// Wire up player light source from server vision data
						bool hasLight = visionDict.TryGetProperty("hasLightSource", out var hlProp) && hlProp.GetBoolean();
						wm.SetPlayerLightSource(hasLight);
					}

					if (dict.TryGetProperty("ambience", out var ambSync) && ambSync.ValueKind == JsonValueKind.String)
					{
						string ambStr = ambSync.GetString();
						if (!string.IsNullOrEmpty(ambStr))
							wm.PlayZoneAmbience(ambStr);
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

	private void HandlePlayerSync(float x, float y, float z, float heading)
	{
		var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
		// Send raw payload to server natively mapping Godot Z to Server Y, negating Godot's axes back to EQ's axes
		// Godot (-X = East, +Y = Up, -Z = North). EQ (-X = East, +Y = North, +Z = Up).
		bool hasLight = wm.GetPlayerCapsule()?.HasLightSource ?? false;
		string payload = $"{{\"type\": \"UPDATE_POS\", \"x\": {x}, \"y\": {y}, \"z\": {z}, \"heading\": {heading}, \"hasLightSource\": {hasLight.ToString().ToLower()}}}";

		bool forceMainSync = false;
		double now = Time.GetTicksMsec() / 1000.0;

		// If this is a "force stop" (zero delta sent from WorldManager), ensure main server gets it too
		// so our final idle position is consistent for ZONE_STATE backstops.
		if (x == 0 && y == 0 && z == 0)
		{
			forceMainSync = true;
			// Resolve actual coordinates for the final stop packet
			if (wm != null)
			{
				var gPos = wm.GetPlayerCapsule()?.GlobalPosition ?? Vector3.Zero;
				x = -gPos.X;
				y = -gPos.Z;
				z = gPos.Y;
				payload = $"{{\"type\": \"UPDATE_POS\", \"x\": {x}, \"y\": {y}, \"z\": {z}, \"heading\": {heading}, \"hasLightSource\": {hasLight.ToString().ToLower()}}}";
			}
		}

		if (_client.IsRelayConnected)
		{
			_client.SendRelayRaw(payload);

			// Periodically sync to main server even if relay is active (1Hz)
			// This keeps 'session.char' coordinates warm so ZONE_STATE (sent every 10s)
			// doesn't rubber-band players back to stale positions.
			if (forceMainSync || (now - _lastMainServerPosSync) >= 1.0)
			{
				_client.SendRaw(payload);
				_lastMainServerPosSync = now;
			}
		}
		else
		{
			_client.SendRaw(payload);
			_lastMainServerPosSync = now;
		}
	}

	private void OnMobMoveReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			var dict = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement;
			var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
			if (wm != null)
			{
				wm.ProcessMobMove(dict);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] OnMobMoveReceived error: {ex.Message}");
		}
	}

	private void OnScribeScrollReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(data.ToString());
			var root = doc.RootElement;
			string type = root.GetProperty("type").GetString();
			if (type == "SCRIBE_STARTED")
			{
				_scribingSpellName = root.TryGetProperty("spellName", out var sn) ? sn.GetString() : "Spell";
				int durationMs = root.TryGetProperty("durationMs", out var dm) ? dm.GetInt32() : 5000;
				_scribeTimeTotal = durationMs / 1000f;
				_scribeTimeElapsed = 0;
				_scribeBar.Value = 0;
				_isScribing = true;
				if (_scribeBarPanel != null) _scribeBarPanel.Show();
			}
			else if (type == "SCRIBE_REJECTED")
			{
				_spellbookUI?.ClearScribingStatus();
				CancelHeldItem();
				SyncInventorySlotsWithGiveNPC();
				if (!_isScribing && !_isMemorizing && _scribeBarPanel != null) _scribeBarPanel.Hide();
			}
			else if (type == "SCRIBE_COMPLETE" || type == "SCRIBE_CANCELLED")
			{
				_isScribing = false;
				_scribeTimeTotal = 0;
				_scribeTimeElapsed = 0;
				_spellbookUI?.ClearScribingStatus();
				CancelHeldItem();
				SyncInventorySlotsWithGiveNPC();
				// Keep the bar visible if a memorize timer is also using it.
				if (!_isMemorizing && _scribeBarPanel != null) _scribeBarPanel.Hide();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] OnScribeScrollReceived error: {ex.Message}");
		}
	}

	private void OnMemorizeSpellReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(data.ToString());
			var root = doc.RootElement;
			string type = root.GetProperty("type").GetString();
			if (type == "MEMORIZE_STARTED")
			{
				_memorizingSpellName = root.TryGetProperty("spellName", out var sn) ? sn.GetString() : "Spell";
				int durationMs = root.TryGetProperty("durationMs", out var dm) ? dm.GetInt32() : 2500;
				_memorizeTimeTotal = durationMs / 1000f;
				_memorizeTimeElapsed = 0;
				_isMemorizing = true;
				if (_scribeBar != null) _scribeBar.Value = 0;
				if (_scribeBarPanel != null) _scribeBarPanel.Show();
			}
			else if (type == "MEMORIZE_REJECTED")
			{
				_isMemorizing = false;
				_memorizeTimeTotal = 0;
				_memorizeTimeElapsed = 0;
				_memorizingSpellName = "";
				// Keep the bar visible if a scribe is still in progress.
				if (!_isScribing && _scribeBarPanel != null) _scribeBarPanel.Hide();
			}
			else if (type == "MEMORIZE_COMPLETE" || type == "MEMORIZE_CANCELLED")
			{
				_isMemorizing = false;
				_memorizeTimeTotal = 0;
				_memorizeTimeElapsed = 0;
				_memorizingSpellName = "";
				if (!_isScribing && _scribeBarPanel != null) _scribeBarPanel.Hide();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] OnMemorizeSpellReceived error: {ex.Message}");
		}
	}

	private void OnSpellAnimationReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			var dict = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement;
			var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
			if (wm != null)
			{
				wm.SpawnSpellAnimation(dict);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] OnSpellAnimationReceived error: {ex.Message}");
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





	private void EnsureBuffContextMenu()
	{
		if (_buffContextMenu != null) return;
		_buffContextMenu = new PopupMenu();
		_buffContextMenu.Name = "BuffContextMenu";
		_buffContextMenu.IdPressed += OnBuffContextMenuItemPressed;
		AddChild(_buffContextMenu);
		_buffContextMenu.Hide();
	}

	private void OnBuffContextMenuItemPressed(long id)
	{
		if (_contextMenuTargetBuff == null) return;

		if (id == 0) // Details
		{
			// Request spell details by name
			_client.SendRaw($"{{\"type\": \"SPELL_INSPECT\", \"spellName\": \"{_contextMenuTargetBuff.Name}\"}}");
		}
		else if (id == 1) // Remove
		{
			_client.SendRaw($"{{\"type\": \"REMOVE_BUFF\", \"name\": \"{_contextMenuTargetBuff.Name}\"}}");
		}

		_contextMenuTargetBuff = null;
	}

	// --- Public API for child windows ---
	public void ExecuteCommand(string text)
	{
		OnChatSubmitted(text);
	}

	public void SendNetworkMessage(string type, Dictionary<string, object> data)
	{
		if (_client == null) return;
		
		var payload = new Dictionary<string, object>(data);
		payload["type"] = type;
		
		string json = JsonSerializer.Serialize(payload);
		_client.SendRaw(json);
	}

	// ═══════════════════════════════════════════════════════════════
	//  CONNECTION RECOVERY
	// ═══════════════════════════════════════════════════════════════

	private void OnCloseUi(Variant data)
	{
		var root = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement;
		string uiName = root.TryGetProperty("uiName", out var uiNode) ? uiNode.GetString() : "";

		if (uiName == "trainer")
		{
			if (_trainerWindow != null && _trainerWindow.Visible)
			{
				_trainerWindow.Hide();
			}
		}
	}



	private void OnClientConnected()
	{
		GD.Print("[MAINUI] Reconnected to server! Attempting to resume session...");
		string pwd = GameState.AccountPassword;
		
		if (!string.IsNullOrEmpty(GameState.AccountName) && !string.IsNullOrEmpty(pwd))
		{
			GD.Print($"[MAINUI] Re-authenticating account: {GameState.AccountName}...");
			_client.SendMessage("LOGIN_ACCOUNT", new { username = GameState.AccountName, password = pwd });
		}
		else
		{
			GD.PrintErr($"[MAINUI] Cannot re-authenticate: Missing username or password.");
		}
	}

	private void OnClientDisconnected()
	{
		GD.Print("[MAINUI] Disconnected from server. Waiting for auto-reconnect...");
	}

	private void OnRelayConnected()
	{
		GD.Print("[UI] Connected to Movement Relay. Sending AUTH...");
		if (_client.CharacterId != -1 && _currentZoneNumericId != -1)
		{
			_client.SendRelayRaw($"{{\"type\": \"RELAY_AUTH\", \"charId\": {_client.CharacterId}, \"zoneId\": {_currentZoneNumericId}}}");
		}
	}

	private void OnAccountOkReceived(Variant data)
	{
		GD.Print("[MAINUI] Account re-authenticated. Requesting to re-enter world...");
		if (!string.IsNullOrEmpty(GameState.CharacterName))
		{
			_client.SendMessage("SELECT_CHARACTER", new { name = GameState.CharacterName });
		}
	}
}
