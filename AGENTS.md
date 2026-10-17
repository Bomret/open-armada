# Open Armada

An open-source engine for Star Trek: Armada (2000) on Godot 4 with C#. It ships no game content and
reads the files of an Armada install the user owns. See `README.md` for layout, usage, and the SOD
format notes.

## Where to read

- Domain terms: `CONTEXT.md`.
- Combat rules and their recovered values: `docs/combat-rules.md`.
- How the game draws a unit's status in the world: `docs/selection-display.md`.
- Decisions: `docs/adr/`. Invariants: `docs/invariants.md`.
- Source layout, controls, and the asset check: `README.md`.
- The log of a run: `godot.log` in Godot's user-data folder; its path per system, entry format, levels, and areas are in `README.md`, "Read the log". The asset check's report appears there and on standard output. After a run, read its `WARN` and `ERROR` entries first.
- Logging rules: `docs/invariants.md` (INV-0004, INV-0005) and `docs/adr/0005-runs-log-into-godots-own-log.md`. All output goes through `Log` in `src/Diagnostics/`; parsers and asset kinds never log.

## Game install

The path of the local Armada 1.2 install is in `CLAUDE.local.md`, a gitignored file per checkout.
Read it there, and ask the user for the path when the file is missing. The 1.2 `Armada.exe` has
the SHA-256 that `docs/combat-rules.md` cites.

Investigate the game's logic and rules in the `Armada.exe` of that install with REA (the
`reverse-engineer-anything` skill and the `rea` MCP tools) before you build a rule. Do not guess a
rule or copy one from a fan source. Record each recovered rule in our own words with its address as evidence (ADR 0004,
INV-0001). Mark a rule that the program does not confirm as **inferred** or **stand-in**.

## Game install

The path of the local Armada 1.2 install is in `CLAUDE.local.md`, a gitignored file per checkout.
Read it there, and ask the user for the path when the file is missing. The 1.2 `Armada.exe` has
the SHA-256 that `docs/combat-rules.md` cites.

Investigate the game's logic and rules in the `Armada.exe` of that install with REA (the
`reverse-engineer-anything` skill and the `rea` MCP tools) before you build a rule. Do not guess a
rule or copy one from a fan source. Record each recovered rule in our own words with its address as evidence (ADR 0004,
INV-0001). Mark a rule that the program does not confirm as **inferred** or **stand-in**.

## Agent skills

### Slice store
Codeberg Issues on `bomret/open-armada`, through the `tea` login for `https://codeberg.org`. See `docs/agents/issue-tracker.md`.

### Code host
Codeberg (`bomret/open-armada`) through `tea`; scopes open as draft pull requests into `main`. See `docs/agents/code-management.md`.

### Documentation
In-repo `AGENTS.md` hierarchy, controlled writing standard, American English. See `docs/agents/documentation.md`.

### Knowledge base
Codeberg wiki of `bomret/open-armada`, staged locally and sent by `/superdev:publish-kb`. See `docs/agents/knowledge-base.md`.

### Coding
C# style rules in `docs/agents/CODING.md`; the patterns this repo uses in `docs/agents/CODING_PATTERNS.md`.

### Toolchain
.NET SDK 10 (net8.0) and Godot 4.7.2 .NET; build with `dotnet build OpenArmada.csproj`. Format with CSharpier 1.3.0, a local tool, through `dotnet csharpier format .`. See `docs/agents/toolchain.md`.

### Verification
Full: format check, unit tests, build, and the headless asset check against `$ARMADA_DIR`; focused: unit tests. Arms the merge green-gate. The first `dotnet tool restore` needs NuGet access. See `docs/agents/verification.md`.

### Deployment
Posture `manual`, no targets yet. See `docs/agents/deployment.md`.
