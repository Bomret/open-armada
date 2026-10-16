# Verification

```sh
dotnet build OpenArmada.csproj && dotnet run --project tools/SodCheck -- "${ARMADA_DIR:?set ARMADA_DIR to an Armada install}/SOD"
```

```sh
dotnet build OpenArmada.csproj && dotnet run --project tools/SodCheck -- "${ARMADA_DIR:?set ARMADA_DIR to an Armada install}/SOD"
```

**No cheaper focused command, deliberately.** The whole check runs in about 2 seconds, so a distinct
focused command saves nothing worth naming, and every caller runs the full command
(`references/test-selection.md`, ADR 0025, ADR 0074, ADR 0085).

The check builds the project and parses every `.sod` model of the user's own Armada install. It needs
`ARMADA_DIR` to point at that install; without it the command fails with
`set ARMADA_DIR to an Armada install`.

No timing command: `SodCheck` reports no per-test durations, and the whole check finishes in seconds.

## Test paths

- `tools/SodCheck/`
- `tests/`
- `test/`
- `*.Tests/`
- `*.Test/`
- `*.test.*`
- `*_test.*`
