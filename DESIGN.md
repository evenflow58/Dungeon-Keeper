# Dungeon-Keeper — Game Design Document

**Version:** 0.4 (Draft)
**Date:** October 2026
**Status:** Pre-production — open for revision

**Changelog**
- **0.4** — Added the Fun Doctrine (§7.4): heroes as players, Adventure Quality scoring, renown & traffic, house styles, recurring hero cast. The Director (§17.2) now optimizes for drama, not slaughter.
- **0.3** — Added multi-floor dungeon structure (§5.3): Floor 1 for single adventures, mid floors for higher-level/multi-party delves, Floor 4 as a 10-person raid with a player-designed floor boss.
- **0.2** — Added the Push & Pull design law (§2.1), AI-driven direction (§17), Unity technical direction (§18).
- **0.1** — Initial draft.

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
5. **Nothing is free.** Every system has a push and a pull — see §2.1. If it doesn't ask for something back, it's unfinished.
6. **Heroes are players, not targets.** Design for their fun — drama, close calls, memorable bosses — not just their deaths. A dungeon nobody survives is a dungeon nobody visits. (See §7.4.)

### 2.1 The First Law: Every System Has a Push and a Pull

Nothing in the dungeon is free. Every benefit (**pull**) demands ongoing upkeep (**push**): worker time, a consumable resource, space, or risk. This is the *Oxygen Not Included* half of the game's soul — the dungeon is a machine you must keep feeding, and neglect is the real enemy.

Resources fall into two classes:

- **Stocks (non-renewable):** gold veins, gem seams, aquifers, sealed relics. They deplete. Expansion is a treadmill — you keep digging because the old veins run dry.
- **Flows (renewable at a labor cost):** food, mana, souls, clean air. They never run out on their own, but every unit costs minion-hours — and minion-hours are the scarcest resource in the game.

The core tension: convert stocks into flows before the stocks run out — while heroes arrive on a schedule set by your infamy, not your readiness.

| System | Pull (benefit) | Push (upkeep / cost) |
|---|---|---|
| Dungeon Heart | Powers conduits, unlocks depth tiers, radiates dread | Must be fed souls regularly; a starved Heart causes conduit brownouts and minion panic |
| Soul conduits | Deliver mana anywhere in the dungeon | Mana must be generated (prayer time, sacrifices); conduits leak and decay — imps must maintain them; overloads burst spectacularly |
| Prayer (cultists) | Steady, safe mana income | Consumes minion work-hours; cultists need food, rest, and faith upkeep |
| Ichor / water | Farms, forges, traps | Aquifers drain (stock!); pumps need tending; pipes leak and flood rooms |
| Mushroom farms | Renewable food | Need water + darkness + fertilizer + farmer labor; blight risk where miasma seeps |
| Meat / ranching | High-quality food (big morale) | Beetles eat and need space; heroes-as-livestock need guards and food |
| Gold veins | Wealth for pay, building, research | Non-renewable; deeper veins mean miasma, heat, and threat; hauling eats labor |
| Minions | Labor + defense | Every mouth eats, sleeps, and wants paying; crowding tanks loyalty |
| Traps | Kill heroes cheaply | Rearming is imp labor; ammo is consumable (boulders, gas canisters) |
| Prisoners | Labor, conversion, sacrifice | Need guards and food; escape risk; torture noise disturbs nearby lairs |
| Research | Unlocks everything late-game | Researcher time; libraries need quiet and dark; advanced tiers demand relics |
| Digging deeper | Richer veins, relics, space | Miasma pockets, heat, longer haul routes, stronger heroes, sealed horrors |
| Miasma | *Weaponizable* — plumb it into gas traps; plague-imps thrive in it | Sickens minions, withers farms; venting is ongoing labor |
| Heat | Faster forges, lava moats | Cooks mushroom farms, exhausts minions; must be vented or deliberately routed |

**Push/pull inversions:** the best moments come when a push becomes a pull through clever engineering — miasma routed into gas traps, forge heat feeding a lava moat, prisoners working the mines. Reward players who turn costs into weapons. Design systems so these inversions are *discoverable*, not documented.

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

### 5.3 The Deep Delve: Multi-Floor Structure

The dungeon is vertical. Floors are discrete levels joined by stairs, shafts, and guarded chokepoints — each with its own character, economy, and threat profile. Digging down isn't just "more dungeon"; it's founding a new colony level, with its own upkeep. Depth multiplies everything: wealth, danger, and the grocery bill.

**Floor 1 — The Threshold** (tutorial & early game)
- Tuned for a **single adventure at a time**: solo adventurers and small parties.
- Holds the essentials: entrance, trap gauntlet, guard post, basic lair / hatchery / treasury.
- Hero tiers 1–2. Losing ground here stings; it isn't fatal.

**Floors 2–3 — The Deeps** (mid game)
- Tuned for **higher-level parties and multiple simultaneous adventures** — two or three hero parties can be inside the dungeon at once, on different floors, and each one needs an answer.
- Themed per floor: e.g., Floor 2 — fungal farms and miasma works; Floor 3 — magma forges and lava moats.
- Demands **floor-local infrastructure**: hauling food and stone down three floors is ruinous. Build local farms, lairs, and workshops on each floor — or engineer vertical logistics (dumbwaiter shafts, imp relay stations), each with its own costs. The push/pull, now vertical.

**Floor 4 — The Throne Below** (endgame)
- Tuned for the **10-person raid**: a full MMO-style raid party — tanks, healers, DPS — with raid mechanics: phases, adds, enrage timers.
- The player designs a **floor boss**: promote a veteran minion to Boss, build its arena, choose its mechanics (summon adds, lava phases, fear auras). The boss is the centerpiece of the floor — feed it, gear it, give it a dramatic entrance.
- The Dungeon Heart beats here. The raid's objective: kill the boss, smash the Heart.

**Verticality systems**
- **Chokepoints:** stairs and shafts are the best trap real estate in the game. A shaft lined with gas vents and a boulder at the top is a strategy, not a hallway.
- **Logistics:** imp hauling slows with depth. Answer with local production or infrastructure — both cost.
- **No skipping:** heroes must fight floor by floor; they can't tunnel straight to the Heart. Defense in depth, literally.
- **Rising hazards:** miasma and heat rise. Deep floors run hotter and more toxic by default; ventilation must be engineered *upward* — the deeper you go, the harder the air is to breathe.
- **Pacing:** Heart levels gate floor unlocks (§9). Each new floor is a soft reset of the colony loop — new space, new resources, new upkeep — while hero pressure never stops scaling.

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

Hero tiers map to dungeon floors (§5.3): tiers 1–2 haunt Floor 1, tier 3 pushes into Floors 2–3 (often several parties at once), and the endgame is the 10-person raid descending on Floor 4.

### 7.3 Prisoners

Knock heroes out (torture chamber, knockout gas) instead of killing them:

- **Convert** them to your cause (slow, glorious),
- **Sacrifice** them for mana and souls,
- **Ransom** them back for gold,
- or put them to work in the mines and hope the guards stay awake.

### 7.4 The Fun Doctrine: Design for Delight, Not Just Death

Here's the twist that makes the game: **the heroes are players, and you're their game master.** The goal isn't to kill every hero that shows up — it's to give them a great dungeon. Like an MMO dungeon, the best runs are challenging, dramatic, and memorable — not a meat grinder at the entrance. Some heroes will die, and that's fine. It might even be exactly what you want. But a dungeon that kills everyone at the door is a *failed* dungeon: no stories, no returning challengers, no legend.

**Adventure Quality.** The Director scores every delve on drama, not body count:
- *Tension & release* — close fights, HP swings, narrow escapes.
- *Variety* — traps triggered, rooms seen, mechanics experienced.
- *Climax* — the boss fight should be the story's peak, win or lose.
- *Mercy* — did the party get a chance to retreat with their lives?

**Renown & traffic (the push/pull).** Surviving heroes spread tales. A dungeon famous as *fair but deadly* draws ambitious, well-geared parties — more loot, more souls, more glorious fights. A pure slaughterhouse earns dread: traffic dries up, and whoever still comes is desperate, undergeared, or suicidal. Killing everyone is profitable tonight and starving next month. You manage your reputation the way you manage your gold.

**House style.** Set per-floor intent and the dungeon behaves accordingly:
- *Gauntlet* — balanced challenge. Minions fight to win; traps wound and kill in fair measure.
- *Theater* — dramatic and survivable. Traps maim more than kill, minions accept surrenders, bosses monologue. Built for stories.
- *Abattoir* — maximum lethality. For when you need souls *now*. Your renown will pay for it.

**Recurring cast.** Heroes who survive level up — and remember. The fighter you spared on Floor 1 returns leading the raid on Floor 4 with a counter for your favorite trap. Mercy is a long-term strategy with a short-term cost, which is exactly the kind of decision this game is about.

**Mercy mechanics:** ransoming or releasing captives boosts renown; sacrificing them yields souls now at a renown cost. The Torture Chamber isn't just evil — it's *marketing spend*.

---

## 8. Traps & Defenses

- **Spike pits, boulder traps, poison-gas vents** (plumb miasma into them!), **lava moats** (route forge heat!), **sentry idols** (mana-powered), **fear totems** (break hero morale — heroes have morale too).
- **Doors:** wooden → iron → magic-sealed. Doors buy time; time lets defenders arrive.
- **Trap maintenance:** traps need rearming by imps — a spent boulder trap is just decor. This ties defense back into the job-priority economy.
- **Kill-box design** is the endgame creative expression: the perfect gauntlet of gas, spikes, and warlocks is *art*. Stairs and shafts between floors are the premium trap real estate in the game — see §5.3.

---

## 9. Research & Progression

- **Library tech tree** branches: Architecture (bigger/better rooms), Traps, Dark Rituals, Creature Lore (attract advanced minions), Necromancy (late-game).
- **Dungeon Heart levels:** feed the Heart souls and gems to level it up. Each level unlocks a depth tier, new rooms, and a chunk of max mana — and makes your infamy spike. The Heart is visible on the map; heroes can *feel* it.
- **Relics:** sealed chambers sometimes hold artifacts (the Crown of Teeth, the Bell That Hungers) with dungeon-wide effects and… side effects.

---

## 10. Win / Loss

- **Loss:** the Dungeon Heart is destroyed. Minions scatter, the dark goes quiet. (Classic.)
- **Campaign win:** complete scenario objectives — e.g., corrupt the kingdom's capital, slay the Hero Guild's Grandmaster, or survive and corrupt the 10-person raid on the deepest floor.
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

## 17. AI-Driven Direction

"AI-driven" means three layers, in priority order. The first two ship with the game; the third is a stretch goal.

### 17.1 Simulation AI — the living dungeon (core)

Minions and heroes run on **utility-based AI**: every agent continuously scores its needs, fears, and opportunities, then acts on the highest-scoring option. No scripts — a hungry orc standing between a bakery and a battlefield *chooses*, and you live with its choice.

- **Minions:** needs (food, rest, loyalty, faith) + the job board + personality weights (brave/cowardly, gluttonous, devout, lazy). Behavior should be legible — the player can see *why* the troll is eating the beetles (because it's starving, and you knew that).
- **Heroes:** role-based party planners. Scouts probe defenses and retreat; assault parties exploit what scouts learned (unmanned traps get disarmed, weak flanks get hit); wounded heroes flee and *report back*, making the next raid smarter.

### 17.2 The AI Director — adaptive opposition

The Threat Meter is a **director**, not a timer — *Left 4 Dead*-style. It reads live sim state (troop composition, trap coverage, which systems you're neglecting) and composes raids to exploit gaps, pacing tension and release:

- Turtled behind spike traps? It sends sappers and burrowers.
- Starved your warlocks? It sends wizards.
- After a crushing player defeat, it eases off — dramatic, not punishing.

The director is the purest expression of push & pull: whatever you underfeed becomes the thing that kills you.

The Director's objective function is **drama, not slaughter**. It composes raids to maximize Adventure Quality (§7.4): parties scaled for close fights, pacing tuned for tension and release, and renown effects that reward dungeons heroes *enjoy* dying in. A total party kill in the entrance corridor is scored as a failure — boring for everyone, including you.

### 17.3 Generative layer — the dungeon that talks back (stretch)

- **The Mentor, generated:** contextual narration of live events in the Mentor's voice — sacrifices, breaches, triumphs, and especially your incompetence. Ships with an authored line bank as fallback; generated lines pass a tone filter.
- **Procedural identities:** heroes with names, titles, and grudges — *"Sir Aldric of the Vale — his brother was sacrificed in your Temple."* The overworld issues bounties and ultimatums that react to what you've actually done.
- **Hard constraint:** the game runs fully offline. Every generative feature ships with a deterministic fallback. No cloud dependency in the critical path.

## 18. Technical Direction

- **Engine: Unity (decided).** C# across the board.
- **World representation:** Unity Tilemaps for the dig/build grid; 2D lights plus a custom gloom pass for the light/darkness interplay.
- **Simulation:** fixed-timestep sim decoupled from rendering — required for pause/speed controls. Deterministic tick for save/load and replays.
- **Scale planning:** if agent counts strain per-frame updates, migrate hot paths to DOTS/ECS. Design systems data-oriented from the start to keep that door open.
- **AI stack:** authored utility AI + the director ship with the game. Unity ML-Agents is reserved as an R&D experiment (e.g., training raider parties against player dungeons) — never a dependency.
- **Saves:** versioned JSON snapshots of sim state; keep them human-inspectable for debugging.

## 19. Open Questions

1. ~~Engine~~ — **Unity, decided Oct 2026.**
2. Sim granularity: individual needs per minion from day one, or abstracted crowds early with per-agent detail unlocked by scale?
3. Hero AI: utility planners vs GOAP — prototype both before committing?
4. Generative Mentor: is any cloud dependency acceptable, or local-only models?
5. ML-Agents: worth the training overhead, or do authored systems carry the AI fantasy on their own?
6. Multiplayer: co-op dungeon vs hero-raider versus — or neither for 1.0?

---

*This is a living document. When a decision gets made, update it here — future-you will be grateful.*
