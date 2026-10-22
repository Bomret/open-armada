using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

/// <summary>
/// The combat profile of every unit of a map, read once from the install, with the gaps found while
/// reading (INV-0002: a gap is reported with its cause once and the map still opens). Beam (<c>cannon</c>) and
/// torpedo (<c>launcher</c>) weapons are read; special weapons are out of scope and skipped without a gap.
/// Each gap is logged once per cause as a WARN; a definition or constants file that exists but cannot be read is
/// logged instead as one ERROR per file, with its name and reason (INV-0005, INV-0002).
/// A value the install lacks takes a stand-in (INV-0003) and is reported when the rules need it; no profile value is
/// a non-number. MaximumCrew is above zero: a maximumCrew missing or 0 takes the stand-in crewCost, at least 1.
/// </summary>
public sealed class CombatPlan
{
    const string BeamClass = "cannon";
    const string TorpedoClass = "launcher";

    /// <summary>One entry per unit of the plan, by the placed object itself (not by value).</summary>
    public IReadOnlyDictionary<PlacedObject, CombatProfile> Profiles { get; }

    /// <summary>The gaps found while reading, once per cause.</summary>
    public IReadOnlyList<ReportEntry> Reports { get; }

    /// <summary>The game constants of RTS_CFG.h that the combat rules read.</summary>
    public float ShieldDamageProtection { get; }

    /// <summary>
    /// <c>SYSTEM_HITPOINTS_BONUS</c>: the hit points added to each ship system a unit has; stand-in 0 where the
    /// install's RTS_CFG.h gives none.
    /// </summary>
    public float SystemHitpointsBonus { get; }

    CombatPlan(
        IReadOnlyDictionary<PlacedObject, CombatProfile> profiles,
        IReadOnlyList<ReportEntry> reports,
        float shieldDamageProtection,
        float systemHitpointsBonus
    )
    {
        Profiles = profiles;
        Reports = reports;
        ShieldDamageProtection = shieldDamageProtection;
        SystemHitpointsBonus = systemHitpointsBonus;
    }

    /// <summary>
    /// Reads the profile of every unit whose class <c>plan</c> resolved; other units get none. Each class is read
    /// once, and its gaps are reported for every unit of it. A missing or unreadable RTS_CFG.h, definition, or value
    /// is a gap with a stand-in.
    /// </summary>
    public static CombatPlan Build(GameFileSystem files, SandboxPlan plan, IEnumerable<SelectableUnit> units)
    {
        var gaps = new Gaps();
        var (protection, bonus) = ReadConstants(files, gaps);
        var reader = new ProfileReader(files);
        var byClass = new Dictionary<string, (CombatProfile Profile, List<(string Cause, string Name)> Gaps)>(
            StringComparer.OrdinalIgnoreCase
        );
        var profiles = new Dictionary<PlacedObject, CombatProfile>(ReferenceEqualityComparer.Instance);
        foreach (var u in units)
        {
            if (!plan.Resolved.TryGetValue(u.Object.Class, out var definition))
                continue;
            if (!byClass.TryGetValue(u.Object.Class, out var read))
            {
                var found = new List<(string, string)>();
                read = (reader.Read(definition, found), found);
                byClass[u.Object.Class] = read;
            }

            profiles[u.Object] = read.Profile;
            foreach (var (cause, name) in read.Gaps)
            {
                gaps.Add(cause, name, u.Object);
            }
        }

        // The plan is complete: each grouped report is one WARN, the same text as its on-screen line. A definition
        // that cannot be read is an ERROR entry of its own instead (see Failed); its grouped line is display only.
        var reports = gaps.Entries;
        foreach (var report in reports.Where(r => !r.Cause.Contains(CannotBeRead)))
        {
            Log.Warn(Area.Combat, report.Text);
        }

        return new CombatPlan(profiles, reports, protection, bonus);
    }

    // Marks the causes of an asset that failed to load; each is logged as one ERROR at the failure site.
    const string CannotBeRead = "cannot be read";

    // An asset that failed to load: INV-0005 (ERROR), INV-0002 (name and reason). Called once per asset.
    static void Failed(string asset, Exception e) => Log.Error(Area.Combat, $"{asset}: {e.Message}");

    static (float Protection, float Bonus) ReadConstants(GameFileSystem files, Gaps gaps)
    {
        GameConstants? constants = null;
        try
        {
            constants = GameConstantsKind.Of(files);
        }
        catch (Exception e) // a header that is missing or cannot be read is a gap, whatever the reason (INV-0002)
        {
            if (Log.IsAbsent(e))
            {
                gaps.Add($"{GameConstantsKind.FileName} missing, stand-ins used for its constants", e.Message);
            }
            else
            {
                Failed(GameConstantsKind.FileName, e);
                gaps.Add($"{GameConstantsKind.FileName} {CannotBeRead}, stand-ins used for its constants", e.Message);
            }
        }

        float Constant(string name, float standIn)
        {
            if (constants == null)
                return standIn;
            if (constants.TryNumber(name, out var v, out _))
                return (float)v;
            gaps.Add($"game constant {name} missing or no number, stand-in {Text(standIn)} used", name);

            return standIn;
        }

        // stand-in: no protection by the shields (the whole damage hurts the shields) and no bonus to system hit
        // points, where the install's RTS_CFG.h gives neither.
        return (Constant("SHIELD_DAMAGE_PROTECTION", 1), Constant("SYSTEM_HITPOINTS_BONUS", 0));
    }

    static string Text(float f) => f.ToString(CultureInfo.InvariantCulture);

    // Reads the definitions of a unit into its profile. Each definition is resolved once.
    sealed class ProfileReader(GameFileSystem files)
    {
        static readonly (string Name, string Key)[] SystemKeys =
        {
            (SystemNames.Engines, "engines"),
            (SystemNames.LifeSupport, "lifeSupport"),
            (SystemNames.Weapons, "weapons"),
            (SystemNames.ShieldGenerator, "shieldGenerator"),
            (SystemNames.Sensors, "sensors"),
        };

        // stand-in: a middling race for a race definition the install lacks: no delay and repair factors,
        // middling crew thresholds.
        static readonly RaceValues StandInRace = new()
        {
            CrewYellowStatus = 0.5f,
            CrewRedStatus = 0.25f,
            WeaponYellow = 1,
            WeaponRed = 1,
            RepairYellow = 1,
            RepairRed = 1,
            RepairStrength = 1,
        };

        // Each definition once: the definition, or null with the cause it could not be read.
        readonly Dictionary<string, (ResolvedDefinition? Definition, string Cause)> _resolved = new(
            StringComparer.OrdinalIgnoreCase
        );
        Dictionary<string, ResolvedDefinition>? _races;
        bool _raceFileUnreadable; // set when a race file races.odf lists exists but cannot be read
        string? _racesProblem; // set when races.odf is missing or cannot be read

        public CombatProfile Read(ResolvedDefinition d, List<(string Cause, string Name)> gaps)
        {
            var r = new Values(d, gaps);
            var crewCost = r.Num("crewCost", 1, report: true); // stand-in: one crew
            var maximumCrew = r.Num("maximumCrew", 0);
            var definedMaximumCrew = Math.Max(0, maximumCrew);
            if (maximumCrew <= 0)
            {
                // stand-in: the crew cost, or one, where no maximum crew is given; a ratio over it stays a number
                maximumCrew = Math.Max(1, crewCost);
                r.Gap("maximumCrew missing or 0, stand-in crewCost (at least 1) used");
            }

            var systems = new List<SystemProfile>();
            foreach (var (name, key) in SystemKeys)
            {
                // a system without hit points does not exist on the unit
                var hitPoints = r.Num(key + "HitPoints", 0);
                if (hitPoints > 0)
                {
                    systems.Add(
                        new SystemProfile
                        {
                            Name = name,
                            HitPoints = hitPoints,
                            HitPercent = r.Num(key + "HitPercent", 0),
                            CrewLoss = r.Num(key + "CrewLoss", 0),
                            RepairTime = r.Num(key + "RepairTime", 0),
                        }
                    );
                }
            }

            return new CombatProfile
            {
                Weapons = ReadWeapons(d, r, gaps),
                MaxHealth = r.Num("maxHealth", 0, report: true),
                ShieldRate = r.Num("shieldRate", 0, report: true),
                ShieldDelay = r.Num("shieldDelay", 0),
                CrewCost = crewCost,
                MaximumCrew = maximumCrew,
                DefinedMaximumCrew = definedMaximumCrew,
                Systems = systems,
                HullHitPercent = r.Num("hullHitPercent", 0),
                LifeSupportLoss = r.Num("lifeSupportLoss", 0),
                DefaultGreenAlert = r.Flag("defaultGreenAlert", false),
                CanAttack = r.Flag("canAttack", true),
                Fireball = r.Str("fireball"),
                Race = ReadRace(d, r, gaps),
            };
        }

        IReadOnlyList<WeaponProfile> ReadWeapons(
            ResolvedDefinition d,
            Values unit,
            List<(string Cause, string Name)> gaps
        )
        {
            var weapons = new List<WeaponProfile>();
            foreach (var slot in d.Indexed("weapon"))
            {
                if (
                    !int.TryParse(
                        slot.Key.AsSpan("weapon".Length),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var n
                    )
                )
                {
                    unit.Gap("weapon slot number too large, slot left out", $"{unit.Who}.{slot.Key}");

                    continue;
                }

                var name = unit.Str(slot.Key);
                if (name.Length == 0)
                    continue;
                var weapon = Resolve(name, "weapon", gaps);
                if (weapon == null)
                    continue;
                var w = new Values(weapon, gaps);
                var label = w.Str("classLabel");
                if (label.Length == 0)
                {
                    w.Gap("classLabel missing, weapon left out");
                }

                if (label != BeamClass && label != TorpedoClass)
                    continue;
                var ordName = w.Str("ordName");
                if (ordName.Length == 0)
                {
                    w.Gap("ordName missing, weapon left out");

                    continue;
                }

                var ordnance = Resolve(ordName, "ordnance", gaps);
                if (ordnance == null)
                    continue;
                var o = new Values(ordnance, gaps);
                var hardpoints = unit.List($"weaponHardpoints{n}");
                if (hardpoints.Count == 0)
                {
                    // a weapon without hardpoints fires no shot, so it would make its unit an attacker that never fires
                    unit.Gap("weapon without hardpoints, weapon left out");

                    continue;
                }

                weapons.Add(
                    new WeaponProfile
                    {
                        Slot = n,
                        Name = name,
                        Hardpoints = hardpoints,
                        ShotDelay = w.Num("shotDelay", 1, report: true),
                        Range = w.Num("range", 0, report: true),
                        HitChance = w.Target("hitChance", 1, report: true),
                        WarpToHitModifier = w.Num("warpToHitModifier", 0),
                        ToHitWarpModifier = w.Num("toHitWarpModifier", 0),
                        StopToHitModifier = w.Num("stopToHitModifier", 0),
                        ToHitStopModifier = w.Num("toHitStopModifier", 0),
                        CrewAffectsWeaponDelay = w.Flag("crewAffectsWeaponDelay", false),
                        FireSound = w.Str("fireSound"),
                        Ordnance = new OrdnanceProfile
                        {
                            ClassLabel = o.Str("classLabel"),
                            DamageBase = o.Target("damageBase", 0, report: true),
                            DamageVariance = o.Target("damageVariance", 0),
                            DamageThreshold = o.Target("damageThreshold", 0),
                            ShieldCrewModifier = o.Target("shieldCrewModifier", 0),
                            HullCrewModifier = o.Target("hullCrewModifier", 0),
                            LifeSpan = o.Num("lifeSpan", 0, report: true),
                            ShotSpeed = o.Num("shotSpeed", 0),
                            OmegaTurn = o.Num("omegaTurn", 0),
                            SeekTime = o.Num("seekTime", 0),
                            Sprite = o.Str("Sprite"),
                        },
                    }
                );
            }

            return weapons;
        }

        // The weapon or ordnance definition, or null with a gap when it is missing or cannot be read. A
        // definition that failed is reported with the same cause on every lookup.
        ResolvedDefinition? Resolve(string name, string what, List<(string Cause, string Name)> gaps)
        {
            var file = name + ".odf";
            if (!_resolved.TryGetValue(file, out var found))
            {
                try
                {
                    _resolved[file] = found = (ResolvedDefinition.Resolve(files, file), "");
                }
                catch (Exception e) // unreadable for whatever reason: reported, the weapon is left out (INV-0002)
                {
                    if (!Log.IsAbsent(e))
                    {
                        Failed($"{what} definition {name}", e);
                    }

                    _resolved[file] = found = (
                        null,
                        Log.IsAbsent(e)
                            ? $"{what} definition missing, weapon left out"
                            : $"{what} definition {CannotBeRead}, weapon left out"
                    );
                }
            }

            if (found.Definition == null)
            {
                gaps.Add((found.Cause, name));
            }

            return found.Definition;
        }

        RaceValues ReadRace(ResolvedDefinition d, Values unit, List<(string Cause, string Name)> gaps)
        {
            var race = unit.Str("race");
            if (race.Length == 0)
            {
                unit.Gap("race missing, stand-in race values used");
            }
            else if (FindRace(race) is { } def)
            {
                var r = new Values(def, gaps);

                // The unit-level weaponYellow and weaponRed are not read: docs/combat-rules.md names the race as
                // the source of the delay factors.
                return new RaceValues
                {
                    CrewYellowStatus = r.Num("crewYellowStatus", 0.5f, report: true),
                    CrewRedStatus = r.Num("crewRedStatus", 0.25f, report: true),
                    WeaponYellow = r.Num("weaponYellow", 1, report: true),
                    WeaponRed = r.Num("weaponRed", 1, report: true),
                    RepairYellow = r.Num("repairYellow", 1, report: true),
                    RepairRed = r.Num("repairRed", 1, report: true),
                    RepairStrength = r.Num("repairStrength", 1, report: true),
                };
            }
            else if (_racesProblem != null)
            {
                unit.Gap(_racesProblem, "races.odf");
            }
            else
            {
                unit.Gap(
                    _raceFileUnreadable
                        ? $"race definition {CannotBeRead}, stand-in race values used"
                        : "race definition missing, stand-in race values used",
                    race
                );
            }

            return StandInRace;
        }

        // The race definition is the file races.odf lists as race0, race1, ... whose `name` is the race (the
        // files can be loose or in an archive).
        ResolvedDefinition? FindRace(string race)
        {
            if (_races == null)
            {
                _races = new Dictionary<string, ResolvedDefinition>(StringComparer.OrdinalIgnoreCase);
                ResolvedDefinition list;
                try
                {
                    list = ResolvedDefinition.Resolve(files, "races.odf");
                }
                catch (Exception e) // missing or unreadable for whatever reason: a gap, whatever the reason (INV-0002)
                {
                    if (!Log.IsAbsent(e))
                    {
                        Failed("races.odf", e);
                    }

                    _racesProblem = Log.IsAbsent(e)
                        ? "races.odf missing, stand-in race values used"
                        : $"races.odf {CannotBeRead}, stand-in race values used";

                    return null;
                }

                foreach (var slot in list.Indexed("race"))
                {
                    try
                    {
                        var def = ResolvedDefinition.Resolve(files, list.GetString(slot.Key));
                        if (def.Has("name"))
                        {
                            _races.TryAdd(def.GetString("name"), def);
                        }
                    }
                    catch (Exception e) // a race file that cannot be read is no race: its race is reported as unreadable
                    {
                        if (!Log.IsAbsent(e))
                        {
                            _raceFileUnreadable = true;
                            Failed($"race definition {list.GetString(slot.Key)}", e);
                        }
                    }
                }
            }

            return _races.GetValueOrDefault(race);
        }
    }

    // Typed reads of one definition that report what is missing or unreadable and fall back to a stand-in.
    sealed class Values(ResolvedDefinition d, List<(string Cause, string Name)> gaps)
    {
        public string Who => d.Name.Replace(".odf", "", StringComparison.OrdinalIgnoreCase);

        public void Gap(string cause, string? name = null) => gaps.Add((cause, name ?? Who));

        // The value of a key through `get`; a missing key (reported when `report`) or an unreadable one takes the
        // stand-in.
        T Read<T>(string key, T standIn, float shown, bool report, Func<T> get)
        {
            if (!d.Has(key))
            {
                if (report)
                {
                    Gap($"{key} missing, stand-in {Text(shown)} used");
                }

                return standIn;
            }

            try
            {
                return get();
            }
            catch (DefinitionException)
            {
                Gap($"{key} is no number, stand-in {Text(shown)} used");

                return standIn;
            }
        }

        public float Num(string key, float standIn, bool report = false) =>
            Read(key, standIn, standIn, report, () => d.GetFloat(key));

        public TargetValue Target(string key, float standIn, bool report = false)
        {
            var v = Read(key, TargetValue.Of(standIn), standIn, report, () => d.GetTargetValue(key));
            if (v.Malformed)
            {
                Gap("per-target override malformed, default used", $"{Who}.{key}");
            }

            return v;
        }

        public bool Flag(string key, bool standIn)
        {
            if (!d.Has(key))
                return standIn;
            try
            {
                return d.GetInt(key) != 0;
            }
            catch (DefinitionException)
            {
                Gap($"{key} is no whole number, stand-in {standIn} used");

                return standIn;
            }
        }

        // "" when missing or unreadable (a missing one is reported by the caller that needs it).
        public string Str(string key)
        {
            if (!d.Has(key))
                return "";
            try
            {
                return d.GetString(key);
            }
            catch (DefinitionException)
            {
                Gap($"{key} is no single value, left out");

                return "";
            }
        }

        public IReadOnlyList<string> List(string key) => d.Has(key) ? d.GetList(key) : Array.Empty<string>();
    }
}
