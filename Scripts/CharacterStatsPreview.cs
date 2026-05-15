using System;
using System.Collections.Generic;

/// <summary>
/// Level 1+ HP/mana preview for character creation — mirrors server/combat.js (calcMaxHP, calcMaxMana).
/// </summary>
public static class CharacterStatsPreview
{
    private readonly struct HpEntry
    {
        public readonly int BaseHp;
        public readonly int LevelMult;
        public readonly double StaFactor;

        public HpEntry(int baseHp, int levelMult, double staFactor)
        {
            BaseHp = baseHp;
            LevelMult = levelMult;
            StaFactor = staFactor;
        }
    }

    private static readonly Dictionary<string, HpEntry> ClassHpTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["warrior"] = new(20, 14, 0.1000),
        ["paladin"] = new(18, 13, 0.0933),
        ["shadow_knight"] = new(18, 13, 0.0933),
        ["ranger"] = new(16, 12, 0.0800),
        ["monk"] = new(18, 13, 0.0800),
        ["rogue"] = new(15, 12, 0.0800),
        ["bard"] = new(15, 12, 0.0800),
        ["beastlord"] = new(16, 12, 0.0800),
        ["cleric"] = new(14, 10, 0.0666),
        ["druid"] = new(12, 9, 0.0666),
        ["shaman"] = new(14, 10, 0.0666),
        ["wizard"] = new(10, 7, 0.0400),
        ["magician"] = new(10, 7, 0.0400),
        ["necromancer"] = new(10, 7, 0.0400),
        ["enchanter"] = new(10, 7, 0.0400),
    };

    private static readonly Dictionary<string, string> ClassManaStat = new(StringComparer.OrdinalIgnoreCase)
    {
        ["warrior"] = null,
        ["rogue"] = null,
        ["monk"] = null,
        ["berserker"] = null,
        ["cleric"] = "wis",
        ["druid"] = "wis",
        ["shaman"] = "wis",
        ["paladin"] = "wis",
        ["ranger"] = "wis",
        ["beastlord"] = "wis",
        ["wizard"] = "intel",
        ["magician"] = "intel",
        ["necromancer"] = "intel",
        ["enchanter"] = "intel",
        ["shadow_knight"] = "intel",
        ["bard"] = "intel",
    };

    public static int CalcMaxHp(string classKey, int level, int stamina)
    {
        if (!ClassHpTable.TryGetValue(classKey ?? "", out var entry))
            entry = ClassHpTable["warrior"];

        int levelHp = Math.Max(0, level - 1) * entry.LevelMult;
        int staHp = (int)Math.Floor(stamina * level * entry.StaFactor);
        return entry.BaseHp + levelHp + staHp;
    }

    public static int CalcMaxMana(string classKey, int level, int wis, int intel)
    {
        if (!ClassManaStat.TryGetValue(classKey ?? "", out string statName) || statName == null)
            return 0;

        int castingStat = statName == "wis" ? wis : intel;
        if (castingStat <= 0)
            castingStat = 75;

        int mana;
        if (castingStat <= 200)
            mana = (int)Math.Floor((80.0 * level * castingStat) / 425.0);
        else
        {
            int baseStatMana = (int)Math.Floor((80.0 * level * 200) / 425.0);
            int extraStatMana = (int)Math.Floor((40.0 * level * (castingStat - 200)) / 425.0);
            mana = baseStatMana + extraStatMana;
        }

        return mana + 10;
    }
}
