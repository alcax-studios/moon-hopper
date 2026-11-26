# Asset Registry

Every third-party, generated, or hand-made asset that ships in the project. Log each one when it's added.

| Asset | Path | Type | Source | License | Prompt / notes | Status |
|---|---|---|---|---|---|---|
| All meshes, materials, prefabs, and textures | `Assets/_Project/Art`, `Assets/_Project/Prefabs` | Placeholder 3D | Procedural (`ForestBuilder`, `ForestAssets`, `BuildUtil`) | Project-owned | Low-poly primitives in the art bible palette | placeholder |
| Gradient sky shader | `Assets/_Project/Art/Shaders/GradientSky.shader` | Shader | Hand-written | Project-owned | | kept |
| All sound effects and the ambient bed | Generated at runtime (`AudioSynth.cs`) | Placeholder audio | Procedural synthesis | Project-owned | | placeholder |
| Font | TMP Essentials (LiberationSans SDF) | Font | Unity TextMeshPro | Unity package license | Styled bold, spaced, outlined | placeholder |
| Concrete Slab Wall 02 (scan) | `Assets/_Project/Art/Textures/PolyHaven/concrete_slab_wall_02/` | PBR texture | Poly Haven (polyhaven.com), by Amal Kumar | CC0 | Atrium walls | kept |
| Concrete Panels (scan) | `Assets/_Project/Art/Textures/PolyHaven/concrete_panels/` | PBR texture | Poly Haven (polyhaven.com) | CC0 | Atrium frames, ceiling, stair | kept |
| Atrium procedural textures | `Assets/_Project/Art/Textures/Atrium/` | PBR and foliage | Procedural (`AtriumTextures`) | Project-owned | Floor, metal, sand, bark, foliage, screen | placeholder |
| Alien sky and light shaft shaders | `Assets/_Project/Art/Shaders/` | Shader | Hand-written | Project-owned | | kept |
| Weapon switch sound | `Assets/_Project/Audio/Overrides/Equip/weapon_switch.mp3` | SFX (override for `Equip`) | User-provided download ("half-life-weapon-switch-made-with-Voicemod") | **Unknown. Check before release** (likely a recreation of a Half-Life sound) | Plays on weapon switch and pickup | prototype only |
| Playground grid texture | `Assets/_Project/Art/Textures/T_PG_Grid.png` | Texture | Procedural (`PlaygroundBuilder`) | Project-owned | 4 m tile with 1 m lines, for reading distances | placeholder |
| Operative characters and weapons | `Assets/_Project/Prefabs/AI/` | Placeholder 3D | Procedural primitives (`CharacterRig`, `PlaygroundBuilder`) | Project-owned | Rifle, carbine, DMR, shotgun, pistol | placeholder |
| Rifle, flyby, body-hit, and grunt sounds | Generated at runtime (`AudioSynth.Combat.cs`) | Placeholder audio | Procedural synthesis | Project-owned | Enemy fire, near-miss crack, impacts, pain | placeholder |
| Shout and radio callout voices | Generated at runtime (`AudioSynth.Combat.cs`) | Placeholder audio | Procedural formant synthesis | Project-owned | **Not used since 2026-11-25** (replaced by recorded voice lines) | retired |
| Player rifle view model and pickups | `Assets/_Project/Prefabs/ViewModels/VM_Rifle.prefab`, `Prefabs/Pickups/P_Rifle`, `P_AmmoRifle` | Placeholder 3D | Procedural primitives (`WeaponAssets`) | Project-owned | | placeholder |
| Atrium ambience (airport ambience recording) | `Assets/_Project/Audio/Ambience/atrium_ambience.mp3` | Ambience loop | User download `freesound_community-airport-ambience-58159.mp3` (Pixabay naming, freesound community) | **Verify**: likely the Pixabay Content License (free, no attribution) | Contained by `RoomAmbience` | prototype |
| Dialogue reply sound | `Assets/_Project/Audio/Overrides/DialogueReply/dialogue_reply.mp3` | SFX (override for `DialogueReply`) | User download `dialogue2.mp3` | **Unknown. Check before release** | Plays when an NPC answers a choice | prototype only |
| Enemy voice lines: contact, contact confirm, copy, cover, engaging, comms 1 and 2 | `Assets/_Project/Audio/Overrides/Vo*/` | Voice SFX (overrides for `VoContact`, `VoContactConfirm`, `VoCopy`, `VoCover`, `VoEngaging`, `VoChatter`) | User downloads (`contact.wav`, `contactconfim.wav`, `copy.wav`, `cover.wav`, `engaging.wav`, `coms-voice-line-1/2.mp3`) | **Unknown. Check before release** | Played by `Callouts`. Callouts without a recording are silent (subtitle only) | prototype only |
