using Godot;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public partial class LootWindow : Godot.Window
{
    private GridContainer _grid;
    public string CorpseId { get; private set; }
    public Button[] Slots { get; private set; }

    public void Init(string corpseId, string corpseName, List<LootItemData> items)
    {
        CorpseId = corpseId;
        Title = "Loot: " + corpseName;
        
        int bagSlots = Mathf.Max(8, items != null ? items.Count : 0); // Default to at least 8 slots visually
        Size = new Vector2I(180, 40 + ((bagSlots + 1) / 2) * 50);
        MinSize = new Vector2I(120, 100);
        Exclusive = false;
        Unresizable = true;
        Transient = true;
        AlwaysOnTop = true;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        panel.AddChild(margin);

        _grid = new GridContainer();
        _grid.Columns = 2;
        _grid.AddThemeConstantOverride("h_separation", 8);
        _grid.AddThemeConstantOverride("v_separation", 8);
        margin.AddChild(_grid);

        UpdateItems(items);

        CloseRequested += () => {
            if (GodotObject.IsInstanceValid(this))
                QueueFree();
        };
    }

    public void UpdateItems(List<LootItemData> items)
    {
        if (!GodotObject.IsInstanceValid(this)) return;

        if (items == null || items.Count == 0)
        {
            QueueFree();
            return;
        }

        if (_grid == null) return;

        foreach (var child in _grid.GetChildren())
        {
            child.QueueFree();
        }

        int bagSlots = Mathf.Max(8, items.Count);
        Slots = new Button[bagSlots];

        for (int i = 0; i < bagSlots; i++)
        {
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(40, 40);
            btn.ExpandIcon = true;
            btn.IconAlignment = HorizontalAlignment.Center;
            
            if (i < items.Count)
            {
                var item = items[i];
                if (IconManager.Instance != null)
                {
                    btn.Icon = IconManager.Instance.GetItemIcon(item.Icon);
                }
                btn.TooltipText = item.Name;
                int lootIdx = item.LootIndex;
                btn.Pressed += () => {
                    if (GodotObject.IsInstanceValid(MainUI.Instance))
                        MainUI.Instance.TakeLootItem(CorpseId, lootIdx);
                };
            }
            else
            {
                btn.Disabled = true;
            }

            _grid.AddChild(btn);
            Slots[i] = btn;
        }
    }
}

public class LootItemData
{
    [JsonPropertyName("lootIndex")]
    public int LootIndex { get; set; }
    
    [JsonPropertyName("itemKey")]
    public string ItemKey { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    [JsonPropertyName("icon")]
    public int Icon { get; set; }
    
    [JsonPropertyName("qty")]
    public int Qty { get; set; }
}
