# Dungeon-Keeper — Game Design Document

**Version:** 0.1 (Draft)
**Date:** October 2026
**Status:** Pre-production — open for revision

---

## 1. High Concept

A dungeon management sim fused with a colony base-builder. You are the Keeper: carve a fortress out of living rock, attract monsters, and keep them fed, rested, and loyal — with an *Oxygen Not Included*-style needs and systems simulation — while heroes of the overworld try to end you, *Dungeon Keeper*-style.

**One-line pitch:** *Oxygen Not Included* meets *Dungeon Keeper*: run a dungeon like a colony, defend it like a fortress.

**Fantasy:** You are a disembodied evil will bound to a Dungeon Heart. You never swing the sword yourself — you dig the chamber the sword is forged in, feed the smith who forges it, and make sure no hero ever reaches the Heart.

---

## 2. Design Pillars

1. **The dungeon is a living machine.** Rooms, tunnels, and utilities form interlocking systems. Every design choice — where the farm goes, how the soul grid is wired — has a simulation consequence.
2. **Monsters are people too (terrible people).** Minions have needs, moods, and breaking points. A starving troll is a liability, not an asset. Neglect is the real enemy.
3. **Greed invites ruin.** Digging deeper yields richer veins — gold, gems, ancient relics — but releases miasma, wakes buried things, and draws stronger heroes. Every expansion is a gamble.
4. **Every defense tells a story.** Traps, choke points, kill-boxes, and ambushes are the player's creative expression. No two dungeons fall the same way.

---

## 3. Core Loop

```
Dig → Claim & build rooms → Attract minions → Sustain their needs
  → Research & fortify → Survive hero raids → Spend spoils → Dig deeper (repeat, harder)
```

**Moment-to-moment:** Prioritize jobs, respond to alerts (miasma leak, starving minions, heroes at the gate), redesign on the fly.
**Session arc:** Each depth tier unlocks new resources, rooms, creatures — and new threats.
**Long arc:** Grow from a dirt hovel with three goblins to a multi-level underworld empire.

---

## 4. Minions

### 4.1 Starter Cast

| Minion | Role | Quirk |
|---|---|---|
| Imp | Digs, builds, hauls, rearms traps | Bound spirit: never sleeps, never eats, but works slowly and dies to a stiff breeze |
| Goblin | Fighter / general labor | Cowardly: flees when loyalty drops or when losing a fight |
| Orc | Heavy fighter, hauls | Huge appetite; gets violent when hungry (attacks *anyone* nearby) |
| Dark Cultist | Researches, prays at the Temple | Generates mana through prayer; useless in a fight |
| Troll | Siege-breaker, slow labor | Regenerates in darkness; eats 3× a normal minion |
| Warlock | Ranged magic | Demands a Library and private lair or loyalty tanks |

### 4.2 Needs (the ONI half, evil-flavored)

Each minion tracks:

- **Food** — Eaten at the Hatchery or Tavern. Diets differ (orcs want meat, cultists eat anything, trolls eat *everything*). Starving minions lose loyalty fast and may eat each other. Yes, really.
- **Rest** — Recovered in the Lair. Lair quality (size, quiet, darkness, decor) sets recovery rate. Exhausted minions work slowly and fight badly.
- **Loyalty** (the Morale/Stress analog) — Rises with good food, victories, paydays from the Treasury, and dark rituals. Falls with hunger, exhaustion, witnessing allies die, and losing battles. Low-loyalty minions desert, steal from the Treasury, brawl with each other — or open your doors to heroes.
- **Faith/Dread** (cultists & warlocks) — Powered by the Temple and proximity to the Dungeon Heart. High faith generates mana; starved faith turns cultists into liabilities.

### 4.3 Jobs & Priorities

ONI-style priority system, adapted:

- Job types: Dig, Build, Haul, Farm, Research, Guard, Pray, Torture, Rearm Traps, Train.
- Every minion has per-job priorities (1–9). Global job board; minions claim the highest-priority task they can perform.
- **Training:** minions assigned to the Training Room gain levels over time — but a training orc isn't guarding the gate. Opportunity cost is the game.
- **Schedules:** ONI-style timetable blocks (Work / Rest / Feast / Pray) per minion or per species. A well-fed, well-rested warlock one-shots a knight. A tired one gets one-shot.

---

## 5. Dungeon Construction

### 5.1 Digging & Claiming

- Imps dig through dirt, rock, gold veins, gem seams, and **sealed chambers** (risk/reward: treasure… or worse).
- Dug tiles must be **claimed** to become dungeon; unclaimed rock slows movement and can't hold rooms.
- Deeper = richer = more dangerous. Depth tiers gate resources and threats.

### 5.2 Rooms

| Room | Purpose | Notes |
|---|---|---|
| Lair | Sleep & recovery | Quality matters: dark, quiet, spacious |
| Hatchery / Tavern | Food production & consumption | Mushroom farms need water + darkness; meat comes from… heroes |
| Treasury | Pays minions, stores gold | Unpaid minions lose loyalty; heroes love looting it |
| Library | Research | Warlocks required; bigger = faster |
| Workshop | Forges weapons, builds traps | Generates heat; needs ventilation |
| Temple | Prayer → mana | Adjacency to Heart boosts output |
| Torture Chamber | Converts captured heroes | Slow, loud (noise lowers nearby Lair quality), incredibly fun |
| Guard Post | Staging for defenders | Minions stationed here respond to breaches first |
| Training Room | Levels up fighters | Chickens optional but encouraged |

**Room efficiency** depends on size, shape, adjacency bonuses (Temple near Heart, Workshop near Treasury), and decor — evil banners, bone chandeliers, flayed-hero tapestries raise minion morale and *dread* intruders.

---

## 6. Simulation Systems (the ONI half)

### 6.1 Resources

- **Gold** — dug from veins; pays minions, funds construction, bribes.
- **Stone & Timber** — construction basics.
- **Food** — mushrooms (farmed), meat (ranched beetles… or fallen heroes).
- **Mana** — generated by prayer and sacrifice; powers rituals, warlocks, and late-game rooms.
- **Souls** — harvested from slain heroes in the Temple; currency for Heart upgrades and dark rituals.

### 6.2 Utilities

- **Ichor pipes** — the plumbing system. Carry water (farms, forges) and pumped miasma. Leaks flood rooms; flooded libraries are *very* sad.
- **Soul conduits** — the power grid. Distribute mana from the Heart/Temple to consumers (warlock towers, ritual circles, powered traps). Overload a conduit and it bursts — spectacularly.
- **Ventilation & miasma** — deep digging releases miasma pockets. Unvented miasma sickens minions and withers farms. Vents, gas pumps, and sealed doors manage it — or weaponize it (see: gas traps).
- **Heat** — forges and smelters heat nearby rooms. Trolls love it, mushrooms hate it, heroes in plate armor *really* hate it. Route heat deliberately.

### 6.3 Farming & Ranching

- **Mushroom farms:** need water, darkness, and fertilizer (guano, bone meal — the circle of dungeon life).
- **Beetle ranching:** docile giant beetles produce meat and shells (armor material). They need feeding and space or they eat your stores.
- **Prisoner labor:** captured heroes can work the mines… until they escape. Risk/reward.

---

## 7. Heroes & Invasions (the DK half)

### 7.1 Infamy & the Threat Meter

Everything you do is *noticed*: gold hoarded, heroes slain, depth dug, villagers "recruited." Infamy fills the Threat Meter, which triggers raids. **You choose the pace of your own doom** — expand recklessly and the kingdom sends its best.

### 7.2 Hero Parties (escalating tiers)

1. **Peasants with pitchforks** — tutorial-tier. Mostly a food delivery service.
2. **Militia & archers** — probe your defenses, learn your layout.
3. **Knights, clerics, wizards** — coordinated parties with roles; clerics heal, wizards blast doors.
4. **Legendary heroes** — named, unique, devastating. Each has a quirk (the Dragonslayer ignores your dragon; the Saint inspires nearby allies).

Heroes pathfind toward the Dungeon Heart, looting the Treasury and freeing prisoners along the way. Wounded heroes retreat — and *report back*, making the next raid smarter.

### 7.3 Prisoners

Knock heroes out (torture chamber, knockout gas) instead of killing them:

- **Convert** them to your cause (slow, glorious),
- **Sacrifice** them for mana and souls,
- **Ransom** them back for gold,
- or put them to work in the mines and hope the guards stay awake.

---

## 8. Traps & Defenses

- **Spike pits, boulder traps, poison-gas vents** (plumb miasma into them!), **lava moats** (route forge heat!), **sentry idols** (mana-powered), **fear totems** (break hero morale — heroes have morale too).
- **Doors:** wooden → iron → magic-sealed. Doors buy time; time lets defenders arrive.
- **Trap maintenance:** traps need rearming by imps — a spent boulder trap is just decor. This ties defense back into the job-priority economy.
- **Kill-box design** is the endgame creative expression: the perfect gauntlet of gas, spikes, and warlocks is *art*.

---

## 9. Research & Progression

- **Library tech tree** branches: Architecture (bigger/better rooms), Traps, Dark Rituals, Creature Lore (attract advanced minions), Necromancy (late-game).
- **Dungeon Heart levels:** feed the Heart souls and gems to level it up. Each level unlocks a depth tier, new rooms, and a chunk of max mana — and makes your infamy spike. The Heart is visible on the map; heroes can *feel* it.
- **Relics:** sealed chambers sometimes hold artifacts (the Crown of Teeth, the Bell That Hungers) with dungeon-wide effects and… side effects.

---

## 10. Win / Loss

- **Loss:** the Dungeon Heart is destroyed. Minions scatter, the dark goes quiet. (Classic.)
- **Campaign win:** complete scenario objectives — e.g., corrupt the kingdom's capital, slay the Hero Guild's Grandmaster.
- **Sandbox:** endless escalation. Your score is how deep you got and how long the Heart kept beating.

---

## 11. Game Modes

- **Campaign** — handcrafted scenarios teaching systems layer by layer, with story beats and the Mentor's commentary.
- **Sandbox** — full systems, endless, configurable difficulty and threat pacing.
- *(Stretch)* **Possession mode** — classic DK: jump into a minion and fight first-hand.

---

## 12. The Mentor (Tone & Narrative)

The dungeon needs a voice: a sarcastic, ancient narrator in the spirit of the original — delighted by your cruelty, disappointed by your incompetence. *"Your imps have discovered gold. Pity they've also discovered the heroes guarding it."* Tone: darkly comedic evil, never grimdark.

---

## 13. Art & Audio Direction

- **Look:** 2D tile-based with rich dynamic lighting — pools of torchlight, creeping gloom, glowing lava and soul conduits. Juicy, readable, dark-whimsical.
- **UI:** ONI-style overlay toggles (plumbing, soul grid, heat, loyalty, threat) — the dungeon's nervous system made visible.
- **Audio:** the Heart's slow heartbeat as ambient bass; it quickens when heroes breach. That's the whole horror movie in one sound cue.

---

## 14. UI/UX Essentials

- **Pause + speed controls** (pause / 1× / 2× / 3×) — non-negotiable for a sim.
- **Priority screen** (ONI-style) for job management.
- **Alert system** with camera jump: miasma leak, starving minions, breached door, Heart under attack.
- **Threat meter** always visible — the sword of Damocles should hang in plain sight.

---

## 15. Scope

### MVP (vertical slice → early access)
- Dig / claim / build; 6 minion types; needs (food, rest, loyalty); 8 rooms; job priorities + schedules; ichor + soul conduit utilities; miasma & heat; mushroom farming; 5 trap types; 3 hero tiers; prisoners (convert/sacrifice); Dungeon Heart + loss condition; sandbox endless mode; pause/speed; Mentor voice (text).

### Post-MVP
- Campaign scenarios, possession mode, necromancy branch, relics, legendary heroes, beetle ranching, multiplayer (co-op dungeon?), mod support.

### Explicit Non-Goals (for now)
- 3D graphics, mobile port, fully voiced Mentor.

---

## 16. Open Questions

1. Engine: Godot vs Unity? (Godot keeps it lean and open.)
2. Real-time with pause vs tick-based sim?
3. How smart should hero AI be — scripted parties or dynamic planners?
4. Multiplayer: shared dungeon co-op, or versus (one player raids as heroes)?
5. Art pipeline: hand-drawn tiles vs procedural decoration?

---

*This is a living document. When a decision gets made, update it here — future-you will be grateful.*
