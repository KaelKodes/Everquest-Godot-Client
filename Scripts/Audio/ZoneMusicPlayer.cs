using Godot;
using System;
using System.IO;

/// <summary>
/// Manages zone background music playback.
/// Loads MP3 files from the asset cache and plays them with crossfade.
/// </summary>
public partial class ZoneMusicPlayer : Node
{
    private AudioStreamPlayer _currentPlayer;
    private AudioStreamPlayer _fadeOutPlayer;
    private string _currentZone = "";
    private float _masterVolume = 0.8f; // 0..1
    private float _fadeTimer = 0;
    private float _fadeDuration = 2.0f; // seconds to crossfade
    private bool _isFading = false;

    public override void _Ready()
    {
        _currentPlayer = new AudioStreamPlayer();
        _currentPlayer.Name = "MusicCurrent";
        _currentPlayer.Bus = "Music"; // Route to Music bus if it exists
        _currentPlayer.VolumeDb = LinearToDb(_masterVolume);
        AddChild(_currentPlayer);

        _fadeOutPlayer = new AudioStreamPlayer();
        _fadeOutPlayer.Name = "MusicFadeOut";
        _fadeOutPlayer.Bus = "Music";
        AddChild(_fadeOutPlayer);
    }

    public override void _Process(double delta)
    {
        if (!_isFading) return;

        _fadeTimer += (float)delta;
        float t = Mathf.Clamp(_fadeTimer / _fadeDuration, 0f, 1f);

        // Fade out old, fade in new
        _fadeOutPlayer.VolumeDb = LinearToDb(_masterVolume * (1f - t));
        _currentPlayer.VolumeDb = LinearToDb(_masterVolume * t);

        if (t >= 1f)
        {
            _isFading = false;
            _fadeOutPlayer.Stop();
        }
    }

    /// <summary>
    /// Play music for the given zone. Crossfades from any currently playing track.
    /// </summary>
    public void PlayZoneMusic(string zoneId)
    {
        if (zoneId == _currentZone && _currentPlayer.Playing) return;

        string zone = zoneId.ToLower();
        var cache = EQAssetCache.Instance;

        // Try to find music: first in cache, then directly in EQ directory
        string musicPath = cache.GetZoneMusicPath(zone);

        if (musicPath == null)
        {
            // Try directly from EQ installation as fallback
            var config = EQAssetConfig.Instance;
            if (config.IsConfigured)
            {
                string directMp3 = Path.Combine(config.EQPath, $"{zone}.mp3");
                if (File.Exists(directMp3))
                    musicPath = directMp3;
            }
        }

        if (musicPath == null)
        {
            GD.Print($"[Music] No music found for zone '{zone}'");
            // If something was playing, just stop it gracefully
            if (_currentPlayer.Playing)
            {
                StopMusic();
            }
            _currentZone = zone;
            return;
        }

        // Only load MP3 — XMI (MIDI) isn't supported by Godot natively
        if (!musicPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            GD.Print($"[Music] Skipping non-MP3 music: {musicPath}");
            _currentZone = zone;
            return;
        }

        // Load the MP3 file
        AudioStream stream = LoadMp3(musicPath);
        if (stream == null)
        {
            GD.PrintErr($"[Music] Failed to load music: {musicPath}");
            _currentZone = zone;
            return;
        }

        // Crossfade: swap current to fadeOut, start new on current
        if (_currentPlayer.Playing)
        {
            _fadeOutPlayer.Stream = _currentPlayer.Stream;
            _fadeOutPlayer.VolumeDb = _currentPlayer.VolumeDb;
            _fadeOutPlayer.Play(_currentPlayer.GetPlaybackPosition());
            _currentPlayer.Stop();

            _isFading = true;
            _fadeTimer = 0;
        }

        _currentPlayer.Stream = stream;
        _currentPlayer.VolumeDb = _isFading ? LinearToDb(0) : LinearToDb(_masterVolume);
        _currentPlayer.Play();

        _currentZone = zone;
        GD.Print($"[Music] Playing zone music: {Path.GetFileName(musicPath)}");
    }

    /// <summary>Stop all music playback.</summary>
    public void StopMusic()
    {
        _isFading = false;
        _currentPlayer.Stop();
        _fadeOutPlayer.Stop();
        _currentZone = "";
    }

    /// <summary>Set music volume (0..1).</summary>
    public void SetVolume(float volume)
    {
        _masterVolume = Mathf.Clamp(volume, 0f, 1f);
        if (!_isFading)
            _currentPlayer.VolumeDb = LinearToDb(_masterVolume);
    }

    /// <summary>Get current volume (0..1).</summary>
    public float GetVolume() => _masterVolume;

    // ── Private ─────────────────────────────────────────────────

    /// <summary>Load an MP3 file from disk at runtime.</summary>
    private AudioStream LoadMp3(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            var mp3Stream = new AudioStreamMP3();
            mp3Stream.Data = data;
            mp3Stream.Loop = true; // Zone music loops
            return mp3Stream;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Music] Error loading MP3: {ex.Message}");
            return null;
        }
    }

    private static float LinearToDb(float linear)
    {
        if (linear <= 0) return -80f;
        return Mathf.Log(linear) * 20f / Mathf.Log(10f);
    }
}
