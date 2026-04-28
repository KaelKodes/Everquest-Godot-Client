using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Social/macro library and command executor.
/// Manages 120 socials (10 pages × 12 per page) and executes command sequences
/// with /pause support, %-code substitution, and EQ-authentic comma syntax.
/// </summary>
public partial class SocialManager : Node
{
	// ── Data Model ──────────────────────────────────────────────────

	public class Social
	{
		public string Name = "";
		public int Color = 0;           // 0-19, EQ's color palette
		public string[] Lines = new string[5] { "", "", "", "", "" };

		public bool IsEmpty => string.IsNullOrEmpty(Name) &&
			Lines.All(l => string.IsNullOrEmpty(l));
	}

	// 120 socials total (10 pages × 12 per page)
	public const int PAGES = 10;
	public const int PER_PAGE = 12;
	public const int TOTAL = PAGES * PER_PAGE;

	public Social[] Socials { get; private set; } = new Social[TOTAL];

	// EQ color palette (the 20 colors from the "A" buttons in the Edit Social dialog)
	public static readonly Color[] EQColors = {
		new Color(1.0f, 1.0f, 1.0f),    // 0  White
		new Color(0.8f, 0.2f, 0.2f),    // 1  Red
		new Color(0.2f, 0.8f, 0.2f),    // 2  Green
		new Color(0.3f, 0.5f, 1.0f),    // 3  Blue
		new Color(1.0f, 1.0f, 0.2f),    // 4  Yellow
		new Color(0.8f, 0.5f, 0.2f),    // 5  Orange
		new Color(0.6f, 0.2f, 0.8f),    // 6  Purple
		new Color(0.2f, 0.8f, 0.8f),    // 7  Cyan
		new Color(1.0f, 0.4f, 0.7f),    // 8  Pink
		new Color(0.6f, 0.6f, 0.6f),    // 9  Gray
		new Color(0.5f, 0.3f, 0.1f),    // 10 Brown
		new Color(0.8f, 0.8f, 0.5f),    // 11 Tan
		new Color(0.4f, 0.7f, 0.4f),    // 12 Olive
		new Color(0.7f, 0.7f, 1.0f),    // 13 Light blue
		new Color(1.0f, 0.6f, 0.6f),    // 14 Light red
		new Color(0.6f, 1.0f, 0.6f),    // 15 Light green
		new Color(0.9f, 0.9f, 0.6f),    // 16 Light yellow
		new Color(0.7f, 0.5f, 0.9f),    // 17 Lavender
		new Color(0.3f, 0.3f, 0.3f),    // 18 Dark gray
		new Color(0.0f, 0.0f, 0.0f),    // 19 Black
	};

	// ── Dependencies ────────────────────────────────────────────────

	private GameClient _client;
	private bool _executing = false;

	// Callbacks for getting game state needed during execution
	public Func<int, int> GetSpellIdForSlot;          // slot index → spellId
	public Func<string> GetCurrentTargetName;          // → target name or ""
	public Func<string> GetCurrentTargetRace;          // → race name or ""
	public Func<string> GetCurrentTargetGenderSubjective;  // → "He"/"She"/"It"
	public Func<string> GetCurrentTargetGenderObjective;   // → "Him"/"Her"/"It"
	public Func<string> GetCurrentTargetGenderPossessive;  // → "His"/"Her"/"Its"
	public Func<string> GetPetName;                    // → pet name or ""

	public override void _Ready()
	{
		// Initialize all socials as empty
		for (int i = 0; i < TOTAL; i++)
			Socials[i] = new Social();

		// Pre-populate page 1 with EQ defaults
		SetDefaultSocials();
	}

	public void Init(GameClient client)
	{
		_client = client;
	}

	// ── Default Socials (Page 1) ────────────────────────────────────

	private void SetDefaultSocials()
	{
		// Page 1 defaults (matching classic EQ's first social page)
		SetSocial(0, "Hail", 0, "/hail");
		SetSocial(1, "Consider", 7, "/consider");
		SetSocial(2, "Wave", 0, "/wave");
		SetSocial(3, "SnsHead", 0, "/sensehead");
		SetSocial(4, "Cheer", 4, "/cheer");
		SetSocial(5, "Disappnt", 9, "/disappointed");
		SetSocial(6, "Rude", 1, "/rude");
		// Slots 7-11 left blank for player customization
	}

	private void SetSocial(int index, string name, int color, params string[] lines)
	{
		if (index < 0 || index >= TOTAL) return;
		Socials[index].Name = name;
		Socials[index].Color = color;
		for (int i = 0; i < 5 && i < lines.Length; i++)
			Socials[index].Lines[i] = lines[i];
	}

	// ── Social Execution ────────────────────────────────────────────

	/// <summary>
	/// Execute a social's command sequence. Lines are processed sequentially
	/// with /pause delays honored between them.
	/// </summary>
	public async void ExecuteSocial(int socialIndex)
	{
		if (socialIndex < 0 || socialIndex >= TOTAL) return;
		if (_client == null) return;
		if (_executing) return; // Don't allow overlapping executions

		var social = Socials[socialIndex];
		if (social.IsEmpty) return;

		_executing = true;

		try
		{
			for (int i = 0; i < 5; i++)
			{
				string line = social.Lines[i];
				if (string.IsNullOrWhiteSpace(line)) continue;

				// Substitute %-codes
				line = SubstitutePercCodes(line);

				// Check for EQ's comma syntax: /pause N,/command
				// The pause is written first but executed AFTER the command
				if (line.StartsWith("/pause ", StringComparison.OrdinalIgnoreCase) &&
					line.Contains(','))
				{
					int commaIdx = line.IndexOf(',');
					string pausePart = line[..commaIdx].Trim();
					string commandPart = line[(commaIdx + 1)..].Trim();

					// Execute the command FIRST
					ExecuteCommand(commandPart);

					// Then apply the pause
					int pauseTenths = ParsePauseValue(pausePart);
					if (pauseTenths > 0)
					{
						float seconds = pauseTenths / 10.0f;
						await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
					}
				}
				else if (line.StartsWith("/pause", StringComparison.OrdinalIgnoreCase))
				{
					// Pure pause line
					int pauseTenths = ParsePauseValue(line);
					if (pauseTenths > 0)
					{
						float seconds = pauseTenths / 10.0f;
						await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
					}
				}
				else
				{
					// Regular command
					ExecuteCommand(line);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[SOCIAL] Execution error: {ex.Message}");
		}
		finally
		{
			_executing = false;
		}
	}

	// ── Command Parsing ─────────────────────────────────────────────

	private void ExecuteCommand(string line)
	{
		if (string.IsNullOrWhiteSpace(line)) return;

		line = line.Trim();

		// Normalize: ensure starts with /
		if (!line.StartsWith("/")) return;

		string[] parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
		string command = parts[0].ToLower();
		string args = parts.Length > 1 ? parts[1] : "";

		switch (command)
		{
			case "/cast":
				HandleCast(args);
				break;
			case "/sit":
				if (args.ToLower() == "off")
					_client.SendRaw("{\"type\": \"STAND\"}");
				else
					_client.SendRaw("{\"type\": \"SIT\"}");
				break;
			case "/stand":
				_client.SendRaw("{\"type\": \"STAND\"}");
				break;
			case "/attack":
				_client.SendRaw("{\"type\": \"START_COMBAT\"}");
				break;
			case "/stopattack":
				_client.SendRaw("{\"type\": \"STOP_COMBAT\"}");
				break;
			case "/say":
				if (!string.IsNullOrEmpty(args))
					_client.SendRaw($"{{\"type\": \"SAY\", \"text\": \"{EscapeJson(args)}\"}}");
				break;
			case "/hail":
				_client.SendRaw("{\"type\": \"HAIL\"}");
				break;
			case "/consider":
				_client.SendRaw("{\"type\": \"CONSIDER\"}");
				break;
			case "/camp":
				_client.SendRaw("{\"type\": \"CAMP\"}");
				break;

			// Ability commands
			case "/kick":
			case "/bash":
			case "/taunt":
			case "/backstab":
			case "/disarm":
			case "/forage":
			case "/hide":
			case "/sneak":
			case "/track":
			case "/mend":
			case "/picklock":
			case "/sensehead":
				string ability = command[1..]; // strip the /
				_client.SendRaw($"{{\"type\": \"ABILITY\", \"ability\": \"{ability}\"}}");
				break;

			// Emote commands — send emote with animation to server
			case "/wave":
				_client.SendRaw("{\"type\": \"EMOTE\", \"emote\": \"wave\", \"anim\": \"s03\"}");
				break;
			case "/cheer":
				_client.SendRaw("{\"type\": \"EMOTE\", \"emote\": \"cheer\", \"anim\": \"s01\"}");
				break;
			case "/disappointed":
				_client.SendRaw("{\"type\": \"EMOTE\", \"emote\": \"disappointed\", \"anim\": \"s02\"}");
				break;
			case "/rude":
				_client.SendRaw("{\"type\": \"EMOTE\", \"emote\": \"rude\", \"anim\": \"s04\"}");
				break;
			case "/dance":
			case "/point":
			case "/bow":
			case "/laugh":
			case "/cry":
			case "/nod":
			case "/shrug":
			case "/salute":
				// Generic emotes — no specific animation yet
				_client.SendRaw($"{{\"type\": \"EMOTE\", \"emote\": \"{command[1..]}\"}}");
				break;

			default:
				GD.Print($"[SOCIAL] Unknown command: {command} {args}");
				break;
		}
	}

	private void HandleCast(string args)
	{
		if (string.IsNullOrWhiteSpace(args)) return;

		// /cast # — # is 1-9 or 0 (for 10th slot)
		// Map to spell slot index: 1→0, 2→1, ..., 9→8, 0→9
		if (int.TryParse(args.Trim(), out int castNum))
		{
			int slotIndex;
			if (castNum == 0)
				slotIndex = 9; // 0 = 10th slot (but we only have 8 for now)
			else
				slotIndex = castNum - 1;

			// Clamp to our 8-slot spell bar
			if (slotIndex < 0 || slotIndex >= 8) return;

			int spellId = GetSpellIdForSlot?.Invoke(slotIndex) ?? -1;
			if (spellId > 0)
			{
				_client.SendRaw($"{{\"type\": \"CAST_SPELL\", \"spellId\": {spellId}, \"slot\": {slotIndex}}}");
			}
		}
	}

	private int ParsePauseValue(string pauseLine)
	{
		// /pause 100 → 100 (tenths of seconds)
		string[] parts = pauseLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length >= 2 && int.TryParse(parts[1], out int val))
			return Math.Clamp(val, 1, 600);
		return 0;
	}

	// ── %-Code Substitution ─────────────────────────────────────────

	private string SubstitutePercCodes(string text)
	{
		if (!text.Contains('%')) return text;

		text = text.Replace("%T", GetCurrentTargetName?.Invoke() ?? "target");
		text = text.Replace("%M", GetPetName?.Invoke() ?? "pet");
		text = text.Replace("%S", GetCurrentTargetGenderSubjective?.Invoke() ?? "It");
		text = text.Replace("%O", GetCurrentTargetGenderObjective?.Invoke() ?? "It");
		text = text.Replace("%P", GetCurrentTargetGenderPossessive?.Invoke() ?? "Its");
		text = text.Replace("%R", GetCurrentTargetRace?.Invoke() ?? "Unknown");

		return text;
	}

	// ── Serialization ───────────────────────────────────────────────

	/// <summary>
	/// Export all socials to a Godot Dictionary for JSON serialization.
	/// </summary>
	public Godot.Collections.Array<Godot.Collections.Dictionary> ExportSocials()
	{
		var arr = new Godot.Collections.Array<Godot.Collections.Dictionary>();
		for (int i = 0; i < TOTAL; i++)
		{
			var s = Socials[i];
			var dict = new Godot.Collections.Dictionary
			{
				["name"] = s.Name,
				["color"] = s.Color,
			};
			for (int l = 0; l < 5; l++)
				dict[$"line{l}"] = s.Lines[l];
			arr.Add(dict);
		}
		return arr;
	}

	/// <summary>
	/// Import socials from serialized data.
	/// </summary>
	public void ImportSocials(Godot.Collections.Array<Godot.Collections.Dictionary> data)
	{
		for (int i = 0; i < TOTAL && i < data.Count; i++)
		{
			var dict = data[i];
			Socials[i].Name = dict.ContainsKey("name") ? dict["name"].AsString() : "";
			Socials[i].Color = dict.ContainsKey("color") ? dict["color"].AsInt32() : 0;
			for (int l = 0; l < 5; l++)
			{
				string key = $"line{l}";
				Socials[i].Lines[l] = dict.ContainsKey(key) ? dict[key].AsString() : "";
			}
		}
	}

	// ── Utilities ───────────────────────────────────────────────────

	private static string EscapeJson(string s)
	{
		return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}
}
