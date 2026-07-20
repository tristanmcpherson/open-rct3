using System.Text;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class PathTypesTests {
  [Test]
  public void Decode_PreservesTexturesShapeOwnersAndResearch() {
    var path = new PathTypeFixture().Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(path.Name, Is.EqualTo("synthetic"));
      Assert.That(path.Flags, Is.EqualTo(1));
      Assert.That(path.InternalName, Is.EqualTo("Asphalt"));
      Assert.That(path.DisplayNameRef, Is.EqualTo("AsphaltName:txt"));
      Assert.That(path.IconRef, Is.EqualTo("AsphaltIcon:gsi"));
      Assert.That(path.Texture1Ref, Is.EqualTo("Asphalt_Texture1"));
      Assert.That(path.Texture2Ref, Is.EqualTo("Asphalt_Texture2"));
      Assert.That(path.ShapeOwners, Has.Count.EqualTo(19));
      Assert.That(path.ShapeOwners[0].Kind, Is.EqualTo(PathTypeShapeKind.Flat));
      Assert.That(path.ShapeOwners[0].Variants, Is.EqualTo(new[] {
        "Shape0Variant0", "Shape0Variant1", "Shape0Variant2", "Shape0Variant3"
      }));
      Assert.That(path.ShapeOwners[^1].Kind, Is.EqualTo(PathTypeShapeKind.SlopeMid));
      Assert.That(path.ShapeOwners[^1].Variants[^1], Is.EqualTo("Shape18Variant3"));
      Assert.That(path.ResearchCategories, Has.Count.EqualTo(1));
      Assert.That(path.ResearchCategories[0],
        Is.EqualTo(new PathResearchCategory("Paths", 42, 84)));
      Assert.That(path.Extended, Is.Null);
    }
  }

  [Test]
  public void Decode_PreservesExtendedShapeOwnersInSerializedOrder() {
    var path = new PathTypeFixture(extended: true, withResearch: false).Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(path.Extended, Is.Not.Null);
      Assert.That(path.Extended!.Unknown1, Is.EqualTo(21));
      Assert.That(path.Extended.Unknown2, Is.EqualTo(22));
      Assert.That(path.Extended.ShapeOwners, Has.Count.EqualTo(16));
      Assert.That(path.Extended.ShapeOwners[0].Kind,
        Is.EqualTo(ExtendedPathTypeShapeKind.FlatFc));
      Assert.That(path.Extended.ShapeOwners[0].Variants[0],
        Is.EqualTo("ExtendedShape0Variant0"));
      Assert.That(path.Extended.ShapeOwners[^1].Kind,
        Is.EqualTo(ExtendedPathTypeShapeKind.SlopeMidTc));
      Assert.That(path.Extended.ShapeOwners[^1].Variants[^1],
        Is.EqualTo("ExtendedShape15Variant3"));
      Assert.That(path.Extended.Paving, Is.Null);
    }
  }

  [Test]
  public void Decode_SharedBudgetCountsAliasedResearchArrayEachTime() {
    var fixture = new PathTypeFixture();
    var context = new PathResourceDecodeContext(
      new PathResourceDecodeLimits(100_000, 79, 100_000));
    fixture.Decode(context);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      fixture.Decode(context)));

    Assert.That(exception!.Message, Does.Contain("research category models"));
  }

  [Test]
  public void Decode_EnforcesByteObjectAndStringBudgets() {
    var fixture = new PathTypeFixture();

    using (Assert.EnterMultipleScope()) {
      Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode(
        new PathResourceDecodeContext(new PathResourceDecodeLimits(3, 1_000, 100_000)))));
      Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode(
        new PathResourceDecodeContext(new PathResourceDecodeLimits(100_000, 0, 100_000)))));
      Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode(
        new PathResourceDecodeContext(new PathResourceDecodeLimits(100_000, 1_000, 7)))));
    }
  }

  [Test]
  public void DataSource_PreflightsCompleteLoaderIndexObjectCharge() {
    using var ovl = new Ovl("fixture");
    ((List<OvlLoaderEntry>)ovl.LoaderEntriesInOrder).Add(
      new OvlLoaderEntry("ptd", 1_000, "fixture.unique.ovl", 900));
    var context = new PathResourceDecodeContext(
      new PathResourceDecodeLimits(1_000, 3, 1_000));

    Assert.Throws<InvalidDataException>(new Action(() => {
      _ = new OvlPathResourceDataSource(ovl, context);
    }));
  }

  [Test]
  public void CommonStringTable_AcceptsDeclaredRangesAndAddressZeroOnly() {
    var directory = Directory.CreateTempSubdirectory();
    try {
      var path = Path.Combine(directory.FullName, "fixture.common.ovl");
      WriteStringTableFixture(path);
      using var ovl = new Ovl("fixture");
      ovl.Add(
        new OvlFile("fixture", FileType.PathType, path),
        new OvlEntry(0, 0));
      var strings = new OvlCommonStringTable(ovl);

      using (Assert.EnterMultipleScope()) {
        Assert.That(strings.TryReadString(0, 16, out var first), Is.True);
        Assert.That(first, Is.EqualTo("zero"));
        Assert.That(strings.TryReadString(5, 16, out var second), Is.True);
        Assert.That(second, Is.EqualTo("second"));
        Assert.That(strings.TryReadString(12, 16, out _), Is.False);
      }
    } finally {
      directory.Delete(recursive: true);
    }
  }

  [TestCase(MalformedPathType.MissingLoaderOwnership)]
  [TestCase(MalformedPathType.WrongHalfSameTagOwnership)]
  [TestCase(MalformedPathType.WrongStructSameTagOwnership)]
  [TestCase(MalformedPathType.TruncatedRecord)]
  [TestCase(MalformedPathType.UnsupportedFlags)]
  [TestCase(MalformedPathType.WrongDisplaySymbol)]
  [TestCase(MalformedPathType.MissingTextureRelocation)]
  [TestCase(MalformedPathType.MissingShapeRelocation)]
  public void Decode_RejectsMalformedOrUnownedData(MalformedPathType malformed) {
    var fixture = new PathTypeFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_InstalledAsphalt_PreservesDeclaredResources() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var archivePath = Path.Combine(root!, "Path", "Asphalt", "Asphalt_Stub.common.ovl");
    Assert.That(
      archivePath, Does.Exist, $"Installed path fixture is missing: {archivePath}");

    using var ovl = Ovl.Load(archivePath);
    var path = PathTypes.Extract(ovl).Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(path.Name, Is.EqualTo("Asphalt").IgnoreCase);
      Assert.That(path.InternalName, Is.EqualTo("Asphalt").IgnoreCase);
      Assert.That(path.DisplayNameRef, Does.EndWith(":txt").IgnoreCase);
      Assert.That(path.IconRef, Does.EndWith(":gsi").IgnoreCase);
      Assert.That(path.Texture1Ref, Is.Not.Empty);
      Assert.That(path.Texture2Ref, Is.Not.Empty);
      Assert.That(path.ShapeOwners, Has.Count.EqualTo(19));
      Assert.That(path.ShapeOwners, Has.All.Matches<PathTypeShapeOwners>(
        group => group.Variants.Count == 4));
      Assert.That(path.ShapeOwners.SelectMany(group => group.Variants), Has.Some.Not.Empty);
    }
  }

  public enum MalformedPathType {
    MissingLoaderOwnership,
    WrongHalfSameTagOwnership,
    WrongStructSameTagOwnership,
    TruncatedRecord,
    UnsupportedFlags,
    WrongDisplaySymbol,
    MissingTextureRelocation,
    MissingShapeRelocation,
  }

  private static void WriteStringTableFixture(string path) {
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
    writer.Write(0x4b524746u);
    writer.Write(0u);
    writer.Write(1u);
    writer.Write(0u);
    writer.Write(0u);
    writer.Write(0u);
    foreach (var typeIndex in Enumerable.Range(0, 9))
      writer.Write(typeIndex switch { 0 => 2u, 1 => 1u, _ => 0u });
    WriteBlock(writer, "zero\0");
    WriteBlock(writer, "second\0");
    WriteBlock(writer, "wrong\0");
  }

  private static void WriteBlock(BinaryWriter writer, string value) {
    var bytes = Encoding.ASCII.GetBytes(value);
    writer.Write(Convert.ToUInt32(bytes.Length));
    writer.Write(bytes);
  }

  private sealed class PathTypeFixture {
    private const uint RecordAddress = 100;
    private const uint ResearchAddress = 100_000;
    private readonly FakePathResourceDataSource source = new();
    private OvlLoaderEntry owner = new("ptd", RecordAddress, "fixture.unique.ovl", 900);
    private uint nextStringAddress = 1_000;

    public PathTypeFixture(bool extended = false, bool withResearch = true) {
      var size = extended ? PathTypes.ExtendedRecordSize : PathTypes.BaseRecordSize;
      var bytes = source.AddBlock(RecordAddress, size);
      source.Loaders.Add(owner);
      WriteValue(bytes, 0, extended ? PathTypes.ExtendedFlag | 1 : 1);
      source.Symbols[RecordAddress + 8] = new OvlSymbolReference(
        "AsphaltName:txt", owner);
      source.Symbols[RecordAddress + 12] = new OvlSymbolReference(
        "AsphaltIcon:gsi", owner);

      AddStringPointer(bytes, RecordAddress, 4, "Asphalt");
      AddStringPointer(bytes, RecordAddress, 16, "Asphalt_Texture1");
      AddStringPointer(bytes, RecordAddress, 20, "Asphalt_Texture2");
      foreach (var group in Enumerable.Range(0, 19)) {
        foreach (var variant in Enumerable.Range(0, 4)) {
          var offset = 24 + (group * 16) + (variant * 4);
          AddStringPointer(bytes, RecordAddress, offset, $"Shape{group}Variant{variant}");
        }
      }

      if (withResearch) AddResearch(bytes);
      if (!extended) return;

      WriteValue(bytes, PathTypes.BaseRecordSize, 21);
      WriteValue(bytes, PathTypes.BaseRecordSize + 4, 22);
      foreach (var group in Enumerable.Range(0, 16)) {
        foreach (var variant in Enumerable.Range(0, 4)) {
          var offset = 344 + (group * 16) + (variant * 4);
          AddStringPointer(
            bytes,
            RecordAddress,
            offset,
            $"ExtendedShape{group}Variant{variant}");
        }
      }
    }

    public PathType Decode() => PathTypes.Decode("synthetic", owner, source);

    public PathType Decode(PathResourceDecodeContext context) =>
      PathTypes.Decode("synthetic", owner, source, context);

    public void MakeMalformed(MalformedPathType malformed) {
      switch (malformed) {
        case MalformedPathType.MissingLoaderOwnership:
          source.Loaders.Clear();
          break;
        case MalformedPathType.WrongHalfSameTagOwnership:
          owner = owner with { SourcePath = "fixture.common.ovl" };
          break;
        case MalformedPathType.WrongStructSameTagOwnership:
          owner = owner with { StructAddress = owner.StructAddress + 1 };
          break;
        case MalformedPathType.TruncatedRecord:
          source.Blocks[RecordAddress] = source.Blocks[RecordAddress][..^1];
          break;
        case MalformedPathType.UnsupportedFlags:
          WriteValue(source.Blocks[RecordAddress], 0, 5);
          break;
        case MalformedPathType.WrongDisplaySymbol:
          source.Symbols[RecordAddress + 8] = new OvlSymbolReference(
            "AsphaltName:gsi", owner);
          break;
        case MalformedPathType.MissingTextureRelocation:
          source.Relocations.Remove(RecordAddress + 16);
          break;
        case MalformedPathType.MissingShapeRelocation:
          source.Relocations.Remove(RecordAddress + 24);
          break;
      }
    }

    private void AddResearch(byte[] record) {
      WriteValue(record, 328, 1);
      WritePointer(record, RecordAddress, 332, ResearchAddress);
      var research = source.AddBlock(ResearchAddress, 12);
      AddStringPointer(research, ResearchAddress, 0, "Paths");
      WriteValue(research, 4, 42);
      WriteValue(research, 8, 84);
    }

    private void AddStringPointer(byte[] bytes, uint ownerAddress, int offset, string value) {
      var address = nextStringAddress;
      nextStringAddress += 100;
      source.AddBlock(address, Encoding.ASCII.GetBytes(value + "\0"));
      WritePointer(bytes, ownerAddress, offset, address);
    }

    private void WritePointer(byte[] bytes, uint ownerAddress, int offset, uint value) {
      WriteValue(bytes, offset, value);
      source.Relocations[ownerAddress + Convert.ToUInt32(offset)] = value;
    }

    private static void WriteValue(byte[] bytes, int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);
  }

  private sealed class FakePathResourceDataSource : IPathResourceDataSource {
    public Dictionary<uint, byte[]> Blocks { get; } = [];
    public Dictionary<uint, uint> Relocations { get; } = [];
    public Dictionary<uint, OvlSymbolReference> Symbols { get; } = [];
    public List<OvlLoaderEntry> Loaders { get; } = [];
    public IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences => Symbols;

    public byte[] AddBlock(uint address, int length) {
      var bytes = new byte[length];
      Blocks.Add(address, bytes);
      return bytes;
    }

    public void AddBlock(uint address, byte[] bytes) => Blocks.Add(address, bytes);

    public bool HasExactLoader(OvlLoaderEntry owner) => Loaders.Any(loader =>
      loader.DataAddress == owner.DataAddress &&
      loader.StructAddress == owner.StructAddress &&
      string.Equals(loader.Tag, owner.Tag, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(loader.SourcePath, owner.SourcePath, StringComparison.OrdinalIgnoreCase));

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

    public bool TryReadString(uint address, int maximumLength, out string value) {
      foreach (var block in Blocks) {
        if (address < block.Key) continue;
        var offset = Convert.ToUInt64(address - block.Key);
        if (offset >= Convert.ToUInt64(block.Value.Length)) continue;
        var start = Convert.ToInt32(offset);
        var available = Math.Min(maximumLength, block.Value.Length - start);
        var end = Array.IndexOf(block.Value, Convert.ToByte(0), start, available);
        if (end < 0) break;
        value = Encoding.ASCII.GetString(block.Value, start, end - start);
        return true;
      }
      value = string.Empty;
      return false;
    }
  }
}
