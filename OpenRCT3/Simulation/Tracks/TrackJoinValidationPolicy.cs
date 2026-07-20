// Track Join Validation Policy
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation.Tracks;

/// <summary>Independent geometric tolerances for validating a dual-rail piece join.</summary>
/// <remarks>
/// A null <see cref="TangentMagnitudeTolerance"/> validates tangent direction but deliberately
/// does not require two independently parameterized curves to have equal raw derivatives.
/// </remarks>
public sealed record TrackJoinValidationPolicy {
  public float PositionTolerance { get; }
  public float TangentDirectionTolerance { get; }
  public float? TangentMagnitudeTolerance { get; }
  public float BankToleranceRadians { get; }

  public TrackJoinValidationPolicy(
    float positionTolerance,
    float tangentDirectionTolerance,
    float? tangentMagnitudeTolerance,
    float bankToleranceRadians
  ) {
    ValidateTolerance(positionTolerance, nameof(positionTolerance));
    ValidateTolerance(tangentDirectionTolerance, nameof(tangentDirectionTolerance));
    if (tangentMagnitudeTolerance is not null)
      ValidateTolerance(tangentMagnitudeTolerance.Value, nameof(tangentMagnitudeTolerance));
    ValidateTolerance(bankToleranceRadians, nameof(bankToleranceRadians));

    PositionTolerance = positionTolerance;
    TangentDirectionTolerance = tangentDirectionTolerance;
    TangentMagnitudeTolerance = tangentMagnitudeTolerance;
    BankToleranceRadians = bankToleranceRadians;
  }

  private static void ValidateTolerance(float tolerance, string parameterName) {
    if (!float.IsFinite(tolerance) || tolerance < 0f)
      throw new ArgumentOutOfRangeException(parameterName);
  }
}
