# Toolchain

## `.` — C# / Godot 4 .NET

- SDK: .NET SDK `10.0.402`, pinned in `global.json` (`rollForward: latestFeature`). Projects target
  `net8.0`.
- Engine: Godot 4.7.2 .NET build (`godot-mono`; macOS: `brew install --cask godot-mono`). The SDK
  version in `OpenArmada.csproj` (`Godot.NET.Sdk/4.7.2`) must match the installed editor.
- Game files: an Armada install the user owns, passed as `--game <dir>` or the `ARMADA_DIR` environment
  variable. The repository ships no game content.
- Commands:
  - Tests — see `docs/agents/verification.md`
  - Restore the formatter — `dotnet tool restore` (needs NuGet access the first time)
  - Format — `dotnet csharpier format .`
  - Check formatting — `dotnet csharpier check .`. The full verification command runs it first.
  - Build — `dotnet build OpenArmada.csproj`
  - Run the asset browser — `godot-mono --path . -- --game "$ARMADA_DIR" [--model <name>]`
  - Render one frame to a file and quit — append `--screenshot <file.png>` to the browser command
  - Unit tests — `dotnet test --project tests/OpenArmada.Tests` (MSTest on Microsoft.Testing.Platform,
    selected in `global.json`)
  - Asset check of an install — `godot-mono --headless --path . -- --check --game "$ARMADA_DIR"`
- Formatter: CSharpier `1.3.0`, a local dotnet tool pinned in `dotnet-tools.json`; `.csharpierrc.json`
  sets `printWidth: 120` and leaves every other option at its default. CSharpier reads `.gitignore`, so
  `.godot/`, `bin/`, `obj/`, and `.superdev/` are skipped. `// csharpier-ignore` keeps a hand-laid-out
  fixture, such as a pixel grid, as written.
- Not wired: no linter or `.editorconfig`.
- Style prose: none beyond `README.md`.
