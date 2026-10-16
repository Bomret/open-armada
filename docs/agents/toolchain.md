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
  - Build — `dotnet build OpenArmada.csproj`
  - Run the model viewer — `godot-mono --path . -- --game "$ARMADA_DIR" [--model <name>]`
  - Render one frame to a file and quit — append `--screenshot <file.png>` to the viewer command
  - Check every model of an install — `dotnet run --project tools/SodCheck -- "$ARMADA_DIR/SOD"`
  - Print one model's node tree — `dotnet run --project tools/SodCheck -- "$ARMADA_DIR/SOD/<name>.SOD"`
- Not wired: no formatter, linter, or `.editorconfig` — formatting is not enforced.
- Style prose: none beyond `README.md`.
