# Asset Pipeline

How art and audio get from an idea into the game.

## Workflow

1. **Describe** the asset: what it is, its class, its style, and where it's used.
2. **Source** it (see Sources and the direction below). Save the prompt if it's AI-generated.
3. **Clean it up in Blender**: retopology if needed, UVs, LODs, materials, origin at the base, transforms applied.
4. **Export** with the Moon Hopper Export add-on (below). It refuses an asset that breaks the rules.
5. **Unity imports it** with the presets for its class, and the validator logs any budget problem as an `[ArtCheck]` warning.
6. **Review** it in the Atrium or a test scene. Keep it, fix it, or regenerate it.
7. **Make a prefab** and add the logic.
8. **Log** it in [asset-registry.md](asset-registry.md).

Until the art is ready, build mechanics with placeholder shapes (primitives or ProBuilder).

## Spec

### Folders

```
Assets/_Project/Art/Models/<Class>/<Asset>/   what Unity imports: <Asset>.fbx, its textures, its materials
SourceArt/<Class>/<Asset>/                     the editable source (.blend, high-poly, bakes); in git through LFS
SourceArt/_local/                              scratch and licensed source that never leaves this PC (gitignored)
Tools/blender/                                 Blender add-ons and scripts for the pipeline
```

Classes: `Props`, `Weapons`, `Architecture`, `Vehicles`, `Characters`, `Creatures`, `Foliage`. The class folder decides the budget and the import presets. The procedural Atrium art (`Art/Meshes`, `Art/Textures/Atrium`) is outside these rules.

### Naming

| Thing | Pattern | Example |
|---|---|---|
| Static mesh | `SM_<Name>` | `SM_SupplyCrate` |
| Skinned mesh | `SK_<Name>` | `SK_ChromasTripod` |
| LODs | `<Mesh>_LOD0`, `_LOD1`, `_LOD2` | Unity builds the LOD Group from the names |
| Collision | `UCX_<Mesh>_00` | simple convex hulls (optional) |
| Textures | `T_<Asset>_<Map>` | `_BaseColor`, `_Normal`, `_MetallicSmoothness`, `_Occlusion`, `_Emission` |
| Materials | `M_<Name>` | `M_SupplyCrate` |
| Animations | `A_<Rig>_<Action>` | `A_Human_Run` |

Maps follow URP Lit: metallic in R and smoothness in A of `_MetallicSmoothness`; data maps are imported linear.

### Units and axes

1 unit = 1 m. Z-up in Blender, Y-up in Unity. Origin at the base (the lowest point at z = 0), scale and rotation applied. Exported FBX: apply unit scale with FBX units, forward -Z, up Y, bake space transform (off for rigs), face smoothing, tangent space, no leaf bones. Tested on 2026-11-27: a 0.6 m crate arrives as 0.6 m, upright, scale 1.

### Budgets

| Class | LOD0 triangles | LODs above | Max texture | Size check | Rig |
|---|---|---|---|---|---|
| Props | 10k | 2k | 2048 | 2 cm to 4 m | none |
| Weapons | 30k | 8k | 4096 | 5 cm to 2.5 m | none |
| Architecture | 8k per module | 3k | 2048 (trim sheets) | 5 cm to 30 m | none, lightmap UVs |
| Vehicles | 60k | 10k | 4096 | 0.5 to 80 m | none |
| Characters | 60k | 15k | 4096 | 0.8 to 2.6 m | Humanoid |
| Creatures | 60k | 15k | 4096 | 0.2 to 12 m | Generic |
| Foliage | 6k | 1.5k | 2048 | 5 cm to 15 m | none |

LODs: LOD1 at about 50% of LOD0, LOD2 at about 25%. Textures are powers of two. The numbers live in `Scripts/Editor/AssetBudgets.cs`; change them there and here together.

### Tools

- **Moon Hopper Export** (`Tools/blender/moon_hopper_export.py`, installed in Blender): sidebar tab **Moon Hopper**. Pick the class, type the asset name, select the objects, then **Check and Export**. It checks the prefix, applied transforms, UVs, and the base at z = 0, writes the FBX into `Art/Models/<Class>/<Asset>/`, and copies the saved `.blend` to `SourceArt/`. Scriptable: `mh.export_asset("Props", "SM_Crate")`.
- **Import presets** (`ArtImportValidator`): on the first import under `Art/Models`, scale 1 from file units, no cameras or lights, MikkTSpace tangents, no mesh compression, the rig type for the class, lightmap UVs for architecture; textures get the class's max size, normal maps marked, data maps linear. Later Inspector tweaks are kept.
- **Validator**: on every import, and for everything with **Alcax > Validate Art Assets**. Warns on triangle budget, missing LODs, size outside the class range (wrong scale), origin off the base, missing UVs, naming, and texture size.
- **Hard-surface and texturing toolkits** (`Tools/blender/hardsurface.py`, `texturing.py`): profiles with machined chamfers, primitives, boolean cutters, the mid-poly finish (angle bevel with hardened normals plus weighted normals); layered wear materials (edge wear, dust in cavities, roughness breakup, scratches); game meshes joined per moving part and triangulated; one UV atlas; a Cycles bake to the Unity map set. Asset scripts live in `Tools/blender/assets/`.
- **Build Materials From Textures** (`ArtMaterials`, menu Alcax > Art): makes `M_<Name>` (URP Lit) from an asset's `Textures/T_<Name>_*` set and assigns it to the model.
- **blender-mcp**: lets Claude run Blender and look at the result. Its generators (Rodin, Hunyuan3D, Tripo, Meshy) and libraries (Poly Haven, Sketchfab, Poly Pizza) are off until switched on in its panel.
- **Git LFS**: models, textures, audio, video, fonts, and `.blend` files go through LFS (`.gitattributes`). Watch the GitHub LFS storage quota as art grows.

## Sources

| Source | Good for | Notes |
|---|---|---|
| Primitives / ProBuilder | Blockouts, level geometry | Built inside Unity |
| Blender 5.3 | Modeling, cleanup, UVs, LODs, rigs | Installed; driven through blender-mcp |
| AI generators (Meshy, Tripo, Rodin, Hunyuan3D) | Props, weapons, organic shapes | Free tiers publish models as CC BY; paid tiers are private. Save the prompt. Local Hunyuan3D or TRELLIS on the RTX 5080 is the free option, planned last |
| Poly Haven | Textures, HDRIs, some models | CC0 |
| Gaea 2 (installed, Community edition) | Terrain heightmaps and masks | **Community: learning and evaluation only, 1024 max, no commercial use.** Shipping terrain needs Gaea Indie ($99, perpetual) |
| Material Maker 1.7 (installed) | Procedural tileable materials | Free, open source (MIT); outputs are ours |
| MPFB 2.0.18 (installed in Blender) | Human base meshes with a game rig | Add-on is GPL; the characters it makes and the MakeHuman asset packs are CC0 |
| Fab / Megascans | Rocks, surfaces, kits | Record the license |
| Unity AI generators (in the Editor) | Textures, materials | Uses Unity Points |
| Asset Store | Anything | Record the license |

## Rigging and animation

**Skeleton generation.** Rigs are generated, not hand-built, so every character of a kind shares one skeleton and animations retarget across them.

- **Humans:** Blender's built-in Rigify generates the rig from a metarig fitted to the mesh (Claude fits it by script). Only the deform bones are exported, named like the UE5 mannequin (`pelvis`, `spine_01`, `thigh_l`, ...) so the same skeleton works in Unity (Humanoid avatar) and Unreal. Mixamo or AccuRIG are the quick fallback. Auto-Rig Pro (paid, about $40) is the upgrade if game export gets painful.
- **Machines** (Chromas synths, drones, weapons with moving parts): a script turns the part hierarchy into bones, one rigid bone per part at its pivot, no weight painting. Imported Generic.
- **In engine:** Unity Animation Rigging for procedural motion: foot IK on uneven ground, aim and look constraints, tripod leg placement, weapon hand IK.

**Animation sources:** keyframes in Blender (by script for mechanical parts, by hand for hero moments), Mixamo's library for human basics, video mocap (Move.ai, Rokoko Vision) for performances, and Cascadeur for physically plausible keyframing. Clips use `A_<Rig>_<Action>`. Animator Controllers live next to their model.

## Map design

1. **Paper layout:** a top-down diagram from the story beats: rooms, the critical path, optional loops, sight lines, landmarks. Approved before building.
2. **Greybox** in Unity (builder code or ProBuilder), built to the level metrics: corridor widths, door and ceiling heights, cover height, jump reach, all taken from the player controller and kept in one table.
3. **Playtest and iterate** while it's cheap.
4. **Art pass:** modular kits on a fixed snap grid (walls, floors, doors, windows, trims), then set dressing, decals for wear and story, lighting. From here the scene is hand-authored and the builder stops writing it (ADR 0004).
5. **Exteriors:** Unity Terrain from Gaea heightmaps and splatmaps (the free Community edition first); rocks, debris, and wrecks scattered by script with rules (slope, height, density), then placed by hand where it matters.
6. **Performance:** LODs, occlusion culling, baked lighting for static spaces, light probes for moving things.

## VFX

The industry standard at the Arc Raiders and Space Marine 2 level is simulated fluids baked into textures, played back by a GPU particle system with lit smoke:

| Layer | Tool | Notes |
|---|---|---|
| Fluid simulation (smoke, fire, explosions, dust) | **EmberGen** (real-time, used across the industry) or **Houdini** (the AAA standard, Indie license). Free start: Blender's smoke and fire simulation, rendered by script | Baked to flipbooks: color, six-way lighting maps, and motion vectors for smooth frame blending |
| Particle system | **Unity VFX Graph** (GPU) | Six-way lit smoke output, millions of particles, strips for trails and beams. Unreal's equivalent is Niagara |
| Small and physics effects | Shuriken (Unity's particle system) | Sparks that bounce, debris, small impacts; easy to drive from code |
| Shader effects | **Shader Graph** | Energy surfaces, scanner beams, shields, heat haze, holograms, dissolve |
| Electric arcs and beams | VFX Graph strips or line renderers with noise, plus an energy shader | Branching lightning by script, with a short-lived light |
| Leftovers | URP Decals | Scorch marks, bullet holes, plasma burns |
| Screen | Post-processing volumes | Flash, chromatic aberration, the chip shock, blackout |

Every effect follows the art bible's effect rules (layers, timing, lit smoke, faction energy colors). VFX Graph assets are harder for Claude to edit as text than code, so the plan is to build them from Unity's VFX Graph templates through its scripting API, verify that on the plasma weapon, and fall back to hand-tuning in the graph where needed.

## Cutscenes

- **Unity Timeline + Cinemachine:** the standard in Unity. Timelines hold animation, camera cuts, audio, activation, and signals that call gameplay (start the alarm, lock the camera to the window). Claude builds them through the Timeline scripting API; they stay editable by hand.
- **Workflow:** previz first (greybox actors, camera moves, timing, scratch audio), then swap in real characters, animation, and VFX.
- **In-engine and interactive where possible:** the prologue keeps head movement and small actions during the attack, so most "cutscenes" are scripted sequences with a locked or guided camera, not movies.
- Unreal's equivalent is Sequencer, compared in the bake-off.

## Audio: replacing synthesized sounds with real recordings

Every sound starts out synthesized in code (`AudioSynth`). To replace one:

1. Drop one or more audio files (.wav, .ogg, or .mp3) into `Assets/_Project/Audio/Overrides/<SoundName>/`. The folder names match the `Sfx` enum: `Groan`, `ZombieHurt`, `ZombieDie`, `ZombieAttack`, `Pistol`, `Shotgun`, `Swing`, `BluntHit`, `BladeHit`, `Punch`, `Footstep`, `AmbientBed`, and so on.
2. Run **Alcax > Refresh Sound Overrides** (the level build also runs it).
3. With several files in a folder, one is picked at random each time it plays. Per-sound volume is set in `Audio/Resources/SoundBank.asset`.

Free sources: Freesound.org (filter by the CC0 license), the Sonniss GDC game audio bundles (royalty-free), and Kenney.nl (CC0). Log every file in the [asset registry](asset-registry.md) with its license.

## Direction (2026-11-27): Claude orchestrates, it doesn't model

Meshes built from code and primitives hit a realism ceiling. From the vertical slice on, the pipeline is: concept image, then a 3D source (AI generation, a scan library, a character creator), then **Blender as the cleanup hub** (retopology, UVs, LODs, PBR bake, rig, scale check, FBX), then the engine (import presets, prefabs, placement, lighting). Claude drives each step through MCP servers or scripts.

| Need | Source |
|---|---|
| Props, weapons, consumables | Meshy (cleaner hard-surface), Tripo (detailed hero assets); compare Hunyuan3D and Rodin |
| Cleanup | Blender + blender-mcp (also reaches Poly Haven, Sketchfab, viewport screenshots) |
| Rocks and surfaces | Quixel Megascans (free on Fab) |
| Architecture | Realistic modular sci-fi kits plus AI set pieces, assembled on a snap grid |
| Terrain | Gaea or World Creator heightmaps and splatmaps |
| Humans | Character Creator 5 or MetaHuman |
| Rigging and animation | Mixamo, AccuRIG, Meshy/Tripo auto-rig; one Humanoid avatar; video mocap or Cascadeur |
| Hero texturing | Substance 3D Painter, trim sheets, wear decals |

Moon Hopper mapping: Kelxor from Gaea terrain and Megascans desert assets; hotel and ship interiors from a modular kit with decals; Chromas synths as rigid hard-surface rigs (parts parented to bones) with procedural IK for tripod and hover movement; the brain faction from AI organic shapes cleaned in Blender; humans from CC5 or MetaHuman.

**Golden asset test next:** one weapon, one wall module, one human NPC, one Chromas synth through the whole pipeline, against the budgets above.

### Audio

Source (library, AI generation, recording), then edit (Reaper with ReaScript batch jobs, or Audacity), then middleware (FMOD Studio, free Indie license), then the engine.

- **Weapons:** recorded libraries, layered. AI SFX are still weak on punchy transients.
- **Ambience:** AI generation plus libraries (Sonniss GDC bundles; Freesound, checking each license).
- **Chromas:** AI generation plus designed layers. Their voice is raw synthetic speech, with the chip's flat translation layered on top once it's active.
- **UI, foley, footsteps:** libraries.
- **Voice:** scratch TTS for every line first; NPCs from ElevenLabs Voice Design (no real person); main cast from actors or speech-to-speech. Whether the protagonist is voiced is open.
- **Standards:** 48 kHz WAV source; mono for positional sounds, stereo for ambience, UI, and music; one loudness target for dialogue and one for SFX; an audio import validator like the mesh one.
- **Release:** AI-generated assets need Steam's AI disclosure; cloning a real voice needs written consent. Licensed audio stays local and gitignored (see CLAUDE.md).
