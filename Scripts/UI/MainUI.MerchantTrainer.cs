using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class MainUI
{
	private void OnMerchantOpened(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			string npcName = root.GetProperty("npcName").GetString();
			string npcId = root.GetProperty("npcId").GetString();
			var items = root.GetProperty("items");

			// Track active merchant for sell transactions
			_activeMerchantId = npcId;

			// Read player class/level from payload
			_merchantPlayerClassBitmask = root.TryGetProperty("playerClassBitmask", out var pcb) ? pcb.GetInt32() : 65535;
			_merchantPlayerLevel = root.TryGetProperty("playerLevel", out var pl) ? pl.GetInt32() : 60;

			// Fix title with proper em-dash
			_merchantTitle.Text = $"{npcName} \u2014 Your coin: {FormatCurrency(_copper)}";

			// Parse all items into struct list for sorting/filtering
			_merchantItems.Clear();
			foreach (var item in items.EnumerateArray())
			{
				string iName = item.GetProperty("name").GetString();
				string iKey = item.GetProperty("itemKey").ToString().Trim('"');
				int price = item.TryGetProperty("price", out var pv) ? (int)pv.GetDouble() : 0;
				string priceText = item.TryGetProperty("priceText", out var pt) ? pt.GetString() : $"{price}cp";
				int scrollLevel = item.TryGetProperty("scrolllevel", out var sl) ? sl.GetInt32() : 0;
				int itemType = item.TryGetProperty("itemtype", out var it) ? it.GetInt32() : 0;
				int classes = item.TryGetProperty("classes", out var cl) ? cl.GetInt32() : 65535;
				int recLevel = item.TryGetProperty("reclevel", out var rl) ? rl.GetInt32() : 0;

				// Build stats snippet
				var statParts = new List<string>();
				if (item.TryGetProperty("ac", out var ac) && ac.GetInt32() > 0) statParts.Add($"AC:{ac.GetInt32()}");
				if (item.TryGetProperty("damage", out var dmg) && dmg.GetInt32() > 0) statParts.Add($"Dmg:{dmg.GetInt32()}");
				if (item.TryGetProperty("delay", out var dly) && dly.GetInt32() > 0) statParts.Add($"Dly:{dly.GetInt32()}");
				if (item.TryGetProperty("hp", out var hp) && hp.GetInt32() > 0) statParts.Add($"HP:+{hp.GetInt32()}");
				if (item.TryGetProperty("mana", out var mana) && mana.GetInt32() > 0) statParts.Add($"Mana:+{mana.GetInt32()}");
				string statsStr = statParts.Count > 0 ? $" ({string.Join(" ", statParts)})" : "";

				_merchantItems.Add(new MerchantItem
				{
					Name = iName,
					ItemKey = iKey,
					Price = price,
					PriceText = priceText,
					StatsStr = statsStr,
					ScrollLevel = scrollLevel,
					ItemType = itemType,
					Classes = classes,
					RecLevel = recLevel,
					NpcId = npcId,
				});
			}

			// Build sort/filter bar
			BuildMerchantSortBar();

			// Render the list
			_merchantSortMode = "name";
			RebuildMerchantList();

			_merchantWindow.Show();
			Log("SYSTEM", $"[color=cyan]{npcName} opens their shop...[/color]");

			// Refresh inventory to show sell buttons
			if (!string.IsNullOrEmpty(_client.LastInventoryPayload))
				OnInventoryUpdated(_client.LastInventoryPayload);
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Merchant Error: {ex.Message}"); }
	}

	private void BuildMerchantSortBar()
	{
		// Remove old sort bar if it exists
		if (_merchantSortBar != null && IsInstanceValid(_merchantSortBar))
		{
			_merchantSortBar.QueueFree();
			_merchantSortBar = null;
		}

		var vbox = _merchantWindow.GetNode<VBoxContainer>("VBox");
		if (vbox == null) return;

		_merchantSortBar = new HBoxContainer();
		_merchantSortBar.Name = "SortBar";
		_merchantSortBar.CustomMinimumSize = new Vector2(0, 24);
		_merchantSortBar.AddThemeConstantOverride("separation", 4);

		var sortLabel = new Label { Text = "Sort:" };
		sortLabel.AddThemeFontSizeOverride("font_size", 11);
		sortLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		_merchantSortBar.AddChild(sortLabel);

		string[] sortModes = { "Name", "Price", "Level" };
		foreach (var mode in sortModes)
		{
			var btn = new Button { Text = mode };
			btn.CustomMinimumSize = new Vector2(50, 22);
			btn.AddThemeFontSizeOverride("font_size", 11);
			string modeKey = mode.ToLower();
			btn.Pressed += () =>
			{
				_merchantSortMode = modeKey;
				RebuildMerchantList();
			};
			_merchantSortBar.AddChild(btn);
		}

		// Spacer
		var spacer = new Control();
		spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_merchantSortBar.AddChild(spacer);

		// Show Usable checkbox
		var usableCheck = new CheckBox { Text = "Usable" };
		usableCheck.AddThemeFontSizeOverride("font_size", 11);
		usableCheck.AddThemeColorOverride("font_color", new Color(0.8f, 0.9f, 0.6f));
		usableCheck.ButtonPressed = _merchantShowUsable;
		usableCheck.Toggled += (toggled) =>
		{
			_merchantShowUsable = toggled;
			RebuildMerchantList();
		};
		_merchantSortBar.AddChild(usableCheck);

		// Insert sort bar after Title (index 1, before Scroll)
		vbox.AddChild(_merchantSortBar);
		vbox.MoveChild(_merchantSortBar, 1);
	}

	private void RebuildMerchantList()
	{
		// Clear old items
		foreach (Node child in _merchantItemList.GetChildren()) child.QueueFree();

		// Filter
		var filtered = new List<MerchantItem>();
		foreach (var mi in _merchantItems)
		{
			if (_merchantShowUsable)
			{
				// Check class bitmask
				if (mi.Classes != 65535 && (mi.Classes & _merchantPlayerClassBitmask) == 0)
					continue;
				// Check level for spells or required level
				int reqLevel = mi.ScrollLevel > 0 ? mi.ScrollLevel : mi.RecLevel;
				if (reqLevel > _merchantPlayerLevel)
					continue;
			}
			filtered.Add(mi);
		}

		// Sort
		switch (_merchantSortMode)
		{
			case "price":
				filtered.Sort((a, b) => a.Price.CompareTo(b.Price));
				break;
			case "level":
				filtered.Sort((a, b) =>
				{
					int la = a.ScrollLevel > 0 ? a.ScrollLevel : a.RecLevel;
					int lb = b.ScrollLevel > 0 ? b.ScrollLevel : b.RecLevel;
					int cmp = la.CompareTo(lb);
					return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
				});
				break;
			default: // name
				filtered.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
				break;
		}

		// Render
		foreach (var mi in filtered)
		{
			var row = new HBoxContainer();
			row.CustomMinimumSize = new Vector2(0, 26);

			var infoVbox = new VBoxContainer();
			infoVbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;

			// Name with level tag for spells
			string displayName = mi.Name;
			if (mi.ScrollLevel > 0)
				displayName = $"{mi.Name} (Lv {mi.ScrollLevel})";
			else if (mi.RecLevel > 0)
				displayName = $"{mi.Name} (Req Lv {mi.RecLevel})";

			var nameLabel = new Label { Text = $"{displayName}{mi.StatsStr}" };
			nameLabel.AddThemeFontSizeOverride("font_size", 13);

			// Color: cyan for spells, gold for normal, grey for unusable
			bool isUsable = true;
			if (mi.Classes != 65535 && (mi.Classes & _merchantPlayerClassBitmask) == 0)
				isUsable = false;
			int effectiveLevel = mi.ScrollLevel > 0 ? mi.ScrollLevel : mi.RecLevel;
			if (effectiveLevel > _merchantPlayerLevel)
				isUsable = false;

			if (!isUsable)
				nameLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
			else if (mi.ItemType == 20 || mi.ScrollLevel > 0)
				nameLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.85f, 1.0f));
			else
				nameLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));

			var priceLabel = new Label { Text = mi.PriceText };
			priceLabel.AddThemeFontSizeOverride("font_size", 11);
			priceLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.3f));
			infoVbox.AddChild(nameLabel);
			infoVbox.AddChild(priceLabel);

			var buyBtn = new Button { Text = "Buy" };
			buyBtn.CustomMinimumSize = new Vector2(50, 0);
			string capturedKey = mi.ItemKey;
			string capturedNpcId = mi.NpcId;
			buyBtn.Pressed += () =>
			{
				_client.SendRaw($"{{\"type\": \"BUY\", \"npcId\": \"{capturedNpcId}\", \"itemKey\": \"{capturedKey}\"}}");
			};

			row.AddChild(infoVbox);
			row.AddChild(buyBtn);
			_merchantItemList.AddChild(row);
		}
	}

	// ═══════════════════════════════════════════════════════════════
	//  TRAINER WINDOW
	// ═══════════════════════════════════════════════════════════════
	private Control _trainerWindow;
	private VBoxContainer _trainerSkillList;
	private Label _trainerTitle;
	private Label _trainerPracticesLabel;
	private Label _trainerMoneyPp, _trainerMoneyGp, _trainerMoneySp, _trainerMoneyCp;
	private Button _trainerTrainBtn;
	private string _trainerSelectedSkillKey = null;
	private int _trainerNpcId = 0;
	private string _trainerNpcName = "";

	private void OnTrainerOpened(Variant data)
	{
		if (!IsInstanceValid(this)) return;
		try
		{
			var root = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement;
			_trainerNpcId = root.TryGetProperty("npcId", out var nId) ? nId.GetInt32() : 0;
			_trainerNpcName = root.TryGetProperty("npcName", out var nN) ? nN.GetString() : "Trainer";
			int practices = root.TryGetProperty("practices", out var pr) ? pr.GetInt32() : 0;
			long copper = root.TryGetProperty("copper", out var cu) ? cu.GetInt64() : 0;

			// Build window if not yet created
			if (_trainerWindow == null) BuildTrainerWindow();

			_trainerTitle.Text = _trainerNpcName;
			_trainerPracticesLabel.Text = $"Practices: {practices}";

			// Break copper into denominations
			long pp = copper / 1000;
			long gp = (copper % 1000) / 100;
			long sp = (copper % 100) / 10;
			long cp = copper % 10;
			_trainerMoneyPp.Text = pp.ToString();
			_trainerMoneyGp.Text = gp.ToString();
			_trainerMoneySp.Text = sp.ToString();
			_trainerMoneyCp.Text = cp.ToString();

			// Populate skill rows
			foreach (Node child in _trainerSkillList.GetChildren())
				child.QueueFree();

			_trainerSelectedSkillKey = null;
			_trainerTrainBtn.Disabled = true;

			if (root.TryGetProperty("skills", out var skillsArr))
			{
				foreach (var skill in skillsArr.EnumerateArray())
				{
					string key = skill.GetProperty("key").GetString();
					string name = skill.GetProperty("name").GetString();
					int value = skill.GetProperty("value").GetInt32();
					string rank = skill.GetProperty("rank").GetString();
					bool canTrain = skill.GetProperty("canTrain").GetBoolean();

					string ppStr = "--", gpStr = "--", spStr = "--", cpStr = "--";
					if (canTrain)
					{
						ppStr = skill.GetProperty("costPp").GetInt32().ToString();
						gpStr = skill.GetProperty("costGp").GetInt32().ToString();
						spStr = skill.GetProperty("costSp").GetInt32().ToString();
						cpStr = skill.GetProperty("costCp").GetInt32().ToString();
					}

					// Color by skill type and trainability
					Color rowColor;
					string skillType = skill.TryGetProperty("type", out var tp) ? tp.GetString() : "";
					if (!canTrain)
						rowColor = new Color(0.5f, 0.5f, 0.5f, 1f); // Grey = can't train
					else if (skillType == "magic")
						rowColor = new Color(0.5f, 0.8f, 1f, 1f); // Light blue
					else if (skillType == "combat" || skillType == "defense" || skillType == "ability")
						rowColor = new Color(1f, 0.85f, 0.5f, 1f); // Gold
					else if (skillType == "language")
						rowColor = new Color(0.7f, 0.9f, 0.7f, 1f); // Light green
					else
						rowColor = new Color(0.9f, 0.9f, 0.9f, 1f); // White

					var row = new Button();
					row.Flat = true;
					row.CustomMinimumSize = new Vector2(0, 22);
					row.ToggleMode = true;
					row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
					row.ClipText = true;

					// Format: Name(padded) Rank Value PP GP SP CP
					string rowText = $"{name,-22} {rank,-12} {value,4}   {ppStr,3}  {gpStr,3}  {spStr,3}  {cpStr,3}";
					row.Text = rowText;
					row.AddThemeFontSizeOverride("font_size", 12);
					row.AddThemeColorOverride("font_color", rowColor);
					row.AddThemeColorOverride("font_hover_color", new Color(1f, 1f, 0.6f, 1f));
					row.AddThemeColorOverride("font_pressed_color", new Color(1f, 1f, 1f, 1f));

					string capturedKey = key;
					bool capturedCanTrain = canTrain;
					row.Pressed += () =>
					{
						// Deselect all other rows
						foreach (Node c in _trainerSkillList.GetChildren())
						{
							if (c is Button b && b != row) b.ButtonPressed = false;
						}
						_trainerSelectedSkillKey = capturedKey;
						_trainerTrainBtn.Disabled = !capturedCanTrain;
					};

					_trainerSkillList.AddChild(row);
				}
			}

			_trainerWindow.Show();
			GD.Print($"[UI] Trainer window opened: {_trainerNpcName}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] Trainer parse error: {ex.Message}");
		}
	}

	// ═══════════════════════════════════════════════════════════════
	//  COMPANION WINDOW (Pet / future Mercenary)
	// ═══════════════════════════════════════════════════════════════
	private void BuildTrainerWindow()
	{
		_trainerWindow = new PanelContainer();
		_trainerWindow.Name = "TrainerWindow";
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.06f, 0.06f, 0.1f, 0.95f);
		style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = 2;
		style.BorderColor = new Color(0.6f, 0.5f, 0.2f, 0.9f);
		style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 6;
		style.ShadowColor = new Color(0, 0, 0, 0.7f);
		style.ShadowSize = 16;
		(_trainerWindow as PanelContainer).AddThemeStyleboxOverride("panel", style);

		_trainerWindow.SetAnchorsPreset(Control.LayoutPreset.Center);
		_trainerWindow.OffsetLeft = -360;
		_trainerWindow.OffsetRight = 360;
		_trainerWindow.OffsetTop = -260;
		_trainerWindow.OffsetBottom = 260;

		var mainHbox = new HBoxContainer();
		mainHbox.AddThemeConstantOverride("separation", 10);
		mainHbox.OffsetLeft = 12; mainHbox.OffsetTop = 10; mainHbox.OffsetRight = -12; mainHbox.OffsetBottom = -10;
		mainHbox.SetAnchorsPreset(LayoutPreset.FullRect);

		// ═══ LEFT SIDE: Skill list ═══
		var leftVbox = new VBoxContainer();
		leftVbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		leftVbox.AddThemeConstantOverride("separation", 4);

		// Header row
		var headerLabel = new Label();
		headerLabel.Text = "Training Window";
		headerLabel.HorizontalAlignment = HorizontalAlignment.Center;
		headerLabel.AddThemeFontSizeOverride("font_size", 13);
		headerLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.3f, 1f));
		leftVbox.AddChild(headerLabel);

		// Column header
		var colHeader = new Label();
		colHeader.Text = $"{"Skill Name",-22} {"Rank",-12} {"Val",4}    {"PP",3}  {"GP",3}  {"SP",3}  {"CP",3}";
		colHeader.AddThemeFontSizeOverride("font_size", 11);
		colHeader.AddThemeColorOverride("font_color", new Color(0.6f, 0.55f, 0.35f, 1f));
		leftVbox.AddChild(colHeader);

		// Scrollable skill list
		var scroll = new ScrollContainer();
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.CustomMinimumSize = new Vector2(480, 0);

		_trainerSkillList = new VBoxContainer();
		_trainerSkillList.AddThemeConstantOverride("separation", 1);
		_trainerSkillList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(_trainerSkillList);
		leftVbox.AddChild(scroll);

		mainHbox.AddChild(leftVbox);

		// ═══ RIGHT SIDE: Info panel ═══
		var rightVbox = new VBoxContainer();
		rightVbox.AddThemeConstantOverride("separation", 8);
		rightVbox.CustomMinimumSize = new Vector2(140, 0);

		// NPC Name / Title
		_trainerTitle = new Label();
		_trainerTitle.Text = "Trainer";
		_trainerTitle.HorizontalAlignment = HorizontalAlignment.Center;
		_trainerTitle.AddThemeFontSizeOverride("font_size", 14);
		_trainerTitle.AddThemeColorOverride("font_color", new Color(0.3f, 0.85f, 0.85f, 1f));
		rightVbox.AddChild(_trainerTitle);

		// Practices
		_trainerPracticesLabel = new Label();
		_trainerPracticesLabel.Text = "Practices: 0";
		_trainerPracticesLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_trainerPracticesLabel.AddThemeFontSizeOverride("font_size", 13);
		_trainerPracticesLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.4f, 1f));
		rightVbox.AddChild(_trainerPracticesLabel);

		// Train button
		_trainerTrainBtn = new Button();
		_trainerTrainBtn.Text = "Train";
		_trainerTrainBtn.CustomMinimumSize = new Vector2(0, 36);
		_trainerTrainBtn.AddThemeFontSizeOverride("font_size", 14);
		_trainerTrainBtn.Disabled = true;
		_trainerTrainBtn.Pressed += OnTrainSkillPressed;
		rightVbox.AddChild(_trainerTrainBtn);

		// Spacer
		var spacer = new Control();
		spacer.SizeFlagsVertical = SizeFlags.ExpandFill;
		rightVbox.AddChild(spacer);

		// Money header
		var moneyHeader = new Label();
		moneyHeader.Text = "Money";
		moneyHeader.HorizontalAlignment = HorizontalAlignment.Center;
		moneyHeader.AddThemeFontSizeOverride("font_size", 12);
		moneyHeader.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.4f, 1f));
		rightVbox.AddChild(moneyHeader);

		// Money rows
		_trainerMoneyPp = AddMoneyRow(rightVbox, "PP", new Color(0.85f, 0.85f, 0.95f, 1f));
		_trainerMoneyGp = AddMoneyRow(rightVbox, "GP", new Color(1f, 0.85f, 0.2f, 1f));
		_trainerMoneySp = AddMoneyRow(rightVbox, "SP", new Color(0.8f, 0.8f, 0.8f, 1f));
		_trainerMoneyCp = AddMoneyRow(rightVbox, "CP", new Color(0.75f, 0.55f, 0.3f, 1f));

		// Done button
		var doneBtn = new Button();
		doneBtn.Text = "Done";
		doneBtn.CustomMinimumSize = new Vector2(0, 32);
		doneBtn.AddThemeFontSizeOverride("font_size", 13);
		doneBtn.Pressed += () => _trainerWindow.Hide();
		rightVbox.AddChild(doneBtn);

		mainHbox.AddChild(rightVbox);
		(_trainerWindow as PanelContainer).AddChild(mainHbox);
		AddChild(_trainerWindow);
		_trainerWindow.Hide();
	}

	private Label AddMoneyRow(VBoxContainer parent, string label, Color color)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		var lbl = new Label();
		lbl.Text = label;
		lbl.CustomMinimumSize = new Vector2(28, 0);
		lbl.AddThemeFontSizeOverride("font_size", 12);
		lbl.AddThemeColorOverride("font_color", color);
		row.AddChild(lbl);
		var val = new Label();
		val.Text = "0";
		val.AddThemeFontSizeOverride("font_size", 12);
		val.AddThemeColorOverride("font_color", color);
		row.AddChild(val);
		parent.AddChild(row);
		return val;
	}

	private void OnTrainSkillPressed()
	{
		if (string.IsNullOrEmpty(_trainerSelectedSkillKey)) return;
		string json = $"{{\"type\": \"TRAIN_SKILL\", \"skillKey\": \"{_trainerSelectedSkillKey}\", \"npcId\": {_trainerNpcId}, \"npcName\": \"{EscapeJson(_trainerNpcName)}\"}}";
		_client.SendRaw(json);
	}
	private void OnBankOpened(Variant data) { if (!IsInstanceValid(this)) return; Log("SYSTEM", "Bank window opened (Placeholder)"); }

}
