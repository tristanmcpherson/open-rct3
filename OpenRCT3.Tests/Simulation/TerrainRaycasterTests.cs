// TerrainRaycasterTests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using OpenCobra.GDK;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class TerrainRaycasterTests {
  private const float WorldPositionTolerance = 0.002f;
  private static readonly Vector2 ViewportSize = new(800f, 600f);
  private static readonly Vector2 ViewportCenter = ViewportSize / 2f;

  [Test]
  public void TryIntersect_CenterScreenFlatTile_ReturnsExactTileTriangleAndHeight() {
    var terrain = NewTerrain(2f, 2f, 2f, 2f);
    var camera = NewCamera(new Vector3(1f, 1f, 2f));

    var found = TerrainRaycaster.TryIntersect(
      terrain, camera.Value, ViewportCenter, ViewportSize, out var hit);

    Assert.That(found, Is.True);
    Assert.That(hit.TileX, Is.EqualTo(0));
    Assert.That(hit.TileY, Is.EqualTo(0));
    Assert.That(hit.Triangle, Is.EqualTo(TerrainTriangle.SouthWest));
    Assert.That(hit.Position.X, Is.EqualTo(1f).Within(WorldPositionTolerance));
    Assert.That(hit.Position.Y, Is.EqualTo(1f).Within(WorldPositionTolerance));
    Assert.That(hit.Position.Z, Is.EqualTo(2f).Within(WorldPositionTolerance));
  }

  [TestCase(1f, 1f, TerrainTriangle.SouthWest, 3f)]
  [TestCase(3f, 3f, TerrainTriangle.NorthEast, 9f)]
  public void TryIntersect_CenterScreenSlopedTile_ReturnsRenderedTriangleAndWorldHeight(
    float targetX,
    float targetY,
    TerrainTriangle expectedTriangle,
    float expectedZ
  ) {
    // These corners lie on z = x + 2y. The target cases sit on opposite sides of the rendered
    // SouthEast-to-NorthWest diagonal.
    var terrain = NewTerrain(0f, 4f, 8f, 12f);
    var camera = NewCamera(new Vector3(targetX, targetY, expectedZ));

    var found = TerrainRaycaster.TryIntersect(
      terrain, camera.Value, ViewportCenter, ViewportSize, out var hit);

    Assert.That(found, Is.True);
    Assert.That(hit.TileX, Is.EqualTo(0));
    Assert.That(hit.TileY, Is.EqualTo(0));
    Assert.That(hit.Triangle, Is.EqualTo(expectedTriangle));
    Assert.That(hit.Position.Z, Is.EqualTo(expectedZ).Within(WorldPositionTolerance));
  }

  [Test]
  public void TryIntersect_CenterScreen_RemainsStableAfterOrbitZoomAndAspectChange() {
    var terrain = NewTerrain(1f, 1f, 1f, 1f);
    var target = new Vector3(1f, 1f, 1f);
    var camera = NewCamera(target);
    camera.Orbit(MathF.PI / 3f);
    camera.OrbitElevation(-MathF.PI / 9f);
    camera.SetDistance(7f);
    var viewportSize = new Vector2(1200f, 500f);
    camera.Update(viewportSize.X / viewportSize.Y);

    var found = TerrainRaycaster.TryIntersect(
      terrain, camera.Value, viewportSize / 2f, viewportSize, out var hit);

    Assert.That(found, Is.True);
    Assert.That(hit.TileX, Is.EqualTo(0));
    Assert.That(hit.TileY, Is.EqualTo(0));
    Assert.That(
      hit.Position,
      Is.EqualTo(target).Using(Vector3ComparerWithTolerance(WorldPositionTolerance)));
  }

  [Test]
  public void TryIntersect_RayOutsideTerrain_ReturnsFalse() {
    var terrain = NewTerrain(0f, 0f, 0f, 0f);
    var camera = NewCamera(new Vector3(100f, 100f, 0f));

    var found = TerrainRaycaster.TryIntersect(
      terrain, camera.Value, ViewportCenter, ViewportSize, out _);

    Assert.That(found, Is.False);
  }

  [TestCase(float.NaN, 10f, 5f, 5f)]
  [TestCase(10f, float.PositiveInfinity, 5f, 5f)]
  [TestCase(10f, 10f, -1f, 5f)]
  [TestCase(10f, 10f, 10f, 5f)]
  public void TryIntersect_MalformedViewport_ReturnsFalse(
    float width,
    float height,
    float x,
    float y
  ) {
    var found = TerrainRaycaster.TryIntersect(
      NewTerrain(0f, 0f, 0f, 0f),
      Matrix4x4.Identity,
      new Vector2(x, y),
      new Vector2(width, height),
      out _);

    Assert.That(found, Is.False);
  }

  [Test]
  public void TryIntersect_NonFiniteOrSingularMatrix_ReturnsFalse() {
    var terrain = NewTerrain(0f, 0f, 0f, 0f);
    var nonFinite = Matrix4x4.Identity;
    nonFinite.M11 = float.NaN;

    Assert.That(TerrainRaycaster.TryIntersect(
      terrain, nonFinite, ViewportCenter, ViewportSize, out _), Is.False);
    Assert.That(TerrainRaycaster.TryIntersect(
      terrain, default, ViewportCenter, ViewportSize, out _), Is.False);
  }

  private static Camera NewCamera(Vector3 target) {
    var camera = new Camera();
    camera.Frame(target, 15f);
    camera.Update(ViewportSize.X / ViewportSize.Y);
    return camera;
  }

  private static Terrain NewTerrain(float southWest, float southEast, float northWest, float northEast)
    => Terrain.FromData(new DatTerrainData(
      1,
      1,
      0f,
      0f,
      4f,
      4f,
      [new DatTerrainCell(southWest, southEast, northWest, northEast, 0, 0)]));

  private static IEqualityComparer<Vector3> Vector3ComparerWithTolerance(float tolerance) =>
    new Vector3ToleranceComparer(tolerance);

  private sealed class Vector3ToleranceComparer(float tolerance) : IEqualityComparer<Vector3> {
    public bool Equals(Vector3 left, Vector3 right) =>
      Vector3.Distance(left, right) <= tolerance;

    public int GetHashCode(Vector3 value) => 0;
  }
}
