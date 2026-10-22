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
  engine stops the closing and the return when the stance leaves red (**stand-in**). A player's attack order
  keeps a derelict target.

  A recent hit moves the tether point. When a shot landed less than one second ago (0x46e640 compares the
  time since the hit, recorded by 0x46e340, against 1.0; strictly less) on the idle unit itself or on a unit
  of its own team within 400 units of it on X and on Z (0x42a000; whether the edge counts is a **stand-in**),
  the idle path posts an order without a position (0x42a370), which sets the tether point to where the unit
  stands (0x46eac0; that this order takes that path is **inferred**). So a ship under fire beyond its tether
  keeps closing. Only a landed shot counts as a hit (**stand-in**). A hit that destroys the teammate it lands
  on counts for no one, since the destroyed unit leaves the world at once (**stand-in**; whether the game
  counts it is not known). What else this order (command type 5)
  does, and the check at 0x42a370 that can skip it, are not traced.

  The game's idle path branches on whether the unit's team is an **AI team**: a per-team flag (`FUN_00472100`
  reads it from the team's record; the getter is `IsAITeam`, 0x436840) that something outside the program
  sets. Only an AI team's idle unit walks toward the position of a unit that hit it less than a second ago
  (0x42a298) or takes the attack path behind the unit definition's flag at +0x237 (0x42a24c). No team in the
  map sandbox is an AI team, because the player orders the units of every team, so neither is built.
- **Yellow:** the unit picks targets by itself and fires at them, and never moves on its own. The game
  makes one more validity check on yellow candidates whose meaning is unknown; the engine skips it
  (**stand-in**).
- **Green:** the unit picks no target by itself. It fires only at a target an attack order gives it
  (0x42ffac).

The install binds the stances to keys in `Input.map` (`green`, `yellow`, `red`).

## Target selection

A unit with no weapons picks no target. Otherwise, at red or yellow (0x42ff80):

1. The search radius is the longest range of the unit's weapons, less 25.
2. Distances are measured surface to surface: the distance between the centers less the radii of both
   units.
3. The current target is kept while it stays valid and within the radius.
4. Otherwise the nearest valid target within the radius is taken.

A target is valid when it is a hostile unit, not being destroyed, and not derelict (0x42faa0). The game
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
- The order ends, and the unit becomes idle, when the target is destroyed (0x429903). A derelict target
  keeps the order, so an attack order can finish off a derelict.
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
  stopped, plus `toHitStopModifier` when the target is stopped. The slow modifiers are not used.
- The shot hits when a uniform random number from 0 to 1 is at most the chance.

A missed shot is aimed by the miss path (0x50aab0), which leads the target and offsets the aim by the target's
radius; 0x50a7c0 is the aim of a shot that hits. The missed shot hits nothing. The sandbox aims a miss beside
the target without the lead (**stand-in**).

## Shots

The ordnance definition's `classLabel` decides how a shot travels.

- **Beam** (`phaser`): the beam runs from the hardpoint to the target for `lifeSpan` seconds and deals its
  damage once, when that time ends (0x4eb47e). It deals no damage when the firing unit is gone or the
  target is more than twice the weapon's `range` away before then (0x4eb2e5). `shotSpeed` plays no part.
- **Torpedo** (`photontorpedo`, `plasmacannon`): the torpedo flies at `shotSpeed`, turning toward its
  target by `omegaTurn` and `seekTime`, and hits on contact with its target, or with the target's shield
  bubble while the shields are up (0x4ed46b). It deals no damage when `lifeSpan` ends first (0x4ed174),
  so a fast target can outrun it.
- **Poleron torpedo** (`polerontorpedo`) flies as a torpedo, and **pulse phaser** (`pphaser`) flies
  straight at `shotSpeed` and hits on contact (**stand-in** for both).

A flying shot touches a unit whose model has no meshes (radius 0) within 5 world units of its center, so
such a unit can be hit (**stand-in**; the game's collision radius is not known).

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
   - A **critical hit** of at least one whole point destroys the unit.
6. When the shields reach zero, they go down for `shieldDelay` seconds (0x465719).

## Shields

The shield maximum is the definition's `maxHealth`; a unit has no hull hit points (0x46d9ef). A unit
starts with full shields.

While the shield generator works and the unit is not derelict, the shields regain `shieldRate` per second
up to the maximum (0x466b00). After the shields went down, they regain nothing until `shieldDelay` has
passed. A hit that leaves the shields above zero does not restart that delay. A destroyed shield
generator stops the regain and leaves the current shields as they are.

## Ship systems

Each of the five ship systems has the definition's `enginesHitPoints`, `lifeSupportHitPoints`,
`weaponsHitPoints`, `shieldGeneratorHitPoints`, and `sensorsHitPoints`, plus `SYSTEM_HITPOINTS_BONUS`
for a system that has any (0x460f8c). A system with zero hit points does not exist on that unit.

A system at zero hit points is destroyed (0x467b70). The crew loses 1 plus the system's `<system>CrewLoss`
percent of the crew, unless the unit is derelict. A destroyed system means:

- **Engines:** the unit cannot move (**inferred**), and every shot at it hits.
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

A unit is destroyed only by a critical hit. Losing every system or the shields does not destroy it.

## Not known

- The attack maneuver within range, and yellow alert's extra validity check.
- Whether a player's attack order drops a target that turns derelict. The game's side check (0x42fb90,
  `task+0x110`) suggests it may, but the writer of that field was not found; the rule above stays.
- What the `targetEnemyCraft` and `targetEnemyBuildings` keys do.
- The flight of pulse phaser shots and the class behind poleron torpedoes.
- The officer factor in repair, and the full-repair path at 0x468760.
