// Terrain Blend Layer Mesh Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Builds one top-face mesh for every participating terrain surface layer.</summary>
/// <remarks>
/// This builder only packages the proven CPU weights into geometry. It does not choose or infer a
/// framebuffer blend equation. The caller owns every returned mesh.
/// </remarks>
public static class TerrainBlendLayerMeshBuilder {
  private const int VerticesPerTileLayer = 4;
  private const int IndicesPerTileLayer = 6;

  /// <summary>Builds TER-scaled surface-layer batches using the terrain's installed catalog.</summary>
  public static IReadOnlyList<TerrainBlendLayerMeshBatch> BuildBatches(
    Terrain terrain,
    Vector4 color,
    string name = "Terrain Blend"
  ) {
    ArgumentNullException.ThrowIfNull(terrain);
    var catalog = terrain.TextureCatalog
      ?? throw new InvalidOperationException("Terrain has no texture catalog.");
    return BuildBatches(
      terrain,
      color,
      catalog.GetSurfaceKind,
      index => {
        var parameters = catalog.GetSurfaceParameters(index);
        return new Vector2(parameters.InvWidth, parameters.InvHeight);
      },
      TerrainBlendLayerMeshBuilderOperations.Default,
      name);
  }

  internal static IReadOnlyList<TerrainBlendLayerMeshBatch> BuildBatches(
    Terrain terrain,
    Vector4 color,
    Func<byte, TerrainTypeKind> getSurfaceKind,
    Func<byte, Vector2> getTextureScale,
    TerrainBlendLayerMeshBuilderOperations operations,
    string name = "Terrain Blend"
  ) {
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(getSurfaceKind);
    ArgumentNullException.ThrowIfNull(getTextureScale);
    ArgumentNullException.ThrowIfNull(operations);
    ArgumentNullException.ThrowIfNull(operations.CreateMesh);
    ArgumentNullException.ThrowIfNull(operations.DisposeMesh);
    if (!IsFinite(color))
      throw new ArgumentOutOfRangeException(nameof(color), "Terrain tint must be finite.");
    if (string.IsNullOrWhiteSpace(name))
      throw new ArgumentException("Terrain blend mesh name cannot be empty.", nameof(name));

    var geometry = new SortedDictionary<
      (TerrainBlendLayerPassRole Role, byte SurfaceIndex), MeshGeometry>();
    var textureScales = new Dictionary<byte, Vector2>();
    for (var tileY = 0; tileY < terrain.Height; tileY++) {
      for (var tileX = 0; tileX < terrain.Width; tileX++) {
        var layers = TerrainBlendWeightBuilder.Build(
          terrain, tileX, tileY, getSurfaceKind);
        var currentSurface = terrain.GetCorner(
          tileX, tileY, TerrainCornerSlot.SouthWest).SurfaceIndex;
        foreach (var layer in layers) {
          if (IsZero(layer)) continue;
          var role = layer.SurfaceIndex == currentSurface
            ? TerrainBlendLayerPassRole.Base
            : TerrainBlendLayerPassRole.Contribution;
          var batch = GetGeometry(
            geometry, textureScales, role, layer.SurfaceIndex, getTextureScale);
          AddTopFace(terrain, tileX, tileY, color, layer, batch);
        }
      }
    }

    var batches = new List<TerrainBlendLayerMeshBatch>(geometry.Count);
    var ownedMeshes = new List<Mesh>(geometry.Count);
    var ownedMeshSet = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    try {
      foreach (var (key, batch) in geometry) {
        var meshName = $"{name} {key.Role} Surface {key.SurfaceIndex}";
        var mesh = operations.CreateMesh(batch.Vertices, batch.Indices, meshName);
        if (mesh == null)
          throw Invalid(key.SurfaceIndex, "mesh factory returned null");
        if (mesh.State == State.Disposed)
          throw Invalid(key.SurfaceIndex, "mesh factory returned a disposed mesh");
        if (!ownedMeshSet.Add(mesh))
          throw Invalid(key.SurfaceIndex, "mesh factory reused a mesh instance");
        ownedMeshes.Add(mesh);
        mesh.Name = meshName;
        batches.Add(new TerrainBlendLayerMeshBatch(
          key.Role, key.SurfaceIndex, mesh));
      }
      return batches.ToArray();
    } catch (Exception buildError) {
      var cleanupErrors = DisposeMeshes(ownedMeshes, operations.DisposeMesh);
      if (cleanupErrors.Count != 0)
        throw new AggregateException([buildError, .. cleanupErrors]);
      throw;
    }
  }

  private static MeshGeometry GetGeometry(
    IDictionary<(TerrainBlendLayerPassRole Role, byte SurfaceIndex), MeshGeometry> geometry,
    IDictionary<byte, Vector2> textureScales,
    TerrainBlendLayerPassRole role,
    byte surfaceIndex,
    Func<byte, Vector2> getTextureScale
  ) {
    var key = (role, surfaceIndex);
    if (geometry.TryGetValue(key, out var batch)) return batch;
    if (!textureScales.TryGetValue(surfaceIndex, out var textureScale)) {
      textureScale = getTextureScale(surfaceIndex);
      if (!IsFinite(textureScale) || textureScale.X <= 0f || textureScale.Y <= 0f)
        throw Invalid(surfaceIndex, "has an invalid texture scale");
      textureScales.Add(surfaceIndex, textureScale);
    }
    batch = new MeshGeometry(textureScale);
    geometry.Add(key, batch);
    return batch;
  }

  private static void AddTopFace(
    Terrain terrain,
    int tileX,
    int tileY,
    Vector4 color,
    TerrainBlendLayer layer,
    MeshGeometry geometry
  ) {
    Reserve(geometry, layer.SurfaceIndex);
    var sw = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.SouthWest);
    var se = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.SouthEast);
    var nw = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.NorthWest);
    var ne = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.NorthEast);
    if (!IsFinite(sw) || !IsFinite(se) || !IsFinite(nw) || !IsFinite(ne))
      throw Invalid(layer.SurfaceIndex, $"tile ({tileX}, {tileY}) has a non-finite position");

    // RCT3 splits terrain cells along SouthEast-to-NorthWest: (SW, SE, NW), (NE, NW, SE).
    var southWestNormal = Vector3.Normalize(Vector3.Cross(se - sw, nw - sw));
    var northEastNormal = Vector3.Normalize(Vector3.Cross(nw - ne, se - ne));
    var sharedNormal = Vector3.Normalize(southWestNormal + northEastNormal);
    if (!IsFinite(southWestNormal) || !IsFinite(northEastNormal) || !IsFinite(sharedNormal))
      throw Invalid(layer.SurfaceIndex, $"tile ({tileX}, {tileY}) has an invalid normal");

    var baseIndex = Convert.ToUInt32(geometry.Vertices.Count);
    AddVertex(
      terrain, geometry, sw, southWestNormal, color, layer.SouthWestWeight,
      layer.SurfaceIndex, tileX, tileY);
    AddVertex(
      terrain, geometry, se, sharedNormal, color, layer.SouthEastWeight,
      layer.SurfaceIndex, tileX, tileY);
    AddVertex(
      terrain, geometry, ne, northEastNormal, color, layer.NorthEastWeight,
      layer.SurfaceIndex, tileX, tileY);
    AddVertex(
      terrain, geometry, nw, sharedNormal, color, layer.NorthWestWeight,
      layer.SurfaceIndex, tileX, tileY);
    geometry.Indices.AddRange([
      baseIndex,
      baseIndex + 1,
      baseIndex + 3,
      baseIndex + 1,
      baseIndex + 2,
      baseIndex + 3,
    ]);
  }

  private static void AddVertex(
    Terrain terrain,
    MeshGeometry geometry,
    Vector3 position,
    Vector3 normal,
    Vector4 color,
    byte weight,
    byte surfaceIndex,
    int tileX,
    int tileY
  ) {
    var texCoord = new Vector2(
      (position.X - terrain.Origin.X) * geometry.TextureScale.X,
      (position.Y - terrain.Origin.Y) * geometry.TextureScale.Y);
    var weightedColor = new Vector4(
      color.X,
      color.Y,
      color.Z,
      Convert.ToSingle(weight) / byte.MaxValue);
    if (!IsFinite(texCoord) || !IsFinite(weightedColor))
      throw Invalid(
        surfaceIndex, $"tile ({tileX}, {tileY}) has a non-finite vertex attribute");
    geometry.Vertices.Add(new Vertex {
      Position = position,
      Normal = normal,
      TexCoord = texCoord,
      Color = weightedColor,
    });
  }

  private static Vector3 CornerPosition(
    Terrain terrain,
    int tileX,
    int tileY,
    TerrainCornerSlot slot
  ) {
    var (dx, dy) = slot switch {
      TerrainCornerSlot.SouthWest => (0, 0),
      TerrainCornerSlot.SouthEast => (1, 0),
      TerrainCornerSlot.NorthWest => (0, 1),
      TerrainCornerSlot.NorthEast => (1, 1),
      _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };
    return new Vector3(
      terrain.Origin.X + ((tileX + dx) * terrain.TileSize.X),
      terrain.Origin.Y + ((tileY + dy) * terrain.TileSize.Y),
      Terrain.CornerHeightToWorldZ(terrain.GetCorner(tileX, tileY, slot).Height));
  }

  private static void Reserve(MeshGeometry geometry, byte surfaceIndex) {
    if (geometry.Vertices.Count > int.MaxValue - VerticesPerTileLayer)
      throw Invalid(surfaceIndex, "exceeds the vertex index budget");
    if (geometry.Indices.Count > int.MaxValue - IndicesPerTileLayer)
      throw Invalid(surfaceIndex, "exceeds the index budget");
  }

  private static List<Exception> DisposeMeshes(
    IReadOnlyList<Mesh> meshes,
    Action<Mesh> disposeMesh
  ) {
    var errors = new List<Exception>();
    for (var index = meshes.Count - 1; index >= 0; index--) {
      try {
        disposeMesh(meshes[index]);
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private static bool IsZero(TerrainBlendLayer layer) =>
    layer.SouthWestWeight == 0 && layer.SouthEastWeight == 0 &&
    layer.NorthWestWeight == 0 && layer.NorthEastWeight == 0;

  private static bool IsFinite(Vector2 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static InvalidDataException Invalid(byte surfaceIndex, string message) =>
    new($"Terrain blend surface {surfaceIndex} {message}.");

  private sealed class MeshGeometry(Vector2 textureScale) {
    public Vector2 TextureScale { get; } = textureScale;
    public List<Vertex> Vertices { get; } = [];
    public List<uint> Indices { get; } = [];
  }
}

/// <summary>The ordered rendering role of a terrain blend-layer mesh.</summary>
public enum TerrainBlendLayerPassRole {
  Base,
  Contribution,
}

/// <summary>A caller-owned mesh containing one terrain surface's weighted top faces.</summary>
public sealed record TerrainBlendLayerMeshBatch(
  TerrainBlendLayerPassRole Role,
  byte SurfaceIndex,
  Mesh Mesh
);

internal sealed record TerrainBlendLayerMeshBuilderOperations(
  Func<List<Vertex>, List<uint>, string, Mesh> CreateMesh,
  Action<Mesh> DisposeMesh
) {
  public static TerrainBlendLayerMeshBuilderOperations Default { get; } = new(
    (vertices, indices, name) => new Mesh(vertices, indices) { Name = name },
    mesh => mesh.Dispose());
}
