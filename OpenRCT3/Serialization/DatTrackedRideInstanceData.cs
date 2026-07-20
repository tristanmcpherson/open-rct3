// DatTrackedRideInstanceData
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;

namespace OpenRCT3.Serialization;

/// <summary>
/// Reference-proven identity and train linkage decoded from a DAT
/// <c>TrackedRideInstance</c> entry.
/// </summary>
internal sealed class DatTrackedRideInstanceData {
  public ulong EntryId { get; }
  public string Name { get; }
  public DatSceneryFlexiColour CarFlexiColours { get; }
  public ulong Track { get; }
  public string TrackedRideOverlayName { get; }
  public string TrackedRideSymbolName { get; }
  public int NTrains { get; }
  public int NCarsPerTrain { get; }
  public int TrainSelection { get; }
  public IReadOnlyList<ulong> Trains { get; }

  public DatTrackedRideInstanceData(
    ulong entryId,
    string name,
    ulong track,
    string trackedRideOverlayName,
    string trackedRideSymbolName,
    int nTrains,
    int nCarsPerTrain,
    int trainSelection,
    ulong[] trains
  ) : this(
    entryId,
    name,
    track,
    trackedRideOverlayName,
    trackedRideSymbolName,
    nTrains,
    nCarsPerTrain,
    trainSelection,
    trains,
    new DatSceneryFlexiColour(1, 2, 3)) { }

  public DatTrackedRideInstanceData(
    ulong entryId,
    string name,
    ulong track,
    string trackedRideOverlayName,
    string trackedRideSymbolName,
    int nTrains,
    int nCarsPerTrain,
    int trainSelection,
    ulong[] trains,
    DatSceneryFlexiColour carFlexiColours
  ) {
    ArgumentNullException.ThrowIfNull(name);
    ArgumentNullException.ThrowIfNull(trackedRideOverlayName);
    ArgumentNullException.ThrowIfNull(trackedRideSymbolName);
    ArgumentNullException.ThrowIfNull(trains);

    EntryId = entryId;
    Name = name;
    CarFlexiColours = carFlexiColours;
    Track = track;
    TrackedRideOverlayName = trackedRideOverlayName;
    TrackedRideSymbolName = trackedRideSymbolName;
    NTrains = nTrains;
    NCarsPerTrain = nCarsPerTrain;
    TrainSelection = trainSelection;
    Trains = Array.AsReadOnly((ulong[])trains.Clone());
  }
}
