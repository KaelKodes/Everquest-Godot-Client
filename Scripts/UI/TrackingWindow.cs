using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

public partial class TrackingWindow : Window
{
    private ItemList _targetList;
    private CheckBox _autoUpdateCheck;
    private OptionButton _sortOption;
    private OptionButton _trackPlayersOption;
    private CheckBox _trackPetsCheck;
    private CheckBox _trackMercsCheck;

    private Dictionary<string, bool> _filters = new Dictionary<string, bool>() {
        { "red", true }, { "yellow", true }, { "white", true },
        { "blue", true }, { "lightblue", true }, { "green", true }, { "gray", true }
    };

    // Stored list of raw tracking data
    private List<JsonElement> _trackedEntities = new List<JsonElement>();
    private Vector2I _lastPos;

    public override void _Ready()
    {
        Title = "Tracking Window";
        Size = new Vector2I(220, 480);
        MinSize = new Vector2I(200, 300);
        Exclusive = false;
        Unresizable = false;
        Transient = true; 
        AlwaysOnTop = true;
        WrapControls = true;
        
        LoadPosition();
        _lastPos = Position;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(panel);

        // Save position when resized
        SizeChanged += SavePosition;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 4);
        margin.AddThemeConstantOverride("margin_top", 4);
        margin.AddThemeConstantOverride("margin_right", 4);
        margin.AddThemeConstantOverride("margin_bottom", 4);
        panel.AddChild(margin);

        var vbox = new VBoxContainer();
        margin.AddChild(vbox);

        var filterLabel = new Label { Text = "Filters", HorizontalAlignment = HorizontalAlignment.Center };
        vbox.AddChild(filterLabel);

        // Filter buttons
        var filterBox = new HBoxContainer();
        filterBox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(filterBox);

        string[] filterNames = { "red", "yellow", "white", "blue", "lightblue", "green", "gray" };
        string[] filterLabels = { "RD", "YL", "WT", "BL", "LB", "GN", "GY" };
        Color[] filterColors = {
            Colors.Red, Colors.Yellow, Colors.White, Colors.Blue,
            Colors.LightBlue, Colors.Green, Colors.Gray
        };

        for (int i = 0; i < filterNames.Length; i++)
        {
            var btn = new Button { Text = filterLabels[i], ToggleMode = true, ButtonPressed = true };
            btn.AddThemeColorOverride("font_color", filterColors[i]);
            btn.AddThemeColorOverride("font_pressed_color", filterColors[i]);
            string fName = filterNames[i];
            btn.Toggled += (pressed) => {
                _filters[fName] = pressed;
                RefreshList();
            };
            filterBox.AddChild(btn);
        }

        _targetList = new ItemList();
        _targetList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _targetList.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(_targetList);

        _autoUpdateCheck = new CheckBox { Text = "Update automatically" };
        vbox.AddChild(_autoUpdateCheck);

        vbox.AddChild(new Label { Text = "Track Sort:" });
        _sortOption = new OptionButton();
        _sortOption.AddItem("normal");
        _sortOption.AddItem("distance");
        _sortOption.ItemSelected += (idx) => RefreshList();
        vbox.AddChild(_sortOption);

        vbox.AddChild(new Label { Text = "Track Players:" });
        _trackPlayersOption = new OptionButton();
        _trackPlayersOption.AddItem("on");
        _trackPlayersOption.AddItem("off");
        _trackPlayersOption.ItemSelected += (idx) => RefreshList();
        vbox.AddChild(_trackPlayersOption);

        _trackPetsCheck = new CheckBox { Text = "Track Pets" };
        vbox.AddChild(_trackPetsCheck);

        _trackMercsCheck = new CheckBox { Text = "Track Mercs" };
        vbox.AddChild(_trackMercsCheck);

        var trackBtn = new Button { Text = "Track" };
        trackBtn.Pressed += OnTrackPressed;
        vbox.AddChild(trackBtn);

        var cancelBtn = new Button { Text = "Cancel" };
        cancelBtn.Pressed += () => { QueueFree(); };
        vbox.AddChild(cancelBtn);

        CloseRequested += () => { QueueFree(); };

        // Initial request
        MainUI.Instance.GetClient().SendRaw("{\"type\": \"ABILITY\", \"ability\": \"tracking\"}");

        // Auto-update timer
        var timer = new Timer();
        timer.WaitTime = 2.0;
        timer.Autostart = true;
        timer.Timeout += () => {
            if (_autoUpdateCheck.ButtonPressed) {
                MainUI.Instance.GetClient().SendRaw("{\"type\": \"GET_TRACKING_LIST\"}");
            }
        };
        AddChild(timer);
    }

    public override void _Process(double delta)
    {
        if (Position != _lastPos)
        {
            _lastPos = Position;
            SavePosition();
        }
    }

    public void UpdateList(JsonElement targetsArray)
    {
        _trackedEntities.Clear();
        foreach (var t in targetsArray.EnumerateArray())
        {
            _trackedEntities.Add(t.Clone());
        }
        RefreshList();
    }

    private void RefreshList()
    {
        _targetList.Clear();
        
        bool sortByDist = _sortOption.Selected == 1;
        bool trackPlayers = _trackPlayersOption.Selected == 0;

        var displayList = new List<JsonElement>();

        foreach (var t in _trackedEntities)
        {
            bool isPlayer = t.GetProperty("isPlayer").GetBoolean();
            if (isPlayer && !trackPlayers) continue;

            string con = t.GetProperty("con").GetString();
            if (!_filters.GetValueOrDefault(con, true)) continue;

            displayList.Add(t);
        }

        if (sortByDist)
        {
            displayList.Sort((a, b) => a.GetProperty("dist").GetDouble().CompareTo(b.GetProperty("dist").GetDouble()));
        }

        foreach (var t in displayList)
        {
            string name = t.GetProperty("name").GetString();
            string con = t.GetProperty("con").GetString();
            
            int idx = _targetList.AddItem(name);
            _targetList.SetItemMetadata(idx, t.GetProperty("id").GetString());

            Color col = Colors.White;
            switch(con) {
                case "red": col = Colors.Red; break;
                case "yellow": col = Colors.Yellow; break;
                case "white": col = Colors.White; break;
                case "blue": col = Colors.Blue; break;
                case "lightblue": col = Colors.LightBlue; break;
                case "green": col = Colors.Green; break;
                case "gray": col = Colors.Gray; break;
            }
            _targetList.SetItemCustomFgColor(idx, col);
        }
    }

    private void OnTrackPressed()
    {
        var selected = _targetList.GetSelectedItems();
        if (selected.Length > 0)
        {
            string id = (string)_targetList.GetItemMetadata(selected[0]);
            MainUI.Instance.GetClient().SendRaw($"{{\"type\": \"SET_TRACKING_TARGET\", \"targetId\": \"{id}\"}}");
        }
        else
        {
            MainUI.Instance.GetClient().SendRaw("{\"type\": \"CLEAR_TRACKING\"}");
        }
    }

    private void SavePosition()
    {
        if (!IsInstanceValid(this)) return;
        var config = new ConfigFile();
        config.Load("user://ui_layout.cfg");
        config.SetValue("TrackingWindow", "position", Position);
        config.SetValue("TrackingWindow", "size", Size);
        config.Save("user://ui_layout.cfg");
    }

    private void LoadPosition()
    {
        var config = new ConfigFile();
        if (config.Load("user://ui_layout.cfg") == Error.Ok)
        {
            Position = (Vector2I)config.GetValue("TrackingWindow", "position", Position);
            Size = (Vector2I)config.GetValue("TrackingWindow", "size", Size);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SavePosition();
        }
        base.Dispose(disposing);
        if (disposing && IsInstanceValid(MainUI.Instance))
        {
            // Clear tracking when window closes
            MainUI.Instance.GetClient().SendRaw("{\"type\": \"CLEAR_TRACKING\"}");
            MainUI.Instance.ClearTrackingWindow();
        }
    }
}
