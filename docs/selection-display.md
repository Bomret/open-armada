# Selection display

How the game draws a unit's status in the world: the shield bar, the energy bar, the crew dot, and the
system icons around a unit on screen.

The rules were recovered from `Armada.exe` of the 1.2 install (SHA-256
`c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5`) and are restated here in our own
words (INV-0001). Each rule gives the address it was read at as evidence. A rule marked **inferred** is an
interpretation that the program does not label itself. A rule marked **unknown** names a question the
recovery left open.

## Loaded sprites

At start, the display loads these sprites by name from the sprite tables (0x499ac0):

- `selection.00` to `selection.22`: the nine parts of the selection reticle.
- `selectionCircle` and `selectionBillboard`.
- `select_shield_bar`, `select_energy_bar`, and `select_crew_dot`.
- `group_background`.
- `select_system.0` to `select_system.4`: one icon per ship system. Each takes its area from the
  `selectSystem_<n>` key of `misc/gui_glob.cfg`.

The draw rules below fill the shield bar and the crew dot as plain colored rectangles. They do not draw
the `select_shield_bar` or `select_crew_dot` sprites (0x49abf0, 0x49afe0). Where the game uses those
two sprites is **unknown**.

## The unit's screen box

Each part of the display is placed relative to the unit's screen box. The screen box is the smallest
screen rectangle that holds the eight corners of the unit's bounding box, projected to the screen
(0x49a3f0). Whether that bounding box turns with the unit or stays aligned to the world axes is
**unknown**: the routine that projects the corners (0x49a4a0) was not read. Below, `left`, `top`, `right`, and `bottom` are the edges of that box in pixels, and the box
width is `right - left + 1`.

## Which units show it

The display walks every object each frame (0x49b6a0). An object that is not shown, or that is off
screen, gets nothing. Of the rest:

- The unit under the cursor shows the shield bar at full opacity.
- A unit whose owner matches the one the display holds shows the shield bar at the display's own
  opacity. A cloaked craft does not. That the matched value is the owner is **inferred**.
- A selected unit among those also shows the energy bar, the crew dot, and the system icons, at the
  display's opacity.

A part draws only while its opacity is above 0.5. Its colors are scaled by the opacity minus 0.5
(0x49abf0, 0x49ad50, 0x49afe0).

## Shield bar

The shield bar sits above the screen box (0x49abf0):

- It is 4 pixels high (0x60c49c) and as wide as the box. Its top edge is at `top - 4 - 1`.
- A back of dark gray (0.2, 0.2, 0.2) fills the whole bar.
- On it, a fill starts at the left edge. Its width is the box width times the shield ratio, clamped to 0
  to 1.
- The fill color is red at no shields and green at full shields, mixed linearly: red is `1 - ratio`,
  green is `ratio`, blue is 0.
- The bar draws only for a unit with a set flag (bit 3 of the word at 0xbc) and a value at 0x470 above
  0.1. The meaning of both is **unknown**.

## Energy bar

The energy bar sits at the bottom edge of the screen box, on crafts only (0x49ad50). It is 4 pixels
high, as wide as the box, and has the same gray back. Its fill is yellow, and its width is the box width
times the energy ratio. Energy belongs to special weapons, which the engine does not model yet
(`docs/combat-rules.md`).

## Crew dot

The crew dot is a square of 7 by 7 pixels. Its top-left corner is at `right`, `bottom - 8` (0x49afe0).

Its color depends on the crew ratio, the current crew over `maximumCrew`:

- At or below the race's `crewRedStatus`, it takes the red color.
- Else at or below the race's `crewYellowStatus`, it takes the yellow color.
- Else it takes the green color.

A static initializer sets the three colors at start (file offset 0x17f9b8): red is (1, 0, 0) at 0x690b50,
yellow is (1, 1, 0) at 0x690b20, and green is (0, 1, 0) at 0x690b40.

## System icons

Below the screen box, the selected unit shows one icon for each ship system it has, in the order 0, 1, 3,
4, 2 (0x60c4b4, 0x49b6a0). The icons start one bar height below the bottom edge of the box. How an icon
shows the state of its system is **unknown**: its draw method was not read.

## What the engine draws

The map sandbox draws the shield bar and the crew dot of each selected unit, and the shield bar of the unit
under the cursor, by the rules above, redrawn every frame. The unit under the cursor is the one a click there
would select. No unit shows a hover bar while the cursor is outside the window, or in a run without a mouse,
such as a `--screenshot` run. It draws no energy bar, no system icons, and no reticle, and it does not show the display for
the units whose owner matches.

The engine fills three gaps with stand-ins:

- The screen box comes from the box around the unit's meshes in world axes.
- A unit shows the shield bar when its `maxHealth` is above 0, in place of the game's own flag and value.
- A game pixel is as large as the display scale of the screen the window is on, so the display keeps its
  size on a high-density screen.
