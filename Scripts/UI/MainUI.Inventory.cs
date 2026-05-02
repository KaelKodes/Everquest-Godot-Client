using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class MainUI
{
	private Dictionary<int, BagWindow> _openBags = new Dictionary<int, BagWindow>();

	public void CloseBag(int slotId)
	{
		if (_openBags.TryGetValue(slotId, out var win)) {
			if (IsInstanceValid(win)) {
				win.QueueFree();
			}
			_openBags.Remove(slotId);
		}
	}
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
				string disp = kvp.Key;
				if (disp == "Ear2") disp = "Ear";
				if (disp == "Wrist2") disp = "Wrist";
				if (disp == "Ring2") disp = "Ring";
				
				kvp.Value.Text = disp;
				kvp.Value.Icon = null;
				kvp.Value.TooltipText = kvp.Key;
			}

			foreach (var kvp in _openBags) {
				if (IsInstanceValid(kvp.Value)) {
					foreach (var btn in kvp.Value.Slots) {
						btn.Text = "";
						btn.Icon = null;
						btn.TooltipText = "Empty";
						_slotItemData.Remove(btn);
					}
				}
			}

			if (_bankWindow != null && IsInstanceValid(_bankWindow)) {
				foreach (var btn in _bankWindow.BankSlots) {
					btn.Text = "";
					btn.Icon = null;
					btn.TooltipText = "Empty";
					_slotItemData.Remove(btn);
				}
				foreach (var btn in _bankWindow.SharedBankSlots) {
					btn.Text = "";
					btn.Icon = null;
					btn.TooltipText = "Empty";
					_slotItemData.Remove(btn);
				}
			}

			// Reset all 8 general inventory slots
			for (int i = 0; i < 8; i++) {
				if (_invSlots[i] != null) {
					_invSlots[i].Text = "";
					_invSlots[i].Icon = null;
					_invSlots[i].TooltipText = "Empty";
				}
			}

			var iconMgr = IconManager.Instance;
			foreach (var item in inventory.EnumerateArray())
			{
				int slotId = item.GetProperty("slotId").GetInt32();
				if (slotId == 30) {
					_heldItem = item.Clone();
					_heldFromSlotId = 30; // Special cursor slot
					int cIconId = item.TryGetProperty("icon", out var cIProp) ? cIProp.GetInt32() : 0;
					_cursorIcon.Texture = (cIconId > 0 && iconMgr != null) ? iconMgr.GetItemIcon(cIconId) : null;
					_cursorIcon.Visible = true;
					continue;
				}
				string name = item.GetProperty("itemName").GetString();
				int equipped = item.GetProperty("equipped").GetInt32();
				int iconId = item.TryGetProperty("icon", out var iProp) ? iProp.GetInt32() : 0;
				string statText = BuildItemStatText(item);

				Texture2D iconTex = null;
				if (iconId > 0 && iconMgr != null) {
					iconTex = iconMgr.GetItemIcon(iconId);
				}

				if (equipped == 1) {
					string slotName = MapSlotToName(slotId);
					if (slotName != null && _equipSlots.TryGetValue(slotName, out var slotBtn)) {
						slotBtn.Text = iconTex != null ? "" : (name.Length > 8 ? name[..8] + ".." : name);
						if (iconTex != null) {
							slotBtn.Icon = iconTex;
							slotBtn.ExpandIcon = true;
							slotBtn.IconAlignment = HorizontalAlignment.Center;
						}
						slotBtn.TooltipText = $"{name}\n{statText}";
						_slotItemData[slotBtn] = item.Clone();
					}
				} else if (slotId >= 251 && slotId <= 330) {
					int bagIdx = (slotId - 251) / 10;
					int parentSlotId = 22 + bagIdx;
					if (_openBags.TryGetValue(parentSlotId, out var win) && IsInstanceValid(win)) {
						int internalIdx = slotId - (251 + (bagIdx * 10));
						if (internalIdx >= 0 && internalIdx < win.Slots.Length) {
							var btn = win.Slots[internalIdx];
							btn.Text = iconTex != null ? "" : (name.Length > 12 ? name[..12] + ".." : name);
							if (iconTex != null) {
								btn.Icon = iconTex;
								btn.ExpandIcon = true;
								btn.IconAlignment = HorizontalAlignment.Center;
							}
							int sellVal = item.TryGetProperty("sellValue", out var svProp) ? svProp.GetInt32() : 0;
							string sellText = sellVal > 0 ? $"\nSell: {FormatCurrency(sellVal)}" : "";
							btn.TooltipText = $"{name}\n{statText}{sellText}";
							_slotItemData[btn] = item.Clone();
						}
					}
				} else if (slotId >= 2531 && slotId <= 2770) {
					int bagIdx = (slotId - 2531) / 10;
					int parentSlotId = 2000 + bagIdx;
					if (_openBags.TryGetValue(parentSlotId, out var win) && IsInstanceValid(win)) {
						int internalIdx = slotId - (2531 + (bagIdx * 10));
						if (internalIdx >= 0 && internalIdx < win.Slots.Length) {
							var btn = win.Slots[internalIdx];
							btn.Text = iconTex != null ? "" : (name.Length > 12 ? name[..12] + ".." : name);
							if (iconTex != null) {
								btn.Icon = iconTex;
								btn.ExpandIcon = true;
								btn.IconAlignment = HorizontalAlignment.Center;
							}
							btn.TooltipText = $"{name}\n{statText}";
							_slotItemData[btn] = item.Clone();
						}
					}
				} else if (slotId >= 2511 && slotId <= 2590) {
					int bagIdx = (slotId - 2511) / 10;
					int parentSlotId = 2500 + bagIdx;
					if (_openBags.TryGetValue(parentSlotId, out var win) && IsInstanceValid(win)) {
						int internalIdx = slotId - (2511 + (bagIdx * 10));
						if (internalIdx >= 0 && internalIdx < win.Slots.Length) {
							var btn = win.Slots[internalIdx];
							btn.Text = iconTex != null ? "" : (name.Length > 12 ? name[..12] + ".." : name);
							if (iconTex != null) {
								btn.Icon = iconTex;
								btn.ExpandIcon = true;
								btn.IconAlignment = HorizontalAlignment.Center;
							}
							btn.TooltipText = $"{name}\n{statText}";
							_slotItemData[btn] = item.Clone();
						}
					}
				} else if (slotId >= 2000 && slotId <= 2023) {
					if (_bankWindow != null && IsInstanceValid(_bankWindow)) {
						int idx = slotId - 2000;
						if (idx >= 0 && idx < 24) {
							var btn = _bankWindow.BankSlots[idx];
							btn.Text = iconTex != null ? "" : (name.Length > 12 ? name[..12] + ".." : name);
							if (iconTex != null) {
								btn.Icon = iconTex;
								btn.ExpandIcon = true;
								btn.IconAlignment = HorizontalAlignment.Center;
							}
							btn.TooltipText = $"{name}\n{statText}";
							_slotItemData[btn] = item.Clone();
						}
					}
				} else if (slotId >= 2500 && slotId <= 2507) {
					if (_bankWindow != null && IsInstanceValid(_bankWindow)) {
						int idx = slotId - 2500;
						if (idx >= 0 && idx < 8) {
							var btn = _bankWindow.SharedBankSlots[idx];
							btn.Text = iconTex != null ? "" : (name.Length > 12 ? name[..12] + ".." : name);
							if (iconTex != null) {
								btn.Icon = iconTex;
								btn.ExpandIcon = true;
								btn.IconAlignment = HorizontalAlignment.Center;
							}
							btn.TooltipText = $"{name}\n{statText}";
							_slotItemData[btn] = item.Clone();
						}
					}
				} else {
					int idx = slotId - 22;
					if (idx >= 0 && idx < 8) {
						var btn = _invSlots[idx];
						if (btn != null) {
							btn.Text = iconTex != null ? "" : (name.Length > 12 ? name[..12] + ".." : name);
							if (iconTex != null) {
								btn.Icon = iconTex;
								btn.ExpandIcon = true;
								btn.IconAlignment = HorizontalAlignment.Center;
							}
							
							int sellVal = item.TryGetProperty("sellValue", out var svProp) ? svProp.GetInt32() : 0;
							string sellText = sellVal > 0 ? $"\nSell: {FormatCurrency(sellVal)}" : "";
							btn.TooltipText = $"{name}\n{statText}{sellText}";
							_slotItemData[btn] = item.Clone();
						}
					}
				}
			}
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Inv Error: {ex.Message}"); }
		
		SyncInventorySlotsWithGiveNPC();
	}

	public void SyncInventorySlotsWithGiveNPC()
	{
		// Gather inst_ids currently in the Give Window
		var giveWindowInstIds = new HashSet<int>();
		if (_giveNPCWindow != null && _giveNPCWindow.Visible)
		{
			for (int i = 0; i < 4; i++)
			{
				if (_giveNPCItemData[i].HasValue)
				{
					if (_giveNPCItemData[i].Value.TryGetProperty("item_id", out var instIdProp))
					{
						giveWindowInstIds.Add(instIdProp.GetInt32());
					}
				}
			}
		}

		// Check all active buttons in _slotItemData
		foreach (var kvp in _slotItemData)
		{
			var btn = kvp.Key;
			var itemData = kvp.Value;
			if (itemData.TryGetProperty("item_id", out var instIdProp))
			{
				int instId = instIdProp.GetInt32();
				// If the item is in the Give Window, or it's currently on the cursor, dim it
				if (giveWindowInstIds.Contains(instId))
				{
					btn.Modulate = new Color(0.4f, 0.4f, 0.4f, 0.6f);
				}
				else
				{
					// Avoid un-dimming if it's the item we are currently holding on the cursor
					if (_heldItem.HasValue && _heldItem.Value.TryGetProperty("item_id", out var heldInst) && heldInst.GetInt32() == instId)
					{
						btn.Modulate = new Color(0.4f, 0.4f, 0.4f, 0.6f);
					}
					else
					{
						btn.Modulate = Colors.White;
					}
				}
			}
		}
	}

	/// <summary>
	/// Create 10 identical slot buttons in the inventory grid (2 columns, 5 rows).
	/// </summary>
	private void BuildInventorySlots()
	{
		if (_slotsGrid == null) return;
		for (int i = 0; i < 8; i++)
		{
			var btn = new Button();
			btn.Text = "";
			btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btn.CustomMinimumSize = new Vector2(0, 40);
			btn.AddThemeFontSizeOverride("font_size", 9);
			btn.ClipText = true;
			btn.TooltipText = "Empty";
			
			// Gold outlined slot style
			var style = new StyleBoxFlat();
			style.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.4f);
			style.BorderColor = new Color(0.6f, 0.5f, 0.2f, 0.7f);
			style.BorderWidthBottom = 1;
			style.BorderWidthTop = 1;
			style.BorderWidthLeft = 1;
			style.BorderWidthRight = 1;
			btn.AddThemeStyleboxOverride("normal", style);
			
			_slotsGrid.AddChild(btn);
			_invSlots[i] = btn;
			int slotId = 22 + i;
			btn.GuiInput += (ev) => HandleSlotInput(ev, btn, slotId);
		}
	}

	// ─── Auto-Equip Slot ────────────────────────────────────────────
	private void BuildAutoEquipSlot()
	{
		// Overlay an invisible button on the class icon area for auto-equip drop target
		var paperdollPanel = _inventoryWindow.GetNodeOrNull<Control>("MainVBox/TabContainer/Inventory/PaperdollColumn/PaperdollPanel");
		if (paperdollPanel == null) return;

		_autoEquipBtn = new Button();
		_autoEquipBtn.Text = "";
		_autoEquipBtn.Flat = true;
		_autoEquipBtn.TooltipText = "Drop a held item here to auto-equip it";
		_autoEquipBtn.MouseFilter = Control.MouseFilterEnum.Pass;

		// Position over the class icon area (centered in paperdoll)
		_autoEquipBtn.SetAnchorsPreset(Control.LayoutPreset.Center);
		_autoEquipBtn.OffsetLeft = -60;
		_autoEquipBtn.OffsetTop = -130;
		_autoEquipBtn.OffsetRight = 60;
		_autoEquipBtn.OffsetBottom = 70;

		paperdollPanel.AddChild(_autoEquipBtn);

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
	private void BuildCursorIcon()
	{
		_cursorIcon = new TextureRect();
		_cursorIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_cursorIcon.CustomMinimumSize = new Vector2(40, 40);
		_cursorIcon.Size = new Vector2(40, 40);
		_cursorIcon.Visible = false;
		_cursorIcon.ZIndex = 100;
		_cursorIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
		_cursorIcon.TopLevel = true;
		AddChild(_cursorIcon);
	}

	// ─── Item Detail Popup ──────────────────────────────────────────
	private void BuildItemDetailPopup()
	{
		_itemDetailPopup = new ItemDetailsWindow();
		AddChild(_itemDetailPopup);
	}

	// ─── Slot Input Handling (all slots use this) ───────────────────
	public void HandleSlotInput(InputEvent inputEvent, Button btn, int slotId)
	{
		if (inputEvent is InputEventMouseButton mb) {
			if (mb.ButtonIndex == MouseButton.Left && mb.Pressed) {
				// Merchant Selling Logic Check
				if (_merchantWindow != null && _merchantWindow.Visible) {
					if (slotId < 22) {
						Log("SYSTEM", "[color=yellow]You must unequip that item before selling it.[/color]");
					} else if (_slotItemData.TryGetValue(btn, out var itemData)) {
						if (itemData.TryGetProperty("item_id", out var instIdProp)) {
							int instId = instIdProp.GetInt32();
							if (!string.IsNullOrEmpty(_activeMerchantId)) {
								_client.SendRaw($"{{\"type\": \"GET_OFFER\", \"npcId\": \"{_activeMerchantId}\", \"itemId\": {instId}}}");
							}
						}
					}
					return; // Block normal inventory dragging
				}

				if (_heldItem.HasValue) {
					// Place held item into this slot
					PlaceHeldItem(slotId, btn);
				} else if (_slotItemData.TryGetValue(btn, out var itemData)) {
					// Pick up item from this slot
					PickUpItem(itemData, slotId, btn);
				}
			}
			if (mb.ButtonIndex == MouseButton.Right) {
				if (mb.Pressed && mb.CtrlPressed && _slotItemData.TryGetValue(btn, out var pickupData)) {
					int clicky = pickupData.TryGetProperty("clicky", out var clickyProp) ? clickyProp.GetInt32() : 0;
					if (clicky > 0) {
						int itemId = pickupData.GetProperty("eq_item_id").GetInt32();
						string name = pickupData.GetProperty("itemName").GetString();
						_hotbarManager?.StartItemDrag(itemId, name);
					}
					GetViewport().SetInputAsHandled();
					return;
				}
				if (mb.Pressed && _slotItemData.TryGetValue(btn, out var itemData)) {
					_rightClickTimer = 0;
					_rightClickTarget = btn;
					_rightClickItemData = itemData;
				} else if (!mb.Pressed) {
					if (_rightClickTimer >= 0 && _rightClickTimer < 1.0) {
						// Short right-click — open bag
						if (_rightClickItemData.HasValue && _rightClickItemData.Value.TryGetProperty("itemtype", out var itProp) && itProp.GetInt32() == 1) {
							// Open bag window if it's a container
							int bagslots = _rightClickItemData.Value.TryGetProperty("bagslots", out var bsProp) ? bsProp.GetInt32() : 8;
							string bagName = _rightClickItemData.Value.TryGetProperty("itemName", out var nProp) ? nProp.GetString() : "Bag";
							if (!_openBags.ContainsKey(slotId)) {
								var bagWin = new BagWindow();
								AddChild(bagWin);
								bagWin.Init(slotId, bagslots, bagName);
								_openBags[slotId] = bagWin;
								
								// Force inventory refresh so it populates immediately
								_client.SendRaw("{\"type\": \"GET_INVENTORY\"}");
							} else {
								CloseBag(slotId);
							}
						}
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
		int pIconId = itemData.TryGetProperty("icon", out var pProp) ? pProp.GetInt32() : 0;
		var iconMgr2 = IconManager.Instance;
		_cursorIcon.Texture = (pIconId > 0 && iconMgr2 != null) ? iconMgr2.GetItemIcon(pIconId) : null;
		_cursorIcon.Visible = true;
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
			_client.SendRaw($"{{\"type\": \"UNEQUIP_ITEM\", \"itemId\": {itemId}, \"slot\": {targetSlotId}}}");
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
		_cursorIcon.Visible = false;
	}

	private void ShowItemDetail(JsonElement item, Vector2 pos)
	{
		_itemDetailPopup.ShowItem(item, pos);
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

	private void BuildEquipmentGrid()
	{
		if (_equipGrid == null) return;
		
		// Define slot layout for paperdoll based on user template
		// 4 columns x 8 rows. Paperdoll size is 240x400.
		// Col X: 12, 72, 132, 192
		// Row Y: 10, 60, 110, 160, 210, 260, 310, 360
		var slots = new (string name, int id, Vector2 pos)[] {
			// Column 1
			("Ear1",      4,  new Vector2(12, 10)),
			("Chest",     17, new Vector2(12, 60)),
			("Arms",      7,  new Vector2(12, 110)),
			("Waist",     20, new Vector2(12, 160)),
			("Wrist1",    9,  new Vector2(12, 210)),
			("Legs",      18, new Vector2(12, 260)),
			("Primary",   13, new Vector2(12, 360)),
			
			// Column 2
			("Head",      2,  new Vector2(72, 10)),
			("Hands",     12, new Vector2(72, 260)),
			("Ring1",     15, new Vector2(72, 310)),
			("Secondary", 14, new Vector2(72, 360)),
			
			// Column 3
			("Face",      3,  new Vector2(132, 10)),
			("Charm",     1,  new Vector2(132, 260)),
			("Ring2",     16, new Vector2(132, 310)),
			("Range",     21, new Vector2(132, 360)),
			
			// Column 4
			("Ear2",      11, new Vector2(192, 10)),
			("Neck",      5,  new Vector2(192, 60)),
			("Back",      8,  new Vector2(192, 110)),
			("Shoulders", 6,  new Vector2(192, 160)),
			("Wrist2",    10, new Vector2(192, 210)),
			("Feet",      19, new Vector2(192, 260)),
			("Ammo",      22, new Vector2(192, 360))
		};

		foreach (var (name, id, pos) in slots)
		{
			var btn = new Button();
			btn.Name = name;
			btn.Text = "";
			btn.CustomMinimumSize = new Vector2(36, 36);
			btn.Position = pos;
			btn.TooltipText = name;
			btn.AddThemeFontSizeOverride("font_size", 8);
			
			// Shadow background placeholder
			var style = new StyleBoxFlat();
			style.BgColor = new Color(0.2f, 0.2f, 0.25f, 0.3f);
			btn.AddThemeStyleboxOverride("normal", style);
			
			_equipGrid.AddChild(btn);
			_equipSlots[name] = btn;
			btn.GuiInput += (ev) => HandleSlotInput(ev, btn, id);
		}

		// Replace static ClassIcon with ClassIconAnim script component
		var oldClassIcon = _inventoryWindow.GetNodeOrNull<TextureRect>("MainVBox/TabContainer/Inventory/PaperdollColumn/PaperdollPanel/ClassIcon");
		if (oldClassIcon != null) {
			var parent = oldClassIcon.GetParent();
			var newClassIcon = new ClassIconAnim();
			newClassIcon.Name = "ClassIconAnim";
			
			// Center it in the Paperdoll panel
			newClassIcon.SetAnchorsPreset(Control.LayoutPreset.Center);
			newClassIcon.OffsetLeft = -60;
			newClassIcon.OffsetTop = -130;
			newClassIcon.OffsetRight = 60;
			newClassIcon.OffsetBottom = 70;
			
			newClassIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			newClassIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			
			// Note: The user can tweak HFrames/VFrames in code if it turns out to be e.g. 5 frames.
			newClassIcon.HFrames = 4;
			newClassIcon.VFrames = 2;
			newClassIcon.Fps = 6.0f;
			newClassIcon.PingPong = true;
			newClassIcon.ActiveFrames = 7;
			
			parent.AddChild(newClassIcon);
			oldClassIcon.QueueFree();
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

		var btn = new Button { Text = slotName.Replace("2", "") };
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
			"Ear" => 1, "Head" => 2, "Face" => 3, "Ear2" => 4,
			"Neck" => 5, "Shoulders" => 6, "Arms" => 7, "Back" => 8,
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
	private int _statDmg = 0;
	private int _statDly = 0;
	private int _statOffhandDmg = 0;
	private int _statOffhandDly = 0;
	private int _ac, _mitigationAC, _avoidanceAC;
	private float _xpPct = 0f;
	private string _cls = "";

	private void UpdateInventoryStats(JsonElement source)
	{
		if (_invStatsText == null) return;
		
		if (source.TryGetProperty("name", out var n)) _charName = n.GetString();
		if (source.TryGetProperty("level", out var l)) _charLevel = l.GetInt32();
		if (source.TryGetProperty("class", out var c)) {
			string rawCls = c.GetString();
			_cls = string.IsNullOrEmpty(rawCls) ? "" : char.ToUpper(rawCls[0]) + rawCls.Substring(1);
		}
		if (source.TryGetProperty("raceId", out var r) || source.TryGetProperty("race", out r)) {
			_raceId = r.ValueKind == System.Text.Json.JsonValueKind.Number ? r.GetInt32() : 1;
		}
		if (source.TryGetProperty("hp", out var h)) _currentHp = h.GetDouble();
		if (source.TryGetProperty("maxHp", out var mh)) _maxHp = mh.GetDouble();
		if (source.TryGetProperty("mana", out var mn)) _currentMana = mn.GetDouble();
		if (source.TryGetProperty("maxMana", out var mm)) _maxMana = mm.GetDouble();
		if (source.TryGetProperty("ac", out var aProp)) { _ac = aProp.GetInt32(); }
		if (source.TryGetProperty("mitigationAC", out var mAcProp)) { _mitigationAC = mAcProp.GetInt32(); }
		if (source.TryGetProperty("avoidanceAC", out var aAcProp)) { _avoidanceAC = aAcProp.GetInt32(); }
		
		if (source.TryGetProperty("str", out var s1)) _statStr = s1.GetInt32();
		if (source.TryGetProperty("sta", out var s2)) _statSta = s2.GetInt32();
		if (source.TryGetProperty("agi", out var s3)) _statAgi = s3.GetInt32();
		if (source.TryGetProperty("dex", out var s4)) _statDex = s4.GetInt32();
		if (source.TryGetProperty("wis", out var s5)) _statWis = s5.GetInt32();
		if (source.TryGetProperty("intel", out var s6)) _statInt = s6.GetInt32();
		if (source.TryGetProperty("cha", out var s7)) _statCha = s7.GetInt32();
		
		if (source.TryGetProperty("dmg", out var d1)) _statDmg = d1.GetInt32();
		if (source.TryGetProperty("dly", out var d2)) _statDly = d2.GetInt32();
		if (source.TryGetProperty("offhandDmg", out var d3)) _statOffhandDmg = d3.GetInt32();
		if (source.TryGetProperty("offhandDly", out var d4)) _statOffhandDly = d4.GetInt32();
		
		if (source.TryGetProperty("xpPercent", out var xp)) _xpPct = (float)xp.GetDouble();
		if (source.TryGetProperty("copper", out var cProp)) _copper = cProp.GetInt32();

		_invStatsText.Clear();
		_invStatsText.AppendText($"[b]{_charName}[/b]\n");
		_invStatsText.AppendText($"{_charLevel}   {_cls}\n");
		_invStatsText.AppendText($"Tunare\n"); // Placeholder deity
		
		_invStatsText.PushTable(2);
		_invStatsText.SetTableColumnExpand(0, true, 1);
		_invStatsText.SetTableColumnExpand(1, true, 1);

		void AddRow(string label, string value, bool bold = false) {
			_invStatsText.PushCell();
			if (bold) _invStatsText.PushBold();
			_invStatsText.AppendText(label);
			if (bold) _invStatsText.Pop();
			_invStatsText.Pop(); // cell
			_invStatsText.PushCell();
			_invStatsText.PushParagraph(HorizontalAlignment.Right);
			_invStatsText.AppendText(value);
			_invStatsText.Pop(); // paragraph
			_invStatsText.Pop(); // cell
		}

		AddRow("HP", $"{_currentHp}/{_maxHp}");
		AddRow("MP", $"{_currentMana}/{_maxMana}");
		AddRow("EN", $"{(int)_currentEndurance}/100");
		AddRow("AC", $"{_ac}");
		AddRow("Mitigation", $"{_mitigationAC}");
		AddRow("Avoidance", $"{_avoidanceAC}");
		AddRow("ATK", "0");
		AddRow("DMG", $"{_statDmg}");
		AddRow("DLY", $"{_statDly}");
		AddRow("STR", $"{_statStr}");
		AddRow("STA", $"{_statSta}");
		AddRow("AGI", $"{_statAgi}");
		AddRow("DEX", $"{_statDex}");
		AddRow("WIS", $"{_statWis}");
		AddRow("INT", $"{_statInt}");
		AddRow("CHA", $"{_statCha}");
		AddRow("POISON", "0");
		AddRow("MAGIC", "0");
		AddRow("DISEASE", "0");
		AddRow("FIRE", "0");
		AddRow("COLD", "0");
		AddRow("CORRUPT", "0");
		AddRow("WEIGHT", "0/100");

		_invStatsText.Pop(); // table
		
		UpdateDetailedStats(source);
		UpdateCurrencyDisplay();
	}

	private void UpdateDetailedStats(JsonElement source)
	{
		if (_detailedStatsText == null) return;
		_detailedStatsText.Clear();
		if (_detailedStatsRight != null) _detailedStatsRight.Clear();

		// Helper: add a stat row to a given RichTextLabel
		void AddRow(RichTextLabel rt, string label, string value) {
			rt.PushCell();
			rt.AppendText(label);
			rt.Pop(); // cell
			rt.PushCell();
			rt.PushParagraph(HorizontalAlignment.Right);
			rt.AppendText(value);
			rt.Pop(); // paragraph
			rt.Pop(); // cell
		}

		// Helper: add a section header
		void AddHeader(RichTextLabel rt, string title) {
			rt.PushCell();
			rt.PushBold();
			rt.PushColor(new Color(0.7f, 0.7f, 0.7f));
			rt.AppendText(title);
			rt.Pop(); // color
			rt.Pop(); // bold
			rt.Pop(); // cell
			rt.PushCell();
			rt.AppendText("");
			rt.Pop(); // cell
		}

		// Helper: start a 2-column table
		void StartTable(RichTextLabel rt) {
			rt.PushTable(2);
			rt.SetTableColumnExpand(0, true, 1);
			rt.SetTableColumnExpand(1, true, 1);
		}

		// ═══════ LEFT COLUMN ═══════
		var L = _detailedStatsText;
		StartTable(L);

		AddRow(L, "HP", $"{_currentHp}/{_maxHp}");
		AddRow(L, "Mana", $"{_currentMana}/{_maxMana}");
		AddRow(L, "Endurance", $"{(int)_currentEndurance}/100");
		AddRow(L, "Armor Class", $"{_ac}");
		AddRow(L, "Attack", "0");
		AddRow(L, "Damage", $"{_statDmg}");
		AddRow(L, "Delay", $"{_statDly}");
		if (_statOffhandDmg > 0) {
			AddRow(L, "Offhand Dmg", $"{_statOffhandDmg}");
			AddRow(L, "Offhand Dly", $"{_statOffhandDly}");
		}
		AddRow(L, "Haste", "0%");
		AddRow(L, "Velocity", "0");

		AddHeader(L, "Regen");
		AddRow(L, "Combat HP Regen", "0");
		AddRow(L, "Combat Mana Regen", "0");
		AddRow(L, "Combat End Regen", "0");

		AddHeader(L, "Stats");
		AddRow(L, "Strength", $"{_statStr}");
		AddRow(L, "Stamina", $"{_statSta}");
		AddRow(L, "Intelligence", $"{_statInt}");
		AddRow(L, "Wisdom", $"{_statWis}");
		AddRow(L, "Agility", $"{_statAgi}");
		AddRow(L, "Dexterity", $"{_statDex}");
		AddRow(L, "Charisma", $"{_statCha}");

		AddHeader(L, "Resists");
		AddRow(L, "Magic", "0/550");
		AddRow(L, "Fire", "0/550");
		AddRow(L, "Cold", "0/550");
		AddRow(L, "Disease", "0/500");
		AddRow(L, "Poison", "0/500");
		AddRow(L, "Corruption", "0/500");

		L.Pop(); // table

		// ═══════ RIGHT COLUMN ═══════
		var R = _detailedStatsRight ?? _detailedStatsText; // fallback
		if (R != _detailedStatsText) {
			StartTable(R);

			AddHeader(R, "Heroic Mods");
			AddRow(R, "Accuracy", "0/150");
			AddRow(R, "Avoidance", "0/100");
			AddRow(R, "Combat Effects", "0/100");
			AddRow(R, "Damage Shielding", "0/35");
			AddRow(R, "Dmg Shield Mitig.", "0/25");
			AddRow(R, "DoT Shielding", "0/35");
			AddRow(R, "Melee Shielding", "0/50");
			AddRow(R, "Spell Shielding", "0/35");
			AddRow(R, "Strike Through", "0/35");
			AddRow(R, "Stun Resist", "0/35");

			AddHeader(R, "Spell Mods");
			AddRow(R, "Heal Amount", "0");
			AddRow(R, "Spell Damage", "0");
			AddRow(R, "Clairvoyance", "0");
			AddRow(R, "Luck", "0");

			AddHeader(R, "Skill Damage Mod");
			AddRow(R, "Bash", "0/100");
			AddRow(R, "Backstab", "0/125");
			AddRow(R, "Dragon Punch", "0/100");
			AddRow(R, "Eagle Strike", "0/100");
			AddRow(R, "Flying Kick", "0/100");
			AddRow(R, "Frenzy", "0/125");
			AddRow(R, "Kick", "0/100");
			AddRow(R, "Round Kick", "0/100");
			AddRow(R, "Tiger Claw", "0/100");

			AddHeader(R, "Vision");
			AddRow(R, "Ultravision", "No");
			AddRow(R, "Infravision", "No");
			AddRow(R, "See Invisible", "No");

			R.Pop(); // table
		}
	}

	private void UpdateCurrencyDisplay()
	{
		int value = _copper;
		int pp = value / 1000;
		int gp = (value % 1000) / 100;
		int sp = (value % 100) / 10;
		int cp = value % 10;
		
		if (_currencyLabels[0] != null) _currencyLabels[0].Text = pp.ToString();
		if (_currencyLabels[1] != null) _currencyLabels[1].Text = gp.ToString();
		if (_currencyLabels[2] != null) _currencyLabels[2].Text = sp.ToString();
		if (_currencyLabels[3] != null) _currencyLabels[3].Text = cp.ToString();
	}

	private string MapSlotToName(int slot) {
		return slot switch {
			1 => "Ear",      // Ear1
			2 => "Head",
			3 => "Face",
			4 => "Ear2",     // Ear2
			5 => "Neck",
			6 => "Shoulders",
			7 => "Arms",
			8 => "Back",
			9 => "Wrist",    // Wrist1
			10 => "Wrist2",  // Wrist2
			11 => "Range",
			12 => "Hands",
			13 => "Primary",
			14 => "Secondary",
			15 => "Ring",    // Ring1
			16 => "Ring2",   // Ring2
			17 => "Chest",
			18 => "Legs",
			19 => "Feet",
			20 => "Waist",
			21 => "Ammo",
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

		double fatigue = 0;
		if (source.TryGetProperty("fatigue", out var fProp)) { fatigue = fProp.GetDouble(); }
		if (_enduranceBar != null)
		{
			_enduranceBar.MaxValue = 100;
			double val = Math.Max(0, 100 - fatigue);
			_enduranceBar.Value = val;
			_currentEndurance = val;
			if (_enduranceLabel != null) {
				_enduranceLabel.Text = $"END: {(int)val}/100";
			}
		}

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
		
		if (source.TryGetProperty("class", out var classProp))
		{
			string className = classProp.GetString();
			if (!string.IsNullOrEmpty(className)) {
				var path = $"res://Assets/UI/ClassicUI/{className}01.tga";
				if (ResourceLoader.Exists(path)) {
					var tex = GD.Load<Texture2D>(path);
					var classIcon = _inventoryWindow.GetNodeOrNull<ClassIconAnim>("MainVBox/TabContainer/Inventory/PaperdollColumn/PaperdollPanel/ClassIconAnim");
					if (classIcon != null) {
						classIcon.SetTextureSheet(tex);
					}
				}
			}
		}
		
		UpdateInventorySkills(source);
	}

	private void UpdateInventorySkills(JsonElement source)
	{
		if (_skillsListContainer == null) return;
		if (!source.TryGetProperty("skills", out var skillsProp) || skillsProp.ValueKind != JsonValueKind.Object) return;

		// Clear existing skills
		foreach (Node child in _skillsListContainer.GetChildren())
		{
			child.QueueFree();
		}

		// Sort skills alphabetically for display
		var sortedSkills = new List<(string Name, int Value)>();
		foreach (var skill in skillsProp.EnumerateObject())
		{
			int val = skill.Value.GetInt32();
			sortedSkills.Add((skill.Name, val));
			
			if (skill.Name == "swimming")
			{
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null)
				{
					wm.PlayerSwimmingSkill = val;
				}
			}
		}
		sortedSkills.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

		foreach (var skill in sortedSkills)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 0);

			var pad = new Control { CustomMinimumSize = new Vector2(6, 0) };
			row.AddChild(pad);

			var nameLbl = new Label();
			nameLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			nameLbl.Text = FormatSkillName(skill.Name);
			nameLbl.AddThemeFontSizeOverride("font_size", 11);
			nameLbl.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
			row.AddChild(nameLbl);

			var rankLbl = new Label();
			rankLbl.CustomMinimumSize = new Vector2(80, 0);
			rankLbl.HorizontalAlignment = HorizontalAlignment.Center;
			rankLbl.Text = "Average"; // Placeholder for rank calculation
			rankLbl.AddThemeFontSizeOverride("font_size", 11);
			rankLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
			row.AddChild(rankLbl);

			var valLbl = new Label();
			valLbl.CustomMinimumSize = new Vector2(60, 0);
			valLbl.HorizontalAlignment = HorizontalAlignment.Center;
			valLbl.Text = skill.Value.ToString() + "/255"; // Placeholder max
			valLbl.AddThemeFontSizeOverride("font_size", 11);
			valLbl.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
			row.AddChild(valLbl);

			_skillsListContainer.AddChild(row);
		}
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
