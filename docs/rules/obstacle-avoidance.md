# Obstacle avoidance

The rules of the avoidance scorer in the 1.2 `Armada.exe`
(SHA-256 `c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5`): how a ship's steering direction
(`docs/rules/flight-model.md`, "The steering routine") is rewritten so the ship passes the objects near its way. Each
rule names its address as evidence (ADR 0004, INV-0001). A rule the code does not confirm is marked **inferred** or
**stand-in** (INV-0003).

Offsets are of the unit object (the ship is `S`, a neighbour `o`). The scorer's code is 0x42ef70
(`CraftProcess_AvoidanceForce`); the steering direction 0x42ebb0 calls it between building the direction and the height
clamp, with no object to ignore and no limit by the goal's distance. The three other callers (0x42cf90, 0x42d2b0,
0x42dc30) are not built.

## The switch

`OBSTACLE_AVOIDANCE` of `RTS_CFG.h` (0x5fbc30, read by 0x4453f0) gates the scorer. The game's compiled default is 1; the
stock install's `RTS_CFG.h` sets 0 (line 272), so the stock game has no avoidance beyond the height clamp. A second
switch (byte 0x5f85b8, 1, set by the export `ObstacleAvoidence(bool)` 0x438b60) is always on. A ship whose byte +0x1b1
is set (set by the phase cloak device 0x508130 while it is in mode 2) does no avoidance; no such ship exists in the
engine.

The engine reads the key through the combat plan. A key the install's `RTS_CFG.h` lacks, or gives as no finite number,
takes the game's compiled default and is reported once; so do `COLLISION_LOOK_AHEAD_TIME` (0x5fbc34, default 4.0, stock
1.4) and `COLLISION_SAFETY_MARGIN` (0x5fbc38, default 1.5, stock 1.2), which only the asteroid-field prediction reads.
A missing `RTS_CFG.h` takes all three defaults, so avoidance is on. Off, the engine's steering seam is empty: no
neighbour changes the direction.

## The scorer

Each static tunable below is the game's own value at the address named; none is a stand-in. They are fixed in the
shipped game: nothing but the debug dialog 0x494c70 writes them, and no file read sets them.

1. The look-ahead is `4 × speed` (the 4.0 at 0x5f8478, the speed +0x138), the ship's present speed.
2. The neighbours are the objects the query finds (below), each tested in this order: not `S` (the engine's neighbour list leaves the ship
   out); `avoidMe` set (+0xd8, the class key, default 1); not phase-cloaked; dying objects are not left out. The engine
   reads each unit class's `avoidMe`, `avoidanceClass`, and `collisionRadius` from its definition: a key it does not set
   takes the class default (1, 1000, none), and one that is no number (no whole number for the first two) takes the
   default and is reported once per class. **stand-in**: the engine reports a `collisionRadius` that is not finite
   (infinity, or beyond a float's range) as no number too, where the game takes any value above 0, infinity included;
   flight must stay finite.
3. An asteroid field goes through the hit prediction (below). A hit ends the scorer at once with the direction
   unchanged. A field that is not hit is not scored (byte 0x632e76, 0, scores fields when set).
4. Any other object passes when its `avoidanceClass` (+0x1ac, default 1000) is at least `S`'s, or its speed is at most 1.0
   (byte 0x5f8462, 1). A byte 0x632e74 (0) would pass every class.
5. The gap `|C_S − C_o| − r_S − r_o` must be at most the look-ahead, `r` being the model bounding radius (+0x60).
6. The threat test: the movement `v = d × speed` (the wanted direction `d`, not normalized) against `o` by the closest
   approach; `o` threatens when the time `t` is above 0 and at most 4.0 (the same 4.0).
7. The first threat builds the candidates; every threat scores them.
8. With no threat the direction stays as it was.

The game also skips a threat that is the unit the ship's move process names, a unit whose process names the same
unit, and a unit whose process names `S` (the process handle +0x7c against +0xc4 and +0xe4's +0x7c; **inferred**). The
engine's move orders name no unit, so the skip never applies; it is not built.

### The neighbour query

The neighbour grid (0x65a308) is rebuilt every frame (0x470940, called by 0x43f1a0): each object goes into one cell by
its center (+0x8c, +0x94, the x and z), and the grid keeps the largest `r` of the objects registered. The query (0x40e990)
takes the box around `S`'s center of half-side `r_S + look-ahead`, widened on each side by that largest radius, in x
and z only. **stand-in**: the engine tests the box exactly on each object's center; the game works in whole cells.

### The closest approach

0x424860 gives, for a ship's movement `w` against `o`, the time `t` and the squared distance `dmin²` of the least
distance between the two centers over the relative path, for all `t` (the callers cut the window). With `d = C_S − C_o`:

- `o`, a ship that is no station (class byte +0x235, `is_starbase`): `w` loses `o`'s velocity (+0x140) where `o` has no
  move target; with one more than 1 away, `w` loses `0.2 × velocity + 0.8 × speed × (unit vector to the target)`
  (0x5f8474 is 0.2); a target within 1 takes nothing off (`o` counts as still). The target test is `o`'s move process
  (+0xe4) with its byte +0xc0 set, the target its +0xb4 (**inferred**); the engine's target is the goal of a move order or
  the point of a move intent.
- Every other object, and a station: `o` counts as still.
- With `|w|² ≥ 0.001` (0x5ca750): `t = −(w·d) / |w|²` and `dmin² = |d + w t|²`; below it `t = 0` and `dmin² = |d|²`.

### The candidates

0x424ba0 builds ten directions from the wanted one, `d` normalized. With `a = π/12`, 15° (0x5ca740, 0x5ca768), and the
turns about the vertical axis that keep the height:

| Index | Direction |
|---|---|
| 0 | `d` |
| 1, 2 | `d` turned by `a` left, right |
| 3, 4 | turned by `2a` (30°) left, right |
| 5, 6 | turned by `4a` (60°, the 4.0 at 0x5ca760) left, right |
| 7 | the ship's forward axis (+0x80) |
| 8, 9 | `d` pitched up, down by `a`, by the rotation about `h = (−d.z, 0, d.x)` (not normalized, so only a horizontal `d` gives unit length) |

The left turn of `d = (x, y, z)` by θ is `(x cos θ − z sin θ, y, z cos θ + x sin θ)` in the game's frame, and the right
turn the same with the signs of the sine terms swapped. In the game's frame, with the bow at +Z and +X the ship's right
as drawn (the sandbox's X is the game's X), the left turn goes toward −X: it is a left turn of the ship **as the sandbox
draws it**. The engine runs the scorer in the game's frame and mirrors the result back.

Each candidate starts at `100 × (c·d)` (0x5f8468). A candidate's test velocity is `c × max(speed, 10)` (0x5ca55c). The
veto of the forward candidate when it points against `d` (byte 0x5f8460) is off in the game, so index 7 is never vetoed.

### The scoring

0x4250e0 runs for every threat `o`, for each candidate `k`: the closest approach of the test velocity against `o`; where
`t ≥ 0` and `dmin² < R²`, with `R = (r_o + r_S) × 1.1` (0x5f845c), the score gains
`−100 × w × R² / dmin²` (0x5f8464 is −100), `w` being 2.0 (0x5f8458) for an `o` of speed below 1.0 (0x5ca238) and 1.0
otherwise: **a slower neighbour weighs double**. A `dmin²` of 0 gives minus infinity. After the loop, where `o` is a ship
that is a station (and where byte 0x5f8461, 1, were 0), the vertical candidates 8 and 9 are set to −1e5 (the immediate
0xc7c35000): there is no dodging a station up or down.

### The pick

With threats, the scorer then adds the hysteresis (0x424b50): `u` the unit vector of the last result (`+0x49c`); each
candidate with `c·u > 0` gains `50 × (c·u)` (0x5f8470, byte 0x5f8463 is 1). **The last pick is favoured.** With a zero
last result the unit is not a number and no candidate gains; that is the engine's start (**stand-in**: the game's initial
`+0x49c` is not recovered; a zero gives no bonus on the first call). The best score takes the candidate (0x425200): the
search starts at −1e30 (0x5ca770) and takes a score only when strictly above, so **the lowest index wins a tie**. The
result replaces the direction: the candidate, scaled by 0.75 (0x5f846c) where the best score is below 0. There is no
blend with the wanted direction. The result, or the wanted direction where nothing threatened, is stored as the last
result (before the height clamp).

The game also, for a path-following move process (type 0x27, +0x7c null) within 100 of its last point, slower than half
of `impulseSpeed` × the warp multiplier, and with candidate 0 scoring below 0, calls 0x46eac0 with 2, which sets +0x110
and restarts a timer; its meaning is not recovered, and the engine does not call it (**stand-in**).

A wanted direction of zero (the ship at its goal) returns zero at once and is stored as the last result; the game's
division by the zero length gives candidates that are not numbers (**stand-in**).

## The asteroid-field hit prediction

For an object with the asteroid-field flag (+0xbc bit 0x8000), 0x42ed20 asks whether the ship hits it:

1. The ship moves at its impulse speed (+0x4a8; times the warp multiplier +0x4ac unless +0x688 is set) along the unit
   wanted direction. The engine takes the warp multiplier as 1 (**stand-in**).
2. The swept-sphere test 0x4353b0 runs on the two spheres, centers `C_S` and `C_f`, velocities that movement and the
   field's (+0x140), radii the collision radii (+0x170: the definition's `collisionRadius` where above 0, else the model
   radius +0x60; the engine reads the key of the ship's class as well as the field's), over `COLLISION_LOOK_AHEAD_TIME` `T` with the radii's sum scaled by `COLLISION_SAFETY_MARGIN` `m`.
   It is a hit when the centers are closer than that reach; no hit when the gap less `T × (|v₁| + |v₂|)` is above the
   reach; no hit when the spheres do not approach; else the roots of the quadratic of the relative motion decide: a
   hit when either lies in `[0, T]`.
3. A hit counts when the field lies ahead (the angle between the ship's forward axis and the way to the field is
   under 90°) and the goal lies on the field's side (the angle between the way to the goal and the way to the field is
   under 90°); the goal is the move process's target (+0xb4, **inferred**).

A hit sets the process byte +0x104 and returns the direction unchanged, with no height clamp in the scorer (0x42ebb0's
own clamp follows) and no new last result. The consumer 0x42e3f0 then zeroes the roll, pitch, and lift controls, calls
the ship's slot 0xe0 (the brake spring 0x4657b0, which `flight-model.md` describes) toward the ship's own position at
velocity 0, sets +0x688 to 2, and clears the byte: the ship brakes in place. The engine does the same (`Brake`) on the
tick after the hit, in place of the move process's own maneuver. **inferred**: the slot's order against the move
process in a tick, since 0x42e3f0 has no caller or table entry found.

A hit does not turn the ship away: the ship is held in front of the field, and a later tick's steering predicts the hit
again while the field stays in the way. The prediction fires only once the field's center lies in the neighbour query's
box, so a large field is met late: the box reaches `r_S + 4 × speed + (the largest radius)` from the ship's center.

**stand-in**: the field's enable word (+0x500, meaning unknown) counts as set. A field's collision radius is the
definition's `collisionRadius` where above 0 (a value that is no number, or not finite, is reported once per class and
counts as 0; the finite limit is the **stand-in** of item 2 of The scorer),
else its field radius (the game's fallback is the model radius, which the field's object has not been traced to); a
field's model radius for the query's widening is its field radius.

## What the engine does not model

- The path-end event 0x46eac0, the debug line (0x632e75), and the other three callers of the scorer.
- The class values of planets and fields: they take the defaults (`avoidMe` 1, `avoidanceClass` 1000, speed 0), and a
  planet is scored as a still object.
- The initial last result, the cell rounding of the query, and the skip of the unit a ship heads for (above).
