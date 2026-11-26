# Combat AI Design: Human Operatives

> Status: **design + Phase 1 in progress** (2026-11-25). The test bed is `Playground.unity`.
> Principle: no scripted state machine with fixed timers. Behavior should come out of *what they know*, *who they are*, *how they feel*, and *what the squad needs*, so it reads as natural and stays hard to predict while still being fair and readable.

## 1. Layers

```
Senses ──► Beliefs (memory) ──► Mind (utility + personality + emotion) ──► Tactics (where) ──► Body (how)
                 ▲                         │
                 └──────── Squad (shared beliefs, roles, tokens, callouts) ◄┘
```

Each layer is its own component, so it can be tuned, debugged, and replaced on its own.

## 2. Senses → awareness

- **Vision:** a cone (wide peripheral, narrow focal) with line-of-sight rays to 3 points on the player (head, chest, hips). Each frame it adds *visibility* =
  points visible × focal or peripheral weight × distance falloff × player-stance factor (crouched or still is harder to see; moving or firing is easier) × lighting (flashlight on raises it).
- **Hearing:** sound *events* (gunshot, impact, sprint footsteps, reload click, door) with a loudness radius, attenuated by walls between the source and the listener (one raycast; occluded means ×0.4).
- **Touch:** taking damage reveals the attacker's rough direction immediately.
- **Awareness meter (0 to 1)** per NPC: visibility and sound fill it, and silence drains it. The states are **Unaware → Suspicious (turns toward it, slows) → Alert/Searching → Combat**, then **Lost → Searching**. The meter means no instant 360° detection, and players can learn the stealth rules.

## 3. Beliefs, not cheating

- Each NPC holds a **belief** about the player: last known position, velocity, a timestamp, and an **uncertainty radius** that grows while the player is unseen (about 1.5 m/s).
- **Prediction:** when the player vanishes, the NPC extrapolates their last velocity along the NavMesh to guess where they went.
- **Information spreads through the squad by talking:** an NPC that sees you *calls it out* (a bark plus a subtitle). Allies who hear the callout update their belief *with the caller's error and a short delay*. Someone who never saw you and never heard a callout doesn't know where you are. **The player can overhear the callouts** ("Contact, by the crates!", "He's reloading!") and use them.

## 4. Mind: utility with personality and emotion

- Every few hundred milliseconds the NPC scores a set of **actions** with *considerations* (normalized response curves). The candidates:
  Idle/Patrol · Investigate · TakeCover · ShootFromCover (peek) · Advance (cover to cover) · Flank · Rush · Hold/Ambush · Retreat · Reposition · Suppress · Search
- **Personality traits** (per archetype, with per-unit random jitter) weight the considerations: aggression, caution, discipline, teamwork, patience, and accuracy.
- **Emotions** are dynamic values that decay over time and shift both the scores and the execution:
  - **Fear:** rises with damage taken, near misses, allies dying, and being outnumbered. Raises cover and retreat, lowers accuracy and willingness to peek.
  - **Anger:** rises when hit by the same enemy repeatedly or when an ally dies close by. Raises rush and advance, and lowers caution.
  - **Stress / suppression:** rises with incoming fire close by. Forces heads down, shakes aim, and fades quickly.
  - **Confidence:** squad health and numbers versus the perceived threat. A confident squad pushes.
- **Anti-predictability without randomness soup:**
  - (a) Score noise of ±8% is resampled every few seconds per NPC, so it isn't per frame.
  - (b) **Commitment hysteresis:** a new action must beat the current one by a margin, and that margin decays the longer the current action runs.
  - (c) Action cooldowns and "boredom" penalties for repeating the same choice.
  - (d) Reaction time per NPC (150 to 450 ms) before switching from the default to the chosen action.

## 5. Tactics (where to stand)

- **Dynamic cover points:** sample the NavMesh around nearby obstacles. For each candidate, cast rays from the believed threat position to the candidate's crouch and stand heads.
  - Blocked at crouch and visible at stand means **low cover** (crouch, pop up to shoot).
  - Blocked at both, but a short side step exposes the threat, means **corner cover** (lean or step out to peek).
- **Scoring:** protection × (1 / travel cost) × preferred engagement distance (per archetype) × spacing from allies (no clumping) × flank-angle bonus (for flankers) × "not where I just was".
- **Danger-aware pathing:** candidate routes are scored by how much of the path is visible from the threat (sampled along the path). Flankers pick routes that stay out of the player's sight cone, so a flank is a real sneak, not a straight run.

## 6. Combat model: capable, not handicapped

**Design stance (user decision, 2026-11-25):** the enemies don't hold back. There are no attack tokens, no deliberate warning misses, and no artificial throttling. They fight like people whose lives depend on it. Difficulty is tuned through encounter design and player tools (cover, information, stealth, mobility), **never** by crippling the AI. What stops them from being aimbots is the same physics and human limits the player has:

- **Same weapons:** NPC guns use the player's model: magazine size, reload time, fire rate, per-shot recoil climb, spread bloom, and recovery.
- **Human aim:** NPCs don't read the target's true position directly. Aim is a point that *tracks* the believed target with:
  - **reaction time** before they start tracking and after the target changes direction (per unit, 150 to 450 ms)
  - **tracking lag** that grows with the target's *angular* speed across their view (strafing close is hard to track, while walking straight at them is easy)
  - **error** from their own movement, stress and suppression, distance, and skill
  - **recoil** they must compensate for, so disciplined units burst-fire while panicked ones spray
- **They exploit openings:** a reload, a heal, a weapon switch, a player stuck in the open, or a player who's lost track of them (attacking from outside the player's view cone).
- **Melee rush:** pointmen sprint to melee when close, when the player is reloading, or when anger is high. They're interruptible by heavy hits.
- **Hit zones and reactions:** head ×2, torso ×1, and limbs ×0.6, each with a directional flinch. Big hits stagger (interrupting the action and aim), and death is a physics collapse.

## 7. Squad brain

- A shared blackboard holds the best belief of the player, members, roles, tokens, and recent events.
- **Dynamic roles:** about every 2 s the squad assigns *anchor* (hold and suppress), *flanker* (the one with the best flank route), *pointman* (rush-capable), and *watcher* (covers the angles), based on positions and traits.
- **Coordinated moves:** bounding overwatch (one moves while one covers), pincer (two flank sides), and fall back together when confidence collapses.
- **Search:** when the player is lost, members sweep the predicted positions in a spread pattern instead of all walking to the same spot.

## 8. Body

On the existing jointed rig with foot IK:
- aim stance with the weapon held up
- crouch (the IK drops the pelvis)
- strafe and backpedal gait (the IK steps along the velocity, not just forward)
- peek-lean
- hit flinches and stagger
- death collapse

Each NPC carries a weapon visual (rifle, pistol, or knife), with a muzzle flash, tracers, and synthesized shots heard in 3D.

## 9. Debug and tuning (core, not an afterthought)

- **An overlay above each NPC:** current action, top 3 utility scores, awareness bar, and emotion bars (fear, anger, stress), toggled with **F3**.
- **Gizmos:** vision cones, the belief sphere, cover candidates colored by score, the chosen path, and token holders.
- **A playground dev panel:** spawn an archetype or a squad at a marker, freeze AI, show debug, time scale, reset, and god mode.
- **Data-driven tuning:** an `EnemyArchetype` asset (traits, weapon, ranges, reaction times) and a global `CombatAIConfig` (perception rates, token count, curve shapes), editable live in Play mode.

## 10. Archetypes (start set)

| Archetype | Weapon | Traits | Plays as |
|---|---|---|---|
| Rifleman | Rifle | Balanced | The backbone: cover, peek, advance |
| Pointman | Shotgun + knife | Aggressive, low caution | Rushes, flanks, punishes reloads |
| Marksman | Scoped rifle | Patient, accurate, cautious | Holds long angles, relocates after shooting |
| Veteran | Rifle | Disciplined, high teamwork | Squad lead: issues flanks, rarely panics |
| Rookie | Rifle | Low morale, jittery aim | Panics, retreats, makes mistakes |

## 11. Playground

A purpose-built test arena:
- an open killzone
- low and high cover (crates, barriers, walls with doorways)
- a two-floor building with interior stairs, windows, and a balcony
- a catwalk and a sniper tower
- a pillar field, a tight corridor maze, and rolling uneven terrain with ramps
- spawn markers and a dev panel

## 12. Phases

1. **Phase 1 (built 2026-11-25, see section 13):**
   - **Map:** the playground and the NavMesh.
   - **The enemy:** body and weapons, senses and awareness, beliefs.
   - **Mind:** the utility core (patrol, investigate, cover, peek and shoot, advance, rush, retreat, search).
   - **Combat:** the aim model (no attack tokens, per section 6), hit zones, reactions, and death.
   - **Tools:** the debug overlay and the dev spawner.
2. **Phase 2:** near-miss suppression (for NPCs **and** the player), danger-aware flanking, callouts with information spreading (and overheard subtitles), full emotions, coordinated search, and bounding overwatch.
3. **Phase 3:** morale break (panic, surrender), flushing (grenades and smoke), lean-peek polish, ragdoll death, an adaptive difficulty director, and the archetype roster expanded.

## 13. Phase 1 as built

Code is in `Assets/_Project/Scripts/Runtime/AI/`. The map comes from `PlaygroundBuilder` (Alcax > Build AI Playground), which also writes the data assets and the operative prefabs.

| Piece | File | Notes |
|---|---|---|
| Orchestrator | `Operative` | Runs the chosen action each frame: movement, facing, crouch, firing, melee, and the reactions to hits and death |
| Senses | `Perception` | Focal and peripheral cone, line of sight to head, chest, and hips; awareness meter (Unaware, Suspicious, Alert, Combat); hearing through `Hearing` |
| Belief | `Belief` (in `Perception.cs`) | Last-known position plus velocity, growing uncertainty, and dead reckoning for about 1.5 s. Never the true position while unseen |
| Mind | `OperativeMind` | Utility scores for 12 actions, score noise resampled every few seconds, a commitment bonus that fades, emotions (fear, anger, stress), a startle delay on first contact, and a bonus for keeping high ground |
| Cover | `CoverFinder` | No cover nodes: samples NavMesh rings, raycasts from the threat's eye at crouch and stand heights, and classifies each spot as Low, Full, or Corner (with a side-step peek point). Scored per intent (hold, advance, retreat, flank). Moves that don't achieve their intent are rejected |
| Weapon | `OperativeWeapon` | Magazine, reload, bloom, and muzzle climb, like the player's guns. Bullets can hit the player (capsule test), the world, or a squadmate in the way |
| Aim | `OperativeAim` | A critically damped follow with a capped turn speed, latency that skill partly cancels by leading, wandering error from stress, movement, and target speed, and a settle period after acquiring. Fire is held only for an effective-range limit or a squadmate in the line (a body-width sweep) |
| Body | `OperativeBody` | Strafe-aware foot IK, a crouch with a staggered stance, two-hand arm IK onto the weapon grips, reload and melee motions, and flinch. The weapon points exactly along the aim, so the visuals match the bullets |
| Squad | `Squad` | Radio reports (0.7 s delay plus position error), casualty events, claimed cover spots, and who is firing (covering fire raises advance and flank scores) |
| Player hooks | `PlayerTarget` | Body points, crouch and flashlight visibility, footstep, reload, and gunshot noises, and near-miss suppression of operatives |
| Data | `CombatAIConfig`, `EnemyArchetype` | `Data/AI/`. The config is created once and never overwritten, so tune it live. Archetype values are set by the builder |
| Dev tools | `PlaygroundDirector`, `AIDebugOverlay` | F1 help, F2 spawn menu (number keys; Shift = already knows where you are; Tab = faction), F3 overlay, F4 no-clip, F9 clear, F10 freeze, F11 god mode |

The player now has a crouch (hold C or Left Ctrl). It lowers their visibility and quiets their footsteps.

Layer 8 is **Actor**: combat characters have per-part colliders on it, with `HitZone` multipliers (head 2.2, torso 1.0, hips 0.9, legs 0.7 and 0.6, arms 0.6). Vision, cover, and foot IK rays use `AILayers.World`, which skips actors and the player.

Pulled forward from Phase 2: player shots suppress the operatives they pass close to, and operatives lay down fire on a last-known position to cover advancing squadmates.

**Known gaps (next):** a HUD damage-direction indicator for the player, suppression effects on the player's own aim, a pistol stance check, and a real ragdoll (death currently collapses the body, then hands it to one rigid body). Callouts and concealed flank routes were added in section 14.

## 14. Variety, comms, and concealment (2026-11-25)

**Layered identity.** Every unit is built from three layers, so two riflemen never fight the same way:
1. **Faction** (`Faction`, `Data/AI/FAC_*`): training offsets on every trait, how much individuals vary, comms (radio or shouting), style tendencies, adaptability, whether they plan concealed routes, and patch-up kits. Two are built in. **Insurgents** are looser, louder, and more varied, favor brawling and ambushing, and take direct routes. **Special Ops** are disciplined, quiet on the radio, sharper, and quicker to learn, and they favor flanking and stalking.
2. **Role** (`EnemyArchetype`): the weapon, ranges, base traits, and the role's own style weights (Pointmen lean Brawler and Stalker, Marksmen lean Ambusher).
3. **Individual:** jittered traits, a name and voice pitch, and a **fighting style**. A primary and a weaker secondary style are rolled from faction weights times role weights.

| Style | Tendency |
|---|---|
| Ambusher | Holds angles and waits. While you don't know where they are and you're closing in, they **hold fire** for a surer first burst |
| Stalker | Creeps up on you (crouch-walking, nearly silent, on a concealed route) while you don't know where they are, then opens fire when close and you're facing away |
| Brawler | Rushes and fights in the open, and doesn't like waiting in cover |
| Suppressor | Peeks and lays down fire on your last position for the team |
| Survivor | Takes cover early, retreats, patches up, and rarely makes a last stand |
| Opportunist | Pushes harder on openings: when you reload or have just been hit |
| Flanker | Goes around |

**Wounds.** Below 60% health a unit is *Wounded*: it limps, its aim gets worse, it turns more cautious, and it calls "I'm hit!". Below 30% it is *Critical* and the unit splits on character. Some make a **last stand** (fear stops mattering: they hold and fight harder, and shout "Come on then!"). Most **flee**: they retreat and, once hidden and unpressured, **patch up** (crouched, weapon down, about 4 to 5 s for up to 35 health). That's a window you can punish.

**In-fight learning.** Each action keeps an outcome score. Getting shot during it lowers it, landing hits raises it, and seeing a squadmate die while doing it lowers it too, scaled by adaptability. The squad also keeps a **danger map** of spots where members got shot. Cover choice and routes then avoid those spots ("he's watching that corner").

**Callouts** (`Callouts`, shown as `CalloutSubtitles`) are information, not flavor:
- **Shouts** carry to everyone in earshot, allies of any squad and you, with subtitles that say where the voice came from. They give away the speaker's position.
- **Radio** reaches the whole faction after a short delay. You only hear the squelch within about 7 m.
- **Content:** contact and position updates (listeners update their belief), reloading and "cover me" (listeners lay down covering fire), "he's reloading" and "he's hit" (others push), "man down" (grief and anger), and "lost him".
- Stalkers stay silent.

**Squad leader** (the most skilled and team-minded alive, marked ★ in the overlay) reads the fight every few seconds. With heavy losses and a scared squad it orders **Fall back**. When you're reloading or hurt it orders **Push**. In a stalemate it sends **one named flanker** to a named side ("Rami, get around left!"). Shouted orders only reach those in earshot.

**True flanks and concealed routes.** `CoverFinder.FindFlank` looks for cover 60 to 120 degrees around you from the squad's line, at the unit's fighting range. `ConcealedRoute` plans the way there. Graph nodes are the plain NavMesh path (subdivided, so a route always exists) plus points sampled around it and along a corridor. Dijkstra's cost is length × (1 + 6 × exposure) plus danger-map spots, where exposure is line of sight from your eye at crouch height. On the move they **duck-walk** wherever only low cover hides them. Measured in the playground on 9 real flanks: the share of the move you could see fell from 38% to 25% (26 m to 18 m of exposed ground), and planning took about 1.6 ms.

**Operative footsteps** are audible (sprinting carries, sneaking is nearly silent) and they make noise for other AI too.

Dev: **F2** cycles the spawn faction (Insurgents, Special Ops, Mixed). The F3 overlay adds style, faction, wound state, "unseen by you" vs "you know where I am", orders, the last callout, hidden routes (cyan), danger spots (red X), and a **COMMS** log of every callout, including radio you couldn't hear.

## 15. Confidence and morale (2026-11-25)

**Squad confidence** (-1 shaken to +1 confident) = a **baseline** from how the fight stands, plus **momentum** from events.
- **Baseline:** the share of members still fighting, numbers (1 to 8), whether a leader is alive, and whether you were just hit or are reloading.
- **Momentum:** fades back to the baseline over about 30 s. A loss costs 0.15, plus 0.08 for each other loss in the last 15 s, so losses in quick succession hurt far more. A leader's death costs 1.4 times as much. Injuries cost by severity. Hits on you raise it. Killing a man who surrendered enrages his squad rather than scaring it.
- The leader **rallies** a shaken squad (a boost scaled by his discipline, every 15 s at most) and **taunts** when winning.

**Each unit's confidence** = the squad's mood times their temperament (faction volatility × discipline), plus personal state (health, recent hits, fear, nerve), plus **contagion** (allies nearby running or giving up), passed through a soft curve.

**Effects:**
- **Confident:** bolder choices (advance, rush, flank, engage). Overconfidence is sloppy: above +0.5 they skip concealed routes and stay exposed longer when peeking.
- **Shaken:** cover, hold, and fall back. A last stand is less likely.
- **Breaking:** below a breaking point (deeper with discipline, nerve, and faction break resistance), a frightened unit **breaks**: it flees with its weapon down, shouts "I'm out of here!", and spreads the panic. After 14 to 25 s, once it's calmer or far away, it rallies and returns to the fight.
- **Surrender:** a broken unit you corner (within 10 m, in sight, aimed at) may **surrender** if its faction allows it and its discipline is low. It drops its weapon (physics), raises its hands, and is out of the fight.

| Faction | Volatility | Break resistance | Surrender |
|---|---|---|---|
| Insurgents | 1.25 | 0 | yes |
| Special Ops | 0.6 | 0.3 | never |

**Measured** (10 insurgents): 4 losses spread over 25 s took confidence from +0.54 to +0.21, it recovered, and they kept pushing with no breaks. 4 losses within 1 s took it to -1, 4 units broke, and cornered ones surrendered. The survivors rallied about 30 s later.

**Dev:** the F3 overlay cycles Off, Full, and Compact, and scales with the screen (`DevGUI`). [ and ] change the dev text size. A squad panel shows the members fighting, the confidence bar, the leader, and the last order. F12 spawns a squad of 10.

## 16. Spatial reasoning: where he could be, where to look, how close to get (2026-11-25)

Grounded in published work. Each piece has an on/off switch in `CombatAIConfig` so it can be A/B tested against the earlier behavior.

**NavGraph** (`NavGraph`): a point graph over the walkable NavMesh (about 7,000 nodes at roughly 1.5 m spacing in the playground, built in about 75 ms when the first squad spawns). Links only where the NavMesh connects directly, so stairs link and floors don't link through ceilings. Each node also stores an **enclosure** score (how many of 8 short rays hit something within 2.5 m).

**Occupancy map** (`OccupancyMap`, one per squad; after Isla, "Probabilistic Target Tracking and Search Using Occupancy Maps", *AI Game Programming Wisdom 3*, 2006):
- A sighting collapses the map onto the player.
- While unseen, it diffuses at an assumed player speed (3 m/s).
- Ground a member actually sees (line of sight at crouched head height) is emptied. This **negative information** is what makes a search smart.
- Noises, hits, and reports multiply it by a Gaussian likelihood (a Bayesian update).
- When it has to guess (a vague noise, or everything has been cleared), it applies a **hiding prior**: people hide in corners and beside cover, not in open ground (weight 0.35 + 1.3 × enclosure). Good information sharpens the map; without it, reasonable assumptions shape it.
- Only the squad's own senses feed it, so there are no wallhacks.

**Attention** (`OperativeAttention`): gaze is a series of fixations (about 0.4 to 1.5 s) chosen over 16 directions by task value. This follows Hayhoe and Ballard, "Eye movements in natural behavior" (2005): gaze goes where the task needs information. Each direction scores:
- occupancy probability visible along it,
- ground the squad hasn't seen recently,
- **depth discontinuities** (doorways and corners, the CQB "danger areas"),
- open distance (a wall a meter away is worth nothing),
- the direction of travel,
- the last-known position and recent noises.

Penalties: **inhibition of return** (Posner and Cohen, 1984) and overlap with a nearby squadmate's gaze, which produces 360-degree security sectors. The pick is a softmax, so disciplined units look consistently and rookies erratically. Pitch follows the height of the likely ground (stairs, balconies). The raised muzzle follows the eyes. The overlay shows why each glance was chosen ("look: doorway/corner", "likely spot", "unchecked").

**Standoff** (per role: Pointman 3 m, Veteran 6, Rifleman 7, Rookie 8, Marksman 14). CQB practice is to observe a danger area from distance and clear it by angles, not walk into it:
- Investigating and searching go to a **vantage point** that sees the spot from standoff distance, preferably with cover.
- Advance and cover choices refuse spots inside the standoff distance or on ground where the target probably is.
- Concealed routes pay extra to pass near probable-target ground.
- Rushing gunmen stop at shooting distance and the rush ends there. Only shotgun rushers close in.
- Melee happens only at arm's length (under 1.2 m), when reloading or dry, or for rushers.
- Too close: Engage gains weight and backs off while fighting.

**Map search**: search goals are the most probable unclaimed clusters (members avoid each other's targets), observed from a vantage point and reached by a route hidden from that spot.

**Pre-aim and threat axis**: in combat with a recent fix (under 15 s), the muzzle stays on the threat while moving or hiding. Good information comes first: a fresh sighting, or a target that was standing still when last seen, is aimed at directly. Only when it is stale does it fall back to the highest-probability ground visible from the **peek position** (or from the current position while moving), weighted toward the last-known position. So when they come out they are already on target rather than swinging around to find you, and their reaction is quicker when you appear where they expected.

**Alert memory**: awareness in the Alert state now decays slowly (0.006/s, about a minute) where a vague glimpse fades fast. Before this, operatives who heard something forgot it within about 5 s and gave up halfway to the source.

**Experiments** (`AIExperiment.Run("search" | "distance", trials, timeScale = 4, maxMinutes = 3)`, Play mode; the same seeds for both arms). Capped by real time. When it finishes, hits the cap, or is aborted (F9 or Esc), it restores the switches and time scale and exits Play mode itself. The bots look fast during a test because it runs at 4x:
```
SEARCH (player hidden in a house room; squad of 4 alerted by a noise; 8 trials per arm)
  time to spot player:           before 19.6 ± 21.2 s (median 7.7)   after 9.6 ± 14.5 s (median 6.4)
  useful gaze (4 m+ open view):  before 81.5 %                       after 93.1 %
  staring at a wall (<1.5 m):    before 3.8 %                        after 0.2 %
  closest approach before seen:  before 12.6 m                       after 14.3 m
DISTANCE (squad knows where the player is; 6 trials per arm)
  gunman (not the shotgun rusher) within 3 m:  before 47.5 ± 16.2 %   after 7.6 ± 6.7 %
  any operative within 3 m (incl. rusher):     before 78.0 %          after 81.0 %
  median operative distance:                   before 10.4 m          after 9.9 m
  aim off target when re-acquiring sight:      before median 23.3 deg after median 23.4 deg (first pre-aim, map only)
RE-ACQUISITION after the fix (fresh sighting first, map only when stale; 10 trials per arm, 3-minute cap)
  aim off target when re-acquiring sight:      before median 28.2 (mean 35.6) deg   after median 20.4 (mean 28.3) deg
  coming out of cover (peek/hold):             before median 3.3 deg (n=11)         after median 4.4 deg (n=6), a tie, too few samples
  gunman within 3 m:                           before 39.9 %                        after 4.5 % (median 0)
  median operative distance:                   before 11.2 m                        after 13.9 m
```

**Aim commitment (2026-11-25).** The weapon now holds **one aim decision** (an `AimSource`: Target, ThreatAxis, Gaze, or Rest) instead of blending three that each recomputed on their own:
- Spotting the target wins immediately. Any other change waits 0.6 s.
- The threat axis follows the action: it holds the line on the target through a fight and hands the muzzle to the eyes only when the unit is actually searching or investigating.
- A pre-aim spot is adopted only if it differs by more than 10°, has stayed the best for two checks, and then stays locked for 2.5 s (unless new information moves it more than 70°).
- Eyes lead and the gun follows: only deliberate looks move the muzzle, at most every 1.2 s and only for looks more than 20° away.
- The choice between facing the path and facing the gaze has hysteresis (105° on, 75° off).
- The overlay shows "aim: <source>".

Measured out of sight (10 trial pairs; reversals are a gun swing one way and straight back):

| | Back-and-forth swings per op-minute | Aim source switches per op-minute |
|---|---|---|
| Before | 2.2 (median 1.1) | 13.7 (median 6.0) |
| After, first try | 2.5 (median 3.1) | 15.6 |
| After, now | **1.1 (median 0)** | 12.5 (median 6.8) |

The remaining switches are mostly sight flickering between Target and ThreatAxis. That's visually seamless, because the threat axis aims at the fresh last-known position.

## 17. Shared information: fusion and trust (2026-11-25)

**What is shared.** A report carries position, velocity, the time it was true, and an error margin. Radio factions stream it with a delay and added error. Shouting factions call it out, and the error grows with distance to the listener. Each belief's uncertainty grows while it is unconfirmed, and that serves as the confidence grade (`Belief.Confidence`).

**Fusion** (`fuseReports`). Previously the newest report replaced the belief. While one squadmate kept the target in sight, the others received a new report every half second, each with its own random error, so their estimate (and the aim that holds on it) jumped by meters. Now every report is **merged into one belief by inverse-variance weighting**, the same rule as sensor fusion and the Kalman filter. Both estimates are projected to now and weighted by 1/uncertainty². A precise fresh report pulls hard and a vague old one barely moves it. A report more than 2.5 times worse is ignored. Own eyes always win. There is always exactly **one** belief, so the decision and aim layers never choose between competing versions.

**Trust** (`useTrust`). People weigh who is talking and learn whom to believe.
- **Initial trust** comes from the speaker's credibility (skill and discipline; another squad counts for less) times the listener's disposition: rookies believe everything, while skilled, independent types discount others.
- **Learned trust:** when a unit sees the target itself, it grades the reports it received in the last 4 s. Within the stated error plus 1.5 m, trust rises by 0.06. Off by more than twice the error plus 4 m, trust falls by 0.12.
- **Effect:** a report's uncertainty is divided by trust, so a distrusted source counts as vaguer than it claims, and with a good picture of their own they ignore it entirely.
- The F3 overlay shows "trusts <name> <value>".

**Measured** (10 trial pairs; the player stands still while a squadmate, not this unit, sees him):

| | Before | After |
|---|---|---|
| Belief movement (ideally 0) | 1.3 m/s (median 1.9) | **0.8 m/s (median 1.1)** |
| Belief error | 0.8 m | 0.8 m |
| Gun reversals out of sight | 3.7/min (median 1.1) | **0.2/min (median 0)** |

In a 40 s fight, Special Ops trust climbed to 0.9 to 1.0 as their calls checked out, the Pointman got 0.75 to 0.8, and insurgents stayed near 0.6 (less credible, rarely verified).

**Note:** experiments force `Application.runInBackground`. Otherwise Unity pauses the game when the Editor is unfocused, and a run looks frozen at 0 trials.

## 18. Cognitive filters: prioritizing and ignoring noise (2026-11-25)

The brain doesn't process everything equally, and these filters (switch `useCognitiveFilters`) model three documented effects in front of the existing senses:
- **Habituation** (`Perception.HabituationFactor`): a stimulus of the same kind from the same 30° sector loses pull each time it repeats (1.00, 0.59, 0.42, 0.32, 0.26). It recovers with quiet (exponential, time constant 4 s). A new direction or a new kind of sound gets full weight and redirects attention. Only arousal habituates (awareness gain and attention interrupts). The information (position estimate, occupancy map) is still recorded, and a loud gunshot still triggers combat.
- **Stress narrowing** (tunnel vision and auditory exclusion; Grossman, *On Combat*; Artwohl's officer-involved shooting studies): the peripheral cone shrinks with stress^1.5, down to about 70 to 100°, and hearing loses up to 50% of loudness. Discipline halves both. In a test fight, stressed rookies and insurgents narrowed from 145° to 100-115°, while calm, disciplined units stayed at 137-145°. A pinned, panicking squad is easier to flank.
- **Limited capacity:** load = stress + 0.3 if a target is in view + 0.3 if under fire. Capacity = 0.6 to 1.25 (skill and discipline). Overloaded units miss low-priority callouts (36 in a minute in the test fight). Contact reports, man down, leader down, fall back, push, and flank orders addressed to them always get through. They also clear less of the occupancy map (fewer line-of-sight checks).

The F3 overlay shows "load X/Y" (red when overloaded) and the current field of view.

Regression check (10 trial pairs, everything on vs. off): gunman within 3 m 3.3% vs 51.3%; re-acquisition median 6.4° vs 12.5°; out-of-sight reversals 0.7 vs 1.5 per minute; belief drift 0.6 vs 1.5 m/s. Nothing regressed.

**Test scripts** live outside the project, in %LOCALAPPDATA%/Temp/alcax_agent, because Unity clears the project's `Temp/` folder.

## 19. Cover repertoire and the ammo budget (2026-11-25)

**Four ways out of cover.** A peek is now a choice between four exposures, each trading risk for information and accuracy (switch: `useCoverRepertoire`; off means every peek is a full step-out, the old behavior):

| Move | What shows | Fires? | Chosen when |
|---|---|---|---|
| Info peek | head only, about 0.6 s (half rise over low cover, a low lean around a corner) | only within 8 m; turns into a lean if he spots an unaware player | information is old or uncertain; cautious and patient traits |
| Blind fire | only the gun, over the top or around the side | yes, ×2.6 error on top of suppression (realistically wild) | a squadmate is moving, Suppressor style, fear, pressure, the last peek drew fire; never on stale info or low ammo |
| Lean | head, shoulder, gun (body offset 0.3 m, 16° roll) | aimed | skilled and disciplined; the default for a fresh target |
| Full step-out | everything | best shot | aggression, confidence, close range, the player reloading; never after a peek that drew fire |

Each move is scored and gets ±0.1 noise, so the same unit varies. **Peek-spot variation:** after a peek that drew fire (or two peeks from the same place, for skilled units), the operative slides 0.7–1.1 m along low cover before popping up again.

**Ammo budget** (switch: `useAmmoBudget`; off means infinite reserve). Each archetype carries `reserveRounds` (Rifleman 120, Pointman 24 shells, Marksman 30, Veteran 150, Rookie 90). Reloads draw from the reserve. Under 30% of a full load and beyond 8 m, bursts cap at 2 rounds and the fire tolerance tightens (make each round count); suppression stops unless covering a squadmate.

**Dry.** With nothing left: one "Cover! I'm out!" callout, confidence −0.35, and the mind switches to a non-shooter set: take and hold cover, retreat, spotting-only info peeks (so he still feeds the squad's shared picture), and a melee rush only within 9 m for aggressive types. The full economy (sidearm switch, scavenging, sharing ammo) is deferred.

**Experiment** (`AIExperiment.Run("cover", 6)`): the player stands in the yard, and a simulated shooter fires every 0.4 s at whatever part of an operative it can see. Only the two switches above toggle. Results are in the devlog.

## 20. Staying engaged when he hides (2026-11-25)

Problem: the mind judged "fresh contact" only from the unit's own eyes, so squad reports and heard shots updated the belief but not the decision. After 6 s unseen, peeking and pushing switched off while searching only began at 4–11 s, and holding cover gained score while blind. Cautious units sat indefinitely.

Fix (switch: `useInfoEngagement`):
- **Freshness from the best information:** the contact age is the smaller of own-sight age and the fused belief's age (when its confidence is above 0.3), so a squadmate's callout keeps everyone live.
- **Re-acquire peeks:** in cover with stale info, PeekShoot stays available. The cover repertoire turns it into a head-only info peek, which converts to a lean if it finds him unaware.
- **Restlessness:** holding cover without new information has a patience-scaled budget (3–10 s). Beyond it, hold loses up to 0.7 and peek, flank, advance and search gain. Patient units still hold an angle; nobody holds forever.

Hide experiment (`AIExperiment.Run("hide", 8)`): the player sits in a hiding spot the squad believes he just ducked into, for 25 s. Off vs on: passive hold on stale info 12.9% vs 4.8% of op-time; active 81% vs 84% (median 91% vs 99.5%); re-sighted after 7.6 s vs 5.8 s.

## 21. Fighting cover, movement doctrine, stamina and fatigue (2026-11-25)

**Fighting cover** (switch `useFightingCover`). Live probes showed two defects: units hid behind cover with no firing line, where peeks failed instantly, and moves were overruled mid-run by small score swings (cover, then advance, then shooting in the open, every 1 to 3 s). Now:
- Peek-proof (Full) cover is only picked for retreating or when out of ammo. Standing behind it doesn't count as being in cover, so they move to a spot they can fight from.
- A move under way (take cover, advance, flank, retreat) gets +0.3 commitment until it arrives. Getting hit weakens it, and it fades after 10 s.
- A firefight or peek gets +0.15 for its first 3 s.
- Advancing over open ground under his eyes, when the last search found no forward cover, scores −0.4 (scaled by discipline, below).
- With the cover repertoire on, fear and pressure barely penalize peeking (blind fire and info peeks are the low-risk choices), so scared units blind-fire instead of freezing.
- Probe, off vs on: time in peek-proof cover 8.5% vs 2.9%, fighting with eyes on 11% vs 19%.

**Cover discipline** (`Doctrine`, 0 to 1) = faction `coverDiscipline` (Insurgents 0.4, Special Ops 0.85) + role offset (Veteran +0.15, Marksman +0.1, Rifleman 0, Rookie −0.1, Pointman −0.15) + 0.3·(discipline − 0.5), ± 0.08.
- Strict units get more PeekShoot, less Engage in the open, take cover when seen, and wait for a squadmate's covering fire before advancing.
- Loose units fight in the open and push, but take cover when it is obvious: shot at, reloading, or hurt.

**Move style**, chosen per move (switch `useMoveDoctrine`):
- **Dash:** gun at low ready, facing the run, no shooting. Sprints across exposed ground or while fresh; otherwise runs.
- **Stop and shoot:** runs, plants for 0.7–1.4 s to fire a burst when he's in view, then runs on, waiting 2–4 s before the next plant.
- **Fire on the move:** the old walk-and-shoot at 60% speed.
- Within 6 m everyone shoots while moving.

**Stamina and fatigue** (`useMoveDoctrine`):
- Sprinting drains stamina (about 11 s from full). Running drains a little. Walking recovers it, and standing still recovers it fastest. Location doesn't matter.
- Below 0.1 stamina the unit is winded: no sprinting until 0.4. Aim error rises up to ×1.45 below 0.3.
- Every sprint also adds long-term fatigue (scaled by the archetype's `fitness`: Veteran 1.15, Pointman 1.05, Rifleman 1, Marksman 0.95, Rookie 0.9). Fatigue fades over minutes, slows recovery by up to 60%, and caps stamina at 1 − 0.5·fatigue.
- The F3 overlay shows stamina/cap, WINDED, the move style, and discipline.

**Probe** (8 units, about 1 minute, 4 per faction):

| | Insurgents (d 0.28) | Special Ops (d 0.92) |
|---|---|---|
| Move styles | fire on move 62, dash 19, stop and shoot 16 | dash 67, stop and shoot 44 |
| Shooting while moving | 13% | 5% |
| Time in PeekShoot | 2% | 27% |
| Engage in the open | 20% | 9% |
| Peak fatigue | 0.19 | 0.20 |

## 22. Contact drill and fire fixes (2026-11-26)

**Diagnostics.** `Operative.HoldFire` records why the last fire check didn't shoot (no wantFire, reacting, ally in line, aim off, burst pause, cycling, reloading, and so on). The F3 overlay shows it while the unit sees the player.

**Why they saw the player and didn't shoot** (probe, share of in-sight samples):
- 37%: the action never asked to fire. Causes: a canRush unit (the shotgun Pointman) held fire inside 6 m to melee; flanks stayed silent beyond 12 m even when spotted; dashes never fired.
- 21%: a squadmate was in the line of fire, and they waited.

**Fixes** (switch `useContactDrill`):
- Rush fires whenever it has ammo.
- A spotted flanker (PlayerKnowsMe) may shoot.
- A dash that stays in view for over 1.2 s, while not under fire, becomes stop-and-shoot.
- An Engage blocked by a squadmate sidesteps 1.6–2.6 m to a spot with a clear line.
- Probe after the fixes: no wantFire 20%, ally in line 2%. The actively firing share (cycling between shots plus burst pauses) roughly doubled, from about 23% to 46%.

**Contact drill.** Being shot at or hit now counts as PlayerKnowsMe. Before, a relaxed unit that got shot at could pick Stalk and crouch-walk in the open. For 3.5 s after entering combat, or 4 s after any shot at them, while not in cover:
- Stalk, Investigate, Search and PatchUp are ruled out, and HoldCover gets −0.3.
- TakeCover gets +0.45 (+0.25 if only fresh contact), with a forced dash.
- Engage gets +0.25 + aggression·0.2 when he's in view.
- The take-cover move may open with a 0.7–1.4 s burst first: aimed if he's in view, suppression toward the shots if not. The chance rises with aggression, discipline and confidence, and falls with fear.
- There's no ducking (crouch-walking) while dashing or under fire.

**Contact experiment** (`AIExperiment.Run("contact", 8)`): a relaxed squad in the open, 22 m away, takes near misses every 0.6 s. Drill off vs on:

| | Off | On |
|---|---|---|
| First shot back (mean) | 3.7 s | 0.8 s |
| First shot back (median) | 2.4 s | 0.4 s |
| In cover at 6 s | 34% | 25% |
| Visible to the player (median) | 70% | 51% |
| Stalk / Investigate / Search under fire | about 1% | about 1% |

The reaction burst costs a moment before running, and the yard's cover is sparse. The crouch-walk you saw needs a relaxed unit that hears shots without a near miss. That case isn't reproduced here, but the PlayerKnowsMe fix covers it.

## 23. Hit response: push through or duck (2026-11-26)

Problem: every hit forced a duck. A hit ended the peek, added a big pressure spike (which the contact drill turned into "take cover now"), and the learning step marked the current action as bad. So peek, get hit, duck, peek again became a predictable, farmable loop, worst in close quarters.

Now (switch `useHitCommit`), a hit during Engage, Rush, PeekShoot, Advance or Flank is a choice. The commit score is −0.25, plus:
- aggression·0.4, nerve·0.35, confidence·0.3, anger·0.3;
- +0.3 under 10 m, 0 at 10–18 m, −0.3 beyond 18 m;
- +0.22 for each duck in the last 20 s;
- +0.25 if they hit the player in the last 3 s;
- +0.6 in a last stand, +0.3 when rushing;
- minus fear·0.4, the hit's share of max health ×1.2, missing health ×0.4, and 0.5 when critical (unless in a last stand);
- ± 0.2 noise. Above 0.35, they commit.

Committing lasts 2.5–4 s:
- The peek stays up 0.6–1.2 s longer, pressure rises 0.25 instead of 0.6, and the contact drill is off.
- The current action gets +0.4, the Outcome lesson is 1.0× instead of 2.5×, and there's no instant re-decision.

Ducking is the old behavior, and each duck is counted. After two ducks in 20 s, the spot counts as a failed peek (peekFails), which pushes toward relocating, flanking, or advancing. The F3 overlay shows COMMITTED and the recent duck count.

**Hit experiment** (`AIExperiment.Run("hits", 8)`): a simulated shooter hits whatever part it can see (every 0.5 s, 50%, 9 damage), with the squad at 9 m or 22 m. Old always-duck reflex vs the tuned choice:

| | Old reflex | Tuned choice |
|---|---|---|
| Duck on a hit at 9 m | 100% | 32% |
| Duck on a hit at 22 m | 100% | 53% |
| Duck loops (3+ ducks within 20 s) | 59% of operatives | 34% |
| Close-range shots fired per op-minute | 68 | 94 |
| Hits taken per op-minute | 28 | 30 |

The first, untuned pass (a 0.2 threshold, flatter range terms) committed about 78% at both ranges, with 3% loops. Under this relentless fire (about one hit a second), repeated ducking at 22 m is partly realistic. The metric doesn't separate the same spot from relocated peeks.

## 24. Space awareness, layer 1: fingerprints and space-scaled distances (2026-11-26)

The AI can't read images, so each space is digested once per map into numbers, the way a person reads a room at a glance. Layer 1 is the automatic per-point fingerprint plus distances that fit the space. Layer 2 (rooms and doorways as a graph; the builders can emit exact room and door volumes like the audio rooms and portals) and layer 3 (CQB behavior: slicing corners, stacking at doors, flanking through another door, room-by-room search) are planned, not built.

**Fingerprint** (`NavGraph.Space[]`, built with the graph; the Playground has 7,061 nodes in 119 ms):
- **Indoor:** a ray straight up from 1 m hits something within 8 m.
- **MeanFree / MaxSight:** the average and longest of 16 rays at eye height (to 40 m).
- **Width:** the narrowest wall-to-wall axis (corridors and doorways).
- **OpenFraction:** the share of directions with more than 6 m free.
- **Size:** MeanFree indoors; outside, max(2·MeanFree, 25).
- **Labels:** corridor, small room, room, tight outside, open.
- The Playground: 6,671 open, 195 tight outside, 168 room, 13 small room, 14 corridor; the building interior reads as a room about 7 m across.

**Space-scaled distances** (switch `useSpaceAwareness`). The fight space is the smaller of my space and his believed space, rechecked every 0.4 s:
- standoff = min(archetype, max(2.5, 0.45·space));
- range min = min(archetype, max(3, 0.6·space));
- range max = min(archetype, max(range min + 4, 1.6·space));
- cover search radius = min(config, max(6, 1.3·space));
- in a fight space under 20 m, the flank ring's inner radius drops from 8 m to 4 m.

These replace every fixed use: standoff checks, TooClose, vantage search, the Engage keep-distance, the rush stop, cover-finder bands, and the advance step. Open ground is unchanged. The F3 overlay shows the space label, the fight-space size, and the effective keep and range. The mind context carries Indoor and FightSpace for later layers.

**Indoor experiment** (`AIExperiment.Run("indoor", 8)`): the player and a squad both on indoor nodes, 5 to 9 m apart, for 20 s. Off vs on:

| | Off | On |
|---|---|---|
| Mean distance | 7.2 m | 6.7 m |
| Face to face (under 2 m) | 3% | 3% |
| Time on outdoor nodes | 40% | 36% (very noisy, ±40) |
| Firing while in sight | 88% | 84% |

A tally of what they do outside: mostly Engage (shooting in from outside), Flank (going around the building), and TakeCover (doorframes and corners outside). Those are legitimate for a small building, and layer 1 doesn't change them. Making "stay in the room" vs "go around" a real decision needs the room and door graph (layer 2). The Playground has only one small building, so indoor testing is thin.

## 25. Space awareness, layer 2: rooms and doorways (2026-11-26)

**Authored, not guessed.** The level builders emit the rooms and doors they build, like the audio rooms and portals, so the AI's room map is exact:
- `CombatRoom`: an axis-aligned box, name, and span.
- `CombatDoor`: rooms a and b, where null means outside; width; and a stairs flag.
- `RoomGraph` collects them per scene. `RoomAt(p)` returns the smallest containing room, or null for outside or unmapped. `FirstDoorToward(myRoom, hisRoom, myPos, hisPos)` returns the door of my room on the shortest walk: Dijkstra over door positions, where two doors link if they share a room (outside included), and a climb counts double.
- NavGraph nodes inside a room count as indoor even without a roof hit.

**First use** (switch `useRoomGraph`). When he's believed to be in another room and unseen, pre-aim holds the doorway toward him (within 20 m, in line of sight), not his spot through the wall. The F3 overlay shows [my room > his room, holding door].

**Playground changes:**
- The map is now 104 m across instead of 124 m. The hills, mesa, boulders, spawns and player start moved in. The maze, the wall line, the yard, the pillars and the tower are unchanged.
- The house's ground floor is now a CQB layout:
  - a central corridor (z −1 to 1) from the east door to a west window;
  - four rooms off it: SW, SE, NW (with the stairs), and NE;
  - a SW–SE connecting door, which makes a loop inside the building;
  - exterior doors east (the corridor), south (SE room), and north (NE room);
  - windows on every side, including one at the corridor's end for a long sightline.
- The upper floor, balcony and catwalk are unchanged.
- That's six rooms (five on the ground floor plus the upper floor) and ten doors, including the stairs and the balcony.
- Interior doors are 1.4 m, because the NavMesh bakes with the default agent radius of 0.5 m and 1.1 m doors came out closed. All checked paths through the house are complete.

**Check** (two operatives spawned in the SW room, the player in the NE room): both resolved the player's room, moved into the corridor, and held the NE doorway while peeking from cover. No errors.

**Next (layer 3, CQB behaviors):** stack at a door before entering; slice the corner (pie) instead of walking through; clear a room by angles; flank through another door (the loop); search room by room on the room graph; hold the doors of his room as a squad instead of everyone aiming at one.

## 26. Layer 3: room clearing (CQB) and aim from the space (2026-11-26)

**ClearRoom action** (switch `useCqb`). Scored when he's believed to be in another authored room (CanClear: a reachable door, and the squad hasn't cleared that room in the last 20 s), he's unseen, and we're not dry or reloading. The score is 0.35 + aggression·0.25 + teamwork·0.1 + discipline·0.15, plus 0.1 with allies, 0.25 when lost, restlessness·0.3, and 0.1 indoors; minus fear, low health and pressure. Search gets −0.3 when a room can be cleared, so searching in a building happens room by room. Styles: Flanker +0.1, Brawler +0.12, Ambusher −0.15.

**Door choice:**
- The shortest walking path, but only to doors whose approach doesn't pass through the room itself. This rules out the balcony door, which you can only reach from inside the upper floor.
- A third man in a stack, or a flanker once someone has a door, takes another door.

**The procedure:**
1. **Approach** to a stack point beside the door, off the line of fire through it. The aim follows the route, then the opening.
2. **Stack:** 0.6–1.3 s, shorter for aggressive units. The first man waits up to 3 s for a partner on his way. The second stacks on the other side.
3. **Pie:** ease to the threshold at walking pace while the muzzle sweeps from the far side (seen first) to the near side over 1.1–1.8 s, skipping directions that hit a wall within 1.5 m.
4. **Entry:** the first man crosses to the far side along the wall, the second hooks to his own side. The entry is committed for 2.5 s, and there's a "Going in!" call.
5. **Dominate:** for 1.4–2.4 s, cycle the room's other doors in view (0.9 s each) and its visible corners (0.45 s each); in a corridor, only its two ends. Then "Clear!", the room is wiped from the squad's occupancy map, and it's marked cleared.

Contact before the entry ends the action, so the mind picks the fight; the 30 s timeout ends it too. **Stairs:** go to the foot (the last path corner downstairs), stack briefly, climb at walking pace with the muzzle on the landing, then enter and dominate.

**Aim from the space** (switch `useSpaceAim`). The problem was unseen aim pointing at walls, ceilings, and irrelevant directions. Causes: pre-aim held the bearing to a fresh-but-occluded position or to the occupancy centroid straight through walls (through the ceiling for a target upstairs), and attention cues behind a wall right in front of them outweighed the wall penalty. Now:
- **RouteAim(target):** the NavMesh path toward him (rechecked every 0.5 s). Hold the point where the path leaves view: the last visible corner, nudged 1.5 m along the next leg (3 m when the corner is within 3 m, to look around it rather than at the frame). Corners within 2.5 m are skipped. If the first leg is already out of view, look level along it. If the whole path is visible, aim at him. Used for occluded fresh beliefs, the occluded moving centroid, and the no-map fallback.
- **Attention:** the route direction gets +0.9. Last-seen and noise cues are scaled by openness, clamp01((depth − 1.5)/3).
- **CQB aims:** the route on approach, the opening at the stack, and the deep corner on entry only if visible (otherwise the room center).

**CQB experiment** (`AIExperiment.Run("cqb", 8)`):
- The player is tucked into the spot least visible from 20 probes around the house and from beyond the room's doors (on even trials; upstairs on odd trials).
- A 4-man squad starts outside, knowing his room.
- 30 s per trial; both switches toggle together.

Results from the final run:

| | Off | On |
|---|---|---|
| Time to find him | 7.4 s | 5.8 s |
| Operatives entering his room | 53% | 66% |
| Unseen aim into a wall within 1.5 m, out of cover | 12.3% | 11.2% |
| Steep aim into a nearby ceiling or floor (all trials) | about 0% | 0.4% |
| Same, player upstairs | 0.1% | 0.9% |

- **Wall-aim noise:** the previous run (before stair clearing) gave 10.5% vs 5.9%, so run-to-run variance is about the size of the effect.
- **What changed it:** the room-clear procedure's own wall-aiming fell from 152 samples to 9 after the aim fixes. Cover poses are excluded from the metric (the gun points at the cover by design).
- **Steep aims:** the ones seen earlier were looking up the open stairwell or toward the balcony, which is intended.
- **The procedure rarely completes in this test:** about 6% of entries. They usually spot him through a doorway at an angle during the approach, and it becomes a normal fight. Phase samples: Approach 730, Pie 116, Stack 62, Entry 22.
- **Bugs found and fixed while testing:** a NavMeshPath created in a field initializer (Unity forbids it; 108 exceptions), aim at doorframes at elbow distance, and approach routes through the room being cleared.

## 27. Steady moves: fewer, clearer behaviors (2026-11-26)

Playtest reports: enemies held fire at range, ran off looking elsewhere, walked back and forth mid-move, and walked backwards in halls. Instrumented first: the ranged-contact test, and per-unit reversal and backpedal counters (`Operative.Reversals`, `MoveTicks`, `BackTicks`).

**Causes found:**
- **At range, in his line of sight,** about 45% of the time was spent dashing with the gun low, facing the run (movement doctrine, plus the contact drill forcing a dash under fire). Moving units couldn't shoot beyond 30 m, and stop-and-shoot waited 2–4 s between bursts.
- **Reversals** came from one move handing off to another with a different destination: cover behind, then an open advance forward, then cover behind again. Also, open-ground advance steps aimed at a straight-line point beyond a wall (a U-turn around it), and ClearRoom and Advance alternated with different destinations.
- **Backpedaling unseen:** the body faced the threat axis or a gaze direction behind the route.

**Rules** (switch `useSteadyMoves`):
1. **In view, fight while moving.** A dash becomes stop-and-shoot (disciplined units, or over 20 m) or fire-on-move. Gun-low sprinting happens only when he's not in view and they're not under fire (retreats excepted). The contact drill no longer forces a dash. Moving fire reaches 0.6 × weapon range. Stop-and-shoot plants for 0.9–1.6 s every 1–2 s.
2. **No backward cover runs.** Hold-cover spots farther from him than I am lose 0.12 per meter. Only Retreat goes backward.
3. **No open-ground advance while he's in view.** It ends the move and the mind picks something else. Exception: loose, aggressive units (discipline under 0.35, aggression over 0.6), who keep walking forward shooting. Unseen advance steps follow the NavMesh route, not a straight line.
4. **Settle.** For 2.5 s after a move arrives, other moves lose 0.3, so they fight from the new spot first. This doesn't apply under fire in the open or in a last stand.
5. **CQB has priority.** While a room can be cleared and he's unseen, Advance is off and Flank loses 0.4.
6. **Face the walk.** Moving over 1 m/s without eyes on him, the body never faces more than 90° from the walk direction. The threat-axis aim switches to the path's next corner (agent.steeringTarget) when the threat is behind the route. Backpedaling remains only while firing at him in sight.

**Results**, off vs on (medians in brackets):

| Ranged contact, 8 trial pairs | Off | On |
|---|---|---|
| Firing while in his line of sight | 18.5% | 22.0% (17 vs 26) |
| Gun lowered in his view | 40% | 2% |
| First shot back, median | 2.5 s | 0.5 s |
| Mid-walk reversals | 10.5% | 6.5% (11.8 vs 4.7) |
| Walking backwards unseen | 2.5% | 1.1% |

| Indoor (hall), 8 trial pairs | Off | On |
|---|---|---|
| Walking backwards unseen | 11.2% | 1.0% |
| Mid-walk reversals | 16% | 9.7% (19 vs 11) |
| Time to find him | 5.8 s | 5.9 s (unchanged) |

**Test runner fix:** `run_exp.sh` read the last line of the CLI output to detect completion, but the CLI appends a hint line, so it always polled to its cap, up to about 4 minutes after the test ended. It now greps for the answer and also stops when Play mode has exited. A full A/B takes about 75 s.

## 28. Peek spam: counter-peek and the hit-stun limit (2026-11-26)

Playtest report: rapidly peeking, shooting and hiding (at a doorframe) got little return fire, even when the shots landed.

**Counter-peek** (switch `useCounterPeek`):
- Perception records LastSeenHead, Repeek (a new sighting within 2 m of the last one and within 6 s), and PeekStreak.
- While he's hidden, pre-aim holds LastSeenHead whenever it's in view, so a ducking target's dead-reckoned position doesn't drag the muzzle behind his cover.
- A re-peek into a muzzle already within 12° gets 0.35× reaction time: an anticipated stimulus is a simple reaction.
- After a streak of 2 or more, units that are aggressive (over 0.4), disciplined (over 0.6), or Suppressor-style put bursts of up to 3 rounds on the spot 0.3–2.5 s after he ducks, while in Engage, HoldCover or PeekShoot, with ammo to spare.

**Hit-stun limit** (switch `useHitStunLimit`). The real cause. Every hit reset the burst, added a 0.15–0.4 s fire pause, and kicked the aim, so a player landing hits every 0.1 s locked the target out of shooting entirely. Now:
- Only the first hit of a volley (no hit in the last 0.8 s) breaks the burst and pauses fire; committed units (hit response) don't pause at all.
- Follow-up hits kick the aim 1/(1+n) as hard and stagger at 0.4×.

**Tests** (6 trial pairs each; the player alternates 1.0 s out and 1.4 s hidden, 10 peeks per trial):

| Test | Off | On |
|---|---|---|
| Crate, hits every 0.4 s: peeks answered | 88% | 97% |
| Crate, hits every 0.4 s: first shot back | 0.4 s | 0.2 s |
| Doorway (inside out and outside in), same switch: peeks answered | 93% | 98% |
| Doorway: first shot back | 0.3 s | 0.2 s |
| Rapid hits on one enemy (every 0.12 s): that enemy fires back during the peek | 10% | 75% |

**Peek prefire** (switch `usePeekPrefire`, added after section 28). The squad, not each member, learns his rhythm: `Squad.NoteSighting` tracks the peek spot, the streak (a reappearance within 2 m and within 6 s), and the hide time between peeks (EMA).
- **Holder:** one per spot, the member whose muzzle is closest to it and who has a line to it. The line is re-checked on every call, and the job is handed over when the holder loses it (for example, a rusher hugging the far side of the cover).
- **On the beat:** after a streak of 2 or more, the holder fires on the spot from 0.4 s before his learned hide time until 1 s after. It aims at chest height if there's a line, else head height. If neither, a holder behind low cover rises for it, latched for 1.3 s; without the latch, rising cleared the line and dropped him back down, a bob loop.
- **Others:** they keep their muzzles on the spot (counter-peek). Suppressor-style units add slow fire, but only with a line.

**Result** (8 trial pairs, crate and doorway, hide time varied 1.1–1.7 s):

| | Off | On |
|---|---|---|
| Rounds on the spot while he's hidden, per peek | 0.2 | 1.1 |
| Rounds already arriving in his first 0.15 s out | 0.7 | 0.7 |
| Peeks answered | 94% | 85% (±21) |
| First shot back | 0.3 s | 0.4 s |

The fire on the spot is real, but it doesn't measurably improve the moment he steps out. The counter-peek already answers in about 0.3 s, and the simulated peek is a teleport, so the player never walks into the stream of fire the way a real peek would. Left on for playtesting; the switch turns it off.

## 29. Station life: civilians and security (2026-11-26)

The Atrium is a peaceful area with people who can get hurt and a security force that responds to crime: the first step toward open-world dynamics outside combat zones.

**Civilians** (`Civilian`, on all residents, Ines, the food court vendors and the quartermaster):
- Built on the shared rig with hit colliders (HitZone multipliers), Health 70, and a kinematic body for a physics collapse on death.
- They panic on gunfire they can hear (loudness over 0.12; range fire doesn't count unless they're in the range or it's very loud) or when hit.
- AmbientNpc residents run (3.4–4.3 m/s) to a reachable spot away from the shooter, preferably out of his line of sight, then huddle facing away until 25 s of quiet, and re-run if the threat moves 5 m. Standing NPCs duck in place.
- No dialogue while panicking. Events: `Civilian.Hurt` and `Civilian.Killed`.

**Security** (`StationSecurity`, in the Atrium scene):
- Spawns guards from `OP_Guard`: the Guard archetype (pistol, 12+36 rounds, careful, disciplined) and the Station Security faction (radio, cover discipline 0.55, can surrender). They walk patrol routes (atrium ground floor; food court and mezzanine; hall wing and armory).
- Guards start neutral (`Operative.HostileToPlayer = false`): perception ignores the player's sight and noises.
- **Crimes:** player gunfire outside the free-fire zone (the range), or hurting a civilian or a guard. Witnesses (in sight, or within 25 m) turn hostile after their reaction time; the rest by radio after 1.2–2 s, with a position error by distance. From then on it's the full combat AI.
- **Stand down:** after 40 s with no new crime and no guard seeing him. Dead guards are replaced 30 s later.
- **HUD:** "WANTED" with the live guard count and a countdown, plus alert and stand-down toasts.

Tested in play: range fire didn't alert security. Shooting a resident in the atrium made 11 of 13 civilians panic and turned all 3 guards hostile within about 1 s (peek-shoot, flank, rush). A killed resident collapses. No errors. The stand-down and replacement weren't exercised in the test.

## 30. NPC versus NPC (2026-11-26)

**Targets.** `ICombatTarget` is anything the AI can see, track and fight; `PlayerTarget` and `Operative` both implement it. Perception, aim, the decision layer, flanking, melee, peeks and callouts work against `Perception.Target` instead of the player. `Targets.Of(gameObject)` resolves noise and damage sources.

**Sides:**
- The player is targeted per `HostileToPlayer` (security only after a crime).
- Operatives are targeted by faction (`Faction.hostileTo`; either side listing the other is enough), never their own squadmates, and always on a grudge (anyone who shot at me, or whom I shot).
- Defaults: Insurgents are hostile to Special Ops and Station Security; Special Ops and Security are on the same side.

**Contacts and focus.** Each unit keeps contacts (last seen, heard, shot at me; a belief per contact):
- Sight checks on up to 3 other hostile candidates every 0.25 s; the current target is watched by Perception itself. Two sightings put an unaware unit into combat.
- The focus is re-picked every 0.4 s. The score is: seen in the last 1.5 s +2 (in the last 8 s, +1; heard in the last 6 s, +0.6), shot at me in the last 4 s +1.6, fired in the last 2 s +0.4, minus 0.03 per meter, plus 0.7 for the current target.
- A candidate needs real evidence within 60 s. A new focus is held at least 2 s unless it dies or someone else just hit me.
- Switching saves the old target's belief and loads the new one's.
- Squad reports carry their target: a report about someone else becomes a contact. The squad's occupancy map follows the leader's focus.

**Shots between NPCs:**
- Bullets already hit any Health (hit zones apply).
- Hits on a hostile count as hits.
- Near misses now crack past other operatives, suppressing them and telling them who's firing (friends' rounds don't suppress).
- Nobody fires through a squadmate or a friendly player.

**Station security:**
- **ThreatActive:** a guard or armed staff has a hostile NPC contact from the last 30 s, plus 5 s of slack. During it, the HUD shows "SECURITY ALERT", the player's gunfire is not a crime, and the quartermaster arms up (he disarms 15 s after the threat ends, if you're not wanted).
- **Crimes:** hurting a guard or civilian is still a crime mid-fight. Guards then add you as a contact and weigh you against the hostiles.
- **Stand-down:** releases you, but keeps fighting any hostiles that remain.

**Civilians:**
- They panic on seeing an armed station threat within 20 m, or a wanted player within 14 m, in line of sight.
- Brave ones fight whoever attacked them (or the nearest threat when an opening appears), not just the player.

**Admin presets:** "Security guards (2)", "Insurgent assault squad (4)", and "Skirmish: insurgents vs special ops (4 v 4)" (two squads 25–35 m apart, each with a rough fix on the other). The F3 overlay shows each unit's target and contact count.

**Tests:**
- **Atrium, 3 insurgents spawned:** within 6 s all 3 guards targeted insurgents and were firing; the quartermaster armed and rushed; 18 of 24 civilians panicked. The player's gunfire during the fight did not make him wanted. Shooting a guard did: one guard switched to the player while the others stayed on the insurgents.
- **Playground skirmish, 2 runs:** Special Ops won (insurgents 1 and 4 dead, Special Ops 0 dead). Target switches went from 8.2 to about 5 per fighter-minute after the evidence and dwell fixes. Nobody targeted the unseen player while hostiles were alive.
- **Player-fight regression:** the ranged-contact test matches its earlier numbers.
- No errors.
