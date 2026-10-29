# Flight model

The rules of a ship's powered flight in the 1.2 `Armada.exe`
(SHA-256 `c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5`): the physics step's alive branch,
the steering that feeds it, and the coefficients the physics definition sets. Each rule names its address as
evidence (ADR 0004, INV-0001). A rule the code does not confirm is marked **inferred** or **stand-in**.

**The engine does not use this model.** It moves a ship by the kinematic model (ADR 0009). This note
records what the game does, for the work that replaces that motion. `docs/rules/combat-rules.md` ("Coasting")
holds the no-thrust branch of the same physics step, which the engine does reproduce (ADR 0008).

Offsets are of the unit object unless a class is named. Coefficient values are those of the `destphys` physics
definition with `impulseSpeed` 20.

## The physics step, alive branch

The branch (0x459440) runs for a ship that is not dying, whose engines (+0x518) work, that is not derelict
(+0x4dc), and whose class does not set `borgPhysics`. A ship in a queue (+0x67c) and a station skip the step.

- **Control gain.** vc (+0x774) = 1 − 1 / (|v − (up · v) up| × c6a8 + c6ac), with v the velocity and up the ship's
  up axis.
- **Pitch acceleration.** −kd × ω_p − c6c0 × vc × control[+4] − c6b8 × vc × pitch, with ω_p the pitch rate. kd
  is c6b4 when c6b4 × dt is below 1, else 1 / dt.
- **Yaw rate.** −c6dc × roll. The ship turns by banking.
- **Linear acceleration.** The sum of:
  - the forward thrust, c6fc or c700 times the throttle, with drag c6f0;
  - up × (control[+8] × c710 − (up · v) × c708);
  - −right × (right · v) × c718.
- **Integration**, per sub-step: v += a × dt / 2, then position += v × dt, then v += a × dt / 2. The orientation
  is rebuilt from the pitch, yaw, and roll angles (0x55d080). Pitch clamps to ±1.5 rad. Roll wraps at ±π.

The accelerations are built from scratch each step and added into the velocity found, which is why a velocity
written by a mover carries into the next step.

## The steering routine

Steering (0x42f740) is called from the move maneuver (0x42e4a0, through 0x42f9b0) and from the dogfight
maneuver (0x42c9d0).

- The direction is dir = (P − C) / max(|P − C|, 20), with P the target point and C the ship's center, plus the
  avoidance term (0x42ebb0).
- dir.y is zeroed above `SHIP_COMBAT_CEILING` (67) when the ship rises, and below `SHIP_COMBAT_FLOOR` (10) when
  it sinks.
- The throttle is clamp(|dir|) × a scale.
- The yaw servo gets atan2(dir.x, dir.z) and the pitch servo gets θd = asin(dir.y / |dir|).
- The lift control is set to 0.

## The servos

- **Pitch servo** (0x458ca0). γ = asin(v_y / |v|) − θd, and 0 when |v| is below 1e-4. control[+4] =
  clamp(c750 × γ + (c6b8 / c6c0) × θd + c748 × ω_p + c74c × (−pitch − θd)).
- **Yaw servo** (0x458b80, `Servo_Yaw`). The steering routine hands it the heading atan2(dir.x, dir.z). Its
  formula is not recovered.
- **Height servos.** The hover and height servos (slots 0xe4, 0xe8, 0xec, 0xf8) have no callers in the shipped
  game. A ship pushed vertically keeps its height: the idle hold (0x428081, then 0x42e4a0) zeroes the lift
  control.

## The coefficient map

The physics definition's keys reach the ship's instance through the class (0x455440 reads the keys, 0x458750
copies them to the instance). The instance field, the key it derives from, and its `destphys` value:

| Field | From | Value |
|---|---|---|
| +0x6a8 | `velocWhenCoeffHalf` term | 0.02381 |
| +0x6ac | 1 / (1 − `velocCoeffStill`) | 1.6667 |
| +0x6b4 | `pitchRotationSpeed` × `impulseSpeed` | 1.334 |
| +0x6b8 | | 0.83417 |
| +0x6c0 | `maxPitch` | 1.01913 |
| +0x6dc | `maxFwd` / (`turnRadius` × `maxRoll` in radians) | 2.2918 |
| +0x6f0 | 1 / (`frontBackInertia` / `impulseSpeed`) | 4 |
| +0x6fc | the forward thrust term c6fc | 80 |
| +0x700 | the forward thrust term c700 | 53.34 |
| +0x708 | 1 / (`upDownInertia` / `impulseSpeed`) | 5.333 |
| +0x710 | `maxVerticalSpeed` term | 35.55 |
| +0x718 | 1 / (`leftRightInertia` / `impulseSpeed`) | 5.333 |
| +0x724 to +0x730 | `shp_*` | |
| +0x734 to +0x744 | `sy_*` | |
| +0x748 to +0x750 | `sp_*` | |
| +0x754 to +0x764 | `spl_*` | |

`pitchControlStiffness` and `liftControlStiffness` are never read. The keys behind the +0x6b8, +0x6fc, and
+0x700 values, and the `shp_*`, `sy_*`, `sp_*`, and `spl_*` groups, are not named in the notes this table rests
on.

## Not known

- The height of the path points on moves (0x42ded0, the path planner 0x40a310).
- The height of the normal plane.
- The vertical part of avoidance (0x42ef70).
- The thrust drag terms (+0x4bc, +0x4c0).
