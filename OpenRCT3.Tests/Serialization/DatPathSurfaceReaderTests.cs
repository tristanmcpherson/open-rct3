// DAT Path Surface Reader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Text;
using OpenRCT3.Serialization;

namespace OpenRCT3.Tests.Serialization;

[TestFixture]
public class DatPathSurfaceReaderTests {
  [Test]
  public void Read_AllPathSurfaceStructures_PreservesAndResolvesExactFields() {
    using var stream = BuildDat(
      [
        LandscapeStructure(),
        PathGroundStructure(),
        PathQueueStructure(),
        PathTypeDatabaseEntryStructure(),
        QueueTypeDatabaseEntryStructure(),
        QueueTypeGroundSurfaceStructure(),
      ],
      [
        new EntrySpec(0, 1, writer => WriteTerrain(writer, width: 3, height: 1)),
        new EntrySpec(3, 101, writer =>
          WriteDatabaseEntry(writer, true, false, true, "Asphalt")),
        new EntrySpec(4, 202, writer =>
          WriteDatabaseEntry(writer, false, true, false, "QueueSet1")),
        new EntrySpec(5, 303, writer => WriteQueueGroundSurface(
          writer,
          new DatPathSurfaceColours(int.MinValue, 0, int.MaxValue),
          queueType: 202)),
        new EntrySpec(1, 401, writer => WriteGroundPath(writer, 0, surface: 101)),
        new EntrySpec(2, 402, writer => WriteQueuePath(writer, 1, surface: 303)),
      ]);

    var terrain = DatTerrainReader.Read(stream);

    var pathType = terrain.PathTypeDatabaseEntries.Single();
    var queueType = terrain.QueueTypeDatabaseEntries.Single();
    var queueGround = terrain.QueueTypeGroundSurfaces.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(pathType.EntryId, Is.EqualTo(101));
      Assert.That(pathType.IsAvailable, Is.True);
      Assert.That(pathType.IsHidden, Is.False);
      Assert.That(pathType.IsInvented, Is.True);
      Assert.That(pathType.SystemName, Is.EqualTo("Asphalt"));

      Assert.That(queueType.EntryId, Is.EqualTo(202));
      Assert.That(queueType.IsAvailable, Is.False);
      Assert.That(queueType.IsHidden, Is.True);
      Assert.That(queueType.IsInvented, Is.False);
      Assert.That(queueType.SystemName, Is.EqualTo("QueueSet1"));

      Assert.That(queueGround.EntryId, Is.EqualTo(303));
      Assert.That(
        queueGround.Colours,
        Is.EqualTo(new DatPathSurfaceColours(int.MinValue, 0, int.MaxValue)));
      Assert.That(queueGround.QueueType, Is.EqualTo(202));
      Assert.That(queueGround.ResolvedQueueType, Is.SameAs(queueType));

      Assert.That(terrain.Paths[0].ResolvedSurface, Is.SameAs(pathType));
      Assert.That(terrain.Paths[1].ResolvedSurface, Is.SameAs(queueGround));
      Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }
  }

  [Test]
  public void Read_PathDatabaseEntryWithMismatchedSchemaFailsClosed() {
    var fields = DatabaseEntryFields();
    fields[3] = new("SystemName", "string", 4);

    AssertInvalidSchema(new("PathTypeDatabaseEntry", fields));
  }

  [Test]
  public void Read_QueueGroundSurfaceWithMismatchedNestedSchemaFailsClosed() {
    var fields = QueueGroundSurfaceFields();
    fields[0] = new(
      "Colours",
      "struct",
      12,
      [
        new("COL0", "int32", 4),
        new("COL1", "int32", 4),
        new("Colour2", "int32", 4),
      ]);

    AssertInvalidSchema(new("QueueTypeGroundSurface", fields));
  }

  [Test]
  public void Read_QueueGroundSurfaceWithReferenceInsteadOfManagedPointerFailsClosed() {
    var fields = QueueGroundSurfaceFields();
    fields[1] = new("QueueType", "reference", 8);

    AssertInvalidSchema(new("QueueTypeGroundSurface", fields));
  }

  [Test]
  public void Read_InvalidPathDatabaseBooleanFailsClosed() {
    using var stream = BuildDat(
      [LandscapeStructure(), PathTypeDatabaseEntryStructure()],
      [
        new EntrySpec(0, 1, writer => WriteTerrain(writer, width: 1, height: 1)),
        new EntrySpec(1, 2, writer => {
          writer.Write(Convert.ToByte(2));
          writer.Write(Convert.ToByte(0));
          writer.Write(Convert.ToByte(1));
          WriteString(writer, "Asphalt");
        }),
      ]);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_FunValleyRetainsInstalledPathAndQueueSurfaceDatabase() {
    var path = Path.GetFullPath(Path.Combine(
      TestContext.CurrentContext.TestDirectory,
      "..",
      "..",
      "..",
      "..",
      "OpenCobra",
      "Tests",
      "Fixtures",
      "Parks",
      "Fun Valley Amusment park.dat"));
    Assert.That(File.Exists(path), Is.True, $"Fun Valley fixture is missing: {path}");

    var terrain = DatTerrainReader.Read(path);

    using (Assert.EnterMultipleScope()) {
      Assert.That(terrain.PathTypeDatabaseEntries, Is.Not.Empty);
      Assert.That(terrain.QueueTypeDatabaseEntries, Is.Not.Empty);
      Assert.That(terrain.QueueTypeGroundSurfaces, Is.Not.Empty);
      Assert.That(
        terrain.PathTypeDatabaseEntries.Select(entry => entry.SystemName),
        Has.All.Not.Empty);
      Assert.That(
        terrain.QueueTypeDatabaseEntries.Select(entry => entry.SystemName),
        Has.All.Not.Empty);
      Assert.That(
        terrain.QueueTypeGroundSurfaces.Where(entry => entry.QueueType != 0),
        Has.All.Matches<DatQueueTypeGroundSurfaceData>(
          entry => entry.ResolvedQueueType != null));
    }
  }

  private static StructureSpec LandscapeStructure() =>
    new("Landscape", [new("EngineTerrain", "GE_Terrain")]);

  private static StructureSpec PathGroundStructure() =>
    new("PathGround", [
      new("ColIndex", "uint8", 1),
      new("Direction", "uint8", 1),
      new("PathType", "uint8", 1),
      new("RowIndex", "uint8", 1),
      new("Surface", "reference", 8),
      new("SurfaceType", "uint8", 1),
      new("bool", "uint8", 1),
    ]);

  private static StructureSpec PathQueueStructure() =>
    new("PathQueue", [
      new("BaseHeight", "int32", 4),
      new("ColIndex", "uint8", 1),
      new("Direction", "uint8", 1),
      new("EndDirection", "uint8", 1),
      new("FenceEntry", "managedobjectptr", 8),
      new(
        "FenceFlexiColours",
        "struct",
        12,
        [
          new("COL0", "int32", 4),
          new("COL1", "int32", 4),
          new("COL2", "int32", 4),
        ]),
      new("PathType", "uint8", 1),
      new("QuantisedHeight", "int32", 4),
      new("QueueLine", "reference", 8),
      new("RowIndex", "uint8", 1),
      new("SceneryItem", "reference", 8),
      new("SlopeType", "uint8", 1),
      new("StartDirection", "uint8", 1),
      new("Surface", "reference", 8),
      new("SurfaceType", "uint8", 1),
      new("bool", "uint8", 1),
    ]);

  private static StructureSpec PathTypeDatabaseEntryStructure() =>
    new("PathTypeDatabaseEntry", DatabaseEntryFields());

  private static StructureSpec QueueTypeDatabaseEntryStructure() =>
    new("QueueTypeDatabaseEntry", DatabaseEntryFields());

  private static StructureSpec QueueTypeGroundSurfaceStructure() =>
    new("QueueTypeGroundSurface", QueueGroundSurfaceFields());

  private static FieldSpec[] DatabaseEntryFields() => [
    new("IsAvailable", "bool", 1),
    new("IsHidden", "bool", 1),
    new("IsInvented", "bool", 1),
    new("SystemName", "string", 0),
  ];

  private static FieldSpec[] QueueGroundSurfaceFields() => [
    new(
      "Colours",
      "struct",
      12,
      [
        new("COL0", "int32", 4),
        new("COL1", "int32", 4),
        new("COL2", "int32", 4),
      ]),
    new("QueueType", "managedobjectptr", 8),
  ];

  private static MemoryStream BuildDat(
    IReadOnlyList<StructureSpec> structures,
    IReadOnlyList<EntrySpec> entries
  ) {
    var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(Convert.ToUInt32(structures.Count));
      foreach (var structure in structures) {
        WriteAscii16(writer, structure.Name);
        writer.Write(Convert.ToUInt32(structure.Fields.Count));
        foreach (var field in structure.Fields) WriteFieldDefinition(writer, field);
      }

      writer.Write(Convert.ToUInt32(entries.Count));
      foreach (var entry in entries) {
        writer.Write(entry.StructureIndex);
        writer.Write(entry.Id);
        entry.WriteValue(writer);
      }
    }
    stream.Position = 0;
    return stream;
  }

  private static void WriteFieldDefinition(BinaryWriter writer, FieldSpec field) {
    WriteAscii16(writer, field.Name);
    WriteAscii16(writer, field.Kind);
    writer.Write(field.FixedSize);
    writer.Write(Convert.ToUInt32(field.Children.Count));
    foreach (var child in field.Children) WriteFieldDefinition(writer, child);
  }

  private static void WriteAscii16(BinaryWriter writer, string value) {
    var bytes = Encoding.ASCII.GetBytes(value);
    writer.Write(Convert.ToUInt16(bytes.Length));
    writer.Write(bytes);
  }

  private static void WriteTerrain(BinaryWriter writer, int width, int height) {
    using var payload = new MemoryStream();
    using (var payloadWriter = new BinaryWriter(payload, Encoding.ASCII, leaveOpen: true)) {
      payloadWriter.Write(Convert.ToByte(width));
      payloadWriter.Write(Convert.ToByte(height));
      payloadWriter.Write(-16f);
      payloadWriter.Write(-20f);
      payloadWriter.Write(4f);
      payloadWriter.Write(4f);
      foreach (var _ in Enumerable.Range(0, checked(width * height))) {
        payloadWriter.Write(1f);
        payloadWriter.Write(2f);
        payloadWriter.Write(3f);
        payloadWriter.Write(4f);
        payloadWriter.Write(Convert.ToByte(5));
        payloadWriter.Write(Convert.ToByte(6));
        foreach (var padding in Enumerable.Range(18, 6))
          payloadWriter.Write(Convert.ToByte(padding));
      }
    }
    writer.Write(Convert.ToUInt32(payload.Length));
    writer.Write(payload.ToArray());
  }

  private static void WriteDatabaseEntry(
    BinaryWriter writer,
    bool isAvailable,
    bool isHidden,
    bool isInvented,
    string systemName
  ) {
    writer.Write(Convert.ToByte(isAvailable));
    writer.Write(Convert.ToByte(isHidden));
    writer.Write(Convert.ToByte(isInvented));
    WriteString(writer, systemName);
  }

  private static void WriteString(BinaryWriter writer, string value) {
    var bytes = Encoding.ASCII.GetBytes(value);
    writer.Write(Convert.ToUInt32(bytes.Length));
    writer.Write(bytes);
  }

  private static void WriteQueueGroundSurface(
    BinaryWriter writer,
    DatPathSurfaceColours colours,
    ulong queueType
  ) {
    writer.Write(colours.Col0);
    writer.Write(colours.Col1);
    writer.Write(colours.Col2);
    writer.Write(queueType);
  }

  private static void WriteGroundPath(BinaryWriter writer, byte colIndex, ulong surface) {
    writer.Write(colIndex);
    writer.Write(byte.MaxValue);
    writer.Write(Convert.ToByte(0));
    writer.Write(Convert.ToByte(0));
    writer.Write(surface);
    writer.Write(byte.MaxValue);
    writer.Write(Convert.ToByte(0));
  }

  private static void WriteQueuePath(BinaryWriter writer, byte colIndex, ulong surface) {
    writer.Write(0);
    writer.Write(colIndex);
    writer.Write(byte.MaxValue);
    writer.Write(byte.MaxValue);
    writer.Write(0ul);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    writer.Write(Convert.ToByte(2));
    writer.Write(0);
    writer.Write(0ul);
    writer.Write(Convert.ToByte(0));
    writer.Write(0ul);
    writer.Write(Convert.ToByte(3));
    writer.Write(byte.MaxValue);
    writer.Write(surface);
    writer.Write(byte.MaxValue);
    writer.Write(Convert.ToByte(0));
  }

  private static void AssertInvalidSchema(StructureSpec invalidStructure) {
    using var stream = BuildDat(
      [LandscapeStructure(), invalidStructure],
      [new EntrySpec(0, 1, writer => WriteTerrain(writer, width: 1, height: 1))]);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  private sealed record StructureSpec(
    string Name,
    IReadOnlyList<FieldSpec> Fields);

  private sealed record FieldSpec(
    string Name,
    string Kind,
    uint FixedSize = 0,
    IReadOnlyList<FieldSpec>? ChildFields = null) {
    public IReadOnlyList<FieldSpec> Children { get; } =
      ChildFields ?? Array.Empty<FieldSpec>();
  }

  private sealed record EntrySpec(
    uint StructureIndex,
    ulong Id,
    Action<BinaryWriter> WriteValue);
}
