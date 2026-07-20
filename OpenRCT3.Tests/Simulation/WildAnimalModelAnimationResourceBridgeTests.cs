using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalModelAnimationResourceBridgeTests {
  [Test]
  public void ResolveInstalled_PreservesAllSlotsPlaceholdersAndDuplicateTargets() {
    var fixture = new BridgeFixture();

    var result = fixture.Resolve();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.AnimationDataReference, Is.EqualTo("Elephant:wad"));
      Assert.That(result.ModelPackageCommonPath, Is.EqualTo(fixture.ModelCommonPath));
      Assert.That(result.AnimationPackageCommonPath, Is.EqualTo(fixture.AnimationCommonPath));
      Assert.That(result.AnimationDataFile.Path, Is.EqualTo(fixture.AnimationUniquePath));
      Assert.That(result.AnimationData, Is.SameAs(fixture.AnimationArchive.AnimationData));
      Assert.That(result.Slots, Has.Count.EqualTo(31));
      Assert.That(result.Slots.Select(slot => slot.SerializedIndex),
        Is.EqualTo(Enumerable.Range(0, 31)));
      Assert.That(result.Slots.Select(slot => slot.Reference),
        Is.EqualTo(fixture.References));
      Assert.That(result.ResolvedSlotCount, Is.EqualTo(27));
      Assert.That(result.PlaceholderSlotCount, Is.EqualTo(4));
      Assert.That(result.Slots.Where(slot => slot.IsPlaceholder)
        .Select(slot => slot.SerializedIndex),
        Is.EqualTo(new[] { 3, 22, 23, 24 }));
      Assert.That(result.Slots.Where(slot => slot.IsPlaceholder)
        .Select(slot => slot.Source),
        Is.All.Null);
      Assert.That(result.Slots.Where(slot => slot.IsResolved)
        .Select(slot => slot.Source),
        Is.All.Not.Null);
      Assert.That(fixture.Source.LoadRequests,
        Is.EqualTo(new[] { fixture.ModelCommonPath, fixture.AnimationCommonPath }));
      Assert.That(fixture.AnimationArchive.AnimationDataDecodeRequests,
        Is.EqualTo(new[] { "Elephant:wad" }));
      Assert.That(fixture.AnimationArchive.ModelAnimationDecodeCount, Is.EqualTo(1));
      Assert.That(fixture.ModelArchive.Disposed, Is.True);
      Assert.That(fixture.AnimationArchive.Disposed, Is.True);
    }
    Assert.That(result.Slots[0].Source, Is.SameAs(result.Slots[2].Source));
    Assert.That(result.Slots[1].Source, Is.SameAs(result.Slots[5].Source));
  }

  [TestCase(MalformedModelAnimationBridge.RequestedReferenceNotSerialized)]
  [TestCase(MalformedModelAnimationBridge.InvalidRequestedReference)]
  [TestCase(MalformedModelAnimationBridge.NullVariant)]
  [TestCase(MalformedModelAnimationBridge.MissingModelCommon)]
  [TestCase(MalformedModelAnimationBridge.MissingModelUnique)]
  [TestCase(MalformedModelAnimationBridge.WrongLoadedModelPair)]
  [TestCase(MalformedModelAnimationBridge.NullExternalReferences)]
  [TestCase(MalformedModelAnimationBridge.NoExternalReferences)]
  [TestCase(MalformedModelAnimationBridge.TooManyExternalReferences)]
  [TestCase(MalformedModelAnimationBridge.MalformedExternalReference)]
  [TestCase(MalformedModelAnimationBridge.DuplicateExternalReference)]
  [TestCase(MalformedModelAnimationBridge.MissingAnimationCommon)]
  [TestCase(MalformedModelAnimationBridge.MissingAnimationUnique)]
  [TestCase(MalformedModelAnimationBridge.WrongLoadedAnimationPair)]
  [TestCase(MalformedModelAnimationBridge.NullAnimationFiles)]
  [TestCase(MalformedModelAnimationBridge.MissingWadSymbol)]
  [TestCase(MalformedModelAnimationBridge.AmbiguousWadSymbol)]
  [TestCase(MalformedModelAnimationBridge.CrossPackageWadSymbol)]
  [TestCase(MalformedModelAnimationBridge.NullDecodedWad)]
  [TestCase(MalformedModelAnimationBridge.WadNameMismatch)]
  [TestCase(MalformedModelAnimationBridge.WadSourceMismatch)]
  [TestCase(MalformedModelAnimationBridge.WrongSerializedCount)]
  [TestCase(MalformedModelAnimationBridge.WrongSlotCount)]
  [TestCase(MalformedModelAnimationBridge.MalformedNamedSlot)]
  [TestCase(MalformedModelAnimationBridge.PaddedSlot)]
  [TestCase(MalformedModelAnimationBridge.MissingModelAnimSymbol)]
  [TestCase(MalformedModelAnimationBridge.AmbiguousModelAnimSymbol)]
  [TestCase(MalformedModelAnimationBridge.CrossPackageModelAnimSymbol)]
  [TestCase(MalformedModelAnimationBridge.NullModelAnimationDefinitions)]
  [TestCase(MalformedModelAnimationBridge.NullModelAnimationDefinition)]
  [TestCase(MalformedModelAnimationBridge.MissingDecodedModelAnimation)]
  [TestCase(MalformedModelAnimationBridge.ExtraDecodedModelAnimation)]
  [TestCase(MalformedModelAnimationBridge.DecodedModelAnimationNameMismatch)]
  [TestCase(MalformedModelAnimationBridge.DecodedModelAnimationSourceMismatch)]
  public void ResolveInstalled_RejectsMissingAmbiguousOrWrongOwnerEvidence(
    MalformedModelAnimationBridge malformed
  ) {
    var fixture = new BridgeFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Resolve()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void ResolveInstalled_FromOstrichPreservesAllThirtyOneWadSlots() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var speciesResources = WildAnimalSpeciesModelResourceBridge.ResolveInstalled(
      root!,
      "Ostrich:was");

    var result = WildAnimalModelAnimationResourceBridge.ResolveInstalled(
      speciesResources,
      "MaleOstrich:wad");

    var animationCommonPath = Path.Combine(
      Path.GetFullPath(root!),
      "WildAnimals",
      "Ostrich",
      "Ostrich_anims.common.ovl");
    var animationUniquePath =
      animationCommonPath[..^".common.ovl".Length] + ".unique.ovl";
    using (Assert.EnterMultipleScope()) {
      Assert.That(result.AnimationPackageCommonPath,
        Is.EqualTo(animationCommonPath).IgnoreCase);
      Assert.That(result.AnimationDataFile.Path,
        Is.EqualTo(animationUniquePath).IgnoreCase);
      Assert.That(result.AnimationData.SourcePath,
        Is.EqualTo(animationUniquePath).IgnoreCase);
      Assert.That(result.Slots, Has.Count.EqualTo(31));
      Assert.That(result.Slots.Select(slot => slot.SerializedIndex),
        Is.EqualTo(Enumerable.Range(0, 31)));
      Assert.That(result.Slots.Select(slot => slot.Reference),
        Is.EqualTo(result.AnimationData.ModelAnimationReferencesAt38));
      Assert.That(result.ResolvedSlotCount, Is.EqualTo(27));
      Assert.That(result.PlaceholderSlotCount, Is.EqualTo(4));
      Assert.That(result.Slots.Where(slot => slot.IsPlaceholder)
        .Select(slot => slot.SerializedIndex),
        Is.EqualTo(new[] { 3, 22, 23, 24 }));
      Assert.That(result.Slots.Where(slot => slot.IsPlaceholder)
        .Select(slot => slot.Reference),
        Is.All.EqualTo(":modelanim").IgnoreCase);
      Assert.That(result.Slots.Where(slot => slot.IsPlaceholder)
        .Select(slot => slot.Source),
        Is.All.Null);
      Assert.That(result.Slots.Where(slot => slot.IsResolved)
        .All(slot => slot.Source != null &&
          slot.Reference.Equals(slot.Source.Identity, StringComparison.OrdinalIgnoreCase)),
        Is.True);
      Assert.That(result.Slots.Where(slot => slot.IsResolved)
        .Select(slot => slot.Source!.File.Path),
        Is.All.EqualTo(animationCommonPath).IgnoreCase);
      Assert.That(result.Slots.Where(slot => slot.IsResolved)
        .Select(slot => slot.Source!.Resource.SourcePath),
        Is.All.EqualTo(animationCommonPath).IgnoreCase);
    }
  }

  private sealed class BridgeFixture {
    public BridgeFixture() {
      InstallRoot = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        "OpenRCT3-WildAnimalModelAnimationBridge-Fixture"));
      ModelCommonPath = Path.Combine(
        InstallRoot,
        "WildAnimals",
        "elephant",
        "Elephant_data.common.ovl");
      ModelUniquePath = ToUniquePath(ModelCommonPath);
      AnimationCommonPath = Path.Combine(
        InstallRoot,
        "WildAnimals",
        "elephant",
        "elephant_anims.common.ovl");
      AnimationUniquePath = ToUniquePath(AnimationCommonPath);
      References = Enumerable.Range(0, 31).Select(index => index switch {
        3 => ":modelanim",
        22 => ":MODELANIM",
        23 or 24 => ":modelanim",
        _ when index % 2 == 0 => "ClipB:modelanim",
        _ => "clipa:MODELANIM",
      }).ToArray();

      ModelArchive = new FakeArchive(ModelCommonPath);
      ModelArchive.ExternalReferencesList!.Add("elephant_anims");
      AnimationArchive = new FakeArchive(AnimationCommonPath);
      AnimationArchive.FilesList!.Add(new OvlFile(
        "Elephant",
        FileType.WildAnimalAnimData,
        AnimationUniquePath));
      AnimationArchive.FilesList.Add(new OvlFile(
        "ClipA",
        FileType.ModelAnim,
        AnimationCommonPath));
      AnimationArchive.FilesList.Add(new OvlFile(
        "ClipB",
        FileType.ModelAnim,
        AnimationCommonPath));
      AnimationArchive.AnimationData = CreateAnimationData(
        "Elephant",
        AnimationUniquePath,
        References);
      AnimationArchive.ModelAnimationsList!.Add(CreateModelAnimation(
        "ClipA",
        AnimationCommonPath,
        1_000));
      AnimationArchive.ModelAnimationsList.Add(CreateModelAnimation(
        "ClipB",
        AnimationCommonPath,
        2_000));

      Source = new FakeSource();
      Source.ExistingPaths.UnionWith([
        ModelCommonPath,
        ModelUniquePath,
        AnimationCommonPath,
        AnimationUniquePath,
      ]);
      Source.Archives.Add(ModelCommonPath, ModelArchive);
      Source.Archives.Add(AnimationCommonPath, AnimationArchive);
      Resources = CreateSpeciesResources();
    }

    public string InstallRoot { get; }
    public string ModelCommonPath { get; }
    public string ModelUniquePath { get; }
    public string AnimationCommonPath { get; }
    public string AnimationUniquePath { get; }
    public string[] References { get; }
    public FakeSource Source { get; }
    public FakeArchive ModelArchive { get; }
    public FakeArchive AnimationArchive { get; }
    public WildAnimalSpeciesModelResourceBridgeResult Resources { get; private set; }
    public string AnimationDataReference { get; private set; } = "Elephant:wad";

    public WildAnimalModelAnimationResourceBridgeResult Resolve() =>
      WildAnimalModelAnimationResourceBridge.ResolveInstalled(
        Resources,
        AnimationDataReference,
        Source);

    public void MakeMalformed(MalformedModelAnimationBridge malformed) {
      switch (malformed) {
        case MalformedModelAnimationBridge.RequestedReferenceNotSerialized:
          AnimationDataReference = "OtherElephant:wad";
          break;
        case MalformedModelAnimationBridge.InvalidRequestedReference:
          AnimationDataReference = "Elephant:modelanim";
          break;
        case MalformedModelAnimationBridge.NullVariant:
          Resources = Resources with {
            Variants = new WildAnimalSpeciesModelVariantLink[] {
              Resources.Variants[0],
              null!,
              Resources.Variants[2],
              Resources.Variants[3],
            },
          };
          break;
        case MalformedModelAnimationBridge.MissingModelCommon:
          Source.ExistingPaths.Remove(ModelCommonPath);
          break;
        case MalformedModelAnimationBridge.MissingModelUnique:
          Source.ExistingPaths.Remove(ModelUniquePath);
          break;
        case MalformedModelAnimationBridge.WrongLoadedModelPair:
          ModelArchive.CommonPathValue = Path.Combine(InstallRoot, "wrong.common.ovl");
          break;
        case MalformedModelAnimationBridge.NullExternalReferences:
          ModelArchive.ExternalReferencesList = null;
          break;
        case MalformedModelAnimationBridge.NoExternalReferences:
          ModelArchive.ExternalReferencesList!.Clear();
          break;
        case MalformedModelAnimationBridge.TooManyExternalReferences:
          ModelArchive.ExternalReferencesList = Enumerable.Range(0, 1_025)
            .Select(index => $"dependency{index}").ToList();
          break;
        case MalformedModelAnimationBridge.MalformedExternalReference:
          ModelArchive.ExternalReferencesList![0] = @"..\elephant_anims";
          break;
        case MalformedModelAnimationBridge.DuplicateExternalReference:
          ModelArchive.ExternalReferencesList!.Add("ELEPHANT_ANIMS");
          break;
        case MalformedModelAnimationBridge.MissingAnimationCommon:
          Source.ExistingPaths.Remove(AnimationCommonPath);
          break;
        case MalformedModelAnimationBridge.MissingAnimationUnique:
          Source.ExistingPaths.Remove(AnimationUniquePath);
          break;
        case MalformedModelAnimationBridge.WrongLoadedAnimationPair:
          AnimationArchive.CommonPathValue = Path.Combine(InstallRoot, "wrong.common.ovl");
          break;
        case MalformedModelAnimationBridge.NullAnimationFiles:
          AnimationArchive.FilesList = null;
          break;
        case MalformedModelAnimationBridge.MissingWadSymbol:
          AnimationArchive.FilesList!.RemoveAt(0);
          break;
        case MalformedModelAnimationBridge.AmbiguousWadSymbol:
          AnimationArchive.FilesList!.Add(new OvlFile(
            "elephant",
            FileType.WildAnimalAnimData,
            AnimationUniquePath));
          break;
        case MalformedModelAnimationBridge.CrossPackageWadSymbol:
          AnimationArchive.FilesList![0] = AnimationArchive.FilesList[0] with {
            Path = Path.Combine(InstallRoot, "outside.unique.ovl"),
          };
          break;
        case MalformedModelAnimationBridge.NullDecodedWad:
          AnimationArchive.AnimationData = null;
          break;
        case MalformedModelAnimationBridge.WadNameMismatch:
          AnimationArchive.AnimationData =
            AnimationArchive.AnimationData! with { Name = "OtherElephant" };
          break;
        case MalformedModelAnimationBridge.WadSourceMismatch:
          AnimationArchive.AnimationData = AnimationArchive.AnimationData! with {
            SourcePath = Path.Combine(InstallRoot, "outside.unique.ovl"),
          };
          break;
        case MalformedModelAnimationBridge.WrongSerializedCount:
          AnimationArchive.AnimationData =
            AnimationArchive.AnimationData! with { SerializedCountAt08 = 30 };
          break;
        case MalformedModelAnimationBridge.WrongSlotCount:
          AnimationArchive.AnimationData = AnimationArchive.AnimationData! with {
            ModelAnimationReferencesAt38 = References.Take(30).ToArray(),
          };
          break;
        case MalformedModelAnimationBridge.MalformedNamedSlot:
          ReplaceReference(0, "ClipA:mdl");
          break;
        case MalformedModelAnimationBridge.PaddedSlot:
          ReplaceReference(0, " ClipA:modelanim");
          break;
        case MalformedModelAnimationBridge.MissingModelAnimSymbol:
          AnimationArchive.FilesList!.RemoveAt(1);
          break;
        case MalformedModelAnimationBridge.AmbiguousModelAnimSymbol:
          AnimationArchive.FilesList!.Add(new OvlFile(
            "clipa",
            FileType.ModelAnim,
            AnimationCommonPath));
          break;
        case MalformedModelAnimationBridge.CrossPackageModelAnimSymbol:
          AnimationArchive.FilesList![1] = AnimationArchive.FilesList[1] with {
            Path = Path.Combine(InstallRoot, "outside.common.ovl"),
          };
          break;
        case MalformedModelAnimationBridge.NullModelAnimationDefinitions:
          AnimationArchive.ModelAnimationsList = null;
          break;
        case MalformedModelAnimationBridge.NullModelAnimationDefinition:
          AnimationArchive.ModelAnimationsList![0] = null!;
          break;
        case MalformedModelAnimationBridge.MissingDecodedModelAnimation:
          AnimationArchive.ModelAnimationsList!.RemoveAt(0);
          break;
        case MalformedModelAnimationBridge.ExtraDecodedModelAnimation:
          AnimationArchive.ModelAnimationsList!.Add(CreateModelAnimation(
            "ClipC",
            AnimationCommonPath,
            3_000));
          break;
        case MalformedModelAnimationBridge.DecodedModelAnimationNameMismatch:
          AnimationArchive.ModelAnimationsList![0] =
            AnimationArchive.ModelAnimationsList[0] with { Name = "OtherClip" };
          break;
        case MalformedModelAnimationBridge.DecodedModelAnimationSourceMismatch:
          AnimationArchive.ModelAnimationsList![0] =
            AnimationArchive.ModelAnimationsList[0] with {
              SourcePath = Path.Combine(InstallRoot, "outside.common.ovl"),
            };
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed), malformed, null);
      }
    }

    private void ReplaceReference(int index, string reference) {
      var references = AnimationArchive.AnimationData!.ModelAnimationReferencesAt38.ToArray();
      references[index] = reference;
      AnimationArchive.AnimationData = AnimationArchive.AnimationData with {
        ModelAnimationReferencesAt38 = references,
      };
    }

    private WildAnimalSpeciesModelResourceBridgeResult CreateSpeciesResources() {
      var speciesCommonPath = Path.Combine(
        InstallRoot,
        "WildAnimals",
        "WildAnimals.common.ovl");
      var speciesFile = new OvlFile(
        "Elephant",
        FileType.WildAnimalSpecies,
        ToUniquePath(speciesCommonPath));
      var variants = Enumerable.Range(0, 4).Select(_ =>
        new WildAnimalSpeciesVariant("AdultElephant:mdl", "Elephant:wad")).ToArray();
      var species = new WildAnimalSpeciesDefinition(
        "Elephant",
        @"WildAnimals\elephant\Elephant_data",
        variants);
      var modelFile = new OvlFile("AdultElephant", FileType.Model, ModelCommonPath);
      var model = new ModelDefinition(
        "AdultElephant", ModelCommonPath, 350, 190, 35, 0, 0, 0);
      var modelSource = new WildAnimalSpeciesModelResourceSource(modelFile, model);
      var links = variants.Select((variant, index) =>
        new WildAnimalSpeciesModelVariantLink(index, variant, modelSource)).ToArray();
      return new WildAnimalSpeciesModelResourceBridgeResult(
        "Elephant:was",
        speciesCommonPath,
        speciesFile,
        species,
        ModelCommonPath,
        links);
    }

    private static WildAnimalAnimationDataDefinition CreateAnimationData(
      string name,
      string sourcePath,
      IReadOnlyList<string> references
    ) => new(
      name,
      sourcePath,
      10_000,
      0,
      0,
      31,
      10_056,
      20_000,
      20_124,
      0,
      0,
      0,
      0,
      0,
      0,
      0,
      0,
      references,
      Enumerable.Repeat(0f, 31).ToArray(),
      Enumerable.Repeat(0f, 31).ToArray());

    private static ModelAnimationDefinition CreateModelAnimation(
      string name,
      string sourcePath,
      uint dataAddress
    ) => new(
      name,
      sourcePath,
      dataAddress,
      0,
      1,
      dataAddress + 88,
      dataAddress + 92,
      1,
      1,
      dataAddress + 96,
      dataAddress + 108,
      new uint[] { 0 },
      new uint[] { 0 },
      new[] { new ModelAnimationTriple(0, 0, 0) },
      new[] { new ModelAnimationFourTuple(0, 0, 0, 1) },
      new[] { "Bone" },
      new[] { "Bone" });

    private static string ToUniquePath(string commonPath) =>
      commonPath[..^".common.ovl".Length] + ".unique.ovl";
  }

  private sealed class FakeSource : IWildAnimalModelAnimationBridgeSource {
    public HashSet<string> ExistingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, FakeArchive> Archives { get; } =
      new(StringComparer.OrdinalIgnoreCase);
    public List<string> LoadRequests { get; } = [];

    public bool FileExists(string path) => ExistingPaths.Contains(path);

    public IWildAnimalModelAnimationArchive LoadPair(string commonPath) {
      LoadRequests.Add(commonPath);
      return Archives[commonPath];
    }
  }

  private sealed class FakeArchive(string commonPath) : IWildAnimalModelAnimationArchive {
    public string CommonPathValue { get; set; } = commonPath;
    public string CommonPath => CommonPathValue;
    public List<string>? ExternalReferencesList { get; set; } = [];
    public IReadOnlyList<string> ExternalReferences => ExternalReferencesList!;
    public List<OvlFile>? FilesList { get; set; } = [];
    public IReadOnlyList<OvlFile> Files => FilesList!;
    public WildAnimalAnimationDataDefinition? AnimationData { get; set; }
    public List<ModelAnimationDefinition>? ModelAnimationsList { get; set; } = [];
    public List<string> AnimationDataDecodeRequests { get; } = [];
    public int ModelAnimationDecodeCount { get; private set; }
    public bool Disposed { get; private set; }

    public WildAnimalAnimationDataDefinition DecodeAnimationData(string reference) {
      AnimationDataDecodeRequests.Add(reference);
      return AnimationData!;
    }

    public IReadOnlyList<ModelAnimationDefinition> DecodeModelAnimations() {
      ModelAnimationDecodeCount++;
      return ModelAnimationsList!;
    }

    public void Dispose() => Disposed = true;
  }
}

public enum MalformedModelAnimationBridge {
  RequestedReferenceNotSerialized,
  InvalidRequestedReference,
  NullVariant,
  MissingModelCommon,
  MissingModelUnique,
  WrongLoadedModelPair,
  NullExternalReferences,
  NoExternalReferences,
  TooManyExternalReferences,
  MalformedExternalReference,
  DuplicateExternalReference,
  MissingAnimationCommon,
  MissingAnimationUnique,
  WrongLoadedAnimationPair,
  NullAnimationFiles,
  MissingWadSymbol,
  AmbiguousWadSymbol,
  CrossPackageWadSymbol,
  NullDecodedWad,
  WadNameMismatch,
  WadSourceMismatch,
  WrongSerializedCount,
  WrongSlotCount,
  MalformedNamedSlot,
  PaddedSlot,
  MissingModelAnimSymbol,
  AmbiguousModelAnimSymbol,
  CrossPackageModelAnimSymbol,
  NullModelAnimationDefinitions,
  NullModelAnimationDefinition,
  MissingDecodedModelAnimation,
  ExtraDecodedModelAnimation,
  DecodedModelAnimationNameMismatch,
  DecodedModelAnimationSourceMismatch,
}
