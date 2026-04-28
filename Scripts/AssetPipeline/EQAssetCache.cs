using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Session-scoped asset cache. Extracted zone/character data lives here
/// during gameplay and is cleared when the game exits.
/// </summary>
public partial class EQAssetCache : RefCounted
{
    private static EQAssetCache _instance;
    public static EQAssetCache Instance => _instance ??= new EQAssetCache();

    private readonly HashSet<string> _extractedZones = new();
    private readonly HashSet<string> _extractedCharacters = new();
    private string _cacheRoot;

    public EQAssetCache()
    {
        // Use Godot's user data directory for the session cache
        _cacheRoot = OS.GetUserDataDir() + "/cache";
        EnsureCacheDir();
        GD.Print($"[AssetCache] Cache root: {_cacheRoot}");
    }

    /// <summary>Root directory for all cached assets.</summary>
    public string CacheRoot => _cacheRoot;

    // ── Zone Cache ──────────────────────────────────────────────

    /// <summary>Check if a zone has been extracted this session.</summary>
    public bool HasZone(string zoneId) => _extractedZones.Contains(zoneId.ToLower());

    /// <summary>Get the cache directory for a specific zone.</summary>
    public string GetZonePath(string zoneId) => $"{_cacheRoot}/zones/{zoneId.ToLower()}";

    /// <summary>Get the path to the zone's terrain GLB mesh.</summary>
    public string GetZoneGlbPath(string zoneId)
    {
        string zone = zoneId.ToLower();
        // LanternExtractor outputs zone meshes as GLB when configured
        string glbPath = $"{GetZonePath(zone)}/Zone/{zone}.glb";
        if (System.IO.File.Exists(glbPath)) return glbPath;

        // Fallback: check for the raw text mesh (older Lantern format)
        string txtPath = $"{GetZonePath(zone)}/Zone/Meshes/{zone}.glb";
        if (System.IO.File.Exists(txtPath)) return txtPath;

        return null;
    }

    /// <summary>Get the path to the zone's object instances file.</summary>
    public string GetObjectInstancesPath(string zoneId)
    {
        return $"{GetZonePath(zoneId.ToLower())}/Zone/object_instances.txt";
    }

    /// <summary>Get the path to the zone's light instances file.</summary>
    public string GetLightInstancesPath(string zoneId)
    {
        return $"{GetZonePath(zoneId.ToLower())}/Zone/light_instances.txt";
    }

    /// <summary>Get the Objects directory for a zone (contains placeable GLBs).</summary>
    public string GetObjectsDir(string zoneId)
    {
        return $"{GetZonePath(zoneId.ToLower())}/Objects";
    }

    /// <summary>Mark a zone as extracted this session.</summary>
    public void MarkZoneExtracted(string zoneId)
    {
        _extractedZones.Add(zoneId.ToLower());
        GD.Print($"[AssetCache] Zone '{zoneId}' cached. Total cached: {_extractedZones.Count}");
    }

    // ── Character Cache ─────────────────────────────────────────

    /// <summary>Check if character models have been extracted this session.</summary>
    public bool HasCharacter(string raceCode) => _extractedCharacters.Contains(raceCode.ToLower());

    /// <summary>Get the path to a character model GLB.</summary>
    public string GetCharacterGlbPath(string raceCode)
    {
        return $"{_cacheRoot}/characters/{raceCode.ToLower()}.glb";
    }

    /// <summary>Get the Characters directory from the global extraction.</summary>
    public string GetCharactersDir()
    {
        return $"{_cacheRoot}/characters";
    }

    /// <summary>Mark character models as extracted.</summary>
    public void MarkCharacterExtracted(string raceCode)
    {
        _extractedCharacters.Add(raceCode.ToLower());
    }

    // ── Audio Cache ─────────────────────────────────────────────

    /// <summary>Get the path to zone music (MP3 or XMI).</summary>
    public string GetZoneMusicPath(string zoneId)
    {
        string zone = zoneId.ToLower();
        // Check for MP3 first (higher quality)
        string mp3 = $"{_cacheRoot}/music/{zone}.mp3";
        if (System.IO.File.Exists(mp3)) return mp3;

        // Fallback: XMI (MIDI)
        string xmi = $"{_cacheRoot}/music/{zone}.xmi";
        if (System.IO.File.Exists(xmi)) return xmi;

        return null;
    }

    // ── Cache Management ────────────────────────────────────────

    /// <summary>Clear the entire session cache. Called on game exit.</summary>
    public void ClearCache()
    {
        try
        {
            if (System.IO.Directory.Exists(_cacheRoot))
            {
                System.IO.Directory.Delete(_cacheRoot, recursive: true);
                GD.Print($"[AssetCache] Cleared cache ({_extractedZones.Count} zones, {_extractedCharacters.Count} characters)");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AssetCache] Failed to clear cache: {ex.Message}");
        }

        _extractedZones.Clear();
        _extractedCharacters.Clear();
        EnsureCacheDir();
    }

    /// <summary>Get cache statistics.</summary>
    public (int zones, int characters, long sizeMB) GetStats()
    {
        long totalSize = 0;
        if (System.IO.Directory.Exists(_cacheRoot))
        {
            foreach (var file in System.IO.Directory.GetFiles(_cacheRoot, "*", System.IO.SearchOption.AllDirectories))
            {
                totalSize += new System.IO.FileInfo(file).Length;
            }
        }

        return (_extractedZones.Count, _extractedCharacters.Count, totalSize / (1024 * 1024));
    }

    private void EnsureCacheDir()
    {
        System.IO.Directory.CreateDirectory(_cacheRoot);
        System.IO.Directory.CreateDirectory($"{_cacheRoot}/zones");
        System.IO.Directory.CreateDirectory($"{_cacheRoot}/characters");
        System.IO.Directory.CreateDirectory($"{_cacheRoot}/music");
    }

    private Dictionary<string, AudioStream> _soundCache = new Dictionary<string, AudioStream>();

    /// <summary>
    /// Loads a WAV file from the EQ installation and caches it for runtime playback.
    /// Supports looping sounds (like ambient fire).
    /// </summary>
    public AudioStream GetSound(string filename, bool loop = false)
    {
        if (_soundCache.TryGetValue(filename, out var cachedStream))
            return cachedStream;

        string path = EQAssetConfig.Instance.GetEQFilePath($"sounds/{filename}");
        if (path == null || !System.IO.File.Exists(path))
        {
            // Fallback to EQ root directory since many EQ installations place .wav files there
            path = EQAssetConfig.Instance.GetEQFilePath(filename);
            if (path == null || !System.IO.File.Exists(path))
                return null;
        }

        var wav = LoadWav(path, loop);
        if (wav != null)
        {
            _soundCache[filename] = wav;
        }
        return wav;
    }

    private AudioStreamWav LoadWav(string path, bool loop)
    {
        try
        {
            byte[] fileData = System.IO.File.ReadAllBytes(path);
            if (fileData.Length < 44) return null;

            if (fileData[0] != 'R' || fileData[1] != 'I' || fileData[2] != 'F' || fileData[3] != 'F') return null;
            if (fileData[8] != 'W' || fileData[9] != 'A' || fileData[10] != 'V' || fileData[11] != 'E') return null;

            int i = 12;
            int numChannels = 1;
            int sampleRate = 44100;
            int bitsPerSample = 16;
            
            while (i < fileData.Length - 8)
            {
                if (fileData[i] == 'f' && fileData[i+1] == 'm' && fileData[i+2] == 't' && fileData[i+3] == ' ')
                {
                    numChannels = BitConverter.ToInt16(fileData, i + 10);
                    sampleRate = BitConverter.ToInt32(fileData, i + 12);
                    bitsPerSample = BitConverter.ToInt16(fileData, i + 22);
                    break;
                }
                i++;
            }

            i = 12;
            int dataOffset = -1;
            int dataSize = 0;
            while (i < fileData.Length - 8)
            {
                if (fileData[i] == 'd' && fileData[i+1] == 'a' && fileData[i+2] == 't' && fileData[i+3] == 'a')
                {
                    dataSize = BitConverter.ToInt32(fileData, i + 4);
                    dataOffset = i + 8;
                    break;
                }
                i++;
            }

            if (dataOffset == -1) return null;

            byte[] audioData = new byte[dataSize];
            Array.Copy(fileData, dataOffset, audioData, 0, Math.Min(dataSize, fileData.Length - dataOffset));

            var wav = new AudioStreamWav();
            wav.Data = audioData;
            wav.Format = bitsPerSample == 8 ? AudioStreamWav.FormatEnum.Format8Bits : AudioStreamWav.FormatEnum.Format16Bits;
            wav.MixRate = sampleRate;
            wav.Stereo = numChannels == 2;
            if (loop)
            {
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                wav.LoopEnd = audioData.Length / (bitsPerSample == 8 ? 1 : 2) / numChannels;
            }
            return wav;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AssetCache] Failed to load WAV '{path}': {ex.Message}");
            return null;
        }
    }
}
