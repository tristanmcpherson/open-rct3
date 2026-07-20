// Ride Train Motion Advance Authorization Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Reflection;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrainMotionAdvanceAuthorizationTests {
  [TestCase(10)]
  [TestCase(13)]
  [TestCase(15)]
  [TestCase(19)]
  [TestCase(22)]
  [TestCase(30)]
  [TestCase(34)]
  [TestCase(36)]
  [TestCase(40)]
  [TestCase(44)]
  [TestCase(45)]
  [TestCase(52)]
  [TestCase(55)]
  [TestCase(59)]
  [TestCase(61)]
  [TestCase(69)]
  [TestCase(70)]
  public void Authorize_AcceptsEveryNativeDistanceStepState(int state) {
    var runtime = Runtime(state, 12.5f);

    var result = RideTrainMotionAdvanceAuthorization.Authorize(runtime);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(
        RideTrainMotionAdvanceAuthorizationStatus.AuthorizedByNativeOperationalState));
      Assert.That(result.IsAuthorized, Is.True);
      Assert.That(result.TrainRuntime, Is.SameAs(runtime));
      Assert.That(result.SavedOperationalState, Is.EqualTo(state));
      Assert.That(result.SavedOperationalStateTime, Is.EqualTo(12.5f));
      Assert.That(runtime.SavedOperationalState, Is.EqualTo(state));
      Assert.That(runtime.SavedOperationalStateTime, Is.EqualTo(12.5f));
    }
  }

  [TestCase(1)]
  [TestCase(28)]
  [TestCase(51)]
  public void Authorize_RejectsRepresentativeNonStepStates(int state) {
    var runtime = Runtime(state, 3f);

    var result = RideTrainMotionAdvanceAuthorization.Authorize(runtime);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(
        RideTrainMotionAdvanceAuthorizationStatus.UnsupportedSavedOperationalState));
      Assert.That(result.IsAuthorized, Is.False);
      Assert.That(result.TrainRuntime, Is.SameAs(runtime));
      Assert.That(result.SavedOperationalState, Is.EqualTo(state));
      Assert.That(result.SavedOperationalStateTime, Is.EqualTo(3f));
    }
  }

  [Test]
  public void Authorize_RejectsRideForwardTrackMismatchEvenWhenTrackBackpointerMatches() {
    var runtime = Runtime(13, 2f, rideTrackEntryId: 701);

    var result = RideTrainMotionAdvanceAuthorization.Authorize(runtime);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(
        RideTrainMotionAdvanceAuthorizationStatus.MalformedRuntimeEntry));
      Assert.That(result.IsAuthorized, Is.False);
      Assert.That(result.SavedOperationalState, Is.EqualTo(13));
      Assert.That(result.SavedOperationalStateTime, Is.EqualTo(2f));
    }
  }

  [Test]
  public void Authorize_FailsClosedForMissingNonFiniteAndMalformedEvidence() {
    var missing = Runtime(state: null, stateTime: null);
    var nonFinite = Runtime(10, 1f);
    var stateTime = typeof(DatRideTrainInstanceData).GetField(
      "<StateTime>k__BackingField",
      BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.That(stateTime, Is.Not.Null);
    stateTime!.SetValue(nonFinite.TrainResource.TrainInstance, float.NaN);
    var valid = Runtime(13, 2f);
    var malformed = valid with { TrackRuntime = null! };

    var missingResult = RideTrainMotionAdvanceAuthorization.Authorize(missing);
    var nonFiniteResult = RideTrainMotionAdvanceAuthorization.Authorize(nonFinite);
    var malformedResult = RideTrainMotionAdvanceAuthorization.Authorize(malformed);
    var nullResult = RideTrainMotionAdvanceAuthorization.Authorize(null);

    using (Assert.EnterMultipleScope()) {
      Assert.That(missingResult.Status, Is.EqualTo(
        RideTrainMotionAdvanceAuthorizationStatus.MissingSavedOperationalState));
      Assert.That(missingResult.SavedOperationalState, Is.Null);
      Assert.That(missingResult.SavedOperationalStateTime, Is.Null);
      Assert.That(nonFiniteResult.Status, Is.EqualTo(
        RideTrainMotionAdvanceAuthorizationStatus.NonFiniteSavedOperationalStateTime));
      Assert.That(nonFiniteResult.SavedOperationalState, Is.EqualTo(10));
      Assert.That(float.IsNaN(nonFiniteResult.SavedOperationalStateTime!.Value), Is.True);
      Assert.That(malformedResult.Status, Is.EqualTo(
        RideTrainMotionAdvanceAuthorizationStatus.MalformedRuntimeEntry));
      Assert.That(malformedResult.SavedOperationalState, Is.EqualTo(13));
      Assert.That(malformedResult.SavedOperationalStateTime, Is.EqualTo(2f));
      Assert.That(nullResult.Status, Is.EqualTo(
        RideTrainMotionAdvanceAuthorizationStatus.MalformedRuntimeEntry));
    }
  }

  private static RideInstanceTrainRuntimeEntry Runtime(
    int? state,
    float? stateTime,
    ulong rideTrackEntryId = 700
  ) {
    var ride = new DatTrackedRideInstanceData(
      900,
      "Ride 900",
      track: rideTrackEntryId,
      "Tracks\\Synthetic",
      "Synthetic:trr",
      nTrains: 1,
      nCarsPerTrain: 1,
      trainSelection: 0,
      trains: [1_000]);
    var track = new RideTrack(
      700,
      direction: 0,
      firstSegmentSourceEntryId: 800,
      lastSegmentSourceEntryId: 801,
      isCircuit: true,
      prototype: false,
      hasSerializedTrackPieceOrder: false,
      trackPieceSourceEntryIds: [9_001],
      segmentSourceEntryIds: [800, 801],
      flexiColour0: 0,
      flexiColour1: 0,
      flexiColour2: 0,
      trackedRideInstanceReference: ride.EntryId,
      flippedTrackSections: null,
      tunnelLightColour: null,
      serializedIsCircuit: false,
      hasAuthoritativeTrackPieceOrder: true);
    var trackRuntime = new RideInstanceTrackRuntimeEntry(
      0,
      0,
      new RideInstanceTrackLink(ride, track),
      new RideTrackGeometryLink(
        track,
        RideTrackGeometryStatus.UnsupportedGeometry,
        Graph: null,
        Circuit: null),
      GraphTraversal: null,
      CircuitTraversal: null);
    var train = new DatRideTrainInstanceData(
      1_000,
      "Cars\\Synthetic\\SyntheticTrain",
      "SyntheticTrain:rit",
      ride.EntryId,
      whichTrain: 0,
      length: 12.5f,
      mass: 1_000f,
      cars: [],
      state: state,
      stateTime: stateTime);
    return new(
      0,
      trackRuntime,
      new RideTrainInstanceResourceLink(ride, train, Ordinal: 0, Source: null));
  }
}
