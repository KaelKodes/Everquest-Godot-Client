using Godot;
using System;

/// <summary>
/// Premium Audio Player window — shows Now Playing info, Music/SFX volume sliders,
/// mute toggles, and a subtle animated peak-meter visualizer.
/// Built programmatically in the EQ classic gold-and-dark UI style.
/// </summary>
public partial class AudioPlayerWindow : Panel
{
    // ── References ──
    private ZoneMusicPlayer _musicPlayer;

    // ── UI Elements ──
    private Label _titleLabel;
    private Label _trackLabel;
    private Label _statusLabel;
    private HSlider _musicSlider;
    private HSlider _sfxSlider;
    private Label _musicVolLabel;
    private Label _sfxVolLabel;
    private CheckBox _musicMuteCheck;
    private CheckBox _sfxMuteCheck;

    private HSlider _ambienceSlider;
    private Label _ambienceVolLabel;
    private CheckBox _ambienceMuteCheck;

    // ── Visualizer ──
    private ColorRect[] _vizBars = new ColorRect[16];
    private float[] _vizValues = new float[16];
    private float _vizTimer = 0f;
    private bool _vizActive = false;
    private static readonly Random _vizRng = new Random();

    // ── Drag State ──
    private bool _dragging = false;
    private Vector2 _dragOffset;

    public void Initialize(ZoneMusicPlayer musicPlayer)
    {
        _musicPlayer = musicPlayer;
        BuildUI();

        if (_musicPlayer != null)
        {
            _musicPlayer.MusicChanged += OnMusicChanged;
            // Sync initial state
            OnMusicChanged(_musicPlayer.CurrentTrackName);
            _musicSlider.Value = _musicPlayer.GetVolume() * 100;
            _sfxSlider.Value = _musicPlayer.GetSfxVolume() * 100;
            _ambienceSlider.Value = _musicPlayer.GetAmbienceVolume() * 100;
        }
    }

    public override void _ExitTree()
    {
        if (_musicPlayer != null && GodotObject.IsInstanceValid(_musicPlayer))
            _musicPlayer.MusicChanged -= OnMusicChanged;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        // Animate visualizer bars
        _vizTimer += (float)delta;
        if (_vizTimer > 0.06f) // ~16 fps animation
        {
            _vizTimer = 0f;
            UpdateVisualizer();
        }
    }

    // ── Build the entire UI ──────────────────────────────────────────

    private void BuildUI()
    {
        // Panel sizing
        CustomMinimumSize = new Vector2(280, 360);
        Size = new Vector2(280, 360);

        // Panel style — dark glass with gold border
        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.95f);
        panelStyle.BorderWidthLeft = 2; panelStyle.BorderWidthTop = 2;
        panelStyle.BorderWidthRight = 2; panelStyle.BorderWidthBottom = 2;
        panelStyle.BorderColor = new Color(0.6f, 0.5f, 0.2f, 1.0f);
        panelStyle.CornerRadiusTopLeft = 4; panelStyle.CornerRadiusTopRight = 4;
        panelStyle.CornerRadiusBottomLeft = 4; panelStyle.CornerRadiusBottomRight = 4;
        panelStyle.ShadowColor = new Color(0, 0, 0, 0.5f);
        panelStyle.ShadowSize = 6;
        AddThemeStyleboxOverride("panel", panelStyle);
        MouseFilter = MouseFilterEnum.Stop;

        // Main VBox
        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.OffsetLeft = 12; vbox.OffsetRight = -12;
        vbox.OffsetTop = 8; vbox.OffsetBottom = -8;
        vbox.AddThemeConstantOverride("separation", 6);
        AddChild(vbox);

        // ── Title Row ──
        var titleRow = new HBoxContainer();
        _titleLabel = new Label();
        _titleLabel.Text = "♫  Audio";
        _titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.4f));
        titleRow.AddChild(_titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "✕";
        closeBtn.CustomMinimumSize = new Vector2(22, 22);
        closeBtn.AddThemeFontSizeOverride("font_size", 11);
        closeBtn.Pressed += () => Visible = false;
        titleRow.AddChild(closeBtn);
        vbox.AddChild(titleRow);

        // ── Separator ──
        vbox.AddChild(new HSeparator());

        // ── Now Playing Section ──
        var nowPlayingLabel = new Label();
        nowPlayingLabel.Text = "NOW PLAYING";
        nowPlayingLabel.AddThemeFontSizeOverride("font_size", 9);
        nowPlayingLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.5f, 0.35f));
        nowPlayingLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(nowPlayingLabel);

        _trackLabel = new Label();
        _trackLabel.Text = "No Track";
        _trackLabel.AddThemeFontSizeOverride("font_size", 13);
        _trackLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.75f));
        _trackLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _trackLabel.ClipText = true;
        vbox.AddChild(_trackLabel);

        _statusLabel = new Label();
        _statusLabel.Text = "⏸  Stopped";
        _statusLabel.AddThemeFontSizeOverride("font_size", 10);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.6f, 0.5f));
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_statusLabel);

        // ── Visualizer ──
        var vizContainer = new HBoxContainer();
        vizContainer.CustomMinimumSize = new Vector2(0, 28);
        vizContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vizContainer.AddThemeConstantOverride("separation", 2);
        vizContainer.Alignment = BoxContainer.AlignmentMode.Center;

        for (int i = 0; i < 16; i++)
        {
            _vizBars[i] = new ColorRect();
            _vizBars[i].CustomMinimumSize = new Vector2(12, 4);
            _vizBars[i].SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
            _vizBars[i].Color = GetVizBarColor(i);
            _vizValues[i] = 0;
            vizContainer.AddChild(_vizBars[i]);
        }
        vbox.AddChild(vizContainer);

        // ── Separator ──
        vbox.AddChild(new HSeparator());

        // ── Music Volume ──
        vbox.AddChild(BuildVolumeRow("Music", 80, out _musicSlider, out _musicVolLabel, out _musicMuteCheck,
            (val) =>
            {
                _musicPlayer?.SetVolume((float)val / 100f);
                _musicVolLabel.Text = $"{val:F0}%";
            },
            (muted) =>
            {
                _musicPlayer?.SetMusicMuted(muted);
            }
        ));

        // ── SFX Volume ──
        vbox.AddChild(BuildVolumeRow("SFX", 80, out _sfxSlider, out _sfxVolLabel, out _sfxMuteCheck,
            (val) =>
            {
                _musicPlayer?.SetSfxVolume((float)val / 100f);
                _sfxVolLabel.Text = $"{val:F0}%";
            },
            (muted) =>
            {
                _musicPlayer?.SetSfxMuted(muted);
            }
        ));

        // ── Ambience Volume ──
        vbox.AddChild(BuildVolumeRow("Ambience", 80, out _ambienceSlider, out _ambienceVolLabel, out _ambienceMuteCheck,
            (val) =>
            {
                _musicPlayer?.SetAmbienceVolume((float)val / 100f);
                _ambienceVolLabel.Text = $"{val:F0}%";
            },
            (muted) =>
            {
                _musicPlayer?.SetAmbienceMuted(muted);
            }
        ));

        // ── Make panel draggable ──
        GuiInput += (ev) =>
        {
            if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed) { _dragging = true; _dragOffset = mb.GlobalPosition - GlobalPosition; }
                else _dragging = false;
            }
            else if (ev is InputEventMouseMotion mm && _dragging)
            {
                GlobalPosition = mm.GlobalPosition - _dragOffset;
            }
        };
    }

    // ── Volume Row Builder ───────────────────────────────────────────

    private VBoxContainer BuildVolumeRow(string label, int defaultVal,
        out HSlider slider, out Label valLabel, out CheckBox muteCheck,
        Godot.Range.ValueChangedEventHandler onValueChanged, Action<bool> onMuteToggled)
    {
        var rowBox = new VBoxContainer();
        rowBox.AddThemeConstantOverride("separation", 2);

        // Label row (Name + Value + Mute)
        var labelRow = new HBoxContainer();
        labelRow.AddThemeConstantOverride("separation", 6);

        var nameLabel = new Label();
        nameLabel.Text = label;
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameLabel.AddThemeFontSizeOverride("font_size", 11);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.6f));
        labelRow.AddChild(nameLabel);

        valLabel = new Label();
        valLabel.Text = $"{defaultVal}%";
        valLabel.CustomMinimumSize = new Vector2(36, 0);
        valLabel.AddThemeFontSizeOverride("font_size", 10);
        valLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f));
        valLabel.HorizontalAlignment = HorizontalAlignment.Right;
        labelRow.AddChild(valLabel);

        muteCheck = new CheckBox();
        muteCheck.Text = "Mute";
        muteCheck.ButtonPressed = false;
        muteCheck.AddThemeFontSizeOverride("font_size", 9);
        muteCheck.AddThemeColorOverride("font_color", new Color(0.6f, 0.5f, 0.4f));
        muteCheck.Toggled += (toggled) => onMuteToggled(toggled);
        labelRow.AddChild(muteCheck);

        rowBox.AddChild(labelRow);

        // Slider
        slider = new HSlider();
        slider.MinValue = 0;
        slider.MaxValue = 100;
        slider.Value = defaultVal;
        slider.Step = 1;
        slider.CustomMinimumSize = new Vector2(0, 18);

        // Gold-tinted slider grabber style
        var grabberStyle = new StyleBoxFlat();
        grabberStyle.BgColor = new Color(0.85f, 0.7f, 0.3f, 1.0f);
        grabberStyle.CornerRadiusTopLeft = 3; grabberStyle.CornerRadiusTopRight = 3;
        grabberStyle.CornerRadiusBottomLeft = 3; grabberStyle.CornerRadiusBottomRight = 3;
        grabberStyle.ContentMarginLeft = 6; grabberStyle.ContentMarginRight = 6;
        grabberStyle.ContentMarginTop = 6; grabberStyle.ContentMarginBottom = 6;
        slider.AddThemeStyleboxOverride("grabber_area", grabberStyle);
        slider.AddThemeStyleboxOverride("grabber_area_highlight", grabberStyle);

        slider.ValueChanged += onValueChanged;
        rowBox.AddChild(slider);

        return rowBox;
    }

    // ── Visualizer Animation ─────────────────────────────────────────

    private void UpdateVisualizer()
    {
        bool isPlaying = _musicPlayer != null && _musicPlayer.IsPlaying;

        for (int i = 0; i < 16; i++)
        {
            float target;
            if (isPlaying)
            {
                // Generate pseudo-random peaks — center bars taller, edges shorter
                float centerBias = 1.0f - Mathf.Abs((i - 7.5f) / 8f);
                target = (float)(_vizRng.NextDouble() * 0.6 + 0.2) * centerBias;
                target = Mathf.Clamp(target * 28f, 3f, 28f);
            }
            else
            {
                target = 2f; // Flat baseline when stopped
            }

            // Smooth decay — bars drop slowly, rise fast
            if (target > _vizValues[i])
                _vizValues[i] = Mathf.Lerp(_vizValues[i], target, 0.7f);
            else
                _vizValues[i] = Mathf.Lerp(_vizValues[i], target, 0.3f);

            _vizBars[i].CustomMinimumSize = new Vector2(12, _vizValues[i]);
            _vizBars[i].Color = GetVizBarColor(i, _vizValues[i] / 28f);
        }
    }

    private Color GetVizBarColor(int index, float intensity = 0.3f)
    {
        // Gold to amber gradient based on position and intensity
        float hue = 0.08f + (index / 16f) * 0.04f; // 0.08 to 0.12 — warm gold range
        float sat = 0.6f + intensity * 0.3f;
        float val = 0.3f + intensity * 0.6f;
        return Color.FromHsv(hue, sat, val, 0.9f);
    }

    // ── Signal Handlers ──────────────────────────────────────────────

    private void OnMusicChanged(string trackName)
    {
        if (!IsInstanceValid(this)) return;

        if (string.IsNullOrEmpty(trackName))
        {
            _trackLabel.Text = "No Track";
            _statusLabel.Text = "⏸  Stopped";
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.45f, 0.35f));
        }
        else
        {
            // Format track name nicely: "qeynos2" → "Qeynos 2"
            string formatted = FormatTrackName(trackName);
            _trackLabel.Text = formatted;
            _statusLabel.Text = "▶  Playing";
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.75f, 0.4f));
        }
    }

    private string FormatTrackName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        // Capitalize first letter, insert space before trailing digits
        string result = "";
        bool prevWasLetter = false;
        foreach (char c in raw)
        {
            if (char.IsDigit(c) && prevWasLetter)
                result += " ";
            result += result.Length == 0 ? char.ToUpper(c) : c;
            prevWasLetter = char.IsLetter(c);
        }
        return result;
    }
}
