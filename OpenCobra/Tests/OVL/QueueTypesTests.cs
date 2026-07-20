using System.Text;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class QueueTypesTests {
  [Test]
  public void Decode_PreservesFtxAndSevenShapeOwnersInSerializedOrder() {
    var queue = new QueueTypeFixture().Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(queue.Name, Is.EqualTo("synthetic"));
      Assert.That(queue.InternalName, Is.EqualTo("QueueSet1"));
      Assert.That(queue.DisplayNameRef, Is.EqualTo("QueueSet1Name:txt"));
      Assert.That(queue.IconRef, Is.EqualTo("QueueSet1Icon:gsi"));
      Assert.That(queue.FlexiTextureRef, Is.EqualTo("QueueSet1_Texture:ftx"));
      Assert.That(new[] {
        queue.Straight,
        queue.TurnLeft,
        queue.TurnRight,
        queue.SlopeUp,
        queue.SlopeDown,
        queue.SlopeStraight1,
        queue.SlopeStraight2
      }, Is.EqualTo(new[] {
        "Straight",
        "TurnL",
        "TurnR",
        "SlopeUp",
        "SlopeDown",
        "SlopeStraightA",
        "SlopeStraightB"
      }));
      Assert.That(queue.ResearchCategories, Is.Empty);
    }
  }

  [TestCase(MalformedQueueType.MissingLoaderOwnership)]
  [TestCase(MalformedQueueType.WrongHalfSameTagOwnership)]
  [TestCase(MalformedQueueType.WrongStructSameTagOwnership)]
  [TestCase(MalformedQueueType.TruncatedRecord)]
  [TestCase(MalformedQueueType.MissingFtxSymbol)]
  [TestCase(MalformedQueueType.WrongFtxTag)]
  [TestCase(MalformedQueueType.SymbolOwnedByAnotherLoader)]
  [TestCase(MalformedQueueType.MissingOwnerStringRelocation)]
  [TestCase(MalformedQueueType.UnterminatedOwnerString)]
  [TestCase(MalformedQueueType.EmptyResearchListWithPointer)]
  public void Decode_RejectsMalformedOrUnownedData(MalformedQueueType malformed) {
    var fixture = new QueueTypeFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  public void Decode_SharedBudgetCountsAliasedResearchArrayEachTime() {
    var fixture = new QueueTypeFixture(withResearch: true);
    var context = new PathResourceDecodeContext(
      new PathResourceDecodeLimits(100_000, 3, 100_000));
    fixture.Decode(context);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      fixture.Decode(context)));

    Assert.That(exception!.Message, Does.Contain("research category models"));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_InstalledQueueSet1_PreservesDeclaredResources() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(root!, "Queue", "QueueSet1", "QueueSet1_Stub.common.ovl");
    Assert.That(path, Does.Exist, $"Installed queue fixture is missing: {path}");

    using var ovl = Ovl.Load(path);
    var queue = QueueTypes.Extract(ovl).Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(queue.InternalName, Is.EqualTo("QueueSet1").IgnoreCase);
      Assert.That(queue.FlexiTextureRef, Does.EndWith(":ftx").IgnoreCase);
      Assert.That(new[] {
        queue.Straight,
        queue.TurnLeft,
        queue.TurnRight,
        queue.SlopeUp,
        queue.SlopeDown,
        queue.SlopeStraight1,
        queue.SlopeStraight2
      }, Has.All.Not.Empty);
    }
  }

  public enum MalformedQueueType {
    MissingLoaderOwnership,
    WrongHalfSameTagOwnership,
    WrongStructSameTagOwnership,
    TruncatedRecord,
    MissingFtxSymbol,
    WrongFtxTag,
    SymbolOwnedByAnotherLoader,
    MissingOwnerStringRelocation,
    UnterminatedOwnerString,
    EmptyResearchListWithPointer,
  }

  private sealed class QueueTypeFixture {
    private const uint RecordAddress = 100;
    private const uint StringAddress = 1_000;
    private const uint ResearchAddress = 10_000;
    private const uint ResearchStringAddress = 11_000;
    private readonly FakePathResourceDataSource source = new();
    private OvlLoaderEntry owner = new("qtd", RecordAddress, "fixture.unique.ovl", 900);

    public QueueTypeFixture(bool withResearch = false) {
      var bytes = source.AddBlock(RecordAddress, QueueTypes.RecordSize);
      source.Loaders.Add(owner);
      source.Symbols[RecordAddress + 4] = new OvlSymbolReference(
        "QueueSet1Name:txt", owner);
      source.Symbols[RecordAddress + 8] = new OvlSymbolReference(
        "QueueSet1Icon:gsi", owner);
      source.Symbols[RecordAddress + 12] = new OvlSymbolReference(
        "QueueSet1_Texture:ftx", owner);

      var values = new[] {
        "QueueSet1",
        "Straight",
        "TurnL",
        "TurnR",
        "SlopeUp",
        "SlopeDown",
        "SlopeStraightA",
        "SlopeStraightB"
      };
      var offsets = new[] { 0, 16, 20, 24, 28, 32, 36, 40 };
      foreach (var index in Enumerable.Range(0, values.Length)) {
        var stringAddress = StringAddress + Convert.ToUInt32(index * 100);
        source.AddBlock(stringAddress, Encoding.ASCII.GetBytes(values[index] + "\0"));
        WritePointer(bytes, RecordAddress, offsets[index], stringAddress);
      }
      if (!withResearch) return;

      BitConverter.GetBytes(Convert.ToUInt32(1)).CopyTo(bytes, 44);
      WritePointer(bytes, RecordAddress, 48, ResearchAddress);
      var research = source.AddBlock(ResearchAddress, 12);
      source.AddBlock(ResearchStringAddress, Encoding.ASCII.GetBytes("Queues\0"));
      WritePointer(research, ResearchAddress, 0, ResearchStringAddress);
    }

    public QueueType Decode() => QueueTypes.Decode("synthetic", owner, source);

    public QueueType Decode(PathResourceDecodeContext context) =>
      QueueTypes.Decode("synthetic", owner, source, context);

    public void MakeMalformed(MalformedQueueType malformed) {
      switch (malformed) {
        case MalformedQueueType.MissingLoaderOwnership:
          source.Loaders.Clear();
          break;
        case MalformedQueueType.WrongHalfSameTagOwnership:
          owner = owner with { SourcePath = "fixture.common.ovl" };
          break;
        case MalformedQueueType.WrongStructSameTagOwnership:
          owner = owner with { StructAddress = owner.StructAddress + 1 };
          break;
        case MalformedQueueType.TruncatedRecord:
          source.Blocks[RecordAddress] = source.Blocks[RecordAddress][..^1];
          break;
        case MalformedQueueType.MissingFtxSymbol:
          source.Symbols.Remove(RecordAddress + 12);
          break;
        case MalformedQueueType.WrongFtxTag:
          source.Symbols[RecordAddress + 12] = new OvlSymbolReference(
            "QueueSet1_Texture:tex", owner);
          break;
        case MalformedQueueType.SymbolOwnedByAnotherLoader:
          source.Symbols[RecordAddress + 12] = new OvlSymbolReference(
            "QueueSet1_Texture:ftx",
            owner with { StructAddress = owner.StructAddress + 1 });
          break;
        case MalformedQueueType.MissingOwnerStringRelocation:
          source.Relocations.Remove(RecordAddress + 16);
          break;
        case MalformedQueueType.UnterminatedOwnerString:
          source.Blocks[StringAddress + 100] = Encoding.ASCII.GetBytes("Straight");
          break;
        case MalformedQueueType.EmptyResearchListWithPointer:
          WritePointer(source.Blocks[RecordAddress], RecordAddress, 48, StringAddress);
          break;
      }
    }

    private void WritePointer(byte[] bytes, uint blockAddress, int offset, uint value) {
      BitConverter.GetBytes(value).CopyTo(bytes, offset);
      source.Relocations[blockAddress + Convert.ToUInt32(offset)] = value;
    }
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
