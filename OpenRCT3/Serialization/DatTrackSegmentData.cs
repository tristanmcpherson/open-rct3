// DAT Track Segment Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>Topology fields retained from one serialized RCT3 <c>TrackSegment</c> entry.</summary>
internal sealed class DatTrackSegmentData {
  public ulong EntryId { get; }
  public int Direction { get; }
  public ulong FirstPiece { get; }
  public ulong LastPiece { get; }
  public ulong NextSegment { get; }
  public ulong PrevSegment { get; }
  public bool Prototype { get; }
  public ulong Track { get; }

  public DatTrackSegmentData(
    ulong entryId,
    int direction,
    ulong firstPiece,
    ulong lastPiece,
    ulong nextSegment,
    ulong prevSegment,
    bool prototype,
    ulong track
  ) {
    EntryId = entryId;
    Direction = direction;
    FirstPiece = firstPiece;
    LastPiece = lastPiece;
    NextSegment = nextSegment;
    PrevSegment = prevSegment;
    Prototype = prototype;
    Track = track;
  }
}
