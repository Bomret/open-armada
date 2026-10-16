using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenArmada.Assets;
using OpenArmada.Formats;

namespace OpenArmada.Sandbox;

// The combat profile of every unit of a map, read once from the install, with the gaps found while
// reading (INV-0002: a gap is reported with its cause once and the map still opens). Beam (`cannon`) and
// torpedo (`launcher`) weapons are read; special weapons are out of scope and skipped without a gap.
// A value the install lacks takes a stand-in and is reported when the rules need it; no profile value is a
// non-number and MaximumCrew is above zero (INV-0003).
public sealed class CombatPlan
{
    const string BeamClass = "cannon";
    const string TorpedoClass = "launcher";

    // One entry per unit of the plan, by the placed object itself (not by value).
    public IReadOnlyDictionary<PlacedObject, CombatProfile> Profiles { get; }
    public IReadOnlyList<ReportEntry> Reports { get; }

    // The game constants of RTS_CFG.h that the combat rules read.
    public float ShieldDamageProtection { get; }
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
                gaps.Add(cause, name, u.Object);
        }
        return new CombatPlan(profiles, gaps.Entries, protection, bonus);
    }

    static (float Protection, float Bonus) ReadConstants(GameFileSystem files, Gaps gaps)
    {
        // stand-in: no protection by the shields (the whole damage hurts the shields) and no bonus to system hit
        // points, where the install's RTS_CFG.h gives neither.
        float protection = 1,
            bonus = 0;
        GameConstants? constants = null;
        try
        {
            constants = GameConstantsKind.Of(files);
        }
        catch (Exception e) // a header that is missing or cannot be read is a gap, whatever the reason (INV-0002)
        {
            gaps.Add($"{GameConstantsKind.FileName} cannot be read, stand-ins used for its constants", e.Message);
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
        protection = Constant("SHIELD_DAMAGE_PROTECTION", protection);
        bonus = Constant("SYSTEM_HITPOINTS_BONUS", bonus);
        return (protection, bonus);
    }

    static string Text(float f) => f.ToString(CultureInfo.InvariantCulture);

    // Reads the definitions of a unit into its profile. Each definition is resolved once.
    sealed class ProfileReader(GameFileSystem files)
    {
        static readonly (string Name, string Key)[] SystemKeys =
        {
            ("Engines", "engines"),
            ("LifeSupport", "lifeSupport"),
            ("Weapons", "weapons"),
            ("ShieldGenerator", "shieldGenerator"),
            ("Sensors", "sensors"),
        };

        readonly Dictionary<string, ResolvedDefinition?> _resolved = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ResolvedDefinition>? _races;

        public CombatProfile Read(ResolvedDefinition d, List<(string Cause, string Name)> gaps)
        {
            var r = new Values(d, gaps);
            var crewCost = r.Num("crewCost", 1, report: true); // stand-in: one crew
            var maximumCrew = r.Num("maximumCrew", 0);
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
                    systems.Add(
                        new SystemProfile(
                            name,
                            hitPoints,
                            r.Num(key + "HitPercent", 0),
                            r.Num(key + "CrewLoss", 0),
                            r.Num(key + "RepairTime", 0)
                        )
                    );
            }
            return new CombatProfile(
                ReadWeapons(d, r, gaps),
                r.Num("maxHealth", 0, report: true), // stand-in: no shields
                r.Num("shieldRate", 0, report: true), // stand-in: no shield regain
                r.Num("shieldDelay", 0),
                crewCost,
                maximumCrew,
                systems,
                r.Num("hullHitPercent", 0),
                r.Num("lifeSupportLoss", 0),
                r.Flag("defaultGreenAlert", false),
                r.Flag("canAttack", true),
                r.Str("fireball"),
                ReadRace(d, r, gaps)
            );
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
                var n = int.Parse(slot.Key.AsSpan("weapon".Length), NumberStyles.None, CultureInfo.InvariantCulture);
                var name = unit.Str(slot.Key);
                if (name.Length == 0)
                    continue;
                var weapon = Resolve(name, "weapon", gaps);
                if (weapon == null)
                    continue;
                var w = new Values(weapon, gaps);
                var label = w.Str("classLabel");
                if (label.Length == 0)
                    w.Gap("classLabel missing, weapon left out");
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
                    unit.Gap("weapon without hardpoints, it cannot fire");
                weapons.Add(
                    new WeaponProfile(
                        n,
                        name,
                        hardpoints,
                        w.Num("shotDelay", 1, report: true), // stand-in: one second between shots
                        w.Num("range", 0, report: true), // stand-in: no range, so the weapon never fires
                        w.Target("hitChance", 1, report: true), // stand-in: a sure hit
                        w.Num("warpToHitModifier", 0),
                        w.Num("toHitWarpModifier", 0),
                        w.Num("stopToHitModifier", 0),
                        w.Num("toHitStopModifier", 0),
                        w.Flag("crewAffectsWeaponDelay", false),
                        w.Str("fireSound"),
                        new OrdnanceProfile(
                            o.Str("classLabel"),
                            o.Target("damageBase", 0, report: true), // stand-in: no damage
                            o.Target("damageVariance", 0),
                            o.Target("damageThreshold", 0),
                            o.Target("shieldCrewModifier", 0),
                            o.Target("hullCrewModifier", 0),
                            o.Num("lifeSpan", 0, report: true), // stand-in: the shot ends at once
                            o.Num("shotSpeed", 0),
                            o.Num("omegaTurn", 0),
                            o.Num("seekTime", 0),
                            o.Str("Sprite")
                        )
                    )
                );
            }
            return weapons;
        }

        // The weapon or ordnance definition, or null with a gap when it is missing or cannot be read.
        ResolvedDefinition? Resolve(string name, string what, List<(string Cause, string Name)> gaps)
        {
            var file = name + ".odf";
            if (!_resolved.TryGetValue(file, out var found))
                try
                {
                    _resolved[file] = found = ResolvedDefinition.Resolve(files, file);
                }
                catch (Exception e) // unreadable for whatever reason: reported, the weapon is left out (INV-0002)
                {
                    _resolved[file] = null;
                    gaps.Add(
                        e is FileNotFoundException
                            ? ($"{what} definition missing, weapon left out", name)
                            : ($"{what} definition cannot be read, weapon left out", name)
                    );
                    return null;
                }
            if (found == null)
                gaps.Add(($"{what} definition missing or unreadable, weapon left out", name));
            return found;
        }

        RaceValues ReadRace(ResolvedDefinition d, Values unit, List<(string Cause, string Name)> gaps)
        {
            var race = unit.Str("race");
            if (race.Length == 0)
                unit.Gap("race missing, stand-in race values used");
            else if (FindRace(race) is { } def)
            {
                var r = new Values(def, gaps);
                return new RaceValues(
                    r.Num("crewYellowStatus", 0.5f, report: true), // stand-in: crew thresholds of a middling race
                    r.Num("crewRedStatus", 0.25f, report: true),
                    r.Num("weaponYellow", 1, report: true), // stand-in: no delay factor
                    r.Num("weaponRed", 1, report: true),
                    r.Num("repairYellow", 1, report: true), // stand-in: no repair factor
                    r.Num("repairRed", 1, report: true),
                    r.Num("repairStrength", 1, report: true)
                );
            }
            else
                unit.Gap("race definition missing, stand-in race values used", race);
            // stand-in: a race with no delay and repair factors and middling crew thresholds
            return new RaceValues(0.5f, 0.25f, 1, 1, 1, 1, 1);
        }

        // The race definition is the definition of the addon folder whose `name` is the race.
        ResolvedDefinition? FindRace(string race)
        {
            if (_races == null)
            {
                _races = new Dictionary<string, ResolvedDefinition>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files.List(".odf", "addon"))
                    try
                    {
                        var def = ResolvedDefinition.Resolve(files, file);
                        if (def.Has("name"))
                            _races.TryAdd(def.GetString("name"), def);
                    }
                    catch (Exception) // a definition that cannot be read is no race
                    { }
            }
            return _races.GetValueOrDefault(race);
        }
    }

    // Typed reads of one definition that report what is missing or unreadable and fall back to a stand-in.
    sealed class Values(ResolvedDefinition d, List<(string Cause, string Name)> gaps)
    {
        public string Who => d.Name.Replace(".odf", "", StringComparison.OrdinalIgnoreCase);

        public void Gap(string cause, string? name = null) => gaps.Add((cause, name ?? Who));

        public float Num(string key, float standIn, bool report = false)
        {
            if (!d.Has(key))
            {
                if (report)
                    Gap($"{key} missing, stand-in {Text(standIn)} used");
                return standIn;
            }
            try
            {
                return d.GetFloat(key);
            }
            catch (DefinitionException)
            {
                Gap($"{key} is no number, stand-in {Text(standIn)} used");
                return standIn;
            }
        }

        public TargetValue Target(string key, float standIn, bool report = false)
        {
            if (!d.Has(key))
            {
                if (report)
                    Gap($"{key} missing, stand-in {Text(standIn)} used");
                return new TargetValue(standIn, new Dictionary<string, float>());
            }
            try
            {
                var v = d.GetTargetValue(key);
                if (v.Problem != null)
                    Gap("per-target override malformed, default used", $"{Who}.{key}");
                return v;
            }
            catch (DefinitionException)
            {
                Gap($"{key} is no number, stand-in {Text(standIn)} used");
                return new TargetValue(standIn, new Dictionary<string, float>());
            }
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
