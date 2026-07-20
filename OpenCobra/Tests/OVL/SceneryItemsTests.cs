using System.Reflection;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class SceneryItemsTests {
  [Test]
  public void Decode_ReadsPlacementMetadataAndOrderedTaggedVisualReferences() {
    var fixture = new SceneryItemFixture();

    var item = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(item.Name, Is.EqualTo("synthetic"));
      Assert.That(item.SerializedHeaderSize, Is.EqualTo(212));
      Assert.That(item.Flags, Is.EqualTo(SidFlags.GroundChange | SidFlags.Skew));
      Assert.That(item.PositionType, Is.EqualTo(SidPosition.TileHalf));
      Assert.That(item.StructureVersion, Is.Zero);
      Assert.That(item.SquaresX, Is.EqualTo(2));
      Assert.That(item.SquaresZ, Is.EqualTo(3));
      Assert.That(item.PositionX, Is.EqualTo(1.25f));
      Assert.That(item.PositionY, Is.EqualTo(-2.5f));
      Assert.That(item.PositionZ, Is.EqualTo(3.75f));
      Assert.That(item.SizeX, Is.EqualTo(4.0f));
      Assert.That(item.SizeY, Is.EqualTo(8.0f));
      Assert.That(item.SizeZ, Is.EqualTo(12.0f));
      Assert.That(item.Type, Is.EqualTo(SidType.SceneryLarge));
      Assert.That(item.VisualRefs,
        Is.EqualTo(new[] { "near:svd", "far:svd" }));
    }
  }

  [TestCase(0, 212)]
  [TestCase(1, 228)]
  [TestCase(2, 236)]
  public void Decode_UsesStructureVersionSelectedHeaderSize(
    int version,
    int expectedSize
  ) {
    var fixture = new SceneryItemFixture();
    fixture.UseStructureVersion(Convert.ToUInt16(version), expectedSize);

    var item = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(item.StructureVersion, Is.EqualTo(Convert.ToUInt16(version)));
      Assert.That(item.SerializedHeaderSize, Is.EqualTo(expectedSize));
      Assert.That(item.VisualRefs,
        Is.EqualTo(new[] { "near:svd", "far:svd" }));
    }
  }

  [Test]
  public void Decode_VersionOneLegacyLayout_ReadsExactFieldsAndZerosAbsentMetadata() {
    var fixture = new SceneryItemFixture();
    fixture.UseLegacyV1Layout();

    var item = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(item.SerializedHeaderSize, Is.EqualTo(164));
      Assert.That(item.Flags, Is.EqualTo(SidFlags.GroundChange | SidFlags.Skew));
      Assert.That(item.PositionType, Is.EqualTo(SidPosition.TileHalf));
      Assert.That(item.StructureVersion, Is.Zero);
      Assert.That(item.SquaresX, Is.EqualTo(2));
      Assert.That(item.SquaresZ, Is.EqualTo(3));
      Assert.That(item.PositionX, Is.Zero);
      Assert.That(item.PositionY, Is.Zero);
      Assert.That(item.PositionZ, Is.Zero);
      Assert.That(item.SizeX, Is.Zero);
      Assert.That(item.SizeY, Is.Zero);
      Assert.That(item.SizeZ, Is.Zero);
      Assert.That(item.Type, Is.EqualTo(SidType.SceneryLarge));
      Assert.That(item.VisualRefs,
        Is.EqualTo(new[] { "near:svd", "far:svd" }));
    }
  }

  [Test]
  public void Decode_VersionOneRevisedLayout_UsesRelocatedPointerDiscriminator() {
    var fixture = new SceneryItemFixture();
    fixture.UseRevisedV1Layout();

    var item = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(item.SerializedHeaderSize, Is.EqualTo(212));
      Assert.That(item.PositionX, Is.EqualTo(1.25f));
      Assert.That(item.Type, Is.EqualTo(SidType.SceneryLarge));
      Assert.That(item.VisualRefs,
        Is.EqualTo(new[] { "near:svd", "far:svd" }));
    }
  }

  [TestCase(VersionOneLayoutMutation.BothCandidatePointers)]
  [TestCase(VersionOneLayoutMutation.NeitherCandidatePointer)]
  public void Decode_VersionOneRejectsAmbiguousOrMissingLayoutEvidence(
    VersionOneLayoutMutation mutation
  ) {
    var fixture = new SceneryItemFixture();
    fixture.MakeVersionOneLayoutMalformed(mutation);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [TestCase(MalformedItem.TruncatedHeader)]
  [TestCase(MalformedItem.TruncatedVersionedHeader)]
  [TestCase(MalformedItem.UnknownStructureVersion)]
  [TestCase(MalformedItem.UnknownPositionType)]
  [TestCase(MalformedItem.UnknownItemType)]
  [TestCase(MalformedItem.ZeroFootprintDimension)]
  [TestCase(MalformedItem.OversizedFootprintDimension)]
  [TestCase(MalformedItem.OversizedFootprintArea)]
  [TestCase(MalformedItem.ZeroVisualCount)]
  [TestCase(MalformedItem.OversizedVisualCount)]
  [TestCase(MalformedItem.MissingVisualArrayRelocation)]
  [TestCase(MalformedItem.MismatchedVisualArrayRelocation)]
  [TestCase(MalformedItem.DanglingVisualArrayPointer)]
  [TestCase(MalformedItem.OverlappingVisualArrayPointer)]
  [TestCase(MalformedItem.OverflowingVisualArrayPointer)]
  [TestCase(MalformedItem.TruncatedVisualArray)]
  [TestCase(MalformedItem.NonzeroRawVisualReference)]
  [TestCase(MalformedItem.RelocatedVisualReference)]
  [TestCase(MalformedItem.MissingVisualReference)]
  [TestCase(MalformedItem.WrongVisualReferenceOwner)]
  [TestCase(MalformedItem.WrongVisualReferenceType)]
  [TestCase(MalformedItem.ExtraOwnedVisualReference)]
  [TestCase(MalformedItem.NonFinitePlacementFloat)]
  [TestCase(MalformedItem.WrongLoaderType)]
  public void Decode_RejectsMalformedOrUnboundedData(MalformedItem malformed) {
    var fixture = new SceneryItemFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  public void Decode_RejectsAggregateByteBudgetOverflow() {
    var fixture = new SceneryItemFixture();

    Assert.Throws<InvalidDataException>(new Action(() =>
      fixture.Decode(new SceneryItemDecodeLimits(219, 10))));
  }

  [Test]
  public void Extract_FromEmbeddedCustomOvls_DecodesPlacementAndSvdLinks() {
    var assembly = Assembly.GetExecutingAssembly();
    var resources = assembly.GetManifestResourceNames();
    var commonResources = resources.Where(name => name.EndsWith(".common.ovl")).ToList();
    var items = new List<SceneryItem>();

    foreach (var commonResource in commonResources) {
      var uniqueResource = commonResource[..^".common.ovl".Length] + ".unique.ovl";
      var tempDir = Directory.CreateTempSubdirectory().FullName;
      try {
        var commonPath = Path.Combine(tempDir, "fixture.common.ovl");
        CopyResource(assembly, commonResource, commonPath);
        if (resources.Contains(uniqueResource))
          CopyResource(assembly, uniqueResource, Path.Combine(tempDir, "fixture.unique.ovl"));
        using var ovl = Ovl.Load(commonPath);
        items.AddRange(SceneryItems.Extract(ovl));
      } finally {
        Directory.Delete(tempDir, recursive: true);
      }
    }

    var townHall = items.Single(item => item.Name == "RS-TownHall");
    var skyBeam = items.Single(item => item.Name == "ZodiSkyBeam");
    TestContext.Progress.WriteLine(
      $"Custom SID evidence: items={items.Count}, " +
      $"visualRefs={items.Sum(item => item.VisualRefs.Count)}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(items, Has.Count.EqualTo(130));
      Assert.That(items.Sum(item => item.VisualRefs.Count), Is.EqualTo(130));
      Assert.That(items.SelectMany(item => item.VisualRefs),
        Is.All.EndsWith(":svd"));
      Assert.That(items.Select(item => item.SerializedHeaderSize),
        Is.All.EqualTo(212));
      Assert.That(townHall.PositionType, Is.EqualTo(SidPosition.TileFull));
      Assert.That(townHall.SquaresX, Is.EqualTo(1));
      Assert.That(townHall.SquaresZ, Is.EqualTo(1));
      Assert.That(townHall.SizeX, Is.EqualTo(4.0f));
      Assert.That(townHall.VisualRefs, Is.EqualTo(new[] { "RS-TownHall:svd" }));
      Assert.That(skyBeam.SquaresX, Is.EqualTo(5));
      Assert.That(skyBeam.SquaresZ, Is.EqualTo(5));
      Assert.That(skyBeam.Type, Is.EqualTo(SidType.Ride));
      Assert.That(skyBeam.VisualRefs, Is.EqualTo(new[] { "ZodiSkyBeam:svd" }));
    }
  }

  private static void CopyResource(Assembly assembly, string resourceName, string path) {
    using var input = assembly.GetManifestResourceStream(resourceName);
    Assert.That(input, Is.Not.Null, $"Embedded resource '{resourceName}' not found.");
    using var output = File.Create(path);
    input.CopyTo(output);
  }

  public enum MalformedItem {
    TruncatedHeader,
    TruncatedVersionedHeader,
    UnknownStructureVersion,
    UnknownPositionType,
    UnknownItemType,
    ZeroFootprintDimension,
    OversizedFootprintDimension,
    OversizedFootprintArea,
    ZeroVisualCount,
    OversizedVisualCount,
    MissingVisualArrayRelocation,
    MismatchedVisualArrayRelocation,
    DanglingVisualArrayPointer,
    OverlappingVisualArrayPointer,
    OverflowingVisualArrayPointer,
    TruncatedVisualArray,
    NonzeroRawVisualReference,
    RelocatedVisualReference,
    MissingVisualReference,
    WrongVisualReferenceOwner,
    WrongVisualReferenceType,
    ExtraOwnedVisualReference,
    NonFinitePlacementFloat,
    WrongLoaderType
  }

  public enum VersionOneLayoutMutation {
    BothCandidatePointers,
    NeitherCandidatePointer
  }

  private sealed class SceneryItemFixture {
    private const uint HeaderAddress = 100;
    private const uint VisualReferencesAddress = 1000;
    private readonly FakeSceneryItemDataSource source = new();
    private OpenCobra.OVL.Version archiveVersion = OpenCobra.OVL.Version.Five;
    private OvlLoaderEntry owner = new("sid", HeaderAddress, "fixture.unique.ovl", 40);

    public SceneryItemFixture() {
      var header = source.AddBlock(HeaderAddress, 212);
      WriteUInt32(header, 4,
        Convert.ToUInt32(SidFlags.GroundChange | SidFlags.Skew));
      WriteUInt16(header, 8, Convert.ToUInt16(SidPosition.TileHalf));
      WriteUInt16(header, 10, 0);
      WriteUInt32(header, 16, 2);
      WriteUInt32(header, 20, 3);
      WriteSingle(header, 28, 1.25f);
      WriteSingle(header, 32, -2.5f);
      WriteSingle(header, 36, 3.75f);
      WriteSingle(header, 40, 4.0f);
      WriteSingle(header, 44, 8.0f);
      WriteSingle(header, 48, 12.0f);
      WriteUInt32(header, 72, Convert.ToUInt32(SidType.SceneryLarge));
      WriteUInt32(header, 80, 2);
      WritePointer(header, HeaderAddress, 84, VisualReferencesAddress);

      source.AddBlock(VisualReferencesAddress, sizeof(uint) * 2);
      source.ResourceReferences.Add(
        VisualReferencesAddress,
        new OvlSymbolReference("near:svd", owner));
      source.ResourceReferences.Add(
        VisualReferencesAddress + sizeof(uint),
        new OvlSymbolReference("far:svd", owner));
    }

    public SceneryItem Decode() =>
      SceneryItems.Decode("synthetic", archiveVersion, owner, source);

    public SceneryItem Decode(SceneryItemDecodeLimits limits) =>
      SceneryItems.Decode("synthetic", archiveVersion, owner, source, limits);

    public void UseStructureVersion(ushort version, int headerSize) {
      var header = ResizeBlock(HeaderAddress, headerSize);
      WriteUInt16(header, 10, version);
    }

    public void UseLegacyV1Layout() {
      archiveVersion = OpenCobra.OVL.Version.One;
      var header = new byte[164];
      WriteUInt32(header, 4,
        Convert.ToUInt32(SidFlags.GroundChange | SidFlags.Skew));
      WriteUInt32(header, 8, Convert.ToUInt32(SidPosition.TileHalf));
      WriteUInt32(header, 16, 2);
      WriteUInt32(header, 20, 3);
      WriteUInt32(header, 48, Convert.ToUInt32(SidType.SceneryLarge));
      WriteUInt32(header, 56, 2);
      WritePointer(header, HeaderAddress, 60, VisualReferencesAddress);
      source.Relocations.Remove(HeaderAddress + 84);
      source.ReplaceBlock(HeaderAddress, header);
    }

    public void UseRevisedV1Layout() => archiveVersion = OpenCobra.OVL.Version.One;

    public void MakeVersionOneLayoutMalformed(VersionOneLayoutMutation mutation) {
      UseLegacyV1Layout();
      switch (mutation) {
        case VersionOneLayoutMutation.BothCandidatePointers:
          source.Relocations[HeaderAddress + 84] = 0;
          break;
        case VersionOneLayoutMutation.NeitherCandidatePointer:
          source.Relocations.Remove(HeaderAddress + 60);
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
      }
    }

    public void MakeMalformed(MalformedItem malformed) {
      var header = source.Blocks[HeaderAddress];
      var visualRefs = source.Blocks[VisualReferencesAddress];
      switch (malformed) {
        case MalformedItem.TruncatedHeader:
          source.ReplaceBlock(HeaderAddress, header[..211]);
          break;
        case MalformedItem.TruncatedVersionedHeader:
          WriteUInt16(header, 10, 1);
          source.ReplaceBlock(HeaderAddress, Resize(header, 227));
          break;
        case MalformedItem.UnknownStructureVersion:
          WriteUInt16(header, 10, 3);
          break;
        case MalformedItem.UnknownPositionType:
          WriteUInt16(header, 8, 9);
          break;
        case MalformedItem.UnknownItemType:
          WriteUInt32(header, 72, 47);
          break;
        case MalformedItem.ZeroFootprintDimension:
          WriteUInt32(header, 16, 0);
          break;
        case MalformedItem.OversizedFootprintDimension:
          WriteUInt32(header, 16, 4 * 1024 + 1);
          break;
        case MalformedItem.OversizedFootprintArea:
          WriteUInt32(header, 16, 2000);
          WriteUInt32(header, 20, 2000);
          break;
        case MalformedItem.ZeroVisualCount:
          WriteUInt32(header, 80, 0);
          break;
        case MalformedItem.OversizedVisualCount:
          WriteUInt32(header, 80, 64 * 1024 + 1);
          break;
        case MalformedItem.MissingVisualArrayRelocation:
          source.Relocations.Remove(HeaderAddress + 84);
          break;
        case MalformedItem.MismatchedVisualArrayRelocation:
          source.Relocations[HeaderAddress + 84] = VisualReferencesAddress + 100;
          break;
        case MalformedItem.DanglingVisualArrayPointer:
          WritePointer(header, HeaderAddress, 84, 9000);
          break;
        case MalformedItem.OverlappingVisualArrayPointer:
          WritePointer(header, HeaderAddress, 84, HeaderAddress + 100);
          break;
        case MalformedItem.OverflowingVisualArrayPointer:
          WritePointer(header, HeaderAddress, 84, uint.MaxValue - 1);
          break;
        case MalformedItem.TruncatedVisualArray:
          WriteUInt32(header, 80, 3);
          break;
        case MalformedItem.NonzeroRawVisualReference:
          WriteUInt32(visualRefs, 0, 123);
          break;
        case MalformedItem.RelocatedVisualReference:
          source.Relocations[VisualReferencesAddress] = 123;
          break;
        case MalformedItem.MissingVisualReference:
          source.ResourceReferences.Remove(VisualReferencesAddress);
          break;
        case MalformedItem.WrongVisualReferenceOwner:
          source.ResourceReferences[VisualReferencesAddress] =
            new OvlSymbolReference("near:svd", owner with { StructAddress = 44 });
          break;
        case MalformedItem.WrongVisualReferenceType:
          source.ResourceReferences[VisualReferencesAddress] =
            new OvlSymbolReference("near:shs", owner);
          break;
        case MalformedItem.ExtraOwnedVisualReference:
          source.ResourceReferences[VisualReferencesAddress + 8] =
            new OvlSymbolReference("extra:svd", owner);
          break;
        case MalformedItem.NonFinitePlacementFloat:
          WriteSingle(header, 28, float.NaN);
          break;
        case MalformedItem.WrongLoaderType:
          owner = owner with { Tag = "svd" };
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed), malformed, null);
      }
    }

    private byte[] ResizeBlock(uint address, int length) {
      var resized = Resize(source.Blocks[address], length);
      source.ReplaceBlock(address, resized);
      return resized;
    }

    private static byte[] Resize(byte[] bytes, int length) {
      var resized = new byte[length];
      bytes.AsSpan(0, Math.Min(bytes.Length, length)).CopyTo(resized);
      return resized;
    }

    private void WritePointer(byte[] bytes, uint address, int offset, uint value) {
      WriteUInt32(bytes, offset, value);
      source.Relocations[address + Convert.ToUInt32(offset)] = value;
    }
  }

  private sealed class FakeSceneryItemDataSource : ISceneryItemDataSource {
    public Dictionary<uint, byte[]> Blocks { get; } = [];
    public Dictionary<uint, uint> Relocations { get; } = [];
    public Dictionary<uint, OvlSymbolReference> ResourceReferences { get; } = [];

    IReadOnlyDictionary<uint, OvlSymbolReference>
      ISceneryItemDataSource.ResourceReferences => ResourceReferences;

    public byte[] AddBlock(uint address, int length) {
      var bytes = new byte[length];
      Blocks.Add(address, bytes);
      return bytes;
    }

    public void ReplaceBlock(uint address, byte[] bytes) => Blocks[address] = bytes;

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
  }

  private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
    BitConverter.GetBytes(value).CopyTo(bytes, offset);

  private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
    BitConverter.GetBytes(value).CopyTo(bytes, offset);

  private static void WriteSingle(byte[] bytes, int offset, float value) =>
    BitConverter.GetBytes(value).CopyTo(bytes, offset);
}
