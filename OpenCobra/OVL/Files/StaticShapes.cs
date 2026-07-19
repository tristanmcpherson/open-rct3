// StaticShapes
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Numerics;
using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>A decoded 36-byte RCT3 static-shape vertex.</summary>
public sealed record StaticShapeVertex(
  Vector3 Position,
  Vector3 Normal,
  Vector2 TexCoord,
  Vector4 Color
);

/// <summary>A decoded mesh within an RCT3 static-shape resource.</summary>
public sealed record StaticShapeMesh(
  string Name,
  int SupportType,
  string? FtxRef,
  string? TxsRef,
  uint Transparency,
  uint TextureFlags,
  uint Sides,
  IReadOnlyList<StaticShapeVertex> Vertices,
  IReadOnlyList<uint> Indices
) {
  /// <summary>How the on-disk index payload is arranged.</summary>
  public StaticShapeIndexLayout IndexLayout { get; init; }
  /// <summary>The raw count stored in <c>StaticShapeMesh.index_count</c>.</summary>
  public uint StoredIndexCount { get; init; }
  /// <summary>Y-axis triangle order for sorted placement meshes.</summary>
  public IReadOnlyList<uint>? YIndices { get; init; }
  /// <summary>Z-axis triangle order for sorted placement meshes.</summary>
  public IReadOnlyList<uint>? ZIndices { get; init; }
  public int TriangleCount => Indices.Count / 3;
}

/// <summary>The three index encodings emitted by <c>ManagerSHS.cpp</c>.</summary>
public enum StaticShapeIndexLayout {
  TriangleList,
  PlacementTriangleList,
  PlacementAxisStreams
}

/// <summary>An effect attachment point stored on a static shape.</summary>
public sealed record ShapeEffect(string Name, Matrix4x4 Position);

/// <summary>A decoded RCT3 static-shape resource.</summary>
public sealed record StaticShape(
  string Name,
  Vector3 BoundingBoxMin,
  Vector3 BoundingBoxMax,
  IReadOnlyList<StaticShapeMesh> Meshes,
  IReadOnlyList<ShapeEffect> Effects
);

/// <summary>Decodes relocated <c>shs</c> resources without resolving them to renderer types.</summary>
public static class StaticShapes {
  // See staticshape.h, vertex.h, and ManagerSHS.cpp in rct3-importer's libOVLng.
  private const int ShapeSize = 56;
  private const int MeshSize = 40;
  private const int VertexSize = 36;
  private const int MatrixSize = 64;
  private const int PointerSize = 4;
  private const int MaximumArrayBytes = 256 * 1024 * 1024;
  private const int MaximumMeshCount = 16 * 1024;
  private const int MaximumEffectCount = 64 * 1024;
  private const int MaximumEffectNameBytes = 4 * 1024;
  private const int MaximumShapeCount = 64 * 1024;

  /// <summary>Decodes every static-shape resource from the unique half of an OVL pair.</summary>
  public static IReadOnlyList<StaticShape> Extract(Ovl ovl) {
    ArgumentNullException.ThrowIfNull(ovl);

    var source = new OvlStaticShapeDataSource(ovl);
    var shapes = new List<StaticShape>();
    var context = new DecodeContext(StaticShapeDecodeLimits.Default);
    foreach (var file in ovl.Keys.Where(file =>
      file.Type == FileType.StaticShape &&
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))) {
      if (shapes.Count >= MaximumShapeCount)
        throw Invalid(file.Name, $"shape count exceeds the decoder limit {MaximumShapeCount}");
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");

      shapes.Add(Decode(file.Name, address, source, context));
    }
    return shapes;
  }

  internal static StaticShape Decode(string name, uint address, IStaticShapeDataSource source) =>
    Decode(name, address, source, new DecodeContext(StaticShapeDecodeLimits.Default));

  internal static StaticShape Decode(
    string name,
    uint address,
    IStaticShapeDataSource source,
    StaticShapeDecodeLimits limits
  ) => Decode(name, address, source, new DecodeContext(limits));

  private static StaticShape Decode(
    string name,
    uint address,
    IStaticShapeDataSource source,
    DecodeContext context
  ) {
    var header = ReadExact(source, address, ShapeSize, name, "shape header");
    var boundsMin = ReadVector3(header, 0);
    var boundsMax = ReadVector3(header, 12);
    ValidateBounds(name, boundsMin, boundsMax);

    var totalVertexCount = ReadUInt32(header, 24);
    var totalIndexCount = ReadUInt32(header, 28);
    var unsupportedMeshCount = ReadUInt32(header, 32);
    var meshCount = ReadUInt32(header, 36);
    if (meshCount == 0)
      throw Invalid(name, "mesh count is zero");
    if (meshCount > MaximumMeshCount)
      throw Invalid(name, $"mesh count {meshCount} exceeds the decoder limit {MaximumMeshCount}");
    if (unsupportedMeshCount > meshCount)
      throw Invalid(name, "unsupported mesh count exceeds mesh count");

    var effectCount = ReadUInt32(header, 44);
    if (effectCount > MaximumEffectCount)
      throw Invalid(name, $"effect count {effectCount} exceeds the decoder limit {MaximumEffectCount}");
    context.ReserveObjects(1 + Convert.ToUInt64(meshCount) + effectCount, name, "shape, mesh, and effect objects");

    var meshPointersAddress = ReadRequiredPointer(
      source, CheckedAdd(address, 40, name), name, "mesh pointer array");
    var meshPointerBytes = ReadArray(
      source, meshPointersAddress, meshCount, PointerSize, name, "mesh pointer array", context);
    var resourceKeys = source.ResourceKeys;
    var decodedMeshCount = ToCount(meshCount, name, "mesh count");
    var meshes = new List<StaticShapeMesh>(decodedMeshCount);
    var meshAddresses = new HashSet<uint>();
    ulong decodedVertexCount = 0;
    ulong decodedIndexCount = 0;
    uint decodedUnsupportedMeshCount = 0;

    for (var i = 0; i < decodedMeshCount; i++) {
      var meshPointerAddress = CheckedAdd(meshPointersAddress, Convert.ToUInt32(i * PointerSize), name);
      var meshAddress = ReadRequiredPointer(source, meshPointerAddress, name, $"mesh {i} pointer");
      var storedMeshAddress = ReadUInt32(meshPointerBytes, i * PointerSize);
      if (storedMeshAddress != meshAddress)
        throw Invalid(name, $"mesh {i} pointer does not match its relocation target");
      if (!meshAddresses.Add(meshAddress))
        throw Invalid(name, $"mesh {i} aliases an earlier mesh header at {meshAddress}");

      var mesh = ReadMesh(name, address, i, meshAddress, source, resourceKeys, context);
      meshes.Add(mesh);
      decodedVertexCount += Convert.ToUInt64(mesh.Vertices.Count);
      decodedIndexCount += mesh.StoredIndexCount;
      if (mesh.SupportType == -1) decodedUnsupportedMeshCount++;
    }

    if (decodedVertexCount != totalVertexCount)
      throw Invalid(name, $"stored vertex total {totalVertexCount} does not match decoded total {decodedVertexCount}");
    if (decodedIndexCount != totalIndexCount)
      throw Invalid(name, $"stored index total {totalIndexCount} does not match decoded total {decodedIndexCount}");
    if (decodedUnsupportedMeshCount != unsupportedMeshCount)
      throw Invalid(name,
        $"stored unsupported mesh count {unsupportedMeshCount} does not match decoded count " +
        decodedUnsupportedMeshCount);

    var effects = ReadEffects(name, address, header, effectCount, source, context);
    return new StaticShape(name, boundsMin, boundsMax, meshes, effects);
  }

  private static StaticShapeMesh ReadMesh(
    string shapeName,
    uint shapeAddress,
    int meshIndex,
    uint meshAddress,
    IStaticShapeDataSource source,
    IReadOnlyDictionary<uint, string> resourceKeys,
    DecodeContext context
  ) {
    var name = $"{shapeName}/mesh/{meshIndex}";
    var bytes = ReadExact(source, meshAddress, MeshSize, shapeName, $"mesh {meshIndex} header");
    var supportType = BitConverter.ToInt32(bytes, 0);
    if (supportType != -1 && (supportType < 0 || supportType > 15))
      throw Invalid(shapeName, $"mesh {meshIndex} has invalid support type {supportType}");

    var ftxRef = ReadResourceReference(
      shapeName, shapeAddress, meshIndex, "ftx", CheckedAdd(meshAddress, 4, shapeName),
      ReadUInt32(bytes, 4), source, resourceKeys);
    var txsRef = ReadResourceReference(
      shapeName, shapeAddress, meshIndex, "txs", CheckedAdd(meshAddress, 8, shapeName),
      ReadUInt32(bytes, 8), source, resourceKeys);
    var transparency = ReadUInt32(bytes, 12);
    if (transparency > 2)
      throw Invalid(shapeName, $"mesh {meshIndex} has invalid transparency value {transparency}");

    var textureFlags = ReadUInt32(bytes, 16);
    var sides = ReadUInt32(bytes, 20);
    if (sides is not 1 and not 3)
      throw Invalid(shapeName, $"mesh {meshIndex} has invalid side mode {sides}");

    var vertexCount = ReadUInt32(bytes, 24);
    var storedIndexCount = ReadUInt32(bytes, 28);
    if (vertexCount == 0)
      throw Invalid(shapeName, $"mesh {meshIndex} has no vertices");
    if (storedIndexCount == 0)
      throw Invalid(shapeName, $"mesh {meshIndex} has no indices");
    if (transparency == 0 && storedIndexCount % 3 != 0)
      throw Invalid(shapeName,
        $"mesh {meshIndex} index count {storedIndexCount} is not a triangle list");

    var verticesAddress = ReadRequiredPointer(
      source, CheckedAdd(meshAddress, 32, shapeName), shapeName, $"mesh {meshIndex} vertices");
    var indicesAddress = ReadRequiredPointer(
      source, CheckedAdd(meshAddress, 36, shapeName), shapeName, $"mesh {meshIndex} indices");
    var vertices = ReadVertices(
      shapeName, meshIndex, source, verticesAddress, vertexCount, context);
    var indexData = ReadIndices(
      shapeName,
      meshIndex,
      source,
      indicesAddress,
      storedIndexCount,
      vertexCount,
      transparency != 0,
      context);
    return new StaticShapeMesh(
      name, supportType, ftxRef, txsRef, transparency, textureFlags, sides, vertices, indexData.Indices) {
      IndexLayout = indexData.Layout,
      StoredIndexCount = storedIndexCount,
      YIndices = indexData.YIndices,
      ZIndices = indexData.ZIndices
    };
  }

  private static IReadOnlyList<StaticShapeVertex> ReadVertices(
    string shapeName,
    int meshIndex,
    IStaticShapeDataSource source,
    uint address,
    uint count,
    DecodeContext context
  ) {
    if (context.TryGetVertices(address, count, out var cached)) return cached;
    context.ReserveObjects(count, shapeName, $"mesh {meshIndex} vertices");
    var bytes = ReadArray(
      source, address, count, VertexSize, shapeName, $"mesh {meshIndex} vertices", context);
    var vertices = new StaticShapeVertex[ToCount(count, shapeName, $"mesh {meshIndex} vertex count")];
    for (var i = 0; i < vertices.Length; i++) {
      var offset = i * VertexSize;
      var position = ReadVector3(bytes, offset);
      var normal = ReadVector3(bytes, offset + 12);
      var texCoord = new Vector2(BitConverter.ToSingle(bytes, offset + 28), BitConverter.ToSingle(bytes, offset + 32));
      if (!IsFinite(position) || !IsFinite(normal) || !IsFinite(texCoord))
        throw Invalid(shapeName, $"mesh {meshIndex} vertex {i} contains a non-finite value");

      var color = ReadUInt32(bytes, offset + 24);
      vertices[i] = new StaticShapeVertex(
        position,
        normal,
        texCoord,
        // RCT3 stores this packed as BGRA bytes in a little-endian uint.
        new Vector4(
          Convert.ToByte(color >> 16 & 255) / 255.0f,
          Convert.ToByte(color >> 8 & 255) / 255.0f,
          Convert.ToByte(color & 255) / 255.0f,
          Convert.ToByte(color >> 24 & 255) / 255.0f));
    }
    context.AddVertices(address, count, vertices, shapeName, meshIndex);
    return vertices;
  }

  private static StaticShapeIndexData ReadIndices(
    string shapeName,
    int meshIndex,
    IStaticShapeDataSource source,
    uint address,
    uint storedCount,
    uint vertexCount,
    bool placementTextured,
    DecodeContext context
  ) {
    if (context.TryGetIndices(
          address, storedCount, vertexCount, placementTextured, out var cached))
      return cached;

    var physicalCount = placementTextured
      ? CheckedMultiply(storedCount, 3, shapeName, $"mesh {meshIndex} placement index count")
      : storedCount;
    var bytes = ReadArray(
      source, address, physicalCount, sizeof(uint), shapeName, $"mesh {meshIndex} indices", context);
    context.ReserveBytes(
      Convert.ToUInt64(physicalCount) * sizeof(uint), shapeName, $"mesh {meshIndex} decoded indices");
    var indices = new uint[ToCount(physicalCount, shapeName, $"mesh {meshIndex} physical index count")];
    for (var i = 0; i < indices.Length; i++) {
      var index = ReadUInt32(bytes, i * sizeof(uint));
      if (index >= vertexCount)
        throw Invalid(shapeName,
          $"mesh {meshIndex} index {i} references vertex {index}, but only {vertexCount} vertices exist");
      indices[i] = index;
    }

    StaticShapeIndexData decoded;
    var streamLength = ToCount(storedCount, shapeName, $"mesh {meshIndex} stored index count");
    if (!placementTextured) {
      decoded = new StaticShapeIndexData(StaticShapeIndexLayout.TriangleList, indices, null, null);
    } else if (storedCount % 3 == 0 && HasEquivalentTriangleStreams(indices, streamLength, context, shapeName)) {
      context.ReserveBytes(
        Convert.ToUInt64(physicalCount) * sizeof(uint),
        shapeName,
        $"mesh {meshIndex} decoded axis streams");
      decoded = new StaticShapeIndexData(
        StaticShapeIndexLayout.PlacementAxisStreams,
        indices[..streamLength],
        indices[streamLength..(streamLength * 2)],
        indices[(streamLength * 2)..]);
    } else {
      decoded = new StaticShapeIndexData(
        StaticShapeIndexLayout.PlacementTriangleList, indices, null, null);
    }

    context.AddIndices(
      address, storedCount, vertexCount, placementTextured, decoded, shapeName, meshIndex);
    return decoded;
  }

  private static IReadOnlyList<ShapeEffect> ReadEffects(
    string shapeName,
    uint shapeAddress,
    byte[] header,
    uint effectCount,
    IStaticShapeDataSource source,
    DecodeContext context
  ) {
    if (effectCount == 0) {
      if (ReadUInt32(header, 48) != 0 || ReadUInt32(header, 52) != 0)
        throw Invalid(shapeName, "zero effects have non-null effect pointers");
      return [];
    }

    var positionsAddress = ReadRequiredPointer(
      source, CheckedAdd(shapeAddress, 48, shapeName), shapeName, "effect positions");
    var namesAddress = ReadRequiredPointer(
      source, CheckedAdd(shapeAddress, 52, shapeName), shapeName, "effect names");
    var positionBytes = ReadArray(
      source, positionsAddress, effectCount, MatrixSize, shapeName, "effect positions", context);
    var namePointerBytes = ReadArray(
      source, namesAddress, effectCount, PointerSize, shapeName, "effect name pointers", context);
    var effects = new ShapeEffect[ToCount(effectCount, shapeName, "effect count")];

    for (var i = 0; i < effects.Length; i++) {
      var namePointerAddress = CheckedAdd(namesAddress, Convert.ToUInt32(i * PointerSize), shapeName);
      var nameAddress = ReadRequiredPointer(source, namePointerAddress, shapeName, $"effect {i} name");
      if (ReadUInt32(namePointerBytes, i * PointerSize) != nameAddress)
        throw Invalid(shapeName, $"effect {i} name pointer does not match its relocation target");
      if (!source.TryReadNullTerminatedString(
            nameAddress, MaximumEffectNameBytes, out var effectName) || string.IsNullOrEmpty(effectName))
        throw Invalid(shapeName,
          $"effect {i} name is missing, unterminated, or exceeds {MaximumEffectNameBytes} bytes");
      context.ReserveBytes(
        Convert.ToUInt64(Encoding.ASCII.GetByteCount(effectName)) + 1,
        shapeName,
        $"effect {i} name");

      var matrix = ReadMatrix(positionBytes, i * MatrixSize);
      if (!IsFinite(matrix))
        throw Invalid(shapeName, $"effect {i} matrix contains a non-finite value");
      effects[i] = new ShapeEffect(effectName, matrix);
    }
    return effects;
  }

  private static string? ReadResourceReference(
    string shapeName,
    uint shapeAddress,
    int meshIndex,
    string tag,
    uint fieldAddress,
    uint rawValue,
    IStaticShapeDataSource source,
    IReadOnlyDictionary<uint, string> resourceKeys
  ) {
    // ManagerSHS stores these fields as null and emits a SymbolRefStruct naming the linker target.
    if (source.ResourceReferences.TryGetValue(fieldAddress, out var symbolReference)) {
      if (rawValue != 0)
        throw Invalid(shapeName,
          $"mesh {meshIndex} {tag} has conflicting raw and SymbolRef values");
      if (symbolReference.OwnerAddress != shapeAddress)
        throw Invalid(shapeName,
          $"mesh {meshIndex} {tag} SymbolRef belongs to another loader");
      if (!HasTag(symbolReference.Symbol, tag))
        throw Invalid(shapeName,
          $"mesh {meshIndex} {tag} SymbolRef targets '{symbolReference.Symbol}'");
      return symbolReference.Symbol;
    }

    // Some already-linked archives may instead carry a direct relocated resource pointer.
    if (rawValue == 0) return null;
    if (!source.TryGetRelocationSource(fieldAddress, out var target) || target != rawValue)
      throw Invalid(shapeName, $"mesh {meshIndex} {tag} reference is not a valid relocation");
    if (!resourceKeys.TryGetValue(target, out var key))
      throw Invalid(shapeName, $"mesh {meshIndex} {tag} reference target {target} is not a known resource");
    if (!HasTag(key, tag))
      throw Invalid(shapeName,
        $"mesh {meshIndex} {tag} reference target '{key}' has the wrong resource type");
    return key;
  }

  private static bool HasTag(string key, string tag) =>
    key.EndsWith($":{tag}", StringComparison.OrdinalIgnoreCase);

  private static byte[] ReadArray(
    IStaticShapeDataSource source,
    uint address,
    uint count,
    int stride,
    string shapeName,
    string description,
    DecodeContext context
  ) {
    var length = ByteCount(count, stride, shapeName, description);
    context.ReserveBytes(Convert.ToUInt64(length), shapeName, description);
    return ReadExact(source, address, length, shapeName, description);
  }

  private static byte[] ReadExact(
    IStaticShapeDataSource source,
    uint address,
    int length,
    string shapeName,
    string description
  ) {
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(shapeName, $"{description} is outside the archive or truncated");
    return bytes;
  }

  private static uint ReadRequiredPointer(
    IStaticShapeDataSource source,
    uint sourceAddress,
    string shapeName,
    string description
  ) {
    if (!source.TryGetRelocationSource(sourceAddress, out var target) || target == 0)
      throw Invalid(shapeName, $"{description} is not a non-null relocated pointer");
    return target;
  }

  private static int ByteCount(uint count, int stride, string shapeName, string description) {
    var length = Convert.ToUInt64(count) * Convert.ToUInt64(stride);
    if (length == 0 || length > MaximumArrayBytes)
      throw Invalid(shapeName, $"{description} byte length {length} is outside the decoder limit");
    return Convert.ToInt32(length);
  }

  private static int ToCount(uint count, string shapeName, string description) {
    if (count > int.MaxValue)
      throw Invalid(shapeName, $"{description} {count} exceeds the decoder limit");
    return Convert.ToInt32(count);
  }

  private static uint CheckedMultiply(uint value, uint multiplier, string shapeName, string description) {
    var result = Convert.ToUInt64(value) * multiplier;
    if (result > uint.MaxValue)
      throw Invalid(shapeName, $"{description} overflowed the OVL count range");
    return Convert.ToUInt32(result);
  }

  private static uint CheckedAdd(uint address, uint offset, string shapeName) {
    var result = Convert.ToUInt64(address) + offset;
    if (result > uint.MaxValue)
      throw Invalid(shapeName, "pointer address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static bool HasEquivalentTriangleStreams(
    uint[] indices,
    int streamLength,
    DecodeContext context,
    string shapeName
  ) {
    var triangleCount = streamLength / 3;
    var workingBytes = Convert.ToUInt64(triangleCount) * 3 * 12;
    context.ReserveBytes(workingBytes, shapeName, "placement index layout validation");
    var streams = new TriangleKey[3][];

    for (var stream = 0; stream < streams.Length; stream++) {
      var triangles = new TriangleKey[triangleCount];
      var streamOffset = stream * streamLength;
      for (var triangle = 0; triangle < triangleCount; triangle++) {
        var offset = streamOffset + triangle * 3;
        triangles[triangle] = new TriangleKey(
          indices[offset], indices[offset + 1], indices[offset + 2]);
      }
      Array.Sort(triangles);
      streams[stream] = triangles;
    }

    return streams[0].SequenceEqual(streams[1]) && streams[0].SequenceEqual(streams[2]);
  }

  private static uint ReadUInt32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);

  private static Vector3 ReadVector3(byte[] bytes, int offset) => new(
    BitConverter.ToSingle(bytes, offset),
    BitConverter.ToSingle(bytes, offset + 4),
    BitConverter.ToSingle(bytes, offset + 8));

  private static Matrix4x4 ReadMatrix(byte[] bytes, int offset) => new(
    BitConverter.ToSingle(bytes, offset),
    BitConverter.ToSingle(bytes, offset + 4),
    BitConverter.ToSingle(bytes, offset + 8),
    BitConverter.ToSingle(bytes, offset + 12),
    BitConverter.ToSingle(bytes, offset + 16),
    BitConverter.ToSingle(bytes, offset + 20),
    BitConverter.ToSingle(bytes, offset + 24),
    BitConverter.ToSingle(bytes, offset + 28),
    BitConverter.ToSingle(bytes, offset + 32),
    BitConverter.ToSingle(bytes, offset + 36),
    BitConverter.ToSingle(bytes, offset + 40),
    BitConverter.ToSingle(bytes, offset + 44),
    BitConverter.ToSingle(bytes, offset + 48),
    BitConverter.ToSingle(bytes, offset + 52),
    BitConverter.ToSingle(bytes, offset + 56),
    BitConverter.ToSingle(bytes, offset + 60));

  private static void ValidateBounds(string name, Vector3 min, Vector3 max) {
    if (!IsFinite(min) || !IsFinite(max))
      throw Invalid(name, "bounding box contains a non-finite value");
    if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
      throw Invalid(name, "bounding box minimum exceeds its maximum");
  }

  private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Static shape '{name}' is malformed: {message}.");

  private readonly record struct TriangleKey(uint A, uint B, uint C) : IComparable<TriangleKey> {
    public int CompareTo(TriangleKey other) {
      var a = A.CompareTo(other.A);
      if (a != 0) return a;
      var b = B.CompareTo(other.B);
      return b != 0 ? b : C.CompareTo(other.C);
    }
  }

  private sealed record StaticShapeIndexData(
    StaticShapeIndexLayout Layout,
    IReadOnlyList<uint> Indices,
    IReadOnlyList<uint>? YIndices,
    IReadOnlyList<uint>? ZIndices
  );

  private sealed class DecodeContext(StaticShapeDecodeLimits limits) {
    private readonly Dictionary<uint, (uint Count, IReadOnlyList<StaticShapeVertex> Vertices)> vertices = [];
    private readonly Dictionary<uint, IndexCacheEntry> indices = [];
    private ulong decodedBytes;
    private ulong decodedObjects;

    public void ReserveBytes(ulong count, string shapeName, string description) {
      if (count > limits.MaximumBytes || decodedBytes > limits.MaximumBytes - count)
        throw Invalid(shapeName,
          $"aggregate decode bytes exceed the limit {limits.MaximumBytes} while reading {description}");
      decodedBytes += count;
    }

    public void ReserveObjects(ulong count, string shapeName, string description) {
      if (count > limits.MaximumObjects || decodedObjects > limits.MaximumObjects - count)
        throw Invalid(shapeName,
          $"aggregate decoded objects exceed the limit {limits.MaximumObjects} while reading {description}");
      decodedObjects += count;
    }

    public bool TryGetVertices(
      uint address,
      uint count,
      out IReadOnlyList<StaticShapeVertex> value
    ) {
      if (!vertices.TryGetValue(address, out var cached)) {
        value = [];
        return false;
      }
      if (cached.Count != count)
        throw new InvalidDataException(
          $"Aliased SHS vertex data at {address} has conflicting counts {cached.Count} and {count}.");
      value = cached.Vertices;
      return true;
    }

    public void AddVertices(
      uint address,
      uint count,
      IReadOnlyList<StaticShapeVertex> value,
      string shapeName,
      int meshIndex
    ) {
      if (!vertices.TryAdd(address, (count, value)))
        throw Invalid(shapeName, $"mesh {meshIndex} vertex alias changed during decoding");
    }

    public bool TryGetIndices(
      uint address,
      uint storedCount,
      uint vertexCount,
      bool placementTextured,
      out StaticShapeIndexData value
    ) {
      if (!indices.TryGetValue(address, out var cached)) {
        value = null!;
        return false;
      }
      if (cached.StoredCount != storedCount || cached.VertexCount != vertexCount ||
          cached.PlacementTextured != placementTextured)
        throw new InvalidDataException(
          $"Aliased SHS index data at {address} has conflicting layout or count metadata.");
      value = cached.Data;
      return true;
    }

    public void AddIndices(
      uint address,
      uint storedCount,
      uint vertexCount,
      bool placementTextured,
      StaticShapeIndexData value,
      string shapeName,
      int meshIndex
    ) {
      var entry = new IndexCacheEntry(storedCount, vertexCount, placementTextured, value);
      if (!indices.TryAdd(address, entry))
        throw Invalid(shapeName, $"mesh {meshIndex} index alias changed during decoding");
    }

    private sealed record IndexCacheEntry(
      uint StoredCount,
      uint VertexCount,
      bool PlacementTextured,
      StaticShapeIndexData Data
    );
  }

  private sealed class OvlStaticShapeDataSource : IStaticShapeDataSource {
    private const int MaximumBlockWalk = 4096;
    private readonly Ovl ovl;

    public OvlStaticShapeDataSource(Ovl ovl) {
      this.ovl = ovl;
      var resources = ovl.Keys
        .Select(file => (File: file, HasAddress: ovl.TryGetDataPointer(file, out var address), Address: address))
        .Where(entry => entry.HasAddress)
        .ToList();
      ResourceKeys = resources
        .GroupBy(entry => entry.Address)
        .ToDictionary(
          group => group.Key,
          group => StableResourceKey(group.First().File));
      ResourceReferences = ReadResourceReferences(
        ovl,
        resources.Where(entry => entry.File.Type == FileType.StaticShape)
          .Select(entry => entry.Address),
        resources.Select(entry => entry.Address));
    }

    public IReadOnlyDictionary<uint, string> ResourceKeys { get; }
    public IReadOnlyDictionary<uint, StaticShapeResourceReference> ResourceReferences { get; }

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

    public bool TryReadNullTerminatedString(uint address, int maximumLength, out string value) {
      if (!ovl.TryResolveRelocation(address, out var block, out var offset)) {
        value = string.Empty;
        return false;
      }

      var start = Convert.ToInt32(offset);
      var available = Math.Min(maximumLength, block.Length - start);
      var end = Array.IndexOf(block, Convert.ToByte(0), start, available);
      if (end < 0) {
        value = string.Empty;
        return false;
      }
      value = Encoding.ASCII.GetString(block, start, end - start);
      return true;
    }

    private static string StableResourceKey(OvlFile file) {
      if (file.Type == FileType.Unknown && file.Name.Contains(':')) return file.Name;
      return $"{file.Name}:{file.Type.ToTagString()}";
    }

    private static IReadOnlyDictionary<uint, StaticShapeResourceReference> ReadResourceReferences(
      Ovl ovl,
      IEnumerable<uint> resourceAddresses,
      IEnumerable<uint> knownResourceAddresses
    ) {
      var references = new Dictionary<uint, StaticShapeResourceReference>();
      var visitedBlocks = new HashSet<uint>();
      var stride = ovl.Version == Version.One ? 12 : 16;
      var owners = resourceAddresses.ToHashSet();
      var knownOwners = knownResourceAddresses.ToHashSet();

      foreach (var resourceAddress in owners) {
        if (!ovl.TryResolveRelocation(resourceAddress, out _, out var resourceOffset)) continue;
        var blockAddress = resourceAddress - resourceOffset;

        for (var walked = 0; walked < MaximumBlockWalk && blockAddress > 0; walked++) {
          var previousAddress = blockAddress - 1;
          if (!ovl.TryResolveRelocation(previousAddress, out var block, out var offset)) break;
          var previousBlockAddress = previousAddress - offset;
          if (previousBlockAddress >= blockAddress) break;
          blockAddress = previousBlockAddress;
          if (!visitedBlocks.Add(blockAddress)) continue;

          if (!TryReadSymbolReferenceBlock(
                ovl, blockAddress, block, stride, owners, knownOwners, out var blockReferences))
            continue;
          foreach (var reference in blockReferences) {
            if (references.TryGetValue(reference.Key, out var existing) && existing != reference.Value)
              throw new InvalidDataException(
                $"SHS SymbolRef field {reference.Key} has conflicting targets or owners.");
            references[reference.Key] = reference.Value;
          }
        }
      }
      return references;
    }

    private static bool TryReadSymbolReferenceBlock(
      Ovl ovl,
      uint blockAddress,
      byte[] block,
      int stride,
      IReadOnlySet<uint> shapeOwners,
      IReadOnlySet<uint> knownOwners,
      out IReadOnlyDictionary<uint, StaticShapeResourceReference> references
    ) {
      var decoded = new Dictionary<uint, StaticShapeResourceReference>();
      if (block.Length == 0 || block.Length % stride != 0) {
        references = decoded;
        return false;
      }

      var decodedRecordCount = 0;
      for (var offset = 0; offset < block.Length; offset += stride) {
        var recordAddressValue = Convert.ToUInt64(blockAddress) + Convert.ToUInt64(offset);
        if (recordAddressValue + 8 > uint.MaxValue) break;
        var recordAddress = Convert.ToUInt32(recordAddressValue);
        if (!ovl.TryGetRelocationSource(recordAddress + 8, out var loaderAddress) || loaderAddress == 0 ||
            !ovl.TryReadBytes(loaderAddress, 20, out var loader) ||
            !ovl.TryGetRelocationSource(loaderAddress + 4, out var ownerAddress) ||
            ReadUInt32(loader, 4) != ownerAddress || !knownOwners.Contains(ownerAddress))
          continue;
        if (!ovl.TryGetRelocationSource(recordAddress, out var referenceAddress) || referenceAddress == 0 ||
            !ovl.TryGetRelocationSource(recordAddress + 4, out var symbolAddress) ||
            !ovl.TryReadBytes(referenceAddress, PointerSize, out _) ||
            !TryResolveSymbolString(ovl, symbolAddress, out var symbol) || !symbol.Contains(':')) {
          continue;
        }
        decodedRecordCount++;
        if (shapeOwners.Contains(ownerAddress)) {
          var reference = new StaticShapeResourceReference(symbol, ownerAddress);
          if (decoded.TryGetValue(referenceAddress, out var existing) && existing != reference)
            throw new InvalidDataException(
              $"SHS SymbolRef field {referenceAddress} has conflicting targets or owners.");
          decoded[referenceAddress] = reference;
        }
      }

      references = decoded;
      // A real SymbolRefStruct block has relocation-backed pointer triples throughout. Requiring
      // every record to validate prevents arbitrary geometry blocks from being mistaken for refs.
      return decodedRecordCount == block.Length / stride;
    }

    private static bool TryResolveSymbolString(Ovl ovl, uint address, out string value) {
      if (address != 0) {
        if (ovl.TryResolveString(address, out var resolved)) {
          value = resolved;
          return true;
        }
        value = string.Empty;
        return false;
      }

      // Symbol strings may legitimately begin at virtual address zero. The relocation on the
      // SymbolRef field proves pointer intent; resolving address one proves a block starts at zero.
      if (!ovl.TryResolveRelocation(1, out var block, out var offset) || offset != 1) {
        value = string.Empty;
        return false;
      }
      var end = Array.IndexOf(block, Convert.ToByte(0));
      if (end < 0) {
        value = string.Empty;
        return false;
      }
      value = Encoding.ASCII.GetString(block, 0, end);
      return true;
    }
  }
}

internal sealed record StaticShapeResourceReference(string Symbol, uint OwnerAddress);

internal readonly record struct StaticShapeDecodeLimits(ulong MaximumBytes, ulong MaximumObjects) {
  public static StaticShapeDecodeLimits Default { get; } = new(256UL * 1024 * 1024, 1_000_000);
}

internal interface IStaticShapeDataSource {
  IReadOnlyDictionary<uint, string> ResourceKeys { get; }
  IReadOnlyDictionary<uint, StaticShapeResourceReference> ResourceReferences { get; }
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryReadNullTerminatedString(uint address, int maximumLength, out string value);
}
