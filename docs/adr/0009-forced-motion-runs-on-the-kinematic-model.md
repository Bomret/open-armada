# 9. Forced motion runs on the kinematic model until the flight model replaces it

**Status:** Superseded
**Superseded by:** ADR 0011

Black holes, push-outs, and collision separation move ships by the game's rules (0x450a80, 0x4644b0,
0x464910) inside the kinematic model of ADR 0003. Each rule keeps what the game writes: a velocity for the
pull, a position for the push-out and the separation, including the separation's fixed vertical push. The
game holds no flying height, so a pushed ship keeps its new height in the game too. The kinematic model has
only a heading, so the collision turn applies only its heading part, and a coasting ship keeps its bank
instead of rolling on. Both are stand-ins until the game's flight model, recovered in
`docs/rules/flight-model.md`, replaces the kinematic model in a later feature.

## Considered options

- The game's flight model in the same feature: pitch, roll, the collision turn, and coasting spin would
  follow the game, but the scope grows a lot and starts with the steered height of path points still
  unrecovered.
- Separation in the plane only: no ship leaves the plane, but the game does push ships apart vertically,
  and it holds no height that would bring them back.
