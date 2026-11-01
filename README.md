# Open Armada

An open-source engine for Star Trek: Armada (2000), built on Godot 4 with C#. It ships no game content: it reads the files of an Armada install the player owns.

Status: prototype. A map sandbox opens one map as a live 3D scene. An asset browser previews the assets of an install: models, textures, sounds, maps, object definitions, tech trees, sprite tables, interface data, fonts, and cursors. A headless asset check loads every asset and reports each failure.

## Requirements

- Godot 4.7.2 .NET build (`brew install --cask godot-mono`)
- .NET SDK 10 (pinned in `global.json`; the project targets `net8.0`)
- An installed copy of Star Trek: Armada, or its GOG installer unpacked as described below

[mise](https://mise.jdx.dev) can install the tools instead: `mise install` reads `mise.toml`, and
`mise tasks` lists the build, test, check, and run tasks. Put `ARMADA_DIR` in a gitignored
`mise.local.toml` under `[env]`.

## Use the GOG installer

The GOG installer is an Inno Setup file. `innoextract` unpacks it on macOS or Linux without running it:

```sh
brew install innoextract   # or your package manager on Linux
innoextract -m -d "/path/to/Star Trek - Armada" "setup_star_trektm_armada_1.2_rdna_fix_(52907).exe"
```

`-m` skips the installer's temporary files. The game files land at the root of the `-d` folder; pass that folder as `--game`. The asset check classifies the files GOG adds beside the game as out of scope.

A GOG install has no `splash.exe`, which holds the decoding key of the `.ldl` text tables. The asset check lists those tables as unsupported and does not fail on them.

## Run the asset browser

```sh
dotnet build OpenArmada.csproj
godot-mono --path . -- --game "/path/to/Star Trek - Armada" --model Fente
```

`ARMADA_DIR` can replace `--game`. The preview shows the asset selected in the category and asset lists. The browser scales its text and lists with the window. Images are scaled to fit the preview with their aspect ratio kept. In a model preview, drag or swipe with two fingers to orbit, and use the mouse wheel or pinch to zoom. `--model <name>` opens the named model. `--screenshot out.png` renders one frame to a file and quits.

A model preview also draws the model's sprite nodes. A sprite node is a sprite that the install's sprite tables attach to a model node by name. Examples are a running light and an explosion's fireball. Its node animation loops. The preview frames a model made only of sprite nodes, such as the explosion model `xplode`, by its sprites.

A model with damage art gets a damage bar above the view. The crew loss slider shows the crew damage sprites one step at a time, up to the four that the game shows. A checkbox per system shows that system's damage art, and "Assimilated" shows the model's `borg` art. Each control appears only for art the model can draw: a mesh, a spray, or a sprite the sprite tables define. The shown parts follow the game's damage node rules (`docs/rules/combat-rules.md`, "Damage nodes"). The steam and plasma sprays under a shown part play as in the sandbox.

## Run the map sandbox

```sh
godot-mono --path . -- --game "/path/to/Star Trek - Armada" --map 2duel
```

`--map <name>` opens a map from the install's `bzn` folder, matched without regard to case, folder, or extension. `docs/maps.md` lists the built-in maps with their teams and units. The scene holds the map's objects with their team colors, its background, and generated asteroid fields. Add `--screenshot out.png` to render one frame to a file and quit.

The sandbox behaves as follows:

- Camera: the arrow keys and the window edges scroll the camera. The mouse wheel changes its height. A middle-button drag rotates it. On a trackpad, a two-finger pan changes the height and rotates, and a pinch changes the height. The camera limits and rates come from the install's `RTS_CFG.h`.
- The sandbox confines the cursor to its window while the window has focus. Esc releases it until the window gains focus again.
- Left-click selects a unit, and a left-button drag selects the units in the box. Right-click on a hostile unit orders the selected units that have weapons to attack it; a unit without weapons ignores the click. Right-click elsewhere sends the selected ships to that point, or to the nearest point of the map's edge when the point lies outside the map. Stations do not move. Several ships make a group move, as the game's: each ship gets its own move order to a cell of a square grid around the point, `FORMATION_SPACING` (from `RTS_CFG.h`) apart, at the middle of the combat band. A move order ends when the ship is within its physics definition's `tooCloseToTurn` of the point in 3D and slower than half its `impulseSpeed`, also where a push leaves it so. A ship with no move order and no attack order holds its tether point: a push that leaves it at or beyond `tooCloseToTurn` from that point makes it fly back, and nearer it brakes to rest where it was moved. `docs/rules/forced-motion.md`, "Group move" and "Move-order arrival and hold", states the rules and their stand-ins.
- Ships move by the game's flight model (`docs/rules/flight-model.md`), not along a straight line at a fixed speed. Each combat tick of 0.02 s a ship's process writes its controls, and the physics step then integrates its velocity and its pitch, yaw, and roll from them: thrust against drag, damping of the sideways and vertical motion, and pitch and roll that spring back to level. A ship turns only by banking: its yaw rate is the bank's, so it rolls into a turn and its turning circle follows from its speed and its definition's `maxRoll` and `turnRadius`. A move order follows a path with a carrot `pathLeadDistance` ahead (the game's planned path: a quadtree of cell costs over the map, written by asteroid fields, belts, nebulae, black holes, and starbases, searched by the game's A* and straightened by its rubber banding; the two points [the ship, the point] across open space; a point in an impassable cell leaves the ship holding; a ship that makes no progress for 12 seconds replans; the planner's `RTS_CFG.h` keys take the game's default where missing and are reported once; `docs/rules/path-planning.md`), steers by the pitch and yaw servos, climbs or sinks toward the middle of the combat band, (`SHIP_COMBAT_FLOOR` + `SHIP_COMBAT_CEILING`) / 2, which is the plane the click lands on and the plane the camera looks at, and never climbs above the ceiling or sinks below the floor; within `tooCloseToTurn` of its point, or with the point inside its turning circle, it stops steering and a brake spring slows it. A ship takes the coefficients of `fastphys.odf` from its first move or attack on; its speed and its distances stay its own. A ship that is stopped, or has thrust and no order, brakes to rest and levels out. A ship without thrust coasts: its velocity slows at 2.0 per second per second until its speed is below 0.1, and its three rates keep spinning, so a rolling ship rolls over; a dying ship with working engines still turns from its bank. A ship that regains thrust keeps its velocity and rates. The speed that the dying timer, the to-hit stop modifiers, and the arrival test read is the length of the physics velocity; a ship is stopped below 0.1. A physics definition key it lacks takes the game's default and is reported once, except `rollRotationSpeed`, whose default of -1 would make the roll grow without bound and which takes the stand-in 5.5 (a value of 0 or less takes it too); a degenerate value is reported and takes the default as a stand-in, as do values whose coefficients leave the control gain's denominator at 0 up to 120 times the square root of 3, the longest velocity the velocity limit's reset leaves, so a pose is always finite. `yawRotationRate` is not read. Warp is not modelled. A ship with Borg physics flies by its own branch: a 3D velocity command moves it, a spring turns its yaw, and it never pitches or banks (`docs/rules/flight-model.md`, "The Borg branch"). An attacking ship moves by the game's attack state and dogfight (`docs/rules/combat-rules.md`, "Attack order"): it moves by path toward its target while the gap (the distance between centers less its radius and half the target's) exceeds its longest non-special weapon range less 50, then drives at the target until within that range, where it levels out and turns to face it while nearly still. A Borg ship does not turn to face, and a ship with a target-location weapon turns in place toward its target.
- Obstacle avoidance: with `OBSTACLE_AVOIDANCE` on, a ship's steering direction passes through the game's avoidance scorer, which looks `4 × speed` seconds ahead, steers around the ships, stations, and planets in its way, and holds the ship back in front of an asteroid field it would hit. The keys come from the install's `RTS_CFG.h`: `OBSTACLE_AVOIDANCE` (compiled default on; the stock file sets 0, so the stock sandbox has no avoidance), `COLLISION_LOOK_AHEAD_TIME` (default 4.0, stock 1.4), and `COLLISION_SAFETY_MARGIN` (default 1.5, stock 1.2), the last two for the field prediction. A key that is missing or no finite number takes the game's default and is reported once; a unit's `avoidMe`, `avoidanceClass`, and `collisionRadius`, and a field's `collisionRadius`, that are no number, or not finite, are reported once per class. `docs/rules/obstacle-avoidance.md` states the rules and their stand-ins.
- The unit under the cursor shows the game's shield bar above it, and the other units of its team show their bars at the display fade. A selected unit among them also shows a crew dot at its lower right corner. A selected unit outside them shows neither. The bar shades from green to red as the shields fall, and the dot turns yellow and then red as the crew falls to the race's crew levels. The display stays for 0.4 seconds after the cursor leaves the unit. `docs/rules/selection-display.md` states the rules.
- A ship that overlaps an asteroid field or a planet is moved straight out in the plane, to the obstacle's radius plus its own model radius; derelict and dying ships enter unhindered. `docs/rules/forced-motion.md`, "Push-out", states the rule and its stand-ins.
- A black hole is a placed object whose definition's `classLabel` is `blackhole_obj` (the stock `mblkhole`), on a map or in a layout. Its model turns about the vertical axis. It pulls in every ship without thrust within its `GravityRadius`, holds every ship with thrust outside a ring around its center, and swallows every unit within its `EventRadius`: the unit leaves on the next tick, with no fireball and no sound, and shatters into debris. A missing or invalid key of its definition takes the game's default and is reported once as a gap; the stock `mblkhole` lacks `GravityForce`, so every map with a hole shows that gap. `docs/rules/forced-motion.md`, "Black hole", states the rules and their stand-ins.
- A nebula is a placed object whose definition's `classLabel` is `nebula_obj` (the stock `mnebula1` to `mnebula5`), on a map or in a layout. The plan reads its position, `effectRadius`, and `pathing_cost` and draws nothing for it. A ship with thrust whose 50-unit cell (cells start at the map's bounds) has its center within `effectRadius` of the nebula, measured in 3D from the plane y = 0, flies under the speed cap of the nebula's kind: the class named `mnebula<kind>`, kind 5 for any other class name, gives `craft_speed_coefficient` (default 0.5) and `craft_speed_limit` (default none). Only the forward speed is capped (a coefficient of 0.6 gives 0.6 of the full speed), each kind applies once however many nebulae overlap, several kinds apply the lowest, and a coasting ship or a ship with Borg physics is not slowed. A nebula whose `effectRadius` is missing or no number is reported as a gap and left out. The plan also reads each asteroid field object as a site, and a field that names a path of type 3 as an asteroid belt along that path, for the path planner; a belt whose path the map lacks is reported as a gap. `docs/rules/nebulae.md` states the rules and their stand-ins.
- The green, yellow, and red alert keys set the alert stance of the selected units. The keys come from the install's `Input.map` (Shift+G, Shift+Y, and Shift+R in the reference install).
- Units and other models draw their sprite nodes, as the model preview does. The rocks of an asteroid field draw theirs too. A critical hit makes a unit a dying unit: it stays on the map for a few seconds, coasts if it is a ship, fires nothing, takes no orders, is no valid target, and shows small explosions. Then the unit shows its explosion at its last position and shatters into debris pieces that fly off, tumble, and disappear. The install's `ART_CFG.h` sets the debris limits; a missing or invalid value takes the game's default and is reported once. `docs/rules/combat-rules.md` states the dying rules and `docs/rules/debris.md` the debris rules, with their addresses. The point of a small explosion and its size are stand-ins.
- A torpedo or pulse that enters the shield ellipsoid of a unit with its shields up hits there and flashes the shield hit effect on that side of the unit, sized to the ellipsoid. The effect is colored from red to green by the shields left, unlit and additive, and its texture plays a ring that grows and fades. It lasts the ordnance's `shieldDuration`. The effect does not appear when the ordnance sets `ignoreShield`, even with the shields up. A beam's shield hit effect appears once a tenth of its lifetime has passed and its line enters the shield, turns with the beam's contact, and ends after its `shieldDuration` even while the beam goes on. A beam on a unit without shields, or whose ordnance sets `ignoreShield`, shows a hull hit sprite instead. A unit whose shield generator is destroyed shows the shield-down overlay until the generator works again or the unit turns derelict. `docs/rules/shield-effects.md` states the rules with their addresses.
- A ship model shows its damage nodes by the unit's state: crew nodes as the crew falls, and a system's nodes while that system is destroyed. The damage emitters under a showing node spray venting plasma or steam. The state dump's `damage=` field lists the showing nodes.
- Fire and explosion sounds fade with their distance from the camera by the game's fade, read from the install's `audio.odf`: full level within the minimum distance, quieter beyond it, and cut off beyond the maximum distance (2000 units in the stock install). An explosion keeps full level to twice the minimum distance. A sound that starts beyond its maximum distance is never heard. A playing sound follows the camera and its ship, goes silent while the camera is beyond its maximum distance, and is heard again once the camera returns. A missing or invalid `audio.odf` value takes the game's default and is reported once. `docs/rules/sound-fade.md` states the rules with their addresses; the fade formula is inferred.
- A map object or definition that cannot be read is reported as a gap, printed and counted on screen, and the map still opens.

These rules apply to every map the sandbox opens.

### Open a layout

```sh
godot-mono --path . -- --game "/path/to/Star Trek - Armada" --layout layouts/two-ships.jsonc
```

`--layout <file>` opens the map sandbox from a layout instead of a map. A layout is a JSONC file, authored outside the install, that places objects the way a map does (ADR 0007). A relative path resolves against the directory the command is started in. `ARMADA_DIR` can replace `--game`, and `--screenshot out.png` works as it does with `--map`. `--layout` together with `--map`, or `--layout` without a file, is a problem, and no scene opens. `--check` wins over `--layout`. The sandbox opens with exactly the layout's objects and behaves as it does on a map.

A layout is one JSON object. `//` and `/* */` comments and trailing commas are allowed. Any key not listed below is a problem, and so is a key given twice in one object. A vector is `[x, y, z]`, three numbers, in the map's own frame.

- `bounds` (required): the playable area on the ground, `{ "minX": 0, "maxX": 4000, "minZ": 0, "maxZ": 4000 }`. Each minimum is below its maximum. An object outside the bounds opens as it does on a map.
- `objects` (required): an array of objects, which may be empty. Each object has these keys:
  - `class` (required): the object's class name, such as `fdestroy`, as a map names it. It is not empty. A class the install has no definition or model for is reported as a gap, as on a map, and the rest of the layout opens.
  - `team` (required): a whole number from 0 to 8. Team 0 is neutral.
  - `position` (required): a vector.
  - `label`: a string. Default: the class name.
  - `right`, `up`, `front`: the object's axes, three vectors, as a map stores them. Give all three or none. Default: the identity facing (`[1, 0, 0]`, `[0, 1, 0]`, `[0, 0, 1]`). Zero or collapsed axes are reported as a gap, as on a map.
  - `crew`, `shields`, `destroyedSystems`: the object's starting state, so a play run can begin mid-fight without a long fight first. `crew` is a fraction from 0 to 1 of the class's `crewCost`, `shields` a fraction from 0 to 1 of its `maxHealth`, and `destroyedSystems` an array of distinct names from `Engines`, `LifeSupport`, `Weapons`, `ShieldGenerator`, and `Sensors`, spelled as a play script's `expect` spells them. Default: full strength. The unit starts with each listed system destroyed at 0 hit points, its hit percent handed to the systems still working; then with the crew fraction of `crewCost` exactly; then with the shields fraction of `maxHealth`. A crew below one crew member starts the unit derelict, with crew 0 and shields 0. From there the combat rules run unchanged: a destroyed system repairs itself, and the shields regain while the shield generator works. A system the class does not have, the keys on an object that is no unit, and a `shields` above 0 on a unit that starts derelict are each reported as a gap that names the object, and the rest of the layout opens. Every unit of a map starts at full strength.
  - `velocity`: the world velocity the object starts with, a vector in units per second in the frame of `position`. It is allowed alone or together with `crew`, `shields`, and `destroyedSystems`. A unit that starts with its engines destroyed or derelict coasts from the first tick along it and brakes as a coasting ship does (2.0 units per second per second); the state dump's `vel=` shows it. A `velocity` on a unit with thrust, on a station or another unit that cannot move (a ship whose physics definition cannot be read included), or on an object that is no unit is reported as a gap that names the object, and the rest of the layout opens. A `velocity` that is not three finite numbers is a layout problem naming the key's path. Maps carry none. Default: at rest. `layouts/coast.jsonc` and `play/coast.play` show a ship with destroyed engines coasting from its starting velocity.
- `background`: the name of the background model, with or without `.sod`. Default: no background.
- `teamColors`: the team-color slot of each team, keyed by team number, such as `{ "1": 3, "2": 5 }`. Default: each team's own slot. A slot outside the install's table is reported as a gap.
- `ambient`: the ambient light color, a vector of red, green, and blue. Default: a stand-in of `[0.35, 0.35, 0.4]`.

Every number is finite. A layout that cannot be read gives one problem that names the full path of the file and either the 1-based line or the JSON path of the faulty value, such as `layout '/home/me/a.jsonc': objects[2].team: team 9 is outside 0..8`. The sandbox opens no scene and shows the problem. A layout problem is reported with or without an install.

`layouts/two-ships.jsonc` is a checked-in example: two hostile destroyers 1000 units apart. `layouts/destroy.jsonc` holds the warship and the sensor station of `play/destroy.play`. `layouts/damage.jsonc` holds the two destroyers of `play/damage.play`, one with a tenth of its crew. `layouts/sound-fade.jsonc` holds the units of `layouts/destroy.jsonc` on a wider map, for `play/sound-fade.play` and `play/sound-fade-start.play`. `layouts/emitters.jsonc` holds the two destroyers of `play/emitters.play`, one with its engines and life support destroyed so that its damage emitters spray.

## Run a play script

A play run opens the map sandbox on a map or a layout, runs the steps of a play script in file order on simulated time, and quits after the last step. It runs under Godot's Movie Maker, which writes a PNG per frame and one WAV of the mixed audio and forces a fixed time step. Clear the output folder before a run, and create it: Movie Maker writes its files into it from the first frame, and the run itself deletes nothing there. A run whose movie output folder is missing is a problem.

```sh
rm -rf play-out/duel && mkdir -p play-out/duel
godot-mono --path . --write-movie play-out/duel/frame.png --fixed-fps 30 -- --game "/path/to/Star Trek - Armada" --layout layouts/two-ships.jsonc --play duel.play
```

`sh tools/play-run.sh <stem> (--layout <file> | --map <map>) --fps <n> [--fresh]` builds `OpenArmada.csproj`, runs the same command with `ARMADA_DIR` as `--game` (a failed build exits 3 and runs no Godot), stores the result under a hash of the run's inputs, reuses it while they are unchanged, and logs each call to `play-out/runs.log`. `docs/agents/verification.md` describes it.

A play run on a stock map is slow under Movie Maker. `docs/agents/verification.md` says how to keep a run short.

`play-out/<script name>/` holds the frames, the WAV, and the state dumps of the run; the script name is the script's file name without its extension. Git ignores `play-out/`. The run creates the folder for a dump or a screenshot when it is missing.

`--play <file>` is only accepted under Movie Maker (`--write-movie` with `--fixed-fps`), with `--map` or `--layout`, and without `--screenshot`. The layout path resolves as it does without `--play`; the play script path resolves against the project folder. Anything else, a `--play` without a file, another flag without a value (such as a bare `--game`, even with `ARMADA_DIR` set), `--map` together with `--layout`, a map or layout that cannot be opened, and a missing movie output folder are problems: each is one `ERROR` entry, and the run quits with exit code 1 and no window. A flag without a value and `--map` together with `--layout` are the run's only entries then; the launch rules above add none. Four problems read `play run cannot start: <problem>`: a flag without a value (including a bare `--play`), `--map` together with `--layout`, a launch rule above that the run breaks, and a missing movie output folder. An example is `play run cannot start: --game needs a value`; another mode logs the same flag problem as a `WARN` entry without the prefix. A map or layout that cannot be opened keeps the text that the sandbox logs for it. A problem in the play script keeps the text that the script reader logs for it.

A play script is a text file with one step per line. Blank lines and lines that start with `#` are skipped. Steps:

- `wait <seconds>` lets that much simulated time pass. The combat runs meanwhile.
- `dump <name>` writes a state dump to `play-out/<script name>/<name>.txt`.
- `expect <unit> <field> <op> <value>` checks one value of a unit's state. The fields are `shields`, `crew`, a system's hit points (`Engines`, `LifeSupport`, `Weapons`, `ShieldGenerator`, or `Sensors`), `destroyed` (`true` or `false`; `true` once the unit has left the map), `dying` (`true` or `false`; `true` from the critical hit until the unit leaves), `target` (a unit name or `none`), and `team` (the unit's current team, an integer). The operators are `<`, `<=`, `=`, `>=`, and `>`; `destroyed`, `dying`, `target`, and `team` take only `=`. A number compares to the unit's value exactly, so use `>=` or `<=` for a value that changes.

The input steps reach the sandbox as Godot input events through its own handlers, so they take effect only where a player's click or key would:

- `select <unit>` left-clicks the unit.
- `attack <unit>` right-clicks the unit; the selected units get an attack order. The step fails, sending no click, when no unit is selected or the unit is not hostile to every selected unit (the click would be a move order for such a unit).
- `move <x> <z>` right-clicks the point of the combat plane at that position (the coordinates of the `pos=` field of a state dump); the selected ships move there, several of them as a group move (see "Run the map sandbox"). A box step takes the selection in the scene's unit order, which the layout's object order fixes.
- `box <left> <top> <right> <bottom>` (four numbers; any other count of numbers is a problem at parse time) drags the selection box between two points in viewport pixels (press, motion, release).
- `box <unit> [<unit> ...]` drags a box around the named units. It selects exactly them; when the box would take another unit too, the step fails.
- `press <key>` presses and releases a key in one frame. `hold <key> <seconds>` presses it, lets that much simulated time pass as a `wait` does, and releases it; a polled key, such as an arrow key that scrolls the camera, stays down across the frames, also when the window loses focus meanwhile: the run presses it again, as an echo, before each frame's steps until the release. A handler that acts only on a fresh, non-echo press, such as the alert keys, ignores the echo and acts once for a hold. A key is named as Godot names it (`Left`, `Right`, `Up`, `Down`, `Escape`, `A`); a chord names its modifiers first, joined with `+` (`press Shift+G` sets green alert in the reference install). A chord carries its modifiers on the key's own event; it does not press the modifier key itself. The modifiers are spelled as Godot spells them on the platform: `Shift` and `Ctrl` everywhere, `Option` and `Command` on macOS, `Alt` and `Windows` on Windows, `Alt` and `Meta` elsewhere. A misspelled modifier, such as `Control` or `Alt` on macOS, is a problem. Key steps and mouse steps of one frame reach the handlers in file order.
- `scroll <up|down> [count]` turns the mouse wheel by that many notches (default 1), which changes the camera height.
- `turn <pixels>` drags the middle button that many pixels to the right (negative: left) from the middle of the viewport, which turns the camera.
- `shot <name>` writes the last drawn frame to `play-out/<script name>/<name>.png`. A step that changes the view takes effect in the frame after it, so put a `wait` before the shot.
- `shot <name> <unit> [scale]` writes only the crop of that frame around the unit, enlarged by the whole-number `scale` (default 1) with each pixel repeated. The crop is the unit's screen box (the box around its model's meshes, seen through the camera) grown past each edge by half its longer side, so a fireball or shield flash around the hull stays in it, and clipped to the frame. A dying unit keeps its box, and a destroyed unit keeps the box of its last frame, so a crop follows its explosion; a dying or destroyed unit whose model no longer shows a mesh keeps the box of the last frame it showed one in. A unit that shows no mesh and has no such box, whose box reaches behind the camera, or whose box lies wholly off screen is a problem, and so is a crop larger than 16384 pixels on a side once enlarged.

A step aimed at a unit or a point sends its events at its projected screen point, in viewport coordinates, at any display scale. The step fails, sending no event, when the unit is off screen or behind the camera, when a click would pick another object than the named unit (an occluded `attack` never becomes a move order), or when a `move` point lies on a unit. In a play run the real mouse neither confines to the window, nor scrolls the camera at the screen edge, nor moves the selection display's hover, and the arrow keys scroll the camera whichever window has focus.

A unit is named by its map label, or by its class when the label is empty. When several units share a name, `name#k` is the k-th of them in map order, and the bare name is a problem. So is a label that reads like such a name (`ship#2` beside two units named `ship`); the state dump lists that unit with its label in quotes. A unit that is destroyed leaves the fight: only `expect <unit> destroyed`, `expect <unit> dying`, and `shot <name> <unit>` can still name it, and every other step that names it is a problem. A dying unit is still on the map, but no click picks it, so a `select` or `attack` step on it fails.

A state dump has one line per unit, destroyed units included, in map order, and a last line `debris=` with the number of live debris pieces. The fields are separated by spaces: the unit's name, `team=`, `pos=` (x,y,z), `vel=` (x,y,z: the unit's world velocity in the frame of `pos=`, whose length is its speed; `0.00,0.00,0.00` for a unit that stands still or has no combat state), `pitch=`, `yaw=`, and `roll=` (the unit's orientation as the game's angles in radians: a positive pitch lowers the nose, a positive yaw turns the bow from the game's +z toward +x, which is the sandbox's -Z toward +X, and a positive roll raises the right side; a unit shows the angles of its placed orientation until it moves, and `0.00` rather than `-0.00` where an angle rounds to zero; `docs/rules/flight-model.md`, "The angle convention", has the composition), `shields=`, `crew=`, one `<System>=<hit points>/<maximum>/<intact or destroyed>` for each system the unit has (in the order Engines, LifeSupport, Weapons, ShieldGenerator, Sensors), `derelict=`, `immobile=`, `coasting=` (`true` for a ship that has no thrust, derelict, dying, or with destroyed engines, and coasts), `destroyed=` (`true` only once the unit has left the map, so the line of a destroyed unit stays), `dying=`, `alert=`, `order=`, `target=` (a unit name or `none`), `move=` (`x,z` of the player's move order while it is open, else `none`; a hold's return and red's closing show `none`), and `shieldDown=` (the state's shield-down overlay flag: `true` while the unit's shield generator is destroyed and it is not derelict). A destroyed unit keeps the values of its last state in every field. A unit without combat state shows `state=none` after its angles. Numbers have two decimals and no thousands separator, whatever the system's culture, and a dump holds no time of day.

Each unit line of a state dump that has combat state (not `state=none`) ends with `damage=`: the names of the damage nodes of the unit's model that the unit's state selects, comma-separated in the model's node order, or `none`; a destroyed unit keeps the nodes of its last state. A ship model's `damage` group shows exactly the nodes the combat state decides each frame (0x4677b0, called from 0x472120; no randomness), and the sprite nodes and damage emitters below a showing node play and spray. The nodes are found among the direct children of the group by whole name, ignoring case: `crew`, `engines`, `life`, `sensors`, `shields`, `target`. The stock models name the shield-generator node `shield`, so it never matches, as in the game. The crew node and the nodes below it, each the first child of the one before, show by the ratio r of crew over `maximumCrew` over the race's `crewYellowStatus` (0x444437; `maximumCrew` defaults to `crewCost`, and a unit whose maximum is 0 has crew over `maximumCrew` of 1, so it shows none): the node at depth k shows while r is below 1 − 0.2k, so five nodes show at most. A race whose `crewYellowStatus` is 0 or less shows none. The subtree of the `engines`, `life`, `sensors`, `target` (weapons), or `shields` (shield generator) node shows from the system's destruction until it is back at full hit points (0x467b70, 0x467d40). A model without a `damage` group, a `crew` node, or a system node shows nothing for it. The `borg` node stays hidden.

The combat random source is seeded with a fixed number and the asteroids animate on frame time, so two runs of one map or layout and one script at one frame rate write byte-identical dumps (INV-0008). Runs at different frame rates can differ: a step takes effect at a frame and the asteroids animate on frame time. The combat itself runs fixed ticks of 0.02 s and carries the rest of a frame to the next, so the outcome of each tick does not depend on the frame rate (`FrameRateTests`).

Each problem is one `ERROR` log entry that names the script and the line, and, for a failed `expect`, the expected and the actual value. A problem is a script that is missing, a line that cannot be read, a unit that cannot be found, a failed expectation, an input step that fails as above, throws, or names a key or a modifier Godot does not know, or a dump or screenshot that cannot be written. The run then quits with exit code 1. A run that finishes quits with exit code 0. Closing the window before the run finishes logs one `ERROR` entry, `play run closed before its last step`, and quits with exit code 4.

`play/destroy.play` is the worked example. On `layouts/destroy.jsonc`, a Vor'cha-class warship destroys a sensor station; the script takes screenshots before the fight, while the station is dying, and across the explosion, dumps the state before, during, and after, expects the station dying and then destroyed. Run it with `--layout layouts/destroy.jsonc`; the layout's header comment has the whole command.

The other checked-in play scripts. Each script's header comment describes the run and names its map or layout.

| Script | Map or layout | Shows |
|---|---|---|
| `black-hole.play` | `layouts/black-hole.jsonc` | A derelict ship pulled in and swallowed; a ship with thrust ordered into the hole's impassable square, holding at its start; the hole's model turning |
| `borg-move.play` | `layouts/borg-move.jsonc` | A Borg ship's move order: its 3D velocity, no pitch or roll, its yaw toward the bearing plus a quarter turn |
| `coast.play` | `layouts/coast.jsonc` | A ship with destroyed engines coasting from its starting velocity |
| `collision.play` | `layouts/collision.jsonc` | Collision separation ending a move order; both ships at rest apart |
| `damage.play` | `layouts/damage.jsonc` | Damage nodes of a ship with a tenth of its crew |
| `drift.play` | `layouts/drift.jsonc` | A dying bird of prey coasting on |
| `emitters.play` | `layouts/emitters.jsonc` | Damage emitters spraying plasma and steam |
| `forced-motion.play` | `layouts/forced-motion.jsonc` | Black hole pull and swallow and collision separation in one run |
| `group-move.play` | `layouts/group-move.jsonc` | A group move: each ship's own cell |
| `hit-explosion.play` | `layouts/hit-explosion.jsonc` | System hit explosions at a hit's node |
| `map-black-hole.play` | `--map borg4` | A stock map with a black hole and asteroid fields |
| `move.play` | `layouts/move.jsonc` | A move order while driving and after arrival |
| `nebula.play` | `layouts/nebula.jsonc` | A ship slowed in a row of nebulae and its path bending out of the row |
| `path-field.play` | `layouts/path-field.jsonc` | A move order's path around an asteroid field |
| `push-out.play` | `layouts/push-out.jsonc` | Push-out from an asteroid field |
| `push-out-planet.play` | `layouts/push-out-planet.jsonc` | Push-out from a planet |
| `scale.play` | `layouts/scale.jsonc` | A model drawn at its `scaleSOD` |
| `shield.play` | `--map kling4` | Shield hit effects and the hull hit sprite |
| `shield-hit.play` | `layouts/shield-hit.jsonc` | Shield hit effects of beams and torpedoes on both sides |
| `shield-hull.play` | `--map kling1` | The hull hit sprite on a unit without shields |
| `shield-red.play` | `--map kling4` | Shield hit colors as shields fall; the shield-down overlay |
| `sound-fade.play` | `layouts/sound-fade.jsonc` | A fireball fading as the camera pans away, silent beyond its maximum distance, heard again on return |
| `sound-fade-start.play` | `layouts/sound-fade.jsonc` | A fireball that starts beyond its maximum distance, never heard |
| `sparks.play` | `layouts/emitters.jsonc` | Repair sparks of repairing systems |
| `vent-fade.play` | `layouts/emitters.jsonc` | A damage emitter stopping once its system is whole |

## Measure the sound of a play run

The WAV level tool prints the peak and RMS level of a Movie Maker WAV, in dB relative to full scale, over a window of it. It needs only the .NET SDK.

```sh
dotnet run --project tools/WavLevel -- play-out/destroy/frame.wav --from 5.5 --to 6.5
```

```text
peak -23.4 dB, RMS -34.3 dB
```

`--from` and `--to` are seconds from the start of the WAV; `--from` defaults to 0 and `--to` to the end. A silent window reads `peak -inf dB, RMS -inf dB`. A problem prints `error: <message>` to standard error and exits with code 1: the file is unreadable, unfinished, or not 32-bit PCM (an extensible format is not accepted), or the window lies outside the audio. Movie Maker's WAV is 32-bit PCM.

## Read the log

Every run writes log entries to Godot's own log, `godot.log`, beside Godot's own lines. The same entries go to standard output. The log of the latest run is:

| System | Path |
|---|---|
| macOS | `~/Library/Application Support/Godot/app_userdata/Open Armada/logs/godot.log` |
| Linux | `~/.local/share/godot/app_userdata/Open Armada/logs/godot.log` |
| Windows | `%APPDATA%\Godot\app_userdata\Open Armada\logs\godot.log` |

Godot keeps the logs of earlier runs beside it as dated copies (`godot<date>T<time>.log`) and deletes the oldest. A run's first entry names the absolute path of its log.

An entry is one line:

```text
2026-10-17T19:08:38.710+02:00 INFO run: ...
```

The line holds the local time (ISO 8601, with milliseconds and the UTC offset), the level, the area, and the text. Text with line breaks becomes one line per text line.

Levels (INV-0005):

- `ERROR`: something broke. An asset failed to load, the engine caught a fault, or a mode failed to open.
- `WARN`: the engine went on by design, with a stand-in, absent data, a skip, or an unsupported asset.
- `INFO`: context, such as the run start and each selection.

Areas: `run` (start, end, launch arguments), `check` (the asset check), `maps` (the map summaries of `--maps`), `browser`, `sandbox`, `combat`, and `render` (models and their textures).

Each run starts with an `INFO` entry that names the mode, the install, and the arguments. A run that quits normally ends with an `INFO` entry that counts its `WARN` and `ERROR` entries; the asset check's and the `--maps` run's also give the exit code. A crash or SIGINT writes no run end; Godot's backtrace stays in `godot.log`. To list the problems of a run, search the log for ` WARN ` and ` ERROR `.

A fault that repeats, such as a combat step that fails each frame, is logged at its first occurrence, and its further occurrences are counted in one entry before the run ends. A texture that fails is logged once per run: `WARN` when it is missing, `ERROR` when it cannot be read. A later use by a named asset adds an `INFO` entry that points back to the first one.

The asset browser logs what it shows. Each category and each asset shown is one `INFO` entry. Each note of a shown asset is one `WARN` entry `<asset>: <note>`, written each time the asset is shown. A preview that fails is one `ERROR` entry with the asset and the reason. A sprite table that an interface configuration needs and that fails to load is one `ERROR` entry per run. A later, different configuration that needs it adds an `INFO` entry that points back to the first one.

## Layout

- `src/Formats/` — parsers for the original file formats, plain C# without Godot dependencies.
- `src/Diagnostics/` — `Log` and its log entries; plain C# without Godot dependencies.
- `src/Rendering/` — converts parsed data into Godot nodes.
- `src/Viewer/` — the asset browser scene script, one preview per asset kind, and the map sandbox scene. The scene handles selection, the selection display, orders, alert keys, and the drawing and sound of the fight.
- `src/Sandbox/` — the map sandbox's plans, camera rig, flight model, path planner, obstacle avoidance, and nebulae, plus its combat. The combat part holds the combat plan read from the install, the combat world and its rules, and the art of the fight. The play part holds the play script parser, the unit names, the state dump, and the play run. The map summary part holds the map summary and the `--maps` run. The folder is plain C# without Godot dependencies. `docs/rules/combat-rules.md` states the rules.
- `src/Assets/` — the game file system (case-insensitive lookup in an install), the asset kinds, the asset check, and the layout reader; plain C# without Godot dependencies.
- `layouts/` — the checked-in layouts ("Open a layout"). Its `.gdignore` keeps Godot from importing the folder.
- `play/` — the checked-in play scripts. The "Run a play script" section explains how to run one.
- `tools/WavLevel/` — the WAV level tool ("Measure the sound of a play run"), plain C# with no Godot dependency.
- `tools/play-run.sh` — the play-run wrapper ("Run a play script"): runs a play script under Movie Maker and reuses the stored result while its inputs are unchanged.
- `tools/focused-tests.sh` — the focused test command (`docs/agents/verification.md`): the test classes of the changed test files, or every unit test.
- `tools/GhidraDump/` — the Ghidra scripts and Python tools that build the local Ghidra project and dump of `Armada.exe` in the gitignored `decomp/`, apply the program index's names to the project, and rebuild the generated parts of `docs/rules/program-index.md`. Its `README.md` explains the runs.
- `tests/OpenArmada.Tests/` — unit tests, run with `dotnet test --project tests/OpenArmada.Tests`. Fixtures are built in code.

C# is formatted with CSharpier: run `dotnet tool restore` once, then `dotnet csharpier format .` to format and `dotnet csharpier check .` to check.

## Check an install

```sh
godot-mono --headless --path . -- --check --game "/path/to/Star Trek - Armada"
```

Loads every asset and logs each failure with its name and reason. The report is log entries, on standard output and in the log (see "Read the log"); each line keeps its marker word (`FAIL`, `UNCLASSIFIED`, `NOTE`, `UNRESOLVED`, `UNSUPPORTED`, `MISSING`) at the start of its text. Classifies every file of the install and lists references that do not resolve. Exits non-zero when any asset failed or any file is unclassified. Assets that are known but unsupported (`Wviracod.SOD`) are listed apart and do not fail the check. A SOD with any other unrecognised version is corrupt and fails it.

## Summarize the maps of an install

```sh
godot-mono --headless --path . -- --maps --game "/path/to/Star Trek - Armada"
```

Use it to find a map with the right setup for a sandbox test on any install, for example warships on two teams for a combat test. `docs/maps.md` gives the same facts for the stock 1.2 install.

The run writes one log entry per map in the install's `bzn` folder, in name order, on standard output and in the log (see "Read the log"). Every line of an entry starts with the map's name, which is the name that `--map` takes. The entry holds the map summary:

- the group: multiplayer for a map with a `.mdf`, else campaign and other;
- the number of start positions;
- the teams 1 to 8 that hold a warship;
- combat at start: `yes`, `no`, or `unknown` when only unresolved classes could make it `yes`;
- the neutral and monster units;
- the unresolved classes, only when the map has any;
- the objects per team, with their classes.

The entry is `WARN` for a map with an unresolved class, and `INFO` for every other map. A map that cannot be read gives one `ERROR` entry with its reason, and the run continues. A map in a format the reader does not read, such as a text-form map or a save game, gives one `WARN` entry with its reason instead, as in the asset check. The run exits 0 once the install opens. It exits 1 when no install is given or the install cannot be opened. `--check` wins over `--maps`, and `--maps` wins over `--map`, `--layout`, and `--play`. The asset browser's map preview shows the same summary.

## SOD format notes

Version 1.8 follows the official Storm3D spec. The older versions were decoded from the shipped files:

| Version | Difference from 1.8 |
|---|---|
| 1.7 | Animation references have no time offset. |
| 1.6 | Also: a texture material table (name, texture, 7 unknown bytes) precedes the lighting materials; meshes carry one identifier that names a table entry. |
| 1.4, 1.5 | Also: no animation reference section. |
| 1.2 | Also: no animation sections. A mesh has one lighting material, counts in the order vertices/faces/texcoords, an unknown float, and faces with their own corner count. No cull byte. |
| 1.0 | Unsupported. Only `Wviracod.SOD` uses it, and the game does not reference it. |

The cull byte decides whether a mesh hides its back faces. `Armada.exe` stores the byte per mesh when it loads the file (0x590d0a–0x590d1b), and the renderer it sets up at 0x59dd90 defers to that byte. Only the value 1 culls back faces, in software (0x5a3de0). Every other value, 0 and any other non-zero value, draws both sides (0x5a3d70). The Direct3D cull mode stays off throughout (0x581de0). The stock files hold only 0 and 1.

The mesh trailer after the cull byte is a count followed by that many 16-bit vertex indices. The spec says the count is always 0; `Zentity.SOD` has 7.

Storm3D is left-handed. The builder mirrors Z and reverses triangle winding.
