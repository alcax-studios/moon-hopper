# CLAUDE.md

Guidance for AI agents working in this Unity project. Human-facing docs live in `docs/` (start at `docs/README.md`).

## Project

- **Unity** 6000.6.10f1, **URP** (PC + Mobile renderer assets in `Assets/Settings/`)
- **Input:** Input System package (`Assets/InputSystem_Actions.inputactions`)
- **Editor control:** `com.unity.pipeline` is installed; drive the live Editor with the Unity CLI (`unity status`, `unity command ...`). Prefer that over hand-editing `.unity` / `.prefab` / `.asset` YAML.
- **Game:** untitled for now (the old working title MERIDIAN was dropped; names and lore come later), a first-person game set on a station on a desert moon. Keep in-game names generic until the lore is decided. The gameplay is not designed yet. See `docs/design/game-design.md` and `docs/design/art-bible.md`. The old zombie prototype is retired (ADR 0003, `Archive/`).
- **The level is generated:** `Assets/_Project/Scripts/Editor/AtriumBuilder.cs` (menu **Alcax > Build Atrium Level**) rebuilds `Atrium.unity` and bakes its lighting three times (Day, Dusk, Night; `AtriumLighting`, a few minutes; the scene ends on Day). Change the builder, not the scene by hand, or the next rebuild wipes your edits. Exit Play mode before building. Weapon assets come from `WeaponAssets` (Alcax > Rebuild Weapon Assets), items from `ItemAssets` (Alcax > Rebuild Item Assets, after the weapons; the Atrium builder places items, so rebuild items before the level), and the player rig from `PlayerRig`. The combat-AI test map `Playground.unity` comes from `PlaygroundBuilder` (Alcax > Build AI Playground), which also writes the `Data/AI` archetypes, the `Prefabs/AI` operatives, and `Resources/SpawnCatalog` (the unit roster the admin console can spawn; `AdminTools`, the F1 admin console, runs on every map, created at startup). `Data/AI/CombatAIConfig.asset` is a hand-tuning asset that builders never overwrite.

## Rules

- Read the relevant doc in `docs/` before changing a system, and update it in the same change when behavior or structure changes.
- Record any significant, hard-to-reverse choice as an ADR in `docs/decisions/` (copy `0000-template.md`).
- Log every imported or generated art/audio asset in `docs/art/asset-registry.md` with its source and license.
- Licensed or unverified audio (`Assets/_Project/Audio/Ambience/` and `Audio/Overrides/`) is gitignored and stays local; never commit or push it. The game falls back to synthesized sounds without it.
- Follow `docs/tech/conventions.md` for folders, naming, and code style.
- One `MonoBehaviour` per `.cs` file, file named after the class (multiple per file can crash player builds).
- Add a short entry to `docs/devlog.md` at the end of a meaningful work session.

## Environment notes

- The project sits inside OneDrive. OneDrive sync can lock or corrupt files in `Library/` and `Temp/`; exclude them from sync or move the project if odd import errors appear.
- Git repo on `main`, remote `origin` = https://github.com/alcax-studios/moon-hopper (push there). `Screenshots/` holds agent verification captures and is gitignored.
