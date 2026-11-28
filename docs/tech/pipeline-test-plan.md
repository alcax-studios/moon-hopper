# Pipeline Test Plan (golden tests)

The order of work for proving the whole pipeline at the target quality (Arc Raiders / Space Marine 2 level, as far as the tools allow) before the game grows. Each test follows the plasma carbine's method: a written brief, real sources, a reproducible build script, the art bible, the validator, in game, and a "done when" list. Everything is built in Unity first, then rebuilt in Unreal for the engine decision ([production-plan.md](production-plan.md)).

| # | Test | Proves |
|---|---|---|
| 1 | Plasma carbine ([brief](../art/golden-plasma-weapon.md)) | Hard-surface modeling, texturing, baking, import, part animation, gameplay, VFX |
| 2a | Hotel room 1202 at hero quality | The limit of interior fidelity: hero assets, materials, lighting, cloth, water, breakables, decals, all the room mechanics |
| 2c | Maintenance bay interior | Modular kits, trim sheets, decals, interior lighting, set dressing |
| 2 | Kelxor exterior slice | Terrain, materials at scale, rocks, procedural plants, wind, atmosphere, map tools, performance |
| 2b | Reactive world sandbox | Physics reactions, destruction, fluids, blood and goop, cloth, ropes, procedural textures, fire and weather |
| 3 | A human NPC | Character modeling, skin, eyes, hair, clothing, rig generation, deformation, animation sets, in-game behavior |
| 4 | A Chromas synth | Machine rigging, procedural locomotion (IK legs, hover), alien materials and energy |
| 5 | The warp-in sequence | Cutscenes and scripted sequences, large VFX, camera direction |
| 6 | Unreal rebuild of 1 to 5 | The engine decision (scorecard, ADR) |

## 2. Kelxor exterior slice

**Brief:** about 250 x 250 m outside the spaceport: wind-carved dunes, rock outcrops and boulder fields, a landing-pad edge and service road, a half-buried wreck, desert scrub and alien succulents, blowing sand, heat haze, the gas giant on the horizon.

| Part | Approach |
|---|---|
| Terrain shape | Gaea (free Community edition) for erosion and dunes, exported as 16-bit heightmap and masks (flow, slope, deposits). Fallback: our own Python erosion and dune generator |
| Terrain materials | Unity Terrain with height-based blending; 4 to 6 layers from scanned CC0 sets (Poly Haven sand, rippled sand, gravel, rock, compacted ground); macro variation and distance blending so nothing visibly tiles |
| Rocks | Scanned rocks (Poly Haven CC0, Megascans on Fab), cleaned in Blender, LODs plus far impostors, pushed through the validator (a new Environment class) |
| Scatter | A rules-based scatter tool: slope, height, and mask limits, clustering, sinking into sand, scale and rotation ranges, keep-out zones along paths; placed by hand where it matters |
| Plants | Generated in Blender (Geometry Nodes or Python): branching structure, leaves and cards with generated or scanned textures, LODs. Vertex colors carry wind weights (trunk bend, branch sway, leaf flutter) |
| Wind | A wind shader on all foliage driven by one global wind (direction, strength, gust noise sweeping across the world), plus the player and NPCs pushing plants aside as they pass; tumbleweed and loose debris as physics |
| Atmosphere | Blowing sand and dust devils (the first VFX Graph effects), heat haze near the ground, height fog, the sun and sky tuned for the desert; Adaptive Probe Volumes for outdoor lighting |
| Map tools | Splines for roads, paths, and cables; ProBuilder greybox; the scatter tool; the level metrics table |

**Done when:** you can walk the whole slice; the scatter rebuilds from rules; plants move in the wind and react to you; nothing tiles at a glance; it holds the frame-rate target on this PC (measured).

## 2a. Hotel room 1202 at hero quality

**Brief:** the opening scene of the game (the first base and the tutorial) rebuilt at the highest quality we can reach: every surface, object, light, and interaction at close range. Pieces: the bed (cloth sheets you can disturb), desk, kitchen (fridge, cabinets, sink with running water), bathroom (mirror, vanity, shower glass), the safe, a TV with the news broadcast, curtains moving with the air, the window onto the spaceport and Kelxor. Breakables (glasses, a lamp), a coffee spill, decals of use. All existing mechanics kept (containers, safe and key, sleep, sinks, theft).

**Done when:** it holds up at arm's length everywhere you look, every object reacts to touch and weapons, and the mechanics all still work.

## 2c. Maintenance bay interior

**Brief:** the spaceport's maintenance bay (from the story layout): an industrial hangar interior overlooking the landing pads, cluttered and worn, with a hangar door open onto Kelxor. The same kit later builds the ship's service areas.

| Part | Approach |
|---|---|
| Modular kit | Walls, floors, ceilings, doors, frames, pillars, pipes, cable trays, vents, stairs, railings on one snap grid (2 m, 0.5 m steps), hard-surface like the carbine; trim sheets and tiling materials so pieces share textures |
| Decals | Wear, grime, oil stains, leaks, hazard stripes, stencils and signage (one shared font set), floor markings |
| Lighting | Practical lights (strip lights, work lamps, warning beacons), sun shafts through the hangar door onto dust, emissive screens; baked plus probes, with a few real-time lights |
| Dressing | Tool carts, crates, a docked vehicle or shuttle part, hoses and cables, workstations; the reactive props from 2b |
| Layout | Paper layout, greybox to the level metrics, then the art pass (the scene becomes hand-authored, ADR 0004) |

**Done when:** it reads as a real working space with no visible kit repetition, the light through the hangar door holds up, and it runs at the frame-rate target.

## 2b. Reactive world sandbox

**Brief:** one test room (the AI Playground or a hangar corner) where everything reacts: shoot it, blow it up, walk through it, spill it. Then the systems spread to every map.

| System | Approach |
|---|---|
| Physics reactions | Every loose prop is a rigidbody with the right mass and physics material (metal, plastic, wood, glass); bullets, plasma, and explosions push, spin, and knock things over; impacts make sound and VFX per material. Hanging things swing (lamps, signs, cables) |
| Destruction | Pre-fractured meshes (Blender cell fracture, chunks with interior material) swapped in on break; crates, panels, consoles, glass. Damage states for big objects (dented, holed, scorched) |
| Baked simulations | Big one-off events (a wall collapsing, the hull breach, a fuel spill) simulated in Blender (or Houdini later) and played back in engine as vertex animation textures (VAT), the industry standard for cinematic destruction and fluids at game cost |
| Fluids: water | A water surface shader: refraction, depth color, foam at edges, ripples where things touch it, flow; splashes (VFX Graph with collision), wet surfaces nearby |
| Fluids: blood, goop, coolant | Particles with collision that leave decals where they land (wet, glossy, then drying); drips and runs down walls; pools that spread; viscous goop (Chromas fluid, plant sap) with stretchy strands; spills from containers you shoot |
| Cloth and ropes | Cloth on flags, tarps, clothing parts (Unity Cloth or a verlet solver); ropes and cables that sag, swing, and can be cut |
| Fire and smoke | Fire that spreads on burnable material, burns out, and leaves scorch; smoke columns from simulated flipbooks (six-way lit) |
| Weather | Sandstorm on Kelxor: wind strength rising, visibility falling, particles everywhere, plants thrashing, dust collecting on surfaces |
| Procedural textures | Our Blender material toolkit (wear, dirt, dust, scratches from geometry) extended to tileable materials and trim sheets; Material Maker (free) or Substance Designer (paid) for hero materials; decals for wear, stains, and markings |
| Surface response | A physical material table (sound, impact VFX, decal, footstep, bullet penetration) shared by every system |

**Done when:** in the sandbox, every object reacts believably to bullets, plasma, explosions, and the player; things break into chunks; water splashes and ripples; blood and goop splatter, drip, and pool; cloth and cables move; a fire spreads and burns out; a sandstorm rolls through.

## 3. A human NPC

**Brief:** one adult human (a spaceport security officer), at hero quality up close, with a set of animations that changes with what they do.

| Part | Approach |
|---|---|
| Body and face | Free first: MPFB2 (MakeHuman for Blender, CC0 output) with a game rig. Upgrade if it falls short: Character Creator 5 ($299, the game-industry standard for this, Unity auto-setup) |
| Clothing | Modeled and simulated in Blender (cloth sim for folds, baked to normals), uniform pieces as separate meshes. Marvelous Designer if needed |
| Skin, eyes, hair | URP has no built-in skin shading, so custom shaders: skin with subsurface-style light wrap, detail normals, and two specular lobes; eyes with refraction and a wet highlight; hair cards with anisotropic highlights. This is where Unreal's MetaHuman stack is strongest; it's a key bake-off comparison |
| Rig generator | One script: fit Rigify to the mesh, generate, export only deform bones named like the UE5 mannequin (works as a Unity Humanoid and in Unreal), auto weights plus cleanup, corrective shape keys for shoulders, elbows, knees, and hips, a range-of-motion test animation to check deformation |
| Face | ARKit-style 52 shape keys for expressions and lip sync (later with audio) |
| Animation sources | No motion capture for now. Free libraries we may use and edit (Mixamo; check each source's license before shipping: some research datasets are non-commercial), used as-is or as references, plus animation authored by script (Blender keyframes and procedural motion: breathing, look-at, foot placement, hit reactions). Cascadeur (free tier) for physically plausible polish |
| Animation set | Locomotion (idle, walk, run, sprint, strafes, starts, stops, turns), combat (aim, fire, reload, cover, grenade), interactions (sit, talk gestures, pick up, open doors, lean on a counter), reactions (hits by direction, stagger, death blending into the ragdoll) |
| In game | Animator with layers (full-body locomotion, upper-body weapon, additive breathing and hit reactions), root motion, Animation Rigging for foot IK, look-at, and hands on the weapon; the existing combat AI drives it; motion matching evaluated later (Unity has none built in) |

**Done when:** they hold up in a close dialogue shot (skin, eyes, hair); joints deform cleanly in the range-of-motion test; they walk, run, turn, sit, talk, take cover, and fight with smooth transitions; feet plant on stairs and slopes; death blends into the ragdoll.

## 4. A Chromas synth

**Brief:** a tripod synth (and a hover drone variant): ceramic-white and black-glass shell, segmented, light from within (art bible), shoulder-mounted weapon.

- Hard-surface like the carbine, but organic-precise forms (subdivision on the shells, no visible seams), alien energy color.
- Rig generated from the part hierarchy (one rigid bone per part).
- Procedural locomotion: IK legs that plan footholds, body sway and balance, gait changes with speed; hover drones with thruster physics.
- Shoulder weapon with charge-up and its own VFX language (Chromas energy, distinct from human plasma).

**Done when:** it walks over uneven ground without foot sliding, turns and stops convincingly, aims and fires, and reads as alien at a glance.

## 5. The warp-in sequence

**Brief:** the prologue's attack as seen from the ship's window: the alarm, the mothership warping in, small ships launching and closing in, the hull breach.

- Timeline + Cinemachine, with signals into gameplay (alarm, lights out, emergency lights).
- The player keeps head movement in the locked seat.
- Large VFX: the warp distortion and flash (VFX Graph + a screen-space distortion), engine trails, the breach explosion with debris and decompression.
- Ship models through the same hard-surface pipeline.

**Done when:** the 60 to 90 second sequence plays in game, directed, with the player looking around freely, at frame rate.

## 6. Unreal rebuild

Rebuild tests 1 to 5 in Unreal 5.8 from the same source assets (FBX, textures, animations, Blender scripts), using Nanite, Lumen, MetaHuman, Niagara, PCG, and Sequencer where they apply. Score both on the scorecard in [production-plan.md](production-plan.md), keep one engine, and write the ADR.

## Cross-cutting

- **Procedural where it makes sense, hand-made where that is better:** use rules and scripts for things that repeat, vary, or need rebuilding (scatter, plant variations, wear, weather); model, place, and animate by hand for hero assets, key spaces, and performances. Pick per case.

- **Quality check per test:** side-by-side screenshots against reference images, scored on the visual scorecard.
- **Performance budget:** frame time measured on this PC for each test; LODs, impostors, and instancing from the start.
- **Art bible:** grows per biome and faction as each test defines them.
- **Registry and licenses:** every source logged; scanned assets are CC0 or recorded.
- **Storage:** Git LFS quota tracked as art grows.

## Decisions needed (from the user)

- Install Gaea Community (free) for terrain.
- A Fab account (free) for Megascans, later; Poly Haven covers surfaces and rocks for now.
- Character Creator 5 ($299) now, or try MPFB2 first (recommended: MPFB2 first).
