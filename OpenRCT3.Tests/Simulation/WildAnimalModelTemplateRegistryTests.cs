// Wild Animal Model Template Registry Tests
//
// Copyright Â© 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalModelTemplateRegistryTests {
  [Test]
  public void Build_AdaptsDistinctSourcesOnceAndSharesSerializedVariantTemplates() {
    var fixture = new RegistryFixture();
    var adaptations = new List<ModelDefinition>();

    using var registry = WildAnimalModelTemplateRegistry.Build(
      fixture.Resources,
      model => {
        adaptations.Add(model);
        return ModelDefinitionMeshBuilder.BuildBatches(model);
      },
      mesh => mesh.Dispose());

    using (Assert.EnterMultipleScope()) {
      Assert.That(adaptations, Is.EqualTo(new[] { fixture.AdultModel, fixture.BabyModel }));
      Assert.That(registry.VariantCount, Is.EqualTo(4));
      Assert.That(registry.DistinctModelSourceCount, Is.EqualTo(2));
      Assert.That(registry.GroupCount, Is.EqualTo(3));
      Assert.That(registry.NonemptyGroupCount, Is.EqualTo(2));
      Assert.That(registry.BatchCount, Is.EqualTo(3));
      Assert.That(registry.VertexCount, Is.EqualTo(9));
      Assert.That(registry.IndexCount, Is.EqualTo(9));
      Assert.That(
        registry.Variants.Select(variant => variant.VariantLink.SerializedIndex),
        Is.EqualTo(new[] { 0, 1, 2, 3 }));
      Assert.That(
        registry.Variants.Select(variant => variant.VariantLink),
        Is.EqualTo(fixture.Resources.Variants));
      Assert.That(
        registry.Templates.Select(template => template.ModelSource.Identity),
        Is.EqualTo(new[] { "AdultElephant:mdl", "BabyElephant:mdl" }));
      Assert.That(
        registry.Templates.SelectMany(template => template.Batches)
          .Select(batch => batch.SourceMeshName),
        Is.EqualTo(new[] {
          "AdultElephant/group-0/mesh-0",
          "BabyElephant/group-1/mesh-0",
          "BabyElephant/group-1/mesh-1",
        }));
    }
    Assert.That(registry.Variants[1].Template,
      Is.SameAs(registry.Variants[0].Template));
    Assert.That(registry.Variants[3].Template,
      Is.SameAs(registry.Variants[2].Template));
    Assert.That(registry.Variants[0].VariantLink,
      Is.SameAs(fixture.Resources.Variants[0]));
  }

  [Test]
  public void Dispose_ReleasesEveryUniqueTemplateMeshOnceAndIsIdempotent() {
    var fixture = new RegistryFixture();
    var disposed = new List<Mesh>();
    var registry = WildAnimalModelTemplateRegistry.Build(
      fixture.Resources,
      ModelDefinitionMeshBuilder.BuildBatches,
      mesh => {
        disposed.Add(mesh);
        mesh.Dispose();
      });
    var meshes = registry.Templates.SelectMany(template => template.Batches)
      .Select(batch => batch.Mesh).ToArray();

    registry.Dispose();
    registry.Dispose();

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.IsDisposed, Is.True);
      Assert.That(disposed, Is.EqualTo(meshes.Reverse()));
      Assert.That(disposed, Has.Count.EqualTo(3));
      Assert.That(meshes.Select(mesh => mesh.State), Is.All.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Build_LaterAdapterFailureDisposesEarlierTemplateMeshes() {
    var fixture = new RegistryFixture();
    var created = new List<Mesh>();
    var disposed = new List<Mesh>();

    var exception = Assert.Throws<InvalidOperationException>(new Action(() =>
      WildAnimalModelTemplateRegistry.Build(
        fixture.Resources,
        model => {
          if (ReferenceEquals(model, fixture.BabyModel))
            throw new InvalidOperationException("baby adaptation failed");
          var batches = ModelDefinitionMeshBuilder.BuildBatches(model);
          created.AddRange(batches.Select(batch => batch.Mesh));
          return batches;
        },
        mesh => {
          disposed.Add(mesh);
          mesh.Dispose();
        })));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Is.EqualTo("baby adaptation failed"));
      Assert.That(created, Has.Count.EqualTo(1));
      Assert.That(disposed, Is.EqualTo(created));
      Assert.That(created[0].State, Is.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void Build_DuplicateAdapterMeshFailsClosedAndDisposesItOnce() {
    var fixture = new RegistryFixture();
    var reused = new Mesh(
      [new Vertex(), new Vertex(), new Vertex()],
      [0, 1, 2]);
    var disposed = new List<Mesh>();

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalModelTemplateRegistry.Build(
        fixture.Resources,
        model => model.Name == "AdultElephant"
          ? [Batch(model, 0, 0, reused)]
          : [Batch(model, 1, 0, reused), Batch(model, 1, 1, reused)],
        mesh => {
          disposed.Add(mesh);
          mesh.Dispose();
        })));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("reused a mesh"));
      Assert.That(disposed, Is.EqualTo(new[] { reused }));
      Assert.That(reused.State, Is.EqualTo(State.Disposed));
    }
  }

  [TestCase(MalformedRegistry.NullSpeciesFile)]
  [TestCase(MalformedRegistry.SpeciesIdentityMismatch)]
  [TestCase(MalformedRegistry.CrossSourceSpeciesFile)]
  [TestCase(MalformedRegistry.NullSpeciesVariants)]
  [TestCase(MalformedRegistry.WrongSpeciesVariantCount)]
  [TestCase(MalformedRegistry.NullBridgeVariants)]
  [TestCase(MalformedRegistry.WrongBridgeVariantCount)]
  [TestCase(MalformedRegistry.DuplicateSerializedIndex)]
  [TestCase(MalformedRegistry.ChangedVariantIdentity)]
  [TestCase(MalformedRegistry.ChangedRepeatedModelSource)]
  [TestCase(MalformedRegistry.CrossModelSource)]
  [TestCase(MalformedRegistry.WrongModelFileType)]
  [TestCase(MalformedRegistry.ModelNameMismatch)]
  [TestCase(MalformedRegistry.ModelOwnerMismatch)]
  public void Build_RejectsChangedDuplicateOrCrossSourceEvidence(
    MalformedRegistry malformed
  ) {
    var fixture = new RegistryFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalModelTemplateRegistry.Build(fixture.Resources)));
  }

  private static ModelDefinitionMeshBatch Batch(
    ModelDefinition model,
    int groupIndex,
    int meshIndex,
    Mesh mesh
  ) {
    var name = $"{model.Name}/group-{groupIndex}/mesh-{meshIndex}";
    mesh.Name = name;
    return new ModelDefinitionMeshBatch(groupIndex, meshIndex, name, mesh);
  }

  private sealed class RegistryFixture {
    private readonly string speciesCommonPath;
    private readonly string speciesUniquePath;
    private readonly string modelCommonPath;
    private readonly WildAnimalSpeciesDefinition species;
    private readonly OvlFile speciesFile;
    private readonly WildAnimalSpeciesModelResourceSource adultSource;
    private readonly WildAnimalSpeciesModelResourceSource babySource;

    public RegistryFixture() {
      var root = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        "OpenRCT3-WildAnimalModelTemplateRegistry-Fixture"));
      speciesCommonPath = Path.Combine(root, "WildAnimals", "WildAnimals.common.ovl");
      speciesUniquePath = ToUniquePath(speciesCommonPath);
      modelCommonPath = Path.Combine(
        root, "WildAnimals", "elephant", "Elephant_data.common.ovl");
      species = new WildAnimalSpeciesDefinition(
        "Elephant",
        @"WildAnimals\elephant\Elephant_data",
        new[] {
          new WildAnimalSpeciesVariant("AdultElephant:mdl", "Elephant:wad"),
          new WildAnimalSpeciesVariant("AdultElephant:mdl", "Elephant:wad"),
          new WildAnimalSpeciesVariant("BabyElephant:mdl", "babyElephant:wad"),
          new WildAnimalSpeciesVariant("BabyElephant:mdl", "babyElephant:wad"),
        });
      speciesFile = new OvlFile("Elephant", FileType.WildAnimalSpecies, speciesUniquePath);
      AdultModel = Definition("AdultElephant", modelCommonPath, [Group(Mesh())]);
      BabyModel = Definition("BabyElephant", modelCommonPath, [Group(), Group(Mesh(), Mesh())]);
      adultSource = new WildAnimalSpeciesModelResourceSource(
        new OvlFile("AdultElephant", FileType.Model, modelCommonPath),
        AdultModel);
      babySource = new WildAnimalSpeciesModelResourceSource(
        new OvlFile("BabyElephant", FileType.Model, modelCommonPath),
        BabyModel);
      Resources = CreateResources();
    }

    public ModelDefinition AdultModel { get; }
    public ModelDefinition BabyModel { get; }
    public WildAnimalSpeciesModelResourceBridgeResult Resources { get; private set; }

    public void MakeMalformed(MalformedRegistry malformed) {
      switch (malformed) {
        case MalformedRegistry.NullSpeciesFile:
          Resources = Resources with { SpeciesFile = null! };
          break;
        case MalformedRegistry.SpeciesIdentityMismatch:
          Resources = Resources with { SpeciesReference = "Giraffe:was" };
          break;
        case MalformedRegistry.CrossSourceSpeciesFile:
          Resources = Resources with {
            SpeciesFile = speciesFile with { Path = modelCommonPath },
          };
          break;
        case MalformedRegistry.NullSpeciesVariants:
          Resources = Resources with { Species = species with { Variants = null! } };
          break;
        case MalformedRegistry.WrongSpeciesVariantCount:
          Resources = Resources with {
            Species = species with { Variants = species.Variants.Take(3).ToArray() },
          };
          break;
        case MalformedRegistry.NullBridgeVariants:
          Resources = Resources with { Variants = null! };
          break;
        case MalformedRegistry.WrongBridgeVariantCount:
          Resources = Resources with { Variants = Resources.Variants.Take(3).ToArray() };
          break;
        case MalformedRegistry.DuplicateSerializedIndex:
          ReplaceLink(1, Resources.Variants[1] with { SerializedIndex = 0 });
          break;
        case MalformedRegistry.ChangedVariantIdentity:
          ReplaceLink(0, Resources.Variants[0] with {
            Variant = Resources.Variants[0].Variant with { },
          });
          break;
        case MalformedRegistry.ChangedRepeatedModelSource:
          ReplaceLink(1, Resources.Variants[1] with {
            ModelSource = new WildAnimalSpeciesModelResourceSource(
              adultSource.File,
              adultSource.Resource),
          });
          break;
        case MalformedRegistry.CrossModelSource:
          ReplaceLink(2, Resources.Variants[2] with { ModelSource = adultSource });
          break;
        case MalformedRegistry.WrongModelFileType:
          ReplaceAdultSource(new WildAnimalSpeciesModelResourceSource(
            adultSource.File with { Type = FileType.BoneShape },
            AdultModel));
          break;
        case MalformedRegistry.ModelNameMismatch:
          ReplaceAdultSource(new WildAnimalSpeciesModelResourceSource(
            adultSource.File,
            AdultModel with { Name = "OtherAdult" }));
          break;
        case MalformedRegistry.ModelOwnerMismatch:
          ReplaceAdultSource(new WildAnimalSpeciesModelResourceSource(
            adultSource.File,
            AdultModel with { SourcePath = speciesCommonPath }));
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed));
      }
    }

    private WildAnimalSpeciesModelResourceBridgeResult CreateResources() {
      var links = new[] {
        new WildAnimalSpeciesModelVariantLink(0, species.Variants[0], adultSource),
        new WildAnimalSpeciesModelVariantLink(1, species.Variants[1], adultSource),
        new WildAnimalSpeciesModelVariantLink(2, species.Variants[2], babySource),
        new WildAnimalSpeciesModelVariantLink(3, species.Variants[3], babySource),
      };
      return new WildAnimalSpeciesModelResourceBridgeResult(
        "Elephant:was",
        speciesCommonPath,
        speciesFile,
        species,
        modelCommonPath,
        links);
    }

    private void ReplaceAdultSource(WildAnimalSpeciesModelResourceSource source) {
      ReplaceLink(0, Resources.Variants[0] with { ModelSource = source });
      ReplaceLink(1, Resources.Variants[1] with { ModelSource = source });
    }

    private void ReplaceLink(int index, WildAnimalSpeciesModelVariantLink replacement) {
      var links = Resources.Variants.ToArray();
      links[index] = replacement;
      Resources = Resources with { Variants = links };
    }

    private static string ToUniquePath(string commonPath) =>
      commonPath[..^".common.ovl".Length] + ".unique.ovl";
  }

  private static ModelDefinition Definition(
    string name,
    string path,
    IReadOnlyList<ModelGroup> groups
  ) => new(
    name,
    path,
    100,
    200,
    0,
    0,
    0,
    0) {
    Bones = [],
    Groups = groups,
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

  private static ModelMesh Mesh() {
    var vertices = new[] {
      Vertex(Vector3.Zero),
      Vertex(new Vector3(0, 0, -1)),
      Vertex(Vector3.UnitX),
    };
    var indices = new uint[] { 0, 2, 1 };
    return new ModelMesh(
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

public enum MalformedRegistry {
  NullSpeciesFile,
  SpeciesIdentityMismatch,
  CrossSourceSpeciesFile,
  NullSpeciesVariants,
  WrongSpeciesVariantCount,
  NullBridgeVariants,
  WrongBridgeVariantCount,
  DuplicateSerializedIndex,
  ChangedVariantIdentity,
  ChangedRepeatedModelSource,
  CrossModelSource,
  WrongModelFileType,
  ModelNameMismatch,
  ModelOwnerMismatch,
}
