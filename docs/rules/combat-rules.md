# Combat rules

The rules by which the game turns weapon fire into hits, shield loss, crew loss, system damage, and
destruction (ADR 0004). They cover beam weapons (`classLabel` `cannon`), torpedo launchers (`launcher`),
shields, ship systems, crew, derelicts, repair, the transport and boarding of units, and the special-weapon order
with the Holding Beam's hold and boarding. Other special weapons, recrew, AI transports, and the vision and counts
of a captured starbase are not covered.

The rules were recovered from `Armada.exe` of the 1.2 install (SHA-256
`c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5`) and are restated here in our own
words (INV-0001). Each rule gives the address it was read at as evidence. A rule marked **inferred** is an
interpretation that the program does not label itself. A rule marked **stand-in** is the engine's own,
used where the game's rule is not known (INV-0003).

Key names are object definition keys unless they are in capitals, which are game constants from
`RTS_CFG.h`.

## Values that depend on the target

`hitChance`, `damageBase`, `damageVariance`, `damageThreshold`, `shieldCrewModifier`, and
`hullCrewModifier` each hold a default value and an optional list of overrides, one per target class:

```
damageBase = 30
	"zmama.odf" 2
```

The value for a shot is the override named by the target's definition name, else the default. The
match is case-sensitive (0x4ffd70). That the name includes the `.odf` suffix is **inferred**.

## Alert stance

Every unit has one of three alert stances: red, yellow, or green (0x426e0b). A unit starts at red; a
definition that sets `defaultGreenAlert = 1` starts at green (0x425bcf, 0x4266bd).

- **Red:** the unit picks targets by itself. An idle unit (one without an order from the player; the engine
  counts a unit with a move order as not idle until it arrives, **stand-in**) runs target selection again
  every tick and hands the pick to its weapons (0x42a170 calls 0x42ff80). The pick is no order: the unit keeps
  it while it stays valid and within the search radius, and takes the nearest valid hostile otherwise. A
  derelict is no valid target (0x42faa0), so a pick that turns derelict is dropped on the next tick. A unit
  that can attack (`canAttack`) stays near its **tether point**. While its center is within 300 units of that
  point (in 3D, the square of the distance at most 90000, 0x42a3a9, the constant at 0x5ca7ac), it fights the
  dogfight at its pick (0x42a170 calls the attack maneuver 0x42c400, "The dogfight" below), with no path move and
  no band test: it drives straight at the pick until the pick lies within the range, then stops and faces it. The
  idle path tests no stance (searched, not found), so the sandbox's rule that red alone closes stays a **stand-in**. Farther away,
  or with no pick, it moves back to the point and stops there (0x42a3e4, 0x42a3f4), keeping its pick, so its
  weapons fire at it whenever one reaches. The edge has no margin: a unit closing on a pick near 300 units turns
  back to the point as soon as it crosses the edge and closes again once back inside (**stand-in**; whether the
  attack maneuver smooths this is not traced). A move order sets the tether point to the destination (0x42704e), so
  a ship stays where it was sent; an order without a position, such as a stop, sets it to where the unit stands
  (0x46eac0). The point starts at the unit's placed position (**stand-in**; the game's point at creation was
  not found). When a player's attack order ends with its target destroyed, the point becomes where the unit
  stands then (**stand-in**). A move gives no height: the order's goal lies at the middle of the combat band, and
  the point takes that height (**stand-in**), so a move that ends short of its goal returns the ship to the band's
  middle. The
  engine stops the closing when the stance leaves red (**stand-in**); the return is the hold below, which no
  stance ends. A player's attack order keeps its target by its stored team relation (see Attack order).

  A recent hit moves the tether point. When a shot landed less than one second ago (0x46e640 compares the
  time since the hit, recorded by 0x46e340, against 1.0; strictly less) on the idle unit itself or on a unit
  of its own team within 400 units of it on X and on Z (0x42a000; whether the edge counts is a **stand-in**),
  the idle path posts an order without a position (0x42a370), which sets the tether point to where the unit
  stands (0x46eac0; that this order takes that path is **inferred**). So a ship under fire beyond its tether
  keeps closing. Only a landed shot counts as a hit (**stand-in**). A hit that dooms the teammate it lands
  on counts like any other while that dying unit is still in the world (**stand-in**; whether the game
  counts it is not known). What else this order (command type 5)
  does, and the check at 0x42a370 that can skip it, are not traced.

  The game's idle path branches on whether the unit's team is an **AI team**: a per-team flag (`FUN_00472100`
  reads it from the team's record; the getter is `IsAITeam`, 0x436840) that something outside the program
  sets. Only an AI team's idle unit walks toward the position of a unit that hit it less than a second ago
  (0x42a298) or takes the attack path behind the unit definition's flag at +0x237 (0x42a24c). No team in the
  map sandbox is an AI team, because the player orders the units of every team, so neither is built.
- **Yellow:** the unit picks targets by itself and fires at them, and never moves on its own to a target (it
  still holds its tether point, below). The game makes one more validity check on yellow candidates whose
  meaning is unknown; the engine skips it (**stand-in**).
- **Green:** the unit picks no target by itself. It fires only at a target an attack order gives it
  (0x42ffac).

**The hold.** An idle ship with thrust holds its tether point at every stance (`docs/rules/forced-motion.md`,
"Move-order arrival and hold").

The install binds the stances to keys in `Input.map` (`green`, `yellow`, `red`).

## Target selection

A unit with no weapons picks no target. Otherwise, at red or yellow (0x42ff80):

1. The search radius is the longest range of the unit's non-special weapons (R, "State 5" below), less 25. A special
   weapon that reaches farther does not widen it.
2. Distances are measured surface to surface: the distance between the centers less the radii of both
   units.
3. The current target is kept while it stays valid and within the radius.
4. Otherwise the nearest valid target within the radius is taken.

A target is valid when it is a hostile unit, not being destroyed (a dying unit is being destroyed), and not
derelict (0x42faa0), and the unit's team is not boarding it (the boarding block, 0x42fb31; see "Transport and
boarding"). An attack order is no pick: it keeps its target by the team relation it stored, so an order
given on a derelict keeps it and an order on a live hostile unit drops it when the unit turns derelict (see Attack
order, 0x42fb90, 0x468cd0). The game
also requires that the target is visible to the unit's team; the engine has no fog of war, so every
unit is visible (**stand-in**).

Selection makes no test of whether any weapon can reach the target: the validity check and the search
(0x42faa0, 0x4300a0) test no hardpoint, arc, or line of fire, and the distances are surface to surface
(0x430017, 0x4301d4; the 25 at 0x42ffd1). A weapon, however, fires only from a hardpoint within its range of
the target's center (see Firing), and nothing drops a pick the unit cannot hit (searched, not found). So a
unit that does not close on its pick, such as a yellow ship or a station, can hold a target with a large
radius whose center no hardpoint reaches, and fire nothing at it for as long as the target stays within the
radius. The game behaves the same way (**inferred** from the program's code; not observed in a running game).

## Attack order

An attack order names one target unit. The player's attack order is the process's state 5 (0x428e90); the AI's
is state 7 (0x4294e0).

**Which state the player's attack posts.** The command the interface offers for a hostile craft is 5: the craft's
cursor command (0x4664e0, vtable slot 0x7c, in the default cursor mode) returns 5 for a hostile target when the
attacker's weapon carrier holds a non-special weapon (0x46e2a0; the carrier's non-special count at +0x2c), and 0x470bf0
sends the command byte on. The script export `Attack` posts 5 (0x436e50), `AI_Attack` posts 7 (0x437270), and the
cheat `canofwhoopass` (0x4a77b0) turns 5 into 7 in the order handler (0x4274c0). That the command byte is the order
type the handler switches on is **inferred** (the handler 0x426ad0 was not read for it). State 7 is the AI's attack,
with a retreat check (0x4292d0, thresholds not studied); no team of the sandbox is an AI team, so it is not built.

**State 5, each tick** (0x428e90):

1. The weapons' target is the ordered unit when it is hostile and its surface distance is under R, else the nearest
   valid pick (0x42ff80; target info 0x42fb90). R is the **range** of the unit: the longest `range` of its
   non-special weapons (the carrier's +0x20, kept by 0x506e30), whatever a special weapon reaches.
2. The gap is the distance between the ship's center and the target's center, less the ship's radius (+0x60) and
   half the target's radius, or less none of the target's when a non-special weapon has `targetLocation = 1`.
3. While the gap exceeds R − 50 (0x5ca798) the ship moves by path toward the target's center (0x42e4a0). The
   game replans a path for a target farther than 400 and builds it again once the target has moved more than 300
   from the path's goal; nearer, the move runs straight at the target. These two distances hold with the game
   setting at 0x632ec4 off, its default; the setting's label is not known. The move may warp when the gap exceeds
   R or the target is faster than the ship's impulse speed. The engine plans the path with the path planner, the
   order list [the target] being planned, advanced, and replanned as a move order's is (`docs/rules/path-planning.md`,
   "The order list and the replans"); a target no path leads to gives the ship the stop order (0x42ded0, 0x46eac0; an
   attack's process type is 5, 6 or 7, never the 0x19 for which 0x42ded0 gives no stop order). That order is a move to the
   ship's own position (0x4267a0 hands it to 0x426ad0 `case 2` on the next tick), so the attack ends and the ship holds
   with no order. It models no warp (**stand-in**). The straight move hands 0x42e4a0 the ordered unit's velocity
   (0x42fb90 reads it from +0x140 to +0x148 and gives it to 0x428e90), which enters the throttle spring as the point's
   velocity; the engine passes it the same way.
4. Within that gap a ship without a target-location weapon fights the dogfight at the weapons' target; with none
   the game calls 0x46eac0(2) (0x429016), which ends the attack as the stop order of step 3 does. A ship with a
   target-location weapon turns in place toward the ordered unit, except where its process byte +0x10c is set: that
   case also calls 0x46eac0(2). 0x42fb90, which 0x428e90 calls first, sets +0x10c to 1 (and clears the ordered unit's
   slot +0x68) when the ordered unit is lost: gone, its serial changed, its +0x1b1 set, or its +0x1a8 bit for the
   ship's player set; 0x46eac0 clears it. After a loss, on the next tick (+0x68 already cleared), 0x42fb90 returns the
   ship's own position as the point, velocity 0, half radius 0, and 0x42ff80's pick as the weapons' target; the gap in
   0x428e90 is then below R − 50. A ship without a target-location weapon therefore fights the dogfight (0x42c400) at
   that pick for as long as one exists and calls 0x46eac0(2) only when there is none; only a target-location ship ends
   at once (+0x10c stays set). **Stand-in:** the engine ends the attack at once, for every ship, when the ordered unit
   is destroyed or dying, and models neither the +0x1b1 nor the detection loss; the continued fight is not built. The
   dump does not settle whether a death handler elsewhere ends the order before 0x428e90 runs again, nor which point
   0x428e90 reads on the tick of the loss itself (the point `local_34` is not written there). The engine never reaches
   the no-target case (inside the gap the ordered unit's surface distance is under R, so it is the weapons' target) and
   would end the attack there as the game does.

**The dogfight** (the attack maneuver 0x42c400, which calls 0x42c9d0). In stock play every attack maneuver takes
the dogfight (maneuver mode 8): the test 0x42bf20 answers true because `MORASS_SHIP_LIMIT` is 1, under 3 (RTS_CFG.h),
and a ship with a target-location weapon or with Borg physics takes it too. The maneuver does nothing for a ship
whose engines are out or that is derelict, and holds its heading at throttle 0 when it has no target. It sets the warp
multiplier to 1 and swaps the ship to the coefficients of `fastphys.odf` (`docs/rules/flight-model.md`, "The
coefficient map"). The dogfight, with `d` the vector from the ship's origin to the target's origin and
R as above:

- The aim `F` is `d × (|d| − R)` while `|d| > R` (strict) and 0 within. Its Y is zeroed while the ship's center is
  above `SHIP_COMBAT_CEILING` and the aim rises, or below `SHIP_COMBAT_FLOOR` and the aim sinks (stock 67 and 10).
- The ship steers along `F` with scale 1 (0x42f740, "The steering routine"): beyond R the throttle is
  min(|F|, 1), full impulse except within about 1/|d| of R, with the pitch servo toward the target's elevation;
  within R the throttle is 0.001 and the pitch target 0, level.
- A second yaw servo call toward the target's bearing atan2(d.x, d.z) follows the steering's and wins, so within R the
  ship stops and faces the target. No lead point, circling, break-off, or pass exists. `DOGFIGHT_RANGE`,
  `CIRCLE_IN_PLACE_DURING_COMBAT`, and the `ANCHOR_*` constants are not read by it.
- A ship with a target-location weapon turns toward the ordered unit's center at throttle 0 (scale 0), or along its own
  forward axis when that point lies within 10 (the squared distance at most 100, 0x5ca86c), so it keeps its heading.
  **stand-in:** a ship with no ordered unit (an idle ship at red, closing on its own pick) takes its own center as the
  point and so keeps its heading; the game's branch reads 0x4304e0 when an order list exists, and what that
  yields for an idle ship, like where a non-idle order gives the point, was not traced.
- A Borg ship writes no yaw target: the steering writes a velocity command only (`docs/rules/flight-model.md`, "The Borg
  branch"), neither routine writes the heading target (+0x4b0), and each physics sub-step resets it to the
  yaw, so the ship does not turn to face the target (**inferred** from the writes; the order of the process and the physics
  step within a tick was not traced). The engine feeds the dogfight's aim to the Borg steering at scale 1 (a velocity
  command along the aim), with no servo and no heading target.
- The path move and the dogfight swap at the band's edge with no memory: the dogfight restarts its drive only when the
  origin distance exceeds R. With small radii a ship enters the dogfight already within R, so it stops and faces.

- The weapons fire at the ordered target only while it is within the longest weapon range. Until then
  they fire at the nearest valid target in range, as target selection picks it (0x42fd6f).
- When the ordered unit is lost, the player's attack (0x428e90, "State 5", step 4) ends at once for a ship with a
  target-location weapon (0x46eac0(2)); a ship without a target-location weapon continues the fight at its own pick
  and ends the order only when no pick is left (0x429016). **Stand-in:** the engine ends the order at once, and the
  unit becomes idle, when the target is destroyed or dying, for every ship; a dying target counts as destroyed. A
  player's attack order on a dying unit is refused.
- **The stored team relation** (0x4275ee, 0x4275f3 write it; 0x42fb90 checks it; 0x468cd0 computes it). The order
  stores the relation between the attacker's team and the target's at the moment it is given: 1 for a derelict target,
  otherwise the attacker's team's diplomacy value toward the target's team (0x4493f0, the team's table at +0x2c), never
  the target's team number. It keeps its target only while that value still holds, so a target that moves to another
  team the attacker's team holds the same value toward keeps the order. Which diplomacy value an enemy team holds was
  not read: were it 1, a live enemy that turns derelict would keep the order. A live target that
  turns derelict, or that changes team so that the relation differs (it joins the attacker's team, say), ends the order
  (**inferred** that the order ends as the destroyed-target order does; the game drops the target in 0x42fb90). An order
  given on a derelict keeps it, so an attack order can finish off a derelict, until the derelict is destroyed or its
  relation changes (a revived or captured unit). The check runs every tick, so it applies out of range as well. A team
  change of the attacker itself replaces its order with a stop (0x46dff0, 0x46eac0).
- The order also ends when the attacker's team is boarding the target (the boarding block, 0x42fb90; see "Transport and
  boarding"), until the team gives a new attack order on it.
- A unit without weapons does not accept the order.

## Firing

A weapon fires when its reload delay has passed and its unit holds a target (0x509bd0). No order is needed
beyond the target the unit holds. Each reload gives one shot, from one hardpoint of the weapon's
`weaponHardpointsN` key, never one shot from each.

The weapon fires only when one of its hardpoints is within the weapon's own `range` of the target's center
(0x50a520, 0x50a290). The distance runs from the hardpoint to the target's center, not surface to surface,
so a target that unit selection picked can still be out of every weapon's reach. The shot leaves from the
nearest hardpoint in range, and a torpedo or pulse starts its flight at that hardpoint's world position. The
first listed hardpoint wins a tie (**inferred**: the tie order follows the
key's list order). Each weapon of a unit is judged by its own `range`, so a short-range weapon stays silent
while a longer one fires. A weapon with no hardpoint in range fires nothing, makes no sound, and stays ready,
so it fires on the first tick a hardpoint comes in range. A weapon whose `weaponHardpointsN` key names no
hardpoint is reported and left out, so it does not count as a weapon of its unit.

The game can also restrict a weapon's fire to an arc (`restrictFireArc`, 0x50a170). The arc is not built:
every weapon fires in any direction (**stand-in**).

After each shot the reload delay is `shotDelay`, multiplied by a crew factor when the weapon sets
`crewAffectsWeaponDelay` (0x506452, 0x50b690). With the crew ratio as current crew over `maximumCrew`,
the factor is the race's `weaponRed` at or below its `crewRedStatus`, the race's `weaponYellow` at or
below its `crewYellowStatus`, and 1 otherwise.

## To-hit

The to-hit roll happens when the weapon fires (0x50a3b0):

- The shot always hits a target whose engines are destroyed or that is derelict.
- Otherwise the chance is `hitChance`, plus `warpToHitModifier` when the firing unit is at warp, plus
  `toHitWarpModifier` when the target is at warp, plus `stopToHitModifier` when the firing unit is
  stopped, plus `toHitStopModifier` when the target is stopped. The slow modifiers are not used. **Stand-in**: a unit is stopped
  when the length of its physics velocity is below 0.1 (the root of the no-thrust brake's rest threshold, 0.01); the game's
  test was not traced.
- The shot hits when a uniform random number from 0 to 1 is at most the chance.

A missed shot is aimed by the miss path (0x50aab0), which leads the target and offsets the aim by the target's
radius; 0x50a7c0 is the aim of a shot that hits. The missed shot hits nothing. The sandbox aims a miss beside
the target without the lead (**stand-in**).

## Shots

The ordnance definition's `classLabel` decides how a shot travels.

- **Beam** (`phaser`): the beam runs from the hardpoint to the target for `lifeSpan` seconds and deals its
  damage once, when that time ends (0x4eb47e). It deals no damage when the firing unit is gone or dying
  (0x4ee5f0, 0x4eb170; see Dying units) or the
  target is more than twice the weapon's `range` away before then (0x4eb2e5). `shotSpeed` plays no part.
- **Torpedo** (`photontorpedo`, `plasmacannon`): the torpedo flies at `shotSpeed`, turning toward its
  target by `omegaTurn` and `seekTime`. It deals no damage when `lifeSpan` ends first (0x4ed174), so a fast
  target can outrun it.
- **Poleron torpedo** (`polerontorpedo`) flies as a torpedo, and **pulse phaser** (`pphaser`) flies
  straight at `shotSpeed` (**stand-in** for both).

A flying shot hits where its step of the tick enters the target's shield ellipsoid, while the target's
shields are up and the ordnance does not set `ignoreShield` (0x4ec27a, 0x4ed427). Otherwise, and when the
step misses the shield, it hits when the target point lies strictly within one step of it (0x4ec2b0,
0x4ed46b). `docs/rules/shield-effects.md` states the shield test and the ellipsoid. **Stand-in**: the target
point is the unit's center; the game uses the target's hardpoint.

## Damage

A shot that lands deals `damageBase` plus a random part of up to `damageVariance` either way (0x4e7158).
A roll below zero deals no damage (**stand-in**; the game's handling is not known).

### Hit location

The hit location is rolled first (0x464dd0). A random whole number from 0 to 99 is reduced by the current
hit percent of engines, life support, weapons, shield generator, sensors, and hull, in that order. The
location whose percent takes the number below zero is hit. When none does, the hit is a critical hit.
The percents are the definition's `enginesHitPercent` and its siblings and `hullHitPercent`;
`criticalHitPercent` is not used.

The roll reads a live percent per system and never a working flag. When a system is destroyed, its whole
live percent, its own and the shares it took before, is shared evenly among the systems still working
(0x467620), or goes to the critical share when none works (0x467b70). On a derelict unit no system counts
as working (0x466ca0), so a system destroyed there sends its live percent to the critical share, and the
systems that are still intact keep theirs. A system destroyed before the unit went derelict keeps its
shares with the systems that took them until those are destroyed. A repaired system takes its own percent
back (0x467600). The other working systems give it up in proportion to their excess over their own
percents, and what their excess does not cover comes out of the critical share, so repairing every
destroyed system of a unit that is not derelict returns every system to its own percent (**stand-in**:
the addresses do not show which systems give it up).

### Applying a hit

For a hit on a unit (0x465100):

1. The shield part is the damage times `SHIELD_DAMAGE_PROTECTION`; the rest is the systems part.
2. While the shields are above zero:
   - When the shield part exceeds `damageThreshold`, the crew loses the smaller of the excess and 1,
     plus the crew times `shieldCrewModifier` times the excess, in percent.
   - The shields lose the shield part. Damage that takes the shields below zero is added to the systems
     part.
3. When the shields are at zero, the whole damage is the systems part.
4. The systems part collects in a running total, and only whole points pass on.
5. By location:
   - A **system** loses one hit point per whole point.
   - The **hull** costs crew: the smaller of the systems part and 1, plus the crew times
     `hullCrewModifier` times the systems part, in percent.
   - A **critical hit** of at least one whole point makes the unit a dying unit (see Dying units).
6. When the shields reach zero, they go down for `shieldDelay` seconds (0x465719).

### Hit explosion

A hit shows an explosion at the node of the target it hits (0x465100, the end of its location switch):

- **When:** the damage is above 0 and the hit's systems part is not 0. A hit that the raised shields take whole
  shows none. A critical hit of at least one whole point dooms the unit and returns before the explosion, and a
  hit on a dying unit shows none.
- **Node:** when a weapon makes its shot (0x50ae40), it rolls the damage and the hit location (0x4ee710, ordnance
  slot 0x58, through 0x464dd0) and stores the hit node (ordnance +0x110, slot 0x64, 0x4d92e0). For a system
  location, the node is the system's `<system>TargetHardpoints` node nearest to the firing hardpoint, and the
  first listed wins a tie (0x468a60; class +0x358 + 16 × k, k in the order shield generator, engines, weapons,
  life support, sensors, read by 0x460f30). A hull or critical hit, or a system without target hardpoints, takes
  the model's root node (model instance +0x38). The class load reads `hullTargetHardpoints` and
  `criticalTargetHardpoints` into a scratch list only (0x46038c, 0x4603d3), and no code fills the hull and
  critical lists at class +0x3a8 and +0x3b8. The hit hands the target its damage and the node (0x4ee7f0, slot
  0x90).
- **Look:** one of `systemexpl1` to `systemexpl7`, picked at random (0x5018c0), in white, at the node, which it
  follows as the unit moves (0x515d50, 0x515fb0). Its size is 0.14 times the model's bounding radius (0x60076c),
  and at least 4 (0x600770); the sprite is twice its size wide (0x5975e0). It shows for 1 second (0x515f40).

**Stand-in**: the sandbox rolls the location and picks the node when the shot lands, from where the firing
hardpoint is then; the game does both when the shot is made. Every unit counts as a ship for the node, which the
game tests with object flag 8 (0x4690a0). The sprite's flipbook plays on the frame clock.

## Shields

The shield maximum is the definition's `maxHealth`; a unit has no hull hit points (0x46d9ef). A unit
starts with full shields.

While the shield generator works and the unit is not derelict, the shields regain `shieldRate` per second
up to the maximum (0x466b00). After the shields went down, they regain nothing until `shieldDelay` has
passed. A hit that leaves the shields above zero does not restart that delay. A destroyed shield
generator stops the regain and leaves the current shields as they are.

### Shield hit effect and shield-down overlay

`docs/rules/shield-effects.md` states the rules of both: when they spawn, their model, duration, contact,
placement on the shield ellipsoid, flipbook, and drawing.

A beam on a target whose shields are at 0, or whose ordnance sets `ignoreShield`, shows an `explosion1`
sprite at the hull instead, once a tenth of its lifetime has passed and its head is in reach of the target,
scaled 2.0 times the weapon's size field (0x6173dc, 0x6173e0). It follows the beam's contact point until the
beam ends. **Stand-in**: the head is in reach once the surfaces of shooter and target are within the
weapon's `range`; the size field is read as the ordnance definition's `size` key (1 when missing), the
sprite is `explosion1` of the `explosion1` family. The sprite reaches its size out from its center on each side,
so it is twice the size wide (0x515d50, 0x5975e0).

A geometry or sprite the install lacks is reported once with its name and skipped; the fight goes on. The
shield geometry of ordnance that sets `ignoreShield` is neither loaded nor reported, because that ordnance
never shows one. The state dump's `shieldDown` field is the unit state's overlay flag; a unit that has left
the map keeps its last value. **Stand-in**: nothing in the engine raises a derelict unit's crew again, so
the overlay's return on crew recovery applies only to a state set from outside.

## Special energy

Every unit of every race carries special energy, read from its definition: `maxSpecialEnergy` is the maximum (class
+0xd8, 0x46a470) and `specialEnergyRate` is the points regained per second (class +0x224, 0x45fea0). A definition
with neither has 0 and 0, with no gap reported, and its unit has no special energy.

Each sandbox tick adds the tick's seconds times the rate while the unit is below the maximum and clamps the result
to the maximum (0x466bf0, 0x46f020). The gain is linear in time, so a span of seconds ends at the same value at any
tick length, and the value never exceeds the maximum.

The state dump's `special=<current>/<maximum>` and `expect <unit> special <op> <value>` read it.

**Stand-in**: a unit starts with its special energy at the maximum; the exe's start value is not yet read. The
regain runs for every unit that has not left the map, derelict and dying ones included; the exe's conditions on it are
not yet read.

## Special weapons

A special weapon is a `toggleweapon` definition with `special = 1`. The sandbox runs the Holding Beam, whose ordnance
`classLabel` is `holding_bm` (`gholdbm`, reached through `hholdbm`'s include), and the Auto-Assimilator, whose ordnance
`classLabel` is `borgbore` (`gborgbor`, ordnance `borgbor`, key F3); every other special weapon is left out of
the plan without a gap. The plan reads the weapon's `buttonSlot` (the number of the `fire_special_N` key, F1 to F6) and
`range`, and the ordnance's `specialEnergyCost` (+0x34, 0x4ea1e0), `targetRange`, `lifeSpan`, `boardingSize`,
`boardingInterval`, and `boardingDelay`. For the effect it also reads the weapon's `fireSound` and `weaponHardpoints`
and the ordnance's `shotGeometry`, `radiusScale`, and `lengthScale` (see "Effect").

**The order.** The player presses the slot's key, then left-clicks a target. A key no selected unit has a weapon for does
nothing. Per selected unit that has the weapon: a weapon that runs is turned off, with no refund, and a unit with it off
waits for the click; a selection of only running units waits for none. The click on a unit gives each waiting unit the order
and ends the wait; a click on empty space changes nothing. A selection change or a right-click ends the wait, and the
right-click gives no move or attack order. **Inferred**: the per-unit loop over the selection and the end of the wait on a
right-click copy the transport order's, and the key of the `fire_special_N` block being the key of the weapon whose
`buttonSlot` is N; the game's key dispatch was not traced.

**Start.** The beam starts only when the unit holds at least the weapon's cost in special energy and the target is an
enemy ship: a unit of a team hostile to the unit's, no station, not dying, not the unit itself (+0xbc bit 0x8 marks the
Holding Beam's targets, 0x4ddd30; **stand-in**: a unit whose definition sets `is_starbase` stands for a target without that
flag, since the flag's link to `is_starbase` was not traced), within the weapon's `range`, measured center to center. It spends the cost once. A target
that is friendly, neutral, a station, out of range, or the unit itself starts nothing and spends nothing.

**Drain.** Each sandbox tick, while the beam runs, takes the cost times the tick's seconds (0x509160, 0x4ddf80). When the
energy cannot cover a tick's drain the beam ends with the energy as it stands, so the energy never goes below zero. The
world runs on fixed ticks, so the beam ends on the same tick at any frame rate. The regain of special energy (see "Special
energy") runs beside the drain; whether the exe regains while a weapon runs was not traced (**stand-in**).

**End.** The beam also ends when its target is destroyed or joins the unit's team, when the target is beyond the weapon's
`range` times 1.33 (0x5d0148), and when its unit is destroyed or captured (a team change clears it, as it clears the order).
**Stand-in**: a dying unit counts as destroyed for both ends, as for a boarding party; a unit runs one special weapon at a
time, so the key of another slot starts nothing while one runs; a move, attack, or stop order leaves a running beam on, since
the game's handling of those orders was not traced.

**Hold.** On the first tick the beam runs, the sandbox records the target's position and heading (pitch, yaw, roll). Each
tick the beam runs, the target's position and heading are reset to them (0x4ddd30). The target's velocity and order stay, so
when the beam ends the ship moves on from where it was held. **Stand-in**: the velocity a held ship builds under its order
carries into the release, since the exe's treatment of the velocity during the hold was not traced. A target without a flight
of its own is not held.

**Boarding.** On the first tick the beam runs (the first hit) a timer starts at the ordnance's `boardingDelay` (1 s for the
cube), and each tick it counts down by the tick's seconds. When it falls below zero (a timer at exactly zero sends nothing, so the first party leaves on the tick after 1 s), while the target's defined maximum crew
is above 0 and the shooter's crew is at least `boardingSize` + 5 (0x5ca8ac), the beam sends one ordinary boarding party of
`boardingSize` crew (20 for the cube) from the shooter, and the timer resets to `boardingInterval` (1 s). The party is the
transport's (see "Transport and boarding"): it takes its crew from the shooter and carries the shooter's team and race
(0x55f620 with race 0 and team -1, from 0x4ddd30), but it lands at once, with no delay and no test of the target's shields,
which it leaves as they were. It fights and captures by the boarding rule (0x55f750, 0x46dff0): a party that outnumbers the
crew gives the ship the shooter's team and race, so a Borg shooter assimilates it (the race `borg` shows the `borg` overlay),
and the beam ends because the target has joined the shooter's team. The timer runs on the fixed ticks, so a step of any
length sends the same parties. **Inferred**: when the crew conditions fail the timer stays below zero and the next tick tests
them again. **Stand-in**: the ordnance's `lifeSpan` (30 s) does not end the beam, since whether the exe ends the beam by it
was not traced; the end conditions and the energy end it.

**Effect.** Each special weapon shows a beam model and plays a fire sound; the Holding Beam and the Auto-Assimilator
(see below) share the rule. The model is the ordnance's `shotGeometry` (`wholdbm.sod` for the Holding Beam, `wborgbor.sod`
for the Auto-Assimilator) and the sound is the weapon definition's `fireSound` (`swbhb.wav`, `swbauto.wav`). While the
weapon runs, the model sits at the firing hardpoint, turned toward the target's center, and scaled `radiusScale` across
and the distance over `lengthScale` along its length axis (the ordnance's +0x128 and +0x124 as 0x4ddf80 reads them for
the Holding Beam each tick, and 0x4da7a0 for the Auto-Assimilator): the model's length is `lengthScale`, so the beam
spans exactly the distance. The beam goes when the weapon ends. The fire sound plays once, when the weapon starts (its
first tick runs). A model or sound that the install lacks or the game's decoders cannot read is a gap and the weapon
works without it. **Stand-in**: the hardpoint is the nearest to the target at the start, among the weapon's
`weaponHardpoints`, or the unit's center when it has none (the game's hardpoint choice, `param_1[0x43]` of 0x4ddf80, was
not traced); the model's length axis is its Z axis and it spans from its origin (inferred from the scale rule).

**Not built yet**: the drones the beam sends aboard as models, and the other special weapons.

### The Auto-Assimilator

The plan reads the weapon's `buttonSlot` (3 for the Borg Assimilator) and the ordnance's `specialEnergyCost`
(+0x34, 0x4ea1e0), `specialValue` (+0x110, 0x4ea1e0: the crew moved per second) and `attackRange` (+0x120, 0x4da590,
the weapon's reach). The ordnance's `duration` is not read: Armada.exe's loader has no such key (0x4da590). The order,
its start (an enemy ship within the reach, the energy to cover the cost, spent once), its energy drain, and its
ending by a second order, by the shooter's capture, or by the target's destruction are the Holding Beam's (see above);
the start's range is `attackRange`.

**Drain.** Each sandbox tick, while the weapon runs, `min(tick x specialValue, the target's crew)` leaves the target
(0x563740 case 7, through 0x467390: a target of a maximum crew above 0 that is not derelict; its crew below one
makes it derelict, 0x466ca0). The shooter takes the crew up to its maximum crew (0x467570); the rest goes to its team's
crew pool, up to the pool's cap, and crew beyond the cap is lost. The target keeps its team and race: the drain never
captures it, and no crew is created. The tick is fixed, so the drained total over a span is the same at any frame rate.

**End** (0x4da8f0). The weapon ends when the target is derelict or has no defined maximum crew, or of the shooter's team, or beyond `attackRange`, when the
shooter is not a ship, and when the shooter's special energy ratio reaches zero. **Stand-in**: the energy end is the tick's drain exceeding the energy left (a cost of 0 never ends it) and the
no-defined-crew end follows the drain's own test; the crew below one that
leaves with the derelict (`CombatRules.CheckCrew`) is lost, so the crew across units and pools never grows but can shrink by
that fraction; the end of range is the `attackRange` itself (the Holding Beam's 1.33 factor, 0x5d0148, belongs to its
beam); a dying unit counts as destroyed, as for the Holding Beam.

## Crew pool

Each team has a crew pool in its team record (+0x20, the cap at +0x24; the record of 0x448f30, built by 0x448d50).
The sandbox's combat world holds one pool per team a unit has belonged to (a team no surplus has reached reads as an empty pool at the cap). The cap is `cfgMaxCrew` of RTS_CFG.h (set into
every team record by 0x448c60 from the config read at 0x4453f0). The Auto-Assimilator adds the crew its shooter cannot
hold (0x563740 case 7: the pool becomes the smaller of the cap and the pool plus the surplus). **Stand-in**: every pool
starts at 0 (the game starts it at a share of `cfgStartingCrew`, 0x448d50), and the cap is 99999, the stock value, where
the install's RTS_CFG.h has no `cfgMaxCrew`. The state dump has a line `pool team=<n> crew=<crew>/<cap>` per team and
`expect <unit> pool <op> <number>` reads the pool of the unit's team.

**Starbase fill.** Each tick each live starbase adds the tick's seconds times its race's `crewAccumulationRate` (race
+0x20c) times `planetCrewAccumulationModifier` (+0x210) to the pool of its current team, up to the cap (0x45f830; the
race values are read by 0x4442b0). A Borg Nexus (3.5 × 1.5) adds 5.25 crew per second. A captured starbase fills its
new team's pool at the race it now has, and its old team's pool stops gaining from it. The fill runs per world tick, so the
amount over a span is the same at any step length. **Stand-in**: a starbase is a unit whose definition sets `is_starbase`
to a number other than 0 (not traced in 0x45f830); a derelict starbase fills like any other, a destroyed or dying one
does not; a race with no `crewAccumulationRate` or `planetCrewAccumulationModifier` has 0 for it, unreported.

## Ship systems

Each of the five ship systems has the definition's `enginesHitPoints`, `lifeSupportHitPoints`,
`weaponsHitPoints`, `shieldGeneratorHitPoints`, and `sensorsHitPoints`, plus `SYSTEM_HITPOINTS_BONUS`
for a system that has any (0x460f8c). A system with zero hit points does not exist on that unit.

A system at zero hit points is destroyed (0x467b70). The crew loses 1 plus the system's `<system>CrewLoss`
percent of the crew, unless the unit is derelict. A destroyed system means:

- **Engines:** the unit has no thrust and coasts (see Coasting), and every shot at it hits.
- **Life support:** each second the crew loses 1 plus `lifeSupportLoss` percent of the crew, until the
  unit is derelict (0x465f6f).
- **Weapons:** no weapon fires.
- **Shield generator:** the shields stop regaining.
- **Sensors:** sensor range falls to `damagedScan` (**inferred**). Nothing reads sensor range.

### Repair

A damaged system regains hit points all the time, in and out of combat (0x4683d0). One hit point takes
`<system>RepairTime` divided by the system's maximum hit points, times the race's `repairStrength`, times
the race's `repairRed` or `repairYellow` when the crew ratio is at or below `crewRedStatus` or
`crewYellowStatus` (0x468060). The game also divides by an officer factor; the engine uses 1
(**stand-in**). A system whose repair time works out to zero does not repair (**stand-in**; the game's
reading is not known). A destroyed system works again only once it is back at full hit points (0x467d40).

### Repair sparks

While a system repairs, it throws sparks (0x4683d0, which runs ahead of each repair step):

- **Clock:** each system below its maximum hit points, destroyed or not, adds the step's seconds to its spark
  clock (system +0x28, 0 at class load, 0x460f30). Nothing resets the clock when the system is whole.
- **Cadence:** once the clock passes `<system>SparkTime` (system +0x24), the clock drops by the spark time until
  it no longer passes it, and one spark shows. A clock that only reaches the spark time throws none. The spark
  time is 0.5 when the definition does not set it (0x45fbe0); no stock definition sets it. On the world's tick
  of 0.02 s the first spark comes at 0.52 s, then one every 0.5 s.
- **Place:** the spark shows at one of the system's `<system>TargetHardpoints`, picked at random (0x5018c0). The
  class load leaves out each name the model holds no node of and warns about it (0x460f30). A system without
  target hardpoints throws no spark, and its clock still runs.
- **Look:** the sprite `xspark`, in white (0x690b60), at the hardpoint's node, which it follows as the unit
  moves (0x515fb0). Its size is 0.14 times the radius of the model's bounding sphere (0x600794; object +0x60,
  copied from model +0x24 by 0x4510a0). The sprite reaches that far out from its center on each side, so it is
  twice as wide (0x5975e0). It shows for 1 second (0x515f40) and goes with its unit.
- A derelict or dying unit repairs nothing, so it throws no spark.

**Stand-in**: the sandbox has no disable timer or held flag (system +0x2c, +0x30), which also stop the sparks
in the game. A spark time of 0 or less throws one spark per tick; the game's drop loop does not end for it. The
spark's flipbook plays on the frame clock, as the small explosions do.

## Crew, derelicts, and destruction

A unit starts with `crewCost` crew; a unit that starts below one is derelict from the start. A layout
object's starting state can lower the crew to a fraction of `crewCost`, lower the shields, and start ship
systems destroyed (`README.md`, "Open a layout"). A starting crew below one makes the unit derelict the
same way, with crew 0 and shields 0. The game starts every unit at full strength, so the starting state is
the engine's own (**stand-in**). When the
crew falls below one (0x466ca0), the crew is set to zero
and the unit becomes **derelict**: all its systems go offline and its shields go down. A derelict
unit regains no shields, loses no crew to life support, fires no weapon, and is no valid target for
target selection. It stays on the map.

The game can instead destroy a unit whose crew falls below one, by a flag whose source is unknown; the
engine always makes it derelict (**stand-in**).

A unit is destroyed only by a critical hit, which makes it a dying unit first. Losing every system or the
shields does not destroy it.

## Transport and boarding

A unit moves crew to another unit by transporter. A transport has a source and a target; the source sends
**boarding parties**, which add crew to a friendly target, fight a hostile one, and capture it when they win.

**Values.** `TRANSPORTER_RANGE`, `TRANSPORTER_DELAY`, `TRANSPORTER_ACTION_DELAY`, and `TRANSPORTER_MAX` come from
`RTS_CFG.h` (loaded at 0x44621f to 0x446294; the stock install gives 500, 1, 1, 5). Each race's `boardingStrength`
comes from its race definition (0x4442b0); the game's default is 1.0. **Stand-in**: a missing constant takes the stock
install's value, a missing or non-positive `boardingStrength` takes 1.0, and the stand-in race carries 1.0; each is
reported once.

**The order** (dispatch 0x426cb8). The ordering unit needs more than 9 crew (0x5ca860). The target must be a crewed
unit that is not being destroyed. **Stand-in**: the order also refuses a live hostile target whose absolute shields
are above 0.01, the cursor test's shield part (below), so a click on such a unit gives no order and changes no unit.
Range is not tested by the order; every cycle tests it. The order does not replace the unit's move or attack order, and a
later move, attack, or stop order does not end the transport; only the transport order without a target (pressing
the chord again, 0x463593, 0x426cbd to 0x467000), a stop condition, or the loss of the unit does (0x467000, 0x467050,
0x461df0).
The chord acts per selected unit (0x463593): a unit transporting (+0x4d4) has its transport cancelled (0x470bb0 with
0xb), and a unit not transporting requests the transport mode (0x471d90 with 5). With a mixed selection the
transporting units cancel and the mode is entered for the idle ones, and the next left-click on a unit gives the order
to those units only; a selection of only transporting units cancels and enters no mode. The per-unit loop over the
selection is **inferred**, not traced. The transport starts only when the source has more than `TRANSPORTER_MAX` crew (0x466f70). A target of any
of these kinds is valid: a hostile ship or station whose shields are down, a derelict of any team, and an own, allied, or neutral (team 0)
ship or station. **Stand-in** (**inferred** from the cycle, which stops on a full friendly target): the order refuses a
live non-hostile target that is at its crew maximum, and refuses the unit itself. A transport order on a unit that
already transports ends the earlier transport first, also when the source has too little crew to start the new one
(**stand-in**; the game's handler overwrites the target); an order on the current target changes nothing. Armed
and unarmed units, ships and stations, all transport: the per-tick transport work (0x465e80 to 0x467050) runs for
every Craft class.

**The cursor test** (0x4664e0, mode 5). A unit refuses the target when the target is not a crewed unit, the source's
crew is 9 or fewer, the target is hostile and its absolute shields are above 0.01, or the center-to-center distance is
above `TRANSPORTER_RANGE`. The engine exposes this as one function. **Stand-in**: the sandbox's order applies every
part but the range, adding the friendly-full part above. The cursor applies the order's test and the range (center
to center, at most `TRANSPORTER_RANGE`, as the cycle measures it), so a target out of range shows the no-transport
cursor and still takes the order. The cycle tests the shield fraction instead (below); the two
differ and both are kept. The Manheim-state test of the hover is not built (no such state in the sandbox).

**The cycle** (0x467050). The source never moves for a transport; a unit out of range takes the order and stops at its
first cycle (**inferred** from 0x4318a0 and 0x467050). The cycle timer counts down on every tick (the game's tick is not
recovered; the engine's tick is the world tick). At or below zero a transporting unit runs the cycle:

1. It stops when it is destroyed, dying, or derelict (0x467050; checked every tick here, **stand-in**).
2. It stops when the target is gone, destroyed, or farther than `TRANSPORTER_RANGE` center to center (squared
   comparison, at most; never the weapons' surface distance).
3. A hostile target whose shield fraction (current shields over maximum, 0 for a maximum of 0) is above 0.01 stops the
   transport. A hostile target whose shields are down gets its shield-down time set to its `shieldDelay` (0x4c4), so
   every boarding cycle holds its shields down.
4. A hostile target, or a non-hostile one below its crew maximum, takes one party of `TRANSPORTER_MAX` crew. The
   source loses the full `TRANSPORTER_MAX` crew; the party is the smaller of that and the crew the source had, and a
   source left below one crew is derelict (0x55f620, 0x4674d0, 0x466ca0). A party onto a hostile target lands after
   `TRANSPORTER_ACTION_DELAY`; onto any other target on the next tick. Any other target stops the transport.
5. The cycle timer is set to `TRANSPORTER_DELAY`.
6. The transport stops when the next party would take the source to its race's `crewRedStatus` (0x467050): the
   source's crew after this party, less `TRANSPORTER_MAX`, over its maximum crew, is at or below `crewRedStatus`. The
   test runs right after the party is sent (0x4672d0–0x467300). The party's constructor 0x55f620 calls 0x4674d0,
   which has already lowered the source's crew by the party (step 4), so the crew operand is the crew after this
   party and the test looks one party ahead. A maximum of 100 and a red status of 0.25 stop once the crew is at or
   below 30. After each party the crew stays above `crewRedStatus` × maximum crew (here 25), except that the first
   party can leave less when the source starts low (22 → 17, 10 → 5; more than 9 is all the order needs).

The first party leaves on the first tick after the order, because the timer starts at or below zero and keeps
counting while the unit is idle. **Stand-in**: a party lands once its delay has elapsed (the game lands it when its
timer falls below 0, at most one tick later); every time is in world ticks, so the frame rate changes nothing.

**A party in flight** (0x55f620, 0x55f750). A party carries the team, the race name, and the race values its source had
when it was sent (0x55f620 takes the source's race) and reads nothing of its source afterwards, so it lands whatever happened to the source. **Stand-in**: a party onto a
target that is dying is lost, as one onto a destroyed target is (the game tests that the target is found).

**The landing** (0x55f750). A party is lost when its target has a maximum crew below one (a unit without crew).

- *A derelict target*: when the party's team is the target's team, crew is added (capped at the maximum) and the
  target takes the party's race name and race values. When the party's team is hostile to the target's, the target is captured with the party
  as its crew. Any other party (a neutral team on either side) only adds crew. A derelict that reaches one crew or more
  ends its derelict state. There is no fight and no boarding sound.
- *A target that is not hostile*: the party's crew is added up to the target's maximum. The overflow stays in the
  party and is lost (**inferred**: nothing reads it afterwards). No fight, no sound.
- *A hostile target*: with the party size `P`, the target's crew `C`, the party race's `boardingStrength` `a`, and the
  target race's `d`, the party's strength in defender crew is `E = (a / d) · P`. The target loses `E` crew (an amount
  that can exceed `C`; crew below one makes it derelict). When `E` is at most `C` the party dies; otherwise its survivors
  are `(d / a) · (E − C)` in attacker crew. The boundary `a · P = d · C` is the party dying. There is no randomness
  (none is called in 0x55f750, 0x467050, 0x466f70, or 0x55f620). The fight is reported (the game plays `msboard.wav`
  at the target).

**Capture** (0x55f750, 0x46dff0). A hostile target whose crew fell to zero with survivors left, or a hostile derelict a
party reaches, joins the party's team and race: the unit's current order is replaced by a stop (0x46eac0), its race
becomes the party's (the owner change writes the race name at +0x1a4, 0x46dff0, and the race values come from the
party's source race, 0x55f620), so a Borg party makes the unit race `borg` and a later Federation party makes it
Federation again; the unit keeps every value of its own definition (weapons, maximum crew, and the rest). Its crew is
the survivors, capped at its maximum; below one crew it stays derelict on the new
team. The unit keeps its alert stance (**inferred**: no code in the owner change writes the stance), its place in
the selection (**inferred**; nothing in the owner-change path touches a selection), and its own outgoing transport
(0x46eac0 does not touch the transporting flag, 0x4d4): its next party is sent with the new team and race. A neutral
(team 0) unit is never captured (0x449490 is false for team 0). The capture of a starbase gives vision and is counted
(0x5619a0, 0x449560); neither is built.

**The boarding block** (0x466f70, 0x42fb31, 0x42fb90, 0x427616, 0x46e030). When a transport starts on a unit of another
team, the source's team is added to the unit's do-not-auto-target set. While it is in the set, that team's automatic
selection rejects the unit and its attack orders drop it. A new attack order on the unit by that team removes the team
from the set; so does the unit's team change (the owner change zeroes the set). Stopping the transport does not (0x467000
and 0x467050 do not write the set). The game skips the set for an AI team; no team of the sandbox is one.

**Feedback** (0x466f70, 0x46db90, 0x467000, 0x467050, 0x55f750, 0x462d40, 0x46e030). None of it has a team filter, so
every player sees and hears it.

- *Effect.* The `xtransport` sprite (row of `Sprites/weapon.spr`, a billboard) is attached once when the transport starts
  (0x466f70 through 0x46db90, name `xtransport`), to the source and to the target, each only if it holds no effect
  (+0xdc is 0). It is not attached again per party. A stop clears the handle on both (0x467000, 0x467050). A target
  boarded by two sources therefore holds one effect, attached by the first, and the first source to stop clears it while
  the second keeps boarding and does not attach another. **Stand-in**: clearing the handle removes the drawn effect (the
  game's release of the effect object was not found); an effect on a destroyed unit goes with the unit; the billboard
  is as wide as the unit's model and loops its flipbook, since the game's size and timing are not recovered.
- *Sounds.* `transprt.wav` plays at the source and at the target for every party sent (0x5aff40 from 0x467050). `msboard.wav`
  plays at the target for every fight against a live hostile unit, won or lost (0x55f750); a derelict or a friendly
  transfer has none. A sound the install lacks is reported once. The 1.2 install's `msboard.wav` holds only silence
  (every sample is the 8-bit zero level), so the fight is soundless there.
- *Voice.* After a transport order the ordering unit's `eventTransport` line plays, also when the unit has too
  little crew to start the transport (0x462d40 with event 8; the keys are
  read in 0x5ab750), unless the unit is derelict or a starbase (0x426d26 to 0x426d4c); a definition without the key plays
  none and is no problem. Only hero definitions set it. **Stand-in**: the game plays it only when the unit's team is
  the local player's (0x470680) and throttles repeats to one per second (0x5ab7d0); the sandbox orders every team, so
  every team counts, and the throttle is not built.
- *Team color.* The team join (0x46e030, called from the owner change) copies the new team's colors into the unit,
  from the color table by the team record (+0x174..+0x190). The captured unit takes the team-color slot of its new team,
  mapped as the map plan maps a team to a slot, and only that unit changes. That the renderer reads the fields at once
  is **inferred**.

**Stations** follow the same rules as ships (they share 0x465e80, 0x467050, 0x55f750, and 0x46dff0).

**Not built**: special transport (command 12, Shift+T), the recrew command, the AI's auto-transport (0x429920), the
vision and counts of a captured starbase, and Borg assimilation.

## Dying units

A critical hit makes the unit a **dying unit** instead of removing it (0x468990 sets the being-destroyed
state). The unit stays in the world until its timer runs out.

- **Timer** (0x466090): 3.0 s when the unit's speed is at most 1.0, 5.0 s when its speed is above 1.0 or its
  class sets `isHero`. The speed is the length of the unit's velocity (+0x140): the thrust speed of a
  ship under thrust, the coast velocity of a coasting ship, 0 for a unit that cannot move. A position push
  changes no speed. The timer counts from the tick after the critical hit.
- **Further hits:** a hit with damage above 0 that lands on a dying unit deals no damage and cuts 0.5 s from
  the timer (check at 0x4651f3, 0x468990; damage gate 0x465132-0x465147). A hit with no damage does nothing.
  A timer cut below 0 ends the phase on the next tick. Shots in flight at the unit still land (0x4ee620).
- **Coasting:** a dying ship coasts (see Coasting). It keeps the velocity and the turn, roll, and pitch rates
  it had; nothing in the dying routines (0x468990, 0x466090, 0x466320, 0x4663d0) gives it speed or spin. A
  dying ship that was moving faster than 1.0 coasts for 5.0 s while it slows. A dying station stays where it
  is. A dying ship takes no collision separation (0x471560), and some forces still move it
  (`docs/rules/forced-motion.md`, "Dying ships").
- **No targets:** no auto, red-alert, or player attack pick takes the unit (0x42faa0). A unit attacking it
  goes idle, as for a destroyed target.
- **No fire:** the unit fires nothing and its weapons stop (0x506d70). Its beams in flight end without damage
  (0x4ee5f0, 0x4eb170). Its torpedoes in flight fly on, without its shooter damage bonus (0x4ee7f0); the
  engine has no shooter damage bonus, so they land with their base damage.
- **No upkeep:** it repairs nothing, regains no shields, and loses no crew to life support (0x465e80 returns
  at 0x465e87).
- **No selection:** it is deselected and cannot be selected (0x4ab5d0, 0x4ab91e and siblings), so it takes no
  orders. The game's own check on a dying unit in the order code is not recovered; the rule rests on the
  deselection. **Stand-in**: a right-click on a dying unit picks no unit, so the selected ships get a move
  order to the point of the plane under it; the game's order code on a dying unit is not recovered.
- **Small explosions:** every 0.05 s (0x600784) one of `systemexpl1` to `systemexpl7`, picked at random,
  plays at a random point on the unit (0x466320, 0x466180, table 0x600750). The sprites are rows of the
  install's sprite tables. The cadence keeps its own accumulator on the world's fixed tick, so it holds at
  any frame rate. **Stand-in**: the point is uniform in the box of the unit's radius around its center, and
  the explosion's size is fixed; 0x466180 is not recovered.
- **End** (0x4663d0): the unit's `fireball` explosion plays at its transform, the unit leaves the world, and
  `UnitDestroyed` is raised once. The `selfDestruct` variant of the explosion is not implemented. The model
  shatters into debris pieces (0x58cef0; `docs/rules/debris.md`).
- **Black-hole death:** a ship swallowed at a black hole's event horizon dies with a timer of 0 and no
  `fireball` (0x450a40, 0x466090; `docs/rules/forced-motion.md`, "Event horizon").

Every random draw goes through the combat world's seeded random source. A missing `systemexpl` sprite or
`fireball` is reported once and skipped; the unit still leaves on time.

## Coasting

A ship with no thrust, a derelict, a dying unit, or one whose engines are destroyed, takes the no-thrust branch of the game's
physics step (0x459440, checks 0x45969b-0x4596bf; ADR 0011). The branch tests the squared length of the
velocity vector (offset 0x140 of the physics object) against 0.01 (0x459c46). Below it the linear acceleration
(0x158) is zeroed (0x459c59), so the ship keeps that residual speed. Otherwise the linear acceleration is the
velocity normalized times the constant −2.0 at 0x5cc140 (0x459ca6-0x459cfe), with no heading term: the
velocity slows against its own direction. The angular acceleration (0x164) is zeroed at 0x459c0a, so the
pitch, yaw, and roll rates the ship had continue undamped. The yaw rate turns the heading. The roll angle wraps
at ±π, so a ship that was rolling keeps rolling over. The pitch angle is held within ±1.5 rad (0x459ea8-0x459ed8)
while its rate stays. The position moves by the velocity after the first half of the step's acceleration
(0x459f2e-0x459f6a: a half kick, the position, a second half kick), and the orientation is built from the three
angles (0x55d080). A coasting ship therefore travels a straight line while it turns. A turning derelict writes no
velocity (0x466ca0), so the ship keeps what it had. A ship that stands still when it loses thrust stays still. A
dying ship with working engines, not derelict, still sets its yaw rate from its roll every sub-step (bank-to-turn,
0x459579 ignores the dying flag), so its yaw follows its constant-rate roll; with its engines destroyed or
derelict it leaves the yaw rate alone. `docs/rules/flight-model.md` ("The physics step") has the whole step.

The coast starts from whatever speed the ship had. The speed cap is `impulseSpeed` times the warp speed
multiplier (0x450a80), and the stock physics definitions set `impulseSpeed` 20 and `warpSpeed` 50 to 100. That a
ship at warp moves at up to `warpSpeed` is **inferred**. At the brake of 2.0, a ship stops after speed / 2
seconds and speed² / 4 units: 10 s and 100 units from 20, 50 s and 2,500 units from 100. Black holes, beams, and
waves can still change a coasting ship's velocity (`docs/rules/forced-motion.md`).

No order, stop, face, or player move gives a coasting ship thrust. A ship whose engines are repaired and that
is not derelict takes orders again, and keeps the velocity and the rates the coast left it. The combat world's
tick that finds the unit without thrust runs the no-thrust branch, on the world's fixed tick, so positions do
not depend on frame rate. The combat world owns every mobile ship's flight from the moment the sandbox opens, and
the view draws each ship between its tick poses, ahead by its velocity and rates; the simulated pose stays on the
tick. A unit that starts without thrust, its engines destroyed or derelict, coasts from the first tick along its
layout's starting velocity. The physics step's pitch clamp holds the angle within ±1.5 while a coasting ship keeps
its pitch rate.

## Damage nodes

A ship model's `damage` group holds the nodes that show wear. The combat state decides each frame which show
(0x472120 calls 0x4677b0 for each ship it draws; no randomness), so a node turns off again on repair or recrew.
The inputs are the crew, the maximum crew, the race's `crewYellowStatus`, whether each system works, and the
ship's race; no hull or system hit points and no threshold enter. The sandbox decides and the view only toggles
nodes; the sprite nodes under a showing node play, and a node that does not show hides its subtree.

The game sets two flags on a node (node +0x58), which the scene walk reads (0x580110): bit 0 skips the node's own
mesh, sprite, or emitter, and bit 1 skips its children.

The class init (0x45fea0) finds seven nodes by whole name, ignoring case, with no prefix match (names at
0x600518..0x600548, lookup 0x57ff30): `crew`, `engines`, `life`, `sensors`, `shields`, `target`, and `borg`. The
stock models name the shield-generator node `shield`, so that node never binds, as in the game. The `borg` node
shows on a unit whose current race is the race named `borg` (0x4677b0 compares the unit's race name at +0x1a4 with
0x601240, looked up once into 0x639fcc), and is hidden with its subtree on any other unit. Only the stock models of
the other races hold art under it (the `assimbig`, `assimmedium`, and `assimsmall` sprites), for an assimilated ship.
The sandbox shows the node's subtree from the unit's current race name (INV-0014), so a unit placed as Borg shows it
from the start, a captured unit shows it while a Borg party's race holds, and a recapture hides it again. A model with
no damage group or no `borg` node shows nothing extra. **Stand-in**: the name comparison ignores case (the game's
string compare was not traced).

- **Crew chain:** the ratio r is crew over `maximumCrew` over the race's `crewYellowStatus` (0x444437,
  0x4677b0). From the `crew` node the walk follows each node's first child (node +0x1c). The node at depth k
  (the `crew` node is depth 0) shows while r + 0.2k is below 1 (constants 0x5ca758 and 0x5ccf30). The chain
  sets only bit 0, so a crew node that does not show still lets its other children draw. There is no depth limit
  in the code; r above 0 shows at most five nodes, and a ratio of 0 shows five. The game compares the
  ship's `maximumCrew` (ship +0x4c8) with 0 (constant 0x5ca16c) at 0x467803..0x46782a; when it is 0 the
  numerator is 1 (constant 0x5ca238) instead of crew over `maximumCrew`, and 0x467836 divides the result by
  the race's `crewYellowStatus` (race +0x204). The class load reads `maximumCrew` into the class
  (0x4601d7, class +0x228; ship init copies it at 0x4618eb) and defaults it to the class's `crewCost`
  (class +0x70). A unit whose maximum crew is 0, by an explicit `maximumCrew 0` or by no `maximumCrew` and a
  `crewCost` of 0, has r = 1 / `crewYellowStatus`, so it shows no crew node for any `crewYellowStatus` of 1
  or less. The sandbox's crew pool and crew-status ratio use a stand-in of the `crewCost`, at least 1, for a
  maximum of 0; the damage nodes read the game's own maximum.
- **System nodes:** the subtree of `engines`, `life`, `sensors`, `target`, or `shields` shows while the ship
  system does not work (its byte at system +0 is 0): engines, life support, sensors, weapons, and shield
  generator in turn (the system structures at 0x518, 0x588, 0x5c0, 0x550, and 0x4e0 of the ship). The system
  stops working when it is destroyed (0x467b70) and works again only at full hit points (0x467d40), so the
  subtree shows until the repair is complete. The game sets only bit 1, so the system node's own mesh always
  draws. All the stock damage emitters hang directly under `life` (steam) and `engines` (plasma).
- A model without a `damage` group, without a `crew` node, or with a differently named system node shows
  nothing for it. A race whose `crewYellowStatus` is 0 or less shows no crew node. A chain that loops stops at
  the first repeat.

**Stand-in**: the nodes are looked up among the direct children of the `damage` group, which holds for the stock
models; the game's search is not known beyond them. A node that does not show hides its whole subtree, and a
system node hides with its subtree; the stock crew chains have only first children and the stock system nodes
have no mesh, so the stock models look as in the game. The game also compares the ship's model with its normal
one before it shows anything (0x4677b0, first branch); the sandbox has only the normal model. A derelict has no
crew, so its crew chain shows.

## Damage emitters

An emitter node (SOD node type 12) under a damage node names an `@emitter` block of a sprite table (0x59b650,
0x58a960). The sprite table parser reads the block (0x599ab0) with these keys:

- `type`: `spray` (1), `cloud` (2), or `beam` (3). Only spray plays; the others are read and reported as not
  played (**stand-in**).
- `sprite`: the name of a `@sprite_node` line, which gives the particle's sprite, size, color, and node
  animation. A spray block with no `sprite` key is reported by the parser, and a name no table defines is reported; the
  emitter sprays nothing in either case.
- `spread_angle`: degrees. `expansion_rate`: world units per second, added to the sprite node's size. `velocity`:
  world units per second (0x5757b0). `lifespan`: seconds. `firing_period`: seconds between spawns.
- `pixel`, `particle_count`, and `cloud_radius` are read and unused (the use of `pixel` is not recovered).
  Any other key is reported, and the rest of the block still parses.

In the stock 1.2 install every emitter under a damage node is a spray: `plasmasml` to `plasmaxl` and `steamsml` to
`steamxl`.

**Spray** (0x575400): the timer (a 32-bit float) starts at the period (0x5753a0, and the copy 0x5754c0), so the
first particle comes one period after the emitter is made. Each step the timer counts down by the step's seconds
(0x575425). While the emitter is active and the timer is below 0, one particle spawns and the timer is set to the
period (0x57549f). The emitter is active from its creation (0x575840), and no code found turns it off. The
overshoot is not carried, so a step spawns at most one particle, a period of 0 means one per step, and a period of
0.1 s gives one particle every 0.12 s (about 8.3 per second) when float arithmetic leaves the timer a hair above 0
after 5 steps of 0.02 s. The game steps the timer by the frame's seconds. **Stand-in**: the sandbox steps it on the
fixed 0.02 s tick of simulated time (`CombatWorld.Tick`), so the rate is the same at any frame rate and step size.

**Particle** (0x5746d0): it starts at the emitter node's world transform, rotated by three angles, each
`spread_angle` times a draw uniform in −1 to 1 (0x501820: the remainder of the seed by 100001, less 50000, times
0.00002). The parser turns `spread_angle` from degrees into radians (0x599ab0, 1/360 at 0x5d43e0 times 2π). The
rotation (0x55d160) turns by the third angle about Z, then the first about X, then the second about Y, so the tilt
from +Z reaches at most acos(cos² `spread_angle`), about `spread_angle` times the square root of two for small
angles. It moves in the world along the node's local +Z at `velocity` (0x5757b0), so a moving ship leaves it
behind. It draws as a
billboard of its sprite node at the sprite node's size plus `expansion_rate` times its age. **Stand-in**: the sprite
node's animation plays once from the particle's birth and holds its last keyframe. A size at or below 0 is not drawn
(0x584040). It dies at `lifespan`.

The particles live in the ship model's own list (model instance +0x2c), not in the emitter. Each frame the
model draws (0x58cb60) moves and ages every particle by the frame's seconds and removes the dead ones (0x58b4f0),
then draws them (0x58b590). Then the scene walk (0x580110) steps each emitter whose node shows (slot 11, 0x5758e0),
which counts the timer down and spawns. An emitter under a hidden damage node therefore holds its timer and
sprays nothing, and the particles it sprayed before fly on and fade at their lifespan. When the node shows again,
the timer goes on from where it stopped. An emitter whose definition or sprite node is missing is reported once
and skipped; the model still loads.

**Stand-in**: the game steps neither the emitters nor the particles of a ship outside the view (0x58cb60 returns
when the frustum test 0x57b3f0 gives 1); the sandbox steps them always. 0x5758e0 also draws the model's whole
particle list after each spray step; what that adds on screen is not recovered, and the view draws each particle
once. The particles' random source is the play run's seed (INV-0008).

## Model scale

A class loads its model at the scale its definition's `scaleSOD` gives. The class load (0x46a470) reads the
value into the class (0x46b01d–0x46b031, class +0x1b0) with a default of 1 and passes it to both model loads
(0x46b046, 0x46b061). From there it reaches the SOD node reader (0x59b650) through 0x58a790 and 0x59c130.

- **Node positions:** the reader multiplies the position of each node's stored transform by the scale
  (0x59b746–0x59b764). The node's axes stay as stored, so the scale does not carry down the hierarchy through
  them.
- **Mesh vertices:** when the scale is not 1, the reader multiplies the vertices of each mesh node (type 1 or
  9, test at 0x595ef0) by the scale on all three axes (0x59ba49–0x59ba6e, through the mesh's 0x5910a0).
- **Sprite nodes:** the same call is empty for a sprite node (0x574cb0), so a sprite node moves with its
  scaled position and keeps the size its sprite table gives.

Together these scale the model uniformly about its origin. Everything the sandbox reads from the model sees
the scaled model: the selection and combat radius from its meshes, hardpoints, damage nodes, damage
emitters, sprite-node positions, and the shield ellipsoid. In the stock 1.2 install, 34 definitions set `scaleSOD`, most of them
`wingman` classes such as `fbattle` (1.4) and `romega` (0.8). The asset browser's model preview draws a
model at its stored size, since a model there has no class.

The lookup (0x553c30) takes the first line whose start matches the key, ignoring case, and that holds a `=`.
It reads the value with the format `= %g`, so `1.3f` gives 1.3 and `.8` gives 0.8. A definition's own line
comes before the lines of its includes. The sandbox matches the whole key, ignoring case, as for every other
key; every stock line that sets the scale spells the key in full.

**Stand-in**: a value that is no number gives a scale of 1 and is reported. The game keeps whatever its
scan leaves in the value. The animation channels of a model keep their stored positions; whether the game
scales them is not recovered, and the sandbox does not play them. The asteroid field definition `mafield`
sets 1.5; the sandbox draws its rocks at their stored size.

## Not known

- Yellow alert's extra validity check, and where yellow stance stops a unit moving (the idle path tests no stance).
- The label of the game setting at 0x632ec4 and the branch it takes in the attack maneuver (0x42dc30, 0x42d2b0), the
  point 0x4304e0 a target-location ship's dogfight turns toward without an ordered unit, and the retreat thresholds of 0x4292d0.
- Whether anything gives an attack back that the stop order of a no-path target ended (an AI or player re-issue, or the
  target field +0x1c4; `docs/rules/path-planning.md`, "The order list and the replans").
- What the `targetEnemyCraft` and `targetEnemyBuildings` keys do.
- The flight of pulse phaser shots and the class behind poleron torpedoes.
- The officer factor in repair, and the full-repair path at 0x468760.
