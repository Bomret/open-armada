# Forced motion

The rules by which something other than a ship's own thrust moves it: black holes, beams and waves that push or
pull, and the separation of ships that overlap. Recovered from the 1.2 `Armada.exe`
(SHA-256 `c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5`). Each rule names its address as
evidence (ADR 0004, INV-0001). A rule the code does not confirm is marked **inferred** or **stand-in**.
`docs/rules/combat-rules.md` holds how a ship without thrust coasts ("Coasting") and how a ship dies ("Dying
units").

Stock values are from the 1.2 install's definitions. Offsets are of the unit object unless a class is named.

## Motion state

The game keeps a ship's motion in the unit, and its physics step (0x459440) integrates it.

- World velocity at +0x140, its length (the speed) at +0x138, and 1/speed at +0x13c, or 1e30 when the speed is at
  most 1e-4. Pitch, yaw, and roll rates at +0x14c, and the angles at +0x768. The world center at +0x12c is the
  position plus the rotated local center (0x471770). Every mover below measures from the world center.
- The physics step builds its accelerations from scratch each step and adds them into the velocity it finds. A
  velocity that a mover writes therefore carries into the next step: a ship without thrust brakes from it at
  2.0 per second per second, and a ship with thrust steers out of it. The step skips a ship in a queue (+0x67c
  not 0, for example docking or a wormhole) and a starbase (class +0x235, `is_starbase`).
- A ship whose engines are out (+0x518 is 0) or that is derelict (+0x4dc) has no thrust and coasts. The movers
  that disable the engines (0x469120, the `SpecialObjectEffect` of type 2, built by 0x561290) make a ship coast
  and expose it to the black hole and the tractor beam. A unit with +0x678 set ignores the disable (meaning of
  +0x678 unknown).

## Tick order

One game tick (0x43f1a0) runs these passes over all objects, in this order:

1. The area pass (0x4713c0, slot 0x8c): a black hole swallows the ships at its event horizon.
2. The simulate pass (0x471410, slot 0x84): the ship physics step and the black hole's pull.
3. The post-simulate pass (0x471560, slot 0x88), which skips dying units: for a ship (0x465e80), its systems and
   then the collision separation (0x464910, slot 0x110).

The ordnance pass (0x4ed9a0, slot 0x44) runs after the tick in 0x43f2b0, so the tractor beam and the other
specials write velocity after that tick's physics step. The order of black holes and ships inside the simulate
pass is unknown.

## Black hole

Class `blackhole_obj` (BlackHoleClass, keys at 0x44b7d0). Keys and defaults: `GravityRadius` 50 (class
+0x1f0), `EventRadius` 10 (class +0x1ec), `GravityForce` 50 (class +0x1f8), `Rotation` 0.02, `ScaleSize` 1. The
stock `mblkhole` sets `GravityRadius` 450, `EventRadius` 70, and `distanceBelowGrid` 70, and keeps the default
`GravityForce` of 50.

### Pull

Each simulate tick (0x450950 turns the model by `Rotation` times the step, then 0x450a80):

- A ship is pulled when its 3D distance d from the hole's center is at most R (`GravityRadius`) **and** its
  engines are out or it is derelict (0x450bcc). A ship with working engines and crew is not pulled. The test does
  not look at the dying flag.
- The pull adds to the velocity: Δv = (direction to the hole's center) × F × step, with F = `GravityForce` × R² /
  d², or `GravityForce` × R² when d² is below 1.0.
- The new velocity is capped at the ship's speed cap, `impulseSpeed` (+0x4a8) times the warp speed multiplier
  (+0x4ac): a longer velocity keeps its direction at the cap's length (0x450f40, 0x450ea0).
- The direction is 3D. That the hole's center lies `distanceBelowGrid` below the plane, so the pull has a
  downward part, is **inferred**: the class's value (+0x1a8) lowers the placed height when `moveBelowCombatArea`
  is set, to `SHIP_COMBAT_FLOOR` − `distanceBelowGrid` (0x46d5c0).

### Push-out

A ship whose engines work and that is not derelict is moved out of a ring around the hole (0x4644b0, from the
collision pass): when the plane distance between the positions is below 1.5 × `EventRadius` plus the ship's
radius (+0x60), the ship's position moves straight outward by the overlap. Height and velocity stay. The stock
ring is 105. The same routine moves ships out of asteroid fields and planets (0x4644b0, object flags 0x8000 and
0x100000), with the object's radius (+0x60) in place of the ring: an asteroid field's radius is its definition's
`fieldRadius`, a planet's its model radius. The routine runs for every non-ship, non-dying neighbor of a ship's
collision pass. The black hole's ring takes ships with working engines only (the routine tests the engines, +0x518,
in that branch alone). The field and planet branch has no engine test, and the pass (0x465e80, slot 0x110 =
0x464910) skips only a dying (+0x1bf) or derelict (+0x4dc) ship: a coasting ship (engines destroyed) is pushed out
of a field or a planet like any other ship, and a derelict or dying ship enters one unhindered. The pass's entry also
tests a flag at +0xd8, which is unidentified; that it is clear for a coasting ship is **inferred**.

The engine's stand-ins for the field and planet push-out:

- **Every obstacle is tested** against every ship that runs the pass (neither dying nor derelict, and with a
  motion), after that ship's contacts with other ships. How the game's grid box holds a large non-ship object is
  not recovered.
- **Centers on one plane point** have no direction, and the engine pushes nothing (the game divides by the zero
  distance).
- **A field's radius** gates its push alone: a field with a valid `fieldRadius` pushes whether or not its rocks
  can be drawn; a field whose radius is missing, not a number, not above 0, or not finite is reported and pushes
  nothing. A planet takes its model's bounding radius; a planet without a model is not placed and pushes nothing.

The overlap test is `0.0 < (r + ship radius)² − d²`, with the plane distance d: the float at 0x5ca16c is 0.0 (read
from `Armada.exe`'s `.rdata`), so the push runs when (r + ship radius)² is above the squared plane distance.

A push-out goes through the push route, so a ship ordered to a point inside a field or a planet and left within
`tooCloseToTurn` of it by the push ends its order and stops at the edge; one held off farther rests at the edge
with its order open, with no back and forth: it stays on the ring at the same distance from the center and slides
slowly along it (`play/push-out.play`, a field; `play/push-out-planet.play`, a planet).

### Event horizon

Each area tick, the hole gathers every unit, ship or station, that is not dying and whose center lies within
`EventRadius` in the plane (0x44da40, gather 0x44db30) and swallows it (0x450a40, slot 0xc8):

- A hole whose `EventRadius` is 0 gathers nothing (0x44da40 skips the horizon when the radius equals 0).
- It sets the black-hole death flag (+0x624) and the hole's center (+0x628), and makes the unit a dying unit
  (0x468990).
- The dying timer is 0 (0x466090), so the unit ends on the next tick. It plays no `fireball`; its model shatters
  into debris pieces (0x4663d0 → 0x58cef0). The pieces store the hole's center and never read it, so the debris is
  the same as for any other ship (`docs/rules/debris.md`, "The shatter").

A ship with working engines never reaches the horizon because of the push-out. A ship without thrust is pulled
in and swallowed.

## Tractor beam

Ordnance class `tractor_bm` (keys at 0x4e7b00): `targetRange` (class +0x120, default 60). Stock: `tracbm` 100,
`tracbmo` 25 with `omegaType` 1.

- The weapon only takes a target whose engines are out, that is derelict, or that carries a slow effect, and
  that no other beam holds (0x4664e0).
- Each ordnance tick (0x4e7fb0, then 0x4e7df0): when the distance d between the world centers is above
  `targetRange`, the target's velocity is **replaced** by (direction to the shooter) × (d − `targetRange`). At
  `targetRange` or nearer, nothing is written.
- The beam ends when the shooter is dying (0x4ee5f0), when the target is no ship or is a starbase, and when the
  target's engines work, it is not derelict, and it carries no slow effect (0x471b30). No test looks at the
  target's dying flag, so that a held ship that dies is pulled on is **inferred**.

## Repulsion field

Ordnance class `rfield` (keys at 0x4e42f0): `repulsionForce` (class +0x120, default 10), `specialValue`. Stock:
`repulsionForce` 50, `specialValue` 20.

On a hit (0x4e44a0, once), unless the target is a starbase:

- The engines are out for `specialValue` seconds (0x561290, type 2).
- The velocity is **replaced** by (dx × f, 0, dz × f), with (dx, dy, dz) the 3D unit direction from the shooter's
  center to the target's center and f = `repulsionForce`. A height difference shortens the push.

The ship then coasts, braking at 2.0 per second per second, while its engines are out. Whether a hit on a dying
ship reaches this handler is not recovered.

## Repulsion wave and shockwave cannon

The `repulsionwave` special (stock `rwave`, `explosionRadius` 800) starts its shooter's dying phase and spawns
the shockwave `wrwave` (0x4e5080). That the wave's full radius is `explosionRadius` is **inferred**.

The shockwave (`shockwavecannon`, keys at 0x51cb90): `rate` (class +0xfc), `waveArc` (default 90), `disableTime`
(default 10), `repulsionForce` (default 50). Stock `wrwave`: `rate` 35, `waveArc` 90, `disableTime` 20,
`repulsionForce` 30, `damageBase` 2000.

- The wave front's radius is 2 × `rate` × the time since the wave started (0x51c4d0). It ends when the radius
  passes the full radius (0x51d570).
- Each tick it acts on every ship that is not dying (0x40f4d0) and not phased, inside the front's radius and
  inside the arc in the plane (0x51c4d0), and does three things (0x51c8d0):
  - a hit of class +0x60 × step (that +0x60 is `damageBase` is **inferred**);
  - unless the ship is a starbase: engines out for `disableTime`;
  - unless the ship is a starbase: velocity **replaced** by (3D direction from the wave's origin to the ship's
    center) × `repulsionForce`.
- The wave keeps no list of ships it has hit, so it pushes a ship again on every tick while the ship is inside.

## Holding beam

Ordnance class `holding_bm` (stock `holdbm`: `targetRange` 300). The first hit records the target's transform
(0x4ddd30). Each tick within range it disables the engines for 1 s and sets the transform back to the recorded
one, so the ship's position and heading stay frozen. Its velocity is not cleared. The beam takes only ships that
are not dying.

## Collision separation

Each post-simulate tick, a ship A runs the pass (0x464910) when it is not a starbase (class +0x235), its +0xd8 is
not 0, and it is not phased (+0x1b1 is 0). A dying ship never runs it (0x471560 skips it). The neighbors B are the
ships in a grid box of ±3.0 × A's model radius (+0x60; 0x5cb170) that are not A, not dying (+0x1bf), not phased,
and have +0xd8 not 0.

**Contact** (slot 0x108, 0x464000). cA and cB are the world centers (+0x12c), rA and rB the model radii, and
d = cA − cB.

- No contact when |d|² is above (1.5 × (rA + rB))² (the double at 0x5ca8b8).
- When |d| is below 0.01, the normal N is (0, rA, 0) and the penetration is not written, so the turn reads a
  stale value.
- Otherwise u = d / |d| and the penetration is pen = |sA − cA| + |sB − cB| − |d|, with sA and sB the hull surface
  points. The ships touch only when pen is above 0. N = u × pen, from B toward A.

**The hull surface point** (0x463f60) lies on the unit's shield ellipsoid (radii and center from 0x46b8f0; see
`docs/rules/shield-effects.md`, "Shield ellipsoid"). The probe point is pA = cA − 4 × rA × u for A (the 4.0 at
0x5ca760) and pB = cB + 4 × rB × u for B.

- **Quirk:** the ellipsoid's radius is sampled along the normalized *world* vector of the probe point, its
  direction from the map origin, not along the line to the other ship. |s − c| = |R × û| with û = normalize(p).
  The inverse world matrix is computed and never used.

**Shares** (0x464b0e to 0x464bb4), sA for A and sB for B:

- A is in a queue (+0x67c not 0): sA = 0 and sB = 1.
- Else B is in a queue: sA = 1 and sB = 0.
- Else |vA| + |vB| below 0.01: 0.5 each.
- Else sA = |vA| / (|vA| + |vB|) and sB = |vB| / (|vA| + |vB|).

The speed is the velocity's length (+0x140), not a position change.

**Vertical push** (0x464bb7 to 0x464c28).

- The base b is 0.42 (0x5ccddc), plus 0.42 when A's class sets `borgPhysics` (class +0x1ec), plus 0.42 when B's
  does.
- The sign σ is −1 when A's height (+0x90) is below B's (strict), else +1. Ships at equal heights push A up.
- V = (N.x, N.y + σ × b, N.z). The 0.42 is world units. It is not scaled by pen, and the share below scales it.

**Move.** A that is not a starbase: its position (slot 0xc) gains sA × V, and then the turn 0x464620 runs with
(A, B, sA, V, pen). B that is not a starbase: its position loses sB × V, and then the turn runs with
(B, A, sB, −V, pen). A starbase's share is lost. Velocity is never written. Each ship runs its own pass, so a pair
is **inferred** to be handled twice per tick.

**The turn** (0x464620), for ship S and the other ship O:

- It returns when the share is at most 0.1 (0x5caa18) or S's class sets `borgPhysics`.
- f is S's forward axis (+0x80) and e = S's position − O's position (+0x8c). α = |acos(f · (−e) / (|f| |e|))|. The
  turn goes on only when α is below 1.57 (0x5ccf18).
- The axis is a = normalize(f × V), with no guard for a zero length. The angle is φ = (pen / |e|) ×
  `MAX_PREVENTION_ROTATION` / α. The nose turns toward V, away from the other ship. The share gates the turn and
  does not scale the angle.
- `MAX_PREVENTION_ROTATION` is read from `RTS_CFG.h` (0x4453f0). The stock value is 0.6 and the compiled default
  is 0.4.

**No height servo.** The shipped game has none. An idle ship pushed vertically keeps its height: the idle hold
(0x428081, then 0x42e4a0) zeroes the lift control, and the hover and height servos have no callers
(`docs/rules/flight-model.md`, "The servos").

**The position clamp.** The position write (slot 0xc, 0x471640) clamps the height to a band around
`SHIP_COMBAT_FLOOR` and `SHIP_COMBAT_CEILING` when the flag at +0x488 is set. That flag is not identified. The
engine does not reproduce the clamp.

### Stand-ins in the engine

- **Heading-only turn** (ADR 0009): the engine applies only the heading (yaw) part of the turn's rotation.
- **No warp multiplier:** the engine's speed cap in the black hole's pull uses a multiplier of 1.
- **The hole's height** is the **inferred** one of "Black hole". The engine lowers the placed position by
  `distanceBelowGrid` whenever the definition gives one (the `moveBelowCombatArea` test and the combat floor of
  0x46d5c0 are not read), and by 0 when it gives none.
- **The black hole's tick.** The engine runs the event horizon first in a tick (after the shots, before the motions),
  then the motions, then the pull, then the separation pass, whose ring push-out takes ships with thrust only
  (engines working and not derelict: a ship the pass runs for is never dying). The order of holes and ships in the
  game's simulate pass is not recovered; the engine takes the plan's order of holes and the unit order of ships.
  Several holes add: each pull is capped at the speed cap in turn, as 0x450ea0 caps per call.
- **A huge force or radius** does not overflow the velocity: the engine computes the force in double precision and
  holds one tick's change at 1e18, so a finite `GravityForce` or `GravityRadius` however large ends at the speed
  cap with a finite velocity (the game's float arithmetic is not recovered for such values).
- **A ship at the exact center** has no direction, so its velocity stays as it was and stays finite. Within 1 unit
  of the center the force is `GravityForce` × R² along the direction to the center (0x450bcc).
- **The event horizon** takes the plane distance squared at or under the radius squared (0x44db30 compares with
  `<=`), for every unit of the world, ship or station. A swallowed unit becomes a dying unit with a timer of 0, so
  it leaves in the next tick through the dying phase, with no small explosions (the game's dying phase shows them
  every 0.05 s; a swallowed unit's timer runs out before the first) and a `UnitDestroyed` whose cause is
  `BlackHole`, so the view plays no fireball and no sound. It shatters as any ending unit does
  (`docs/rules/debris.md`); debris takes no pull from a hole, since the pull reads ships only.
- **The hole's keys.** A key the definition lacks takes the game's default (`GravityRadius` 50, `EventRadius` 10,
  `GravityForce` 50, `Rotation` 0.02, `distanceBelowGrid` 0) and is reported once per cause with its
  `class: key`. The stock `mblkhole` lacks `GravityForce`, so each stock map with a hole shows that one gap. A
  value that is no number, or a radius below 0, is reported and takes the default (the game's reading of a bad
  value is not recovered). A hole whose model the install lacks is not placed and has no pull.
- **The spin** is `Rotation` radians per second of world time about the scene's vertical axis, positive about
  Godot's +Y; the game's sign is not recovered. The turn is a pure function of the world's time (ticks run plus the
  part of a tick not yet run), so it does not depend on the frame rate.
- **A unit without a shield ellipsoid** gets a sphere of its model radius for the hull surface point.
- **The centers** cA and cB are the units' positions. The turn reads the positions after A's move.
- **The pass's ships:** a unit without a motion (a station) and a derelict ship run no pass, and a unit that is
  not dying is a neighbor. The engine has no phased state. The neighbor box is on X and Z.
- **A head-on push turns no heading.** The rotation about the axis of the nose and the push is a pitch for a
  ship pushed straight along its nose, and the engine keeps only the heading part. A ship that drives head-on into
  another keeps its heading and slides up over it, since nothing in the engine holds a flying height. A ship held
  off farther than its `tooCloseToTurn` from the point keeps its order open, is lifted by the separation's
  vertical push, passes over the other ship, and ends its order by arrival above it; neither moves afterwards. The
  reasoning: the game's separation is position-only, with the fixed 0.42 vertical push and no height hold (ADR
  0009), so the game very likely climbs too. That game-side climb is **inferred, not observed**.
- **The turn rotates a moving ship's velocity.** A ship with thrust takes its velocity from its heading, so the
  collision turn changes the direction of its velocity (the speed stays). The game's turn writes only the
  orientation, never the velocity (+0x140).
- **`borgPhysics`** is read from the physics definition as a number other than 0.

The avoidance force of `Collision.cfg` (0x436580, 0x435600) is steering of the unit AI. The stock file sets
`COLLISION_REAL_COLLISIONS 0`.

## Move-order arrival and hold

A push and a separation touch no order state (0x464910 and its callees read and write none). The order's own
update ends it, and the idle handler holds the spot afterwards.

- **Arrival** (the move order's update, 0x428170). Each tick, before the path follower, the order posts the stop
  order (0x46eac0) when the 3D distance to the path's last point is under `tooCloseToTurn` (class +0x208) **and**
  the speed (+0x138) is under 0.5 × `impulseSpeed` (+0x4a8; the 0.5 at 0x5ca230). A looping path skips the test.
  The stop order is a one-point path at the ship's own position. The same tick ends the order of a ship that a
  separation or a push-out leaves inside the radius and slowed down; one held off beyond the radius keeps its order
  and replans its path (the stuck rule in 0x42ded0 never ends an order).
- **`tooCloseToTurn`** is a key of the physics definition (read by 0x455440). The stock definitions set 50, and
  `destphys.odf` sets 40. The compiled default is 25 (0x45fbe0, which also sets `tooCloseForWarp` 100,
  `tooSlowToTurn` 1, and `pathLeadDistance` 30).
- **Hold** (the idle handler 0x42a170, through 0x428081 and the move maneuver 0x42e4a0). A ship with no order
  holds the point of its last path, its tether point, at every alert stance, with or without weapons. Within
  `tooCloseToTurn` of the point the maneuver zeroes the control vector and brakes the throttle along the forward
  axis (a critically damped brake, 0x4657b0, 0x4699e0) and the ship stays. Beyond it the ship steers back. That
  a ship pushed beyond the radius steers back to the point is **inferred** from the maneuver's structure. The
  game's attack maneuver (0x42c400) is not traced.
- **Group move.** Several ships ordered to one point never share it; see "Group move" below.

### Stand-ins in the engine

- **Arrival by a push** (ADR 0009): the kinematic model has no brake, so a push that leaves a ship strictly under
  `tooCloseToTurn` from the order's point stands for "slow and near": the order ends where the ship stands, the
  motion's target ends with it, and that spot, its height included, becomes the tether point. A push that leaves it
  at or beyond the radius leaves the order open. A ship that drives to its point ends the order on reaching it,
  as before.
- **The hold** has no brake: a ship nearer than `tooCloseToTurn` to its tether point does not move at all, and a
  ship at or beyond it drives back and stops on reaching the point (or on a push that leaves it inside the
  radius, which makes that spot the tether point). A change of stance does not end the return. Red's close-on-pick
  within 300 units of the tether point keeps precedence, and red's return after closing is the same hold.
- **`tooCloseToTurn` default:** a definition without the key, or whose value is not a positive number, takes 25
  and is reported once per class as a plan gap. The stock `destphys.odf` writes `tooCloseToTurn = 40;` with a
  closing semicolon, so the engine takes one trailing `;` off the value (the game's read of the number's prefix is
  **inferred**).
- A push on a ship without thrust ends no order, and a push on a ship with an attack order leaves it alone.

## Group move

The player's command over the selection (0x480c30; callers 0x4ad3d0, 0x4ad5c0, and the minimap click 0x485a10) is
a group move when it is a move and the selection holds more than one unit. Any other command goes to each ship
alone. The sandbox's right-click and a play script's `move` step aim the same click.

- **Who takes part.** A ship joins unless it is a starbase, has disabled engines, or is a derelict (0x4848e0); such
  a ship gets no order from the command. A ship diverted to an attack by a click on a hostile unit is no part of
  it. One ship that takes part goes to the click itself.
- **Heading** (0x47fde0). The plain arithmetic mean of each ship's bearing to the click, atan2(click z − ship z,
  click x − ship x), with no correction where bearings straddle the ±π cut. It averages over every selected ship
  the command was given (a ship diverted to an attack is no part of it), starbases, derelicts and ships with
  disabled engines included; only the grouping leaves those out.
- **Groups** (0x47fe90). In selection order, a ship joins the existing group whose running mean centre is nearest
  (3D) when that distance is under 800 (the first group wins a tie), else starts a new group. Nothing groups by
  class or size.
- **Layout of the groups** (0x480890, 0x480790). The first group's origin is the click. Each next origin is the
  previous one plus the previous group's side × `FORMATION_SPACING` along (sin heading, 0, −cos heading): the
  groups stand side by side, perpendicular to the mean heading, with no gap. A group's side is the root of its
  member count, rounded up.
- **Grid** (0x480080, 0x480400, 0x4805d0). The cells are `FORMATION_SPACING` apart and axis-aligned (the heading
  never turns the grid), enumerated row by row from the row of largest Z, each row from the smallest X. The
  assignment is cell first: with the block anchored at the group's smallest ship X and Z and at the middle of the
  combat band, (`SHIP_COMBAT_FLOOR` + `SHIP_COMBAT_CEILING`) / 2 (38.5 on the install), each cell in turn takes the
  nearest unassigned ship (3D, to its current position); a tie goes to the earlier ship in selection order, and the
  trailing cells stay empty. A ship's destination is the group's origin shifted by (column − side div 2, row
  counted up from the bottom row − side div 2) × `FORMATION_SPACING`, integer division, so an even side is half a
  cell off-centre.
- **Orders** (0x4805d0, 0x428560). The ship that fills the leader's cell gets a move order to its cell; every
  other ship a follow order on the leader with its cell's offset from the leader's. With the stock
  `DEFAULT_TO_FORMATION_MOVE` of 0 and the main right-click handler, each follower's order turns, once its
  countdown of three updates ends, into a plain move along the leader's path shifted by its offset (0x428560), and
  nothing ties it to the leader afterwards.
- **Constants** (0x4453f0). `FORMATION_SPACING` is 100 in the install's `RTS_CFG.h`; the compiled default is 40.
  `FORMATION_LEADER_SLOWDOWN` and `FORMATIONS_RELATIVE_TO_SHIP` act only when the follow order is kept
  (`DEFAULT_TO_FORMATION_MOVE` not 0), which the stock configuration never does.

### Stand-ins in the engine

- **Arrangement.** The taking ships' Z is the game's: the engine mirrors the sandbox world's Z into the game's frame
  for the layout and back, so "largest Z" and the heading's perpendicular are the game's.
- **Taking part** is decided by the engine's states: a unit that is no mobile ship, a derelict, one with destroyed
  engines, a dying one, or a coasting one. The game's cloak equality in the grouping test and its
  `|maxX − minX| × 999` bound are left out (the sandbox has no cloak, and the bound never binds beside 800).
- **One move order per cell.** The leader's order and the followers' follow orders reduce to one move order per
  ship to its cell (arrival as in "Move-order arrival and hold"), since the stock `DEFAULT_TO_FORMATION_MOVE` of 0
  makes a follower a plain mover and the sandbox's targets are points. Followers start at once, not after the
  countdown of three updates. The tether point is the cell at the ship's own height.
- **Height.** Each ship keeps its own height; the game's flight model steers it (ADR 0009), which the engine does
  not yet.
- **Selection order** is the order the sandbox lists its selected units: a box takes them in the scene's unit
  order, which the layout's object order fixes, so every run of a script sees the same order.
- **A cell outside the map** goes to the nearest point of the map's edge, as a single ship's click does; two
  cells so clamped may coincide.
- **`FORMATION_SPACING`** is read from `RTS_CFG.h` by the combat plan. A value missing, not a number, or no finite
  number above 0 takes the compiled default 40 and is reported once. `SHIP_COMBAT_FLOOR` and `SHIP_COMBAT_CEILING`
  are read as the camera reads them.
- The empty group a rejected ship leaves in the game, and the group centre's initial value, do not arise: only the
  taking ships are grouped.

## What does not move a ship

- **Hits.** A weapon hit, a torpedo impact, and a critical hit change no velocity, rate, or position (0x465100;
  the shot carries no direction).
- **Explosions.** The `fireball`, system explosions, the self-destruct explosion (0x508fc0, 0x4663d0), and the
  shockwave explosion (0x51d2a0, damage only) push nothing.
- **Debris.** The pieces of a ship that ends do not take its velocity: 0x4663d0 passes the velocity (× 1.0, or ×
  2.5 for the `selfDestruct` explosion) to 0x58cef0 (its `this` is the unit), and the piece spawner 0x57cf90
  never reads that argument (`docs/rules/debris.md`).
- **Gravity mine.** `gravmine` lowers the warp speed multiplier, m → 1 + (m − 1) × `slowPercent` / 100 (0x4dd720;
  stock `slowPercent` 50). It moves nothing itself, but a slowed ship can be held by a tractor beam.
- **Nebulas** cap the speed (0x44c520, 0x4690c0). **Ion storms** only damage (0x451870).

## Dying ships

A dying ship coasts (`docs/rules/combat-rules.md`, "Coasting") and takes no collision separation (0x471560
skips it). Of the movers above:

| Mover | Acts on a dying ship |
|---|---|
| Black hole pull | Yes, when its engines are out or it is derelict |
| Black hole push-out and event horizon | No |
| Tractor beam | **Inferred** yes, once it holds the ship |
| Repulsion field | Not recovered |
| Shockwave | No (0x40f4d0) |
| Holding beam | No |

## Not known

- Which key the group move's modifier byte (0x65ac58) is, and what 0x471d90, which the command calls for each
  selected ship afterwards, does.
- The order of black holes and ships in the simulate pass.
- What the flag at +0x488 (the position clamp's gate) is.
- Whether the repulsion field's hit reaches a dying ship.
- What the tractor beam's `targetSpeed` key (default 2) does; nothing in the beam reads it.
- Wormhole and docking transport (queue routines 0x557e20, 0x558240, 0x557400) is only skimmed: a ship in a
  queue has its velocity zeroed (0x462dd0) and is moved along a curve that writes its transform.
