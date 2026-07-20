// DAT Track Reader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Text;

namespace OpenRCT3.Tests.Serialization;

[TestFixture]
public class DatTrackReaderTests {
  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Read_InstalledCampaignsAcrossExpansionsFullyParse() {
    var installPath = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installPath),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(installPath),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");

    var campaignPaths = new[] {
      Path.Combine(installPath!, "Campaigns", "Base", "BoxOffice.dat"),
      Path.Combine(installPath!, "Campaigns", "Base", "Soaked", "Atlantis.dat"),
      Path.Combine(installPath!, "Campaigns", "Base", "Wild", "GeminiBasin.dat"),
    };
    foreach (var campaignPath in campaignPaths) {
      Assert.That(File.Exists(campaignPath), Is.True, campaignPath);

      DatTerrainData data;
      try {
        data = DatTerrainReader.Read(campaignPath);
      } catch (Exception exception) {
        throw new InvalidDataException(
          $"Installed campaign '{campaignPath}' did not fully parse.",
          exception);
      }

      using (Assert.EnterMultipleScope()) {
        Assert.That(data.Width, Is.GreaterThan(0), campaignPath);
        Assert.That(data.Height, Is.GreaterThan(0), campaignPath);
      }
    }
  }

  [Test]
  public void Read_FunValleyFixtureSupportsExpansionTrackSchemas() {
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
    var data = DatTerrainReader.Read(fixturePath);

    using (Assert.EnterMultipleScope()) {
      Assert.That(data.Width, Is.GreaterThan(0));
      Assert.That(data.Height, Is.GreaterThan(0));
      Assert.That(data.TrackPieces, Is.Not.Empty);
      Assert.That(data.RideTracks, Is.Not.Empty);
      Assert.That(data.TrackSegments, Is.Not.Empty);
      Assert.That(data.RideTracks.All(track => track.IsCircuit == null), Is.True);
      Assert.That(data.RideTracks.All(track => track.TrackPieces == null), Is.True);
      Assert.That(data.RideTracks.All(track => track.FlippedTrackSections != null), Is.True);
      Assert.That(data.RideTracks.All(track => track.TunnelLightColour != null), Is.True);
    }
  }

  [Test]
  public void Read_InstalledTrackPieceSchemaCapturesSemanticPlacementFields() {
    using var stream = BuildDat(TrackPieceStructure());

    var data = DatTerrainReader.Read(stream);

    var track = data.TrackPieces.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(stream.Position, Is.EqualTo(stream.Length));
      Assert.That(track.EntryId, Is.EqualTo(500));
      Assert.That(track.FlexiColourField, Is.EqualTo(new DatSceneryFlexiColour(4, 5, 6)));
      Assert.That(track.Next, Is.EqualTo(501));
      Assert.That(track.Owner, Is.EqualTo(800));
      Assert.That(track.PlatformPiece, Is.EqualTo(800));
      Assert.That(track.Prev, Is.EqualTo(499));
      Assert.That(track.Reversed, Is.True);
      Assert.That(track.SidDatabaseEntry, Is.EqualTo(200));
      Assert.That(track.SymbolName, Is.EqualTo("Test_SID:tks"));
      Assert.That(track.SceneryItem, Is.EqualTo(100));
      Assert.That(
        track.SceneryItemDataField,
        Is.EqualTo(new DatSceneryItemDataField(3, 1, 6, null, 1, 1)));
      Assert.That(track.Segment, Is.EqualTo(800));
      Assert.That(track.UserAngleDegrees, Is.EqualTo(45));
    }
  }

  [Test]
  public void Read_SoakedTrackPieceSchemaCapturesSemanticPlacementFields() {
    using var stream = BuildDat(SoakedTrackPieceStructure(), soakedTrackPiece: true);

    var data = DatTerrainReader.Read(stream);

    var track = data.TrackPieces.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(stream.Position, Is.EqualTo(stream.Length));
      Assert.That(track.EntryId, Is.EqualTo(500));
      Assert.That(track.SymbolName, Is.EqualTo("Test_SID:tks"));
      Assert.That(track.SceneryItem, Is.EqualTo(100));
      Assert.That(track.Segment, Is.EqualTo(800));
      Assert.That(track.UserAngleDegrees, Is.EqualTo(45));
    }
  }

  [Test]
  public void Read_InstalledTrackAndSegmentSchemasCaptureSemanticTopology() {
    using var stream = BuildDat(TrackPieceStructure(), includeTopology: true);

    var data = DatTerrainReader.Read(stream);

    var track = data.RideTracks.Single();
    var segment = data.TrackSegments.Single();
    using (Assert.EnterMultipleScope()) {
      Assert.That(track.EntryId, Is.EqualTo(700));
      Assert.That(track.Direction, Is.EqualTo(2));
      Assert.That(track.FirstSegment, Is.EqualTo(800));
      Assert.That(track.LastSegment, Is.EqualTo(800));
      Assert.That(track.IsCircuit, Is.False);
      Assert.That(track.Prototype, Is.False);
      Assert.That(track.TrackPieces, Is.EqualTo(new ulong[] { 500 }));
      Assert.That(track.TrackFlexiColours, Is.EqualTo(new DatSceneryFlexiColour(4, 5, 6)));
      Assert.That(track.TrackedRideInstance, Is.EqualTo(900));
      Assert.That(segment.EntryId, Is.EqualTo(800));
      Assert.That(segment.Direction, Is.EqualTo(2));
      Assert.That(segment.FirstPiece, Is.EqualTo(500));
      Assert.That(segment.LastPiece, Is.EqualTo(500));
      Assert.That(segment.NextSegment, Is.EqualTo(1697));
      Assert.That(segment.PrevSegment, Is.EqualTo(1697));
      Assert.That(segment.Track, Is.EqualTo(track.EntryId));
    }
  }

  [Test]
  public void Read_TrackPieceSchemaDriftFailsClosed() {
    using var stream = BuildDat(TrackPieceStructure(userAngleFixedSize: 8));

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  private static MemoryStream BuildDat(
    StructureSpec trackPieceStructure,
    bool includeTopology = false,
    bool soakedTrackPiece = false
  ) {
    var structures = includeTopology
      ? new[] {
        LandscapeStructure(),
        trackPieceStructure,
        RideTrackStructure(),
        TrackSegmentStructure(),
      }
      : [LandscapeStructure(), trackPieceStructure];
    var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(Convert.ToUInt32(structures.Length));
      foreach (var structure in structures)
        WriteStructure(writer, structure);
      writer.Write(Convert.ToUInt32(includeTopology ? 4 : 2));
      writer.Write(0u);
      writer.Write(1ul);
      WriteTerrain(writer);
      writer.Write(1u);
      writer.Write(500ul);
      WriteTrackPiece(writer, soakedTrackPiece);
      if (includeTopology) {
        writer.Write(2u);
        writer.Write(700ul);
        WriteRideTrack(writer);
        writer.Write(3u);
        writer.Write(800ul);
        WriteTrackSegment(writer);
      }
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

  private static void WriteTrackPiece(BinaryWriter writer, bool soakedTrackPiece) {
    writer.Write(15);
    writer.Write(-1);
    writer.Write(2);
    writer.Write(3);
    writer.Write(0.25f);
    writer.Write(true);
    writer.Write(4);
    writer.Write(5);
    writer.Write(6);
    writer.Write(7);
    writer.Write(0ul);
    writer.Write(501ul);
    writer.Write(800ul);
    writer.Write(800ul);
    writer.Write(499ul);
    writer.Write(true);
    writer.Write(36u);
    writer.Write(2u);
    writer.Write(false);
    writer.Write(900ul);
    writer.Write(901ul);
    writer.Write(true);
    writer.Write(true);
    writer.Write(902ul);
    writer.Write(903ul);
    writer.Write(false);
    writer.Write(200ul);
    WriteDatString(writer, "Test_SID:tks");
    writer.Write(100ul);
    writer.Write(3);
    writer.Write(1);
    writer.Write(6);
    writer.Write(1);
    writer.Write(1);
    writer.Write(800ul);
    if (soakedTrackPiece) {
      writer.Write(12);
      writer.Write(1.25f);
    }
    writer.Write(0.5f);
    writer.Write(0.75f);
    writer.Write(2);
    if (soakedTrackPiece) writer.Write(7);
    writer.Write(45);
    if (soakedTrackPiece) writer.Write(999ul);
    writer.Write(new byte[64]);
  }

  private static void WriteRideTrack(BinaryWriter writer) {
    writer.Write(2);
    writer.Write(800ul);
    writer.Write(false);
    writer.Write(800ul);
    writer.Write(false);
    writer.Write(8u);
    writer.Write(1u);
    writer.Write(500ul);
    writer.Write(4);
    writer.Write(5);
    writer.Write(6);
    writer.Write(900ul);
  }

  private static void WriteTrackSegment(BinaryWriter writer) {
    writer.Write(2);
    writer.Write(500ul);
    writer.Write(500ul);
    writer.Write(1697ul);
    writer.Write(1697ul);
    writer.Write(false);
    writer.Write(700ul);
  }

  private static StructureSpec LandscapeStructure() => new(
    "Landscape",
    [new FieldSpec("EngineTerrain", "GE_Terrain")]);

  private static StructureSpec TrackPieceStructure(uint userAngleFixedSize = 4) => new(
    "TrackPiece",
    [
      new FieldSpec("BrakeSpeed", "int32", 4),
      new FieldSpec("CarTransformBone", "int32", 4),
      new FieldSpec("CurrentAnim", "int32", 4),
      new FieldSpec("CurrentAnimIndex", "int32", 4),
      new FieldSpec("CurrentAnimTime", "float32", 4),
      new FieldSpec("FirstAdvance", "bool", 1),
      FlexiColourField(),
      new FieldSpec("HoldingTime", "int32", 4),
      new FieldSpec("HoldingTrain", "managedobjectptr", 8),
      new FieldSpec("Next", "managedobjectptr", 8),
      new FieldSpec("Owner", "managedobjectptr", 8),
      new FieldSpec("PlatformPiece", "managedobjectptr", 8),
      new FieldSpec("Prev", "managedobjectptr", 8),
      new FieldSpec("Reversed", "bool", 1),
      RideEventTriggersField(),
      new FieldSpec("SIDDatabaseEntry", "managedobjectptr", 8),
      new FieldSpec("SYMBOLNAME", "string"),
      new FieldSpec("SceneryItem", "managedobjectptr", 8),
      SceneryItemDataField(),
      new FieldSpec("Segment", "managedobjectptr", 8),
      new FieldSpec("StartDistance", "float32", 4),
      new FieldSpec("StartDistanceBackwardsSpline", "float32", 4),
      new FieldSpec("TargetAnimState", "int32", 4),
      new FieldSpec("UserAngleDegrees", "int32", userAngleFixedSize),
      new FieldSpec("m_carTransformAtRestInverse", "matrix44", 64),
    ]);

  private static StructureSpec RideTrackStructure() => new(
    "Track",
    [
      new FieldSpec("Direction", "int32", 4),
      new FieldSpec("FirstSegment", "managedobjectptr", 8),
      new FieldSpec("IsCircuit", "bool", 1),
      new FieldSpec("LastSegment", "managedobjectptr", 8),
      new FieldSpec("Prototype", "bool", 1),
      new FieldSpec(
        "Track",
        "list",
        0,
        [new FieldSpec("TrackPiece", "managedobjectptr", 8)]),
      TrackFlexiColoursField(),
      new FieldSpec("TrackedRideInstance", "managedobjectptr", 8),
    ]);

  private static StructureSpec SoakedTrackPieceStructure() => new(
    "TrackPiece",
    [
      new FieldSpec("BrakeSpeed", "int32", 4),
      new FieldSpec("CarTransformBone", "int32", 4),
      new FieldSpec("CurrentAnim", "int32", 4),
      new FieldSpec("CurrentAnimIndex", "int32", 4),
      new FieldSpec("CurrentAnimTime", "float32", 4),
      new FieldSpec("FirstAdvance", "bool", 1),
      FlexiColourField(),
      new FieldSpec("HoldingTime", "int32", 4),
      new FieldSpec("HoldingTrain", "managedobjectptr", 8),
      new FieldSpec("Next", "managedobjectptr", 8),
      new FieldSpec("Owner", "managedobjectptr", 8),
      new FieldSpec("PlatformPiece", "managedobjectptr", 8),
      new FieldSpec("Prev", "managedobjectptr", 8),
      new FieldSpec("Reversed", "bool", 1),
      RideEventTriggersField(),
      new FieldSpec("SIDDatabaseEntry", "managedobjectptr", 8),
      new FieldSpec("SYMBOLNAME", "string"),
      new FieldSpec("SceneryItem", "managedobjectptr", 8),
      SceneryItemDataField(),
      new FieldSpec("Segment", "managedobjectptr", 8),
      new FieldSpec("Speed", "int32", 4),
      new FieldSpec("SpiralLiftHillDistance", "float32", 4),
      new FieldSpec("StartDistance", "float32", 4),
      new FieldSpec("StartDistanceBackwardsSpline", "float32", 4),
      new FieldSpec("TargetAnimState", "int32", 4),
      new FieldSpec("TunnelLightColours", "int32", 4),
      new FieldSpec("UserAngleDegrees", "int32", 4),
      new FieldSpec(
        "WalkAlongTrackPersonInfosOnTrackPiece",
        "managedobjectptr",
        8),
      new FieldSpec("m_carTransformAtRestInverse", "matrix44", 64),
    ]);

  private static StructureSpec TrackSegmentStructure() => new(
    "TrackSegment",
    [
      new FieldSpec("Direction", "int32", 4),
      new FieldSpec("FirstPiece", "managedobjectptr", 8),
      new FieldSpec("LastPiece", "managedobjectptr", 8),
      new FieldSpec("NextSegment", "managedobjectptr", 8),
      new FieldSpec("PrevSegment", "managedobjectptr", 8),
      new FieldSpec("Prototype", "bool", 1),
      new FieldSpec("Track", "managedobjectptr", 8),
    ]);

  private static FieldSpec FlexiColourField() => new(
    "FlexiColourField",
    "struct",
    12,
    [
      new FieldSpec("COL0", "int32", 4),
      new FieldSpec("COL1", "int32", 4),
      new FieldSpec("COL2", "int32", 4),
    ]);

  private static FieldSpec TrackFlexiColoursField() => new(
    "TrackFlexiColours",
    "struct",
    12,
    [
      new FieldSpec("COL0", "int32", 4),
      new FieldSpec("COL1", "int32", 4),
      new FieldSpec("COL2", "int32", 4),
    ]);

  private static FieldSpec RideEventTriggersField() => new(
    "RideEventTriggers",
    "list",
    0,
    [
      new FieldSpec("PreTrigger", "bool", 1),
      new FieldSpec("SourceObject", "reference", 8),
      new FieldSpec("TargetObject", "reference", 8),
      new FieldSpec("Trigger", "bool", 1),
    ]);

  private static FieldSpec SceneryItemDataField() => new(
    "SceneryItemDataField",
    "struct",
    20,
    [
      new FieldSpec("CORNER", "int32", 4),
      new FieldSpec("DIRECTION", "int32", 4),
      new FieldSpec("HEIGHT", "int32", 4),
      new FieldSpec("POSX", "int32", 4),
      new FieldSpec("POSZ", "int32", 4),
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
