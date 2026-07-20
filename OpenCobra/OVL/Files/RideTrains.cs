// Ride Trains
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>The three serialized RCT3 ride-train layouts.</summary>
public enum RideTrainVersion : uint {
  Vanilla = 0,
  Soaked = 2,
  Wild = 3,
}

/// <summary>The car resources and allowed consist lengths for one ride train.</summary>
public sealed record RideTrainCars(
  string Front,
  string? Second,
  string? Middle,
  string? Penultimate,
  string? Rear,
  string? Link,
  uint MinimumCount,
  uint MaximumCount,
  uint DefaultCount,
  string? WildUnknown
);

/// <summary>The three fixed speed fields in <c>RideTrain_V</c>.</summary>
public sealed record RideTrainSpeedSettings(float Unknown1, float Unknown2, float Unknown3);

/// <summary>The fixed camera settings in <c>RideTrain_V</c>.</summary>
public sealed record RideTrainCameraSettings(
  float LookAhead,
  float GDisplacementMultiplier,
  float GDisplacementMaximum,
  float GDisplacementSmoothing,
  float ShakeMinimumSpeed,
  float ShakeFactor,
  float ShakeHorizontalMaximum,
  float ShakeVerticalMaximum,
  float ChainShakeFactor,
  float ChainShakeHorizontalMaximum,
  float ChainShakeVerticalMaximum,
  float Smoothing,
  float LookAheadTiltFactor
);

/// <summary>The fixed water-motion settings in <c>RideTrain_V</c>.</summary>
public sealed record RideTrainWaterSettings(
  float Unknown1,
  float Unknown2,
  float Unknown3,
  float Unknown4,
  float Unknown5,
  float Unknown6,
  float FreeRoamCurveAngle,
  float Unknown7
);

/// <summary>The eight trailing unknown floats in <c>RideTrain_V</c>.</summary>
public sealed record RideTrainUnknownSettings(
  float Unknown37,
  float Unknown38,
  float Unknown39,
  float Unknown40,
  float Unknown41,
  float Unknown42,
  float Unknown43,
  float Unknown44
);

/// <summary>The Soaked fields shared by Soaked and Wild ride trains.</summary>
public sealed record RideTrainExpansionSettings(uint Icon, uint Unknown51);

/// <summary>The Wild-only fixed extension fields in <c>RideTrain_Wext</c>.</summary>
public sealed record RideTrainWildSettings(
  float AirboatUnknown1,
  float AirboatUnknown2,
  float AirboatUnknown3,
  float AirboatUnknown4,
  float AirboatUnknown5,
  float AirboatUnknown6,
  uint Unknown59,
  uint Unknown60,
  float FrequentFallerSiezmicUnknown
);

/// <summary>A decoded RCT3 <c>rit</c> resource.</summary>
public sealed record RideTrain(
  string Name,
  RideTrainVersion Version,
  string InternalName,
  string InternalDescription,
  RideTrainCars Cars,
  RideTrainSpeedSettings Speed,
  RideTrainCameraSettings Camera,
  RideTrainWaterSettings Water,
  uint OvertakeFlag,
  RideTrainUnknownSettings Unknowns,
  string? LeftLiftSpline,
  string? RightLiftSpline,
  string? Station,
  RideTrainExpansionSettings? Expansion,
  RideTrainWildSettings? Wild
);

/// <summary>Decodes relocated <c>rit</c> resources without resolving car meshes.</summary>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/train.h">
/// rct3-importer train layouts
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerRIT.cpp">
/// rct3-importer RideTrain serializer
/// </seealso>
public static class RideTrains {
  // Frontier pointers are 32-bit. train.h and ManagerRIT.cpp emit exactly these fixed layouts.
  private const int VanillaSize = 188;
  private const int SoakedSize = 204;
  private const int WildSize = 244;
  private const int MaximumTrainCount = 64 * 1024;
  private const int MaximumResourceCount = 1_000_000;
  private const int MaximumStringBytes = 4 * 1024;

  /// <summary>Decodes every ride-train resource from the unique half of an OVL pair.</summary>
  public static IReadOnlyList<RideTrain> Extract(Ovl ovl) =>
    Extract(ovl, RideTrainDecodeLimits.Default);

  internal static IReadOnlyList<RideTrain> Extract(Ovl ovl, RideTrainDecodeLimits limits) {
    ArgumentNullException.ThrowIfNull(ovl);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the RIT decoder limit {MaximumResourceCount}.");

    var context = new DecodeContext(limits);
    var files = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file =>
      file.Type == FileType.RideTrain &&
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))) {
      if (files.Count >= MaximumTrainCount)
        throw Invalid(file.Name,
          $"train count exceeds the decoder limit {MaximumTrainCount}");
      context.ReserveObjects(1, file.Name, "ride-train resource index");
      files.Add(file);
    }

    var source = new OvlRideTrainDataSource(ovl, context);
    var trains = new List<RideTrain>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier RIT at {address}");
      var owner = source.GetTrainLoader(file, address);
      trains.Add(Decode(file.Name, owner, source, context));
    }
    return trains;
  }

  internal static RideTrain Decode(
    string name,
    OvlLoaderEntry owner,
    IRideTrainDataSource source
  ) => Decode(name, owner, source, new DecodeContext(RideTrainDecodeLimits.Default));

  internal static RideTrain Decode(
    string name,
    OvlLoaderEntry owner,
    IRideTrainDataSource source,
    RideTrainDecodeLimits limits
  ) => Decode(name, owner, source, new DecodeContext(limits));

  private static RideTrain Decode(
    string name,
    OvlLoaderEntry owner,
    IRideTrainDataSource source,
    DecodeContext context
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.RideTrain)
      throw Invalid(name, $"loader type '{owner.Tag}' is not rit");

    var address = owner.DataAddress;
    var vanilla = ReadExact(source, address, VanillaSize, name, "vanilla header", context);
    var marker = ReadUInt32(vanilla, 0);
    RideTrainVersion version;
    byte[] expansion = [];
    byte[] wild = [];
    uint nameFieldAddress;
    uint storedNamePointer;
    if (marker == uint.MaxValue) {
      if (source.TryGetRelocationSource(address, out _))
        throw Invalid(name, "version marker is also a relocated pointer");
      expansion = ReadExact(
        source,
        CheckedAdd(address, VanillaSize, name),
        SoakedSize - VanillaSize,
        name,
        "expansion header",
        context);
      version = ReadUInt32(expansion, 0) switch {
        2 => RideTrainVersion.Soaked,
        3 => RideTrainVersion.Wild,
        var value => throw Invalid(name, $"unsupported expansion version {value}"),
      };
      nameFieldAddress = CheckedAdd(address, 192, name);
      storedNamePointer = ReadUInt32(expansion, 4);
      if (version == RideTrainVersion.Wild)
        wild = ReadExact(
          source,
          CheckedAdd(address, SoakedSize, name),
          WildSize - SoakedSize,
          name,
          "Wild header",
          context);
    } else {
      version = RideTrainVersion.Vanilla;
      nameFieldAddress = address;
      storedNamePointer = marker;
    }

    var internalName = ReadRequiredString(
      source, nameFieldAddress, storedNamePointer, name, "internal name", context);
    var description = ReadRequiredString(
      source,
      CheckedAdd(address, 4, name),
      ReadUInt32(vanilla, 4),
      name,
      "internal description",
      context);
    var station = ReadOptionalString(
      source,
      CheckedAdd(address, 184, name),
      ReadUInt32(vanilla, 184),
      name,
      "station name",
      context);

    var expectedReferenceFields = new HashSet<uint>();
    string? ReadRef(int offset, string tag, bool required, string description) {
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

    var front = ReadRef(8, "ric", required: true, "front car");
    var second = ReadRef(12, "ric", required: false, "second car");
    var middle = ReadRef(16, "ric", required: false, "middle car");
    var penultimate = ReadRef(20, "ric", required: false, "penultimate car");
    var rear = ReadRef(24, "ric", required: false, "rear car");
    var link = ReadRef(28, "ric", required: false, "car link");
    var leftLiftSpline = ReadRef(176, "spl", required: false, "left lift spline");
    var rightLiftSpline = ReadRef(180, "spl", required: false, "right lift spline");

    string? wildUnknownCar = null;
    if (version == RideTrainVersion.Wild) {
      var fieldAddress = CheckedAdd(address, 204, name);
      expectedReferenceFields.Add(fieldAddress);
      wildUnknownCar = ReadResourceReference(
        source,
        owner,
        fieldAddress,
        ReadUInt32(wild, 0),
        "ric",
        required: false,
        name,
        "Wild unknown car");
    }
    ValidateOwnedReferences(name, owner, expectedReferenceFields, source);

    var cars = new RideTrainCars(
      front!, second, middle, penultimate, rear, link,
      ReadUInt32(vanilla, 32),
      ReadUInt32(vanilla, 36),
      ReadUInt32(vanilla, 40),
      wildUnknownCar);
    var speed = new RideTrainSpeedSettings(
      ReadFiniteSingle(vanilla, 44, name, "speed unknown 1"),
      ReadFiniteSingle(vanilla, 48, name, "speed unknown 2"),
      ReadFiniteSingle(vanilla, 52, name, "speed unknown 3"));
    var camera = new RideTrainCameraSettings(
      ReadFiniteSingle(vanilla, 56, name, "camera look-ahead"),
      ReadFiniteSingle(vanilla, 60, name, "camera G displacement multiplier"),
      ReadFiniteSingle(vanilla, 64, name, "camera G displacement maximum"),
      ReadFiniteSingle(vanilla, 68, name, "camera G displacement smoothing"),
      ReadFiniteSingle(vanilla, 72, name, "camera shake minimum speed"),
      ReadFiniteSingle(vanilla, 76, name, "camera shake factor"),
      ReadFiniteSingle(vanilla, 80, name, "camera shake horizontal maximum"),
      ReadFiniteSingle(vanilla, 84, name, "camera shake vertical maximum"),
      ReadFiniteSingle(vanilla, 88, name, "camera chain shake factor"),
      ReadFiniteSingle(vanilla, 92, name, "camera chain shake horizontal maximum"),
      ReadFiniteSingle(vanilla, 96, name, "camera chain shake vertical maximum"),
      ReadFiniteSingle(vanilla, 100, name, "camera smoothing"),
      ReadFiniteSingle(vanilla, 104, name, "camera look-ahead tilt factor"));
    var water = new RideTrainWaterSettings(
      ReadFiniteSingle(vanilla, 108, name, "water unknown 1"),
      ReadFiniteSingle(vanilla, 112, name, "water unknown 2"),
      ReadFiniteSingle(vanilla, 116, name, "water unknown 3"),
      ReadFiniteSingle(vanilla, 120, name, "water unknown 4"),
      ReadFiniteSingle(vanilla, 124, name, "water unknown 5"),
      ReadFiniteSingle(vanilla, 128, name, "water unknown 6"),
      ReadFiniteSingle(vanilla, 132, name, "water free-roam curve angle"),
      ReadFiniteSingle(vanilla, 136, name, "water unknown 7"));
    var unknowns = new RideTrainUnknownSettings(
      ReadFiniteSingle(vanilla, 144, name, "unknown 37"),
      ReadFiniteSingle(vanilla, 148, name, "unknown 38"),
      ReadFiniteSingle(vanilla, 152, name, "unknown 39"),
      ReadFiniteSingle(vanilla, 156, name, "unknown 40"),
      ReadFiniteSingle(vanilla, 160, name, "unknown 41"),
      ReadFiniteSingle(vanilla, 164, name, "unknown 42"),
      ReadFiniteSingle(vanilla, 168, name, "unknown 43"),
      ReadFiniteSingle(vanilla, 172, name, "unknown 44"));

    var expansionSettings = version == RideTrainVersion.Vanilla
      ? null
      : new RideTrainExpansionSettings(
        ReadUInt32(expansion, 8), ReadUInt32(expansion, 12));
    var wildSettings = version != RideTrainVersion.Wild
      ? null
      : new RideTrainWildSettings(
        ReadFiniteSingle(wild, 4, name, "Wild airboat unknown 1"),
        ReadFiniteSingle(wild, 8, name, "Wild airboat unknown 2"),
        ReadFiniteSingle(wild, 12, name, "Wild airboat unknown 3"),
        ReadFiniteSingle(wild, 16, name, "Wild airboat unknown 4"),
        ReadFiniteSingle(wild, 20, name, "Wild airboat unknown 5"),
        ReadFiniteSingle(wild, 24, name, "Wild airboat unknown 6"),
        ReadUInt32(wild, 28),
        ReadUInt32(wild, 32),
        ReadFiniteSingle(wild, 36, name, "Wild Frequent Faller/Siezmic unknown"));
    context.ReserveObjects(8, name, "decoded ride-train model");

    return new RideTrain(
      name,
      version,
      internalName,
      description,
      cars,
      speed,
      camera,
      water,
      ReadUInt32(vanilla, 140),
      unknowns,
      leftLiftSpline,
      rightLiftSpline,
      station,
      expansionSettings,
      wildSettings);
  }

  private static string? ReadResourceReference(
    IRideTrainDataSource source,
    OvlLoaderEntry owner,
    uint fieldAddress,
    uint storedPointer,
    string expectedTag,
    bool required,
    string trainName,
    string description
  ) {
    if (!source.ResourceReferences.TryGetValue(fieldAddress, out var reference)) {
      if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
        throw Invalid(trainName,
          $"{description} contains conflicting direct pointer data");
      if (required)
        throw Invalid(trainName, $"{description} SymbolRef is missing");
      return null;
    }
    if (!SameLoader(reference.Owner, owner))
      throw Invalid(trainName, $"{description} SymbolRef is owned by another loader");
    if (!HasTag(reference.Symbol, expectedTag))
      throw Invalid(trainName,
        $"{description} SymbolRef '{reference.Symbol}' is not {expectedTag}");
    if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(trainName,
        $"{description} SymbolRef field contains conflicting pointer data");
    return reference.Symbol;
  }

  private static void ValidateOwnedReferences(
    string trainName,
    OvlLoaderEntry owner,
    IReadOnlySet<uint> expectedFields,
    IRideTrainDataSource source
  ) {
    foreach (var reference in source.ResourceReferences) {
      if (!SameLoader(reference.Value.Owner, owner)) continue;
      if (!expectedFields.Contains(reference.Key))
        throw Invalid(trainName,
          $"loader owns a SymbolRef outside the RIT layout at {reference.Key}");
    }
  }

  private static string ReadRequiredString(
    IRideTrainDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string trainName,
    string description,
    DecodeContext context
  ) {
    var value = ReadString(
      source, fieldAddress, storedPointer, trainName, description, context, required: true);
    return value!;
  }

  private static string? ReadOptionalString(
    IRideTrainDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string trainName,
    string description,
    DecodeContext context
  ) => ReadString(
    source, fieldAddress, storedPointer, trainName, description, context, required: false);

  private static string? ReadString(
    IRideTrainDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string trainName,
    string description,
    DecodeContext context,
    bool required
  ) {
    if (!source.TryGetRelocationSource(fieldAddress, out var address)) {
      if (storedPointer != 0)
        throw Invalid(trainName,
          $"{description} contains an unproven pointer {storedPointer}");
      if (required) throw Invalid(trainName, $"{description} is not a relocated pointer");
      return null;
    }
    if (storedPointer != address)
      throw Invalid(trainName, $"{description} does not match its relocation target");
    if (!source.TryGetNullTerminatedStringByteLength(
          address, MaximumStringBytes, out var length) || (required && length == 0))
      throw Invalid(trainName,
        $"{description} is missing, unterminated, or exceeds {MaximumStringBytes} bytes");
    context.ReserveBytes(Convert.ToUInt64(length) + 1, trainName, description);
    if (!source.TryReadNullTerminatedString(address, length + 1, out var value) ||
        (required && string.IsNullOrEmpty(value)))
      throw Invalid(trainName, $"{description} changed while it was being decoded");
    return value;
  }

  private static byte[] ReadExact(
    IRideTrainDataSource source,
    uint address,
    int length,
    string trainName,
    string description,
    DecodeContext context
  ) {
    context.ReserveBytes(Convert.ToUInt64(length), trainName, description);
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(trainName, $"{description} is outside the archive or truncated");
    return bytes;
  }

  private static float ReadFiniteSingle(
    byte[] bytes,
    int offset,
    string trainName,
    string description
  ) {
    var value = BitConverter.ToSingle(bytes, offset);
    if (!float.IsFinite(value))
      throw Invalid(trainName, $"{description} is not finite");
    return value;
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static bool HasTag(string key, string tag) =>
    key.EndsWith($":{tag}", StringComparison.OrdinalIgnoreCase);

  private static uint CheckedAdd(uint address, int offset, string trainName) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(trainName, "pointer address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static uint ReadUInt32(byte[] bytes, int offset) =>
    BitConverter.ToUInt32(bytes, offset);

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Ride train '{name}' is malformed: {message}.");

  private sealed class DecodeContext(RideTrainDecodeLimits limits) {
    private ulong decodedBytes;
    private ulong decodedObjects;

    public void ReserveBytes(ulong count, string trainName, string description) {
      if (count > limits.MaximumBytes || decodedBytes > limits.MaximumBytes - count)
        throw Invalid(trainName,
          $"aggregate decode bytes exceed the limit {limits.MaximumBytes} while reading " +
          description);
      decodedBytes += count;
    }

    public void ReserveObjects(ulong count, string trainName, string description) {
      if (count > limits.MaximumObjects || decodedObjects > limits.MaximumObjects - count)
        throw Invalid(trainName,
          $"aggregate decoded objects exceed the limit {limits.MaximumObjects} while reading " +
          description);
      decodedObjects += count;
    }
  }

  private sealed class OvlRideTrainDataSource : IRideTrainDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;
    private readonly OvlCommonStringTable stringTable;

    public OvlRideTrainDataSource(Ovl ovl, DecodeContext context) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the RIT decoder limit " +
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

    public OvlLoaderEntry GetTrainLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact rit loader-table entry");
      var matches = candidates.Where(entry =>
        entry.Tag.ToFileType() == FileType.RideTrain &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact rit loader-table entry");
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

internal readonly record struct RideTrainDecodeLimits(ulong MaximumBytes, ulong MaximumObjects) {
  public static RideTrainDecodeLimits Default { get; } =
    new(256UL * 1024 * 1024, 1_000_000);
}

internal interface IRideTrainDataSource {
  IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryGetNullTerminatedStringByteLength(uint address, int maximumLength, out int length);
  bool TryReadNullTerminatedString(uint address, int maximumLength, out string value);
}
