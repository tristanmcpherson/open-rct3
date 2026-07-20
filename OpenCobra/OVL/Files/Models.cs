// Models
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Numerics;
using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>One proven serialized MDL bone plus its separately serialized name and pair.</summary>
public sealed record ModelBone(
  string Name,
  Vector4 PositionQuaternion,
  Vector4 RotationQuaternion,
  Matrix4x4 Matrix,
  ushort Parent,
  ushort BoneNumber
);

/// <summary>One neutral 0x10 surface-like record and its two serialized value tails.</summary>
public sealed record ModelSurfaceRecord(
  ushort Field0,
  ushort CountA,
  ushort CountB,
  ushort Field6,
  IReadOnlyList<uint> ValuesA,
  IReadOnlyList<uint> ValuesB
);

/// <summary>One exact weighted VERTEX2 mesh from an MDL group.</summary>
public sealed record ModelMesh(
  uint Fvf,
  uint StoredIndexCount,
  ushort Multiplier,
  ushort StoredVertexCount,
  ushort Field0C,
  ushort Field0E,
  uint Field10,
  uint Field1C,
  IReadOnlyList<BoneShapeVertex> Vertices,
  IReadOnlyList<uint> Indices
) {
  public int TriangleCount => Indices.Count / 3;
}

/// <summary>One neutral MDL group in stable serialized order.</summary>
public sealed record ModelGroup(
  ushort SurfaceRecordCount,
  ushort MeshCount,
  uint Field4,
  uint BitsetMarker,
  IReadOnlyList<uint> BoneBitsetWords,
  uint OptionalRecordMarker,
  IReadOnlyList<ModelMesh> Meshes,
  IReadOnlyList<ModelSurfaceRecord> SurfaceRecords
);

/// <summary>Evidence-backed static fields and geometry from one installed <c>mdl</c>.</summary>
public sealed record ModelDefinition(
  string Name,
  string SourcePath,
  uint DataAddress,
  uint LoaderStructAddress,
  uint Count0,
  uint Count1,
  uint Count2,
  uint Count3
) {
  /// <summary><c>Count0</c> is proven to be the serialized bone count.</summary>
  public uint BoneCount => Count0;
  /// <summary>Optional raw u32 values governed by loader-record field +0x4C.</summary>
  public IReadOnlyList<uint> InlineValues { get; init; } = [];
  /// <summary>Count0 bones with exact same-index names and raw parent/bone-number pairs.</summary>
  public IReadOnlyList<ModelBone> Bones { get; init; } = [];
  /// <summary>The neutral Count2 u32 region.</summary>
  public IReadOnlyList<uint> Count2Values { get; init; } = [];
  /// <summary>Length-prefixed strings governed by loader-record field +0x06.</summary>
  public IReadOnlyList<string> Strings { get; init; } = [];
  /// <summary>Static groups in exact serialized order.</summary>
  public IReadOnlyList<ModelGroup> Groups { get; init; } = [];
}

/// <summary>Decodes the proven static subset of the installed two-chunk <c>mdl</c> layout.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> SHA-256
/// <c>1C9316E728D67AAA3BFE36D0A1634F3139582927440A5467C351E4C949B5B21D</c> registers the
/// <c>Model</c>/<c>mdl</c> loader at VA <c>0x00F1A490</c>. Its load hook at
/// <c>0x00F1A3F0</c> calls the hydrator at <c>0x00F18C10</c>. The hydrator's 0x40 allocator
/// argument is an alignment, not a header size. The serialized chunk begins with an exact 0x10
/// four-count prefix.
///
/// Native cursor proofs establish Count0 as the bone count, the 0x60 bone stride, parent/bone pairs,
/// Count0 names in chunk 1, group/mesh/surface cursor order, and FVF 0x1305 as the same exact 44-byte
/// weighted VERTEX2 layout used by BSH. Count1, Count2, Count3, group field +0x04, surface fields,
/// and other retained mesh fields remain deliberately neutral. Nonzero optional-record markers and
/// every unsupported FVF/multiplier fail closed.
///
/// The relocatable loader record is separate evidence. Installed AdultElephant proves an exact
/// common-half owner, a relocated loader data field, a 0x50-byte record with no internal
/// relocations, and exactly two owner-bound extra-data chunks.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/model.h">
/// Pinned model extra-data declarations
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/vertex.h#L51-L58">
/// Pinned weighted VERTEX2 declaration
/// </seealso>
public static class Models {
  private const int LoaderRecordSize = 0x50;
  private const int SerializedCountSize = 0x10;
  private const int BoneSize = 0x60;
  private const int BonePairSize = 4;
  private const int GroupHeaderSize = 0x14;
  private const int MeshHeaderSize = 0x20;
  private const int SurfaceRecordSize = 0x10;
  private const int VertexSize = 44;
  private const uint SupportedFvf = 0x1305;
  private const ushort SupportedMultiplier = 1;
  private const int InstalledExtraChunkCount = 2;
  private const uint MaximumNeutralCount = 1_000_000;
  private const uint MaximumBoneCount = 64 * 1024;
  private const int MaximumInlineValues = 1_000_000;
  private const int MaximumStringBytes = 4 * 1024;
  private const ulong MaximumMeshes = 16 * 1024;
  private const ulong MaximumVertices = 4 * 1024 * 1024;
  private const ulong MaximumIndices = 16 * 1024 * 1024;
  private const ulong MaximumSurfaceRecords = 1_000_000;
  private const ulong MaximumSurfaceValues = 4 * 1024 * 1024;
  private const int MaximumChunk0Bytes = 256 * 1024 * 1024;
  private const int MaximumChunk1Bytes = 16 * 1024 * 1024;
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
    if (chunks[0].Length > MaximumChunk0Bytes)
      throw Invalid(name, $"first extra-data chunk exceeds {MaximumChunk0Bytes} bytes");
    if (chunks[1].Length > MaximumChunk1Bytes)
      throw Invalid(name, $"second extra-data chunk exceeds {MaximumChunk1Bytes} bytes");

    var reader = new ModelCursor(chunks[0], name, "first extra-data chunk");
    if (reader.Remaining < SerializedCountSize)
      throw Invalid(name, "first extra-data chunk is shorter than the four-count prefix");
    var counts = Enumerable.Range(0, 4).Select(_ => reader.ReadUInt32("count prefix")).ToArray();
    ValidateCounts(name, counts);
    var boneCount = ToCount(counts[0], MaximumBoneCount, name, "bone count");

    var inlineCount = ToCount(
      ReadUInt32(record, 0x4C), MaximumInlineValues, name, "inline value count");
    var inlineValues = ReadUInt32Values(reader, inlineCount, "inline values");
    reader.Align16("bone array");

    var rawBones = ReadBones(reader, boneCount);
    var pairs = ReadBonePairs(reader, boneCount);
    var count2ValueCount = ToCount(
      counts[2], MaximumNeutralCount, name, "Count2 value count");
    var count2Values = ReadUInt32Values(reader, count2ValueCount, "Count2 values");
    reader.Align16("length-prefixed strings");

    var stringCount = ReadUInt16(record, 6);
    var strings = ReadLengthPrefixedStrings(reader, stringCount);
    reader.Align16("group headers");

    var groupCount = record[4];
    var groups = ReadGroups(reader, groupCount, boneCount);
    reader.RequireEnd();
    var boneNames = ReadBoneNames(chunks[1], boneCount, name);
    var bones = new ModelBone[boneCount];
    foreach (var index in Enumerable.Range(0, boneCount)) {
      bones[index] = new ModelBone(
        boneNames[index],
        rawBones[index].PositionQuaternion,
        rawBones[index].RotationQuaternion,
        rawBones[index].Matrix,
        pairs[index].Parent,
        pairs[index].BoneNumber);
    }
    ValidateSkinning(name, groups, boneCount);

    return new ModelDefinition(
      name,
      owner.SourcePath,
      owner.DataAddress,
      owner.StructAddress,
      counts[0],
      counts[1],
      counts[2],
      counts[3]) {
      InlineValues = Array.AsReadOnly(inlineValues),
      Bones = Array.AsReadOnly(bones),
      Count2Values = Array.AsReadOnly(count2Values),
      Strings = Array.AsReadOnly(strings),
      Groups = Array.AsReadOnly(groups),
    };
  }

  private static RawBone[] ReadBones(ModelCursor reader, int count) {
    reader.RequireElements(count, BoneSize, "bone array");
    var bones = new RawBone[count];
    foreach (var index in Enumerable.Range(0, count)) {
      var position = reader.ReadVector4($"bone {index} position quaternion");
      var rotation = reader.ReadVector4($"bone {index} rotation quaternion");
      var matrix = reader.ReadMatrix($"bone {index} matrix");
      if (!IsFinite(position) || !IsFinite(rotation) || !IsFinite(matrix))
        throw Invalid(reader.Name, $"bone {index} contains a non-finite value");
      bones[index] = new RawBone(position, rotation, matrix);
    }
    return bones;
  }

  private static RawBonePair[] ReadBonePairs(ModelCursor reader, int count) {
    reader.RequireElements(count, BonePairSize, "bone parent/number pairs");
    var pairs = new RawBonePair[count];
    foreach (var index in Enumerable.Range(0, count)) {
      var parent = reader.ReadUInt16($"bone {index} parent");
      var boneNumber = reader.ReadUInt16($"bone {index} bone number");
      if (parent != ushort.MaxValue && parent >= count)
        throw Invalid(reader.Name,
          $"bone {index} parent {parent} is outside the {count}-bone array");
      pairs[index] = new RawBonePair(parent, boneNumber);
    }
    return pairs;
  }

  private static string[] ReadLengthPrefixedStrings(ModelCursor reader, int count) {
    reader.RequireElements(count, sizeof(uint), "string length table");
    var lengths = new int[count];
    foreach (var index in Enumerable.Range(0, count)) {
      var rawLength = reader.ReadUInt32($"string {index} length");
      lengths[index] = ToCount(
        rawLength, MaximumStringBytes, reader.Name, $"string {index} byte length");
      if (lengths[index] == 0)
        throw Invalid(reader.Name, $"string {index} has zero byte length");
    }

    var strings = new string[count];
    foreach (var index in Enumerable.Range(0, count))
      strings[index] = reader.ReadAsciiString(lengths[index], $"string {index}");
    return strings;
  }

  private static ModelGroup[] ReadGroups(
    ModelCursor reader,
    int count,
    int boneCount
  ) {
    reader.RequireElements(count, GroupHeaderSize, "group header array");
    var headers = new SerializedGroupHeader[count];
    ulong totalMeshes = 0;
    ulong totalSurfaces = 0;
    foreach (var index in Enumerable.Range(0, count)) {
      var surfaceCount = reader.ReadUInt16($"group {index} surface-record count");
      var meshCount = reader.ReadUInt16($"group {index} mesh count");
      var field4 = reader.ReadUInt32($"group {index} field +0x04");
      var surfaceCursor = reader.ReadUInt32($"group {index} runtime surface cursor");
      var meshCursor = reader.ReadUInt32($"group {index} runtime mesh cursor");
      var bitsetMarker = reader.ReadUInt32($"group {index} bitset marker");
      if (surfaceCursor != 0 || meshCursor != 0)
        throw Invalid(reader.Name,
          $"group {index} serialized runtime cursor field is nonzero");
      Reserve(ref totalMeshes, meshCount, MaximumMeshes, reader.Name, "meshes");
      Reserve(
        ref totalSurfaces, surfaceCount, MaximumSurfaceRecords, reader.Name, "surface records");
      headers[index] = new SerializedGroupHeader(
        surfaceCount, meshCount, field4, bitsetMarker);
    }

    reader.RequireElements(count, sizeof(uint), "group optional-record marker array");
    var markers = new uint[count];
    foreach (var index in Enumerable.Range(0, count)) {
      markers[index] = reader.ReadUInt32($"group {index} optional-record marker");
      if (markers[index] != 0)
        throw Invalid(reader.Name,
          $"group {index} uses an unsupported optional 0x14 record marker " +
          $"0x{markers[index]:X8}");
    }
    reader.Align16("group payloads");

    var groups = new ModelGroup[count];
    ulong totalVertices = 0;
    ulong totalIndices = 0;
    ulong totalSurfaceValues = 0;
    foreach (var index in Enumerable.Range(0, count)) {
      var header = headers[index];
      var bitsetWords = ReadBitset(reader, header.BitsetMarker, boneCount, index);
      var meshHeaders = ReadMeshHeaders(reader, header.MeshCount, index);
      reader.Align16($"group {index} mesh payload");
      var meshes = new ModelMesh[meshHeaders.Length];
      foreach (var meshIndex in Enumerable.Range(0, meshHeaders.Length)) {
        var meshHeader = meshHeaders[meshIndex];
        Reserve(
          ref totalVertices,
          meshHeader.VertexCount,
          MaximumVertices,
          reader.Name,
          "vertices");
        Reserve(
          ref totalIndices,
          meshHeader.IndexCount,
          MaximumIndices,
          reader.Name,
          "indices");
        meshes[meshIndex] = ReadMesh(reader, meshHeader, index, meshIndex);
      }

      var surfaceHeaders = ReadSurfaceHeaders(reader, header.SurfaceCount, index);
      var surfaces = new ModelSurfaceRecord[surfaceHeaders.Length];
      foreach (var surfaceIndex in Enumerable.Range(0, surfaceHeaders.Length)) {
        var surfaceHeader = surfaceHeaders[surfaceIndex];
        Reserve(
          ref totalSurfaceValues,
          Convert.ToUInt64(surfaceHeader.CountA) + surfaceHeader.CountB,
          MaximumSurfaceValues,
          reader.Name,
          "surface values");
        var valuesA = ReadUInt32Values(
          reader,
          surfaceHeader.CountA,
          $"group {index} surface {surfaceIndex} values A");
        var valuesB = ReadUInt32Values(
          reader,
          surfaceHeader.CountB,
          $"group {index} surface {surfaceIndex} values B");
        surfaces[surfaceIndex] = new ModelSurfaceRecord(
          surfaceHeader.Field0,
          surfaceHeader.CountA,
          surfaceHeader.CountB,
          surfaceHeader.Field6,
          Array.AsReadOnly(valuesA),
          Array.AsReadOnly(valuesB));
      }

      groups[index] = new ModelGroup(
        header.SurfaceCount,
        header.MeshCount,
        header.Field4,
        header.BitsetMarker,
        Array.AsReadOnly(bitsetWords),
        markers[index],
        Array.AsReadOnly(meshes),
        Array.AsReadOnly(surfaces));
    }
    return groups;
  }

  private static uint[] ReadBitset(
    ModelCursor reader,
    uint marker,
    int boneCount,
    int groupIndex
  ) {
    if (marker == 0) return [];
    var wordCount = Convert.ToInt32((Convert.ToUInt64(boneCount) + 31) >> 5);
    return ReadUInt32Values(reader, wordCount, $"group {groupIndex} bone bitset");
  }

  private static SerializedMeshHeader[] ReadMeshHeaders(
    ModelCursor reader,
    int count,
    int groupIndex
  ) {
    reader.RequireElements(count, MeshHeaderSize, $"group {groupIndex} mesh headers");
    var headers = new SerializedMeshHeader[count];
    foreach (var index in Enumerable.Range(0, count)) {
      var fvf = reader.ReadUInt32($"group {groupIndex} mesh {index} FVF");
      var indexCount = reader.ReadUInt32($"group {groupIndex} mesh {index} index count");
      var multiplier = reader.ReadUInt16($"group {groupIndex} mesh {index} multiplier");
      var vertexCount = reader.ReadUInt16($"group {groupIndex} mesh {index} vertex count");
      var field0C = reader.ReadUInt16($"group {groupIndex} mesh {index} field +0x0C");
      var field0E = reader.ReadUInt16($"group {groupIndex} mesh {index} field +0x0E");
      var field10 = reader.ReadUInt32($"group {groupIndex} mesh {index} field +0x10");
      var vertexCursor = reader.ReadUInt32(
        $"group {groupIndex} mesh {index} runtime vertex cursor");
      var indexCursor = reader.ReadUInt32(
        $"group {groupIndex} mesh {index} runtime index cursor");
      var field1C = reader.ReadUInt32($"group {groupIndex} mesh {index} field +0x1C");
      if (fvf != SupportedFvf)
        throw Invalid(reader.Name,
          $"group {groupIndex} mesh {index} FVF 0x{fvf:X} is not supported " +
          $"0x{SupportedFvf:X}");
      if (multiplier != SupportedMultiplier)
        throw Invalid(reader.Name,
          $"group {groupIndex} mesh {index} multiplier {multiplier} is not supported " +
          $"{SupportedMultiplier}");
      if (vertexCursor != 0 || indexCursor != 0)
        throw Invalid(reader.Name,
          $"group {groupIndex} mesh {index} serialized runtime cursor field is nonzero");
      if (vertexCount == 0)
        throw Invalid(reader.Name, $"group {groupIndex} mesh {index} has zero vertices");
      if (indexCount == 0 || indexCount % 3 != 0)
        throw Invalid(reader.Name,
          $"group {groupIndex} mesh {index} index count {indexCount} is not triangles");
      if (indexCount > MaximumIndices)
        throw Invalid(reader.Name,
          $"group {groupIndex} mesh {index} index count exceeds {MaximumIndices}");
      headers[index] = new SerializedMeshHeader(
        fvf,
        indexCount,
        multiplier,
        vertexCount,
        field0C,
        field0E,
        field10,
        field1C);
    }
    return headers;
  }

  private static ModelMesh ReadMesh(
    ModelCursor reader,
    SerializedMeshHeader header,
    int groupIndex,
    int meshIndex
  ) {
    reader.RequireElements(
      header.VertexCount,
      VertexSize,
      $"group {groupIndex} mesh {meshIndex} vertices");
    var vertices = new BoneShapeVertex[header.VertexCount];
    foreach (var vertexIndex in Enumerable.Range(0, vertices.Length))
      vertices[vertexIndex] = ReadVertex(reader, groupIndex, meshIndex, vertexIndex);

    var indexCount = ToCount(
      header.IndexCount,
      MaximumIndices,
      reader.Name,
      $"group {groupIndex} mesh {meshIndex} index count");
    reader.RequireElements(
      indexCount,
      sizeof(ushort),
      $"group {groupIndex} mesh {meshIndex} indices");
    var indices = new uint[indexCount];
    foreach (var index in Enumerable.Range(0, indexCount)) {
      indices[index] = reader.ReadUInt16($"group {groupIndex} mesh {meshIndex} index {index}");
      if (indices[index] >= header.VertexCount)
        throw Invalid(reader.Name,
          $"group {groupIndex} mesh {meshIndex} index {index} value {indices[index]} " +
          $"is outside {header.VertexCount} vertices");
    }

    return new ModelMesh(
      header.Fvf,
      header.IndexCount,
      header.Multiplier,
      header.VertexCount,
      header.Field0C,
      header.Field0E,
      header.Field10,
      header.Field1C,
      Array.AsReadOnly(vertices),
      Array.AsReadOnly(indices));
  }

  private static BoneShapeVertex ReadVertex(
    ModelCursor reader,
    int groupIndex,
    int meshIndex,
    int vertexIndex
  ) {
    var position = reader.ReadVector3(
      $"group {groupIndex} mesh {meshIndex} vertex {vertexIndex} position");
    var normal = reader.ReadVector3(
      $"group {groupIndex} mesh {meshIndex} vertex {vertexIndex} normal");
    var bone0 = reader.ReadByte("vertex bone 0");
    var bone1 = reader.ReadByte("vertex bone 1");
    var bone2 = reader.ReadByte("vertex bone 2");
    var bone3 = reader.ReadByte("vertex bone 3");
    var weight0 = reader.ReadByte("vertex weight 0");
    var weight1 = reader.ReadByte("vertex weight 1");
    var weight2 = reader.ReadByte("vertex weight 2");
    var weight3 = reader.ReadByte("vertex weight 3");
    var color = reader.ReadUInt32("vertex color");
    var texCoord = new Vector2(
      reader.ReadSingle("vertex texture U"),
      reader.ReadSingle("vertex texture V"));
    if (!IsFinite(position) || !IsFinite(normal) || !IsFinite(texCoord))
      throw Invalid(reader.Name,
        $"group {groupIndex} mesh {meshIndex} vertex {vertexIndex} is non-finite");

    return new BoneShapeVertex(
      position,
      normal,
      texCoord,
      new Vector4(
        Convert.ToByte(color >> 16 & 255) / 255.0f,
        Convert.ToByte(color >> 8 & 255) / 255.0f,
        Convert.ToByte(color & 255) / 255.0f,
        Convert.ToByte(color >> 24 & 255) / 255.0f),
      new BoneShapeSkinning(
        bone0, bone1, bone2, bone3,
        weight0, weight1, weight2, weight3));
  }

  private static SerializedSurfaceHeader[] ReadSurfaceHeaders(
    ModelCursor reader,
    int count,
    int groupIndex
  ) {
    reader.RequireElements(
      count,
      SurfaceRecordSize,
      $"group {groupIndex} surface-record headers");
    var headers = new SerializedSurfaceHeader[count];
    foreach (var index in Enumerable.Range(0, count)) {
      var field0 = reader.ReadUInt16($"group {groupIndex} surface {index} field +0x00");
      var countA = reader.ReadUInt16($"group {groupIndex} surface {index} count A");
      var countB = reader.ReadUInt16($"group {groupIndex} surface {index} count B");
      var field6 = reader.ReadUInt16($"group {groupIndex} surface {index} field +0x06");
      var cursorA = reader.ReadUInt32($"group {groupIndex} surface {index} runtime cursor A");
      var cursorB = reader.ReadUInt32($"group {groupIndex} surface {index} runtime cursor B");
      if (cursorA != 0 || cursorB != 0)
        throw Invalid(reader.Name,
          $"group {groupIndex} surface {index} serialized runtime cursor field is nonzero");
      headers[index] = new SerializedSurfaceHeader(field0, countA, countB, field6);
    }
    return headers;
  }

  private static string[] ReadBoneNames(byte[] bytes, int count, string name) {
    var reader = new ModelCursor(bytes, name, "bone-name chunk");
    var names = new string[count];
    foreach (var index in Enumerable.Range(0, count))
      names[index] = reader.ReadNullTerminatedAsciiString(
        MaximumStringBytes,
        $"bone {index} name");
    reader.RequireEnd();
    return names;
  }

  private static void ValidateSkinning(
    string name,
    IReadOnlyList<ModelGroup> groups,
    int boneCount
  ) {
    foreach (var group in groups.Select((value, index) => (value, index))) {
      foreach (var mesh in group.value.Meshes.Select((value, index) => (value, index))) {
        foreach (var vertex in mesh.value.Vertices.Select((value, index) => (value, index))) {
          var skinning = vertex.value.Skinning;
          ValidateInfluence(
            name, group.index, mesh.index, vertex.index, 0,
            skinning.Bone0, skinning.Weight0, boneCount);
          ValidateInfluence(
            name, group.index, mesh.index, vertex.index, 1,
            skinning.Bone1, skinning.Weight1, boneCount);
          ValidateInfluence(
            name, group.index, mesh.index, vertex.index, 2,
            skinning.Bone2, skinning.Weight2, boneCount);
          ValidateInfluence(
            name, group.index, mesh.index, vertex.index, 3,
            skinning.Bone3, skinning.Weight3, boneCount);
        }
      }
    }
  }

  private static void ValidateInfluence(
    string name,
    int groupIndex,
    int meshIndex,
    int vertexIndex,
    int slot,
    byte bone,
    byte weight,
    int boneCount
  ) {
    if (weight == 0) return;
    if (bone >= boneCount)
      throw Invalid(name,
        $"group {groupIndex} mesh {meshIndex} vertex {vertexIndex} bone slot {slot} " +
        $"references {bone}, but only {boneCount} bones exist");
  }

  private static uint[] ReadUInt32Values(
    ModelCursor reader,
    int count,
    string description
  ) {
    reader.RequireElements(count, sizeof(uint), description);
    var values = new uint[count];
    foreach (var index in Enumerable.Range(0, count))
      values[index] = reader.ReadUInt32($"{description} {index}");
    return values;
  }

  private static void ValidateCounts(string name, IReadOnlyList<uint> counts) {
    foreach (var indexed in counts.Select((value, index) => (value, index))) {
      if (indexed.value > MaximumNeutralCount)
        throw Invalid(name,
          $"Count{indexed.index} {indexed.value} exceeds the decoder limit " +
          $"{MaximumNeutralCount}");
    }
    if (counts[0] > MaximumBoneCount)
      throw Invalid(name, $"bone count {counts[0]} exceeds {MaximumBoneCount}");
  }

  private static int ToCount(
    uint value,
    ulong maximum,
    string name,
    string description
  ) {
    if (Convert.ToUInt64(value) > maximum || value > int.MaxValue)
      throw Invalid(name, $"{description} {value} exceeds {maximum}");
    return Convert.ToInt32(value);
  }

  private static void Reserve(
    ref ulong total,
    ulong value,
    ulong maximum,
    string name,
    string description
  ) {
    if (value > maximum - total)
      throw Invalid(name, $"aggregate {description} exceed {maximum}");
    total += value;
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

  private static ushort ReadUInt16(byte[] bytes, int offset) =>
    BitConverter.ToUInt16(bytes, offset);

  private static uint ReadUInt32(byte[] bytes, int offset) =>
    BitConverter.ToUInt32(bytes, offset);

  private static bool IsFinite(Vector2 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

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
    new($"Model '{name}' is malformed: {message}.");

  private sealed record RawBone(
    Vector4 PositionQuaternion,
    Vector4 RotationQuaternion,
    Matrix4x4 Matrix
  );

  private sealed record RawBonePair(ushort Parent, ushort BoneNumber);

  private sealed record SerializedGroupHeader(
    ushort SurfaceCount,
    ushort MeshCount,
    uint Field4,
    uint BitsetMarker
  );

  private sealed record SerializedMeshHeader(
    uint Fvf,
    uint IndexCount,
    ushort Multiplier,
    ushort VertexCount,
    ushort Field0C,
    ushort Field0E,
    uint Field10,
    uint Field1C
  );

  private sealed record SerializedSurfaceHeader(
    ushort Field0,
    ushort CountA,
    ushort CountB,
    ushort Field6
  );

  private sealed class ModelCursor(byte[] data, string name, string section) {
    public string Name { get; } = name;
    public int Offset { get; private set; }
    public int Remaining => data.Length - Offset;

    public byte ReadByte(string description) {
      Require(1, description);
      return data[Offset++];
    }

    public ushort ReadUInt16(string description) {
      Require(sizeof(ushort), description);
      var value = BitConverter.ToUInt16(data, Offset);
      Offset += sizeof(ushort);
      return value;
    }

    public uint ReadUInt32(string description) {
      Require(sizeof(uint), description);
      var value = BitConverter.ToUInt32(data, Offset);
      Offset += sizeof(uint);
      return value;
    }

    public float ReadSingle(string description) {
      Require(sizeof(float), description);
      var value = BitConverter.ToSingle(data, Offset);
      Offset += sizeof(float);
      return value;
    }

    public Vector3 ReadVector3(string description) => new(
      ReadSingle(description),
      ReadSingle(description),
      ReadSingle(description));

    public Vector4 ReadVector4(string description) => new(
      ReadSingle(description),
      ReadSingle(description),
      ReadSingle(description),
      ReadSingle(description));

    public Matrix4x4 ReadMatrix(string description) => new(
      ReadSingle(description), ReadSingle(description),
      ReadSingle(description), ReadSingle(description),
      ReadSingle(description), ReadSingle(description),
      ReadSingle(description), ReadSingle(description),
      ReadSingle(description), ReadSingle(description),
      ReadSingle(description), ReadSingle(description),
      ReadSingle(description), ReadSingle(description),
      ReadSingle(description), ReadSingle(description));

    public string ReadAsciiString(int length, string description) {
      Require(length, description);
      var start = Offset;
      Offset += length;
      if (data[Offset - 1] != 0 ||
          Array.IndexOf(data, Convert.ToByte(0), start, length - 1) >= 0)
        throw Invalid(Name, $"{description} is not exactly NUL terminated");
      if (data.AsSpan(start, length - 1).ContainsAnyExceptInRange(
            Convert.ToByte(32), Convert.ToByte(126)))
        throw Invalid(Name, $"{description} contains non-ASCII text bytes");
      return Encoding.ASCII.GetString(data, start, length - 1);
    }

    public string ReadNullTerminatedAsciiString(int maximumBytes, string description) {
      var available = Math.Min(Remaining, maximumBytes + 1);
      var end = available <= 0
        ? -1
        : Array.IndexOf(data, Convert.ToByte(0), Offset, available);
      if (end <= Offset)
        throw Invalid(Name,
          $"{description} is empty, unterminated, or exceeds {maximumBytes} bytes");
      var length = end - Offset;
      if (data.AsSpan(Offset, length).ContainsAnyExceptInRange(
            Convert.ToByte(32), Convert.ToByte(126)))
        throw Invalid(Name, $"{description} contains non-ASCII text bytes");
      var value = Encoding.ASCII.GetString(data, Offset, length);
      Offset = end + 1;
      return value;
    }

    public void Align16(string description) {
      var aligned = (Convert.ToInt64(Offset) + 15) & ~15L;
      if (aligned > int.MaxValue)
        throw Invalid(Name, $"{description} alignment exceeds the supported range");
      Require(Convert.ToInt32(aligned) - Offset, $"{description} alignment");
      Offset = Convert.ToInt32(aligned);
    }

    public void RequireElements(int count, int stride, string description) {
      if (count < 0 || stride < 0)
        throw Invalid(Name, $"{description} has a negative count or stride");
      var bytes = Convert.ToInt64(count) * stride;
      if (bytes > int.MaxValue || bytes > Remaining)
        throw Invalid(Name, $"{description} is outside or truncated in the {section}");
    }

    public void RequireEnd() {
      if (Offset != data.Length)
        throw Invalid(Name,
          $"{section} cursor ended at {Offset} instead of exact length {data.Length}");
    }

    private void Require(int length, string description) {
      if (length < 0 || length > Remaining)
        throw Invalid(Name, $"{description} is outside or truncated in the {section}");
    }
  }

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
