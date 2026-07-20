using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideInstanceTrainConsistRuntimeRegistryTests {
  [Test]
  public void Build_ComposesBoxOfficeEquivalentRolesWithExactGraphEvidence() {
    var fixture = Create();

    var registry = RideInstanceTrainConsistRuntimeRegistry.Build(
      fixture.TrainRuntime,
      fixture.Resources);

    Assert.That(registry.Entries, Has.Count.EqualTo(1));
    var entry = registry.Entries[0];
    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.ResolvedCount, Is.EqualTo(1));
      Assert.That(registry.UnresolvedCount, Is.Zero);
      Assert.That(entry.Status, Is.EqualTo(RideInstanceTrainConsistRuntimeStatus.Resolved));
      Assert.That(entry.TrainRuntime, Is.SameAs(fixture.TrainRuntime.Entries[0]));
      Assert.That(entry.RideGraph, Is.SameAs(fixture.RideGraph));
      Assert.That(entry.TrainGraph, Is.SameAs(fixture.TrainGraph));
      Assert.That(entry.Roles, Is.Not.Null);
      Assert.That(entry.Roles!.EffectiveCarCount, Is.EqualTo(4));
      Assert.That(entry.Cars.Select(car => car.Role.Role), Is.EqualTo(new[] {
        RideTrainCarRole.Front,
        RideTrainCarRole.Link,
        RideTrainCarRole.Middle,
        RideTrainCarRole.Link,
        RideTrainCarRole.Middle,
        RideTrainCarRole.Link,
        RideTrainCarRole.Rear,
      }));
      Assert.That(entry.Cars.Select(car => car.Role.PeepSlotCount),
        Is.EqualTo(new[] { 3, 0, 12, 0, 12, 0, 12 }));
    }
    foreach (var car in entry.Cars) {
      Assert.That(car.CarResource, Is.SameAs(fixture.Cars[car.Role.Role]));
      Assert.That(car.PeepSlotEvidence.Car, Is.SameAs(car.CarResource));
      Assert.That(
        car.CarResource.Source!.AllowedArchivePaths,
        Is.SameAs(fixture.ArchiveClosure));
    }
  }

  [Test]
  public void Build_UnresolvedSavedRitRemainsExplicit() {
    var fixture = Create(resolveSavedTrain: false);

    var entry = RideInstanceTrainConsistRuntimeRegistry.Build(
      fixture.TrainRuntime,
      fixture.Resources).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        entry.Status,
        Is.EqualTo(RideInstanceTrainConsistRuntimeStatus.UnresolvedRideTrainResource));
      Assert.That(entry.TrainGraph, Is.Null);
      Assert.That(entry.Roles, Is.Null);
      Assert.That(entry.Cars, Is.Empty);
    }
  }

  [Test]
  public void Build_UnresolvedRicRemainsExplicit() {
    var fixture = Create(unresolvedRole: RideTrainCarRole.Middle);

    var entry = RideInstanceTrainConsistRuntimeRegistry.Build(
      fixture.TrainRuntime,
      fixture.Resources).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        entry.Status,
        Is.EqualTo(RideInstanceTrainConsistRuntimeStatus.UnresolvedRideCarResource));
      Assert.That(entry.RideGraph, Is.SameAs(fixture.RideGraph));
      Assert.That(entry.TrainGraph, Is.SameAs(fixture.TrainGraph));
      Assert.That(entry.Roles, Is.Null);
      Assert.That(entry.Cars, Is.Empty);
    }
  }

  [Test]
  public void Build_MissingBodyMarkerEvidenceRemainsExplicit() {
    var fixture = Create(omitEvidenceRole: RideTrainCarRole.Rear);

    var entry = RideInstanceTrainConsistRuntimeRegistry.Build(
      fixture.TrainRuntime,
      fixture.Resources).Entries.Single();

    Assert.That(
      entry.Status,
      Is.EqualTo(RideInstanceTrainConsistRuntimeStatus.MissingPeepSlotEvidence));
    Assert.That(entry.Cars, Is.Empty);
  }

  [Test]
  public void Build_DoesNotSubstituteValueEqualDecodedRit() {
    var fixture = Create(cloneGraphTrain: true);

    var entry = RideInstanceTrainConsistRuntimeRegistry.Build(
      fixture.TrainRuntime,
      fixture.Resources).Entries.Single();

    Assert.That(
      entry.Status,
      Is.EqualTo(RideInstanceTrainConsistRuntimeStatus.MissingRideTrainGraph));
  }

  [Test]
  public void Build_RejectsDuplicateExactRitOccurrenceWithinRide() {
    var fixture = Create(duplicateGraphTrain: true);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrainConsistRuntimeRegistry.Build(
        fixture.TrainRuntime,
        fixture.Resources)));

    Assert.That(error!.Message, Does.Contain("duplicate exact RIT"));
  }

  [Test]
  public void Build_AcceptsIndependentlyMaterializedEquivalentArchiveClosure() {
    var fixture = Create(cloneGraphTrainClosure: true);

    var entry = RideInstanceTrainConsistRuntimeRegistry.Build(
      fixture.TrainRuntime,
      fixture.Resources).Entries.Single();

    Assert.That(entry.Status, Is.EqualTo(RideInstanceTrainConsistRuntimeStatus.Resolved));
    Assert.That(entry.TrainGraph!.Source!.AllowedArchivePaths,
      Is.Not.SameAs(fixture.ArchiveClosure));
  }

  [Test]
  public void Build_RejectsDifferentSavedAndGraphArchiveClosures() {
    var fixture = Create(differentGraphTrainClosure: true);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrainConsistRuntimeRegistry.Build(
        fixture.TrainRuntime,
        fixture.Resources)));

    Assert.That(error!.Message, Does.Contain("archive-closure identity"));
  }

  private static Fixture Create(
    bool resolveSavedTrain = true,
    RideTrainCarRole? unresolvedRole = null,
    RideTrainCarRole? omitEvidenceRole = null,
    bool cloneGraphTrain = false,
    bool duplicateGraphTrain = false,
    bool cloneGraphTrainClosure = false,
    bool differentGraphTrainClosure = false
  ) {
    var archiveClosure = new[] {
      "ride.unique.ovl",
      "train.unique.ovl",
      "cars.unique.ovl",
      "visuals.unique.ovl",
      "front-body.unique.ovl",
      "middle-body.unique.ovl",
      "rear-body.unique.ovl",
      "link-body.unique.ovl",
    };
    var ride = Ride("BoxOfficeRide", "StreamlinedMono");
    var rideFile = new OvlFile(ride.Name, FileType.TrackedRide, archiveClosure[0]);
    var train = Train("StreamlinedMono");
    var trainFile = new OvlFile(train.Name, FileType.RideTrain, archiveClosure[1]);
    var savedRide = Instance(900, 700, [1_000]);
    var savedTrain = TrainInstance(1_000, savedRide);
    var instanceLink = new RideInstanceResourceLink(
      savedRide,
      new RideInstanceResourceSource("BoxOffice", rideFile, ride));
    var trainResources = RideTrainInstanceResourceRegistry.Build(
      [savedRide],
      [savedTrain],
      resolveSavedTrain
        ? [new RideTrainInstanceResourceSource(
          savedTrain.RideTrainOverlayName,
          new RideTrainResourceSource(trainFile, train, archiveClosure))]
        : []);
    var trackRuntime = Runtime([savedRide]);
    var trainRuntime = RideInstanceTrainRuntimeRegistry.Build(trackRuntime, trainResources);

    var graphTrain = cloneGraphTrain ? CloneTrain(train) : train;
    IReadOnlyList<string> graphClosure = differentGraphTrainClosure
      ? archiveClosure.Append("extra.unique.ovl").ToArray()
      : cloneGraphTrainClosure
        ? archiveClosure.ToArray()
        : archiveClosure;
    var visuals = new List<RideCarVisualShapeLink>();
    var cars = new Dictionary<RideTrainCarRole, RideCarLink>();
    var carParts = new List<CarPart>();
    AddCar(RideTrainCarRole.Front, "Front", 3);
    AddCar(RideTrainCarRole.Middle, "Middle", 12);
    AddCar(RideTrainCarRole.Rear, "Rear", 12);
    AddCar(RideTrainCarRole.Link, "Link", 0);
    var trainGraph = new RideTrainLink(
      graphTrain.Name,
      new RideTrainResourceSource(trainFile, graphTrain, graphClosure),
      carParts.Select(part => part.Link).ToArray());
    var trainGraphs = duplicateGraphTrain
      ? new[] { trainGraph, trainGraph }
      : [trainGraph];
    var rideGraph = new TrackedRideResourceLink(
      new TrackedRideResourceSource(rideFile, ride, archiveClosure),
      trainGraphs,
      null);
    foreach (var part in carParts) {
      if (part.Body == null || part.Role == omitEvidenceRole) continue;
      visuals.Add(new(
        rideGraph,
        trainGraph,
        part.Link,
        part.Body.Visual,
        [part.Body.Lod]));
    }

    var resources = new RideInstanceResourceLoadResult(
      [instanceLink],
      trainResources,
      new RideResourceGraph([rideGraph], unresolvedRole == null ? 0 : 1),
      new RideCarVisualResourceBridgeResult(
        visuals,
        omitEvidenceRole == null ? 0 : 1),
      new RideResourceDecodeCounts(1, 1, 4, 4));
    return new(
      trainRuntime,
      resources,
      rideGraph,
      trainGraph,
      cars,
      archiveClosure);

    void AddCar(RideTrainCarRole role, string name, int peepCount) {
      var reference = $"{name}:ric";
      if (role == unresolvedRole) {
        var unresolved = new RideCarLink(role, reference, null, []);
        cars.Add(role, unresolved);
        carParts.Add(new(role, unresolved, null));
        return;
      }

      var bodyVisual = new SceneryItemVisual(
        $"{name}Body",
        0,
        0f,
        0f,
        0f,
        0f,
        [BodyLod($"{name}BodyShape")],
        null);
      var visualLink = new RideVisualLink(
        RideVisualRole.Body,
        $"{name}Body:svd",
        new RideVisualResourceSource(
          new OvlFile(
            bodyVisual.Name,
            FileType.SceneryItemVisual,
            archiveClosure[3]),
          bodyVisual));
      var car = Car(name, visualLink.Reference);
      var carLink = new RideCarLink(
        role,
        reference,
        new RideCarResourceSource(
          new OvlFile(name, FileType.RideCar, archiveClosure[2]),
          car,
          archiveClosure),
        [visualLink]);
      var shape = Shape($"{name}BodyShape", peepCount);
      var path = archiveClosure[role switch {
        RideTrainCarRole.Front => 4,
        RideTrainCarRole.Middle => 5,
        RideTrainCarRole.Rear => 6,
        RideTrainCarRole.Link => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
      }];
      var lod = new RideVisualShapeLodLink(
        bodyVisual.Lods[0],
        null,
        new RideBoneShapeResourceSource(
          new OvlFile(shape.Name, FileType.BoneShape, path),
          shape));
      cars.Add(role, carLink);
      carParts.Add(new(role, carLink, new(visualLink, lod)));
    }
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
    "Box Office Monorail",
    trackId,
    "BoxOffice",
    "BoxOfficeRide:trr",
    nTrains: trains.Length,
    nCarsPerTrain: 4,
    trainSelection: 0,
    trains);

  private static DatRideTrainInstanceData TrainInstance(
    ulong entryId,
    DatTrackedRideInstanceData owner
  ) => new(
    entryId,
    "BoxOffice",
    "StreamlinedMono:rit",
    owner.EntryId,
    whichTrain: 0,
    length: 12.5f,
    mass: 1_000f,
    cars: []);

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

  private static SceneryItemVisualLod BodyLod(string shape) => new(
    "high",
    SvdLodType.BoneShape,
    null,
    $"{shape}:bsh",
    null,
    null,
    new SceneryVisualBillboardSettings(1f, 1f, 0f, 0f, 1f, 1f),
    20f,
    []);

  private static BoneShape Shape(string name, int peepCount) => new(
    name,
    Vector3.Zero,
    Vector3.One,
    [],
    Enumerable.Range(1, peepCount).Select((number, index) => new BoneShapeBone(
      number < 10 ? $"Peep0{number}" : $"Peep{number}",
      index - 1,
      Matrix4x4.Identity,
      Matrix4x4.Identity)).ToArray());

  private static RideCar Car(string name, string visual) => new(
    name,
    RideCarVersion.Vanilla,
    name,
    name,
    0,
    0,
    visual,
    1f,
    null,
    -1f,
    new RideCarAxisSettings(0, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    new RideCarBobbingSettings(0, 0f, 0f, 0f),
    new RideCarAnimationSettings(
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    [],
    new RideCarWheelSettings(
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarAxleSettings(
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarBaseUnknownSettings(0, 0f, 0, 0f, 0f, 0f, 0f),
    null,
    null);

  private static RideTrain Train(string name) => new(
    name,
    RideTrainVersion.Vanilla,
    name,
    name,
    new RideTrainCars(
      "Front:ric",
      null,
      "Middle:ric",
      null,
      "Rear:ric",
      "Link:ric",
      1,
      8,
      4,
      null),
    new RideTrainSpeedSettings(0f, 0f, 0f),
    new RideTrainCameraSettings(
      0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    new RideTrainWaterSettings(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    0,
    new RideTrainUnknownSettings(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    null,
    null,
    null,
    null,
    null);

  private static RideTrain CloneTrain(RideTrain train) => new(
    train.Name,
    train.Version,
    train.InternalName,
    train.InternalDescription,
    train.Cars,
    train.Speed,
    train.Camera,
    train.Water,
    train.OvertakeFlag,
    train.Unknowns,
    train.LeftLiftSpline,
    train.RightLiftSpline,
    train.Station,
    train.Expansion,
    train.Wild);

  private static TrackedRide Ride(string name, string train) => new(
    name,
    TrackedRideVersion.Vanilla,
    [],
    [train],
    null,
    null,
    null,
    null!,
    null!,
    null!,
    null!,
    null!,
    null,
    null);

  private sealed record BodyPart(
    RideVisualLink Visual,
    RideVisualShapeLodLink Lod);

  private sealed record CarPart(
    RideTrainCarRole Role,
    RideCarLink Link,
    BodyPart? Body);

  private sealed record Fixture(
    RideInstanceTrainRuntimeRegistry TrainRuntime,
    RideInstanceResourceLoadResult Resources,
    TrackedRideResourceLink RideGraph,
    RideTrainLink TrainGraph,
    IReadOnlyDictionary<RideTrainCarRole, RideCarLink> Cars,
    IReadOnlyList<string> ArchiveClosure);
}
