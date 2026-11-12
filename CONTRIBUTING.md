# Contributing

Open Armada is an engine for Star Trek: Armada (2000) on Godot 4 with C#. Read `README.md` for the
layout and `AGENTS.md` for the project rules. Everyone who takes part follows the
[Code of Conduct](CODE_OF_CONDUCT.md). Report a vulnerability as `SECURITY.md` describes, not in a
public issue.

## Rules

- **No game content.** The repository contains no bytes taken from an install and no code transcribed
  from the game's program (INV-0001 in `docs/invariants.md`). Tests build their fixtures in code, and
  layouts hold only authored values.
- **The install is the reference.** The reference is the Armada 1.2 `Armada.exe`
  ([ADR 0004](docs/adr/0004-combat-follows-rules-recovered-from-the-game.md)). Recover a rule from it,
  not from a fan source or another reimplementation. Describe each recovered rule in your own words in
  `docs/rules/`, with its address as evidence. Mark a rule that the program does not confirm as
  **inferred** or **stand-in**. `AGENTS.md`, "Game install", describes the process.
- **Code style.** Follow `docs/agents/CODING.md` and `docs/agents/CODING_PATTERNS.md`.
- **Docs.** Update the docs that your change makes wrong. `docs/agents/documentation.md` gives the
  writing standard: American English, present tense, current state only.

## Set up

1. Install [mise](https://mise.jdx.dev).
2. Run `mise install` in the repository root. It installs the pinned tools from `mise.toml`.
3. Create the gitignored `mise.local.toml` with the path of your install:

   ```toml
   [env]
   ARMADA_DIR = "/path/to/Star Trek - Armada"
   ```

`README.md` lists the requirements and how to get the install. The unit tests need no install.

## Make a change

Run every command through its mise task from the repository root. `mise tasks` lists the tasks.

- `mise run format` formats the C# code with CSharpier.
- `mise run test` runs the unit tests, which need neither the install nor Godot.
- `mise run check` runs the full verification: format check, unit tests, build, and the asset check
  against `ARMADA_DIR`. It must pass before you open a pull request.
- `mise run test:integration` runs the integration tests: every checked-in play script, against
  `ARMADA_DIR` and Godot. Run it before you open a pull request that changes the sandbox, a play script, or a layout.

A change to what a player sees or hears in the map sandbox is checked with a play run.
`docs/agents/verification.md` describes it.

## Commits and pull requests

- Write commit messages as [Conventional Commits](https://www.conventionalcommits.org/):
  `<type>(<scope>): <summary>`, for example `fix(assets): report a truncated SOD file`.
- Report bugs and propose changes as issues on GitHub (`Bomret/open-armada`).
- Open a pull request into `main` on GitHub. Keep one change per pull request.
- Pull requests are merged by squash. The pull request title becomes the commit message, so it follows
  Conventional Commits too.
