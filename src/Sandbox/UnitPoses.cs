using System.Collections.Generic;
using System.Linq;

namespace OpenArmada.Sandbox;

/// <summary>
/// The current pose of every unit of a map: its placed pose from the moment the map opens, until a FlightModel
/// moves it. Units no flight model drives, such as stations, keep their placed pose.
/// </summary>
public sealed class UnitPoses
{
    readonly Dictionary<PlacedObject, ShipPose> _poses;

    /// <summary>Records each unit's placed pose.</summary>
    public UnitPoses(IEnumerable<PlacedObject> units) =>
        _poses = units.ToDictionary(u => u, u => MoveGeometry.PlacedPose(u));

    /// <summary>The current pose of a unit of the map; throws KeyNotFoundException for an object that is no unit.</summary>
    public ShipPose Pose(PlacedObject unit) => _poses[unit];

    /// <summary>Records the pose of the unit's flight model as the unit's current pose and returns it.</summary>
    public ShipPose Record(PlacedObject unit, FlightModel flight) => _poses[unit] = flight.Pose;
}
