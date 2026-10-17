using System;
using System.Globalization;

namespace OpenArmada.Sandbox;

/// <summary>
/// The text the readout shows for one selected unit: shields, crew, the alert stance, the hit points of the five ship systems
/// (current and maximum, or <c>-</c> for a system the unit lacks), and <c>derelict</c> for a derelict unit.
/// </summary>
public static class CombatReadout
{
    static string System(UnitCombatState state, string key, string label) =>
        state.Systems.TryGetValue(key, out var s)
            ? string.Create(CultureInfo.InvariantCulture, $"{label} {Math.Floor(s.HitPoints):0}/{s.Max:0}")
            : $"{label} -";

    /// <summary>The readout line for the unit shown as <c>name</c>; numbers use the invariant culture.</summary>
    public static string Line(string name, UnitCombatState state) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{name}  shields {Math.Round(state.Shields):0}/{state.Profile.MaxHealth:0}  crew {Math.Round(state.Crew):0}  alert {state.Alert.ToString().ToLowerInvariant()}"
        )
        + $"  {System(state, SystemNames.Engines, "engines")}  {System(state, SystemNames.LifeSupport, "life support")}"
        + $"  {System(state, SystemNames.Weapons, "weapons")}  {System(state, SystemNames.ShieldGenerator, "shield generator")}"
        + $"  {System(state, SystemNames.Sensors, "sensors")}"
        + (state.Derelict ? "  derelict" : "");
}
