// Ride Train Operational State Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrainOperationalStateTests {
  private static readonly (RideTrainOperationalState State, string NativeName)[] NativeStates = [
    (RideTrainOperationalState.WaitingToLoad, "StateWaitingToLoad"),
    (RideTrainOperationalState.Loading, "StateLoading"),
    (RideTrainOperationalState.LoadingReadyToGo, "StateLoadingReadyToGo"),
    (RideTrainOperationalState.RestraintsClosing, "StateRestraintsClosing"),
    (RideTrainOperationalState.DoorsClosing, "StateDoorsClosing"),
    (RideTrainOperationalState.WaitingToGoAtStation, "StateWaitingToGoAtStation"),
    (RideTrainOperationalState.WaitingToGoAtStationSyncOtherTrains,
      "StateWaitingToGoAtStationSyncOtherTrains"),
    (RideTrainOperationalState.WaitingToGoAtStationSyncAdjacentStations,
      "StateWaitingToGoAtStationSyncAdjacentStations"),
    (RideTrainOperationalState.WaitingForCableLiftAtStation,
      "StateWaitingForCableLiftAtStation"),
    (RideTrainOperationalState.XxxxEngagingCableLift, "XXXXStateEngagingCableLift"),
    (RideTrainOperationalState.AscendingCableLift, "StateAscendingCableLift"),
    (RideTrainOperationalState.DisengagingCableLift, "StateDisengagingCableLift"),
    (RideTrainOperationalState.WaitingToGoAtCableLift, "StateWaitingToGoAtCableLift"),
    (RideTrainOperationalState.TravelToNextStation, "StateTravelToNextStation"),
    (RideTrainOperationalState.WaitingToGoAtLiftHill, "StateWaitingToGoAtLiftHill"),
    (RideTrainOperationalState.StoppingAtBlockBrake, "StateStoppingAtBlockBrake"),
    (RideTrainOperationalState.WaitingToGoAtBlockBrake, "StateWaitingToGoAtBlockBrake"),
    (RideTrainOperationalState.WaitingForCableLiftAtBlockBrake,
      "StateWaitingForCableLiftAtBlockBrake"),
    (RideTrainOperationalState.WaitingAtTop, "StateWaitingAtTop"),
    (RideTrainOperationalState.StoppingOnHoldingPiece, "StateStoppingOnHoldingPiece"),
    (RideTrainOperationalState.StoppedOnHoldingPiece, "StateStoppedOnHoldingPiece"),
    (RideTrainOperationalState.WaitingOnHoldingPiece, "StateWaitingOnHoldingPiece"),
    (RideTrainOperationalState.StoppingAtStation, "StateStoppingAtStation"),
    (RideTrainOperationalState.StoppedAtStation, "StateStoppedAtStation"),
    (RideTrainOperationalState.AligningAtStation, "StateAligningAtStation"),
    (RideTrainOperationalState.WaitingToUnloadAtStation, "StateWaitingToUnloadAtStation"),
    (RideTrainOperationalState.WaitingToUnloadAtStationSyncOtherTrains,
      "StateWaitingToUnloadAtStationSyncOtherTrains"),
    (RideTrainOperationalState.DoorsOpening, "StateDoorsOpening"),
    (RideTrainOperationalState.RestraintsOpening, "StateRestraintsOpening"),
    (RideTrainOperationalState.Unloading, "StateUnloading"),
    (RideTrainOperationalState.StoppingAtThrillLift, "StateStoppingAtThrillLift"),
    (RideTrainOperationalState.AscendingThrillLift, "StateAscendingThrillLift"),
    (RideTrainOperationalState.WaitingToGoAtThrillLift, "StateWaitingToGoAtThrillLift"),
    (RideTrainOperationalState.CableLiftIdle, "StateCableLiftIdle"),
    (RideTrainOperationalState.CableLiftDescending, "StateCableLiftDescending"),
    (RideTrainOperationalState.CableLiftEngaging, "StateCableLiftEngaging"),
    (RideTrainOperationalState.CableLiftAscending, "StateCableLiftAscending"),
    (RideTrainOperationalState.CableLiftDisengaging, "StateCableLiftDisengaging"),
    (RideTrainOperationalState.ThrillLiftIdle, "StateThrillLiftIdle"),
    (RideTrainOperationalState.ThrillLiftEngaging, "StateThrillLiftEngaging"),
    (RideTrainOperationalState.ThrillLiftAscending, "StateThrillLiftAscending"),
    (RideTrainOperationalState.ThrillLiftWaitingAtTop, "StateThrillLiftWaitingAtTop"),
    (RideTrainOperationalState.ThrillLiftDisengaging, "StateThrillLiftDisengaging"),
    (RideTrainOperationalState.ThrillLiftTrainLeaving, "StateThrillLiftTrainLeaving"),
    (RideTrainOperationalState.ThrillLiftDescending, "StateThrillLiftDescending"),
    (RideTrainOperationalState.Crashing, "StateCrashing"),
    (RideTrainOperationalState.OffTrack, "StateOffTrack"),
    (RideTrainOperationalState.OffTrackReturning, "StateOffTrackReturning"),
    (RideTrainOperationalState.OffTrackReturnedAligning, "StateOffTrackReturnedAligning"),
    (RideTrainOperationalState.OffTrackReturnedEntering, "StateOffTrackReturnedEntering"),
    (RideTrainOperationalState.ThrillLiftEntering, "StateThrillLiftEntering"),
    (RideTrainOperationalState.Crashed, "StateCrashed"),
    (RideTrainOperationalState.SlowingAtBlockBrake, "StateSlowingAtBlockBrake"),
    (RideTrainOperationalState.WaitingForReverseCableLiftAtStation,
      "StateWaitingForReverseCableLiftAtStation"),
    (RideTrainOperationalState.XxxxEngagingReverseCableLift,
      "XXXXStateEngagingReverseCableLift"),
    (RideTrainOperationalState.AscendingReverseCableLift,
      "StateAscendingReverseCableLift"),
    (RideTrainOperationalState.DisengagingReverseCableLift,
      "StateDisengagingReverseCableLift"),
    (RideTrainOperationalState.WaitingToGoAtReverseCableLift,
      "StateWaitingToGoAtReverseCableLift"),
    (RideTrainOperationalState.ReverseCableLiftIdle, "StateReverseCableLiftIdle"),
    (RideTrainOperationalState.ReverseCableLiftDescending,
      "StateReverseCableLiftDescending"),
    (RideTrainOperationalState.ReverseCableLiftEngaging,
      "StateReverseCableLiftEngaging"),
    (RideTrainOperationalState.ReverseCableLiftAscending,
      "StateReverseCableLiftAscending"),
    (RideTrainOperationalState.ReverseCableLiftDisengaging,
      "StateReverseCableLiftDisengaging"),
    (RideTrainOperationalState.WaitingToAscendCableLiftSyncAdjacentStations,
      "StateWaitingToAscendCableLiftSyncAdjacentStations"),
    (RideTrainOperationalState.CableLiftWaitingToAscend,
      "StateCableLiftWaitingToAscend"),
    (RideTrainOperationalState.WaitingToAscendReverseCableLiftSyncAdjacentStations,
      "StateWaitingToAscendReverseCableLiftSyncAdjacentStations"),
    (RideTrainOperationalState.ReverseCableLiftWaitingToAscend,
      "StateReverseCableLiftWaitingToAscend"),
    (RideTrainOperationalState.FillingWithWater, "StateFillingWithWater"),
    (RideTrainOperationalState.OffTrackHeldInvisible, "StateOffTrackHeldInvisible"),
    (RideTrainOperationalState.BadState, "BAD STATE"),
    (RideTrainOperationalState.SlowingForObstruction, "StateSlowingForObstruction"),
  ];

  [Test]
  public void Resolve_MapsEveryNativeStateNameAndRawValue() {
    Assert.That(NativeStates, Has.Length.EqualTo(71));

    for (var rawValue = 0; rawValue < NativeStates.Length; rawValue++) {
      var expected = NativeStates[rawValue];
      var actual = RideTrainOperationalStateCatalog.Resolve(rawValue);

      using (Assert.EnterMultipleScope()) {
        Assert.That(Convert.ToInt32(expected.State), Is.EqualTo(rawValue));
        Assert.That(actual.RawValue, Is.EqualTo(rawValue));
        Assert.That(actual.KnownState, Is.EqualTo(expected.State));
        Assert.That(actual.NativeName, Is.EqualTo(expected.NativeName));
        Assert.That(actual.IsKnown, Is.True);
      }
    }
  }

  [TestCase(-1)]
  [TestCase(71)]
  [TestCase(int.MinValue)]
  [TestCase(int.MaxValue)]
  public void Resolve_LeavesValuesOutsideNativeTableUnknown(int rawValue) {
    var result = RideTrainOperationalStateCatalog.Resolve(rawValue);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.RawValue, Is.EqualTo(rawValue));
      Assert.That(result.KnownState, Is.Null);
      Assert.That(result.NativeName, Is.Null);
      Assert.That(result.IsKnown, Is.False);
    }
  }

  [TestCase(1)]
  [TestCase(13)]
  [TestCase(22)]
  [TestCase(28)]
  [TestCase(51)]
  [TestCase(69)]
  public void TryAdvance_UpdatesTimerForKnownStateRegardlessOfMotionBehavior(int rawState) {
    var accepted = RideTrainOperationalStateClock.TryAdvance(
      rawState,
      16_777_216f,
      0.75f,
      out var stateTime);

    using (Assert.EnterMultipleScope()) {
      Assert.That(accepted, Is.True);
      Assert.That(
        BitConverter.SingleToInt32Bits(stateTime),
        Is.EqualTo(BitConverter.SingleToInt32Bits(0.75f + 16_777_216f)));
    }
  }

  [Test]
  public void TryAdvance_RejectsUnknownNonFiniteAndOverflowWithoutChangingTimer() {
    var unknownAccepted = RideTrainOperationalStateClock.TryAdvance(71, 4f, 1f, out var unknown);
    var nonFiniteAccepted = RideTrainOperationalStateClock.TryAdvance(
      13,
      4f,
      float.NaN,
      out var nonFinite);
    var overflowAccepted = RideTrainOperationalStateClock.TryAdvance(
      13,
      float.MaxValue,
      float.MaxValue,
      out var overflow);

    using (Assert.EnterMultipleScope()) {
      Assert.That(unknownAccepted, Is.False);
      Assert.That(unknown, Is.EqualTo(4f));
      Assert.That(nonFiniteAccepted, Is.False);
      Assert.That(nonFinite, Is.EqualTo(4f));
      Assert.That(overflowAccepted, Is.False);
      Assert.That(overflow, Is.EqualTo(float.MaxValue));
    }
  }

  [Test]
  public void TryTransition_ResetsTimerOnlyWhenRawStateChanges() {
    var unchangedAccepted = RideTrainOperationalStateClock.TryTransition(
      28,
      -0f,
      28,
      out var unchangedState,
      out var unchangedTime);
    var changedAccepted = RideTrainOperationalStateClock.TryTransition(
      28,
      6.5f,
      29,
      out var changedState,
      out var changedTime);

    using (Assert.EnterMultipleScope()) {
      Assert.That(unchangedAccepted, Is.True);
      Assert.That(unchangedState, Is.EqualTo(28));
      Assert.That(BitConverter.SingleToInt32Bits(unchangedTime),
        Is.EqualTo(BitConverter.SingleToInt32Bits(-0f)));
      Assert.That(changedAccepted, Is.True);
      Assert.That(changedState, Is.EqualTo(29));
      Assert.That(BitConverter.SingleToInt32Bits(changedTime), Is.Zero);
    }
  }

  [Test]
  public void TryTransition_RejectsUnknownOrNonFiniteEvidenceWithoutChangingValues() {
    var unknownCurrentAccepted = RideTrainOperationalStateClock.TryTransition(
      71,
      4f,
      13,
      out var unknownCurrentState,
      out var unknownCurrentTime);
    var unknownRequestedAccepted = RideTrainOperationalStateClock.TryTransition(
      13,
      4f,
      71,
      out var unknownRequestedState,
      out var unknownRequestedTime);
    var nonFiniteAccepted = RideTrainOperationalStateClock.TryTransition(
      13,
      float.PositiveInfinity,
      22,
      out var nonFiniteState,
      out var nonFiniteTime);

    using (Assert.EnterMultipleScope()) {
      Assert.That(unknownCurrentAccepted, Is.False);
      Assert.That(unknownCurrentState, Is.EqualTo(71));
      Assert.That(unknownCurrentTime, Is.EqualTo(4f));
      Assert.That(unknownRequestedAccepted, Is.False);
      Assert.That(unknownRequestedState, Is.EqualTo(13));
      Assert.That(unknownRequestedTime, Is.EqualTo(4f));
      Assert.That(nonFiniteAccepted, Is.False);
      Assert.That(nonFiniteState, Is.EqualTo(13));
      Assert.That(nonFiniteTime, Is.EqualTo(float.PositiveInfinity));
    }
  }
}
