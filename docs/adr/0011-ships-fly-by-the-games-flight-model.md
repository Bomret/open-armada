# 11. Ships fly by the game's flight model

**Status:** Accepted

Supersedes ADR 0003, ADR 0008, and ADR 0009.

A ship in the map sandbox flies by the flight model recovered from `Armada.exe`
(`docs/rules/flight-model.md`), on the combat world's fixed tick. The physics step (0x459440) moves it by
thrust, drag, and servos: it pitches toward its steering point, banks to turn, and keeps its three rotation
rates while it coasts. A Borg-physics ship takes the game's Borg branch. Its move orders follow the game's
path planner (0x40a310), its attacks follow the dogfight (0x42c9d0), and a nebula slows it by the game's
cell rule. The collision turn rotates it in 3D. Its pose is the game's pitch, yaw, and roll. Every
coefficient comes from the physics definition, and from `fastphys.odf` after the ship's first move, as in
the game. The kinematic model of ADR 0003, its coast of ADR 0008, and the heading-only collision turn and
held bank of ADR 0009 are retired.

## Considered options

- Keep the kinematic model and add pitch and roll to it: less work, but every stand-in of ADR 0003 and
  ADR 0009 stays, and ships still move unlike the game.
- The flight model with a straight two-point path and the sandbox's close-then-face attack: a smaller
  scope, but ships fly through asteroid fields and nebulae the game routes around, and attack unlike the
  game. Rejected for fidelity.
- Keep the kinematic model as a fallback for a ship whose physics values are degenerate: no ship of a
  readable definition would take it, so it would be dead code; a degenerate value is reported and replaced
  by a stand-in instead.
