// Ride Track Wild Installed Pipeline Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackWildInstalledPipelineTests {
  [Test]
  [Explicit("Requires installed RCT3 assets and the ScrubGardens DAT.")]
  public void ScrubGardens_SeizmicResolvesTwoExactCircuitsAndSavedCars() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installRoot),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(installRoot),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");
    var mapPath = Path.Combine(
      installRoot!,
      "Campaigns",
      "Base",
      "Wild",
      "ScrubGardens.dat");
    Assert.That(File.Exists(mapPath), Is.True, mapPath);

    var data = DatTerrainReader.Read(mapPath);
    var targetRide = data.TrackedRideInstances.Single(ride => ride.EntryId == 6_467);
    var trainsById = data.RideTrainInstances.ToDictionary(train => train.EntryId);
    var targetTrains = targetRide.Trains.Select(id => trainsById[id]).ToArray();
    var targetTrainIds = targetTrains.Select(train => train.EntryId).ToHashSet();
    var targetCars = data.RideCarInstances
      .Where(car => targetTrainIds.Contains(car.RideTrainInstance))
      .ToArray();
    var targetTrack = data.RideTracks.Single(track => track.EntryId == 6_469);
    var targetSegments = data.TrackSegments
      .Where(segment => segment.Track == targetTrack.EntryId)
      .ToArray();
    var targetSegmentIds = targetSegments.Select(segment => segment.EntryId).ToHashSet();
    var targetPieces = data.TrackPieces
      .Where(piece => targetSegmentIds.Contains(piece.Segment))
      .ToArray();
    var targetSceneryIds = targetPieces.Select(piece => piece.SceneryItem).ToHashSet();
    var expectedVariants = targetTrains.Select(train =>
      train.WhichRideCarSivVariant == 1
        ? RideCarVisualVariant.WildFlipped
        : RideCarVisualVariant.Normal).ToArray();
    var expectedBodyRoles = expectedVariants.Select(variant =>
      variant == RideCarVisualVariant.WildFlipped
        ? RideVisualRole.WildFlippedBody
        : RideVisualRole.Body).ToArray();
    var expectedCarIds = Enumerable.Range(0, 18)
      .Select(index => 7_937ul + Convert.ToUInt64(index * 2))
      .ToArray();
    var expectedPieceIds = new ulong[] {
      6_470, 6_710, 6_470, 6_710, 6_549, 6_551,
      6_491, 6_496, 6_491, 6_496, 6_494, 6_499,
      6_703, 6_706, 6_703, 6_706, 6_714, 6_717,
    };
    var expectedSegmentPieceOrders = new Dictionary<ulong, ulong[]> {
      [6_489] = [
        7_635, 7_651, 7_649, 7_647, 7_645, 7_643, 7_641, 7_639,
        6_528, 6_527, 6_525, 6_523, 6_524, 6_531, 6_533, 6_535,
        6_543, 6_545, 6_549, 6_564, 6_562, 6_563, 6_570, 6_576,
        6_578, 6_580, 6_582, 6_584, 6_602, 6_604, 6_606, 6_607,
        6_622, 6_619, 6_620, 6_470, 6_714, 6_703, 6_494, 6_491,
        6_492, 6_501, 6_505, 6_509, 6_513, 7_613, 7_615, 7_617,
        7_619, 7_621, 7_623, 7_625, 7_627, 7_629, 7_631, 7_633,
        7_636,
      ],
      [6_490] = [
        6_517, 7_689, 7_687, 7_685, 7_683, 7_681, 7_679, 7_677,
        7_675, 7_673, 7_671, 7_669, 7_667, 7_665, 7_663, 7_661,
        7_659, 7_657, 7_655, 6_521, 6_519, 6_520, 6_554, 6_556,
        6_558, 6_539, 6_537, 6_538, 6_541, 6_547, 6_551, 6_568,
        6_566, 6_567, 6_586, 6_588, 6_590, 6_592, 6_594, 6_596,
        6_598, 6_600, 6_609, 6_611, 6_617, 6_614, 6_615, 6_710,
        6_717, 6_706, 6_499, 6_496, 6_497, 6_503, 6_507, 6_511,
        6_515,
      ],
    };
    var targetPiecesById = targetPieces.ToDictionary(piece => piece.EntryId);
    var actualSegmentPieceOrders = targetSegments.ToDictionary(
      segment => segment.EntryId,
      segment => WalkClosedSegment(segment, targetPiecesById)
        .Select(piece => piece.EntryId)
        .ToArray());
    var expectedCarDistances = new[] {
      257.6889f, 325.68713f, 255.05885f, 323.05722f, 154.21814f, 222.19547f,
      273.46298f, 341.46838f, 270.83868f, 338.83643f, 268.20917f, 336.20703f,
      265.57846f, 333.57648f, 262.94925f, 330.9475f, 260.3188f, 328.31723f,
    };
    var expectedFrontDistances = new[] {
      256.88293f, 324.88156f, 254.2529f, 322.25165f, 153.41219f, 221.38991f,
      272.657f, 340.6628f, 270.0327f, 338.03085f, 267.4032f, 335.40146f,
      264.7725f, 332.7709f, 262.14328f, 330.14194f, 259.51282f, 327.51166f,
    };
    var expectedRearDistances = new[] {
      256.4439f, 324.44254f, 253.81389f, 321.81262f, 152.97318f, 220.9509f,
      272.218f, 340.2238f, 269.5937f, 337.59183f, 266.96417f, 334.96243f,
      264.33347f, 332.33188f, 261.70425f, 329.7029f, 259.0738f, 327.07263f,
    };
    var normalVariantCount = expectedVariants.Count(variant =>
      variant == RideCarVisualVariant.Normal);
    var wildVariantCount = expectedVariants.Count(variant =>
      variant == RideCarVisualVariant.WildFlipped);

    var terrain = Terrain.FromData(data);
    try {
      var park = new Park(terrain);
      SceneryManagerLoader.Load(
        park,
        terrain,
        data.SceneryItems.Where(item => targetSceneryIds.Contains(item.EntryId)).ToArray(),
        data.SceneryItemPlacements.Where(placement =>
          targetSceneryIds.Contains(placement.SceneryItem)).ToArray());
      RideTrackManagerLoader.Load(park, terrain, targetPieces);
      RideTrackTopologyLoader.Load(park, [targetTrack], targetSegments);

      using var loaded = RideTrackResourceCatalogLoader.Load(
        installRoot!,
        park.RideTrackPlacements,
        [targetRide],
        targetTrains);
      var resources = loaded.Catalog.ResolveAll(park.RideTrackPlacements);
      var geometry = RideTrackGeometryResolver.Resolve(
        terrain,
        park.RideTracks,
        resources);
      var targetGeometry = geometry.Tracks.Single();
      var instanceTracks = RideInstanceTrackGraph.Build([targetRide], park.RideTracks);
      var trackRuntime = RideInstanceTrackRuntimeRegistry.Build(instanceTracks, geometry);
      var trainRuntime = RideInstanceTrainRuntimeRegistry.Build(
        trackRuntime,
        loaded.RideResources.TrainInstances);
      var consistRuntime = RideInstanceTrainConsistRuntimeRegistry.Build(
        trainRuntime,
        loaded.RideResources,
        targetCars);
      var carRuntime = RideCarInstanceRuntimeRegistry.Build(
        trainRuntime,
        targetCars,
        consistRuntime);
      var visualVariants = RideCarVisualVariantSelector.Build(
        carRuntime,
        loaded.RideResources.CarVisuals);
      var wheelCursors = RideCarSavedWheelCursorRegistry.Build(carRuntime, targetPieces);
      using var variantTemplates = RideCarVariantVisualTemplateRegistry.Build(
        visualVariants);
      var variantCars = RideCarVariantStaticInstanceRegistry.Build(
        carRuntime,
        wheelCursors,
        visualVariants,
        variantTemplates);
      var visualHierarchy = RideCarVisualHierarchyResolver.Resolve(
        loaded.RideResources.Graph,
        loaded.RideResources.CarVisuals);
      using var visualTemplates = RideCarVisualTemplateRegistry.Build(
        loaded.RideResources.CarVisuals);
      var hierarchyParts = RideCarVisualHierarchyStaticInstanceRegistry.Build(
        variantCars,
        visualHierarchy,
        visualTemplates);

      foreach (var entry in wheelCursors.Entries) {
        var car = entry.CarRuntime.CarInstance;
        TestContext.Progress.WriteLine(
          $"Seizmic cursor: car={car.EntryId} train={car.RideTrainInstance} " +
          $"trackStatus={entry.CarRuntime.TrackStatus} distance={car.Distance:R} " +
          $"reversed={car.Reversed} front={FormatContact(entry.Front)} " +
          $"rear={FormatContact(entry.Rear)}");
      }
      TestContext.Progress.WriteLine(
        $"ScrubGardens Seizmic: ride={targetRide.EntryId} track={targetTrack.EntryId} " +
        $"trains={targetTrains.Length} cars={targetCars.Length} " +
        $"segments={targetSegments.Length} geometry={targetGeometry.Status} " +
        $"detail={targetGeometry.Detail} " +
        $"normal={normalVariantCount} wild={wildVariantCount} " +
        $"selected={visualVariants.SelectedCount} " +
        $"variantCars={variantCars.ResolvedCount} " +
        $"hierarchyParts={hierarchyParts.PartCount} " +
        $"hierarchyIssues={hierarchyParts.Issues.Count}");
      foreach (var issue in hierarchyParts.Issues)
        TestContext.Progress.WriteLine(
          $"Hierarchy issue: car={issue.SavedCar.CarInstanceEntryId} " +
          $"role={issue.Role} status={issue.Status} detail={issue.Detail}");

      using (Assert.EnterMultipleScope()) {
        Assert.That(targetTrains, Has.Length.EqualTo(18));
        Assert.That(targetCars, Has.Length.EqualTo(18));
        Assert.That(targetSegments, Has.Length.EqualTo(2));
        Assert.That(targetTrack.Direction, Is.Zero);
        Assert.That(targetTrack.FirstSegment, Is.EqualTo(6_489));
        Assert.That(targetTrack.LastSegment, Is.EqualTo(6_490));
        Assert.That(targetTrack.Prototype, Is.True);
        Assert.That(targetTrack.TrackPieces, Is.Null);
        Assert.That(targetSegments.OrderBy(segment => segment.EntryId).Select(segment => (
          segment.EntryId,
          segment.FirstPiece,
          segment.LastPiece,
          segment.PrevSegment,
          segment.NextSegment,
          segment.Direction)), Is.EqualTo(new[] {
            (6_489ul, 7_635ul, 7_636ul, 6_106ul, 6_490ul, 0),
            (6_490ul, 6_517ul, 6_515ul, 6_489ul, 6_553ul, 0),
          }));
        Assert.That(targetPieces, Has.Length.EqualTo(114));
        Assert.That(actualSegmentPieceOrders, Is.EqualTo(expectedSegmentPieceOrders));
        Assert.That(targetGeometry.Track.HasAuthoritativeTrackPieceOrder, Is.True);
        Assert.That(targetGeometry.Track.IsCircuit, Is.Null);
        Assert.That(targetTrains.All(train => train.Cars.Count == 1), Is.True);
        Assert.That(targetTrains.Select(train => train.WhichRideCarSivVariant),
          Is.EqualTo(Enumerable.Range(0, 18).Select(index => index % 2 == 0 ? 1 : 0)));
        Assert.That(targetTrains.Count(train => train.WhichRideCarSivVariant == 1),
          Is.EqualTo(9));
        Assert.That(targetTrains.Count(train => train.WhichRideCarSivVariant == 0),
          Is.EqualTo(9));
        Assert.That(targetTrains.Select(train => train.RideTrainOverlayName),
          Is.All.EqualTo(@"Cars\CoasterCars\SiezmicCoaster\SiezmicCoaster"));
        Assert.That(targetTrains.Select(train => train.RideTrainSymbolName),
          Is.All.EqualTo("SiezmicCoaster:rit"));
        Assert.That(targetCars.Select(car => car.WhichCar), Is.All.Zero);
        Assert.That(targetCars.Select(car => car.WhichRideTrainCar), Is.All.Zero);
        Assert.That(targetCars.Select(car => car.EntryId), Is.EqualTo(expectedCarIds));
        Assert.That(targetCars.Select(car => car.RideTrainInstance),
          Is.EqualTo(Enumerable.Range(6_471, 18).Select(Convert.ToUInt64)));
        Assert.That(targetCars.Select(car => car.TrackPiece), Is.EqualTo(expectedPieceIds));
        Assert.That(targetCars.Select(car => car.RearTrackPiece),
          Is.EqualTo(expectedPieceIds));
        Assert.That(targetCars.Select(car => car.Distance),
          Is.EqualTo(expectedCarDistances));
        Assert.That(targetCars.Select(car => car.FrontWheelDistance),
          Is.EqualTo(expectedFrontDistances));
        Assert.That(targetCars.Select(car => car.RearWheelDistance),
          Is.EqualTo(expectedRearDistances));
        Assert.That(targetCars.Select(car => car.Reversed), Is.All.False);
        Assert.That(targetGeometry.Status,
          Is.EqualTo(RideTrackGeometryStatus.MultiCircuit));
        Assert.That(targetGeometry.Graph, Is.Null);
        Assert.That(targetGeometry.Circuit, Is.Null);
        Assert.That(targetGeometry.SegmentCircuits.Select(circuit =>
          circuit.SegmentSourceEntryId), Is.EqualTo(new ulong[] { 6_489, 6_490 }));
        Assert.That(targetGeometry.SegmentCircuits.ToDictionary(
          circuit => circuit.SegmentSourceEntryId,
          circuit => circuit.PieceSourceEntryIds.ToArray()),
          Is.EqualTo(expectedSegmentPieceOrders));
        Assert.That(targetGeometry.SegmentCircuits.Select(circuit =>
          circuit.Circuit.Pieces.Count), Is.EqualTo(new[] { 57, 57 }));
        Assert.That(targetGeometry.Detail, Is.Null);
        Assert.That(trackRuntime.ResolvedTrackCount, Is.EqualTo(1));
        Assert.That(trackRuntime.MultiCircuitTrackCount, Is.EqualTo(1));
        Assert.That(trackRuntime.UnsupportedTopologyTrackCount, Is.Zero);
        Assert.That(trackRuntime.Entries.Single().CircuitTraversal, Is.Null);
        Assert.That(trackRuntime.Entries.Single().SegmentCircuitTraversals,
          Has.Count.EqualTo(2));
        Assert.That(trainRuntime.Entries, Has.Count.EqualTo(18));
        Assert.That(trainRuntime.Entries.Select(train =>
          RideTrainCircuitMotionAuthorization.Authorize(
            train,
            train.TrackRuntime).IsAuthorized), Is.All.False);
        Assert.That(trainRuntime.Entries.Select(train =>
          RideTrainCircuitMotionAuthorization.Authorize(
            train,
            train.TrackRuntime).Status),
          Is.All.EqualTo(
            RideTrainCircuitMotionAuthorizationStatus.UnprovenEndpointFallback));
        Assert.That(consistRuntime.Entries, Has.Count.EqualTo(18));
        Assert.That(consistRuntime.ResolvedCount, Is.EqualTo(18));
        Assert.That(consistRuntime.Entries.All(consist =>
          consist.Cars.Count == 1 &&
          consist.Cars.Single().Role.Role == RideTrainCarRole.Front &&
          consist.Cars.Single().PeepSlotEvidence.PeepSlotCount == 0), Is.True);
        Assert.That(carRuntime.Entries, Has.Count.EqualTo(18));
        Assert.That(carRuntime.Entries.Select(car => car.SavedRole),
          Is.All.EqualTo(RideTrainCarRole.Front));
        Assert.That(carRuntime.Entries.Select(car => car.TrackStatus),
          Is.All.EqualTo(RideTrackGeometryStatus.MultiCircuit));
        Assert.That(carRuntime.Entries.Select(car => car.TrackPiece.Status),
          Is.All.EqualTo(RideCarTrackPieceRuntimeStatus.Resolved));
        Assert.That(carRuntime.Entries.Select(car => car.RearTrackPiece.Status),
          Is.All.EqualTo(RideCarTrackPieceRuntimeStatus.Resolved));
        Assert.That(carRuntime.Entries.Select(car => car.TrackPiece.CircuitIndex),
          Is.EqualTo(Enumerable.Range(0, 18).Select(index => (int?)(index % 2))));
        Assert.That(carRuntime.Entries.Select(car => car.RearTrackPiece.CircuitIndex),
          Is.EqualTo(Enumerable.Range(0, 18).Select(index => (int?)(index % 2))));
        Assert.That(wheelCursors.CarCount, Is.EqualTo(18));
        Assert.That(wheelCursors.ResolvedCarCount, Is.EqualTo(18));
        Assert.That(wheelCursors.ResolvedContactCount, Is.EqualTo(36));
        Assert.That(wheelCursors.Entries.SelectMany(entry =>
          new[] { entry.Front.Status, entry.Rear.Status }),
          Is.All.EqualTo(RideCarSavedWheelCursorStatus.Resolved));
        Assert.That(wheelCursors.Entries.SelectMany(entry =>
          new[] { entry.Front, entry.Rear }).All(contact =>
            contact.NormalizedCircuitDistance != null &&
            contact.TrackPieceData != null &&
            contact.SplineStart != null &&
            contact.Cursor != null &&
            contact.Sample != null), Is.True);
        Assert.That(visualVariants.Entries, Has.Count.EqualTo(18));
        Assert.That(visualVariants.SelectedCount, Is.EqualTo(18));
        Assert.That(visualVariants.FailedCount, Is.Zero);
        Assert.That(visualVariants.Entries.Select(entry => entry.SelectedVariant),
          Is.EqualTo(expectedVariants));
        Assert.That(visualVariants.Entries.Select(entry => entry.RequiredBodyRole),
          Is.EqualTo(expectedBodyRoles));
        Assert.That(visualVariants.Entries.Select(entry => entry.Body!.Visual.Role),
          Is.EqualTo(expectedBodyRoles));
        Assert.That(variantTemplates.ResolvedCount, Is.EqualTo(18));
        Assert.That(variantTemplates.FailedCount, Is.Zero);
        Assert.That(variantCars.CarCount, Is.EqualTo(18));
        Assert.That(variantCars.ResolvedCount, Is.EqualTo(18));
        Assert.That(variantCars.UnresolvedCount, Is.Zero);
        Assert.That(variantCars.UnresolvedSavedCursorCount, Is.Zero);
        Assert.That(variantCars.Entries.Select(entry => entry.Issues),
          Is.All.EqualTo(RideCarVariantStaticInstanceIssue.None));
        Assert.That(variantCars.Entries.Select(entry => entry.SelectedVariant),
          Is.EqualTo(expectedVariants));
        Assert.That(variantCars.Entries.Select(entry => entry.RequiredBodyRole),
          Is.EqualTo(expectedBodyRoles));
        Assert.That(visualHierarchy.AmbiguousPartCount, Is.Zero);
        Assert.That(hierarchyParts.SourceCarCount, Is.EqualTo(18));
        Assert.That(hierarchyParts.EligibleCarCount, Is.EqualTo(18));
        Assert.That(hierarchyParts.PlannedCarCount, Is.EqualTo(18));
        Assert.That(hierarchyParts.PartCount, Is.Zero);
        Assert.That(hierarchyParts.Issues, Is.Empty);
      }
    } finally {
      terrain.TextureCatalog?.Dispose();
    }
  }

  private static DatTrackPieceData[] WalkClosedSegment(
    DatTrackSegmentData segment,
    IReadOnlyDictionary<ulong, DatTrackPieceData> piecesById
  ) {
    var ownedPieceCount = piecesById.Values.Count(piece => piece.Segment == segment.EntryId);
    var ordered = new DatTrackPieceData[ownedPieceCount];
    var currentId = segment.FirstPiece;
    foreach (var index in Enumerable.Range(0, ownedPieceCount)) {
      Assert.That(
        piecesById.TryGetValue(currentId, out var current),
        Is.True,
        $"TrackSegment {segment.EntryId} references missing TrackPiece {currentId}.");
      Assert.That(current!.Segment, Is.EqualTo(segment.EntryId));
      Assert.That(current.Owner, Is.EqualTo(segment.EntryId));
      ordered[index] = current;
      currentId = current.Next;
    }

    Assert.That(currentId, Is.EqualTo(segment.FirstPiece));
    foreach (var index in Enumerable.Range(0, ordered.Length)) {
      var previous = ordered[(index + ordered.Length - 1) % ordered.Length];
      var current = ordered[index];
      var next = ordered[(index + 1) % ordered.Length];
      Assert.That(current.Prev, Is.EqualTo(previous.EntryId));
      Assert.That(current.Next, Is.EqualTo(next.EntryId));
    }
    return ordered;
  }

  private static string FormatContact(RideCarSavedWheelContactCursor contact) =>
    $"{contact.Status}:piece={contact.SavedTrackPieceEntryId}:" +
    $"distance={contact.SavedGlobalDistance:R}:normalized=" +
    $"{contact.NormalizedCircuitDistance:R}:start={contact.TrackPieceData?.StartDistance:R}:" +
    $"backStart={contact.TrackPieceData?.StartDistanceBackwardsSpline:R}";
}
