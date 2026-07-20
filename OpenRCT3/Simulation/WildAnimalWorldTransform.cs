// Wild Animal World Transform
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>Converts one native saved Wild-animal world matrix into park coordinates.</summary>
/// <remarks>
/// Complete Edition's <c>0x00DEE090</c> path consumes <c>WildAnimalVisual.WorldMatrix</c> in
/// row-vector order. MDL vertices already cross the native <c>(X, Y, Z)</c> to park
/// <c>(X, Z, Y)</c> bridge, so the corresponding world transform is <c>S * native * S</c>.
/// </remarks>
internal static class WildAnimalWorldTransform {
  private static Matrix4x4 NativeToParkBasis { get; } = new(
    1f, 0f, 0f, 0f,
    0f, 0f, 1f, 0f,
    0f, 1f, 0f, 0f,
    0f, 0f, 0f, 1f);

  public static Matrix4x4 ToPark(Matrix4x4 nativeWorldMatrix) {
    if (!IsFinite(nativeWorldMatrix))
      throw Invalid("native WorldMatrix contains a non-finite value");
    var parkWorldMatrix = NativeToParkBasis * nativeWorldMatrix * NativeToParkBasis;
    if (!IsFinite(parkWorldMatrix))
      throw Invalid("basis conversion produced a non-finite value");
    return parkWorldMatrix;
  }

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  private static InvalidDataException Invalid(string message) =>
    new($"Cannot convert saved Wild animal world transform: {message}.");
}
