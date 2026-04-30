using Godot;

public interface ITargetable
{
    string EntityName { get; }
    string EntityType { get; }
    Vector3 GlobalPosition { get; }
    void SetTargeted(bool targeted);
}
