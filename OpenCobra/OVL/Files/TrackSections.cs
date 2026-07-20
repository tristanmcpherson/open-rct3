// Track Sections
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>The three serialized RCT3 track-section layouts.</summary>
public enum TrackSectionVersion : uint {
  Vanilla = 0,
  Soaked = 2,
  Wild = 3,
}

/// <summary>The fixed entry or exit topology fields in <c>TrackSection_V</c>.</summary>
public sealed record TrackSectionEndpoint(
  uint Curve,
  uint Flags,
  uint Unknown,
  uint Slope,
  uint Bank,
  string? Group
);

/// <summary>A left/right pair of exact <c>spl</c> references.</summary>
public sealed record TrackSectionSplinePair(string Left, string Right);

/// <summary>One element from the version-dependent base speed array.</summary>
public sealed record TrackSectionSpeed(
  float Unknown1,
  float Unknown2,
  float Unknown3,
  float? Unknown4,
  float? Unknown5
);

/// <summary>The exact nine, ten, or eleven track-section animation indices.</summary>
public sealed record TrackSectionAnimations(
  int Stopped,
  int Starting,
  int Running,
  int Stopping,
  int HoldAfterTrainStop,
  int HoldLoop,
  int HoldBeforeRelease,
  int HoldLeaving,
  int HoldAfterTrainLeft,
  int? PreStationLeave,
  int? RotatingTowerIdle
);

/// <summary>The base ride-behaviour fields following the speed array pointer.</summary>
public sealed record TrackSectionOptions(
  uint TowerRideBaseFlag,
  float TowerUnknown1,
  float WaterSplash1,
  float WaterSplash2,
  float Reverser,
  float ElevatorTop,
  float Rapids1,
  float Rapids2,
  float Rapids3,
  float Whirlpool1,
  float Whirlpool2,
  uint WaterSplineFlag,
  string? ChairLiftStationEnd
);

/// <summary>The non-reference unknown fields in the fixed Vanilla header.</summary>
public sealed record TrackSectionBaseUnknowns(
  uint Unknown15,
  uint Unknown16,
  uint Unknown17,
  uint Unknown22,
  uint Unknown23,
  uint Unknown24,
  uint Unknown45,
  uint Unknown47,
  uint Unknown53
);

/// <summary>One exact 16-byte <c>RideStationLimit</c> element.</summary>
public sealed record TrackSectionStationLimit(int X, int Z, int Height, uint Flags);

/// <summary>One speed selector and its required left/right spline references.</summary>
public sealed record TrackSectionSpeedSpline(
  float SpeedSelector,
  TrackSectionSplinePair Splines
);

/// <summary>The six construction-group arrays in <c>TrackSection_Sext</c>.</summary>
public sealed record TrackSectionGroups(
  IReadOnlyList<string> IsAtEntry,
  IReadOnlyList<string> IsAtExit,
  IReadOnlyList<string> MustHaveAtEntry,
  IReadOnlyList<string> MustHaveAtExit,
  IReadOnlyList<string> MustNotBeAtEntry,
  IReadOnlyList<string> MustNotBeAtExit
);

/// <summary>The exact Soaked extension shared by Soaked and Wild sections.</summary>
public sealed record TrackSectionExpansion(
  string? LoopSpline,
  IReadOnlyList<string> PathSplines,
  IReadOnlyList<TrackSectionStationLimit> StationLimits,
  float TowerUnknown2,
  float Aquarium,
  string? AutoGroup,
  int GiantFlume,
  uint Unknown68,
  uint Unknown69,
  uint EntryWideFlag,
  uint ExitWideFlag,
  IReadOnlyList<TrackSectionSpeedSpline> SpeedSplines,
  float SlideEndToLiftHill,
  uint SoakedOptions,
  float SpeedSplineValue,
  float Unknown77,
  float Unknown78,
  float Unknown79,
  TrackSectionGroups Groups
);

/// <summary>The exact Wild-only extension.</summary>
public sealed record TrackSectionWild(
  uint SplitterHalf,
  string? SplitterJoinedOther,
  uint RotatorType,
  float AnimalHouse,
  string? AlternateTextLookup,
  float TowerCap1,
  float TowerCap2
);

/// <summary>A decoded RCT3 <c>tks</c> resource.</summary>
public sealed record TrackSection(
  string Name,
  TrackSectionVersion Version,
  string InternalName,
  string SceneryItem,
  TrackSectionEndpoint Entry,
  TrackSectionEndpoint Exit,
  uint SpecialCurves,
  uint Direction,
  TrackSectionSplinePair CarSplines,
  TrackSectionSplinePair JoinSplines,
  TrackSectionSplinePair? ExtraSplines,
  TrackSectionSplinePair? WaterSplines,
  IReadOnlyList<TrackSectionSpeed> Speeds,
  TrackSectionAnimations Animations,
  TrackSectionOptions Options,
  TrackSectionBaseUnknowns Unknowns,
  TrackSectionExpansion? Expansion,
  TrackSectionWild? Wild
);

/// <summary>Decodes the fixed and relocation-backed portions of <c>tks</c> resources.</summary>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/tracksection.h">
/// rct3-importer track-section layouts
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerTKS.cpp">
/// rct3-importer track-section serializer
/// </seealso>
public static class TrackSections {
  private const int VanillaSize = 228;
  private const int SoakedSize = 364;
  private const int WildSize = 392;
  private const uint ExtendedStructureFlag = 4_194_304;
  private const int VanillaSpeedSize = 12;
  private const int ExpansionSpeedSize = 20;
  private const int StationLimitSize = 16;
  private const int SpeedSplineSize = 12;
  private const int MaximumSectionCount = 64 * 1024;
  private const uint MaximumArrayCount = 64 * 1024;
  private const int MaximumResourceCount = 1_000_000;
  private const int MaximumStringBytes = 4 * 1024;

  /// <summary>Decodes every track-section resource from the unique half of an OVL pair.</summary>
  public static IReadOnlyList<TrackSection> Extract(Ovl ovl) =>
    Extract(ovl, TrackSectionDecodeLimits.Default);

  internal static IReadOnlyList<TrackSection> Extract(
    Ovl ovl,
    TrackSectionDecodeLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(ovl);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the TKS decoder limit {MaximumResourceCount}.");

    var context = new DecodeContext(limits);
    var files = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file =>
      file.Type == FileType.TrackSection &&
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))) {
      if (files.Count >= MaximumSectionCount)
        throw Invalid(file.Name,
          $"section count exceeds the decoder limit {MaximumSectionCount}");
      context.ReserveObjects(1, file.Name, "track-section resource index");
      files.Add(file);
    }

    var source = new OvlTrackSectionDataSource(ovl, context);
    var sections = new List<TrackSection>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier TKS at {address}");
      var owner = source.GetTrackSectionLoader(file, address);
      sections.Add(Decode(file.Name, owner, source, context));
    }
    return sections;
  }

  internal static TrackSection Decode(
    string name,
    OvlLoaderEntry owner,
    ITrackSectionDataSource source
  ) => Decode(name, owner, source, new DecodeContext(TrackSectionDecodeLimits.Default));

  internal static TrackSection Decode(
    string name,
    OvlLoaderEntry owner,
    ITrackSectionDataSource source,
    TrackSectionDecodeLimits limits
  ) => Decode(name, owner, source, new DecodeContext(limits));

  private static TrackSection Decode(
    string name,
    OvlLoaderEntry owner,
    ITrackSectionDataSource source,
    DecodeContext context
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.TrackSection)
      throw Invalid(name, $"loader type '{owner.Tag}' is not tks");

    var address = owner.DataAddress;
    var vanilla = ReadExact(source, address, VanillaSize, name, "Vanilla header", context);
    var entryFlags = ReadUInt32(vanilla, 24);
    var exitFlags = ReadUInt32(vanilla, 28);
    // Frontier's endpoint flags may differ. The entry flag is the expansion-layout discriminator.
    var entryExtended = (entryFlags & ExtendedStructureFlag) != 0;

    var version = TrackSectionVersion.Vanilla;
    byte[] expansion = [];
    byte[] wild = [];
    if (entryExtended) {
      expansion = ReadExact(
        source,
        CheckedAdd(address, VanillaSize, name),
        SoakedSize - VanillaSize,
        name,
        "Soaked header",
        context);
      version = ReadUInt32(expansion, 0) switch {
        2 => TrackSectionVersion.Soaked,
        3 => TrackSectionVersion.Wild,
        var value => throw Invalid(name, $"unsupported expansion version {value}"),
      };
      if (version == TrackSectionVersion.Wild)
        wild = ReadExact(
          source,
          CheckedAdd(address, SoakedSize, name),
          WildSize - SoakedSize,
          name,
          "Wild header",
          context);
    }

    var internalName = ReadRequiredString(
      source, address, ReadUInt32(vanilla, 0), name, "internal name", context);
    var expectedReferenceFields = new HashSet<uint>();
    string? ReadHeaderRef(int offset, string tag, bool required, string description) {
      var fieldAddress = CheckedAdd(address, offset, name);
      expectedReferenceFields.Add(fieldAddress);
      return ReadResourceReference(
        source,
        owner,
        fieldAddress,
        ReadUInt32(vanilla, offset),
        tag,
        required,
        name,
        description);
    }

    TrackSectionSplinePair ReadRequiredPair(int leftOffset, int rightOffset, string description) =>
      new(
        ReadHeaderRef(leftOffset, "spl", required: true, $"{description} left")!,
        ReadHeaderRef(rightOffset, "spl", required: true, $"{description} right")!);

    TrackSectionSplinePair? ReadOptionalPair(
      int leftOffset,
      int rightOffset,
      string description
    ) {
      var left = ReadHeaderRef(leftOffset, "spl", required: false, $"{description} left");
      var right = ReadHeaderRef(rightOffset, "spl", required: false, $"{description} right");
      if ((left == null) != (right == null))
        throw Invalid(name, $"{description} has only one side");
      return left == null ? null : new TrackSectionSplinePair(left, right!);
    }

    var sceneryItem = ReadHeaderRef(4, "sid", required: true, "scenery item")!;
    var carSplines = ReadRequiredPair(32, 36, "car splines");
    var joinSplines = ReadRequiredPair(40, 44, "join splines");
    var extraSplines = ReadOptionalPair(48, 52, "extra splines");
    var waterSplineFlag = ReadUInt32(vanilla, 212);
    if (waterSplineFlag > 1)
      throw Invalid(name, $"water-spline flag {waterSplineFlag} is not 0 or 1");
    var waterSplines = ReadOptionalPair(216, 220, "water splines");
    if (waterSplineFlag == 0 && waterSplines != null)
      throw Invalid(name, "water splines are present while their flag is clear");

    var speeds = ReadSpeeds(
      name,
      version,
      source,
      context,
      address,
      vanilla);
    var animations = ReadAnimations(
      name,
      version,
      source,
      context,
      address,
      vanilla);
    var expansionSettings = version == TrackSectionVersion.Vanilla
      ? null
      : ReadExpansion(
        name,
        owner,
        source,
        context,
        address,
        expansion,
        expectedReferenceFields);
    TrackSectionWild? wildSettings = null;
    if (version == TrackSectionVersion.Wild) {
      var joinedFieldAddress = CheckedAdd(address, SoakedSize + 4, name);
      expectedReferenceFields.Add(joinedFieldAddress);
      wildSettings = new TrackSectionWild(
        ReadUInt32(wild, 0),
        ReadResourceReference(
          source,
          owner,
          joinedFieldAddress,
          ReadUInt32(wild, 4),
          "tks",
          required: false,
          name,
          "splitter joined section"),
        ReadUInt32(wild, 8),
        ReadFiniteSingle(wild, 12, name, "Wild animal-house value"),
        ReadOptionalString(
          source,
          CheckedAdd(address, SoakedSize + 16, name),
          ReadUInt32(wild, 16),
          name,
          "Wild alternate text lookup",
          context),
        ReadFiniteSingle(wild, 20, name, "Wild tower-cap value 1"),
        ReadFiniteSingle(wild, 24, name, "Wild tower-cap value 2"));
    }
    ValidateOwnedReferences(name, owner, expectedReferenceFields, source);
    context.ReserveObjects(12, name, "decoded track-section model");

    return new TrackSection(
      name,
      version,
      internalName,
      sceneryItem,
      new TrackSectionEndpoint(
        ReadUInt32(vanilla, 8),
        entryFlags,
        ReadUInt32(vanilla, 68),
        ReadUInt32(vanilla, 72),
        ReadUInt32(vanilla, 76),
        ReadOptionalString(
          source,
          CheckedAdd(address, 80, name),
          ReadUInt32(vanilla, 80),
          name,
          "entry track group",
          context)),
      new TrackSectionEndpoint(
        ReadUInt32(vanilla, 12),
        exitFlags,
        ReadUInt32(vanilla, 96),
        ReadUInt32(vanilla, 100),
        ReadUInt32(vanilla, 104),
        ReadOptionalString(
          source,
          CheckedAdd(address, 108, name),
          ReadUInt32(vanilla, 108),
          name,
          "exit track group",
          context)),
      ReadUInt32(vanilla, 16),
      ReadUInt32(vanilla, 20),
      carSplines,
      joinSplines,
      extraSplines,
      waterSplines,
      speeds,
      animations,
      new TrackSectionOptions(
        ReadUInt32(vanilla, 120),
        ReadFiniteSingle(vanilla, 124, name, "tower unknown 1"),
        ReadFiniteSingle(vanilla, 128, name, "water splash 1"),
        ReadFiniteSingle(vanilla, 132, name, "water splash 2"),
        ReadFiniteSingle(vanilla, 136, name, "reverser value"),
        ReadFiniteSingle(vanilla, 180, name, "elevator-top value"),
        ReadFiniteSingle(vanilla, 188, name, "rapids value 1"),
        ReadFiniteSingle(vanilla, 192, name, "rapids value 2"),
        ReadFiniteSingle(vanilla, 196, name, "rapids value 3"),
        ReadFiniteSingle(vanilla, 200, name, "whirlpool value 1"),
        ReadFiniteSingle(vanilla, 204, name, "whirlpool value 2"),
        waterSplineFlag,
        ReadOptionalString(
          source,
          CheckedAdd(address, 224, name),
          ReadUInt32(vanilla, 224),
          name,
          "chair-lift station end",
          context)),
      new TrackSectionBaseUnknowns(
        ReadUInt32(vanilla, 56),
        ReadUInt32(vanilla, 60),
        ReadUInt32(vanilla, 64),
        ReadUInt32(vanilla, 84),
        ReadUInt32(vanilla, 88),
        ReadUInt32(vanilla, 92),
        ReadUInt32(vanilla, 176),
        ReadUInt32(vanilla, 184),
        ReadUInt32(vanilla, 208)),
      expansionSettings,
      wildSettings);
  }

  private static IReadOnlyList<TrackSectionSpeed> ReadSpeeds(
    string name,
    TrackSectionVersion version,
    ITrackSectionDataSource source,
    DecodeContext context,
    uint address,
    byte[] vanilla
  ) {
    var count = ReadUInt32(vanilla, 112);
    ValidateCount(count, name, "speed");
    var arrayAddress = ReadRelocatedPointer(
      source,
      CheckedAdd(address, 116, name),
      ReadUInt32(vanilla, 116),
      count > 0,
      name,
      "speed array");
    if (count == 0) return [];
    var stride = version == TrackSectionVersion.Vanilla
      ? VanillaSpeedSize
      : ExpansionSpeedSize;
    var bytes = ReadExact(
      source,
      arrayAddress,
      CheckedArrayLength(count, stride, name, "speed array"),
      name,
      "speed array",
      context);
    context.ReserveObjects(count, name, "speed models");
    var values = new TrackSectionSpeed[Convert.ToInt32(count)];
    foreach (var index in Enumerable.Range(0, values.Length)) {
      var offset = checked(index * stride);
      values[index] = new TrackSectionSpeed(
        ReadFiniteSingle(bytes, offset, name, $"speed {index} unknown 1"),
        ReadFiniteSingle(bytes, offset + 4, name, $"speed {index} unknown 2"),
        ReadFiniteSingle(bytes, offset + 8, name, $"speed {index} unknown 3"),
        version == TrackSectionVersion.Vanilla
          ? null
          : ReadFiniteSingle(bytes, offset + 12, name, $"speed {index} unknown 4"),
        version == TrackSectionVersion.Vanilla
          ? null
          : ReadFiniteSingle(bytes, offset + 16, name, $"speed {index} unknown 5"));
    }
    return values;
  }

  private static TrackSectionAnimations ReadAnimations(
    string name,
    TrackSectionVersion version,
    ITrackSectionDataSource source,
    DecodeContext context,
    uint address,
    byte[] vanilla
  ) {
    if (version == TrackSectionVersion.Vanilla)
      return new TrackSectionAnimations(
        ReadInt32(vanilla, 140),
        ReadInt32(vanilla, 144),
        ReadInt32(vanilla, 148),
        ReadInt32(vanilla, 152),
        ReadInt32(vanilla, 156),
        ReadInt32(vanilla, 160),
        ReadInt32(vanilla, 164),
        ReadInt32(vanilla, 168),
        ReadInt32(vanilla, 172),
        null,
        null);

    var count = ReadInt32(vanilla, 140);
    var expectedCount = version == TrackSectionVersion.Soaked ? 10 : 11;
    if (count != expectedCount)
      throw Invalid(name,
        $"animation count {count} does not match {version}'s {expectedCount}-field layout");
    var animationAddress = ReadRelocatedPointer(
      source,
      CheckedAdd(address, 144, name),
      ReadUInt32(vanilla, 144),
      required: true,
      name,
      "animation array");
    var bytes = ReadExact(
      source,
      animationAddress,
      checked(count * sizeof(int)),
      name,
      "animation array",
      context);
    context.ReserveObjects(Convert.ToUInt64(count), name, "animation fields");
    return new TrackSectionAnimations(
      ReadInt32(bytes, 0),
      ReadInt32(bytes, 4),
      ReadInt32(bytes, 8),
      ReadInt32(bytes, 12),
      ReadInt32(bytes, 16),
      ReadInt32(bytes, 20),
      ReadInt32(bytes, 24),
      ReadInt32(bytes, 28),
      ReadInt32(bytes, 32),
      ReadInt32(bytes, 36),
      version == TrackSectionVersion.Wild ? ReadInt32(bytes, 40) : null);
  }

  private static TrackSectionExpansion ReadExpansion(
    string name,
    OvlLoaderEntry owner,
    ITrackSectionDataSource source,
    DecodeContext context,
    uint address,
    byte[] expansion,
    ISet<uint> expectedReferenceFields
  ) {
    var expansionAddress = CheckedAdd(address, VanillaSize, name);
    var loopFieldAddress = CheckedAdd(expansionAddress, 4, name);
    expectedReferenceFields.Add(loopFieldAddress);
    var loopSpline = ReadResourceReference(
      source,
      owner,
      loopFieldAddress,
      ReadUInt32(expansion, 4),
      "spl",
      required: false,
      name,
      "loop spline");
    var pathSplines = ReadReferenceArray(
      name,
      owner,
      source,
      context,
      CheckedAdd(expansionAddress, 12, name),
      ReadUInt32(expansion, 12),
      ReadUInt32(expansion, 8),
      "spl",
      "path splines",
      expectedReferenceFields);
    var stationLimits = ReadStationLimits(
      name,
      source,
      context,
      CheckedAdd(expansionAddress, 20, name),
      ReadUInt32(expansion, 20),
      ReadUInt32(expansion, 16));
    var speedSplines = ReadSpeedSplines(
      name,
      owner,
      source,
      context,
      CheckedAdd(expansionAddress, 60, name),
      ReadUInt32(expansion, 60),
      ReadUInt32(expansion, 56),
      expectedReferenceFields);
    var groups = new TrackSectionGroups(
      ReadStringArray(name, source, context, expansionAddress, expansion, 88, 92,
        "groups at entry"),
      ReadStringArray(name, source, context, expansionAddress, expansion, 96, 100,
        "groups at exit"),
      ReadStringArray(name, source, context, expansionAddress, expansion, 104, 108,
        "required groups at entry"),
      ReadStringArray(name, source, context, expansionAddress, expansion, 112, 116,
        "required groups at exit"),
      ReadStringArray(name, source, context, expansionAddress, expansion, 120, 124,
        "forbidden groups at entry"),
      ReadStringArray(name, source, context, expansionAddress, expansion, 128, 132,
        "forbidden groups at exit"));

    return new TrackSectionExpansion(
      loopSpline,
      pathSplines,
      stationLimits,
      ReadFiniteSingle(expansion, 24, name, "expansion tower unknown 2"),
      ReadFiniteSingle(expansion, 28, name, "expansion aquarium value"),
      ReadOptionalString(
        source,
        CheckedAdd(expansionAddress, 32, name),
        ReadUInt32(expansion, 32),
        name,
        "auto group",
        context),
      ReadInt32(expansion, 36),
      ReadUInt32(expansion, 40),
      ReadUInt32(expansion, 44),
      ReadUInt32(expansion, 48),
      ReadUInt32(expansion, 52),
      speedSplines,
      ReadFiniteSingle(expansion, 64, name, "slide-end-to-lift-hill value"),
      ReadUInt32(expansion, 68),
      ReadFiniteSingle(expansion, 72, name, "speed-spline value"),
      ReadFiniteSingle(expansion, 76, name, "expansion unknown 77"),
      ReadFiniteSingle(expansion, 80, name, "expansion unknown 78"),
      ReadFiniteSingle(expansion, 84, name, "expansion unknown 79"),
      groups);
  }

  private static IReadOnlyList<string> ReadReferenceArray(
    string name,
    OvlLoaderEntry owner,
    ITrackSectionDataSource source,
    DecodeContext context,
    uint fieldAddress,
    uint storedPointer,
    uint count,
    string tag,
    string description,
    ISet<uint> expectedReferenceFields
  ) {
    ValidateCount(count, name, description);
    var arrayAddress = ReadRelocatedPointer(
      source, fieldAddress, storedPointer, count > 0, name, description);
    if (count == 0) return [];
    var bytes = ReadExact(
      source,
      arrayAddress,
      CheckedArrayLength(count, 4, name, description),
      name,
      description,
      context);
    context.ReserveObjects(count, name, description);
    var values = new string[Convert.ToInt32(count)];
    foreach (var index in Enumerable.Range(0, values.Length)) {
      var offset = checked(index * 4);
      var slotAddress = CheckedAdd(arrayAddress, offset, name);
      expectedReferenceFields.Add(slotAddress);
      values[index] = ReadResourceReference(
        source,
        owner,
        slotAddress,
        ReadUInt32(bytes, offset),
        tag,
        required: true,
        name,
        $"{description} entry {index}")!;
    }
    return values;
  }

  private static IReadOnlyList<TrackSectionStationLimit> ReadStationLimits(
    string name,
    ITrackSectionDataSource source,
    DecodeContext context,
    uint fieldAddress,
    uint storedPointer,
    uint count
  ) {
    ValidateCount(count, name, "station limits");
    var arrayAddress = ReadRelocatedPointer(
      source, fieldAddress, storedPointer, count > 0, name, "station-limit array");
    if (count == 0) return [];
    var bytes = ReadExact(
      source,
      arrayAddress,
      CheckedArrayLength(count, StationLimitSize, name, "station-limit array"),
      name,
      "station-limit array",
      context);
    context.ReserveObjects(count, name, "station-limit models");
    var values = new TrackSectionStationLimit[Convert.ToInt32(count)];
    foreach (var index in Enumerable.Range(0, values.Length)) {
      var offset = checked(index * StationLimitSize);
      values[index] = new TrackSectionStationLimit(
        ReadInt32(bytes, offset),
        ReadInt32(bytes, offset + 4),
        ReadInt32(bytes, offset + 8),
        ReadUInt32(bytes, offset + 12));
    }
    return values;
  }

  private static IReadOnlyList<TrackSectionSpeedSpline> ReadSpeedSplines(
    string name,
    OvlLoaderEntry owner,
    ITrackSectionDataSource source,
    DecodeContext context,
    uint fieldAddress,
    uint storedPointer,
    uint count,
    ISet<uint> expectedReferenceFields
  ) {
    ValidateCount(count, name, "speed splines");
    var arrayAddress = ReadRelocatedPointer(
      source, fieldAddress, storedPointer, count > 0, name, "speed-spline array");
    if (count == 0) return [];
    var bytes = ReadExact(
      source,
      arrayAddress,
      CheckedArrayLength(count, SpeedSplineSize, name, "speed-spline array"),
      name,
      "speed-spline array",
      context);
    context.ReserveObjects(count, name, "speed-spline models");
    var values = new TrackSectionSpeedSpline[Convert.ToInt32(count)];
    foreach (var index in Enumerable.Range(0, values.Length)) {
      var offset = checked(index * SpeedSplineSize);
      var leftFieldAddress = CheckedAdd(arrayAddress, offset + 4, name);
      var rightFieldAddress = CheckedAdd(arrayAddress, offset + 8, name);
      expectedReferenceFields.Add(leftFieldAddress);
      expectedReferenceFields.Add(rightFieldAddress);
      values[index] = new TrackSectionSpeedSpline(
        ReadFiniteSingle(bytes, offset, name, $"speed-spline {index} selector"),
        new TrackSectionSplinePair(
          ReadResourceReference(
            source,
            owner,
            leftFieldAddress,
            ReadUInt32(bytes, offset + 4),
            "spl",
            required: true,
            name,
            $"speed-spline {index} left")!,
          ReadResourceReference(
            source,
            owner,
            rightFieldAddress,
            ReadUInt32(bytes, offset + 8),
            "spl",
            required: true,
            name,
            $"speed-spline {index} right")!));
    }
    return values;
  }

  private static IReadOnlyList<string> ReadStringArray(
    string name,
    ITrackSectionDataSource source,
    DecodeContext context,
    uint expansionAddress,
    byte[] expansion,
    int countOffset,
    int pointerOffset,
    string description
  ) {
    var count = ReadUInt32(expansion, countOffset);
    ValidateCount(count, name, description);
    var arrayAddress = ReadRelocatedPointer(
      source,
      CheckedAdd(expansionAddress, pointerOffset, name),
      ReadUInt32(expansion, pointerOffset),
      count > 0,
      name,
      description);
    if (count == 0) return [];
    var bytes = ReadExact(
      source,
      arrayAddress,
      CheckedArrayLength(count, 4, name, description),
      name,
      description,
      context);
    context.ReserveObjects(count, name, description);
    var values = new string[Convert.ToInt32(count)];
    foreach (var index in Enumerable.Range(0, values.Length)) {
      var offset = checked(index * 4);
      values[index] = ReadRequiredString(
        source,
        CheckedAdd(arrayAddress, offset, name),
        ReadUInt32(bytes, offset),
        name,
        $"{description} entry {index}",
        context);
    }
    return values;
  }

  private static string? ReadResourceReference(
    ITrackSectionDataSource source,
    OvlLoaderEntry owner,
    uint fieldAddress,
    uint storedPointer,
    string expectedTag,
    bool required,
    string sectionName,
    string description
  ) {
    if (!source.ResourceReferences.TryGetValue(fieldAddress, out var reference)) {
      if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
        throw Invalid(sectionName,
          $"{description} contains conflicting direct pointer data");
      if (required) throw Invalid(sectionName, $"{description} SymbolRef is missing");
      return null;
    }
    if (!SameLoader(reference.Owner, owner))
      throw Invalid(sectionName, $"{description} SymbolRef is owned by another loader");
    if (!HasTag(reference.Symbol, expectedTag))
      throw Invalid(sectionName,
        $"{description} SymbolRef '{reference.Symbol}' is not {expectedTag}");
    if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(sectionName,
        $"{description} SymbolRef field contains conflicting pointer data");
    return reference.Symbol;
  }

  private static void ValidateOwnedReferences(
    string sectionName,
    OvlLoaderEntry owner,
    IReadOnlySet<uint> expectedFields,
    ITrackSectionDataSource source
  ) {
    foreach (var reference in source.ResourceReferences) {
      if (!SameLoader(reference.Value.Owner, owner)) continue;
      if (!expectedFields.Contains(reference.Key))
        throw Invalid(sectionName,
          $"loader owns a SymbolRef outside the TKS layout at {reference.Key}");
    }
  }

  private static uint ReadRelocatedPointer(
    ITrackSectionDataSource source,
    uint fieldAddress,
    uint storedPointer,
    bool required,
    string sectionName,
    string description
  ) {
    if (!source.TryGetRelocationSource(fieldAddress, out var address)) {
      if (storedPointer != 0)
        throw Invalid(sectionName, $"{description} contains an unproven pointer {storedPointer}");
      if (required) throw Invalid(sectionName, $"{description} is not a relocated pointer");
      return 0;
    }
    if (storedPointer != address)
      throw Invalid(sectionName, $"{description} does not match its relocation target");
    return address;
  }

  private static string ReadRequiredString(
    ITrackSectionDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string sectionName,
    string description,
    DecodeContext context
  ) {
    var value = ReadString(
      source, fieldAddress, storedPointer, sectionName, description, context, required: true);
    return value!;
  }

  private static string? ReadOptionalString(
    ITrackSectionDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string sectionName,
    string description,
    DecodeContext context
  ) => ReadString(
    source, fieldAddress, storedPointer, sectionName, description, context, required: false);

  private static string? ReadString(
    ITrackSectionDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string sectionName,
    string description,
    DecodeContext context,
    bool required
  ) {
    if (!source.TryGetRelocationSource(fieldAddress, out var address)) {
      if (storedPointer != 0)
        throw Invalid(sectionName, $"{description} contains an unproven pointer {storedPointer}");
      if (required) throw Invalid(sectionName, $"{description} is not a relocated pointer");
      return null;
    }
    if (storedPointer != address)
      throw Invalid(sectionName, $"{description} does not match its relocation target");
    if (!source.TryGetNullTerminatedStringByteLength(
          address, MaximumStringBytes, out var length) || (required && length == 0))
      throw Invalid(sectionName,
        $"{description} is missing, unterminated, or exceeds {MaximumStringBytes} bytes");
    context.ReserveBytes(Convert.ToUInt64(length) + 1, sectionName, description);
    if (!source.TryReadNullTerminatedString(address, length + 1, out var value) ||
        (required && string.IsNullOrEmpty(value)))
      throw Invalid(sectionName, $"{description} changed while it was being decoded");
    return value;
  }

  private static byte[] ReadExact(
    ITrackSectionDataSource source,
    uint address,
    int length,
    string sectionName,
    string description,
    DecodeContext context
  ) {
    context.ReserveBytes(Convert.ToUInt64(length), sectionName, description);
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(sectionName, $"{description} is outside the archive or truncated");
    return bytes;
  }

  private static float ReadFiniteSingle(
    byte[] bytes,
    int offset,
    string sectionName,
    string description
  ) {
    var value = BitConverter.ToSingle(bytes, offset);
    if (!float.IsFinite(value)) throw Invalid(sectionName, $"{description} is not finite");
    return value;
  }

  private static void ValidateCount(uint count, string sectionName, string description) {
    if (count > MaximumArrayCount)
      throw Invalid(sectionName,
        $"{description} count {count} exceeds the decoder limit {MaximumArrayCount}");
  }

  private static int CheckedArrayLength(
    uint count,
    int stride,
    string sectionName,
    string description
  ) {
    var length = Convert.ToUInt64(count) * Convert.ToUInt64(stride);
    if (length > int.MaxValue)
      throw Invalid(sectionName, $"{description} byte count exceeds the decoder range");
    return Convert.ToInt32(length);
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static bool HasTag(string key, string tag) =>
    key.EndsWith($":{tag}", StringComparison.OrdinalIgnoreCase);

  private static uint CheckedAdd(uint address, int offset, string sectionName) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(sectionName, "pointer address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static uint ReadUInt32(byte[] bytes, int offset) =>
    BitConverter.ToUInt32(bytes, offset);

  private static int ReadInt32(byte[] bytes, int offset) =>
    BitConverter.ToInt32(bytes, offset);

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Track section '{name}' is malformed: {message}.");

  private sealed class DecodeContext(TrackSectionDecodeLimits limits) {
    private ulong decodedBytes;
    private ulong decodedObjects;

    public void ReserveBytes(ulong count, string sectionName, string description) {
      if (count > limits.MaximumBytes || decodedBytes > limits.MaximumBytes - count)
        throw Invalid(sectionName,
          $"aggregate decode bytes exceed the limit {limits.MaximumBytes} while reading " +
          description);
      decodedBytes += count;
    }

    public void ReserveObjects(ulong count, string sectionName, string description) {
      if (count > limits.MaximumObjects || decodedObjects > limits.MaximumObjects - count)
        throw Invalid(sectionName,
          $"aggregate decoded objects exceed the limit {limits.MaximumObjects} while reading " +
          description);
      decodedObjects += count;
    }
  }

  private sealed class OvlTrackSectionDataSource : ITrackSectionDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;
    private readonly OvlCommonStringTable stringTable;

    public OvlTrackSectionDataSource(Ovl ovl, DecodeContext context) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the TKS decoder limit " +
          $"{MaximumResourceCount}.");

      context.ReserveObjects(
        OvlLoaderIndexBudget.Calculate(ovl.LoaderEntriesInOrder.Count),
        "OVL",
        "loader metadata index");
      var mutableLoaders = new Dictionary<uint, List<OvlLoaderEntry>>();
      foreach (var entry in ovl.LoaderEntriesInOrder) {
        if (!mutableLoaders.TryGetValue(entry.DataAddress, out var entries))
          mutableLoaders.Add(entry.DataAddress, entries = []);
        entries.Add(entry);
      }
      loadersByDataAddress = mutableLoaders.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<OvlLoaderEntry>)pair.Value);
      ResourceReferences = OvlSymbolReferenceIndex.Create(ovl).References;
      stringTable = new OvlCommonStringTable(ovl);
    }

    public IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }

    public OvlLoaderEntry GetTrackSectionLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact tks loader-table entry");
      var matches = candidates.Where(entry =>
        entry.Tag.ToFileType() == FileType.TrackSection &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact tks loader-table entry");
      return matches[0];
    }

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      if (ovl.TryReadBytes(address, length, out var resolved)) {
        bytes = resolved;
        return true;
      }
      bytes = [];
      return false;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      ovl.TryGetRelocationSource(address, out value);

    public bool TryGetNullTerminatedStringByteLength(
      uint address,
      int maximumLength,
      out int length
    ) {
      if (!stringTable.TryReadString(address, maximumLength, out var value)) {
        length = 0;
        return false;
      }
      length = Encoding.ASCII.GetByteCount(value);
      return true;
    }

    public bool TryReadNullTerminatedString(uint address, int maximumLength, out string value) =>
      stringTable.TryReadString(address, maximumLength, out value);
  }
}

internal readonly record struct TrackSectionDecodeLimits(ulong MaximumBytes, ulong MaximumObjects) {
  public static TrackSectionDecodeLimits Default { get; } =
    new(256UL * 1024 * 1024, 1_000_000);
}

internal interface ITrackSectionDataSource {
  IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryGetNullTerminatedStringByteLength(uint address, int maximumLength, out int length);
  bool TryReadNullTerminatedString(uint address, int maximumLength, out string value);
}
