// Ride Track Placement Transform
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Builds the proven world transform for one decoded DAT ride-track placement.</summary>
public static class RideTrackPlacementTransform {
  /// <summary>
  /// Places local Z-up track geometry at its resolved SID position-type anchor, using the DAT
  /// placement's absolute height and scenery-specific direction ordinal.
  /// </summary>
  /// <remarks>
  /// <see cref="RideTrackPlacement.Reversed"/> reverses the local rail traversal before this world
  /// transform is applied. <see cref="RideTrackPlacement.UserAngleDegrees"/> targets the vehicle's
  /// spin/swing part over the piece and does not alter static rail placement.
  /// </remarks>
  public static Matrix4x4 Create(
    RideTrackPlacement placement,
    SceneryItem sceneryItem,
    Terrain terrain
  ) {
    ArgumentNullException.ThrowIfNull(placement);
    ArgumentNullException.ThrowIfNull(sceneryItem);
    ArgumentNullException.ThrowIfNull(terrain);

    if (!terrain.HasTile(placement.TileX, placement.TileY))
      throw Invalid(placement, "tile is outside the terrain grid");
    if (placement.SerializedDirection is < 0 or > 3)
      throw Invalid(
        placement,
        $"serialized direction {placement.SerializedDirection} is unsupported");
    if (!Enum.IsDefined(placement.Rotation))
      throw Invalid(placement, $"rotation {placement.Rotation} is unsupported");

    var expectedRotation = RotationForDirection(placement.SerializedDirection);
    if (placement.Rotation != expectedRotation)
      throw Invalid(
        placement,
        $"rotation {placement.Rotation} does not match serialized direction " +
        $"{placement.SerializedDirection} ({expectedRotation})");
    if (!string.Equals(
      placement.ObjectKey,
      sceneryItem.Name,
      StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        placement,
        $"object key '{placement.ObjectKey}' does not match resolved SID " +
        $"'{sceneryItem.Name}'");
    var anchor = SceneryGeometryBuilder.CalculatePlacementAnchor(
      terrain,
      placement.TileX,
      placement.TileY,
      sceneryItem,
      placement.SerializedDirection,
      placement.Corner,
      placement.ObjectKey);
    var translation = new Vector3(
      anchor,
      Convert.ToSingle(placement.SerializedHeight));
    if (!IsFinite(translation))
      throw Invalid(placement, "world translation is non-finite");

    var transform = DirectionRotation(placement.SerializedDirection) *
      Matrix4x4.CreateTranslation(translation);
    if (!IsFinite(transform))
      throw Invalid(placement, "world transform is non-finite");
    return transform;
  }

  private static Edge RotationForDirection(int direction) => direction switch {
    0 => Edge.West,
    1 => Edge.North,
    2 => Edge.East,
    3 => Edge.South,
    _ => throw new ArgumentOutOfRangeException(nameof(direction)),
  };

  private static Matrix4x4 DirectionRotation(int direction) => direction switch {
    // This is the executable-backed scenery transform also used by SceneryGeometryBuilder:
    // 0 West, 1 North, 2 East, 3 South. Keep exact quarter turns instead of trig approximations.
    0 => new Matrix4x4(
      -1f, 0f, 0f, 0f,
      0f, -1f, 0f, 0f,
      0f, 0f, 1f, 0f,
      0f, 0f, 0f, 1f),
    1 => new Matrix4x4(
      0f, 1f, 0f, 0f,
      -1f, 0f, 0f, 0f,
      0f, 0f, 1f, 0f,
      0f, 0f, 0f, 1f),
    2 => Matrix4x4.Identity,
    3 => new Matrix4x4(
      0f, -1f, 0f, 0f,
      1f, 0f, 0f, 0f,
      0f, 0f, 1f, 0f,
      0f, 0f, 0f, 1f),
    _ => throw new ArgumentOutOfRangeException(nameof(direction)),
  };

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

  private static InvalidDataException Invalid(
    RideTrackPlacement placement,
    string message
  ) => new($"Ride track placement {placement.SourceEntryId} is invalid: {message}.");
}
