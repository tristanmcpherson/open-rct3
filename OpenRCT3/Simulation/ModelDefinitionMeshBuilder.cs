// Model Definition Mesh Builder
//
// Copyright Â© 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Builds neutral static render meshes from decoded RCT3 model resources.</summary>
/// <remarks>
/// MDL FVF <c>0x1305</c> uses the same serialized VERTEX2 payload as BSH. This adapter therefore
/// follows <see cref="BoneShapeMeshBuilder"/> exactly: it retains the decoded rest-pose vertex
/// attributes, maps native RCT3 <c>(X, Y, Z)</c> coordinates to park <c>(X, Z, Y)</c> coordinates,
/// and swaps each triangle's first two indices to preserve counter-clockwise winding. It does not
/// apply bones, infer material references from strings, select variants, or create textures.
/// </remarks>
public static class ModelDefinitionMeshBuilder {
  private const uint SupportedFvf = 0x1305;
  private const ushort SupportedMultiplier = 1;

  /// <summary>
  /// Converts every mesh in every nonempty source group into a caller-owned render batch while
  /// retaining group-major source order. The caller must dispose every returned mesh.
  /// </summary>
  public static IReadOnlyList<ModelDefinitionMeshBatch> BuildBatches(
    ModelDefinition definition
  ) => BuildBatches(definition, ModelDefinitionMeshBuilderOperations.Default);

  internal static IReadOnlyList<ModelDefinitionMeshBatch> BuildBatches(
    ModelDefinition definition,
    ModelDefinitionMeshBuilderOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(definition);
    ArgumentNullException.ThrowIfNull(operations);
    if (string.IsNullOrWhiteSpace(definition.Name))
      throw Invalid(definition, null, null, "has no name");
    if (definition.Bones == null)
      throw Invalid(definition, null, null, "has a null bone list");
    if (Convert.ToUInt64(definition.Bones.Count) != definition.BoneCount)
      throw Invalid(
        definition,
        null,
        null,
        $"bone count {definition.BoneCount} does not match " +
        $"the {definition.Bones.Count} decoded bones");
    if (definition.Groups == null)
      throw Invalid(definition, null, null, "has a null group list");

    var batches = new List<ModelDefinitionMeshBatch>();
    var ownedMeshes = new List<Mesh>();
    var ownedMeshSet = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    try {
      foreach (var groupIndex in Enumerable.Range(0, definition.Groups.Count)) {
        var group = definition.Groups[groupIndex];
        if (group == null)
          throw Invalid(definition, groupIndex, null, "is null");
        ValidateGroup(definition, group, groupIndex);
        foreach (var meshIndex in Enumerable.Range(0, group.Meshes.Count)) {
          var source = group.Meshes[meshIndex];
          if (source == null)
            throw Invalid(definition, groupIndex, meshIndex, "is null");
          var batch = BuildBatch(
            definition,
            source,
            groupIndex,
            meshIndex,
            operations,
            ownedMeshes,
            ownedMeshSet);
          batches.Add(batch);
        }
      }
      return batches.ToArray();
    } catch (Exception buildError) {
      var cleanupErrors = DisposeMeshes(ownedMeshes, operations);
      if (cleanupErrors.Count != 0)
        throw new AggregateException([buildError, .. cleanupErrors]);
      throw;
    }
  }

  private static void ValidateGroup(
    ModelDefinition definition,
    ModelGroup group,
    int groupIndex
  ) {
    if (group.Meshes == null)
      throw Invalid(definition, groupIndex, null, "has a null mesh list");
    if (group.Meshes.Count != group.MeshCount)
      throw Invalid(
        definition,
        groupIndex,
        null,
        $"mesh count {group.MeshCount} does not match " +
        $"the {group.Meshes.Count} decoded meshes");
    if (group.SurfaceRecords == null)
      throw Invalid(definition, groupIndex, null, "has a null surface-record list");
    if (group.SurfaceRecords.Count != group.SurfaceRecordCount)
      throw Invalid(
        definition,
        groupIndex,
        null,
        $"surface-record count {group.SurfaceRecordCount} does not match " +
        $"the {group.SurfaceRecords.Count} decoded records");
  }

  private static ModelDefinitionMeshBatch BuildBatch(
    ModelDefinition definition,
    ModelMesh source,
    int groupIndex,
    int meshIndex,
    ModelDefinitionMeshBuilderOperations operations,
    List<Mesh> ownedMeshes,
    HashSet<Mesh> ownedMeshSet
  ) {
    if (source.Fvf != SupportedFvf)
      throw Invalid(
        definition,
        groupIndex,
        meshIndex,
        $"uses unsupported FVF 0x{source.Fvf:X}");
    if (source.Multiplier != SupportedMultiplier)
      throw Invalid(
        definition,
        groupIndex,
        meshIndex,
        $"uses unsupported multiplier {source.Multiplier}");
    if (source.Vertices == null || source.Vertices.Count == 0)
      throw Invalid(definition, groupIndex, meshIndex, "contains no vertices");
    if (source.Vertices.Count != source.StoredVertexCount)
      throw Invalid(
        definition,
        groupIndex,
        meshIndex,
        $"stored vertex count {source.StoredVertexCount} does not match " +
        $"the {source.Vertices.Count} decoded vertices");
    if (source.Indices == null || source.Indices.Count == 0)
      throw Invalid(definition, groupIndex, meshIndex, "contains no indices");
    if (Convert.ToUInt64(source.Indices.Count) != source.StoredIndexCount)
      throw Invalid(
        definition,
        groupIndex,
        meshIndex,
        $"stored index count {source.StoredIndexCount} does not match " +
        $"the {source.Indices.Count} decoded indices");
    if (source.Indices.Count % 3 != 0)
      throw Invalid(
        definition,
        groupIndex,
        meshIndex,
        $"has {source.Indices.Count} indices, not a triangle list");

    var vertices = new List<Vertex>(source.Vertices.Count);
    foreach (var vertexIndex in Enumerable.Range(0, source.Vertices.Count)) {
      var sourceVertex = source.Vertices[vertexIndex];
      if (sourceVertex == null)
        throw Invalid(
          definition,
          groupIndex,
          meshIndex,
          $"vertex {vertexIndex} is null");
      if (!IsFinite(sourceVertex.Position) || !IsFinite(sourceVertex.Normal) ||
          !IsFinite(sourceVertex.TexCoord) || !IsFinite(sourceVertex.Color))
        throw Invalid(
          definition,
          groupIndex,
          meshIndex,
          $"vertex {vertexIndex} contains a non-finite value");

      var position = ToOpenRct3Coordinates(sourceVertex.Position);
      var normal = ToOpenRct3Coordinates(sourceVertex.Normal);
      if (!IsFinite(position) || !IsFinite(normal))
        throw Invalid(
          definition,
          groupIndex,
          meshIndex,
          $"vertex {vertexIndex} transforms to a non-finite value");
      vertices.Add(new Vertex {
        Position = position,
        Normal = normal,
        TexCoord = sourceVertex.TexCoord,
        Color = sourceVertex.Color,
      });
    }

    var indices = new List<uint>(source.Indices.Count);
    for (var index = 0; index < source.Indices.Count; index += 3) {
      ValidateIndex(
        definition, groupIndex, meshIndex, source.Indices[index], index, vertices.Count);
      ValidateIndex(
        definition, groupIndex, meshIndex, source.Indices[index + 1], index + 1,
        vertices.Count);
      ValidateIndex(
        definition, groupIndex, meshIndex, source.Indices[index + 2], index + 2,
        vertices.Count);

      // This axis swap changes handedness. Match BSH and SHS by swapping the first two vertices so
      // the transformed triangle remains counter-clockwise.
      indices.Add(source.Indices[index + 1]);
      indices.Add(source.Indices[index]);
      indices.Add(source.Indices[index + 2]);
    }

    var name = $"{definition.Name}/group-{groupIndex}/mesh-{meshIndex}";
    var mesh = operations.CreateMesh(vertices, indices, name);
    if (mesh == null)
      throw Invalid(definition, groupIndex, meshIndex, "mesh factory returned null");
    if (mesh.State == State.Disposed)
      throw Invalid(definition, groupIndex, meshIndex, "mesh factory returned a disposed mesh");
    if (!ownedMeshSet.Add(mesh))
      throw Invalid(definition, groupIndex, meshIndex, "mesh factory reused a mesh instance");
    ownedMeshes.Add(mesh);
    mesh.Name = name;
    return new ModelDefinitionMeshBatch(groupIndex, meshIndex, name, mesh);
  }

  private static void ValidateIndex(
    ModelDefinition definition,
    int groupIndex,
    int meshIndex,
    uint vertexIndex,
    int indexOffset,
    int vertexCount
  ) {
    if (vertexIndex >= Convert.ToUInt32(vertexCount))
      throw Invalid(
        definition,
        groupIndex,
        meshIndex,
        $"index {indexOffset} references vertex {vertexIndex}, but only " +
        $"{vertexCount} vertices exist");
  }

  private static List<Exception> DisposeMeshes(
    IReadOnlyList<Mesh> meshes,
    ModelDefinitionMeshBuilderOperations operations
  ) {
    var errors = new List<Exception>();
    for (var index = meshes.Count - 1; index >= 0; index--) {
      try {
        operations.DisposeMesh(meshes[index]);
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
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
    ModelDefinition definition,
    int? groupIndex,
    int? meshIndex,
    string message
  ) {
    var group = groupIndex.HasValue ? $" group {groupIndex.Value}" : string.Empty;
    var mesh = meshIndex.HasValue ? $" mesh {meshIndex.Value}" : string.Empty;
    return new InvalidDataException($"Model '{definition.Name}'{group}{mesh} {message}.");
  }
}

/// <summary>A source MDL group/mesh and its caller-owned neutral render mesh.</summary>
public sealed record ModelDefinitionMeshBatch(
  int SourceGroupIndex,
  int SourceMeshIndex,
  string SourceMeshName,
  Mesh Mesh
);

internal sealed record ModelDefinitionMeshBuilderOperations(
  Func<List<Vertex>, List<uint>, string, Mesh> CreateMesh,
  Action<Mesh> DisposeMesh
) {
  public static ModelDefinitionMeshBuilderOperations Default { get; } = new(
    (vertices, indices, name) => new Mesh(vertices, indices) { Name = name },
    mesh => mesh.Dispose());
}
