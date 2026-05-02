using Godot;
using System;

public partial class ResponsiveIconWindow : Window
{
    [Export] public Vector2I IconSize = new Vector2I(40, 40);
    [Export] public int Separation = 4;
    [Export] public int Margins = 8;

    private Timer _snapTimer;
    private bool _snapping = false;

    public override void _Ready()
    {
        // Debounce timer — snap only after user stops dragging for 0.15s
        _snapTimer = new Timer();
        _snapTimer.OneShot = true;
        _snapTimer.WaitTime = 0.15;
        _snapTimer.Timeout += DoSnap;
        AddChild(_snapTimer);

        // Fully transparent window
        Transparent = true;
        TransparentBg = true;
        var transparentStyle = new StyleBoxFlat();
        transparentStyle.BgColor = new Color(0, 0, 0, 0);
        AddThemeStyleboxOverride("embedded_border", transparentStyle);
        AddThemeStyleboxOverride("embedded_unfocused_border", transparentStyle);

        SizeChanged += OnSizeChanged;
        CallDeferred(MethodName.DoSnap);
    }
    
    public void RequestUpdate()
    {
        CallDeferred(MethodName.RebuildSlots);
    }

    private void OnSizeChanged()
    {
        if (_snapping) return;
        // Restart the debounce timer every time the user drags
        _snapTimer.Stop();
        _snapTimer.Start();
        // Still rebuild slots live so user sees the grid update
        RebuildSlots();
    }

    private void DoSnap()
    {
        // Calculate how many cols/rows fit in current size
        int w = Size.X - (Margins * 2);
        int h = Size.Y - (Margins * 2);
        
        int cols = Mathf.Max(1, (w + Separation) / (IconSize.X + Separation));
        int rows = Mathf.Max(1, (h + Separation) / (IconSize.Y + Separation));
        
        int gridWidth = (cols * IconSize.X) + ((cols - 1) * Separation);
        int gridHeight = (rows * IconSize.Y) + ((rows - 1) * Separation);
        int snapWidth = gridWidth + (Margins * 2);
        int snapHeight = gridHeight + (Margins * 2);

        _snapping = true;
        Size = new Vector2I(snapWidth, snapHeight);
        _snapping = false;

        RebuildSlots();
    }

    private void RebuildSlots()
    {
        int w = Size.X - (Margins * 2);
        int h = Size.Y - (Margins * 2);
        
        int cols = Mathf.Max(1, (w + Separation) / (IconSize.X + Separation));
        int rows = Mathf.Max(1, (h + Separation) / (IconSize.Y + Separation));

        var container = GetChildOrNull<GridContainer>(0);
        if (container == null || MainUI.Instance == null || MainUI.Instance.BuffIconScene == null)
            return;

        container.AddThemeConstantOverride("h_separation", Separation);
        container.AddThemeConstantOverride("v_separation", Separation);
        container.Columns = cols;
        int totalSlots = cols * rows;
        
        while (container.GetChildCount() < totalSlots)
        {
            var slot = MainUI.Instance.BuffIconScene.Instantiate<Panel>();
            // Prevent GridContainer from stretching the icon
            slot.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            slot.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            container.AddChild(slot);
        }
        while (container.GetChildCount() > totalSlots)
        {
            var slot = container.GetChild(container.GetChildCount() - 1);
            container.RemoveChild(slot);
            slot.QueueFree();
        }

        // Ensure all existing children also have correct flags
        foreach (var child in container.GetChildren())
        {
            if (child is Control c)
            {
                c.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
                c.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            }
        }
        
        // Position grid container with margin offset
        container.SetAnchorsPreset(Control.LayoutPreset.TopLeft, true);
        int gridWidth = (cols * IconSize.X) + ((cols - 1) * Separation);
        int gridHeight = (rows * IconSize.Y) + ((rows - 1) * Separation);
        container.Size = new Vector2(gridWidth, gridHeight);
        container.Position = new Vector2(Margins, Margins);
        
        // Re-render buffs to fill the new slots
        MainUI.Instance.CallDeferred("RefreshBuffDisplay");
    }
}
