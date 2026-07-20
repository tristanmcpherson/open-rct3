// Scenery Scene Loader Tests
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
public class ScenerySceneLoaderTests {
  [Test]
  public void Load_GroupsExactOverlayPathsAndCountsBlankOrMissingOverlays() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("first", @"Style\Trees"));
    park.SceneryPlacements.Add(Placement("second", @"Style\Trees"));
    park.SceneryPlacements.Add(Placement("case", @"style\trees"));
    park.SceneryPlacements.Add(Placement("absent", @"Style\Absent"));
    park.SceneryPlacements.Add(Placement("blank", " "));
    var createdPaths = new List<string>();
    var contexts = new Dictionary<string, FakeContext>(StringComparer.Ordinal);

    var result = ScenerySceneLoader.Load(
      park,
      terrain,
      path => {
        createdPaths.Add(path);
        var context = new FakeContext(
          isMissingOverlay: path == @"Style\Absent",
          resolve: _ => null);
        contexts.Add(path, context);
        return context;
      },
      (sourcePark, _, lookup) => {
        foreach (var placement in sourcePark.SceneryPlacements) lookup(placement);
        return Geometry([]);
      });

    using (Assert.EnterMultipleScope()) {
      Assert.That(createdPaths, Is.EqualTo(new[] {
        @"Style\Trees", @"style\trees", @"Style\Absent"
      }));
      Assert.That(contexts[@"Style\Trees"].ResolvedKeys,
        Is.EqualTo(new[] { "first", "second" }));
      Assert.That(contexts[@"style\trees"].ResolvedKeys,
        Is.EqualTo(new[] { "case" }));
      Assert.That(contexts[@"Style\Absent"].ResolvedKeys, Is.Empty);
      Assert.That(contexts.Values.Select(context => context.DisposeCount),
        Is.All.EqualTo(1));
      Assert.That(result.MissingOverlayPlacementCount, Is.EqualTo(2));
      Assert.That(result.Models, Is.Empty);
    }
  }

  [Test]
  public void Load_MissingSidAndNoStaticVisualRemainExplicitUnsupportedOutcomes() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("NoOverlay", null));
    park.SceneryPlacements.Add(Placement("MissingSid", "Style"));
    park.SceneryPlacements.Add(Placement("NoStatic", "Style"));
    var noStatic = new ResolvedSceneryObject(Item("NoStatic", "Visual:svd"), []);
    var context = new FakeContext(resolve: key => key == "NoStatic" ? noStatic : null);

    var result = ScenerySceneLoader.Load(
      park,
      terrain,
      _ => context,
      SceneryGeometryBuilder.Build);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.MissingOverlayPlacementCount, Is.EqualTo(1));
      Assert.That(result.Geometry.PlacementCount, Is.EqualTo(3));
      Assert.That(result.Geometry.UnresolvedPlacementCount, Is.EqualTo(2));
      Assert.That(result.Geometry.ResolvedPlacementCount, Is.EqualTo(1));
      Assert.That(result.Geometry.UnsupportedVisualPlacementCount, Is.EqualTo(1));
      Assert.That(result.Geometry.RenderedPlacementCount, Is.Zero);
      Assert.That(result.Models, Is.Empty);
      Assert.That(context.DisposeCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Load_SelectsMaterialsSkipsMissingFtxAndTransfersTextureLeases() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", "Style"));
    var texture = Texture("shared");
    var context = new FakeContext(
      resolve: _ => null,
      resolveTexture: (reference, _) => reference == "Missing:ftx"
        ? (false, null)
        : (true, texture),
      dispose: texture.Dispose);
    var opaqueMesh = Mesh(new Vector2(0.25f, 0.75f), new Vector4(0.1f, 0.2f, 0.3f, 0.4f));
    var maskMesh = Mesh();
    var alphaMesh = Mesh();
    var flatMesh = Mesh();
    var missingMesh = Mesh();
    var batches = new[] {
      Batch("Style", "Opaque:ftx", 0, opaqueMesh),
      Batch("Style", "Mask:ftx", 1, maskMesh),
      Batch("Style", "Alpha:ftx", 2, alphaMesh),
      Batch("Style", null, 0, flatMesh),
      Batch("Style", "Missing:ftx", 0, missingMesh),
    };

    var result = ScenerySceneLoader.Load(
      park,
      terrain,
      _ => context,
      (sourcePark, _, lookup) => {
        lookup(sourcePark.SceneryPlacements[0]);
        return Geometry(batches);
      });

    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.Models, Has.Count.EqualTo(4));
        Assert.That(result.Models[0].Mesh, Is.SameAs(opaqueMesh));
        Assert.That(result.Models[0].Material, Is.TypeOf<Textured>());
        Assert.That(result.Models[0].Material!.RenderState,
          Is.EqualTo(MaterialRenderState.Opaque));
        Assert.That(result.Models[1].Material, Is.TypeOf<Textured>());
        Assert.That(result.Models[1].Material!.RenderState,
          Is.EqualTo(MaterialRenderState.AlphaMask));
        Assert.That(result.Models[2].Material, Is.TypeOf<Textured>());
        Assert.That(result.Models[2].Material!.RenderState,
          Is.EqualTo(MaterialRenderState.AlphaBlend));
        Assert.That(result.Models[3].Material, Is.TypeOf<Flat>());
        Assert.That(opaqueMesh.Vertices[0].TexCoord, Is.EqualTo(new Vector2(0.25f, 0.75f)));
        Assert.That(opaqueMesh.Vertices[0].Color,
          Is.EqualTo(new Vector4(0.1f, 0.2f, 0.3f, 0.4f)));
        Assert.That(result.MissingTextureBatchCount, Is.EqualTo(1));
        Assert.That(missingMesh.State, Is.EqualTo(State.Disposed));
        Assert.That(texture.State, Is.EqualTo(State.Uninitialized));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
      }

      result.Models[0].Dispose();
      Assert.That(texture.State, Is.EqualTo(State.Uninitialized));
      result.Models[1].Dispose();
      Assert.That(texture.State, Is.EqualTo(State.Uninitialized));
      result.Models[2].Dispose();
      Assert.That(texture.State, Is.EqualTo(State.Disposed));
    } finally {
      foreach (var model in result.Models) model.Dispose();
    }
  }

  [Test]
  public void Load_CarriesStaticShapeSideModeIntoMaterialRenderState() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", "Style"));
    var context = new FakeContext(resolve: _ => null);

    var result = ScenerySceneLoader.Load(
      park,
      terrain,
      _ => context,
      (sourcePark, _, lookup) => {
        lookup(sourcePark.SceneryPlacements[0]);
        return Geometry([
          Batch("Style", null, 0, Mesh(), sides: 1),
          Batch("Style", null, 0, Mesh(), sides: 3),
        ]);
      });

    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.Models[0].Material!.RenderState.CullBackFaces, Is.False);
        Assert.That(result.Models[1].Material!.RenderState.CullBackFaces, Is.True);
      }
    } finally {
      foreach (var model in result.Models) model.Dispose();
    }
  }

  [Test]
  public void Load_RejectsUnknownStaticShapeSideMode() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", "Style"));
    var context = new FakeContext(resolve: _ => null);
    var mesh = Mesh();

    Assert.Throws<InvalidDataException>(new Action(() =>
      ScenerySceneLoader.Load(
        park,
        terrain,
        _ => context,
        (sourcePark, _, lookup) => {
          lookup(sourcePark.SceneryPlacements[0]);
          return Geometry([Batch("Style", null, 0, mesh, sides: 2)]);
        })));

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.State, Is.EqualTo(State.Disposed));
      Assert.That(context.DisposeCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Load_PassesBatchFlexiColoursToTextureResolution() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", "Style"));
    var texture = Texture("coloured");
    var context = new FakeContext(
      resolve: _ => null,
      resolveTexture: (_, _) => (true, texture),
      dispose: texture.Dispose);
    var colours = new SceneryFlexiColours(28, 11, 7);
    var mesh = Mesh();

    var result = ScenerySceneLoader.Load(
      park,
      terrain,
      _ => context,
      (sourcePark, _, lookup) => {
        lookup(sourcePark.SceneryPlacements[0]);
        return Geometry([
          Batch("Style", "Tinted:ftx", 0, mesh, flexiColours: colours)
        ]);
      });

    try {
      Assert.That(context.TextureRequests, Is.EqualTo(new[] {
        ("Tinted:ftx", colours)
      }));
    } finally {
      foreach (var model in result.Models) model.Dispose();
    }
  }

  [Test]
  public void Load_RoutesOnlyExactAlphaStyleFamiliesToNativeReferences() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", "Style"));
    var texture = Texture("alpha-styles");
    var context = new FakeContext(
      resolve: _ => null,
      resolveTexture: (_, _) => (true, texture),
      dispose: texture.Dispose);
    var batches = new[] {
      Batch("Style", "low:ftx", 1, Mesh(), "SIAlphaMaskLowLeaves:TXS"),
      Batch("Style", "mask:ftx", 1, Mesh(), "SIAlphaMaskFence:txs"),
      Batch("Style", "alpha:ftx", 2, Mesh(), "SIAlphaGlass:txs"),
      Batch("Style", "opaque:ftx", 0, Mesh(), "SIOpaque:txs"),
      Batch("Style", "embedded:ftx", 2, Mesh(), "MySIAlpha:txs"),
      Batch("Style", "wrong-tag:ftx", 2, Mesh(), "SIAlpha:shs"),
    };

    var result = ScenerySceneLoader.Load(
      park,
      terrain,
      _ => context,
      (sourcePark, _, lookup) => {
        lookup(sourcePark.SceneryPlacements[0]);
        return Geometry(batches);
      });

    try {
      var materials = result.Models
        .Select(model => (Textured)model.Material!)
        .ToArray();
      using (Assert.EnterMultipleScope()) {
        Assert.That(materials[0].AlphaReference, Is.EqualTo(100));
        Assert.That(
          materials[1].AlphaReference,
          Is.EqualTo(Textured.DefaultAlphaMaskReference));
        Assert.That(materials[2].AlphaReference, Is.EqualTo(8));
        Assert.That(materials[3].AlphaReference, Is.Null);
        Assert.That(materials[4].AlphaReference, Is.Null);
        Assert.That(materials[5].AlphaReference, Is.Null);
      }
    } finally {
      foreach (var model in result.Models) model.Dispose();
    }
  }

  [Test]
  public void Load_UsesExactEngineGlobalStylesWithoutResolvingDummyFtxSlots() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", "Style"));
    var textureResolveCount = 0;
    var context = new FakeContext(
      resolve: _ => null,
      resolveTexture: (_, _) => {
        textureResolveCount++;
        return (false, null);
      });
    var waterMesh = Mesh();
    var chromeMesh = Mesh();
    var modulatedChromeMesh = Mesh();
    var batches = new[] {
      Batch("Style", "siwater:ftx", 2, waterMesh, "SIWATER:TxS"),
      Batch("Style", "two:ftx", 0, chromeMesh, "SIOpaqueChrome:txs"),
      Batch(
        "Style",
        "missing:ftx",
        0,
        modulatedChromeMesh,
        "SIOpaqueChromeModulate:txs"),
    };

    var result = ScenerySceneLoader.Load(
      park,
      terrain,
      _ => context,
      (sourcePark, _, lookup) => {
        lookup(sourcePark.SceneryPlacements[0]);
        return Geometry(batches);
      });

    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.Models, Has.Count.EqualTo(2));
        Assert.That(result.Models[0].Material, Is.TypeOf<Water>());
        Assert.That(result.Models[1].Material, Is.TypeOf<Chrome>());
        Assert.That(result.MissingTextureBatchCount, Is.EqualTo(1));
        Assert.That(textureResolveCount, Is.EqualTo(1));
        Assert.That(modulatedChromeMesh.State, Is.EqualTo(State.Disposed));
      }
    } finally {
      foreach (var model in result.Models) model.Dispose();
    }
  }

  [TestCase("SIWater:txs", 0u)]
  [TestCase("SIOpaqueChrome:txs", 2u)]
  public void Load_EngineGlobalStyleTransparencyMismatchFailsClosed(
    string txsRef,
    uint transparency
  ) {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", "Style"));
    var context = new FakeContext(resolve: _ => null);
    var mesh = Mesh();

    Assert.Throws<InvalidDataException>(new Action(() =>
      ScenerySceneLoader.Load(
        park,
        terrain,
        _ => context,
        (sourcePark, _, lookup) => {
          lookup(sourcePark.SceneryPlacements[0]);
          return Geometry([Batch("Style", "dummy:ftx", transparency, mesh, txsRef)]);
        })));

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.State, Is.EqualTo(State.Disposed));
      Assert.That(context.DisposeCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Load_FailureDisposesCreatedModelsPendingMeshesAndResourceOwners() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", "Style"));
    var texture = Texture("owned");
    var context = new FakeContext(
      resolve: _ => null,
      resolveTexture: (reference, _) => {
        if (reference == "Broken:ftx")
          throw new InvalidDataException("primary decoder failure");
        return (true, texture);
      },
      dispose: texture.Dispose);
    var firstMesh = Mesh();
    var pendingMesh = Mesh();

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      ScenerySceneLoader.Load(
        park,
        terrain,
        _ => context,
        (sourcePark, _, lookup) => {
          lookup(sourcePark.SceneryPlacements[0]);
          return Geometry([
            Batch("Style", "Good:ftx", 0, firstMesh),
            Batch("Style", "Broken:ftx", 0, pendingMesh),
          ]);
        })));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Is.EqualTo("primary decoder failure"));
      Assert.That(firstMesh.State, Is.EqualTo(State.Disposed));
      Assert.That(pendingMesh.State, Is.EqualTo(State.Disposed));
      Assert.That(texture.State, Is.EqualTo(State.Disposed));
      Assert.That(context.DisposeCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Load_MalformedBatchListDisposesEveryCollectableMesh() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", "Style"));
    var context = new FakeContext(resolve: _ => null);
    var mesh = Mesh();

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      ScenerySceneLoader.Load(
        park,
        terrain,
        _ => context,
        (sourcePark, _, lookup) => {
          lookup(sourcePark.SceneryPlacements[0]);
          return Geometry([Batch("Style", null, 0, mesh), null!]);
        })));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("null batch"));
      Assert.That(mesh.State, Is.EqualTo(State.Disposed));
      Assert.That(context.DisposeCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Load_ModelOwnershipTransferFailureReleasesMaterialLease() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", "Style"));
    var texture = Texture("transfer");
    var context = new FakeContext(
      resolve: _ => null,
      resolveTexture: (_, _) => (true, texture),
      dispose: texture.Dispose);
    var mesh = Mesh();

    Assert.Throws<ObjectDisposedException>(new Action(() =>
      ScenerySceneLoader.Load(
        park,
        terrain,
        _ => context,
        (sourcePark, _, lookup) => {
          lookup(sourcePark.SceneryPlacements[0]);
          return Geometry([Batch("Style", "Texture:ftx", 0, mesh)]);
        },
        sourceMesh => {
          var model = new Model(sourceMesh);
          model.Dispose();
          return model;
        })));

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.State, Is.EqualTo(State.Disposed));
      Assert.That(texture.State, Is.EqualTo(State.Disposed));
      Assert.That(context.DisposeCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Load_CleanupErrorsDoNotMaskThePrimaryFailure() {
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", "Style"));
    var context = new FakeContext(
      resolve: _ => null,
      dispose: () => throw new ApplicationException("cleanup failure"));

    var error = Assert.Throws<AggregateException>(new Action(() =>
      ScenerySceneLoader.Load(
        park,
        terrain,
        _ => context,
        (sourcePark, _, lookup) => {
          lookup(sourcePark.SceneryPlacements[0]);
          throw new InvalidDataException("primary failure");
        })));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.InnerExceptions, Has.Count.EqualTo(2));
      Assert.That(error.InnerExceptions[0], Is.TypeOf<InvalidDataException>());
      Assert.That(error.InnerExceptions[0].Message, Is.EqualTo("primary failure"));
      Assert.That(error.InnerExceptions[1], Is.TypeOf<ApplicationException>());
      Assert.That(error.InnerExceptions[1].Message, Is.EqualTo("cleanup failure"));
      Assert.That(context.DisposeCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Load_AbsentPairIsUnsupportedButIncompletePairPropagates() {
    var root = Path.Combine(Path.GetTempPath(), $"openrct3-scene-{Guid.NewGuid():N}");
    var overlayDirectory = Path.Combine(root, "Style");
    Directory.CreateDirectory(overlayDirectory);
    var terrain = Terrain();
    var park = new Park(terrain);
    park.SceneryPlacements.Add(Placement("Item", @"Style\Absent"));

    try {
      var absent = ScenerySceneLoader.Load(park, terrain, root);

      using (Assert.EnterMultipleScope()) {
        Assert.That(absent.MissingOverlayPlacementCount, Is.EqualTo(1));
        Assert.That(absent.Geometry.UnresolvedPlacementCount, Is.EqualTo(1));
        Assert.That(absent.Models, Is.Empty);
      }

      File.WriteAllBytes(Path.Combine(overlayDirectory, "Incomplete.common.ovl"), []);
      park.SceneryPlacements[0] = Placement("Item", @"Style\Incomplete");

      Assert.Throws<FileNotFoundException>(new Action(() =>
        ScenerySceneLoader.Load(park, terrain, root)));
    } finally {
      Directory.Delete(root, recursive: true);
    }
  }

  private static Terrain Terrain() => new(width: 2, height: 2);

  private static SceneryPlacement Placement(string objectKey, string? overlayPath) =>
    new(objectKey, 0, 0) { OverlayPath = overlayPath };

  private static SceneryItem Item(string name, params string[] visualRefs) => new(
    name,
    0,
    SidPosition.TileFull,
    0,
    1,
    1,
    0f,
    0f,
    0f,
    4f,
    4f,
    4f,
    SidType.SceneryMisc,
    visualRefs);

  private static SceneryGeometryBatch Batch(
    string? overlayPath,
    string? ftxRef,
    uint transparency,
    Mesh mesh,
    string? txsRef = null,
    SceneryFlexiColours? flexiColours = null,
    uint sides = 1
  ) => new(
    new SceneryMaterialKey(overlayPath, ftxRef, txsRef, 0, transparency, 0, sides) {
      FlexiColours = flexiColours ?? default,
    },
    mesh);

  private static SceneryGeometryBuildResult Geometry(
    IReadOnlyList<SceneryGeometryBatch> batches
  ) => new(batches, 0, 0, 0, 0, 0, 0, batches.Count, 0, 0);

  private static Mesh Mesh(
    Vector2? texCoord = null,
    Vector4? color = null
  ) => new([
    new Vertex {
      Position = Vector3.Zero,
      Normal = Vector3.UnitZ,
      TexCoord = texCoord ?? Vector2.Zero,
      Color = color ?? Vector4.One
    },
    new Vertex {
      Position = Vector3.UnitX,
      Normal = Vector3.UnitZ,
      TexCoord = Vector2.UnitX,
      Color = Vector4.One
    },
    new Vertex {
      Position = Vector3.UnitY,
      Normal = Vector3.UnitZ,
      TexCoord = Vector2.UnitY,
      Color = Vector4.One
    }
  ], [0, 1, 2]);

  private static Texture Texture(string name) => new(
    name,
    1,
    1,
    new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3, 255)));

  private sealed class FakeContext : IScenerySceneResourceContext {
    private readonly Func<string, ResolvedSceneryObject?> resolve;
    private readonly Func<
      string,
      SceneryFlexiColours,
      (bool Found, Texture? Texture)> resolveTexture;
    private readonly Action dispose;

    public bool IsMissingOverlay { get; }
    public List<string> ResolvedKeys { get; } = [];
    public List<(string Reference, SceneryFlexiColours Colours)> TextureRequests { get; } = [];
    public int DisposeCount { get; private set; }

    public FakeContext(
      bool isMissingOverlay = false,
      Func<string, ResolvedSceneryObject?>? resolve = null,
      Func<
        string,
        SceneryFlexiColours,
        (bool Found, Texture? Texture)>? resolveTexture = null,
      Action? dispose = null
    ) {
      IsMissingOverlay = isMissingOverlay;
      this.resolve = resolve ?? (_ => null);
      this.resolveTexture = resolveTexture ?? ((_, _) => (false, null));
      this.dispose = dispose ?? (() => { });
    }

    public ResolvedSceneryObject? Resolve(string objectKey) {
      ResolvedKeys.Add(objectKey);
      return resolve(objectKey);
    }

    public bool TryResolveTexture(
      string taggedReference,
      SceneryFlexiColours flexiColours,
      out Texture? texture
    ) {
      TextureRequests.Add((taggedReference, flexiColours));
      var result = resolveTexture(taggedReference, flexiColours);
      texture = result.Texture;
      return result.Found;
    }

    public void Dispose() {
      DisposeCount++;
      dispose();
    }
  }
}
