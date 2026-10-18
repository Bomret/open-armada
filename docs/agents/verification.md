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
that sets up the situation and takes named screenshots, then run it under Movie Maker; the folder
must exist before the run:

```sh
rm -rf play-out/<stem> && mkdir -p play-out/<stem> && godot-mono --path . --write-movie play-out/<stem>/frame.png --fixed-fps 30 -- --game "${ARMADA_DIR:?set ARMADA_DIR to an Armada install}" --map <map> --play play/<stem>.play
```

- Read the named screenshots and the frames around them, and report which files show the thing.
- Measure a sound with the WAV level tool over its window and over a quiet window:
  `dotnet run --project tools/WavLevel -- play-out/<stem>/frame.wav --from <s> --to <s>`. A level clearly above the
  quiet window is the sound.
- Run the script twice and `cmp` the state dumps when the line needs a stable outcome.
- `play/destroy.play` is the worked example: a unit destroyed, its explosion drawn, its sound heard.

`README.md`, "Run a play script", holds the step syntax and the dump fields. Never commit `play-out/`.

No timing command: the test runner's per-test durations are not wired in.

## Test paths

- `tests/`
- `test/`
- `*.Tests/`
- `*.Test/`
- `*.test.*`
- `*_test.*`
