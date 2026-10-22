# Verification

```sh
dotnet tool restore && dotnet csharpier check . && dotnet test --project tests/OpenArmada.Tests && dotnet build OpenArmada.csproj && godot-mono --headless --path . -- --check --game "${ARMADA_DIR:?set ARMADA_DIR to an Armada install}"
```

```sh
dotnet test --project tests/OpenArmada.Tests
```

The full command first checks formatting. It restores the local tools and runs CSharpier's check.
The check exits non-zero and names each C# or project file that is not formatted.
`dotnet csharpier format .` fixes them. The first `dotnet tool restore` on a machine needs NuGet
access. The full command then runs the unit tests, builds the project, and runs the asset check: the
engine started headless, loading every asset of the user's own Armada install; images and sounds are
also decoded by Godot's own loaders. The asset check exits non-zero when any asset fails;
unsupported assets and `NOTE` lines (such as a texture whose mip chain ends at a level of the wrong
size) are listed and do not fail it. It needs `ARMADA_DIR` to point at that install; without it the
command fails with `set ARMADA_DIR to an Armada install`.

The focused command runs the unit tests alone. They build their fixtures in code and need neither an
install nor Godot.

## Sandbox Definition-of-Done lines

Verify a Definition-of-Done line about what a player sees or hears in the map sandbox with a play
run, and judge the result yourself. Do not ask the operator to play. Write a play script in `play/`
that sets up the situation and takes named screenshots, then run it on a layout under Movie Maker;
the folder must exist before the run:

```sh
rm -rf play-out/<stem> && mkdir -p play-out/<stem> && godot-mono --path . --write-movie play-out/<stem>/frame.png --fixed-fps <fps> -- --game "${ARMADA_DIR:?set ARMADA_DIR to an Armada install}" --layout layouts/<layout>.jsonc --play play/<stem>.play
```

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
- A layout object can start damaged: `crew`, `shields`, and `destroyedSystems` set its starting state.
  `README.md`, "Open a layout", has the keys.
- Pick the lowest fixed frame rate the line allows: 30 where the line needs the frames around a short
  visual event, lower, such as 10, for a line about unit state or a long fight.
- Read the named screenshots and the frames around them, and report which files show the thing.
- Measure a sound with the WAV level tool over its window and over a quiet window:
  `dotnet run --project tools/WavLevel -- play-out/<stem>/frame.wav --from <s> --to <s>`. A level clearly above the
  quiet window is the sound.
- Run the script twice and `cmp` the state dumps when the line needs a stable outcome. State dumps
  compare only between runs at one frame rate.
- `play/destroy.play` on `layouts/destroy.jsonc` is the worked example: a unit destroyed, its explosion
  drawn, its sound heard. The layout's header comment has its command.

A play run cannot show the selection display: the display draws only through the hovered unit, and a
play run has no hover point. A `hover` step is planned in triage #106.

`README.md`, "Run a play script", holds the step syntax and the dump fields. Never commit `play-out/`.

No timing command: the test runner's per-test durations are not wired in.

## Test paths

- `tests/`
- `test/`
- `*.Tests/`
- `*.Test/`
- `*.test.*`
- `*_test.*`
