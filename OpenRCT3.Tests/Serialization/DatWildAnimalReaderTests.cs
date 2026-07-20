// DAT Wild Animal Reader Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Numerics;
using System.Text;

namespace OpenRCT3.Tests.Serialization;

[TestFixture]
public class DatWildAnimalReaderTests {
  [Test]
  public void Read_LinksExactSpeciesAndVisualsInSavedAnimalOrder() {
    using var stream = BuildDat();

    var data = DatTerrainReader.Read(stream);

    var first = data.WildAnimalPlacements[0];
    using (Assert.EnterMultipleScope()) {
      Assert.That(stream.Position, Is.EqualTo(stream.Length));
      Assert.That(
        data.WildAnimalSpeciesDatabaseEntries.Select(entry => entry.EntryId),
        Is.EqualTo(new ulong[] { 101 }));
      Assert.That(
        data.WildAnimalVisuals.Select(visual => visual.EntryId),
        Is.EqualTo(new ulong[] { 201, 202, 203 }));
      Assert.That(
        data.WildAnimals.Select(animal => animal.EntryId),
        Is.EqualTo(new ulong[] { 301, 302 }));
      Assert.That(
        data.WildAnimalPlacements.Select(placement => placement.Animal.EntryId),
        Is.EqualTo(new ulong[] { 301, 302 }));
      Assert.That(first.Species.EntryId, Is.EqualTo(101));
      Assert.That(first.Species.IsUnlocked, Is.True);
      Assert.That(first.Species.OverlayFilename, Is.EqualTo(@"WildAnimals\WildAnimals"));
      Assert.That(first.Species.SymbolName, Is.EqualTo("Ostrich"));
      Assert.That(first.Visual.EntryId, Is.EqualTo(201));
      Assert.That(first.Visual.DoShadows, Is.True);
      Assert.That(first.Visual.Visible, Is.True);
      Assert.That(first.Visual.WorldMatrix, Is.EqualTo(FirstMatrix));
      Assert.That(first.Visual.AnimationData, Is.EqualTo(new[] {
        new DatWildAnimalAnimationData(1.25f, 7, 0.75f),
        new DatWildAnimalAnimationData(2.5f, 8, 0.25f),
      }));
      Assert.That(first.Animal.IsAdult, Is.True);
      Assert.That(first.Animal.IsMale, Is.False);
      Assert.That(first.Animal.Type, Is.EqualTo(1));
      Assert.That(
        first.VariantSelectionStatus,
        Is.EqualTo(DatWildAnimalVariantSelectionStatus.Unsupported));
      Assert.That(first.Species, Is.SameAs(data.WildAnimalSpeciesDatabaseEntries[0]));
      Assert.That(first.Visual, Is.SameAs(data.WildAnimalVisuals[0]));
    }
  }

  [Test]
  public void Read_WildAnimalVisualSchemaDriftFailsClosed() {
    using var stream = BuildDat(
      visualStructure: WildAnimalVisualStructure(
        weightField: new FieldSpec("Weight", "int32", 4)));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain("WildAnimalVisual.AnimData.Weight"));
  }

  [TestCase(999ul, 201ul, "WASDatabaseEntry 999")]
  [TestCase(101ul, 999ul, "WildAnimalVisual 999")]
  public void Read_MissingAnimalLinkFailsClosed(
    ulong speciesEntryId,
    ulong visualEntryId,
    string expectedDetail
  ) {
    using var stream = BuildDat(
      firstAnimalSpeciesEntryId: speciesEntryId,
      firstAnimalVisualEntryId: visualEntryId);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain(expectedDetail));
  }

  [TestCase(DuplicateEntryKind.Species, "duplicate WASDatabaseEntry ID 101")]
  [TestCase(DuplicateEntryKind.Visual, "duplicate WildAnimalVisual ID 201")]
  [TestCase(DuplicateEntryKind.Animal, "duplicate WildAnimal ID 301")]
  public void Read_DuplicateWildAnimalIdentityFailsClosed(
    DuplicateEntryKind kind,
    string expectedDetail
  ) {
    using var stream = BuildDat(duplicateEntry: kind);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain(expectedDetail));
  }

  [Test]
  public void Read_NonFiniteWildAnimalVisualMatrixFailsClosed() {
    var matrix = FirstMatrix;
    matrix.M23 = float.NaN;
    using var stream = BuildDat(firstMatrix: matrix);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      DatTerrainReader.Read(stream)));

    Assert.That(exception!.Message, Does.Contain(
      "WildAnimalVisual WorldMatrix[6] contains a non-finite value"));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Read_OstrichFarmCapturesExactWildAnimalIdentityAndFiniteMatrices() {
    var installPath = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(string.IsNullOrWhiteSpace(installPath), Is.False);
    var campaignPath = Path.Combine(
      installPath!,
      "Campaigns",
      "Base",
      "Wild",
      "OstrichFarm.dat");
    Assert.That(File.Exists(campaignPath), Is.True, campaignPath);

    var data = DatTerrainReader.Read(campaignPath);
    var ostrich = data.WildAnimalSpeciesDatabaseEntries.Single(entry => entry.EntryId == 8_115);
    var first = data.WildAnimalPlacements.Single(
      placement => placement.Animal.EntryId == 8_733);

    using (Assert.EnterMultipleScope()) {
      Assert.That(data.WildAnimalSpeciesDatabaseEntries, Has.Count.EqualTo(21));
      Assert.That(data.WildAnimals, Has.Count.EqualTo(16));
      Assert.That(data.WildAnimalVisuals, Has.Count.EqualTo(16));
      Assert.That(data.WildAnimalPlacements, Has.Count.EqualTo(16));
      Assert.That(ostrich.OverlayFilename, Is.EqualTo(@"WildAnimals\WildAnimals"));
      Assert.That(ostrich.SymbolName, Is.EqualTo("Ostrich"));
      Assert.That(
        data.WildAnimalPlacements.All(placement =>
          placement.Species.EntryId == ostrich.EntryId),
        Is.True);
      Assert.That(first.Animal.VisualEntryId, Is.EqualTo(8_739));
      Assert.That(first.Visual.WorldMatrix.M41, Is.EqualTo(-60.26788f).Within(0.00001f));
      Assert.That(first.Visual.WorldMatrix.M42, Is.EqualTo(-1.406913f).Within(0.00001f));
      Assert.That(first.Visual.WorldMatrix.M43, Is.EqualTo(103.0577f).Within(0.0001f));
      Assert.That(
        data.WildAnimalVisuals.All(visual => IsFinite(visual.WorldMatrix)),
        Is.True);
      Assert.That(
        data.WildAnimalPlacements.All(placement =>
          placement.VariantSelectionStatus ==
            DatWildAnimalVariantSelectionStatus.Unsupported),
        Is.True);
    }
  }

  private static readonly Matrix4x4 FirstMatrix = new(
    1f, 2f, 3f, 0f,
    4f, 5f, 6f, 0f,
    7f, 8f, 9f, 0f,
    10f, 11f, 12f, 1f);

  private static readonly Matrix4x4 SecondMatrix = Matrix4x4.CreateTranslation(
    -4f,
    0.5f,
    9f);

  private static MemoryStream BuildDat(
    StructureSpec? visualStructure = null,
    ulong firstAnimalSpeciesEntryId = 101,
    ulong firstAnimalVisualEntryId = 201,
    DuplicateEntryKind duplicateEntry = DuplicateEntryKind.None,
    Matrix4x4? firstMatrix = null
  ) {
    var structures = new[] {
      LandscapeStructure(),
      WildAnimalSpeciesDatabaseEntryStructure(),
      visualStructure ?? WildAnimalVisualStructure(),
      WildAnimalStructure(),
    };
    var entries = new List<EntrySpec> {
      new(0, 1, WriteTerrain),
      new(3, 301, writer => WriteWildAnimal(
        writer,
        firstAnimalSpeciesEntryId,
        firstAnimalVisualEntryId,
        isAdult: true,
        isMale: false,
        type: 1)),
      new(2, 201, writer => WriteWildAnimalVisual(
        writer,
        firstMatrix ?? FirstMatrix,
        [
          new DatWildAnimalAnimationData(1.25f, 7, 0.75f),
          new DatWildAnimalAnimationData(2.5f, 8, 0.25f),
        ])),
      new(1, 101, WriteWildAnimalSpeciesDatabaseEntry),
      new(2, 202, writer => WriteWildAnimalVisual(writer, SecondMatrix, [])),
      new(2, 203, writer => WriteWildAnimalVisual(writer, Matrix4x4.Identity, [])),
      new(3, 302, writer => WriteWildAnimal(
        writer,
        101,
        202,
        isAdult: false,
        isMale: true,
        type: 0)),
    };
    switch (duplicateEntry) {
      case DuplicateEntryKind.Species:
        entries.Add(new EntrySpec(1, 101, WriteWildAnimalSpeciesDatabaseEntry));
        break;
      case DuplicateEntryKind.Visual:
        entries.Add(new EntrySpec(
          2,
          201,
          writer => WriteWildAnimalVisual(writer, SecondMatrix, [])));
        break;
      case DuplicateEntryKind.Animal:
        entries.Add(new EntrySpec(
          3,
          301,
          writer => WriteWildAnimal(writer, 101, 202, true, true, 0)));
        break;
    }

    var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(Convert.ToUInt32(structures.Length));
      foreach (var structure in structures)
        WriteStructure(writer, structure);
      writer.Write(Convert.ToUInt32(entries.Count));
      foreach (var entry in entries) {
        writer.Write(Convert.ToUInt32(entry.StructureIndex));
        writer.Write(entry.EntryId);
        entry.Write(writer);
      }
    }
    stream.Position = 0;
    return stream;
  }

  private static void WriteWildAnimalSpeciesDatabaseEntry(BinaryWriter writer) {
    writer.Write(true);
    WriteDatString(writer, @"WildAnimals\WildAnimals");
    WriteDatString(writer, "Ostrich");
  }

  private static void WriteWildAnimalVisual(
    BinaryWriter writer,
    Matrix4x4 matrix,
    IReadOnlyList<DatWildAnimalAnimationData> animationData
  ) {
    writer.Write(Convert.ToUInt32(animationData.Count * 12));
    writer.Write(Convert.ToUInt32(animationData.Count));
    foreach (var animation in animationData) {
      writer.Write(animation.Time);
      writer.Write(animation.Type);
      writer.Write(animation.Weight);
    }
    writer.Write(true);
    writer.Write(true);
    WriteMatrix(writer, matrix);
  }

  private static void WriteWildAnimal(
    BinaryWriter writer,
    ulong speciesEntryId,
    ulong visualEntryId,
    bool isAdult,
    bool isMale,
    int type
  ) {
    writer.Write(987u);
    writer.Write(speciesEntryId);
    writer.Write(isAdult);
    WriteDatString(writer, "opaque");
    writer.Write(isMale);
    writer.Write(type);
    writer.Write(visualEntryId);
  }

  private static void WriteMatrix(BinaryWriter writer, Matrix4x4 matrix) {
    writer.Write(matrix.M11);
    writer.Write(matrix.M12);
    writer.Write(matrix.M13);
    writer.Write(matrix.M14);
    writer.Write(matrix.M21);
    writer.Write(matrix.M22);
    writer.Write(matrix.M23);
    writer.Write(matrix.M24);
    writer.Write(matrix.M31);
    writer.Write(matrix.M32);
    writer.Write(matrix.M33);
    writer.Write(matrix.M34);
    writer.Write(matrix.M41);
    writer.Write(matrix.M42);
    writer.Write(matrix.M43);
    writer.Write(matrix.M44);
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

  private static StructureSpec LandscapeStructure() => new(
    "Landscape",
    [new FieldSpec("EngineTerrain", "GE_Terrain")]);

  private static StructureSpec WildAnimalSpeciesDatabaseEntryStructure() => new(
    "WASDatabaseEntry",
    [
      new FieldSpec("ISUNLOCKED", "bool", 1),
      new FieldSpec("OVERLAYFILENAME", "string"),
      new FieldSpec("SYMBOLNAME", "string"),
    ]);

  private static StructureSpec WildAnimalVisualStructure(FieldSpec? weightField = null) => new(
    "WildAnimalVisual",
    [
      new FieldSpec(
        "AnimData",
        "array",
        NestedFields: [
          new FieldSpec("Time", "float32", 4),
          new FieldSpec("Type", "int32", 4),
          weightField ?? new FieldSpec("Weight", "float32", 4),
        ]),
      new FieldSpec("DoShadows", "bool", 1),
      new FieldSpec("Visible", "bool", 1),
      new FieldSpec("WorldMatrix", "matrix44", 64),
    ]);

  private static StructureSpec WildAnimalStructure() => new(
    "WildAnimal",
    [
      new FieldSpec("OpaqueCounter", "uint32", 4),
      new FieldSpec("Entry", "managedobjectptr", 8),
      new FieldSpec("IsAdult", "bool", 1),
      new FieldSpec("OpaqueName", "string"),
      new FieldSpec("IsMale", "bool", 1),
      new FieldSpec("Type", "int32", 4),
      new FieldSpec("Visual", "managedobjectptr", 8),
    ]);

  private static bool IsFinite(Matrix4x4 value) =>
    float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
    float.IsFinite(value.M43) && float.IsFinite(value.M44);

  public enum DuplicateEntryKind {
    None,
    Species,
    Visual,
    Animal,
  }

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

  private sealed record EntrySpec(
    int StructureIndex,
    ulong EntryId,
    Action<BinaryWriter> Write);
}
