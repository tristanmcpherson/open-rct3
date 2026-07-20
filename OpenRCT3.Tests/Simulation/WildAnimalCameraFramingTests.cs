// Wild Animal Camera Framing Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalCameraFramingTests {
  private const float Epsilon = 0.001f;

  [Test]
  public void Calculate_UsesOneRenderedTranslationPerBuiltPlacement() {
    var first = new Vector3(10f, 20f, 30f);
    var second = new Vector3(30f, 40f, 50f);
    using var fixture = new SceneFixture(
      new PlacementInput(0, first, 2),
      new PlacementInput(1, second, 1));

    var framing = WildAnimalCameraFraming.Calculate(fixture.Scene);

    Assert.That(framing.HasValue, Is.True);
    var expectedTarget = (first + second) * 0.5f;
    var expectedRadius = Vector3.Distance(expectedTarget, first);
    using (Assert.EnterMultipleScope()) {
      Assert.That(framing!.Value.PlacementCount, Is.EqualTo(2));
      Assert.That(framing.Value.Target.X, Is.EqualTo(expectedTarget.X).Within(Epsilon));
      Assert.That(framing.Value.Target.Y, Is.EqualTo(expectedTarget.Y).Within(Epsilon));
      Assert.That(framing.Value.Target.Z, Is.EqualTo(expectedTarget.Z).Within(Epsilon));
      Assert.That(framing.Value.Target,
        Is.Not.EqualTo(((first * 2f) + second) / 3f));
      Assert.That(framing.Value.Radius, Is.EqualTo(expectedRadius).Within(Epsilon));
      Assert.That(
        framing.Value.Distance,
        Is.EqualTo((expectedRadius * 2f) + 8f).Within(Epsilon));
      Assert.That(framing.Value.MinimumDistance,
        Is.EqualTo(Camera.NearPlaneDistance * 2f));
      Assert.That(float.IsFinite(framing.Value.Target.X), Is.True);
      Assert.That(float.IsFinite(framing.Value.Target.Y), Is.True);
      Assert.That(float.IsFinite(framing.Value.Target.Z), Is.True);
      Assert.That(float.IsFinite(framing.Value.Radius), Is.True);
    }
  }

  [Test]
  public void Calculate_ClampsNearAndFarDiagnosticDistances() {
    using var near = new SceneFixture(
      new PlacementInput(0, new Vector3(5f, 6f, 7f), 1));
    using var far = new SceneFixture(
      new PlacementInput(0, new Vector3(-100f, 0f, 0f), 1),
      new PlacementInput(1, new Vector3(100f, 0f, 0f), 1));

    var nearFraming = WildAnimalCameraFraming.Calculate(near.Scene);
    var farFraming = WildAnimalCameraFraming.Calculate(far.Scene);

    using (Assert.EnterMultipleScope()) {
      Assert.That(nearFraming!.Value.Radius, Is.Zero);
      Assert.That(nearFraming.Value.Distance,
        Is.EqualTo(WildAnimalCameraFraming.MinimumDiagnosticDistance));
      Assert.That(farFraming!.Value.Radius, Is.EqualTo(100f).Within(Epsilon));
      Assert.That(farFraming.Value.Distance,
        Is.EqualTo(WildAnimalCameraFraming.MaximumDiagnosticDistance));
      Assert.That(nearFraming.Value.MinimumDistance,
        Is.EqualTo(WildAnimalCameraFraming.MinimumCameraDistance));
      Assert.That(farFraming.Value.MinimumDistance,
        Is.EqualTo(WildAnimalCameraFraming.MinimumCameraDistance));
    }
  }

  [Test]
  public void Calculate_ReturnsNoFrameForValidSceneWithoutBuiltPlacements() {
    var scene = new WildAnimalStaticSceneBuildResult(
      Array.AsReadOnly(Array.Empty<Model>()),
      Array.AsReadOnly(Array.Empty<WildAnimalStaticSceneModelBinding>()),
      SourcePlacementCount: 2,
      VisiblePlacementCount: 1,
      HiddenPlacementCount: 1,
      BuiltPlacementCount: 0,
      SkippedPlacementCount: 2,
      ModelCount: 0,
      MissingMaterialBatchCount: 1,
      ClonedVertexCount: 0,
      ClonedIndexCount: 0);

    Assert.That(WildAnimalCameraFraming.Calculate(scene), Is.Null);
  }

  [TestCase(MalformedCameraScene.SummaryCounts)]
  [TestCase(MalformedCameraScene.ModelIdentity)]
  [TestCase(MalformedCameraScene.BatchIdentity)]
  [TestCase(MalformedCameraScene.NonFiniteTransform)]
  [TestCase(MalformedCameraScene.ChangedSavedTransform)]
  public void Calculate_RejectsMalformedSceneOrBindingBeforeReturningAFrame(
    MalformedCameraScene malformed
  ) {
    using var fixture = new SceneFixture(
      new PlacementInput(0, new Vector3(1f, 2f, 3f), 2),
      new PlacementInput(1, new Vector3(4f, 5f, 6f), 1));
    var scene = fixture.Scene;
    switch (malformed) {
      case MalformedCameraScene.SummaryCounts:
        scene = scene with { BuiltPlacementCount = 1 };
        break;
      case MalformedCameraScene.ModelIdentity:
        scene = scene with {
          ModelBindings = ReplaceBinding(
            scene,
            0,
            scene.ModelBindings[0] with { Model = scene.Models[1] }),
        };
        break;
      case MalformedCameraScene.BatchIdentity:
        scene = scene with {
          ModelBindings = ReplaceBinding(
            scene,
            0,
            scene.ModelBindings[0] with { Batch = scene.ModelBindings[1].Batch }),
        };
        break;
      case MalformedCameraScene.NonFiniteTransform:
        var nonFinite = scene.Models[0].Transform.Matrix;
        nonFinite.M41 = float.NaN;
        scene.Models[0].Transform.Matrix = nonFinite;
        break;
      case MalformedCameraScene.ChangedSavedTransform:
        scene.Models[0].Transform.Matrix *= Matrix4x4.CreateTranslation(1f, 0f, 0f);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(malformed));
    }

    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalCameraFraming.Calculate(scene)));
  }

  private static IReadOnlyList<WildAnimalStaticSceneModelBinding> ReplaceBinding(
    WildAnimalStaticSceneBuildResult scene,
    int index,
    WildAnimalStaticSceneModelBinding replacement
  ) {
    var bindings = scene.ModelBindings.ToArray();
    bindings[index] = replacement;
    return Array.AsReadOnly(bindings);
  }

  public enum MalformedCameraScene {
    SummaryCounts,
    ModelIdentity,
    BatchIdentity,
    NonFiniteTransform,
    ChangedSavedTransform,
  }

  private sealed record PlacementInput(int Type, Vector3 Position, int BatchCount);

  private sealed class SceneFixture : IDisposable {
    private readonly List<Mesh> templateMeshes = [];

    public SceneFixture(params PlacementInput[] placements) {
      if (placements.Length == 0)
        throw new ArgumentException("The fixture needs at least one placement.");
      var batchCounts = Enumerable.Range(0, 4)
        .Select(type => BatchCount(placements, type))
        .ToArray();
      var root = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        $"OpenRCT3-WildAnimalCameraFraming-{Guid.NewGuid():N}"));
      var speciesCommonPath = Path.Combine(root, "WildAnimals.common.ovl");
      var modelCommonPath = Path.Combine(root, "Models.common.ovl");
      var decodedVariants = Enumerable.Range(0, 4)
        .Select(type => new WildAnimalSpeciesVariant(
          $"Animal{type}:mdl",
          $"Animal{type}:wad"))
        .ToArray();
      var decodedSpecies = new WildAnimalSpeciesDefinition(
        "Species",
        @"WildAnimals\Species_data",
        decodedVariants);
      var variantTemplates = new WildAnimalModelVariantTemplateLink[4];
      var variantLinks = new WildAnimalSpeciesModelVariantLink[4];
      foreach (var type in Enumerable.Range(0, 4)) {
        var definition = Definition(
          $"Animal{type}",
          modelCommonPath,
          batchCounts[type]);
        var source = new WildAnimalSpeciesModelResourceSource(
          new OvlFile(definition.Name, FileType.Model, modelCommonPath),
          definition);
        var link = new WildAnimalSpeciesModelVariantLink(
          type,
          decodedVariants[type],
          source);
        var batches = ModelDefinitionMeshBuilder.BuildBatches(definition);
        templateMeshes.AddRange(batches.Select(batch => batch.Mesh));
        variantLinks[type] = link;
        variantTemplates[type] = new(
          link,
          new WildAnimalModelTemplate(source, batches));
      }
      var bridge = new WildAnimalSpeciesModelResourceBridgeResult(
        "Species:was",
        speciesCommonPath,
        new OvlFile("Species", FileType.WildAnimalSpecies, speciesCommonPath),
        decodedSpecies,
        modelCommonPath,
        Array.AsReadOnly(variantLinks));
      var datSpecies = new DatWildAnimalSpeciesDatabaseEntryData(
        100,
        true,
        @"WildAnimals\WildAnimals",
        "Species");
      var speciesResource = new WildAnimalParkSpeciesResource(
        0,
        datSpecies,
        bridge,
        Array.AsReadOnly(Enumerable.Range(0, placements.Length).ToArray()));
      var models = new List<Model>();
      var bindings = new List<WildAnimalStaticSceneModelBinding>();
      foreach (var placementIndex in Enumerable.Range(0, placements.Length)) {
        var input = placements[placementIndex];
        var animal = new DatWildAnimalData(
          Convert.ToUInt64(1_000 + placementIndex),
          datSpecies.EntryId,
          Convert.ToUInt64(2_000 + placementIndex),
          input.Type is 0 or 1,
          input.Type is 0 or 2,
          input.Type);
        var nativeTransform = Matrix4x4.CreateTranslation(
          input.Position.X,
          input.Position.Z,
          input.Position.Y);
        var visual = new DatWildAnimalVisualData(
          animal.VisualEntryId,
          [],
          true,
          true,
          nativeTransform);
        var placement = new WildAnimalParkPlacementResource(
          placementIndex,
          new DatWildAnimalPlacementData(
            animal,
            datSpecies,
            visual,
            DatWildAnimalVariantSelectionStatus.Unsupported),
          speciesResource);
        var selection = new WildAnimalSavedVariantSelection(
          animal,
          input.Type,
          variantTemplates[input.Type]);
        var transform = WildAnimalWorldTransform.ToPark(nativeTransform);
        foreach (var batchIndex in Enumerable.Range(
                   0,
                   selection.Variant.Template.Batches.Count)) {
          var batch = selection.Variant.Template.Batches[batchIndex];
          var mesh = new Mesh(
            new List<Vertex>(batch.Mesh.Vertices),
            new List<uint>(batch.Mesh.Indices));
          var model = new Model(mesh) { Material = new Flat() };
          model.Transform.Matrix = transform;
          models.Add(model);
          bindings.Add(new(
            model,
            placement,
            selection,
            batchIndex,
            batch));
        }
      }
      Scene = new(
        Array.AsReadOnly(models.ToArray()),
        Array.AsReadOnly(bindings.ToArray()),
        placements.Length,
        placements.Length,
        0,
        placements.Length,
        0,
        models.Count,
        0,
        Convert.ToUInt64(models.Count * 3),
        Convert.ToUInt64(models.Count * 3));
    }

    public WildAnimalStaticSceneBuildResult Scene { get; }

    public void Dispose() {
      foreach (var model in Scene.Models.Reverse()) model.Dispose();
      foreach (var mesh in templateMeshes.AsEnumerable().Reverse()) mesh.Dispose();
    }

    private static int BatchCount(IReadOnlyList<PlacementInput> placements, int type) {
      var counts = placements
        .Where(placement => placement.Type == type)
        .Select(placement => placement.BatchCount)
        .Distinct()
        .ToArray();
      if (counts.Length > 1 || counts.FirstOrDefault(1) <= 0)
        throw new ArgumentException("One variant needs one positive fixture batch count.");
      return counts.FirstOrDefault(1);
    }

    private static ModelDefinition Definition(string name, string path, int batchCount) => new(
      name,
      path,
      100,
      200,
      0,
      0,
      0,
      0) {
      Bones = [],
      Groups = [Group(Enumerable.Range(0, batchCount).Select(Mesh).ToArray())],
    };

    private static ModelGroup Group(params ModelMesh[] meshes) => new(
      0,
      Convert.ToUInt16(meshes.Length),
      0,
      0,
      [],
      0,
      meshes,
      []);

    private static ModelMesh Mesh(int index) {
      var offset = Convert.ToSingle(index * 10);
      var vertices = new[] {
        Vertex(new Vector3(offset, 0f, 0f)),
        Vertex(new Vector3(offset, 0f, -1f)),
        Vertex(new Vector3(offset + 1f, 0f, 0f)),
      };
      var indices = new uint[] { 0, 2, 1 };
      return new(
        0x1305,
        Convert.ToUInt32(indices.Length),
        1,
        Convert.ToUInt16(vertices.Length),
        0,
        0,
        0,
        0,
        vertices,
        indices);
    }

    private static BoneShapeVertex Vertex(Vector3 position) => new(
      position,
      Vector3.UnitY,
      Vector2.Zero,
      Vector4.One,
      new BoneShapeSkinning(255, 255, 255, 255, 0, 0, 0, 0));
  }
}
