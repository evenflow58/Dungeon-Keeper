# Dungeon-Keeper — Game Design Document

**Version:** 0.9 (Draft)
**Date:** October 2026
**Status:** Pre-production — open for revision

**Changelog**
- **0.9** — Added layout philosophy (§5.6): open caverns vs. guided hallways as equal first-class choices, with per-layout counters, trap taxonomy, and layout-blind scoring.
- **0.8** — Added frontstage/backstage zoning (§5.5): Keeper's Seal doors, delve-route overlay, breaches as emergency gameplay, Heart placement doctrine.
- **0.7** — Rescoped to a single-floor beta (§15): all systems on Floor 1, solo hero delves, champion-tier endgame; multi-floor, parties, and the raid move to post-beta.
- **0.6** — Locked product direction (§1.1): PC/Steam, hardcore sim audience, solo developer, passion project, emergent story, single-player 1.0.
- **0.5** — Added theming & coherence bonuses (§5.4): tag-based judging, tiered rewards, fusion combos; theme counters feed the Director.
- **0.4** — Added the Fun Doctrine (§7.4): heroes as players, Adventure Quality scoring, renown & traffic, house styles, recurring hero cast. The Director (§17.2) now optimizes for drama, not slaughter.
- **0.3** — Added multi-floor dungeon structure (§5.3): Floor 1 for single adventures, mid floors for higher-level/multi-party delves, Floor 4 as a 10-person raid with a player-designed floor boss.
- **0.2** — Added the Push & Pull design law (§2.1), AI-driven direction (§17), Unity technical direction (§18).
- **0.1** — Initial draft.

---

## 1. High Concept

A dungeon management sim fused with a colony base-builder. You are the Keeper: carve a fortress out of living rock, attract monsters, and keep them fed, rested, and loyal — with an *Oxygen Not Included*-style needs and systems simulation — while heroes of the overworld try to end you, *Dungeon Keeper*-style.

**One-line pitch:** *Oxygen Not Included* meets *Dungeon Keeper*: run a dungeon like a colony, defend it like a fortress.

**Fantasy:** You are a disembodied evil will bound to a Dungeon Heart. You never swing the sword yourself — you dig the chamber the sword is forged in, feed the smith who forges it, and make sure no hero ever reaches the Heart.

### 1.1 Product Direction

- **Platform: PC, Steam release.** Mouse-first UI with keyboard shortcuts for power users. No gamepad-first compromises, no mobile port on the roadmap.
- **Audience: hardcore management/colony-sim fans** — the ONI / Dwarf Fortress / RimWorld crowd. Expect a steep-but-fair learning curve and respect the player's intelligence: deep systems, full transparency (overlays for everything), tutorials that teach without handholding.
- **Team: single developer.** Scope discipline is a survival trait. Every system must be deep *and* implementable by one person — which means: data-driven over bespoke (tags, weights, tables — see §5.4), no feature that needs custom code without earning it, and mod support as a force multiplier so players can build what one dev can't.
- **Business model: passion project.** No monetization design, no live-service hooks, no F2P mechanics. Steam is for reach and the Workshop, not revenue optimization.
- **Story: minimal and emergent.** No authored campaign narrative — the player's dungeon *is* the story. The Mentor provides flavor and commentary, not plot. Scenario objectives give structure; the drama comes from the sim.
- **Multiplayer: single-player for 1.0, decided.** No netcode, no co-op, no versus. Revisit only if 1.0 finds its people.

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
| Themed decor & coherence | Efficiency, morale, hero dread, renown | Costs gold/labor; restricts object choice; the Director sends theme counters |
| Backstage sealing (Keeper's Seal doors, zoning) | Farms and Heart stay safe; heroes stay on the ride | Doors + space cost; imperfect seals leak; sappers exist |
| Open-area layout | Flanking, dynamic fights, exploration variety | Heroes bypass traps; flow hard to control; needs patrol coverage |
| Guided-hallway layout | Trap efficiency, predictable flow, easy theming | Sappers/AoE counter it; less dynamic; one breach unzips the line |

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

> **Post-beta.** The beta ships Floor 1 only — see §15. Everything below is the expansion roadmap, designed now so the beta's architecture doesn't block it.

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

### 5.4 Theming: Coherence Bonuses

**Principle:** a dungeon with a strong identity is more than the sum of its rooms. Fire, ice, nature, death, shadow, arcane — when a section commits to a theme, everything in it works better. Minions are inspired, traps hit harder, and heroes *feel* it (dread is a mechanic).

**How theming is judged** — tag-based coherence, fully data-driven:

1. **Everything placeable carries theme weights.** A magma vent is {fire: 3}; a frost rune {ice: 3}; a bone chandelier {death: 2, fire: 1}; moss carpeting {nature: 2}. Weights are designer-tuned data, not code — easy to balance, easy to mod.
2. **Each room, section, or floor declares a theme** — or the game auto-detects it from the dominant tag. Declared themes are a commitment; auto-detect is the casual path.
3. **Coherence score** = matching-theme weight ÷ total theme weight in the section, minus **dissonance** penalties for opposed pairs (fire×ice, holy×death).
4. **Tier thresholds** grant escalating bonuses:
   - *Themed* (60%+) — minor efficiency and morale boost.
   - *Immersive* (80%+) — strong boost; heroes suffer dread while inside.
   - *Paragon* (95%+) — legendary bonus plus renown; the Mentor names the section ("The Burning Deeps").
5. **Fusions reward mastery.** Deliberate opposed pairings unlock crossover effects instead of penalties when built intentionally: fire+ice = *steam vents* (concealment), nature+death = *rotbloom* (heals undead, poisons heroes), shadow+arcane = *whispering dark* (hero confusion). The judge recognizes specific pairs — a combo system hiding inside the penalty system, waiting for advanced players to find it.

**Bonuses cohere with everything else:**
- Minions with matching affinity (fire imps in fire rooms) gain loyalty and work speed.
- Theme-matched traps hit harder and cost less to rearm.
- Heroes take a dread penalty in Immersive+ sections (feeds the morale mechanic).
- Themed sections score heavily on Adventure Quality *variety* (§7.4) and generate renown — heroes tell stories about *places*, not corridors.

**The push:** themed decor costs gold and builder labor, and committing to a theme restricts what fits — every brazier in the ice palace is a choice. And the Director notices: it starts sending fire-warded heroes against your beloved Burning Deeps. Counter-play is the sincerest form of flattery.

**Starter theme set:** Fire, Ice, Nature, Death, Shadow, Arcane. (Holy exists only as a corruption target — desecrating it is the point.)

### 5.5 Frontstage and Backstage: Zoning the Dungeon

**The problem:** your mushroom farm feeds your orcs, but a hero with a sword turns it into a salad bar. Your Dungeon Heart ends the game if it breaks. And yet heroes need a path to the boss — a great adventure, not a locked-door simulator.

**The answer is zoning — think theme park.** Every dungeon has two layers:

- **Frontstage (the ride):** entrance → trap gauntlet → arenas → boss → (behind the boss) the Heart chamber. This is the adventure path. Heroes walk it, fight through it, and tell stories about it.
- **Backstage (the machine):** farms, lairs, workshops, treasury, barracks. Heroes should never see these rooms. Minions live here.

**How you enforce it:**
- **Keeper's Seal doors** (new door type, §8) — your minions pass freely; heroes cannot, period. Sappers and siege-class heroes can breach them, but slowly and loudly. You'll know.
- **Layout funneling** — corridors, chokepoints, and locked doors shape the delve route. Level design *is* the solution: you are literally designing the ride.
- **Delve-route overlay** (UI, §14) — the game predicts and shows the hero path from entrance to boss, so you design around it instead of guessing. If the overlay shows heroes walking through your farm, that's on you.

**Breaches are a feature, not a bug.** Seals aren't perfect and heroes aren't stupid:
- Scouts can spot service doors; sappers breach them; a panicked, fleeing hero might slip through a door your imp left propped open.
- A rogue loose in the mushroom farm is an *emergency*, not a failure: alarms sound, guards respond, imps panic. Drama — recoverable, unless you ignored every warning.
- The push/pull: perfect sealing costs doors, space, and labor. Every shortcut you take is a breach waiting to happen.

**The Dungeon Heart** sits in its chamber at the end of the ride, behind the boss arena — the final room of the dungeon, MMO-style. To even *see* the Heart, a hero must survive everything you built. If the Heart is destroyed, the game ends (§10) — so its approach is the most important level design in your dungeon: layered seals, guard posts, your best traps, and a boss who does not miss twice.

### 5.6 Layout Philosophy: Open Caverns vs. Guided Halls

**Both are first-class. You build what you want.**

Some Keepers carve vast open caverns where heroes roam, choose routes, and get swarmed. Others build tight guided hallways that force every hero down the same gauntlet. The game doesn't pick for you — it makes both work, and judges both fairly.

**Open-area dungeons**
- Big caverns, multiple routes, room to maneuver — for heroes *and* your minions.
- Defense is about **area control**: guard posts as patrol hubs, alarm totems, intercepting warbands, open-field traps (pit traps, gas clouds, rolling boulders).
- Strengths: dynamic fights, flanking, exploration variety (scores well on Adventure Quality *variety*).
- Weaknesses: heroes bypass traps and pick their fights; flow is hard to control.

**Guided-hallway dungeons**
- Linear corridors, chokepoints, forced paths — the classic gauntlet.
- Defense is about **holding the line**: spike strips, boulder chutes, kill-boxes, layered seals.
- Strengths: trap efficiency, predictable flow, easy per-section theming.
- Weaknesses: sappers, burrowers, and AoE punish linearity; less dynamic; a single breach unzips the whole line.

**The game supports both:**
- **Trap taxonomy** splits corridor types (spike strips, boulder chutes) and area types (pit traps, gas clouds) — neither layout starves.
- **Guard AI** adapts per guard post: station-and-hold in hallways, patrol-and-intercept in open ground.
- **The Director counters both**: sappers and AoE against hallway turtles; skirmishers and ranged heroes against open caverns.
- **The delve-route overlay** renders flow *fields* in open areas (likely paths as heat) and single lines in hallways.
- **Adventure Quality is layout-blind**: tension, variety, climax, and mercy score in either. Open dungeons earn their points on variety; guided dungeons earn them on tension.

**The meta is hybrid.** The best dungeons will mix both: a guided entrance gauntlet opening into a grand arena, a themed hallway feeding a boss chamber. Frontstage/backstage zoning (§5.5) works in either — backstage is backstage whether it's behind a sealed door or across a dark cavern.

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

**Beta scope:** solo delves only — one hero at a time, tiers 1–2, escalating to champion-tier solo heroes (named, brutal, single combatants) as the beta endgame. Parties, simultaneous multi-party delves, and the raid are post-beta (§15).

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
- *Variety* — traps triggered, rooms seen, mechanics experienced. Themed sections (§5.4) score heavily here — heroes remember *places*.
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

- **Spike pits, boulder traps, poison-gas vents** (plumb miasma into them!), **lava moats** (route forge heat!), **sentry idols** (mana-powered), **fear totems** (break hero morale — heroes have morale too). Traps split into corridor types (spike strips, boulder chutes) and area types (pit traps, gas clouds) — see §5.6.
- **Doors:** wooden → iron → magic-sealed. Doors buy time; time lets defenders arrive. **Keeper's Seal** doors are minion-only — heroes can't pass (sappers excepted, slowly and loudly). They're how the farm stays backstage (§5.5).
- **Trap maintenance:** traps need rearming by imps — a spent boulder trap is just decor. This ties defense back into the job-priority economy.
- **Kill-box design** is the endgame creative expression: the perfect gauntlet of gas, spikes, and warlocks is *art*. Stairs and shafts between floors are the premium trap real estate in the game — see §5.3.

---

## 9. Research & Progression

- **Library tech tree** branches: Architecture (bigger/better rooms), Traps, Dark Rituals, Creature Lore (attract advanced minions), Necromancy (late-game).
- **Dungeon Heart levels:** feed the Heart souls and gems to level it up. Each level unlocks new rooms and raises max mana — and, post-beta, new depth tiers (§5.3). Leveling the Heart spikes your infamy. The Heart is visible on the map; heroes can *feel* it.
- **Relics:** sealed chambers sometimes hold artifacts (the Crown of Teeth, the Bell That Hungers) with dungeon-wide effects and… side effects.

---

## 10. Win / Loss

- **Loss:** the Dungeon Heart is destroyed. Minions scatter, the dark goes quiet. (Classic.) The Heart waits behind the boss arena (§5.5) — anything that reaches it has survived your whole dungeon, and earned the attempt.
- **Beta:** no formal win condition — sandbox survival against escalating champion-tier heroes. The Heart's survival time and depth of development are the score.
- **Campaign win (post-beta):** complete scenario objectives — e.g., corrupt the kingdom's capital, slay the Hero Guild's Grandmaster, or survive and corrupt the 10-person raid on the deepest floor.
- **Sandbox:** endless escalation. Your score is how deep you got and how long the Heart kept beating.

---

## 11. Game Modes

- **Tutorial + Sandbox (beta)** — teach the systems, then turn the player loose on Floor 1 with endless escalating delves.
- **Campaign (post-beta)** — handcrafted scenarios teaching systems layer by layer, with objectives and the Mentor's commentary. No authored narrative: the dungeon is the story.
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
- **Delve-route overlay** — predicted hero path from entrance to boss (§5.5). Design the ride; don't guess it.

---

## 15. Scope

### Beta (initial launch)

One floor. Every system. The beta proves the sim and the fun on a single floor — "The Threshold" (§5.3) — before anything goes vertical.

**In:**
- Dig / claim / build; full single-floor room set (Lair, Hatchery/Tavern, Treasury, Library, Workshop, Temple, Torture Chamber, Guard Post, Training Room)
- 6 minion types; needs (food, rest, loyalty, faith); job priorities + schedules
- Utilities: ichor pipes, soul conduits, miasma + ventilation, heat routing
- Mushroom farming with the water/darkness/fertilizer loop
- Trap set with rearming labor and consumable ammo
- **Solo hero delves** — one hero at a time, utility-AI driven (scout, fight, flee, report); small class roster (fighter, rogue, cleric…); escalating to champion-tier solo heroes as the beta endgame
- Prisoners: capture, convert, sacrifice, ransom
- Fun Doctrine core: Adventure Quality scoring, renown & traffic, house styles (Gauntlet / Theater / Abattoir)
- Theming & coherence bonuses (§5.4)
- Dungeon Heart + loss condition; Heart levels gating unlocks within the floor
- Research tree (core branches)
- Mentor commentary (authored text lines)
- Pause/speed, alert system, full overlay suite
- Tutorial + sandbox endless

**Out (post-beta):** floors 2–4, simultaneous multi-party delves, the 10-person raid + floor boss designer, vertical logistics, campaign scenarios, possession mode, necromancy branch, relics, beetle ranching, generative Mentor layer, Workshop mod support.

### The solo-dev rule

If a feature can't be data-driven, it must justify its code cost. When in doubt, cut — the beta earns the right to grow.

### Explicit Non-Goals (for now)
- 3D graphics, mobile port, fully voiced Mentor.
- Multiplayer/netcode of any kind (single-player 1.0, decided).

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
6. ~~Multiplayer~~ — **single-player for 1.0, decided Oct 2026.**
7. Steam Workshop mod support at launch or post-launch? (Data-driven design in §5.4 makes this cheap — but solo-dev time is still solo-dev time.)

---

*This is a living document. When a decision gets made, update it here — future-you will be grateful.*
