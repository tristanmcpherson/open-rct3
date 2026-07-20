// Track Consist Layout
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation.Tracks;

/// <summary>One ordered circuit pose retained at an exact cursor behind a consist lead.</summary>
public readonly record struct TrackCircuitConsistPose(
  double OffsetBehindLead,
  TrackCircuitCursor Cursor,
  TrackContactPose Pose
);

/// <summary>One ordered graph pose retained at an exact cursor behind a consist lead.</summary>
public readonly record struct TrackGraphConsistPose(
  double OffsetBehindLead,
  TrackGraphCursor Cursor,
  TrackContactPose Pose
);

/// <summary>Places ordered geometry-only consist contacts behind one lead cursor.</summary>
/// <remarks>
/// Offsets are nondecreasing distances behind the lead. Placement only advances track cursors,
/// resamples their exact dual-rail contacts, and derives contact frames. It does not infer vehicle
/// dimensions, speed, gravity, suspension, articulation, or any other ride-physics state.
/// </remarks>
public static class TrackConsistLayoutBuilder {
  /// <summary>Places ordered contacts behind one lead cursor on a closed circuit.</summary>
  public static IReadOnlyList<TrackCircuitConsistPose> FromCircuit(
    TrackCircuitCursor lead,
    IReadOnlyList<double> offsetsBehindLead
  ) => FromCircuit(lead, offsetsBehindLead, TrackConsistLayoutLimits.Default);

  /// <summary>
  /// Places ordered contacts behind one graph lead, using the explicit selector at reverse merges.
  /// </summary>
  public static IReadOnlyList<TrackGraphConsistPose> FromGraph(
    TrackGraphCursor lead,
    IReadOnlyList<double> offsetsBehindLead,
    TrackGraphEdgeSelector reverseSelector
  ) => FromGraph(
    lead,
    offsetsBehindLead,
    reverseSelector,
    TrackConsistLayoutLimits.Default);

  internal static IReadOnlyList<TrackCircuitConsistPose> FromCircuit(
    TrackCircuitCursor lead,
    IReadOnlyList<double> offsetsBehindLead,
    TrackConsistLayoutLimits limits
  ) {
    var offsets = CopyAndValidateOffsets(offsetsBehindLead, limits);
    _ = lead.Sample();

    var result = new TrackCircuitConsistPose[offsets.Length];
    var cursor = lead;
    var previousOffset = 0d;
    foreach (var index in Enumerable.Range(0, offsets.Length)) {
      cursor = cursor.Advance(-(offsets[index] - previousOffset));
      var sample = cursor.Sample();
      result[index] = new(
        offsets[index],
        cursor,
        TrackContactPoseAdapter.Create(sample.ContactPoints));
      previousOffset = offsets[index];
    }
    return Array.AsReadOnly(result);
  }

  internal static IReadOnlyList<TrackGraphConsistPose> FromGraph(
    TrackGraphCursor lead,
    IReadOnlyList<double> offsetsBehindLead,
    TrackGraphEdgeSelector reverseSelector,
    TrackConsistLayoutLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(reverseSelector);
    var offsets = CopyAndValidateOffsets(offsetsBehindLead, limits);
    _ = lead.Sample();

    var result = new TrackGraphConsistPose[offsets.Length];
    var cursor = lead;
    var previousOffset = 0d;
    foreach (var index in Enumerable.Range(0, offsets.Length)) {
      cursor = cursor.Advance(
        -(offsets[index] - previousOffset),
        reverseSelector: reverseSelector);
      var sample = cursor.Sample();
      result[index] = new(
        offsets[index],
        cursor,
        TrackContactPoseAdapter.Create(sample.ContactPoints));
      previousOffset = offsets[index];
    }
    return Array.AsReadOnly(result);
  }

  private static double[] CopyAndValidateOffsets(
    IReadOnlyList<double> offsets,
    TrackConsistLayoutLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(offsets);
    if (limits.MaximumPoseCount < 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
    var count = offsets.Count;
    if (count > limits.MaximumPoseCount)
      throw new InvalidDataException(
        $"Consist pose count {count} exceeds the bound {limits.MaximumPoseCount}.");

    var copied = new double[count];
    var previous = 0d;
    foreach (var index in Enumerable.Range(0, count)) {
      var offset = offsets[index];
      if (!double.IsFinite(offset) || offset < 0d)
        throw new ArgumentOutOfRangeException(
          nameof(offsets),
          $"Consist offset {index} must be finite and nonnegative.");
      if (index > 0 && offset < previous)
        throw new ArgumentException(
          "Consist offsets must be sorted in nondecreasing order.",
          nameof(offsets));
      copied[index] = offset;
      previous = offset;
    }
    return copied;
  }
}

internal readonly record struct TrackConsistLayoutLimits(int MaximumPoseCount) {
  public static TrackConsistLayoutLimits Default { get; } = new(16_384);
}
