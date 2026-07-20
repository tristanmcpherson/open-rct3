// DAT Ride Train Instance Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;

namespace OpenRCT3.Serialization;

/// <summary>
/// Exact saved resource identity, ownership, dimensions, motion state, and visual variant from one
/// DAT <c>RideTrainInstance</c> entry.
/// </summary>
internal sealed class DatRideTrainInstanceData {
  public ulong EntryId { get; }
  public string RideTrainOverlayName { get; }
  public string RideTrainSymbolName { get; }
  public ulong TrackedRideInstance { get; }
  public int WhichTrain { get; }
  public float Length { get; }
  public float Mass { get; }
  public bool HasSavedMotionState { get; }
  public float Distance { get; }
  public bool Reversed { get; }
  public float Speed { get; }
  public bool HasSavedOperationalState { get; }
  public int State { get; }
  public float StateTime { get; }
  public int? WhichRideCarSivVariant { get; }
  public IReadOnlyList<ulong> Cars { get; }

  public DatRideTrainInstanceData(
    ulong entryId,
    string rideTrainOverlayName,
    string rideTrainSymbolName,
    ulong trackedRideInstance,
    int whichTrain,
    float length,
    float mass,
    ulong[]? cars = null,
    int? whichRideCarSivVariant = null,
    int? state = null,
    float? stateTime = null
  ) : this(
    entryId,
    rideTrainOverlayName,
    rideTrainSymbolName,
    trackedRideInstance,
    whichTrain,
    length,
    mass,
    false,
    0f,
    false,
    0f,
    cars,
    whichRideCarSivVariant,
    state,
    stateTime) { }

  public DatRideTrainInstanceData(
    ulong entryId,
    string rideTrainOverlayName,
    string rideTrainSymbolName,
    ulong trackedRideInstance,
    int whichTrain,
    float length,
    float mass,
    float distance,
    bool reversed,
    float speed,
    ulong[]? cars = null,
    int? whichRideCarSivVariant = null,
    int? state = null,
    float? stateTime = null
  ) : this(
    entryId,
    rideTrainOverlayName,
    rideTrainSymbolName,
    trackedRideInstance,
    whichTrain,
    length,
    mass,
    true,
    distance,
    reversed,
    speed,
    cars,
    whichRideCarSivVariant,
    state,
    stateTime) { }

  private DatRideTrainInstanceData(
    ulong entryId,
    string rideTrainOverlayName,
    string rideTrainSymbolName,
    ulong trackedRideInstance,
    int whichTrain,
    float length,
    float mass,
    bool hasSavedMotionState,
    float distance,
    bool reversed,
    float speed,
    ulong[]? cars,
    int? whichRideCarSivVariant,
    int? state,
    float? stateTime
  ) {
    ArgumentNullException.ThrowIfNull(rideTrainOverlayName);
    ArgumentNullException.ThrowIfNull(rideTrainSymbolName);
    if (!float.IsFinite(length))
      throw new ArgumentOutOfRangeException(nameof(length), "Ride-train length must be finite.");
    if (!float.IsFinite(mass))
      throw new ArgumentOutOfRangeException(nameof(mass), "Ride-train mass must be finite.");
    if (!float.IsFinite(distance))
      throw new ArgumentOutOfRangeException(nameof(distance), "Ride-train distance must be finite.");
    if (!float.IsFinite(speed))
      throw new ArgumentOutOfRangeException(nameof(speed), "Ride-train speed must be finite.");
    if (state.HasValue != stateTime.HasValue)
      throw new ArgumentException("Ride-train State and StateTime must be supplied together.");
    if (stateTime.HasValue && !float.IsFinite(stateTime.Value))
      throw new ArgumentOutOfRangeException(
        nameof(stateTime), "Ride-train state time must be finite.");

    EntryId = entryId;
    RideTrainOverlayName = rideTrainOverlayName;
    RideTrainSymbolName = rideTrainSymbolName;
    TrackedRideInstance = trackedRideInstance;
    WhichTrain = whichTrain;
    Length = length;
    Mass = mass;
    HasSavedMotionState = hasSavedMotionState;
    Distance = distance;
    Reversed = reversed;
    Speed = speed;
    HasSavedOperationalState = state.HasValue;
    State = state.GetValueOrDefault();
    StateTime = stateTime.GetValueOrDefault();
    WhichRideCarSivVariant = whichRideCarSivVariant;
    Cars = Array.AsReadOnly((ulong[])(cars?.Clone() ?? Array.Empty<ulong>()));
  }
}
