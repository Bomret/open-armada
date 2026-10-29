# 10. Debris draws from the combat world's random source

**Status:** Accepted

When a unit ends, the combat world makes the random draws of its debris pieces from its own seeded random
source, in the game's order per piece: life, direction with its rejection loop, speed, tumble (0x57d270).
The game draws them from the one random state that its game logic shares, at the unit's end (0x4663d0).
The combat world therefore needs each model's face count, and a unit's model changes the draws that later
combat rolls see, as in the game.

## Considered options

- A separate seeded source in the art layer: the combat world needs no model data, and combat outcomes stay
  independent of models, but the game keeps one shared state.
