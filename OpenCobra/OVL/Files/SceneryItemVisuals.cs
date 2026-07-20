// SceneryItemVisuals
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>The six billboard-related floats serialized in every SVD LOD record.</summary>
public sealed record SceneryVisualBillboardSettings(
  float Width,
  float Height,
  float U1,
  float V1,
  float U2,
  float V2
);

/// <summary>A decoded 72-byte level-of-detail record from an SVD resource.</summary>
public sealed record SceneryItemVisualLod(
  string Name,
  SvdLodType Type,
  string? StaticShapeRef,
  string? BoneShapeRef,
  string? FlexibleTextureRef,
  string? TextureStyleRef,
  SceneryVisualBillboardSettings Billboard,
  float Distance,
  IReadOnlyList<string> AnimationRefs
) {
  public uint Unknown2 { get; init; }
  public uint Unknown4 { get; init; }
  public uint Unknown14 { get; init; }
}

/// <summary>A decoded RCT3 scenery-item visual resource.</summary>
public sealed record SceneryItemVisual(
  string Name,
  SvdFlags Flags,
  float Sway,
  float Brightness,
  float Unknown4,
  float Scale,
  IReadOnlyList<SceneryItemVisualLod> Lods,
  string? ProxyRef
) {
  public uint Unknown6 { get; init; }
  public uint Unknown7 { get; init; }
  public uint Unknown8 { get; init; }
  public uint Unknown9 { get; init; }
  public uint Unknown10 { get; init; }
  public uint Unknown11 { get; init; }
  public uint? WildUnknown13 { get; init; }
  public int SerializedHeaderSize { get; init; }
}

/// <summary>Decodes relocation-backed <c>svd</c> resources without renderer dependencies.</summary>
public static class SceneryItemVisuals {
  // See sceneryvisual.h and ManagerSVD.cpp in rct3-importer's libOVLng. Frontier's pointers are
  // 32-bit, so the vanilla/Soaked/Wild headers are exactly 52/56/60 bytes and every LOD is 72.
  private const int BaseHeaderSize = 52;
  private const int SoakedHeaderSize = 56;
  private const int WildHeaderSize = 60;
  private const int LodSize = 72;
  private const int PointerSize = 4;
  private const int MaximumArrayBytes = 256 * 1024 * 1024;
  private const int MaximumLodCount = 16 * 1024;
  private const int MaximumAnimationCount = 64 * 1024;
  private const int MaximumVisualCount = 64 * 1024;
  private const int MaximumResourceCount = 1_000_000;
  private const int MaximumNameBytes = 4 * 1024;

  /// <summary>Decodes every scenery-item visual from the unique half of an OVL pair.</summary>
  public static IReadOnlyList<SceneryItemVisual> Extract(Ovl ovl) =>
    Extract(ovl, SceneryVisualDecodeLimits.Default);

  internal static IReadOnlyList<SceneryItemVisual> Extract(
    Ovl ovl,
    SceneryVisualDecodeLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(ovl);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the SVD decoder limit {MaximumResourceCount}.");

    var context = new DecodeContext(limits);
    var files = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file =>
      file.Type == FileType.SceneryItemVisual &&
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))) {
      if (files.Count >= MaximumVisualCount)
        throw Invalid(file.Name,
          $"visual count exceeds the decoder limit {MaximumVisualCount}");
      context.ReserveObjects(1, file.Name, "visual resource index");
      files.Add(file);
    }

    var source = new OvlSceneryVisualDataSource(ovl, context);
    var visuals = new List<SceneryItemVisual>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier SVD at {address}");
      var owner = source.GetVisualLoader(file, address);
      visuals.Add(Decode(file.Name, owner, source, context));
    }
    return visuals;
  }

  internal static SceneryItemVisual Decode(
    string name,
    OvlLoaderEntry owner,
    ISceneryVisualDataSource source
  ) => Decode(
    name, owner, source, new DecodeContext(SceneryVisualDecodeLimits.Default));

  internal static SceneryItemVisual Decode(
    string name,
    OvlLoaderEntry owner,
    ISceneryVisualDataSource source,
    SceneryVisualDecodeLimits limits
  ) => Decode(name, owner, source, new DecodeContext(limits));

  private static SceneryItemVisual Decode(
    string name,
    OvlLoaderEntry owner,
    ISceneryVisualDataSource source,
    DecodeContext context
  ) {
    if (owner.Tag.ToFileType() != FileType.SceneryItemVisual)
      throw Invalid(name, $"loader type '{owner.Tag}' is not svd");
    var address = owner.DataAddress;
    var header = ReadExact(source, address, BaseHeaderSize, name, "base header", context);
    var flags = (SvdFlags)ReadUInt32(header, 0);
    var headerSize = HeaderSize(flags);
    var extension = headerSize == BaseHeaderSize
      ? []
      : ReadExact(
        source,
        CheckedAdd(address, BaseHeaderSize, name),
        headerSize - BaseHeaderSize,
        name,
        "versioned header extension",
        context);

    var sway = ReadFiniteSingle(header, 4, name, "sway");
    var brightness = ReadFiniteSingle(header, 8, name, "brightness");
    var unknown4 = ReadFiniteSingle(header, 12, name, "unknown header float");
    var scale = ReadFiniteSingle(header, 16, name, "scale");
    var lodCount = ReadUInt32(header, 20);
    if (lodCount == 0) throw Invalid(name, "LOD count is zero");
    if (lodCount > MaximumLodCount)
      throw Invalid(name, $"LOD count {lodCount} exceeds the decoder limit {MaximumLodCount}");
    context.ReserveObjects(Convert.ToUInt64(lodCount) + 1, name, "visual and LOD objects");

    var lodPointerAddress = ReadRequiredPointer(
      source,
      CheckedAdd(address, 24, name),
      ReadUInt32(header, 24),
      name,
      "LOD pointer array");
    var pointerBytes = ReadArray(
      source, lodPointerAddress, lodCount, PointerSize, name, "LOD pointer array", context);
    var lods = new SceneryItemVisualLod[ToCount(lodCount, name, "LOD count")];
    var lodAddresses = new HashSet<uint>();
    foreach (var index in Enumerable.Range(0, lods.Length)) {
      var slotAddress = CheckedAdd(
        lodPointerAddress, index * PointerSize, name);
      var storedAddress = ReadUInt32(pointerBytes, index * PointerSize);
      var lodAddress = ReadRequiredPointer(
        source, slotAddress, storedAddress, name, $"LOD {index} pointer");
      if (!lodAddresses.Add(lodAddress))
        throw Invalid(name, $"LOD {index} aliases an earlier LOD record at {lodAddress}");
      lods[index] = ReadLod(name, owner, index, lodAddress, source, context);
    }

    var proxyRef = headerSize == BaseHeaderSize
      ? null
      : ReadResourceReference(
        name,
        "proxy",
        CheckedAdd(address, 52, name),
        ReadUInt32(extension, 0),
        "mam",
        owner,
        source,
        false);
    return new SceneryItemVisual(
      name, flags, sway, brightness, unknown4, scale, lods, proxyRef) {
      Unknown6 = ReadUInt32(header, 28),
      Unknown7 = ReadUInt32(header, 32),
      Unknown8 = ReadUInt32(header, 36),
      Unknown9 = ReadUInt32(header, 40),
      Unknown10 = ReadUInt32(header, 44),
      Unknown11 = ReadUInt32(header, 48),
      WildUnknown13 = headerSize == WildHeaderSize ? ReadUInt32(extension, 4) : null,
      SerializedHeaderSize = headerSize
    };
  }

  private static SceneryItemVisualLod ReadLod(
    string visualName,
    OvlLoaderEntry owner,
    int index,
    uint address,
    ISceneryVisualDataSource source,
    DecodeContext context
  ) {
    var bytes = ReadExact(
      source, address, LodSize, visualName, $"LOD {index} record", context);
    var rawType = ReadUInt32(bytes, 0);
    if (rawType is not 0 and not 3 and not 4)
      throw Invalid(visualName, $"LOD {index} has unsupported type {rawType}");
    var type = (SvdLodType)rawType;
    var name = ReadRequiredString(
      source,
      CheckedAdd(address, 4, visualName),
      ReadUInt32(bytes, 4),
      visualName,
      $"LOD {index} name",
      context);

    var staticShapeRef = ReadResourceReference(
      visualName,
      $"LOD {index} shs_ref",
      CheckedAdd(address, 8, visualName),
      ReadUInt32(bytes, 8),
      "shs",
      owner,
      source,
      type == SvdLodType.StaticShape);
    var boneShapeRef = ReadResourceReference(
      visualName,
      $"LOD {index} bsh_ref",
      CheckedAdd(address, 16, visualName),
      ReadUInt32(bytes, 16),
      "bsh",
      owner,
      source,
      type == SvdLodType.BoneShape);
    var flexiTextureRef = ReadResourceReference(
      visualName,
      $"LOD {index} ftx_ref",
      CheckedAdd(address, 24, visualName),
      ReadUInt32(bytes, 24),
      "ftx",
      owner,
      source,
      type == SvdLodType.Billboard);
    var textureStyleRef = ReadResourceReference(
      visualName,
      $"LOD {index} txs_ref",
      CheckedAdd(address, 28, visualName),
      ReadUInt32(bytes, 28),
      "txs",
      owner,
      source,
      type == SvdLodType.Billboard);
    if (type != SvdLodType.StaticShape && staticShapeRef != null)
      throw Invalid(visualName, $"LOD {index} has an shs_ref for type {rawType}");
    if (type != SvdLodType.BoneShape && boneShapeRef != null)
      throw Invalid(visualName, $"LOD {index} has a bsh_ref for type {rawType}");
    if (type != SvdLodType.Billboard &&
        (flexiTextureRef != null || textureStyleRef != null))
      throw Invalid(visualName, $"LOD {index} has billboard references for type {rawType}");

    var billboard = new SceneryVisualBillboardSettings(
      ReadFiniteSingle(bytes, 32, visualName, $"LOD {index} billboard width"),
      ReadFiniteSingle(bytes, 36, visualName, $"LOD {index} billboard height"),
      ReadFiniteSingle(bytes, 40, visualName, $"LOD {index} billboard U1"),
      ReadFiniteSingle(bytes, 44, visualName, $"LOD {index} billboard V1"),
      ReadFiniteSingle(bytes, 48, visualName, $"LOD {index} billboard U2"),
      ReadFiniteSingle(bytes, 52, visualName, $"LOD {index} billboard V2"));
    var distance = ReadFiniteSingle(bytes, 56, visualName, $"LOD {index} distance");
    var animationCount = ReadUInt32(bytes, 60);
    if (animationCount > MaximumAnimationCount)
      throw Invalid(visualName,
        $"LOD {index} animation count {animationCount} exceeds the decoder limit " +
        MaximumAnimationCount);
    var animations = ReadAnimations(
      visualName,
      owner,
      index,
      address,
      animationCount,
      ReadUInt32(bytes, 68),
      source,
      context);
    return new SceneryItemVisualLod(
      name,
      type,
      staticShapeRef,
      boneShapeRef,
      flexiTextureRef,
      textureStyleRef,
      billboard,
      distance,
      animations) {
      Unknown2 = ReadUInt32(bytes, 12),
      Unknown4 = ReadUInt32(bytes, 20),
      Unknown14 = ReadUInt32(bytes, 64)
    };
  }

  private static IReadOnlyList<string> ReadAnimations(
    string visualName,
    OvlLoaderEntry owner,
    int lodIndex,
    uint lodAddress,
    uint count,
    uint storedPointer,
    ISceneryVisualDataSource source,
    DecodeContext context
  ) {
    var fieldAddress = CheckedAdd(lodAddress, 68, visualName);
    if (count == 0) {
      if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
        throw Invalid(visualName,
          $"LOD {lodIndex} has an animation pointer but its animation count is zero");
      return [];
    }

    context.ReserveObjects(count, visualName, $"LOD {lodIndex} animation references");
    var pointerAddress = ReadRequiredPointer(
      source,
      fieldAddress,
      storedPointer,
      visualName,
      $"LOD {lodIndex} animation pointer array");
    var pointerBytes = ReadArray(
      source,
      pointerAddress,
      count,
      PointerSize,
      visualName,
      $"LOD {lodIndex} animation pointer array",
      context);
    var animations = new string[ToCount(count, visualName, "animation count")];
    var referenceFields = new HashSet<uint>();
    foreach (var index in Enumerable.Range(0, animations.Length)) {
      var slotAddress = CheckedAdd(
        pointerAddress, index * PointerSize, visualName);
      var storedReferenceField = ReadUInt32(pointerBytes, index * PointerSize);
      var referenceField = ReadRequiredPointer(
        source,
        slotAddress,
        storedReferenceField,
        visualName,
        $"LOD {lodIndex} animation {index} reference pointer");
      if (!referenceFields.Add(referenceField))
        throw Invalid(visualName,
          $"LOD {lodIndex} animation {index} aliases an earlier reference field at " +
          referenceField);
      var rawReference = ReadExact(
        source,
        referenceField,
        PointerSize,
        visualName,
        $"LOD {lodIndex} animation {index} reference field",
        context);
      animations[index] = ReadResourceReference(
        visualName,
        $"LOD {lodIndex} animation {index}",
        referenceField,
        ReadUInt32(rawReference, 0),
        "ban",
        owner,
        source,
        true)!;
    }
    return animations;
  }

  private static string? ReadResourceReference(
    string visualName,
    string description,
    uint fieldAddress,
    uint rawValue,
    string expectedTag,
    OvlLoaderEntry owner,
    ISceneryVisualDataSource source,
    bool required
  ) {
    // ManagerSVD stores these fields as null and emits SymbolRefStruct records naming the targets.
    if (source.ResourceReferences.TryGetValue(fieldAddress, out var reference)) {
      if (rawValue != 0 || source.TryGetRelocationSource(fieldAddress, out _))
        throw Invalid(visualName,
          $"{description} has conflicting direct and SymbolRef values");
      if (!SameLoader(reference.Owner, owner))
        throw Invalid(visualName, $"{description} belongs to another loader");
      if (!HasTag(reference.Symbol, expectedTag))
        throw Invalid(visualName,
          $"{description} targets '{reference.Symbol}' instead of {expectedTag}");
      if (source.ResourcesByKey.TryGetValue(reference.Symbol, out var metadata)) {
        if (!string.Equals(metadata.Tag, expectedTag, StringComparison.OrdinalIgnoreCase))
          throw Invalid(visualName,
            $"{description} target '{reference.Symbol}' has conflicting archive metadata");
      } else if (source.LocalResourceKeys.Contains(reference.Symbol)) {
        throw Invalid(visualName,
          $"{description} target '{reference.Symbol}' is local but has no validated loader metadata");
      }
      return reference.Symbol;
    }

    if (rawValue != 0 || source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(visualName,
        $"{description} is not an exact SymbolRef owned by the SVD loader");
    if (required) throw Invalid(visualName, $"{description} is missing");
    return null;
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static bool HasTag(string key, string tag) =>
    key.EndsWith($":{tag}", StringComparison.OrdinalIgnoreCase);

  private static int HeaderSize(SvdFlags flags) {
    if ((flags & SvdFlags.Soaked) != 0) return SoakedHeaderSize;
    if ((flags & SvdFlags.Wild) != 0) return WildHeaderSize;
    return BaseHeaderSize;
  }

  private static string ReadRequiredString(
    ISceneryVisualDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string visualName,
    string description,
    DecodeContext context
  ) {
    var address = ReadRequiredPointer(
      source, fieldAddress, storedPointer, visualName, description);
    if (!source.TryGetNullTerminatedStringByteLength(
          address, MaximumNameBytes, out var length) || length == 0)
      throw Invalid(visualName,
        $"{description} is missing, unterminated, or exceeds {MaximumNameBytes} bytes");
    context.ReserveBytes(Convert.ToUInt64(length) + 1, visualName, description);
    if (!source.TryReadNullTerminatedString(address, length + 1, out var value) ||
        string.IsNullOrEmpty(value))
      throw Invalid(visualName, $"{description} changed while it was being decoded");
    return value;
  }

  private static uint ReadRequiredPointer(
    ISceneryVisualDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string visualName,
    string description
  ) {
    // Virtual address zero is the first byte of block 0, not necessarily a null pointer. ManagerSVD
    // can place the first LOD name there, so relocation-table membership is the pointer evidence.
    if (!source.TryGetRelocationSource(fieldAddress, out var target))
      throw Invalid(visualName, $"{description} is not a relocated pointer");
    if (storedPointer != target)
      throw Invalid(visualName,
        $"{description} does not match its relocation target");
    return target;
  }

  private static byte[] ReadArray(
    ISceneryVisualDataSource source,
    uint address,
    uint count,
    int stride,
    string visualName,
    string description,
    DecodeContext context
  ) => ReadExact(
    source,
    address,
    ByteCount(count, stride, visualName, description),
    visualName,
    description,
    context);

  private static byte[] ReadExact(
    ISceneryVisualDataSource source,
    uint address,
    int length,
    string visualName,
    string description,
    DecodeContext context
  ) {
    context.ReserveBytes(Convert.ToUInt64(length), visualName, description);
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(visualName, $"{description} is outside the archive or truncated");
    return bytes;
  }

  private static int ByteCount(
    uint count,
    int stride,
    string visualName,
    string description
  ) {
    var length = Convert.ToUInt64(count) * Convert.ToUInt64(stride);
    if (length > MaximumArrayBytes || length > int.MaxValue)
      throw Invalid(visualName,
        $"{description} exceeds the decoder byte limit {MaximumArrayBytes}");
    return Convert.ToInt32(length);
  }

  private static int ToCount(uint count, string visualName, string description) {
    if (count > int.MaxValue)
      throw Invalid(visualName, $"{description} {count} exceeds the decoder limit");
    return Convert.ToInt32(count);
  }

  private static uint CheckedAdd(uint address, int offset, string visualName) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(visualName, "pointer address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static uint ReadUInt32(byte[] bytes, int offset) =>
    BitConverter.ToUInt32(bytes, offset);

  private static float ReadFiniteSingle(
    byte[] bytes,
    int offset,
    string visualName,
    string description
  ) {
    var value = BitConverter.ToSingle(bytes, offset);
    if (!float.IsFinite(value))
      throw Invalid(visualName, $"{description} contains a non-finite value");
    return value;
  }

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Scenery visual '{name}' is malformed: {message}.");

  private sealed class DecodeContext(SceneryVisualDecodeLimits limits) {
    private ulong decodedBytes;
    private ulong decodedObjects;

    public void ReserveBytes(ulong count, string visualName, string description) {
      if (count > limits.MaximumBytes || decodedBytes > limits.MaximumBytes - count)
        throw Invalid(visualName,
          $"aggregate decode bytes exceed the limit {limits.MaximumBytes} " +
          $"while reading {description}");
      decodedBytes += count;
    }

    public void ReserveObjects(ulong count, string visualName, string description) {
      if (count > limits.MaximumObjects || decodedObjects > limits.MaximumObjects - count)
        throw Invalid(visualName,
          $"aggregate decoded objects exceed the limit {limits.MaximumObjects} " +
          $"while reading {description}");
      decodedObjects += count;
    }
  }

  private sealed class OvlSceneryVisualDataSource : ISceneryVisualDataSource {
    private readonly Ovl ovl;
    private readonly DecodeContext context;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;

    public OvlSceneryVisualDataSource(Ovl ovl, DecodeContext context) {
      this.ovl = ovl;
      this.context = context;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the SVD decoder limit " +
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
      var localKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var resourcesByKey = new Dictionary<string, SceneryVisualResourceMetadata>(
        StringComparer.OrdinalIgnoreCase);
      foreach (var file in ovl.Keys) {
        var key = CreateResourceKey(file);
        localKeys.Add(key);
        context.ReserveBytes(
          Convert.ToUInt64(Encoding.ASCII.GetByteCount(key)) + 1,
          file.Name,
          "resource index key");
        context.ReserveObjects(1, file.Name, "resource key index");
        if (!ovl.TryGetDataPointer(file, out var address) ||
            !TryGetExactLoader(address, file.Path, out var loader))
          continue;
        var metadata = new SceneryVisualResourceMetadata(key, loader.Tag);
        if (!resourcesByKey.TryAdd(key, metadata) && resourcesByKey[key] != metadata)
          throw new InvalidDataException(
            $"OVL resource key '{key}' has conflicting archive metadata.");
        context.ReserveObjects(1, file.Name, "resource metadata index");
      }
      LocalResourceKeys = localKeys;
      ResourcesByKey = resourcesByKey;
    }

    public IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
    public IReadOnlyDictionary<string, SceneryVisualResourceMetadata> ResourcesByKey { get; }
    public IReadOnlySet<string> LocalResourceKeys { get; }

    public OvlLoaderEntry GetVisualLoader(OvlFile file, uint address) {
      if (!TryGetExactLoader(address, file.Path, out var loader) ||
          loader.Tag.ToFileType() != FileType.SceneryItemVisual)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact svd loader-table entry");
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

    public bool TryGetNullTerminatedStringByteLength(
      uint address,
      int maximumLength,
      out int length
    ) {
      if (!TryGetStringLocation(address, out var block, out var start)) {
        length = 0;
        return false;
      }
      return TryGetNullTerminatedByteLength(
        block, start, maximumLength, out length);
    }

    public bool TryReadNullTerminatedString(uint address, int maximumLength, out string value) {
      if (!TryGetStringLocation(address, out var block, out var start)) {
        value = string.Empty;
        return false;
      }
      var available = Math.Min(maximumLength, block.Length - start);
      var end = Array.IndexOf(block, Convert.ToByte(0), start, available);
      if (end < 0) {
        value = string.Empty;
        return false;
      }
      value = Encoding.ASCII.GetString(block, start, end - start);
      return true;
    }

    private bool TryGetStringLocation(uint address, out byte[] block, out int start) {
      if (address != 0) {
        if (!ovl.TryResolveRelocation(address, out block!, out var offset)) {
          start = 0;
          return false;
        }
        start = Convert.ToInt32(offset);
        return true;
      }

      // A valid LOD name can be the first string-table entry at address zero. Resolving address one
      // proves that a real block begins at zero without weakening Ovl's general null-pointer rule.
      if (!ovl.TryResolveRelocation(1, out block!, out var offsetAtOne) || offsetAtOne != 1) {
        start = 0;
        return false;
      }
      start = 0;
      return true;
    }

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

    private static string CreateResourceKey(OvlFile file) {
      if (file.Type == FileType.Unknown && file.Name.Contains(':')) return file.Name;
      return $"{file.Name}:{file.Type.ToTagString()}";
    }

    private static bool TryGetNullTerminatedByteLength(
      byte[] block,
      int start,
      int maximumLength,
      out int length
    ) {
      if (start < 0 || start >= block.Length || maximumLength <= 0) {
        length = 0;
        return false;
      }
      var available = Math.Min(maximumLength, block.Length - start);
      var end = Array.IndexOf(block, Convert.ToByte(0), start, available);
      length = end < 0 ? 0 : end - start;
      return end >= 0;
    }
  }
}

internal sealed record SceneryVisualResourceMetadata(string Key, string Tag);

internal readonly record struct SceneryVisualDecodeLimits(
  ulong MaximumBytes,
  ulong MaximumObjects
) {
  public static SceneryVisualDecodeLimits Default { get; } =
    new(256UL * 1024 * 1024, 1_000_000);
}

internal interface ISceneryVisualDataSource {
  IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
  IReadOnlyDictionary<string, SceneryVisualResourceMetadata> ResourcesByKey { get; }
  IReadOnlySet<string> LocalResourceKeys { get; }
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryGetNullTerminatedStringByteLength(uint address, int maximumLength, out int length);
  bool TryReadNullTerminatedString(uint address, int maximumLength, out string value);
}
