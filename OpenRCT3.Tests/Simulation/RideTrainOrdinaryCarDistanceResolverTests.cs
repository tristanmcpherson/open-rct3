// Ride Train Ordinary Car Distance Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrainOrdinaryCarDistanceResolverTests {
  [Test]
  public void Resolve_OrdinaryStartsAtClampedFrontExtentAndSubtractsCarLengths() {
    var result = RideTrainOrdinaryCarDistanceResolver.Resolve(
      new(50f, 0f, Reversed: false),
      20f,
      [Car(4f, frontFromFront: 2f), Car(5f, frontFromFront: -2f)],
      100f);

    Assert.That(result.BaseDistances, Is.EqualTo(new[] { 48f, 44f }));
  }

  [Test]
  public void Resolve_OrdinaryPreservesNegativeFrontOffsetAsZeroClamp() {
    var result = RideTrainOrdinaryCarDistanceResolver.Resolve(
      new(50f, 0f, Reversed: false),
      20f,
      [Car(4f, frontFromFront: -2f), Car(5f, frontFromFront: -2f)],
      100f);

    Assert.That(result.BaseDistances, Is.EqualTo(new[] { 50f, 46f }));
  }

  [Test]
  public void Resolve_ReversedAddsLastRearExtentThenEachCarLength() {
    var result = RideTrainOrdinaryCarDistanceResolver.Resolve(
      new(50f, 0f, Reversed: true),
      20f,
      [Car(4f, rearFromFront: -6f), Car(5f, rearFromFront: -6f)],
      100f);

    Assert.That(result.BaseDistances, Is.EqualTo(new[] { 35f, 40f }));
  }

  [Test]
  public void Resolve_ReversedWithoutRearGeometryDoesNotInventAnExtent() {
    var result = RideTrainOrdinaryCarDistanceResolver.Resolve(
      new(50f, 0f, Reversed: true),
      20f,
      [Car(4f), Car(5f, rearFromFront: float.NaN, hasRearGeometry: false)],
      100f);

    Assert.That(result.BaseDistances, Is.EqualTo(new[] { 34f, 39f }));
  }

  [Test]
  public void Resolve_OrdinaryCorrectsEachLowerBoundaryOnce() {
    var result = RideTrainOrdinaryCarDistanceResolver.Resolve(
      new(1f, 0f, Reversed: false),
      20f,
      [Car(4f, frontFromFront: 2f), Car(5f)],
      100f);

    Assert.That(result.BaseDistances, Is.EqualTo(new[] { 99f, 95f }));
  }

  [Test]
  public void Resolve_ReversedCorrectsInitialAndPerCarUpperBoundariesOnce() {
    var result = RideTrainOrdinaryCarDistanceResolver.Resolve(
      new(5f, 0f, Reversed: true),
      20f,
      [Car(10f), Car(5f, rearFromFront: -6f)],
      20f);

    Assert.That(result.BaseDistances, Is.EqualTo(new[] { 16f, 1f }));
  }

  [Test]
  public void Resolve_ReversedDefersAnUpperInitialCursorUntilAfterAddingTheCar() {
    var result = RideTrainOrdinaryCarDistanceResolver.Resolve(
      new(99f, 0f, Reversed: true),
      1f,
      [Car(1f, rearFromFront: -3f)],
      100f);

    Assert.That(result.BaseDistances.Single(), Is.EqualTo(1f));
  }

  [Test]
  public void Resolve_PreservesNativeSinglePrecisionOperationOrder() {
    var result = RideTrainOrdinaryCarDistanceResolver.Resolve(
      new(16_777_216f, 0f, Reversed: false),
      0f,
      [Car(1f, frontFromFront: 1f)],
      33_554_432f);

    Assert.That(result.BaseDistances.Single(), Is.EqualTo(16_777_215f));
  }

  [Test]
  public void Resolve_FailsWhenOneCorrectionCannotRestoreTheCursor() {
    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrainOrdinaryCarDistanceResolver.Resolve(
        new(1f, 0f, Reversed: false),
        0f,
        [Car(250f)],
        100f)));

    Assert.That(exception!.Message, Does.Contain("after one correction"));
  }

  [TestCase(float.NaN)]
  [TestCase(float.PositiveInfinity)]
  [TestCase(-1f)]
  public void Resolve_RejectsInvalidCarLength(float length) {
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideTrainOrdinaryCarDistanceResolver.Resolve(
        new(1f, 0f, Reversed: false),
        0f,
        [Car(length)],
        100f)));
  }

  [Test]
  public void Resolve_RejectsEmptyOrOversizedConsists() {
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrainOrdinaryCarDistanceResolver.Resolve(
        new(1f, 0f, Reversed: false),
        0f,
        [],
        100f)));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideTrainOrdinaryCarDistanceResolver.Resolve(
        new(1f, 0f, Reversed: false),
        0f,
        [Car(1f), Car(1f)],
        100f,
        new RideTrainOrdinaryCarDistanceResolverLimits(1))));
  }

  private static RideTrainOrdinaryCarDistanceInput Car(
    float length,
    float frontFromFront = -2f,
    float rearFromFront = -6f,
    bool hasRearGeometry = true
  ) => new(
    length,
    Geometry(frontFromFront, rearFromFront),
    hasRearGeometry);

  private static RideCarLongitudinalGeometry Geometry(
    float frontFromFront,
    float rearFromFront
  ) {
    var carRear = Vector3.Zero;
    var carFront = new Vector3(10f, 0f, 0f);
    var frontWheel = carFront + new Vector3(frontFromFront, 0f, 0f);
    var rearWheel = frontWheel + new Vector3(rearFromFront, 0f, 0f);
    return new(
      carFront,
      carRear,
      frontWheel,
      rearWheel,
      Vector3.UnitX,
      10f,
      frontWheel.X,
      rearWheel.X,
      Math.Abs(rearFromFront),
      2f,
      2f);
  }
}
