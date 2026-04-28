using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A single EverQuest-style hotbar widget. Contains 10 slots across 10 pages (100 total hotbuttons).
/// Supports resizable grid layouts (1×10, 2×5, 5×2, 10×1), drag-to-reposition,
/// right-click context menu, and customizable appearance.
/// </summary>
public partial class Hotbar : Control
{
	// ── Data Model ──────────────────────────────────────────────────

	public enum HotbuttonType { Empty, Spell, Ability, Item, Social }

	public class HotbuttonData
	{
		public HotbuttonType Type = HotbuttonType.Empty;
		public int SpellSlotIndex = -1;    // For Spell: memorized slot (0-7)
		public string AbilityName = "";    // For Ability: "kick", "bash", etc.
		public int ItemId = -1;            // For Item: inventory item ID
		public string ItemKey = "";        // For Item: item key for equip/unequip
		public int SocialIndex = -1;       // For Social: index into social library
		public string DisplayName = "";    // Label on the button
		public int IconIndex = -1;         // Sprite atlas index (if applicable)
	}

	// 10 pages × 10 slots
	public HotbuttonData[,] SlotData { get; private set; } = new HotbuttonData[10, 10];

	// ── Configuration ───────────────────────────────────────────────

	public int BarIndex { get; set; } = 0;       // Which hotbar number (0-based)
	public int CurrentPage { get; private set; } = 0;
	public string BarName { get; set; } = "";    // User-defined name (shown in nav bar)

	// Layout: rows × cols (must multiply to 10)
	private int _layoutRows = 1;
	private int _layoutCols = 10;
	private static readonly (int rows, int cols)[] LayoutPresets = {
		(1, 10), (2, 5), (5, 2), (10, 1)
	};
	private int _currentLayoutIndex = 0;

	// Appearance
	public Color BgColor { get; set; } = new Color(0.06f, 0.06f, 0.08f, 0.9f);
	public Color BorderColor { get; set; } = new Color(0.55f, 0.45f, 0.2f, 0.9f);
	public float BarAlpha { get; set; } = 1.0f;
	public bool FadeWhenInactive { get; set; } = false;
	public bool Locked { get; set; } = false;
	public bool ShowSlotNumbers { get; set; } = true;

	// ── Signals ─────────────────────────────────────────────────────

	[Signal] public delegate void HotbuttonActivatedEventHandler(int barIndex, int page, int slot);
	[Signal] public delegate void NewHotbarRequestedEventHandler();
	[Signal] public delegate void HotbarClosedEventHandler(int barIndex);
	[Signal] public delegate void SaveSetRequestedEventHandler(int barIndex);
	[Signal] public delegate void LoadSetRequestedEventHandler(int barIndex);
	[Signal] public delegate void DeleteSetRequestedEventHandler(int barIndex);

	// ── UI Nodes ────────────────────────────────────────────────────

	private Panel _backgroundPanel;
	private GridContainer _slotGrid;
	private Button[] _slotButtons = new Button[10];
	private Label[] _slotLabels = new Label[10];   // Overlay labels for names
	private Button _prevPageBtn;
	private Button _nextPageBtn;
	private Label _pageLabel;
	private Button _menuBtn;
	private PopupMenu _contextMenu;
	private PopupMenu _layoutSubmenu;
	private PopupMenu _appearanceSubmenu;

	// Rename dialog
	private Panel _renamePanel;
	private LineEdit _renameField;

	// Drag state
	private bool _dragging = false;
	private Vector2 _dragOffset;

	// Hover fade
	private bool _mouseInside = false;
	private float _currentAlpha = 1.0f;

	// Slot sizing
	private const int SLOT_SIZE = 40;
	private const int SLOT_GAP = 2;
	private const int NAV_HEIGHT = 22;

	public override void _Ready()
	{
		// Initialize all slot data as empty
		for (int p = 0; p < 10; p++)
			for (int s = 0; s < 10; s++)
				SlotData[p, s] = new HotbuttonData();

		BuildUI();
		RefreshSlots();
	}

	// ── UI Construction ─────────────────────────────────────────────

	private void BuildUI()
	{
		MouseFilter = MouseFilterEnum.Stop;

		// Background panel with border
		_backgroundPanel = new Panel();
		_backgroundPanel.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(_backgroundPanel);
		ApplyBackgroundStyle();

		// Navigation row: [<] [Page 1] [>] [≡]
		var navRow = new HBoxContainer();
		navRow.Name = "NavRow";
		navRow.MouseFilter = MouseFilterEnum.Pass;
		AddChild(navRow);

		_prevPageBtn = CreateSmallButton("◀");
		_prevPageBtn.Pressed += () => ChangePage(-1);
		_prevPageBtn.CustomMinimumSize = new Vector2(NAV_HEIGHT, NAV_HEIGHT);
		navRow.AddChild(_prevPageBtn);

		_pageLabel = new Label();
		_pageLabel.Text = "1";
		_pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_pageLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_pageLabel.AddThemeFontSizeOverride("font_size", 11);
		_pageLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.65f, 0.4f));
		_pageLabel.MouseFilter = MouseFilterEnum.Pass;
		navRow.AddChild(_pageLabel);

		// Build rename panel (hidden by default)
		BuildRenameDialog();

		_nextPageBtn = CreateSmallButton("▶");
		_nextPageBtn.Pressed += () => ChangePage(1);
		_nextPageBtn.CustomMinimumSize = new Vector2(NAV_HEIGHT, NAV_HEIGHT);
		navRow.AddChild(_nextPageBtn);

		_menuBtn = CreateSmallButton("≡");
		_menuBtn.Pressed += ShowContextMenu;
		_menuBtn.CustomMinimumSize = new Vector2(NAV_HEIGHT, NAV_HEIGHT);
		navRow.AddChild(_menuBtn);

		// Slot grid
		_slotGrid = new GridContainer();
		_slotGrid.Name = "SlotGrid";
		_slotGrid.Columns = _layoutCols;
		_slotGrid.AddThemeConstantOverride("h_separation", SLOT_GAP);
		_slotGrid.AddThemeConstantOverride("v_separation", SLOT_GAP);
		AddChild(_slotGrid);

		// Create 10 slot buttons
		for (int i = 0; i < 10; i++)
		{
			int slotIndex = i;
			var container = new Panel();
			container.CustomMinimumSize = new Vector2(SLOT_SIZE, SLOT_SIZE);
			container.MouseFilter = MouseFilterEnum.Stop;

			var slotStyle = new StyleBoxFlat();
			slotStyle.BgColor = new Color(0.1f, 0.1f, 0.12f, 0.85f);
			slotStyle.BorderWidthLeft = 1; slotStyle.BorderWidthTop = 1;
			slotStyle.BorderWidthRight = 1; slotStyle.BorderWidthBottom = 1;
			slotStyle.BorderColor = new Color(0.4f, 0.35f, 0.2f, 0.7f);
			slotStyle.CornerRadiusTopLeft = 2; slotStyle.CornerRadiusTopRight = 2;
			slotStyle.CornerRadiusBottomLeft = 2; slotStyle.CornerRadiusBottomRight = 2;
			container.AddThemeStyleboxOverride("panel", slotStyle);

			// Invisible button overlay for click/drop handling
			var btn = new Button();
			btn.Flat = true;
			btn.SetAnchorsPreset(LayoutPreset.FullRect);
			btn.ClipText = true;
			btn.AddThemeFontSizeOverride("font_size", 9);
			btn.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.6f));

			// Hover style
			var hoverStyle = new StyleBoxFlat();
			hoverStyle.BgColor = new Color(0.2f, 0.2f, 0.25f, 0.6f);
			hoverStyle.BorderWidthLeft = 1; hoverStyle.BorderWidthTop = 1;
			hoverStyle.BorderWidthRight = 1; hoverStyle.BorderWidthBottom = 1;
			hoverStyle.BorderColor = new Color(0.8f, 0.7f, 0.3f, 0.9f);
			btn.AddThemeStyleboxOverride("hover", hoverStyle);

			// Normal: transparent
			var normalStyle = new StyleBoxFlat();
			normalStyle.BgColor = new Color(0, 0, 0, 0);
			btn.AddThemeStyleboxOverride("normal", normalStyle);
			btn.AddThemeStyleboxOverride("pressed", normalStyle);
			btn.FocusMode = FocusModeEnum.None; // Prevent TAB key from cycling into hotbar

			btn.Pressed += () => OnSlotPressed(slotIndex);
			btn.GuiInput += (ev) => OnSlotGuiInput(ev, slotIndex);

			container.AddChild(btn);

			// Slot number label (tiny, bottom-right)
			var numLabel = new Label();
			numLabel.Text = (i == 9) ? "0" : (i + 1).ToString();
			numLabel.AnchorLeft = 1; numLabel.AnchorTop = 1;
			numLabel.AnchorRight = 1; numLabel.AnchorBottom = 1;
			numLabel.OffsetLeft = -14; numLabel.OffsetTop = -14;
			numLabel.AddThemeFontSizeOverride("font_size", 8);
			numLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.45f, 0.3f, 0.6f));
			numLabel.MouseFilter = MouseFilterEnum.Ignore;
			container.AddChild(numLabel);

			_slotGrid.AddChild(container);
			_slotButtons[i] = btn;
			_slotLabels[i] = numLabel;
		}

		// Build context menus
		BuildContextMenu();

		// Make the nav row draggable for repositioning
		_pageLabel.GuiInput += OnNavBarInput;

		// Position layout
		RepositionElements();

		// Mouse enter/exit for fade
		MouseEntered += () => _mouseInside = true;
		MouseExited += () => _mouseInside = false;
	}

	private Button CreateSmallButton(string text)
	{
		var btn = new Button();
		btn.Text = text;
		btn.AddThemeFontSizeOverride("font_size", 10);
		btn.FocusMode = FocusModeEnum.None; // Prevent TAB key from cycling into hotbar

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.1f, 0.1f, 0.12f, 0.7f);
		style.BorderWidthLeft = 1; style.BorderWidthTop = 1;
		style.BorderWidthRight = 1; style.BorderWidthBottom = 1;
		style.BorderColor = new Color(0.4f, 0.35f, 0.2f, 0.5f);
		btn.AddThemeStyleboxOverride("normal", style);

		var hoverStyle = new StyleBoxFlat();
		hoverStyle.BgColor = new Color(0.18f, 0.18f, 0.22f, 0.9f);
		hoverStyle.BorderWidthLeft = 1; hoverStyle.BorderWidthTop = 1;
		hoverStyle.BorderWidthRight = 1; hoverStyle.BorderWidthBottom = 1;
		hoverStyle.BorderColor = new Color(0.7f, 0.6f, 0.3f, 0.9f);
		btn.AddThemeStyleboxOverride("hover", hoverStyle);

		return btn;
	}

	private void ApplyBackgroundStyle()
	{
		var style = new StyleBoxFlat();
		style.BgColor = BgColor;
		style.BorderWidthLeft = 2; style.BorderWidthTop = 2;
		style.BorderWidthRight = 2; style.BorderWidthBottom = 2;
		style.BorderColor = BorderColor;
		style.CornerRadiusTopLeft = 3; style.CornerRadiusTopRight = 3;
		style.CornerRadiusBottomLeft = 3; style.CornerRadiusBottomRight = 3;
		style.ContentMarginLeft = 4; style.ContentMarginRight = 4;
		style.ContentMarginTop = 2; style.ContentMarginBottom = 4;
		_backgroundPanel.AddThemeStyleboxOverride("panel", style);
	}

	private void RepositionElements()
	{
		var navRow = GetNode<HBoxContainer>("NavRow");

		float contentWidth = _layoutCols * SLOT_SIZE + (_layoutCols - 1) * SLOT_GAP;
		float contentHeight = _layoutRows * SLOT_SIZE + (_layoutRows - 1) * SLOT_GAP;
		float totalWidth = contentWidth + 12;   // padding
		float totalHeight = contentHeight + NAV_HEIGHT + 10; // padding + nav

		// Nav row at top
		navRow.Position = new Vector2(4, 2);
		navRow.Size = new Vector2(totalWidth - 8, NAV_HEIGHT);

		// Grid below nav
		_slotGrid.Position = new Vector2(6, NAV_HEIGHT + 4);
		_slotGrid.Columns = _layoutCols;

		// Background covers everything
		_backgroundPanel.Position = Vector2.Zero;
		_backgroundPanel.Size = new Vector2(totalWidth, totalHeight);

		// Set our own size
		CustomMinimumSize = new Vector2(totalWidth, totalHeight);
		Size = CustomMinimumSize;
	}

	// ── Context Menu ────────────────────────────────────────────────

	private void BuildContextMenu()
	{
		_contextMenu = new PopupMenu();
		_contextMenu.Name = "ContextMenu";
		AddChild(_contextMenu);

		_contextMenu.AddItem("New Hotbar", 0);
		_contextMenu.AddSeparator();
		_contextMenu.AddItem("Save Hotbutton Set", 1);
		_contextMenu.AddItem("Load Hotbutton Set", 2);
		_contextMenu.AddItem("Delete Hotbutton Set", 3);
		_contextMenu.AddItem("Clear Current Hotbuttons", 4);
		_contextMenu.AddSeparator();

		// Layout submenu
		_layoutSubmenu = new PopupMenu();
		_layoutSubmenu.Name = "LayoutSubmenu";
		_layoutSubmenu.AddItem("1 × 10 (Horizontal)", 0);
		_layoutSubmenu.AddItem("2 × 5", 1);
		_layoutSubmenu.AddItem("5 × 2", 2);
		_layoutSubmenu.AddItem("10 × 1 (Vertical)", 3);
		_layoutSubmenu.IdPressed += OnLayoutSelected;
		_contextMenu.AddChild(_layoutSubmenu);
		_contextMenu.AddSubmenuNodeItem("Layout", _layoutSubmenu, 10);

		_contextMenu.AddSeparator();
		_contextMenu.AddItem("Rename", 8);
		_contextMenu.AddCheckItem("Lock Position", 5);
		_contextMenu.AddCheckItem("Show Slot Numbers", 6);
		_contextMenu.SetItemChecked(_contextMenu.GetItemIndex(6), ShowSlotNumbers);
		_contextMenu.AddSeparator();
		_contextMenu.AddItem("Close Hotbar", 7);

		_contextMenu.IdPressed += OnContextMenuSelected;
	}

	private void ShowContextMenu()
	{
		_contextMenu.SetItemChecked(_contextMenu.GetItemIndex(5), Locked);
		_contextMenu.SetItemChecked(_contextMenu.GetItemIndex(6), ShowSlotNumbers);
		_contextMenu.Position = (Vector2I)GetGlobalMousePosition();
		_contextMenu.Popup();
	}

	private void OnContextMenuSelected(long id)
	{
		switch (id)
		{
			case 0: EmitSignal(SignalName.NewHotbarRequested); break;
			case 1: EmitSignal(SignalName.SaveSetRequested, BarIndex); break;
			case 2: EmitSignal(SignalName.LoadSetRequested, BarIndex); break;
			case 3: EmitSignal(SignalName.DeleteSetRequested, BarIndex); break;
			case 4: ClearAllSlots(); break;
			case 5:
				Locked = !Locked;
				break;
			case 6:
				ShowSlotNumbers = !ShowSlotNumbers;
				for (int i = 0; i < 10; i++)
					_slotLabels[i].Visible = ShowSlotNumbers;
				break;
			case 7: EmitSignal(SignalName.HotbarClosed, BarIndex); break;
			case 8: ShowRenameDialog(); break;
		}
	}

	private void OnLayoutSelected(long id)
	{
		if (id >= 0 && id < LayoutPresets.Length)
		{
			_currentLayoutIndex = (int)id;
			_layoutRows = LayoutPresets[_currentLayoutIndex].rows;
			_layoutCols = LayoutPresets[_currentLayoutIndex].cols;
			RepositionElements();
		}
	}

	// ── Page Navigation ─────────────────────────────────────────────

	private void ChangePage(int delta)
	{
		CurrentPage = (CurrentPage + delta + 10) % 10;
		UpdateNavLabel();
		RefreshSlots();
	}

	public void SetPage(int page)
	{
		if (page < 0 || page >= 10) return;
		CurrentPage = page;
		UpdateNavLabel();
		RefreshSlots();
	}

	/// <summary>
	/// Updates the nav bar label to show name + page number.
	/// </summary>
	private void UpdateNavLabel()
	{
		if (string.IsNullOrEmpty(BarName))
			_pageLabel.Text = (CurrentPage + 1).ToString();
		else
			_pageLabel.Text = $"{BarName} [{CurrentPage + 1}]";
	}

	// ── Rename Dialog ────────────────────────────────────────────────

	private void BuildRenameDialog()
	{
		_renamePanel = new Panel();
		_renamePanel.Visible = false;
		_renamePanel.CustomMinimumSize = new Vector2(220, 60);
		_renamePanel.ZIndex = 200;
		_renamePanel.MouseFilter = MouseFilterEnum.Stop;

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.97f);
		style.BorderWidthLeft = 2; style.BorderWidthTop = 2;
		style.BorderWidthRight = 2; style.BorderWidthBottom = 2;
		style.BorderColor = new Color(0.7f, 0.6f, 0.3f, 1.0f);
		style.CornerRadiusTopLeft = 3; style.CornerRadiusTopRight = 3;
		style.CornerRadiusBottomLeft = 3; style.CornerRadiusBottomRight = 3;
		style.ContentMarginLeft = 8; style.ContentMarginRight = 8;
		style.ContentMarginTop = 6; style.ContentMarginBottom = 6;
		_renamePanel.AddThemeStyleboxOverride("panel", style);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsPreset(LayoutPreset.FullRect);
		vbox.OffsetLeft = 8; vbox.OffsetRight = -8;
		vbox.OffsetTop = 6; vbox.OffsetBottom = -6;
		vbox.AddThemeConstantOverride("separation", 4);
		_renamePanel.AddChild(vbox);

		var label = new Label();
		label.Text = "Rename Hotbar";
		label.AddThemeFontSizeOverride("font_size", 12);
		label.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.4f));
		vbox.AddChild(label);

		_renameField = new LineEdit();
		_renameField.PlaceholderText = "Hotbar name...";
		_renameField.MaxLength = 20;
		_renameField.CustomMinimumSize = new Vector2(0, 26);
		_renameField.AddThemeFontSizeOverride("font_size", 12);
		_renameField.TextSubmitted += OnRenameSubmitted;
		vbox.AddChild(_renameField);

		AddChild(_renamePanel);
	}

	private void ShowRenameDialog()
	{
		_renameField.Text = BarName;
		_renamePanel.Visible = true;
		_renamePanel.GlobalPosition = GetGlobalMousePosition() - new Vector2(110, 30);
		_renameField.GrabFocus();
		_renameField.SelectAll();
	}

	private void OnRenameSubmitted(string newName)
	{
		BarName = newName.Trim();
		UpdateNavLabel();
		_renamePanel.Visible = false;
		GD.Print($"[HOTBAR] Bar {BarIndex + 1} renamed to '{BarName}'");
	}

	// ── Slot Display ────────────────────────────────────────────────

	public void RefreshSlots()
	{
		for (int i = 0; i < 10; i++)
		{
			var data = SlotData[CurrentPage, i];
			var btn = _slotButtons[i];

			if (data.Type == HotbuttonType.Empty)
			{
				btn.Text = "";
				btn.TooltipText = $"Empty (Slot {(i == 9 ? 0 : i + 1)})";
				btn.Icon = null;
				btn.Disabled = false;
			}
			else
			{
				string label = data.DisplayName;
				if (label.Length > 6)
					label = label[..6];
				btn.Text = label;
				btn.TooltipText = data.DisplayName;
				btn.Disabled = false;

				// Color-code by type
				Color fontColor = data.Type switch
				{
					HotbuttonType.Spell => new Color(0.6f, 0.8f, 1.0f),
					HotbuttonType.Ability => new Color(1.0f, 0.8f, 0.5f),
					HotbuttonType.Item => new Color(0.7f, 1.0f, 0.7f),
					HotbuttonType.Social => new Color(1.0f, 0.7f, 1.0f),
					_ => new Color(0.85f, 0.8f, 0.6f),
				};
				btn.AddThemeColorOverride("font_color", fontColor);
			}
		}
	}

	// ── Slot Interaction ────────────────────────────────────────────

	private void OnSlotPressed(int slotIndex)
	{
		var data = SlotData[CurrentPage, slotIndex];
		if (data.Type == HotbuttonType.Empty) return;

		EmitSignal(SignalName.HotbuttonActivated, BarIndex, CurrentPage, slotIndex);
	}

	private void OnSlotGuiInput(InputEvent ev, int slotIndex)
	{
		if (ev is InputEventMouseButton mb)
		{
			// Right-click to clear a slot
			if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
			{
				var data = SlotData[CurrentPage, slotIndex];
				if (data.Type != HotbuttonType.Empty)
				{
					// Create mini context menu for the slot
					var slotMenu = new PopupMenu();
					slotMenu.AddItem("Clear Slot", 0);
					slotMenu.AddItem("Use", 1);
					slotMenu.IdPressed += (id) => {
						if (id == 0)
						{
							SlotData[CurrentPage, slotIndex] = new HotbuttonData();
							RefreshSlots();
						}
						else if (id == 1)
						{
							OnSlotPressed(slotIndex);
						}
						slotMenu.QueueFree();
					};
					AddChild(slotMenu);
					slotMenu.Position = (Vector2I)GetGlobalMousePosition();
					slotMenu.Popup();
				}
			}
		}
	}

	// ── Drag-and-Drop ───────────────────────────────────────────────

	/// <summary>
	/// Accepts a dropped hotbutton data dictionary onto a specific slot.
	/// Called by HotbarManager when a drag completes over this bar.
	/// </summary>
	public void SetSlot(int page, int slot, HotbuttonData data)
	{
		if (page < 0 || page >= 10 || slot < 0 || slot >= 10) return;
		SlotData[page, slot] = data;
		if (page == CurrentPage) RefreshSlots();
	}

	/// <summary>
	/// Determines which slot index (0-9) the given local position corresponds to.
	/// Returns -1 if not over any slot.
	/// </summary>
	public int GetSlotAtPosition(Vector2 localPos)
	{
		for (int i = 0; i < 10; i++)
		{
			var container = _slotGrid.GetChild<Panel>(i);
			var rect = new Rect2(
				_slotGrid.Position + container.Position,
				container.Size
			);
			if (rect.HasPoint(localPos))
				return i;
		}
		return -1;
	}

	// ── Drag-to-Reposition ──────────────────────────────────────────

	private void OnNavBarInput(InputEvent ev)
	{
		if (Locked) return;

		if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				_dragging = true;
				_dragOffset = GetGlobalMousePosition() - GlobalPosition;
			}
			else
			{
				_dragging = false;
			}
		}
		else if (ev is InputEventMouseMotion && _dragging)
		{
			GlobalPosition = GetGlobalMousePosition() - _dragOffset;
		}
	}

	// ── Process (fade animation) ────────────────────────────────────

	public override void _Process(double delta)
	{
		if (FadeWhenInactive)
		{
			float targetAlpha = _mouseInside ? BarAlpha : BarAlpha * 0.3f;
			_currentAlpha = Mathf.Lerp(_currentAlpha, targetAlpha, (float)delta * 6.0f);
			Modulate = new Color(1, 1, 1, _currentAlpha);
		}
		else
		{
			Modulate = new Color(1, 1, 1, BarAlpha);
		}
	}

	// ── Utility ─────────────────────────────────────────────────────

	public void ClearAllSlots()
	{
		for (int p = 0; p < 10; p++)
			for (int s = 0; s < 10; s++)
				SlotData[p, s] = new HotbuttonData();
		RefreshSlots();
	}

	/// <summary>
	/// Get current layout info for serialization.
	/// </summary>
	public (int rows, int cols, int layoutIndex) GetLayout()
	{
		return (_layoutRows, _layoutCols, _currentLayoutIndex);
	}

	/// <summary>
	/// Restore layout from saved data.
	/// </summary>
	public void SetLayout(int layoutIndex)
	{
		if (layoutIndex < 0 || layoutIndex >= LayoutPresets.Length) return;
		_currentLayoutIndex = layoutIndex;
		_layoutRows = LayoutPresets[layoutIndex].rows;
		_layoutCols = LayoutPresets[layoutIndex].cols;
		RepositionElements();
	}

	/// <summary>
	/// Update a slot's enabled/disabled state based on spell cooldowns, mana, etc.
	/// </summary>
	public void SetSlotDisabled(int slot, bool disabled)
	{
		if (slot < 0 || slot >= 10) return;
		_slotButtons[slot].Disabled = disabled;
	}

	/// <summary>
	/// Update the tooltip on a slot (e.g., to show cooldown time remaining).
	/// </summary>
	public void SetSlotTooltip(int slot, string tooltip)
	{
		if (slot < 0 || slot >= 10) return;
		_slotButtons[slot].TooltipText = tooltip;
	}
}
