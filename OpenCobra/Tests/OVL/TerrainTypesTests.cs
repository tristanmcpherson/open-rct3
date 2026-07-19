using System.Buffers.Binary;
using NUnit.Framework;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class TerrainTypesTests {
  [Test]
  public void Decode_PreservesFieldsAndTypedReferences() {
    var bytes = ValidRecord();
    WriteUInt32(bytes, 4, 7);
    WriteUInt32(bytes, 12, 42);
    WriteUInt32(bytes, 32, 0x11223344);
    WriteUInt32(bytes, 36, 0x55667788);
    WriteSingle(bytes, 40, 0.25f);
    WriteSingle(bytes, 44, 0.5f);
    WriteSingle(bytes, 48, 0.3f);
    WriteSingle(bytes, 52, -0.125f);
    WriteSingle(bytes, 56, 0.75f);

    var terrain = TerrainTypes.Decode("TerrainGrass", bytes, ResolveReference);

    using (Assert.EnterMultipleScope()) {
      Assert.That(terrain.Name, Is.EqualTo("TerrainGrass"));
      Assert.That(terrain.DescriptionName, Is.EqualTo("TerrainGrassDescription"));
      Assert.That(terrain.Description.Type, Is.EqualTo(FileType.Text));
      Assert.That(terrain.IconName, Is.EqualTo("TerrainGrassIcon"));
      Assert.That(terrain.Icon.Type, Is.EqualTo(FileType.GuiSkinItem));
      Assert.That(terrain.TextureRef, Is.EqualTo("Terrain_00"));
      Assert.That(terrain.Texture.QualifiedName, Is.EqualTo("Terrain_00:tex"));
      Assert.That(terrain.Version, Is.EqualTo(1));
      Assert.That(terrain.Addon, Is.EqualTo(TerrainAddon.Soaked));
      Assert.That(terrain.Number, Is.EqualTo(42));
      Assert.That(terrain.Type, Is.EqualTo(TerrainTypeKind.GroundBlended));
      Assert.That(terrain.Parameters.Color01, Is.EqualTo(0x11223344));
      Assert.That(terrain.Parameters.Color02, Is.EqualTo(0x55667788));
      Assert.That(terrain.Parameters.InvWidth, Is.EqualTo(0.25f));
      Assert.That(terrain.Parameters.InvHeight, Is.EqualTo(0.5f));
      Assert.That(terrain.Unknowns.Unk02, Is.EqualTo(7));
      Assert.That(terrain.Unknowns.Unk13, Is.EqualTo(0.3f));
      Assert.That(terrain.Unknowns.Unk14, Is.EqualTo(-0.125f));
      Assert.That(terrain.Unknowns.Unk15, Is.EqualTo(0.75f));
    }
  }

  [TestCase(0)]
  [TestCase(TerrainTypes.RecordSize - 1)]
  [TestCase(TerrainTypes.RecordSize + 1)]
  public void Decode_RequiresExactRecordSize(int size) {
    Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainTypes.Decode("bad-size", new byte[size], ResolveReference)));
  }

  [TestCase(0u)]
  [TestCase(2u)]
  [TestCase(uint.MaxValue)]
  public void Decode_RejectsUnsupportedVersion(uint version) {
    var bytes = ValidRecord();
    WriteUInt32(bytes, 0, version);

    Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainTypes.Decode("bad-version", bytes, ResolveReference)));
  }

  [TestCase(2u)]
  [TestCase(uint.MaxValue)]
  public void Decode_RejectsInvalidAddon(uint addon) {
    var bytes = ValidRecord();
    WriteUInt32(bytes, 8, addon);

    Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainTypes.Decode("bad-addon", bytes, ResolveReference)));
  }

  [TestCase(3u)]
  [TestCase(uint.MaxValue)]
  public void Decode_RejectsInvalidTerrainType(uint type) {
    var bytes = ValidRecord();
    WriteUInt32(bytes, 16, type);

    Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainTypes.Decode("bad-type", bytes, ResolveReference)));
  }

  [TestCase(TerrainAddon.BaseGame, TerrainTypeKind.GroundUnblended)]
  [TestCase(TerrainAddon.BaseGame, TerrainTypeKind.Cliff)]
  [TestCase(TerrainAddon.BaseGame, TerrainTypeKind.GroundBlended)]
  [TestCase(TerrainAddon.Soaked, TerrainTypeKind.GroundUnblended)]
  [TestCase(TerrainAddon.Soaked, TerrainTypeKind.Cliff)]
  [TestCase(TerrainAddon.Soaked, TerrainTypeKind.GroundBlended)]
  public void Decode_AcceptsKnownEnumBoundaries(TerrainAddon addon, TerrainTypeKind type) {
    var bytes = ValidRecord();
    WriteUInt32(bytes, 8, Convert.ToUInt32(addon));
    WriteUInt32(bytes, 16, Convert.ToUInt32(type));

    var terrain = TerrainTypes.Decode("known-enums", bytes, ResolveReference);

    Assert.That(terrain.Addon, Is.EqualTo(addon));
    Assert.That(terrain.Type, Is.EqualTo(type));
  }

  [TestCase(40)]
  [TestCase(44)]
  [TestCase(48)]
  [TestCase(52)]
  [TestCase(56)]
  public void Decode_RejectsNonFiniteFloats(int offset) {
    var bytes = ValidRecord();
    WriteSingle(bytes, offset, float.NaN);

    Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainTypes.Decode("non-finite", bytes, ResolveReference)));
  }

  [TestCase(40, 0f)]
  [TestCase(40, -0.1f)]
  [TestCase(44, 0f)]
  [TestCase(44, -0.1f)]
  public void Decode_RejectsNonPositiveInverseDimensions(int offset, float value) {
    var bytes = ValidRecord();
    WriteSingle(bytes, offset, value);

    Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainTypes.Decode("bad-scale", bytes, ResolveReference)));
  }

  [Test]
  public void Decode_RejectsWrongReferenceType() {
    Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainTypes.Decode(
        "wrong-reference",
        ValidRecord(),
        (_, _) => new TerrainResourceReference("wrong", FileType.Texture)
      )));
  }

  [Test]
  public void Decode_RejectsEmptyReferenceName() {
    Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainTypes.Decode(
        "empty-reference",
        ValidRecord(),
        (_, type) => new TerrainResourceReference("", type)
      )));
  }

  private static TerrainResourceReference ResolveReference(int offset, FileType expectedType) =>
    offset switch {
      20 => new TerrainResourceReference("Terrain_00", FileType.Texture),
      24 => new TerrainResourceReference("TerrainGrassDescription", FileType.Text),
      28 => new TerrainResourceReference("TerrainGrassIcon", FileType.GuiSkinItem),
      _ => throw new AssertionException($"Unexpected reference offset {offset} for {expectedType}.")
    };

  private static byte[] ValidRecord() {
    var bytes = new byte[TerrainTypes.RecordSize];
    WriteUInt32(bytes, 0, 1);
    WriteUInt32(bytes, 8, Convert.ToUInt32(TerrainAddon.Soaked));
    WriteUInt32(bytes, 16, Convert.ToUInt32(TerrainTypeKind.GroundBlended));
    WriteSingle(bytes, 40, 0.1f);
    WriteSingle(bytes, 44, 0.1f);
    return bytes;
  }

  private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);

  private static void WriteSingle(byte[] bytes, int offset, float value) =>
    WriteUInt32(bytes, offset, BitConverter.SingleToUInt32Bits(value));
}
