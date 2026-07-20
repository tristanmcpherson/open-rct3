// BoneShapes
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Numerics;
using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>The four fixed bone slots serialized with an RCT3 bone-shape vertex.</summary>
public readonly record struct BoneShapeSkinning(
  sbyte Bone0,
  sbyte Bone1,
  sbyte Bone2,
  sbyte Bone3,
  byte Weight0,
  byte Weight1,
  byte Weight2,
  byte Weight3
);

/// <summary>A decoded 44-byte RCT3 bone-shape vertex.</summary>
public sealed record BoneShapeVertex(
  Vector3 Position,
  Vector3 Normal,
  Vector2 TexCoord,
  Vector4 Color,
  BoneShapeSkinning Skinning
);

/// <summary>A decoded mesh within an RCT3 bone-shape resource.</summary>
public sealed record BoneShapeMesh(
  string Name,
  int SupportType,
  string? FtxRef,
  string? TxsRef,
  uint Transparency,
  uint TextureFlags,
  uint Sides,
  IReadOnlyList<BoneShapeVertex> Vertices,
  IReadOnlyList<uint> Indices
) {
  /// <summary>How the on-disk index payload is arranged.</summary>
  public StaticShapeIndexLayout IndexLayout { get; init; }
  /// <summary>The raw count stored in <c>BoneShapeMesh.index_count</c>.</summary>
  public uint StoredIndexCount { get; init; }
  /// <summary>The index count added to <c>BoneShape.total_index_count</c> by libOVLng.</summary>
  public uint LogicalIndexCount { get; init; }
  /// <summary>The three X/Y/Z triangle orders emitted for placement-sorted meshes.</summary>
  public IReadOnlyList<IReadOnlyList<uint>> PlacementSortPermutations { get; init; } = [];
  /// <summary>The triangle count in the retained render order.</summary>
  public int TriangleCount => Indices.Count / 3;
}

/// <summary>A named bone and its two bind/base matrices.</summary>
public sealed record BoneShapeBone(
  string Name,
  int ParentBoneNumber,
  Matrix4x4 Position1,
  Matrix4x4 Position2
);

/// <summary>A decoded RCT3 bone-shape resource.</summary>
public sealed record BoneShape(
  string Name,
  Vector3 BoundingBoxMin,
  Vector3 BoundingBoxMax,
  IReadOnlyList<BoneShapeMesh> Meshes,
  IReadOnlyList<BoneShapeBone> Bones
);

/// <summary>Decodes relocated <c>bsh</c> resources without resolving renderer types.</summary>
public static class BoneShapes {
  // See boneshape.h, vertex.h, ManagerBSH.cpp, and ManagerBSH.h in rct3-importer.
  private const int ShapeSize = 60;
  private const int MeshSize = 40;
  // VERTEX2 is 44 bytes: two VECTORs, four signed bone indices, four weights, BGRA, and UV.
  private const int VertexSize = 44;
  private const int BoneSize = 8;
  private const int MatrixSize = 64;
  private const int PointerSize = 4;
  private const int IndexSize = 2;
  private const int MaximumArrayBytes = 256 * 1024 * 1024;
  private const int MaximumMeshCount = 16 * 1024;
  private const int MaximumBoneCount = 64 * 1024;
  private const int MaximumBoneNameBytes = 4 * 1024;
  private const int MaximumResourceNameBytes = 4 * 1024;
  private const int MaximumShapeCount = 64 * 1024;
  private const int MaximumResourceCount = 1_000_000;

  /// <summary>Decodes every bone-shape resource from the unique half of an OVL pair.</summary>
  public static IReadOnlyList<BoneShape> Extract(Ovl ovl) =>
    Extract(ovl, BoneShapeDecodeLimits.Default);

  internal static IReadOnlyList<BoneShape> Extract(Ovl ovl, BoneShapeDecodeLimits limits) {
    ArgumentNullException.ThrowIfNull(ovl);

    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the BSH decoder limit {MaximumResourceCount}.");
    var context = new DecodeContext(limits);
    var shapeFiles = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file =>
      file.Type == FileType.BoneShape &&
      file.Path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))) {
      if (shapeFiles.Count >= MaximumShapeCount)
        throw Invalid(file.Name, $"shape count exceeds the decoder limit {MaximumShapeCount}");
      context.ReserveObjects(1, file.Name, "shape resource index");
      shapeFiles.Add(file);
    }

    var source = new OvlBoneShapeDataSource(ovl, context);
    var shapes = new List<BoneShape>(shapeFiles.Count);
    foreach (var file in shapeFiles) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      shapes.Add(Decode(file.Name, address, source, context));
    }
    return shapes;
  }

  internal static BoneShape Decode(string name, uint address, IBoneShapeDataSource source) =>
    Decode(name, address, source, new DecodeContext(BoneShapeDecodeLimits.Default));

  internal static BoneShape Decode(
    string name,
    uint address,
    IBoneShapeDataSource source,
    BoneShapeDecodeLimits limits
  ) => Decode(name, address, source, new DecodeContext(limits));

  private static BoneShape Decode(
    string name,
    uint address,
    IBoneShapeDataSource source,
    DecodeContext context
  ) {
    if (!source.BoneShapeLoaderDataAddresses.Contains(address))
      throw Invalid(name, $"address {address} is not owned by a bsh loader-table entry");
    var header = ReadExact(source, address, ShapeSize, name, "shape header", context);
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

    var boneCount = ReadUInt32(header, 44);
    if (boneCount > MaximumBoneCount)
      throw Invalid(name, $"bone count {boneCount} exceeds the decoder limit {MaximumBoneCount}");
    context.ReserveObjects(
      1 + Convert.ToUInt64(meshCount) + boneCount,
      name,
      "shape, mesh, and bone objects");

    var meshPointersAddress = ReadMatchingRequiredPointer(
      source,
      CheckedAdd(address, 40, name),
      ReadUInt32(header, 40),
      name,
      "mesh pointer array");
    var meshPointerBytes = ReadArray(
      source, meshPointersAddress, meshCount, PointerSize, name, "mesh pointer array", context);
    var decodedMeshCount = ToCount(meshCount, name, "mesh count");
    var meshes = new List<BoneShapeMesh>(decodedMeshCount);
    var meshAddresses = new HashSet<uint>();
    ulong decodedVertexCount = 0;
    ulong decodedIndexCount = 0;
    uint decodedUnsupportedMeshCount = 0;

    for (var i = 0; i < decodedMeshCount; i++) {
      var pointerOffset = Convert.ToUInt32(i * PointerSize);
      var meshPointerAddress = CheckedAdd(meshPointersAddress, pointerOffset, name);
      var storedMeshAddress = ReadUInt32(meshPointerBytes, i * PointerSize);
      var meshAddress = ReadMatchingRequiredPointer(
        source, meshPointerAddress, storedMeshAddress, name, $"mesh {i} pointer");
      if (!meshAddresses.Add(meshAddress))
        throw Invalid(name, $"mesh {i} aliases an earlier mesh header at {meshAddress}");

      var mesh = ReadMesh(name, address, i, meshAddress, source, context);
      meshes.Add(mesh);
      decodedVertexCount += Convert.ToUInt64(mesh.Vertices.Count);
      decodedIndexCount += mesh.LogicalIndexCount;
      if (mesh.SupportType == -1) decodedUnsupportedMeshCount++;
    }

    if (decodedVertexCount != totalVertexCount)
      throw Invalid(name,
        $"stored vertex total {totalVertexCount} does not match decoded total {decodedVertexCount}");
    if (decodedIndexCount != totalIndexCount)
      throw Invalid(name,
        $"stored index total {totalIndexCount} does not match decoded total {decodedIndexCount}");
    if (decodedUnsupportedMeshCount != unsupportedMeshCount)
      throw Invalid(name,
        $"stored unsupported mesh count {unsupportedMeshCount} does not match decoded count " +
        decodedUnsupportedMeshCount);

    var bones = ReadBones(name, address, header, boneCount, source, context);
    ValidateSkinning(name, meshes, bones.Count);
    ValidateBoneHierarchy(name, bones, context);
    return new BoneShape(name, boundsMin, boundsMax, meshes, bones);
  }

  private static BoneShapeMesh ReadMesh(
    string shapeName,
    uint shapeAddress,
    int meshIndex,
    uint meshAddress,
    IBoneShapeDataSource source,
    DecodeContext context
  ) {
    var name = $"{shapeName}/mesh/{meshIndex}";
    var bytes = ReadExact(
      source, meshAddress, MeshSize, shapeName, $"mesh {meshIndex} header", context);
    var supportType = BitConverter.ToInt32(bytes, 0);
    if (supportType != -1 && (supportType < 0 || supportType > 15))
      throw Invalid(shapeName, $"mesh {meshIndex} has invalid support type {supportType}");

    var ftxRef = ReadResourceReference(
      shapeName, shapeAddress, meshIndex, "ftx", CheckedAdd(meshAddress, 4, shapeName),
      ReadUInt32(bytes, 4), source);
    var txsRef = ReadResourceReference(
      shapeName, shapeAddress, meshIndex, "txs", CheckedAdd(meshAddress, 8, shapeName),
      ReadUInt32(bytes, 8), source);
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

    var verticesAddress = ReadMatchingRequiredPointer(
      source,
      CheckedAdd(meshAddress, 32, shapeName),
      ReadUInt32(bytes, 32),
      shapeName,
      $"mesh {meshIndex} vertices");
    var indicesAddress = ReadMatchingRequiredPointer(
      source,
      CheckedAdd(meshAddress, 36, shapeName),
      ReadUInt32(bytes, 36),
      shapeName,
      $"mesh {meshIndex} indices");
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
    return new BoneShapeMesh(
      name, supportType, ftxRef, txsRef, transparency, textureFlags, sides,
      vertices, indexData.Indices) {
      IndexLayout = indexData.Layout,
      StoredIndexCount = storedIndexCount,
      LogicalIndexCount = indexData.LogicalCount,
      PlacementSortPermutations = indexData.PlacementSortPermutations
    };
  }

  private static IReadOnlyList<BoneShapeVertex> ReadVertices(
    string shapeName,
    int meshIndex,
    IBoneShapeDataSource source,
    uint address,
    uint count,
    DecodeContext context
  ) {
    if (context.TryGetVertices(address, count, out var cached)) return cached;
    context.ReserveObjects(count, shapeName, $"mesh {meshIndex} vertices");
    var bytes = ReadArray(
      source, address, count, VertexSize, shapeName, $"mesh {meshIndex} vertices", context);
    var vertices = new BoneShapeVertex[ToCount(count, shapeName, $"mesh {meshIndex} vertex count")];
    for (var i = 0; i < vertices.Length; i++) {
      var offset = i * VertexSize;
      var position = ReadVector3(bytes, offset);
      var normal = ReadVector3(bytes, offset + 12);
      var texCoord = new Vector2(
        BitConverter.ToSingle(bytes, offset + 36),
        BitConverter.ToSingle(bytes, offset + 40));
      if (!IsFinite(position) || !IsFinite(normal) || !IsFinite(texCoord))
        throw Invalid(shapeName, $"mesh {meshIndex} vertex {i} contains a non-finite value");

      var color = ReadUInt32(bytes, offset + 32);
      vertices[i] = new BoneShapeVertex(
        position,
        normal,
        texCoord,
        new Vector4(
          Convert.ToByte(color >> 16 & 255) / 255.0f,
          Convert.ToByte(color >> 8 & 255) / 255.0f,
          Convert.ToByte(color & 255) / 255.0f,
          Convert.ToByte(color >> 24 & 255) / 255.0f),
        new BoneShapeSkinning(
          ReadSByte(bytes[offset + 24]),
          ReadSByte(bytes[offset + 25]),
          ReadSByte(bytes[offset + 26]),
          ReadSByte(bytes[offset + 27]),
          bytes[offset + 28],
          bytes[offset + 29],
          bytes[offset + 30],
          bytes[offset + 31]));
    }
    context.AddVertices(address, count, vertices, shapeName, meshIndex);
    return vertices;
  }

  private static BoneShapeIndexData ReadIndices(
    string shapeName,
    int meshIndex,
    IBoneShapeDataSource source,
    uint address,
    uint storedCount,
    uint vertexCount,
    bool placementTextured,
    DecodeContext context
  ) {
    if (context.TryGetIndices(
          address, storedCount, vertexCount, placementTextured, out var cached))
      return cached;

    // ManagerBSH writes either one ordinary order, storedCount*3 no-sort indices where the
    // field stores triangle count, or three storedCount-index X/Y/Z permutations.
    var physicalCount = placementTextured
      ? CheckedMultiply(storedCount, 3, shapeName, $"mesh {meshIndex} placement index count")
      : storedCount;
    context.ReserveBytes(
      Convert.ToUInt64(physicalCount) * sizeof(uint),
      shapeName,
      $"mesh {meshIndex} decoded indices");
    var bytes = ReadArray(
      source, address, physicalCount, IndexSize, shapeName, $"mesh {meshIndex} indices", context);
    var indices = new uint[ToCount(physicalCount, shapeName, $"mesh {meshIndex} physical index count")];
    for (var i = 0; i < indices.Length; i++) {
      var index = Convert.ToUInt32(BitConverter.ToUInt16(bytes, i * IndexSize));
      if (index >= vertexCount)
        throw Invalid(shapeName,
          $"mesh {meshIndex} index {i} references vertex {index}, but only {vertexCount} vertices exist");
      indices[i] = index;
    }

    var layout = placementTextured
      ? StaticShapeIndexLayout.PlacementTriangleList
      : StaticShapeIndexLayout.TriangleList;
    var logicalCount = physicalCount;
    IReadOnlyList<uint> retainedIndices = indices;
    IReadOnlyList<IReadOnlyList<uint>> placementSortPermutations = [];
    if (placementTextured && storedCount % 3 == 0 &&
        HasMatchingPlacementSortPermutations(
          indices, storedCount, shapeName, meshIndex, context)) {
      layout = StaticShapeIndexLayout.PlacementSortedTriangleList;
      logicalCount = storedCount;
      var permutationCount = ToCount(
        storedCount, shapeName, $"mesh {meshIndex} sorted placement index count");
      placementSortPermutations = [
        new ArraySegment<uint>(indices, 0, permutationCount),
        new ArraySegment<uint>(indices, permutationCount, permutationCount),
        new ArraySegment<uint>(indices, permutationCount * 2, permutationCount)
      ];
      retainedIndices = placementSortPermutations[0];
    }

    var decoded = new BoneShapeIndexData(
      layout,
      retainedIndices,
      logicalCount,
      placementSortPermutations);
    context.AddIndices(
      address, storedCount, vertexCount, placementTextured, decoded, shapeName, meshIndex);
    return decoded;
  }

  private static bool HasMatchingPlacementSortPermutations(
    uint[] indices,
    uint storedCount,
    string shapeName,
    int meshIndex,
    DecodeContext context
  ) {
    var permutationIndexCount = ToCount(
      storedCount, shapeName, $"mesh {meshIndex} placement permutation index count");
    if (permutationIndexCount == 0 || permutationIndexCount % 3 != 0) return false;
    if (Convert.ToInt64(permutationIndexCount) * 3 != indices.Length)
      throw Invalid(shapeName,
        $"mesh {meshIndex} placement payload cannot be split into three complete permutations");

    var triangleCount = permutationIndexCount / 3;
    var comparisonBytes = Convert.ToUInt64(triangleCount) * 3 * sizeof(uint) * 2;
    context.ReserveBytes(
      comparisonBytes, shapeName, $"mesh {meshIndex} placement permutation comparison");
    var expected = ReadTriangles(indices, 0, triangleCount);
    var candidate = new BoneShapeTriangle[triangleCount];
    Array.Sort(expected);

    foreach (var offset in new[] { permutationIndexCount, permutationIndexCount * 2 }) {
      ReadTriangles(indices, offset, candidate);
      Array.Sort(candidate);
      if (!expected.AsSpan().SequenceEqual(candidate)) return false;
    }
    return true;
  }

  private static BoneShapeTriangle[] ReadTriangles(
    uint[] indices,
    int offset,
    int triangleCount
  ) {
    var triangles = new BoneShapeTriangle[triangleCount];
    ReadTriangles(indices, offset, triangles);
    return triangles;
  }

  private static void ReadTriangles(
    uint[] indices,
    int offset,
    BoneShapeTriangle[] triangles
  ) {
    for (var triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++) {
      var indexOffset = offset + triangleIndex * 3;
      triangles[triangleIndex] = new BoneShapeTriangle(
        indices[indexOffset], indices[indexOffset + 1], indices[indexOffset + 2]);
    }
  }

  private static IReadOnlyList<BoneShapeBone> ReadBones(
    string shapeName,
    uint shapeAddress,
    byte[] header,
    uint boneCount,
    IBoneShapeDataSource source,
    DecodeContext context
  ) {
    // libOVLng emits relocations for these even when count is zero. Like other counted OVL
    // arrays, zero count makes the pointer fields semantically absent.
    if (boneCount == 0) return [];

    var bonesAddress = ReadMatchingRequiredPointer(
      source,
      CheckedAdd(shapeAddress, 48, shapeName),
      ReadUInt32(header, 48),
      shapeName,
      "bone array");
    var positions1Address = ReadMatchingRequiredPointer(
      source,
      CheckedAdd(shapeAddress, 52, shapeName),
      ReadUInt32(header, 52),
      shapeName,
      "bone position1 array");
    var positions2Address = ReadMatchingRequiredPointer(
      source,
      CheckedAdd(shapeAddress, 56, shapeName),
      ReadUInt32(header, 56),
      shapeName,
      "bone position2 array");
    var boneBytes = ReadArray(
      source, bonesAddress, boneCount, BoneSize, shapeName, "bone array", context);
    var positions1 = ReadArray(
      source, positions1Address, boneCount, MatrixSize, shapeName, "bone position1 array", context);
    var positions2 = ReadArray(
      source, positions2Address, boneCount, MatrixSize, shapeName, "bone position2 array", context);
    var bones = new BoneShapeBone[ToCount(boneCount, shapeName, "bone count")];

    for (var i = 0; i < bones.Length; i++) {
      var boneOffset = i * BoneSize;
      var rawNameAddress = ReadUInt32(boneBytes, boneOffset);
      var nameFieldAddress = CheckedAdd(
        bonesAddress, Convert.ToUInt32(boneOffset), shapeName);
      var nameAddress = ReadMatchingRelocatedPointer(
        source,
        nameFieldAddress,
        rawNameAddress,
        allowZero: true,
        shapeName,
        $"bone {i} name");
      var boneName = ReadBoneName(shapeName, i, nameAddress, source, context);
      var parentBoneNumber = BitConverter.ToInt32(boneBytes, boneOffset + 4);
      if (parentBoneNumber < -1 || parentBoneNumber >= bones.Length)
        throw Invalid(shapeName,
          $"bone {i} parent {parentBoneNumber} is outside the {bones.Length}-bone skeleton");

      var position1 = ReadMatrix(positions1, i * MatrixSize);
      var position2 = ReadMatrix(positions2, i * MatrixSize);
      if (!IsFinite(position1) || !IsFinite(position2))
        throw Invalid(shapeName, $"bone {i} matrix contains a non-finite value");
      bones[i] = new BoneShapeBone(boneName, parentBoneNumber, position1, position2);
    }
    return bones;
  }

  private static string ReadBoneName(
    string shapeName,
    int boneIndex,
    uint address,
    IBoneShapeDataSource source,
    DecodeContext context
  ) {
    if (context.TryGetBoneName(address, out var cached)) return cached;
    if (!source.TryGetNullTerminatedStringByteLength(
          address, MaximumBoneNameBytes, out var byteLength) || byteLength == 0)
      throw Invalid(shapeName,
        $"bone {boneIndex} name is missing, unterminated, or exceeds {MaximumBoneNameBytes} bytes");
    context.ReserveBytes(
      Convert.ToUInt64(byteLength) + 1, shapeName, $"bone {boneIndex} name");
    context.ReserveObjects(1, shapeName, $"bone {boneIndex} name cache");
    if (!source.TryReadNullTerminatedString(address, byteLength + 1, out var value) ||
        string.IsNullOrEmpty(value))
      throw Invalid(shapeName, $"bone {boneIndex} name changed while it was being decoded");
    context.AddBoneName(address, value, shapeName, boneIndex);
    return value;
  }

  private static void ValidateSkinning(
    string shapeName,
    IReadOnlyList<BoneShapeMesh> meshes,
    int boneCount
  ) {
    foreach (var (mesh, meshIndex) in meshes.Select((value, index) => (value, index))) {
      foreach (var (vertex, vertexIndex) in
               mesh.Vertices.Select((value, index) => (value, index))) {
        var skinning = vertex.Skinning;
        ValidateInfluence(shapeName, meshIndex, vertexIndex, 0,
          skinning.Bone0, skinning.Weight0, boneCount);
        ValidateInfluence(shapeName, meshIndex, vertexIndex, 1,
          skinning.Bone1, skinning.Weight1, boneCount);
        ValidateInfluence(shapeName, meshIndex, vertexIndex, 2,
          skinning.Bone2, skinning.Weight2, boneCount);
        ValidateInfluence(shapeName, meshIndex, vertexIndex, 3,
          skinning.Bone3, skinning.Weight3, boneCount);
      }
    }
  }

  private static void ValidateInfluence(
    string shapeName,
    int meshIndex,
    int vertexIndex,
    int slot,
    sbyte bone,
    byte weight,
    int boneCount
  ) {
    if (weight == 0) return;
    if (bone < 0 || bone >= boneCount)
      throw Invalid(shapeName,
        $"mesh {meshIndex} vertex {vertexIndex} bone slot {slot} references {bone}, " +
        $"but only {boneCount} bones exist");
  }

  private static void ValidateBoneHierarchy(
    string shapeName,
    IReadOnlyList<BoneShapeBone> bones,
    DecodeContext context
  ) {
    if (bones.Count == 0) return;
    context.ReserveBytes(
      Convert.ToUInt64(bones.Count) * (sizeof(byte) + sizeof(int)),
      shapeName,
      "bone hierarchy validation");
    context.ReserveObjects(1, shapeName, "bone hierarchy path");
    var states = new byte[bones.Count];
    var path = new List<int>(bones.Count);
    for (var start = 0; start < bones.Count; start++) {
      if (states[start] == 2) continue;
      path.Clear();
      var current = start;
      for (var step = 0; step <= bones.Count; step++) {
        if (current == -1 || states[current] == 2) break;
        if (states[current] == 1)
          throw Invalid(shapeName, $"bone hierarchy contains a cycle at bone {current}");
        states[current] = 1;
        path.Add(current);
        current = bones[current].ParentBoneNumber;
        if (step == bones.Count)
          throw Invalid(shapeName, "bone hierarchy traversal exceeds the bone count");
      }
      foreach (var boneIndex in path) states[boneIndex] = 2;
    }
  }

  private static string? ReadResourceReference(
    string shapeName,
    uint shapeAddress,
    int meshIndex,
    string tag,
    uint fieldAddress,
    uint rawValue,
    IBoneShapeDataSource source
  ) {
    // ManagerBSH stores these fields as null and emits SymbolRefStruct linker targets.
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
      if (source.ResourcesByKey.TryGetValue(symbolReference.Symbol, out var symbolResource)) {
        if (!string.Equals(symbolResource.Tag, tag, StringComparison.OrdinalIgnoreCase))
          throw Invalid(shapeName,
            $"mesh {meshIndex} {tag} SymbolRef target '{symbolReference.Symbol}' has conflicting " +
            "archive resource metadata");
      } else if (source.LocalResourceKeys.Contains(symbolReference.Symbol)) {
        throw Invalid(shapeName,
          $"mesh {meshIndex} {tag} SymbolRef target '{symbolReference.Symbol}' is local but has no " +
          "validated loader metadata");
      }
      // Stock TXS styles are commonly external. A matching SymbolRef tag plus exact BSH loader
      // ownership proves those references when no local archive symbol claims the same key.
      return symbolReference.Symbol;
    }

    // Already-linked archives may instead carry a direct relocated resource pointer. Address zero
    // can be a valid target, so relocation membership distinguishes it from a null field.
    if (!source.TryGetRelocationSource(fieldAddress, out var target)) {
      if (rawValue == 0) return null;
      throw Invalid(shapeName, $"mesh {meshIndex} {tag} reference is not a valid relocation");
    }
    if (target != rawValue)
      throw Invalid(shapeName, $"mesh {meshIndex} {tag} reference has a conflicting relocation");
    if (!source.ResourcesByAddress.TryGetValue(target, out var resource))
      throw Invalid(shapeName,
        $"mesh {meshIndex} {tag} reference target {target} is not a known resource");
    if (!string.Equals(resource.Tag, tag, StringComparison.OrdinalIgnoreCase) ||
        !HasTag(resource.Key, tag))
      throw Invalid(shapeName,
        $"mesh {meshIndex} {tag} reference target '{resource.Key}' has the wrong resource type");
    return resource.Key;
  }

  private static bool HasTag(string key, string tag) =>
    key.EndsWith($":{tag}", StringComparison.OrdinalIgnoreCase);

  private static byte[] ReadArray(
    IBoneShapeDataSource source,
    uint address,
    uint count,
    int stride,
    string shapeName,
    string description,
    DecodeContext context
  ) {
    var length = ByteCount(count, stride, shapeName, description);
    return ReadExact(source, address, length, shapeName, description, context);
  }

  private static byte[] ReadExact(
    IBoneShapeDataSource source,
    uint address,
    int length,
    string shapeName,
    string description,
    DecodeContext context
  ) {
    context.ReserveBytes(Convert.ToUInt64(length), shapeName, description);
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(shapeName, $"{description} is outside the archive or truncated");
    return bytes;
  }

  private static uint ReadMatchingRequiredPointer(
    IBoneShapeDataSource source,
    uint sourceAddress,
    uint rawValue,
    string shapeName,
    string description
  ) => ReadMatchingRelocatedPointer(
    source, sourceAddress, rawValue, allowZero: false, shapeName, description);

  private static uint ReadMatchingRelocatedPointer(
    IBoneShapeDataSource source,
    uint sourceAddress,
    uint rawValue,
    bool allowZero,
    string shapeName,
    string description
  ) {
    if (!source.TryGetRelocationSource(sourceAddress, out var target) ||
        (!allowZero && target == 0))
      throw Invalid(shapeName,
        allowZero
          ? $"{description} is not a relocated pointer"
          : $"{description} is not a non-null relocated pointer");
    if (target != rawValue)
      throw Invalid(shapeName, $"{description} does not match its relocation target");
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

  private static uint CheckedMultiply(
    uint value,
    uint multiplier,
    string shapeName,
    string description
  ) {
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

  private static uint ReadUInt32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);

  private static sbyte ReadSByte(byte value) => value < 128
    ? Convert.ToSByte(value)
    : Convert.ToSByte(Convert.ToInt16(value) - 256);

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
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Bone shape '{name}' is malformed: {message}.");

  private sealed record BoneShapeIndexData(
    StaticShapeIndexLayout Layout,
    IReadOnlyList<uint> Indices,
    uint LogicalCount,
    IReadOnlyList<IReadOnlyList<uint>> PlacementSortPermutations
  );

  private readonly record struct BoneShapeTriangle(uint A, uint B, uint C)
    : IComparable<BoneShapeTriangle> {
    public int CompareTo(BoneShapeTriangle other) {
      var a = A.CompareTo(other.A);
      if (a != 0) return a;
      var b = B.CompareTo(other.B);
      return b != 0 ? b : C.CompareTo(other.C);
    }
  }

  private sealed class DecodeContext(BoneShapeDecodeLimits limits) {
    private readonly Dictionary<uint, (uint Count, IReadOnlyList<BoneShapeVertex> Vertices)>
      vertices = [];
    private readonly Dictionary<uint, IndexCacheEntry> indices = [];
    private readonly Dictionary<uint, string> boneNames = [];
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
      out IReadOnlyList<BoneShapeVertex> value
    ) {
      if (!vertices.TryGetValue(address, out var cached)) {
        value = [];
        return false;
      }
      if (cached.Count != count)
        throw new InvalidDataException(
          $"Aliased BSH vertex data at {address} has conflicting counts {cached.Count} and {count}.");
      value = cached.Vertices;
      return true;
    }

    public void AddVertices(
      uint address,
      uint count,
      IReadOnlyList<BoneShapeVertex> value,
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
      out BoneShapeIndexData value
    ) {
      if (!indices.TryGetValue(address, out var cached)) {
        value = null!;
        return false;
      }
      if (cached.StoredCount != storedCount || cached.VertexCount != vertexCount ||
          cached.PlacementTextured != placementTextured)
        throw new InvalidDataException(
          $"Aliased BSH index data at {address} has conflicting layout or count metadata.");
      value = cached.Data;
      return true;
    }

    public void AddIndices(
      uint address,
      uint storedCount,
      uint vertexCount,
      bool placementTextured,
      BoneShapeIndexData value,
      string shapeName,
      int meshIndex
    ) {
      var entry = new IndexCacheEntry(storedCount, vertexCount, placementTextured, value);
      if (!indices.TryAdd(address, entry))
        throw Invalid(shapeName, $"mesh {meshIndex} index alias changed during decoding");
    }

    public bool TryGetBoneName(uint address, out string value) =>
      boneNames.TryGetValue(address, out value!);

    public void AddBoneName(uint address, string value, string shapeName, int boneIndex) {
      if (!boneNames.TryAdd(address, value))
        throw Invalid(shapeName, $"bone {boneIndex} name alias changed during decoding");
    }

    private sealed record IndexCacheEntry(
      uint StoredCount,
      uint VertexCount,
      bool PlacementTextured,
      BoneShapeIndexData Data
    );
  }

  private sealed class OvlBoneShapeDataSource : IBoneShapeDataSource {
    private readonly Ovl ovl;
    private readonly DecodeContext context;

    public OvlBoneShapeDataSource(Ovl ovl, DecodeContext context) {
      this.ovl = ovl;
      this.context = context;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the BSH decoder limit " +
          $"{MaximumResourceCount}.");
      var symbolReferences = OvlSymbolReferenceIndex.Create(ovl);

      context.ReserveObjects(
        Convert.ToUInt64(ovl.LoaderEntriesInOrder.Count) * 4,
        "OVL",
        "loader metadata index");
      var loaderMetadata = new Dictionary<uint, OvlLoaderEntry>();
      var shapeLoaders = new HashSet<uint>();
      foreach (var entry in ovl.LoaderEntriesInOrder) {
        if (loaderMetadata.TryGetValue(entry.DataAddress, out var existing) &&
            !string.Equals(existing.Tag, entry.Tag, StringComparison.OrdinalIgnoreCase))
          throw new InvalidDataException(
            $"OVL loader data address {entry.DataAddress} has conflicting archive types.");
        loaderMetadata[entry.DataAddress] = entry;
        if (entry.Tag.ToFileType() == FileType.BoneShape)
          shapeLoaders.Add(entry.DataAddress);
      }
      BoneShapeLoaderDataAddresses = shapeLoaders;

      var byAddress = new Dictionary<uint, BoneShapeResourceMetadata>();
      var byKey = new Dictionary<string, BoneShapeResourceMetadata>(StringComparer.OrdinalIgnoreCase);
      var localKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var file in ovl.Keys) {
        var key = CreateResourceKey(file, context);
        context.ReserveObjects(1, key, "local resource key index");
        localKeys.Add(key);
        if (!ovl.TryGetDataPointer(file, out var address) ||
            !loaderMetadata.TryGetValue(address, out var loader))
          continue;
        context.ReserveObjects(3, key, "resource metadata indexes");
        var metadata = new BoneShapeResourceMetadata(key, loader.Tag);
        if (!byAddress.TryAdd(address, metadata) && byAddress[address] != metadata)
          throw new InvalidDataException(
            $"OVL resource address {address} has conflicting archive metadata.");
        if (!byKey.TryAdd(key, metadata) && byKey[key] != metadata)
          throw new InvalidDataException(
            $"OVL resource key '{key}' has conflicting archive metadata.");
      }
      ResourcesByAddress = byAddress;
      ResourcesByKey = byKey;
      LocalResourceKeys = localKeys;
      ResourceReferences = ReadResourceReferences(symbolReferences);
    }

    public IReadOnlyDictionary<uint, BoneShapeResourceMetadata> ResourcesByAddress { get; }
    public IReadOnlyDictionary<string, BoneShapeResourceMetadata> ResourcesByKey { get; }
    public IReadOnlySet<string> LocalResourceKeys { get; }
    public IReadOnlyDictionary<uint, BoneShapeResourceReference> ResourceReferences { get; }
    public IReadOnlySet<uint> BoneShapeLoaderDataAddresses { get; }

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
      if (!TryResolveStringAddress(address, out var block, out var start)) {
        length = 0;
        return false;
      }
      return TryGetNullTerminatedByteLength(block, start, maximumLength, out length);
    }

    public bool TryReadNullTerminatedString(uint address, int maximumLength, out string value) {
      if (!TryResolveStringAddress(address, out var block, out var start)) {
        value = string.Empty;
        return false;
      }

      var available = Math.Min(maximumLength, block.Length - start);
      var end = Array.IndexOf(block, Convert.ToByte(0), start, available);
      if (end < 0) {
        value = string.Empty;
        return false;
      }
      value = Encoding.ASCII.GetString(block, start, end - start);
      return true;
    }

    private bool TryResolveStringAddress(uint address, out byte[] block, out int start) {
      if (address != 0) {
        if (!ovl.TryResolveRelocation(address, out block!, out var offset)) {
          start = 0;
          return false;
        }
        start = Convert.ToInt32(offset);
        return true;
      }

      // A relocation on the BoneStruct field proves pointer intent. Resolve byte one to prove
      // that a string-table block begins at virtual address zero without treating arbitrary nulls
      // as pointers.
      if (!ovl.TryResolveRelocation(1, out block!, out var offsetAtOne) || offsetAtOne != 1) {
        start = 0;
        return false;
      }
      start = 0;
      return true;
    }

    private static string CreateResourceKey(OvlFile file, DecodeContext context) {
      if (file.Type == FileType.Unknown && file.Name.Contains(':')) {
        ReserveResourceKey(file.Name, file.Name, context);
        return file.Name;
      }
      var tag = file.Type.ToTagString();
      var length = Encoding.ASCII.GetByteCount(file.Name) + 1 + Encoding.ASCII.GetByteCount(tag);
      ReserveResourceKey(length, file.Name, context);
      return $"{file.Name}:{tag}";
    }

    private static void ReserveResourceKey(string key, string name, DecodeContext context) =>
      ReserveResourceKey(Encoding.ASCII.GetByteCount(key), name, context);

    private static void ReserveResourceKey(
      int length,
      string name,
      DecodeContext context
    ) {
      if (length > MaximumResourceNameBytes)
        throw new InvalidDataException(
          $"OVL resource key exceeds the BSH decoder limit {MaximumResourceNameBytes} bytes.");
      context.ReserveBytes(Convert.ToUInt64(length) + 1, name, "resource index key");
    }

    private IReadOnlyDictionary<uint, BoneShapeResourceReference> ReadResourceReferences(
      OvlSymbolReferenceIndex symbolReferenceIndex
    ) {
      var references = new Dictionary<uint, BoneShapeResourceReference>();
      foreach (var (fieldAddress, indexed) in symbolReferenceIndex.References) {
        if (indexed.Owner.Tag.ToFileType() != FileType.BoneShape) continue;
        var reference = new BoneShapeResourceReference(
          indexed.Symbol, indexed.Owner.DataAddress);
        if (references.TryGetValue(fieldAddress, out var existing) && existing != reference)
          throw new InvalidDataException(
            $"BSH SymbolRef field {fieldAddress} has conflicting targets or owners.");
        if (references.ContainsKey(fieldAddress)) continue;
        context.ReserveBytes(
          Convert.ToUInt64(Encoding.ASCII.GetByteCount(indexed.Symbol)) + 1,
          indexed.Symbol,
          "SymbolRef string");
        context.ReserveObjects(1, indexed.Symbol, "SymbolRef index");
        references.Add(fieldAddress, reference);
      }
      return references;
    }

    private static bool TryGetNullTerminatedByteLength(
      byte[] block,
      int start,
      int maximumLength,
      out int length
    ) {
      if (start < 0 || start >= block.Length || maximumLength <= 0) {
        length = 0;
        return false;
      }
      var available = Math.Min(maximumLength, block.Length - start);
      var end = Array.IndexOf(block, Convert.ToByte(0), start, available);
      length = end < 0 ? 0 : end - start;
      return end >= 0;
    }
  }
}

internal sealed record BoneShapeResourceReference(string Symbol, uint OwnerAddress);
internal sealed record BoneShapeResourceMetadata(string Key, string Tag);

internal readonly record struct BoneShapeDecodeLimits(ulong MaximumBytes, ulong MaximumObjects) {
  public static BoneShapeDecodeLimits Default { get; } =
    new(256UL * 1024 * 1024, 1_000_000);
}

internal interface IBoneShapeDataSource {
  IReadOnlyDictionary<uint, BoneShapeResourceMetadata> ResourcesByAddress { get; }
  IReadOnlyDictionary<string, BoneShapeResourceMetadata> ResourcesByKey { get; }
  IReadOnlySet<string> LocalResourceKeys { get; }
  IReadOnlyDictionary<uint, BoneShapeResourceReference> ResourceReferences { get; }
  IReadOnlySet<uint> BoneShapeLoaderDataAddresses { get; }
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryGetNullTerminatedStringByteLength(uint address, int maximumLength, out int length);
  bool TryReadNullTerminatedString(uint address, int maximumLength, out string value);
}
