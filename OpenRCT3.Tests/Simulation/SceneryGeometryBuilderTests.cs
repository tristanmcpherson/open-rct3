// Scenery Geometry Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class SceneryGeometryBuilderTests {
  private const int TileX = 2;
  private const int TileY = 3;

  [Test]
  public void CalculateTileAnchor_UsesIsolatedTileCenterAssumption() {
    var terrain = new Terrain(4, 5);

    var anchor = SceneryGeometryBuilder.CalculateTileAnchor(terrain, TileX, TileY);

    Assert.That(anchor, Is.EqualTo(new Vector2(
      terrain.Origin.X + ((TileX + 0.5f) * terrain.TileSize.X),
      terrain.Origin.Y + ((TileY + 0.5f) * terrain.TileSize.Y))));
  }

  [TestCase(SidPosition.TileFull, 0, 0, 2, 4, 1f, 2f)]
  [TestCase(SidPosition.TileFull, 1, 0, 2, 4, 2f, 1f)]
  [TestCase(SidPosition.TileFull, 2, 0, 2, 4, 1f, 2f)]
  [TestCase(SidPosition.TileFull, 3, 0, 2, 4, 2f, 1f)]
  [TestCase(SidPosition.PathCenter, 0, 0, 2, 4, 1f, 2f)]
  [TestCase(SidPosition.PathCenter, 1, 0, 2, 4, 2f, 1f)]
  [TestCase(SidPosition.PathCenter, 2, 0, 2, 4, 1f, 2f)]
  [TestCase(SidPosition.PathCenter, 3, 0, 2, 4, 2f, 1f)]
  [TestCase(SidPosition.PathEdgeInner, 0, 0, 1, 1, 0.15f, 0.5f)]
  [TestCase(SidPosition.PathEdgeInner, 1, 0, 1, 1, 0.5f, 0.85f)]
  [TestCase(SidPosition.PathEdgeInner, 2, 0, 1, 1, 0.85f, 0.5f)]
  [TestCase(SidPosition.PathEdgeInner, 3, 0, 1, 1, 0.5f, 0.15f)]
  [TestCase(SidPosition.PathEdgeOuter, 0, 0, 1, 1, 0.05f, 0.5f)]
  [TestCase(SidPosition.PathEdgeOuter, 1, 0, 1, 1, 0.5f, 0.95f)]
  [TestCase(SidPosition.PathEdgeOuter, 2, 0, 1, 1, 0.95f, 0.5f)]
  [TestCase(SidPosition.PathEdgeOuter, 3, 0, 1, 1, 0.5f, 0.05f)]
  [TestCase(SidPosition.Wall, 0, 0, 1, 1, 0f, 0.5f)]
  [TestCase(SidPosition.Wall, 1, 0, 1, 1, 0.5f, 1f)]
  [TestCase(SidPosition.Wall, 2, 0, 1, 1, 1f, 0.5f)]
  [TestCase(SidPosition.Wall, 3, 0, 1, 1, 0.5f, 0f)]
  [TestCase(SidPosition.TileQuarter, 0, 0, 1, 1, 0.25f, 0.75f)]
  [TestCase(SidPosition.TileQuarter, 0, 1, 1, 1, 0.75f, 0.75f)]
  [TestCase(SidPosition.TileQuarter, 0, 2, 1, 1, 0.25f, 0.25f)]
  [TestCase(SidPosition.TileQuarter, 0, 3, 1, 1, 0.75f, 0.25f)]
  [TestCase(SidPosition.TileHalf, 0, 0, 1, 1, 0.25f, 0.5f)]
  [TestCase(SidPosition.TileHalf, 1, 0, 1, 1, 0.5f, 0.75f)]
  [TestCase(SidPosition.TileHalf, 2, 0, 1, 1, 0.75f, 0.5f)]
  [TestCase(SidPosition.TileHalf, 3, 0, 1, 1, 0.5f, 0.25f)]
  [TestCase(SidPosition.Corner, 0, 0, 1, 1, 0f, 0.5f)]
  [TestCase(SidPosition.Corner, 1, 0, 1, 1, 0.5f, 1f)]
  [TestCase(SidPosition.Corner, 2, 0, 1, 1, 1f, 0.5f)]
  [TestCase(SidPosition.Corner, 3, 0, 1, 1, 0.5f, 0f)]
  [TestCase(SidPosition.PathEdgeJoin, 0, 0, 1, 1, 0.05f, 0.5f)]
  [TestCase(SidPosition.PathEdgeJoin, 1, 0, 1, 1, 0.5f, 0.95f)]
  [TestCase(SidPosition.PathEdgeJoin, 2, 0, 1, 1, 0.95f, 0.5f)]
  [TestCase(SidPosition.PathEdgeJoin, 3, 0, 1, 1, 0.5f, 0.05f)]
  public void Build_UsesExecutablePositionTypeAnchor(
    SidPosition position,
    int direction,
    int corner,
    int squaresX,
    int squaresZ,
    float expectedU,
    float expectedV
  ) {
    var terrain = new Terrain();
    var placement = Placement("Item", serializedHeight: 0) with {
      SerializedDirection = direction,
      Corner = corner,
      ForceAbsoluteHeight = true
    };
    var item = Item(
      "Item",
      ["Visual:svd"],
      position,
      squaresX: Convert.ToUInt32(squaresX),
      squaresZ: Convert.ToUInt32(squaresZ));

    var positionResult = BuildFirstPosition(terrain, placement, item);

    AssertPosition(positionResult, new Vector3(
      terrain.Origin.X + ((TileX + expectedU) * terrain.TileSize.X),
      terrain.Origin.Y + ((TileY + expectedV) * terrain.TileSize.Y),
      0f));
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  public void Build_QuarterCornerAnchorDoesNotRotateWithDirection(int direction) {
    var terrain = new Terrain();
    var placement = Placement("Item", serializedHeight: 0) with {
      SerializedDirection = direction,
      Corner = 1,
      ForceAbsoluteHeight = true
    };
    var item = Item("Item", ["Visual:svd"], SidPosition.TileQuarter);

    var position = BuildFirstPosition(terrain, placement, item);

    AssertPosition(position, new Vector3(
      terrain.Origin.X + ((TileX + 0.75f) * terrain.TileSize.X),
      terrain.Origin.Y + ((TileY + 0.75f) * terrain.TileSize.Y),
      0f));
  }

  [Test]
  public void Build_VisualRefsAndShapeLodsUseFirstSupportedSerializedEntries() {
    var terrain = new Terrain();
    var park = ParkWith(Placement("Item"));
    var billboard = Visual(
      "Billboard",
      Lod("billboard", SvdLodType.Billboard));
    var boneLod = Lod(
      "bones", SvdLodType.BoneShape, boneShapeRef: "Bones:bsh");
    var nearLod = Lod("near", SvdLodType.StaticShape, "Near:shs");
    var farLod = Lod("far", SvdLodType.StaticShape, "Far:shs");
    var threeDimensional = Visual(
      "ThreeDimensional",
      boneLod,
      nearLod,
      farLod);
    var alternateLod = Lod("alternate", SvdLodType.StaticShape, "Alternate:shs");
    var alternate = Visual("Alternate", alternateLod);
    var item = Item(
      "Item",
      ["Billboard:svd", "ThreeDimensional:svd", "Alternate:svd"]);
    var resolved = new ResolvedSceneryObject(item, [
      new ResolvedSceneryStaticLod(threeDimensional, nearLod, Shape("Near", "Near:ftx")),
      new ResolvedSceneryStaticLod(threeDimensional, farLod, Shape("Far", "Far:ftx")),
      new ResolvedSceneryStaticLod(alternate, alternateLod, Shape("Alternate", "Alt:ftx")),
    ]) {
      BoneLods = [
        new ResolvedSceneryBoneLod(
          threeDimensional, boneLod, BoneShape("Bones", "Bone:ftx"))
      ]
    };

    var result = SceneryGeometryBuilder.Build(park, terrain, _ => resolved);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Batches, Has.Count.EqualTo(1));
      Assert.That(result.Batches[0].Key.FtxRef, Is.EqualTo("Bone:ftx"));
      Assert.That(result.VertexCount, Is.EqualTo(3));
      Assert.That(result.RenderedPlacementCount, Is.EqualTo(1));
      Assert.That(result.UnsupportedVisualPlacementCount, Is.Zero);
    }
  }

  [TestCase(0, -1f, -2f, -1f, -2f)]
  [TestCase(1, -2f, 1f, -2f, 1f)]
  [TestCase(2, 1f, 2f, 1f, 2f)]
  [TestCase(3, 2f, -1f, 2f, -1f)]
  public void Build_SerializedDirectionAppliesExecutableQuarterTurn(
    int direction,
    float expectedX,
    float expectedY,
    float expectedNormalX,
    float expectedNormalY
  ) {
    var terrain = new Terrain();
    var placement = Placement("Item", Edge.South, serializedHeight: 0) with {
      SerializedDirection = direction,
      ForceAbsoluteHeight = true
    };
    var park = ParkWith(placement);
    var lod = Lod("lod", SvdLodType.StaticShape, "Shape:shs");
    var visual = Visual("Visual", lod);
    var shape = Shape(
      "Shape",
      "Texture:ftx",
      OpenVertex(
        new Vector3(1f, 2f, 3f),
        new Vector3(1f, 2f, 0f)));
    var resolved = Resolved(Item("Item", ["Visual:svd"]), visual, lod, shape);
    var anchor = SceneryGeometryBuilder.CalculateTileAnchor(terrain, TileX, TileY);

    var vertex = SceneryGeometryBuilder.Build(park, terrain, _ => resolved)
      .Batches.Single().Mesh.Vertices[0];

    using (Assert.EnterMultipleScope()) {
      Assert.That(vertex.Position,
        Is.EqualTo(new Vector3(anchor.X + expectedX, anchor.Y + expectedY, 3f)));
      Assert.That(vertex.Normal,
        Is.EqualTo(new Vector3(expectedNormalX, expectedNormalY, 0f)));
    }
  }

  [TestCase(0, -1f, -3f, -4f, -6f)]
  [TestCase(1, -3f, 1f, -6f, 4f)]
  [TestCase(2, 1f, 3f, 4f, 6f)]
  [TestCase(3, 3f, -1f, 6f, -4f)]
  public void Build_RawNativeVertexUsesParkAxesAndExecutableYaw(
    int direction,
    float expectedX,
    float expectedY,
    float expectedNormalX,
    float expectedNormalY
  ) {
    var terrain = new Terrain();
    var placement = Placement("Item", Edge.South, serializedHeight: 0) with {
      SerializedDirection = direction,
      ForceAbsoluteHeight = true
    };
    var park = ParkWith(placement);
    var lod = Lod("lod", SvdLodType.StaticShape, "Shape:shs");
    var visual = Visual("Visual", lod);
    var rawVertex = new StaticShapeVertex(
      new Vector3(1f, 2f, 3f),
      new Vector3(4f, 5f, 6f),
      Vector2.Zero,
      Vector4.One);
    var shape = Shape("Shape", "Texture:ftx", rawVertex);
    var resolved = Resolved(Item("Item", ["Visual:svd"]), visual, lod, shape);
    var anchor = SceneryGeometryBuilder.CalculateTileAnchor(terrain, TileX, TileY);

    var vertex = SceneryGeometryBuilder.Build(park, terrain, _ => resolved)
      .Batches.Single().Mesh.Vertices[0];

    using (Assert.EnterMultipleScope()) {
      Assert.That(vertex.Position,
        Is.EqualTo(new Vector3(anchor.X + expectedX, anchor.Y + expectedY, 2f)));
      Assert.That(vertex.Normal,
        Is.EqualTo(new Vector3(expectedNormalX, expectedNormalY, 5f)));
    }
  }

  [Test]
  public void Build_NonzeroSidPositionAndScaleVariationDoNotInventVisualTransform() {
    var terrain = new Terrain();
    var placement = Placement("Item", Edge.West, serializedHeight: 7) with {
      HeightAdjust = 0.25f,
      ForceAbsoluteHeight = true
    };
    var park = ParkWith(placement);
    var texCoord = new Vector2(0.25f, 0.75f);
    var color = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);
    var lod = Lod("lod", SvdLodType.StaticShape, "Shape:shs");
    var visual = Visual("Visual", 0.4f, lod);
    var item = Item(
      "Item",
      ["Visual:svd"],
      positionX: 10f,
      positionY: 20f,
      positionZ: 30f);
    var shape = Shape(
      "Shape",
      "Texture:ftx",
      OpenVertex(
        new Vector3(1f, 2f, 3f),
        new Vector3(4f, 5f, 6f),
        texCoord,
        color));
    var resolved = Resolved(item, visual, lod, shape);
    var anchor = SceneryGeometryBuilder.CalculateTileAnchor(terrain, TileX, TileY);

    var vertex = SceneryGeometryBuilder.Build(park, terrain, _ => resolved)
      .Batches.Single().Mesh.Vertices[0];

    using (Assert.EnterMultipleScope()) {
      Assert.That(vertex.Position,
        Is.EqualTo(new Vector3(anchor.X - 1f, anchor.Y - 2f, 10.25f)));
      Assert.That(vertex.Normal, Is.EqualTo(new Vector3(-4f, -5f, 6f)));
      Assert.That(vertex.TexCoord, Is.EqualTo(texCoord));
      Assert.That(vertex.Color, Is.EqualTo(color));
    }
  }

  [Test]
  public void Build_NoSerializedHeightSamplesTerrain() {
    var terrain = new Terrain();
    terrain.SetCornerHeight(TileX, TileY, TerrainCornerSlot.SouthWest, 100);
    terrain.SetCornerHeight(TileX, TileY, TerrainCornerSlot.SouthEast, 200);
    terrain.SetCornerHeight(TileX, TileY, TerrainCornerSlot.NorthWest, 300);
    terrain.SetCornerHeight(TileX, TileY, TerrainCornerSlot.NorthEast, 400);
    var park = ParkWith(Placement("Item"));
    var lod = Lod("lod", SvdLodType.StaticShape, "Shape:shs");
    var visual = Visual("Visual", lod);
    var shape = Shape("Shape", "Texture:ftx", OpenVertex(new Vector3(0f, 0f, 3f)));
    var resolved = Resolved(Item("Item", ["Visual:svd"]), visual, lod, shape);

    var position = SceneryGeometryBuilder.Build(park, terrain, _ => resolved)
      .Batches.Single().Mesh.Vertices[0].Position;

    Assert.That(position.Z, Is.EqualTo(6f).Within(0.0001f));
  }

  [TestCase(2, 1f)]
  [TestCase(1, 5f)]
  [TestCase(0, 2f)]
  public void Build_TerrainSurfaceSamplingUsesExecutableTriangleSplitAndCeiling(
    int corner,
    float expectedHeight
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, 100, 200, 800);
    var placement = Placement("Item") with { Corner = corner };
    var item = Item("Item", ["Visual:svd"], SidPosition.TileQuarter);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(expectedHeight).Within(0.0001f));
  }

  [Test]
  public void Build_RelativeTerrainSamplingCeilsNegativeFractionTowardPositiveInfinity() {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, -100, -200, -800);
    var placement = Placement("Item") with { Corner = 2 };
    var item = Item("Item", ["Visual:svd"], SidPosition.TileQuarter);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.Zero.Within(0.0001f));
  }

  [Test]
  public void Build_RelativeTerrainSamplingPreservesNegativeWholeUnit() {
    var terrain = new Terrain();
    SetCornerHeights(terrain, -100, -100, -100, -100);
    var placement = Placement("Item");
    var item = Item("Item", ["Visual:svd"]);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(-1f).Within(0.0001f));
  }

  [TestCase(SidPosition.TileFull, 0, 0, 2f)]
  [TestCase(SidPosition.TileHalf, 0, 0, 2f)]
  [TestCase(SidPosition.PathCenter, 0, 0, 2f)]
  public void Build_SurfacePositionTypesSampleAtTheirExactAnchor(
    SidPosition positionType,
    int direction,
    int corner,
    float expectedHeight
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, 100, 200, 800);
    var placement = Placement("Item") with {
      SerializedDirection = direction,
      Corner = corner
    };
    var item = Item("Item", ["Visual:svd"], positionType);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(expectedHeight).Within(0.0001f));
  }

  [TestCase(SidPosition.PathEdgeInner, 0, 2f)]
  [TestCase(SidPosition.PathEdgeInner, 1, 4f)]
  [TestCase(SidPosition.PathEdgeInner, 2, 3f)]
  [TestCase(SidPosition.PathEdgeInner, 3, 2f)]
  [TestCase(SidPosition.PathEdgeOuter, 0, 2f)]
  [TestCase(SidPosition.PathEdgeOuter, 1, 4f)]
  [TestCase(SidPosition.PathEdgeOuter, 2, 3f)]
  [TestCase(SidPosition.PathEdgeOuter, 3, 2f)]
  [TestCase(SidPosition.Wall, 0, 2f)]
  [TestCase(SidPosition.Wall, 1, 4f)]
  [TestCase(SidPosition.Wall, 2, 3f)]
  [TestCase(SidPosition.Wall, 3, 2f)]
  [TestCase(SidPosition.PathEdgeJoin, 0, 2f)]
  [TestCase(SidPosition.PathEdgeJoin, 1, 4f)]
  [TestCase(SidPosition.PathEdgeJoin, 2, 3f)]
  [TestCase(SidPosition.PathEdgeJoin, 3, 2f)]
  public void Build_EdgePositionTypesAverageTheSelectedRawDirectionEdge(
    SidPosition positionType,
    int direction,
    float expectedHeight
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 100, 200, 300, 400);
    var placement = Placement("Item") with { SerializedDirection = direction };
    var item = Item("Item", ["Visual:svd"], positionType);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(expectedHeight).Within(0.0001f));
  }

  [TestCase(-100, -1f)]
  [TestCase(-50, 0f)]
  public void Build_NegativeEdgeHeightConvertsCornersBeforeCeiling(
    int northWest,
    float expectedHeight
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, -100, 0, northWest, 0);
    var placement = Placement("Item") with { SerializedDirection = 0 };
    var item = Item("Item", ["Visual:svd"], SidPosition.Wall);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(expectedHeight).Within(0.0001f));
  }

  [TestCase(0, 3f)]
  [TestCase(1, 4f)]
  [TestCase(2, 2f)]
  [TestCase(3, 1f)]
  public void Build_CornerPositionUsesOneRawDirectionTerrainCorner(
    int direction,
    float expectedHeight
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 100, 200, 300, 400);
    var placement = Placement("Item") with { SerializedDirection = direction };
    var item = Item("Item", ["Visual:svd"], SidPosition.Corner);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(expectedHeight).Within(0.0001f));
  }

  [TestCase(-100, -1f)]
  [TestCase(-50, 0f)]
  public void Build_NegativeCornerHeightConvertsCornerBeforeCeiling(
    int northWest,
    float expectedHeight
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, 0, northWest, 0);
    var placement = Placement("Item") with { SerializedDirection = 0 };
    var item = Item("Item", ["Visual:svd"], SidPosition.Corner);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(expectedHeight).Within(0.0001f));
  }

  [Test]
  public void Build_RelativeHeightUsesHeightOffsetAndAdjustmentAfterGroundCeiling() {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, 100, 200, 800);
    var placement = Placement("Item", serializedHeight: 99) with {
      Corner = 2,
      HeightOffset = 7,
      HeightAdjust = 0.25f
    };
    var item = Item("Item", ["Visual:svd"], SidPosition.TileQuarter);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(8.25f).Within(0.0001f));
  }

  [TestCase(0, 100, 200, 800, 0.75f)]
  [TestCase(0, -100, -200, -800, -0.75f)]
  public void Build_SmoothHeightPreservesRawRelativeTerrainSample(
    int southWest,
    int southEast,
    int northWest,
    int northEast,
    float expectedHeight
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, southWest, southEast, northWest, northEast);
    var placement = Placement("Item") with { Corner = 2 };
    var item = Item(
      "Item",
      ["Visual:svd"],
      SidPosition.TileQuarter,
      flags: SidFlags.SmoothHeight);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(expectedHeight).Within(0.0001f));
  }

  [Test]
  public void Build_WildSmoothFullTileFenceUsesFourCornerAverage() {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, 100, 200, 800);
    var placement = Placement("Item");
    var item = Item(
      "Item",
      ["Visual:svd"],
      flags: SidFlags.SmoothHeight | SidFlags.Fence,
      structureVersion: 2);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(2.75f).Within(0.0001f));
  }

  [TestCase(0, 1f)]
  [TestCase(1, 0f)]
  [TestCase(2, 3f)]
  [TestCase(3, 4f)]
  public void Build_FullTileSmoothFenceFollowsPhysicalEastRisingPlane(
    int direction,
    float expectedZ
  ) {
    var terrain = new Terrain();
    // Raw 400 means four world metres. Every direction must retain the same physical east-rising
    // terrain plane after its executable quarter-turn.
    SetCornerHeights(terrain, 0, 400, 0, 400);
    var placement = Placement("Item") with { SerializedDirection = direction };
    var item = Item(
      "Item",
      ["Visual:svd"],
      flags: SidFlags.SmoothHeight | SidFlags.Fence,
      structureVersion: 2);

    var vertex = BuildFirstVertex(
      terrain,
      placement,
      item,
      new Vector3(1f, 2f, 0f),
      new Vector3(0f, 0f, 2f));

    using (Assert.EnterMultipleScope()) {
      Assert.That(vertex.Position.Z, Is.EqualTo(expectedZ).Within(0.0001f));
      AssertPosition(
        vertex.Normal,
        Vector3.Normalize(new Vector3(-2f, 0f, 2f)));
    }
  }

  [TestCase(0, 0f, 0f, -1f, 2f)]
  [TestCase(1, 2f, -1f, 0f, 2f)]
  [TestCase(2, 4f, 0f, -1.5f, 2f)]
  [TestCase(3, 1f, -0.5f, 0f, 2f)]
  public void Build_RevisedSmoothFenceEdgeAppliesExecutableSlopeAndNormal(
    int direction,
    float expectedZ,
    float expectedNormalX,
    float expectedNormalY,
    float expectedNormalZ
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, 100, 200, 400);
    var placement = Placement("Item") with { SerializedDirection = direction };
    var item = Item(
      "Item",
      ["Visual:svd"],
      SidPosition.Wall,
      SidFlags.SmoothHeight | SidFlags.Fence,
      structureVersion: 2);

    var vertex = BuildFirstVertex(
      terrain,
      placement,
      item,
      new Vector3(1f, 2f, 0f),
      new Vector3(0f, 0f, 2f));

    using (Assert.EnterMultipleScope()) {
      Assert.That(vertex.Position.Z, Is.EqualTo(expectedZ).Within(0.0001f));
      AssertPosition(
        vertex.Normal,
        Vector3.Normalize(new Vector3(
          expectedNormalX,
          expectedNormalY,
          expectedNormalZ)));
    }
  }

  [TestCase(0, -0.5f, -1f, -1f, 2f)]
  [TestCase(1, 2.75f, -1f, -1.5f, 2f)]
  [TestCase(2, 4.25f, -0.5f, -1.5f, 2f)]
  [TestCase(3, 0.5f, -0.5f, -1f, 2f)]
  public void Build_RevisedSmoothFenceCornerAppliesExecutableSlopeAndNormal(
    int direction,
    float expectedZ,
    float expectedNormalX,
    float expectedNormalY,
    float expectedNormalZ
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, 100, 200, 400);
    var placement = Placement("Item") with { SerializedDirection = direction };
    var item = Item(
      "Item",
      ["Visual:svd"],
      SidPosition.Corner,
      SidFlags.SmoothHeight | SidFlags.Fence,
      structureVersion: 2);

    var vertex = BuildFirstVertex(
      terrain,
      placement,
      item,
      new Vector3(1f, 2f, 0f),
      new Vector3(0f, 0f, 2f));

    using (Assert.EnterMultipleScope()) {
      Assert.That(vertex.Position.Z, Is.EqualTo(expectedZ).Within(0.0001f));
      AssertPosition(
        vertex.Normal,
        Vector3.Normalize(new Vector3(
          expectedNormalX,
          expectedNormalY,
          expectedNormalZ)));
    }
  }

  [TestCase(SidPosition.Wall, 0, SidFlags.SmoothHeight, 0f)]
  [TestCase(SidPosition.Wall, 2, SidFlags.SmoothHeight, 1f)]
  [TestCase(
    SidPosition.Wall,
    2,
    SidFlags.SmoothHeight | SidFlags.Skew,
    1f)]
  [TestCase(SidPosition.PathEdgeInner, 0, SidFlags.SmoothHeight, 1f)]
  [TestCase(SidPosition.Corner, 0, SidFlags.SmoothHeight, 2f)]
  public void Build_SmoothSlopeUsesExactVersionPositionAndFenceGate(
    SidPosition positionType,
    int structureVersion,
    SidFlags flags,
    float expectedZ
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, 100, 200, 400);
    var placement = Placement("Item") with { SerializedDirection = 0 };
    var item = Item(
      "Item",
      ["Visual:svd"],
      positionType,
      flags,
      Convert.ToUInt16(structureVersion));

    var position = BuildFirstPosition(
      terrain, placement, item, new Vector3(0f, 2f, 0f));

    Assert.That(position.Z, Is.EqualTo(expectedZ).Within(0.0001f));
  }

  [TestCase(true, (SidFlags)0)]
  [TestCase(false, SidFlags.GroundChange)]
  public void Build_AbsoluteHeightBypassesSmoothSlopeAndNormalTransform(
    bool forceAbsoluteHeight,
    SidFlags absoluteFlag
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, 400, 0, 400);
    var placement = Placement("Item", serializedHeight: 7) with {
      ForceAbsoluteHeight = forceAbsoluteHeight
    };
    var item = Item(
      "Item",
      ["Visual:svd"],
      flags: SidFlags.SmoothHeight | SidFlags.Fence | absoluteFlag,
      structureVersion: 2);

    var vertex = BuildFirstVertex(
      terrain,
      placement,
      item,
      new Vector3(1f, 0f, 0f),
      new Vector3(0f, 0f, 2f));

    using (Assert.EnterMultipleScope()) {
      Assert.That(vertex.Position.Z, Is.EqualTo(7f).Within(0.0001f));
      Assert.That(vertex.Normal, Is.EqualTo(new Vector3(0f, 0f, 2f)));
    }
  }

  [Test]
  public void Build_SmoothSlopePreservesZeroNormal() {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, 400, 0, 400);
    var placement = Placement("Item");
    var item = Item(
      "Item",
      ["Visual:svd"],
      flags: SidFlags.SmoothHeight | SidFlags.Fence,
      structureVersion: 2);

    var vertex = BuildFirstVertex(
      terrain,
      placement,
      item,
      new Vector3(1f, 0f, 0f),
      Vector3.Zero);

    Assert.That(vertex.Normal, Is.EqualTo(Vector3.Zero));
  }

  [Test]
  public void Build_SmoothSlopePositionOverflowFailsClosed() {
    var terrain = new Terrain();
    SetCornerHeights(terrain, int.MinValue, int.MaxValue, int.MinValue, int.MaxValue);
    var placement = Placement("Item");
    var item = Item(
      "Item",
      ["Visual:svd"],
      flags: SidFlags.SmoothHeight | SidFlags.Fence,
      structureVersion: 2);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      BuildFirstPosition(
        terrain,
        placement,
        item,
        new Vector3(float.MaxValue, 0f, 0f))));

    Assert.That(exception!.Message, Does.Contain("transforms to a non-finite value"));
  }

  [TestCase(false, SidFlags.GroundChange)]
  [TestCase(true, (SidFlags)0)]
  public void Build_AbsoluteHeightBypassesTerrain(
    bool forceAbsoluteHeight,
    SidFlags flags
  ) {
    var terrain = new Terrain();
    SetCornerHeights(terrain, 0, 100, 200, 800);
    var placement = Placement("Item", serializedHeight: 7) with {
      Corner = 2,
      HeightAdjust = 0.25f,
      ForceAbsoluteHeight = forceAbsoluteHeight
    };
    var item = Item(
      "Item", ["Visual:svd"], SidPosition.TileQuarter, flags: flags);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(7.25f).Within(0.0001f));
  }

  [Test]
  public void Build_MultiTileHeightSamplesTheRotatedFootprintCenterTile() {
    var terrain = new Terrain();
    terrain.SetCornerHeight(
      TileX + 2, TileY + 1, TerrainCornerSlot.SouthWest, 250);
    var placement = Placement("Item") with { SerializedDirection = 1 };
    var item = Item(
      "Item",
      ["Visual:svd"],
      SidPosition.TileFull,
      squaresX: 2,
      squaresZ: 4);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.EqualTo(3f).Within(0.0001f));
  }

  [Test]
  public void Build_NewPlacementDerivesSceneryDirectionFromRotation() {
    var terrain = new Terrain();
    var placement = new SceneryPlacement(
      "Item", TileX, TileY, Edge.North, serializedHeight: 0) {
      ForceAbsoluteHeight = true
    };
    var item = Item("Item", ["Visual:svd"]);
    var localPosition = new Vector3(1f, 2f, 0f);
    var anchor = SceneryGeometryBuilder.CalculateTileAnchor(terrain, TileX, TileY);

    var position = BuildFirstPosition(terrain, placement, item, localPosition);

    AssertPosition(position, new Vector3(anchor.X - 2f, anchor.Y + 1f, 0f));
  }

  [Test]
  public void Build_InvalidSerializedDirectionFailsClosed() {
    var terrain = new Terrain();
    var placement = Placement("Item") with { SerializedDirection = 4 };
    var item = Item("Item", ["Visual:svd"]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      BuildFirstPosition(terrain, placement, item)));

    Assert.That(exception!.Message, Does.Contain("serialized direction 4"));
  }

  [Test]
  public void Build_InvalidQuarterCornerFailsClosed() {
    var terrain = new Terrain();
    var placement = Placement("Item") with { Corner = 4 };
    var item = Item("Item", ["Visual:svd"], SidPosition.TileQuarter);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      BuildFirstPosition(terrain, placement, item)));

    Assert.That(exception!.Message, Does.Contain("quarter corner 4"));
  }

  [TestCase(0u, 1u)]
  [TestCase(1u, 0u)]
  [TestCase(uint.MaxValue, 1u)]
  public void Build_InvalidFootprintFailsClosed(uint squaresX, uint squaresZ) {
    var terrain = new Terrain();
    var placement = Placement("Item");
    var item = Item(
      "Item", ["Visual:svd"], squaresX: squaresX, squaresZ: squaresZ);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      BuildFirstPosition(terrain, placement, item)));

    Assert.That(exception!.Message, Does.Contain("footprint dimensions"));
  }

  [Test]
  public void Build_RelativeFootprintCenterOutsideTerrainFailsClosed() {
    var terrain = new Terrain(2, 2);
    var placement = Placement("Item") with {
      TileX = terrain.Width - 1,
      TileY = terrain.Height - 1
    };
    var item = Item("Item", ["Visual:svd"], squaresX: 2, squaresZ: 2);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      BuildFirstPosition(terrain, placement, item)));

    Assert.That(exception!.Message, Does.Contain("terrain sample"));
  }

  [Test]
  public void Build_AbsoluteFootprintMayOverhangTerrainWhenItsAnchorIsFinite() {
    var terrain = new Terrain(2, 2);
    var placement = Placement("Item", serializedHeight: 0) with {
      TileX = terrain.Width - 1,
      TileY = terrain.Height - 1,
      ForceAbsoluteHeight = true
    };
    var item = Item("Item", ["Visual:svd"], squaresX: 2, squaresZ: 2);

    var position = BuildFirstPosition(terrain, placement, item);

    Assert.That(position.Z, Is.Zero);
  }

  [TestCase(float.NaN)]
  [TestCase(float.PositiveInfinity)]
  public void Build_NonFiniteHeightAdjustmentFailsClosed(float adjustment) {
    var terrain = new Terrain();
    var placement = Placement("Item") with { HeightAdjust = adjustment };
    var item = Item("Item", ["Visual:svd"]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      BuildFirstPosition(terrain, placement, item)));

    Assert.That(exception!.Message, Does.Contain("height adjustment is non-finite"));
  }

  [TestCase(true, (SidFlags)0)]
  [TestCase(false, SidFlags.GroundChange)]
  public void Build_AbsoluteHeightWithoutSerializedValueFailsClosed(
    bool forceAbsoluteHeight,
    SidFlags flags
  ) {
    var terrain = new Terrain();
    var placement = Placement("Item") with {
      ForceAbsoluteHeight = forceAbsoluteHeight
    };
    var item = Item("Item", ["Visual:svd"], flags: flags);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      BuildFirstPosition(terrain, placement, item)));

    Assert.That(exception!.Message, Does.Contain("without a serialized value"));
  }

  [Test]
  public void Build_TransformedPositionOverflowFailsClosed() {
    var terrain = new Terrain();
    var placement = Placement("Item", serializedHeight: int.MaxValue) with {
      ForceAbsoluteHeight = true,
      HeightAdjust = float.MaxValue
    };
    var item = Item("Item", ["Visual:svd"]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      BuildFirstPosition(
        terrain,
        placement,
        item,
        new Vector3(0f, 0f, float.MaxValue))));

    Assert.That(exception!.Message, Does.Contain("transforms to a non-finite value"));
  }

  [Test]
  public void Build_CachesShapeAdaptationAndKeepsCatalogAndExactFtxIdentity() {
    var terrain = new Terrain();
    var park = ParkWith(
      Placement("First") with { OverlayPath = "Style\\A\\objects" },
      Placement("Second") with { OverlayPath = "Style\\A\\objects" },
      Placement("Third") with { OverlayPath = "Style\\B\\objects" },
      Placement("Fourth") with { OverlayPath = "Style\\A\\objects" });
    var sharedShape = Shape("Shared", "Texture:ftx");
    var distinctCaseShape = Shape("Distinct", "texture:ftx");
    var first = ResolvedFor("First", sharedShape);
    var second = ResolvedFor("Second", sharedShape);
    var third = ResolvedFor("Third", sharedShape);
    var fourth = ResolvedFor("Fourth", distinctCaseShape);
    var adaptationCalls = 0;
    var adaptedMeshes = new List<Mesh>();
    IReadOnlyList<StaticShapeMeshBatch> Adapt(StaticShape shape) {
      adaptationCalls++;
      var batches = StaticShapeMeshBuilder.BuildBatches(shape);
      adaptedMeshes.AddRange(batches.Select(batch => batch.Mesh));
      return batches;
    }

    var result = SceneryGeometryBuilder.Build(
      park,
      terrain,
      placement => placement.ObjectKey switch {
        "First" => first,
        "Second" => second,
        "Third" => third,
        "Fourth" => fourth,
        _ => null,
      },
      Adapt,
      SceneryGeometryBuildLimits.Default);

    using (Assert.EnterMultipleScope()) {
      Assert.That(adaptationCalls, Is.EqualTo(2));
      Assert.That(result.Batches, Has.Count.EqualTo(3));
      Assert.That(result.Batches.Select(batch => batch.Key.OverlayPath), Is.EqualTo(new[] {
        "Style\\A\\objects", "Style\\B\\objects", "Style\\A\\objects"
      }));
      Assert.That(result.Batches.Select(batch => batch.Key.FtxRef), Is.EqualTo(new[] {
        "Texture:ftx", "Texture:ftx", "texture:ftx"
      }));
      Assert.That(result.Batches[0].Mesh.Indices,
        Is.EqualTo(new uint[] { 1, 0, 2, 4, 3, 5 }));
      Assert.That(result.Batches[0].Mesh.Vertices, Has.Count.EqualTo(6));
      Assert.That(result.SourceBatchInstanceCount, Is.EqualTo(4));
      Assert.That(adaptedMeshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
      Assert.That(result.Batches.Select(batch => batch.Mesh.State),
        Is.All.EqualTo(State.Uninitialized));
    }
  }

  [Test]
  public void Build_CachesBoneShapeAdaptationAndNeverUsesStaticAdapter() {
    var terrain = new Terrain();
    var park = ParkWith(Placement("First"), Placement("Second"));
    var sharedShape = BoneShape("Shared", "Texture:ftx");
    var first = BoneResolvedFor("First", sharedShape);
    var second = BoneResolvedFor("Second", sharedShape);
    var staticCalls = 0;
    var boneCalls = 0;
    var adaptedMeshes = new List<Mesh>();
    IReadOnlyList<StaticShapeMeshBatch> AdaptStatic(StaticShape shape) {
      staticCalls++;
      throw new AssertionException("Static adapter should not run for a bone shape.");
    }
    IReadOnlyList<StaticShapeMeshBatch> AdaptBone(BoneShape shape) {
      boneCalls++;
      var batches = BoneShapeMeshBuilder.BuildBatches(shape);
      adaptedMeshes.AddRange(batches.Select(batch => batch.Mesh));
      return batches;
    }

    var result = SceneryGeometryBuilder.Build(
      park,
      terrain,
      placement => placement.ObjectKey == "First" ? first : second,
      AdaptStatic,
      AdaptBone,
      SceneryGeometryBuildLimits.Default);

    using (Assert.EnterMultipleScope()) {
      Assert.That(staticCalls, Is.Zero);
      Assert.That(boneCalls, Is.EqualTo(1));
      Assert.That(result.Batches, Has.Count.EqualTo(1));
      Assert.That(result.Batches[0].Key.FtxRef, Is.EqualTo("Texture:ftx"));
      Assert.That(result.Batches[0].Mesh.Vertices, Has.Count.EqualTo(6));
      Assert.That(result.SourceBatchInstanceCount, Is.EqualTo(2));
      Assert.That(adaptedMeshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Build_PersistedAnimationStateKeepsRestPoseUntilSamplingIsProven() {
    var terrain = new Terrain();
    var shape = BoneShape("Shared", "Texture:ftx");
    var animation = new BoneAnimation(
      "Move",
      10f,
      [new BoneAnimationBone(
        "root",
        [new BoneAnimationKeyframe(0f, new Vector3(100, 200, 300))],
        [])]);
    var resolved = BoneResolvedFor("Item", shape, [animation]);
    var restPlacement = Placement("Item");
    var savedPlacement = restPlacement with {
      AnimationStates = [new SceneryAnimationState(true, 0, 5f, false)]
    };

    var rest = SceneryGeometryBuilder.Build(
      ParkWith(restPlacement), terrain, _ => resolved);
    var saved = SceneryGeometryBuilder.Build(
      ParkWith(savedPlacement), terrain, _ => resolved);

    Assert.That(
      saved.Batches.Single().Mesh.Vertices.Select(value => value.Position),
      Is.EqualTo(rest.Batches.Single().Mesh.Vertices.Select(value => value.Position)));
  }

  [Test]
  public void Build_FlexiColoursArePartOfOpaqueMaterialIdentity() {
    var terrain = new Terrain();
    var first = Placement("First") with {
      OverlayPath = "Style",
      FlexiColour0 = 1,
      FlexiColour1 = 2,
      FlexiColour2 = 3
    };
    var second = Placement("Second") with {
      TileX = TileX + 1,
      OverlayPath = "Style",
      FlexiColour0 = 1,
      FlexiColour1 = 2,
      FlexiColour2 = 3
    };
    var third = Placement("Third") with {
      TileY = TileY + 1,
      OverlayPath = "Style",
      FlexiColour0 = 4,
      FlexiColour1 = 5,
      FlexiColour2 = 6
    };
    var park = ParkWith(first, second, third);
    var shape = Shape("Shared", "Texture:ftx");

    var result = SceneryGeometryBuilder.Build(
      park,
      terrain,
      placement => ResolvedFor(placement.ObjectKey, shape));

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Batches, Has.Count.EqualTo(2));
      Assert.That(result.Batches.Select(batch => batch.Key.FlexiColours), Is.EqualTo(new[] {
        new SceneryFlexiColours(1, 2, 3),
        new SceneryFlexiColours(4, 5, 6)
      }));
      Assert.That(result.Batches[0].Mesh.Vertices, Has.Count.EqualTo(6));
      Assert.That(result.Batches[1].Mesh.Vertices, Has.Count.EqualTo(3));
    }
  }

  [Test]
  public void Build_InvalidFlexiColourGroupUsesTheDatDefaultTriplet() {
    var terrain = new Terrain();
    var placement = Placement("Item") with {
      OverlayPath = "Style",
      FlexiColour0 = -1,
      FlexiColour1 = 31,
      FlexiColour2 = 32
    };
    var park = ParkWith(placement);

    var result = SceneryGeometryBuilder.Build(
      park,
      terrain,
      _ => ResolvedFor("Item", Shape("Shape", "Texture:ftx")));

    Assert.That(
      result.Batches.Single().Key.FlexiColours,
      Is.EqualTo(new SceneryFlexiColours(1, 2, 3)));
  }

  [Test]
  public void Build_MaterialMetadataSeparatesBatchesInSourceOrder() {
    var terrain = new Terrain();
    var park = ParkWith(Placement("Item") with { OverlayPath = "Style\\A" });
    var lod = Lod("lod", SvdLodType.StaticShape, "Shape:shs");
    var visual = Visual("Visual", lod);
    var shape = new StaticShape(
      "Shape",
      Vector3.Zero,
      Vector3.One,
      [
        SourceMesh("opaque", "Texture:ftx", transparency: 0, textureFlags: 4, sides: 3),
        SourceMesh("alpha", "Texture:ftx", transparency: 2, textureFlags: 8, sides: 1),
      ],
      []);
    var resolved = Resolved(Item("Item", ["Visual:svd"]), visual, lod, shape);

    var result = SceneryGeometryBuilder.Build(park, terrain, _ => resolved);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Batches, Has.Count.EqualTo(2));
      Assert.That(result.Batches.Select(batch => batch.Key.Transparency),
        Is.EqualTo(new uint[] { 0, 2 }));
      Assert.That(result.Batches.Select(batch => batch.Key.TextureFlags),
        Is.EqualTo(new uint[] { 4, 8 }));
      Assert.That(result.Batches.Select(batch => batch.Key.Sides),
        Is.EqualTo(new uint[] { 3, 1 }));
    }
  }

  [Test]
  public void Build_ComplexTransparencyKeepsPlacementsInSeparateSortableBatches() {
    var terrain = new Terrain();
    var firstPlacement = Placement("First") with { OverlayPath = "Style" };
    var secondPlacement = Placement("Second") with {
      TileX = TileX + 1,
      OverlayPath = "Style"
    };
    var park = ParkWith(firstPlacement, secondPlacement);
    var shape = new StaticShape(
      "Glass",
      Vector3.Zero,
      Vector3.One,
      [SourceMesh("glass", "Glass:ftx", transparency: 2)],
      []);
    var first = ResolvedFor("First", shape);
    var second = ResolvedFor("Second", shape);

    var result = SceneryGeometryBuilder.Build(
      park,
      terrain,
      placement => placement.ObjectKey == "First" ? first : second);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Batches, Has.Count.EqualTo(2));
      Assert.That(result.Batches.Select(batch => batch.Key),
        Is.All.EqualTo(result.Batches[0].Key));
      Assert.That(result.Batches.Select(batch => batch.Mesh.Vertices.Count),
        Is.EqualTo(new[] { 3, 3 }));
      Assert.That(
        result.Batches[1].Mesh.Vertices[0].Position.X -
        result.Batches[0].Mesh.Vertices[0].Position.X,
        Is.EqualTo(terrain.TileSize.X).Within(0.0001f));
    }
  }

  [Test]
  public void Build_SkipsHiddenUnresolvedAndUnsupportedWithExactCounts() {
    var terrain = new Terrain();
    var park = ParkWith(
      Placement("Hidden") with { IsHidden = true },
      Placement("Missing"),
      Placement("Unsupported"),
      Placement("Rendered"));
    var unsupported = new ResolvedSceneryObject(
      Item("Unsupported", ["Billboard:svd"]),
      []);
    var rendered = ResolvedFor("Rendered", Shape("Shape", "Texture:ftx"));
    var lookupCalls = new List<string>();

    var result = SceneryGeometryBuilder.Build(park, terrain, placement => {
      lookupCalls.Add(placement.ObjectKey);
      return placement.ObjectKey switch {
        "Unsupported" => unsupported,
        "Rendered" => rendered,
        _ => null,
      };
    });

    using (Assert.EnterMultipleScope()) {
      Assert.That(lookupCalls, Is.EqualTo(new[] { "Missing", "Unsupported", "Rendered" }));
      Assert.That(result.PlacementCount, Is.EqualTo(4));
      Assert.That(result.HiddenPlacementCount, Is.EqualTo(1));
      Assert.That(result.UnresolvedPlacementCount, Is.EqualTo(1));
      Assert.That(result.ResolvedPlacementCount, Is.EqualTo(2));
      Assert.That(result.UnsupportedVisualPlacementCount, Is.EqualTo(1));
      Assert.That(result.RenderedPlacementCount, Is.EqualTo(1));
      Assert.That(result.SkippedPlacementCount, Is.EqualTo(3));
      Assert.That(result.VertexCount, Is.EqualTo(3));
      Assert.That(result.IndexCount, Is.EqualTo(3));
    }
  }

  [Test]
  public void Build_MissingFirstStaticLodFailsClosed() {
    var terrain = new Terrain();
    var park = ParkWith(Placement("Item"));
    var firstLod = Lod("first", SvdLodType.StaticShape, "First:shs");
    var secondLod = Lod("second", SvdLodType.StaticShape, "Second:shs");
    var visual = Visual("Visual", firstLod, secondLod);
    var resolved = new ResolvedSceneryObject(
      Item("Item", ["Visual:svd"]),
      [new ResolvedSceneryStaticLod(visual, secondLod, Shape("Second", "Texture:ftx"))]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryGeometryBuilder.Build(park, terrain, _ => resolved)));

    Assert.That(exception!.Message,
      Does.Contain("resolved 1 supported shape LODs, expected 2"));
  }

  [Test]
  public void Build_MixedShapeLodOrderMismatchFailsClosed() {
    var terrain = new Terrain();
    var park = ParkWith(Placement("Item"));
    var firstBoneLod = Lod(
      "first-bone", SvdLodType.BoneShape, boneShapeRef: "FirstBone:bsh");
    var staticLod = Lod("static", SvdLodType.StaticShape, "Static:shs");
    var secondBoneLod = Lod(
      "second-bone", SvdLodType.BoneShape, boneShapeRef: "SecondBone:bsh");
    var visual = Visual("Visual", firstBoneLod, staticLod, secondBoneLod);
    var resolved = new ResolvedSceneryObject(
      Item("Item", ["Visual:svd"]),
      [new ResolvedSceneryStaticLod(
        visual, staticLod, Shape("Static", "Static:ftx"))]) {
      BoneLods = [
        new ResolvedSceneryBoneLod(
          visual, secondBoneLod, BoneShape("SecondBone", "Second:ftx")),
        new ResolvedSceneryBoneLod(
          visual, firstBoneLod, BoneShape("FirstBone", "First:ftx"))
      ]
    };

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryGeometryBuilder.Build(park, terrain, _ => resolved)));

    Assert.That(exception!.Message,
      Does.Contain("bone LOD order does not match its serialized order"));
  }

  [Test]
  public void Build_InvalidMaterialMetadataFailsClosed() {
    var terrain = new Terrain();
    var park = ParkWith(Placement("Item"));
    var lod = Lod("lod", SvdLodType.StaticShape, "Shape:shs");
    var visual = Visual("Visual", lod);
    var shape = new StaticShape(
      "Shape",
      Vector3.Zero,
      Vector3.One,
      [SourceMesh("mesh", "Texture:ftx", transparency: 3)],
      []);
    var resolved = Resolved(Item("Item", ["Visual:svd"]), visual, lod, shape);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryGeometryBuilder.Build(park, terrain, _ => resolved)));

    Assert.That(exception!.Message, Does.Contain("invalid transparency 3"));
  }

  [TestCase(float.NaN)]
  [TestCase(float.PositiveInfinity)]
  [TestCase(-0.1f)]
  public void Build_InvalidScaleVariationFailsClosed(float scale) {
    var terrain = new Terrain();
    var park = ParkWith(Placement("Item"));
    var lod = Lod("lod", SvdLodType.StaticShape, "Shape:shs");
    var visual = Visual("Visual", scale, lod);
    var resolved = Resolved(
      Item("Item", ["Visual:svd"]),
      visual,
      lod,
      Shape("Shape", "Texture:ftx"));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryGeometryBuilder.Build(park, terrain, _ => resolved)));

    Assert.That(exception!.Message, Does.Contain("scale variation is invalid"));
  }

  [Test]
  public void Build_GeometryBudgetFailsBeforeAggregation() {
    var terrain = new Terrain();
    var park = ParkWith(Placement("Item"));
    var resolved = ResolvedFor("Item", Shape("Shape", "Texture:ftx"));
    var limits = SceneryGeometryBuildLimits.Default with { MaximumVertices = 2 };
    Mesh? adaptedMesh = null;
    IReadOnlyList<StaticShapeMeshBatch> Adapt(StaticShape shape) {
      var batches = StaticShapeMeshBuilder.BuildBatches(shape);
      adaptedMesh = batches.Single().Mesh;
      return batches;
    }

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      SceneryGeometryBuilder.Build(
        park,
        terrain,
        _ => resolved,
        Adapt,
        limits)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("vertices exceed the limit 2"));
      Assert.That(adaptedMesh?.State, Is.EqualTo(State.Disposed));
    }
  }

  private static Park ParkWith(params SceneryPlacement[] placements) {
    var park = new Park();
    park.SceneryPlacements.AddRange(placements);
    return park;
  }

  private static SceneryPlacement Placement(
    string objectKey,
    Edge rotation = Edge.West,
    int? serializedHeight = null
  ) {
    var placement = new SceneryPlacement(
      objectKey, TileX, TileY, rotation, serializedHeight) {
      SourceEntryId = 1
    };
    return placement;
  }

  private static Vector3 BuildFirstPosition(
    Terrain terrain,
    SceneryPlacement placement,
    SceneryItem item,
    Vector3? localPosition = null
  ) => BuildFirstVertex(terrain, placement, item, localPosition).Position;

  private static Vertex BuildFirstVertex(
    Terrain terrain,
    SceneryPlacement placement,
    SceneryItem item,
    Vector3? localPosition = null,
    Vector3? localNormal = null
  ) {
    var park = ParkWith(placement);
    var lod = Lod("lod", SvdLodType.StaticShape, "Shape:shs");
    var visual = Visual("Visual", lod);
    var shape = Shape(
      "Shape",
      "Texture:ftx",
      OpenVertex(localPosition ?? Vector3.Zero, localNormal));
    var resolved = Resolved(item, visual, lod, shape);
    return SceneryGeometryBuilder.Build(park, terrain, _ => resolved)
      .Batches.Single().Mesh.Vertices[0];
  }

  private static void SetCornerHeights(
    Terrain terrain,
    int southWest,
    int southEast,
    int northWest,
    int northEast
  ) {
    terrain.SetCornerHeight(TileX, TileY, TerrainCornerSlot.SouthWest, southWest);
    terrain.SetCornerHeight(TileX, TileY, TerrainCornerSlot.SouthEast, southEast);
    terrain.SetCornerHeight(TileX, TileY, TerrainCornerSlot.NorthWest, northWest);
    terrain.SetCornerHeight(TileX, TileY, TerrainCornerSlot.NorthEast, northEast);
  }

  private static void AssertPosition(Vector3 actual, Vector3 expected) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.0001f));
      Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.0001f));
      Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.0001f));
    }
  }

  private static ResolvedSceneryObject ResolvedFor(string itemName, StaticShape shape) {
    var lod = Lod("lod", SvdLodType.StaticShape, $"{shape.Name}:shs");
    var visual = Visual($"{itemName}Visual", lod);
    return Resolved(Item(itemName, [$"{visual.Name}:svd"]), visual, lod, shape);
  }

  private static ResolvedSceneryObject BoneResolvedFor(
    string itemName,
    BoneShape shape,
    IReadOnlyList<BoneAnimation>? animations = null
  ) {
    var lod = Lod(
      "lod", SvdLodType.BoneShape, boneShapeRef: $"{shape.Name}:bsh");
    var visual = Visual($"{itemName}Visual", lod);
    return new ResolvedSceneryObject(Item(itemName, [$"{visual.Name}:svd"]), []) {
      BoneLods = [new ResolvedSceneryBoneLod(visual, lod, shape) {
        Animations = animations ?? []
      }]
    };
  }

  private static ResolvedSceneryObject Resolved(
    SceneryItem item,
    SceneryItemVisual visual,
    SceneryItemVisualLod lod,
    StaticShape shape
  ) => new(item, [new ResolvedSceneryStaticLod(visual, lod, shape)]);

  private static SceneryItem Item(
    string name,
    IReadOnlyList<string> visualRefs,
    SidPosition positionType = SidPosition.TileFull,
    SidFlags flags = 0,
    ushort structureVersion = 0,
    uint squaresX = 1,
    uint squaresZ = 1,
    float positionX = 0f,
    float positionY = 0f,
    float positionZ = 0f
  ) => new(
    name,
    flags,
    positionType,
    structureVersion,
    squaresX,
    squaresZ,
    positionX,
    positionY,
    positionZ,
    4f,
    4f,
    4f,
    SidType.SceneryMisc,
    visualRefs);

  private static SceneryItemVisual Visual(
    string name,
    params SceneryItemVisualLod[] lods
  ) => Visual(name, 0f, lods);

  private static SceneryItemVisual Visual(
    string name,
    float scale,
    params SceneryItemVisualLod[] lods
  ) => new(name, 0, 0f, 1f, 0f, scale, lods, null);

  private static SceneryItemVisualLod Lod(
    string name,
    SvdLodType type,
    string? staticShapeRef = null,
    string? boneShapeRef = null
  ) => new(
    name,
    type,
    staticShapeRef,
    boneShapeRef,
    null,
    null,
    new SceneryVisualBillboardSettings(0f, 0f, 0f, 0f, 0f, 0f),
    100f,
    []);

  private static StaticShape Shape(
    string name,
    string ftxRef,
    StaticShapeVertex? firstVertex = null
  ) => new(
    name,
    Vector3.Zero,
    Vector3.One,
    [SourceMesh("mesh", ftxRef, firstVertex: firstVertex)],
    []);

  private static BoneShape BoneShape(string name, string ftxRef) => new(
    name,
    Vector3.Zero,
    Vector3.One,
    [new BoneShapeMesh(
      "mesh",
      0,
      ftxRef,
      "SIOpaque:txs",
      0,
      0,
      3,
      [
        BoneVertex(Vector3.Zero),
        BoneVertex(Vector3.UnitX),
        BoneVertex(Vector3.UnitY),
      ],
      new uint[] { 0, 1, 2 }) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = 3,
      LogicalIndexCount = 3
    }],
    []);

  private static StaticShapeMesh SourceMesh(
    string name,
    string ftxRef,
    uint transparency = 0,
    uint textureFlags = 0,
    uint sides = 3,
    StaticShapeVertex? firstVertex = null
  ) {
    var vertices = new[] {
      firstVertex ?? OpenVertex(Vector3.Zero),
      OpenVertex(Vector3.UnitX),
      OpenVertex(Vector3.UnitY),
    };
    return new StaticShapeMesh(
      name,
      0,
      ftxRef,
      "SIOpaque:txs",
      transparency,
      textureFlags,
      sides,
      vertices,
      new uint[] { 0, 1, 2 }) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = 3
    };
  }

  private static StaticShapeVertex OpenVertex(
    Vector3 openPosition,
    Vector3? openNormal = null,
    Vector2? texCoord = null,
    Vector4? color = null
  ) => new(
    // StaticShapeMeshBuilder applies (x, z, y); this is its exact inverse fixture transform.
    new Vector3(openPosition.X, openPosition.Z, openPosition.Y),
    new Vector3(
      openNormal?.X ?? 0f,
      openNormal?.Z ?? 1f,
      openNormal?.Y ?? 0f),
    texCoord ?? Vector2.Zero,
    color ?? Vector4.One);

  private static BoneShapeVertex BoneVertex(Vector3 position) => new(
    new Vector3(position.X, position.Z, position.Y),
    Vector3.UnitY,
    Vector2.Zero,
    Vector4.One,
    new BoneShapeSkinning(-1, -1, -1, -1, 0, 0, 0, 0));
}
