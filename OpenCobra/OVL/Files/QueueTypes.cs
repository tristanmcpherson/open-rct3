// Queue Types
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenCobra.OVL.Files;

/// <summary>A decoded RCT3 queue-path definition.</summary>
public sealed record QueueType(
  string Name,
  string InternalName,
  string DisplayNameRef,
  string IconRef,
  string FlexiTextureRef,
  string Straight,
  string TurnLeft,
  string TurnRight,
  string SlopeUp,
  string SlopeDown,
  string SlopeStraight1,
  string SlopeStraight2,
  IReadOnlyList<PathResearchCategory> ResearchCategories
);

/// <summary>Decodes relocation-backed <c>qtd</c> resources.</summary>
/// <remarks>
/// The 32-bit layout and field order are ported directly from
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/path.h">path.h</see>
/// and
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerQTD.cpp">ManagerQTD.cpp</see>.
/// </remarks>
public static class QueueTypes {
  internal const int RecordSize = 52;

  /// <summary>Extracts every queue-path definition from an OVL pair.</summary>
  public static IReadOnlyList<QueueType> Extract(Ovl ovl) =>
    Extract(ovl, PathResourceDecodeLimits.Default);

  internal static IReadOnlyList<QueueType> Extract(
    Ovl ovl,
    PathResourceDecodeLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(ovl);
    var context = new PathResourceDecodeContext(limits);
    var files = PathResourceDecoder.Files(
      ovl, FileType.QueueType, "QTD", context);
    if (files.Count == 0) return [];

    var source = new OvlPathResourceDataSource(ovl, context);
    var queues = new List<QueueType>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      var address = PathResourceDecoder.RequiredAddress(ovl, file, "QTD");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier QTD at {address}");
      var owner = source.GetLoader(file, address, FileType.QueueType, "QTD");
      queues.Add(Decode(file.Name, owner, source, context));
    }
    return queues;
  }

  internal static QueueType Decode(
    string name,
    OvlLoaderEntry owner,
    IPathResourceDataSource source
  ) => Decode(
    name,
    owner,
    source,
    new PathResourceDecodeContext(PathResourceDecodeLimits.Default));

  internal static QueueType Decode(
    string name,
    OvlLoaderEntry owner,
    IPathResourceDataSource source,
    PathResourceDecodeLimits limits
  ) => Decode(name, owner, source, new PathResourceDecodeContext(limits));

  internal static QueueType Decode(
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
      source, owner, FileType.QueueType, name, "QTD");
    var address = owner.DataAddress;
    var bytes = PathResourceDecoder.ReadExact(
      source, address, RecordSize, name, "QTD record", context);
    context.ReserveObjects(1, name, "QTD model");

    return new QueueType(
      name,
      RequiredString(source, bytes, address, 0, name, "internal name", context),
      PathResourceDecoder.ReadRequiredSymbol(
        source, owner, bytes, address, 4, name, "display name", "txt"),
      PathResourceDecoder.ReadRequiredSymbol(
        source, owner, bytes, address, 8, name, "icon", "gsi"),
      PathResourceDecoder.ReadRequiredSymbol(
        source, owner, bytes, address, 12, name, "flexi texture", "ftx"),
      RequiredString(source, bytes, address, 16, name, "straight owner", context),
      RequiredString(source, bytes, address, 20, name, "left-turn owner", context),
      RequiredString(source, bytes, address, 24, name, "right-turn owner", context),
      RequiredString(source, bytes, address, 28, name, "slope-up owner", context),
      RequiredString(source, bytes, address, 32, name, "slope-down owner", context),
      RequiredString(source, bytes, address, 36, name, "slope-straight owner 1", context),
      RequiredString(source, bytes, address, 40, name, "slope-straight owner 2", context),
      PathResourceDecoder.ReadResearchCategories(
        source, bytes, address, 44, 48, name, context));
  }

  private static string RequiredString(
    IPathResourceDataSource source,
    byte[] bytes,
    uint address,
    int offset,
    string name,
    string description,
    PathResourceDecodeContext context
  ) => PathResourceDecoder.ReadRequiredString(
    source, bytes, address, offset, name, description, allowEmpty: false, context: context);

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Queue type '{name}' is malformed: {message}.");
}
