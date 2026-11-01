# Program index of Armada.exe

This index says where a function, rule, or topic lives in `Armada.exe`, so an investigation starts at the right
address instead of searching the whole program. It holds addresses and our own words only (INV-0001). The
rules themselves live in the rule notes it links to.

## The program

| Fact | Value |
|---|---|
| File | `Armada.exe` of the Star Trek: Armada 1.2 install (`$ARMADA_DIR`) |
| Size | 2,502,701 bytes |
| SHA-256 | `c5f32464c6b4322469d79ba19d33f26fde67b5c06ca2ee24a071cd67a52a88b5` |
| Format | PE, x86, MSVC C++ with RTTI |
| Image base | 0x400000 |
| Sections | `.text`, `.rdata`, `.data`, `.idata`, `.rsrc`, `.reloc` |
| Game code band | 0x401000–0x5bb4c0: the game and its Storm3D engine, 9,401 functions |
| Outside the band | 0x5bb4c2 onward: import thunks, the C runtime, `entry` (0x5bbdac), and exception unwind handlers, 4,690 functions |

Every routine in the band is game layer: the 1.2 release has no platform layer added after the fact. The
`layer` column is there so the format matches the other projects that use it.

## How to look something up

1. Search this index for the topic, the address, the ODF key, or the class.
2. Read the local dump (`decomp/armada/dump/`, see `tools/GhidraDump/README.md`): `functions/<0xaddress>.c`
   for one function, `functions.tsv` for callers, callees, and strings (a caller can be a switch case block of
   a larger routine; see the dump README), `strings.tsv` to go from a string to
   the functions that use it, `vtables.tsv` to go from a class to its methods.
3. Confirm unclear pseudocode against the instructions (Ghidra on `decomp/armada/project/`, or `objdump`). The pseudocode can be wrong: 0x501820
   returns the remainder of a division where the pseudocode shows the quotient.
4. Use REA only for a question steps 1 to 3 cannot answer. Add the routines it finds to this index, and apply
   its other findings to the Ghidra project. Re-run the dump only as `tools/GhidraDump/README.md`,
   "When to re-run", says.

Strong anchors in this program, strongest first:

- **ODF keys.** Every definition key is a string passed to an ODF getter, so a key leads to the routine that
  reads it (see ODF keys).
- **RTTI class names.** 444 vtables carry their class name. A class's own vtable slots lie in one address
  range, which places the class in the program (see By topic).
- **Source paths.** Assert and log calls pass the original source file, `D:\Startrek\Source\<dir>\<file>.cpp`.
  A path places a function in its source file.
- **Log texts.** Diagnostic strings name methods, for example `Program::GameOpenLoad() took %d seconds.`

## Status and columns

A routine row has the columns address, our name, role, status, layer, evidence anchor, and rule note.

- **confirmed**: read in the decompile or the instructions.
- **inferred**: from callers, strings, keys, or the class's vtable, not read.
- **unknown**: the role is open; the row records the address and the anchor that led to it.

Names are `Class_Method` for a method of an RTTI class and `Subsystem_Verb` otherwise. The project update
(`tools/GhidraDump/README.md`) applies the names of "By address" to the Ghidra project.

## By topic

Address ranges come from the classes' own vtable slots and from source paths; they are **inferred** and
approximate. A class whose name ends in `Class` is the definition (read from an ODF) of the class without
the suffix.

### Program, startup, and frame loop

Range 0x43f2b0–0x4434d0 (`Game Logic\Program.cpp`). Not surveyed; see Unexplored.

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x4404a0 | Program_GameOpenPreLoad | Loads the game before a mission; checks the CD | inferred | game | log text `GameOpenPreLoad() took %d seconds.` | — |
| 0x4407b0 | Program_GameOpenLoad | Second loading step of a mission | inferred | game | log text `Program::GameOpenLoad() took %d seconds.` | — |
| 0x43f1a0 | Program_Tick | One game tick: the area, simulate, and post-simulate passes | confirmed | game | calls 0x4713c0, 0x471410, 0x471560 | [forced-motion](forced-motion.md#tick-order) |
| 0x43f2b0 | Program_Frame | One frame: the AI update (0x433b30), then the world tick (0x43f1a0), then the weapon and effect updates | confirmed | game | calls 0x433b30, 0x43f1a0 | [flight-model](flight-model.md#the-tick) |
| 0x4453f0 | RtsCfg_Load | Reads `RTS_CFG.h` and `ART_CFG.h`: `MAX_PREVENTION_ROTATION`, the `ST3D_CHUNK_*` values | confirmed | game | strings `MAX_PREVENTION_ROTATION`, `ST3D_CHUNK_DENSITY` | [forced-motion](forced-motion.md#collision-separation) |
| 0x4713c0 | GameObject_AreaPass | Calls slot 0x8c on every object | confirmed | game | — | [forced-motion](forced-motion.md#tick-order) |
| 0x471410 | GameObject_SimulatePass | Calls slot 0x84 (simulate) on every object | confirmed | game | — | [forced-motion](forced-motion.md#tick-order) |
| 0x471560 | GameObject_PostSimulatePass | Calls slot 0x88 on every object that is not dying | confirmed | game | — | [forced-motion](forced-motion.md#tick-order) |

### Object definitions (ODF)

The getters at 0x5537a0–0x554a40 (`Misc\ParameterDB.cpp`) read one key from a definition. Each takes the
key name, a pointer to the field, and a default.

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x553ba0 | Odf_GetInt | Reads a whole number key | inferred | game | `sscanf` without locale; 112 call sites | — |
| 0x553c30 | Odf_GetFloat | Reads a decimal key; the first line whose start matches the key wins | confirmed | game | 256 call sites | [combat-rules](combat-rules.md#model-scale) |
| 0x553d40 | Odf_GetString | Reads a text key into a buffer | inferred | game | 8 call sites | — |
| 0x553e20 | Odf_GetName | Reads a text key | inferred | game | 62 call sites | — |
| 0x553f20 | Odf_GetLookup | Reads a key and maps it through a table | inferred | game | 6 call sites | — |
| 0x554060 | Odf_GetFloatList | Reads decimal values | inferred | game | 11 call sites | — |
| 0x554320 | Odf_GetVector3 | Reads three decimals (`%g %g %g`) | inferred | game | 15 call sites | — |
| 0x554430 | Odf_GetVector2 | Reads two decimals | inferred | game | 1 call site | — |
| 0x554530 | Odf_GetList | Reads a list key | unknown | game | 5 call sites | — |
| 0x5537a0 | Odf_GetText | Reads a key as a text object | unknown | game | 8 call sites | — |
| 0x4ffd70 | Name_Equals | Compares two names, case-sensitive | confirmed | game | — | [combat-rules](combat-rules.md#values-that-depend-on-the-target) |

### Races

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x443df0 | RaceLoader_Load | Reads `races_odf` and `numberOfRaces`, then each race | inferred | game | `RaceLoader` vtable slot 0 | — |
| 0x4440b0 | Race_FindByName | Returns the race with a given name | confirmed | game | called with `borg` (0x601240) | [combat-rules](combat-rules.md#damage-nodes) |
| 0x4442b0 | Race_Load | Reads a race's keys: crew statuses, repair factors, officers | confirmed | game | `Game Logic\Race.cpp`; key `crewYellowStatus` | [combat-rules](combat-rules.md#damage-nodes) |

### Game object definitions

Each definition class reads its ODF in a load routine that first calls its base class's load. The ODF keys
section lists every key with its reader.

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x46a0d0 | GameObjectClass_Init | Sets the prototype defaults of a definition, for example `shieldPad` 0.5 | confirmed | game | 0x46a39d | [shield-effects](shield-effects.md#shield-ellipsoid) |
| 0x46a470 | GameObjectClass_Load | Reads the keys every object has, including `scaleSOD`, and loads the model | confirmed | game | `GameObjectClass.cpp` | [combat-rules](combat-rules.md#model-scale) |
| 0x46b8f0 | GameObjectClass_ShieldEllipsoid | Builds the shield ellipsoid from the box of the loaded model | confirmed | game | — | [shield-effects](shield-effects.md#shield-ellipsoid) |
| 0x46bae0 | GameObjectClass_ModelPoints | Collects the model's points through the node transforms | confirmed | game | — | [shield-effects](shield-effects.md#shield-ellipsoid) |
| 0x45fea0 | CraftClass_Load | Reads a ship's keys; binds the damage nodes by name | confirmed | game | key `maximumCrew`; node names 0x600518–0x600548 | [combat-rules](combat-rules.md#damage-nodes) |
| 0x460f30 | CraftClass_LoadSystem | Reads one system's keys (`<system>HitPoints`, `SparkTime`, `TargetHardpoints`) | confirmed | game | `CraftClass.cpp`; `%sHitPoints` | [combat-rules](combat-rules.md#ship-systems) |
| 0x5053a0 | WeaponClass_Load | Reads a weapon's keys | inferred | game | `WeaponClass.cpp`; 35 keys | — |
| 0x4e9ed0 | OrdnanceClass_Init | Sets the prototype defaults of an ordnance definition | confirmed | game | 0x4ea15a | [shield-effects](shield-effects.md#model-and-duration) |
| 0x4ea1e0 | OrdnanceClass_Load | Reads an ordnance's keys, including the shield effect's | confirmed | game | `OrdnanceClass.cpp` | [shield-effects](shield-effects.md#model-and-duration) |

### Ships (Craft)

Range 0x462d80–0x469a40 (`Game Object\Craft\Craft.cpp`). Hits, ship systems, crew, repair, damage nodes,
and dying.

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x4617b0 | Craft_Init | Sets up a ship from its definition; copies `maximumCrew` | confirmed | game | 0x4618eb | [combat-rules](combat-rules.md#damage-nodes) |
| 0x464dd0 | Craft_RollHitLocation | Rolls where a hit lands | confirmed | game | — | [combat-rules](combat-rules.md#hit-location) |
| 0x465100 | Craft_ApplyHit | Applies a hit to shields, systems, and crew; queues a system explosion at the hit node | confirmed | game | `systemexpl1`–`7` | [combat-rules](combat-rules.md#hit-explosion) |
| 0x465e80 | Craft_UpdateLifeSupport | The ship's post-simulate tick (slot 0x88): takes crew each second while life support is destroyed, then runs the systems and the collision separation | confirmed | game | 0x465f6f | [combat-rules](combat-rules.md#ship-systems) |
| 0x466090 | Craft_DyingTimer | Sets the dying phase's length from the speed; 0 for a black-hole death | confirmed | game | — | [combat-rules](combat-rules.md#dying-units) |
| 0x466180 | Craft_RandomModelPoint | Picks a random point on the model | inferred | game | walks meshes through 0x4662b0 | [combat-rules](combat-rules.md#dying-units) |
| 0x466320 | Craft_PlayDyingExplosion | Plays a dying explosion at a random point | confirmed | game | table 0x600750 | [combat-rules](combat-rules.md#dying-units) |
| 0x4663d0 | Craft_EndDying | Ends the dying phase with the `fireball` explosion and shatters the model | confirmed | game | key `fireball` | [combat-rules](combat-rules.md#dying-units) |
| 0x466b00 | Craft_RegenerateShields | Regains shield strength up to the maximum | confirmed | game | — | [combat-rules](combat-rules.md#shields) |
| 0x466ca0 | Craft_OnCrewChanged | Re-evaluates a ship after a crew change | inferred | game | called after crew loss in 0x467b70 | [combat-rules](combat-rules.md#hit-location) |
| 0x4675d0 | Craft_ShareSystemDamage | Gives a destroyed system's share to the working systems | inferred | game | 0x467600 | [combat-rules](combat-rules.md#hit-location) |
| 0x467620 | Craft_PassSystemDamage | Sends system damage on, or to the critical share when no system works | inferred | game | — | [combat-rules](combat-rules.md#hit-location) |
| 0x4677b0 | Craft_UpdateDamageNodes | Shows the crew chain, system, and `borg` damage nodes | confirmed | game | flags at node +0x58 | [combat-rules](combat-rules.md#damage-nodes) |
| 0x4679b0 | CraftSystem_DisableFor | Takes a system offline for a time | confirmed | game | no caller found | [combat-rules](combat-rules.md#damage-nodes) |
| 0x467a80 | CraftSystem_SetHeld | Holds a system offline or releases it | confirmed | game | no caller found | — |
| 0x467b70 | CraftSystem_TakeDamage | Takes hit points; at 0 the system stops working and the crew loses its share | confirmed | game | log `%s 0x%x : System Destroyed` | [combat-rules](combat-rules.md#ship-systems) |
| 0x467d40 | CraftSystem_TryRestore | Works the system again once its timer ran out, it is not held, and it is at full hit points | confirmed | game | — | [combat-rules](combat-rules.md#repair) |
| 0x468060 | CraftSystem_RepairRate | The time one hit point of repair takes | confirmed | game | race `repairRed`, `repairYellow` | [combat-rules](combat-rules.md#repair) |
| 0x4683d0 | Craft_RepairSystems | Repairs every damaged system; emits repair sparks | confirmed | game | `xspark` | [combat-rules](combat-rules.md#repair-sparks) |
| 0x4685c0 | Craft_RepairAll | The full-repair path (0x468760) | unknown | game | — | [combat-rules](combat-rules.md#not-known) |
| 0x468a60 | Craft_HitNode | The node a shot hits: the hit location's target hardpoint nearest the firing hardpoint, else the model root | confirmed | game | class +0x358–+0x3b8 | [combat-rules](combat-rules.md#hit-explosion) |
| 0x468990 | Craft_BeginDying | Marks the unit as being destroyed | confirmed | game | — | [combat-rules](combat-rules.md#dying-units) |
| 0x467050 | Craft_TransportCycle | The per-tick transport work: counts the cycle timer down, sends a boarding party (0x55f620), and stops the transport once (crew − `TRANSPORTER_MAX`) / maximum crew is at or below the race's `crewRedStatus` | confirmed | game | crew +0x4d8, maximum crew +0x4c8, race `crewRedStatus` +0x208; test at 0x4672d0–0x467300 | [combat-rules](combat-rules.md#transport-and-boarding) |
| 0x466f70 | Craft_StartTransport | Starts a transport: attaches the `xtransport` effect to the source and the target, adds the source's team to the target's boarding block | confirmed | game | `xtransport`, `TRANSPORTER_MAX` test | [combat-rules](combat-rules.md#transport-and-boarding) |
| 0x46db90 | Craft_AttachEffect | Attaches a named effect to a unit only when it holds none (+0xdc is 0) | confirmed | game | name `xtransport`; handle +0xdc | [combat-rules](combat-rules.md#transport-and-boarding) |
| 0x46e030 | Craft_JoinTeam | The team join of the owner change: copies the new team's colors into the unit (+0x174..+0x190) and zeroes the boarding block | confirmed | game | color table 0x633ac8 | [combat-rules](combat-rules.md#transport-and-boarding) |
| 0x462d40 | Craft_PlayEventSound | Plays the unit definition's event voice line (event 8 is `eventTransport`) for the local player's team | confirmed | game | event keys read in 0x5ab750; throttle 0x5ab7d0 | [combat-rules](combat-rules.md#transport-and-boarding) |
| 0x468cd0 | Craft_TeamRelation | The relation an attack order stores: 1 for a derelict target (+0x4dc), else the attacker's team's diplomacy value toward the target's team (0x4493f0) | confirmed | game | written at 0x4275ee–0x4275f3, compared at 0x42fbde–0x42fbe9 | [combat-rules](combat-rules.md#attack-order) |
| 0x4692f0 | Craft_SpawnShieldDownOverlay | Spawns the shield-down overlay | confirmed | game | 0x469368–0x4693a6 | [shield-effects](shield-effects.md#shield-down-overlay) |
| 0x472120 | GameObject_FrameUpdate | A per-frame update that runs the damage nodes for each ship | inferred | game | calls 0x4677b0 | [combat-rules](combat-rules.md#damage-nodes) |
| 0x459440 | HoverCraft_PhysicsStep | One physics step of a ship's motion | confirmed | game | `HoverCraft.cpp`; log `pitch:% .2f, roll:` | [combat-rules](combat-rules.md#coasting), [flight-model](flight-model.md#the-physics-step) |
| 0x459a99 | HoverCraft_PhysicsStep_BorgBranch | The Borg branch inside 0x459440: on sub-step 0 the yaw spring toward +0x4b0 and the linear law c6fc × command − c6f0 × v | confirmed | game | `test ebx, ebx` at 0x459a99 | [flight-model](flight-model.md#the-borg-branch) |
| 0x455440 | HoverCraftClass_Load | Reads the physics definition's keys (`impulseSpeed`, the inertias, `borgPhysics`, `tooCloseToTurn`, and the rest) | confirmed | game | key `frontBackInertia` | [flight-model](flight-model.md#the-coefficient-map) |
| 0x45fbe0 | HoverCraftClass_SetDefaults | The compiled defaults of a ship class's physics values: `tooCloseToTurn` 25, `tooCloseForWarp` 100, `tooSlowToTurn` 1, `pathLeadDistance` 30 | confirmed | game | — | [forced-motion](forced-motion.md#move-order-arrival-and-hold) |
| 0x458750 | HoverCraft_CopyCoefficients | Derives the instance's physics coefficients from the class's keys | confirmed | game | — | [flight-model](flight-model.md#the-coefficient-map) |
| 0x458b80 | HoverCraft_ServoYaw | The yaw servo | confirmed | game | `HoverCraft.cpp`; log `Servo_Yaw` | [flight-model](flight-model.md#the-servos) |
| 0x458ca0 | HoverCraft_ServoPitch | The pitch servo | confirmed | game | — | [flight-model](flight-model.md#the-servos) |
| 0x458ea0 | HoverCraft_MatrixToAngles | Reads a ship's pitch (+0x768), yaw (+0x76c), and roll (+0x770) from its orientation matrix, and sets the heading target (+0x4b0) to the yaw; vtable slot 0x48 | confirmed | game | vtable slot 0x48 of the `HoverCraft` vtable at 0x5cbfec; called by 0x4718e0 | [flight-model](flight-model.md#the-decomposition) |
| 0x463f60 | Craft_HullSurfacePoint | The point of a unit's shield ellipsoid along a probe point's world direction | confirmed | game | called by 0x464000 | [forced-motion](forced-motion.md#collision-separation) |
| 0x462dd0 | Craft_StopMotion | Zeroes velocity, rates, and both accelerations when a ship enters a queue | confirmed | game | called from 0x5550f0 | [forced-motion](forced-motion.md#not-known) |
| 0x464000 | Craft_CollisionOverlap | Penetration depth and separation of two ships (slot 0x108) | confirmed | game | — | [forced-motion](forced-motion.md#collision-separation) |
| 0x464620 | Craft_CollisionTurn | Turns a ship's whole orientation away from a contact after a separation: a rotation about the axis of its nose and the push, multiplied into the orientation and written through the setter (0x4718e0, which decomposes it by 0x458ea0), so pitch, yaw, and roll change and the rates stay | confirmed | game | `MAX_PREVENTION_ROTATION` | [forced-motion](forced-motion.md#collision-separation), [flight-model](flight-model.md#the-decomposition) |
| 0x4644b0 | Craft_PushOutOfObstacle | Moves a ship out of an asteroid field, a planet, or a black hole's ring | confirmed | game | — | [forced-motion](forced-motion.md#push-out) |
| 0x464910 | Craft_ResolveCollisions | Separates overlapping ships by position (slot 0x110) | confirmed | game | grid box 3.0 × radius (0x5cb170) | [forced-motion](forced-motion.md#collision-separation) |
| 0x4664e0 | Craft_TargetValid | Whether a weapon may take a target, and the cursor command for a unit under the pointer (slot 0x7c): 5 for a hostile craft when the attacker holds a non-special weapon; the tractor beam takes only ships without thrust or slowed | inferred | game | `tractor_bm` | [forced-motion](forced-motion.md#tractor-beam), [combat-rules](combat-rules.md#attack-order) |
| 0x4690c0 | Craft_CapSpeed | Caps the speed coefficient and limit by minimum (slot 0xfc) | confirmed | game | nebula 0x44c520 | [nebulae](nebulae.md#what-a-nebula-does-to-a-ship) |
| 0x469120 | Craft_DisableEngines | Takes the engines offline for a time; ignored while +0x678 is set | confirmed | game | — | [forced-motion](forced-motion.md#motion-state) |
| 0x471b30 | Craft_FindSlowEffect | Finds a slow effect on the ship | inferred | game | — | [forced-motion](forced-motion.md#tractor-beam) |

### Units in general (Entity, GameObject)

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x46d790 | Entity_InitHealth | Sets the shield maximum from `maxHealth`; there is no hull | confirmed | game | 0x46d9ef | [combat-rules](combat-rules.md#shields) |
| 0x46e340 | GameObject_RecordHit | Records the time of the last hit | confirmed | game | — | [combat-rules](combat-rules.md#alert-stance) |
| 0x46e640 | GameObject_HitRecently | Whether a hit landed less than 1 s ago | confirmed | game | — | [combat-rules](combat-rules.md#alert-stance) |
| 0x46e670 | GameObject_CloakState | Reads the cloak state (+0x40) | inferred | game | cast at 0x46e79b | [shield-effects](shield-effects.md#shield-down-overlay) |
| 0x46eac0 | GameObject_UpdateTether | Moves the tether point of the alert stance; the stop order it makes is a pending command 2 with a one-point list at the ship's own position (ship+0x10c), which 0x4267a0 hands to 0x426ad0 `case 2` on the next tick and so ends an attack | confirmed | game | — | [combat-rules](combat-rules.md#alert-stance), [forced-motion](forced-motion.md#move-order-arrival-and-hold) |
| 0x40f4d0 | Entity_AsLivingShip | The entity as a ship, or none when it is no ship or is dying | confirmed | game | flag +0xbc & 8, +0x1bf | [forced-motion](forced-motion.md#repulsion-wave-and-shockwave-cannon) |
| 0x450ea0 | Craft_SetVelocity | Writes the velocity, the speed, and its inverse | confirmed | game | — | [forced-motion](forced-motion.md#pull) |
| 0x450f40 | Vector_Normalize | Writes a vector scaled to length 1 | inferred | game | one square root of the squared length | [forced-motion](forced-motion.md#pull) |
| 0x46d5c0 | GameObject_PlaceHeight | Sets a placed object's height; lowers a black hole by `distanceBelowGrid`; a craft whose class has `is_starbase` (+0x235) writes its box into the path planner at 198 | inferred | game | class +0x235 | [forced-motion](forced-motion.md#pull), [path-planning](path-planning.md#obstacles) |
| 0x450f20 | Entity_AsShip | The entity as a ship, dying or not | confirmed | game | flag +0xbc & 8 | [forced-motion](forced-motion.md#pull) |
| 0x471770 | GameObject_UpdateCenter | Computes the world center at +0x12c (slot 0x3c) | confirmed | game | — | [forced-motion](forced-motion.md#motion-state) |
| 0x471640 | GameObject_ClampHeight | Clamps the height to a band around `SHIP_COMBAT_FLOOR` and `SHIP_COMBAT_CEILING` when the flag at +0x488 is set | inferred | game | called by 0x471850 | [forced-motion](forced-motion.md#collision-separation) |
| 0x471850 | GameObject_SetPosition | Sets the position (slot 0xc) | confirmed | game | bounds check 0x471640 | [forced-motion](forced-motion.md#collision-separation) |
| 0x55d080 | Math_MatrixFromAngles | Builds an orientation from pitch, yaw, and roll, plus a position: yaw about the world's up axis after pitch about the side axis after roll about the forward axis | confirmed | game | called by 0x459440 after each integration step, argument order read at the call | [combat-rules](combat-rules.md#coasting), [flight-model](flight-model.md#the-angle-convention) |
| 0x4718e0 | GameObject_SetMatrix | Stores a placed or computed orientation (slot 0x14): copies the matrix, decomposes it into the angles (slot 0x48, 0x458ea0), then updates the world center (slot 0x3c, 0x471770) | confirmed | game | vtable slot 0x14 of the `HoverCraft` vtable at 0x5cbfec | [flight-model](flight-model.md#the-decomposition) |

### Unit AI: alert stance, targets, orders

Range 0x4259d0–0x430400 (`Ai\Tactical\CraftProcess.cpp`). Strategic AI lies before it; see Unexplored.

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x425a20 | CraftProcess_Create | Starts a unit's process; sets the start stance | inferred | game | 0x425bcf, key `defaultGreenAlert` | [combat-rules](combat-rules.md#alert-stance) |
| 0x4266a0 | CraftProcess_Reset | Resets the stance | inferred | game | 0x4266bd | [combat-rules](combat-rules.md#alert-stance) |
| 0x4267a0 | CraftProcess_Tick | The process's per-tick method (slot 0x24): clears a borg ship's acceleration, runs the pending order, and the order dispatch 0x427ff0 unless the ship is derelict | confirmed | game | calls 0x427ff0 through slot 0x38 | [flight-model](flight-model.md#the-tick) |
| 0x428e90 | CraftProcess_AttackState | The player's attack order (state 5): sets the weapon target, then the path move beyond the gap R − 50, the dogfight inside it, the pivot for a target-location weapon | confirmed | game | the 50 at 0x5ca798 | [combat-rules](combat-rules.md#attack-order) |
| 0x4294e0 | CraftProcess_AttackOrder | The AI's attack order (state 7): the same band test on the centre distance, a retreat check first | confirmed | game | — | [combat-rules](combat-rules.md#attack-order) |
| 0x42c8b0 | CraftProcess_SetAttackTarget | Stores the attack maneuver's target (+0x6c) and its circle point | confirmed | game | caller 0x42c400 | [combat-rules](combat-rules.md#attack-order) |
| 0x42a000 | CraftProcess_EnemyNear | Whether an enemy is within 400 units on X and Z | confirmed | game | — | [combat-rules](combat-rules.md#alert-stance) |
| 0x42a170 | CraftProcess_Update | Picks a target each tick and hands it to the weapons; its idle handler holds the tether point at every stance | confirmed | game | calls 0x42e4a0 | [combat-rules](combat-rules.md#alert-stance) |
| 0x428170 | CraftProcess_GoUpdate | The move order's update: posts the stop order once the ship is inside `tooCloseToTurn` of the path's end and slower than half its `impulseSpeed` | confirmed | game | `tooCloseToTurn`; the 0.5 at 0x5ca230 | [forced-motion](forced-motion.md#move-order-arrival-and-hold) |
| 0x428560 | CraftProcess_FollowerGoPath | A group-move follower's GO order built from the leader's path: every point shifted by the follower's offset, the first point the follower's own position | confirmed | game | — | [forced-motion](forced-motion.md#group-move) |
| 0x47fde0 | Selection_GroupHeading | The group move's heading: the plain mean of the selected ships' bearings to the click | confirmed | game | — | [forced-motion](forced-motion.md#group-move) |
| 0x47fe90 | Selection_BuildGroups | Sorts the selected ships into groups by the distance to each group's running mean center (under 800); stations, derelicts, and ships with disabled engines are rejected | confirmed | game | the 800 at 0x5cd740 | [forced-motion](forced-motion.md#group-move) |
| 0x480080 | Group_BuildGrid | Builds a group's grid: its side (the root of the member count, rounded up), the empty cells, and the anchor at the members' smallest X and Z | confirmed | game | — | [forced-motion](forced-motion.md#group-move) |
| 0x480400 | Group_AssignCells | Gives each cell of the grid in turn, row by row, the nearest remaining ship, the earliest on a tie | confirmed | game | — | [forced-motion](forced-motion.md#group-move) |
| 0x4805d0 | Group_IssueOrders | Turns the cells into orders: the leader's move to its cell, a follow order for each other ship | confirmed | game | `FORMATION_SPACING`, `DEFAULT_TO_FORMATION_MOVE` | [forced-motion](forced-motion.md#group-move) |
| 0x480790 | Group_Layout | Lays one group on its origin: builds its grid (0x480080), assigns the cells (0x480400), and issues the orders (0x4805d0) | confirmed | game | — | [forced-motion](forced-motion.md#group-move) |
| 0x480890 | Selection_LayOutGroups | Lays the groups side by side along the perpendicular to the heading, each origin one side × `FORMATION_SPACING` after the last | confirmed | game | — | [forced-motion](forced-motion.md#group-move) |
| 0x480c30 | Selection_GroupCommand | The player's command over the selection: a move of several ships becomes a group move, any other command goes to each ship alone | confirmed | game | callers 0x4ad3d0, 0x4ad5c0, 0x485a10 | [forced-motion](forced-motion.md#group-move) |
| 0x42c400 | CraftProcess_AttackManeuver | The attack maneuver: with the game setting off every call takes the dogfight (mode 8) | confirmed | game | `MORASS_SHIP_LIMIT`, calls 0x42c9d0 | [combat-rules](combat-rules.md#attack-order) |
| 0x42faa0 | Target_IsValid | Whether a unit is a valid target; a derelict is not | confirmed | game | — | [combat-rules](combat-rules.md#target-selection) |
| 0x42fb90 | Target_FindNearest | The nearest valid target in range | confirmed | game | 0x42fd6f | [combat-rules](combat-rules.md#attack-order) |
| 0x42ff80 | Craft_SetWeaponTarget | Hands a target to a unit's weapons | confirmed | game | — | [combat-rules](combat-rules.md#alert-stance) |
| 0x4300a0 | Target_IsValidFor | The target test of target selection | confirmed | game | — | [combat-rules](combat-rules.md#target-selection) |
| 0x436840 | Team_IsAITeam | Whether a team is played by the AI | confirmed | game | getter name `IsAITeam` | [combat-rules](combat-rules.md#alert-stance) |
| 0x4493f0 | Team_Relation | A team's diplomacy value toward another team, from the table at +0x2c indexed by that team | confirmed | game | called by 0x468cd0 | [combat-rules](combat-rules.md#attack-order) |
| 0x449430 | Team_SetRelation | Sets a team's diplomacy value toward another team | confirmed | game | writes the table at +0x2c | [combat-rules](combat-rules.md#attack-order) |
| 0x40a310 | Path_Plan | Plans the path of a move: the goal clamp, the no-path and same-leaf cases, the search, the rubber banding, and the points at the middle of the combat band with the ship first and the goal last | confirmed | game | called by 0x42ded0 | [path-planning](path-planning.md#a-plan-0x40a310), [flight-model](flight-model.md#the-move-order) |
| 0x428081 | CraftProcess_IdleHold | The idle hold: the move maneuver with the ship's own position as point, goal, and carrot and a velocity of 0 | confirmed | game | calls 0x42e4a0 | [flight-model](flight-model.md#the-hold) |
| 0x42c9d0 | CraftProcess_DogfightManeuver | The dogfight: the aim d × (\|d\| − R) beyond R, steering with scale 1, a second yaw servo call toward the target; a target-location ship turns in place at scale 0 | confirmed | game | log text `DogFight_Forces` | [combat-rules](combat-rules.md#attack-order), [flight-model](flight-model.md#the-steering-routine) |
| 0x42ded0 | CraftProcess_PathPoints | The order list's index and plans (a new order, a passed point, the stuck test of 12 seconds), then walks the carrot along the planned path 10 units a pass until it lies the lead distance ahead, and hands it and its velocity to the move maneuver | confirmed | game | calls 0x40a310, 0x434a50, 0x42e4a0 | [flight-model](flight-model.md#the-move-order), [path-planning](path-planning.md#the-order-list-and-the-replans-0x42ded0) |
| 0x434a50 | Path_PointAt | The point at an arc length along a path, moving the carrot's segment forward; the cumulative lengths are built on the first call | confirmed | game | caller 0x42ded0 | [flight-model](flight-model.md#the-move-order) |
| 0x42e4a0 | CraftProcess_MoveManeuver | The move maneuver: swaps in `fastphys.odf`, holds the goal within `tooCloseToTurn` by the brake spring, skips steering for a point inside the turning circle on its side, else steers (0x42f9b0) and lets the spring replace the throttle within 45 degrees | confirmed | game | calls 0x42f9b0, 0x42ebb0, 0x458750, 0x4657b0 | [flight-model](flight-model.md#the-move-maneuver), [forced-motion](forced-motion.md#move-order-arrival-and-hold) |
| 0x4657b0 | Craft_BrakeSpring | The critically damped spring along the forward axis, handed to 0x458990 (slot 0xe0) | confirmed | game | vtable slot 0xe0 | [flight-model](flight-model.md#the-move-maneuver) |
| 0x458990 | Craft_ThrottleFromForce | Turns a wanted force into a throttle by the forward or reverse thrust term, clamped (slot 0xdc) | confirmed | game | vtable slot 0xdc | [flight-model](flight-model.md#the-move-maneuver) |
| 0x458080 | Craft_TurnRadius | The turning radius of the ship's active physics class (slot 0xb0) | confirmed | game | reads class +0x488 through +0x77c | [flight-model](flight-model.md#the-move-maneuver) |
| 0x455230 | HoverClass_SetDefaults | The hover class's defaults of the physics keys (`velocCoeffStill` 0.1, `maxRoll` 30, `turnRadius` 25, and the servo coefficients) | confirmed | game | called by 0x425a20 | [flight-model](flight-model.md#the-coefficient-map) |
| 0x433b30 | Ai_Update | The AI update the frame function calls before the world tick (slot 0x1c of the object at 0x633218) | inferred | game | caller 0x43f2b0 | [flight-model](flight-model.md#the-tick) |
| 0x42ebb0 | CraftProcess_Avoidance | The steering direction of a move: the offset over at least 20, the avoidance, and the height clamp | confirmed | game | calls 0x42ef70 | [flight-model](flight-model.md#the-steering-routine) |
| 0x42ef70 | CraftProcess_AvoidanceForce | The obstacle-avoidance scorer: with `OBSTACLE_AVOIDANCE` on, the neighbour query and filter, the threat test, the ten candidate directions scored against every threat, the hysteresis, and the pick; with it off only the height clamp | confirmed | game | calls 0x40e990, 0x424860, 0x424ba0, 0x4250e0, 0x424b50, 0x425200, 0x42ed20 | [obstacle-avoidance](obstacle-avoidance.md#the-scorer) |
| 0x424860 | CraftProcess_ClosestApproach | The closest approach of the ship's movement to a neighbour: time and squared distance over the relative path | confirmed | game | caller 0x42ef70 | [obstacle-avoidance](obstacle-avoidance.md#the-closest-approach) |
| 0x424ba0 | CraftProcess_AvoidanceCandidates | Builds the ten candidate directions of the wanted one, each with its starting score | confirmed | game | caller 0x42ef70 | [obstacle-avoidance](obstacle-avoidance.md#the-candidates) |
| 0x4250e0 | CraftProcess_AvoidanceScore | Scores the ten candidates against one threat: the penalty where a candidate passes inside the keep-away distance, and the vertical veto against a station | confirmed | game | caller 0x42ef70 | [obstacle-avoidance](obstacle-avoidance.md#the-scoring) |
| 0x424b50 | CraftProcess_AvoidanceHysteresis | Adds 50 times the alignment with the last pick to each candidate that points the same way | confirmed | game | caller 0x42ef70 | [obstacle-avoidance](obstacle-avoidance.md#the-pick) |
| 0x425200 | CraftProcess_AvoidancePick | The best-scoring candidate, the lowest index on a tie, starting from -1e30 | confirmed | game | caller 0x42ef70 | [obstacle-avoidance](obstacle-avoidance.md#the-pick) |
| 0x42ed20 | CraftProcess_FieldHitPrediction | Whether the ship, at its impulse speed along the wanted direction, hits an asteroid field in the look-ahead time with the field ahead and the goal beyond it; sets process byte +0x104 | confirmed | game | calls 0x4353b0 | [obstacle-avoidance](obstacle-avoidance.md#the-asteroid-field-hit-prediction) |
| 0x4353b0 | Collision_SweptSphereHit | The swept-sphere test of two moving spheres over a time, by the quadratic of their relative motion | confirmed | game | callers 0x42ed20, 0x4355b0 | [obstacle-avoidance](obstacle-avoidance.md#the-asteroid-field-hit-prediction) |
| 0x42e3f0 | CraftProcess_FieldHitStop | Consumes the predicted field hit (+0x104): zeroes roll, pitch, and lift, calls the brake spring (slot 0xe0, 0x4657b0) toward the ship's position at velocity 0, sets +0x688 to 2 | confirmed | game | no caller or table entry found, reads +0x104 | [obstacle-avoidance](obstacle-avoidance.md#the-asteroid-field-hit-prediction) |
| 0x40e990 | Grid_Query | The neighbour grid's box query: cell indices of a box widened on each side by the largest radius of the registered objects | confirmed | game | caller 0x42ef70 | [obstacle-avoidance](obstacle-avoidance.md#the-neighbour-query) |
| 0x470940 | Grid_Rebuild | Rebuilds the neighbour grid each frame: every object in one cell by its center, and the largest radius | confirmed | game | caller 0x43f1a0 | [obstacle-avoidance](obstacle-avoidance.md#the-neighbour-query) |
| 0x42f740 | CraftProcess_Steer | Steering: sets the throttle and drives the pitch and yaw servos toward a direction | confirmed | game | — | [flight-model](flight-model.md#the-steering-routine) |
| 0x42f9b0 | CraftProcess_SteerToPoint | Called by the move maneuver; calls 0x42f740 | inferred | game | caller 0x42e4a0 | [flight-model](flight-model.md#the-steering-routine) |
| 0x426ad0 | CraftProcess_AcceptOrder | The order handler: on accepting a move or follow order it draws the process's facing offset (+0x114) once; the attack orders draw none | confirmed | game | `fild` of the draw times π and 0.5 | [flight-model](flight-model.md#the-borg-branch) |
| 0x42e7c0 | CraftProcess_MoveManeuver_Borg | The Borg path of 0x42e4a0 (0x42e7c0–0x42e8cb): inside `tooCloseToTurn` of the goal the 3D spring command, else the steering direction through 0x42f9b0 | confirmed | game | class +0x1ec test | [flight-model](flight-model.md#the-borg-branch) |
| 0x42f7a6 | CraftProcess_Steer_Borg | The Borg path of 0x42f740 (0x42f7a6–0x42f940): the 3D velocity command toward the direction; no servos | confirmed | game | class +0x1ec test | [flight-model](flight-model.md#the-borg-branch) |

### Path planning

Range 0x406c50–0x40e470 (`Ai\Path Planning\a_star_graph_node.cpp`; classes `A_Star_Search`, `A_Star_Graph_Map`, `A_Star_Node`, `A_Star_Graph_Node`), the planner's obstacle writers, and the goal clamp. The rule note is [path-planning](path-planning.md); the planner's 0x40a310 and its caller 0x42ded0 are in "Unit AI" above, the belt's and the nebula's writers in "Map objects".

| Address | Name | Role | Status | Layer | Evidence anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x408e30 | PathQuadtree_Create | Builds the planner: the quadtree over the map plus 500 each side, the finest cell, the four impassable border strips, the graph object | confirmed | game | `HIGHEST_QUADTREE_GRID_RESOLUTION`; callers 0x54ca90, 0x54d1e0 | [path-planning](path-planning.md#the-grid) |
| 0x4093b0 | PathPlanner_Rebuild | Rebuilds the region graph while the dirty flag is set: clear, merge, nodes, edges | confirmed | game | callers 0x431f20, 0x54ca90, 0x54d1e0 | [path-planning](path-planning.md#the-region-graph) |
| 0x409900 | PathPlanner_WriteObstacle | Writes a rectangle grown by a pad into the quadtree in grid coordinates | confirmed | game | callers: the obstacle writers | [path-planning](path-planning.md#the-grid) |
| 0x409a80 | PathPlanner_RemoveObstacle | Writes the default over a rectangle with the same rule; destructors of starbases, wormholes, and construction objects call it | confirmed | game | callers 0x461e70, 0x4542c0, 0x451150 | [path-planning](path-planning.md#obstacles) |
| 0x40c700 | PathQuadtree_WriteRect | Writes a value into the cells a rectangle touches, splitting the cells it covers in part; values only grow, impassable wins | confirmed | game | — | [path-planning](path-planning.md#the-grid) |
| 0x40cee0 | PathQuadtree_PushToChild | Gives a split cell's child the cell's value on the write rule | confirmed | game | caller 0x40c700 | [path-planning](path-planning.md#the-grid) |
| 0x40c5e0 | PathQuadtree_Lookup | The leaf cell that holds a grid point, by descending on the cells' centers | confirmed | game | — | [path-planning](path-planning.md#the-grid) |
| 0x40d280 | PathQuadtree_MergeLeaves | Turns a split cell whose children are leaves of one value into a leaf | confirmed | game | — | [path-planning](path-planning.md#the-region-graph) |
| 0x40cd30 | PathGraph_CreateNodes | The top of the node and edge build, from the root cell | confirmed | game | caller 0x40a310 | [path-planning](path-planning.md#the-region-graph) |
| 0x40dd70 | PathGraph_MakeLeafNode | A node at a leaf's center, and the leaf on the four border lists | confirmed | game | — | [path-planning](path-planning.md#the-region-graph) |
| 0x40d450 | PathGraph_BuildSplit | Builds a split cell's four children, passes up the border lists, and links the facing sides in four walks | confirmed | game | — | [path-planning](path-planning.md#the-region-graph) |
| 0x40dfd0 | PathGraph_LinkSides | The walk along two facing border lists, with its own edge build when the flag is set | confirmed | game | caller 0x40d450 | [path-planning](path-planning.md#the-region-graph) |
| 0x40e320 | PathGraph_LinkLeaves | The edges between two leaves: both ways, only out of an impassable leaf, or both ways at the default × 10000 | confirmed | game | — | [path-planning](path-planning.md#the-region-graph) |
| 0x40e470 | PathGraph_LeafCost | The edge cost between two leaves: the values blended by width times the distance of the centers | confirmed | game | caller 0x40dfd0 | [path-planning](path-planning.md#the-region-graph) |
| 0x4070c0 | PathNode_Create | A graph node at (x, z) with its edge lists | confirmed | game | RTTI `A_Star_Graph_Node` | [path-planning](path-planning.md#the-search) |
| 0x407b10 | PathNode_AddEdge | Appends a directed edge and its cost to a node | confirmed | game | — | [path-planning](path-planning.md#the-region-graph) |
| 0x407990 | PathNode_Expand | Relaxes each neighbour of a node whose mask the search takes | confirmed | game | vtable slot 0x34 | [path-planning](path-planning.md#the-search) |
| 0x408500 | AStar_Run | The search loop: pop the least f, close, expand, until the goal; the route is the parents back | confirmed | game | callers 0x40a310, 0x40ac80 | [path-planning](path-planning.md#the-search) |
| 0x408940 | AStar_Init | A new search id; the start is relaxed from itself, closed, and expanded | confirmed | game | — | [path-planning](path-planning.md#the-search) |
| 0x408660 | AStar_Relax | First visit, reopening a closed node, and the rule that an open node is never improved | confirmed | game | — | [path-planning](path-planning.md#the-search) |
| 0x408d50 | AStar_AdjustHeap | The heap's pop: the hole down the smaller child, the value back up on the live f | confirmed | game | caller 0x408500 | [path-planning](path-planning.md#the-search) |
| 0x40a950 | Path_GridCheck | Converts a world point to grid coordinates, puts it into the grid, and says whether it lay inside | confirmed | game | caller 0x40a310 | [path-planning](path-planning.md#a-plan-0x40a310) |
| 0x40ac80 | Path_PortalGraph | The rubber banding's graph through the route's cells, searched for one end per border | confirmed | game | `PERFORM_RUBBER_BANDING` | [path-planning](path-planning.md#a-plan-0x40a310) |
| 0x40aa20 | Path_Portal | The two ends of the border between two cells, ordered by the direction of travel, and the direction case | confirmed | game | — | [path-planning](path-planning.md#a-plan-0x40a310) |
| 0x40b8c0 | Path_Funnel | The funnel that pulls the portal route straight | confirmed | game | — | [path-planning](path-planning.md#a-plan-0x40a310) |
| 0x40b490 | Path_SideTest | The side of a ray a point lies on | confirmed | game | caller 0x40b8c0 | [path-planning](path-planning.md#a-plan-0x40a310) |
| 0x40b4f0 | Path_OrderPortalEnds | Orders a border's two ends into the funnel's side A and side B as seen from the anchor | confirmed | game | caller 0x40b8c0 | [path-planning](path-planning.md#a-plan-0x40a310) |
| 0x40b680 | Path_NarrowFunnel | Narrows the funnel's two sides to a border; fails where they would cross | confirmed | game | caller 0x40b8c0 | [path-planning](path-planning.md#a-plan-0x40a310) |
| 0x44ab30 | Map_ClampGoal | Puts a goal's X and Z at least `MAP_EDGE_BUFFER` inside the map, and the high side `MAP_EDGE_EPSILON` further | confirmed | game | `MAP_EDGE_BUFFER`, `MAP_EDGE_EPSILON` | [path-planning](path-planning.md#a-plan-0x40a310) |
| 0x44a810 | Map_DefaultBounds | The default of the map's six bounds floats | confirmed | game | — | [path-planning](path-planning.md#the-grid) |
| 0x450810 | BlackHole_WritePathCost | Writes the black hole's square, padded by the safety coefficient times the class's +0x1ec (its `EventRadius`) | confirmed | game | `BLACKHOLE_PATHING_COST`, `BLACKHOLE_PATHING_SAFETY_COEFFICIENT` | [path-planning](path-planning.md#obstacles) |
| 0x4af590 | Path_PointAtParameter | The point of a map path at a parameter: the Hermite curve between the integer part's two points | confirmed | game | caller 0x434c30 | [path-planning](path-planning.md#obstacles) |
| 0x4af2c0 | Path_Hermite | The cubic Hermite blend between two path points with chord tangents of the segment's length | confirmed | game | caller 0x4af590 | [path-planning](path-planning.md#obstacles) |
| 0x428d20 | CraftProcess_DirectTarget | A direct target farther than the replan distance becomes a path move; the order list is rebuilt only when the target moved far | confirmed | game | calls 0x42ded0; setting 0x632ec4 | [path-planning](path-planning.md#the-order-list-and-the-replans-0x42ded0) |
| 0x4f82f0 | Config_Primary | The expression evaluator's primary: a number, an int, or an identifier looked up among the earlier declarations (0x4fc2e0, 0x4fc1d0, 0x4fc0d0) | confirmed | game | log text `Config or AIP float parameter` | [path-planning](path-planning.md#the-keys) |
| 0x4fc1d0 | Config_FloatParam | A float key by name; only a `float` or `double` declaration is read | confirmed | game | callers 0x4453f0 | [path-planning](path-planning.md#the-keys) |
| 0x4fc0d0 | Config_IntParam | An int key by name; only an `int` declaration is read | confirmed | game | callers 0x4453f0 | [path-planning](path-planning.md#the-keys) |

### Weapons and firing

Weapon definitions 0x5030d0–0x505d00, weapons 0x506080–0x50c810 (`Weapon\`).

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x506120 | AreaEffectCannon_FireDelay | Reads `crewAffectsWeaponDelay` in a reload delay | inferred | game | 0x506452 | [combat-rules](combat-rules.md#firing) |
| 0x506d70 | Craft_StopWeapons | Stops every weapon of a dying unit | confirmed | game | — | [combat-rules](combat-rules.md#dying-units) |
| 0x506e30 | WeaponCarrier_Add | Adds a weapon to the carrier: the range limits of the non-special weapons (+0x1c, +0x20) and of all (+0x24, +0x28), their counts, and the `targetLocation` and `restrictFireArc` flags (+0x34, +0x35) | confirmed | game | caller 0x506ef0 | [combat-rules](combat-rules.md#attack-order) |
| 0x508750 | ReflectWeapon_Apply | The reflector special | inferred | game | 0x50883c | [shield-effects](shield-effects.md#other-effects-of-the-same-object) |
| 0x509bd0 | Weapon_Update | Fires when the reload delay passed and the unit holds a target | confirmed | game | — | [combat-rules](combat-rules.md#firing) |
| 0x50a170 | Weapon_InFireArc | The `restrictFireArc` test | confirmed | game | — | [combat-rules](combat-rules.md#firing) |
| 0x50a290 | Weapon_DistanceToTarget | Distance from the hardpoint to the target | confirmed | game | — | [combat-rules](combat-rules.md#firing) |
| 0x50a3b0 | Weapon_RollToHit | The to-hit roll when the weapon fires | confirmed | game | — | [combat-rules](combat-rules.md#to-hit) |
| 0x50a520 | Weapon_InRange | Whether the target is in range | confirmed | game | — | [combat-rules](combat-rules.md#firing) |
| 0x50a7c0 | Weapon_AimHit | Aims a shot that hits | confirmed | game | — | [combat-rules](combat-rules.md#to-hit) |
| 0x50aab0 | Weapon_AimMiss | Aims a missed shot: leads the target and offsets the aim | confirmed | game | — | [combat-rules](combat-rules.md#to-hit) |
| 0x50ae40 | Weapon_MakeShot | Makes the shot: its damage and hit location (0x4ee710) and its hit node (0x468a60) | confirmed | game | — | [combat-rules](combat-rules.md#hit-explosion) |
| 0x50b690 | Weapon_ReloadDelay | The reload delay, with `crewAffectsWeaponDelay` | confirmed | game | — | [combat-rules](combat-rules.md#firing) |
| 0x50b7c0 | Weapon_PlayFireSound | Plays the weapon's fire sound at a minimum scale of 1 | confirmed | game | — | [sound-fade](sound-fade.md#a-sounds-distances) |

### Ordnance: shots, beams, torpedoes, mines

Definitions 0x4e9410–0x4eab30, ordnance 0x4ebb10–0x4ef950 (`Ordnance\`).

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x4d92e0 | Ordnance_SetHitNode | Sets the shot's hit node (ordnance +0x110, slot 0x64) | confirmed | game | — | [combat-rules](combat-rules.md#hit-explosion) |
| 0x4e70f0 | Ordnance_RollDamage | `damageBase` plus a random part up to `damageVariance` either way | confirmed | game | 0x4e7158 | [combat-rules](combat-rules.md#damage) |
| 0x4eb170 | Beam_Update | A beam deals its damage once, when its time ends | inferred | game | 0x4eb47e | [combat-rules](combat-rules.md#shots) |
| 0x4ec100 | Ordnance_HitShields | Whether a hit meets the shields (`ignoreShield`) | inferred | game | 0x4ec27a | [combat-rules](combat-rules.md#shots) |
| 0x4ec880 | Mine_Update | A mine's tick; spawns the shield effect on a hit | inferred | game | `Mine.cpp`; 0x4eca69 | [shield-effects](shield-effects.md#when-it-spawns) |
| 0x4ed150 | Missile_Update | Steers a torpedo by `omegaTurn`, `seekTime`; ends at `lifeSpan` | confirmed | game | `Missile.cpp` | [combat-rules](combat-rules.md#shots) |
| 0x4ee5f0 | Ordnance_ShooterDying | Ends ordnance whose shooter is dying | inferred | game | — | [combat-rules](combat-rules.md#shots) |
| 0x4ee620 | Ordnance_TargetDying | Lets shots in flight at a dying unit land | confirmed | game | — | [combat-rules](combat-rules.md#dying-units) |
| 0x4ee710 | Ordnance_RollHit | Rolls the shot's damage and, on a ship, its hit location when the shot is made (slot 0x58) | confirmed | game | 0x464dd0 | [combat-rules](combat-rules.md#hit-explosion) |
| 0x4ee7f0 | Ordnance_DropShooterBonus | A dying shooter's torpedoes fly on without its bonus; hands the hit and its node to the target (slot 0x90 → 0x465100) | confirmed | game | — | [combat-rules](combat-rules.md#dying-units) |
| 0x4ee860 | Ordnance_HitTest | Maps a shot's segment into the unit and tests the shield ellipsoid | confirmed | game | — | [shield-effects](shield-effects.md#contact) |
| 0x4ddd30 | HoldingBeam_Hold | Freezes the target at the transform of the first hit | confirmed | game | — | [forced-motion](forced-motion.md#holding-beam) |
| 0x4ddf80 | HoldingBeam_Update | Drains special energy and holds the target each tick | confirmed | game | — | [forced-motion](forced-motion.md#holding-beam) |
| 0x4e42f0 | RepulsionFieldClass_Load | Reads `repulsionForce` | confirmed | game | — | [forced-motion](forced-motion.md#repulsion-field) |
| 0x4e44a0 | RepulsionField_Hit | Disables the engines and replaces the target's velocity | confirmed | game | — | [forced-motion](forced-motion.md#repulsion-field) |
| 0x4e7b00 | TractorBeamClass_Load | Reads `targetRange`, `targetSpeed`, `omegaType` | confirmed | game | — | [forced-motion](forced-motion.md#tractor-beam) |
| 0x4e7df0 | TractorBeam_Pull | Replaces the target's velocity with a pull toward the shooter beyond `targetRange` | confirmed | game | 0x4e7e97–0x4e7f1a | [forced-motion](forced-motion.md#tractor-beam) |
| 0x4e7fb0 | TractorBeam_Update | Checks the beam each tick and pulls | confirmed | game | — | [forced-motion](forced-motion.md#tractor-beam) |
| 0x4ed9a0 | Ordnance_UpdatePass | Calls slot 0x44 on every ordnance after the tick | confirmed | game | — | [forced-motion](forced-motion.md#tick-order) |

### Special weapons and timed effects

Specials 0x4d9010–0x4e9350, one definition class and one class per special. Timed effects on a unit
0x55ef00–0x563a00 (`SpecialObjectEffect` and its `Timed…` subclasses). Special flags 0x564100–0x5648b0
(`SpecialFlag\SpecialFlag.cpp`).

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x4dc9c0 | EnergyShieldConverter_Apply | The Energy Shield Converter's effect on shields | inferred | game | `wshldadd`; 0x4dca33 | [shield-effects](shield-effects.md#other-effects-of-the-same-object) |
| 0x4dfe00 | ManheimEffect_Apply | The Manheim effect, 2.0 s | inferred | game | `wmanhiem`; 0x4dffd1 | [shield-effects](shield-effects.md#other-effects-of-the-same-object) |
| 0x4e5da0 | ShieldInversion_Apply | The Shield Inversion's effect on shields | inferred | game | `wshldsub`; 0x4e5e68 | [shield-effects](shield-effects.md#other-effects-of-the-same-object) |
| 0x5623f0 | ShieldRemodulationEffect_Apply | The Shield Remodulation effect | inferred | game | `wshldrmd`; 0x5624c0 | [shield-effects](shield-effects.md#other-effects-of-the-same-object) |
| 0x4dd720 | GravityMine_Hit | Lowers the warp speed multiplier by `slowPercent` | confirmed | game | — | [forced-motion](forced-motion.md#what-does-not-move-a-ship) |
| 0x4e4a80 | RepulsionWave_Apply | Pushes a ship and disables its engines; no reference found | unknown | game | — | [forced-motion](forced-motion.md#repulsion-wave-and-shockwave-cannon) |
| 0x4e5080 | RepulsionWave_Fire | Starts the shooter's dying phase and spawns the shockwave `wrwave` | confirmed | game | — | [forced-motion](forced-motion.md#repulsion-wave-and-shockwave-cannon) |
| 0x508fc0 | SelfDestruct_Update | Counts down, sets the explosion size, and starts dying | confirmed | game | — | [forced-motion](forced-motion.md#what-does-not-move-a-ship) |
| 0x560a60 | SlowEffect_Init | The slow effect of a gravity mine | inferred | game | — | [forced-motion](forced-motion.md#what-does-not-move-a-ship) |
| 0x561290 | TimedDisable_Init | A timed disable; type 2 takes the engines offline (0x469120) | confirmed | game | — | [forced-motion](forced-motion.md#motion-state) |

### Map objects

Range 0x44afc0–0x4548d0, with the nebula grid (0x43e880–0x43eef3) and the map's path lookup (0x4348d0, 0x434c30) that
belong to it. The black hole, the nebula, and the asteroid field's belt are surveyed; see Unexplored.

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x434c30 | AsteroidBelt_WritePathCost | Writes the path-cost squares along a belt's path (type 3), sampled in steps of 0.01 of the path parameter | confirmed | game | `ASTEROID_BELT_PATHING_DISTANCE` | [nebulae](nebulae.md#asteroid-belts), [path-planning](path-planning.md#obstacles) |
| 0x4348d0 | AiPath_FindByLabel | Finds the map's path with a label | confirmed | game | — | [nebulae](nebulae.md#asteroid-belts) |
| 0x43e880 | NebulaGrid_Create | Builds the nebula grid over the map bounds, 50-unit cells | confirmed | game | 0x6343f0–0x634404 | [nebulae](nebulae.md#the-grid) |
| 0x43ea20 | NebulaGrid_Mark | Adds or removes one nebula's count in the cells whose center lies in its sphere | confirmed | game | — | [nebulae](nebulae.md#marking-the-cells) |
| 0x43edc0 | NebulaGrid_ShipPass | Gives each craft the effect of every nebula kind its cell holds, before the area pass | confirmed | game | calls 0x44c520 | [nebulae](nebulae.md#the-nebula-pass) |
| 0x44abb0 | MapBounds_Load | Reads the six bounds floats of a map and its rescale flag | confirmed | game | — | [nebulae](nebulae.md#the-grid) |
| 0x44b7d0 | BlackHoleClass_Load | Reads `GravityRadius`, `EventRadius`, `GravityForce`, `Rotation`, `ScaleSize` | confirmed | game | — | [forced-motion](forced-motion.md#black-hole) |
| 0x44bf20 | NebulaKind_ClassOf | The class named `mnebula<kind>` whose values a kind uses (cached) | confirmed | game | string table 0x5fd908 | [nebulae](nebulae.md#kinds-and-their-classes) |
| 0x44bfa0 | NebulaClass_Load | Reads the nebula keys, sets the kind from the class name (compared by case), and defaults `pathing_cost` to `CANNOT_PATH_INTO_COST` less 1 (0x44c2f4, 0x44c301) | confirmed | game | `craft_speed_coefficient` | [nebulae](nebulae.md#kinds-and-their-classes) |
| 0x44c520 | Nebula_ApplyToShip | Caps a ship's speed and applies the nebula's damage and disables | confirmed | game | +0x1b1 and +0xbc tests | [nebulae](nebulae.md#what-a-nebula-does-to-a-ship) |
| 0x44e7b0 | AsteroidField_WritePathCost | A field without a belt path writes its path-cost squares; a path not of type 3 is dropped | confirmed | game | — | [nebulae](nebulae.md#asteroid-belts) |
| 0x44f8e0 | AsteroidField_LoadBelt | Reads the field's belt record: `have_belt`, offsets, and the path label | confirmed | game | — | [nebulae](nebulae.md#asteroid-belts) |
| 0x44f9b0 | AsteroidField_PostLoad | Looks the stored path label up and writes the field's path costs | confirmed | game | calls 0x4348d0 | [nebulae](nebulae.md#asteroid-belts) |
| 0x451e00 | Nebula_Place | Marks the nebula's cells with 0x43ea20 at placement | confirmed | game | calls 0x43ea20 | [nebulae](nebulae.md#marking-the-cells) |
| 0x44da40 | AreaEffectObject_AreaTick | Calls slot 0xc8 for every living ship within `EventRadius` (slot 0x8c) | confirmed | game | — | [forced-motion](forced-motion.md#event-horizon) |
| 0x44db30 | AreaEffectObject_GatherShips | Collects the living ships within a plane radius | confirmed | game | +0x1bf test | [forced-motion](forced-motion.md#event-horizon) |
| 0x450950 | BlackHole_Simulate | Turns the model and pulls ships (slot 0x84) | confirmed | game | — | [forced-motion](forced-motion.md#pull) |
| 0x450a40 | BlackHole_Swallow | Sets the black-hole death and starts dying (slot 0xc8) | confirmed | game | — | [forced-motion](forced-motion.md#event-horizon) |
| 0x450a80 | BlackHole_PullShips | Adds the pull to ships without thrust within `GravityRadius` | confirmed | game | 0x450bcc | [forced-motion](forced-motion.md#pull) |
| 0x451870 | IonStorm_Touch | Damages units that touch the storm | inferred | game | — | [forced-motion](forced-motion.md#what-does-not-move-a-ship) |

### Explosions and shockwaves

Range 0x51a400–0x51e000 (`Explosion`, `ShockwaveCannon`, `ShockwaveExplosion`).

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x51a490 | Explosion_PlaySound | Plays the explosion's sound at a minimum scale of 2 | confirmed | game | 0x5d2b4c | [sound-fade](sound-fade.md#a-sounds-distances) |
| 0x51c4d0 | ShockwaveCannon_Update | Grows the wave front and finds the ships in its arc | confirmed | game | 0x51c51e | [forced-motion](forced-motion.md#repulsion-wave-and-shockwave-cannon) |
| 0x51c8d0 | ShockwaveCannon_HitShip | Damages a ship, disables its engines, and replaces its velocity | confirmed | game | 0x51c987–0x51ca32 | [forced-motion](forced-motion.md#repulsion-wave-and-shockwave-cannon) |
| 0x51cb90 | ShockwaveCannonClass_Load | Reads `waveArc`, `disableTime`, `repulsionForce` | confirmed | game | — | [forced-motion](forced-motion.md#repulsion-wave-and-shockwave-cannon) |
| 0x51d2a0 | ShockwaveExplosion_Update | Damages units in the radius; pushes nothing | confirmed | game | — | [forced-motion](forced-motion.md#what-does-not-move-a-ship) |
| 0x51d570 | Shockwave_End | Fades the wave and ends it past its full radius | confirmed | game | — | [forced-motion](forced-motion.md#repulsion-wave-and-shockwave-cannon) |

### Effects at nodes: sparks and system explosions

Range 0x514940–0x516200.

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x515d50 | NodeEffect_Queue | Queues a sprite effect at a model node (queue 0x67f57c) | confirmed | game | `xspark`, `systemexpl1` | [combat-rules](combat-rules.md#repair-sparks) |
| 0x515f40 | NodeEffect_Age | Ages the queued effects; removes one whose age passed its duration | confirmed | game | — | [combat-rules](combat-rules.md#repair-sparks) |
| 0x515fb0 | NodeEffect_Draw | Draws the queued effects at their nodes' world positions | confirmed | game | — | [combat-rules](combat-rules.md#repair-sparks) |

### Shield effect

Range 0x5169a0–0x517070 (`ShieldEffect`).

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x516540 | ShieldEffect_Spawn | Spawns the shield hit effect or the shield-down overlay | confirmed | game | — | [shield-effects](shield-effects.md#the-effect-object) |
| 0x5169a0 | ShieldEffect_LoadGeometry | Loads one of the seven shield models | confirmed | game | names 0x5d2a80 | [shield-effects](shield-effects.md#the-effect-object) |
| 0x516b80 | ShieldEffect_DefaultGeometry | `xshldx01` for a missing key | confirmed | game | — | [shield-effects](shield-effects.md#model-and-duration) |
| 0x516b90 | ShieldEffect_FindGeometry | Matches `shieldGeometryName` against the seven names | confirmed | game | — | [shield-effects](shield-effects.md#model-and-duration) |
| 0x516c40 | ShieldEffect_MoveContact | Turns the effect to a new contact point | confirmed | game | — | [shield-effects](shield-effects.md#when-it-spawns) |
| 0x516fa0 | ShieldEffect_Update | Ends the effect after its duration; −1 keeps it | confirmed | game | — | [shield-effects](shield-effects.md#the-effect-object) |
| 0x5170e0 | ShieldEffect_Color | The effect's color, white without a team | confirmed | game | 0x690b60 | [shield-effects](shield-effects.md#the-effect-object) |
| 0x55d160 | Matrix_YawPitchRoll | Rotation from pitch, yaw, roll: roll about Z, then pitch about X, then yaw about Y | confirmed | game | — | [combat-rules](combat-rules.md#damage-emitters) |
| 0x55d840 | Matrix_FromDirection | Builds a transform from a direction | inferred | game | — | [shield-effects](shield-effects.md#placement-and-size) |

### Interface: selection display

Range 0x499ab0–0x49b9c0 (`SelectionDisplay`). The rest of the interface, 0x47ea90–0x4b0280, is unexplored.

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x499ac0 | SelectionDisplay_LoadSprites | Loads the display's sprites by name | confirmed | game | key `selectSystem` | [selection-display](selection-display.md#loaded-sprites) |
| 0x499f70 | SelectionDisplay_Update | Updates the hovered unit, group key, and fade once per frame | confirmed | game | — | [selection-display](selection-display.md#which-units-show-it) |
| 0x49a3f0 | SelectionDisplay_ScreenBox | The unit's screen box | confirmed | game | — | [selection-display](selection-display.md#the-units-screen-box) |
| 0x49a4a0 | SelectionDisplay_ProjectCorners | Projects the box corners | unknown | game | — | [selection-display](selection-display.md#the-units-screen-box) |
| 0x49abf0 | SelectionDisplay_DrawShieldBar | Draws with `select_shield_bar` | confirmed | game | — | [selection-display](selection-display.md#loaded-sprites) |
| 0x49ad50 | SelectionDisplay_DrawPart | Draws one part of the display | confirmed | game | — | [selection-display](selection-display.md#which-units-show-it) |
| 0x49afe0 | SelectionDisplay_DrawCrewDots | Draws with `select_crew_dot` | confirmed | game | — | [selection-display](selection-display.md#loaded-sprites) |
| 0x49b6a0 | SelectionDisplay_Fade | The display fade | confirmed | game | — | [selection-display](selection-display.md#which-units-show-it) |
| 0x4ab5d0 | OverView_Deselect | Deselects a dying unit | confirmed | game | — | [combat-rules](combat-rules.md#dying-units) |
| 0x4ab8c0 | OverView_CanSelect | Refuses to select a dying unit | confirmed | game | 0x4ab91e | [combat-rules](combat-rules.md#dying-units) |

### Storm3D: models, nodes, and the scene walk

Storm3D (`ST3D_…` classes) runs about 0x5746d0–0x5a6200: nodes, meshes, sprites, lights, cameras, the Direct3D 7
and software devices, textures, and the SOD and sprite table readers.

Node flags at node +0x58: bit 0 skips the node's own drawing and its emitter step; bit 1 skips its children.
Node +0x1c is the first child and +0x20 the next sibling.

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x57b3f0 | Camera_FrustumTest | Whether an instance is inside the view | confirmed | game | — | — |
| 0x57cc40 | ChunkManager_Get | The scene's one chunk manager | inferred | game | string `chunk_db`; class `ST3D_ChunkManager` | [debris](debris.md#the-shatter) |
| 0x57cd90 | ChunkManager_Update | Moves, turns, and ages every debris piece each frame; removes the dead | confirmed | game | — | [debris](debris.md#the-update) |
| 0x57ceb0 | ChunkManager_Draw | Draws the debris pieces | inferred | game | calls 0x57dc80 | [debris](debris.md#the-draw) |
| 0x57d270 | Debris_CreatePiece | Makes one piece from a face: its shape, its start matrix, and its random life, direction, speed, and tumble | confirmed | game | called by 0x57cf90 | [debris](debris.md#the-draws-and-their-order) |
| 0x57cee0 | Node_MeshWalk | Walks the meshes of a node tree, honouring the flags; not the render walk | confirmed | game | casts to `ST3D_Mesh` | [debris](debris.md#the-walk) |
| 0x57ff30 | Model_FindNode | Finds a model node by whole name, ignoring case | confirmed | game | — | [combat-rules](combat-rules.md#damage-nodes) |
| 0x580110 | Node_DrawWalk | The render walk: draws a node and steps its emitter unless bit 0 is set; descends unless bit 1 is set | confirmed | game | node vtable +0x10, +0x2c | [combat-rules](combat-rules.md#damage-emitters) |
| 0x580510 | Node_NameEquals | Compares a node's name, ignoring case | confirmed | game | `_stricmp` | [combat-rules](combat-rules.md#damage-nodes) |
| 0x58a790 | Model_LoadSod | A step of the model load toward the SOD reader | confirmed | game | — | [combat-rules](combat-rules.md#model-scale) |
| 0x58a960 | Model_BindEmitter | Binds an emitter node to its sprite table block | inferred | game | — | [combat-rules](combat-rules.md#damage-emitters) |
| 0x58b4c0 | Instance_FrameSeconds | The seconds since the instance's last frame (+0x98 − +0x9c) | confirmed | game | — | [combat-rules](combat-rules.md#damage-emitters) |
| 0x58b4f0 | Instance_UpdateParticles | Moves and ages the instance's particles; removes dead ones | confirmed | game | — | [combat-rules](combat-rules.md#damage-emitters) |
| 0x58b590 | Instance_DrawParticles | Draws the instance's particles | confirmed | game | — | [combat-rules](combat-rules.md#damage-emitters) |
| 0x58cb60 | Instance_Draw | Draws an instance: its particles, then the node walk | confirmed | game | 0x58cc3e | [shield-effects](shield-effects.md#flipbook) |
| 0x58d0e0 | Instance_SetRadius | Sets the instance radius from the model | confirmed | game | 0x58d190 | [shield-effects](shield-effects.md#placement-and-size) |
| 0x5910a0 | Mesh_Scale | Scales a mesh on all three axes | confirmed | game | — | [combat-rules](combat-rules.md#model-scale) |
| 0x595ef0 | Sod_IsMeshNode | Tests a SOD node's type for a mesh | confirmed | game | — | [combat-rules](combat-rules.md#model-scale) |
| 0x5967f0 | Model_PointBox | The per-axis box of the points the model load collects | confirmed | game | — | [shield-effects](shield-effects.md#shield-ellipsoid) |
| 0x598f90 | SpriteNode_FindByName | Finds a sprite node by name up to the underscore, ignoring case | confirmed | game | — | [shield-effects](shield-effects.md#shield-ellipsoid) |
| 0x599000 | SpriteNode_Corners | A sprite node's corner points | confirmed | game | — | [shield-effects](shield-effects.md#shield-ellipsoid) |
| 0x599380 | SpriteNode_Size | A sprite's width and height in the node's XY plane | confirmed | game | — | [shield-effects](shield-effects.md#shield-ellipsoid) |
| 0x59b650 | Sod_ReadNode | Reads one SOD node | confirmed | game | — | [combat-rules](combat-rules.md#damage-emitters) |
| 0x59c130 | Sod_ReadModel | Reads a SOD model | confirmed | game | — | [combat-rules](combat-rules.md#model-scale) |
| 0x580040 | Model_AllPoints | Collects every mesh vertex and sprite corner | confirmed | game | — | [shield-effects](shield-effects.md#shield-ellipsoid) |
| 0x57cf90 | Debris_SpawnPieces | Makes one debris piece per mesh face; ignores its velocity argument | confirmed | game | no read of [ebp+0x20] | [debris](debris.md#density-and-cap) |
| 0x58cef0 | SceneObject_Shatter | Breaks a unit's model into debris pieces (`this` is the unit) | confirmed | game | called by 0x4663d0 | [debris](debris.md#the-shatter) |

### Storm3D: particles and emitters

Range 0x5746d0–0x575c00 (`ST3D_ParticleEmitter` and its `_Spray`, `_Cloud`, `_Beam` subclasses; particle
classes `SprayParticle`, `RandomParticle`, `StraightParticle`).

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x501820 | Random_Signed | A random decimal, uniform in −1..1 | confirmed | game | instructions; the pseudocode is wrong | [combat-rules](combat-rules.md#damage-emitters) |
| 0x501870 | Random_Scaled | The same draw times its argument times 1e-5 | confirmed | game | — | — |
| 0x5746d0 | Particle_Init | Places a new particle at the emitter node, turned by three random angles within the spread | confirmed | game | — | [combat-rules](combat-rules.md#damage-emitters) |
| 0x5753a0 | SprayEmitter_Create | Creates a spray emitter; its timer starts at the period | confirmed | game | `ST3D_ParticleEmitter_Spray` vtable 0x5d39ac | [combat-rules](combat-rules.md#damage-emitters) |
| 0x575400 | SprayEmitter_Step | Counts the timer down; below 0 spawns a particle and resets the timer | confirmed | game | vtable slot 13 | [combat-rules](combat-rules.md#damage-emitters) |
| 0x5754c0 | SprayEmitter_Clone | Copies a spray emitter for a model instance | confirmed | game | vtable slot 12 | [combat-rules](combat-rules.md#damage-emitters) |
| 0x5756e0 | RandomParticle_Update | A cloud particle dies at its lifespan or beyond the cloud radius | confirmed | game | `RandomParticle` vtable | — |
| 0x5757b0 | SprayParticle_Update | Moves a spray particle at its velocity | confirmed | game | `SprayParticle` vtable 0x5d3a04 | [combat-rules](combat-rules.md#damage-emitters) |
| 0x575840 | ParticleEmitter_Init | The base emitter setup; the emitter starts active (+0x90) | confirmed | game | — | — |
| 0x5758e0 | ParticleEmitter_Step | Steps the emitter with the instance's frame seconds | confirmed | game | vtable slot 11 of every emitter | [combat-rules](combat-rules.md#damage-emitters) |
| 0x575950 | Particle_Create | Creates a particle by kind: 0 cloud, 1 spray, 2 beam | confirmed | game | — | [combat-rules](combat-rules.md#damage-emitters) |
| 0x584040 | DeviceDX7_DrawSprite | Draws a sprite; a size at or below 0 draws nothing | confirmed | game | — | [combat-rules](combat-rules.md#damage-emitters) |
| 0x599ab0 | SpriteTable_ParseEmitter | Reads an `@emitter` block; turns degrees into radians | confirmed | game | keys `spread_angle`, `firing_period` | [combat-rules](combat-rules.md#damage-emitters) |

### Storm3D: textures, animation, and drawing

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x576220 | AnimationChannel_Frame | The frame of a flipbook at a time | confirmed | game | — | [shield-effects](shield-effects.md#flipbook) |
| 0x577350 | AnimationChannel_Copy | Copies a channel without its mode | confirmed | game | — | [shield-effects](shield-effects.md#flipbook) |
| 0x580a00 | Device_DrawNoDepth | Draws without depth writes | confirmed | game | 0x580a35 | [shield-effects](shield-effects.md#drawing) |
| 0x589070 | Material_Replace | Replaces a node's lighting material | confirmed | game | 0x5891ef | [shield-effects](shield-effects.md#drawing) |
| 0x58fd80 | Mesh_SetLightingMaterial | Sets a mesh's lighting material | confirmed | game | — | [shield-effects](shield-effects.md#drawing) |
| 0x59a5b0 | Texture_FlipbookCells | The cells of a flipbook texture | confirmed | game | — | [shield-effects](shield-effects.md#flipbook) |
| 0x59c730 | Texture_Upload | Uploads a texture; reads the file's origin bit | confirmed | game | — | [shield-effects](shield-effects.md#flipbook) |
| 0x59f050 | Tga_Origin | A TGA file's origin bit | confirmed | game | — | [shield-effects](shield-effects.md#flipbook) |
| 0x59f320 | Texture_ColorKey | The color-key path of a texture load | unknown | game | — | [shield-effects](shield-effects.md#drawing) |
| 0x59fa90 | Texture_LoadForDevice | Loads `Textures\RGB` for a 16- or 32-bit device | confirmed | game | — | [shield-effects](shield-effects.md#drawing) |
| 0x5a39a0 | Mesh_FlipbookUv | Adds a flipbook cell's UV offset | confirmed | game | 0x5a3a65 | [shield-effects](shield-effects.md#flipbook) |
| 0x5a4200 | Mesh_VertexColor | Sets each vertex's color | confirmed | game | — | [shield-effects](shield-effects.md#drawing) |
| 0x5a62c0 | Workspace_CullFront | Face culling of a draw | confirmed | game | — | [shield-effects](shield-effects.md#drawing) |
| 0x5a6360 | Workspace_CullBack | Face culling of a draw | confirmed | game | — | [shield-effects](shield-effects.md#drawing) |

### Audio

Range 0x5abbc0–0x5b6180 (`Utilities\Audio\`). The rest of the range is in Unexplored.

| Address | Name | Role | Status | Layer | Anchor | Rule note |
|---|---|---|---|---|---|---|
| 0x5abbc0 | Audio_ReadSettings | Reads `audio.odf`: the rolloff and the minimum and maximum 3D distances, among others | confirmed | game | 0x62c284–0x62c290 | [sound-fade](sound-fade.md#the-settings) |
| 0x5abed0 | Audio_Update | Hands the camera's pose to the listener each frame | confirmed | game | — | [sound-fade](sound-fade.md#the-listener) |
| 0x5acb60 | Audio_SetListener | Sets DirectSound's listener position and orientation from a pose | confirmed | game | — | [sound-fade](sound-fade.md#the-listener) |
| 0x5ad9c0 | Sound_CreateBuffer | Creates a DirectSound buffer with the flags it is given | confirmed | game | caller 0x5aff40 | [sound-fade](sound-fade.md#following-and-the-maximum-distance) |
| 0x5aff40 | Sound3D_Create | Makes a 3D sound; its minimum and maximum distance are the settings times its two scales | confirmed | game | — | [sound-fade](sound-fade.md#a-sounds-distances) |
| 0x5b09e0 | Sound3D_Follow | Moves a playing 3D sound to its object's position | confirmed | game | — | [sound-fade](sound-fade.md#following-and-the-maximum-distance) |
| 0x5b0b90 | Sound3D_Play | Starts a 3D sound; refuses one beyond its maximum distance from the listener | confirmed | game | — | [sound-fade](sound-fade.md#following-and-the-maximum-distance) |

## Structures and globals

Offsets are from the start of the object. A ship is a `Craft`; its definition is a `CraftClass`.

### Ship (`Craft`)

| Offset | Field | Type | Evidence |
|---|---|---|---|
| +0x60 | Model bounding radius, from model +0x24 (0x4510a0) | float | spark and explosion size (0x4683d0, 0x465100) |
| +0x68 | Unit matrix | matrix | 0x57d270 |
| +0x80 | Forward axis | vector | 0x464620 |
| +0x8c | Position | vector | 0x459f2e, 0x471850 |
| +0x90 | Height (the position's y) | float | 0x464bb7 |
| +0xd8 | Gate of the collision pass when 0; meaning unknown | | 0x464910 |
| +0x1b1 | Phased | byte | 0x464910 |
| +0xa0 | Current model | pointer | 0x4677b0 |
| +0xe0 | Definition (`CraftClass`) | pointer | 0x4677b0 |
| +0x12c | World center: position plus the rotated local center | vector | 0x471770 |
| +0x138 | Speed, the length of the velocity | float | 0x459440 |
| +0x13c | 1 / speed, 1e30 at a speed of at most 1e-4 | float | 0x465cc0 |
| +0x140 | World velocity | vector | 0x459440 |
| +0x14c | Pitch, yaw, and roll rates | vector | 0x459440 |
| +0x158 | Linear acceleration, rebuilt each physics step | vector | 0x459440 |
| +0x164 | Angular acceleration, rebuilt each physics step | vector | 0x459440 |
| +0x1a4 | Race | pointer | 0x4677b0 |
| +0x1bf | Dying | byte | 0x468990 |
| +0x1ac | Object checked before the shield-down overlay spawns; role unknown | pointer | 0x467b70 |
| +0x494 | Control values; +0x10 is the dying timer | pointer | 0x466090, 0x466320 |
| +0x488 | Flag that gates the height clamp of the position write; not identified | byte | 0x471640 |
| +0x4a8 | `impulseSpeed` | float | 0x450a80, save 0x462680 |
| +0x4ac | Warp speed multiplier | float | 0x450a80, save 0x462680 |
| +0x4c8 | `maximumCrew` | float | 0x4677b0, 0x4618eb |
| +0x4d8 | Crew | float | 0x4677b0, 0x467b70 |
| +0x4dc | Derelict | byte | 0x467b70 |
| +0x4e0 | Shield generator system | system | 0x4677b0 |
| +0x518 | Engines system | system | 0x4677b0 |
| +0x550 | Weapons system | system | 0x4677b0 |
| +0x588 | Life support system | system | 0x4677b0 |
| +0x5c0 | Sensors system | system | 0x4677b0 |
| +0x620 | Shield-down overlay handle, −1 for none | int | 0x467b70, 0x467d40 |
| +0x624 | Black-hole death | byte | 0x450a40, save `m_blackHoleDeath` |
| +0x628 | Center of the black hole that swallowed the ship | vector | 0x450a40, 0x4663d0 |
| +0x67c | Queue stage; the physics step skips a ship in a queue | int | 0x459440, save `queueStage` |
| +0x690 | `decommissionID`; −1 lets the end of the dying phase shatter the model | int | 0x4663d0 |
| +0x768 | Pitch, yaw, and roll angles | vector | 0x459440 |
| +0x774 | Control gain vc of the physics step | float | 0x459440 |

### Ship system (5 per ship, 0x38 bytes)

| Offset | Field | Type | Evidence |
|---|---|---|---|
| +0x00 | Working | byte | cleared at 0x467b70, set at 0x467d40 |
| +0x04 | Maximum hit points | int | 0x467d40 |
| +0x08 | Hit points | int | 0x467b70 |
| +0x14 | `<system>CrewLoss` percent | float | 0x467b70 (times 0.01 at 0x5caa90) |
| +0x1c, +0x20 | Repair time per hit point | float | 0x467b70 |
| +0x24 | `<system>SparkTime` | float | 0x4683d0 |
| +0x28 | Spark timer | float | 0x4683d0 |
| +0x2c | Disable timer | float | 0x4679b0, 0x467d40 |
| +0x30 | Held offline | byte | 0x467a80, 0x467d40 |

### Ship definition (`CraftClass`)

| Offset | Field | Type | Evidence |
|---|---|---|---|
| +0x70 | `crewCost` | int | combat-rules |
| +0x1e8 | Normal model | pointer | 0x4677b0 |
| +0x1ec | `borgPhysics` | byte | 0x464910 |
| +0x228 | `maximumCrew` | float | 0x4601d7 |
| +0x235 | `is_starbase` | byte | 0x464910 |
| +0x434 | Damage node `crew` | node | 0x45fea0 |
| +0x438 | Damage node `shields` | node | 0x45fea0 |
| +0x43c | Damage node `target` | node | 0x45fea0 |
| +0x440 | Damage node `sensors` | node | 0x45fea0 |
| +0x444 | Damage node `engines` | node | 0x45fea0 |
| +0x448 | Damage node `life` | node | 0x45fea0 |
| +0x44c | Damage node `borg` | node | 0x45fea0 |

### Race

| Offset | Field | Type | Evidence |
|---|---|---|---|
| +0x204 | `crewYellowStatus` | float | 0x4677b0 |

### Storm3D node (`ST3D_Node`)

| Offset | Field | Type | Evidence |
|---|---|---|---|
| +0x08 | Name | char pointer | 0x580510 |
| +0x1c | First child | node | 0x57cee0, 0x4677b0 |
| +0x20 | Next sibling | node | 0x57cee0 |
| +0x58 | Flags: bit 0 skip self, bit 1 skip children | uint | 0x580110, 0x4677b0 |

### Spray emitter (`ST3D_ParticleEmitter_Spray`, 0xa0 bytes)

| Offset | Field | Type | Evidence |
|---|---|---|---|
| +0x74 | Spread angle, radians | float | 0x5746d0 |
| +0x90 | Active | byte | 0x575840, 0x575400 |
| +0x98 | Timer | float | 0x575400, 0x5753a0 |
| +0x9c | Firing period | float | 0x575400, 0x5753a0 |

### Globals

| Address | Name | Role | Evidence |
|---|---|---|---|
| 0x5ca16c | Zero | float 0.0 | many |
| 0x5ca238 | One | float 1.0 | 0x4677b0 |
| 0x5ca758 | OneDouble | double 1.0 | 0x4677b0 |
| 0x5ccf30 | CrewChainStep | float 0.2 | 0x4677b0 |
| 0x5ca760 | HullProbeFactor | double 4.0, the probe distance in radii of the hull surface point | 0x464000 |
| 0x5ca8b8 | ContactFactor | double 1.5, the contact distance in radii sums | 0x464000 |
| 0x5caa18 | TurnMinShare | float 0.1, the smallest share that turns a ship | 0x464620 |
| 0x5ccddc | SeparationLift | float 0.42, the vertical push of a collision separation | 0x464bb7 |
| 0x5ccf18 | TurnMaxAngle | float 1.57, the widest angle that turns a ship | 0x464620 |
| 0x5ccf20 | MinDirectionLength | float 0.1, the shortest direction a debris piece accepts | 0x57d270 |
| 0x67dc98 | OrderRandomState | The random state of the order draws (the facing offset), shared with to-hit rolls and hit locations; `s × 2273 + 31` modulo 429397397 | 0x5014d0 |
| 0x67dca4 | DebrisRandomState | The one random state of the debris draws | 0x57d270 |
| 0x600518–0x600548 | DamageNodeNames | `crew`, `engines`, `life`, `sensors`, `shields`, `target`, `borg` | 0x45fea0 |
| 0x600750 | DyingExplosionTable | Dying explosion sprites | 0x466320 |
| 0x601240 | BorgRaceName | `borg` | 0x4677b0 |
| 0x639fcc | BorgRace | The `borg` race, looked up once | 0x4677b0 |
| 0x67f57c | NodeEffectQueue | Queued effects at model nodes | 0x515d50 |
| 0x690b60 | White | Default effect color | 0x5170e0 |
| 0x6d4aa0 | CurrentScene | The scene being drawn; +0x118 the current instance | 0x575400, 0x580110 |

## By address

Built from the topic tables by `tools/GhidraDump/program_index.py`; do not edit by hand.

<!-- by-address:start -->
| Address | Name | Status | Topic |
|---|---|---|---|
| 0x4070c0 | PathNode_Create | confirmed | Path planning |
| 0x407990 | PathNode_Expand | confirmed | Path planning |
| 0x407b10 | PathNode_AddEdge | confirmed | Path planning |
| 0x408500 | AStar_Run | confirmed | Path planning |
| 0x408660 | AStar_Relax | confirmed | Path planning |
| 0x408940 | AStar_Init | confirmed | Path planning |
| 0x408d50 | AStar_AdjustHeap | confirmed | Path planning |
| 0x408e30 | PathQuadtree_Create | confirmed | Path planning |
| 0x4093b0 | PathPlanner_Rebuild | confirmed | Path planning |
| 0x409900 | PathPlanner_WriteObstacle | confirmed | Path planning |
| 0x409a80 | PathPlanner_RemoveObstacle | confirmed | Path planning |
| 0x40a310 | Path_Plan | confirmed | Unit AI: alert stance, targets, orders |
| 0x40a950 | Path_GridCheck | confirmed | Path planning |
| 0x40aa20 | Path_Portal | confirmed | Path planning |
| 0x40ac80 | Path_PortalGraph | confirmed | Path planning |
| 0x40b490 | Path_SideTest | confirmed | Path planning |
| 0x40b4f0 | Path_OrderPortalEnds | confirmed | Path planning |
| 0x40b680 | Path_NarrowFunnel | confirmed | Path planning |
| 0x40b8c0 | Path_Funnel | confirmed | Path planning |
| 0x40c5e0 | PathQuadtree_Lookup | confirmed | Path planning |
| 0x40c700 | PathQuadtree_WriteRect | confirmed | Path planning |
| 0x40cd30 | PathGraph_CreateNodes | confirmed | Path planning |
| 0x40cee0 | PathQuadtree_PushToChild | confirmed | Path planning |
| 0x40d280 | PathQuadtree_MergeLeaves | confirmed | Path planning |
| 0x40d450 | PathGraph_BuildSplit | confirmed | Path planning |
| 0x40dd70 | PathGraph_MakeLeafNode | confirmed | Path planning |
| 0x40dfd0 | PathGraph_LinkSides | confirmed | Path planning |
| 0x40e320 | PathGraph_LinkLeaves | confirmed | Path planning |
| 0x40e470 | PathGraph_LeafCost | confirmed | Path planning |
| 0x40e990 | Grid_Query | confirmed | Unit AI: alert stance, targets, orders |
| 0x40f4d0 | Entity_AsLivingShip | confirmed | Units in general (Entity, GameObject) |
| 0x424860 | CraftProcess_ClosestApproach | confirmed | Unit AI: alert stance, targets, orders |
| 0x424b50 | CraftProcess_AvoidanceHysteresis | confirmed | Unit AI: alert stance, targets, orders |
| 0x424ba0 | CraftProcess_AvoidanceCandidates | confirmed | Unit AI: alert stance, targets, orders |
| 0x4250e0 | CraftProcess_AvoidanceScore | confirmed | Unit AI: alert stance, targets, orders |
| 0x425200 | CraftProcess_AvoidancePick | confirmed | Unit AI: alert stance, targets, orders |
| 0x425a20 | CraftProcess_Create | inferred | Unit AI: alert stance, targets, orders |
| 0x4266a0 | CraftProcess_Reset | inferred | Unit AI: alert stance, targets, orders |
| 0x4267a0 | CraftProcess_Tick | confirmed | Unit AI: alert stance, targets, orders |
| 0x426ad0 | CraftProcess_AcceptOrder | confirmed | Unit AI: alert stance, targets, orders |
| 0x428081 | CraftProcess_IdleHold | confirmed | Unit AI: alert stance, targets, orders |
| 0x428170 | CraftProcess_GoUpdate | confirmed | Unit AI: alert stance, targets, orders |
| 0x428560 | CraftProcess_FollowerGoPath | confirmed | Unit AI: alert stance, targets, orders |
| 0x428d20 | CraftProcess_DirectTarget | confirmed | Path planning |
| 0x428e90 | CraftProcess_AttackState | confirmed | Unit AI: alert stance, targets, orders |
| 0x4294e0 | CraftProcess_AttackOrder | confirmed | Unit AI: alert stance, targets, orders |
| 0x42a000 | CraftProcess_EnemyNear | confirmed | Unit AI: alert stance, targets, orders |
| 0x42a170 | CraftProcess_Update | confirmed | Unit AI: alert stance, targets, orders |
| 0x42c400 | CraftProcess_AttackManeuver | confirmed | Unit AI: alert stance, targets, orders |
| 0x42c8b0 | CraftProcess_SetAttackTarget | confirmed | Unit AI: alert stance, targets, orders |
| 0x42ded0 | CraftProcess_PathPoints | confirmed | Unit AI: alert stance, targets, orders |
| 0x42e3f0 | CraftProcess_FieldHitStop | confirmed | Unit AI: alert stance, targets, orders |
| 0x42e4a0 | CraftProcess_MoveManeuver | confirmed | Unit AI: alert stance, targets, orders |
| 0x42e7c0 | CraftProcess_MoveManeuver_Borg | confirmed | Unit AI: alert stance, targets, orders |
| 0x42ebb0 | CraftProcess_Avoidance | confirmed | Unit AI: alert stance, targets, orders |
| 0x42ed20 | CraftProcess_FieldHitPrediction | confirmed | Unit AI: alert stance, targets, orders |
| 0x42ef70 | CraftProcess_AvoidanceForce | confirmed | Unit AI: alert stance, targets, orders |
| 0x42f740 | CraftProcess_Steer | confirmed | Unit AI: alert stance, targets, orders |
| 0x42f7a6 | CraftProcess_Steer_Borg | confirmed | Unit AI: alert stance, targets, orders |
| 0x42f9b0 | CraftProcess_SteerToPoint | inferred | Unit AI: alert stance, targets, orders |
| 0x42faa0 | Target_IsValid | confirmed | Unit AI: alert stance, targets, orders |
| 0x42fb90 | Target_FindNearest | confirmed | Unit AI: alert stance, targets, orders |
| 0x42ff80 | Craft_SetWeaponTarget | confirmed | Unit AI: alert stance, targets, orders |
| 0x4300a0 | Target_IsValidFor | confirmed | Unit AI: alert stance, targets, orders |
| 0x433b30 | Ai_Update | inferred | Unit AI: alert stance, targets, orders |
| 0x4348d0 | AiPath_FindByLabel | confirmed | Map objects |
| 0x434a50 | Path_PointAt | confirmed | Unit AI: alert stance, targets, orders |
| 0x434c30 | AsteroidBelt_WritePathCost | confirmed | Map objects |
| 0x4353b0 | Collision_SweptSphereHit | confirmed | Unit AI: alert stance, targets, orders |
| 0x436840 | Team_IsAITeam | confirmed | Unit AI: alert stance, targets, orders |
| 0x43e880 | NebulaGrid_Create | confirmed | Map objects |
| 0x43ea20 | NebulaGrid_Mark | confirmed | Map objects |
| 0x43edc0 | NebulaGrid_ShipPass | confirmed | Map objects |
| 0x43f1a0 | Program_Tick | confirmed | Program, startup, and frame loop |
| 0x43f2b0 | Program_Frame | confirmed | Program, startup, and frame loop |
| 0x4404a0 | Program_GameOpenPreLoad | inferred | Program, startup, and frame loop |
| 0x4407b0 | Program_GameOpenLoad | inferred | Program, startup, and frame loop |
| 0x443df0 | RaceLoader_Load | inferred | Races |
| 0x4440b0 | Race_FindByName | confirmed | Races |
| 0x4442b0 | Race_Load | confirmed | Races |
| 0x4453f0 | RtsCfg_Load | confirmed | Program, startup, and frame loop |
| 0x4493f0 | Team_Relation | confirmed | Unit AI: alert stance, targets, orders |
| 0x449430 | Team_SetRelation | confirmed | Unit AI: alert stance, targets, orders |
| 0x44a810 | Map_DefaultBounds | confirmed | Path planning |
| 0x44ab30 | Map_ClampGoal | confirmed | Path planning |
| 0x44abb0 | MapBounds_Load | confirmed | Map objects |
| 0x44b7d0 | BlackHoleClass_Load | confirmed | Map objects |
| 0x44bf20 | NebulaKind_ClassOf | confirmed | Map objects |
| 0x44bfa0 | NebulaClass_Load | confirmed | Map objects |
| 0x44c520 | Nebula_ApplyToShip | confirmed | Map objects |
| 0x44da40 | AreaEffectObject_AreaTick | confirmed | Map objects |
| 0x44db30 | AreaEffectObject_GatherShips | confirmed | Map objects |
| 0x44e7b0 | AsteroidField_WritePathCost | confirmed | Map objects |
| 0x44f8e0 | AsteroidField_LoadBelt | confirmed | Map objects |
| 0x44f9b0 | AsteroidField_PostLoad | confirmed | Map objects |
| 0x450810 | BlackHole_WritePathCost | confirmed | Path planning |
| 0x450950 | BlackHole_Simulate | confirmed | Map objects |
| 0x450a40 | BlackHole_Swallow | confirmed | Map objects |
| 0x450a80 | BlackHole_PullShips | confirmed | Map objects |
| 0x450ea0 | Craft_SetVelocity | confirmed | Units in general (Entity, GameObject) |
| 0x450f20 | Entity_AsShip | confirmed | Units in general (Entity, GameObject) |
| 0x450f40 | Vector_Normalize | inferred | Units in general (Entity, GameObject) |
| 0x451870 | IonStorm_Touch | inferred | Map objects |
| 0x451e00 | Nebula_Place | confirmed | Map objects |
| 0x455230 | HoverClass_SetDefaults | confirmed | Unit AI: alert stance, targets, orders |
| 0x455440 | HoverCraftClass_Load | confirmed | Ships (Craft) |
| 0x458080 | Craft_TurnRadius | confirmed | Unit AI: alert stance, targets, orders |
| 0x458750 | HoverCraft_CopyCoefficients | confirmed | Ships (Craft) |
| 0x458990 | Craft_ThrottleFromForce | confirmed | Unit AI: alert stance, targets, orders |
| 0x458b80 | HoverCraft_ServoYaw | confirmed | Ships (Craft) |
| 0x458ca0 | HoverCraft_ServoPitch | confirmed | Ships (Craft) |
| 0x458ea0 | HoverCraft_MatrixToAngles | confirmed | Ships (Craft) |
| 0x459440 | HoverCraft_PhysicsStep | confirmed | Ships (Craft) |
| 0x459a99 | HoverCraft_PhysicsStep_BorgBranch | confirmed | Ships (Craft) |
| 0x45fbe0 | HoverCraftClass_SetDefaults | confirmed | Ships (Craft) |
| 0x45fea0 | CraftClass_Load | confirmed | Game object definitions |
| 0x460f30 | CraftClass_LoadSystem | confirmed | Game object definitions |
| 0x4617b0 | Craft_Init | confirmed | Ships (Craft) |
| 0x462d40 | Craft_PlayEventSound | confirmed | Ships (Craft) |
| 0x462dd0 | Craft_StopMotion | confirmed | Ships (Craft) |
| 0x463f60 | Craft_HullSurfacePoint | confirmed | Ships (Craft) |
| 0x464000 | Craft_CollisionOverlap | confirmed | Ships (Craft) |
| 0x4644b0 | Craft_PushOutOfObstacle | confirmed | Ships (Craft) |
| 0x464620 | Craft_CollisionTurn | confirmed | Ships (Craft) |
| 0x464910 | Craft_ResolveCollisions | confirmed | Ships (Craft) |
| 0x464dd0 | Craft_RollHitLocation | confirmed | Ships (Craft) |
| 0x465100 | Craft_ApplyHit | confirmed | Ships (Craft) |
| 0x4657b0 | Craft_BrakeSpring | confirmed | Unit AI: alert stance, targets, orders |
| 0x465e80 | Craft_UpdateLifeSupport | confirmed | Ships (Craft) |
| 0x466090 | Craft_DyingTimer | confirmed | Ships (Craft) |
| 0x466180 | Craft_RandomModelPoint | inferred | Ships (Craft) |
| 0x466320 | Craft_PlayDyingExplosion | confirmed | Ships (Craft) |
| 0x4663d0 | Craft_EndDying | confirmed | Ships (Craft) |
| 0x4664e0 | Craft_TargetValid | inferred | Ships (Craft) |
| 0x466b00 | Craft_RegenerateShields | confirmed | Ships (Craft) |
| 0x466ca0 | Craft_OnCrewChanged | inferred | Ships (Craft) |
| 0x466f70 | Craft_StartTransport | confirmed | Ships (Craft) |
| 0x467050 | Craft_TransportCycle | confirmed | Ships (Craft) |
| 0x4675d0 | Craft_ShareSystemDamage | inferred | Ships (Craft) |
| 0x467620 | Craft_PassSystemDamage | inferred | Ships (Craft) |
| 0x4677b0 | Craft_UpdateDamageNodes | confirmed | Ships (Craft) |
| 0x4679b0 | CraftSystem_DisableFor | confirmed | Ships (Craft) |
| 0x467a80 | CraftSystem_SetHeld | confirmed | Ships (Craft) |
| 0x467b70 | CraftSystem_TakeDamage | confirmed | Ships (Craft) |
| 0x467d40 | CraftSystem_TryRestore | confirmed | Ships (Craft) |
| 0x468060 | CraftSystem_RepairRate | confirmed | Ships (Craft) |
| 0x4683d0 | Craft_RepairSystems | confirmed | Ships (Craft) |
| 0x4685c0 | Craft_RepairAll | unknown | Ships (Craft) |
| 0x468990 | Craft_BeginDying | confirmed | Ships (Craft) |
| 0x468a60 | Craft_HitNode | confirmed | Ships (Craft) |
| 0x468cd0 | Craft_TeamRelation | confirmed | Ships (Craft) |
| 0x4690c0 | Craft_CapSpeed | confirmed | Ships (Craft) |
| 0x469120 | Craft_DisableEngines | confirmed | Ships (Craft) |
| 0x4692f0 | Craft_SpawnShieldDownOverlay | confirmed | Ships (Craft) |
| 0x46a0d0 | GameObjectClass_Init | confirmed | Game object definitions |
| 0x46a470 | GameObjectClass_Load | confirmed | Game object definitions |
| 0x46b8f0 | GameObjectClass_ShieldEllipsoid | confirmed | Game object definitions |
| 0x46bae0 | GameObjectClass_ModelPoints | confirmed | Game object definitions |
| 0x46d5c0 | GameObject_PlaceHeight | inferred | Units in general (Entity, GameObject) |
| 0x46d790 | Entity_InitHealth | confirmed | Units in general (Entity, GameObject) |
| 0x46db90 | Craft_AttachEffect | confirmed | Ships (Craft) |
| 0x46e030 | Craft_JoinTeam | confirmed | Ships (Craft) |
| 0x46e340 | GameObject_RecordHit | confirmed | Units in general (Entity, GameObject) |
| 0x46e640 | GameObject_HitRecently | confirmed | Units in general (Entity, GameObject) |
| 0x46e670 | GameObject_CloakState | inferred | Units in general (Entity, GameObject) |
| 0x46eac0 | GameObject_UpdateTether | confirmed | Units in general (Entity, GameObject) |
| 0x470940 | Grid_Rebuild | confirmed | Unit AI: alert stance, targets, orders |
| 0x4713c0 | GameObject_AreaPass | confirmed | Program, startup, and frame loop |
| 0x471410 | GameObject_SimulatePass | confirmed | Program, startup, and frame loop |
| 0x471560 | GameObject_PostSimulatePass | confirmed | Program, startup, and frame loop |
| 0x471640 | GameObject_ClampHeight | inferred | Units in general (Entity, GameObject) |
| 0x471770 | GameObject_UpdateCenter | confirmed | Units in general (Entity, GameObject) |
| 0x471850 | GameObject_SetPosition | confirmed | Units in general (Entity, GameObject) |
| 0x4718e0 | GameObject_SetMatrix | confirmed | Units in general (Entity, GameObject) |
| 0x471b30 | Craft_FindSlowEffect | inferred | Ships (Craft) |
| 0x472120 | GameObject_FrameUpdate | inferred | Ships (Craft) |
| 0x47fde0 | Selection_GroupHeading | confirmed | Unit AI: alert stance, targets, orders |
| 0x47fe90 | Selection_BuildGroups | confirmed | Unit AI: alert stance, targets, orders |
| 0x480080 | Group_BuildGrid | confirmed | Unit AI: alert stance, targets, orders |
| 0x480400 | Group_AssignCells | confirmed | Unit AI: alert stance, targets, orders |
| 0x4805d0 | Group_IssueOrders | confirmed | Unit AI: alert stance, targets, orders |
| 0x480790 | Group_Layout | confirmed | Unit AI: alert stance, targets, orders |
| 0x480890 | Selection_LayOutGroups | confirmed | Unit AI: alert stance, targets, orders |
| 0x480c30 | Selection_GroupCommand | confirmed | Unit AI: alert stance, targets, orders |
| 0x499ac0 | SelectionDisplay_LoadSprites | confirmed | Interface: selection display |
| 0x499f70 | SelectionDisplay_Update | confirmed | Interface: selection display |
| 0x49a3f0 | SelectionDisplay_ScreenBox | confirmed | Interface: selection display |
| 0x49a4a0 | SelectionDisplay_ProjectCorners | unknown | Interface: selection display |
| 0x49abf0 | SelectionDisplay_DrawShieldBar | confirmed | Interface: selection display |
| 0x49ad50 | SelectionDisplay_DrawPart | confirmed | Interface: selection display |
| 0x49afe0 | SelectionDisplay_DrawCrewDots | confirmed | Interface: selection display |
| 0x49b6a0 | SelectionDisplay_Fade | confirmed | Interface: selection display |
| 0x4ab5d0 | OverView_Deselect | confirmed | Interface: selection display |
| 0x4ab8c0 | OverView_CanSelect | confirmed | Interface: selection display |
| 0x4af2c0 | Path_Hermite | confirmed | Path planning |
| 0x4af590 | Path_PointAtParameter | confirmed | Path planning |
| 0x4d92e0 | Ordnance_SetHitNode | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4dc9c0 | EnergyShieldConverter_Apply | inferred | Special weapons and timed effects |
| 0x4dd720 | GravityMine_Hit | confirmed | Special weapons and timed effects |
| 0x4ddd30 | HoldingBeam_Hold | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4ddf80 | HoldingBeam_Update | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4dfe00 | ManheimEffect_Apply | inferred | Special weapons and timed effects |
| 0x4e42f0 | RepulsionFieldClass_Load | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4e44a0 | RepulsionField_Hit | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4e4a80 | RepulsionWave_Apply | unknown | Special weapons and timed effects |
| 0x4e5080 | RepulsionWave_Fire | confirmed | Special weapons and timed effects |
| 0x4e5da0 | ShieldInversion_Apply | inferred | Special weapons and timed effects |
| 0x4e70f0 | Ordnance_RollDamage | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4e7b00 | TractorBeamClass_Load | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4e7df0 | TractorBeam_Pull | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4e7fb0 | TractorBeam_Update | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4e9ed0 | OrdnanceClass_Init | confirmed | Game object definitions |
| 0x4ea1e0 | OrdnanceClass_Load | confirmed | Game object definitions |
| 0x4eb170 | Beam_Update | inferred | Ordnance: shots, beams, torpedoes, mines |
| 0x4ec100 | Ordnance_HitShields | inferred | Ordnance: shots, beams, torpedoes, mines |
| 0x4ec880 | Mine_Update | inferred | Ordnance: shots, beams, torpedoes, mines |
| 0x4ed150 | Missile_Update | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4ed9a0 | Ordnance_UpdatePass | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4ee5f0 | Ordnance_ShooterDying | inferred | Ordnance: shots, beams, torpedoes, mines |
| 0x4ee620 | Ordnance_TargetDying | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4ee710 | Ordnance_RollHit | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4ee7f0 | Ordnance_DropShooterBonus | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4ee860 | Ordnance_HitTest | confirmed | Ordnance: shots, beams, torpedoes, mines |
| 0x4f82f0 | Config_Primary | confirmed | Path planning |
| 0x4fc0d0 | Config_IntParam | confirmed | Path planning |
| 0x4fc1d0 | Config_FloatParam | confirmed | Path planning |
| 0x4ffd70 | Name_Equals | confirmed | Object definitions (ODF) |
| 0x501820 | Random_Signed | confirmed | Storm3D: particles and emitters |
| 0x501870 | Random_Scaled | confirmed | Storm3D: particles and emitters |
| 0x5053a0 | WeaponClass_Load | inferred | Game object definitions |
| 0x506120 | AreaEffectCannon_FireDelay | inferred | Weapons and firing |
| 0x506d70 | Craft_StopWeapons | confirmed | Weapons and firing |
| 0x506e30 | WeaponCarrier_Add | confirmed | Weapons and firing |
| 0x508750 | ReflectWeapon_Apply | inferred | Weapons and firing |
| 0x508fc0 | SelfDestruct_Update | confirmed | Special weapons and timed effects |
| 0x509bd0 | Weapon_Update | confirmed | Weapons and firing |
| 0x50a170 | Weapon_InFireArc | confirmed | Weapons and firing |
| 0x50a290 | Weapon_DistanceToTarget | confirmed | Weapons and firing |
| 0x50a3b0 | Weapon_RollToHit | confirmed | Weapons and firing |
| 0x50a520 | Weapon_InRange | confirmed | Weapons and firing |
| 0x50a7c0 | Weapon_AimHit | confirmed | Weapons and firing |
| 0x50aab0 | Weapon_AimMiss | confirmed | Weapons and firing |
| 0x50ae40 | Weapon_MakeShot | confirmed | Weapons and firing |
| 0x50b690 | Weapon_ReloadDelay | confirmed | Weapons and firing |
| 0x50b7c0 | Weapon_PlayFireSound | confirmed | Weapons and firing |
| 0x515d50 | NodeEffect_Queue | confirmed | Effects at nodes: sparks and system explosions |
| 0x515f40 | NodeEffect_Age | confirmed | Effects at nodes: sparks and system explosions |
| 0x515fb0 | NodeEffect_Draw | confirmed | Effects at nodes: sparks and system explosions |
| 0x516540 | ShieldEffect_Spawn | confirmed | Shield effect |
| 0x5169a0 | ShieldEffect_LoadGeometry | confirmed | Shield effect |
| 0x516b80 | ShieldEffect_DefaultGeometry | confirmed | Shield effect |
| 0x516b90 | ShieldEffect_FindGeometry | confirmed | Shield effect |
| 0x516c40 | ShieldEffect_MoveContact | confirmed | Shield effect |
| 0x516fa0 | ShieldEffect_Update | confirmed | Shield effect |
| 0x5170e0 | ShieldEffect_Color | confirmed | Shield effect |
| 0x51a490 | Explosion_PlaySound | confirmed | Explosions and shockwaves |
| 0x51c4d0 | ShockwaveCannon_Update | confirmed | Explosions and shockwaves |
| 0x51c8d0 | ShockwaveCannon_HitShip | confirmed | Explosions and shockwaves |
| 0x51cb90 | ShockwaveCannonClass_Load | confirmed | Explosions and shockwaves |
| 0x51d2a0 | ShockwaveExplosion_Update | confirmed | Explosions and shockwaves |
| 0x51d570 | Shockwave_End | confirmed | Explosions and shockwaves |
| 0x5537a0 | Odf_GetText | unknown | Object definitions (ODF) |
| 0x553ba0 | Odf_GetInt | inferred | Object definitions (ODF) |
| 0x553c30 | Odf_GetFloat | confirmed | Object definitions (ODF) |
| 0x553d40 | Odf_GetString | inferred | Object definitions (ODF) |
| 0x553e20 | Odf_GetName | inferred | Object definitions (ODF) |
| 0x553f20 | Odf_GetLookup | inferred | Object definitions (ODF) |
| 0x554060 | Odf_GetFloatList | inferred | Object definitions (ODF) |
| 0x554320 | Odf_GetVector3 | inferred | Object definitions (ODF) |
| 0x554430 | Odf_GetVector2 | inferred | Object definitions (ODF) |
| 0x554530 | Odf_GetList | unknown | Object definitions (ODF) |
| 0x55d080 | Math_MatrixFromAngles | confirmed | Units in general (Entity, GameObject) |
| 0x55d160 | Matrix_YawPitchRoll | confirmed | Shield effect |
| 0x55d840 | Matrix_FromDirection | inferred | Shield effect |
| 0x560a60 | SlowEffect_Init | inferred | Special weapons and timed effects |
| 0x561290 | TimedDisable_Init | confirmed | Special weapons and timed effects |
| 0x5623f0 | ShieldRemodulationEffect_Apply | inferred | Special weapons and timed effects |
| 0x5746d0 | Particle_Init | confirmed | Storm3D: particles and emitters |
| 0x5753a0 | SprayEmitter_Create | confirmed | Storm3D: particles and emitters |
| 0x575400 | SprayEmitter_Step | confirmed | Storm3D: particles and emitters |
| 0x5754c0 | SprayEmitter_Clone | confirmed | Storm3D: particles and emitters |
| 0x5756e0 | RandomParticle_Update | confirmed | Storm3D: particles and emitters |
| 0x5757b0 | SprayParticle_Update | confirmed | Storm3D: particles and emitters |
| 0x575840 | ParticleEmitter_Init | confirmed | Storm3D: particles and emitters |
| 0x5758e0 | ParticleEmitter_Step | confirmed | Storm3D: particles and emitters |
| 0x575950 | Particle_Create | confirmed | Storm3D: particles and emitters |
| 0x576220 | AnimationChannel_Frame | confirmed | Storm3D: textures, animation, and drawing |
| 0x577350 | AnimationChannel_Copy | confirmed | Storm3D: textures, animation, and drawing |
| 0x57b3f0 | Camera_FrustumTest | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x57cc40 | ChunkManager_Get | inferred | Storm3D: models, nodes, and the scene walk |
| 0x57cd90 | ChunkManager_Update | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x57ceb0 | ChunkManager_Draw | inferred | Storm3D: models, nodes, and the scene walk |
| 0x57cee0 | Node_MeshWalk | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x57cf90 | Debris_SpawnPieces | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x57d270 | Debris_CreatePiece | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x57ff30 | Model_FindNode | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x580040 | Model_AllPoints | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x580110 | Node_DrawWalk | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x580510 | Node_NameEquals | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x580a00 | Device_DrawNoDepth | confirmed | Storm3D: textures, animation, and drawing |
| 0x584040 | DeviceDX7_DrawSprite | confirmed | Storm3D: particles and emitters |
| 0x589070 | Material_Replace | confirmed | Storm3D: textures, animation, and drawing |
| 0x58a790 | Model_LoadSod | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x58a960 | Model_BindEmitter | inferred | Storm3D: models, nodes, and the scene walk |
| 0x58b4c0 | Instance_FrameSeconds | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x58b4f0 | Instance_UpdateParticles | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x58b590 | Instance_DrawParticles | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x58cb60 | Instance_Draw | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x58cef0 | SceneObject_Shatter | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x58d0e0 | Instance_SetRadius | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x58fd80 | Mesh_SetLightingMaterial | confirmed | Storm3D: textures, animation, and drawing |
| 0x5910a0 | Mesh_Scale | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x595ef0 | Sod_IsMeshNode | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x5967f0 | Model_PointBox | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x598f90 | SpriteNode_FindByName | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x599000 | SpriteNode_Corners | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x599380 | SpriteNode_Size | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x599ab0 | SpriteTable_ParseEmitter | confirmed | Storm3D: particles and emitters |
| 0x59a5b0 | Texture_FlipbookCells | confirmed | Storm3D: textures, animation, and drawing |
| 0x59b650 | Sod_ReadNode | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x59c130 | Sod_ReadModel | confirmed | Storm3D: models, nodes, and the scene walk |
| 0x59c730 | Texture_Upload | confirmed | Storm3D: textures, animation, and drawing |
| 0x59f050 | Tga_Origin | confirmed | Storm3D: textures, animation, and drawing |
| 0x59f320 | Texture_ColorKey | unknown | Storm3D: textures, animation, and drawing |
| 0x59fa90 | Texture_LoadForDevice | confirmed | Storm3D: textures, animation, and drawing |
| 0x5a39a0 | Mesh_FlipbookUv | confirmed | Storm3D: textures, animation, and drawing |
| 0x5a4200 | Mesh_VertexColor | confirmed | Storm3D: textures, animation, and drawing |
| 0x5a62c0 | Workspace_CullFront | confirmed | Storm3D: textures, animation, and drawing |
| 0x5a6360 | Workspace_CullBack | confirmed | Storm3D: textures, animation, and drawing |
| 0x5abbc0 | Audio_ReadSettings | confirmed | Audio |
| 0x5abed0 | Audio_Update | confirmed | Audio |
| 0x5acb60 | Audio_SetListener | confirmed | Audio |
| 0x5ad9c0 | Sound_CreateBuffer | confirmed | Audio |
| 0x5aff40 | Sound3D_Create | confirmed | Audio |
| 0x5b09e0 | Sound3D_Follow | confirmed | Audio |
| 0x5b0b90 | Sound3D_Play | confirmed | Audio |
<!-- by-address:end -->

## Unexplored

Ranges and anchors nobody has investigated. When an investigation covers one, add its routines to a topic
table and narrow or remove its line here.

| Range | Contents | Anchors |
|---|---|---|
| 0x401570–0x4233b0 | Strategic AI: missions, goals, agents, squads, the scheduler, and the path planning's wormhole-pair code 0x40bd40–0x40c570 (the rest of the planner is named in "Path planning") | `Ai\Strategic AI\*.cpp`; classes `AiMission`, `Goal`, `STAgent`, `Scheduler`, `A_Star_*` |
| 0x430710–0x436000 | Salvage and starbase AI, `AI` (only the AI update 0x433b30 and the swept-sphere test 0x4353b0 are named) | `SalvageProcess`, `StarbaseProcess`, `Ai\Ai.cpp` |
| 0x43d160–0x4434d0 | Game events, localized strings, the program and its frame loop beyond 0x43f1a0 and 0x43f2b0 | `Game Logic\Program.cpp`, `GameEvent.cpp`, `Localized_Strings.cpp` |
| 0x446d70–0x44a7e0 | Fog of war, shroud, cloak fog, scan grid, tech tree | `Fog`, `Shroud`, `ScanGrid`, `TechnologyTree`; key `techlvl_odf` |
| 0x44afc0–0x4548d0 | Map objects beyond the rows above: asteroid fields beyond the belt record and path cost, nebulas beyond the speed cap and the grid (damage, disables, regeneration, the pulsing), ion storms, planets, wormholes, scrap | `…Class` definitions; keys in ODF keys |
| 0x454970–0x45fb80 | Buildings and support ships: construction rigs, freighters, mining, shipyards, starbases, research | `ConstructionRig`, `Freighter`, `MiningStation`, `Shipyard`, `Starbase`, `ResearchStation` |
| 0x462d80–0x469a40 | Ship code beyond the rows above: other movement orders, cloaking, boarding, officers (the brake spring 0x4657b0, the throttle 0x458990, and the turn radius 0x458080 are named) | `Craft.cpp` |
| 0x46be50–0x4725d0 | Entity and game object code beyond the rows above | `Entity.cpp`, `GameObject.cpp` |
| 0x477a20–0x47ea10 | Cameras, cinematics, video | `CinematicCamera`, `GameCamera`, `ShotDirector`, `PlayVideo` |
| 0x47ea90–0x4b0280 | The interface beyond the selection display: control panel, radar, ship display, comm bar, overview | `ControlPanel`, `MapRadar`, `ShipDisplay`, `CommDisplay`, `OverView` |
| 0x4bb000–0x4d8fe0 | Network: transports, file transfer, round store, sync, replays | `Network\*.cpp` |
| 0x4d9010–0x4e9350 | Special weapons beyond the rows above | one class per special |
| 0x4f15d0–0x500300 | Utilities: cache, cineractive scripts, debug, parser, log, lookup tables | `Utilities\*.cpp` |
| 0x50c950–0x51e000 | Building effects, ghosts, worker bees, and explosions beyond the rows above | `BuildingEffect`, `Explosion`, `ShockwaveCannon` |
| 0x51eca0–0x552000 | Shell: menus, multiplayer setup, load and save | `Shell\*.cpp`, `LoadSaveGame.cpp` |
| 0x5547e0–0x55b830 | Parameter database internals, queue managers, user profile | `ParameterDB.cpp`, `QueueManager.cpp` |
| 0x55ef00–0x5648b0 | Timed effects and special flags beyond the rows above | `Timed…` classes, `SpecialFlag.cpp` |
| 0x576450–0x5a6200 | Storm3D beyond the rows above: devices, lights, LOD, text streams; the debris draw 0x57dc80 | `ST3D_…` classes |
| 0x5abbc0–0x5b7b10 | Audio beyond the rows above (the settings, the listener, and a 3D sound's distances, start, and following are named), jukebox, credits, starfield | `Utilities\Audio\*.cpp`, `JukeBox`, `Credits3D` |

<!-- coverage:start -->
Coverage: the index names 328 routines of the 9,401 functions in the game code band: 280 confirmed, 42 inferred, 6 unknown.
<!-- coverage:end -->

## ODF keys

Every ODF key the program reads through a getter, with the routine that reads it. Built from the dump by
`tools/GhidraDump/program_index.py`. A key read with a short string (under 8 characters, such as `range`)
can be missing (for example `crew`, `life`); search `strings.tsv` for it.

<!-- odf-keys:start -->
| Reader | Owner | Keys |
|---|---|---|
| 0x402860 | called by `Inst4XMission` | `hisPilots`, `hisScrap`, `myPilots`, `myScrap` |
| 0x4408b0 | — | `techlvl_odf` |
| 0x443df0 | RaceLoader_Load | `numberOfRaces`, `races_odf` |
| 0x4442b0 | Race_Load | `boardingStrength`, `crewAccumulationRate`, `crewRedStatus`, `crewRetreatRatio`, `crewYellowStatus`, `displayName`, `interfaceConfiguration`, `interfaceSprites`, `officerName`, `officerOperate`, `officerRes`, `officerUpgradeODF`, `planetCrewAccumulationModifier`, `repairRed`, `repairStrength`, `repairYellow`, `retreatStrength`, `weaponRed`, `weaponYellow` |
| 0x448aa0 | — | `teamcolor_odf` |
| 0x44af20 | — | `effectRadius` |
| 0x44afc0 | a method of `AreaEffectObjectClass` | `effectRadius` |
| 0x44b200 | called by `AsteroidFieldClass` | `fieldRadius`, `maxAsteroids`, `minAsteroids`, `orbitingSpeed`, `repelIfWithin`, `rotationSpeed` |
| 0x44b7d0 | BlackHoleClass_Load | `EventRadius`, `GravityForce`, `GravityRadius`, `Rotation`, `ScaleSize` |
| 0x44bc60 | called by `IonStormClass` | `damageValue`, `lifeTimer`, `Rotation`, `ScalingCount`, `ScalingStepSize` |
| 0x44bfa0 | NebulaClass_Load | `blue_glow`, `blue_glow_range`, `clearEffects`, `craft_speed_coefficient`, `craft_speed_limit`, `damageCrewModifier`, `damageHullModifier`, `damageThreshold`, `damageValue`, `damageVariance`, `disableCloak`, `disableCrew`, `disableSensors`, `disableShields`, `disableWeapons`, `glow_falloff_range`, `glow_falloff_start`, `green_glow`, `green_glow_range`, `hideContents`, `lifeTimer`, `min_speed_change`, `one_in_n_chance_of_light_source`, `pathing_cost`, `red_glow`, `red_glow_range`, `regenValue`, `repairValue`, `speed_change_period_divisor`, `speed_change_range` |
| 0x44cad0 | — | `crewModifier`, `Rotation` |
| 0x44cba0 | a method of `PlanetClass` | `crewModifier`, `Rotation` |
| 0x44cde0 | — | `infinite`, `rotationSpeed` |
| 0x44cf10 | a method of `ScrapClass` | `infinite`, `rotationSpeed` |
| 0x44d2a0 | called by `UtritiumBallClass` | `damageValue`, `lifeTimer`, `Radius`, `Rotation`, `ScalingCount`, `ScalingStepSize` |
| 0x44d580 | called by `WormHoleClass` | `AnimationRadius`, `AnimationTime`, `blue_glow`, `ClosedScale`, `closeSound`, `DisableTimer`, `glow_falloff_range`, `glow_falloff_start`, `green_glow`, `MaxScaleFactor`, `MaxSpeedFactor`, `OpenScale`, `openSound`, `red_glow`, `Rotation`, `ScaleEffectRange`, `SpeedEffectRange`, `TransportRadius` |
| 0x454b70 | called by `ConstructionRigClass` | `numberOfWorkerBees`, `timeUntilClosure`, `timeUntilTakeoff`, `timeWelding`, `weldingBeamSprite`, `weldingBeamWidth`, `workerBeeHardpoints`, `workerBeeName` |
| 0x454fe0 | — | `maxScrap` |
| 0x455080 | a method of `FreighterClass` | `maxScrap` |
| 0x4553a0 | called by `FreighterClass` | `physicsFile` |
| 0x455cf0 | a method of `HoverCraftClass` | `physicsFile` |
| 0x455ec0 | called by `MiningStationClass` | `dilithiumTransferRate`, `dockingBuffer`, `dockingHardpoint`, `freighterName` |
| 0x4561c0 | called by `ProducerClass` | `buildHardpoint`, `recrewRate` |
| 0x4564d0 | — | `isUpgrade` |
| 0x456580 | a method of `ResearchPodClass` | `isUpgrade` |
| 0x456770 | called by `ResearchStationClass` | `podHardpoints`, `upgrade` |
| 0x456cd0 | called by `ShipyardClass` | `buildAnimation`, `dockingBuffer`, `repairFacility`, `repairHardpoint`, `repairSide` |
| 0x457010 | called by `StarbaseClass` | `maximumUpgrades`, `officerGain` |
| 0x45fea0 | CraftClass_Load | `alwaysUseSpecialWeapons`, `attackPower`, `canAttack`, `criticalHitPercent`, `criticalTargetHardpoints`, `damagedScan`, `defaultGreenAlert`, `fireball`, `hullHitPercent`, `hullTargetHardpoints`, `intrinsicValue`, `is_starbase`, `isHero`, `lifeSupportLoss`, `maximumCrew`, `physicsFile`, `possibleCraftNames`, `rangeScan`, `scout`, `selfDestruct`, `shieldDelay`, `shieldRate`, `specialEnergyRate` |
| 0x461520 | — | `isTranswarpGate` |
| 0x4615e0 | a method of `SensorArrayClass` | `isTranswarpGate` |
| 0x46a470 | GameObjectClass_Load | `aiName`, `ambientSound`, `animation`, `avoidanceClass`, `avoidMe`, `baseName`, `buildTime`, `collisionRadius`, `crewCost`, `decommissionValue`, `dilithiumCost`, `distanceBelowGrid`, `hidden`, `keymapLabel`, `label`, `mapColor`, `mapIcon`, `mapSize`, `mapSprite`, `maxHealth`, `maxSpecialEnergy`, `moveBelowCombatArea`, `officerCost`, `physicsFile`, `scaleSOD`, `shieldPad`, `tooltip`, `unitName`, `verboseTooltip`, `weldingAnimSpeed`, `weldingRadius`, `weldingSpeed`, `weldingSpriteName` |
| 0x46b380 | called by `ConstructionRig` | `classLabel` |
| 0x46c2e0 | — | `officerGain` |
| 0x46c370 | a method of `UpgradeClass` | `officerGain` |
| 0x4811c0 | called by `ControlPanel` | `buildName`, `forceToNeutral` |
| 0x489b70 | a method of `ShipDisplay` | `infoSystem` |
| 0x492440 | a method of `OfficerDisplay` | `officerTooltip`, `officerVerboseTooltip` |
| 0x493d90 | a method of `SpeedRail` | `speedSingleButtonGap` |
| 0x499ac0 | SelectionDisplay_LoadSprites | `selectSystem` |
| 0x49c570 | called by `CinematicView` | `button_odf`, `colorActive`, `colorBorderDisabled`, `colorBorderMouseOver`, `colorBorderNormal`, `colorDisabled`, `colorMouseOver`, `colorNormal`, `colorPressed` |
| 0x49cf20 | called by `SpeedRail` | `component_odf`, `tooltipDelay`, `verboseTooltipDelay` |
| 0x4a2e70 | a method of `CommDisplay` | `commBarNumberOfPlayers`, `commIndicatorAnchor`, `commIndicatorWidth` |
| 0x4a8440 | — | `gui_fed_cfg` |
| 0x4c0a50 | — | `StartLocations` |
| 0x4d93d0 | — | `blastRadius`, `objectName` |
| 0x4d94a0 | a method of `ArtilleryClass` | `blastRadius`, `objectName` |
| 0x4d9c50 | called by `BlackHoleCreatorClass` | `expansionTime`, `initialScale`, `maxScale`, `retractionTime`, `timeAtMaxSize`, `visualFactor` |
| 0x4da4c0 | — | `attackRange`, `lengthScale`, `radiusScale` |
| 0x4da590 | a method of `BorgBoreClass` | `attackRange`, `lengthScale`, `radiusScale` |
| 0x4daed0 | — | `chainDamageModifier`, `chainRadius`, `maxHits` |
| 0x4daf90 | a method of `ChainPulsarClass` | `chainDamageModifier`, `chainRadius`, `maxHits` |
| 0x4dbb30 | called by `ComputerOverrideClass` | `ownerSystemDuration`, `particleArt`, `targetSystemDuration`, `timeBetweenSystems` |
| 0x4dc570 | called by `DeathChantClass` | `DamageModifier`, `FireModifier`, `HitModifier`, `RepairModifier`, `SpeedModifier`, `TurnModifier` |
| 0x4dd1d0 | — | `shieldsReturned` |
| 0x4dd260 | a method of `FedRepairTeamClass` | `shieldsReturned` |
| 0x4dd560 | — | `slowPercent` |
| 0x4dd5f0 | a method of `GravityMineClass` | `slowPercent` |
| 0x4ddab0 | called by `HoldingBeamClass` | `boardingDelay`, `boardingInterval`, `boardingSize`, `lengthScale`, `radiusScale`, `targetRange` |
| 0x4de650 | — | `objectName` |
| 0x4de710 | a method of `IonStormTorpedoClass` | `objectName` |
| 0x4defa0 | called by `KlingonCommandoTeamClass` | `fallDuration`, `fallRate`, `partySize`, `rotationRate`, `stayDuration`, `timeDelay` |
| 0x4dfab0 | — | `effectTimer` |
| 0x4dfb40 | a method of `ManheimEffectClass` | `effectTimer` |
| 0x4e0190 | called by `MicroOrganismClass` | `particleArt`, `specialEnergyDelay`, `specialEnergyDrainRate` |
| 0x4e0630 | — | `lengthScale`, `radiusScale` |
| 0x4e06e0 | a method of `MiningBeamClass` | `lengthScale`, `radiusScale` |
| 0x4e0fd0 | called by `NanitesClass` | `AIeffectTime`, `effectTexture`, `maxTime`, `minTime`, `movementSpeed`, `rollRate`, `startReturnTime` |
| 0x4e16c0 | — | `flareSprite` |
| 0x4e1750 | a method of `PhaserClass` | `flareSprite` |
| 0x4e2300 | called by `PlasmaOverdriveClass` | `DamageModifier`, `FireModifier`, `HitModifier`, `RepairModifier`, `SpeedModifier`, `TurnModifier` |
| 0x4e32f0 | — | `detectCloak`, `periodScan`, `rangeScan` |
| 0x4e33b0 | a method of `ProbeTorpedoClass` | `detectCloak`, `periodScan`, `rangeScan` |
| 0x4e3a80 | — | `percentage` |
| 0x4e3b10 | a method of `PsychonicBlastClass` | `percentage` |
| 0x4e42f0 | RepulsionFieldClass_Load | `repulsionForce` |
| 0x4e4380 | a method of `RepulsionFieldClass` | `repulsionForce` |
| 0x4e4c60 | called by `RepulsionWaveClass` | `amplitude`, `explosionRadius`, `frequency`, `repulsionForce`, `segments`, `spreadSpeed`, `Sprite`, `waveArc`, `waveLength`, `waveSpeed` |
| 0x4e5ef0 | — | `drainRate` |
| 0x4e5f80 | a method of `ShieldInversionClass` | `drainRate` |
| 0x4e69d0 | called by `TargetOverrideClass` | `segmentLength`, `segmentRadius`, `segmentTexture`, `segmentVariance` |
| 0x4e7b00 | TractorBeamClass_Load | `lengthScale`, `omegaType`, `radiusScale`, `targetRange`, `targetSpeed` |
| 0x4e83c0 | — | `initialDistance`, `scaleSize` |
| 0x4e8460 | a method of `TranswarpDriveClass` | `initialDistance`, `scaleSize` |
| 0x4e8a90 | called by `UtritiumBeamClass` | `objectName`, `scaleCount`, `scaleValue` |
| 0x4e9940 | called by `AntiMatterMineClass` | `detectRadius`, `omegaTurn`, `shotAccel` |
| 0x4e9bc0 | called by `ChainPulsarClass` | `omegaTurn`, `seekTime` |
| 0x4e9c70 | a method of `MissileClass` | `omegaTurn`, `seekTime` |
| 0x4ea1e0 | OrdnanceClass_Load | `damageBase`, `damageThreshold`, `damageVariance`, `hitSound`, `hullCrewModifier`, `ignoreShield`, `Length`, `lifeSpan`, `lightColor`, `lightFalloffRange`, `lightFalloffStart`, `lodShift`, `Radius`, `shieldCrewModifier`, `shieldDuration`, `shieldGeometryName`, `shotColor`, `shotGeometry`, `shotInfo`, `shotSound`, `shotSpeed`, `specialEnergyCost`, `specialValue`, `Sprite`, `spriteDuration` |
| 0x4ea860 | — | `classLabel` |
| 0x502ff0 | called by `AreaEffectCannonClass` | `hitCondition`, `maxTargets`, `waveEffect`, `waveName` |
| 0x5032d0 | — | `hitCondition`, `maxTargets` |
| 0x503380 | a method of `AreaEffectMissileClass` | `hitCondition`, `maxTargets` |
| 0x5035a0 | called by `CannonClass` | `maxShots`, `minimumShotInterval`, `saveFire` |
| 0x5038d0 | called by `CloakingDeviceClass` | `effectTimer`, `stepSize`, `transparency`, `unsafeCloakDistance` |
| 0x503b20 | a method of `DetectCloakClass` | `detectClass`, `detectTimer` |
| 0x503bf0 | — | `detectClass`, `detectTimer` |
| 0x504160 | a method of `PhaseCloakDeviceClass` | `energyDrainRate` |
| 0x504210 | — | `energyDrainRate` |
| 0x504440 | a method of `ReflectWeaponClass` | `reflectBeam`, `reflectMissile` |
| 0x504500 | — | `reflectBeam`, `reflectMissile` |
| 0x504760 | a method of `RegenerationWeaponClass` | `regenModifier` |
| 0x504810 | — | `regenModifier` |
| 0x504a00 | a method of `ToggleWeaponClass` | `needTarget` |
| 0x504ab0 | — | `needTarget` |
| 0x504e80 | called by `HideEffect` | `classLabel` |
| 0x5053a0 | WeaponClass_Load | `AItargetLocation`, `beneficial`, `buttonBorder`, `buttonPriority`, `buttonSlot`, `cloakEnable`, `crewAffectsWeaponDelay`, `fireEventID`, `fireSound`, `hitChance`, `needTarget`, `offSound`, `ordName`, `range`, `regularWeaponDelay`, `restrictFireArc`, `shotDelay`, `slowToHitModifier`, `special`, `specialEnergyCost`, `stopToHitModifier`, `switchToAttack`, `targetAlliedBuildings`, `targetAlliedCraft`, `targetEnemyBuildings`, `targetEnemyCraft`, `targetLocation`, `toggle`, `toHitSlowModifier`, `toHitStopModifier`, `toHitWarpModifier`, `tooltip`, `verboseTooltip`, `warpToHitModifier`, `wpnName` |
| 0x505db0 | — | `drainRate`, `lifetime`, `wormholeID` |
| 0x505e90 | a method of `WormholeGeneratorClass` | `drainRate`, `lifetime`, `wormholeID` |
| 0x508d90 | — | `radius`, `timer` |
| 0x508e40 | a method of `SelfDestructClass` | `radius`, `timer` |
| 0x5149b0 | a method of `Ghost` | `ghostAmbient`, `ghostDiffuse`, `ghostSpecular`, `light_odf` |
| 0x51a8d0 | called by `ConstructionRig` | `classLabel` |
| 0x51ad50 | called by `ExplosionClass` | `explSound` |
| 0x51b7e0 | called by `FireballExplosionClass` | `dbName`, `length`, `shockwave`, `shockwaveDelay`, `shockwaveMinimum` |
| 0x51c0f0 | — | `topEdgeModifier`, `waveHeight` |
| 0x51c1b0 | a method of `OmegaExplosionClass` | `topEdgeModifier`, `waveHeight` |
| 0x51cb90 | ShockwaveCannonClass_Load | `disableTime`, `repulsionForce`, `waveArc` |
| 0x51d950 | called by `OmegaExplosionClass` | `color`, `damageBase`, `damageThreshold`, `damageVariance`, `fadeFactor`, `hullCrewModifier`, `radiusFactor`, `shieldCrewModifier`, `texture` |
| 0x5abbc0 | Audio_ReadSettings | `audio_odf`, `bitsPerSample`, `distanceFactor`, `dopplerFactor`, `maximumDistance3D`, `minimum3DSounds`, `minimumDistance3D`, `rolloffFactor`, `sampleRate`, `streamBufferLength`, `streamChunkLength` |
<!-- odf-keys:end -->
