# Story: Prologue and Act 1

Beats, spaces, and the chip mechanic. The spine is [game-design.md](game-design.md). Times are rough.

## Prologue

| Beat | Time | What happens | Mechanics taught |
|---|---|---|---|
| Hotel room | 8 to 15 min | He wakes, ready to leave on a business trip | Drink from the faucet, key opens the safe, loot, equip, consume, maybe craft food |
| Lobby and spaceport | 5 to 10 min | Elevator down, grab food, board the flight | Talking, trading; implicit: stealable things show red, punching and stealing have repercussions |
| Boarding and calm flight | 3 to 5 min | Camera locks to the window (stars, distant planets). Then free: walk, talk, grab things, return to the seat, fall asleep | |
| The attack | | Alarm: hostile threat, authorities called. Window lock: a huge alien ship warps in, smaller ships launch, blow open the hull (lights out, emergency lights), enter, blow the connecting door, scan the passengers, cull some | |
| Capture | 5 to 8 min with the attack | He is chosen as a prisoner. A robot rips him from the seat and holds him; another fires a mask device around his head. The visor goes black and gasses him | |
| Transfer and cell | 2 to 3 min | Wakes in a cell on a small ship, sees the civilian ship destroyed through a window. Sleeps, wakes in a cell. **Title drop.** | |

Pacing reference: Half-Life 15 to 20 min before the disaster, The Last of Us about 15, Skyrim 20 to 30, Fallout 4 30 to 60, Fallout 3 1 to 2 hours (often called too long). This prologue lands at 25 to 40 min.

### Direction

- **Make the calm uneasy.** The TV news foreshadowing, a lobby PA "security advisory on outbound routes", the departure board flipping to DELAYED or REROUTED, an NPC nervous about the border systems, a crew member watching a screen a little too long.
- **Keep the player in control** during the attack and capture: free head movement and small actions while locked in the seat.
- **The title is the payoff:** right after the cell, the player acts. A long passive cell makes the whole prologue feel long in hindsight.
- **Safety net:** if the hotel tests slow, open with a 10-second flash-forward to the warp-in, then cut to waking in the hotel.

## Spaceport layout (prologue hub)

```
                    [ Landing pads: seen through the atrium windows ]
                                        ||
[Hotel lobby] -- [ATRIUM: info booth, seating, boards] -- [Security checkpoint] -- [Departure lounge] -- [Docking tube -> Ship]
                    |                         |
            [Armory + range]          [Food court + restrooms]
                    |                         |
                    +---- [Service corridor: STAFF ONLY] ----+
                                   |
                    [Maintenance bay] -- [Ops / control room]
```

| Space | Path | Purpose |
|---|---|---|
| Security checkpoint | critical | Takes the boarding pass; weapons are checked or surrendered, which is why he is unarmed when the Chromas board |
| Departure lounge | critical | Last calm moment, NPC talk, view of the docked ship |
| Service corridor | optional | Backstage loop linking armory storage and the food court kitchen; trespass, stealth, higher-stakes theft |
| Maintenance bay | optional | Overlooks the pads, industrial clutter, better loot |
| Ops / control room | optional | Screens and radio chatter; overhear the border advisory or see an unidentified contact |

Guardrails: three optional rooms, about five minutes to finish. One industrial modular kit covers the corridor, bay, and control room, and gets reused for the ship's service areas. Getting caught in staff-only areas is its own consequence, separate from fighting in the lobby (pillar 3).

Touches: boards listing his flight; "Gate C closed for renovation" behind a barrier; travelers with luggage, a queue at security, PA announcements; the armory as partly his company's showroom, where the trader knows him.

## Act 1: The Leash

The Chromas transmute his body for his new role and implant a chip in his brain. The chip:

- translates alien language into human language;
- grants some alien abilities (to be decided);
- tracks his location at all times;
- punishes disobedience with an electroshock.

**Awakening (proposed):** he wakes in the Chromas lab, alien speech is noise. The chip activates, the noise becomes words, the wall glyphs decode into English, and he sees his modified arm.

### Dialogue shock (decided: the simple version)

It tells the player they are not free yet; its absence later reads as progress.

- Forbidden options appear in red (the same "red means forbidden" as theft).
- Picking one: screen flash, distorted audio, a camera jolt, his line cuts off mid-sentence ("Listen, they're making me..."), the NPC reacts ("You okay?").
- The option is removed for that conversation; he picks an allowed one. No damage, no escalation, no trust penalty.
- Use it sparingly: one forbidden option in a few key conversations. A pool of four or five NPC reactions.
- After the release, red options stop appearing, and the first conversation afterward lets a formerly forbidden option through.

Build cost: a `#forbidden` dialogue tag, a `chip_leashed` flag, one shared shock effect (flash, shake, sound), the reaction pool; optionally a `defiance_count` flag.

### First mission

Infiltrate a human faction hostile to the Chromas, based on the lawless planet: find them, join, earn their trust, steal information or access for the Chromas. Proposed: the faction scans recruits (tension with a chip in his head), and a handler can contact him at any time through the tracking.

### The release

The tracking and shock are disabled, by the player or by an event; the other perks stay. Candidates: the faction's tech, a black-market surgeon (costs credits), hacking it, an event (an EMP, a damaged Chromas ship). In reserve: is it truly off? A late reactivation threat could depend on how it was disabled.

### After the release

| Who | Reaction (proposed) |
|---|---|
| Ordinary humans | Fear or distrust; some shops refuse him, scanners flag him |
| Human resistance | Values him as a weapon, doesn't fully trust him |
| Syndicates | Want to buy, rent, or extract the tech |
| Other species | Respect or curiosity: he speaks their language |

What he can do that other humans can't: read alien terminals, overhear Chromas talk, trade with other species, and a Chromas-tech augment branch in the skill tree upgraded with scavenged alien parts.

### Factions (New Vegas model)

Every faction is available early; paths lock at commitment points (a betrayal, a key quest), never on a random line. Reputation per faction. Paths split and reconverge at shared key events that play out differently. Endings combine each faction's outcome. Candidates: Chromas (loyalist), the human resistance, the human government and allies, syndicates, the brain civilization, independent.

Flags so far: `chip_leashed`, `defiance_count` (optional), one reputation value per faction.
