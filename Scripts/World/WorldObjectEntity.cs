using Godot;

/// <summary>
/// PEQ zone <c>object</c> placeable (forge, loom, etc.): target ring + click-to-target like doors/NPCs.
/// </summary>
public partial class WorldObjectEntity : Node3D, ITargetable
{
    public string EntityName { get; private set; }
    public string EntityType => "world_object";
    public string ObjectId { get; private set; }
    public bool IsTargetable { get; set; } = true;

    private MeshInstance3D _targetRing;
    private Aabb _meshAabb;

    public void Setup(string displayName, string objectId, Aabb meshWorldAabb)
    {
        EntityName = displayName ?? "Object";
        ObjectId = objectId ?? "";
        _meshAabb = meshWorldAabb;
    }

    public void SetTargeted(bool targeted)
    {
        if (!IsTargetable) return;

        if (targeted)
        {
            if (_targetRing == null)
            {
                _targetRing = new MeshInstance3D { Name = "TargetRing" };
                var torus = new TorusMesh { InnerRadius = 0.6f, OuterRadius = 0.8f };
                var mat = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.35f, 0.85f, 1f, 0.75f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    EmissionEnabled = true,
                    Emission = new Color(0.2f, 0.6f, 1f),
                    EmissionEnergyMultiplier = 2f,
                };
                torus.Material = mat;
                _targetRing.Mesh = torus;

                float maxScale = Mathf.Max(_meshAabb.Size.X, _meshAabb.Size.Z);
                if (maxScale > 0.05f)
                    _targetRing.Scale = new Vector3(maxScale, 1f, maxScale);

                // World-space AABB: sit ring on the ground (min Y), centered on footprint — not mid-mesh.
                Vector3 baseCenterWorld = new Vector3(
                    _meshAabb.Position.X + _meshAabb.Size.X * 0.5f,
                    _meshAabb.Position.Y + 0.06f,
                    _meshAabb.Position.Z + _meshAabb.Size.Z * 0.5f);
                _targetRing.Position = ToLocal(baseCenterWorld);
                AddChild(_targetRing);
            }
            _targetRing.Visible = true;
        }
        else if (_targetRing != null)
        {
            _targetRing.Visible = false;
        }
    }

    public void OnInputEvent(Node camera, InputEvent @event, Vector3 position, Vector3 normal, long shapeIdx)
    {
        if (!IsTargetable) return;
        if (@event is not InputEventMouseButton mouseBtn || !mouseBtn.Pressed) return;

        WorldManager wm = WorldManager.ResolveFromDescendant(this);
        if (wm == null) return;

        if (mouseBtn.ButtonIndex == MouseButton.Left || mouseBtn.ButtonIndex == MouseButton.Right)
            wm.SetTarget(this);
    }
}
