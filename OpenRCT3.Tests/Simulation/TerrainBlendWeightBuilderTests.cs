// Terrain Blend Weight Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class TerrainBlendWeightBuilderTests {
  [Test]
  public void Build_BlendedCheckerboard_CollapsesToEqualLayersAtEveryCorner() {
    var terrain = NewTerrain(3, 3, (x, y) => Convert.ToByte((x + y) % 2 == 0 ? 8 : 30));

    var layers = TerrainBlendWeightBuilder.Build(
      terrain, 1, 1, _ => TerrainTypeKind.GroundBlended);

    Assert.That(layers, Is.EqualTo(new[] {
      new TerrainBlendLayer(8, 127, 127, 127, 127),
      new TerrainBlendLayer(30, 127, 127, 127, 127),
    }));
  }

  [Test]
  public void Build_DistinctFlatNeighbors_AssignsExactCornerContributorsInIndexOrder() {
    var terrain = NewTerrain(3, 3, (x, y) => Convert.ToByte((y * 3) + x));

    var layers = TerrainBlendWeightBuilder.Build(
      terrain, 1, 1, _ => TerrainTypeKind.GroundBlended);

    Assert.That(layers.Select(layer => layer.SurfaceIndex),
      Is.EqualTo(Enumerable.Range(0, 9).Select(Convert.ToByte)));
    Assert.That(layers, Is.EqualTo(new[] {
      new TerrainBlendLayer(0, 63, 0, 0, 0),
      new TerrainBlendLayer(1, 63, 63, 0, 0),
      new TerrainBlendLayer(2, 0, 63, 0, 0),
      new TerrainBlendLayer(3, 63, 0, 63, 0),
      new TerrainBlendLayer(4, 63, 63, 63, 63),
      new TerrainBlendLayer(5, 0, 63, 0, 63),
      new TerrainBlendLayer(6, 0, 0, 63, 0),
      new TerrainBlendLayer(7, 0, 0, 63, 63),
      new TerrainBlendLayer(8, 0, 0, 0, 63),
    }));
  }

  [Test]
  public void Build_DiagonalWithBrokenOuterLink_DoesNotCrossTheDiscontinuity() {
    var terrain = NewTerrain(3, 3, (x, y) => Convert.ToByte((y * 3) + x));
    terrain.SetCornerHeight(0, 1, TerrainCornerSlot.NorthWest, 1);

    var layers = TerrainBlendWeightBuilder.Build(
      terrain, 1, 1, _ => TerrainTypeKind.GroundBlended);

    Assert.That(layers.Any(layer => layer.SurfaceIndex == 6), Is.False);
    Assert.That(layers.Single(layer => layer.SurfaceIndex == 3).NorthWestWeight,
      Is.EqualTo(85));
    Assert.That(layers.Single(layer => layer.SurfaceIndex == 7).NorthWestWeight,
      Is.EqualTo(85));
    Assert.That(layers.Single(layer => layer.SurfaceIndex == 4).NorthWestWeight,
      Is.EqualTo(85));
  }

  [Test]
  public void Build_DetachedSharedEdge_DoesNotBlendAcrossTheCliff() {
    var terrain = NewTerrain(2, 1, (x, _) => Convert.ToByte(x + 1));
    terrain.SetCornerHeight(1, 0, TerrainCornerSlot.SouthWest, 1);

    var layers = TerrainBlendWeightBuilder.Build(
      terrain, 0, 0, _ => TerrainTypeKind.GroundBlended);

    Assert.That(layers, Is.EqualTo(new[] {
      new TerrainBlendLayer(1, 255, 255, 255, 255),
    }));
  }

  [Test]
  public void Build_UnblendedCurrentOrNeighbor_DoesNotBlend() {
    var terrain = NewTerrain(2, 1, (x, _) => Convert.ToByte(x + 1));

    var unblendedCurrent = TerrainBlendWeightBuilder.Build(
      terrain, 0, 0, _ => TerrainTypeKind.GroundUnblended);
    var unblendedNeighbor = TerrainBlendWeightBuilder.Build(
      terrain, 0, 0,
      surface => surface == 1
        ? TerrainTypeKind.GroundBlended
        : TerrainTypeKind.GroundUnblended);

    var expected = new[] { new TerrainBlendLayer(1, 255, 255, 255, 255) };
    Assert.That(unblendedCurrent, Is.EqualTo(expected));
    Assert.That(unblendedNeighbor, Is.EqualTo(expected));
  }

  [Test]
  public void Build_SameBlendedSurfaceOnEveryTile_ProducesOneOpaqueLayer() {
    var terrain = NewTerrain(3, 3, (_, _) => 8);

    var layers = TerrainBlendWeightBuilder.Build(
      terrain, 1, 1, _ => TerrainTypeKind.GroundBlended);

    Assert.That(layers, Is.EqualTo(new[] {
      new TerrainBlendLayer(8, 255, 255, 255, 255),
    }));
  }

  [Test]
  public void Build_MixedTileSurfaceIndices_FailsClosed() {
    var terrain = NewTerrain(1, 1, (_, _) => 8);
    var corner = terrain.GetCorner(0, 0, TerrainCornerSlot.NorthEast);
    corner.SurfaceIndex = 30;
    terrain.SetCorner(0, 0, TerrainCornerSlot.NorthEast, corner);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendWeightBuilder.Build(
        terrain, 0, 0, _ => TerrainTypeKind.GroundBlended)));

    Assert.That(exception!.Message, Does.Contain("mixed surface indices"));
  }

  [Test]
  public void Build_InvalidBoundsOrSurfaceKind_FailsClosed() {
    var terrain = NewTerrain(1, 1, (_, _) => 8);

    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      TerrainBlendWeightBuilder.Build(
        terrain, -1, 0, _ => TerrainTypeKind.GroundBlended)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      TerrainBlendWeightBuilder.Build(
        terrain, 0, 1, _ => TerrainTypeKind.GroundBlended)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendWeightBuilder.Build(
        terrain, 0, 0, _ => TerrainTypeKind.Cliff)));
  }

  private static Terrain NewTerrain(
    int width,
    int height,
    Func<int, int, byte> getSurface
  ) {
    var cells = new List<DatTerrainCell>(width * height);
    for (var y = 0; y < height; y++) {
      for (var x = 0; x < width; x++)
        cells.Add(new DatTerrainCell(0, 0, 0, 0, getSurface(x, y), 0));
    }
    return Terrain.FromData(new DatTerrainData(
      width, height, 0, 0, 4, 4, cells.ToArray()));
  }
}
