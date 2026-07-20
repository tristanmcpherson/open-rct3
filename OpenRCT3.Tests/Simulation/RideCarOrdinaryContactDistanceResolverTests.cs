// Ride Car Ordinary Contact Distance Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarOrdinaryContactDistanceResolverTests {
  [Test]
  public void Resolve_UsesSignedForwardOffsets() {
    var result = RideCarOrdinaryContactDistanceResolver.Resolve(
      20f,
      Geometry(
        frontOffsetFromCarFront: -1.25f,
        frontOffsetFromCarRear: 4.75f,
        rearOffsetFromFront: -3.5f),
      reversed: false,
      hasRearGeometry: true,
      circuitLength: null);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.FrontDistance, Is.EqualTo(18.75f));
      Assert.That(result.RearDistance, Is.EqualTo(15.25f));
    }
  }

  [Test]
  public void Resolve_ReversedUsesCarRearOffsetAndInvertsSignedRearOffset() {
    var result = RideCarOrdinaryContactDistanceResolver.Resolve(
      20f,
      Geometry(
        frontOffsetFromCarFront: -1.25f,
        frontOffsetFromCarRear: 4.75f,
        rearOffsetFromFront: -3.5f),
      reversed: true,
      hasRearGeometry: true,
      circuitLength: null);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.FrontDistance, Is.EqualTo(15.25f));
      Assert.That(result.RearDistance, Is.EqualTo(18.75f));
    }
  }

  [Test]
  public void Resolve_MissingRearGeometryUsesFrontContactForRear() {
    var geometry = Geometry(
      frontOffsetFromCarFront: -1f,
      frontOffsetFromCarRear: 4f,
      rearOffsetFromFront: float.NaN);

    var result = RideCarOrdinaryContactDistanceResolver.Resolve(
      20f,
      geometry,
      reversed: false,
      hasRearGeometry: false,
      circuitLength: null);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.FrontDistance, Is.EqualTo(19f));
      Assert.That(result.RearDistance, Is.EqualTo(19f));
    }
  }

  [Test]
  public void Resolve_CircuitCorrectsUpperBoundaryExactlyOnce() {
    var result = RideCarOrdinaryContactDistanceResolver.Resolve(
      99f,
      Geometry(2f, 8f, -4f),
      reversed: false,
      hasRearGeometry: true,
      circuitLength: 100f);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.FrontDistance, Is.EqualTo(1f));
      Assert.That(result.RearDistance, Is.EqualTo(97f));
    }
  }

  [Test]
  public void Resolve_CircuitCorrectsLowerBoundaryExactlyOnce() {
    var result = RideCarOrdinaryContactDistanceResolver.Resolve(
      1f,
      Geometry(-3f, 7f, -4f),
      reversed: false,
      hasRearGeometry: true,
      circuitLength: 100f);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.FrontDistance, Is.EqualTo(98f));
      Assert.That(result.RearDistance, Is.EqualTo(94f));
    }
  }

  [TestCase(0f, 0f)]
  [TestCase(100f, 0f)]
  [TestCase(-100f, 0f)]
  public void Resolve_CircuitPinsNativeComparatorBoundaries(
    float baseDistance,
    float expected
  ) {
    var result = RideCarOrdinaryContactDistanceResolver.Resolve(
      baseDistance,
      Geometry(0f, 1f, 0f),
      reversed: false,
      hasRearGeometry: true,
      circuitLength: 100f);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.FrontDistance, Is.EqualTo(expected));
      Assert.That(result.RearDistance, Is.EqualTo(expected));
    }
  }

  [Test]
  public void Resolve_CircuitCorrectsRearIndependentlyFromFront() {
    var result = RideCarOrdinaryContactDistanceResolver.Resolve(
      2f,
      Geometry(0f, 1f, -4f),
      reversed: false,
      hasRearGeometry: true,
      circuitLength: 100f);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.FrontDistance, Is.EqualTo(2f));
      Assert.That(result.RearDistance, Is.EqualTo(98f));
    }
  }

  [TestCase(-101f)]
  [TestCase(200f)]
  public void Resolve_CircuitRejectsInputsRequiringMoreThanOneCorrection(float baseDistance) {
    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarOrdinaryContactDistanceResolver.Resolve(
        baseDistance,
        Geometry(0f, 1f, -4f),
        reversed: false,
        hasRearGeometry: false,
        circuitLength: 100f)));

    Assert.That(exception!.Message, Does.Contain("requires more than one circuit correction"));
  }

  [TestCase(float.NaN)]
  [TestCase(float.PositiveInfinity)]
  [TestCase(float.NegativeInfinity)]
  public void Resolve_RejectsNonFiniteBaseDistance(float baseDistance) {
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideCarOrdinaryContactDistanceResolver.Resolve(
        baseDistance,
        Geometry(-1f, 4f, -3f),
        reversed: false,
        hasRearGeometry: true,
        circuitLength: 100f)));
  }

  [TestCase(0f)]
  [TestCase(-1f)]
  [TestCase(float.NaN)]
  [TestCase(float.PositiveInfinity)]
  public void Resolve_RejectsInvalidCircuitLength(float circuitLength) {
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideCarOrdinaryContactDistanceResolver.Resolve(
        20f,
        Geometry(-1f, 4f, -3f),
        reversed: false,
        hasRearGeometry: true,
        circuitLength: circuitLength)));
  }

  [Test]
  public void Resolve_RejectsNonFiniteRelevantGeometryOffset() {
    var geometry = Geometry(
      frontOffsetFromCarFront: float.NaN,
      frontOffsetFromCarRear: 4f,
      rearOffsetFromFront: -3f);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarOrdinaryContactDistanceResolver.Resolve(
        20f,
        geometry,
        reversed: false,
        hasRearGeometry: true,
        circuitLength: null)));

    Assert.That(exception!.Message, Does.Contain("front wheel-center offset is non-finite"));
  }

  private static RideCarLongitudinalGeometry Geometry(
    float frontOffsetFromCarFront,
    float frontOffsetFromCarRear,
    float rearOffsetFromFront
  ) {
    var carRear = Vector3.Zero;
    var carFront = new Vector3(
      frontOffsetFromCarRear - frontOffsetFromCarFront,
      0f,
      0f);
    var frontWheel = new Vector3(frontOffsetFromCarRear, 0f, 0f);
    var rearWheel = new Vector3(frontOffsetFromCarRear + rearOffsetFromFront, 0f, 0f);
    return new(
      carFront,
      carRear,
      frontWheel,
      rearWheel,
      Vector3.UnitX,
      carFront.X,
      frontWheel.X,
      rearWheel.X,
      Math.Abs(rearOffsetFromFront),
      2f,
      1.5f);
  }
}
