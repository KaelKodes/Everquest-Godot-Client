using Godot;
using System;
using System.Text.Json;

public partial class MainUI : Control
{
    private Window _hireStudentWindow;
    private LineEdit _hireNameInput;
    private OptionButton _hireRaceSelect;
    private OptionButton _hireClassSelect;
    private SpinBox _hireLevelSpinner;
    private Label _hireCostLabel;
    private Button _hireOkBtn;
    private Button _hireCancelBtn;

    private int _hireMaxLevel = 1;
    private int _hireCurrentLevel = 1;

    // Helper map to recreate string names, similar to server
    private string[] _raceNames = { "Unknown", "Human", "Barbarian", "Erudite", "Wood Elf", "High Elf", "Dark Elf", "Half Elf", "Dwarf", "Troll", "Ogre", "Halfling", "Gnome" };
    private string[] _classNames = { "Unknown", "Warrior", "Cleric", "Paladin", "Ranger", "Shadow Knight", "Druid", "Monk", "Bard", "Rogue", "Shaman", "Necromancer", "Wizard", "Magician", "Enchanter", "Beastlord", "Berserker" };

    private void SetupHireStudentUI()
    {
        _hireStudentWindow = new Window();
        _hireStudentWindow.Title = "Hire Student";
        _hireStudentWindow.Size = new Vector2I(300, 250);
        _hireStudentWindow.Exclusive = true;
        _hireStudentWindow.CloseRequested += () => _hireStudentWindow.Hide();
        AddChild(_hireStudentWindow);
        _hireStudentWindow.Hide();

        var bg = new ColorRect();
        bg.Color = new Color(0.1f, 0.1f, 0.1f, 1.0f);
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _hireStudentWindow.AddChild(bg);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bg.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        margin.AddChild(vbox);

        // Name
        _hireNameInput = new LineEdit();
        _hireNameInput.PlaceholderText = "Student Name";
        _hireNameInput.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(_hireNameInput);

        // Race
        _hireRaceSelect = new OptionButton();
        _hireRaceSelect.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(_hireRaceSelect);

        // Class
        _hireClassSelect = new OptionButton();
        _hireClassSelect.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(_hireClassSelect);

        // Bottom Row (Level, Cost, Buttons)
        var bottomHBox = new HBoxContainer();
        bottomHBox.AddThemeConstantOverride("separation", 15);
        bottomHBox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(bottomHBox);

        // Level Spinner
        var lvlVbox = new VBoxContainer();
        var lvlLabel = new Label();
        lvlLabel.Text = "Lvl";
        lvlLabel.HorizontalAlignment = HorizontalAlignment.Center;
        lvlVbox.AddChild(lvlLabel);

        _hireLevelSpinner = new SpinBox();
        _hireLevelSpinner.MinValue = 1;
        _hireLevelSpinner.MaxValue = 1;
        _hireLevelSpinner.Value = 1;
        _hireLevelSpinner.Alignment = HorizontalAlignment.Center;
        _hireLevelSpinner.CustomMinimumSize = new Vector2(60, 0);
        _hireLevelSpinner.ValueChanged += OnHireLevelChanged;
        lvlVbox.AddChild(_hireLevelSpinner);
        bottomHBox.AddChild(lvlVbox);

        // Cost Box
        var costBox = new PanelContainer();
        costBox.CustomMinimumSize = new Vector2(100, 40);
        var costMargin = new MarginContainer();
        costMargin.AddThemeConstantOverride("margin_left", 5);
        costMargin.AddThemeConstantOverride("margin_right", 5);
        costBox.AddChild(costMargin);

        _hireCostLabel = new Label();
        _hireCostLabel.Text = "Cost: Free";
        _hireCostLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _hireCostLabel.VerticalAlignment = VerticalAlignment.Center;
        _hireCostLabel.AddThemeFontSizeOverride("font_size", 16);
        costMargin.AddChild(_hireCostLabel);
        bottomHBox.AddChild(costBox);

        // Buttons Box
        var btnVbox = new VBoxContainer();
        btnVbox.Alignment = BoxContainer.AlignmentMode.Center;
        bottomHBox.AddChild(btnVbox);

        _hireOkBtn = new Button();
        _hireOkBtn.Text = "OK";
        _hireOkBtn.Pressed += OnHireStudentOkPressed;
        btnVbox.AddChild(_hireOkBtn);

        _hireCancelBtn = new Button();
        _hireCancelBtn.Text = "Cancel";
        _hireCancelBtn.Pressed += () => _hireStudentWindow.Hide();
        btnVbox.AddChild(_hireCancelBtn);
    }

    private void OnHireStudentReceived(Variant data)
    {
        if (_hireStudentWindow == null)
        {
            SetupHireStudentUI();
        }

        try
        {
            var jsonStr = data.AsString();
            using JsonDocument doc = JsonDocument.Parse(jsonStr);
            JsonElement root = doc.RootElement;

            string trainerName = root.GetProperty("trainerName").GetString();
            
            // Re-populate options based on trainer capability
            _hireRaceSelect.Clear();
            if (root.TryGetProperty("validRaces", out JsonElement races))
            {
                foreach (var r in races.EnumerateArray())
                {
                    int rId = r.GetInt32();
                    string rName = (rId > 0 && rId < _raceNames.Length) ? _raceNames[rId] : "Unknown";
                    _hireRaceSelect.AddItem(rName, rId);
                }
            }

            _hireClassSelect.Clear();
            if (root.TryGetProperty("validClasses", out JsonElement classes))
            {
                foreach (var c in classes.EnumerateArray())
                {
                    int cId = c.GetInt32();
                    string cName = (cId > 0 && cId < _classNames.Length) ? _classNames[cId] : "Unknown";
                    _hireClassSelect.AddItem(cName, cId);
                }
            }

            // Set max level to the player's level. (Fallback to 1 if we don't have it).
            _hireMaxLevel = 1;
            if (root.TryGetProperty("playerLevel", out JsonElement pLevel))
            {
                int playerLevel = pLevel.GetInt32();
                if (playerLevel > 1) _hireMaxLevel = playerLevel;
            }

            _hireLevelSpinner.MaxValue = _hireMaxLevel;
            _hireLevelSpinner.Value = 1;
            _hireNameInput.Text = "";

            UpdateHireCost();

            // Center window and show
            _hireStudentWindow.PopupCentered();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[MainUI.HireStudent] Error parsing OPEN_HIRE_STUDENT: {e.Message}");
        }
    }

    private void OnHireLevelChanged(double val)
    {
        _hireCurrentLevel = (int)val;
        UpdateHireCost();
    }

    private void UpdateHireCost()
    {
        if (_hireCurrentLevel == 1)
        {
            _hireCostLabel.Text = "Free";
        }
        else
        {
            // Simple math for cost based on level (10pp per level above 1)
            int plat = (_hireCurrentLevel - 1) * 10;
            _hireCostLabel.Text = $"{plat} pp";
        }
    }

    private void OnHireStudentOkPressed()
    {
        int selectedRaceId = _hireRaceSelect.GetSelectedId();
        int selectedClassId = _hireClassSelect.GetSelectedId();
        string studentName = _hireNameInput.Text.Trim();

        if (string.IsNullOrEmpty(studentName))
        {
            _client.SendRaw($"{{\"type\": \"CHAT\", \"channel\": \"system\", \"text\": \"You must provide a name for your student.\", \"sender\": \"\"}}");
            return;
        }

        _hireStudentWindow.Hide();

        // The user requested that hitting OK pops open the Character Creation UI with these locked in.
        // We simulate that transition by firing a local event or sending a packet.
        // For now, we'll send a payload to the server indicating the "Configured Student".
        // When the Main Menu agent hooks it up, it will intercept this state.
        
        var payload = new {
            type = "HIRE_STUDENT_CONFIG",
            name = studentName,
            raceId = selectedRaceId,
            classId = selectedClassId,
            level = _hireCurrentLevel
        };

        _client.SendRaw(JsonSerializer.Serialize(payload));
    }
}
