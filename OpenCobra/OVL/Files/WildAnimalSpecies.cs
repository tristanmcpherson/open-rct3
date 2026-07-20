// Wild Animal Species
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>One of the four serialized model/animation variants in a Wild animal species.</summary>
public sealed record WildAnimalSpeciesVariant(
  string ModelReference,
  string AnimationDataReference
);

/// <summary>The evidence-backed rendering references in one <c>was</c> resource.</summary>
public sealed record WildAnimalSpeciesDefinition(
  string Name,
  string PackagePath,
  IReadOnlyList<WildAnimalSpeciesVariant> Variants
);

/// <summary>Decodes the installed Complete Edition four-variant <c>was</c> layout.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> SHA-256
/// <c>1C9316E728D67AAA3BFE36D0A1634F3139582927440A5467C351E4C949B5B21D</c> registers the RCT3
/// loader name <c>WildAnimalSpecies</c> with tag <c>was</c> at VA 0x00F64870. The fixed layout
/// decoded here is intentionally limited to installed Elephant archive evidence: a 0x40-byte
/// header followed by four 0x478-byte variants. The executable registration and its empty
/// type-specific callback hooks do not themselves prove those sizes.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerCommon.cpp#L50">
/// rct3-importer WAS tag declaration
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/car.h#L278-L281">
/// rct3-importer Elephant ride-car WAS reference
/// </seealso>
public static class WildAnimalSpecies {
  private const int HeaderSize = 0x40;
  private const int VariantSize = 0x478;
  private const int VariantCount = 4;
  private const int RecordSize = HeaderSize + VariantCount * VariantSize;
  private const int PackagePathOffset = 0x14;
  private const int FirstVariantPointerOffset = 0x18;
  private const int MaximumStringBytes = 4 * 1024;
  private const int MaximumResourceCount = 1_000_000;

  /// <summary>Decodes one exact, case-insensitively matched <c>Name:was</c> reference.</summary>
  public static WildAnimalSpeciesDefinition Extract(Ovl ovl, string taggedReference) {
    ArgumentNullException.ThrowIfNull(ovl);
    ArgumentException.ThrowIfNullOrWhiteSpace(taggedReference);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the WAS decoder limit " +
        $"{MaximumResourceCount}.");

    var name = ParseTaggedReference(taggedReference);
    var matches = ovl.Keys.Where(file =>
      file.Type == FileType.WildAnimalSpecies &&
      string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1)
      throw Invalid(name,
        $"reference resolves to {matches.Count} Wild animal species resources instead of one");

    var file = matches[0];
    // Ovl.Load(commonPath) ingests both archive halves. OvlFile.Path is the exact block that owns
    // the resource symbol/data, and the installed WildAnimals WAS resources are owned by unique.
    if (!file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid(name, "resource is not owned by a unique OVL archive");
    if (!ovl.TryGetDataPointer(file, out var address))
      throw Invalid(name, "resource data pointer is missing");

    var source = new OvlWildAnimalSpeciesDataSource(ovl);
    var owner = source.GetSpeciesLoader(file, address);
    return Decode(file.Name, owner, source);
  }

  internal static WildAnimalSpeciesDefinition Decode(
    string name,
    OvlLoaderEntry owner,
    IWildAnimalSpeciesDataSource source
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.WildAnimalSpecies)
      throw Invalid(name, $"loader type '{owner.Tag}' is not was");

    var address = owner.DataAddress;
    var regionLoaders = source.GetDataRegionLoaders(owner);
    if (regionLoaders.Count(loader => SameLoader(loader, owner)) != 1)
      throw Invalid(name, "loader is not present exactly once in its proven data region");
    var nextLoader = regionLoaders
      .Where(loader => loader.DataAddress > address)
      .OrderBy(loader => loader.DataAddress)
      .FirstOrDefault();
    if (nextLoader == null)
      throw Invalid(name,
        "record is the final loader in its proven data region and its block end is unavailable");
    var recordEnd = nextLoader.DataAddress;
    var extent = Convert.ToUInt64(recordEnd) - Convert.ToUInt64(address);
    if (extent != RecordSize)
      throw Invalid(name,
        $"record extent {extent} does not match the installed four-variant size {RecordSize}");
    if (!source.TryReadBytes(address, RecordSize, out var record) ||
        record.Length != RecordSize)
      throw Invalid(name, "record is outside the archive or truncated");

    var packagePath = ReadRequiredString(
      source,
      CheckedAdd(address, PackagePathOffset, name),
      ReadUInt32(record, PackagePathOffset),
      name,
      "package path");
    var expectedReferenceFields = new HashSet<uint>();
    var variants = new List<WildAnimalSpeciesVariant>(VariantCount);
    var previousVariantAddress = CheckedAdd(address, HeaderSize - 1, name);
    foreach (var index in Enumerable.Range(0, VariantCount)) {
      var pointerOffset = checked(FirstVariantPointerOffset + index * sizeof(uint));
      var fieldAddress = CheckedAdd(address, pointerOffset, name);
      var variantAddress = ReadRequiredPointer(
        source,
        fieldAddress,
        ReadUInt32(record, pointerOffset),
        name,
        $"variant {index} pointer");
      if (variantAddress <= previousVariantAddress)
        throw Invalid(name, $"variant {index} pointer is not monotonic");

      var expectedVariantAddress = CheckedAdd(
        address,
        checked(HeaderSize + index * VariantSize),
        name);
      var variantEnd = Convert.ToUInt64(variantAddress) + VariantSize;
      if (variantAddress < CheckedAdd(address, HeaderSize, name) ||
          variantEnd > recordEnd)
        throw Invalid(name, $"variant {index} is outside the WAS record");
      if (variantAddress != expectedVariantAddress)
        throw Invalid(name,
          $"variant {index} pointer does not match the installed fixed layout");

      var variantOffset = checked(HeaderSize + index * VariantSize);
      var modelField = variantAddress;
      var animationDataField = CheckedAdd(variantAddress, sizeof(uint), name);
      expectedReferenceFields.Add(modelField);
      expectedReferenceFields.Add(animationDataField);
      variants.Add(new WildAnimalSpeciesVariant(
        ReadRequiredReference(
          source,
          owner,
          modelField,
          ReadUInt32(record, variantOffset),
          "mdl",
          name,
          $"variant {index} model"),
        ReadRequiredReference(
          source,
          owner,
          animationDataField,
          ReadUInt32(record, variantOffset + sizeof(uint)),
          "wad",
          name,
          $"variant {index} animation data")));
      previousVariantAddress = variantAddress;
    }
    ValidateOwnedModelReferences(name, owner, expectedReferenceFields, source);

    return new WildAnimalSpeciesDefinition(
      name,
      packagePath,
      variants.AsReadOnly());
  }

  private static string ParseTaggedReference(string reference) {
    var separator = reference.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals("was", StringComparison.OrdinalIgnoreCase))
      throw new ArgumentException(
        $"'{reference}' is not an exact Name:was reference.", nameof(reference));
    return reference[..separator];
  }

  private static uint ReadRequiredPointer(
    IWildAnimalSpeciesDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string name,
    string description
  ) {
    if (!source.TryGetRelocationSource(fieldAddress, out var target)) {
      if (storedPointer != 0)
        throw Invalid(name, $"{description} contains an unproven pointer {storedPointer}");
      throw Invalid(name, $"{description} is not a relocated pointer");
    }
    if (target == 0 || storedPointer != target)
      throw Invalid(name, $"{description} does not match its relocation target");
    return target;
  }

  private static string ReadRequiredString(
    IWildAnimalSpeciesDataSource source,
    uint fieldAddress,
    uint storedPointer,
    string name,
    string description
  ) {
    var address = ReadRequiredPointer(source, fieldAddress, storedPointer, name, description);
    if (!source.TryGetNullTerminatedStringByteLength(
          address, MaximumStringBytes, out var length) || length == 0)
      throw Invalid(name,
        $"{description} is missing, unterminated, or exceeds {MaximumStringBytes} bytes");
    if (!source.TryReadNullTerminatedString(address, length + 1, out var value) ||
        string.IsNullOrWhiteSpace(value))
      throw Invalid(name, $"{description} changed while it was being decoded");
    return value;
  }

  private static string ReadRequiredReference(
    IWildAnimalSpeciesDataSource source,
    OvlLoaderEntry owner,
    uint fieldAddress,
    uint storedPointer,
    string expectedTag,
    string name,
    string description
  ) {
    if (!source.ResourceReferences.TryGetValue(fieldAddress, out var reference)) {
      if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
        throw Invalid(name, $"{description} contains conflicting direct pointer data");
      throw Invalid(name, $"{description} SymbolRef is missing");
    }
    if (!SameLoader(reference.Owner, owner))
      throw Invalid(name, $"{description} SymbolRef is owned by another loader");
    if (!HasTag(reference.Symbol, expectedTag))
      throw Invalid(name,
        $"{description} SymbolRef '{reference.Symbol}' is not {expectedTag}");
    if (storedPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(name, $"{description} SymbolRef field contains conflicting pointer data");
    return reference.Symbol;
  }

  private static void ValidateOwnedModelReferences(
    string name,
    OvlLoaderEntry owner,
    IReadOnlySet<uint> expectedFields,
    IWildAnimalSpeciesDataSource source
  ) {
    foreach (var reference in source.ResourceReferences) {
      if (!SameLoader(reference.Value.Owner, owner) ||
          !HasTag(reference.Value.Symbol, "mdl") &&
          !HasTag(reference.Value.Symbol, "wad")) continue;
      if (!expectedFields.Contains(reference.Key))
        throw Invalid(name,
          $"loader owns an ambiguous model or animation-data SymbolRef at {reference.Key}");
    }
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static bool HasTag(string key, string tag) =>
    key.EndsWith($":{tag}", StringComparison.OrdinalIgnoreCase);

  private static uint CheckedAdd(uint address, int offset, string name) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(name, "field address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static uint ReadUInt32(byte[] bytes, int offset) =>
    BitConverter.ToUInt32(bytes, offset);

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Wild animal species '{name}' is malformed: {message}.");

  private sealed class OvlWildAnimalSpeciesDataSource : IWildAnimalSpeciesDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyList<OvlLoaderEntry> loaders;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;
    private readonly OvlCommonStringTable stringTable;

    public OvlWildAnimalSpeciesDataSource(Ovl ovl) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the WAS decoder limit " +
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
        pair => (IReadOnlyList<OvlLoaderEntry>)pair.Value);
      ResourceReferences = OvlSymbolReferenceIndex.Create(ovl).References;
      stringTable = new OvlCommonStringTable(ovl);
    }

    public IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }

    public OvlLoaderEntry GetSpeciesLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact was loader-table entry");
      var matches = candidates.Where(entry =>
        entry.Tag.ToFileType() == FileType.WildAnimalSpecies &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact was loader-table entry");
      return matches[0];
    }

    public IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner) {
      if (!ovl.TryResolveRelocation(owner.DataAddress, out var ownerBlock, out _)) return [];

      // TryResolveRelocation returns the exact FileBlock.Data array. Reference identity therefore
      // proves that candidate loaders share the owner's archive block rather than merely having a
      // nearby virtual address. Ovl exposes no block end, so a final loader remains undecodable.
      var result = new List<OvlLoaderEntry>();
      foreach (var entry in loaders.Where(entry => string.Equals(
                 entry.SourcePath, owner.SourcePath, StringComparison.OrdinalIgnoreCase))) {
        if (!ovl.TryResolveRelocation(entry.DataAddress, out var candidateBlock, out _) ||
            !ReferenceEquals(ownerBlock, candidateBlock)) continue;
        result.Add(entry);
      }
      return result.AsReadOnly();
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

internal interface IWildAnimalSpeciesDataSource {
  IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
  IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner);
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryGetNullTerminatedStringByteLength(uint address, int maximumLength, out int length);
  bool TryReadNullTerminatedString(uint address, int maximumLength, out string value);
}
