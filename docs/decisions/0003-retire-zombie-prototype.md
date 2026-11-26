# 0003: Retire the zombie-forest prototype and focus on Meridian Station

- **Date:** 2026-11-25
- **Status:** accepted (supersedes the game concept in 0002; 0002's "generated levels" approach still stands)

## Context

The first prototype (GRAVEPINE: low-poly forest, zombie waves, and loot) proved out the player, weapons, feel, and tooling. The user then built Meridian Station's atrium in a realistic-leaning style and decided that's the game's direction.

## Decision

Remove all zombie and forest content: the zombie AI, waves, and spawner, the forest scene and builder, its low-poly environment assets and materials, the loot spawner, the zombie sounds, and the zombie admin cheats. Keep and generalize everything reusable:
- `PlayerRig` (player and title cameras, extracted from the forest builder)
- `WeaponAssets` (weapon data, view models, and pickups)
- weapons, pickups, health, targets, dialogue, UI, audio, and FX

Everything removed is in `Archive/gravepine_zombie_forest_2026-11-25.zip` (with .meta files, so it can be restored by unzipping into the project root).

## Consequences

- The project has one scene (`Atrium.unity`), and gameplay is still to be designed.
- `Sfx` enum values shifted. The SoundBank is rebuilt by Alcax > Refresh Sound Overrides.
