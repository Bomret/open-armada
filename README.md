# Open Armada

An open-source engine for Star Trek: Armada (2000), built on Godot 4 with C#. It ships no game content: it reads the files of an Armada install the player owns.

Status: prototype. A map sandbox opens one map as a live 3D scene. An asset browser previews the assets of an install: models, textures, sounds, maps, object definitions, tech trees, sprite tables, interface data, fonts, and cursors. A headless asset check loads every asset and reports each failure.

## Requirements

- Godot 4.7 .NET build (`brew install --cask godot-mono`)
- .NET SDK 10 (pinned in `global.json`; the project targets `net8.0`)
- An installed copy of Star Trek: Armada, or its GOG installer unpacked as described below

## Use the GOG installer

The GOG installer is an Inno Setup file. `innoextract` unpacks it on macOS or Linux without running it:

```sh
brew install innoextract   # or your package manager on Linux
innoextract -m -d "/path/to/Star Trek - Armada" "setup_star_trektm_armada_1.2_rdna_fix_(52907).exe"
```

`-m` skips the installer's temporary files. The game files land at the root of the `-d` folder; pass that folder as `--game`. The asset check classifies the files GOG adds beside the game as out of scope.

## Run the asset browser

```sh
dotnet build OpenArmada.csproj
godot-mono --path . -- --game "/path/to/Star Trek - Armada" --model Fente
```

`ARMADA_DIR` can replace `--game`. The preview shows the asset selected in the category and asset lists. In a model preview, drag to orbit and use the mouse wheel to zoom. `--model <name>` opens the named model. `--screenshot out.png` renders one frame to a file and quits.

A model preview also draws the model's sprite nodes. A sprite node is a sprite that the install's sprite tables attach to a model node by name. Examples are a running light and an explosion's fireball. Its node animation loops. The preview frames a model made only of sprite nodes, such as the explosion model `xplode`, by its sprites.

## Run the map sandbox

```sh
godot-mono --path . -- --game "/path/to/Star Trek - Armada" --map 2duel
```

`--map <name>` opens a map from the install's `bzn` folder, matched without regard to case, folder, or extension. The scene holds the map's objects with their team colors, its background, and generated asteroid fields. Add `--screenshot out.png` to render one frame to a file and quit.

The sandbox behaves as follows:

- Camera: the arrow keys and the window edges scroll the camera. The mouse wheel changes its height. A middle-button drag rotates it. On a trackpad, a two-finger pan changes the height and rotates, and a pinch changes the height. The camera limits and rates come from the install's `RTS_CFG.h`.
- The sandbox confines the cursor to its window while the window has focus. Esc releases it until the window gains focus again.
- Left-click selects a unit, and a left-button drag selects the units in the box. Right-click on a hostile unit orders the selected units that have weapons to attack it; a unit without weapons ignores the click. Right-click elsewhere sends the selected ships to that point; a point outside the map sends them to the nearest point of the map's edge. Stations do not move.
- The green, yellow, and red alert keys set the alert stance of the selected units. The keys come from the install's `Input.map` (Shift+G, Shift+Y, and Shift+R in the reference install).
- Units and other models draw their sprite nodes, as the model preview does. The rocks of an asteroid field draw theirs too. A destroyed unit shows its explosion at its last position, and the explosion plays its sprite nodes once.
- Fire and explosion sounds fade with their distance from the camera. Nothing is cut off, and a sound at the camera's opening distance stays audible. The install defines no falloff, so the engine uses a stand-in.
- A map object or definition that cannot be read is reported as a gap, printed and counted on screen, and the map still opens.

These rules apply to every map the sandbox opens.

## Layout

- `src/Formats/` — parsers for the original file formats, plain C# without Godot dependencies.
- `src/Rendering/` — converts parsed data into Godot nodes.
- `src/Viewer/` — the asset browser scene script, one preview per asset kind, and the map sandbox scene. The scene handles selection, orders, alert keys, and the drawing and sound of the fight.
- `src/Sandbox/` — the map sandbox's plans, camera rig, and ship motion, plus its combat. The combat part holds the combat plan read from the install, the combat world and its rules, and the art of the fight. The folder is plain C# without Godot dependencies. `docs/combat-rules.md` states the rules.
- `src/Assets/` — the game file system (case-insensitive lookup in an install), the asset kinds, and the asset check; plain C# without Godot dependencies.
- `tests/OpenArmada.Tests/` — unit tests, run with `dotnet test --project tests/OpenArmada.Tests`. Fixtures are built in code.

C# is formatted with CSharpier: run `dotnet tool restore` once, then `dotnet csharpier format .` to format and `dotnet csharpier check .` to check.

## Check an install

```sh
godot-mono --headless --path . -- --check --game "/path/to/Star Trek - Armada"
```

Loads every asset and prints each failure with its name and reason. Classifies every file of the install and lists references that do not resolve. Exits non-zero when any asset failed or any file is unclassified. Assets that are known but unsupported (`Wviracod.SOD`) are listed apart and do not fail the check. A SOD with any other unrecognised version is corrupt and fails it.

## SOD format notes

Version 1.8 follows the official Storm3D spec. The older versions were decoded from the shipped files:

| Version | Difference from 1.8 |
|---|---|
| 1.7 | Animation references have no time offset. |
| 1.6 | Also: a texture material table (name, texture, 7 unknown bytes) precedes the lighting materials; meshes carry one identifier that names a table entry. |
| 1.4, 1.5 | Also: no animation reference section. |
| 1.2 | Also: no animation sections. A mesh has one lighting material, counts in the order vertices/faces/texcoords, an unknown float, and faces with their own corner count. No cull byte. |
| 1.0 | Unsupported. Only `Wviracod.SOD` uses it, and the game does not reference it. |

The mesh trailer after the cull byte is a count followed by that many 16-bit vertex indices. The spec says the count is always 0; `Zentity.SOD` has 7.

Storm3D is left-handed. The builder mirrors Z and reverses triangle winding.
