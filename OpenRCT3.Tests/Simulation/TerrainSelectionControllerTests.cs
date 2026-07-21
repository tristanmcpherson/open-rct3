// Terrain Selection Controller Tests
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class TerrainSelectionControllerTests {
  [Test]
  public void Select_PreservesExactRaycastHitAndReplacesCurrentSelection() {
    var controller = new TerrainSelectionController(3, 2);
    var firstHit = Hit(0, 0, TerrainTriangle.SouthWest, new(1f, 2f, 3f), 4f);
    var secondHit = Hit(2, 1, TerrainTriangle.NorthEast, new(5f, 6f, 7f), 8f);

    controller.Select(firstHit);
    var selected = controller.Select(secondHit);

    using (Assert.EnterMultipleScope()) {
      Assert.That(controller.HasSelection, Is.True);
      Assert.That(controller.Current, Is.EqualTo(selected));
      Assert.That(selected.Hit, Is.EqualTo(secondHit));
      Assert.That(selected.TileX, Is.EqualTo(2));
      Assert.That(selected.TileY, Is.EqualTo(1));
      Assert.That(selected.Triangle, Is.EqualTo(TerrainTriangle.NorthEast));
      Assert.That(selected.Position, Is.EqualTo(new Vector3(5f, 6f, 7f)));
      Assert.That(selected.Distance, Is.EqualTo(8f));
    }
  }

  [Test]
  public void Clear_IsIdempotentAndReportsWhetherStateChanged() {
    var controller = new TerrainSelectionController(1, 1);
    controller.Select(Hit(0, 0));

    var firstClear = controller.Clear();
    var secondClear = controller.Clear();

    using (Assert.EnterMultipleScope()) {
      Assert.That(firstClear, Is.True);
      Assert.That(secondClear, Is.False);
      Assert.That(controller.HasSelection, Is.False);
      Assert.That(controller.Current, Is.Null);
    }
  }

  [TestCase(0, 1)]
  [TestCase(-1, 1)]
  [TestCase(1, 0)]
  [TestCase(1, -1)]
  public void Constructor_RejectsNonPositiveTerrainBounds(int width, int height) {
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      _ = new TerrainSelectionController(width, height)));
  }

  [TestCase(-1, 0)]
  [TestCase(3, 0)]
  [TestCase(0, -1)]
  [TestCase(0, 2)]
  public void Select_RejectsOutOfBoundsTilesWithoutChangingCurrent(int tileX, int tileY) {
    var controller = SelectedController();
    var previous = controller.Current;

    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      controller.Select(Hit(tileX, tileY))));

    Assert.That(controller.Current, Is.EqualTo(previous));
  }

  [Test]
  public void Select_RejectsUnknownTriangleWithoutChangingCurrent() {
    var controller = SelectedController();
    var previous = controller.Current;

    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      controller.Select(Hit(0, 0, (TerrainTriangle)99))));

    Assert.That(controller.Current, Is.EqualTo(previous));
  }

  [TestCase(float.NaN, 0f)]
  [TestCase(float.PositiveInfinity, 0f)]
  [TestCase(0f, float.NaN)]
  [TestCase(0f, float.PositiveInfinity)]
  [TestCase(0f, -0.01f)]
  public void Select_RejectsMalformedGeometryWithoutChangingCurrent(
    float positionX,
    float distance
  ) {
    var controller = SelectedController();
    var previous = controller.Current;

    Assert.Throws<ArgumentException>(new Action(() =>
      controller.Select(Hit(0, 0, TerrainTriangle.SouthWest, new(positionX, 0f, 0f), distance))));

    Assert.That(controller.Current, Is.EqualTo(previous));
  }

  private static TerrainSelectionController SelectedController() {
    var controller = new TerrainSelectionController(3, 2);
    controller.Select(Hit(1, 1));
    return controller;
  }

  private static TerrainRaycastHit Hit(
    int tileX,
    int tileY,
    TerrainTriangle triangle = TerrainTriangle.SouthWest,
    Vector3 position = default,
    float distance = 0f
  ) => new(tileX, tileY, triangle, position, distance);
}
