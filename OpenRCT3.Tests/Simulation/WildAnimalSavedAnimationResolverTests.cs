// Wild Animal Saved Animation Resolver Tests
//
// Copyright (c) 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalSavedAnimationResolverTests {
  private static readonly int[] PlaceholderSlots = [3, 22, 23, 24];

  [Test]
  public void Resolve_PreservesEveryEntryAndMapsTypeDirectlyToWadSlot() {
    var resources = CreateResources();
    var visual = Visual([
      new DatWildAnimalAnimationData(1.25f, 4, 1f),
      new DatWildAnimalAnimationData(2.5f, 0, 0f),
      new DatWildAnimalAnimationData(3.75f, 3, 0f),
    ]);

    var result = WildAnimalSavedAnimationResolver.Resolve(visual, resources);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Visual, Is.SameAs(visual));
      Assert.That(result.Resources, Is.SameAs(resources));
      Assert.That(result.Entries, Has.Count.EqualTo(3));
      Assert.That(result.Entries.Select(entry => entry.SavedIndex),
        Is.EqualTo(new[] { 0, 1, 2 }));
      Assert.That(result.Entries.Select(entry => entry.SavedEntry),
        Is.EqualTo(visual.AnimationData));
      Assert.That(result.Entries.Select(entry => entry.Slot),
        Is.EqualTo(new[] { resources.Slots[4], resources.Slots[0], resources.Slots[3] }));
      Assert.That(result.Entries.Select(entry => entry.IsActive),
        Is.EqualTo(new[] { true, false, false }));
      Assert.That(result.ActiveEntryCount, Is.EqualTo(1));
      Assert.That(result.Status,
        Is.EqualTo(WildAnimalSavedAnimationResolutionStatus.ExactSingleClip));
      Assert.That(result.ExactSingleClipEntry, Is.SameAs(result.Entries[0]));
      Assert.That(result.ExactSingleClipEntry!.Slot.Source!.Identity,
        Is.EqualTo("Clip4:modelanim"));
    }
  }

  [Test]
  public void Resolve_PreservesInstalledWeightedBlendWithoutSelectingOneClip() {
    var resources = CreateResources();
    var visual = Visual([
      new DatWildAnimalAnimationData(0.14244859f, 0, 0.80371034f),
      new DatWildAnimalAnimationData(0.03722034f, 2, 0.19628961f),
      new DatWildAnimalAnimationData(1.5f, 4, 0f),
    ]);

    var result = WildAnimalSavedAnimationResolver.Resolve(visual, resources);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Entries.Select(entry => entry.SavedEntry),
        Is.EqualTo(visual.AnimationData));
      Assert.That(result.ActiveEntryCount, Is.EqualTo(2));
      Assert.That(result.Entries.Where(entry => entry.IsActive)
        .Select(entry => entry.Slot.SerializedIndex), Is.EqualTo(new[] { 0, 2 }));
      Assert.That(result.Entries.Where(entry => entry.IsActive)
        .Select(entry => entry.SavedEntry.Weight),
        Is.EqualTo(new[] { 0.80371034f, 0.19628961f }));
      Assert.That(result.Status,
        Is.EqualTo(WildAnimalSavedAnimationResolutionStatus.WeightedState));
      Assert.That(result.ExactSingleClipEntry, Is.Null);
    }
  }

  [TestCase(MalformedSavedAnimation.NullEntries)]
  [TestCase(MalformedSavedAnimation.TooManyEntries)]
  [TestCase(MalformedSavedAnimation.TypeBelowRange)]
  [TestCase(MalformedSavedAnimation.TypeAboveRange)]
  [TestCase(MalformedSavedAnimation.DuplicateType)]
  [TestCase(MalformedSavedAnimation.NonFiniteTime)]
  [TestCase(MalformedSavedAnimation.NonFiniteWeight)]
  [TestCase(MalformedSavedAnimation.ActivePlaceholder)]
  public void Resolve_RejectsMalformedSavedState(MalformedSavedAnimation malformed) {
    var visual = MalformedVisual(malformed);

    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalSavedAnimationResolver.Resolve(visual, CreateResources())));
  }

  [TestCase(MalformedAnimationResources.WrongSlotCount)]
  [TestCase(MalformedAnimationResources.ChangedSerializedIndex)]
  [TestCase(MalformedAnimationResources.ChangedReference)]
  [TestCase(MalformedAnimationResources.MissingResolvedSource)]
  [TestCase(MalformedAnimationResources.WrongWadCount)]
  public void Resolve_RejectsResourceGraphsThatCannotProveDirectSlotIdentity(
    MalformedAnimationResources malformed
  ) {
    var resources = MalformedResources(malformed);

    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalSavedAnimationResolver.Resolve(
        Visual([new DatWildAnimalAnimationData(0f, 0, 1f)]),
        resources)));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void ResolveInstalled_OstrichFarmRetainsAllNativeStates() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var installRoot = Path.GetFullPath(root!);
    var data = DatTerrainReader.Read(Path.Combine(
      installRoot,
      "Campaigns",
      "Base",
      "Wild",
      "OstrichFarm.dat"));
    var species = WildAnimalSpeciesModelResourceBridge.ResolveInstalled(
      installRoot,
      "Ostrich:was");
    var bridges = species.Variants
      .Select(variant => variant.Variant.AnimationDataReference)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
        reference => reference,
        reference => WildAnimalModelAnimationResourceBridge.ResolveInstalled(
          species,
          reference),
        StringComparer.OrdinalIgnoreCase);

    var results = data.WildAnimalPlacements.Select(placement => {
      var reference = species.Variants[placement.Animal.Type]
        .Variant.AnimationDataReference;
      return WildAnimalSavedAnimationResolver.Resolve(
        placement.Visual,
        bridges[reference]);
    }).ToArray();

    using (Assert.EnterMultipleScope()) {
      Assert.That(results, Has.Length.EqualTo(16));
      Assert.That(results.Sum(result => result.Entries.Count), Is.EqualTo(171));
      Assert.That(results.Sum(result => result.ActiveEntryCount), Is.EqualTo(17));
      Assert.That(results.Count(result => result.Status ==
          WildAnimalSavedAnimationResolutionStatus.ExactSingleClip),
        Is.EqualTo(15));
      Assert.That(results.Count(result => result.Status ==
          WildAnimalSavedAnimationResolutionStatus.WeightedState),
        Is.EqualTo(1));
      Assert.That(results.All(result => result.Entries.All(entry =>
        ReferenceEquals(
          entry.Slot,
          result.Resources.Slots[entry.SavedEntry.Type]))), Is.True);
    }

    var blend = results.Single(result => result.Visual.EntryId == 9_137);
    using (Assert.EnterMultipleScope()) {
      Assert.That(blend.ActiveEntryCount, Is.EqualTo(2));
      Assert.That(blend.Entries.Where(entry => entry.IsActive)
        .Select(entry => entry.SavedEntry.Type), Is.EqualTo(new[] { 0, 2 }));
      Assert.That(blend.ExactSingleClipEntry, Is.Null);
    }
  }

  private static DatWildAnimalVisualData MalformedVisual(
    MalformedSavedAnimation malformed
  ) => malformed switch {
    MalformedSavedAnimation.NullEntries => Visual(null!),
    MalformedSavedAnimation.TooManyEntries => Visual(
      Enumerable.Range(0, 32)
        .Select(index => new DatWildAnimalAnimationData(0f, index, 0f))
        .ToArray()),
    MalformedSavedAnimation.TypeBelowRange =>
      Visual([new DatWildAnimalAnimationData(0f, -1, 0f)]),
    MalformedSavedAnimation.TypeAboveRange =>
      Visual([new DatWildAnimalAnimationData(0f, 31, 0f)]),
    MalformedSavedAnimation.DuplicateType => Visual([
      new DatWildAnimalAnimationData(0f, 1, 0f),
      new DatWildAnimalAnimationData(0f, 1, 1f),
    ]),
    MalformedSavedAnimation.NonFiniteTime =>
      Visual([new DatWildAnimalAnimationData(float.NaN, 0, 1f)]),
    MalformedSavedAnimation.NonFiniteWeight =>
      Visual([new DatWildAnimalAnimationData(0f, 0, float.PositiveInfinity)]),
    MalformedSavedAnimation.ActivePlaceholder =>
      Visual([new DatWildAnimalAnimationData(0f, 3, 1f)]),
    _ => throw new ArgumentOutOfRangeException(nameof(malformed)),
  };

  private static WildAnimalModelAnimationResourceBridgeResult MalformedResources(
    MalformedAnimationResources malformed
  ) {
    var resources = CreateResources();
    switch (malformed) {
      case MalformedAnimationResources.WrongSlotCount:
        return resources with { Slots = resources.Slots.Take(30).ToArray() };
      case MalformedAnimationResources.ChangedSerializedIndex: {
        var slots = resources.Slots.ToArray();
        slots[0] = slots[0] with { SerializedIndex = 1 };
        return resources with { Slots = slots };
      }
      case MalformedAnimationResources.ChangedReference: {
        var slots = resources.Slots.ToArray();
        slots[0] = slots[0] with { Reference = "Other:modelanim" };
        return resources with { Slots = slots };
      }
      case MalformedAnimationResources.MissingResolvedSource: {
        var slots = resources.Slots.ToArray();
        slots[0] = slots[0] with { Source = null };
        return resources with { Slots = slots };
      }
      case MalformedAnimationResources.WrongWadCount:
        return resources with {
          AnimationData = resources.AnimationData with { SerializedCountAt08 = 30 },
        };
      default:
        throw new ArgumentOutOfRangeException(nameof(malformed));
    }
  }

  private static DatWildAnimalVisualData Visual(
    IReadOnlyList<DatWildAnimalAnimationData> entries
  ) => new(100, entries, true, true, Matrix4x4.Identity);

  private static WildAnimalModelAnimationResourceBridgeResult CreateResources() {
    var root = Path.GetFullPath(Path.Combine(
      Path.GetTempPath(),
      "OpenRCT3-WildAnimalSavedAnimationResolver-Fixture"));
    var modelCommonPath = Path.Combine(root, "AnimalModels.common.ovl");
    var animationCommonPath = Path.Combine(root, "AnimalAnimations.common.ovl");
    var animationUniquePath = Path.Combine(root, "AnimalAnimations.unique.ovl");
    var references = Enumerable.Range(0, 31).Select(index =>
      PlaceholderSlots.Contains(index) ? ":modelanim" : $"Clip{index}:modelanim")
      .ToArray();
    var wad = new WildAnimalAnimationDataDefinition(
      "Animal",
      animationUniquePath,
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
      Enumerable.Repeat(1f, 31).ToArray(),
      Enumerable.Repeat(0f, 31).ToArray());
    var slots = references.Select((reference, index) => {
      if (PlaceholderSlots.Contains(index))
        return new WildAnimalModelAnimationSlotLink(
          index,
          reference,
          WildAnimalModelAnimationSlotStatus.Placeholder,
          null);
      var name = $"Clip{index}";
      var resource = ModelAnimation(
        name,
        animationCommonPath,
        Convert.ToUInt32(index + 1));
      var source = new WildAnimalModelAnimationResourceSource(
        new OvlFile(name, FileType.ModelAnim, animationCommonPath),
        resource);
      return new WildAnimalModelAnimationSlotLink(
        index,
        reference,
        WildAnimalModelAnimationSlotStatus.Resolved,
        source);
    }).ToArray();
    return new(
      "Animal:wad",
      modelCommonPath,
      animationCommonPath,
      new OvlFile("Animal", FileType.WildAnimalAnimData, animationUniquePath),
      wad,
      slots);
  }

  private static ModelAnimationDefinition ModelAnimation(
    string name,
    string sourcePath,
    uint dataAddress
  ) => new(
    name,
    sourcePath,
    dataAddress,
    1f,
    1,
    dataAddress + 88,
    dataAddress + 92,
    1,
    1,
    dataAddress + 96,
    dataAddress + 108,
    new uint[] { 0 },
    new uint[] { 0 },
    [new ModelAnimationTriple(0, 0, 0)],
    [new ModelAnimationFourTuple(0, 0, 0, 1)],
    ["Bone"],
    ["Bone"]);

  public enum MalformedSavedAnimation {
    NullEntries,
    TooManyEntries,
    TypeBelowRange,
    TypeAboveRange,
    DuplicateType,
    NonFiniteTime,
    NonFiniteWeight,
    ActivePlaceholder,
  }

  public enum MalformedAnimationResources {
    WrongSlotCount,
    ChangedSerializedIndex,
    ChangedReference,
    MissingResolvedSource,
    WrongWadCount,
  }
}
