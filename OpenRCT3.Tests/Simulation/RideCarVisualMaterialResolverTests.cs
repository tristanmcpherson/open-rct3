// Ride Car Visual Material Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarVisualMaterialResolverTests {
  [Test]
  public void Resolve_UsesSavedCarColoursCachesFirstFrameAndReturnsFreshLeasedMaterials() {
    var path = FixturePath("ride-textures.unique.ovl");
    using var archive = Archive(path, "Body");
    using var context = Context(archive);
    var unusedFrame = new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3, 255));
    var decodeCount = 0;
    var resolver = new RideCarVisualMaterialResolver(context, (_, _) => {
      decodeCount++;
      return new FlexiTextureList(0, [
        IndexedFrame(Recolorable.First, 42),
        new FlexiTexture(Recolorable.None, unusedFrame),
      ]);
    });
    var mesh = Mesh();
    var batch = Batch(mesh, "Body:ftx", 0);
    var greenCar = Car(batch, [path], 11, 2, 3);
    var redCar = Car(batch, [path], 28, 2, 3);

    var first = resolver.Resolve(greenCar, batch);
    var second = resolver.Resolve(greenCar, batch);
    var third = resolver.Resolve(redCar, batch);
    var firstMaterial = (Textured)first.Material!;
    var secondMaterial = (Textured)second.Material!;
    var thirdMaterial = (Textured)third.Material!;
    var greenTexture = firstMaterial.AlbedoTexture!;
    var redTexture = thirdMaterial.AlbedoTexture!;

    using (Assert.EnterMultipleScope()) {
      Assert.That(first.IsResolved, Is.True);
      Assert.That(second.IsResolved, Is.True);
      Assert.That(third.IsResolved, Is.True);
      Assert.That(firstMaterial, Is.Not.SameAs(secondMaterial));
      Assert.That(secondMaterial.AlbedoTexture, Is.SameAs(greenTexture));
      Assert.That(redTexture, Is.Not.SameAs(greenTexture));
      Assert.That(redTexture.CacheKey, Is.Not.EqualTo(greenTexture.CacheKey));
      Assert.That(greenTexture.Pixels[0, 0], Is.EqualTo(FlexiColourPalette.Get(11)));
      Assert.That(redTexture.Pixels[0, 0], Is.EqualTo(FlexiColourPalette.Get(28)));
      Assert.That(greenTexture.Pixels[0, 0], Is.Not.EqualTo(FlexiColourPalette.Get(12)));
      Assert.That(decodeCount, Is.EqualTo(1));
      Assert.Throws<ObjectDisposedException>(new Action(() => _ = unusedFrame[0, 0]));
    }

    resolver.Dispose();
    resolver.Dispose();
    using (Assert.EnterMultipleScope()) {
      Assert.That(greenTexture.State, Is.EqualTo(State.Uninitialized));
      Assert.That(redTexture.State, Is.EqualTo(State.Uninitialized));
    }
    firstMaterial.Dispose();
    Assert.That(greenTexture.State, Is.EqualTo(State.Uninitialized));
    secondMaterial.Dispose();
    Assert.That(greenTexture.State, Is.EqualTo(State.Disposed));
    thirdMaterial.Dispose();
    Assert.That(redTexture.State, Is.EqualTo(State.Disposed));
    mesh.Dispose();
  }

  [Test]
  public void Resolve_MissingNonblankFtxIsTypedAndNeverUsesOutsideWhitelist() {
    var allowedPath = FixturePath("allowed.unique.ovl");
    var outsidePath = FixturePath("outside.unique.ovl");
    using var archive = Archive(outsidePath, "Body");
    using var context = Context(archive);
    var decodeCount = 0;
    using var resolver = new RideCarVisualMaterialResolver(context, (_, _) => {
      decodeCount++;
      throw new AssertionException("An out-of-whitelist FTX must not be decoded.");
    });
    var mesh = Mesh();
    var batch = Batch(mesh, "Body:ftx", 0);

    var result = resolver.Resolve(
      batch,
      [allowedPath],
      new SceneryFlexiColours(1, 2, 3));

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status,
        Is.EqualTo(RideCarVisualMaterialResolutionStatus.MissingFlexibleTexture));
      Assert.That(result.IsResolved, Is.False);
      Assert.That(result.Material, Is.Null);
      Assert.That(result.MissingFlexibleTextureReference, Is.EqualTo("Body:ftx"));
      Assert.That(decodeCount, Is.Zero);
    }
    mesh.Dispose();
  }

  [Test]
  public void Resolve_UsesOnlyRegisteredEngineGlobalNullBitmapOwner() {
    var ownerPath = FixturePath("nullbmp.common.ovl");
    var allowedPath = FixturePath("allowed.unique.ovl");
    using var archive = Archive(ownerPath, "nullbmp");
    archive.Add(
      new OvlFile("Body", FileType.FlexibleTexture, ownerPath),
      new OvlEntry(0, 1));
    using var context = Context(archive, ownerPath, ownerPath);
    var decodedFiles = new List<OvlFile>();
    using var resolver = new RideCarVisualMaterialResolver(context, (_, file) => {
      decodedFiles.Add(file);
      return new FlexiTextureList(0, [
        new FlexiTexture(
          Recolorable.None,
          new Image<Rgba32>(1, 1, new Rgba32(10, 20, 30, 255)))
      ]);
    });
    var nullBitmapMesh = Mesh();
    var bodyMesh = Mesh();

    var nullBitmap = resolver.Resolve(
      Batch(nullBitmapMesh, "NULLBMP:FTX", 0),
      [allowedPath],
      new SceneryFlexiColours(1, 2, 3));
    var body = resolver.Resolve(
      Batch(bodyMesh, "Body:ftx", 0),
      [allowedPath],
      new SceneryFlexiColours(1, 2, 3));

    using (Assert.EnterMultipleScope()) {
      Assert.That(nullBitmap.IsResolved, Is.True);
      Assert.That(nullBitmap.Material, Is.TypeOf<Textured>());
      Assert.That(decodedFiles, Has.Count.EqualTo(1));
      Assert.That(decodedFiles[0].Name, Is.EqualTo("nullbmp"));
      Assert.That(decodedFiles[0].Path, Is.EqualTo(ownerPath));
      Assert.That(body.Status,
        Is.EqualTo(RideCarVisualMaterialResolutionStatus.MissingFlexibleTexture));
      Assert.That(body.Material, Is.Null);
    }
    nullBitmap.Material!.Dispose();
    nullBitmapMesh.Dispose();
    bodyMesh.Dispose();
  }

  [Test]
  public void Resolve_UnregisteredEngineGlobalNullBitmapRemainsTypedMissing() {
    var ownerPath = FixturePath("nullbmp.common.ovl");
    var allowedPath = FixturePath("allowed.unique.ovl");
    using var archive = Archive(ownerPath, "nullbmp");
    using var context = Context(archive, ownerPath);
    var decodeCount = 0;
    using var resolver = new RideCarVisualMaterialResolver(context, (_, _) => {
      decodeCount++;
      throw new AssertionException("An unregistered engine-global FTX must not be decoded.");
    });
    var mesh = Mesh();

    var result = resolver.Resolve(
      Batch(mesh, "nullbmp:ftx", 0),
      [allowedPath],
      new SceneryFlexiColours(1, 2, 3));

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status,
        Is.EqualTo(RideCarVisualMaterialResolutionStatus.MissingFlexibleTexture));
      Assert.That(result.Material, Is.Null);
      Assert.That(decodeCount, Is.Zero);
    }
    mesh.Dispose();
  }

  [Test]
  public void Resolve_PreservesSceneryMaterialPolicies() {
    var path = FixturePath("materials.unique.ovl");
    using var archive = Archive(path, "Body");
    using var context = Context(archive);
    using var resolver = new RideCarVisualMaterialResolver(context, (_, _) =>
      new FlexiTextureList(0, [
        new FlexiTexture(
          Recolorable.None,
          new Image<Rgba32>(1, 1, new Rgba32(10, 20, 30, 255)))
      ]));
    var colours = new SceneryFlexiColours(1, 2, 3);
    var materials = new List<Material>();
    var meshes = new List<Mesh>();

    try {
      var flat = Resolve(Batch(NewMesh(), null, 99, sides: 1));
      var water = Resolve(Batch(
        NewMesh(), "MissingDummy:ftx", 2, "SIWater:TXS", sides: 3));
      var chrome = Resolve(Batch(
        NewMesh(), "MissingDummy:ftx", 0, "SIOpaqueChrome:txs", sides: 1));
      var opaque = Resolve(Batch(NewMesh(), "Body:ftx", 0, sides: 3));
      var lowMask = Resolve(Batch(
        NewMesh(), "Body:ftx", 1, "SIAlphaMaskLowLeaves:txs"));
      var mask = Resolve(Batch(
        NewMesh(), "Body:ftx", 1, "SIAlphaMaskFence:txs"));
      var alpha = Resolve(Batch(
        NewMesh(), "Body:ftx", 2, "SIAlphaGlass:txs"));

      using (Assert.EnterMultipleScope()) {
        Assert.That(flat, Is.TypeOf<Flat>());
        Assert.That(flat.CullBackFaces, Is.False);
        Assert.That(water, Is.TypeOf<Water>());
        Assert.That(water.RenderState, Is.EqualTo(
          MaterialRenderState.AlphaBlend with { CullBackFaces = true }));
        Assert.That(chrome, Is.TypeOf<Chrome>());
        Assert.That(chrome.RenderState, Is.EqualTo(MaterialRenderState.Opaque));
        Assert.That(opaque.RenderState, Is.EqualTo(
          MaterialRenderState.Opaque with { CullBackFaces = true }));
        Assert.That(lowMask.RenderState.BlendMode,
          Is.EqualTo(MaterialBlendMode.AlphaMask));
        Assert.That(((Textured)lowMask).AlphaReference, Is.EqualTo(100));
        Assert.That(((Textured)mask).AlphaReference,
          Is.EqualTo(Textured.DefaultAlphaMaskReference));
        Assert.That(alpha.RenderState, Is.EqualTo(MaterialRenderState.AlphaBlend));
        Assert.That(((Textured)alpha).AlphaReference, Is.EqualTo(8));
      }
    } finally {
      foreach (var material in materials) material.Dispose();
      foreach (var mesh in meshes) mesh.Dispose();
    }
    return;

    Mesh NewMesh() {
      var mesh = Mesh();
      meshes.Add(mesh);
      return mesh;
    }

    Material Resolve(StaticShapeMeshBatch batch) {
      var result = resolver.Resolve(batch, [path], colours);
      Assert.That(result.IsResolved, Is.True);
      materials.Add(result.Material!);
      return result.Material!;
    }
  }

  [Test]
  public void Resolve_RejectsInvalidSidesTransparencyAndGlobalStyleCombinations() {
    var path = FixturePath("invalid-materials.unique.ovl");
    using var archive = Archive(path, "Body");
    using var context = Context(archive);
    using var resolver = new RideCarVisualMaterialResolver(context, (_, _) =>
      new FlexiTextureList(0, [
        new FlexiTexture(Recolorable.None, new Image<Rgba32>(1, 1))
      ]));
    var first = Mesh();
    var second = Mesh();
    var third = Mesh();
    var fourth = Mesh();
    var colours = new SceneryFlexiColours(1, 2, 3);

    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.Resolve(Batch(first, null, 0, sides: 2), [path], colours)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.Resolve(Batch(second, "Body:ftx", 9), [path], colours)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.Resolve(
        Batch(third, "Body:ftx", 0, "SIWater:txs"),
        [path],
        colours)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.Resolve(
        Batch(fourth, "Body:ftx", 2, "SIOpaqueChrome:txs"),
        [path],
        colours)));
    first.Dispose();
    second.Dispose();
    third.Dispose();
    fourth.Dispose();
  }

  private static RideCarStaticInstanceEntry Car(
    StaticShapeMeshBatch batch,
    IReadOnlyList<string> allowedPaths,
    int first,
    int second,
    int third
  ) {
    var track = new RideTrack(
      1,
      0,
      0,
      0,
      false,
      false,
      false,
      [],
      [],
      12,
      2,
      2,
      2,
      null,
      null);
    var ride = new DatTrackedRideInstanceData(
      entryId: 2,
      name: "Synthetic ride",
      track: track.SourceEntryId,
      trackedRideOverlayName: "SyntheticRide",
      trackedRideSymbolName: "SyntheticRide:trr",
      nTrains: 1,
      nCarsPerTrain: 1,
      trainSelection: 0,
      trains: [3],
      carFlexiColours: new DatSceneryFlexiColour(first, second, third));
    var identity = new RideInstanceTrackLink(ride, track);
    var geometry = new RideTrackGeometryLink(
      track,
      RideTrackGeometryStatus.UnsupportedGeometry,
      null,
      null);
    var trackRuntime = new RideInstanceTrackRuntimeEntry(
      0,
      0,
      identity,
      geometry,
      null,
      null);
    var trainRuntime = new RideInstanceTrainRuntimeEntry(0, trackRuntime, null!);
    var carSource = new RideCarResourceSource(
      new OvlFile("Car", FileType.RideCar, allowedPaths[0]),
      null!,
      allowedPaths);
    var carLink = new RideCarLink(RideTrainCarRole.Front, "Car:ric", carSource, []);
    var carRuntime = new RideCarInstanceRuntimeEntry(
      0,
      0,
      trainRuntime,
      null!,
      RideTrainCarRole.Front,
      null!,
      null!,
      RideCarResourceRuntimeStatus.Resolved,
      carLink,
      null,
      null);
    var shape = new BoneShape("Body", Vector3.Zero, Vector3.One, [], []);
    var visual = new RideCarVisualShapeLink(null!, null!, carLink, null!, []);
    var template = new RideCarVisualMeshTemplate(
      visual,
      null!,
      RideCarVisualTemplateShapeKind.BoneShape,
      null,
      shape,
      [batch]);
    return new RideCarStaticInstanceEntry(
      0,
      carRuntime,
      null!,
      RideCarStaticInstanceIssue.None,
      template,
      null,
      null,
      null,
      null);
  }

  private static StaticShapeMeshBatch Batch(
    Mesh mesh,
    string? ftx,
    uint transparency,
    string? txs = null,
    uint sides = 1
  ) => new(0, "Body", 0, ftx, txs, transparency, 0, sides, mesh);

  private static Mesh Mesh() => new([
    new Vertex { Position = Vector3.Zero, Normal = Vector3.UnitZ, Color = Vector4.One },
    new Vertex { Position = Vector3.UnitX, Normal = Vector3.UnitZ, Color = Vector4.One },
    new Vertex { Position = Vector3.UnitY, Normal = Vector3.UnitZ, Color = Vector4.One },
  ], [0, 1, 2]);

  private static FlexiTexture IndexedFrame(Recolorable recolorable, byte index) {
    var palette = new byte[256 * 4];
    palette[index * 4] = byte.MaxValue;
    return new FlexiTexture(
      recolorable,
      new Image<Rgba32>(1, 1, new Rgba32(0, 0, byte.MaxValue, byte.MaxValue))) {
      PaletteBgra = palette,
      IndexedPixels = new[] { index },
    };
  }

  private static Ovl Archive(string path, string resourceName) {
    var archive = new Ovl(Path.GetFileName(path));
    archive.Add(
      new OvlFile(resourceName, FileType.FlexibleTexture, path),
      new OvlEntry(0, 1));
    return archive;
  }

  private static RideTrackResourceCatalogLoadContext Context(
    Ovl archive,
    string loadedCommonPath = "fixture.common.ovl",
    string? engineGlobalNullBitmapOwnerPath = null
  ) => new(
    [loadedCommonPath],
    [archive],
    _ => { },
    engineGlobalNullBitmapOwnerPath);

  private static string FixturePath(string fileName) => Path.Combine(
    Path.GetTempPath(),
    "openrct3-ride-material-tests",
    fileName);
}
