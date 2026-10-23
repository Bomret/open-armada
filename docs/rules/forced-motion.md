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
  downward part, is **inferred**.

### Push-out

A ship whose engines work and that is not derelict is moved out of a ring around the hole (0x4644b0, from the
collision pass): when the plane distance between the positions is below 1.5 × `EventRadius` plus the ship's
radius (+0x60), the ship's position moves straight outward by the overlap. Height and velocity stay. The stock
ring is 105. The same routine moves ships out of asteroid fields and planets, with their radius in place of the
ring.

### Event horizon

Each area tick, the hole gathers every ship that is not dying and whose center lies within `EventRadius` in the
plane (0x44da40, gather 0x44db30) and swallows it (0x450a40, slot 0xc8):

- It sets the black-hole death flag (+0x624) and the hole's center (+0x628), and makes the ship a dying unit
  (0x468990).
- The dying timer is 0 (0x466090), so the ship ends on the next tick. It plays no `fireball`; its model shatters
  into debris pieces that carry the hole's center (0x4663d0 → 0x58cef0). How a piece uses the center is unknown.

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

Each post-simulate tick, a ship that is not a starbase, not phased, and not dying separates from every other
ship that overlaps it (0x464910):

- The overlap is the penetration depth along the line between the centers (0x464000, surface points from
  0x463f60; the hull shape that 0x463f60 tests is not recovered). Ships farther apart than 1.5 × the sum of
  their radii do not touch.
- Each ship moves by its share of the separation: its speed over the sum of both speeds, or half each when the
  sum is below 0.01. A ship in a queue takes no share and the other takes all of it. A starbase does not move,
  and its share is lost.
- The vertical part of the separation gets 0.42 more (0x5ccddc), and 0.42 more for each ship whose class sets
  `borgPhysics`, so the lower ship moves down.
- The game writes the positions (slot 0xc) and turns each moved ship a little away from the contact (0x464620,
  scaled by `MAX_PREVENTION_ROTATION`; the angle's formula is **inferred**). It writes no velocity.

The avoidance force of `Collision.cfg` (0x436580, 0x435600) is steering of the unit AI. The stock file sets
`COLLISION_REAL_COLLISIONS 0`.

## What does not move a ship

- **Hits.** A weapon hit, a torpedo impact, and a critical hit change no velocity, rate, or position (0x465100;
  the shot carries no direction).
- **Explosions.** The `fireball`, system explosions, the self-destruct explosion (0x508fc0, 0x4663d0), and the
  shockwave explosion (0x51d2a0, damage only) push nothing.
- **Debris.** The pieces of a ship that ends do not take its velocity: 0x4663d0 passes the velocity (× 1.0, or ×
  2.5 for the `selfDestruct` explosion) to 0x58cef0, and the piece spawner 0x57cf90 never reads that argument.
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

- The order of black holes and ships in the simulate pass.
- The hull shape of 0x463f60 and the exact turn of 0x464620.
- How a debris piece uses the black hole's center.
- Whether the repulsion field's hit reaches a dying ship.
- What the tractor beam's `targetSpeed` key (default 2) does; nothing in the beam reads it.
- Wormhole and docking transport (queue routines 0x557e20, 0x558240, 0x557400) is only skimmed: a ship in a
  queue has its velocity zeroed (0x462dd0) and is moved along a curve that writes its transform.
