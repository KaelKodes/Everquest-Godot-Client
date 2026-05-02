using Godot;
using System;

/// <summary>
/// Attach this script to a GPUParticles3D node to automatically apply EQ spell textures
/// and clean up the node when the emission finishes.
/// </summary>
public partial class EQParticleSystem : GpuParticles3D
{
    [Export] 
    public bool IsAura = false;
    
    [Export] 
    public int SpellAnimId = -1;
    
    [Export] 
    public string TextureOverride = "";

    private StandardMaterial3D _material;

    public override void _Ready()
    {
        // One shot particles should auto-delete when done
        if (!IsAura)
        {
            OneShot = true;
            Emitting = true;
            Finished += OnFinished;
        }

        // Setup the visual material for the particles
        _material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, // EQ effects are usually additive
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            VertexColorUseAsAlbedo = true
        };

        MaterialOverride = _material;

        // Apply texture if one was preset in the inspector
        if (SpellAnimId >= 0 || !string.IsNullOrEmpty(TextureOverride))
        {
            ApplyTexture();
        }
    }

    /// <summary>
    /// Call this when spawning the particle system to initialize its visual state.
    /// </summary>
    public void PlayAnim(int animId)
    {
        SpellAnimId = animId;
        ApplyTexture();
        
        if (!IsAura)
        {
            Restart();
        }
        else
        {
            Emitting = true;
        }
    }

    private void ApplyTexture()
    {
        string texName = TextureOverride;
        
        // If no explicit override, lookup the texture by SpellAnimId
        if (string.IsNullOrEmpty(texName) && SpellAnimId >= 0)
        {
            texName = SpellEffectManager.Instance.GetPrimaryTextureForSpellAnim(SpellAnimId);
        }

        if (!string.IsNullOrEmpty(texName))
        {
            var tex = SpellEffectManager.Instance.GetParticleTexture(texName);
            if (tex != null)
            {
                _material.AlbedoTexture = tex;
            }
            else
            {
                GD.PrintErr($"[EQParticleSystem] Could not load texture for '{texName}'.");
            }
        }
    }

    private void OnFinished()
    {
        QueueFree();
    }
}
