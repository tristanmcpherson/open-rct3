// Animated Rides
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.ObjectModel;
using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>The three serialized RCT3 animated-ride layouts.</summary>
public enum AnimatedRideVersion : byte {
  Vanilla = 0,
  Soaked = 2,
  Wild = 3,
}

/// <summary>An exact serialized ride-option payload, excluding its type word.</summary>
public sealed record AnimatedRideOption(uint Type, IReadOnlyList<uint> PayloadWords);

/// <summary>An entrance/exit placement limit from <c>RideStationLimit</c>.</summary>
public sealed record AnimatedRideStationLimit(int X, int Z, int Height, uint Flags);

/// <summary>A Soaked/Wild show-item animation and its relocated string-table name.</summary>
public sealed record AnimatedRideShowItem(uint AnimationIndex, string Name);

/// <summary>The attraction fields shared by the three animated-ride layouts.</summary>
public sealed record AnimatedRideAttraction(
  uint Type,
  string NameTextReference,
  string DescriptionTextReference,
  string IconReference,
  uint BaseUpkeep,
  string LoopSplineReference,
  IReadOnlyList<string> PathSplineReferences,
  uint Flags,
  int MaximumHeight,
  uint? AddonAssociation,
  uint? UiDeactivation,
  IReadOnlyDictionary<int, uint> UnknownFields
);

/// <summary>The ride fields shared by animated rides.</summary>
public sealed record AnimatedRideSettings(
  uint Attractivity,
  uint Seating,
  IReadOnlyList<AnimatedRideOption> Options,
  uint MinimumCircuits,
  int MaximumCircuits,
  uint EntryFee,
  IReadOnlyList<AnimatedRideStationLimit> StationLimits,
  IReadOnlyDictionary<int, uint> UnknownFields
);

/// <summary>A decoded RCT3 <c>anr</c> resource.</summary>
public sealed record AnimatedRide(
  string Name,
  AnimatedRideVersion Version,
  AnimatedRideAttraction Attraction,
  AnimatedRideSettings Ride,
  string SceneryItemReference,
  IReadOnlyList<AnimatedRideShowItem> ShowItems,
  IReadOnlyDictionary<int, uint> UnknownFields
);

/// <summary>Decodes relocation- and SymbolRef-proven RCT3 animated rides.</summary>
/// <remarks>
/// Unknown scalar fields retain their offsets relative to the structure that owns them. Pointer
/// layout, option sizes, and expansion selection are ported from the pinned writer rather than the
/// empty Rust ANR dispatch stub in this repository.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/animatedride.h">
/// rct3-importer animated-ride layouts
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerANR.cpp">
/// rct3-importer AnimatedRide serializer
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerCommon.cpp">
/// rct3-importer attraction, ride-option, and shared-ride serializer
/// </seealso>
public static class AnimatedRides {
  private const int VanillaHeaderSize = 100;
  private const int ExpansionHeaderSize = 44;
  private const int RideVanillaSize = 24;
  private const int RideSoakedSize = 60;
  private const int RideWildSize = 68;
  private const int AttractionVanillaSize = 56;
  private const int AttractionSoakedSize = 64;
  private const int AttractionWildSize = 68;
  private const int StationLimitSize = 16;
  private const int ShowItemSize = 8;
  private const int MaximumAnimatedRideCount = 64 * 1024;
  private const uint MaximumArrayCount = 64 * 1024;
  private const int MaximumResourceCount = 1_000_000;
  private const int MaximumStringBytes = 4 * 1024;
  private const uint SoakedAddonBits = 512;
  private const uint WildAddonBits = 768;
  private const uint AddonMask = 768;

  /// <summary>Decodes every animated ride from the unique half of an OVL pair.</summary>
  public static IReadOnlyList<AnimatedRide> Extract(Ovl ovl) =>
    Extract(ovl, AnimatedRideDecodeLimits.Default);

  internal static IReadOnlyList<AnimatedRide> Extract(
    Ovl ovl,
    AnimatedRideDecodeLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(ovl);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the ANR decoder limit " +
        $"{MaximumResourceCount}.");

    var context = new DecodeContext(limits);
    var files = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file => file.Type == FileType.AnimatedRide)) {
      if (!file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))
        throw Invalid(file.Name, "resource is not owned by a unique OVL archive");
      if (files.Count >= MaximumAnimatedRideCount)
        throw Invalid(file.Name,
          $"animated-ride count exceeds the decoder limit {MaximumAnimatedRideCount}");
      context.ReserveObjects(1, file.Name, "animated-ride resource index");
      files.Add(file);
    }

    var source = new OvlAnimatedRideDataSource(ovl, context);
    var rides = new List<AnimatedRide>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier ANR at {address}");
      var owner = source.GetAnimatedRideLoader(file, address);
      rides.Add(Decode(file.Name, owner, source, context));
    }
    return rides;
  }

  internal static AnimatedRide Decode(
    string name,
    OvlLoaderEntry owner,
    IAnimatedRideDataSource source
  ) => Decode(name, owner, source, new DecodeContext(AnimatedRideDecodeLimits.Default));

  internal static AnimatedRide Decode(
    string name,
    OvlLoaderEntry owner,
    IAnimatedRideDataSource source,
    AnimatedRideDecodeLimits limits
  ) => Decode(name, owner, source, new DecodeContext(limits));

  private static AnimatedRide Decode(
    string name,
    OvlLoaderEntry owner,
    IAnimatedRideDataSource source,
    DecodeContext context
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.AnimatedRide)
      throw Invalid(name, $"loader type '{owner.Tag}' is not anr");
    if (string.IsNullOrWhiteSpace(owner.SourcePath) ||
        !owner.SourcePath.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid(name, "loader is not owned by a unique OVL archive");

    var address = owner.DataAddress;
    var regionLoaders = source.GetDataRegionLoaders(owner);
    if (regionLoaders.Count(loader => SameLoader(loader, owner)) != 1)
      throw Invalid(name, "loader is not present exactly once in its proven data region");
    if (regionLoaders.Count(loader => loader.DataAddress == address) != 1)
      throw Invalid(name, $"data address {address} is aliased by another loader");
    var loaderDataField = CheckedAdd(owner.StructAddress, sizeof(uint), name);
    if (!source.TryGetRelocationSource(loaderDataField, out var loaderDataAddress) ||
        loaderDataAddress != address)
      throw Invalid(name, "loader data field is not relocated to its exact data address");
    if (source.ResourceReferences.ContainsKey(loaderDataField))
      throw Invalid(name, "loader data field conflicts with a SymbolRef");
    context.BeginResource(source, owner, name);

    var marker = ReadExact(source, address, 4, name, "version marker", context);
    if (source.TryGetRelocationSource(address, out _))
      throw Invalid(name, "version/type marker is also a relocated pointer");

    var expectedReferenceFields = new HashSet<uint>();
    AnimatedRide result;
    if (ReadUInt32(marker, 0) == uint.MaxValue)
      result = DecodeExpansion(
        name, owner, source, context, address, expectedReferenceFields);
    else
      result = DecodeVanilla(
        name, owner, source, context, address, expectedReferenceFields);

    ValidateOwnedReferences(name, owner, expectedReferenceFields, source);
    context.ValidateModeledRelocations(source, name);
    return result;
  }

  private static AnimatedRide DecodeVanilla(
    string name,
    OvlLoaderEntry owner,
    IAnimatedRideDataSource source,
    DecodeContext context,
    uint address,
    ISet<uint> expectedReferenceFields
  ) {
    var header = ReadExact(
      source, address, VanillaHeaderSize, name, "Vanilla header", context);
    var type = ReadUInt32(header, 0);
    if ((type & ~255u) != 0)
      throw Invalid(name, $"unsupported Vanilla attraction type {type}");

    var attraction = DecodeAttraction(
      name,
      AnimatedRideVersion.Vanilla,
      owner,
      source,
      context,
      address,
      header,
      0,
      CheckedAdd(address, VanillaHeaderSize, name),
      expectedReferenceFields);
    var rideAddress = CheckedAdd(address, AttractionVanillaSize, name);
    var ride = DecodeRide(
      name,
      AnimatedRideVersion.Vanilla,
      source,
      context,
      rideAddress,
      header,
      AttractionVanillaSize,
      [],
      out _);
    var sid = ReadExpectedReference(
      source,
      owner,
      CheckedAdd(address, 80, name),
      ReadUInt32(header, 80),
      "sid",
      allowEmptyName: false,
      name,
      "scenery item",
      expectedReferenceFields);
    context.ReserveObjects(4, name, "decoded Vanilla animated-ride model");
    return new AnimatedRide(
      name,
      AnimatedRideVersion.Vanilla,
      attraction,
      ride,
      sid,
      [],
      UnknownFields(
        (84, ReadUInt32(header, 84)),
        (88, ReadUInt32(header, 88)),
        (92, ReadUInt32(header, 92)),
        (96, ReadUInt32(header, 96))));
  }

  private static AnimatedRide DecodeExpansion(
    string name,
    OvlLoaderEntry owner,
    IAnimatedRideDataSource source,
    DecodeContext context,
    uint address,
    ISet<uint> expectedReferenceFields
  ) {
    var header = ReadExact(
      source, address, ExpansionHeaderSize, name, "Soaked/Wild header", context);
    var rideAddress = ReadRelocatedPointer(
      source,
      CheckedAdd(address, 20, name),
      ReadUInt32(header, 20),
      required: true,
      name,
      "ride substructure",
      context);
    var expectedRideAddress = CheckedAdd(address, ExpansionHeaderSize, name);
    if (rideAddress != expectedRideAddress)
      throw Invalid(name,
        $"ride substructure is at {rideAddress} instead of writer offset {ExpansionHeaderSize}");

    var rideBase = ReadExact(
      source, rideAddress, RideSoakedSize, name, "Soaked ride substructure", context);
    if (ReadUInt32(rideBase, 0) != uint.MaxValue)
      throw Invalid(name, "Soaked/Wild ride substructure lacks its version marker");
    var attractionAddress = ReadRelocatedPointer(
      source,
      CheckedAdd(rideAddress, 24, name),
      ReadUInt32(rideBase, 24),
      required: true,
      name,
      "attraction substructure",
      context);
    var attractionBase = ReadExact(
      source,
      attractionAddress,
      AttractionVanillaSize,
      name,
      "attraction base substructure",
      context);
    var version = (ReadUInt32(attractionBase, 0) & AddonMask) switch {
      SoakedAddonBits => AnimatedRideVersion.Soaked,
      WildAddonBits => AnimatedRideVersion.Wild,
      var bits => throw Invalid(name,
        $"unsupported attraction addon bits {bits}"),
    };
    var rideSize = version == AnimatedRideVersion.Wild ? RideWildSize : RideSoakedSize;
    var attractionSize = version == AnimatedRideVersion.Wild
      ? AttractionWildSize
      : AttractionSoakedSize;
    var expectedAttractionAddress = CheckedAdd(rideAddress, rideSize, name);
    if (attractionAddress != expectedAttractionAddress)
      throw Invalid(name,
        $"attraction substructure is at {attractionAddress} instead of writer offset " +
        $"{ExpansionHeaderSize + rideSize}");

    var rideExtension = version == AnimatedRideVersion.Wild
      ? ReadExact(
        source,
        CheckedAdd(rideAddress, RideSoakedSize, name),
        RideWildSize - RideSoakedSize,
        name,
        "Wild ride extension",
        context)
      : [];
    var attractionExtension = ReadExact(
      source,
      CheckedAdd(attractionAddress, AttractionVanillaSize, name),
      attractionSize - AttractionVanillaSize,
      name,
      $"{version} attraction extension",
      context);
    var attractionHeader = attractionBase.Concat(attractionExtension).ToArray();
    context.ReserveBytes(
      Convert.ToUInt64(attractionHeader.Length), name, "assembled attraction header");
    var attraction = DecodeAttraction(
      name,
      version,
      owner,
      source,
      context,
      attractionAddress,
      attractionHeader,
      0,
      CheckedAdd(attractionAddress, attractionSize, name),
      expectedReferenceFields);
    var ride = DecodeRide(
      name,
      version,
      source,
      context,
      rideAddress,
      rideBase,
      0,
      rideExtension,
      out var commonEndAddress);
    var sid = ReadExpectedReference(
      source,
      owner,
      CheckedAdd(address, 24, name),
      ReadUInt32(header, 24),
      "sid",
      allowEmptyName: false,
      name,
      "scenery item",
      expectedReferenceFields);
    var showItems = DecodeShowItems(
      name,
      source,
      context,
      CheckedAdd(address, 40, name),
      ReadUInt32(header, 40),
      ReadUInt32(header, 36),
      commonEndAddress);
    context.ReserveObjects(4, name, "decoded Soaked/Wild animated-ride model");
    return new AnimatedRide(
      name,
      version,
      attraction,
      ride,
      sid,
      showItems,
      UnknownFields(
        (4, ReadUInt32(header, 4)),
        (8, ReadUInt32(header, 8)),
        (12, ReadUInt32(header, 12)),
        (16, ReadUInt32(header, 16)),
        (28, ReadUInt32(header, 28)),
        (32, ReadUInt32(header, 32))));
  }

  private static AnimatedRideAttraction DecodeAttraction(
    string name,
    AnimatedRideVersion version,
    OvlLoaderEntry owner,
    IAnimatedRideDataSource source,
    DecodeContext context,
    uint address,
    byte[] header,
    int headerOffset,
    uint expectedPathArrayAddress,
    ISet<uint> expectedReferenceFields
  ) {
    var type = ReadUInt32(header, headerOffset);
    var expectedAddonBits = version switch {
      AnimatedRideVersion.Vanilla => 0u,
      AnimatedRideVersion.Soaked => SoakedAddonBits,
      AnimatedRideVersion.Wild => WildAddonBits,
      _ => throw Invalid(name, $"unsupported animated-ride version {version}"),
    };
    if ((type & AddonMask) != expectedAddonBits || (type & ~1023u) != 0)
      throw Invalid(name,
        $"attraction type {type} does not match the {version} layout");

    var nameReference = ReadExpectedReference(
      source,
      owner,
      CheckedAdd(address, 4, name),
      ReadUInt32(header, headerOffset + 4),
      "txt",
      allowEmptyName: false,
      name,
      "name text",
      expectedReferenceFields);
    var descriptionReference = ReadExpectedReference(
      source,
      owner,
      CheckedAdd(address, 8, name),
      ReadUInt32(header, headerOffset + 8),
      "txt",
      allowEmptyName: false,
      name,
      "description text",
      expectedReferenceFields);
    var iconReference = ReadExpectedReference(
      source,
      owner,
      CheckedAdd(address, 12, name),
      ReadUInt32(header, headerOffset + 12),
      "gsi",
      allowEmptyName: true,
      name,
      "icon",
      expectedReferenceFields);
    var loopSplineReference = ReadExpectedReference(
      source,
      owner,
      CheckedAdd(address, 36, name),
      ReadUInt32(header, headerOffset + 36),
      "spl",
      allowEmptyName: true,
      name,
      "loop spline",
      expectedReferenceFields);
    var pathCount = ReadUInt32(header, headerOffset + 40);
    ValidateCount(pathCount, name, "path splines");
    var pathArrayAddress = ReadRelocatedPointer(
      source,
      CheckedAdd(address, 44, name),
      ReadUInt32(header, headerOffset + 44),
      pathCount > 0,
      name,
      "path-spline array",
      context);
    IReadOnlyList<string> pathSplines = [];
    if (pathCount > 0) {
      if (pathArrayAddress != expectedPathArrayAddress)
        throw Invalid(name,
          $"path-spline array is at {pathArrayAddress} instead of writer address " +
          expectedPathArrayAddress);
      var pathBytes = ReadExact(
        source,
        pathArrayAddress,
        CheckedArrayLength(pathCount, 4, name, "path-spline array"),
        name,
        "path-spline array",
        context);
      context.ReserveObjects(pathCount, name, "path-spline models");
      var paths = new string[Convert.ToInt32(pathCount)];
      foreach (var index in Enumerable.Range(0, paths.Length)) {
        var offset = checked(index * 4);
        paths[index] = ReadExpectedReference(
          source,
          owner,
          CheckedAdd(pathArrayAddress, offset, name),
          ReadUInt32(pathBytes, offset),
          "spl",
          allowEmptyName: true,
          name,
          $"path spline {index}",
          expectedReferenceFields);
      }
      pathSplines = paths;
    }

    var unknowns = version == AnimatedRideVersion.Vanilla
      ? UnknownFields(
        (16, ReadUInt32(header, headerOffset + 16)),
        (20, ReadUInt32(header, headerOffset + 20)),
        (24, ReadUInt32(header, headerOffset + 24)),
        (28, ReadUInt32(header, headerOffset + 28)))
      : UnknownFields(
        (16, ReadUInt32(header, headerOffset + 16)),
        (20, ReadUInt32(header, headerOffset + 20)),
        (24, ReadUInt32(header, headerOffset + 24)),
        (28, ReadUInt32(header, headerOffset + 28)),
        (60, ReadUInt32(header, headerOffset + 60)));
    uint? addonAssociation = version == AnimatedRideVersion.Vanilla
      ? null
      : ReadUInt32(header, headerOffset + 56);
    if (addonAssociation is > 2)
      throw Invalid(name,
        $"addon association {addonAssociation} is outside the Vanilla/Soaked/Wild domain");

    context.ReserveObjects(2, name, "decoded attraction model");
    return new AnimatedRideAttraction(
      type,
      nameReference,
      descriptionReference,
      iconReference,
      ReadUInt32(header, headerOffset + 32),
      loopSplineReference,
      pathSplines,
      ReadUInt32(header, headerOffset + 48),
      ReadInt32(header, headerOffset + 52),
      addonAssociation,
      version == AnimatedRideVersion.Wild
        ? ReadUInt32(header, headerOffset + 64)
        : null,
      unknowns);
  }

  private static AnimatedRideSettings DecodeRide(
    string name,
    AnimatedRideVersion version,
    IAnimatedRideDataSource source,
    DecodeContext context,
    uint address,
    byte[] header,
    int headerOffset,
    byte[] wildExtension,
    out uint commonEndAddress
  ) {
    if (version != AnimatedRideVersion.Vanilla &&
        ReadUInt32(header, headerOffset) != uint.MaxValue)
      throw Invalid(name, "Soaked/Wild ride version marker is missing");
    var options = DecodeOptions(
      name,
      source,
      context,
      CheckedAdd(address, 8, name),
      ReadUInt32(header, headerOffset + 8),
      out commonEndAddress);

    IReadOnlyList<AnimatedRideStationLimit> stationLimits = [];
    var attractivity = ReadUInt32(header, headerOffset);
    var unknowns = UnknownFields();
    if (version != AnimatedRideVersion.Vanilla) {
      attractivity = ReadUInt32(header, headerOffset + 28);
      var stationCount = ReadUInt32(header, headerOffset + 32);
      ValidateCount(stationCount, name, "station limits");
      var stationAddress = ReadRelocatedPointer(
        source,
        CheckedAdd(address, 36, name),
        ReadUInt32(header, headerOffset + 36),
        stationCount > 0,
        name,
        "station-limit array",
        context);
      if (stationCount > 0) {
        if (stationAddress != commonEndAddress)
          throw Invalid(name,
            $"station-limit array is at {stationAddress} instead of writer address " +
            commonEndAddress);
        var bytes = ReadExact(
          source,
          stationAddress,
          CheckedArrayLength(stationCount, StationLimitSize, name, "station-limit array"),
          name,
          "station-limit array",
          context,
          AnimatedRideStorage.Common);
        context.ReserveObjects(stationCount, name, "station-limit models");
        var limits = new AnimatedRideStationLimit[Convert.ToInt32(stationCount)];
        foreach (var index in Enumerable.Range(0, limits.Length)) {
          var offset = checked(index * StationLimitSize);
          limits[index] = new AnimatedRideStationLimit(
            ReadInt32(bytes, offset),
            ReadInt32(bytes, offset + 4),
            ReadInt32(bytes, offset + 8),
            ReadUInt32(bytes, offset + 12));
        }
        stationLimits = limits;
        commonEndAddress = CheckedAdd(
          commonEndAddress,
          CheckedArrayLength(stationCount, StationLimitSize, name, "station-limit array"),
          name);
      }
      unknowns = version == AnimatedRideVersion.Wild
        ? UnknownFields(
          (40, ReadUInt32(header, headerOffset + 40)),
          (44, ReadUInt32(header, headerOffset + 44)),
          (48, ReadUInt32(header, headerOffset + 48)),
          (52, ReadUInt32(header, headerOffset + 52)),
          (56, ReadUInt32(header, headerOffset + 56)),
          (60, ReadUInt32(wildExtension, 0)),
          (64, ReadUInt32(wildExtension, 4)))
        : UnknownFields(
          (40, ReadUInt32(header, headerOffset + 40)),
          (44, ReadUInt32(header, headerOffset + 44)),
          (48, ReadUInt32(header, headerOffset + 48)),
          (52, ReadUInt32(header, headerOffset + 52)),
          (56, ReadUInt32(header, headerOffset + 56)));
    }

    context.ReserveObjects(2, name, "decoded ride settings");
    return new AnimatedRideSettings(
      attractivity,
      ReadUInt32(header, headerOffset + 4),
      options,
      ReadUInt32(header, headerOffset + 12),
      ReadInt32(header, headerOffset + 16),
      ReadUInt32(header, headerOffset + 20),
      stationLimits,
      unknowns);
  }

  private static IReadOnlyList<AnimatedRideOption> DecodeOptions(
    string name,
    IAnimatedRideDataSource source,
    DecodeContext context,
    uint fieldAddress,
    uint storedPointer,
    out uint endAddress
  ) {
    var arrayAddress = ReadRelocatedPointer(
      source,
      fieldAddress,
      storedPointer,
      required: true,
      name,
      "ride-option pointer array",
      context);
    var pointers = new List<uint>();
    var foundSentinel = false;
    foreach (var index in Enumerable.Range(0, Convert.ToInt32(MaximumArrayCount) + 1)) {
      var slotAddress = CheckedAdd(arrayAddress, checked(index * 4), name);
      var bytes = ReadExact(
        source,
        slotAddress,
        4,
        name,
        "ride-option pointer",
        context,
        AnimatedRideStorage.Common);
      var rawPointer = ReadUInt32(bytes, 0);
      if (!source.TryGetRelocationSource(slotAddress, out var pointer)) {
        if (rawPointer != 0)
          throw Invalid(name,
            $"ride-option pointer {index} contains an unproven address {rawPointer}");
        foundSentinel = true;
        break;
      }
      if (rawPointer != pointer)
        throw Invalid(name,
          $"ride-option pointer {index} does not match its relocation target");
      context.ExpectRelocation(slotAddress);
      if (pointers.Count >= MaximumArrayCount)
        throw Invalid(name,
          $"ride-option count exceeds the decoder limit {MaximumArrayCount}");
      context.ReserveObjects(1, name, "ride-option pointer");
      pointers.Add(pointer);
    }
    if (!foundSentinel)
      throw Invalid(name,
        $"ride-option pointer array has no sentinel within {MaximumArrayCount + 1} slots");

    var optionAddress = CheckedAdd(arrayAddress, checked((pointers.Count + 1) * 4), name);
    var options = new AnimatedRideOption[pointers.Count];
    foreach (var index in Enumerable.Range(0, pointers.Count)) {
      if (pointers[index] != optionAddress)
        throw Invalid(name,
          $"ride option {index} is at {pointers[index]} instead of writer address " +
          optionAddress);
      var typeBytes = ReadExact(
        source,
        optionAddress,
        4,
        name,
        $"ride option {index} type",
        context,
        AnimatedRideStorage.Common);
      var type = ReadUInt32(typeBytes, 0);
      var size = OptionSize(type, name);
      var payloadBytes = ReadExact(
        source,
        CheckedAdd(optionAddress, 4, name),
        size - 4,
        name,
        $"ride option {index} payload",
        context,
        AnimatedRideStorage.Common);
      var payload = new uint[(size - 4) / 4];
      foreach (var payloadIndex in Enumerable.Range(0, payload.Length))
        payload[payloadIndex] = ReadUInt32(payloadBytes, payloadIndex * 4);
      ValidateOptionPayload(type, payloadBytes, name, index);
      context.ReserveObjects(
        Convert.ToUInt64(payload.Length) + 1,
        name,
        $"ride option {index} model");
      options[index] = new AnimatedRideOption(type, payload);
      optionAddress = CheckedAdd(optionAddress, size, name);
    }
    endAddress = optionAddress;
    return options;
  }

  private static IReadOnlyList<AnimatedRideShowItem> DecodeShowItems(
    string name,
    IAnimatedRideDataSource source,
    DecodeContext context,
    uint fieldAddress,
    uint storedPointer,
    uint count,
    uint expectedAddress
  ) {
    ValidateCount(count, name, "show items");
    var address = ReadRelocatedPointer(
      source,
      fieldAddress,
      storedPointer,
      count > 0,
      name,
      "show-item array",
      context);
    if (count == 0) return [];
    if (address != expectedAddress)
      throw Invalid(name,
        $"show-item array is at {address} instead of writer address {expectedAddress}");
    var bytes = ReadExact(
      source,
      address,
      CheckedArrayLength(count, ShowItemSize, name, "show-item array"),
      name,
      "show-item array",
      context,
      AnimatedRideStorage.Common);
    context.ReserveObjects(count, name, "show-item models");
    var items = new AnimatedRideShowItem[Convert.ToInt32(count)];
    foreach (var index in Enumerable.Range(0, items.Length)) {
      var offset = checked(index * ShowItemSize);
      items[index] = new AnimatedRideShowItem(
        ReadUInt32(bytes, offset),
        ReadString(
          source,
          CheckedAdd(address, offset + 4, name),
          ReadUInt32(bytes, offset + 4),
          name,
          $"show item {index} name",
          context));
    }
    return items;
  }

  private static string ReadExpectedReference(
    IAnimatedRideDataSource source,
    OvlLoaderEntry owner,
    uint fieldAddress,
    uint storedPointer,
    string expectedTag,
    bool allowEmptyName,
    string rideName,
    string description,
    ISet<uint> expectedReferenceFields
  ) {
    expectedReferenceFields.Add(fieldAddress);
    if (!source.ResourceReferences.TryGetValue(fieldAddress, out var reference)) {
      if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
        throw Invalid(rideName,
          $"{description} contains conflicting direct pointer data");
      throw Invalid(rideName, $"{description} SymbolRef is missing");
    }
    if (!SameLoader(reference.Owner, owner))
      throw Invalid(rideName, $"{description} SymbolRef is owned by another loader");
    if (!HasTag(reference.Symbol, expectedTag) ||
        (!allowEmptyName && reference.Symbol.Length == expectedTag.Length + 1))
      throw Invalid(rideName,
        $"{description} SymbolRef '{reference.Symbol}' is not a valid {expectedTag} reference");
    if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(rideName,
        $"{description} SymbolRef field contains conflicting pointer data");
    return reference.Symbol;
  }

  private static void ValidateOwnedReferences(
    string name,
    OvlLoaderEntry owner,
    IReadOnlySet<uint> expectedFields,
    IAnimatedRideDataSource source
  ) {
    foreach (var reference in source.ResourceReferences) {
      if (!SameLoader(reference.Value.Owner, owner)) continue;
      if (!expectedFields.Contains(reference.Key))
        throw Invalid(name,
          $"loader owns a SymbolRef outside the ANR layout at {reference.Key}");
    }
  }

  private static string ReadString(
    IAnimatedRideDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string rideName,
    string description,
    DecodeContext context
  ) {
    context.ExpectRelocation(fieldAddress);
    if (!source.TryGetRelocationSource(fieldAddress, out var address))
      throw Invalid(rideName, $"{description} is not a relocated pointer");
    if (storedPointer != address)
      throw Invalid(rideName, $"{description} does not match its relocation target");
    if (!source.TryGetNullTerminatedStringByteLength(
          address, MaximumStringBytes, out var length))
      throw Invalid(rideName,
        $"{description} is missing, unterminated, or exceeds {MaximumStringBytes} bytes");
    context.ReserveBytes(Convert.ToUInt64(length) + 1, rideName, description);
    if (!source.TryReadNullTerminatedString(address, length + 1, out var value))
      throw Invalid(rideName, $"{description} changed while it was being decoded");
    return value;
  }

  private static uint ReadRelocatedPointer(
    IAnimatedRideDataSource source,
    uint fieldAddress,
    uint storedPointer,
    bool required,
    string rideName,
    string description,
    DecodeContext context
  ) {
    if (required) context.ExpectRelocation(fieldAddress);
    if (!source.TryGetRelocationSource(fieldAddress, out var address)) {
      if (storedPointer != 0)
        throw Invalid(rideName,
          $"{description} contains an unproven pointer {storedPointer}");
      if (required) throw Invalid(rideName, $"{description} is not a relocated pointer");
      return 0;
    }
    if (!required)
      throw Invalid(rideName,
        $"empty {description} unexpectedly has a relocation");
    if (storedPointer != address)
      throw Invalid(rideName, $"{description} does not match its relocation target");
    return address;
  }

  private static byte[] ReadExact(
    IAnimatedRideDataSource source,
    uint address,
    int length,
    string rideName,
    string description,
    DecodeContext context,
    AnimatedRideStorage storage = AnimatedRideStorage.Unique
  ) {
    if (length < 0) throw Invalid(rideName, $"{description} has a negative length");
    context.ReserveBytes(Convert.ToUInt64(length), rideName, description);
    if (length == 0) return [];
    context.RequireStorage(source, address, length, storage, rideName, description);
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(rideName, $"{description} is outside the archive or truncated");
    return bytes;
  }

  private static int OptionSize(uint type, string name) => type switch {
    0 => 4,
    1 => 8,
    2 or 3 or 4 or 5 or 6 or 7 or 10 or 11 => 12,
    8 => 24,
    9 or 12 => 16,
    _ => throw Invalid(name, $"unsupported ride-option type {type}"),
  };

  private static void ValidateOptionPayload(
    uint type,
    byte[] payload,
    string name,
    int optionIndex
  ) {
    IEnumerable<int> floatOffsets = type switch {
      0 or 1 => [],
      2 or 3 or 4 or 5 or 6 or 7 => [4],
      8 => [0, 4, 8, 16],
      9 or 12 => [0, 4, 8],
      10 or 11 => [0],
      _ => throw Invalid(name, $"unsupported ride-option type {type}"),
    };
    foreach (var offset in floatOffsets) {
      if (!float.IsFinite(BitConverter.ToSingle(payload, offset)))
        throw Invalid(name,
          $"ride option {optionIndex} type {type} contains a non-finite value at offset " +
          (offset + 4));
    }
  }

  private static void ValidateCount(uint count, string name, string description) {
    if (count > MaximumArrayCount)
      throw Invalid(name,
        $"{description} count {count} exceeds the decoder limit {MaximumArrayCount}");
  }

  private static int CheckedArrayLength(
    uint count,
    int stride,
    string name,
    string description
  ) {
    var length = Convert.ToUInt64(count) * Convert.ToUInt64(stride);
    if (length > int.MaxValue)
      throw Invalid(name, $"{description} byte length exceeds the decoder limit");
    return Convert.ToInt32(length);
  }

  private static IReadOnlyDictionary<int, uint> UnknownFields(
    params (int Offset, uint Value)[] fields
  ) => new ReadOnlyDictionary<int, uint>(
    fields.ToDictionary(field => field.Offset, field => field.Value));

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static bool HasTag(string key, string tag) =>
    key.EndsWith($":{tag}", StringComparison.OrdinalIgnoreCase);

  private static string ToCommonPath(string path) {
    const string uniqueSuffix = ".unique.ovl";
    return path[..^uniqueSuffix.Length] + ".common.ovl";
  }

  private static uint CheckedAdd(uint address, int offset, string name) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(name, "pointer address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static uint ReadUInt32(byte[] bytes, int offset) =>
    BitConverter.ToUInt32(bytes, offset);

  private static int ReadInt32(byte[] bytes, int offset) =>
    BitConverter.ToInt32(bytes, offset);

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Animated ride '{name}' is malformed: {message}.");

  private enum AnimatedRideStorage {
    Unique,
    Common,
  }

  private sealed class DecodeContext(AnimatedRideDecodeLimits limits) {
    private ulong decodedBytes;
    private ulong decodedObjects;
    private readonly HashSet<uint> expectedRelocations = [];
    private readonly List<(uint Address, int Length)> modeledSpans = [];
    private OvlDataBlockIdentity? commonBlock;
    private string? expectedCommonPath;
    private string? expectedUniquePath;
    private OvlDataBlockIdentity? uniqueBlock;

    public void BeginResource(
      IAnimatedRideDataSource source,
      OvlLoaderEntry owner,
      string rideName
    ) {
      expectedRelocations.Clear();
      modeledSpans.Clear();
      commonBlock = null;
      expectedUniquePath = owner.SourcePath;
      expectedCommonPath = ToCommonPath(owner.SourcePath);
      if (!source.TryGetDataBlock(owner.DataAddress, 1, out var block))
        throw Invalid(rideName, "loader data address is outside a proven storage block");
      if (!string.Equals(
            block.SourcePath, expectedUniquePath, StringComparison.OrdinalIgnoreCase) ||
          block.TypeIndex != 2)
        throw Invalid(rideName,
          "loader data is not in its exact unique type-2 storage block");
      uniqueBlock = block;
    }

    public void ExpectRelocation(uint fieldAddress) =>
      expectedRelocations.Add(fieldAddress);

    public void RequireStorage(
      IAnimatedRideDataSource source,
      uint address,
      int length,
      AnimatedRideStorage storage,
      string rideName,
      string description
    ) {
      if (length == 0) return;
      if (!source.TryGetDataBlock(address, length, out var block))
        throw Invalid(rideName,
          $"{description} is outside one exact archive storage block");

      if (storage == AnimatedRideStorage.Unique) {
        if (!string.Equals(
              block.SourcePath, expectedUniquePath, StringComparison.OrdinalIgnoreCase) ||
            block.TypeIndex != 2 ||
            !ReferenceEquals(block.Identity, uniqueBlock!.Identity))
          throw Invalid(rideName,
            $"{description} is not in the loader's exact unique type-2 storage block");
      } else {
        if (!string.Equals(
              block.SourcePath, expectedCommonPath, StringComparison.OrdinalIgnoreCase) ||
            block.TypeIndex != 2)
          throw Invalid(rideName,
            $"{description} is not in the paired common type-2 storage block");
        if (commonBlock == null)
          commonBlock = block;
        else if (!ReferenceEquals(block.Identity, commonBlock.Identity))
          throw Invalid(rideName,
            $"{description} is not in the exact common storage block");
      }

      modeledSpans.Add((address, length));
    }

    public void ValidateModeledRelocations(
      IAnimatedRideDataSource source,
      string rideName
    ) {
      var checkedFields = new HashSet<uint>();
      foreach (var span in modeledSpans) {
        if (span.Length % sizeof(uint) != 0)
          throw Invalid(rideName, "modeled span is not a whole number of 32-bit fields");
        foreach (var index in Enumerable.Range(0, span.Length / sizeof(uint))) {
          var fieldAddress = CheckedAdd(
            span.Address,
            checked(index * sizeof(uint)),
            rideName);
          if (!checkedFields.Add(fieldAddress) ||
              expectedRelocations.Contains(fieldAddress)) continue;
          if (source.TryGetRelocationSource(fieldAddress, out _))
            throw Invalid(rideName,
              $"unexpected relocation targets modeled scalar field {fieldAddress}");
        }
      }
    }

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

  private sealed class OvlAnimatedRideDataSource : IAnimatedRideDataSource {
    private readonly IReadOnlyList<OvlLoaderEntry> loaders;
    private readonly Ovl ovl;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;
    private readonly OvlCommonStringTable stringTable;

    public OvlAnimatedRideDataSource(Ovl ovl, DecodeContext context) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the ANR decoder limit " +
          $"{MaximumResourceCount}.");

      context.ReserveObjects(
        OvlLoaderIndexBudget.Calculate(ovl.LoaderEntriesInOrder.Count),
        "OVL",
        "loader metadata index");
      loaders = ovl.LoaderEntriesInOrder.ToArray();
      var mutableLoaders = new Dictionary<uint, List<OvlLoaderEntry>>();
      foreach (var entry in loaders) {
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

    public OvlLoaderEntry GetAnimatedRideLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact anr loader-table entry");
      if (candidates.Count != 1)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact anr loader-table entry");
      var owner = candidates[0];
      if (owner.Tag.ToFileType() != FileType.AnimatedRide ||
          !string.Equals(owner.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact anr loader-table entry");
      return owner;
    }

    public IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner) {
      if (!ovl.TryResolveDataBlock(owner.DataAddress, 1, out var ownerBlock)) return [];
      var result = new List<OvlLoaderEntry>();
      foreach (var loader in loaders.Where(loader => string.Equals(
                 loader.SourcePath, owner.SourcePath, StringComparison.OrdinalIgnoreCase))) {
        if (!ovl.TryResolveDataBlock(loader.DataAddress, 1, out var candidateBlock) ||
            !ReferenceEquals(ownerBlock.Identity, candidateBlock.Identity)) continue;
        result.Add(loader);
      }
      return result.AsReadOnly();
    }

    public bool TryGetDataBlock(
      uint address,
      int length,
      out OvlDataBlockIdentity block
    ) {
      if (ovl.TryResolveDataBlock(address, length, out var resolved)) {
        block = resolved;
        return true;
      }
      block = null!;
      return false;
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

internal readonly record struct AnimatedRideDecodeLimits(
  ulong MaximumBytes,
  ulong MaximumObjects
) {
  public static AnimatedRideDecodeLimits Default { get; } =
    new(256UL * 1024 * 1024, 1_000_000);
}

internal interface IAnimatedRideDataSource {
  IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
  IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner);
  bool TryGetDataBlock(uint address, int length, out OvlDataBlockIdentity block);
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryGetNullTerminatedStringByteLength(uint address, int maximumLength, out int length);
  bool TryReadNullTerminatedString(uint address, int maximumLength, out string value);
}
