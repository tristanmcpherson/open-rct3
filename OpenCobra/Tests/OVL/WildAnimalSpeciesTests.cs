using System.Text;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class WildAnimalSpeciesTests {
  [Test]
  public void Decode_ReadsExactPackageAndFourVariantReferences() {
    var fixture = new WildAnimalSpeciesFixture();

    var species = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(species.Name, Is.EqualTo("elephant"));
      Assert.That(
        species.PackagePath,
        Is.EqualTo(@"WildAnimals\elephant\Elephant_data"));
      Assert.That(species.Variants, Is.EqualTo(new[] {
        new WildAnimalSpeciesVariant("AdultElephant:mdl", "Elephant:wad"),
        new WildAnimalSpeciesVariant("AdultElephant:mdl", "Elephant:wad"),
        new WildAnimalSpeciesVariant("BabyElephant:mdl", "babyElephant:wad"),
        new WildAnimalSpeciesVariant("BabyElephant:mdl", "babyElephant:wad"),
      }));
    }
  }

  [Test]
  public void Decode_ReadsExactCompactPackageAndFourVariantReferences() {
    var fixture = new WildAnimalSpeciesFixture(WildAnimalSpeciesFixtureLayout.Compact);

    var species = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(species.Name, Is.EqualTo("ostrich"));
      Assert.That(
        species.PackagePath,
        Is.EqualTo(@"WildAnimals\Ostrich\Ostrich_data"));
      Assert.That(species.Variants, Is.EqualTo(new[] {
        new WildAnimalSpeciesVariant("MaleOstrich:mdl", "MaleOstrich:wad"),
        new WildAnimalSpeciesVariant("FemaleOstrich:mdl", "MaleOstrich:wad"),
        new WildAnimalSpeciesVariant("BabyOstrich:mdl", "BabyOstrich:wad"),
        new WildAnimalSpeciesVariant("BabyOstrich:mdl", "BabyOstrich:wad"),
      }));
    }
  }

  [Test]
  public void Decode_UsesImmediateInterleavedNonWasLoaderAsRecordBoundary() {
    var fixture = new WildAnimalSpeciesFixture();
    fixture.UseInterleavedNonWasBoundary();

    var species = fixture.Decode();

    Assert.That(species.Variants, Has.Count.EqualTo(4));
  }

  [Test]
  public void Decode_FinalLoaderInDataRegionFailsClosedWithoutBlockEndEvidence() {
    var fixture = new WildAnimalSpeciesFixture();
    fixture.MakeFinalLoaderInRegion();

    var exception = Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));

    Assert.That(exception!.Message, Does.Contain(
      "final loader in its proven data region and its block end is unavailable"));
  }

  [TestCase(MalformedWildAnimalSpecies.WrongLoaderType)]
  [TestCase(MalformedWildAnimalSpecies.MissingRecordBoundary)]
  [TestCase(MalformedWildAnimalSpecies.NonMonotonicRecordBoundary)]
  [TestCase(MalformedWildAnimalSpecies.WrongRecordExtent)]
  [TestCase(MalformedWildAnimalSpecies.TruncatedRecord)]
  [TestCase(MalformedWildAnimalSpecies.MissingPackageRelocation)]
  [TestCase(MalformedWildAnimalSpecies.MissingVariantRelocation)]
  [TestCase(MalformedWildAnimalSpecies.NonMonotonicVariantPointers)]
  [TestCase(MalformedWildAnimalSpecies.OutOfRangeVariantPointer)]
  [TestCase(MalformedWildAnimalSpecies.MissingModelReference)]
  [TestCase(MalformedWildAnimalSpecies.MissingAnimationDataReference)]
  [TestCase(MalformedWildAnimalSpecies.WrongModelTag)]
  [TestCase(MalformedWildAnimalSpecies.WrongReferenceOwner)]
  [TestCase(MalformedWildAnimalSpecies.ConflictingReferencePointer)]
  [TestCase(MalformedWildAnimalSpecies.AmbiguousOwnedModelReference)]
  [TestCase(MalformedWildAnimalSpecies.WrongArchiveVersion)]
  [TestCase(MalformedWildAnimalSpecies.UnsupportedSoundSlotCount)]
  [TestCase(MalformedWildAnimalSpecies.MismatchedLayoutSoundSlotCount)]
  [TestCase(MalformedWildAnimalSpecies.DriftedVariantSoundSlotCount)]
  [TestCase(MalformedWildAnimalSpecies.MissingSoundSlotRelocation)]
  [TestCase(MalformedWildAnimalSpecies.MisplacedSoundSlotPointer)]
  [TestCase(MalformedWildAnimalSpecies.MissingSoundReference)]
  [TestCase(MalformedWildAnimalSpecies.WrongSoundTag)]
  public void Decode_RejectsMalformedOrUnprovenData(MalformedWildAnimalSpecies malformed) {
    var fixture = new WildAnimalSpeciesFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledElephantReadsExactFourVariantLayout() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(rct3Path, "WildAnimals", "WildAnimals.common.ovl");
    Assert.That(path, Does.Exist, $"Installed WildAnimals OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    Assert.That(ovl.Version, Is.EqualTo(OpenCobra.OVL.Version.Five));
    // Loading the common path ingests both halves. The symbol's exact resolved block provenance is
    // retained on OvlFile.Path, and installed WAS data is serialized in the unique half.
    var file = ovl.Keys.Single(candidate =>
      candidate.Type == FileType.WildAnimalSpecies &&
      string.Equals(candidate.Name, "elephant", StringComparison.OrdinalIgnoreCase));
    Assert.That(
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase),
      Is.True,
      $"Installed Elephant WAS source was not unique: {file.Path}");
    Assert.That(ovl.TryGetDataPointer(file, out var address), Is.True);
    var owner = ovl.LoaderEntriesInOrder.Single(entry =>
      entry.DataAddress == address &&
      entry.Tag.ToFileType() == FileType.WildAnimalSpecies &&
      string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase));
    Assert.That(owner.SourcePath, Is.EqualTo(file.Path));

    var species = WildAnimalSpecies.Extract(ovl, "Elephant:WAS");

    TestContext.Progress.WriteLine(
      $"Installed WAS evidence: name={species.Name}, package={species.PackagePath}, " +
      $"variants={string.Join(", ", species.Variants.Select(variant =>
        $"{variant.ModelReference}/{variant.AnimationDataReference}"))}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(species.Name, Is.EqualTo("elephant"));
      Assert.That(
        species.PackagePath,
        Is.EqualTo(@"WildAnimals\elephant\Elephant_data"));
      Assert.That(species.Variants, Is.EqualTo(new[] {
        new WildAnimalSpeciesVariant("AdultElephant:mdl", "Elephant:wad"),
        new WildAnimalSpeciesVariant("AdultElephant:mdl", "Elephant:wad"),
        new WildAnimalSpeciesVariant("BabyElephant:mdl", "babyElephant:wad"),
        new WildAnimalSpeciesVariant("BabyElephant:mdl", "babyElephant:wad"),
      }));
    }
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledOstrichReadsExactCompactFourVariantLayout() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(rct3Path, "WildAnimals", "WildAnimals.common.ovl");
    Assert.That(path, Does.Exist, $"Installed WildAnimals OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var file = ovl.Keys.Single(candidate =>
      candidate.Type == FileType.WildAnimalSpecies &&
      string.Equals(candidate.Name, "ostrich", StringComparison.OrdinalIgnoreCase));
    Assert.That(
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase),
      Is.True,
      $"Installed Ostrich WAS source was not unique: {file.Path}");
    Assert.That(ovl.Version, Is.EqualTo(OpenCobra.OVL.Version.Five));
    Assert.That(ovl.TryGetDataPointer(file, out var address), Is.True);
    var owner = ovl.LoaderEntriesInOrder.Single(entry =>
      entry.DataAddress == address &&
      entry.Tag.ToFileType() == FileType.WildAnimalSpecies &&
      string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase));
    Assert.That(owner.SourcePath, Is.EqualTo(file.Path));

    var species = WildAnimalSpecies.Extract(ovl, "Ostrich:WAS");

    using (Assert.EnterMultipleScope()) {
      Assert.That(species.Name, Is.EqualTo("Ostrich"));
      Assert.That(
        species.PackagePath,
        Is.EqualTo(@"WildAnimals\Ostrich\Ostrich_data"));
      Assert.That(species.Variants, Is.EqualTo(new[] {
        new WildAnimalSpeciesVariant("MaleOstrich:mdl", "MaleOstrich:wad"),
        new WildAnimalSpeciesVariant("FemaleOstrich:mdl", "MaleOstrich:wad"),
        new WildAnimalSpeciesVariant("BabyOstrich:mdl", "BabyOstrich:wad"),
        new WildAnimalSpeciesVariant("BabyOstrich:mdl", "BabyOstrich:wad"),
      }));
    }
  }

  private sealed class WildAnimalSpeciesFixture {
    private const uint HeaderAddress = 1_000;
    private const int HeaderSize = 0x40;
    private const int VariantPrefixSize = 0x58;
    private const int SoundSlotSize = 0x2C;
    private const int VariantCount = 4;
    private const string SourcePath = "fixture.unique.ovl";

    private readonly string name;
    private readonly int soundSlotCount;
    private readonly int variantSize;
    private readonly int recordSize;
    private readonly byte[] record;
    private readonly FakeWildAnimalSpeciesDataSource source = new();
    private OpenCobra.OVL.Version archiveVersion = OpenCobra.OVL.Version.Five;
    private OvlLoaderEntry owner = new("was", HeaderAddress, SourcePath, 900);

    public WildAnimalSpeciesFixture(
      WildAnimalSpeciesFixtureLayout layout = WildAnimalSpeciesFixtureLayout.Expanded
    ) {
      var compact = layout == WildAnimalSpeciesFixtureLayout.Compact;
      name = compact ? "ostrich" : "elephant";
      soundSlotCount = compact ? 10 : 24;
      variantSize = checked(VariantPrefixSize + soundSlotCount * SoundSlotSize);
      recordSize = checked(HeaderSize + VariantCount * variantSize);
      record = new byte[recordSize];
      source.AddBytes(HeaderAddress, record);
      source.RegionLoaders.Add(owner);
      source.RegionLoaders.Add(new OvlLoaderEntry(
        "was",
        HeaderAddress + Convert.ToUInt32(recordSize),
        SourcePath,
        920));

      AddString(
        0x14,
        compact
          ? @"WildAnimals\Ostrich\Ostrich_data"
          : @"WildAnimals\elephant\Elephant_data");
      foreach (var index in Enumerable.Range(0, VariantCount)) {
        var variantOffset = HeaderSize + index * variantSize;
        AddPointer(
          0x18 + index * sizeof(uint),
          HeaderAddress + Convert.ToUInt32(variantOffset));
        var modelReference = compact
          ? index switch {
            0 => "MaleOstrich:mdl",
            1 => "FemaleOstrich:mdl",
            _ => "BabyOstrich:mdl",
          }
          : index < 2 ? "AdultElephant:mdl" : "BabyElephant:mdl";
        var animationReference = compact
          ? index < 2 ? "MaleOstrich:wad" : "BabyOstrich:wad"
          : index < 2 ? "Elephant:wad" : "babyElephant:wad";
        AddReference(
          variantOffset,
          modelReference);
        AddReference(
          variantOffset + sizeof(uint),
          animationReference);
        WriteUInt32(
          variantOffset + 0x50,
          Convert.ToUInt32(soundSlotCount));
        AddPointer(
          variantOffset + 0x54,
          HeaderAddress + Convert.ToUInt32(variantOffset + VariantPrefixSize));
        foreach (var slotIndex in Enumerable.Range(0, soundSlotCount))
          AddReference(
            variantOffset + VariantPrefixSize + slotIndex * SoundSlotSize,
            $"{name}Sound{slotIndex}:snd");
      }
    }

    public WildAnimalSpeciesDefinition Decode() =>
      WildAnimalSpecies.Decode(name, archiveVersion, owner, source);

    public void UseInterleavedNonWasBoundary() {
      source.RegionLoaders.Clear();
      source.RegionLoaders.Add(owner);
      source.RegionLoaders.Add(new OvlLoaderEntry(
        "tex",
        HeaderAddress + Convert.ToUInt32(recordSize),
        SourcePath,
        920));
      source.RegionLoaders.Add(new OvlLoaderEntry(
        "was",
        HeaderAddress + Convert.ToUInt32(recordSize) + 100,
        SourcePath,
        940));
    }

    public void MakeFinalLoaderInRegion() {
      source.RegionLoaders.Clear();
      source.RegionLoaders.Add(owner);
    }

    public void MakeMalformed(MalformedWildAnimalSpecies malformed) {
      switch (malformed) {
        case MalformedWildAnimalSpecies.WrongLoaderType:
          owner = owner with { Tag = "wai" };
          break;
        case MalformedWildAnimalSpecies.MissingRecordBoundary:
          source.RegionLoaders.Clear();
          break;
        case MalformedWildAnimalSpecies.NonMonotonicRecordBoundary:
          source.RegionLoaders[1] = source.RegionLoaders[1] with {
            DataAddress = HeaderAddress - 1,
          };
          break;
        case MalformedWildAnimalSpecies.WrongRecordExtent:
          source.RegionLoaders[1] = source.RegionLoaders[1] with {
            DataAddress = source.RegionLoaders[1].DataAddress - 1,
          };
          break;
        case MalformedWildAnimalSpecies.TruncatedRecord:
          source.ReplaceBytes(HeaderAddress, record[..100]);
          break;
        case MalformedWildAnimalSpecies.MissingPackageRelocation:
          source.Relocations.Remove(HeaderAddress + 0x14);
          break;
        case MalformedWildAnimalSpecies.MissingVariantRelocation:
          source.Relocations.Remove(HeaderAddress + 0x18);
          break;
        case MalformedWildAnimalSpecies.NonMonotonicVariantPointers:
          AddPointer(0x1C, HeaderAddress + HeaderSize);
          break;
        case MalformedWildAnimalSpecies.OutOfRangeVariantPointer:
          AddPointer(0x18, HeaderAddress + Convert.ToUInt32(recordSize));
          break;
        case MalformedWildAnimalSpecies.MissingModelReference:
          source.ResourceReferences.Remove(HeaderAddress + HeaderSize);
          break;
        case MalformedWildAnimalSpecies.MissingAnimationDataReference:
          source.ResourceReferences.Remove(HeaderAddress + HeaderSize + sizeof(uint));
          break;
        case MalformedWildAnimalSpecies.WrongModelTag:
          source.ResourceReferences[HeaderAddress + HeaderSize] =
            new OvlSymbolReference("AdultElephant:bsh", owner);
          break;
        case MalformedWildAnimalSpecies.WrongReferenceOwner:
          source.ResourceReferences[HeaderAddress + HeaderSize] = new OvlSymbolReference(
            "AdultElephant:mdl",
            owner with { StructAddress = owner.StructAddress + 1 });
          break;
        case MalformedWildAnimalSpecies.ConflictingReferencePointer:
          WriteUInt32(HeaderSize, 123_456);
          break;
        case MalformedWildAnimalSpecies.AmbiguousOwnedModelReference:
          source.ResourceReferences.Add(
            HeaderAddress + 0x30,
            new OvlSymbolReference("Unexpected:mdl", owner));
          break;
        case MalformedWildAnimalSpecies.WrongArchiveVersion:
          archiveVersion = OpenCobra.OVL.Version.Four;
          break;
        case MalformedWildAnimalSpecies.UnsupportedSoundSlotCount:
          WriteUInt32(HeaderSize + 0x50, 11);
          break;
        case MalformedWildAnimalSpecies.MismatchedLayoutSoundSlotCount:
          WriteUInt32(HeaderSize + 0x50, 10);
          break;
        case MalformedWildAnimalSpecies.DriftedVariantSoundSlotCount:
          WriteUInt32(HeaderSize + variantSize + 0x50, 10);
          break;
        case MalformedWildAnimalSpecies.MissingSoundSlotRelocation:
          source.Relocations.Remove(HeaderAddress + HeaderSize + 0x54);
          break;
        case MalformedWildAnimalSpecies.MisplacedSoundSlotPointer:
          AddPointer(
            HeaderSize + 0x54,
            HeaderAddress + HeaderSize + VariantPrefixSize + sizeof(uint));
          break;
        case MalformedWildAnimalSpecies.MissingSoundReference:
          source.ResourceReferences.Remove(
            HeaderAddress + HeaderSize + VariantPrefixSize);
          break;
        case MalformedWildAnimalSpecies.WrongSoundTag:
          source.ResourceReferences[HeaderAddress + HeaderSize + VariantPrefixSize] =
            new OvlSymbolReference("elephantSound0:txt", owner);
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed));
      }
    }

    private void AddString(int fieldOffset, string value) {
      var address = source.AddString(value);
      AddPointer(fieldOffset, address);
    }

    private void AddPointer(int fieldOffset, uint target) {
      WriteUInt32(fieldOffset, target);
      source.Relocations[HeaderAddress + Convert.ToUInt32(fieldOffset)] = target;
    }

    private void AddReference(int fieldOffset, string symbol) {
      source.ResourceReferences.Add(
        HeaderAddress + Convert.ToUInt32(fieldOffset),
        new OvlSymbolReference(symbol, owner));
    }

    private void WriteUInt32(int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(record, offset);
  }

  private sealed class FakeWildAnimalSpeciesDataSource : IWildAnimalSpeciesDataSource {
    private readonly Dictionary<uint, byte[]> segments = [];
    private uint nextDataAddress = 10_000;

    public Dictionary<uint, OvlSymbolReference> ResourceReferences { get; } = [];
    IReadOnlyDictionary<uint, OvlSymbolReference>
      IWildAnimalSpeciesDataSource.ResourceReferences => ResourceReferences;
    public Dictionary<uint, uint> Relocations { get; } = [];
    public List<OvlLoaderEntry> RegionLoaders { get; } = [];

    public void AddBytes(uint address, byte[] bytes) => segments.Add(address, bytes);

    public void ReplaceBytes(uint address, byte[] bytes) => segments[address] = bytes;

    public uint AddString(string value) {
      var address = nextDataAddress;
      var bytes = Encoding.ASCII.GetBytes(value + "\0");
      segments.Add(address, bytes);
      nextDataAddress += Convert.ToUInt32(bytes.Length + 16);
      return address;
    }

    public IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner) =>
      RegionLoaders;

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

    public bool TryGetNullTerminatedStringByteLength(
      uint address,
      int maximumLength,
      out int length
    ) {
      if (!TryFindSegment(address, out var segment, out var offset)) {
        length = 0;
        return false;
      }
      var available = Math.Min(maximumLength, segment.Length - offset);
      var end = Array.IndexOf(segment, Convert.ToByte(0), offset, available);
      length = end < 0 ? 0 : end - offset;
      return end >= 0;
    }

    public bool TryReadNullTerminatedString(uint address, int maximumLength, out string value) {
      if (!TryFindSegment(address, out var segment, out var offset)) {
        value = string.Empty;
        return false;
      }
      var available = Math.Min(maximumLength, segment.Length - offset);
      var end = Array.IndexOf(segment, Convert.ToByte(0), offset, available);
      if (end < 0) {
        value = string.Empty;
        return false;
      }
      value = Encoding.ASCII.GetString(segment, offset, end - offset);
      return true;
    }

    private bool TryFindSegment(uint address, out byte[] segment, out int offset) {
      foreach (var candidate in segments) {
        var start = Convert.ToUInt64(candidate.Key);
        var target = Convert.ToUInt64(address);
        var end = start + Convert.ToUInt64(candidate.Value.Length);
        if (target < start || target >= end) continue;
        segment = candidate.Value;
        offset = Convert.ToInt32(target - start);
        return true;
      }
      segment = [];
      offset = 0;
      return false;
    }
  }

  private enum WildAnimalSpeciesFixtureLayout {
    Expanded,
    Compact,
  }
}

public enum MalformedWildAnimalSpecies {
  WrongLoaderType,
  MissingRecordBoundary,
  NonMonotonicRecordBoundary,
  WrongRecordExtent,
  TruncatedRecord,
  MissingPackageRelocation,
  MissingVariantRelocation,
  NonMonotonicVariantPointers,
  OutOfRangeVariantPointer,
  MissingModelReference,
  MissingAnimationDataReference,
  WrongModelTag,
  WrongReferenceOwner,
  ConflictingReferencePointer,
  AmbiguousOwnedModelReference,
  WrongArchiveVersion,
  UnsupportedSoundSlotCount,
  MismatchedLayoutSoundSlotCount,
  DriftedVariantSoundSlotCount,
  MissingSoundSlotRelocation,
  MisplacedSoundSlotPointer,
  MissingSoundReference,
  WrongSoundTag,
}
