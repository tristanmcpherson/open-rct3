using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class TrackSectionsTests {
  private const uint ExtendedStructureFlag = 4_194_304;

  [Test]
  public void Decode_VanillaReadsFixedLayoutSplinesSpeedsAndAnimations() {
    var fixture = new TrackSectionFixture(TrackSectionVersion.Vanilla);

    var section = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(section.Name, Is.EqualTo("synthetic"));
      Assert.That(section.Version, Is.EqualTo(TrackSectionVersion.Vanilla));
      Assert.That(section.InternalName, Is.EqualTo("Synthetic Section"));
      Assert.That(section.SceneryItem, Is.EqualTo("SyntheticSid:sid"));
      Assert.That(section.Entry, Is.EqualTo(
        new TrackSectionEndpoint(1, 17, 25, 2, 3, "EntryGroup")));
      Assert.That(section.Exit, Is.EqualTo(
        new TrackSectionEndpoint(4, 18, 25, 5, 6, "ExitGroup")));
      Assert.That(section.SpecialCurves, Is.EqualTo(7));
      Assert.That(section.Direction, Is.EqualTo(2));
      Assert.That(section.CarSplines,
        Is.EqualTo(new TrackSectionSplinePair("CarLeft:spl", "CarRight:spl")));
      Assert.That(section.JoinSplines,
        Is.EqualTo(new TrackSectionSplinePair("JoinLeft:spl", "JoinRight:spl")));
      Assert.That(section.ExtraSplines,
        Is.EqualTo(new TrackSectionSplinePair("ExtraLeft:spl", "ExtraRight:spl")));
      Assert.That(section.WaterSplines,
        Is.EqualTo(new TrackSectionSplinePair("WaterLeft:spl", "WaterRight:spl")));
      Assert.That(section.Speeds, Has.Count.EqualTo(2));
      Assert.That(section.Speeds[0], Is.EqualTo(new TrackSectionSpeed(1, 2, 3, null, null)));
      Assert.That(section.Animations.Stopped, Is.EqualTo(100));
      Assert.That(section.Animations.HoldAfterTrainLeft, Is.EqualTo(108));
      Assert.That(section.Animations.PreStationLeave, Is.Null);
      Assert.That(section.Options.WaterSplineFlag, Is.EqualTo(1));
      Assert.That(section.Options.ChairLiftStationEnd, Is.EqualTo("Alt"));
      Assert.That(section.Expansion, Is.Null);
      Assert.That(section.Wild, Is.Null);
    }
  }

  [Test]
  public void Decode_SoakedReadsVersionedArraysAndConstructionGroups() {
    var fixture = new TrackSectionFixture(TrackSectionVersion.Soaked);

    var section = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(section.Version, Is.EqualTo(TrackSectionVersion.Soaked));
      Assert.That(section.Speeds[0], Is.EqualTo(new TrackSectionSpeed(1, 2, 3, 4, 5)));
      Assert.That(section.Animations.PreStationLeave, Is.EqualTo(109));
      Assert.That(section.Animations.RotatingTowerIdle, Is.Null);
      Assert.That(section.Expansion, Is.Not.Null);
      Assert.That(section.Expansion!.LoopSpline, Is.EqualTo("Loop:spl"));
      Assert.That(section.Expansion.PathSplines,
        Is.EqualTo(new[] { "PathA:spl", "PathB:spl" }));
      Assert.That(section.Expansion.StationLimits,
        Is.EqualTo(new[] { new TrackSectionStationLimit(1, 2, 3, 49) }));
      Assert.That(section.Expansion.SpeedSplines, Has.Count.EqualTo(1));
      Assert.That(section.Expansion.SpeedSplines[0], Is.EqualTo(
        new TrackSectionSpeedSpline(15,
          new TrackSectionSplinePair("SpeedLeft:spl", "SpeedRight:spl"))));
      Assert.That(section.Expansion.AutoGroup, Is.EqualTo("AutoGroup"));
      Assert.That(section.Expansion.Groups.IsAtEntry, Is.EqualTo(new[] { "EntryA" }));
      Assert.That(section.Expansion.Groups.IsAtExit, Is.EqualTo(new[] { "ExitA" }));
      Assert.That(section.Expansion.Groups.MustHaveAtEntry,
        Is.EqualTo(new[] { "RequiredEntry" }));
      Assert.That(section.Expansion.Groups.MustNotBeAtExit,
        Is.EqualTo(new[] { "ForbiddenExit" }));
      Assert.That(section.Wild, Is.Null);
    }
  }

  [Test]
  public void Decode_SoakedUsesEntryFlagWhenExitExtendedFlagIsClear() {
    var fixture = new TrackSectionFixture(TrackSectionVersion.Soaked);
    fixture.ClearExitExtendedStructureFlag();

    var section = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(section.Version, Is.EqualTo(TrackSectionVersion.Soaked));
      Assert.That(section.Entry.Flags & ExtendedStructureFlag,
        Is.EqualTo(ExtendedStructureFlag));
      Assert.That(section.Exit.Flags & ExtendedStructureFlag, Is.Zero);
      Assert.That(section.Expansion, Is.Not.Null);
    }
  }

  [Test]
  public void Decode_WildReadsEleventhAnimationAndExactTail() {
    var fixture = new TrackSectionFixture(TrackSectionVersion.Wild);

    var section = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(section.Version, Is.EqualTo(TrackSectionVersion.Wild));
      Assert.That(section.Animations.RotatingTowerIdle, Is.EqualTo(110));
      Assert.That(section.Wild, Is.Not.Null);
      Assert.That(section.Wild!.SplitterHalf, Is.EqualTo(1));
      Assert.That(section.Wild.SplitterJoinedOther, Is.EqualTo("OtherHalf:tks"));
      Assert.That(section.Wild.RotatorType, Is.EqualTo(2));
      Assert.That(section.Wild.AnimalHouse, Is.EqualTo(0.1f));
      Assert.That(section.Wild.AlternateTextLookup, Is.EqualTo("AlternateText"));
      Assert.That(section.Wild.TowerCap1, Is.EqualTo(-1f));
      Assert.That(section.Wild.TowerCap2, Is.EqualTo(14.75f));
    }
  }

  [Test]
  public void Decode_EnforcesAggregateByteBudget() {
    var fixture = new TrackSectionFixture(TrackSectionVersion.Vanilla);

    Assert.Throws<InvalidDataException>(new Action(() =>
      fixture.Decode(new TrackSectionDecodeLimits(227, 100))));
  }

  [Test]
  public void Extract_PreflightsCompleteLoaderIndexObjectCharge() {
    using var ovl = new Ovl("fixture");
    ((List<OvlLoaderEntry>)ovl.LoaderEntriesInOrder).Add(
      new OvlLoaderEntry("tks", 1_000, "fixture.unique.ovl", 900));

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSections.Extract(ovl, new TrackSectionDecodeLimits(1_000, 3))));
  }

  [TestCase(MalformedTrackSection.TruncatedBase)]
  [TestCase(MalformedTrackSection.UnsupportedVersion)]
  [TestCase(MalformedTrackSection.MissingInternalNameRelocation)]
  [TestCase(MalformedTrackSection.MissingSceneryItem)]
  [TestCase(MalformedTrackSection.WrongSceneryItemTag)]
  [TestCase(MalformedTrackSection.WrongSceneryItemOwner)]
  [TestCase(MalformedTrackSection.ConflictingSceneryItemRelocation)]
  [TestCase(MalformedTrackSection.UnexpectedOwnedReference)]
  [TestCase(MalformedTrackSection.ExcessiveSpeedCount)]
  [TestCase(MalformedTrackSection.MissingSpeedArrayRelocation)]
  [TestCase(MalformedTrackSection.NonFiniteBaseSetting)]
  [TestCase(MalformedTrackSection.BadAnimationCount)]
  [TestCase(MalformedTrackSection.MissingPathReference)]
  [TestCase(MalformedTrackSection.MissingGroupStringRelocation)]
  [TestCase(MalformedTrackSection.NonFiniteExpansionSetting)]
  [TestCase(MalformedTrackSection.OneSidedExtraSplines)]
  [TestCase(MalformedTrackSection.OneSidedWaterSplines)]
  public void Decode_RejectsMalformedOrUnprovenData(MalformedTrackSection malformed) {
    var version = malformed switch {
      MalformedTrackSection.UnsupportedVersion or
      MalformedTrackSection.BadAnimationCount or
      MalformedTrackSection.MissingPathReference or
      MalformedTrackSection.MissingGroupStringRelocation or
      MalformedTrackSection.NonFiniteExpansionSetting => TrackSectionVersion.Soaked,
      _ => TrackSectionVersion.Vanilla,
    };
    var fixture = new TrackSectionFixture(version);
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [TestCase("Track16", TrackSectionVersion.Vanilla)]
  [TestCase("Track59", TrackSectionVersion.Wild)]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledCoasterArchiveDecodesTrackSections(
    string archive,
    TrackSectionVersion expectedVersion
  ) {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      rct3Path,
      "Tracks",
      "coasters",
      archive,
      $"{archive}.common.ovl");
    Assert.That(path, Does.Exist, $"Installed {archive} OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var sections = TrackSections.Extract(ovl);

    TestContext.Progress.WriteLine(
      $"Installed TKS evidence: sections={sections.Count}, " +
      $"versions={string.Join(",", sections.GroupBy(section => section.Version).Select(
        group => $"{group.Key}:{group.Count()}"))}, " +
      $"groups={sections.Sum(section => section.Expansion?.Groups.IsAtEntry.Count ?? 0)}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(sections, Is.Not.Empty);
      Assert.That(sections.All(section => section.SceneryItem.EndsWith(
        ":sid", StringComparison.OrdinalIgnoreCase)), Is.True);
      Assert.That(sections.All(section => section.CarSplines.Left.EndsWith(
        ":spl", StringComparison.OrdinalIgnoreCase)), Is.True);
      Assert.That(sections.Any(section => section.Version == expectedVersion), Is.True);
    }
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledTrackBased10AcceptsMismatchedEndpointFlags() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      rct3Path,
      "Tracks",
      "TrackedRides",
      "TrackBased10",
      "TrackBased10.common.ovl");
    Assert.That(path, Does.Exist, $"Installed TrackBased10 OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var sections = TrackSections.Extract(ovl);
    var section = sections.Single(section => section.Name == "Medslope2straight");

    TestContext.Progress.WriteLine(
      $"Installed TrackBased10 TKS evidence: sections={sections.Count}, " +
      $"Medslope2straight-version={section.Version}, entry-flags={section.Entry.Flags}, " +
      $"exit-flags={section.Exit.Flags}");

    using (Assert.EnterMultipleScope()) {
      Assert.That(section.Version, Is.EqualTo(TrackSectionVersion.Wild));
      Assert.That(section.Entry.Flags & ExtendedStructureFlag,
        Is.EqualTo(ExtendedStructureFlag));
      Assert.That(section.Exit.Flags & ExtendedStructureFlag, Is.Zero);
    }
  }

  private sealed class TrackSectionFixture {
    private const uint HeaderAddress = 1_000;
    private const string SourcePath = "fixture.unique.ovl";

    private readonly byte[] header;
    private readonly OvlLoaderEntry owner = new("tks", HeaderAddress, SourcePath, 900);
    private readonly FakeTrackSectionDataSource source = new();
    private uint firstGroupStringSlotAddress;
    private uint firstPathReferenceAddress;

    public TrackSectionFixture(TrackSectionVersion version) {
      header = new byte[version switch {
        TrackSectionVersion.Vanilla => 228,
        TrackSectionVersion.Soaked => 364,
        TrackSectionVersion.Wild => 392,
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
      }];
      source.AddBytes(HeaderAddress, header);
      AddStringPointer(header, 0, HeaderAddress, "Synthetic Section");
      AddReference(HeaderAddress + 4, "SyntheticSid:sid");
      WriteUInt32(header, 8, 1);
      WriteUInt32(header, 12, 4);
      WriteUInt32(header, 16, 7);
      WriteUInt32(header, 20, 2);
      WriteUInt32(header, 24,
        version == TrackSectionVersion.Vanilla ? 17 : 17 | ExtendedStructureFlag);
      WriteUInt32(header, 28,
        version == TrackSectionVersion.Vanilla ? 18 : 18 | ExtendedStructureFlag);
      AddReference(HeaderAddress + 32, "CarLeft:spl");
      AddReference(HeaderAddress + 36, "CarRight:spl");
      AddReference(HeaderAddress + 40, "JoinLeft:spl");
      AddReference(HeaderAddress + 44, "JoinRight:spl");
      AddReference(HeaderAddress + 48, "ExtraLeft:spl");
      AddReference(HeaderAddress + 52, "ExtraRight:spl");
      WriteUInt32(header, 68, 25);
      WriteUInt32(header, 72, 2);
      WriteUInt32(header, 76, 3);
      AddStringPointer(header, 80, HeaderAddress, "EntryGroup");
      WriteUInt32(header, 96, 25);
      WriteUInt32(header, 100, 5);
      WriteUInt32(header, 104, 6);
      AddStringPointer(header, 108, HeaderAddress, "ExitGroup");
      AddSpeeds(version);
      WriteUInt32(header, 120, 1);
      WriteSingle(header, 124, 0.1f);
      WriteSingle(header, 128, 5.1f);
      WriteSingle(header, 132, 15.3f);
      WriteSingle(header, 136, 0.75f);
      AddAnimations(version);
      WriteSingle(header, 180, 4.1f);
      WriteSingle(header, 188, 1f);
      WriteSingle(header, 192, 1.2f);
      WriteSingle(header, 196, 1f);
      WriteSingle(header, 200, 3f);
      WriteSingle(header, 204, 3f);
      WriteUInt32(header, 212, 1);
      AddReference(HeaderAddress + 216, "WaterLeft:spl");
      AddReference(HeaderAddress + 220, "WaterRight:spl");
      AddStringPointer(header, 224, HeaderAddress, "Alt");

      if (version == TrackSectionVersion.Vanilla) return;
      AddExpansion(version);
    }

    public TrackSection Decode() => TrackSections.Decode("synthetic", owner, source);

    public TrackSection Decode(TrackSectionDecodeLimits limits) =>
      TrackSections.Decode("synthetic", owner, source, limits);

    public void ClearExitExtendedStructureFlag() =>
      WriteUInt32(header, 28, ReadUInt32(header, 28) & ~ExtendedStructureFlag);

    public void MakeMalformed(MalformedTrackSection malformed) {
      switch (malformed) {
        case MalformedTrackSection.TruncatedBase:
          source.ReplaceBytes(HeaderAddress, header[..100]);
          break;
        case MalformedTrackSection.UnsupportedVersion:
          WriteUInt32(header, 228, 1);
          break;
        case MalformedTrackSection.MissingInternalNameRelocation:
          source.Relocations.Remove(HeaderAddress);
          break;
        case MalformedTrackSection.MissingSceneryItem:
          source.ResourceReferences.Remove(HeaderAddress + 4);
          break;
        case MalformedTrackSection.WrongSceneryItemTag:
          source.ResourceReferences[HeaderAddress + 4] =
            new OvlSymbolReference("SyntheticSid:shs", owner);
          break;
        case MalformedTrackSection.WrongSceneryItemOwner:
          source.ResourceReferences[HeaderAddress + 4] = new OvlSymbolReference(
            "SyntheticSid:sid",
            owner with { StructAddress = owner.StructAddress + 1 });
          break;
        case MalformedTrackSection.ConflictingSceneryItemRelocation:
          source.Relocations[HeaderAddress + 4] = 123_456;
          break;
        case MalformedTrackSection.UnexpectedOwnedReference:
          source.ResourceReferences[HeaderAddress + 208] =
            new OvlSymbolReference("Unexpected:spl", owner);
          break;
        case MalformedTrackSection.ExcessiveSpeedCount:
          WriteUInt32(header, 112, 65_537);
          break;
        case MalformedTrackSection.MissingSpeedArrayRelocation:
          source.Relocations.Remove(HeaderAddress + 116);
          break;
        case MalformedTrackSection.NonFiniteBaseSetting:
          WriteSingle(header, 124, float.NaN);
          break;
        case MalformedTrackSection.BadAnimationCount:
          WriteInt32(header, 140, 9);
          break;
        case MalformedTrackSection.MissingPathReference:
          source.ResourceReferences.Remove(firstPathReferenceAddress);
          break;
        case MalformedTrackSection.MissingGroupStringRelocation:
          source.Relocations.Remove(firstGroupStringSlotAddress);
          break;
        case MalformedTrackSection.NonFiniteExpansionSetting:
          WriteSingle(header, 252, float.PositiveInfinity);
          break;
        case MalformedTrackSection.OneSidedExtraSplines:
          source.ResourceReferences.Remove(HeaderAddress + 52);
          break;
        case MalformedTrackSection.OneSidedWaterSplines:
          source.ResourceReferences.Remove(HeaderAddress + 220);
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed));
      }
    }

    private void AddSpeeds(TrackSectionVersion version) {
      var stride = version == TrackSectionVersion.Vanilla ? 12 : 20;
      var bytes = new byte[stride * 2];
      foreach (var index in Enumerable.Range(0, 2)) {
        var offset = index * stride;
        WriteSingle(bytes, offset, 1 + index * 5);
        WriteSingle(bytes, offset + 4, 2 + index * 5);
        WriteSingle(bytes, offset + 8, 3 + index * 5);
        if (version == TrackSectionVersion.Vanilla) continue;
        WriteSingle(bytes, offset + 12, 4 + index * 5);
        WriteSingle(bytes, offset + 16, 5 + index * 5);
      }
      var address = source.AddBytes(bytes);
      WriteUInt32(header, 112, 2);
      AddPointer(header, 116, HeaderAddress, address);
    }

    private void AddAnimations(TrackSectionVersion version) {
      if (version == TrackSectionVersion.Vanilla) {
        foreach (var index in Enumerable.Range(0, 9))
          WriteInt32(header, 140 + index * 4, 100 + index);
        return;
      }
      var count = version == TrackSectionVersion.Soaked ? 10 : 11;
      var bytes = new byte[count * sizeof(int)];
      foreach (var index in Enumerable.Range(0, count))
        WriteInt32(bytes, index * sizeof(int), 100 + index);
      var address = source.AddBytes(bytes);
      WriteInt32(header, 140, count);
      AddPointer(header, 144, HeaderAddress, address);
    }

    private void AddExpansion(TrackSectionVersion version) {
      const int expansionOffset = 228;
      var expansionAddress = HeaderAddress + expansionOffset;
      WriteUInt32(header, expansionOffset, Convert.ToUInt32(version));
      AddReference(expansionAddress + 4, "Loop:spl");

      var pathArray = new byte[8];
      var pathArrayAddress = source.AddBytes(pathArray);
      WriteUInt32(header, expansionOffset + 8, 2);
      AddPointer(header, expansionOffset + 12, HeaderAddress, pathArrayAddress);
      firstPathReferenceAddress = pathArrayAddress;
      AddReference(pathArrayAddress, "PathA:spl");
      AddReference(pathArrayAddress + 4, "PathB:spl");

      var stationLimits = new byte[16];
      WriteInt32(stationLimits, 0, 1);
      WriteInt32(stationLimits, 4, 2);
      WriteInt32(stationLimits, 8, 3);
      WriteUInt32(stationLimits, 12, 49);
      var stationAddress = source.AddBytes(stationLimits);
      WriteUInt32(header, expansionOffset + 16, 1);
      AddPointer(header, expansionOffset + 20, HeaderAddress, stationAddress);
      WriteSingle(header, expansionOffset + 24, 0.3f);
      WriteSingle(header, expansionOffset + 28, -2f);
      AddStringPointer(header, expansionOffset + 32, HeaderAddress, "AutoGroup");
      WriteInt32(header, expansionOffset + 36, 2);
      WriteUInt32(header, expansionOffset + 40, 68);
      WriteUInt32(header, expansionOffset + 44, 69);
      WriteUInt32(header, expansionOffset + 48, 1);
      WriteUInt32(header, expansionOffset + 52, 1);

      var speedSpline = new byte[12];
      WriteSingle(speedSpline, 0, 15f);
      var speedSplineAddress = source.AddBytes(speedSpline);
      AddReference(speedSplineAddress + 4, "SpeedLeft:spl");
      AddReference(speedSplineAddress + 8, "SpeedRight:spl");
      WriteUInt32(header, expansionOffset + 56, 1);
      AddPointer(header, expansionOffset + 60, HeaderAddress, speedSplineAddress);
      WriteSingle(header, expansionOffset + 64, 7.7f);
      WriteUInt32(header, expansionOffset + 68, 36);
      WriteSingle(header, expansionOffset + 72, 5f);
      WriteSingle(header, expansionOffset + 76, -1f);
      WriteSingle(header, expansionOffset + 80, -1f);
      WriteSingle(header, expansionOffset + 84, -1f);

      AddStringArray(expansionOffset + 88, expansionOffset + 92, "EntryA", recordFirst: true);
      AddStringArray(expansionOffset + 96, expansionOffset + 100, "ExitA");
      AddStringArray(expansionOffset + 104, expansionOffset + 108, "RequiredEntry");
      AddStringArray(expansionOffset + 112, expansionOffset + 116, "RequiredExit");
      AddStringArray(expansionOffset + 120, expansionOffset + 124, "ForbiddenEntry");
      AddStringArray(expansionOffset + 128, expansionOffset + 132, "ForbiddenExit");

      if (version != TrackSectionVersion.Wild) return;
      const int wildOffset = 364;
      var wildAddress = HeaderAddress + wildOffset;
      WriteUInt32(header, wildOffset, 1);
      AddReference(wildAddress + 4, "OtherHalf:tks");
      WriteUInt32(header, wildOffset + 8, 2);
      WriteSingle(header, wildOffset + 12, 0.1f);
      AddStringPointer(header, wildOffset + 16, HeaderAddress, "AlternateText");
      WriteSingle(header, wildOffset + 20, -1f);
      WriteSingle(header, wildOffset + 24, 14.75f);
    }

    private void AddStringArray(
      int countOffset,
      int pointerOffset,
      string value,
      bool recordFirst = false
    ) {
      var array = new byte[4];
      var arrayAddress = source.AddBytes(array);
      AddStringPointer(array, 0, arrayAddress, value);
      WriteUInt32(header, countOffset, 1);
      AddPointer(header, pointerOffset, HeaderAddress, arrayAddress);
      if (recordFirst) firstGroupStringSlotAddress = arrayAddress;
    }

    private void AddReference(uint fieldAddress, string symbol) =>
      source.ResourceReferences.Add(fieldAddress, new OvlSymbolReference(symbol, owner));

    private void AddStringPointer(
      byte[] container,
      int offset,
      uint containerAddress,
      string value
    ) {
      var address = source.AddString(value);
      AddPointer(container, offset, containerAddress, address);
    }

    private void AddPointer(
      byte[] container,
      int offset,
      uint containerAddress,
      uint targetAddress
    ) {
      WriteUInt32(container, offset, targetAddress);
      source.Relocations.Add(containerAddress + Convert.ToUInt32(offset), targetAddress);
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
      BitConverter.ToUInt32(bytes, offset);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteSingle(byte[] bytes, int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);
  }

  private sealed class FakeTrackSectionDataSource : ITrackSectionDataSource {
    private readonly Dictionary<uint, byte[]> segments = [];
    private uint nextDataAddress = 10_000;

    public Dictionary<uint, OvlSymbolReference> ResourceReferences { get; } = [];
    IReadOnlyDictionary<uint, OvlSymbolReference> ITrackSectionDataSource.ResourceReferences =>
      ResourceReferences;
    public Dictionary<uint, uint> Relocations { get; } = [];

    public void AddBytes(uint address, byte[] bytes) => segments.Add(address, bytes);

    public uint AddBytes(byte[] bytes) {
      var address = nextDataAddress;
      segments.Add(address, bytes);
      nextDataAddress += Convert.ToUInt32(bytes.Length + 16);
      return address;
    }

    public void ReplaceBytes(uint address, byte[] bytes) => segments[address] = bytes;

    public uint AddString(string value) => AddBytes(Encoding.ASCII.GetBytes(value + "\0"));

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
}

public enum MalformedTrackSection {
  TruncatedBase,
  UnsupportedVersion,
  MissingInternalNameRelocation,
  MissingSceneryItem,
  WrongSceneryItemTag,
  WrongSceneryItemOwner,
  ConflictingSceneryItemRelocation,
  UnexpectedOwnedReference,
  ExcessiveSpeedCount,
  MissingSpeedArrayRelocation,
  NonFiniteBaseSetting,
  BadAnimationCount,
  MissingPathReference,
  MissingGroupStringRelocation,
  NonFiniteExpansionSetting,
  OneSidedExtraSplines,
  OneSidedWaterSplines,
}
