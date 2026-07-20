// Terrain Blend Scene Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

using TerrainTypeKind = OpenCobra.OVL.Files.TerrainTypeKind;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class TerrainBlendSceneBuilderTests {
  [Test]
  public void Build_ExactLayerBatchesBecomeDeterministicRoleBoundModels() {
    var terrain = NewTerrain(
      2, 1, (x, _) => x == 0 ? Convert.ToByte(30) : Convert.ToByte(8));
    var textures = new Dictionary<byte, Texture> {
      [8] = NewTexture("Terrain_08"),
      [30] = NewTexture("Terrain_30"),
    };
    var textureCalls = new List<byte>();
    var materialPasses = new List<TerrainBlendPass>();
    var operations = TerrainBlendSceneBuilderOperations.Default with {
      BuildMeshBatches = (source, color, name) =>
        TerrainBlendLayerMeshBuilder.BuildBatches(
          source,
          color,
          _ => TerrainTypeKind.GroundBlended,
          _ => Vector2.One,
          TerrainBlendLayerMeshBuilderOperations.Default,
          name),
      ResolveSurfaceTexture = (_, surface) => {
        textureCalls.Add(surface);
        return textures[surface];
      },
      CreateMaterial = pass => {
        materialPasses.Add(pass);
        return new TerrainBlend(pass);
      },
    };

    var result = TerrainBlendSceneBuilder.Build(
      terrain,
      new Vector4(0.2f, 0.3f, 0.4f, 0.9f),
      "Exact Blend",
      operations);

    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.SourceBatchCount, Is.EqualTo(4));
        Assert.That(result.ModelCount, Is.EqualTo(4));
        Assert.That(result.BaseModelCount, Is.EqualTo(2));
        Assert.That(result.ContributionModelCount, Is.EqualTo(2));
        Assert.That(textureCalls, Is.EqualTo(new byte[] { 8, 30, 8, 30 }));
        Assert.That(materialPasses, Is.EqualTo(new[] {
          TerrainBlendPass.Base,
          TerrainBlendPass.Base,
          TerrainBlendPass.Contribution,
          TerrainBlendPass.Contribution,
        }));
        Assert.That(result.ModelBindings.Select(binding =>
          (binding.Role, binding.SurfaceIndex)), Is.EqualTo(new[] {
            (TerrainBlendLayerPassRole.Base, Convert.ToByte(8)),
            (TerrainBlendLayerPassRole.Base, Convert.ToByte(30)),
            (TerrainBlendLayerPassRole.Contribution, Convert.ToByte(8)),
            (TerrainBlendLayerPassRole.Contribution, Convert.ToByte(30)),
          }));
      }

      foreach (var index in Enumerable.Range(0, result.ModelCount)) {
        var model = result.Models[index];
        var binding = result.ModelBindings[index];
        var expectedState = binding.Role == TerrainBlendLayerPassRole.Base
          ? MaterialRenderState.Opaque
          : MaterialRenderState.AdditiveContribution;
        using (Assert.EnterMultipleScope()) {
          Assert.That(binding.BatchIndex, Is.EqualTo(index));
          Assert.That(binding.Model, Is.SameAs(model));
          Assert.That(model.Mesh, Is.SameAs(binding.Batch.Mesh));
          Assert.That(model.Material, Is.SameAs(binding.Material));
          Assert.That(binding.Material.AlbedoTexture,
            Is.SameAs(textures[binding.SurfaceIndex]));
          Assert.That(binding.AlbedoTexture,
            Is.SameAs(textures[binding.SurfaceIndex]));
          Assert.That(binding.Material.RenderState, Is.EqualTo(expectedState));
        }
      }
    } finally {
      DisposeModels(result.Models);
      DisposeTextures(textures.Values);
    }

    Assert.That(result.Models.Select(model => model.Mesh.State),
      Is.All.EqualTo(State.Disposed));
    Assert.That(result.ModelBindings.Select(binding => binding.Material.State),
      Is.All.EqualTo(State.Disposed));
  }

  [Test]
  public void Build_ReusedOrUnsortedBatchOutputsAreRejectedAndReleasedInReverse() {
    var terrain = NewTerrain(1, 1, (_, _) => 1);
    var first = NewMesh("first");
    var second = NewMesh("second");
    var reusedBatches = new[] {
      new TerrainBlendLayerMeshBatch(TerrainBlendLayerPassRole.Base, 1, first),
      new TerrainBlendLayerMeshBatch(TerrainBlendLayerPassRole.Base, 2, first),
      new TerrainBlendLayerMeshBatch(TerrainBlendLayerPassRole.Contribution, 1, second),
    };
    var reusedCleanup = new List<Mesh>();
    var reusedOperations = Operations(reusedBatches, new Dictionary<byte, Texture>()) with {
      ResolveSurfaceTexture = (_, _) => throw new AssertionException("unexpected lookup"),
      DisposeMesh = mesh => {
        reusedCleanup.Add(mesh);
        mesh.Dispose();
      },
    };

    var reusedError = Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendSceneBuilder.Build(
        terrain, Vector4.One, "Reused", reusedOperations)));

    var unsortedFirst = NewMesh("unsorted-first");
    var unsortedSecond = NewMesh("unsorted-second");
    var unsortedBatches = new[] {
      new TerrainBlendLayerMeshBatch(
        TerrainBlendLayerPassRole.Contribution, 1, unsortedFirst),
      new TerrainBlendLayerMeshBatch(
        TerrainBlendLayerPassRole.Base, 2, unsortedSecond),
    };
    var unsortedCleanup = new List<Mesh>();
    var unsortedOperations = Operations(
      unsortedBatches, new Dictionary<byte, Texture>()) with {
      ResolveSurfaceTexture = (_, _) => throw new AssertionException("unexpected lookup"),
      DisposeMesh = mesh => {
        unsortedCleanup.Add(mesh);
        mesh.Dispose();
      },
    };

    var unsortedError = Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendSceneBuilder.Build(
        terrain, Vector4.One, "Unsorted", unsortedOperations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(reusedError!.Message, Does.Contain("reused a mesh"));
      Assert.That(reusedCleanup, Is.EqualTo(new[] { second, first }));
      Assert.That(unsortedError!.Message, Does.Contain("strict role and surface order"));
      Assert.That(unsortedCleanup,
        Is.EqualTo(new[] { unsortedSecond, unsortedFirst }));
    }
  }

  [Test]
  public void Build_DisposedCatalogTextureIsRejectedWithoutClaimingTextureOwnership() {
    var terrain = NewTerrain(1, 1, (_, _) => 1);
    var mesh = NewMesh("surface");
    var texture = NewTexture("disposed");
    texture.Dispose();
    var materialCalls = 0;
    var operations = Operations(
      [new TerrainBlendLayerMeshBatch(TerrainBlendLayerPassRole.Base, 1, mesh)],
      new Dictionary<byte, Texture> { [1] = texture }) with {
      CreateMaterial = pass => {
        materialCalls++;
        return new TerrainBlend(pass);
      },
    };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendSceneBuilder.Build(
        terrain, Vector4.One, "Disposed Texture", operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("disposed catalog texture"));
      Assert.That(materialCalls, Is.Zero);
      Assert.That(mesh.State, Is.EqualTo(State.Disposed));
      Assert.That(texture.State, Is.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Build_MaterialMustBeFreshTerrainBlendWithTheExactRole() {
    var terrain = NewTerrain(1, 1, (_, _) => 1);
    var texture = NewTexture("surface");
    var wrongRoleMesh = NewMesh("wrong-role");
    var wrongRoleMaterial = new TerrainBlend(TerrainBlendPass.Contribution);
    var wrongRoleOperations = Operations(
      [new TerrainBlendLayerMeshBatch(
        TerrainBlendLayerPassRole.Base, 1, wrongRoleMesh)],
      new Dictionary<byte, Texture> { [1] = texture }) with {
      CreateMaterial = _ => wrongRoleMaterial,
    };

    var wrongRoleError = Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendSceneBuilder.Build(
        terrain, Vector4.One, "Wrong Role", wrongRoleOperations)));

    var wrongTypeMesh = NewMesh("wrong-type");
    var wrongTypeMaterial = new Flat();
    var wrongTypeOperations = Operations(
      [new TerrainBlendLayerMeshBatch(
        TerrainBlendLayerPassRole.Base, 1, wrongTypeMesh)],
      new Dictionary<byte, Texture> { [1] = texture }) with {
      CreateMaterial = _ => wrongTypeMaterial,
    };

    var wrongTypeError = Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendSceneBuilder.Build(
        terrain, Vector4.One, "Wrong Type", wrongTypeOperations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(wrongRoleError!.Message, Does.Contain("exact blend role"));
      Assert.That(wrongRoleMaterial.State, Is.EqualTo(State.Disposed));
      Assert.That(wrongRoleMesh.State, Is.EqualTo(State.Disposed));
      Assert.That(wrongTypeError!.Message, Does.Contain("non-terrain material"));
      Assert.That(wrongTypeMaterial.State, Is.EqualTo(State.Disposed));
      Assert.That(wrongTypeMesh.State, Is.EqualTo(State.Disposed));
    }
    texture.Dispose();
  }

  [Test]
  public void Build_ReusedMaterialIsRejectedWithoutDoubleClaimingIt() {
    var terrain = NewTerrain(1, 1, (_, _) => 1);
    var firstMesh = NewMesh("first");
    var secondMesh = NewMesh("second");
    var texture = NewTexture("surface");
    var reused = new TerrainBlend(TerrainBlendPass.Base);
    var directMaterialCleanup = 0;
    var modelCleanup = 0;
    var meshCleanup = 0;
    var operations = Operations(
      [
        new TerrainBlendLayerMeshBatch(TerrainBlendLayerPassRole.Base, 1, firstMesh),
        new TerrainBlendLayerMeshBatch(TerrainBlendLayerPassRole.Base, 2, secondMesh),
      ],
      new Dictionary<byte, Texture> { [1] = texture, [2] = texture }) with {
      CreateMaterial = _ => reused,
      DisposeMaterial = material => {
        directMaterialCleanup++;
        material.Dispose();
      },
      DisposeModel = model => {
        modelCleanup++;
        model.Dispose();
      },
      DisposeMesh = mesh => {
        meshCleanup++;
        mesh.Dispose();
      },
    };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendSceneBuilder.Build(
        terrain, Vector4.One, "Reused Material", operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("reused a material"));
      Assert.That(directMaterialCleanup, Is.Zero);
      Assert.That(modelCleanup, Is.EqualTo(1));
      Assert.That(meshCleanup, Is.EqualTo(1));
      Assert.That(reused.State, Is.EqualTo(State.Disposed));
      Assert.That(firstMesh.State, Is.EqualTo(State.Disposed));
      Assert.That(secondMesh.State, Is.EqualTo(State.Disposed));
    }
    texture.Dispose();
  }

  [Test]
  public void Build_ModelPreloadedWithCreatedMaterialReleasesItThroughTheModel() {
    var terrain = NewTerrain(1, 1, (_, _) => 1);
    var mesh = NewMesh("surface");
    var texture = NewTexture("surface");
    TerrainBlend? material = null;
    var directMaterialCleanup = 0;
    var modelCleanup = 0;
    var operations = Operations(
      [new TerrainBlendLayerMeshBatch(TerrainBlendLayerPassRole.Base, 1, mesh)],
      new Dictionary<byte, Texture> { [1] = texture }) with {
      CreateMaterial = pass => material = new TerrainBlend(pass),
      CreateModel = source => new Model(source) { Material = material },
      DisposeMaterial = resource => {
        directMaterialCleanup++;
        resource.Dispose();
      },
      DisposeModel = model => {
        modelCleanup++;
        model.Dispose();
      },
    };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendSceneBuilder.Build(
        terrain, Vector4.One, "Preloaded Material", operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("fresh model"));
      Assert.That(modelCleanup, Is.EqualTo(1));
      Assert.That(directMaterialCleanup, Is.Zero);
      Assert.That(material!.State, Is.EqualTo(State.Disposed));
      Assert.That(mesh.State, Is.EqualTo(State.Disposed));
    }
    texture.Dispose();
  }

  [Test]
  public void Build_ModelMustAdoptTheExactLayerMesh() {
    var terrain = NewTerrain(1, 1, (_, _) => 1);
    var layerMesh = NewMesh("layer");
    var wrongMesh = NewMesh("wrong");
    var texture = NewTexture("surface");
    var cleanup = new List<string>();
    var operations = Operations(
      [new TerrainBlendLayerMeshBatch(
        TerrainBlendLayerPassRole.Base, 1, layerMesh)],
      new Dictionary<byte, Texture> { [1] = texture }) with {
      CreateModel = _ => new Model(wrongMesh),
      DisposeModel = model => {
        cleanup.Add("model");
        model.Dispose();
      },
      DisposeMaterial = material => {
        cleanup.Add("material");
        material.Dispose();
      },
      DisposeMesh = mesh => {
        cleanup.Add("mesh");
        mesh.Dispose();
      },
    };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendSceneBuilder.Build(
        terrain, Vector4.One, "Wrong Model Mesh", operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Does.Contain("did not adopt its layer mesh"));
      Assert.That(cleanup, Is.EqualTo(new[] { "model", "material", "mesh" }));
      Assert.That(layerMesh.State, Is.EqualTo(State.Disposed));
      Assert.That(wrongMesh.State, Is.EqualTo(State.Disposed));
    }
    texture.Dispose();
  }

  [Test]
  public void Build_LaterFactoryFailureReleasesResourcesInReverseAcquisitionOrder() {
    var terrain = NewTerrain(1, 1, (_, _) => 1);
    var meshes = new[] { NewMesh("zero"), NewMesh("one"), NewMesh("two") };
    var textures = new Dictionary<byte, Texture> {
      [1] = NewTexture("one"),
      [2] = NewTexture("two"),
    };
    var batches = new[] {
      new TerrainBlendLayerMeshBatch(TerrainBlendLayerPassRole.Base, 1, meshes[0]),
      new TerrainBlendLayerMeshBatch(TerrainBlendLayerPassRole.Base, 2, meshes[1]),
      new TerrainBlendLayerMeshBatch(
        TerrainBlendLayerPassRole.Contribution, 1, meshes[2]),
    };
    var materials = new List<Material>();
    var models = new List<Model>();
    var cleanup = new List<string>();
    var modelCalls = 0;
    var operations = Operations(batches, textures) with {
      CreateMaterial = pass => {
        var material = new TerrainBlend(pass);
        materials.Add(material);
        return material;
      },
      CreateModel = mesh => {
        modelCalls++;
        if (modelCalls == 2) throw new InvalidOperationException("model failure");
        var model = new Model(mesh);
        models.Add(model);
        return model;
      },
      DisposeMaterial = material => {
        cleanup.Add($"material-{materials.IndexOf(material)}");
        material.Dispose();
      },
      DisposeModel = model => {
        cleanup.Add($"model-{models.IndexOf(model)}");
        model.Dispose();
      },
      DisposeMesh = mesh => {
        cleanup.Add($"mesh-{Array.IndexOf(meshes, mesh)}");
        mesh.Dispose();
      },
    };

    var error = Assert.Throws<InvalidOperationException>(new Action(() =>
      TerrainBlendSceneBuilder.Build(
        terrain, Vector4.One, "Later Failure", operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.Message, Is.EqualTo("model failure"));
      Assert.That(cleanup, Is.EqualTo(new[] {
        "material-1",
        "model-0",
        "mesh-2",
        "mesh-1",
      }));
      Assert.That(meshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
      Assert.That(materials.Select(material => material.State),
        Is.All.EqualTo(State.Disposed));
    }
    DisposeTextures(textures.Values);
  }

  [Test]
  public void Build_PrimaryAndEveryCleanupFailureAreReportedInOrder() {
    var terrain = NewTerrain(1, 1, (_, _) => 1);
    var meshes = new[] { NewMesh("zero"), NewMesh("one") };
    var texture = NewTexture("surface");
    var primaryError = new InvalidOperationException("model failure");
    var materialError = new IOException("material cleanup failure");
    var modelError = new IOException("model cleanup failure");
    var meshError = new IOException("mesh cleanup failure");
    var modelCalls = 0;
    var operations = Operations(
      [
        new TerrainBlendLayerMeshBatch(TerrainBlendLayerPassRole.Base, 1, meshes[0]),
        new TerrainBlendLayerMeshBatch(TerrainBlendLayerPassRole.Base, 2, meshes[1]),
      ],
      new Dictionary<byte, Texture> { [1] = texture, [2] = texture }) with {
      CreateModel = mesh => {
        modelCalls++;
        if (modelCalls == 2) throw primaryError;
        return new Model(mesh);
      },
      DisposeMaterial = material => {
        material.Dispose();
        throw materialError;
      },
      DisposeModel = model => {
        model.Dispose();
        throw modelError;
      },
      DisposeMesh = mesh => {
        mesh.Dispose();
        throw meshError;
      },
    };

    var error = Assert.Throws<AggregateException>(new Action(() =>
      TerrainBlendSceneBuilder.Build(
        terrain, Vector4.One, "Cleanup Failure", operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error!.InnerExceptions, Is.EqualTo(new Exception[] {
        primaryError,
        materialError,
        modelError,
        meshError,
      }));
      Assert.That(meshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
    }
    texture.Dispose();
  }

  private static TerrainBlendSceneBuilderOperations Operations(
    IReadOnlyList<TerrainBlendLayerMeshBatch> batches,
    IReadOnlyDictionary<byte, Texture> textures
  ) => TerrainBlendSceneBuilderOperations.Default with {
    BuildMeshBatches = (_, _, _) => batches,
    ResolveSurfaceTexture = (_, surface) => textures[surface],
  };

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

  private static Mesh NewMesh(string name) => new(
    [
      new Vertex { Position = Vector3.Zero, Normal = Vector3.UnitZ, Color = Vector4.One },
      new Vertex { Position = Vector3.UnitX, Normal = Vector3.UnitZ, Color = Vector4.One },
      new Vertex { Position = Vector3.UnitY, Normal = Vector3.UnitZ, Color = Vector4.One },
    ],
    [0, 1, 2]) { Name = name };

  private static Texture NewTexture(string name) => new(
    name,
    1,
    1,
    new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3, byte.MaxValue)));

  private static void DisposeModels(IEnumerable<Model> models) {
    foreach (var model in models.Reverse()) model.Dispose();
  }

  private static void DisposeTextures(IEnumerable<Texture> textures) {
    foreach (var texture in textures.Distinct()) texture.Dispose();
  }
}
