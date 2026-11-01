# Debris

The rules of the debris pieces a unit leaves when it ends: how the model shatters, where each piece starts, how
it flies, and what the shipped data lacks. Recovered from the 1.2 `Armada.exe`
(SHA-256 `c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5`). Each rule names its address as
evidence (ADR 0004, INV-0001). A rule the code does not confirm is marked **inferred** or **stand-in**.
`docs/rules/combat-rules.md` holds the dying phase that ends in the shatter ("Dying units"), and
`docs/rules/forced-motion.md` holds why the pieces take no velocity from the ship.

Offsets are of the unit object unless a class is named.

## The shatter

The end of the dying phase (0x4663d0) shatters the unit's model when the unit's `decommissionID` (+0x690) is −1
and the unit has a model. The call is 0x58cef0, and its `this` is the unit, not a scene object. Its arguments
are the color, the velocity, the chunk flag (1), the face cap (−1), and the black hole's center or 0.

- One chunk manager serves the whole scene (0x57cc40, update 0x57cd90, draw 0x57ceb0).
- The velocity argument (the ship's velocity, or 2.5 times it for the `selfDestruct` explosion) is never read, so a piece takes none of the ship's velocity (0x57cf90).
- The black hole's flag and center are stored in the piece (+0x84, +0x88) and never read. A ship swallowed by a
  black hole makes the same debris as any other ship. The engine's swallow ends the unit through the same
  shatter as the fireball end, and the hole's center is not carried into the piece.

## The walk

The walk (0x57cee0) goes through the model's node tree from its root and only looks at mesh nodes.

- A node whose flag bit 0 is set gives no piece from its own mesh. A node whose flag bit 1 is set gives none from
  its children. Which flags these are is **inferred**: the hidden and damage-group state of a node, the bits the
  render walk reads (0x580110).

## Density and cap

Faces are taken per mesh (0x57cf90).

- The mesh's density accumulator starts at 0. For each face it adds `ST3D_CHUNK_DENSITY`, and while the
  accumulator is above 1.0 (strictly), the face spawns one piece and the accumulator loses 1.0. A density below
  1 spawns a piece for a fraction of the faces. A density above 1 spawns several from one face.
- Before each face, when the live count of pieces is above `ST3D_CHUNK_MANAGER_MAX_CHUNKS`, the oldest piece is
  removed. At most one goes per face.
  **Stand-in:** the engine removes the oldest pieces over the cap in batches: when a spawn would find the live count
  at twice the cap or more, it cuts the count to the cap, and it cuts once more at the end of a shatter. The engine
  keeps the newest cap pieces, in order. For a density of at most 1 the game keeps one more, the oldest of those
  kept after a face, so the pieces match except for that one oldest piece. For a density above 1 a face spawns
  several pieces while one goes per face, so the game's count can stand more than one over the cap.

## The piece's shape

A piece is one triangle of the model (0x57d270).

- Its vertices lie relative to the centroid of the face (the intersection of its medians), with the vertex
  normals of the face and the face's material.

## Placement

The start matrix of a piece is the translation to the centroid, then the mesh node's own matrix, then the unit's
matrix (+0x68) (0x57d270).

- **Quirk:** the transforms of the mesh node's parents are not applied. A mesh under a moved parent node starts
  at the place its own matrix gives.

## The draws and their order

Each piece takes its values from one shared random state (0x67dca4), in this order (0x57d270):

1. Its life: `RandScaled(ST3D_CHUNK_MAX_LIFETIME)`.
2. Its direction: three `RandSigned` draws, stored as (third, second, first). The three draws repeat while the
   vector's length is below 0.1 (0x5ccf20). The result is normalized.
3. Its speed: `RandScaled(ST3D_CHUNK_MAX_VELOCITY)`.
4. Its tumble: three `RandSigned` draws times `ST3D_CHUNK_MAX_ANGULAR_VELOCITY`, stored as (third, second,
   first).

`RandSigned` is in [−1, 1] and `RandScaled(x)` is in [0, x].

The game draws one more value at detail level 4, the chance (0.02) of a chunk sprite. **Stand-in:** the engine's
source is the combat world's seeded random source (ADR 0010), and the engine has no detail level, so it makes no
extra draw. No chunk sprite can appear (see Chunk sprites).

## The update

Each frame (0x57cd90) a piece turns by −ω × dt in its own frame, moves by its velocity × dt, and loses dt of
life. It is removed when its life is below 0. There is no gravity, no drag, no scaling, and no fade.

## The draw

The chunk manager's draw (0x57ceb0, handing on to 0x57dc80) draws each live piece's triangle with its material
at the piece's current matrix. That the draw adds no fade or scale is **inferred** from the update, which keeps
neither; the draw routine's body is not recovered.

## Chunk sprites

The sprite names `chunk1` to `chunk4` are defined by no shipped sprite table, so none appears. The engine draws none.

## Values

The values come from `ART_CFG.h`, which `RTS_CFG.h` includes (read in 0x4453f0). The stock install's file sets
the first column; the program's defaults apply to a missing key. **Stand-in:** the engine also takes the default for
a value that is no finite float, takes 100 for a density above 100, and takes 10000 for a cap above 10000 (a value
beyond int's range included), each reported once; the game bounds none of them, and a density of 2^24 or more never
empties its float accumulator.

| Key | Stock | Default |
|---|---|---|
| `ST3D_CHUNK_MANAGER_MAX_CHUNKS` | 1500 | 3000 |
| `ST3D_CHUNK_DENSITY` | 0.8 | 1.0 |
| `ST3D_CHUNK_MAX_LIFETIME` | 2.75 | 3.0 |
| `ST3D_CHUNK_MAX_ANGULAR_VELOCITY` | 4.0 | 4.0 |
| `ST3D_CHUNK_MAX_VELOCITY` | 70.0 | 200.0 |

## Not known

- Which node flags the walk's two bits are.
- The body of the draw routine (0x57dc80).
