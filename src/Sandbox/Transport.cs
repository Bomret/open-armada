namespace OpenArmada.Sandbox;

/// <summary>
/// The transporter game constants of RTS_CFG.h (0x44621f..0x446294): the center-to-center range, the seconds between
/// two parties, the seconds a party takes to land on a hostile target, and the crew of one party.
/// </summary>
public readonly record struct TransporterValues(float Range, float Delay, float ActionDelay, int Max)
{
    /// <summary>
    /// stand-in: the values of the 1.2 install's RTS_CFG.h, for an install that gives none (INV-0003); the game has no
    /// compiled default that was recovered.
    /// </summary>
    public static readonly TransporterValues Stock = new(500, 1, 1, 5);
}

/// <summary>
/// A boarding party in flight (0x55f620): the crew a transport took from its source, the team and race the source had
/// when it sent the party, and the seconds until it lands. It reads nothing of its source after it is sent, so it lands
/// whatever happened to the source (0x55f750).
/// </summary>
public sealed class BoardingParty(
    PlacedObject source,
    UnitCombatState target,
    double size,
    int team,
    RaceValues race,
    double delay
)
{
    /// <summary>The unit that sent the party; it may be destroyed or captured by the time the party lands.</summary>
    public PlacedObject Source { get; } = source;

    /// <summary>The unit the party lands on.</summary>
    public UnitCombatState Target { get; } = target;

    /// <summary>The party's crew.</summary>
    public double Size { get; } = size;

    /// <summary>The source's team when the party was sent.</summary>
    public int Team { get; } = team;

    /// <summary>The source's race when the party was sent.</summary>
    public RaceValues Race { get; } = race;

    /// <summary>The seconds until the party lands.</summary>
    public double Remaining { get; set; } = delay;
}

/// <summary>What a view does for a transport event.</summary>
public enum CueKind
{
    /// <summary>Attach the transport effect to the unit.</summary>
    EffectOn,

    /// <summary>Take the transport effect off the unit.</summary>
    EffectOff,

    /// <summary>Play the sound <see cref="TransportCue.Name"/> at the unit.</summary>
    Sound,

    /// <summary>Give the unit the team color of the team-color slot of <see cref="TransportCue.Team"/>.</summary>
    Recolor,
}

/// <summary>One thing a view shows or plays for a transport event, from <see cref="CombatArt.TransportCues"/>.</summary>
public readonly record struct TransportCue(CueKind Kind, PlacedObject Unit, string Name = "", int Team = 0)
{
    /// <summary>The effect's sprite row (0x466f70 attaches <c>xtransport</c>).</summary>
    public const string EffectSprite = "xtransport";

    /// <summary>The sound played at the source and the target of every party (0x467050).</summary>
    public const string TransportSound = "transprt.wav";

    /// <summary>The sound played at the target of a fight against a live hostile unit (0x55f750).</summary>
    public const string BoardingSound = "msboard.wav";
}

/// <summary>A transport began: <c>Source</c> beams crew to <c>Target</c>.</summary>
public sealed record TransportStarted : CombatEvent
{
    /// <summary>The transporting unit.</summary>
    public required PlacedObject Source { get; init; }

    /// <summary>The unit the transport aims at.</summary>
    public required PlacedObject Target { get; init; }
}

/// <summary>
/// An accepted transport order that starts no transport, since the source has no more than <c>TRANSPORTER_MAX</c> crew:
/// the order's voice line plays and nothing else.
/// </summary>
public sealed record TransportOrdered : CombatEvent
{
    /// <summary>The ordered unit.</summary>
    public required PlacedObject Source { get; init; }

    /// <summary>The unit the order aims at.</summary>
    public required PlacedObject Target { get; init; }
}

/// <summary>A transport ended, by a cancel, a stop condition, or the loss of one of its units.</summary>
public sealed record TransportStopped : CombatEvent
{
    /// <summary>The transporting unit.</summary>
    public required PlacedObject Source { get; init; }

    /// <summary>The unit the transport aimed at.</summary>
    public required PlacedObject Target { get; init; }
}

/// <summary>A transport sent one boarding party.</summary>
public sealed record PartySent : CombatEvent
{
    /// <summary>The unit the party left.</summary>
    public required PlacedObject Source { get; init; }

    /// <summary>The unit the party is bound for.</summary>
    public required PlacedObject Target { get; init; }
}

/// <summary>A boarding party fought the crew of a live hostile target (0x55f750).</summary>
public sealed record BoardingFight : CombatEvent
{
    /// <summary>The unit that sent the party.</summary>
    public required PlacedObject Source { get; init; }

    /// <summary>The unit that was boarded.</summary>
    public required PlacedObject Target { get; init; }
}

/// <summary>A hostile unit changed team and race through boarding: the capture.</summary>
public sealed record UnitCaptured : CombatEvent
{
    /// <summary>The captured unit.</summary>
    public required PlacedObject Unit { get; init; }

    /// <summary>The unit that sent the capturing party.</summary>
    public required PlacedObject Source { get; init; }

    /// <summary>The team the unit joined.</summary>
    public required int Team { get; init; }
}
