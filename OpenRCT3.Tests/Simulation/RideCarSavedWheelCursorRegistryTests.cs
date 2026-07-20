// Ride Car Saved Wheel Cursor Registry Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarSavedWheelCursorRegistryTests {
  [Test]
  public void Build_ResolvesSavedContactsWithIndependentWrapAndExactCachedPieces() {
    var fixture = CircuitFixture(reversed: true);
    var traversal = fixture.CarRuntime.Entries.Single().TrainRuntime.TrackRuntime.CircuitTraversal!;
    var starts = PieceStarts(traversal.Circuit);
    var frontDistance = Convert.ToSingle(starts[1] + 2d + traversal.Length);
    var rearDistance = Convert.ToSingle(starts[3] + 3d - traversal.Length);
    fixture = CircuitFixture(
      reversed: true,
      frontDistance,
      rearDistance,
      frontPieceIndex: 1,
      rearPieceIndex: 3,
      backwardsStarts: [0f, 0f, 0f, Convert.ToSingle(starts[3])],
      forwardStarts: [0f, Convert.ToSingle(starts[1]), 0f, 0f]);

    var registry = RideCarSavedWheelCursorRegistry.Build(
      fixture.CarRuntime,
      fixture.TrackPieces);
    var entry = registry.Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.CarCount, Is.EqualTo(1));
      Assert.That(registry.ResolvedCarCount, Is.EqualTo(1));
      Assert.That(registry.ResolvedContactCount, Is.EqualTo(2));
      Assert.That(entry.CarRuntime, Is.SameAs(fixture.CarRuntime.Entries.Single()));
      Assert.That(entry.RequiresPiContactFrameRotation, Is.True);
      Assert.That(entry.Front.Status, Is.EqualTo(RideCarSavedWheelCursorStatus.Resolved));
      Assert.That(entry.Front.SplineStart,
        Is.EqualTo(RideCarSavedWheelSplineStart.Forward));
      Assert.That(entry.Front.SavedTrackPieceEntryId, Is.EqualTo(fixture.PieceIds[1]));
      Assert.That(entry.Front.Cursor!.Value.PieceIndex, Is.EqualTo(1));
      Assert.That(entry.Front.Cursor.Value.PieceArcLength, Is.EqualTo(2d).Within(0.00001d));
      Assert.That(entry.Rear.Status, Is.EqualTo(RideCarSavedWheelCursorStatus.Resolved));
      Assert.That(entry.Rear.SplineStart,
        Is.EqualTo(RideCarSavedWheelSplineStart.Backwards));
      Assert.That(entry.Rear.SavedTrackPieceEntryId, Is.EqualTo(fixture.PieceIds[3]));
      Assert.That(entry.Rear.Cursor!.Value.PieceIndex, Is.EqualTo(3));
      Assert.That(entry.Rear.Cursor.Value.PieceArcLength, Is.EqualTo(3d).Within(0.00001d));
      Assert.That(entry.Front.NormalizedCircuitDistance,
        Is.EqualTo(starts[1] + 2d).Within(0.00001d));
      Assert.That(entry.Rear.NormalizedCircuitDistance,
        Is.EqualTo(starts[3] + 3d).Within(0.00001d));
      Assert.That(entry.Front.Sample!.Value.CircuitPiece.Piece,
        Is.SameAs(entry.CarRuntime.TrackPiece.Piece));
      Assert.That(entry.Rear.Sample!.Value.CircuitPiece.Piece,
        Is.SameAs(entry.CarRuntime.RearTrackPiece.Piece));
      Assert.That(IsFinite(entry.Front.Sample.Value), Is.True);
      Assert.That(IsFinite(entry.Rear.Sample.Value), Is.True);
    }
  }

  [Test]
  public void Build_DoesNotUseReversedToChooseSplineStart() {
    var normal = CircuitFixture(
      reversed: false,
      frontDistance: 5f,
      rearDistance: 5f,
      frontPieceIndex: 0,
      rearPieceIndex: 0,
      forwardStarts: [0f, 0f, 0f, 0f],
      backwardsStarts: [100f, 0f, 0f, 0f]);
    var reversed = CircuitFixture(
      reversed: true,
      frontDistance: 5f,
      rearDistance: 5f,
      frontPieceIndex: 0,
      rearPieceIndex: 0,
      forwardStarts: [0f, 0f, 0f, 0f],
      backwardsStarts: [100f, 0f, 0f, 0f]);

    var normalEntry = RideCarSavedWheelCursorRegistry.Build(
      normal.CarRuntime,
      normal.TrackPieces).Entries.Single();
    var reversedEntry = RideCarSavedWheelCursorRegistry.Build(
      reversed.CarRuntime,
      reversed.TrackPieces).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(normalEntry.Front.SplineStart,
        Is.EqualTo(RideCarSavedWheelSplineStart.Forward));
      Assert.That(reversedEntry.Front.SplineStart,
        Is.EqualTo(RideCarSavedWheelSplineStart.Forward));
      Assert.That(reversedEntry.Front.Cursor!.Value.PieceIndex,
        Is.EqualTo(normalEntry.Front.Cursor!.Value.PieceIndex));
      Assert.That(reversedEntry.Front.Cursor.Value.PieceArcLength,
        Is.EqualTo(normalEntry.Front.Cursor.Value.PieceArcLength));
      Assert.That(normalEntry.RequiresPiContactFrameRotation, Is.False);
      Assert.That(reversedEntry.RequiresPiContactFrameRotation, Is.True);
    }
  }

  [Test]
  public void Build_ExposesAmbiguousOutOfRangeAndMissingDataAsTypedOutcomes() {
    var ambiguous = CircuitFixture(
      frontDistance: 5f,
      rearDistance: 5f,
      frontPieceIndex: 0,
      rearPieceIndex: 0,
      forwardStarts: [0f, 0f, 0f, 0f],
      backwardsStarts: [1f, 0f, 0f, 0f]);
    var outOfRange = CircuitFixture(
      frontDistance: 5f,
      rearDistance: 5f,
      frontPieceIndex: 0,
      rearPieceIndex: 0,
      forwardStarts: [100f, 0f, 0f, 0f],
      backwardsStarts: [200f, 0f, 0f, 0f]);
    var missing = CircuitFixture(
      frontDistance: 5f,
      rearDistance: 5f,
      frontPieceIndex: 0,
      rearPieceIndex: 0,
      forwardStarts: [0f, 0f, 0f, 0f],
      backwardsStarts: [100f, 0f, 0f, 0f]);
    var circuitLength = missing.CarRuntime.Entries.Single()
      .TrainRuntime.TrackRuntime.CircuitTraversal!.Length;
    var outsideCircuit = CircuitFixture(
      frontDistance: Convert.ToSingle((2d * circuitLength) + 5d),
      rearDistance: 5f,
      frontPieceIndex: 0,
      rearPieceIndex: 0,
      forwardStarts: [0f, 0f, 0f, 0f],
      backwardsStarts: [100f, 0f, 0f, 0f]);

    var ambiguousEntry = RideCarSavedWheelCursorRegistry.Build(
      ambiguous.CarRuntime,
      ambiguous.TrackPieces).Entries.Single();
    var outOfRangeEntry = RideCarSavedWheelCursorRegistry.Build(
      outOfRange.CarRuntime,
      outOfRange.TrackPieces).Entries.Single();
    var missingEntry = RideCarSavedWheelCursorRegistry.Build(
      missing.CarRuntime,
      missing.TrackPieces.Skip(1).ToArray()).Entries.Single();
    var outsideCircuitEntry = RideCarSavedWheelCursorRegistry.Build(
      outsideCircuit.CarRuntime,
      outsideCircuit.TrackPieces).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(ambiguousEntry.Front.Status,
        Is.EqualTo(RideCarSavedWheelCursorStatus.AmbiguousSplineStart));
      Assert.That(ambiguousEntry.Front.Cursor, Is.Null);
      Assert.That(ambiguousEntry.Front.Sample, Is.Null);
      Assert.That(outOfRangeEntry.Front.Status,
        Is.EqualTo(RideCarSavedWheelCursorStatus.DistanceOutsideSavedPiece));
      Assert.That(outOfRangeEntry.Front.Cursor, Is.Null);
      Assert.That(missingEntry.Front.Status,
        Is.EqualTo(RideCarSavedWheelCursorStatus.MissingTrackPieceData));
      Assert.That(missingEntry.Front.TrackPieceData, Is.Null);
      Assert.That(outsideCircuitEntry.Front.Status,
        Is.EqualTo(RideCarSavedWheelCursorStatus.DistanceOutsideCircuit));
      Assert.That(outsideCircuitEntry.Front.NormalizedCircuitDistance, Is.Null);
    }
  }

  [Test]
  public void Build_ExposesOpenUnresolvedAndMissingReferencesWithoutInventingCursors() {
    var open = NonCircuitFixture(resolvedOpenTrack: true, includeSavedReferences: true);
    var unresolved = NonCircuitFixture(resolvedOpenTrack: false, includeSavedReferences: true);
    var missing = NonCircuitFixture(resolvedOpenTrack: false, includeSavedReferences: false);

    var openEntry = RideCarSavedWheelCursorRegistry.Build(
      open.CarRuntime,
      open.TrackPieces).Entries.Single();
    var unresolvedEntry = RideCarSavedWheelCursorRegistry.Build(
      unresolved.CarRuntime,
      unresolved.TrackPieces).Entries.Single();
    var missingEntry = RideCarSavedWheelCursorRegistry.Build(
      missing.CarRuntime,
      missing.TrackPieces).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(openEntry.Front.Status,
        Is.EqualTo(RideCarSavedWheelCursorStatus.UnsupportedOpenTrack));
      Assert.That(unresolvedEntry.Front.Status,
        Is.EqualTo(RideCarSavedWheelCursorStatus.UnresolvedTrack));
      Assert.That(missingEntry.Front.Status,
        Is.EqualTo(RideCarSavedWheelCursorStatus.MissingSavedReference));
      Assert.That(new[] { openEntry, unresolvedEntry, missingEntry }
        .SelectMany(entry => new[] { entry.Front, entry.Rear })
        .All(contact => contact.Cursor == null && contact.Sample == null), Is.True);
    }
  }

  [Test]
  public void Build_RejectsContradictoryOwnerOrderDuplicatesAndBounds() {
    var fixture = CircuitFixture(
      frontDistance: 5f,
      rearDistance: 5f,
      frontPieceIndex: 0,
      rearPieceIndex: 0,
      forwardStarts: [0f, 0f, 0f, 0f],
      backwardsStarts: [100f, 0f, 0f, 0f]);
    var first = fixture.TrackPieces[0];
    var wrongOwner = Replace(first, owner: 999);
    var wrongOrder = Replace(first, next: fixture.PieceIds[2]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarSavedWheelCursorRegistry.Build(
        fixture.CarRuntime,
        [wrongOwner, .. fixture.TrackPieces.Skip(1)])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarSavedWheelCursorRegistry.Build(
        fixture.CarRuntime,
        [wrongOrder, .. fixture.TrackPieces.Skip(1)])));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarSavedWheelCursorRegistry.Build(
        fixture.CarRuntime,
        [first, first])));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarSavedWheelCursorRegistry.Build(
        fixture.CarRuntime,
        fixture.TrackPieces,
        new RideCarSavedWheelCursorRegistryLimits(0, 4))));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarSavedWheelCursorRegistry.Build(
        fixture.CarRuntime,
        fixture.TrackPieces,
        new RideCarSavedWheelCursorRegistryLimits(1, 3))));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets and the BoxOffice DAT.")]
  public void Build_BoxOfficeResolvesAllSavedWheelContactsToExactCachedPieces() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(string.IsNullOrWhiteSpace(installRoot), Is.False);
    var mapPath = Path.Combine(installRoot!, "Campaigns", "Base", "BoxOffice.dat");
    Assert.That(File.Exists(mapPath), Is.True, mapPath);

    var data = DatTerrainReader.Read(mapPath);
    var terrain = Terrain.FromData(data);
    try {
      var park = new Park(terrain);
      SceneryManagerLoader.Load(
        park,
        terrain,
        data.SceneryItems,
        data.SceneryItemPlacements);
      RideTrackManagerLoader.Load(park, terrain, data.TrackPieces);
      RideTrackTopologyLoader.Load(park, data.RideTracks, data.TrackSegments);
      using var loaded = RideTrackResourceCatalogLoader.Load(
        installRoot!,
        park.RideTrackPlacements,
        data.TrackedRideInstances,
        data.RideTrainInstances);
      var resources = loaded.Catalog.ResolveAll(park.RideTrackPlacements);
      var geometry = RideTrackGeometryResolver.Resolve(terrain, park.RideTracks, resources);
      var instanceTracks = RideInstanceTrackGraph.Build(
        data.TrackedRideInstances,
        park.RideTracks);
      var trackRuntime = RideInstanceTrackRuntimeRegistry.Build(instanceTracks, geometry);
      var trainRuntime = RideInstanceTrainRuntimeRegistry.Build(
        trackRuntime,
        loaded.RideResources.TrainInstances);
      var carRuntime = RideCarInstanceRuntimeRegistry.Build(
        trainRuntime,
        data.RideCarInstances);
      var registry = RideCarSavedWheelCursorRegistry.Build(carRuntime, data.TrackPieces);
      var contacts = registry.Entries
        .SelectMany(entry => new[] { entry.Front, entry.Rear })
        .ToArray();

      foreach (var entry in registry.Entries)
        TestContext.Progress.WriteLine(
          $"car={entry.CarInstanceEntryId} " +
          $"front={Format(entry.Front)} rear={Format(entry.Rear)}");

      using (Assert.EnterMultipleScope()) {
        Assert.That(registry.CarCount, Is.EqualTo(7));
        Assert.That(registry.ResolvedCarCount, Is.EqualTo(7));
        Assert.That(registry.ResolvedContactCount, Is.EqualTo(14));
        Assert.That(registry.Entries.Select(entry => entry.CarInstanceEntryId),
          Is.EqualTo(new ulong[] {
            20_402, 20_403, 20_404, 20_405, 20_406, 20_407, 20_408,
          }));
        Assert.That(registry.Entries.All(entry => !entry.RequiresPiContactFrameRotation), Is.True);
        Assert.That(contacts.All(contact => contact.IsResolved), Is.True);
        Assert.That(contacts.Select(contact => contact.SplineStart),
          Is.All.EqualTo(RideCarSavedWheelSplineStart.Forward));
        Assert.That(contacts.All(contact =>
          contact.Cursor!.Value.CircuitPiece.Piece ==
            contact.Sample!.Value.CircuitPiece.Piece), Is.True);
        Assert.That(contacts.All(contact =>
          contact.Cursor!.Value.PieceArcLength >= 0d &&
          contact.Cursor.Value.PieceArcLength <=
            contact.Cursor.Value.CircuitPiece.Piece.Length), Is.True);
        Assert.That(contacts.All(contact => IsFinite(contact.Sample!.Value)), Is.True);
        Assert.That(registry.Entries.Select(entry => entry.Front.SavedTrackPieceEntryId),
          Is.EqualTo(new ulong[] { 9_902, 9_899, 9_896, 9_893, 9_890, 9_887, 9_886 }));
        Assert.That(registry.Entries.Select(entry => entry.Rear.SavedTrackPieceEntryId),
          Is.EqualTo(new ulong[] { 9_899, 9_896, 9_893, 9_890, 9_887, 9_886, 5_407 }));
      }
    } finally {
      terrain.TextureCatalog?.Dispose();
    }
  }

  private static CircuitTestFixture CircuitFixture(
    bool reversed = false,
    float? frontDistance = null,
    float? rearDistance = null,
    int frontPieceIndex = 0,
    int rearPieceIndex = 0,
    float[]? forwardStarts = null,
    float[]? backwardsStarts = null
  ) {
    var pieceIds = new ulong[] { 710, 711, 712, 713 };
    var runtimePieces = CirclePieces();
    var circuit = new TrackCircuit(pieceIds.Select((id, index) =>
      new TrackCircuitPiece($"track-piece-{id}", runtimePieces[index])));
    var starts = PieceStarts(circuit);
    forwardStarts ??= starts.Take(pieceIds.Length).Select(Convert.ToSingle).ToArray();
    backwardsStarts ??= Enumerable.Repeat(100f, pieceIds.Length).ToArray();
    if (forwardStarts.Length != pieceIds.Length || backwardsStarts.Length != pieceIds.Length)
      throw new ArgumentException("Synthetic start-distance arrays must match the circuit.");

    var resolvedFrontDistance = frontDistance ?? Convert.ToSingle(starts[frontPieceIndex] + 1d);
    var resolvedRearDistance = rearDistance ?? Convert.ToSingle(starts[rearPieceIndex] + 1d);
    var ride = Instance([2_000]);
    var train = Train(ride, [3_000]);
    var car = Car(
      train,
      resolvedFrontDistance,
      resolvedRearDistance,
      pieceIds[frontPieceIndex],
      pieceIds[rearPieceIndex],
      reversed);
    var track = Track(ride, pieceIds, isCircuit: true);
    var carRuntime = CarRuntime(ride, train, car, track, new(
      track,
      RideTrackGeometryStatus.Circuit,
      Graph: null,
      circuit));
    var raw = pieceIds.Select((id, index) => TrackPieceData(
      id,
      owner: 800,
      previous: pieceIds[(index + pieceIds.Length - 1) % pieceIds.Length],
      next: pieceIds[(index + 1) % pieceIds.Length],
      forwardStarts[index],
      backwardsStarts[index])).ToArray();
    return new(carRuntime, raw, pieceIds);
  }

  private static CircuitTestFixture NonCircuitFixture(
    bool resolvedOpenTrack,
    bool includeSavedReferences
  ) {
    var pieceIds = new ulong[] { 710, 711 };
    var ride = Instance([2_000]);
    var train = Train(ride, [3_000]);
    var car = Car(
      train,
      frontWheelDistance: 2f,
      rearWheelDistance: 1f,
      trackPiece: includeSavedReferences ? pieceIds[0] : 0,
      rearTrackPiece: includeSavedReferences ? pieceIds[0] : 0,
      reversed: false);
    var track = Track(ride, pieceIds, isCircuit: false);
    RideTrackGeometryLink geometry;
    if (resolvedOpenTrack) {
      var first = new TrackNode("first");
      var middle = new TrackNode("middle");
      var last = new TrackNode("last");
      geometry = new(
        track,
        RideTrackGeometryStatus.OpenTrack,
        new TrackGraph(
          [first, middle, last],
          [
            new TrackEdge("track-piece-710", first, middle, StraightPiece(0f, 10f)),
            new TrackEdge("track-piece-711", middle, last, StraightPiece(10f, 20f)),
          ]),
        Circuit: null);
    } else {
      geometry = new(
        track,
        RideTrackGeometryStatus.UnsupportedGeometry,
        Graph: null,
        Circuit: null);
    }
    var carRuntime = CarRuntime(ride, train, car, track, geometry);
    var raw = new[] {
      TrackPieceData(pieceIds[0], owner: 800, previous: 0, next: pieceIds[1], 0f, 0f),
      TrackPieceData(pieceIds[1], owner: 800, previous: pieceIds[0], next: 0, 10f, 0f),
    };
    return new(carRuntime, raw, pieceIds);
  }

  private static RideCarInstanceRuntimeRegistry CarRuntime(
    DatTrackedRideInstanceData ride,
    DatRideTrainInstanceData train,
    DatRideCarInstanceData car,
    RideTrack track,
    RideTrackGeometryLink geometry
  ) {
    var instanceTracks = RideInstanceTrackGraph.Build([ride], [track]);
    var resolution = new RideTrackGeometryResolution(
      [geometry],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount:
        geometry.Status == RideTrackGeometryStatus.UnsupportedGeometry ? 1 : 0,
      UnsupportedTopologyTrackCount: 0);
    var trackRuntime = RideInstanceTrackRuntimeRegistry.Build(instanceTracks, resolution);
    var trainResources = RideTrainInstanceResourceRegistry.Build([ride], [train], []);
    var trainRuntime = RideInstanceTrainRuntimeRegistry.Build(trackRuntime, trainResources);
    return RideCarInstanceRuntimeRegistry.Build(trainRuntime, [car]);
  }

  private static DatTrackedRideInstanceData Instance(ulong[] trains) => new(
    entryId: 900,
    name: "Synthetic",
    track: 700,
    trackedRideOverlayName: @"Cars\Synthetic",
    trackedRideSymbolName: "Synthetic:trr",
    nTrains: trains.Length,
    nCarsPerTrain: 1,
    trainSelection: 0,
    trains);

  private static DatRideTrainInstanceData Train(
    DatTrackedRideInstanceData ride,
    ulong[] cars
  ) => new(
    entryId: 2_000,
    rideTrainOverlayName: @"Cars\Synthetic\Train",
    rideTrainSymbolName: "Train:rit",
    trackedRideInstance: ride.EntryId,
    whichTrain: 0,
    length: 4f,
    mass: 1_000f,
    cars);

  private static DatRideCarInstanceData Car(
    DatRideTrainInstanceData train,
    float frontWheelDistance,
    float rearWheelDistance,
    ulong trackPiece,
    ulong rearTrackPiece,
    bool reversed
  ) => new(
    entryId: 3_000,
    rideTrainInstance: train.EntryId,
    whichCar: 0,
    whichRideTrainCar: 0,
    frontWheelDistance,
    rearWheelDistance,
    trackPiece,
    rearTrackPiece,
    distance: 0f,
    reversed,
    speed: 0f);

  private static RideTrack Track(
    DatTrackedRideInstanceData ride,
    ulong[] pieceIds,
    bool isCircuit
  ) => new(
    sourceEntryId: ride.Track,
    direction: 0,
    firstSegmentSourceEntryId: 800,
    lastSegmentSourceEntryId: 800,
    isCircuit,
    prototype: false,
    hasSerializedTrackPieceOrder: true,
    trackPieceSourceEntryIds: pieceIds,
    segmentSourceEntryIds: [800],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: ride.EntryId,
    flippedTrackSections: null,
    tunnelLightColour: null);

  private static DatTrackPieceData TrackPieceData(
    ulong id,
    ulong owner,
    ulong previous,
    ulong next,
    float forwardStart,
    float backwardsStart
  ) => new(
    entryId: id,
    flexiColourField: new DatSceneryFlexiColour(0, 0, 0),
    next,
    owner,
    platformPiece: 0,
    prev: previous,
    reversed: false,
    sidDatabaseEntry: 1,
    symbolName: "Synthetic:tks",
    sceneryItem: 1,
    sceneryItemDataField: new DatSceneryItemDataField(0, 0, 0, null, 1, 1),
    segment: 800,
    startDistance: forwardStart,
    startDistanceBackwardsSpline: backwardsStart,
    userAngleDegrees: 0);

  private static DatTrackPieceData Replace(
    DatTrackPieceData source,
    ulong? owner = null,
    ulong? next = null
  ) => TrackPieceData(
    source.EntryId,
    owner ?? source.Owner,
    source.Prev,
    next ?? source.Next,
    source.StartDistance,
    source.StartDistanceBackwardsSpline);

  private static TrackPiece[] CirclePieces() {
    const float radius = 10f;
    const float tangent = 16.568542f;
    return [
      CurvedPiece(
        new(radius, 0f, 0f),
        new(0f, radius, 0f),
        new(0f, tangent, 0f),
        new(-tangent, 0f, 0f)),
      CurvedPiece(
        new(0f, radius, 0f),
        new(-radius, 0f, 0f),
        new(-tangent, 0f, 0f),
        new(0f, -tangent, 0f)),
      CurvedPiece(
        new(-radius, 0f, 0f),
        new(0f, -radius, 0f),
        new(0f, -tangent, 0f),
        new(tangent, 0f, 0f)),
      CurvedPiece(
        new(0f, -radius, 0f),
        new(radius, 0f, 0f),
        new(tangent, 0f, 0f),
        new(0f, tangent, 0f)),
    ];
  }

  private static TrackPiece CurvedPiece(
    Vector3 start,
    Vector3 end,
    Vector3 startTangent,
    Vector3 endTangent
  ) {
    var halfGauge = Vector3.UnitZ * 0.5f;
    return new TrackPiece(TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        start - halfGauge,
        startTangent,
        start + halfGauge,
        startTangent,
        0f),
      new RailControlPair(
        1f,
        end - halfGauge,
        endTangent,
        end + halfGauge,
        endTangent,
        0f),
    ]));
  }

  private static TrackPiece StraightPiece(float startX, float endX) {
    var start = new Vector3(startX, 0f, 0f);
    var end = new Vector3(endX, 0f, 0f);
    var tangent = end - start;
    var halfGauge = Vector3.UnitY * 0.5f;
    return new TrackPiece(TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        start - halfGauge,
        tangent,
        start + halfGauge,
        tangent,
        0f),
      new RailControlPair(
        1f,
        end - halfGauge,
        tangent,
        end + halfGauge,
        tangent,
        0f),
    ]));
  }

  private static double[] PieceStarts(TrackCircuit circuit) {
    var starts = new double[circuit.Pieces.Count + 1];
    foreach (var index in Enumerable.Range(0, circuit.Pieces.Count))
      starts[index + 1] = starts[index] + circuit.Pieces[index].Piece.Length;
    return starts;
  }

  private static bool IsFinite(TrackCircuitSample sample) =>
    float.IsFinite(sample.CircuitArcLength) &&
    float.IsFinite(sample.PieceArcLength) &&
    IsFinite(sample.ContactPoints.Left) &&
    IsFinite(sample.ContactPoints.Right);

  private static bool IsFinite(RailSample sample) =>
    float.IsFinite(sample.ArcLength) &&
    IsFinite(sample.Position) &&
    IsFinite(sample.Tangent) &&
    IsFinite(sample.Orientation) &&
    float.IsFinite(sample.BankRadians);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Quaternion value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static string Format(RideCarSavedWheelContactCursor contact) =>
    $"{contact.SavedTrackPieceEntryId}@{contact.Cursor?.PieceArcLength:R}";

  private sealed record CircuitTestFixture(
    RideCarInstanceRuntimeRegistry CarRuntime,
    DatTrackPieceData[] TrackPieces,
    ulong[] PieceIds
  );
}
