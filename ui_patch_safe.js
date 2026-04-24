const fs = require('fs');

const path = 'd:\\Kael Kodes\\EQMUD\\eqmud\\Scenes\\MainUI.tscn';
let data = fs.readFileSync(path, 'utf8');

// 1. Move the spellbar out of ActionPanel to MainHBox
data = data.replace(
    /\[node name="SpellBar" type="VBoxContainer" parent="MainHBox\/LeftColumn\/ActionPanel"/g,
    '[node name="SpellBar" type="VBoxContainer" parent="MainHBox"'
);

// 2. Adjust Slot1 through Slot8 parent
data = data.replace(
    /parent="MainHBox\/LeftColumn\/ActionPanel\/SpellBar"/g,
    'parent="MainHBox/SpellBar"'
);

// 3. Rename ActionPanel to MacroPanel
data = data.replace(
    /\[node name="ActionPanel" type="Panel" parent="MainHBox\/LeftColumn"/g,
    '[node name="MacroPanel" type="Panel" parent="MainHBox/LeftColumn"'
);

// 4. Inject MacroGrid under MacroPanel
const macroGrid = `
[node name="MacroGrid" type="GridContainer" parent="MainHBox/LeftColumn/MacroPanel" unique_id=987654321]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 10.0
offset_top = 10.0
offset_right = -10.0
offset_bottom = -10.0
grow_horizontal = 2
grow_vertical = 2
columns = 2

[node name="BtnAFK" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid" unique_id=987654322]
layout_mode = 2
size_flags_horizontal = 3
text = "Afk"
[node name="BtnFB" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid" unique_id=987654323]
layout_mode = 2
size_flags_horizontal = 3
text = "Feedback"
[node name="BtnAnon" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid" unique_id=987654324]
layout_mode = 2
size_flags_horizontal = 3
text = "Anon"
[node name="BtnHail" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid" unique_id=987654325]
layout_mode = 2
size_flags_horizontal = 3
text = "Hail"
[node name="BtnSplit" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid" unique_id=987654326]
layout_mode = 2
size_flags_horizontal = 3
text = "Split"
[node name="BtnPlayed" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid" unique_id=987654327]
layout_mode = 2
size_flags_horizontal = 3
text = "Played"
[node name="BtnConsider" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid" unique_id=987654328]
layout_mode = 2
size_flags_horizontal = 3
text = "Consider"
[node name="BtnDuel" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid" unique_id=987654329]
layout_mode = 2
size_flags_horizontal = 3
text = "Duel"
`;

// Insert the MacroGrid directly into MacroPanel using string replacement on the end of MacroPanel's definition block
data = data.replace(
    /(theme_override_styles\/panel = SubResource\("StyleBoxFlat_dark"\))/g,
    function(match, p1, offset, string) {
        // Only do this for the ActionPanel which is now MacroPanel
        // Since we have multiple matches for that string, let's find the one right near MacroPanel
        const precedingText = string.substring(Math.max(0, offset - 200), offset);
        if (precedingText.includes('name="MacroPanel"')) {
            return match + '\n' + macroGrid;
        }
        return match;
    }
);

// 5. Add Abilities Button
let abilitiesBtn = `
[node name="AbilitiesBtn" type="Button" parent="MainHBox/LeftColumn/MenuPanel/VBox" unique_id=987654330]
layout_mode = 2
text = "ABILITIES"
`;
data = data.replace(/(\[node name="SpellsBtn" [^]*?text = "SPELLS"\n)/, '$1' + abilitiesBtn + '\n');

// 6. Add Camp Button and format CommonButtonsGrid
let campBtn = `
[node name="CampBtn" type="Button" parent="MainHBox/RightColumn/CommonButtonsGrid" unique_id=987654331]
unique_name_in_owner = true
layout_mode = 2
size_flags_horizontal = 3
text = "Camp"
`;

data = data.replace(/(\[node name="InventoryBtn" [^]*?text = "Inventory"\n)/, '$1' + campBtn + '\n');


fs.writeFileSync(path, data, 'utf8');
console.log("Safe UI Patch applied.");
