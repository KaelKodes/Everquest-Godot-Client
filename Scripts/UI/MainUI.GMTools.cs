using Godot;
using System;
using System.Text.Json;

public partial class MainUI
{
	private Button _gmToolsMenuBtn;
	private Window _gmToolsPanel;
	private GmInventoryPeekWindow _gmPeekWindow;
	private bool _isGmAccount;

	public void ApplyGmStatusFromServer(bool isGm)
	{
		_isGmAccount = isGm;
		if (_gmToolsMenuBtn != null)
			_gmToolsMenuBtn.Visible = isGm;
		if (!isGm && _gmToolsPanel != null)
			_gmToolsPanel.Hide();
	}

	/// <summary>Wired from <see cref="MainUI._Ready"/> after the main menu VBox exists.</summary>
	private void RegisterGmToolsMenu(VBoxContainer menuVBox)
	{
		if (menuVBox == null) return;
		_gmToolsMenuBtn = new Button();
		_gmToolsMenuBtn.Text = "GM TOOLS";
		_gmToolsMenuBtn.Visible = false;
		_gmToolsMenuBtn.CustomMinimumSize = new Vector2(0, 30);
		menuVBox.AddChild(_gmToolsMenuBtn);
		_gmToolsMenuBtn.Pressed += ToggleGmToolsPanel;

		BuildGmToolsPanel();
	}

	private void ToggleGmToolsPanel()
	{
		if (_gmToolsPanel == null) return;
		_gmToolsPanel.Visible = !_gmToolsPanel.Visible;
	}

	private void BuildGmToolsPanel()
	{
		_gmToolsPanel = new Window();
		_gmToolsPanel.Title = "GM Tools";
		_gmToolsPanel.Size = new Vector2I(280, 420);
		_gmToolsPanel.Visible = false;
		_gmToolsPanel.CloseRequested += () => _gmToolsPanel.Hide();
		AddChild(_gmToolsPanel);

		var v = new VBoxContainer();
		v.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		v.OffsetLeft = 10;
		v.OffsetRight = -10;
		v.OffsetTop = 10;
		v.OffsetBottom = -10;
		_gmToolsPanel.AddChild(v);

		void AddGmButton(string label, Action onPress)
		{
			var b = new Button { Text = label };
			b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			b.Pressed += onPress;
			v.AddChild(b);
		}

		AddGmButton("Summon player to me", () => PromptGmLine("Summon to me", "Player name", GmGuessTargetPlayerName(), name =>
		{
			if (string.IsNullOrWhiteSpace(name)) return;
			SendGmServerCommand("/summon", name.Trim());
		}));
		AddGmButton("Teleport to player", () => PromptGmLine("Teleport to", "Player name", GmGuessTargetPlayerName(), name =>
		{
			if (string.IsNullOrWhiteSpace(name)) return;
			SendGmServerCommand("/goto", name.Trim());
		}));
		AddGmButton("Succor (group if grouped)", () =>
		{
			SendGmServerCommand("/groupsuccor", "");
			_gmToolsPanel?.Hide();
		});
		AddGmButton("View player inventory…", () => PromptGmLine("Peek inventory", "Player name", GmGuessTargetPlayerName(), name =>
		{
			if (string.IsNullOrWhiteSpace(name)) return;
			SendGmServerCommand("/peek", name.Trim());
		}));
		AddGmButton("Spawn item…", () => PromptGmLine("Spawn item", "EQ item id (number)", "", raw =>
		{
			if (string.IsNullOrWhiteSpace(raw)) return;
			SendGmServerCommand("/item", raw.Trim());
		}));
		AddGmButton("Spawn item on player…", () =>
		{
			PromptGmLineTwo("Spawn item on player", "EQ item id", "", "Player name", GmGuessTargetPlayerName(), (itemId, pname) =>
			{
				if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(pname)) return;
				SendGmServerCommand("/item", $"{itemId.Trim()} {pname.Trim()}");
			});
		});
		AddGmButton("Spawn mob…", () => PromptGmLine("Spawn mob", "Mob key (e.g. spawn id)", "", key =>
		{
			if (string.IsNullOrWhiteSpace(key)) return;
			SendGmServerCommand("/spawn", key.Trim());
		}));
		AddGmButton("Adjust EXP…", () => OpenGmExpDialog());
		AddGmButton("Talk as NPC (target + text)…", () => PromptGmLine("NPC says", "Text (target an NPC first)", "", text =>
		{
			if (string.IsNullOrWhiteSpace(text)) return;
			SendGmServerCommand("/npcsay", text.Trim());
		}));
		AddGmButton("Take control of NPC", () =>
		{
			SendGmServerCommand("/possess", "");
			Log("SYSTEM", "[color=gray]If implemented, you would puppet your NPC target. Server reports status in chat.[/color]");
		});
	}

	private void SendGmServerCommand(string command, string args)
	{
		if (_client == null) return;
		_client.SendRaw($"{{\"type\": \"SERVER_COMMAND\", \"command\": \"{EscapeJson(command)}\", \"args\": \"{EscapeJson(args)}\"}}");
	}

	private string GmGuessTargetPlayerName()
	{
		if (_targetNameLabel == null) return "";
		string t = _targetNameLabel.Text ?? "";
		int idx = t.IndexOf(" (Lv", StringComparison.Ordinal);
		if (idx > 0) return t[..idx].Trim();
		return t.Trim();
	}

	private void PromptGmLine(string title, string fieldLabel, string initial, Action<string> onOk)
	{
		var dlg = new Window();
		dlg.Title = title;
		dlg.Size = new Vector2I(400, 150);
		dlg.Unresizable = true;
		dlg.CloseRequested += () => dlg.QueueFree();
		AddChild(dlg);
		PushGmWorldInputBlock();
		dlg.TreeExited += PopGmWorldInputBlock;

		var v = new VBoxContainer();
		v.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		v.OffsetLeft = 12;
		v.OffsetRight = -12;
		v.OffsetTop = 12;
		v.OffsetBottom = -12;
		dlg.AddChild(v);

		v.AddChild(new Label { Text = fieldLabel });
		var edit = new LineEdit();
		edit.Text = initial ?? "";
		edit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		edit.PlaceholderText = fieldLabel;
		v.AddChild(edit);

		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		var ok = new Button { Text = "OK" };
		var cancel = new Button { Text = "Cancel" };
		row.AddChild(cancel);
		row.AddChild(ok);
		v.AddChild(row);

		void Submit()
		{
			onOk(edit.Text);
			dlg.QueueFree();
		}

		cancel.Pressed += () => dlg.QueueFree();
		ok.Pressed += Submit;
		edit.TextSubmitted += _ => Submit();
		dlg.PopupCentered();
		edit.GrabFocus();
	}

	private void PromptGmLineTwo(string title, string l1, string i1, string l2, string i2, Action<string, string> onOk)
	{
		var dlg = new Window();
		dlg.Title = title;
		dlg.Size = new Vector2I(380, 200);
		dlg.Unresizable = true;
		dlg.CloseRequested += () => dlg.QueueFree();
		AddChild(dlg);
		PushGmWorldInputBlock();
		dlg.TreeExited += PopGmWorldInputBlock;

		var v = new VBoxContainer();
		v.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		v.OffsetLeft = 12;
		v.OffsetRight = -12;
		v.OffsetTop = 12;
		v.OffsetBottom = -12;
		dlg.AddChild(v);

		v.AddChild(new Label { Text = l1 });
		var e1 = new LineEdit { Text = i1 ?? "" };
		e1.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		v.AddChild(e1);
		v.AddChild(new Label { Text = l2 });
		var e2 = new LineEdit { Text = i2 ?? "" };
		e2.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		v.AddChild(e2);

		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		var ok = new Button { Text = "OK" };
		var cancel = new Button { Text = "Cancel" };
		row.AddChild(cancel);
		row.AddChild(ok);
		v.AddChild(row);

		cancel.Pressed += () => dlg.QueueFree();
		ok.Pressed += () =>
		{
			onOk(e1.Text, e2.Text);
			dlg.QueueFree();
		};
		dlg.PopupCentered();
		e1.GrabFocus();
	}

	private void OpenGmExpDialog()
	{
		var dlg = new Window();
		dlg.Title = "GM: Adjust EXP";
		dlg.Size = new Vector2I(400, 260);
		dlg.Unresizable = true;
		dlg.CloseRequested += () => dlg.QueueFree();
		AddChild(dlg);
		PushGmWorldInputBlock();
		dlg.TreeExited += PopGmWorldInputBlock;

		var v = new VBoxContainer();
		v.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		v.OffsetLeft = 12;
		v.OffsetRight = -12;
		v.OffsetTop = 12;
		v.OffsetBottom = -12;
		dlg.AddChild(v);

		v.AddChild(new Label { Text = "Player (leave empty for yourself)" });
		var playerEdit = new LineEdit { Text = GmGuessTargetPlayerName() };
		playerEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		v.AddChild(playerEdit);

		v.AddChild(new Label { Text = "Mode" });
		var mode = new OptionButton();
		mode.AddItem("Grant (+)", 0);
		mode.AddItem("Remove (-)", 1);
		mode.AddItem("Set (=)", 2);
		v.AddChild(mode);

		v.AddChild(new Label { Text = "Amount (experience points)" });
		var amt = new LineEdit { Text = "1000", PlaceholderText = "e.g. 5000" };
		amt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		v.AddChild(amt);

		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		var ok = new Button { Text = "Apply" };
		var cancel = new Button { Text = "Cancel" };
		row.AddChild(cancel);
		row.AddChild(ok);
		v.AddChild(row);

		cancel.Pressed += () => dlg.QueueFree();
		ok.Pressed += () =>
		{
			if (!int.TryParse(amt.Text.Trim(), out int n) || n < 0)
			{
				Log("SYSTEM", "Enter a non-negative integer amount.");
				return;
			}
			char sign = mode.Selected == 1 ? '-' : (mode.Selected == 2 ? '=' : '+');
			string pname = playerEdit.Text.Trim();
			string argBody = string.IsNullOrEmpty(pname) ? $"{sign}{n}" : $"{pname} {sign}{n}";
			SendGmServerCommand("/exp", argBody);
			dlg.QueueFree();
		};

		dlg.PopupCentered();
		playerEdit.GrabFocus();
	}

	private void HandleGmInventoryViewMessage(string json)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			string pname = root.TryGetProperty("targetName", out var tn) ? tn.GetString() : "Unknown";
			if (!root.TryGetProperty("inventory", out var inv) || inv.ValueKind != JsonValueKind.Array)
				return;
			if (_gmPeekWindow == null || !IsInstanceValid(_gmPeekWindow))
			{
				_gmPeekWindow = new GmInventoryPeekWindow();
				AddChild(_gmPeekWindow);
			}
			_gmPeekWindow.ShowForPlayer(pname, inv);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[UI] GM_INVENTORY_VIEW: {ex.Message}");
		}
	}
}
