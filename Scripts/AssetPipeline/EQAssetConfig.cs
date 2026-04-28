using Godot;
using System;
using System.Text.Json;

/// <summary>
/// Persists the player's EQ Live installation path across sessions.
/// Provides validation and relink capability.
/// </summary>
public partial class EQAssetConfig : RefCounted
{
    private static EQAssetConfig _instance;
    public static EQAssetConfig Instance => _instance ??= new EQAssetConfig();

    private const string ConfigPath = "user://eq_config.json";

    /// <summary>The validated path to the EQ installation directory.</summary>
    public string EQPath { get; private set; } = "";

    /// <summary>Whether the EQ path has been configured and validated.</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(EQPath) && Validate(EQPath);

    // Key files that must exist in a valid EQ installation
    private static readonly string[] RequiredFiles = {
        "eqgame.exe",          // EQ executable
        "gfaydark.s3d",        // Classic zone (sanity check)
        "global_chr.s3d",      // Global character models
    };

    // Files that should exist but aren't strictly required
    private static readonly string[] OptionalFiles = {
        "butcher.s3d",         // Butcherblock Mountains
        "crushbone.s3d",       // Crushbone
    };

    public EQAssetConfig()
    {
        Load();
    }

    /// <summary>
    /// Validate that a given path contains a real EQ installation.
    /// Returns true if all required marker files are found.
    /// </summary>
    public bool Validate(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        // Use Godot's DirAccess to check — works cross-platform
        foreach (var file in RequiredFiles)
        {
            string fullPath = System.IO.Path.Combine(path, file);
            if (!System.IO.File.Exists(fullPath))
            {
                GD.Print($"[EQ Config] Missing required file: {fullPath}");
                return false;
            }
        }

        GD.Print($"[EQ Config] Validated EQ installation at: {path}");
        return true;
    }

    /// <summary>
    /// Set and persist the EQ installation path.
    /// Returns true if the path is valid.
    /// </summary>
    public bool SetPath(string path)
    {
        if (!Validate(path))
        {
            GD.PrintErr($"[EQ Config] Invalid EQ path: {path}");
            return false;
        }

        EQPath = path;
        Save();
        GD.Print($"[EQ Config] EQ path set to: {path}");
        return true;
    }

    /// <summary>
    /// Unlink the EQ installation. Clears the saved path.
    /// </summary>
    public void Unlink()
    {
        EQPath = "";
        Save();
        GD.Print("[EQ Config] EQ path unlinked.");
    }

    /// <summary>
    /// Get the full path to a specific EQ file (zone S3D, character model, etc.)
    /// </summary>
    public string GetEQFilePath(string filename)
    {
        if (!IsConfigured) return null;
        return System.IO.Path.Combine(EQPath, filename);
    }

    /// <summary>
    /// Check if a specific EQ file exists in the installation.
    /// </summary>
    public bool HasEQFile(string filename)
    {
        if (!IsConfigured) return false;
        return System.IO.File.Exists(System.IO.Path.Combine(EQPath, filename));
    }

    /// <summary>
    /// Get a summary of what's available in the linked EQ installation.
    /// </summary>
    public (int zoneCount, int charCount, int musicCount) GetAssetSummary()
    {
        if (!IsConfigured) return (0, 0, 0);

        int zones = 0, chars = 0, music = 0;
        foreach (var file in System.IO.Directory.GetFiles(EQPath))
        {
            string ext = System.IO.Path.GetExtension(file).ToLower();
            string name = System.IO.Path.GetFileNameWithoutExtension(file).ToLower();

            if (ext == ".s3d" && !name.Contains("_chr") && !name.Contains("_obj") && !name.Contains("global"))
                zones++;
            else if (ext == ".s3d" && (name.Contains("_chr") || name.Contains("global")))
                chars++;
            else if (ext == ".mp3" || ext == ".xmi")
                music++;
        }

        return (zones, chars, music);
    }

    // ── Persistence ──────────────────────────────────────────────

    private void Save()
    {
        try
        {
            var data = new { eqPath = EQPath };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

            using var file = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Write);
            if (file != null)
            {
                file.StoreString(json);
                GD.Print("[EQ Config] Saved configuration.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EQ Config] Failed to save: {ex.Message}");
        }
    }

    private void Load()
    {
        try
        {
            if (!FileAccess.FileExists(ConfigPath))
            {
                GD.Print("[EQ Config] No saved configuration found.");
                return;
            }

            using var file = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Read);
            if (file == null) return;

            string json = file.GetAsText();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("eqPath", out var pathProp))
            {
                string path = pathProp.GetString();
                if (!string.IsNullOrEmpty(path) && Validate(path))
                {
                    EQPath = path;
                    GD.Print($"[EQ Config] Loaded saved EQ path: {path}");
                }
                else
                {
                    GD.Print("[EQ Config] Saved path no longer valid.");
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EQ Config] Failed to load: {ex.Message}");
        }
    }
}
