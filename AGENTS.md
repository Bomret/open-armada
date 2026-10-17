# Open Armada

An open-source engine for Star Trek: Armada (2000) on Godot 4 with C#. It ships no game content and
reads the files of an Armada install the user owns. See `README.md` for layout, usage, and the SOD
format notes.

## Where to read

- Domain terms: `CONTEXT.md`.
- Combat rules and their recovered values: `docs/combat-rules.md`.
- Decisions: `docs/adr/`. Invariants: `docs/invariants.md`.
- Source layout, controls, and the asset check: `README.md`.

## Agent skills

### Slice store
Codeberg Issues on `bomret/open-armada`, through the `tea` login for `https://codeberg.org`. See `docs/agents/issue-tracker.md`.

### Code host
Codeberg (`bomret/open-armada`) through `tea`; scopes open as draft pull requests into `main`. See `docs/agents/code-management.md`.

### Documentation
In-repo `AGENTS.md` hierarchy, controlled writing standard, American English. See `docs/agents/documentation.md`.

### Knowledge base
Codeberg wiki of `bomret/open-armada`, staged locally and sent by `/superdev:publish-kb`. See `docs/agents/knowledge-base.md`.

### Toolchain
.NET SDK 10 (net8.0) and Godot 4.7.2 .NET; build with `dotnet build OpenArmada.csproj`. Format with CSharpier 1.3.0, a local tool, through `dotnet csharpier format .`. See `docs/agents/toolchain.md`.

### Verification
Full: format check, unit tests, build, and the headless asset check against `$ARMADA_DIR`; focused: unit tests. Arms the merge green-gate. The first `dotnet tool restore` needs NuGet access. See `docs/agents/verification.md`.

### Deployment
Posture `manual`, no targets yet. See `docs/agents/deployment.md`.
