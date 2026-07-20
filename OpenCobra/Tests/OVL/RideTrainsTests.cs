using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class RideTrainsTests {
  [Test]
  public void Decode_VanillaReadsCompositionStringsAndFixedSettings() {
    var fixture = new RideTrainFixture(RideTrainVersion.Vanilla);

    var train = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(train.Name, Is.EqualTo("synthetic"));
      Assert.That(train.Version, Is.EqualTo(RideTrainVersion.Vanilla));
      Assert.That(train.InternalName, Is.EqualTo("Synthetic Train"));
      Assert.That(train.InternalDescription, Is.EqualTo("Synthetic description"));
      Assert.That(train.Station, Is.EqualTo("Synthetic Station"));
      Assert.That(train.Cars.Front, Is.EqualTo("FrontCar:ric"));
      Assert.That(train.Cars.Middle, Is.EqualTo("MiddleCar:ric"));
      Assert.That(train.Cars.Link, Is.EqualTo("LinkCar:ric"));
      Assert.That(train.Cars.Second, Is.Null);
      Assert.That(train.Cars.MinimumCount, Is.EqualTo(2));
      Assert.That(train.Cars.MaximumCount, Is.EqualTo(5));
      Assert.That(train.Cars.DefaultCount, Is.EqualTo(3));
      Assert.That(train.LeftLiftSpline, Is.EqualTo("LiftLeft:spl"));
      Assert.That(train.RightLiftSpline, Is.Null);
      Assert.That(train.OvertakeFlag, Is.EqualTo(7));
      Assert.That(train.Speed.Unknown1, Is.EqualTo(11f));
      Assert.That(train.Camera.LookAhead, Is.EqualTo(14f));
      Assert.That(train.Water.Unknown1, Is.EqualTo(27f));
      Assert.That(train.Unknowns.Unknown37, Is.EqualTo(36f));
      Assert.That(train.Expansion, Is.Null);
      Assert.That(train.Wild, Is.Null);
    }
  }

  [Test]
  public void Decode_WildReadsVersionedNameAndWildExtension() {
    var fixture = new RideTrainFixture(RideTrainVersion.Wild);

    var train = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(train.Version, Is.EqualTo(RideTrainVersion.Wild));
      Assert.That(train.InternalName, Is.EqualTo("Synthetic Train"));
      Assert.That(train.Cars.WildUnknown, Is.EqualTo("WildCar:ric"));
      Assert.That(train.Expansion, Is.EqualTo(new RideTrainExpansionSettings(2, 51)));
      Assert.That(train.Wild, Is.Not.Null);
      Assert.That(train.Wild!.AirboatUnknown1, Is.EqualTo(0.02f));
      Assert.That(train.Wild.AirboatUnknown6, Is.EqualTo(6f));
      Assert.That(train.Wild.Unknown59, Is.EqualTo(59));
      Assert.That(train.Wild.Unknown60, Is.EqualTo(60));
      Assert.That(train.Wild.FrequentFallerSiezmicUnknown, Is.EqualTo(-90f));
    }
  }

  [Test]
  public void Decode_SoakedReadsSharedExpansionWithoutWildFields() {
    var fixture = new RideTrainFixture(RideTrainVersion.Soaked);

    var train = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(train.Version, Is.EqualTo(RideTrainVersion.Soaked));
      Assert.That(train.Expansion, Is.EqualTo(new RideTrainExpansionSettings(2, 51)));
      Assert.That(train.Cars.WildUnknown, Is.Null);
      Assert.That(train.Wild, Is.Null);
    }
  }

  [Test]
  public void Decode_EnforcesAggregateByteBudget() {
    var fixture = new RideTrainFixture(RideTrainVersion.Vanilla);

    Assert.Throws<InvalidDataException>(new Action(() =>
      fixture.Decode(new RideTrainDecodeLimits(187, 100))));
  }

  [Test]
  public void Extract_PreflightsCompleteLoaderIndexObjectCharge() {
    using var ovl = new Ovl("fixture");
    ((List<OvlLoaderEntry>)ovl.LoaderEntriesInOrder).Add(
      new OvlLoaderEntry("rit", 1_000, "fixture.unique.ovl", 900));

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrains.Extract(ovl, new RideTrainDecodeLimits(1_000, 3))));
  }

  [TestCase(MalformedRideTrain.TruncatedVanilla)]
  [TestCase(MalformedRideTrain.UnsupportedVersion)]
  [TestCase(MalformedRideTrain.TruncatedWild)]
  [TestCase(MalformedRideTrain.MissingNameRelocation)]
  [TestCase(MalformedRideTrain.RelocatedVersionMarker)]
  [TestCase(MalformedRideTrain.MissingFrontCar)]
  [TestCase(MalformedRideTrain.WrongFrontCarTag)]
  [TestCase(MalformedRideTrain.WrongFrontCarOwner)]
  [TestCase(MalformedRideTrain.ConflictingFrontCarRelocation)]
  [TestCase(MalformedRideTrain.UnexpectedOwnedReference)]
  [TestCase(MalformedRideTrain.UnprovenDirectReference)]
  [TestCase(MalformedRideTrain.NonFiniteSetting)]
  public void Decode_RejectsMalformedOrUnprovenData(MalformedRideTrain malformed) {
    var version = malformed is MalformedRideTrain.UnsupportedVersion or
      MalformedRideTrain.TruncatedWild
      ? RideTrainVersion.Wild
      : RideTrainVersion.Vanilla;
    var fixture = new RideTrainFixture(version);
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledWoodenCoasterArchiveDecodesTrainComposition() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      rct3Path,
      "Cars",
      "CoasterCars",
      "WoodenCoaster",
      "WoodenCoaster.common.ovl");
    Assert.That(path, Does.Exist, $"Installed WoodenCoaster OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var trains = RideTrains.Extract(ovl);

    TestContext.Progress.WriteLine(
      $"Installed RIT evidence: trains={trains.Count}, " +
      $"names={string.Join(",", trains.Select(train => train.Name))}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(trains, Is.Not.Empty);
      Assert.That(trains.All(train => !string.IsNullOrWhiteSpace(train.InternalName)), Is.True);
      Assert.That(trains.All(train => train.Cars.Front.EndsWith(
        ":ric", StringComparison.OrdinalIgnoreCase)), Is.True);
    }
  }

  private sealed class RideTrainFixture {
    private const uint HeaderAddress = 1_000;
    private const string SourcePath = "fixture.unique.ovl";

    private readonly byte[] header;
    private readonly FakeRideTrainDataSource source = new();
    private readonly OvlLoaderEntry owner = new("rit", HeaderAddress, SourcePath, 900);
    private readonly RideTrainVersion version;

    public RideTrainFixture(RideTrainVersion version) {
      this.version = version;
      header = new byte[version switch {
        RideTrainVersion.Vanilla => 188,
        RideTrainVersion.Soaked => 204,
        RideTrainVersion.Wild => 244,
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
      }];
      source.AddBytes(HeaderAddress, header);

      if (version == RideTrainVersion.Vanilla)
        AddString(0, "Synthetic Train");
      else {
        WriteUInt32(0, uint.MaxValue);
        WriteUInt32(188, Convert.ToUInt32(version));
        AddString(192, "Synthetic Train");
        WriteUInt32(196, 2);
        WriteUInt32(200, 51);
      }
      AddString(4, "Synthetic description");
      AddString(184, "Synthetic Station");

      AddReference(8, "FrontCar:ric");
      AddReference(16, "MiddleCar:ric");
      AddReference(28, "LinkCar:ric");
      AddReference(176, "LiftLeft:spl");
      WriteUInt32(32, 2);
      WriteUInt32(36, 5);
      WriteUInt32(40, 3);
      foreach (var offset in Enumerable.Range(11, 24).Select(value => value * 4))
        WriteSingle(offset, Convert.ToSingle(offset / 4));
      WriteUInt32(140, 7);
      foreach (var offset in Enumerable.Range(36, 8).Select(value => value * 4))
        WriteSingle(offset, Convert.ToSingle(offset / 4));

      if (version != RideTrainVersion.Wild) return;
      AddReference(204, "WildCar:ric");
      WriteSingle(208, 0.02f);
      WriteSingle(212, 0.15f);
      WriteSingle(216, -1f);
      WriteSingle(220, 3f);
      WriteSingle(224, 0.75f);
      WriteSingle(228, 6f);
      WriteUInt32(232, 59);
      WriteUInt32(236, 60);
      WriteSingle(240, -90f);
    }

    public RideTrain Decode() => RideTrains.Decode("synthetic", owner, source);

    public RideTrain Decode(RideTrainDecodeLimits limits) =>
      RideTrains.Decode("synthetic", owner, source, limits);

    public void MakeMalformed(MalformedRideTrain malformed) {
      switch (malformed) {
        case MalformedRideTrain.TruncatedVanilla:
          source.ReplaceBytes(HeaderAddress, header[..100]);
          break;
        case MalformedRideTrain.UnsupportedVersion:
          WriteUInt32(188, 1);
          break;
        case MalformedRideTrain.TruncatedWild:
          source.ReplaceBytes(HeaderAddress, header[..220]);
          break;
        case MalformedRideTrain.MissingNameRelocation:
          source.Relocations.Remove(HeaderAddress);
          break;
        case MalformedRideTrain.RelocatedVersionMarker:
          WriteUInt32(0, uint.MaxValue);
          source.Relocations[HeaderAddress] = 5_000;
          break;
        case MalformedRideTrain.MissingFrontCar:
          source.ResourceReferences.Remove(HeaderAddress + 8);
          break;
        case MalformedRideTrain.WrongFrontCarTag:
          source.ResourceReferences[HeaderAddress + 8] =
            new OvlSymbolReference("FrontCar:shs", owner);
          break;
        case MalformedRideTrain.WrongFrontCarOwner:
          source.ResourceReferences[HeaderAddress + 8] = new OvlSymbolReference(
            "FrontCar:ric",
            owner with { StructAddress = owner.StructAddress + 1 });
          break;
        case MalformedRideTrain.ConflictingFrontCarRelocation:
          source.Relocations[HeaderAddress + 8] = 123_456;
          break;
        case MalformedRideTrain.UnexpectedOwnedReference:
          source.ResourceReferences[HeaderAddress + 220] =
            new OvlSymbolReference("Extra:ric", owner);
          break;
        case MalformedRideTrain.UnprovenDirectReference:
          WriteUInt32(12, 123_456);
          break;
        case MalformedRideTrain.NonFiniteSetting:
          WriteSingle(56, float.NaN);
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed));
      }
    }

    private void AddString(int fieldOffset, string value) {
      var address = source.AddString(value);
      WriteUInt32(fieldOffset, address);
      source.Relocations.Add(HeaderAddress + Convert.ToUInt32(fieldOffset), address);
    }

    private void AddReference(int fieldOffset, string symbol) {
      source.ResourceReferences.Add(
        HeaderAddress + Convert.ToUInt32(fieldOffset),
        new OvlSymbolReference(symbol, owner));
    }

    private void WriteUInt32(int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(header, offset);

    private void WriteSingle(int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(header, offset);
  }

  private sealed class FakeRideTrainDataSource : IRideTrainDataSource {
    private readonly Dictionary<uint, byte[]> segments = [];
    private uint nextStringAddress = 10_000;

    public Dictionary<uint, OvlSymbolReference> ResourceReferences { get; } = [];
    IReadOnlyDictionary<uint, OvlSymbolReference> IRideTrainDataSource.ResourceReferences =>
      ResourceReferences;
    public Dictionary<uint, uint> Relocations { get; } = [];

    public void AddBytes(uint address, byte[] bytes) => segments.Add(address, bytes);

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

public enum MalformedRideTrain {
  TruncatedVanilla,
  UnsupportedVersion,
  TruncatedWild,
  MissingNameRelocation,
  RelocatedVersionMarker,
  MissingFrontCar,
  WrongFrontCarTag,
  WrongFrontCarOwner,
  ConflictingFrontCarRelocation,
  UnexpectedOwnedReference,
  UnprovenDirectReference,
  NonFiniteSetting,
}
