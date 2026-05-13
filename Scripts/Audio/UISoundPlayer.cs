using Godot;
using System;

/// <summary>
/// Global 2D audio player for non-positional UI sounds (level dings, loot,
/// fizzles, button clicks). Singleton — instantiated by MainUI.
/// </summary>
public partial class UISoundPlayer : Node
{
    private static UISoundPlayer _instance;
    public static UISoundPlayer Instance => _instance;

    private AudioStreamPlayer _player;
    private AudioStreamPlayer _secondaryPlayer; // For overlapping sounds

    public override void _Ready()
    {
        _instance = this;

        // MainUI can initialize before WorldManager; create buses so Bus = "SFX" is valid.
        ZoneMusicPlayer.EnsureAudioBuses();

        _player = new AudioStreamPlayer();
        _player.Name = "UISfx1";
        _player.Bus = "SFX";
        _player.VolumeDb = -6f; // Slightly below full to avoid clipping
        AddChild(_player);

        _secondaryPlayer = new AudioStreamPlayer();
        _secondaryPlayer.Name = "UISfx2";
        _secondaryPlayer.Bus = "SFX";
        _secondaryPlayer.VolumeDb = -6f;
        AddChild(_secondaryPlayer);
    }

    /// <summary>
    /// Play any sound file from the EQ sounds/ directory.
    /// Non-positional 2D playback — for UI events that aren't tied to 3D entities.
    /// </summary>
    public void PlaySound(string soundFile, float volumeDb = 0f)
    {
        var stream = EQAssetCache.Instance.GetSound(soundFile);
        if (stream == null) return;

        // Use the secondary player if the primary is busy
        var player = _player.Playing ? _secondaryPlayer : _player;
        player.VolumeDb = volumeDb;
        player.Stream = stream;
        player.Play();
    }

    /// <summary>Play the first available file from <paramref name="soundFiles"/> (EQ <c>sounds/</c> names).</summary>
    public void PlaySoundFirstAvailable(float volumeDb, params string[] soundFiles)
    {
        if (soundFiles == null || soundFiles.Length == 0) return;
        foreach (string name in soundFiles)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var stream = EQAssetCache.Instance.GetSound(name.Trim());
            if (stream == null) continue;
            var player = _player.Playing ? _secondaryPlayer : _player;
            player.VolumeDb = volumeDb;
            player.Stream = stream;
            player.Play();
            return;
        }
    }

    // ── Convenience Methods ─────────────────────────────────────

    /// <summary>Level up "DING!" — the classic EQ achievement chime.</summary>
    public void PlayLevelUp()
    {
        PlaySound("achievement.wav", 0f);
    }

    /// <summary>Loot/coin pickup sound.</summary>
    public void PlayLoot()
    {
        PlaySound("btn_flp.wav", -8f); // Subtle click/flip
    }

    /// <summary>Spell fizzle sound.</summary>
    public void PlayFizzle()
    {
        PlaySound("CHT_cast.wav", -6f);
    }

    /// <summary>XP gain — subtle chime.</summary>
    public void PlayXpGain()
    {
        PlaySound("bell001.wav", -12f);
    }

    /// <summary>Door/chest opening.</summary>
    public void PlayOpen()
    {
        PlaySound("cst_opn.wav", -6f);
    }

    /// <summary>Inventory / bank bag window open (classic UI).</summary>
    public void PlayBagOpen()
    {
        PlaySoundFirstAvailable(-5f, "wind_opn.wav", "windowopen.wav", "invopen.wav", "gen_open.wav", "cst_opn.wav");
    }

    /// <summary>Inventory / bank bag window close.</summary>
    public void PlayBagClose()
    {
        PlaySoundFirstAvailable(-5f, "wind_cls.wav", "windowclose.wav", "invclose.wav", "gen_close.wav", "cst_cls.wav");
    }

    /// <summary>Picking up a normal item onto the cursor.</summary>
    public void PlayItemPickUp()
    {
        PlaySoundFirstAvailable(-6f, "gen_pickup.wav", "pickup.wav", "item_pickup.wav", "btn_flp.wav");
    }

    /// <summary>Dropping a held item into a slot (or swap).</summary>
    public void PlayItemPutDown()
    {
        PlaySoundFirstAvailable(-6f, "gen_drop.wav", "putdown.wav", "item_drop.wav", "btn_flp.wav");
    }

    /// <summary>Picking up coin currency.</summary>
    public void PlayCoinPickUp()
    {
        PlaySoundFirstAvailable(-4f, "coins.wav", "coin.wav", "money.wav", "gen_pickup.wav");
    }

    /// <summary>Dropping coin into a slot.</summary>
    public void PlayCoinPutDown()
    {
        PlaySoundFirstAvailable(-4f, "dropcoins.wav", "coins.wav", "coin.wav", "gen_drop.wav");
    }

    // ── Weapon Impact Sounds (2D fallback for when entity is out of range) ──

    /// <summary>
    /// Play a weapon impact sound based on attack type.
    /// Maps EQ combat text types to the DKM_* weapon sound files.
    /// </summary>
    public void PlayWeaponImpact(string attackType)
    {
        string file = MapAttackToSound(attackType);
        PlaySound(file, -8f);
    }

    /// <summary>
    /// Map EQ attack type strings to weapon impact sound files.
    /// Uses the DKM_* (Drakkin Male) impact sounds as universal weapon SFX.
    /// </summary>
    public static string MapAttackToSound(string attackType)
    {
        switch (attackType?.ToLower())
        {
            case "slash":
            case "slice":
            case "slashing":
                return "DKM_PrimarySlashAtk.wav";
            case "pierce":
            case "piercing":
            case "stab":
                return "DKM_PrimaryStab.wav";
            case "crush":
            case "crushing":
            case "blunt":
                return "DKM_PrimaryBluntAtk.wav";
            case "bash":
                return "DKM_Bash.wav";
            case "kick":
                return "DKM_Kick.wav";
            case "punch":
            case "hand to hand":
            case "h2h":
                return "DKM_Punch.wav";
            case "backstab":
                return "DKM_PrimaryStab.wav";
            case "2hslash":
            case "2h slash":
                return "DKM_2HSlashAtk.wav";
            case "2hblunt":
            case "2h blunt":
                return "DKM_2HBluntAtk.wav";
            case "archery":
            case "bow":
            case "arrow":
                return "DKM_FireBow.wav";
            case "roundkick":
            case "round kick":
                return "DKM_RoundHouseKick.wav";
            case "flying kick":
            case "flyingkick":
                return "DKM_MonkSpecialKick.wav";
            case "eagle strike":
            case "tiger claw":
            case "dragon punch":
                return "DKM_MonkHandAtk1.wav";
            default:
                return "DKM_PrimarySlashAtk.wav"; // Default fallback
        }
    }
}
