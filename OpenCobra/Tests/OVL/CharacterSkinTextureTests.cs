using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class CharacterSkinTextureTests {
  private static readonly ReferencedTextureField[] PartFields = [
    new(8, "tex", true),
    new(12, "mms", false),
  ];

  private static readonly ReferencedTextureField[] ParticleFields = [
    new(8, "tex", true),
  ];

  [Test]
  public void ResolveLocalTextures_PrtFollowsExactTexAndMmsReferences() {
    var source = new FakeReferencedTextureDataSource();
    var owner = source.AddOwner("prt", 100, "skins.unique.ovl", 10);
    source.References.Add(108, new OvlSymbolReference("BodyTexture:tex", owner));
    source.References.Add(112, new OvlSymbolReference("BodyMaterial:mms", owner));
    var texture = source.AddTarget(
      "BodyTexture", FileType.Texture, "tex", 500, "skins.unique.ovl", 20);
    source.AddTarget(
      "BodyMaterial", FileType.CharacterSkinSet, "mms", 600, "skins.common.ovl", 30);

    var resolved = ReferencedTextureResolver.ResolveLocalTextures(
      OpenCobra.OVL.Version.Five,
      "character skin part",
      "prt",
      20,
      PartFields,
      source);

    Assert.That(resolved, Is.EqualTo(new[] { texture }));
  }

  [Test]
  public void ResolveLocalTextures_PsiDeduplicatesSharedTextureInOwnerOrder() {
    var source = new FakeReferencedTextureDataSource();
    var first = source.AddOwner("psi", 100, "particles.unique.ovl", 10);
    var second = source.AddOwner("psi", 128, "particles.unique.ovl", 20);
    source.References.Add(108, new OvlSymbolReference("ParticlePage01:tex", first));
    source.References.Add(136, new OvlSymbolReference("ParticlePage01:tex", second));
    var texture = source.AddTarget(
      "ParticlePage01", FileType.Texture, "tex", 500, "particles.unique.ovl", 30);

    var resolved = ReferencedTextureResolver.ResolveLocalTextures(
      OpenCobra.OVL.Version.Five,
      "particle sprite item",
      "psi",
      28,
      ParticleFields,
      source);

    Assert.That(resolved, Is.EqualTo(new[] { texture }));
  }

  [Test]
  public void ResolveLocalTextures_AllowsExternalTextureReference() {
    var source = new FakeReferencedTextureDataSource();
    var owner = source.AddOwner("psi", 100, "particles.unique.ovl", 10);
    source.References.Add(108, new OvlSymbolReference("ExpansionParticle:tex", owner));

    var resolved = ReferencedTextureResolver.ResolveLocalTextures(
      OpenCobra.OVL.Version.Five,
      "particle sprite item",
      "psi",
      28,
      ParticleFields,
      source);

    Assert.That(resolved, Is.Empty);
  }

  [TestCase(MalformedReferencedTexture.WrongArchiveVersion)]
  [TestCase(MalformedReferencedTexture.TruncatedRecord)]
  [TestCase(MalformedReferencedTexture.AddressOverflow)]
  [TestCase(MalformedReferencedTexture.MissingExpectedReference)]
  [TestCase(MalformedReferencedTexture.WrongReferenceOwner)]
  [TestCase(MalformedReferencedTexture.WrongReferenceTag)]
  [TestCase(MalformedReferencedTexture.MalformedReference)]
  [TestCase(MalformedReferencedTexture.UnexpectedOwnedReference)]
  [TestCase(MalformedReferencedTexture.AmbiguousLocalTarget)]
  [TestCase(MalformedReferencedTexture.MissingLocalTargetAddress)]
  [TestCase(MalformedReferencedTexture.MissingExactTargetLoader)]
  [TestCase(MalformedReferencedTexture.AmbiguousExactTargetLoader)]
  public void ResolveLocalTextures_RejectsMalformedOrAmbiguousData(
    MalformedReferencedTexture malformed
  ) {
    var fixture = ReferencedTextureFixture.CreateParticle();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Resolve()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledAf01BodyDecodesExactCharacterSkinTexture() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(rct3Path, "Characters", "AF", "AF01_Body_Main.common.ovl");
    Assert.That(path, Does.Exist, $"Installed AF01 body OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    using var textures = CharacterSkins.Extract(ovl);

    Assert.That(textures.Names, Is.EqualTo(new[] { "AF01_Body.tex" }));
    var texture = textures["AF01_Body.tex"];
    var image = texture.MipLevels[0];
    using (Assert.EnterMultipleScope()) {
      Assert.That(texture.Width, Is.EqualTo(256));
      Assert.That(texture.Height, Is.EqualTo(256));
      Assert.That(image[0, 0], Is.EqualTo(new Rgba32(49, 44, 49, 255)));
      Assert.That(image[128, 128], Is.EqualTo(new Rgba32(222, 149, 164, 255)));
    }
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledParticlesDecodesExactReferencedTextures() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(rct3Path, "Particles", "Particles.common.ovl");
    Assert.That(path, Does.Exist, $"Installed particle OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    using var textures = ParticleEffects.Extract(ovl);

    Assert.That(
      textures.Names,
      Is.EqualTo(new[] { "ParticlePage01.tex", "LensFlares.tex" }));
    var particlePage = textures["ParticlePage01.tex"].MipLevels[0];
    var lensFlares = textures["LensFlares.tex"].MipLevels[0];
    using (Assert.EnterMultipleScope()) {
      Assert.That(particlePage.Width, Is.EqualTo(512));
      Assert.That(particlePage.Height, Is.EqualTo(512));
      Assert.That(particlePage[32, 2], Is.EqualTo(new Rgba32(2, 2, 1, 2)));
      Assert.That(lensFlares.Width, Is.EqualTo(256));
      Assert.That(lensFlares.Height, Is.EqualTo(256));
      Assert.That(lensFlares[221, 1], Is.EqualTo(new Rgba32(2, 2, 255, 2)));
    }
  }

  public enum MalformedReferencedTexture {
    WrongArchiveVersion,
    TruncatedRecord,
    AddressOverflow,
    MissingExpectedReference,
    WrongReferenceOwner,
    WrongReferenceTag,
    MalformedReference,
    UnexpectedOwnedReference,
    AmbiguousLocalTarget,
    MissingLocalTargetAddress,
    MissingExactTargetLoader,
    AmbiguousExactTargetLoader,
  }

  private sealed class ReferencedTextureFixture {
    private const string SourcePath = "particles.unique.ovl";
    private readonly FakeReferencedTextureDataSource source = new();
    private OpenCobra.OVL.Version version = OpenCobra.OVL.Version.Five;
    private OvlLoaderEntry owner = null!;
    private OvlFile target = null!;

    public static ReferencedTextureFixture CreateParticle() {
      var fixture = new ReferencedTextureFixture();
      fixture.owner = fixture.source.AddOwner("psi", 100, SourcePath, 10);
      fixture.source.References.Add(
        108,
        new OvlSymbolReference("ParticlePage01:tex", fixture.owner));
      fixture.target = fixture.source.AddTarget(
        "ParticlePage01", FileType.Texture, "tex", 500, SourcePath, 20);
      return fixture;
    }

    public IReadOnlyList<OvlFile> Resolve() =>
      ReferencedTextureResolver.ResolveLocalTextures(
        version,
        "particle sprite item",
        "psi",
        28,
        ParticleFields,
        source);

    public void MakeMalformed(MalformedReferencedTexture malformed) {
      switch (malformed) {
        case MalformedReferencedTexture.WrongArchiveVersion:
          version = OpenCobra.OVL.Version.One;
          break;
        case MalformedReferencedTexture.TruncatedRecord:
          source.Readable = false;
          break;
        case MalformedReferencedTexture.AddressOverflow:
          source.Loaders[0] = new OvlLoaderEntry(
            "psi", uint.MaxValue - 10, SourcePath, owner.StructAddress);
          break;
        case MalformedReferencedTexture.MissingExpectedReference:
          source.References.Remove(108);
          break;
        case MalformedReferencedTexture.WrongReferenceOwner:
          var wrongOwner = new OvlLoaderEntry("psi", 200, SourcePath, 11);
          source.References[108] = new OvlSymbolReference("ParticlePage01:tex", wrongOwner);
          break;
        case MalformedReferencedTexture.WrongReferenceTag:
          source.References[108] = new OvlSymbolReference("ParticlePage01:flic", owner);
          break;
        case MalformedReferencedTexture.MalformedReference:
          source.References[108] = new OvlSymbolReference("ParticlePage01", owner);
          break;
        case MalformedReferencedTexture.UnexpectedOwnedReference:
          source.References.Add(104, new OvlSymbolReference("Unexpected:tex", owner));
          break;
        case MalformedReferencedTexture.AmbiguousLocalTarget:
          source.AddTarget(
            "ParticlePage01", FileType.Texture, "tex", 600, "other.unique.ovl", 30);
          break;
        case MalformedReferencedTexture.MissingLocalTargetAddress:
          source.Addresses.Remove(target);
          break;
        case MalformedReferencedTexture.MissingExactTargetLoader:
          source.Loaders.RemoveAll(loader => loader.Tag == "tex");
          break;
        case MalformedReferencedTexture.AmbiguousExactTargetLoader:
          source.Loaders.Add(new OvlLoaderEntry("tex", 500, SourcePath, 30));
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed), malformed, null);
      }
    }
  }

  private sealed class FakeReferencedTextureDataSource : IReferencedTextureDataSource {
    public List<OvlLoaderEntry> Loaders { get; } = [];
    public List<OvlFile> Resources { get; } = [];
    public Dictionary<uint, OvlSymbolReference> References { get; } = [];
    public Dictionary<OvlFile, uint> Addresses { get; } = [];
    public bool Readable { get; set; } = true;

    IReadOnlyList<OvlLoaderEntry> IReferencedTextureDataSource.Loaders => Loaders;
    IReadOnlyCollection<OvlFile> IReferencedTextureDataSource.Resources => Resources;
    IReadOnlyDictionary<uint, OvlSymbolReference> IReferencedTextureDataSource.References =>
      References;

    public OvlLoaderEntry AddOwner(string tag, uint address, string path, uint structAddress) {
      var owner = new OvlLoaderEntry(tag, address, path, structAddress);
      Loaders.Add(owner);
      return owner;
    }

    public OvlFile AddTarget(
      string name,
      FileType type,
      string tag,
      uint address,
      string path,
      uint structAddress
    ) {
      var target = new OvlFile(name, type, path);
      Resources.Add(target);
      Addresses.Add(target, address);
      Loaders.Add(new OvlLoaderEntry(tag, address, path, structAddress));
      return target;
    }

    public bool CanRead(uint address, int length) => Readable;

    public bool TryGetDataPointer(OvlFile file, out uint address) =>
      Addresses.TryGetValue(file, out address);
  }
}
