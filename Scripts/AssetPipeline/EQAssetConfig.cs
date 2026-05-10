using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Extra directories scanned for zone *.s3d (e.g. classic dump at D:\EQ while main path is RoF2).</summary>
    private readonly List<string> _zoneSearchPaths = new();

    /// <summary>Whether the EQ path has been configured and validated.</summary>
    public bool IsConfigured => _isValidated;
    private bool _isValidated = false;

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

    private static bool HasRequiredFilesAt(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir)) return false;
        foreach (var file in RequiredFiles)
        {
            if (!System.IO.File.Exists(System.IO.Path.Combine(dir, file)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Resolves a folder the user picked (sometimes the parent launcher dir) to the directory
    /// that actually contains eqgame.exe and zone .s3d files (one level of subfolders).
    /// </summary>
    /// <returns>Full path to the game root, or null if not found.</returns>
    public string ResolveEqInstallRoot(string path, bool silent = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string trimmed = path.Trim();
        try
        {
            if (HasRequiredFilesAt(trimmed))
            {
                string full = System.IO.Path.GetFullPath(trimmed);
                if (!silent) GD.Print($"[EQ Config] Validated EQ installation at: {full}");
                return full;
            }

            int scanned = 0;
            foreach (var sub in System.IO.Directory.EnumerateDirectories(trimmed))
            {
                if (++scanned > 80) break;
                if (!HasRequiredFilesAt(sub)) continue;
                string full = System.IO.Path.GetFullPath(sub);
                if (!silent)
                    GD.Print($"[EQ Config] Validated EQ installation at: {full} (resolved from parent: {trimmed})");
                return full;
            }
        }
        catch (Exception ex)
        {
            if (!silent) GD.PrintErr($"[EQ Config] ResolveEqInstallRoot: {ex.Message}");
            return null;
        }

        if (!silent)
        {
            foreach (var file in RequiredFiles)
            {
                string fullPath = System.IO.Path.Combine(trimmed, file);
                if (!System.IO.File.Exists(fullPath))
                    GD.Print($"[EQ Config] Missing required file: {fullPath}");
            }
        }
        return null;
    }

    /// <summary>
    /// Validate that a given path contains a real EQ installation (or a direct child folder that does).
    /// </summary>
    public bool Validate(string path, bool silent = false) => ResolveEqInstallRoot(path, silent) != null;

    /// <summary>
    /// Set and persist the EQ installation path.
    /// Returns true if the path is valid.
    /// </summary>
    public bool SetPath(string path)
    {
        string resolved = ResolveEqInstallRoot(path);
        if (resolved == null)
        {
            GD.PrintErr($"[EQ Config] Invalid EQ path: {path}");
            _isValidated = false;
            return false;
        }

        EQPath = resolved;
        _isValidated = true;
        Save();
        GD.Print($"[EQ Config] EQ path set to: {resolved}");
        return true;
    }

    /// <summary>
    /// Persist extra roots for FindZoneS3dPath (paths need not pass full EQ validation).
    /// </summary>
    public void SetZoneSearchPaths(IReadOnlyList<string> paths)
    {
        _zoneSearchPaths.Clear();
        if (paths == null) return;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            string t = p.Trim();
            if (!System.IO.Directory.Exists(t)) continue;
            if (!_zoneSearchPaths.Exists(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
                _zoneSearchPaths.Add(t);
        }
        if (_isValidated) Save();
    }

    /// <summary>
    /// Unlink the EQ installation. Clears the saved path.
    /// </summary>
    public void Unlink()
    {
        EQPath = "";
        _zoneSearchPaths.Clear();
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
    /// Locate a zone archive (&lt;name&gt;.s3d) under the main EQ path and any <see cref="_zoneSearchPaths"/>.
    /// </summary>
    public string FindZoneS3dPath(string zoneBaseName)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(zoneBaseName)) return null;
        string want = zoneBaseName.Trim();

        foreach (var root in GetZoneSearchRoots())
        {
            foreach (var name in ZoneS3dSearchNames(want))
            {
                var hit = SearchZoneS3dUnderRoot(root, name);
                if (hit != null) return hit;
            }
        }

        return null;
    }

    /// <summary>PEQ / atlas keys that do not match the on-disk .s3d basename (RoF2 still ships tox.s3d for Toxxulia).</summary>
    private static IEnumerable<string> ZoneS3dSearchNames(string want)
    {
        if (string.IsNullOrWhiteSpace(want)) yield break;
        yield return want.Trim();
        if (string.Equals(want, "tox", StringComparison.OrdinalIgnoreCase))
            yield return "toxxulia";
        else if (string.Equals(want, "toxxulia", StringComparison.OrdinalIgnoreCase))
            yield return "tox";
    }

    private IEnumerable<string> GetZoneSearchRoots()
    {
        if (IsConfigured && !string.IsNullOrEmpty(EQPath) && System.IO.Directory.Exists(EQPath))
            yield return EQPath;
        foreach (var p in _zoneSearchPaths)
        {
            if (!string.IsNullOrEmpty(p) && System.IO.Directory.Exists(p))
                yield return p;
        }
    }

    /// <summary>Comma-separated roots scanned for zone .s3d (for logs).</summary>
    public string FormatZoneSearchRootsForDiagnostics()
    {
        var roots = GetZoneSearchRoots().ToList();
        return roots.Count > 0 ? string.Join(", ", roots) : "(none)";
    }

    private static string SearchZoneS3dUnderRoot(string installRoot, string want)
    {
        try
        {
            string Direct(string dir, string z)
            {
                foreach (var ext in new[] { ".s3d", ".S3d", ".S3D" })
                {
                    var p = System.IO.Path.Combine(dir, z + ext);
                    if (System.IO.File.Exists(p)) return p;
                }
                return null;
            }

            bool NameMatch(string filePath)
            {
                return string.Equals(System.IO.Path.GetFileNameWithoutExtension(filePath), want, StringComparison.OrdinalIgnoreCase);
            }

            var hit = Direct(installRoot, want);
            if (hit != null) return hit;

            foreach (var path in System.IO.Directory.EnumerateFiles(installRoot, "*.s3d"))
            {
                if (NameMatch(path)) return path;
            }

            string[] subHints = {
                "resources", "Resources", "zones", "Zones", "maps", "Maps",
                "zonemeshes", "ZoneMeshes", "assets", "Assets", "data", "Data"
            };
            var hintedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in subHints) hintedDirs.Add(s);

            foreach (var sub in subHints)
            {
                string d = System.IO.Path.Combine(installRoot, sub);
                if (!System.IO.Directory.Exists(d)) continue;
                hit = Direct(d, want);
                if (hit != null) return hit;
                foreach (var path in System.IO.Directory.EnumerateFiles(d, "*.s3d"))
                {
                    if (NameMatch(path)) return path;
                }
            }

            foreach (var dir in System.IO.Directory.EnumerateDirectories(installRoot))
            {
                if (hintedDirs.Contains(System.IO.Path.GetFileName(dir))) continue;
                hit = Direct(dir, want);
                if (hit != null) return hit;
                foreach (var path in System.IO.Directory.EnumerateFiles(dir, "*.s3d"))
                {
                    if (NameMatch(path)) return path;
                }
            }

            foreach (var dir in System.IO.Directory.EnumerateDirectories(installRoot))
            {
                foreach (var dir2 in System.IO.Directory.EnumerateDirectories(dir))
                {
                    hit = Direct(dir2, want);
                    if (hit != null) return hit;
                    foreach (var path in System.IO.Directory.EnumerateFiles(dir2, "*.s3d"))
                    {
                        if (NameMatch(path)) return path;
                    }
                }
            }

            // Deeper layouts (some installs nest zones under extra folders).
            hit = SearchZoneS3dBfs(installRoot, want, maxDepth: 12, maxDirsVisited: 12000);
            if (hit != null) return hit;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EQ Config] SearchZoneS3dUnderRoot({installRoot}): {ex.Message}");
        }

        return null;
    }

    /// <summary>Breadth-first search for &lt;want&gt;.s3d under <paramref name="root"/>.</summary>
    private static string SearchZoneS3dBfs(string root, string want, int maxDepth, int maxDirsVisited)
    {
        try
        {
            string Direct(string dir, string z)
            {
                foreach (var ext in new[] { ".s3d", ".S3d", ".S3D" })
                {
                    var p = System.IO.Path.Combine(dir, z + ext);
                    if (System.IO.File.Exists(p)) return p;
                }
                return null;
            }

            bool NameMatch(string filePath) =>
                string.Equals(System.IO.Path.GetFileNameWithoutExtension(filePath), want, StringComparison.OrdinalIgnoreCase);

            var q = new Queue<(string path, int depth)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            q.Enqueue((root, 0));
            seen.Add(System.IO.Path.GetFullPath(root));
            int visited = 0;

            while (q.Count > 0 && visited < maxDirsVisited)
            {
                var (dir, depth) = q.Dequeue();
                visited++;

                var hit = Direct(dir, want);
                if (hit != null) return hit;
                foreach (var path in System.IO.Directory.EnumerateFiles(dir, "*.s3d"))
                {
                    if (NameMatch(path)) return path;
                }

                if (depth >= maxDepth) continue;

                foreach (var sub in System.IO.Directory.EnumerateDirectories(dir))
                {
                    string full = System.IO.Path.GetFullPath(sub);
                    if (seen.Add(full))
                        q.Enqueue((sub, depth + 1));
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EQ Config] SearchZoneS3dBfs: {ex.Message}");
        }

        return null;
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
            string json;
            var opts = new JsonSerializerOptions { WriteIndented = true };
            if (_zoneSearchPaths.Count > 0)
                json = JsonSerializer.Serialize(new { eqPath = EQPath, zoneSearchPaths = _zoneSearchPaths.ToArray() }, opts);
            else
                json = JsonSerializer.Serialize(new { eqPath = EQPath }, opts);

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

            bool resaveEqPath = false;
            if (doc.RootElement.TryGetProperty("eqPath", out var pathProp))
            {
                string path = pathProp.GetString();
                string resolved = !string.IsNullOrEmpty(path) ? ResolveEqInstallRoot(path, silent: true) : null;
                if (resolved != null)
                {
                    EQPath = resolved;
                    _isValidated = true;
                    GD.Print($"[EQ Config] Loaded saved EQ path: {resolved}");
                    if (!string.Equals(path.Trim(), resolved, StringComparison.OrdinalIgnoreCase))
                        resaveEqPath = true;
                }
                else
                {
                    GD.Print("[EQ Config] Saved path no longer valid.");
                }
            }

            _zoneSearchPaths.Clear();
            if (doc.RootElement.TryGetProperty("zoneSearchPaths", out var zsp) && zsp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in zsp.EnumerateArray())
                {
                    string s = el.GetString()?.Trim();
                    if (string.IsNullOrEmpty(s) || !System.IO.Directory.Exists(s)) continue;
                    if (!_zoneSearchPaths.Exists(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase)))
                        _zoneSearchPaths.Add(s);
                }
                if (_zoneSearchPaths.Count > 0)
                    GD.Print($"[EQ Config] Extra zone search path(s): {string.Join(", ", _zoneSearchPaths)}");
            }

            if (resaveEqPath && _isValidated)
                Save();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EQ Config] Failed to load: {ex.Message}");
        }
    }
}
