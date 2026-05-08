using Godot;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;

public partial class SkillsWindow : DraggablePanel
{
    private VBoxContainer _skillList;
    private Dictionary<string, (int value, int max)> _currentSkills = new Dictionary<string, (int value, int max)>();

    public override void _Ready()
    {
        base._Ready();
        _skillList = GetNode<VBoxContainer>("VBox/Scroll/SkillList");
    }

    public void UpdateSkillsExtended(JsonElement skillDataObj)
    {
        _currentSkills.Clear();
        foreach (var prop in skillDataObj.EnumerateObject())
        {
            int val = prop.Value.GetProperty("value").GetInt32();
            int max = prop.Value.GetProperty("max").GetInt32();
            _currentSkills[prop.Name] = (val, max);
        }
        RefreshUI();
    }

    public void UpdateSkills(JsonElement skillsObj, int playerLevel)
    {
        _currentSkills.Clear();
        foreach (var prop in skillsObj.EnumerateObject())
        {
            if (prop.Value.TryGetInt32(out int val))
            {
                // Fallback to legacy formula for max if extended data is missing
                _currentSkills[prop.Name] = (val, (playerLevel * 5) + 5);
            }
        }
        RefreshUI();
    }

    public void RefreshUI()
    {
        if (_skillList == null) return;

        // Clear existing children
        foreach (Node child in _skillList.GetChildren())
        {
            child.QueueFree();
        }

        // Sort skills alphabetically by formatted name
        var sortedSkills = _currentSkills.OrderBy(kvp => FormatSkillName(kvp.Key)).ToList();

        foreach (var kvp in sortedSkills)
        {
            string rawKey = kvp.Key;
            int currentVal = kvp.Value.value;
            int displayMax = kvp.Value.max;

            var row = new HBoxContainer();
            row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddThemeConstantOverride("separation", 10);

            var nameLabel = new Label();
            nameLabel.Text = FormatSkillName(rawKey);
            nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            nameLabel.AddThemeFontSizeOverride("font_size", 12);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.6f)); // EQ Gold

            var valueLabel = new Label();
            valueLabel.Text = $"{currentVal} / {displayMax}";
            valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
            valueLabel.CustomMinimumSize = new Vector2(60, 0);
            valueLabel.AddThemeFontSizeOverride("font_size", 12);

            row.AddChild(nameLabel);
            row.AddChild(valueLabel);

            _skillList.AddChild(row);
        }
    }

    private string FormatSkillName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        string[] parts = raw.Split('_');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
                parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1).ToLower();
        }
        return string.Join(" ", parts);
    }
}
