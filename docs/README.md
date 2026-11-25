# Documentation

The docs live here, outside `Assets/`, so Unity doesn't import them.

| Area | Doc | What it holds |
|---|---|---|
| Design | [design/game-design.md](design/game-design.md) | Concept, pillars, core loop, mechanics, scope |
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
