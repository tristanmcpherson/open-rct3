// Ride Instance Train Runtime Registry Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideInstanceTrainRuntimeRegistryTests {
  [Test]
  public void Build_ComposesExactSavedTrainsInForwardReferenceOrder() {
    var firstRide = Instance(900, 700, [1_000, 1_001]);
    var secondRide = Instance(901, 701, [1_002]);
    var trackRuntime = Runtime([firstRide, secondRide]);
    var trainResources = RideTrainInstanceResourceRegistry.Build(
      [firstRide, secondRide],
      [
        Train(1_002, secondRide, 0),
        Train(1_001, firstRide, 1),
        Train(1_000, firstRide, 0),
      ],
      []);

    var registry = RideInstanceTrainRuntimeRegistry.Build(trackRuntime, trainResources);

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.RideInstanceCount, Is.EqualTo(2));
      Assert.That(registry.LinkedTrainCount, Is.EqualTo(3));
      Assert.That(registry.SavedTrainCount, Is.EqualTo(3));
      Assert.That(registry.UnreferencedTrainCount, Is.Zero);
      Assert.That(registry.ResolvedTrackTrainCount, Is.Zero);
      Assert.That(registry.ResolvedResourceTrainCount, Is.Zero);
      Assert.That(registry.ResolvedCircuitTrainCount, Is.Zero);
      Assert.That(registry.Entries.Select(entry => entry.SavedTrainIndex),
        Is.EqualTo(new[] { 0, 1, 2 }));
      Assert.That(registry.Entries.Select(entry => entry.RideInstanceEntryId),
        Is.EqualTo(new ulong[] { 900, 900, 901 }));
      Assert.That(registry.Entries.Select(entry => entry.TrainInstanceEntryId),
        Is.EqualTo(new ulong[] { 1_000, 1_001, 1_002 }));
      Assert.That(registry.Entries.Select(entry => entry.TrainOrdinal),
        Is.EqualTo(new[] { 0, 1, 0 }));
      Assert.That(registry.Entries[0].TrackRuntime.Instance, Is.SameAs(firstRide));
      Assert.That(registry.Entries[2].TrackRuntime.Instance, Is.SameAs(secondRide));
      Assert.That(registry.Entries[0].TrainResource,
        Is.SameAs(trainResources.Links[0]));
    }
  }

  [Test]
  public void Build_RetainsUnreferencedSavedTrainWithoutInventingRuntimeLink() {
    var ride = Instance(900, 700, []);
    var unreferenced = Train(1_000, ride, 0);

    var registry = RideInstanceTrainRuntimeRegistry.Build(
      Runtime([ride]),
      RideTrainInstanceResourceRegistry.Build([ride], [unreferenced], []));

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.RideInstanceCount, Is.EqualTo(1));
      Assert.That(registry.LinkedTrainCount, Is.Zero);
      Assert.That(registry.SavedTrainCount, Is.EqualTo(1));
      Assert.That(registry.UnreferencedTrainCount, Is.EqualTo(1));
      Assert.That(registry.Entries, Is.Empty);
    }
  }

  [Test]
  public void Build_PreservesSavedOperationalStateWithoutInterpretingIt() {
    var ride = Instance(900, 700, [1_000]);
    var train = Train(1_000, ride, 0, state: 45, stateTime: 6.5f);
    var trainResources = RideTrainInstanceResourceRegistry.Build([ride], [train], []);

    var entry = RideInstanceTrainRuntimeRegistry.Build(
      Runtime([ride]), trainResources).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.HasSavedOperationalState, Is.True);
      Assert.That(entry.SavedOperationalState, Is.EqualTo(45));
      Assert.That(entry.SavedOperationalStateTime, Is.EqualTo(6.5f));
    }
  }

  [Test]
  public void Build_RejectsRideObjectSubstitutionEvenWhenIdsMatch() {
    var runtimeRide = Instance(900, 700, [1_000]);
    var substitutedRide = Instance(900, 700, [1_000]);
    var trainResources = RideTrainInstanceResourceRegistry.Build(
      [substitutedRide],
      [Train(1_000, substitutedRide, 0)],
      []);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrainRuntimeRegistry.Build(Runtime([runtimeRide]), trainResources)));

    Assert.That(exception!.Message, Does.Contain("changed exact DAT object identity"));
  }

  [Test]
  public void Build_EnforcesRideAndTrainBounds() {
    var ride = Instance(900, 700, [1_000]);
    var secondRide = Instance(901, 701, []);
    var runtime = Runtime([ride]);
    var twoRideRuntime = Runtime([ride, secondRide]);
    var trainResources = RideTrainInstanceResourceRegistry.Build(
      [ride],
      [Train(1_000, ride, 0)],
      []);

    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideInstanceTrainRuntimeRegistry.Build(
        runtime,
        trainResources,
        new(MaximumRideInstanceCount: 1, MaximumTrainCount: 0))));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideInstanceTrainRuntimeRegistry.Build(
        twoRideRuntime,
        trainResources,
        new(MaximumRideInstanceCount: 1, MaximumTrainCount: 1))));
  }

  private static RideInstanceTrackRuntimeRegistry Runtime(
    IReadOnlyList<DatTrackedRideInstanceData> instances
  ) {
    var tracks = instances.Select(instance => Track(instance.Track, instance.EntryId)).ToArray();
    var identities = RideInstanceTrackGraph.Build(instances, tracks);
    var geometry = new RideTrackGeometryResolution(
      tracks.Select(track => new RideTrackGeometryLink(
        track,
        RideTrackGeometryStatus.UnsupportedGeometry,
        Graph: null,
        Circuit: null)).ToArray(),
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: tracks.Length,
      UnsupportedTopologyTrackCount: 0);
    return RideInstanceTrackRuntimeRegistry.Build(identities, geometry);
  }

  private static DatTrackedRideInstanceData Instance(
    ulong entryId,
    ulong trackId,
    ulong[] trains
  ) => new(
    entryId,
    $"Ride {entryId}",
    trackId,
    @"Tracks\Synthetic",
    "Synthetic:trr",
    nTrains: trains.Length,
    nCarsPerTrain: 4,
    trainSelection: 0,
    trains);

  private static DatRideTrainInstanceData Train(
    ulong entryId,
    DatTrackedRideInstanceData owner,
    int ordinal,
    int? state = null,
    float? stateTime = null
  ) => new(
    entryId,
    @"Cars\Synthetic\SyntheticTrain",
    "SyntheticTrain:rit",
    owner.EntryId,
    ordinal,
    length: 12.5f,
    mass: 1_000f,
    cars: [],
    state: state,
    stateTime: stateTime);

  private static RideTrack Track(ulong entryId, ulong instanceReference) => new(
    entryId,
    direction: 0,
    firstSegmentSourceEntryId: entryId + 100,
    lastSegmentSourceEntryId: entryId + 100,
    isCircuit: null,
    prototype: false,
    hasSerializedTrackPieceOrder: false,
    trackPieceSourceEntryIds: [],
    segmentSourceEntryIds: [entryId + 100],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: instanceReference,
    flippedTrackSections: null,
    tunnelLightColour: null);
}
