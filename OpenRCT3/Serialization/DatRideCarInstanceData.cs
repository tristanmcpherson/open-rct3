// DAT Ride Car Instance Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>
/// Exact saved ownership, resource-role identity, and resume state from one DAT
/// <c>RideCarInstance</c> entry.
/// </summary>
/// <remarks>
/// The wheel distances are saved global track positions, not the RIC resource's axle or wheel
/// geometry. All track-piece references, distances, direction, and speed are retained as saved
/// state. They are not inferred from resource geometry and do not by themselves authorize motion
/// playback.
/// </remarks>
internal sealed class DatRideCarInstanceData {
  public ulong EntryId { get; }
  public ulong RideTrainInstance { get; }
  public int WhichCar { get; }
  public int WhichRideTrainCar { get; }
  /// <summary>The saved global track distance of the front wheel contact.</summary>
  public float FrontWheelDistance { get; }
  /// <summary>The saved global track distance of the rear wheel contact.</summary>
  public float RearWheelDistance { get; }
  public ulong TrackPiece { get; }
  public ulong RearTrackPiece { get; }
  public float Distance { get; }
  public bool Reversed { get; }
  public float Speed { get; }

  public DatRideCarInstanceData(
    ulong entryId,
    ulong rideTrainInstance,
    int whichCar,
    int whichRideTrainCar,
    ulong trackPiece,
    ulong rearTrackPiece,
    float distance,
    bool reversed,
    float speed
  ) : this(
    entryId,
    rideTrainInstance,
    whichCar,
    whichRideTrainCar,
    0f,
    0f,
    trackPiece,
    rearTrackPiece,
    distance,
    reversed,
    speed) { }

  public DatRideCarInstanceData(
    ulong entryId,
    ulong rideTrainInstance,
    int whichCar,
    int whichRideTrainCar,
    float frontWheelDistance,
    float rearWheelDistance,
    ulong trackPiece,
    ulong rearTrackPiece,
    float distance,
    bool reversed,
    float speed
  ) {
    if (entryId == 0)
      throw new ArgumentOutOfRangeException(nameof(entryId), "Ride-car ID must be nonzero.");
    if (rideTrainInstance == 0)
      throw new ArgumentOutOfRangeException(
        nameof(rideTrainInstance), "Ride-car train ownership must be nonzero.");
    if (whichCar < 0)
      throw new ArgumentOutOfRangeException(
        nameof(whichCar), "Ride-car index must be nonnegative.");
    // The executable's RIT slot getter recognizes Front through Link as values zero through five.
    if (whichRideTrainCar is < 0 or > 5)
      throw new ArgumentOutOfRangeException(
        nameof(whichRideTrainCar), "Ride-car resource role must be between zero and five.");
    if (!float.IsFinite(frontWheelDistance))
      throw new ArgumentOutOfRangeException(
        nameof(frontWheelDistance), "Ride-car front-wheel distance must be finite.");
    if (!float.IsFinite(rearWheelDistance))
      throw new ArgumentOutOfRangeException(
        nameof(rearWheelDistance), "Ride-car rear-wheel distance must be finite.");
    if (!float.IsFinite(distance))
      throw new ArgumentOutOfRangeException(nameof(distance), "Ride-car distance must be finite.");
    if (!float.IsFinite(speed))
      throw new ArgumentOutOfRangeException(nameof(speed), "Ride-car speed must be finite.");

    EntryId = entryId;
    RideTrainInstance = rideTrainInstance;
    WhichCar = whichCar;
    WhichRideTrainCar = whichRideTrainCar;
    FrontWheelDistance = frontWheelDistance;
    RearWheelDistance = rearWheelDistance;
    TrackPiece = trackPiece;
    RearTrackPiece = rearTrackPiece;
    Distance = distance;
    Reversed = reversed;
    Speed = speed;
  }
}
