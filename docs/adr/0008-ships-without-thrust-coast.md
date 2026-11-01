# 8. Ships without thrust coast inside the kinematic model

**Status:** Superseded
**Superseded by:** ADR 0011
**Amends:** ADR 0003

Amends ADR 0003. A ship that has no thrust coasts instead of stopping. This covers a dying unit, a
derelict, and a ship whose engines are destroyed. The game sends all three through one no-thrust branch
of its physics step (0x459440). That branch keeps the ship's velocity and turn rate and slows it at
2.0 units/s² until its speed falls below 0.1. The kinematic model of ADR 0003 starts the coast from the
ship's current speed and turn rate. It reproduces only this branch of the game's flight controller.

## Considered options

- The game's whole flight controller: the inertia that coasting needs comes with it, but ADR 0003 already
  rejected it as reverse-engineering work of unknown size.
- A dead stop for every ship without thrust: this is what the sandbox did before. The game does not do it,
  and a dying ship could not then drift along its heading before it explodes.
