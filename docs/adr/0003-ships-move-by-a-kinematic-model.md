# 3. Ships move by a kinematic model fed by the physics definition

**Status:** Superseded
**Superseded by:** ADR 0011
**Amended by:** ADR 0008

A ship in the map sandbox turns toward its target at its physics definition's `yawRotationRate`, banks up
to its `maxRoll`, travels at its `impulseSpeed`, and stops at the target. The engine does not reproduce
the game's flight controller, the inertia and the `shp_`/`sy_`/`sp_` coefficients that the physics
definitions also hold. The simple model makes ships move in the first milestone that has a live world, and
it can be replaced later without changing the scene, because the scene only asks a ship for its pose.

## Considered options

- The game's flight controller, reverse-engineered from `Armada.exe` first: closer motion, but a
  reverse-engineering effort of unknown size before anything moves.
- Pathfinding and obstacle avoidance first: these only matter once ships move, so they come later on top
  of this model.
