using System;
using System.Globalization;

namespace OpenArmada.Sandbox;

// The text the readout shows for one selected unit.
public static class CombatReadout
{
    public static string Line(string name, UnitCombatState state) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{name}  shields {Math.Round(state.Shields):0}/{state.Profile.MaxHealth:0}  crew {Math.Round(state.Crew):0}"
        );
}
