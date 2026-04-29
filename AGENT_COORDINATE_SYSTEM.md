# EverQuest to Godot Coordinate Mapping

This document serves as a definitive reference for AI agents and developers working on the `EQMUD` project to understand how EverQuest (EQ) coordinates map to our Godot environment.

## 1. Positional Swizzling
The raw EQ coordinates extracted by LanternExtractor are mapped into Godot's space using the following swizzle in `ZoneObjectPlacer.cs`:

```csharp
Godot.X = -EQ.Z
Godot.Y = EQ.Y (Height/Up)
Godot.Z = -EQ.X
```
This transformation effectively:
1. Swaps the horizontal `X` and `Z` axes.
2. Negates both of them.

*Note for Agents: If you are sending packets to the server or placing custom objects, you must reverse this operation.*

## 2. Object Rotations (Euler YXZ)
Because the horizontal plane is swapped and negated `(-Z, Y, -X)`, the mapping is a **reflection**.

Godot applies rotations in **Y-X-Z** (Yaw, Pitch, Roll) order. To correct for the positional reflection without breaking local axes, **all three Euler angles must be inverted**, and a **-90 degree offset** must be applied exclusively to the **Yaw (Y-axis)** to account for the physical X and Z axis base swap.

```csharp
// Correct Rotation Mapping
instance.Rotation = new Vector3(
    Mathf.DegToRad(-rotX), // Inverted Pitch 
    Mathf.DegToRad(-rotY - 90f), // Inverted Yaw + Base Offset
    Mathf.DegToRad(-rotZ)  // Inverted Roll
);
```

**Why Invert Everything?**
When you mirror a coordinate system across `X = -Z`, a rotation `(X, Y, Z)` in the original space must become `(-X, -Y, -Z)` in the reflected space to point at the same physical target. If you do not invert them, half of the orientations (like North/South) might match by coincidence due to the base offset, but the other half (like East/West) will be exactly 180 degrees backwards.

## 3. Server Packets
When sending movement updates to the EQEmu server, Godot coordinates must be mapped back to EQ:
```json
{
  "type": "UPDATE_POS",
  "x": -Godot.X,
  "y": -Godot.Z
}
```
*(Y in EQ network packets is North/South, which corresponds to Godot's Z axis. Height is mapped as Z).*
