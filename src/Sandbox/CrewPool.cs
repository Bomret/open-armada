namespace OpenArmada.Sandbox;

/// <summary>
/// A team's crew pool: the reserve of crew outside its units, in the team record at +0x20 with its cap at +0x24 (the team
/// record of 0x448f30, built by 0x448d50). An Auto-Assimilator's crew beyond its unit's maximum goes into it, and crew
/// beyond the cap is lost (0x563740 case 7). See docs/rules/combat-rules.md, "Crew pool".
/// </summary>
/// <param name="cap">The most crew the pool holds.</param>
public sealed class CrewPool(double cap)
{
    /// <summary>The crew in the pool; starts at 0 (stand-in: the game's start value is a share of <c>cfgStartingCrew</c>).</summary>
    public double Crew { get; private set; }

    /// <summary>The most crew the pool holds.</summary>
    public double Cap { get; } = cap;

    /// <summary>Adds crew up to the cap; the rest is lost (0x563740 case 7).</summary>
    public void Add(double crew) => Crew = System.Math.Min(Cap, Crew + crew);
}
