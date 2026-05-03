using Godot;
using System;
using System.Text.Json;

public partial class MainUI : Control
{
	private void OnMercenariesUpdated(Variant data)
	{
		try
		{
			string json = data.AsString();
			using var doc = JsonDocument.Parse(json);
			_mercenariesData = doc.RootElement.Clone();
			UpdateMercenariesUI();
		}
		catch (Exception e)
		{
			GD.PrintErr($"[MainUI.Mercenaries] Error handling mercenaries update: {e.Message}");
		}
	}

	private void UpdateMercenariesUI()
	{
		EnsureMercenariesManagerWindow();

		if (_mercenariesData.ValueKind != JsonValueKind.Object || !_mercenariesData.TryGetProperty("mercenaries", out var mercArray) || mercArray.ValueKind != JsonValueKind.Array)
		{
			for (int i = 0; i < 2; i++)
			{
				if (_mercSlotButtons[i] != null) _mercSlotButtons[i].Text = "---";
			}
			return;
		}

		for (int i = 0; i < 2; i++)
		{
			if (i < mercArray.GetArrayLength())
			{
				var merc = mercArray[i];
				if (merc.ValueKind != JsonValueKind.Null)
				{
					string name = merc.TryGetProperty("name", out var n) ? n.GetString() : "Unknown";
					string race = merc.TryGetProperty("race", out var r) ? r.GetString() : "Human";
					string cls = merc.TryGetProperty("class", out var c) ? c.GetString() : "Warrior";
					if (_mercSlotButtons[i] != null) _mercSlotButtons[i].Text = $"{name} {race}/{cls}";
					continue;
				}
			}
			if (_mercSlotButtons[i] != null) _mercSlotButtons[i].Text = "---";
		}
	}

	private void EnsureMercenariesManagerWindow()
	{
		if (_mercenariesManagerWindow != null && IsInstanceValid(_mercenariesManagerWindow)) return;

		_mercenariesManagerWindow = new Window();
		_mercenariesManagerWindow.Name = "MercenariesManagerWindow";
		_mercenariesManagerWindow.Title = "Mercenaries";
		_mercenariesManagerWindow.Size = new Vector2I(200, 260);
		_mercenariesManagerWindow.Position = new Vector2I(100, 100);
		_mercenariesManagerWindow.Visible = false; // Hide by default until opened via some menu
		_mercenariesManagerWindow.CloseRequested += () => _mercenariesManagerWindow.Visible = false;
		AddChild(_mercenariesManagerWindow);

		var panel = new PanelContainer();
		panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.9f);
		panel.AddThemeStyleboxOverride("panel", style);
		_mercenariesManagerWindow.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 10);
		panel.AddChild(vbox);

		var title = new Label();
		title.Text = "Mercenaries";
		title.AddThemeFontSizeOverride("font_size", 18);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(title);

		// The list
		var listVbox = new VBoxContainer();
		listVbox.AddThemeConstantOverride("separation", 4);
		var panelBox = new PanelContainer();
		var boxStyle = new StyleBoxFlat();
		boxStyle.BorderColor = Colors.White;
		boxStyle.BorderWidthBottom = 2; boxStyle.BorderWidthTop = 2; boxStyle.BorderWidthLeft = 2; boxStyle.BorderWidthRight = 2;
		panelBox.AddThemeStyleboxOverride("panel", boxStyle);
		panelBox.AddChild(listVbox);
		vbox.AddChild(panelBox);

		_mercSlotButtons = new Button[2];

		for (int i = 0; i < 2; i++)
		{
			var btn = new Button();
			btn.CustomMinimumSize = new Vector2(0, 30);
			btn.Text = "---";
			btn.ToggleMode = true;
			int index = i;
			btn.Pressed += () =>
			{
				_selectedMercSlot = index;
				for (int j = 0; j < 2; j++)
				{
					if (j != index) _mercSlotButtons[j].ButtonPressed = false;
				}
			};
			listVbox.AddChild(btn);
			_mercSlotButtons[i] = btn;
		}

		var btnVbox = new VBoxContainer();
		btnVbox.AddThemeConstantOverride("separation", 6);
		vbox.AddChild(btnVbox);

		_mercSwitchBtn = new Button { Text = "Switch" };
		_mercSwitchBtn.CustomMinimumSize = new Vector2(0, 35);
		_mercSwitchBtn.Pressed += () => SendMercAction("switch");
		btnVbox.AddChild(_mercSwitchBtn);

		_mercSuspendBtn = new Button { Text = "Suspend" };
		_mercSuspendBtn.CustomMinimumSize = new Vector2(0, 35);
		_mercSuspendBtn.Pressed += () => SendMercAction("suspend");
		btnVbox.AddChild(_mercSuspendBtn);

		_mercReleaseBtn = new Button { Text = "Release" };
		_mercReleaseBtn.CustomMinimumSize = new Vector2(0, 35);
		_mercReleaseBtn.Pressed += () => SendMercAction("release");
		btnVbox.AddChild(_mercReleaseBtn);
	}

	private void SendMercAction(string action)
	{
		if (_selectedMercSlot < 0 || _selectedMercSlot >= 2) return;
		_client.SendRaw($"{{\"type\": \"MERCENARY_ACTION\", \"action\": \"{action}\", \"index\": {_selectedMercSlot}}}");
	}
}
