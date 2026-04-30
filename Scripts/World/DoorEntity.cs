using Godot;

public partial class DoorEntity : Node3D, ITargetable
{
    public string EntityName { get; private set; }
    public string EntityType => "door";
    public string DoorId { get; private set; }
    public int LocalDoorId { get; private set; }
    public int TriggerDoor { get; private set; }
    public int DoorParam { get; private set; }
    public bool InvertState { get; private set; }
    public bool IsOpen { get; private set; }
    public bool IsTargetable { get; set; } = true;

    private MeshInstance3D _targetRing;
    private Aabb _doorAabb;
    public Vector3 BasePosition { get; private set; }
    private Tween _doorTween;
    private AnimatableBody3D _platformBody;

    public DoorEntity()
    {
    }

    public void Setup(string name, string doorId, int localDoorId, int triggerDoor, Aabb doorAabb, int doorParam, bool invertState, AnimatableBody3D platformBody)
    {
        EntityName = name;
        DoorId = doorId;
        LocalDoorId = localDoorId;
        TriggerDoor = triggerDoor;
        _doorAabb = doorAabb;
        DoorParam = doorParam;
        InvertState = invertState;
        BasePosition = Position;
        _platformBody = platformBody;

        // Apply initial closed state instantly without tweening
        SetOpenState(false, true);
    }

    public void SetOpenState(bool isOpen, bool instant = false)
    {
        if (IsOpen == isOpen && !instant) return;
        IsOpen = isOpen;

        if (_doorTween != null && _doorTween.IsValid())
        {
            _doorTween.Kill();
        }

        if (!instant)
        {
            _doorTween = CreateTween();
            
            // Ensure physics syncing is enabled before moving so the platform carries the player
            if (_platformBody != null)
            {
                _platformBody.SyncToPhysics = true;
            }
        }
        
        Vector3 targetPos = BasePosition;
        
        // If inverted, false = open (up), true = closed (down)
        bool effectivelyOpen = InvertState ? !isOpen : isOpen;
        
        if (effectivelyOpen)
        {
            targetPos.Y += DoorParam;
        }

        // We want to tween the platformBody's local position relative to DoorEntity.
        Vector3 localTargetOffset = targetPos - BasePosition;

        if (instant)
        {
            if (_platformBody != null)
            {
                _platformBody.Position = localTargetOffset;
            }
            else
            {
                Position = targetPos;
            }
            return;
        }

        // Tween over 5 seconds (standard slow elevator speed)
        if (_platformBody != null)
        {
            _doorTween.TweenProperty(_platformBody, "position", localTargetOffset, 5.0f)
                      .SetTrans(Tween.TransitionType.Sine)
                      .SetEase(Tween.EaseType.InOut);
        }
        else
        {
            // Fallback for non-elevator doors
            _doorTween.TweenProperty(this, "position", targetPos, 5.0f)
                      .SetTrans(Tween.TransitionType.Sine)
                      .SetEase(Tween.EaseType.InOut);
        }
        
        GD.Print($"[WORLD] Door {DoorId} {(isOpen ? "opening" : "closing")} to {targetPos}");
    }

    public void SetTargeted(bool targeted)
    {
        if (!IsTargetable) return;
        
        if (targeted)
        {
            if (_targetRing == null)
            {
                _targetRing = new MeshInstance3D { Name = "TargetRing" };
                var torus = new TorusMesh
                {
                    InnerRadius = 0.6f,
                    OuterRadius = 0.8f
                };
                var mat = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.3f, 0.3f, 0.8f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.2f, 0.2f),
                    EmissionEnergyMultiplier = 2f
                };
                torus.Material = mat;
                _targetRing.Mesh = torus;

                // Scale ring based on the door's AABB size if needed, or just use a fixed size
                float maxScale = Mathf.Max(_doorAabb.Size.X, _doorAabb.Size.Z);
                if (maxScale > 0)
                {
                    _targetRing.Scale = new Vector3(maxScale, 1f, maxScale);
                }

                // Place target ring at the bottom of the door
                _targetRing.Position = new Vector3(_doorAabb.Position.X + _doorAabb.Size.X / 2f, _doorAabb.Position.Y + 0.1f, _doorAabb.Position.Z + _doorAabb.Size.Z / 2f);
                AddChild(_targetRing);
            }
            _targetRing.Visible = true;
        }
        else
        {
            if (_targetRing != null)
                _targetRing.Visible = false;
        }
    }

    public void OnInputEvent(Node camera, InputEvent @event, Vector3 position, Vector3 normal, long shapeIdx)
    {
        if (!IsTargetable) return;

        if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left)
            {
                var wm = GetTree().Root.GetNodeOrNull<WorldManager>("MainScene/WorldManager") ?? GetParent().GetParent() as WorldManager;
                if (wm != null)
                {
                    wm.SetTarget(this);
                }
            }
            else if (mouseBtn.ButtonIndex == MouseButton.Right)
            {
                var wm = GetTree().Root.GetNodeOrNull<WorldManager>("MainScene/WorldManager") ?? GetParent().GetParent() as WorldManager;
                if (wm != null)
                {
                    if (wm.CurrentTargetId == this.Name)
                    {
                        if (GameClient.Instance != null)
                        {
                            GameClient.Instance.SendRaw($"{{\"type\": \"DOOR_CLICK\", \"door_id\": {DoorId}}}");
                            GD.Print($"[WORLD] Sent DOOR_CLICK for door {DoorId} ({EntityName})");
                        }
                        else
                        {
                            GD.PrintErr("[WORLD] Cannot interact with door: GameClient.Instance is null");
                        }
                    }
                    else
                    {
                        GD.Print($"[WORLD] Right-clicked door {DoorId}, but it is not the current target. Current target: {wm.CurrentTargetId}");
                        // Auto-target it if they right-click it, to make it more user friendly
                        wm.SetTarget(this);
                        if (GameClient.Instance != null)
                        {
                            GameClient.Instance.SendRaw($"{{\"type\": \"DOOR_CLICK\", \"door_id\": {DoorId}}}");
                            GD.Print($"[WORLD] Auto-targeted and sent DOOR_CLICK for door {DoorId} ({EntityName})");
                        }
                    }
                }
            }
        }
    }
}
