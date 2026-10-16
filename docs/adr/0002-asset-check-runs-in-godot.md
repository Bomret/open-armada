# 2. The asset check runs inside Godot, headless

**Status:** Accepted

The asset check is the engine itself started headless with a check flag, not a separate .NET tool. Images
and sounds load through Godot's own runtime loaders, so a separate tool would test a different decoder
than the one the game uses. The cost is that the check needs Godot installed; plain format logic stays
in Godot-free code covered by unit tests.

## Considered options

- Extending the plain .NET `SodCheck` tool into an `AssetCheck`: faster start and no Godot needed, but it
  would validate image and WAV files with its own code rather than the loaders the game runs.
