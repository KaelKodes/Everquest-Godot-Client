const fs = require('fs');

const path = 'd:\\Kael Kodes\\EQMUD\\eqmud\\Scenes\\MainUI.tscn';
let data = fs.readFileSync(path, 'utf8');

// The Goal:
// 1. Move SpellBar so it's a child of MainHBox, immediately before MiddleColumn.
// 2. Clear out ActionPanel's old SpellBar dependency since it moved.
// 3. Add Macro Grid to ActionPanel (now named MacroPanel).
// 4. Add Abilities Button.
// 5. Add Camp Button.

// 1. Move SpellBar. We find the block for SpellBar and all its buttons.
const spellBarRegex = /(\[node name="SpellBar" type="VBoxContainer" parent="MainHBox\/LeftColumn\/ActionPanel"[\s\S]*?)(?=\[node name="MiddleColumn")/;
const match = data.match(spellBarRegex);

if (match) {
    let spellBarBlock = match[1];
    data = data.replace(spellBarBlock, ''); // Remove from LeftColumn

    spellBarBlock = spellBarBlock.replace(/parent="MainHBox\/LeftColumn\/ActionPanel"/g, 'parent="MainHBox"');
    
    // Inject it right before MiddleColumn
    data = data.replace(/\[node name="MiddleColumn" type="VBoxContainer" parent="MainHBox"/, spellBarBlock + '\n[node name="MiddleColumn" type="VBoxContainer" parent="MainHBox"');
}

// 2. Rename ActionPanel to MacroPanel
data = data.replace(/name="ActionPanel"/g, 'name="MacroPanel"');
data = data.replace(/LeftColumn\/ActionPanel/g, 'LeftColumn/MacroPanel');

// 3. Add Macro Grid to MacroPanel
let macroGrid = `
[node name="MacroGrid" type="GridContainer" parent="MainHBox/LeftColumn/MacroPanel"]
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

[node name="BtnAFK" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid"]
layout_mode = 2
size_flags_horizontal = 3
text = "Afk"
[node name="BtnFB" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid"]
layout_mode = 2
size_flags_horizontal = 3
text = "Feedback"
[node name="BtnAnon" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid"]
layout_mode = 2
size_flags_horizontal = 3
text = "Anon"
[node name="BtnHail" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid"]
layout_mode = 2
size_flags_horizontal = 3
text = "Hail"
[node name="BtnSplit" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid"]
layout_mode = 2
size_flags_horizontal = 3
text = "Split"
[node name="BtnPlayed" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid"]
layout_mode = 2
size_flags_horizontal = 3
text = "Played"
[node name="BtnConsider" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid"]
layout_mode = 2
size_flags_horizontal = 3
text = "Consider"
[node name="BtnDuel" type="Button" parent="MainHBox/LeftColumn/MacroPanel/MacroGrid"]
layout_mode = 2
size_flags_horizontal = 3
text = "Duel"
`;

data = data.replace(/(\[node name="MacroPanel" [^]*?theme_override_styles\/panel = SubResource\("StyleBoxFlat_dark"\)\n)/, '$1' + macroGrid + '\n');

// 4. Add Abilities Button
let abilitiesBtn = `
[node name="AbilitiesBtn" type="Button" parent="MainHBox/LeftColumn/MenuPanel/VBox"]
layout_mode = 2
text = "ABILITIES"
`;
data = data.replace(/(\[node name="SpellsBtn" [^]*?text = "SPELLS"\n)/, '$1' + abilitiesBtn + '\n');

// 5. Add Camp Button and format CommonButtonsGrid
let campBtn = `
[node name="CampBtn" type="Button" parent="MainHBox/RightColumn/CommonButtonsGrid"]
unique_name_in_owner = true
layout_mode = 2
size_flags_horizontal = 3
text = "Camp"
`;

// wait, is CommonButtonsGrid defined? Let's carefully insert CampBtn inside it.
data = data.replace(/(\[node name="InventoryBtn" [^]*?text = "Inventory"\n)/, '$1' + campBtn + '\n');

fs.writeFileSync(path, data, 'utf8');
console.log("MainUI.tscn successfully updated via JS parser.");
