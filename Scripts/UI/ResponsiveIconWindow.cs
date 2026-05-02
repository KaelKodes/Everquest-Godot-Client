using Godot;
using System;

public partial class ResponsiveIconWindow : Window
{
    [Export] public Vector2I IconSize = new Vector2I(40, 40);
    [Export] public int Separation = 4;
    [Export] public int Margins = 8;
    [Export] public int TitleBarHeight = 0; // Usually 0 if borderless

    private PopupMenu _contextMenu;
    private bool _isMoving = false;
    private bool _isResizing = false;

    public override void _Ready()
    {
        SizeChanged += OnSizeChanged;
        
        // Locked by default
        Borderless = true;
        Unfocusable = true;

        // Create Context Menu
        _contextMenu = new PopupMenu();
        _contextMenu.Name = "WindowContextMenu";
        _contextMenu.AddItem("Move", 0);
        _contextMenu.AddItem("Resize", 1);
        _contextMenu.IdPressed += OnContextMenuItemPressed;
        AddChild(_contextMenu);

        // Trigger initial calculation
        CallDeferred(MethodName.OnSizeChanged);
    }
    
    public void RequestUpdate()
    {
        CallDeferred(MethodName.OnSizeChanged);
    }

    public void StartMove()
    {
        _isMoving = true;
        _isResizing = false;
        Borderless = true;
        Unfocusable = false;
    }

    public void StartResize()
    {
        _isMoving = false;
        _isResizing = true;
        Borderless = false;
        Unfocusable = false;
    }

    private void OnContextMenuItemPressed(long id)
    {
        if (id == 0) StartMove();
        else if (id == 1) StartResize();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Handle right-click on the window background to open the context menu
        if (!_isMoving && !_isResizing)
        {
            if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                _contextMenu.Position = new Vector2I((int)mb.GlobalPosition.X, (int)mb.GlobalPosition.Y);
                _contextMenu.Popup();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Cancel Move/Resize if user clicks anywhere (Left or Right click)
        if ((_isMoving || _isResizing) && @event is InputEventMouseButton mb && mb.Pressed)
        {
            // If they clicked the context menu itself, ignore
            if (mb.ButtonIndex == MouseButton.Right) return; 

            // Stop interacting
            _isMoving = false;
            _isResizing = false;
            Borderless = true;
            Unfocusable = true;

            // Optional: Save layout here
            if (MainUI.Instance != null) {
                MainUI.Instance.SaveWindowPositions();
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        // Handle Sticky Move
        if (_isMoving && @event is InputEventMouseMotion mm)
        {
            // Follow the mouse cursor
            Position += new Vector2I((int)mm.Relative.X, (int)mm.Relative.Y);
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnSizeChanged()
    {
        // Calculate how many columns can fit in current width
        int w = Size.X - (Margins * 2);
        int cols = Mathf.Max(1, (w + Separation) / (IconSize.X + Separation));
        int targetWidth = (cols * (IconSize.X + Separation)) - Separation + (Margins * 2);
        
        // Count how many children we have to determine rows
        int childCount = 0;
        var container = GetChildOrNull<Container>(0);
        if (container != null)
        {
            foreach (Node n in container.GetChildren())
            {
                if (n is Control c && c.Visible) childCount++;
            }
        }
        
        int rows = 1;
        if (childCount > 0)
        {
            rows = Mathf.CeilToInt((float)childCount / cols);
        }
        
        int targetHeight = (rows * (IconSize.Y + Separation)) - Separation + (Margins * 2) + TitleBarHeight;
        
        // Let user resize, but snap if we are significantly off or just lock to grid
        if (Size.X != targetWidth || Size.Y != targetHeight)
        {
            Size = new Vector2I(targetWidth, targetHeight);
        }
    }
}
