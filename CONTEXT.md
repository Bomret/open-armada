# Open Armada glossary

## Game data

**Install**
A copy of Star Trek: Armada that the player owns, on disk. The engine reads all game data from it and
ships none of its own. The reference install is the final patch, version 1.2.
_Avoid_: game folder, game directory, assets folder

**Game file system**
The single view of an install's files that every loader reads through: loose files, archive contents,
and the override layer, looked up by name without regard to case.
_Avoid_: VFS, file layer, GameFiles

**Archive**
A `.zfs` container inside an install that bundles many files under one name table, some of them
obfuscated with a key stored in the archive itself.
_Avoid_: ZFS file, pack, package

**Index**
The `startrek.zix` file that names, for every game file, the folder or archive holding it.
_Avoid_: zix, file list

**Override layer**
The install's `addon` folder, whose files replace same-named files from the archives.
_Avoid_: patch folder, addon directory

**Shadowed copy**
A file of an install whose name another file wins in the game file system's lookup. An example is a stock
definition that the override layer replaces. The asset check reports a shadowed copy that no asset kind
reads as known but not loaded.
_Avoid_: duplicate, overridden file

**Asset kind**
One category of asset, such as models or textures, with its own way to list, load, and check its assets.
The asset browser offers one category per asset kind.
_Avoid_: asset type, loader

**Asset**
One loadable piece of game data in an install: a model, texture, sound, map, object definition, tech
tree, sprite table, interface configuration, font, cursor, or text table.
_Avoid_: resource, game file

**Object definition**
A text file (`.odf`) that sets the properties of one class of game object, such as a ship, weapon, or
race, and may inherit from another through an include.
_Avoid_: ODF file, unit file, config

**Resolved definition**
An object definition with its include chain and the override layer applied, giving one value per key.
_Avoid_: merged ODF, flattened ODF

**Tech tree**
A text file (`.tt`) that states which objects a race can build and what each requires.
_Avoid_: TT file, build tree

**Map**
A playable scenario (`.bzn`) with its objects, their positions and teams, plus its start positions
(`.mdf`) and overview image.
_Avoid_: level, scenario file, BZN

**Start position**
The place where one player begins on a map, read from the map's `.mdf` file. Only multiplayer maps have them.
_Avoid_: spawn, start cell, start point

**Overview image**
The picture of a map that the game's menus show, the map's `.bmp` file.
_Avoid_: minimap, map thumbnail

**Sprite table**
A text file (`.spr`) that names regions of textures as sprites and gives their blend mode and animation.
_Avoid_: SPR, sprite sheet definition

**Team color**
The color that marks which player owns an object, applied to a model's team-colored materials.
_Avoid_: player color, faction color

## Tools

**Asset check**
The headless run of the engine that loads every in-scope asset of an install and reports each one that
fails, with its name and reason. It also lists the references that do not resolve and puts every file of
the install into one file class. It fails when any asset fails or any file is unclassified.
_Avoid_: SodCheck, validator, asset test

**File class**
The group that the asset check assigns to a file of an install. Loaded: an asset kind reads the file.
Out of scope: the file holds no game data the engine reads. Known but not loaded: the file is recognized
but no asset kind reads it, such as a shadowed copy. Unclassified: no rule matches the file, and the
asset check fails.
_Avoid_: file category, file status

**Asset browser**
The engine's interactive mode for picking any asset of an install and inspecting its preview.
_Avoid_: model viewer, asset viewer

**Preview**
The asset browser's rendering of one asset, showing that its meaning was understood: a model as the game
draws it, a map from above, a sound as playback, a definition as its resolved values.
_Avoid_: thumbnail, view
