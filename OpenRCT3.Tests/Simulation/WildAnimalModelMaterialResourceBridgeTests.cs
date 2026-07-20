// Wild Animal Model Material Resource Bridge Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using DecodedTexture = OpenCobra.OVL.Files.Texture;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalModelMaterialResourceBridgeTests {
  [Test]
  public void Resolve_UsesSurfaceValuesInsteadOfZippingMeshesToStrings() {
    var fixture = new MaterialFixture();

    var result = WildAnimalModelMaterialResourceBridge.Resolve(
      fixture.Resources,
      fixture.Archive);

    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(result.ModelPackageCommonPath, Is.EqualTo(fixture.ModelCommonPath));
        Assert.That(result.Materials.Select(material => material.TextureReference),
          Is.EqualTo(new[] { "TextureA:tex", "TextureB:tex" }));
        Assert.That(result.Materials.Select(material => material.TextureStyleReference),
          Is.EqualTo(new[] { "SIAlphaDS:txs", "SIOpaque:txs" }));
        Assert.That(result.Materials.Select(material => material.TextureDataAddress),
          Is.EqualTo(new uint[] { 1_000, 2_000 }));
        Assert.That(result.Materials.Select(material => material.Albedo.Style),
          Is.EqualTo(new[] { "SIAlphaDS:txs", "SIOpaque:txs" }));
        Assert.That(result.Bindings.Select(binding => binding.SourceMeshName),
          Is.EqualTo(new[] {
            "AdultElephant/group-0/mesh-0",
            "AdultElephant/group-0/mesh-1",
            "BabyElephant/group-0/mesh-0",
          }));
        Assert.That(result.Bindings.Select(binding => binding.SerializedTextureName),
          Is.EqualTo(new[] { "TextureB", "TextureA", "TextureA" }));
        Assert.That(result.Bindings.Select(binding => binding.Material.TextureReference),
          Is.EqualTo(new[] { "TextureB:tex", "TextureA:tex", "TextureA:tex" }));
        Assert.That(result.Bindings.Select(binding => binding.SourceSurfaceIndex),
          Is.EqualTo(new[] { 1, 0, 0 }));
        Assert.That(result.Bindings[1].Material,
          Is.SameAs(result.Bindings[2].Material));
        Assert.That(fixture.Archive.DecodeRequests,
          Is.EqualTo(new[] { "TextureA", "TextureB" }));
      }
    } finally {
      result.Dispose();
    }

    result.Dispose();
    Assert.That(result.IsDisposed, Is.True);
  }

  [TestCase(MalformedMaterial.StringIndexDrift)]
  [TestCase(MalformedMaterial.CountADrift)]
  [TestCase(MalformedMaterial.ValuesB)]
  [TestCase(MalformedMaterial.Field6)]
  [TestCase(MalformedMaterial.UnsupportedHighWord)]
  [TestCase(MalformedMaterial.OutOfRangeMesh)]
  [TestCase(MalformedMaterial.DuplicateMeshMapping)]
  [TestCase(MalformedMaterial.MissingMeshMapping)]
  [TestCase(MalformedMaterial.MissingTexture)]
  [TestCase(MalformedMaterial.AmbiguousTexture)]
  [TestCase(MalformedMaterial.CrossPairTexture)]
  [TestCase(MalformedMaterial.MissingTextureAddress)]
  [TestCase(MalformedMaterial.MissingStyleReference)]
  [TestCase(MalformedMaterial.StyleOwnerDrift)]
  [TestCase(MalformedMaterial.WrongStyleTag)]
  [TestCase(MalformedMaterial.AlbedoIdentityDrift)]
  [TestCase(MalformedMaterial.ModelAddressDrift)]
  [TestCase(MalformedMaterial.AmbiguousModelSymbol)]
  [TestCase(MalformedMaterial.ArchivePairDrift)]
  public void Resolve_RejectsMalformedAmbiguousMissingOrDriftedEvidence(
    MalformedMaterial malformed
  ) {
    var fixture = new MaterialFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() =>
      WildAnimalModelMaterialResourceBridge.Resolve(
        fixture.Resources,
        fixture.Archive)));

    foreach (var texture in fixture.Archive.DecodedTextures)
      texture.Dispose();
  }

  private sealed class MaterialFixture {
    private readonly string speciesCommonPath;
    private readonly string speciesUniquePath;
    private readonly string modelUniquePath;
    private readonly OvlFile speciesFile;
    private readonly OvlFile adultFile;
    private readonly OvlFile babyFile;
    private readonly OvlFile textureAFile;
    private readonly OvlFile textureBFile;
    private readonly WildAnimalSpeciesDefinition species;
    private ModelDefinition adultModel;
    private readonly ModelDefinition babyModel;

    public MaterialFixture() {
      var root = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        "OpenRCT3-WildAnimalModelMaterialResourceBridge-Fixture"));
      speciesCommonPath = Path.Combine(root, "WildAnimals", "WildAnimals.common.ovl");
      speciesUniquePath = ToUniquePath(speciesCommonPath);
      ModelCommonPath = Path.Combine(
        root,
        "WildAnimals",
        "elephant",
        "Elephant_data.common.ovl");
      modelUniquePath = ToUniquePath(ModelCommonPath);
      speciesFile = new OvlFile(
        "Elephant", FileType.WildAnimalSpecies, speciesUniquePath);
      adultFile = new OvlFile("AdultElephant", FileType.Model, ModelCommonPath);
      babyFile = new OvlFile("BabyElephant", FileType.Model, ModelCommonPath);
      textureAFile = new OvlFile("TextureA", FileType.Texture, modelUniquePath);
      textureBFile = new OvlFile("TextureB", FileType.Texture, modelUniquePath);
      adultModel = Definition(
        "AdultElephant",
        ModelCommonPath,
        350,
        ["TextureA", "TextureB"],
        Group(
          2,
          Surface(0, 0x00010001),
          Surface(1, 0x00010000)));
      babyModel = Definition(
        "BabyElephant",
        ModelCommonPath,
        430,
        ["TextureA"],
        Group(1, Surface(0, 0x00010000)));
      species = new WildAnimalSpeciesDefinition(
        "Elephant",
        @"WildAnimals\elephant\Elephant_data",
        new[] {
          new WildAnimalSpeciesVariant("AdultElephant:mdl", "Elephant:wad"),
          new WildAnimalSpeciesVariant("AdultElephant:mdl", "Elephant:wad"),
          new WildAnimalSpeciesVariant("BabyElephant:mdl", "babyElephant:wad"),
          new WildAnimalSpeciesVariant("BabyElephant:mdl", "babyElephant:wad"),
        });
      Archive = new FakeArchive(ModelCommonPath);
      Archive.FilesList.AddRange([adultFile, babyFile, textureAFile, textureBFile]);
      Archive.DataAddresses.Add(adultFile, 350);
      Archive.DataAddresses.Add(babyFile, 430);
      Archive.DataAddresses.Add(textureAFile, 1_000);
      Archive.DataAddresses.Add(textureBFile, 2_000);
      Archive.SymbolReferences.Add(
        1_044,
        Style("SIAlphaDS:txs", textureAFile, 1_000, 700));
      Archive.SymbolReferences.Add(
        2_044,
        Style("SIOpaque:txs", textureBFile, 2_000, 720));
      Resources = CreateResources();
    }

    public string ModelCommonPath { get; }
    public FakeArchive Archive { get; }
    public WildAnimalSpeciesModelResourceBridgeResult Resources { get; private set; }

    public void MakeMalformed(MalformedMaterial malformed) {
      switch (malformed) {
        case MalformedMaterial.StringIndexDrift:
          ReplaceAdultGroup(Group(
            2,
            Surface(2, 0x00010001),
            Surface(1, 0x00010000)));
          break;
        case MalformedMaterial.CountADrift:
          ReplaceAdultGroup(Group(
            2,
            new ModelSurfaceRecord(0, 2, 0, 0, [0x00010001], []),
            Surface(1, 0x00010000)));
          break;
        case MalformedMaterial.ValuesB:
          ReplaceAdultGroup(Group(
            2,
            new ModelSurfaceRecord(0, 1, 1, 0, [0x00010001], [0]),
            Surface(1, 0x00010000)));
          break;
        case MalformedMaterial.Field6:
          ReplaceAdultGroup(Group(
            2,
            new ModelSurfaceRecord(0, 1, 0, 1, [0x00010001], []),
            Surface(1, 0x00010000)));
          break;
        case MalformedMaterial.UnsupportedHighWord:
          ReplaceAdultGroup(Group(
            2,
            Surface(0, 0x00020001),
            Surface(1, 0x00010000)));
          break;
        case MalformedMaterial.OutOfRangeMesh:
          ReplaceAdultGroup(Group(
            2,
            Surface(0, 0x00010002),
            Surface(1, 0x00010000)));
          break;
        case MalformedMaterial.DuplicateMeshMapping:
          ReplaceAdultGroup(Group(
            2,
            Surface(0, 0x00010000),
            Surface(1, 0x00010000)));
          break;
        case MalformedMaterial.MissingMeshMapping:
          ReplaceAdultGroup(Group(
            2,
            Surface(0, 0x00010001),
            Surface(1)));
          break;
        case MalformedMaterial.MissingTexture:
          Archive.FilesList.Remove(textureAFile);
          break;
        case MalformedMaterial.AmbiguousTexture:
          Archive.FilesList.Add(new OvlFile(
            "texturea", FileType.Texture, ModelCommonPath));
          break;
        case MalformedMaterial.CrossPairTexture:
          Archive.FilesList[Archive.FilesList.IndexOf(textureAFile)] =
            textureAFile with { Path = Path.Combine(Path.GetTempPath(), "outside.unique.ovl") };
          break;
        case MalformedMaterial.MissingTextureAddress:
          Archive.DataAddresses.Remove(textureAFile);
          break;
        case MalformedMaterial.MissingStyleReference:
          Archive.SymbolReferences.Remove(1_044);
          break;
        case MalformedMaterial.StyleOwnerDrift:
          Archive.SymbolReferences[1_044] =
            Style("SIAlphaDS:txs", textureAFile, 1_001, 700);
          break;
        case MalformedMaterial.WrongStyleTag:
          Archive.SymbolReferences[1_044] =
            Style("SIAlphaDS:ftx", textureAFile, 1_000, 700);
          break;
        case MalformedMaterial.AlbedoIdentityDrift:
          Archive.DecodedNameOverride = "OtherTexture";
          break;
        case MalformedMaterial.ModelAddressDrift:
          adultModel = adultModel with { DataAddress = 351 };
          Resources = CreateResources();
          break;
        case MalformedMaterial.AmbiguousModelSymbol:
          Archive.FilesList.Add(new OvlFile(
            "adultElephant", FileType.Model, ModelCommonPath));
          break;
        case MalformedMaterial.ArchivePairDrift:
          Archive.CommonPathValue = Path.Combine(
            Path.GetDirectoryName(ModelCommonPath)!,
            "Other.common.ovl");
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed));
      }
    }

    private void ReplaceAdultGroup(ModelGroup group) {
      adultModel = adultModel with { Groups = [group] };
      Resources = CreateResources();
    }

    private WildAnimalSpeciesModelResourceBridgeResult CreateResources() {
      var adultSource = new WildAnimalSpeciesModelResourceSource(adultFile, adultModel);
      var babySource = new WildAnimalSpeciesModelResourceSource(babyFile, babyModel);
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
        ModelCommonPath,
        Array.AsReadOnly(links));
    }

    private static ResourceSymbolReference Style(
      string symbol,
      OvlFile owner,
      uint dataAddress,
      uint structAddress
    ) => new(symbol, "tex", dataAddress, owner.Path, structAddress);

    private static ModelDefinition Definition(
      string name,
      string path,
      uint dataAddress,
      IReadOnlyList<string> strings,
      params ModelGroup[] groups
    ) => new(name, path, dataAddress, dataAddress - 160, 0, 0, 0, 0) {
      Strings = strings,
      Groups = groups,
    };

    private static ModelGroup Group(
      ushort meshCount,
      params ModelSurfaceRecord[] surfaces
    ) => new(
      Convert.ToUInt16(surfaces.Length),
      meshCount,
      0,
      0,
      [],
      0,
      Enumerable.Range(0, meshCount).Select(_ => Mesh()).ToArray(),
      surfaces);

    private static ModelSurfaceRecord Surface(
      ushort stringIndex,
      params uint[] values
    ) => new(
      stringIndex,
      Convert.ToUInt16(values.Length),
      0,
      0,
      values,
      []);

    private static ModelMesh Mesh() => new(
      0x1305,
      0,
      1,
      0,
      0,
      ushort.MaxValue,
      0,
      0,
      [],
      []);

    private static string ToUniquePath(string commonPath) =>
      commonPath[..^".common.ovl".Length] + ".unique.ovl";
  }

  internal sealed class FakeArchive(string commonPath) : IWildAnimalModelMaterialArchive {
    public string CommonPathValue { get; set; } = commonPath;
    public string CommonPath => CommonPathValue;
    public List<OvlFile> FilesList { get; } = [];
    public IReadOnlyList<OvlFile> Files => FilesList;
    public Dictionary<OvlFile, uint> DataAddresses { get; } = [];
    public Dictionary<uint, ResourceSymbolReference> SymbolReferences { get; } = [];
    public List<string> DecodeRequests { get; } = [];
    public List<DecodedTexture> DecodedTextures { get; } = [];
    public string? DecodedNameOverride { get; set; }

    public bool TryGetDataPointer(OvlFile file, out uint address) =>
      DataAddresses.TryGetValue(file, out address);

    public bool TryGetSymbolReference(
      uint fieldAddress,
      out ResourceSymbolReference? reference
    ) => SymbolReferences.TryGetValue(fieldAddress, out reference);

    public DecodedTexture DecodeTexture(OvlFile file) {
      DecodeRequests.Add(file.Name);
      var texture = new DecodedTexture(
        DecodedNameOverride ?? file.Name,
        TextureFormat.A8R8G8B8,
        1,
        1);
      texture.MipLevels[0] = new Image<Rgba32>(1, 1);
      DecodedTextures.Add(texture);
      return texture;
    }
  }
}

public enum MalformedMaterial {
  StringIndexDrift,
  CountADrift,
  ValuesB,
  Field6,
  UnsupportedHighWord,
  OutOfRangeMesh,
  DuplicateMeshMapping,
  MissingMeshMapping,
  MissingTexture,
  AmbiguousTexture,
  CrossPairTexture,
  MissingTextureAddress,
  MissingStyleReference,
  StyleOwnerDrift,
  WrongStyleTag,
  AlbedoIdentityDrift,
  ModelAddressDrift,
  AmbiguousModelSymbol,
  ArchivePairDrift,
}
