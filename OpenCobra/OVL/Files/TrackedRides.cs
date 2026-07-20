// Tracked Rides
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>The three serialized RCT3 tracked-ride layouts.</summary>
public enum TrackedRideVersion : uint {
  Vanilla = 0,
  Soaked = 1,
  Wild = 2,
}

/// <summary>A track-section dependency and its parallel cost metadata.</summary>
public sealed record TrackedRideTrackSection(string Resource, string InternalName, uint Cost);

/// <summary>The platform and dispatch settings in <c>TrackedRide_Common</c>.</summary>
public sealed record TrackedRideStation(
  string? Name,
  uint PlatformHeightOverTrack,
  uint StartPreset,
  uint StartPossibilities,
  float RollSpeed,
  int ModusFlags
);

/// <summary>The named speed controls in <c>TrackedRide_Common</c>.</summary>
public sealed record TrackedRideMotion(
  float LaunchedPreset,
  float LaunchedMinimum,
  float LaunchedMaximum,
  float LaunchedStep,
  float ChainPreset,
  float ChainMinimum,
  float ChainMaximum,
  float ChainStep,
  float ChainLock,
  float ConstantPreset,
  float ConstantMinimum,
  float ConstantMaximum,
  float ConstantStep,
  float SpeedUpDownVariation,
  float TowerTopDuration,
  float TowerTopDistance,
  float FreeSpaceProfileHeight,
  float AccelerationBehaviour
);

/// <summary>The fixed construction and operation flags in <c>TrackedRide_Common</c>.</summary>
public sealed record TrackedRideOptions(
  uint OnlyOnWater,
  int OnWaterOffset,
  uint BlocksPossible,
  uint CarRotation,
  uint LoopNotAllowed,
  uint AutoComplete,
  uint DeconstructEverywhere
);

/// <summary>The fixed upkeep and preview-cost fields in <c>TrackedRide_Common</c>.</summary>
public sealed record TrackedRideCosts(
  uint UpkeepPerTrain,
  uint UpkeepPerCar,
  uint UpkeepPerStation,
  uint Preview1,
  uint Preview2,
  uint Preview3,
  uint CostPerFourHeight
);

/// <summary>The fixed track-section and spline SymbolRefs owned by the TRR loader.</summary>
public sealed record TrackedRideReferences(
  string? OtherTop,
  string? OtherMiddle,
  string? TowerTop,
  string? TowerMiddle,
  string? TrackSpline,
  string? TrackBigSpline,
  string? CarSpline,
  string? CarSwingSpline
);

/// <summary>The Soaked fields shared by Soaked and Wild tracked rides.</summary>
public sealed record TrackedRideExpansion(
  string? OtherTopFlipped,
  string? OtherMiddleFlipped,
  uint GroupDefinitionCount,
  IReadOnlyList<string> TrackPaths,
  int MinimumLength,
  uint WaterSectionFlag,
  float WaterSectionSpeed,
  float WaterSectionAcceleration,
  float WaterSectionDeceleration,
  float Unknown102,
  float? Unknown103
);

/// <summary>The Wild-only fixed extension.</summary>
public sealed record TrackedRideWild(
  string? Splitter,
  uint RoboFlag,
  uint SpinnerControl,
  uint EquivalentInversion,
  uint Unknown108,
  int DefaultTrainCount,
  uint StationSyncDefault,
  string? InternalName
);

/// <summary>A decoded RCT3 <c>trr</c> main/core resource.</summary>
public sealed record TrackedRide(
  string Name,
  TrackedRideVersion Version,
  IReadOnlyList<TrackedRideTrackSection> TrackSections,
  IReadOnlyList<string> TrainNames,
  string? CableLift,
  string? LiftCar,
  string? VanillaTrackPath,
  TrackedRideStation Station,
  TrackedRideMotion Motion,
  TrackedRideOptions Options,
  TrackedRideCosts Costs,
  TrackedRideReferences References,
  TrackedRideExpansion? Expansion,
  TrackedRideWild? Wild
);

/// <summary>Decodes the reference-proven main/core of relocated <c>trr</c> resources.</summary>
/// <remarks>
/// Ride options, attraction path arrays, station-limit arrays, and grouped-section definitions are
/// separate nested structures. Their pointers and bounded counts are validated where they occur in
/// the main/core, but their elements are deliberately outside this model until independently ported.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/trackedride.h">
/// rct3-importer tracked-ride layouts
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerTRR.cpp">
/// rct3-importer TrackedRide serializer
/// </seealso>
public static class TrackedRides {
  // Frontier pointers are 32-bit. trackedride.h and ManagerTRR.cpp prove these sizes.
  private const int VanillaSize = 380;
  private const int SoakedShortSize = 408;
  private const int SoakedSize = 412;
  private const int WildSize = 444;
  private const int CommonSize = 300;
  private const int VanillaCommonOffset = 80;
  private const int MaximumRideCount = 64 * 1024;
  private const int MaximumDependencyCount = 64 * 1024;
  private const int MaximumResourceCount = 1_000_000;
  private const int MaximumStringBytes = 4 * 1024;

  /// <summary>Decodes every tracked-ride resource from the unique half of an OVL pair.</summary>
  public static IReadOnlyList<TrackedRide> Extract(Ovl ovl) =>
    Extract(ovl, TrackedRideDecodeLimits.Default);

  internal static IReadOnlyList<TrackedRide> Extract(Ovl ovl, TrackedRideDecodeLimits limits) {
    ArgumentNullException.ThrowIfNull(ovl);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the TRR decoder limit {MaximumResourceCount}.");

    var context = new DecodeContext(limits);
    var files = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file =>
      file.Type == FileType.TrackedRide &&
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))) {
      if (files.Count >= MaximumRideCount)
        throw Invalid(file.Name,
          $"tracked-ride count exceeds the decoder limit {MaximumRideCount}");
      context.ReserveObjects(1, file.Name, "tracked-ride resource index");
      files.Add(file);
    }

    var source = new OvlTrackedRideDataSource(ovl, context);
    var rides = new List<TrackedRide>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier TRR at {address}");
      var owner = source.GetTrackedRideLoader(file, address);
      rides.Add(Decode(file.Name, owner, source, context));
    }
    return rides;
  }

  internal static TrackedRide Decode(
    string name,
    OvlLoaderEntry owner,
    ITrackedRideDataSource source
  ) => Decode(name, owner, source, new DecodeContext(TrackedRideDecodeLimits.Default));

  internal static TrackedRide Decode(
    string name,
    OvlLoaderEntry owner,
    ITrackedRideDataSource source,
    TrackedRideDecodeLimits limits
  ) => Decode(name, owner, source, new DecodeContext(limits));

  private static TrackedRide Decode(
    string name,
    OvlLoaderEntry owner,
    ITrackedRideDataSource source,
    DecodeContext context
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.TrackedRide)
      throw Invalid(name, $"loader type '{owner.Tag}' is not trr");

    var address = owner.DataAddress;
    var baseHeader = ReadExact(source, address, VanillaSize, name, "base header", context);
    var marker = ReadUInt32(baseHeader, 0);
    var version = TrackedRideVersion.Vanilla;
    var commonOffset = VanillaCommonOffset;
    byte[] soakedCore = [];
    byte[] expansionTail = [];

    if (marker == uint.MaxValue) {
      if (source.TryGetRelocationSource(address, out _))
        throw Invalid(name, "version marker is also a relocated pointer");
      commonOffset = 0;
      soakedCore = ReadExact(
        source,
        CheckedAdd(address, VanillaSize, name),
        SoakedShortSize - VanillaSize,
        name,
        "Soaked core",
        context);
      version = ReadExpansionVersion(name, owner, source, baseHeader, context);
      var waterSectionFlag = ReadUInt32(soakedCore, 8);
      if (waterSectionFlag > 1)
        throw Invalid(name, $"water-section flag {waterSectionFlag} is not 0 or 1");
      if (version == TrackedRideVersion.Wild && waterSectionFlag != 0)
        throw Invalid(name, "Wild layout uses the short Soaked water-section form");
      var tailLength = version == TrackedRideVersion.Wild
        ? WildSize - SoakedShortSize
        : waterSectionFlag == 0 ? SoakedSize - SoakedShortSize : 0;
      if (tailLength > 0)
        expansionTail = ReadExact(
          source,
          CheckedAdd(address, SoakedShortSize, name),
          tailLength,
          name,
          version == TrackedRideVersion.Wild ? "Wild tail" : "Soaked tail",
          context);
    }

    var commonAddress = CheckedAdd(address, commonOffset, name);
    uint CommonUInt32(int offset) => ReadUInt32(baseHeader, commonOffset + offset);
    int CommonInt32(int offset) => BitConverter.ToInt32(baseHeader, commonOffset + offset);
    float CommonSingle(int offset, string description) =>
      ReadFiniteSingle(baseHeader, commonOffset + offset, name, description);

    ValidateCommonFloats(name, baseHeader, commonOffset);
    var trackSectionCount = version == TrackedRideVersion.Vanilla
      ? CommonUInt32(0)
      : ReadUInt32(baseHeader, 304);
    var sectionMetadataCount = CommonUInt32(256);
    ValidateCount(trackSectionCount, name, "track-section");
    ValidateCount(sectionMetadataCount, name, "track-section metadata");
    if (sectionMetadataCount != trackSectionCount)
      throw Invalid(name,
        $"track-section count {trackSectionCount} does not match metadata count " +
        sectionMetadataCount);

    var expectedReferenceFields = new HashSet<uint>();
    var trackSections = ReadTrackSections(
      name,
      owner,
      source,
      context,
      commonAddress,
      baseHeader,
      commonOffset,
      trackSectionCount,
      expectedReferenceFields,
      out var trackReferenceRange);
    var trainNames = ReadStringArray(
      name,
      source,
      context,
      CheckedAdd(commonAddress, 12, name),
      CommonUInt32(12),
      CommonUInt32(8),
      "train names");

    string? ReadCommonRef(int offset, string tag, string description) {
      var fieldAddress = CheckedAdd(commonAddress, offset, name);
      expectedReferenceFields.Add(fieldAddress);
      return ReadResourceReference(
        source,
        owner,
        fieldAddress,
        CommonUInt32(offset),
        tag,
        required: false,
        name,
        description);
    }

    var references = new TrackedRideReferences(
      ReadCommonRef(212, "tks", "other top track section"),
      ReadCommonRef(216, "tks", "other middle track section"),
      ReadCommonRef(220, "tks", "tower top track section"),
      ReadCommonRef(224, "tks", "tower middle track section"),
      ReadCommonRef(276, "spl", "track spline"),
      ReadCommonRef(280, "spl", "large track spline"),
      ReadCommonRef(284, "spl", "car spline"),
      ReadCommonRef(288, "spl", "car swing spline"));

    TrackedRideExpansion? expansion = null;
    TrackedRideWild? wild = null;
    var modeledRanges = new List<AddressRange> {
      new(commonAddress, CommonSize),
    };
    string? vanillaTrackPath = null;
    if (version == TrackedRideVersion.Vanilla) {
      vanillaTrackPath = ReadRequiredString(
        source,
        CheckedAdd(commonAddress, 208, name),
        CommonUInt32(208),
        name,
        "Vanilla track path",
        context);
    } else {
      modeledRanges.Add(new(CheckedAdd(address, 304, name),
        version == TrackedRideVersion.Wild
          ? WildSize - 304
          : (ReadUInt32(soakedCore, 8) == 0 ? SoakedSize : SoakedShortSize) - 304));
      expansion = ReadExpansion(
        name,
        version,
        owner,
        source,
        context,
        address,
        baseHeader,
        soakedCore,
        expansionTail,
        expectedReferenceFields);
      if (version == TrackedRideVersion.Wild)
        wild = ReadWild(
          name,
          owner,
          source,
          context,
          address,
          expansionTail,
          expectedReferenceFields);
    }

    if (trackReferenceRange.Length > 0) modeledRanges.Add(trackReferenceRange);
    ValidateOwnedReferences(name, owner, expectedReferenceFields, modeledRanges, source);
    context.ReserveObjects(9, name, "decoded tracked-ride model");

    var station = new TrackedRideStation(
      ReadOptionalString(
        source,
        CheckedAdd(commonAddress, 56, name),
        CommonUInt32(56),
        name,
        "station/platform name",
        context),
      CommonUInt32(60),
      CommonUInt32(72),
      CommonUInt32(76),
      CommonSingle(84, "station roll speed"),
      CommonInt32(236));
    var motion = new TrackedRideMotion(
      CommonSingle(92, "launched speed preset"),
      CommonSingle(264, "launched speed minimum"),
      CommonSingle(268, "launched speed maximum"),
      CommonSingle(272, "launched speed step"),
      CommonSingle(100, "chain speed preset"),
      CommonSingle(240, "chain speed minimum"),
      CommonSingle(244, "chain speed maximum"),
      CommonSingle(248, "chain speed step"),
      CommonSingle(104, "chain lock"),
      CommonSingle(124, "constant speed preset"),
      CommonSingle(128, "constant speed minimum"),
      CommonSingle(132, "constant speed maximum"),
      CommonSingle(136, "constant speed step"),
      CommonSingle(140, "speed up/down variation"),
      CommonSingle(180, "tower top duration"),
      CommonSingle(184, "tower top distance"),
      CommonSingle(292, "free-space profile height"),
      CommonSingle(296, "acceleration behaviour"));
    var options = new TrackedRideOptions(
      CommonUInt32(64),
      CommonInt32(68),
      CommonUInt32(80),
      CommonUInt32(176),
      CommonUInt32(188),
      CommonUInt32(228),
      CommonUInt32(232));
    var costs = new TrackedRideCosts(
      CommonUInt32(164),
      CommonUInt32(168),
      CommonUInt32(172),
      CommonUInt32(196),
      CommonUInt32(200),
      CommonUInt32(252),
      CommonUInt32(204));

    return new TrackedRide(
      name,
      version,
      trackSections,
      trainNames,
      ReadOptionalString(
        source,
        CheckedAdd(commonAddress, 16, name),
        CommonUInt32(16),
        name,
        "cable-lift name",
        context),
      ReadOptionalString(
        source,
        CheckedAdd(commonAddress, 36, name),
        CommonUInt32(36),
        name,
        "lift-car name",
        context),
      vanillaTrackPath,
      station,
      motion,
      options,
      costs,
      references,
      expansion,
      wild);
  }

  private static TrackedRideVersion ReadExpansionVersion(
    string name,
    OvlLoaderEntry owner,
    ITrackedRideDataSource source,
    byte[] baseHeader,
    DecodeContext context
  ) {
    var rideFieldAddress = CheckedAdd(owner.DataAddress, 300, name);
    var rideAddress = ReadRelocatedPointer(
      source,
      rideFieldAddress,
      ReadUInt32(baseHeader, 300),
      required: true,
      name,
      "Soaked/Wild ride substructure");
    var rideHeader = ReadExact(source, rideAddress, 28, name, "ride substructure", context);
    if (ReadUInt32(rideHeader, 0) != uint.MaxValue)
      throw Invalid(name, "Soaked/Wild ride substructure lacks its version marker");
    var attractionFieldAddress = CheckedAdd(rideAddress, 24, name);
    var attractionAddress = ReadRelocatedPointer(
      source,
      attractionFieldAddress,
      ReadUInt32(rideHeader, 24),
      required: true,
      name,
      "Soaked/Wild attraction substructure");
    var attractionHeader = ReadExact(
      source, attractionAddress, 60, name, "attraction substructure", context);
    var attractionType = ReadUInt32(attractionHeader, 0);
    return (attractionType & 768u) switch {
      512 => TrackedRideVersion.Soaked,
      768 => TrackedRideVersion.Wild,
      var value => throw Invalid(name,
        $"unsupported attraction type addon bits {value} in {attractionType}"),
    };
  }

  private static IReadOnlyList<TrackedRideTrackSection> ReadTrackSections(
    string name,
    OvlLoaderEntry owner,
    ITrackedRideDataSource source,
    DecodeContext context,
    uint commonAddress,
    byte[] baseHeader,
    int commonOffset,
    uint count,
    ISet<uint> expectedReferenceFields,
    out AddressRange referenceRange
  ) {
    var referenceFieldAddress = CheckedAdd(commonAddress, 4, name);
    var referenceArrayAddress = ReadRelocatedPointer(
      source,
      referenceFieldAddress,
      ReadUInt32(baseHeader, commonOffset + 4),
      count > 0,
      name,
      "track-section reference array");
    var metadataFieldAddress = CheckedAdd(commonAddress, 260, name);
    var metadataArrayAddress = ReadRelocatedPointer(
      source,
      metadataFieldAddress,
      ReadUInt32(baseHeader, commonOffset + 260),
      count > 0,
      name,
      "track-section metadata array");
    if (count == 0) {
      referenceRange = default;
      return [];
    }

    var referenceBytes = ReadExact(
      source,
      referenceArrayAddress,
      CheckedArrayLength(count, 4, name, "track-section references"),
      name,
      "track-section references",
      context);
    var metadataBytes = ReadExact(
      source,
      metadataArrayAddress,
      CheckedArrayLength(count, 8, name, "track-section metadata"),
      name,
      "track-section metadata",
      context);
    referenceRange = new(referenceArrayAddress, referenceBytes.Length);
    context.ReserveObjects(count, name, "track-section models");

    var sections = new List<TrackedRideTrackSection>(Convert.ToInt32(count));
    foreach (var index in Enumerable.Range(0, Convert.ToInt32(count))) {
      var referenceOffset = index * 4;
      var referenceAddress = CheckedAdd(referenceArrayAddress, referenceOffset, name);
      expectedReferenceFields.Add(referenceAddress);
      var resource = ReadResourceReference(
        source,
        owner,
        referenceAddress,
        ReadUInt32(referenceBytes, referenceOffset),
        "tks",
        required: true,
        name,
        $"track section {index}")!;
      var metadataOffset = index * 8;
      var internalName = ReadRequiredString(
        source,
        CheckedAdd(metadataArrayAddress, metadataOffset, name),
        ReadUInt32(metadataBytes, metadataOffset),
        name,
        $"track-section metadata name {index}",
        context);
      sections.Add(new(resource, internalName, ReadUInt32(metadataBytes, metadataOffset + 4)));
    }
    return sections;
  }

  private static TrackedRideExpansion ReadExpansion(
    string name,
    TrackedRideVersion version,
    OvlLoaderEntry owner,
    ITrackedRideDataSource source,
    DecodeContext context,
    uint address,
    byte[] baseHeader,
    byte[] soakedCore,
    byte[] expansionTail,
    ISet<uint> expectedReferenceFields
  ) {
    ValidateExpansionFloats(name, baseHeader, soakedCore, expansionTail);
    var groupCount = ReadUInt32(baseHeader, 328);
    ValidateCount(groupCount, name, "group-definition");
    ReadRelocatedPointer(
      source,
      CheckedAdd(address, 332, name),
      ReadUInt32(baseHeader, 332),
      groupCount > 0,
      name,
      "group-definition array");
    var trackPathCount = ReadUInt32(baseHeader, 360);
    ValidateCount(trackPathCount, name, "track-path");
    if (trackPathCount == 0) throw Invalid(name, "track-path array is empty");
    var trackPaths = ReadStringArray(
      name,
      source,
      context,
      CheckedAdd(address, 364, name),
      ReadUInt32(baseHeader, 364),
      trackPathCount,
      "track paths");

    string? ReadRef(int offset, string description) {
      var fieldAddress = CheckedAdd(address, offset, name);
      expectedReferenceFields.Add(fieldAddress);
      return ReadResourceReference(
        source,
        owner,
        fieldAddress,
        ReadUInt32(baseHeader, offset),
        "tks",
        required: false,
        name,
        description);
    }

    var waterFlag = ReadUInt32(soakedCore, 8);
    return new(
      ReadRef(308, "flipped other top track section"),
      ReadRef(312, "flipped other middle track section"),
      groupCount,
      trackPaths,
      BitConverter.ToInt32(soakedCore, 0),
      waterFlag,
      ReadFiniteSingle(soakedCore, 12, name, "water-section speed"),
      ReadFiniteSingle(soakedCore, 16, name, "water-section acceleration"),
      ReadFiniteSingle(soakedCore, 20, name, "water-section deceleration"),
      ReadFiniteSingle(soakedCore, 24, name, "Soaked unknown 102"),
      waterFlag == 0
        ? ReadFiniteSingle(expansionTail, 0, name, "Soaked unknown 103")
        : null);
  }

  private static TrackedRideWild ReadWild(
    string name,
    OvlLoaderEntry owner,
    ITrackedRideDataSource source,
    DecodeContext context,
    uint address,
    byte[] expansionTail,
    ISet<uint> expectedReferenceFields
  ) {
    var splitterFieldAddress = CheckedAdd(address, 412, name);
    expectedReferenceFields.Add(splitterFieldAddress);
    var splitter = ReadResourceReference(
      source,
      owner,
      splitterFieldAddress,
      ReadUInt32(expansionTail, 4),
      "svd",
      required: false,
      name,
      "Wild splitter visual");
    return new(
      splitter,
      ReadUInt32(expansionTail, 8),
      ReadUInt32(expansionTail, 12),
      ReadUInt32(expansionTail, 16),
      ReadUInt32(expansionTail, 20),
      BitConverter.ToInt32(expansionTail, 24),
      ReadUInt32(expansionTail, 28),
      ReadOptionalString(
        source,
        CheckedAdd(address, 440, name),
        ReadUInt32(expansionTail, 32),
        name,
        "Wild internal name",
        context));
  }

  private static IReadOnlyList<string> ReadStringArray(
    string name,
    ITrackedRideDataSource source,
    DecodeContext context,
    uint fieldAddress,
    uint storedPointer,
    uint count,
    string description
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
    var values = new List<string>(Convert.ToInt32(count));
    foreach (var index in Enumerable.Range(0, Convert.ToInt32(count))) {
      var offset = index * 4;
      values.Add(ReadRequiredString(
        source,
        CheckedAdd(arrayAddress, offset, name),
        ReadUInt32(bytes, offset),
        name,
        $"{description} entry {index}",
        context));
    }
    return values;
  }

  private static string? ReadResourceReference(
    ITrackedRideDataSource source,
    OvlLoaderEntry owner,
    uint fieldAddress,
    uint storedPointer,
    string expectedTag,
    bool required,
    string rideName,
    string description
  ) {
    if (!source.ResourceReferences.TryGetValue(fieldAddress, out var reference)) {
      if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
        throw Invalid(rideName,
          $"{description} contains conflicting direct pointer data");
      if (required) throw Invalid(rideName, $"{description} SymbolRef is missing");
      return null;
    }
    if (!SameLoader(reference.Owner, owner))
      throw Invalid(rideName, $"{description} SymbolRef is owned by another loader");
    if (!HasTag(reference.Symbol, expectedTag))
      throw Invalid(rideName,
        $"{description} SymbolRef '{reference.Symbol}' is not {expectedTag}");
    if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(rideName,
        $"{description} SymbolRef field contains conflicting pointer data");
    return reference.Symbol;
  }

  private static void ValidateOwnedReferences(
    string rideName,
    OvlLoaderEntry owner,
    IReadOnlySet<uint> expectedFields,
    IReadOnlyList<AddressRange> modeledRanges,
    ITrackedRideDataSource source
  ) {
    foreach (var reference in source.ResourceReferences) {
      if (!SameLoader(reference.Value.Owner, owner)) continue;
      if (expectedFields.Contains(reference.Key)) continue;
      if (modeledRanges.Any(range => range.Contains(reference.Key)))
        throw Invalid(rideName,
          $"loader owns an unexpected SymbolRef inside the modeled TRR core at {reference.Key}");
      // Nested attraction paths and ride/group option structures remain outside this model.
    }
  }

  private static uint ReadRelocatedPointer(
    ITrackedRideDataSource source,
    uint fieldAddress,
    uint storedPointer,
    bool required,
    string rideName,
    string description
  ) {
    if (!source.TryGetRelocationSource(fieldAddress, out var address)) {
      if (storedPointer != 0)
        throw Invalid(rideName, $"{description} contains an unproven pointer {storedPointer}");
      if (required) throw Invalid(rideName, $"{description} is not a relocated pointer");
      return 0;
    }
    if (storedPointer != address)
      throw Invalid(rideName, $"{description} does not match its relocation target");
    return address;
  }

  private static string ReadRequiredString(
    ITrackedRideDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string rideName,
    string description,
    DecodeContext context
  ) {
    var value = ReadString(
      source, fieldAddress, storedPointer, rideName, description, context, required: true);
    return value!;
  }

  private static string? ReadOptionalString(
    ITrackedRideDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string rideName,
    string description,
    DecodeContext context
  ) => ReadString(
    source, fieldAddress, storedPointer, rideName, description, context, required: false);

  private static string? ReadString(
    ITrackedRideDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string rideName,
    string description,
    DecodeContext context,
    bool required
  ) {
    if (!source.TryGetRelocationSource(fieldAddress, out var address)) {
      if (storedPointer != 0)
        throw Invalid(rideName, $"{description} contains an unproven pointer {storedPointer}");
      if (required) throw Invalid(rideName, $"{description} is not a relocated pointer");
      return null;
    }
    if (storedPointer != address)
      throw Invalid(rideName, $"{description} does not match its relocation target");
    if (!source.TryGetNullTerminatedStringByteLength(
          address, MaximumStringBytes, out var length) || (required && length == 0))
      throw Invalid(rideName,
        $"{description} is missing, unterminated, or exceeds {MaximumStringBytes} bytes");
    context.ReserveBytes(Convert.ToUInt64(length) + 1, rideName, description);
    if (!source.TryReadNullTerminatedString(address, length + 1, out var value) ||
        (required && string.IsNullOrEmpty(value)))
      throw Invalid(rideName, $"{description} changed while it was being decoded");
    return value;
  }

  private static byte[] ReadExact(
    ITrackedRideDataSource source,
    uint address,
    int length,
    string rideName,
    string description,
    DecodeContext context
  ) {
    context.ReserveBytes(Convert.ToUInt64(length), rideName, description);
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(rideName, $"{description} is outside the archive or truncated");
    return bytes;
  }

  private static void ValidateCommonFloats(string name, byte[] header, int commonOffset) {
    foreach (var offset in new[] {
      20, 24, 28, 32, 40, 44, 48, 52, 84, 88, 92, 96, 100, 104, 108, 112,
      116, 120, 124, 128, 132, 136, 140, 180, 184, 240, 244, 248, 264, 268,
      272, 292, 296,
    })
      ReadFiniteSingle(header, commonOffset + offset, name, $"common float at offset {offset}");
  }

  private static void ValidateExpansionFloats(
    string name,
    byte[] baseHeader,
    byte[] soakedCore,
    byte[] expansionTail
  ) {
    foreach (var offset in new[] { 324, 352, 356, 368, 372 })
      ReadFiniteSingle(baseHeader, offset, name, $"Soaked float at offset {offset}");
    foreach (var offset in new[] { 12, 16, 20, 24 })
      ReadFiniteSingle(soakedCore, offset, name, $"Soaked core float at offset {offset + 380}");
    if (ReadUInt32(soakedCore, 8) == 0)
      ReadFiniteSingle(expansionTail, 0, name, "Soaked unknown 103");
  }

  private static float ReadFiniteSingle(
    byte[] bytes,
    int offset,
    string rideName,
    string description
  ) {
    var value = BitConverter.ToSingle(bytes, offset);
    if (!float.IsFinite(value)) throw Invalid(rideName, $"{description} is not finite");
    return value;
  }

  private static int CheckedArrayLength(
    uint count,
    int stride,
    string rideName,
    string description
  ) {
    var length = Convert.ToUInt64(count) * Convert.ToUInt64(stride);
    if (length > int.MaxValue)
      throw Invalid(rideName, $"{description} byte length exceeds the supported range");
    return Convert.ToInt32(length);
  }

  private static void ValidateCount(uint count, string rideName, string description) {
    if (count > MaximumDependencyCount)
      throw Invalid(rideName,
        $"{description} count {count} exceeds the decoder limit {MaximumDependencyCount}");
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static bool HasTag(string key, string tag) =>
    key.EndsWith($":{tag}", StringComparison.OrdinalIgnoreCase);

  private static uint CheckedAdd(uint address, int offset, string rideName) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(rideName, "pointer address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static uint ReadUInt32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Tracked ride '{name}' is malformed: {message}.");

  private readonly record struct AddressRange(uint Start, int Length) {
    public bool Contains(uint address) {
      var target = Convert.ToUInt64(address);
      var start = Convert.ToUInt64(Start);
      return target >= start && target < start + Convert.ToUInt64(Length);
    }
  }

  private sealed class DecodeContext(TrackedRideDecodeLimits limits) {
    private ulong decodedBytes;
    private ulong decodedObjects;

    public void ReserveBytes(ulong count, string rideName, string description) {
      if (count > limits.MaximumBytes || decodedBytes > limits.MaximumBytes - count)
        throw Invalid(rideName,
          $"aggregate decode bytes exceed the limit {limits.MaximumBytes} while reading " +
          description);
      decodedBytes += count;
    }

    public void ReserveObjects(ulong count, string rideName, string description) {
      if (count > limits.MaximumObjects || decodedObjects > limits.MaximumObjects - count)
        throw Invalid(rideName,
          $"aggregate decoded objects exceed the limit {limits.MaximumObjects} while reading " +
          description);
      decodedObjects += count;
    }
  }

  private sealed class OvlTrackedRideDataSource : ITrackedRideDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;
    private readonly OvlCommonStringTable stringTable;

    public OvlTrackedRideDataSource(Ovl ovl, DecodeContext context) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the TRR decoder limit " +
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

    public OvlLoaderEntry GetTrackedRideLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact trr loader-table entry");
      var matches = candidates.Where(entry =>
        entry.Tag.ToFileType() == FileType.TrackedRide &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact trr loader-table entry");
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

internal readonly record struct TrackedRideDecodeLimits(ulong MaximumBytes, ulong MaximumObjects) {
  public static TrackedRideDecodeLimits Default { get; } =
    new(256UL * 1024 * 1024, 1_000_000);
}

internal interface ITrackedRideDataSource {
  IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryGetNullTerminatedStringByteLength(uint address, int maximumLength, out int length);
  bool TryReadNullTerminatedString(uint address, int maximumLength, out string value);
}
