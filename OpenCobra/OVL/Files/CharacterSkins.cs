// CharacterSkins
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
//
// Decodes character skin/body-part textures from "prt" (FileType.CharacterSkinPart) records -
// e.g. the SkinBody_*/Body_*/OptionalBody_* symbols in Characters/*/*_Main.ovl. A version-four/five
// prt is a 20-byte record whose exact owning SymbolRefs point to its tex at +8 and mms at +12.
// Neither the prt nor the referenced mms is texture-shaped; the tex target is decoded through the
// same TextureDecoding routines Textures.cs uses.
//
// The prior mms/prt failure documented in .agents/plans/fix/ovl-texture-decoding.md came from
// interpreting the owner records as tex/flic/btbl payloads. Resolution now follows relocation-
// proven SymbolRefs from each exact loader owner and fails closed when their layout is ambiguous.

namespace OpenCobra.OVL.Files;

public static class CharacterSkins {
  private const int PartRecordSize = 20;

  private static readonly ReferencedTextureField[] PartFields = [
    new(8, "tex", true),
    new(12, "mms", false),
  ];

  // Extract all character skin textures referenced by exact prt owners from an OVL.
  public static TextureCollection Extract(Ovl ovl) =>
    ReferencedTextureResolver.Extract(
      ovl,
      "character skin part",
      "prt",
      PartRecordSize,
      PartFields);
}

internal readonly record struct ReferencedTextureField(
  int Offset,
  string Tag,
  bool IsTexture
);

internal interface IReferencedTextureDataSource {
  IReadOnlyList<OvlLoaderEntry> Loaders { get; }
  IReadOnlyCollection<OvlFile> Resources { get; }
  IReadOnlyDictionary<uint, OvlSymbolReference> References { get; }

  bool CanRead(uint address, int length);
  bool TryGetDataPointer(OvlFile file, out uint address);
}

internal static class ReferencedTextureResolver {
  private const int MaximumRecordCount = 1_000_000;

  internal static TextureCollection Extract(
    Ovl ovl,
    string family,
    string ownerTag,
    int recordSize,
    IReadOnlyList<ReferencedTextureField> fields
  ) {
    ArgumentNullException.ThrowIfNull(ovl);

    // Phase 1: prove each serialized owner and resolve only its exact SymbolRef targets.
    var source = new OvlReferencedTextureDataSource(ovl);
    var textureFiles = ResolveLocalTextures(
      ovl.Version, family, ownerTag, recordSize, fields, source);

    // Phase 2: decode the proven local tex targets through the canonical texture decoder.
    using var archiveTextures = Textures.Extract(ovl);
    var textures = new TextureCollection();
    try {
      foreach (var file in textureFiles) {
        var name = file.ToString();
        var matches = archiveTextures.Where(texture =>
          string.Equals(texture.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
          throw Invalid(family,
            $"local target '{name}' decoded {matches.Count} times instead of once");
        textures.Add(matches[0].WithName(name));
      }
      return textures;
    } catch {
      textures.Dispose();
      throw;
    }
  }

  internal static IReadOnlyList<OvlFile> ResolveLocalTextures(
    Version archiveVersion,
    string family,
    string ownerTag,
    int recordSize,
    IReadOnlyList<ReferencedTextureField> fields,
    IReferencedTextureDataSource source
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(family);
    ArgumentException.ThrowIfNullOrWhiteSpace(ownerTag);
    ArgumentNullException.ThrowIfNull(fields);
    ArgumentNullException.ThrowIfNull(source);
    ValidateLayout(family, recordSize, fields);

    var owners = source.Loaders.Where(loader =>
      string.Equals(loader.Tag, ownerTag, StringComparison.OrdinalIgnoreCase)).ToList();
    if (owners.Count == 0) return [];
    if (owners.Count > MaximumRecordCount)
      throw Invalid(family,
        $"record count {owners.Count} exceeds the decoder limit {MaximumRecordCount}");
    if (archiveVersion is not (Version.Four or Version.Five))
      throw Invalid(family,
        $"archive version {archiveVersion} is unsupported; proven layouts are version four/five");

    var textures = new List<OvlFile>();
    var seenTextures = new HashSet<OvlFile>();
    foreach (var owner in owners) {
      _ = CheckedAdd(owner.DataAddress, recordSize - 1, family);
      if (!source.CanRead(owner.DataAddress, recordSize))
        throw Invalid(family,
          $"{ownerTag} record at {owner.DataAddress} is outside the archive or truncated");

      var ownedReferences = source.References.Where(pair =>
        SameLoader(pair.Value.Owner, owner)).ToList();
      if (ownedReferences.Count != fields.Count)
        throw Invalid(family,
          $"{ownerTag} record at {owner.DataAddress} owns {ownedReferences.Count} SymbolRefs " +
          $"instead of {fields.Count}");

      foreach (var field in fields) {
        var fieldAddress = CheckedAdd(owner.DataAddress, field.Offset, family);
        if (!source.References.TryGetValue(fieldAddress, out var reference) ||
            !SameLoader(reference.Owner, owner))
          throw Invalid(family,
            $"{ownerTag} record at {owner.DataAddress} is missing its owned +{field.Offset} " +
            $"SymbolRef");

        var (targetName, targetTag) = ParseReference(reference.Symbol, family);
        if (!string.Equals(targetTag, field.Tag, StringComparison.OrdinalIgnoreCase))
          throw Invalid(family,
            $"{ownerTag} record at {owner.DataAddress} references '{reference.Symbol}' at " +
            $"+{field.Offset} instead of a {field.Tag} target");

        var target = ResolveLocalTarget(targetName, field.Tag, family, source);
        if (field.IsTexture && target != null && seenTextures.Add(target))
          textures.Add(target);
      }
    }
    return textures.AsReadOnly();
  }

  private static void ValidateLayout(
    string family,
    int recordSize,
    IReadOnlyList<ReferencedTextureField> fields
  ) {
    if (recordSize <= 0)
      throw Invalid(family, $"record size {recordSize} is invalid");
    if (fields.Count == 0 || fields.Count(field => field.IsTexture) != 1)
      throw Invalid(family, "layout must declare exactly one texture field");
    if (fields.Any(field =>
          field.Offset < 0 ||
          field.Offset > recordSize - sizeof(uint) ||
          string.IsNullOrWhiteSpace(field.Tag)) ||
        fields.Select(field => field.Offset).Distinct().Count() != fields.Count)
      throw Invalid(family, "layout contains an invalid or duplicate SymbolRef field");
  }

  private static OvlFile? ResolveLocalTarget(
    string name,
    string tag,
    string family,
    IReferencedTextureDataSource source
  ) {
    var type = tag.ToFileType();
    if (type == FileType.Unknown)
      throw Invalid(family, $"target tag '{tag}' is not a classified resource type");
    var matches = source.Resources.Where(file =>
      file.Type == type &&
      string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count == 0) return null;
    if (matches.Count != 1)
      throw Invalid(family,
        $"reference '{name}:{tag}' resolves to {matches.Count} local resources instead of one");

    var target = matches[0];
    if (!source.TryGetDataPointer(target, out var address))
      throw Invalid(family, $"local target '{target}' has no relocated data address");
    var loaders = source.Loaders.Where(loader =>
      loader.DataAddress == address &&
      string.Equals(loader.Tag, tag, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(loader.SourcePath, target.Path, StringComparison.OrdinalIgnoreCase)).ToList();
    if (loaders.Count != 1)
      throw Invalid(family,
        $"local target '{target}' has {loaders.Count} exact loader owners instead of one");
    return target;
  }

  private static (string Name, string Tag) ParseReference(string symbol, string family) {
    var separator = symbol.LastIndexOf(':');
    if (separator <= 0 || separator == symbol.Length - 1)
      throw Invalid(family, $"SymbolRef '{symbol}' is not a tagged reference");
    return (symbol[..separator], symbol[(separator + 1)..]);
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.StructAddress == right.StructAddress &&
    left.DataAddress == right.DataAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static uint CheckedAdd(uint address, int offset, string family) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(family, $"record address {address} exceeds the address space");
    return Convert.ToUInt32(result);
  }

  private static InvalidDataException Invalid(string family, string reason) =>
    new($"Invalid {family} texture reference layout: {reason}.");

  private sealed class OvlReferencedTextureDataSource : IReferencedTextureDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyCollection<OvlFile> resources;

    public OvlReferencedTextureDataSource(Ovl ovl) {
      this.ovl = ovl;
      resources = [.. ovl.Keys];
      References = OvlSymbolReferenceIndex.Create(ovl).References;
    }

    public IReadOnlyList<OvlLoaderEntry> Loaders => ovl.LoaderEntriesInOrder;
    public IReadOnlyCollection<OvlFile> Resources => resources;
    public IReadOnlyDictionary<uint, OvlSymbolReference> References { get; }

    public bool CanRead(uint address, int length) =>
      ovl.TryReadBytes(address, length, out _);

    public bool TryGetDataPointer(OvlFile file, out uint address) =>
      ovl.TryGetDataPointer(file, out address);
  }
}
