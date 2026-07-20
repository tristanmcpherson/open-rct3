using System.Numerics;
using System.Reflection;
using System.Text;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class BoneShapesTests {
  [Test]
  public void Decode_ReadsExactBshLayoutGeometrySkinningBonesAndResourceKeys() {
    var fixture = new BoneShapeFixture();

    var shape = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(shape.Name, Is.EqualTo("synthetic"));
      Assert.That(shape.BoundingBoxMin, Is.EqualTo(new Vector3(-1, -2, -3)));
      Assert.That(shape.BoundingBoxMax, Is.EqualTo(new Vector3(4, 5, 6)));
      Assert.That(shape.Meshes, Has.Count.EqualTo(1));
      Assert.That(shape.Bones, Has.Count.EqualTo(2));
    }

    var mesh = shape.Meshes[0];
    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.Name, Is.EqualTo("synthetic/mesh/0"));
      Assert.That(mesh.SupportType, Is.EqualTo(-1));
      Assert.That(mesh.FtxRef, Is.EqualTo("water:ftx"));
      Assert.That(mesh.TxsRef, Is.EqualTo("opaque:txs"));
      Assert.That(mesh.Transparency, Is.Zero);
      Assert.That(mesh.TextureFlags, Is.EqualTo(12));
      Assert.That(mesh.Sides, Is.EqualTo(3));
      Assert.That(mesh.Vertices, Has.Count.EqualTo(3));
      Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 0, 1, 2 }));
      Assert.That(mesh.IndexLayout, Is.EqualTo(StaticShapeIndexLayout.TriangleList));
      Assert.That(mesh.StoredIndexCount, Is.EqualTo(3));
      Assert.That(mesh.LogicalIndexCount, Is.EqualTo(3));
      Assert.That(mesh.TriangleCount, Is.EqualTo(1));
    }

    var first = mesh.Vertices[0];
    using (Assert.EnterMultipleScope()) {
      Assert.That(first.Position, Is.EqualTo(new Vector3(1, 2, 3)));
      Assert.That(first.Normal, Is.EqualTo(Vector3.UnitY));
      Assert.That(first.TexCoord, Is.EqualTo(new Vector2(0.25f, 0.75f)));
      Assert.That(first.Color.X, Is.EqualTo(17 / 255.0f));
      Assert.That(first.Color.Y, Is.EqualTo(34 / 255.0f));
      Assert.That(first.Color.Z, Is.EqualTo(51 / 255.0f));
      Assert.That(first.Color.W, Is.EqualTo(1));
      Assert.That(first.Skinning,
        Is.EqualTo(new BoneShapeSkinning(0, 1, -1, -1, 200, 55, 0, 0)));
      Assert.That(mesh.Vertices[1].Position, Is.EqualTo(new Vector3(4, 5, 6)));
      Assert.That(mesh.Vertices[2].Position, Is.EqualTo(new Vector3(7, 8, 9)));
    }

    using (Assert.EnterMultipleScope()) {
      Assert.That(shape.Bones[0].Name, Is.EqualTo("Scene Root"));
      Assert.That(shape.Bones[0].ParentBoneNumber, Is.EqualTo(-1));
      Assert.That(shape.Bones[0].Position1, Is.EqualTo(Matrix4x4.Identity));
      Assert.That(shape.Bones[0].Position2, Is.EqualTo(Matrix4x4.Identity));
      Assert.That(shape.Bones[1].Name, Is.EqualTo("arm"));
      Assert.That(shape.Bones[1].ParentBoneNumber, Is.Zero);
      Assert.That(shape.Bones[1].Position1,
        Is.EqualTo(Matrix4x4.CreateTranslation(10, 20, 30)));
      Assert.That(shape.Bones[1].Position2, Is.EqualTo(Matrix4x4.CreateScale(2)));
    }
  }

  [Test]
  public void Decode_UsesFortyFourByteVertexStrideAndUInt16Indices() {
    var fixture = new BoneShapeFixture();
    fixture.UseLargeVertexSetWithHighIndices();

    var mesh = fixture.Decode().Meshes[0];

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.Vertices, Has.Count.EqualTo(258));
      Assert.That(mesh.Vertices[1].Position, Is.EqualTo(new Vector3(1, 2, 3)));
      Assert.That(mesh.Vertices[257].Position, Is.EqualTo(new Vector3(257, 258, 259)));
      Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 0, 256, 257 }));
    }
  }

  [Test]
  public void Decode_PlacementNoSort_UsesStoredTriangleCountAndLogicalIndexTotal() {
    var fixture = new BoneShapeFixture();
    fixture.UsePlacementTriangleList([0, 1, 2, 2, 1, 0]);

    var mesh = fixture.Decode().Meshes[0];

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.StoredIndexCount, Is.EqualTo(2));
      Assert.That(mesh.LogicalIndexCount, Is.EqualTo(6));
      Assert.That(mesh.IndexLayout, Is.EqualTo(StaticShapeIndexLayout.PlacementTriangleList));
      Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 0, 1, 2, 2, 1, 0 }));
      Assert.That(mesh.PlacementSortPermutations, Is.Empty);
    }
  }

  [Test]
  public void Decode_PlacementSorted_PreservesThreeProvenTrianglePermutations() {
    var fixture = new BoneShapeFixture();
    fixture.UsePlacementSortedPermutations([
      0, 1, 2, 2, 1, 0,
      2, 1, 0, 0, 1, 2,
      0, 1, 2, 2, 1, 0
    ]);

    var mesh = fixture.Decode().Meshes[0];

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.StoredIndexCount, Is.EqualTo(6));
      Assert.That(mesh.LogicalIndexCount, Is.EqualTo(6));
      Assert.That(mesh.IndexLayout,
        Is.EqualTo(StaticShapeIndexLayout.PlacementSortedTriangleList));
      Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 0, 1, 2, 2, 1, 0 }));
      Assert.That(mesh.PlacementSortPermutations, Has.Count.EqualTo(3));
      Assert.That(mesh.PlacementSortPermutations[1],
        Is.EqualTo(new uint[] { 2, 1, 0, 0, 1, 2 }));
    }
  }

  [Test]
  public void Decode_DivisiblePlacementNoSort_RetainsTheCompleteTriangleList() {
    var fixture = new BoneShapeFixture();
    fixture.UsePlacementTriangleList([0, 1, 2, 1, 2, 0, 2, 0, 1]);

    var mesh = fixture.Decode().Meshes[0];

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.StoredIndexCount, Is.EqualTo(3));
      Assert.That(mesh.LogicalIndexCount, Is.EqualTo(9));
      Assert.That(mesh.IndexLayout, Is.EqualTo(StaticShapeIndexLayout.PlacementTriangleList));
      Assert.That(mesh.Indices, Has.Count.EqualTo(9));
    }
  }

  [Test]
  public void Decode_DistinctMeshHeadersReuseAliasedGeometryWithinAggregateBudget() {
    var fixture = new BoneShapeFixture();
    fixture.UseDistinctMeshFanSharingGeometry(100);

    var shape = fixture.Decode(new BoneShapeDecodeLimits(5_000, 500));

    using (Assert.EnterMultipleScope()) {
      Assert.That(shape.Meshes, Has.Count.EqualTo(100));
      Assert.That(shape.Meshes.All(mesh =>
        ReferenceEquals(mesh.Vertices, shape.Meshes[0].Vertices)), Is.True);
      Assert.That(shape.Meshes.All(mesh =>
        ReferenceEquals(mesh.Indices, shape.Meshes[0].Indices)), Is.True);
    }
  }

  [Test]
  public void Decode_EnforcesWholeDecodeByteAndObjectBudgets() {
    var byteFixture = new BoneShapeFixture();
    var objectFixture = new BoneShapeFixture();

    using (Assert.EnterMultipleScope()) {
      Assert.Throws<InvalidDataException>(new Action(() =>
        byteFixture.Decode(new BoneShapeDecodeLimits(100, 1_000))));
      Assert.Throws<InvalidDataException>(new Action(() =>
        objectFixture.Decode(new BoneShapeDecodeLimits(1_024, 5))));
    }
  }

  [Test]
  public void Decode_RelocatedBoneNameAtAddressZeroIsNotTreatedAsNull() {
    var fixture = new BoneShapeFixture();
    fixture.UseRootBoneNameAtAddressZero();

    var shape = fixture.Decode();

    Assert.That(shape.Bones[0].Name, Is.EqualTo("Scene Root"));
  }

  [TestCase(MalformedBoneShape.TruncatedHeader)]
  [TestCase(MalformedBoneShape.OversizedMeshCount)]
  [TestCase(MalformedBoneShape.MissingMeshRelocation)]
  [TestCase(MalformedBoneShape.TruncatedVertices)]
  [TestCase(MalformedBoneShape.OutOfRangeIndex)]
  [TestCase(MalformedBoneShape.NonTriangleIndexCount)]
  [TestCase(MalformedBoneShape.VertexTotalMismatch)]
  [TestCase(MalformedBoneShape.IndexTotalMismatch)]
  [TestCase(MalformedBoneShape.UnsupportedMeshTotalMismatch)]
  [TestCase(MalformedBoneShape.NonFiniteVertex)]
  [TestCase(MalformedBoneShape.TruncatedPlacementIndices)]
  [TestCase(MalformedBoneShape.PlacementIndexCountOverflow)]
  [TestCase(MalformedBoneShape.OversizedBoneCount)]
  [TestCase(MalformedBoneShape.MissingBoneRelocation)]
  [TestCase(MalformedBoneShape.TruncatedBoneArray)]
  [TestCase(MalformedBoneShape.TruncatedPositionMatrix)]
  [TestCase(MalformedBoneShape.UnterminatedBoneName)]
  [TestCase(MalformedBoneShape.NonFiniteBoneMatrix)]
  [TestCase(MalformedBoneShape.InvalidBoneParent)]
  [TestCase(MalformedBoneShape.BoneHierarchyCycle)]
  [TestCase(MalformedBoneShape.InvalidWeightedBone)]
  [TestCase(MalformedBoneShape.WrongSymbolReferenceType)]
  [TestCase(MalformedBoneShape.WrongSymbolReferenceArchiveType)]
  [TestCase(MalformedBoneShape.WrongSymbolReferenceOwner)]
  [TestCase(MalformedBoneShape.ConflictingRawAndSymbolReference)]
  [TestCase(MalformedBoneShape.WrongDirectReferenceType)]
  [TestCase(MalformedBoneShape.MissingBoneShapeLoaderOwnership)]
  public void Decode_RejectsMalformedOrUnboundedData(MalformedBoneShape malformed) {
    var fixture = new BoneShapeFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  public void Decode_RejectsBoneNameWhoseTerminatorIsAfterTheLimit() {
    var fixture = new BoneShapeFixture();
    fixture.MakeMalformed(MalformedBoneShape.BoneNameTooLong);

    var error = Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));

    Assert.That(error!.Message, Does.Contain("exceeds 4096 bytes"));
  }

  [Test]
  public void Extract_FromEmbeddedSkyBeam_DecodesThePublicOvlPath() {
    WithSkyBeamOvl(ovl => {
      var shapes = BoneShapes.Extract(ovl);
      var skyBeam = shapes.Single(shape => shape.Name == "ZodiSkyBeam");
      var vertexCount = skyBeam.Meshes.Sum(mesh => mesh.Vertices.Count);
      var indexCount = skyBeam.Meshes.Sum(mesh => mesh.Indices.Count);

      TestContext.Progress.WriteLine(
        $"SkyBeam BSH evidence: shapes={shapes.Count}, meshes={skyBeam.Meshes.Count}, " +
        $"bones={skyBeam.Bones.Count}, vertices={vertexCount}, indices={indexCount}");
      using (Assert.EnterMultipleScope()) {
        Assert.That(skyBeam.Meshes, Is.Not.Empty);
        Assert.That(skyBeam.Bones, Is.Not.Empty);
        Assert.That(vertexCount, Is.GreaterThan(0));
        Assert.That(indexCount, Is.GreaterThan(0));
      }
    });
  }

  [Test]
  public void Extract_ChargesPublicPathMetadataAgainstTheAggregateBudget() {
    WithSkyBeamOvl(ovl => {
      var error = Assert.Throws<InvalidDataException>(new Action(() =>
        BoneShapes.Extract(ovl, new BoneShapeDecodeLimits(1, 1_000_000))));

      Assert.That(error!.Message, Does.Contain("resource index key"));
    });
  }

  private static void WithSkyBeamOvl(Action<Ovl> action) {
    var assembly = Assembly.GetExecutingAssembly();
    var resources = assembly.GetManifestResourceNames();
    var commonResource = resources.Single(name => name.EndsWith(
      ".CFRs.SkyBeam.SkyBeam.common.ovl", StringComparison.OrdinalIgnoreCase));
    var uniqueResource = commonResource[..^".common.ovl".Length] + ".unique.ovl";
    var tempDir = Directory.CreateTempSubdirectory().FullName;
    try {
      var commonPath = Path.Combine(tempDir, "fixture.common.ovl");
      CopyResource(assembly, commonResource, commonPath);
      CopyResource(assembly, uniqueResource, Path.Combine(tempDir, "fixture.unique.ovl"));
      using var ovl = Ovl.Load(commonPath);
      action(ovl);
    } finally {
      Directory.Delete(tempDir, recursive: true);
    }
  }

  private static void CopyResource(Assembly assembly, string resourceName, string path) {
    using var input = assembly.GetManifestResourceStream(resourceName);
    Assert.That(input, Is.Not.Null, $"Embedded resource '{resourceName}' not found.");
    using var output = File.Create(path);
    input.CopyTo(output);
  }

  public enum MalformedBoneShape {
    TruncatedHeader,
    OversizedMeshCount,
    MissingMeshRelocation,
    TruncatedVertices,
    OutOfRangeIndex,
    NonTriangleIndexCount,
    VertexTotalMismatch,
    IndexTotalMismatch,
    UnsupportedMeshTotalMismatch,
    NonFiniteVertex,
    TruncatedPlacementIndices,
    PlacementIndexCountOverflow,
    OversizedBoneCount,
    MissingBoneRelocation,
    TruncatedBoneArray,
    TruncatedPositionMatrix,
    UnterminatedBoneName,
    BoneNameTooLong,
    NonFiniteBoneMatrix,
    InvalidBoneParent,
    BoneHierarchyCycle,
    InvalidWeightedBone,
    WrongSymbolReferenceType,
    WrongSymbolReferenceArchiveType,
    WrongSymbolReferenceOwner,
    ConflictingRawAndSymbolReference,
    WrongDirectReferenceType,
    MissingBoneShapeLoaderOwnership
  }

  private sealed class BoneShapeFixture {
    private const uint ShapeAddress = 100;
    private const uint MeshPointersAddress = 200;
    private const uint MeshAddress = 300;
    private const uint VerticesAddress = 1_000;
    private const uint IndicesAddress = 20_000;
    private const uint BonesAddress = 21_000;
    private const uint Positions1Address = 22_000;
    private const uint Positions2Address = 24_000;
    private const uint RootNameAddress = 26_000;
    private const uint ChildNameAddress = 26_100;
    private const uint FtxAddress = 27_000;
    private const uint TxsAddress = 27_100;
    private const int VertexSize = 44;

    private readonly FakeBoneShapeDataSource source = new();

    public BoneShapeFixture() {
      var shape = source.AddBlock(ShapeAddress, 60);
      WriteVector3(shape, 0, new Vector3(-1, -2, -3));
      WriteVector3(shape, 12, new Vector3(4, 5, 6));
      WriteUInt32(shape, 24, 3);
      WriteUInt32(shape, 28, 3);
      WriteUInt32(shape, 32, 1);
      WriteUInt32(shape, 36, 1);
      WritePointer(shape, ShapeAddress, 40, MeshPointersAddress);
      WriteUInt32(shape, 44, 2);
      WritePointer(shape, ShapeAddress, 48, BonesAddress);
      WritePointer(shape, ShapeAddress, 52, Positions1Address);
      WritePointer(shape, ShapeAddress, 56, Positions2Address);

      var meshPointers = source.AddBlock(MeshPointersAddress, 4);
      WritePointer(meshPointers, MeshPointersAddress, 0, MeshAddress);

      var mesh = source.AddBlock(MeshAddress, 40);
      WriteInt32(mesh, 0, -1);
      WriteUInt32(mesh, 4, 0);
      WriteUInt32(mesh, 8, 0);
      WriteUInt32(mesh, 12, 0);
      WriteUInt32(mesh, 16, 12);
      WriteUInt32(mesh, 20, 3);
      WriteUInt32(mesh, 24, 3);
      WriteUInt32(mesh, 28, 3);
      WritePointer(mesh, MeshAddress, 32, VerticesAddress);
      WritePointer(mesh, MeshAddress, 36, IndicesAddress);

      var vertices = source.AddBlock(VerticesAddress, VertexSize * 3);
      WriteVertex(
        vertices,
        0,
        new Vector3(1, 2, 3),
        Vector3.UnitY,
        new Vector2(0.25f, 0.75f),
        [0, 1, -1, -1],
        [200, 55, 0, 0]);
      WriteVertex(
        vertices,
        VertexSize,
        new Vector3(4, 5, 6),
        Vector3.UnitY,
        Vector2.Zero,
        [1, -1, -1, -1],
        [255, 0, 0, 0]);
      WriteVertex(
        vertices,
        VertexSize * 2,
        new Vector3(7, 8, 9),
        Vector3.UnitY,
        Vector2.One,
        [-1, -1, -1, -1],
        [0, 0, 0, 0]);
      source.AddBlock(IndicesAddress, EncodeIndices([0, 1, 2]));

      var bones = source.AddBlock(BonesAddress, 16);
      WritePointer(bones, BonesAddress, 0, RootNameAddress);
      WriteInt32(bones, 4, -1);
      WritePointer(bones, BonesAddress, 8, ChildNameAddress);
      WriteInt32(bones, 12, 0);

      var positions1 = source.AddBlock(Positions1Address, 128);
      WriteMatrix(positions1, 0, Matrix4x4.Identity);
      WriteMatrix(positions1, 64, Matrix4x4.CreateTranslation(10, 20, 30));
      var positions2 = source.AddBlock(Positions2Address, 128);
      WriteMatrix(positions2, 0, Matrix4x4.Identity);
      WriteMatrix(positions2, 64, Matrix4x4.CreateScale(2));
      source.AddBlock(RootNameAddress, Encoding.ASCII.GetBytes("Scene Root\0"));
      source.AddBlock(ChildNameAddress, Encoding.ASCII.GetBytes("arm\0"));
      source.AddBlock(FtxAddress, 4);
      source.AddBlock(TxsAddress, 4);

      source.MutableResourcesByAddress[FtxAddress] =
        new BoneShapeResourceMetadata("water:ftx", "ftx");
      source.MutableResourcesByAddress[TxsAddress] =
        new BoneShapeResourceMetadata("opaque:txs", "txs");
      source.MutableResourcesByKey["water:ftx"] = source.MutableResourcesByAddress[FtxAddress];
      source.MutableResourcesByKey["opaque:txs"] = source.MutableResourcesByAddress[TxsAddress];
      source.MutableLocalResourceKeys.Add("water:ftx");
      source.MutableLocalResourceKeys.Add("opaque:txs");
      source.MutableBoneShapeLoaderDataAddresses.Add(ShapeAddress);
      source.MutableResourceReferences[MeshAddress + 4] =
        new BoneShapeResourceReference("water:ftx", ShapeAddress);
      source.MutableResourceReferences[MeshAddress + 8] =
        new BoneShapeResourceReference("opaque:txs", ShapeAddress);
    }

    public BoneShape Decode() => BoneShapes.Decode("synthetic", ShapeAddress, source);
    public BoneShape Decode(BoneShapeDecodeLimits limits) =>
      BoneShapes.Decode("synthetic", ShapeAddress, source, limits);

    public void UseLargeVertexSetWithHighIndices() {
      const int count = 258;
      var vertices = new byte[count * VertexSize];
      for (var i = 0; i < count; i++)
        WriteVertex(
          vertices,
          i * VertexSize,
          new Vector3(i, i + 1, i + 2),
          Vector3.UnitY,
          Vector2.Zero,
          [-1, -1, -1, -1],
          [0, 0, 0, 0]);
      source.ReplaceBlock(VerticesAddress, vertices);
      source.ReplaceBlock(IndicesAddress, EncodeIndices([0, 256, 257]));
      WriteUInt32(source.Blocks[ShapeAddress], 24, Convert.ToUInt32(count));
      WriteUInt32(source.Blocks[MeshAddress], 24, Convert.ToUInt32(count));
    }

    public void UsePlacementTriangleList(uint[] indices) {
      Assert.That(indices.Length, Is.Positive);
      Assert.That(indices.Length % 3, Is.Zero);
      WriteUInt32(source.Blocks[MeshAddress], 12, 1);
      WriteUInt32(source.Blocks[MeshAddress], 28, Convert.ToUInt32(indices.Length / 3));
      WriteUInt32(source.Blocks[ShapeAddress], 28, Convert.ToUInt32(indices.Length));
      source.ReplaceBlock(IndicesAddress, EncodeIndices(indices));
    }

    public void UsePlacementSortedPermutations(uint[] indices) {
      Assert.That(indices.Length, Is.Positive);
      Assert.That(indices.Length % 9, Is.Zero);
      var permutationCount = Convert.ToUInt32(indices.Length / 3);
      WriteUInt32(source.Blocks[MeshAddress], 12, 1);
      WriteUInt32(source.Blocks[MeshAddress], 28, permutationCount);
      WriteUInt32(source.Blocks[ShapeAddress], 28, permutationCount);
      source.ReplaceBlock(IndicesAddress, EncodeIndices(indices));
    }

    public void UseDistinctMeshFanSharingGeometry(int count) {
      const uint fanPointersAddress = 30_000;
      var pointers = new byte[count * 4];
      for (var i = 0; i < count; i++) {
        var meshAddress = i == 0 ? MeshAddress : Convert.ToUInt32(40_000 + (i - 1) * 40);
        if (i != 0) {
          source.AddBlock(meshAddress, source.Blocks[MeshAddress].ToArray());
          source.Relocations[meshAddress + 32] = VerticesAddress;
          source.Relocations[meshAddress + 36] = IndicesAddress;
          source.MutableResourceReferences[meshAddress + 4] =
            new BoneShapeResourceReference("water:ftx", ShapeAddress);
          source.MutableResourceReferences[meshAddress + 8] =
            new BoneShapeResourceReference("opaque:txs", ShapeAddress);
        }
        WritePointer(pointers, fanPointersAddress, i * 4, meshAddress);
      }
      source.AddBlock(fanPointersAddress, pointers);
      WritePointer(source.Blocks[ShapeAddress], ShapeAddress, 40, fanPointersAddress);
      WriteUInt32(source.Blocks[ShapeAddress], 24, Convert.ToUInt32(count * 3));
      WriteUInt32(source.Blocks[ShapeAddress], 28, Convert.ToUInt32(count * 3));
      WriteUInt32(source.Blocks[ShapeAddress], 32, Convert.ToUInt32(count));
      WriteUInt32(source.Blocks[ShapeAddress], 36, Convert.ToUInt32(count));
    }

    public void UseRootBoneNameAtAddressZero() {
      WritePointer(source.Blocks[BonesAddress], BonesAddress, 0, 0);
      source.AddBlock(0, Encoding.ASCII.GetBytes("Scene Root\0"));
    }

    public void MakeMalformed(MalformedBoneShape malformed) {
      switch (malformed) {
        case MalformedBoneShape.TruncatedHeader:
          source.ReplaceBlock(ShapeAddress, source.Blocks[ShapeAddress][..59]);
          break;
        case MalformedBoneShape.OversizedMeshCount:
          WriteUInt32(source.Blocks[ShapeAddress], 36, uint.MaxValue);
          break;
        case MalformedBoneShape.MissingMeshRelocation:
          source.Relocations.Remove(MeshPointersAddress);
          break;
        case MalformedBoneShape.TruncatedVertices:
          source.ReplaceBlock(VerticesAddress, source.Blocks[VerticesAddress][..^1]);
          break;
        case MalformedBoneShape.OutOfRangeIndex:
          WriteUInt16(source.Blocks[IndicesAddress], 4, 3);
          break;
        case MalformedBoneShape.NonTriangleIndexCount:
          WriteUInt32(source.Blocks[ShapeAddress], 28, 2);
          WriteUInt32(source.Blocks[MeshAddress], 28, 2);
          break;
        case MalformedBoneShape.VertexTotalMismatch:
          WriteUInt32(source.Blocks[ShapeAddress], 24, 4);
          break;
        case MalformedBoneShape.IndexTotalMismatch:
          WriteUInt32(source.Blocks[ShapeAddress], 28, 4);
          break;
        case MalformedBoneShape.UnsupportedMeshTotalMismatch:
          WriteUInt32(source.Blocks[ShapeAddress], 32, 0);
          break;
        case MalformedBoneShape.NonFiniteVertex:
          WriteSingle(source.Blocks[VerticesAddress], 0, float.NaN);
          break;
        case MalformedBoneShape.TruncatedPlacementIndices:
          UsePlacementTriangleList([0, 1, 2, 2, 1, 0]);
          source.ReplaceBlock(IndicesAddress, source.Blocks[IndicesAddress][..^1]);
          break;
        case MalformedBoneShape.PlacementIndexCountOverflow:
          WriteUInt32(source.Blocks[MeshAddress], 12, 1);
          WriteUInt32(source.Blocks[MeshAddress], 28, uint.MaxValue);
          WriteUInt32(source.Blocks[ShapeAddress], 28, uint.MaxValue);
          break;
        case MalformedBoneShape.OversizedBoneCount:
          WriteUInt32(source.Blocks[ShapeAddress], 44, 65_537);
          break;
        case MalformedBoneShape.MissingBoneRelocation:
          source.Relocations.Remove(ShapeAddress + 48);
          break;
        case MalformedBoneShape.TruncatedBoneArray:
          source.ReplaceBlock(BonesAddress, source.Blocks[BonesAddress][..^1]);
          break;
        case MalformedBoneShape.TruncatedPositionMatrix:
          source.ReplaceBlock(Positions2Address, source.Blocks[Positions2Address][..^1]);
          break;
        case MalformedBoneShape.UnterminatedBoneName:
          source.ReplaceBlock(RootNameAddress, Encoding.ASCII.GetBytes("Scene Root"));
          break;
        case MalformedBoneShape.BoneNameTooLong:
          source.ReplaceBlock(
            RootNameAddress,
            [.. Enumerable.Repeat(Convert.ToByte('x'), 4 * 1024), Convert.ToByte(0)]);
          break;
        case MalformedBoneShape.NonFiniteBoneMatrix:
          WriteSingle(source.Blocks[Positions1Address], 64, float.PositiveInfinity);
          break;
        case MalformedBoneShape.InvalidBoneParent:
          WriteInt32(source.Blocks[BonesAddress], 12, 2);
          break;
        case MalformedBoneShape.BoneHierarchyCycle:
          WriteInt32(source.Blocks[BonesAddress], 4, 1);
          break;
        case MalformedBoneShape.InvalidWeightedBone:
          source.Blocks[VerticesAddress][24] = 2;
          break;
        case MalformedBoneShape.WrongSymbolReferenceType:
          source.MutableResourceReferences[MeshAddress + 4] =
            new BoneShapeResourceReference("opaque:txs", ShapeAddress);
          break;
        case MalformedBoneShape.WrongSymbolReferenceArchiveType:
          source.MutableResourcesByKey["water:ftx"] =
            new BoneShapeResourceMetadata("water:ftx", "txs");
          break;
        case MalformedBoneShape.WrongSymbolReferenceOwner:
          source.MutableResourceReferences[MeshAddress + 4] =
            new BoneShapeResourceReference("water:ftx", ShapeAddress + 1);
          break;
        case MalformedBoneShape.ConflictingRawAndSymbolReference:
          WriteUInt32(source.Blocks[MeshAddress], 4, FtxAddress);
          break;
        case MalformedBoneShape.WrongDirectReferenceType:
          source.MutableResourceReferences.Remove(MeshAddress + 4);
          WritePointer(source.Blocks[MeshAddress], MeshAddress, 4, FtxAddress);
          source.MutableResourcesByAddress[FtxAddress] =
            new BoneShapeResourceMetadata("water:ftx", "txs");
          break;
        case MalformedBoneShape.MissingBoneShapeLoaderOwnership:
          source.MutableBoneShapeLoaderDataAddresses.Clear();
          break;
      }
    }

    private static byte[] EncodeIndices(uint[] indices) {
      var bytes = new byte[indices.Length * sizeof(ushort)];
      for (var i = 0; i < indices.Length; i++)
        WriteUInt16(bytes, i * sizeof(ushort), Convert.ToUInt16(indices[i]));
      return bytes;
    }

    private void WritePointer(byte[] bytes, uint blockAddress, int offset, uint value) {
      WriteUInt32(bytes, offset, value);
      source.Relocations[blockAddress + Convert.ToUInt32(offset)] = value;
    }

    private static void WriteVertex(
      byte[] bytes,
      int offset,
      Vector3 position,
      Vector3 normal,
      Vector2 texCoord,
      sbyte[] bones,
      byte[] weights
    ) {
      Assert.That(bones, Has.Length.EqualTo(4));
      Assert.That(weights, Has.Length.EqualTo(4));
      WriteVector3(bytes, offset, position);
      WriteVector3(bytes, offset + 12, normal);
      for (var i = 0; i < bones.Length; i++) bytes[offset + 24 + i] = EncodeSByte(bones[i]);
      weights.CopyTo(bytes, offset + 28);
      WriteUInt32(bytes, offset + 32, 4_279_312_947);
      WriteSingle(bytes, offset + 36, texCoord.X);
      WriteSingle(bytes, offset + 40, texCoord.Y);
    }

    private static byte EncodeSByte(sbyte value) => value < 0
      ? Convert.ToByte(Convert.ToInt16(value) + 256)
      : Convert.ToByte(value);

    private static void WriteMatrix(byte[] bytes, int offset, Matrix4x4 value) {
      var values = new[] {
        value.M11, value.M12, value.M13, value.M14,
        value.M21, value.M22, value.M23, value.M24,
        value.M31, value.M32, value.M33, value.M34,
        value.M41, value.M42, value.M43, value.M44
      };
      for (var i = 0; i < values.Length; i++) WriteSingle(bytes, offset + i * 4, values[i]);
    }

    private static void WriteVector3(byte[] bytes, int offset, Vector3 value) {
      WriteSingle(bytes, offset, value.X);
      WriteSingle(bytes, offset + 4, value.Y);
      WriteSingle(bytes, offset + 8, value.Z);
    }

    private static void WriteSingle(byte[] bytes, int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);
  }

  private sealed class FakeBoneShapeDataSource : IBoneShapeDataSource {
    public Dictionary<uint, byte[]> Blocks { get; } = [];
    public Dictionary<uint, uint> Relocations { get; } = [];
    public Dictionary<uint, BoneShapeResourceMetadata> MutableResourcesByAddress { get; } = [];
    public Dictionary<string, BoneShapeResourceMetadata> MutableResourcesByKey { get; } =
      new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> MutableLocalResourceKeys { get; } =
      new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<uint, BoneShapeResourceReference> MutableResourceReferences { get; } = [];
    public HashSet<uint> MutableBoneShapeLoaderDataAddresses { get; } = [];
    public IReadOnlyDictionary<uint, BoneShapeResourceMetadata> ResourcesByAddress =>
      MutableResourcesByAddress;
    public IReadOnlyDictionary<string, BoneShapeResourceMetadata> ResourcesByKey =>
      MutableResourcesByKey;
    public IReadOnlySet<string> LocalResourceKeys => MutableLocalResourceKeys;
    public IReadOnlyDictionary<uint, BoneShapeResourceReference> ResourceReferences =>
      MutableResourceReferences;
    public IReadOnlySet<uint> BoneShapeLoaderDataAddresses => MutableBoneShapeLoaderDataAddresses;

    public byte[] AddBlock(uint address, int length) {
      var bytes = new byte[length];
      Blocks.Add(address, bytes);
      return bytes;
    }

    public void AddBlock(uint address, byte[] bytes) => Blocks.Add(address, bytes);
    public void ReplaceBlock(uint address, byte[] bytes) => Blocks[address] = bytes;

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      foreach (var block in Blocks) {
        if (address < block.Key) continue;
        var offset = Convert.ToUInt64(address - block.Key);
        if (offset + Convert.ToUInt64(length) > Convert.ToUInt64(block.Value.Length)) continue;
        bytes = block.Value.AsSpan(Convert.ToInt32(offset), length).ToArray();
        return true;
      }
      bytes = [];
      return false;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      Relocations.TryGetValue(address, out value);

    public bool TryGetNullTerminatedStringByteLength(
      uint address,
      int maximumLength,
      out int length
    ) {
      foreach (var block in Blocks) {
        if (address < block.Key) continue;
        var offset = Convert.ToUInt64(address - block.Key);
        if (offset >= Convert.ToUInt64(block.Value.Length)) continue;
        var start = Convert.ToInt32(offset);
        var available = Math.Min(maximumLength, block.Value.Length - start);
        var end = Array.IndexOf(block.Value, Convert.ToByte(0), start, available);
        length = end < 0 ? 0 : end - start;
        return end >= 0;
      }
      length = 0;
      return false;
    }

    public bool TryReadNullTerminatedString(uint address, int maximumLength, out string value) {
      foreach (var block in Blocks) {
        if (address < block.Key) continue;
        var offset = Convert.ToUInt64(address - block.Key);
        if (offset >= Convert.ToUInt64(block.Value.Length)) continue;
        var start = Convert.ToInt32(offset);
        var available = Math.Min(maximumLength, block.Value.Length - start);
        var end = Array.IndexOf(block.Value, Convert.ToByte(0), start, available);
        if (end < 0) break;
        value = Encoding.ASCII.GetString(block.Value, start, end - start);
        return true;
      }
      value = string.Empty;
      return false;
    }
  }
}
