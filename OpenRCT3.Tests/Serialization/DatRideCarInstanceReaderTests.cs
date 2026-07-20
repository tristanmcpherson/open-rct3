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
      Assert.That(train.HasSavedMotionState, Is.True);
      Assert.That(train.Distance, Is.EqualTo(96.5f));
      Assert.That(train.Reversed, Is.True);
      Assert.That(train.Speed, Is.EqualTo(-2.25f));
      Assert.That(train.HasSavedOperationalState, Is.True);
      Assert.That(train.State, Is.EqualTo(45));
      Assert.That(train.StateTime, Is.EqualTo(6.5f));
      Assert.That(train.WhichRideCarSivVariant, Is.EqualTo(1));
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
      Assert.That(cars.Select(car => car.HasSavedPhysicalState), Is.All.True);
      Assert.That(cars.Select(car => car.Length), Is.All.EqualTo(4f));
      Assert.That(cars.Select(car => car.Mass), Is.All.EqualTo(1_000f));
      Assert.That(cars.Select(car => car.PositionValid), Is.All.True);
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

  [Test]
  public void Read_NonFiniteRideTrainSpeedFailsClosed() {
    using var stream = BuildDat(trainSpeed: float.NegativeInfinity);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message,
      Does.Contain("RideTrainInstance Speed contains a non-finite value"));
  }

  [Test]
  public void Read_PartialRideTrainSavedMotionSchemaFailsClosed() {
    var fields = RideTrainInstanceStructure().Fields
      .Where(field => field.Name != "Reversed")
      .ToArray();
    using var stream = BuildDat(
      rideTrainStructure: new StructureSpec("RideTrainInstance", fields));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message,
      Does.Contain("must declare Distance, Reversed, and Speed together"));
  }

  [Test]
  public void Read_PartialRideTrainOperationalStateSchemaFailsClosed() {
    var fields = RideTrainInstanceStructure().Fields
      .Where(field => field.Name != "StateTime")
      .ToArray();
    using var stream = BuildDat(
      rideTrainStructure: new StructureSpec("RideTrainInstance", fields));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain("must declare State and StateTime together"));
  }

  [Test]
  public void Read_RideTrainSavedSpeedSchemaMismatchFailsClosed() {
    var fields = RideTrainInstanceStructure().Fields.ToArray();
    var speedIndex = Array.FindIndex(fields, field => field.Name == "Speed");
    fields[speedIndex] = new FieldSpec("Speed", "int32", 4);
    using var stream = BuildDat(
      rideTrainStructure: new StructureSpec("RideTrainInstance", fields));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain("Speed has kind Int32"));
  }

  [Test]
  public void Read_RideTrainVisualVariantSchemaMismatchFailsClosed() {
    var fields = RideTrainInstanceStructure().Fields.ToArray();
    var variantIndex = Array.FindIndex(
      fields,
      field => field.Name == "WhichRideCarSIVVariant");
    fields[variantIndex] = new FieldSpec("WhichRideCarSIVVariant", "bool", 1);
    using var stream = BuildDat(
      rideTrainStructure: new StructureSpec("RideTrainInstance", fields));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message,
      Does.Contain("WhichRideCarSIVVariant has kind Bool"));
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

  [TestCase(true)]
  [TestCase(false)]
  public void Read_NonFiniteRideCarPhysicalValueFailsClosed(bool length) {
    using var stream = BuildDat(
      firstLength: length ? float.NaN : 4f,
      firstMass: length ? 1_000f : float.PositiveInfinity);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain(length
      ? "RideCarInstance Stats.Length contains a non-finite value"
      : "RideCarInstance Stats.Mass contains a non-finite value"));
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
      $"BoxOffice train 3987: distance={train.Distance:R} reversed={train.Reversed} " +
      $"speed={train.Speed:R} state={train.State} stateTime={train.StateTime:R} " +
      $"length={train.Length:R} mass={train.Mass:R} " +
      $"car IDs={string.Join(", ", train.Cars)}");
    foreach (var car in cars)
      TestContext.WriteLine(
        $"car={car.EntryId} index={car.WhichCar} role={car.WhichRideTrainCar} " +
        $"track={car.TrackPiece}/{car.RearTrackPiece} distance={car.Distance:R} " +
        $"wheels={car.FrontWheelDistance:R}/{car.RearWheelDistance:R} " +
        $"length={car.Length:R} mass={car.Mass:R} posValid={car.PositionValid} " +
        $"reversed={car.Reversed} speed={car.Speed:R}");

    using (Assert.EnterMultipleScope()) {
      Assert.That(train.Cars,
        Is.EqualTo(new ulong[] { 20_402, 20_403, 20_404, 20_405, 20_406, 20_407, 20_408 }));
      Assert.That(train.HasSavedMotionState, Is.True);
      Assert.That(train.Distance, Is.EqualTo(88.933914f));
      Assert.That(train.Reversed, Is.False);
      Assert.That(train.Speed, Is.Zero);
      Assert.That(train.HasSavedOperationalState, Is.True);
      Assert.That(train.State, Is.EqualTo(28));
      Assert.That(train.StateTime, Is.EqualTo(2.699997f).Within(0.000001f));
      Assert.That(train.WhichRideCarSivVariant, Is.Null);
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
      Assert.That(cars.Select(car => car.Length), Is.EqualTo(new[] {
        6.789251f,
        1.4448397f,
        6.8630643f,
        1.4448397f,
        6.8630643f,
        1.4448397f,
        6.3483453f,
      }));
      Assert.That(cars.Select(car => car.HasSavedPhysicalState), Is.All.True);
      Assert.That(cars.Select(car => car.Mass), Is.EqualTo(new[] {
        1_600f,
        1_600f,
        1_100f,
        1_600f,
        1_100f,
        1_600f,
        1_600f,
      }));
      Assert.That(cars.Select(car => car.PositionValid), Is.All.True);
      Assert.That(cars.Select(car => car.Reversed), Is.All.False);
      Assert.That(cars.Select(car => car.Speed), Is.All.Zero);
    }
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Read_ScrubGardensCapturesWildTrainVisualVariantSelections() {
    var installPath = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(string.IsNullOrWhiteSpace(installPath), Is.False);
    var campaignPath = Path.Combine(
      installPath!,
      "Campaigns",
      "Base",
      "Wild",
      "ScrubGardens.dat");

    var data = DatTerrainReader.Read(campaignPath);
    var variantTrains = data.RideTrainInstances
      .Where(train => train.WhichRideCarSivVariant.HasValue)
      .ToArray();
    var carsById = data.RideCarInstances.ToDictionary(car => car.EntryId);
    foreach (var train in variantTrains)
      TestContext.WriteLine(
        $"train={train.EntryId} ride={train.TrackedRideInstance} " +
        $"variant={train.WhichRideCarSivVariant} distance={train.Distance:R} " +
        $"reversed={train.Reversed} speed={train.Speed:R} state={train.State} " +
        $"stateTime={train.StateTime:R} cars={train.Cars.Count} " +
        $"roles={string.Join(',', train.Cars.Select(id => carsById[id].WhichRideTrainCar))}");

    using (Assert.EnterMultipleScope()) {
      Assert.That(variantTrains, Has.Length.EqualTo(24));
      Assert.That(variantTrains.All(train => train.HasSavedMotionState), Is.True);
      Assert.That(variantTrains.All(train => train.HasSavedOperationalState), Is.True);
      Assert.That(
        variantTrains.Where(train => train.TrackedRideInstance == 6_467)
          .Select(train => train.WhichRideCarSivVariant),
        Is.EqualTo(Enumerable.Range(0, 18).Select(index => index % 2 == 0 ? 1 : 0)));
      Assert.That(
        variantTrains.Where(train => train.TrackedRideInstance == 5_014)
          .Select(train => train.WhichRideCarSivVariant),
        Is.All.EqualTo(0));
      Assert.That(variantTrains.Count(train => train.WhichRideCarSivVariant == 1),
        Is.EqualTo(9));
      Assert.That(variantTrains.Count(train => train.WhichRideCarSivVariant == 0),
        Is.EqualTo(15));
      Assert.That(
        variantTrains.SelectMany(train => train.Cars)
          .Select(id => carsById[id].WhichRideTrainCar),
        Is.All.EqualTo(0));
    }
  }

  private static MemoryStream BuildDat(
    ulong firstCarOwner = 1_000,
    int firstWhichCar = 0,
    int firstWhichRideTrainCar = 0,
    float firstSpeed = 3.5f,
    float firstFrontWheelDistance = 91.25f,
    float firstRearWheelDistance = 90.75f,
    float firstLength = 4f,
    float firstMass = 1_000f,
    bool firstPositionValid = true,
    ulong[]? trainCars = null,
    StructureSpec? rideCarStructure = null,
    float trainDistance = 96.5f,
    bool trainReversed = true,
    float trainSpeed = -2.25f,
    int trainVisualVariant = 1,
    int trainState = 45,
    float trainStateTime = 6.5f,
    StructureSpec? rideTrainStructure = null
  ) {
    var cars = trainCars ?? [1_001, 1_002];
    var structures = new[] {
      LandscapeStructure(),
      rideTrainStructure ?? RideTrainInstanceStructure(),
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
      WriteRideTrainInstance(
        writer,
        cars,
        trainDistance,
        trainReversed,
        trainSpeed,
        trainVisualVariant,
        trainState,
        trainStateTime);
      writer.Write(2u);
      writer.Write(1_001ul);
      WriteRideCarInstance(
        writer, firstCarOwner, firstWhichCar, firstWhichRideTrainCar,
        firstFrontWheelDistance, firstRearWheelDistance,
        700, 699, 1.25f, false, firstSpeed,
        firstLength, firstMass, firstPositionValid);
      writer.Write(2u);
      writer.Write(1_002ul);
      WriteRideCarInstance(
        writer, 1_000, 1, 4, 84.5f, 84f, 698, 697, 2.5f, true, -1.25f,
        4f, 1_000f, true);
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

  private static void WriteRideTrainInstance(
    BinaryWriter writer,
    IReadOnlyList<ulong> cars,
    float distance,
    bool reversed,
    float speed,
    int visualVariant,
    int state,
    float stateTime
  ) {
    writer.Write(Convert.ToUInt32(cars.Count * sizeof(ulong)));
    writer.Write(Convert.ToUInt32(cars.Count));
    foreach (var car in cars)
      writer.Write(car);
    writer.Write(distance);
    writer.Write(28.754667f);
    writer.Write(10_200f);
    writer.Write(reversed);
    WriteDatString(writer, @"Cars\TrackedRideCars\SyntheticTrain\SyntheticTrain");
    WriteDatString(writer, "SyntheticTrain:rit");
    writer.Write(speed);
    writer.Write(state);
    writer.Write(stateTime);
    writer.Write(900ul);
    writer.Write(visualVariant);
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
    float speed,
    float length,
    float mass,
    bool positionValid
  ) {
    writer.Write(frontWheelDistance);
    writer.Write(rearWheelDistance);
    writer.Write(rideTrainInstance);
    writer.Write(false);
    WriteVector3(writer);
    writer.Write(distance);
    writer.Write(length);
    writer.Write(mass);
    WriteVector3(writer);
    WriteVector3(writer);
    WriteVector3(writer);
    writer.Write(positionValid);
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
      new FieldSpec("Distance", "float32", 4),
      new FieldSpec("Length", "float32", 4),
      new FieldSpec("Mass", "float32", 4),
      new FieldSpec("Reversed", "bool", 1),
      new FieldSpec("RideTrainOverlayName", "string"),
      new FieldSpec("RideTrainSymbolName", "string"),
      new FieldSpec("Speed", "float32", 4),
      new FieldSpec("State", "int32", 4),
      new FieldSpec("StateTime", "float32", 4),
      new FieldSpec("TrackedRideInstance", "managedobjectptr", 8),
      new FieldSpec("WhichRideCarSIVVariant", "int32", 4),
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
