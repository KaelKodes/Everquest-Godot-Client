using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class MainUI
{
	private string[] _assignedAbilities = new string[8];
	private string[] _assignedSkills = new string[8];

	private void OnAbilityPressed(int index)
	{
		// Slot 0 = Attack toggle
		if (index == 0)
		{
			if (_autoFight)
			{
				_client.SendRaw("{\"type\":\"STOP_COMBAT\"}");
			}
			else
			{
				// Check melee range before engaging
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null && !wm.IsTargetInRange(WorldManager.MELEE_RANGE))
				{
					Log("COMBAT", "Your target is too far away!");
					return;
				}
				if (wm != null && wm.CurrentTargetId != null && wm.CurrentTargetId != "Player")
				{
					_client.SendRaw($"{{\"type\":\"ATTACK_TARGET\", \"targetId\": \"{wm.CurrentTargetId}\"}}");
				}
				else
				{
					Log("SYSTEM", "You must select a target to attack.");
				}
			}
			return;
		}
		// Slots 1-6 map to _availableAbilities[0-5]
		int abilIndex = index - 1;
		if (abilIndex < 0 || abilIndex >= _availableAbilities.Count) return;
		string ability = _availableAbilities[abilIndex].ToLower();

		// Only melee combat abilities require a target in range
		var meleeAbilities = new System.Collections.Generic.HashSet<string> { "kick", "bash", "taunt", "backstab", "disarm" };
		if (meleeAbilities.Contains(ability))
		{
			var wmCheck = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
			if (wmCheck != null && !wmCheck.IsTargetInRange(WorldManager.MELEE_RANGE))
			{
				Log("COMBAT", "Your target is too far away!");
				return;
			}
		}

		_client.SendRaw($"{{\"type\":\"ABILITY\",\"ability\":\"{ability}\"}}");
	}

	/// <summary>Handles ability activation from the new ActionPanel (by name instead of slot index).</summary>
	private void OnActionPanelAbilityActivated(string abilityName)
	{
		if (string.IsNullOrEmpty(abilityName)) return;

		// Special: Attack toggle
		if (abilityName.Equals("Attack", StringComparison.OrdinalIgnoreCase))
		{
			OnAbilityPressed(0);
			return;
		}

		string ability = abilityName.ToLower();

		// Only melee combat abilities require a target in range
		var meleeAbilities = new System.Collections.Generic.HashSet<string> { "kick", "bash", "taunt", "backstab", "disarm" };
		if (meleeAbilities.Contains(ability))
		{
			var wmCheck = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
			if (wmCheck != null && !wmCheck.IsTargetInRange(WorldManager.MELEE_RANGE))
			{
				Log("COMBAT", "Your target is too far away!");
				return;
			}
		}

		_client.SendRaw($"{{\"type\":\"ABILITY\",\"ability\":\"{ability}\"}}");
	}

	/// <summary>Handles skill activation from the new ActionPanel (utility skills like Hide, Sneak, etc.).</summary>
	private void OnActionPanelSkillActivated(string skillName)
	{
		if (string.IsNullOrEmpty(skillName)) return;
		string skill = skillName.ToLower();

		switch (skill)
		{
			case "hide":
				// Send USE_HIDE to the server — also toggle crouch if not already crouching
				_client.SendRaw("{\"type\":\"USE_HIDE\",\"hiding\":true}");
				break;
			case "sneak":
				// Toggle sneak via UPDATE_SNEAK
				_client.SendRaw("{\"type\":\"UPDATE_SNEAK\",\"sneaking\":true}");
				break;
			case "mining":
				// Mining: send MINE with current target ID
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null && wm.CurrentTargetId != null)
				{
					_client.SendRaw($"{{\"type\":\"MINE\",\"targetId\":\"{wm.CurrentTargetId}\"}}");
				}
				else
				{
					Log("SYSTEM", "You must target a mining node to mine.");
				}
				break;
			default:
				// Generic skill activation
				_client.SendRaw($"{{\"type\":\"ABILITY\",\"ability\":\"{skill}\"}}");
				break;
		}
	}

	// ── Action Tab Switching (for the upgraded ActionBarWindow) ──────

	// Non-slotable skills that shouldn't appear in the Skills tab
	private static readonly HashSet<string> _nonSlotableSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"alcohol tolerance", "swimming"
	};

	/// <summary>Shorten skill/ability names for the grid buttons.</summary>
	private string ShortenName(string name)
	{
		if (string.IsNullOrEmpty(name)) return name;
		// Specific overrides
		var lower = name.ToLower();
		return lower switch
		{
			"bind wound" => "Bind",
			"sense heading" => "Sense",
			"safe fall" => "Safe Fall",
			"apply poison" => "Poison",
			"pick lock" => "Pick Lk",
			"pick pocket" => "Pick Pkt",
			"sense traps" => "Sns Trap",
			"disarm traps" => "Dis Trap",
			"double attack" => "Dbl Atk",
			"dual wield" => "Dual Wld",
			"round kick" => "Rnd Kick",
			"tiger claw" => "Tiger",
			"eagle strike" => "Eagle",
			"dragon punch" => "Dragon",
			"flying kick" => "Fly Kick",
			"feign death" => "Feign",
			"lay on hands" => "Lay Hnds",
			"harm touch" => "Harm Tch",
			"instill doubt" => "Doubt",
			"begging" => "Beg",
			"fishing" => "Fish",
			"singing" => "Sing",
			"swimming" => "Swim",
			_ => TitleCase(DropIng(name))
		};
	}

	private string DropIng(string name)
	{
		if (name.EndsWith("ing", StringComparison.OrdinalIgnoreCase) && name.Length > 4)
			return name.Substring(0, name.Length - 3);
		return name;
	}

	private string TitleCase(string s)
	{
		if (string.IsNullOrEmpty(s)) return s;
		return char.ToUpper(s[0]) + s.Substring(1).ToLower();
	}

	private void ChangeSocialPage(int delta)
	{
		_socialPage = (_socialPage + delta + SocialManager.PAGES) % SocialManager.PAGES;
		if (_socialPageLabel != null) _socialPageLabel.Text = (_socialPage + 1).ToString();
	}

	private void SwitchActionTab(int tabIndex, Button[] tabBtns, GridContainer grid)
	{
		_actionCurrentTab = tabIndex;
		StyleActionTabs(tabBtns, tabIndex);
		
		// Show/hide social page nav and shift grid accordingly
		if (_socialNavRow != null) _socialNavRow.Visible = (tabIndex == 0);
		if (grid != null) grid.OffsetTop = (tabIndex == 0) ? 80 : 60;
		
		if (grid == null) return;
		
		// Update the 8 grid buttons based on which tab is active
		for (int i = 0; i < 8; i++)
		{
			var btn = grid.GetChildOrNull<Button>(i);
			if (btn == null) continue;

			switch (tabIndex)
			{
				case 0: // Socials
				{
					int socialIdx = _socialPage * 8 + i;
					if (_hotbarManager != null)
					{
						var sm = _hotbarManager.GetSocialManager();
						if (sm != null && socialIdx < sm.Socials.Length)
						{
							var social = sm.Socials[socialIdx];
							btn.Text = social.IsEmpty ? $"{i + 1}" : social.Name;
						}
						else btn.Text = $"{i + 1}";
					}
					else btn.Text = $"{i + 1}";
					break;
				}
				case 1: // Abilities
					if (i == 0) btn.Text = _autoFight ? "Stop" : "Attack";
					else
					{
						string assigned = _assignedAbilities[i];
						btn.Text = string.IsNullOrEmpty(assigned) ? $"{i + 1}" : ShortenName(assigned);
					}
					break;
				case 2: // Skills
				{
					string assigned = _assignedSkills[i];
					btn.Text = string.IsNullOrEmpty(assigned) ? $"{i + 1}" : ShortenName(assigned);
					break;
				}
			}
		}
	}

	private void StyleActionTabs(Button[] tabBtns, int activeIndex)
	{
		if (tabBtns == null) return;
		for (int i = 0; i < tabBtns.Length; i++)
		{
			var style = new StyleBoxFlat();
			if (i == activeIndex)
			{
				style.BgColor = new Color(0.2f, 0.18f, 0.1f, 0.95f);
				style.BorderColor = new Color(0.7f, 0.6f, 0.3f, 1f);
				style.SetBorderWidthAll(1);
				style.BorderWidthBottom = 2;
			}
			else
			{
				style.BgColor = new Color(0.08f, 0.08f, 0.1f, 0.85f);
				style.BorderColor = new Color(0.3f, 0.25f, 0.15f, 0.6f);
				style.SetBorderWidthAll(1);
			}
			style.SetCornerRadiusAll(3);
			tabBtns[i].AddThemeStyleboxOverride("normal", style);
			tabBtns[i].AddThemeColorOverride("font_color",
				i == activeIndex ? new Color(0.95f, 0.85f, 0.5f) : new Color(0.6f, 0.55f, 0.4f));
		}
	}

	public void ToggleActionBarWindow()
	{
		if (_actionBarWindow == null) return;
		_actionBarWindow.Visible = !_actionBarWindow.Visible;
	}

	private void OnActionGridSlotInput(InputEvent ev, int btnIdx, Button btn)
	{
		if (ev is not InputEventMouseButton mb || !mb.Pressed) return;

		switch (_actionCurrentTab)
		{
			case 0: // Socials
			{
				int socialIdx = _socialPage * 8 + btnIdx;
				if (mb.ButtonIndex == MouseButton.Left)
				{
					if (_hotbarManager != null)
					{
						var sm = _hotbarManager.GetSocialManager();
						if (sm != null && socialIdx < sm.Socials.Length)
						{
							sm.ExecuteSocial(socialIdx);
						}
					}
				}
				else if (mb.ButtonIndex == MouseButton.Middle)
				{
					if (_hotbarManager != null)
					{
						var sm = _hotbarManager.GetSocialManager();
						if (sm != null && socialIdx < sm.Socials.Length && !sm.Socials[socialIdx].IsEmpty)
						{
							var social = sm.Socials[socialIdx];
							_hotbarManager?.StartSocialDrag(socialIdx, social.Name, social.Color);
						}
					}
				}
				// Social right click not implemented here yet
				break;
			}
			case 1: // Abilities
			{
				if (btnIdx == 0)
				{
					if (mb.ButtonIndex == MouseButton.Left)
					{
						OnAutoFightPressed();
						SwitchActionTab(_actionCurrentTab, _actionTabButtons, _actionGrid);
					}
				}
				else
				{
					string assigned = _assignedAbilities[btnIdx];
					if (mb.ButtonIndex == MouseButton.Right)
					{
						ShowActionContextMenu(btnIdx, _assignedAbilities, "ability", btn);
					}
					else if (mb.ButtonIndex == MouseButton.Left && !string.IsNullOrEmpty(assigned))
					{
						GD.Print($"[UI] Ability pressed: {assigned}");
						_client.SendRaw($"{{\"type\":\"ABILITY\", \"ability\":\"{assigned}\"}}");
					}
					else if (mb.ButtonIndex == MouseButton.Middle && !string.IsNullOrEmpty(assigned))
					{
						_hotbarManager?.StartAbilityDrag(assigned);
					}
				}
				break;
			}
			case 2: // Skills
			{
				string assigned = _assignedSkills[btnIdx];
				if (mb.ButtonIndex == MouseButton.Right)
				{
					ShowActionContextMenu(btnIdx, _assignedSkills, "skill", btn);
				}
				else if (mb.ButtonIndex == MouseButton.Left && !string.IsNullOrEmpty(assigned))
				{
					string skill = assigned.ToLower();
					switch (skill)
					{
						case "hide":
							_client.SendRaw("{\"type\":\"USE_HIDE\",\"hiding\":true}");
							break;
						case "sneak":
							_client.SendRaw("{\"type\":\"UPDATE_SNEAK\",\"sneaking\":true}");
							break;
						default:
							_client.SendRaw($"{{\"type\":\"ABILITY\", \"ability\":\"{skill}\"}}");
							break;
					}
				}
				else if (mb.ButtonIndex == MouseButton.Middle && !string.IsNullOrEmpty(assigned))
				{
					_hotbarManager?.StartAbilityDrag(assigned); // Abilities and skills use the same drag payload
				}
				break;
			}
		}
	}

	private void ShowActionContextMenu(int slotIndex, string[] assignments, string tabType, Button anchorBtn)
	{
		var popup = new PopupMenu();
		popup.Name = "SlotContext";
		popup.AddThemeFontSizeOverride("font_size", 12);
        
		string assigned = assignments[slotIndex];
		bool isEmpty = string.IsNullOrEmpty(assigned);

		if (isEmpty)
		{
			popup.AddItem("Set", 3);
		}
		else
		{
			popup.AddItem("Clear", 2);
			popup.AddItem("Set", 3);
		}

		popup.IdPressed += (id) =>
		{
			if (id == 2) // Clear
			{
				assignments[slotIndex] = "";
				SwitchActionTab(_actionCurrentTab, _actionTabButtons, _actionGrid);
			}
			else if (id == 3) // Set
			{
				ShowSkillPicker(slotIndex, assignments, tabType, anchorBtn);
			}
			popup.QueueFree();
		};

		popup.PopupHide += () => popup.QueueFree();

		AddChild(popup);
		var mousePos = GetGlobalMousePosition();
		popup.Position = new Vector2I((int)mousePos.X, (int)mousePos.Y);
		popup.Popup();
	}

	private void ShowSkillPicker(int slotIndex, string[] assignments, string tabType, Button anchorBtn)
	{
		var popup = new PopupMenu();
		popup.Name = "SkillPicker";
		popup.AddThemeFontSizeOverride("font_size", 12);

		var list = new List<string>();
		if (tabType == "ability")
		{
			foreach (var a in _availableAbilities) list.Add(a);
		}
		else
		{
			foreach (var s in _availableSkills)
			{
				if (!_nonSlotableSkills.Contains(s)) list.Add(s);
			}
		}

		if (list.Count == 0)
		{
			popup.AddItem("(none available)");
			popup.SetItemDisabled(0, true);
		}
		else
		{
			for (int i = 0; i < list.Count; i++)
			{
				popup.AddItem(list[i]);
			}
		}

		popup.IdPressed += (id) =>
		{
			if (id >= 0 && id < list.Count)
			{
				assignments[slotIndex] = list[(int)id];
				SwitchActionTab(_actionCurrentTab, _actionTabButtons, _actionGrid);
			}
			popup.QueueFree();
		};

		popup.PopupHide += () => popup.QueueFree();

		AddChild(popup);
		var mousePos = GetGlobalMousePosition();
		popup.Position = new Vector2I((int)mousePos.X, (int)mousePos.Y);
		popup.Popup();
	}

}
