using Godot;
using System;

public partial class SpellIconDebugger : Window
{
	private VBoxContainer _vbox;
	private float _currentScale = 1.0f;
    public Action<int> OnIconSelected;

	public SpellIconDebugger(bool pickerMode = false)
	{
		Title = pickerMode ? "Select Icon" : "Spell Icon Debugger (TGA Mapping)";
		Size = new Vector2I(800, 600);
		MinSize = new Vector2I(400, 300);
		InitialPosition = Window.WindowInitialPosition.CenterMainWindowScreen;
		CloseRequested += Hide;

		var scroll = new ScrollContainer();
		scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(scroll);

		// --- Custom Close Button ---
		if (pickerMode)
		{
			var topCloseBtn = new Button();
			topCloseBtn.Text = "X";
			topCloseBtn.CustomMinimumSize = new Vector2(24, 24);
			var closeSb = new StyleBoxFlat();
			closeSb.BgColor = new Color(0.4f, 0.1f, 0.1f, 1f);
			topCloseBtn.AddThemeStyleboxOverride("normal", closeSb);
			topCloseBtn.Pressed += Hide;
			
			topCloseBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
			topCloseBtn.OffsetLeft = -34;
			topCloseBtn.OffsetRight = -10;
			topCloseBtn.OffsetTop = 10;
			topCloseBtn.OffsetBottom = 34;
			
			AddChild(topCloseBtn);
		}

		_vbox = new VBoxContainer();
		_vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_vbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_vbox.AddThemeConstantOverride("separation", 20);
		
		// Wrap vbox in a control to handle scaling easily within the scroll container
		var scaleContainer = new Control();
		scaleContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scaleContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scaleContainer.AddChild(_vbox);
		scroll.AddChild(scaleContainer);

		string[] filesToLoad = pickerMode ? 
			new string[] { "gemicons01.tga", "gemicons02.tga" } : 
			new string[] {
				"spells01.tga", "spells02.tga", "spells03.tga", "spells04.tga", "spells05.tga", "spells06.tga", "spells07.tga",
				"gemicons01.tga", "gemicons02.tga"
			};

		foreach (string file in filesToLoad)
		{
			string path = $"res://Assets/UI/ClassicUI/{file}";

			if (ResourceLoader.Exists(path))
			{
				var sheetTex = GD.Load<Texture2D>(path);
				
				var sheetHeader = new Label();
				sheetHeader.Text = file;
				sheetHeader.AddThemeFontSizeOverride("font_size", 24);
				_vbox.AddChild(sheetHeader);

				// Create a container for the texture so we can overlay numbers on it
				var texContainer = new Control();
				texContainer.CustomMinimumSize = sheetTex.GetSize();
				texContainer.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
				texContainer.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
				_vbox.AddChild(texContainer);

				var texRect = new TextureRect();
				texRect.Texture = sheetTex;
				texRect.StretchMode = TextureRect.StretchModeEnum.Keep;
				texRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
				texContainer.AddChild(texRect);

				bool isGem = file.StartsWith("gem");
				int count = isGem ? 100 : 36;
				int cols = isGem ? 10 : 6;
				int cellSize = isGem ? 24 : 40;

				for (int index = 0; index < count; index++)
				{
					int col = index % cols;
					int row = index / cols;
                    
                    // Global icon ID mapping across files (Assuming 0-indexed across sheets)
                    // If it's gemicons01, fileIdx = 0, so global ID = index.
                    // If it's gemicons02, fileIdx = 1, so global ID = 100 + index.
                    int fileOffset = 0;
                    if (file == "gemicons02.tga") fileOffset = 100;
                    int iconId = index + fileOffset;

                    if (pickerMode)
                    {
                        var btn = new Button();
                        btn.Position = new Vector2(col * cellSize, row * cellSize);
                        btn.Size = new Vector2(cellSize, cellSize);
                        btn.Flat = true; // Invisible button overlay
                        
                        // Hover effect
                        var hoverStyle = new StyleBoxFlat();
                        hoverStyle.BgColor = new Color(1f, 1f, 1f, 0.2f);
                        btn.AddThemeStyleboxOverride("hover", hoverStyle);
                        
                        btn.Pressed += () => {
                            OnIconSelected?.Invoke(iconId);
                            Hide();
                        };
                        texContainer.AddChild(btn);
                    }
                    else
                    {
                        var label = new Label();
                        label.Text = index.ToString();
                        label.Position = new Vector2(col * cellSize, row * cellSize);
                        label.Size = new Vector2(cellSize, cellSize);
                        label.HorizontalAlignment = HorizontalAlignment.Center;
                        label.VerticalAlignment = VerticalAlignment.Center;
                        
                        var styleBox = new StyleBoxFlat();
                        styleBox.BgColor = new Color(0, 0, 0, 0.5f);
                        label.AddThemeStyleboxOverride("normal", styleBox);
                        
                        label.AddThemeColorOverride("font_color", Colors.Yellow);
                        label.AddThemeFontSizeOverride("font_size", 14);

                        texContainer.AddChild(label);
                    }
				}
			}
		}

        if (pickerMode) {
            _currentScale = 2.5f;
            UpdateZoom();
        }
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
		{
			if (mouseBtn.IsCommandOrControlPressed())
			{
				if (mouseBtn.ButtonIndex == MouseButton.WheelUp)
				{
					_currentScale += 0.2f;
					UpdateZoom();
					GetViewport().SetInputAsHandled();
				}
				else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
				{
					_currentScale = Mathf.Max(0.2f, _currentScale - 0.2f);
					UpdateZoom();
					GetViewport().SetInputAsHandled();
				}
			}
		}
	}

	private void UpdateZoom()
	{
		_vbox.Scale = new Vector2(_currentScale, _currentScale);
		// Update the parent control's minimum size to reflect the scaled vbox
		if (_vbox.GetParent() is Control parent)
		{
			parent.CustomMinimumSize = _vbox.Size * _currentScale;
		}
	}
}
