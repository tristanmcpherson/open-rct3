// Ride Cars
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>The three serialized RCT3 ride-car layouts.</summary>
public enum RideCarVersion : byte {
  Vanilla = 0,
  Soaked = 2,
  Wild = 3,
}

/// <summary>The moving-part axis settings in <c>RideCar_V</c>.</summary>
public sealed record RideCarAxisSettings(
  uint Type,
  float Stability,
  float Unknown1,
  float Unknown2,
  float Momentum,
  float Unknown3,
  float Unknown4,
  float Maximum
);

/// <summary>The water-bobbing settings in <c>RideCar_V</c>.</summary>
public sealed record RideCarBobbingSettings(uint Flag, float X, float Y, float Z);

/// <summary>The twenty base animation indices in <c>RideCar_V</c>.</summary>
public sealed record RideCarAnimationSettings(
  int Start,
  int StartedIdle,
  int Stop,
  int StoppedIdle,
  int BeltOpen,
  int BeltOpenIdle,
  int BeltClose,
  int BeltClosedIdle,
  int DoorsOpen,
  int DoorsOpenIdle,
  int DoorsClose,
  int DoorsClosedIdle,
  int RowBoth,
  int RowLeft,
  int RowRight,
  int CanoeStationIdle,
  int CanoeIdleFront,
  int CanoeIdleBack,
  int CanoeRowIdle,
  int CanoeRow
);

/// <summary>An optional visual and its serialized wheel-flip or axle-type value.</summary>
public sealed record RideCarVisualPart(string? Visual, uint Type);

/// <summary>The four independently referenced wheel visuals.</summary>
public sealed record RideCarWheelSettings(
  RideCarVisualPart FrontRight,
  RideCarVisualPart FrontLeft,
  RideCarVisualPart BackRight,
  RideCarVisualPart BackLeft
);

/// <summary>The independently referenced front and rear axle visuals.</summary>
public sealed record RideCarAxleSettings(RideCarVisualPart Front, RideCarVisualPart Rear);

/// <summary>The trailing Vanilla fields shared by every ride-car version.</summary>
public sealed record RideCarBaseUnknownSettings(
  uint Unknown54,
  float VanillaSoakedUnknown1,
  int SkySlideUnknown1,
  float SkySlideUnknown2,
  float SkySlideUnknown3,
  float VanillaSoakedUnknown2,
  float Unknown60
);

/// <summary>The exact thirteen-field <c>RideCar_Sext</c> layout.</summary>
public sealed record RideCarSoakedSettings(
  float SkySlideUnknown4,
  uint Unknown62,
  uint Unknown63,
  float Unknown64,
  uint SlideUnknown1,
  uint SkySlideUnknown5,
  float SuperSoakerUnknown1,
  uint SuperSoakerUnknown2,
  uint UnstableUnknown1,
  uint Unknown70,
  float SlideUnknown2,
  uint Unknown72,
  float SlideUnknown3
);

/// <summary>The exact eleven-field <c>RideCar_Wext</c> layout.</summary>
public sealed record RideCarWildSettings(
  uint FlipUnknown,
  string? FlippedVisual,
  string? FlippedMovingVisual,
  uint Unknown77,
  float DriftingUnknown,
  float Unknown79,
  string? AnimalSpecies,
  float PaddleSteamerUnknown1,
  uint PaddleSteamerUnknown2,
  int RudderAnimation,
  uint BallCoasterUnknown
);

/// <summary>A decoded RCT3 <c>ric</c> resource.</summary>
public sealed record RideCar(
  string Name,
  RideCarVersion Version,
  string InternalName,
  string Username,
  byte Seating,
  ushort Unused,
  string Visual,
  float Inertia,
  string? MovingVisual,
  float MovingInertia,
  RideCarAxisSettings Axis,
  RideCarBobbingSettings Bobbing,
  RideCarAnimationSettings Animations,
  IReadOnlyList<uint> SeatTypes,
  RideCarWheelSettings Wheels,
  RideCarAxleSettings Axles,
  RideCarBaseUnknownSettings Unknowns,
  RideCarSoakedSettings? Soaked,
  RideCarWildSettings? Wild
);

/// <summary>Decodes relocated <c>ric</c> resources without resolving their visual meshes.</summary>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/car.h">
/// rct3-importer ride-car layouts
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerRIC.cpp">
/// rct3-importer RideCar serializer
/// </seealso>
public static class RideCars {
  // Frontier pointers are 32-bit. car.h and ManagerRIC.cpp emit exactly these fixed layouts.
  private const int VanillaSize = 240;
  private const int SoakedSize = 292;
  private const int WildSize = 336;
  private const int MaximumRideCarCount = 64 * 1024;
  private const uint MaximumSeatTypeCount = 64 * 1024;
  private const int MaximumResourceCount = 1_000_000;
  private const int MaximumStringBytes = 4 * 1024;

  /// <summary>Decodes every ride-car resource from the unique half of an OVL pair.</summary>
  public static IReadOnlyList<RideCar> Extract(Ovl ovl) =>
    Extract(ovl, RideCarDecodeLimits.Default);

  internal static IReadOnlyList<RideCar> Extract(Ovl ovl, RideCarDecodeLimits limits) {
    ArgumentNullException.ThrowIfNull(ovl);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the RIC decoder limit {MaximumResourceCount}.");

    var context = new DecodeContext(limits);
    var files = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file =>
      file.Type == FileType.RideCar &&
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))) {
      if (files.Count >= MaximumRideCarCount)
        throw Invalid(file.Name,
          $"ride-car count exceeds the decoder limit {MaximumRideCarCount}");
      context.ReserveObjects(1, file.Name, "ride-car resource index");
      files.Add(file);
    }

    var source = new OvlRideCarDataSource(ovl, context);
    var cars = new List<RideCar>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier RIC at {address}");
      var owner = source.GetRideCarLoader(file, address);
      cars.Add(Decode(file.Name, owner, source, context));
    }
    return cars;
  }

  internal static RideCar Decode(
    string name,
    OvlLoaderEntry owner,
    IRideCarDataSource source
  ) => Decode(name, owner, source, new DecodeContext(RideCarDecodeLimits.Default));

  internal static RideCar Decode(
    string name,
    OvlLoaderEntry owner,
    IRideCarDataSource source,
    RideCarDecodeLimits limits
  ) => Decode(name, owner, source, new DecodeContext(limits));

  private static RideCar Decode(
    string name,
    OvlLoaderEntry owner,
    IRideCarDataSource source,
    DecodeContext context
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.RideCar)
      throw Invalid(name, $"loader type '{owner.Tag}' is not ric");

    var address = owner.DataAddress;
    var vanilla = ReadExact(source, address, VanillaSize, name, "Vanilla header", context);
    var version = vanilla[9] switch {
      0 => RideCarVersion.Vanilla,
      2 => RideCarVersion.Soaked,
      3 => RideCarVersion.Wild,
      var value => throw Invalid(name, $"unsupported version {value}"),
    };
    byte[] soaked = [];
    byte[] wild = [];
    if (version != RideCarVersion.Vanilla)
      soaked = ReadExact(
        source,
        CheckedAdd(address, VanillaSize, name),
        SoakedSize - VanillaSize,
        name,
        "Soaked header",
        context);
    if (version == RideCarVersion.Wild)
      wild = ReadExact(
        source,
        CheckedAdd(address, SoakedSize, name),
        WildSize - SoakedSize,
        name,
        "Wild header",
        context);

    var internalName = ReadRequiredString(
      source, address, ReadUInt32(vanilla, 0), name, "internal name", context);
    var username = ReadRequiredString(
      source,
      CheckedAdd(address, 4, name),
      ReadUInt32(vanilla, 4),
      name,
      "username",
      context);

    var expectedReferenceFields = new HashSet<uint>();
    string? ReadBaseRef(int offset, bool required, string description) {
      var fieldAddress = CheckedAdd(address, offset, name);
      expectedReferenceFields.Add(fieldAddress);
      return ReadResourceReference(
        source,
        owner,
        fieldAddress,
        ReadUInt32(vanilla, offset),
        "svd",
        required,
        name,
        description);
    }

    var visual = ReadBaseRef(12, required: true, "body visual");
    var movingVisual = ReadBaseRef(20, required: false, "moving visual");
    var wheels = new RideCarWheelSettings(
      new RideCarVisualPart(
        ReadBaseRef(164, required: false, "front-right wheel visual"),
        ReadUInt32(vanilla, 168)),
      new RideCarVisualPart(
        ReadBaseRef(172, required: false, "front-left wheel visual"),
        ReadUInt32(vanilla, 176)),
      new RideCarVisualPart(
        ReadBaseRef(180, required: false, "back-right wheel visual"),
        ReadUInt32(vanilla, 184)),
      new RideCarVisualPart(
        ReadBaseRef(188, required: false, "back-left wheel visual"),
        ReadUInt32(vanilla, 192)));
    var axles = new RideCarAxleSettings(
      new RideCarVisualPart(
        ReadBaseRef(196, required: false, "front axle visual"),
        ReadUInt32(vanilla, 204)),
      new RideCarVisualPart(
        ReadBaseRef(200, required: false, "rear axle visual"),
        ReadUInt32(vanilla, 208)));

    RideCarWildSettings? wildSettings = null;
    if (version == RideCarVersion.Wild) {
      string? ReadWildRef(int offset, string tag, string description) {
        var fieldAddress = CheckedAdd(address, SoakedSize + offset, name);
        expectedReferenceFields.Add(fieldAddress);
        return ReadResourceReference(
          source,
          owner,
          fieldAddress,
          ReadUInt32(wild, offset),
          tag,
          required: false,
          name,
          description);
      }

      wildSettings = new RideCarWildSettings(
        ReadUInt32(wild, 0),
        ReadWildRef(4, "svd", "flipped body visual"),
        ReadWildRef(8, "svd", "flipped moving visual"),
        ReadUInt32(wild, 12),
        ReadFiniteSingle(wild, 16, name, "Wild drifting unknown"),
        ReadFiniteSingle(wild, 20, name, "Wild unknown 79"),
        ReadWildRef(24, "was", "Wild animal species"),
        ReadFiniteSingle(wild, 28, name, "Wild paddle-steamer unknown 1"),
        ReadUInt32(wild, 32),
        ReadInt32(wild, 36),
        ReadUInt32(wild, 40));
    }
    ValidateOwnedReferences(name, owner, expectedReferenceFields, source);

    var seatTypes = ReadSeatTypes(
      source,
      address,
      vanilla,
      name,
      context);
    var axis = new RideCarAxisSettings(
      ReadUInt32(vanilla, 24),
      ReadFiniteSingle(vanilla, 32, name, "axis stability"),
      ReadFiniteSingle(vanilla, 36, name, "axis unknown 1"),
      ReadFiniteSingle(vanilla, 40, name, "axis unknown 2"),
      ReadFiniteSingle(vanilla, 44, name, "axis momentum"),
      ReadFiniteSingle(vanilla, 48, name, "axis unknown 3"),
      ReadFiniteSingle(vanilla, 52, name, "axis unknown 4"),
      ReadFiniteSingle(vanilla, 56, name, "axis maximum"));
    var bobbing = new RideCarBobbingSettings(
      ReadUInt32(vanilla, 60),
      ReadFiniteSingle(vanilla, 64, name, "bobbing X"),
      ReadFiniteSingle(vanilla, 68, name, "bobbing Y"),
      ReadFiniteSingle(vanilla, 72, name, "bobbing Z"));
    var animations = new RideCarAnimationSettings(
      ReadInt32(vanilla, 76),
      ReadInt32(vanilla, 80),
      ReadInt32(vanilla, 84),
      ReadInt32(vanilla, 88),
      ReadInt32(vanilla, 92),
      ReadInt32(vanilla, 96),
      ReadInt32(vanilla, 100),
      ReadInt32(vanilla, 104),
      ReadInt32(vanilla, 108),
      ReadInt32(vanilla, 112),
      ReadInt32(vanilla, 116),
      ReadInt32(vanilla, 120),
      ReadInt32(vanilla, 124),
      ReadInt32(vanilla, 128),
      ReadInt32(vanilla, 132),
      ReadInt32(vanilla, 136),
      ReadInt32(vanilla, 140),
      ReadInt32(vanilla, 144),
      ReadInt32(vanilla, 148),
      ReadInt32(vanilla, 152));
    var unknowns = new RideCarBaseUnknownSettings(
      ReadUInt32(vanilla, 212),
      ReadFiniteSingle(vanilla, 216, name, "Vanilla/Soaked unknown 1"),
      ReadInt32(vanilla, 220),
      ReadFiniteSingle(vanilla, 224, name, "SkySlide unknown 2"),
      ReadFiniteSingle(vanilla, 228, name, "SkySlide unknown 3"),
      ReadFiniteSingle(vanilla, 232, name, "Vanilla/Soaked unknown 2"),
      ReadFiniteSingle(vanilla, 236, name, "unknown 60"));
    var soakedSettings = version == RideCarVersion.Vanilla
      ? null
      : new RideCarSoakedSettings(
        ReadFiniteSingle(soaked, 0, name, "Soaked SkySlide unknown 4"),
        ReadUInt32(soaked, 4),
        ReadUInt32(soaked, 8),
        ReadFiniteSingle(soaked, 12, name, "Soaked unknown 64"),
        ReadUInt32(soaked, 16),
        ReadUInt32(soaked, 20),
        ReadFiniteSingle(soaked, 24, name, "Soaked SuperSoaker unknown 1"),
        ReadUInt32(soaked, 28),
        ReadUInt32(soaked, 32),
        ReadUInt32(soaked, 36),
        ReadFiniteSingle(soaked, 40, name, "Soaked slide unknown 2"),
        ReadUInt32(soaked, 44),
        ReadFiniteSingle(soaked, 48, name, "Soaked slide unknown 3"));
    context.ReserveObjects(
      version == RideCarVersion.Wild ? 14UL : 13UL,
      name,
      "decoded ride-car model");

    return new RideCar(
      name,
      version,
      internalName,
      username,
      vanilla[8],
      ReadUInt16(vanilla, 10),
      visual!,
      ReadFiniteSingle(vanilla, 16, name, "body inertia"),
      movingVisual,
      ReadFiniteSingle(vanilla, 28, name, "moving inertia"),
      axis,
      bobbing,
      animations,
      seatTypes,
      wheels,
      axles,
      unknowns,
      soakedSettings,
      wildSettings);
  }

  private static IReadOnlyList<uint> ReadSeatTypes(
    IRideCarDataSource source,
    uint address,
    byte[] vanilla,
    string carName,
    DecodeContext context
  ) {
    var count = ReadUInt32(vanilla, 156);
    if (count > MaximumSeatTypeCount)
      throw Invalid(carName,
        $"seat-type count {count} exceeds the decoder limit {MaximumSeatTypeCount}");

    var fieldAddress = CheckedAdd(address, 160, carName);
    var storedPointer = ReadUInt32(vanilla, 160);
    var hasRelocation = source.TryGetRelocationSource(fieldAddress, out var targetAddress);
    if (!hasRelocation) {
      if (storedPointer != 0)
        throw Invalid(carName, $"seat-type array contains an unproven pointer {storedPointer}");
      if (count != 0)
        throw Invalid(carName, "seat-type array is not a relocated pointer");
      return Array.Empty<uint>();
    }
    if (storedPointer != targetAddress)
      throw Invalid(carName, "seat-type array does not match its relocation target");
    if (count == 0) return Array.Empty<uint>();

    var byteCount = Convert.ToUInt64(count) * sizeof(uint);
    if (byteCount > int.MaxValue)
      throw Invalid(carName, "seat-type byte count exceeds the addressable decoder range");
    context.ReserveBytes(byteCount, carName, "seat-type array");
    context.ReserveObjects(count, carName, "seat-type array");
    if (!source.TryReadBytes(targetAddress, Convert.ToInt32(byteCount), out var bytes) ||
        bytes.Length != Convert.ToInt32(byteCount))
      throw Invalid(carName, "seat-type array is outside the archive or truncated");

    var values = new uint[Convert.ToInt32(count)];
    foreach (var index in Enumerable.Range(0, values.Length))
      values[index] = ReadUInt32(bytes, checked(index * sizeof(uint)));
    return values;
  }

  private static string? ReadResourceReference(
    IRideCarDataSource source,
    OvlLoaderEntry owner,
    uint fieldAddress,
    uint storedPointer,
    string expectedTag,
    bool required,
    string carName,
    string description
  ) {
    if (!source.ResourceReferences.TryGetValue(fieldAddress, out var reference)) {
      if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
        throw Invalid(carName,
          $"{description} contains conflicting direct pointer data");
      if (required) throw Invalid(carName, $"{description} SymbolRef is missing");
      return null;
    }
    if (!SameLoader(reference.Owner, owner))
      throw Invalid(carName, $"{description} SymbolRef is owned by another loader");
    if (!HasTag(reference.Symbol, expectedTag))
      throw Invalid(carName,
        $"{description} SymbolRef '{reference.Symbol}' is not {expectedTag}");
    if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(carName,
        $"{description} SymbolRef field contains conflicting pointer data");
    return reference.Symbol;
  }

  private static void ValidateOwnedReferences(
    string carName,
    OvlLoaderEntry owner,
    IReadOnlySet<uint> expectedFields,
    IRideCarDataSource source
  ) {
    foreach (var reference in source.ResourceReferences) {
      if (!SameLoader(reference.Value.Owner, owner)) continue;
      if (!expectedFields.Contains(reference.Key))
        throw Invalid(carName,
          $"loader owns a SymbolRef outside the RIC layout at {reference.Key}");
    }
  }

  private static string ReadRequiredString(
    IRideCarDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string carName,
    string description,
    DecodeContext context
  ) {
    if (!source.TryGetRelocationSource(fieldAddress, out var address)) {
      if (storedPointer != 0)
        throw Invalid(carName, $"{description} contains an unproven pointer {storedPointer}");
      throw Invalid(carName, $"{description} is not a relocated pointer");
    }
    if (storedPointer != address)
      throw Invalid(carName, $"{description} does not match its relocation target");
    if (!source.TryGetNullTerminatedStringByteLength(
          address, MaximumStringBytes, out var length) || length == 0)
      throw Invalid(carName,
        $"{description} is missing, unterminated, or exceeds {MaximumStringBytes} bytes");
    context.ReserveBytes(Convert.ToUInt64(length) + 1, carName, description);
    if (!source.TryReadNullTerminatedString(address, length + 1, out var value) ||
        string.IsNullOrEmpty(value))
      throw Invalid(carName, $"{description} changed while it was being decoded");
    return value;
  }

  private static byte[] ReadExact(
    IRideCarDataSource source,
    uint address,
    int length,
    string carName,
    string description,
    DecodeContext context
  ) {
    context.ReserveBytes(Convert.ToUInt64(length), carName, description);
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(carName, $"{description} is outside the archive or truncated");
    return bytes;
  }

  private static float ReadFiniteSingle(
    byte[] bytes,
    int offset,
    string carName,
    string description
  ) {
    var value = BitConverter.ToSingle(bytes, offset);
    if (!float.IsFinite(value)) throw Invalid(carName, $"{description} is not finite");
    return value;
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static bool HasTag(string key, string tag) =>
    key.EndsWith($":{tag}", StringComparison.OrdinalIgnoreCase);

  private static uint CheckedAdd(uint address, int offset, string carName) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(carName, "pointer address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static ushort ReadUInt16(byte[] bytes, int offset) =>
    BitConverter.ToUInt16(bytes, offset);

  private static uint ReadUInt32(byte[] bytes, int offset) =>
    BitConverter.ToUInt32(bytes, offset);

  private static int ReadInt32(byte[] bytes, int offset) =>
    BitConverter.ToInt32(bytes, offset);

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Ride car '{name}' is malformed: {message}.");

  private sealed class DecodeContext(RideCarDecodeLimits limits) {
    private ulong decodedBytes;
    private ulong decodedObjects;

    public void ReserveBytes(ulong count, string carName, string description) {
      if (count > limits.MaximumBytes || decodedBytes > limits.MaximumBytes - count)
        throw Invalid(carName,
          $"aggregate decode bytes exceed the limit {limits.MaximumBytes} while reading " +
          description);
      decodedBytes += count;
    }

    public void ReserveObjects(ulong count, string carName, string description) {
      if (count > limits.MaximumObjects || decodedObjects > limits.MaximumObjects - count)
        throw Invalid(carName,
          $"aggregate decoded objects exceed the limit {limits.MaximumObjects} while reading " +
          description);
      decodedObjects += count;
    }
  }

  private sealed class OvlRideCarDataSource : IRideCarDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;
    private readonly OvlCommonStringTable stringTable;

    public OvlRideCarDataSource(Ovl ovl, DecodeContext context) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the RIC decoder limit " +
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

    public OvlLoaderEntry GetRideCarLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact ric loader-table entry");
      var matches = candidates.Where(entry =>
        entry.Tag.ToFileType() == FileType.RideCar &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact ric loader-table entry");
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

internal readonly record struct RideCarDecodeLimits(ulong MaximumBytes, ulong MaximumObjects) {
  public static RideCarDecodeLimits Default { get; } =
    new(256UL * 1024 * 1024, 1_000_000);
}

internal interface IRideCarDataSource {
  IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryGetNullTerminatedStringByteLength(uint address, int maximumLength, out int length);
  bool TryReadNullTerminatedString(uint address, int maximumLength, out string value);
}
