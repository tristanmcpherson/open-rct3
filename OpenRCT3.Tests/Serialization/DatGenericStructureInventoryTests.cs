// DAT Generic Structure Inventory Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Text;

namespace OpenRCT3.Tests.Serialization;

[TestFixture]
public class DatGenericStructureInventoryTests {
  [Test]
  public void Read_GenericEntriesCaptureExactBoundedStructureEvidence() {
    using var stream = BuildInventoryDat();

    var data = DatTerrainReader.Read(stream);
    var inventory = data.GenericStructureInventory.Single(item => item.Name == "GuestManager");

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        data.GenericStructureInventory.Select(item => item.Name),
        Is.EqualTo(new[] { "Landscape", "GuestManager", "EmptyManager" }));
      Assert.That(inventory.StructureIndex, Is.EqualTo(1));
      Assert.That(inventory.Name, Is.EqualTo("GuestManager"));
      Assert.That(inventory.EntryCount, Is.EqualTo(2));
      Assert.That(inventory.EntryIds, Is.EqualTo(new ulong[] { 99, 42 }));
      Assert.That(inventory.Fields, Has.Count.EqualTo(2));
      Assert.That(inventory.Fields[0].Name, Is.EqualTo("Flags"));
      Assert.That(inventory.Fields[0].Kind, Is.EqualTo("uint32"));
      Assert.That(inventory.Fields[0].FixedSize, Is.EqualTo(4));
      Assert.That(inventory.Fields[1].Name, Is.EqualTo("Counters"));
      Assert.That(inventory.Fields[1].Kind, Is.EqualTo("struct"));
      Assert.That(inventory.Fields[1].FixedSize, Is.EqualTo(4));
      Assert.That(
        inventory.Fields[1].Children.Select(field => (field.Name, field.Kind)),
        Is.EqualTo(new[] { ("Guests", "uint16"), ("Staff", "uint16") }));
      Assert.That(
        data.GenericStructureInventory.Single(item => item.Name == "EmptyManager").EntryCount,
        Is.Zero);
    }
  }

  [Test]
  public void Read_GenericCollectionWithMalformedCountFailsClosed() {
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(1u);
      WriteStructure(writer, "GuestManager", [
        new FieldSpec(
          "Guests",
          "array",
          0,
          [new FieldSpec("Guest", "managedobjectptr", 8)]),
      ]);
      writer.Write(1u);
      writer.Write(0u);
      writer.Write(17ul);
      writer.Write(0u);
      writer.Write(uint.MaxValue);
    }
    stream.Position = 0;

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_FunValleyFixtureProducesBoundedManagerCandidateCensus() {
    var repositoryRoot = Path.GetFullPath(Path.Combine(
      TestContext.CurrentContext.TestDirectory,
      "..", "..", "..", ".."));
    var fixturePath = Path.Combine(
      repositoryRoot,
      "OpenCobra",
      "Tests",
      "Fixtures",
      "Parks",
      "Fun Valley Amusment park.dat");

    var inventory = DatTerrainReader.Read(fixturePath).GenericStructureInventory;

    using (Assert.EnterMultipleScope()) {
      Assert.That(inventory, Is.Not.Empty);
      Assert.That(inventory.Select(item => item.StructureIndex), Is.Ordered.Ascending);
      Assert.That(inventory.Select(item => item.StructureIndex), Is.Unique);
      Assert.That(inventory.Sum(item => item.EntryCount), Is.GreaterThan(0));
      Assert.That(inventory.All(item => item.EntryCount == item.EntryIds.Count), Is.True);
      Assert.That(Census(inventory), Has.All.Not.Empty);
    }

    foreach (var candidate in inventory
      .OrderByDescending(item => item.EntryCount)
      .ThenBy(item => item.StructureIndex)
      .Take(12))
      TestContext.WriteLine(
        $"[{candidate.StructureIndex}] {candidate.Name}: {candidate.EntryCount} entries");
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Read_RaidersProducesStableManagerCandidateCensus() {
    var installPath = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(string.IsNullOrWhiteSpace(installPath), Is.False);
    var fixturePath = Path.Combine(
      installPath!,
      "Campaigns",
      "Base",
      "Wild",
      "RaidersOfTheLostCoaster.dat");
    Assert.That(File.Exists(fixturePath), Is.True, fixturePath);

    var inventory = DatTerrainReader.Read(fixturePath).GenericStructureInventory;
    Assert.That(inventory, Is.Not.Empty);

    foreach (var candidate in inventory
      .OrderByDescending(item => item.EntryCount)
      .ThenBy(item => item.StructureIndex))
      TestContext.WriteLine(
        $"[{candidate.StructureIndex}] {candidate.Name}: {candidate.EntryCount} entries, " +
        $"IDs {string.Join(",", candidate.EntryIds.Take(8))}");
  }

  private static string[] Census(
    IReadOnlyList<DatGenericStructureInventoryData> inventory
  ) => inventory
    .Select(item =>
      $"{item.StructureIndex}|{item.Name}|{item.EntryCount}|" +
      $"{string.Join(",", item.EntryIds)}|{FieldSignature(item.Fields)}")
    .ToArray();

  private static string FieldSignature(
    IReadOnlyList<DatGenericStructureFieldData> fields
  ) => string.Join(",", fields.Select(field =>
    $"{field.Name}:{field.Kind}:{field.FixedSize}[{FieldSignature(field.Children)}]"));

  private static MemoryStream BuildInventoryDat() {
    var terrainPayload = BuildTerrainPayload();
    var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(3u);
      WriteStructure(writer, "Landscape", [
        new FieldSpec("EngineTerrain", "GE_Terrain", 0),
      ]);
      WriteStructure(writer, "GuestManager", [
        new FieldSpec("Flags", "uint32", 4),
        new FieldSpec("Counters", "struct", 4, [
          new FieldSpec("Guests", "uint16", 2),
          new FieldSpec("Staff", "uint16", 2),
        ]),
      ]);
      WriteStructure(writer, "EmptyManager", [
        new FieldSpec("Enabled", "bool", 1),
      ]);
      writer.Write(3u);

      writer.Write(1u);
      writer.Write(99ul);
      writer.Write(7u);
      writer.Write(Convert.ToUInt16(100));
      writer.Write(Convert.ToUInt16(20));

      writer.Write(0u);
      writer.Write(1ul);
      writer.Write(Convert.ToUInt32(terrainPayload.Length));
      writer.Write(terrainPayload);

      writer.Write(1u);
      writer.Write(42ul);
      writer.Write(9u);
      writer.Write(Convert.ToUInt16(120));
      writer.Write(Convert.ToUInt16(25));
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
    foreach (var field in fields) WriteField(writer, field);
  }

  private static void WriteField(BinaryWriter writer, FieldSpec field) {
    WriteAscii16(writer, field.Name);
    WriteAscii16(writer, field.Kind);
    writer.Write(field.FixedSize);
    writer.Write(Convert.ToUInt32(field.Children.Count));
    foreach (var child in field.Children) WriteField(writer, child);
  }

  private static void WriteAscii16(BinaryWriter writer, string value) {
    var bytes = Encoding.ASCII.GetBytes(value);
    writer.Write(Convert.ToUInt16(bytes.Length));
    writer.Write(bytes);
  }

  private sealed record FieldSpec(
    string Name,
    string Kind,
    uint FixedSize,
    IReadOnlyList<FieldSpec>? ChildFields = null
  ) {
    public IReadOnlyList<FieldSpec> Children { get; } =
      ChildFields ?? Array.Empty<FieldSpec>();
  }
}
