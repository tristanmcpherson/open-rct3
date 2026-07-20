// Camera
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using OpenCobra.GDK.Shaders;
using System.Numerics;

namespace OpenCobra.GDK;

public class Camera : Uniform<Matrix4x4> {
  public static readonly string UniformName = "u_ViewProj";
  public new readonly string Name = UniformName;

  /// <summary>
  /// The world-space offset (South-East, elevated) the camera's default framing looks from.
  /// </summary>
  /// <remarks>
  /// The Z component is deliberately large relative to the X/Y horizontal offset — roughly a 60°
  /// elevation above the horizon, not a shallow grazing angle. A large, flat, unlit, single-color
  /// terrain plane viewed edge-on from a shallow angle (the original (20, -20, 15) offset, ~28°
  /// elevation) has no visual cue distinguishing "looking down at the ground from above" from "looking
  /// up at a ceiling from below" — there's no horizon, no shading gradient, nothing but a silhouette.
  /// A steep, mostly-downward angle removes that ambiguity.
  /// </remarks>
  private static readonly Vector3 DefaultViewOffset = new(20, -20, 50);
  /// <summary>The unit direction of <see cref="DefaultViewOffset"/>, used by <see cref="Frame"/>.</summary>
  public static readonly Vector3 DefaultViewDirection = Vector3.Normalize(DefaultViewOffset);
  private static readonly float DefaultDistance = DefaultViewOffset.Length();

  /// <summary>Near clip distance, in world-space meters. 1cm is close enough for any placeable object.</summary>
  public const float NearPlaneDistance = 0.01f;
  /// <summary>
  /// Maximum camera elevation above or below the horizon, leaving one degree between the view
  /// direction and either world-Z pole so <see cref="Matrix4x4.CreateLookAt"/> remains well-defined.
  /// </summary>
  public const float MaximumAbsoluteElevation = (MathF.PI / 2f) - (MathF.PI / 180f);
  /// <summary>
  /// Far clip distance, expressed as a multiple of the greater of the current eye-to-target distance
  /// and the distance supplied to the last <see cref="Frame"/> call. Frame callers (see <c>Game.cs</c>)
  /// already pick a distance that keeps an entire park's mesh on-screen, scaled to that park's actual
  /// size. Retaining that framing distance keeps the park's depth range visible while zooming in,
  /// instead of shrinking the far plane with the eye-to-target distance and clipping most of the map.
  /// Using the current distance when it is greater still expands the projection when zooming out. The
  /// 2x margin leaves room for a future cube-mapped skybox drawn just outside a park's total
  /// (OOB-inclusive) bounds without needing to be revisited once one exists.
  /// </summary>
  private const float FarPlaneDistanceMargin = 2f;
  /// <summary>The last framing distance retained as the minimum far-plane coverage.</summary>
  private float framingDistance = DefaultDistance;

  /// <summary>The world-space point the camera is aimed at.</summary>
  public Vector3 Target { get; private set; } = Vector3.Zero;
  /// <summary>The camera's world-space eye position.</summary>
  public Vector3 Eye { get; private set; }
  /// <summary>The world-space distance from <see cref="Eye"/> to <see cref="Target"/>.</summary>
  public float Distance => Vector3.Distance(Eye, Target);
  /// <summary>The scene-aware closest distance recommended for camera controls.</summary>
  public float MinimumDistance { get; private set; } = NearPlaneDistance;

  public Camera() {
    Value = Matrix4x4.Identity;
    Eye = Target + (DefaultViewDirection * DefaultDistance);
  }

  /// <summary>
  /// Re-aims the camera at <paramref name="target"/>, keeping the same South-East/elevated viewing
  /// direction, with the eye placed <paramref name="distance"/> units away along that direction.
  /// </summary>
  public void Frame(
    Vector3 target,
    float distance,
    float minimumDistance = NearPlaneDistance
  ) {
    if (!IsFinite(target)) throw new ArgumentOutOfRangeException(nameof(target));
    ValidateDistance(distance);
    ValidateMinimumDistance(minimumDistance, distance);

    Target = target;
    Eye = target + (DefaultViewDirection * distance);
    framingDistance = distance;
    MinimumDistance = minimumDistance;
  }

  /// <summary>Moves the eye and target together by a world-space offset.</summary>
  public void Pan(Vector3 offset) {
    if (!IsFinite(offset)) throw new ArgumentOutOfRangeException(nameof(offset));

    var target = Target + offset;
    var eye = Eye + offset;
    if (!IsFinite(target) || !IsFinite(eye))
      throw new ArgumentOutOfRangeException(nameof(offset));

    Target = target;
    Eye = eye;
  }

  /// <summary>Rotates the eye around the target on the world's Z axis.</summary>
  public void Orbit(float radians) {
    if (!float.IsFinite(radians)) throw new ArgumentOutOfRangeException(nameof(radians));
    if (radians == 0f) return;

    var normalizedRadians = MathF.IEEERemainder(radians, MathF.Tau);
    var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, normalizedRadians);
    var eye = Target + Vector3.Transform(Eye - Target, rotation);
    if (!IsFinite(eye)) throw new ArgumentOutOfRangeException(nameof(radians));

    Eye = eye;
  }

  /// <summary>
  /// Rotates the eye vertically around the target while preserving its azimuth and distance.
  /// Positive angles raise the eye above the horizon; negative angles lower it.
  /// </summary>
  public void OrbitElevation(float radians) {
    if (!float.IsFinite(radians)) throw new ArgumentOutOfRangeException(nameof(radians));
    if (radians == 0f) return;

    var offset = Eye - Target;
    var distance = offset.Length();
    if (!float.IsFinite(distance) || distance <= 0f)
      throw new InvalidOperationException("Camera eye and target must be distinct.");

    var horizontalDistance = new Vector2(offset.X, offset.Y).Length();
    if (!float.IsFinite(horizontalDistance))
      throw new InvalidOperationException("Camera eye and target must be finite.");

    var azimuth = MathF.Atan2(offset.Y, offset.X);
    var elevation = MathF.Atan2(offset.Z, horizontalDistance);
    var nextElevation = Math.Clamp(
      elevation + radians,
      -MaximumAbsoluteElevation,
      MaximumAbsoluteElevation
    );
    var nextHorizontalDistance = MathF.Cos(nextElevation) * distance;
    var eye = Target + new Vector3(
      MathF.Cos(azimuth) * nextHorizontalDistance,
      MathF.Sin(azimuth) * nextHorizontalDistance,
      MathF.Sin(nextElevation) * distance
    );
    if (!IsFinite(eye)) throw new ArgumentOutOfRangeException(nameof(radians));

    Eye = eye;
  }

  /// <summary>Moves the eye along its current target-to-eye direction.</summary>
  public void SetDistance(float distance) {
    ValidateDistance(distance);

    var offset = Eye - Target;
    var currentDistance = offset.Length();
    if (!float.IsFinite(currentDistance) || currentDistance <= 0f)
      throw new InvalidOperationException("Camera eye and target must be distinct.");

    var eye = Target + ((offset / currentDistance) * distance);
    if (!IsFinite(eye)) throw new ArgumentOutOfRangeException(nameof(distance));
    Eye = eye;
  }

  /// <summary>
  /// Updates the camera view and projection matrices.
  /// </summary>
  /// <param name="aspectRatio">The aspect ratio of the viewport.</param>
  public void Update(float aspectRatio) {
    if (!float.IsFinite(aspectRatio) || aspectRatio <= 0f)
      throw new ArgumentOutOfRangeException(nameof(aspectRatio));

    var view = Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitZ);
    var projectionDistance = MathF.Max(Distance, framingDistance);
    var farPlaneDistance = projectionDistance * FarPlaneDistanceMargin;
    var projection = CreatePerspectiveFieldOfViewGL(
      MathF.PI / 3f,
      aspectRatio,
      NearPlaneDistance,
      farPlaneDistance
    );

    Value = view * projection;
  }

  /// <summary>
  /// Creates a right-handed perspective projection matrix using OpenGL's clip-space Z convention:
  /// <paramref name="nearPlaneDistance"/> maps to NDC z = -1 and <paramref name="farPlaneDistance"/>
  /// maps to NDC z = +1.
  /// </summary>
  /// <remarks>
  /// <see cref="Matrix4x4.CreatePerspectiveFieldOfView"/> targets Direct3D's [0, 1] NDC-z convention
  /// instead. This engine renders exclusively via OpenGL (with no <c>glClipControl</c> override — the
  /// GL 4.1 Core profile this project targets predates that extension's core availability), so using
  /// the D3D-convention matrix directly compresses the entire visible depth range into the back half
  /// of the depth buffer, discarding precision where it matters most: close to the camera.
  /// </remarks>
  public static Matrix4x4 CreatePerspectiveFieldOfViewGL(
    float fieldOfView,
    float aspectRatio,
    float nearPlaneDistance,
    float farPlaneDistance) {
    if (!float.IsFinite(fieldOfView) || fieldOfView <= 0f || fieldOfView >= MathF.PI)
      throw new ArgumentOutOfRangeException(nameof(fieldOfView));
    if (!float.IsFinite(aspectRatio) || aspectRatio <= 0f)
      throw new ArgumentOutOfRangeException(nameof(aspectRatio));
    if (!float.IsFinite(nearPlaneDistance) || nearPlaneDistance <= 0f)
      throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));
    if (!float.IsFinite(farPlaneDistance) || farPlaneDistance <= nearPlaneDistance)
      throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));

    var yScale = 1.0f / MathF.Tan(fieldOfView * 0.5f);
    var xScale = yScale / aspectRatio;
    var range = farPlaneDistance - nearPlaneDistance;

    return new Matrix4x4(
      xScale, 0, 0, 0,
      0, yScale, 0, 0,
      0, 0, -(farPlaneDistance + nearPlaneDistance) / range, -1,
      0, 0, -2 * farPlaneDistance * nearPlaneDistance / range, 0
    );
  }

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static void ValidateDistance(float distance) {
    if (!float.IsFinite(distance)
      || distance <= NearPlaneDistance
      || distance > float.MaxValue / FarPlaneDistanceMargin)
      throw new ArgumentOutOfRangeException(nameof(distance));
  }

  private static void ValidateMinimumDistance(float minimumDistance, float framingDistance) {
    if (!float.IsFinite(minimumDistance)
      || minimumDistance < NearPlaneDistance
      || minimumDistance > framingDistance)
      throw new ArgumentOutOfRangeException(nameof(minimumDistance));
  }
}
