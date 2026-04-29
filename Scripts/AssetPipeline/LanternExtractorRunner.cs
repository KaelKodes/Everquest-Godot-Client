using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Wraps LanternExtractor.exe to extract EQ assets on demand.
/// Dynamically writes settings.txt, runs the process, and copies
/// output to the session cache.
/// </summary>
public partial class LanternExtractorRunner : RefCounted
{
    private static LanternExtractorRunner _instance;
    public static LanternExtractorRunner Instance => _instance ??= new LanternExtractorRunner();

    // Path to the bundled LanternExtractor executable
    // In exported builds this would be in the app directory
    private string _lanternDir;
    private string _lanternExe;
    
    private readonly System.Threading.SemaphoreSlim _extractLock = new System.Threading.SemaphoreSlim(1, 1);

    private static readonly System.Collections.Generic.Dictionary<string, string> _zoneAliases = new()
    {
        { "steamfontmts", "steamfont" },
        { "qeynos2", "qeynos" },
        { "highpasskeep", "highkeep" },
        { "kithforest", "kithicor" },
        { "neriakd", "neriakc" },
        { "oldhighpass", "highpass" },
        { "befallenb", "befallen" }
    };

    public LanternExtractorRunner()
    {
        // Locate LanternExtractor — shipped alongside the game
        // Try multiple possible locations
        string appDir = OS.GetExecutablePath().GetBaseDir();
        string[] searchPaths = {
            Path.Combine(appDir, "LanternExtractor"),
            Path.Combine(appDir, "tools", "LanternExtractor"),
            // Development path
            Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "server", "tools", "LanternExtractor"),
        };

        foreach (var dir in searchPaths)
        {
            string exe = Path.Combine(dir, "LanternExtractor.exe");
            if (File.Exists(exe))
            {
                _lanternDir = dir;
                _lanternExe = exe;
                GD.Print($"[Lantern] Found LanternExtractor at: {exe}");
                break;
            }
        }

        if (_lanternExe == null)
        {
            GD.PrintErr("[Lantern] LanternExtractor.exe not found! Asset extraction will not work.");
        }
    }

    /// <summary>Whether LanternExtractor is available.</summary>
    public bool IsAvailable => _lanternExe != null;

    /// <summary>
    /// Extract a single zone from the EQ installation to the session cache.
    /// Returns true on success.
    /// </summary>
    public async Task<bool> ExtractZone(string zoneId)
    {
        await _extractLock.WaitAsync();
        try
        {
            if (!IsAvailable)
        {
            GD.PrintErr("[Lantern] Cannot extract — LanternExtractor not found.");
            return false;
        }

        var config = EQAssetConfig.Instance;
        if (!config.IsConfigured)
        {
            GD.PrintErr("[Lantern] Cannot extract — EQ directory not configured.");
            return false;
        }

        var cache = EQAssetCache.Instance;
        if (cache.HasZone(zoneId))
        {
            GD.Print($"[Lantern] Zone '{zoneId}' already cached, skipping extraction.");
            return true;
        }

        string zone = zoneId.ToLower();
        string extractTarget = _zoneAliases.TryGetValue(zone, out string alias) ? alias : zone;

        // Write settings.txt to configure LanternExtractor
        WriteSettings(config.EQPath);

        GD.Print($"[Lantern] Extracting zone: {extractTarget} (mapped from {zone})...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        bool success = await RunExtractor(extractTarget);
        sw.Stop();

        if (!success)
        {
            GD.PrintErr($"[Lantern] Extraction failed for zone: {extractTarget}");
            return false;
        }

        // Copy extracted files from Lantern's Exports dir to the session cache
        string exportDir = Path.Combine(_lanternDir, "Exports", extractTarget);
        string cacheDir = cache.GetZonePath(zone);

        if (!Directory.Exists(exportDir))
        {
            GD.PrintErr($"[Lantern] Export directory not found: {exportDir}");
            return false;
        }

        CopyDirectory(exportDir, cacheDir);

        // If an alias was used, rename the core files to match the expected zoneId
        if (zone != extractTarget)
        {
            string zoneDir = Path.Combine(cacheDir, "Zone");
            if (Directory.Exists(zoneDir))
            {
                string oldGlb = Path.Combine(zoneDir, $"{extractTarget}.glb");
                string newGlb = Path.Combine(zoneDir, $"{zone}.glb");
                if (File.Exists(oldGlb)) File.Move(oldGlb, newGlb);
            }
        }

        cache.MarkZoneExtracted(zone);

        GD.Print($"[Lantern] Zone '{zone}' extracted in {sw.Elapsed.TotalSeconds:F1}s");

        // Also copy zone music if available
        CopyZoneMusic(zone, config.EQPath);

        return true;
        }
        finally
        {
            _extractLock.Release();
        }
    }

    /// <summary>
    /// Extract global character models to the session cache.
    /// </summary>
    public async Task<bool> ExtractCharacters()
    {
        await _extractLock.WaitAsync();
        try
        {
            if (!IsAvailable || !EQAssetConfig.Instance.IsConfigured) return false;

            WriteSettings(EQAssetConfig.Instance.EQPath);

            GD.Print("[Lantern] Extracting global character models...");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Extract the global character archives
            bool success = await RunExtractor("global");
            sw.Stop();

            if (!success)
            {
                GD.PrintErr("[Lantern] Character extraction failed.");
                return false;
            }

            // Copy GLBs from Exports/global/Characters to cache
            string exportDir = Path.Combine(_lanternDir, "Exports", "global", "Characters");
            string cacheDir = EQAssetCache.Instance.GetCharactersDir();

            if (Directory.Exists(exportDir))
            {
                CopyDirectory(exportDir, cacheDir);
                // Mark all extracted character codes
                foreach (var glb in Directory.GetFiles(cacheDir, "*.glb"))
                {
                    string code = Path.GetFileNameWithoutExtension(glb);
                    EQAssetCache.Instance.MarkCharacterExtracted(code);
                }
            }

            GD.Print($"[Lantern] Characters extracted in {sw.Elapsed.TotalSeconds:F1}s");
            return true;
        }
        finally
        {
            _extractLock.Release();
        }
    }

    // ── Private ─────────────────────────────────────────────────

    /// <summary>
    /// Write the settings.txt file that LanternExtractor reads.
    /// </summary>
    private void WriteSettings(string eqPath)
    {
        string settingsPath = Path.Combine(_lanternDir, "settings.txt");
        string settings = $@"EverQuestDirectory={eqPath}
ModelExportFormat=2
ExportAllAnimationFrames=true
ExportGltfInGlbFormat=true
ExportCharacterToSingleFolder=true
ExportGltfVertexColors=false
ExportZoneMeshGroups=false
ExportHiddenGeometry=false
LoggerVerbosity=0
";
        File.WriteAllText(settingsPath, settings);
    }

    /// <summary>
    /// Run LanternExtractor as a child process with the given argument.
    /// </summary>
    private Task<bool> RunExtractor(string argument)
    {
        return Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _lanternExe,
                    Arguments = argument,
                    WorkingDirectory = _lanternDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();

                process.WaitForExit(60000); // 60 second timeout

                if (!string.IsNullOrEmpty(stdout))
                    GD.Print($"[Lantern] {stdout.Trim()}");
                if (!string.IsNullOrEmpty(stderr))
                    GD.PrintErr($"[Lantern] Error: {stderr.Trim()}");

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Lantern] Process error: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Copy zone music files (MP3/XMI) from the EQ directory to the cache.
    /// </summary>
    private void CopyZoneMusic(string zoneId, string eqPath)
    {
        string musicDir = Path.Combine(EQAssetCache.Instance.CacheRoot, "music");
        Directory.CreateDirectory(musicDir);

        // Try MP3 first
        string mp3Source = Path.Combine(eqPath, $"{zoneId}.mp3");
        if (File.Exists(mp3Source))
        {
            string dest = Path.Combine(musicDir, $"{zoneId}.mp3");
            File.Copy(mp3Source, dest, overwrite: true);
            GD.Print($"[Lantern] Copied zone music: {zoneId}.mp3");
            return;
        }

        // Fallback: XMI (MIDI)
        string xmiSource = Path.Combine(eqPath, $"{zoneId}.xmi");
        if (File.Exists(xmiSource))
        {
            string dest = Path.Combine(musicDir, $"{zoneId}.xmi");
            File.Copy(xmiSource, dest, overwrite: true);
            GD.Print($"[Lantern] Copied zone music: {zoneId}.xmi");
        }
    }

    /// <summary>
    /// Recursively copy a directory tree.
    /// </summary>
    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
        {
            string destFile = Path.Combine(dest, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            string destDir = Path.Combine(dest, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }
}
