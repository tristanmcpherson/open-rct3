// Path Types
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>The serialized PTD shape-owner field, in native field order.</summary>
public enum PathTypeShapeKind {
  Flat,
  StraightA,
  StraightB,
  TurnU,
  TurnLA,
  TurnLB,
  TurnTA,
  TurnTB,
  TurnTC,
  TurnX,
  CornerA,
  CornerB,
  CornerC,
  CornerD,
  Slope,
  SlopeStraight,
  SlopeStraightLeft,
  SlopeStraightRight,
  SlopeMid,
}

/// <summary>The serialized extended PTD shape-owner field, in native field order.</summary>
public enum ExtendedPathTypeShapeKind {
  FlatFc,
  SlopeFc,
  SlopeBc,
  SlopeTc,
  SlopeStraightFc,
  SlopeStraightBc,
  SlopeStraightTc,
  SlopeStraightLeftFc,
  SlopeStraightLeftBc,
  SlopeStraightLeftTc,
  SlopeStraightRightFc,
  SlopeStraightRightBc,
  SlopeStraightRightTc,
  SlopeMidFc,
  SlopeMidBc,
  SlopeMidTc,
}

/// <summary>Four direction variants for one PTD shape owner.</summary>
public sealed record PathTypeShapeOwners(
  PathTypeShapeKind Kind,
  IReadOnlyList<string> Variants
);

/// <summary>Four direction variants for one extended PTD shape owner.</summary>
public sealed record ExtendedPathTypeShapeOwners(
  ExtendedPathTypeShapeKind Kind,
  IReadOnlyList<string> Variants
);

/// <summary>One optional research-category record shared by PTD and QTD resources.</summary>
public sealed record PathResearchCategory(string? Category, uint Unknown1, uint Unknown2);

/// <summary>Expansion-era PTD fields appended when the extended flag is set.</summary>
public sealed record ExtendedPathType(
  uint Unknown1,
  uint Unknown2,
  IReadOnlyList<ExtendedPathTypeShapeOwners> ShapeOwners,
  string? Paving
);

/// <summary>A decoded RCT3 ordinary-path definition.</summary>
public sealed record PathType(
  string Name,
  uint Flags,
  string InternalName,
  string DisplayNameRef,
  string IconRef,
  string Texture1Ref,
  string Texture2Ref,
  IReadOnlyList<PathTypeShapeOwners> ShapeOwners,
  IReadOnlyList<PathResearchCategory> ResearchCategories,
  ExtendedPathType? Extended
);

/// <summary>Decodes relocation-backed <c>ptd</c> resources.</summary>
/// <remarks>
/// The 32-bit layouts and field order are ported directly from
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/path.h">path.h</see>
/// and
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerPTD.cpp">ManagerPTD.cpp</see>.
/// </remarks>
public static class PathTypes {
  internal const int BaseRecordSize = 336;
  internal const int ExtendedRecordSize = 604;
  internal const uint ExtendedFlag = 16_777_216;
  private const uint KnownFlags = 16_777_219;
  private const int ShapeGroupCount = 19;
  private const int ExtendedShapeGroupCount = 16;
  private const int ShapeVariantCount = 4;
  private const int ShapeGroupsOffset = 24;
  private const int ResearchCountOffset = 328;
  private const int ResearchPointerOffset = 332;
  private const int ExtendedShapeGroupsOffset = 344;
  private const int ExtendedPavingOffset = 600;

  /// <summary>Extracts every ordinary-path definition from an OVL pair.</summary>
  public static IReadOnlyList<PathType> Extract(Ovl ovl) =>
    Extract(ovl, PathResourceDecodeLimits.Default);

  internal static IReadOnlyList<PathType> Extract(
    Ovl ovl,
    PathResourceDecodeLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(ovl);
    var context = new PathResourceDecodeContext(limits);
    var files = PathResourceDecoder.Files(
      ovl, FileType.PathType, "PTD", context);
    if (files.Count == 0) return [];

    var source = new OvlPathResourceDataSource(ovl, context);
    var paths = new List<PathType>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      var address = PathResourceDecoder.RequiredAddress(ovl, file, "PTD");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier PTD at {address}");
      var owner = source.GetLoader(file, address, FileType.PathType, "PTD");
      paths.Add(Decode(file.Name, owner, source, context));
    }
    return paths;
  }

  internal static PathType Decode(
    string name,
    OvlLoaderEntry owner,
    IPathResourceDataSource source
  ) => Decode(
    name,
    owner,
    source,
    new PathResourceDecodeContext(PathResourceDecodeLimits.Default));

  internal static PathType Decode(
    string name,
    OvlLoaderEntry owner,
    IPathResourceDataSource source,
    PathResourceDecodeLimits limits
  ) => Decode(name, owner, source, new PathResourceDecodeContext(limits));

  internal static PathType Decode(
    string name,
    OvlLoaderEntry owner,
    IPathResourceDataSource source,
    PathResourceDecodeContext context
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(context);
    PathResourceDecoder.RequireOwnership(
      source, owner, FileType.PathType, name, "PTD");

    var address = owner.DataAddress;
    var prefix = PathResourceDecoder.ReadExact(
      source, address, 4, name, "PTD flags", context);
    var flags = BitConverter.ToUInt32(prefix, 0);
    if ((flags & 1) == 0 || (flags & ~KnownFlags) != 0)
      throw Invalid(name, $"flags {flags} contain unsupported values");
    var extended = (flags & ExtendedFlag) != 0;
    var size = extended ? ExtendedRecordSize : BaseRecordSize;
    var bytes = PathResourceDecoder.ReadExact(
      source, address, size, name, "PTD record", context);
    context.ReserveObjects(1, name, "PTD model");

    var internalName = PathResourceDecoder.ReadRequiredString(
      source, bytes, address, 4, name, "internal name", allowEmpty: false, context: context);
    var displayNameRef = PathResourceDecoder.ReadRequiredSymbol(
      source, owner, bytes, address, 8, name, "display name", "txt");
    var iconRef = PathResourceDecoder.ReadRequiredSymbol(
      source, owner, bytes, address, 12, name, "icon", "gsi");
    var texture1 = PathResourceDecoder.ReadRequiredString(
      source, bytes, address, 16, name, "texture 1", allowEmpty: false, context: context);
    var texture2 = PathResourceDecoder.ReadRequiredString(
      source, bytes, address, 20, name, "texture 2", allowEmpty: false, context: context);

    var groups = new PathTypeShapeOwners[ShapeGroupCount];
    foreach (var index in Enumerable.Range(0, ShapeGroupCount)) {
      var variants = ReadVariants(
        source,
        bytes,
        address,
        ShapeGroupsOffset + (index * ShapeVariantCount * 4),
        name,
        $"{(PathTypeShapeKind)index} shape owner",
        context);
      groups[index] = new PathTypeShapeOwners((PathTypeShapeKind)index, variants);
    }

    var research = PathResourceDecoder.ReadResearchCategories(
      source,
      bytes,
      address,
      ResearchCountOffset,
      ResearchPointerOffset,
      name,
      context: context);
    var extendedFields = extended
      ? DecodeExtended(name, address, bytes, source, context)
      : null;
    return new PathType(
      name,
      flags,
      internalName,
      displayNameRef,
      iconRef,
      texture1,
      texture2,
      groups,
      research,
      extendedFields);
  }

  private static ExtendedPathType DecodeExtended(
    string name,
    uint address,
    byte[] bytes,
    IPathResourceDataSource source,
    PathResourceDecodeContext context
  ) {
    context.ReserveObjects(1, name, "extended PTD model");
    var groups = new ExtendedPathTypeShapeOwners[ExtendedShapeGroupCount];
    foreach (var index in Enumerable.Range(0, ExtendedShapeGroupCount)) {
      var variants = ReadVariants(
        source,
        bytes,
        address,
        ExtendedShapeGroupsOffset + (index * ShapeVariantCount * 4),
        name,
        $"{(ExtendedPathTypeShapeKind)index} extended shape owner",
        context: context);
      groups[index] = new ExtendedPathTypeShapeOwners(
        (ExtendedPathTypeShapeKind)index, variants);
    }
    var paving = PathResourceDecoder.ReadOptionalString(
      source,
      bytes,
      address,
      ExtendedPavingOffset,
      name,
      "extended paving",
      allowEmpty: true,
      context);
    return new ExtendedPathType(
      BitConverter.ToUInt32(bytes, BaseRecordSize),
      BitConverter.ToUInt32(bytes, BaseRecordSize + 4),
      groups,
      paving);
  }

  private static IReadOnlyList<string> ReadVariants(
    IPathResourceDataSource source,
    byte[] bytes,
    uint address,
    int offset,
    string name,
    string description,
    PathResourceDecodeContext context
  ) {
    context.ReserveObjects(2, name, $"{description} model and variants");
    var values = new string[ShapeVariantCount];
    foreach (var index in Enumerable.Range(0, ShapeVariantCount))
      values[index] = PathResourceDecoder.ReadRequiredString(
        source,
        bytes,
        address,
        offset + (index * 4),
        name,
        $"{description} variant {index}",
        allowEmpty: true,
        context);
    return values;
  }

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Path type '{name}' is malformed: {message}.");
}

internal interface IPathResourceDataSource {
  IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
  bool HasExactLoader(OvlLoaderEntry owner);
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryReadString(uint address, int maximumLength, out string value);
}

internal static class PathResourceDecoder {
  private const int MaximumResourceCount = 1_000_000;
  private const int MaximumStringBytes = 4 * 1024;
  private const int MaximumResearchCategories = 65_536;
  private const int ResearchRecordSize = 12;

  public static IReadOnlyList<OvlFile> Files(
    Ovl ovl,
    FileType type,
    string kind,
    PathResourceDecodeContext context
  ) {
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the {kind} decoder limit " +
        $"{MaximumResourceCount}.");
    var files = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file => file.Type == type)) {
      if (files.Count >= MaximumResourceCount)
        throw new InvalidDataException($"OVL {kind} count exceeds the decoder limit.");
      if (!file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))
        throw Invalid(file.Name, kind, "resource is not stored in a unique OVL");
      context.ReserveObjects(1, file.Name, $"{kind} resource index");
      files.Add(file);
    }
    return files;
  }

  public static uint RequiredAddress(Ovl ovl, OvlFile file, string kind) {
    if (ovl.TryGetDataPointer(file, out var address)) return address;
    throw Invalid(file.Name, kind, "resource data pointer is missing");
  }

  public static void RequireOwnership(
    IPathResourceDataSource source,
    OvlLoaderEntry owner,
    FileType type,
    string name,
    string kind
  ) {
    if (owner.Tag.ToFileType() != type || !source.HasExactLoader(owner))
      throw Invalid(name, kind,
        $"address {owner.DataAddress} is not owned by one exact " +
        $"{type.ToTagString()} loader");
  }

  public static byte[] ReadExact(
    IPathResourceDataSource source,
    uint address,
    int length,
    string name,
    string description,
    PathResourceDecodeContext context
  ) {
    context.ReserveBytes(Convert.ToUInt64(length), name, description);
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(name, "path resource", $"{description} is outside the archive or truncated");
    return bytes;
  }

  public static string ReadRequiredString(
    IPathResourceDataSource source,
    byte[] bytes,
    uint recordAddress,
    int offset,
    string name,
    string description,
    bool allowEmpty,
    PathResourceDecodeContext context
  ) => ReadString(
    source,
    bytes,
    recordAddress,
    offset,
    name,
    description,
    allowEmpty,
    required: true,
    context: context)!;

  public static string? ReadOptionalString(
    IPathResourceDataSource source,
    byte[] bytes,
    uint recordAddress,
    int offset,
    string name,
    string description,
    bool allowEmpty,
    PathResourceDecodeContext context
  ) => ReadString(
    source,
    bytes,
    recordAddress,
    offset,
    name,
    description,
    allowEmpty,
    required: false,
    context: context);

  public static string ReadRequiredSymbol(
    IPathResourceDataSource source,
    OvlLoaderEntry owner,
    byte[] bytes,
    uint recordAddress,
    int offset,
    string name,
    string description,
    string expectedTag
  ) {
    var fieldAddress = CheckedAdd(recordAddress, offset, name);
    var rawValue = BitConverter.ToUInt32(bytes, offset);
    if (!source.ResourceReferences.TryGetValue(fieldAddress, out var reference))
      throw Invalid(name, "path resource", $"{description} SymbolRef is missing");
    if (rawValue != 0 || source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(name, "path resource", $"{description} has conflicting pointer data");
    if (!SameLoader(reference.Owner, owner))
      throw Invalid(name, "path resource", $"{description} belongs to another loader");
    if (!reference.Symbol.EndsWith($":{expectedTag}", StringComparison.OrdinalIgnoreCase))
      throw Invalid(name, "path resource",
        $"{description} targets '{reference.Symbol}' instead of {expectedTag}");
    return reference.Symbol;
  }

  public static IReadOnlyList<PathResearchCategory> ReadResearchCategories(
    IPathResourceDataSource source,
    byte[] bytes,
    uint recordAddress,
    int countOffset,
    int pointerOffset,
    string name,
    PathResourceDecodeContext context
  ) {
    var count = BitConverter.ToUInt32(bytes, countOffset);
    if (count > MaximumResearchCategories)
      throw Invalid(name, "path resource",
        $"research category count {count} exceeds the decoder limit " +
        MaximumResearchCategories);
    var pointerField = CheckedAdd(recordAddress, pointerOffset, name);
    var rawPointer = BitConverter.ToUInt32(bytes, pointerOffset);
    if (count == 0) {
      if (rawPointer != 0 || source.TryGetRelocationSource(pointerField, out _))
        throw Invalid(name, "path resource", "empty research category list has a pointer");
      return [];
    }

    var categoriesAddress = RequiredPointer(
      source, pointerField, rawPointer, name, "research category list");
    var byteLength = Convert.ToUInt64(count) * ResearchRecordSize;
    if (byteLength > int.MaxValue)
      throw Invalid(name, "path resource", "research category list is too large");
    var categoryBytes = ReadExact(
      source,
      categoriesAddress,
      Convert.ToInt32(byteLength),
      name,
      "research category list",
      context);
    context.ReserveObjects(count, name, "research category models");
    var categories = new PathResearchCategory[Convert.ToInt32(count)];
    foreach (var index in Enumerable.Range(0, categories.Length)) {
      var offset = index * ResearchRecordSize;
      var category = ReadOptionalString(
        source,
        categoryBytes,
        categoriesAddress,
        offset,
        name,
        $"research category {index}",
        allowEmpty: false,
        context: context);
      categories[index] = new PathResearchCategory(
        category,
        BitConverter.ToUInt32(categoryBytes, offset + 4),
        BitConverter.ToUInt32(categoryBytes, offset + 8));
    }
    return categories;
  }

  private static string? ReadString(
    IPathResourceDataSource source,
    byte[] bytes,
    uint recordAddress,
    int offset,
    string name,
    string description,
    bool allowEmpty,
    bool required,
    PathResourceDecodeContext context
  ) {
    var fieldAddress = CheckedAdd(recordAddress, offset, name);
    var rawPointer = BitConverter.ToUInt32(bytes, offset);
    if (!source.TryGetRelocationSource(fieldAddress, out var target)) {
      if (!required && rawPointer == 0) return null;
      throw Invalid(name, "path resource",
        $"{description} is not a relocated pointer (raw value {rawPointer})");
    }
    if (target != rawPointer)
      throw Invalid(name, "path resource", $"{description} conflicts with its relocation target");
    if (!source.TryReadString(target, MaximumStringBytes, out var value))
      throw Invalid(name, "path resource",
        $"{description} is missing, unterminated, or exceeds {MaximumStringBytes} bytes");
    if (!allowEmpty && string.IsNullOrWhiteSpace(value))
      throw Invalid(name, "path resource", $"{description} is empty");
    context.ReserveStringBytes(
      Convert.ToUInt64(value.Length) + 1,
      name,
      description);
    return value;
  }

  private static uint RequiredPointer(
    IPathResourceDataSource source,
    uint fieldAddress,
    uint rawValue,
    string name,
    string description
  ) {
    if (!source.TryGetRelocationSource(fieldAddress, out var target) || target != rawValue)
      throw Invalid(name, "path resource", $"{description} is not a matching relocation");
    return target;
  }

  private static uint CheckedAdd(uint address, int offset, string name) {
    var value = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (value > uint.MaxValue)
      throw Invalid(name, "path resource", "field address exceeds the OVL address space");
    return Convert.ToUInt32(value);
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static InvalidDataException Invalid(string name, string kind, string message) =>
    new($"{kind} '{name}' is malformed: {message}.");
}

internal sealed class OvlPathResourceDataSource : IPathResourceDataSource {
  private const int MaximumLoaderCount = 1_000_000;
  private readonly Ovl ovl;
  private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;
  private readonly IReadOnlyList<OvlLoaderEntry> loaders;
  private readonly OvlCommonStringTable stringTable;

  public OvlPathResourceDataSource(Ovl ovl, PathResourceDecodeContext context) {
    this.ovl = ovl;
    if (ovl.LoaderEntriesInOrder.Count > MaximumLoaderCount)
      throw new InvalidDataException(
        $"OVL loader count exceeds the path resource decoder limit {MaximumLoaderCount}.");

    context.ReserveObjects(
      OvlLoaderIndexBudget.Calculate(ovl.LoaderEntriesInOrder.Count),
      "OVL",
      "path loader metadata index");
    loaders = ovl.LoaderEntriesInOrder.ToArray();
    var mutableLoaders = new Dictionary<uint, List<OvlLoaderEntry>>();
    foreach (var loader in ovl.LoaderEntriesInOrder) {
      if (!mutableLoaders.TryGetValue(loader.DataAddress, out var candidates))
        mutableLoaders.Add(loader.DataAddress, candidates = []);
      candidates.Add(loader);
    }
    loadersByDataAddress = mutableLoaders.ToDictionary(
      pair => pair.Key,
      pair => (IReadOnlyList<OvlLoaderEntry>)pair.Value);

    ResourceReferences = OvlSymbolReferenceIndex.Create(ovl).References;
    stringTable = new OvlCommonStringTable(ovl);
  }

  public IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }

  public OvlLoaderEntry GetLoader(
    OvlFile file,
    uint address,
    FileType type,
    string kind
  ) {
    if (!loadersByDataAddress.TryGetValue(address, out var candidates))
      throw new InvalidDataException(
        $"{kind} '{file.Name}' is malformed: address {address} is not owned by one exact " +
        $"{type.ToTagString()} loader.");
    var matches = candidates.Where(entry =>
      entry.Tag.ToFileType() == type &&
      string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1)
      throw new InvalidDataException(
        $"{kind} '{file.Name}' is malformed: address {address} is not owned by one exact " +
        $"{type.ToTagString()} loader.");
    return matches[0];
  }

  public bool HasExactLoader(OvlLoaderEntry owner) =>
    loaders.Any(loader => SameLoader(loader, owner));

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

  public bool TryReadString(uint address, int maximumLength, out string value) {
    return stringTable.TryReadString(address, maximumLength, out value);
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct PathResourceDecodeLimits(
  ulong MaximumBytes,
  ulong MaximumObjects,
  ulong MaximumStringBytes
) {
  public static PathResourceDecodeLimits Default { get; } =
    new(256UL * 1024 * 1024, 1_000_000, 64UL * 1024 * 1024);
}

internal sealed class PathResourceDecodeContext(PathResourceDecodeLimits limits) {
  private ulong decodedBytes;
  private ulong decodedObjects;
  private ulong decodedStringBytes;

  public void ReserveBytes(ulong count, string resourceName, string description) =>
    Reserve(
      count,
      limits.MaximumBytes,
      ref decodedBytes,
      resourceName,
      description,
      "bytes");

  public void ReserveObjects(ulong count, string resourceName, string description) =>
    Reserve(
      count,
      limits.MaximumObjects,
      ref decodedObjects,
      resourceName,
      description,
      "objects");

  public void ReserveStringBytes(ulong count, string resourceName, string description) =>
    Reserve(
      count,
      limits.MaximumStringBytes,
      ref decodedStringBytes,
      resourceName,
      description,
      "string bytes");

  private static void Reserve(
    ulong count,
    ulong maximum,
    ref ulong used,
    string resourceName,
    string description,
    string kind
  ) {
    if (count > maximum || used > maximum - count)
      throw new InvalidDataException(
        $"Path resource '{resourceName}' is malformed: aggregate decoded {kind} exceed " +
        $"the limit {maximum} while reading {description}.");
    used += count;
  }
}

/// <summary>Computes the temporary and retained loader-index object charge.</summary>
internal static class OvlLoaderIndexBudget {
  private const ulong ObjectsPerLoader = 4;

  public static ulong Calculate(int loaderCount) =>
    checked(Convert.ToUInt64(loaderCount) * ObjectsPerLoader);
}

/// <summary>Reads strings only from declared type-0 blocks in the common OVL half.</summary>
internal sealed class OvlCommonStringTable(Ovl ovl) {
  private const int MaximumExternalReferences = 65_536;
  private const int MaximumLoaderHeaders = 4_096;
  private const int MaximumBlocksPerType = 65_536;
  private const int MaximumTotalBlocks = 65_536;
  private const int TypeCount = 9;

  private IReadOnlyList<StringTableRange>? ranges;
  private string? commonPath;
  private bool initialized;
  private uint cachedAddress;
  private string cachedValue = string.Empty;
  private bool hasCachedValue;

  public bool TryReadString(uint address, int maximumLength, out string value) {
    if (maximumLength <= 0 || !TryInitialize() ||
        !TryGetRange(address, out var range, out var offsetInRange)) {
      value = string.Empty;
      return false;
    }

    if (hasCachedValue && cachedAddress == address &&
        Encoding.ASCII.GetByteCount(cachedValue) < maximumLength) {
      value = cachedValue;
      return true;
    }

    var remaining = range.Size - offsetInRange;
    var bytesToScan = Convert.ToInt32(Math.Min(
      remaining,
      Convert.ToUInt32(maximumLength)));
    if (bytesToScan == 0) {
      value = string.Empty;
      return false;
    }

    try {
      using var stream = File.OpenRead(commonPath!);
      stream.Position = checked(range.FileOffset + offsetInRange);
      var bytes = new byte[bytesToScan];
      stream.ReadExactly(bytes);
      var end = Array.IndexOf(bytes, Convert.ToByte(0));
      if (end < 0 || bytes.Take(end).Any(item => item > 127)) {
        value = string.Empty;
        return false;
      }
      value = Encoding.ASCII.GetString(bytes, 0, end);
      cachedAddress = address;
      cachedValue = value;
      hasCachedValue = true;
      return true;
    } catch (IOException) {
      value = string.Empty;
      return false;
    } catch (UnauthorizedAccessException) {
      value = string.Empty;
      return false;
    }
  }

  private bool TryInitialize() {
    if (initialized) return ranges != null;
    initialized = true;
    var paths = ovl.Keys
      .Select(file => ToCommonPath(file.Path))
      .Where(File.Exists)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();
    if (paths.Count != 1) return false;

    try {
      commonPath = paths[0];
      using var stream = File.OpenRead(commonPath);
      using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
      ranges = ReadStringTableRanges(reader);
      return true;
    } catch (IOException) {
      ranges = null;
      return false;
    } catch (UnauthorizedAccessException) {
      ranges = null;
      return false;
    } catch (InvalidDataException) {
      ranges = null;
      return false;
    }
  }

  private bool TryGetRange(
    uint address,
    out StringTableRange range,
    out uint offsetInRange
  ) {
    foreach (var candidate in ranges!) {
      if (candidate.Size == 0 || address < candidate.Address ||
          address - candidate.Address >= candidate.Size) continue;
      range = candidate;
      offsetInRange = address - candidate.Address;
      return true;
    }
    range = default;
    offsetInRange = 0;
    return false;
  }

  private static IReadOnlyList<StringTableRange> ReadStringTableRanges(BinaryReader reader) {
    Require(reader, 16);
    if (reader.ReadUInt32() != 0x4b524746)
      throw new InvalidDataException("Invalid OVL magic while indexing the common string table.");
    reader.ReadUInt32();
    var version = reader.ReadUInt32();
    var headerReferences = reader.ReadUInt32();
    if (version is not 1 and not 4 and not 5)
      throw new InvalidDataException($"Unsupported OVL version {version}.");

    var subVersionFlag = 0u;
    var referenceCount = headerReferences;
    if (version == 4) {
      Require(reader, 4);
      referenceCount = reader.ReadUInt32();
    } else if (version == 5) {
      Require(reader, 4);
      subVersionFlag = reader.ReadUInt32();
      if (subVersionFlag != 0) {
        Skip(reader, 12);
        ReadNullTerminatedBytes(reader);
        var padding = (4 - (reader.BaseStream.Position % 4)) % 4;
        Skip(reader, padding);
      }
      Require(reader, 4);
      referenceCount = reader.ReadUInt32();
    }

    var boundedReferenceCount = BoundedCount(
      reader, referenceCount, 2, MaximumExternalReferences, "external reference");
    foreach (var _ in Enumerable.Range(0, boundedReferenceCount)) {
      Require(reader, 2);
      Skip(reader, reader.ReadUInt16());
    }

    Require(reader, 8);
    reader.ReadUInt32();
    var loaderCount = reader.ReadUInt32();
    var boundedLoaderCount = BoundedCount(
      reader, loaderCount, 10, MaximumLoaderHeaders, "loader header");
    foreach (var _ in Enumerable.Range(0, boundedLoaderCount)) {
      SkipLengthPrefixedString(reader);
      SkipLengthPrefixedString(reader);
      Skip(reader, 4);
      SkipLengthPrefixedString(reader);
    }
    if (version == 5) Skip(reader, checked(boundedLoaderCount * 8L));

    var blockCounts = new int[TypeCount];
    var blockSizes = Enumerable.Range(0, TypeCount)
      .Select(_ => new List<uint>())
      .ToArray();
    var totalBlockCount = 0;
    foreach (var typeIndex in Enumerable.Range(0, TypeCount)) {
      Require(reader, 4);
      var blockCount = reader.ReadUInt32();
      if (version > 1) {
        Skip(reader, 4);
        if (version == 5 && subVersionFlag != 0) Skip(reader, 4);
      }
      var boundedBlockCount = BoundedCount(
        reader, blockCount, 4, MaximumBlocksPerType, $"type-{typeIndex} block");
      if (boundedBlockCount > MaximumTotalBlocks - totalBlockCount)
        throw new InvalidDataException("OVL aggregate block count exceeds decoder limits.");
      totalBlockCount += boundedBlockCount;
      blockCounts[typeIndex] = boundedBlockCount;
      if (version <= 1) continue;
      blockSizes[typeIndex] = new List<uint>(boundedBlockCount);
      foreach (var _ in Enumerable.Range(0, boundedBlockCount)) {
        Require(reader, 4);
        blockSizes[typeIndex].Add(reader.ReadUInt32());
      }
    }

    if (version == 4) {
      Skip(reader, 8);
    } else if (version == 5) {
      Require(reader, 4);
      Skip(reader, reader.ReadUInt32());
      Require(reader, 4);
      Skip(reader, checked(Convert.ToInt64(reader.ReadUInt32()) * 4));
    }

    var stringRanges = new List<StringTableRange>(blockCounts[0]);
    var relativeOffset = 0u;
    foreach (var typeIndex in Enumerable.Range(0, TypeCount)) {
      foreach (var blockIndex in Enumerable.Range(0, blockCounts[typeIndex])) {
        uint blockSize;
        if (version == 1) {
          Require(reader, 4);
          blockSize = reader.ReadUInt32();
        } else {
          blockSize = blockSizes[typeIndex][blockIndex];
        }

        var fileOffset = reader.BaseStream.Position;
        Require(reader, blockSize);
        if (typeIndex == 0)
          stringRanges.Add(new StringTableRange(relativeOffset, fileOffset, blockSize));
        if (blockSize > uint.MaxValue - relativeOffset)
          throw new InvalidDataException("OVL block data exceeds the 32-bit address space.");
        relativeOffset += blockSize;
        Skip(reader, blockSize);
      }
    }
    return stringRanges;
  }

  private static int BoundedCount(
    BinaryReader reader,
    uint count,
    int minimumBytesPerItem,
    int maximumCount,
    string description
  ) {
    if (count > maximumCount)
      throw new InvalidDataException($"OVL {description} count exceeds decoder limits.");
    var remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
    if (minimumBytesPerItem > 0 && count > remainingBytes / minimumBytesPerItem)
      throw new InvalidDataException($"OVL {description} count exceeds the remaining file data.");
    return Convert.ToInt32(count);
  }

  private static void SkipLengthPrefixedString(BinaryReader reader) {
    Require(reader, 2);
    Skip(reader, reader.ReadUInt16());
  }

  private static void ReadNullTerminatedBytes(BinaryReader reader) {
    const int maximumLength = 4 * 1024;
    foreach (var _ in Enumerable.Range(0, maximumLength)) {
      Require(reader, 1);
      if (reader.ReadByte() == 0) return;
    }
    throw new InvalidDataException("OVL header string exceeds decoder limits.");
  }

  private static void Skip(BinaryReader reader, long length) {
    if (length < 0) throw new InvalidDataException("OVL field has a negative length.");
    Require(reader, length);
    reader.BaseStream.Seek(length, SeekOrigin.Current);
  }

  private static void Require(BinaryReader reader, long length) {
    if (length < 0 || reader.BaseStream.Length - reader.BaseStream.Position < length)
      throw new InvalidDataException("OVL header is truncated.");
  }

  private static string ToCommonPath(string path) {
    const string uniqueSuffix = ".unique.ovl";
    if (!path.EndsWith(uniqueSuffix, StringComparison.OrdinalIgnoreCase)) return path;
    return path[..^uniqueSuffix.Length] + ".common.ovl";
  }

  private readonly record struct StringTableRange(uint Address, long FileOffset, uint Size);
}
