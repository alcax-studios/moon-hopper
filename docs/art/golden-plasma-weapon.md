# Golden asset 1: the plasma carbine

The first asset through the whole pipeline at the target quality (model, parts, rig, animation, VFX, in game). It proves the pipeline and sets the bar for every weapon after it. Follows the [art bible](../design/art-bible.md).

## Status (2026-11-27)

- [x] Model: hand-modeled mid-poly in Blender, 66 parts, 25.4k triangles (LOD0), LOD1 and LOD2 generated.
- [x] Materials: layered wear materials (edge wear, Kelxor dust, roughness breakup, scratches), stencils and serials as decal geometry.
- [x] Bake: one 4K set (BaseColor, Normal, MetallicSmoothness, Occlusion, Emission); `M_PlasmaCarbine` built by **Alcax > Art > Build Materials From Textures**.
- [x] In Unity: correct scale and facing (+Z), grip at the origin, moving parts named (`Mag` = the energy cell, `VentFront`, `VentRear`, `Trigger`, `Coil1-3`), a `Muzzle` point.
- [x] In hand: view model (`VM_PlasmaCarbine`), plasma bolts (`PlasmaBolt`), heat and overheat (`PlasmaRig`, HUD heat bar), cell reload (the cell is `Mag`). Sold by the armory trader with energy charges.
- [x] Part animation (code): coils spin with heat, vents snap open and steam on overheat, trigger pull, arcs between the coils.
- [x] Plasma fluid: glass strips along the barrel and the cell window show animated fluid (`Alcax/Plasma Fluid`): lava-lamp blobs, rising bubbles, an undercurrent; faster and hotter with heat, boiling when overheated.
- [x] VFX: muzzle charge, flash and light; bolt core, glow, trail, shed sparks, light; impact flash, splash, bouncing sparks, lit smoke (simulated flipbook), embers, light, scorch decal that cools.
- Playtest note (2026-11-27): the fluid works but reads poorly as a thin tube far down the barrel, and its glow washed out the detail (to tone down). For a future weapon, put fluid windows where the player sees them most (receiver, cell area), larger, or make it the feature of a different gun.
- [ ] Sounds (the user's), first-person arms (with the human rig).

Rebuild: in Blender run `plasma_carbine.py`, then `plasma_carbine_tex.py` with STAGE = "materials", "bake", "export".

## Brief

A compact human plasma carbine from his company's R&D line: a premium, engineered product he'd demo to clients, not a scrapyard gun. Human military style: gunmetal and anodized aluminum receiver, matte black polymer furniture, rubberized grip, safety-orange markings and a serial plate. Human energy color (`#6FE3FF`, white-hot core) shows only through the coil windows, the cell window, and the emitter.

| Part | Moves | Notes |
|---|---|---|
| Receiver | | Main body; serial plate, stencils |
| Barrel shroud with cooling fins | | Around the emitter; the fins glow when hot |
| Emitter | | Muzzle; where the bolt forms |
| Charge coils (3 rings) | spin up, glow | Visible through windows in the shroud |
| Energy cell | ejects and inserts | The "magazine"; glass window shows charge |
| Heat vents (2 flaps) | open on overheat | Steam out |
| Trigger | pulls | |
| Optic | | Small holographic sight, cyan reticle |
| Stock | | Folding, fixed for now |

## Behavior (for gameplay)

- Fires plasma bolts (visible projectiles, fast but not instant), each building heat.
- Overheats when the heat bar fills: the vents open, steam vents, and it can't fire for a moment. Reloading swaps the energy cell.

## Animations

`A_PlasmaCarbine_Idle`, `_Fire`, `_ChargeSpin`, `_Overheat` (vents open, close), `_Reload` (cell out, cell in), `_Equip`. Part animations come first; first-person arms come with the human rig.

## VFX

| Effect | Layers |
|---|---|
| Muzzle | Bolt forming in the emitter, a flash, a short real light, heat shimmer |
| Bolt | A white core, a cyan glow, a trail, sparks shed along the way |
| Impact | A plasma splash, bouncing sparks, a small lit smoke puff, a scorch decal, a light |
| Charge | Electric arcs between the coils, building with heat |
| Overheat | Steam (lit smoke) from the vents, glowing fins cooling down |

## Done when

- It passes the import validator (Weapons: 30k LOD0 triangles, LODs, 4K textures max).
- It reads as the same world as the art bible, side by side with the station.
- You can pick it up at the armory, fire, overheat, reload, and see every effect in game.
