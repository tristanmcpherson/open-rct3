// Path Manager Loader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class PathManagerLoaderTests {
  [Test]
  public void Load_GroundPathConvertsDirectionAndPreservesSurface() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = new DatPathGroundData(
      entryId: 9,
      colIndex: 2,
      direction: 0,
      pathType: 0,
      rowIndex: 3,
      surface: 42,
      surfaceType: byte.MaxValue,
      boolValue: 1);

    PathManagerLoader.Load(park, terrain, [source]);

    var tile = park.Paths[(2, 3)];
    using (Assert.EnterMultipleScope()) {
      Assert.That(tile.Direction, Is.EqualTo(Edge.West));
      Assert.That(tile.SurfaceReference, Is.EqualTo(42));
      Assert.That(tile.SurfaceType, Is.EqualTo(byte.MaxValue));
      Assert.That(tile.Raised, Is.False);
      Assert.That(tile.IsQueue, Is.False);
    }
  }

  [Test]
  public void Load_FlatGroundPathAcceptsUnsetDirection() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = new DatPathGroundData(
      entryId: 9,
      colIndex: 1,
      direction: byte.MaxValue,
      pathType: 0,
      rowIndex: 1,
      surface: 0,
      surfaceType: byte.MaxValue,
      boolValue: 0);

    PathManagerLoader.Load(park, terrain, [source]);

    Assert.That(park.Paths[(1, 1)].Direction, Is.Null);
  }

  [TestCase(1, PathRaisedSlope.Sloped, Edge.North, 200)]
  [TestCase(2, PathRaisedSlope.Sloped, Edge.South, 200)]
  [TestCase(4, PathRaisedSlope.Gentle, Edge.North, 100)]
  [TestCase(5, PathRaisedSlope.Gentle, Edge.South, 100)]
  public void Load_FlyingPathConvertsSlopeDirectionAndRise(
    byte slopeType,
    PathRaisedSlope expectedSlope,
    Edge expectedHighEdge,
    int expectedRise
  ) {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Flying(direction: 3, slopeType, quantisedHeight: 6);

    PathManagerLoader.Load(park, terrain, [source]);

    var tile = park.Paths[(1, 1)];
    using (Assert.EnterMultipleScope()) {
      Assert.That(tile.Raised, Is.True);
      Assert.That(tile.RaisedHeight, Is.EqualTo(600));
      Assert.That(tile.RaisedSlope, Is.EqualTo(expectedSlope));
      Assert.That(tile.RaisedSlopeDirection, Is.EqualTo(expectedHighEdge));
      Assert.That(
        tile.GetRaisedEdgeHeight(expectedHighEdge) - tile.RaisedHeight,
        Is.EqualTo(expectedRise));
    }
  }

  [Test]
  public void Load_FlyingPathUsesLegacyHeightMarkerAndAllowsNegativeHeight() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Flying(
      direction: 1,
      slopeType: 0,
      quantisedHeight: -286_331_154,
      baseHeight: -2);

    PathManagerLoader.Load(park, terrain, [source]);

    Assert.That(park.Paths[(1, 1)].RaisedHeight, Is.EqualTo(-400));
  }

  [Test]
  public void Load_LegacyHeightOverflowFailsClosed() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Flying(
      direction: 1,
      slopeType: 0,
      quantisedHeight: -286_331_154,
      baseHeight: int.MaxValue);

    Assert.Throws<InvalidDataException>(new Action(() =>
      PathManagerLoader.Load(park, terrain, [source])));
  }

  [Test]
  public void Load_SlopedHighEdgeOverflowFailsBeforeMutatingPark() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = Flying(
      direction: 3,
      slopeType: 1,
      quantisedHeight: 21_474_835);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      PathManagerLoader.Load(park, terrain, [source])));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error?.Message, Does.Contain("height exceeds the simulation range"));
      Assert.That(park.PathPlacements, Is.Empty);
      Assert.That(park.Paths, Is.Empty);
    }
  }

  [Test]
  public void Load_TerrainFollowingFlyingPathStaysAtGrade() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);

    PathManagerLoader.Load(park, terrain, [Flying(direction: 2, slopeType: 3)]);

    Assert.That(park.Paths[(1, 1)].Raised, Is.False);
  }

  [Test]
  public void Load_QueuePreservesDirectedEndpointsAndOwner() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = new DatPathQueueData(
      entryId: 12,
      baseHeight: 1,
      colIndex: 1,
      direction: 1,
      endDirection: 3,
      fenceEntry: 0,
      fenceFlexiColours: new DatFenceFlexiColours(1, 2, 3),
      pathType: 2,
      quantisedHeight: 2,
      queueLine: 99,
      rowIndex: 1,
      sceneryItem: 0,
      slopeType: 0,
      startDirection: 0,
      surface: 8,
      surfaceType: byte.MaxValue,
      undergroundFlag: true,
      boolValue: 0);

    PathManagerLoader.Load(park, terrain, [source]);

    var tile = park.Paths[(1, 1)];
    using (Assert.EnterMultipleScope()) {
      Assert.That(tile.IsQueue, Is.True);
      Assert.That(tile.QueueStartDirection, Is.EqualTo(Edge.West));
      Assert.That(tile.QueueEndDirection, Is.EqualTo(Edge.North));
      Assert.That(tile.QueueFlowDirection, Is.EqualTo(Edge.North));
      Assert.That(tile.QueueLineReference, Is.EqualTo(99));
      Assert.That(tile.Underground, Is.True);
    }
  }

  [Test]
  public void Load_PreservesGroundAndElevatedLayersAtSameCoordinate() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var flying = Flying(direction: 0, slopeType: 0, quantisedHeight: 8);
    var ground = new DatPathGroundData(11, 1, 0, 0, 1, 0, byte.MaxValue, 0);

    PathManagerLoader.Load(park, terrain, [flying, ground]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(park.PathPlacements, Has.Count.EqualTo(2));
      Assert.That(park.PathPlacements.Count(path => path.Tile.Raised), Is.EqualTo(1));
      Assert.That(park.Paths[(1, 1)].Raised, Is.False);
    }
  }

  [Test]
  public void Load_ThenPlacePathReplacesPrimaryLayerForRenderingAndSerialization() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var flying = Flying(direction: 0, slopeType: 0, quantisedHeight: 8);
    var ground = new DatPathGroundData(11, 1, 0, 0, 1, 0, byte.MaxValue, 0);
    PathManagerLoader.Load(park, terrain, [flying, ground]);
    var replacement = new PathTile { IsQueue = true, SurfaceReference = 77 };

    var placed = park.TryPlacePath(1, 1, terrain, replacement);
    var mesh = PathMeshBuilder.Build(park, terrain, Vector4.UnitX, Vector4.UnitY);

    using (Assert.EnterMultipleScope()) {
      Assert.That(placed, Is.True);
      Assert.That(park.Paths[(1, 1)].SurfaceReference, Is.EqualTo(77));
      Assert.That(park.PathPlacements, Has.Count.EqualTo(2));
      Assert.That(park.PathPlacements.Count(path => path.Tile.Raised), Is.EqualTo(1));
      Assert.That(park.PathPlacements.Count(path => path.Tile.IsQueue), Is.EqualTo(1));
      Assert.That(mesh.Vertices, Has.Count.EqualTo(8));
      Assert.That(mesh.Vertices.Take(4).All(vertex => vertex.Color == Vector4.UnitY), Is.True);
      Assert.That(mesh.Vertices.Skip(4).All(vertex => vertex.Color == Vector4.UnitX), Is.True);
    }
  }

  [Test]
  public void Load_RejectsStructurePathTypeMismatch() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    var source = new DatPathTileData(1, 1, 0, 2, 1, 0, 0, 0);

    Assert.Throws<InvalidDataException>(new Action(() =>
      PathManagerLoader.Load(park, terrain, [source])));
  }

  [Test]
  public void Load_RejectsInvalidDirectionAndSlope() {
    var terrain = new Terrain(1, 1, 0);
    var invalidDirectionPark = new Park(buildableWidth: 1, buildableHeight: 1);
    var invalidSlopePark = new Park(buildableWidth: 1, buildableHeight: 1);

    using (Assert.EnterMultipleScope()) {
      Assert.Throws<InvalidDataException>(new Action(() =>
        PathManagerLoader.Load(
          invalidDirectionPark,
          terrain,
          [Flying(direction: 4, slopeType: 0)])));
      Assert.Throws<InvalidDataException>(new Action(() =>
        PathManagerLoader.Load(
          invalidSlopePark,
          terrain,
          [Flying(direction: 0, slopeType: 6)])));
    }
  }

  [Test]
  public void Load_RejectsUnsetDirectionOnSlopedPath() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);

    Assert.Throws<InvalidDataException>(new Action(() =>
      PathManagerLoader.Load(park, terrain, [Flying(direction: byte.MaxValue, slopeType: 1)])));
  }

  private static DatPathFlyingData Flying(
    byte direction,
    byte slopeType,
    int quantisedHeight = 0,
    int baseHeight = 0
  ) => new(
    entryId: 10,
    baseHeight,
    colIndex: 1,
    direction,
    pathType: 1,
    quantisedHeight,
    rowIndex: 1,
    sceneryItem: 0,
    slopeType,
    surface: 0,
    surfaceType: byte.MaxValue,
    undergroundFlag: null,
    boolValue: 0);
}
