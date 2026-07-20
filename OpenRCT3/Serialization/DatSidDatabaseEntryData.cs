// DAT SID Database Entry Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>One raw <c>SIDDatabaseEntry</c> DAT entry.</summary>
internal sealed class DatSidDatabaseEntryData : DatSceneryEntryData {
  public bool IsAvailable { get; }
  public bool IsHidden { get; }
  public bool? IsInvented { get; }
  public string OverlayFilename { get; }
  public string SymbolName { get; }

  public DatSidDatabaseEntryData(
    ulong entryId,
    bool isAvailable,
    bool isHidden,
    bool? isInvented,
    string overlayFilename,
    string symbolName
  ) : base(entryId) {
    ArgumentNullException.ThrowIfNull(overlayFilename);
    ArgumentNullException.ThrowIfNull(symbolName);

    IsAvailable = isAvailable;
    IsHidden = isHidden;
    IsInvented = isInvented;
    OverlayFilename = overlayFilename;
    SymbolName = symbolName;
  }
}
