using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class MainUI
{
	private Window _petInventoryWindow;
	private Control _petEquipGrid;
	private GridContainer _petSlotsGrid;
	private Dictionary<string, Button> _petEquipSlots = new Dictionary<string, Button>();
	private Button[] _petInvSlots = new Button[8];
	private Dictionary<Button, JsonElement> _petSlotItemData = new Dictionary<Button, JsonElement>();

	private void EnsurePetInventoryWindow()
	{
		if (_petInventoryWindow != null && IsInstanceValid(_petInventoryWindow)) return;

		_petInventoryWindow = new Window();
		_petInventoryWindow.Name = "PetInventoryWindow";
		_petInventoryWindow.Title = "Pet Equipment & Inventory";
		_petInventoryWindow.Size = new Vector2I(300, 520);
		_petInventoryWindow.Position = new Vector2I(300, 100);
		_petInventoryWindow.Visible = false;
		_petInventoryWindow.AlwaysOnTop = true;
		_petInventoryWindow.Unresizable = false;
		_petInventoryWindow.Exclusive = false;
		_petInventoryWindow.CloseRequested += () => _petInventoryWindow.Visible = false;
		AddChild(_petInventoryWindow);

		var panel = new PanelContainer();
		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.04f, 0.06f, 0.10f, 0.97f);
		panelStyle.BorderWidthLeft = panelStyle.BorderWidthTop = panelStyle.BorderWidthRight = panelStyle.BorderWidthBottom = 1;
		panelStyle.BorderColor = new Color(0.5f, 0.45f, 0.2f, 0.8f);
		panel.AddThemeStyleboxOverride("panel", panelStyle);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_petInventoryWindow.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 10);
		panel.AddChild(vbox);

		// Equipment Area (Paperdoll style)
		_petEquipGrid = new Control();
		_petEquipGrid.CustomMinimumSize = new Vector2(240, 400);
		_petEquipGrid.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		vbox.AddChild(_petEquipGrid);
		BuildPetEquipmentGrid(_petEquipGrid);

		// Separator
		var sep = new HSeparator();
		vbox.AddChild(sep);

		// Inventory Slots Area (2 rows of 4)
		_petSlotsGrid = new GridContainer();
		_petSlotsGrid.Columns = 4;
		_petSlotsGrid.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_petSlotsGrid.AddThemeConstantOverride("h_separation", 4);
		_petSlotsGrid.AddThemeConstantOverride("v_separation", 4);
		vbox.AddChild(_petSlotsGrid);

		for (int i = 0; i < 8; i++)
		{
			var btn = new Button();
			btn.Text = "";
			btn.CustomMinimumSize = new Vector2(40, 40);
			btn.AddThemeFontSizeOverride("font_size", 9);
			btn.ClipText = true;
			btn.TooltipText = "Empty";
			
			var style = new StyleBoxFlat();
			style.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.4f);
			style.BorderColor = new Color(0.6f, 0.5f, 0.2f, 0.7f);
			style.BorderWidthBottom = 1;
			style.BorderWidthTop = 1;
			style.BorderWidthLeft = 1;
			style.BorderWidthRight = 1;
			btn.AddThemeStyleboxOverride("normal", style);
			
			_petSlotsGrid.AddChild(btn);
			_petInvSlots[i] = btn;
			
			int slotId = i;
			btn.GuiInput += (ev) => HandlePetSlotInput(ev, btn, "inventory", slotId.ToString());
		}
	}

	private void BuildPetEquipmentGrid(Control parent)
	{
		var slots = new (string name, string id, Vector2 pos)[] {
			// Column 1
			("Ear1",      "ear1",      new Vector2(12, 10)),
			("Chest",     "chest",     new Vector2(12, 60)),
			("Arms",      "arms",      new Vector2(12, 110)),
			("Waist",     "waist",     new Vector2(12, 160)),
			("Wrist1",    "wrists1",   new Vector2(12, 210)),
			("Legs",      "legs",      new Vector2(12, 260)),
			("Primary",   "primary",   new Vector2(12, 360)),
			
			// Column 2
			("Head",      "head",      new Vector2(72, 10)),
			("Hands",     "hands",     new Vector2(72, 260)),
			("Ring1",     "fingers1",  new Vector2(72, 310)),
			("Secondary", "secondary", new Vector2(72, 360)),
			
			// Column 3
			("Face",      "face",      new Vector2(132, 10)),
			("Charm",     "charm",     new Vector2(132, 260)),
			("Ring2",     "fingers2",  new Vector2(132, 310)),
			("Range",     "ranged",    new Vector2(132, 360)),
			
			// Column 4
			("Ear2",      "ear2",      new Vector2(192, 10)),
			("Neck",      "neck",      new Vector2(192, 60)),
			("Back",      "back",      new Vector2(192, 110)),
			("Shoulders", "shoulders", new Vector2(192, 160)),
			("Wrist2",    "wrists2",   new Vector2(192, 210)),
			("Feet",      "feet",      new Vector2(192, 260)),
			("Ammo",      "ammo",      new Vector2(192, 360))
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
			
			var style = new StyleBoxFlat();
			style.BgColor = new Color(0.2f, 0.2f, 0.25f, 0.3f);
			btn.AddThemeStyleboxOverride("normal", style);
			
			parent.AddChild(btn);
			_petEquipSlots[id] = btn;
			btn.GuiInput += (ev) => HandlePetSlotInput(ev, btn, "equip", id);
		}

		// Center class icon animation
		var classIcon = new ClassIconAnim();
		classIcon.Name = "ClassIconAnim";
		classIcon.SetAnchorsPreset(Control.LayoutPreset.Center);
		classIcon.OffsetLeft = -60;
		classIcon.OffsetTop = -130;
		classIcon.OffsetRight = 60;
		classIcon.OffsetBottom = 70;
		classIcon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		classIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		classIcon.HFrames = 4;
		classIcon.VFrames = 2;
		classIcon.Fps = 6.0f;
		classIcon.PingPong = true;
		classIcon.ActiveFrames = 7;
		parent.AddChild(classIcon);
	}

	private void HandlePetSlotInput(InputEvent inputEvent, Button btn, string locType, string locSlot)
	{
		if (inputEvent is InputEventMouseButton mb) {
			if (mb.ButtonIndex == MouseButton.Left && mb.Pressed) {
				if (_heldItem.HasValue) {
					// We CANNOT give an item to the pet this way! 
					// "you have to use the give ui to give a pet an item, you can not place an item from anywhere else inside the pets ui or equipment"
					// We can only MOVE items within the pet.
					int heldFromId = _heldFromSlotId; // Not actually enough to know if it's from pet or player!
					// Let's check if the held item came from the pet.
					// We'll use a hack: if _heldFromSlotId is -100, it's from pet. But wait, how do we track pet source slot?
					// Let's define a new field: private (string type, string slot)? _heldPetSource;
					if (_heldPetSource != null) {
						// Moving within pet
						string moveMsg = $"{{\"type\": \"PET_INVENTORY_ACTION\", \"action\": \"move\", " +
										 $"\"location\": {{\"type\":\"{_heldPetSource.Value.type}\", \"slot\":\"{_heldPetSource.Value.slot}\"}}, " +
										 $"\"destination\": {{\"type\":\"{locType}\", \"slot\":\"{locSlot}\"}}}}";
						_client.SendRaw(moveMsg);
						CancelHeldItem(); // also clears _heldPetSource
					} else {
						Log("SYSTEM", "[color=yellow]You must use the Give window to hand items to your pet.[/color]");
					}
				} else if (_petSlotItemData.TryGetValue(btn, out var itemData)) {
					// Pick up item from this slot
					_heldItem = itemData;
					_heldFromSlotId = -100; // Magic number for "from pet"
					_heldPetSource = (locType, locSlot);

					int pIconId = itemData.TryGetProperty("icon", out var pProp) ? pProp.GetInt32() : 0;
					var iconMgr = IconManager.Instance;
					_cursorIcon.Texture = (pIconId > 0 && iconMgr != null) ? iconMgr.GetItemIcon(pIconId) : null;
					_cursorIcon.Visible = true;
					btn.Modulate = new Color(0.4f, 0.4f, 0.4f, 0.6f); // dim
				}
			}
		}
	}

	private (string type, string slot)? _heldPetSource = null;

	private void OnPetInventoryUpdated(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			
			if (!root.TryGetProperty("equipment", out var equipment) || !root.TryGetProperty("inventory", out var inventory)) return;

			// Reset UI
			_petSlotItemData.Clear();
			foreach (var kvp in _petEquipSlots) {
				kvp.Value.Text = kvp.Key;
				kvp.Value.Icon = null;
				kvp.Value.TooltipText = kvp.Key;
				SetInventorySlotStackOverlay(kvp.Value, 1);
			}
			for (int i = 0; i < 8; i++) {
				if (_petInvSlots[i] != null) {
					_petInvSlots[i].Text = "";
					_petInvSlots[i].Icon = null;
					_petInvSlots[i].TooltipText = "Empty";
					SetInventorySlotStackOverlay(_petInvSlots[i], 1);
				}
			}

			var iconMgr = IconManager.Instance;

			// Populate Equip
			foreach (var prop in equipment.EnumerateObject()) {
				string slotKey = prop.Name;
				var item = prop.Value;
				if (_petEquipSlots.TryGetValue(slotKey, out var btn)) {
					PopulatePetBtn(btn, item, iconMgr);
				}
			}

			// Populate Inventory
			int idx = 0;
			foreach (var item in inventory.EnumerateArray()) {
				if (idx < 8 && _petInvSlots[idx] != null) {
					PopulatePetBtn(_petInvSlots[idx], item, iconMgr);
				}
				idx++;
			}
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Pet Inv Error: {ex.Message}"); }
	}

	private void PopulatePetBtn(Button btn, JsonElement item, IconManager iconMgr) {
		string name = item.TryGetProperty("itemName", out var n) ? n.GetString() : "Item";
		int iconId = item.TryGetProperty("icon", out var iProp) ? iProp.GetInt32() : 0;

		Texture2D iconTex = null;
		if (iconId > 0 && iconMgr != null) iconTex = iconMgr.GetItemIcon(iconId);

		btn.Text = iconTex != null ? "" : (name.Length > 8 ? name[..8] + ".." : name);
		if (iconTex != null) {
			btn.Icon = iconTex;
			btn.ExpandIcon = true;
			btn.IconAlignment = HorizontalAlignment.Center;
		}
		btn.TooltipText = name;
		SetInventorySlotStackOverlay(btn, ReadItemStackCount(item));
		_petSlotItemData[btn] = item.Clone();
	}
}
