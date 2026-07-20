// Wild Animal Park Resource Registry Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalParkResourceRegistryTests {
  private const string SpeciesOverlay = @"WildAnimals\WildAnimals";

  [Test]
  public void Build_ResolvesDistinctUsedSpeciesOnceAndPreservesPlacementOrder() {
    var ostrich = Species(101, "Ostrich");
    var elephant = Species(102, "Elephant");
    var park = ParkWith(
      Placement(301, 201, ostrich),
      Placement(302, 202, elephant),
      Placement(303, 203, ostrich));
    var resolver = new FakeResolver();
    var ostrichResult = Result("Ostrich");
    var elephantResult = Result("Elephant");
    resolver.Results.Add("Ostrich:was", ostrichResult);
    resolver.Results.Add("Elephant:was", elephantResult);

    var registry = WildAnimalParkResourceRegistry.Build(park, InstallRoot, resolver);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        resolver.Requests,
        Is.EqualTo(new[] {
          (InstallRoot, "Ostrich:was"),
          (InstallRoot, "Elephant:was"),
        }));
      Assert.That(
        registry.SpeciesResources.Select(resource => resource.Species.EntryId),
        Is.EqualTo(new ulong[] { 101, 102 }));
      Assert.That(
        registry.SpeciesResources.Select(resource => resource.RegistryIndex),
        Is.EqualTo(new[] { 0, 1 }));
      Assert.That(
        registry.SpeciesResources.Select(resource => resource.PlacementIndices),
        Is.EqualTo(new[] {
          new[] { 0, 2 },
          new[] { 1 },
        }));
      Assert.That(
        registry.Placements.Select(link => link.Placement.Animal.EntryId),
        Is.EqualTo(new ulong[] { 301, 302, 303 }));
      Assert.That(
        registry.Placements.Select(link => link.SpeciesResource.RegistryIndex),
        Is.EqualTo(new[] { 0, 1, 0 }));
      Assert.That(
        registry.Placements.Select(link => link.VariantSelectionStatus),
        Is.All.EqualTo(DatWildAnimalVariantSelectionStatus.Unsupported));
      Assert.That(registry.SpeciesResources[0].Bridge, Is.SameAs(ostrichResult));
      Assert.That(registry.SpeciesResources[1].Bridge, Is.SameAs(elephantResult));
    }
    Assert.That(
      registry.Placements[0].SpeciesResource,
      Is.SameAs(registry.Placements[2].SpeciesResource));
  }

  [Test]
  public void Build_RejectsIdentityDriftForOneWasEntryIdBeforeResolving() {
    var park = ParkWith(
      Placement(301, 201, Species(101, "Ostrich")),
      Placement(302, 202, Species(101, "Elephant")));
    var resolver = new FakeResolver();

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalParkResourceRegistry.Build(park, InstallRoot, resolver)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("changes overlay, symbol, or unlock identity"));
      Assert.That(resolver.Requests, Is.Empty);
    }
  }

  [Test]
  public void Build_RejectsDuplicateSemanticIdentityIgnoringCaseAndSeparators() {
    var first = Species(101, "Ostrich");
    var second = new DatWildAnimalSpeciesDatabaseEntryData(
      102,
      true,
      "wildanimals/wildanimals",
      "ostrich");
    var park = ParkWith(
      Placement(301, 201, first),
      Placement(302, 202, second));
    var resolver = new FakeResolver();

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalParkResourceRegistry.Build(park, InstallRoot, resolver)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("duplicate DAT WAS identity"));
      Assert.That(resolver.Requests, Is.Empty);
    }
  }

  [Test]
  public void Build_RejectsOneResolverResultReusedAcrossDistinctSpecies() {
    var park = ParkWith(
      Placement(301, 201, Species(101, "Ostrich")),
      Placement(302, 202, Species(102, "Elephant")));
    var shared = Result("Ostrich");
    var resolver = new FakeResolver();
    resolver.Results.Add("Ostrich:was", shared);
    resolver.Results.Add("Elephant:was", shared);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalParkResourceRegistry.Build(park, InstallRoot, resolver)));

    Assert.That(exception!.Message, Does.Contain("reused one bridge result"));
  }

  [TestCase(WildAnimalRegistryResolverDrift.OwnerPath)]
  [TestCase(WildAnimalRegistryResolverDrift.CrossPackageSymbol)]
  [TestCase(WildAnimalRegistryResolverDrift.SpeciesReference)]
  [TestCase(WildAnimalRegistryResolverDrift.SpeciesIdentity)]
  [TestCase(WildAnimalRegistryResolverDrift.SerializedVariant)]
  public void Build_RejectsCrossSourceOrDriftedResolverEvidence(
    WildAnimalRegistryResolverDrift drift
  ) {
    var park = ParkWith(Placement(301, 201, Species(101, "Ostrich")));
    var resolver = new FakeResolver();
    resolver.Results.Add("Ostrich:was", Drift(Result("Ostrich"), drift));

    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalParkResourceRegistry.Build(park, InstallRoot, resolver)));
  }

  [Test]
  public void Build_RejectsPlacementCountAboveBoundBeforeResolving() {
    var placement = Placement(301, 201, Species(101, "Ostrich"));
    var park = new Park();
    park.WildAnimalPlacements.AddRange(
      Enumerable.Repeat(placement, 100_001));
    var resolver = new FakeResolver();

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalParkResourceRegistry.Build(park, InstallRoot, resolver)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("placement count 100001 exceeds 100000"));
      Assert.That(resolver.Requests, Is.Empty);
    }
  }

  [Test]
  public void Build_RejectsWasSymbolWhoseTaggedReferenceExceedsBound() {
    var park = ParkWith(Placement(
      301,
      201,
      Species(101, new string('X', 4_093))));
    var resolver = new FakeResolver();

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalParkResourceRegistry.Build(park, InstallRoot, resolver)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("exceeds 4092 characters"));
      Assert.That(resolver.Requests, Is.Empty);
    }
  }

  [Test]
  public void Build_RejectsCrossLinkedPlacementBeforeResolving() {
    var species = Species(101, "Ostrich");
    var placement = Placement(301, 201, species);
    var driftedAnimal = placement.Animal with { SpeciesDatabaseEntryId = 999 };
    var park = ParkWith(placement with { Animal = driftedAnimal });
    var resolver = new FakeResolver();

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalParkResourceRegistry.Build(park, InstallRoot, resolver)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain(
        "references WAS entry 999, but placement carries 101"));
      Assert.That(resolver.Requests, Is.Empty);
    }
  }

  private static string InstallRoot { get; } = Path.GetFullPath(Path.Combine(
    Path.GetTempPath(),
    "OpenRCT3-WildAnimalParkResourceRegistry-Fixture"));

  private static Park ParkWith(params DatWildAnimalPlacementData[] placements) {
    var park = new Park();
    park.WildAnimalPlacements.AddRange(placements);
    return park;
  }

  private static DatWildAnimalSpeciesDatabaseEntryData Species(
    ulong entryId,
    string symbol
  ) => new(entryId, true, SpeciesOverlay, symbol);

  private static DatWildAnimalPlacementData Placement(
    ulong animalEntryId,
    ulong visualEntryId,
    DatWildAnimalSpeciesDatabaseEntryData species
  ) {
    var animal = new DatWildAnimalData(
      animalEntryId,
      species.EntryId,
      visualEntryId,
      true,
      false,
      1);
    var visual = new DatWildAnimalVisualData(
      visualEntryId,
      [],
      true,
      true,
      Matrix4x4.Identity);
    return new DatWildAnimalPlacementData(
      animal,
      species,
      visual,
      DatWildAnimalVariantSelectionStatus.Unsupported);
  }

  private static WildAnimalSpeciesModelResourceBridgeResult Result(string symbol) {
    var speciesCommonPath = Path.Combine(
      InstallRoot,
      "WildAnimals",
      "WildAnimals.common.ovl");
    var speciesUniquePath = ToUniquePath(speciesCommonPath);
    var modelCommonPath = Path.Combine(
      InstallRoot,
      "WildAnimals",
      symbol,
      $"{symbol}_data.common.ovl");
    var variants = Enumerable.Range(0, 4)
      .Select(index => new WildAnimalSpeciesVariant(
        $"{symbol}Model{index}:mdl",
        $"{symbol}Animation{index}:wad"))
      .ToArray();
    var species = new WildAnimalSpeciesDefinition(
      symbol,
      $@"WildAnimals\{symbol}\{symbol}_data",
      variants);
    var links = variants.Select((variant, index) => {
      var modelName = $"{symbol}Model{index}";
      var modelFile = new OvlFile(modelName, FileType.Model, modelCommonPath);
      var model = new ModelDefinition(
        modelName,
        modelCommonPath,
        1,
        1,
        1,
        0,
        0,
        0);
      return new WildAnimalSpeciesModelVariantLink(
        index,
        variant,
        new WildAnimalSpeciesModelResourceSource(modelFile, model));
    }).ToArray();
    return new WildAnimalSpeciesModelResourceBridgeResult(
      $"{symbol}:was",
      speciesCommonPath,
      new OvlFile(symbol, FileType.WildAnimalSpecies, speciesUniquePath),
      species,
      modelCommonPath,
      Array.AsReadOnly(links));
  }

  private static WildAnimalSpeciesModelResourceBridgeResult Drift(
    WildAnimalSpeciesModelResourceBridgeResult result,
    WildAnimalRegistryResolverDrift drift
  ) => drift switch {
    WildAnimalRegistryResolverDrift.OwnerPath => result with {
      SpeciesCommonPath = Path.Combine(InstallRoot, "Other", "Owner.common.ovl"),
    },
    WildAnimalRegistryResolverDrift.CrossPackageSymbol => result with {
      SpeciesFile = result.SpeciesFile with {
        Path = Path.Combine(InstallRoot, "Other", "Owner.unique.ovl"),
      },
    },
    WildAnimalRegistryResolverDrift.SpeciesReference => result with {
      SpeciesReference = "Giraffe:was",
    },
    WildAnimalRegistryResolverDrift.SpeciesIdentity => result with {
      SpeciesFile = result.SpeciesFile with { Name = "Giraffe" },
    },
    WildAnimalRegistryResolverDrift.SerializedVariant => result with {
      Variants = Array.AsReadOnly(result.Variants
        .Select((variant, index) => index == 0
          ? variant with { SerializedIndex = 1 }
          : variant)
        .ToArray()),
    },
    _ => throw new ArgumentOutOfRangeException(nameof(drift)),
  };

  private static string ToUniquePath(string commonPath) =>
    commonPath[..^".common.ovl".Length] + ".unique.ovl";

  private sealed class FakeResolver : IWildAnimalParkSpeciesResourceResolver {
    public Dictionary<string, WildAnimalSpeciesModelResourceBridgeResult> Results { get; } =
      new(StringComparer.Ordinal);
    public List<(string InstallRoot, string SpeciesReference)> Requests { get; } = [];

    public WildAnimalSpeciesModelResourceBridgeResult Resolve(
      string installRoot,
      string speciesReference
    ) {
      Requests.Add((installRoot, speciesReference));
      return Results[speciesReference];
    }
  }
}

public enum WildAnimalRegistryResolverDrift {
  OwnerPath,
  CrossPackageSymbol,
  SpeciesReference,
  SpeciesIdentity,
  SerializedVariant,
}
