using Godot;
using System;
using System.Collections.Generic;

public partial class WorldManager : Node3D
{
    private void UpdateFootstepSounds(Vector3 vel, bool onFloor, bool sprinting, double delta)
    {
        // Determine movement state
        float horizSpeed = new Vector2(vel.X, vel.Z).Length();
        string newState;

        if (!onFloor && vel.Y < -2f)
            newState = "idle"; // Falling — no footsteps
        else if (horizSpeed < 0.5f)
            newState = "idle";
        else if (sprinting)
            newState = "run";
        else
            newState = "walk";

        // Create footstep player on first use
        if (_footstepPlayer == null && _playerCapsule != null)
        {
            _footstepPlayer = new AudioStreamPlayer();
            _footstepPlayer.Name = "FootstepPlayer";
            _playerCapsule.AddChild(_footstepPlayer);
        }

        // State change — reset timer
        if (newState != _lastFootstepState)
        {
            // Debug logging removed — footstep system is stable
            _lastFootstepState = newState;
            _footstepTimer = 0; // Play immediately on transition

            if (newState == "idle" && _footstepPlayer != null)
            {
                _footstepPlayer.Stop();
            }
        }

        if (newState == "idle") return;

        // Cadence: run = faster steps, walk = slower
        _footstepCadence = newState == "run" ? 0.58 : 0.55;

        _footstepTimer -= delta;
        if (_footstepTimer <= 0)
        {
            _footstepTimer = _footstepCadence;
            PlayFootstep(newState);
        }
    }
    private void PlayFootstep(string moveType)
    {
        if (_playerCapsule == null) return;

        // Get the player's model code for race-specific sounds
        string modelCode = _playerCapsule.GetModelCode();
        if (string.IsNullOrEmpty(modelCode)) modelCode = "hum";

        // Map moveType to sound suffix (EQ naming: hum_run.wav, hum_wlk.wav, hum_swm.wav, hum_atk.wav)
        string suffix = moveType switch
        {
            "run" => "_run.wav",
            "walk" => "_wlk.wav",
            "swim" => "_swm.wav",
            "jmp" => "_atk.wav", // Jump uses the vocal attack grunt
            _ => "_wlk.wav"
        };

        // Try race-specific first, then human fallback
        string soundFile = $"{modelCode}{suffix}";
        var stream = EQAssetCache.Instance.GetSound(soundFile);

        if (stream == null)
        {
            // Fallback: try 3-letter code
            string shortCode = modelCode.Substring(0, System.Math.Min(3, modelCode.Length));
            soundFile = $"{shortCode}{suffix}";
            stream = EQAssetCache.Instance.GetSound(soundFile);
        }

        if (stream == null)
        {
            // Final fallback: Use high-quality Dark Elf Male/Female audio as the universal humanoid default
            string dkPrefix = _playerCapsule.Gender == 1 ? "DKF_" : "DKM_";
            string dkSuffix = moveType switch
            {
                "run" => "Run.wav",
                "walk" => "Walk.wav",
                "swim" => "SwimFoward.wav",
                "jmp" => "Jump.wav",
                _ => "Walk.wav"
            };
            soundFile = $"{dkPrefix}{dkSuffix}";
            stream = EQAssetCache.Instance.GetSound(soundFile);
        }

        if (stream != null)
        {
            // Use the global UI sound player for local player footsteps/jumps
            // since we know its audio pipeline is 100% functional and local sounds
            // don't need 3D spatial attenuation.
            float sfxVol = _musicPlayer?.GetSfxVolume() ?? 0.8f;
            bool sfxMuted = _musicPlayer?.IsSfxMuted ?? false;
            
            if (!sfxMuted && sfxVol > 0f)
            {
                float finalVol = 2f; // Base volume (+2dB)
                
                // Silent if successfully sneaking or hiding
                if (_isSneaking || _isHiding)
                {
                    finalVol = -80f; // Godot's minimum dB (silent)
                }
                else if (_isCrouching)
                {
                    // If crouched but failed sneak check (or just crouching normally)
                    if (PlayerHasStealthSkill)
                        finalVol = -10f; // Even quieter if they have the skill but failed
                    else
                        finalVol = -4f;  // Quieter for normal crouch
                }

                if (finalVol > -80f)
                {
                    UISoundPlayer.Instance?.PlaySound(soundFile, finalVol);
                }
            }
        }
        else
        {
            GD.Print($"[Footstep] No sound found for model='{modelCode}' type='{moveType}'");
        }
    }

    /// <summary>Track airborne height/speed and send one <c>FALL_IMPACT</c> per landing (server applies damage).</summary>
    private void ProcessFallImpactTelemetry()
    {
        if (_playerCapsule == null) return;
        if (_flyMode || _teleportSettling || _playerCapsule.IsInWater)
        {
            _fallTracking = false;
            return;
        }

        bool onFloor = _playerCapsule.IsOnFloor();
        if (_playerLevitating)
        {
            _fallTracking = false;
            return;
        }

        if (!onFloor)
        {
            if (!_fallTracking)
            {
                _fallTracking = true;
                _fallPeakY = _playerCapsule.GlobalPosition.Y;
                _fallMaxDownSpeed = 0f;
            }
            else
            {
                _fallPeakY = Mathf.Max(_fallPeakY, _playerCapsule.GlobalPosition.Y);
                _fallMaxDownSpeed = Mathf.Max(_fallMaxDownSpeed, Mathf.Max(0f, -_playerCapsule.Velocity.Y));
            }
            return;
        }

        if (!_fallTracking)
            return;

        float fallDist = _fallPeakY - _playerCapsule.GlobalPosition.Y;
        float peakSpeed = _fallMaxDownSpeed;
        _fallTracking = false;

        if (_zoneImmunityTimer > 0f)
            return;

        if (fallDist < FallMinHeightReport && peakSpeed < 22f)
            return;

        double nowSec = Time.GetTicksMsec() * 0.001;
        if (nowSec - _lastFallImpactSentAt < FallImpactCooldownSec)
            return;

        _lastFallImpactSentAt = nowSec;

        var client = GetNodeOrNull<GameClient>("/root/GameClient");
        if (client == null) return;

        string fh = fallDist.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string sp = peakSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        client.SendRaw($"{{\"type\":\"FALL_IMPACT\",\"fallHeight\":{fh},\"impactSpeed\":{sp}}}");
    }
}
