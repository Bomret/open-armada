# GhidraDump

`DumpProgram.java` is a Ghidra script that writes a local, greppable dump of an analysed program.
`CreateMissingFunctions.java` runs before it and creates the functions Ghidra's analysis leaves out: call
targets without a function, and runs of instructions that belong to no function (code reached only through
jumps or jump tables). On `Armada.exe` it creates 2,570 functions. `ApplyIndexNames.java` applies the names
of the "By address" table in `docs/rules/program-index.md` to the project: a function name at a code address,
creating the function when there is none, and a label at a data address. Use them for
`Armada.exe` (see `docs/rules/program-index.md`). The dump is decompiler output of the original game, so it
stays in `decomp/`, which git ignores, and never enters the repository (ADR 0004).

## Output

In the output directory:

- `functions/<0xaddress>.c`: one pseudocode file per function. The first line lists the callers and the
  data references (vtable slots, function pointers) to the function's entry. A caller that is a bare call
  site address lies in code no Ghidra function contains; after `CreateMissingFunctions.java` there are none
  in `Armada.exe`. A function whose only reference is a data reference inside another function (a jump
  table) is a case block of that function's switch, not a routine of its own. For example 0x465401 to
  0x465537 are cases of 0x465100.
- `functions.tsv`: address, class-qualified name, size in bytes, callers, callees, referenced strings, data
  references.
- `strings.tsv`: each defined string with the functions that reference it.
- `vtables.tsv`: each vtable that Ghidra labels, with its class and its slots in order. Ghidra labels none in
  `Armada.exe`, so overwrite this file with the output of `rtti_vtables.py`, which reads the 444 vtables
  and their class names from the exe's MSVC RTTI data:

  ```bash
  python3 -I tools/GhidraDump/rtti_vtables.py "$ARMADA_DIR/Armada.exe" decomp/armada/dump/vtables.tsv
  ```

## Running it

- Ghidra 12.1.x, the release line REA accepts, so one install serves REA and the dump. Extract the
  official release from `github.com/NationalSecurityAgency/ghidra`. The release ships no native
  decompiler for macOS on Apple silicon: build it with `gradle buildNatives` in the release's
  `support/gradle/` folder (`brew install gradle`).
- A JDK 21 or newer (`brew install openjdk@25`).

```bash
export JAVA_HOME=/opt/homebrew/opt/openjdk@25/libexec/openjdk.jdk/Contents/Home
export PATH="$JAVA_HOME/bin:$PATH"
export HEADLESS=<Ghidra 12.1.x folder>/support/analyzeHeadless
```

The first run imports and analyses the program into a Ghidra project, creates the missing functions,
applies the index's names, and dumps it. Every path must be
absolute and contain no element that starts with a dot; Ghidra rejects such a project path.

```bash
"$HEADLESS" "$PWD/decomp/armada/project" Armada -import "$ARMADA_DIR/Armada.exe" -overwrite -scriptPath "$PWD/tools/GhidraDump" -postScript CreateMissingFunctions.java -postScript ApplyIndexNames.java "$PWD/docs/rules/program-index.md" -postScript DumpProgram.java "$PWD/decomp/armada/dump"
```

The project update runs both scripts again on the saved project, without Ghidra's auto-analysis, and saves
the project. It writes no dump and takes seconds. Without `-noanalysis` Ghidra first re-analyses the whole
program, which takes about 6 minutes:

```bash
"$HEADLESS" "$PWD/decomp/armada/project" Armada -process Armada.exe -noanalysis -scriptPath "$PWD/tools/GhidraDump" -postScript CreateMissingFunctions.java -postScript ApplyIndexNames.java "$PWD/docs/rules/program-index.md"
```

Dump runs reuse the saved project and skip the analysis:

```bash
"$HEADLESS" "$PWD/decomp/armada/project" Armada -process Armada.exe -noanalysis -readOnly -scriptPath "$PWD/tools/GhidraDump" -postScript DumpProgram.java "$PWD/decomp/armada/dump"
```

A second script argument `index-only` writes the three tables without the pseudocode files, which take most
of the run time.

## When to re-run

A run is costly. Re-run the dump only when all of these hold:

1. The scope's implementation is done. Never re-run during an investigation or a slice, and never because
   the binary was opened or a finding was made.
2. The project holds a change that alters what the dump says about code: a function created, a function's
   boundaries fixed, or a type or signature that changes the pseudocode. Names and comments alone do not
   count; the program index carries them.
3. The dump is read again for later work. A change no later investigation will read can wait for the next
   run that is due.
4. The user agreed to this run. Ask once, name the changes from 2, and wait for a yes.

Run the project update at the end of the scope when the "By address" table changed, and before a dump run
that is due. It needs no yes. The names it applies do not make a dump run due by themselves (condition 2),
so the dump shows the index's names as of its last run.

When only `functions.tsv`, `strings.tsv`, or `vtables.tsv` must change, run with `index-only`. Re-run the
first-run command (`-import -overwrite`) only when `decomp/` is missing: it replaces the project and loses
every change applied to it.

## Gotchas

- REA opens `Armada.exe` itself and does not read this Ghidra project. The dump is the first source; use
  REA only for a question the dump cannot answer. Names and missing functions reach the project
  through the index and the project update. Apply other findings (a type, a comment, a function's
  boundaries) in Ghidra on the project as you go. Re-run the dump only as "When to re-run" says. Never edit the
  dump by hand; the next run overwrites it.
- Run one Ghidra analysis at a time on this machine; two at once overload it.
