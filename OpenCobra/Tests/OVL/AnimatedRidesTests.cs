using System.Text;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class AnimatedRidesTests {
  [Test]
  public void Decode_VanillaReadsAttractionRideOptionsAndReferences() {
    var fixture = new AnimatedRideFixture(AnimatedRideVersion.Vanilla);

    var ride = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(ride.Name, Is.EqualTo("synthetic"));
      Assert.That(ride.Version, Is.EqualTo(AnimatedRideVersion.Vanilla));
      Assert.That(ride.Attraction.Type, Is.EqualTo(4));
      Assert.That(ride.Attraction.NameTextReference, Is.EqualTo("RideName:txt"));
      Assert.That(ride.Attraction.DescriptionTextReference,
        Is.EqualTo("RideDescription:txt"));
      Assert.That(ride.Attraction.IconReference, Is.EqualTo(":gsi"));
      Assert.That(ride.Attraction.LoopSplineReference, Is.EqualTo(":spl"));
      Assert.That(ride.Attraction.PathSplineReferences,
        Is.EqualTo(new[] { "GuestPath:spl", "MechanicPath:spl" }));
      Assert.That(ride.Attraction.BaseUpkeep, Is.EqualTo(4960));
      Assert.That(ride.Attraction.MaximumHeight, Is.EqualTo(16));
      Assert.That(ride.Ride.Attractivity, Is.EqualTo(45));
      Assert.That(ride.Ride.Seating, Is.EqualTo(12));
      Assert.That(ride.Ride.Options, Has.Count.EqualTo(2));
      Assert.That(ride.Ride.Options[0].Type, Is.EqualTo(1));
      Assert.That(ride.Ride.Options[0].PayloadWords, Is.EqualTo(new uint[] { 16_777_218 }));
      Assert.That(ride.Ride.Options[1].Type, Is.EqualTo(8));
      Assert.That(ride.Ride.Options[1].PayloadWords, Has.Count.EqualTo(5));
      Assert.That(ride.Ride.MinimumCircuits, Is.EqualTo(1));
      Assert.That(ride.Ride.MaximumCircuits, Is.EqualTo(-1));
      Assert.That(ride.Ride.EntryFee, Is.EqualTo(100));
      Assert.That(ride.Ride.StationLimits, Is.Empty);
      Assert.That(ride.SceneryItemReference, Is.EqualTo("SyntheticRide:sid"));
      Assert.That(ride.ShowItems, Is.Empty);
    }
  }

  [TestCase(AnimatedRideVersion.Soaked, 512u, 1u, null)]
  [TestCase(AnimatedRideVersion.Wild, 768u, 2u, 7u)]
  public void Decode_ExpansionReadsNestedRideStationAndShowItems(
    AnimatedRideVersion version,
    uint type,
    uint addonAssociation,
    uint? uiDeactivation
  ) {
    var fixture = new AnimatedRideFixture(version);

    var ride = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(ride.Version, Is.EqualTo(version));
      Assert.That(ride.Attraction.Type, Is.EqualTo(type + 4));
      Assert.That(ride.Attraction.PathSplineReferences,
        Is.EqualTo(new[] { "ExpansionPath:spl" }));
      Assert.That(ride.Attraction.AddonAssociation, Is.EqualTo(addonAssociation));
      Assert.That(ride.Attraction.UiDeactivation, Is.EqualTo(uiDeactivation));
      Assert.That(ride.Ride.Attractivity, Is.EqualTo(55));
      Assert.That(ride.Ride.Seating, Is.EqualTo(9));
      Assert.That(ride.Ride.Options, Has.Count.EqualTo(2));
      Assert.That(ride.Ride.StationLimits, Is.EqualTo(new[] {
        new AnimatedRideStationLimit(10, 6, 2, 50),
      }));
      Assert.That(ride.SceneryItemReference, Is.EqualTo("SyntheticRide:sid"));
      Assert.That(ride.ShowItems, Is.EqualTo(new[] {
        new AnimatedRideShowItem(3, "show_idle"),
      }));
    }
  }

  [Test]
  public void Decode_EnforcesAggregateByteBudget() {
    var fixture = new AnimatedRideFixture(AnimatedRideVersion.Vanilla);

    Assert.Throws<InvalidDataException>(new Action(() =>
      fixture.Decode(new AnimatedRideDecodeLimits(4, 1_000))));
  }

  [TestCase(MalformedAnimatedRide.TruncatedHeader)]
  [TestCase(MalformedAnimatedRide.RelocatedVersionMarker)]
  [TestCase(MalformedAnimatedRide.UnsupportedVanillaType)]
  [TestCase(MalformedAnimatedRide.HeaderRedirectedToCommon)]
  [TestCase(MalformedAnimatedRide.VanillaOptionsInSecondUnique)]
  [TestCase(MalformedAnimatedRide.OptionsInCommonStringBlock)]
  [TestCase(MalformedAnimatedRide.WrongUniqueBlock)]
  [TestCase(MalformedAnimatedRide.WrongCommonBlock)]
  [TestCase(MalformedAnimatedRide.AliasedLoader)]
  [TestCase(MalformedAnimatedRide.MissingLoaderDataRelocation)]
  [TestCase(MalformedAnimatedRide.WrongLoaderDataRelocation)]
  [TestCase(MalformedAnimatedRide.LoaderDataReferenceConflict)]
  [TestCase(MalformedAnimatedRide.MissingRideRelocation)]
  [TestCase(MalformedAnimatedRide.NonContiguousRide)]
  [TestCase(MalformedAnimatedRide.MissingAttractionRelocation)]
  [TestCase(MalformedAnimatedRide.UnsupportedAddon)]
  [TestCase(MalformedAnimatedRide.AddonAssociationOutOfRange)]
  [TestCase(MalformedAnimatedRide.PathCountCeiling)]
  [TestCase(MalformedAnimatedRide.MissingPathRelocation)]
  [TestCase(MalformedAnimatedRide.WrongTextTag)]
  [TestCase(MalformedAnimatedRide.WrongReferenceOwner)]
  [TestCase(MalformedAnimatedRide.ConflictingReferenceRelocation)]
  [TestCase(MalformedAnimatedRide.UnprovenOptionSentinel)]
  [TestCase(MalformedAnimatedRide.UnsupportedOption)]
  [TestCase(MalformedAnimatedRide.NonFiniteOption)]
  [TestCase(MalformedAnimatedRide.UnexpectedScalarRelocation)]
  [TestCase(MalformedAnimatedRide.StationCountCeiling)]
  [TestCase(MalformedAnimatedRide.MissingStationRelocation)]
  [TestCase(MalformedAnimatedRide.ShowCountCeiling)]
  [TestCase(MalformedAnimatedRide.MissingShowRelocation)]
  [TestCase(MalformedAnimatedRide.UnterminatedShowName)]
  [TestCase(MalformedAnimatedRide.UnexpectedOwnedReference)]
  public void Decode_RejectsMalformedOrUnprovenLayout(MalformedAnimatedRide malformed) {
    var version = malformed switch {
      MalformedAnimatedRide.TruncatedHeader or
      MalformedAnimatedRide.UnsupportedVanillaType or
      MalformedAnimatedRide.VanillaOptionsInSecondUnique => AnimatedRideVersion.Vanilla,
      _ => AnimatedRideVersion.Wild,
    };
    var fixture = new AnimatedRideFixture(version);
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledRoundupArchiveDecodesAnimatedRide() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      rct3Path,
      "Style",
      "Vanilla",
      "Rides",
      "Roundup",
      "roundup.common.ovl");
    Assert.That(path, Does.Exist, $"Installed Roundup OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var rides = AnimatedRides.Extract(ovl);

    TestContext.Progress.WriteLine(
      $"Installed ANR evidence: rides={rides.Count}, " +
      $"names={string.Join(",", rides.Select(ride => ride.Name))}, " +
      $"versions={string.Join(",", rides.Select(ride => ride.Version))}, " +
      $"sids={string.Join(",", rides.Select(ride => ride.SceneryItemReference))}, " +
      $"addons={string.Join(",", rides.Select(ride => ride.Attraction.AddonAssociation))}, " +
      $"options={string.Join(",", rides.Select(ride => ride.Ride.Options.Count))}, " +
      $"stations={string.Join(",", rides.Select(ride => ride.Ride.StationLimits.Count))}, " +
      $"shows={string.Join(",", rides.Select(ride => ride.ShowItems.Count))}");
    var ride = rides.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(ride.Name, Is.EqualTo("roundup"));
      Assert.That(ride.Version, Is.EqualTo(AnimatedRideVersion.Wild));
      Assert.That(ride.SceneryItemReference, Is.EqualTo("Roundup:sid"));
      Assert.That(ride.Attraction.NameTextReference, Does.EndWith(":txt"));
      Assert.That(ride.Attraction.DescriptionTextReference, Does.EndWith(":txt"));
      Assert.That(ride.Attraction.AddonAssociation, Is.Zero);
      Assert.That(ride.Ride.Options, Has.Count.EqualTo(9));
      Assert.That(ride.Ride.StationLimits, Is.Empty);
      Assert.That(ride.ShowItems, Is.Empty);
    }
  }

  private sealed class AnimatedRideFixture {
    private const uint HeaderAddress = 1_000;
    private const uint OptionsAddress = 5_000;
    private const uint ShowStringAddress = 50_000;
    private const string SourcePath = "fixture.unique.ovl";

    private readonly byte[] attraction;
    private readonly uint attractionAddress;
    private readonly byte[] header;
    private readonly OvlLoaderEntry owner = new("anr", HeaderAddress, SourcePath, 900);
    private readonly byte[] options = new byte[44];
    private readonly uint pathArrayAddress;
    private readonly byte[] ride;
    private readonly uint rideAddress;
    private readonly byte[] showName = Encoding.ASCII.GetBytes("show_idle\0");
    private readonly FakeAnimatedRideDataSource source = new();
    private readonly AnimatedRideVersion version;

    public AnimatedRideFixture(AnimatedRideVersion version) {
      this.version = version;
      source.DataRegionLoaders.Add(owner);
      source.Relocations.Add(owner.StructAddress + 4, owner.DataAddress);
      header = new byte[version == AnimatedRideVersion.Vanilla ? 100 : 44];
      rideAddress = version == AnimatedRideVersion.Vanilla
        ? HeaderAddress + 56
        : HeaderAddress + 44;
      var rideSize = version switch {
        AnimatedRideVersion.Vanilla => 24,
        AnimatedRideVersion.Soaked => 60,
        AnimatedRideVersion.Wild => 68,
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
      };
      ride = version == AnimatedRideVersion.Vanilla
        ? header.AsSpan(56, rideSize).ToArray()
        : new byte[rideSize];
      var attractionSize = version switch {
        AnimatedRideVersion.Vanilla => 56,
        AnimatedRideVersion.Soaked => 64,
        AnimatedRideVersion.Wild => 68,
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
      };
      attractionAddress = version == AnimatedRideVersion.Vanilla
        ? HeaderAddress
        : rideAddress + Convert.ToUInt32(rideSize);
      attraction = version == AnimatedRideVersion.Vanilla
        ? header.AsSpan(0, attractionSize).ToArray()
        : new byte[attractionSize];
      pathArrayAddress = version == AnimatedRideVersion.Vanilla
        ? HeaderAddress + Convert.ToUInt32(header.Length)
        : attractionAddress + Convert.ToUInt32(attraction.Length);

      AddOptions();
      if (version == AnimatedRideVersion.Vanilla)
        AddVanilla();
      else
        AddExpansion();
    }

    public AnimatedRide Decode() => AnimatedRides.Decode("synthetic", owner, source);

    public AnimatedRide Decode(AnimatedRideDecodeLimits limits) =>
      AnimatedRides.Decode("synthetic", owner, source, limits);

    public void MakeMalformed(MalformedAnimatedRide malformed) {
      switch (malformed) {
        case MalformedAnimatedRide.TruncatedHeader:
          source.ReplaceBytes(HeaderAddress, header[..20]);
          break;
        case MalformedAnimatedRide.RelocatedVersionMarker:
          source.Relocations[HeaderAddress] = 123_456;
          break;
        case MalformedAnimatedRide.UnsupportedVanillaType:
          WriteUInt32(header, 0, 512);
          break;
        case MalformedAnimatedRide.HeaderRedirectedToCommon:
          source.SetBlock(HeaderAddress, source.CommonBlock);
          break;
        case MalformedAnimatedRide.VanillaOptionsInSecondUnique:
          source.SetBlock(OptionsAddress, source.SecondUniqueBlock);
          break;
        case MalformedAnimatedRide.OptionsInCommonStringBlock:
          source.SetBlock(OptionsAddress, source.StringBlock);
          break;
        case MalformedAnimatedRide.WrongUniqueBlock:
          source.SetBlock(attractionAddress, source.SecondUniqueBlock);
          break;
        case MalformedAnimatedRide.WrongCommonBlock:
          source.SetBlock(
            OptionsAddress + Convert.ToUInt32(options.Length),
            source.SecondCommonBlock);
          break;
        case MalformedAnimatedRide.AliasedLoader:
          source.DataRegionLoaders.Add(
            owner with { StructAddress = owner.StructAddress + 20 });
          break;
        case MalformedAnimatedRide.MissingLoaderDataRelocation:
          source.Relocations.Remove(owner.StructAddress + 4);
          break;
        case MalformedAnimatedRide.WrongLoaderDataRelocation:
          source.Relocations[owner.StructAddress + 4] = owner.DataAddress + 4;
          break;
        case MalformedAnimatedRide.LoaderDataReferenceConflict:
          source.ResourceReferences.Add(
            owner.StructAddress + 4,
            new OvlSymbolReference("Unexpected:spl", owner));
          break;
        case MalformedAnimatedRide.MissingRideRelocation:
          source.Relocations.Remove(HeaderAddress + 20);
          break;
        case MalformedAnimatedRide.NonContiguousRide:
          SetPointer(header, 20, HeaderAddress, rideAddress + 4);
          break;
        case MalformedAnimatedRide.MissingAttractionRelocation:
          source.Relocations.Remove(rideAddress + 24);
          break;
        case MalformedAnimatedRide.UnsupportedAddon:
          WriteUInt32(attraction, 0, 256);
          break;
        case MalformedAnimatedRide.AddonAssociationOutOfRange:
          WriteUInt32(attraction, 56, 3);
          break;
        case MalformedAnimatedRide.PathCountCeiling:
          WriteUInt32(attraction, 40, 65_537);
          break;
        case MalformedAnimatedRide.MissingPathRelocation:
          source.Relocations.Remove(attractionAddress + 44);
          break;
        case MalformedAnimatedRide.WrongTextTag:
          source.ResourceReferences[attractionAddress + 4] =
            new OvlSymbolReference("RideName:shs", owner);
          break;
        case MalformedAnimatedRide.WrongReferenceOwner:
          source.ResourceReferences[attractionAddress + 4] = new OvlSymbolReference(
            "RideName:txt", owner with { StructAddress = owner.StructAddress + 1 });
          break;
        case MalformedAnimatedRide.ConflictingReferenceRelocation:
          source.Relocations[attractionAddress + 4] = 123_456;
          break;
        case MalformedAnimatedRide.UnprovenOptionSentinel:
          WriteUInt32(options, 8, 123_456);
          break;
        case MalformedAnimatedRide.UnsupportedOption:
          WriteUInt32(options, 12, 13);
          break;
        case MalformedAnimatedRide.NonFiniteOption:
          WriteSingle(options, 24, float.NaN);
          break;
        case MalformedAnimatedRide.UnexpectedScalarRelocation:
          source.Relocations[attractionAddress + 32] = 4960;
          break;
        case MalformedAnimatedRide.StationCountCeiling:
          WriteUInt32(ride, 32, 65_537);
          break;
        case MalformedAnimatedRide.MissingStationRelocation:
          source.Relocations.Remove(rideAddress + 36);
          break;
        case MalformedAnimatedRide.ShowCountCeiling:
          WriteUInt32(header, 36, 65_537);
          break;
        case MalformedAnimatedRide.MissingShowRelocation:
          source.Relocations.Remove(HeaderAddress + 40);
          break;
        case MalformedAnimatedRide.UnterminatedShowName:
          source.ReplaceBytes(ShowStringAddress, Encoding.ASCII.GetBytes("show_idle"));
          break;
        case MalformedAnimatedRide.UnexpectedOwnedReference:
          source.ResourceReferences.Add(
            HeaderAddress + 28,
            new OvlSymbolReference("Unexpected:spl", owner));
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed));
      }
    }

    private void AddVanilla() {
      WriteUInt32(header, 0, 4);
      WriteUInt32(header, 32, 4960);
      WriteUInt32(header, 40, 2);
      AddPointer(header, 44, HeaderAddress, pathArrayAddress);
      WriteUInt32(header, 48, 3);
      WriteInt32(header, 52, 16);
      AddAttractionReferences(HeaderAddress);
      var pathPointers = new byte[8];
      source.AddBytes(pathArrayAddress, pathPointers, source.UniqueBlock);
      source.ResourceReferences.Add(
        pathArrayAddress,
        new OvlSymbolReference("GuestPath:spl", owner));
      source.ResourceReferences.Add(
        pathArrayAddress + 4,
        new OvlSymbolReference("MechanicPath:spl", owner));

      WriteUInt32(header, 56, 45);
      WriteUInt32(header, 60, 12);
      AddPointer(header, 64, HeaderAddress, OptionsAddress);
      WriteUInt32(header, 68, 1);
      WriteInt32(header, 72, -1);
      WriteUInt32(header, 76, 100);
      AddReference(HeaderAddress + 80, "SyntheticRide:sid");
      WriteUInt32(header, 96, 10);
      source.AddBytes(HeaderAddress, header, source.UniqueBlock);
    }

    private void AddExpansion() {
      WriteUInt32(header, 0, uint.MaxValue);
      AddPointer(header, 20, HeaderAddress, rideAddress);
      AddReference(HeaderAddress + 24, "SyntheticRide:sid");
      WriteUInt32(header, 36, 1);

      WriteUInt32(ride, 0, uint.MaxValue);
      WriteUInt32(ride, 4, 9);
      AddPointer(ride, 8, rideAddress, OptionsAddress);
      WriteUInt32(ride, 12, 1);
      WriteInt32(ride, 16, -1);
      WriteUInt32(ride, 20, 100);
      AddPointer(ride, 24, rideAddress, attractionAddress);
      WriteUInt32(ride, 28, 55);
      WriteUInt32(ride, 32, 1);
      var stationAddress = OptionsAddress + Convert.ToUInt32(options.Length);
      AddPointer(ride, 36, rideAddress, stationAddress);
      WriteUInt32(ride, 40, 3);
      WriteUInt32(ride, 44, 3);
      WriteInt32(ride, 48, -2);
      WriteInt32(ride, 52, -2);
      WriteInt32(ride, 56, -2);
      if (version == AnimatedRideVersion.Wild) {
        WriteUInt32(ride, 60, 1);
        WriteUInt32(ride, 64, 1);
      }
      source.AddBytes(rideAddress, ride, source.UniqueBlock);

      WriteUInt32(attraction, 0,
        version == AnimatedRideVersion.Soaked ? 516u : 772u);
      WriteUInt32(attraction, 32, 4960);
      WriteUInt32(attraction, 40, 1);
      AddPointer(attraction, 44, attractionAddress, pathArrayAddress);
      WriteUInt32(attraction, 48, 3);
      WriteInt32(attraction, 52, 16);
      WriteUInt32(attraction, 56,
        version == AnimatedRideVersion.Soaked ? 1u : 2u);
      if (version == AnimatedRideVersion.Wild) WriteUInt32(attraction, 64, 7);
      AddAttractionReferences(attractionAddress);
      source.AddBytes(attractionAddress, attraction, source.UniqueBlock);
      var pathPointer = new byte[4];
      source.AddBytes(pathArrayAddress, pathPointer, source.UniqueBlock);
      source.ResourceReferences.Add(
        pathArrayAddress,
        new OvlSymbolReference("ExpansionPath:spl", owner));

      var station = new byte[16];
      WriteInt32(station, 0, 10);
      WriteInt32(station, 4, 6);
      WriteInt32(station, 8, 2);
      WriteUInt32(station, 12, 50);
      source.AddBytes(stationAddress, station, source.CommonBlock);

      var showAddress = stationAddress + Convert.ToUInt32(station.Length);
      AddPointer(header, 40, HeaderAddress, showAddress);
      var show = new byte[8];
      WriteUInt32(show, 0, 3);
      AddPointer(show, 4, showAddress, ShowStringAddress);
      source.AddBytes(showAddress, show, source.CommonBlock);
      source.AddBytes(ShowStringAddress, showName, source.StringBlock);
      source.AddBytes(HeaderAddress, header, source.UniqueBlock);
    }

    private void AddOptions() {
      var firstOptionAddress = OptionsAddress + 12;
      var secondOptionAddress = firstOptionAddress + 8;
      AddPointer(options, 0, OptionsAddress, firstOptionAddress);
      AddPointer(options, 4, OptionsAddress, secondOptionAddress);
      WriteUInt32(options, 12, 1);
      WriteUInt32(options, 16, 16_777_218);
      WriteUInt32(options, 20, 8);
      WriteSingle(options, 24, 5f);
      WriteSingle(options, 28, 6f);
      WriteSingle(options, 32, 7f);
      WriteUInt32(options, 36, 16_777_218);
      WriteSingle(options, 40, 1.5f);
      source.AddBytes(OptionsAddress, options, source.CommonBlock);
    }

    private void AddAttractionReferences(uint address) {
      AddReference(address + 4, "RideName:txt");
      AddReference(address + 8, "RideDescription:txt");
      AddReference(address + 12, ":gsi");
      AddReference(address + 36, ":spl");
    }

    private void AddReference(uint fieldAddress, string symbol) =>
      source.ResourceReferences.Add(fieldAddress, new OvlSymbolReference(symbol, owner));

    private void AddPointer(byte[] target, int offset, uint targetAddress, uint value) {
      WriteUInt32(target, offset, value);
      source.Relocations.Add(targetAddress + Convert.ToUInt32(offset), value);
    }

    private void SetPointer(byte[] target, int offset, uint targetAddress, uint value) {
      WriteUInt32(target, offset, value);
      source.Relocations[targetAddress + Convert.ToUInt32(offset)] = value;
    }

    private static void WriteUInt32(byte[] target, int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(target, offset);

    private static void WriteInt32(byte[] target, int offset, int value) =>
      BitConverter.GetBytes(value).CopyTo(target, offset);

    private static void WriteSingle(byte[] target, int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(target, offset);
  }

  private sealed class FakeAnimatedRideDataSource : IAnimatedRideDataSource {
    private readonly Dictionary<uint, OvlDataBlockIdentity> blockIdentities = [];
    private readonly Dictionary<uint, byte[]> segments = [];

    public OvlDataBlockIdentity CommonBlock { get; } =
      new(new object(), "fixture.common.ovl", 2);
    public List<OvlLoaderEntry> DataRegionLoaders { get; } = [];
    public Dictionary<uint, OvlSymbolReference> ResourceReferences { get; } = [];
    IReadOnlyDictionary<uint, OvlSymbolReference> IAnimatedRideDataSource.ResourceReferences =>
      ResourceReferences;
    public Dictionary<uint, uint> Relocations { get; } = [];
    public OvlDataBlockIdentity SecondCommonBlock { get; } =
      new(new object(), "fixture.common.ovl", 2);
    public OvlDataBlockIdentity SecondUniqueBlock { get; } =
      new(new object(), "fixture.unique.ovl", 2);
    public OvlDataBlockIdentity StringBlock { get; } =
      new(new object(), "fixture.common.ovl", 0);
    public OvlDataBlockIdentity UniqueBlock { get; } =
      new(new object(), "fixture.unique.ovl", 2);

    public void AddBytes(
      uint address,
      byte[] bytes,
      OvlDataBlockIdentity blockIdentity
    ) {
      segments.Add(address, bytes);
      blockIdentities.Add(address, blockIdentity);
    }

    public void ReplaceBytes(uint address, byte[] bytes) => segments[address] = bytes;

    public void SetBlock(uint address, OvlDataBlockIdentity blockIdentity) =>
      blockIdentities[address] = blockIdentity;

    public IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner) =>
      DataRegionLoaders;

    public bool TryGetDataBlock(
      uint address,
      int length,
      out OvlDataBlockIdentity block
    ) {
      if (length < 0 ||
          !TryFindSegment(address, out var segmentAddress, out var segment, out var offset) ||
          length > segment.Length - offset) {
        block = null!;
        return false;
      }
      block = blockIdentities[segmentAddress];
      return true;
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
      if (!TryFindSegment(address, out _, out var segment, out var offset)) {
        length = 0;
        return false;
      }
      var available = Math.Min(maximumLength, segment.Length - offset);
      var end = Array.IndexOf(segment, Convert.ToByte(0), offset, available);
      length = end < 0 ? 0 : end - offset;
      return end >= 0;
    }

    public bool TryReadNullTerminatedString(uint address, int maximumLength, out string value) {
      if (!TryFindSegment(address, out _, out var segment, out var offset)) {
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

    private bool TryFindSegment(
      uint address,
      out uint segmentAddress,
      out byte[] segment,
      out int offset
    ) {
      foreach (var candidate in segments) {
        var start = Convert.ToUInt64(candidate.Key);
        var target = Convert.ToUInt64(address);
        var end = start + Convert.ToUInt64(candidate.Value.Length);
        if (target < start || target >= end) continue;
        segmentAddress = candidate.Key;
        segment = candidate.Value;
        offset = Convert.ToInt32(target - start);
        return true;
      }
      segmentAddress = 0;
      segment = [];
      offset = 0;
      return false;
    }
  }
}

public enum MalformedAnimatedRide {
  TruncatedHeader,
  RelocatedVersionMarker,
  UnsupportedVanillaType,
  HeaderRedirectedToCommon,
  VanillaOptionsInSecondUnique,
  OptionsInCommonStringBlock,
  WrongUniqueBlock,
  WrongCommonBlock,
  AliasedLoader,
  MissingLoaderDataRelocation,
  WrongLoaderDataRelocation,
  LoaderDataReferenceConflict,
  MissingRideRelocation,
  NonContiguousRide,
  MissingAttractionRelocation,
  UnsupportedAddon,
  AddonAssociationOutOfRange,
  PathCountCeiling,
  MissingPathRelocation,
  WrongTextTag,
  WrongReferenceOwner,
  ConflictingReferenceRelocation,
  UnprovenOptionSentinel,
  UnsupportedOption,
  NonFiniteOption,
  UnexpectedScalarRelocation,
  StationCountCeiling,
  MissingStationRelocation,
  ShowCountCeiling,
  MissingShowRelocation,
  UnterminatedShowName,
  UnexpectedOwnedReference,
}
