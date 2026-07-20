// OvlSymbolReferenceIndex
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>A relocation-proven symbol reference and its exact owning loader.</summary>
internal sealed record OvlSymbolReference(string Symbol, OvlLoaderEntry Owner);

/// <summary>
/// Builds a bounded index over the exact SymbolRef blocks identified by loader metadata.
/// </summary>
internal sealed class OvlSymbolReferenceIndex {
  private const int MaximumBlockCount = 1_000_000;
  private const ulong MaximumRecordCount = 1_000_000;
  private const int MaximumSymbolBytes = 4 * 1024;
  private const ulong MaximumDecodedBytes = 256UL * 1024 * 1024;
  private const ulong MaximumDecodedObjects = 2_000_000;

  private readonly IReadOnlyDictionary<uint, OvlSymbolReference> references;

  private OvlSymbolReferenceIndex(IReadOnlyDictionary<uint, OvlSymbolReference> references) {
    this.references = references;
  }

  public IReadOnlyDictionary<uint, OvlSymbolReference> References => references;

  public bool TryGetValue(uint fieldAddress, out OvlSymbolReference reference) =>
    references.TryGetValue(fieldAddress, out reference!);

  public static OvlSymbolReferenceIndex Create(Ovl ovl) {
    ArgumentNullException.ThrowIfNull(ovl);
    if (Convert.ToUInt64(ovl.LoaderEntriesInOrder.Count) > MaximumRecordCount)
      throw new InvalidDataException(
        $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the SymbolRef index limit " +
        $"{MaximumRecordCount}.");
    if (ovl.SymbolReferenceBlocksInOrder.Count > MaximumBlockCount)
      throw new InvalidDataException(
        $"OVL SymbolRef block count exceeds the index limit {MaximumBlockCount}.");

    var budget = new IndexBudget();
    budget.ReserveObjects(
      Convert.ToUInt64(ovl.LoaderEntriesInOrder.Count), "loader owner index");
    var loadersByStructAddress = new Dictionary<uint, OvlLoaderEntry>();
    foreach (var loader in ovl.LoaderEntriesInOrder) {
      if (!loadersByStructAddress.TryAdd(loader.StructAddress, loader))
        throw new InvalidDataException(
          $"OVL loader struct address {loader.StructAddress} is duplicated.");
    }

    var references = new Dictionary<uint, OvlSymbolReference>();
    var symbolStrings = new Dictionary<uint, string>();
    ulong indexedRecordCount = 0;
    foreach (var block in ovl.SymbolReferenceBlocksInOrder) {
      budget.ReserveObjects(1, "SymbolRef block index");
      ValidateBlockMetadata(block);
      if (indexedRecordCount > MaximumRecordCount - block.RecordCount)
        throw new InvalidDataException(
          $"OVL SymbolRef count exceeds the index limit {MaximumRecordCount}.");
      indexedRecordCount += block.RecordCount;

      // Validate the complete relocation-backed record block before allocating or indexing it.
      foreach (var offset in RecordOffsets(block)) {
        var recordAddress = GetRecordAddress(block, offset);
        ValidateRecord(
          ovl, block, recordAddress, loadersByStructAddress);
      }

      foreach (var offset in RecordOffsets(block)) {
        var recordAddress = GetRecordAddress(block, offset);
        ovl.TryGetRelocationSource(recordAddress, out var fieldAddress);
        ovl.TryGetRelocationSource(recordAddress + 4, out var symbolAddress);
        ovl.TryGetRelocationSource(recordAddress + 8, out var loaderAddress);
        var owner = loadersByStructAddress[loaderAddress];
        var symbol = ResolveSymbol(
          ovl, symbolAddress, symbolStrings, budget);
        var reference = new OvlSymbolReference(symbol, owner);
        if (references.TryGetValue(fieldAddress, out var existing) && existing != reference)
          throw new InvalidDataException(
            $"OVL SymbolRef field {fieldAddress} has conflicting targets or owners.");
        if (references.ContainsKey(fieldAddress)) continue;
        budget.ReserveObjects(1, "SymbolRef field index");
        references.Add(fieldAddress, reference);
      }
    }
    return new OvlSymbolReferenceIndex(references);
  }

  private static void ValidateBlockMetadata(OvlBlockEntry block) {
    var expectedStride = block.Version switch {
      Version.One => 12,
      Version.Four or Version.Five => 16,
      _ => throw new InvalidDataException(
        $"OVL SymbolRef block has unsupported version {block.Version}.")
    };
    if (block.RecordStride != expectedStride)
      throw new InvalidDataException(
        $"OVL SymbolRef stride {block.RecordStride} does not match version " +
        $"{block.Version}'s {expectedStride}-byte layout.");
    if (block.RecordCount > MaximumRecordCount)
      throw new InvalidDataException(
        $"OVL SymbolRef count exceeds the index limit {MaximumRecordCount}.");

    var expectedLength = Convert.ToUInt64(block.RecordCount) *
      Convert.ToUInt64(expectedStride);
    if (expectedLength != Convert.ToUInt64(block.Data.Length))
      throw new InvalidDataException(
        $"OVL SymbolRef block size {block.Data.Length} does not match its parsed count " +
        $"{block.RecordCount} and stride {expectedStride}.");
  }

  private static IEnumerable<int> RecordOffsets(OvlBlockEntry block) {
    for (var offset = 0; offset < block.Data.Length; offset += block.RecordStride)
      yield return offset;
  }

  private static uint GetRecordAddress(OvlBlockEntry block, int offset) {
    var address = Convert.ToUInt64(block.Address) + Convert.ToUInt64(offset);
    if (address + 8 > uint.MaxValue)
      throw new InvalidDataException("OVL SymbolRef record address exceeds the address space.");
    return Convert.ToUInt32(address);
  }

  private static void ValidateRecord(
    Ovl ovl,
    OvlBlockEntry block,
    uint recordAddress,
    IReadOnlyDictionary<uint, OvlLoaderEntry> loadersByStructAddress
  ) {
    var hasFieldRelocation =
      ovl.TryGetRelocationSource(recordAddress, out var fieldAddress);
    var hasSymbolRelocation =
      ovl.TryGetRelocationSource(recordAddress + 4, out var symbolAddress);
    var hasOwnerRelocation =
      ovl.TryGetRelocationSource(recordAddress + 8, out var ownerAddress);
    if (!hasOwnerRelocation ||
        ownerAddress == 0 ||
        !loadersByStructAddress.TryGetValue(ownerAddress, out var owner) ||
        !string.Equals(owner.SourcePath, block.SourcePath, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"OVL SymbolRef owner {ownerAddress} at {recordAddress} in '{block.SourcePath}' " +
        "is not an exact loader-table entry.");
    if (!hasFieldRelocation || fieldAddress == 0 ||
        !hasSymbolRelocation ||
        !ovl.TryResolveRelocation(fieldAddress, out _, out _) ||
        !TryGetSymbolStringLocation(
          ovl, symbolAddress, out var symbolBlock, out var symbolStart, out var symbolLength) ||
        Array.IndexOf(
          symbolBlock, Convert.ToByte(':'), symbolStart, symbolLength) < 0)
      throw new InvalidDataException(
        $"OVL SymbolRef record at {recordAddress} has invalid relocation or symbol data.");
  }

  private static string ResolveSymbol(
    Ovl ovl,
    uint address,
    Dictionary<uint, string> symbolStrings,
    IndexBudget budget
  ) {
    if (symbolStrings.TryGetValue(address, out var cached)) return cached;
    if (!TryGetSymbolStringLocation(
          ovl, address, out var block, out var start, out var length))
      throw new InvalidDataException(
        $"OVL SymbolRef string at {address} is missing, unterminated, or too long.");
    budget.ReserveBytes(Convert.ToUInt64(length) + 1, "SymbolRef string");
    budget.ReserveObjects(1, "SymbolRef string cache entry");
    var value = Encoding.ASCII.GetString(block, start, length);
    symbolStrings.Add(address, value);
    return value;
  }

  private static bool TryGetSymbolStringLocation(
    Ovl ovl,
    uint address,
    out byte[] block,
    out int start,
    out int length
  ) {
    if (address != 0) {
      if (!ovl.TryResolveRelocation(address, out block!, out var offset)) {
        start = 0;
        length = 0;
        return false;
      }
      start = Convert.ToInt32(offset);
      return TryGetNullTerminatedByteLength(block, start, out length);
    }

    // Symbol strings may legitimately begin at virtual address zero. The relocation on the
    // SymbolRef field proves pointer intent; resolving address one proves a block starts at zero.
    if (!ovl.TryResolveRelocation(1, out block!, out var offsetAtOne) || offsetAtOne != 1) {
      start = 0;
      length = 0;
      return false;
    }
    start = 0;
    return TryGetNullTerminatedByteLength(block, start, out length);
  }

  private static bool TryGetNullTerminatedByteLength(
    byte[] block,
    int start,
    out int length
  ) {
    if (start < 0 || start >= block.Length) {
      length = 0;
      return false;
    }
    var available = Math.Min(MaximumSymbolBytes, block.Length - start);
    var end = Array.IndexOf(block, Convert.ToByte(0), start, available);
    length = end < 0 ? 0 : end - start;
    return end >= 0;
  }

  private sealed class IndexBudget {
    private ulong decodedBytes;
    private ulong decodedObjects;

    public void ReserveBytes(ulong count, string description) {
      if (count > MaximumDecodedBytes || decodedBytes > MaximumDecodedBytes - count)
        throw new InvalidDataException(
          $"OVL SymbolRef decoded bytes exceed the limit {MaximumDecodedBytes} " +
          $"while indexing {description}.");
      decodedBytes += count;
    }

    public void ReserveObjects(ulong count, string description) {
      if (count > MaximumDecodedObjects || decodedObjects > MaximumDecodedObjects - count)
        throw new InvalidDataException(
          $"OVL SymbolRef decoded objects exceed the limit {MaximumDecodedObjects} " +
          $"while indexing {description}.");
      decodedObjects += count;
    }
  }
}
