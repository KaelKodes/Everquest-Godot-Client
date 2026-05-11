using Godot;
using System;
using System.Text.Json;

public partial class MainUI
{
	private RichTextLabel _partyLog;
	private RichTextLabel _guildLog;
	private string _currentChatTab = "Main";
	private OptionButton _chatChannelSelect;

	private void OnCombatLogReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			if (!root.TryGetProperty("events", out var events)) return;

			foreach (var evt in events.EnumerateArray())
			{
				string eventType = evt.GetProperty("event").GetString();
				FormatAndLogEvent(evt, eventType);
			}
		}
		catch (Exception ex) { GD.PrintErr($"[UI] CombatLog Error: {ex.Message}"); }
	}

	private void FormatAndLogEvent(JsonElement evt, string eventType)
	{
		switch (eventType)
		{
			case "MELEE_HIT":
			{
				string src = evt.GetProperty("source").GetString();
				string tgt = evt.GetProperty("target").GetString();
				string srcId = evt.TryGetProperty("sourceId", out var sIdProp) && sIdProp.ValueKind != JsonValueKind.Null ? sIdProp.GetString() : "";
				string tgtId = evt.TryGetProperty("targetId", out var tIdProp) && tIdProp.ValueKind != JsonValueKind.Null ? tIdProp.GetString() : "";
				
				int dmg = evt.GetProperty("damage").GetInt32();
				string text = "slash";
				if (evt.TryGetProperty("text", out var tProp) && tProp.ValueKind != JsonValueKind.Null)
				{
					string val = tProp.GetString();
					if (!string.IsNullOrEmpty(val)) text = val;
				}
				
				string typeStr = "slash";
				if (evt.TryGetProperty("type", out var typeProp) && typeProp.ValueKind != JsonValueKind.Null)
				{
					string val = typeProp.GetString();
					if (!string.IsNullOrEmpty(val)) typeStr = val;
				}

				if (src == "You")
					Log("HIT", $"You hit {tgt} for {dmg} points of damage.");
				else
					Log("HIT_TAKEN", $"{src} hits YOU for {dmg} points of damage.");
					
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null) 
				{
					bool isHeavy = dmg >= 25 || text.Contains("Bash") || text.Contains("Kick") || text.Contains("Backstab") || text.Contains("Crush") || text.Contains("Critical");
					string hitAction = isHeavy ? "hit_heavy" : "hit";
					
					if (!string.IsNullOrEmpty(tgtId)) wm.TriggerEntityActionById(tgtId, hitAction);
					else wm.TriggerEntityAction(tgt, hitAction);
					
					// Player attack animations are client-timer-driven; only trigger NPC swings here
					if (src != "You")
					{
						if (!string.IsNullOrEmpty(srcId)) wm.TriggerCombatAnimationById(srcId, typeStr, true);
						else wm.TriggerCombatAnimation(src, typeStr, true);
					}
				}
				// Weapon impact sound
				UISoundPlayer.Instance?.PlayWeaponImpact(text);
				break;
			}
			case "MELEE_MISS":
			{
				string src = evt.GetProperty("source").GetString();
				string tgt = evt.GetProperty("target").GetString();
				string srcId = evt.TryGetProperty("sourceId", out var sIdProp) && sIdProp.ValueKind != JsonValueKind.Null ? sIdProp.GetString() : "";
				string tgtId = evt.TryGetProperty("targetId", out var tIdProp) && tIdProp.ValueKind != JsonValueKind.Null ? tIdProp.GetString() : "";

				string text = "slash";
				if (evt.TryGetProperty("text", out var tProp) && tProp.ValueKind != JsonValueKind.Null)
				{
					string val = tProp.GetString();
					if (!string.IsNullOrEmpty(val)) text = val;
				}

				string typeStr = "slash";
				if (evt.TryGetProperty("type", out var typeProp) && typeProp.ValueKind != JsonValueKind.Null)
				{
					string val = typeProp.GetString();
					if (!string.IsNullOrEmpty(val)) typeStr = val;
				}

				if (src == "You")
					Log("MISS", $"You try to hit {tgt}, but miss!");
				else
					Log("MISS", $"{src} tries to hit YOU, but misses!");
					
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null) 
				{
					if (!string.IsNullOrEmpty(tgtId)) wm.TriggerEntityActionById(tgtId, "miss");
					else wm.TriggerEntityAction(tgt, "miss");
					
					// Player attack animations are client-timer-driven; only trigger NPC swings here
					if (src != "You")
					{
						if (!string.IsNullOrEmpty(srcId)) wm.TriggerCombatAnimationById(srcId, typeStr, false);
						else wm.TriggerCombatAnimation(src, typeStr, false);
					}
				}
				break;
			}
			case "SPELL_DAMAGE":
			{
				string src = evt.GetProperty("source").GetString();
				string tgt = evt.GetProperty("target").GetString();
				string spell = evt.GetProperty("spell").GetString();
				int dmg = evt.GetProperty("damage").GetInt32();
				Log("SPELL", $"{src} hit {tgt} for {dmg} points of non-melee damage. ({spell})");
				
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null) 
				{
					string hitAction = dmg >= 25 ? "hit_heavy" : "hit";
					wm.TriggerEntityAction(tgt, hitAction);
				}
				// Spell impact — use a generic blunt sound for magical force
				UISoundPlayer.Instance?.PlayWeaponImpact("crush");
				break;
			}
			case "SPELL_HEAL":
			{
				string src = evt.GetProperty("source").GetString();
				string tgt = evt.GetProperty("target").GetString();
				string spell = evt.GetProperty("spell").GetString();
				int amt = evt.GetProperty("amount").GetInt32();
				Log("HEAL", $"{spell} heals {tgt} for {amt} hit points.");
				break;
			}
			case "DOT_TICK":
			{
				string tgt = evt.GetProperty("target").GetString();
				string spell = evt.GetProperty("spell").GetString();
				int dmg = evt.GetProperty("damage").GetInt32();
				Log("DOT", $"{tgt} has taken {dmg} damage from {spell}.");
				break;
			}
			case "DEATH":
			{
				string who = evt.GetProperty("who").GetString();
				Log("DEATH", $"{who} has been slain!");
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null) wm.TriggerEntityAction(who, "die");
				break;
			}
			case "XP_GAIN":
			{
				int amt = evt.GetProperty("amount").GetInt32();
				Log("XP", $"You gained {amt} experience!");
				UISoundPlayer.Instance?.PlayXpGain();
				break;
			}
			case "LOOT":
			{
				if (evt.TryGetProperty("text", out var textProp))
				{
					Log("LOOT", textProp.GetString());
				}
				else
				{
					string item = evt.GetProperty("item").GetString();
					string from = evt.TryGetProperty("source", out var src) ? src.GetString() : "a corpse";
					Log("LOOT", $"You loot {item} from {from}.");
				}
				UISoundPlayer.Instance?.PlayLoot();
				break;
			}
			case "LEVEL_UP":
			{
				int lvl = evt.GetProperty("level").GetInt32();
				Log("DING", $"You have reached level {lvl}! Congratulations!");
				UISoundPlayer.Instance?.PlayLevelUp();
				break;
			}
			case "FIZZLE":
			{
				string spell = evt.GetProperty("spell").GetString();
				Log("FIZZLE", $"Your {spell} spell fizzles!");
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null) wm.TriggerEntityAction("You", "fizzle");
				UISoundPlayer.Instance?.PlayFizzle();
				break;
			}
			case "RESIST":
			{
				string tgt = evt.GetProperty("target").GetString();
				string spell = evt.GetProperty("spell").GetString();
				Log("RESIST", $"{tgt} resisted your {spell}!");
				break;
			}
			case "MESSAGE":
			{
				string text = evt.GetProperty("text").GetString();
				Log("SYSTEM", text);
				break;
			}
			case "ERROR":
			{
				string text = evt.GetProperty("text").GetString();
				Log("ERROR", text);
				break;
			}
			case "NPC_SAY":
			{
				string npcName = evt.GetProperty("npcName").GetString();
				string text = evt.GetProperty("text").GetString();
				// Meta-clickable keywords might be in the text as [keyword]
				// We format them on the client for the RichTextLabel
				LogNPC(npcName, text);
				break;
			}
			case "CONSIDER":
			{
				string text = evt.GetProperty("text").GetString();
				string color = evt.TryGetProperty("color", out var colProp) ? colProp.GetString() : "white";
				// Map con colors to log categories for color-coding
				string logCat = color switch {
					"green" => "CON_GREEN",
					"blue" => "CON_BLUE",
					"yellow" => "CON_YELLOW",
					"red" => "CON_RED",
					_ => "SYSTEM"
				};
				Log(logCat, text);
				break;
			}
			case "MINING_HIT":
			{
				string text = evt.GetProperty("text").GetString();
				Log("HIT", $"[color=orange]{text}[/color]");
				break;
			}
			case "MINING_MISS":
			{
				string text = evt.GetProperty("text").GetString();
				Log("MISS", $"[color=yellow]{text}[/color]");
				break;
			}
			case "NODE_SHATTER":
			{
				string text = evt.GetProperty("text").GetString();
				string nodeId = evt.TryGetProperty("nodeId", out var nidProp) ? nidProp.GetString() : null;
				Log("LOOT", $"[color=gold]{text}[/color]");
				// Remove the node from the world
				if (!string.IsNullOrEmpty(nodeId))
				{
					var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
					if (wm != null) wm.RemoveEntity(nodeId);
				}
				break;
			}
			default:
				GD.Print($"[UI] Unhandled combat event: {eventType}");
				break;
		}
	}

	private void OnNpcSayReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			string npcName = root.GetProperty("npcName").GetString();
			string text = root.GetProperty("text").GetString();
			LogNPC(npcName, text);
		}
		catch (Exception ex) { GD.PrintErr($"[UI] NPC_SAY Error: {ex.Message}"); }
	}

	private void OnMetaClicked(Variant meta)
	{
		string keyword = meta.ToString();
		if (keyword.StartsWith("{") && keyword.EndsWith("}"))
		{
			try
			{
				using var doc = JsonDocument.Parse(keyword);
				var root = doc.RootElement;
				string type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "";
				int id = root.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
				
				if (type == "item" && id > 0)
				{
					_client.SendRaw($"{{\"type\": \"ITEM_INSPECT\", \"itemId\": {id}}}");
				}
				else if (type == "spell" && id > 0)
				{
					_client.SendRaw($"{{\"type\": \"SPELL_INSPECT\", \"spellId\": {id}}}");
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[UI] Failed to parse meta JSON: {ex.Message}");
			}
			return;
		}

		GD.Print($"[UI] Meta clicked: {keyword}");
		_client.SendRaw($"{{\"type\": \"SAY\", \"text\": \"{keyword}\"}}");
	}

	// ── Chat System ─────────────────────────────────────────────────────

	public void OnChatSubmitted(string text)
	{
		_chatInput.Text = "";
		CallDeferred(MethodName.ReleaseChatFocus);
		
		if (string.IsNullOrWhiteSpace(text)) return;

		string trimmed = text.Trim();

		// If it doesn't start with /, use the selected channel
		if (!trimmed.StartsWith("/"))
		{
			string channelType = "SAY";
			if (_chatChannelSelect != null)
			{
				int selected = _chatChannelSelect.Selected;
				if (selected == 1) channelType = "GROUP";
				else if (selected == 2) channelType = "GUILD";
				else if (selected == 3) channelType = "SHOUT";
				else if (selected == 4) channelType = "OOC";
			}
			
			_client.SendRaw($"{{\"type\": \"{channelType}\", \"text\": \"{EscapeJson(trimmed)}\"}}");
			return;
		}

		// Parse slash command
		string[] parts = trimmed.Split(' ', 2, System.StringSplitOptions.RemoveEmptyEntries);
		string cmd = parts[0].ToLower();
		string body = parts.Length > 1 ? parts[1] : "";

		switch (cmd)
		{
			case "/say":
			case "/s":
				_client.SendRaw($"{{\"type\": \"SAY\", \"text\": \"{EscapeJson(body)}\"}}");
				break;

			case "/shout":
			case "/sho":
				if (string.IsNullOrEmpty(body)) { Log("SYSTEM", "Usage: /shout <message>"); break; }
				_client.SendRaw($"{{\"type\": \"SHOUT\", \"text\": \"{EscapeJson(body)}\"}}");
				break;

			case "/ooc":
				if (string.IsNullOrEmpty(body)) { Log("SYSTEM", "Usage: /ooc <message>"); break; }
				_client.SendRaw($"{{\"type\": \"OOC\", \"text\": \"{EscapeJson(body)}\"}}");
				break;

			case "/yell":
				_client.SendRaw($"{{\"type\": \"YELL\", \"text\": \"{EscapeJson(body)}\"}}");
				break;

			case "/whisper":
			case "/w":
			case "/tell":
			case "/t":
			{
				string[] whisperParts = body.Split(' ', 2, System.StringSplitOptions.RemoveEmptyEntries);
				if (whisperParts.Length < 2) { Log("SYSTEM", "Usage: /whisper <name> <message>"); break; }
				_client.SendRaw($"{{\"type\": \"WHISPER\", \"target\": \"{EscapeJson(whisperParts[0])}\", \"text\": \"{EscapeJson(whisperParts[1])}\"}}");
				break;
			}
			
			case "/melody":
			{
				if (string.IsNullOrEmpty(body)) { 
					if (_melodyWindow != null) _melodyWindow.Visible = !_melodyWindow.Visible;
					break; 
				}
				_client.SendRaw($"{{\"type\": \"MELODY\", \"slots\": \"{EscapeJson(body)}\"}}");
				break;
			}
			
			case "/stop":
			case "/stopsong":
			case "/stopcast":
			{
				_client.SendRaw($"{{\"type\": \"STOP_MELODY\"}}");
				break;
			}

			case "/reply":
			case "/r":
			{
				if (string.IsNullOrEmpty(_lastWhisperSender))
				{
					Log("SYSTEM", "No one has whispered you yet.");
					break;
				}
				if (string.IsNullOrEmpty(body)) { Log("SYSTEM", "Usage: /reply <message>"); break; }
				_client.SendRaw($"{{\"type\": \"WHISPER\", \"target\": \"{EscapeJson(_lastWhisperSender)}\", \"text\": \"{EscapeJson(body)}\"}}");
				break;
			}

			case "/group":
			case "/g":
			case "/gsay":
				if (string.IsNullOrEmpty(body)) { Log("SYSTEM", "Usage: /group <message>"); break; }
				_client.SendRaw($"{{\"type\": \"GROUP\", \"text\": \"{EscapeJson(body)}\"}}");
				break;

			case "/guild":
			case "/gu":
				if (string.IsNullOrEmpty(body)) { Log("SYSTEM", "Usage: /guild <message>"); break; }
				_client.SendRaw($"{{\"type\": \"GUILD\", \"text\": \"{EscapeJson(body)}\"}}");
				break;

			case "/raid":
			case "/rs":
				if (string.IsNullOrEmpty(body)) { Log("SYSTEM", "Usage: /raid <message>"); break; }
				_client.SendRaw($"{{\"type\": \"RAID\", \"text\": \"{EscapeJson(body)}\"}}");
				break;

			case "/announcement":
			case "/ann":
				if (string.IsNullOrEmpty(body)) { Log("SYSTEM", "Usage: /announcement <message>"); break; }
				_client.SendRaw($"{{\"type\": \"ANNOUNCEMENT\", \"text\": \"{EscapeJson(body)}\"}}");
				break;

			case "/target":
			case "/tar":
			{
				if (string.IsNullOrEmpty(body)) { Log("SYSTEM", "Usage: /target <name>"); break; }
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null)
				{
					var target = wm.TargetEntityByPartialName(body);
					if (target == null)
					{
						Log("SYSTEM", $"No entity found matching '{body}'");
					}
				}
				break;
			}

			case "/hail":
			{
				var wm = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wm != null && wm.CurrentTargetId != null)
				{
					var targetName = wm.GetEntityById(wm.CurrentTargetId)?.EntityName ?? "";
					if (targetName.ToLower().Contains("tuner") || targetName.ToLower().Contains("capsule"))
					{
						var tuner = new LightTunerWindow();
						tuner.Setup(wm, wm.CurrentTargetId);
						AddChild(tuner);
						tuner.Show();
						break;
					}
					_client.SendRaw($"{{\"type\": \"HAIL\", \"targetId\": \"{wm.CurrentTargetId}\"}}");
				}
				else
				{
					_client.SendRaw("{\"type\": \"HAIL\"}");
				}
				break;
			}

			case "/pet":
			{
				// Pet commands: /pet attack, /pet follow, /pet guard, etc.
				string petCmd = body.Trim().ToLower();
				if (string.IsNullOrEmpty(petCmd))
				{
					Log("SYSTEM", "Usage: /pet <attack|follow|guard|sit|backoff|taunt|getlost|health|leader|target|asyouwere>");
					break;
				}
				// Map common aliases
				if (petCmd == "follow me" || petCmd == "guard me") petCmd = "follow";
				else if (petCmd == "guard here") petCmd = "guard";
				else if (petCmd == "sit down") petCmd = "sit";
				else if (petCmd == "back off") petCmd = "backoff";
				else if (petCmd == "get lost") petCmd = "getlost";
				else if (petCmd == "as you were") petCmd = "asyouwere";
				else if (petCmd == "taunt on" || petCmd == "taunt off") petCmd = "taunt";
				_client.SendRaw($"{{\"type\": \"PET_COMMAND\", \"command\": \"{EscapeJson(petCmd)}\"}}");
				break;
			}

			case "/invite":
				if (string.IsNullOrEmpty(body)) { Log("SYSTEM", "Usage: /invite <name>"); break; }
				_client.SendRaw($"{{\"type\": \"GROUP_INVITE\", \"targetName\": \"{EscapeJson(body)}\"}}");
				break;

			case "/disband":
			{
				if (_hasActiveMercenary)
				{
					_client.SendRaw("{\"type\": \"MERCENARY_ACTION\", \"action\": \"suspend_active\", \"index\": 0}");
				}
				else
				{
					_client.SendRaw("{\"type\": \"GROUP_DISBAND\"}");
				}
				break;
			}

			case "/grouproles":
				_client.SendRaw($"{{\"type\": \"GROUPROLES\", \"text\": \"{EscapeJson(body)}\"}}");
				break;

			case "/assist":
				if (body.ToLower() == "group")
				{
					_client.SendRaw("{\"type\": \"ASSIST_GROUP\"}");
				}
				else
				{
					_client.SendRaw($"{{\"type\": \"ASSIST\", \"target\": \"{EscapeJson(body)}\"}}");
				}
				break;

			case "/cast":
				if (string.IsNullOrEmpty(body)) { Log("SYSTEM", "Usage: /cast <slot 1-8>"); break; }
				if (int.TryParse(body, out int slot))
				{
					_client.SendRaw($"{{\"type\": \"CAST_SPELL\", \"slot\": {slot - 1}}}");
				}
				break;
			case "/resetcastbar":
			{
				if (_castBarPanel != null)
				{
					_castBarPanel.AnchorLeft = 0.5f;
					_castBarPanel.AnchorRight = 0.5f;
					_castBarPanel.AnchorTop = 0.55f;
					_castBarPanel.AnchorBottom = 0.55f;
					_castBarPanel.OffsetLeft = -100;
					_castBarPanel.OffsetRight = 100;
					_castBarPanel.OffsetTop = -15;
					_castBarPanel.OffsetBottom = 15;
					_castBarPanel.Show();
					Log("SYSTEM", "Casting bar has been reset to center screen.");
				}
				break;
			}
			case "/con":
			case "/consider":
				_client.SendRaw("{\"type\": \"CONSIDER\"}");
				break;
			case "/sit":
				_client.SendRaw("{\"type\": \"SIT\"}");
				break;
			case "/stand":
				_client.SendRaw("{\"type\": \"STAND\"}");
				break;
			case "/camp":
				_client.SendRaw("{\"type\": \"CAMP\"}");
				break;
			case "/who":
				_client.SendRaw("{\"type\": \"WHO\"}");
				break;
			case "/loc":
				var wmLoc = GetNodeOrNull<WorldManager>("ViewPortPanel/SubViewportContainer/SubViewport/World3D");
				if (wmLoc != null)
				{
					Log("SYSTEM", $"Your location is {wmLoc.PlayerPosition.X:F2}, {wmLoc.PlayerPosition.Z:F2}, {wmLoc.PlayerPosition.Y:F2}");
				}
				break;
			case "/time":
				_client.SendRaw("{\"type\": \"TIME\"}");
				break;
			case "/emote":
			case "/em":
			case "/me":
				if (string.IsNullOrEmpty(body)) { Log("SYSTEM", "Usage: /emote <action>"); break; }
				_client.SendRaw($"{{\"type\": \"EMOTE\", \"emote\": \"{EscapeJson(body)}\"}}");
				break;
			case "/roll":
			{
				int maxRoll = 20;
				if (!string.IsNullOrWhiteSpace(body) && int.TryParse(body.Trim(), out int parsedRoll))
					maxRoll = parsedRoll;
				_client.SendRaw($"{{\"type\": \"ROLL\", \"max\": {maxRoll}}}");
				break;
			}
			case "/random":
			{
				int maxRoll = 100;
				if (!string.IsNullOrWhiteSpace(body) && int.TryParse(body.Trim(), out int parsedRoll))
					maxRoll = parsedRoll;
				_client.SendRaw($"{{\"type\": \"RANDOM\", \"max\": {maxRoll}}}");
				break;
			}

			case "/corpse":
			case "/dragcorpse":
				_client.SendRaw("{\"type\": \"CORPSE_DRAG\"}");
				break;
			case "/consent":
				if (string.IsNullOrEmpty(body))
				{
					Log("SYSTEM", "Usage: /consent <name>, /consent group, /consent list");
					break;
				}
				_client.SendRaw($"{{\"type\": \"CORPSE_CONSENT\", \"targetName\": \"{EscapeJson(body)}\"}}");
				break;
			case "/deny":
				if (string.IsNullOrEmpty(body))
				{
					Log("SYSTEM", "Usage: /deny <name>, /deny group, /deny all");
					break;
				}
				_client.SendRaw($"{{\"type\": \"CORPSE_DENY\", \"targetName\": \"{EscapeJson(body)}\"}}");
				break;

			default:
				_client.SendRaw($"{{\"type\": \"SERVER_COMMAND\", \"command\": \"{EscapeJson(cmd)}\", \"args\": \"{EscapeJson(body)}\"}}");
				break;
		}
	}

	private void OnChatReceived(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			string channel = root.TryGetProperty("channel", out var ch) ? ch.GetString() : "say";
			string sender = root.TryGetProperty("sender", out var sn) ? sn.GetString() : "";
			string text = root.TryGetProperty("text", out var tx) ? tx.GetString() : "";
			string direction = root.TryGetProperty("direction", out var dir) ? dir.GetString() : "";

			// Track last whisper sender for /reply
			if (channel == "whisper" && direction == "from")
				_lastWhisperSender = sender;

			// Format and colorize based on channel
			string color;
			string formatted;
			switch (channel)
			{
				case "say":
					color = "#e0e0e0";
					formatted = $"{sender} says, '{text}'";
					break;
				case "shout":
					color = "#ff4444";
					formatted = $"{sender} shouts, '{text}'";
					break;
				case "ooc":
					color = "#00cc00";
					formatted = $"{sender} says out of character, '{text}'";
					break;
				case "yell":
					color = "#ffaa00";
					formatted = $"{sender} yells, '{text}'";
					break;
				case "whisper":
					color = "#cc44cc";
					if (direction == "to")
						formatted = $"You whisper to {sender}, '{text}'";
					else
						formatted = $"{sender} whispers, '{text}'";
					break;
				case "group":
					color = "#44aaff";
					formatted = $"{sender} tells the group, '{text}'";
					break;
				case "guild":
					color = "#44ff44";
					formatted = $"{sender} tells the guild, '{text}'";
					break;
				case "raid":
					color = "#ffcc44";
					formatted = $"{sender} tells the raid, '{text}'";
					break;
				case "announcement":
					color = "#ffdd00";
					formatted = $"[ANNOUNCEMENT] {sender}: {text}";
					break;
				case "system":
					color = "#cccccc";
					formatted = text;
					break;
				default:
					color = "#cccccc";
					formatted = $"[{channel}] {sender}: {text}";
					break;
			}

			_combatLog.AppendText($"[color={color}]{formatted}[/color]\n");
			_logLineCount++;
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Chat Error: {ex.Message}"); }
	}

	private static string EscapeJson(string s)
	{
		return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}

	private void Log(string type, string message)
	{
		// Trim old lines if we're getting too long
		if (_logLineCount > MaxLogLines)
		{
			_combatLog.Clear();
			_logLineCount = 0;
			_combatLog.AppendText("[color=gray]--- log trimmed ---[/color]\n");
		}

		string color = type switch
		{
			"MISS"      => "#888888",   // Gray
			"HIT"       => "#55cc55",   // Green â€” your hits
			"HIT_TAKEN" => "#cc5555",   // Red â€” damage taken
			"SPELL"     => "#55cccc",   // Cyan
			"HEAL"      => "#88ee88",   // Light green
			"DOT"       => "#cccc55",   // Yellow
			"DEATH"     => "#ff4444",   // Bright red
			"XP"        => "#ddaa44",   // Gold
			"LOOT"      => "#ddaa44",   // Gold
			"DING"      => "#ffdd00",   // Bright gold
			"FIZZLE"    => "#aa55aa",   // Purple
			"RESIST"    => "#aa55aa",   // Purple
			"SYSTEM"    => "#dd8833",   // Orange
			"ERROR"     => "#ff4444",   // Red for system errors
			"CON_GREEN" => "#00cc00",   // EQ green con
			"CON_BLUE"  => "#4488ff",   // EQ blue con
			"CON_YELLOW"=> "#ffff00",   // EQ yellow con
			"CON_RED"   => "#ff4444",   // EQ red con
			"EMOTE"     => "#cc88cc",   // Light purple for emotes
			_           => "#cccccc",   // Default light gray
		};

		_combatLog.AppendText($"[color={color}]{message}[/color]\n");
		_logLineCount++;
	}

	private void LogNPC(string npcName, string text)
	{
		if (_logLineCount > MaxLogLines)
		{
			_combatLog.Clear();
			_logLineCount = 0;
			_combatLog.AppendText("[color=gray]--- log trimmed ---[/color]\n");
		}

		// Replace [keyword] with [url=keyword][color=blue][b]keyword[/b][/color][/url]
		string formattedText = text;
		var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\[([^\]]+)\]");
		foreach (System.Text.RegularExpressions.Match match in matches)
		{
			string keyword = match.Groups[1].Value;
			string replacement = $"[url={keyword}][color=blue][b]{keyword}[/b][/color][/url]";
			formattedText = formattedText.Replace(match.Value, replacement);
		}

		_combatLog.AppendText($"[color=lightblue]{npcName} says, '{formattedText}'[/color]\n");
		_logLineCount++;
	}

	public void SetupChatTabs()
	{
		// 1. Setup Chat OptionButton next to ChatInput
		if (_chatInput != null)
		{
			var parent = _chatInput.GetParent();
			var hbox = new HBoxContainer();
			hbox.Name = "InputHBox";
			
			// Move ChatInput into the new HBox
			parent.AddChild(hbox);
			parent.MoveChild(hbox, _chatInput.GetIndex());
			parent.RemoveChild(_chatInput);
			
			_chatChannelSelect = new OptionButton();
			_chatChannelSelect.AddItem("Say", 0);
			_chatChannelSelect.AddItem("Group", 1);
			_chatChannelSelect.AddItem("Guild", 2);
			_chatChannelSelect.AddItem("Shout", 3);
			_chatChannelSelect.AddItem("OOC", 4);
			_chatChannelSelect.CustomMinimumSize = new Vector2(80, 0);
			
			hbox.AddChild(_chatChannelSelect);
			hbox.AddChild(_chatInput);
			_chatInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		}

		// 2. Setup RichTextLabels for Party and Guild
		if (_combatLog != null)
		{
			var parent = _combatLog.GetParent();
			
			_partyLog = (RichTextLabel)_combatLog.Duplicate();
			_partyLog.Name = "PartyLog";
			_partyLog.Text = "";
			parent.AddChild(_partyLog);
			parent.MoveChild(_partyLog, _combatLog.GetIndex() + 1);
			_partyLog.Hide();

			_guildLog = (RichTextLabel)_combatLog.Duplicate();
			_guildLog.Name = "GuildLog";
			_guildLog.Text = "";
			parent.AddChild(_guildLog);
			parent.MoveChild(_guildLog, _combatLog.GetIndex() + 2);
			_guildLog.Hide();
		}

		// 3. Wire the Buttons
		var mainBtn = GetNodeOrNull<Button>("%CombatLog/../../HBoxContainer/MainBtn");
		var partyBtn = GetNodeOrNull<Button>("%CombatLog/../../HBoxContainer/PartyBtn");
		var guildBtn = GetNodeOrNull<Button>("%CombatLog/../../HBoxContainer/GuildBtn");

		if (mainBtn != null) mainBtn.Pressed += () => SwitchChatTab("Main");
		if (partyBtn != null) partyBtn.Pressed += () => SwitchChatTab("Party");
		if (guildBtn != null) guildBtn.Pressed += () => SwitchChatTab("Guild");
	}

	private void SwitchChatTab(string tabName)
	{
		_currentChatTab = tabName;
		_combatLog?.Hide();
		_partyLog?.Hide();
		_guildLog?.Hide();

		if (tabName == "Main") _combatLog?.Show();
		else if (tabName == "Party")
		{
			_partyLog?.Show();
			_chatChannelSelect?.Select(1); // Auto switch dropdown to Group
		}
		else if (tabName == "Guild")
		{
			_guildLog?.Show();
			_chatChannelSelect?.Select(2); // Auto switch dropdown to Guild
		}
	}

	// â”€â”€â”€ Status Bars â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
}
