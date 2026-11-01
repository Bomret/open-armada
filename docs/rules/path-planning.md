# Path planning

The rules by which a move order gets its path in the 1.2 `Armada.exe`
(SHA-256 `c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5`): the grid of cell costs the map's objects
write into, the A* over it, the rubber banding that straightens the result, the order list and the replans that drive
the planner, and the tunables of `RTS_CFG.h`. Each rule names its address as evidence (ADR 0004, INV-0001). A rule the
code does not confirm is marked **inferred** or **stand-in**. `docs/rules/flight-model.md` ("The move order") holds what
a ship does with the path, `docs/rules/nebulae.md` the nebula and belt data the planner reads, and
`docs/rules/forced-motion.md` the black hole. The source file of the classes is
`Ai\Path Planning\a_star_graph_node.cpp` (`A_Star_Search`, `A_Star_Graph_Map`, `A_Star_Node`, `A_Star_Graph_Node`).

The planner is one global object built at map load (0x408e30, then 0x4093b0) and used by every ship, each planning its
own path (the only caller of 0x40a310 is 0x42ded0). Nothing in it is random. The sandbox's code is `PathPlanner`
(`PathQuadtree`, `PathSearch`), `PathObstacles`, `PathPlanConfig`, and `PlannedMove`.

## The keys

0x4453f0 reads these into globals when the game starts; a key it cannot read leaves the exe's default. An int key is
read only through an `int` declaration (0x4fc0d0) and a float key only through a `float` one (0x4fc1d0); a key
declared the other way is not read and the default stands. The sandbox reports each missing, non-numeric, or
mistyped key once, under one cause with the key names, and takes the default.

| Key | Global | Exe default | Install | Use |
|---|---|---|---|---|
| `HIGHEST_QUADTREE_GRID_RESOLUTION` | 0x5fbc48 | 100 | 50 | the finest cell |
| `USE_PATH_PLANNING` | 0x5fb86c | 1 | 1 | 0: every plan is the two points [ship, goal] |
| `IMPASSABLE_TERRAIN` | 0x5fb870 | -1 | -1 | the value no path enters |
| `DEFAULT_TERRAIN_VALUE` | 0x5fb874 | 10 | 100 | a free cell's value; the heuristic's weight; the both-impassable edge cost |
| `CANNOT_PATH_INTO_COST` | 0x5fb878 | 200 | 200 | a nebula's `pathing_cost` default less 1 (`docs/rules/nebulae.md`); nothing in the planner reads it |
| `PERFORM_RUBBER_BANDING` | 0x5fb87c | 1 | 1 | 0: the path is the cell centers |
| `NEBULAE_PATHING_SAFETY_COEFFICIENT` | 0x5fb890 | 1.5 | 1.8 | a nebula's pad in effect radii |
| `ASTEROID_BELT_PATHING_COST` | 0x5fb8a4 | 1000 | 201 | the value along a belt |
| `ASTEROID_BELT_PATHING_DISTANCE` | 0x5fb8a8 | 200 | 150 | twice the half size of a belt's and a field's squares |
| `BLACKHOLE_PATHING_COST` | 0x5fb894 | 1000 | `IMPASSABLE_TERRAIN` | a black hole's value |
| `BLACKHOLE_PATHING_SAFETY_COEFFICIENT` | 0x5fb898 | 1.5 | 1.5 | a black hole's pad in event radii |
| `FORMATION_SPACING` | 0x5fbc20 | 40 | 100 | a field's outer ring adds it to the pad |
| `MAP_EDGE_BUFFER` | 0x5fbc44 | 100 | 100 | the goal clamp's margin (0x44ab30) |
| `MAP_EDGE_EPSILON` | 0x5fbc40 | 0.1 | 0.1 | the clamp's step back from the high edges |

`OFF_MAP_PATHING_COST` (0x5fb868) and `NEBULAE_PATHING_COST` (0x5fb88c) are read and never used by the planner.
`SHOW_QUADTREE` (0x633a6c) only draws the grid (0x409660).

**An identifier value.** The install gives `BLACKHOLE_PATHING_COST = IMPASSABLE_TERRAIN;`. The reader's expression
evaluator takes an identifier (token type 4, 0x4f82f0) and looks the name up, by exact comparison, among the
declarations read so far (0x4fc2e0, then 0x4fc1d0, then 0x4fc0d0): its value is that declaration's number. The install
declares `IMPASSABLE_TERRAIN` (-1) at line 203, before line 236, so the black hole's cost is -1.0, the impassable value.
A name declared later, or never, is not found: the key is not read and its default stands. An `int` declaration takes
the truncation of the named value toward zero (the C `ftol`, 0x4f82f0): `int K = RATIO;` with `RATIO` 1.5 is 1, and a
`float` declaration keeps 1.5. `GameConstants` evaluates an identifier the same way.

## The grid

**Coordinates.** A grid coordinate is the world coordinate less the map's lowest value on that axis plus 500
(0x5ca220), so the grid reaches 500 beyond the map on every side. The extent is |highest − lowest| + 1000 on each
axis (kept as a float, fractions included: 0x408e30 stores the bits at 0x408ee9 and 0x408eec, and 0x40a998 compares it
as a float). The map's bounds are read by 0x44abb0, which the save loader
0x54ca90 calls: six floats in the order lowest X, highest X, lowest Y, highest Y, lowest Z, highest Z into
0x6343f0, 0x6343fc, 0x6343f4, 0x634400, 0x6343f8, 0x634404; 0x44a810 holds the defaults. The sandbox's `MapBounds` is
the map file's bounds block, which 0x44abb0 reads. A point below 0 is put at 0, and a point past the extent at the
extent cut down to a whole number of finest cells (less the epsilon, which is 0).

**The quadtree** (0x408e30). The finest cell is `HIGHEST_QUADTREE_GRID_RESOLUTION`. The finest level holds
ceil(extent / cell) cells on each axis; each coarser level holds half as many, rounded up, down to one cell, and a
cell is twice the size of the one below. A cell is a value, a split flag, and a graph node; every cell starts at
`DEFAULT_TERRAIN_VALUE`. A cell's box is (column × size, row × size) to one size further. **Stand-in:** the level
count is taken from an exact integer base-2 logarithm; the game divides two natural logarithms in extended
precision, which can differ for a count that is a power of 2.

**The border** (0x408e30). Four strips of the impassable value, each the finest cell plus 500 wide, along the grid's
edges. With the install's 50 they reach 550 in from the edge, which is 50 inside the map, and the next cell, which the
strip touches, is impassable too.

**Writing a rectangle** (0x409900, then 0x40c700 from the top level). 0x409900 takes the rectangle's two corners and a
pad, grows the rectangle by the pad on every side, puts each corner into the grid (below 0 at 0, past the extent at
the snapped extent), and writes. A cell the rectangle covers whole, and that is not split, takes the value. A cell it
covers in part is split first: its four children take its value, then each child is written in the order
(2c, 2r), (2c, 2r+1), (2c+1, 2r), (2c+1, 2r+1). At the finest level any cell the rectangle touches, **including at an
edge** (a closed interval test), takes the value. **The write rule** (0x40c831 and its twins; always used with the
force flag 1): a value goes in when the old one is lower and not impassable, when the value is impassable, or when the
old value is the default. So a value never lowers a cell that holds more, except that a cell at the default takes any
value, a lower one too (the stock `mnebula4` cost 2), and the impassable value always wins and is never replaced. Any
write sets the dirty flag.

**Lookup** (0x40c5e0). The point is put back into the grid, then the tree is descended from the top, taking at each
split cell the child on the side of the cell's center the point lies on (a point at the center goes to the higher
side), until the cell is not split.

## Obstacles

Every writer calls 0x409900 with the object's position (X, Z) as a point rectangle unless stated.

| Writer | Object | Pad | Value |
|---|---|---|---|
| 0x44e7b0 | an asteroid field without a belt path | `ASTEROID_BELT_PATHING_DISTANCE` × 0.5 + `FORMATION_SPACING`, then × 0.5 alone | 3 × `DEFAULT_TERRAIN_VALUE` (ring), then impassable (core) |
| 0x434c30 | an asteroid belt (a path of type 3), once | `ASTEROID_BELT_PATHING_DISTANCE` × 0.5, at each sampled point | `ASTEROID_BELT_PATHING_COST` truncated |
| 0x450810 | a black hole | `BLACKHOLE_PATHING_SAFETY_COEFFICIENT` × the class's +0x1ec | `BLACKHOLE_PATHING_COST` truncated |
| 0x451e00 | a nebula | `NEBULAE_PATHING_SAFETY_COEFFICIENT` × `effectRadius` (+0x1ec) | the definition's `pathing_cost` (+0x258) truncated |
| 0x453f00 | a wormhole | the model's bounding radius (+0x1e0) | 500 (the float at 0x5fb89c) |
| 0x4531a0 | scrap | the object's radius (+0x60) | 198 |
| 0x4510a0 | a construction object | none; the model's box at the object's position | 198 |
| 0x46d5c0 | a craft whose class has `is_starbase` (+0x235) | none; the model's box at the object's position | 198 |

Planets, ships, and ion storms write nothing. The values are the truncation of a float (the C `ftol`), so a cost of
400.9 is 400. The squares are axis-aligned.

- **The black hole's radius.** 0x450810 multiplies the safety coefficient by the class's +0x1ec, which is the area
  object's `effectRadius`; but the black hole class (0x44b7d0) reads its own keys and then copies its `EventRadius` over
  that field. The pad is `BLACKHOLE_PATHING_SAFETY_COEFFICIENT` × `EventRadius` (10 when the definition lacks the key),
  confirmed in the instructions of 0x450810 and 0x44b7d0. A black hole of the install pads by 1.5 × its event radius.
- **A belt** (0x434c30). The path's parameter t runs from 0 in steps of 0.01 (0x5caa90, added in a float) while it is
  below the number of points. The point at t (0x4af590): the integer part picks the segment and the fraction goes
  through a cubic Hermite curve between the segment's two points; each end's tangent is the unit chord between the
  neighbours (the missing neighbour of the first and last segment is the end point reflected through the other end)
  times the segment's length (0x4af2c0); from t = count − 1 on it is the last point, and a segment shorter than
  0.032 stays at its start. A square is written at the first point and at every point farther than the half distance (in
  3D) from the last one written. Only a path of type 3 not yet written is sampled.
- **An asteroid field** (0x44e7b0). The outer ring is written because the switch at 0x5fdfc8 is 1 and nothing writes it;
  `fieldRadius` is not read.
- **A starbase's, a construction object's box** (0x46d5c0, 0x4510a0). The box is the object's own (vtable +4) at its
  position's X and Z, not turned (**inferred**). The sandbox takes the model's box scaled by the placed scale
  (**stand-in**: how the game scales or turns the object's box is not recovered). Its second pad (`footprintBuffer`)
  goes to another grid, not the planner's.
- **Removal.** A destroyed starbase, wormhole, or construction object writes the default over its rectangle with the
  same rule (0x409a80). The default replaces nothing but a cell already at the default, so it changes nothing and
  sets the dirty flag. The sandbox never destroys these.
- **Order.** The game writes each as its object is created. The sandbox writes the asteroid fields and belts, the
  nebulae, the black holes, and the starbases in that order (**stand-in**); the order matters only where a value below
  the default is written.
- Not built: wormholes, construction objects, and scrap do not exist in the sandbox. A mask-3 search through a
  wormhole's nodes (0x40a627) and the owner record's byte +0xaa that selects it are not built.

## The region graph

The graph is rebuilt before a plan whenever a write set the dirty flag (0x40a310's head), and by the AI update 0x431f20
(0x4093b0). A ship's movement and the death of a ship write nothing.

1. **Merge** (0x40d280). A split cell whose four children are all leaves of one value becomes a leaf of that value.
   **Stand-in:** a split cell with a child beyond the grid keeps its children (the game compares an uninitialized
   value there).
2. **Nodes** (0x40dd70, from 0x40cd30). Every leaf gets a node at the cell's center, put back into the grid like a
   point, with the leaf's value taken from its cell.
3. **Edges** (0x40d450 and 0x40dfd0). Each split cell builds its four children in the order above, collecting for
   each the leaves along its four sides (lower Z, upper Z, upper X, lower X); it passes the outer sides' lists up and
   links the facing sides of its children in four walks: child (2c, 2r)'s upper Z side with child (2c, 2r+1)'s lower Z,
   (2c, 2r)'s upper X with (2c+1, 2r)'s lower X, (2c+1, 2r+1)'s lower Z with (2c+1, 2r)'s upper Z, and (2c+1, 2r+1)'s
   lower X with (2c, 2r+1)'s upper X. A walk goes along the two sorted lists as the intervals' ends pass each other,
   swapping which list is which at each step. Two leaves link when their intervals overlap by the test
   `x0 ≤ y0 and y0 < x1, or x0 < y1 and y1 ≤ x1` for the pair taken in that order, so a leaf that lies strictly inside
   the other's interval links only when it is the second of the pair. Leaves that touch only at a corner do not link.
4. **Costs and directions** (0x40e320, 0x40e470). Let the two leaves have half widths a and b, values A and B, and
   centers a distance d apart in the plane; w = a / (a + b), and the edge costs (A × w + B × (1 − w)) × d, with
   the impassable value -1 in the blend. Both passable: an edge each way (the first leaf's, then the second's). One
   impassable: only the edge out of the impassable leaf. Both impassable: an edge each way at
   `DEFAULT_TERRAIN_VALUE` × 10000. So the search can leave an impassable cell and none can enter. A ship standing in
   an impassable cell still gets no path from a move order, because step 2 refuses a goal in an impassable leaf and a
   move order's first pass plans to the ship's own position ("The order list and the replans").
5. **Wormhole pairs** (0x40c2f0): none in the sandbox, so the heuristic's weight is `DEFAULT_TERRAIN_VALUE`.

The order in which a node's edges were added decides which of equal-f nodes the search opens first; the sandbox adds
them in the game's order.

## The search

0x408500 runs the A* from the ship's leaf to the goal's. The nodes' f and g are floats.

- **The open list** is a binary min-heap of nodes on their live f: a push sifts up while the parent's f is above the
  new node's (0x408660); a pop (0x408d50) moves the last node to the root's place, walks the hole down along the
  child of the smaller f (the right child on a tie) and sifts the node back up. A node may be in the heap more than once.
- **Start** (0x408940). The start is relaxed from itself at cost 0, which puts it in the heap, then closed and
  expanded.
- **Relax** (0x408660). A node reached for the first time (f = 0) gets g = g(from) + cost, f = g + weight × the
  straight distance to the goal, the parent, and enters the heap. A **closed** node is reopened when g(from) + cost +
  h(n), with h(n) = f(n) − g(n) as stored, is below its f: it takes the new g, f, and parent, enters the heap again,
  and is open. **A node that is open and already visited is never improved**, even by a cheaper route (a quirk).
- **End.** The search ends when the node taken from the heap is the goal; it fails when the heap empties. There is no
  node limit and no time limit. The route is the goal's parents back to the start.
- A cost below `DEFAULT_TERRAIN_VALUE` in a region (a nebula of cost 2) makes the heuristic overestimate there.

## A plan (0x40a310)

0x40a310 takes the ship's position, the goal, and two flags and returns the points, or nothing.

1. **The goal clamp** (0x44ab30). The goal's X and Z are put at least `MAP_EDGE_BUFFER` inside the map: X above
   (highest X − buffer) becomes that less `MAP_EDGE_EPSILON`, X below (lowest X + buffer) becomes that, likewise Z. The
   clamp decides the plan; the path's last point is the goal as given while the ship's byte +0x488
   (`m_force_onto_map`, 0x470180) is 0: **inferred** always, since no code sets it, and 0x430920 clears it. A ship's
   own position is not clamped by this, only put back into the grid.
2. **No path.** The clamped goal must lie in the grid (0x40a950: 0 ≤ x ≤ extent on both axes); its leaf must not be
   impassable. Otherwise the plan is nothing, whatever `USE_PATH_PLANNING` is. The goal is not moved out of an
   obstacle. The caller (0x42ded0) then gives the ship the stop order, unless its process type is 0x19 ("The order list and the replans"); the ship holds, and an attack ends.
3. **The shortcut.** When the ship's leaf and the goal's leaf have the same node, or `USE_PATH_PLANNING` is 0, or the
   ship's byte +0x1b1 (a phased ship, **inferred**) is set, the points are [ship, goal]. No search runs.
4. **The search** between the two leaves' nodes; no route is no plan.
5. **Straightening.** With `PERFORM_RUBBER_BANDING` set and at least 3 nodes on the route, the portal graph and the
   funnel below; if the portal search fails, or there are fewer than 3 nodes, the points are the leaf centers.
6. **The points.** Each node becomes (lowest X + x − 500, H, lowest Z + z − 500) clamped by 0x44ab30, with H the
   middle of the combat band, (`SHIP_COMBAT_CEILING` + `SHIP_COMBAT_FLOOR`) / 2 (0x40a792; 38.5 stock). Then the first
   point becomes the ship's position, its own height included, and the last the goal.

**The portal graph** (0x40ac80). The cells of the route are looked up again from the nodes' positions (the start's from
the start position): [the start's, the cell of each route node after the first, the goal's], so the last two are the
same cell. For each pair of consecutive cells 0x40aa20 gives the two ends of their shared border, ordered by the
direction of travel: let the second cell's center be (dx, dz) from the first's; when |dx| ≤ |dz| the border is along X,
at the second cell's lower Z for dz ≥ 0 (case 0, ends from the lower X to the higher) and at its upper Z otherwise (case 1,
from the higher X to the lower); else at the second cell's lower X for dx > 0 (case 2, from the higher Z to the lower) and
at its upper X otherwise (case 3, from the lower Z to the higher). The ends span the overlap of the two cells' intervals,
at exact cell corners with no inset. A node is made for the start, for each end, and for the goal; each of the two nodes
of the previous border gets an edge to each of the two new ones, costing the previous cell's value times the distance;
the last border's two nodes get an edge to the goal costing the last cell's value times the distance. A search
(weight `DEFAULT_TERRAIN_VALUE`) from the start to the goal picks one end per border.

**The funnel** (0x40b8c0). It walks the portal search's nodes P and the cells C (the same count). The anchor starts at
P[0], and the output starts with it. For each node i in turn, with the border of C[i−1] and C[i] (none when they are
the same cell, the last step), the ends are ordered by 0x40b4f0 into side A and side B: with the anchor a and the ends
n1, n2, A is n1 when (n2.z − a.z)(n1.x − a.x) < (n2.x − a.x)(n1.z − a.z), else n2.

- **The first border after the anchor** opens the funnel: sides A and B are its ends, and P[i] is accepted.
- **Any later border** stops the scan unless the anchor lies strictly on the near side of the border line, by case:
  case 0 needs anchor.z < A.z, case 1 anchor.z > A.z, case 2 anchor.x < A.x, case 3 anchor.x > A.x. Then it narrows
  the funnel (0x40b680; the cross products are of vectors from the anchor, written (u.z)(v.x) against (u.x)(v.z)):
  side A moves to the new A when (newA.z)(A.x) < (newA.x)(A.z), and the funnel fails if then (B.x)(newA.z) ≤
  (newA.x)(B.z); side B moves to the new B when (newB.z)(B.x) > (newB.x)(B.z), and the funnel fails if then
  (A.x)(newB.z) > (newB.x)(A.z) or the two are equal. A failed funnel stops the scan.
- **P[i] must lie inside the funnel:** (P.z)(A.x) ≤ (P.x)(A.z) and not (P.z)(B.x) < (P.x)(B.z), all relative to the anchor.
  Inside, P[i] is accepted and i advances; outside, the scan stops.
- **On a stop** at node i, the last accepted P becomes a waypoint and the new anchor, the funnel is dropped, and the
  scan restarts at the node after it. The goal is the last node and ends the output; the loop checks it as any other.

The sign conventions above are read in the instructions of 0x40b490, 0x40b4f0, and 0x40b680 (the decompiler garbles
the flag tests of the return values: every failing return is 0 and every success 1). Output: the start, a subset of the
borders' ends, the goal.

## The order list and the replans (0x42ded0)

A ship plans in its process, not when it is ordered: 0x42ded0 plans whenever the ship has no path.

- **The order list.** A player's move is [the ship's position at order time, the goal]; an attack's path move
  (0x428d20) is [the target]. The index starts at -1 and the first pass makes it 0 and drops the path.
- **Passing a point.** While the index is below the last, a pass whose 3D squared distance from the ship to the
  point is below `pathLeadDistance` squared (class +0x214, stored squared) makes the index one more and drops the path.
  This is an else of the first rule, so the first pass plans to the list's first point, the ship's own position, and the
  second passes it. **Stand-in:** the sandbox runs the two passes at once when the order is given.
- **Planning.** With no path the planner is asked for a path from the ship's position to the point at the index. Its
  path starts the carrot afresh (`docs/rules/flight-model.md`, "The move order"); the maneuver and the arrival test use
  the list's last point, not the path's. With no path the ship gets the stop order and holds, with no fault, unless its process type
  (+0x20, the primary command) is 0x19, which returns first. The stop order (0x46eac0, a method of the ship) writes a
  pending command record at the ship (+0x10c): command 2 with a one-point list at the ship's own position. The next tick's
  process (0x4267a0, vtable 0x5ca7e0 slot 9) reads that record (0x46ea90); a non-zero command goes to 0x426ad0 (slot 12),
  and 0x46ea00 moves pending to current. In 0x426ad0 `case 2` (once 0x4268d0 passes) sets the process type to 2 and takes
  the one-point list as the order list. So the stop order is a move to the ship's own position, which ends on arrival,
  and it replaces whatever command ran. A move order's type is 2, an attack's 5, 6 or 7 (0x426ad0; **inferred** for the
  attack from the command's guards: a craft with a weapon class and a unit, point or weapon target), so both take the stop
  order; the attack is gone from the next tick. Type 0x19 is another command (0x426ad0 stores a random timer and the
  ship's position for it; its name is not recovered); the sandbox has no such command. So a ship standing in an impassable cell (a wall, a black hole's square, the strip along the map's edge,
  about 100 inside it) gets no path for any move order: the first pass plans to its own position, and the ship holds.
- **The stuck test.** While a path exists and the list's last point is farther than the lead distance from the ship
  (squared distances), the game's clock in whole seconds (truncated; **inferred** to be seconds: ticks × 0.001, 0x5d1e68)
  is read. When the ship's squared distance from the stored place exceeds 1.0, the clock and the place are stored. When
  the clock is more than 12 above the stored one (the stop flag +0x104 being clear), the index becomes 0 and the path is
  dropped; planning follows in the same pass, so the replan leads to the list's **first** point, the ship's position at the
  order. **Inferred** that the stored place starts such that the first pass stores; the sandbox stores on the first pass.
  Once the ship is within the lead distance of that point, the next pass passes it and plans to the goal again.
- **A direct target** (0x428d20, with the setting at 0x632ec4 off): an attack's closing farther than 400 plans a path
  to the target; the order list is built again, and the path with it, once the target has moved more than 300 from the
  stored one (`docs/rules/combat-rules.md`, "Attack order"). 0x42ded0 plans, advances, and replans it as above. A target
  no path leads to gives the ship the stop order (the attack's process type is not 0x19), and that order ends the attack
  (0x46eac0, 0x4267a0, 0x426ad0 `case 2`, as in "The order list and the replans"): the ship holds with no order and the
  attack is gone from the next tick (**stand-in:** the sandbox drops it in the tick that finds no path). The two stop
  orders of 0x4294e0 (the AI's attack order, which calls 0x46eac0(2) at 0x429568 when the ship's target field +0x1c4 is
  empty and at 0x42990e, the shared exit LAB_00429907, where it holds the ship in range) go through the same chain. A
  target in an impassable cell or off the grid is refused before any
  search (the grid test and the leaf test of 0x40a310), so a ship ordered to attack into a wall ends the attack at once.

## Stand-ins and open questions

- **Stand-in:** the merge of a split cell with an absent child (above), the integer logarithm, the obstacles' write
  order, a starbase's box, and the one-tick collapse of the first two passes.
- **Inferred:** `m_force_onto_map` always 0; the clock unit of the stuck test.
- **Not recovered:** the owner record's byte +0xaa (0x4720d0) that makes the mask 3; the removal of a wormhole pair
  from the planner's list; the flag of the 0x40dfd0 branch's direction beyond what is stated (read in pseudocode, the
  same as 0x40e320); the label of the setting at 0x632ec4.
- **Not known:** whether anything gives the attack back after the stop order ended it (an AI or player re-issue, or the
  target field +0x1c4, which `case 2` does not clear on this path). The sandbox gives it back to nothing.
