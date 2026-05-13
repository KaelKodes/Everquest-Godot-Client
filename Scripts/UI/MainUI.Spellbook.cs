using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class MainUI
{
	private void OnSpellSlotPressed(int slotIndex)
	{
		var spell = _spells[slotIndex];
		if (spell.SpellId <= 0) return;
		if (spell.CooldownRemaining > 0) return;
		if (_isCasting)
		{
			Log("SYSTEM", "You are already casting a spell!");
			return;
		}
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
	}

	// ── Spell Bar Right-Click Menus ─────────────────────────────────

	/// <summary>Show a categorized spell picker for memorizing into an empty gem slot.</summary>
	private void ShowSpellMemorizePicker(int slotIndex, Button anchorBtn)
	{
		if (!_isSitting)
		{
			Log("SYSTEM", "You must be sitting to memorize spells.");
			return;
		}
		if (_knownSpells.Count == 0)
		{
			Log("SYSTEM", "You have no scribed spells.");
			return;
		}

		// Group by effect category
		var categories = new Dictionary<string, List<KnownSpell>>();
		foreach (var sp in _knownSpells)
		{
			string cat = FormatEffectCategory(sp.Effect);
			if (!categories.ContainsKey(cat))
				categories[cat] = new List<KnownSpell>();
			categories[cat].Add(sp);
		}

		// Build cascading popup
		var popup = new PopupMenu();
		popup.Name = "SpellPicker";
		popup.AddThemeFontSizeOverride("font_size", 12);

		// If few spells, show flat list; otherwise group by category
		if (_knownSpells.Count <= 8)
		{
			for (int i = 0; i < _knownSpells.Count; i++)
			{
				var sp = _knownSpells[i];
				popup.AddItem($"{sp.Name} [{sp.ManaCost}m]", i);
			}

			popup.IdPressed += (id) =>
			{
				if (id >= 0 && id < _knownSpells.Count)
				{
					var picked = _knownSpells[(int)id];
					BeginMemorizeSpellTimed(slotIndex, picked.SpellKey, picked.Name);
				}
				popup.QueueFree();
			};
		}
		else
		{
			// Cascading submenus by category
			int itemIdx = 0;
			foreach (var kvp in categories)
			{
				var subMenu = new PopupMenu();
				subMenu.Name = $"Cat_{kvp.Key.Replace(" ", "")}";
				subMenu.AddThemeFontSizeOverride("font_size", 12);

				for (int i = 0; i < kvp.Value.Count; i++)
				{
					var sp = kvp.Value[i];
					subMenu.AddItem($"{sp.Name} (Lv{sp.Level}) [{sp.ManaCost}m]", i);
				}

				var capturedList = kvp.Value;
				subMenu.IdPressed += (id) =>
				{
					if (id >= 0 && id < capturedList.Count)
					{
						var picked = capturedList[(int)id];
						BeginMemorizeSpellTimed(slotIndex, picked.SpellKey, picked.Name);
					}
					popup.QueueFree();
				};

				popup.AddChild(subMenu);
				popup.AddSubmenuNodeItem(kvp.Key, subMenu);
				itemIdx++;
			}
		}

		popup.PopupHide += () => popup.QueueFree();
		AddChild(popup);
		popup.Position = new Vector2I((int)anchorBtn.GlobalPosition.X, (int)(anchorBtn.GlobalPosition.Y + anchorBtn.Size.Y));
		popup.Popup();
	}

	/// <summary>Show context menu when right-clicking a filled spell gem.</summary>
	private void ShowSpellSlotContextMenu(int slotIndex, Button anchorBtn)
	{
		var popup = new PopupMenu();
		popup.Name = "SpellSlotContext";
		popup.AddThemeFontSizeOverride("font_size", 12);
		popup.AddItem("Scribe (Replace)", 0);
		popup.AddItem("Forget", 1);

		popup.IdPressed += (id) =>
		{
			if (id == 0)
			{
				// Open picker to replace this slot
				ShowSpellMemorizePicker(slotIndex, anchorBtn);
			}
			else if (id == 1)
			{
				// Forget: clear this slot
				_spells[slotIndex] = new MemorizedSpell();
				var slotBtn = _spellSlotButtons[slotIndex];
				slotBtn.Text = "";
				slotBtn.TooltipText = $"Empty Spell Gem {slotIndex + 1}";
				slotBtn.Disabled = true;
				slotBtn.Icon = null;
				_spellSlotLabels[slotIndex].Text = $"Gem {slotIndex + 1}";
				_spellSlotLabels[slotIndex].AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f, 0.5f));
				_client.SendRaw($"{{\"type\": \"FORGET_SPELL\", \"slot\": {slotIndex}}}");
			}
			popup.QueueFree();
		};

		popup.PopupHide += () => popup.QueueFree();
		AddChild(popup);
		popup.Position = new Vector2I((int)anchorBtn.GlobalPosition.X, (int)(anchorBtn.GlobalPosition.Y + anchorBtn.Size.Y));
		popup.Popup();
	}

	private void ShowSpellbarContextMenu(Control anchor)
	{
		var popup = new PopupMenu();
		popup.Name = "SpellbarContext";
		popup.AddThemeFontSizeOverride("font_size", 12);
		
		popup.AddItem("Save Loadout", 0);
		popup.AddItem("Load Loadout", 1);
		popup.AddItem("Clear Spells", 2);

		popup.IdPressed += (id) =>
		{
			if (id == 0) // Save
			{
				ShowSaveLoadoutDialog();
			}
			else if (id == 1) // Load
			{
				ShowLoadLoadoutContextMenu(anchor);
			}
			else if (id == 2) // Clear
			{
				_client.SendRaw("{\"type\": \"CLEAR_SPELLS\"}");
			}
			popup.QueueFree();
		};

		popup.PopupHide += () => popup.QueueFree();
		AddChild(popup);
		popup.Position = new Vector2I((int)GetViewport().GetMousePosition().X, (int)GetViewport().GetMousePosition().Y);
		popup.Popup();
	}

	private void ShowSaveLoadoutDialog()
	{
		var dialog = new AcceptDialog();
		dialog.Title = "Save Spell Loadout";
		dialog.DialogText = "Enter a name for this loadout:";
		
		var input = new LineEdit();
		input.CustomMinimumSize = new Vector2I(200, 30);
		dialog.AddChild(input);
		// Move to proper VBox container inside dialog
		dialog.RegisterTextEnter(input);

		dialog.Confirmed += () => {
			string name = input.Text.Trim();
			if (!string.IsNullOrEmpty(name))
			{
				_client.SendRaw($"{{\"type\": \"SAVE_SPELL_LOADOUT\", \"name\": \"{name}\"}}");
			}
			dialog.QueueFree();
		};
		
		dialog.Canceled += () => dialog.QueueFree();
		
		AddChild(dialog);
		dialog.PopupCentered();
	}

	private void ShowLoadLoadoutContextMenu(Control anchor)
	{
		if (_spellLoadouts.Count == 0)
		{
			Log("SYSTEM", "You have no saved spell loadouts.");
			return;
		}

		var popup = new PopupMenu();
		popup.Name = "LoadLoadoutContext";
		popup.AddThemeFontSizeOverride("font_size", 12);
		
		for (int i = 0; i < _spellLoadouts.Count; i++)
		{
			popup.AddItem(_spellLoadouts[i], i);
		}

		popup.IdPressed += (id) =>
		{
			if (id >= 0 && id < _spellLoadouts.Count)
			{
				string loadoutName = _spellLoadouts[(int)id];
				ShowLoadoutActionDialog(loadoutName);
			}
			popup.QueueFree();
		};

		popup.PopupHide += () => popup.QueueFree();
		AddChild(popup);
		popup.Position = new Vector2I((int)GetViewport().GetMousePosition().X, (int)GetViewport().GetMousePosition().Y);
		popup.Popup();
	}

	private void ShowLoadoutActionDialog(string loadoutName)
	{
		var dialog = new ConfirmationDialog();
		dialog.Title = $"Loadout: {loadoutName}";
		dialog.DialogText = $"What would you like to do with '{loadoutName}'?";
		
		// The confirmation dialog has "OK" and "Cancel" by default. 
		// We can change "OK" to "Load", add a "Delete" button.
		dialog.OkButtonText = "Load";
		var deleteBtn = dialog.AddButton("Delete", true, "delete");
		
		dialog.Confirmed += () => {
			_client.SendRaw($"{{\"type\": \"LOAD_SPELL_LOADOUT\", \"name\": \"{loadoutName}\"}}");
			dialog.QueueFree();
		};
		
		dialog.CustomAction += (action) => {
			if (action == "delete")
			{
				_client.SendRaw($"{{\"type\": \"DELETE_SPELL_LOADOUT\", \"name\": \"{loadoutName}\"}}");
				dialog.QueueFree();
			}
		};
		
		dialog.Canceled += () => dialog.QueueFree();
		
		AddChild(dialog);
		dialog.PopupCentered();
	}

	/// <summary>
	/// Kick off a server-timed memorize. Works for any scribed spell — even ones that
	/// aren't currently on the spellbar — because the server resolves everything else
	/// (definition, cast time, duration, cooldown) from the spellKey.
	/// </summary>
	private void BeginMemorizeSpellTimed(int slotIndex, string spellKey, string spellName)
	{
		if (string.IsNullOrEmpty(spellKey)) return;
		if (slotIndex < 0 || slotIndex >= 8) return;

		if (_isMemorizing)
		{
			Log("SYSTEM", "You are already memorizing a spell.");
			return;
		}
		if (_isCasting)
		{
			Log("SYSTEM", "You cannot memorize while casting.");
			return;
		}
		if (!_isSitting)
		{
			Log("SYSTEM", "You must be sitting to memorize spells.");
			return;
		}

		string safeKey = spellKey.Replace("\\", "\\\\").Replace("\"", "\\\"");
		_client.SendRaw($"{{\"type\":\"BEGIN_MEMORIZE_SPELL\",\"spellKey\":\"{safeKey}\",\"slot\":{slotIndex}}}");
	}

	private string FormatEffectCategory(string effect)
	{
		return effect switch
		{
			"heal" => "🩹 Heals",
			"dd" => "⚡ Direct Damage",
			"dot" => "🔥 Damage over Time",
			"buff" => "🛡 Buffs",
			"debuff" => "💀 Debuffs",
			"root" => "🌿 Roots",
			"snare" => "🕸 Snares",
			"cure" => "💊 Cures",
			"info" => "📖 Utility",
			_ => "📜 Other"
		};
	}

	/// <summary>
	/// Handle cast events (CAST_START, CAST_COMPLETE, CAST_INTERRUPTED)
	/// </summary>
	private void OnSpellbookUpdated(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			if (!root.TryGetProperty("spells", out var spells)) return;

			// Snapshot existing cooldowns by SpellId so we can preserve them across a
			// server refresh (e.g., the SPELLBOOK_UPDATE that immediately follows a
			// MEMORIZE_SPELL must not wipe the freshly-applied "just memorized" cooldown).
			var preservedCooldowns = new Dictionary<int, float>();
			for (int i = 0; i < _spells.Length; i++)
			{
				if (_spells[i].SpellId > 0 && _spells[i].CooldownRemaining > 0f)
					preservedCooldowns[_spells[i].SpellId] = _spells[i].CooldownRemaining;
			}

			// Reset all slots
			for (int i = 0; i < 8; i++)
			{
				_spells[i] = new MemorizedSpell();
				var slotBtn = _spellSlotButtons[i];
				slotBtn.Text = "";
				slotBtn.TooltipText = $"Empty Spell Gem {i + 1}";
				slotBtn.Disabled = true;
				slotBtn.Icon = null;
				_spellSlotLabels[i].Text = $"Gem {i + 1}";
				_spellSlotLabels[i].AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f, 0.5f));
			}

			// Populate from server data AND build known spells list
			_knownSpells.Clear();
			foreach (var spell in spells.EnumerateArray())
			{
				int slot = spell.GetProperty("slot").GetInt32();
				int spellId = spell.GetProperty("spellId").GetInt32();
				if (spellId <= 0) continue;

				string name = spell.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : $"Spell #{spellId}";
				string spellKey = spell.TryGetProperty("spellKey", out var keyProp) ? keyProp.GetString() : "";
				int manaCost = spell.TryGetProperty("manaCost", out var manaProp) ? manaProp.GetInt32() : 0;
				float castTime = spell.TryGetProperty("castTime", out var ctProp) ? (float)ctProp.GetDouble() : 1.5f;
				string effect = spell.TryGetProperty("effect", out var eProp) ? eProp.GetString() : "unknown";
				int level = spell.TryGetProperty("level", out var lvProp) ? lvProp.GetInt32() : 1;
				string description = spell.TryGetProperty("description", out var descProp) ? descProp.GetString() : "";
				int memIcon = spell.TryGetProperty("memIcon", out var memProp) ? memProp.GetInt32() : 0;
				int iconId = spell.TryGetProperty("icon", out var iconProp) ? iconProp.GetInt32() : 0;
				// GD.Print($"[DEBUG] Parsed Spell {name}: memIcon={memIcon}, icon={iconId}");

				// Add to known spells list (for right-click memorize picker)
				_knownSpells.Add(new KnownSpell {
					SpellId = spellId,
					SpellKey = spellKey,
					Name = name,
					ManaCost = manaCost,
					CastTime = castTime,
					Effect = effect,
					Level = level,
					Description = description,
					MemIcon = memIcon,
					Icon = iconId
				});

				if (slot < 0 || slot >= 8) continue;

				// Server is authoritative for cooldowns when it sends one. If the field is
				// absent (older payload), preserve whatever the client was already ticking.
				float cooldown = 0f;
				if (spell.TryGetProperty("cooldownRemaining", out var cdProp) && cdProp.ValueKind == JsonValueKind.Number)
				{
					cooldown = (float)cdProp.GetDouble();
				}
				else if (preservedCooldowns.TryGetValue(spellId, out float kept))
				{
					cooldown = kept;
				}

				_spells[slot] = new MemorizedSpell
				{
					SpellId = spellId,
					Name = name,
					ManaCost = manaCost,
					CastTime = castTime,
					CooldownRemaining = cooldown,
					Description = description,
					MemIcon = memIcon,
					Icon = iconId
				};

				var slotBtn = _spellSlotButtons[slot];
				slotBtn.Text = "";
				string descTip = !string.IsNullOrEmpty(description) ? $"\n{description}" : "";
				slotBtn.TooltipText = $"{name} [{manaCost}m] Cast: {castTime:F1}s{descTip}";
				slotBtn.Disabled = _currentMana < manaCost;

				_spellSlotLabels[slot].Text = name;
				_spellSlotLabels[slot].AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 1.0f));
				
				var iconMgr = IconManager.Instance;
				if (iconMgr != null) {
					// GD.Print($"[SPELLBAR DEBUG] Rendering {name} with memIcon: {memIcon}");
					var icon = iconMgr.GetSpellGem(memIcon);
					if (icon != null) {
						slotBtn.Icon = icon;
						slotBtn.ExpandIcon = true;
					} else {
						var atlas = new AtlasTexture();
						atlas.Atlas = _spellGemTexture;
						atlas.Region = GetSpellIconRect(name);
						slotBtn.Icon = atlas;
						slotBtn.ExpandIcon = false;
					}
				}
				slotBtn.IconAlignment = HorizontalAlignment.Right;
			}

			GD.Print("[UI] Spellbook updated.");
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Spellbook Error: {ex.Message}"); }
	}

	/// <summary>Handle the full spellbook payload (all scribed spells with book positions).</summary>
	private void OnSpellbookFullReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		string json = (string)data;
		_spellbookUI?.LoadFromPayload(json);
	}

	private void OnSpellLoadoutsReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			if (root.TryGetProperty("loadouts", out var loadoutsArray))
			{
				_spellLoadouts.Clear();
				foreach (var l in loadoutsArray.EnumerateArray())
				{
					_spellLoadouts.Add(l.GetString());
				}
			}
		}
		catch (Exception ex) { GD.PrintErr($"[UI] SpellLoadouts Error: {ex.Message}"); }
	}

	/// <summary>Called when the player left-clicks a spell in the spellbook UI.</summary>
	private void OnSpellSelectedForMemorize(string spellKey, string spellName)
	{
		_pendingMemorizeSpellKey = spellKey;
		_pendingMemorizeSpellName = spellName;
		Log("SYSTEM", $"Left-click a spell gem to memorize {spellName}...");
	}

	/// <summary>Toggle the spellbook UI. Must be sitting to open.</summary>
	public void ToggleSpellbook()
	{
		if (_spellbookUI == null) return;
		if (_spellbookUI.Visible)
		{
			_spellbookUI.Visible = false;
			_pendingMemorizeSpellKey = null;
			_pendingMemorizeSpellName = null;
		}
		else
		{
			if (!_isSitting)
			{
				Log("SYSTEM", "You must be sitting to open your spellbook.");
				return;
			}
			_spellbookUI.Visible = true;
			// Center on screen
			var screenSize = GetViewport().GetVisibleRect().Size;
			_spellbookUI.GlobalPosition = (screenSize - _spellbookUI.Size) / 2;
		}
	}

	// ── Options Panel ───────────────────────────────────────────────

}
