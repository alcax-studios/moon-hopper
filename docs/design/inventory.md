# Inventory, items, and the game menu

Status: first pass (2026-11-26). Content (items, quests, codex text) is placeholder until the lore is written.

## Items

Every item is an `ItemDefinition` (written by `ItemAssets`): id, name, description, category, stack size, a visual-only world model, and what it does.

| Category | Examples | Use |
|---|---|---|
| Weapon | pistol, shotgun, rifle, machete, bat | In hand from the hotbar. Each gun keeps its own magazine count (`ItemStack.loaded`), so a dropped gun keeps its rounds. |
| Ammo | light rounds, shells, rifle rounds | Stacks (120, 48, 150). Reloads take rounds from the inventory. |
| Aid | bandage (+20, 1.5 s), medkit (+55, 3.5 s) | Hold the attack button with it in hand, or Use in the menu (instant). |
| Food | ration bar, coffee | Same as aid, smaller heals. |
| Wearable | helmet, cap, armored vest, work jacket, cargo pants, boots, gloves | Head, chest, pants, shoes, or gloves slot. Armor points reduce damage by 100 / (100 + armor); head armor only counts against headshots. |
| Misc | coffee mug, keycard, data pad | Carried; nothing yet. |

You start wearing the work jacket, cargo pants, and boots, and carrying 2 bandages.

## Inventory

- **Backpack:** 6 x 5 slots. **Hotbar:** 8 slots, keys 1 to 8 and the mouse wheel; the selected slot is in your hands (fists, hidden, when it isn't a weapon). **Body:** head, chest, pants, shoes, gloves.
- New weapons go to the hotbar, and into your hands when you aren't holding a weapon. A wearable goes straight onto an empty body slot. Everything else goes to the backpack.
- Stacks merge first. Moving onto a different item swaps (when both slots accept the other).

## The game menu (Tab or I)

The world keeps running (`GameState.Menu`: the cursor frees up and the player stops). Tab, I, or Esc closes it; Q and E switch tabs.

- **Inventory:** drag to move or swap; drop outside the window to drop the item on the floor. Click or right-click for the item menu: Use, Wear or Take off, To hotbar or To backpack, Hold, Split, Drop one or Drop all, Details. Shift+click quick-moves; double-click uses or wears. On the right: the character (a mannequin in what you wear; drag to turn it), the body slots, armor totals, and an info box for the hovered item.
- **Quests:** active and completed quests and their objectives. `QuestLog.Progress(key)` advances objectives. The station starts you on *Orientation*: talk to someone, get a weapon, put on armor, use aid. The HUD tracks the active quest under the objective line.
- **Codex:** Station, People, Factions, Items. Entries unlock when you pick up an item, talk to someone, or first see a faction's unit up close.
- **Status:** health, armor, what's in hand, what you wear, the station's view of you (clear, alert, wanted), and session numbers.

## In the world

- **World items** (`WorldItem`): physical; tap E to take, hold E to drag. Placed by the Atrium builder: mugs on food court tables, coffee and ration bars at the stalls, bandages in the restrooms, a helmet, a vest, and gloves on the armory shelf, and a data pad, keycard, mug, and cap in room 1202.
- **Nothing is picked up automatically:** ammo, weapons, and medkits all take E.
- **The grab cue:** the crosshair shows four corner brackets when something movable is under it (white), and holds them tighter in amber while you carry it. Props have no text prompt; world items only show "[E] TAKE ...". Grabbing makes no sound.
- **Props** (`Grabbable`): crates, a cargo crate, folding chairs, bins. Hold E to pick one up by the spot you look at; release E to drop it; click to throw it. Anything over 25 kg (the cargo crate) and bodies are dragged along the floor instead of carried. Props carve the NavMesh where they come to rest.
- **Bodies:** every ragdoll bone is grabbable.

## Money

- Credits (CR) are an item: **Credits**, category Currency, one stack with no limit. Your balance is the credits you carry. You start with a stack of 150. Selling with no cash makes a new stack appear; more cash adds to it; spending it all removes it. If there's no room for a new stack, the cash drops at your feet.
- Containers and bodies hold their cash as a Credits stack you can see, drag, or Take All. Taking cash from an owner is theft (reported if seen), but the bills aren't marked stolen. Traders don't buy credits.
- Every item has a value (ItemAssets sets the prices).

## Looting

- **Containers** (`LootContainer`): supply crates, lockers, stall registers (mostly cash), the room 1202 fridge, and the Guest Services lost and found. Contents roll from a table in `LootTables` the first time they appear.
- **People carry things.** Every guard, insurgent, special ops unit, and civilian rolls a loot table at spawn (by faction, or "civilian"): ammo, aid, food, sometimes armor, a keycard, a data pad, and some credits. Their body is searchable once they're dead. Their gun drops on the floor as a takable item, with whatever was left in its magazine.
- **The loot window** (tap E on a container or a body; holding E on a body still drags it): the container on the left, your backpack and hotbar on the right. Double-click or drag moves items either way; **Take All (R)** takes everything plus the cash. E, Tab, or Esc close it. Right-click any item for the same kind of menu as the inventory (Take or Steal, Take and use, Take and wear, Put in, Use, Wear, To hotbar, Details); in the trade window it offers Buy or Sell 1 or the stack, with prices.

## Trading

- Traders: Bayo (Armory: weapons, ammo, armor, aid), Mei Lin (Noodle Bar), Diego Vargas (Grill 03), and Noor Rahimi (Orbit Coffee). Talk to them and pick "Let's trade."
- They sell at value x markup (1.1 to 1.2). They buy what they deal in at value x buy rate (0.4 to 0.5), only up to their budget (Armory 600 to 1100, food stalls 50 to 140, rolled per visit). The budget grows when you buy from them.
- Click an item to see its price, then Buy or Sell 1 or the whole stack. Double-click buys or sells one (Shift: the stack). Dragging across trades the stack.

## Ownership and theft

- **Stealable** things belong to someone or are **station property** (`stationProperty`: nobody's in particular, still a crime). Either way, anyone who sees you take one reports it. Containers open normally ("OPEN REGISTER"); what's inside shows a red STEAL tag, and Take All becomes Steal All. World items show a red "[E] STEAL ...". The UI stays plain: no owner names, no watcher hints, no explanatory captions.
- Station property in the Atrium: the staff locker and the first-aid kit on the info desk.
- Stealable items, by owner: In the Atrium: the coffee, rations, and noodles on the stall counters (their stall's), the armory shelf gear, the armory and staff lockers, the registers, and Guest Services' data pad, coffee, and lost and found by the info desk.
- **Unseen**, a stolen item is just marked STOLEN. Keep it, use it, eat it.
- **Seen** by anyone within 14 m who faces you with a clear view: the nearest calm guard walks up and asks for a fine (what you took, plus 50). Pay it (the stolen goods are confiscated; if you can't afford it, you hand over all your credits), or refuse and fight. Walking away after being asked, or stalling for a minute, counts as refusing.
- **Selling stolen goods** to these traders is an instant crime: security turns on you, and the quartermaster arms up.

## Survival (`PlayerVitals`)

- **Stamina:** sprinting drains 16/s, and a jump costs 10. It recovers 16/s after a 1 s pause (faster when standing still or crouched). Running dry locks out sprinting until it's back to 30%. It's the thin bar under health.
- **Needs:** food, water, and energy drain to empty over about 40, 25, and 60 minutes (faster while sprinting). Below 25 each one slows stamina recovery, and low energy cuts max stamina to 60. At zero food or water you lose 1 health every 2 s (down to 5). Three small bars under the stamina bar show them, with a warning (HUNGRY, THIRSTY, TIRED...).
- **Consumables:** ration (food 30), noodles (food 50, water 10), wrap (food 45), water bottle (water 45), coffee (water 15, energy 20, Caffeinated), Charge (water 15, energy 15, Energized). Sinks (the room 1202 kitchen and bathroom) give water 25, free.
- **Effects:**

| Effect | Lasts | Does |
|---|---|---|
| Energized | 12 s | No stamina drain, then a Crash |
| Crash | 3 s | Stamina recovers at 40% |
| Caffeinated | 45 s | Stamina recovers 25% faster |
| Well rested | 180 s | +20 max stamina, recovers 20% faster |

- **Sleep:** E on the room 1202 bed and pick one of the next two times of day (from Day: Dusk or Night). The screen fades, the time changes, and you wake rested: energy 100, a bit hungrier and thirstier, +30 health, debuffs cleared, Well rested.
- **Admin (Player section):** Infinite stamina, Needs don't drain, Fill needs and stamina.

## Furniture

- The room 1202 fridge has a real door (E opens or closes it). The items inside are physical (water, Charge, noodles, a wrap, coffee): you pick them up by hand, and only while it's open.
- Kitchen cabinets (5 lower and 5 upper units, each with its own doors and handles), the island drawers, the nightstands, the bathroom vanity, and the armory's medical cabinet (station property) are containers.

## Time of day

- Day, Dusk, and Night, picked in the admin console (World section) for now. The Atrium is baked once per preset (`AtriumLighting`, three bakes). `TimeOfDay` swaps the lightmaps, light probes, ambient, and reflections along with the sun, sky, fog, exposure, and window light shafts. Far scenery uses the ambient instead of the probes by the glass. Maps without baked presets (the AI Playground) dim the sun and ambient instead.

## Extending

- A new item: add it in `ItemAssets` and rebuild the items. New categories go in `ItemCategory`; `ItemDefinition.Consumable` and `Wearable` drive the menu actions.
- A new quest: `QuestLog.Add(new Quest{...}.Objective(text, key))`, then call `QuestLog.Progress(key)` where it happens.
- A new loot table: a line in `LootTables`. A container in a level: `LootContainer.Configure(title, table, owner)`.
- A new trader: add `Trader` to an NPC, with a dialogue choice leading to `@trade`.
- A new menu tab: a page `RectTransform` and a panel component in `GameMenu.Build`.
