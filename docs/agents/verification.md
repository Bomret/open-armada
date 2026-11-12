# Verification

```sh
dotnet tool restore && dotnet csharpier check . && dotnet test --project tests/OpenArmada.Tests --filter 'TestCategory!=Install&TestCategory!=Godot' && dotnet build OpenArmada.csproj && godot-mono --headless --path . -- --check --game "${ARMADA_DIR:?set ARMADA_DIR to an Armada install}"
```

```sh
sh tools/focused-tests.sh {paths}
```

The full command first checks formatting (`docs/agents/toolchain.md` has the formatter commands); a
failed check names each unformatted file. It then runs the unit tests, builds the project, and runs the asset check: the
engine started headless, loading every asset of the user's own Armada install; images and sounds are
also decoded by Godot's own loaders. The asset check exits non-zero when any asset fails;
unsupported assets and `NOTE` lines (such as a texture whose mip chain ends at a level of the wrong
size) are listed and do not fail it. It needs `ARMADA_DIR` to point at that install; without it the
command fails with `set ARMADA_DIR to an Armada install`.

The focused command runs the unit tests the changed paths need. They build their fixtures in code and
need neither an install nor Godot. A changed test file runs the test classes it holds, through
`dotnet test --filter`. Any other changed path except Markdown runs every unit test, since nothing maps a
source file to the tests that cover it. Markdown alone runs no test. A red-green step therefore passes only
the test files it works on; the run over a slice's whole changed-path set runs the whole suite. A changed test
file that holds only integration tests selects no test, and the command passes.

The command finds a test file's classes from each `[TestClass]` line to its class declaration, across any attribute
lines between, and selects them with `--filter "FullyQualifiedName~OpenArmada.Tests.<Class>."`. Keep the trailing
dot: without it `PlayRun` also selects `PlayRunInputTests`. `dotnet test` exits 8 when the filter selects no test.
The focused command ignores that exit, so a class holding only integration tests passes; a fail-to-pass command must
not ignore it, since a filter that selects nothing would pass both runs.

Both commands run the unit tests only. They skip the integration tests: the tests with the MSTest category
`Install` or `Godot`, through the filter `TestCategory!=Install&TestCategory!=Godot`. The category names are the
constants of `tests/OpenArmada.Tests/Needs.cs`. Put `[TestCategory(Needs.Install)]` and `[TestCategory(Needs.Godot)]`
on every test that needs the install or Godot, on the class where every test in it does. `NeedsTests` pins the
filters of `mise.toml` and `tools/focused-tests.sh` to `Needs`.

## Integration command

```sh
mise run test:integration
```

It runs only the tests of the categories `Install` and `Godot`. `CheckedInPlayRunTests` runs every checked-in play
script through the play-run wrapper and fails on an exit status other than 0: a failed `expect`, a step that cannot
run, a failed build. Its table holds each script's wrapper arguments (layout or map, frame rate), and
`CheckedInPlayTests` fails where the table misses a script in `play/`. The wrapper reuses a stored result whose
inputs hash the same, so a run on unchanged code starts no Godot. A test whose `ARMADA_DIR` is unset, or whose Godot
(`PLAY_RUN_GODOT`, which `mise` sets) does not run, reports inconclusive.

Neither the full command nor the focused one runs these tests. Run the integration command before a pull request that
changes the sandbox, a play script, or a layout. A new play script gets its row in the table.

## Sandbox Definition-of-Done lines

Verify a Definition-of-Done line about what a player sees or hears in the map sandbox with a play
run, and judge the result yourself. Do not ask the operator to play. Write a play script in `play/`
that sets up the situation and takes named screenshots, then run it on a layout under Movie Maker
through the play-run wrapper, from the repository root:

```sh
sh tools/play-run.sh <stem> --layout layouts/<layout>.jsonc --fps <fps>
```

The wrapper clears `play-out/<stem>/`, builds `OpenArmada.csproj` with `dotnet build`, and runs the
Godot command `README.md`'s "Run a play script" gives, with `ARMADA_DIR` as `--game` (`--map <map>` in
place of `--layout` for a stock map). It stores the result under a hash of the run's inputs in
`play-out/.cache/<hash>/` and reuses it on the next call whose inputs hash the same: no Godot run, the
stored dumps, named screenshots and WAV copied back into `play-out/<stem>/`, the stored `WARN`/`ERROR`
lines printed under a `reused` line, and the stored exit status. Only a run that exits 0 or 1 is stored.
`--fresh` always builds, runs Godot, and replaces the stored result. A failed build prints the build
output, starts no Godot, stores nothing, and exits 3. A run whose window is closed before its last step
exits 4 and is not stored. Godot does not build C# itself, so without the build step it runs stale code,
or none.

- The hash covers every tracked or untracked, not ignored file under `src/` and `scenes/` (content and
  path), `project.godot`, `OpenArmada.csproj`, `global.json`, `Directory.Build.props`, the play script,
  the layout's content, the map name, the frame rate, the Godot version, and the `ARMADA_DIR` path.
- It leaves out the install's content, `.godot/`, and the display. After a change to the install, run
  with `--fresh`.
- A fix cycle runs only the play scripts its findings map to. The scope review runs every script the
  scope's Definition of Done needs once, at the tip.
- Each call appends one tab-separated line to `play-out/runs.log`: UTC time, input hash, `HEAD`,
  `clean` or `dirty` (an untracked file counts), stem, `layout=<file>` or `map=<map>`, `fps=<n>`,
  `exit=<status>`, and `run`, `reused`, or `build` (a failed build). A line proves a run at a tip when
  its `HEAD` equals the tip and it reads `clean`.
- Bad arguments exit 2 with a message, and run, store, and log nothing.
- Calls for one stem run one at a time: a second call waits on the lock directory
  `play-out/.lock-<stem>`, says so once on stderr, then hashes its inputs and reuses what the first
  stored. The lock holds its owner's pid, and a call takes over the lock of a dead owner, and of a lock still without a pid after 5 one-second checks of the
  waiting call (the count restarts when a pid appears or the lock is replaced). Remove a lock
  by hand only when no run is live. Only the call holding the takeover mutex
  `play-out/.lock-<stem>.takeover` removes a lock, after reading its pid again; a mutex left by a killed
  call is removed by hand too. An unwritable `play-out/` exits 2 with a message.

Movie Maker decouples simulated time from the wall clock. Each frame advances the game by exactly
`1 / <fps>` seconds, and the next frame starts as soon as the last one is written. A run's wall-clock
time is therefore its frame count times the cost of one frame: a cheap scene runs faster than real
time, and an expensive one slower. The recorded frames and audio play at real speed either way. Most of a
frame's cost is Movie Maker's PNG encoding, which grows with what the frame shows: a frame of a small layout
encodes in about 20 ms, one of a stock map or a screen-filling planet in 150 to 700 ms.

Verify each line with the smallest layout that shows it. A play run on a stock map is slow under
Movie Maker, and a long fight there can take hours.

- Place only the units the line needs, close together, so the opening camera shows them and no
  camera step is needed.
- Keep the script short, with few frames: wait only as long as the event needs.
- Reach a later state with `fast-forward until <unit> <field> <op> <value> max <seconds>` instead of a long `wait`:
  it runs the combat world without drawing, so its time costs no frames. It shows and plays nothing of the skipped
  time, so keep a `wait` across what the line is about.
- Leave to the unit tests a line about unit state that a unit test reaches, such as a rule of the flight model, a
  push, or an arrival. A play run on unit state alone is for what only the player's input decides, such as the orders
  a click or a box selection gives (`group-move.play`, `path-field.play`, `borg-move.play`, `nebula.play`); a play run
  otherwise checks what a player sees or hears.
- Use a stock map (`--map <map>`) only when the line is about that map.
- Look through the checked-in layouts in `layouts/` first, and reuse one that fits.
- Write a new layout whenever one serves the check better. Put it in `layouts/` and commit it with the
  play script that uses it.
- A layout for one very specific situation only may be a scratch file outside the repository. Never
  commit it.
- A layout object can start damaged: `crew`, `shields`, and `destroyedSystems` set its starting state, and
  `velocity` sets the velocity it starts with. `README.md`, "Open a layout", has the keys.
- Pick the lowest fixed frame rate the line allows: 30 where the line needs the frames around a short
  visual event, lower, such as 10, for a line about unit state or a long fight.
- Take a shot of a unit as a crop around it, `shot <name> <unit> [scale]`, whenever the line is about that unit:
  a crop costs a fraction of a whole frame's image tokens. Enlarge it with a `scale` of 2 or more when the thing is a
  few pixels wide, such as a spark. Take a whole frame only when the line is about the whole view or several units
  apart.
- Read the named screenshots and the frames around them, and report which files show the thing.
- Measure a sound with the WAV level tool over its window and over a quiet window:
  `dotnet run --project tools/WavLevel -- play-out/<stem>/frame.wav --from <s> --to <s>`. A level clearly above the
  quiet window is the sound.
- Run the script twice and `cmp` the state dumps when the line needs a stable outcome: copy the first
  run's dumps out of `play-out/<stem>/`, then run again with `--fresh`, since a plain second call
  reuses the first result. State dumps compare only between runs at one frame rate.
- `play/destroy.play` on `layouts/destroy.jsonc` is the worked example: a unit destroyed, its explosion
  drawn, its sound heard. The layout's header comment has its command.

A play run shows the selection display through its play pointer, which a `hover` step puts on a unit.
`README.md`, "Run a play script", gives how long a script waits after a hover before a shot shows the
team's bars and the crew dots.

The checked-in play scripts, the layout or map each runs on, and the mechanics each shows:

| Script | Layout or map | Shows |
|---|---|---|
| `black-hole.play` | `layouts/black-hole.jsonc` | A derelict ship pulled in and swallowed; a ship with thrust ordered into the hole's impassable square, holding at its start; the hole's model turning |
| `borg-move.play` | `layouts/borg-move.jsonc` | A Borg ship's move order: its 3D velocity, no pitch or roll, its yaw toward the bearing plus a quarter turn |
| `coast.play` | `layouts/coast.jsonc` | A ship with destroyed engines coasting from its starting velocity |
| `damage.play` | `layouts/damage.jsonc` | Damage nodes of a ship with a tenth of its crew |
| `destroy.play` | `layouts/destroy.jsonc` | A unit destroyed: dying, its explosions and fireball, its sound |
| `drift.play` | `layouts/drift.jsonc` | A dying bird of prey coasting on |
| `emitters.play` | `layouts/emitters.jsonc` | Damage emitters spraying plasma and steam; repair sparks of repairing systems; a damage emitter stopping once its system is whole |
| `group-move.play` | `layouts/group-move.jsonc` | A group move: each ship's own cell |
| `hit-explosion.play` | `layouts/hit-explosion.jsonc` | System hit explosions at a hit's node |
| `map-black-hole.play` | `--map borg4` | A stock map with a black hole and asteroid fields |
| `nebula.play` | `layouts/nebula.jsonc` | A ship slowed in a row of nebulae and its path bending out of the row |
| `path-field.play` | `layouts/path-field.jsonc` | A move order's path around an asteroid field |
| `scale.play` | `layouts/scale.jsonc` | A model drawn at its `scaleSOD` |
| `shield-hit.play` | `layouts/shield-hit.jsonc` | Shield hit effects of beams and torpedoes on both sides |
| `shield-hull.play` | `layouts/destroy.jsonc` | The hull hit sprite on a unit without shields |
| `shield-red.play` | `layouts/shield-red.jsonc` | Shield hit colors by shield fraction; the shield-down overlay |
| `sound-fade.play` | `layouts/sound-fade.jsonc` | A fireball fading as the camera pans away, silent beyond its maximum distance, heard again on return |
| `sound-fade-start.play` | `layouts/sound-fade.jsonc` | A fireball that starts beyond its maximum distance, never heard |
| `transport-feedback.play` | `layouts/transport-feedback.jsonc` | The transport effect on source and target while the transport runs and not after a cancel; the transport sound; the captured ship in its new team color beside a ship of its model in the old one |
| `transport-cases.play` | `layouts/transport-cases.jsonc` | The transport to a derelict ship (captured) and to a friendly ship (crew added); the hero ship's voice line at its order and a ship with none silent; the transport sound at each party |
| `special-weapon.play` | `layouts/special-weapon.jsonc` | A Borg cube's Holding Beam (F4, then a click) holds a moving ship's position and heading and drains the cube's special energy; the second key press turns it off and the ship drives on |
| `special-beams.play` | `layouts/special-beams.jsonc` | A Borg cube's Holding Beam and a Borg Assimilator's Auto-Assimilator each show their beam model between ship and target while they run, play their fire sound once at the start, and show no beam once ordered off |
| `borg-assimilation.play` | `layouts/borg-assimilation.jsonc` | A Borg cube's Holding Beam boards a Klingon destroyer through its shields until the parties outnumber its crew; the destroyer ends on the cube's team with the race `borg` |
| `auto-assimilator.play` | `layouts/auto-assimilator.jsonc` | A Borg Assimilator's Auto-Assimilator (F3, then a click) drains a Klingon destroyer's crew into its own and the team's crew pool until the destroyer is derelict; the destroyer keeps its team and race |
| `hover.play` | `layouts/hover.jsonc` | The play pointer and the selection display it hovers; `README.md`, "Run a play script", lists the cases |

`README.md`, "Run a play script", holds the step syntax and the dump fields. Never commit `play-out/`.
The integration command runs every script in the table and checks its exit status. It does not judge the frames,
the shots, or the WAV.

No timing command: the test runner's per-test durations are not wired in.

## Complete selector

The focused command selects every test a changed path can affect.

- `tools/focused-tests.sh`

## Test paths

- `tests/`
- `test/`
- `*.Tests/`
- `*.Test/`
- `*.test.*`
- `*_test.*`

## Prepare command

`dotnet tool restore`
