using Godot;
using Godot.Collections;

public partial class CopyLayoutWindow : Panel
{
    private ItemList _layoutList;
    private CheckBox _chkHotButtons;
    private CheckBox _chkWindows;
    private CheckBox _chkSocials;
    private Button _btnCopy;
    private Button _btnClose;
    private Array<string> _availableFiles = new Array<string>();

    public override void _Ready()
    {
        Name = "CopyLayoutWindow";
        CustomMinimumSize = new Vector2(440, 300);
        Size = new Vector2(440, 300);

        var screenSize = GetViewport().GetVisibleRect().Size;
        GlobalPosition = (screenSize - Size) / 2;

        // EQ-style dark panel
        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.95f);
        panelStyle.BorderWidthLeft = 2; panelStyle.BorderWidthTop = 2;
        panelStyle.BorderWidthRight = 2; panelStyle.BorderWidthBottom = 2;
        panelStyle.BorderColor = new Color(0.6f, 0.5f, 0.2f, 1.0f);
        panelStyle.CornerRadiusTopLeft = 3; panelStyle.CornerRadiusTopRight = 3;
        panelStyle.CornerRadiusBottomLeft = 3; panelStyle.CornerRadiusBottomRight = 3;
        AddThemeStyleboxOverride("panel", panelStyle);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.OffsetLeft = 8; vbox.OffsetRight = -8;
        vbox.OffsetTop = 8; vbox.OffsetBottom = -8;
        AddChild(vbox);

        // Title row
        var titleRow = new HBoxContainer();
        var titleLbl = new Label { Text = "Copy Layout", HorizontalAlignment = HorizontalAlignment.Center };
        titleLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleRow.AddChild(titleLbl);

        var btnAdd = new Button { Text = "Add", CustomMinimumSize = new Vector2(45, 22) };
        btnAdd.AddThemeFontSizeOverride("font_size", 12);
        btnAdd.Pressed += () => {
            if (MainUI.Instance != null) MainUI.Instance.SaveFullLayout();
            RefreshLayouts();
        };
        titleRow.AddChild(btnAdd);

        _btnClose = new Button { Text = "X", CustomMinimumSize = new Vector2(22, 22) };
        _btnClose.AddThemeFontSizeOverride("font_size", 11);
        _btnClose.Pressed += () => Visible = false;
        titleRow.AddChild(_btnClose);
        vbox.AddChild(titleRow);

        vbox.AddChild(new Label { Text = "Source Layouts:" });

        _layoutList = new ItemList();
        _layoutList.SizeFlagsVertical = SizeFlags.ExpandFill;
        _layoutList.SelectMode = ItemList.SelectModeEnum.Single;
        vbox.AddChild(_layoutList);

        var checksRow = new HBoxContainer();
        _chkHotButtons = new CheckBox { Text = "Hot Buttons", ButtonPressed = true };
        _chkWindows = new CheckBox { Text = "Loadouts (Windows)", ButtonPressed = true };
        _chkSocials = new CheckBox { Text = "Socials", ButtonPressed = true };
        
        var vChecks1 = new VBoxContainer();
        vChecks1.AddChild(_chkHotButtons);
        vChecks1.AddChild(_chkSocials);
        checksRow.AddChild(vChecks1);

        var vChecks2 = new VBoxContainer();
        vChecks2.AddChild(_chkWindows);
        checksRow.AddChild(vChecks2);

        _btnCopy = new Button { Text = "Copy", CustomMinimumSize = new Vector2(80, 30) };
        _btnCopy.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        _btnCopy.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        _btnCopy.Pressed += OnCopyPressed;
        checksRow.AddChild(_btnCopy);

        vbox.AddChild(checksRow);

        // Dragging
        bool dragging = false;
        Vector2 dragOffset = Vector2.Zero;
        GuiInput += (ev) =>
        {
            if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) { dragging = true; dragOffset = mb.GlobalPosition - GlobalPosition; }
                else {
                    dragging = false;
                    if (MainUI.Instance != null) {
                        MainUI.Instance.SaveWindowPositions();
                    }
                }
            }
            else if (ev is InputEventMouseMotion mm && dragging)
            {
                GlobalPosition = mm.GlobalPosition - dragOffset;
            }
        };

        VisibilityChanged += OnVisibilityChanged;
    }

    private void OnVisibilityChanged()
    {
        if (Visible)
        {
            RefreshLayouts();
        }
    }

    private void RefreshLayouts()
    {
        _layoutList.Clear();
        _availableFiles = UILayoutManager.GetAvailableLayouts();

        foreach (string file in _availableFiles)
        {
            string displayName = file.Replace("UI_", "").Replace(".json", "");
            _layoutList.AddItem(displayName);
        }
    }

    private void OnCopyPressed()
    {
        var selected = _layoutList.GetSelectedItems();
        if (selected.Length == 0) return;

        string selectedFile = _availableFiles[selected[0]];
        var sourceData = UILayoutManager.LoadLayoutFromFile(selectedFile);

        if (_chkHotButtons.ButtonPressed && sourceData.ContainsKey("Hotbars"))
        {
            UILayoutManager.SetSection("Hotbars", sourceData["Hotbars"].AsGodotDictionary());
        }

        if (_chkWindows.ButtonPressed && sourceData.ContainsKey("Windows"))
        {
            UILayoutManager.SetSection("Windows", sourceData["Windows"].AsGodotDictionary());
        }

        if (_chkSocials.ButtonPressed && sourceData.ContainsKey("Socials"))
        {
            UILayoutManager.SetSection("Socials", sourceData["Socials"].AsGodotDictionary());
        }

        // Save immediately for the current character
        UILayoutManager.SaveLayout(GameState.CharacterName);

        // Notify MainUI to reload positions
        if (MainUI.Instance != null) {
            MainUI.Instance.ReloadUILayout();
            if (MainUI.Instance.HotbarManager != null) {
                MainUI.Instance.HotbarManager.ReloadHotbars();
            }
        }

        Visible = false;
    }
}
