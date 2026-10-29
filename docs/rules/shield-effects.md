# Shield effects

The rules of the shield hit effect and the shield-down overlay, recovered from the 1.2 `Armada.exe`
(SHA-256 `c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5`). Each rule names its address as
evidence (ADR 0004, INV-0001). A rule the code does not confirm is marked **inferred** or **stand-in**.
`docs/rules/combat-rules.md` holds the rest of the combat rules.

## The effect object

The game draws both effects as a scene node of a shield geometry, spawned by 0x516540 with five inputs: the
unit, an orientation, a geometry index, a duration, and a color flag.

- **Geometry.** The index picks one of seven models (names at 0x5d2a80, loaded at 0x516a70): 0 `xshldx01`,
  1 `xshlddie`, 2 `wshldadd`, 3 `wshldsub`, 4 `wcorbrfr`, 5 `wshldrmd`, 6 `wmanhiem`.
- **Duration.** A duration above 0 counts down by each frame's time and removes the effect at 0 or below
  (0x516fa0). A duration of −1 keeps the effect until its owner removes it.
- **Color.** With the flag set, the color is (1 − s, s, 0), with s the unit's shield fraction (shields over
  `maxHealth`) when the effect spawns. Without it, the color is white (0x5170e0, 0x690b60).

## Shield hit effect

### When it spawns

A hit of beam, bullet, mine, or torpedo ordnance spawns the effect when all of these hold: the ordnance does
not set `ignoreShield`, the target's shields are above 0, and the shot's segment enters the target's shield
ellipsoid (see Contact). The gate is read before the damage.

- **Projectiles** (bullet, pulse, mine, torpedo): the effect spawns after the hit's damage (0x4ec2a6,
  0x4eca69, 0x4ed45e).
- **Beams**: from a tenth of the beam's lifetime on (0x4eb170; 0.1 at 0x6173e8), each tick tests the line
  from the hardpoint the beam leaves now to the target. The first tick whose line enters the shield spawns
  the effect (0x4eb3d8). Each later tick whose line enters it turns the effect to the new contact (0x516c40,
  0x4eb3f0). A beam whose line never enters the shield shows no effect.
- A hit on a unit with its shields at 0, and a hit of ordnance that sets `ignoreShield`, spawns none. In the
  stock install the holding beam, the tractor beams, and eight more ordnance set `ignoreShield`.

### Model and duration

- **Model.** The ordnance's `shieldGeometryName`, matched against the seven names ignoring case (0x516b90,
  stored at 0x4ea50d). A missing key gives `xshldx01` (0x516b80). No stock ODF sets the key.
- **Duration.** The ordnance's `shieldDuration` (0x4ea521), also for a beam (0x4eb3c3): a beam's effect ends
  after its duration whether the beam still runs or not. The default comes from the ordnance class's
  prototype, 0.8 (0x4ea15a). Stock: 1.0 for `phasero.odf` and the 18 phaser ODFs that include it,
  `fedrep.odf`, and `remod.odf`; 0.8 for the rest.
- **Stand-in**: a name outside the seven shows no effect and is reported. The game reads the entry before its
  table.

### Contact

The hit test is 0x4ee860. It maps the shot's segment into the unit-sphere space of the target's shield test
ellipsoid and finds where the segment enters the unit sphere: t = b − √(1 − (|A|² − b²)) with A the start,
b = −A·d, and d the unit direction. It hits when the discriminant is 0 or more and 0 ≤ t ≤ the segment's
length. A segment that starts inside the shield misses.

- The contact direction u is the entry point, a unit vector in the unit-sphere space, along the model's axes.
- The world contact point is u carried through the ellipsoid's own transform and the unit's world transform
  (0x4eec60). A projectile that hits moves there.
- **Projectiles**: the segment runs from the shot's position to its next position. When it misses the
  shield, or the shields are down, the shot hits when the target point lies within one step of the shot
  (0x4ec2b0, 0x4eca85, 0x4ed46b; strictly closer than the step). That hit shows no effect.
- **Beams**: the segment runs from the beam's hardpoint to the target point.
- **Stand-in**: the target point is the unit's center. The game uses the target's hardpoint (+0x110).

### Placement and size

The effect's transform is built from u (0x55d840) and the target's shield ellipsoid (0x516540):

- **Orientation.** Rows a, b, u: a is the normalized (u.z, 0, −u.x), b = u × a. When u.x² + u.z² is below
  0.02 (0x5cb178), a is the X axis instead and is not made square to u. The geometry's own +Z axis points
  along u.
- **Size and position.** A point v of the geometry goes to (v.x·a + v.y·b + v.z·u) ⊙ (R / ρ) + c in the
  unit's model coordinates. R and c are the ellipsoid's radii and center. ρ is the geometry's bounding
  radius (0x516659, node +0x60 set by 0x58d190 from the model's +0x24). The effect sits at the ellipsoid's
  center, not at the contact point.
- **Inferred**: the draw applies the node's scale before its rows. 0x516540 stores the rows normalized and
  their lengths as the node's scale; how the draw recombines them is not traced.

### Shield ellipsoid

Each unit class has an axis-aligned ellipsoid in its model's coordinates, built once at class load by
0x46b8f0 from the box of the model as loaded at the class's `scaleSOD` (0x46b046, then 0x46b8f0 at the end of
0x46a470; `docs/rules/combat-rules.md` states the scaling):

1. **Box.** The per-axis minimum and maximum of the points the model load collects (0x5967f0, run by
   0x580040): every mesh vertex and the corners of every sprite node, through the node transforms below the
   root. The root's own transform is not applied. Hidden damage nodes count.
2. **Sprite corners** (0x599000). A sprite node takes its size from the first `@sprite_node` line of
   `sprites.spr` and its includes, in reading order, whose name is the node's name up to its first
   underscore, ignoring case (0x598f90). A billboard sprite adds its position ± its full width on the
   model's X axis and ± its full height on Y. Any other sprite adds the corners of a quad of half its width
   and height in the node's own XY plane (0x599380). A sprite is a billboard when its line's mode word is
   exactly `billboard`; a `//billboard` is not. A node with no matching line adds nothing.
3. **First radii.** The box's half extents times √3 (0x5cd0a4), around the box's center c.
4. **Fit.** The radii are scaled by √S. S is the largest |p / r₀ − c / r₀|² over the mesh vertices, through
   the node transforms including the root's own (0x46bae0). The farthest mesh vertex then lies on the
   ellipsoid.
5. **Pad.** k = `shieldPad` / (the largest fitted radius) + 1, and R = the fitted radii times k. The longest
   radius grows by exactly `shieldPad`. The default comes from the unit class's prototype, 0.5 (0x46a39d); no
   stock ODF sets the key.
6. A model without a root node, or without mesh vertices, keeps the first radii and gets no pad.

The hit test uses a second matrix (class +0x08) that the fit and the pad rescale without moving its offset.
Its ellipsoid has the radii R around c · R / r₀, not around c. The effect itself sits around c.

**Stand-in**: a box flat on an axis has no ellipsoid and shows no effect, and is reported. The game divides
by zero there.

Results for three stock models (with the sprite corners):

| Model | Center | R (x, y, z) | Scale R / 4.5088 |
|---|---|---|---|
| `Kdestroy` | (−0.02, −1.91, 3.77) | 16.46, 7.59, 15.42 | 3.65, 1.68, 3.42 |
| `Bcruise1` | (0, 0.83, 0) | 17.75, 18.67, 17.75 | 3.94, 4.14, 3.94 |
| `Fdestroy` | (0, 0.35, −0.04) | 12.62, 3.46, 16.93 | 2.80, 0.77, 3.75 |

`Xshldx01.SOD` has a bounding radius of 4.5087652: a cap from z 2.5 to 5.0 around (0, 0, 3.75).

### Flipbook

`Xshldx01.SOD`'s meshes reference the texture animation `tex4x4` (`Sprites/tex_anim.spr`: 16 frames in
0.5 s, `@auto=square`). The texture `Xshldx01` holds a ring that grows from a dot and fades out.

- **Clock.** The animation time is the game clock (0x67dcb4, which stops while the game is paused) minus the
  node's start time. 0x516540 never sets the start time, so it is 0 (0x58cc3e reads node +0x14; 0x51662c
  writes +0x10). A new effect shows the frame the clock is at, not the first.
- **Frame** (0x576220): t′ = (t + the reference's offset) modulo the length L, then frame =
  trunc(t′ / (L + 0.05) × count) (0.05 at 0x5ccf24). For `tex4x4` the frame is trunc(16 · t′ / 0.55):
  frames 0 to 14, each shown 0.034375 s. Frame 15 never shows.
- The animation loops (node flags 3, bit 0 set) and steps without blending. A model's animation reference
  is always stepped (0x577350 does not copy the mode).
- **Cells** (0x59a5b0). n = trunc(√count) columns and rows, each 1/n wide; frame i is column i mod n, row
  i div n, as a UV offset added to the mesh's own UVs (0x5a3a65). Row 0 is the top row of the image as the
  game uploads it (the file's origin bit, 0x59c730, 0x59f050).
- The SOD reference's offset exists from SOD version 1.8 on; `Xshldx01.SOD` is version 1.7, so it is 0.

### Drawing

- **Color.** The effect draws through the game's "shield" lighting material (0x5169a0): lighting model 0,
  unlit. Each vertex takes the color (0x5a4200), and the texture multiplies it. Ambient and specular play
  no part.
- **Blend.** The geometry's own `additive` texture material: source one, destination one, vertex alpha 1.0
  (0x5891ef). The texture and the blend stay; only the lighting material is replaced (0x58fd80).
- **Depth.** The effect is drawn after the world, depth-tested against the hull (less or equal), and writes
  no depth (0x580a00, 0x580a35). Its triangles join the other transparent triangles, sorted back to front by
  face (0x5a62c0, 0x5a6360).
- **Culling.** `Xshldx01.SOD`'s meshes have a cull byte of 0, so both sides of the cap draw.
- **Texture.** A Direct3D device of 16 or 32 bits loads `Textures\RGB` (0x59fa90).
- **Stand-in**: Godot sorts transparent objects by object, not by face.
- **Not traced**: whether the texture takes the color-key path (0x59f320), which can make black texels
  transparent when the texture format has no alpha.

## Shield-down overlay

The `xshlddie` geometry (index 1), white, without expiry. It is spawned when the shield generator goes
offline (0x467b70, 0x4679b0) and removed when the generator works again (0x467e06) or the unit turns
derelict (0x466e7e). It is spawned again when the crew recovers while the generator is still offline
(0x466f07). A unit never has more than one.

Every spawn checks these conditions (for example 0x469368–0x4693a6):

- The unit has no cloaking device, or its cloak state is 0, uncloaked (unit +0x1ac, the `CloakingDevice`
  cast at 0x46e79b; state at +0x40, `m_cloakState`). The overlay never spawns while the unit cloaks, is
  cloaked, or decloaks.
- The unit is not derelict.
- The class's shield generator has hit points above 0 (unit +0x4e4, the record's maximum from the ODF).
- The unit has no overlay yet.

The overlay sits on the shield ellipsoid like a hit effect, sized R / ρ around c with ρ the bounding radius
of `xshlddie`.

- **Inferred**: its orientation is the identity (0x68de08), so its +Z axis is the model's +Z axis.
- **Not built**: the sandbox has no cloaking device, so the cloak condition never applies.

## Other effects of the same object

Special weapons spawn the same object with a fixed model:

- `wshldadd` and `wshldsub`: Energy Shield Converter and Shield Inversion (0x4dca33, 0x4e5e68 and siblings).
- `wmanhiem`: the Manheim effect, 2.0 s (0x4dffd1).
- `wshldrmd`: Shield Remodulation (0x5624c0).
- `wcorbrfr`: the reflector (0x50883c).

These are not built.
