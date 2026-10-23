# GhidraDump

`DumpProgram.java` is a Ghidra script that writes a local, greppable dump of an analysed program.
`CreateMissingFunctions.java` runs before it and creates the functions Ghidra's analysis leaves out: call
targets without a function, and runs of instructions that belong to no function (code reached only through
jumps or jump tables). On `Armada.exe` it creates 2,570 functions. Use it for
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

Ghidra 12.1 and Java 21 from Homebrew (`brew install ghidra`), the same install REA uses:

```bash
export JAVA_HOME=/opt/homebrew/opt/openjdk@21 PATH=/opt/homebrew/opt/openjdk@21/bin:$PATH
```

The first run imports and analyses the program into a Ghidra project, creates the missing functions,
and dumps it. Every path must be
absolute and contain no element that starts with a dot; Ghidra rejects such a project path.

```bash
/opt/homebrew/opt/ghidra/libexec/support/analyzeHeadless "$PWD/decomp/armada/project" Armada -import "$ARMADA_DIR/Armada.exe" -overwrite -scriptPath "$PWD/tools/GhidraDump" -postScript CreateMissingFunctions.java -postScript DumpProgram.java "$PWD/decomp/armada/dump"
```

Later runs reuse the saved project and skip the analysis:

```bash
/opt/homebrew/opt/ghidra/libexec/support/analyzeHeadless "$PWD/decomp/armada/project" Armada -process Armada.exe -noanalysis -readOnly -scriptPath "$PWD/tools/GhidraDump" -postScript DumpProgram.java "$PWD/decomp/armada/dump"
```

A second script argument `index-only` writes the three tables without the pseudocode files, which take most
of the run time.

## Gotchas

- REA stops a provider request after 330 s, and the first full analysis of `Armada.exe` takes longer, so
  REA cannot open the exe from scratch, and it does not read a Ghidra project. Run the analysis here instead.
- Run one Ghidra analysis at a time on this machine; two at once overload it.
