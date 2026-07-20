using System.Text;
using OpenRCT3.Serialization;

namespace OpenRCT3.Tests.Serialization;

[TestFixture]
public class DatPathReaderTests {
  [Test]
  public void Read_AllFourPathStructures_PreservesRawValuesIdsAndKinds() {
    var structures = new[] {
      LandscapeStructure(),
      PathTileStructure(),
      PathGroundStructure(),
      PathFlyingStructure(underground: false),
      PathQueueStructure(underground: false),
    };
    using var stream = BuildDat(structures, [
      new EntrySpec(0, 1, writer => WriteTerrain(writer, 2, 2)),
      new EntrySpec(1, 11, writer => WriteGroundPath(
        writer, 0, 250, 7, 1, 0x0102030405060708ul, 9, 255)),
      new EntrySpec(2, 12, writer => WriteGroundPath(
        writer, 1, 5, 6, 0, 0x1112131415161718ul, 10, 128)),
      new EntrySpec(3, 13, writer => WriteFlyingPath(
        writer,
        -1_234,
        1,
        2,
        3,
        5_678,
        0,
        0x2122232425262728ul,
        240,
        0x3132333435363738ul,
        11,
        undergroundFlag: null,
        boolValue: 127)),
      new EntrySpec(4, 14, writer => WriteQueuePath(
        writer,
        int.MinValue,
        0,
        4,
        5,
        0x4142434445464748ul,
        new(int.MinValue, 0, int.MaxValue),
        6,
        int.MaxValue,
        0x5152535455565758ul,
        1,
        0x6162636465666768ul,
        251,
        7,
        0x7172737475767778ul,
        12,
        undergroundFlag: null,
        boolValue: 126)),
    ]);

    var terrain = DatTerrainReader.Read(stream);

    var tile = (DatPathTileData)terrain.Paths[0];
    var ground = (DatPathGroundData)terrain.Paths[1];
    var flying = (DatPathFlyingData)terrain.Paths[2];
    var queue = (DatPathQueueData)terrain.Paths[3];
    using (Assert.EnterMultipleScope()) {
      Assert.That(terrain.Paths, Has.Count.EqualTo(4));
      Assert.That(tile.EntryId, Is.EqualTo(11));
      Assert.That(tile.StructureKind, Is.EqualTo(DatPathStructureKind.PathTile));
      Assert.That(tile.ColIndex, Is.Zero);
      Assert.That(tile.Direction, Is.EqualTo(250));
      Assert.That(tile.PathType, Is.EqualTo(7));
      Assert.That(tile.RowIndex, Is.EqualTo(1));
      Assert.That(tile.Surface, Is.EqualTo(0x0102030405060708ul));
      Assert.That(tile.SurfaceType, Is.EqualTo(9));
      Assert.That(tile.BoolValue, Is.EqualTo(255));

      Assert.That(ground.EntryId, Is.EqualTo(12));
      Assert.That(ground.StructureKind, Is.EqualTo(DatPathStructureKind.PathGround));
      Assert.That(ground.Surface, Is.EqualTo(0x1112131415161718ul));

      Assert.That(flying.EntryId, Is.EqualTo(13));
      Assert.That(flying.StructureKind, Is.EqualTo(DatPathStructureKind.PathFlying));
      Assert.That(flying.BaseHeight, Is.EqualTo(-1_234));
      Assert.That(flying.QuantisedHeight, Is.EqualTo(5_678));
      Assert.That(flying.SceneryItem, Is.EqualTo(0x2122232425262728ul));
      Assert.That(flying.SlopeType, Is.EqualTo(240));
      Assert.That(flying.Surface, Is.EqualTo(0x3132333435363738ul));
      Assert.That(flying.UndergroundFlag, Is.Null);
      Assert.That(flying.BoolValue, Is.EqualTo(127));

      Assert.That(queue.EntryId, Is.EqualTo(14));
      Assert.That(queue.StructureKind, Is.EqualTo(DatPathStructureKind.PathQueue));
      Assert.That(queue.BaseHeight, Is.EqualTo(int.MinValue));
      Assert.That(queue.EndDirection, Is.EqualTo(5));
      Assert.That(queue.FenceEntry, Is.EqualTo(0x4142434445464748ul));
      Assert.That(queue.FenceFlexiColours,
        Is.EqualTo(new DatFenceFlexiColours(int.MinValue, 0, int.MaxValue)));
      Assert.That(queue.QuantisedHeight, Is.EqualTo(int.MaxValue));
      Assert.That(queue.QueueLine, Is.EqualTo(0x5152535455565758ul));
      Assert.That(queue.SceneryItem, Is.EqualTo(0x6162636465666768ul));
      Assert.That(queue.SlopeType, Is.EqualTo(251));
      Assert.That(queue.StartDirection, Is.EqualTo(7));
      Assert.That(queue.Surface, Is.EqualTo(0x7172737475767778ul));
      Assert.That(queue.UndergroundFlag, Is.Null);
      Assert.That(queue.BoolValue, Is.EqualTo(126));
      Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }
  }

  [Test]
  public void Read_ProvenUndergroundVariants_CaptureTheirBooleanFlags() {
    using var stream = BuildDat(
      [
        LandscapeStructure(),
        PathFlyingStructure(underground: true),
        PathQueueStructure(underground: true),
      ],
      [
        new EntrySpec(0, 1, writer => WriteTerrain(writer, 1, 1)),
        new EntrySpec(1, 21, writer => WriteFlyingPath(
          writer, 1, 0, 2, 3, 4, 0, 5, 6, 7, 8, true, 9)),
        new EntrySpec(2, 22, writer => WriteQueuePath(
          writer, 1, 0, 2, 3, 4, new(5, 6, 7), 8, 9, 10, 0, 11, 12, 13,
          14, 15, false, 16)),
      ]);

    var terrain = DatTerrainReader.Read(stream);

    using (Assert.EnterMultipleScope()) {
      Assert.That(((DatPathFlyingData)terrain.Paths[0]).UndergroundFlag, Is.True);
      Assert.That(((DatPathQueueData)terrain.Paths[1]).UndergroundFlag, Is.False);
    }
  }

  [Test]
  public void Read_DuplicatePathCoordinates_RemainRepresentableInFileOrder() {
    using var stream = BuildDat(
      [LandscapeStructure(), PathTileStructure()],
      [
        new EntrySpec(0, 1, writer => WriteTerrain(writer, 1, 1)),
        new EntrySpec(1, 31, writer =>
          WriteGroundPath(writer, 0, 1, 2, 0, 3, 4, 5)),
        new EntrySpec(1, 32, writer =>
          WriteGroundPath(writer, 0, 6, 7, 0, 8, 9, 10)),
      ]);

    var terrain = DatTerrainReader.Read(stream);

    using (Assert.EnterMultipleScope()) {
      Assert.That(terrain.Paths, Has.Count.EqualTo(2));
      Assert.That(terrain.Paths.Select(path => path.EntryId),
        Is.EqualTo(new ulong[] { 31, 32 }));
      Assert.That(terrain.Paths.Select(path => (path.ColIndex, path.RowIndex)),
        Is.EqualTo(new[] {
          (Convert.ToByte(0), Convert.ToByte(0)),
          (Convert.ToByte(0), Convert.ToByte(0)),
        }));
    }
  }

  [Test]
  public void Read_SelectedPathWithMismatchedLeafSchema_ThrowsInvalidDataException() {
    var fields = GroundPathFields();
    fields[4] = new("Surface", "reference", 4);

    AssertInvalidSchema(new("PathTile", fields));
  }

  [Test]
  public void Read_PathQueueWithMismatchedNestedSchema_ThrowsInvalidDataException() {
    var fields = QueuePathFields(underground: false);
    fields[5] = new(
      "FenceFlexiColours",
      "struct",
      12,
      [
        new("COL0", "int32", 4),
        new("COL1", "int32", 4),
        new("Colour2", "int32", 4),
      ]);

    AssertInvalidSchema(new("PathQueue", fields));
  }

  [Test]
  public void Read_UnprovenPathSchemaVariant_ThrowsInvalidDataException() {
    var fields = GroundPathFields().ToList();
    fields.Insert(fields.Count - 1, new("UndergroundFlag", "bool", 1));

    AssertInvalidSchema(new("PathGround", fields));
  }

  [Test]
  public void Read_TruncatedPathEntry_ThrowsInvalidDataException() {
    using var complete = BuildDat(
      [LandscapeStructure(), PathFlyingStructure(underground: false)],
      [
        new EntrySpec(0, 1, writer => WriteTerrain(writer, 1, 1)),
        new EntrySpec(1, 41, writer => WriteFlyingPath(
          writer, 1, 0, 2, 3, 4, 0, 5, 6, 7, 8, null, 9)),
      ]);
    var bytes = complete.ToArray();
    Array.Resize(ref bytes, bytes.Length - 1);
    using var truncated = new MemoryStream(bytes);

    Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(truncated)));
  }

  [Test]
  public void Read_NonTargetOrdinaryEntry_IsConsumedBeforePathCapture() {
    var decoration = new StructureSpec(
      "Decoration",
      [
        new("Marker", "uint32", 4),
        new("Target", "reference", 8),
        new("Nested", "struct", 4, [new("Value", "int32", 4)]),
        new("Label", "string"),
      ]);
    using var stream = BuildDat(
      [LandscapeStructure(), decoration, PathTileStructure()],
      [
        new EntrySpec(0, 1, writer => WriteTerrain(writer, 1, 1)),
        new EntrySpec(1, 51, writer => {
          writer.Write(0x01020304u);
          writer.Write(0x1112131415161718ul);
          writer.Write(-123);
          writer.Write(3u);
          writer.Write(Encoding.ASCII.GetBytes("abc"));
        }),
        new EntrySpec(2, 52, writer =>
          WriteGroundPath(writer, 0, 1, 2, 0, 3, 4, 5)),
      ]);

    var terrain = DatTerrainReader.Read(stream);

    using (Assert.EnterMultipleScope()) {
      Assert.That(terrain.Paths.Select(path => path.EntryId),
        Is.EqualTo(new ulong[] { 52 }));
      Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }
  }

  [Test]
  public void Read_PathCoordinateOutsideTerrain_ThrowsInvalidDataException() {
    using var stream = BuildDat(
      [LandscapeStructure(), PathTileStructure()],
      [
        new EntrySpec(0, 1, writer => WriteTerrain(writer, 1, 1)),
        new EntrySpec(1, 61, writer =>
          WriteGroundPath(writer, 1, 2, 3, 0, 4, 5, 6)),
      ]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_InvalidUndergroundBool_ThrowsInvalidDataException() {
    using var stream = BuildDat(
      [LandscapeStructure(), PathFlyingStructure(underground: true)],
      [
        new EntrySpec(0, 1, writer => WriteTerrain(writer, 1, 1)),
        new EntrySpec(1, 71, writer => WriteFlyingPath(
          writer, 1, 0, 2, 3, 4, 0, 5, 6, 7, 8, null, 9, rawFlag: 2)),
      ]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));
  }

  private static StructureSpec LandscapeStructure() =>
    new("Landscape", [new("EngineTerrain", "GE_Terrain")]);

  private static StructureSpec PathTileStructure() =>
    new("PathTile", GroundPathFields());

  private static StructureSpec PathGroundStructure() =>
    new("PathGround", GroundPathFields());

  private static StructureSpec PathFlyingStructure(bool underground) =>
    new("PathFlying", FlyingPathFields(underground));

  private static StructureSpec PathQueueStructure(bool underground) =>
    new("PathQueue", QueuePathFields(underground));

  private static FieldSpec[] GroundPathFields() => [
    new("ColIndex", "uint8", 1),
    new("Direction", "uint8", 1),
    new("PathType", "uint8", 1),
    new("RowIndex", "uint8", 1),
    new("Surface", "reference", 8),
    new("SurfaceType", "uint8", 1),
    new("bool", "uint8", 1),
  ];

  private static FieldSpec[] FlyingPathFields(bool underground) {
    var fields = new List<FieldSpec> {
      new("BaseHeight", "int32", 4),
      new("ColIndex", "uint8", 1),
      new("Direction", "uint8", 1),
      new("PathType", "uint8", 1),
      new("QuantisedHeight", "int32", 4),
      new("RowIndex", "uint8", 1),
      new("SceneryItem", "reference", 8),
      new("SlopeType", "uint8", 1),
      new("Surface", "reference", 8),
      new("SurfaceType", "uint8", 1),
    };
    if (underground) fields.Add(new("UndergroundFlag", "bool", 1));
    fields.Add(new("bool", "uint8", 1));
    return [.. fields];
  }

  private static FieldSpec[] QueuePathFields(bool underground) {
    var fields = new List<FieldSpec> {
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
    };
    if (underground) fields.Add(new("UndergroundFlag", "bool", 1));
    fields.Add(new("bool", "uint8", 1));
    return [.. fields];
  }

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
        foreach (var field in structure.Fields)
          WriteFieldDefinition(writer, field);
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
    using (var payloadWriter = new BinaryWriter(
      payload,
      Encoding.ASCII,
      leaveOpen: true
    )) {
      payloadWriter.Write(Convert.ToByte(width));
      payloadWriter.Write(Convert.ToByte(height));
      payloadWriter.Write(-16f);
      payloadWriter.Write(-20f);
      payloadWriter.Write(4f);
      payloadWriter.Write(4f);
      for (var index = 0; index < checked(width * height); index++) {
        payloadWriter.Write(1f);
        payloadWriter.Write(2f);
        payloadWriter.Write(3f);
        payloadWriter.Write(4f);
        payloadWriter.Write(Convert.ToByte(5));
        payloadWriter.Write(Convert.ToByte(6));
        for (var padding = 18; padding < 24; padding++)
          payloadWriter.Write(Convert.ToByte(padding));
      }
    }
    writer.Write(Convert.ToUInt32(payload.Length));
    writer.Write(payload.ToArray());
  }

  private static void WriteGroundPath(
    BinaryWriter writer,
    byte colIndex,
    byte direction,
    byte pathType,
    byte rowIndex,
    ulong surface,
    byte surfaceType,
    byte boolValue
  ) {
    writer.Write(colIndex);
    writer.Write(direction);
    writer.Write(pathType);
    writer.Write(rowIndex);
    writer.Write(surface);
    writer.Write(surfaceType);
    writer.Write(boolValue);
  }

  private static void WriteFlyingPath(
    BinaryWriter writer,
    int baseHeight,
    byte colIndex,
    byte direction,
    byte pathType,
    int quantisedHeight,
    byte rowIndex,
    ulong sceneryItem,
    byte slopeType,
    ulong surface,
    byte surfaceType,
    bool? undergroundFlag,
    byte boolValue,
    byte? rawFlag = null
  ) {
    writer.Write(baseHeight);
    writer.Write(colIndex);
    writer.Write(direction);
    writer.Write(pathType);
    writer.Write(quantisedHeight);
    writer.Write(rowIndex);
    writer.Write(sceneryItem);
    writer.Write(slopeType);
    writer.Write(surface);
    writer.Write(surfaceType);
    if (undergroundFlag.HasValue || rawFlag.HasValue)
      writer.Write(rawFlag ?? Convert.ToByte(undergroundFlag!.Value));
    writer.Write(boolValue);
  }

  private static void WriteQueuePath(
    BinaryWriter writer,
    int baseHeight,
    byte colIndex,
    byte direction,
    byte endDirection,
    ulong fenceEntry,
    DatFenceFlexiColours fenceFlexiColours,
    byte pathType,
    int quantisedHeight,
    ulong queueLine,
    byte rowIndex,
    ulong sceneryItem,
    byte slopeType,
    byte startDirection,
    ulong surface,
    byte surfaceType,
    bool? undergroundFlag,
    byte boolValue
  ) {
    writer.Write(baseHeight);
    writer.Write(colIndex);
    writer.Write(direction);
    writer.Write(endDirection);
    writer.Write(fenceEntry);
    writer.Write(fenceFlexiColours.Col0);
    writer.Write(fenceFlexiColours.Col1);
    writer.Write(fenceFlexiColours.Col2);
    writer.Write(pathType);
    writer.Write(quantisedHeight);
    writer.Write(queueLine);
    writer.Write(rowIndex);
    writer.Write(sceneryItem);
    writer.Write(slopeType);
    writer.Write(startDirection);
    writer.Write(surface);
    writer.Write(surfaceType);
    if (undergroundFlag.HasValue)
      writer.Write(Convert.ToByte(undergroundFlag.Value));
    writer.Write(boolValue);
  }

  private static void AssertInvalidSchema(StructureSpec invalidStructure) {
    using var stream = BuildDat(
      [LandscapeStructure(), invalidStructure],
      [new EntrySpec(0, 1, writer => WriteTerrain(writer, 1, 1))]);

    Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));
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
