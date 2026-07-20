// Ride Train Ordinary Car Distance Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>One car's authoritative inputs to native ordinary consist spacing.</summary>
internal sealed record RideTrainOrdinaryCarDistanceInput(
  float Length,
  RideCarLongitudinalGeometry Geometry,
  bool HasRearGeometry
);

/// <summary>Native ordinary-mode base distances in consist order.</summary>
internal sealed record RideTrainOrdinaryCarDistancePlan(
  IReadOnlyList<float> BaseDistances
);

/// <summary>Reproduces the native ordinary-mode consist-distance loop.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> initializes the consist cursor at <c>0x00AB6236</c> through
/// <c>0x00AB62CB</c>. It visits cars in consist order at <c>0x00AB62F0</c>, adds each saved
/// current ride-car length at <c>+0x190</c> before a reversed car at <c>0x00AB6B30</c>, and
/// subtracts it after an ordinary car at <c>0x00AB6C49</c>. Every circuit correction is at most one
/// add or subtract. The caller must supply that current runtime value; this layer does not claim a
/// serialized length snapshot is bit-identical. It preserves scalar-float operation order and
/// never infers missing geometry, track topology, contacts, or poses.
/// </remarks>
internal static class RideTrainOrdinaryCarDistanceResolver {
  public static RideTrainOrdinaryCarDistancePlan Resolve(
    RideTrainMotionState train,
    float trainLength,
    IReadOnlyList<RideTrainOrdinaryCarDistanceInput> cars,
    float circuitLength
  ) => Resolve(
    train,
    trainLength,
    cars,
    circuitLength,
    RideTrainOrdinaryCarDistanceResolverLimits.Default);

  internal static RideTrainOrdinaryCarDistancePlan Resolve(
    RideTrainMotionState train,
    float trainLength,
    IReadOnlyList<RideTrainOrdinaryCarDistanceInput> cars,
    float circuitLength,
    RideTrainOrdinaryCarDistanceResolverLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(cars);
    ValidateLimits(limits);
    ValidateFiniteNonnegative(trainLength, nameof(trainLength));
    if (!float.IsFinite(train.Distance) || !float.IsFinite(train.Speed))
      throw new ArgumentOutOfRangeException(
        nameof(train),
        "Ordinary ride-train motion state must be finite.");
    if (!float.IsFinite(circuitLength) || circuitLength <= 0f)
      throw new ArgumentOutOfRangeException(
        nameof(circuitLength),
        "Ordinary ride-train circuit length must be finite and positive.");
    if (train.Distance < 0f || train.Distance >= circuitLength)
      throw Invalid("saved train distance is outside its circuit");
    if (cars.Count == 0) throw Invalid("consist is empty");
    if (cars.Count > limits.MaximumCarCount)
      throw new InvalidOperationException(
        $"Ordinary ride-train car count exceeds the limit {limits.MaximumCarCount}.");

    var inputs = cars.ToArray();
    foreach (var index in Enumerable.Range(0, inputs.Length))
      ValidateCar(inputs[index], index);

    var cursor = InitialCursor(train, trainLength, inputs);
    cursor = CorrectLowerOnce(cursor, circuitLength, "initial consist cursor");
    var distances = new float[inputs.Length];
    foreach (var index in Enumerable.Range(0, inputs.Length)) {
      var car = inputs[index];
      if (train.Reversed) {
        cursor += car.Length;
        if (!float.IsFinite(cursor))
          throw Invalid($"car {index} distance accumulation is non-finite");
        if (cursor >= circuitLength) cursor -= circuitLength;
        ValidateCorrected(cursor, circuitLength, $"car {index} base distance");
        distances[index] = cursor;
      } else {
        ValidateCorrected(cursor, circuitLength, $"car {index} base distance");
        distances[index] = cursor;
        cursor -= car.Length;
        if (!float.IsFinite(cursor))
          throw Invalid($"car {index} distance accumulation is non-finite");
        if (cursor < 0f) cursor += circuitLength;
        ValidateCorrected(cursor, circuitLength, $"cursor after car {index}");
      }
    }
    return new(Array.AsReadOnly(distances));
  }

  private static float InitialCursor(
    RideTrainMotionState train,
    float trainLength,
    IReadOnlyList<RideTrainOrdinaryCarDistanceInput> cars
  ) {
    if (!train.Reversed) {
      var frontOffset = cars[0].Geometry.FrontWheelCenterOffsetFromCarFront;
      var clampedOffset = MathF.Max(frontOffset, 0f);
      var cursor = train.Distance - clampedOffset;
      if (!float.IsFinite(cursor)) throw Invalid("initial ordinary cursor is non-finite");
      return cursor;
    }

    var reversedCursor = train.Distance - trainLength;
    if (!float.IsFinite(reversedCursor))
      throw Invalid("initial reversed cursor is non-finite");
    var last = cars[^1];
    if (!last.HasRearGeometry) return reversedCursor;

    var rearExtent = -last.Geometry.RearWheelCenterOffsetFromFrontWheelCenter;
    if (!float.IsFinite(rearExtent)) throw Invalid("last-car rear extent is non-finite");
    if (rearExtent <= last.Length) return reversedCursor;
    rearExtent -= last.Length;
    reversedCursor += rearExtent;
    if (!float.IsFinite(reversedCursor))
      throw Invalid("reversed rear-geometry adjustment is non-finite");
    return reversedCursor;
  }

  private static void ValidateCar(RideTrainOrdinaryCarDistanceInput? car, int index) {
    if (car == null) throw Invalid($"car {index} is null");
    ArgumentNullException.ThrowIfNull(car.Geometry);
    ValidateFiniteNonnegative(car.Length, $"cars[{index}].Length");
    if (!float.IsFinite(car.Geometry.FrontWheelCenterOffsetFromCarFront))
      throw Invalid($"car {index} front offset is non-finite");
    if (car.HasRearGeometry &&
        !float.IsFinite(car.Geometry.RearWheelCenterOffsetFromFrontWheelCenter))
      throw Invalid($"car {index} rear offset is non-finite");
  }

  private static float CorrectLowerOnce(float value, float circuitLength, string description) {
    if (value < 0f) value += circuitLength;
    if (!float.IsFinite(value) || value < 0f)
      throw Invalid($"{description} is negative after one lower correction");
    return value;
  }

  private static void ValidateCorrected(
    float value,
    float circuitLength,
    string description
  ) {
    if (!float.IsFinite(value) || value < 0f || value >= circuitLength)
      throw Invalid($"{description} is outside the circuit after one correction");
  }

  private static void ValidateFiniteNonnegative(float value, string name) {
    if (!float.IsFinite(value) || value < 0f)
      throw new ArgumentOutOfRangeException(name, "Value must be finite and nonnegative.");
  }

  private static void ValidateLimits(RideTrainOrdinaryCarDistanceResolverLimits limits) {
    if (limits.MaximumCarCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Cannot resolve ordinary ride-train car distances: {message}.");
}

internal readonly record struct RideTrainOrdinaryCarDistanceResolverLimits(
  int MaximumCarCount
) {
  public static RideTrainOrdinaryCarDistanceResolverLimits Default { get; } = new(16_384);
}
