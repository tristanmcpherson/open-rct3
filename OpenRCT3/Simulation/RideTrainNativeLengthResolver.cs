// Ride Train Native Length Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The typed outcome of reproducing the native ride-train length accumulator.</summary>
internal enum RideTrainNativeLengthStatus {
  Resolved,
  EmptyConsist,
  CarCountExceedsLimit,
  MalformedRuntimeEntry,
  UnresolvedConsist,
  CarCountMismatch,
  ChangedTrainIdentity,
  ChangedCarOrder,
  UnresolvedCarResource,
  ChangedCarResourceIdentity,
  SavedPhysicalStateUnavailable,
  NonFiniteSavedLength,
  NonFiniteRuntimeGeometry,
  NonFiniteAccumulation,
  RuntimeGeometryUnavailable,
}

/// <summary>A resolved native train length or typed fail-closed evidence.</summary>
internal sealed record RideTrainNativeLengthResult(
  RideTrainNativeLengthStatus Status,
  int CarCount,
  int? FailedCarIndex,
  float? Length
) {
  public bool IsResolved => Status == RideTrainNativeLengthStatus.Resolved;
}

/// <summary>One aligned runtime car and its authoritative longitudinal geometry.</summary>
internal sealed record RideTrainNativeLengthInput(
  RideCarInstanceRuntimeEntry RuntimeEntry,
  RideCarLongitudinalGeometry Geometry,
  bool HasRearGeometry
);

/// <summary>Validates and reproduces the native ride-train length accumulator.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> SHA-256
/// <c>1C9316E728D67AAA3BFE36D0A1634F3139582927440A5467C351E4C949B5B21D</c> adds each current
/// runtime car length from <c>+0x190</c> at <c>0x00AAEF84</c>. The first-car gate at
/// <c>0x00AAEF96</c> then adds <c>[car+0x414]-&gt;[+0xD4]-&gt;[+0x0C]</c>, and the final-car gate
/// at <c>0x00AAF019</c> adds <c>[+0x10]</c>. Runtime-car construction loads the raw RideCar at
/// <c>0x00A5381B</c> and calls <c>0x00F3F980</c> at <c>0x00A5383E</c>. That callee lazily
/// allocates the RideCar-owned 28-byte <c>+0xD4</c> record at <c>0x00F3F990</c> through
/// <c>0x00F3F99C</c>, then copies all fields at <c>0x00F3F9A6</c> through <c>0x00F3F9B9</c>.
/// The safe operational names for <c>+0x0C</c> and <c>+0x10</c> are first-end and last-end
/// extensions; the exact model-marker business names remain unknown. The overload without
/// geometry retains fail-closed saved-state validation and never exposes its subtotal as native.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/car.h#L155-L161">
/// rct3-importer RideCar_V layout; <c>unk54</c> is always serialized as zero
/// </seealso>
internal static class RideTrainNativeLengthResolver {
  public static RideTrainNativeLengthResult Resolve(
    IReadOnlyList<RideCarInstanceRuntimeEntry> orderedCars
  ) => Resolve(orderedCars, RideTrainNativeLengthResolverLimits.Default);

  internal static RideTrainNativeLengthResult Resolve(
    IReadOnlyList<RideCarInstanceRuntimeEntry> orderedCars,
    RideTrainNativeLengthResolverLimits limits
  ) {
    var validation = ValidateRuntimeEntries(orderedCars, limits, out var cars);
    if (validation != null) return validation;

    var carCount = cars.Length;
    foreach (var index in Enumerable.Range(0, carCount)) {
      var car = cars[index];
      if (!car.HasSavedPhysicalState)
        return Failed(
          RideTrainNativeLengthStatus.SavedPhysicalStateUnavailable,
          carCount,
          index);
      if (!float.IsFinite(car.SavedLength))
        return Failed(RideTrainNativeLengthStatus.NonFiniteSavedLength, carCount, index);
    }

    var length = 0f;
    foreach (var index in Enumerable.Range(0, carCount)) {
      length += cars[index].SavedLength;
      if (!float.IsFinite(length))
        return Failed(RideTrainNativeLengthStatus.NonFiniteAccumulation, carCount, index);
    }

    return Failed(RideTrainNativeLengthStatus.RuntimeGeometryUnavailable, carCount);
  }

  public static RideTrainNativeLengthResult Resolve(
    IReadOnlyList<RideTrainNativeLengthInput> orderedCars
  ) => Resolve(orderedCars, RideTrainNativeLengthResolverLimits.Default);

  internal static RideTrainNativeLengthResult Resolve(
    IReadOnlyList<RideTrainNativeLengthInput> orderedCars,
    RideTrainNativeLengthResolverLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(orderedCars);
    ValidateLimits(limits);

    var carCount = orderedCars.Count;
    if (carCount == 0)
      return Failed(RideTrainNativeLengthStatus.EmptyConsist, carCount);
    if (carCount > limits.MaximumCarCount)
      return Failed(RideTrainNativeLengthStatus.CarCountExceedsLimit, carCount);

    var inputs = new RideTrainNativeLengthInput[carCount];
    var runtimeCars = new RideCarInstanceRuntimeEntry[carCount];
    foreach (var index in Enumerable.Range(0, carCount)) {
      var input = orderedCars[index];
      if (input?.RuntimeEntry == null)
        return Failed(
          RideTrainNativeLengthStatus.MalformedRuntimeEntry,
          carCount,
          index);
      inputs[index] = input;
      runtimeCars[index] = input.RuntimeEntry;
    }

    var validation = ValidateRuntimeEntries(runtimeCars, limits, out _);
    if (validation != null) return validation;

    foreach (var index in Enumerable.Range(0, carCount)) {
      var input = inputs[index];
      if (input.Geometry == null)
        return Failed(
          RideTrainNativeLengthStatus.RuntimeGeometryUnavailable,
          carCount,
          index);
      if (!float.IsFinite(input.Geometry.CarLength) ||
          (index == 0 &&
           !float.IsFinite(input.Geometry.FrontWheelCenterOffsetFromCarFront)) ||
          (index == carCount - 1 && input.HasRearGeometry &&
           !float.IsFinite(input.Geometry.RearWheelCenterOffsetFromFrontWheelCenter)))
        return Failed(
          RideTrainNativeLengthStatus.NonFiniteRuntimeGeometry,
          carCount,
          index);
    }

    var length = 0f;
    foreach (var index in Enumerable.Range(0, carCount)) {
      var input = inputs[index];
      length += input.Geometry.CarLength;
      if (!float.IsFinite(length))
        return Failed(RideTrainNativeLengthStatus.NonFiniteAccumulation, carCount, index);

      if (index == 0) {
        length += FirstEndExtension(input.Geometry);
        if (!float.IsFinite(length))
          return Failed(RideTrainNativeLengthStatus.NonFiniteAccumulation, carCount, index);
      }

      if (index == carCount - 1) {
        length += LastEndExtension(input);
        if (!float.IsFinite(length))
          return Failed(RideTrainNativeLengthStatus.NonFiniteAccumulation, carCount, index);
      }
    }

    return new(RideTrainNativeLengthStatus.Resolved, carCount, null, length);
  }

  private static RideTrainNativeLengthResult? ValidateRuntimeEntries(
    IReadOnlyList<RideCarInstanceRuntimeEntry> orderedCars,
    RideTrainNativeLengthResolverLimits limits,
    out RideCarInstanceRuntimeEntry[] cars
  ) {
    ArgumentNullException.ThrowIfNull(orderedCars);
    ValidateLimits(limits);

    var carCount = orderedCars.Count;
    cars = [];
    if (carCount == 0)
      return Failed(RideTrainNativeLengthStatus.EmptyConsist, carCount);
    if (carCount > limits.MaximumCarCount)
      return Failed(RideTrainNativeLengthStatus.CarCountExceedsLimit, carCount);

    cars = new RideCarInstanceRuntimeEntry[carCount];
    foreach (var index in Enumerable.Range(0, carCount)) {
      var car = orderedCars[index];
      if (car == null || car.TrainRuntime == null || car.CarInstance == null)
        return Failed(
          RideTrainNativeLengthStatus.MalformedRuntimeEntry,
          carCount,
          index);
      cars[index] = car;
    }

    var trainRuntime = cars[0].TrainRuntime;
    var trainResource = trainRuntime.TrainResource;
    if (trainResource?.TrainInstance?.Cars == null)
      return Failed(RideTrainNativeLengthStatus.MalformedRuntimeEntry, carCount, 0);

    var savedTrain = trainResource.TrainInstance;
    var consist = cars[0].TrainConsist;
    if (consist == null || !consist.IsResolved)
      return Failed(RideTrainNativeLengthStatus.UnresolvedConsist, carCount, 0);
    if (consist.Cars == null || consist.Roles?.Entries == null)
      return Failed(RideTrainNativeLengthStatus.MalformedRuntimeEntry, carCount, 0);
    if (!ReferenceEquals(consist.TrainRuntime, trainRuntime))
      return Failed(RideTrainNativeLengthStatus.ChangedTrainIdentity, carCount, 0);
    if (savedTrain.Cars.Count != carCount ||
        consist.Cars.Count != carCount ||
        consist.Roles.Entries.Count != carCount)
      return Failed(RideTrainNativeLengthStatus.CarCountMismatch, carCount);

    foreach (var index in Enumerable.Range(0, carCount)) {
      var car = cars[index];
      if (!ReferenceEquals(car.TrainRuntime, trainRuntime) ||
          !ReferenceEquals(car.TrainConsist, consist) ||
          car.CarInstance.RideTrainInstance != savedTrain.EntryId)
        return Failed(RideTrainNativeLengthStatus.ChangedTrainIdentity, carCount, index);
      if (car.CarInstance.WhichCar != index ||
          savedTrain.Cars[index] != car.CarInstance.EntryId)
        return Failed(RideTrainNativeLengthStatus.ChangedCarOrder, carCount, index);

      var consistCar = consist.Cars[index];
      if (consistCar == null || car.ConsistCar == null || car.CarResource == null ||
          car.ResourceStatus != RideCarResourceRuntimeStatus.Resolved ||
          !car.CarResource.IsResolved || car.CarResource.Car == null)
        return Failed(RideTrainNativeLengthStatus.UnresolvedCarResource, carCount, index);
      if (consistCar.Role.RuntimeIndex != index ||
          consist.Roles.Entries[index] != consistCar.Role ||
          car.SavedRole != consistCar.Role.Role ||
          car.CarInstance.WhichRideTrainCar != Convert.ToInt32(car.SavedRole))
        return Failed(RideTrainNativeLengthStatus.ChangedCarOrder, carCount, index);
      if (!ReferenceEquals(car.ConsistCar, consistCar) ||
          !ReferenceEquals(car.CarResource, consistCar.CarResource) ||
          car.CarResource.Role != car.SavedRole)
        return Failed(
          RideTrainNativeLengthStatus.ChangedCarResourceIdentity,
          carCount,
          index);
    }

    return null;
  }

  private static float FirstEndExtension(RideCarLongitudinalGeometry geometry) =>
    MathF.Max(geometry.FrontWheelCenterOffsetFromCarFront, 0f);

  private static float LastEndExtension(RideTrainNativeLengthInput input) {
    if (!input.HasRearGeometry) return 0f;

    var rearExtent = -input.Geometry.RearWheelCenterOffsetFromFrontWheelCenter;
    if (rearExtent <= input.Geometry.CarLength) return 0f;
    return rearExtent - input.Geometry.CarLength;
  }

  private static void ValidateLimits(RideTrainNativeLengthResolverLimits limits) {
    if (limits.MaximumCarCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static RideTrainNativeLengthResult Failed(
    RideTrainNativeLengthStatus status,
    int carCount,
    int? failedCarIndex = null
  ) => new(status, carCount, failedCarIndex, null);
}

internal readonly record struct RideTrainNativeLengthResolverLimits(int MaximumCarCount) {
  public static RideTrainNativeLengthResolverLimits Default { get; } = new(16_384);
}
