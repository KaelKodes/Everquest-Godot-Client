# EQMUD Technical Reference

This document serves as a definitive reference for AI agents and developers working on the `EQMUD` project.

## 1. Coordinate Mapping (EQ to Godot)

### 1.1 Extracted Zone Assets (LanternExtractor)
The raw EQ coordinates extracted by LanternExtractor into `object_instances.txt` are mapped into Godot's space using a **reflection** swizzle in `ZoneObjectPlacer.cs`:

```csharp
Godot.X = -EQ.Z
Godot.Y = EQ.Y (Lantern-swapped Height)
Godot.Z = -EQ.X
```

**Rotation for Extracted Assets:**
Because this mapping is a **reflection**, all three Euler angles must be inverted, and a **-90 degree offset** must be applied to the **Yaw (Y-axis)**.

```csharp
instance.Rotation = new Vector3(Mathf.DegToRad(-rotX), Mathf.DegToRad(-rotY - 90f), Mathf.DegToRad(-rotZ));
```

### 1.2 Database Entities (NPCs, Doors, Spawns)
Coordinates pulled directly from the **EQEmu MySQL Database** use the original EQ coordinate system (where **Z is Height**). These are mapped using a **180-degree rotation** swizzle in `WorldManager.cs`:

```csharp
Godot.X = -EQ.X
Godot.Y = EQ.Z (True EQ Height)
Godot.Z = -EQ.Y
```

**Rotation for Database Entities:**
Because this is a **rotation** and not a reflection, the yaw should be **positive**. Apply a **-90 degree offset** to align model fronts with Godot forward.

```csharp
doorNode.RotationDegrees = new Vector3(0, godotYaw - 90f, 0);
```

---

## 2. Server Infrastructure & Boot Sequence

### 2.1 Database Connectivity
- **Host:** For the Node.js server running on Windows/WSL to reach the MariaDB container in Docker, the host **must** be `host.docker.internal` in the `.env` file.
- **Port:** Default is `3307` (mapped from Docker's 3306).

### 2.2 Initialization Chain (`index.js`)
The server MUST initialize in this order to avoid race conditions (like the "falling through the world" bug):
1. **`DB.initDatabase()`**: Establishes pool and **must** populate `ZONE_ID_TO_SHORT` cache.
2. **`engine.initZones()`**: Loads world data (spells, items) and zone-specific data (spawns, doors).
3. **`server.listen()`**: Port 3005 is opened **ONLY after** the above are complete. 

*Note: If port 3005 is opened before the zone cache is ready, fast-connecting clients will fail to resolve their zone names and default to "zone_ID" instead of "felwithea".*

---

## 3. Account & Login Rules
- **Case-Insensitivity:** Database queries for accounts should use `LOWER(name) = LOWER(?)` to allow players to log in regardless of how they capitalized their username (e.g., `gmkael` vs `GMKael`).
- **Authentication:** Failed DB connections should never be interpreted as "Account not found." Check server logs for `ECONNREFUSED` if login fails suddenly.

---

## 4. Summary Table

| Data Source | Height Axis | Godot Mapping | Rotation Logic |
| :--- | :--- | :--- | :--- |
| **Lantern Extractor** | `EQ.Y` | `(-Z, Y, -X)` (Reflected) | Invert all, -90 offset |
| **MySQL Database** | `EQ.Z` | `(-X, Z, -Y)` (Rotated) | **Positive Yaw**, -90 offset |
