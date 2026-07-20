using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class RideCarsTests {
  [Test]
  public void Decode_VanillaReadsExactBaseLayoutAndReferences() {
    var fixture = new RideCarFixture(RideCarVersion.Vanilla);

    var car = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(car.Name, Is.EqualTo("synthetic"));
      Assert.That(car.Version, Is.EqualTo(RideCarVersion.Vanilla));
      Assert.That(car.InternalName, Is.EqualTo("Synthetic Car"));
      Assert.That(car.Username, Is.EqualTo("Synthetic Username"));
      Assert.That(car.Seating, Is.EqualTo(2));
      Assert.That(car.Unused, Is.EqualTo(19));
      Assert.That(car.Visual, Is.EqualTo("Body:svd"));
      Assert.That(car.MovingVisual, Is.EqualTo("Moving:svd"));
      Assert.That(car.Inertia, Is.EqualTo(500f));
      Assert.That(car.MovingInertia, Is.EqualTo(40f));
      Assert.That(car.Axis, Is.EqualTo(
        new RideCarAxisSettings(4, 1f, 2f, 3f, 4f, 5f, 6f, 7f)));
      Assert.That(car.Bobbing, Is.EqualTo(new RideCarBobbingSettings(1, 8f, 9f, 10f)));
      Assert.That(car.Animations.Start, Is.EqualTo(100));
      Assert.That(car.Animations.CanoeRow, Is.EqualTo(119));
      Assert.That(car.SeatTypes, Is.Empty);
      Assert.That(car.Wheels.FrontRight,
        Is.EqualTo(new RideCarVisualPart("FrontRight:svd", 11)));
      Assert.That(car.Wheels.FrontLeft.Visual, Is.Null);
      Assert.That(car.Axles.Front,
        Is.EqualTo(new RideCarVisualPart("FrontAxle:svd", 3)));
      Assert.That(car.Unknowns.Unknown54, Is.EqualTo(54));
      Assert.That(car.Unknowns.SkySlideUnknown1, Is.EqualTo(-1));
      Assert.That(car.Soaked, Is.Null);
      Assert.That(car.Wild, Is.Null);
    }
  }

  [Test]
  public void Decode_SoakedReadsSeatArrayAndExactExpansionLayout() {
    var fixture = new RideCarFixture(RideCarVersion.Soaked);

    var car = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(car.Version, Is.EqualTo(RideCarVersion.Soaked));
      Assert.That(car.SeatTypes, Is.EqualTo(new uint[] { 8, 9, 10 }));
      Assert.That(car.Soaked, Is.Not.Null);
      Assert.That(car.Soaked!.SkySlideUnknown4, Is.EqualTo(0.1f));
      Assert.That(car.Soaked.Unknown62, Is.EqualTo(62));
      Assert.That(car.Soaked.Unknown63, Is.EqualTo(63));
      Assert.That(car.Soaked.Unknown64, Is.EqualTo(-1f));
      Assert.That(car.Soaked.SlideUnknown1, Is.EqualTo(1));
      Assert.That(car.Soaked.SkySlideUnknown5, Is.EqualTo(4));
      Assert.That(car.Soaked.SuperSoakerUnknown1, Is.EqualTo(5f));
      Assert.That(car.Soaked.SuperSoakerUnknown2, Is.EqualTo(1));
      Assert.That(car.Soaked.UnstableUnknown1, Is.EqualTo(625));
      Assert.That(car.Soaked.Unknown70, Is.EqualTo(70));
      Assert.That(car.Soaked.SlideUnknown2, Is.EqualTo(3.1f));
      Assert.That(car.Soaked.Unknown72, Is.EqualTo(72));
      Assert.That(car.Soaked.SlideUnknown3, Is.EqualTo(0.3f));
      Assert.That(car.Wild, Is.Null);
    }
  }

  [Test]
  public void Decode_WildReadsExactExtensionAndOptionalReferences() {
    var fixture = new RideCarFixture(RideCarVersion.Wild);

    var car = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(car.Version, Is.EqualTo(RideCarVersion.Wild));
      Assert.That(car.Wild, Is.Not.Null);
      Assert.That(car.Wild!.FlipUnknown, Is.EqualTo(2));
      Assert.That(car.Wild.FlippedVisual, Is.EqualTo("Flipped:svd"));
      Assert.That(car.Wild.FlippedMovingVisual, Is.EqualTo("FlippedMoving:svd"));
      Assert.That(car.Wild.Unknown77, Is.EqualTo(77));
      Assert.That(car.Wild.DriftingUnknown, Is.EqualTo(0.5f));
      Assert.That(car.Wild.Unknown79, Is.EqualTo(1f));
      Assert.That(car.Wild.AnimalSpecies, Is.EqualTo("Elephant:was"));
      Assert.That(car.Wild.PaddleSteamerUnknown1, Is.EqualTo(4.1f));
      Assert.That(car.Wild.PaddleSteamerUnknown2, Is.EqualTo(1));
      Assert.That(car.Wild.RudderAnimation, Is.EqualTo(123));
      Assert.That(car.Wild.BallCoasterUnknown, Is.EqualTo(1));
    }
  }

  [Test]
  public void Decode_WildAnimalSpeciesMaySupplyBodyWithoutSvd() {
    var fixture = new RideCarFixture(RideCarVersion.Wild);
    fixture.UseAnimalSpeciesBody();

    var car = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(car.Version, Is.EqualTo(RideCarVersion.Wild));
      Assert.That(car.Visual, Is.Null);
      Assert.That(car.Wild, Is.Not.Null);
      Assert.That(car.Wild!.AnimalSpecies, Is.EqualTo("Elephant:was"));
    }
  }

  [Test]
  public void Decode_EnforcesAggregateByteBudget() {
    var fixture = new RideCarFixture(RideCarVersion.Vanilla);

    Assert.Throws<InvalidDataException>(new Action(() =>
      fixture.Decode(new RideCarDecodeLimits(239, 100))));
  }

  [Test]
  public void Extract_PreflightsCompleteLoaderIndexObjectCharge() {
    using var ovl = new Ovl("fixture");
    ((List<OvlLoaderEntry>)ovl.LoaderEntriesInOrder).Add(
      new OvlLoaderEntry("ric", 1_000, "fixture.unique.ovl", 900));

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCars.Extract(ovl, new RideCarDecodeLimits(1_000, 3))));
  }

  [TestCase(MalformedRideCar.TruncatedVanilla)]
  [TestCase(MalformedRideCar.UnsupportedVersion)]
  [TestCase(MalformedRideCar.TruncatedWild)]
  [TestCase(MalformedRideCar.MissingNameRelocation)]
  [TestCase(MalformedRideCar.MissingBodyVisual)]
  [TestCase(MalformedRideCar.MissingBodyVisualAndAnimalSpecies)]
  [TestCase(MalformedRideCar.WrongBodyVisualTag)]
  [TestCase(MalformedRideCar.WrongBodyVisualOwner)]
  [TestCase(MalformedRideCar.ConflictingBodyVisualRelocation)]
  [TestCase(MalformedRideCar.UnexpectedOwnedReference)]
  [TestCase(MalformedRideCar.UnprovenDirectReference)]
  [TestCase(MalformedRideCar.ExcessiveSeatTypeCount)]
  [TestCase(MalformedRideCar.MissingSeatTypeRelocation)]
  [TestCase(MalformedRideCar.NonFiniteBaseSetting)]
  [TestCase(MalformedRideCar.NonFiniteExpansionSetting)]
  public void Decode_RejectsMalformedOrUnprovenData(MalformedRideCar malformed) {
    var version = malformed is MalformedRideCar.UnsupportedVersion or
      MalformedRideCar.TruncatedWild or
      MalformedRideCar.MissingBodyVisualAndAnimalSpecies or
      MalformedRideCar.NonFiniteExpansionSetting
      ? RideCarVersion.Wild
      : RideCarVersion.Vanilla;
    var fixture = new RideCarFixture(version);
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledWoodenCoasterArchiveDecodesRideCars() {
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
    var cars = RideCars.Extract(ovl);

    TestContext.Progress.WriteLine(
      $"Installed RIC evidence: cars={cars.Count}, " +
      $"names={string.Join(",", cars.Select(car => car.Name))}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(cars, Is.Not.Empty);
      Assert.That(cars.All(car => !string.IsNullOrWhiteSpace(car.InternalName)), Is.True);
      Assert.That(cars.All(car => car.Visual != null && car.Visual.EndsWith(
        ":svd", StringComparison.OrdinalIgnoreCase)), Is.True);
    }
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledElephantAllowsAnimalSpeciesBody() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      rct3Path,
      "Cars",
      "TrackedRideCars",
      "Elephant",
      "Elephant.common.ovl");
    Assert.That(path, Does.Exist, $"Installed Elephant RIC OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var file = ovl.Keys.Single(candidate =>
      candidate.Name == "Elephant" && candidate.Type == FileType.RideCar);
    Assert.That(ovl.TryGetDataPointer(file, out var address), Is.True);
    var owner = ovl.LoaderEntriesInOrder.Single(entry =>
      entry.DataAddress == address &&
      entry.Tag.ToFileType() == FileType.RideCar &&
      string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase));
    Assert.That(ovl.TryReadBytes(address, 336, out var bytes), Is.True);
    var header = bytes!;
    var references = OvlSymbolReferenceIndex.Create(ovl).References
      .Where(pair => pair.Value.Owner == owner)
      .OrderBy(pair => pair.Key)
      .ToArray();

    TestContext.Progress.WriteLine(
      $"Elephant RIC address={address}, version={header[9]}, " +
      $"body={BitConverter.ToUInt32(header, 12)}, " +
      $"was={BitConverter.ToUInt32(header, 316)}");
    TestContext.Progress.WriteLine(
      "Elephant RIC SymbolRefs: " + string.Join(", ", references.Select(pair =>
        $"+{pair.Key - address}={pair.Value.Symbol}")));
    foreach (var offset in new[] { 0, 4, 12, 20, 160, 296, 300, 316 }) {
      var field = address + Convert.ToUInt32(offset);
      TestContext.Progress.WriteLine(
        $"Elephant RIC +{offset}: raw={BitConverter.ToUInt32(header, offset)}, " +
        $"relocated={ovl.TryGetRelocationSource(field, out var target)}, target={target}");
    }

    var car = RideCars.Extract(ovl).Single(decoded => decoded.Name == "Elephant");

    using (Assert.EnterMultipleScope()) {
      Assert.That(header[9], Is.EqualTo(Convert.ToByte(RideCarVersion.Wild)));
      Assert.That(BitConverter.ToUInt32(header, 12), Is.Zero);
      Assert.That(ovl.TryGetRelocationSource(address + 12, out _), Is.False);
      Assert.That(references.Select(pair => pair.Key - address), Is.EqualTo(new uint[] { 316 }));
      Assert.That(references[0].Value.Symbol, Is.EqualTo("Elephant:was"));
      Assert.That(car.Visual, Is.Null);
      Assert.That(car.Wild, Is.Not.Null);
      Assert.That(car.Wild!.AnimalSpecies, Is.EqualTo("Elephant:was"));
    }
  }

  private sealed class RideCarFixture {
    private const uint HeaderAddress = 1_000;
    private const string SourcePath = "fixture.unique.ovl";

    private readonly byte[] header;
    private readonly FakeRideCarDataSource source = new();
    private readonly OvlLoaderEntry owner = new("ric", HeaderAddress, SourcePath, 900);

    public RideCarFixture(RideCarVersion version) {
      header = new byte[version switch {
        RideCarVersion.Vanilla => 240,
        RideCarVersion.Soaked => 292,
        RideCarVersion.Wild => 336,
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
      }];
      source.AddBytes(HeaderAddress, header);

      AddString(0, "Synthetic Car");
      AddString(4, "Synthetic Username");
      header[8] = 2;
      header[9] = Convert.ToByte(version);
      WriteUInt16(10, 19);
      AddReference(12, "Body:svd");
      WriteSingle(16, 500f);
      AddReference(20, "Moving:svd");
      WriteUInt32(24, 4);
      WriteSingle(28, 40f);
      foreach (var index in Enumerable.Range(0, 7))
        WriteSingle(32 + index * 4, index + 1);
      WriteUInt32(60, 1);
      WriteSingle(64, 8f);
      WriteSingle(68, 9f);
      WriteSingle(72, 10f);
      foreach (var index in Enumerable.Range(0, 20))
        WriteInt32(76 + index * 4, 100 + index);

      AddReference(164, "FrontRight:svd");
      WriteUInt32(168, 11);
      WriteUInt32(176, 12);
      AddReference(180, "BackRight:svd");
      WriteUInt32(184, 13);
      WriteUInt32(192, 14);
      AddReference(196, "FrontAxle:svd");
      WriteUInt32(204, 3);
      WriteUInt32(208, 4);
      WriteUInt32(212, 54);
      WriteSingle(216, -1f);
      WriteInt32(220, -1);
      WriteSingle(224, 40f);
      WriteSingle(228, 4f);
      WriteSingle(232, -1f);
      WriteSingle(236, -1f);

      if (version == RideCarVersion.Vanilla) return;
      AddSeatTypes(8, 9, 10);
      WriteSingle(240, 0.1f);
      WriteUInt32(244, 62);
      WriteUInt32(248, 63);
      WriteSingle(252, -1f);
      WriteUInt32(256, 1);
      WriteUInt32(260, 4);
      WriteSingle(264, 5f);
      WriteUInt32(268, 1);
      WriteUInt32(272, 625);
      WriteUInt32(276, 70);
      WriteSingle(280, 3.1f);
      WriteUInt32(284, 72);
      WriteSingle(288, 0.3f);

      if (version != RideCarVersion.Wild) return;
      WriteUInt32(292, 2);
      AddReference(296, "Flipped:svd");
      AddReference(300, "FlippedMoving:svd");
      WriteUInt32(304, 77);
      WriteSingle(308, 0.5f);
      WriteSingle(312, 1f);
      AddReference(316, "Elephant:was");
      WriteSingle(320, 4.1f);
      WriteUInt32(324, 1);
      WriteInt32(328, 123);
      WriteUInt32(332, 1);
    }

    public RideCar Decode() => RideCars.Decode("synthetic", owner, source);

    public RideCar Decode(RideCarDecodeLimits limits) =>
      RideCars.Decode("synthetic", owner, source, limits);

    public void UseAnimalSpeciesBody() =>
      source.ResourceReferences.Remove(HeaderAddress + 12);

    public void MakeMalformed(MalformedRideCar malformed) {
      switch (malformed) {
        case MalformedRideCar.TruncatedVanilla:
          source.ReplaceBytes(HeaderAddress, header[..100]);
          break;
        case MalformedRideCar.UnsupportedVersion:
          header[9] = 1;
          break;
        case MalformedRideCar.TruncatedWild:
          source.ReplaceBytes(HeaderAddress, header[..320]);
          break;
        case MalformedRideCar.MissingNameRelocation:
          source.Relocations.Remove(HeaderAddress);
          break;
        case MalformedRideCar.MissingBodyVisual:
          source.ResourceReferences.Remove(HeaderAddress + 12);
          break;
        case MalformedRideCar.MissingBodyVisualAndAnimalSpecies:
          source.ResourceReferences.Remove(HeaderAddress + 12);
          source.ResourceReferences.Remove(HeaderAddress + 316);
          break;
        case MalformedRideCar.WrongBodyVisualTag:
          source.ResourceReferences[HeaderAddress + 12] =
            new OvlSymbolReference("Body:shs", owner);
          break;
        case MalformedRideCar.WrongBodyVisualOwner:
          source.ResourceReferences[HeaderAddress + 12] = new OvlSymbolReference(
            "Body:svd",
            owner with { StructAddress = owner.StructAddress + 1 });
          break;
        case MalformedRideCar.ConflictingBodyVisualRelocation:
          source.Relocations[HeaderAddress + 12] = 123_456;
          break;
        case MalformedRideCar.UnexpectedOwnedReference:
          source.ResourceReferences[HeaderAddress + 212] =
            new OvlSymbolReference("Extra:svd", owner);
          break;
        case MalformedRideCar.UnprovenDirectReference:
          source.ResourceReferences.Remove(HeaderAddress + 20);
          WriteUInt32(20, 123_456);
          break;
        case MalformedRideCar.ExcessiveSeatTypeCount:
          WriteUInt32(156, 65_537);
          break;
        case MalformedRideCar.MissingSeatTypeRelocation:
          WriteUInt32(156, 2);
          source.Relocations.Remove(HeaderAddress + 160);
          WriteUInt32(160, 0);
          break;
        case MalformedRideCar.NonFiniteBaseSetting:
          WriteSingle(16, float.NaN);
          break;
        case MalformedRideCar.NonFiniteExpansionSetting:
          WriteSingle(240, float.PositiveInfinity);
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

    private void AddSeatTypes(params uint[] values) {
      var address = source.AddUInt32Array(values);
      WriteUInt32(156, Convert.ToUInt32(values.Length));
      WriteUInt32(160, address);
      source.Relocations.Add(HeaderAddress + 160, address);
    }

    private void AddReference(int fieldOffset, string symbol) {
      source.ResourceReferences.Add(
        HeaderAddress + Convert.ToUInt32(fieldOffset),
        new OvlSymbolReference(symbol, owner));
    }

    private void WriteUInt16(int offset, ushort value) =>
      BitConverter.GetBytes(value).CopyTo(header, offset);

    private void WriteUInt32(int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(header, offset);

    private void WriteInt32(int offset, int value) =>
      BitConverter.GetBytes(value).CopyTo(header, offset);

    private void WriteSingle(int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(header, offset);
  }

  private sealed class FakeRideCarDataSource : IRideCarDataSource {
    private readonly Dictionary<uint, byte[]> segments = [];
    private uint nextDataAddress = 10_000;

    public Dictionary<uint, OvlSymbolReference> ResourceReferences { get; } = [];
    IReadOnlyDictionary<uint, OvlSymbolReference> IRideCarDataSource.ResourceReferences =>
      ResourceReferences;
    public Dictionary<uint, uint> Relocations { get; } = [];

    public void AddBytes(uint address, byte[] bytes) => segments.Add(address, bytes);

    public void ReplaceBytes(uint address, byte[] bytes) => segments[address] = bytes;

    public uint AddString(string value) {
      var address = nextDataAddress;
      var bytes = Encoding.ASCII.GetBytes(value + "\0");
      segments.Add(address, bytes);
      nextDataAddress += Convert.ToUInt32(bytes.Length + 16);
      return address;
    }

    public uint AddUInt32Array(IReadOnlyList<uint> values) {
      var address = nextDataAddress;
      var bytes = new byte[checked(values.Count * sizeof(uint))];
      foreach (var index in Enumerable.Range(0, values.Count))
        BitConverter.GetBytes(values[index]).CopyTo(bytes, checked(index * sizeof(uint)));
      segments.Add(address, bytes);
      nextDataAddress += Convert.ToUInt32(bytes.Length + 16);
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

public enum MalformedRideCar {
  TruncatedVanilla,
  UnsupportedVersion,
  TruncatedWild,
  MissingNameRelocation,
  MissingBodyVisual,
  MissingBodyVisualAndAnimalSpecies,
  WrongBodyVisualTag,
  WrongBodyVisualOwner,
  ConflictingBodyVisualRelocation,
  UnexpectedOwnedReference,
  UnprovenDirectReference,
  ExcessiveSeatTypeCount,
  MissingSeatTypeRelocation,
  NonFiniteBaseSetting,
  NonFiniteExpansionSetting,
}
