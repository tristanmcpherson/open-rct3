// Path Surface Resolution Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class PathSurfaceResolutionTests {
  [Test]
  public void Load_PropagatesResolvedPtdAndQtdNamesAndQueueColours() {
    var path = new DatPathGroundData(1, 0, byte.MaxValue, 0, 0, 11, byte.MaxValue, 0);
    var queue = new DatPathQueueData(
      entryId: 2,
      baseHeight: 0,
      colIndex: 1,
      direction: byte.MaxValue,
      endDirection: byte.MaxValue,
      fenceEntry: 0,
      fenceFlexiColours: default,
      pathType: 2,
      quantisedHeight: 0,
      queueLine: 0,
      rowIndex: 0,
      sceneryItem: 0,
      slopeType: 3,
      startDirection: byte.MaxValue,
      surface: 22,
      surfaceType: byte.MaxValue,
      undergroundFlag: null,
      boolValue: 0);
    var pathType = new DatPathTypeDatabaseEntryData(11, true, false, true, "Asphalt");
    var queueType = new DatQueueTypeDatabaseEntryData(33, true, false, true, "QueueSet1");
    var queueGround = new DatQueueTypeGroundSurfaceData(
      22,
      new DatPathSurfaceColours(4, 5, 6),
      queueType.EntryId);
    DatPathSurfaceResolver.Resolve([path, queue], [pathType, queueGround, queueType]);
    var terrain = new Terrain(2, 1, 0);
    var park = new Park(buildableWidth: 2, buildableHeight: 1);

    PathManagerLoader.Load(park, terrain, [path, queue]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(park.Paths[(0, 0)].SurfaceSystemName, Is.EqualTo("Asphalt"));
      Assert.That(park.Paths[(0, 0)].SurfaceColours, Is.Null);
      Assert.That(park.Paths[(1, 0)].SurfaceSystemName, Is.EqualTo("QueueSet1"));
      Assert.That(
        park.Paths[(1, 0)].SurfaceColours,
        Is.EqualTo(new PathSurfaceColours(4, 5, 6)));
    }
  }
}
