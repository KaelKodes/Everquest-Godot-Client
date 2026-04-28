using Godot;
using System;

/// <summary>
/// Modal popup dialog for editing a single social/macro.
/// Matches EQ's Edit Social window: name, color picker (20 colored A buttons), 5 command lines.
/// </summary>
public partial class EditSocialDialog : Panel
{
	[Signal] public delegate void SocialAcceptedEventHandler(int socialIndex, string name, int color,
		string line0, string line1, string line2, string line3, string line4);
	[Signal] public delegate void DialogClosedEventHandler();

	private int _socialIndex = -1;
	private int _selectedColor = 0;

	private LineEdit _nameField;
	private Button[] _colorButtons = new Button[20];
	private LineEdit[] _lineFields = new LineEdit[5];
	private Button _clearBtn;
	private Button _acceptBtn;
	private Button _closeBtn;

	public override void _Ready()
	{
		CustomMinimumSize = new Vector2(420, 320);
		MouseFilter = MouseFilterEnum.Stop;

		// Panel style
		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.97f);
		panelStyle.BorderWidthLeft = 2; panelStyle.BorderWidthTop = 2;
		panelStyle.BorderWidthRight = 2; panelStyle.BorderWidthBottom = 2;
		panelStyle.BorderColor = new Color(0.6f, 0.5f, 0.2f, 1.0f);
		panelStyle.CornerRadiusTopLeft = 4; panelStyle.CornerRadiusTopRight = 4;
		panelStyle.CornerRadiusBottomLeft = 4; panelStyle.CornerRadiusBottomRight = 4;
		panelStyle.ContentMarginLeft = 12; panelStyle.ContentMarginRight = 12;
		panelStyle.ContentMarginTop = 8; panelStyle.ContentMarginBottom = 8;
		AddThemeStyleboxOverride("panel", panelStyle);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsPreset(LayoutPreset.FullRect);
		vbox.OffsetLeft = 12; vbox.OffsetRight = -12;
		vbox.OffsetTop = 8; vbox.OffsetBottom = -8;
		vbox.AddThemeConstantOverride("separation", 6);
		AddChild(vbox);

		// Title bar
		var titleRow = new HBoxContainer();
		var titleLabel = new Label();
		titleLabel.Text = "Edit Social";
		titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.AddThemeFontSizeOverride("font_size", 16);
		titleLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.4f));
		titleRow.AddChild(titleLabel);

		_closeBtn = new Button();
		_closeBtn.Text = "✕";
		_closeBtn.CustomMinimumSize = new Vector2(24, 24);
		_closeBtn.AddThemeFontSizeOverride("font_size", 12);
		_closeBtn.Pressed += () => { Visible = false; EmitSignal(SignalName.DialogClosed); };
		titleRow.AddChild(_closeBtn);
		vbox.AddChild(titleRow);

		// Social Name row
		var nameRow = new HBoxContainer();
		var nameLabel = new Label();
		nameLabel.Text = "Social Name:";
		nameLabel.CustomMinimumSize = new Vector2(100, 0);
		nameLabel.AddThemeFontSizeOverride("font_size", 12);
		nameLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f));
		nameRow.AddChild(nameLabel);

		_nameField = new LineEdit();
		_nameField.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_nameField.PlaceholderText = "Name";
		_nameField.MaxLength = 20;
		_nameField.AddThemeFontSizeOverride("font_size", 12);
		nameRow.AddChild(_nameField);
		vbox.AddChild(nameRow);

		// Color picker row — 20 colored "A" buttons
		var colorRow = new HBoxContainer();
		colorRow.AddThemeConstantOverride("separation", 2);

		for (int i = 0; i < 20; i++)
		{
			int colorIndex = i;
			var colorBtn = new Button();
			colorBtn.Text = "A";
			colorBtn.CustomMinimumSize = new Vector2(18, 22);
			colorBtn.AddThemeFontSizeOverride("font_size", 11);
			colorBtn.AddThemeColorOverride("font_color", SocialManager.EQColors[i]);
			colorBtn.ClipText = true;

			var btnStyle = new StyleBoxFlat();
			btnStyle.BgColor = new Color(0.08f, 0.08f, 0.1f, 0.9f);
			btnStyle.BorderWidthLeft = 1; btnStyle.BorderWidthTop = 1;
			btnStyle.BorderWidthRight = 1; btnStyle.BorderWidthBottom = 1;
			btnStyle.BorderColor = new Color(0.3f, 0.25f, 0.15f, 0.6f);
			colorBtn.AddThemeStyleboxOverride("normal", btnStyle);

			colorBtn.Pressed += () => SelectColor(colorIndex);
			colorRow.AddChild(colorBtn);
			_colorButtons[i] = colorBtn;
		}

		vbox.AddChild(colorRow);

		// Second row of color buttons (for spacing, show color on the name preview)
		// Actually EQ shows them in one row — 20 buttons is tight but matches the screenshots

		// Separator
		var sep = new HSeparator();
		sep.AddThemeConstantOverride("separation", 4);
		vbox.AddChild(sep);

		// 5 command line fields
		for (int i = 0; i < 5; i++)
		{
			var lineField = new LineEdit();
			lineField.PlaceholderText = $"Line {i + 1}";
			lineField.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			lineField.CustomMinimumSize = new Vector2(0, 28);
			lineField.AddThemeFontSizeOverride("font_size", 12);

			var fieldStyle = new StyleBoxFlat();
			fieldStyle.BgColor = new Color(0.04f, 0.04f, 0.06f, 0.95f);
			fieldStyle.BorderWidthLeft = 1; fieldStyle.BorderWidthTop = 1;
			fieldStyle.BorderWidthRight = 1; fieldStyle.BorderWidthBottom = 1;
			fieldStyle.BorderColor = new Color(0.3f, 0.25f, 0.15f, 0.5f);
			lineField.AddThemeStyleboxOverride("normal", fieldStyle);

			vbox.AddChild(lineField);
			_lineFields[i] = lineField;
		}

		// Button row
		var btnRow = new HBoxContainer();
		btnRow.Alignment = BoxContainer.AlignmentMode.End;
		btnRow.AddThemeConstantOverride("separation", 10);

		_clearBtn = new Button();
		_clearBtn.Text = "Clear";
		_clearBtn.CustomMinimumSize = new Vector2(80, 30);
		_clearBtn.AddThemeFontSizeOverride("font_size", 13);
		_clearBtn.Pressed += OnClear;
		btnRow.AddChild(_clearBtn);

		_acceptBtn = new Button();
		_acceptBtn.Text = "Accept";
		_acceptBtn.CustomMinimumSize = new Vector2(80, 30);
		_acceptBtn.AddThemeFontSizeOverride("font_size", 13);
		_acceptBtn.Pressed += OnAccept;
		btnRow.AddChild(_acceptBtn);

		vbox.AddChild(btnRow);
	}

	// ── Color Selection ─────────────────────────────────────────────

	private void SelectColor(int colorIndex)
	{
		_selectedColor = colorIndex;

		// Highlight the selected color button
		for (int i = 0; i < 20; i++)
		{
			var btnStyle = new StyleBoxFlat();
			btnStyle.BgColor = new Color(0.08f, 0.08f, 0.1f, 0.9f);
			btnStyle.BorderWidthLeft = 1; btnStyle.BorderWidthTop = 1;
			btnStyle.BorderWidthRight = 1; btnStyle.BorderWidthBottom = 1;

			if (i == colorIndex)
				btnStyle.BorderColor = new Color(1.0f, 0.9f, 0.4f, 1.0f); // Bright gold highlight
			else
				btnStyle.BorderColor = new Color(0.3f, 0.25f, 0.15f, 0.6f);

			_colorButtons[i].AddThemeStyleboxOverride("normal", btnStyle);
		}

		// Update name field color to preview
		_nameField.AddThemeColorOverride("font_color", SocialManager.EQColors[colorIndex]);
	}

	// ── Open / Populate ─────────────────────────────────────────────

	/// <summary>
	/// Open the dialog to edit a specific social.
	/// </summary>
	public void OpenForSocial(int socialIndex, SocialManager.Social social)
	{
		_socialIndex = socialIndex;
		_nameField.Text = social.Name;
		_selectedColor = social.Color;

		for (int i = 0; i < 5; i++)
			_lineFields[i].Text = social.Lines[i] ?? "";

		SelectColor(social.Color);
		Visible = true;

		// Center on screen
		var viewport = GetViewport();
		if (viewport != null)
		{
			var screenSize = viewport.GetVisibleRect().Size;
			GlobalPosition = (screenSize - Size) / 2;
		}
	}

	// ── Buttons ─────────────────────────────────────────────────────

	private void OnClear()
	{
		_nameField.Text = "";
		_selectedColor = 0;
		SelectColor(0);
		for (int i = 0; i < 5; i++)
			_lineFields[i].Text = "";
	}

	private void OnAccept()
	{
		EmitSignal(SignalName.SocialAccepted,
			_socialIndex,
			_nameField.Text,
			_selectedColor,
			_lineFields[0].Text,
			_lineFields[1].Text,
			_lineFields[2].Text,
			_lineFields[3].Text,
			_lineFields[4].Text
		);
		Visible = false;
		EmitSignal(SignalName.DialogClosed);
	}
}
