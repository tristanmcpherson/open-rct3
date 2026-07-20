// Ride Instance Resource Load Result
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>Counts of reference-backed ride resources decoded from the exact loaded closure.</summary>
internal readonly record struct RideResourceDecodeCounts(
  int TrackedRides,
  int RideTrains,
  int RideCars,
  int SceneryItemVisuals
);

/// <summary>
/// Exact DAT ride-instance links plus the bounded TRR/RIT/RIC/SVD and SHS/BSH graph reachable from
/// their roots.
/// </summary>
internal sealed class RideInstanceResourceLoadResult {
  public static RideInstanceResourceLoadResult Empty { get; } = new(
    [],
    RideTrainInstanceResourceRegistry.Empty,
    new RideResourceGraph([], 0),
    new RideCarVisualResourceBridgeResult([], 0),
    new RideResourceDecodeCounts(0, 0, 0, 0));

  public IReadOnlyList<RideInstanceResourceLink> Instances { get; }
  public RideTrainInstanceResourceRegistry TrainInstances { get; }
  public RideResourceGraph Graph { get; }
  public RideCarVisualResourceBridgeResult CarVisuals { get; }
  public RideResourceDecodeCounts DecodedCounts { get; }
  public int ResolvedInstanceCount { get; }
  public int UnresolvedInstanceCount => Instances.Count - ResolvedInstanceCount;
  public int SavedTrainCount => TrainInstances.SavedInstanceCount;
  public int ResolvedSavedTrainCount => TrainInstances.ResolvedInstanceCount;
  public int UnresolvedSavedTrainCount => TrainInstances.UnresolvedInstanceCount;

  internal RideInstanceResourceLoadResult(
    IReadOnlyList<RideInstanceResourceLink> instances,
    RideTrainInstanceResourceRegistry trainInstances,
    RideResourceGraph graph,
    RideCarVisualResourceBridgeResult carVisuals,
    RideResourceDecodeCounts decodedCounts
  ) {
    ArgumentNullException.ThrowIfNull(instances);
    ArgumentNullException.ThrowIfNull(trainInstances);
    ArgumentNullException.ThrowIfNull(graph);
    ArgumentNullException.ThrowIfNull(carVisuals);
    if (decodedCounts.TrackedRides < 0 ||
        decodedCounts.RideTrains < 0 ||
        decodedCounts.RideCars < 0 ||
        decodedCounts.SceneryItemVisuals < 0)
      throw new ArgumentOutOfRangeException(
        nameof(decodedCounts),
        "Decoded ride-resource counts cannot be negative.");

    Instances = Array.AsReadOnly(instances.ToArray());
    TrainInstances = trainInstances;
    Graph = graph;
    CarVisuals = carVisuals;
    DecodedCounts = decodedCounts;
    ResolvedInstanceCount = Instances.Count(link => link.IsResolved);
  }
}
