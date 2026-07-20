// Models
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenCobra.OVL.Files;

/// <summary>Evidence-backed neutral fields from one installed <c>mdl</c> resource.</summary>
public sealed record ModelDefinition(
  string Name,
  string SourcePath,
  uint DataAddress,
  uint LoaderStructAddress,
  uint Count0,
  uint Count1,
  uint Count2,
  uint Count3
);

/// <summary>Decodes the proven count prefix of the installed two-chunk <c>mdl</c> layout.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> SHA-256
/// <c>1C9316E728D67AAA3BFE36D0A1634F3139582927440A5467C351E4C949B5B21D</c> registers the
/// <c>Model</c>/<c>mdl</c> loader at VA <c>0x00F1A490</c>. Its load hook at
/// <c>0x00F1A3F0</c> calls the hydrator at <c>0x00F18C10</c>. That routine materializes a
/// 0x40-byte runtime header from a serialized extra-data chunk whose first 16 bytes are four
/// unsigned counts; serialized variable data starts at +0x10, its first Count0 region has a 0x60
/// stride, and a 0x2C metadata region follows. None of the four counts are assigned guessed names.
///
/// The relocatable loader record is separate evidence. Installed AdultElephant proves an exact
/// common-half owner, a relocated loader data field, a 0x50-byte record with no internal
/// relocations, and exactly two owner-bound extra-data chunks. This decoder fails closed on any
/// other shape and leaves all payload after the proven prefix opaque.
/// </remarks>
public static class Models {
  private const int LoaderRecordSize = 0x50;
  private const int SerializedCountSize = 0x10;
  private const int FirstRegionStride = 0x60;
  private const int MetadataSize = 0x2C;
  private const int InstalledExtraChunkCount = 2;
  private const uint MaximumNeutralCount = 1_000_000;
  private const int MaximumResourceCount = 1_000_000;

  /// <summary>Decodes one exact, case-insensitively matched <c>Name:mdl</c> reference.</summary>
  public static ModelDefinition Extract(Ovl ovl, string taggedReference) {
    ArgumentNullException.ThrowIfNull(ovl);
    ArgumentException.ThrowIfNullOrWhiteSpace(taggedReference);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the MDL decoder limit " +
        $"{MaximumResourceCount}.");

    var name = ParseTaggedReference(taggedReference);
    var matches = ovl.Keys.Where(file =>
      file.Type == FileType.Model &&
      string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1)
      throw Invalid(name, $"reference resolves to {matches.Count} Model resources instead of one");

    var file = matches[0];
    if (!ovl.TryGetDataPointer(file, out var address))
      throw Invalid(name, "resource data pointer is missing");

    var source = new OvlModelDataSource(ovl);
    var owner = source.GetModelLoader(file, address);
    return Decode(file.Name, owner, source);
  }

  internal static ModelDefinition Decode(
    string name,
    OvlLoaderEntry owner,
    IModelDataSource source
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.Model)
      throw Invalid(name, $"loader type '{owner.Tag}' is not mdl");
    if (string.IsNullOrWhiteSpace(owner.SourcePath))
      throw Invalid(name, "loader source path is missing");

    var regionLoaders = source.GetDataRegionLoaders(owner);
    if (regionLoaders.Count(loader => SameLoader(loader, owner)) != 1)
      throw Invalid(name, "loader is not present exactly once in its proven data region");

    var dataFieldAddress = CheckedAdd(owner.StructAddress, sizeof(uint), name);
    if (!source.TryGetRelocationSource(dataFieldAddress, out var relocatedDataAddress) ||
        relocatedDataAddress != owner.DataAddress)
      throw Invalid(name, "loader data field is not relocated to its exact data address");

    var nextLoader = regionLoaders
      .Where(loader => loader.DataAddress > owner.DataAddress)
      .OrderBy(loader => loader.DataAddress)
      .FirstOrDefault();
    if (nextLoader == null)
      throw Invalid(name,
        "record is the final loader in its proven data region and its block end is unavailable");
    var extent = Convert.ToUInt64(nextLoader.DataAddress) -
      Convert.ToUInt64(owner.DataAddress);
    if (extent != LoaderRecordSize)
      throw Invalid(name,
        $"record extent {extent} does not match the installed size {LoaderRecordSize}");
    if (!source.TryReadBytes(owner.DataAddress, LoaderRecordSize, out var record) ||
        record.Length != LoaderRecordSize)
      throw Invalid(name, "loader record is outside the archive or truncated");

    foreach (var offset in Enumerable.Range(0, LoaderRecordSize)) {
      var fieldAddress = CheckedAdd(owner.DataAddress, offset, name);
      if (source.TryGetRelocationSource(fieldAddress, out _))
        throw Invalid(name, $"loader record contains an unproven relocation at +0x{offset:X}");
    }

    if (!source.TryReadExtraData(owner, out var chunks))
      throw Invalid(name, "loader has no owner-bound extra data");
    if (chunks.Count != InstalledExtraChunkCount)
      throw Invalid(name,
        $"loader has {chunks.Count} extra-data chunks instead of {InstalledExtraChunkCount}");
    var serialized = chunks[0];
    if (serialized.Length < SerializedCountSize)
      throw Invalid(name, "first extra-data chunk is shorter than the four-count prefix");
    if (chunks[1].Length == 0)
      throw Invalid(name, "second extra-data chunk is empty");

    var counts = Enumerable.Range(0, 4)
      .Select(index => BitConverter.ToUInt32(serialized, index * sizeof(uint)))
      .ToArray();
    foreach (var indexed in counts.Select((value, index) => (value, index))) {
      if (indexed.value > MaximumNeutralCount)
        throw Invalid(name,
          $"Count{indexed.index} {indexed.value} exceeds the decoder limit " +
          $"{MaximumNeutralCount}");
    }

    var requiredFirstRegionBytes = Convert.ToUInt64(SerializedCountSize) +
      Convert.ToUInt64(counts[0]) * Convert.ToUInt64(FirstRegionStride) + MetadataSize;
    if (requiredFirstRegionBytes > Convert.ToUInt64(serialized.Length))
      throw Invalid(name,
        "first extra-data chunk cannot contain its Count0 region and metadata");

    return new ModelDefinition(
      name,
      owner.SourcePath,
      owner.DataAddress,
      owner.StructAddress,
      counts[0],
      counts[1],
      counts[2],
      counts[3]);
  }

  private static string ParseTaggedReference(string reference) {
    var separator = reference.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals("mdl", StringComparison.OrdinalIgnoreCase))
      throw new ArgumentException(
        $"'{reference}' is not an exact Name:mdl reference.", nameof(reference));
    return reference[..separator];
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static uint CheckedAdd(uint address, int offset, string name) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(name, "field address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Model '{name}' is malformed: {message}.");

  private sealed class OvlModelDataSource : IModelDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyList<OvlLoaderEntry> loaders;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;

    public OvlModelDataSource(Ovl ovl) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the MDL decoder limit " +
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
    }

    public OvlLoaderEntry GetModelLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact mdl loader-table entry");
      var matches = candidates.Where(entry =>
        entry.Tag.ToFileType() == FileType.Model &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact mdl loader-table entry");
      return matches[0];
    }

    public IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner) {
      if (!ovl.TryResolveRelocation(owner.DataAddress, out var ownerBlock, out _)) return [];

      // TryResolveRelocation returns the exact FileBlock.Data array. Reference identity and an exact
      // source path prove a shared archive data region instead of merely adjacent virtual addresses.
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

    public bool TryReadExtraData(
      OvlLoaderEntry owner,
      out IReadOnlyList<byte[]> chunks
    ) {
      if (ovl.TryReadExtraData(owner.DataAddress, out var resolved)) {
        chunks = resolved;
        return true;
      }
      chunks = [];
      return false;
    }
  }
}

internal interface IModelDataSource {
  IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner);
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryReadExtraData(OvlLoaderEntry owner, out IReadOnlyList<byte[]> chunks);
}
