# CLAUDE.md

Guidance for AI agents working in this Unity project. Human-facing docs live in `docs/` (start at `docs/README.md`).

## Project

- **Unity** 6000.6.10f1, **URP** (PC + Mobile renderer assets in `Assets/Settings/`)
- **Input:** Input System package (`Assets/InputSystem_Actions.inputactions`)
- **Editor control:** `com.unity.pipeline` is installed; drive the live Editor with the Unity CLI (`unity status`, `unity command ...`). Prefer that over hand-editing `.unity` / `.prefab` / `.asset` YAML.
- **Game:** **Moon Hopper** (working title), a first-person open-world survival RPG with Fallout-style combat. The hub is Kelxor, a desert moon spaceport. The spine is `docs/design/game-design.md` (story in `docs/design/story.md`); design changes land there first. The old zombie prototype is retired (ADR 0003, `Archive/`).
- **The level is generated:** `Assets/_Project/Scripts/Editor/AtriumBuilder.cs` (menu **Alcax > Build Atrium Level**) rebuilds `Atrium.unity` and bakes its lighting once (Day; Dusk and Night reuse it with a different sun, sky, and exposure). **Alcax > Bake Atrium Lighting (All Times)** bakes all three (a few minutes) when night needs its own lightmaps. Change the builder, not the scene by hand, or the next rebuild wipes your edits. Static geometry (`Add`/`AddRot`) must be added before `FlushMeshes`; later steps (props, furniture, people) use GameObjects, and the builder logs an error if geometry arrives late. Exit Play mode before building. Weapon assets come from `WeaponAssets` (Alcax > Rebuild Weapon Assets), items from `ItemAssets` (Alcax > Rebuild Item Assets, after the weapons; the Atrium builder places items, so rebuild items before the level), and the player rig from `PlayerRig`. The combat-AI test map `Playground.unity` comes from `PlaygroundBuilder` (Alcax > Build AI Playground), which also writes the `Data/AI` archetypes, the `Prefabs/AI` operatives, and `Resources/SpawnCatalog` (the unit roster the admin console can spawn; `AdminTools`, the F1 admin console, runs on every map, created at startup). `Data/AI/CombatAIConfig.asset` is a hand-tuning asset that builders never overwrite. The Kelxor exterior test map `Kelxor.unity` comes from `KelxorBuilder` (Alcax > Build Kelxor Exterior): terrain from `TerrainSynth` (dunes, mesas, erosion), Poly Haven ground layers painted by rules, and the scanned rocks in `Art/Models/Environment` placed by rules.

## Rules

- Read the relevant doc in `docs/` before changing a system, and update it in the same change when behavior or structure changes.
- Record any significant, hard-to-reverse choice as an ADR in `docs/decisions/` (copy `0000-template.md`).
- Imported art goes in `Assets/_Project/Art/Models/<Class>/<Asset>/` through the Blender export add-on, inside the budgets in `docs/art/asset-pipeline.md`; fix every `[ArtCheck]` warning. Editable source goes in `SourceArt/`.
- Log every imported or generated art/audio asset in `docs/art/asset-registry.md` with its source and license.
- Licensed or unverified audio (`Assets/_Project/Audio/Ambience/` and `Audio/Overrides/`) is gitignored and stays local; never commit or push it. The game falls back to synthesized sounds without it.
- Follow `docs/tech/conventions.md` for folders, naming, and code style.
- One `MonoBehaviour` per `.cs` file, file named after the class (multiple per file can crash player builds).
- Add a short entry to `docs/devlog.md` at the end of a meaningful work session.

## Environment notes

- The project lives at `C:/Dev/moon-hopper` (moved out of OneDrive on 2026-11-27; keep it out of synced folders, since sync fights Unity and git over large binary files).
- Git repo on `main`, remote `origin` = https://github.com/alcax-studios/moon-hopper (push there). `Screenshots/` holds agent verification captures and is gitignored.
