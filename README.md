# The Knight

A top-down 2D roguelike action game built in Unity with reinforcement learning-trained AI agents and procedurally generated dungeons. Developed as a thesis project exploring generalizable RL agents using curriculum learning and intrinsic motivation.

---

## Overview

**The Knight** combines a Soul Knight-style top-down combat game with a full ML-Agents training environment. The player (or RL agent) navigates procedurally generated dungeons, fights enemies, and uses weapons and dashes in real-time combat.

Key research goals:
- Train a generalizable combat agent using **PPO + Curiosity (ICM)**
- Apply **curriculum learning** across difficulty tiers (Easy → Hard)
- Use **procedurally generated text-based maps** as the training environment

---

## Tech Stack

| Component | Technology |
|---|---|
| Game Engine | Unity 2022.x |
| RL Framework | [Unity ML-Agents v4.0.0](https://github.com/Unity-Technologies/ml-agents) |
| Game Framework | TopDownEngine v4.4 (MoreMountains) |
| Language | C# |
| Training | Python `mlagents-learn` (PPO / LSTM / ICM / RND) |

---

## Project Structure

```
The Knight/
├── Assets/
│   ├── Scripts/
│   │   ├── Agent/
│   │   │   ├── CombatAgent.cs          # Main RL combat agent (210-dim obs, 5 action branches)
│   │   │   ├── MeleeAgent.cs           # Simplified melee agent
│   │   │   ├── TrainingManager.cs      # Episode lifecycle bridge (ML-Agents ↔ TopDownEngine)
│   │   │   ├── Txtmap/                 # Text-based map layouts (Easy / Medium / Hard)
│   │   │   ├── results/                # Training outputs & model checkpoints
│   │   │   └── *.yaml                  # Training configs (50+ variants)
│   │   └── PGCMap/
│   │       ├── TilemapGenerator.cs     # Runtime map rendering + enemy spawning
│   │       └── Editor/RoomGeneratorEditor.cs
│   ├── TopDownEngine/                  # Third-party framework (do not modify)
│   │   └── Demos/Koala2D/MyPGC.unity  # Main training scene
│   └── CLAUDE.md                       # Developer reference
├── Build/                              # Standalone Windows executable
├── ProjectSettings/
└── The Knight.sln
```

---

## Agent Design

### CombatAgent (`CombatAgent.cs`)

| Feature | Details |
|---|---|
| Observation space | 210-dim vector: player stats (13), global context (6), enemies (54), bullets (120), wall rays (16) |
| Action branches | 5 discrete: move X, move Y, attack, dash, aim direction (8) |
| Algorithm | PPO with optional ICM curiosity |
| Network | 2-layer, 256-unit MLP (or LSTM variant) |

Reward shaping includes: damage dealt/received, kill bonuses, cooldown patience, dodge rewards, and spatial coverage tracking.

### TilemapGenerator (`TilemapGenerator.cs`)

Renders rooms at runtime from plain-text layout files. Map cell encoding:

```
0 = floor
1 = wall
A–Z = enemy spawn points
```

Supports four map selection modes: `SingleTextAsset`, `FolderByIndex`, `FolderRandom`, `Curriculum`.

---

## Getting Started

### Prerequisites

- Unity 2022.x (via Unity Hub)
- Python 3.8–3.10
- `mlagents` package: `pip install mlagents`

### Play in Editor

1. Open the project in Unity Hub.
2. Open scene: `Assets/TopDownEngine/Demos/Koala2D/MyPGC.unity`
3. Press **Play**.

### Run ML-Agents Training

```bash
# From project root
mlagents-learn Assets/Scripts/Agent/CombatAgentConfig.yaml --run-id=MyRun
```

Then press **Play** in the Unity Editor. Training results and checkpoints are saved to `Assets/Scripts/Agent/results/<run-id>/`.

**Common training configs:**

| Config | Description |
|---|---|
| `CombatAgentConfig.yaml` | Baseline PPO, 300K steps, ICM enabled |
| `curriculum_icm_config.yaml` | Curriculum learning (Easy → Hard) + ICM |
| `curriculum_lstm_config.yaml` | Curriculum + LSTM memory |
| `melee_config.yaml` | Lightweight config for quick iteration |

### Curriculum Difficulty Levels

| Value | Mode |
|---|---|
| 0 | Easy |
| 1 | Medium |
| 2 | Hard (fixed spawn) |
| 3 | Hard (random spawn) |

---

## Editor Tools

- **Room Generator** — `Tools → PGC → Room Generator` — design and export text map layouts
- **Agent Setup** — `Assets/Scripts/Agent/Editor/KoalaAgentSetup.cs` — configure agent parameters

---

## Architecture Notes

The project has two layers:

**TopDownEngine (framework — read-only):** handles movement physics, health, weapons, respawn, input routing, and UI events.

**Custom Scripts (game layer):** `CombatAgent`, `TrainingManager`, and `TilemapGenerator` extend the framework without modifying it (except `LevelManager.cs`, which has documented changes).

Communication between layers uses the TopDownEngine event bus:
`PlayerDeath` → `RespawnStarted` → `RespawnComplete` → `GameOver`

---

## Results

Training outputs are stored in `Assets/Scripts/Agent/results/` and `Assets/results/`. Each run folder contains:
- `run_logs/` — per-agent episode logs
- `<run-id>.onnx` — exported model for inference

---

## License

This project uses [TopDownEngine](https://topdown-engine-docs.moremountains.com/) (commercial license, MoreMountains) and [Unity ML-Agents](https://github.com/Unity-Technologies/ml-agents) (Apache 2.0).

Custom game and agent code (`Assets/Scripts/`) is part of a thesis project and is not licensed for redistribution.
