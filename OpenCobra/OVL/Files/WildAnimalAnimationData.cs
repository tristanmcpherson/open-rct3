// Wild Animal Animation Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenCobra.OVL.Files;

/// <summary>The structurally proven fields in one <c>wad</c> resource.</summary>
public sealed record WildAnimalAnimationDataDefinition(
  string Name,
  string SourcePath,
  uint DataAddress,
  uint Field00,
  uint Field04,
  uint SerializedCountAt08,
  uint ReferencesAt0CAddress,
  uint ValuesAt10Address,
  uint ValuesAt14Address,
  float Field18,
  float Field1C,
  float Field20,
  float Field24,
  float Field28,
  float Field2C,
  float Field30,
  float Field34,
  IReadOnlyList<string> ModelAnimationReferencesAt38,
  IReadOnlyList<float> ValuesAt10,
  IReadOnlyList<float> ValuesAt14
);

/// <summary>Decodes installed Complete Edition <c>wad</c> resources.</summary>
/// <remarks>
/// The pinned rct3-importer source establishes the version-five loader and SymbolRef envelope but
/// contains no WAD manager. Complete Edition <c>RCT3.exe</c> SHA-256
/// <c>1C9316E728D67AAA3BFE36D0A1634F3139582927440A5467C351E4C949B5B21D</c> registers
/// <c>WildAnimalAnimData</c>/<c>wad</c>. Installed Elephant and Ostrich archives independently
/// prove the same 180-byte record: count 31 at +0x08, three relocation-backed pointers at
/// +0x0C..+0x14, eight finite opaque values at +0x18..+0x34, and 31 owner-bound
/// <c>modelanim</c> SymbolRefs at +0x38. The two relocated value arrays each contain 31 adjacent
/// finite floats. Unknown fields remain named by offset and no animation-slot semantics are
/// inferred.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLDump/OVLDump.cpp#L586-L664">
/// Pinned version-five loader and SymbolRef reader
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/ovlstructs.h#L71-L90">
/// Pinned loader and SymbolRef structures
/// </seealso>
public static class WildAnimalAnimationData {
  private const int RecordSize = 0xB4;
  private const int SerializedCountOffset = 0x08;
  private const int ReferencesPointerOffset = 0x0C;
  private const int ValuesAt10PointerOffset = 0x10;
  private const int ValuesAt14PointerOffset = 0x14;
  private const int FirstOpaqueFloatOffset = 0x18;
  private const int ReferenceArrayOffset = 0x38;
  private const int ValueCount = 31;
  private const int ValueArraySize = ValueCount * sizeof(float);
  private const int CombinedValueArraySize = ValueArraySize * 2;
  private const int MaximumAnimationDataCount = 64 * 1024;
  private const int MaximumResourceCount = 1_000_000;

  /// <summary>Decodes every uniquely owned WAD in data-address order.</summary>
  public static IReadOnlyList<WildAnimalAnimationDataDefinition> Extract(Ovl ovl) {
    ArgumentNullException.ThrowIfNull(ovl);
    ValidateResourceCount(ovl);

    var resources = new List<(OvlFile File, uint Address)>();
    foreach (var file in ovl.Keys.Where(file =>
               file.Type == FileType.WildAnimalAnimData)) {
      if (resources.Count >= MaximumAnimationDataCount)
        throw Invalid(file.Name,
          $"resource count exceeds the decoder limit {MaximumAnimationDataCount}");
      RequireUniqueSource(file);
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      resources.Add((file, address));
    }

    var source = new OvlWildAnimalAnimationDataSource(ovl);
    var decoded = new List<WildAnimalAnimationDataDefinition>(resources.Count);
    var addresses = new HashSet<uint>();
    foreach (var resource in resources.OrderBy(resource => resource.Address)) {
      if (!addresses.Add(resource.Address))
        throw Invalid(resource.File.Name,
          $"resource record aliases an earlier WAD at {resource.Address}");
      var owner = source.GetAnimationDataLoader(resource.File, resource.Address);
      decoded.Add(Decode(resource.File.Name, ovl.Version, owner, source));
    }
    return decoded.AsReadOnly();
  }

  /// <summary>Decodes one exact, case-insensitively matched <c>Name:wad</c> reference.</summary>
  public static WildAnimalAnimationDataDefinition Extract(Ovl ovl, string taggedReference) {
    ArgumentNullException.ThrowIfNull(ovl);
    ArgumentException.ThrowIfNullOrWhiteSpace(taggedReference);
    ValidateResourceCount(ovl);

    var name = ParseTaggedReference(taggedReference);
    var matches = ovl.Keys.Where(file =>
      file.Type == FileType.WildAnimalAnimData &&
      string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1)
      throw Invalid(name,
        $"reference resolves to {matches.Count} Wild animal animation data resources instead of one");

    var file = matches[0];
    RequireUniqueSource(file);
    if (!ovl.TryGetDataPointer(file, out var address))
      throw Invalid(name, "resource data pointer is missing");
    var source = new OvlWildAnimalAnimationDataSource(ovl);
    var owner = source.GetAnimationDataLoader(file, address);
    return Decode(file.Name, ovl.Version, owner, source);
  }

  internal static WildAnimalAnimationDataDefinition Decode(
    string name,
    Version archiveVersion,
    OvlLoaderEntry owner,
    IWildAnimalAnimationDataSource source
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.WildAnimalAnimData)
      throw Invalid(name, $"loader type '{owner.Tag}' is not wad");
    if (archiveVersion != Version.Five)
      throw Invalid(name,
        $"archive version {archiveVersion} is unsupported; installed WAD evidence is version five");
    if (string.IsNullOrWhiteSpace(owner.SourcePath) ||
        !owner.SourcePath.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid(name, "loader is not owned by a unique OVL archive");

    var address = owner.DataAddress;
    _ = CheckedAdd(address, RecordSize - 1, name);
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

    var nextLoader = regionLoaders
      .Where(loader => loader.DataAddress > address)
      .OrderBy(loader => loader.DataAddress)
      .FirstOrDefault();
    if (nextLoader != null) {
      var extent = Convert.ToUInt64(nextLoader.DataAddress) - Convert.ToUInt64(address);
      if (extent != RecordSize)
        throw Invalid(name,
          $"record extent {extent} does not match the installed size {RecordSize}");
    }

    if (!source.TryReadBytes(address, RecordSize, out var record) ||
        record.Length != RecordSize)
      throw Invalid(name, "record is outside the archive or truncated");
    if (nextLoader == null && source.TryReadBytes(address, RecordSize + 1, out _))
      throw Invalid(name,
        "final loader has unowned trailing bytes instead of ending at the proven record boundary");

    var field00 = ReadPlainUInt32(record, 0, address, name, "field +0x00", source);
    var field04 = ReadPlainUInt32(record, 4, address, name, "field +0x04", source);
    var serializedCount = ReadPlainUInt32(
      record,
      SerializedCountOffset,
      address,
      name,
      "serialized count at +0x08",
      source);
    if (serializedCount != ValueCount)
      throw Invalid(name,
        $"serialized count {serializedCount} does not match the installed count {ValueCount}");

    var referencesAddress = ReadRequiredPointer(
      record,
      ReferencesPointerOffset,
      address,
      name,
      "reference array at +0x0C",
      source);
    var expectedReferencesAddress = CheckedAdd(address, ReferenceArrayOffset, name);
    if (referencesAddress != expectedReferencesAddress)
      throw Invalid(name,
        "reference array does not immediately follow the installed fixed prefix");

    var valuesAt10Address = ReadRequiredPointer(
      record,
      ValuesAt10PointerOffset,
      address,
      name,
      "value array at +0x10",
      source);
    var valuesAt14Address = ReadRequiredPointer(
      record,
      ValuesAt14PointerOffset,
      address,
      name,
      "value array at +0x14",
      source);
    var expectedValuesAt14Address = CheckedAdd(valuesAt10Address, ValueArraySize, name);
    if (valuesAt14Address != expectedValuesAt14Address)
      throw Invalid(name,
        "value arrays are not adjacent in the installed +0x10 then +0x14 address order");
    _ = CheckedAdd(valuesAt14Address, ValueArraySize - 1, name);
    if (RangesOverlap(address, RecordSize, valuesAt10Address, CombinedValueArraySize))
      throw Invalid(name, "value arrays overlap the WAD record");

    var field18 = ReadPlainFiniteSingle(
      record, FirstOpaqueFloatOffset, address, name, "field +0x18", source);
    var field1C = ReadPlainFiniteSingle(
      record, 0x1C, address, name, "field +0x1C", source);
    var field20 = ReadPlainFiniteSingle(
      record, 0x20, address, name, "field +0x20", source);
    var field24 = ReadPlainFiniteSingle(
      record, 0x24, address, name, "field +0x24", source);
    var field28 = ReadPlainFiniteSingle(
      record, 0x28, address, name, "field +0x28", source);
    var field2C = ReadPlainFiniteSingle(
      record, 0x2C, address, name, "field +0x2C", source);
    var field30 = ReadPlainFiniteSingle(
      record, 0x30, address, name, "field +0x30", source);
    var field34 = ReadPlainFiniteSingle(
      record, 0x34, address, name, "field +0x34", source);

    var referenceFields = new HashSet<uint>();
    var references = new string[ValueCount];
    foreach (var index in Enumerable.Range(0, ValueCount)) {
      var recordOffset = checked(ReferenceArrayOffset + index * sizeof(uint));
      var fieldAddress = CheckedAdd(address, recordOffset, name);
      referenceFields.Add(fieldAddress);
      references[index] = ReadRequiredModelAnimationReference(
        record,
        recordOffset,
        fieldAddress,
        owner,
        name,
        index,
        source);
    }
    ValidateOwnedReferences(name, owner, referenceFields, source);

    if (!source.TryReadBytes(
          valuesAt10Address, CombinedValueArraySize, out var valueBytes) ||
        valueBytes.Length != CombinedValueArraySize)
      throw Invalid(name, "adjacent value arrays are outside one archive block or truncated");
    var valuesAt10 = ReadPlainFiniteArray(
      valueBytes, 0, valuesAt10Address, name, "+0x10", source);
    var valuesAt14 = ReadPlainFiniteArray(
      valueBytes, ValueArraySize, valuesAt14Address, name, "+0x14", source);

    return new WildAnimalAnimationDataDefinition(
      name,
      owner.SourcePath,
      address,
      field00,
      field04,
      serializedCount,
      referencesAddress,
      valuesAt10Address,
      valuesAt14Address,
      field18,
      field1C,
      field20,
      field24,
      field28,
      field2C,
      field30,
      field34,
      Array.AsReadOnly(references),
      Array.AsReadOnly(valuesAt10),
      Array.AsReadOnly(valuesAt14));
  }

  private static uint ReadPlainUInt32(
    byte[] record,
    int offset,
    uint address,
    string name,
    string description,
    IWildAnimalAnimationDataSource source
  ) {
    RequirePlainField(CheckedAdd(address, offset, name), name, description, source);
    return BitConverter.ToUInt32(record, offset);
  }

  private static float ReadPlainFiniteSingle(
    byte[] record,
    int offset,
    uint address,
    string name,
    string description,
    IWildAnimalAnimationDataSource source
  ) {
    RequirePlainField(CheckedAdd(address, offset, name), name, description, source);
    var value = BitConverter.ToSingle(record, offset);
    if (!float.IsFinite(value)) throw Invalid(name, $"{description} is non-finite");
    return value;
  }

  private static float[] ReadPlainFiniteArray(
    byte[] bytes,
    int sourceOffset,
    uint address,
    string name,
    string offsetName,
    IWildAnimalAnimationDataSource source
  ) {
    var values = new float[ValueCount];
    foreach (var index in Enumerable.Range(0, ValueCount)) {
      var byteOffset = checked(index * sizeof(float));
      var fieldAddress = CheckedAdd(address, byteOffset, name);
      RequirePlainField(
        fieldAddress,
        name,
        $"{offsetName} value {index}",
        source);
      var value = BitConverter.ToSingle(bytes, checked(sourceOffset + byteOffset));
      if (!float.IsFinite(value))
        throw Invalid(name, $"{offsetName} value {index} is non-finite");
      values[index] = value;
    }
    return values;
  }

  private static uint ReadRequiredPointer(
    byte[] record,
    int offset,
    uint address,
    string name,
    string description,
    IWildAnimalAnimationDataSource source
  ) {
    var fieldAddress = CheckedAdd(address, offset, name);
    var storedPointer = BitConverter.ToUInt32(record, offset);
    if (source.ResourceReferences.ContainsKey(fieldAddress))
      throw Invalid(name, $"{description} conflicts with a SymbolRef");
    if (!source.TryGetRelocationSource(fieldAddress, out var target)) {
      if (storedPointer != 0)
        throw Invalid(name, $"{description} contains an unproven pointer {storedPointer}");
      throw Invalid(name, $"{description} is not a relocated pointer");
    }
    if (target == 0 || storedPointer != target)
      throw Invalid(name, $"{description} does not match its relocation target");
    return target;
  }

  private static string ReadRequiredModelAnimationReference(
    byte[] record,
    int recordOffset,
    uint fieldAddress,
    OvlLoaderEntry owner,
    string name,
    int index,
    IWildAnimalAnimationDataSource source
  ) {
    var rawValue = BitConverter.ToUInt32(record, recordOffset);
    if (!source.ResourceReferences.TryGetValue(fieldAddress, out var reference)) {
      if (rawValue != 0 || source.TryGetRelocationSource(fieldAddress, out _))
        throw Invalid(name,
          $"model-animation reference {index} contains conflicting pointer data");
      throw Invalid(name, $"model-animation reference {index} SymbolRef is missing");
    }
    if (!SameLoader(reference.Owner, owner))
      throw Invalid(name, $"model-animation reference {index} is owned by another loader");
    if (!IsExactModelAnimationReference(reference.Symbol))
      throw Invalid(name,
        $"model-animation reference {index} '{reference.Symbol}' is not an exact modelanim identity");
    if (rawValue != 0 || source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(name,
        $"model-animation reference {index} field contains conflicting pointer data");
    return reference.Symbol;
  }

  private static void ValidateOwnedReferences(
    string name,
    OvlLoaderEntry owner,
    IReadOnlySet<uint> expectedFields,
    IWildAnimalAnimationDataSource source
  ) {
    var owned = source.GetOwnedResourceReferences(owner);
    if (owned.Count != ValueCount)
      throw Invalid(name,
        $"loader owns {owned.Count} SymbolRefs instead of the installed count {ValueCount}");
    foreach (var reference in owned) {
      if (!SameLoader(reference.Value.Owner, owner))
        throw Invalid(name, "owner SymbolRef index contains another loader");
      if (!expectedFields.Contains(reference.Key))
        throw Invalid(name,
          $"loader owns an ambiguous SymbolRef at {reference.Key} outside +0x38");
    }
  }

  private static void RequirePlainField(
    uint fieldAddress,
    string name,
    string description,
    IWildAnimalAnimationDataSource source
  ) {
    if (source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(name, $"{description} has an unproven relocation");
    if (source.ResourceReferences.ContainsKey(fieldAddress))
      throw Invalid(name, $"{description} conflicts with a SymbolRef");
  }

  private static bool IsExactModelAnimationReference(string symbol) {
    if (string.IsNullOrEmpty(symbol)) return false;
    var separator = symbol.IndexOf(':');
    if (separator < 0 || separator != symbol.LastIndexOf(':')) return false;
    if (!symbol[(separator + 1)..].Equals(
          "modelanim", StringComparison.OrdinalIgnoreCase)) return false;
    if (separator == 0) return symbol.Equals(":modelanim", StringComparison.OrdinalIgnoreCase);

    var name = symbol[..separator];
    return !string.IsNullOrWhiteSpace(name) &&
      name.Equals(name.Trim(), StringComparison.Ordinal) &&
      !name.Contains('/') &&
      !name.Contains('\\');
  }

  private static string ParseTaggedReference(string reference) {
    var separator = reference.IndexOf(':');
    if (separator <= 0 || separator != reference.LastIndexOf(':') ||
        !reference[(separator + 1)..].Equals("wad", StringComparison.OrdinalIgnoreCase))
      throw new ArgumentException(
        $"'{reference}' is not an exact Name:wad reference.", nameof(reference));
    var name = reference[..separator];
    if (string.IsNullOrWhiteSpace(name) ||
        !name.Equals(name.Trim(), StringComparison.Ordinal) ||
        name.Contains('/') ||
        name.Contains('\\'))
      throw new ArgumentException(
        $"'{reference}' is not an exact Name:wad reference.", nameof(reference));
    return name;
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static bool RangesOverlap(
    uint firstAddress,
    int firstLength,
    uint secondAddress,
    int secondLength
  ) {
    var firstStart = Convert.ToUInt64(firstAddress);
    var firstEnd = firstStart + Convert.ToUInt64(firstLength);
    var secondStart = Convert.ToUInt64(secondAddress);
    var secondEnd = secondStart + Convert.ToUInt64(secondLength);
    return firstStart < secondEnd && secondStart < firstEnd;
  }

  private static uint CheckedAdd(uint address, int offset, string name) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(name, "field address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static void ValidateResourceCount(Ovl ovl) {
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the WAD decoder limit " +
        $"{MaximumResourceCount}.");
  }

  private static void RequireUniqueSource(OvlFile file) {
    if (!file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid(file.Name, "resource is not owned by a unique OVL archive");
  }

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Wild animal animation data '{name}' is malformed: {message}.");

  private sealed class OvlWildAnimalAnimationDataSource : IWildAnimalAnimationDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyList<OvlLoaderEntry> loaders;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;
    private readonly IReadOnlyDictionary<uint,
      IReadOnlyList<KeyValuePair<uint, OvlSymbolReference>>> referencesByOwner;

    public OvlWildAnimalAnimationDataSource(Ovl ovl) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the WAD decoder limit " +
          $"{MaximumResourceCount}.");

      loaders = ovl.LoaderEntriesInOrder.ToArray();
      var mutableLoaders = new Dictionary<uint, List<OvlLoaderEntry>>();
      foreach (var entry in loaders) {
        if (!mutableLoaders.TryGetValue(entry.DataAddress, out var entries))
          mutableLoaders.Add(entry.DataAddress, entries = []);
        entries.Add(entry);
      }
      loadersByDataAddress = mutableLoaders.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<OvlLoaderEntry>)pair.Value.AsReadOnly());

      ResourceReferences = OvlSymbolReferenceIndex.Create(ovl).References;
      var mutableReferences = new Dictionary<uint,
        List<KeyValuePair<uint, OvlSymbolReference>>>();
      foreach (var reference in ResourceReferences) {
        var ownerAddress = reference.Value.Owner.StructAddress;
        if (!mutableReferences.TryGetValue(ownerAddress, out var references))
          mutableReferences.Add(ownerAddress, references = []);
        references.Add(reference);
      }
      referencesByOwner = mutableReferences.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<KeyValuePair<uint, OvlSymbolReference>>)pair.Value.AsReadOnly());
    }

    public IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }

    public OvlLoaderEntry GetAnimationDataLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact wad loader-table entry");
      var matches = candidates.Where(entry =>
        entry.Tag.ToFileType() == FileType.WildAnimalAnimData &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact wad loader-table entry");
      return matches[0];
    }

    public IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner) {
      if (!ovl.TryResolveRelocation(owner.DataAddress, out var ownerBlock, out _)) return [];

      // Reference identity proves that candidates share the exact archive block containing the
      // WAD, not merely a nearby virtual address. A final WAD is accepted only when an exact-size
      // read succeeds and the following byte cannot resolve in that block.
      var result = new List<OvlLoaderEntry>();
      foreach (var entry in loaders.Where(entry => string.Equals(
                 entry.SourcePath, owner.SourcePath, StringComparison.OrdinalIgnoreCase))) {
        if (!ovl.TryResolveRelocation(entry.DataAddress, out var candidateBlock, out _) ||
            !ReferenceEquals(ownerBlock, candidateBlock)) continue;
        result.Add(entry);
      }
      return result.AsReadOnly();
    }

    public IReadOnlyList<KeyValuePair<uint, OvlSymbolReference>> GetOwnedResourceReferences(
      OvlLoaderEntry owner
    ) => referencesByOwner.TryGetValue(owner.StructAddress, out var references)
      ? references
      : [];

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
  }
}

internal interface IWildAnimalAnimationDataSource {
  IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
  IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner);
  IReadOnlyList<KeyValuePair<uint, OvlSymbolReference>> GetOwnedResourceReferences(
    OvlLoaderEntry owner);
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
}
