// Ride Track Placement Transform Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackPlacementTransformTests {
  [Test]
  public void Create_UsesTileCenterAndRawAbsoluteHeight() {
    var terrain = new Terrain(4, 5);
    var placement = Placement(tileX: 2, tileY: 3, serializedHeight: 17);
    var expectedAnchor = SceneryGeometryBuilder.CalculateTileAnchor(terrain, 2, 3);

    var transform = RideTrackPlacementTransform.Create(placement, terrain);

    AssertVector(
      Vector3.Transform(Vector3.Zero, transform),
      new Vector3(expectedAnchor, 17f));
  }

  [TestCase(0, Edge.West, -1f, 0f)]
  [TestCase(1, Edge.North, 0f, 1f)]
  [TestCase(2, Edge.East, 1f, 0f)]
  [TestCase(3, Edge.South, 0f, -1f)]
  public void Create_MapsLocalForwardThroughExactSceneryDirection(
    int direction,
    Edge rotation,
    float expectedX,
    float expectedY
  ) {
    var terrain = new Terrain(1, 1);
    var placement = Placement(direction: direction, rotation: rotation);
    var transform = RideTrackPlacementTransform.Create(placement, terrain);
    var origin = Vector3.Transform(Vector3.Zero, transform);

    var forward = Vector3.Transform(Vector3.UnitX, transform) - origin;

    AssertVector(forward, new Vector3(expectedX, expectedY, 0f));
  }

  [Test]
  public void Create_PreservesLocalUp() {
    var terrain = new Terrain(1, 1);
    var transform = RideTrackPlacementTransform.Create(
      Placement(direction: 1, rotation: Edge.North),
      terrain);
    var origin = Vector3.Transform(Vector3.Zero, transform);

    var up = Vector3.Transform(Vector3.UnitZ, transform) - origin;

    AssertVector(up, Vector3.UnitZ);
  }

  [Test]
  public void Create_RejectsDirectionRotationMismatch() {
    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackPlacementTransform.Create(
        Placement(direction: 1, rotation: Edge.East),
        new Terrain(1, 1))));

    Assert.That(exception!.Message, Does.Contain("does not match serialized direction 1"));
  }

  [Test]
  public void Create_RejectsInvalidDirection() {
    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackPlacementTransform.Create(
        Placement(direction: 4, rotation: Edge.East),
        new Terrain(1, 1))));

    Assert.That(exception!.Message, Does.Contain("serialized direction 4 is unsupported"));
  }

  [Test]
  public void Create_RejectsInvalidRotation() {
    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackPlacementTransform.Create(
        Placement(direction: 2, rotation: (Edge)99),
        new Terrain(1, 1))));

    Assert.That(exception!.Message, Does.Contain("rotation 99 is unsupported"));
  }

  [Test]
  public void Create_RejectsReversedPlacementUntilItsTransformIsProven() {
    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackPlacementTransform.Create(
        Placement(reversed: true),
        new Terrain(1, 1))));

    Assert.That(exception!.Message, Does.Contain("reversed geometry has no proven transform"));
  }

  [TestCase(1)]
  [TestCase(-45)]
  [TestCase(int.MaxValue)]
  public void Create_RejectsNonzeroUserAngleUntilItsTransformIsProven(int userAngleDegrees) {
    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackPlacementTransform.Create(
        Placement(userAngleDegrees: userAngleDegrees),
        new Terrain(1, 1))));

    Assert.That(exception!.Message, Does.Contain(
      $"user angle {userAngleDegrees} degrees has no proven transform"));
  }

  [Test]
  public void Create_RejectsPlacementOutsideTerrain() {
    var terrain = new Terrain(1, 1);
    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrackPlacementTransform.Create(
        Placement(tileX: terrain.Width, tileY: 0),
        terrain)));

    Assert.That(exception!.Message, Does.Contain("tile is outside the terrain grid"));
  }

  private static RideTrackPlacement Placement(
    int tileX = 1,
    int tileY = 1,
    int serializedHeight = 0,
    int direction = 2,
    Edge rotation = Edge.East,
    bool reversed = false,
    int userAngleDegrees = 0
  ) => new(
    sourceEntryId: 500,
    sceneryPlacementSourceEntryId: 100,
    sidDatabaseEntryReference: 200,
    symbolName: "Straight:tks",
    objectKey: "Straight",
    overlayPath: "Tracks\\Test",
    tileX,
    tileY,
    rotation,
    serializedDirection: direction,
    serializedHeight,
    corner: 0,
    ownerReference: 600,
    segmentReference: 700,
    previousPieceReference: 499,
    nextPieceReference: 501,
    platformPieceReference: 0,
    reversed,
    userAngleDegrees,
    flexiColour0: 1,
    flexiColour1: 2,
    flexiColour2: 3);

  private static void AssertVector(Vector3 actual, Vector3 expected) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.0001f));
      Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.0001f));
      Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.0001f));
    }
  }
}
