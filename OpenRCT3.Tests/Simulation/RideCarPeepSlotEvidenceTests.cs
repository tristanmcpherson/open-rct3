using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarPeepSlotEvidenceTests {
  [Test]
  public void Build_UsesContiguousPeepMarkersAndAllowsMarkerFreeLowerLod() {
    var fixture = Body(
      BoneLod("high", "body-high.unique.ovl", Shape("BodyHigh", PeepNames(12))),
      BoneLod("low", "body-low.unique.ovl", Shape("BodyLow", "Root")));

    var index = RideCarPeepSlotEvidenceIndex.Build(new([fixture.Body], 0));

    Assert.That(index.TryGet(fixture.Car, out var evidence), Is.True);
    using (Assert.EnterMultipleScope()) {
      Assert.That(evidence.Car, Is.SameAs(fixture.Car));
      Assert.That(evidence.BodyVisual, Is.SameAs(fixture.Body));
      Assert.That(evidence.PeepSlotCount, Is.EqualTo(12));
      Assert.That(evidence.MarkerLods, Has.Count.EqualTo(1));
      Assert.That(evidence.MarkerLods[0], Is.SameAs(fixture.Body.Lods[0]));
    }
  }

  [Test]
  public void Build_RejectsGapInNativePeepMarkerSequence() {
    var fixture = Body(BoneLod(
      "high",
      "body-high.unique.ovl",
      Shape("BodyHigh", "Peep01", "Peep03")));

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarPeepSlotEvidenceIndex.Build(new([fixture.Body], 0))));

    Assert.That(error!.Message, Does.Contain("not contiguous"));
  }

  [Test]
  public void Build_RejectsConflictingPositiveLodCounts() {
    var fixture = Body(
      BoneLod("high", "body-high.unique.ovl", Shape("BodyHigh", PeepNames(3))),
      BoneLod("medium", "body-medium.unique.ovl", Shape("BodyMedium", PeepNames(4))));

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarPeepSlotEvidenceIndex.Build(new([fixture.Body], 0))));

    Assert.That(error!.Message, Does.Contain("disagree"));
  }

  [Test]
  public void Build_UnresolvedBodyLodLeavesCapacityUnproven() {
    var fixture = Body(new RideVisualShapeLodLink(
      Lod("missing", "Missing:bsh"),
      null,
      null));

    var index = RideCarPeepSlotEvidenceIndex.Build(new([fixture.Body], 1));

    Assert.That(index.TryGet(fixture.Car, out _), Is.False);
  }

  private static (RideCarLink Car, RideCarVisualShapeLink Body) Body(
    params RideVisualShapeLodLink[] lods
  ) {
    var closure = new[] {
      "ride.unique.ovl",
      "train.unique.ovl",
      "car.unique.ovl",
      "visual.unique.ovl",
    }.Concat(lods
      .Where(lod => lod.BoneShapeSource != null)
      .Select(lod => lod.BoneShapeSource!.File.Path))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    var visual = new SceneryItemVisual(
      "Body",
      0,
      0f,
      0f,
      0f,
      0f,
      lods.Select(lod => lod.Lod).ToArray(),
      null);
    var visualLink = new RideVisualLink(
      RideVisualRole.Body,
      "Body:svd",
      new RideVisualResourceSource(
        new OvlFile("Body", FileType.SceneryItemVisual, "visual.unique.ovl"),
        visual));
    var car = Car("Car", visualLink.Reference);
    var carLink = new RideCarLink(
      RideTrainCarRole.Front,
      "Car:ric",
      new RideCarResourceSource(
        new OvlFile("Car", FileType.RideCar, "car.unique.ovl"),
        car,
        closure),
      [visualLink]);
    var train = Train("Train", carLink.Reference);
    var trainLink = new RideTrainLink(
      "Train",
      new RideTrainResourceSource(
        new OvlFile("Train", FileType.RideTrain, "train.unique.ovl"),
        train,
        closure),
      [carLink]);
    var ride = Ride("Ride", train.Name);
    var rideLink = new TrackedRideResourceLink(
      new TrackedRideResourceSource(
        new OvlFile("Ride", FileType.TrackedRide, "ride.unique.ovl"),
        ride,
        closure),
      [trainLink],
      null);
    return (carLink, new(rideLink, trainLink, carLink, visualLink, lods));
  }

  private static RideVisualShapeLodLink BoneLod(
    string lodName,
    string path,
    BoneShape shape
  ) => new(
    Lod(lodName, $"{shape.Name}:bsh"),
    null,
    new RideBoneShapeResourceSource(
      new OvlFile(shape.Name, FileType.BoneShape, path),
      shape));

  private static SceneryItemVisualLod Lod(string name, string reference) => new(
    name,
    SvdLodType.BoneShape,
    null,
    reference,
    null,
    null,
    new SceneryVisualBillboardSettings(1f, 1f, 0f, 0f, 1f, 1f),
    20f,
    []);

  private static BoneShape Shape(string name, params string[] bones) => new(
    name,
    Vector3.Zero,
    Vector3.One,
    [],
    bones.Select((bone, index) => new BoneShapeBone(
      bone,
      index - 1,
      Matrix4x4.Identity,
      Matrix4x4.Identity)).ToArray());

  private static string[] PeepNames(int count) => Enumerable.Range(1, count)
    .Select(number => number < 10 ? $"Peep0{number}" : $"Peep{number}")
    .ToArray();

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

  private static RideTrain Train(string name, string front) => new(
    name,
    RideTrainVersion.Vanilla,
    name,
    name,
    new RideTrainCars(front, null, null, null, null, null, 1, 1, 1, null),
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
}
