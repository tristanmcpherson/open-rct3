// DAT Ride Car Instance Reader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Text;
using OpenRCT3.Serialization;

namespace OpenRCT3.Tests.Serialization;

[TestFixture]
public class DatRideCarInstanceReaderTests {
  [Test]
  public void Read_CapturesExactTrainOrderRoleAndResumeState() {
    using var stream = BuildDat();

    var data = DatTerrainReader.Read(stream);
    var train = data.RideTrainInstances.Single();
    var cars = data.RideCarInstances.ToArray();

    using (Assert.EnterMultipleScope()) {
      Assert.That(stream.Position, Is.EqualTo(stream.Length));
      Assert.That(train.Cars, Is.EqualTo(new ulong[] { 1_001, 1_002 }));
      Assert.That(cars.Select(car => car.EntryId), Is.EqualTo(train.Cars));
      Assert.That(cars.Select(car => car.RideTrainInstance),
        Is.All.EqualTo(train.EntryId));
      Assert.That(cars.Select(car => car.WhichCar), Is.EqualTo(new[] { 0, 1 }));
      Assert.That(cars.Select(car => car.WhichRideTrainCar), Is.EqualTo(new[] { 0, 4 }));
      Assert.That(cars.Select(car => car.FrontWheelDistance),
        Is.EqualTo(new[] { 91.25f, 84.5f }));
      Assert.That(cars.Select(car => car.RearWheelDistance),
        Is.EqualTo(new[] { 90.75f, 84f }));
      Assert.That(cars.Select(car => car.TrackPiece),
        Is.EqualTo(new ulong[] { 700, 698 }));
      Assert.That(cars.Select(car => car.RearTrackPiece),
        Is.EqualTo(new ulong[] { 699, 697 }));
      Assert.That(cars.Select(car => car.Distance), Is.EqualTo(new[] { 1.25f, 2.5f }));
      Assert.That(cars.Select(car => car.Reversed), Is.EqualTo(new[] { false, true }));
      Assert.That(cars.Select(car => car.Speed), Is.EqualTo(new[] { 3.5f, -1.25f }));
    }
  }

  [Test]
  public void Read_RideCarBackpointerMismatchFailsClosed() {
    using var stream = BuildDat(firstCarOwner: 999);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain("reciprocal owner is 999"));
  }

  [Test]
  public void Read_RideCarWhichCarMismatchFailsClosed() {
    using var stream = BuildDat(firstWhichCar: 1);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain("index 0, but its WhichCar value is 1"));
  }

  [Test]
  public void Read_RideCarReferenceRepeatedByTrainFailsClosed() {
    using var stream = BuildDat(trainCars: [1_001, 1_001]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain("referenced more than once"));
  }

  [Test]
  public void Read_NonFiniteRideCarSpeedFailsClosed() {
    using var stream = BuildDat(firstSpeed: float.NaN);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message,
      Does.Contain("RideCarInstance Stats.Speed contains a non-finite value"));
  }

  [TestCase(true)]
  [TestCase(false)]
  public void Read_NonFiniteRideCarWheelDistanceFailsClosed(bool frontWheel) {
    using var stream = BuildDat(
      firstFrontWheelDistance: frontWheel ? float.NaN : 91.25f,
      firstRearWheelDistance: frontWheel ? 90.75f : float.PositiveInfinity);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain(frontWheel
      ? "RideCarInstance FrontWheelDistance contains a non-finite value"
      : "RideCarInstance RearWheelDistance contains a non-finite value"));
  }

  [Test]
  public void Read_UnsupportedRideTrainCarRoleFailsClosed() {
    using var stream = BuildDat(firstWhichRideTrainCar: 6);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain("unsupported WhichRideTrainCar value 6"));
  }

  [Test]
  public void Read_RideCarStatsSchemaMismatchFailsClosed() {
    var stats = RideCarStatsFields().ToArray();
    var speedIndex = Array.FindIndex(stats, field => field.Name == "Speed");
    stats[speedIndex] = new FieldSpec("Speed", "int32", 4);
    using var stream = BuildDat(rideCarStructure: RideCarInstanceStructure(stats));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain("Stats.Speed has kind Int32"));
  }

  [Test]
  public void Read_RideCarWheelDistanceSchemaMismatchFailsClosed() {
    using var stream = BuildDat(
      rideCarStructure: RideCarInstanceStructure(
        RideCarStatsFields(),
        frontWheelDistance: new FieldSpec("FrontWheelDistance", "int32", 4)));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain("FrontWheelDistance has kind Int32"));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Read_BoxOfficeCapturesExactStreamlinedMonoConsistAndResumeState() {
    var installPath = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(string.IsNullOrWhiteSpace(installPath), Is.False);
    var campaignPath = Path.Combine(installPath!, "Campaigns", "Base", "BoxOffice.dat");

    var data = DatTerrainReader.Read(campaignPath);
    var train = data.RideTrainInstances.Single(instance => instance.EntryId == 3_987);
    var carsById = data.RideCarInstances.ToDictionary(car => car.EntryId);
    var cars = train.Cars.Select(id => carsById[id]).ToArray();
    TestContext.WriteLine(
      $"BoxOffice train 3987 car IDs: {string.Join(", ", train.Cars)}");
    foreach (var car in cars)
      TestContext.WriteLine(
        $"car={car.EntryId} index={car.WhichCar} role={car.WhichRideTrainCar} " +
        $"track={car.TrackPiece}/{car.RearTrackPiece} distance={car.Distance:R} " +
        $"wheels={car.FrontWheelDistance:R}/{car.RearWheelDistance:R} " +
        $"reversed={car.Reversed} speed={car.Speed:R}");

    using (Assert.EnterMultipleScope()) {
      Assert.That(train.Cars,
        Is.EqualTo(new ulong[] { 20_402, 20_403, 20_404, 20_405, 20_406, 20_407, 20_408 }));
      Assert.That(cars.Select(car => car.RideTrainInstance),
        Is.All.EqualTo(train.EntryId));
      Assert.That(cars.Select(car => car.WhichCar),
        Is.EqualTo(Enumerable.Range(0, 7)));
      Assert.That(cars.Select(car => car.WhichRideTrainCar),
        Is.EqualTo(new[] { 0, 5, 2, 5, 2, 5, 4 }));
      Assert.That(cars.Select(car => car.FrontWheelDistance), Is.EqualTo(new[] {
        87.03028f,
        82.1484f,
        77.86171f,
        73.840485f,
        69.553795f,
        65.53257f,
        61.308304f,
      }));
      Assert.That(cars.Select(car => car.RearWheelDistance), Is.EqualTo(new[] {
        84.05878f,
        80.69277f,
        76.056206f,
        72.38486f,
        67.74829f,
        64.07694f,
        59.502808f,
      }));
      Assert.That(cars.Select(car => car.TrackPiece),
        Is.EqualTo(new ulong[] { 9_902, 9_899, 9_896, 9_893, 9_890, 9_887, 9_886 }));
      Assert.That(cars.Select(car => car.RearTrackPiece),
        Is.EqualTo(new ulong[] { 9_899, 9_896, 9_893, 9_890, 9_887, 9_886, 5_407 }));
      Assert.That(cars.Select(car => car.Distance), Is.EqualTo(new[] {
        88.933914f,
        82.14466f,
        80.699814f,
        73.83675f,
        72.3919f,
        65.52883f,
        64.083984f,
      }));
      Assert.That(cars.Select(car => car.Reversed), Is.All.False);
      Assert.That(cars.Select(car => car.Speed), Is.All.Zero);
    }
  }

  private static MemoryStream BuildDat(
    ulong firstCarOwner = 1_000,
    int firstWhichCar = 0,
    int firstWhichRideTrainCar = 0,
    float firstSpeed = 3.5f,
    float firstFrontWheelDistance = 91.25f,
    float firstRearWheelDistance = 90.75f,
    ulong[]? trainCars = null,
    StructureSpec? rideCarStructure = null
  ) {
    var cars = trainCars ?? [1_001, 1_002];
    var structures = new[] {
      LandscapeStructure(),
      RideTrainInstanceStructure(),
      rideCarStructure ?? RideCarInstanceStructure(RideCarStatsFields()),
    };
    var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(Convert.ToUInt32(structures.Length));
      foreach (var structure in structures)
        WriteStructure(writer, structure);
      writer.Write(4u);
      writer.Write(0u);
      writer.Write(1ul);
      WriteTerrain(writer);
      writer.Write(1u);
      writer.Write(1_000ul);
      WriteRideTrainInstance(writer, cars);
      writer.Write(2u);
      writer.Write(1_001ul);
      WriteRideCarInstance(
        writer, firstCarOwner, firstWhichCar, firstWhichRideTrainCar,
        firstFrontWheelDistance, firstRearWheelDistance,
        700, 699, 1.25f, false, firstSpeed);
      writer.Write(2u);
      writer.Write(1_002ul);
      WriteRideCarInstance(
        writer, 1_000, 1, 4, 84.5f, 84f, 698, 697, 2.5f, true, -1.25f);
    }
    stream.Position = 0;
    return stream;
  }

  private static void WriteStructure(BinaryWriter writer, StructureSpec structure) {
    WriteAscii16(writer, structure.Name);
    writer.Write(Convert.ToUInt32(structure.Fields.Count));
    foreach (var field in structure.Fields)
      WriteField(writer, field);
  }

  private static void WriteField(BinaryWriter writer, FieldSpec field) {
    WriteAscii16(writer, field.Name);
    WriteAscii16(writer, field.Kind);
    writer.Write(field.FixedSize);
    writer.Write(Convert.ToUInt32(field.Children.Count));
    foreach (var child in field.Children)
      WriteField(writer, child);
  }

  private static void WriteAscii16(BinaryWriter writer, string value) {
    var bytes = Encoding.ASCII.GetBytes(value);
    writer.Write(Convert.ToUInt16(bytes.Length));
    writer.Write(bytes);
  }

  private static void WriteDatString(BinaryWriter writer, string value) {
    var bytes = Encoding.ASCII.GetBytes(value);
    writer.Write(Convert.ToUInt32(bytes.Length));
    writer.Write(bytes);
  }

  private static void WriteTerrain(BinaryWriter writer) {
    writer.Write(42u);
    writer.Write(Convert.ToByte(1));
    writer.Write(Convert.ToByte(1));
    writer.Write(0f);
    writer.Write(0f);
    writer.Write(4f);
    writer.Write(4f);
    writer.Write(0f);
    writer.Write(0f);
    writer.Write(0f);
    writer.Write(0f);
    writer.Write(Convert.ToByte(0));
    writer.Write(Convert.ToByte(0));
    writer.Write(new byte[6]);
  }

  private static void WriteRideTrainInstance(BinaryWriter writer, IReadOnlyList<ulong> cars) {
    writer.Write(Convert.ToUInt32(cars.Count * sizeof(ulong)));
    writer.Write(Convert.ToUInt32(cars.Count));
    foreach (var car in cars)
      writer.Write(car);
    writer.Write(28.754667f);
    writer.Write(10_200f);
    WriteDatString(writer, @"Cars\TrackedRideCars\SyntheticTrain\SyntheticTrain");
    WriteDatString(writer, "SyntheticTrain:rit");
    writer.Write(900ul);
    writer.Write(0);
  }

  private static void WriteRideCarInstance(
    BinaryWriter writer,
    ulong rideTrainInstance,
    int whichCar,
    int whichRideTrainCar,
    float frontWheelDistance,
    float rearWheelDistance,
    ulong trackPiece,
    ulong rearTrackPiece,
    float distance,
    bool reversed,
    float speed
  ) {
    writer.Write(frontWheelDistance);
    writer.Write(rearWheelDistance);
    writer.Write(rideTrainInstance);
    writer.Write(false);
    WriteVector3(writer);
    writer.Write(distance);
    writer.Write(4f);
    writer.Write(1_000f);
    WriteVector3(writer);
    WriteVector3(writer);
    WriteVector3(writer);
    writer.Write(true);
    writer.Write(rearTrackPiece);
    writer.Write(reversed);
    WriteVector3(writer);
    writer.Write(speed);
    WriteVector3(writer);
    WriteVector3(writer);
    writer.Write(trackPiece);
    WriteVector3(writer);
    writer.Write(0f);
    writer.Write(true);
    WriteVector3(writer);
    WriteVector3(writer);
    writer.Write(whichCar);
    writer.Write(whichRideTrainCar);
  }

  private static void WriteVector3(BinaryWriter writer) {
    writer.Write(0f);
    writer.Write(0f);
    writer.Write(0f);
  }

  private static StructureSpec LandscapeStructure() => new(
    "Landscape",
    [new FieldSpec("EngineTerrain", "GE_Terrain")]);

  private static StructureSpec RideTrainInstanceStructure() => new(
    "RideTrainInstance",
    [
      new FieldSpec(
        "Cars",
        "array",
        NestedFields: [new FieldSpec("Car", "managedobjectptr", 8)]),
      new FieldSpec("Length", "float32", 4),
      new FieldSpec("Mass", "float32", 4),
      new FieldSpec("RideTrainOverlayName", "string"),
      new FieldSpec("RideTrainSymbolName", "string"),
      new FieldSpec("TrackedRideInstance", "managedobjectptr", 8),
      new FieldSpec("WhichTrain", "int32", 4),
    ]);

  private static StructureSpec RideCarInstanceStructure(
    IReadOnlyList<FieldSpec> statsFields,
    FieldSpec? frontWheelDistance = null
  ) => new(
    "RideCarInstance",
    [
      frontWheelDistance ?? new FieldSpec("FrontWheelDistance", "float32", 4),
      new FieldSpec("RearWheelDistance", "float32", 4),
      new FieldSpec("RideTrainInstance", "managedobjectptr", 8),
      new FieldSpec("Stats", "struct", 160, statsFields),
      new FieldSpec("WhichCar", "int32", 4),
      new FieldSpec("WhichRideTrainCar", "int32", 4),
    ]);

  private static IReadOnlyList<FieldSpec> RideCarStatsFields() => [
    new("AccelValid", "bool", 1),
    new("AngVelocity", "vector3", 12),
    new("Distance", "float32", 4),
    new("Length", "float32", 4),
    new("Mass", "float32", 4),
    new("MidSplinePos", "vector3", 12),
    new("Orient", "orientation", 12),
    new("Pos", "vector3", 12),
    new("PosValid", "bool", 1),
    new("RearTrackPiece", "managedobjectptr", 8),
    new("Reversed", "bool", 1),
    new("SeatAccel", "vector3", 12),
    new("Speed", "float32", 4),
    new("TrackAccel", "vector3", 12),
    new("TrackOrient", "orientation", 12),
    new("TrackPiece", "managedobjectptr", 8),
    new("TrackVelocity", "vector3", 12),
    new("VelTimeDeltaAccum", "float32", 4),
    new("VelocityValid", "bool", 1),
    new("WorldAccel", "vector3", 12),
    new("WorldVelocity", "vector3", 12),
  ];

  private sealed record StructureSpec(
    string Name,
    IReadOnlyList<FieldSpec> Fields);

  private sealed record FieldSpec(
    string Name,
    string Kind,
    uint FixedSize = 0,
    IReadOnlyList<FieldSpec>? NestedFields = null
  ) {
    public IReadOnlyList<FieldSpec> Children { get; } =
      NestedFields ?? Array.Empty<FieldSpec>();
  }
}
