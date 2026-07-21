// Terrain Raycaster
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Identifies one of the two rendered triangles that make up a terrain tile.</summary>
public enum TerrainTriangle {
  SouthWest,
  NorthEast,
}

/// <summary>A closest visible-terrain intersection in world space.</summary>
public readonly record struct TerrainRaycastHit(
  int TileX,
  int TileY,
  TerrainTriangle Triangle,
  Vector3 Position,
  float Distance
);

/// <summary>Unprojects viewport coordinates and intersects the rendered terrain surface.</summary>
public static class TerrainRaycaster {
  private const float IntersectionEpsilon = 0.000001f;

  /// <summary>
  /// Attempts to intersect a top-left-origin viewport coordinate with the terrain top faces.
  /// </summary>
  /// <param name="terrain">The terrain whose rendered top triangles will be tested.</param>
  /// <param name="viewProjection">
  /// The current row-vector view-projection matrix, normally <c>Scene.Camera.Value</c> after the
  /// camera has been updated for the viewport's aspect ratio.
  /// </param>
  /// <param name="viewportPosition">Pixel position measured from the viewport's top-left.</param>
  /// <param name="viewportSize">Viewport width and height in pixels.</param>
  /// <param name="hit">The nearest terrain hit when this method returns true.</param>
  public static bool TryIntersect(
    Terrain? terrain,
    Matrix4x4? viewProjection,
    Vector2 viewportPosition,
    Vector2 viewportSize,
    out TerrainRaycastHit hit
  ) {
    hit = default;
    if (terrain == null
      || viewProjection is not { } matrix
      || !IsFinite(matrix)
      || !IsFinite(viewportPosition)
      || !IsFinite(viewportSize)
      || viewportSize.X <= 0f
      || viewportSize.Y <= 0f
      || viewportPosition.X < 0f
      || viewportPosition.Y < 0f
      || viewportPosition.X >= viewportSize.X
      || viewportPosition.Y >= viewportSize.Y
      || !Matrix4x4.Invert(matrix, out var inverse))
      return false;

    var ndcX = ((viewportPosition.X / viewportSize.X) * 2f) - 1f;
    var ndcY = 1f - ((viewportPosition.Y / viewportSize.Y) * 2f);
    if (!TryUnproject(new Vector4(ndcX, ndcY, -1f, 1f), inverse, out var near)
      || !TryUnproject(new Vector4(ndcX, ndcY, 1f, 1f), inverse, out var far))
      return false;

    var ray = far - near;
    var rayLength = ray.Length();
    if (!float.IsFinite(rayLength) || rayLength <= IntersectionEpsilon) return false;
    var direction = ray / rayLength;
    var closestDistance = float.PositiveInfinity;

    for (var tileY = 0; tileY < terrain.Height; tileY++) {
      for (var tileX = 0; tileX < terrain.Width; tileX++) {
        var sw = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.SouthWest);
        var se = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.SouthEast);
        var nw = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.NorthWest);
        var ne = CornerPosition(terrain, tileX, tileY, TerrainCornerSlot.NorthEast);

        // Keep this split and winding identical to TerrainMeshBuilder.AddTopFace.
        TryUpdateHit(
          tileX,
          tileY,
          TerrainTriangle.SouthWest,
          sw,
          se,
          nw,
          near,
          direction,
          rayLength,
          ref closestDistance,
          ref hit);
        TryUpdateHit(
          tileX,
          tileY,
          TerrainTriangle.NorthEast,
          se,
          ne,
          nw,
          near,
          direction,
          rayLength,
          ref closestDistance,
          ref hit);
      }
    }

    return float.IsFinite(closestDistance);
  }

  private static void TryUpdateHit(
    int tileX,
    int tileY,
    TerrainTriangle triangle,
    Vector3 a,
    Vector3 b,
    Vector3 c,
    Vector3 origin,
    Vector3 direction,
    float rayLength,
    ref float closestDistance,
    ref TerrainRaycastHit hit
  ) {
    if (!TryIntersectTriangle(origin, direction, a, b, c, out var distance)
      || distance > rayLength
      || distance >= closestDistance)
      return;

    var position = origin + (direction * distance);
    if (!IsFinite(position)) return;
    closestDistance = distance;
    hit = new TerrainRaycastHit(tileX, tileY, triangle, position, distance);
  }

  private static bool TryIntersectTriangle(
    Vector3 origin,
    Vector3 direction,
    Vector3 a,
    Vector3 b,
    Vector3 c,
    out float distance
  ) {
    distance = 0f;
    var edge1 = b - a;
    var edge2 = c - a;
    var perpendicular = Vector3.Cross(direction, edge2);
    var determinant = Vector3.Dot(edge1, perpendicular);
    if (!float.IsFinite(determinant) || MathF.Abs(determinant) <= IntersectionEpsilon)
      return false;

    var inverseDeterminant = 1f / determinant;
    var originOffset = origin - a;
    var u = Vector3.Dot(originOffset, perpendicular) * inverseDeterminant;
    if (!float.IsFinite(u) || u < -IntersectionEpsilon || u > 1f + IntersectionEpsilon)
      return false;

    var perpendicular2 = Vector3.Cross(originOffset, edge1);
    var v = Vector3.Dot(direction, perpendicular2) * inverseDeterminant;
    if (!float.IsFinite(v) || v < -IntersectionEpsilon || u + v > 1f + IntersectionEpsilon)
      return false;

    distance = Vector3.Dot(edge2, perpendicular2) * inverseDeterminant;
    return float.IsFinite(distance) && distance >= 0f;
  }

  private static bool TryUnproject(Vector4 clip, Matrix4x4 inverse, out Vector3 world) {
    world = default;
    var homogeneous = Vector4.Transform(clip, inverse);
    if (!IsFinite(homogeneous) || MathF.Abs(homogeneous.W) <= IntersectionEpsilon)
      return false;

    world = new Vector3(homogeneous.X, homogeneous.Y, homogeneous.Z) / homogeneous.W;
    return IsFinite(world);
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

  private static bool IsFinite(Vector2 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X)
    && float.IsFinite(value.Y)
    && float.IsFinite(value.Z)
    && float.IsFinite(value.W);

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12)
    && float.IsFinite(value.M13) && float.IsFinite(value.M14)
    && float.IsFinite(value.M21) && float.IsFinite(value.M22)
    && float.IsFinite(value.M23) && float.IsFinite(value.M24)
    && float.IsFinite(value.M31) && float.IsFinite(value.M32)
    && float.IsFinite(value.M33) && float.IsFinite(value.M34)
    && float.IsFinite(value.M41) && float.IsFinite(value.M42)
    && float.IsFinite(value.M43) && float.IsFinite(value.M44);
}
