// SceneryItems
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenCobra.OVL.Files;

/// <summary>Placement metadata and visual references decoded from an RCT3 SID resource.</summary>
public sealed record SceneryItem(
  string Name,
  SidFlags Flags,
  SidPosition PositionType,
  ushort StructureVersion,
  uint SquaresX,
  uint SquaresZ,
  float PositionX,
  float PositionY,
  float PositionZ,
  float SizeX,
  float SizeY,
  float SizeZ,
  SidType Type,
  IReadOnlyList<string> VisualRefs
) {
  public int SerializedHeaderSize { get; init; }
}

/// <summary>Decodes relocation-backed <c>sid</c> resources without renderer dependencies.</summary>
public static class SceneryItems {
  // See sceneryold.h/StyleOVL.cpp and sceneryrevised.h/ManagerSID.cpp in rct3-importer. Frontier's
  // pointers are 32-bit. Stock v1 uses a 164-byte legacy SID, while revised V/S/W headers are
  // exactly 212/228/236 bytes.
  private const int LegacyV1HeaderSize = 164;
  private const int BaseHeaderSize = 212;
  private const int SoakedHeaderSize = 228;
  private const int WildHeaderSize = 236;
  private const int PointerSize = 4;
  private const int MaximumItemCount = 64 * 1024;
  private const int MaximumVisualCount = 64 * 1024;
  private const uint MaximumFootprintDimension = 4 * 1024;
  private const ulong MaximumFootprintSquares = 1_000_000;
  private const int MaximumResourceCount = 1_000_000;

  /// <summary>Decodes every scenery item from the unique half of an OVL pair.</summary>
  public static IReadOnlyList<SceneryItem> Extract(Ovl ovl) =>
    Extract(ovl, SceneryItemDecodeLimits.Default);

  internal static IReadOnlyList<SceneryItem> Extract(
    Ovl ovl,
    SceneryItemDecodeLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(ovl);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the SID decoder limit " +
        $"{MaximumResourceCount}.");

    var context = new DecodeContext(limits);
    var files = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file =>
      file.Type == FileType.SceneryItem &&
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))) {
      if (files.Count >= MaximumItemCount)
        throw Invalid(file.Name,
          $"item count exceeds the decoder limit {MaximumItemCount}");
      context.ReserveObjects(1, file.Name, "item resource index");
      files.Add(file);
    }

    var source = new OvlSceneryItemDataSource(ovl, context);
    var items = new List<SceneryItem>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier SID at {address}");
      var owner = source.GetItemLoader(file, address);
      items.Add(Decode(file.Name, ovl.Version, owner, source, context));
    }
    return items;
  }

  internal static SceneryItem Decode(
    string name,
    OvlLoaderEntry owner,
    ISceneryItemDataSource source
  ) => Decode(
    name, Version.Five, owner, source, new DecodeContext(SceneryItemDecodeLimits.Default));

  internal static SceneryItem Decode(
    string name,
    Version archiveVersion,
    OvlLoaderEntry owner,
    ISceneryItemDataSource source
  ) => Decode(
    name, archiveVersion, owner, source,
    new DecodeContext(SceneryItemDecodeLimits.Default));

  internal static SceneryItem Decode(
    string name,
    OvlLoaderEntry owner,
    ISceneryItemDataSource source,
    SceneryItemDecodeLimits limits
  ) => Decode(name, Version.Five, owner, source, new DecodeContext(limits));

  internal static SceneryItem Decode(
    string name,
    Version archiveVersion,
    OvlLoaderEntry owner,
    ISceneryItemDataSource source,
    SceneryItemDecodeLimits limits
  ) => Decode(name, archiveVersion, owner, source, new DecodeContext(limits));

  private static SceneryItem Decode(
    string name,
    Version archiveVersion,
    OvlLoaderEntry owner,
    ISceneryItemDataSource source,
    DecodeContext context
  ) {
    if (owner.Tag.ToFileType() != FileType.SceneryItem)
      throw Invalid(name, $"loader type '{owner.Tag}' is not sid");

    var address = owner.DataAddress;
    var layout = SelectHeaderLayout(name, archiveVersion, address, source);
    var baseHeaderSize = layout == SidHeaderLayout.LegacyV1
      ? LegacyV1HeaderSize
      : BaseHeaderSize;
    var header = ReadExact(
      source, address, baseHeaderSize, name, "base header", context);
    var structureVersion = layout == SidHeaderLayout.LegacyV1
      ? Convert.ToUInt16(0)
      : BitConverter.ToUInt16(header, 10);
    var headerSize = layout == SidHeaderLayout.LegacyV1
      ? LegacyV1HeaderSize
      : structureVersion switch {
        0 => BaseHeaderSize,
        1 => SoakedHeaderSize,
        2 => WildHeaderSize,
        _ => throw Invalid(name,
          $"structure version {structureVersion} is unsupported")
      };
    if (layout == SidHeaderLayout.Revised && headerSize != BaseHeaderSize)
      ReadExact(
        source,
        CheckedAdd(address, BaseHeaderSize, name),
        headerSize - BaseHeaderSize,
        name,
        "versioned header extension",
        context);

    var rawPositionType = layout == SidHeaderLayout.LegacyV1
      ? ReadUInt32(header, 8)
      : BitConverter.ToUInt16(header, 8);
    if (rawPositionType > int.MaxValue)
      throw Invalid(name, $"position type {rawPositionType} is unsupported");
    var positionType = (SidPosition)Convert.ToInt32(rawPositionType);
    if (!Enum.IsDefined(positionType))
      throw Invalid(name, $"position type {rawPositionType} is unsupported");

    var squaresX = ReadUInt32(header, 16);
    var squaresZ = ReadUInt32(header, 20);
    ValidateFootprint(name, squaresX, squaresZ);

    var typeOffset = layout == SidHeaderLayout.LegacyV1 ? 48 : 72;
    var rawType = ReadUInt32(header, typeOffset);
    var type = (SidType)rawType;
    if (!Enum.IsDefined(type))
      throw Invalid(name, $"item type {rawType} is unsupported");

    var visualCountOffset = layout == SidHeaderLayout.LegacyV1 ? 56 : 80;
    var visualPointerOffset = layout == SidHeaderLayout.LegacyV1 ? 60 : 84;
    var visualCount = ReadUInt32(header, visualCountOffset);
    if (visualCount == 0) throw Invalid(name, "SVD count is zero");
    if (visualCount > MaximumVisualCount)
      throw Invalid(name,
        $"SVD count {visualCount} exceeds the decoder limit {MaximumVisualCount}");
    context.ReserveObjects(Convert.ToUInt64(visualCount) + 1, name, "item and SVD refs");

    var visualPointerAddress = ReadRequiredPointer(
      source,
      CheckedAdd(address, visualPointerOffset, name),
      ReadUInt32(header, visualPointerOffset),
      name,
      "SVD reference array");
    var headerEnd = CheckedAdd(address, headerSize, name);
    if (visualPointerAddress < headerEnd)
      throw Invalid(name, "SVD reference array overlaps its versioned header");

    var pointerBytes = ReadArray(
      source,
      visualPointerAddress,
      visualCount,
      PointerSize,
      name,
      "SVD reference array",
      context);
    var visualRefs = new string[ToCount(visualCount, name, "SVD count")];
    var expectedReferenceFields = new HashSet<uint>();
    foreach (var index in Enumerable.Range(0, visualRefs.Length)) {
      var fieldAddress = CheckedAdd(
        visualPointerAddress, index * PointerSize, name);
      expectedReferenceFields.Add(fieldAddress);
      var rawValue = ReadUInt32(pointerBytes, index * PointerSize);
      visualRefs[index] = ReadVisualReference(
        name, index, fieldAddress, rawValue, owner, source);
    }
    ValidateOwnedVisualReferences(name, owner, expectedReferenceFields, source);

    return new SceneryItem(
      name,
      (SidFlags)ReadUInt32(header, 4),
      positionType,
      structureVersion,
      squaresX,
      squaresZ,
      layout == SidHeaderLayout.LegacyV1
        ? 0
        : ReadFiniteSingle(header, 28, name, "position X"),
      layout == SidHeaderLayout.LegacyV1
        ? 0
        : ReadFiniteSingle(header, 32, name, "position Y"),
      layout == SidHeaderLayout.LegacyV1
        ? 0
        : ReadFiniteSingle(header, 36, name, "position Z"),
      layout == SidHeaderLayout.LegacyV1
        ? 0
        : ReadFiniteSingle(header, 40, name, "size X"),
      layout == SidHeaderLayout.LegacyV1
        ? 0
        : ReadFiniteSingle(header, 44, name, "size Y"),
      layout == SidHeaderLayout.LegacyV1
        ? 0
        : ReadFiniteSingle(header, 48, name, "size Z"),
      type,
      visualRefs) {
      SerializedHeaderSize = headerSize
    };
  }

  private static SidHeaderLayout SelectHeaderLayout(
    string name,
    Version archiveVersion,
    uint address,
    ISceneryItemDataSource source
  ) {
    if (archiveVersion is Version.Four or Version.Five) return SidHeaderLayout.Revised;
    if (archiveVersion != Version.One)
      throw Invalid(name, $"archive version {archiveVersion} is unsupported");

    // Version-one archives exist in both forms. Stock game archives put svds_ref at +60;
    // StyleOVL's revised-compatible layout puts it at +84. Relocation-table membership is the
    // serialized discriminator.
    var hasLegacyPointer = source.TryGetRelocationSource(
      CheckedAdd(address, 60, name), out _);
    var hasRevisedPointer = source.TryGetRelocationSource(
      CheckedAdd(address, 84, name), out _);
    if (hasLegacyPointer == hasRevisedPointer)
      throw Invalid(name,
        "version 1 header does not identify exactly one legacy or revised SVD pointer field");
    return hasLegacyPointer ? SidHeaderLayout.LegacyV1 : SidHeaderLayout.Revised;
  }

  private static void ValidateFootprint(string name, uint squaresX, uint squaresZ) {
    if (squaresX == 0 || squaresZ == 0)
      throw Invalid(name, $"footprint {squaresX}x{squaresZ} has a zero dimension");
    if (squaresX > MaximumFootprintDimension || squaresZ > MaximumFootprintDimension)
      throw Invalid(name,
        $"footprint {squaresX}x{squaresZ} exceeds the per-axis limit " +
        $"{MaximumFootprintDimension}");
    var squareCount = Convert.ToUInt64(squaresX) * Convert.ToUInt64(squaresZ);
    if (squareCount > MaximumFootprintSquares)
      throw Invalid(name,
        $"footprint {squaresX}x{squaresZ} exceeds the square limit " +
        $"{MaximumFootprintSquares}");
  }

  private static string ReadVisualReference(
    string itemName,
    int index,
    uint fieldAddress,
    uint rawValue,
    OvlLoaderEntry owner,
    ISceneryItemDataSource source
  ) {
    // ManagerSID zeroes every slot and emits a SymbolRefStruct naming the target SVD.
    if (rawValue != 0 || source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(itemName,
        $"SVD reference {index} has a conflicting direct or relocated value");
    if (!source.ResourceReferences.TryGetValue(fieldAddress, out var reference))
      throw Invalid(itemName, $"SVD reference {index} is missing");
    if (!SameLoader(reference.Owner, owner))
      throw Invalid(itemName, $"SVD reference {index} belongs to another loader");
    if (!HasTag(reference.Symbol, "svd"))
      throw Invalid(itemName,
        $"SVD reference {index} targets '{reference.Symbol}' instead of svd");
    return reference.Symbol;
  }

  private static void ValidateOwnedVisualReferences(
    string itemName,
    OvlLoaderEntry owner,
    IReadOnlySet<uint> expectedFields,
    ISceneryItemDataSource source
  ) {
    foreach (var entry in source.ResourceReferences) {
      if (!SameLoader(entry.Value.Owner, owner) || !HasTag(entry.Value.Symbol, "svd"))
        continue;
      if (!expectedFields.Contains(entry.Key))
        throw Invalid(itemName,
          $"loader owns an SVD SymbolRef outside its declared reference array at {entry.Key}");
    }
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static bool HasTag(string key, string tag) =>
    key.EndsWith($":{tag}", StringComparison.OrdinalIgnoreCase);

  private static uint ReadRequiredPointer(
    ISceneryItemDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string itemName,
    string description
  ) {
    if (!source.TryGetRelocationSource(fieldAddress, out var target) || target == 0)
      throw Invalid(itemName, $"{description} is not a non-null relocated pointer");
    if (storedPointer != target)
      throw Invalid(itemName, $"{description} does not match its relocation target");
    return target;
  }

  private static byte[] ReadArray(
    ISceneryItemDataSource source,
    uint address,
    uint count,
    int stride,
    string itemName,
    string description,
    DecodeContext context
  ) {
    var length = ByteCount(count, stride, itemName, description);
    var end = Convert.ToUInt64(address) + Convert.ToUInt64(length);
    if (end > Convert.ToUInt64(uint.MaxValue) + 1)
      throw Invalid(itemName, $"{description} overflows the OVL address space");
    return ReadExact(source, address, length, itemName, description, context);
  }

  private static byte[] ReadExact(
    ISceneryItemDataSource source,
    uint address,
    int length,
    string itemName,
    string description,
    DecodeContext context
  ) {
    context.ReserveBytes(Convert.ToUInt64(length), itemName, description);
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(itemName, $"{description} is outside the archive or truncated");
    return bytes;
  }

  private static int ByteCount(
    uint count,
    int stride,
    string itemName,
    string description
  ) {
    var length = Convert.ToUInt64(count) * Convert.ToUInt64(stride);
    if (length > SceneryItemDecodeLimits.Default.MaximumBytes || length > int.MaxValue)
      throw Invalid(itemName,
        $"{description} exceeds the decoder byte limit " +
        $"{SceneryItemDecodeLimits.Default.MaximumBytes}");
    return Convert.ToInt32(length);
  }

  private static int ToCount(uint count, string itemName, string description) {
    if (count > int.MaxValue)
      throw Invalid(itemName, $"{description} {count} exceeds the decoder limit");
    return Convert.ToInt32(count);
  }

  private static uint CheckedAdd(uint address, int offset, string itemName) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(itemName, "pointer address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static uint ReadUInt32(byte[] bytes, int offset) =>
    BitConverter.ToUInt32(bytes, offset);

  private static float ReadFiniteSingle(
    byte[] bytes,
    int offset,
    string itemName,
    string description
  ) {
    var value = BitConverter.ToSingle(bytes, offset);
    if (!float.IsFinite(value))
      throw Invalid(itemName, $"{description} contains a non-finite value");
    return value;
  }

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Scenery item '{name}' is malformed: {message}.");

  private sealed class DecodeContext(SceneryItemDecodeLimits limits) {
    private ulong decodedBytes;
    private ulong decodedObjects;

    public void ReserveBytes(ulong count, string itemName, string description) {
      if (count > limits.MaximumBytes || decodedBytes > limits.MaximumBytes - count)
        throw Invalid(itemName,
          $"aggregate decode bytes exceed the limit {limits.MaximumBytes} " +
          $"while reading {description}");
      decodedBytes += count;
    }

    public void ReserveObjects(ulong count, string itemName, string description) {
      if (count > limits.MaximumObjects || decodedObjects > limits.MaximumObjects - count)
        throw Invalid(itemName,
          $"aggregate decoded objects exceed the limit {limits.MaximumObjects} " +
          $"while reading {description}");
      decodedObjects += count;
    }
  }

  private enum SidHeaderLayout {
    LegacyV1,
    Revised
  }

  private sealed class OvlSceneryItemDataSource : ISceneryItemDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;

    public OvlSceneryItemDataSource(Ovl ovl, DecodeContext context) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the SID decoder limit " +
          $"{MaximumResourceCount}.");

      var mutableLoaders = new Dictionary<uint, List<OvlLoaderEntry>>();
      foreach (var entry in ovl.LoaderEntriesInOrder) {
        if (!mutableLoaders.TryGetValue(entry.DataAddress, out var entries))
          mutableLoaders.Add(entry.DataAddress, entries = []);
        entries.Add(entry);
      }
      context.ReserveObjects(
        Convert.ToUInt64(ovl.LoaderEntriesInOrder.Count), "OVL", "loader metadata index");
      loadersByDataAddress = mutableLoaders.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<OvlLoaderEntry>)pair.Value);

      var symbolReferences = OvlSymbolReferenceIndex.Create(ovl);
      ResourceReferences = symbolReferences.References;
    }

    public IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }

    public OvlLoaderEntry GetItemLoader(OvlFile file, uint address) {
      if (!TryGetExactLoader(address, file.Path, out var loader) ||
          loader.Tag.ToFileType() != FileType.SceneryItem)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact sid loader-table entry");
      return loader;
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

    private bool TryGetExactLoader(
      uint address,
      string sourcePath,
      out OvlLoaderEntry loader
    ) {
      loader = null!;
      if (!loadersByDataAddress.TryGetValue(address, out var entries)) return false;
      var matches = entries.Where(entry => string.Equals(
        entry.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1) return false;
      loader = matches[0];
      return true;
    }
  }
}

internal readonly record struct SceneryItemDecodeLimits(
  ulong MaximumBytes,
  ulong MaximumObjects
) {
  public static SceneryItemDecodeLimits Default { get; } =
    new(256UL * 1024 * 1024, 1_000_000);
}

internal interface ISceneryItemDataSource {
  IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
}
