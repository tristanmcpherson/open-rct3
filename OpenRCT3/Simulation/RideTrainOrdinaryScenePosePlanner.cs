// Ride Train Ordinary Scene Pose Planner
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>One exact runtime/static car identity and its current longitudinal geometry.</summary>
internal sealed record RideTrainOrdinaryScenePoseCarInput(
  RideCarInstanceRuntimeEntry RuntimeEntry,
  RideCarStaticInstanceEntry StaticEntry,
  RideCarLongitudinalGeometry Geometry,
  bool HasRearGeometry
);

/// <summary>A complete immutable ordinary-train pose plan ready for scene application.</summary>
internal sealed record RideTrainOrdinaryScenePosePlan(
  TrackCircuitTraversal Traversal,
  RideTrainMotionState MotionState,
  RideTrainNativeLengthResult NativeLength,
  RideTrainOrdinaryCarDistancePlan CarDistances,
  IReadOnlyList<RideCarOrdinaryPose> CarPoses,
  IReadOnlyList<RideCarSceneTransformTarget> Targets
);

/// <summary>Composes an authorized ordinary train state into exact per-car scene targets.</summary>
/// <remarks>
/// This layer never advances time, selects an operating mode, or infers authorization. Its caller
/// supplies one already-authorized motion state and the exact circuit traversal owned by the
/// runtime train. Current model geometry feeds both Complete Edition's native train-length
/// accumulator and ordinary consist-spacing loop; serialized length snapshots are not consulted.
/// All cars, distances, poses, and transforms are preflighted before an immutable target list is
/// returned, so a failure cannot expose a partial scene update.
/// </remarks>
internal static class RideTrainOrdinaryScenePosePlanner {
  public static RideTrainOrdinaryScenePosePlan Resolve(
    TrackCircuitTraversal traversal,
    RideTrainMotionState motionState,
    IReadOnlyList<RideTrainOrdinaryScenePoseCarInput> orderedCars
  ) {
    ArgumentNullException.ThrowIfNull(traversal);
    ArgumentNullException.ThrowIfNull(orderedCars);

    var maximumCarCount = RideTrainNativeLengthResolverLimits.Default.MaximumCarCount;
    if (orderedCars.Count > maximumCarCount)
      throw new InvalidOperationException(
        $"Ordinary scene-pose car count exceeds the limit {maximumCarCount}.");

    var inputs = new RideTrainOrdinaryScenePoseCarInput[orderedCars.Count];
    var nativeLengthInputs = new RideTrainNativeLengthInput[orderedCars.Count];
    var distanceInputs = new RideTrainOrdinaryCarDistanceInput[orderedCars.Count];
    var registryIndexes = new HashSet<int>();
    var carInstanceEntryIds = new HashSet<ulong>();
    foreach (var index in Enumerable.Range(0, orderedCars.Count)) {
      var input = orderedCars[index];
      ValidateCar(
        input,
        index,
        traversal,
        registryIndexes,
        carInstanceEntryIds);
      if (index > 0 &&
          Convert.ToInt64(input.RuntimeEntry.RegistryIndex) !=
            Convert.ToInt64(inputs[index - 1].RuntimeEntry.RegistryIndex) + 1L)
        throw Invalid($"car {index} changed authoritative saved registry order");

      inputs[index] = input;
      nativeLengthInputs[index] = new(
        input.RuntimeEntry,
        input.Geometry,
        input.HasRearGeometry);
      // Complete Edition spaces cars with the same current +0x190 geometry length.
      distanceInputs[index] = new(
        input.Geometry.CarLength,
        input.Geometry,
        input.HasRearGeometry);
    }

    var nativeLength = RideTrainNativeLengthResolver.Resolve(
      Array.AsReadOnly(nativeLengthInputs));
    ValidateNativeLength(nativeLength, inputs.Length);

    var carDistances = RideTrainOrdinaryCarDistanceResolver.Resolve(
      motionState,
      nativeLength.Length!.Value,
      Array.AsReadOnly(distanceInputs),
      traversal.Circuit.Length);
    ValidateCarDistances(carDistances, inputs.Length);

    var poses = new RideCarOrdinaryPose[inputs.Length];
    var targets = new RideCarSceneTransformTarget[inputs.Length];
    foreach (var index in Enumerable.Range(0, inputs.Length)) {
      var input = inputs[index];
      var pose = RideCarOrdinaryPoseResolver.Resolve(
        traversal,
        carDistances.BaseDistances[index],
        input.Geometry,
        motionState.Reversed,
        input.HasRearGeometry);
      poses[index] = pose;
      targets[index] = new(
        input.StaticEntry.RegistryIndex,
        input.StaticEntry.CarInstanceEntryId,
        pose.Transform);
    }

    return new(
      traversal,
      motionState,
      nativeLength,
      carDistances,
      Array.AsReadOnly(poses),
      Array.AsReadOnly(targets));
  }

  private static void ValidateCar(
    RideTrainOrdinaryScenePoseCarInput? input,
    int index,
    TrackCircuitTraversal traversal,
    ISet<int> registryIndexes,
    ISet<ulong> carInstanceEntryIds
  ) {
    if (input?.RuntimeEntry == null || input.StaticEntry == null || input.Geometry == null)
      throw Invalid($"car {index} has an incomplete runtime, static, or geometry input");
    if (!input.HasRearGeometry)
      throw Invalid($"car {index} has no rear geometry for a two-contact body pose");

    var runtime = input.RuntimeEntry;
    var staticEntry = input.StaticEntry;
    if (runtime.RegistryIndex < 0 || staticEntry.RegistryIndex != runtime.RegistryIndex ||
        !registryIndexes.Add(runtime.RegistryIndex))
      throw Invalid($"car {index} changed or duplicated its saved registry identity");
    if (runtime.CarInstance == null || runtime.CarInstanceEntryId == 0 ||
        !carInstanceEntryIds.Add(runtime.CarInstanceEntryId))
      throw Invalid($"car {index} has a missing or duplicated saved-car entry identity");
    if (!ReferenceEquals(staticEntry.CarRuntime, runtime) ||
        staticEntry.CarInstanceEntryId != runtime.CarInstanceEntryId)
      throw Invalid($"car {index} changed its exact runtime/static identity");
    if (!staticEntry.IsResolved || staticEntry.Geometry == null)
      throw Invalid($"car {index} static instance is unresolved");
    if (!ReferenceEquals(staticEntry.Geometry, input.Geometry))
      throw Invalid($"car {index} changed its exact current geometry identity");

    var trainRuntime = runtime.TrainRuntime;
    if (trainRuntime?.TrackRuntime == null ||
        !ReferenceEquals(trainRuntime.TrackRuntime.CircuitTraversal, traversal))
      throw Invalid($"car {index} does not belong to the exact circuit traversal");
  }

  private static void ValidateNativeLength(
    RideTrainNativeLengthResult? result,
    int carCount
  ) {
    if (result == null || !result.IsResolved ||
        result.Status != RideTrainNativeLengthStatus.Resolved ||
        result.CarCount != carCount || result.FailedCarIndex != null ||
        result.Length is not float length || !float.IsFinite(length) || length < 0f)
      throw Invalid(
        "native train length did not resolve completely for the exact ordered consist");
  }

  private static void ValidateCarDistances(
    RideTrainOrdinaryCarDistancePlan? result,
    int carCount
  ) {
    if (result?.BaseDistances == null || result.BaseDistances.Count != carCount)
      throw Invalid("ordinary car-distance plan is missing or partial");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Cannot plan ordinary ride-train scene poses: {message}.");
}
