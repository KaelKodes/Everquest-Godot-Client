using Godot;
using System.Text.Json;

public partial class BankWindow : Window
{
    private GridContainer _leftGrid;
    private GridContainer _middleGrid;
    private GridContainer _rightGrid;
    private GridContainer _sharedGrid;

    public Button[] BankSlots { get; private set; } = new Button[24];
    public Button[] SharedBankSlots { get; private set; } = new Button[8];

    private Label _moneyLabel;

    public override void _Ready()
    {
        Title = "Bank";
        Size = new Vector2I(340, 500);
        MinSize = new Vector2I(340, 500);
        Exclusive = false;
        Unresizable = true;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        
        // Classic EQ Bank dark blue background
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.05f, 0.1f, 0.2f, 1.0f);
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        panel.AddChild(margin);

        var mainVbox = new VBoxContainer();
        margin.AddChild(mainVbox);

        var titleLabel = new Label();
        titleLabel.Text = "a bank broker";
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        mainVbox.AddChild(titleLabel);

        var hbox = new HBoxContainer();
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        hbox.AddThemeConstantOverride("separation", 16);
        mainVbox.AddChild(hbox);

        // Left Block (Slots 0-7)
        var leftVbox = new VBoxContainer();
        _leftGrid = new GridContainer();
        _leftGrid.Columns = 2;
        leftVbox.AddChild(_leftGrid);
        
        // Buttons (Auto-Bank etc are crossed out by user but we can add Shroud/Tradeskill just as placeholders)
        var btnVbox = new VBoxContainer();
        var depotBtn = new Button { Text = "Tradeskill Depot" };
        btnVbox.AddChild(depotBtn);
        btnVbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        btnVbox.Alignment = BoxContainer.AlignmentMode.End;
        leftVbox.AddChild(btnVbox);
        hbox.AddChild(leftVbox);

        // Middle Block (Slots 8-15) + Money
        var midVbox = new VBoxContainer();
        _middleGrid = new GridContainer();
        _middleGrid.Columns = 2;
        midVbox.AddChild(_middleGrid);
        
        var moneyVbox = new VBoxContainer();
        moneyVbox.AddThemeConstantOverride("separation", 2);
        string[] coins = {"Platinum", "Gold", "Silver", "Copper"};
        Color[] colors = { Colors.LightBlue, Colors.Gold, Colors.LightGray, new Color(0.8f, 0.5f, 0.2f) };
        for (int i=0; i<4; i++) {
            var cRow = new HBoxContainer();
            var icon = new ColorRect { CustomMinimumSize = new Vector2(16, 16), Color = colors[i] };
            var val = new Label { Text = "0", HorizontalAlignment = HorizontalAlignment.Right, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            cRow.AddChild(icon);
            cRow.AddChild(val);
            moneyVbox.AddChild(cRow);
        }
        var changeBtn = new Button { Text = "Change" };
        moneyVbox.AddChild(changeBtn);
        midVbox.AddChild(moneyVbox);
        hbox.AddChild(midVbox);

        // Right Block (Slots 16-23) + Shared Bank (8 slots)
        var rightVbox = new VBoxContainer();
        _rightGrid = new GridContainer();
        _rightGrid.Columns = 2;
        rightVbox.AddChild(_rightGrid);

        var sharedLabel = new Label { Text = "Shared Bank", HorizontalAlignment = HorizontalAlignment.Center };
        rightVbox.AddChild(sharedLabel);

        _sharedGrid = new GridContainer();
        _sharedGrid.Columns = 2;
        rightVbox.AddChild(_sharedGrid);
        hbox.AddChild(rightVbox);

        // Initialize Slot Buttons
        for (int i = 0; i < 24; i++)
        {
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(40, 40);
            btn.ExpandIcon = true;
            btn.IconAlignment = HorizontalAlignment.Center;
            int slotId = 2000 + i;
            btn.GuiInput += (ev) => MainUI.Instance.HandleSlotInput(ev, btn, slotId);
            
            if (i < 8) _leftGrid.AddChild(btn);
            else if (i < 16) _middleGrid.AddChild(btn);
            else _rightGrid.AddChild(btn);
            
            BankSlots[i] = btn;
        }

        for (int i = 0; i < 8; i++)
        {
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(40, 40);
            btn.ExpandIcon = true;
            btn.IconAlignment = HorizontalAlignment.Center;
            int slotId = 2500 + i;
            btn.GuiInput += (ev) => MainUI.Instance.HandleSlotInput(ev, btn, slotId);
            _sharedGrid.AddChild(btn);
            SharedBankSlots[i] = btn;
        }

        // Bottom Done bar
        var bottomHbox = new HBoxContainer();
        bottomHbox.Alignment = BoxContainer.AlignmentMode.Center;
        bottomHbox.SizeFlagsVertical = Control.SizeFlags.Expand | Control.SizeFlags.ShrinkEnd;
        mainVbox.AddChild(bottomHbox);

        var doneBtn = new Button { Text = "Done", CustomMinimumSize = new Vector2(100, 30) };
        doneBtn.Pressed += () => { QueueFree(); };
        bottomHbox.AddChild(doneBtn);

        _moneyLabel = new Label { Text = "0" };
        bottomHbox.AddChild(_moneyLabel);

        CloseRequested += () => { QueueFree(); };
    }

    public void UpdateMoneyDisplay(int copper)
    {
        if (_moneyLabel != null)
            _moneyLabel.Text = copper.ToString();
    }
}
