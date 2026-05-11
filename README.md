# 🌑 Everquest.Godot (Client)

*The world of Norrath has grown dark. The ancient forests of the Faydark are no longer safe, and the shadows have a life of their own. Steel your nerves, traveler.*

**EQ.gd** is an immersive, atmospheric reimagining of the classic EverQuest experience, built on the Godot engine. This is not just a trip down memory lane; it is a descent into a world where the night is long and the dangers are real.

### 📜 The Traveler's Code (Warnings)
- **Bring Your Own Steel:** This project **requires a legal EverQuest installation** (preferably RoF2). The engine dynamically extracts models, zones, and secrets directly from your own files at runtime.
- **Zero Coinage:** This is a fan project with **zero monetization**. If you wish to support Norrath, please head to [EverQuest.com](https://www.everquest.com) and support the original developers.
- **WIP:** We are currently in **Phase 1**. Expect bugs, ghosts in the machine, and shifting sands.

### 🔌 Server & editor setup (open source contributors)

1. **Godot:** 4.6+ with **C# / .NET** enabled (see `project.godot` features). Open this folder as the project root.
2. **Game server:** Run the Node stack from **`../server/`** (MariaDB + Redis + `npm run cluster`). **Start here:** **`../server/README_SETUP.md`** (full install, database, hosting expectations); **`../server/README.md`** for Node-only overview.
3. **Default WebSocket URL:** `GameClient` defaults to **`ws://localhost:3005`** (login). Change in the client UI or in code if your login port differs.
4. **Coordinates & pipeline:** **`DEVELOPER_REFERENCE.md`** in this folder — EQ ↔ Godot swizzles, Lantern vs server-space, DB bootstrap order.

There is **no server lobby** in the client yet (pick-a-server UI is a future goal). You set the WebSocket URL for **your** stack. Policy text lives in **`../server/README_SETUP.md`**.

### 🕯️ Immersive Features
- **The Living Dark:** A completely overhauled lighting and vision system. Infravision and Ultravision aren't just toggles—they are survival tools in a world that respects the darkness.
- **Shadow & Silence:** A reworked Hide and Sneak system that emphasizes position and atmosphere.
- **Secrets of the Earth:** A new Mining tradeskill for those brave enough to plumb the depths.
- **Elemental Fury:** Real-time weather systems that affect the world and your survival.
- **Expanded Destinies:** New Class/Race combinations and a deep, faction-based world.
- **Gothic Immersion:** A UI and atmosphere designed to make every encounter feel heavy and meaningful.


### How to Play
The current build of EQ.gd is for local servers only.
The MMO server should be up soon.
You will need to set up your own server.
- **[Everquest.godot Server:](https://github.com/KaelKodes/Everquest-Godot-Server)** I have tried to provide instructions as best I can at this time, things are still moving around quickly. Once you have the server running, it gets much easier.
- **[EQ.gd Launcher:](https://github.com/KaelKodes/Everquest-Godot-Launcher)** This is the real face of the project. Right now it just handles the Client-side but includes everything needed for it to read your database.
- **Support:** Because this is in active development it is hard for me to provide much support, as I myself am still trying to get it all working. Please be have patience and expect much better documentation once its closer to 100% stable!
---
*Safe travels, and keep your torch lit.*
