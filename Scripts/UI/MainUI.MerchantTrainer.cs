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
				int icon = item.TryGetProperty("icon", out var ic) ? ic.GetInt32() : 0;

				_merchantItems.Add(new MerchantItem
				{
					Name = iName,
					ItemKey = iKey,
					Price = price,
					PriceText = priceText,
					StatsStr = "", // Not needed in list view
					ScrollLevel = scrollLevel,
					ItemType = itemType,
					Classes = classes,
					RecLevel = recLevel,
					NpcId = npcId,
					Icon = icon
				});
			}

			// Setup UI events if not already done
			if (!_merchantSearchInput.HasSignal("text_changed") || _merchantSearchInput.GetSignalConnectionList("text_changed").Count == 0)
			{
				_merchantSearchInput.TextChanged += (text) => RebuildMerchantList();
				_merchantTabs.TabChanged += (tab) => {
					if (tab == 0) RebuildMerchantList();
					else _client.SendRaw($"{{\"type\": \"BUY_RECOVER\", \"npcId\": \"{_activeMerchantId}\"}}"); // Fetch recover list when switching to tab
				};
				
				_merchantActionBtn.Pressed += OnMerchantActionClicked;
				_merchantSellJunkBtn.Pressed += () => _client.SendRaw($"{{\"type\": \"SELL_JUNK\", \"npcId\": \"{_activeMerchantId}\"}}");
				
				// Enable drop on slot rect
				_merchantSlotRect.MouseFilter = Control.MouseFilterEnum.Stop;
				// Connect GUI input directly since Godot C# CanDropData/DropData override can be finicky on TextureRect
				_merchantSlotRect.GuiInput += OnMerchantSlotGuiInput;
			}

			// Reset Selection Slot
			ClearMerchantSelection();

			// Render the list
			_merchantTabs.CurrentTab = 0;
			RebuildMerchantList();

			_merchantWindow.Show();
			Log("SYSTEM", $"[color=cyan]{npcName} opens their shop...[/color]");

			// Refresh inventory to show sell buttons
			if (!string.IsNullOrEmpty(_client.LastInventoryPayload))
				OnInventoryUpdated(_client.LastInventoryPayload);
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Merchant Error: {ex.Message}"); }
	}
	
	private void OnMerchantSlotGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseBtn && !mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
		{
			// Try to get drop data from viewport
			var viewport = GetViewport();
			var dragData = viewport.GuiGetDragData();
			if (dragData.VariantType != Variant.Type.Nil)
			{
				string json = (string)dragData;
				using var doc = JsonDocument.Parse(json);
				if (doc.RootElement.GetProperty("type").GetString() == "INVENTORY_ITEM")
				{
					string itemKey = doc.RootElement.GetProperty("itemKey").GetString();
					string instId = doc.RootElement.GetProperty("instanceId").GetString();
					
					// Request offer from server
					_client.SendRaw($"{{\"type\": \"GET_OFFER\", \"npcId\": \"{_activeMerchantId}\", \"itemInstanceId\": \"{instId}\"}}");
				}
			}
		}
	}
	
	private void OnMerchantOfferReceived(Variant data)
	{
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			
			string name = root.GetProperty("name").GetString();
			int instId = root.GetProperty("itemId").GetInt32();
			int price = root.GetProperty("price").GetInt32();
			string priceText = root.GetProperty("priceText").GetString();
			int icon = root.GetProperty("icon").GetInt32();
			
			_merchantSelectedItemId = instId.ToString();
			_merchantSelectedItemKey = null;
			_merchantSelectedAction = "SELL";
			
			_merchantSelectionName.Text = name;
			_merchantSelectionPrice.Text = $"Offer: {priceText}";
			_merchantSlotRect.Texture = IconManager.Instance.GetItemIcon(icon);
			
			_merchantActionBtn.Text = "Accept";
			_merchantActionBtn.Disabled = false;
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Merchant Offer Error: {ex.Message}"); }
	}
	
	private void OnMerchantRecoverListReceived(Variant data)
	{
		try
		{
			string json = (string)data;
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			
			// Rebuild Recover List
			foreach (Node child in _merchantRecoverList.GetChildren()) child.QueueFree();
			
			var items = root.GetProperty("items");
			var iconMgr = IconManager.Instance;
			
			foreach (var item in items.EnumerateArray())
			{
				string name = item.GetProperty("name").GetString();
				string iKey = item.GetProperty("itemKey").ToString().Trim('"');
				int price = item.TryGetProperty("price", out var pv) ? pv.GetInt32() : 0;
				string priceText = item.TryGetProperty("priceText", out var pt) ? pt.GetString() : $"{price}cp";
				int icon = item.TryGetProperty("icon", out var ic) ? ic.GetInt32() : 0;
				int buybackId = item.GetProperty("buybackId").GetInt32();
				
				var row = CreateMerchantRow(name, priceText, icon, iconMgr, () => {
					_merchantSelectedItemId = null;
					_merchantSelectedItemKey = iKey;
					_merchantSelectedBuybackId = buybackId;
					_merchantSelectedPrice = price;
					_merchantSelectedAction = "BUY_RECOVER";
					
					_merchantSelectionName.Text = name;
					_merchantSelectionPrice.Text = $"Cost: {priceText}";
					_merchantSlotRect.Texture = iconMgr.GetItemIcon(icon);
					_merchantActionBtn.Text = "Accept";
					_merchantActionBtn.Disabled = _copper < price;
				});
				_merchantRecoverList.AddChild(row);
			}
		}
		catch (Exception ex) { GD.PrintErr($"[UI] Merchant Recover List Error: {ex.Message}"); }
	}
	
	private void ClearMerchantSelection()
	{
		_merchantSelectedItemId = null;
		_merchantSelectedItemKey = null;
		_merchantSelectedBuybackId = -1;
		_merchantSelectedAction = "";
		
		_merchantSelectionName.Text = "Select or Drop Item";
		_merchantSelectionPrice.Text = "---";
		_merchantSlotRect.Texture = null;
		_merchantActionBtn.Text = "Action";
		_merchantActionBtn.Disabled = true;
	}

	private void OnMerchantActionClicked()
	{
		if (_merchantSelectedAction == "BUY" && _merchantSelectedItemKey != null)
		{
			if (_copper < _merchantSelectedPrice) {
				Log("SYSTEM", "[color=red]You don't have enough money.[/color]");
				return;
			}
			_client.SendRaw($"{{\"type\": \"BUY\", \"npcId\": \"{_activeMerchantId}\", \"itemKey\": \"{_merchantSelectedItemKey}\"}}");
		}
		else if (_merchantSelectedAction == "SELL" && _merchantSelectedItemId != null)
		{
			_client.SendRaw($"{{\"type\": \"SELL\", \"npcId\": \"{_activeMerchantId}\", \"itemId\": {_merchantSelectedItemId}}}");
			ClearMerchantSelection();
		}
		else if (_merchantSelectedAction == "BUY_RECOVER" && _merchantSelectedBuybackId != -1)
		{
			if (_copper < _merchantSelectedPrice) {
				Log("SYSTEM", "[color=red]You don't have enough money.[/color]");
				return;
			}
			_client.SendRaw($"{{\"type\": \"BUY_RECOVER\", \"npcId\": \"{_activeMerchantId}\", \"buybackId\": {_merchantSelectedBuybackId}}}");
			ClearMerchantSelection();
		}
	}

	private void RebuildMerchantList()
	{
		// Clear old items
		foreach (Node child in _merchantTradeList.GetChildren()) child.QueueFree();

		string searchText = _merchantSearchInput.Text.ToLower();

		// Filter
		var filtered = new List<MerchantItem>();
		foreach (var mi in _merchantItems)
		{
			if (!string.IsNullOrEmpty(searchText) && !mi.Name.ToLower().Contains(searchText))
				continue;
			filtered.Add(mi);
		}

		filtered.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

		var iconMgr = IconManager.Instance;

		// Render
		foreach (var mi in filtered)
		{
			string displayName = mi.Name;
			if (mi.ScrollLevel > 0)
				displayName = $"{mi.Name} (Lv {mi.ScrollLevel})";
			else if (mi.RecLevel > 0)
				displayName = $"{mi.Name} (Req Lv {mi.RecLevel})";
				
			var row = CreateMerchantRow(displayName, mi.PriceText, mi.Icon, iconMgr, () => {
				_merchantSelectedItemId = null;
				_merchantSelectedItemKey = mi.ItemKey;
				_merchantSelectedPrice = mi.Price;
				_merchantSelectedAction = "BUY";
				
				_merchantSelectionName.Text = displayName;
				_merchantSelectionPrice.Text = $"Cost: {mi.PriceText}";
				_merchantSlotRect.Texture = iconMgr.GetItemIcon(mi.Icon);
				
				_merchantActionBtn.Text = "Accept";
				_merchantActionBtn.Disabled = _copper < mi.Price;
			});
			
			// Set color based on usability
			bool isUsable = true;
			if (mi.Classes != 65535 && (mi.Classes & _merchantPlayerClassBitmask) == 0)
				isUsable = false;
			int effectiveLevel = mi.ScrollLevel > 0 ? mi.ScrollLevel : mi.RecLevel;
			if (effectiveLevel > _merchantPlayerLevel)
				isUsable = false;
				
			var nameLbl = row.GetNode<Label>("RowHBox/NameLbl");
			if (!isUsable)
				nameLbl.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
			else if (mi.ItemType == 20 || mi.ScrollLevel > 0)
				nameLbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.85f, 1.0f));
			else
				nameLbl.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));

			_merchantTradeList.AddChild(row);
		}
	}
	
	private MarginContainer CreateMerchantRow(string name, string priceText, int icon, IconManager iconMgr, Action onClick)
	{
		var container = new MarginContainer();
		container.CustomMinimumSize = new Vector2(0, 40);
		
		var btn = new Button {
			Flat = true,
			FocusMode = Control.FocusModeEnum.None,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		btn.Pressed += () => onClick();
		container.AddChild(btn);

		var row = new HBoxContainer();
		row.Name = "RowHBox";
		row.MouseFilter = Control.MouseFilterEnum.Ignore;
		row.AddThemeConstantOverride("separation", 10);
		
		var iconRect = new TextureRect {
			Texture = iconMgr.GetItemIcon(icon),
			CustomMinimumSize = new Vector2(40, 40),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		
		var nameLbl = new Label {
			Name = "NameLbl",
			Text = name,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center,
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
			ClipText = true,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		nameLbl.AddThemeFontSizeOverride("font_size", 14);
		
		var qtyLbl = new Label {
			Text = "--",
			CustomMinimumSize = new Vector2(40, 0),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		
		var costLbl = new Label {
			Text = priceText,
			CustomMinimumSize = new Vector2(100, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		costLbl.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.3f));
		
		var lvlLbl = new Label {
			Text = "--",
			CustomMinimumSize = new Vector2(40, 0),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		
		row.AddChild(iconRect);
		row.AddChild(nameLbl);
		row.AddChild(qtyLbl);
		row.AddChild(costLbl);
		row.AddChild(lvlLbl);
		
		container.AddChild(row);
		
		return container;
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
