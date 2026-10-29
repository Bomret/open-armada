# Combat rules

The rules by which the game turns weapon fire into hits, shield loss, crew loss, system damage, and
destruction (ADR 0004). They cover beam weapons (`classLabel` `cannon`), torpedo launchers (`launcher`),
shields, ship systems, crew, derelicts, and repair. Special weapons and boarding are not covered.

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
  point (in 3D, the square of the distance at most 90000, 0x42a3a9, the constant at 0x5ca7ac), it closes on its
  pick (the game's attack maneuver, 0x42c400, is not traced, so how it closes is a **stand-in**). Farther away,
  or with no pick, it moves back to the point and stops there (0x42a3e4, 0x42a3f4), keeping its pick, so its
  weapons fire at it whenever one reaches. The edge has no margin: a unit closing on a pick near 300 units turns
  back to the point as soon as it crosses the edge and closes again once back inside (**stand-in**; whether the
  attack maneuver smooths this is not traced). A move order sets the tether point to the destination (0x42704e), so
  a ship stays where it was sent; an order without a position, such as a stop, sets it to where the unit stands
  (0x46eac0). The point starts at the unit's placed position (**stand-in**; the game's point at creation was
  not found). When a player's attack order ends with its target destroyed, the point becomes where the unit
  stands then (**stand-in**). A move gives no height, so the point keeps the unit's own (**stand-in**). The
  engine stops the closing when the stance leaves red (**stand-in**); the return is the hold below, which no
  stance ends. A player's attack order
  keeps a derelict target.

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

**The hold.** A mobile ship with thrust, no move order, and no attack order holds its tether point at every
stance, with or without weapons: the idle handler (0x42a170) keeps calling the move maneuver on the point
(0x42e4a0, `docs/rules/forced-motion.md` "Move-order arrival and hold"). A push that leaves the ship at or beyond
its `tooCloseToTurn` from the point makes it drive back, and nearer it stays where it was moved. The return ends
on reaching the point, or by a push inside the radius, which makes that spot the tether point. Red's close on a
pick within 300 units of the tether point keeps precedence, and red's return after closing is the same hold. A
ship that regains thrust away from its tether point drives back to it (**stand-in**: the engine's return has no
brake, so a ship inside the radius does not move).

The install binds the stances to keys in `Input.map` (`green`, `yellow`, `red`).

## Target selection

A unit with no weapons picks no target. Otherwise, at red or yellow (0x42ff80):

1. The search radius is the longest range of the unit's weapons, less 25.
2. Distances are measured surface to surface: the distance between the centers less the radii of both
   units.
3. The current target is kept while it stays valid and within the radius.
4. Otherwise the nearest valid target within the radius is taken.

A target is valid when it is a hostile unit, not being destroyed (a dying unit is being destroyed), and not
derelict (0x42faa0). The game
also requires that the target is visible to the unit's team; every unit is visible until fog of war
exists (**stand-in**).

Selection makes no test of whether any weapon can reach the target: the validity check and the search
(0x42faa0, 0x4300a0) test no hardpoint, arc, or line of fire, and the distances are surface to surface
(0x430017, 0x4301d4; the 25 at 0x42ffd1). A weapon, however, fires only from a hardpoint within its range of
the target's center (see Firing), and nothing drops a pick the unit cannot hit (searched, not found). So a
unit that does not close on its pick, such as a yellow ship or a station, can hold a target with a large
radius whose center no hardpoint reaches, and fire nothing at it for as long as the target stays within the
radius. The game behaves the same way (**inferred** from the program's code; not observed in a running game).

## Attack order

An attack order (0x4294e0) names one target unit.

- The closing range is the longest range of the unit's weapons (0x429616). While the surface distance is
  more than the closing range less 50, the unit moves toward the target.
- Within that distance, the unit holds position and turns toward the target once at least one of its
  weapons has a hardpoint within the weapon's range of the target's center (see Firing). Until then it keeps
  moving toward the target, so a target with a large radius is not out of reach (**stand-in**; the game
  runs an attack maneuver that was not traced).
- The weapons fire at the ordered target only while it is within the longest weapon range. Until then
  they fire at the nearest valid target in range, as target selection picks it (0x42fd6f).
- The order ends, and the unit becomes idle, when the target is destroyed (0x429903). A dying target counts
  as destroyed. A derelict target keeps the order, so an attack order can finish off a derelict. A player's
  attack order on a dying unit is refused.
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
  when its velocity is 0; the game's test was not traced.
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
- **Sensors:** sensor range falls to `damagedScan` (**inferred**). Nothing reads sensor range until fog
  of war exists.

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
  `UnitDestroyed` is raised once. The `selfDestruct` variant of the explosion is not implemented. The game also
  shatters the model into debris pieces (0x58cef0; `docs/rules/debris.md`), which do not take the ship's
  velocity (`docs/rules/forced-motion.md`, "What does not move a ship").
- **Black-hole death:** a ship swallowed at a black hole's event horizon dies with a timer of 0 and no
  `fireball` (0x450a40, 0x466090; `docs/rules/forced-motion.md`, "Event horizon"). Its debris is the same as for
  any other ship. The engine raises `UnitDestroyed` with the cause `BlackHole`, which the view answers with no
  fireball, no sound, and no small explosions.

Every random draw goes through the combat world's seeded random source. A missing `systemexpl` sprite or
`fireball` is reported once and skipped; the unit still leaves on time.

## Coasting

A ship with no thrust, a derelict, a dying unit, or one whose engines are destroyed, takes the no-thrust branch of the game's
physics step (0x459440, checks 0x45969b-0x4596bf; ADR 0008). The branch tests the squared length of the
velocity vector (offset 0x140 of the physics object) against 0.01 (0x459c46). Below it the linear acceleration
(0x158) is zeroed (0x459c59), so the ship keeps that residual speed. Otherwise the linear acceleration is the
velocity normalized times the constant −2.0 at 0x5cc140 (0x459ca6-0x459cfe), with no heading term: the
velocity slows against its own direction. The angular acceleration (0x164) is zeroed at 0x459c0a, so the
pitch, yaw, and roll rates the ship had continue undamped. The yaw rate turns the heading. The roll angle wraps
at ±π, so a ship that was rolling keeps rolling over. The pitch angle is held within ±1.5 rad (0x459ea8-0x459ed8)
while its rate stays. The position moves by the world velocity at the middle of each step (0x459f2e-0x459f6a),
and the orientation is built from the three angles (0x55d080). A coasting ship therefore travels a straight
line while it turns. A turning derelict writes no velocity (0x466ca0), so the ship keeps what it had. A ship
that stands still when it loses thrust stays still.

The coast starts from whatever speed the ship had. The speed cap is `impulseSpeed` times the warp speed
multiplier (0x450a80), and the stock physics definitions set `impulseSpeed` 20 and `warpSpeed` 50 to 100. That a
ship at warp moves at up to `warpSpeed` is **inferred**. At the brake of 2.0, a ship stops after speed / 2
seconds and speed² / 4 units: 10 s and 100 units from 20, 50 s and 2,500 units from 100. Black holes, beams, and
waves can still change a coasting ship's velocity (`docs/rules/forced-motion.md`).

No order, stop, face, or player move gives a coasting ship thrust. A ship whose engines are repaired and that
is not derelict takes orders again. The coast starts at the combat world's tick that finds the unit without
thrust and runs on the world's fixed tick, so positions do not depend on frame rate. The combat world owns
every mobile ship's motion from the moment the sandbox opens, and the view draws each ship between its tick
poses; the simulated pose stays on the tick. A unit that starts without thrust, its engines destroyed or derelict,
coasts from the first tick along its layout's starting velocity. **Stand-in**: the position moves by the mean
velocity of each tick (the half-step integration detail is medium confidence).

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
shows on a ship whose race is the race named `borg` (0x601240, looked up once into 0x639fcc), and is hidden with
its subtree on any other ship. Only the stock models of the other races hold art under it, for an assimilated
ship. The sandbox keeps it hidden (**stand-in**: assimilation is not built, and the stock Borg models hold nothing
under it).

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
- `sprite`: the name of a `@sprite_node` line, which gives the particle's sprite, size, colour, and node
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

- The attack maneuver within range, and yellow alert's extra validity check.
- Whether a player's attack order drops a target that turns derelict. The game's side check (0x42fb90,
  `task+0x110`) suggests it may, but the writer of that field was not found; the rule above stays.
- What the `targetEnemyCraft` and `targetEnemyBuildings` keys do.
- The flight of pulse phaser shots and the class behind poleron torpedoes.
- The officer factor in repair, and the full-repair path at 0x468760.
