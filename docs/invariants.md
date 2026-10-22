# Invariant register

### INV-0001

Class: legal
Concepts: Install, Asset, Combat rules, Layout
Statement: The repository contains no bytes taken from an install and no code transcribed from the game's program; tests build their fixtures in code, layouts hold only authored values, and rules recovered from the program are described in our own words.
Breaks: silently
Check: none — no cheap test can tell a copied byte sequence or a transcribed routine from an original one; reviewers check that fixtures are built in code and that recovered rules are restated, not transcribed.

### INV-0002

Class: behavior
Concepts: Asset, Game file system, Install, Log entry, Layout
Statement: An asset or a layout that is missing or fails to parse is reported with its name and reason and skipped; it never stops the engine. Each problem the engine reports reaches the log exactly once, as one log entry written by the component that turns findings into a report; parsers return their findings as data and never log.
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
Statement: Every line the engine writes about a run goes through `Log`, which has no Godot dependency; no other engine code writes to the console or calls Godot's print functions, and no parser or asset kind calls `Log`.
Breaks: silently
Check: a unit test that scans the engine sources for direct console writes, Godot print calls outside the sink, and `Log` calls in the parsers and asset kinds.
Paths: src/**

### INV-0005

Class: behavior
Concepts: Log entry, Asset check, Stand-in
Statement: A log entry's level states what happened, the same for one cause in every mode: `ERROR` when something broke — an asset failed to load, the engine caught a fault, or a mode failed to open — whether or not the run goes on; `WARN` when the engine went on by design with a stand-in, absent data, a skip, or an unsupported asset; `INFO` for context.
Breaks: silently
Check: none — the level of each call site is a judgment; the asset check's marker-to-level mapping has its own test, and reviewers check every other call site against this rule.

### INV-0006

Class: behavior
Concepts: Selection display, Hovered unit, Display fade
Statement: The selection display shows only through the hovered unit: no unit shows a shield bar or a crew dot because it is selected unless it is the hovered unit or of the hovered unit's team.
Breaks: silently
Check: a unit test that selects units with no hovered unit, and with a hovered unit of another team, and finds no shield bar and no crew dot.
Paths: src/Sandbox/SelectionDisplay.cs, src/Viewer/SelectionDisplayView.cs

### INV-0007

Class: behavior
Concepts: Play script, Play run, Log entry
Statement: A play script that cannot be read, a step that cannot run, or an expectation that does not hold is reported as a log entry naming the script, the line, and, for an expectation, the expected and actual values; the play run then quits with a non-zero exit code and never throws out of the sandbox.
Breaks: loudly

### INV-0008

Class: behavior
Concepts: Play run, Play script, State dump
Statement: A play run fixes its frame rate and every random source it reads, so two play runs of one map and one play script write identical state dumps.
Breaks: silently
Check: a unit test that runs one play script twice against the same sandbox plan and compares the state dumps.

### INV-0009

Class: architecture
Concepts: Layout, Map, Map sandbox
Statement: The map sandbox plans a layout and a map through the same plan; no plan or view code branches on whether the objects came from a layout or a map.
Breaks: silently
Check: none — no cheap test tells a source-specific branch from a general one; reviewers check that the layout reader ends at the parsed map and that the plan and view take no layout type.

### INV-0010

Class: behavior
Concepts: Asset browser, Preview, Log entry
Statement: A preview returns the notes of the asset it shows beside the view it builds and never writes them to the log; the asset browser writes each note as one log entry, once per preview shown.
Breaks: silently
Check: a unit test that scans the preview sources for log calls on notes and for direct `Log.Warn`, `Log.Info`, and `Log.Error` calls, and a unit test that shows one asset through the asset browser and finds each of its notes logged once.
Paths: src/Viewer/*Preview.cs, src/Assets/AssetBrowser.cs

### INV-0012

Class: behavior
Concepts: Team, Hostile, Selection display, State dump
Statement: Hostility, targeting, orders, the selection display, and the state dump read a unit's current team; once the map sandbox runs, none of them reads the team the map placed the unit on.
Breaks: silently
Check: a unit test that scans the sandbox and viewer sources for reads of the placed object's team outside the map plan, the map summary, and the map previews, and a unit test that captures a unit and finds its former teammates hostile to it.
Paths: src/Sandbox/**, src/Viewer/**

### INV-0013

Class: behavior
Concepts: Play script, Play run, Map sandbox
Statement: Every order, selection, or key action a player gives in the map sandbox has a play-script step that sends the same input event through the same handler.
Breaks: silently
Check: none — no cheap test pairs each input handler with a step; reviewers check that a change adding a player action adds its play-script step.
Paths: src/Viewer/**, src/Sandbox/PlayScript.cs
