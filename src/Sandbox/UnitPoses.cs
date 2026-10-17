using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

/// <summary>
/// The current pose of every unit of a map: its placed pose from the moment the map opens, until a ShipMotion
/// moves it. Units no motion drives, such as stations, keep their placed pose.
/// </summary>
public sealed class UnitPoses
{
    readonly Dictionary<PlacedObject, ShipPose> _poses;

    /// <summary>Records each unit's placed pose.</summary>
    public UnitPoses(IEnumerable<PlacedObject> units) =>
        _poses = units.ToDictionary(u => u, u => MoveGeometry.PlacedPose(u.Position, u.AxisZ));

    /// <summary>The current pose of a unit of the map; throws KeyNotFoundException for an object that is no unit.</summary>
    public ShipPose Pose(PlacedObject unit) => _poses[unit];

    /// <summary>Steps the unit's motion and records the pose it reaches.</summary>
    public ShipPose Advance(PlacedObject unit, ShipMotion motion, double seconds) =>
        _poses[unit] = motion.Step(seconds);
}
