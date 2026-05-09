# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**The Knight** is a Unity 6.0.1.3f1 top-down action game built on **TopDownEngine v4.4** (by MoreMountains), with Unity ML-Agents v4.0.0 for AI training and procedural map generation for roguelike gameplay.

## Build & Development

This is a Unity project — there is no CLI build script. All building is done through the Unity Editor.

- **Open project:** Launch Unity Hub → Open → select the project root (parent of `/Assets`)
- **Play in Editor:** Press Play in Unity; the main development scene is `Assets/TopDownEngine/Demos/Koala2D/MyPGC.unity`
- **Standalone build output:** `/Build/The Knight.exe` (Windows)
- **ML-Agents training:** Run `mlagents-learn <config>.yaml --run-id=<RunName>` from project root, then press Play in Editor
  - Config files: `Assets/Scripts/Agent/CombatAgentConfig.yaml`, `Assets/Scripts/Agent/melee_config.yaml`
  - Training results: `Assets/Scripts/Agent/results/`

## Architecture

### Two-Layer Design

**Framework layer** — `Assets/TopDownEngine/` — do not modify unless patching a bug. Core managers live in `Common/Scripts/Managers/`:
- `GameManager.cs` — global game state and lives
- `LevelManager.cs` — spawn/respawn/checkpoint logic (has been customized)
- `GUIManager.cs` — death/pause screen control
- `InputManager.cs` — input routing

**Game layer** — `Assets/Scripts/` — all custom game code:
- `Agent/` — ML-Agents reinforcement learning
- `PGCMap/` — procedural tilemap generation

### Character System

Characters are assembled from `MonoBehaviour` components attached to a `Character` GameObject:
- `Character.cs` — root controller, determines Player vs AI
- `TopDownController2D` — physics/movement
- `CharacterMovement`, `CharacterOrientation2D` — locomotion and facing
- `Health.cs` — damage, death, respawn events
- `CharacterHandleWeapon.cs` — weapon equip/fire

AI characters use an `AIBrain` state machine composed of `AIAction` and `AIDecision` components. Custom ML agents (`CombatAgent`, `MeleeAgent`) extend `Agent` (Unity ML-Agents) and override this with neural-network-driven actions.

### ML Agents

**CombatAgent** (`Assets/Scripts/Agent/CombatAgent.cs`):
- 321-dimensional observation vector: player stats (13), global (6), enemies (54), bullets (120), items (68), hazards (60)
- 5 discrete actions: Moving, Deciding, Attacking, Dashing, MoveAway
- PPO trainer with curiosity reward signal

**MeleeAgent** (`Assets/Scripts/Agent/MeleeAgent.cs`):
- Simpler 3-state agent (Detecting → Moving → Attacking)
- Action locking for timing-sensitive abilities

**TrainingManager** (`Assets/Scripts/Agent/TrainingManager.cs`) listens to TopDownEngine events (`PlayerDeath`, `RespawnComplete`, etc.) to reset episodes without reloading scenes.

### Procedural Map Generation

`TilemapGenerator.cs` reads text-asset room layouts (0 = floor, 1 = wall, letters = enemy spawn markers) and renders them to Unity Tilemaps at runtime.

- Editor tool: **Tools → PGC → Room Generator**
- Supports single TextAsset or random selection from a folder

### Event System

Communication between systems uses TopDownEngine's event bus. Subscribe via:
```csharp
this.MMEventStartListening<TopDownEngineEvent>();
// implement MMEventListener<TopDownEngineEvent>
public void OnMMEvent(TopDownEngineEvent e) { ... }
```

Key event types: `PlayerDeath`, `RespawnStarted`, `RespawnComplete`, `GameOver`, `UnPause`.

## Key Files

| Purpose | Path |
|---|---|
| Active dev scene | `TopDownEngine/Demos/Koala2D/MyPGC.unity` |
| ML combat agent | `Scripts/Agent/CombatAgent.cs` |
| ML melee agent | `Scripts/Agent/MeleeAgent.cs` |
| Training episode manager | `Scripts/Agent/TrainingManager.cs` |
| Agent setup editor tool | `Scripts/Agent/Editor/KoalaAgentSetup.cs` |
| Tilemap generator | `Scripts/PGCMap/TilemapGenerator.cs` |
| Level/respawn manager | `TopDownEngine/Common/Scripts/Managers/LevelManager.cs` |
| Death/pause UI | `TopDownEngine/Common/Scripts/Managers/GUIManager.cs` |
| Death screen analysis | `DEATH_SCREEN_ANALYSIS.md` |

## Notes

- Custom scripts contain Vietnamese comments — this is expected.
- `TopDownEngine/` is a third-party asset; prefer extending it rather than modifying it directly. When modifications are necessary (as in `LevelManager.cs`), document the change clearly.
- The project uses Unity's new Input System package, not the legacy `Input` class.
