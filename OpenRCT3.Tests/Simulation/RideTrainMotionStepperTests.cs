// Ride Train Motion Stepper Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrainMotionStepperTests {
  [Test]
  public void Advance_UsesNativeSinglePrecisionMultiplyThenAddOrder() {
    var speed = MathF.BitIncrement(1f);
    var elapsedSeconds = MathF.BitDecrement(1f);
    var state = new RideTrainMotionState(-1f, speed, Reversed: false);

    var advanced = RideTrainMotionStepper.Advance(state, elapsedSeconds);

    // The separately rounded product is exactly 1f, while a fused multiply-add is non-zero.
    Assert.That(BitConverter.SingleToInt32Bits(advanced.Distance), Is.Zero);
  }

  [Test]
  public void Advance_DoesNotNegateSpeedWhenReversedAndPreservesSavedState() {
    var forward = RideTrainMotionStepper.Advance(
      new RideTrainMotionState(10f, 3f, Reversed: false),
      0.5f);
    var reversed = RideTrainMotionStepper.Advance(
      new RideTrainMotionState(10f, 3f, Reversed: true),
      0.5f);

    using (Assert.EnterMultipleScope()) {
      Assert.That(forward.Distance, Is.EqualTo(11.5f));
      Assert.That(reversed.Distance, Is.EqualTo(forward.Distance));
      Assert.That(forward.Speed, Is.EqualTo(3f));
      Assert.That(reversed.Speed, Is.EqualTo(3f));
      Assert.That(forward.Reversed, Is.False);
      Assert.That(reversed.Reversed, Is.True);
    }
  }

  [Test]
  public void Advance_ReturnsRawDistanceWithoutAssumingCircuitNormalization() {
    var advanced = RideTrainMotionStepper.Advance(
      new RideTrainMotionState(99f, 4f, Reversed: false),
      1f);

    Assert.That(advanced.Distance, Is.EqualTo(103f));
  }

  [TestCase(float.NaN, 1f, 1f)]
  [TestCase(float.PositiveInfinity, 1f, 1f)]
  [TestCase(1f, float.NegativeInfinity, 1f)]
  [TestCase(1f, 1f, float.NaN)]
  [TestCase(1f, 1f, float.PositiveInfinity)]
  public void Advance_RejectsNonFiniteInputs(
    float distance,
    float speed,
    float elapsedSeconds
  ) {
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideTrainMotionStepper.Advance(
        new RideTrainMotionState(distance, speed, Reversed: false),
        elapsedSeconds)));
  }

  [Test]
  public void Advance_RejectsMultiplyAndAddOverflow() {
    var multiplyException = Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideTrainMotionStepper.Advance(
        new RideTrainMotionState(0f, float.MaxValue, Reversed: false),
        2f)));
    var addException = Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideTrainMotionStepper.Advance(
        new RideTrainMotionState(float.MaxValue, float.MaxValue, Reversed: false),
        1f)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(multiplyException!.ParamName, Is.EqualTo("elapsedSeconds"));
      Assert.That(addException!.ParamName, Is.EqualTo("state"));
    }
  }
}
