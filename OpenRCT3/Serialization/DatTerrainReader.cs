// DatTerrainReader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace OpenRCT3.Serialization;

/// <summary>
/// Reads terrain, water, and proven ordinary path/scenery structures from an RCT3 DAT file while
/// consuming every declared value.
/// </summary>
internal static class DatTerrainReader {
  private const string TargetFieldName = "EngineTerrain";
  private const int ExtendedHeaderVersion1Offset = 0x40;
  private const int ExtendedHeaderVersion2Offset = 0x50;
  private const int MaxStructureCount = 4_096;
  private const int MaxSchemaFieldCount = 65_536;
  private const int MaxSchemaDepth = 32;
  private const int MaxDefinitionStringBytes = 4_096;
  private const int MaxEntryCount = 100_000;
  private const int MaxCollectionLength = 100_000;
  private const int MaxTotalCollectionElements = 1_000_000;
  private const int MaxValueReadCount = 8_000_000;
  private const int MaxStringBytes = 16 * 1024 * 1024;
  private const int MaxPayloadBytes = 64 * 1024 * 1024;
  private const int TerrainHeaderBytes = 18;
  private const int TerrainTailBytes = 8;
  private const int WaterManagerHeaderBytes = 6;
  private const int WaterPoolHeaderBytes = 8;
  private const int WaterRecordBytes = 4;
  private const int MaxWaterRecordCount = 2 * byte.MaxValue * byte.MaxValue;
  private static readonly Encoding StrictUtf16 = new UnicodeEncoding(
    bigEndian: false,
    byteOrderMark: false,
    throwOnInvalidBytes: true);
  private static readonly ExpectedField[] GroundPathSchema = [
    new("ColIndex", FieldKind.UInt8, 1),
    new("Direction", FieldKind.UInt8, 1),
    new("PathType", FieldKind.UInt8, 1),
    new("RowIndex", FieldKind.UInt8, 1),
    new("Surface", FieldKind.Reference, 8),
    new("SurfaceType", FieldKind.UInt8, 1),
    new("bool", FieldKind.UInt8, 1),
  ];
  private static readonly ExpectedField[] FlyingPathSchema = [
    new("BaseHeight", FieldKind.Int32, 4),
    new("ColIndex", FieldKind.UInt8, 1),
    new("Direction", FieldKind.UInt8, 1),
    new("PathType", FieldKind.UInt8, 1),
    new("QuantisedHeight", FieldKind.Int32, 4),
    new("RowIndex", FieldKind.UInt8, 1),
    new("SceneryItem", FieldKind.Reference, 8),
    new("SlopeType", FieldKind.UInt8, 1),
    new("Surface", FieldKind.Reference, 8),
    new("SurfaceType", FieldKind.UInt8, 1),
    new("bool", FieldKind.UInt8, 1),
  ];
  private static readonly ExpectedField[] UndergroundFlyingPathSchema = [
    new("BaseHeight", FieldKind.Int32, 4),
    new("ColIndex", FieldKind.UInt8, 1),
    new("Direction", FieldKind.UInt8, 1),
    new("PathType", FieldKind.UInt8, 1),
    new("QuantisedHeight", FieldKind.Int32, 4),
    new("RowIndex", FieldKind.UInt8, 1),
    new("SceneryItem", FieldKind.Reference, 8),
    new("SlopeType", FieldKind.UInt8, 1),
    new("Surface", FieldKind.Reference, 8),
    new("SurfaceType", FieldKind.UInt8, 1),
    new("UndergroundFlag", FieldKind.Bool, 1),
    new("bool", FieldKind.UInt8, 1),
  ];
  private static readonly ExpectedField FenceFlexiColoursField = new(
    "FenceFlexiColours",
    FieldKind.Struct,
    12,
    [
      new("COL0", FieldKind.Int32, 4),
      new("COL1", FieldKind.Int32, 4),
      new("COL2", FieldKind.Int32, 4),
    ]);
  private static readonly ExpectedField[] QueuePathSchema = [
    new("BaseHeight", FieldKind.Int32, 4),
    new("ColIndex", FieldKind.UInt8, 1),
    new("Direction", FieldKind.UInt8, 1),
    new("EndDirection", FieldKind.UInt8, 1),
    new("FenceEntry", FieldKind.ManagedObjectPtr, 8),
    FenceFlexiColoursField,
    new("PathType", FieldKind.UInt8, 1),
    new("QuantisedHeight", FieldKind.Int32, 4),
    new("QueueLine", FieldKind.Reference, 8),
    new("RowIndex", FieldKind.UInt8, 1),
    new("SceneryItem", FieldKind.Reference, 8),
    new("SlopeType", FieldKind.UInt8, 1),
    new("StartDirection", FieldKind.UInt8, 1),
    new("Surface", FieldKind.Reference, 8),
    new("SurfaceType", FieldKind.UInt8, 1),
    new("bool", FieldKind.UInt8, 1),
  ];
  private static readonly ExpectedField[] UndergroundQueuePathSchema = [
    new("BaseHeight", FieldKind.Int32, 4),
    new("ColIndex", FieldKind.UInt8, 1),
    new("Direction", FieldKind.UInt8, 1),
    new("EndDirection", FieldKind.UInt8, 1),
    new("FenceEntry", FieldKind.ManagedObjectPtr, 8),
    FenceFlexiColoursField,
    new("PathType", FieldKind.UInt8, 1),
    new("QuantisedHeight", FieldKind.Int32, 4),
    new("QueueLine", FieldKind.Reference, 8),
    new("RowIndex", FieldKind.UInt8, 1),
    new("SceneryItem", FieldKind.Reference, 8),
    new("SlopeType", FieldKind.UInt8, 1),
    new("StartDirection", FieldKind.UInt8, 1),
    new("Surface", FieldKind.Reference, 8),
    new("SurfaceType", FieldKind.UInt8, 1),
    new("UndergroundFlag", FieldKind.Bool, 1),
    new("bool", FieldKind.UInt8, 1),
  ];
  private static readonly ExpectedField AnimInfoListField = new(
    "AnimInfoList",
    FieldKind.List,
    0,
    [
      new("AutoLoop", FieldKind.Bool, 1),
      new("CurrentAnimation", FieldKind.Int32, 4),
      new("CurrentAnimationTime", FieldKind.Float32, 4),
      new("MarkedForDeletion", FieldKind.Bool, 1),
    ]);
  private static readonly ExpectedField BehaviourArrayField = new(
    "BehaviourArray",
    FieldKind.Array,
    0,
    [new("BehaviourReference", FieldKind.Reference, 8)]);
  private static readonly ExpectedField FlexiColourField = new(
    "FlexiColourField",
    FieldKind.Struct,
    12,
    [
      new("COL0", FieldKind.Int32, 4),
      new("COL1", FieldKind.Int32, 4),
      new("COL2", FieldKind.Int32, 4),
    ]);
  private static readonly ExpectedField FireworkSlotTransformField = new(
    "FireworkSlotTransform",
    FieldKind.Struct,
    8,
    [
      new("Angle", FieldKind.Float32, 4),
      new("Elevation", FieldKind.Float32, 4),
    ]);
  private static readonly ExpectedField LightFlexiColourField = new(
    "LightFlexiColourField",
    FieldKind.Struct,
    12,
    [
      new("COL0", FieldKind.Int32, 4),
      new("COL1", FieldKind.Int32, 4),
      new("COL2", FieldKind.Int32, 4),
    ]);
  private static readonly ExpectedField ParticleSourceEntriesField = new(
    "ParticleSourceEntries",
    FieldKind.Array,
    0,
    [new("SourceRef", FieldKind.Reference, 8)]);
  private static readonly ExpectedField SceneryItemDataField20 = new(
    "SceneryItemDataField",
    FieldKind.Struct,
    20,
    [
      new("CORNER", FieldKind.Int32, 4),
      new("DIRECTION", FieldKind.Int32, 4),
      new("HEIGHT", FieldKind.Int32, 4),
      new("POSX", FieldKind.Int32, 4),
      new("POSZ", FieldKind.Int32, 4),
    ]);
  private static readonly ExpectedField SceneryItemDataField24 = new(
    "SceneryItemDataField",
    FieldKind.Struct,
    24,
    [
      new("CORNER", FieldKind.Int32, 4),
      new("DIRECTION", FieldKind.Int32, 4),
      new("HEIGHT", FieldKind.Int32, 4),
      new("HEIGHTADJUST", FieldKind.Float32, 4),
      new("POSX", FieldKind.Int32, 4),
      new("POSZ", FieldKind.Int32, 4),
    ]);
  private static readonly ExpectedField[] SidDatabaseEntrySchema = [
    new("IsAvailable", FieldKind.Bool, 1),
    new("IsHidden", FieldKind.Bool, 1),
    new("IsInvented", FieldKind.Bool, 1),
    new("OVERLAYFILENAME", FieldKind.String, 0),
    new("SYMBOLNAME", FieldKind.String, 0),
  ];
  private static readonly ExpectedField[] TutorialSidDatabaseEntrySchema = [
    new("IsAvailable", FieldKind.Bool, 1),
    new("IsHidden", FieldKind.Bool, 1),
    new("OVERLAYFILENAME", FieldKind.String, 0),
    new("SYMBOLNAME", FieldKind.String, 0),
  ];
  private static readonly ExpectedField[] BaseSceneryItemSchema = [
    AnimInfoListField,
    new("BREAKFLAGS", FieldKind.Int32, 4),
    new("BREAKTIME", FieldKind.Float32, 4),
    BehaviourArrayField,
    new("CustomUVProvider", FieldKind.Reference, 8),
    new("DATABASEENTRY", FieldKind.Reference, 8),
    new("FORCEABSOLUTEHEIGHT", FieldKind.Bool, 1),
    new("FRAMEOFFSET", FieldKind.Int32, 4),
    FlexiColourField,
    new("HEIGHTOFFSET", FieldKind.Int32, 4),
    new("IsHidden", FieldKind.Bool, 1),
    new("Owner", FieldKind.Reference, 8),
    ParticleSourceEntriesField,
    SceneryItemDataField20,
    new("Vendor", FieldKind.ManagedObjectPtr, 8),
  ];
  private static readonly ExpectedField[] TutorialSceneryItemSchema = [
    AnimInfoListField,
    new("BREAKFLAGS", FieldKind.Int32, 4),
    new("BREAKTIME", FieldKind.Float32, 4),
    BehaviourArrayField,
    new("DATABASEENTRY", FieldKind.Reference, 8),
    new("FORCEABSOLUTEHEIGHT", FieldKind.Bool, 1),
    new("FRAMEOFFSET", FieldKind.Int32, 4),
    FlexiColourField,
    new("HEIGHTOFFSET", FieldKind.Int32, 4),
    new("IsHidden", FieldKind.Bool, 1),
    new("Owner", FieldKind.Reference, 8),
    ParticleSourceEntriesField,
    SceneryItemDataField20,
    new("Vendor", FieldKind.ManagedObjectPtr, 8),
  ];
  private static readonly ExpectedField[] SoakedSceneryItemSchema = [
    AnimInfoListField,
    new("BREAKFLAGS", FieldKind.Int32, 4),
    new("BREAKTIME", FieldKind.Float32, 4),
    BehaviourArrayField,
    new("CustomUVProvider", FieldKind.Reference, 8),
    new("DATABASEENTRY", FieldKind.Reference, 8),
    new("FORCEABSOLUTEHEIGHT", FieldKind.Bool, 1),
    new("FRAMEOFFSET", FieldKind.Int32, 4),
    FireworkSlotTransformField,
    FlexiColourField,
    new("HEIGHTOFFSET", FieldKind.Int32, 4),
    new("IsHidden", FieldKind.Bool, 1),
    LightFlexiColourField,
    new("Owner", FieldKind.Reference, 8),
    ParticleSourceEntriesField,
    SceneryItemDataField24,
    new("Vendor", FieldKind.ManagedObjectPtr, 8),
  ];
  private static readonly ExpectedField[] WildSceneryItemSchema = [
    new("ADSPEND", FieldKind.Float32, 4),
    AnimInfoListField,
    new("BREAKFLAGS", FieldKind.Int32, 4),
    new("BREAKTIME", FieldKind.Float32, 4),
    BehaviourArrayField,
    new("CustomUVProvider", FieldKind.Reference, 8),
    new("DATABASEENTRY", FieldKind.Reference, 8),
    new("FORCEABSOLUTEHEIGHT", FieldKind.Bool, 1),
    new("FRAMEOFFSET", FieldKind.Int32, 4),
    FireworkSlotTransformField,
    FlexiColourField,
    new("HEIGHTOFFSET", FieldKind.Int32, 4),
    new("IsHidden", FieldKind.Bool, 1),
    LightFlexiColourField,
    new("MADINDEX", FieldKind.Int32, 4),
    new("Owner", FieldKind.Reference, 8),
    ParticleSourceEntriesField,
    SceneryItemDataField24,
    new("Vendor", FieldKind.ManagedObjectPtr, 8),
  ];
  private static readonly ExpectedField[] SceneryItemPlacementSingleSchema = [
    FlexiColourField,
    new("Owner", FieldKind.ManagedObjectPtr, 8),
    new("SIDDatabaseEntry", FieldKind.ManagedObjectPtr, 8),
    new("SceneryItem", FieldKind.ManagedObjectPtr, 8),
    SceneryItemDataField20,
  ];

  public static DatTerrainData Read(string path) {
    if (string.IsNullOrWhiteSpace(path))
      throw new ArgumentException("A DAT file path is required.", nameof(path));

    using var stream = File.OpenRead(path);
    return Read(stream);
  }

  public static DatTerrainData Read(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!stream.CanRead) throw new ArgumentException("The DAT stream must be readable.", nameof(stream));

    var reader = new DatBinaryReader(stream);
    var structureCount = ReadStructureCount(reader);
    var structures = ReadStructureDefinitions(reader, structureCount);
    var entryCount = ReadBoundedCount(reader.ReadUInt32(), MaxEntryCount, "entry count");
    var state = new ValueReadState();

    for (var entryIndex = 0; entryIndex < entryCount; entryIndex++) {
      var structureIndex = reader.ReadUInt32();
      if (structureIndex >= Convert.ToUInt32(structures.Length))
        throw new InvalidDataException($"DAT entry {entryIndex} has an invalid structure index.");

      var entryId = reader.ReadUInt64();
      var structure = structures[Convert.ToInt32(structureIndex)];
      if (TryGetPathStructureKind(structure.Name, out var pathKind))
        state.CapturePath(ReadPathEntry(reader, structure, entryId, pathKind, state));
      else if (TryGetSceneryStructureKind(structure.Name, out var sceneryKind))
        state.CaptureScenery(ReadSceneryEntry(reader, structure, entryId, sceneryKind, state));
      else {
        foreach (var field in structure.Fields)
          ReadFieldValue(reader, field, state);
      }
    }

    var terrain = state.Terrain
      ?? throw new InvalidDataException(
        "The DAT file does not contain an EngineTerrain/GE_Terrain field.");
    state.ResolveSceneryDatabaseEntries();
    return AttachDecodedData(
      terrain,
      state.WaterManager,
      state.Paths,
      state.SceneryEntries);
  }

  private static int ReadStructureCount(DatBinaryReader reader) {
    var firstValue = reader.ReadUInt32();
    if (firstValue != 0)
      return ReadBoundedCount(firstValue, MaxStructureCount, "structure count");

    reader.ReadUInt32();
    var version = reader.ReadByte();
    var definitionOffset = version switch {
      0x1A => ExtendedHeaderVersion1Offset,
      0x2A => ExtendedHeaderVersion2Offset,
      _ => throw new InvalidDataException($"Unsupported DAT extended-header version 0x{version:X2}."),
    };
    if (reader.Position > definitionOffset)
      throw new InvalidDataException("The DAT extended header exceeds its definition offset.");

    reader.Skip(Convert.ToInt32(definitionOffset - reader.Position));
    return ReadBoundedCount(reader.ReadUInt32(), MaxStructureCount, "structure count");
  }

  private static DataStructure[] ReadStructureDefinitions(DatBinaryReader reader, int structureCount) {
    var structures = new DataStructure[structureCount];
    var state = new SchemaReadState();
    for (var index = 0; index < structureCount; index++)
      structures[index] = ReadStructureDefinition(reader, state);
    return structures;
  }

  private static DataStructure ReadStructureDefinition(
    DatBinaryReader reader,
    SchemaReadState state) {
    var name = reader.ReadAscii16("structure name");
    var fieldCount = ReadSchemaChildCount(reader.ReadUInt32(), state);
    var fields = new FieldDefinition[fieldCount];
    for (var index = 0; index < fieldCount; index++)
      fields[index] = ReadFieldDefinition(reader, state, depth: 1);
    var structure = new DataStructure(name, fields);
    if (TryGetPathStructureKind(name, out var pathKind))
      ValidatePathStructureSchema(structure, pathKind);
    else if (TryGetSceneryStructureKind(name, out var sceneryKind))
      ValidateSceneryStructureSchema(structure, sceneryKind);
    return structure;
  }

  private static FieldDefinition ReadFieldDefinition(
    DatBinaryReader reader,
    SchemaReadState state,
    int depth) {
    if (depth > MaxSchemaDepth)
      throw new InvalidDataException("DAT schema nesting exceeds the supported depth.");

    state.AddField();
    var name = reader.ReadAscii16("field name");
    var kindName = reader.ReadAscii16("field kind");
    var kind = ParseFieldKind(kindName);
    var fixedSize = reader.ReadUInt32();
    var childCount = ReadSchemaChildCount(reader.ReadUInt32(), state);
    var children = new FieldDefinition[childCount];
    for (var index = 0; index < childCount; index++)
      children[index] = ReadFieldDefinition(reader, state, depth + 1);
    return new FieldDefinition(name, kind, fixedSize, children);
  }

  private static int ReadSchemaChildCount(uint value, SchemaReadState state) {
    var remaining = MaxSchemaFieldCount - state.FieldCount;
    return ReadBoundedCount(value, remaining, "schema field count");
  }

  private static bool TryGetPathStructureKind(
    string name,
    out DatPathStructureKind pathKind
  ) {
    switch (name) {
      case "PathTile":
        pathKind = DatPathStructureKind.PathTile;
        return true;
      case "PathGround":
        pathKind = DatPathStructureKind.PathGround;
        return true;
      case "PathFlying":
        pathKind = DatPathStructureKind.PathFlying;
        return true;
      case "PathQueue":
        pathKind = DatPathStructureKind.PathQueue;
        return true;
      default:
        pathKind = default;
        return false;
    }
  }

  private static bool TryGetSceneryStructureKind(
    string name,
    out SceneryStructureKind sceneryKind
  ) {
    switch (name) {
      case "SIDDatabaseEntry":
        sceneryKind = SceneryStructureKind.SidDatabaseEntry;
        return true;
      case "SceneryItem":
        sceneryKind = SceneryStructureKind.SceneryItem;
        return true;
      case "SceneryItemPlacementSingle":
        sceneryKind = SceneryStructureKind.SceneryItemPlacementSingle;
        return true;
      default:
        sceneryKind = default;
        return false;
    }
  }

  private static void ValidatePathStructureSchema(
    DataStructure structure,
    DatPathStructureKind pathKind
  ) {
    var valid = pathKind switch {
      DatPathStructureKind.PathTile => SchemaMatches(structure.Fields, GroundPathSchema),
      DatPathStructureKind.PathGround => SchemaMatches(structure.Fields, GroundPathSchema),
      DatPathStructureKind.PathFlying =>
        SchemaMatches(structure.Fields, FlyingPathSchema)
        || SchemaMatches(structure.Fields, UndergroundFlyingPathSchema),
      DatPathStructureKind.PathQueue =>
        SchemaMatches(structure.Fields, QueuePathSchema)
        || SchemaMatches(structure.Fields, UndergroundQueuePathSchema),
      _ => false,
    };
    if (!valid)
      throw new InvalidDataException(
        $"DAT structure '{structure.Name}' does not match a supported exact schema.");
  }

  private static void ValidateSceneryStructureSchema(
    DataStructure structure,
    SceneryStructureKind sceneryKind
  ) {
    var valid = sceneryKind switch {
      SceneryStructureKind.SidDatabaseEntry =>
        SchemaMatches(structure.Fields, SidDatabaseEntrySchema)
        || SchemaMatches(structure.Fields, TutorialSidDatabaseEntrySchema),
      SceneryStructureKind.SceneryItem =>
        TryGetSceneryItemSchemaKind(structure, out _),
      SceneryStructureKind.SceneryItemPlacementSingle =>
        SchemaMatches(structure.Fields, SceneryItemPlacementSingleSchema),
      _ => false,
    };
    if (!valid)
      throw new InvalidDataException(
        $"DAT structure '{structure.Name}' does not match a supported exact schema.");
  }

  private static bool TryGetSceneryItemSchemaKind(
    DataStructure structure,
    out SceneryItemSchemaKind schemaKind
  ) {
    if (SchemaMatches(structure.Fields, BaseSceneryItemSchema)) {
      schemaKind = SceneryItemSchemaKind.Base;
      return true;
    }
    if (SchemaMatches(structure.Fields, TutorialSceneryItemSchema)) {
      schemaKind = SceneryItemSchemaKind.Tutorial;
      return true;
    }
    if (SchemaMatches(structure.Fields, SoakedSceneryItemSchema)) {
      schemaKind = SceneryItemSchemaKind.Soaked;
      return true;
    }
    if (SchemaMatches(structure.Fields, WildSceneryItemSchema)) {
      schemaKind = SceneryItemSchemaKind.Wild;
      return true;
    }

    schemaKind = default;
    return false;
  }

  private static bool SchemaMatches(
    FieldDefinition[] actual,
    ExpectedField[] expected
  ) {
    if (actual.Length != expected.Length) return false;
    for (var index = 0; index < actual.Length; index++) {
      var actualField = actual[index];
      var expectedField = expected[index];
      var expectedChildren = expectedField.Children ?? Array.Empty<ExpectedField>();
      if (actualField.Name != expectedField.Name
        || actualField.Kind != expectedField.Kind
        || actualField.FixedSize != expectedField.FixedSize
        || !SchemaMatches(actualField.Children, expectedChildren)) return false;
    }
    return true;
  }

  private static FieldKind ParseFieldKind(string kind) => kind switch {
    "array" => FieldKind.Array,
    "list" => FieldKind.List,
    "bool" => FieldKind.Bool,
    "float32" => FieldKind.Float32,
    "int8" => FieldKind.Int8,
    "int16" => FieldKind.Int16,
    "int32" => FieldKind.Int32,
    "managedobjectptr" => FieldKind.ManagedObjectPtr,
    "matrix44" => FieldKind.Matrix44,
    "orientation" => FieldKind.Orientation,
    "reference" => FieldKind.Reference,
    "uint8" => FieldKind.UInt8,
    "uint16" => FieldKind.UInt16,
    "uint32" => FieldKind.UInt32,
    "vector3" => FieldKind.Vector3,
    "struct" => FieldKind.Struct,
    "string" => FieldKind.String,
    "graphedValue" => FieldKind.GraphedValue,
    "WaterManager" => FieldKind.WaterManager,
    "GE_Terrain" => FieldKind.GETerrain,
    "SkirtTrees" => FieldKind.SkirtTrees,
    "PathTileList" => FieldKind.PathTileList,
    "waypointlist" => FieldKind.WaypointList,
    "flexicachelist" => FieldKind.FlexiCacheList,
    "managedImage" => FieldKind.ManagedImage,
    "pathnodearray" => FieldKind.PathNodeArray,
    "resourcesymbol" => FieldKind.ResourceSymbol,
    "stringTable" => FieldKind.StringTable,
    "BlockingScenery" => FieldKind.BlockingScenery,
    _ => throw new InvalidDataException($"Unsupported DAT field kind '{kind}'."),
  };

  private static void ReadFieldValue(
    DatBinaryReader reader,
    FieldDefinition field,
    ValueReadState state) {
    state.AddValue();

    switch (field.Kind) {
      case FieldKind.Bool:
      case FieldKind.Int8:
      case FieldKind.UInt8:
        reader.Skip(1);
        return;
      case FieldKind.Int16:
      case FieldKind.UInt16:
        reader.Skip(2);
        return;
      case FieldKind.Int32:
      case FieldKind.UInt32:
      case FieldKind.Float32:
        reader.Skip(4);
        return;
      case FieldKind.ManagedObjectPtr:
      case FieldKind.Reference:
        reader.Skip(8);
        return;
      case FieldKind.Vector3:
      case FieldKind.Orientation:
        reader.Skip(12);
        return;
      case FieldKind.Matrix44:
        reader.Skip(64);
        return;
      case FieldKind.String:
        ReadDatString(reader, "string");
        return;
      case FieldKind.Array:
      case FieldKind.List:
        ReadCollection(reader, field, state);
        return;
      case FieldKind.Struct:
        // dat.rs records this size but walks the child definitions directly. Treat it as bounded
        // metadata rather than inventing an unproven container boundary.
        ReadSizedValueLength(reader, field.FixedSize, "structure payload");
        ReadChildValues(reader, field, state);
        return;
      case FieldKind.GraphedValue:
      case FieldKind.SkirtTrees:
      case FieldKind.PathTileList:
      case FieldKind.WaypointList:
      case FieldKind.FlexiCacheList:
      case FieldKind.ManagedImage:
      case FieldKind.PathNodeArray:
      case FieldKind.ResourceSymbol:
      case FieldKind.StringTable:
      case FieldKind.BlockingScenery:
        reader.Skip(ReadSizedValueLength(reader, field.FixedSize, "custom payload"));
        return;
      case FieldKind.WaterManager:
        var waterPayloadSize = ReadSizedValueLength(
          reader,
          field.FixedSize,
          "WaterManager payload");
        var waterManager = ReadWaterManager(reader, waterPayloadSize, state);
        state.CaptureWaterManager(waterManager);
        return;
      case FieldKind.GETerrain:
        var payloadSize = ReadSizedValueLength(reader, field.FixedSize, "GE_Terrain payload");
        if (field.Name == TargetFieldName) {
          var terrain = ReadTerrain(reader, payloadSize);
          state.CaptureTerrain(terrain);
          return;
        }
        reader.Skip(payloadSize);
        return;
      default:
        throw new InvalidDataException($"Unsupported DAT field kind '{field.Kind}'.");
    }
  }

  private static void ReadCollection(
    DatBinaryReader reader,
    FieldDefinition field,
    ValueReadState state) {
    // Like struct sizes, array/list sizes are metadata in dat.rs; element count and child schema
    // determine how many bytes follow.
    ReadBoundedSize(reader.ReadUInt32(), MaxPayloadBytes, "collection payload");
    var length = ReadBoundedCount(reader.ReadUInt32(), MaxCollectionLength, "collection length");
    state.AddCollectionElements(length);

    for (var elementIndex = 0; elementIndex < length; elementIndex++)
      ReadChildValues(reader, field, state);
  }

  private static void ReadChildValues(
    DatBinaryReader reader,
    FieldDefinition field,
    ValueReadState state) {
    foreach (var child in field.Children)
      ReadFieldValue(reader, child, state);
  }

  private static DatPathData ReadPathEntry(
    DatBinaryReader reader,
    DataStructure structure,
    ulong entryId,
    DatPathStructureKind pathKind,
    ValueReadState state
  ) {
    foreach (var field in structure.Fields)
      CountPathFieldValues(field, state);

    return pathKind switch {
      DatPathStructureKind.PathTile => ReadPathTile(reader, entryId),
      DatPathStructureKind.PathGround => ReadPathGround(reader, entryId),
      DatPathStructureKind.PathFlying => ReadPathFlying(
        reader,
        entryId,
        HasUndergroundFlag(structure)),
      DatPathStructureKind.PathQueue => ReadPathQueue(
        reader,
        entryId,
        HasUndergroundFlag(structure)),
      _ => throw new InvalidDataException($"Unsupported path structure kind '{pathKind}'."),
    };
  }

  private static DatSceneryEntryData ReadSceneryEntry(
    DatBinaryReader reader,
    DataStructure structure,
    ulong entryId,
    SceneryStructureKind sceneryKind,
    ValueReadState state
  ) {
    foreach (var field in structure.Fields)
      CountScenerySchemaValues(field, state);

    return sceneryKind switch {
      SceneryStructureKind.SidDatabaseEntry =>
        ReadSidDatabaseEntry(reader, structure, entryId),
      SceneryStructureKind.SceneryItem =>
        ReadSceneryItem(reader, structure, entryId, state),
      SceneryStructureKind.SceneryItemPlacementSingle =>
        ReadSceneryItemPlacementSingle(reader, entryId),
      _ => throw new InvalidDataException(
        $"Unsupported scenery structure kind '{sceneryKind}'."),
    };
  }

  private static void CountScenerySchemaValues(
    FieldDefinition field,
    ValueReadState state
  ) {
    state.AddValue();
    if (field.Kind is FieldKind.Array or FieldKind.List) return;
    foreach (var child in field.Children)
      CountScenerySchemaValues(child, state);
  }

  private static DatSidDatabaseEntryData ReadSidDatabaseEntry(
    DatBinaryReader reader,
    DataStructure structure,
    ulong entryId
  ) {
    var isAvailable = ReadBoolean(reader, "SIDDatabaseEntry IsAvailable");
    var isHidden = ReadBoolean(reader, "SIDDatabaseEntry IsHidden");
    var hasIsInvented = SchemaMatches(structure.Fields, SidDatabaseEntrySchema);
    var isInvented = hasIsInvented
      ? ReadBoolean(reader, "SIDDatabaseEntry IsInvented")
      : (bool?)null;
    var overlayFilename = ReadDatString(reader, "SIDDatabaseEntry OVERLAYFILENAME");
    var symbolName = ReadDatString(reader, "SIDDatabaseEntry SYMBOLNAME");
    return new DatSidDatabaseEntryData(
      entryId,
      isAvailable,
      isHidden,
      isInvented,
      overlayFilename,
      symbolName);
  }

  private static DatSceneryItemData ReadSceneryItem(
    DatBinaryReader reader,
    DataStructure structure,
    ulong entryId,
    ValueReadState state
  ) {
    if (!TryGetSceneryItemSchemaKind(structure, out var schemaKind))
      throw new InvalidDataException(
        "SceneryItem does not match a supported exact schema.");

    var isSoakedOrWild = schemaKind is SceneryItemSchemaKind.Soaked
      or SceneryItemSchemaKind.Wild;
    var variant = schemaKind switch {
      SceneryItemSchemaKind.Soaked => DatSceneryItemVariant.Soaked,
      SceneryItemSchemaKind.Wild => DatSceneryItemVariant.Wild,
      _ => DatSceneryItemVariant.Base,
    };
    var adSpend = schemaKind == SceneryItemSchemaKind.Wild
      ? ReadFiniteSingle(reader, "SceneryItem ADSPEND")
      : (float?)null;
    var animInfoList = ReadAnimationInfoList(reader, state);
    var breakFlags = reader.ReadInt32();
    var breakTime = ReadFiniteSingle(reader, "SceneryItem BREAKTIME");
    var behaviourArray = ReadReferenceCollection(
      reader,
      state,
      "SceneryItem BehaviourArray");
    var customUvProvider = schemaKind != SceneryItemSchemaKind.Tutorial
      ? reader.ReadUInt64()
      : (ulong?)null;
    var databaseEntry = reader.ReadUInt64();
    var forceAbsoluteHeight = ReadBoolean(reader, "SceneryItem FORCEABSOLUTEHEIGHT");
    var frameOffset = reader.ReadInt32();
    var fireworkSlotTransform = isSoakedOrWild
      ? ReadFireworkSlotTransform(reader)
      : (DatFireworkSlotTransform?)null;
    var flexiColourField = ReadSceneryFlexiColour(reader);
    var heightOffset = reader.ReadInt32();
    var isHidden = ReadBoolean(reader, "SceneryItem IsHidden");
    var lightFlexiColourField = isSoakedOrWild
      ? ReadSceneryFlexiColour(reader)
      : (DatSceneryFlexiColour?)null;
    var madIndex = schemaKind == SceneryItemSchemaKind.Wild
      ? reader.ReadInt32()
      : (int?)null;
    var owner = reader.ReadUInt64();
    var particleSourceEntries = ReadReferenceCollection(
      reader,
      state,
      "SceneryItem ParticleSourceEntries");
    var sceneryItemDataField = ReadSceneryItemDataField(
      reader,
      hasHeightAdjust: isSoakedOrWild,
      "SceneryItem SceneryItemDataField");
    var vendor = reader.ReadUInt64();
    return new DatSceneryItemData(
      entryId,
      variant,
      adSpend,
      animInfoList,
      breakFlags,
      breakTime,
      behaviourArray,
      customUvProvider,
      databaseEntry,
      forceAbsoluteHeight,
      frameOffset,
      fireworkSlotTransform,
      flexiColourField,
      heightOffset,
      isHidden,
      lightFlexiColourField,
      madIndex,
      owner,
      particleSourceEntries,
      sceneryItemDataField,
      vendor);
  }

  private static DatSceneryItemPlacementSingleData ReadSceneryItemPlacementSingle(
    DatBinaryReader reader,
    ulong entryId
  ) {
    var flexiColourField = ReadSceneryFlexiColour(reader);
    var owner = reader.ReadUInt64();
    var sidDatabaseEntry = reader.ReadUInt64();
    var sceneryItem = reader.ReadUInt64();
    var sceneryItemDataField = ReadSceneryItemDataField(
      reader,
      hasHeightAdjust: false,
      "SceneryItemPlacementSingle SceneryItemDataField");
    return new DatSceneryItemPlacementSingleData(
      entryId,
      flexiColourField,
      owner,
      sidDatabaseEntry,
      sceneryItem,
      sceneryItemDataField);
  }

  private static DatSceneryAnimationInfo[] ReadAnimationInfoList(
    DatBinaryReader reader,
    ValueReadState state
  ) {
    var length = ReadCollectionLength(reader, state, "SceneryItem AnimInfoList");
    state.AddValues(checked(length * 4));
    var values = new DatSceneryAnimationInfo[length];
    for (var index = 0; index < length; index++) {
      var autoLoop = ReadBoolean(reader, $"SceneryItem AnimInfoList[{index}] AutoLoop");
      var currentAnimation = reader.ReadInt32();
      var currentAnimationTime = ReadFiniteSingle(
        reader,
        $"SceneryItem AnimInfoList[{index}] CurrentAnimationTime");
      var markedForDeletion = ReadBoolean(
        reader,
        $"SceneryItem AnimInfoList[{index}] MarkedForDeletion");
      values[index] = new DatSceneryAnimationInfo(
        autoLoop,
        currentAnimation,
        currentAnimationTime,
        markedForDeletion);
    }
    return values;
  }

  private static ulong[] ReadReferenceCollection(
    DatBinaryReader reader,
    ValueReadState state,
    string description
  ) {
    var length = ReadCollectionLength(reader, state, description);
    state.AddValues(length);
    var values = new ulong[length];
    for (var index = 0; index < length; index++)
      values[index] = reader.ReadUInt64();
    return values;
  }

  private static int ReadCollectionLength(
    DatBinaryReader reader,
    ValueReadState state,
    string description
  ) {
    ReadBoundedSize(reader.ReadUInt32(), MaxPayloadBytes, $"{description} payload");
    var length = ReadBoundedCount(
      reader.ReadUInt32(),
      MaxCollectionLength,
      $"{description} length");
    state.AddCollectionElements(length);
    return length;
  }

  private static DatFireworkSlotTransform ReadFireworkSlotTransform(
    DatBinaryReader reader
  ) => new(
    ReadFiniteSingle(reader, "SceneryItem FireworkSlotTransform Angle"),
    ReadFiniteSingle(reader, "SceneryItem FireworkSlotTransform Elevation"));

  private static DatSceneryFlexiColour ReadSceneryFlexiColour(
    DatBinaryReader reader
  ) => new(
    reader.ReadInt32(),
    reader.ReadInt32(),
    reader.ReadInt32());

  private static DatSceneryItemDataField ReadSceneryItemDataField(
    DatBinaryReader reader,
    bool hasHeightAdjust,
    string description
  ) {
    var corner = reader.ReadInt32();
    var direction = reader.ReadInt32();
    var height = reader.ReadInt32();
    var heightAdjust = hasHeightAdjust
      ? ReadFiniteSingle(reader, $"{description} HEIGHTADJUST")
      : (float?)null;
    return new DatSceneryItemDataField(
      corner,
      direction,
      height,
      heightAdjust,
      reader.ReadInt32(),
      reader.ReadInt32());
  }

  private static float ReadFiniteSingle(
    DatBinaryReader reader,
    string description
  ) {
    var value = reader.ReadSingle();
    if (!float.IsFinite(value))
      throw new InvalidDataException($"DAT {description} contains a non-finite value.");
    return value;
  }

  private static void CountPathFieldValues(
    FieldDefinition field,
    ValueReadState state
  ) {
    state.AddValue();
    foreach (var child in field.Children)
      CountPathFieldValues(child, state);
  }

  private static bool HasUndergroundFlag(DataStructure structure) =>
    structure.Fields.Length >= 2
    && structure.Fields[^2].Name == "UndergroundFlag";

  private static DatPathTileData ReadPathTile(DatBinaryReader reader, ulong entryId) =>
    new(
      entryId,
      reader.ReadByte(),
      reader.ReadByte(),
      reader.ReadByte(),
      reader.ReadByte(),
      reader.ReadUInt64(),
      reader.ReadByte(),
      reader.ReadByte());

  private static DatPathGroundData ReadPathGround(DatBinaryReader reader, ulong entryId) =>
    new(
      entryId,
      reader.ReadByte(),
      reader.ReadByte(),
      reader.ReadByte(),
      reader.ReadByte(),
      reader.ReadUInt64(),
      reader.ReadByte(),
      reader.ReadByte());

  private static DatPathFlyingData ReadPathFlying(
    DatBinaryReader reader,
    ulong entryId,
    bool hasUndergroundFlag
  ) {
    var baseHeight = reader.ReadInt32();
    var colIndex = reader.ReadByte();
    var direction = reader.ReadByte();
    var pathType = reader.ReadByte();
    var quantisedHeight = reader.ReadInt32();
    var rowIndex = reader.ReadByte();
    var sceneryItem = reader.ReadUInt64();
    var slopeType = reader.ReadByte();
    var surface = reader.ReadUInt64();
    var surfaceType = reader.ReadByte();
    var undergroundFlag = hasUndergroundFlag
      ? ReadBoolean(reader, "PathFlying UndergroundFlag")
      : (bool?)null;
    var boolValue = reader.ReadByte();
    return new DatPathFlyingData(
      entryId,
      baseHeight,
      colIndex,
      direction,
      pathType,
      quantisedHeight,
      rowIndex,
      sceneryItem,
      slopeType,
      surface,
      surfaceType,
      undergroundFlag,
      boolValue);
  }

  private static DatPathQueueData ReadPathQueue(
    DatBinaryReader reader,
    ulong entryId,
    bool hasUndergroundFlag
  ) {
    var baseHeight = reader.ReadInt32();
    var colIndex = reader.ReadByte();
    var direction = reader.ReadByte();
    var endDirection = reader.ReadByte();
    var fenceEntry = reader.ReadUInt64();
    var fenceFlexiColours = new DatFenceFlexiColours(
      reader.ReadInt32(),
      reader.ReadInt32(),
      reader.ReadInt32());
    var pathType = reader.ReadByte();
    var quantisedHeight = reader.ReadInt32();
    var queueLine = reader.ReadUInt64();
    var rowIndex = reader.ReadByte();
    var sceneryItem = reader.ReadUInt64();
    var slopeType = reader.ReadByte();
    var startDirection = reader.ReadByte();
    var surface = reader.ReadUInt64();
    var surfaceType = reader.ReadByte();
    var undergroundFlag = hasUndergroundFlag
      ? ReadBoolean(reader, "PathQueue UndergroundFlag")
      : (bool?)null;
    var boolValue = reader.ReadByte();
    return new DatPathQueueData(
      entryId,
      baseHeight,
      colIndex,
      direction,
      endDirection,
      fenceEntry,
      fenceFlexiColours,
      pathType,
      quantisedHeight,
      queueLine,
      rowIndex,
      sceneryItem,
      slopeType,
      startDirection,
      surface,
      surfaceType,
      undergroundFlag,
      boolValue);
  }

  private static bool ReadBoolean(DatBinaryReader reader, string description) {
    var value = reader.ReadByte();
    if (value > 1)
      throw new InvalidDataException($"DAT {description} contains an invalid bool value.");
    return value != 0;
  }

  private static string ReadDatString(
    DatBinaryReader reader,
    string description
  ) {
    var length = ReadBoundedSize(
      reader.ReadUInt32(),
      MaxStringBytes,
      $"{description} length");
    var bytes = reader.ReadBytes(length);
    var hasUtf16Marker = length >= sizeof(uint)
      && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0xEFEFEFEF;
    if (!hasUtf16Marker) {
      foreach (var value in bytes) {
        if (value > 0x7F)
          throw new InvalidDataException($"DAT {description} is not ASCII.");
      }
      return Encoding.ASCII.GetString(bytes);
    }

    var utf16Length = length - sizeof(uint);
    if ((utf16Length & 1) != 0)
      throw new InvalidDataException(
        $"DAT {description} has an invalid UTF-16 byte length.");
    try {
      return StrictUtf16.GetString(bytes, sizeof(uint), utf16Length);
    }
    catch (DecoderFallbackException exception) {
      throw new InvalidDataException(
        $"DAT {description} contains malformed UTF-16 data.",
        exception);
    }
  }

  private static int ReadSizedValueLength(
    DatBinaryReader reader,
    uint fixedSize,
    string description) {
    var size = fixedSize == 0 ? reader.ReadUInt32() : fixedSize;
    return ReadBoundedSize(size, MaxPayloadBytes, description);
  }

  private static DatWaterManagerData ReadWaterManager(
    DatBinaryReader reader,
    int payloadSize,
    ValueReadState state) {
    var payloadStart = reader.Position;
    EnsurePayloadBytesRemaining(
      reader,
      payloadStart,
      payloadSize,
      WaterManagerHeaderBytes,
      "WaterManager header");

    var width = Convert.ToInt32(reader.ReadByte());
    var height = Convert.ToInt32(reader.ReadByte());
    if (width == 0 || height == 0)
      throw new InvalidDataException("WaterManager dimensions must be positive.");

    var poolCount = ReadBoundedCount(
      reader.ReadUInt32(),
      MaxCollectionLength,
      "WaterManager pool count");
    state.AddCollectionElements(poolCount);

    var minimumPayloadSize = Convert.ToInt64(WaterManagerHeaderBytes)
      + (Convert.ToInt64(poolCount) * WaterPoolHeaderBytes);
    if (minimumPayloadSize > payloadSize)
      throw new InvalidDataException("WaterManager pool count exceeds its payload size.");

    var pools = new DatWaterPoolData[poolCount];
    var maximumRecordCount = checked(width * height * 2);
    for (var poolIndex = 0; poolIndex < poolCount; poolIndex++) {
      EnsurePayloadBytesRemaining(
        reader,
        payloadStart,
        payloadSize,
        WaterPoolHeaderBytes,
        $"WaterManager pool {poolIndex} header");

      var poolHeight = reader.ReadSingle();
      if (!float.IsFinite(poolHeight))
        throw new InvalidDataException(
          $"WaterManager pool {poolIndex} contains a non-finite height.");

      var recordCount = ReadBoundedCount(
        reader.ReadUInt32(),
        MaxWaterRecordCount,
        $"WaterManager pool {poolIndex} record count");
      if (recordCount > maximumRecordCount)
        throw new InvalidDataException(
          $"WaterManager pool {poolIndex} record count exceeds its grid bounds.");
      state.AddCollectionElements(recordCount);

      var recordPayloadSize = checked(recordCount * WaterRecordBytes);
      EnsurePayloadBytesRemaining(
        reader,
        payloadStart,
        payloadSize,
        recordPayloadSize,
        $"WaterManager pool {poolIndex} records");

      var records = new DatWaterRecord[recordCount];
      HashSet<int>? occupiedTriangles = null;
      for (var recordIndex = 0; recordIndex < recordCount; recordIndex++) {
        var x = reader.ReadByte();
        var y = reader.ReadByte();
        var triangle = reader.ReadByte();
        var vertexMask = reader.ReadByte();
        if (x >= width || y >= height)
          throw new InvalidDataException(
            $"WaterManager pool {poolIndex} record {recordIndex} is outside the manager bounds.");
        if (triangle > 1)
          throw new InvalidDataException(
            $"WaterManager pool {poolIndex} record {recordIndex} has an invalid triangle.");
        if (vertexMask is < 1 or > 7)
          throw new InvalidDataException(
            $"WaterManager pool {poolIndex} record {recordIndex} has an invalid vertex mask.");

        var triangleAddress = ((((Convert.ToInt32(y) * width) + x) * 2) + triangle);
        occupiedTriangles ??= new HashSet<int>();
        if (!occupiedTriangles.Add(triangleAddress))
          throw new InvalidDataException(
            $"WaterManager pool {poolIndex} contains a duplicate terrain triangle.");
        records[recordIndex] = new DatWaterRecord(x, y, triangle, vertexMask);
      }

      pools[poolIndex] = new DatWaterPoolData(poolHeight, records);
    }

    var consumed = reader.Position - payloadStart;
    if (consumed != payloadSize)
      throw new InvalidDataException(
        "WaterManager payload size does not exactly match its decoded contents.");
    return new DatWaterManagerData(width, height, pools);
  }

  private static void EnsurePayloadBytesRemaining(
    DatBinaryReader reader,
    long payloadStart,
    int payloadSize,
    int requiredBytes,
    string description) {
    var consumed = reader.Position - payloadStart;
    if (consumed < 0 || requiredBytes < 0 || consumed + requiredBytes > payloadSize)
      throw new InvalidDataException($"The {description} exceeds its declared payload size.");
  }

  private static DatTerrainData AttachDecodedData(
    DatTerrainData terrain,
    DatWaterManagerData? waterManager,
    IReadOnlyList<DatPathData> paths,
    IReadOnlyList<DatSceneryEntryData> sceneryEntries
  ) {
    if (waterManager != null
      && (waterManager.Width != terrain.Width || waterManager.Height != terrain.Height))
      throw new InvalidDataException(
        "WaterManager dimensions do not match the decoded GE_Terrain dimensions.");

    foreach (var path in paths) {
      if (path.ColIndex >= terrain.Width || path.RowIndex >= terrain.Height)
        throw new InvalidDataException(
          $"DAT path entry {path.EntryId} is outside the decoded GE_Terrain dimensions.");
    }

    if (waterManager == null && paths.Count == 0 && sceneryEntries.Count == 0) return terrain;

    var cells = new DatTerrainCell[terrain.Cells.Count];
    for (var index = 0; index < cells.Length; index++)
      cells[index] = terrain.Cells[index];
    return new DatTerrainData(
      terrain.Width,
      terrain.Height,
      terrain.OriginX,
      terrain.OriginY,
      terrain.TileSizeX,
      terrain.TileSizeY,
      cells,
      waterManager,
      [.. paths],
      [.. sceneryEntries]);
  }

  private static DatTerrainData ReadTerrain(DatBinaryReader reader, int payloadSize) {
    if (payloadSize < TerrainHeaderBytes)
      throw new InvalidDataException("The GE_Terrain payload is too small for its header.");

    var width = Convert.ToInt32(reader.ReadByte());
    var height = Convert.ToInt32(reader.ReadByte());
    if (width == 0 || height == 0)
      throw new InvalidDataException("GE_Terrain dimensions must be positive.");

    var originX = reader.ReadSingle();
    var originY = reader.ReadSingle();
    var tileSizeX = reader.ReadSingle();
    var tileSizeY = reader.ReadSingle();
    if (!float.IsFinite(originX) || !float.IsFinite(originY)
      || !float.IsFinite(tileSizeX) || !float.IsFinite(tileSizeY))
      throw new InvalidDataException("GE_Terrain metadata contains a non-finite value.");
    if (tileSizeX <= 0 || tileSizeY <= 0)
      throw new InvalidDataException("GE_Terrain tile sizes must be positive.");

    var cellCount = checked(width * height);
    var layout = DetermineTerrainLayout(payloadSize, cellCount);
    var cells = new DatTerrainCell[cellCount];
    for (var index = 0; index < cellCount; index++) {
      var southWest = reader.ReadSingle();
      var southEast = reader.ReadSingle();
      var northWest = reader.ReadSingle();
      var northEast = reader.ReadSingle();
      if (!float.IsFinite(southWest) || !float.IsFinite(southEast)
        || !float.IsFinite(northWest) || !float.IsFinite(northEast))
        throw new InvalidDataException($"GE_Terrain cell {index} contains a non-finite height.");

      var surfaceIndex = reader.ReadByte();
      var cliffIndex = reader.ReadByte();
      reader.Skip(layout.RecordSize - 18);
      cells[index] = new DatTerrainCell(
        southWest,
        southEast,
        northWest,
        northEast,
        surfaceIndex,
        cliffIndex);
    }

    reader.Skip(layout.TailSize);
    return new DatTerrainData(
      width,
      height,
      originX,
      originY,
      tileSizeX,
      tileSizeY,
      cells);
  }

  private static TerrainLayout DetermineTerrainLayout(int payloadSize, int cellCount) {
    var matches24 = MatchesTerrainLayout(payloadSize, cellCount, recordSize: 24, tailSize: 0);
    var matches20 = MatchesTerrainLayout(payloadSize, cellCount, recordSize: 20, tailSize: 0);
    var matches24WithTail = MatchesTerrainLayout(
      payloadSize,
      cellCount,
      recordSize: 24,
      tailSize: TerrainTailBytes);
    var matches20WithTail = MatchesTerrainLayout(
      payloadSize,
      cellCount,
      recordSize: 20,
      tailSize: TerrainTailBytes);
    var matchCount = 0;
    if (matches24) matchCount++;
    if (matches20) matchCount++;
    if (matches24WithTail) matchCount++;
    if (matches20WithTail) matchCount++;
    if (matchCount > 1)
      throw new InvalidDataException("GE_Terrain payload has an ambiguous cell layout.");

    if (matches24)
      return new TerrainLayout(recordSize: 24, tailSize: 0);
    if (matches20)
      return new TerrainLayout(recordSize: 20, tailSize: 0);
    if (matches24WithTail)
      return new TerrainLayout(recordSize: 24, tailSize: TerrainTailBytes);
    if (matches20WithTail)
      return new TerrainLayout(recordSize: 20, tailSize: TerrainTailBytes);

    throw new InvalidDataException(
      "GE_Terrain payload size does not match a supported 20-byte or 24-byte cell layout.");
  }

  private static bool MatchesTerrainLayout(
    int payloadSize,
    int cellCount,
    int recordSize,
    int tailSize) {
    var expected = Convert.ToInt64(TerrainHeaderBytes)
      + (Convert.ToInt64(cellCount) * recordSize)
      + tailSize;
    return expected == payloadSize;
  }

  private static int ReadBoundedCount(uint value, int maximum, string description) {
    if (maximum < 0 || value > Convert.ToUInt32(maximum))
      throw new InvalidDataException($"DAT {description} exceeds the supported limit.");
    return Convert.ToInt32(value);
  }

  private static int ReadBoundedSize(uint value, int maximum, string description) {
    if (value > Convert.ToUInt32(maximum))
      throw new InvalidDataException(
        $"DAT {description} value {value} exceeds the supported limit.");
    return Convert.ToInt32(value);
  }

  private enum SceneryStructureKind {
    SidDatabaseEntry,
    SceneryItem,
    SceneryItemPlacementSingle,
  }

  private enum SceneryItemSchemaKind {
    Base,
    Tutorial,
    Soaked,
    Wild,
  }

  private enum FieldKind {
    Bool,
    Int8,
    Int16,
    Int32,
    UInt8,
    UInt16,
    UInt32,
    Float32,
    Vector3,
    Matrix44,
    Orientation,
    ManagedObjectPtr,
    Reference,
    String,
    Array,
    List,
    Struct,
    GraphedValue,
    WaterManager,
    GETerrain,
    SkirtTrees,
    PathTileList,
    WaypointList,
    FlexiCacheList,
    ManagedImage,
    PathNodeArray,
    ResourceSymbol,
    StringTable,
    BlockingScenery,
  }

  private sealed class DataStructure {
    public string Name { get; }
    public FieldDefinition[] Fields { get; }

    public DataStructure(string name, FieldDefinition[] fields) {
      Name = name;
      Fields = fields;
    }
  }

  private sealed class FieldDefinition {
    public string Name { get; }
    public FieldKind Kind { get; }
    public uint FixedSize { get; }
    public FieldDefinition[] Children { get; }

    public FieldDefinition(
      string name,
      FieldKind kind,
      uint fixedSize,
      FieldDefinition[] children) {
      Name = name;
      Kind = kind;
      FixedSize = fixedSize;
      Children = children;
    }
  }

  private readonly record struct ExpectedField(
    string Name,
    FieldKind Kind,
    uint FixedSize,
    ExpectedField[]? Children = null);

  private sealed class SchemaReadState {
    public int FieldCount { get; private set; }

    public void AddField() {
      if (FieldCount >= MaxSchemaFieldCount)
        throw new InvalidDataException("DAT schema field count exceeds the supported limit.");
      FieldCount++;
    }
  }

  private sealed class ValueReadState {
    private int _collectionElementCount;
    private int _valueReadCount;
    private readonly List<DatPathData> _paths = [];
    private readonly List<DatSceneryEntryData> _sceneryEntries = [];

    public DatTerrainData? Terrain { get; private set; }
    public DatWaterManagerData? WaterManager { get; private set; }
    public IReadOnlyList<DatPathData> Paths => _paths;
    public IReadOnlyList<DatSceneryEntryData> SceneryEntries => _sceneryEntries;

    public void AddCollectionElements(int count) {
      if (count > MaxTotalCollectionElements - _collectionElementCount)
        throw new InvalidDataException("DAT collection element count exceeds the supported limit.");
      _collectionElementCount += count;
    }

    public void AddValue() {
      AddValues(1);
    }

    public void AddValues(int count) {
      if (count < 0 || count > MaxValueReadCount - _valueReadCount)
        throw new InvalidDataException("DAT value count exceeds the supported limit.");
      _valueReadCount += count;
    }

    public void CaptureTerrain(DatTerrainData terrain) {
      Terrain ??= terrain;
    }

    public void CaptureWaterManager(DatWaterManagerData waterManager) {
      WaterManager ??= waterManager;
    }

    public void CapturePath(DatPathData path) => _paths.Add(path);

    public void CaptureScenery(DatSceneryEntryData sceneryEntry) =>
      _sceneryEntries.Add(sceneryEntry);

    public void ResolveSceneryDatabaseEntries() {
      var sidEntries = new Dictionary<ulong, DatSidDatabaseEntryData>();
      foreach (var entry in _sceneryEntries) {
        if (entry is not DatSidDatabaseEntryData sidEntry) continue;
        if (!sidEntries.TryAdd(sidEntry.EntryId, sidEntry))
          throw new InvalidDataException(
            $"DAT contains duplicate SIDDatabaseEntry ID {sidEntry.EntryId}.");
      }

      foreach (var entry in _sceneryEntries) {
        if (entry is not DatSceneryItemData sceneryItem
          || sceneryItem.DatabaseEntry == 0
          || !sidEntries.TryGetValue(sceneryItem.DatabaseEntry, out var sidEntry)) continue;
        sceneryItem.ResolveDatabaseEntry(sidEntry);
      }
    }
  }

  private readonly struct TerrainLayout {
    public int RecordSize { get; }
    public int TailSize { get; }

    public TerrainLayout(int recordSize, int tailSize) {
      RecordSize = recordSize;
      TailSize = tailSize;
    }
  }

  private sealed class DatBinaryReader {
    private readonly Stream _stream;

    public long Position { get; private set; }

    public DatBinaryReader(Stream stream) {
      _stream = stream;
    }

    public byte ReadByte() {
      Span<byte> bytes = stackalloc byte[1];
      ReadExact(bytes);
      return bytes[0];
    }

    public ushort ReadUInt16() {
      Span<byte> bytes = stackalloc byte[2];
      ReadExact(bytes);
      return BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    }

    public int ReadInt32() {
      Span<byte> bytes = stackalloc byte[4];
      ReadExact(bytes);
      return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    public uint ReadUInt32() {
      Span<byte> bytes = stackalloc byte[4];
      ReadExact(bytes);
      return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    public ulong ReadUInt64() {
      Span<byte> bytes = stackalloc byte[8];
      ReadExact(bytes);
      return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    public float ReadSingle() {
      Span<byte> bytes = stackalloc byte[4];
      ReadExact(bytes);
      return BinaryPrimitives.ReadSingleLittleEndian(bytes);
    }

    public byte[] ReadBytes(int count) {
      if (count < 0) throw new InvalidDataException("DAT byte count is invalid.");
      var bytes = new byte[count];
      ReadExact(bytes);
      return bytes;
    }

    public string ReadAscii16(string description) {
      var length = ReadBoundedCount(
        ReadUInt16(),
        MaxDefinitionStringBytes,
        description);
      var bytes = new byte[length];
      ReadExact(bytes);
      foreach (var value in bytes) {
        if (value > 0x7F)
          throw new InvalidDataException($"DAT {description} is not ASCII.");
      }
      return Encoding.ASCII.GetString(bytes);
    }

    public void Skip(int count) {
      if (count < 0) throw new InvalidDataException("DAT skip length is invalid.");

      Span<byte> buffer = stackalloc byte[4_096];
      var remaining = count;
      while (remaining > 0) {
        var chunkLength = Math.Min(remaining, buffer.Length);
        ReadExact(buffer[..chunkLength]);
        remaining -= chunkLength;
      }
    }

    private void ReadExact(Span<byte> buffer) {
      var offset = 0;
      while (offset < buffer.Length) {
        var read = _stream.Read(buffer[offset..]);
        if (read == 0) throw new InvalidDataException("Unexpected end of DAT stream.");
        offset += read;
      }

      if (Position > long.MaxValue - buffer.Length)
        throw new InvalidDataException("DAT stream position overflowed.");
      Position += buffer.Length;
    }
  }
}
