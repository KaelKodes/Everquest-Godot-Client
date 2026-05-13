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

    private static bool _warnedTradeskillEqgMissing;
    private static bool _warnedTradeskillEqgNotLanternExportable;
    private static bool _loggedTradeskillExternal;

    /// <summary>Clears the session marker so tradeskill resolution re-runs after config changes.</summary>
    public static void InvalidateTradeskillCacheMarker()
    {
        try
        {
            string marker = Path.Combine(EQAssetCache.Instance.CacheRoot, "shared", "tradeskill_objects", ".lantern_ok");
            if (File.Exists(marker))
                File.Delete(marker);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Lantern] Could not reset tradeskill marker: {ex.Message}");
        }
    }

    /// <summary>PEQ short_name → on-disk .s3d basename. Keep in sync with <c>LANTERN_ARCHIVE_ALIASES</c> in <c>server/eqemu_db.js</c>.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> _zoneAliases = new()
    {
        { "steamfontmts", "steamfont" },
        { "qeynos2", "qeynos" },
        { "highpasskeep", "highkeep" },
        { "kithforest", "kithicor" },
        { "neriakd", "neriakc" },
        { "oldhighpass", "highpass" },
        { "befallenb", "befallen" },
        { "fayrtrt", "qrg" },
        // RoF2 / atlas may use toxxulia; classic zone mesh is still tox.s3d
        { "toxxulia", "tox" },
    };

    /// <summary>When STATUS still sends <c>zone_95100</c> (numeric PEQ id), map to logical short_name for cache + GLB paths. Keep in sync with <c>ZONES_NUM_FALLBACK</c> in server <c>eqemu_db.js</c>.</summary>
    private static readonly System.Collections.Generic.Dictionary<int, string> s_zoneIdNumberToShort = new()
    {
        [95100] = "fayrtrt",
    };

    /// <summary>PEQ numeric id form <c>zone_95100</c> → short_name for cache paths and extraction. Call from UI when applying STATUS.</summary>
    public static string NormalizeZoneId(string zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) return zoneId;
        var z = zoneId.Trim().ToLowerInvariant();
        const string prefix = "zone_";
        if (z.StartsWith(prefix, StringComparison.Ordinal) && z.Length > prefix.Length)
        {
            var tail = z[prefix.Length..];
            if (int.TryParse(tail, out int n) && s_zoneIdNumberToShort.TryGetValue(n, out var sn))
                return sn;
        }
        return z;
    }

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
    /// <paramref name="lanternArchiveBase"/> overrides the .s3d basename when the server sends <c>zoneArchiveBase</c> (DB short_name may differ from file name).
    /// Returns true on success.
    /// </summary>
    public async Task<bool> ExtractZone(string zoneId, string lanternArchiveBase = null)
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
            await TryExtractTradeskillObjectsIfNeeded(cache);
            return true;
        }

        string zone = NormalizeZoneId(zoneId);
        string extractTarget;
        if (!string.IsNullOrWhiteSpace(lanternArchiveBase))
            extractTarget = lanternArchiveBase.Trim().ToLowerInvariant();
        else if (_zoneAliases.TryGetValue(zone, out string alias))
            extractTarget = alias;
        else
            extractTarget = zone;

        string s3dPath = config.FindZoneS3dPath(extractTarget);
        string eqDirForLantern = config.EQPath;
        if (s3dPath != null)
        {
            eqDirForLantern = Path.GetDirectoryName(s3dPath) ?? config.EQPath;
            if (!string.Equals(eqDirForLantern, config.EQPath, StringComparison.OrdinalIgnoreCase))
                GD.Print($"[Lantern] Zone archive for '{extractTarget}': {s3dPath}");
        }
        else
        {
            if (extractTarget == "cshome")
            {
                GD.PrintErr($"[Lantern] No 'cshome.s3d' found. Sunset Home is an admin zone that may be missing from your EQ installation. Try searching for it in a Titanium or Live client and copy it to {config.EQPath}.");
            }
            else
            {
                GD.PrintErr($"[Lantern] No '{extractTarget}.s3d' found under EQ path(s): {config.FormatZoneSearchRootsForDiagnostics()}. " +
                    "Point eqPath at the folder that contains eqgame.exe and the zone .s3d files together, or add a \"zoneSearchPaths\" array in user://eq_config.json if archives live elsewhere. " +
                    "A partial install (only a few zones) will break every other zone the same way.");
            }
            return false;
        }

        WriteSettings(eqDirForLantern);

        GD.Print($"[Lantern] Extracting zone: {extractTarget} (logical zone '{zone}', EQ dir for Lantern: {eqDirForLantern})...");
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
                if (File.Exists(oldGlb))
                {
                    try
                    {
                        if (File.Exists(newGlb))
                            File.Delete(newGlb);
                        File.Move(oldGlb, newGlb);
                    }
                    catch (IOException ex)
                    {
                        GD.PrintErr($"[Lantern] Could not rename mesh '{oldGlb}' -> '{newGlb}': {ex.Message}");
                    }
                }
            }
        }

        cache.MarkZoneExtracted(zone);

        GD.Print($"[Lantern] Zone '{zone}' extracted in {sw.Elapsed.TotalSeconds:F1}s");

        await TryExtractTradeskillObjectsIfNeeded(cache);

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
            await TryExtractTradeskillObjectsIfNeeded(EQAssetCache.Instance);
            return true;
        }
        finally
        {
            _extractLock.Release();
        }
    }

    // ── Private ─────────────────────────────────────────────────

    /// <summary>
    /// RoF2 ships crafting stations in <c>tradeskill_objects.eqg</c> (ZON/mod data). LanternExtractor only resolves
    /// classic <c>.s3d</c> shortnames in <c>EqFileHelper</c>, so it cannot export this archive. Populate GLBs via
    /// <c>tradeskillObjectsObjectsDir</c> in <c>user://eq_config.json</c> or copy <c>*.glb</c> into
    /// <c>cache/shared/tradeskill_objects/Objects/</c>.
    /// </summary>
    private Task TryExtractTradeskillObjectsIfNeeded(EQAssetCache cache)
    {
        if (!EQAssetConfig.Instance.IsConfigured)
            return Task.CompletedTask;

        const string archiveBase = "tradeskill_objects";
        string destRoot = Path.Combine(cache.CacheRoot, "shared", archiveBase);
        string objectsDir = Path.Combine(destRoot, "Objects");
        string marker = Path.Combine(destRoot, ".lantern_ok");

        var cfg = EQAssetConfig.Instance;

        // Configured folder wins over an old marker (so JSON/UI changes apply without manual marker delete).
        string resolvedTs = cfg.GetResolvedTradeskillObjectsDir();
        if (!string.IsNullOrWhiteSpace(resolvedTs) && Directory.Exists(resolvedTs))
        {
            Directory.CreateDirectory(destRoot);
            File.WriteAllText(marker, "external\t" + resolvedTs);
            if (!_loggedTradeskillExternal)
            {
                _loggedTradeskillExternal = true;
                GD.Print($"[Lantern] Tradeskill props: using configured GLB folder ({resolvedTs}).");
            }
            return Task.CompletedTask;
        }

        if (Directory.Exists(objectsDir))
        {
            try
            {
                if (Directory.GetFiles(objectsDir, "*.glb", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    Directory.CreateDirectory(destRoot);
                    File.WriteAllText(marker, "cache-objects\t" + DateTime.UtcNow.ToString("o"));
                    GD.Print($"[Lantern] Tradeskill props: found GLBs under cache ({objectsDir}).");
                    return Task.CompletedTask;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Lantern] Could not scan tradeskill cache Objects: {ex.Message}");
            }
        }

        if (File.Exists(marker))
            return Task.CompletedTask;

        string eqgPath = cfg.FindEqgPath(archiveBase);
        if (eqgPath != null)
        {
            if (!_warnedTradeskillEqgNotLanternExportable)
            {
                _warnedTradeskillEqgNotLanternExportable = true;
                GD.PrintErr(
                    "[Lantern] tradeskill_objects.eqg is present but LanternExtractor cannot export this ZON-style archive " +
                    "(only classic .s3d/.pfs shortnames are resolved). For IT* props, use <EQ>/eqsage/objects, EQ settings, " +
                    "user://eq_config.json \"tradeskillObjectsObjectsDir\", or copy .glb files into " +
                    $"'{objectsDir.Replace('\\', '/')}'.");
            }
            return Task.CompletedTask;
        }

        if (!_warnedTradeskillEqgMissing)
        {
            _warnedTradeskillEqgMissing = true;
            GD.PrintErr($"[Lantern] tradeskill_objects.eqg not found under EQ path(s): {cfg.FormatZoneSearchRootsForDiagnostics()}. " +
                "IT* props need <EQ>/eqsage/objects, a saved tradeskill folder in EQ settings, or tradeskillObjectsObjectsDir in eq_config.json.");
        }

        return Task.CompletedTask;
    }

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

        CopyOneMusicStem(zoneId, eqPath, musicDir, "zone music");

        // Classic / live loose per-zone ambience (e.g. gfaydarkam.xmi). RoF2 often has no loose *am file — use server zone_client_ambience.json → amb_*.
        foreach (string ambStem in new[] { zoneId + "am", zoneId + "_am" })
            CopyOneMusicStem(ambStem, eqPath, musicDir, "zone ambience");
    }

    /// <summary>Copy first available MP3, else XMI, else WAV for <paramref name="stem"/> into session cache/music.</summary>
    private static void CopyOneMusicStem(string stem, string eqPath, string musicDir, string logLabel)
    {
        string mp3Source = Path.Combine(eqPath, $"{stem}.mp3");
        if (File.Exists(mp3Source))
        {
            string dest = Path.Combine(musicDir, $"{stem}.mp3");
            File.Copy(mp3Source, dest, overwrite: true);
            GD.Print($"[Lantern] Copied {logLabel}: {stem}.mp3");
            return;
        }

        string xmiSource = Path.Combine(eqPath, $"{stem}.xmi");
        if (File.Exists(xmiSource))
        {
            string dest = Path.Combine(musicDir, $"{stem}.xmi");
            File.Copy(xmiSource, dest, overwrite: true);
            GD.Print($"[Lantern] Copied {logLabel}: {stem}.xmi");
            return;
        }

        string wavSource = Path.Combine(eqPath, $"{stem}.wav");
        if (File.Exists(wavSource))
        {
            string dest = Path.Combine(musicDir, $"{stem}.wav");
            File.Copy(wavSource, dest, overwrite: true);
            GD.Print($"[Lantern] Copied {logLabel}: {stem}.wav");
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
