using Godot;
using System.Collections.Generic;

public partial class BagWindow : Godot.Window
{
    private GridContainer _grid;
    public int ParentSlotId { get; private set; }
    public int BaseBagSlot { get; private set; }
    public Button[] Slots { get; private set; }

    public void Init(int parentSlotId, int bagSlots, string bagName)
    {
        ParentSlotId = parentSlotId;
        if (parentSlotId >= 2000 && parentSlotId <= 2023) {
            BaseBagSlot = 2531 + ((parentSlotId - 2000) * 10);
        } else if (parentSlotId >= 2500 && parentSlotId <= 2507) {
            BaseBagSlot = 2511 + ((parentSlotId - 2500) * 10);
        } else {
            BaseBagSlot = 251 + ((parentSlotId - 22) * 10);
        }
        Title = bagName;
        Size = new Vector2I(180, 20 + ((bagSlots + 1) / 2) * 50);
        MinSize = new Vector2I(120, 100);
        Exclusive = false;
        Unresizable = true;

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
        _grid.Columns = 2; // Standard EQ bags are 2 columns
        _grid.AddThemeConstantOverride("h_separation", 8);
        _grid.AddThemeConstantOverride("v_separation", 8);
        margin.AddChild(_grid);

        Slots = new Button[bagSlots];
        for (int i = 0; i < bagSlots; i++)
        {
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(40, 40);
            btn.ExpandIcon = true;
            btn.IconAlignment = HorizontalAlignment.Center;
            int internalSlot = BaseBagSlot + i;
            btn.GuiInput += (ev) => MainUI.Instance.HandleSlotInput(ev, btn, internalSlot);
            _grid.AddChild(btn);
            Slots[i] = btn;
        }

        CloseRequested += () => {
            MainUI.Instance.CloseBag(ParentSlotId);
        };
    }
}
