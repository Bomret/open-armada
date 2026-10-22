# Patterns (LLM Reference)

Target audience: LLMs working in this repo. The folder layout is in `README.md` (Layout).

## Namespace

`OpenArmada.<Folder>`, one namespace per folder of `src/`. For example, `OpenArmada.Formats`. Tests use
`OpenArmada.Tests`.

## Godot boundary

`src/Formats/`, `src/Assets/`, and `src/Sandbox/` are Godot-free. The test project compiles their source
files directly, so the tests run without Godot. A `using Godot` in these folders breaks the test build.

- Put logic that needs no engine in a Godot-free folder and test it there. For example, `ShipMotion`
  computes a pose, and `MoveOrders` in the viewer only applies the pose to a node.
- When Godot-free code needs an engine service, pass it in as a delegate. For example,
  `AssetCheck.Run` takes `GodotDecoders.Check` from the viewer.
- Only `src/Rendering/` and `src/Viewer/` reference Godot.

## Format readers

A reader in `src/Formats/` parses bytes into plain data. It does not read files or know where an
asset is installed.

```csharp
public static class Palette
{
    public static IReadOnlyList<(byte R, byte G, byte B)> Read(byte[] data)
    {
        if (data.Length != 768)
            throw new InvalidDataException($"palette is {data.Length} bytes, expected 768 (256 RGB colors)");
        // ...
    }
}
```

- Throw `InvalidDataException` for a corrupt asset. The message names what is wrong and, where known,
  the offset.
- Throw `UnsupportedFormatException` for a format that is known but deliberately not read. The asset
  check lists it apart and does not fail on it.

## Asset kinds and previews

An asset kind is an `AssetKind` subclass in its own file in `src/Assets/`, with a parameterless
constructor. `AssetKind.Discover` finds every kind by reflection, so a new kind edits no shared list.

```csharp
public sealed class PaletteKind : AssetKind
{
    public const string KindName = "palettes";

    public override string Name => KindName;

    public override IEnumerable<string> Enumerate(GameFileSystem files) => files.List(".pal", "", "Textures/RGB");

    public override object Load(GameFileSystem files, string name) => Palette.Read(files.ReadAllBytes(name));
}
```

- Override `Check`, `Unresolved`, or `Reads` only when the default does not fit the kind.
- Read install files only through `GameFileSystem`. It resolves names case-insensitively across
  folders, the override layer, and archives.
- The asset browser preview of a kind is a `KindPreview` subclass in `src/Viewer/`, named after the
  kind (`PalettePreview`). `KindPreview.Discover` matches it to the kind through `KindName`.
- `KindPreview.Create` returns a `Preview`: the view it builds and the notes the view shows. A preview
  never logs its notes. `AssetBrowser` logs them each time it shows the asset (INV-0010), so the rule
  holds in one place for every preview. `PreviewNotesTests` fails on a preview that logs notes or calls
  `Log.Warn`, `Log.Info`, or `Log.Error` directly; `ViewerLog.TextureFailed` stays allowed.
- `AssetBrowser` runs the selection flow and writes the browser's log entries. `AssetBrowserView` only
  builds the controls and shows the `Pane` the browser hands it, so the unit tests reach every entry.
  `AssetBrowserView` wraps each preview with `AssetBrowser.Showing<Control>`; a preview whose view is of
  another type is a failed preview: one `ERROR` entry, and the pane says why.

## Gaps never stop the engine

An asset that is missing or fails to parse is reported with its name and reason and then skipped
(INV-0002, `docs/invariants.md`). Do not let such an exception escape a load loop or a scene build.
The map sandbox collects its gaps in `Gaps`, which reports each cause once with a count.

## Stand-ins

A value that the install defines is read from the install at run time. A value that the engine
supplies itself is a stand-in (INV-0003). Mark each stand-in with a `stand-in:` comment where it is
used. The comment says what the value replaces.

```csharp
// stand-in: a target nearer than this counts as reached.
const float Reached = 1e-4f;
```

## Tests

Tests use MSTest (`[TestClass]`, `[TestMethod]`, `[DataRow]`). A test method name is a sentence that
states the behavior (`AnUnknownMapIsAProblemNamingItAndTheMapsOfTheInstall`).

The repository holds no bytes from an install (INV-0001). Build every fixture in code:

- `TempInstall` builds an install in a temporary folder from path and content pairs.
- `ZfsBuilder`, `BznBuilder`, and `SodBytes` build binary files in their formats.

## Common pitfalls

- Do not split a large class into `partial` files to manage its size. Extract cohesive
  responsibilities into separate collaborator types instead. A class that derives from a Godot type
  is `partial` because the Godot source generator requires it. That use is the only exception.
- Do not hard-code a value that the install defines (INV-0003).
