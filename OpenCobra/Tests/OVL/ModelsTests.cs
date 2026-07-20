using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class ModelsTests {
  [Test]
  public void Decode_ReadsNeutralCountsAndExactOwner() {
    var fixture = new ModelFixture();

    var model = fixture.Decode();

    Assert.That(model, Is.EqualTo(new ModelDefinition(
      "AdultElephant",
      "fixture.common.ovl",
      1_000,
      900,
      3,
      4,
      5,
      6)));
  }

  [Test]
  public void Decode_UsesImmediateInterleavedNonModelLoaderBoundary() {
    var fixture = new ModelFixture();
    fixture.UseInterleavedNonModelBoundary();

    var model = fixture.Decode();

    Assert.That(model.Count0, Is.EqualTo(3));
  }

  [Test]
  public void Extract_RejectsReferenceWithoutExactModelTag() {
    using var ovl = new Ovl("fixture");

    Assert.Throws<ArgumentException>(new Action(() => Models.Extract(ovl, "AdultElephant:bsh")));
  }

  [TestCase(MalformedModel.WrongLoaderType)]
  [TestCase(MalformedModel.MissingSourcePath)]
  [TestCase(MalformedModel.MissingOwnerFromDataRegion)]
  [TestCase(MalformedModel.DuplicateOwnerInDataRegion)]
  [TestCase(MalformedModel.FinalLoaderWithoutBlockEnd)]
  [TestCase(MalformedModel.WrongRecordExtent)]
  [TestCase(MalformedModel.TruncatedRecord)]
  [TestCase(MalformedModel.MissingLoaderDataRelocation)]
  [TestCase(MalformedModel.WrongLoaderDataRelocation)]
  [TestCase(MalformedModel.InternalRecordRelocation)]
  [TestCase(MalformedModel.MissingExtraData)]
  [TestCase(MalformedModel.WrongExtraChunkCount)]
  [TestCase(MalformedModel.EmptySecondExtraChunk)]
  [TestCase(MalformedModel.TruncatedCountPrefix)]
  [TestCase(MalformedModel.TruncatedCount0Region)]
  [TestCase(MalformedModel.ExcessiveCount0)]
  [TestCase(MalformedModel.ExcessiveCount3)]
  public void Decode_RejectsMalformedOrUnprovenData(MalformedModel malformed) {
    var fixture = new ModelFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledAdultElephantReadsProvenCountLayout() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      rct3Path,
      "WildAnimals",
      "elephant",
      "Elephant_data.common.ovl");
    Assert.That(path, Does.Exist, $"Installed Elephant data OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var file = ovl.Keys.Single(candidate =>
      candidate.Type == FileType.Model &&
      string.Equals(candidate.Name, "AdultElephant", StringComparison.OrdinalIgnoreCase));
    Assert.That(file.Path, Is.EqualTo(path));
    Assert.That(ovl.TryGetDataPointer(file, out var address), Is.True);
    Assert.That(address, Is.EqualTo(350));
    var owner = ovl.LoaderEntriesInOrder.Single(entry =>
      entry.DataAddress == address &&
      entry.Tag.ToFileType() == FileType.Model &&
      string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase));
    Assert.That(owner.StructAddress, Is.EqualTo(190));
    Assert.That(
      ovl.TryGetRelocationSource(owner.StructAddress + sizeof(uint), out var ownerTarget),
      Is.True);
    Assert.That(ownerTarget, Is.EqualTo(address));
    if (!ovl.TryResolveRelocation(address, out var block, out _))
      throw new AssertionException("AdultElephant model block did not resolve.");
    var next = ovl.LoaderEntriesInOrder
      .Where(entry =>
        entry.DataAddress > address &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase) &&
        ovl.TryResolveRelocation(entry.DataAddress, out var candidateBlock, out _) &&
        ReferenceEquals(block, candidateBlock))
      .OrderBy(entry => entry.DataAddress)
      .First();
    using (Assert.EnterMultipleScope()) {
      Assert.That(next.Tag, Is.EqualTo("mdl"));
      Assert.That(next.DataAddress, Is.EqualTo(430));
      Assert.That(next.DataAddress - address, Is.EqualTo(0x50));
    }
    Assert.That(ovl.TryReadBytes(address, 0x50, out var record), Is.True);
    Assert.That(record, Has.Length.EqualTo(0x50));
    foreach (var offset in Enumerable.Range(0, 0x50))
      Assert.That(ovl.TryGetRelocationSource(address + Convert.ToUInt32(offset), out _), Is.False);
    if (!ovl.TryReadExtraData(address, out var chunks))
      throw new AssertionException("AdultElephant model extra data did not resolve.");
    using (Assert.EnterMultipleScope()) {
      Assert.That(chunks, Has.Count.EqualTo(2));
      Assert.That(chunks[0], Has.Length.EqualTo(55_344));
      Assert.That(chunks[1], Has.Length.EqualTo(464));
      Assert.That(BitConverter.ToUInt32(chunks[0], 0), Is.EqualTo(35));
      Assert.That(BitConverter.ToUInt32(chunks[0], 4), Is.Zero);
      Assert.That(BitConverter.ToUInt32(chunks[0], 8), Is.Zero);
      Assert.That(BitConverter.ToUInt32(chunks[0], 12), Is.Zero);
      Assert.That(chunks[0].Length, Is.GreaterThanOrEqualTo(0x10 + 35 * 0x60 + 0x2C));
    }

    var model = Models.Extract(ovl, "AdultElephant:MDL");

    TestContext.Progress.WriteLine(
      $"Installed MDL evidence: source={model.SourcePath}, data={model.DataAddress}, " +
      $"loader={model.LoaderStructAddress}, counts=" +
      $"[{model.Count0}, {model.Count1}, {model.Count2}, {model.Count3}]");
    Assert.That(model, Is.EqualTo(new ModelDefinition(
      "AdultElephant",
      path,
      350,
      190,
      35,
      0,
      0,
      0)));
  }

  private sealed class ModelFixture {
    private const uint RecordAddress = 1_000;
    private const uint StructAddress = 900;
    private const int RecordSize = 0x50;
    private const int CountPrefixSize = 0x10;
    private const int FirstRegionStride = 0x60;
    private const int MetadataSize = 0x2C;
    private const string SourcePath = "fixture.common.ovl";

    private readonly FakeModelDataSource source = new();
    private OvlLoaderEntry owner = new("mdl", RecordAddress, SourcePath, StructAddress);

    public ModelFixture() {
      source.AddBytes(RecordAddress, new byte[RecordSize]);
      source.RegionLoaders.Add(owner);
      source.RegionLoaders.Add(new OvlLoaderEntry(
        "mdl",
        RecordAddress + RecordSize,
        SourcePath,
        StructAddress + 20));
      source.Relocations[StructAddress + sizeof(uint)] = RecordAddress;

      var firstChunk = new byte[
        CountPrefixSize + 3 * FirstRegionStride + MetadataSize];
      WriteCount(firstChunk, 0, 3);
      WriteCount(firstChunk, 1, 4);
      WriteCount(firstChunk, 2, 5);
      WriteCount(firstChunk, 3, 6);
      source.ExtraChunks.Add(firstChunk);
      source.ExtraChunks.Add([1]);
    }

    public ModelDefinition Decode() => Models.Decode("AdultElephant", owner, source);

    public void UseInterleavedNonModelBoundary() {
      source.RegionLoaders.Clear();
      source.RegionLoaders.Add(owner);
      source.RegionLoaders.Add(new OvlLoaderEntry(
        "tex",
        RecordAddress + RecordSize,
        SourcePath,
        StructAddress + 20));
      source.RegionLoaders.Add(new OvlLoaderEntry(
        "mdl",
        RecordAddress + RecordSize + 16,
        SourcePath,
        StructAddress + 40));
    }

    public void MakeMalformed(MalformedModel malformed) {
      switch (malformed) {
        case MalformedModel.WrongLoaderType:
          owner = owner with { Tag = "bsh" };
          break;
        case MalformedModel.MissingSourcePath:
          owner = owner with { SourcePath = "" };
          break;
        case MalformedModel.MissingOwnerFromDataRegion:
          source.RegionLoaders.RemoveAt(0);
          break;
        case MalformedModel.DuplicateOwnerInDataRegion:
          source.RegionLoaders.Add(owner);
          break;
        case MalformedModel.FinalLoaderWithoutBlockEnd:
          source.RegionLoaders.RemoveAt(1);
          break;
        case MalformedModel.WrongRecordExtent:
          source.RegionLoaders[1] = source.RegionLoaders[1] with {
            DataAddress = source.RegionLoaders[1].DataAddress - 1,
          };
          break;
        case MalformedModel.TruncatedRecord:
          source.ReplaceBytes(RecordAddress, new byte[RecordSize - 1]);
          break;
        case MalformedModel.MissingLoaderDataRelocation:
          source.Relocations.Remove(StructAddress + sizeof(uint));
          break;
        case MalformedModel.WrongLoaderDataRelocation:
          source.Relocations[StructAddress + sizeof(uint)] = RecordAddress + 1;
          break;
        case MalformedModel.InternalRecordRelocation:
          source.Relocations[RecordAddress + 4] = 123_456;
          break;
        case MalformedModel.MissingExtraData:
          source.HasExtraData = false;
          break;
        case MalformedModel.WrongExtraChunkCount:
          source.ExtraChunks.RemoveAt(1);
          break;
        case MalformedModel.EmptySecondExtraChunk:
          source.ExtraChunks[1] = [];
          break;
        case MalformedModel.TruncatedCountPrefix:
          source.ExtraChunks[0] = new byte[CountPrefixSize - 1];
          break;
        case MalformedModel.TruncatedCount0Region:
          source.ExtraChunks[0] = source.ExtraChunks[0][..^1];
          break;
        case MalformedModel.ExcessiveCount0:
          WriteCount(source.ExtraChunks[0], 0, 1_000_001);
          break;
        case MalformedModel.ExcessiveCount3:
          WriteCount(source.ExtraChunks[0], 3, 1_000_001);
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed));
      }
    }

    private static void WriteCount(byte[] chunk, int index, uint value) =>
      BitConverter.GetBytes(value).CopyTo(chunk, index * sizeof(uint));
  }

  private sealed class FakeModelDataSource : IModelDataSource {
    private readonly Dictionary<uint, byte[]> segments = [];

    public List<OvlLoaderEntry> RegionLoaders { get; } = [];
    public Dictionary<uint, uint> Relocations { get; } = [];
    public List<byte[]> ExtraChunks { get; } = [];
    public bool HasExtraData { get; set; } = true;

    public void AddBytes(uint address, byte[] bytes) => segments.Add(address, bytes);

    public void ReplaceBytes(uint address, byte[] bytes) => segments[address] = bytes;

    public IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner) =>
      RegionLoaders;

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      if (!segments.TryGetValue(address, out var segment) || length > segment.Length) {
        bytes = [];
        return false;
      }
      bytes = segment.AsSpan(0, length).ToArray();
      return true;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      Relocations.TryGetValue(address, out value);

    public bool TryReadExtraData(
      OvlLoaderEntry owner,
      out IReadOnlyList<byte[]> chunks
    ) {
      chunks = ExtraChunks;
      return HasExtraData;
    }
  }
}

public enum MalformedModel {
  WrongLoaderType,
  MissingSourcePath,
  MissingOwnerFromDataRegion,
  DuplicateOwnerInDataRegion,
  FinalLoaderWithoutBlockEnd,
  WrongRecordExtent,
  TruncatedRecord,
  MissingLoaderDataRelocation,
  WrongLoaderDataRelocation,
  InternalRecordRelocation,
  MissingExtraData,
  WrongExtraChunkCount,
  EmptySecondExtraChunk,
  TruncatedCountPrefix,
  TruncatedCount0Region,
  ExcessiveCount0,
  ExcessiveCount3,
}
