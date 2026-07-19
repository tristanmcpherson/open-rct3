// DatTerrainReaderTests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Text;

namespace OpenRCT3.Tests.Serialization;

[TestFixture]
public class DatTerrainReaderTests {
  private const int TerrainRecord20Bytes = 20;
  private const int TerrainRecord24Bytes = 24;

  [Test]
  public void Read_24ByteTerrain_DecodesMetadataAndRowMajorCells() {
    var cells = new[] {
      new CellSpec(-4.25f, 2.5f, 3.75f, 4.5f, 1, 2),
      new CellSpec(5.25f, 6.5f, 7.75f, 8.5f, 3, 4),
      new CellSpec(9.25f, 10.5f, 11.75f, 12.5f, 5, 6),
      new CellSpec(13.25f, 14.5f, 15.75f, 16.5f, 7, 8),
    };
    var payload = BuildTerrainPayload(
      width: 2,
      height: 2,
      TerrainRecord24Bytes,
      cells,
      originX: -128f,
      originY: 64f,
      tileSizeX: 4f,
      tileSizeY: 5f);
    using var stream = BuildDat(
      [TargetTerrainField()],
      writer => WriteDynamicPayload(writer, payload));

    var terrain = DatTerrainReader.Read(stream);

    Assert.Multiple(new Action(() => {
      Assert.That(terrain.Width, Is.EqualTo(2));
      Assert.That(terrain.Height, Is.EqualTo(2));
      Assert.That(terrain.OriginX, Is.EqualTo(-128f));
      Assert.That(terrain.OriginY, Is.EqualTo(64f));
      Assert.That(terrain.TileSizeX, Is.EqualTo(4f));
      Assert.That(terrain.TileSizeY, Is.EqualTo(5f));
      Assert.That(terrain.Cells, Has.Count.EqualTo(4));
      Assert.That(terrain.Cells[0].SouthWestHeight, Is.EqualTo(-4.25f));
      Assert.That(terrain.Cells[1].SouthWestHeight, Is.EqualTo(5.25f));
      Assert.That(terrain.Cells[2].SouthWestHeight, Is.EqualTo(9.25f));
      Assert.That(terrain.Cells[3].NorthEastHeight, Is.EqualTo(16.5f));
      Assert.That(terrain.Cells[3].SurfaceIndex, Is.EqualTo(7));
      Assert.That(terrain.Cells[3].CliffIndex, Is.EqualTo(8));
    }));
  }

  [Test]
  public void Read_20ByteTerrainWithTail_DecodesCell() {
    var payload = BuildTerrainPayload(
      width: 1,
      height: 1,
      TerrainRecord20Bytes,
      [new CellSpec(-9f, -8f, -7f, -6f, 11, 12)],
      includeTail: true);
    using var stream = BuildDat(
      [TargetTerrainField()],
      writer => WriteDynamicPayload(writer, payload));

    var terrain = DatTerrainReader.Read(stream);

    Assert.Multiple(new Action(() => {
      Assert.That(terrain.Cells[0].SouthWestHeight, Is.EqualTo(-9f));
      Assert.That(terrain.Cells[0].NorthEastHeight, Is.EqualTo(-6f));
      Assert.That(terrain.Cells[0].SurfaceIndex, Is.EqualTo(11));
      Assert.That(terrain.Cells[0].CliffIndex, Is.EqualTo(12));
    }));
  }

  [Test]
  public void Read_TwoCellAmbiguousLayout_ThrowsInvalidDataException() {
    var payload = BuildTerrainPayload(
      width: 2,
      height: 1,
      TerrainRecord20Bytes,
      [
        new CellSpec(1f, 2f, 3f, 4f, 5, 6),
        new CellSpec(7f, 8f, 9f, 10f, 11, 12),
      ],
      includeTail: true);
    using var stream = BuildDat(
      [TargetTerrainField()],
      writer => WriteDynamicPayload(writer, payload));

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_FixedSizeTerrain_DoesNotReadSizePrefix() {
    var payload = BuildTerrainPayload(
      width: 1,
      height: 1,
      TerrainRecord24Bytes,
      [new CellSpec(1f, 2f, 3f, 4f, 5, 6)]);
    using var stream = BuildDat(
      [new FieldSpec("EngineTerrain", "GE_Terrain", Convert.ToUInt32(payload.Length))],
      writer => writer.Write(payload));

    var terrain = DatTerrainReader.Read(stream);

    Assert.That(terrain.Cells[0].SouthEastHeight, Is.EqualTo(2f));
  }

  [Test]
  public void Read_NonTargetTerrainField_SkipsToEngineTerrain() {
    var ignoredPayload = BuildTerrainPayload(
      width: 1,
      height: 1,
      TerrainRecord24Bytes,
      [new CellSpec(100f, 200f, 300f, 400f, 1, 2)]);
    var targetPayload = BuildTerrainPayload(
      width: 1,
      height: 1,
      TerrainRecord24Bytes,
      [new CellSpec(10f, 20f, 30f, 40f, 3, 4)]);
    using var stream = BuildDat(
      [new FieldSpec("PreviewTerrain", "GE_Terrain"), TargetTerrainField()],
      writer => {
        WriteDynamicPayload(writer, ignoredPayload);
        WriteDynamicPayload(writer, targetPayload);
      });

    var terrain = DatTerrainReader.Read(stream);

    Assert.That(terrain.Cells[0].SouthWestHeight, Is.EqualTo(10f));
  }

  [TestCase(0x1A)]
  [TestCase(0x2A)]
  public void Read_ExtendedHeader_UsesVersionDefinitionOffset(int version) {
    var payload = BuildTerrainPayload(
      width: 1,
      height: 1,
      TerrainRecord24Bytes,
      [new CellSpec(1f, 2f, 3f, 4f, 5, 6)]);
    using var stream = BuildDat(
      [TargetTerrainField()],
      writer => WriteDynamicPayload(writer, payload),
      extendedVersion: Convert.ToByte(version));

    var terrain = DatTerrainReader.Read(stream);

    Assert.That(terrain.Cells[0].NorthEastHeight, Is.EqualTo(4f));
  }

  [Test]
  public void Read_CollectionSizeMetadata_DoesNotOverrideElementSchema() {
    var payload = BuildTerrainPayload(
      width: 1,
      height: 1,
      TerrainRecord24Bytes,
      [new CellSpec(10f, 20f, 30f, 40f, 2, 3)]);
    var arrayField = new FieldSpec(
      "Items",
      "array",
      children: [new FieldSpec("Value", "uint16")]);
    using var stream = BuildDat(
      [new FieldSpec("Enabled", "bool"), arrayField, TargetTerrainField()],
      writer => {
        writer.Write(true);
        writer.Write(1_234u);
        writer.Write(2u);
        writer.Write(Convert.ToUInt16(100));
        writer.Write(Convert.ToUInt16(200));
        WriteDynamicPayload(writer, payload);
      });

    var terrain = DatTerrainReader.Read(stream);

    Assert.That(terrain.Cells[0].NorthWestHeight, Is.EqualTo(30f));
  }

  [Test]
  public void Read_TruncatedTerrain_ThrowsInvalidDataException() {
    var bytes = BuildValidDatBytes();
    Array.Resize(ref bytes, bytes.Length - 1);

    AssertInvalid(bytes);
  }

  [Test]
  public void Read_InvalidStructureIndex_ThrowsInvalidDataException() {
    using var stream = BuildDat(
      [TargetTerrainField()],
      _ => { },
      structureIndex: 1);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_ExcessiveStructureCount_ThrowsInvalidDataException() {
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
      writer.Write(uint.MaxValue);
    stream.Position = 0;

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_ExcessiveEntryCount_ThrowsInvalidDataException() {
    using var stream = BuildDat(
      [TargetTerrainField()],
      _ => { },
      entryCount: uint.MaxValue);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_InvalidTerrainSize_ThrowsInvalidDataException() {
    var payload = BuildTerrainPayload(
      width: 1,
      height: 1,
      TerrainRecord24Bytes,
      [new CellSpec(1f, 2f, 3f, 4f, 5, 6)]);
    using var stream = BuildDat(
      [TargetTerrainField()],
      writer => {
        writer.Write(Convert.ToUInt32(payload.Length - 1));
        writer.Write(payload);
      });

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_ExcessiveCustomPayloadSize_ThrowsInvalidDataException() {
    using var stream = BuildDat(
      [TargetTerrainField()],
      writer => writer.Write(uint.MaxValue));

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_TruncatedCustomPayload_ThrowsInvalidDataException() {
    using var stream = BuildDat(
      [new FieldSpec("Trees", "SkirtTrees"), TargetTerrainField()],
      writer => {
        writer.Write(4u);
        writer.Write(Convert.ToUInt16(0));
      });

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_NonFiniteHeight_ThrowsInvalidDataException() {
    var payload = BuildTerrainPayload(
      width: 1,
      height: 1,
      TerrainRecord24Bytes,
      [new CellSpec(float.NaN, 2f, 3f, 4f, 5, 6)]);
    using var stream = BuildDat(
      [TargetTerrainField()],
      writer => WriteDynamicPayload(writer, payload));

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_NonFiniteMetadata_ThrowsInvalidDataException() {
    var payload = BuildTerrainPayload(
      width: 1,
      height: 1,
      TerrainRecord24Bytes,
      [new CellSpec(1f, 2f, 3f, 4f, 5, 6)],
      tileSizeX: float.PositiveInfinity);
    using var stream = BuildDat(
      [TargetTerrainField()],
      writer => WriteDynamicPayload(writer, payload));

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_ExcessiveCollectionLength_ThrowsInvalidDataException() {
    var arrayField = new FieldSpec(
      "Items",
      "array",
      children: [new FieldSpec("Value", "uint8")]);
    using var stream = BuildDat(
      [arrayField, TargetTerrainField()],
      writer => {
        writer.Write(0u);
        writer.Write(uint.MaxValue);
      });

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_UnsupportedKind_ThrowsInvalidDataException() {
    using var stream = BuildDat(
      [new FieldSpec("Mystery", "unsupported")],
      _ => { });

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_UnsupportedExtendedHeaderVersion_ThrowsInvalidDataException() {
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(0u);
      writer.Write(0u);
      writer.Write(Convert.ToByte(0x3A));
    }
    stream.Position = 0;

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  private static byte[] BuildValidDatBytes() {
    var payload = BuildTerrainPayload(
      width: 1,
      height: 1,
      TerrainRecord24Bytes,
      [new CellSpec(1f, 2f, 3f, 4f, 5, 6)]);
    using var stream = BuildDat(
      [TargetTerrainField()],
      writer => WriteDynamicPayload(writer, payload));
    return stream.ToArray();
  }

  private static FieldSpec TargetTerrainField()
    => new("EngineTerrain", "GE_Terrain");

  private static MemoryStream BuildDat(
    IReadOnlyList<FieldSpec> fields,
    Action<BinaryWriter> writeValues,
    uint structureIndex = 0,
    uint entryCount = 1,
    byte? extendedVersion = null) {
    var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      if (extendedVersion.HasValue) {
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(extendedVersion.Value);
        var definitionOffset = extendedVersion.Value == 0x1A ? 0x40 : 0x50;
        while (stream.Position < definitionOffset) writer.Write(Convert.ToByte(0));
      }

      writer.Write(1u);
      WriteAscii16(writer, "Landscape");
      writer.Write(Convert.ToUInt32(fields.Count));
      foreach (var field in fields) WriteFieldDefinition(writer, field);
      writer.Write(entryCount);
      if (entryCount == 1) {
        writer.Write(structureIndex);
        writer.Write(1ul);
        writeValues(writer);
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

  private static void WriteDynamicPayload(BinaryWriter writer, byte[] payload) {
    writer.Write(Convert.ToUInt32(payload.Length));
    writer.Write(payload);
  }

  private static byte[] BuildTerrainPayload(
    int width,
    int height,
    int recordSize,
    IReadOnlyList<CellSpec> cells,
    bool includeTail = false,
    float originX = -16f,
    float originY = -20f,
    float tileSizeX = 4f,
    float tileSizeY = 4f) {
    if (cells.Count != checked(width * height))
      throw new ArgumentException("Test cells must match the requested dimensions.", nameof(cells));

    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(Convert.ToByte(width));
      writer.Write(Convert.ToByte(height));
      writer.Write(originX);
      writer.Write(originY);
      writer.Write(tileSizeX);
      writer.Write(tileSizeY);
      foreach (var cell in cells) {
        writer.Write(cell.SouthWest);
        writer.Write(cell.SouthEast);
        writer.Write(cell.NorthWest);
        writer.Write(cell.NorthEast);
        writer.Write(cell.SurfaceIndex);
        writer.Write(cell.CliffIndex);
        for (var padding = 18; padding < recordSize; padding++)
          writer.Write(Convert.ToByte(padding));
      }
      if (includeTail) writer.Write(0x0102030405060708ul);
    }
    return stream.ToArray();
  }

  private static void AssertInvalid(byte[] bytes) {
    using var stream = new MemoryStream(bytes);
    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  private sealed class FieldSpec {
    public string Name { get; }
    public string Kind { get; }
    public uint FixedSize { get; }
    public IReadOnlyList<FieldSpec> Children { get; }

    public FieldSpec(
      string name,
      string kind,
      uint fixedSize = 0,
      IReadOnlyList<FieldSpec>? children = null) {
      Name = name;
      Kind = kind;
      FixedSize = fixedSize;
      Children = children ?? Array.Empty<FieldSpec>();
    }
  }

  private readonly struct CellSpec {
    public float SouthWest { get; }
    public float SouthEast { get; }
    public float NorthWest { get; }
    public float NorthEast { get; }
    public byte SurfaceIndex { get; }
    public byte CliffIndex { get; }

    public CellSpec(
      float southWest,
      float southEast,
      float northWest,
      float northEast,
      byte surfaceIndex,
      byte cliffIndex) {
      SouthWest = southWest;
      SouthEast = southEast;
      NorthWest = northWest;
      NorthEast = northEast;
      SurfaceIndex = surfaceIndex;
      CliffIndex = cliffIndex;
    }
  }
}
