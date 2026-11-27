# Devlog

Newest entries first.

## 2026-11-26: Fridge door, cabinet units, cursor lock

- The fridge door swings out into the room (the angle sign was reversed).
- The room 1202 kitchen run is 5 lower and 5 upper cabinet units, with doors, gaps, and handles. Each unit is its own container.
- The cursor re-locks every frame in play whenever something freed it (closing the inventory, loot, or trade, or the Editor dropping the lock), and stays free in menus.

## 2026-11-26: Fixed: the stamina bar never moved

- The HUD looked up PlayerVitals before the player added it, so the stamina and need bars never updated. It looks the component up until found now. The stamina bar eases to its value, a trailing chip shows what you just spent, it brightens while recovering, pulses red when exhausted, and dims when full. Tested: 100 to 31 after 3 s of sprinting (the chip held at full), then back to 76.

## 2026-11-26: Survival: stamina, needs, effects, sleep, and furniture

- Stamina (sprint and jump, recovers after a pause, an empty bar locks out sprinting until 30%), and food, water, and energy that drain slowly. Low needs slow recovery; empty food or water costs health. HUD bars sit under health.
- Effects: Charge (energy drink) gives Energized (no drain for 12 s), then a 3 s Crash. Coffee gives Caffeinated; sleep gives Well rested. New drinks: water bottle and Charge. Sinks are free water.
- The hotel bed: sleep until one of the next two times of day. The room 1202 fridge has a real door with physical items inside; the cabinets, island, nightstands, vanity, and armory medical cabinet are containers.
- Fixed: `GetComponent<T>() is { }` lets Unity's fake-null through in the editor. It broke focus points on bodies (an exception every frame), and NPC rounds hitting other NPCs were treated as stopped by armored glass, so NPC-vs-NPC damage failed in editor play. TryGetComponent now.
- Tested: sprint to empty (locked out until 30%), Charge (no drain, then a Crash), the sink (water 78 to 100), the fridge door blocking its contents until opened, and sleeping from Day to Night (energy 100, Well rested, max stamina 120).

## 2026-11-26: Theft fixes and a plainer UI

- Fixed: opening a container with E closed it again in the same frame (the loot window also closes on E), so nothing could be taken. The opening press is ignored now. Tested with real key events.
- Containers aren't stealable themselves; their contents are (red STEAL tags, Steal All). Added station property: stealable with no owner.
- Removed UI commentary: owner names, "taking is stealing", who is watching, window hint lines, captions, placeholder texts, and named-witness toasts.

## 2026-11-26: Credits are an item

- Money is a stackable Credits item (no stack limit). The balance is what you carry. Containers and bodies show their cash as a stack. Tested: start 150, spend all (the stack is gone), sell 3 coffee (a new stack of 3), +40 (43, same stack), a register holding 201 as a stack.

## 2026-11-26: Item menus everywhere

- The item menu and Details popup are shared (`ItemMenus`). Right-click works in the loot and trade windows too. Details shows the value and the stolen tag.
- Fixed: double-click in the inventory never fired, because the first click opened the menu under the cursor. A single left click now opens the menu after 0.28 s unless a second click comes.

## 2026-11-26: Fixed: circle-strafing made you unhittable

- The AI aim now feeds your angular motion forward and learns steady motion within about a second. Sprinting in circles around a guard went from 1 to 8 hits taken in 14 s to 42 to 82, about the same as standing still. Details: combat-ai.md section 32.

## 2026-11-26: Loot, money, traders, theft, and time of day

- **Loot:** containers (supply crates, lockers, registers, a fridge, a lost and found) and every person carry items from loot tables. Bodies are searchable (tap E; holding E still drags), and dropped guns are takable items. The two-sided loot window has double-click, drag, and Take All (R).
- **Money:** credits and item values. **Traders:** the quartermaster and the three food stalls buy and sell ("Let's trade."), with markups, buy rates, and limited budgets.
- **Theft:** owned items and containers. Unseen, an item is just marked stolen. Seen, a guard comes to fine you (value + 50: pay, or refuse and fight). Selling stolen goods to a trader is an instant crime. The Atrium has owned and free test items around the info desk.
- **Time of day:** Day, Dusk, and Night in the admin console's World section. The Atrium bakes once per preset and swaps the lightmaps, probes, and reflections at runtime. The lower-light preset is named Dusk.
- Dialogue choices can trigger actions (`@trade`, `@fine_pay`, `@fine_refuse`). Guards gained a Confront duty state.
- Tested: looting a body (credits, ammo, the gun on the floor), buying and selling with Noor (her budget moved), a seen theft (the fine dialogue opened in 3 s, and paying cleared it), fencing (wanted), and a screenshot of each time of day.

## 2026-11-26: Inventory, items, game menu, and dragging

- Items are data now (`ItemDefinition`, 22 items across weapon, ammo, aid, food, wearable, and misc; built by Alcax > Rebuild Item Assets). Weapons and ammo live in the inventory, and the 8-slot hotbar (1 to 8, wheel) decides what is in your hands. Reloads draw rounds from the inventory, and every gun keeps its own magazine.
- **Tab** opens the game menu: Inventory (6 x 5 backpack, hotbar, five body slots, a live character preview in what you wear, armor totals, drag and drop, an item menu with Use, Wear, Split, Drop, and Details), Quests, Codex, and Status. The world keeps running while it is open.
- Wearables give armor (head armor against headshots only). Aid and food heal: hold the attack button with them in hand, or Use them from the menu.
- First quest (*Orientation*) and codex entries that unlock from items, people, and factions; the HUD gets a hotbar strip, a use bar, and a quest tracker. Notices queue instead of overwriting each other.
- **Dragging:** hold E on a prop or body to carry or drag it, release to drop, click to throw. Tap E takes world items. The Atrium has loose items (mugs, coffee, rations, bandages, armor, a data pad, a keycard) and props (crates, folding chairs, bins).
- Medkit pickups now go into the inventory instead of healing on the spot. Nothing is auto-collected anymore (ammo too): everything takes E.
- Movable things show a crosshair cue (corner brackets) instead of a text prompt; grabbing is silent.
- Fixed: with domain reload off at Play mode, static events and the quest log carried over between sessions (a MissingReferenceException from the menu). They reset at startup now.
- Details: docs/design/inventory.md.

## 2026-11-26: Admin console

- F1 (or F2) opens one admin console on every map. It frees the cursor and pauses mouse look and attacks; you can still walk.
- **Spawn:** one unit per click, where the crosshair points, always unaware. A ray goes from the screen center, through glass, and snaps to the nearest walkable point. Aim at a wall and the unit lands on the floor in front of it. Repeat clicks scatter up to 1.2 m, never through a wall. With no walkable ground in view, nothing spawns. Shift+click makes it know where you are. A Faction button cycles Random and each faction. Units of the same faction join one squad, up to 6. The old multi-unit presets (squads, patrols, high ground, skirmish) are gone from the menu.
- **AI:** alert everyone hostile to you, clear spawned units, freeze, AI overlay mode. **Player:** god mode, no-clip, infinite ammo, one-hit kills, all weapons, full heal, +50 ammo. **Maps:** load or reload any map. **View:** dev text size.
- Sections are collapsible headers (Spawn starts open). A closed header shows a short summary, such as the faction, the active cheats or the current map. The scroll height is measured from what is drawn, which fixes the last map being cut off.
- **Invisible** (Player section): no AI can see or target you, and their rounds pass through you. Units already hunting you search your last known spot, then lose interest.
- The Esc menu is now Resume, Restart, Settings (volume, mouse) and Quit. The Admin tab and the map buttons moved to the console.
- Glass marks are removed when a pane shatters (both faces are one object now).

## 2026-11-26: Glass

- Glass has its own physics layer (9, Glass). Everyone sees through it: vision and cover rays skip it, so glass is never cover. Bodies still cannot walk through it, and the NavMesh treats it as a wall.
- **Armored** glass (the atrium window wall, the mezzanine railings, the room 1202 view) stops bullets. Each hit leaves a crack, and hits close together grow it into a spiderweb. The AI stops shooting through armored glass it has seen bounce a round, or that shows 2 or more cracks, and repositions instead (`HoldFire = "armored glass"`).
- **Breakable** glass (the 3 range viewing panes) lets bullets through at 75% damage and leaves holes. After about 70 damage it shatters into shards, and the opening becomes walkable.
- `GlassPane` (Combat) handles marks, shatter and sounds. The player and NPC hitscan loops pass through breakable panes. Each map area picks a kind with `AtriumBuilder.ArmoredGlass` / `BreakableGlass`.
- Details: combat-ai.md section 31.

## 2026-11-26: NPC versus NPC

- The AI now fights any hostile character, not just the player: a common target type, factions that declare who they fight, grudges (shoot me and you're hostile), contact lists, and a focus target re-picked every 0.4 s with hysteresis.
- Station: insurgents spawned in the atrium are engaged by security right away, the quartermaster arms up, and civilians flee them as they do the player. Your gunfire while security fights hostiles isn't a crime; hitting a guard or civilian still is, and then guards weigh you against the insurgents.
- Admin presets: insurgent assault squad, a 4 v 4 skirmish, and security guards. F3 shows each unit's target.
- Details and test numbers: combat-ai.md section 30.

## 2026-11-26: Global admin tools, straight-to-play start, no more MERIDIAN

- **Admin tools are global** (`AdminTools`, created at startup and kept across maps): F1 help, F2 spawn menu, F3 AI overlay, F4 or double-tap Space for no-clip, F9 clear, F10 freeze, F11 god mode, and [ ] for dev text size.
  - The spawn data lives in `Resources/SpawnCatalog` (roster, factions, config, presets, plus a new "Security guards" preset), written by the Playground builder.
  - Spawns land on random reachable NavGraph points at least 18 m from the player, anywhere on any map. A spawned patrol gets a generated route.
  - `PlaygroundDirector` is now just the Playground's loadout. The AI experiments spawn through `AdminTools`.
- **No title screen or camera glide:** every map starts in play. The camera blend is a cut, and the death screen's Title button is gone.
- **Unarmed:** no hands on screen; the fists appear only while punching.
- **Elevator:** the car has its own bi-parting doors (front at the lobby, back upstairs). They're shut while riding and open with the landing door.
- **The working title MERIDIAN is dropped everywhere in-game:** the atrium lettering now reads ATRIUM C / HYDROPONICS DECK, the hotel's subline SKYPORT, and the map is "Atrium". Dialogue, the HUD objective, menu names and comments are generic. CLAUDE.md, the design docs and the architecture doc are updated; dated devlog history is left as written.
- Tested: the Atrium starts in Playing with hands hidden; admin tools spawned a squad and a patrol in the atrium; car doors are shut while riding and open on arrival; an AI experiment ran in the Playground through the new spawner. No errors.

## 2026-11-26: Airport hotel

- **Layout,** east of the atrium:
  - a doorway in the atrium's east wall at mezzanine level leads down a short hall to the hotel lobby (glass double doors);
  - the lobby has a reception desk with a backlit AIRPORT HOTEL wall, a lounge with two facing sofas, planters, a luggage cart, a receptionist, a concierge and two guests who live in the lounge;
  - an elevator joins the lobby to the 12 m floor;
  - up there, a small landing has the doors of 1201 and 1203 (locked, red lights) and the door of 1202.
- **Room 1202:** one open studio under a three-coffer ceiling with cyan cove lines. Kitchen (a white run, dark tops, a fridge, an island with three stools under pendants), living (an L sofa turned to the view, a dark rug, a low table, a cyan holo table) and sleeping (a low floating bed against an upholstered wall, nightstands with lamps). A floor-to-ceiling glass wall faces north over the landing pads. Only the bathroom (walk-in shower, vanity with mirror, toilet) has a door.
- **New systems:**
  - `Interactable`: anything that shows "[E] label" and acts when used (PlayerInteractor picks the best in reach).
  - `Elevator` and `ElevatorButton`: a call button on each landing, UP and DOWN inside the car. Landing doors unlock only while the car is parked at that floor; the rider is carried with the car; there's a click and an arrival chime.
- Tested: routes from the atrium to the lobby, landing to room, room to kitchen and bathroom (the bathroom door widened to 1.5 m for the NavMesh). The elevator ride carried the player from 6 m to 12 m and swapped the door locks. The room and bath doors open; 1201 and 1203 stay shut. No errors.

## 2026-11-26: Civilians borrow the combat AI; holstered guards

- **Fleeing reuses the combat AI's spatial tools.** Candidate spots are NavGraph nodes within 42 m (with the hiding prior's enclosure score), and cover is rated with CoverFinder.Classify against the shooter's eye line.
  - Far from him counts most, and cover or out-of-sight adds more.
  - Routes passing within 5 m of him are rejected.
  - Spots other fleeing people just picked are penalized, so crowds spread out.
- **Bravery is a decision.** A brave civilian (bravery over 0.6) in a panic weighs an opening twice a second: him reloading (+0.5), his back turned (+0.4), being cornered (+0.25), having been hurt (+0.2), minus distance and wounds. Above 0.95 within 10 m, they attack. There's an 8 s cooldown after a fight.
- **Holstered guards:** calm, neutral guards carry the pistol at the hip (long guns slung on the back) with arms hanging. They draw in 0.35 s, with the equip sound, as soon as they're alerted or hostile. Playground enemies are always hostile, so their guns are always out.
- Test (16 residents, shot fired in the atrium): mean distance gained 14.3 m in 10 s (10.7 m before tuning); 14 of 16 ended out of sight or in full cover. A brave resident attacked a player 5 m away with his back turned. All 3 guards were holstered on patrol and drew within 1.5 s of the shot. No errors.

## 2026-11-26: Doors, ragdolls, brave civilians, restrooms

- **Doors** open for anyone (player, residents, staff, guards) standing right at the doorway, within 1.5 m on either side and only across the door width, so passing near several doors no longer opens them all. They open in 0.2 s, and the leaves are left out of the NavMesh bake. Before, every route into the hall, range, armory and food court was blocked for NPCs; all now complete.
- **NavMesh climb** (project agent setting) went from 0.75 to 0.45 m: people walked over the 0.7 m planters at the stair. 0.35 broke the top of the grand stair (voxel rounding), so it's 0.45. All routes are verified, including the mezzanine.
- **Ragdolls:** on death every limb gets a rigidbody and a character joint. The hit pushes the struck limb, and the body rests with heavier damping but stays physical; the player now pushes loose rigidbodies. Operatives drop their gun when they die.
- **Civilians:**
  - Fleeing commits to a destination and re-plans only when it's compromised (he's within 6 m, or the spot is in his view and nearer to him), at most every 3 s. Huddlers move only if spotted within 15 m, after 4 s.
  - About one in five is brave: hit up close, they rush the player and punch (7–12 damage), then break off after 4 punches, 2 more hits taken, or when badly hurt.
  - Cornered people (shooter close and in sight) crouch with their hands up.
  - Ines can now flee her booth (hands up if cornered), and walks back afterwards.
  - The quartermaster arms himself when security is alerted, and puts the gun away on stand-down.
- **Food court:** 8 regulars who start seated, prefer occupied tables, and mostly stay there. The entrance planters are gone, and so are the range-counter supplies.
- **Restrooms** off the food court's west wall: white tiling, a vanity with three basins and a mirror, three stalls, a hand dryer, its own ambience zone and probe.
- Tested in play: restroom route complete; a brave resident charged and punched, then ran; fleeing under a moving shooter reversed direction in 10 of 130 running samples; corpses lie flat with 10 loose limbs. No errors. Punch damage wasn't checked (god mode was on).

## 2026-11-26: Atrium life: food court, armory, civilians, security

- **Food court** behind the west blast door (the sealed door is now a working one): three stalls (Noodle Bar, Grill 03, Orbit Coffee) with vendors, menu boards and canopies, 8 tables with 32 seats that residents use, planters, and warm lighting. It has its own ambience zone and probes.
- **Armory** west of the hall, its door facing the range door:
  - a lit weapon wall with labeled, restocking slots (sidearm, shotgun, rifle, machete, bat);
  - ammo shelving, a medical cabinet with medkits, lockers, a cleaning bench;
  - a quartermaster who explains the rules.
- **The range** keeps ammo and medkits (now restocking); the weapons moved to the armory. New `PickupRack` puts a fresh pickup back 12–25 s after one is taken.
- **People are shootable:** residents, Ines, vendors and the quartermaster can be hurt and killed. They panic at gunfire (run and hide, then huddle) and stop talking while scared.
- **Station security:** 3 pistol guards patrol, ignore you until you fire outside the range or hurt someone, then fight with the full combat AI. They radio each other, stand down after 40 s without contact, and replace losses. A "WANTED" HUD shows the state.
- Fixes along the way: NPC foot rays ignore people's own hit colliders, and the guard data lives with the other AI data (`ARC_Guard`, `FAC_Security`, `OP_Guard`; spawnable in the Playground too).

## 2026-11-26: Peek spam fixes

- The cause was a hit-stun lock: every hit reset the enemy's burst and paused its fire, so landing rapid hits kept the target from ever shooting back. Now only the first hit of a volley breaks the burst; follow-ups only flinch, less each time. The enemy being hit fires back during the peek 10% to 75%.
- Counter-peek: they hold the exact spot you ducked from, react about 3x faster when you reappear where they're aiming, and after repeated peeks some put short bursts on the spot. Crate peeks answered 88% to 97%, first shot back 0.4 to 0.2 s.
- Peek prefire: the squad learns your hide time at a peek spot, and one holder fires on the spot just before you're due back. That's 5x more rounds on the spot while you're hidden, but no measurable gain at the moment you step out in the test (where peeks are teleports). Switch: usePeekPrefire.
- Dev controls: double-tap Space toggles no-clip (like F4); the F2 spawn menu stays open after a pick.
- Details: combat-ai.md section 28.

## 2026-11-26: Steady moves (consistency pass)

- Measured the reported oddities first. At range, in his line of sight, enemies spent about 45% of the time sprinting gun-low facing away (no shooting). Moves handed off to moves in other directions (back and forth). Unseen, the body faced the threat or gaze behind the route (walking backwards in halls).
- One switch, `useSteadyMoves`, adds six simple rules: fight while moving when he's in view (sprint only unseen); no backward cover runs; no open-ground advance under his eyes (except loose, aggressive units); settle 2.5 s after arriving; room clearing over advance and flank indoors; face the walk when unseen.
- Results: gun lowered in his view 40% to 2%, first shot back median 2.5 to 0.5 s, firing in sight 18.5% to 22%, outdoor reversals 10.5% to 6.5%, indoor reversals 16% to 9.7%, walking backwards indoors 11% to 1%. Details: combat-ai.md section 27.
- The test runner no longer polls for minutes after a test ends (it read the CLI's hint line as the answer). A full A/B now takes about 75 s.

## 2026-11-26: Room clearing (CQB) and aim from the space

- New ClearRoom action. With the player believed in another room and unseen, they stack beside a door, pie the corner, enter (first man across, second hooks), dominate (other doors and visible corners; a corridor's two ends), call "Clear!", and wipe the room from the squad's map. Stairs: to the foot, climb with the muzzle on the landing. Doors are picked by walking distance, never through the room itself, and a third man or a flanker takes another door. Search inside buildings goes room by room.
- Aim from the space: unseen aim follows the walking route to where he probably is and holds where it leaves view (a corner, the hall's end, a doorway, the stairs), not a bearing through walls or ceilings. Attention favors that direction, and cues behind a wall right in front fade.
- CQB A/B (8 trial pairs, hidden player, half upstairs): time to find him 7.4 to 5.8 s, operatives reaching his room 53% to 66%. Out-of-cover wall aim 12.3% vs 11.2% (10.5% vs 5.9% the run before; noisy). Aim into nearby ceilings or floors is about 0 either way. The full procedure rarely completes in the test because they usually spot him through a doorway first. Details: combat-ai.md section 26.

## 2026-11-26: Smaller Playground, CQB house, rooms and doorways (layer 2)

- The Playground is about 16% smaller (104 m across instead of 124 m); the hills, mesa, spawns and player start moved in. The maze, wall line, yard, pillars and tower are unchanged.
- The house's ground floor is now four rooms off a central corridor, with a SW–SE connecting door (a loop), three exterior doors, and windows on every side. The upper floor and balcony are unchanged. Interior doors are 1.4 m (narrower ones closed under the NavMesh agent radius).
- Layer 2 of space awareness: the builder emits CombatRoom and CombatDoor markers (six rooms, ten doors), and RoomGraph answers which room a point is in and which door leads toward another room (by walking distance). First use: with the player in another room and unseen, enemies hold the doorway toward him. Checked in play: they moved into the corridor and held the NE-room door. No errors.

## 2026-11-26: Space awareness (layer 1)

- Every NavGraph node now has a spatial fingerprint: indoors (a roof overhead), the space's size and longest sightline (16 eye-height rays), corridor width, and openness. Labels: corridor, small room, room, tight outside, open. It takes 119 ms for the Playground's 7,061 nodes.
- Standoff, fighting range, cover search radius, and the flank ring now shrink to fit the fight's space (the smaller of mine and his). A 5 m room means keeping about 2.5 m apart and fighting at 3 to 8 m instead of 7 m and 10 to 25 m. Open ground is unchanged. The F3 overlay shows the space and effective distances.
- Indoor A/B (8 trial pairs): mean distance 7.2 to 6.7 m, face to face unchanged at 3%, time outside the building 40% to 36% (noisy). Outside they mostly shoot in, go around, or use doorframes. Layer 2 (rooms and doorways) and layer 3 (CQB behaviors) are the next steps. Details: combat-ai.md section 24.

## 2026-11-26: Hit response (push through or duck)

- A hit while fighting or peeking is now a choice: push through for 2.5 to 4 s, or duck. Close range, nerve, confidence, winning the exchange, and having already ducked recently favor pushing; fear, big hits, and low health favor ducking. Two ducks in 20 s mark the spot as bad, so they relocate or flank. This breaks the peek, get hit, duck, peek loop. Hit A/B (8 trial pairs, tuned): duck on a hit 100% to 32% at 9 m and 53% at 22 m, duck loops 59% to 34% of operatives, close-range return fire 68 to 94 shots per op-minute. Details: combat-ai.md section 23.
- Process slip: the patch and recompile were applied while the Editor was in Play mode (the gate checked that the status call worked, not that it said stopped). The running session broke with NullReferenceExceptions; the code itself compiles cleanly. The gate now compares the value.

## 2026-11-26: Contact drill and fire fixes

- Instrumented why enemies with eyes on the player held fire. The causes: the action never asked to fire (37%: shotgun rushers holding for melee, silent spotted flanks, dashes in plain view) and a squadmate in the line (21%). Fixed: rushers fire, spotted flankers shoot, long dashes in view plant and shoot, and blocked shooters sidestep. The actively firing share roughly doubled.
- Contact drill: being shot at now counts as the player knowing where they are (no more stalking or crouch-walking in the open after being shot at). Right after contact or under fire in the open: return fire, or a short reaction burst toward the shots, then dash for cover. No searching, stalking, patching up, or ducking under fire.
- Contact A/B (8 trial pairs): first shot back 3.7 s to 0.8 s (median 2.4 to 0.4). In cover at 6 s did not improve (34% vs 25%). Details: combat-ai.md section 22.

## 2026-11-25: Fighting cover, movement doctrine, stamina and fatigue

- Fixed enemies hiding behind cover they can't shoot from (peek-proof cover is now only for retreating or when dry), and moves being dropped mid-run (cover, advance, shoot in the open, every 1 to 3 s). Moves commit until they arrive, firefights get 3 s before second thoughts, and scared units blind-fire instead of freezing.
- Cover discipline per faction and role: Special Ops and veterans shoot from cover and bound cover to cover under overwatch; insurgents walk and shoot but take cover when shot at, reloading, or hurt.
- Each move is a dash (gun low, sprint), stop and shoot (run, plant, burst, run on), or fire on the move. Stamina limits sprinting (recovers when walking or standing, fastest standing), and fatigue from repeated sprints lowers and slows recovery, scaled by role fitness.
- Probe, Insurgents vs Special Ops: shooting while moving 13% vs 5%, PeekShoot 2% vs 27%, fighting in the open 20% vs 9%. No errors. Details: combat-ai.md section 21.

## 2026-11-25: Enemies stay engaged when you hide

- Contact freshness now comes from the best information (own sight, squad reports, heard shots), not just the unit's own eyes. In cover with stale info they make re-acquire peeks (head-only) instead of waiting, and holding cover with nothing new has a patience-scaled limit (3 to 10 s) before peeking, flanking or searching win. Switch: useInfoEngagement. Details: combat-ai.md section 20.
- Hide A/B (8 trial pairs): passive holding 12.9% to 4.8% of the time, re-sighted 7.6 s to 5.8 s (mean). The scenario is short and they usually find you within about 6 s either way, so long hides (where the old dead zone bit hardest) are the real test; that is a playtest call.

## 2026-11-25: Cover repertoire and the ammo budget

- Coming out of cover is now one of four moves, scored per peek: info peek (head only, no shot), blind fire (gun only, very inaccurate suppression), lean (head, shoulder, and gun, aimed), and full step-out. Peek spots shift along low cover after a peek that drew fire. Details: combat-ai.md section 19.
- Finite spare ammo per archetype: conservation below 30% (2-round bursts, tighter fire tolerance, no suppression unless covering someone), and a dry state (a "Cover! I'm out!" callout, then hiding, falling back, spotting peeks, and melee only up close). The F3 overlay shows the reserve and DRY.
- Cover A/B, two runs of 6 trial pairs (only the two new switches toggle). Visible to the player: 43% to 40% in the first run, 39% to 30% in the second. Hits taken: 39 to 37 and 38 to 28 per op-minute. Belief freshness in cover was unchanged (about 2 s either way). Trials are 24 s, so peeks are a small share (about 15 per arm) and the peek-only numbers (n = 3 or 4) are too sparse to read. The direction is right; the size is modest and noisy. No one ran dry in 24 s, so the dry state has only been compiled, not seen in play.

## 2026-11-25: Distance falloff and wall occlusion for world sounds

- World sounds use inverse-distance falloff per kind instead of linear over 45 m: voices min 2.5 m and max 60, gunshots 6 and 300, footsteps 1.5 and 25. A voice at 12 m is now 0.21 of full volume (linear gave about 0.8).
- Occlusion: a two-ray world check at play time. When blocked, voices drop to 0.35 with a 900 Hz low-pass, gunshots to 0.55 with 1.6 kHz, footsteps to 0.3 with 700 Hz. The switch is `SoundManager.SpatialRealism`.
- Voice-line overrides are forced to mono for 3D placement.

## 2026-11-25: Recorded enemy voice lines

- Seven recordings drive the callouts: contact, contact confirm (a squadmate seeing you right after a contact call), copy (new: the flanker acknowledges flank orders and one squadmate acknowledges push and fall back), cover (reloading and cover me), engaging (new: opening fire on a fresh sighting, at most every 10 s), and two comms chatter lines (moving, flanking, position updates, lost visual, suppressing). Each speaker plays them at a slight pitch offset, and the subtitles match the recording.
- The synthesized babble voice is removed: callouts without a recording are silent but still subtitled and still heard by the AI.

## 2026-11-25: Gunfire voice management, trimmed custom sounds

- The crackly "pixelated" gunfire was voice overload. Measured with the player firing the rifle into a firefight: 27 gunshot voices at once, summed to about 23x full scale, hitting the 32 real-voice ceiling. Now there is a per-sound concurrency cap (the oldest copy fades out over 60 ms), every new gunshot ducks ringing tails to 50%, player automatic fire ducks its own previous shot to 30%, and the real-voice limit is 64. The same test now peaks at 9 gunshot voices and about 2.8x (a conservative estimate). The switch is `SoundManager.LimitVoices`.
- Custom recordings under 6 s get their leading silence trimmed at load: the weapon-switch sound now starts at 0.005 s (it was 0.154 s).
- `WeaponHolder` guards a null current weapon (an inventory lost to a script reload during Play mode used to throw every frame).

## 2026-11-25: Contained atrium ambience and a dialogue reply sound

- `RoomAmbience` and `AmbienceZone` (rooms and portals): the atrium ambience loop (crossfaded at the loop point) plays all around you in the atrium. Through the hall door it comes from the doorway, fading with distance and losing highs, and it is silent once the door closes. The range is a second zone behind the range door. The station hum and the shuttle engines are contained the same way: the shuttle engines used to reach the hall through every wall and were the "airplane" drone.
- `Sfx.DialogueReply` (override `dialogue2.mp3`) plays when the NPC answers a chosen line.
- Fixed: the zones hung under the moving ambience object, so their portals flew away and the doorway volume read 0.

## 2026-11-25: Rifle recoil, playground dev controls, player hit zones

- Automatic weapons use a damped-spring kick (mostly back, a little up, random roll) with a gentler camera climb and a slow recovery. The pistol and shotgun are unchanged.
- Playground: F2 opens a spawn menu (number keys, Shift = knowing, Tab = faction; weapon switching pauses while it is open). F4 toggles no-clip (fly, untargetable). F1 help trimmed.
- Player hit zones: head 1.8x, body 1x, legs 0.7x (enemies already had per-part zones).
- Sprint pose and head bob ease in and out (about 0.2 to 0.3 s) instead of snapping. Firing from a sprint brings the weapon up in 0.1 s.

## 2026-11-25: Player rifle

- New automatic rifle for the player (`W_Rifle`, slot 4): 30-round magazine, its own `AmmoType.Rifle` ("RIFLE ROUNDS"), 20 damage at 0.1 s, a drop-out magazine reload, ejecting casings, a front sight for aiming, and the enemy rifle sound. In the playground loadout (with 300 rounds), the admin All weapons cheat, and the atrium armory (`P_Rifle`, `P_AmmoRifle`). Enemy shotguns already exist on the Pointman.

## 2026-11-25: Cognitive filters (habituation, stress narrowing, limited capacity)

- Repeated stimuli lose their pull and novel ones redirect attention. Stress narrows vision and dulls hearing (discipline resists). Overloaded units miss low-priority chatter while critical calls always get through. All three sit behind `useCognitiveFilters`. The regression A/B is clean.

## 2026-11-25: Belief fusion and trust between operatives

- Squad reports are merged into one belief by confidence (inverse-variance fusion) instead of the newest replacing it, and they are weighted by trust in the speaker (credibility, listener disposition, and a track record learned by checking reports against own sightings). Belief drift while a squadmate watches the player went from 1.3 to 0.8 m/s, and gun reversals out of sight from 3.7 to 0.2 per minute.
- Crouch is C in the Editor and is now listed on the title screen. Experiments keep ticking when the Editor is unfocused.

## 2026-11-25: Spatial reasoning (occupancy maps, attention, standoff, pre-aim)

- Per-squad occupancy maps on a NavMesh point graph (after Isla 2006), with negative information from what they see and a hiding prior from enclosure. A task-driven attention model picks gaze targets: likely spots, doorways and corners, unchecked ground, inhibition of return, and squad sectors. Vantage points and per-role standoff distances. Search driven by the map. Pre-aim and a threat-axis muzzle. Alert memory: alerted units no longer forget a noise within 5 s.
- A/B experiments (`AIExperiment`): time to spot a hidden player halved (19.6 to 9.6 s), wall-staring fell from 3.8% to 0.2%, and gunmen within 3 m of the player fell from 47.5% to 7.6% of the time. Re-acquisition after the fresh-sighting pre-aim fix improved from a median of 28 to 20 degrees, and gunmen within 3 m fell from 40% to 4.5%. Experiments are capped at 3 minutes and exit Play mode themselves.
- Aim commitment: one committed aim source with hysteresis, a pre-aim lock, and the gun following only deliberate looks. Back-and-forth gun swings out of sight went from 2.2 to 1.1 per op-minute (median from 1.1 to 0).
- Fixed: the MERIDIAN title stuck on screen after switching maps (overlapping fades on one panel), and the playground director losing its config reference on rebuild.

## 2026-11-25: Confidence and morale, map switching, readable dev overlay

- Squad confidence (a baseline plus decaying momentum; fast losses hurt far more), per-unit confidence (temperament, personal state, and panic contagion), leader rally and taunt, breaking (flee, then rally) and surrender (hands up, weapon dropped) for cornered broken insurgents. Confidence leans all decisions, and overconfidence makes them sloppy.
- Map switching: the pause menu has "Go to:" buttons for each other map (`GameManager.Maps`, loading straight into play), and the Editor menu has Alcax > Maps > Open or Play for each map.
- The dev overlays scale with the screen (`DevGUI`, [ and ] to adjust). F3 cycles Off, Full, and Compact, and a squad panel was added. F12 spawns a squad of 10.
- Fixed: in the Editor, Left Ctrl crouch plus S triggered Unity's save shortcut ("exit Play mode to save") mid-fight. Crouch is now C in the Editor (Ctrl still works in builds). `WeaponHolder` is bounds-checked against script reloads during Play mode. Killing blows no longer double count as injuries in squad momentum.

## 2026-11-25: Combat AI, varied behavior, callouts, and concealed flanks

- Layered identity for operatives: faction (Insurgents vs Special Ops: training, comms, style tendencies, doctrine), then role, then individual (traits, name, voice, and a primary and secondary fighting style: Ambusher, Stalker, Brawler, Suppressor, Survivor, Opportunist, Flanker). F2 picks the spawn faction.
- New behaviors: Stalk (crouch-walk a hidden route to get behind you while you don't know where they are), PatchUp (heal once hidden), ambush hold-fire, wound states (limp and poor aim; critical splits into fleeing or a last stand), squad-leader orders (push, fall back, named flanker), in-fight learning per action, and a squad danger map.
- Callouts with subtitles. Shouts are heard by anyone in earshot, you included. Radio covers the faction quietly. Each callout changes what listeners know or do. Synthesized shout and radio voices.
- True flanks (60 to 120 degrees around you) on concealed routes, with duck-walking behind low cover. This cut the share of a flank you could see from 38% to 25%.
- Fixes from testing: duplicate names, ordered flankers looping on arrival, last stands far too common (6 of 8 at first), the radio only reaching its own squad, and investigations stalling on unreachable noises.
- Soak: two factions, 8 to 12 units, the player firing. Zero errors and about 1.9 ms per frame.

## 2026-11-25: Combat AI Phase 1 and the AI playground

- New map `Playground.unity` (Alcax > Build AI Playground): a grid-textured outdoor arena with a crate yard, a wall with doors and windows, a two-floor house with stairs and a balcony, a catwalk to a container stack, a sniper tower, a pillar field, a corridor maze, and hills with a mesa. Every zone is verified reachable on the NavMesh.
- Human operatives with five archetypes: Rifleman, Pointman (shotgun rusher), Marksman, Veteran, and Rookie. They have perception and awareness, beliefs with uncertainty, a utility mind with personality and emotions, dynamic cover finding, player-equivalent guns, and a human aim model with no attack tokens or deliberate misses. Squads share information over a delayed radio. Bodies have strafe IK, crouch, arm IK onto the weapon, flinch, and a physics collapse on death.
- Player crouch (C or Left Ctrl). Player shots now make noise for the AI and suppress operatives they pass close to.
- Dev tools: F1 help, F3 overlay, F4 to F8 spawns, F9 clear, F10 freeze, F11 god mode.
- `BuildCharacter` moved to the shared `CharacterRig`, with identical output for the lobby NPCs. Layer 8 "Actor" was added.
- Soak tested with 10 operatives in Play mode: zero errors or warnings, no units out of bounds or off the NavMesh, and about 2 ms per frame. Fixed four problems found there: shuffling in place on advance, shotguns firing at 37 m, friendly fire through a thin line check, and marksmen leaving high ground.

## 2026-11-25: Lobby NPCs

- 8 ambient residents (`AmbientNpc` brain plus `Npc` rig) with distinct roles, outfits, skin tones, and hair. Activities: sit on bench seats, look out the window, type at consoles, lean on the mezzanine railing, wander, and chat in pairs (turn-taking, gestures, murmur). Each has small-talk dialogue and pauses to face the player during a conversation.
- `StationPoint` markers (65: 48 seats, 8 window spots, 4 consoles, 5 railing spots). The atrium NavMesh is baked in the builder, and the closed wing door keeps residents in the lobby. The player carries a `NavMeshObstacle` so agents steer around them.
- The shared character rig (`BuildCharacter`) has hip and knee joints for walking and sitting. Ines was rebuilt on it.
- Range and aim fixes: the range floor now covers the full width (verified with 288/288 ray hits), aiming switched to a heuristic aim pose with a narrowing crosshair, the door sound is a soft pneumatic spray, and hall strip z-fighting is fixed.

## 2026-11-25: Zombie prototype retired; south wing with a firing range

- Removed all zombie and forest content (44 assets, archived to `Archive/gravepine_zombie_forest_2026-11-25.zip`). See ADR 0003. Reusable code was extracted to `PlayerRig` and `WeaponAssets`. The Admin tab now has God mode, Infinite ammo, All weapons, One-hit kills, Full heal, and +50 ammo.
- The east blast door on the atrium's south wall is now a working `AutoDoor` (sliding leaves, pneumatic sounds) into a service hall. The west door is sealed, and the hall ends at a sealed Sector C-2 door.
- Firing range off the hall: observation windows, a sliding door, a firing-line counter with weapons and ammo, 4 lanes with dividers, a floor stripe, an acoustic backstop and catch berm, 4 target plates, and baked lighting plus probes. The atrium armory and targets were removed.

## 2026-11-25: Atrium detail pass, armory, firing range

- Removed the exterior terrace slab. The dunes now drift up into a sand berm against the window wall.
- Steam now rises from slim slot vents along the floor-wall joint of the side walls, replacing the floor grates.
- Loft-style exposed services under the coffers: two spiral ducts with seams, straps, and diffusers, a central pipe bundle on trapeze hangers with flanges, cross runs with risers and valve wheels, red sprinkler lines with heads, and cable trays. `MeshAccum.Cylinder` was added for these.
- Armory on the west wall with every weapon, ammo, and a medkit (pickup prefabs). Three hinged steel target plates (`TargetPlate`) clang, flash, swing, fall at 60 damage, and reset.
- The user's weapon-switch sound plays through the `Equip` override. License unknown (see the registry).
- Fixed: the shared player builder loads weapon data by path (cached statics were cleared by script reloads, which could leave the atrium player without fists). Admin "All weapons" now works in maps without a loot table (`WeaponHolder.allWeapons`).

## 2026-11-25: Atrium life, NPC dialogue, airport seating, shuttles

- **Dialogue system:** `DialogueTree` (data: nodes of lines and choices), `Npc` (talkable character with idle, head-tracking, jaw, and gestures), and `DialogueUI` (typed lines with blips, choices on 1 to 5 or the mouse, Esc to leave). `GameState.Dialogue` blocks movement and weapons, and `GameManager.InputLive` adds a short input grace after menus close.
- **Ines Varga**, Guest Services, at a new information desk at the foot of the stair. Her dialogue (`Data/Dialogue/DLG_InfoDesk`) covers the station, the planet Tessaly and its moon Kharon, the shuttles, and a lore hook: lab doors cycling on their own, and the missing Dr. Hale.
- Airport beam seating (8 rows of 6) along the sides, replacing the window-facing rows. Steam grates and consoles moved.
- Planters are hollow, with mounded soil beds, pebbles, and plant bases sunk into the soil (`SoilY`).
- Two VTOL shuttles (`ShipTraffic`) loop: approach, hover, land with engine light, exhaust, and pad dust, wait, lift off, and depart. Engine rumble is low-passed as if heard through glass. Second landing pad added.
- Atrium camera far clip raised from 170 m to 4 km, so the full desert and incoming ships show.
- The builder starts the bake on the next editor tick (an immediate bake sometimes threw a null reference).

## 2026-11-25: Fixed near-black atrium textures

- Bug: `AtriumTextures` used `Mathf.SmoothStep(a, b, t)` (a blend) as if it were shader `smoothstep(edge0, edge1, x)` (an edge test). Every pixel was treated as a joint and a tie hole, so albedos came out at about 12% of their intended brightness and smoothness at 0. Replaced with a proper `SS()` helper. Verified: wall albedo averages 217/255, floor 194/255, mask smoothness 0.29.

## 2026-11-25: Atrium look pass, bright Ando concrete and office light

- Feedback: too dark and glossy, a wood feel. The direction is now bright, dry concrete with a clean futuristic feel.
- Every structural surface uses procedural architectural board-tie concrete (`BuildAndo`): 1.82 x 0.91 m panels with hairline joints and six form-tie holes each. The floor gets the same finish with subtler holes. The texture repeats every 3.64 m (`AndoTile`).
- Light tech panels and matte grey seats. White office light: every ceiling coffer has a fixture plus a baked area light, the hanging panels are neutral white, and the sun and shafts are softer and less orange. Grading is slightly cool.

## 2026-11-25: New map, Meridian Station Atrium C

- `Atrium.unity` (build index 1), generated by **Alcax > Build Atrium Level**. Lighting re-bakes with **Alcax > Bake Atrium Lighting**.
- Realistic-leaning direction after the user's reference image: a dark glossy tech floor, slanted chevron frames with hanging cyan-white light panels, a deep window grid, a coffered ceiling with ducts, a south mezzanine with a monolithic grand stair, seating with orange underglow, glowing consoles, steam grates, and selective plants.
- Window view: a desert at sunset with a ringed gas giant (`Alcax/AlienSky` shader), plus exterior base structures.
- Lighting: baked GI (GPU lightmapper, mixed sun, baked area lights for the panels), a box-projected reflection probe, light probes, stronger SSAO, light shafts, dust, and steam.
- Real CC0 concrete scans from Poly Haven. Gameplay for this map is still to be designed.

## 2026-11-25: Settings menu and admin cheats

- Pause > **Settings** has a General tab (music, effects, mouse sensitivity) and an Admin tab. Every cheat starts off: god mode, infinite ammo, all weapons, one-hit kills, invisible to zombies, freeze zombies, and pause the wave timer. Action buttons: next wave now, kill all, full heal, +50 ammo.
- `AdminSettings` (static, so it survives a Restart in the same session) and `GameSettings` (saved to PlayerPrefs). The HUD shows an amber "ADMIN: ..." reminder while any cheat is on.

## 2026-11-25: Gunshot redesign, backhand fix

- Gunshots rebuilt from gunshot acoustics: an N-wave muzzle blast with a ground reflection, a noise blast body plus a low-passed noise punch (no pitched tone, which was the "balloon"), and a limiter. The outdoor tail is sparse, darkening tree and terrain reflections plus a rolling low rumble, replacing the comb reverb (the "echoing room").
- Backhand swing: the head now points forward and lies across the body on the wind-up, so it leads the hit instead of the butt end.

## 2026-11-25: Gun mechanics, audio overhaul, zombie fix

- **Fixed:** zombies frozen. The Zombie script was saved disabled in the prefabs (the builder now forces it on and checks every prefab), and wanderers stalled because the stopping distance (1.05) was larger than the "arrived" check (0.8).
- **Fixed:** the level builder half-ran during Play mode. It now refuses with an error.
- Melee: a second swing, a backhand pivoting from the chest, alternates with the approved forehand. Every swing gets a ±6° random tilt.
- Pistol: the slide kicks back on every shot and ejects a brass casing (physics, tinks on impact), and it locks back on empty. Reloads drop the magazine, the support hand seats a new one, and the slide racks if the gun was empty.
- Shotgun: it pumps after every shot, ejecting the shell, and loads one shell at a time (0.5 s each), interruptible by firing.
- Audio: gunshots get a crack, blast, saturated boom, and forest echo (slapbacks and reverb). The whoosh is now low and airy, not crinkly. Impacts are a thump, smack, and wet squelch, plus a wooden tok (bat) or a slice and ring (machete). Mechanism sounds are new. Zombie voices get a creaky subharmonic growl and throat rattle.
- New: a drop-in **sound override system** (`Audio/Overrides/<Sfx>/`, plus Alcax > Refresh Sound Overrides).

## 2026-11-25: Melee swing and zombie voice

- Zombie voices rebuilt: a creaky glottal pulse through three formants ("uhh" to "ahh") with breath noise, replacing the buzzy saw-plus-vibrato (`AudioSynth.AddVoice`).
- Melee is now a real swing: wind up over the shoulder, then an arc around a shoulder pivot with a wrist snap and a follow-through. Every swing is the diagonal forehand (the mirrored backhand was dropped after playtesting), and a motion trail runs on the weapon tip. The hit lands mid-strike.
- Fists throw alternating jabs.
- Tunables: `WeaponHolder.shoulderPivot`, `strikeTime`, and the key poses in `SwingPose()`.

## 2026-11-25: First playable, GRAVEPINE

- Built a first-person zombie survival loop in a forest: loot, then survive waves.
- Systems: game state shell (title, pause, death, restart), the player controller, 5 weapons (fists, bat, machete, pistol, shotgun), headshots, 3 zombie types with procedural animation, wave spawner, randomized loot, HUD, synthesized audio, and pooled VFX.
- The whole level is generated by **Alcax > Build Forest Level** (see ADR 0002).
- Installed Cinemachine 6.6.0 and AI Navigation 2.0.17. Imported TMP Essentials. Set the URP assets to HDR on and a 60 m shadow distance.
- Fixed: zombie crowds pushing the player's capsule through the map. Bodies no longer collide with the player, and each hit gives a deliberate shove (`ZombieData.shove`).
- Visual scorecard (first pass): palette 4, style 4, lighting 3, silhouette 4, UI 4, feedback 3.

## 2026-11-25: Project setup

- Created the project from the Universal 3D template on Unity 6000.6.10f1.
- Installed the Unity CLI and connected it to the Editor through `com.unity.pipeline`.
- Added the documentation set: `CLAUDE.md`, `README.md`, and `docs/`.
