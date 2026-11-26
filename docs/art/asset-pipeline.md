# Asset Pipeline

How art and audio get from an idea into the game.

## Workflow

1. **Describe** the asset: what it is, its style, and where it's used.
2. **Source** it from one of the options below. Write down the prompt if it's AI-generated.
3. **Import** it into `Assets/_Project/Art/Models/<AssetName>/`.
4. **Fix up** the import: scale (1 unit = 1 m), forward axis, materials converted to URP Lit, and colliders.
5. **Review** it in a test scene with a screenshot. Keep it or regenerate it.
6. **Rig and animate** it if needed (see below).
7. **Make a prefab** and add the logic.
8. **Log** it in [asset-registry.md](asset-registry.md).

Until the art is ready, build mechanics with placeholder shapes (primitives or ProBuilder).

## Sources

| Source | Good for | Notes |
|---|---|---|
| Primitives / ProBuilder | Blockouts, level geometry, simple props | Built inside Unity |
| Blender (scripted) | Custom low-poly models, fixing up imports, simple rigs | Free. Not installed yet |
| AI generators (Meshy, Tripo, Rodin, ...) | Characters, detailed props | Export as GLB or FBX. Check each tool's license and save the prompt |
| Unity AI generators (in the Editor) | Textures, materials, sprites, and more | Uses Unity Points. Check the Editor's AI menu for what's available |
| Asset Store | Anything | Record the license |

## Rigging and animation

- **Humanoids:** auto-rig with Mixamo, then download animations as FBX for Unity. Import with **Animation Type: Humanoid**.
- **Non-humanoids:** use AI-generator rigs where offered, or a scripted Blender rig.
- Animator Controllers go next to their model. Clips use the `A_` prefix.

## Import settings

- **Models:** Scale Factor 1. Don't import cameras or lights. Extract materials into `Materials/`.
- **Textures:** Mark normal maps as Normal Map. Set Max Size to the smallest size that looks right.
- **Audio:** see below.

## Audio: replacing synthesized sounds with real recordings

Every sound starts out synthesized in code (`AudioSynth`). To replace one:

1. Drop one or more audio files (.wav, .ogg, or .mp3) into `Assets/_Project/Audio/Overrides/<SoundName>/`. The folder names match the `Sfx` enum: `Groan`, `ZombieHurt`, `ZombieDie`, `ZombieAttack`, `Pistol`, `Shotgun`, `Swing`, `BluntHit`, `BladeHit`, `Punch`, `Footstep`, `AmbientBed`, and so on.
2. Run **Alcax > Refresh Sound Overrides** (the level build also runs it).
3. With several files in a folder, one is picked at random each time it plays. Per-sound volume is set in `Audio/Resources/SoundBank.asset`.

Free sources: Freesound.org (filter by the CC0 license), the Sonniss GDC game audio bundles (royalty-free), and Kenney.nl (CC0). Log every file in the [asset registry](asset-registry.md) with its license.
