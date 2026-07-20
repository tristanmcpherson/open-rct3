// Ride Train Native Length Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrainNativeLengthResolverTests {
  [Test]
  public void Resolve_ValidatedCarsFailClosedWithoutRuntimeGeometry() {
    var train = Train(
      new CarTerms(10f),
      new CarTerms(20f));

    var result = RideTrainNativeLengthResolver.Resolve(train.Cars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(RideTrainNativeLengthStatus.RuntimeGeometryUnavailable));
      Assert.That(result.IsResolved, Is.False);
      Assert.That(result.CarCount, Is.EqualTo(2));
      Assert.That(result.FailedCarIndex, Is.Null);
      Assert.That(result.Length, Is.Null);
    }
  }

  [Test]
  public void Resolve_WithRuntimeGeometryUsesCurrentLengthsInNativeOperationOrder() {
    var train = Train(
      new CarTerms(100f),
      new CarTerms(200f));
    var inputs = Inputs(
      train,
      new GeometryTerms(16_777_216f, 1f),
      new GeometryTerms(2f));

    var result = RideTrainNativeLengthResolver.Resolve(inputs);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(RideTrainNativeLengthStatus.Resolved));
      Assert.That(result.IsResolved, Is.True);
      Assert.That(result.CarCount, Is.EqualTo(2));
      Assert.That(result.FailedCarIndex, Is.Null);
      Assert.That(
        BitConverter.SingleToInt32Bits(result.Length!.Value),
        Is.EqualTo(BitConverter.SingleToInt32Bits(16_777_218f)));
    }
  }

  [Test]
  public void Resolve_WithRuntimeGeometryAddsFirstAndFinalEndExtensions() {
    var train = Train(new CarTerms(100f), new CarTerms(200f));
    var inputs = Inputs(
      train,
      new GeometryTerms(10f, 3f),
      new GeometryTerms(7f, RearOffset: -12f, HasRearGeometry: true));

    var result = RideTrainNativeLengthResolver.Resolve(inputs);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(RideTrainNativeLengthStatus.Resolved));
      Assert.That(result.Length, Is.EqualTo(25f));
    }
  }

  [Test]
  public void Resolve_WithRuntimeGeometrySingleCarAddsBothEndExtensions() {
    var train = Train(new CarTerms(100f));
    var inputs = Inputs(
      train,
      new GeometryTerms(10f, 2f, -14f, true));

    var result = RideTrainNativeLengthResolver.Resolve(inputs);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(RideTrainNativeLengthStatus.Resolved));
      Assert.That(result.Length, Is.EqualTo(16f));
    }
  }

  [Test]
  public void Resolve_WithoutRearGeometrySkipsUnreadableFinalEndExtension() {
    var train = Train(new CarTerms(100f));
    var inputs = Inputs(
      train,
      new GeometryTerms(10f, 2f, float.NaN, false));

    var result = RideTrainNativeLengthResolver.Resolve(inputs);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(RideTrainNativeLengthStatus.Resolved));
      Assert.That(result.Length, Is.EqualTo(12f));
    }
  }

  [Test]
  public void Resolve_MissingAlignedRuntimeGeometryFailsClosed() {
    var train = Train(new CarTerms(100f));
    RideTrainNativeLengthInput[] inputs = [new(train.Cars[0], null!, false)];

    var result = RideTrainNativeLengthResolver.Resolve(inputs);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(RideTrainNativeLengthStatus.RuntimeGeometryUnavailable));
      Assert.That(result.FailedCarIndex, Is.Zero);
      Assert.That(result.Length, Is.Null);
    }
  }

  [Test]
  public void Resolve_ReorderedAlignedRuntimeGeometryFailsIdentityValidation() {
    var train = Train(new CarTerms(100f), new CarTerms(200f));
    var first = new RideTrainNativeLengthInput(train.Cars[1], Geometry(2f), false);
    var second = new RideTrainNativeLengthInput(train.Cars[0], Geometry(1f), false);

    var result = RideTrainNativeLengthResolver.Resolve([first, second]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(RideTrainNativeLengthStatus.ChangedCarOrder));
      Assert.That(result.FailedCarIndex, Is.Zero);
    }
  }

  [Test]
  public void Resolve_NonFiniteRuntimeCarLengthFailsClosed() {
    var train = Train(new CarTerms(100f));
    var inputs = Inputs(train, new GeometryTerms(float.NaN));

    var result = RideTrainNativeLengthResolver.Resolve(inputs);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(RideTrainNativeLengthStatus.NonFiniteRuntimeGeometry));
      Assert.That(result.FailedCarIndex, Is.Zero);
      Assert.That(result.Length, Is.Null);
    }
  }

  [Test]
  public void Resolve_NonFiniteFirstEndExtensionFailsClosed() {
    var train = Train(new CarTerms(100f));
    var inputs = Inputs(train, new GeometryTerms(1f, float.PositiveInfinity));

    var result = RideTrainNativeLengthResolver.Resolve(inputs);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(RideTrainNativeLengthStatus.NonFiniteRuntimeGeometry));
      Assert.That(result.FailedCarIndex, Is.Zero);
    }
  }

  [Test]
  public void Resolve_NonFinitePresentRearGeometryFailsClosed() {
    var train = Train(new CarTerms(100f));
    var inputs = Inputs(
      train,
      new GeometryTerms(1f, RearOffset: float.NegativeInfinity, HasRearGeometry: true));

    var result = RideTrainNativeLengthResolver.Resolve(inputs);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(RideTrainNativeLengthStatus.NonFiniteRuntimeGeometry));
      Assert.That(result.FailedCarIndex, Is.Zero);
    }
  }

  [Test]
  public void Resolve_RuntimeEndExtensionOverflowFailsClosed() {
    var train = Train(new CarTerms(100f));
    var inputs = Inputs(
      train,
      new GeometryTerms(float.MaxValue, float.MaxValue));

    var result = RideTrainNativeLengthResolver.Resolve(inputs);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(RideTrainNativeLengthStatus.NonFiniteAccumulation));
      Assert.That(result.FailedCarIndex, Is.Zero);
      Assert.That(result.Length, Is.Null);
    }
  }

  [Test]
  public void Resolve_EmptyConsistFailsClosed() {
    var result = RideTrainNativeLengthResolver.Resolve(
      Array.Empty<RideCarInstanceRuntimeEntry>());

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(RideTrainNativeLengthStatus.EmptyConsist));
      Assert.That(result.CarCount, Is.Zero);
      Assert.That(result.FailedCarIndex, Is.Null);
      Assert.That(result.Length, Is.Null);
    }
  }

  [Test]
  public void Resolve_CarCountAboveLimitFailsClosedBeforeTraversal() {
    var train = Train(new CarTerms(1f), new CarTerms(4f));

    var result = RideTrainNativeLengthResolver.Resolve(
      train.Cars,
      new RideTrainNativeLengthResolverLimits(1));

    Assert.That(
      result.Status,
      Is.EqualTo(RideTrainNativeLengthStatus.CarCountExceedsLimit));
  }

  [Test]
  public void Resolve_NullRuntimeEntryFailsClosed() {
    RideCarInstanceRuntimeEntry[] cars = [null!];

    var result = RideTrainNativeLengthResolver.Resolve(cars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(RideTrainNativeLengthStatus.MalformedRuntimeEntry));
      Assert.That(result.FailedCarIndex, Is.Zero);
      Assert.That(result.Length, Is.Null);
    }
  }

  [Test]
  public void Resolve_UnresolvedConsistFailsClosed() {
    var train = Train(new CarTerms(1f));
    train.Cars[0] = train.Cars[0] with { TrainConsist = null };

    var result = RideTrainNativeLengthResolver.Resolve(train.Cars);

    Assert.That(result.Status, Is.EqualTo(RideTrainNativeLengthStatus.UnresolvedConsist));
  }

  [Test]
  public void Resolve_MissingCarResourceFailsClosed() {
    var train = Train(new CarTerms(1f), new CarTerms(4f));
    train.Cars[1] = train.Cars[1] with {
      ResourceStatus = RideCarResourceRuntimeStatus.UnresolvedCarResource,
      CarResource = null,
    };

    var result = RideTrainNativeLengthResolver.Resolve(train.Cars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(RideTrainNativeLengthStatus.UnresolvedCarResource));
      Assert.That(result.FailedCarIndex, Is.EqualTo(1));
      Assert.That(result.Length, Is.Null);
    }
  }

  [Test]
  public void Resolve_ChangedExactCarResourceIdentityFailsClosed() {
    var train = Train(new CarTerms(1f), new CarTerms(4f));
    var equalClone = train.Cars[1].CarResource! with { };
    train.Cars[1] = train.Cars[1] with { CarResource = equalClone };

    var result = RideTrainNativeLengthResolver.Resolve(train.Cars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(RideTrainNativeLengthStatus.ChangedCarResourceIdentity));
      Assert.That(result.FailedCarIndex, Is.EqualTo(1));
    }
  }

  [Test]
  public void Resolve_ChangedExactTrainIdentityFailsClosed() {
    var train = Train(new CarTerms(1f), new CarTerms(4f));
    var equalClone = train.Cars[1].TrainRuntime with { };
    train.Cars[1] = train.Cars[1] with { TrainRuntime = equalClone };

    var result = RideTrainNativeLengthResolver.Resolve(train.Cars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(RideTrainNativeLengthStatus.ChangedTrainIdentity));
      Assert.That(result.FailedCarIndex, Is.EqualTo(1));
    }
  }

  [Test]
  public void Resolve_ReorderedRuntimeCarsFailClosed() {
    var train = Train(new CarTerms(1f), new CarTerms(4f));
    RideCarInstanceRuntimeEntry[] reordered = [train.Cars[1], train.Cars[0]];

    var result = RideTrainNativeLengthResolver.Resolve(reordered);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Status, Is.EqualTo(RideTrainNativeLengthStatus.ChangedCarOrder));
      Assert.That(result.FailedCarIndex, Is.Zero);
    }
  }

  [Test]
  public void Resolve_TruncatedRuntimeCarsFailClosed() {
    var train = Train(new CarTerms(1f), new CarTerms(4f));

    var result = RideTrainNativeLengthResolver.Resolve([train.Cars[0]]);

    Assert.That(result.Status, Is.EqualTo(RideTrainNativeLengthStatus.CarCountMismatch));
  }

  [Test]
  public void Resolve_CompatibilityCarWithoutSavedPhysicalStateFailsClosed() {
    var train = Train(new CarTerms(1f), new CarTerms(4f));
    var savedCar = train.Cars[1].CarInstance;
    train.Cars[1] = train.Cars[1] with {
      CarInstance = new DatRideCarInstanceData(
        savedCar.EntryId,
        savedCar.RideTrainInstance,
        savedCar.WhichCar,
        savedCar.WhichRideTrainCar,
        savedCar.TrackPiece,
        savedCar.RearTrackPiece,
        savedCar.Distance,
        savedCar.Reversed,
        savedCar.Speed),
    };

    var result = RideTrainNativeLengthResolver.Resolve(train.Cars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(RideTrainNativeLengthStatus.SavedPhysicalStateUnavailable));
      Assert.That(result.FailedCarIndex, Is.EqualTo(1));
      Assert.That(result.Length, Is.Null);
    }
  }

  [Test]
  public void Resolve_NonFiniteAccumulatorFailsClosed() {
    var train = Train(
      new CarTerms(float.MaxValue),
      new CarTerms(float.MaxValue));

    var result = RideTrainNativeLengthResolver.Resolve(train.Cars);

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        result.Status,
        Is.EqualTo(RideTrainNativeLengthStatus.NonFiniteAccumulation));
      Assert.That(result.FailedCarIndex, Is.EqualTo(1));
      Assert.That(result.Length, Is.Null);
    }
  }

  [Test]
  public void Resolve_RejectsNullInput() {
    Assert.Throws<ArgumentNullException>(new Action(() =>
      RideTrainNativeLengthResolver.Resolve(
        (IReadOnlyList<RideCarInstanceRuntimeEntry>)null!)));
  }

  [TestCase(0)]
  [TestCase(-1)]
  public void Resolve_RejectsInvalidLimit(int maximumCarCount) {
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideTrainNativeLengthResolver.Resolve(
        Array.Empty<RideCarInstanceRuntimeEntry>(),
        new RideTrainNativeLengthResolverLimits(maximumCarCount))));
  }

  private static RideTrainNativeLengthInput[] Inputs(
    BuiltTrain train,
    params GeometryTerms[] terms
  ) {
    if (train.Cars.Length != terms.Length)
      throw new ArgumentException("Every runtime car requires aligned geometry.", nameof(terms));

    return terms
      .Select((term, index) => new RideTrainNativeLengthInput(
        train.Cars[index],
        Geometry(term.CarLength, term.FrontOffset, term.RearOffset),
        term.HasRearGeometry))
      .ToArray();
  }

  private static RideCarLongitudinalGeometry Geometry(
    float carLength,
    float frontOffset = 0f,
    float rearOffset = 0f
  ) {
    var frontWheel = new Vector3(frontOffset, 0f, 0f);
    var rearWheel = new Vector3(frontOffset + rearOffset, 0f, 0f);
    return new RideCarLongitudinalGeometry(
      Vector3.Zero,
      new Vector3(-carLength, 0f, 0f),
      frontWheel,
      rearWheel,
      Vector3.UnitX,
      carLength,
      frontOffset,
      frontOffset + rearOffset,
      MathF.Abs(rearOffset),
      1f,
      1f);
  }

  private static BuiltTrain Train(params CarTerms[] terms) {
    var trainId = 100UL;
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
    var trainRuntime = new RideInstanceTrainRuntimeEntry(0, null!, trainLink);

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
      cars[index] = new(
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
    }
    return new(cars);
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

  private sealed record BuiltTrain(RideCarInstanceRuntimeEntry[] Cars);

  private readonly record struct CarTerms(float SavedLength);

  private readonly record struct GeometryTerms(
    float CarLength,
    float FrontOffset = 0f,
    float RearOffset = 0f,
    bool HasRearGeometry = false
  );
}
