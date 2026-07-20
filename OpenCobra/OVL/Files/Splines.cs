// Splines
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenCobra.OVL.Files;

/// <summary>One exact <c>SplineNode</c> control record.</summary>
/// <param name="Position">The node position.</param>
/// <param name="PreviousControlOffset">
/// Control point 1, relative to <paramref name="Position"/> and pointing toward the previous node.
/// </param>
/// <param name="NextControlOffset">
/// Control point 2, relative to <paramref name="Position"/> and pointing toward the next node.
/// </param>
public sealed record SplineNode(
  Vector3 Position,
  Vector3 PreviousControlOffset,
  Vector3 NextControlOffset
);

/// <summary>One spline segment's serialized length and exact 14-byte travel table.</summary>
public sealed record SplineSegment(float Length, IReadOnlyList<byte> TravelData);

/// <summary>A relocated RCT3 <c>spl</c> resource.</summary>
public sealed record Spline(
  string Name,
  bool Cyclic,
  float TotalLength,
  float InverseTotalLength,
  float MaximumY,
  IReadOnlyList<SplineNode> Nodes,
  IReadOnlyList<SplineSegment> Segments
);

/// <summary>Decodes exact relocated <c>spl</c> resources.</summary>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/spline.h">
/// rct3-importer spline layout
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerSPL.cpp">
/// rct3-importer spline serializer
/// </seealso>
public static class Splines {
  private const int HeaderSize = 32;
  private const int NodeSize = 36;
  private const int TravelDataSize = 14;
  private const int MaximumResourceCount = 1_000_000;
  private const uint MaximumSplineCount = 1_000_000;
  private const uint MaximumNodeCount = 1_000_000;

  /// <summary>Extracts every spline serialized in the common half of an OVL pair.</summary>
  public static IReadOnlyList<Spline> Extract(Ovl ovl) =>
    Extract(ovl, SplineDecodeLimits.Default);

  internal static IReadOnlyList<Spline> Extract(Ovl ovl, SplineDecodeLimits limits) {
    ArgumentNullException.ThrowIfNull(ovl);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the SPL decoder limit {MaximumResourceCount}.");

    var context = new DecodeContext(limits);
    var files = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file =>
      file.Type == FileType.Spline &&
      file.Path.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase))) {
      if (Convert.ToUInt32(files.Count) >= MaximumSplineCount)
        throw Invalid(file.Name,
          $"spline count exceeds the decoder limit {MaximumSplineCount}");
      context.ReserveObjects(1, file.Name, "spline resource index");
      files.Add(file);
    }

    var source = new OvlSplineDataSource(ovl, context);
    var splines = new List<Spline>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier SPL at {address}");
      var owner = source.GetSplineLoader(file, address);
      splines.Add(Decode(file.Name, owner, source, context));
    }
    return splines;
  }

  internal static Spline Decode(
    string name,
    OvlLoaderEntry owner,
    ISplineDataSource source
  ) => Decode(name, owner, source, new DecodeContext(SplineDecodeLimits.Default));

  internal static Spline Decode(
    string name,
    OvlLoaderEntry owner,
    ISplineDataSource source,
    SplineDecodeLimits limits
  ) => Decode(name, owner, source, new DecodeContext(limits));

  private static Spline Decode(
    string name,
    OvlLoaderEntry owner,
    ISplineDataSource source,
    DecodeContext context
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.Spline)
      throw Invalid(name, $"loader type '{owner.Tag}' is not spl");

    var address = owner.DataAddress;
    var header = ReadExact(source, address, HeaderSize, name, "header", context);
    var nodeCount = ReadUInt32(header, 0);
    if (nodeCount == 0) throw Invalid(name, "node count is zero");
    if (nodeCount > MaximumNodeCount)
      throw Invalid(name, $"node count {nodeCount} exceeds the limit {MaximumNodeCount}");

    var cyclicFlag = ReadUInt32(header, 8);
    if (cyclicFlag > 1) throw Invalid(name, $"cyclic flag {cyclicFlag} is not 0 or 1");
    var cyclic = cyclicFlag == 1;
    var segmentCount = cyclic ? nodeCount : nodeCount - 1;
    var totalLength = ReadFiniteSingle(header, 12, name, "total length");
    var inverseTotalLength = ReadFiniteSingle(header, 16, name, "inverse total length");
    var maximumY = ReadFiniteSingle(header, 28, name, "maximum Y");

    var nodeByteCount = CheckedByteCount(nodeCount, NodeSize, name, "node array");
    var lengthByteCount = CheckedByteCount(segmentCount, sizeof(float), name, "length array");
    var travelByteCount =
      CheckedByteCount(segmentCount, TravelDataSize, name, "travel-data array");
    // The retained model owns a Spline, two top-level arrays, one record per node, and both a
    // SplineSegment plus its distinct travel-data array per segment. Reserve the complete object
    // graph before allocating any of it so a large cyclic spline cannot exceed the advertised
    // aggregate object budget through its per-segment byte arrays.
    context.ReserveObjects(
      Convert.ToUInt64(nodeCount) + (Convert.ToUInt64(segmentCount) * 2) + 3,
      name,
      "spline model, arrays, nodes, segments, and travel data");

    var nodesAddress = ReadPointer(
      source,
      CheckedAdd(address, 4, name),
      ReadUInt32(header, 4),
      allowZero: false,
      name,
      "node array");
    var lengthsAddress = ReadPointer(
      source,
      CheckedAdd(address, 20, name),
      ReadUInt32(header, 20),
      allowZero: segmentCount == 0,
      name,
      "length array");
    var travelAddress = ReadPointer(
      source,
      CheckedAdd(address, 24, name),
      ReadUInt32(header, 24),
      allowZero: segmentCount == 0,
      name,
      "travel-data array");

    var nodeBytes = ReadExact(
      source, nodesAddress, nodeByteCount, name, "node array", context);
    ValidateNodes(nodeBytes, nodeCount, name);
    var lengthBytes = ReadExact(
      source, lengthsAddress, lengthByteCount, name, "length array", context);
    ValidateLengths(lengthBytes, segmentCount, name);
    var travelBytes = ReadExact(
      source, travelAddress, travelByteCount, name, "travel-data array", context);

    var nodes = new SplineNode[Convert.ToInt32(nodeCount)];
    foreach (var index in Enumerable.Range(0, nodes.Length)) {
      var offset = index * NodeSize;
      nodes[index] = new SplineNode(
        ReadVector3(nodeBytes, offset),
        ReadVector3(nodeBytes, offset + 12),
        ReadVector3(nodeBytes, offset + 24));
    }

    var segments = new SplineSegment[Convert.ToInt32(segmentCount)];
    foreach (var index in Enumerable.Range(0, segments.Length)) {
      segments[index] = new SplineSegment(
        ReadSingle(lengthBytes, index * sizeof(float)),
        travelBytes.AsSpan(index * TravelDataSize, TravelDataSize).ToArray());
    }

    return new Spline(
      name,
      cyclic,
      totalLength,
      inverseTotalLength,
      maximumY,
      nodes,
      segments);
  }

  private static void ValidateNodes(byte[] bytes, uint count, string name) {
    foreach (var nodeIndex in Enumerable.Range(0, Convert.ToInt32(count))) {
      var offset = nodeIndex * NodeSize;
      foreach (var componentIndex in Enumerable.Range(0, 9)) {
        var value = ReadSingle(bytes, offset + (componentIndex * sizeof(float)));
        if (!float.IsFinite(value))
          throw Invalid(name,
            $"node {nodeIndex} component {componentIndex} is not finite");
      }
    }
  }

  private static void ValidateLengths(byte[] bytes, uint count, string name) {
    foreach (var index in Enumerable.Range(0, Convert.ToInt32(count))) {
      var value = ReadSingle(bytes, index * sizeof(float));
      if (!float.IsFinite(value))
        throw Invalid(name, $"segment {index} length is not finite");
    }
  }

  private static uint ReadPointer(
    ISplineDataSource source,
    uint fieldAddress,
    uint storedPointer,
    bool allowZero,
    string name,
    string description
  ) {
    if (!source.TryGetRelocationSource(fieldAddress, out var target))
      throw Invalid(name, $"{description} is not a relocated pointer");
    if (target != storedPointer)
      throw Invalid(name, $"{description} does not match its relocation target");
    if (!allowZero && target == 0)
      throw Invalid(name, $"{description} relocation target is zero");
    return target;
  }

  private static byte[] ReadExact(
    ISplineDataSource source,
    uint address,
    int length,
    string name,
    string description,
    DecodeContext context
  ) {
    context.ReserveBytes(Convert.ToUInt64(length), name, description);
    if (length == 0) return [];
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(name, $"{description} is truncated or outside relocated data");
    return bytes;
  }

  private static int CheckedByteCount(
    uint count,
    int stride,
    string name,
    string description
  ) {
    var bytes = Convert.ToUInt64(count) * Convert.ToUInt64(stride);
    if (bytes > int.MaxValue)
      throw Invalid(name, $"{description} byte count exceeds the addressable range");
    return Convert.ToInt32(bytes);
  }

  private static uint CheckedAdd(uint address, int offset, string name) {
    try {
      return checked(address + Convert.ToUInt32(offset));
    } catch (OverflowException exception) {
      throw new InvalidDataException(
        $"Spline '{name}' is malformed: pointer-field address overflow.", exception);
    }
  }

  private static uint ReadUInt32(byte[] bytes, int offset) =>
    BitConverter.ToUInt32(bytes, offset);

  private static float ReadSingle(byte[] bytes, int offset) =>
    BitConverter.ToSingle(bytes, offset);

  private static float ReadFiniteSingle(
    byte[] bytes,
    int offset,
    string name,
    string description
  ) {
    var value = ReadSingle(bytes, offset);
    if (!float.IsFinite(value)) throw Invalid(name, $"{description} is not finite");
    return value;
  }

  private static Vector3 ReadVector3(byte[] bytes, int offset) => new(
    ReadSingle(bytes, offset),
    ReadSingle(bytes, offset + 4),
    ReadSingle(bytes, offset + 8));

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Spline '{name}' is malformed: {message}.");

  private sealed class DecodeContext(SplineDecodeLimits limits) {
    private ulong decodedBytes;
    private ulong decodedObjects;

    public void ReserveBytes(ulong count, string splineName, string description) {
      if (count > limits.MaximumBytes || decodedBytes > limits.MaximumBytes - count)
        throw Invalid(splineName,
          $"aggregate decode bytes exceed the limit {limits.MaximumBytes} while reading " +
          description);
      decodedBytes += count;
    }

    public void ReserveObjects(ulong count, string splineName, string description) {
      if (count > limits.MaximumObjects || decodedObjects > limits.MaximumObjects - count)
        throw Invalid(splineName,
          $"aggregate decoded objects exceed the limit {limits.MaximumObjects} while reading " +
          description);
      decodedObjects += count;
    }
  }

  private sealed class OvlSplineDataSource : ISplineDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;

    public OvlSplineDataSource(Ovl ovl, DecodeContext context) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the SPL decoder limit " +
          $"{MaximumResourceCount}.");

      context.ReserveObjects(
        OvlLoaderIndexBudget.Calculate(ovl.LoaderEntriesInOrder.Count),
        "OVL",
        "loader metadata index");
      var mutableLoaders = new Dictionary<uint, List<OvlLoaderEntry>>();
      foreach (var entry in ovl.LoaderEntriesInOrder) {
        if (!mutableLoaders.TryGetValue(entry.DataAddress, out var entries))
          mutableLoaders.Add(entry.DataAddress, entries = []);
        entries.Add(entry);
      }
      loadersByDataAddress = mutableLoaders.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<OvlLoaderEntry>)pair.Value);
    }

    public OvlLoaderEntry GetSplineLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact spl loader-table entry");
      var matches = candidates.Where(entry =>
        entry.Tag.ToFileType() == FileType.Spline &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact spl loader-table entry");
      return matches[0];
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
  }
}

internal readonly record struct SplineDecodeLimits(ulong MaximumBytes, ulong MaximumObjects) {
  public static SplineDecodeLimits Default { get; } =
    new(512UL * 1024 * 1024, 4_000_000);
}

internal interface ISplineDataSource {
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
}
