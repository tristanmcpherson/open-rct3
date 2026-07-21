// DAT Game Time Reader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Text;

namespace OpenRCT3.Tests.Serialization;

[TestFixture]
public class DatGameTimeReaderTests {
  [Test]
  public void Read_GameTimeCapturesExactSavedClockState() {
    using var stream = BuildDat([
      new GameTimeEntry(17, 2, 0.25f, 0.5f, 1234.5f, 1000f),
    ]);

    var gameTime = DatTerrainReader.Read(stream).GameTime;

    Assert.That(gameTime, Is.Not.Null);
    using (Assert.EnterMultipleScope()) {
      Assert.That(gameTime!.EntryId, Is.EqualTo(17));
      Assert.That(gameTime.DayNightMode, Is.EqualTo(2));
      Assert.That(gameTime.DayNightTime, Is.EqualTo(0.25f));
      Assert.That(gameTime.DayNightTimeAsRendered, Is.EqualTo(0.5f));
      Assert.That(gameTime.Time, Is.EqualTo(1234.5f));
      Assert.That(gameTime.ZeroTime, Is.EqualTo(1000f));
    }
  }

  [Test]
  public void Read_GameTimeNonFiniteClockValueFailsClosed() {
    using var stream = BuildDat([
      new GameTimeEntry(17, 2, float.NaN, 0.5f, 1234.5f, 1000f),
    ]);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_DuplicateGameTimeEntryFailsClosed() {
    using var stream = BuildDat([
      new GameTimeEntry(17, 2, 0.25f, 0.5f, 1234.5f, 1000f),
      new GameTimeEntry(18, 1, 0.75f, 0.75f, 1500f, 1000f),
    ]);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  private static MemoryStream BuildDat(IReadOnlyList<GameTimeEntry> gameTimes) {
    var terrainPayload = BuildTerrainPayload();
    var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(2u);
      WriteStructure(writer, "Landscape", [
        new FieldSpec("EngineTerrain", "GE_Terrain", 0),
      ]);
      WriteStructure(writer, "GameTime", [
        new FieldSpec("DayNightMode", "int32", 4),
        new FieldSpec("DayNightTime", "float32", 4),
        new FieldSpec("DayNightTimeAsRendered", "float32", 4),
        new FieldSpec("Time", "float32", 4),
        new FieldSpec("ZeroTime", "float32", 4),
      ]);
      writer.Write(Convert.ToUInt32(gameTimes.Count + 1));

      writer.Write(0u);
      writer.Write(1ul);
      writer.Write(Convert.ToUInt32(terrainPayload.Length));
      writer.Write(terrainPayload);

      foreach (var gameTime in gameTimes) {
        writer.Write(1u);
        writer.Write(gameTime.EntryId);
        writer.Write(gameTime.DayNightMode);
        writer.Write(gameTime.DayNightTime);
        writer.Write(gameTime.DayNightTimeAsRendered);
        writer.Write(gameTime.Time);
        writer.Write(gameTime.ZeroTime);
      }
    }
    stream.Position = 0;
    return stream;
  }

  private static byte[] BuildTerrainPayload() {
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(Convert.ToByte(1));
      writer.Write(Convert.ToByte(1));
      writer.Write(0f);
      writer.Write(0f);
      writer.Write(4f);
      writer.Write(4f);
      writer.Write(1f);
      writer.Write(2f);
      writer.Write(3f);
      writer.Write(4f);
      writer.Write(Convert.ToByte(0));
      writer.Write(Convert.ToByte(0));
      writer.Write(new byte[6]);
    }
    return stream.ToArray();
  }

  private static void WriteStructure(
    BinaryWriter writer,
    string name,
    IReadOnlyList<FieldSpec> fields
  ) {
    WriteAscii16(writer, name);
    writer.Write(Convert.ToUInt32(fields.Count));
    foreach (var field in fields) {
      WriteAscii16(writer, field.Name);
      WriteAscii16(writer, field.Kind);
      writer.Write(field.FixedSize);
      writer.Write(0u);
    }
  }

  private static void WriteAscii16(BinaryWriter writer, string value) {
    var bytes = Encoding.ASCII.GetBytes(value);
    writer.Write(Convert.ToUInt16(bytes.Length));
    writer.Write(bytes);
  }

  private sealed record FieldSpec(string Name, string Kind, uint FixedSize);

  private sealed record GameTimeEntry(
    ulong EntryId,
    int DayNightMode,
    float DayNightTime,
    float DayNightTimeAsRendered,
    float Time,
    float ZeroTime);
}
