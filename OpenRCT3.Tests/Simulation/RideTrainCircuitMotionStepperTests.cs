// Ride Train Circuit Motion Stepper Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrainCircuitMotionStepperTests {
  [Test]
  public void Advance_PreservesNativeSinglePrecisionMultiplyThenAddOrder() {
    var speed = MathF.BitIncrement(1f);
    var elapsedSeconds = MathF.BitDecrement(1f);

    var advanced = RideTrainCircuitMotionStepper.Advance(
      new RideTrainMotionState(-1f, speed, Reversed: false),
      elapsedSeconds,
      2f);

    using (Assert.EnterMultipleScope()) {
      Assert.That(BitConverter.SingleToInt32Bits(advanced.Distance), Is.Zero);
      Assert.That(advanced.Speed, Is.EqualTo(speed));
      Assert.That(advanced.Reversed, Is.False);
    }
  }

  [Test]
  public void Advance_SubtractsCircuitLengthOnceAtTheUpperBoundary() {
    var boundary = RideTrainCircuitMotionStepper.Advance(
      new RideTrainMotionState(8f, 2f, Reversed: false),
      1f,
      10f);
    var beyond = RideTrainCircuitMotionStepper.Advance(
      new RideTrainMotionState(9f, 3f, Reversed: true),
      1f,
      10f);

    using (Assert.EnterMultipleScope()) {
      Assert.That(boundary.Distance, Is.Zero);
      Assert.That(beyond.Distance, Is.EqualTo(2f));
      Assert.That(beyond.Speed, Is.EqualTo(3f));
      Assert.That(beyond.Reversed, Is.True);
    }
  }

  [Test]
  public void Advance_AddsCircuitLengthOnceBelowTheLowerBoundary() {
    var advanced = RideTrainCircuitMotionStepper.Advance(
      new RideTrainMotionState(1f, -3f, Reversed: true),
      1f,
      10f);

    using (Assert.EnterMultipleScope()) {
      Assert.That(advanced.Distance, Is.EqualTo(8f));
      Assert.That(advanced.Speed, Is.EqualTo(-3f));
      Assert.That(advanced.Reversed, Is.True);
    }
  }

  [Test]
  public void Advance_LeavesAnInRangeDistanceUnchanged() {
    var advanced = RideTrainCircuitMotionStepper.Advance(
      new RideTrainMotionState(4f, 2f, Reversed: false),
      1f,
      10f);

    Assert.That(advanced, Is.EqualTo(
      new RideTrainMotionState(6f, 2f, Reversed: false)));
  }

  [Test]
  public void Advance_FailsWhenOneCorrectionCannotRestoreTheDistance() {
    var upper = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrainCircuitMotionStepper.Advance(
        new RideTrainMotionState(9f, 21f, Reversed: false),
        1f,
        10f)));
    var lower = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrainCircuitMotionStepper.Advance(
        new RideTrainMotionState(1f, -22f, Reversed: false),
        1f,
        10f)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(upper!.Message, Does.Contain("after one native correction"));
      Assert.That(lower!.Message, Does.Contain("after one native correction"));
    }
  }

  [TestCase(0f)]
  [TestCase(-1f)]
  [TestCase(float.NaN)]
  [TestCase(float.PositiveInfinity)]
  public void Advance_RejectsInvalidCircuitLength(float circuitLength) {
    var exception = Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideTrainCircuitMotionStepper.Advance(
        new RideTrainMotionState(1f, 1f, Reversed: false),
        1f,
        circuitLength)));

    Assert.That(exception!.ParamName, Is.EqualTo("circuitLength"));
  }
}
