// Model Animation Frame-Zero Mesh Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Builds caller-owned CPU-skinned MDL meshes from one exact frame-zero pose.</summary>
/// <remarks>
/// MDL FVF <c>0x1305</c> stores four zero-based MDL bone-array indices followed by four byte
/// weights whose exact total is 255. RCT3 uses row-vector matrices, so each native position is
/// transformed by the selected pose bone's <see cref="ModelAnimationFrameZeroBonePose.SkinTransform"/>
/// before the weighted results are combined. Normals use the same matrices without translation.
/// The native <c>(X, Y, Z)</c> to park <c>(X, Z, Y)</c> conversion is applied once after skinning.
/// </remarks>
internal static class ModelAnimationFrameZeroMeshBuilder {
  private const uint SupportedFvf = 0x1305;
  private const ushort SupportedMultiplier = 1;
  private const int SerializedWeightTotal = 255;

  /// <summary>
  /// Converts every MDL mesh into a caller-owned frame-zero render batch in group-major order.
  /// The caller must dispose every returned mesh.
  /// </summary>
  internal static IReadOnlyList<ModelDefinitionMeshBatch> BuildBatches(
    ModelAnimationFrameZeroPose pose
  ) => BuildBatches(pose, ModelAnimationFrameZeroMeshBuilderOperations.Default);

  internal static IReadOnlyList<ModelDefinitionMeshBatch> BuildBatches(
    ModelAnimationFrameZeroPose pose,
    ModelAnimationFrameZeroMeshBuilderOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(pose);
    ArgumentNullException.ThrowIfNull(operations);
    var model = ValidatePose(pose);

    var batches = new List<ModelDefinitionMeshBatch>();
    var ownedMeshes = new List<Mesh>();
    var ownedMeshSet = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    try {
      foreach (var groupIndex in Enumerable.Range(0, model.Groups.Count)) {
        var group = model.Groups[groupIndex];
        if (group == null)
          throw Invalid(pose, groupIndex, null, "group is null");
        ValidateGroup(pose, group, groupIndex);
        foreach (var meshIndex in Enumerable.Range(0, group.Meshes.Count)) {
          var source = group.Meshes[meshIndex];
          if (source == null)
            throw Invalid(pose, groupIndex, meshIndex, "mesh is null");
          batches.Add(BuildBatch(
            pose,
            source,
            groupIndex,
            meshIndex,
            operations,
            ownedMeshes,
            ownedMeshSet));
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

  private static ModelDefinition ValidatePose(ModelAnimationFrameZeroPose pose) {
    var model = pose.Model
      ?? throw new InvalidDataException("Cannot build frame-zero mesh: pose has no MDL.");
    if (pose.Animation == null)
      throw Invalid(pose, null, null, "pose has no animation");
    if (string.IsNullOrWhiteSpace(model.Name))
      throw Invalid(pose, null, null, "MDL has no name");
    if (string.IsNullOrWhiteSpace(pose.Animation.Name))
      throw Invalid(pose, null, null, "animation has no name");
    if (model.Bones == null)
      throw Invalid(pose, null, null, "MDL has a null bone list");
    if (Convert.ToUInt64(model.Bones.Count) != model.BoneCount)
      throw Invalid(
        pose,
        null,
        null,
        $"MDL bone count {model.BoneCount} does not match " +
        $"the {model.Bones.Count} decoded bones");
    if (pose.Bones == null || pose.Bones.Count != model.Bones.Count)
      throw Invalid(pose, null, null, "pose bone list is missing or differs from the MDL");
    if (pose.TranslatedBoneCount < 0 || pose.TranslatedBoneCount > pose.Bones.Count ||
        pose.RotatedBoneCount < 0 || pose.RotatedBoneCount > pose.Bones.Count)
      throw Invalid(pose, null, null, "pose channel counts are outside the MDL skeleton");

    foreach (var index in Enumerable.Range(0, pose.Bones.Count)) {
      var bonePose = pose.Bones[index];
      if (bonePose == null)
        throw Invalid(pose, null, null, $"pose bone {index} is null");
      if (bonePose.ModelBoneIndex != index ||
          bonePose.Bone == null ||
          !ReferenceEquals(bonePose.Bone, model.Bones[index]))
        throw Invalid(pose, null, null, $"pose bone {index} does not match MDL bone {index}");
      if (!IsFinite(bonePose.LocalTransform) ||
          !IsFinite(bonePose.WorldTransform) ||
          !IsFinite(bonePose.SkinTransform))
        throw Invalid(pose, null, null, $"pose bone {index} contains a non-finite matrix");
    }
    if (model.Groups == null)
      throw Invalid(pose, null, null, "MDL has a null group list");
    return model;
  }

  private static void ValidateGroup(
    ModelAnimationFrameZeroPose pose,
    ModelGroup group,
    int groupIndex
  ) {
    if (group.Meshes == null)
      throw Invalid(pose, groupIndex, null, "group has a null mesh list");
    if (group.Meshes.Count != group.MeshCount)
      throw Invalid(
        pose,
        groupIndex,
        null,
        $"group mesh count {group.MeshCount} does not match " +
        $"the {group.Meshes.Count} decoded meshes");
    if (group.SurfaceRecords == null)
      throw Invalid(pose, groupIndex, null, "group has a null surface-record list");
    if (group.SurfaceRecords.Count != group.SurfaceRecordCount)
      throw Invalid(
        pose,
        groupIndex,
        null,
        $"group surface-record count {group.SurfaceRecordCount} does not match " +
        $"the {group.SurfaceRecords.Count} decoded records");
  }

  private static ModelDefinitionMeshBatch BuildBatch(
    ModelAnimationFrameZeroPose pose,
    ModelMesh source,
    int groupIndex,
    int meshIndex,
    ModelAnimationFrameZeroMeshBuilderOperations operations,
    List<Mesh> ownedMeshes,
    HashSet<Mesh> ownedMeshSet
  ) {
    ValidateMesh(pose, source, groupIndex, meshIndex);

    var vertices = new List<Vertex>(source.Vertices.Count);
    foreach (var vertexIndex in Enumerable.Range(0, source.Vertices.Count)) {
      var sourceVertex = source.Vertices[vertexIndex];
      if (sourceVertex == null)
        throw Invalid(pose, groupIndex, meshIndex, $"vertex {vertexIndex} is null");
      vertices.Add(SkinVertex(pose, sourceVertex, groupIndex, meshIndex, vertexIndex));
    }

    var indices = new List<uint>(source.Indices.Count);
    for (var index = 0; index < source.Indices.Count; index += 3) {
      ValidateIndex(
        pose, groupIndex, meshIndex, source.Indices[index], index, vertices.Count);
      ValidateIndex(
        pose, groupIndex, meshIndex, source.Indices[index + 1], index + 1,
        vertices.Count);
      ValidateIndex(
        pose, groupIndex, meshIndex, source.Indices[index + 2], index + 2,
        vertices.Count);

      // The native-to-park axis swap changes handedness, so retain the established MDL winding.
      indices.Add(source.Indices[index + 1]);
      indices.Add(source.Indices[index]);
      indices.Add(source.Indices[index + 2]);
    }

    var name = $"{pose.Model.Name}/group-{groupIndex}/mesh-{meshIndex}";
    var mesh = operations.CreateMesh(vertices, indices, name);
    if (mesh == null)
      throw Invalid(pose, groupIndex, meshIndex, "mesh factory returned null");
    if (mesh.State == State.Disposed)
      throw Invalid(pose, groupIndex, meshIndex, "mesh factory returned a disposed mesh");
    if (!ownedMeshSet.Add(mesh))
      throw Invalid(pose, groupIndex, meshIndex, "mesh factory reused a mesh instance");
    ownedMeshes.Add(mesh);
    mesh.Name = name;
    return new ModelDefinitionMeshBatch(groupIndex, meshIndex, name, mesh);
  }

  private static void ValidateMesh(
    ModelAnimationFrameZeroPose pose,
    ModelMesh source,
    int groupIndex,
    int meshIndex
  ) {
    if (source.Fvf != SupportedFvf)
      throw Invalid(
        pose, groupIndex, meshIndex, $"mesh uses unsupported FVF 0x{source.Fvf:X}");
    if (source.Multiplier != SupportedMultiplier)
      throw Invalid(
        pose, groupIndex, meshIndex, $"mesh uses unsupported multiplier {source.Multiplier}");
    if (source.Vertices == null || source.Vertices.Count == 0)
      throw Invalid(pose, groupIndex, meshIndex, "mesh contains no vertices");
    if (source.Vertices.Count != source.StoredVertexCount)
      throw Invalid(
        pose,
        groupIndex,
        meshIndex,
        $"mesh stored vertex count {source.StoredVertexCount} does not match " +
        $"the {source.Vertices.Count} decoded vertices");
    if (source.Indices == null || source.Indices.Count == 0)
      throw Invalid(pose, groupIndex, meshIndex, "mesh contains no indices");
    if (Convert.ToUInt64(source.Indices.Count) != source.StoredIndexCount)
      throw Invalid(
        pose,
        groupIndex,
        meshIndex,
        $"mesh stored index count {source.StoredIndexCount} does not match " +
        $"the {source.Indices.Count} decoded indices");
    if (source.Indices.Count % 3 != 0)
      throw Invalid(
        pose,
        groupIndex,
        meshIndex,
        $"mesh has {source.Indices.Count} indices, not a triangle list");
  }

  private static Vertex SkinVertex(
    ModelAnimationFrameZeroPose pose,
    BoneShapeVertex source,
    int groupIndex,
    int meshIndex,
    int vertexIndex
  ) {
    if (!IsFinite(source.Position) || !IsFinite(source.Normal) ||
        !IsFinite(source.TexCoord) || !IsFinite(source.Color))
      throw Invalid(
        pose, groupIndex, meshIndex, $"vertex {vertexIndex} contains a non-finite value");

    var skinning = source.Skinning;
    var weightTotal = Convert.ToInt32(skinning.Weight0) + skinning.Weight1 +
      skinning.Weight2 + skinning.Weight3;
    if (weightTotal != SerializedWeightTotal)
      throw Invalid(
        pose,
        groupIndex,
        meshIndex,
        $"vertex {vertexIndex} weights total {weightTotal}, expected {SerializedWeightTotal}");

    var nativePosition = Vector3.Zero;
    var nativeNormal = Vector3.Zero;
    AccumulateInfluence(
      pose, source, groupIndex, meshIndex, vertexIndex, 0,
      skinning.Bone0, skinning.Weight0, ref nativePosition, ref nativeNormal);
    AccumulateInfluence(
      pose, source, groupIndex, meshIndex, vertexIndex, 1,
      skinning.Bone1, skinning.Weight1, ref nativePosition, ref nativeNormal);
    AccumulateInfluence(
      pose, source, groupIndex, meshIndex, vertexIndex, 2,
      skinning.Bone2, skinning.Weight2, ref nativePosition, ref nativeNormal);
    AccumulateInfluence(
      pose, source, groupIndex, meshIndex, vertexIndex, 3,
      skinning.Bone3, skinning.Weight3, ref nativePosition, ref nativeNormal);

    if (!IsFinite(nativePosition) || !IsFinite(nativeNormal))
      throw Invalid(
        pose, groupIndex, meshIndex, $"vertex {vertexIndex} skins to a non-finite value");
    var normalLengthSquared = nativeNormal.LengthSquared();
    if (!float.IsFinite(normalLengthSquared) || normalLengthSquared <= 0f)
      throw Invalid(
        pose, groupIndex, meshIndex, $"vertex {vertexIndex} skins to a zero or invalid normal");
    nativeNormal /= MathF.Sqrt(normalLengthSquared);

    var position = ToOpenRct3Coordinates(nativePosition);
    var normal = ToOpenRct3Coordinates(nativeNormal);
    if (!IsFinite(position) || !IsFinite(normal))
      throw Invalid(
        pose, groupIndex, meshIndex, $"vertex {vertexIndex} converts to a non-finite value");
    return new Vertex {
      Position = position,
      Normal = normal,
      TexCoord = source.TexCoord,
      Color = source.Color,
    };
  }

  private static void AccumulateInfluence(
    ModelAnimationFrameZeroPose pose,
    BoneShapeVertex source,
    int groupIndex,
    int meshIndex,
    int vertexIndex,
    int slot,
    byte boneIndex,
    byte weight,
    ref Vector3 position,
    ref Vector3 normal
  ) {
    if (weight == 0) return;
    if (boneIndex == byte.MaxValue)
      throw Invalid(
        pose,
        groupIndex,
        meshIndex,
        $"vertex {vertexIndex} bone slot {slot} uses the weighted -1 sentinel");
    if (boneIndex >= pose.Bones.Count)
      throw Invalid(
        pose,
        groupIndex,
        meshIndex,
        $"vertex {vertexIndex} bone slot {slot} references {boneIndex}, " +
        $"but the MDL has {pose.Bones.Count} bones");

    // BoneNumber is unrelated metadata. VERTEX2 stores a direct zero-based MDL Bones[] index.
    var transform = pose.Bones[boneIndex].SkinTransform;
    var transformedPosition = Vector3.Transform(source.Position, transform);
    var transformedNormal = Vector3.TransformNormal(source.Normal, transform);
    if (!IsFinite(transformedPosition) || !IsFinite(transformedNormal))
      throw Invalid(
        pose,
        groupIndex,
        meshIndex,
        $"vertex {vertexIndex} bone slot {slot} produces a non-finite value");
    var normalizedWeight = Convert.ToSingle(weight) / SerializedWeightTotal;
    position += transformedPosition * normalizedWeight;
    normal += transformedNormal * normalizedWeight;
  }

  private static void ValidateIndex(
    ModelAnimationFrameZeroPose pose,
    int groupIndex,
    int meshIndex,
    uint vertexIndex,
    int indexOffset,
    int vertexCount
  ) {
    if (vertexIndex >= Convert.ToUInt32(vertexCount))
      throw Invalid(
        pose,
        groupIndex,
        meshIndex,
        $"index {indexOffset} references vertex {vertexIndex}, but only " +
        $"{vertexCount} vertices exist");
  }

  private static List<Exception> DisposeMeshes(
    IReadOnlyList<Mesh> meshes,
    ModelAnimationFrameZeroMeshBuilderOperations operations
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

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  private static InvalidDataException Invalid(
    ModelAnimationFrameZeroPose pose,
    int? groupIndex,
    int? meshIndex,
    string message
  ) {
    var group = groupIndex.HasValue ? $" group {groupIndex.Value}" : string.Empty;
    var mesh = meshIndex.HasValue ? $" mesh {meshIndex.Value}" : string.Empty;
    return new InvalidDataException(
      $"Cannot build frame-zero mesh for animation " +
      $"'{pose.Animation?.Name ?? "<unknown>"}' and MDL " +
      $"'{pose.Model?.Name ?? "<unknown>"}'{group}{mesh}: {message}.");
  }
}

internal sealed record ModelAnimationFrameZeroMeshBuilderOperations(
  Func<List<Vertex>, List<uint>, string, Mesh> CreateMesh,
  Action<Mesh> DisposeMesh
) {
  public static ModelAnimationFrameZeroMeshBuilderOperations Default { get; } = new(
    (vertices, indices, name) => new Mesh(vertices, indices) { Name = name },
    mesh => mesh.Dispose());
}
