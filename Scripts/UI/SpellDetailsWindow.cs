using Godot;
using System.Text.Json;

public partial class SpellDetailsWindow : PanelContainer
{
    private Label _titleLabel;
    private RichTextLabel _detailsText;
    private TextureRect _spellIcon;
    private Button _closeButton;

    private bool _isDragging = false;
    private Vector2 _dragOffset;

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(300, 240);
        ZIndex = 300;
        MouseFilter = MouseFilterEnum.Stop;

        var sb = new StyleBoxFlat();
        sb.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        sb.BorderColor = new Color(0.8f, 0.7f, 0.2f, 1f); // Golden border
        sb.SetBorderWidthAll(2);
        sb.SetCornerRadiusAll(4);
        AddThemeStyleboxOverride("panel", sb);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 0);
        AddChild(vbox);

        // Title bar (draggable)
        var titleBar = new ColorRect();
        titleBar.Color = new Color(0.2f, 0.2f, 0.25f, 1f);
        titleBar.CustomMinimumSize = new Vector2(0, 24);
        titleBar.MouseFilter = MouseFilterEnum.Stop;
        titleBar.GuiInput += OnTitleBarInput;
        vbox.AddChild(titleBar);

        var titleHBox = new HBoxContainer();
        titleHBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        titleBar.AddChild(titleHBox);

        _titleLabel = new Label();
        _titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.VerticalAlignment = VerticalAlignment.Center;
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        titleHBox.AddChild(_titleLabel);

        _closeButton = new Button();
        _closeButton.Text = "X";
        _closeButton.CustomMinimumSize = new Vector2(24, 24);
        
        // Basic red styling for close button
        var closeSb = new StyleBoxFlat();
        closeSb.BgColor = new Color(0.4f, 0.1f, 0.1f, 1f);
        _closeButton.AddThemeStyleboxOverride("normal", closeSb);
        _closeButton.Pressed += () => Visible = false;
        titleHBox.AddChild(_closeButton);

        var marginContainer = new MarginContainer();
        marginContainer.AddThemeConstantOverride("margin_left", 8);
        marginContainer.AddThemeConstantOverride("margin_right", 8);
        marginContainer.AddThemeConstantOverride("margin_top", 8);
        marginContainer.AddThemeConstantOverride("margin_bottom", 8);
        marginContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(marginContainer);

        var hbox = new HBoxContainer();
        marginContainer.AddChild(hbox);

        _detailsText = new RichTextLabel();
        _detailsText.BbcodeEnabled = true;
        _detailsText.FitContent = true;
        _detailsText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _detailsText.SizeFlagsVertical = SizeFlags.ExpandFill;
        hbox.AddChild(_detailsText);

        _spellIcon = new TextureRect();
        _spellIcon.CustomMinimumSize = new Vector2(40, 40);
        _spellIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _spellIcon.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        _spellIcon.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        hbox.AddChild(_spellIcon);
    }

    public void ShowSpell(JsonElement spell, Vector2 pos)
    {
        string name = spell.TryGetProperty("name", out var n) ? n.GetString() : "Unknown Spell";
        _titleLabel.Text = $"Spell: {name}";

        int level = spell.TryGetProperty("level", out var l) ? l.GetInt32() : 1;
        string skill = spell.TryGetProperty("skill", out var sk) ? sk.GetString() : "Unknown";
        int mana = spell.TryGetProperty("manaCost", out var m) ? m.GetInt32() : 0;
        float castTime = spell.TryGetProperty("castTime", out var ct) ? ct.GetSingle() : 0f;
        string target = spell.TryGetProperty("target", out var tg) ? tg.GetString() : "self";
        int range = spell.TryGetProperty("range", out var r) ? r.GetInt32() : 0;
        int duration = spell.TryGetProperty("duration", out var d) ? d.GetInt32() : 0;
        string desc = spell.TryGetProperty("description", out var ds) ? ds.GetString() : "";
        string spellLine = spell.TryGetProperty("spellLine", out var sl) ? sl.GetString() : "";
        bool reflectable = spell.TryGetProperty("reflectable", out var rf) ? rf.GetBoolean() : false;

        _detailsText.Clear();
        _detailsText.AppendText($"[color=#dddddd]Level: {level}[/color]\n");
        _detailsText.AppendText($"[color=#dddddd]Skill: {skill}[/color]\n");
        
        if (mana > 0)
            _detailsText.AppendText($"[color=#dddddd]Mana: {mana}[/color]\n");
        else
            _detailsText.AppendText($"[color=#dddddd]Mana: 0[/color]\n");

        _detailsText.AppendText($"[color=#dddddd]Cast: {castTime:F1} sec[/color]\n");
        
        if (!string.IsNullOrEmpty(spellLine))
            _detailsText.AppendText($"[color=#dddddd]Spell Line: {spellLine}[/color]\n");
            
        _detailsText.AppendText($"[color=#dddddd]Target: {target}[/color]\n");
        _detailsText.AppendText($"[color=#dddddd]Reflectable: {(reflectable ? "Yes" : "No")}[/color]\n");
        
        if (range > 0)
            _detailsText.AppendText($"[color=#dddddd]Range: {range}[/color]\n");
        
        string durStr = duration > 0 ? $"{(duration/60)}:{(duration%60):D2}" : "Instant";
        _detailsText.AppendText($"[color=#dddddd]Duration: {durStr}[/color]\n\n");
        
        _detailsText.AppendText($"[color=#aaaaff]{desc}[/color]");

        int memIcon = spell.TryGetProperty("memIcon", out var ic) ? ic.GetInt32() : 0;
        var iconMgr = IconManager.Instance;
        if (iconMgr != null && memIcon > 0) {
            _spellIcon.Texture = iconMgr.GetSpellGem(memIcon);
        } else {
            _spellIcon.Texture = null;
        }

        if (!LoadPosition()) {
            GlobalPosition = pos;
        }
        MoveToFront();
        Visible = true;
    }

    private void OnTitleBarInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _isDragging = true;
                _dragOffset = GetGlobalMousePosition() - GlobalPosition;
            }
            else if (_isDragging)
            {
                _isDragging = false;
                SavePosition();
            }
        }
        else if (ev is InputEventMouseMotion && _isDragging)
        {
            GlobalPosition = GetGlobalMousePosition() - _dragOffset;
        }
    }

    private void SavePosition()
    {
        try {
            var dict = new Godot.Collections.Dictionary { ["x"] = GlobalPosition.X, ["y"] = GlobalPosition.Y };
            using var file = FileAccess.Open("user://spell_window_pos.json", FileAccess.ModeFlags.Write);
            if (file != null) file.StoreString(Json.Stringify(dict));
        } catch {}
    }

    private bool LoadPosition()
    {
        try {
            if (!FileAccess.FileExists("user://spell_window_pos.json")) return false;
            using var file = FileAccess.Open("user://spell_window_pos.json", FileAccess.ModeFlags.Read);
            if (file != null) {
                var data = Json.ParseString(file.GetAsText()).AsGodotDictionary();
                GlobalPosition = new Vector2(data["x"].AsSingle(), data["y"].AsSingle());
                return true;
            }
        } catch {}
        return false;
    }
}
