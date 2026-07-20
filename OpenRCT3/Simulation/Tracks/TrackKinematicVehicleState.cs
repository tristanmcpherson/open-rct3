// Track Kinematic Vehicle State
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation.Tracks;

/// <summary>A constant-speed vehicle state bound to one exact circuit cursor.</summary>
/// <remarks>
/// Speed is signed centerline distance per second. This state only performs deterministic kinematic
/// advancement and contact-pose sampling. It does not model forces, gravity, acceleration, braking,
/// suspension, or other ride physics.
/// </remarks>
public readonly record struct TrackCircuitKinematicVehicleState {
  /// <summary>The exact double-precision circuit cursor retained by this state.</summary>
  public TrackCircuitCursor Cursor { get; }

  /// <summary>The finite signed centerline speed in distance units per second.</summary>
  public double Speed { get; }

  /// <summary>The vehicle-placement pose sampled from <see cref="Cursor"/>.</summary>
  public TrackContactPose Pose { get; }

  /// <summary>Whether this value was constructed with an initialized track cursor.</summary>
  public bool IsInitialized => Cursor.IsInitialized;

  public TrackCircuitKinematicVehicleState(TrackCircuitCursor cursor, double speed) {
    TrackKinematicVehicleStep.ValidateSpeed(speed);
    var sample = cursor.Sample();
    Cursor = cursor;
    Speed = speed;
    Pose = TrackContactPoseAdapter.Create(sample.ContactPoints);
  }

  /// <summary>Advances by one finite, non-negative fixed time step at constant speed.</summary>
  public TrackCircuitKinematicVehicleState Advance(TimeSpan elapsed) {
    TrackKinematicVehicleStep.ValidateInitialized(IsInitialized);
    var distance = TrackKinematicVehicleStep.GetDistance(Speed, elapsed);
    if (distance == 0d) return this;
    return new(Cursor.Advance(distance), Speed);
  }
}

/// <summary>A constant-speed vehicle state bound to one exact graph cursor.</summary>
/// <remarks>
/// Speed is signed centerline distance per second. Forward and reverse selectors are passed
/// directly to graph traversal and are required when the step crosses an ambiguous branch or merge.
/// This state does not model forces, gravity, acceleration, braking, suspension, or other ride
/// physics.
/// </remarks>
public readonly record struct TrackGraphKinematicVehicleState {
  /// <summary>The exact double-precision graph cursor retained by this state.</summary>
  public TrackGraphCursor Cursor { get; }

  /// <summary>The finite signed centerline speed in distance units per second.</summary>
  public double Speed { get; }

  /// <summary>The vehicle-placement pose sampled from <see cref="Cursor"/>.</summary>
  public TrackContactPose Pose { get; }

  /// <summary>Whether this value was constructed with an initialized track cursor.</summary>
  public bool IsInitialized => Cursor.IsInitialized;

  public TrackGraphKinematicVehicleState(TrackGraphCursor cursor, double speed) {
    TrackKinematicVehicleStep.ValidateSpeed(speed);
    var sample = cursor.Sample();
    Cursor = cursor;
    Speed = speed;
    Pose = TrackContactPoseAdapter.Create(sample.ContactPoints);
  }

  /// <summary>
  /// Advances by one finite, non-negative fixed time step using explicit branch and merge
  /// selectors.
  /// </summary>
  public TrackGraphKinematicVehicleState Advance(
    TimeSpan elapsed,
    TrackGraphEdgeSelector? forwardSelector = null,
    TrackGraphEdgeSelector? reverseSelector = null
  ) {
    TrackKinematicVehicleStep.ValidateInitialized(IsInitialized);
    var distance = TrackKinematicVehicleStep.GetDistance(Speed, elapsed);
    if (distance == 0d) return this;
    return new(
      Cursor.Advance(distance, forwardSelector, reverseSelector),
      Speed);
  }
}

internal static class TrackKinematicVehicleStep {
  internal static void ValidateSpeed(double speed) {
    if (!double.IsFinite(speed))
      throw new ArgumentOutOfRangeException(nameof(speed));
  }

  internal static void ValidateInitialized(bool isInitialized) {
    if (!isInitialized)
      throw new InvalidOperationException("The kinematic vehicle state is not initialized.");
  }

  internal static double GetDistance(double speed, TimeSpan elapsed) {
    ValidateSpeed(speed);
    if (elapsed < TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(nameof(elapsed));

    var elapsedSeconds = elapsed.TotalSeconds;
    if (!double.IsFinite(elapsedSeconds))
      throw new ArgumentOutOfRangeException(nameof(elapsed));
    var distance = speed * elapsedSeconds;
    if (!double.IsFinite(distance))
      throw new ArgumentOutOfRangeException(
        nameof(elapsed),
        "The fixed time step overflows the finite kinematic travel distance.");
    return distance;
  }
}
