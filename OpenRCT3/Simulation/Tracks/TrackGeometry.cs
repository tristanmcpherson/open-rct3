// Track Geometry
//
// Authors:
//   - OpenRCT3 Contributors
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation.Tracks;

/// <summary>Identifies one of a tracked ride piece's two authoritative rail splines.</summary>
public enum RailSide {
  Left = 0,
  Right = 1,
}

/// <summary>Identifies how a complete piece's control data was authored.</summary>
public enum TrackPieceAuthoringMode {
  Procedural = 0,
  HandAuthored = 1,
}

/// <summary>
/// The paired control data at one normalized position along a track piece. Tangents are derivatives
/// with respect to <see cref="Parameter"/>, not normalized direction vectors.
/// </summary>
public readonly record struct RailControlPair(
  float Parameter,
  Vector3 LeftPosition,
  Vector3 LeftTangent,
  Vector3 RightPosition,
  Vector3 RightTangent,
  float BankRadians
);

/// <summary>Rail-pair data returned by a procedural piece profile.</summary>
public readonly record struct RailProfileSample(
  Vector3 LeftPosition,
  Vector3 LeftTangent,
  Vector3 RightPosition,
  Vector3 RightTangent,
  float BankRadians
);

/// <summary>
/// Immutable analytic control data for one whole track piece. A piece is either fully procedural or
/// fully hand-authored; individual control points never mix the two modes.
/// </summary>
public sealed class TrackPieceGeometry {
  private const float MinimumGauge = 0.0001f;
  private const float MaximumGaugeTangentDot = 0.25f;
  private readonly ReadOnlyCollection<RailControlPair> controlPoints;

  public TrackPieceAuthoringMode AuthoringMode { get; }
  public IReadOnlyList<RailControlPair> ControlPoints => controlPoints;

  private TrackPieceGeometry(
    TrackPieceAuthoringMode authoringMode,
    IEnumerable<RailControlPair> controlPoints
  ) {
    var copied = controlPoints.ToArray();
    Validate(copied);
    AuthoringMode = authoringMode;
    this.controlPoints = Array.AsReadOnly(copied);
  }

  /// <summary>Creates a piece whose complete control-point set is explicitly authored.</summary>
  public static TrackPieceGeometry FromHandAuthored(
    IEnumerable<RailControlPair> controlPoints
  ) {
    ArgumentNullException.ThrowIfNull(controlPoints);
    return new(TrackPieceAuthoringMode.HandAuthored, controlPoints);
  }

  /// <summary>
  /// Creates a piece by sampling one procedural rail-pair profile over the normalized piece range.
  /// </summary>
  public static TrackPieceGeometry FromProcedural(
    int controlPointCount,
    Func<float, RailProfileSample> profile
  ) {
    if (controlPointCount < 2)
      throw new ArgumentOutOfRangeException(
        nameof(controlPointCount),
        "A rail spline needs at least two control points."
      );
    ArgumentNullException.ThrowIfNull(profile);

    var points = new RailControlPair[controlPointCount];
    for (var index = 0; index < controlPointCount; index++) {
      var parameter = Convert.ToSingle(index) / Convert.ToSingle(controlPointCount - 1);
      var sample = profile(parameter);
      points[index] = new(
        parameter,
        sample.LeftPosition,
        sample.LeftTangent,
        sample.RightPosition,
        sample.RightTangent,
        sample.BankRadians
      );
    }

    return new(TrackPieceAuthoringMode.Procedural, points);
  }

  private static void Validate(IReadOnlyList<RailControlPair> points) {
    if (points.Count < 2)
      throw new ArgumentException("A rail spline needs at least two control points.", nameof(points));
    if (points[0].Parameter != 0f || points[^1].Parameter != 1f)
      throw new ArgumentException("Control-point parameters must span exactly 0 through 1.", nameof(points));

    var previousParameter = -1f;
    foreach (var point in points) {
      if (!float.IsFinite(point.Parameter) || point.Parameter <= previousParameter)
        throw new ArgumentException(
          "Control-point parameters must be finite and strictly increasing.",
          nameof(points)
        );
      if (!TrackMath.IsFinite(point.LeftPosition)
          || !TrackMath.IsFinite(point.RightPosition)
          || !TrackMath.IsFinite(point.LeftTangent)
          || !TrackMath.IsFinite(point.RightTangent)
          || !float.IsFinite(point.BankRadians))
        throw new ArgumentException("Rail control data must contain only finite values.", nameof(points));

      var leftTangentLength = point.LeftTangent.Length();
      var rightTangentLength = point.RightTangent.Length();
      if (leftTangentLength <= TrackMath.Epsilon || rightTangentLength <= TrackMath.Epsilon)
        throw new ArgumentException("Rail tangents cannot be zero-length.", nameof(points));

      var leftTangent = point.LeftTangent / leftTangentLength;
      var rightTangent = point.RightTangent / rightTangentLength;
      if (Vector3.Dot(leftTangent, rightTangent) <= 0f)
        throw new ArgumentException("Paired rail tangents must point in the same direction.", nameof(points));

      var gauge = point.RightPosition - point.LeftPosition;
      var gaugeLength = gauge.Length();
      if (gaugeLength <= MinimumGauge)
        throw new ArgumentException("Left and right rail positions must be distinct.", nameof(points));

      var averageTangent = Vector3.Normalize(leftTangent + rightTangent);
      var gaugeTangentDot = MathF.Abs(Vector3.Dot(gauge / gaugeLength, averageTangent));
      if (gaugeTangentDot > MaximumGaugeTangentDot)
        throw new ArgumentException(
          "The rail gauge must remain approximately perpendicular to rail travel.",
          nameof(points)
        );

      previousParameter = point.Parameter;
    }
  }
}

/// <summary>Global adaptive-bake tolerances shared by all track piece types.</summary>
public sealed record TrackBakeSettings(
  float ChordToleranceGaugeFraction = 0.02f,
  float MinimumChordTolerance = 0.03f,
  float MaximumBankAngleChangeRadians = 0.08726646f,
  int MaximumSubdivisionDepth = 12
) {
  public static TrackBakeSettings Default { get; } = new();

  internal void Validate() {
    if (!float.IsFinite(ChordToleranceGaugeFraction) || ChordToleranceGaugeFraction < 0f)
      throw new ArgumentOutOfRangeException(nameof(ChordToleranceGaugeFraction));
    if (!float.IsFinite(MinimumChordTolerance) || MinimumChordTolerance <= 0f)
      throw new ArgumentOutOfRangeException(nameof(MinimumChordTolerance));
    if (!float.IsFinite(MaximumBankAngleChangeRadians)
        || MaximumBankAngleChangeRadians <= 0f)
      throw new ArgumentOutOfRangeException(nameof(MaximumBankAngleChangeRadians));
    if (MaximumSubdivisionDepth is < 1 or > 24)
      throw new ArgumentOutOfRangeException(nameof(MaximumSubdivisionDepth));
  }
}

/// <summary>An exact analytic rail-spline evaluation, used for authoring and bake regeneration.</summary>
public readonly record struct RailEvaluation(
  float Parameter,
  Vector3 Position,
  Vector3 Tangent,
  float BankRadians
);

/// <summary>A runtime rail contact sampled only from a piece's baked lookup table.</summary>
public readonly record struct RailSample(
  float ArcLength,
  Vector3 Position,
  Vector3 Tangent,
  Quaternion Orientation,
  float BankRadians
);

/// <summary>The paired wheel-contact query result at one piece-local arc length.</summary>
public readonly record struct TrackContactPoints(
  float ArcLength,
  RailSample Left,
  RailSample Right
) {
  public Vector3 Midpoint => (Left.Position + Right.Position) * 0.5f;
}

/// <summary>One rail's exact boundary state, used to validate joins between graph edges.</summary>
public readonly record struct RailEndpoint(Vector3 Position, Vector3 Tangent, float BankRadians);

/// <summary>The paired exact boundary state at a track piece entry or exit.</summary>
public readonly record struct TrackPieceEndpoint(RailEndpoint Left, RailEndpoint Right);

internal static class TrackMath {
  public const float Epsilon = 0.000001f;
  public const float TwoPi = MathF.PI * 2f;

  public static bool IsFinite(Vector3 value)
    => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  public static bool IsFinite(Matrix4x4 value)
    => float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13)
      && float.IsFinite(value.M14) && float.IsFinite(value.M21) && float.IsFinite(value.M22)
      && float.IsFinite(value.M23) && float.IsFinite(value.M24) && float.IsFinite(value.M31)
      && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
      && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43)
      && float.IsFinite(value.M44);

  public static float AngleDelta(float start, float end) {
    var delta = (end - start) % TwoPi;
    if (delta > MathF.PI) delta -= TwoPi;
    if (delta < -MathF.PI) delta += TwoPi;
    return delta;
  }

  public static float LerpAngle(float start, float end, float amount)
    => start + (AngleDelta(start, end) * amount);
}
