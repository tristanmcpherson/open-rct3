namespace OpenRCT3.Tests;

[TestFixture]
public class GameLoopTimingTests {
  [Test]
  public void PlanSimulation_UsesUpdateIntervalAndPreservesCappedBacklog() {
    var plan = GameLoopTiming.PlanSimulation(
      TimeSpan.FromMilliseconds(95),
      TimeSpan.FromMilliseconds(10),
      maxTicks: 8);

    using (Assert.EnterMultipleScope()) {
      Assert.That(plan.TickCount, Is.EqualTo(8));
      Assert.That(plan.TickDelta, Is.EqualTo(TimeSpan.FromMilliseconds(10)));
      Assert.That(plan.TickInterpolation, Is.EqualTo(1.0));
      Assert.That(plan.RemainingLag, Is.EqualTo(TimeSpan.FromMilliseconds(15)));
    }
  }

  [Test]
  public void PlanSimulation_BelowUpdateThresholdLeavesLagForNextFrame() {
    var lag = TimeSpan.FromMilliseconds(9);

    var plan = GameLoopTiming.PlanSimulation(
      lag,
      TimeSpan.FromMilliseconds(10),
      maxTicks: 8);

    using (Assert.EnterMultipleScope()) {
      Assert.That(plan.TickCount, Is.Zero);
      Assert.That(plan.RemainingLag, Is.EqualTo(lag));
    }
  }

  [Test]
  public void CalculateFrameSleep_SubtractsOnlyCurrentFrameWork() {
    var sleep = GameLoopTiming.CalculateFrameSleep(
      TimeSpan.FromMilliseconds(16),
      TimeSpan.FromMilliseconds(6));

    Assert.That(sleep, Is.EqualTo(TimeSpan.FromMilliseconds(10)));
  }

  [TestCase(16)]
  [TestCase(20)]
  public void CalculateFrameSleep_FrameAtOrOverBudgetDoesNotSleep(double workMilliseconds) {
    var sleep = GameLoopTiming.CalculateFrameSleep(
      TimeSpan.FromMilliseconds(16),
      TimeSpan.FromMilliseconds(workMilliseconds));

    Assert.That(sleep, Is.EqualTo(TimeSpan.Zero));
  }

  [Test]
  public void UpdateRateConversion_UsesReciprocalSecondsSemantics() {
    var interval = GameLoopTiming.UpdateInterval(50);
    var rate = GameLoopTiming.UpdatesPerSecond(TimeSpan.FromMilliseconds(20));

    using (Assert.EnterMultipleScope()) {
      Assert.That(interval, Is.EqualTo(TimeSpan.FromMilliseconds(20)));
      Assert.That(rate, Is.EqualTo(50));
    }
  }

  [TestCase(0.0)]
  [TestCase(-1.0)]
  [TestCase(double.NaN)]
  [TestCase(double.PositiveInfinity)]
  public void UpdateInterval_NonpositiveOrNonfiniteRateIsRejected(double rate) {
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      GameLoopTiming.UpdateInterval(rate)));
  }

  [Test]
  public void PlanSimulation_NonpositiveUpdateIntervalIsRejected() {
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      GameLoopTiming.PlanSimulation(TimeSpan.Zero, TimeSpan.Zero, maxTicks: 8)));
  }
}
