using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class WildAnimalAnimationDataTests {
  [Test]
  public void Decode_PreservesExactOpaqueFieldsReferencesAndValueArrays() {
    var fixture = new WildAnimalAnimationDataFixture();

    var data = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(data.Name, Is.EqualTo("Elephant"));
      Assert.That(data.SourcePath, Is.EqualTo("fixture.unique.ovl"));
      Assert.That(data.DataAddress, Is.EqualTo(1_000));
      Assert.That(data.Field00, Is.EqualTo(101));
      Assert.That(data.Field04, Is.EqualTo(202));
      Assert.That(data.SerializedCountAt08, Is.EqualTo(31));
      Assert.That(data.ReferencesAt0CAddress, Is.EqualTo(1_056));
      Assert.That(data.ValuesAt10Address, Is.EqualTo(2_000));
      Assert.That(data.ValuesAt14Address, Is.EqualTo(2_124));
      Assert.That(
        new[] {
          data.Field18,
          data.Field1C,
          data.Field20,
          data.Field24,
          data.Field28,
          data.Field2C,
          data.Field30,
          data.Field34,
        },
        Is.EqualTo(Enumerable.Range(0, 8).Select(index => index + 0.25f)));
      Assert.That(data.ModelAnimationReferencesAt38, Has.Count.EqualTo(31));
      Assert.That(data.ModelAnimationReferencesAt38[0], Is.EqualTo("Clip00:modelanim"));
      Assert.That(data.ModelAnimationReferencesAt38[3], Is.EqualTo(":modelanim"));
      Assert.That(data.ModelAnimationReferencesAt38[22], Is.EqualTo(":modelanim"));
      Assert.That(data.ModelAnimationReferencesAt38[23], Is.EqualTo(":modelanim"));
      Assert.That(data.ModelAnimationReferencesAt38[24], Is.EqualTo(":modelanim"));
      Assert.That(
        data.ValuesAt10,
        Is.EqualTo(Enumerable.Range(0, 31).Select(index => index + 0.5f)));
      Assert.That(
        data.ValuesAt14,
        Is.EqualTo(Enumerable.Range(0, 31).Select(index => index + 100.5f)));
    }
  }

  [Test]
  public void Decode_FinalLoaderAcceptsOnlyExactArchiveBlockEnd() {
    var fixture = new WildAnimalAnimationDataFixture();
    fixture.MakeFinalLoaderAtExactBlockEnd();

    var data = fixture.Decode();

    Assert.That(data.ModelAnimationReferencesAt38, Has.Count.EqualTo(31));
  }

  [TestCase(MalformedWildAnimalAnimationData.WrongLoaderType)]
  [TestCase(MalformedWildAnimalAnimationData.WrongArchiveVersion)]
  [TestCase(MalformedWildAnimalAnimationData.WrongArchiveHalf)]
  [TestCase(MalformedWildAnimalAnimationData.MissingOwnerInRegion)]
  [TestCase(MalformedWildAnimalAnimationData.AliasedDataAddress)]
  [TestCase(MalformedWildAnimalAnimationData.MissingLoaderDataRelocation)]
  [TestCase(MalformedWildAnimalAnimationData.WrongLoaderDataRelocation)]
  [TestCase(MalformedWildAnimalAnimationData.WrongRecordExtent)]
  [TestCase(MalformedWildAnimalAnimationData.TruncatedRecord)]
  [TestCase(MalformedWildAnimalAnimationData.FinalRecordHasTrailingBytes)]
  [TestCase(MalformedWildAnimalAnimationData.RelocatedOpaqueField)]
  [TestCase(MalformedWildAnimalAnimationData.NonFiniteOpaqueField)]
  [TestCase(MalformedWildAnimalAnimationData.WrongSerializedCount)]
  [TestCase(MalformedWildAnimalAnimationData.MissingReferencesPointerRelocation)]
  [TestCase(MalformedWildAnimalAnimationData.WrongReferencesPointerRelocation)]
  [TestCase(MalformedWildAnimalAnimationData.MisplacedReferencesArray)]
  [TestCase(MalformedWildAnimalAnimationData.PointerConflictsWithSymbolRef)]
  [TestCase(MalformedWildAnimalAnimationData.MissingValuesPointerRelocation)]
  [TestCase(MalformedWildAnimalAnimationData.WrongValuesPointerRelocation)]
  [TestCase(MalformedWildAnimalAnimationData.NonAdjacentValueArrays)]
  [TestCase(MalformedWildAnimalAnimationData.ValueArraysOverlapRecord)]
  [TestCase(MalformedWildAnimalAnimationData.TruncatedValueArrays)]
  [TestCase(MalformedWildAnimalAnimationData.RelocatedValue)]
  [TestCase(MalformedWildAnimalAnimationData.ValueConflictsWithSymbolRef)]
  [TestCase(MalformedWildAnimalAnimationData.NonFiniteValueAt10)]
  [TestCase(MalformedWildAnimalAnimationData.NonFiniteValueAt14)]
  [TestCase(MalformedWildAnimalAnimationData.MissingModelAnimationReference)]
  [TestCase(MalformedWildAnimalAnimationData.WrongModelAnimationTag)]
  [TestCase(MalformedWildAnimalAnimationData.NonBareModelAnimationName)]
  [TestCase(MalformedWildAnimalAnimationData.WrongReferenceOwner)]
  [TestCase(MalformedWildAnimalAnimationData.ReferenceContainsRawPointer)]
  [TestCase(MalformedWildAnimalAnimationData.ReferenceContainsRelocation)]
  [TestCase(MalformedWildAnimalAnimationData.ExtraOwnedReference)]
  public void Decode_RejectsMalformedOrUnprovenData(
    MalformedWildAnimalAnimationData malformed
  ) {
    var fixture = new WildAnimalAnimationDataFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledElephantProvesExactLayout() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      rct3Path,
      "WildAnimals",
      "elephant",
      "elephant_anims.common.ovl");
    Assert.That(path, Does.Exist, $"Installed Elephant animation OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var definitions = WildAnimalAnimationData.Extract(ovl);

    AssertInstalledDefinitions(
      definitions,
      new[] { "Elephant", "BabyElephant" });
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledOstrichProvesExactLayout() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      rct3Path,
      "WildAnimals",
      "Ostrich",
      "Ostrich_anims.common.ovl");
    Assert.That(path, Does.Exist, $"Installed Ostrich animation OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var definitions = WildAnimalAnimationData.Extract(ovl);

    // Symbol-table order is Baby then Male in this archive. Data-address order is Male then Baby,
    // so this also protects the loader/data ownership rule from accidental symbol-order zipping.
    AssertInstalledDefinitions(
      definitions,
      new[] { "MaleOstrich", "BabyOstrich" });
  }

  private static void AssertInstalledDefinitions(
    IReadOnlyList<WildAnimalAnimationDataDefinition> definitions,
    IReadOnlyList<string> expectedNames
  ) {
    Assert.That(definitions.Select(definition => definition.Name), Is.EqualTo(expectedNames));
    Assert.That(
      definitions.Select(definition => definition.DataAddress),
      Is.Ordered.Ascending);
    foreach (var definition in definitions) {
      using (Assert.EnterMultipleScope()) {
        Assert.That(
          definition.SourcePath.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase),
          Is.True);
        Assert.That(definition.SerializedCountAt08, Is.EqualTo(31));
        Assert.That(
          definition.ReferencesAt0CAddress,
          Is.EqualTo(definition.DataAddress + Convert.ToUInt32(0x38)));
        Assert.That(
          definition.ValuesAt14Address,
          Is.EqualTo(definition.ValuesAt10Address + Convert.ToUInt32(31 * sizeof(float))));
        Assert.That(definition.ModelAnimationReferencesAt38, Has.Count.EqualTo(31));
        Assert.That(
          definition.ModelAnimationReferencesAt38.Count(reference =>
            reference.Equals(":modelanim", StringComparison.OrdinalIgnoreCase)),
          Is.EqualTo(4));
        Assert.That(definition.ValuesAt10, Has.Count.EqualTo(31));
        Assert.That(definition.ValuesAt14, Has.Count.EqualTo(31));
        Assert.That(definition.ValuesAt10.All(float.IsFinite), Is.True);
        Assert.That(definition.ValuesAt14.All(float.IsFinite), Is.True);
      }
    }
  }

  private sealed class WildAnimalAnimationDataFixture {
    private const uint RecordAddress = 1_000;
    private const uint ValuesAt10Address = 2_000;
    private const uint ValuesAt14Address = 2_124;
    private const int RecordSize = 0xB4;
    private const int ReferenceArrayOffset = 0x38;
    private const int ValueCount = 31;
    private const int ValueArraySize = ValueCount * sizeof(float);
    private const string SourcePath = "fixture.unique.ovl";

    private readonly byte[] record = new byte[RecordSize];
    private readonly byte[] valueArrays = new byte[ValueArraySize * 2];
    private readonly FakeWildAnimalAnimationDataSource source = new();
    private OpenCobra.OVL.Version archiveVersion = OpenCobra.OVL.Version.Five;
    private OvlLoaderEntry owner = new("wad", RecordAddress, SourcePath, 900);

    public WildAnimalAnimationDataFixture() {
      source.AddBytes(RecordAddress, record);
      source.AddBytes(ValuesAt10Address, valueArrays);
      source.RegionLoaders.Add(owner);
      source.RegionLoaders.Add(new OvlLoaderEntry(
        "modelanim",
        RecordAddress + RecordSize,
        SourcePath,
        920));
      source.Relocations.Add(owner.StructAddress + sizeof(uint), owner.DataAddress);

      WriteUInt32(0, 101);
      WriteUInt32(4, 202);
      WriteUInt32(8, ValueCount);
      AddPointer(0x0C, RecordAddress + ReferenceArrayOffset);
      AddPointer(0x10, ValuesAt10Address);
      AddPointer(0x14, ValuesAt14Address);
      foreach (var index in Enumerable.Range(0, 8))
        WriteSingle(0x18 + index * sizeof(float), index + 0.25f);
      foreach (var index in Enumerable.Range(0, ValueCount)) {
        AddReference(
          ReferenceArrayOffset + index * sizeof(uint),
          index is 3 or 22 or 23 or 24
            ? ":modelanim"
            : $"Clip{index:00}:modelanim");
        WriteValueSingle(index * sizeof(float), index + 0.5f);
        WriteValueSingle(ValueArraySize + index * sizeof(float), index + 100.5f);
      }
    }

    public WildAnimalAnimationDataDefinition Decode() =>
      WildAnimalAnimationData.Decode("Elephant", archiveVersion, owner, source);

    public void MakeFinalLoaderAtExactBlockEnd() {
      source.RegionLoaders.RemoveAt(1);
    }

    public void MakeMalformed(MalformedWildAnimalAnimationData malformed) {
      switch (malformed) {
        case MalformedWildAnimalAnimationData.WrongLoaderType:
          owner = owner with { Tag = "was" };
          break;
        case MalformedWildAnimalAnimationData.WrongArchiveVersion:
          archiveVersion = OpenCobra.OVL.Version.Four;
          break;
        case MalformedWildAnimalAnimationData.WrongArchiveHalf:
          owner = owner with { SourcePath = "fixture.common.ovl" };
          break;
        case MalformedWildAnimalAnimationData.MissingOwnerInRegion:
          source.RegionLoaders.RemoveAt(0);
          break;
        case MalformedWildAnimalAnimationData.AliasedDataAddress:
          source.RegionLoaders.Insert(1, owner with { StructAddress = 901 });
          break;
        case MalformedWildAnimalAnimationData.MissingLoaderDataRelocation:
          source.Relocations.Remove(owner.StructAddress + sizeof(uint));
          break;
        case MalformedWildAnimalAnimationData.WrongLoaderDataRelocation:
          source.Relocations[owner.StructAddress + sizeof(uint)] = RecordAddress + 4;
          break;
        case MalformedWildAnimalAnimationData.WrongRecordExtent:
          source.RegionLoaders[1] = source.RegionLoaders[1] with {
            DataAddress = RecordAddress + RecordSize - 1,
          };
          break;
        case MalformedWildAnimalAnimationData.TruncatedRecord:
          source.ReplaceBytes(RecordAddress, record[..^1]);
          break;
        case MalformedWildAnimalAnimationData.FinalRecordHasTrailingBytes:
          source.RegionLoaders.RemoveAt(1);
          source.ReplaceBytes(RecordAddress, new byte[RecordSize + 1]);
          break;
        case MalformedWildAnimalAnimationData.RelocatedOpaqueField:
          source.Relocations.Add(RecordAddress + 0x18, 123);
          break;
        case MalformedWildAnimalAnimationData.NonFiniteOpaqueField:
          WriteSingle(0x18, float.NaN);
          break;
        case MalformedWildAnimalAnimationData.WrongSerializedCount:
          WriteUInt32(8, ValueCount - 1);
          break;
        case MalformedWildAnimalAnimationData.MissingReferencesPointerRelocation:
          source.Relocations.Remove(RecordAddress + 0x0C);
          break;
        case MalformedWildAnimalAnimationData.WrongReferencesPointerRelocation:
          source.Relocations[RecordAddress + 0x0C] = RecordAddress + ReferenceArrayOffset + 4;
          break;
        case MalformedWildAnimalAnimationData.MisplacedReferencesArray:
          AddPointer(0x0C, RecordAddress + ReferenceArrayOffset + 4);
          break;
        case MalformedWildAnimalAnimationData.PointerConflictsWithSymbolRef:
          source.ResourceReferences.Add(
            RecordAddress + 0x10,
            new OvlSymbolReference("Unexpected:modelanim", owner));
          break;
        case MalformedWildAnimalAnimationData.MissingValuesPointerRelocation:
          source.Relocations.Remove(RecordAddress + 0x10);
          break;
        case MalformedWildAnimalAnimationData.WrongValuesPointerRelocation:
          source.Relocations[RecordAddress + 0x10] = ValuesAt10Address + 4;
          break;
        case MalformedWildAnimalAnimationData.NonAdjacentValueArrays:
          AddPointer(0x14, ValuesAt14Address + 4);
          break;
        case MalformedWildAnimalAnimationData.ValueArraysOverlapRecord:
          AddPointer(0x10, RecordAddress);
          AddPointer(0x14, RecordAddress + ValueArraySize);
          break;
        case MalformedWildAnimalAnimationData.TruncatedValueArrays:
          source.ReplaceBytes(ValuesAt10Address, valueArrays[..^1]);
          break;
        case MalformedWildAnimalAnimationData.RelocatedValue:
          source.Relocations.Add(ValuesAt10Address, 123);
          break;
        case MalformedWildAnimalAnimationData.ValueConflictsWithSymbolRef:
          source.ResourceReferences.Add(
            ValuesAt10Address,
            new OvlSymbolReference("Unexpected:modelanim", owner with { StructAddress = 901 }));
          break;
        case MalformedWildAnimalAnimationData.NonFiniteValueAt10:
          WriteValueSingle(0, float.PositiveInfinity);
          break;
        case MalformedWildAnimalAnimationData.NonFiniteValueAt14:
          WriteValueSingle(ValueArraySize, float.NegativeInfinity);
          break;
        case MalformedWildAnimalAnimationData.MissingModelAnimationReference:
          source.ResourceReferences.Remove(RecordAddress + ReferenceArrayOffset);
          break;
        case MalformedWildAnimalAnimationData.WrongModelAnimationTag:
          ReplaceReference(0, "Clip00:ban", owner);
          break;
        case MalformedWildAnimalAnimationData.NonBareModelAnimationName:
          ReplaceReference(0, @"Animations\Clip00:modelanim", owner);
          break;
        case MalformedWildAnimalAnimationData.WrongReferenceOwner:
          ReplaceReference(0, "Clip00:modelanim", owner with { StructAddress = 901 });
          break;
        case MalformedWildAnimalAnimationData.ReferenceContainsRawPointer:
          WriteUInt32(ReferenceArrayOffset, 123);
          break;
        case MalformedWildAnimalAnimationData.ReferenceContainsRelocation:
          source.Relocations.Add(RecordAddress + ReferenceArrayOffset, 123);
          break;
        case MalformedWildAnimalAnimationData.ExtraOwnedReference:
          source.ResourceReferences.Add(
            RecordAddress + RecordSize + 20,
            new OvlSymbolReference("Unexpected:modelanim", owner));
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed));
      }
    }

    private void AddPointer(int offset, uint target) {
      WriteUInt32(offset, target);
      source.Relocations[RecordAddress + Convert.ToUInt32(offset)] = target;
    }

    private void AddReference(int offset, string symbol) {
      source.ResourceReferences.Add(
        RecordAddress + Convert.ToUInt32(offset),
        new OvlSymbolReference(symbol, owner));
    }

    private void ReplaceReference(int index, string symbol, OvlLoaderEntry referenceOwner) {
      source.ResourceReferences[
        RecordAddress + ReferenceArrayOffset + Convert.ToUInt32(index * sizeof(uint))] =
        new OvlSymbolReference(symbol, referenceOwner);
    }

    private void WriteUInt32(int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(record, offset);

    private void WriteSingle(int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(record, offset);

    private void WriteValueSingle(int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(valueArrays, offset);
  }

  private sealed class FakeWildAnimalAnimationDataSource : IWildAnimalAnimationDataSource {
    private readonly Dictionary<uint, byte[]> segments = [];

    public Dictionary<uint, OvlSymbolReference> ResourceReferences { get; } = [];
    IReadOnlyDictionary<uint, OvlSymbolReference>
      IWildAnimalAnimationDataSource.ResourceReferences => ResourceReferences;
    public Dictionary<uint, uint> Relocations { get; } = [];
    public List<OvlLoaderEntry> RegionLoaders { get; } = [];

    public void AddBytes(uint address, byte[] bytes) => segments.Add(address, bytes);

    public void ReplaceBytes(uint address, byte[] bytes) => segments[address] = bytes;

    public IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner) =>
      RegionLoaders;

    public IReadOnlyList<KeyValuePair<uint, OvlSymbolReference>> GetOwnedResourceReferences(
      OvlLoaderEntry owner
    ) => ResourceReferences.Where(reference =>
      reference.Value.Owner.StructAddress == owner.StructAddress).ToList();

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      foreach (var segment in segments) {
        var start = Convert.ToUInt64(segment.Key);
        var requested = Convert.ToUInt64(address);
        var end = requested + Convert.ToUInt64(length);
        var segmentEnd = start + Convert.ToUInt64(segment.Value.Length);
        if (requested < start || end > segmentEnd) continue;
        var offset = Convert.ToInt32(requested - start);
        bytes = segment.Value.AsSpan(offset, length).ToArray();
        return true;
      }
      bytes = [];
      return false;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      Relocations.TryGetValue(address, out value);
  }
}

public enum MalformedWildAnimalAnimationData {
  WrongLoaderType,
  WrongArchiveVersion,
  WrongArchiveHalf,
  MissingOwnerInRegion,
  AliasedDataAddress,
  MissingLoaderDataRelocation,
  WrongLoaderDataRelocation,
  WrongRecordExtent,
  TruncatedRecord,
  FinalRecordHasTrailingBytes,
  RelocatedOpaqueField,
  NonFiniteOpaqueField,
  WrongSerializedCount,
  MissingReferencesPointerRelocation,
  WrongReferencesPointerRelocation,
  MisplacedReferencesArray,
  PointerConflictsWithSymbolRef,
  MissingValuesPointerRelocation,
  WrongValuesPointerRelocation,
  NonAdjacentValueArrays,
  ValueArraysOverlapRecord,
  TruncatedValueArrays,
  RelocatedValue,
  ValueConflictsWithSymbolRef,
  NonFiniteValueAt10,
  NonFiniteValueAt14,
  MissingModelAnimationReference,
  WrongModelAnimationTag,
  NonBareModelAnimationName,
  WrongReferenceOwner,
  ReferenceContainsRawPointer,
  ReferenceContainsRelocation,
  ExtraOwnedReference,
}
