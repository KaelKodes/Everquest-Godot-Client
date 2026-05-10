# EQMUD Technical Reference

This document serves as a definitive reference for AI agents and developers working on the `EQMUD` project.

## 1. Coordinate Mapping (EQ to Godot)

### 1.1 Extracted Zone Assets (LanternExtractor)
The raw EQ coordinates extracted by LanternExtractor into `object_instances.txt` are mapped into Godot's space using a **reflection** swizzle in `Scripts/AssetPipeline/ZoneObjectPlacer.cs` (`EQToGodot`):

```csharp
Godot.X = -EQ.Z
Godot.Y = EQ.Y (Lantern height axis in extract data)
Godot.Z = -EQ.X
```

**Rotation for Extracted Assets:**  
Because this mapping is a **reflection**, all three Euler angles must be inverted, and a **-90 degree offset** must be applied to the **Yaw (Y-axis)**.

```csharp
instance.Rotation = new Vector3(Mathf.DegToRad(-rotX), Mathf.DegToRad(-rotY - 90f), Mathf.DegToRad(-rotZ));
```

### 1.2 Server / Database Entities (NPCs, Doors, Player, Spawns)
Coordinates from the **game server** (EQEmu-style: **Z is height**) use the same **rotation-style** swizzle across `Scripts/World/WorldManager.Doors.cs`, `WorldManager.Entities.cs`, and `WorldManager.Player.cs`:

```csharp
Godot.X = -EQ.X
Godot.Y = EQ.Z   // EQ height → Godot Y
Godot.Z = -EQ.Y
```

**Rotation (important split):**

| Kind | Code | Yaw |
| :--- | :--- | :--- |
| **Doors** (DB mesh placement) | `WorldManager.Doors.cs` | `godotYaw = (heading / 512f) * 360f`, then **`RotationDegrees = (0, godotYaw - 90f, 0)`** — the **-90°** aligns door model facing with Godot forward. |
| **NPCs / mobs** (live entity sync) | `WorldManager.Entities.cs` | `godotYaw = (rawHeading / 512f) * 360f`, **`RotationDegrees = (0, godotYaw, 0)`** — **no** extra -90; comments in code state no reflection offset is needed for this path. |
| **Player** | `WorldManager.Player.cs` | Same **position** swizzle as entities; facing is driven by the player capsule / camera, not the door formula. |

Heading convention (EQEmu): **0–512** (0 = North, 128 = West, 256 = South, 384 = East).

---

## 1.3 Lighting Pipeline (Godot + Lantern)

### Runtime zone/object lighting

- Zone terrain GLBs are loaded at runtime via `GltfDocument` in `Scripts/World/WorldManager.Zone.cs`.
- Lantern exports carry a negative-determinant transform chain, so normals must be corrected after load.
- `BakeFlippedNormalsRecursive(...)` now supports `recomputeNormals`:
  - Zone terrain path calls `recomputeNormals: true` (rebuild from geometry, then apply reflection compensation).
  - Character/object paths keep `recomputeNormals: false` and only apply flip compensation.
- This is the key fix for the "omni only lights ground in one direction" terrain artifact.

### Light source spawning

- Dynamic light props (torch/lantern/campfire/brazier/etc.) are spawned from object instances in `ZoneObjectPlacer.AddLightIfSource(...)`.
- Baked zone lights are spawned from `Zone/light_instances.txt` in `ZoneObjectPlacer.PlaceLights(...)` (this was previously stubbed).
- `light_instances.txt` format:
  - `PosX, PosY, PosZ, Radius, ColorR, ColorG, ColorB`
  - Converted with `EQToGodot(...)`.

### `object_lights.json` behavior

- Type defaults (e.g. `torch`, `lantern`) are loaded from `Data/object_lights.json` with built-in fallback defaults if file is missing.
- Per-model overrides are supported via:
  - `"models": { "<exact-lowercase-model-name>": { ... } }`
- Override resolution: model key first, then type default.
- Supported keys:
  - `energy`, `range`, `position`, `color`, `sound`, `soundVolume`
  - `castShadows` (default follows runtime `ShadowsEnabled`)
  - `sourceCastsShadow` (default `false`; keeps source mesh from self-occluding its own omni)
  - `pairedOmni` (optional fallback to create a second opposite-orientation omni and split energy)

### Held player/NPC lights

- Held light source is managed in `EntityCapsule`.
- `SetLightSource(true)` now:
  - creates the omni if missing,
  - enables it,
  - applies canonical settings,
  - attaches/syncs it to lantern/hand anchor.
- `SetLightSource(false)` cleanly hides it and disables lantern emissive.

---

## 2. Server Infrastructure & Boot Sequence

### 2.1 Database Connectivity
Configuration is read from `.env` in the **`server/`** directory (`server/eqemu_db.js`):

- **`EQEMU_HOST`** — defaults to `127.0.0.1` if unset. Use **`host.docker.internal`** when the Node process runs on the Windows host or WSL and MariaDB listens **inside Docker** (so `localhost` would not reach the container). If the DB is reachable on the host loopback (e.g. published port), `127.0.0.1` is fine.
- **`EQEMU_PORT`** — defaults to **`3307`** if unset (common when Docker maps host `3307` → container `3306`).

### 2.2 Initialization Order
The critical invariant: **the DB pool must finish initialization (including the zone metadata query that fills `ZONE_ID_TO_SHORT`) before the process accepts player connections.** Otherwise fast-connecting clients can get `zone_<id>` instead of a proper short name (e.g. `felwithea`).

**Primary: zone cluster — `server/zone_server.js`** (via `server/master.js` / `npm run cluster`)

**Reference / legacy monolith — `Reference/server_monolith/index.js`** (`npm run monolith` from `server/`)

Monolith init order:

1. **`await engine.bootstrapServer()`** — calls **`DB.initDatabase()`** → `eqemu_db.init()`: creates pool, runs migrations/custom tables, loads **`ZONE_ID_TO_SHORT`** and related zone caches from the `zone` table. Also initializes core engine subsystems (`CombatSystem`, `SpellSystem`, etc.). **`ZoneSystem.initZones()` is not called here** (it was removed from bootstrap; zones load on demand).
2. **`engine.startGameLoop()`** — starts the tick loop.
3. Express + WebSocket setup, then **`server.listen(PORT)`** — default **`PORT`** is **3005** (`process.env.PORT || 3005`).

**Zone cluster entrypoint — `server/zone_server.js`** (same bootstrap as above, then broker + `initZones([])`)

1. **`await engine.bootstrapServer()`** — same DB and system init as above.
2. **`await broker.init()`** — cluster broker.
3. **`await engine.initZones([])`** — explicit zone init (empty array = no full pre-load; routing still set up for the node).
4. **`engine.startGameLoop()`**, then listen — default port **3010** (`process.env.PORT || 3010`).

---

## 3. Account & Login Rules

- **Current implementation (`server/eqemu_db.js`, `loginAccount`):** looks up accounts with `SELECT ... FROM account WHERE name = ?` using the **exact** string the client sends. Case-insensitive login depends on **MySQL collation** for `account.name`; do not assume `gmkael` and `GMKael` match unless the schema/collation guarantees it.
- **Recommended if you want explicit case-insensitive logins:** use `WHERE LOWER(name) = LOWER(?)` (and the same pattern anywhere else account name is matched).
- **Authentication vs connectivity:** A failed TCP/DB connection (`ECONNREFUSED`, etc.) must not be reported to the player as “account not found.” Check server logs for connection errors.

---

## 4. Summary Table

| Data Source | Height axis in source | Godot position | Rotation / yaw |
| :--- | :--- | :--- | :--- |
| **Lantern Extractor** (`object_instances.txt`) | `EQ.Y` | `(-Z, Y, -X)` reflection | Invert Euler X/Y/Z; **-90°** on yaw |
| **MySQL doors** (meshes from DB) | `EQ.Z` | `(-X, Z, -Y)` | `(heading/512)*360` then **-90°** on Godot yaw |
| **Live NPCs/mobs** (server sync) | `EQ.Z` | `(-X, Z, -Y)` | `(heading/512)*360`, **no** extra -90° |
| **Player spawn / teleport** | `EQ.Z` | `(-X, Z, -Y)` (+ client height boost for floor snap) | Handled via player capsule / server heading sync, not the door formula |
