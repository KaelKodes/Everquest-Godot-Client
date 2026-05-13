using Godot;

public partial class DoorEntity : Node3D, ITargetable
{
    public enum DoorBehavior { Swinging, Sliding, Portcullis, Elevator }

    public string EntityName { get; private set; }
    public string EntityType => "door";
    public string DoorId { get; private set; }
    public int LocalDoorId { get; private set; }
    public int TriggerDoor { get; private set; }
    public int DoorParam { get; private set; }
    public bool InvertState { get; private set; }
    public bool IsOpen { get; private set; }
    public bool IsTargetable { get; set; } = true;
    public DoorBehavior Behavior { get; private set; }

    private MeshInstance3D _targetRing;
    private Aabb _doorAabb;
    private Vector3 _localMeshCenter;
    public Vector3 BasePosition { get; private set; }
    public Vector3 BaseRotation { get; private set; }
    private Tween _doorTween;
    private AnimatableBody3D _platformBody;
    private float _lastSwingAngle = 90f;

    public DoorEntity()
    {
    }

    public void Setup(string name, string doorId, int localDoorId, int triggerDoor, Aabb doorAabb, int doorParam, bool invertState, int opentype, AnimatableBody3D platformBody)
    {
        EntityName = name;
        DoorId = doorId;
        LocalDoorId = localDoorId;
        TriggerDoor = triggerDoor;
        _doorAabb = doorAabb;
        DoorParam = doorParam;
        InvertState = invertState;
        BasePosition = Position;
        BaseRotation = RotationDegrees;
        _platformBody = platformBody;
        
        Vector3 worldCenter = doorAabb.Position + doorAabb.Size / 2f;
        _localMeshCenter = ToLocal(worldCenter);

        if (opentype == 59 || name.ToUpper().Contains("LEVATOR")) Behavior = DoorBehavior.Elevator;
        else if (opentype == 65 || opentype == 40) Behavior = DoorBehavior.Portcullis;
        else if (opentype == 25 || opentype == 26 || opentype == 27) Behavior = DoorBehavior.Sliding;
        else Behavior = DoorBehavior.Swinging;

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
        Vector3 targetRot = BaseRotation;
        
        // If inverted, false = open (up), true = closed (down)
        bool effectivelyOpen = InvertState ? !isOpen : isOpen;
        
        float duration = Behavior == DoorBehavior.Elevator ? 5.0f : 1.0f;

        if (effectivelyOpen)
        {
            if (Behavior == DoorBehavior.Elevator || Behavior == DoorBehavior.Portcullis)
            {
                targetPos.Y += DoorParam;
            }
            else if (Behavior == DoorBehavior.Sliding)
            {
                // Slide along the local right vector (X-axis relative to rotation)
                float slideDist = DoorParam != 0 ? DoorParam : 10f;
                targetPos += Transform.Basis.X * slideDist;
            }
            else if (Behavior == DoorBehavior.Swinging)
            {
                float angle = InvertState ? -90f : 90f;
                
                // Dynamically pick +90 or -90 to swing away from player
                var wm = WorldManager.ResolveFromDescendant(this);
                if (wm != null && wm.PlayerPosition != Vector3.Zero)
                {
                    Basis rotPlus90 = Basis.FromEuler(new Vector3(0, Mathf.Pi / 2f, 0));
                    Vector3 localCenterPlus90 = rotPlus90 * _localMeshCenter;
                    Vector3 worldCenterPlus90 = ToGlobal(localCenterPlus90);
                    
                    Basis rotMinus90 = Basis.FromEuler(new Vector3(0, -Mathf.Pi / 2f, 0));
                    Vector3 localCenterMinus90 = rotMinus90 * _localMeshCenter;
                    Vector3 worldCenterMinus90 = ToGlobal(localCenterMinus90);
                    
                    float distPlus90 = worldCenterPlus90.DistanceTo(wm.PlayerPosition);
                    float distMinus90 = worldCenterMinus90.DistanceTo(wm.PlayerPosition);
                    
                    angle = distPlus90 > distMinus90 ? 90f : -90f;
                }
                
                _lastSwingAngle = angle;
                targetRot.Y += angle;
            }
        }
        else if (Behavior == DoorBehavior.Swinging)
        {
            // Reverse whatever angle we used to open it
            // targetRot is already BaseRotation, so we do nothing here, it will naturally tween back to BaseRotation.
        }

        // We want to tween the platformBody's local position relative to DoorEntity.
        Vector3 localTargetOffset = targetPos - BasePosition;

        if (instant)
        {
            if (_platformBody != null)
                _platformBody.Position = localTargetOffset;
            else
                Position = targetPos;
            
            RotationDegrees = targetRot;
            return;
        }

        if (_platformBody != null)
        {
            _doorTween.TweenProperty(_platformBody, "position", localTargetOffset, duration)
                      .SetTrans(Tween.TransitionType.Sine)
                      .SetEase(Tween.EaseType.InOut);
        }
        else if (Behavior == DoorBehavior.Swinging)
        {
            _doorTween.TweenProperty(this, "rotation_degrees", targetRot, duration)
                      .SetTrans(Tween.TransitionType.Sine)
                      .SetEase(Tween.EaseType.InOut);
        }
        else
        {
            // Fallback for non-elevator translation (Portcullis, Sliding)
            _doorTween.TweenProperty(this, "position", targetPos, duration)
                      .SetTrans(Tween.TransitionType.Sine)
                      .SetEase(Tween.EaseType.InOut);
        }
        
        GD.Print($"[WORLD] Door {DoorId} (Type: {Behavior}) {(isOpen ? "opening" : "closing")} to Pos: {targetPos}, Rot: {targetRot}");
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

                float maxScale = Mathf.Max(_doorAabb.Size.X, _doorAabb.Size.Z);
                if (maxScale > 0)
                {
                    _targetRing.Scale = new Vector3(maxScale, 1f, maxScale);
                }

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
                var wm = WorldManager.ResolveFromDescendant(this);
                if (wm != null)
                {
                    wm.SetTarget(this);
                }
            }
            else if (mouseBtn.ButtonIndex == MouseButton.Right)
            {
                var wm = WorldManager.ResolveFromDescendant(this);
                if (wm != null)
                {
                    if (wm.CurrentTargetId == this.Name)
                    {
                        if (GameClient.Instance != null)
                        {
                            GameClient.Instance.SendRaw($"{{\"type\": \"DOOR_CLICK\", \"door_id\": {DoorId}}}");
                            GD.Print($"[WORLD] Sent DOOR_CLICK for door {DoorId} ({EntityName})");
                        }
                    }
                    else
                    {
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
