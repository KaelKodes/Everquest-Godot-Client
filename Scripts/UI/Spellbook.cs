using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Classic EverQuest-style spellbook UI.
/// Displays an open book with two pages (left + right), each holding 8 spell slots (2×4).
/// Pages can be turned with arrow buttons. 99 pages max (792 spell slots).
///
/// Interaction:
///   Left-click a scribed spell → enters "memorize mode" (click a spell bar gem to memorize)
///   Right-click a spell → enters "swap mode" → right-click another slot to swap positions
///   X button or stand → closes the book
/// </summary>
public partial class Spellbook : Panel
{
	// ── Signals ─────────────────────────────────────────────────────
	[Signal] public delegate void SpellSelectedForMemorizeEventHandler(string spellKey, string spellName);
	[Signal] public delegate void SpellbookClosedEventHandler();

	// ── Constants ───────────────────────────────────────────────────
	private const int SlotsPerPage = 8;
	private const int MaxPages = 99;
	private const int MaxSlots = MaxPages * SlotsPerPage; // 792
	private const int SlotColumns = 2;
	private const int SlotRows = 4;

	// ── Data ────────────────────────────────────────────────────────
	public struct BookSpell
	{
		public int BookSlot;
		public int SpellId;
		public string SpellKey;
		public string Name;
		public int ManaCost;
		public float CastTime;
		public string Effect;
		public int Level;
		public string Description;
		public int MemIcon;
		public int Icon;
	}

	private Dictionary<int, BookSpell> _bookSpells = new Dictionary<int, BookSpell>();
	private int _currentLeftPage = 0; // 0-indexed, always even (left page = even, right page = odd)

	// ── Swap State ──────────────────────────────────────────────────
	private int _swapFromSlot = -1; // bookSlot of the spell being swapped

	// Right-click inspect
	private double _rightClickTimer = -1;
	private int _rightClickSlot = -1;

	public override void _Process(double delta)
	{
		if (_rightClickTimer >= 0) {
			_rightClickTimer += delta;
			if (_rightClickTimer >= 1.0 && _rightClickSlot >= 0) {
				if (_bookSpells.TryGetValue(_rightClickSlot, out var spell)) {
					GameClient.Instance.SendRaw($"{{\"type\": \"SPELL_INSPECT\", \"spellId\": {spell.SpellId}}}");
				}
				_rightClickTimer = -1;
				_rightClickSlot = -1;
			}
		}
	}

	// ── UI References ───────────────────────────────────────────────
	private Panel _bookPanel;
	private Label _leftPageLabel;
	private Label _rightPageLabel;
	private Button _prevBtn;
	private Button _nextBtn;
	private Button _closeBtn;
	private Button[] _leftSlots = new Button[SlotsPerPage];
	private Button[] _rightSlots = new Button[SlotsPerPage];
	private Label _statusLabel;

	// ── Dragging ────────────────────────────────────────────────────
	private bool _isDragging = false;
	private Vector2 _dragOffset;

	private GameClient _client;

	public override void _Ready()
	{
		_client = GameClient.Instance;
		BuildUI();
		Visible = false;
	}

	// ── UI Construction ─────────────────────────────────────────────

	private void BuildUI()
	{
		// Main panel — book shape
		CustomMinimumSize = new Vector2(520, 380);
		Size = new Vector2(520, 380);
		ZIndex = 60;

		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.25f, 0.18f, 0.12f, 0.95f); // Dark leather
		panelStyle.BorderColor = new Color(0.55f, 0.40f, 0.20f, 1f);
		panelStyle.SetBorderWidthAll(3);
		panelStyle.SetCornerRadiusAll(6);
		AddThemeStyleboxOverride("panel", panelStyle);

		// ── Title Bar (drag handle) ─────────────────────────────────
		var titleBar = new Panel();
		titleBar.SetAnchorsPreset(LayoutPreset.TopWide);
		titleBar.OffsetBottom = 28;
		var titleStyle = new StyleBoxFlat();
		titleStyle.BgColor = new Color(0.35f, 0.25f, 0.15f, 0.9f);
		titleStyle.SetCornerRadiusAll(4);
		titleBar.AddThemeStyleboxOverride("panel", titleStyle);
		AddChild(titleBar);

		var titleLabel = new Label();
		titleLabel.Text = "Spellbook";
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		titleLabel.AddThemeFontSizeOverride("font_size", 14);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
		titleBar.AddChild(titleLabel);

		// Drag handling
		titleBar.GuiInput += (ev) =>
		{
			if (ev is InputEventMouseButton mb)
			{
				if (mb.ButtonIndex == MouseButton.Left)
				{
					if (mb.Pressed)
					{
						_isDragging = true;
						_dragOffset = mb.GlobalPosition - GlobalPosition;
					}
					else _isDragging = false;
				}
			}
			else if (ev is InputEventMouseMotion mm && _isDragging)
			{
				GlobalPosition = mm.GlobalPosition - _dragOffset;
			}
		};

		// ── Close Button ────────────────────────────────────────────
		_closeBtn = new Button();
		_closeBtn.Text = "X";
		_closeBtn.Position = new Vector2(Size.X - 30, 2);
		_closeBtn.Size = new Vector2(24, 24);
		_closeBtn.AddThemeFontSizeOverride("font_size", 12);
		_closeBtn.Pressed += () => { Visible = false; EmitSignal(SignalName.SpellbookClosed); };
		AddChild(_closeBtn);

		// ── Book Interior ───────────────────────────────────────────
		// Two pages side by side with a spine divider
		var bookArea = new HBoxContainer();
		bookArea.Position = new Vector2(8, 34);
		bookArea.Size = new Vector2(504, 310);
		bookArea.AddThemeConstantOverride("separation", 4);
		AddChild(bookArea);

		// Left Page
		var leftPage = BuildPage(_leftSlots, true);
		bookArea.AddChild(leftPage);

		// Spine divider
		var spine = new Panel();
		spine.CustomMinimumSize = new Vector2(4, 0);
		spine.SizeFlagsVertical = SizeFlags.ExpandFill;
		var spineStyle = new StyleBoxFlat();
		spineStyle.BgColor = new Color(0.4f, 0.3f, 0.18f, 0.8f);
		spine.AddThemeStyleboxOverride("panel", spineStyle);
		bookArea.AddChild(spine);

		// Right Page
		var rightPage = BuildPage(_rightSlots, false);
		bookArea.AddChild(rightPage);

		// ── Page Number Labels ──────────────────────────────────────
		_leftPageLabel = new Label();
		_leftPageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_leftPageLabel.AddThemeFontSizeOverride("font_size", 13);
		_leftPageLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.25f, 0.15f));
		leftPage.AddChild(_leftPageLabel);
		_leftPageLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
		_leftPageLabel.OffsetTop = 2;
		_leftPageLabel.OffsetBottom = 18;

		_rightPageLabel = new Label();
		_rightPageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_rightPageLabel.AddThemeFontSizeOverride("font_size", 13);
		_rightPageLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.25f, 0.15f));
		rightPage.AddChild(_rightPageLabel);
		_rightPageLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
		_rightPageLabel.OffsetTop = 2;
		_rightPageLabel.OffsetBottom = 18;

		// ── Navigation Arrows ───────────────────────────────────────
		_prevBtn = new Button();
		_prevBtn.Text = "◀";
		_prevBtn.Position = new Vector2(8, Size.Y - 34);
		_prevBtn.Size = new Vector2(32, 26);
		_prevBtn.AddThemeFontSizeOverride("font_size", 14);
		_prevBtn.Pressed += () => TurnPage(-2);
		AddChild(_prevBtn);

		_nextBtn = new Button();
		_nextBtn.Text = "▶";
		_nextBtn.Position = new Vector2(Size.X - 40, Size.Y - 34);
		_nextBtn.Size = new Vector2(32, 26);
		_nextBtn.AddThemeFontSizeOverride("font_size", 14);
		_nextBtn.Pressed += () => TurnPage(2);
		AddChild(_nextBtn);

		// ── Status Label ────────────────────────────────────────────
		_statusLabel = new Label();
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_statusLabel.Position = new Vector2(60, Size.Y - 32);
		_statusLabel.Size = new Vector2(Size.X - 120, 24);
		_statusLabel.AddThemeFontSizeOverride("font_size", 11);
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.45f));
		AddChild(_statusLabel);
	}

	private Panel BuildPage(Button[] slots, bool isLeft)
	{
		var page = new Panel();
		page.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		page.SizeFlagsVertical = SizeFlags.ExpandFill;
		page.CustomMinimumSize = new Vector2(244, 0);

		var pageStyle = new StyleBoxFlat();
		pageStyle.BgColor = new Color(0.72f, 0.65f, 0.50f, 0.9f); // Parchment
		pageStyle.BorderColor = new Color(0.5f, 0.4f, 0.25f, 0.6f);
		pageStyle.SetBorderWidthAll(1);
		pageStyle.SetCornerRadiusAll(3);
		page.AddThemeStyleboxOverride("panel", pageStyle);

		// Grid of spell slots
		var grid = new GridContainer();
		grid.Columns = SlotColumns;
		grid.Position = new Vector2(16, 24);
		grid.Size = new Vector2(212, 270);
		grid.AddThemeConstantOverride("h_separation", 8);
		grid.AddThemeConstantOverride("v_separation", 8);
		page.AddChild(grid);

		var slotStyle = new StyleBoxFlat();
		slotStyle.BgColor = new Color(0.6f, 0.52f, 0.35f, 0.7f);
		slotStyle.BorderColor = new Color(0.7f, 0.6f, 0.35f, 0.9f);
		slotStyle.SetBorderWidthAll(2);
		slotStyle.SetCornerRadiusAll(2);

		var hoverStyle = new StyleBoxFlat();
		hoverStyle.BgColor = new Color(0.7f, 0.6f, 0.4f, 0.85f);
		hoverStyle.BorderColor = new Color(0.85f, 0.7f, 0.3f, 1f);
		hoverStyle.SetBorderWidthAll(2);
		hoverStyle.SetCornerRadiusAll(2);

		for (int i = 0; i < SlotsPerPage; i++)
		{
			int slotIdx = i;
			var btn = new Button();
			btn.CustomMinimumSize = new Vector2(96, 58);
			btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btn.SizeFlagsVertical = SizeFlags.ExpandFill;
			btn.ClipText = true;
			btn.AddThemeFontSizeOverride("font_size", 9);
			btn.IconAlignment = HorizontalAlignment.Center;
			btn.VerticalIconAlignment = VerticalAlignment.Top;
			btn.ExpandIcon = true;
			btn.AddThemeStyleboxOverride("normal", slotStyle);
			btn.AddThemeStyleboxOverride("hover", hoverStyle);
			btn.AddThemeStyleboxOverride("pressed", hoverStyle);

			// Wire click handler
			btn.GuiInput += (ev) =>
			{
				if (ev is InputEventMouseButton mb) {
					int bookSlot = GetBookSlotForPageSlot(slotIdx, isLeft);
					if (mb.ButtonIndex == MouseButton.Left && mb.Pressed) {
						OnSlotLeftClicked(bookSlot);
					}
					else if (mb.ButtonIndex == MouseButton.Right) {
						if (mb.Pressed) {
							_rightClickTimer = 0;
							_rightClickSlot = bookSlot;
						} else {
							if (_rightClickTimer >= 0 && _rightClickTimer < 1.0) {
								OnSlotRightClicked(bookSlot);
							}
							_rightClickTimer = -1;
							_rightClickSlot = -1;
						}
					}
				}
			};

			grid.AddChild(btn);
			slots[i] = btn;
		}

		return page;
	}

	// ── Data Loading ────────────────────────────────────────────────

	/// <summary>Load all scribed spells from the SPELLBOOK_FULL payload.</summary>
	public void LoadFromPayload(string json)
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(json);
			var root = doc.RootElement;
			if (!root.TryGetProperty("entries", out var entries)) return;

			_bookSpells.Clear();
			foreach (var entry in entries.EnumerateArray())
			{
				int bookSlot = entry.GetProperty("bookSlot").GetInt32();
				var spell = new BookSpell
				{
					BookSlot = bookSlot,
					SpellId = entry.TryGetProperty("spellId", out var id) ? id.GetInt32() : 0,
					SpellKey = entry.TryGetProperty("spellKey", out var k) ? k.GetString() : "",
					Name = entry.TryGetProperty("name", out var n) ? n.GetString() : "???",
					ManaCost = entry.TryGetProperty("manaCost", out var mc) ? mc.GetInt32() : 0,
					CastTime = entry.TryGetProperty("castTime", out var ct) ? (float)ct.GetDouble() : 1.5f,
					Effect = entry.TryGetProperty("effect", out var ef) ? ef.GetString() : "unknown",
					Level = entry.TryGetProperty("level", out var lv) ? lv.GetInt32() : 1,
					Description = entry.TryGetProperty("description", out var desc) ? desc.GetString() : "",
					MemIcon = entry.TryGetProperty("memIcon", out var mi) ? mi.GetInt32() : 0,
					Icon = entry.TryGetProperty("icon", out var ic) ? ic.GetInt32() : 0,
				};
				_bookSpells[bookSlot] = spell;
			}

			RefreshPageDisplay();
			GD.Print($"[SPELLBOOK] Loaded {_bookSpells.Count} scribed spells into book UI.");
		}
		catch (Exception ex) { GD.PrintErr($"[SPELLBOOK] Parse error: {ex.Message}"); }
	}

	/// <summary>Returns all known spell keys (for the right-click memorize picker on the spell bar).</summary>
	public List<BookSpell> GetAllScribedSpells()
	{
		return new List<BookSpell>(_bookSpells.Values);
	}

	public bool IsBookSlotOccupied(int bookSlot) => _bookSpells.ContainsKey(bookSlot);

	// ── Page Navigation ─────────────────────────────────────────────

	private void TurnPage(int delta)
	{
		int newPage = _currentLeftPage + delta;
		if (newPage < 0) newPage = 0;
		if (newPage >= MaxPages - 1) newPage = MaxPages - 2; // keep even
		if (newPage % 2 != 0) newPage--; // ensure left page is even
		_currentLeftPage = newPage;
		RefreshPageDisplay();
	}

	public void GoToPage(int page)
	{
		if (page < 0) page = 0;
		if (page >= MaxPages) page = MaxPages - 1;
		if (page % 2 != 0) page--; // ensure left page is even
		_currentLeftPage = page;
		RefreshPageDisplay();
	}

	// ── Display Refresh ─────────────────────────────────────────────

	private void RefreshPageDisplay()
	{
		int leftPageNum = _currentLeftPage + 1;  // 1-indexed for display
		int rightPageNum = _currentLeftPage + 2;

		_leftPageLabel.Text = leftPageNum.ToString();
		_rightPageLabel.Text = rightPageNum.ToString();

		// Update left page slots
		for (int i = 0; i < SlotsPerPage; i++)
		{
			int bookSlot = _currentLeftPage * SlotsPerPage + i;
			UpdateSlotButton(_leftSlots[i], bookSlot);
		}

		// Update right page slots
		for (int i = 0; i < SlotsPerPage; i++)
		{
			int bookSlot = (_currentLeftPage + 1) * SlotsPerPage + i;
			UpdateSlotButton(_rightSlots[i], bookSlot);
		}

		// Navigation button states
		_prevBtn.Disabled = _currentLeftPage <= 0;
		_nextBtn.Disabled = _currentLeftPage >= MaxPages - 2;
	}

	private void UpdateSlotButton(Button btn, int bookSlot)
	{
		if (_bookSpells.TryGetValue(bookSlot, out var spell))
		{
			btn.Text = spell.Name;
			string descLine = !string.IsNullOrEmpty(spell.Description) ? $"\n{spell.Description}" : "";
			btn.TooltipText = $"{spell.Name}\nMana: {spell.ManaCost} | Cast: {spell.CastTime:F1}s\nLevel: {spell.Level} | Type: {spell.Effect}{descLine}";
			btn.Disabled = false;

			// Load spell gem icon
			var iconMgr = IconManager.Instance;
			btn.Icon = null;
			if (iconMgr != null && spell.MemIcon > 0) {
				var gemTex = iconMgr.GetSpellGem(spell.MemIcon);
				if (gemTex != null) btn.Icon = gemTex;
			}

			// Highlight if this is the swap source
			if (_swapFromSlot == bookSlot)
			{
				var highlightStyle = new StyleBoxFlat();
				highlightStyle.BgColor = new Color(0.4f, 0.6f, 0.3f, 0.8f);
				highlightStyle.BorderColor = new Color(0.5f, 0.8f, 0.3f, 1f);
				highlightStyle.SetBorderWidthAll(2);
				highlightStyle.SetCornerRadiusAll(2);
				btn.AddThemeStyleboxOverride("normal", highlightStyle);
			}
			else
			{
				// Reset to default style
				var defaultStyle = new StyleBoxFlat();
				defaultStyle.BgColor = new Color(0.6f, 0.52f, 0.35f, 0.7f);
				defaultStyle.BorderColor = new Color(0.7f, 0.6f, 0.35f, 0.9f);
				defaultStyle.SetBorderWidthAll(2);
				defaultStyle.SetCornerRadiusAll(2);
				btn.AddThemeStyleboxOverride("normal", defaultStyle);
			}
		}
		else
		{
			btn.Text = "";
			btn.Icon = null;
			btn.TooltipText = $"Empty (Slot {bookSlot + 1})";
			btn.Disabled = false; // Needs to be clickable for swap target

			var emptyStyle = new StyleBoxFlat();
			emptyStyle.BgColor = new Color(0.55f, 0.48f, 0.33f, 0.4f);
			emptyStyle.BorderColor = new Color(0.6f, 0.5f, 0.3f, 0.5f);
			emptyStyle.SetBorderWidthAll(1);
			emptyStyle.SetCornerRadiusAll(2);
			btn.AddThemeStyleboxOverride("normal", emptyStyle);
		}
	}

	// ── Slot Interaction ────────────────────────────────────────────

	private int GetBookSlotForPageSlot(int slotIdx, bool isLeft)
	{
		int pageOffset = isLeft ? _currentLeftPage : _currentLeftPage + 1;
		return pageOffset * SlotsPerPage + slotIdx;
	}

	/// <summary>Left-click: select spell for memorization into spell bar.</summary>
	private void OnSlotLeftClicked(int bookSlot)
	{
		// Cancel any pending swap
		_swapFromSlot = -1;

		if (!_bookSpells.TryGetValue(bookSlot, out var spell))
		{
			if (MainUI.Instance != null && MainUI.Instance.TryDropHeldScrollOnBookSlot(bookSlot))
				_statusLabel.Text = "Scribing scroll...";
			return;
		}

		// Emit signal — MainUI will handle the "click a gem to memorize" flow
		EmitSignal(SignalName.SpellSelectedForMemorize, spell.SpellKey, spell.Name);
		_statusLabel.Text = $"Left-click a spell gem to memorize {spell.Name}...";
	}

	/// <summary>Right-click: enter swap mode or complete a swap.</summary>
	private void OnSlotRightClicked(int bookSlot)
	{
		if (_swapFromSlot < 0)
		{
			// First right-click: select source
			if (!_bookSpells.ContainsKey(bookSlot))
			{
				_statusLabel.Text = "Right-click a spell to swap it.";
				return;
			}
			_swapFromSlot = bookSlot;
			_statusLabel.Text = $"Right-click another slot to swap with {_bookSpells[bookSlot].Name}...";
			RefreshPageDisplay();
		}
		else
		{
			// Second right-click: execute swap
			if (bookSlot == _swapFromSlot)
			{
				// Cancel
				_swapFromSlot = -1;
				_statusLabel.Text = "";
				RefreshPageDisplay();
				return;
			}

			// Send swap to server
			_client?.SendRaw($"{{\"type\":\"SWAP_BOOK_SPELLS\",\"fromSlot\":{_swapFromSlot},\"toSlot\":{bookSlot}}}");

			// Optimistic local swap
			bool hasFrom = _bookSpells.TryGetValue(_swapFromSlot, out var fromSpell);
			bool hasTo = _bookSpells.TryGetValue(bookSlot, out var toSpell);

			if (hasFrom && hasTo)
			{
				fromSpell.BookSlot = bookSlot;
				toSpell.BookSlot = _swapFromSlot;
				_bookSpells[bookSlot] = fromSpell;
				_bookSpells[_swapFromSlot] = toSpell;
			}
			else if (hasFrom)
			{
				fromSpell.BookSlot = bookSlot;
				_bookSpells[bookSlot] = fromSpell;
				_bookSpells.Remove(_swapFromSlot);
			}

			_swapFromSlot = -1;
			_statusLabel.Text = "Spells swapped.";
			RefreshPageDisplay();
		}
	}

	public void CancelPendingAction()
	{
		_swapFromSlot = -1;
		_statusLabel.Text = "";
		RefreshPageDisplay();
	}

	/// <summary>Clears transient status (e.g. after server rejects scribe).</summary>
	public void ClearScribingStatus()
	{
		_statusLabel.Text = "";
	}

	// ── Input Handling ──────────────────────────────────────────────

	public override void _UnhandledInput(InputEvent ev)
	{
		if (!Visible) return;
		if (ev is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
		{
			if (_swapFromSlot >= 0)
			{
				CancelPendingAction();
			}
			else
			{
				Visible = false;
				EmitSignal(SignalName.SpellbookClosed);
			}
			GetViewport().SetInputAsHandled();
		}
	}
}
