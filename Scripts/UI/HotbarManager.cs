using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Central manager for all hotbars, social library, drag-drop routing, keyboard shortcuts,
/// and local file persistence. Lives as a child of MainUI.
/// </summary>
public partial class HotbarManager : Control
{
	// ── Dependencies ────────────────────────────────────────────────

	private GameClient _client;
	private SocialManager _socialManager;

	// Spell data callback (set by MainUI)
	public Func<int, int> GetSpellIdForSlot;
	public Func<int, string> GetSpellNameForSlot;

	// Target data callbacks (set by MainUI)
	public Func<string> GetTargetName;

	// Item equip callback (set by MainUI)
	public Action<int> EquipItemById;

	// Auto attack toggle callback
	public Action ToggleAutoAttack;

	// ── State ───────────────────────────────────────────────────────

	private List<Hotbar> _hotbars = new();
	private const int MAX_HOTBARS = 10;
	private string _characterName = "";
	private bool _initialized = false;

	// Drag state for hotbutton placement
	private bool _isDragging = false;
	private Hotbar.HotbuttonData _dragData;
	public bool IsDragging => _isDragging;
	public Hotbar.HotbuttonData DragData => _dragData;
	private Label _dragLabel;

	// ── Initialization ──────────────────────────────────────────────

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);

		// Create the drag cursor label
		_dragLabel = new Label();
		_dragLabel.Visible = false;
		_dragLabel.ZIndex = 500;
		_dragLabel.MouseFilter = MouseFilterEnum.Ignore;
		_dragLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.5f));
		_dragLabel.AddThemeFontSizeOverride("font_size", 11);
		AddChild(_dragLabel);

		// Create SocialManager
		_socialManager = new SocialManager();
		_socialManager.Name = "SocialManager";
		AddChild(_socialManager);

		// Create ActionPanel (hidden by default) — replaces the old standalone SocialWindow
	}

	/// <summary>
	/// Must be called after _Ready, once GameClient and character info are available.
	/// </summary>
	public void Init(GameClient client, string characterName)
	{
		if (_initialized) return; // Already initialized — don't re-init on every status tick
		_initialized = true;

		_client = client;
		_characterName = characterName;

		_socialManager.Init(client);
		_socialManager.GetSpellIdForSlot = (slot) => GetSpellIdForSlot?.Invoke(slot) ?? -1;
		_socialManager.GetCurrentTargetName = () => GetTargetName?.Invoke() ?? "target";

		// Load saved state or create default hotbar
		if (!LoadState())
		{
			CreateHotbar();
		}

		GD.Print($"[HOTBAR] Initialized for '{characterName}' with {_hotbars.Count} hotbar(s).");
	}

	// ── Hotbar Lifecycle ────────────────────────────────────────────

	private Hotbar CreateHotbar()
	{
		if (_hotbars.Count >= MAX_HOTBARS) return null;

		var bar = new Hotbar();
		bar.BarIndex = _hotbars.Count;
		bar.Name = $"Hotbar{_hotbars.Count}";
		bar.ZIndex = 10 + _hotbars.Count;

		// Position each new bar below the previous one
		float yOffset = 60 + _hotbars.Count * 70;
		bar.Position = new Vector2(400, yOffset);

		// Wire signals
		bar.HotbuttonActivated += OnHotbuttonActivated;
		bar.HotbuttonInspectRequested += OnHotbuttonInspectRequested;
		bar.NewHotbarRequested += () => CreateHotbar();
		bar.HotbarClosed += OnHotbarClosed;

		AddChild(bar);
		_hotbars.Add(bar);

		GD.Print($"[HOTBAR] Created Hotbar {bar.BarIndex + 1}");
		return bar;
	}

	private void OnHotbarClosed(int barIndex)
	{
		// Don't allow closing the last hotbar
		if (_hotbars.Count <= 1) return;

		for (int i = _hotbars.Count - 1; i >= 0; i--)
		{
			if (_hotbars[i].BarIndex == barIndex)
			{
				_hotbars[i].QueueFree();
				_hotbars.RemoveAt(i);
				break;
			}
		}

		// Re-index remaining bars
		for (int i = 0; i < _hotbars.Count; i++)
			_hotbars[i].BarIndex = i;
	}

	// ── Hotbutton Activation ────────────────────────────────────────

	private void OnHotbuttonActivated(int barIndex, int page, int slot)
	{
		if (barIndex < 0 || barIndex >= _hotbars.Count) return;

		var data = _hotbars[barIndex].SlotData[page, slot];
		ExecuteHotbutton(data);
	}

	private void OnHotbuttonInspectRequested(int barIndex, int page, int slot)
	{
		if (barIndex < 0 || barIndex >= _hotbars.Count) return;
		var bar = _hotbars[barIndex];
		var data = bar.SlotData[page, slot];
		if (data.Type == Hotbar.HotbuttonType.Spell)
		{
			// Try to get spell ID
			int spellId = GetSpellIdForSlot?.Invoke(data.SpellSlotIndex) ?? -1;
			if (spellId > 0)
			{
				GameClient.Instance.SendRaw($"{{\"type\": \"SPELL_INSPECT\", \"spellId\": {spellId}}}");
			}
		}
	}

	private void ExecuteHotbutton(Hotbar.HotbuttonData data)
	{
		if (data == null || data.Type == Hotbar.HotbuttonType.Empty) return;
		if (_client == null) return;

		switch (data.Type)
		{
			case Hotbar.HotbuttonType.Spell:
			{
				int spellId = GetSpellIdForSlot?.Invoke(data.SpellSlotIndex) ?? -1;
				if (spellId > 0)
					_client.SendRaw($"{{\"type\": \"CAST_SPELL\", \"spellId\": {spellId}, \"slot\": {data.SpellSlotIndex}}}");
				break;
			}
			case Hotbar.HotbuttonType.Ability:
			{
				string ability = data.AbilityName.ToLower();
				if (ability == "attack")
				{
					ToggleAutoAttack?.Invoke();
				}
				else
				{
					_client.SendRaw($"{{\"type\": \"ABILITY\", \"ability\": \"{ability}\"}}");
				}
				break;
			}
			case Hotbar.HotbuttonType.Item:
			{
				// Equipment fast-swap: equip/unequip the item
				if (data.ItemId > 0)
					EquipItemById?.Invoke(data.ItemId);
				break;
			}
			case Hotbar.HotbuttonType.Social:
			{
				_socialManager.ExecuteSocial(data.SocialIndex);
				break;
			}
			case Hotbar.HotbuttonType.Macro:
			{
				if (!string.IsNullOrEmpty(data.MacroText))
				{
					// Route the macro string back through the main chat command parser
					var mainUI = GetNodeOrNull<MainUI>("/root/MainUI") ?? GetParent() as MainUI;
					if (mainUI != null) mainUI.ExecuteCommand(data.MacroText);
				}
				break;
			}
		}
	}

	// ── Drag-and-Drop: Starting Drags ───────────────────────────────

	/// <summary>
	/// Start dragging a spell from the spell bar. Called by MainUI.
	/// </summary>
	public void StartSpellDrag(int spellSlotIndex, string spellName)
	{
		_isDragging = true;
		_dragData = new Hotbar.HotbuttonData
		{
			Type = Hotbar.HotbuttonType.Spell,
			SpellSlotIndex = spellSlotIndex,
			DisplayName = spellName
		};
		_dragLabel.Text = $"[Spell] {spellName}";
		_dragLabel.Visible = true;
	}

	/// <summary>
	/// Start dragging an ability. Called by MainUI.
	/// </summary>
	public void StartAbilityDrag(string abilityName)
	{
		_isDragging = true;
		_dragData = new Hotbar.HotbuttonData
		{
			Type = Hotbar.HotbuttonType.Ability,
			AbilityName = abilityName,
			DisplayName = abilityName
		};
		_dragLabel.Text = $"[Ability] {abilityName}";
		_dragLabel.Visible = true;
	}

	/// <summary>
	/// Start dragging an item. Called by MainUI.
	/// </summary>
	public void StartItemDrag(int itemId, string itemName, string itemKey = "")
	{
		_isDragging = true;
		_dragData = new Hotbar.HotbuttonData
		{
			Type = Hotbar.HotbuttonType.Item,
			ItemId = itemId,
			ItemKey = itemKey,
			DisplayName = itemName
		};
		_dragLabel.Text = $"[Item] {itemName}";
		_dragLabel.Visible = true;
	}

	public void StartSocialDrag(int socialIndex, string socialName, int socialColor)
	{
		_isDragging = true;
		_dragData = new Hotbar.HotbuttonData
		{
			Type = Hotbar.HotbuttonType.Social,
			SocialIndex = socialIndex,
			DisplayName = socialName
		};
		_dragLabel.Text = $"[Social] {socialName}";
		_dragLabel.Visible = true;
	}

	public void StartMacroDrag(string macroText, string macroName)
	{
		_isDragging = true;
		_dragData = new Hotbar.HotbuttonData
		{
			Type = Hotbar.HotbuttonType.Macro,
			MacroText = macroText,
			DisplayName = macroName
		};
		_dragLabel.Text = $"[Macro] {macroName}";
		_dragLabel.Visible = true;
	}

	// ── Drag Processing ─────────────────────────────────────────────

	public override void _Process(double delta)
	{
		if (_isDragging && _dragLabel.Visible)
		{
			_dragLabel.GlobalPosition = GetGlobalMousePosition() + new Vector2(14, 14);
		}
	}

	public override void _Input(InputEvent @event)
	{
		// Handle drag drop
		if (_isDragging && @event is InputEventMouseButton mb &&
			mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
		{
			// Mouse released — try to drop onto a hotbar slot
			CompleteDrag();
		}

		// Keyboard shortcuts
		if (@event is InputEventKey k && k.Pressed && !k.Echo)
		{
			HandleHotbarKeypress(k);
		}
	}

	private void CompleteDrag()
	{
		if (!_isDragging) return;

		var mousePos = GetGlobalMousePosition();

		// Check each hotbar for a hit
		bool placed = false;
		foreach (var bar in _hotbars)
		{
			var localPos = mousePos - bar.GlobalPosition;
			var barRect = new Rect2(Vector2.Zero, bar.Size);
			if (barRect.HasPoint(localPos))
			{
				int slotIndex = bar.GetSlotAtPosition(localPos);
				if (slotIndex >= 0)
				{
					bar.SetSlot(bar.CurrentPage, slotIndex, _dragData);
					placed = true;
					GD.Print($"[HOTBAR] Placed {_dragData.Type} '{_dragData.DisplayName}' on Bar {bar.BarIndex + 1}, Page {bar.CurrentPage + 1}, Slot {slotIndex + 1}");
					break;
				}
			}
		}

		if (!placed)
		{
			GD.Print("[HOTBAR] Drag cancelled — not dropped on a hotbar slot.");
		}

		_isDragging = false;
		_dragData = null;
		_dragLabel.Visible = false;
	}

	public void CancelDrag()
	{
		_isDragging = false;
		_dragData = null;
		_dragLabel.Visible = false;
	}

	// ── Keyboard Shortcuts ──────────────────────────────────────────

	private void HandleHotbarKeypress(InputEventKey k)
	{
		if (_hotbars.Count == 0) return;
		var bar = _hotbars[0]; // Hotbar 1 gets keyboard shortcuts

		// Shift+1-0: Switch pages
		if (k.ShiftPressed && !k.CtrlPressed && !k.AltPressed)
		{
			int pageNum = KeyToNumber(k.Keycode);
			if (pageNum >= 0)
			{
				bar.SetPage(pageNum);
				return;
			}
		}



		// Alt+H: Toggle all hotbar visibility
		if (k.AltPressed && k.Keycode == Key.H)
		{
			foreach (var hb in _hotbars)
				hb.Visible = !hb.Visible;
			return;
		}

		// 1-0: Activate hotbar 1 slots (only when no modifier or only basic keys)
		// Note: We need to be careful not to conflict with existing keybinds.
		// For now, only fire when NO modifier keys are held AND the chat input is not focused.
		if (!k.ShiftPressed && !k.CtrlPressed && !k.AltPressed)
		{
			int slotNum = KeyToNumber(k.Keycode);
			if (slotNum >= 0)
			{
				// Don't fire if a LineEdit/TextEdit has focus (player typing in chat)
				var focused = GetViewport().GuiGetFocusOwner();
				if (focused is LineEdit || focused is TextEdit) return;

				OnHotbuttonActivated(0, bar.CurrentPage, slotNum);
			}
		}
	}

	/// <summary>
	/// Maps Key.Key1-Key.Key0 to slot indices 0-9.
	/// Returns -1 if not a number key.
	/// </summary>
	private int KeyToNumber(Key key)
	{
		return key switch
		{
			Key.Key1 => 0,
			Key.Key2 => 1,
			Key.Key3 => 2,
			Key.Key4 => 3,
			Key.Key5 => 4,
			Key.Key6 => 5,
			Key.Key7 => 6,
			Key.Key8 => 7,
			Key.Key9 => 8,
			Key.Key0 => 9,
			_ => -1,
		};
	}

	// ── Action Panel ────────────────────────────────────────────────

	public SocialManager GetSocialManager() => _socialManager;

	// ── Spell Cooldown Updates ──────────────────────────────────────

	/// <summary>
	/// Called by MainUI when spell cooldowns or mana changes.
	/// Updates the enabled/disabled state of spell hotbuttons.
	/// </summary>
	public void UpdateSpellSlotState(int slotIndex, bool canCast, string tooltip)
	{
		foreach (var bar in _hotbars)
		{
			for (int s = 0; s < 10; s++)
			{
				var data = bar.SlotData[bar.CurrentPage, s];
				if (data.Type == Hotbar.HotbuttonType.Spell && data.SpellSlotIndex == slotIndex)
				{
					bar.SetSlotDisabled(s, !canCast);
					bar.SetSlotTooltip(s, tooltip);
				}
			}
		}
	}

	// ── Persistence (Local JSON) ────────────────────────────────────

	private string GetSavePath()
	{
		return UILayoutManager.GetCurrentLayoutFileName(_characterName);
	}

	public void SaveState()
	{
		try
		{
			var saveData = new Godot.Collections.Dictionary();

			// Save hotbar data
			var barsArray = new Godot.Collections.Array<Godot.Collections.Dictionary>();
			foreach (var bar in _hotbars)
			{
				var barDict = new Godot.Collections.Dictionary();
				barDict["barName"] = bar.BarName;
				barDict["posX"] = bar.Position.X;
				barDict["posY"] = bar.Position.Y;
				barDict["page"] = bar.CurrentPage;
				barDict["visible"] = bar.Visible;
				barDict["locked"] = bar.Locked;
				barDict["showSlotNumbers"] = bar.ShowSlotNumbers;
				barDict["fadeWhenInactive"] = bar.FadeWhenInactive;
				barDict["alpha"] = bar.BarAlpha;
				if (bar.BgColor != default) barDict["bgColor"] = bar.BgColor.ToHtml();
				if (bar.BorderColor != default) barDict["borderColor"] = bar.BorderColor.ToHtml();

				var layoutInfo = bar.GetLayout();
				barDict["layoutIndex"] = layoutInfo.layoutIndex;

				// Save slot data (all 10 pages × 10 slots)
				var slotsArray = new Godot.Collections.Array<Godot.Collections.Dictionary>();
				for (int p = 0; p < 10; p++)
				{
					for (int s = 0; s < 10; s++)
					{
						var data = bar.SlotData[p, s];
						var slotDict = new Godot.Collections.Dictionary
						{
							["page"] = p,
							["slot"] = s,
							["type"] = (int)data.Type,
							["spellSlot"] = data.SpellSlotIndex,
							["abilityName"] = data.AbilityName,
							["itemId"] = data.ItemId,
							["itemKey"] = data.ItemKey,
							["socialIndex"] = data.SocialIndex,
							["displayName"] = data.DisplayName,
							["macroText"] = data.MacroText,
						};
						slotsArray.Add(slotDict);
					}
				}
				barDict["slots"] = slotsArray;
				barsArray.Add(barDict);
			}

			// Add to UILayoutManager
			UILayoutManager.SetSection("Hotbars", new Godot.Collections.Dictionary { ["bars"] = barsArray });
			UILayoutManager.SetSection("Socials", new Godot.Collections.Dictionary { ["data"] = _socialManager.ExportSocials() });

			// Actually save the file
			UILayoutManager.SaveLayout(_characterName);
			GD.Print($"[HOTBAR] Saved state to {GetSavePath()}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[HOTBAR] Save error: {ex.Message}");
		}
	}

	public bool LoadState()
	{
		try
		{
			// Load from UILayoutManager first
			var hotbarsSection = UILayoutManager.GetSection("Hotbars");
			var socialsSection = UILayoutManager.GetSection("Socials");

			if (hotbarsSection.Count == 0 && socialsSection.Count == 0)
			{
				// Legacy fallback
				string path = $"user://hotbars_{_characterName}.json";
				if (!FileAccess.FileExists(path)) return false;

				using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
				if (file == null) return false;

				string jsonStr = file.GetAsText();
				var parsed = Json.ParseString(jsonStr);
				if (parsed.VariantType != Variant.Type.Dictionary) return false;

				var saveData = parsed.AsGodotDictionary();
				if (saveData.ContainsKey("socials"))
					_socialManager.ImportSocials(saveData["socials"].AsGodotArray<Godot.Collections.Dictionary>());

				if (saveData.ContainsKey("hotbars"))
					LoadHotbarsFromArray(saveData["hotbars"].AsGodotArray<Godot.Collections.Dictionary>());

				return _hotbars.Count > 0;
			}

			if (socialsSection.ContainsKey("data"))
			{
				_socialManager.ImportSocials(socialsSection["data"].AsGodotArray<Godot.Collections.Dictionary>());
			}

			if (hotbarsSection.ContainsKey("bars"))
			{
				LoadHotbarsFromArray(hotbarsSection["bars"].AsGodotArray<Godot.Collections.Dictionary>());
				return _hotbars.Count > 0;
			}

			return false;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[HOTBAR] Load error: {ex.Message}");
			return false;
		}
	}

	private void LoadHotbarsFromArray(Godot.Collections.Array<Godot.Collections.Dictionary> barsData)
	{
		foreach (var barDict in barsData)
		{
			var bar = CreateHotbar();
			if (bar == null) break;

					bar.Position = new Vector2(
						barDict.ContainsKey("posX") ? barDict["posX"].AsSingle() : 400,
						barDict.ContainsKey("posY") ? barDict["posY"].AsSingle() : 60
					);

					if (barDict.ContainsKey("barName")) bar.BarName = barDict["barName"].AsString();

					if (barDict.ContainsKey("page")) bar.SetPage(barDict["page"].AsInt32());
					if (barDict.ContainsKey("visible")) bar.Visible = barDict["visible"].AsBool();
					if (barDict.ContainsKey("locked")) bar.Locked = barDict["locked"].AsBool();
					if (barDict.ContainsKey("showSlotNumbers")) bar.ShowSlotNumbers = barDict["showSlotNumbers"].AsBool();
					if (barDict.ContainsKey("fadeWhenInactive")) bar.FadeWhenInactive = barDict["fadeWhenInactive"].AsBool();
					if (barDict.ContainsKey("alpha")) bar.BarAlpha = barDict["alpha"].AsSingle();
					if (barDict.ContainsKey("layoutIndex")) bar.SetLayout(barDict["layoutIndex"].AsInt32());

					if (barDict.ContainsKey("bgColor"))
						bar.BgColor = new Color(barDict["bgColor"].AsString());
					if (barDict.ContainsKey("borderColor"))
						bar.BorderColor = new Color(barDict["borderColor"].AsString());

					// Load slot data
					if (barDict.ContainsKey("slots"))
					{
						var slotsData = barDict["slots"].AsGodotArray<Godot.Collections.Dictionary>();
						foreach (var slotDict in slotsData)
						{
							int page = slotDict["page"].AsInt32();
							int slot = slotDict["slot"].AsInt32();
							var data = new Hotbar.HotbuttonData
							{
								Type = (Hotbar.HotbuttonType)slotDict["type"].AsInt32(),
								SpellSlotIndex = slotDict["spellSlot"].AsInt32(),
								AbilityName = slotDict["abilityName"].AsString(),
								ItemId = slotDict["itemId"].AsInt32(),
								ItemKey = slotDict.ContainsKey("itemKey") ? slotDict["itemKey"].AsString() : "",
								SocialIndex = slotDict["socialIndex"].AsInt32(),
								DisplayName = slotDict["displayName"].AsString(),
								MacroText = slotDict.ContainsKey("macroText") ? slotDict["macroText"].AsString() : "",
							};
							bar.SlotData[page, slot] = data;
						}
					}

					bar.RefreshSlots();
				}

		GD.Print($"[HOTBAR] Loaded {_hotbars.Count} hotbar(s) from UILayoutManager.");
	}

	public void ReloadHotbars()
	{
		// Called by MainUI when copying a layout
		foreach (var h in _hotbars)
		{
			h.QueueFree();
		}
		_hotbars.Clear();
		
		if (!LoadState())
		{
			CreateHotbar();
		}
	}

	/// <summary>
	/// Called when the player logs out or zones — save state.
	/// </summary>
	public void OnPlayerLogout()
	{
		SaveState();
	}
}
