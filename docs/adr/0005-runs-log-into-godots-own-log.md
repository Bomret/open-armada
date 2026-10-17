# 5. Runs log into Godot's own log through a Godot-free Log

**Status:** Accepted

Every line the engine writes about a run goes through one `Log` in plain C#, which formats each log entry
with time, level, and area and hands it to a sink. The Godot side installs a sink that writes every level
through `GD.Print`, so the entries land in Godot's own rotated `godot.log`, interleaved with the engine's
errors and crash backtraces, and on standard output in call order. The parsers, plans, and asset check stay
free of Godot and are tested by capturing the sink.

## Considered options

- An own log file (`openarmada.log`, JSON lines): it needs its own rotation, and a post-mortem would need
  two files because engine errors and crashes stay in `godot.log`.
- Microsoft.Extensions.Logging or Serilog: a new dependency with configuration and sinks for a handful of
  call sites, while Godot already writes and rotates the file.
- A custom Godot `Logger` that re-timestamps the whole engine stream into an own file: it duplicates
  Godot's file logging and must be thread-safe.
- `Log` on the Godot side only: the Godot-free layers could not log directly, and their tests could not
  see the entries.
- `WARN` and `ERROR` on standard error: a separate stream for a human at a terminal, but a pipe of the
  asset check's output would lose its `FAIL` lines, and the order across two streams is not kept.
