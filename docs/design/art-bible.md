# Art Bible

The baseline every model, material, texture, effect, and space follows, so the world reads as one place instead of a mix of sources. Proposed 2026-11-27; the faction colors and shape language are proposals until signed off. Code colors come from `Assets/_Project/Scripts/Runtime/Core/Palette.cs` (the `St*` entries); don't use literal colors elsewhere.

## 1. Style

**Grounded, used, believable sci-fi.** Real materials, real wear, real scale: things look manufactured, maintained, and lived with. Quality bar: Arc Raiders and Warhammer 40,000: Space Marine 2 for materials and effects; Alien and The Expanse for how human tech looks. No toy proportions, no clean-room plastic, no generic neon.

- **Human civil** (spaceport, hotel, the company): brutalist-futurist. Bright, dry, matte architectural concrete (Ando-style board-tie panels with form-tie holes), light tech paneling, exposed loft-style services, plants used sparingly in real soil. Clean but worn.
- **Human military and industrial** (armory, maintenance, weapons): gunmetal, anodized aluminum, matte polymer, rubberized grips, stenciled markings, safety orange.
- **Chromas** (synthetic conquerors): no readable seams, ceramic-white and black-glass shells, segmented like insects, precise; their light comes from inside.
- **Brain / biome civilization:** organic, translucent, grown; no straight lines.
- **The lawless planet:** patched, mixed, rusted, salvaged from all of the above.

## 2. Palette

| Role | Color | Use |
|---|---|---|
| Pale concrete | `#E2E0DC` | Station walls |
| Floor concrete | `#C9C5BD` | Station floors |
| Tech panel | `#B7BCC2` | Paneling |
| Panel light | `#F2F6FA` | Interior light, neutral white |
| Desert sand | `#C98A4F` | Kelxor ground |
| Sky haze | `#E7B88E` | Kelxor sky |
| Planet bands | `#D8A47C` / `#9C6A55` | The gas giant |
| **Human energy** | `#6FE3FF` | Human tech light, plasma, screens (white-hot core) |
| **Safety orange** | `#FF6A2B` | Human warnings, markings, underglow |
| **Chromas energy** (proposal) | `#B44CFF` | Chromas light, weapons, eyes (white-violet core) |
| **Forbidden / danger** | `#FF3B3B` | Theft and forbidden highlights, alarms, the chip shock |
| Gunmetal | `#3A3F45` | Weapon metal |
| Polymer | `#2B2D30` | Grips, housings |

Each faction's energy color is used only for that faction's energy, so the player learns to read a glow by its color.

## 3. Materials (PBR)

- **Albedo** stays within 30 to 240 sRGB (no pure black or white). Metals use their real reflectance color with metallic 1; everything else is metallic 0 (in-between only for dirt over metal).
- **Roughness** carries the realism, and no surface is uniform. Painted metal 0.4 to 0.6, bare worn metal 0.25 to 0.45, polymer 0.5 to 0.7, rubber 0.8 to 0.95, concrete 0.7 to 0.9, glass 0.05.
- **Wear** follows use: edge wear on exposed edges, grime in cavities, scratches along the direction of handling, polish where hands go. Never uniform noise.
- **Markings:** stencils, serials, warning labels, and logos as decals or a trim sheet, in a few shared fonts. They give scale and story.

## 4. Detail and texel density

| Asset | Texel density | Edges |
|---|---|---|
| First-person weapons | about 2048 px over 0.6 m | Every hard edge beveled, 0.5 to 2 mm |
| Props | 1024 px/m | Bevels 1 to 5 mm |
| Architecture | 512 px/m, trim sheets and tiling | Bevels 5 to 20 mm |
| Characters | 4096 for the body set, 2048 for gear | Fabric folds baked into normals |

- Silhouette first: it reads at 10 m with the lighting off.
- Large, medium, and small detail in about a 70 / 20 / 10 split; leave rest areas so the detail reads.
- Real scale and plausible function: every vent, screw, and seam could exist.

## 5. Light and emission

- Inside: neutral white office light. Outside: a permanent warm desert sunset under the ringed gas giant. Warmth stays outside, white stays inside.
- Emission intensity (HDR): indicators 2 to 4, screens 1 to 3, energy cores and plasma 10 to 30 so they bloom. Strong emitters also cast real light when it matters (a muzzle flash, an explosion).
- Interiors are matte and dry: concrete smoothness about 0.3, nothing wet-looking. Glass reflects at about 40% (`GlassReflectionProbe`).

## 6. Effects

- **Layered:** a big effect is several layers: a hot core, glow, smoke, sparks and debris, a real light, heat distortion, a decal left behind, camera shake, and sound.
- **Timing:** fast attack, slow decay. Explosions peak in under 0.1 s; their smoke lingers for seconds.
- **Lit smoke:** smoke and dust are lit by the scene (six-way lighting), never flat sprites.
- **Energy** uses the faction color with a white-hot core and a colored falloff; it flickers and arcs, it doesn't just glow.
- **Physical:** sparks bounce, debris has weight, dust drifts. Scale and speed match the world.
- Light shafts and dust stay subtle and never hide the view.

## 7. Spaces

- Every space has a purpose you can read without text: where people work, wait, eat, sleep.
- One modular grid per kit, so pieces snap together (see the map pipeline in [../art/asset-pipeline.md](../art/asset-pipeline.md)).
- Landmarks to navigate by: the window, the departure boards, the info booth.
