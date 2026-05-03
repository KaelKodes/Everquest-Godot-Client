using Godot;
using System;
using System.Collections.Generic;

public partial class MelodyWindow : Window
{
    private MainUI _mainUI;
    private HotbarManager _hotbarManager;

    // Data Structure: 4 melodies, each containing an array of 4 spell gem slots (0-7, or -1 for empty)
    private int[][] _melodies = new int[4][];
    private string[][] _melodyDelays = new string[4][];
    private int[] _melodyIcons = new int[] { -1, -1, -1, -1 };
    private int _currentMelodyIndex = 0;

    // UI Nodes
    private Button[] _melodyButtons = new Button[4];
    private Button[] _letterBoxes = new Button[4];
    private LineEdit[] _delayInputs = new LineEdit[4];
    private PopupMenu _contextMenu;

    private int _contextMenuTargetMelody = -1;

    public override void _Ready()
    {
        Title = "Melodies";
        
        // Initialize blank data
        for (int i = 0; i < 4; i++)
        {
            _melodies[i] = new int[] { -1, -1, -1, -1 };
            _melodyDelays[i] = new string[] { "Twist", "Twist", "Twist", "Twist" };
        }

        // Setup Main Container
        var mainPanel = new Panel();
        mainPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.08f, 0.08f, 0.08f, 0.9f);
        mainPanel.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(mainPanel);

        var marginContainer = new MarginContainer();
        marginContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        marginContainer.AddThemeConstantOverride("margin_left", 10);
        marginContainer.AddThemeConstantOverride("margin_top", 10);
        marginContainer.AddThemeConstantOverride("margin_right", 10);
        marginContainer.AddThemeConstantOverride("margin_bottom", 10);
        mainPanel.AddChild(marginContainer);

        var mainHBox = new HBoxContainer();
        mainHBox.AddThemeConstantOverride("separation", 20);
        marginContainer.AddChild(mainHBox);

        // --- Left Side: Melody Slots (1-4) ---
        var leftVBox = new VBoxContainer();
        leftVBox.AddThemeConstantOverride("separation", 15);
        leftVBox.Alignment = BoxContainer.AlignmentMode.Center;
        mainHBox.AddChild(leftVBox);

        for (int i = 0; i < 4; i++)
        {
            var btn = new Button();
            btn.Text = (i + 1).ToString();
            btn.CustomMinimumSize = new Vector2(40, 40);
            
            int index = i;
            btn.GuiInput += (ev) => OnMelodyButtonGuiInput(ev, index);
            
            _melodyButtons[i] = btn;
            leftVBox.AddChild(btn);
        }

        // --- Right Side: Letterboxes & Controls ---
        var rightVBox = new VBoxContainer();
        rightVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightVBox.Alignment = BoxContainer.AlignmentMode.Center;
        rightVBox.AddThemeConstantOverride("separation", 15);
        mainHBox.AddChild(rightVBox);

        var grid = new GridContainer();
        grid.Columns = 2;
        grid.AddThemeConstantOverride("h_separation", 20);
        grid.AddThemeConstantOverride("v_separation", 20);
        grid.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        rightVBox.AddChild(grid);

        string[] letters = { "A", "B", "C", "D" };
        for (int i = 0; i < 4; i++)
        {
            var cellVBox = new VBoxContainer();
            cellVBox.Alignment = BoxContainer.AlignmentMode.Center;
            cellVBox.AddThemeConstantOverride("separation", 5);

            var box = new Button();
            box.Text = letters[i];
            box.CustomMinimumSize = new Vector2(60, 60);
            box.MouseFilter = Control.MouseFilterEnum.Stop;
            
            int letterIndex = i;
            box.GuiInput += (ev) => OnLetterBoxGuiInput(ev, letterIndex);
            
            _letterBoxes[i] = box;
            cellVBox.AddChild(box);

            var delayInput = new LineEdit();
            delayInput.Text = "Twist";
            delayInput.CustomMinimumSize = new Vector2(60, 24);
            delayInput.Alignment = HorizontalAlignment.Center;
            delayInput.TooltipText = "Wait before next song ('Twist', 'Auto', or e.g. '2.5')";
            delayInput.TextChanged += (text) => OnDelayTextChanged(text, letterIndex);
            
            _delayInputs[i] = delayInput;
            cellVBox.AddChild(delayInput);

            grid.AddChild(cellVBox);
        }

        var controlHBox = new HBoxContainer();
        controlHBox.Alignment = BoxContainer.AlignmentMode.Center;
        controlHBox.AddThemeConstantOverride("separation", 15);
        rightVBox.AddChild(controlHBox);

        var clearBtn = new Button();
        clearBtn.Text = "CLEAR";
        clearBtn.CustomMinimumSize = new Vector2(70, 30);
        clearBtn.Pressed += () => ClearCurrentMelody();
        controlHBox.AddChild(clearBtn);

        var saveBtn = new Button();
        saveBtn.Text = "SAVE";
        saveBtn.CustomMinimumSize = new Vector2(70, 30);
        saveBtn.Pressed += () => SaveToLayout();
        controlHBox.AddChild(saveBtn);

        // --- Context Menu ---
        _contextMenu = new PopupMenu();
        _contextMenu.Name = "MelodyContextMenu";
        _contextMenu.AddItem("Hotbutton", 0);
        _contextMenu.AddItem("Set Icon", 1);
        _contextMenu.AddItem("Clear", 2);
        _contextMenu.IdPressed += OnContextMenuItemPressed;
        AddChild(_contextMenu);

        // --- Top Right Close Button ---
        var topCloseBtn = new Button();
        topCloseBtn.Text = "X";
        topCloseBtn.CustomMinimumSize = new Vector2(24, 24);
        var closeSb = new StyleBoxFlat();
        closeSb.BgColor = new Color(0.4f, 0.1f, 0.1f, 1f);
        topCloseBtn.AddThemeStyleboxOverride("normal", closeSb);
        topCloseBtn.Pressed += () => Hide();
        
        topCloseBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        topCloseBtn.OffsetLeft = -34;
        topCloseBtn.OffsetRight = -10;
        topCloseBtn.OffsetTop = 10;
        topCloseBtn.OffsetBottom = 34;
        mainPanel.AddChild(topCloseBtn);

        CloseRequested += () => Hide();

        LoadFromLayout();
        UpdateMelodyIcons();
        SelectMelody(0);
    }

    private void UpdateMelodyIcons()
    {
        var iconMgr = IconManager.Instance;
        for (int i = 0; i < 4; i++) {
            if (_melodyIcons[i] >= 0 && iconMgr != null) {
                _melodyButtons[i].Icon = iconMgr.GetSpellGem(_melodyIcons[i]);
                _melodyButtons[i].Text = "";
                _melodyButtons[i].ExpandIcon = true;
                _melodyButtons[i].IconAlignment = HorizontalAlignment.Center;
            } else {
                _melodyButtons[i].Icon = null;
                _melodyButtons[i].Text = (i + 1).ToString();
            }
        }
    }

    public void InjectDependencies(MainUI mainUI, HotbarManager hotbarManager)
    {
        _mainUI = mainUI;
        _hotbarManager = hotbarManager;
    }

    private void SelectMelody(int index)
    {
        _currentMelodyIndex = index;
        
        for (int i = 0; i < _melodyButtons.Length; i++)
        {
            if (i == index)
            {
                _melodyButtons[i].AddThemeColorOverride("font_color", new Color(0, 1, 0)); // Highlight selected
            }
            else
            {
                _melodyButtons[i].RemoveThemeColorOverride("font_color");
            }
        }

        UpdateLetterBoxes();
    }

    private void UpdateLetterBoxes()
    {
        string[] letters = { "A", "B", "C", "D" };
        var currentLoadout = _melodies[_currentMelodyIndex];
        var currentDelays = _melodyDelays[_currentMelodyIndex];

        for (int i = 0; i < 4; i++)
        {
            int gemSlot = currentLoadout[i];
            if (gemSlot >= 0)
            {
                _letterBoxes[i].Text = $"Gem {gemSlot + 1}";
            }
            else
            {
                _letterBoxes[i].Text = letters[i];
            }
            
            _delayInputs[i].Text = string.IsNullOrEmpty(currentDelays[i]) ? "Twist" : currentDelays[i];
        }
    }

    private void ClearCurrentMelody()
    {
        for (int i = 0; i < 4; i++)
        {
            _melodies[_currentMelodyIndex][i] = -1;
            _melodyDelays[_currentMelodyIndex][i] = "Twist";
        }
        UpdateLetterBoxes();
    }

    private void OnDelayTextChanged(string text, int index)
    {
        _melodyDelays[_currentMelodyIndex][index] = text;
        SaveToLayout();
    }

    private void OnMelodyButtonGuiInput(InputEvent ev, int index)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                SelectMelody(index);
            }
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                _contextMenuTargetMelody = index;
                _contextMenu.Position = (Vector2I)GetMousePosition();
                _contextMenu.Popup();
            }
        }
    }

    private void OnLetterBoxGuiInput(InputEvent ev, int letterIndex)
    {
        if (ev is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            // Handle Spell Drop
            if (_hotbarManager != null && _hotbarManager.IsDragging)
            {
                var dragData = _hotbarManager.DragData;
                if (dragData != null && dragData.Type == Hotbar.HotbuttonType.Spell)
                {
                    int gemSlot = dragData.SpellSlotIndex;
                    _melodies[_currentMelodyIndex][letterIndex] = gemSlot;
                    _hotbarManager.CancelDrag();
                    UpdateLetterBoxes();
                }
            }
        }
    }

    private void OnContextMenuItemPressed(long id)
    {
        if (_contextMenuTargetMelody < 0) return;

        if (id == 0) // Hotbutton
        {
            GenerateHotbutton(_contextMenuTargetMelody);
        }
        else if (id == 1) // Set Icon
        {
            var picker = new SpellIconDebugger(true);
            picker.OnIconSelected = (iconId) => {
                _melodyIcons[_contextMenuTargetMelody] = iconId;
                UpdateMelodyIcons();
                SaveToLayout();
            };
            AddChild(picker);
            picker.PopupCentered();
        }
        else if (id == 2) // Clear
        {
            for (int i = 0; i < 4; i++) _melodies[_contextMenuTargetMelody][i] = -1;
            if (_currentMelodyIndex == _contextMenuTargetMelody) UpdateLetterBoxes();
        }
    }

    private void GenerateHotbutton(int melodyIndex)
    {
        if (_hotbarManager == null) return;
        
        string macroText = $"/melody";
        bool hasSpells = false;
        
        for (int i = 0; i < 4; i++)
        {
            int gemSlot = _melodies[melodyIndex][i];
            if (gemSlot >= 0)
            {
                string delay = _melodyDelays[melodyIndex][i];
                if (string.IsNullOrWhiteSpace(delay)) delay = "Twist";
                macroText += $" {gemSlot + 1} {delay}";
                hasSpells = true;
            }
        }

        if (!hasSpells)
        {
            GD.Print("Cannot create hotbutton for empty melody.");
            return;
        }

        // Start a drag event for a Macro button containing the /melody command
        _hotbarManager.StartMacroDrag(macroText, $"Melody {melodyIndex + 1}");
    }

    private void SaveToLayout()
    {
        var section = UILayoutManager.GetSection("Melodies");
        for (int i = 0; i < 4; i++)
        {
            var arr = new Godot.Collections.Array<int>();
            var delayArr = new Godot.Collections.Array<string>();
            for (int j = 0; j < 4; j++) 
            {
                arr.Add(_melodies[i][j]);
                delayArr.Add(string.IsNullOrWhiteSpace(_melodyDelays[i][j]) ? "Twist" : _melodyDelays[i][j]);
            }
            section[$"Melody_{i}"] = arr;
            section[$"MelodyDelays_{i}"] = delayArr;
            section[$"MelodyIcon_{i}"] = _melodyIcons[i];
        }
        UILayoutManager.SetSection("Melodies", section);
        GD.Print("Melodies saved to UI layout.");
    }

    private void LoadFromLayout()
    {
        var section = UILayoutManager.GetSection("Melodies");
        for (int i = 0; i < 4; i++)
        {
            if (section.TryGetValue($"Melody_{i}", out Variant val))
            {
                var arr = val.AsGodotArray<int>();
                for (int j = 0; j < 4 && j < arr.Count; j++)
                {
                    _melodies[i][j] = arr[j];
                }
            }
            if (section.TryGetValue($"MelodyDelays_{i}", out Variant delayVal))
            {
                var arr = delayVal.AsGodotArray<string>();
                for (int j = 0; j < 4 && j < arr.Count; j++)
                {
                    _melodyDelays[i][j] = arr[j];
                }
            }
            if (section.TryGetValue($"MelodyIcon_{i}", out Variant iconVal))
            {
                _melodyIcons[i] = iconVal.AsInt32();
            }
        }
    }
}
