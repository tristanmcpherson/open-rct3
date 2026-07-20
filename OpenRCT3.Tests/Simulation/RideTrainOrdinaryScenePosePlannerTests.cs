// Ride Train Ordinary Scene Pose Planner Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrainOrdinaryScenePosePlannerTests {
  [Test]
  public void Resolve_ComposesCurrentGeometryIntoCompleteExactSceneTargets() {
    var traversal = new TrackCircuitTraversal(LongStadium());
    var train = Train(
      traversal,
      new(100f, Geometry(4f, 1f, -2f)),
      new(200f, Geometry(5f, -1f, -2f)));
    var motion = new RideTrainMotionState(20f, 3f, Reversed: false);

    var result = RideTrainOrdinaryScenePosePlanner.Resolve(
      traversal,
      motion,
      train.Inputs);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Traversal, Is.SameAs(traversal));
      Assert.That(result.MotionState, Is.EqualTo(motion));
      Assert.That(result.NativeLength.IsResolved, Is.True);
      Assert.That(result.NativeLength.Length, Is.EqualTo(10f));
      Assert.That(result.NativeLength.Length,
        Is.Not.EqualTo(train.Cars.Sum(car => car.SavedLength)));
      Assert.That(result.CarDistances.BaseDistances, Is.EqualTo(new[] { 19f, 15f }));
      Assert.That(result.CarPoses, Has.Count.EqualTo(2));
      Assert.That(result.Targets, Has.Count.EqualTo(2));
      Assert.That(result.Targets.Select(target => target.RegistryIndex),
        Is.EqualTo(train.Cars.Select(car => car.RegistryIndex)));
      Assert.That(result.Targets.Select(target => target.CarInstanceEntryId),
        Is.EqualTo(train.Cars.Select(car => car.CarInstanceEntryId)));
      Assert.That(result.Targets.Select(target => target.Transform),
        Is.EqualTo(result.CarPoses.Select(pose => pose.Transform)));
      Assert.That(result.CarPoses.All(pose => pose.Traversal == traversal), Is.True);
      Assert.That(result.CarPoses.All(pose => TrackMath.IsFinite(pose.Transform)), Is.True);
    }
  }

  [Test]
  public void Resolve_FailsClosedOnMissingRearGeometry() {
    var traversal = new TrackCircuitTraversal(LongStadium());
    var train = Train(
      traversal,
      new(100f, Geometry(4f, 1f, -2f)),
      new(200f, Geometry(5f, -1f, -2f)));
    var missingRear = train.Inputs.ToArray();
    missingRear[1] = missingRear[1] with { HasRearGeometry = false };

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrainOrdinaryScenePosePlanner.Resolve(
        traversal,
        new(20f, 3f, Reversed: false),
        missingRear)));

    Assert.That(error!.Message, Does.Contain("no rear geometry"));
  }

  private static BuiltTrain Train(
    TrackCircuitTraversal traversal,
    params CarTerms[] terms
  ) {
    const ulong trainId = 100;
    var carIds = terms
      .Select((_, index) => Convert.ToUInt64(1_000 + index))
      .ToArray();
    var ride = new DatTrackedRideInstanceData(
      10,
      "Test Ride",
      20,
      "test-ride",
      "test-ride:trr",
      1,
      terms.Length,
      0,
      [trainId]);
    var savedTrain = new DatRideTrainInstanceData(
      trainId,
      "test-train",
      "test-train:rit",
      ride.EntryId,
      0,
      0f,
      0f,
      carIds);
    var trainLink = new RideTrainInstanceResourceLink(ride, savedTrain, 0, null);
    var trackRuntime = new RideInstanceTrackRuntimeEntry(
      0,
      0,
      null!,
      null!,
      GraphTraversal: null,
      traversal);
    var trainRuntime = new RideInstanceTrainRuntimeEntry(0, trackRuntime, trainLink);

    var resourceLinks = new RideCarLink[terms.Length];
    var roles = new RideTrainConsistRoleEntry[terms.Length];
    var consistCars = new RideInstanceTrainConsistCarRuntimeEntry[terms.Length];
    foreach (var index in Enumerable.Range(0, terms.Length)) {
      var role = Role(index, terms.Length);
      var resourceName = $"car-{index}:ric";
      var resource = CarResource($"car-{index}");
      var source = new RideCarResourceSource(null!, resource, []);
      var link = new RideCarLink(role, resourceName, source, []);
      var roleEntry = new RideTrainConsistRoleEntry(index, index, role, resourceName, 1);
      resourceLinks[index] = link;
      roles[index] = roleEntry;
      consistCars[index] = new(
        roleEntry,
        link,
        new RideCarPeepSlotEvidence(link, null!, 1, []));
    }

    var roleResolution = new RideTrainConsistRoleResolution(terms.Length, roles);
    var consist = new RideInstanceTrainConsistRuntimeEntry(
      trainRuntime,
      new TrackedRideResourceLink(null!, [], null),
      new RideTrainLink("test-train", null, resourceLinks),
      roleResolution,
      Array.AsReadOnly(consistCars),
      RideInstanceTrainConsistRuntimeStatus.Resolved);
    var missingPiece = new RideCarTrackPieceRuntimeLink(
      0,
      RideCarTrackPieceRuntimeStatus.MissingSavedReference,
      null,
      null);
    var cars = new RideCarInstanceRuntimeEntry[terms.Length];
    var inputs = new RideTrainOrdinaryScenePoseCarInput[terms.Length];
    foreach (var index in Enumerable.Range(0, terms.Length)) {
      var role = roles[index].Role;
      var savedCar = new DatRideCarInstanceData(
        carIds[index],
        trainId,
        index,
        Convert.ToInt32(role),
        0f,
        0f,
        0,
        0,
        0f,
        false,
        0f,
        terms[index].SavedLength,
        1_000f,
        true);
      var runtime = new RideCarInstanceRuntimeEntry(
        index,
        index,
        trainRuntime,
        savedCar,
        role,
        missingPiece,
        missingPiece,
        RideCarResourceRuntimeStatus.Resolved,
        resourceLinks[index],
        consist,
        consistCars[index]);
      var staticEntry = new RideCarStaticInstanceEntry(
        index,
        runtime,
        SavedCursor: null!,
        RideCarStaticInstanceIssue.None,
        BodyTemplate: null,
        terms[index].Geometry,
        Pose: null,
        GeometryUnavailableDetail: null,
        StaticPoseUnavailableDetail: null);
      cars[index] = runtime;
      inputs[index] = new(runtime, staticEntry, terms[index].Geometry, true);
    }
    return new(cars, inputs);
  }

  private static RideCarLongitudinalGeometry Geometry(
    float carLength,
    float frontOffset,
    float rearOffset
  ) {
    var carRear = Vector3.Zero;
    var carFront = new Vector3(carLength, 0f, 0f);
    var frontWheel = carFront + new Vector3(frontOffset, 0f, 0f);
    var rearWheel = frontWheel + new Vector3(rearOffset, 0f, 0f);
    return new(
      carFront,
      carRear,
      frontWheel,
      rearWheel,
      Vector3.UnitX,
      carLength,
      frontWheel.X,
      rearWheel.X,
      MathF.Abs(rearOffset),
      2f,
      2f);
  }

  private static RideTrainCarRole Role(int index, int count) {
    if (index == 0) return RideTrainCarRole.Front;
    if (index == count - 1) return RideTrainCarRole.Rear;
    return RideTrainCarRole.Middle;
  }

  private static RideCar CarResource(string name) => new(
    name,
    RideCarVersion.Vanilla,
    name,
    name,
    0,
    0,
    "body:svd",
    0f,
    null,
    0f,
    new RideCarAxisSettings(0, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    new RideCarBobbingSettings(0, 0f, 0f, 0f),
    new RideCarAnimationSettings(
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    [],
    new RideCarWheelSettings(
      new(null, 0),
      new(null, 0),
      new(null, 0),
      new(null, 0)),
    new RideCarAxleSettings(new(null, 0), new(null, 0)),
    new RideCarBaseUnknownSettings(
      0,
      0f,
      0,
      0f,
      0f,
      0f,
      0f),
    null,
    null);

  private static TrackCircuit LongStadium() {
    const float tangentScale = 3f;
    return new TrackCircuit([
      CircuitPiece("bottom", new(-5f, 0f, 0f), new(5f, 0f, 0f),
        Vector3.UnitX * tangentScale, Vector3.UnitX * tangentScale),
      CircuitPiece("lower-right", new(5f, 0f, 0f), new(7f, 0f, 2f),
        Vector3.UnitX * tangentScale, Vector3.UnitZ * tangentScale),
      CircuitPiece("right", new(7f, 0f, 2f), new(7f, 0f, 8f),
        Vector3.UnitZ * tangentScale, Vector3.UnitZ * tangentScale),
      CircuitPiece("upper-right", new(7f, 0f, 8f), new(5f, 0f, 10f),
        Vector3.UnitZ * tangentScale, -Vector3.UnitX * tangentScale),
      CircuitPiece("top", new(5f, 0f, 10f), new(-5f, 0f, 10f),
        -Vector3.UnitX * tangentScale, -Vector3.UnitX * tangentScale),
      CircuitPiece("upper-left", new(-5f, 0f, 10f), new(-7f, 0f, 8f),
        -Vector3.UnitX * tangentScale, -Vector3.UnitZ * tangentScale),
      CircuitPiece("left", new(-7f, 0f, 8f), new(-7f, 0f, 2f),
        -Vector3.UnitZ * tangentScale, -Vector3.UnitZ * tangentScale),
      CircuitPiece("lower-left", new(-7f, 0f, 2f), new(-5f, 0f, 0f),
        -Vector3.UnitZ * tangentScale, Vector3.UnitX * tangentScale),
    ]);
  }

  private static TrackCircuitPiece CircuitPiece(
    string id,
    Vector3 start,
    Vector3 end,
    Vector3 startTangent,
    Vector3 endTangent
  ) {
    var startGauge = Vector3.UnitY * 0.5f;
    var endGauge = Vector3.UnitY * 0.5f;
    return new(id, new TrackPiece(TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        start - startGauge,
        startTangent,
        start + startGauge,
        startTangent,
        0f),
      new RailControlPair(
        1f,
        end - endGauge,
        endTangent,
        end + endGauge,
        endTangent,
        0f),
    ])));
  }

  private sealed record BuiltTrain(
    RideCarInstanceRuntimeEntry[] Cars,
    RideTrainOrdinaryScenePoseCarInput[] Inputs
  );

  private readonly record struct CarTerms(
    float SavedLength,
    RideCarLongitudinalGeometry Geometry
  );
}
