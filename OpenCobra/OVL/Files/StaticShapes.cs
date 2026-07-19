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
  public int TriangleCount => Indices.Count / 3;
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

  /// <summary>Decodes every static-shape resource from the unique half of an OVL pair.</summary>
  public static IReadOnlyList<StaticShape> Extract(Ovl ovl) {
    ArgumentNullException.ThrowIfNull(ovl);

    var source = new OvlStaticShapeDataSource(ovl);
    var shapes = new List<StaticShape>();
    foreach (var file in ovl.Keys.Where(file =>
      file.Type == FileType.StaticShape &&
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");

      shapes.Add(Decode(file.Name, address, source));
    }
    return shapes;
  }

  internal static StaticShape Decode(string name, uint address, IStaticShapeDataSource source) {
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
    if (unsupportedMeshCount > meshCount)
      throw Invalid(name, "unsupported mesh count exceeds mesh count");

    var meshPointersAddress = ReadRequiredPointer(
      source, CheckedAdd(address, 40, name), name, "mesh pointer array");
    var meshPointerBytes = ReadArray(source, meshPointersAddress, meshCount, PointerSize, name, "mesh pointer array");
    var resourceKeys = source.ResourceKeys;
    var decodedMeshCount = ToCount(meshCount, name, "mesh count");
    var meshes = new List<StaticShapeMesh>(decodedMeshCount);
    ulong decodedVertexCount = 0;
    ulong decodedIndexCount = 0;
    uint decodedUnsupportedMeshCount = 0;

    for (var i = 0; i < decodedMeshCount; i++) {
      var meshPointerAddress = CheckedAdd(meshPointersAddress, Convert.ToUInt32(i * PointerSize), name);
      var meshAddress = ReadRequiredPointer(source, meshPointerAddress, name, $"mesh {i} pointer");
      var storedMeshAddress = ReadUInt32(meshPointerBytes, i * PointerSize);
      if (storedMeshAddress != meshAddress)
        throw Invalid(name, $"mesh {i} pointer does not match its relocation target");

      var mesh = ReadMesh(name, i, meshAddress, source, resourceKeys);
      meshes.Add(mesh);
      decodedVertexCount += Convert.ToUInt64(mesh.Vertices.Count);
      decodedIndexCount += Convert.ToUInt64(mesh.Indices.Count);
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

    var effectCount = ReadUInt32(header, 44);
    var effects = ReadEffects(name, address, header, effectCount, source);
    return new StaticShape(name, boundsMin, boundsMax, meshes, effects);
  }

  private static StaticShapeMesh ReadMesh(
    string shapeName,
    int meshIndex,
    uint meshAddress,
    IStaticShapeDataSource source,
    IReadOnlyDictionary<uint, string> resourceKeys
  ) {
    var name = $"{shapeName}/mesh/{meshIndex}";
    var bytes = ReadExact(source, meshAddress, MeshSize, shapeName, $"mesh {meshIndex} header");
    var supportType = BitConverter.ToInt32(bytes, 0);
    if (supportType != -1 && (supportType < 0 || supportType > 15))
      throw Invalid(shapeName, $"mesh {meshIndex} has invalid support type {supportType}");

    var ftxRef = ReadResourceReference(
      shapeName, meshIndex, "ftx", CheckedAdd(meshAddress, 4, shapeName),
      ReadUInt32(bytes, 4), source, resourceKeys);
    var txsRef = ReadResourceReference(
      shapeName, meshIndex, "txs", CheckedAdd(meshAddress, 8, shapeName),
      ReadUInt32(bytes, 8), source, resourceKeys);
    var transparency = ReadUInt32(bytes, 12);
    if (transparency > 2)
      throw Invalid(shapeName, $"mesh {meshIndex} has invalid transparency value {transparency}");

    var textureFlags = ReadUInt32(bytes, 16);
    var sides = ReadUInt32(bytes, 20);
    if (sides is not 1 and not 3)
      throw Invalid(shapeName, $"mesh {meshIndex} has invalid side mode {sides}");

    var vertexCount = ReadUInt32(bytes, 24);
    var indexCount = ReadUInt32(bytes, 28);
    if (vertexCount == 0)
      throw Invalid(shapeName, $"mesh {meshIndex} has no vertices");
    if (indexCount == 0 || indexCount % 3 != 0)
      throw Invalid(shapeName, $"mesh {meshIndex} index count {indexCount} is not a non-zero triangle list");

    var verticesAddress = ReadRequiredPointer(
      source, CheckedAdd(meshAddress, 32, shapeName), shapeName, $"mesh {meshIndex} vertices");
    var indicesAddress = ReadRequiredPointer(
      source, CheckedAdd(meshAddress, 36, shapeName), shapeName, $"mesh {meshIndex} indices");
    var vertexBytes = ReadArray(
      source, verticesAddress, vertexCount, VertexSize, shapeName, $"mesh {meshIndex} vertices");
    var indexBytes = ReadArray(
      source, indicesAddress, indexCount, sizeof(uint), shapeName, $"mesh {meshIndex} indices");

    var vertices = ReadVertices(shapeName, meshIndex, vertexBytes, vertexCount);
    var indices = ReadIndices(shapeName, meshIndex, indexBytes, indexCount, vertexCount);
    return new StaticShapeMesh(
      name, supportType, ftxRef, txsRef, transparency, textureFlags, sides, vertices, indices);
  }

  private static IReadOnlyList<StaticShapeVertex> ReadVertices(
    string shapeName,
    int meshIndex,
    byte[] bytes,
    uint count
  ) {
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
    return vertices;
  }

  private static IReadOnlyList<uint> ReadIndices(
    string shapeName,
    int meshIndex,
    byte[] bytes,
    uint count,
    uint vertexCount
  ) {
    var indices = new uint[ToCount(count, shapeName, $"mesh {meshIndex} index count")];
    for (var i = 0; i < indices.Length; i++) {
      var index = ReadUInt32(bytes, i * sizeof(uint));
      if (index >= vertexCount)
        throw Invalid(shapeName,
          $"mesh {meshIndex} index {i} references vertex {index}, but only {vertexCount} vertices exist");
      indices[i] = index;
    }
    return indices;
  }

  private static IReadOnlyList<ShapeEffect> ReadEffects(
    string shapeName,
    uint shapeAddress,
    byte[] header,
    uint effectCount,
    IStaticShapeDataSource source
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
    var positionBytes = ReadArray(source, positionsAddress, effectCount, MatrixSize, shapeName, "effect positions");
    var namePointerBytes = ReadArray(source, namesAddress, effectCount, PointerSize, shapeName, "effect name pointers");
    var effects = new ShapeEffect[ToCount(effectCount, shapeName, "effect count")];

    for (var i = 0; i < effects.Length; i++) {
      var namePointerAddress = CheckedAdd(namesAddress, Convert.ToUInt32(i * PointerSize), shapeName);
      var nameAddress = ReadRequiredPointer(source, namePointerAddress, shapeName, $"effect {i} name");
      if (ReadUInt32(namePointerBytes, i * PointerSize) != nameAddress)
        throw Invalid(shapeName, $"effect {i} name pointer does not match its relocation target");
      if (!source.TryReadNullTerminatedString(nameAddress, out var effectName) || string.IsNullOrEmpty(effectName))
        throw Invalid(shapeName, $"effect {i} name is missing or unterminated");

      var matrix = ReadMatrix(positionBytes, i * MatrixSize);
      if (!IsFinite(matrix))
        throw Invalid(shapeName, $"effect {i} matrix contains a non-finite value");
      effects[i] = new ShapeEffect(effectName, matrix);
    }
    return effects;
  }

  private static string? ReadResourceReference(
    string shapeName,
    int meshIndex,
    string tag,
    uint fieldAddress,
    uint rawValue,
    IStaticShapeDataSource source,
    IReadOnlyDictionary<uint, string> resourceKeys
  ) {
    // ManagerSHS stores these fields as null and emits a SymbolRefStruct naming the linker target.
    if (source.ResourceReferences.TryGetValue(fieldAddress, out var symbolReference))
      return symbolReference;

    // Some already-linked archives may instead carry a direct relocated resource pointer.
    if (rawValue == 0) return null;
    if (!source.TryGetRelocationSource(fieldAddress, out var target) || target != rawValue)
      throw Invalid(shapeName, $"mesh {meshIndex} {tag} reference is not a valid relocation");
    if (!resourceKeys.TryGetValue(target, out var key))
      throw Invalid(shapeName, $"mesh {meshIndex} {tag} reference target {target} is not a known resource");
    return key;
  }

  private static byte[] ReadArray(
    IStaticShapeDataSource source,
    uint address,
    uint count,
    int stride,
    string shapeName,
    string description
  ) {
    var length = ByteCount(count, stride, shapeName, description);
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

  private static uint CheckedAdd(uint address, uint offset, string shapeName) {
    var result = Convert.ToUInt64(address) + offset;
    if (result > uint.MaxValue)
      throw Invalid(shapeName, "pointer address overflowed the OVL address space");
    return Convert.ToUInt32(result);
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
          .Select(entry => entry.Address));
    }

    public IReadOnlyDictionary<uint, string> ResourceKeys { get; }
    public IReadOnlyDictionary<uint, string> ResourceReferences { get; }

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

    public bool TryReadNullTerminatedString(uint address, out string value) {
      if (!ovl.TryResolveRelocation(address, out var block, out var offset)) {
        value = string.Empty;
        return false;
      }

      var start = Convert.ToInt32(offset);
      var end = Array.IndexOf(block, Convert.ToByte(0), start);
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

    private static IReadOnlyDictionary<uint, string> ReadResourceReferences(
      Ovl ovl,
      IEnumerable<uint> resourceAddresses
    ) {
      var references = new Dictionary<uint, string>();
      var visitedBlocks = new HashSet<uint>();
      var stride = ovl.Version == Version.One ? 12 : 16;

      foreach (var resourceAddress in resourceAddresses) {
        if (!ovl.TryResolveRelocation(resourceAddress, out _, out var resourceOffset)) continue;
        var blockAddress = resourceAddress - resourceOffset;

        for (var walked = 0; walked < MaximumBlockWalk && blockAddress > 0; walked++) {
          var previousAddress = blockAddress - 1;
          if (!ovl.TryResolveRelocation(previousAddress, out var block, out var offset)) break;
          var previousBlockAddress = previousAddress - offset;
          if (previousBlockAddress >= blockAddress) break;
          blockAddress = previousBlockAddress;
          if (!visitedBlocks.Add(blockAddress)) continue;

          if (TryReadSymbolReferenceBlock(ovl, blockAddress, block, stride, out var blockReferences))
            foreach (var reference in blockReferences)
              references[reference.Key] = reference.Value;
        }
      }
      return references;
    }

    private static bool TryReadSymbolReferenceBlock(
      Ovl ovl,
      uint blockAddress,
      byte[] block,
      int stride,
      out IReadOnlyDictionary<uint, string> references
    ) {
      var decoded = new Dictionary<uint, string>();
      if (block.Length == 0 || block.Length % stride != 0) {
        references = decoded;
        return false;
      }

      for (var offset = 0; offset < block.Length; offset += stride) {
        var recordAddressValue = Convert.ToUInt64(blockAddress) + Convert.ToUInt64(offset);
        if (recordAddressValue + 8 > uint.MaxValue) break;
        var recordAddress = Convert.ToUInt32(recordAddressValue);
        if (!ovl.TryGetRelocationSource(recordAddress, out var referenceAddress) || referenceAddress == 0 ||
            !ovl.TryGetRelocationSource(recordAddress + 4, out var symbolAddress) || symbolAddress == 0 ||
            !ovl.TryGetRelocationSource(recordAddress + 8, out var loaderAddress) || loaderAddress == 0 ||
            !ovl.TryReadBytes(referenceAddress, PointerSize, out _) ||
            !ovl.TryReadBytes(loaderAddress, 20, out _) ||
            !ovl.TryResolveString(symbolAddress, out var symbol) || !symbol.Contains(':')) {
          continue;
        }
        decoded[referenceAddress] = symbol;
      }

      references = decoded;
      // A real SymbolRefStruct block has relocation-backed pointer triples throughout. Permit one
      // unresolved record for a legitimate address-zero string-table entry, but reject sparse
      // accidental matches in arbitrary geometry/data blocks.
      return decoded.Count > 0 && decoded.Count + 1 >= block.Length / stride;
    }
  }
}

internal interface IStaticShapeDataSource {
  IReadOnlyDictionary<uint, string> ResourceKeys { get; }
  IReadOnlyDictionary<uint, string> ResourceReferences { get; }
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryReadNullTerminatedString(uint address, out string value);
}
