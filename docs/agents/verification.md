# Verification

```sh
dotnet test --project tests/OpenArmada.Tests && dotnet build OpenArmada.csproj && godot-mono --headless --path . -- --check --game "${ARMADA_DIR:?set ARMADA_DIR to an Armada install}"
```

```sh
dotnet test --project tests/OpenArmada.Tests
```

The full command runs the unit tests, builds the project, and runs the asset check: the engine started
headless, loading every asset of the user's own Armada install; images and sounds are also decoded by
Godot's own loaders. The asset check exits non-zero when any asset fails; unsupported assets and
`NOTE` lines (such as a texture whose mip chain ends at a level of the wrong size) are listed and do
not fail it. It needs `ARMADA_DIR` to point at that
install; without it the command fails with `set ARMADA_DIR to an Armada install`.

The focused command runs the unit tests alone. They build their fixtures in code and need neither an
install nor Godot.

No timing command: the test runner's per-test durations are not wired in.

## Test paths

- `tests/`
- `test/`
- `*.Tests/`
- `*.Test/`
- `*.test.*`
- `*_test.*`
