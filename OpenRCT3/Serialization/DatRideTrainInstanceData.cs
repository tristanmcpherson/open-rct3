// DAT Ride Train Instance Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;

namespace OpenRCT3.Serialization;

/// <summary>
/// Exact saved resource identity, ownership, and seed dimensions from one DAT
/// <c>RideTrainInstance</c> entry.
/// </summary>
internal sealed class DatRideTrainInstanceData {
  public ulong EntryId { get; }
  public string RideTrainOverlayName { get; }
  public string RideTrainSymbolName { get; }
  public ulong TrackedRideInstance { get; }
  public int WhichTrain { get; }
  public float Length { get; }
  public float Mass { get; }
  public IReadOnlyList<ulong> Cars { get; }

  public DatRideTrainInstanceData(
    ulong entryId,
    string rideTrainOverlayName,
    string rideTrainSymbolName,
    ulong trackedRideInstance,
    int whichTrain,
    float length,
    float mass,
    ulong[]? cars = null
  ) {
    ArgumentNullException.ThrowIfNull(rideTrainOverlayName);
    ArgumentNullException.ThrowIfNull(rideTrainSymbolName);
    if (!float.IsFinite(length))
      throw new ArgumentOutOfRangeException(nameof(length), "Ride-train length must be finite.");
    if (!float.IsFinite(mass))
      throw new ArgumentOutOfRangeException(nameof(mass), "Ride-train mass must be finite.");

    EntryId = entryId;
    RideTrainOverlayName = rideTrainOverlayName;
    RideTrainSymbolName = rideTrainSymbolName;
    TrackedRideInstance = trackedRideInstance;
    WhichTrain = whichTrain;
    Length = length;
    Mass = mass;
    Cars = Array.AsReadOnly((ulong[])(cars?.Clone() ?? Array.Empty<ulong>()));
  }
}
