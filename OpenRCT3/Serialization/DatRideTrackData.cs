// DAT Ride Track Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;

namespace OpenRCT3.Serialization;

/// <summary>Topology fields retained from one serialized RCT3 <c>Track</c> entry.</summary>
internal sealed class DatRideTrackData {
  public ulong EntryId { get; }
  public int Direction { get; }
  public ulong FirstSegment { get; }
  public bool? IsCircuit { get; }
  public ulong LastSegment { get; }
  public bool Prototype { get; }
  public IReadOnlyList<ulong>? TrackPieces { get; }
  public DatSceneryFlexiColour TrackFlexiColours { get; }
  public ulong TrackedRideInstance { get; }
  public bool? FlippedTrackSections { get; }
  public int? TunnelLightColour { get; }

  public DatRideTrackData(
    ulong entryId,
    int direction,
    ulong firstSegment,
    bool? isCircuit,
    ulong lastSegment,
    bool prototype,
    ulong[]? trackPieces,
    DatSceneryFlexiColour trackFlexiColours,
    ulong trackedRideInstance,
    bool? flippedTrackSections = null,
    int? tunnelLightColour = null
  ) {
    EntryId = entryId;
    Direction = direction;
    FirstSegment = firstSegment;
    IsCircuit = isCircuit;
    LastSegment = lastSegment;
    Prototype = prototype;
    TrackPieces = trackPieces == null
      ? null
      : Array.AsReadOnly((ulong[])trackPieces.Clone());
    TrackFlexiColours = trackFlexiColours;
    TrackedRideInstance = trackedRideInstance;
    FlippedTrackSections = flippedTrackSections;
    TunnelLightColour = tunnelLightColour;
  }
}
