namespace OpenArmada.Sandbox;

/// <summary>The status of a unit's crew against its race's crew levels (docs/rules/combat-rules.md).</summary>
public enum CrewStatus
{
    /// <summary>Above the race's crewYellowStatus.</summary>
    Green,

    /// <summary>At or below crewYellowStatus, above crewRedStatus.</summary>
    Yellow,

    /// <summary>At or below crewRedStatus.</summary>
    Red,
}
