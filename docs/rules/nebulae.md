# Nebulae and asteroid belts

The rules by which a nebula slows a ship, and the map data the path planner reads from nebulae and asteroid belts.
Recovered from the 1.2 `Armada.exe` (SHA-256 `c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5`).
Each rule names its address as evidence (ADR 0004, INV-0001). A rule the code does not confirm is marked
**inferred** or **stand-in**. `docs/rules/flight-model.md` holds the physics step that applies the cap ("The
physics step").

Stock values are from the 1.2 install's definitions. Offsets are of the unit object unless a class is named.

## The nebula pass

One game tick (0x43f1a0) runs the nebula pass (0x43edc0) first among the world passes: before the area pass
(0x4713c0) and the simulate pass (0x471410), which runs the physics step. The pass gives the cap, the physics step
reads it in the same tick, and the step resets it (0x465cc0), so a ship stays slowed only while its cell holds a
nebula at every tick. The sandbox gives the cap right before the step of each ship, after the ship's process wrote its
controls; nothing between the two reads it.

For every craft the game has (a list that every hover craft joins at construction, 0x4617b0), the pass takes the
ship's x and z, converts each to a cell by `(position - origin) / 50` truncated toward zero, and reads, for each
nebula kind 1 to 5, the count in that cell. A position outside the grid has no counts. **Inferred**: the truncation is
the C runtime's `_ftol`, so a position less than one cell below the origin is in the first cell. For each kind
whose count is not 0 it applies that kind's effect once (0x44c520), however many nebulae of the kind overlap.
The ship's height and size are not read.

## The grid

The grid (0x43e880) holds 50-unit cells (0x5cabe0). Its columns number `(xmax + 0.5 - xmin) / 50` and its rows
`(zmax + 0.5 - zmin) / 50`, each truncated. Each cell holds one count per nebula kind.

**Settled: the grid's origin and extent are the map's bounds.** The map loader (0x54ca90) reads the map's six bounds
floats with 0x44abb0 (xmin, xmax, ymin, ymax, zmin, zmax into 0x6343f0, 0x6343fc, 0x6343f4, 0x634400, 0x6343f8,
0x634404, the order of the map file's bounds), and builds the grid after it. A layout gives the same bounds, so the
plan treats a layout and a map alike. The static defaults of 0x44a810 (0, 6400, 96000, 102400) hold only before a map
loads. The bounds are in the map's own frame, whose Z is the sandbox's negated. A map file with its rescale flag set
is not read (the map reader rejects it).

## Marking the cells

Placing a nebula (0x451e00) calls 0x43ea20 with the nebula's position, its `effectRadius` (class +0x1ec), and its
kind, and destroying it calls it again to subtract. The routine visits the cells in the box of the radius around the
nebula (clamped to the grid) and adds one to the kind's count in each cell whose center, at height 0, lies within the
radius of the nebula's center in 3D: `(dx)^2 + y^2 + (dz)^2 <= r^2`. The area is therefore a sphere cut by the plane
y = 0 and rasterized by cell centers: a nebula whose height is more than the radius away from y = 0 marks no cell,
one at exactly the radius marks the cell whose center lies right under (or over) it, and a ship at exactly the
radius can still lie outside, as its cell's center does. Counts add, so overlapping nebulae of
a kind raise the count and none of them is applied twice.

## Kinds and their classes

A nebula's kind (class +0x1f0, set by the loader 0x44bfa0) is 1 to 5 when its class name is `mnebula1` to `mnebula5`,
and 5 for any other name. The comparison is byte for byte (an inline string compare in 0x44bfa0), so a class named
`MNebula3` is kind 5. Whether the game changes the case of the class name before it compares is not recovered.

The effect of a kind always comes from the class named `mnebula<kind>` (0x44bf20, found once and cached), never from
the placed nebula's own class, so a nebula of another name acts as `mnebula5`. The placed class's own `effectRadius`
and `pathing_cost` size the area and the path cost.

Keys read by 0x44bfa0 that matter here:

| Key | Offset | Default |
|---|---|---|
| `effectRadius` | +0x1ec | the parent class's value (not recovered) |
| `craft_speed_coefficient` | +0x250 | 0.5 |
| `craft_speed_limit` | +0x254 | -1 (no limit) |
| `pathing_cost` | +0x258 | `CANNOT_PATH_INTO_COST` of RTS_CFG.h less 1 (199 for the stock 200) |

The other keys (damage, the disables, regeneration and repair, the glow and pulse values) are read from the same
class; the sandbox does not apply them (see "Not built").

Stock definitions: `mnebula1` (Radioactive) and `mnebula4` (Metaphasic) set a coefficient of 1; `mnebula2` (Metreon)
sets 0.7; `mnebula3` (Mutara) sets 0.6; `mnebula5` (Cerulean) sets 1. No stock nebula sets a limit. Each has
`effectRadius` 100, and `pathing_cost` 2000 (kinds 1 and 2), 400 (3), 2 (4), and 800 (5).

## What a nebula does to a ship

0x44c520 skips a craft whose byte +0x1b1 is set, which the phase cloak device (vtable 0x5d2404) sets while its state
is 2: a phase-cloaked ship is exempt. **Inferred** meaning of the byte. It caps only a craft whose ship flag
(+0xbc bit 3, 0x4690a0) is set. It does not test for a dying or derelict ship; the cap written for one is not read,
because the no-thrust branch of the physics step reads no cap.

The cap is `Craft_CapSpeed` (0x4690c0, slot 0xfc), called with the kind's coefficient and limit: the ship's terrain
coefficient (+0x4bc) becomes the lower of itself and the coefficient, and for a limit of 0 or more the ship's cap
(+0x4c0) becomes the lower of itself and the limit. Several kinds, and the formation follower (0x4288f0), combine by
minimum. A coefficient above 1 therefore changes nothing. The values return to 1 and no cap after each physics step.

The physics step (0x459440) reads them in its alive branch only, in the forward force: with the coefficient `k` the
forward speed settles at `k` times its full value, and a limit caps it there. The up and side terms read neither, so
only the forward speed is slowed. The Borg branch of the step reads neither, and neither does the no-thrust branch: a
Borg ship and a coasting ship are not slowed. No randomness is on this path.

The sandbox gives the cap to every ship it moves except a ship with Borg physics (whose branch ignores it). No ship
is phased yet (**stand-in**: no ship is exempt on that account).

## Asteroid belts

An asteroid field object's record carries the belt data (loader 0x44f8e0, writer 0x44f9f0): a flag `have_belt`, and when
it is set an offset vector, three floats, and the label of a path. After loading (0x44f9b0) the game looks the label up
among the map's paths (0x4348d0, an exact comparison). 0x44e7b0 keeps the path only when its type is 3, and otherwise
treats the field as a plain field. In a map file the path's type is the integer after its points; it is 3 on every
belt of the stock maps. A layout's fields have no record, so none of them is a belt.

The path planner writes a belt as squares along its path (0x434c30, sampled in steps of 0.01 of the path's parameter,
a square every time the point is more than `ASTEROID_BELT_PATHING_DISTANCE` x 0.5 from the last) and a plain field as a
square around its position (0x44e7b0); the field's `fieldRadius` is not used. The plan carries the points in the
sandbox frame; `docs/rules/path-planning.md`, "Obstacles", states the sampling and the curve between the points.

The sandbox plan reads every asteroid field object as a site, whether or not its rocks can be drawn. A record that
cannot be read, or a label no path of the map has, is a gap naming the class or the label, and the field is planned
without a belt.

## What the sandbox plans

- Every nebula (position, kind, `effectRadius`, `pathing_cost`) and the speed values of each kind in use. A nebula whose
  `effectRadius` is missing or no number is a gap and is left out (**stand-in**: the game takes the parent class's
  value). A speed key or `pathing_cost` that is no number is a gap and takes the game's default; a kind class that is
  missing or cannot be read is a gap and takes the defaults (0.5, no limit).
- The `pathing_cost` default is `CANNOT_PATH_INTO_COST` of the install's RTS_CFG.h less 1: 0x44bfa0 loads the integer
  the constant sets (0x5fb878, set by 0x4453f0) at 0x44c2f4 and subtracts the double 1.0 at 0x44c301. The stock value
  is 200, so the default is 199. Where RTS_CFG.h or the constant cannot be read, the plan takes the value the game
  keeps when RTS_CFG.h gives none, its static 200 at 0x5fb878 (confirmed), and reports one gap.
- The kind classes `mnebula<k>` are read only for the kinds the placed nebulae have.
- Every asteroid field site and each belt's path.
- Nothing new is drawn. The nebulae of a map keep their models.

## Not built

- The nebula's damage, disables, regeneration and repair, light, and pulsing (0x44c520's other effects, 0x4522f0).
- The hide layer (`hideContents`) and the path cost map the game rebuilds from the grid (0x43ec30).
- The speed cap of a non-ship craft, and the phase-cloak exemption (no ship cloaks yet).

## Not known

- The parent class's `effectRadius` default.
- Whether the game changes the case of a class name before the kind comparison, and whether `_ftol` truncates as assumed.
- Which hover craft other than ships carry the ship flag.
