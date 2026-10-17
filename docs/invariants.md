# Invariant register

### INV-0001

Class: legal
Concepts: Install, Asset, Combat rules
Statement: The repository contains no bytes taken from an install and no code transcribed from the game's program; tests build their fixtures in code, and rules recovered from the program are described in our own words.
Breaks: silently
Check: none — no cheap test can tell a copied byte sequence or a transcribed routine from an original one; reviewers check that fixtures are built in code and that recovered rules are restated, not transcribed.

### INV-0002

Class: behavior
Concepts: Asset, Game file system, Install, Log entry
Statement: An asset that is missing or fails to parse is reported with its name and reason and skipped; it never stops the engine. Each problem the engine reports reaches the log exactly once, as one log entry written by the component that turns findings into a report; parsers return their findings as data and never log.
Breaks: loudly

### INV-0003

Class: behavior
Concepts: Install, Object definition, Game constants, Stand-in
Statement: A tunable that the install defines is read from the install at run time; a value the engine supplies itself is a stand-in, named as one in a comment where it is used.
Breaks: silently
Check: none — no cheap test tells a hard-coded copy of an install value from a deliberate stand-in; reviewers check that every engine-supplied tunable carries a `stand-in:` comment.

### INV-0004

Class: architecture
Concepts: Log, Log entry, Run
Statement: Every line the engine writes about a run goes through `Log`, which has no Godot dependency; no other engine code writes to the console or calls Godot's print functions, and no parser calls `Log`.
Breaks: silently
Check: a unit test that scans the engine sources for direct console writes, Godot print calls outside the sink, and `Log` calls in the parsers.
Paths: src/**

### INV-0005

Class: behavior
Concepts: Log entry, Asset check, Stand-in
Statement: A log entry's level states the consequence: `ERROR` when an asset or the run failed, so the asset check fails or a mode does not open; `WARN` when the engine went on with a stand-in, a gap, a skip, or an unsupported asset; `INFO` for context.
Breaks: silently
Check: none — the level of each call site is a judgment; the asset check's marker-to-level mapping has its own test, and reviewers check every other call site against this rule.
