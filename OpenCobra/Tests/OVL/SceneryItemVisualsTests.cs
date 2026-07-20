using System.Reflection;
using System.Text;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class SceneryItemVisualsTests {
  [Test]
  public void Decode_ReadsBaseHeaderStaticLodAndMetadata() {
    var fixture = new SceneryVisualFixture();

    var visual = fixture.Decode();
    var lod = visual.Lods.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(visual.Name, Is.EqualTo("synthetic"));
      Assert.That(visual.SerializedHeaderSize, Is.EqualTo(52));
      Assert.That(visual.Flags, Is.EqualTo((SvdFlags)0));
      Assert.That(visual.Sway, Is.EqualTo(0.2f));
      Assert.That(visual.Brightness, Is.EqualTo(0.8f));
      Assert.That(visual.Unknown4, Is.EqualTo(1.0f));
      Assert.That(visual.Scale, Is.EqualTo(0.4f));
      Assert.That(visual.Unknown6, Is.EqualTo(6));
      Assert.That(visual.Unknown11, Is.EqualTo(11));
      Assert.That(visual.ProxyRef, Is.Null);
      Assert.That(visual.WildUnknown13, Is.Null);
      Assert.That(lod.Name, Is.EqualTo("near"));
      Assert.That(lod.Type, Is.EqualTo(SvdLodType.StaticShape));
      Assert.That(lod.StaticShapeRef, Is.EqualTo("mesh:shs"));
      Assert.That(lod.Distance, Is.EqualTo(40.0f));
      Assert.That(lod.AnimationRefs, Is.Empty);
      Assert.That(lod.Billboard.Width, Is.EqualTo(1.0f));
      Assert.That(lod.Billboard.V2, Is.EqualTo(1.0f));
    }
  }

  [TestCase(HeaderVariant.Soaked, 56, null)]
  [TestCase(HeaderVariant.Wild, 60, 13u)]
  [TestCase(HeaderVariant.SoakedAndWild, 56, null)]
  public void Decode_UsesFlagSelectedHeaderLayout(
    HeaderVariant variant,
    int expectedSize,
    uint? expectedWildUnknown
  ) {
    var fixture = new SceneryVisualFixture();
    fixture.UseHeaderVariant(variant);

    var visual = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(visual.SerializedHeaderSize, Is.EqualTo(expectedSize));
      Assert.That(visual.ProxyRef, Is.EqualTo("proxy:mam"));
      Assert.That(visual.WildUnknown13, Is.EqualTo(expectedWildUnknown));
    }
  }

  [Test]
  public void Decode_ReadsBoneLodAndDoublePointerAnimationReferences() {
    var fixture = new SceneryVisualFixture();
    fixture.UseBoneLodWithAnimations();

    var lod = fixture.Decode().Lods.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(lod.Type, Is.EqualTo(SvdLodType.BoneShape));
      Assert.That(lod.StaticShapeRef, Is.Null);
      Assert.That(lod.BoneShapeRef, Is.EqualTo("skeleton:bsh"));
      Assert.That(lod.AnimationRefs,
        Is.EqualTo(new[] { "idle:ban", "wave:ban" }));
    }
  }

  [Test]
  public void Decode_ReadsBillboardLodReferencesAndCoordinates() {
    var fixture = new SceneryVisualFixture();
    fixture.UseBillboardLod();

    var lod = fixture.Decode().Lods.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(lod.Type, Is.EqualTo(SvdLodType.Billboard));
      Assert.That(lod.FlexibleTextureRef, Is.EqualTo("leaves:ftx"));
      Assert.That(lod.TextureStyleRef, Is.EqualTo("BillboardStandard:txs"));
      Assert.That(lod.Billboard,
        Is.EqualTo(new SceneryVisualBillboardSettings(2, 3, 0.1f, 0.2f, 0.8f, 0.9f)));
    }
  }

  [Test]
  public void Decode_ReadsLodNameAtRelocatedVirtualAddressZero() {
    var fixture = new SceneryVisualFixture();
    fixture.UseNameAtFirstVirtualAddress();

    var lod = fixture.Decode().Lods.Single();

    Assert.That(lod.Name, Is.EqualTo("near"));
  }

  [Test]
  public void Decode_RejectsRelocatedZeroNameWithoutStringData() {
    var fixture = new SceneryVisualFixture();
    fixture.UseNameAtFirstVirtualAddress();
    fixture.RemoveFirstVirtualAddressBlock();

    var exception = Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));

    Assert.That(exception!.Message,
      Does.Contain("LOD 0 name is missing, unterminated, or exceeds 4096 bytes"));
  }

  [Test]
  public void Decode_RejectsStoredNameThatDiffersFromZeroRelocationTarget() {
    var fixture = new SceneryVisualFixture();
    fixture.UseMismatchedZeroNameRelocation();

    var exception = Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));

    Assert.That(exception!.Message,
      Does.Contain("LOD 0 name does not match its relocation target"));
  }

  [TestCase(MalformedVisual.TruncatedHeader)]
  [TestCase(MalformedVisual.SoakedHeaderWithoutExtension)]
  [TestCase(MalformedVisual.OversizedLodCount)]
  [TestCase(MalformedVisual.TruncatedLodPointerArray)]
  [TestCase(MalformedVisual.MissingLodArrayRelocation)]
  [TestCase(MalformedVisual.DanglingLodArrayRelocation)]
  [TestCase(MalformedVisual.MissingLodRecordRelocation)]
  [TestCase(MalformedVisual.TruncatedLodRecord)]
  [TestCase(MalformedVisual.MissingNameRelocation)]
  [TestCase(MalformedVisual.UnterminatedName)]
  [TestCase(MalformedVisual.UnknownLodType)]
  [TestCase(MalformedVisual.NonFiniteHeaderFloat)]
  [TestCase(MalformedVisual.NonFiniteLodFloat)]
  [TestCase(MalformedVisual.WrongSymbolReferenceOwner)]
  [TestCase(MalformedVisual.WrongSymbolReferenceType)]
  [TestCase(MalformedVisual.MissingStaticReference)]
  [TestCase(MalformedVisual.ConflictingRawAndSymbolReference)]
  [TestCase(MalformedVisual.OversizedAnimationCount)]
  [TestCase(MalformedVisual.MissingAnimationArrayRelocation)]
  [TestCase(MalformedVisual.TruncatedAnimationPointerArray)]
  [TestCase(MalformedVisual.DanglingAnimationReferencePointer)]
  [TestCase(MalformedVisual.WrongAnimationReferenceType)]
  [TestCase(MalformedVisual.ZeroAnimationCountWithPointer)]
  [TestCase(MalformedVisual.WrongLoaderType)]
  public void Decode_RejectsMalformedOrUnboundedData(MalformedVisual malformed) {
    var fixture = new SceneryVisualFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  public void Extract_FromEmbeddedCustomOvls_DecodesVisualsAndExactReferences() {
    var assembly = Assembly.GetExecutingAssembly();
    var resources = assembly.GetManifestResourceNames();
    var commonResources = resources.Where(name => name.EndsWith(".common.ovl")).ToList();
    var visuals = new List<SceneryItemVisual>();

    foreach (var commonResource in commonResources) {
      var uniqueResource = commonResource[..^".common.ovl".Length] + ".unique.ovl";
      var tempDir = Directory.CreateTempSubdirectory().FullName;
      try {
        var commonPath = Path.Combine(tempDir, "fixture.common.ovl");
        CopyResource(assembly, commonResource, commonPath);
        if (resources.Contains(uniqueResource))
          CopyResource(assembly, uniqueResource, Path.Combine(tempDir, "fixture.unique.ovl"));
        using var ovl = Ovl.Load(commonPath);
        visuals.AddRange(SceneryItemVisuals.Extract(ovl));
      } finally {
        Directory.Delete(tempDir, recursive: true);
      }
    }

    var townHall = visuals.Single(visual => visual.Name == "RS-TownHall");
    var skyBeam = visuals.Single(visual => visual.Name == "ZodiSkyBeam");
    TestContext.Progress.WriteLine(
      $"Custom SVD evidence: visuals={visuals.Count}, lods={visuals.Sum(item => item.Lods.Count)}, " +
      $"animations={visuals.Sum(item => item.Lods.Sum(lod => lod.AnimationRefs.Count))}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(visuals, Has.Count.EqualTo(8));
      Assert.That(visuals.Sum(visual => visual.Lods.Count), Is.EqualTo(10));
      Assert.That(townHall.Lods.Select(lod => lod.StaticShapeRef),
        Is.All.EqualTo("RS-TownHall:shs"));
      Assert.That(townHall.Lods.Select(lod => lod.Distance),
        Is.EqualTo(new[] { 40.0f, 100.0f, 4000.0f }));
      Assert.That(skyBeam.Lods.Single().BoneShapeRef, Is.EqualTo("ZodiSkyBeam:bsh"));
      Assert.That(skyBeam.Lods.Single().AnimationRefs,
        Is.EqualTo(new[] {
          "ZodiAnim1Idle:ban",
          "ZodiAnim2Start:ban",
          "ZodiAnim3Loop:ban",
          "ZodiAnim4Stop:ban"
        }));
    }
  }

  private static void CopyResource(Assembly assembly, string resourceName, string path) {
    using var input = assembly.GetManifestResourceStream(resourceName);
    Assert.That(input, Is.Not.Null, $"Embedded resource '{resourceName}' not found.");
    using var output = File.Create(path);
    input.CopyTo(output);
  }

  public enum HeaderVariant {
    Soaked,
    Wild,
    SoakedAndWild
  }

  public enum MalformedVisual {
    TruncatedHeader,
    SoakedHeaderWithoutExtension,
    OversizedLodCount,
    TruncatedLodPointerArray,
    MissingLodArrayRelocation,
    DanglingLodArrayRelocation,
    MissingLodRecordRelocation,
    TruncatedLodRecord,
    MissingNameRelocation,
    UnterminatedName,
    UnknownLodType,
    NonFiniteHeaderFloat,
    NonFiniteLodFloat,
    WrongSymbolReferenceOwner,
    WrongSymbolReferenceType,
    MissingStaticReference,
    ConflictingRawAndSymbolReference,
    OversizedAnimationCount,
    MissingAnimationArrayRelocation,
    TruncatedAnimationPointerArray,
    DanglingAnimationReferencePointer,
    WrongAnimationReferenceType,
    ZeroAnimationCountWithPointer,
    WrongLoaderType
  }

  private sealed class SceneryVisualFixture {
    private const uint HeaderAddress = 100;
    private const uint LodPointersAddress = 200;
    private const uint LodAddress = 300;
    private const uint NameAddress = 1000;
    private const uint AnimationPointersAddress = 1200;
    private const uint FirstAnimationReferenceAddress = 1300;
    private const uint SecondAnimationReferenceAddress = 1310;

    private readonly FakeSceneryVisualDataSource source = new();
    private OvlLoaderEntry owner = new("svd", HeaderAddress, "fixture.unique.ovl", 40);

    public SceneryVisualFixture() {
      var header = source.AddBlock(HeaderAddress, 52);
      WriteSingle(header, 4, 0.2f);
      WriteSingle(header, 8, 0.8f);
      WriteSingle(header, 12, 1.0f);
      WriteSingle(header, 16, 0.4f);
      WriteUInt32(header, 20, 1);
      WritePointer(header, HeaderAddress, 24, LodPointersAddress);
      foreach (var index in Enumerable.Range(0, 6))
        WriteUInt32(header, 28 + index * sizeof(uint), Convert.ToUInt32(index + 6));

      var pointers = source.AddBlock(LodPointersAddress, sizeof(uint));
      WritePointer(pointers, LodPointersAddress, 0, LodAddress);
      var lod = source.AddBlock(LodAddress, 72);
      WriteUInt32(lod, 0, Convert.ToUInt32(SvdLodType.StaticShape));
      WritePointer(lod, LodAddress, 4, NameAddress);
      WriteUInt32(lod, 12, 2);
      WriteUInt32(lod, 20, 4);
      WriteSingle(lod, 32, 1);
      WriteSingle(lod, 36, 1);
      WriteSingle(lod, 40, 0);
      WriteSingle(lod, 44, 1);
      WriteSingle(lod, 48, 0);
      WriteSingle(lod, 52, 1);
      WriteSingle(lod, 56, 40);
      WriteUInt32(lod, 64, 14);
      source.AddBlock(NameAddress, Encoding.ASCII.GetBytes("near\0"));
      source.AddReference(LodAddress + 8, "mesh:shs", owner, "shs");
    }

    public SceneryItemVisual Decode() =>
      SceneryItemVisuals.Decode("synthetic", owner, source);

    public void UseHeaderVariant(HeaderVariant variant) {
      var size = variant == HeaderVariant.Wild ? 60 : 56;
      var header = ResizeBlock(HeaderAddress, size);
      var flags = variant switch {
        HeaderVariant.Soaked => SvdFlags.Soaked,
        HeaderVariant.Wild => SvdFlags.Wild,
        HeaderVariant.SoakedAndWild => SvdFlags.SoakedOrWild,
        _ => throw new ArgumentOutOfRangeException(nameof(variant))
      };
      WriteUInt32(header, 0, Convert.ToUInt32(flags));
      if (variant == HeaderVariant.Wild) WriteUInt32(header, 56, 13);
      source.AddReference(HeaderAddress + 52, "proxy:mam", owner, "mam");
    }

    public void UseBoneLodWithAnimations() {
      var lod = source.Blocks[LodAddress];
      WriteUInt32(lod, 0, Convert.ToUInt32(SvdLodType.BoneShape));
      source.ResourceReferences.Remove(LodAddress + 8);
      source.AddReference(LodAddress + 16, "skeleton:bsh", owner, "bsh");
      WriteUInt32(lod, 60, 2);
      WritePointer(lod, LodAddress, 68, AnimationPointersAddress);

      var pointers = source.AddBlock(AnimationPointersAddress, sizeof(uint) * 2);
      WritePointer(pointers, AnimationPointersAddress, 0, FirstAnimationReferenceAddress);
      WritePointer(pointers, AnimationPointersAddress, 4, SecondAnimationReferenceAddress);
      source.AddBlock(FirstAnimationReferenceAddress, sizeof(uint));
      source.AddBlock(SecondAnimationReferenceAddress, sizeof(uint));
      source.AddReference(FirstAnimationReferenceAddress, "idle:ban", owner, "ban");
      source.AddReference(SecondAnimationReferenceAddress, "wave:ban", owner, "ban");
    }

    public void UseBillboardLod() {
      var lod = source.Blocks[LodAddress];
      WriteUInt32(lod, 0, Convert.ToUInt32(SvdLodType.Billboard));
      source.ResourceReferences.Remove(LodAddress + 8);
      source.AddReference(LodAddress + 24, "leaves:ftx", owner, "ftx");
      source.AddReference(
        LodAddress + 28, "BillboardStandard:txs", owner, "txs");
      WriteSingle(lod, 32, 2);
      WriteSingle(lod, 36, 3);
      WriteSingle(lod, 40, 0.1f);
      WriteSingle(lod, 44, 0.2f);
      WriteSingle(lod, 48, 0.8f);
      WriteSingle(lod, 52, 0.9f);
    }

    public void UseNameAtFirstVirtualAddress() {
      var lod = source.Blocks[LodAddress];
      source.Blocks.Remove(NameAddress);
      source.AddBlock(0, Encoding.ASCII.GetBytes("near\0"));
      WritePointer(lod, LodAddress, 4, 0);
    }

    public void RemoveFirstVirtualAddressBlock() => source.Blocks.Remove(0);

    public void UseMismatchedZeroNameRelocation() {
      var lod = source.Blocks[LodAddress];
      source.Relocations[LodAddress + 4] = 0;
      source.AddBlock(0, Encoding.ASCII.GetBytes("near\0"));
      WriteUInt32(lod, 4, NameAddress);
    }

    public void MakeMalformed(MalformedVisual malformed) {
      var header = source.Blocks[HeaderAddress];
      var lod = source.Blocks[LodAddress];
      switch (malformed) {
        case MalformedVisual.TruncatedHeader:
          source.ReplaceBlock(HeaderAddress, header[..51]);
          break;
        case MalformedVisual.SoakedHeaderWithoutExtension:
          WriteUInt32(header, 0, Convert.ToUInt32(SvdFlags.Soaked));
          break;
        case MalformedVisual.OversizedLodCount:
          WriteUInt32(header, 20, 16 * 1024 + 1);
          break;
        case MalformedVisual.TruncatedLodPointerArray:
          WriteUInt32(header, 20, 2);
          break;
        case MalformedVisual.MissingLodArrayRelocation:
          source.Relocations.Remove(HeaderAddress + 24);
          break;
        case MalformedVisual.DanglingLodArrayRelocation:
          WritePointer(header, HeaderAddress, 24, 9000);
          break;
        case MalformedVisual.MissingLodRecordRelocation:
          source.Relocations.Remove(LodPointersAddress);
          break;
        case MalformedVisual.TruncatedLodRecord:
          source.ReplaceBlock(LodAddress, lod[..71]);
          break;
        case MalformedVisual.MissingNameRelocation:
          source.Relocations.Remove(LodAddress + 4);
          break;
        case MalformedVisual.UnterminatedName:
          source.ReplaceBlock(NameAddress,
            Enumerable.Repeat(Convert.ToByte('x'), 4 * 1024).ToArray());
          break;
        case MalformedVisual.UnknownLodType:
          WriteUInt32(lod, 0, 2);
          break;
        case MalformedVisual.NonFiniteHeaderFloat:
          WriteSingle(header, 4, float.NaN);
          break;
        case MalformedVisual.NonFiniteLodFloat:
          WriteSingle(lod, 56, float.PositiveInfinity);
          break;
        case MalformedVisual.WrongSymbolReferenceOwner:
          source.ResourceReferences[LodAddress + 8] =
            new OvlSymbolReference("mesh:shs", owner with { StructAddress = 60 });
          break;
        case MalformedVisual.WrongSymbolReferenceType:
          source.ResourceReferences[LodAddress + 8] =
            new OvlSymbolReference("mesh:bsh", owner);
          break;
        case MalformedVisual.MissingStaticReference:
          source.ResourceReferences.Remove(LodAddress + 8);
          break;
        case MalformedVisual.ConflictingRawAndSymbolReference:
          WriteUInt32(lod, 8, 999);
          break;
        case MalformedVisual.OversizedAnimationCount:
          WriteUInt32(lod, 60, 64 * 1024 + 1);
          break;
        case MalformedVisual.MissingAnimationArrayRelocation:
          UseBoneLodWithAnimations();
          source.Relocations.Remove(LodAddress + 68);
          break;
        case MalformedVisual.TruncatedAnimationPointerArray:
          UseBoneLodWithAnimations();
          source.ReplaceBlock(AnimationPointersAddress,
            source.Blocks[AnimationPointersAddress][..sizeof(uint)]);
          break;
        case MalformedVisual.DanglingAnimationReferencePointer:
          UseBoneLodWithAnimations();
          WritePointer(
            source.Blocks[AnimationPointersAddress], AnimationPointersAddress, 0, 9000);
          break;
        case MalformedVisual.WrongAnimationReferenceType:
          UseBoneLodWithAnimations();
          source.ResourceReferences[FirstAnimationReferenceAddress] =
            new OvlSymbolReference("idle:shs", owner);
          break;
        case MalformedVisual.ZeroAnimationCountWithPointer:
          WritePointer(lod, LodAddress, 68, AnimationPointersAddress);
          source.AddBlock(AnimationPointersAddress, sizeof(uint));
          break;
        case MalformedVisual.WrongLoaderType:
          owner = owner with { Tag = "shs" };
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed), malformed, null);
      }
    }

    private byte[] ResizeBlock(uint address, int length) {
      var resized = new byte[length];
      source.Blocks[address].CopyTo(resized, 0);
      source.ReplaceBlock(address, resized);
      return resized;
    }

    private void WritePointer(byte[] bytes, uint address, int offset, uint value) {
      WriteUInt32(bytes, offset, value);
      source.Relocations[address + Convert.ToUInt32(offset)] = value;
    }
  }

  private sealed class FakeSceneryVisualDataSource : ISceneryVisualDataSource {
    public Dictionary<uint, byte[]> Blocks { get; } = [];
    public Dictionary<uint, uint> Relocations { get; } = [];
    public Dictionary<uint, OvlSymbolReference> ResourceReferences { get; } = [];
    public Dictionary<string, SceneryVisualResourceMetadata> ResourcesByKey { get; } =
      new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> LocalResourceKeys { get; } =
      new(StringComparer.OrdinalIgnoreCase);

    IReadOnlyDictionary<uint, OvlSymbolReference>
      ISceneryVisualDataSource.ResourceReferences => ResourceReferences;
    IReadOnlyDictionary<string, SceneryVisualResourceMetadata>
      ISceneryVisualDataSource.ResourcesByKey => ResourcesByKey;
    IReadOnlySet<string> ISceneryVisualDataSource.LocalResourceKeys => LocalResourceKeys;

    public byte[] AddBlock(uint address, int length) {
      var bytes = new byte[length];
      Blocks.Add(address, bytes);
      return bytes;
    }

    public void AddBlock(uint address, byte[] bytes) => Blocks.Add(address, bytes);
    public void ReplaceBlock(uint address, byte[] bytes) => Blocks[address] = bytes;

    public void AddReference(
      uint fieldAddress,
      string symbol,
      OvlLoaderEntry owner,
      string tag
    ) {
      ResourceReferences.Add(fieldAddress, new OvlSymbolReference(symbol, owner));
      LocalResourceKeys.Add(symbol);
      ResourcesByKey[symbol] = new SceneryVisualResourceMetadata(symbol, tag);
    }

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      foreach (var block in Blocks) {
        if (address < block.Key) continue;
        var offset = Convert.ToUInt64(address - block.Key);
        if (offset + Convert.ToUInt64(length) > Convert.ToUInt64(block.Value.Length)) continue;
        bytes = block.Value.AsSpan(Convert.ToInt32(offset), length).ToArray();
        return true;
      }
      bytes = [];
      return false;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      Relocations.TryGetValue(address, out value);

    public bool TryGetNullTerminatedStringByteLength(
      uint address,
      int maximumLength,
      out int length
    ) {
      foreach (var block in Blocks) {
        if (address < block.Key || address >= block.Key + block.Value.Length) continue;
        var offset = Convert.ToInt32(address - block.Key);
        var available = Math.Min(maximumLength, block.Value.Length - offset);
        var end = Array.IndexOf(block.Value, Convert.ToByte(0), offset, available);
        length = end < 0 ? 0 : end - offset;
        return end >= 0;
      }
      length = 0;
      return false;
    }

    public bool TryReadNullTerminatedString(
      uint address,
      int maximumLength,
      out string value
    ) {
      foreach (var block in Blocks) {
        if (address < block.Key || address >= block.Key + block.Value.Length) continue;
        var offset = Convert.ToInt32(address - block.Key);
        var available = Math.Min(maximumLength, block.Value.Length - offset);
        var end = Array.IndexOf(block.Value, Convert.ToByte(0), offset, available);
        if (end < 0) break;
        value = Encoding.ASCII.GetString(block.Value, offset, end - offset);
        return true;
      }
      value = string.Empty;
      return false;
    }
  }

  private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
    BitConverter.GetBytes(value).CopyTo(bytes, offset);

  private static void WriteSingle(byte[] bytes, int offset, float value) =>
    BitConverter.GetBytes(value).CopyTo(bytes, offset);
}
