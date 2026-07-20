// DatSceneryReaderTests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using System.Text;

namespace OpenRCT3.Tests.Serialization;

[TestFixture]
public class DatSceneryReaderTests {
  private const ulong BehaviourReference = 0x1112131415161718;
  private const ulong CustomUvProvider = 0x2122232425262728;
  private const ulong OwnerReference = 0x3132333435363738;
  private const ulong ParticleReference = 0x4142434445464748;
  private const ulong VendorPointer = 0x5152535455565758;
  private const ulong UnresolvedDatabaseEntry = 0xF0F1F2F3F4F5F6F7;

  [Test]
  public void Read_BaseTutorialAndPlacementEntries_PreservesOrderAndResolvesBothDirections() {
    using var stream = BuildDat(
      [
        LandscapeStructure(),
        SidDatabaseEntryStructure(hasIsInvented: true),
        SceneryItemStructure(ScenerySchemaVariant.Base, hasCustomUvProvider: true),
        SceneryItemStructure(ScenerySchemaVariant.Base, hasCustomUvProvider: false),
        SidDatabaseEntryStructure(hasIsInvented: false),
        SceneryItemPlacementSingleStructure(),
      ],
      [
        new EntrySpec(0, 1, WriteTerrain),
        new EntrySpec(1, 100, writer => WriteSidDatabaseEntry(
          writer,
          hasIsInvented: true,
          overlayFilename: "base.ovl",
          symbolName: "Ω景",
          utf16SymbolName: true)),
        new EntrySpec(2, 200, writer => WriteSceneryItem(
          writer,
          ScenerySchemaVariant.Base,
          hasCustomUvProvider: true,
          databaseEntry: 100)),
        new EntrySpec(3, 201, writer => WriteSceneryItem(
          writer,
          ScenerySchemaVariant.Base,
          hasCustomUvProvider: false,
          databaseEntry: 101)),
        new EntrySpec(5, 300, writer => WritePlacement(
          writer,
          sidDatabaseEntry: 100,
          sceneryItem: 200)),
        new EntrySpec(4, 101, writer => WriteSidDatabaseEntry(
          writer,
          hasIsInvented: false,
          overlayFilename: "tutorial.ovl",
          symbolName: "TutorialSymbol")),
        new EntrySpec(2, 202, writer => WriteSceneryItem(
          writer,
          ScenerySchemaVariant.Base,
          hasCustomUvProvider: true,
          databaseEntry: UnresolvedDatabaseEntry)),
      ]);

    var terrain = DatTerrainReader.Read(stream);

    var backwardItem = terrain.SceneryItems[0];
    var forwardItem = terrain.SceneryItems[1];
    var unresolvedItem = terrain.SceneryItems[2];
    var placement = terrain.SceneryItemPlacements[0];
    Assert.Multiple(new Action(() => {
      Assert.That(
        terrain.SceneryEntries.Select(entry => entry.EntryId),
        Is.EqualTo(new ulong[] { 100, 200, 201, 300, 101, 202 }));
      Assert.That(terrain.SidDatabaseEntries, Has.Count.EqualTo(2));
      Assert.That(terrain.SceneryItems, Has.Count.EqualTo(3));
      Assert.That(terrain.SceneryItemPlacements, Has.Count.EqualTo(1));
      Assert.That(terrain.SidDatabaseEntries[0].IsAvailable, Is.True);
      Assert.That(terrain.SidDatabaseEntries[0].IsHidden, Is.False);
      Assert.That(terrain.SidDatabaseEntries[0].IsInvented, Is.True);
      Assert.That(terrain.SidDatabaseEntries[0].OverlayFilename, Is.EqualTo("base.ovl"));
      Assert.That(terrain.SidDatabaseEntries[0].SymbolName, Is.EqualTo("Ω景"));
      Assert.That(terrain.SidDatabaseEntries[1].IsInvented, Is.Null);
      Assert.That(backwardItem.Variant, Is.EqualTo(DatSceneryItemVariant.Base));
      Assert.That(backwardItem.AnimInfoList, Has.Count.EqualTo(2));
      Assert.That(backwardItem.AnimInfoList[0].AutoLoop, Is.True);
      Assert.That(backwardItem.AnimInfoList[0].CurrentAnimation, Is.EqualTo(-2));
      Assert.That(backwardItem.AnimInfoList[0].CurrentAnimationTime, Is.EqualTo(0.5f));
      Assert.That(backwardItem.AnimInfoList[1].MarkedForDeletion, Is.True);
      Assert.That(backwardItem.BreakFlags, Is.EqualTo(7));
      Assert.That(backwardItem.BreakTime, Is.EqualTo(1.5f));
      Assert.That(
        backwardItem.BehaviourArray,
        Is.EqualTo(new ulong[] { BehaviourReference, ulong.MaxValue }));
      Assert.That(backwardItem.CustomUvProvider, Is.EqualTo(CustomUvProvider));
      Assert.That(backwardItem.DatabaseEntry, Is.EqualTo(100));
      Assert.That(backwardItem.ResolvedDatabaseEntry, Is.SameAs(terrain.SidDatabaseEntries[0]));
      Assert.That(backwardItem.ForceAbsoluteHeight, Is.True);
      Assert.That(backwardItem.FrameOffset, Is.EqualTo(-3));
      Assert.That(backwardItem.FlexiColourField, Is.EqualTo(new DatSceneryFlexiColour(1, 2, 3)));
      Assert.That(backwardItem.HeightOffset, Is.EqualTo(-4));
      Assert.That(backwardItem.IsHidden, Is.False);
      Assert.That(backwardItem.Owner, Is.EqualTo(OwnerReference));
      Assert.That(
        backwardItem.ParticleSourceEntries,
        Is.EqualTo(new ulong[] { 0, ParticleReference }));
      Assert.That(
        backwardItem.SceneryItemDataField,
        Is.EqualTo(new DatSceneryItemDataField(7, 8, -9, null, 10, 11)));
      Assert.That(backwardItem.Vendor, Is.EqualTo(VendorPointer));
      Assert.That(forwardItem.CustomUvProvider, Is.Null);
      Assert.That(forwardItem.ResolvedDatabaseEntry, Is.SameAs(terrain.SidDatabaseEntries[1]));
      Assert.That(unresolvedItem.DatabaseEntry, Is.EqualTo(UnresolvedDatabaseEntry));
      Assert.That(unresolvedItem.ResolvedDatabaseEntry, Is.Null);
      Assert.That(placement.EntryId, Is.EqualTo(300));
      Assert.That(placement.Owner, Is.EqualTo(0x6162636465666768));
      Assert.That(placement.SidDatabaseEntry, Is.EqualTo(100));
      Assert.That(placement.SceneryItem, Is.EqualTo(200));
      Assert.That(
        placement.SceneryItemDataField,
        Is.EqualTo(new DatSceneryItemDataField(12, 13, -14, null, 15, 16)));
      Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }));
  }

  [Test]
  public void Read_SoakedSceneryItem_DecodesExpansionFields() {
    using var stream = BuildSingleSceneryItemDat(
      ScenerySchemaVariant.Soaked,
      writer => WriteSceneryItem(
        writer,
        ScenerySchemaVariant.Soaked,
        hasCustomUvProvider: true,
        databaseEntry: UnresolvedDatabaseEntry));

    var item = DatTerrainReader.Read(stream).SceneryItems.Single();

    Assert.Multiple(new Action(() => {
      Assert.That(item.Variant, Is.EqualTo(DatSceneryItemVariant.Soaked));
      Assert.That(item.AdSpend, Is.Null);
      Assert.That(
        item.FireworkSlotTransform,
        Is.EqualTo(new DatFireworkSlotTransform(15.5f, -3.25f)));
      Assert.That(
        item.LightFlexiColourField,
        Is.EqualTo(new DatSceneryFlexiColour(4, 5, 6)));
      Assert.That(item.MadIndex, Is.Null);
      Assert.That(item.SceneryItemDataField.HeightAdjust, Is.EqualTo(2.75f));
    }));
  }

  [Test]
  public void Read_WildSceneryItem_DecodesExpansionFields() {
    using var stream = BuildSingleSceneryItemDat(
      ScenerySchemaVariant.Wild,
      writer => WriteSceneryItem(
        writer,
        ScenerySchemaVariant.Wild,
        hasCustomUvProvider: true,
        databaseEntry: UnresolvedDatabaseEntry));

    var item = DatTerrainReader.Read(stream).SceneryItems.Single();

    Assert.Multiple(new Action(() => {
      Assert.That(item.Variant, Is.EqualTo(DatSceneryItemVariant.Wild));
      Assert.That(item.AdSpend, Is.EqualTo(12.25f));
      Assert.That(item.MadIndex, Is.EqualTo(-17));
      Assert.That(item.FireworkSlotTransform, Is.Not.Null);
      Assert.That(item.LightFlexiColourField, Is.Not.Null);
      Assert.That(item.SceneryItemDataField.HeightAdjust, Is.EqualTo(2.75f));
    }));
  }

  [Test]
  public void Read_ScenerySchemaWithReorderedFields_ThrowsInvalidDataException() {
    var fields = SceneryItemFields(
      ScenerySchemaVariant.Wild,
      hasCustomUvProvider: true).ToList();
    var madIndex = fields.Single(field => field.Name == "MADINDEX");
    fields.Remove(madIndex);
    fields.Add(madIndex);

    AssertInvalidScenerySchema(new StructureSpec("SceneryItem", fields));
  }

  [Test]
  public void Read_ScenerySchemaWithWrongNestedSize_ThrowsInvalidDataException() {
    var fields = SceneryItemFields(
      ScenerySchemaVariant.Soaked,
      hasCustomUvProvider: true).ToList();
    var index = fields.FindIndex(field => field.Name == "SceneryItemDataField");
    fields[index] = fields[index] with { FixedSize = 20 };

    AssertInvalidScenerySchema(new StructureSpec("SceneryItem", fields));
  }

  [Test]
  public void Read_InvalidSidBoolean_ThrowsInvalidDataException() {
    using var stream = BuildDat(
      [LandscapeStructure(), SidDatabaseEntryStructure(hasIsInvented: true)],
      [
        new EntrySpec(0, 1, WriteTerrain),
        new EntrySpec(1, 2, writer => {
          writer.Write(Convert.ToByte(2));
          writer.Write(false);
          writer.Write(true);
          WriteDatString(writer, "base.ovl");
          WriteDatString(writer, "Symbol");
        }),
      ]);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_NonFiniteWildFloat_ThrowsInvalidDataException() {
    using var stream = BuildSingleSceneryItemDat(
      ScenerySchemaVariant.Wild,
      writer => WriteSceneryItem(
        writer,
        ScenerySchemaVariant.Wild,
        hasCustomUvProvider: true,
        databaseEntry: UnresolvedDatabaseEntry,
        adSpend: float.NaN));

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_DuplicateSidEntryIds_ThrowsInvalidDataException() {
    using var stream = BuildDat(
      [LandscapeStructure(), SidDatabaseEntryStructure(hasIsInvented: true)],
      [
        new EntrySpec(0, 1, WriteTerrain),
        new EntrySpec(1, 2, writer => WriteSidDatabaseEntry(
          writer,
          hasIsInvented: true,
          overlayFilename: "first.ovl",
          symbolName: "First")),
        new EntrySpec(1, 2, writer => WriteSidDatabaseEntry(
          writer,
          hasIsInvented: true,
          overlayFilename: "second.ovl",
          symbolName: "Second")),
      ]);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [TestCase(MalformedStringKind.NonAscii)]
  [TestCase(MalformedStringKind.OddUtf16)]
  [TestCase(MalformedStringKind.UnpairedSurrogate)]
  [TestCase(MalformedStringKind.ExcessiveLength)]
  public void Read_MalformedSidString_ThrowsInvalidDataException(MalformedStringKind kind) {
    using var stream = BuildDat(
      [LandscapeStructure(), SidDatabaseEntryStructure(hasIsInvented: true)],
      [
        new EntrySpec(0, 1, WriteTerrain),
        new EntrySpec(1, 2, writer => {
          writer.Write(true);
          writer.Write(false);
          writer.Write(true);
          WriteMalformedString(writer, kind);
          WriteDatString(writer, "Symbol");
        }),
      ]);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  [Test]
  public void Read_TruncatedSceneryItem_ThrowsInvalidDataException() {
    using var complete = BuildSingleSceneryItemDat(
      ScenerySchemaVariant.Base,
      writer => WriteSceneryItem(
        writer,
        ScenerySchemaVariant.Base,
        hasCustomUvProvider: true,
        databaseEntry: UnresolvedDatabaseEntry));
    var bytes = complete.ToArray();
    Array.Resize(ref bytes, bytes.Length - 1);
    using var truncated = new MemoryStream(bytes);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(truncated)));
  }

  private static MemoryStream BuildSingleSceneryItemDat(
    ScenerySchemaVariant variant,
    Action<BinaryWriter> writeSceneryItem
  ) => BuildDat(
    [LandscapeStructure(), SceneryItemStructure(variant, hasCustomUvProvider: true)],
    [new EntrySpec(0, 1, WriteTerrain), new EntrySpec(1, 2, writeSceneryItem)]);

  private static void AssertInvalidScenerySchema(StructureSpec invalidStructure) {
    using var stream = BuildDat(
      [LandscapeStructure(), invalidStructure],
      [new EntrySpec(0, 1, WriteTerrain)]);

    Assert.Throws<InvalidDataException>(new Action(() => DatTerrainReader.Read(stream)));
  }

  private static MemoryStream BuildDat(
    IReadOnlyList<StructureSpec> structures,
    IReadOnlyList<EntrySpec> entries
  ) {
    var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true)) {
      writer.Write(Convert.ToUInt32(structures.Count));
      foreach (var structure in structures)
        WriteStructure(writer, structure);
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

  private static void WriteSidDatabaseEntry(
    BinaryWriter writer,
    bool hasIsInvented,
    string overlayFilename,
    string symbolName,
    bool utf16SymbolName = false
  ) {
    writer.Write(true);
    writer.Write(false);
    if (hasIsInvented) writer.Write(true);
    WriteDatString(writer, overlayFilename);
    WriteDatString(writer, symbolName, utf16SymbolName);
  }

  private static void WriteSceneryItem(
    BinaryWriter writer,
    ScenerySchemaVariant variant,
    bool hasCustomUvProvider,
    ulong databaseEntry,
    float adSpend = 12.25f
  ) {
    var isSoakedOrWild = variant is ScenerySchemaVariant.Soaked
      or ScenerySchemaVariant.Wild;
    if (variant == ScenerySchemaVariant.Wild) writer.Write(adSpend);
    writer.Write(20u);
    writer.Write(2u);
    writer.Write(true);
    writer.Write(-2);
    writer.Write(0.5f);
    writer.Write(false);
    writer.Write(false);
    writer.Write(3);
    writer.Write(1.25f);
    writer.Write(true);
    writer.Write(7);
    writer.Write(1.5f);
    writer.Write(16u);
    writer.Write(2u);
    writer.Write(BehaviourReference);
    writer.Write(ulong.MaxValue);
    if (hasCustomUvProvider) writer.Write(CustomUvProvider);
    writer.Write(databaseEntry);
    writer.Write(true);
    writer.Write(-3);
    if (isSoakedOrWild) {
      writer.Write(15.5f);
      writer.Write(-3.25f);
    }
    writer.Write(1);
    writer.Write(2);
    writer.Write(3);
    writer.Write(-4);
    writer.Write(false);
    if (isSoakedOrWild) {
      writer.Write(4);
      writer.Write(5);
      writer.Write(6);
    }
    if (variant == ScenerySchemaVariant.Wild) writer.Write(-17);
    writer.Write(OwnerReference);
    writer.Write(16u);
    writer.Write(2u);
    writer.Write(0ul);
    writer.Write(ParticleReference);
    writer.Write(7);
    writer.Write(8);
    writer.Write(-9);
    if (isSoakedOrWild) writer.Write(2.75f);
    writer.Write(10);
    writer.Write(11);
    writer.Write(VendorPointer);
  }

  private static void WritePlacement(
    BinaryWriter writer,
    ulong sidDatabaseEntry,
    ulong sceneryItem
  ) {
    writer.Write(21);
    writer.Write(22);
    writer.Write(23);
    writer.Write(0x6162636465666768ul);
    writer.Write(sidDatabaseEntry);
    writer.Write(sceneryItem);
    writer.Write(12);
    writer.Write(13);
    writer.Write(-14);
    writer.Write(15);
    writer.Write(16);
  }

  private static void WriteDatString(
    BinaryWriter writer,
    string value,
    bool utf16 = false
  ) {
    var bytes = utf16 ? Encoding.Unicode.GetBytes(value) : Encoding.ASCII.GetBytes(value);
    writer.Write(Convert.ToUInt32(bytes.Length + (utf16 ? sizeof(uint) : 0)));
    if (utf16) writer.Write(0xEFEFEFEFu);
    writer.Write(bytes);
  }

  private static void WriteMalformedString(BinaryWriter writer, MalformedStringKind kind) {
    switch (kind) {
      case MalformedStringKind.NonAscii:
        writer.Write(1u);
        writer.Write(Convert.ToByte(0x80));
        return;
      case MalformedStringKind.OddUtf16:
        writer.Write(5u);
        writer.Write(0xEFEFEFEFu);
        writer.Write(Convert.ToByte(0x41));
        return;
      case MalformedStringKind.UnpairedSurrogate:
        writer.Write(6u);
        writer.Write(0xEFEFEFEFu);
        writer.Write(Convert.ToUInt16(0xD800));
        return;
      case MalformedStringKind.ExcessiveLength:
        writer.Write(uint.MaxValue);
        return;
      default:
        throw new ArgumentOutOfRangeException(nameof(kind));
    }
  }

  private static StructureSpec LandscapeStructure() => new(
    "Landscape",
    [new FieldSpec("EngineTerrain", "GE_Terrain")]);

  private static StructureSpec SidDatabaseEntryStructure(bool hasIsInvented) => new(
    "SIDDatabaseEntry",
    hasIsInvented
      ? [
        new FieldSpec("IsAvailable", "bool", 1),
        new FieldSpec("IsHidden", "bool", 1),
        new FieldSpec("IsInvented", "bool", 1),
        new FieldSpec("OVERLAYFILENAME", "string"),
        new FieldSpec("SYMBOLNAME", "string"),
      ]
      : [
        new FieldSpec("IsAvailable", "bool", 1),
        new FieldSpec("IsHidden", "bool", 1),
        new FieldSpec("OVERLAYFILENAME", "string"),
        new FieldSpec("SYMBOLNAME", "string"),
      ]);

  private static StructureSpec SceneryItemStructure(
    ScenerySchemaVariant variant,
    bool hasCustomUvProvider
  ) => new("SceneryItem", SceneryItemFields(variant, hasCustomUvProvider));

  private static IReadOnlyList<FieldSpec> SceneryItemFields(
    ScenerySchemaVariant variant,
    bool hasCustomUvProvider
  ) {
    var fields = new List<FieldSpec>();
    if (variant == ScenerySchemaVariant.Wild)
      fields.Add(new FieldSpec("ADSPEND", "float32", 4));
    fields.Add(AnimInfoListField());
    fields.Add(new FieldSpec("BREAKFLAGS", "int32", 4));
    fields.Add(new FieldSpec("BREAKTIME", "float32", 4));
    fields.Add(new FieldSpec(
      "BehaviourArray",
      "array",
      0,
      [new FieldSpec("BehaviourReference", "reference", 8)]));
    if (hasCustomUvProvider)
      fields.Add(new FieldSpec("CustomUVProvider", "reference", 8));
    fields.Add(new FieldSpec("DATABASEENTRY", "reference", 8));
    fields.Add(new FieldSpec("FORCEABSOLUTEHEIGHT", "bool", 1));
    fields.Add(new FieldSpec("FRAMEOFFSET", "int32", 4));
    var isSoakedOrWild = variant is ScenerySchemaVariant.Soaked
      or ScenerySchemaVariant.Wild;
    if (isSoakedOrWild) fields.Add(FireworkSlotTransformField());
    fields.Add(FlexiColourField("FlexiColourField"));
    fields.Add(new FieldSpec("HEIGHTOFFSET", "int32", 4));
    fields.Add(new FieldSpec("IsHidden", "bool", 1));
    if (isSoakedOrWild) fields.Add(FlexiColourField("LightFlexiColourField"));
    if (variant == ScenerySchemaVariant.Wild)
      fields.Add(new FieldSpec("MADINDEX", "int32", 4));
    fields.Add(new FieldSpec("Owner", "reference", 8));
    fields.Add(new FieldSpec(
      "ParticleSourceEntries",
      "array",
      0,
      [new FieldSpec("SourceRef", "reference", 8)]));
    fields.Add(SceneryItemDataField(isSoakedOrWild));
    fields.Add(new FieldSpec("Vendor", "managedobjectptr", 8));
    return fields;
  }

  private static StructureSpec SceneryItemPlacementSingleStructure() => new(
    "SceneryItemPlacementSingle",
    [
      FlexiColourField("FlexiColourField"),
      new FieldSpec("Owner", "managedobjectptr", 8),
      new FieldSpec("SIDDatabaseEntry", "managedobjectptr", 8),
      new FieldSpec("SceneryItem", "managedobjectptr", 8),
      SceneryItemDataField(hasHeightAdjust: false),
    ]);

  private static FieldSpec AnimInfoListField() => new(
    "AnimInfoList",
    "list",
    0,
    [
      new FieldSpec("AutoLoop", "bool", 1),
      new FieldSpec("CurrentAnimation", "int32", 4),
      new FieldSpec("CurrentAnimationTime", "float32", 4),
      new FieldSpec("MarkedForDeletion", "bool", 1),
    ]);

  private static FieldSpec FireworkSlotTransformField() => new(
    "FireworkSlotTransform",
    "struct",
    8,
    [
      new FieldSpec("Angle", "float32", 4),
      new FieldSpec("Elevation", "float32", 4),
    ]);

  private static FieldSpec FlexiColourField(string name) => new(
    name,
    "struct",
    12,
    [
      new FieldSpec("COL0", "int32", 4),
      new FieldSpec("COL1", "int32", 4),
      new FieldSpec("COL2", "int32", 4),
    ]);

  private static FieldSpec SceneryItemDataField(bool hasHeightAdjust) => new(
    "SceneryItemDataField",
    "struct",
    hasHeightAdjust ? 24u : 20u,
    hasHeightAdjust
      ? [
        new FieldSpec("CORNER", "int32", 4),
        new FieldSpec("DIRECTION", "int32", 4),
        new FieldSpec("HEIGHT", "int32", 4),
        new FieldSpec("HEIGHTADJUST", "float32", 4),
        new FieldSpec("POSX", "int32", 4),
        new FieldSpec("POSZ", "int32", 4),
      ]
      : [
        new FieldSpec("CORNER", "int32", 4),
        new FieldSpec("DIRECTION", "int32", 4),
        new FieldSpec("HEIGHT", "int32", 4),
        new FieldSpec("POSX", "int32", 4),
        new FieldSpec("POSZ", "int32", 4),
      ]);

  private enum ScenerySchemaVariant {
    Base,
    Soaked,
    Wild,
  }

  public enum MalformedStringKind {
    NonAscii,
    OddUtf16,
    UnpairedSurrogate,
    ExcessiveLength,
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
