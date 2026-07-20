// DAT Tracked Ride Instance Reader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Text;

namespace OpenRCT3.Tests.Serialization;

[TestFixture]
public class DatTrackedRideInstanceReaderTests {
  [Test]
  public void Read_IdentityAndLinkageFieldsConsumeOpaqueFieldsInDeclaredOrder() {
    using var stream = BuildDat(TrackedRideInstanceStructure());

    var data = DatTerrainReader.Read(stream);

    var ride = data.TrackedRideInstances.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(stream.Position, Is.EqualTo(stream.Length));
      Assert.That(ride.EntryId, Is.EqualTo(900));
      Assert.That(ride.Name, Is.EqualTo("Synthetic coaster"));
      Assert.That(ride.Track, Is.EqualTo(700));
      Assert.That(ride.TrackedRideOverlayName, Is.EqualTo("rides\\synthetic"));
      Assert.That(ride.TrackedRideSymbolName, Is.EqualTo("Synthetic_Ride"));
      Assert.That(ride.NTrains, Is.EqualTo(2));
      Assert.That(ride.NCarsPerTrain, Is.EqualTo(4));
      Assert.That(ride.TrainSelection, Is.EqualTo(5));
      Assert.That(ride.Trains, Is.EqualTo(new ulong[] { 1_000, 1_001 }));
    }
  }

  [Test]
  public void Read_NameKindDriftFailsClosed() {
    var structure = TrackedRideInstanceStructure(
      nameField: new FieldSpec("Name", "uint32", 4));
    using var stream = BuildDat(structure);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_TrainsElementSchemaDriftFailsClosed() {
    var structure = TrackedRideInstanceStructure(
      trainsField: new FieldSpec(
        "Trains",
        "array",
        0,
        [new FieldSpec("Train", "reference", 8)]));
    using var stream = BuildDat(structure);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [TestCase("Campaigns/Base/BoxOffice.dat")]
  [TestCase("Campaigns/Base/Soaked/Atlantis.dat")]
  [TestCase("Campaigns/Base/Wild/GeminiBasin.dat")]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Read_InstalledCampaignCapturesRideInstanceLinkage(string relativePath) {
    var installPath = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installPath),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(installPath),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");

    var campaignPath = Path.Combine(
      installPath!,
      relativePath.Replace('/', Path.DirectorySeparatorChar));
    Assert.That(File.Exists(campaignPath), Is.True, campaignPath);

    var data = DatTerrainReader.Read(campaignPath);
    var instanceIds = data.TrackedRideInstances
      .Select(instance => instance.EntryId)
      .ToHashSet();
    var trackIds = data.RideTracks.Select(track => track.EntryId).ToHashSet();

    using (Assert.EnterMultipleScope()) {
      Assert.That(data.TrackedRideInstances, Is.Not.Empty, campaignPath);
      Assert.That(instanceIds, Has.Count.EqualTo(data.TrackedRideInstances.Count), campaignPath);
      foreach (var track in data.RideTracks) {
        if (track.TrackedRideInstance == 0) continue;
        Assert.That(
          instanceIds.Contains(track.TrackedRideInstance),
          Is.True,
          $"{campaignPath}: Track {track.EntryId} references {track.TrackedRideInstance}");
      }
      foreach (var instance in data.TrackedRideInstances) {
        if (instance.Track == 0) continue;
        Assert.That(
          trackIds.Contains(instance.Track),
          Is.True,
          $"{campaignPath}: TrackedRideInstance {instance.EntryId} references {instance.Track}");
      }
    }

    TestContext.WriteLine(
      $"{relativePath}: {data.TrackedRideInstances.Count} ride instances, " +
      $"{data.RideTracks.Count} tracks");
  }

  private static MemoryStream BuildDat(StructureSpec trackedRideInstanceStructure) {
    var structures = new[] { LandscapeStructure(), trackedRideInstanceStructure };
    var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(Convert.ToUInt32(structures.Length));
      foreach (var structure in structures)
        WriteStructure(writer, structure);
      writer.Write(2u);
      writer.Write(0u);
      writer.Write(1ul);
      WriteTerrain(writer);
      writer.Write(1u);
      writer.Write(900ul);
      WriteTrackedRideInstance(writer);
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

  private static void WriteTrackedRideInstance(BinaryWriter writer) {
    writer.Write(123u);
    writer.Write(700ul);
    WriteDatString(writer, "Synthetic coaster");
    writer.Write(2u);
    writer.Write(Convert.ToUInt16(17));
    writer.Write(2);
    WriteDatString(writer, "rides\\synthetic");
    writer.Write(16u);
    writer.Write(2u);
    writer.Write(1_000ul);
    writer.Write(1_001ul);
    writer.Write(4);
    writer.Write(2u);
    writer.Write(2u);
    writer.Write(false);
    writer.Write(true);
    WriteDatString(writer, "Synthetic_Ride");
    writer.Write(5);
  }

  private static StructureSpec LandscapeStructure() => new(
    "Landscape",
    [new FieldSpec("EngineTerrain", "GE_Terrain")]);

  private static StructureSpec TrackedRideInstanceStructure(
    FieldSpec? nameField = null,
    FieldSpec? trainsField = null
  ) => new(
    "TrackedRideInstance",
    [
      new FieldSpec("OpaqueCounter", "uint32", 4),
      new FieldSpec("Track", "managedobjectptr", 8),
      nameField ?? new FieldSpec("Name", "string"),
      new FieldSpec(
        "OpaqueStruct",
        "struct",
        0,
        [new FieldSpec("Value", "uint16", 2)]),
      new FieldSpec("NTrains", "int32", 4),
      new FieldSpec("TrackedRideOverlayName", "string"),
      trainsField ?? new FieldSpec(
        "Trains",
        "array",
        0,
        [new FieldSpec("Train", "managedobjectptr", 8)]),
      new FieldSpec("NCarsPerTrain", "int32", 4),
      new FieldSpec(
        "OpaqueList",
        "list",
        0,
        [new FieldSpec("Value", "bool", 1)]),
      new FieldSpec("TrackedRideSymbolName", "string"),
      new FieldSpec("TrainSelection", "int32", 4),
    ]);

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
