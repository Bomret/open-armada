# 4. Combat follows the rules recovered from the game

**Status:** Accepted

Combat applies the game's own rules for hits, shields, crew loss, system damage, repair, and destruction,
recovered from `Armada.exe` and written down in our own words before they are built. The object
definitions give the numbers but not the rules that combine them: which part of a hit reaches the
shields, when crew die, what destroys a ship. A model guessed from the numbers alone would turn every
fight out differently from the game. Unlike motion (ADR 0003), combat outcomes are what players compare,
so the reverse-engineering cost is paid first.

## Considered options

- A simple model fed by the definitions, with stand-ins for every unstated rule, as ADR 0003 chose for
  motion: faster to build, but fights would resolve differently from the game and each stand-in would
  need replacing later.
- Recovering the rules as the first part of the build rather than before planning: no wait, but the
  later parts would be planned against rules nobody knew yet.
