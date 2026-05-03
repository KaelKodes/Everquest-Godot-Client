using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;

public partial class LightTunerWindow : Window
{
    private ItemList _objectList;
    private Button _okButton;
    private Button _cancelButton;
    private Button _reloadButton;

    private WorldManager _worldManager;
    private string _targetEntityId;
    
    // Known light objects
    private readonly List<string> _lightModels = new()
    {
        "cfi", "campfire", "bfi", "brazier", "tfi", "torch", "sconce", "wfi", 
        "lfi", "lantern", "lamp", "ffi", "forge", "candle", "cndl", "candel", 
        "candelabra", "chandelier", "chndlr", "chand", "mistlamp", "ogglantern",
        "pfi", "fire"
    };

    public void Setup(WorldManager wm, string targetEntityId)
    {
        _worldManager = wm;
        _targetEntityId = targetEntityId;
        
        Title = "Light Tuner";
        Size = new Vector2I(300, 400);
        Position = new Vector2I(100, 100);
        Exclusive = false;
        
        CloseRequested += QueueFree;

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 10);
        AddChild(vbox);

        var label = new Label { Text = "Select Light Model:" };
        vbox.AddChild(label);

        _objectList = new ItemList();
        _objectList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(_objectList);

        // Populate with common EQ light source model prefixes
        foreach (var model in _lightModels.OrderBy(x => x))
        {
            _objectList.AddItem(model);
        }

        var hbox = new HBoxContainer();
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(hbox);

        _okButton = new Button { Text = "Load Model" };
        _okButton.Pressed += OnOkPressed;
        hbox.AddChild(_okButton);

        _reloadButton = new Button { Text = "Reload JSON & Apply" };
        _reloadButton.Pressed += OnReloadPressed;
        hbox.AddChild(_reloadButton);

        _cancelButton = new Button { Text = "Cancel" };
        _cancelButton.Pressed += QueueFree;
        hbox.AddChild(_cancelButton);
    }

    private void OnOkPressed()
    {
        if (_objectList.GetSelectedItems().Length > 0)
        {
            string selectedModel = _objectList.GetItemText(_objectList.GetSelectedItems()[0]);
            ApplyModelToTarget(selectedModel);
        }
    }

    private void OnReloadPressed()
    {
        if (_objectList.GetSelectedItems().Length > 0)
        {
            string selectedModel = _objectList.GetItemText(_objectList.GetSelectedItems()[0]);
            // Force reload of JSON data in ZoneObjectPlacer
            _worldManager.ReloadLightTuning();
            ApplyModelToTarget(selectedModel);
        }
    }

    private void ApplyModelToTarget(string modelName)
    {
        // Actually replace the target's visual with the GLB model
        if (_worldManager != null)
        {
            _worldManager.SetEntityAsLightModel(_targetEntityId, modelName);
        }
    }
}
