using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class MainUI
{
	private string[] _assignedAbilities = new string[80];
	private string[] _assignedSkills = new string[80];
	private int[] _actionPages = new int[3]; // 0=Socials, 1=Abilities, 2=Skills
	
	private string _actionCursorType = null; // "social", "ability", "skill"
	private int _actionCursorSourceIdx = -1;
	private string _actionCursorData = null;
	private Label _actionCursorLabel;
	private bool _autoRanged = false;

	private Label GetActionCursorLabel()
	{
		if (_actionCursorLabel == null)
		{
			_actionCursorLabel = new Label();
			_actionCursorLabel.Visible = false;
			_actionCursorLabel.ZIndex = 600;
			_actionCursorLabel.MouseFilter = MouseFilterEnum.Ignore;
			_actionCursorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.5f));
			_actionCursorLabel.AddThemeFontSizeOverride("font_size", 11);
			AddChild(_actionCursorLabel);
		}
		return _actionCursorLabel;
	}

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
			"weak normal vision" => "Normivision",
			"speak normal vision" => "Normivision",
			"eak normal vision" => "Normivision", // Typo catch
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

	private void ChangeActionPage(int delta)
	{
		_actionPages[_actionCurrentTab] = (_actionPages[_actionCurrentTab] + delta + 10) % 10;
		if (_socialPageLabel != null) _socialPageLabel.Text = (_actionPages[_actionCurrentTab] + 1).ToString();
	}

	private void SwitchActionTab(int tabIndex, Button[] tabBtns, GridContainer grid)
	{
		_actionCurrentTab = tabIndex;
		StyleActionTabs(tabBtns, tabIndex);
		
		// Show nav row for all tabs and maintain grid offset
		if (_socialNavRow != null) _socialNavRow.Visible = true;
		if (_socialPageLabel != null) _socialPageLabel.Text = (_actionPages[tabIndex] + 1).ToString();
		if (grid != null) grid.OffsetTop = 80;
		
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
					int socialIdx = _actionPages[0] * 8 + i;
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
					if (_actionPages[1] == 0 && i == 0) btn.Text = _autoFight ? "Stop" : "Attack";
					else if (_actionPages[1] == 0 && i == 1) btn.Text = _autoRanged ? "Stop Ranged" : "Ranged";
					else
					{
						int idx = _actionPages[1] * 8 + i;
						string assigned = _assignedAbilities[idx];
						btn.Text = string.IsNullOrEmpty(assigned) ? $"{i + 1}" : ShortenName(assigned);
					}
					break;
				case 2: // Skills
				{
					int idx = _actionPages[2] * 8 + i;
					string assigned = _assignedSkills[idx];
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

		// Handle active action cursor drops/cancels
		if (_actionCursorType != null)
		{
			if (mb.ButtonIndex == MouseButton.Left)
			{
				HandleActionCursorDrop(btnIdx);
			}
			else if (mb.ButtonIndex == MouseButton.Right)
			{
				ClearActionCursor();
			}
			return; // Consumed
		}

		switch (_actionCurrentTab)
		{
			case 0: // Socials
			{
				int socialIdx = _actionPages[0] * 8 + btnIdx;
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
				else if (mb.ButtonIndex == MouseButton.Right)
				{
					if (mb.CtrlPressed)
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
						GetViewport().SetInputAsHandled();
						return;
					}
					else
					{
						ShowSocialContextMenu(socialIdx, btn);
					}
				}
				break;
			}
			case 1: // Abilities
			{
				int globalIdx = _actionPages[1] * 8 + btnIdx;
				if (_actionPages[1] == 0 && btnIdx == 0)
				{
					if (mb.ButtonIndex == MouseButton.Right)
					{
						if (mb.CtrlPressed)
						{
							_hotbarManager?.StartAbilityDrag("Attack");
							GetViewport().SetInputAsHandled();
							return;
						}
					}
					else if (mb.ButtonIndex == MouseButton.Left)
					{
						OnAbilityPressed(0);
						SwitchActionTab(_actionCurrentTab, _actionTabButtons, _actionGrid);
					}
				}
				else if (_actionPages[1] == 0 && btnIdx == 1)
				{
					if (mb.ButtonIndex == MouseButton.Right)
					{
						if (mb.CtrlPressed)
						{
							_hotbarManager?.StartAbilityDrag("Ranged");
							GetViewport().SetInputAsHandled();
							return;
						}
					}
					else if (mb.ButtonIndex == MouseButton.Left)
					{
						_autoRanged = !_autoRanged;
						if (_autoRanged)
							_client.SendRaw("{\"type\": \"START_RANGED\"}");
						else
							_client.SendRaw("{\"type\": \"STOP_RANGED\"}");
						SwitchActionTab(_actionCurrentTab, _actionTabButtons, _actionGrid);
					}
				}
				else
				{
					string assigned = _assignedAbilities[globalIdx];
					if (mb.ButtonIndex == MouseButton.Right)
					{
						if (mb.CtrlPressed && !string.IsNullOrEmpty(assigned))
						{
							_hotbarManager?.StartAbilityDrag(assigned);
							GetViewport().SetInputAsHandled();
							return;
						}
						ShowActionContextMenu(globalIdx, _assignedAbilities, "ability", btn);
					}
					else if (mb.ButtonIndex == MouseButton.Left && !string.IsNullOrEmpty(assigned))
					{
						GD.Print($"[UI] Ability pressed: {assigned}");
						_client.SendRaw($"{{\"type\":\"ABILITY\", \"ability\":\"{assigned}\"}}");
					}
				}
				break;
			}
			case 2: // Skills
			{
				int globalIdx = _actionPages[2] * 8 + btnIdx;
				string assigned = _assignedSkills[globalIdx];
				if (mb.ButtonIndex == MouseButton.Right)
				{
					if (mb.CtrlPressed && !string.IsNullOrEmpty(assigned))
					{
						_hotbarManager?.StartAbilityDrag(assigned); // Abilities and skills use the same drag payload
						GetViewport().SetInputAsHandled();
						return;
					}
					ShowActionContextMenu(globalIdx, _assignedSkills, "skill", btn);
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
				break;
			}
		}
	}

		private EditSocialDialog _editSocialDialog;

	private EditSocialDialog GetEditSocialDialog()
	{
		if (_editSocialDialog == null)
		{
			_editSocialDialog = new EditSocialDialog();
			_editSocialDialog.Visible = false;
			_editSocialDialog.ZIndex = 650;
			_editSocialDialog.SocialAccepted += OnSocialEdited;
			AddChild(_editSocialDialog);
		}
		return _editSocialDialog;
	}

	private void OnSocialEdited(int socialIndex, string name, int color, string line0, string line1, string line2, string line3, string line4)
	{
		if (_hotbarManager == null) return;
		var sm = _hotbarManager.GetSocialManager();
		if (sm == null || socialIndex < 0 || socialIndex >= sm.Socials.Length) return;

		var social = sm.Socials[socialIndex];
		social.Name = name;
		social.Color = color;
		social.Lines[0] = line0;
		social.Lines[1] = line1;
		social.Lines[2] = line2;
		social.Lines[3] = line3;
		social.Lines[4] = line4;

		SwitchActionTab(_actionCurrentTab, _actionTabButtons, _actionGrid);
	}

	private void ShowSocialContextMenu(int socialIdx, Button anchorBtn)
	{
		if (_hotbarManager == null) return;
		var sm = _hotbarManager.GetSocialManager();
		if (sm == null || socialIdx >= sm.Socials.Length) return;

		var social = sm.Socials[socialIdx];
		var popup = new PopupMenu();
		popup.Name = "SocialContext";
		popup.AddThemeFontSizeOverride("font_size", 12);

		if (!social.IsEmpty)
		{
			popup.AddItem("Copy", 2);
			popup.AddItem("Edit", 0);
			popup.AddItem("Clear", 1);
		}
		else
		{
			popup.AddItem("Edit", 0);
		}

		popup.IdPressed += (id) =>
		{
			if (id == 2) // Copy
			{
				_actionCursorType = "social";
				_actionCursorSourceIdx = socialIdx;
				_actionCursorData = socialIdx.ToString();
				var lbl = GetActionCursorLabel();
				lbl.Text = $"[Copy] {social.Name}";
				lbl.Modulate = SocialManager.EQColors[Math.Clamp(social.Color, 0, 19)];
				lbl.Visible = true;
			}
			else if (id == 0) // Edit
			{
				var dialog = GetEditSocialDialog();
				dialog.OpenForSocial(socialIdx, social);
			}
			else if (id == 1) // Clear
			{
				sm.Socials[socialIdx] = new SocialManager.Social { Color = 0, Name = "", Lines = new string[5] };
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
			popup.AddItem("Copy", 4);
			popup.AddItem("Clear", 2);
			popup.AddItem("Set", 3);
		}

		popup.IdPressed += (id) =>
		{
			if (id == 4) // Copy
			{
				_actionCursorType = tabType;
				_actionCursorSourceIdx = slotIndex;
				_actionCursorData = assignments[slotIndex];
				var lbl = GetActionCursorLabel();
				lbl.Text = $"[Copy] {assignments[slotIndex]}";
				lbl.Visible = true;
			}
			else if (id == 2) // Clear
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

	private void HandleActionCursorDrop(int btnIdx)
	{
		if (_actionCursorType == null) return;

		if (_actionCurrentTab == 0 && _actionCursorType == "social")
		{
			int destIdx = _actionPages[0] * 8 + btnIdx;
			if (_hotbarManager != null)
			{
				var sm = _hotbarManager.GetSocialManager();
				if (sm != null)
				{
					// Swap
					var temp = sm.Socials[destIdx];
					sm.Socials[destIdx] = sm.Socials[_actionCursorSourceIdx];
					sm.Socials[_actionCursorSourceIdx] = temp;
					SwitchActionTab(0, _actionTabButtons, _actionGrid);
				}
			}
		}
		else if (_actionCurrentTab == 1 && _actionCursorType == "ability")
		{
			if (_actionPages[1] == 0 && btnIdx == 0)
			{
				Log("SYSTEM", "Cannot overwrite the Attack button.");
			}
			else if (_actionPages[1] == 0 && btnIdx == 1)
			{
				Log("SYSTEM", "Cannot overwrite the Ranged button.");
			}
			else
			{
				int destIdx = _actionPages[1] * 8 + btnIdx;
				// Swap
				string temp = _assignedAbilities[destIdx];
				_assignedAbilities[destIdx] = _assignedAbilities[_actionCursorSourceIdx];
				_assignedAbilities[_actionCursorSourceIdx] = temp;
				SwitchActionTab(1, _actionTabButtons, _actionGrid);
			}
		}
		else if (_actionCurrentTab == 2 && _actionCursorType == "skill")
		{
			int destIdx = _actionPages[2] * 8 + btnIdx;
			// Swap
			string temp = _assignedSkills[destIdx];
			_assignedSkills[destIdx] = _assignedSkills[_actionCursorSourceIdx];
			_assignedSkills[_actionCursorSourceIdx] = temp;
			SwitchActionTab(2, _actionTabButtons, _actionGrid);
		}
		else
		{
			Log("SYSTEM", "You cannot place that type of action here.");
			return; // Don't clear cursor if they clicked wrong tab
		}

		ClearActionCursor();
	}

	private void ClearActionCursor()
	{
		_actionCursorType = null;
		_actionCursorSourceIdx = -1;
		_actionCursorData = null;
		if (_actionCursorLabel != null)
		{
			_actionCursorLabel.Visible = false;
			_actionCursorLabel.Modulate = Colors.White;
		}
	}
}
