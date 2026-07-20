// Track Circuit Kinematic Train State
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation.Tracks;

/// <summary>
/// An immutable constant-speed lead and ordered contact layout on a track circuit.
/// </summary>
/// <remarks>
/// Offsets can represent cars, axles, or any other caller-defined contacts behind the lead. This
/// state only advances the exact lead cursor and rebuilds geometry-only contact poses. It does not
/// infer dimensions, resources, forces, gravity, acceleration, braking, or other ride physics.
/// </remarks>
public sealed class TrackCircuitKinematicTrainState {
  /// <summary>The exact constant-speed lead vehicle state.</summary>
  public TrackCircuitKinematicVehicleState Lead { get; }

  /// <summary>The exact lead cursor retained in double precision.</summary>
  public TrackCircuitCursor LeadCursor => Lead.Cursor;

  /// <summary>The finite signed lead speed in centerline distance units per second.</summary>
  public double Speed => Lead.Speed;

  /// <summary>The bounded nondecreasing caller-defined offsets behind the lead.</summary>
  public IReadOnlyList<double> OffsetsBehindLead { get; }

  /// <summary>The exact ordered cursors and sampled poses at the retained offsets.</summary>
  public IReadOnlyList<TrackCircuitConsistPose> Poses { get; }

  public TrackCircuitKinematicTrainState(
    TrackCircuitCursor leadCursor,
    double speed,
    IReadOnlyList<double> offsetsBehindLead
  ) : this(
    new TrackCircuitKinematicVehicleState(leadCursor, speed),
    offsetsBehindLead
  ) { }

  public TrackCircuitKinematicTrainState(
    TrackCircuitKinematicVehicleState lead,
    IReadOnlyList<double> offsetsBehindLead
  ) {
    var poses = TrackConsistLayoutBuilder.FromCircuit(lead.Cursor, offsetsBehindLead);
    Lead = lead;
    OffsetsBehindLead = Array.AsReadOnly(
      poses.Select(pose => pose.OffsetBehindLead).ToArray());
    Poses = poses;
  }

  /// <summary>
  /// Advances the exact lead by one fixed step and rebuilds every retained contact.
  /// </summary>
  public TrackCircuitKinematicTrainState Advance(TimeSpan elapsed) =>
    new(Lead.Advance(elapsed), OffsetsBehindLead);

  /// <summary>Returns the same exact cursor and layout with a new finite signed speed.</summary>
  public TrackCircuitKinematicTrainState WithSpeed(double speed) =>
    new(Lead.Cursor, speed, OffsetsBehindLead);
}
