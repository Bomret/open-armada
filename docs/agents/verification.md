# Verification

```sh
dotnet tool restore && dotnet csharpier check . && dotnet test --project tests/OpenArmada.Tests && dotnet build OpenArmada.csproj && godot-mono --headless --path . -- --check --game "${ARMADA_DIR:?set ARMADA_DIR to an Armada install}"
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
the test files it works on; the run over a slice's whole changed-path set runs the whole suite.

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
  path), `project.godot`, `OpenArmada.csproj`, `global.json`, the play script, the layout's content,
  the map name, the frame rate, the Godot version, and the `ARMADA_DIR` path.
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
  stored. The lock holds its owner's pid, and a call takes over the lock of a dead owner. Remove a lock
  by hand only when no run is live. Only the call holding the takeover mutex
  `play-out/.lock-<stem>.takeover` removes a lock, after reading its pid again; a mutex left by a killed
  call is removed by hand too. An unwritable `play-out/` exits 2 with a message.

Movie Maker decouples simulated time from the wall clock. Each frame advances the game by exactly
`1 / <fps>` seconds, and the next frame starts as soon as the last one is written. A run's wall-clock
time is therefore its frame count times the cost of one frame: a cheap scene runs faster than real
time, and an expensive one slower. The recorded frames and audio play at real speed either way.

Verify each line with the smallest layout that shows it. A play run on a stock map is slow under
Movie Maker, and a long fight there can take hours.

- Place only the units the line needs, close together, so the opening camera shows them and no
  camera step is needed.
- Keep the script short, with few frames: wait only as long as the event needs.
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

A play run cannot show the selection display: the display draws only through the hovered unit, and a
play run has no hover point. Issue #106 tracks a `hover` step.

The checked-in play scripts, the layout or map each runs on, and the mechanics each shows:

| Script | Layout or map | Shows |
|---|---|---|
| `black-hole.play` | `layouts/black-hole.jsonc` | A derelict ship pulled in and swallowed; a ship with thrust ordered into the hole's impassable square, holding at its start; the hole's model turning |
| `borg-move.play` | `layouts/borg-move.jsonc` | A Borg ship's move order: its 3D velocity, no pitch or roll, its yaw toward the bearing plus a quarter turn |
| `coast.play` | `layouts/coast.jsonc` | A ship with destroyed engines coasting from its starting velocity |
| `collision.play` | `layouts/collision.jsonc` | Collision separation ending a move order; both ships at rest apart |
| `damage.play` | `layouts/damage.jsonc` | Damage nodes of a ship with a tenth of its crew |
| `destroy.play` | `layouts/destroy.jsonc` | A unit destroyed: dying, its explosions and fireball, its sound |
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
| `transport.play` | `layouts/transport.jsonc` | A destroyer ordered to transport (key, then click) boards and captures a crippled hostile ship; its team changes |
| `transport-feedback.play` | `layouts/transport-feedback.jsonc` | The transport effect on source and target while the transport runs and not after a cancel; the transport sound; the captured ship in its new team color beside a ship of its model in the old one |
| `transport-cases.play` | `layouts/transport-cases.jsonc` | The transport to a derelict ship (captured) and to a friendly ship (crew added); the hero ship's voice line at its order and a ship with none silent; the transport sound at each party |
| `special-weapon.play` | `layouts/special-weapon.jsonc` | A Borg cube's Holding Beam (F4, then a click) holds a moving ship's position and heading and drains the cube's special energy; the second key press turns it off and the ship drives on |
| `vent-fade.play` | `layouts/emitters.jsonc` | A damage emitter stopping once its system is whole |

`README.md`, "Run a play script", holds the step syntax and the dump fields. Never commit `play-out/`.

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
