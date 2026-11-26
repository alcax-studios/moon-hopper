# Game Design Document (untitled)

> The working title MERIDIAN was dropped on 2026-11-26. In-game names stay generic ("the station", "Atrium C") until the lore is written.

> Status: **pre-production.** The setting and a vertical-slice space exist. The core gameplay is not designed yet (a planning session is next).
> The earlier zombie-survival prototype ("GRAVEPINE") was retired on 2026-11-25. It's archived in `Archive/` (see ADR 0003).

## Setting

The station sits on Kharon, a tidally locked desert moon of the ringed gas giant Tessaly, under a permanent sunset. The current playable space is **Atrium C, Hydroponics Deck**: a bright, dry, brutalist-futurist hall with a giant window onto the dunes, plus a south service wing with a firing range.

Lore hooks already in the world (from Ines at the information desk):
- The lab doors have been cycling on their own.
- The lab supervisor, Dr. Hale, hasn't checked in since Tuesday.
- Supply shuttles come up from an orbital ring. The Sector C-2 door at the end of the hall is sealed.

## What exists (systems ready to build gameplay on)

| System | Notes |
|---|---|
| First-person controller | Walk, sprint, jump, head bob, recoil, and hit shove |
| Weapons | Fists, bat, machete (two swing variants), pistol (magazine reload), and shotgun (shell-by-shell reload, pump). Headshot multiplier. |
| Pickups | Weapons need E. Ammo is collected by walking over it, and medkits too when hurt. |
| Targets | Hinged steel plates that react to hits and reset |
| NPC dialogue | Branching `DialogueTree` data, a talkable `Npc`, and a polished `DialogueUI` |
| Doors | `AutoDoor`: proximity sliding doors, which can be locked |
| Ambient life | Shuttle traffic outside, steam, dust, and light shafts |
| Shell | Title, pause, settings (General and Admin cheats), and the death screen |

## Controls

WASD move · Mouse look · LMB attack · E interact or pick up · R reload · 1 to 5 or scroll for weapons · F flashlight · Shift sprint · Space jump · Esc pause

## Open questions (for the planning session)

- What is the core loop? Exploration and mystery, combat, survival, or a mix?
- Who or what is the antagonist, given the lab doors and the missing supervisor?
- What is the scope of the first milestone (vertical slice)?
- How do NPCs and dialogue feed progression?
