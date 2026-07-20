// Bone Shape Mesh Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Builds rest-pose render meshes from decoded RCT3 bone-shape resources.</summary>
/// <remarks>
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/vertex.h">
/// VERTEX2</see> stores position, normal, UV, color, and skinning bytes together. The authoritative
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerBSH.cpp">
/// ManagerBSH</see> copies those vertices directly and stores bone matrices separately. This adapter
/// deliberately renders only the stored vertex rest pose. It does not apply bone matrices or infer
/// animation semantics. Coordinates and winding use the same native RCT3-to-park conversion as static
/// shapes.
/// </remarks>
public static class BoneShapeMeshBuilder {
  /// <summary>
  /// Converts each decoded source mesh into one material-selectable render batch, retaining source
  /// order and one deterministic triangle order for placement-sorted meshes.
  /// </summary>
  public static IReadOnlyList<StaticShapeMeshBatch> BuildBatches(BoneShape shape) {
    ArgumentNullException.ThrowIfNull(shape);
    if (shape.Meshes == null || shape.Meshes.Count == 0)
      throw Invalid(shape, null, "contains no meshes");

    var batches = new List<StaticShapeMeshBatch>(shape.Meshes.Count);
    var meshIndex = 0;
    foreach (var source in shape.Meshes) {
      if (source == null)
        throw Invalid(shape, meshIndex, "is null");
      if (source.Name == null)
        throw Invalid(shape, meshIndex, "has no source name");
      batches.Add(BuildBatch(shape, source, meshIndex));
      meshIndex++;
    }
    return batches.ToArray();
  }

  private static StaticShapeMeshBatch BuildBatch(
    BoneShape shape,
    BoneShapeMesh source,
    int meshIndex
  ) {
    if (source.Vertices == null || source.Vertices.Count == 0)
      throw Invalid(shape, meshIndex, "contains no vertices");
    if (source.Indices == null || source.Indices.Count == 0)
      throw Invalid(shape, meshIndex, "contains no indices");

    ValidateIndexLayout(shape, source, meshIndex);
    if (source.Indices.Count % 3 != 0)
      throw Invalid(shape, meshIndex, $"has {source.Indices.Count} indices, not a triangle list");

    var vertices = new List<Vertex>(source.Vertices.Count);
    var vertexIndex = 0;
    foreach (var sourceVertex in source.Vertices) {
      if (!IsFinite(sourceVertex.Position) || !IsFinite(sourceVertex.Normal) ||
          !IsFinite(sourceVertex.TexCoord) || !IsFinite(sourceVertex.Color))
        throw Invalid(shape, meshIndex, $"vertex {vertexIndex} contains a non-finite value");

      var position = ToOpenRct3Coordinates(sourceVertex.Position);
      var normal = ToOpenRct3Coordinates(sourceVertex.Normal);
      if (!IsFinite(position) || !IsFinite(normal))
        throw Invalid(shape, meshIndex, $"vertex {vertexIndex} transforms to a non-finite value");

      vertices.Add(new Vertex {
        Position = position,
        Normal = normal,
        TexCoord = sourceVertex.TexCoord,
        Color = sourceVertex.Color
      });
      vertexIndex++;
    }

    var indices = new List<uint>(source.Indices.Count);
    for (var index = 0; index < source.Indices.Count; index += 3) {
      ValidateIndex(shape, meshIndex, source.Indices[index], index, vertices.Count);
      ValidateIndex(shape, meshIndex, source.Indices[index + 1], index + 1, vertices.Count);
      ValidateIndex(shape, meshIndex, source.Indices[index + 2], index + 2, vertices.Count);

      // The coordinate transform changes handedness. Swap the first two vertices so the rest-pose
      // geometry stays counter-clockwise, exactly as for decoded static shapes.
      indices.Add(source.Indices[index + 1]);
      indices.Add(source.Indices[index]);
      indices.Add(source.Indices[index + 2]);
    }

    var mesh = new Mesh(vertices, indices) { Name = source.Name };
    return new StaticShapeMeshBatch(
      meshIndex,
      source.Name,
      source.SupportType,
      source.FtxRef,
      source.TxsRef,
      source.Transparency,
      source.TextureFlags,
      source.Sides,
      mesh);
  }

  private static void ValidateIndexLayout(
    BoneShape shape,
    BoneShapeMesh source,
    int meshIndex
  ) {
    var physicalCount = Convert.ToUInt64(source.Indices.Count);
    if (physicalCount != source.LogicalIndexCount)
      throw Invalid(shape, meshIndex,
        $"logical index count {source.LogicalIndexCount} does not match " +
        $"the {physicalCount} retained indices");
    if (source.PlacementSortPermutations == null)
      throw Invalid(shape, meshIndex, "has a null placement sort permutation list");

    switch (source.IndexLayout) {
      case StaticShapeIndexLayout.TriangleList:
        if (source.PlacementSortPermutations.Count != 0)
          throw Invalid(shape, meshIndex, "triangle list unexpectedly carries sort permutations");
        if (physicalCount != source.StoredIndexCount)
          throw Invalid(shape, meshIndex,
            $"stored index count {source.StoredIndexCount} does not match " +
            $"the {physicalCount} decoded indices");
        break;
      case StaticShapeIndexLayout.PlacementTriangleList:
        if (source.PlacementSortPermutations.Count != 0)
          throw Invalid(shape, meshIndex,
            "unsorted placement list unexpectedly carries sort permutations");
        var expectedCount = Convert.ToUInt64(source.StoredIndexCount) * 3;
        if (physicalCount != expectedCount)
          throw Invalid(shape, meshIndex,
            $"stored placement triangle count {source.StoredIndexCount} requires " +
            $"{expectedCount} decoded indices, but found {physicalCount}");
        break;
      case StaticShapeIndexLayout.PlacementSortedTriangleList:
        if (physicalCount != source.StoredIndexCount)
          throw Invalid(shape, meshIndex,
            $"stored sorted-placement index count {source.StoredIndexCount} does not match " +
            $"the {physicalCount} retained permutation indices");
        ValidatePlacementSortPermutations(shape, source, meshIndex);
        break;
      default:
        throw Invalid(shape, meshIndex, $"has unknown index layout {source.IndexLayout}");
    }
  }

  private static void ValidatePlacementSortPermutations(
    BoneShape shape,
    BoneShapeMesh source,
    int meshIndex
  ) {
    if (source.StoredIndexCount % 3 != 0)
      throw Invalid(shape, meshIndex,
        $"stored sorted-placement index count {source.StoredIndexCount} is not a triangle list");
    if (source.PlacementSortPermutations.Count != 3)
      throw Invalid(shape, meshIndex,
        $"has {source.PlacementSortPermutations.Count} placement sort permutations, expected 3");
    if (source.PlacementSortPermutations[0] == null)
      throw Invalid(shape, meshIndex, "placement sort permutation 0 is null");
    if (!source.Indices.SequenceEqual(source.PlacementSortPermutations[0]))
      throw Invalid(shape, meshIndex,
        "default indices do not match the first placement sort permutation");

    BoneShapeTriangle[]? expectedTriangles = null;
    var permutationIndex = 0;
    foreach (var permutation in source.PlacementSortPermutations) {
      if (permutation == null || Convert.ToUInt64(permutation.Count) != source.StoredIndexCount)
        throw Invalid(shape, meshIndex,
          $"placement sort permutation {permutationIndex} does not match stored index count " +
          $"{source.StoredIndexCount}");
      var indexOffset = 0;
      foreach (var vertexIndex in permutation) {
        ValidateIndex(shape, meshIndex, vertexIndex, indexOffset, source.Vertices.Count);
        indexOffset++;
      }
      var triangles = ReadSortedTriangles(permutation);
      expectedTriangles ??= triangles;
      if (!expectedTriangles.AsSpan().SequenceEqual(triangles))
        throw Invalid(shape, meshIndex,
          $"placement sort permutation {permutationIndex} does not contain the same triangles");
      permutationIndex++;
    }
  }

  private static BoneShapeTriangle[] ReadSortedTriangles(IReadOnlyList<uint> indices) {
    var triangles = new BoneShapeTriangle[indices.Count / 3];
    for (var triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++) {
      var indexOffset = triangleIndex * 3;
      triangles[triangleIndex] = new BoneShapeTriangle(
        indices[indexOffset], indices[indexOffset + 1], indices[indexOffset + 2]);
    }
    Array.Sort(triangles);
    return triangles;
  }

  private static void ValidateIndex(
    BoneShape shape,
    int meshIndex,
    uint vertexIndex,
    int indexOffset,
    int vertexCount
  ) {
    if (vertexIndex >= Convert.ToUInt32(vertexCount))
      throw Invalid(shape, meshIndex,
        $"index {indexOffset} references vertex {vertexIndex}, but only " +
        $"{vertexCount} vertices exist");
  }

  private static Vector3 ToOpenRct3Coordinates(Vector3 value) =>
    new(value.X, value.Z, value.Y);

  private static bool IsFinite(Vector2 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static InvalidDataException Invalid(
    BoneShape shape,
    int? meshIndex,
    string message
  ) {
    var mesh = meshIndex.HasValue ? $" mesh {meshIndex.Value}" : string.Empty;
    return new InvalidDataException($"Bone shape '{shape.Name}'{mesh} {message}.");
  }

  private readonly record struct BoneShapeTriangle(uint A, uint B, uint C)
    : IComparable<BoneShapeTriangle> {
    public int CompareTo(BoneShapeTriangle other) {
      var a = A.CompareTo(other.A);
      if (a != 0) return a;
      var b = B.CompareTo(other.B);
      return b != 0 ? b : C.CompareTo(other.C);
    }
  }
}
