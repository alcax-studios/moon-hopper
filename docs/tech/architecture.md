# Technical Architecture

## Engine and packages

| Item | Version / setting |
|---|---|
| Unity | 6000.6.10f1 |
| Render pipeline | URP 17.6.0 (HDR, shadow distance 90 m, 4 cascades, SSAO on the PC renderer) |
| Input | Input System 1.20.1, `Player` map of `Assets/InputSystem_Actions.inputactions` (plus direct `Keyboard`/`Mouse` reads for R, F, 1 to 5, and Esc) |
| Camera | Cinemachine 6.6.0 (CM3 API) |
| Navigation | AI Navigation 2.0.17 (installed; unused since the zombie removal) |
| UI | uGUI and TextMeshPro, built from code |
| Editor automation | `com.unity.pipeline` 1.0.1, driven by the Unity CLI |

## Scenes

| Scene | Purpose |
|---|---|
| `Assets/_Project/Scenes/Atrium.unity` | The station: Atrium C, the south wing, the food court, restrooms, and the airport hotel (build index 0). Generated. |
| `Assets/_Project/Scenes/Playground.unity` | Combat-AI test map (outdoor, realtime light, no bake). Generated. |

Switch maps from the admin console's Maps section (F1, from `GameManager.Maps`) or in the Editor with **Alcax > Maps > Open/Play ...**. A new map must be added to `GameManager.Maps` and the build settings.
| `Assets/Scenes/SampleScene.unity` | Template leftover, unused |

## Generated content (don't hand-edit)

| Builder | Menu | Produces |
|---|---|---|
| `AtriumBuilder` | Alcax > Build Atrium Level (and Bake Atrium Lighting) | The scene, merged static meshes, materials, the NPC and dialogue asset, ships, probes, and the bake |
| `AtriumTextures` | (called by the builder) | Ando concrete, metal, sand, bark, foliage, screen, and hazard textures |
| `WeaponAssets` | Alcax > Rebuild Weapon Assets | Weapon data, view model prefabs, and pickup prefabs |
| `ItemAssets` | Alcax > Rebuild Item Assets (run after the weapon assets) | `Data/Items/IT_*` item definitions, `Prefabs/Items/IM_*` visual-only world models (weapons and ammo reuse their pickup models), and `Resources/ItemDatabase` with the starting outfit |
| `PlaygroundBuilder` | Alcax > Build AI Playground | The playground scene and NavMesh, the grid texture, the AI config and archetype assets (`Data/AI`), and the operative prefabs (`Prefabs/AI`) |
| `CharacterRig` | (called by builders) | The shared primitive character rig. Combat mode adds elbows, gear, and hit-zone colliders on the Actor layer |
| `PlayerRig` | (called by builders) | The player (controller, weapons, interactor, flashlight), the camera with Cinemachine, and the title camera |
| `SoundOverrides` | Alcax > Refresh Sound Overrides | `Audio/Resources/SoundBank.asset` from `Audio/Overrides/<Sfx>/` |

Static geometry is accumulated per material in `MeshAccum` (world-scaled UVs, lightmap UVs), then merged. There's one collision mesh.

## Runtime systems

- **Core:** `GameManager` (Title, Playing, Paused, Ended, Dialogue, and Menu states, `InputLive` grace), `FeelManager` (impulse shake and hit-stop), `AdminSettings` (cheats), `GameSettings` (sensitivity), and `Palette` (the single color source).
- **Player:** `PlayerController` (with crouch), `WeaponHolder` (melee swings with two variants, hitscan, pistol magazine reloads, shotgun pump and shell reloads, ejected debris), `PlayerInteractor` (pickups, NPCs, interactables; tap E takes a world item, hold E drags), `PlayerGrabber` (carry, drag, throw physics props), `DialogueCamera` (turns to and zooms on the person you talk to), `HeldItem` (non-weapon items in hand: hold to use aid and food, click to wear), and `Flashlight`.
- **Items (see `docs/design/inventory.md`):** `ItemDefinition`, `ItemDatabase`, `ItemStack`, `PlayerInventory` (grid, hotbar, body slots, armor), `WorldItem` (an item in the world), `Grabbable` (draggable), `QuestLog`, `Codex`, `StoryDirector` (feeds both from game events), `LootContainer` and `LootTables` (containers and people's pockets), `Trader`, and `Theft` (witness check; fines go through `StationSecurity`).
- **Ambient:** `ScannerDrone` (free flight in a `FlightVolume`, scan fans), `InfoBot` (hover robot with an Npc dialogue), `SteamVent`, and `AmbientArt` (runtime materials, the fan mesh, particle jets).
- **Time of day:** `TimeOfDay` (the static switcher), `TimeOfDaySet` (the scene's sun, volume, probes, and shafts), and `TimeOfDayProfile` (per-preset values and baked data, written by `AtriumLighting`).
- **Menu UI:** `GameMenu` (Tab: Inventory, Quests, Codex, Status), `InventoryPanel`, `TransferWindow` (loot and trade), `ItemSlotView` (through `ISlotPanel`), `QuestsPanel`, `CodexPanel`, `StatusPanel`, and `PreviewStudio` (renders item icons and the dressed character on layer 30, far below the map).
- **Combat:** `Health` (with `Revive`), `HitZone`, `WeaponData`, and `TargetPlate`.
- **Interaction:** `Interactable` (anything with an [E] prompt; picked by `PlayerInteractor`), `Elevator` and `ElevatorButton`.
- **Station life:** `Civilian` (hurtable, panicking people on the shared rig), `AmbientNpc` (lobby life, panic and flee), `PickupRack` (restocking pickups).
- **AI (combat):** `Operative`, `Perception`, `OperativeMind`, `CoverFinder`, `ConcealedRoute`, `OperativeWeapon`, `OperativeAim`, `OperativeBody`, `Squad`, `Callouts`, `CalloutSubtitles`, `Faction`, `CombatStyle`, `Hearing`, `PlayerTarget`, `PatrolRoute`, `StationSecurity` (guards and crime in peaceful areas), the space model (`NavGraph` with per-node `SpaceInfo`, the authored `CombatRoom` and `CombatDoor` markers, and `RoomGraph`), plus the dev tools `PlaygroundDirector` and `AIDebugOverlay`. See `docs/design/combat-ai.md` section 13.
- **Dialogue:** `DialogueTree` (data), `Npc` (procedural idle, head-look, and talking), and `DialogueUI`.
- **Loot:** `Pickup` (static placements; the random loot director was removed).
- **FX:** `FXManager` (pooled particles and tracers), `ShipTraffic`, `AutoDoor`, `Debris`, and `TitleOrbit`.
- **Audio:** `AudioSynth` (procedural clips), `SoundManager` (pooled voices, a gain layer, and the SoundBank overrides), and `SoundBank`.
- **UI:** `HUD`, `ShellUI` (title, pause, settings, and death), and `UIFactory`.

## Lighting (Atrium)

Mixed sun (baked indirect), baked rectangle area lights (coffers, hanging panels, and the wing), baked emissives, box-projected reflection probes (room, glass at 40%, hall, and range), and light probe groups. The far clip plane is 4 km for the desert view.
