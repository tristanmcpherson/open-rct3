// Ride Track
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;

namespace OpenRCT3.Simulation;

/// <summary>
/// One semantic DAT ride-track root. It retains serialized topology without coercing the original
/// cyclic or sentinel-linked structure into <c>TrackGraph</c>.
/// </summary>
public sealed class RideTrack {
  public ulong SourceEntryId { get; }
  public int Direction { get; }
  public ulong FirstSegmentSourceEntryId { get; }
  public ulong LastSegmentSourceEntryId { get; }
  /// <summary>
  /// The authoritative link-derived circuit classification, or <c>null</c> when the current
  /// runtime model cannot represent the decoded segment topology as one traversal.
  /// </summary>
  public bool? IsCircuit { get; }
  /// <summary>
  /// The advisory serialized field retained by older DAT layouts. Complete Edition's native
  /// <c>Track</c> does not consume this field; reciprocal piece links define closure.
  /// </summary>
  public bool? SerializedIsCircuit { get; }
  public bool Prototype { get; }
  /// <summary>
  /// Whether <see cref="TrackPieceSourceEntryIds"/> is an authoritative traversal, either retained
  /// from a non-empty serialized Track list or reconstructed from reciprocal piece links.
  /// </summary>
  public bool HasAuthoritativeTrackPieceOrder { get; }
  /// <summary>Whether the DAT Track itself serialized a non-empty TrackPiece list.</summary>
  public bool HasSerializedTrackPieceOrder { get; }
  public IReadOnlyList<ulong> TrackPieceSourceEntryIds { get; }
  public IReadOnlyList<ulong> SegmentSourceEntryIds { get; }
  public int FlexiColour0 { get; }
  public int FlexiColour1 { get; }
  public int FlexiColour2 { get; }
  public ulong TrackedRideInstanceReference { get; }
  public bool? FlippedTrackSections { get; }
  public int? TunnelLightColour { get; }

  internal RideTrack(
    ulong sourceEntryId,
    int direction,
    ulong firstSegmentSourceEntryId,
    ulong lastSegmentSourceEntryId,
    bool? isCircuit,
    bool prototype,
    bool hasSerializedTrackPieceOrder,
    ulong[] trackPieceSourceEntryIds,
    ulong[] segmentSourceEntryIds,
    int flexiColour0,
    int flexiColour1,
    int flexiColour2,
    ulong trackedRideInstanceReference,
    bool? flippedTrackSections,
    int? tunnelLightColour,
    bool? serializedIsCircuit = null,
    bool? hasAuthoritativeTrackPieceOrder = null
  ) {
    ArgumentNullException.ThrowIfNull(trackPieceSourceEntryIds);
    ArgumentNullException.ThrowIfNull(segmentSourceEntryIds);

    SourceEntryId = sourceEntryId;
    Direction = direction;
    FirstSegmentSourceEntryId = firstSegmentSourceEntryId;
    LastSegmentSourceEntryId = lastSegmentSourceEntryId;
    IsCircuit = isCircuit;
    SerializedIsCircuit = serializedIsCircuit;
    Prototype = prototype;
    HasSerializedTrackPieceOrder = hasSerializedTrackPieceOrder;
    HasAuthoritativeTrackPieceOrder = hasAuthoritativeTrackPieceOrder
      ?? hasSerializedTrackPieceOrder;
    TrackPieceSourceEntryIds = Array.AsReadOnly((ulong[])trackPieceSourceEntryIds.Clone());
    SegmentSourceEntryIds = Array.AsReadOnly((ulong[])segmentSourceEntryIds.Clone());
    FlexiColour0 = flexiColour0;
    FlexiColour1 = flexiColour1;
    FlexiColour2 = flexiColour2;
    TrackedRideInstanceReference = trackedRideInstanceReference;
    FlippedTrackSections = flippedTrackSections;
    TunnelLightColour = tunnelLightColour;
  }
}
