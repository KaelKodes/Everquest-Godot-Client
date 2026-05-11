using Godot;
using System;
using System.IO;

/// <summary>
/// Manages zone background music playback.
/// Loads MP3 files from the asset cache and plays them with crossfade.
/// </summary>
public partial class ZoneMusicPlayer : Node
{
    [Signal] public delegate void MusicChangedEventHandler(string trackName);

    private AudioStreamPlayer _currentPlayer;
    private AudioStreamPlayer _fadeOutPlayer;
    private string _currentZone = "";
    private string _currentTrackName = "";
    private float _masterVolume = 0.8f; // 0..1
    private float _sfxVolume = 0.8f;    // 0..1 — controls entity/ambient SFX
    private AudioStreamPlayer _ambiencePlayer;
    private AudioStreamPlayer _ambienceFadeOutPlayer;
    private string _currentAmbience = "";
    private float _ambienceVolume = 0.8f;
    private bool _ambienceMuted = false;
    private float _ambienceFadeTimer = 0;
    private bool _isAmbienceFading = false;
    private float _fadeTimer = 0;
    private float _fadeDuration = 2.0f; // seconds to crossfade
    private bool _isFading = false;
    private bool _musicMuted = false;
    private bool _sfxMuted = false;

    /// <summary>Display name of the currently playing track (zone filename).</summary>
    public string CurrentTrackName => _currentTrackName;

    /// <summary>True if music is currently playing.</summary>
    public bool IsPlaying => _currentPlayer != null && _currentPlayer.Playing;

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

        _ambiencePlayer = new AudioStreamPlayer();
        _ambiencePlayer.Name = "AmbienceCurrent";
        _ambiencePlayer.Bus = "SFX";
        _ambiencePlayer.VolumeDb = LinearToDb(_ambienceVolume);
        AddChild(_ambiencePlayer);

        _ambienceFadeOutPlayer = new AudioStreamPlayer();
        _ambienceFadeOutPlayer.Name = "AmbienceFadeOut";
        _ambienceFadeOutPlayer.Bus = "SFX";
        AddChild(_ambienceFadeOutPlayer);
    }

    public override void _Process(double delta)
    {
        if (_isFading)
        {
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

        if (_isAmbienceFading)
        {
            _ambienceFadeTimer += (float)delta;
            float tAmb = Mathf.Clamp(_ambienceFadeTimer / _fadeDuration, 0f, 1f);

            _ambienceFadeOutPlayer.VolumeDb = LinearToDb(_ambienceVolume * (1f - tAmb));
            _ambiencePlayer.VolumeDb = LinearToDb(_ambienceVolume * tAmb);

            if (tAmb >= 1f)
            {
                _isAmbienceFading = false;
                _ambienceFadeOutPlayer.Stop();
            }
        }
    }

    /// <summary>
    /// Play music for the given zone. Crossfades from any currently playing track.
    /// Search order: MP3 in EQ dir → MP3 in cache → XMI (rendered via SoundFont).
    /// </summary>
    public void PlayZoneMusic(string zoneId)
    {
        if (zoneId == _currentZone && _currentPlayer.Playing) return;

        string zone = zoneId.ToLower();
        var cache = EQAssetCache.Instance;
        var config = EQAssetConfig.Instance;

        // ── Search for music in priority order ──
        string musicPath = null;

        // 1) MP3 directly in the EQ installation folder (highest quality)
        if (config.IsConfigured)
        {
            string directMp3 = Path.Combine(config.EQPath, $"{zone}.mp3");
            if (File.Exists(directMp3))
                musicPath = directMp3;
        }

        // 2) MP3 in cache (extracted by Lantern)
        if (musicPath == null)
        {
            string cacheMp3 = $"{cache.CacheRoot}/music/{zone}.mp3";
            if (File.Exists(cacheMp3))
                musicPath = cacheMp3;
        }

        // 3) XMI in cache (extracted by Lantern)
        string xmiPath = null;
        if (musicPath == null)
        {
            string cacheXmi = $"{cache.CacheRoot}/music/{zone}.xmi";
            if (File.Exists(cacheXmi))
                xmiPath = cacheXmi;
        }

        // 4) XMI directly in EQ folder
        if (musicPath == null && xmiPath == null && config.IsConfigured)
        {
            string directXmi = Path.Combine(config.EQPath, $"{zone}.xmi");
            if (File.Exists(directXmi))
                xmiPath = directXmi;
        }

        // No music at all
        if (musicPath == null && xmiPath == null)
        {
            GD.Print($"[Music] No music found for zone '{zone}'");
            if (_currentPlayer.Playing) StopMusic();
            _currentZone = zone;
            return;
        }

        // ── Load the audio stream ──
        AudioStream stream = null;

        if (musicPath != null)
        {
            stream = LoadMp3(musicPath);
        }
        else if (xmiPath != null)
        {
            stream = LoadXmi(xmiPath);
        }

        if (stream == null)
        {
            GD.PrintErr($"[Music] Failed to load music for zone '{zone}'");
            _currentZone = zone;
            return;
        }

        // ── Crossfade: swap current to fadeOut, start new on current ──
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
        _currentPlayer.VolumeDb = _musicMuted ? -80f : (_isFading ? LinearToDb(0) : LinearToDb(_masterVolume));
        _currentPlayer.Play();

        _currentZone = zone;
        string sourceName = musicPath ?? xmiPath;
        _currentTrackName = Path.GetFileNameWithoutExtension(sourceName);
        string formatTag = musicPath != null ? "MP3" : "XMI→MIDI";
        GD.Print($"[Music] Playing zone music ({formatTag}): {Path.GetFileName(sourceName)}");
        EmitSignal(SignalName.MusicChanged, _currentTrackName);
    }

    public void StopMusic()
    {
        _isFading = false;
        _currentPlayer.Stop();
        _fadeOutPlayer.Stop();
        _currentZone = "";
        _currentTrackName = "";
        EmitSignal(SignalName.MusicChanged, "");
    }

    public void PlayZoneAmbience(string trackName)
    {
        if (trackName == _currentAmbience && _ambiencePlayer.Playing) return;

        string zone = trackName.ToLower();
        var cache = EQAssetCache.Instance;
        var config = EQAssetConfig.Instance;

        string musicPath = null;
        if (config.IsConfigured)
        {
            string directMp3 = Path.Combine(config.EQPath, $"{zone}.mp3");
            if (File.Exists(directMp3)) musicPath = directMp3;
        }

        if (musicPath == null)
        {
            string cacheMp3 = $"{cache.CacheRoot}/music/{zone}.mp3";
            if (File.Exists(cacheMp3)) musicPath = cacheMp3;
        }

        if (musicPath == null)
        {
            if (_ambiencePlayer.Playing) StopAmbience();
            _currentAmbience = zone;
            return;
        }

        AudioStream stream = LoadMp3(musicPath);

        if (stream == null)
        {
            _currentAmbience = zone;
            return;
        }

        if (_ambiencePlayer.Playing)
        {
            _ambienceFadeOutPlayer.Stream = _ambiencePlayer.Stream;
            _ambienceFadeOutPlayer.VolumeDb = _ambiencePlayer.VolumeDb;
            _ambienceFadeOutPlayer.Play(_ambiencePlayer.GetPlaybackPosition());
            _ambiencePlayer.Stop();

            _isAmbienceFading = true;
            _ambienceFadeTimer = 0;
        }

        _ambiencePlayer.Stream = stream;
        _ambiencePlayer.VolumeDb = _ambienceMuted ? -80f : (_isAmbienceFading ? LinearToDb(0) : LinearToDb(_ambienceVolume));
        _ambiencePlayer.Play();

        _currentAmbience = zone;
    }

    public void StopAmbience()
    {
        _isAmbienceFading = false;
        _ambiencePlayer.Stop();
        _ambienceFadeOutPlayer.Stop();
        _currentAmbience = "";
    }

    /// <summary>Set music volume (0..1).</summary>
    public void SetVolume(float volume)
    {
        _masterVolume = Mathf.Clamp(volume, 0f, 1f);
        if (!_isFading)
            _currentPlayer.VolumeDb = LinearToDb(_masterVolume);
    }

    /// <summary>Get current music volume (0..1).</summary>
    public float GetVolume() => _masterVolume;

    /// <summary>Set SFX volume (0..1). Affects all AudioStreamPlayer3D nodes in the scene.</summary>
    public void SetSfxVolume(float volume)
    {
        _sfxVolume = Mathf.Clamp(volume, 0f, 1f);
    }

    /// <summary>Get current SFX volume (0..1).</summary>
    public float GetSfxVolume() => _sfxVolume;

    /// <summary>Toggle music mute.</summary>
    public void SetMusicMuted(bool muted)
    {
        _musicMuted = muted;
        if (_currentPlayer != null)
            _currentPlayer.VolumeDb = muted ? -80f : LinearToDb(_masterVolume);
    }

    /// <summary>Is music muted?</summary>
    public bool IsMusicMuted => _musicMuted;

    /// <summary>Toggle SFX mute.</summary>
    public void SetSfxMuted(bool muted)
    {
        _sfxMuted = muted;
    }

    /// <summary>Is SFX muted?</summary>
    public bool IsSfxMuted => _sfxMuted;

    public void SetAmbienceVolume(float volume)
    {
        _ambienceVolume = Mathf.Clamp(volume, 0f, 1f);
        if (!_isAmbienceFading && _ambiencePlayer != null)
            _ambiencePlayer.VolumeDb = LinearToDb(_ambienceVolume);
    }

    public float GetAmbienceVolume() => _ambienceVolume;

    public void SetAmbienceMuted(bool muted)
    {
        _ambienceMuted = muted;
        if (_ambiencePlayer != null)
            _ambiencePlayer.VolumeDb = muted ? -80f : LinearToDb(_ambienceVolume);
    }

    public bool IsAmbienceMuted => _ambienceMuted;

    // ── Private Audio Loaders ───────────────────────────────────

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

    // ── XMI Rendering ───────────────────────────────────────────

    private MeltySynth.Synthesizer _synth;
    private string _soundFontSource; // human-readable label for logging

    /// <summary>
    /// Resolve the bundled SoundFont as a byte array.
    ///
    /// Resolution order:
    ///   1. Embedded .NET resource inside EQMUD.dll (the most reliable —
    ///      ships with the C# assembly so it is never omitted by Godot's
    ///      export pipeline).
    ///   2. Godot resource path <c>res://Assets/Audio/TimGM6mb.sf2</c>
    ///      (works in editor; only works in export if the file is packed).
    ///   3. A copy sitting next to the executable, e.g.
    ///      <c>&lt;exe-dir&gt;/Assets/Audio/TimGM6mb.sf2</c>.
    /// </summary>
    private static byte[] LoadBundledSoundFontBytes(out string sourceLabel)
    {
        // 1) Embedded resource (preferred — see EQMUD.csproj <EmbeddedResource>)
        try
        {
            var asm = typeof(ZoneMusicPlayer).Assembly;
            using var s = asm.GetManifestResourceStream("EQMUD.TimGM6mb.sf2");
            if (s != null)
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                byte[] bytes = ms.ToArray();
                if (bytes.Length > 0)
                {
                    sourceLabel = "TimGM6mb.sf2 (embedded)";
                    return bytes;
                }
                GD.PrintErr("[Music] Embedded SF2 stream was empty.");
            }
            else
            {
                GD.Print("[Music] Embedded SF2 resource 'EQMUD.TimGM6mb.sf2' not present in assembly.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Music] Embedded SF2 load failed: {ex.Message}");
        }

        // 2) Godot resource path (works in editor; PCK-dependent in exports)
        const string bundledSf2 = "res://Assets/Audio/TimGM6mb.sf2";
        if (Godot.FileAccess.FileExists(bundledSf2))
        {
            byte[] bytes = Godot.FileAccess.GetFileAsBytes(bundledSf2);
            if (bytes != null && bytes.Length > 0)
            {
                sourceLabel = "TimGM6mb.sf2 (res://)";
                return bytes;
            }
            GD.PrintErr("[Music] res:// SF2 existed but read returned 0 bytes.");
        }
        else
        {
            GD.Print($"[Music] SF2 not found at {bundledSf2} (PCK may not contain it).");
        }

        // 3) Sibling-to-executable fallback (manual drop-in for emergency use)
        try
        {
            string exeDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(exeDir))
            {
                string sidecar = Path.Combine(exeDir, "Assets", "Audio", "TimGM6mb.sf2");
                if (File.Exists(sidecar))
                {
                    byte[] bytes = File.ReadAllBytes(sidecar);
                    if (bytes.Length > 0)
                    {
                        sourceLabel = $"TimGM6mb.sf2 (sidecar: {sidecar})";
                        return bytes;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Music] Sidecar SF2 load failed: {ex.Message}");
        }

        sourceLabel = null;
        return null;
    }

    /// <summary>
    /// Resolve a fallback SoundFont from the EQ installation as bytes.
    /// </summary>
    private static byte[] LoadEqSoundFontBytes(out string sourceLabel)
    {
        var config = EQAssetConfig.Instance;
        if (config.IsConfigured)
        {
            string sf2 = Path.Combine(config.EQPath, "synthusr.sf2");
            if (File.Exists(sf2))
            {
                try
                {
                    sourceLabel = "synthusr.sf2 (EQ install)";
                    return File.ReadAllBytes(sf2);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[Music] Failed to read EQ SoundFont: {ex.Message}");
                }
            }
        }

        sourceLabel = null;
        return null;
    }

    /// <summary>
    /// Load an XMI file, convert to MIDI, render via MeltySynth + EQ SoundFont,
    /// and return as a looping AudioStreamWav.
    /// </summary>
    private AudioStream LoadXmi(string xmiPath)
    {
        try
        {
            // Convert XMI → standard MIDI bytes
            byte[] midiBytes = XmiToMidi.ConvertFile(xmiPath);
            if (midiBytes == null || midiBytes.Length < 14)
            {
                GD.PrintErr($"[Music] XMI conversion failed for: {xmiPath}");
                return null;
            }

            // Initialize synthesizer (lazy, reuse across tracks).
            // We load the SoundFont bytes via Godot.FileAccess so the bundled
            // SF2 resolves correctly inside an exported .pck.
            if (_synth == null)
            {
                _synth = TryCreateSynth(22050);
                if (_synth == null)
                    return null;
            }

            // Load MIDI from bytes
            var midiFile = new MeltySynth.MidiFile(new MemoryStream(midiBytes));
            var sequencer = new MeltySynth.MidiFileSequencer(_synth);
            sequencer.Play(midiFile, false);

            // Render to PCM buffers (16-bit signed, 22050 Hz, stereo)
            int sampleRate = 22050;
            double durationSec = midiFile.Length.TotalSeconds;
            if (durationSec < 1.0) durationSec = 60.0; // Fallback for very short/corrupt MIDI
            int totalSamples = (int)(sampleRate * durationSec) + sampleRate; // +1s buffer

            var leftBuf = new float[totalSamples];
            var rightBuf = new float[totalSamples];
            sequencer.Render(leftBuf, rightBuf);

            // Interleave stereo and convert to 16-bit PCM
            byte[] pcmData = new byte[totalSamples * 4]; // 2 channels * 2 bytes each
            for (int i = 0; i < totalSamples; i++)
            {
                short l = (short)Mathf.Clamp(leftBuf[i] * 32767f, -32768f, 32767f);
                short r = (short)Mathf.Clamp(rightBuf[i] * 32767f, -32768f, 32767f);
                pcmData[i * 4 + 0] = (byte)(l & 0xFF);
                pcmData[i * 4 + 1] = (byte)((l >> 8) & 0xFF);
                pcmData[i * 4 + 2] = (byte)(r & 0xFF);
                pcmData[i * 4 + 3] = (byte)((r >> 8) & 0xFF);
            }

            var wav = new AudioStreamWav();
            wav.Data = pcmData;
            wav.Format = AudioStreamWav.FormatEnum.Format16Bits;
            wav.MixRate = sampleRate;
            wav.Stereo = true;
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopEnd = totalSamples;

            GD.Print($"[Music] Rendered XMI ({durationSec:F1}s, {sampleRate}Hz stereo): {Path.GetFileName(xmiPath)}");
            return wav;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Music] Error rendering XMI: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Try to construct a MeltySynth.Synthesizer using the bundled SoundFont first,
    /// then the EQ installation SoundFont as a fallback. Loads SF2 bytes via Godot's
    /// FileAccess so the bundled file resolves both in the editor and in an exported .pck.
    /// Returns null and logs if no usable SoundFont is available.
    /// </summary>
    private MeltySynth.Synthesizer TryCreateSynth(int sampleRate)
    {
        // 1) Bundled SoundFont (preferred — known-good GM bank)
        byte[] bytes = LoadBundledSoundFontBytes(out string label);

        // 2) Fallback: EQ install SoundFont
        if (bytes == null)
            bytes = LoadEqSoundFontBytes(out label);

        if (bytes == null)
        {
            GD.PrintErr("[Music] No SoundFont available (neither bundled nor EQ install). XMI playback disabled.");
            return null;
        }

        try
        {
            using var ms = new MemoryStream(bytes);
            var soundFont = new MeltySynth.SoundFont(ms);
            var synth = new MeltySynth.Synthesizer(soundFont, sampleRate);
            _soundFontSource = label;
            GD.Print($"[Music] SoundFont loaded: {label}");
            return synth;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Music] SoundFont '{label}' failed to load: {ex.Message}");

            // If the bundled SF2 was tried first and we haven't tried the EQ fallback,
            // give it one more shot.
            if (label != null && label.StartsWith("TimGM6mb"))
            {
                byte[] fallback = LoadEqSoundFontBytes(out string fallbackLabel);
                if (fallback != null)
                {
                    try
                    {
                        using var ms2 = new MemoryStream(fallback);
                        var soundFont2 = new MeltySynth.SoundFont(ms2);
                        var synth2 = new MeltySynth.Synthesizer(soundFont2, sampleRate);
                        _soundFontSource = fallbackLabel;
                        GD.Print($"[Music] SoundFont loaded (fallback): {fallbackLabel}");
                        return synth2;
                    }
                    catch (Exception ex2)
                    {
                        GD.PrintErr($"[Music] Fallback SoundFont '{fallbackLabel}' failed to load: {ex2.Message}");
                    }
                }
            }

            return null;
        }
    }

    private static float LinearToDb(float linear)
    {
        if (linear <= 0) return -80f;
        return Mathf.Log(linear) * 20f / Mathf.Log(10f);
    }
}
