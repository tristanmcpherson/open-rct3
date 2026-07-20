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

/// <summary>Decodes installed Complete Edition four-variant <c>was</c> layouts.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> SHA-256
/// <c>1C9316E728D67AAA3BFE36D0A1634F3139582927440A5467C351E4C949B5B21D</c> registers the RCT3
/// loader name <c>WildAnimalSpecies</c> with tag <c>was</c> at VA 0x00F64870. Installed version-five
/// evidence has a 0x40-byte header followed by four variants. Each variant serializes a 0x58-byte
/// prefix, an exact sound-slot count at +0x50, a relocated slot-array pointer at +0x54, and
/// count * 0x2C bytes of sound slots. Elephant has 24 slots (0x478 bytes per variant); Ostrich has
/// 10 slots (0x210 bytes per variant). The decoder requires that serialized count, pointer
/// topology, SymbolRefs, archive version, and loader boundary all agree; record length alone never
/// selects a layout. Unknown counts fail closed.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerCommon.cpp#L50">
/// rct3-importer WAS tag declaration
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/car.h#L278-L281">
/// rct3-importer Elephant ride-car WAS reference
/// </seealso>
public static class WildAnimalSpecies {
  private const int HeaderSize = 0x40;
  private const int VariantPrefixSize = 0x58;
  private const int SoundSlotSize = 0x2C;
  private const int CompactSoundSlotCount = 10;
  private const int ExpandedSoundSlotCount = 24;
  private const int VariantCount = 4;
  private const int PackagePathOffset = 0x14;
  private const int FirstVariantPointerOffset = 0x18;
  private const int SoundSlotCountOffset = 0x50;
  private const int SoundSlotsPointerOffset = 0x54;
  private const int MaximumStringBytes = 4 * 1024;
  private const int MaximumResourceCount = 1_000_000;

  private static readonly WildAnimalSpeciesLayout CompactLayout = CreateLayout(
    "10-slot",
    CompactSoundSlotCount);
  private static readonly WildAnimalSpeciesLayout ExpandedLayout = CreateLayout(
    "24-slot",
    ExpandedSoundSlotCount);

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
    return Decode(file.Name, ovl.Version, owner, source);
  }

  internal static WildAnimalSpeciesDefinition Decode(
    string name,
    Version archiveVersion,
    OvlLoaderEntry owner,
    IWildAnimalSpeciesDataSource source
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.WildAnimalSpecies)
      throw Invalid(name, $"loader type '{owner.Tag}' is not was");
    if (archiveVersion != Version.Five)
      throw Invalid(name,
        $"archive version {archiveVersion} is unsupported; installed WAS evidence is version five");

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
    if (extent < Convert.ToUInt64(CompactLayout.RecordSize) ||
        extent > Convert.ToUInt64(ExpandedLayout.RecordSize))
      throw Invalid(name,
        $"record extent {extent} is outside the bounded installed WAS range " +
        $"{CompactLayout.RecordSize}..{ExpandedLayout.RecordSize}");
    var recordLength = Convert.ToInt32(extent);
    if (!source.TryReadBytes(address, recordLength, out var record) ||
        record.Length != recordLength)
      throw Invalid(name, "record is outside the archive or truncated");

    var layout = SelectLayout(name, address, extent, record, source);

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
        checked(HeaderSize + index * layout.VariantSize),
        name);
      var variantEnd = Convert.ToUInt64(variantAddress) +
        Convert.ToUInt64(layout.VariantSize);
      if (variantAddress < CheckedAdd(address, HeaderSize, name) ||
          variantEnd > recordEnd)
        throw Invalid(name, $"variant {index} is outside the WAS record");
      if (variantAddress != expectedVariantAddress)
        throw Invalid(name,
          $"variant {index} pointer does not match the installed {layout.Name} layout");

      var variantOffset = checked(HeaderSize + index * layout.VariantSize);
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

      var serializedSoundSlotCount = ReadUInt32(
        record,
        checked(variantOffset + SoundSlotCountOffset));
      if (serializedSoundSlotCount != Convert.ToUInt32(layout.SoundSlotCount))
        throw Invalid(name,
          $"variant {index} sound-slot count {serializedSoundSlotCount} does not match " +
          $"the installed {layout.Name} layout");
      var soundSlotsField = CheckedAdd(variantAddress, SoundSlotsPointerOffset, name);
      var soundSlotsAddress = ReadRequiredPointer(
        source,
        soundSlotsField,
        ReadUInt32(record, checked(variantOffset + SoundSlotsPointerOffset)),
        name,
        $"variant {index} sound-slot array");
      var expectedSoundSlotsAddress = CheckedAdd(
        variantAddress,
        VariantPrefixSize,
        name);
      if (soundSlotsAddress != expectedSoundSlotsAddress)
        throw Invalid(name,
          $"variant {index} sound-slot array does not immediately follow its prefix");

      foreach (var slotIndex in Enumerable.Range(0, layout.SoundSlotCount)) {
        var slotOffset = checked(VariantPrefixSize + slotIndex * SoundSlotSize);
        var soundField = CheckedAdd(variantAddress, slotOffset, name);
        expectedReferenceFields.Add(soundField);
        _ = ReadRequiredReference(
          source,
          owner,
          soundField,
          ReadUInt32(record, checked(variantOffset + slotOffset)),
          "snd",
          name,
          $"variant {index} sound slot {slotIndex}");
      }
      previousVariantAddress = variantAddress;
    }
    ValidateOwnedLayoutReferences(name, owner, expectedReferenceFields, source);

    return new WildAnimalSpeciesDefinition(
      name,
      packagePath,
      variants.AsReadOnly());
  }

  private static WildAnimalSpeciesLayout SelectLayout(
    string name,
    uint address,
    ulong extent,
    byte[] record,
    IWildAnimalSpeciesDataSource source
  ) {
    var firstVariantField = CheckedAdd(address, FirstVariantPointerOffset, name);
    var firstVariantAddress = ReadRequiredPointer(
      source,
      firstVariantField,
      ReadUInt32(record, FirstVariantPointerOffset),
      name,
      "variant 0 pointer");
    var expectedFirstVariantAddress = CheckedAdd(address, HeaderSize, name);
    if (firstVariantAddress != expectedFirstVariantAddress)
      throw Invalid(name,
        "variant 0 pointer does not prove the installed 0x40-byte header");

    var soundSlotCount = ReadUInt32(
      record,
      checked(HeaderSize + SoundSlotCountOffset));
    var layout = soundSlotCount switch {
      CompactSoundSlotCount => CompactLayout,
      ExpandedSoundSlotCount => ExpandedLayout,
      _ => throw Invalid(name,
        $"serialized sound-slot count {soundSlotCount} has no proven installed layout"),
    };
    if (extent != Convert.ToUInt64(layout.RecordSize))
      throw Invalid(name,
        $"serialized sound-slot count {soundSlotCount} selects the {layout.Name} " +
        $"record size {layout.RecordSize}, but the loader boundary proves extent {extent}");
    return layout;
  }

  private static WildAnimalSpeciesLayout CreateLayout(string name, int soundSlotCount) {
    var variantSize = checked(VariantPrefixSize + soundSlotCount * SoundSlotSize);
    return new WildAnimalSpeciesLayout(
      name,
      soundSlotCount,
      variantSize,
      checked(HeaderSize + VariantCount * variantSize));
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

  private static void ValidateOwnedLayoutReferences(
    string name,
    OvlLoaderEntry owner,
    IReadOnlySet<uint> expectedFields,
    IWildAnimalSpeciesDataSource source
  ) {
    foreach (var reference in source.ResourceReferences) {
      var isLayoutReference = HasTag(reference.Value.Symbol, "mdl") ||
        HasTag(reference.Value.Symbol, "wad") ||
        HasTag(reference.Value.Symbol, "snd");
      if (!SameLoader(reference.Value.Owner, owner) || !isLayoutReference) continue;
      if (!expectedFields.Contains(reference.Key))
        throw Invalid(name,
          $"loader owns an ambiguous model, animation-data, or sound SymbolRef at " +
          $"{reference.Key}");
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

  private sealed record WildAnimalSpeciesLayout(
    string Name,
    int SoundSlotCount,
    int VariantSize,
    int RecordSize
  );

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
