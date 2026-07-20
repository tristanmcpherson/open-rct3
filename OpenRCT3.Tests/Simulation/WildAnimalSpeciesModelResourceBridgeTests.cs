using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalSpeciesModelResourceBridgeTests {
  [Test]
  public void ResolveInstalled_PreservesFourSerializedVariantsAndDecodesDistinctModelsOnce() {
    var fixture = new BridgeFixture();

    var result = fixture.Resolve();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.SpeciesReference, Is.EqualTo("Elephant:was"));
      Assert.That(result.SpeciesCommonPath, Is.EqualTo(fixture.SpeciesCommonPath));
      Assert.That(result.SpeciesFile.Path, Is.EqualTo(fixture.SpeciesUniquePath));
      Assert.That(result.ModelPackageCommonPath, Is.EqualTo(fixture.ModelCommonPath));
      Assert.That(result.Variants.Select(link => link.SerializedIndex),
        Is.EqualTo(new[] { 0, 1, 2, 3 }));
      Assert.That(result.Variants.Select(link => link.Variant.ModelReference),
        Is.EqualTo(new[] {
          "AdultElephant:mdl",
          "AdultElephant:mdl",
          "BabyElephant:mdl",
          "BabyElephant:mdl",
        }));
      Assert.That(result.Variants.Select(link => link.Variant.AnimationDataReference),
        Is.EqualTo(new[] {
          "Elephant:wad",
          "Elephant:wad",
          "babyElephant:wad",
          "babyElephant:wad",
        }));
      Assert.That(result.Variants.Select(link => link.ModelSource.Identity),
        Is.EqualTo(new[] {
          "AdultElephant:mdl",
          "AdultElephant:mdl",
          "BabyElephant:mdl",
          "BabyElephant:mdl",
        }));
      Assert.That(fixture.Source.LoadRequests,
        Is.EqualTo(new[] { fixture.SpeciesCommonPath, fixture.ModelCommonPath }));
      Assert.That(fixture.ModelArchive.ModelDecodeRequests,
        Is.EqualTo(new[] { "AdultElephant:mdl", "BabyElephant:mdl" }));
      Assert.That(fixture.SpeciesArchive.Disposed, Is.True);
      Assert.That(fixture.ModelArchive.Disposed, Is.True);
    }
    Assert.That(result.Variants[1].ModelSource, Is.SameAs(result.Variants[0].ModelSource));
    Assert.That(result.Variants[3].ModelSource, Is.SameAs(result.Variants[2].ModelSource));
  }

  [TestCase(MalformedBridge.MissingSpeciesCommon)]
  [TestCase(MalformedBridge.MissingSpeciesUnique)]
  [TestCase(MalformedBridge.WrongLoadedSpeciesPair)]
  [TestCase(MalformedBridge.MissingSpeciesSymbol)]
  [TestCase(MalformedBridge.AmbiguousSpeciesSymbol)]
  [TestCase(MalformedBridge.CrossPackageSpeciesSymbol)]
  [TestCase(MalformedBridge.SpeciesNameMismatch)]
  [TestCase(MalformedBridge.NullVariantList)]
  [TestCase(MalformedBridge.WrongVariantCount)]
  [TestCase(MalformedBridge.NullVariant)]
  [TestCase(MalformedBridge.InvalidModelReference)]
  [TestCase(MalformedBridge.InvalidAnimationReference)]
  [TestCase(MalformedBridge.PackagePathEscape)]
  [TestCase(MalformedBridge.RootedPackagePath)]
  [TestCase(MalformedBridge.PackagePathWithOvlSuffix)]
  [TestCase(MalformedBridge.MissingModelCommon)]
  [TestCase(MalformedBridge.MissingModelUnique)]
  [TestCase(MalformedBridge.WrongLoadedModelPair)]
  [TestCase(MalformedBridge.MissingModelSymbol)]
  [TestCase(MalformedBridge.AmbiguousModelSymbol)]
  [TestCase(MalformedBridge.CrossPackageModelSymbol)]
  [TestCase(MalformedBridge.ModelNameMismatch)]
  [TestCase(MalformedBridge.ModelSourceMismatch)]
  public void ResolveInstalled_RejectsMissingAmbiguousOrCrossPackageEvidence(
    MalformedBridge malformed
  ) {
    var fixture = new BridgeFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Resolve()));
  }

  [Test]
  public void ResolveInstalled_RejectsPaddedInstallationRoot() {
    var fixture = new BridgeFixture();

    Assert.Throws<ArgumentException>(new Action(() =>
      WildAnimalSpeciesModelResourceBridge.ResolveInstalled(
        $" {fixture.InstallRoot}",
        "Elephant:was",
        fixture.Source)));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void ResolveInstalled_FromElephantLinksExactFourModelsInSerializedOrder() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");

    var result = WildAnimalSpeciesModelResourceBridge.ResolveInstalled(
      rct3Path,
      "Elephant:was");

    var speciesCommonPath = Path.Combine(
      Path.GetFullPath(rct3Path),
      "WildAnimals",
      "WildAnimals.common.ovl");
    var speciesUniquePath = speciesCommonPath[..^".common.ovl".Length] + ".unique.ovl";
    var modelCommonPath = Path.Combine(
      Path.GetFullPath(rct3Path),
      "WildAnimals",
      "elephant",
      "Elephant_data.common.ovl");
    using (Assert.EnterMultipleScope()) {
      Assert.That(result.SpeciesCommonPath, Is.EqualTo(speciesCommonPath).IgnoreCase);
      Assert.That(result.SpeciesFile.Path, Is.EqualTo(speciesUniquePath).IgnoreCase);
      Assert.That(result.Species.Name, Is.EqualTo("elephant").IgnoreCase);
      Assert.That(result.Species.PackagePath,
        Is.EqualTo(@"WildAnimals\elephant\Elephant_data"));
      Assert.That(result.ModelPackageCommonPath, Is.EqualTo(modelCommonPath).IgnoreCase);
      Assert.That(result.Variants.Select(link => link.SerializedIndex),
        Is.EqualTo(new[] { 0, 1, 2, 3 }));
      Assert.That(result.Variants.Select(link => link.Variant.ModelReference),
        Is.EqualTo(new[] {
          "AdultElephant:mdl",
          "AdultElephant:mdl",
          "BabyElephant:mdl",
          "BabyElephant:mdl",
        }));
      Assert.That(result.Variants.Select(link => link.ModelSource.File.Path),
        Is.All.EqualTo(modelCommonPath).IgnoreCase);
      Assert.That(result.Variants.Select(link => link.ModelSource.Resource.SourcePath),
        Is.All.EqualTo(modelCommonPath).IgnoreCase);
      Assert.That(result.Variants.Select(link => link.ModelSource.Resource.Name),
        Is.EqualTo(new[] {
          "AdultElephant",
          "AdultElephant",
          "BabyElephant",
          "BabyElephant",
        }));
    }
    Assert.That(result.Variants[1].ModelSource, Is.SameAs(result.Variants[0].ModelSource));
    Assert.That(result.Variants[3].ModelSource, Is.SameAs(result.Variants[2].ModelSource));
    TestContext.Progress.WriteLine(
      $"Installed WAS→MDL bridge: {string.Join(", ", result.Variants.Select(link =>
        $"{link.SerializedIndex}:{link.ModelSource.Identity}@{link.ModelSource.File.Path}"))}");
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void ResolveInstalled_FromOstrichLinksExactFourModelsInSerializedOrder() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");

    var result = WildAnimalSpeciesModelResourceBridge.ResolveInstalled(
      rct3Path,
      "Ostrich:was");

    var speciesCommonPath = Path.Combine(
      Path.GetFullPath(rct3Path),
      "WildAnimals",
      "WildAnimals.common.ovl");
    var speciesUniquePath = speciesCommonPath[..^".common.ovl".Length] + ".unique.ovl";
    var modelCommonPath = Path.Combine(
      Path.GetFullPath(rct3Path),
      "WildAnimals",
      "Ostrich",
      "Ostrich_data.common.ovl");
    using (Assert.EnterMultipleScope()) {
      Assert.That(result.SpeciesCommonPath, Is.EqualTo(speciesCommonPath).IgnoreCase);
      Assert.That(result.SpeciesFile.Path, Is.EqualTo(speciesUniquePath).IgnoreCase);
      Assert.That(result.Species.Name, Is.EqualTo("ostrich").IgnoreCase);
      Assert.That(result.Species.PackagePath,
        Is.EqualTo(@"WildAnimals\Ostrich\Ostrich_data"));
      Assert.That(result.ModelPackageCommonPath, Is.EqualTo(modelCommonPath).IgnoreCase);
      Assert.That(result.Variants.Select(link => link.SerializedIndex),
        Is.EqualTo(new[] { 0, 1, 2, 3 }));
      Assert.That(result.Variants.Select(link => link.Variant.ModelReference),
        Is.EqualTo(new[] {
          "MaleOstrich:mdl",
          "FemaleOstrich:mdl",
          "BabyOstrich:mdl",
          "BabyOstrich:mdl",
        }));
      Assert.That(result.Variants.Select(link => link.Variant.AnimationDataReference),
        Is.EqualTo(new[] {
          "MaleOstrich:wad",
          "MaleOstrich:wad",
          "BabyOstrich:wad",
          "BabyOstrich:wad",
        }));
      Assert.That(result.Variants.Select(link => link.ModelSource.File.Path),
        Is.All.EqualTo(modelCommonPath).IgnoreCase);
      Assert.That(result.Variants.Select(link => link.ModelSource.Resource.SourcePath),
        Is.All.EqualTo(modelCommonPath).IgnoreCase);
      Assert.That(result.Variants.Select(link => link.ModelSource.Resource.Name),
        Is.EqualTo(new[] {
          "MaleOstrich",
          "FemaleOstrich",
          "BabyOstrich",
          "BabyOstrich",
        }));
    }
    Assert.That(result.Variants[3].ModelSource, Is.SameAs(result.Variants[2].ModelSource));
  }

  private sealed class BridgeFixture {
    public BridgeFixture() {
      InstallRoot = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        "OpenRCT3-WildAnimalSpeciesModelBridge-Fixture"));
      SpeciesCommonPath = Path.Combine(
        InstallRoot,
        "WildAnimals",
        "WildAnimals.common.ovl");
      SpeciesUniquePath = ToUniquePath(SpeciesCommonPath);
      ModelCommonPath = Path.Combine(
        InstallRoot,
        "WildAnimals",
        "elephant",
        "Elephant_data.common.ovl");
      ModelUniquePath = ToUniquePath(ModelCommonPath);
      SpeciesArchive = new FakeArchive(SpeciesCommonPath) {
        Species = SpeciesDefinition(),
      };
      SpeciesArchive.FilesList.Add(new OvlFile(
        "Elephant",
        FileType.WildAnimalSpecies,
        SpeciesUniquePath));
      ModelArchive = new FakeArchive(ModelCommonPath);
      ModelArchive.FilesList.Add(new OvlFile(
        "AdultElephant",
        FileType.Model,
        ModelCommonPath));
      ModelArchive.FilesList.Add(new OvlFile(
        "BabyElephant",
        FileType.Model,
        ModelCommonPath));
      ModelArchive.Models.Add(
        "AdultElephant",
        new ModelDefinition(
          "AdultElephant", ModelCommonPath, 350, 190, 35, 0, 0, 0));
      ModelArchive.Models.Add(
        "BabyElephant",
        new ModelDefinition(
          "BabyElephant", ModelCommonPath, 430, 210, 30, 0, 0, 0));
      Source = new FakeSource();
      Source.ExistingPaths.UnionWith([
        SpeciesCommonPath,
        SpeciesUniquePath,
        ModelCommonPath,
        ModelUniquePath,
      ]);
      Source.Archives.Add(SpeciesCommonPath, SpeciesArchive);
      Source.Archives.Add(ModelCommonPath, ModelArchive);
    }

    public string InstallRoot { get; }
    public string SpeciesCommonPath { get; }
    public string SpeciesUniquePath { get; }
    public string ModelCommonPath { get; }
    public string ModelUniquePath { get; }
    public FakeSource Source { get; }
    public FakeArchive SpeciesArchive { get; }
    public FakeArchive ModelArchive { get; }

    public WildAnimalSpeciesModelResourceBridgeResult Resolve() =>
      WildAnimalSpeciesModelResourceBridge.ResolveInstalled(
        InstallRoot,
        "Elephant:was",
        Source);

    public void MakeMalformed(MalformedBridge malformed) {
      switch (malformed) {
        case MalformedBridge.MissingSpeciesCommon:
          Source.ExistingPaths.Remove(SpeciesCommonPath);
          break;
        case MalformedBridge.MissingSpeciesUnique:
          Source.ExistingPaths.Remove(SpeciesUniquePath);
          break;
        case MalformedBridge.WrongLoadedSpeciesPair:
          SpeciesArchive.CommonPathValue = Path.Combine(InstallRoot, "other.common.ovl");
          break;
        case MalformedBridge.MissingSpeciesSymbol:
          SpeciesArchive.FilesList.Clear();
          break;
        case MalformedBridge.AmbiguousSpeciesSymbol:
          SpeciesArchive.FilesList.Add(new OvlFile(
            "elephant",
            FileType.WildAnimalSpecies,
            SpeciesUniquePath));
          break;
        case MalformedBridge.CrossPackageSpeciesSymbol:
          SpeciesArchive.FilesList[0] = SpeciesArchive.FilesList[0] with {
            Path = Path.Combine(InstallRoot, "outside.unique.ovl"),
          };
          break;
        case MalformedBridge.SpeciesNameMismatch:
          SpeciesArchive.Species = SpeciesArchive.Species! with { Name = "Giraffe" };
          break;
        case MalformedBridge.NullVariantList:
          SpeciesArchive.Species = SpeciesArchive.Species! with { Variants = null! };
          break;
        case MalformedBridge.WrongVariantCount:
          SpeciesArchive.Species = SpeciesArchive.Species! with {
            Variants = SpeciesArchive.Species.Variants.Take(3).ToArray(),
          };
          break;
        case MalformedBridge.NullVariant:
          SpeciesArchive.Species = SpeciesArchive.Species! with {
            Variants = new WildAnimalSpeciesVariant[] {
              SpeciesArchive.Species.Variants[0],
              null!,
              SpeciesArchive.Species.Variants[2],
              SpeciesArchive.Species.Variants[3],
            },
          };
          break;
        case MalformedBridge.InvalidModelReference:
          ReplaceVariant(0, SpeciesArchive.Species!.Variants[0] with {
            ModelReference = "AdultElephant:bsh",
          });
          break;
        case MalformedBridge.InvalidAnimationReference:
          ReplaceVariant(0, SpeciesArchive.Species!.Variants[0] with {
            AnimationDataReference = "Elephant:ban",
          });
          break;
        case MalformedBridge.PackagePathEscape:
          SpeciesArchive.Species = SpeciesArchive.Species! with {
            PackagePath = @"..\outside\Elephant_data",
          };
          break;
        case MalformedBridge.RootedPackagePath:
          SpeciesArchive.Species = SpeciesArchive.Species! with {
            PackagePath = Path.Combine(
              Path.GetPathRoot(InstallRoot)!,
              "outside",
              "Elephant_data"),
          };
          break;
        case MalformedBridge.PackagePathWithOvlSuffix:
          SpeciesArchive.Species = SpeciesArchive.Species! with {
            PackagePath = @"WildAnimals\elephant\Elephant_data.common.ovl",
          };
          break;
        case MalformedBridge.MissingModelCommon:
          Source.ExistingPaths.Remove(ModelCommonPath);
          break;
        case MalformedBridge.MissingModelUnique:
          Source.ExistingPaths.Remove(ModelUniquePath);
          break;
        case MalformedBridge.WrongLoadedModelPair:
          ModelArchive.CommonPathValue = Path.Combine(InstallRoot, "other.common.ovl");
          break;
        case MalformedBridge.MissingModelSymbol:
          ModelArchive.FilesList.RemoveAt(0);
          break;
        case MalformedBridge.AmbiguousModelSymbol:
          ModelArchive.FilesList.Add(new OvlFile(
            "adultElephant",
            FileType.Model,
            ModelCommonPath));
          break;
        case MalformedBridge.CrossPackageModelSymbol:
          ModelArchive.FilesList[0] = ModelArchive.FilesList[0] with {
            Path = Path.Combine(InstallRoot, "outside.common.ovl"),
          };
          break;
        case MalformedBridge.ModelNameMismatch:
          ModelArchive.Models["AdultElephant"] =
            ModelArchive.Models["AdultElephant"] with { Name = "OtherElephant" };
          break;
        case MalformedBridge.ModelSourceMismatch:
          ModelArchive.Models["AdultElephant"] =
            ModelArchive.Models["AdultElephant"] with {
              SourcePath = Path.Combine(InstallRoot, "outside.common.ovl"),
            };
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed));
      }
    }

    private void ReplaceVariant(int index, WildAnimalSpeciesVariant replacement) {
      var variants = SpeciesArchive.Species!.Variants.ToArray();
      variants[index] = replacement;
      SpeciesArchive.Species = SpeciesArchive.Species with { Variants = variants };
    }

    private static WildAnimalSpeciesDefinition SpeciesDefinition() => new(
      "Elephant",
      @"WildAnimals\elephant\Elephant_data",
      new[] {
        new WildAnimalSpeciesVariant("AdultElephant:mdl", "Elephant:wad"),
        new WildAnimalSpeciesVariant("AdultElephant:mdl", "Elephant:wad"),
        new WildAnimalSpeciesVariant("BabyElephant:mdl", "babyElephant:wad"),
        new WildAnimalSpeciesVariant("BabyElephant:mdl", "babyElephant:wad"),
      });

    private static string ToUniquePath(string commonPath) =>
      commonPath[..^".common.ovl".Length] + ".unique.ovl";
  }

  internal sealed class FakeSource : IWildAnimalSpeciesModelBridgeSource {
    public HashSet<string> ExistingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, FakeArchive> Archives { get; } =
      new(StringComparer.OrdinalIgnoreCase);
    public List<string> LoadRequests { get; } = [];

    public bool FileExists(string path) => ExistingPaths.Contains(path);

    public IWildAnimalSpeciesModelArchive LoadPair(string commonPath) {
      LoadRequests.Add(commonPath);
      return Archives[commonPath];
    }
  }

  internal sealed class FakeArchive(string commonPath) : IWildAnimalSpeciesModelArchive {
    public string CommonPathValue { get; set; } = commonPath;
    public string CommonPath => CommonPathValue;
    public List<OvlFile> FilesList { get; } = [];
    public IReadOnlyList<OvlFile> Files => FilesList;
    public WildAnimalSpeciesDefinition? Species { get; set; }
    public Dictionary<string, ModelDefinition> Models { get; } =
      new(StringComparer.OrdinalIgnoreCase);
    public List<string> ModelDecodeRequests { get; } = [];
    public bool Disposed { get; private set; }

    public WildAnimalSpeciesDefinition DecodeSpecies(string reference) => Species!;

    public ModelDefinition DecodeModel(string reference) {
      ModelDecodeRequests.Add(reference);
      var separator = reference.LastIndexOf(':');
      return Models[reference[..separator]];
    }

    public void Dispose() => Disposed = true;
  }
}

public enum MalformedBridge {
  MissingSpeciesCommon,
  MissingSpeciesUnique,
  WrongLoadedSpeciesPair,
  MissingSpeciesSymbol,
  AmbiguousSpeciesSymbol,
  CrossPackageSpeciesSymbol,
  SpeciesNameMismatch,
  NullVariantList,
  WrongVariantCount,
  NullVariant,
  InvalidModelReference,
  InvalidAnimationReference,
  PackagePathEscape,
  RootedPackagePath,
  PackagePathWithOvlSuffix,
  MissingModelCommon,
  MissingModelUnique,
  WrongLoadedModelPair,
  MissingModelSymbol,
  AmbiguousModelSymbol,
  CrossPackageModelSymbol,
  ModelNameMismatch,
  ModelSourceMismatch,
}
