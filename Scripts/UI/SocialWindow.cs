using Godot;
using System;

/// <summary>
/// The Actions/Socials window (Ctrl+A). Displays 10 pages of 12 social buttons each.
/// Right-click a social to edit, left-click+drag to pick up for dropping onto a hotbar.
/// </summary>
public partial class SocialWindow : Panel
{
	[Signal] public delegate void SocialDragStartedEventHandler(int socialIndex, string socialName, int socialColor);
	[Signal] public delegate void WindowClosedEventHandler();

	private SocialManager _socialManager;
	private EditSocialDialog _editDialog;

	private int _currentPage = 0;
	private Button[] _socialButtons = new Button[12];
	private Label _pageLabel;

	// Drag state
	private int _dragSocialIndex = -1;

	public override void _Ready()
	{
		CustomMinimumSize = new Vector2(260, 360);
		Size = CustomMinimumSize;
		MouseFilter = MouseFilterEnum.Stop;

		// Panel style
		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.95f);
		panelStyle.BorderWidthLeft = 2; panelStyle.BorderWidthTop = 2;
		panelStyle.BorderWidthRight = 2; panelStyle.BorderWidthBottom = 2;
		panelStyle.BorderColor = new Color(0.6f, 0.5f, 0.2f, 1.0f);
		panelStyle.CornerRadiusTopLeft = 3; panelStyle.CornerRadiusTopRight = 3;
		panelStyle.CornerRadiusBottomLeft = 3; panelStyle.CornerRadiusBottomRight = 3;
		panelStyle.ContentMarginLeft = 8; panelStyle.ContentMarginRight = 8;
		panelStyle.ContentMarginTop = 6; panelStyle.ContentMarginBottom = 6;
		AddThemeStyleboxOverride("panel", panelStyle);

		BuildUI();
	}

	public void Init(SocialManager socialManager)
	{
		_socialManager = socialManager;
		RefreshButtons();
	}

	// ── UI Construction ─────────────────────────────────────────────

	private void BuildUI()
	{
		var vbox = new VBoxContainer();
		vbox.SetAnchorsPreset(LayoutPreset.FullRect);
		vbox.OffsetLeft = 8; vbox.OffsetRight = -8;
		vbox.OffsetTop = 6; vbox.OffsetBottom = -6;
		vbox.AddThemeConstantOverride("separation", 4);
		AddChild(vbox);

		// Title bar: [Actions] [Socials Page <Ctrl O>] [X]
		var titleRow = new HBoxContainer();

		var actionsLabel = new Label();
		actionsLabel.Text = "Actions";
		actionsLabel.AddThemeFontSizeOverride("font_size", 12);
		actionsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f));
		titleRow.AddChild(actionsLabel);

		var socialsLabel = new Label();
		socialsLabel.Text = "  Socials Page";
		socialsLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		socialsLabel.AddThemeFontSizeOverride("font_size", 12);
		socialsLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.4f));
		titleRow.AddChild(socialsLabel);

		var closeBtn = new Button();
		closeBtn.Text = "✕";
		closeBtn.CustomMinimumSize = new Vector2(22, 22);
		closeBtn.AddThemeFontSizeOverride("font_size", 11);
		closeBtn.Pressed += () => { Visible = false; EmitSignal(SignalName.WindowClosed); };
		titleRow.AddChild(closeBtn);

		vbox.AddChild(titleRow);

		// Page navigation: [◀] [1] [▶]
		var navRow = new HBoxContainer();
		navRow.Alignment = BoxContainer.AlignmentMode.Center;
		navRow.AddThemeConstantOverride("separation", 6);

		var prevBtn = new Button();
		prevBtn.Text = "◀";
		prevBtn.CustomMinimumSize = new Vector2(28, 24);
		prevBtn.AddThemeFontSizeOverride("font_size", 10);
		prevBtn.Pressed += () => ChangePage(-1);
		navRow.AddChild(prevBtn);

		_pageLabel = new Label();
		_pageLabel.Text = "1";
		_pageLabel.CustomMinimumSize = new Vector2(20, 0);
		_pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_pageLabel.AddThemeFontSizeOverride("font_size", 14);
		_pageLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
		navRow.AddChild(_pageLabel);

		var nextBtn = new Button();
		nextBtn.Text = "▶";
		nextBtn.CustomMinimumSize = new Vector2(28, 24);
		nextBtn.AddThemeFontSizeOverride("font_size", 10);
		nextBtn.Pressed += () => ChangePage(1);
		navRow.AddChild(nextBtn);

		vbox.AddChild(navRow);

		// Social buttons grid: 2 columns × 6 rows = 12 per page
		var grid = new GridContainer();
		grid.Columns = 2;
		grid.AddThemeConstantOverride("h_separation", 4);
		grid.AddThemeConstantOverride("v_separation", 3);
		grid.SizeFlagsVertical = SizeFlags.ExpandFill;

		for (int i = 0; i < 12; i++)
		{
			int slotIndex = i;
			var btn = new Button();
			btn.CustomMinimumSize = new Vector2(112, 28);
			btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			btn.ClipText = true;
			btn.AddThemeFontSizeOverride("font_size", 11);
			btn.Text = "";

			var normalStyle = new StyleBoxFlat();
			normalStyle.BgColor = new Color(0.08f, 0.08f, 0.1f, 0.85f);
			normalStyle.BorderWidthLeft = 1; normalStyle.BorderWidthTop = 1;
			normalStyle.BorderWidthRight = 1; normalStyle.BorderWidthBottom = 1;
			normalStyle.BorderColor = new Color(0.35f, 0.3f, 0.15f, 0.6f);
			normalStyle.CornerRadiusTopLeft = 2; normalStyle.CornerRadiusTopRight = 2;
			normalStyle.CornerRadiusBottomLeft = 2; normalStyle.CornerRadiusBottomRight = 2;
			btn.AddThemeStyleboxOverride("normal", normalStyle);

			var hoverStyle = new StyleBoxFlat();
			hoverStyle.BgColor = new Color(0.15f, 0.15f, 0.18f, 0.9f);
			hoverStyle.BorderWidthLeft = 1; hoverStyle.BorderWidthTop = 1;
			hoverStyle.BorderWidthRight = 1; hoverStyle.BorderWidthBottom = 1;
			hoverStyle.BorderColor = new Color(0.7f, 0.6f, 0.3f, 0.9f);
			btn.AddThemeStyleboxOverride("hover", hoverStyle);

			// Right-click to edit, left-click to start drag
			btn.GuiInput += (ev) => OnSocialButtonInput(ev, slotIndex);

			grid.AddChild(btn);
			_socialButtons[i] = btn;
		}

		vbox.AddChild(grid);

		// Build the edit dialog (hidden by default)
		_editDialog = new EditSocialDialog();
		_editDialog.Visible = false;
		_editDialog.ZIndex = 300;
		_editDialog.SocialAccepted += OnSocialEdited;
		AddChild(_editDialog);
	}

	// ── Page Navigation ─────────────────────────────────────────────

	private void ChangePage(int delta)
	{
		_currentPage = (_currentPage + delta + SocialManager.PAGES) % SocialManager.PAGES;
		_pageLabel.Text = (_currentPage + 1).ToString();
		RefreshButtons();
	}

	// ── Refresh ─────────────────────────────────────────────────────

	public void RefreshButtons()
	{
		if (_socialManager == null) return;

		for (int i = 0; i < 12; i++)
		{
			int globalIndex = _currentPage * SocialManager.PER_PAGE + i;
			if (globalIndex >= SocialManager.TOTAL) break;

			var social = _socialManager.Socials[globalIndex];
			var btn = _socialButtons[i];

			if (social.IsEmpty)
			{
				btn.Text = "";
				btn.TooltipText = "Empty — Right-click to create";
				btn.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
			}
			else
			{
				btn.Text = social.Name;
				btn.TooltipText = $"{social.Name}\n{string.Join("\n", social.Lines)}";

				// Apply the social's color
				int colorIdx = Math.Clamp(social.Color, 0, 19);
				btn.AddThemeColorOverride("font_color", SocialManager.EQColors[colorIdx]);
			}
		}
	}

	// ── Interaction ─────────────────────────────────────────────────

	private void OnSocialButtonInput(InputEvent ev, int slotIndex)
	{
		if (ev is InputEventMouseButton mb && mb.Pressed)
		{
			int globalIndex = _currentPage * SocialManager.PER_PAGE + slotIndex;

			if (mb.ButtonIndex == MouseButton.Right)
			{
				// Right-click → open Edit Social dialog
				if (_socialManager != null && globalIndex < SocialManager.TOTAL)
				{
					_editDialog.OpenForSocial(globalIndex, _socialManager.Socials[globalIndex]);
				}
			}
			else if (mb.ButtonIndex == MouseButton.Left)
			{
				// Left-click → start drag for dropping onto hotbar
				if (_socialManager != null && globalIndex < SocialManager.TOTAL)
				{
					var social = _socialManager.Socials[globalIndex];
					if (!social.IsEmpty)
					{
						EmitSignal(SignalName.SocialDragStarted, globalIndex, social.Name, social.Color);
					}
				}
			}
		}
	}

	private void OnSocialEdited(int socialIndex, string name, int color,
		string line0, string line1, string line2, string line3, string line4)
	{
		if (_socialManager == null || socialIndex < 0 || socialIndex >= SocialManager.TOTAL) return;

		var social = _socialManager.Socials[socialIndex];
		social.Name = name;
		social.Color = color;
		social.Lines[0] = line0;
		social.Lines[1] = line1;
		social.Lines[2] = line2;
		social.Lines[3] = line3;
		social.Lines[4] = line4;

		RefreshButtons();
	}

	// ── Drag support (window repositioning) ─────────────────────────

	private bool _windowDragging = false;
	private Vector2 _windowDragOffset;

	public override void _GuiInput(InputEvent ev)
	{
		if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				_windowDragging = true;
				_windowDragOffset = GetGlobalMousePosition() - GlobalPosition;
			}
			else
			{
				_windowDragging = false;
			}
		}
		else if (ev is InputEventMouseMotion && _windowDragging)
		{
			GlobalPosition = GetGlobalMousePosition() - _windowDragOffset;
		}
	}
}
