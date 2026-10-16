using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

// The current pose of every unit of a map: its placed pose from the moment the map opens, until a ShipMotion
// moves it. Units no motion drives, such as stations, keep their placed pose.
public sealed class UnitPoses
{
    readonly Dictionary<PlacedObject, ShipPose> _poses;

    public UnitPoses(IEnumerable<PlacedObject> units) =>
        _poses = units.ToDictionary(u => u, u => MoveGeometry.PlacedPose(u.Position, u.AxisZ));

    // The current pose of a unit of the map; throws KeyNotFoundException for an object that is no unit.
    public ShipPose Pose(PlacedObject unit) => _poses[unit];

    // Steps the unit's motion and records the pose it reaches.
    public ShipPose Advance(PlacedObject unit, ShipMotion motion, double seconds) =>
        _poses[unit] = motion.Step(seconds);
}
