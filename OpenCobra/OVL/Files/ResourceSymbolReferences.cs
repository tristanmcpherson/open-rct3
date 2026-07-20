// Resource Symbol References
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace OpenCobra.OVL.Files;

/// <summary>A public, relocation-proven SymbolRef with its exact loader owner.</summary>
public sealed record ResourceSymbolReference(
  string Symbol,
  string OwnerTag,
  uint OwnerDataAddress,
  string OwnerSourcePath,
  uint OwnerStructAddress
);

/// <summary>Immutable public projection of one archive's validated SymbolRef index.</summary>
/// <remarks>
/// The underlying index accepts only loader-metadata-proven SymbolRef blocks and validates every
/// record's field, string, and exact same-source loader owner before exposing any entry.
/// </remarks>
public sealed class ResourceSymbolReferenceIndex {
  private readonly OvlSymbolReferenceIndex index;

  private ResourceSymbolReferenceIndex(OvlSymbolReferenceIndex index) {
    this.index = index;
  }

  public static ResourceSymbolReferenceIndex Create(Ovl ovl) =>
    new(OvlSymbolReferenceIndex.Create(ovl ??
      throw new ArgumentNullException(nameof(ovl))));

  public bool TryGetValue(
    uint fieldAddress,
    [MaybeNullWhen(false)] out ResourceSymbolReference reference
  ) {
    if (!index.TryGetValue(fieldAddress, out var resolved)) {
      reference = null;
      return false;
    }

    reference = new ResourceSymbolReference(
      resolved.Symbol,
      resolved.Owner.Tag,
      resolved.Owner.DataAddress,
      resolved.Owner.SourcePath,
      resolved.Owner.StructAddress);
    return true;
  }
}
