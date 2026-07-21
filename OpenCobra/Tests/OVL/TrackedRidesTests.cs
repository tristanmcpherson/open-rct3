using System.Text;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class TrackedRidesTests {
  [Test]
  public void Decode_VanillaReadsSectionsTrainsStationAndMotion() {
    var fixture = new TrackedRideFixture(TrackedRideVersion.Vanilla);

    var ride = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(ride.Name, Is.EqualTo("synthetic"));
      Assert.That(ride.Version, Is.EqualTo(TrackedRideVersion.Vanilla));
      Assert.That(ride.TrackSections, Has.Count.EqualTo(2));
      Assert.That(ride.TrackSections[0], Is.EqualTo(
        new TrackedRideTrackSection("Station:tks", "station", 150)));
      Assert.That(ride.TrackSections[1].Resource, Is.EqualTo("Straight:tks"));
      Assert.That(ride.TrainNames, Is.EqualTo(new[] { "WoodenTrain", "ReversedTrain" }));
      Assert.That(ride.CableLift, Is.EqualTo("CableLift"));
      Assert.That(ride.LiftCar, Is.EqualTo("LiftCar"));
      Assert.That(ride.VanillaTrackPath, Is.EqualTo("WoodenTrack"));
      Assert.That(ride.Station.Name, Is.EqualTo("WoodenPlatform"));
      Assert.That(ride.Station.PlatformHeightOverTrack, Is.EqualTo(7));
      Assert.That(ride.Station.StartPreset, Is.EqualTo(TrackedRideStartPreset.Launched));
      Assert.That(ride.Station.RollSpeed, Is.EqualTo(8f));
      Assert.That(ride.Motion.LaunchedMaximum, Is.EqualTo(50f));
      Assert.That(ride.Options.BlocksPossible, Is.EqualTo(257));
      Assert.That(ride.Costs.UpkeepPerTrain, Is.EqualTo(1100));
      Assert.That(ride.References.OtherTop, Is.EqualTo("TowerTop:tks"));
      Assert.That(ride.References.TrackSpline, Is.EqualTo("TrackProfile:spl"));
      Assert.That(ride.Expansion, Is.Null);
      Assert.That(ride.Wild, Is.Null);
    }
  }

  [Test]
  public void Decode_SoakedReadsFullExpansionAndTrackPaths() {
    var fixture = new TrackedRideFixture(TrackedRideVersion.Soaked);

    var ride = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(ride.Version, Is.EqualTo(TrackedRideVersion.Soaked));
      Assert.That(ride.VanillaTrackPath, Is.Null);
      Assert.That(ride.Expansion, Is.Not.Null);
      Assert.That(ride.Expansion!.TrackPaths, Is.EqualTo(
        new[] { "SoakedTrack", "SoakedTrackAlternate" }));
      Assert.That(ride.Expansion.OtherTopFlipped, Is.EqualTo("FlippedTop:tks"));
      Assert.That(ride.Expansion.MinimumLength, Is.EqualTo(-1));
      Assert.That(ride.Expansion.WaterSectionFlag, Is.Zero);
      Assert.That(ride.Expansion.Unknown103, Is.EqualTo(0.25f));
      Assert.That(ride.Wild, Is.Null);
    }
  }

  [Test]
  public void Decode_SoakedReadsShortWaterSectionLayout() {
    var fixture = new TrackedRideFixture(TrackedRideVersion.Soaked, shortWaterLayout: true);

    var ride = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(ride.Version, Is.EqualTo(TrackedRideVersion.Soaked));
      Assert.That(ride.Expansion!.WaterSectionFlag, Is.EqualTo(1));
      Assert.That(ride.Expansion.WaterSectionSpeed, Is.EqualTo(3f));
      Assert.That(ride.Expansion.Unknown103, Is.Null);
    }
  }

  [Test]
  public void Decode_WildUsesNestedAddonAndReadsWildTail() {
    var fixture = new TrackedRideFixture(TrackedRideVersion.Wild);

    var ride = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(ride.Version, Is.EqualTo(TrackedRideVersion.Wild));
      Assert.That(ride.Expansion, Is.Not.Null);
      Assert.That(ride.Wild, Is.Not.Null);
      Assert.That(ride.Wild!.Splitter, Is.EqualTo("TrackSplitter:svd"));
      Assert.That(ride.Wild.RoboFlag, Is.EqualTo(1));
      Assert.That(ride.Wild.SpinnerControl, Is.EqualTo(2));
      Assert.That(ride.Wild.EquivalentInversion, Is.EqualTo(3));
      Assert.That(ride.Wild.DefaultTrainCount, Is.EqualTo(-1));
      Assert.That(ride.Wild.StationSyncDefault, Is.EqualTo(1));
      Assert.That(ride.Wild.InternalName, Is.EqualTo("synthetictrr"));
    }
  }

  [Test]
  public void Decode_EnforcesAggregateByteBudget() {
    var fixture = new TrackedRideFixture(TrackedRideVersion.Vanilla);

    Assert.Throws<InvalidDataException>(new Action(() =>
      fixture.Decode(new TrackedRideDecodeLimits(379, 100))));
  }

  [Test]
  public void Extract_PreflightsCompleteLoaderIndexObjectCharge() {
    using var ovl = new Ovl("fixture");
    ((List<OvlLoaderEntry>)ovl.LoaderEntriesInOrder).Add(
      new OvlLoaderEntry("trr", 1_000, "fixture.unique.ovl", 900));

    Assert.Throws<InvalidDataException>(new Action(() =>
      TrackedRides.Extract(ovl, new TrackedRideDecodeLimits(1_000, 3))));
  }

  [TestCase(MalformedTrackedRide.TruncatedBase)]
  [TestCase(MalformedTrackedRide.RelocatedVersionMarker)]
  [TestCase(MalformedTrackedRide.MissingRideRelocation)]
  [TestCase(MalformedTrackedRide.UnsupportedAddon)]
  [TestCase(MalformedTrackedRide.TrackSectionCountCeiling)]
  [TestCase(MalformedTrackedRide.MissingTrackSectionArrayRelocation)]
  [TestCase(MalformedTrackedRide.WrongTrackSectionTag)]
  [TestCase(MalformedTrackedRide.WrongTrackSectionOwner)]
  [TestCase(MalformedTrackedRide.ConflictingTrackSectionRelocation)]
  [TestCase(MalformedTrackedRide.NonFiniteCommonSetting)]
  [TestCase(MalformedTrackedRide.NonFiniteExpansionSetting)]
  [TestCase(MalformedTrackedRide.UnexpectedCoreReference)]
  [TestCase(MalformedTrackedRide.SectionMetadataCountMismatch)]
  [TestCase(MalformedTrackedRide.WildShortWaterLayout)]
  [TestCase(MalformedTrackedRide.UnsupportedStartPreset)]
  public void Decode_RejectsMalformedOrUnprovenCore(MalformedTrackedRide malformed) {
    var version = malformed switch {
      MalformedTrackedRide.RelocatedVersionMarker or
      MalformedTrackedRide.MissingRideRelocation or
      MalformedTrackedRide.UnsupportedAddon or
      MalformedTrackedRide.NonFiniteExpansionSetting => TrackedRideVersion.Soaked,
      MalformedTrackedRide.WildShortWaterLayout => TrackedRideVersion.Wild,
      _ => TrackedRideVersion.Vanilla,
    };
    var fixture = new TrackedRideFixture(version);
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledLogFlumeArchiveDecodesTrackedRideCore() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      rct3Path,
      "tracks",
      "TrackedRides",
      "LogFlume",
      "LogFlume.common.ovl");
    Assert.That(path, Does.Exist, $"Installed LogFlume OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var rides = TrackedRides.Extract(ovl);

    TestContext.Progress.WriteLine(
      $"Installed TRR evidence: rides={rides.Count}, " +
      $"names={string.Join(",", rides.Select(ride => ride.Name))}, " +
      $"versions={string.Join(",", rides.Select(ride => ride.Version))}, " +
      $"train-names={string.Join(",", rides.SelectMany(ride => ride.TrainNames))}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(rides, Is.Not.Empty);
      Assert.That(rides.All(ride => ride.TrackSections.Count > 0), Is.True);
      Assert.That(rides.All(ride => ride.TrainNames.Count > 0), Is.True);
      Assert.That(rides.All(ride => ride.Expansion?.TrackPaths.Count > 0), Is.True);
    }
  }

  private sealed class TrackedRideFixture {
    private const uint HeaderAddress = 1_000;
    private const string SourcePath = "fixture.unique.ovl";

    private readonly byte[] attraction = new byte[60];
    private readonly int commonOffset;
    private readonly byte[] header;
    private readonly OvlLoaderEntry owner = new("trr", HeaderAddress, SourcePath, 900);
    private readonly FakeTrackedRideDataSource source = new();
    private readonly uint trackReferenceAddress;
    private readonly TrackedRideVersion version;

    public TrackedRideFixture(
      TrackedRideVersion version,
      bool shortWaterLayout = false
    ) {
      this.version = version;
      commonOffset = version == TrackedRideVersion.Vanilla ? 80 : 0;
      header = new byte[version switch {
        TrackedRideVersion.Vanilla => 380,
        TrackedRideVersion.Soaked when shortWaterLayout => 408,
        TrackedRideVersion.Soaked => 412,
        TrackedRideVersion.Wild => 444,
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
      }];
      source.AddBytes(HeaderAddress, header);

      if (version == TrackedRideVersion.Vanilla)
        WriteUInt32(header, 0, 256);
      else {
        WriteUInt32(header, 0, uint.MaxValue);
        AddExpansionVersionPointers();
      }

      if (version == TrackedRideVersion.Vanilla)
        WriteUInt32(header, commonOffset, 2);
      else
        WriteUInt32(header, 304, 2);
      WriteUInt32(header, commonOffset + 8, 2);
      WriteUInt32(header, commonOffset + 256, 2);

      var trackReferences = new byte[8];
      trackReferenceAddress = source.AddBytes(trackReferences);
      AddPointer(header, commonOffset + 4, HeaderAddress, trackReferenceAddress);
      source.ResourceReferences.Add(
        trackReferenceAddress,
        new OvlSymbolReference("Station:tks", owner));
      source.ResourceReferences.Add(
        trackReferenceAddress + 4,
        new OvlSymbolReference("Straight:tks", owner));

      var metadata = new byte[16];
      var metadataAddress = source.AddBytes(metadata);
      AddPointer(header, commonOffset + 260, HeaderAddress, metadataAddress);
      AddStringPointer(metadata, 0, metadataAddress, "station");
      WriteUInt32(metadata, 4, 150);
      AddStringPointer(metadata, 8, metadataAddress, "straight");
      WriteUInt32(metadata, 12, 80);

      var trainNames = new byte[8];
      var trainNamesAddress = source.AddBytes(trainNames);
      AddPointer(header, commonOffset + 12, HeaderAddress, trainNamesAddress);
      AddStringPointer(trainNames, 0, trainNamesAddress, "WoodenTrain");
      AddStringPointer(trainNames, 4, trainNamesAddress, "ReversedTrain");

      AddStringPointer(header, commonOffset + 16, HeaderAddress, "CableLift");
      AddStringPointer(header, commonOffset + 36, HeaderAddress, "LiftCar");
      AddStringPointer(header, commonOffset + 56, HeaderAddress, "WoodenPlatform");
      WriteUInt32(header, commonOffset + 60, 7);
      WriteUInt32(header, commonOffset + 72, 1);
      WriteUInt32(header, commonOffset + 76, 3);
      WriteUInt32(header, commonOffset + 80, 257);
      WriteSingle(header, commonOffset + 84, 8f);
      WriteSingle(header, commonOffset + 92, 20f);
      WriteSingle(header, commonOffset + 100, 4f);
      WriteSingle(header, commonOffset + 104, 30_000f);
      WriteSingle(header, commonOffset + 124, -1f);
      WriteSingle(header, commonOffset + 128, 1f);
      WriteSingle(header, commonOffset + 132, 25f);
      WriteSingle(header, commonOffset + 136, 1f);
      WriteSingle(header, commonOffset + 140, 1f);
      WriteUInt32(header, commonOffset + 164, 1100);
      WriteUInt32(header, commonOffset + 168, 300);
      WriteUInt32(header, commonOffset + 172, 1000);
      WriteUInt32(header, commonOffset + 176, 15);
      WriteSingle(header, commonOffset + 180, 10f);
      WriteSingle(header, commonOffset + 184, 0.01f);
      WriteUInt32(header, commonOffset + 196, 45);
      WriteUInt32(header, commonOffset + 200, 200);
      WriteUInt32(header, commonOffset + 204, 1600);
      WriteUInt32(header, commonOffset + 228, 1);
      WriteUInt32(header, commonOffset + 232, 1);
      WriteInt32(header, commonOffset + 236, -1);
      WriteSingle(header, commonOffset + 240, 1f);
      WriteSingle(header, commonOffset + 244, 25f);
      WriteSingle(header, commonOffset + 248, 0.1f);
      WriteUInt32(header, commonOffset + 252, 675_000);
      WriteSingle(header, commonOffset + 264, 1f);
      WriteSingle(header, commonOffset + 268, 50f);
      WriteSingle(header, commonOffset + 272, 0.1f);
      WriteSingle(header, commonOffset + 296, -1f);
      AddReference(commonOffset + 212, "TowerTop:tks");
      AddReference(commonOffset + 276, "TrackProfile:spl");

      if (version == TrackedRideVersion.Vanilla) {
        AddStringPointer(header, commonOffset + 208, HeaderAddress, "WoodenTrack");
        return;
      }

      AddExpansion(shortWaterLayout);
      if (version == TrackedRideVersion.Wild) AddWild();
    }

    public TrackedRide Decode() => TrackedRides.Decode("synthetic", owner, source);

    public TrackedRide Decode(TrackedRideDecodeLimits limits) =>
      TrackedRides.Decode("synthetic", owner, source, limits);

    public void MakeMalformed(MalformedTrackedRide malformed) {
      switch (malformed) {
        case MalformedTrackedRide.TruncatedBase:
          source.ReplaceBytes(HeaderAddress, header[..200]);
          break;
        case MalformedTrackedRide.RelocatedVersionMarker:
          source.Relocations[HeaderAddress] = 50_000;
          break;
        case MalformedTrackedRide.MissingRideRelocation:
          source.Relocations.Remove(HeaderAddress + 300);
          break;
        case MalformedTrackedRide.UnsupportedAddon:
          WriteUInt32(attraction, 0, 256);
          break;
        case MalformedTrackedRide.TrackSectionCountCeiling:
          WriteUInt32(header, commonOffset, 65_537);
          break;
        case MalformedTrackedRide.MissingTrackSectionArrayRelocation:
          source.Relocations.Remove(HeaderAddress + Convert.ToUInt32(commonOffset + 4));
          break;
        case MalformedTrackedRide.WrongTrackSectionTag:
          source.ResourceReferences[trackReferenceAddress] =
            new OvlSymbolReference("Station:shs", owner);
          break;
        case MalformedTrackedRide.WrongTrackSectionOwner:
          source.ResourceReferences[trackReferenceAddress] = new OvlSymbolReference(
            "Station:tks", owner with { StructAddress = owner.StructAddress + 1 });
          break;
        case MalformedTrackedRide.ConflictingTrackSectionRelocation:
          source.Relocations[trackReferenceAddress] = 123_456;
          break;
        case MalformedTrackedRide.NonFiniteCommonSetting:
          WriteSingle(header, commonOffset + 84, float.NaN);
          break;
        case MalformedTrackedRide.NonFiniteExpansionSetting:
          WriteSingle(header, 324, float.PositiveInfinity);
          break;
        case MalformedTrackedRide.UnexpectedCoreReference:
          source.ResourceReferences[HeaderAddress + Convert.ToUInt32(commonOffset + 108)] =
            new OvlSymbolReference("Unexpected:spl", owner);
          break;
        case MalformedTrackedRide.SectionMetadataCountMismatch:
          WriteUInt32(header, commonOffset + 256, 1);
          break;
        case MalformedTrackedRide.WildShortWaterLayout:
          WriteUInt32(header, 388, 1);
          break;
        case MalformedTrackedRide.UnsupportedStartPreset:
          WriteUInt32(header, commonOffset + 72, 16);
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed));
      }
    }

    private void AddExpansionVersionPointers() {
      var ride = new byte[28];
      WriteUInt32(ride, 0, uint.MaxValue);
      WriteUInt32(attraction, 0,
        version == TrackedRideVersion.Soaked ? 512u : 768u);
      WriteUInt32(attraction, 56, Convert.ToUInt32(version));
      var attractionAddress = source.AddBytes(attraction);
      var rideAddress = source.AddBytes(ride);
      AddPointer(ride, 24, rideAddress, attractionAddress);
      AddPointer(header, 300, HeaderAddress, rideAddress);
    }

    private void AddExpansion(bool shortWaterLayout) {
      AddReference(308, "FlippedTop:tks");
      WriteSingle(header, 324, 0.001f);
      WriteInt32(header, 348, -1);
      WriteSingle(header, 352, 1f);
      WriteSingle(header, 356, 1f);
      WriteUInt32(header, 360, 2);
      var trackPaths = new byte[8];
      var trackPathsAddress = source.AddBytes(trackPaths);
      AddPointer(header, 364, HeaderAddress, trackPathsAddress);
      AddStringPointer(trackPaths, 0, trackPathsAddress, "SoakedTrack");
      AddStringPointer(trackPaths, 4, trackPathsAddress, "SoakedTrackAlternate");
      WriteSingle(header, 368, -1f);
      WriteSingle(header, 372, -1f);
      WriteInt32(header, 380, -1);
      WriteUInt32(header, 388, shortWaterLayout ? 1u : 0u);
      WriteSingle(header, 392, shortWaterLayout ? 3f : -1f);
      WriteSingle(header, 396, shortWaterLayout ? 1000f : 0f);
      WriteSingle(header, 400, shortWaterLayout ? 2000f : 0f);
      WriteSingle(header, 404, 4f);
      if (!shortWaterLayout) WriteSingle(header, 408, 0.25f);
    }

    private void AddWild() {
      AddReference(412, "TrackSplitter:svd");
      WriteUInt32(header, 416, 1);
      WriteUInt32(header, 420, 2);
      WriteUInt32(header, 424, 3);
      WriteUInt32(header, 428, 1);
      WriteInt32(header, 432, -1);
      WriteUInt32(header, 436, 1);
      AddStringPointer(header, 440, HeaderAddress, "synthetictrr");
    }

    private void AddReference(int fieldOffset, string symbol) {
      source.ResourceReferences.Add(
        HeaderAddress + Convert.ToUInt32(fieldOffset),
        new OvlSymbolReference(symbol, owner));
    }

    private void AddStringPointer(byte[] target, int offset, uint targetAddress, string value) {
      var address = source.AddString(value);
      AddPointer(target, offset, targetAddress, address);
    }

    private void AddPointer(byte[] target, int offset, uint targetAddress, uint value) {
      WriteUInt32(target, offset, value);
      source.Relocations.Add(targetAddress + Convert.ToUInt32(offset), value);
    }

    private static void WriteUInt32(byte[] target, int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(target, offset);

    private static void WriteInt32(byte[] target, int offset, int value) =>
      BitConverter.GetBytes(value).CopyTo(target, offset);

    private static void WriteSingle(byte[] target, int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(target, offset);
  }

  private sealed class FakeTrackedRideDataSource : ITrackedRideDataSource {
    private readonly Dictionary<uint, byte[]> segments = [];
    private uint nextDataAddress = 5_000;
    private uint nextStringAddress = 50_000;

    public Dictionary<uint, OvlSymbolReference> ResourceReferences { get; } = [];
    IReadOnlyDictionary<uint, OvlSymbolReference> ITrackedRideDataSource.ResourceReferences =>
      ResourceReferences;
    public Dictionary<uint, uint> Relocations { get; } = [];

    public void AddBytes(uint address, byte[] bytes) => segments.Add(address, bytes);

    public uint AddBytes(byte[] bytes) {
      var address = nextDataAddress;
      segments.Add(address, bytes);
      nextDataAddress += Convert.ToUInt32(bytes.Length + 32);
      return address;
    }

    public void ReplaceBytes(uint address, byte[] bytes) => segments[address] = bytes;

    public uint AddString(string value) {
      var address = nextStringAddress;
      var bytes = Encoding.ASCII.GetBytes(value + "\0");
      segments.Add(address, bytes);
      nextStringAddress += Convert.ToUInt32(bytes.Length + 16);
      return address;
    }

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

public enum MalformedTrackedRide {
  TruncatedBase,
  RelocatedVersionMarker,
  MissingRideRelocation,
  UnsupportedAddon,
  TrackSectionCountCeiling,
  MissingTrackSectionArrayRelocation,
  WrongTrackSectionTag,
  WrongTrackSectionOwner,
  ConflictingTrackSectionRelocation,
  NonFiniteCommonSetting,
  NonFiniteExpansionSetting,
  UnexpectedCoreReference,
  SectionMetadataCountMismatch,
  WildShortWaterLayout,
  UnsupportedStartPreset,
}
