using Godot;
using System;
using System.Text.Json;
using System.Collections.Generic;

public partial class ItemDetailsWindow : PanelContainer
{
    private Label _titleLabel;
    private RichTextLabel _headerText;
    private RichTextLabel _statsText;
    private RichTextLabel _augmentsText;
    private TextureRect _itemIcon;
    private Button _closeButton;

    private bool _isDragging = false;
    private Vector2 _dragOffset;

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(320, 300);
        ZIndex = 300;
        MouseFilter = MouseFilterEnum.Stop;

        var sb = new StyleBoxFlat();
        sb.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        sb.BorderColor = new Color(0.8f, 0.7f, 0.2f, 1f); // Golden border
        sb.SetBorderWidthAll(2);
        sb.SetCornerRadiusAll(4);
        AddThemeStyleboxOverride("panel", sb);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 0);
        AddChild(vbox);

        // Title bar (draggable)
        var titleBar = new ColorRect();
        titleBar.Color = new Color(0.2f, 0.2f, 0.25f, 1f);
        titleBar.CustomMinimumSize = new Vector2(0, 24);
        titleBar.MouseFilter = MouseFilterEnum.Stop;
        titleBar.GuiInput += OnTitleBarInput;
        vbox.AddChild(titleBar);

        var titleHBox = new HBoxContainer();
        titleHBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        titleBar.AddChild(titleHBox);

        _titleLabel = new Label();
        _titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.VerticalAlignment = VerticalAlignment.Center;
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        titleHBox.AddChild(_titleLabel);

        _closeButton = new Button();
        _closeButton.Text = "X";
        _closeButton.CustomMinimumSize = new Vector2(24, 24);
        var closeSb = new StyleBoxFlat();
        closeSb.BgColor = new Color(0.4f, 0.1f, 0.1f, 1f);
        _closeButton.AddThemeStyleboxOverride("normal", closeSb);
        _closeButton.Pressed += () => Visible = false;
        titleHBox.AddChild(_closeButton);

        var marginContainer = new MarginContainer();
        marginContainer.AddThemeConstantOverride("margin_left", 8);
        marginContainer.AddThemeConstantOverride("margin_right", 8);
        marginContainer.AddThemeConstantOverride("margin_top", 8);
        marginContainer.AddThemeConstantOverride("margin_bottom", 8);
        marginContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(marginContainer);

        var contentVBox = new VBoxContainer();
        marginContainer.AddChild(contentVBox);

        // Header HBox (Icon + Header Text)
        var headerHBox = new HBoxContainer();
        contentVBox.AddChild(headerHBox);

        _itemIcon = new TextureRect();
        _itemIcon.CustomMinimumSize = new Vector2(40, 40);
        _itemIcon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _itemIcon.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        headerHBox.AddChild(_itemIcon);

        _headerText = new RichTextLabel();
        _headerText.BbcodeEnabled = true;
        _headerText.FitContent = true;
        _headerText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerHBox.AddChild(_headerText);

        var sep1 = new HSeparator();
        contentVBox.AddChild(sep1);

        // Stats Text
        _statsText = new RichTextLabel();
        _statsText.BbcodeEnabled = true;
        _statsText.FitContent = true;
        _statsText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        contentVBox.AddChild(_statsText);

        var sep2 = new HSeparator();
        contentVBox.AddChild(sep2);

        // Augments Text
        _augmentsText = new RichTextLabel();
        _augmentsText.BbcodeEnabled = true;
        _augmentsText.FitContent = true;
        _augmentsText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        contentVBox.AddChild(_augmentsText);
    }

    /// <summary>Safely extract an int from a JsonElement that may be a number, string, or decimal.</summary>
    private static int SafeInt(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                if (el.TryGetInt32(out int iv)) return iv;
                return (int)el.GetDouble(); // handles decimals like 10.0
            case JsonValueKind.String:
                return int.TryParse(el.GetString(), out int sv) ? sv : 0;
            default:
                return 0;
        }
    }

    /// <summary>Safely extract a string from a JsonElement that may be a string, number, or null.</summary>
    private static string SafeStr(JsonElement el)
    {
        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.ToString();
    }

    public void ShowItem(JsonElement item, Vector2 pos)
    {
        string name = item.TryGetProperty("itemName", out var n) ? SafeStr(n) : "Unknown Item";
        _titleLabel.Text = name;

        int iconId = item.TryGetProperty("icon", out var ic) ? SafeInt(ic) : 0;
        var iconMgr = IconManager.Instance;
        if (iconMgr != null && iconId > 0) {
            _itemIcon.Texture = iconMgr.GetItemIcon(iconId);
        } else {
            _itemIcon.Texture = null;
        }

        int classes = item.TryGetProperty("classes", out var cl) ? SafeInt(cl) : 0;
        int races = item.TryGetProperty("races", out var r) ? SafeInt(r) : 0;
        int equipSlot = item.TryGetProperty("equipSlot", out var es) ? SafeInt(es) : 0;
        
        string magic = (item.TryGetProperty("magic", out var mg) && SafeInt(mg) > 0) ? "MAGIC ITEM  " : "";
        string lore = (item.TryGetProperty("lore", out var lr) && !string.IsNullOrEmpty(SafeStr(lr))) ? "LORE ITEM  " : "";
        string nodrop = (item.TryGetProperty("nodrop", out var nd) && SafeInt(nd) == 0) ? "NO DROP  " : "";
        string norent = (item.TryGetProperty("norent", out var nr) && SafeInt(nr) == 0) ? "NO RENT  " : "";
        string placeable = (item.TryGetProperty("placeable", out var pl) && SafeInt(pl) > 0) ? "PLACEABLE  " : "";

        string flags = (magic + lore + nodrop + norent + placeable).Trim();

        _headerText.Clear();
        // Item Name in Green if Magic, else White
        string nameColor = magic != "" ? "#00ff00" : "#ffffff";
        _headerText.AppendText($"[color={nameColor}]{name}[/color]\n");
        if (flags != "") _headerText.AppendText($"[color=#dddddd]{flags}[/color]\n");
        
        int playerClassMask = MainUI.Instance != null ? MainUI.Instance.GetPlayerClassMask() : 1;
        int playerRaceMask = MainUI.Instance != null ? MainUI.Instance.GetPlayerRaceMask() : 1;
        int playerLevel = MainUI.Instance != null ? MainUI.Instance.GetPlayerLevel() : 1;

        var classInfo = GetClassString(classes, playerClassMask);
        var raceInfo = GetRaceString(races, playerRaceMask);

        string classLabelColor = classInfo.Meets ? "#ffffff" : "#ff0000";
        string raceLabelColor = raceInfo.Meets ? "#ffffff" : "#ff0000";

        _headerText.AppendText($"[color={classLabelColor}]Class:[/color] {classInfo.Text}\n");
        _headerText.AppendText($"[color={raceLabelColor}]Race:[/color] {raceInfo.Text}\n");
        
        if (equipSlot > 0) {
            _headerText.AppendText($"[color=#dddddd]Slot: {GetSlotString(equipSlot)}[/color]");
        }

        // Stats
        _statsText.Clear();
        int hp = item.TryGetProperty("hp", out var h) ? SafeInt(h) : 0;
        int mana = item.TryGetProperty("mana", out var m) ? SafeInt(m) : 0;
        int endur = item.TryGetProperty("endur", out var en) ? SafeInt(en) : 0;
        int ac = item.TryGetProperty("ac", out var a) ? SafeInt(a) : 0;
        
        int damage = item.TryGetProperty("damage", out var dmg) ? SafeInt(dmg) : 0;
        int delay = item.TryGetProperty("delay", out var dly) ? SafeInt(dly) : 0;
        int weight = item.TryGetProperty("weight", out var w) ? SafeInt(w) : 0;
        int size = item.TryGetProperty("size", out var sz) ? SafeInt(sz) : 0;
        
        int reqLevel = item.TryGetProperty("reqlevel", out var req) ? SafeInt(req) : 0;
        int recLevel = item.TryGetProperty("reclevel", out var rec) ? SafeInt(rec) : 0;

        // Build a grid using spaces or a table (BBCode table is supported in Godot RichTextLabel)
        _statsText.AppendText("[table=3]");
        
        _statsText.AppendText($"[cell]Size: {GetSizeString(size)}[/cell]");
        _statsText.AppendText($"[cell]HP: {hp}[/cell]");
        _statsText.AppendText(damage > 0 ? $"[cell]Base Dmg: {damage}[/cell]" : "[cell][/cell]");

        _statsText.AppendText($"[cell]Weight: {(weight/10f):0.0}[/cell]");
        _statsText.AppendText($"[cell]Mana: {mana}[/cell]");
        _statsText.AppendText(delay > 0 ? $"[cell]Delay: {delay}[/cell]" : "[cell][/cell]");

        string levelStr = "";
        if (reqLevel > 0) {
            string color = playerLevel >= reqLevel ? "#00ff00" : "#ff0000";
            levelStr = $"[color={color}]Req Level: {reqLevel}[/color]";
        } else if (recLevel > 0) {
            string color = playerLevel >= recLevel ? "#00ff00" : "#ffffff";
            levelStr = $"[color={color}]Rec Level: {recLevel}[/color]";
        }
        
        _statsText.AppendText($"[cell]{levelStr}[/cell]");
        _statsText.AppendText($"[cell]End: {endur}[/cell]");
        
        if (damage > 0 && delay > 0) {
            float ratio = (float)damage / delay;
            _statsText.AppendText($"[cell]Ratio: {ratio:0.00}[/cell]");
            
            // Calculate Damage Bonus (1H approximation)
            int dmgBon = 0;
            if (playerLevel >= 28) {
                dmgBon = (playerLevel - 25) / 3;
            }
            if (dmgBon > 0) {
                _statsText.AppendText("[cell][/cell][cell][/cell]");
                _statsText.AppendText($"[cell]Dmg Bon: {dmgBon}[/cell]");
            }
        } else {
            _statsText.AppendText("[cell][/cell]");
        }
        _statsText.AppendText("[/table]\n\n");

        // Base Stats
        int str = item.TryGetProperty("str", out var s) ? SafeInt(s) : 0;
        int sta = item.TryGetProperty("sta", out var st) ? SafeInt(st) : 0;
        int intel = item.TryGetProperty("int", out var i) ? SafeInt(i) : 0;
        int wis = item.TryGetProperty("wis", out var wi) ? SafeInt(wi) : 0;
        int agi = item.TryGetProperty("agi", out var ag) ? SafeInt(ag) : 0;
        int dex = item.TryGetProperty("dex", out var dx) ? SafeInt(dx) : 0;
        int cha = item.TryGetProperty("cha", out var c) ? SafeInt(c) : 0;
        
        int mr = item.TryGetProperty("mr", out var mr_prop) ? SafeInt(mr_prop) : 0;
        int fr = item.TryGetProperty("fr", out var fr_prop) ? SafeInt(fr_prop) : 0;
        int cr = item.TryGetProperty("cr", out var cr_prop) ? SafeInt(cr_prop) : 0;
        int dr = item.TryGetProperty("dr", out var dr_prop) ? SafeInt(dr_prop) : 0;
        int pr = item.TryGetProperty("pr", out var pr_prop) ? SafeInt(pr_prop) : 0;

        _statsText.AppendText("[table=2]");
        if (str != 0) _statsText.AppendText($"[cell]Strength: {str}[/cell]"); else _statsText.AppendText("[cell][/cell]");
        if (mr != 0) _statsText.AppendText($"[cell]Magic: {mr}[/cell]"); else _statsText.AppendText("[cell][/cell]");
        
        if (sta != 0) _statsText.AppendText($"[cell]Stamina: {sta}[/cell]"); else _statsText.AppendText("[cell][/cell]");
        if (fr != 0) _statsText.AppendText($"[cell]Fire: {fr}[/cell]"); else _statsText.AppendText("[cell][/cell]");

        if (intel != 0) _statsText.AppendText($"[cell]Intelligence: {intel}[/cell]"); else _statsText.AppendText("[cell][/cell]");
        if (cr != 0) _statsText.AppendText($"[cell]Cold: {cr}[/cell]"); else _statsText.AppendText("[cell][/cell]");

        if (wis != 0) _statsText.AppendText($"[cell]Wisdom: {wis}[/cell]"); else _statsText.AppendText("[cell][/cell]");
        if (dr != 0) _statsText.AppendText($"[cell]Disease: {dr}[/cell]"); else _statsText.AppendText("[cell][/cell]");

        if (agi != 0) _statsText.AppendText($"[cell]Agility: {agi}[/cell]"); else _statsText.AppendText("[cell][/cell]");
        if (pr != 0) _statsText.AppendText($"[cell]Poison: {pr}[/cell]"); else _statsText.AppendText("[cell][/cell]");

        if (dex != 0) _statsText.AppendText($"[cell]Dexterity: {dex}[/cell][cell][/cell]");
        if (cha != 0) _statsText.AppendText($"[cell]Charisma: {cha}[/cell][cell][/cell]");
        _statsText.AppendText("[/table]");

        // Augments
        _augmentsText.Clear();
        bool hasAugs = false;
        for (int augIdx = 1; augIdx <= 6; augIdx++) {
            if (item.TryGetProperty($"augslot{augIdx}type", out var augProp)) {
                int augType = SafeInt(augProp);
                if (augType > 0) {
                    _augmentsText.AppendText($"Slot {augIdx}, type {augType}: empty\n");
                    hasAugs = true;
                }
            }
        }
        
        if (!hasAugs) {
            _augmentsText.GetParent<Control>().GetChild<Control>(2).Visible = false; // Hide separator 2
            _augmentsText.Visible = false;
        } else {
            _augmentsText.GetParent<Control>().GetChild<Control>(2).Visible = true;
            _augmentsText.Visible = true;
        }

        if (!LoadPosition()) {
            GlobalPosition = pos;
        }
        MoveToFront();
        Visible = true;
    }

    private (string Text, bool Meets) GetClassString(int mask, int playerMask)
    {
        if (mask == 65535 || mask == 0) return ("[color=#00ff00]ALL[/color]", true);
        
        bool meets = (mask & playerMask) != 0;
        var classes = new List<string>();
        Action<int, string> check = (bit, name) => {
            if ((mask & bit) != 0) {
                if (bit == playerMask) classes.Add($"[color=#00ff00]{name}[/color]");
                else classes.Add(name);
            }
        };
        
        check(1, "WAR"); check(2, "CLR"); check(4, "PAL"); check(8, "RNG");
        check(16, "SHD"); check(32, "DRU"); check(64, "MNK"); check(128, "BRD");
        check(256, "ROG"); check(512, "SHM"); check(1024, "NEC"); check(2048, "WIZ");
        check(4096, "MAG"); check(8192, "ENC"); check(16384, "BST"); check(32768, "BER");
        
        return (string.Join(" ", classes), meets);
    }

    private (string Text, bool Meets) GetRaceString(int mask, int playerMask)
    {
        if (mask == 65535 || mask == 32767 || mask == 0) return ("[color=#00ff00]ALL[/color]", true);
        
        bool meets = (mask & playerMask) != 0;
        var races = new List<string>();
        Action<int, string> check = (bit, name) => {
            if ((mask & bit) != 0) {
                if (bit == playerMask) races.Add($"[color=#00ff00]{name}[/color]");
                else races.Add(name);
            }
        };
        
        check(1, "HUM"); check(2, "BAR"); check(4, "ERU"); check(8, "ELF");
        check(16, "HIE"); check(32, "DEF"); check(64, "HEF"); check(128, "DWF");
        check(256, "TRO"); check(512, "OGR"); check(1024, "HFL"); check(2048, "GNM");
        check(4096, "IKS"); check(8192, "VAH"); check(16384, "FRG");
        
        return (string.Join(" ", races), meets);
    }

    private string GetSlotString(int mask)
    {
        if (mask == 4194304) return "AMMO";
        var slots = new List<string>();
        if ((mask & 1) != 0) slots.Add("CHARM");
        if ((mask & 2) != 0) slots.Add("EAR");
        if ((mask & 4) != 0) slots.Add("HEAD");
        if ((mask & 8) != 0) slots.Add("FACE");
        if ((mask & 16) != 0) slots.Add("EAR");
        if ((mask & 32) != 0) slots.Add("NECK");
        if ((mask & 64) != 0) slots.Add("SHOULDERS");
        if ((mask & 128) != 0) slots.Add("ARMS");
        if ((mask & 256) != 0) slots.Add("BACK");
        if ((mask & 512) != 0) slots.Add("WRIST");
        if ((mask & 1024) != 0) slots.Add("WRIST");
        if ((mask & 2048) != 0) slots.Add("RANGE");
        if ((mask & 4096) != 0) slots.Add("HANDS");
        if ((mask & 8192) != 0) slots.Add("PRIMARY");
        if ((mask & 16384) != 0) slots.Add("SECONDARY");
        if ((mask & 32768) != 0) slots.Add("FINGERS");
        if ((mask & 65536) != 0) slots.Add("FINGERS");
        if ((mask & 131072) != 0) slots.Add("CHEST");
        if ((mask & 262144) != 0) slots.Add("LEGS");
        if ((mask & 524288) != 0) slots.Add("FEET");
        if ((mask & 1048576) != 0) slots.Add("WAIST");
        if ((mask & 2097152) != 0) slots.Add("POWER SOURCE");
        
        var distinct = new List<string>();
        foreach (var s in slots) if (!distinct.Contains(s)) distinct.Add(s);
        return string.Join(" ", distinct);
    }

    private string GetSizeString(int size)
    {
        return size switch {
            0 => "TINY",
            1 => "SMALL",
            2 => "MEDIUM",
            3 => "LARGE",
            4 => "GIANT",
            _ => "MEDIUM"
        };
    }

    private void OnTitleBarInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _isDragging = true;
                _dragOffset = GetGlobalMousePosition() - GlobalPosition;
            }
            else if (_isDragging)
            {
                _isDragging = false;
                SavePosition();
            }
        }
        else if (ev is InputEventMouseMotion && _isDragging)
        {
            GlobalPosition = GetGlobalMousePosition() - _dragOffset;
        }
    }

    private void SavePosition()
    {
        try {
            var dict = new Godot.Collections.Dictionary { ["x"] = GlobalPosition.X, ["y"] = GlobalPosition.Y };
            using var file = FileAccess.Open("user://item_window_pos.json", FileAccess.ModeFlags.Write);
            if (file != null) file.StoreString(Json.Stringify(dict));
        } catch {}
    }

    private bool LoadPosition()
    {
        try {
            if (!FileAccess.FileExists("user://item_window_pos.json")) return false;
            using var file = FileAccess.Open("user://item_window_pos.json", FileAccess.ModeFlags.Read);
            if (file != null) {
                var data = Json.ParseString(file.GetAsText()).AsGodotDictionary();
                GlobalPosition = new Vector2(data["x"].AsSingle(), data["y"].AsSingle());
                return true;
            }
        } catch {}
        return false;
    }
}
