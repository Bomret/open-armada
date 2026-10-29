# Open Armada glossary

## Game data

**Install**
A copy of Star Trek: Armada that the player owns, on disk. The engine reads all game data from it and
ships none of its own. The reference install is the final patch, version 1.2.
_Avoid_: game folder, game directory, assets folder

**Game file system**
The single view of an install's files that every loader reads through: loose files, archive contents,
and the override layer, looked up by name without regard to case.
_Avoid_: VFS, file layer, GameFiles

**Archive**
A `.zfs` container inside an install that bundles many files under one name table, some of them
obfuscated with a key stored in the archive itself.
_Avoid_: ZFS file, pack, package

**Index**
The `startrek.zix` file that names, for every game file, the folder or archive holding it.
_Avoid_: zix, file list

**Override layer**
The install's `addon` folder, whose files replace same-named files from the archives.
_Avoid_: patch folder, addon directory

**Shadowed copy**
A file of an install whose name another file wins in the game file system's lookup. An example is a stock
definition that the override layer replaces. The asset check reports a shadowed copy that no asset kind
reads as known but not loaded.
_Avoid_: duplicate, overridden file

**Asset kind**
One category of asset, such as models or textures, with its own way to list, load, and check its assets.
The asset browser offers one category per asset kind.
_Avoid_: asset type, loader

**Asset**
One loadable piece of game data in an install: a model, texture, sound, map, object definition, tech
tree, sprite table, interface configuration, font, cursor, or text table.
_Avoid_: resource, game file

**Object definition**
A text file (`.odf`) that sets the properties of one class of game object, such as a ship, weapon, or
race, and may inherit from another through an include.
_Avoid_: ODF file, unit file, config

**Resolved definition**
An object definition with its include chain and the override layer applied, giving one value per key.
_Avoid_: merged ODF, flattened ODF

**Tech tree**
A text file (`.tt`) that states which objects a race can build and what each requires.
_Avoid_: TT file, build tree

**Map**
A playable scenario (`.bzn`) with its objects, their positions and teams, plus its start positions
(`.mdf`) and overview image.
_Avoid_: level, scenario file, BZN

**Start position**
The place where one player begins on a map, read from the map's `.mdf` file. Only multiplayer maps have them.
_Avoid_: spawn, start cell, start point

**Overview image**
The picture of a map that the game's menus show, the map's `.bmp` file.
_Avoid_: minimap, map thumbnail

**Sprite node**
A sprite that a sprite table attaches to a model node by name. It has a size, a color, and an animation, and it faces the camera when the table says so. An explosion model holds only sprite nodes.
_Avoid_: node sprite, attached sprite

**Sprite table**
A text file (`.spr`) that names regions of textures as sprites and gives their blend mode and animation.
_Avoid_: SPR, sprite sheet definition

**Team color**
The color that marks which player owns an object, applied to a model's team-colored materials.
_Avoid_: player color, faction color

**Team-color slot**
One of the 27 named entries of the game's team-color table, such as `red` or `cerulean`. Each takes its
color from `teamcolor.odf`. A slot the file gives no valid color is black.
_Avoid_: team index, color index

**Physics definition**
The object definition (`*phys.odf`) that an object definition names in its `physicsFile` key. It holds the
movement values of a class, such as its speed and turn rate.
_Avoid_: phys file, physics ODF

**Unit**
A map object whose definition names a physics definition. A unit is a ship or a station.
_Avoid_: mobile unit, actor, entity

**Ship**
A unit whose definition does not set `is_starbase`. A ship moves when it is ordered.
_Avoid_: vessel, craft, mobile unit

**Station**
A unit whose definition sets `is_starbase`, such as a starbase, shipyard, or sensor array. A station does
not move.
_Avoid_: base, building, structure

**Warship**
A ship whose definition's include chain reaches one of the game's combat hulls: `battle.odf`,
`cruise1.odf`, `cruise2.odf`, `destroy.odf`, `scout.odf`, or `special.odf`. Construction ships and
freighters are ships but not warships.
_Avoid_: combat ship, armed ship, fighting unit

**Asteroid field**
A map object of class `asteroidfield` whose rocks the engine generates from the field's definition. The map
stores only the field, not the rocks.
_Avoid_: asteroid belt, rock field

**Game constants**
The named values that the install's `RTS_CFG.h` declares, such as the camera heights and the combat plane.
_Avoid_: RTS config, config header values, settings

**Combat plane**
The horizontal band between the `SHIP_COMBAT_FLOOR` and `SHIP_COMBAT_CEILING` game constants. The camera
looks at its middle height.
_Avoid_: play plane, ground plane, map plane

**Stand-in**
A value or behavior that the engine supplies itself because the install defines none, or because the
game's own behavior is not known. Each stand-in is documented as such where it is used.
_Avoid_: default, fallback, placeholder

## Combat

**Combat rules**
The game's rules that turn a weapon's fire into hits, shield loss, crew loss, system damage, and
destruction, recovered from the game's program and restated in our own words.
_Avoid_: damage model, combat formulas

**Hostile**
A unit of another team than the unit judging it, team 0 excepted. Team 0 is neutral and hostile to no one.
_Avoid_: enemy unit, opponent

**Weapon**
A definition a unit names in a `weaponN` key, which fires ordnance from the unit's hardpoints at a delay
and within a range. A beam weapon has class label `cannon`; a torpedo launcher has `launcher`.
_Avoid_: gun, turret, hardpoint

**Ordnance**
The definition a weapon names in its `ordName` key: what one shot does on a hit and how it is drawn.
_Avoid_: projectile, shot definition, ammo

**Alert stance**
A unit has one of three settings, red, yellow, or green. The setting decides whether the unit picks targets and moves on its own.
The player sets it with a key chord.
_Avoid_: alert level, stance, combat mode

**Attack order**
An order that names one hostile unit as the target of a unit with weapons. The unit moves toward the target
until one of its weapons is within that weapon's range of the target's center, measured from the weapon's
mount point on the model (see `docs/rules/combat-rules.md`, §Firing). Keep-closing past the closing distance is an
engine stand-in (see `docs/rules/combat-rules.md`, §Attack order). The order ends when the target is destroyed.
_Avoid_: attack command, focus fire

**Move order**
An order that sends a ship to a point of the combat plane. The ship drives to the point and the order ends
there. It also ends where the ship stands when a forced motion moves the ship while the ship is within its
physics definition's `tooCloseToTurn` of the point.
_Avoid_: move command, go order, waypoint

**Group move**
A move order given to several selected ships at once. The ships are laid on a grid of cells around the
clicked point, and each ship goes to its own cell, so no two share a point.
_Avoid_: formation move (the game's optional mode in which followers trail a leader), group order

**Tether point**
The point an idle unit stays near. A unit at any alert stance that a forced motion leaves at or beyond its
`tooCloseToTurn` from the point moves back to it. An idle unit at red closes on its pick only while its center
is within 300 units of the point, and moves back to it otherwise (see `docs/rules/combat-rules.md`, §Alert
stance). A move order sets it to the destination, and to where the ship stands when the order ends short of
it; a recent hit on the unit or a teammate nearby sets it to where the unit stands.
_Avoid_: home, anchor, leash

**Hold**
What an idle ship with thrust does at every alert stance: it stays near its tether point and drives back to it
when a forced motion leaves it at or beyond its `tooCloseToTurn` from the point.
_Avoid_: guard, station-keeping

**Pick**
The hostile unit that a unit at red or yellow alert stance chooses by itself as its weapons' target. A pick is
no order: the unit keeps it while it stays valid and within its search radius.
_Avoid_: auto-target, target lock

**AI team**
A team the game's own computer player controls, marked by a per-team flag the game sets outside its
executable. In the map sandbox no team is an AI team, because the player orders the units of every team.
_Avoid_: computer player, CPU team, enemy team

**Key chord**
A key plus the modifiers held with it: Shift, Control, or Alt. The install's `Input.map` names the chord as a binding.
_Avoid_: hotkey, shortcut, key combo

**Ship system**
One of the five parts of a unit that can be destroyed apart from the unit: engines, life support, weapons,
shield generator, and sensors. Each has hit points and repairs itself over time.
_Avoid_: subsystem, component

**Derelict**
A unit whose crew has fallen below one. All its systems are offline and its shields are down, and it
stays on the map until it is destroyed.
_Avoid_: dead ship, disabled ship, wreck

**Critical hit**
A hit that lands on no ship system and not on the hull. It makes the unit a dying unit.
_Avoid_: core breach, instant kill

**Dying unit**
A unit that a critical hit has doomed, or that a black hole has swallowed, and that has not yet left the
map. It fires nothing and is no valid target, and a dying ship coasts. A doomed unit shows small explosions
until its timer runs out, then its fireball plays and it leaves the map; a swallowed unit's timer is 0, so it
leaves on the next tick with no fireball and no small explosions.
_Avoid_: dead unit, destroyed unit, wreck

**Coasting**
The motion of a ship that has no thrust: a dying unit, a derelict, or a ship whose engines are
destroyed. It keeps its velocity and its turn rate and slows down at a fixed rate.
_Avoid_: drifting, gliding, inertia

**Forced motion**
Motion that something other than a ship's own thrust gives it: a black hole's pull, a push-out, or a
collision separation.
_Avoid_: knockback, impulse, drift

**Black hole**
A map object that pulls a ship whose engines are destroyed or that is derelict (dying or not) toward its
center, and swallows every living unit, ship or station, that reaches its event horizon. A dying ship whose
engines work is not pulled, although it coasts.
_Avoid_: gravity well, singularity

**Event horizon**
The circle of a black hole's `EventRadius` in the plane, around its center. A living unit, ship or station,
whose center lies inside it is swallowed and ends without a fireball. A hole whose `EventRadius` is 0 has none.
_Avoid_: core, kill radius

**Push-out**
The move that takes a ship out of an asteroid field or a planet (every ship that is not derelict and
not dying), or a ship with thrust out of the ring around a black hole, by position alone.
_Avoid_: bounce, repulsion

**Collision separation**
The move that takes two overlapping ships apart by position each tick, each by its share of their
speeds.
_Avoid_: collision response, bounce, avoidance (avoidance is the unit AI's steering)

**Debris piece**
One triangle of a unit's model that flies off and tumbles for a short, random time when the unit ends.
_Avoid_: chunk, shard, wreckage

**Shield hit effect**
The shield geometry that flashes around a unit for a moment when a hit lands while its shields are up,
turned toward the hit and colored from red to green by the shields left.
_Avoid_: shield flash, shield bubble, impact effect

**Shield ellipsoid**
The axis-aligned ellipsoid around a unit class's model that its shields fill: shots hit where they enter it,
and the shield hit effect is sized to it.
_Avoid_: shield bubble, shield sphere

**Shield-down overlay**
The shield geometry that stays on a unit while its shield generator is destroyed.
_Avoid_: shield collapse, shield-die effect

**Damage node**
A node in a ship model's `damage` group that shows wear: a crew node that shows as the crew falls, or a
system node that shows while its ship system is destroyed. Its sprite nodes and damage emitters run
only while it shows.
_Avoid_: damage layer, damage texture, damage decal

**Damage emitter**
An emitter node under a damage node that sprays short-lived sprites, such as venting plasma or steam,
by its sprite table's `@emitter` definition.
_Avoid_: particle system, smoke emitter

**Repair spark**
The `xspark` sprite that shows for a second at one of a system's target hardpoints, every `<system>SparkTime`
seconds while the system repairs.
_Avoid_: damage spark, system spark

**Crew status**
The state of a unit's crew against its race's crew levels: green, yellow at or below `crewYellowStatus`,
red at or below `crewRedStatus`. It slows weapons and repair, and it colors the crew dot.
_Avoid_: crew level, crew health

**Selection display**
The game's status display drawn in the world around a unit: the shield bar above it and the crew dot at
its corner. Only the hovered unit and the units of its team show it, and of those only a selected unit
shows the crew dot. A selected unit that is not in that group shows nothing.
_Avoid_: health bar, unit overlay, HUD

**Hovered unit**
The unit under the cursor, as the selection display uses it. It stays the hovered unit for a short hold
after the cursor leaves it.
_Avoid_: mouse-over unit, highlighted unit

**Display fade**
The selection display's opacity for the units of the hovered unit's team. It grows while the hovered team
stays the same and starts over when the hovered team changes.
_Avoid_: hover alpha, fade-in timer

**Screen box**
The smallest screen rectangle around a unit's projected bounding box. The selection display is placed
relative to it.
_Avoid_: bounding rectangle, screen bounds

## Tools

**Asset check**
The headless run of the engine that loads every in-scope asset of an install and reports each one that
fails, with its name and reason. It also lists the references that do not resolve and puts every file of
the install into one file class. It fails when any asset fails or any file is unclassified.
_Avoid_: SodCheck, validator, asset test

**File class**
The group that the asset check assigns to a file of an install. Loaded: an asset kind reads the file.
Out of scope: the file holds no game data the engine reads. Known but not loaded: the file is recognized
but no asset kind reads it, such as a shadowed copy. Unclassified: no rule matches the file, and the
asset check fails.
_Avoid_: file category, file status

**Asset browser**
The engine's interactive mode for picking any asset of an install and inspecting its preview.
_Avoid_: model viewer, asset viewer

**Map sandbox**
The engine's mode that opens one map as a live 3D scene. The player can select units, order ships to
move, and order units to attack. The fight plays out in the scene.
_Avoid_: map viewer, 3D preview, game mode

**Layout**
A text file, authored outside the install, that places objects for the map sandbox the way a map does.
It is not a map: the game cannot load it, and it has no start positions or overview image.
_Avoid_: scenario, test map, custom map

**Starting state**
The condition a layout object starts the fight in: its crew and shields as fractions of its class's
maximum, and its destroyed ship systems. A unit without one, and every unit of a map, starts at full
strength.
_Avoid_: initial damage, preset, handicap

**Preview**
The asset browser's rendering of one asset, showing that its meaning was understood: a model as the game
draws it, a map from above, a sound as playback, a definition as its resolved values.
_Avoid_: thumbnail, view

**Map summary**
The engine's text account of what one map holds: its group, its start positions, the objects of each team
with their classes, the teams that hold a warship, whether combat is possible at start, and its neutral and
monster units. The map preview and the `--maps` run show the same text.
_Avoid_: map report, map info, map scan

**Run**
One start of the engine in one mode (asset browser, map sandbox, asset check, or the `--maps` run of map summaries) until it quits.
_Avoid_: session, launch, execution

**Log**
The file `godot.log` in Godot's user-data folder that holds the log entries of the latest run beside
Godot's own lines. Godot keeps the logs of earlier runs as dated copies.
_Avoid_: log file, trace, diagnostics file, console output

**Log entry**
One line of the log that the engine writes: its local time, its level, its area, and its text. The level
is `ERROR` when something broke (an asset failed to load, the engine caught a fault, or a mode failed to
open), `WARN` when the engine went on by design with a stand-in, absent data, a skip, or an unsupported
asset, and `INFO` for context. The area names the part of the engine that wrote it.
_Avoid_: log message, log line, event

**Play run**
A run of the map sandbox that a play script drives on fixed simulated time, quitting after the script's
last step.
_Avoid_: replay, scripted session, demo

**Play script**
A checked-in text file of steps, one per line, run in order: waits, selections, orders, keys, camera
moves, named screenshots, state dumps, and expectations on unit state.
_Avoid_: scenario, macro, test script

**State dump**
The text lines a play run writes at one step, one line per unit, naming each unit as a play script
addresses it and giving its combat state.
_Avoid_: snapshot, state export
