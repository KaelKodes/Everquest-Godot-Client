using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class MainUI
{
	private void OnInventoryUpdated(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			
			if (!root.TryGetProperty("inventory", out var inventory)) return;

			// Update Log if message provided
			if (root.TryGetProperty("message", out var msg)) Log("SYSTEM", msg.GetString());

			// Cancel any held item on inventory refresh
			CancelHeldItem();
			_slotItemData.Clear();

			// Reset all equipment slots to empty
			foreach (var kvp in _equipSlots) {
				kvp.Value.Text = "---";
				kvp.Value.TooltipText = kvp.Key;
			}

			// Reset all 10 general inventory slots
			for (int i = 0; i < 10; i++) {
				_invSlots[i].Text = $"\u2500\u2500 Slot {i+1} \u2500\u2500";
				_invSlots[i].TooltipText = "Empty";
			}

			foreach (var item in inventory.EnumerateArray())
			{
				string name = item.GetProperty("itemName").GetString();
				int equipped = item.GetProperty("equipped").GetInt32();
				int slotId = item.GetProperty("slotId").GetInt32();
				string statText = BuildItemStatText(item);

				if (equipped == 1) {
					string slotName = MapSlotToName(slotId);
					if (slotName != null && _equipSlots.TryGetValue(slotName, out var slotBtn)) {
						slotBtn.Text = name.Length > 12 ? name[..12] + ".." : name;
						slotBtn.TooltipText = $"{name}\n{statText}";
						_slotItemData[slotBtn] = item.Clone();
					}
				} else {
					int idx = slotId - 22;
					if (idx < 0 || idx >= 10) continue;

					var btn = _invSlots[idx];
					btn.Text = name.Length > 18 ? name[..18] + ".." : name;
					int sellVal = item.TryGetProperty("sellValue", out var svProp) ? svProp.GetInt32() : 0;
					string sellText = sellVal > 0 ? $"\nSell: {FormatCurrency(sellVal)}" : "";
					btn.TooltipText = $"{name}\n{statText}{sellText}";
					_slotItemData[btn] = item.Clone();
				}
			}
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Inv Error: {ex.Message}"); }
	}

	/// <summary>
	/// Create 10 identical slot buttons in the inventory grid (2 columns, 5 rows).
	/// </summary>
	private void BuildInventorySlots()
	{
		for (int i = 0; i < 10; i++)
		{
			var btn = new Button();
			btn.Text = $"\u2500\u2500 Slot {i+1} \u2500\u2500";
			btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btn.CustomMinimumSize = new Vector2(0, 28);
			btn.AddThemeFontSizeOverride("font_size", 10);
			btn.ClipText = true;
			btn.TooltipText = "Empty";
			_slotsGrid.AddChild(btn);
			_invSlots[i] = btn;
			int slotId = 22 + i;
			btn.GuiInput += (ev) => HandleSlotInput(ev, btn, slotId);
		}
	}

	// ─── Auto-Equip Slot ────────────────────────────────────────────
	private void BuildAutoEquipSlot()
	{
		var mainVBox = _inventoryWindow.GetNode<VBoxContainer>("MainVBox");
		var slotsSection = mainVBox.GetNode<VBoxContainer>("SlotsSection");

		_autoEquipBtn = new Button();
		_autoEquipBtn.Text = "▼ Auto-Equip ▼";
		_autoEquipBtn.CustomMinimumSize = new Vector2(0, 30);
		_autoEquipBtn.AddThemeFontSizeOverride("font_size", 11);
		_autoEquipBtn.TooltipText = "Drop a held item here to auto-equip it";

		// Insert between equip panel and slots section
		int idx = slotsSection.GetIndex();
		mainVBox.AddChild(_autoEquipBtn);
		mainVBox.MoveChild(_autoEquipBtn, idx);

		_autoEquipBtn.GuiInput += (inputEvent) => {
			if (inputEvent is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) {
				if (_heldItem.HasValue) {
					int itemId = _heldItem.Value.GetProperty("item_id").GetInt32();
					_client.SendRaw($"{{\"type\": \"AUTO_EQUIP\", \"itemId\": {itemId}}}");
					CancelHeldItem();
				}
			}
		};
	}

	// ─── Cursor Label (floating item name on cursor) ────────────────
	private void BuildCursorLabel()
	{
		_cursorLabel = new Label();
		_cursorLabel.Text = "";
		_cursorLabel.Visible = false;
		_cursorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.5f));
		_cursorLabel.AddThemeFontSizeOverride("font_size", 12);
		_cursorLabel.ZIndex = 100;
		_cursorLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_cursorLabel);
	}

	// ─── Item Detail Popup ──────────────────────────────────────────
	private void BuildItemDetailPopup()
	{
		_itemDetailPopup = new Panel();
		_itemDetailPopup.Visible = false;
		_itemDetailPopup.CustomMinimumSize = new Vector2(220, 200);
		_itemDetailPopup.ZIndex = 200;
		_itemDetailPopup.MouseFilter = Control.MouseFilterEnum.Stop;

		var sb = new StyleBoxFlat();
		sb.BgColor = new Color(0.08f, 0.07f, 0.05f, 0.95f);
		sb.BorderWidthLeft = 2; sb.BorderWidthTop = 2; sb.BorderWidthRight = 2; sb.BorderWidthBottom = 2;
		sb.BorderColor = new Color(0.6f, 0.5f, 0.2f, 0.9f);
		sb.ContentMarginLeft = 8; sb.ContentMarginTop = 8; sb.ContentMarginRight = 8; sb.ContentMarginBottom = 8;
		_itemDetailPopup.AddThemeStyleboxOverride("panel", sb);

		var scrollContainer = new ScrollContainer();
		scrollContainer.SetAnchorsPreset(LayoutPreset.FullRect);
		scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_itemDetailPopup.AddChild(scrollContainer);

		var rtl = new RichTextLabel();
		rtl.Name = "DetailText";
		rtl.BbcodeEnabled = true;
		rtl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rtl.SizeFlagsVertical = SizeFlags.ExpandFill;
		rtl.FitContent = true;
		rtl.AddThemeColorOverride("default_color", new Color(0.85f, 0.8f, 0.65f));
		rtl.AddThemeFontSizeOverride("normal_font_size", 12);
		scrollContainer.AddChild(rtl);

		AddChild(_itemDetailPopup);

		// Close on any click
		_itemDetailPopup.GuiInput += (inputEvent) => {
			if (inputEvent is InputEventMouseButton mb && mb.Pressed) {
				_itemDetailPopup.Visible = false;
			}
		};
	}

	// ─── Slot Input Handling (all slots use this) ───────────────────
	private void HandleSlotInput(InputEvent inputEvent, Button btn, int slotId)
	{
		if (inputEvent is InputEventMouseButton mb) {
			if (mb.ButtonIndex == MouseButton.Left && mb.Pressed) {
				if (_heldItem.HasValue) {
					// Place held item into this slot
					PlaceHeldItem(slotId, btn);
				} else if (_slotItemData.TryGetValue(btn, out var itemData)) {
					// Pick up item from this slot
					PickUpItem(itemData, slotId, btn);
				}
			}
			if (mb.ButtonIndex == MouseButton.Right) {
				if (mb.Pressed && _slotItemData.TryGetValue(btn, out var itemData)) {
					_rightClickTimer = 0;
					_rightClickTarget = btn;
					_rightClickItemData = itemData;
				} else if (!mb.Pressed) {
					if (_rightClickTimer >= 0 && _rightClickTimer < 1.0) {
						// Short right-click — TODO: clicky/bag action
					}
					_rightClickTimer = -1;
					_rightClickTarget = null;
				}
			}
		}
	}

	private void PickUpItem(JsonElement itemData, int slotId, Button sourceBtn)
	{
		_heldItem = itemData;
		_heldFromSlotId = slotId;
		_cursorLabel.Text = itemData.GetProperty("itemName").GetString();
		_cursorLabel.Visible = true;
		sourceBtn.Modulate = new Color(0.4f, 0.4f, 0.4f, 0.6f); // dim the source
	}

	private void PlaceHeldItem(int targetSlotId, Button targetBtn)
	{
		if (!_heldItem.HasValue) return;
		int fromSlot = _heldFromSlotId;

		bool targetIsEquipSlot = targetSlotId < 22;
		bool sourceIsEquipSlot = fromSlot < 22;

		if (targetIsEquipSlot && !sourceIsEquipSlot) {
			// Placing from inventory onto equipment slot
			int itemId = _heldItem.Value.GetProperty("item_id").GetInt32();
			_client.SendRaw($"{{\"type\": \"EQUIP_ITEM\", \"itemId\": {itemId}, \"slot\": {targetSlotId}}}");
		} else if (!targetIsEquipSlot && sourceIsEquipSlot) {
			// Placing from equipment to inventory
			int itemId = _heldItem.Value.GetProperty("item_id").GetInt32();
			_client.SendRaw($"{{\"type\": \"UNEQUIP_ITEM\", \"itemId\": {itemId}}}");
		} else if (!targetIsEquipSlot && !sourceIsEquipSlot) {
			// Moving between inventory slots
			_client.SendRaw($"{{\"type\": \"MOVE_ITEM\", \"fromSlot\": {fromSlot}, \"toSlot\": {targetSlotId}}}");
		} else {
			// Equipment to equipment — swap both slots
			int itemId = _heldItem.Value.GetProperty("item_id").GetInt32();
			_client.SendRaw($"{{\"type\": \"EQUIP_ITEM\", \"itemId\": {itemId}, \"slot\": {targetSlotId}}}");
		}

		CancelHeldItem();
	}

	private void CancelHeldItem()
	{
		if (_heldItem.HasValue && _heldFromSlotId >= 0) {
			// Restore source button appearance
			if (_heldFromSlotId < 22) {
				string slotName = MapSlotToName(_heldFromSlotId);
				if (slotName != null && _equipSlots.TryGetValue(slotName, out var btn))
					btn.Modulate = Colors.White;
			} else {
				int idx = _heldFromSlotId - 22;
				if (idx >= 0 && idx < 10) _invSlots[idx].Modulate = Colors.White;
			}
		}
		_heldItem = null;
		_heldFromSlotId = -1;
		_cursorLabel.Visible = false;
	}

	private void ShowItemDetail(JsonElement item, Vector2 pos)
	{
		var rtl = _itemDetailPopup.GetNode<RichTextLabel>("DetailText");
		rtl.Clear();

		string name = item.GetProperty("itemName").GetString();
		rtl.AppendText($"[b][color=#ddaa22]{name}[/color][/b]\n\n");

		// Slot info
		int equipSlot = item.TryGetProperty("equipSlot", out var esProp) ? esProp.GetInt32() : 0;
		if (equipSlot > 0) {
			string slotDesc = GetSlotDescription(equipSlot);
			rtl.AppendText($"[color=#aaaaaa]Slot: {slotDesc}[/color]\n");
		}

		// Stats
		var statLines = new List<string>();
		if (item.TryGetProperty("ac", out var ac) && ac.GetInt32() > 0) statLines.Add($"AC: {ac.GetInt32()}");
		if (item.TryGetProperty("damage", out var dmg) && dmg.GetInt32() > 0) statLines.Add($"Dmg: {dmg.GetInt32()}");
		if (item.TryGetProperty("delay", out var dly) && dly.GetInt32() > 0) statLines.Add($"Delay: {dly.GetInt32()}");
		if (item.TryGetProperty("hp", out var hp) && hp.GetInt32() != 0) statLines.Add($"HP: +{hp.GetInt32()}");
		if (item.TryGetProperty("mana", out var mana) && mana.GetInt32() != 0) statLines.Add($"Mana: +{mana.GetInt32()}");
		if (item.TryGetProperty("str", out var str) && str.GetInt32() != 0) statLines.Add($"STR: +{str.GetInt32()}");
		if (item.TryGetProperty("sta", out var sta) && sta.GetInt32() != 0) statLines.Add($"STA: +{sta.GetInt32()}");
		if (item.TryGetProperty("agi", out var agi) && agi.GetInt32() != 0) statLines.Add($"AGI: +{agi.GetInt32()}");
		if (item.TryGetProperty("dex", out var dex) && dex.GetInt32() != 0) statLines.Add($"DEX: +{dex.GetInt32()}");
		if (item.TryGetProperty("wis", out var wis) && wis.GetInt32() != 0) statLines.Add($"WIS: +{wis.GetInt32()}");
		if (item.TryGetProperty("int", out var intel) && intel.GetInt32() != 0) statLines.Add($"INT: +{intel.GetInt32()}");
		if (item.TryGetProperty("cha", out var cha) && cha.GetInt32() != 0) statLines.Add($"CHA: +{cha.GetInt32()}");

		if (statLines.Count > 0) {
			rtl.AppendText("\n");
			foreach (var line in statLines)
				rtl.AppendText($"[color=#88cc88]{line}[/color]\n");
		}

		// Weight & Value
		float weight = item.TryGetProperty("weight", out var wProp) ? (float)wProp.GetDouble() : 0;
		int value = item.TryGetProperty("value", out var vProp) ? vProp.GetInt32() : 0;
		rtl.AppendText($"\n[color=#aaaaaa]Weight: {weight:F1}[/color]\n");
		if (value > 0) {
			int pp = value / 1000; int gp = (value % 1000) / 100;
			int sp = (value % 100) / 10; int cp = value % 10;
			rtl.AppendText($"[color=#aaaaaa]Value: {pp}pp {gp}gp {sp}sp {cp}cp[/color]\n");
		}

		int sellVal = item.TryGetProperty("sellValue", out var svProp) ? svProp.GetInt32() : 0;
		if (sellVal > 0) {
			rtl.AppendText($"[color=#ccaa44]Sell: {FormatCurrency(sellVal)}[/color]\n");
		}

		_itemDetailPopup.GlobalPosition = pos;
		_itemDetailPopup.Visible = true;
	}

	private string GetSlotDescription(int bitmask)
	{
		var slotNames = new List<string>();
		string[] names = { "Charm", "Ear", "Head", "Face", "Ear", "Neck", "Shoulders", "Arms",
			"Back", "Wrist", "Wrist", "Range", "Hands", "Primary", "Secondary",
			"Ring", "Ring", "Chest", "Legs", "Feet", "Waist", "Ammo" };
		for (int i = 0; i < names.Length && i < 22; i++) {
			if ((bitmask & (1 << i)) != 0) slotNames.Add(names[i]);
		}
		return slotNames.Count > 0 ? string.Join(", ", slotNames) : "None";
	}

	private string BuildItemStatText(JsonElement item)
	{
		var parts = new List<string>();
		if (item.TryGetProperty("damage", out var dmg) && dmg.GetInt32() > 0) parts.Add($"Dmg: {dmg.GetInt32()}");
		if (item.TryGetProperty("delay", out var delay) && delay.GetInt32() > 0) parts.Add($"Dly: {delay.GetInt32()}");
		if (item.TryGetProperty("ac", out var ac) && ac.GetInt32() > 0) parts.Add($"AC: {ac.GetInt32()}");
		if (item.TryGetProperty("hp", out var hp) && hp.GetInt32() > 0) parts.Add($"HP:+{hp.GetInt32()}");
		if (item.TryGetProperty("mana", out var mana) && mana.GetInt32() > 0) parts.Add($"Mana:+{mana.GetInt32()}");
		if (item.TryGetProperty("str", out var str) && str.GetInt32() != 0) parts.Add($"STR:+{str.GetInt32()}");
		if (item.TryGetProperty("sta", out var sta) && sta.GetInt32() != 0) parts.Add($"STA:+{sta.GetInt32()}");
		if (item.TryGetProperty("agi", out var agi) && agi.GetInt32() != 0) parts.Add($"AGI:+{agi.GetInt32()}");
		if (item.TryGetProperty("dex", out var dex) && dex.GetInt32() != 0) parts.Add($"DEX:+{dex.GetInt32()}");
		if (item.TryGetProperty("wis", out var wis) && wis.GetInt32() != 0) parts.Add($"WIS:+{wis.GetInt32()}");
		if (item.TryGetProperty("int", out var intel) && intel.GetInt32() != 0) parts.Add($"INT:+{intel.GetInt32()}");
		if (item.TryGetProperty("cha", out var cha) && cha.GetInt32() != 0) parts.Add($"CHA:+{cha.GetInt32()}");
		return parts.Count > 0 ? string.Join(" | ", parts) : "";
	}

	/// <summary>
	/// Build the equipment paperdoll grid programmatically.
	/// Creates labeled slot rows arranged in pairs for a compact paperdoll look.
	/// </summary>
	private void BuildEquipmentGrid()
	{
		// Define slot layout: each tuple is (leftSlot, rightSlot) for a row
		var rows = new (string left, string right)[] {
			("Head",      "Face"),
			("Neck",      "Ear"),
			("Shoulders", "Chest"),
			("Arms",      "Back"),
			("Wrist",     "Wrist2"),
			("Hands",     "Ring"),
			("Primary",   "Secondary"),
			("Legs",      "Ring2"),
			("Feet",      "Waist"),
			("Range",     "Ammo"),
		};

		foreach (var (left, right) in rows)
		{
			var rowBox = new HBoxContainer();
			rowBox.AddThemeConstantOverride("separation", 4);
			
			AddSlotToRow(rowBox, left);
			AddSlotToRow(rowBox, right);
			
			_equipGrid.AddChild(rowBox);
		}
	}

	private void AddSlotToRow(HBoxContainer row, string slotName)
	{
		var container = new HBoxContainer();
		container.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		container.AddThemeConstantOverride("separation", 2);

		var label = new Label { Text = slotName.Length > 6 ? slotName[..6] : slotName };
		label.CustomMinimumSize = new Vector2(50, 0);
		label.AddThemeFontSizeOverride("font_size", 10);
		label.AddThemeColorOverride("font_color", new Color(0.6f, 0.55f, 0.4f));

		var btn = new Button { Text = "---" };
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		btn.CustomMinimumSize = new Vector2(0, 24);
		btn.AddThemeFontSizeOverride("font_size", 10);
		btn.ClipText = true;

		container.AddChild(label);
		container.AddChild(btn);
		row.AddChild(container);

		_equipSlots[slotName] = btn;
		int numericSlot = NameToSlotId(slotName);
		btn.GuiInput += (ev) => HandleSlotInput(ev, btn, numericSlot);
	}

	private int NameToSlotId(string name) {
		return name switch {
			"Head" => 2, "Face" => 3, "Ear" => 4, "Neck" => 5,
			"Shoulders" => 6, "Arms" => 7, "Back" => 8,
			"Wrist" => 9, "Wrist2" => 10, "Range" => 11,
			"Hands" => 12, "Primary" => 13, "Secondary" => 14,
			"Ring" => 15, "Ring2" => 16, "Chest" => 17,
			"Legs" => 18, "Feet" => 19, "Waist" => 20, "Ammo" => 21,
			_ => 0,
		};
	}

	/// <summary>
	/// Update the inventory window's built-in stats panel with current character data.
	/// Called from OnCharacterStatusReceived.
	/// </summary>
	// Character stats cache to handle partial STATUS updates
	private int _statStr = 0;
	private int _statSta = 0;
	private int _statAgi = 0;
	private int _statDex = 0;
	private int _statWis = 0;
	private int _statInt = 0;
	private int _statCha = 0;
	private int _ac = 0;
	private float _xpPct = 0f;
	private string _cls = "";

	private void UpdateInventoryStats(JsonElement source)
	{
		if (_invStatsText == null) return;
		
		if (source.TryGetProperty("name", out var n)) _charName = n.GetString();
		if (source.TryGetProperty("level", out var l)) _charLevel = l.GetInt32();
		if (source.TryGetProperty("class", out var c)) _cls = c.GetString();
		if (source.TryGetProperty("hp", out var h)) _currentHp = h.GetInt32();
		if (source.TryGetProperty("maxHp", out var mh)) _maxHp = mh.GetInt32();
		if (source.TryGetProperty("mana", out var mn)) _currentMana = mn.GetInt32();
		if (source.TryGetProperty("maxMana", out var mm)) _maxMana = mm.GetInt32();
		if (source.TryGetProperty("ac", out var a)) _ac = a.GetInt32();
		
		if (source.TryGetProperty("str", out var s1)) _statStr = s1.GetInt32();
		if (source.TryGetProperty("sta", out var s2)) _statSta = s2.GetInt32();
		if (source.TryGetProperty("agi", out var s3)) _statAgi = s3.GetInt32();
		if (source.TryGetProperty("dex", out var s4)) _statDex = s4.GetInt32();
		if (source.TryGetProperty("wis", out var s5)) _statWis = s5.GetInt32();
		if (source.TryGetProperty("intel", out var s6)) _statInt = s6.GetInt32();
		if (source.TryGetProperty("cha", out var s7)) _statCha = s7.GetInt32();
		
		if (source.TryGetProperty("xpPercent", out var xp)) _xpPct = (float)xp.GetDouble();

		_invStatsText.Clear();
		_invStatsText.Text = "";
		_invStatsText.AppendText($"[color=#d4a840]{_charName}[/color]\n");
		_invStatsText.AppendText($"{_charLevel}  {_cls}\n\n");
		_invStatsText.AppendText($"[color=#cc4444]HP[/color]  {_currentHp}/{_maxHp}\n");
		_invStatsText.AppendText($"[color=#4488cc]MP[/color]  {_currentMana}/{_maxMana}\n");
		_invStatsText.AppendText($"AC  {_ac}\n\n");
		_invStatsText.AppendText($"[color=#88cc88]STR[/color] {_statStr}\n");
		_invStatsText.AppendText($"[color=#88cc88]STA[/color] {_statSta}\n");
		_invStatsText.AppendText($"[color=#88cc88]AGI[/color] {_statAgi}\n");
		_invStatsText.AppendText($"[color=#88cc88]DEX[/color] {_statDex}\n");
		_invStatsText.AppendText($"[color=#88cc88]WIS[/color] {_statWis}\n");
		_invStatsText.AppendText($"[color=#88cc88]INT[/color] {_statInt}\n");
		_invStatsText.AppendText($"[color=#88cc88]CHA[/color] {_statCha}\n\n");
		_invStatsText.AppendText($"NEXT LVL {_xpPct:F1}%\n");
		_invStatsText.AppendText($"WEIGHT   0\n\n");
		int pp = _copper / 1000;
		int gp = (_copper % 1000) / 100;
		int sp = (_copper % 100) / 10;
		int cp = _copper % 10;
		_invStatsText.AppendText($"[color=#ddaa22]PP[/color]  {pp}\n");
		_invStatsText.AppendText($"[color=#cccccc]GP[/color]  {gp}\n");
		_invStatsText.AppendText($"[color=#aa8855]SP[/color]  {sp}\n");
		_invStatsText.AppendText($"[color=#cc8844]CP[/color]  {cp}\n");
	}

	private string MapSlotToName(int slot) {
		return slot switch {
			2 => "Head",
			3 => "Face",
			4 => "Ear",      // Ear1
			5 => "Neck",
			6 => "Shoulders",
			7 => "Arms",
			8 => "Back",
			9 => "Wrist",    // Wrist1
			10 => "Wrist2",  // Wrist2
			11 => "Ear",     // Ear2 (shares slot)
			12 => "Hands",
			13 => "Primary",
			14 => "Secondary",
			15 => "Ring",     // Ring1
			16 => "Ring2",    // Ring2
			17 => "Chest",
			18 => "Legs",
			19 => "Feet",
			20 => "Waist",
			21 => "Range",
			22 => "Ammo",
			_ => null,
		};
	}

	/// <summary>
	/// Format copper into EQ-style pp/gp/sp/cp string.
	/// </summary>
	private static string FormatCurrency(int copper)
	{
		int pp = copper / 1000;
		int gp = (copper % 1000) / 100;
		int sp = (copper % 100) / 10;
		int cp = copper % 10;
		var parts = new List<string>();
		if (pp > 0) parts.Add($"{pp}pp");
		if (gp > 0) parts.Add($"{gp}gp");
		if (sp > 0) parts.Add($"{sp}sp");
		if (cp > 0 || parts.Count == 0) parts.Add($"{cp}cp");
		return string.Join(" ", parts);
	}

	// â”€â”€â”€ Logging â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
			_currentHp = hp;
			_maxHp = maxHp;
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
		if (source.TryGetProperty("name", out var nProp))
		{
			string charName = nProp.GetString();
			_playerNameLabel.Text = charName;
			_charName = charName;

			// Initialize hotbar manager with character name (only once)
			if (_hotbarManager != null && !string.IsNullOrEmpty(charName))
			{
				_hotbarManager.Init(_client, charName);
			}
		}
		UpdateInventorySkills(source);
	}

	private void UpdateInventorySkills(JsonElement source)
	{
		if (_invSkillsText == null) return;
		if (!source.TryGetProperty("skills", out var skillsProp) || skillsProp.ValueKind != JsonValueKind.Object) return;

		// Vision skill keys that should appear under Racial Traits instead of Skills
		var visionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"normal_vision", "weak_normal_vision", "infravision", "ultravision", "cat_eye", "serpent_sight"
		};

		var sb = new System.Text.StringBuilder();
		sb.AppendLine("[color=#d4a840]-- Racial Traits --[/color]\n");

		// List each vision skill the character possesses
		foreach (var skill in skillsProp.EnumerateObject())
		{
			if (visionKeys.Contains(skill.Name))
			{
				string displayName = FormatSkillName(skill.Name);
				sb.AppendLine($"[color=#88cc88]{displayName}[/color]");
			}
		}

		sb.AppendLine();
		sb.AppendLine("[color=#d4a840]-- Skills --[/color]\n");

		// List non-vision skills
		foreach (var skill in skillsProp.EnumerateObject())
		{
			if (visionKeys.Contains(skill.Name)) continue;
			string skillName = FormatSkillName(skill.Name);
			int val = skill.Value.GetInt32();
			sb.AppendLine($"[color=#88cc88]{skillName}[/color]  {val}");
		}
		_invSkillsText.Text = sb.ToString();
	}

	private void BuildSkillsTab()
	{
		var titleHBox = _inventoryWindow.GetNode<HBoxContainer>("MainVBox/TitleBar/HBox");
		var titleLabel = titleHBox.GetNode<Label>("Title");

		var skillsBtn = new Button { Text = "Skills", ToggleMode = true };
		skillsBtn.CustomMinimumSize = new Vector2(55, 0);
		skillsBtn.AddThemeFontSizeOverride("font_size", 11);
		titleHBox.AddChild(skillsBtn);
		titleHBox.MoveChild(skillsBtn, titleLabel.GetIndex() + 1);

		var mainVBox = _inventoryWindow.GetNode<VBoxContainer>("MainVBox");
		var contentHBox = mainVBox.GetNode<HBoxContainer>("ContentHBox");
		var slotsSection = mainVBox.GetNode<VBoxContainer>("SlotsSection");

		var skillsPanel = new PanelContainer();
		skillsPanel.Name = "SkillsPanel";
		skillsPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		skillsPanel.Visible = false;
		var sbx = new StyleBoxFlat();
		sbx.BgColor = new Color(0.04f, 0.04f, 0.04f, 0.9f);
		sbx.BorderWidthLeft = 1; sbx.BorderWidthTop = 1; sbx.BorderWidthRight = 1; sbx.BorderWidthBottom = 1;
		sbx.BorderColor = new Color(0.3f, 0.25f, 0.15f, 0.7f);
		skillsPanel.AddThemeStyleboxOverride("panel", sbx);

		var skillsScroll = new ScrollContainer();
		skillsScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		skillsPanel.AddChild(skillsScroll);

		_invSkillsText = new RichTextLabel();
		_invSkillsText.BbcodeEnabled = true;
		_invSkillsText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_invSkillsText.SizeFlagsVertical = SizeFlags.ExpandFill;
		_invSkillsText.AddThemeColorOverride("default_color", new Color(0.8f, 0.8f, 0.7f));
		_invSkillsText.AddThemeFontSizeOverride("normal_font_size", 12);
		_invSkillsText.FitContent = true;
		skillsScroll.AddChild(_invSkillsText);

		mainVBox.AddChild(skillsPanel);
		mainVBox.MoveChild(skillsPanel, contentHBox.GetIndex() + 1);

		skillsBtn.Toggled += (toggled) => {
			contentHBox.Visible = !toggled;
			slotsSection.Visible = !toggled;
			skillsPanel.Visible = toggled;
			titleLabel.Text = toggled ? "Skills" : "Inventory";
		};
	}

	private void SetupInventoryDrag()
	{
		var titleBar = _inventoryWindow.GetNode<PanelContainer>("MainVBox/TitleBar");
		titleBar.GuiInput += (inputEvent) => {
			if (inputEvent is InputEventMouseButton mb) {
				if (mb.ButtonIndex == MouseButton.Left) {
					_draggingInventory = mb.Pressed;
					_dragOffset = mb.GlobalPosition - _inventoryWindow.GlobalPosition;
				}
			} else if (inputEvent is InputEventMouseMotion mm && _draggingInventory) {
				_inventoryWindow.GlobalPosition = mm.GlobalPosition - _dragOffset;
			}
		};
	}

	private string FormatSkillName(string key)
	{
		var parts = key.Split('_');
		for (int i = 0; i < parts.Length; i++)
		{
			if (parts[i].Length > 0)
				parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
		}
		return string.Join(" ", parts);
}
}
