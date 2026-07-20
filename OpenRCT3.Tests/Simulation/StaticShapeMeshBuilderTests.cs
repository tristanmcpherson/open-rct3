// Static Shape Mesh Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class StaticShapeMeshBuilderTests {
  [Test]
  public void BuildBatches_ConvertsCoordinatesAndPreservesVertexAttributes() {
    var color = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);
    var texCoord = new Vector2(0.25f, 0.75f);
    var vertices = TriangleVertices();
    vertices[0] = new StaticShapeVertex(
      new Vector3(1f, 2f, 3f),
      new Vector3(4f, 5f, 6f),
      texCoord,
      color);
    var shape = Shape(Mesh(vertices: vertices));

    var vertex = StaticShapeMeshBuilder.BuildBatches(shape).Single().Mesh.Vertices[0];

    using (Assert.EnterMultipleScope()) {
      Assert.That(vertex.Position, Is.EqualTo(new Vector3(-3f, 1f, 2f)));
      Assert.That(vertex.Normal, Is.EqualTo(new Vector3(-6f, 4f, 5f)));
      Assert.That(vertex.TexCoord, Is.EqualTo(texCoord));
      Assert.That(vertex.Color, Is.EqualTo(color));
    }
  }

  [Test]
  public void BuildBatches_HandednessChangeKeepsCcwWindingAlignedWithNormals() {
    var batch = StaticShapeMeshBuilder.BuildBatches(Shape(Mesh())).Single();
    var mesh = batch.Mesh;

    Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 2, 0, 1 }));
    var a = mesh.Vertices[Convert.ToInt32(mesh.Indices[0])].Position;
    var b = mesh.Vertices[Convert.ToInt32(mesh.Indices[1])].Position;
    var c = mesh.Vertices[Convert.ToInt32(mesh.Indices[2])].Position;
    var faceNormal = Vector3.Normalize(Vector3.Cross(b - a, c - a));

    using (Assert.EnterMultipleScope()) {
      Assert.That(faceNormal, Is.EqualTo(Vector3.UnitZ));
      Assert.That(mesh.Vertices.Select(vertex => vertex.Normal),
        Is.All.EqualTo(Vector3.UnitZ));
    }
  }

  [Test]
  public void BuildBatches_PreservesSourceOrderNamesAndMaterialMetadata() {
    var zeta = Mesh(
      name: "zeta",
      supportType: -1,
      ftxRef: "RS-Zeta:ftx",
      txsRef: "SIAlpha:txs",
      transparency: 2,
      textureFlags: 68,
      sides: 1) with {
      IndexLayout = StaticShapeIndexLayout.PlacementTriangleList,
      StoredIndexCount = 1
    };
    var alpha = Mesh(
      name: "alpha",
      supportType: 4,
      ftxRef: "RS-Alpha:ftx",
      txsRef: "SIOpaque:txs",
      transparency: 0,
      textureFlags: 12,
      sides: 3);

    var batches = StaticShapeMeshBuilder.BuildBatches(Shape(zeta, alpha));

    Assert.That(batches.Select(batch => batch.SourceMeshName),
      Is.EqualTo(new[] { "zeta", "alpha" }));
    using (Assert.EnterMultipleScope()) {
      Assert.That(batches[0].SourceMeshIndex, Is.EqualTo(0));
      Assert.That(batches[0].Mesh.Name, Is.EqualTo("zeta"));
      Assert.That(batches[0].SupportType, Is.EqualTo(-1));
      Assert.That(batches[0].FtxRef, Is.EqualTo("RS-Zeta:ftx"));
      Assert.That(batches[0].TxsRef, Is.EqualTo("SIAlpha:txs"));
      Assert.That(batches[0].Transparency, Is.EqualTo(2));
      Assert.That(batches[0].TextureFlags, Is.EqualTo(68));
      Assert.That(batches[0].Sides, Is.EqualTo(1));
      Assert.That(batches[1].SourceMeshIndex, Is.EqualTo(1));
      Assert.That(batches[1].Mesh.Name, Is.EqualTo("alpha"));
      Assert.That(batches[1].FtxRef, Is.EqualTo("RS-Alpha:ftx"));
      Assert.That(batches[1].TxsRef, Is.EqualTo("SIOpaque:txs"));
      Assert.That(batches[1].Transparency, Is.Zero);
      Assert.That(batches[1].TextureFlags, Is.EqualTo(12));
      Assert.That(batches[1].Sides, Is.EqualTo(3));
    }
  }

  [Test]
  public void BuildBatches_PlacementTriangleListUsesAllDecodedTriangles() {
    var source = Mesh(transparency: 1, indices: [0, 2, 1]) with {
      IndexLayout = StaticShapeIndexLayout.PlacementTriangleList,
      StoredIndexCount = 1
    };

    var mesh = StaticShapeMeshBuilder.BuildBatches(Shape(source)).Single().Mesh;

    Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 2, 0, 1 }));
  }

  [Test]
  public void BuildBatches_DivisiblePlacementTriangleListUsesAllDecodedTriangles() {
    var source = Mesh(
      transparency: 1,
      indices: [0, 2, 1, 1, 0, 2, 2, 1, 0]) with {
      IndexLayout = StaticShapeIndexLayout.PlacementTriangleList,
      StoredIndexCount = 3
    };

    var mesh = StaticShapeMeshBuilder.BuildBatches(Shape(source)).Single().Mesh;

    Assert.That(mesh.Indices,
      Is.EqualTo(new uint[] { 2, 0, 1, 0, 1, 2, 1, 2, 0 }));
  }

  [Test]
  public void BuildBatches_SortedPlacementTriangleListUsesTheRetainedPermutationOnce() {
    var source = Mesh(
      transparency: 1,
      indices: [0, 2, 1, 1, 2, 0]) with {
      IndexLayout = StaticShapeIndexLayout.PlacementSortedTriangleList,
      StoredIndexCount = 6,
      PlacementSortPermutations = [
        new uint[] { 0, 2, 1, 1, 2, 0 },
        new uint[] { 1, 2, 0, 0, 2, 1 },
        new uint[] { 2, 0, 1, 1, 0, 2 }
      ]
    };

    var mesh = StaticShapeMeshBuilder.BuildBatches(Shape(source)).Single().Mesh;

    Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 2, 0, 1, 2, 1, 0 }));
  }

  [Test]
  public void BuildBatches_OutOfRangeIndexFailsClosed() {
    var shape = Shape(Mesh(indices: [0, 2, 3]));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      StaticShapeMeshBuilder.BuildBatches(shape)));

    Assert.That(exception!.Message, Does.Contain("references vertex 3"));
  }

  [Test]
  public void BuildBatches_NonFiniteVertexFailsClosed() {
    var vertices = TriangleVertices();
    vertices[1] = vertices[1] with {
      Position = new Vector3(float.NaN, 0f, 0f)
    };
    var shape = Shape(Mesh(vertices: vertices));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      StaticShapeMeshBuilder.BuildBatches(shape)));

    Assert.That(exception!.Message, Does.Contain("non-finite value"));
  }

  [Test]
  public void BuildBatches_NonFiniteNormalFailsClosed() {
    var vertices = TriangleVertices();
    vertices[1] = vertices[1] with {
      Normal = new Vector3(0f, float.PositiveInfinity, 0f)
    };
    var shape = Shape(Mesh(vertices: vertices));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      StaticShapeMeshBuilder.BuildBatches(shape)));

    Assert.That(exception!.Message, Does.Contain("non-finite value"));
  }

  [Test]
  public void BuildBatches_StoredIndexCountMismatchFailsClosed() {
    var source = Mesh() with { StoredIndexCount = 6 };

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      StaticShapeMeshBuilder.BuildBatches(Shape(source))));

    Assert.That(exception!.Message, Does.Contain("does not match"));
  }

  [Test]
  public void BuildBatches_SortedPlacementCountMismatchFailsClosed() {
    var source = Mesh(
      transparency: 1,
      indices: [0, 2, 1, 0, 2, 1, 0, 2, 1]) with {
      IndexLayout = StaticShapeIndexLayout.PlacementSortedTriangleList,
      StoredIndexCount = 3
    };

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      StaticShapeMeshBuilder.BuildBatches(Shape(source))));

    Assert.That(exception!.Message,
      Does.Contain("does not match the 9 retained permutation indices"));
  }

  private static StaticShape Shape(params StaticShapeMesh[] meshes) => new(
    "shape",
    new Vector3(-1f),
    new Vector3(1f),
    meshes,
    []);

  private static StaticShapeMesh Mesh(
    string name = "mesh",
    int supportType = 0,
    string? ftxRef = "texture:ftx",
    string? txsRef = "SIOpaque:txs",
    uint transparency = 0,
    uint textureFlags = 0,
    uint sides = 3,
    StaticShapeVertex[]? vertices = null,
    uint[]? indices = null
  ) {
    indices ??= [0, 2, 1];
    return new StaticShapeMesh(
      name,
      supportType,
      ftxRef,
      txsRef,
      transparency,
      textureFlags,
      sides,
      vertices ?? TriangleVertices(),
      indices) {
      IndexLayout = StaticShapeIndexLayout.TriangleList,
      StoredIndexCount = Convert.ToUInt32(indices.Length)
    };
  }

  private static StaticShapeVertex[] TriangleVertices() => [
    Vertex(new Vector3(0f, 0f, 0f)),
    Vertex(new Vector3(0f, 0f, -1f)),
    Vertex(new Vector3(1f, 0f, 0f)),
  ];

  private static StaticShapeVertex Vertex(Vector3 position) => new(
    position,
    Vector3.UnitY,
    Vector2.Zero,
    Vector4.One);
}
