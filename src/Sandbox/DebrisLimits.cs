namespace OpenArmada.Sandbox;

/// <summary>
/// The limits on a unit's debris pieces: the <c>ST3D_CHUNK_*</c> values of the install's ART_CFG.h (which RTS_CFG.h
/// includes). The defaults are the game's compiled ones (0x58cef0, 0x57cd90, 0x57d270), which apply to every value
/// the install lacks or gives as no finite number. A cap, a density, or a lifetime of 0 or less leaves no debris.
/// </summary>
public sealed record DebrisLimits
{
    /// <summary><c>ST3D_CHUNK_MANAGER_MAX_CHUNKS</c>: the most pieces alive at once; the oldest goes first.</summary>
    public int MaxChunks { get; init; } = 3000;

    /// <summary><c>ST3D_CHUNK_DENSITY</c>: the share of a piece each face of a mesh adds to the mesh's accumulator.</summary>
    public float Density { get; init; } = 1f;

    /// <summary><c>ST3D_CHUNK_MAX_LIFETIME</c>: the longest life of a piece in seconds.</summary>
    public float MaxLifetime { get; init; } = 3f;

    /// <summary><c>ST3D_CHUNK_MAX_ANGULAR_VELOCITY</c>: the greatest tumble rate of a piece about each axis, radians per second.</summary>
    public float MaxAngularVelocity { get; init; } = 4f;

    /// <summary><c>ST3D_CHUNK_MAX_VELOCITY</c>: the greatest speed of a piece in units per second.</summary>
    public float MaxVelocity { get; init; } = 200f;

    /// <summary>
    /// The greatest density the sandbox takes from an install; a greater one is reported and this is used.
    /// </summary>
    /// <remarks>
    /// stand-in: the game sets no bound, and a density of 2^24 or more never empties the float accumulator, so a shatter
    /// would not end. 100 pieces per face is far above any install's (the stock one is below 1) and keeps a shatter short;
    /// raise it if an install ever needs more.
    /// </remarks>
    public const float MostDensity = 100f;

    /// <summary>
    /// The greatest cap on live pieces the sandbox takes from an install; a greater one (a value beyond int's range
    /// included) is reported and this is used.
    /// </summary>
    /// <remarks>
    /// stand-in: the game sets no bound. 10000 is over three times the game's default of 3000 and over six times the stock
    /// 1500, and bounds the triangles the view draws at once, one node each.
    /// </remarks>
    public const int MostChunks = 10000;

    /// <summary>The game's compiled defaults.</summary>
    public static DebrisLimits Default { get; } = new();

    /// <summary>Whether these limits let any piece exist.</summary>
    public bool AllowsDebris => MaxChunks > 0 && Density > 0 && MaxLifetime > 0;
}
