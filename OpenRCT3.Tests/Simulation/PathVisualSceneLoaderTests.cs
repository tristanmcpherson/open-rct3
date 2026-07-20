// Path Visual Scene Loader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

using Texture = OpenCobra.GDK.Materials.Texture;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class PathVisualSceneLoaderTests {
  [Test]
  public void Load_SeparatesNativeShapeModelsFromFallbackModels() {
    var terrain = new Terrain(width: 4, height: 4);
    var park = new Park(terrain);
    park.PathPlacements.Add(new PathPlacement(0, 0, NativeSlopeTile()));
    park.PathPlacements.Add(new PathPlacement(2, 2, new PathTile {
      SurfaceSystemName = "missing",
    }));
    var texture = Texture("native");
    var resolver = new FakeResolver(Shape(Mesh()), texture);

    var result = PathVisualSceneLoader.Load(park, terrain, resolver);

    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.RenderedPlacementCount, Is.EqualTo(1));
        Assert.That(result.FallbackPlacementCount, Is.EqualTo(1));
        Assert.That(result.ShapeModelCount, Is.EqualTo(1));
        Assert.That(result.Models, Has.Count.EqualTo(2));
      }
    } finally {
      foreach (var model in result.Models) model.Dispose();
      texture.Dispose();
    }
  }

  [Test]
  public void Load_ReleasesNativeModelsAndMeshesWhenLaterMaterialFails() {
    var terrain = new Terrain(width: 2, height: 2);
    var park = new Park(terrain);
    park.PathPlacements.Add(new PathPlacement(0, 0, NativeSlopeTile()));
    var texture = Texture("owned");
    var resolver = new FakeResolver(Shape(Mesh()), texture);
    var validMesh = MeshGeometry();
    var invalidMesh = MeshGeometry();
    var batches = new[] {
      Batch("valid:ftx", "SIOpaque:txs", 0, sides: 1, mesh: validMesh),
      Batch("invalid:ftx", "SIOpaque:txs", 0, sides: 2, mesh: invalidMesh),
    };

    Assert.Throws<InvalidDataException>(new Action(() =>
      PathVisualSceneLoader.Load(park, terrain, resolver, _ => batches)));
    texture.Dispose();

    using (Assert.EnterMultipleScope()) {
      Assert.That(resolver.ShapeTextureRequests, Is.EqualTo(1));
      Assert.That(validMesh.State, Is.EqualTo(State.Disposed));
      Assert.That(invalidMesh.State, Is.EqualTo(State.Disposed));
      Assert.That(texture.State, Is.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void CreateMaterial_UsesPrimaryPtdTextureWithoutTileParitySwitching() {
    var texture = Texture("primary");
    var resolver = new FakeResolver(Shape(Mesh()), texture);
    var firstBatch = Batch(ftxRef: null, txsRef: "SIOpaque:txs", transparency: 0);
    var secondBatch = Batch(ftxRef: null, txsRef: "SIOpaque:txs", transparency: 0);

    using var first = PathVisualSceneLoader.CreateMaterial(
      new PathPlacement(0, 0, NativeSlopeTile()),
      null!,
      firstBatch,
      resolver);
    using var second = PathVisualSceneLoader.CreateMaterial(
      new PathPlacement(1, 0, NativeSlopeTile()),
      null!,
      secondBatch,
      resolver);

    using (Assert.EnterMultipleScope()) {
      Assert.That(resolver.SurfaceTextureRequests, Is.EqualTo(2));
      Assert.That(resolver.ShapeTextureRequests, Is.Zero);
      Assert.That(first.AlbedoTexture, Is.SameAs(texture));
      Assert.That(second.AlbedoTexture, Is.SameAs(texture));
    }
    firstBatch.Mesh.Dispose();
    secondBatch.Mesh.Dispose();
    texture.Dispose();
  }

  [Test]
  public void CreateMaterial_AppliesOnlyExactAlphaTextureStyleFamilies() {
    var texture = Texture("alpha");
    var resolver = new FakeResolver(Shape(Mesh()), texture);
    var batches = new[] {
      Batch("low:ftx", "SIAlphaMaskLowLeaves:TXS", 1),
      Batch("mask:ftx", "SIAlphaMaskFence:txs", 1),
      Batch("alpha:ftx", "SIAlphaGlass:txs", 2),
      Batch("opaque:ftx", "SIOpaque:txs", 0),
      Batch("embedded:ftx", "MySIAlpha:txs", 2),
      Batch("wrong-tag:ftx", "SIAlpha:shs", 2),
    };
    var materials = new List<Material>();
    try {
      foreach (var batch in batches)
        materials.Add(PathVisualSceneLoader.CreateMaterial(
          new PathPlacement(0, 0, NativeSlopeTile()),
          null!,
          batch,
          resolver));
      var textured = materials.Cast<Textured>().ToArray();

      using (Assert.EnterMultipleScope()) {
        Assert.That(textured[0].AlphaReference, Is.EqualTo(100));
        Assert.That(textured[1].AlphaReference,
          Is.EqualTo(Textured.DefaultAlphaMaskReference));
        Assert.That(textured[2].AlphaReference, Is.EqualTo(8));
        Assert.That(textured[3].AlphaReference, Is.Null);
        Assert.That(textured[4].AlphaReference, Is.Null);
        Assert.That(textured[5].AlphaReference, Is.Null);
      }
    } finally {
      foreach (var material in materials) material.Dispose();
      foreach (var batch in batches) batch.Mesh.Dispose();
      texture.Dispose();
    }
  }

  [Test]
  public void CreateMaterial_UsesExactEngineGlobalStylesWithoutTextureLookup() {
    var texture = Texture("unused");
    var resolver = new FakeResolver(Shape(Mesh()), texture);
    var waterBatch = Batch("water:ftx", "SIWater:txs", 2, sides: 1);
    var chromeBatch = Batch("chrome:ftx", "SIOpaqueChrome:txs", 0, sides: 3);

    using var water = PathVisualSceneLoader.CreateMaterial(
      new PathPlacement(0, 0, NativeSlopeTile()), null!, waterBatch, resolver);
    using var chrome = PathVisualSceneLoader.CreateMaterial(
      new PathPlacement(0, 0, NativeSlopeTile()), null!, chromeBatch, resolver);

    using (Assert.EnterMultipleScope()) {
      Assert.That(water, Is.TypeOf<Water>());
      Assert.That(water.CullBackFaces, Is.False);
      Assert.That(chrome, Is.TypeOf<Chrome>());
      Assert.That(chrome.CullBackFaces, Is.True);
      Assert.That(resolver.SurfaceTextureRequests, Is.Zero);
      Assert.That(resolver.ShapeTextureRequests, Is.Zero);
    }
    waterBatch.Mesh.Dispose();
    chromeBatch.Mesh.Dispose();
    texture.Dispose();
  }

  [Test]
  public void CreateMaterial_RejectsInvalidSidesBeforeAcquiringTextureLease() {
    var texture = Texture("unused");
    var resolver = new FakeResolver(Shape(Mesh()), texture);
    var batch = Batch("shape:ftx", "SIOpaque:txs", 0, sides: 2);

    Assert.Throws<InvalidDataException>(new Action(() =>
      PathVisualSceneLoader.CreateMaterial(
        new PathPlacement(0, 0, NativeSlopeTile()), null!, batch, resolver)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(resolver.SurfaceTextureRequests, Is.Zero);
      Assert.That(resolver.ShapeTextureRequests, Is.Zero);
    }
    batch.Mesh.Dispose();
    texture.Dispose();
  }

  [Test]
  public void HasDiagonal_UsesAtGradeSharedCornerTolerance() {
    var terrain = new Terrain(width: 3, height: 3);
    var placement = new PathPlacement(1, 1, new PathTile());
    var diagonal = new PathPlacement(2, 2, new PathTile());
    var placements = new Dictionary<(int X, int Y), PathPlacement[]> {
      [(2, 2)] = [diagonal],
    };
    terrain.SetCornerHeight(1, 1, TerrainCornerSlot.NorthEast, 0);
    terrain.SetCornerHeight(
      2,
      2,
      TerrainCornerSlot.SouthWest,
      (Park.AtGradePathMaxRise / 2) + 1);

    var separated = PathVisualSceneLoader.HasDiagonal(
      placement,
      PathCornerMask.NorthEast,
      terrain,
      placements);
    terrain.SetCornerHeight(
      2,
      2,
      TerrainCornerSlot.SouthWest,
      Park.AtGradePathMaxRise / 2);
    var connected = PathVisualSceneLoader.HasDiagonal(
      placement,
      PathCornerMask.NorthEast,
      terrain,
      placements);

    using (Assert.EnterMultipleScope()) {
      Assert.That(separated, Is.False);
      Assert.That(connected, Is.True);
    }
  }

  [Test]
  public void HasDiagonal_UsesRaisedSlopeCornerHeightInsteadOfBaseHeight() {
    var terrain = new Terrain(width: 3, height: 3);
    var placement = new PathPlacement(1, 1, new PathTile {
      Raised = true,
      RaisedHeight = 100,
      RaisedSlope = PathRaisedSlope.Gentle,
      RaisedSlopeDirection = Edge.North,
    });
    var diagonal = new PathPlacement(2, 2, new PathTile {
      Raised = true,
      RaisedHeight = 100,
      RaisedSlope = PathRaisedSlope.Flat,
    });
    var placements = new Dictionary<(int X, int Y), PathPlacement[]> {
      [(2, 2)] = [diagonal],
    };

    var separated = PathVisualSceneLoader.HasDiagonal(
      placement,
      PathCornerMask.NorthEast,
      terrain,
      placements);
    placements[(2, 2)] = [diagonal with {
      Tile = new PathTile {
        Raised = true,
        RaisedHeight = 200,
        RaisedSlope = PathRaisedSlope.Flat,
      }
    }];
    var connected = PathVisualSceneLoader.HasDiagonal(
      placement,
      PathCornerMask.NorthEast,
      terrain,
      placements);

    using (Assert.EnterMultipleScope()) {
      Assert.That(separated, Is.False);
      Assert.That(connected, Is.True);
    }
  }

  private static PathTile NativeSlopeTile() => new() {
    SurfaceSystemName = "native",
    Raised = true,
    RaisedHeight = 100,
    RaisedSlope = PathRaisedSlope.Sloped,
    RaisedSlopeDirection = Edge.West,
  };

  private static PathType PathResource() => new(
    "native",
    1,
    "native",
    "display:txt",
    "icon:gsi",
    "primary",
    "alternate",
    Enum.GetValues<PathTypeShapeKind>().Select(kind => new PathTypeShapeOwners(
      kind,
      Enumerable.Range(0, 4).Select(index => $"{kind}-{index}").ToArray())).ToArray(),
    [],
    null);

  private static ResolvedPathShape Shape(params StaticShapeMesh[] meshes) {
    var lod = new SceneryItemVisualLod(
      "high",
      SvdLodType.StaticShape,
      "shape:shs",
      null,
      null,
      null,
      new SceneryVisualBillboardSettings(0f, 0f, 0f, 0f, 0f, 0f),
      10f,
      []);
    var visual = new SceneryItemVisual(
      "owner",
      (SvdFlags)0,
      0f,
      1f,
      0f,
      0f,
      [lod],
      null);
    var shape = new StaticShape(
      "shape",
      Vector3.Zero,
      Vector3.One,
      meshes,
      []);
    return new ResolvedPathShape(visual, lod, shape, null!, null!);
  }

  private static StaticShapeMesh Mesh(
    string name = "mesh",
    uint sides = 1
  ) => new(
    name,
    0,
    "shape:ftx",
    "SIOpaque:txs",
    0,
    0,
    sides,
    [
      new StaticShapeVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero, Vector4.One),
      new StaticShapeVertex(Vector3.UnitX, Vector3.UnitZ, Vector2.UnitX, Vector4.One),
      new StaticShapeVertex(Vector3.UnitY, Vector3.UnitZ, Vector2.UnitY, Vector4.One),
    ],
    [0, 1, 2]) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = 3,
    };

  private static StaticShapeMeshBatch Batch(
    string? ftxRef,
    string? txsRef,
    uint transparency,
    uint sides = 1,
    OpenCobra.GDK.Meshes.Mesh? mesh = null
  ) => new(0, "mesh", 0, ftxRef, txsRef, transparency, 0, sides,
    mesh ?? MeshGeometry());

  private static Mesh MeshGeometry() => new([
    new Vertex { Position = Vector3.Zero, Normal = Vector3.UnitZ, Color = Vector4.One },
    new Vertex { Position = Vector3.UnitX, Normal = Vector3.UnitZ, Color = Vector4.One },
    new Vertex { Position = Vector3.UnitY, Normal = Vector3.UnitZ, Color = Vector4.One },
  ], [0, 1, 2]);

  private static Texture Texture(string name) => new(
    name,
    1,
    1,
    new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3, 255)));

  private sealed class FakeResolver(
    ResolvedPathShape shape,
    Texture texture
  ) : IPathVisualResourceResolver {
    private readonly ResolvedPathTypeResource resource = new(PathResource());

    public int SurfaceTextureRequests { get; private set; }
    public int ShapeTextureRequests { get; private set; }

    public bool TryResolve(PathTile tile, out ResolvedPathSurfaceResource? resolved) {
      resolved = string.Equals(
        tile.SurfaceSystemName,
        resource.SystemName,
        StringComparison.OrdinalIgnoreCase) ? resource : null;
      return resolved != null;
    }

    public bool TryResolveTexture(PathTile tile, out Texture? resolved) {
      SurfaceTextureRequests++;
      resolved = texture;
      return true;
    }

    public bool TryResolveShape(
      PathTile tile,
      string ownerName,
      out ResolvedPathShape? resolved
    ) {
      resolved = shape;
      return true;
    }

    public bool TryResolveShapeTexture(
      PathTile tile,
      SceneryResourceEntry shapeSource,
      string? taggedReference,
      out Texture? resolved
    ) {
      ShapeTextureRequests++;
      resolved = texture;
      return true;
    }
  }
}
