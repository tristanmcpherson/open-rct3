// Wild Animal Frame-Zero Pose Registry Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalFrameZeroPoseRegistryTests {
  [Test]
  public void Build_RetainsEverySlotAndCachesOnlyExactModelAndAnimationSourcePairs() {
    var fixture = new RegistryFixture();
    var evaluations = new List<(ModelDefinition Model, ModelAnimationDefinition Animation)>();

    var registry = WildAnimalFrameZeroPoseRegistry.Build(
      fixture.Resources,
      fixture.AnimationResources,
      (model, animation) => {
        evaluations.Add((model, animation));
        return ModelAnimationFrameZeroPoseEvaluator.Evaluate(model, animation);
      });

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.VariantCount, Is.EqualTo(4));
      Assert.That(registry.PoseCount, Is.EqualTo(3));
      Assert.That(registry.ResolvedSlotCount, Is.EqualTo(12));
      Assert.That(registry.PlaceholderSlotCount, Is.EqualTo(112));
      Assert.That(registry.Variants.Select(item => item.VariantLink),
        Is.EqualTo(fixture.Resources.Variants));
      Assert.That(registry.Variants.Select(item => item.Slots.Count),
        Is.All.EqualTo(31));
      Assert.That(registry.Variants.SelectMany(item => item.Slots)
        .Select(item => item.SerializedIndex),
        Is.EqualTo(Enumerable.Range(0, 4).SelectMany(_ =>
          Enumerable.Range(0, 31))));
      Assert.That(evaluations, Has.Count.EqualTo(3));
      Assert.That(evaluations.Select(item => item.Model), Is.EqualTo(new[] {
        fixture.AdultModel,
        fixture.AdultModel,
        fixture.BabyModel,
      }));
    }

    foreach (var variant in registry.Variants) {
      var expectedBridge = fixture.AnimationResources[
        variant.VariantLink.Variant.AnimationDataReference];
      Assert.That(variant.AnimationResources, Is.SameAs(expectedBridge));
      foreach (var index in Enumerable.Range(0, 31)) {
        var slot = variant.Slots[index];
        Assert.That(slot.AnimationSlotLink, Is.SameAs(expectedBridge.Slots[index]));
        Assert.That(slot.Pose == null, Is.EqualTo(slot.IsPlaceholder));
        if (slot.Pose == null) continue;
        using (Assert.EnterMultipleScope()) {
          Assert.That(slot.Pose.Model,
            Is.SameAs(variant.VariantLink.ModelSource.Resource));
          Assert.That(slot.Pose.Animation,
            Is.SameAs(slot.AnimationSlotLink.Source!.Resource));
        }
      }
    }

    var adultShared = registry.Variants[0].Slots[0].Pose;
    Assert.That(adultShared, Is.Not.Null);
    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.Variants[0].Slots[7].Pose, Is.SameAs(adultShared));
      Assert.That(registry.Variants[0].Slots[30].Pose, Is.SameAs(adultShared));
      Assert.That(registry.Variants[1].Slots[0].Pose, Is.SameAs(adultShared));
      Assert.That(registry.Variants[2].Slots[0].Pose, Is.Not.SameAs(adultShared));
      Assert.That(registry.Variants[3].Slots[0].Pose, Is.Not.SameAs(adultShared));
    }
  }

  [Test]
  public void Build_DefaultEvaluatorRetainsExactFrameZeroTransform() {
    var fixture = new RegistryFixture();

    var registry = WildAnimalFrameZeroPoseRegistry.Build(
      fixture.Resources,
      fixture.AnimationResources);
    var pose = registry.Variants[0].Slots[0].Pose!;

    using (Assert.EnterMultipleScope()) {
      Assert.That(pose.Model, Is.SameAs(fixture.AdultModel));
      Assert.That(pose.Animation, Is.SameAs(fixture.Animation));
      Assert.That(pose.Bones, Has.Count.EqualTo(1));
      Assert.That(pose.Bones[0].LocalTransform.Translation.X, Is.EqualTo(2f));
      Assert.That(pose.Bones[0].LocalTransform.Translation.Y, Is.EqualTo(3f));
      Assert.That(pose.Bones[0].LocalTransform.Translation.Z, Is.EqualTo(4f));
      Assert.That(pose.Bones[0].SkinTransform.Translation,
        Is.EqualTo(new Vector3(2f, 3f, 4f)));
    }
  }

  [TestCase(MalformedPoseRegistry.NullSpeciesFile)]
  [TestCase(MalformedPoseRegistry.WrongSpeciesOwner)]
  [TestCase(MalformedPoseRegistry.DuplicateVariantIndex)]
  [TestCase(MalformedPoseRegistry.ChangedVariantIdentity)]
  [TestCase(MalformedPoseRegistry.ChangedRepeatedModelSource)]
  [TestCase(MalformedPoseRegistry.NullModelSource)]
  [TestCase(MalformedPoseRegistry.MissingVariantWad)]
  [TestCase(MalformedPoseRegistry.ExtraVariantWad)]
  [TestCase(MalformedPoseRegistry.DuplicateWadKey)]
  [TestCase(MalformedPoseRegistry.NullAnimationBridge)]
  [TestCase(MalformedPoseRegistry.ChangedBridgeReference)]
  [TestCase(MalformedPoseRegistry.WrongModelPackageOwner)]
  [TestCase(MalformedPoseRegistry.WrongAnimationPackageOwner)]
  [TestCase(MalformedPoseRegistry.WrongWadOwner)]
  [TestCase(MalformedPoseRegistry.NullWadResource)]
  [TestCase(MalformedPoseRegistry.ChangedSlotReference)]
  [TestCase(MalformedPoseRegistry.DuplicateSlotIndex)]
  [TestCase(MalformedPoseRegistry.NullSlot)]
  [TestCase(MalformedPoseRegistry.ChangedRepeatedAnimationSource)]
  [TestCase(MalformedPoseRegistry.WrongAnimationSourceOwner)]
  [TestCase(MalformedPoseRegistry.SkeletonMismatch)]
  public void Build_RejectsMissingDuplicateDriftedOrSkeletonMismatchedEvidence(
    MalformedPoseRegistry malformed
  ) {
    var fixture = new RegistryFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalFrameZeroPoseRegistry.Build(
        fixture.Resources,
        fixture.AnimationResources)));
  }

  [Test]
  public void Build_RejectsNullOrSourceDriftedEvaluatorResults() {
    var fixture = new RegistryFixture();
    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalFrameZeroPoseRegistry.Build(
        fixture.Resources,
        fixture.AnimationResources,
        (_, _) => null!)));

    var wrong = ModelAnimationFrameZeroPoseEvaluator.Evaluate(
      fixture.BabyModel,
      fixture.Animation);
    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalFrameZeroPoseRegistry.Build(
        fixture.Resources,
        fixture.AnimationResources,
        (_, _) => wrong)));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Build_InstalledOstrichRetainsAndEvaluatesEveryVariantWadSlot() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var species = WildAnimalSpeciesModelResourceBridge.ResolveInstalled(
      root!,
      "Ostrich:was");
    var animations = species.Variants
      .Select(variant => variant.Variant.AnimationDataReference)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
        reference => reference,
        reference => WildAnimalModelAnimationResourceBridge.ResolveInstalled(
          species,
          reference),
        StringComparer.OrdinalIgnoreCase);

    var registry = WildAnimalFrameZeroPoseRegistry.Build(species, animations);

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.VariantCount, Is.EqualTo(4));
      Assert.That(registry.Variants.Select(item => item.Slots.Count),
        Is.All.EqualTo(31));
      Assert.That(registry.ResolvedSlotCount, Is.GreaterThan(0));
      Assert.That(registry.PlaceholderSlotCount, Is.GreaterThan(0));
      Assert.That(registry.PoseCount, Is.GreaterThan(0));
      Assert.That(registry.Variants.SelectMany(item => item.Slots)
        .Where(item => item.IsPlaceholder).Select(item => item.Pose),
        Is.All.Null);
      Assert.That(registry.Variants.SelectMany(item => item.Slots)
        .Where(item => item.IsResolved).Select(item => item.Pose),
        Is.All.Not.Null);
    }
    foreach (var variant in registry.Variants) {
      foreach (var slot in variant.Slots.Where(item => item.IsResolved)) {
        using (Assert.EnterMultipleScope()) {
          Assert.That(slot.Pose!.Model,
            Is.SameAs(variant.VariantLink.ModelSource.Resource));
          Assert.That(slot.Pose.Animation,
            Is.SameAs(slot.AnimationSlotLink.Source!.Resource));
          Assert.That(slot.Pose.Bones,
            Has.Count.EqualTo(variant.VariantLink.ModelSource.Resource.Bones.Count));
        }
      }
    }
  }

  private sealed class RegistryFixture {
    private const string AdultWadReference = "AdultData:wad";
    private const string OtherWadReference = "OtherData:wad";
    private readonly string root;
    private readonly string speciesCommonPath;
    private readonly string modelCommonPath;
    private readonly string animationCommonPath;
    private readonly WildAnimalSpeciesDefinition species;
    private readonly WildAnimalSpeciesModelResourceSource adultSource;
    private readonly WildAnimalSpeciesModelResourceSource babySource;
    private readonly WildAnimalModelAnimationResourceSource animationSource;
    private readonly WildAnimalModelAnimationResourceSource otherAnimationSource;

    public RegistryFixture() {
      root = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        "OpenRCT3-WildAnimalFrameZeroPoseRegistry-Fixture"));
      speciesCommonPath = Path.Combine(root, "WildAnimals", "WildAnimals.common.ovl");
      modelCommonPath = Path.Combine(
        root, "WildAnimals", "animal", "Animal_data.common.ovl");
      animationCommonPath = Path.Combine(
        root, "WildAnimals", "animal", "Animal_anims.common.ovl");
      var variants = new[] {
        new WildAnimalSpeciesVariant("AdultAnimal:mdl", AdultWadReference),
        new WildAnimalSpeciesVariant("AdultAnimal:mdl", AdultWadReference),
        new WildAnimalSpeciesVariant("AdultAnimal:mdl", OtherWadReference),
        new WildAnimalSpeciesVariant("BabyAnimal:mdl", AdultWadReference),
      };
      species = new(
        "Animal",
        @"WildAnimals\animal\Animal_data",
        variants);
      AdultModel = Model("AdultAnimal", modelCommonPath);
      BabyModel = Model("BabyAnimal", modelCommonPath);
      adultSource = new(
        new OvlFile("AdultAnimal", FileType.Model, modelCommonPath),
        AdultModel);
      babySource = new(
        new OvlFile("BabyAnimal", FileType.Model, modelCommonPath),
        BabyModel);
      Animation = AnimationDefinition("Idle", animationCommonPath);
      var animationFile = new OvlFile("Idle", FileType.ModelAnim, animationCommonPath);
      animationSource = new(animationFile, Animation);
      // Same decoded resource, different bridge wrapper: identity text is insufficient for reuse.
      otherAnimationSource = new(animationFile, Animation);
      Resources = CreateResources();
      AnimationResources = CreateAnimationResources();
    }

    public ModelDefinition AdultModel { get; }
    public ModelDefinition BabyModel { get; }
    public ModelAnimationDefinition Animation { get; }
    public WildAnimalSpeciesModelResourceBridgeResult Resources { get; private set; }
    public IReadOnlyDictionary<string, WildAnimalModelAnimationResourceBridgeResult>
      AnimationResources { get; private set; }

    public void MakeMalformed(MalformedPoseRegistry malformed) {
      switch (malformed) {
        case MalformedPoseRegistry.NullSpeciesFile:
          Resources = Resources with { SpeciesFile = null! };
          break;
        case MalformedPoseRegistry.WrongSpeciesOwner:
          Resources = Resources with {
            SpeciesFile = Resources.SpeciesFile with { Path = modelCommonPath },
          };
          break;
        case MalformedPoseRegistry.DuplicateVariantIndex:
          ReplaceVariant(1, Resources.Variants[1] with { SerializedIndex = 0 });
          break;
        case MalformedPoseRegistry.ChangedVariantIdentity:
          ReplaceVariant(0, Resources.Variants[0] with {
            Variant = Resources.Variants[0].Variant with { },
          });
          break;
        case MalformedPoseRegistry.ChangedRepeatedModelSource:
          ReplaceVariant(1, Resources.Variants[1] with {
            ModelSource = new(adultSource.File, adultSource.Resource),
          });
          break;
        case MalformedPoseRegistry.NullModelSource:
          ReplaceVariant(0, Resources.Variants[0] with { ModelSource = null! });
          break;
        case MalformedPoseRegistry.MissingVariantWad:
          AnimationResources = new Dictionary<
            string,
            WildAnimalModelAnimationResourceBridgeResult>(StringComparer.OrdinalIgnoreCase) {
            [AdultWadReference] = AnimationResources[AdultWadReference],
          };
          break;
        case MalformedPoseRegistry.ExtraVariantWad:
          SetAnimationResources(
            ("Extra:wad", AnimationResources[AdultWadReference]));
          break;
        case MalformedPoseRegistry.DuplicateWadKey:
          AnimationResources = new Dictionary<
            string,
            WildAnimalModelAnimationResourceBridgeResult>(StringComparer.Ordinal) {
            [AdultWadReference] = AnimationResources[AdultWadReference],
            ["adultdata:WAD"] = AnimationResources[AdultWadReference],
            [OtherWadReference] = AnimationResources[OtherWadReference],
          };
          break;
        case MalformedPoseRegistry.NullAnimationBridge:
          ReplaceAnimation(AdultWadReference, null!);
          break;
        case MalformedPoseRegistry.ChangedBridgeReference:
          ReplaceAnimation(
            AdultWadReference,
            AnimationResources[AdultWadReference] with {
              AnimationDataReference = "Wrong:wad",
            });
          break;
        case MalformedPoseRegistry.WrongModelPackageOwner:
          ReplaceAnimation(
            AdultWadReference,
            AnimationResources[AdultWadReference] with {
              ModelPackageCommonPath = Path.Combine(root, "wrong.common.ovl"),
            });
          break;
        case MalformedPoseRegistry.WrongAnimationPackageOwner:
          var wrongAnimationCommonPath = Path.Combine(
            root,
            "outside",
            "Animal_anims.common.ovl");
          var wrongAnimationSource = new WildAnimalModelAnimationResourceSource(
            animationSource.File with { Path = wrongAnimationCommonPath },
            animationSource.Resource with { SourcePath = wrongAnimationCommonPath });
          var wrongAnimation = AnimationResources[AdultWadReference];
          ReplaceResolvedSources(AdultWadReference, wrongAnimationSource);
          wrongAnimation = AnimationResources[AdultWadReference];
          ReplaceAnimation(
            AdultWadReference,
            wrongAnimation with {
              AnimationPackageCommonPath = wrongAnimationCommonPath,
              AnimationDataFile = wrongAnimation.AnimationDataFile with {
                Path = ToUniquePath(wrongAnimationCommonPath),
              },
              AnimationData = wrongAnimation.AnimationData with {
                SourcePath = ToUniquePath(wrongAnimationCommonPath),
              },
            });
          break;
        case MalformedPoseRegistry.WrongWadOwner:
          ReplaceAnimation(
            AdultWadReference,
            AnimationResources[AdultWadReference] with {
              AnimationDataFile = AnimationResources[AdultWadReference].AnimationDataFile with {
                Path = modelCommonPath,
              },
            });
          break;
        case MalformedPoseRegistry.NullWadResource:
          ReplaceAnimation(
            AdultWadReference,
            AnimationResources[AdultWadReference] with { AnimationData = null! });
          break;
        case MalformedPoseRegistry.ChangedSlotReference:
          ReplaceSlot(
            AdultWadReference,
            0,
            AnimationResources[AdultWadReference].Slots[0] with {
              Reference = "Other:modelanim",
            });
          break;
        case MalformedPoseRegistry.DuplicateSlotIndex:
          ReplaceSlot(
            AdultWadReference,
            1,
            AnimationResources[AdultWadReference].Slots[1] with { SerializedIndex = 0 });
          break;
        case MalformedPoseRegistry.NullSlot:
          ReplaceSlot(AdultWadReference, 1, null!);
          break;
        case MalformedPoseRegistry.ChangedRepeatedAnimationSource:
          ReplaceSlot(
            AdultWadReference,
            7,
            AnimationResources[AdultWadReference].Slots[7] with {
              Source = new(animationSource.File, animationSource.Resource),
            });
          break;
        case MalformedPoseRegistry.WrongAnimationSourceOwner:
          ReplaceResolvedSources(
            AdultWadReference,
            new(
              animationSource.File with { Path = ToUniquePath(animationCommonPath) },
              animationSource.Resource with { SourcePath = ToUniquePath(animationCommonPath) }));
          break;
        case MalformedPoseRegistry.SkeletonMismatch:
          ReplaceResolvedSources(
            OtherWadReference,
            new(
              otherAnimationSource.File,
              otherAnimationSource.Resource with { FullBoneNames = ["Missing"] }));
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed), malformed, null);
      }
    }

    private WildAnimalSpeciesModelResourceBridgeResult CreateResources() {
      var links = new[] {
        new WildAnimalSpeciesModelVariantLink(0, species.Variants[0], adultSource),
        new WildAnimalSpeciesModelVariantLink(1, species.Variants[1], adultSource),
        new WildAnimalSpeciesModelVariantLink(2, species.Variants[2], adultSource),
        new WildAnimalSpeciesModelVariantLink(3, species.Variants[3], babySource),
      };
      return new(
        "Animal:was",
        speciesCommonPath,
        new OvlFile("Animal", FileType.WildAnimalSpecies, ToUniquePath(speciesCommonPath)),
        species,
        modelCommonPath,
        links);
    }

    private IReadOnlyDictionary<string, WildAnimalModelAnimationResourceBridgeResult>
      CreateAnimationResources() =>
      new Dictionary<string, WildAnimalModelAnimationResourceBridgeResult>(
        StringComparer.OrdinalIgnoreCase) {
        [AdultWadReference] = AnimationBridge("AdultData", animationSource),
        [OtherWadReference] = AnimationBridge("OtherData", otherAnimationSource),
      };

    private WildAnimalModelAnimationResourceBridgeResult AnimationBridge(
      string wadName,
      WildAnimalModelAnimationResourceSource source
    ) {
      var references = Enumerable.Range(0, 31).Select(index =>
        index is 0 or 7 or 30 ? "Idle:modelanim" : ":modelanim").ToArray();
      var wad = AnimationData(wadName, references);
      var slots = references.Select((reference, index) =>
        reference == ":modelanim"
          ? new WildAnimalModelAnimationSlotLink(
            index,
            reference,
            WildAnimalModelAnimationSlotStatus.Placeholder,
            null)
          : new WildAnimalModelAnimationSlotLink(
            index,
            reference,
            WildAnimalModelAnimationSlotStatus.Resolved,
            source)).ToArray();
      return new(
        $"{wadName}:wad",
        modelCommonPath,
        animationCommonPath,
        new OvlFile(wadName, FileType.WildAnimalAnimData, ToUniquePath(animationCommonPath)),
        wad,
        slots);
    }

    private WildAnimalAnimationDataDefinition AnimationData(
      string name,
      IReadOnlyList<string> references
    ) => new(
      name,
      ToUniquePath(animationCommonPath),
      1_000,
      0,
      0,
      31,
      1_056,
      2_000,
      2_124,
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

    private void ReplaceVariant(int index, WildAnimalSpeciesModelVariantLink replacement) {
      var variants = Resources.Variants.ToArray();
      variants[index] = replacement;
      Resources = Resources with { Variants = variants };
    }

    private void ReplaceAnimation(
      string reference,
      WildAnimalModelAnimationResourceBridgeResult replacement
    ) {
      var animations = AnimationResources.ToDictionary(
        pair => pair.Key,
        pair => pair.Value,
        StringComparer.OrdinalIgnoreCase);
      animations[reference] = replacement;
      AnimationResources = animations;
    }

    private void SetAnimationResources(
      params (string Reference, WildAnimalModelAnimationResourceBridgeResult Resource)[] additions
    ) {
      var animations = AnimationResources.ToDictionary(
        pair => pair.Key,
        pair => pair.Value,
        StringComparer.OrdinalIgnoreCase);
      foreach (var addition in additions)
        animations.Add(addition.Reference, addition.Resource);
      AnimationResources = animations;
    }

    private void ReplaceSlot(
      string wadReference,
      int index,
      WildAnimalModelAnimationSlotLink replacement
    ) {
      var animation = AnimationResources[wadReference];
      var slots = animation.Slots.ToArray();
      slots[index] = replacement;
      ReplaceAnimation(wadReference, animation with { Slots = slots });
    }

    private void ReplaceResolvedSources(
      string wadReference,
      WildAnimalModelAnimationResourceSource source
    ) {
      var animation = AnimationResources[wadReference];
      var slots = animation.Slots.Select(slot => slot.IsResolved
        ? slot with { Source = source }
        : slot).ToArray();
      ReplaceAnimation(wadReference, animation with { Slots = slots });
    }
  }

  private static ModelDefinition Model(string name, string path) => new(
    name,
    path,
    100,
    200,
    1,
    0,
    0,
    0) {
    Bones = [new ModelBone(
      "Root",
      new Vector4(0f, 0f, 0f, 1f),
      new Vector4(0f, 0f, 0f, 1f),
      Matrix4x4.Identity,
      ushort.MaxValue,
      1)],
  };

  private static ModelAnimationDefinition AnimationDefinition(string name, string path) => new(
    name,
    path,
    300,
    0,
    1,
    400,
    500,
    1,
    1,
    600,
    700,
    new uint[] { 0 },
    new uint[] { 0 },
    new[] { new ModelAnimationTriple(2f, 3f, 4f) },
    new[] { new ModelAnimationFourTuple(0f, 0f, 0f, 1f) },
    new[] { "Root" },
    new[] { "Root" });

  private static string ToUniquePath(string commonPath) =>
    commonPath[..^".common.ovl".Length] + ".unique.ovl";
}

public enum MalformedPoseRegistry {
  NullSpeciesFile,
  WrongSpeciesOwner,
  DuplicateVariantIndex,
  ChangedVariantIdentity,
  ChangedRepeatedModelSource,
  NullModelSource,
  MissingVariantWad,
  ExtraVariantWad,
  DuplicateWadKey,
  NullAnimationBridge,
  ChangedBridgeReference,
  WrongModelPackageOwner,
  WrongAnimationPackageOwner,
  WrongWadOwner,
  NullWadResource,
  ChangedSlotReference,
  DuplicateSlotIndex,
  NullSlot,
  ChangedRepeatedAnimationSource,
  WrongAnimationSourceOwner,
  SkeletonMismatch,
}
