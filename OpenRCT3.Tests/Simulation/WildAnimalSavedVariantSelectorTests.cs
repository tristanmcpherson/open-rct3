// Wild Animal Saved Variant Selector Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalSavedVariantSelectorTests {
  [TestCase(0, true, true)]
  [TestCase(1, true, false)]
  [TestCase(2, false, true)]
  [TestCase(3, false, false)]
  public void Select_BindsExactNativeOrderedRegistryVariant(
    int type,
    bool isAdult,
    bool isMale
  ) {
    using var registry = CreateRegistry();
    var animal = Animal(type, isAdult, isMale);

    var selection = WildAnimalSavedVariantSelector.Select(animal, registry);

    using (Assert.EnterMultipleScope()) {
      Assert.That(selection.Animal, Is.SameAs(animal));
      Assert.That(selection.SerializedVariantIndex, Is.EqualTo(type));
      Assert.That(selection.Variant, Is.SameAs(registry.Variants[type]));
      Assert.That(selection.Variant.VariantLink.SerializedIndex, Is.EqualTo(type));
    }
  }

  [Test]
  public void Select_RejectsEveryOutOfRangeTypeWithoutSlotThreeFallback() {
    using var registry = CreateRegistry();
    foreach (var type in new[] { int.MinValue, -1, 4, int.MaxValue }) {
      var error = Assert.Throws<InvalidDataException>(new Action(() =>
        WildAnimalSavedVariantSelector.Select(Animal(type, false, false), registry)));
      Assert.That(error!.Message, Does.Contain($"Type {type}").And.Contain("outside"));
    }
  }

  [Test]
  public void Select_RejectsAdultOrSexStateThatDisagreesWithType() {
    using var registry = CreateRegistry();
    foreach (var type in Enumerable.Range(0, 4)) {
      var expectedAdult = type is 0 or 1;
      var expectedMale = type is 0 or 2;
      var adultError = Assert.Throws<InvalidDataException>(new Action(() =>
        WildAnimalSavedVariantSelector.Select(
          Animal(type, !expectedAdult, expectedMale),
          registry)));
      var maleError = Assert.Throws<InvalidDataException>(new Action(() =>
        WildAnimalSavedVariantSelector.Select(
          Animal(type, expectedAdult, !expectedMale),
          registry)));
      using (Assert.EnterMultipleScope()) {
        Assert.That(adultError!.Message, Does.Contain($"Type {type}").And.Contain("requires"));
        Assert.That(maleError!.Message, Does.Contain($"Type {type}").And.Contain("requires"));
      }
    }
  }

  private static DatWildAnimalData Animal(int type, bool isAdult, bool isMale) => new(
    100,
    200,
    300,
    isAdult,
    isMale,
    type);

  private static WildAnimalModelTemplateRegistry CreateRegistry() {
    var root = Path.GetFullPath(Path.Combine(
      Path.GetTempPath(),
      "OpenRCT3-WildAnimalSavedVariantSelector-Fixture"));
    var speciesCommonPath = Path.Combine(root, "Animals.common.ovl");
    var speciesUniquePath = Path.Combine(root, "Animals.unique.ovl");
    var modelCommonPath = Path.Combine(root, "Models.common.ovl");
    var variants = Enumerable.Range(0, 4)
      .Select(index => new WildAnimalSpeciesVariant(
        "Animal:mdl",
        $"Animal{index}:wad"))
      .ToArray();
    var species = new WildAnimalSpeciesDefinition(
      "Species",
      @"WildAnimals\Species_data",
      variants);
    var model = new ModelDefinition(
      "Animal",
      modelCommonPath,
      0,
      0,
      0,
      0,
      0,
      0) {
      Bones = [],
      Groups = [],
    };
    var source = new WildAnimalSpeciesModelResourceSource(
      new OvlFile("Animal", FileType.Model, modelCommonPath),
      model);
    var links = variants.Select((variant, index) =>
      new WildAnimalSpeciesModelVariantLink(index, variant, source)).ToArray();
    var resources = new WildAnimalSpeciesModelResourceBridgeResult(
      "Species:was",
      speciesCommonPath,
      new OvlFile("Species", FileType.WildAnimalSpecies, speciesUniquePath),
      species,
      modelCommonPath,
      links);
    return WildAnimalModelTemplateRegistry.Build(resources, _ => [], _ => { });
  }
}
