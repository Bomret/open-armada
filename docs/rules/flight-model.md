# Flight model

The rules of a ship's flight in the 1.2 `Armada.exe`
(SHA-256 `c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5`): the physics step that integrates a
ship's motion, the steering and the move maneuver that write its controls, the move order's carrot and arrival test,
and the coefficients the physics definition sets. Each rule names its address as evidence (ADR 0004, INV-0001). A
rule the code does not confirm is marked **inferred** or **stand-in**.

Ships fly by these rules (ADR 0011), through `FlightModel`. A ship with Borg physics takes its own branch of the
physics step, steering, and move maneuver (see "The Borg branch"). `docs/rules/combat-rules.md` ("Coasting")
holds the no-thrust branch and the dogfight (0x42c9d0) with the attack that enters it ("Attack order"),
`docs/rules/nebulae.md` the nebula slowdown that lowers the terrain coefficient and cap below,
`docs/rules/obstacle-avoidance.md` the avoidance scorer, and `docs/rules/path-planning.md` path planning. The collision
turn belongs to its own part of the flight model.

Offsets are of the unit object unless a class is named. Coefficient values quoted are those of the `destphys`
physics definition with `impulseSpeed` 20.

## The tick

The combat world runs on a fixed tick, and each tick of a ship runs its process first and the physics step after it.
The process (the move order, the hold, the dogfight) writes the controls the physics step reads. **Inferred**: the frame
function 0x43f2b0 calls the AI update 0x433b30 (slot 0x1c of the object at 0x633218) before the world tick
0x43f1a0, whose simulate pass (0x471410, slot 0x84) runs the physics step, and the process's per-tick method (slot
0x24, 0x4267a0) runs the order dispatch (0x427ff0). The tick length is not recovered (**stand-in**: 0.02 s).

Between the world's ticks the view draws each ship ahead of its simulated pose by the velocity and the three rates
over the part of a tick not yet run.

## The angle convention

A ship's orientation is three angles in radians, stored at +0x768 (pitch), +0x76c (yaw), and +0x770 (roll), and a
matrix built from them. The builder (0x55d080) is called by the physics step (0x459440) after each position
integration, with the three angles and the position; the call fixes the argument order.

- **Axes.** The matrix rows are the ship's side (right), up, and forward axes in world coordinates, then the
  position. Local +x is the side, +y up, +z forward.
- **Composition.** A local vector reaches the world by roll about the local forward axis first, then pitch about
  the side axis, then yaw about the world's up axis: world = Ry(yaw) · Rx(pitch) · Rz(roll) · local, with the
  standard right-handed matrices. With sin and cos written s and c, the forward axis is
  (c_pitch s_yaw, −s_pitch, c_pitch c_yaw) and the side axis is
  (c_roll c_yaw + s_roll s_pitch s_yaw, s_roll c_pitch, −c_roll s_yaw + s_roll s_pitch c_yaw).
- **Signs.** Yaw 0 faces +z, and a positive yaw turns the forward axis toward +x. A positive pitch lowers the nose
  (forward.y = −sin pitch). A positive roll raises the right side (side.y = sin roll · cos pitch).
- **The sandbox's frame.** The engine's world is the game's with Z mirrored (a point (x, y, z) stands at
  (x, y, −z); a model is built the same way). A rotation seen through that mirror keeps its roll and reverses its
  yaw and pitch, so a model offset in the sandbox's frame turns by Ry(−yaw) · Rx(−pitch) · Rz(roll). The model's
  bow, the game's +z, stands at −Z at yaw 0. The sandbox's own ground-plane bearing (0 faces +Z, positive toward
  +X) is π − yaw. `MoveGeometry` states this once. The flight model holds its state
  in the game's frame and gives every vector to the engine in the sandbox's.

## The decomposition

The placed orientation of a unit, and any matrix written to a ship by its matrix setter (slot 0x14, 0x4718e0),
becomes angles through the decomposition (slot 0x48, 0x458ea0), which the setter calls. It reads the forward
axis f, the side axis r, and the up axis u:

- **Usual case.** pitch = −asin(f.y), yaw = atan2(f.x, f.z), roll = atan2(r.y, u.y). It is the inverse of the
  composition above away from the poles.
- **Forward axis within 0.999 of straight up or down** (f.y above 0.999, or below −0.999, strict). Pitch is −π/2
  for up and +π/2 for down, roll is 0, and yaw = atan2(−r.z, r.x).
- It also sets the heading target (+0x4b0) to the yaw, and leaves the turn rates alone. So a matrix written
  to a ship, such as the one the collision turn (0x464620) turns, persists as angles, and the next physics step
  rebuilds the matrix from them. The engine's setter (`FlightModel.SetAngles`) writes the angles and the heading
  target the same way and leaves the rates. The collision turn rotates the whole orientation in 3D (pitch and roll
  included) and writes it through the setter (`docs/rules/forced-motion.md`, "Collision separation").

**stand-in:** a mirrored placed basis (negative determinant) has no meaning for the game's matrix, which holds
a rotation only. The engine takes the mirror on the side axis alone: the rotation keeps the basis's forward and up
axes as placed and reverses its side axis, and the mirror and the scale stay on the model. The decomposition reads
the forward axis the model shows, so a mirrored unit stands as the map placed it and moves bow first. The physics
step rebuilds a pure rotation from the angles, so a mirrored unit keeps its mirror while it flies: the view pairs the
placed scale (`HardpointGeometry.Split`) with the pose's rotation.

## The physics step

The step (0x459440) integrates one ship over the tick's time dt. It skips a ship in a queue (+0x67c) and a starbase.

- **Sub-steps.** h = max(0.05, min(0.1 / |ω|, 4 / speed)), with ω the three rates and the speed at the start of the
  step; a division by 0 is infinity. A dt above h runs n = ceil(dt / h) sub-steps of dt / n (0x459494). The
  reciprocal of the sub-step is 1000 when the sub-step is below 0.001.
- **Branches.** A ship is **alive** when it is not dying (+0x1bf), its engines work (+0x518), it is not derelict
  (+0x4dc), and its class does not set `borgPhysics`. Otherwise it takes the **no-thrust branch**, except a
  `borgPhysics` ship that is not dying, with working engines, and not derelict, which takes the Borg branch
  (0x459a99). **Bank-to-turn** is on for a ship with working engines, not derelict, and not Borg, whatever its dying
  flag (0x459579): a dying ship with working engines still turns from its bank.

Per sub-step the step computes the accelerations once from the state at its start, then integrates (below). The
alive branch, with f, u, r the forward, up, and side axes, v the velocity, ω the rates, (p, y, ρ) the angles, and
ctl the control block (roll, pitch, lift, throttle; the order is the log format at 0x5ff9ec):

- **Control gain.** vc = 1 − 1 / (|v − (u · v) u| × c6a8 + c6ac).
- **Pitch acceleration.** −kp × ω_p − ctl.pitch × c6c0 × vc − c6b8 × vc × p, with kp = c6b4 when the sub-step
  times c6b4 is below 1, else the reciprocal of the sub-step.
- **Roll acceleration.** −kr × ω_r + c6d4 × ctl.roll × vc − c6cc × ρ, with kr = c6c8 under the same test.
- **Yaw acceleration.** 0. With bank-to-turn the yaw rate is set instead: −c6dc × ρ, after the first half kick.
- **Forward force.** With s = f · v: the base is c6fc when the throttle is above 0, else c700 when s is above 0, else
  0; the force is the base times the throttle (the hyper effect's speed modifier, +0xe8 of a `TimedHyper`, is not
  read: **stand-in**, 1). The terrain coefficient (+0x4bc) and cap (+0x4c0), 1 and 10¹⁰ unless something lowered them,
  set the drag k: when force × coefficient is above cap × c6f0, k = force / cap (the cap raised to 0.01 first when it
  is below), else k = c6f0 / coefficient. The force becomes force − k × s. When s + force × dt would be below 0, the
  force is −s over the sub-step: the forward speed never crosses 0 within a sub-step, so a velocity astern along the
  bow is taken to 0 in one tick.
- **Linear acceleration.** force × f + (ctl.lift × c710 − (u · v) × c708) u − c718 × (r · v) r.

The no-thrust branch (see `docs/rules/combat-rules.md`, "Coasting") has no angular acceleration, so the three
rates keep their values; its linear acceleration is −2.0 along v when |v|² is at least 0.01, else 0.

- **Integration**, per sub-step, with half = dt / 2: the rates gain acceleration × half (roll and pitch only); under
  bank-to-turn the yaw rate becomes −c6dc × ρ (the roll of the sub-step's start); the angles gain dt × rate; the
  rates gain acceleration × half again; v gains a × half; pitch is clamped to ±1.5; roll wraps into [−π, π]; the
  position gains dt × v and the matrix is rebuilt from the angles (0x55d080); v gains a × half again; the speed
  (+0x138) is |v|.
- **After the sub-steps.** The yaw wraps into [−π, π]. When any component of v is above 120 in size (0x5cc130) the
  velocity and the speed are 0. The terrain coefficient and cap return to 1 and 10¹⁰ (0x465cc0), so whatever caps the
  speed (`Craft_CapSpeed`, 0x4690c0: coefficient = min, cap = min when the cap argument is 0 or more) sets them
  before every step.

A ship that has no thrust and regains it keeps its velocity and rates: nothing resets them. The speed that every rule
reads (the dying timer, the to-hit stop modifiers, the arrival test, the separation share) is the length of the
velocity.

**stand-in:** the engine takes the ship's world center (+0x12c) as its position. A step that leaves any state
non-finite is reported, once per ship and cause, and restores the ship at rest. The engine wraps an angle in constant
time, so a non-finite angle, or one a degenerate definition value drives to an absurd size, comes out as NaN or as a
wrapped angle and never stalls the tick; the NaN is such a non-finite state. A fault in a ship's process (the controls
for the tick) is reported once per ship and exception type apart from a flight fault (a message that changes each
tick is still one cause), the step still runs with the controls as they stand, and the other ships fly on. A failure
to record the pose or to test the move order's arrival after the step is reported the same way and spares the other
ships' ticks.

## The Borg branch

A ship whose class sets `borgPhysics` (class +0x1ec: the stock `bbatphys.odf` for the Cube, Sphere, and Locutus's
ship, and `bcnsphys.odf` for the Diamond) flies by its own laws. The branch of the physics step (0x459a99), the Borg
path of the steering (0x42f740), and the Borg path of the move maneuver (0x42e4a0) replace the servos, the throttle, and
the bank. The ship's own keys are the class's: `tooCloseToTurn`, `tooSlowToTurn`, `rotationAcceleration` a,
`maxRotationSpeed` w, and `rotationStiffness` K. The linear law reads the instance coefficients (c6f0, c6fc, k, 2√k),
which are `fastphys.odf`'s after the ship's first move; the yaw law reads the class's own values whatever file the ship
has swapped to (0x458750 does not copy them).

- **When.** The branch applies to a ship that is not dying, has working engines, and is not derelict. It computes only
  on the sub-step 0 of a step (the test at 0x459a99 reads the sub-step counter); later sub-steps reuse the stored
  linear and yaw accelerations. It writes only the linear acceleration and the yaw acceleration: the pitch and roll
  accelerations and rates are not written, bank-to-turn is off, and no terrain or nebula cap and no zero-crossing guard
  applies.
- **Linear law.** The acceleration is c6fc × (the command, +0x158) − c6f0 × v. The command is a vector the steering or the
  spring writes; the process's per-tick method clears it before the order runs (0x4267a0 writes a zero vector, 0x4258e0),
  so a tick in which no maneuver writes one commands 0 and the ship brakes by its drag.
- **Yaw law.** With e = yaw − heading target (+0x4b0), folded once each way into [−π, π], M = a × w, and D = 2√K: the
  acceleration is q × M − a × ω, where q = clamp((a × ω − K × e − D × ω) / M, −1, 1) and ω is the yaw rate. Unsaturated
  it is −K × e − D × ω, a critically damped spring on the yaw. Saturated, ω relaxes to ±w at the rate a. Stock values
  (a 2.0, w 0.75, K 1.0) saturate at an acceleration of 1.5.
- **No bank.** The yaw rate is never overridden by the roll. The pitch and roll stay at the angles the ship has (level
  for a ship placed level) and the rates stay 0: a Borg ship moves vertically without pitching.
- **Heading target.** Every sub-step ends with the matrix setter's decomposition, which sets the heading target to the
  yaw (0x4718e0, 0x458ea0). A Borg ship therefore turns only toward a target written after the last step: the move
  maneuver below writes one each tick, the dogfight and the arrival spring write none, so its yaw only damps. A step
  that faults on a non-finite state restores the yaw and sets the heading target to it, so a non-finite target does
  not fault the later steps.
- **Without thrust** (dying, engines destroyed, or derelict) the ship takes the no-thrust branch as any ship does: its
  velocity brakes at 2.0, and because the yaw rate is not overridden by a bank it keeps spinning at its last yaw rate.

**The steering** (0x42f740, direction d, scale s) for a Borg ship writes no servo. With L = |d| (0.001 below that),
m = min(L, 1), and the speed cap v₀ (+0x4a8, the `impulseSpeed`): the throttle control is m × s (unused by the branch),
the wanted velocity is vd = d × (m × s × v₀ / L), and the command is u = (−2√k × (v − vd) + c6f0 × v) / c6fc, scaled to
length s when |u|² exceeds s². Without the scale limit the acceleration is −2√k × (v − vd): a critically damped approach
to a full 3D velocity, d.y included, with no relation to the ship's facing.

**The move maneuver** (0x42e4a0) skips the turning-circle test and the throttle spring. With P the steered point (the
carrot), G the goal, and the 3D distance from G to the ship:

- Farther than `tooCloseToTurn`: d is the steering direction toward P (0x42ebb0), the heading target is atan2(d.x, d.z)
  plus the order's **facing offset** (+0x114 of the process, 0x42f9b0), minus 2π when above π (the upper wrap only), and
  the steering above runs with scale 1.
- Within `tooCloseToTurn` (arrival and the idle hold, where P is the ship's own position): the **3D spring**
  a = −k × (position − P) − 2√k × v becomes the command u = (a + c6f0 × v) / c6fc, limited to length 1 (see the
  stand-in below), so the unsaturated acceleration is a: a critically damped spring to P, whatever the ship faces. When
  the speed is under `tooSlowToTurn`, the heading target becomes the yaw.

**The facing offset** is a multiple of 90°, k × π / 2 with k drawn from 0 to 3, which the order handler (0x426ad0) draws
once when it accepts an order: the move orders 3, 0x26, and 0x27 and the follow orders 9, 0x1a, 0x19, 0x1b, and 0x1d,
whatever the ship's class. The attack orders (5, 6, 7) draw none, so the value of the previous order stays through an
attack. Only the Borg heading target reads it. The game draws it from the one random state that its combat rolls share
(0x67dc98, a linear congruential generator, `s × 2273 + 31` modulo 429397397, k = s mod 4), so each draw shifts the
rolls after it.

- **Settled:** the engine draws it in `CombatWorld.Move` (a player's move order, and so each ship's cell of a group
  move) from the combat world's seeded random source, for every ship that takes the order, Borg or not, as the game's
  handler tests no class (ADR 0010's precedent: the draw's place and count are the game's, the generator is not). The
  attack order draws none. A unit without a flight, or one that cannot move, takes no order and draws none.
- **stand-in:** the other draw sites (0x42a450, 0x42b430, and 0x430270 draw k from a second state, 0x67dca4, per new
  destination of the freighter and similar states) are not built. The game's own generator is not reproduced.

**The dogfight.** The attack maneuver's steering (0x42c9d0, 0x42c400) calls the Borg steering with no heading target, so
a Borg ship that attacks does not turn to face its target: it closes by the velocity command and its yaw only damps.
The engine's attack routines do the same for a Borg ship: `Dogfight` feeds the aim (d times (|d| - range), or 0 within the
range) to the Borg steering at scale 1 and writes no servo or heading target, `Pivot` commands scale 0 (no thrust, no
turn), and `Approach` closes by the Borg maneuver without the heading target.

**stand-in:** the warp multiplier (+0x4ac) of the arrival spring's braking limit is not modelled, so the command's limit
is 1 whichever way it points (the steering's warp scale is 1 as well). The first-tick quirk of 0x42e4a0 (the process
byte +0x104, which sends a stop order through slot 0xe0 and leaves the command at 0) is not built: the engine sets no
such byte.
The idle return to the tether point of a Borg ship runs through the engine's `Approach`, which sets no heading target,
where the game's 0x42e4a0 would turn the ship toward its bearing plus the facing offset.

## The coefficient map

The physics definition's keys reach the ship's instance through the class: the loader (0x455440) reads the keys and
derives the class values, and the copy (0x458750) puts them on the instance. A key the definition lacks takes the
class default (the hover class's constructor 0x455230; `rollRotationSpeed` takes the loader's own −1, for which the
engine takes a stand-in, below). With an
`impulseSpeed` above 0 the loader scales some keys by it and takes it as the forward speed; otherwise the keys stand
as read and `maxForwardSpeed` (read only then) is the forward speed. Scaled keys: `velocWhenCoeffHalf`,
`pitchRotationSpeed`, `rollRotationSpeed`, `maxReverseSpeed`, and `maxVerticalSpeed` are multiplied; `frontBackInertia`,
`upDownInertia`, and `leftRightInertia` are divided. Written i for the impulse speed, m for the forward speed, a for
`maxRoll` in radians, and R for `turnRadius`:

| Instance | Value | Default key values (destphys result) |
|---|---|---|
| +0x6ac | 1 / (1 − `velocCoeffStill`) | `velocCoeffStill` 0.1; 1.6667 |
| +0x6a8 | (2 − c6ac) / `velocWhenCoeffHalf` | 7.5 (× i); 0.02381 |
| vref | 1 − 1 / (c6a8 × m + c6ac) | the gain at the forward speed |
| +0x6b4 | `pitchRotationSpeed` | 3.5 (× i); 1.334 |
| +0x6b8 | 0.25 × c6b4² / vref | 0.83417 |
| +0x6c0 | c6b8 × `maxPitch` in radians | 70°; 1.01913 |
| +0x6c8 | `rollRotationSpeed` | −1 (× i); the engine takes 5.5 |
| +0x6cc | 0.25 × c6c8² | |
| +0x6d4 | c6cc × a / vref | `maxRoll` 30° |
| +0x6dc | m / (R × a) | R 25; 2.2918 |
| +0x6f0 | 1 / `frontBackInertia` | 2 (÷ i); 4 |
| +0x6fc, +0x700 | c6f0 × m, c6f0 × `maxReverseSpeed` | 15 (× i); 80, 53.34 |
| +0x708, +0x710 | 1 / `upDownInertia`, c708 × `maxVerticalSpeed` | 0.5 (÷ i), 10 (× i); 5.333, 35.55 |
| +0x718 | 1 / `leftRightInertia` | 0.5 (÷ i); 5.333 |
| +0x4b4, +0x4b8 | `braccelControlStiffness`, twice its root | 10 (the class default) |
| +0x734 | `sy_rollVelocCoeff` / `maxRoll` in degrees / i | 1.221 |
| +0x738 | `sy_rollAngleCoeff` / `maxRoll` in degrees | 0.174 |
| +0x73c, +0x740 | `sy_yawVelocCoeff`, `sy_yawAngleCoeff` | −0.043, 0.726 |
| +0x748 | `sp_pitchVelocCoeff` / i | 2 |
| +0x74c, +0x750 | `sp_pitchAngleCoeff`, `sp_velocAngleCoeff` | −1.774, −2.566 |

The remaining defaults are `impulseSpeed` −1, `maxForwardSpeed` 20, `turnRadius` 25, and the inertias above. The
`shp_*` and `spl_*` groups (+0x724 to +0x72c, +0x754 to +0x764) belong to the hover servos, which have no callers in
the shipped game; the engine does not read them. `pitchControlStiffness` and `liftControlStiffness` are never read.
`sy_velocAngleCoeff` is read and copied (+0x744) and no servo uses it.

The engine reports a missing key once per key, naming the ships, and takes the game's default. **stand-in:** a value
that makes a coefficient degenerate, namely a non-finite value, `velocCoeffStill` of 1 or more, or a divisor key
(`velocWhenCoeffHalf`, `maxPitch`, `maxRoll`, `turnRadius`, and the three inertias) of 0 or less, is reported and
takes the key's default, so no coefficient is a NaN; the game divides by it. Keys whose values still give a
non-finite coefficient are reported and take every default.

**stand-in:** `rollRotationSpeed` of 0 or less, or missing (the loader's default is −1, 0x455440), is reported and
takes 5.5, the hover class's value (0x455230). The game's −1 gives the roll negative damping: the roll rate grows
without bound and the step's state goes non-finite, which the engine cannot show. The same holds for keys whose coefficients leave the control gain's denominator, `c6a8 × speed + c6ac`, at 0 or less
at a speed up to 120 × √3 ≈ 208, the longest velocity the end-of-step reset leaves (it clears a velocity with a
component over 120, and the gain reads the velocity's length off the up axis) (a `velocCoeffStill` over 0.5 gives `c6a8` a negative slope): the gain
divides by it, so the engine reports the definition and every key takes its default. A `velocCoeffStill` between 0.5
and 1 whose denominator stays above 0 flies as the game does.

**The swap to `fastphys.odf`.** The process constructor (0x425a20) loads one physics class, shared by all ships, from
`fastphys.odf` (class 0x632ecc). The move maneuver (0x42e4a0) and the dogfight (0x42c9d0) copy it onto the ship
(0x458750) the first time they run (the ship's byte +0x78c), and nothing in play restores the ship's own, so a ship
keeps `fastphys.odf`'s coefficients from its first move or attack on. The copy takes the brake spring, the
coefficients above, and the servo coefficients. It does not take the ship's speed (+0x4a8), its warp multiplier, or
the class distances `tooCloseToTurn`, `tooCloseForWarp`, `tooSlowToTurn`, and `pathLeadDistance` (the process reads
them from the ship's own class, +0xe0). **stand-in:** the idle hold (0x428081) also reaches the move maneuver, which
swaps for every caller, the hold included (the swap is gated by a bit test of +0xbc & 0x10 and by +0x78c, whose
meaning is not known). The engine's hold keeps the ship's own coefficients and swaps only at a move order, an
approach, a dogfight, or a pivot: the Definition of Done has the dynamic coefficients be `fastphys.odf`'s from the ship's first
move or attack on, and the idle order's state number is not traced. The engine reads `fastphys.odf` once; a missing or unreadable file is
reported once and every ship keeps its own coefficients.

## The steering routine

Steering (0x42f740, direction `d`, scale `s`) is called from the move maneuver (through 0x42f9b0) and from the
dogfight maneuver (0x42c9d0).

- The throttle is clamp(|d|, 1) × s, with |d| taken as 0.001 when it is below that.
- The pitch servo gets θd = asin(d.y / |d|) and the yaw servo gets atan2(d.x, d.z). The lift control is 0.
- The steering direction (0x42ebb0) is d = (P − C) / max(|P − C|, 20), zero when |P − C| is below 0.001, with P the
  steered point and C the ship's center; then the avoidance (0x42ef70, off in the stock game; the engine's seam holds
  the scorer of `obstacle-avoidance.md` when `OBSTACLE_AVOIDANCE` is on) may rewrite it. The avoidance gets the goal G that the move maneuver passed in (0x42ebb0's fourth
  argument), not P, which only the offset above uses, and the engine's seam gets the goal too; then d.y is zeroed
  above `SHIP_COMBAT_CEILING` when the ship's height is above it and d.y is positive, and below `SHIP_COMBAT_FLOOR`
  when the height is below it and d.y is negative. The comparisons are strict. d is not normalized.
- 0x42f9b0 also sets the heading target (+0x4b0) to atan2(d.x, d.z) plus the process's facing offset (+0x114), wrapped,
  for the Borg yaw law only ("The Borg branch"); the other ships' servos take the bearing alone. The warp multiplier it applies to the scale when the heading is
  within `headingCloseEnoughForWarp` is not modelled (**stand-in**: scale 1).

One floor and one ceiling, the **combat band** read from `RTS_CFG.h` (stock 10 and 67), feed this clamp, the plane
that clicks and path points lie on, and the camera's look-at height. The plane's height is (ceiling + floor) / 2
(38.5 on the stock band; 0x4ac746, 0x40a792).

## The servos

- **Pitch servo** (0x458ca0, wanted pitch θd). γ = asin(v.y / |v|) − θd, and 0 when |v| is below 1e-4.
  ctl.pitch = clamp(c750 × γ + (c6b8 / c6c0) × θd + c748 × ω_p + c74c × (−pitch − θd)).
- **Yaw servo** (0x458b80, `Servo_Yaw`, wanted heading θd). e = yaw − θd, wrapped once into [−π, π].
  ctl.roll = clamp(c740 × e + c734 × ω_r + c738 × ρ + c73c × ω_y). The yaw servo drives the roll, and bank-to-turn
  turns the roll into yaw.
- **Height servos.** The hover and height servos (slots 0xe4, 0xe8, 0xec, 0xf8) have no callers in the shipped
  game. A ship pushed vertically keeps its height until its steering lifts it: the hold zeroes the lift control.

## The move maneuver

The maneuver (0x42e4a0) steers a ship to a point P with velocity vP, the carrot of a path (or the point of a hold, with
vP 0, or the ordered unit's centre and velocity for an attack's straight move, `docs/rules/combat-rules.md`);
G is the goal, the last point of the order. Before everything it swaps in `fastphys.odf` (see above). Then:

1. **Near the goal.** When the 3D distance from G is at most `tooCloseToTurn` (class +0x208), the roll, pitch, and
   lift controls are 0 and the throttle comes from the spring below. The ship no longer steers.
2. **Inside a turning circle.** The ship's right axis in the plane is (f.z, −f.x) normalized. The point's side is
   the right when its plane offset from the ship has a positive dot with it, else the left. The circle of that side
   has its center a turning radius to that side of the ship. When P lies strictly inside the circle the controls are
   0 and the throttle is the spring's: the ship does not turn to a point it cannot reach, and it brakes to the
   point's projection on its heading. **Settled:** the radius is the one the active physics class holds (slot
   0xb0, 0x458080 reads class +0x488 through the ship's +0x77c), so it is `fastphys.odf`'s after the swap.
3. **Steering.** Otherwise the ship steers by the steering direction toward P with scale 1 (0x42f9b0, 0x42f740).
   When the plane offset to P lies within 45° of the heading (dot above 0.707 times the lengths), the spring's
   throttle then replaces the steering's.

The **spring** (0x4657b0, then 0x458990) is a critically damped brake along the forward axis: a = k × (f · (P − C)) +
2√k × (f · (vP − v)), with k = +0x4b4 and 2√k = +0x4b8; the force is a + c6f0 × (f · v), and the throttle is that over
c700 when it is 0 or less and over c6fc otherwise, clamped to −1 and 1. (The warp factor of the clamp, 0x469900, is
1.) The ship's reverse thrust is c700.

## The move order

A move order is a path (`MovePath`) and a carrot. The order's list is [the ship's position at order time, the goal],
and the planner (0x40a310) turns it into the path the ship follows: the two points [the ship, the goal] when nothing
bends the way, else the points of the planned route (`docs/rules/path-planning.md`, which also states the order's
index and replans). The goal's height is the plane's middle (the order's tether point lies there too,
`docs/rules/combat-rules.md`, "Alert stance"); the first point keeps the ship's own height.

- **The carrot** (0x42ded0, 0x434a50). It stands on the path at an arc length that starts at 0 and gains 10 a pass.
  A new path's first pass always advances the carrot once. After that each tick the carrot advances only while its
  squared distance from the ship is below `pathLeadDistance` squared (class +0x214, stored squared; the key's default
  is the root of 30, a lead of about 5.5) and it is not on the path's last segment: the loop tests the distance before
  it advances, so a carrot that is already far enough stands still. Its velocity is the unit tangent of its segment times the ship's speed (+0x4a8), and 0 on the
  last segment. A segment of 0 length gives no tangent (**stand-in**: velocity 0; the game divides by 0). The
  maneuver then runs with the carrot as P and its velocity as vP, and the order's last point as the goal (the
  path's last point, except for the replan of a ship that made no progress, which leads to the order's first point).
- **Arrival** (0x428170, before the carrot each tick). The order ends when the 3D distance from the order's last
  point is under `tooCloseToTurn` and the speed is under 0.5 × the ship's `impulseSpeed` (0x5ca230). The ship has
  then written no controls that tick. The same test runs on a ship that forced motion has just moved, so the order
  ends in the tick of the push when the push leaves the ship near and slow; a ship pushed near but fast keeps its
  order.
- **A goal that cannot be reached by these rules**, such as one inside a turning circle and outside
  `tooCloseToTurn`, leaves the order open: the ship rests at the point's projection on its heading and does not
  circle. Coinciding points and a goal at the ship give a finite pose.
- **The stuck replan** (a ship that has not moved more than 1 unit in 12 whole seconds while beyond the lead distance
  drops its path and plans again) is in `docs/rules/path-planning.md`, "The order list and the replans".
- A move order that ends leaves a one-point order at the ship's position (0x46eac0): the hold. It is a pending command 2 that the next tick's
  0x4267a0 hands to 0x426ad0 (`case 2`), so it replaces any attack (`docs/rules/path-planning.md`, "The order list and
  the replans").

## The hold

An idle ship, and a ship whose order has just ended, run the hold (the process state 0x24, 0x428081): the move
maneuver with the ship's own position as the point, the goal, and the carrot, and a velocity of 0. The ship is
inside `tooCloseToTurn` of that point, so the roll, pitch, and lift controls are 0 and the spring brakes the forward
speed to 0: the ship brakes to rest, and the restoring terms of the physics step level its pitch and roll. A
velocity across the bow is damped by the side drag. The idle handler of the alert stance (0x42a170) drives a ship
that is farther than `tooCloseToTurn` from its tether point back to it through the same maneuver
(`docs/rules/combat-rules.md`, "Alert stance"; `docs/rules/forced-motion.md`, "Move-order arrival and hold").

A ship that attacks does not hold: it closes on its target by the move maneuver (a path beyond 400, straight at the
target's origin nearer) and fights the dogfight inside the band (`docs/rules/combat-rules.md`, "Attack order"). The
dogfight is the steering with scale 1 along its aim and a second yaw servo call toward the target's bearing
(`FlightModel.Dogfight`); a ship with a target-location weapon turns in place by the steering with scale 0
(`FlightModel.Pivot`).

## Stand-ins in the engine

- The warp state and multiplier (+0x688, +0x4ac) are not modelled: ships fly at their impulse speed and the steering
  scale is 1. The game's ships at warp reach `warpSpeed`.
- The ship's center is its position.
- The Borg branch's stand-ins (the warp multiplier of the arrival spring, the first-tick stop byte, the idle return's
  missing heading target, the facing offset's other draw sites and generator) are listed in "The Borg branch".
- The hyper effect's speed modifier is 1.
- The tick length is 0.02 s.
- A ship is stopped when its speed is below 0.1 (the root of the no-thrust branch's rest threshold 0.01); the game's
  test for the to-hit stop modifiers is not traced.
- The idle hold's swap to `fastphys.odf` (the game swaps in the hold too, gated by +0xbc & 0x10 and +0x78c, meaning
  unknown) is not taken: a ship holds on its own coefficients until its first move order, approach, dogfight, or pivot, from the
  Definition of Done's "from the ship's first move or attack on".

## Not known

- The tick length, and the scheduling of the AI update against the physics step beyond the inference above.
- The terrain cap callers other than the nebula effect and the formation follower (0x4288f0).
