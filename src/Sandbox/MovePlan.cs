using System;
using System.Collections.Generic;
using System.Globalization;
using OpenArmada.Assets;
using OpenArmada.Diagnostics;

namespace OpenArmada.Sandbox;

/// <summary>
/// The movement values of the scene's ships, read from their physics definitions and from <c>fastphys.odf</c>, and the
/// gaps found while reading them (INV-0002). A key the definition lacks takes the game's default and is reported once; a
/// value that is no usable number is reported and takes the game's default as a stand-in, so every ship has finite
/// coefficients. A physics definition that is missing altogether is the unit plan's report (such a ship is not
/// Movable); one the install holds that cannot be read is a gap here, and the ship has no entry and never moves.
/// </summary>
public sealed class MovePlan
{
    const string CannotBeRead = "physics definition cannot be read, ships cannot move";
    const string FastFile = "fastphys.odf";
    const string FastCannotBeRead = "fastphys.odf cannot be read, ships keep their own coefficients";

    /// <summary>The movement values of each ship that can move; a ship with a gap in reading its definition has no entry.</summary>
    public IReadOnlyDictionary<PlacedObject, PhysicsValues> Physics { get; }

    /// <summary>The gaps found while reading the physics definitions.</summary>
    public IReadOnlyList<ReportEntry> Reports { get; }

    MovePlan(IReadOnlyDictionary<PlacedObject, PhysicsValues> physics, IReadOnlyList<ReportEntry> reports)
    {
        Physics = physics;
        Reports = reports;
    }

    /// <summary>
    /// Reads the physics definition of each <see cref="Ship"/> among <c>units</c> that is Movable; stations and
    /// ships that are not Movable are skipped without a gap. <c>fastphys.odf</c> is read once, at the first ship.
    /// </summary>
    public static MovePlan Build(SandboxPlan plan, IEnumerable<SelectableUnit> units, GameFileSystem files)
    {
        var physics = new Dictionary<PlacedObject, PhysicsValues>();
        var gaps = new Gaps();
        var failedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PhysicsKeys? fast = null;
        var fastRead = false;
        foreach (var unit in units)
        {
            if (unit.Unit is not Ship || !unit.Movable)
                continue;
            var o = unit.Object;
            ResolvedDefinition definition;
            var file = plan.Resolved[o.Class].GetString("physicsFile");
            try
            {
                definition = ResolvedDefinition.Resolve(files, file);
            }
            catch (Exception e) // a physics definition that cannot be resolved is a gap, whatever the reason (INV-0002)
            {
                if (Log.IsAbsent(e))
                {
                    // absent data, such as a base definition the install does not hold: the WARN of the gap
                    gaps.Add("physics definition missing, ships cannot move", o.Class);
                }
                else
                {
                    // an asset failed to load: one ERROR with its name and reason per file (INV-0005, INV-0002)
                    if (failedFiles.Add(file))
                    {
                        Log.Error(Area.Sandbox, $"physics definition {file}: {e.Message}");
                    }

                    gaps.Add(CannotBeRead, o.Class);
                }

                continue;
            }

            if (!fastRead)
            {
                fastRead = true;
                fast = ReadFast(files, gaps);
            }

            var keys = ReadKeys(definition, o.Class, "physics definition", gaps);

            // stand-in: the game's compiled default (0x45fbe0) for a missing key or a value that is no number above 0
            var tooClose = Distance(definition, "tooCloseToTurn");
            if (tooClose == null)
            {
                gaps.Add(
                    $"physics definition's tooCloseToTurn is missing or no number above 0, ships take the game's default {PhysicsValues.DefaultTooCloseToTurn}",
                    o.Class
                );
            }

            var lead = Distance(definition, "pathLeadDistance");
            if (lead == null)
            {
                gaps.Add(
                    $"physics definition's pathLeadDistance is missing or no number above 0, ships take the game's default {PhysicsValues.DefaultPathLeadDistance.ToString("0.###", CultureInfo.InvariantCulture)}",
                    o.Class
                );
            }

            // the game reads borgPhysics as a byte (0x455440): a number other than 0 sets it; a missing key leaves it off
            var borg = false;
            if (definition.Has("borgPhysics"))
            {
                var tokens = definition.GetList("borgPhysics");
                if (tokens.Count == 1 && ResolvedDefinition.TryNumber(tokens[0], out var flag))
                {
                    borg = flag != 0;
                }
                else
                {
                    gaps.Add("physics definition's borgPhysics is no number, ships take no borg physics", o.Class);
                }
            }

            double BorgKey(string key, double fallback) => ReadBorgKey(definition, borg, key, fallback, o.Class, gaps);

            physics[o] = PhysicsValues.From(keys, fast) with
            {
                BorgPhysics = borg,
                TooSlowToTurn = BorgKey("tooSlowToTurn", PhysicsValues.DefaultTooSlowToTurn),
                RotationAcceleration = BorgKey("rotationAcceleration", PhysicsValues.DefaultRotationAcceleration),
                MaxRotationSpeed = BorgKey("maxRotationSpeed", PhysicsValues.DefaultMaxRotationSpeed),
                RotationStiffness = BorgKey("rotationStiffness", PhysicsValues.DefaultRotationStiffness),
                TooCloseToTurn = tooClose ?? PhysicsValues.DefaultTooCloseToTurn,
                PathLeadDistance = lead ?? PhysicsValues.DefaultPathLeadDistance,
            };
        }

        return new MovePlan(physics, Gaps.Logged(gaps.Entries, CannotBeRead, FastCannotBeRead));
    }

    // The keys of fastphys.odf, which every ship swaps to at its first move; null when the install has no readable file.
    static PhysicsKeys? ReadFast(GameFileSystem files, Gaps gaps)
    {
        ResolvedDefinition definition;
        try
        {
            definition = ResolvedDefinition.Resolve(files, FastFile);
        }
        catch (Exception e) // a file that cannot be resolved is a gap, whatever the reason (INV-0002)
        {
            if (Log.IsAbsent(e))
            {
                gaps.Add("fastphys.odf missing, ships keep their own coefficients", FastFile);
            }
            else
            {
                Log.Error(Area.Sandbox, $"physics definition {FastFile}: {e.Message}");
                gaps.Add(FastCannotBeRead, FastFile);
            }

            return null;
        }

        return ReadKeys(definition, FastFile, FastFile, gaps);
    }

    // The keys the game's loader reads (0x455440). A key the definition lacks takes the game's default and is reported once
    // per key; a value that is no usable number (PhysicsKeys.Specs) is reported and takes the default as a stand-in;
    // keys whose values give coefficients that are not usable (FlightCoefficients.IsUsable) are reported and the defaults
    // stand in for all of them. maxForwardSpeed is read only without an impulse speed above 0, as the game does.
    static PhysicsKeys ReadKeys(ResolvedDefinition definition, string name, string label, Gaps gaps)
    {
        var keys = PhysicsKeys.Game;
        var scaled = false;
        foreach (var spec in PhysicsKeys.Specs)
        {
            if (spec.Name == "maxForwardSpeed" && scaled)
                continue;
            if (!definition.Has(spec.Name))
            {
                gaps.Add(
                    $"{label} lacks {spec.Name}, ships take the {(spec.StandIn ? "stand-in" : "game's default")} {Show(spec.Default)}",
                    name
                );

                continue;
            }

            if (!TryNumber(definition, spec.Name, out var value) || !spec.Usable(value))
            {
                gaps.Add($"{label}'s {spec.Name} is {spec.Rule}, ships take the stand-in {Show(spec.Default)}", name);

                continue;
            }

            keys = keys.With((spec.Name, value));
            scaled |= spec.Name == "impulseSpeed" && value > 0;
        }

        if (!FlightCoefficients.Derive(keys).IsUsable)
        {
            gaps.Add($"{label}'s values give no usable flight coefficients, ships take the defaults", name);

            return PhysicsKeys.Game;
        }

        return keys;
    }

    static string Show(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // The key's number: one token, with one trailing ';' taken off. stand-in: the stock destphys.odf writes
    // `tooCloseToTurn = 40;` with a closing ';', which the game reads as 40 (the read of the number's prefix is inferred).
    static bool TryNumber(ResolvedDefinition definition, string key, out double value)
    {
        var tokens = definition.GetList(key);
        if (
            tokens.Count == 1
            && ResolvedDefinition.TryNumber(tokens[0].EndsWith(';') ? tokens[0][..^1] : tokens[0], out var f)
        )
        {
            value = f;

            return true;
        }

        value = 0;

        return false;
    }

    // A Borg key's number above 0, or the game's default with a report. stand-in: the game's loader reads the number as it
    // stands; a value of 0 or less divides the yaw spring by 0 or takes the root of a negative, so it counts as missing.
    // A ship without Borg physics is not asked: the keys only matter to the Borg branch.
    static double ReadBorgKey(
        ResolvedDefinition definition,
        bool borg,
        string key,
        double fallback,
        string unitClass,
        Gaps gaps
    )
    {
        if (!borg)
            return fallback;
        var value = Distance(definition, key);
        if (value == null)
        {
            gaps.Add(
                $"physics definition's {key} is missing or no number above 0, Borg ships take the game's default {fallback.ToString("0.###", CultureInfo.InvariantCulture)}",
                unitClass
            );
        }

        return value ?? fallback;
    }

    // The key's number when it is above 0; otherwise null.
    static double? Distance(ResolvedDefinition definition, string key) =>
        definition.Has(key) && TryNumber(definition, key, out var value) && value > 0 ? value : null;
}
