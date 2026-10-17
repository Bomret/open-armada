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
  counts a unit with a move order as not idle until it arrives, **stand-in**) that can attack (`canAttack`) is given an attack
  order on the target it picks, or toward the position of a unit that recently hit it (0x42a24c,
  0x42a298). The engine ends such an order, and the walk toward an attacker, when the stance leaves red
  (**stand-in**). How long "recently" lasts is not known; the engine counts the last attacker until it is invalid
  (**stand-in**).
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

When a system is destroyed, its percent is shared evenly among the systems still working, or added to
the critical share when none works. A repaired system takes its own percent back (0x467600).

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

A unit starts with `crewCost` crew; a unit that starts below one is derelict from the start. When the
crew falls below one (0x466ca0), the crew is set to zero
and the unit becomes **derelict**: all its systems go offline and its shields go down. A derelict
unit regains no shields, loses no crew to life support, fires no weapon, and is no valid target for
target selection. It stays on the map.

The game can instead destroy a unit whose crew falls below one, by a flag whose source is unknown; the
engine always makes it derelict (**stand-in**).

A unit is destroyed only by a critical hit. Losing every system or the shields does not destroy it.

## Not known

- The attack maneuver within range, and yellow alert's extra validity check.
- What the `targetEnemyCraft` and `targetEnemyBuildings` keys do.
- The flight of pulse phaser shots and the class behind poleron torpedoes.
- The officer factor in repair, and the full-repair path at 0x468760.
