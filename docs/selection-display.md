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

The display holds a hovered unit, a group key, and a display fade, updated once per frame (0x499f70):

- The hovered unit is the unit the pick finds under the cursor. A pick of another unit hovers that unit at
  once. When the pick finds no unit, the last hovered unit stays hovered until 0.4 seconds have passed
  (0x60d7f0). This is the hold. Each pick of a unit, the held one included, restarts the hold.
- The group key is the hovered unit's team, the number at field 0x194. Without a hovered unit it is 0.
  Target validity (0x42faa0) uses that field as a bit index into the target's ally mask. This shows that
  the field is the team.
- The display fade starts at 0 and grows each frame by that frame's time, up to 1. It starts over at 0
  on the frame the group key changes. It keeps growing while the hover moves between units of one team.
  The team compare is the only reset. No hover and a hovered unit of team 0 both give key 0. So the
  first hover of a team-0 unit keeps the fade that grew while nothing was hovered. The tooltip timer
  at 0x78 in the same update starts over when a unit becomes hovered. The display reads only the fade
  at 0x80 (0x49b6a0).

The display walks every object each frame (0x49b6a0). An object that is not shown, or that is off
screen, gets nothing. Of the rest:

- The hovered unit shows the shield bar at full opacity.
- Every other unit of the hovered unit's team shows the shield bar at the display fade. A hovered unit of
  team 0 shows only its own bar. A cloaked craft does not show it.
- A selected unit among those also shows the energy bar, the crew dot, and the system icons, at the
  display fade. A selected unit outside that group shows nothing.

A part draws only while its opacity is above 0.5. Its colors are scaled by the opacity minus 0.5
(0x49abf0, 0x49ad50, 0x49afe0). Read literally, a part at full opacity draws at half its color's
intensity. Whether the game's renderer brightens the colors again is **unknown**.

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
It draws only for a unit whose `maximumCrew` is not 0.

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

The map sandbox draws the shield bar and the crew dot by the rules above, redrawn every frame. It draws
them for the hovered unit and the units of its team, and the crew dots of the selected among them. It
applies the hold and the display fade. With no hovered unit, no unit shows either part, whatever is
selected. The engine's pick is the one a click there would select. No unit is picked while the cursor is
outside the window. No unit is picked in a run without a mouse, such as a `--screenshot` run. In both
cases the display shows nothing once the hold has passed. The cursor counts as inside while the window
confines it. A destroyed unit ends the hover on the frame it is removed.
The engine draws no energy bar, no system icons, and no reticle, and it has no cloaking.

The engine fills these gaps with stand-ins:

- The screen box comes from the box around the unit's meshes in world axes.
- The pick is the click pick, the nearest object under the cursor, in place of the game's own pick.
- A unit shows the shield bar when its `maxHealth` is above 0, in place of the game's own flag and value.
  The crew dot takes the same gate. The game's own gate is `maximumCrew` not 0. It always holds in the
  engine, because a `maximumCrew` missing or 0 takes the stand-in `crewCost`, at least 1.
- A game pixel is as large as the display scale of the screen the window is on. So the display keeps its
  size on a high-density screen.
- A part draws in full-intensity colors at an alpha. The alpha is the opacity minus 0.5, doubled, kept
  within 0 and 1. The game's display brightness is **unknown**. A part shows only while its opacity is
  above 0.5, as in the game.
