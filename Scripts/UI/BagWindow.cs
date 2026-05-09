using Godot;
using System;

/// <summary>Floating bag panel. Uses <see cref="PanelContainer"/> + <see cref="Control.TopLevel"/> instead of <see cref="Window"/>,
/// because embedded <c>Window</c> siblings under <see cref="MainUI"/> effectively stack as one layer and steal/break input.</summary>
public partial class BagWindow : PanelContainer
{
    private const int SlotPx = 40;
    private const int GridSep = 8;
    private const int MarginPx = 8;
    private const int TitleRowH = 28;

    private GridContainer _grid;
    private bool _dragging;

    public int ParentSlotId { get; private set; }
    public int HostItemInstanceId { get; private set; }
    public int BaseBagSlot { get; private set; }
    public Button[] Slots { get; private set; }

    /// <summary>Nested inventory IDs per parent slot; must match <c>NESTED_BAG_STRIDE</c> in server <c>inventory.js</c>.</summary>
    public const int NestedBagSlotStride = 10;

    public void Init(int parentSlotId, int hostItemInstanceId, int bagSlots, string bagName, int cascadeIndex)
    {
        ParentSlotId = parentSlotId;
        HostItemInstanceId = hostItemInstanceId;
        if (parentSlotId >= 2000 && parentSlotId <= 2023) {
            BaseBagSlot = 2531 + ((parentSlotId - 2000) * NestedBagSlotStride);
        } else if (parentSlotId >= 2500 && parentSlotId <= 2507) {
            BaseBagSlot = 2511 + ((parentSlotId - 2500) * NestedBagSlotStride);
        } else {
            BaseBagSlot = 251 + ((parentSlotId - 22) * NestedBagSlotStride);
        }

        bagSlots = Math.Max(1, bagSlots);
        int cols = 2;
        int rows = (bagSlots + cols - 1) / cols;
        int gridW = cols * SlotPx + (cols - 1) * GridSep;
        int gridH = rows * SlotPx + Math.Max(0, rows - 1) * GridSep;

        TopLevel = true;
        ZIndex = 100 + (cascadeIndex % 40);
        MouseFilter = Control.MouseFilterEnum.Stop;
        FocusMode = Control.FocusModeEnum.None;

        int innerW = gridW;
        int innerH = TitleRowH + 4 + gridH;
        int winW = innerW + MarginPx * 2;
        int winH = innerH + MarginPx * 2;

        var frame = new StyleBoxFlat();
        frame.BgColor = new Color(0.06f, 0.055f, 0.05f, 0.98f);
        frame.BorderWidthLeft = frame.BorderWidthTop = frame.BorderWidthRight = frame.BorderWidthBottom = 2;
        frame.BorderColor = new Color(0.55f, 0.45f, 0.2f, 1f);
        frame.CornerRadiusTopLeft = frame.CornerRadiusTopRight = frame.CornerRadiusBottomLeft = frame.CornerRadiusBottomRight = 4;
        AddThemeStyleboxOverride("panel", frame);

        CustomMinimumSize = new Vector2(winW, winH);
        Size = new Vector2(winW, winH);

        var outer = new MarginContainer();
        outer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("margin_left", MarginPx);
        outer.AddThemeConstantOverride("margin_top", MarginPx);
        outer.AddThemeConstantOverride("margin_right", MarginPx);
        outer.AddThemeConstantOverride("margin_bottom", MarginPx);
        AddChild(outer);

        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 4);
        outer.AddChild(root);

        var titleRow = new HBoxContainer();
        titleRow.CustomMinimumSize = new Vector2(0, TitleRowH);
        titleRow.MouseFilter = Control.MouseFilterEnum.Stop;
        root.AddChild(titleRow);

        var titleStyle = new StyleBoxFlat();
        titleStyle.BgColor = new Color(0.12f, 0.1f, 0.08f, 1f);
        titleStyle.BorderWidthBottom = 1;
        titleStyle.BorderColor = new Color(0.55f, 0.45f, 0.2f, 0.9f);
        var titleBg = new PanelContainer();
        titleBg.AddThemeStyleboxOverride("panel", titleStyle);
        titleBg.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleBg.MouseFilter = Control.MouseFilterEnum.Stop;
        titleRow.AddChild(titleBg);

        var titleLabel = new Label();
        titleLabel.Text = bagName ?? "Bag";
        titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleLabel.VerticalAlignment = VerticalAlignment.Center;
        titleLabel.AddThemeFontSizeOverride("font_size", 12);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.45f));
        titleLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        titleBg.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "×";
        closeBtn.CustomMinimumSize = new Vector2(TitleRowH, TitleRowH);
        closeBtn.FocusMode = Control.FocusModeEnum.None;
        closeBtn.Flat = true;
        closeBtn.AddThemeFontSizeOverride("font_size", 18);
        closeBtn.Pressed += RequestClose;
        titleRow.AddChild(closeBtn);

        titleBg.GuiInput += OnTitleBarGuiInput;

        _grid = new GridContainer();
        _grid.Columns = 2;
        _grid.AddThemeConstantOverride("h_separation", GridSep);
        _grid.AddThemeConstantOverride("v_separation", GridSep);
        root.AddChild(_grid);

        Slots = new Button[bagSlots];
        for (int i = 0; i < bagSlots; i++)
        {
            var btn = new Button();
            btn.CustomMinimumSize = new Vector2(SlotPx, SlotPx);
            btn.ExpandIcon = true;
            btn.IconAlignment = HorizontalAlignment.Center;
            int internalSlot = BaseBagSlot + i;
            btn.GuiInput += (ev) => MainUI.Instance.HandleSlotInput(ev, btn, internalSlot);
            _grid.AddChild(btn);
            Slots[i] = btn;
        }

        ApplySavedOrDefaultPosition(cascadeIndex);
        Visible = true;
        MoveToFront();
    }

    private void RequestClose()
    {
        Callable.From(CloseFromMain).CallDeferred();
    }

    private void CloseFromMain()
    {
        var ui = MainUI.Instance;
        if (ui != null && GodotObject.IsInstanceValid(ui))
            ui.CloseBag(HostItemInstanceId);
    }

    private void OnTitleBarGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (!mb.Pressed && _dragging)
                BagLayoutStore.Save(ParentSlotId, GlobalPosition);
            _dragging = mb.Pressed;
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            GlobalPosition += mm.Relative;
        }
    }

    private void ApplySavedOrDefaultPosition(int cascadeIndex)
    {
        var vr = GetViewport().GetVisibleRect();
        var sz = Size;
        if (BagLayoutStore.TryLoad(ParentSlotId, out var saved))
        {
            GlobalPosition = BagLayoutStore.ClampGlobalPosition(saved, sz, vr);
            return;
        }
        ApplyCascadePosition(cascadeIndex);
    }

    private void ApplyCascadePosition(int cascadeIndex)
    {
        var vr = GetViewport().GetVisibleRect();
        int step = 28;
        int ox = cascadeIndex * step + (ParentSlotId % 7) * 3;
        int oy = cascadeIndex * step + (ParentSlotId % 5) * 2;
        float w = Size.X;
        float h = Size.Y;
        float x = vr.Position.X + (vr.Size.X - w) * 0.5f + ox;
        float y = vr.Position.Y + (vr.Size.Y - h) * 0.5f + oy;

        float minX = vr.Position.X + 4;
        float minY = vr.Position.Y + 4;
        float maxX = vr.Position.X + vr.Size.X - w - 4;
        float maxY = vr.Position.Y + vr.Size.Y - h - 4;
        if (maxX < minX) maxX = minX;
        if (maxY < minY) maxY = minY;

        x = Mathf.Clamp(x, minX, maxX);
        y = Mathf.Clamp(y, minY, maxY);
        GlobalPosition = new Vector2(x, y);
    }
}
