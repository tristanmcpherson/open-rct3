// Ride Track Segment
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Simulation;

/// <summary>One semantic DAT track segment linking a ride-track root to its placed pieces.</summary>
public sealed class RideTrackSegment {
  public ulong SourceEntryId { get; }
  public ulong RideTrackSourceEntryId { get; }
  public int Direction { get; }
  public ulong FirstPieceSourceEntryId { get; }
  public ulong LastPieceSourceEntryId { get; }
  public ulong NextSegmentReference { get; }
  public ulong PreviousSegmentReference { get; }
  public bool Prototype { get; }

  internal RideTrackSegment(
    ulong sourceEntryId,
    ulong rideTrackSourceEntryId,
    int direction,
    ulong firstPieceSourceEntryId,
    ulong lastPieceSourceEntryId,
    ulong nextSegmentReference,
    ulong previousSegmentReference,
    bool prototype
  ) {
    SourceEntryId = sourceEntryId;
    RideTrackSourceEntryId = rideTrackSourceEntryId;
    Direction = direction;
    FirstPieceSourceEntryId = firstPieceSourceEntryId;
    LastPieceSourceEntryId = lastPieceSourceEntryId;
    NextSegmentReference = nextSegmentReference;
    PreviousSegmentReference = previousSegmentReference;
    Prototype = prototype;
  }
}
