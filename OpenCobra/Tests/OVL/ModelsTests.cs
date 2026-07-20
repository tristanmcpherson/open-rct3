using System.Numerics;
using System.Text;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class ModelsTests {
  [Test]
  public void Decode_ReadsExactStaticGeometryAndOwner() {
    var fixture = new ModelFixture();

    var model = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(model.Name, Is.EqualTo("AdultElephant"));
      Assert.That(model.SourcePath, Is.EqualTo("fixture.common.ovl"));
      Assert.That(model.DataAddress, Is.EqualTo(1_000));
      Assert.That(model.LoaderStructAddress, Is.EqualTo(900));
      Assert.That(model.Count0, Is.EqualTo(1));
      Assert.That(model.Count1, Is.EqualTo(2));
      Assert.That(model.Count2, Is.EqualTo(1));
      Assert.That(model.Count3, Is.EqualTo(3));
      Assert.That(model.BoneCount, Is.EqualTo(1));
      Assert.That(model.InlineValues, Is.EqualTo(new uint[] { 0xAABBCCDD }));
      Assert.That(model.Count2Values, Is.EqualTo(new uint[] { 0x11223344 }));
      Assert.That(model.Strings, Is.EqualTo(new[] { "texture" }));
      Assert.That(model.Bones, Has.Count.EqualTo(1));
      Assert.That(model.Groups, Has.Count.EqualTo(1));
    }

    var bone = model.Bones[0];
    using (Assert.EnterMultipleScope()) {
      Assert.That(bone.Name, Is.EqualTo("root"));
      Assert.That(bone.PositionQuaternion, Is.EqualTo(new Vector4(1, 2, 3, 1)));
      Assert.That(bone.RotationQuaternion, Is.EqualTo(new Vector4(0, 0, 0, 1)));
      Assert.That(bone.Matrix, Is.EqualTo(Matrix4x4.Identity));
      Assert.That(bone.Parent, Is.EqualTo(ushort.MaxValue));
      Assert.That(bone.BoneNumber, Is.EqualTo(1));
    }

    var group = model.Groups[0];
    using (Assert.EnterMultipleScope()) {
      Assert.That(group.SurfaceRecordCount, Is.EqualTo(1));
      Assert.That(group.MeshCount, Is.EqualTo(1));
      Assert.That(group.Field4, Is.EqualTo(1));
      Assert.That(group.BitsetMarker, Is.EqualTo(1));
      Assert.That(group.BoneBitsetWords, Is.EqualTo(new uint[] { 1 }));
      Assert.That(group.OptionalRecordMarker, Is.Zero);
      Assert.That(group.Meshes, Has.Count.EqualTo(1));
      Assert.That(group.SurfaceRecords, Has.Count.EqualTo(1));
    }

    var mesh = group.Meshes[0];
    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.Fvf, Is.EqualTo(0x1305));
      Assert.That(mesh.StoredIndexCount, Is.EqualTo(3));
      Assert.That(mesh.Multiplier, Is.EqualTo(1));
      Assert.That(mesh.StoredVertexCount, Is.EqualTo(3));
      Assert.That(mesh.Field0C, Is.Zero);
      Assert.That(mesh.Field0E, Is.EqualTo(ushort.MaxValue));
      Assert.That(mesh.Field10, Is.Zero);
      Assert.That(mesh.Field1C, Is.Zero);
      Assert.That(mesh.Vertices, Has.Count.EqualTo(3));
      Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 0, 1, 2 }));
      Assert.That(mesh.TriangleCount, Is.EqualTo(1));
      Assert.That(mesh.Vertices[1].Position, Is.EqualTo(Vector3.UnitX));
      Assert.That(mesh.Vertices[2].TexCoord, Is.EqualTo(Vector2.UnitY));
      Assert.That(mesh.Vertices[0].Skinning.Bone0, Is.Zero);
      Assert.That(mesh.Vertices[0].Skinning.Weight0, Is.EqualTo(255));
    }

    var surface = group.SurfaceRecords[0];
    using (Assert.EnterMultipleScope()) {
      Assert.That(surface.Field0, Is.EqualTo(7));
      Assert.That(surface.CountA, Is.EqualTo(1));
      Assert.That(surface.CountB, Is.EqualTo(1));
      Assert.That(surface.Field6, Is.EqualTo(9));
      Assert.That(surface.ValuesA, Is.EqualTo(new uint[] { 0x11111111 }));
      Assert.That(surface.ValuesB, Is.EqualTo(new uint[] { 0x22222222 }));
    }
  }

  [Test]
  public void Decode_UsesImmediateInterleavedNonModelLoaderBoundary() {
    var fixture = new ModelFixture();
    fixture.UseInterleavedNonModelBoundary();

    var model = fixture.Decode();

    Assert.That(model.BoneCount, Is.EqualTo(1));
  }

  [Test]
  public void Extract_RejectsReferenceWithoutExactModelTag() {
    using var ovl = new Ovl("fixture");

    Assert.Throws<ArgumentException>(new Action(() =>
      Models.Extract(ovl, "AdultElephant:bsh")));
  }

  [TestCase(MalformedModel.WrongLoaderType)]
  [TestCase(MalformedModel.MissingSourcePath)]
  [TestCase(MalformedModel.MissingOwnerFromDataRegion)]
  [TestCase(MalformedModel.DuplicateOwnerInDataRegion)]
  [TestCase(MalformedModel.FinalLoaderWithoutBlockEnd)]
  [TestCase(MalformedModel.WrongRecordExtent)]
  [TestCase(MalformedModel.TruncatedRecord)]
  [TestCase(MalformedModel.MissingLoaderDataRelocation)]
  [TestCase(MalformedModel.WrongLoaderDataRelocation)]
  [TestCase(MalformedModel.InternalRecordRelocation)]
  [TestCase(MalformedModel.MissingExtraData)]
  [TestCase(MalformedModel.WrongExtraChunkCount)]
  [TestCase(MalformedModel.EmptySecondExtraChunk)]
  [TestCase(MalformedModel.TruncatedCountPrefix)]
  [TestCase(MalformedModel.TruncatedBoneRegion)]
  [TestCase(MalformedModel.ExcessiveBoneCount)]
  [TestCase(MalformedModel.ExcessiveCount3)]
  [TestCase(MalformedModel.NonFiniteBone)]
  [TestCase(MalformedModel.InvalidParent)]
  [TestCase(MalformedModel.UnterminatedBoneNames)]
  [TestCase(MalformedModel.TrailingBoneNameBytes)]
  [TestCase(MalformedModel.InvalidLengthPrefixedString)]
  [TestCase(MalformedModel.NonzeroGroupRuntimeCursor)]
  [TestCase(MalformedModel.NonzeroOptionalRecordMarker)]
  [TestCase(MalformedModel.UnsupportedFvf)]
  [TestCase(MalformedModel.UnsupportedMultiplier)]
  [TestCase(MalformedModel.NonzeroMeshVertexCursor)]
  [TestCase(MalformedModel.NonzeroMeshIndexCursor)]
  [TestCase(MalformedModel.NonTriangleIndexCount)]
  [TestCase(MalformedModel.NonFiniteVertex)]
  [TestCase(MalformedModel.OutOfRangeIndex)]
  [TestCase(MalformedModel.OutOfRangeBoneInfluence)]
  [TestCase(MalformedModel.NonzeroSurfaceRuntimeCursor)]
  [TestCase(MalformedModel.TruncatedGeometry)]
  [TestCase(MalformedModel.TrailingGeometry)]
  public void Decode_RejectsMalformedOrUnprovenData(MalformedModel malformed) {
    var fixture = new ModelFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledElephantsReadsStaticGeometry() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      rct3Path,
      "WildAnimals",
      "elephant",
      "Elephant_data.common.ovl");
    Assert.That(path, Does.Exist, $"Installed Elephant data OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    AssertExactInstalledAdultOwner(ovl, path);

    var adult = Models.Extract(ovl, "AdultElephant:MDL");
    var baby = Models.Extract(ovl, "BabyElephant:mdl");

    AssertInstalledAdult(adult, path);
    AssertInstalledBaby(baby, path);
  }

  private static void AssertExactInstalledAdultOwner(Ovl ovl, string path) {
    var file = ovl.Keys.Single(candidate =>
      candidate.Type == FileType.Model &&
      string.Equals(candidate.Name, "AdultElephant", StringComparison.OrdinalIgnoreCase));
    Assert.That(file.Path, Is.EqualTo(path));
    Assert.That(ovl.TryGetDataPointer(file, out var address), Is.True);
    Assert.That(address, Is.EqualTo(350));
    var owner = ovl.LoaderEntriesInOrder.Single(entry =>
      entry.DataAddress == address &&
      entry.Tag.ToFileType() == FileType.Model &&
      string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase));
    Assert.That(owner.StructAddress, Is.EqualTo(190));
    Assert.That(
      ovl.TryGetRelocationSource(owner.StructAddress + sizeof(uint), out var ownerTarget),
      Is.True);
    Assert.That(ownerTarget, Is.EqualTo(address));
    if (!ovl.TryResolveRelocation(address, out var block, out _))
      throw new AssertionException("AdultElephant model block did not resolve.");
    var next = ovl.LoaderEntriesInOrder
      .Where(entry =>
        entry.DataAddress > address &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase) &&
        ovl.TryResolveRelocation(entry.DataAddress, out var candidateBlock, out _) &&
        ReferenceEquals(block, candidateBlock))
      .OrderBy(entry => entry.DataAddress)
      .First();
    using (Assert.EnterMultipleScope()) {
      Assert.That(next.Tag, Is.EqualTo("mdl"));
      Assert.That(next.DataAddress, Is.EqualTo(430));
      Assert.That(next.DataAddress - address, Is.EqualTo(0x50));
    }
    Assert.That(ovl.TryReadBytes(address, 0x50, out var record), Is.True);
    Assert.That(record, Has.Length.EqualTo(0x50));
    foreach (var offset in Enumerable.Range(0, 0x50))
      Assert.That(
        ovl.TryGetRelocationSource(address + Convert.ToUInt32(offset), out _),
        Is.False);
    if (!ovl.TryReadExtraData(address, out var chunks))
      throw new AssertionException("AdultElephant model extra data did not resolve.");
    using (Assert.EnterMultipleScope()) {
      Assert.That(chunks, Has.Count.EqualTo(2));
      Assert.That(chunks[0], Has.Length.EqualTo(55_344));
      Assert.That(chunks[1], Has.Length.EqualTo(464));
      Assert.That(BitConverter.ToUInt32(chunks[0], 0), Is.EqualTo(35));
      Assert.That(BitConverter.ToUInt32(chunks[0], 4), Is.Zero);
      Assert.That(BitConverter.ToUInt32(chunks[0], 8), Is.Zero);
      Assert.That(BitConverter.ToUInt32(chunks[0], 12), Is.Zero);
    }
  }

  private static void AssertInstalledAdult(ModelDefinition model, string path) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(model.Name, Is.EqualTo("AdultElephant"));
      Assert.That(model.SourcePath, Is.EqualTo(path));
      Assert.That(model.DataAddress, Is.EqualTo(350));
      Assert.That(model.LoaderStructAddress, Is.EqualTo(190));
      Assert.That(model.BoneCount, Is.EqualTo(35));
      Assert.That(model.Count1, Is.Zero);
      Assert.That(model.Count2, Is.Zero);
      Assert.That(model.Count3, Is.Zero);
      Assert.That(model.InlineValues, Is.Empty);
      Assert.That(model.Count2Values, Is.Empty);
      Assert.That(model.Strings, Has.Count.EqualTo(4));
      Assert.That(model.Groups, Has.Count.EqualTo(6));
      Assert.That(
        model.Groups.Select(group => group.Meshes.Count),
        Is.EqualTo(new[] { 2, 2, 1, 2, 0, 0 }));
      Assert.That(
        model.Groups.Select(group => group.Meshes.Select(mesh => mesh.Vertices.Count)),
        Is.EqualTo(new[] {
          new[] { 508, 8 },
          new[] { 322, 6 },
          new[] { 136 },
          new[] { 35, 8 },
          Array.Empty<int>(),
          Array.Empty<int>(),
        }));
      Assert.That(
        model.Groups.Select(group => group.Meshes.Select(mesh => mesh.Indices.Count)),
        Is.EqualTo(new[] {
          new[] { 1_644, 12 },
          new[] { 936, 6 },
          new[] { 294 },
          new[] { 78, 12 },
          Array.Empty<int>(),
          Array.Empty<int>(),
        }));
      Assert.That(
        model.Groups.Sum(group => group.Meshes.Sum(mesh => mesh.Vertices.Count)),
        Is.EqualTo(1_023));
      Assert.That(
        model.Groups.Sum(group => group.Meshes.Sum(mesh => mesh.Indices.Count)),
        Is.EqualTo(2_982));
    }
    AssertInstalledModelIsInternallyConsistent(model, "AdultElephant", path);
  }

  private static void AssertInstalledBaby(ModelDefinition model, string path) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(model.BoneCount, Is.EqualTo(35));
      Assert.That(model.Count1, Is.Zero);
      Assert.That(model.Count2, Is.Zero);
      Assert.That(model.Count3, Is.Zero);
      Assert.That(model.InlineValues, Is.Empty);
      Assert.That(model.Count2Values, Is.Empty);
      Assert.That(model.Strings, Has.Count.EqualTo(4));
      Assert.That(model.Groups, Has.Count.EqualTo(6));
      Assert.That(
        model.Groups.Select(group => group.Meshes.Count),
        Is.EqualTo(new[] { 2, 2, 1, 2, 0, 0 }));
      Assert.That(
        model.Groups.Select(group => group.Meshes.Select(mesh => mesh.Vertices.Count)),
        Is.EqualTo(new[] {
          new[] { 430, 8 },
          new[] { 315, 6 },
          new[] { 131 },
          new[] { 35, 8 },
          Array.Empty<int>(),
          Array.Empty<int>(),
        }));
      Assert.That(
        model.Groups.Select(group => group.Meshes.Select(mesh => mesh.Indices.Count)),
        Is.EqualTo(new[] {
          new[] { 1_368, 12 },
          new[] { 864, 6 },
          new[] { 288 },
          new[] { 78, 12 },
          Array.Empty<int>(),
          Array.Empty<int>(),
        }));
      Assert.That(
        model.Groups.Sum(group => group.Meshes.Sum(mesh => mesh.Vertices.Count)),
        Is.EqualTo(933));
      Assert.That(
        model.Groups.Sum(group => group.Meshes.Sum(mesh => mesh.Indices.Count)),
        Is.EqualTo(2_628));
    }
    AssertInstalledModelIsInternallyConsistent(model, "BabyElephant", path);
  }

  private static void AssertInstalledModelIsInternallyConsistent(
    ModelDefinition model,
    string name,
    string path
  ) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(model.Name, Is.EqualTo(name));
      Assert.That(model.SourcePath, Is.EqualTo(path));
      Assert.That(model.BoneCount, Is.GreaterThan(0));
      Assert.That(model.Bones, Has.Count.EqualTo(model.BoneCount));
      Assert.That(model.Groups, Is.Not.Empty);
      Assert.That(model.Groups.All(group => group.OptionalRecordMarker == 0), Is.True);
      Assert.That(
        model.Groups.SelectMany(group => group.Meshes)
          .All(mesh => mesh.Fvf == 0x1305 && mesh.Multiplier == 1),
        Is.True);
      Assert.That(
        model.Groups.SelectMany(group => group.Meshes)
          .All(mesh => mesh.Indices.Count > 0 && mesh.Indices.Count % 3 == 0),
        Is.True);
    }
  }

  private sealed class ModelFixture {
    private const uint RecordAddress = 1_000;
    private const uint StructAddress = 900;
    private const int RecordSize = 0x50;
    private const string SourcePath = "fixture.common.ovl";

    private readonly FakeModelDataSource source = new();
    private OvlLoaderEntry owner = new("mdl", RecordAddress, SourcePath, StructAddress);

    public ModelFixture() {
      var record = new byte[RecordSize];
      record[4] = 1;
      WriteUInt16(record, 6, 1);
      WriteUInt32(record, 0x4C, 1);
      source.AddBytes(RecordAddress, record);
      source.RegionLoaders.Add(owner);
      source.RegionLoaders.Add(new OvlLoaderEntry(
        "mdl",
        RecordAddress + RecordSize,
        SourcePath,
        StructAddress + 20));
      source.Relocations[StructAddress + sizeof(uint)] = RecordAddress;

      var firstChunk = new List<byte>();
      AddUInt32(firstChunk, 1);
      AddUInt32(firstChunk, 2);
      AddUInt32(firstChunk, 1);
      AddUInt32(firstChunk, 3);
      AddUInt32(firstChunk, 0xAABBCCDD);
      Align16(firstChunk);
      BoneOffset = firstChunk.Count;
      AddVector4(firstChunk, new Vector4(1, 2, 3, 1));
      AddVector4(firstChunk, new Vector4(0, 0, 0, 1));
      AddMatrix(firstChunk, Matrix4x4.Identity);
      AddUInt16(firstChunk, ushort.MaxValue);
      AddUInt16(firstChunk, 1);
      AddUInt32(firstChunk, 0x11223344);
      Align16(firstChunk);
      StringLengthOffset = firstChunk.Count;
      AddUInt32(firstChunk, 8);
      firstChunk.AddRange(Encoding.ASCII.GetBytes("texture\0"));
      Align16(firstChunk);
      GroupHeaderOffset = firstChunk.Count;
      AddUInt16(firstChunk, 1);
      AddUInt16(firstChunk, 1);
      AddUInt32(firstChunk, 1);
      AddUInt32(firstChunk, 0);
      AddUInt32(firstChunk, 0);
      AddUInt32(firstChunk, 1);
      OptionalMarkerOffset = firstChunk.Count;
      AddUInt32(firstChunk, 0);
      Align16(firstChunk);
      AddUInt32(firstChunk, 1);
      MeshHeaderOffset = firstChunk.Count;
      AddUInt32(firstChunk, 0x1305);
      AddUInt32(firstChunk, 3);
      AddUInt16(firstChunk, 1);
      AddUInt16(firstChunk, 3);
      AddUInt16(firstChunk, 0);
      AddUInt16(firstChunk, ushort.MaxValue);
      AddUInt32(firstChunk, 0);
      AddUInt32(firstChunk, 0);
      AddUInt32(firstChunk, 0);
      AddUInt32(firstChunk, 0);
      Align16(firstChunk);
      VertexOffset = firstChunk.Count;
      AddVertex(firstChunk, Vector3.Zero, Vector2.Zero);
      AddVertex(firstChunk, Vector3.UnitX, Vector2.UnitX);
      AddVertex(firstChunk, Vector3.UnitY, Vector2.UnitY);
      IndexOffset = firstChunk.Count;
      AddUInt16(firstChunk, 0);
      AddUInt16(firstChunk, 1);
      AddUInt16(firstChunk, 2);
      SurfaceHeaderOffset = firstChunk.Count;
      AddUInt16(firstChunk, 7);
      AddUInt16(firstChunk, 1);
      AddUInt16(firstChunk, 1);
      AddUInt16(firstChunk, 9);
      AddUInt32(firstChunk, 0);
      AddUInt32(firstChunk, 0);
      AddUInt32(firstChunk, 0x11111111);
      AddUInt32(firstChunk, 0x22222222);
      source.ExtraChunks.Add(firstChunk.ToArray());
      source.ExtraChunks.Add(Encoding.ASCII.GetBytes("root\0"));
    }

    private int BoneOffset { get; }
    private int StringLengthOffset { get; }
    private int GroupHeaderOffset { get; }
    private int OptionalMarkerOffset { get; }
    private int MeshHeaderOffset { get; }
    private int VertexOffset { get; }
    private int IndexOffset { get; }
    private int SurfaceHeaderOffset { get; }

    public ModelDefinition Decode() => Models.Decode("AdultElephant", owner, source);

    public void UseInterleavedNonModelBoundary() {
      source.RegionLoaders.Clear();
      source.RegionLoaders.Add(owner);
      source.RegionLoaders.Add(new OvlLoaderEntry(
        "tex",
        RecordAddress + RecordSize,
        SourcePath,
        StructAddress + 20));
      source.RegionLoaders.Add(new OvlLoaderEntry(
        "mdl",
        RecordAddress + RecordSize + 16,
        SourcePath,
        StructAddress + 40));
    }

    public void MakeMalformed(MalformedModel malformed) {
      switch (malformed) {
        case MalformedModel.WrongLoaderType:
          owner = owner with { Tag = "bsh" };
          break;
        case MalformedModel.MissingSourcePath:
          owner = owner with { SourcePath = "" };
          break;
        case MalformedModel.MissingOwnerFromDataRegion:
          source.RegionLoaders.RemoveAt(0);
          break;
        case MalformedModel.DuplicateOwnerInDataRegion:
          source.RegionLoaders.Add(owner);
          break;
        case MalformedModel.FinalLoaderWithoutBlockEnd:
          source.RegionLoaders.RemoveAt(1);
          break;
        case MalformedModel.WrongRecordExtent:
          source.RegionLoaders[1] = source.RegionLoaders[1] with {
            DataAddress = source.RegionLoaders[1].DataAddress - 1,
          };
          break;
        case MalformedModel.TruncatedRecord:
          source.ReplaceBytes(RecordAddress, new byte[RecordSize - 1]);
          break;
        case MalformedModel.MissingLoaderDataRelocation:
          source.Relocations.Remove(StructAddress + sizeof(uint));
          break;
        case MalformedModel.WrongLoaderDataRelocation:
          source.Relocations[StructAddress + sizeof(uint)] = RecordAddress + 1;
          break;
        case MalformedModel.InternalRecordRelocation:
          source.Relocations[RecordAddress + 4] = 123_456;
          break;
        case MalformedModel.MissingExtraData:
          source.HasExtraData = false;
          break;
        case MalformedModel.WrongExtraChunkCount:
          source.ExtraChunks.RemoveAt(1);
          break;
        case MalformedModel.EmptySecondExtraChunk:
          source.ExtraChunks[1] = [];
          break;
        case MalformedModel.TruncatedCountPrefix:
          source.ExtraChunks[0] = new byte[15];
          break;
        case MalformedModel.TruncatedBoneRegion:
          source.ExtraChunks[0] = source.ExtraChunks[0][..(BoneOffset + 95)];
          break;
        case MalformedModel.ExcessiveBoneCount:
          WriteUInt32(source.ExtraChunks[0], 0, 65_537);
          break;
        case MalformedModel.ExcessiveCount3:
          WriteUInt32(source.ExtraChunks[0], 12, 1_000_001);
          break;
        case MalformedModel.NonFiniteBone:
          WriteSingle(source.ExtraChunks[0], BoneOffset, float.NaN);
          break;
        case MalformedModel.InvalidParent:
          WriteUInt16(source.ExtraChunks[0], BoneOffset + 0x60, 1);
          break;
        case MalformedModel.UnterminatedBoneNames:
          source.ExtraChunks[1] = Encoding.ASCII.GetBytes("root");
          break;
        case MalformedModel.TrailingBoneNameBytes:
          source.ExtraChunks[1] = [.. source.ExtraChunks[1], 1];
          break;
        case MalformedModel.InvalidLengthPrefixedString:
          WriteUInt32(source.ExtraChunks[0], StringLengthOffset, 7);
          break;
        case MalformedModel.NonzeroGroupRuntimeCursor:
          WriteUInt32(source.ExtraChunks[0], GroupHeaderOffset + 8, 1);
          break;
        case MalformedModel.NonzeroOptionalRecordMarker:
          WriteUInt32(source.ExtraChunks[0], OptionalMarkerOffset, 1);
          break;
        case MalformedModel.UnsupportedFvf:
          WriteUInt32(source.ExtraChunks[0], MeshHeaderOffset, 0x1304);
          break;
        case MalformedModel.UnsupportedMultiplier:
          WriteUInt16(source.ExtraChunks[0], MeshHeaderOffset + 8, 2);
          break;
        case MalformedModel.NonzeroMeshVertexCursor:
          WriteUInt32(source.ExtraChunks[0], MeshHeaderOffset + 0x14, 1);
          break;
        case MalformedModel.NonzeroMeshIndexCursor:
          WriteUInt32(source.ExtraChunks[0], MeshHeaderOffset + 0x18, 1);
          break;
        case MalformedModel.NonTriangleIndexCount:
          WriteUInt32(source.ExtraChunks[0], MeshHeaderOffset + 4, 4);
          break;
        case MalformedModel.NonFiniteVertex:
          WriteSingle(source.ExtraChunks[0], VertexOffset, float.NaN);
          break;
        case MalformedModel.OutOfRangeIndex:
          WriteUInt16(source.ExtraChunks[0], IndexOffset, 3);
          break;
        case MalformedModel.OutOfRangeBoneInfluence:
          source.ExtraChunks[0][VertexOffset + 24] = 1;
          break;
        case MalformedModel.NonzeroSurfaceRuntimeCursor:
          WriteUInt32(source.ExtraChunks[0], SurfaceHeaderOffset + 8, 1);
          break;
        case MalformedModel.TruncatedGeometry:
          source.ExtraChunks[0] = source.ExtraChunks[0][..^1];
          break;
        case MalformedModel.TrailingGeometry:
          source.ExtraChunks[0] = [.. source.ExtraChunks[0], 0];
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed));
      }
    }

    private static void AddVertex(
      List<byte> bytes,
      Vector3 position,
      Vector2 texCoord
    ) {
      AddSingle(bytes, position.X);
      AddSingle(bytes, position.Y);
      AddSingle(bytes, position.Z);
      AddSingle(bytes, 0);
      AddSingle(bytes, 0);
      AddSingle(bytes, 1);
      bytes.AddRange([0, byte.MaxValue, byte.MaxValue, byte.MaxValue]);
      bytes.AddRange([byte.MaxValue, 0, 0, 0]);
      AddUInt32(bytes, uint.MaxValue);
      AddSingle(bytes, texCoord.X);
      AddSingle(bytes, texCoord.Y);
    }

    private static void AddVector4(List<byte> bytes, Vector4 value) {
      AddSingle(bytes, value.X);
      AddSingle(bytes, value.Y);
      AddSingle(bytes, value.Z);
      AddSingle(bytes, value.W);
    }

    private static void AddMatrix(List<byte> bytes, Matrix4x4 value) {
      foreach (var element in new[] {
                 value.M11, value.M12, value.M13, value.M14,
                 value.M21, value.M22, value.M23, value.M24,
                 value.M31, value.M32, value.M33, value.M34,
                 value.M41, value.M42, value.M43, value.M44,
               })
        AddSingle(bytes, element);
    }

    private static void Align16(List<byte> bytes) {
      while (bytes.Count % 16 != 0) bytes.Add(0);
    }

    private static void AddUInt16(List<byte> bytes, ushort value) =>
      bytes.AddRange(BitConverter.GetBytes(value));

    private static void AddUInt32(List<byte> bytes, uint value) =>
      bytes.AddRange(BitConverter.GetBytes(value));

    private static void AddSingle(List<byte> bytes, float value) =>
      bytes.AddRange(BitConverter.GetBytes(value));

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteSingle(byte[] bytes, int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);
  }

  private sealed class FakeModelDataSource : IModelDataSource {
    private readonly Dictionary<uint, byte[]> segments = [];

    public List<OvlLoaderEntry> RegionLoaders { get; } = [];
    public Dictionary<uint, uint> Relocations { get; } = [];
    public List<byte[]> ExtraChunks { get; } = [];
    public bool HasExtraData { get; set; } = true;

    public void AddBytes(uint address, byte[] bytes) => segments.Add(address, bytes);

    public void ReplaceBytes(uint address, byte[] bytes) => segments[address] = bytes;

    public IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner) =>
      RegionLoaders;

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      if (!segments.TryGetValue(address, out var segment) || length > segment.Length) {
        bytes = [];
        return false;
      }
      bytes = segment.AsSpan(0, length).ToArray();
      return true;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      Relocations.TryGetValue(address, out value);

    public bool TryReadExtraData(
      OvlLoaderEntry owner,
      out IReadOnlyList<byte[]> chunks
    ) {
      chunks = ExtraChunks;
      return HasExtraData;
    }
  }
}

public enum MalformedModel {
  WrongLoaderType,
  MissingSourcePath,
  MissingOwnerFromDataRegion,
  DuplicateOwnerInDataRegion,
  FinalLoaderWithoutBlockEnd,
  WrongRecordExtent,
  TruncatedRecord,
  MissingLoaderDataRelocation,
  WrongLoaderDataRelocation,
  InternalRecordRelocation,
  MissingExtraData,
  WrongExtraChunkCount,
  EmptySecondExtraChunk,
  TruncatedCountPrefix,
  TruncatedBoneRegion,
  ExcessiveBoneCount,
  ExcessiveCount3,
  NonFiniteBone,
  InvalidParent,
  UnterminatedBoneNames,
  TrailingBoneNameBytes,
  InvalidLengthPrefixedString,
  NonzeroGroupRuntimeCursor,
  NonzeroOptionalRecordMarker,
  UnsupportedFvf,
  UnsupportedMultiplier,
  NonzeroMeshVertexCursor,
  NonzeroMeshIndexCursor,
  NonTriangleIndexCount,
  NonFiniteVertex,
  OutOfRangeIndex,
  OutOfRangeBoneInfluence,
  NonzeroSurfaceRuntimeCursor,
  TruncatedGeometry,
  TrailingGeometry,
}
