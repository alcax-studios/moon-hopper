# Production Plan

How the game gets made: stages, testing, story state, tooling, and the engine question. Asset specifics are in [../art/asset-pipeline.md](../art/asset-pipeline.md).

## Stages

Find the fun first, then scale.

| Stage | Exit test |
|---|---|
| Concept | Pitch, pillars, platform, audience, scope (see the spine) |
| Prototype | The core loop is fun in greybox |
| Vertical slice | 5 to 15 min at final quality: art, tech, fun |
| Production | Full content, tools, pipelines |
| Alpha | Feature complete |
| Beta | Content complete; bugs, balance, performance |
| Release candidate, launch, live ops | |

Throughout: playtest constantly, short iterations, ruthless scope, data-driven content, modular systems.

## Testing progression without full playthroughs

Progression is stored state (flags, quest stages), so tests set it directly:

- **Debug console** (the F1 admin console): set a stage, set or clear a flag, jump to a scene, add an item.
- **Scenario loading:** named start states ("right before boarding", "leash just released") instead of save files for now.
- **Automated checks:** unit tests on unlock logic; a graph check that finds unreachable dialogue nodes, quest steps nobody can reach, and flags that are set but never read.
- Full human playthroughs late, to catch sequence bugs the shortcuts hide.

## Story state and map changes

- One flag registry (names, types, who sets and reads each) as the source of truth, saved with the game.
- Map changes are state-driven variants of the same scene: objects and layers enabled by flags (the departure board flipping to DELAYED, the intact and breached ship cabin). A separate scene only where memory or a drastic change needs it, behind a fade.
- Cheap changes first: lighting, sky, music, announcements, NPC placement.

## Dialogue tooling

Recommendation: **Ink** for authoring (plain text, diffable, Claude can write and validate it), with a runtime bridge into the existing `DialogueUI`. Every line gets a line ID; voice files are named by it. Tags carry game hooks (`#forbidden`, `#trade`). Move to articy:draft only if the project outgrows Ink.

## Engine choice (decided process, 2026-11-27)

1. Build and prove the whole pipeline in **Unity** first (assets, rigs, animation, VFX, maps, cutscenes), with the plasma weapon as the first golden asset.
2. When it works, build the same test slice in **Unreal 5.8** (installed) from the same shared assets.
3. Score both on the scorecard below, keep one engine, drop the other, and record it as an ADR. The kept engine's tooling, structure, and docs become the single spine for development.

## Bake-off details

The idea: build the same three-scene slice (hotel room; Kelxor spaceport exterior; Chromas boarding) in Unity and Unreal 5.8 from shared inputs, score both, pick one.

Scorecard, fixed before building: visual fidelity (same camera, side by side), how well Claude works in each (first-try success, rework, self-verification), iteration speed, performance on the target PC, open-world readiness, character pipeline friction, cost and licensing.

Biases to correct for: Unity has a large head start here (every system above already runs in Unity); judge the second week, not the first day; give both the same time budget. Unity's C# is all plain text for an agent, while Unreal splits work between C++ (slow compile loops) and binary Blueprints.

