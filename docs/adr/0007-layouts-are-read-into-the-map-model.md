# 7. Layouts are read into the map model, not written as maps

**Status:** Accepted

A layout is a JSONC file, read with `System.Text.Json` into the same parsed map the `.bzn` reader returns,
and handed to the map sandbox's plan unchanged. The engine writes no `.bzn` file. A layout sets only what
the sandbox reads from a map: bounds, background, ambient light, team colors, and each object's class,
team, position, facing, and label. The raw per-object tokens whose meaning is unknown stay empty. So one
plan path serves maps and layouts, and a layout in the repository holds no byte of an install.

## Considered options

- A `.bzn` writer (the test `BznBuilder` moved into the engine, `--map` taking a path outside the
  install): a layout still needs a text source, so this adds a writer on top of the same reader, and
  binary files do not diff. The game would likely not load the output, because the per-class tokens are
  unknown.
- An overlay on a shipped map (remove and add objects on a map of the install): the result depends on
  the install's map content, so it is not reproducible from the repository alone.
- A placement editor in the sandbox: a large interface that an agent drives poorly, and its output
  still needs a file format.
- A line format like `.mdf`: every error names its line, but it needs its own parser; JSONC needs none.
