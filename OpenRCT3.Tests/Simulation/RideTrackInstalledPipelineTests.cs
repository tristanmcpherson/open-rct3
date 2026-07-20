// Ride Track Installed Pipeline Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackInstalledPipelineTests {
  [Test]
  [Explicit("Requires installed RCT3 assets and the BoxOffice DAT.")]
  public void BoxOffice_DatThroughCatalogProducesTypedGeometryOutcomes() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installRoot),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(installRoot),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");
    var mapPath = ResolveMapPath(installRoot!);
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
      var resolvedPlacements = resources.Placements.Count(link => link.IsResolved);
      var issueCounts = loaded.Issues
        .GroupBy(issue => issue.Kind)
        .ToDictionary(group => group.Key, group => group.Count());
      var reversedPlacements = park.RideTrackPlacements.Count(placement => placement.Reversed);
      var userAngledPlacements = park.RideTrackPlacements.Count(placement =>
        placement.UserAngleDegrees != 0);
      var savedTrainLink = loaded.RideResources.TrainInstances.Links.Single();
      var savedCarsById = data.RideCarInstances.ToDictionary(car => car.EntryId);
      var savedCars = savedTrainLink.TrainInstance.Cars
        .Select(id => savedCarsById[id])
        .ToArray();
      var expectedSavedTrainPath = Path.GetFullPath(Path.Combine(
        installRoot!,
        "Cars",
        "TrackedRideCars",
        "StreamlinedMono",
        "StreamlinedMono.unique.ovl"));

      TestContext.Progress.WriteLine(
        $"BoxOffice DAT: pieces={data.TrackPieces.Count}, tracks={data.RideTracks.Count}, " +
        $"segments={data.TrackSegments.Count}, semanticPlacements=" +
        $"{park.RideTrackPlacements.Count}, semanticTracks={park.RideTracks.Count}");
      TestContext.Progress.WriteLine(
        $"Catalog: pairs={loaded.Context.LoadedCommonPaths.Count}, " +
        $"issues={loaded.Issues.Count}, resolvedPlacements={resolvedPlacements}, " +
        $"unresolvedPlacements={resources.UnresolvedPlacementCount}, " +
        $"issueKinds={FormatCounts(issueCounts)}");
      TestContext.Progress.WriteLine(
        $"Placement flags: reversed={reversedPlacements}, userAngled={userAngledPlacements}");
      TestContext.Progress.WriteLine(
        $"Ride resources: instances={loaded.RideResources.Instances.Count}, " +
        $"resolvedInstances={loaded.RideResources.ResolvedInstanceCount}, " +
        $"rides={loaded.RideResources.DecodedCounts.TrackedRides}, " +
        $"trains={loaded.RideResources.DecodedCounts.RideTrains}, " +
        $"cars={loaded.RideResources.DecodedCounts.RideCars}, " +
        $"visuals={loaded.RideResources.DecodedCounts.SceneryItemVisuals}, " +
        $"savedTrains={loaded.RideResources.SavedTrainCount}, " +
        $"resolvedSavedTrains={loaded.RideResources.ResolvedSavedTrainCount}, " +
        $"savedCars={data.RideCarInstances.Count}, " +
        $"carVisuals={loaded.RideResources.CarVisuals.Visuals.Count}, " +
        $"shapeLods={loaded.RideResources.CarVisuals.ResolvedShapeLodCount}, " +
        $"graphRides={loaded.RideResources.Graph.Rides.Count}, " +
        $"unresolvedEdges={loaded.RideResources.Graph.UnresolvedReferenceCount}");

      using (Assert.EnterMultipleScope()) {
        Assert.That(data.TrackPieces, Has.Count.EqualTo(216));
        Assert.That(data.RideTracks, Has.Count.EqualTo(1));
        Assert.That(data.TrackSegments, Has.Count.EqualTo(1));
        Assert.That(park.RideTrackPlacements, Has.Count.EqualTo(data.TrackPieces.Count));
        Assert.That(park.RideTracks, Has.Count.EqualTo(data.RideTracks.Count));
        Assert.That(park.RideTrackSegments, Has.Count.EqualTo(data.TrackSegments.Count));
        Assert.That(loaded.Context.LoadedCommonPaths, Has.Count.EqualTo(9));
        Assert.That(loaded.Issues, Is.Empty);
        Assert.That(loaded.RideResources.Instances,
          Has.Count.EqualTo(data.TrackedRideInstances.Count));
        Assert.That(loaded.RideResources.ResolvedInstanceCount,
          Is.EqualTo(data.TrackedRideInstances.Count));
        Assert.That(loaded.RideResources.UnresolvedInstanceCount, Is.Zero);
        Assert.That(loaded.RideResources.Graph.Rides, Has.Count.EqualTo(1));
        Assert.That(loaded.RideResources.DecodedCounts,
          Is.EqualTo(new RideResourceDecodeCounts(1, 4, 13, 13)));
        Assert.That(
          loaded.RideResources.Graph.UnresolvedReferenceCount,
          Is.Zero);
        Assert.That(data.RideTrainInstances, Has.Count.EqualTo(1));
        Assert.That(loaded.RideResources.SavedTrainCount, Is.EqualTo(1));
        Assert.That(loaded.RideResources.ResolvedSavedTrainCount, Is.EqualTo(1));
        Assert.That(loaded.RideResources.UnresolvedSavedTrainCount, Is.Zero);
        Assert.That(data.RideCarInstances, Has.Count.EqualTo(7));
        Assert.That(savedTrainLink.TrainInstance.Cars,
          Is.EqualTo(new ulong[] {
            20_402, 20_403, 20_404, 20_405, 20_406, 20_407, 20_408,
          }));
        Assert.That(savedCars.Select(car => car.WhichCar),
          Is.EqualTo(Enumerable.Range(0, 7)));
        Assert.That(savedCars.Select(car => car.WhichRideTrainCar),
          Is.EqualTo(new[] { 0, 5, 2, 5, 2, 5, 4 }));
        Assert.That(loaded.RideResources.CarVisuals.Visuals, Has.Count.EqualTo(14));
        Assert.That(loaded.RideResources.CarVisuals.ResolvedShapeLodCount, Is.EqualTo(39));
        Assert.That(
          loaded.RideResources.CarVisuals.UnresolvedShapeReferenceCount,
          Is.Zero);
        Assert.That(savedTrainLink.RideInstanceEntryId, Is.EqualTo(3_986));
        Assert.That(
          savedTrainLink.RideInstance.CarFlexiColours,
          Is.EqualTo(new DatSceneryFlexiColour(6, 18, 25)));
        Assert.That(savedTrainLink.TrainInstanceEntryId, Is.EqualTo(3_987));
        Assert.That(savedTrainLink.Ordinal, Is.Zero);
        Assert.That(savedTrainLink.WhichTrain, Is.Zero);
        Assert.That(savedTrainLink.Length, Is.EqualTo(28.754667f));
        Assert.That(savedTrainLink.Mass, Is.EqualTo(10_200f));
        Assert.That(savedTrainLink.HasSavedMotionState, Is.True);
        Assert.That(savedTrainLink.SavedDistance, Is.EqualTo(88.933914f));
        Assert.That(savedTrainLink.SavedReversed, Is.False);
        Assert.That(savedTrainLink.SavedSpeed, Is.Zero);
        Assert.That(savedTrainLink.SavedVisualVariant, Is.Null);
        Assert.That(savedTrainLink.TrainInstance.RideTrainOverlayName,
          Is.EqualTo(@"Cars\TrackedRideCars\StreamlinedMono\StreamlinedMono"));
        Assert.That(savedTrainLink.TrainInstance.RideTrainSymbolName,
          Is.EqualTo("StreamlinedMono:rit"));
        Assert.That(savedTrainLink.Source!.File.Path, Is.EqualTo(expectedSavedTrainPath));
        Assert.That(savedTrainLink.Resource!.Name, Is.EqualTo("StreamlinedMono"));
        Assert.That(
          loaded.RideResources.Graph.Rides.Single().Trains.All(train => train.IsResolved),
          Is.True);
        Assert.That(resources.Placements, Has.Count.EqualTo(data.TrackPieces.Count));
        Assert.That(resolvedPlacements, Is.EqualTo(216));
        Assert.That(resources.UnresolvedPlacementCount, Is.Zero);
        Assert.That(reversedPlacements, Is.EqualTo(58));
        Assert.That(userAngledPlacements, Is.EqualTo(13));
        Assert.That(
          resolvedPlacements + resources.UnresolvedPlacementCount,
          Is.EqualTo(data.TrackPieces.Count));
        Assert.That(loaded.IsComplete, Is.EqualTo(loaded.Issues.Count == 0));
        Assert.That(loaded.Issues.All(issue => Enum.IsDefined(issue.Kind)), Is.True);
      }

      var geometry = RideTrackGeometryResolver.Resolve(
        terrain,
        park.RideTracks,
        resources);
      var statusCounts = geometry.Tracks
        .GroupBy(link => link.Status)
        .ToDictionary(group => group.Key, group => group.Count());
      var openTracks = Count(statusCounts, RideTrackGeometryStatus.OpenTrack);
      var circuits = Count(statusCounts, RideTrackGeometryStatus.Circuit);
      var unresolvedTracks = Count(
        statusCounts,
        RideTrackGeometryStatus.UnresolvedResources);
      var unsupportedGeometryTracks = Count(
        statusCounts,
        RideTrackGeometryStatus.UnsupportedGeometry);
      var unsupportedTracks = Count(
        statusCounts,
        RideTrackGeometryStatus.UnsupportedTopology);

      TestContext.Progress.WriteLine(
        $"Geometry: total={geometry.Tracks.Count}, open={openTracks}, circuits={circuits}, " +
        $"unresolved={unresolvedTracks}, unsupportedGeometry={unsupportedGeometryTracks}, " +
        $"unsupportedTopology={unsupportedTracks}");
      foreach (var outcome in geometry.Tracks.Where(link => !link.IsResolved))
        TestContext.Progress.WriteLine(
          $"Geometry track {outcome.Track.SourceEntryId}: {outcome.Status}: " +
          $"{outcome.Detail}");
      var circuit = geometry.Tracks.Single().Circuit;
      Assert.That(circuit, Is.Not.Null);
      var joinMetrics = MeasureRailJoins(circuit!);
      var maxPositionDifference = joinMetrics.Max(metric => metric.PositionDifference);
      var maxDirectionDifference = joinMetrics.Max(metric => metric.DirectionDifference);
      var maxBankDifference = joinMetrics.Max(metric => metric.BankDifferenceRadians);
      var magnitudeMismatchCount = joinMetrics.Count(metric =>
        metric.MagnitudeDifference > 0.001f);
      var instanceTracks = RideInstanceTrackGraph.Build(
        data.TrackedRideInstances,
        park.RideTracks);
      var trackRuntime = RideInstanceTrackRuntimeRegistry.Build(instanceTracks, geometry);
      var trainRuntime = RideInstanceTrainRuntimeRegistry.Build(
        trackRuntime,
        loaded.RideResources.TrainInstances);
      var runtimeTrain = trainRuntime.Entries.Single();
      var motionAdvanceAuthorization =
        RideTrainMotionAdvanceAuthorization.Authorize(runtimeTrain);
      var consistRuntime = RideInstanceTrainConsistRuntimeRegistry.Build(
        trainRuntime,
        loaded.RideResources,
        data.RideCarInstances);
      var runtimeConsist = consistRuntime.Entries.Single();
      var visualHierarchy = RideCarVisualHierarchyResolver.Resolve(
        loaded.RideResources.Graph,
        loaded.RideResources.CarVisuals);
      var carRuntime = RideCarInstanceRuntimeRegistry.Build(
        trainRuntime,
        data.RideCarInstances,
        consistRuntime);
      var nativeTrainLengthWithoutGeometry =
        RideTrainNativeLengthResolver.Resolve(carRuntime.Entries);
      var visualVariants = RideCarVisualVariantSelector.Build(
        carRuntime,
        loaded.RideResources.CarVisuals);
      var wheelCursors = RideCarSavedWheelCursorRegistry.Build(
        carRuntime,
        data.TrackPieces);
      using var variantTemplates = RideCarVariantVisualTemplateRegistry.Build(
        visualVariants);
      var variantCars = RideCarVariantStaticInstanceRegistry.Build(
        carRuntime,
        wheelCursors,
        visualVariants,
        variantTemplates);
      using var visualTemplates = RideCarVisualTemplateRegistry.Build(
        loaded.RideResources.CarVisuals);
      var distinctBodyShapes = visualTemplates.BodyTemplates
        .DistinctBy(template => template.Shape, ReferenceEqualityComparer.Instance)
        .ToArray();
      var bodyBatches = distinctBodyShapes
        .SelectMany(template => template.Batches)
        .ToArray();
      var hierarchyParts = RideCarVisualHierarchyStaticInstanceRegistry.Build(
        variantCars,
        visualHierarchy,
        visualTemplates);
      var staticCars = RideCarStaticInstanceRegistry.Build(
        carRuntime,
        wheelCursors,
        visualTemplates);
      var nativeTrainLength = RideTrainNativeLengthResolver.Resolve(
        variantCars.Entries.Select(entry => new RideTrainNativeLengthInput(
          entry.CarRuntime,
          entry.Geometry!,
          HasRearGeometry: true)).ToArray());
      var ordinaryCarDistances = RideTrainOrdinaryCarDistanceResolver.Resolve(
        runtimeTrain.SavedMotionState!.Value,
        nativeTrainLength.Length!.Value,
        variantCars.Entries.Select(entry => new RideTrainOrdinaryCarDistanceInput(
          entry.Geometry!.CarLength,
          entry.Geometry!,
          HasRearGeometry: true)).ToArray(),
        circuit.Length);
      var savedCarDistanceDeltas = Enumerable.Range(0, carRuntime.Entries.Count)
        .Select(index => Math.Abs(
          ordinaryCarDistances.BaseDistances[index] -
          carRuntime.Entries[index].SavedDistance))
        .ToArray();
      var maxSavedCarDistanceDelta = savedCarDistanceDeltas.Max();
      var ordinaryPosePlan = RideTrainOrdinaryScenePosePlanner.Resolve(
        runtimeTrain.TrackRuntime.CircuitTraversal!,
        runtimeTrain.SavedMotionState.Value,
        staticCars.Entries.Select(entry => new RideTrainOrdinaryScenePoseCarInput(
          entry.CarRuntime,
          entry,
          entry.Geometry!,
          HasRearGeometry: true)).ToArray());
      var maxSavedPoseTransformDelta = Enumerable.Range(0, staticCars.Entries.Count)
        .Max(index => MaximumMatrixDifference(
          ordinaryPosePlan.CarPoses[index].Transform,
          staticCars.Entries[index].Pose!.Transform));
      RideCarStaticSceneBuildResult? carSceneForCleanup = null;
      RideCarVisualHierarchySceneBuildResult? hierarchySceneForCleanup = null;
      try {
        RideCarStaticSceneBuildResult carScene;
        RideCarVisualHierarchySceneBuildResult hierarchyScene;
        using (var visualMaterials = new RideCarVisualMaterialResolver(loaded.Context)) {
          carScene = RideCarStaticSceneBuilder.Build(
            variantCars,
            (entry, batch) => {
              var colours = RideCarVisualMaterialResolver.ResolveSavedCarColours(
                entry.CarRuntime);
              return visualMaterials.Resolve(
                batch,
                entry.BodyTemplate!.Link.Car.Source!.AllowedArchivePaths,
                colours).Material;
            });
          carSceneForCleanup = carScene;
          hierarchyScene = RideCarVisualHierarchySceneBuilder.Build(
            hierarchyParts,
            visualMaterials.ResolveMaterial);
          hierarchySceneForCleanup = hierarchyScene;
        }

        TestContext.Progress.WriteLine(
          $"Geometry joins: rails={joinMetrics.Count}, " +
          $"maxPosition={maxPositionDifference:R}, " +
          $"maxDirection={maxDirectionDifference:R}, " +
          $"maxBankRadians={maxBankDifference:R}, " +
          $"magnitudeMismatches={magnitudeMismatchCount}");
        TestContext.Progress.WriteLine(
          $"Runtime identities: rides={trackRuntime.InstanceCount}, " +
          $"resolvedTracks={trackRuntime.ResolvedTrackCount}, " +
          $"linkedTrains={trainRuntime.LinkedTrainCount}, " +
           $"resolvedTrainResources={trainRuntime.ResolvedResourceTrainCount}, " +
           $"trainState={runtimeTrain.SavedOperationalState}, " +
           $"motionAdvance={motionAdvanceAuthorization.Status}, " +
          $"resolvedConsists={consistRuntime.ResolvedCount}, " +
          $"linkedCars={carRuntime.LinkedCarCount}, " +
          $"resolvedCarPieces={carRuntime.ResolvedTrackPieceCarCount}, " +
          $"resolvedCarResources={carRuntime.ResolvedResourceCarCount}, " +
          $"nativeTrainLengthWithoutGeometry={nativeTrainLengthWithoutGeometry.Status}, " +
          $"nativeTrainLength={nativeTrainLength.Length:R}, " +
          $"maxSavedCarDistanceDelta={maxSavedCarDistanceDelta:R}, " +
          $"maxSavedPoseTransformDelta={maxSavedPoseTransformDelta:R}, " +
          $"hierarchyCars={visualHierarchy.Cars.Count}, " +
          $"resolvedHierarchyParts={visualHierarchy.ResolvedPartCount}, " +
          $"ambiguousHierarchyParts={visualHierarchy.AmbiguousPartCount}, " +
          $"selectedVariants={visualVariants.SelectedCount}, " +
          $"variantFallbacks={visualVariants.BodyControlFallbackCount}, " +
          $"variantTemplates={variantTemplates.ResolvedCount}, " +
          $"variantCars={variantCars.ResolvedCount}, " +
          $"resolvedWheelCars={wheelCursors.ResolvedCarCount}, " +
           $"resolvedWheelContacts={wheelCursors.ResolvedContactCount}, " +
           $"visualTemplates={visualTemplates.Templates.Count}, " +
           $"hierarchyPartInstances={hierarchyParts.PartCount}, " +
           $"hierarchyPartIssues={hierarchyParts.Issues.Count}, " +
           $"hierarchyPartModels={hierarchyScene.ModelCount}, " +
           $"staticCars={staticCars.ResolvedCount}, " +
          $"carModels={carScene.ModelCount}, " +
          $"missingCarMaterials={carScene.MissingMaterialBatchCount}");
        foreach (var car in staticCars.Entries.Where(entry => entry.Geometry != null)) {
          var source = car.CarRuntime;
          var carGeometry = car.Geometry!;
          TestContext.Progress.WriteLine(
            $"Car geometry: id={source.CarInstanceEntryId}, role={source.SavedRole}, " +
            $"savedLength={source.SavedLength:R}, bodyLength={carGeometry.CarLength:R}, " +
            $"front=({carGeometry.CarFrontPosition.X:R}," +
            $"{carGeometry.CarFrontPosition.Y:R},{carGeometry.CarFrontPosition.Z:R}), " +
            $"rear=({carGeometry.CarRearPosition.X:R}," +
            $"{carGeometry.CarRearPosition.Y:R},{carGeometry.CarRearPosition.Z:R}), " +
            $"frontFromFront={carGeometry.FrontWheelCenterOffsetFromCarFront:R}, " +
            $"frontFromRear={carGeometry.FrontWheelCenterOffsetFromCarRear:R}, " +
            $"rearFromFront={carGeometry.RearWheelCenterOffsetFromFrontWheelCenter:R}");
        }

        using (Assert.EnterMultipleScope()) {
          Assert.That(geometry.Tracks, Has.Count.EqualTo(data.RideTracks.Count));
          Assert.That(
            openTracks + circuits + unresolvedTracks + unsupportedGeometryTracks +
              unsupportedTracks,
            Is.EqualTo(data.RideTracks.Count));
          Assert.That(openTracks, Is.Zero);
          Assert.That(circuits, Is.EqualTo(1));
          Assert.That(unresolvedTracks, Is.Zero);
          Assert.That(unsupportedGeometryTracks, Is.Zero);
          Assert.That(unsupportedTracks, Is.Zero);
          Assert.That(circuit!.Pieces, Has.Count.EqualTo(216));
          Assert.That(joinMetrics, Has.Count.EqualTo(432));
          Assert.That(maxPositionDifference, Is.LessThanOrEqualTo(0.001f));
          Assert.That(
            maxDirectionDifference,
            Is.LessThanOrEqualTo(RideTrackGraphAdapter.ImportedJoinDirectionTolerance));
          Assert.That(maxBankDifference, Is.LessThanOrEqualTo(0.001f));
          Assert.That(magnitudeMismatchCount, Is.EqualTo(410));
          Assert.That(trackRuntime.InstanceCount, Is.EqualTo(1));
          Assert.That(trackRuntime.CircuitTrackCount, Is.EqualTo(1));
          Assert.That(trackRuntime.ResolvedTrackCount, Is.EqualTo(1));
          Assert.That(trainRuntime.RideInstanceCount, Is.EqualTo(1));
          Assert.That(trainRuntime.LinkedTrainCount, Is.EqualTo(1));
          Assert.That(trainRuntime.SavedTrainCount, Is.EqualTo(1));
          Assert.That(trainRuntime.UnreferencedTrainCount, Is.Zero);
          Assert.That(trainRuntime.ResolvedTrackTrainCount, Is.EqualTo(1));
          Assert.That(trainRuntime.ResolvedResourceTrainCount, Is.EqualTo(1));
          Assert.That(trainRuntime.ResolvedCircuitTrainCount, Is.EqualTo(1));
          Assert.That(runtimeTrain.RideInstanceEntryId, Is.EqualTo(3_986));
          Assert.That(runtimeTrain.TrainInstanceEntryId, Is.EqualTo(3_987));
          Assert.That(runtimeTrain.TrackEntryId,
            Is.EqualTo(park.RideTracks.Single().SourceEntryId));
          Assert.That(runtimeTrain.TrainResource, Is.SameAs(savedTrainLink));
          Assert.That(runtimeTrain.HasSavedMotionState, Is.True);
          Assert.That(runtimeTrain.SavedDistance, Is.EqualTo(88.933914f));
          Assert.That(runtimeTrain.SavedReversed, Is.False);
           Assert.That(runtimeTrain.SavedSpeed, Is.Zero);
           Assert.That(runtimeTrain.HasSavedOperationalState, Is.True);
           Assert.That(runtimeTrain.SavedOperationalState, Is.EqualTo(28));
           Assert.That(runtimeTrain.SavedOperationalStateTime,
             Is.EqualTo(2.699997f).Within(0.000001f));
           Assert.That(
             motionAdvanceAuthorization.Status,
             Is.EqualTo(
               RideTrainMotionAdvanceAuthorizationStatus.UnsupportedSavedOperationalState));
           Assert.That(motionAdvanceAuthorization.IsAuthorized, Is.False);
           Assert.That(runtimeTrain.SavedVisualVariant, Is.Null);
          Assert.That(
            runtimeTrain.SavedMotionState,
            Is.EqualTo(new RideTrainMotionState(88.933914f, 0f, Reversed: false)));
          Assert.That(runtimeTrain.TrackRuntime.Instance,
            Is.SameAs(data.TrackedRideInstances.Single()));
          Assert.That(consistRuntime.Entries, Has.Count.EqualTo(1));
          Assert.That(consistRuntime.ResolvedCount, Is.EqualTo(1));
          Assert.That(consistRuntime.UnresolvedCount, Is.Zero);
          Assert.That(runtimeConsist.Status,
            Is.EqualTo(RideInstanceTrainConsistRuntimeStatus.Resolved));
          Assert.That(runtimeConsist.Roles!.Entries.Select(entry => entry.Role), Is.EqualTo(
            new[] {
              OpenCobra.OVL.Files.RideTrainCarRole.Front,
              OpenCobra.OVL.Files.RideTrainCarRole.Link,
              OpenCobra.OVL.Files.RideTrainCarRole.Middle,
              OpenCobra.OVL.Files.RideTrainCarRole.Link,
              OpenCobra.OVL.Files.RideTrainCarRole.Middle,
              OpenCobra.OVL.Files.RideTrainCarRole.Link,
              OpenCobra.OVL.Files.RideTrainCarRole.Rear,
            }));
          Assert.That(runtimeConsist.Cars.Select(car => car.PeepSlotEvidence.PeepSlotCount),
            Is.EqualTo(new[] { 3, 0, 12, 0, 12, 0, 12 }));
          Assert.That(carRuntime.SavedCarCount, Is.EqualTo(7));
          Assert.That(carRuntime.LinkedCarCount, Is.EqualTo(7));
          Assert.That(carRuntime.UnreferencedCarCount, Is.Zero);
          Assert.That(carRuntime.ResolvedTrackPieceCarCount, Is.EqualTo(7));
          Assert.That(carRuntime.ResolvedResourceCarCount, Is.EqualTo(7));
          Assert.That(
            nativeTrainLengthWithoutGeometry.Status,
            Is.EqualTo(RideTrainNativeLengthStatus.RuntimeGeometryUnavailable));
          Assert.That(nativeTrainLengthWithoutGeometry.CarCount, Is.EqualTo(7));
          Assert.That(nativeTrainLengthWithoutGeometry.FailedCarIndex, Is.Null);
          Assert.That(nativeTrainLengthWithoutGeometry.Length, Is.Null);
          Assert.That(nativeTrainLength.Status, Is.EqualTo(RideTrainNativeLengthStatus.Resolved));
          Assert.That(nativeTrainLength.CarCount, Is.EqualTo(7));
          Assert.That(nativeTrainLength.FailedCarIndex, Is.Null);
          Assert.That(nativeTrainLength.Length, Is.Not.Null);
          Assert.That(nativeTrainLength.Length, Is.GreaterThan(0f));
          Assert.That(visualHierarchy.AmbiguousPartCount, Is.Zero);
          Assert.That(
            visualHierarchy.ResolvedPartCount + visualHierarchy.UnavailablePartCount,
            Is.EqualTo(visualHierarchy.Cars.Count * 6));
          Assert.That(visualVariants.Entries, Has.Count.EqualTo(7));
          Assert.That(visualVariants.SelectedCount, Is.EqualTo(7));
          Assert.That(visualVariants.FailedCount, Is.Zero);
          Assert.That(
            visualVariants.Entries.Select(entry => entry.SelectedVariant),
            Is.All.EqualTo(RideCarVisualVariant.Normal));
          Assert.That(variantTemplates.Entries, Has.Count.EqualTo(7));
          Assert.That(variantTemplates.ResolvedCount, Is.EqualTo(7));
          Assert.That(variantTemplates.FailedCount, Is.Zero);
          Assert.That(variantCars.CarCount, Is.EqualTo(7));
          Assert.That(variantCars.ResolvedCount, Is.EqualTo(7));
          Assert.That(variantCars.UnresolvedCount, Is.Zero);
          Assert.That(variantCars.Entries.All(car =>
            car.SelectedVariant == RideCarVisualVariant.Normal &&
            car.RequiredBodyRole == OpenCobra.OVL.Files.RideVisualRole.Body), Is.True);
          Assert.That(carRuntime.Entries.Select(car => car.CarInstanceEntryId),
            Is.EqualTo(savedTrainLink.TrainInstance.Cars));
          Assert.That(carRuntime.Entries.All(car => car.HasSavedPhysicalState), Is.True);
          Assert.That(carRuntime.Entries.Select(car => car.SavedLength), Is.EqualTo(new[] {
            6.789251f,
            1.4448397f,
            6.8630643f,
            1.4448397f,
            6.8630643f,
            1.4448397f,
            6.3483453f,
          }));
          Assert.That(carRuntime.Entries.Select(car => car.SavedMass), Is.EqualTo(new[] {
            1_600f,
            1_600f,
            1_100f,
            1_600f,
            1_100f,
            1_600f,
            1_600f,
          }));
          Assert.That(carRuntime.Entries.All(car => car.SavedPositionValid), Is.True);
          Assert.That(maxSavedCarDistanceDelta, Is.LessThanOrEqualTo(0.00003f));
          Assert.That(ordinaryPosePlan.Targets, Has.Count.EqualTo(7));
          Assert.That(
            ordinaryPosePlan.Targets.Select(target => target.CarInstanceEntryId),
            Is.EqualTo(carRuntime.Entries.Select(car => car.CarInstanceEntryId)));
          // Piece-local saved wheel cursors and circuit-global reconstructed distances can select
          // opposite sides of an imported join. Their pose matrices must stay within the same
          // executable-backed join-direction tolerance used by the circuit adapter.
          Assert.That(
            maxSavedPoseTransformDelta,
            Is.LessThanOrEqualTo(RideTrackGraphAdapter.ImportedJoinDirectionTolerance));
          Assert.That(carRuntime.Entries.All(car => car.HasResolvedTrackPieces), Is.True);
          Assert.That(carRuntime.Entries.All(car => car.HasResolvedResource), Is.True);
          Assert.That(wheelCursors.CarCount, Is.EqualTo(7));
          Assert.That(wheelCursors.ResolvedCarCount, Is.EqualTo(7));
          Assert.That(wheelCursors.ResolvedContactCount, Is.EqualTo(14));
          Assert.That(wheelCursors.Entries.All(car => car.IsResolved), Is.True);
          Assert.That(
            visualTemplates.Templates.Count + visualTemplates.UnresolvedVisualCount,
            Is.EqualTo(visualTemplates.VisualOccurrenceCount));
           Assert.That(visualTemplates.BodyTemplates, Has.Count.EqualTo(14));
           Assert.That(hierarchyParts.SourceCarCount, Is.EqualTo(7));
           Assert.That(hierarchyParts.EligibleCarCount, Is.EqualTo(7));
           Assert.That(hierarchyParts.PlannedCarCount, Is.LessThanOrEqualTo(7));
           Assert.That(hierarchyParts.PartCount,
             Is.LessThanOrEqualTo(hierarchyParts.PlannedCarCount * 6));
           Assert.That(hierarchyParts.Instances.All(instance =>
             instance.SavedCar.IsResolved &&
             instance.RegistryIndex >= 0 &&
             instance.RegistryIndex < hierarchyParts.PartCount), Is.True);
           Assert.That(hierarchyScene.SourceCarCount, Is.EqualTo(7));
           Assert.That(hierarchyScene.PlannedCarCount,
             Is.EqualTo(hierarchyParts.PlannedCarCount));
           Assert.That(hierarchyScene.SourcePartCount, Is.EqualTo(hierarchyParts.PartCount));
           Assert.That(hierarchyScene.BuiltPartCount + hierarchyScene.SkippedPartCount,
             Is.EqualTo(hierarchyParts.PartCount));
           Assert.That(hierarchyScene.ModelBindings, Has.Count.EqualTo(hierarchyScene.ModelCount));
           Assert.That(hierarchyScene.ModelBindings.All(binding =>
             ReferenceEquals(
               binding.Instance,
               hierarchyParts.Instances[binding.PartRegistryIndex])), Is.True);
           Assert.That(distinctBodyShapes, Has.Length.EqualTo(13));
          Assert.That(bodyBatches, Has.Length.EqualTo(41));
          Assert.That(
            bodyBatches.Sum(batch => batch.Mesh.Vertices.Count),
            Is.EqualTo(5_780));
          Assert.That(
            bodyBatches.Sum(batch => batch.Mesh.Indices.Count),
            Is.EqualTo(12_192));
          Assert.That(staticCars.CarCount, Is.EqualTo(7));
          Assert.That(staticCars.ResolvedCount, Is.EqualTo(7));
          Assert.That(staticCars.UnresolvedCount, Is.Zero);
          Assert.That(staticCars.UnavailableStaticPoseCount, Is.Zero);
          Assert.That(staticCars.Entries.All(car => car.Pose != null), Is.True);
          Assert.That(carScene.SourceCarCount, Is.EqualTo(7));
          Assert.That(carScene.BuiltCarCount, Is.EqualTo(7));
          Assert.That(carScene.SkippedCarCount, Is.Zero);
          Assert.That(carScene.ModelCount, Is.EqualTo(15));
          Assert.That(carScene.MissingMaterialBatchCount, Is.Zero);
          Assert.That(
            carScene.BuildMode,
            Is.EqualTo(RideCarStaticSceneBuildMode.SelectedVariants));
          Assert.That(carScene.VariantBuiltCarCount, Is.EqualTo(7));
          Assert.That(carScene.BaseFallbackBuiltCarCount, Is.Zero);
          Assert.That(carScene.ModelBindings.All(binding =>
            binding.Source == RideCarStaticSceneModelSource.SelectedVariant &&
            binding.VariantEntry != null), Is.True);
          Assert.That(
            geometry.UnresolvedResourceTrackCount,
            Is.EqualTo(unresolvedTracks));
          Assert.That(
            geometry.UnsupportedGeometryTrackCount,
            Is.EqualTo(unsupportedGeometryTracks));
          Assert.That(
            geometry.UnsupportedTopologyTrackCount,
            Is.EqualTo(unsupportedTracks));
          Assert.That(geometry.Tracks.Where(link => link.IsResolved).All(link =>
            link.Status is RideTrackGeometryStatus.OpenTrack or RideTrackGeometryStatus.Circuit),
            Is.True);
          Assert.That(geometry.Tracks.Where(link => !link.IsResolved).All(link =>
            link.Status is RideTrackGeometryStatus.UnresolvedResources or
              RideTrackGeometryStatus.UnsupportedGeometry or
              RideTrackGeometryStatus.UnsupportedTopology),
            Is.True);
        }
      } finally {
        if (hierarchySceneForCleanup != null)
          foreach (var model in hierarchySceneForCleanup.Models.Reverse()) model.Dispose();
        if (carSceneForCleanup != null)
          foreach (var model in carSceneForCleanup.Models.Reverse()) model.Dispose();
      }
    } finally {
      terrain.TextureCatalog?.Dispose();
    }
  }

  private static string ResolveMapPath(string installRoot) {
    var configured = Environment.GetEnvironmentVariable("OPENRCT3_MAP_PATH");
    if (string.IsNullOrWhiteSpace(configured))
      return Path.Combine(installRoot, "Campaigns", "Base", "BoxOffice.dat");
    return Path.GetFullPath(Path.IsPathRooted(configured)
      ? configured
      : Path.Combine(installRoot, configured));
  }

  private static float MaximumMatrixDifference(Matrix4x4 left, Matrix4x4 right) =>
    new[] {
      MathF.Abs(left.M11 - right.M11), MathF.Abs(left.M12 - right.M12),
      MathF.Abs(left.M13 - right.M13), MathF.Abs(left.M14 - right.M14),
      MathF.Abs(left.M21 - right.M21), MathF.Abs(left.M22 - right.M22),
      MathF.Abs(left.M23 - right.M23), MathF.Abs(left.M24 - right.M24),
      MathF.Abs(left.M31 - right.M31), MathF.Abs(left.M32 - right.M32),
      MathF.Abs(left.M33 - right.M33), MathF.Abs(left.M34 - right.M34),
      MathF.Abs(left.M41 - right.M41), MathF.Abs(left.M42 - right.M42),
      MathF.Abs(left.M43 - right.M43), MathF.Abs(left.M44 - right.M44),
    }.Max();

  private static int Count(
    IReadOnlyDictionary<RideTrackGeometryStatus, int> counts,
    RideTrackGeometryStatus status
  ) => counts.TryGetValue(status, out var count) ? count : 0;

  private static IReadOnlyList<RailJoinMetric> MeasureRailJoins(TrackCircuit circuit) {
    var metrics = new List<RailJoinMetric>(circuit.Pieces.Count * 2);
    foreach (var index in Enumerable.Range(0, circuit.Pieces.Count)) {
      var outgoing = circuit.Pieces[index].Piece.Exit;
      var incoming = circuit.Pieces[(index + 1) % circuit.Pieces.Count].Piece.Entry;
      metrics.Add(MeasureRailJoin(outgoing.Left, incoming.Left));
      metrics.Add(MeasureRailJoin(outgoing.Right, incoming.Right));
    }
    return metrics;
  }

  private static RailJoinMetric MeasureRailJoin(
    RailEndpoint outgoing,
    RailEndpoint incoming
  ) {
    var outgoingMagnitude = outgoing.Tangent.Length();
    var incomingMagnitude = incoming.Tangent.Length();
    var magnitudeScale = MathF.Max(outgoingMagnitude, incomingMagnitude);
    return new(
      Vector3.Distance(outgoing.Position, incoming.Position),
      Vector3.Distance(
        Vector3.Normalize(outgoing.Tangent),
        Vector3.Normalize(incoming.Tangent)),
      MathF.Abs(outgoingMagnitude - incomingMagnitude) / magnitudeScale,
      MathF.Abs(TrackMath.ShortestAngleDelta(
        outgoing.BankRadians,
        incoming.BankRadians)));
  }

  private static string FormatCounts(
    IReadOnlyDictionary<RideTrackResourceCatalogLoadIssueKind, int> counts
  ) => string.Join(
    ",",
    Enum.GetValues<RideTrackResourceCatalogLoadIssueKind>().Select(kind =>
      $"{kind}={counts.GetValueOrDefault(kind)}"));

  private sealed record RailJoinMetric(
    float PositionDifference,
    float DirectionDifference,
    float MagnitudeDifference,
    float BankDifferenceRadians);
}
