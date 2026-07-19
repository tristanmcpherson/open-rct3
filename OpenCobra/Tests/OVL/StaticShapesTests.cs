using System.Numerics;
using System.Reflection;
using System.Text;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class StaticShapesTests {
  [Test]
  public void Decode_ReadsRelocatedMeshGeometryEffectsAndResourceKeys() {
    var fixture = new StaticShapeFixture();

    var shape = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(shape.Name, Is.EqualTo("synthetic"));
      Assert.That(shape.BoundingBoxMin, Is.EqualTo(new Vector3(-1, -2, -3)));
      Assert.That(shape.BoundingBoxMax, Is.EqualTo(new Vector3(4, 5, 6)));
      Assert.That(shape.Meshes, Has.Count.EqualTo(1));
      Assert.That(shape.Effects, Has.Count.EqualTo(1));
    }

    var mesh = shape.Meshes[0];
    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.Name, Is.EqualTo("synthetic/mesh/0"));
      Assert.That(mesh.SupportType, Is.EqualTo(-1));
      Assert.That(mesh.FtxRef, Is.EqualTo("water:ftx"));
      Assert.That(mesh.TxsRef, Is.EqualTo("opaque:txs"));
      Assert.That(mesh.Transparency, Is.EqualTo(0));
      Assert.That(mesh.TextureFlags, Is.EqualTo(12));
      Assert.That(mesh.Sides, Is.EqualTo(3));
      Assert.That(mesh.Vertices, Has.Count.EqualTo(3));
      Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 0, 1, 2 }));
      Assert.That(mesh.IndexLayout, Is.EqualTo(StaticShapeIndexLayout.TriangleList));
      Assert.That(mesh.StoredIndexCount, Is.EqualTo(3));
      Assert.That(mesh.YIndices, Is.Null);
      Assert.That(mesh.ZIndices, Is.Null);
      Assert.That(mesh.TriangleCount, Is.EqualTo(1));
    }

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.Vertices[0].Position, Is.EqualTo(new Vector3(1, 2, 3)));
      Assert.That(mesh.Vertices[0].Normal, Is.EqualTo(Vector3.UnitY));
      Assert.That(mesh.Vertices[0].TexCoord, Is.EqualTo(new Vector2(0.25f, 0.75f)));
      Assert.That(mesh.Vertices[0].Color.X, Is.EqualTo(17 / 255.0f));
      Assert.That(mesh.Vertices[0].Color.Y, Is.EqualTo(34 / 255.0f));
      Assert.That(mesh.Vertices[0].Color.Z, Is.EqualTo(51 / 255.0f));
      Assert.That(mesh.Vertices[0].Color.W, Is.EqualTo(1));
      Assert.That(shape.Effects[0].Name, Is.EqualTo("spark"));
      Assert.That(shape.Effects[0].Position, Is.EqualTo(Matrix4x4.Identity));
    }
  }

  [Test]
  public void Decode_PlacementNoSort_UsesStoredTriangleCountAndThreeIndicesPerTriangle() {
    var fixture = new StaticShapeFixture();
    fixture.UsePlacementTriangleList([0, 1, 2, 2, 1, 0]);

    var mesh = fixture.Decode().Meshes[0];

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.StoredIndexCount, Is.EqualTo(2));
      Assert.That(mesh.IndexLayout, Is.EqualTo(StaticShapeIndexLayout.PlacementTriangleList));
      Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 0, 1, 2, 2, 1, 0 }));
      Assert.That(mesh.TriangleCount, Is.EqualTo(2));
      Assert.That(mesh.YIndices, Is.Null);
      Assert.That(mesh.ZIndices, Is.Null);
    }
  }

  [Test]
  public void Decode_PlacementSorted_ReadsThreeEquivalentAxisStreams() {
    var fixture = new StaticShapeFixture();
    fixture.UsePlacementAxisStreams(
      [0, 1, 2, 2, 1, 0],
      [2, 1, 0, 0, 1, 2],
      [0, 1, 2, 2, 1, 0]);

    var mesh = fixture.Decode().Meshes[0];

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.StoredIndexCount, Is.EqualTo(6));
      Assert.That(mesh.IndexLayout, Is.EqualTo(StaticShapeIndexLayout.PlacementAxisStreams));
      Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 0, 1, 2, 2, 1, 0 }));
      Assert.That(mesh.YIndices, Is.EqualTo(new uint[] { 2, 1, 0, 0, 1, 2 }));
      Assert.That(mesh.ZIndices, Is.EqualTo(new uint[] { 0, 1, 2, 2, 1, 0 }));
      Assert.That(mesh.TriangleCount, Is.EqualTo(2));
    }
  }

  [Test]
  public void Decode_EnforcesWholeDecodeByteAndObjectBudgets() {
    var byteFixture = new StaticShapeFixture();
    var objectFixture = new StaticShapeFixture();

    using (Assert.EnterMultipleScope()) {
      Assert.Throws<InvalidDataException>(new Action(() =>
        byteFixture.Decode(new StaticShapeDecodeLimits(130, 100))));
      Assert.Throws<InvalidDataException>(new Action(() =>
        objectFixture.Decode(new StaticShapeDecodeLimits(1024, 5))));
    }
  }

  [TestCase(MalformedShape.TruncatedHeader)]
  [TestCase(MalformedShape.OversizedMeshCount)]
  [TestCase(MalformedShape.MissingMeshRelocation)]
  [TestCase(MalformedShape.TruncatedVertices)]
  [TestCase(MalformedShape.OutOfRangeIndex)]
  [TestCase(MalformedShape.NonTriangleIndexCount)]
  [TestCase(MalformedShape.AggregateCountMismatch)]
  [TestCase(MalformedShape.UnknownResourceReference)]
  [TestCase(MalformedShape.UnterminatedEffectName)]
  [TestCase(MalformedShape.NonFiniteVertex)]
  [TestCase(MalformedShape.TruncatedPlacementIndices)]
  [TestCase(MalformedShape.PlacementIndexCountOverflow)]
  [TestCase(MalformedShape.OversizedEffectCount)]
  [TestCase(MalformedShape.EffectNameTooLong)]
  [TestCase(MalformedShape.DuplicateMeshFan)]
  [TestCase(MalformedShape.WrongSymbolReferenceType)]
  [TestCase(MalformedShape.WrongSymbolReferenceOwner)]
  [TestCase(MalformedShape.ConflictingRawAndSymbolReference)]
  [TestCase(MalformedShape.WrongDirectReferenceType)]
  public void Decode_RejectsMalformedOrUnboundedData(MalformedShape malformed) {
    var fixture = new StaticShapeFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  public void Extract_FromCustomOvlFixtures_DecodesStaticShapes() {
    var assembly = Assembly.GetExecutingAssembly();
    var resources = assembly.GetManifestResourceNames();
    var commonResources = resources.Where(name => name.EndsWith(".common.ovl")).ToList();
    var shapeCount = 0;
    var meshCount = 0;
    var vertexCount = 0;
    var indexCount = 0;
    var referenceCount = 0;
    string? townHallMesh4Ftx = null;

    foreach (var commonResource in commonResources) {
      var uniqueResource = commonResource[..^".common.ovl".Length] + ".unique.ovl";
      var tempDir = Directory.CreateTempSubdirectory().FullName;
      try {
        var commonPath = Path.Combine(tempDir, "fixture.common.ovl");
        CopyResource(assembly, commonResource, commonPath);
        if (resources.Contains(uniqueResource))
          CopyResource(assembly, uniqueResource, Path.Combine(tempDir, "fixture.unique.ovl"));

        using var ovl = Ovl.Load(commonPath);
        var shapes = StaticShapes.Extract(ovl);
        shapeCount += shapes.Count;
        meshCount += shapes.Sum(shape => shape.Meshes.Count);
        vertexCount += shapes.Sum(shape => shape.Meshes.Sum(mesh => mesh.Vertices.Count));
        indexCount += shapes.Sum(shape => shape.Meshes.Sum(mesh => mesh.Indices.Count));
        referenceCount += shapes.Sum(shape => shape.Meshes.Sum(mesh =>
          Convert.ToInt32(mesh.FtxRef != null) + Convert.ToInt32(mesh.TxsRef != null)));
        var townHall = shapes.SingleOrDefault(shape => shape.Name == "RS-TownHall");
        if (townHall != null) {
          Assert.That(townHall.Meshes, Has.Count.GreaterThan(4));
          townHallMesh4Ftx = townHall.Meshes[4].FtxRef;
        }
      } finally {
        Directory.Delete(tempDir, recursive: true);
      }
    }

    TestContext.Progress.WriteLine(
      $"Custom SHS evidence: shapes={shapeCount}, meshes={meshCount}, vertices={vertexCount}, " +
      $"indices={indexCount}, resolvedReferences={referenceCount}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(shapeCount, Is.GreaterThan(0), "No static shapes were found in the custom OVL fixtures.");
      Assert.That(referenceCount, Is.GreaterThan(0), "No SHS resource references were resolved.");
      Assert.That(townHallMesh4Ftx, Is.EqualTo("RS-Base:ftx"));
    }
  }

  private static void CopyResource(Assembly assembly, string resourceName, string path) {
    using var input = assembly.GetManifestResourceStream(resourceName);
    Assert.That(input, Is.Not.Null, $"Embedded resource '{resourceName}' not found.");
    using var output = File.Create(path);
    input.CopyTo(output);
  }

  public enum MalformedShape {
    TruncatedHeader,
    OversizedMeshCount,
    MissingMeshRelocation,
    TruncatedVertices,
    OutOfRangeIndex,
    NonTriangleIndexCount,
    AggregateCountMismatch,
    UnknownResourceReference,
    UnterminatedEffectName,
    NonFiniteVertex,
    TruncatedPlacementIndices,
    PlacementIndexCountOverflow,
    OversizedEffectCount,
    EffectNameTooLong,
    DuplicateMeshFan,
    WrongSymbolReferenceType,
    WrongSymbolReferenceOwner,
    ConflictingRawAndSymbolReference,
    WrongDirectReferenceType
  }

  private sealed class StaticShapeFixture {
    private const uint ShapeAddress = 100;
    private const uint MeshPointersAddress = 200;
    private const uint MeshAddress = 300;
    private const uint VerticesAddress = 400;
    private const uint IndicesAddress = 600;
    private const uint PositionsAddress = 700;
    private const uint NamesAddress = 800;
    private const uint NameAddress = 900;
    private const uint FtxAddress = 1000;
    private const uint TxsAddress = 1100;

    private readonly FakeStaticShapeDataSource source = new();

    public StaticShapeFixture() {
      var shape = source.AddBlock(ShapeAddress, 56);
      WriteVector3(shape, 0, new Vector3(-1, -2, -3));
      WriteVector3(shape, 12, new Vector3(4, 5, 6));
      WriteUInt32(shape, 24, 3);
      WriteUInt32(shape, 28, 3);
      WriteUInt32(shape, 32, 1);
      WriteUInt32(shape, 36, 1);
      WritePointer(shape, ShapeAddress, 40, MeshPointersAddress);
      WriteUInt32(shape, 44, 1);
      WritePointer(shape, ShapeAddress, 48, PositionsAddress);
      WritePointer(shape, ShapeAddress, 52, NamesAddress);

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

      var vertices = source.AddBlock(VerticesAddress, 36 * 3);
      WriteVertex(vertices, 0, new Vector3(1, 2, 3), Vector3.UnitY, new Vector2(0.25f, 0.75f));
      WriteVertex(vertices, 36, new Vector3(4, 5, 6), Vector3.UnitY, Vector2.Zero);
      WriteVertex(vertices, 72, new Vector3(7, 8, 9), Vector3.UnitY, Vector2.One);

      var indices = source.AddBlock(IndicesAddress, 12);
      WriteUInt32(indices, 0, 0);
      WriteUInt32(indices, 4, 1);
      WriteUInt32(indices, 8, 2);

      var positions = source.AddBlock(PositionsAddress, 64);
      WriteMatrix(positions, 0, Matrix4x4.Identity);
      var names = source.AddBlock(NamesAddress, 4);
      WritePointer(names, NamesAddress, 0, NameAddress);
      source.AddBlock(NameAddress, Encoding.ASCII.GetBytes("spark\0"));
      source.AddBlock(FtxAddress, 4);
      source.AddBlock(TxsAddress, 4);
      source.MutableResourceKeys[FtxAddress] = "water:ftx";
      source.MutableResourceKeys[TxsAddress] = "opaque:txs";
      source.MutableResourceReferences[MeshAddress + 4] =
        new StaticShapeResourceReference("water:ftx", ShapeAddress);
      source.MutableResourceReferences[MeshAddress + 8] =
        new StaticShapeResourceReference("opaque:txs", ShapeAddress);
    }

    public StaticShape Decode() => StaticShapes.Decode("synthetic", ShapeAddress, source);
    public StaticShape Decode(StaticShapeDecodeLimits limits) =>
      StaticShapes.Decode("synthetic", ShapeAddress, source, limits);

    public void UsePlacementTriangleList(uint[] indices) {
      Assert.That(indices.Length, Is.Positive);
      Assert.That(indices.Length % 3, Is.Zero);
      WriteUInt32(source.Blocks[MeshAddress], 12, 1);
      WriteUInt32(source.Blocks[MeshAddress], 28, Convert.ToUInt32(indices.Length / 3));
      WriteUInt32(source.Blocks[ShapeAddress], 28, Convert.ToUInt32(indices.Length / 3));
      source.ReplaceBlock(IndicesAddress, EncodeIndices(indices));
    }

    public void UsePlacementAxisStreams(uint[] x, uint[] y, uint[] z) {
      Assert.That(x.Length, Is.Positive);
      Assert.That(x.Length % 3, Is.Zero);
      Assert.That(y, Has.Length.EqualTo(x.Length));
      Assert.That(z, Has.Length.EqualTo(x.Length));
      WriteUInt32(source.Blocks[MeshAddress], 12, 1);
      WriteUInt32(source.Blocks[MeshAddress], 28, Convert.ToUInt32(x.Length));
      WriteUInt32(source.Blocks[ShapeAddress], 28, Convert.ToUInt32(x.Length));
      source.ReplaceBlock(IndicesAddress, EncodeIndices([.. x, .. y, .. z]));
    }

    public void MakeMalformed(MalformedShape malformed) {
      switch (malformed) {
        case MalformedShape.TruncatedHeader:
          source.ReplaceBlock(ShapeAddress, source.Blocks[ShapeAddress][..55]);
          break;
        case MalformedShape.OversizedMeshCount:
          WriteUInt32(source.Blocks[ShapeAddress], 36, uint.MaxValue);
          break;
        case MalformedShape.MissingMeshRelocation:
          source.Relocations.Remove(MeshPointersAddress);
          break;
        case MalformedShape.TruncatedVertices:
          source.ReplaceBlock(VerticesAddress, source.Blocks[VerticesAddress][..^1]);
          break;
        case MalformedShape.OutOfRangeIndex:
          WriteUInt32(source.Blocks[IndicesAddress], 8, 3);
          break;
        case MalformedShape.NonTriangleIndexCount:
          WriteUInt32(source.Blocks[ShapeAddress], 28, 2);
          WriteUInt32(source.Blocks[MeshAddress], 28, 2);
          break;
        case MalformedShape.AggregateCountMismatch:
          WriteUInt32(source.Blocks[ShapeAddress], 24, 4);
          break;
        case MalformedShape.UnknownResourceReference:
          source.MutableResourceReferences.Remove(MeshAddress + 4);
          WriteUInt32(source.Blocks[MeshAddress], 4, FtxAddress);
          source.Relocations[MeshAddress + 4] = FtxAddress;
          source.MutableResourceKeys.Remove(FtxAddress);
          break;
        case MalformedShape.UnterminatedEffectName:
          source.ReplaceBlock(NameAddress, Encoding.ASCII.GetBytes("spark"));
          break;
        case MalformedShape.NonFiniteVertex:
          WriteSingle(source.Blocks[VerticesAddress], 0, float.NaN);
          break;
        case MalformedShape.TruncatedPlacementIndices:
          UsePlacementTriangleList([0, 1, 2, 2, 1, 0]);
          source.ReplaceBlock(IndicesAddress, source.Blocks[IndicesAddress][..^1]);
          break;
        case MalformedShape.PlacementIndexCountOverflow:
          WriteUInt32(source.Blocks[MeshAddress], 12, 1);
          WriteUInt32(source.Blocks[MeshAddress], 28, uint.MaxValue);
          WriteUInt32(source.Blocks[ShapeAddress], 28, uint.MaxValue);
          break;
        case MalformedShape.OversizedEffectCount:
          WriteUInt32(source.Blocks[ShapeAddress], 44, 65_537);
          break;
        case MalformedShape.EffectNameTooLong:
          source.ReplaceBlock(NameAddress, new byte[4 * 1024]);
          break;
        case MalformedShape.DuplicateMeshFan:
          MakeDuplicateMeshFan(10_000);
          break;
        case MalformedShape.WrongSymbolReferenceType:
          source.MutableResourceReferences[MeshAddress + 4] =
            new StaticShapeResourceReference("opaque:txs", ShapeAddress);
          break;
        case MalformedShape.WrongSymbolReferenceOwner:
          source.MutableResourceReferences[MeshAddress + 4] =
            new StaticShapeResourceReference("water:ftx", ShapeAddress + 1);
          break;
        case MalformedShape.ConflictingRawAndSymbolReference:
          WriteUInt32(source.Blocks[MeshAddress], 4, FtxAddress);
          break;
        case MalformedShape.WrongDirectReferenceType:
          source.MutableResourceReferences.Remove(MeshAddress + 4);
          WritePointer(source.Blocks[MeshAddress], MeshAddress, 4, FtxAddress);
          source.MutableResourceKeys[FtxAddress] = "water:txs";
          break;
      }
    }

    private void MakeDuplicateMeshFan(int count) {
      var pointers = new byte[count * 4];
      for (var i = 0; i < count; i++)
        WritePointer(pointers, MeshPointersAddress, i * 4, MeshAddress);
      source.ReplaceBlock(MeshPointersAddress, pointers);
      WriteUInt32(source.Blocks[ShapeAddress], 24, Convert.ToUInt32(count * 3));
      WriteUInt32(source.Blocks[ShapeAddress], 28, Convert.ToUInt32(count * 3));
      WriteUInt32(source.Blocks[ShapeAddress], 32, Convert.ToUInt32(count));
      WriteUInt32(source.Blocks[ShapeAddress], 36, Convert.ToUInt32(count));
    }

    private static byte[] EncodeIndices(uint[] indices) {
      var bytes = new byte[indices.Length * sizeof(uint)];
      for (var i = 0; i < indices.Length; i++) WriteUInt32(bytes, i * sizeof(uint), indices[i]);
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
      Vector2 texCoord
    ) {
      WriteVector3(bytes, offset, position);
      WriteVector3(bytes, offset + 12, normal);
      WriteUInt32(bytes, offset + 24, 4_279_312_947);
      WriteSingle(bytes, offset + 28, texCoord.X);
      WriteSingle(bytes, offset + 32, texCoord.Y);
    }

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

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);
  }

  private sealed class FakeStaticShapeDataSource : IStaticShapeDataSource {
    public Dictionary<uint, byte[]> Blocks { get; } = [];
    public Dictionary<uint, uint> Relocations { get; } = [];
    public Dictionary<uint, string> MutableResourceKeys { get; } = [];
    public Dictionary<uint, StaticShapeResourceReference> MutableResourceReferences { get; } = [];
    public IReadOnlyDictionary<uint, string> ResourceKeys => MutableResourceKeys;
    public IReadOnlyDictionary<uint, StaticShapeResourceReference> ResourceReferences =>
      MutableResourceReferences;

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

    public bool TryReadNullTerminatedString(uint address, int maximumLength, out string value) {
      foreach (var block in Blocks) {
        if (address < block.Key || address >= block.Key + block.Value.Length) continue;
        var offset = Convert.ToInt32(address - block.Key);
        var available = Math.Min(maximumLength, block.Value.Length - offset);
        var end = Array.IndexOf(block.Value, Convert.ToByte(0), offset, available);
        if (end < 0) break;
        value = Encoding.ASCII.GetString(block.Value, offset, end - offset);
        return true;
      }
      value = string.Empty;
      return false;
    }
  }
}
