# 1. Read the original game files at runtime

**Status:** Accepted

The engine loads every asset straight from the player's install, through its own parsers, each time it
needs it. It does not convert the install into Godot resources first. One code path means the asset
check exercises exactly what the game runs, and nothing derived from copyrighted data is written to disk.

## Considered options

- A one-time import into Godot `.tres`/`.res` files under `user://`: faster repeat loads, but two code
  paths (converter and Godot loader) and a re-import whenever the install changes. A cache can still be
  added later if load times demand it.
