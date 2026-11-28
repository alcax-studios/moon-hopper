# Moon Hopper: Game Design Document (the spine)

> Status: **pre-production.** Working title **Moon Hopper** (adopted 2026-11-27). This page is the spine: vision, pillars, loops, structure, scope. Detail lives in the linked docs, and every design change lands here first.
>
> - Story beats, spaceport layout, and the chip: [story.md](story.md)
> - Inventory, economy, theft: [inventory.md](inventory.md)
> - Combat AI: [combat-ai.md](combat-ai.md)
> - Production plan (pipeline, testing, engine bake-off): [../tech/production-plan.md](../tech/production-plan.md)

## Vision

- **Genre / platform:** first-person open-world survival and crafting with Fallout-style combat and RPG choices. PC.
- **Hook (draft):** a retired special-ops commander turned weapons salesman is abducted by a synthetic empire, chipped, and sent to betray his own species. What he does once the leash comes off is up to you.
- **Experience goal:** a normal working world that slowly turns apocalyptic, totalitarian, and alien, and a player who survives it by being resourceful, not by being chosen.
- **Setting:** year 5071 AD. The hub is **Kelxor**, a desert moon that serves as a travel hub for humans and their allies.
- **Protagonist:** player-named, human, retired special-ops commander, now a sales rep for an advanced weapons R&D company. The stakes are his own future and freedom.
- **Tone:** starts normal and functioning, then shifts toward apocalyptic, totalitarian, or alien as the game goes on.

## Pillars

1. **Scarce resources and an economy.** Survival and progress force financial gain and skill growth. Every credit has a source and a sink.
2. **Gear and skill open the world.** Weapons, strategy, skill trees, and equipment gate new areas and raise the odds of success.
3. **The world remembers.** Choices change the world permanently, and a track record limits or opens interactions (theft, trespass, factions).

## Loops

| Loop | Length | What happens |
|---|---|---|
| Moment to moment | seconds | Scout, infiltrate, engage, loot. Manage stamina, hunger, thirst, energy. |
| Session | 20 to 60 min | Go out (explore or take a mission), unlock something, return to the hub or a base. |
| Meta | the whole game | Credits and parts buy equipment, enhancements, a better home, and ships. Faction reputation opens or closes paths. |

Design backward from the feeling (MDA): pick the aesthetic, then the dynamics that cause it, then the mechanics that cause those.

## Factions

| Faction | Role |
|---|---|
| Humans | Technologically behind; survive by diplomacy |
| Chromas | Synthetic conquerors, thousands of years ahead. Shoulder-mounted weapons; bipod, tripod, and hover robots; hive-like and strategic |
| Brain / biome civilization | Planet-bound, neutral; possibly a hidden or late path |
| Close ally | Human-like, slightly more advanced |
| The lawless planet | Tatooine-like; every faction converges there; syndicates |

## Structure

| Part | Player is | Content |
|---|---|---|
| Prologue | a civilian | Hotel room, spaceport, flight, Chromas capture, title drop from the cell |
| Act 1: The Leash | a coerced operative | Body modification, the chip, the first mission: infiltrate a human faction on the lawless planet. Climax: the leash comes off (shock and tracking end, perks stay) |
| Act 2: Open world | free, and marked | Faction paths open, reputation builds |
| Act 3: Commitment | committed | Choices lock out paths; endings combine each faction's outcome |

Recommended scope: three full main paths (Chromas, resistance, independent); the other factions as reputation and side-quest factions.

## MVP (15 min to 1 hr)

Wake in the airport hotel room (first base), board a business flight, the ship is intercepted by Chromas, the crew is culled, the player is taken prisoner and the ship destroyed, then coerced to work for them. Estimated at 25 to 40 minutes. Beats in [story.md](story.md).

## What already exists (and where it fits)

| System | Where it serves the MVP |
|---|---|
| First-person controller, stairs, jumping, stamina | Everywhere |
| Weapons, armory trader, firing range | Spaceport Gate 1 (the armory as his company's showroom) |
| Combat AI (guards, operatives) | Spaceport security, later the Chromas |
| Inventory, containers, wearables, consumables | Hotel room tutorial (sink, safe and key, fridge, eat, equip) |
| Economy, traders, theft, witnesses, fines, scanner drones | Spaceport: red-highlight theft and repercussions (pillar 3) |
| Survival vitals, bed and sleep | Hotel room, the seat on the flight |
| Dialogue with camera framing | Lobby, lounge, flight; later the forbidden-option shock |
| Time of day (Day, Dusk, Night) | Hub mood |
| Room 1202 (hotel room), atrium, food court, info desk | Prologue hub; needs a hotel lobby and elevator, a security checkpoint, a departure lounge, a docking tube |
| F1 admin console | Grows into the debug console for setting story state |

The old in-world names (moon Kharon, gas giant Tessaly, Dr. Hale and the cycling lab doors) predate this plan and are parked until the lore pass decides what stays.

## Controls

WASD move · Mouse look · LMB attack or use the held item · E interact (hold for drag) · R reload · 1 to 8 or wheel for the hotbar · Tab inventory · F flashlight · Shift sprint · Space jump · Esc pause · F1 admin console

## Open questions

- The unique hook mechanic (is the chip it?).
- Why is he chosen as a prisoner after the scan?
- What happens in the holding cell, and how fast does it turn active after the title?
- Which human faction does he infiltrate, and why are they enemies of the Chromas?
- Which two or three alien abilities does the chip grant?
- How is the leash disabled: a player choice, an event, or both?
- How many main paths: three (recommended) or more?
- Is the protagonist voiced: fully, silent, or effort sounds only?
- Biomes, core cast, story chapters, hero assets, environment kits, rig archetypes.
- Death and checkpoint rules; prologue items (briefcase, contract, boarding pass).
