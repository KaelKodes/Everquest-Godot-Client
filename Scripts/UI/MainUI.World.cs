using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class MainUI
{
	private void OnEnvironmentUpdated(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			var dict = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement;
			var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
			if (wm != null)
			{
				int worldHour = dict.TryGetProperty("worldHour", out var wh) ? wh.GetInt32() : 12;
				int dawn = dict.TryGetProperty("dawn", out var d) ? d.GetInt32() : 6;
				int dusk = dict.TryGetProperty("dusk", out var dk) ? dk.GetInt32() : 18;

				string drinalPhase = "Full";
				if (dict.TryGetProperty("moons", out var mDict) && mDict.TryGetProperty("drinal", out var dDict) && dDict.TryGetProperty("phase", out var pProp))
				{
					drinalPhase = pProp.GetString();
				}

				wm.UpdateEnvironmentTime(worldHour, dawn, dusk, drinalPhase);
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[UI] OnEnvironmentUpdated error: {ex.Message}");
		}
	}

	private void OnEntitySneakReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
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

	private void OnEntityHideReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			var dict = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement;
			string id = dict.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
			bool hidden = dict.TryGetProperty("hidden", out var hProp) && hProp.GetBoolean();
			
			if (id != null)
			{
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null)
				{
					wm.UpdateEntityHide(id, hidden);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] OnEntityHideReceived error: {ex.Message}");
		}
	}

	private void OnSneakResultReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			var dict = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement;
			bool success = dict.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
			
			if (!success)
			{
				// Server rejected sneak — cancel local crouch state
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				wm?.CancelStealth(true, true);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] OnSneakResultReceived error: {ex.Message}");
		}
	}

	private void OnHideResultReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			var dict = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement;
			bool success = dict.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
			
			if (!success)
			{
				// Server rejected hide — cancel local hide state only (sneak stays)
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				wm?.CancelStealth(false, true);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] OnHideResultReceived error: {ex.Message}");
		}
	}

	private void OnSneakBrokenReceived()
	{
		if (!IsInstanceValid(this)) return;
		// Server broke our sneak (hit by spell/melee, casting, etc.)
		var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
		wm?.CancelStealth(true, true);
	}

	private void OnHideBrokenReceived()
	{
		if (!IsInstanceValid(this)) return;
		// Server broke our hide (hit, casting, moving without sneak)
		var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
		wm?.CancelStealth(false, true);
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
	private void EnsureCompanionWindow()
	{
		if (_companionWindow != null && IsInstanceValid(_companionWindow)) return;

		_companionWindow = new Window();
		_companionWindow.Name = "CompanionWindow";
		_companionWindow.Title = "Companion";
		_companionWindow.Size = new Vector2I(260, 280);
		_companionWindow.Position = new Vector2I(10, 380);
		_companionWindow.Visible = false;
		_companionWindow.AlwaysOnTop = true;
		_companionWindow.Unresizable = false;
		_companionWindow.Exclusive = false;
		_companionWindow.CloseRequested += () => _companionWindow.Visible = false;
		AddChild(_companionWindow);

		var panel = new PanelContainer();
		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.04f, 0.06f, 0.10f, 0.97f);
		panelStyle.BorderWidthLeft = panelStyle.BorderWidthTop = panelStyle.BorderWidthRight = panelStyle.BorderWidthBottom = 1;
		panelStyle.BorderColor = new Color(0.5f, 0.45f, 0.2f, 0.8f);
		panelStyle.ContentMarginLeft = panelStyle.ContentMarginRight = 10;
		panelStyle.ContentMarginTop = panelStyle.ContentMarginBottom = 8;
		panel.AddThemeStyleboxOverride("panel", panelStyle);
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_companionWindow.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 6);
		panel.AddChild(vbox);

		// Pet name + level
		_companionNameLabel = new Label();
		_companionNameLabel.Text = "No Companion";
		_companionNameLabel.AddThemeFontSizeOverride("font_size", 14);
		_companionNameLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.6f));
		_companionNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(_companionNameLabel);

		// HP bar container
		var hpContainer = new HBoxContainer();
		hpContainer.AddThemeConstantOverride("separation", 4);

		var hpLabelLeft = new Label { Text = "HP" };
		hpLabelLeft.AddThemeFontSizeOverride("font_size", 11);
		hpLabelLeft.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
		hpContainer.AddChild(hpLabelLeft);

		_companionHpBar = new ProgressBar();
		_companionHpBar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_companionHpBar.CustomMinimumSize = new Vector2(120, 18);
		_companionHpBar.MaxValue = 100;
		_companionHpBar.Value = 100;
		_companionHpBar.ShowPercentage = false;
		var barStyle = new StyleBoxFlat();
		barStyle.BgColor = new Color(0.15f, 0.5f, 0.15f, 0.9f);
		_companionHpBar.AddThemeStyleboxOverride("fill", barStyle);
		var barBg = new StyleBoxFlat();
		barBg.BgColor = new Color(0.15f, 0.1f, 0.1f, 0.8f);
		_companionHpBar.AddThemeStyleboxOverride("background", barBg);
		hpContainer.AddChild(_companionHpBar);

		_companionHpLabel = new Label { Text = "100%" };
		_companionHpLabel.AddThemeFontSizeOverride("font_size", 11);
		_companionHpLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.9f, 0.8f));
		_companionHpLabel.CustomMinimumSize = new Vector2(36, 0);
		_companionHpLabel.HorizontalAlignment = HorizontalAlignment.Right;
		hpContainer.AddChild(_companionHpLabel);
		vbox.AddChild(hpContainer);

		// State label
		_companionStateLabel = new Label();
		_companionStateLabel.Text = "Following";
		_companionStateLabel.AddThemeFontSizeOverride("font_size", 11);
		_companionStateLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.75f, 0.9f));
		_companionStateLabel.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(_companionStateLabel);

		// Separator
		var sep = new HSeparator();
		sep.CustomMinimumSize = new Vector2(0, 4);
		vbox.AddChild(sep);

		// Command buttons grid (2 columns)
		var btnGrid = new GridContainer();
		btnGrid.Columns = 2;
		btnGrid.AddThemeConstantOverride("h_separation", 4);
		btnGrid.AddThemeConstantOverride("v_separation", 4);
		vbox.AddChild(btnGrid);

		string[] commands = { "Attack", "Follow", "Guard", "Back Off", "Sit", "Taunt", "Health", "Get Lost" };
		string[] cmdKeys = { "attack", "follow", "guard", "backoff", "sit", "taunt", "health", "getlost" };

		for (int i = 0; i < commands.Length; i++)
		{
			var btn = new Button();
			btn.Text = commands[i];
			btn.CustomMinimumSize = new Vector2(110, 28);
			btn.AddThemeFontSizeOverride("font_size", 12);
			string key = cmdKeys[i];
			btn.Pressed += () =>
			{
				_client.SendRaw($"{{\"type\": \"PET_COMMAND\", \"command\": \"{key}\"}}");
			};
			btnGrid.AddChild(btn);
		}
	}

	// â”€â”€â”€ Buff System â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
	/// <summary>
	/// Expected JSON: { "type": "BUFFS_UPDATE", "buffs": [
	///   { "name": "Spirit of Wolf", "duration": 36.0, "maxDuration": 60.0, "beneficial": true },
	///   { "name": "Poison", "duration": 12.0, "maxDuration": 18.0, "beneficial": false },
	///   ...
	/// ]}
	/// </summary>
	private void OnBuffsUpdated(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			if (!root.TryGetProperty("buffs", out var buffs)) return;

			_activeBuffs.Clear();
			_activeSongBuffs.Clear();

			foreach (var buff in buffs.EnumerateArray())
			{
				string name = buff.GetProperty("name").GetString();
				float duration = buff.TryGetProperty("duration", out var durProp) ? (float)durProp.GetDouble() : 0f;
				float maxDuration = buff.TryGetProperty("maxDuration", out var maxDurProp) ? (float)maxDurProp.GetDouble() : duration;
				bool beneficial = buff.TryGetProperty("beneficial", out var benProp) ? benProp.GetBoolean() : true;
				int memIcon = buff.TryGetProperty("memIcon", out var memProp) ? memProp.GetInt32() : 0;
				bool isSong = buff.TryGetProperty("isSong", out var songProp) ? songProp.GetBoolean() : false;

				var buffObj = new ActiveBuff
				{
					Name = name,
					DurationMax = maxDuration,
					DurationRemaining = duration,
					IconNode = null,
					IsBeneficial = beneficial,
					MemIcon = memIcon
				};

				if (isSong && beneficial)
					_activeSongBuffs.Add(buffObj);
				else
					_activeBuffs.Add(buffObj);
			}

			RefreshBuffDisplay();

			GD.Print($"[UI] Buffs updated: {_activeBuffs.Count} active.");
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Buff Error: {ex.Message}"); }
	}

	// â”€â”€â”€ Sit / Stand / Combat â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
			if (wm != null && wm.CurrentTargetId != null && wm.CurrentTargetId != "Player")
			{
				_client.SendRaw($"{{\"type\":\"ATTACK_TARGET\", \"targetId\": \"{wm.CurrentTargetId}\"}}");
			}
			else
			{
				Log("SYSTEM", "You must select a target to attack.");
			}
		}
	}

	private void OnCampPressed()
	{
		// Client-side sitting check for immediate feedback
		if (!_isSitting)
		{
			Log("SYSTEM", "You must be sitting to camp.");
			return;
		}
		_client.SendRaw("{\"type\": \"CAMP\"}");
	}

	private void OnCampComplete()
	{
		if (!IsInstanceValid(this)) return;
		// Save UI and hotbar state before leaving
		SaveFullLayout();
		_hotbarManager?.OnPlayerLogout();
		GD.Print("[UI] Camp complete — returning to character select.");
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}

	// â”€â”€â”€ Status Handling â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
	private void OnCharacterStatusReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try 
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			var character = root.GetProperty("character");

			UpdateBars(character);
			UpdateStatsUI(character);
			UpdateInventoryStats(character);
			
			if (character.TryGetProperty("vision", out var visionDict))
			{
				if (visionDict.TryGetProperty("modeName", out var modeNameProp))
				{
					_visionModeName = modeNameProp.GetString();
				}
				if (visionDict.TryGetProperty("renderStyle", out var renderStyleProp))
				{
					var wmVision = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wmVision != null)
					{
						wmVision.SetVisionStyle(renderStyleProp.GetString());
					}
				}
			}

			// Sit/Stand state
			if (character.TryGetProperty("state", out var stateProp))
			{
				string state = stateProp.GetString();
				bool wasSitting = _isSitting;
				_isSitting = (state == "medding"); 
				if (_sitStandBtn != null) _sitStandBtn.Text = _isSitting ? "Stand" : "Sit";

				// Auto-close spellbook when standing up
				if (wasSitting && !_isSitting && _spellbookUI != null && _spellbookUI.Visible)
				{
					_spellbookUI.Visible = false;
					_pendingMemorizeSpellKey = null;
					_pendingMemorizeSpellName = null;
					Log("SYSTEM", "Your spellbook closes as you stand.");
				}

				// Trigger sit/stand animation on player model
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null) wm.SetPlayerSitting(_isSitting);
			}

			// Auto-fight state (use autoFight flag, not inCombat)
			if (character.TryGetProperty("autoFight", out var autoFightProp))
			{
				_autoFight = autoFightProp.GetBoolean();
				if (_autoFightBtn != null) _autoFightBtn.Text = _autoFight ? "Stop Combat" : "Start Combat";
			}

			// Target frame
			// Self-target overrides server target data
			if (_isSelfTargeted)
			{
				_targetWindow.Visible = true;
				string charName = _charName ?? "You";
				_targetNameLabel.Text = $"{charName} (Lv {_charLevel})";
				_targetHpBar.MaxValue = _maxHp;
				_targetHpBar.Value = Math.Max(0, _currentHp);
				double pct = _maxHp > 0 ? (_currentHp / (double)_maxHp * 100) : 100;
				_targetHpLabel.Text = $"{pct:F0}%";
			}
			else if (character.TryGetProperty("target", out var targetProp) && targetProp.ValueKind != JsonValueKind.Null)
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

			// ── Pet / Companion Status ──
			if (character.TryGetProperty("pet", out var petProp) && petProp.ValueKind != JsonValueKind.Null)
			{
				_hasPet = true;
				string petName = petProp.GetProperty("name").GetString();
				int petHp = (int)petProp.GetProperty("hp").GetDouble();
				int petMaxHp = (int)petProp.GetProperty("maxHp").GetDouble();
				int petLevel = petProp.TryGetProperty("level", out var plProp) ? plProp.GetInt32() : 0;
				string petState = petProp.TryGetProperty("state", out var psProp) ? psProp.GetString() : "follow";
				bool petTaunting = petProp.TryGetProperty("taunting", out var ptProp) && ptProp.GetBoolean();
				bool isCharmed = petProp.TryGetProperty("isCharmed", out var pcProp) && pcProp.GetBoolean();
				string petTarget = petProp.TryGetProperty("target", out var pttProp) && pttProp.ValueKind != JsonValueKind.Null ? pttProp.GetString() : null;

				EnsureCompanionWindow();
				if (_companionWindow != null)
				{
					_companionWindow.Visible = true;
					string typeTag = isCharmed ? "[Charmed]" : "[Pet]";
					_companionNameLabel.Text = $"{petName} Lv{petLevel} {typeTag}";
					_companionHpBar.MaxValue = petMaxHp;
					_companionHpBar.Value = Math.Max(0, petHp);
					double petPct = petMaxHp > 0 ? ((double)petHp / petMaxHp * 100) : 0;
					_companionHpLabel.Text = $"{petPct:F0}%";
					string stateDisplay = petState switch {
						"follow" => "Following",
						"guard" => "Guarding",
						"sit" => "Sitting",
						_ => petState
					};
					if (petTarget != null) stateDisplay += $" → {petTarget}";
					if (petTaunting) stateDisplay += " [Taunt]";
					_companionStateLabel.Text = stateDisplay;
				}
			}
			else
			{
				if (_hasPet)
				{
					_hasPet = false;
					if (_companionWindow != null) _companionWindow.Visible = false;
				}
			}

			// Extended Targets
			if (character.TryGetProperty("extendedTargets", out var extArr) && extArr.ValueKind == JsonValueKind.Array)
			{
				int i = 0;
				foreach (var extMob in extArr.EnumerateArray())
				{
					if (i >= 10) break; // Hardcap at 10 buttons
					
					var idProp = extMob.GetProperty("id");
					string mId = idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt32().ToString() : idProp.GetString();
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

			// Utility Skills (for Skills tab)
			if (character.TryGetProperty("availableSkills", out var skillsArr) && skillsArr.ValueKind == JsonValueKind.Array)
			{
				_availableSkills.Clear();
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null) wm.PlayerHasStealthSkill = false;
				foreach (var sk in skillsArr.EnumerateArray())
				{
					string sName = sk.GetString();
					if (!string.IsNullOrEmpty(sName))
					{
						sName = char.ToUpper(sName[0]) + sName.Substring(1);
						_availableSkills.Add(sName);
						if (sName.ToLower() == "sneak" || sName.ToLower() == "hide")
						{
							if (wm != null) wm.PlayerHasStealthSkill = true;
						}
					}
				}
			}

			// Auto-populate action bar if it's currently empty (e.g., first login)
			bool hasAssignedAbilities = false;
			for(int i=0; i<_assignedAbilities.Length; i++) { if(!string.IsNullOrEmpty(_assignedAbilities[i])) hasAssignedAbilities = true; }
			if (!hasAssignedAbilities)
			{
				for(int i=1; i<8; i++) 
				{
					if (i-1 < _availableAbilities.Count) _assignedAbilities[i] = _availableAbilities[i-1];
				}
			}

			bool hasAssignedSkills = false;
			for(int i=0; i<_assignedSkills.Length; i++) { if(!string.IsNullOrEmpty(_assignedSkills[i])) hasAssignedSkills = true; }
			if (!hasAssignedSkills)
			{
				var slotable = new System.Collections.Generic.List<string>();
				foreach (var s in _availableSkills) { if (!_nonSlotableSkills.Contains(s)) slotable.Add(s); }
				for(int i=0; i<8; i++) 
				{
					if (i < slotable.Count) _assignedSkills[i] = slotable[i];
				}
			}
			
			// Refresh UI to show newly assigned or populated labels
			if (_actionGrid != null) SwitchActionTab(_actionCurrentTab, _actionTabButtons, _actionGrid);

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


			// Zone connections â€” rebuild buttons only when zone changes
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
				_pendingSpawnY = spawnProp.TryGetProperty("z", out var zp) ? zp.GetSingle() : 0f;
				_isInitialLoadPending = true;

				// Pass player appearance to WorldManager for correct model
				int raceId = character.TryGetProperty("raceId", out var ridProp) ? ridProp.GetInt32() : 1;
				int gender = character.TryGetProperty("gender", out var genProp) ? genProp.GetInt32() : 0;
				int face = character.TryGetProperty("face", out var faceProp) ? faceProp.GetInt32() : 0;
				string equipVisuals = character.TryGetProperty("equipVisuals", out var evProp) ? evProp.ToString() : "";
				var wmApp = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wmApp != null) wmApp.SetPlayerAppearance(raceId, gender, face, equipVisuals);
				
				if (_loadingLayer != null) 
				{
					_loadingLayer.Show();
					_flavorLabel.Text = "Constructing Terrain...";
					_loadingBar.Value = 10;
				}
				
				GD.Print($"[UI] Server requested spawn at {_pendingSpawnX}, {_pendingSpawnZ}. race={raceId} gender={gender} face={face}. Delaying until entities ready...");
			}

			// Update equipment visuals on every STATUS (handles equip/unequip without respawn)
			if (character.TryGetProperty("equipVisuals", out var equipVisProp))
			{
				string equipVis = equipVisProp.ToString();
				var wmEquip = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wmEquip != null) wmEquip.UpdatePlayerEquipVisuals(equipVis);
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
			// Format zone ID into display name: "west_karana" â†’ "West Karana"
			string displayName = FormatZoneName(targetZoneId);

			var btn = new Button
			{
				Text = $"â†’ {displayName}",
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

		public void RefreshBuffDisplay()
		{
			RenderBuffsToContainer(_buffBar, _activeBuffs);
			RenderBuffsToContainer(_songBar, _activeSongBuffs);
			
			// Show windows only when they have content
			if (_buffBarWindow != null)
			{
				// Ensure transparent background before first show
				if (!_buffBarWindow.HasMeta("StyleApplied"))
				{
					var ts = new StyleBoxFlat();
					ts.BgColor = new Color(0, 0, 0, 0);
					_buffBarWindow.AddThemeStyleboxOverride("embedded_border", ts);
					_buffBarWindow.AddThemeStyleboxOverride("embedded_unfocused_border", ts);
					_buffBarWindow.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
					_buffBarWindow.SetMeta("StyleApplied", true);
				}
				_buffBarWindow.Visible = _activeBuffs.Count > 0;
			}
			if (_songBarWindow != null)
			{
				if (!_songBarWindow.HasMeta("StyleApplied"))
				{
					var ts = new StyleBoxFlat();
					ts.BgColor = new Color(0, 0, 0, 0);
					_songBarWindow.AddThemeStyleboxOverride("embedded_border", ts);
					_songBarWindow.AddThemeStyleboxOverride("embedded_unfocused_border", ts);
					_songBarWindow.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
					_songBarWindow.SetMeta("StyleApplied", true);
				}
				_songBarWindow.Visible = _activeSongBuffs.Count > 0;
			}
		}

		private void RenderBuffsToContainer(Container container, List<ActiveBuff> buffs)
		{
			if (container == null) return;
			var slots = container.GetChildren();
			
			for (int i = 0; i < slots.Count; i++)
			{
				if (slots[i] is Panel slot)
				{
					var iconRect = slot.GetNodeOrNull<TextureRect>("Icon");
					var label = slot.GetNodeOrNull<Label>("Label");
					var durationBar = slot.GetNodeOrNull<ProgressBar>("Duration");
					
					if (i < buffs.Count)
					{
						var buff = buffs[i];
						buff.IconNode = slot;
						slot.SetMeta("ActiveBuffIndex", i);
						slot.SetMeta("IsSong", container == _songBar);
						
						slot.TooltipText = buff.Name;
						
						if (iconRect != null && buff.MemIcon > 0)
						{
							var iconMgr = IconManager.Instance;
							if (iconMgr != null)
							{
								var tex = iconMgr.GetSpellGem(buff.MemIcon);
								if (tex != null)
								{
									iconRect.Texture = tex;
									label.Text = "";
									iconRect.Visible = true;
								}
								else
								{
									label.Text = buff.Name.Length > 5 ? buff.Name[..5] : buff.Name;
									iconRect.Visible = false;
								}
							}
						}
						else
						{
							if (iconRect != null) iconRect.Visible = false;
							if (label != null) label.Text = buff.Name.Length > 5 ? buff.Name[..5] : buff.Name;
						}
						
						if (durationBar != null)
						{
							durationBar.Visible = true;
							durationBar.MaxValue = buff.DurationMax;
							durationBar.Value = buff.DurationRemaining;
						}
						
						if (!buff.IsBeneficial)
						{
							var style = new StyleBoxFlat();
							style.BgColor = new Color(0.15f, 0.08f, 0.08f, 0.9f);
							style.BorderWidthLeft = 1;
							style.BorderWidthTop = 1;
							style.BorderWidthRight = 1;
							style.BorderWidthBottom = 1;
							style.BorderColor = new Color(1f, 0.3f, 0.3f, 1f);
							slot.AddThemeStyleboxOverride("panel", style);
						}
						else
						{
							slot.RemoveThemeStyleboxOverride("panel");
						}
						
						if (!slot.HasMeta("HasGuiInput"))
						{
							slot.GuiInput += (ev) => OnSlotGuiInput(ev, slot);
							slot.SetMeta("HasGuiInput", true);
						}
					}
					else
					{
						slot.SetMeta("ActiveBuffIndex", -1);
						slot.TooltipText = "";
						slot.RemoveThemeStyleboxOverride("panel");
						if (iconRect != null) { iconRect.Texture = null; iconRect.Visible = false; }
						if (label != null) label.Text = "";
						if (durationBar != null) durationBar.Visible = false;
					}
				}
			}
		}

		private void OnSlotGuiInput(InputEvent ev, Panel slot)
		{
			if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
			{
				int index = slot.GetMeta("ActiveBuffIndex").AsInt32();
				if (index == -1) return;
				
				bool isSong = slot.GetMeta("IsSong").AsBool();
				var buffList = isSong ? _activeSongBuffs : _activeBuffs;
				
				if (index >= buffList.Count) return;
				
				var buff = buffList[index];
				
				slot.AcceptEvent();
				_contextMenuTargetBuff = buff;
				_buffContextMenu.Clear();
				_buffContextMenu.AddItem("Details", 0);
				
				if (buff.IsBeneficial)
				{
					_buffContextMenu.AddItem("Remove", 1);
				}
				
				var mousePos = GetGlobalMousePosition();
				_buffContextMenu.Position = new Vector2I((int)mousePos.X, (int)mousePos.Y);
				_buffContextMenu.Popup();
			}
		}

}

