# Documentation

The docs live here, outside `Assets/`, so Unity doesn't import them.

| Area | Doc | What it holds |
|---|---|---|
| Design | [design/game-design.md](design/game-design.md) | The spine: vision, pillars, loops, factions, structure, MVP |
| Design | [design/story.md](design/story.md) | Prologue beats, spaceport layout, Act 1 and the chip |
| Design | [design/inventory.md](design/inventory.md) | Items, economy, theft |
| Design | [design/combat-ai.md](design/combat-ai.md) | Combat AI |
| Tech | [tech/pipeline-test-plan.md](tech/pipeline-test-plan.md) | The golden tests in order: carbine, Kelxor exterior, human NPC, Chromas synth, warp-in sequence, Unreal rebuild |
| Tech | [tech/production-plan.md](tech/production-plan.md) | Stages, progression testing, story state, dialogue tooling, engine bake-off |
| Tech | [tech/architecture.md](tech/architecture.md) | Systems, scenes, data flow, packages |
| Tech | [tech/conventions.md](tech/conventions.md) | Folder layout, naming, code style |
| Art | [art/asset-pipeline.md](art/asset-pipeline.md) | How models, rigs, animations, and audio get into the project |
| Art | [art/asset-registry.md](art/asset-registry.md) | Every imported or generated asset, with its source and license |
| Decisions | [decisions/](decisions/) | Architecture Decision Records (ADRs) |
| Log | [devlog.md](devlog.md) | Dated notes on what changed and why |

## Keeping it alive

- Change a system, then update its doc in the same change.
- Make a hard-to-reverse choice, then write an ADR.
- Add an asset, then add a registry row.
