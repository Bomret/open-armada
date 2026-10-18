# 6. Play runs drive the sandbox through its own input under Movie Maker

**Status:** Accepted

The agent checks what a player sees and hears in the map sandbox with a play run: a play script replayed
on fixed simulated time under Godot's Movie Maker, which writes a PNG per frame and one WAV of the mixed
audio. Selection, orders, keys, and camera moves enter as Godot input events at the unit's projected
screen point, so a play run exercises the same input handlers a player does. The agent judges frames and
audio itself; only unit state is asserted by the script.

## Considered options

- Calling the order and selection code directly: less code and no screen projection, but it skips the
  input path, and input flows are one of the things these runs check.
- An own interactive bridge (a TCP or stdin command channel, perhaps wrapped as an MCP server): it rebuilds
  what an existing Godot runtime MCP offers, with more code and no gain over a scripted run.
- Computer use on the desktop window: no code, but slow, deaf to audio, blind to unit state, and not
  repeatable.
- A runtime MCP as the only tool: no audio, real-time timing, and nothing to check into the repository.
