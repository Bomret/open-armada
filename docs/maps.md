# Built-in maps

This page describes every map in the `bzn` folder of a Star Trek: Armada 1.2 install: 28 multiplayer
maps and 21 campaign and other maps. Use it to pick a map for a sandbox test, for example a map where two
teams start with warships for a combat test. The facts come from the map files and object definitions of
an unmodified 1.2 install, read with the engine's own readers. An install with override files can give
other classes.

## How to read an entry

- **Map** is the file stem in `bzn/`, the name the sandbox takes with `--map`. **Title** is the name the
  map file gives itself.
- **Group.** A multiplayer map has a `.mdf` file beside its `.bzn`. Every other map is in the campaign and
  other maps group.
- **Start positions** is the count the `.mdf` gives. A map without a `.mdf` has none.
- **Objects per team** lists every object of each team present, 0 to 8, with its count and its classes.
  `×N` gives the number of objects of one class. Team 0 holds the neutral objects, such as nebulae,
  moons, and asteroid fields.
- **Teams with a warship** names the teams 1 to 8 that hold at least one
  [warship](../CONTEXT.md) (a ship whose definition's include chain reaches a combat hull).
- **Combat at start** is yes when two or more teams from 1 to 8 each hold a warship. Team 0 never counts.
- **Neutral and monster units** lists the warships of team 0 and every object of team 8 on a map with
  fewer than 8 start positions. On a map with 8 start positions, team 8 is a player team.
- **Unresolved classes** appears only for a map that names a class whose definition or include chain
  cannot be read. No map of the 1.2 install has one.

## Multiplayer maps

| Map | Title | Start positions | Combat at start |
|---|---|---|---|
| [`2blue`](#2blue) | Blue Passages | 2 | yes |
| [`2borders`](#2borders) | Hostile Borders | 2 | yes |
| [`2deadzone`](#2deadzone) | The Dead Zone | 2 | no |
| [`2divide`](#2divide) | Divided Lands | 2 | yes |
| [`2duel`](#2duel) | On The Ready Line | 2 | no |
| [`2hidden`](#2hidden) | Hidden Treasure | 2 | no |
| [`2maze`](#2maze) | Nebula Maze | 2 | yes |
| [`3race`](#3race) | Race for the Base | 3 | yes |
| [`3triad`](#3triad) | The Triad | 3 | no |
| [`4across`](#4across) | Across the Red River | 4 | no |
| [`4brown`](#4brown) | Red & Green & Blue make Brown | 4 | no |
| [`4crux`](#4crux) | The Cross | 4 | no |
| [`4first`](#4first) | The Very First Map | 4 | no |
| [`4green`](#4green) | Green Glow | 4 | no |
| [`4hole`](#4hole) | Hole in the Middle | 4 | yes |
| [`4hub`](#4hub) | The Central Hub | 4 | no |
| [`4junky`](#4junky) | The Junkyard | 4 | no |
| [`4mama`](#4mama) | Big Mama | 4 | no |
| [`4more`](#4more) | Simply Boxes | 4 | no |
| [`4nirvana`](#4nirvana) | All Roads Lead to Nirvana | 4 | no |
| [`4redsea`](#4redsea) | The Red Sea | 4 | no |
| [`4strange`](#4strange) | Weird and Strange | 4 | no |
| [`5assault`](#5assault) | Assault (3 vs 2) | 5 | no |
| [`6brown`](#6brown) | Even Browner | 6 | no |
| [`6violins`](#6violins) | The Trouble With Games | 6 | no |
| [`8compete`](#8compete) | Fierce Competition | 8 | no |
| [`8nirvana`](#8nirvana) | Tic Tac Toe | 8 | no |
| [`8open`](#8open) | The Great Wide Open | 8 | no |

### 2blue

- Title: Blue Passages
- Group: multiplayer
- Start positions: 2
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (277): `dlight`, `mafield` ×6, `mdmoon` ×4, `mmooninf` ×2, `mnebula1` ×65, `mnebula2` ×83, `mnebula3` ×53, `mnebula4` ×21, `mnebula5` ×38, `mwrmhole` ×4
  - t1 (4): `kbase`, `kbattle`, `kconst`, `kdestroy`
  - t2 (4): `rbase`, `rbattle`, `rconst`, `rdestroy`

### 2borders

- Title: Hostile Borders
- Group: multiplayer
- Start positions: 2
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: t0: `zferscav` ×2
- Objects per team:
  - t0 (168): `dlight`, `mafield` ×118, `mdmoon` ×4, `mmooninf`, `mnebula3` ×20, `mnebula4` ×8, `mnebula5` ×14, `zferscav` ×2
  - t1 (9): `rbase`, `rconst`, `rcruise1`, `rdestroy`, `rfreight`, `rmining`, `rscout`, `rturret` ×2
  - t2 (9): `kbase`, `kconst`, `kcruise1`, `kdestroy`, `kmining`, `kspecial`, `kturret` ×2, `rfreight`

### 2deadzone

- Title: The Dead Zone
- Group: multiplayer
- Start positions: 2
- Teams with a warship: t8
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4; t8: `zentity` ×10
- Objects per team:
  - t0 (254): `dlight`, `mafield` ×134, `mblkhole`, `mdmoon` ×6, `mmooninf` ×2, `mnebula1` ×9, `mnebula2` ×25, `mnebula3` ×34, `mnebula4` ×14, `mnebula5` ×21, `mwreck`, `mwrmhole` ×2, `zferscav` ×4
  - t1 (1): `fconst`
  - t2 (1): `fconst`
  - t8 (10): `zentity` ×10

### 2divide

- Title: Divided Lands
- Group: multiplayer
- Start positions: 2
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (333): `dlight`, `mafield` ×228, `mdmoon` ×4, `mmooninf` ×2, `mnebula1` ×15, `mnebula2` ×19, `mnebula3` ×30, `mnebula4` ×28, `mwrmhole` ×6
  - t1 (7): `bbase`, `bdestroy` ×6
  - t2 (7): `kbase`, `kdestroy` ×6

### 2duel

- Title: On The Ready Line
- Group: multiplayer
- Start positions: 2
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: none
- Objects per team:
  - t0 (211): `dlight`, `mdmoon` ×6, `MLankal`, `mmooninf`, `mnebula1` ×98, `mnebula2` ×75, `mnebula3` ×7, `mnebula4` ×22
  - t1 (2): `kbase`, `kconst`
  - t2 (2): `kbase`, `kconst`

### 2hidden

- Title: Hidden Treasure
- Group: multiplayer
- Start positions: 2
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4; t8: `zjemyard` ×2
- Objects per team:
  - t0 (651): `dlight` ×3, `mafield` ×201, `mblkhole` ×2, `mdmoon` ×6, `Mearthbg`, `MEridon`, `mmooninf` ×2, `mnebula1` ×155, `mnebula2` ×132, `mnebula3` ×74, `mnebula4` ×20, `mnebula5` ×48, `mwrmhole` ×2, `zferscav` ×4
  - t1 (2): `bbase`, `bconst`
  - t2 (2): `rbase`, `rconst`
  - t8 (2): `zjemyard` ×2

### 2maze

- Title: Nebula Maze
- Group: multiplayer
- Start positions: 2
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (641): `dlight`, `mafield` ×105, `mdmoon` ×6, `mmooninf`, `mnebula1` ×115, `mnebula2` ×105, `mnebula3` ×161, `mnebula4` ×66, `mnebula5` ×81
  - t1 (6): `fbase`, `fbattle`, `fcruise1` ×2, `ffreight`, `fmining`
  - t2 (6): `fbase`, `fbattle`, `fcruise1` ×2, `ffreight`, `fmining`

### 3race

- Title: Race for the Base
- Group: multiplayer
- Start positions: 3
- Teams with a warship: t1, t2, t3
- Combat at start: yes
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (293): `dlight`, `mafield` ×211, `mdmoon` ×6, `mmooninf`, `mnebula4` ×11, `mnebula5` ×53, `mwrmhole` ×6, `zferscav` ×4
  - t1 (4): `kbase` ×3, `kbattle`
  - t2 (4): `kbase` ×3, `kbattle`
  - t3 (4): `kbase` ×3, `kbattle`

### 3triad

- Title: The Triad
- Group: multiplayer
- Start positions: 3
- Teams with a warship: t1
- Combat at start: no
- Neutral and monster units: none
- Objects per team:
  - t0 (480): `dlight`, `mafield` ×120, `mdmoon` ×6, `mmooninf`, `mnebula1` ×124, `mnebula2` ×215, `mnebula4` ×7, `mwrmhole` ×6
  - t1 (4): `fbase`, `fconst`, `zferscav` ×2
  - t2 (2): `kbase`, `kconst`
  - t3 (2): `rbase`, `rconst`

### 4across

- Title: Across the Red River
- Group: multiplayer
- Start positions: 4
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (487): `dlight`, `mafield` ×130, `mdmoon` ×8, `Mearth`, `MEridon`, `MKrios`, `MLankal`, `mmooninf` ×3, `mnebula2` ×103, `mnebula3` ×222, `mwrmhole` ×12, `zferscav` ×4
  - t1 (2): `kbase`, `kconst`
  - t2 (2): `fbase`, `fconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `bbase`, `bconst`

### 4brown

- Title: Red & Green & Blue make Brown
- Group: multiplayer
- Start positions: 4
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (319): `dlight`, `mafield` ×72, `mdmoon` ×12, `mnebula1` ×70, `mnebula2` ×57, `mnebula3` ×3, `mnebula4` ×22, `mnebula5` ×70, `mwrmhole` ×8, `zferscav` ×4
  - t1 (2): `fbase`, `fconst`
  - t2 (2): `kbase`, `kconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `bbase`, `bconst`

### 4crux

- Title: The Cross
- Group: multiplayer
- Start positions: 4
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (425): `dlight`, `mafield` ×208, `mdmoon` ×8, `mmooninf` ×2, `mnebula1` ×53, `mnebula3` ×112, `mnebula4` ×36, `MRomulus`, `zferscav` ×4
  - t1 (2): `fbase`, `fconst`
  - t2 (2): `kbase`, `kconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `bbase`, `bconst`

### 4first

- Title: The Very First Map
- Group: multiplayer
- Start positions: 4
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (289): `dlight` ×2, `mafield` ×110, `Mbaku`, `MBarisa`, `mblkhole` ×4, `mdmoon` ×12, `MKrios`, `Mmoon`, `mmooninf`, `mnebula2` ×56, `mnebula3` ×72, `mnebula4` ×16, `MQonos`, `Msun1`, `Msun2`, `Msun3`, `mwrmhole` ×4, `zferscav` ×4

### 4green

- Title: Green Glow
- Group: multiplayer
- Start positions: 4
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (243): `dlight` ×2, `mafield` ×145, `mdmoon` ×4, `mmooninf` ×4, `mnebula3` ×36, `mnebula4` ×36, `mwrmhole` ×12, `zferscav` ×4
  - t1 (2): `fbase`, `fconst`
  - t2 (2): `kbase`, `kconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `bbase`, `bconst`

### 4hole

- Title: Hole in the Middle
- Group: multiplayer
- Start positions: 4
- Teams with a warship: t1, t2, t3, t4
- Combat at start: yes
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (287): `dlight`, `mafield` ×100, `mblkhole` ×4, `mdmoon` ×12, `mmooninf`, `mnebula1` ×48, `mnebula2` ×68, `mnebula3` ×29, `mnebula5` ×16, `mwrmhole` ×4, `zferscav` ×4
  - t1 (8): `fbase`, `fconst`, `fcruise1`, `fdestroy`, `ffreight`, `fmining`, `fscout` ×2
  - t2 (9): `kbase`, `kconst`, `kcruise1`, `kdestroy`, `kfreight`, `kmining`, `kscout` ×2, `mconstph`
  - t3 (8): `rbase`, `rconst`, `rcruise1`, `rdestroy`, `rfreight`, `rmining`, `rscout` ×2
  - t4 (8): `bbase`, `bconst`, `bcruise2`, `bdestroy`, `bfreight`, `bmining`, `bscout` ×2

### 4hub

- Title: The Central Hub
- Group: multiplayer
- Start positions: 4
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (439): `dlight`, `mafield` ×235, `mdmoon` ×12, `MKrios`, `mnebula1` ×35, `mnebula2` ×36, `mnebula3` ×54, `mnebula5` ×45, `mwrmhole` ×16, `zferscav` ×4
  - t1 (2): `fbase`, `fconst`
  - t2 (2): `kbase`, `kconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `bbase`, `bconst`

### 4junky

- Title: The Junkyard
- Group: multiplayer
- Start positions: 4
- Teams with a warship: t8
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4; t8: `blocutus`, `fbattle` ×2, `fcruise1` ×2, `fcruise2`, `fdestroy`, `fentd`, `fgalaxy`, `fpremnew`, `kbattle` ×2, `kcruise1` ×4, `kcruise2`, `kdestroy` ×4, `kmartok`, `rbattle` ×2, `rcruise1` ×2, `rcruise2`, `rsela`
- Objects per team:
  - t0 (203): `dlight`, `mafield` ×76, `mblkhole`, `mdmoon` ×8, `mnebula4` ×16, `mnebula5` ×30, `mwreck` ×67, `zferscav` ×4
  - t1 (2): `fbase`, `fconst`
  - t2 (2): `kbase`, `kconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `bbase`, `bconst`
  - t8 (28): `blocutus`, `fbattle` ×2, `fcruise1` ×2, `fcruise2`, `fdestroy`, `fentd`, `fgalaxy`, `fpremnew`, `kbattle` ×2, `kcruise1` ×4, `kcruise2`, `kdestroy` ×4, `kmartok`, `rbattle` ×2, `rcruise1` ×2, `rcruise2`, `rsela`

### 4mama

- Title: Big Mama
- Group: multiplayer
- Start positions: 4
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4; t8: `zmltmama` ×2
- Objects per team:
  - t0 (754): `dlight` ×2, `mafield` ×320, `mblkhole` ×4, `mborgpl`, `mdmoon` ×8, `Mearth`, `MEridon`, `mmooninf` ×4, `mnebula2` ×209, `mnebula3` ×134, `mnebula4` ×16, `mnebula5` ×40, `MQonos`, `MRomulus`, `mwrmhole` ×8, `zferscav` ×4
  - t1 (5): `bbase`, `bconst`, `bsuperbl`, `bturret` ×2
  - t2 (5): `bbase`, `bconst`, `bsuperbl`, `bturret` ×2
  - t3 (5): `bbase`, `bconst`, `bsuperbl`, `bturret` ×2
  - t4 (5): `bbase`, `bconst`, `bsuperbl`, `bturret` ×2
  - t8 (2): `zmltmama` ×2

### 4more

- Title: Simply Boxes
- Group: multiplayer
- Start positions: 4
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (408): `dlight`, `mafield` ×374, `mdmoon` ×8, `mmooninf`, `mnebula4` ×12, `mwrmhole` ×8, `zferscav` ×4
  - t1 (2): `fbase`, `fconst`
  - t2 (2): `kbase`, `kconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `bbase`, `bconst`

### 4nirvana

- Title: All Roads Lead to Nirvana
- Group: multiplayer
- Start positions: 4
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (266): `dlight`, `mafield` ×156, `Mbaku`, `mdmoon` ×8, `mmooninf` ×4, `mnebula2` ×18, `mnebula3` ×26, `mnebula4` ×24, `mnebula5` ×16, `mwrmhole` ×8, `zferscav` ×4

### 4redsea

- Title: The Red Sea
- Group: multiplayer
- Start positions: 4
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×7
- Objects per team:
  - t0 (260): `dlight` ×2, `mblkhole`, `mdmoon` ×8, `MKrios`, `mnebula2` ×110, `mnebula3` ×130, `Msun3`, `zferscav` ×7
  - t1 (2): `fbase`, `fconst`
  - t2 (2): `kbase`, `kconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `bbase`, `bconst`

### 4strange

- Title: Weird and Strange
- Group: multiplayer
- Start positions: 4
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (301): `dlight`, `mafield` ×90, `mblkhole` ×3, `mdmoon` ×9, `MLankal`, `mmooninf`, `mnebula3` ×115, `mnebula4` ×27, `mnebula5` ×48, `mwrmhole` ×2, `zferscav` ×4
  - t1 (2): `fbase`, `fconst`
  - t2 (2): `bbase`, `bconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `kbase`, `kconst`

### 5assault

- Title: Assault (3 vs 2)
- Group: multiplayer
- Start positions: 5
- Teams with a warship: t7
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (393): `dlight`, `mafield` ×147, `mblkhole`, `mdmoon` ×6, `MEridon`, `mmooninf` ×3, `mnebula1` ×46, `mnebula2` ×46, `mnebula3` ×59, `mnebula4` ×41, `mnebula5` ×35, `Msun1`, `mwrmhole` ×2, `zferscav` ×4
  - t1 (2): `bbase`, `bconst`
  - t2 (2): `rbase`, `rconst`
  - t3 (2): `kbase`, `kconst`
  - t4 (2): `fbase`, `fconst`
  - t5 (2): `kbase`, `kconst`
  - t7 (1): `zbreen`

### 6brown

- Title: Even Browner
- Group: multiplayer
- Start positions: 6
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (395): `dlight`, `mafield` ×102, `mdmoon` ×12, `mmooninf` ×2, `mnebula1` ×70, `mnebula2` ×66, `mnebula3` ×3, `mnebula5` ×127, `mwrmhole` ×8, `zferscav` ×4
  - t1 (2): `fbase`, `fconst`
  - t2 (2): `kbase`, `kconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `bbase`, `bconst`
  - t5 (2): `fbase`, `fconst`
  - t6 (2): `rbase`, `rconst`

### 6violins

- Title: The Trouble With Games
- Group: multiplayer
- Start positions: 6
- Teams with a warship: t8
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×8; t8: `zentity` ×10
- Objects per team:
  - t0 (312): `dlight`, `mafield` ×93, `mblkhole`, `mdmoon` ×12, `MEridon`, `mmooninf` ×3, `mnebula1` ×48, `mnebula2` ×72, `mnebula3` ×30, `mnebula4` ×20, `mnebula5` ×17, `Msun1`, `Msun2`, `mwrmhole` ×4, `zferscav` ×8
  - t1 (2): `kbase`, `kconst`
  - t2 (4): `bbase`, `bconst`, `rbase`, `rconst`
  - t4 (2): `fbase`, `fconst`
  - t5 (4): `fbase`, `fconst`, `kbase`, `kconst`
  - t8 (10): `zentity` ×10

### 8compete

- Title: Fierce Competition
- Group: multiplayer
- Start positions: 8
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (232): `dlight`, `mafield` ×160, `mdmoon` ×8, `mmooninf` ×10, `mnebula3` ×49, `zferscav` ×4
  - t1 (2): `fbase`, `fconst`
  - t2 (2): `kbase`, `kconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `bbase`, `bconst`
  - t5 (2): `fbase`, `fconst`
  - t6 (2): `kbase`, `kconst`
  - t7 (2): `rbase`, `rconst`
  - t8 (2): `bbase`, `bconst`

### 8nirvana

- Title: Tic Tac Toe
- Group: multiplayer
- Start positions: 8
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×8
- Objects per team:
  - t0 (351): `dlight`, `mafield` ×283, `Mbaku`, `mdmoon` ×16, `mmooninf` ×4, `mnebula3` ×21, `Msun1`, `mwrmhole` ×16, `zferscav` ×8
  - t1 (1): `fconst`
  - t2 (1): `fconst`
  - t3 (1): `kconst`
  - t4 (1): `kconst`
  - t5 (1): `rconst`
  - t6 (1): `rconst`
  - t7 (1): `bconst`
  - t8 (1): `bconst`

### 8open

- Title: The Great Wide Open
- Group: multiplayer
- Start positions: 8
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: t0: `zferscav` ×4
- Objects per team:
  - t0 (152): `dlight`, `mblkhole`, `mdmoon` ×16, `MLankal`, `mmooninf`, `mnebula1` ×17, `mnebula2` ×24, `mnebula3` ×45, `mnebula4` ×10, `mnebula5` ×32, `zferscav` ×4
  - t1 (2): `fbase`, `fconst`
  - t2 (2): `kbase`, `kconst`
  - t3 (2): `rbase`, `rconst`
  - t4 (2): `bbase`, `bconst`
  - t5 (2): `fbase`, `fconst`
  - t6 (2): `kbase`, `kconst`
  - t7 (2): `rbase`, `rconst`
  - t8 (2): `bbase`, `bconst`

## Campaign and other maps

| Map | Title | Combat at start |
|---|---|---|
| [`borg1`](#borg1) | Resurrection | yes |
| [`borg3`](#borg3) | Assimilation | yes |
| [`borg4`](#borg4) | Extermination | yes |
| [`borg5`](#borg5) | The Twilight Hour | yes |
| [`fed1`](#fed1) | Premonitions | yes |
| [`fed2`](#fed2) | Paradise Revisited | yes |
| [`fed3`](#fed3) | Dark Omens | yes |
| [`fed5`](#fed5) | Vendetta | yes |
| [`finale1`](#finale1) | A Line In The Sand | yes |
| [`finale4`](#finale4) | Once and Again | yes |
| [`finale5`](#finale5) | Alpha and Omega part 1 | yes |
| [`finale6`](#finale6) | The Alpha and The Omega Part 2 | yes |
| [`fulltech`](#fulltech) | Roscoe's Free Technology Emporium | no |
| [`kling1`](#kling1) | The Enemy Within | no |
| [`kling3`](#kling3) | To The Gates Of Sto'Vo'Kor | yes |
| [`kling4`](#kling4) | A Good Day To Die | yes |
| [`kling5`](#kling5) | Gray Eminance | yes |
| [`rom2`](#rom2) | Cloak and Dagger | yes |
| [`rom3`](#rom3) | The Gauntlet | yes |
| [`rom4`](#rom4) | A Call To Power | yes |
| [`rom5`](#rom5) | Unholy Alliances | yes |

### borg1

- Title: Resurrection
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (258): `Bcolect1` ×3, `Bcolect2` ×4, `Bcolect3` ×4, `dlight` ×3, `mafield` ×54, `mblkhole`, `mborgpl`, `mdmoon` ×3, `mdmoon3`, `mnebula1` ×48, `mnebula2` ×47, `mnebula3` ×63, `mnebula4` ×8, `mnebula5` ×15, `momega`, `Msun3` ×2
  - t1 (6): `bconst` ×2, `bcruise1`, `bcruise2` ×3
  - t2 (32): `kturret` ×16, `kturret2` ×4, `zclonfac`, `zjembat` ×3, `zjemdest` ×3, `zjemyard` ×5
  - t3 (9): `bbase` ×3, `bmining`, `bresear` ×2, `bsuperbl`, `byard`, `byard2`

### borg3

- Title: Assimilation
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2, t3, t4, t5, t6, t8
- Combat at start: yes
- Neutral and monster units: t8: `fbase`, `fbattle` ×2, `fcruise1` ×2, `fdestroy` ×2, `fresear`, `fresear2`, `fscout` ×6, `fspecial` ×3
- Objects per team:
  - t0 (443): `dlight` ×6, `mafield` ×270, `mblkhole`, `mdmoon` ×2, `mdmoon3`, `MEridon`, `MLankal`, `mnebula1` ×29, `mnebula2` ×78, `mnebula4` ×10, `mnebula5` ×41, `mwreck` ×3
  - t1 (6): `bconst`, `bcruise2` ×2, `bdestroy` ×2, `blocutus`
  - t2 (9): `kbattle`, `kcruise2`, `kdestroy` ×3, `kfreight`, `kmining`, `kresear`, `kresear2`
  - t3 (4): `zbreen` ×4
  - t4 (26): `rbase`, `rbattle` ×2, `rcruise1` ×2, `rfreight`, `rmining`, `rresear`, `rresear2`, `rturret` ×13, `rturret2`, `ryard` ×2, `ryard2`
  - t5 (1): `zferengi`
  - t6 (5): `zbreen` ×3, `zjembat`, `zjemyard`
  - t8 (18): `fbase`, `fbattle` ×2, `fcruise1` ×2, `fdestroy` ×2, `fresear`, `fresear2`, `fscout` ×6, `fspecial` ×3

### borg4

- Title: Extermination
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (74): `dlight` ×2, `mafield` ×12, `MBarisa`, `mblkhole`, `mdmoon` ×6, `mdmoon3` ×2, `mnebula1` ×11, `mnebula2` ×15, `mnebula4` ×10, `mnebula5` ×12, `Msun1`, `Msun3`
  - t1 (7): `bbattle`, `bconst` ×2, `bcruise2`, `blocutus`, `bomega`, `bspecial`
  - t2 (24): `zentity` ×20, `zmama` ×4

### borg5

- Title: The Twilight Hour
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2, t3, t4
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (91): `dlight` ×3, `mafield` ×47, `mdmoon` ×5, `mearth`, `Mmoon`, `mnebula1` ×32, `MQonos`, `Msun1`
  - t1 (7): `bconst` ×2, `bcruise2`, `bdestroy`, `blocutus`, `wborgate` ×2
  - t2 (82): `favenger`, `fbase`, `fbasehq`, `fbattle` ×4, `fcruise1` ×4, `fcruise2`, `fdestroy` ×3, `fente`, `ffreight` ×3, `fgalaxy` ×2, `fmining` ×3, `fpremnew`, `fresear`, `fresear2`, `fscout` ×2, `fsensor` ×8, `fspecial` ×2, `fsuperbl`, `fturret` ×27, `fturret2` ×12, `fyard` ×2, `fyard2`
  - t3 (1): `bcruise1`
  - t4 (1): `bspecial`
  - t5 (1): `fyard`
  - t6 (1): `fyard`
  - t7 (1): `fyard`

### fed1

- Title: Premonitions
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (323): `dlight` ×2, `mafield` ×155, `MBarisa`, `mdmoon`, `mdmoon2`, `mnebula2` ×84, `mnebula3` ×33, `mnebula5` ×45, `Msun2`
  - t1 (4): `fconst`, `fcruise1` ×2, `fente`
  - t2 (6): `zjemdest` ×6

### fed2

- Title: Paradise Revisited
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (486): `dlight` ×2, `Mbaku`, `mblkhole`, `mdmoon`, `mnebula2` ×442, `mnebula4` ×39
  - t1 (10): `favenger` ×2, `fbase`, `fcruise1` ×2, `fscout` ×2, `fturret` ×3
  - t2 (3): `zsonbat`, `zsondest` ×2

### fed3

- Title: Dark Omens
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t4
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (215): `dlight` ×2, `mafield` ×63, `mdmoon`, `MEridon`, `mnebula1` ×18, `mnebula2` ×21, `mnebula3` ×55, `mnebula4` ×25, `mnebula5` ×11, `mwreck` ×18
  - t1 (23): `fbase`, `fconst` ×2, `fcruise1` ×4, `fcruise2`, `fdestroy` ×5, `fente`, `ffreight` ×2, `fmining`, `fscout` ×3, `fspecial` ×2, `fyard`
  - t4 (9): `bconst`, `bcruise1` ×2, `bcruise2` ×3, `bdestroy`, `bscout`, `bspecial`

### fed5

- Title: Vendetta
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2, t8
- Combat at start: yes
- Neutral and monster units: t8: `kcruise1`, `kdestroy`, `rdestroy`, `zferengi`, `zjemdest`, `zsondest`
- Objects per team:
  - t0 (594): `dlight` ×2, `mafield` ×322, `mblkhole` ×2, `mnebula1` ×9, `mnebula2` ×75, `mnebula3` ×17, `mnebula4` ×31, `mnebula5` ×127, `Msun1`, `Msun2`, `mwrmhole` ×7
  - t1 (1): `favenger`
  - t2 (9): `kdestroy` ×8, `ktoral`
  - t3 (3): `fbase`, `fyard`, `zneubase`
  - t8 (6): `kcruise1`, `kdestroy`, `rdestroy`, `zferengi`, `zjemdest`, `zsondest`

### finale1

- Title: A Line In The Sand
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (92): `dlight` ×3, `mafield` ×47, `mdmoon` ×5, `mearth`, `Mmoon`, `mnebula1` ×34, `Msun1`
  - t1 (85): `favenger`, `fbase`, `fbasehq`, `fbattle` ×2, `fcruise1` ×4, `fcruise2`, `fdestroy` ×3, `fente` ×2, `ffreight` ×3, `fgalaxy` ×2, `fmining` ×3, `fpremnew`, `fresear`, `fresear2`, `fscout` ×4, `fsensor` ×8, `fspecial` ×2, `fsuperbl`, `fturret` ×27, `fturret2` ×12, `fyard` ×2, `fyard2`, `mprmgate` ×2
  - t2 (77): `bbase` ×2, `bbattle` ×15, `bconst` ×2, `bcruise1` ×4, `bcruise2` ×5, `bdestroy` ×2, `bfreight` ×6, `blocutus`, `bmining` ×2, `bresear`, `bresear2`, `bscout` ×2, `bsensor` ×5, `bspecial` ×2, `bsuperbl` ×2, `bturret` ×14, `bturret2` ×3, `byard` ×4, `byard2` ×4

### finale4

- Title: Once and Again
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2, t3, t4, t5, t7
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (688): `dlight` ×8, `mafield` ×148, `Mearth`, `Mmoon` ×3, `mnebula1` ×45, `mnebula2` ×130, `mnebula3` ×183, `mnebula5` ×153, `mprmgate` ×4, `MRemus`, `MRomulus`, `Msun1`, `Msun2`, `Msun3`, `mwreck` ×6, `mwrmhole` ×2
  - t1 (10): `fcruise1` ×2, `fdestroy` ×4, `fente`, `fgalaxy`, `fspecial` ×2
  - t2 (21): `bbattle` ×4, `bcruise1`, `bcruise2` ×5, `bdestroy` ×6, `bspecial` ×4, `bsuperbl`
  - t3 (10): `bbattle`, `bcruise1`, `bdestroy`, `bspecial`, `rbase`, `rbattle` ×2, `rturret` ×3
  - t4 (13): `fcruise1` ×2, `fdestroy` ×4, `fente` ×3, `fgalaxy`, `fpremnew`, `fspecial` ×2
  - t5 (10): `bbattle`, `bcruise1`, `bdestroy`, `bspecial`, `rbattle`, `rcruise1`, `rdestroy` ×2, `rscout` ×2
  - t7 (4): `bbattle`, `bcruise1`, `bdestroy`, `bspecial`

### finale5

- Title: Alpha and Omega part 1
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (69): `dlight` ×2, `MBarisa`, `mblkhole`, `mdmoon` ×17, `mnebula1` ×11, `mnebula2` ×15, `mnebula4` ×10, `mnebula5` ×12
  - t1 (7): `favenger`, `fconst`, `fente`, `kconst`, `kmartok`, `rconst`, `rsela`
  - t2 (52): `bbase` ×2, `bbattle` ×2, `bconst`, `bcruise1`, `bcruise2` ×4, `bdestroy` ×10, `bfreight` ×2, `bmining` ×2, `bresear`, `bresear2`, `bscout` ×3, `bsensor` ×3, `bspecial`, `bsuperbl`, `bturret` ×12, `bturret2` ×3, `byard` ×2, `byard2`

### finale6

- Title: The Alpha and The Omega Part 2
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (277): `Bcolect1` ×8, `Bcolect2` ×5, `Bcolect3` ×7, `dlight` ×3, `mafield` ×64, `mborgpl` ×2, `mdmoon` ×5, `mmooninf` ×3, `mnebula1` ×52, `mnebula5` ×124, `Msun2`, `Msun3`, `mwreck` ×2
  - t1 (8): `fbattle` ×2, `fconst`, `fente`, `kconst`, `kmartok`, `rconst`, `rsela`
  - t2 (144): `bbase` ×6, `bbattle` ×23, `bcruise1` ×6, `bcruise2` ×10, `bdestroy` ×6, `bfreight` ×6, `blocutus`, `bmining` ×7, `bomega`, `bresear` ×3, `bresear2`, `bsensor` ×9, `bspecial` ×5, `bsuperbl`, `bturret` ×37, `bturret2` ×11, `byard` ×5, `byard2` ×4, `f6bconst` ×2
  - t3 (1): `f6bsuper`

### fulltech

- Title: Roscoe's Free Technology Emporium
- Group: campaign and other
- Start positions: none
- Teams with a warship: none
- Combat at start: no
- Neutral and monster units: none
- Objects per team:
  - t0 (1): `dlight`

### kling1

- Title: The Enemy Within
- Group: campaign and other
- Start positions: none
- Teams with a warship: t2
- Combat at start: no
- Neutral and monster units: none
- Objects per team:
  - t0 (332): `dlight` ×2, `mafield` ×85, `mdmoon` ×2, `mnebula1` ×73, `mnebula2` ×101, `mnebula3` ×36, `mnebula5` ×32, `Msun3`
  - t1 (19): `kconst` ×2, `kmarbase`, `ksensor` ×16
  - t2 (24): `kbattle` ×8, `kcruise1` ×8, `kcruise2` ×2, `kdestroy` ×5, `ktoral`

### kling3

- Title: To The Gates Of Sto'Vo'Kor
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (225): `dlight` ×3, `mafield` ×89, `mdmoon` ×8, `MKrios`, `mnebula1` ×11, `mnebula2` ×62, `mnebula3` ×12, `mnebula4` ×8, `mnebula5` ×31
  - t1 (27): `kbase`, `kconst`, `kcruise1` ×7, `kdestroy` ×4, `kfreight`, `kmartok` ×2, `kmining`, `ksensor` ×3, `kturret` ×7
  - t2 (54): `bbase`, `bcruise1` ×6, `bcruise2` ×8, `bdestroy` ×7, `bfreight` ×4, `bmining` ×6, `bresear`, `bscout`, `bspecial` ×3, `bturret` ×10, `bturret2` ×2, `byard` ×2, `byard2` ×3

### kling4

- Title: A Good Day To Die
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2, t3
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (377): `dlight` ×2, `mafield` ×79, `mdmoon` ×2, `mmooninf`, `mnebula2` ×276, `mnebula4` ×14, `MQonos`, `mwrmhole` ×2
  - t1 (10): `kconst`, `kdestroy` ×4, `kfreight`, `kmarbase`, `kmining`, `ksensor`, `kyard`
  - t2 (34): `kbase`, `kbattle`, `kconst` ×2, `kcruise1` ×2, `kcruise2` ×2, `kdestroy` ×13, `kmining`, `kresear`, `kscout`, `ksensor`, `kturret` ×7, `kyard`, `kyard2`
  - t3 (1): `favenger`

### kling5

- Title: Gray Eminance
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2, t5
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (257): `dlight` ×2, `mafield` ×72, `MBarisa`, `mblkhole` ×2, `mdmoon` ×8, `mnebula1` ×19, `mnebula2` ×55, `mnebula3` ×25, `mnebula4` ×35, `mnebula5` ×35, `MRemus`, `mwrmhole` ×2
  - t1 (6): `kconst` ×2, `kcruise1`, `kdestroy` ×2, `kmartok`
  - t2 (16): `ktoral`, `rbase` ×3, `rfreight` ×3, `rmining` ×3, `rresear`, `rresear2`, `ryard` ×3, `ryard2`
  - t4 (31): `rsensor` ×7, `rturret` ×14, `rturret2` ×10
  - t5 (1): `favenger`

### rom2

- Title: Cloak and Dagger
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (236): `dlight` ×2, `mafield` ×55, `mblkhole`, `mdmoon` ×4, `mdmoon2`, `mdmoon3` ×2, `MKrios`, `mnebula1` ×13, `mnebula2` ×77, `mnebula4` ×12, `mnebula5` ×68
  - t1 (8): `rconst` ×2, `rcruise1` ×2, `rcruise2` ×2, `rdestroy`, `rsela`
  - t2 (33): `kconst`, `kcruise2` ×2, `kdestroy` ×9, `kfreight`, `kmining`, `kprison`, `kresear`, `kresear2`, `kturret` ×12, `kturret2` ×2, `kyard`, `kyard2`

### rom3

- Title: The Gauntlet
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t5, t6, t8
- Combat at start: yes
- Neutral and monster units: t8: `rbattle`, `rcruise1` ×2
- Objects per team:
  - t0 (505): `dlight` ×11, `mafield` ×269, `mblkhole`, `mdmoon`, `mdmoon2`, `mdmoon3` ×2, `mnebula1` ×17, `mnebula2` ×46, `mnebula3` ×99, `mnebula4`, `mnebula5` ×57
  - t1 (10): `rbattle` ×2, `rcruise1` ×3, `rcruise2`, `romegaon`, `rsela`, `rspecial` ×2
  - t5 (4): `kbattle`, `kcruise1`, `kcruise2`, `kspecial`
  - t6 (33): `kbase`, `kbattle` ×3, `kcruise1` ×7, `kdestroy` ×8, `kfreight` ×2, `kmining`, `kresear` ×2, `kresear2` ×2, `ksensor` ×2, `kspecial`, `kyard` ×3, `kyard2`
  - t8 (3): `rbattle`, `rcruise1` ×2

### rom4

- Title: A Call To Power
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t5, t6
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (407): `dlight` ×9, `mafield` ×75, `mdmoon` ×6, `mnebula1` ×104, `mnebula2` ×188, `mnebula4` ×24, `Momega`
  - t1 (16): `rbase`, `rbattle`, `rconst`, `rcruise1`, `rcruise2`, `rdestroy` ×2, `rfreight`, `rmining`, `rscout` ×4, `rsela`, `rturret` ×2
  - t5 (29): `ffreight`, `fmining`, `fturret` ×12, `fturret2` ×7, `zferengi` ×6, `zferyard` ×2
  - t6 (21): `kmining`, `kturret` ×8, `kturret2` ×8, `zcardbat`, `zcaryard` ×3

### rom5

- Title: Unholy Alliances
- Group: campaign and other
- Start positions: none
- Teams with a warship: t1, t2, t4
- Combat at start: yes
- Neutral and monster units: none
- Objects per team:
  - t0 (249): `dlight` ×2, `mafield` ×65, `mdmoon` ×10, `mnebula1` ×8, `mnebula2` ×112, `mnebula3` ×31, `mnebula4` ×4, `mnebula5` ×17
  - t1 (7): `rconst` ×2, `rcruise1` ×2, `rscout` ×2, `rsela`
  - t2 (60): `kbase`, `kbattle` ×5, `kcruise1` ×8, `kcruise2` ×3, `kdestroy` ×4, `kfreight` ×4, `kmining` ×3, `kresear`, `kresear2`, `ksensor` ×5, `kspecial`, `ksuperbl`, `kturret` ×15, `kturret2` ×4, `kyard` ×2, `kyard2` ×2
  - t4 (34): `bbase`, `bbattle` ×3, `bcruise1` ×5, `bcruise2`, `bdestroy` ×2, `bfreight` ×3, `bmining` ×3, `bresear`, `bresear2`, `bspecial`, `bturret` ×8, `bturret2` ×2, `byard`, `byard2` ×2
