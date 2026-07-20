// Model Definition Mesh Builder Tests
//
// Copyright Â© 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class ModelDefinitionMeshBuilderTests {
  [Test]
  public void BuildBatches_PreservesSourceOrderAndNeutralRestPoseAttributes() {
    var color = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);
    var texCoord = new Vector2(0.25f, 0.75f);
    var vertices = TriangleVertices();
    vertices[0] = new BoneShapeVertex(
      new Vector3(1, 2, 3),
      new Vector3(4, 5, 6),
      texCoord,
      color,
      new BoneShapeSkinning(0, 255, 255, 255, 255, 0, 0, 0));
    var definition = Definition(
      [Group(Mesh(vertices: vertices)), Group(), Group(Mesh(), Mesh())],
      [new ModelBone(
        "root",
        new Vector4(10, 20, 30, 1),
        new Vector4(0, 0, 0, 1),
        Matrix4x4.CreateScale(100) * Matrix4x4.CreateTranslation(500, 600, 700),
        ushort.MaxValue,
        0)]);

    var batches = ModelDefinitionMeshBuilder.BuildBatches(definition);

    try {
      using (Assert.EnterMultipleScope()) {
        Assert.That(
          batches.Select(batch => batch.SourceGroupIndex),
          Is.EqualTo(new[] { 0, 2, 2 }));
        Assert.That(
          batches.Select(batch => batch.SourceMeshIndex),
          Is.EqualTo(new[] { 0, 0, 1 }));
        Assert.That(
          batches.Select(batch => batch.SourceMeshName),
          Is.EqualTo(new[] {
            "animal/group-0/mesh-0",
            "animal/group-2/mesh-0",
            "animal/group-2/mesh-1",
          }));
        Assert.That(
          batches.Select(batch => batch.Mesh.Name),
          Is.EqualTo(batches.Select(batch => batch.SourceMeshName)));
        Assert.That(batches[0].Mesh.Vertices[0].Position,
          Is.EqualTo(new Vector3(1, 3, 2)));
        Assert.That(batches[0].Mesh.Vertices[0].Normal,
          Is.EqualTo(new Vector3(4, 6, 5)));
        Assert.That(batches[0].Mesh.Vertices[0].TexCoord, Is.EqualTo(texCoord));
        Assert.That(batches[0].Mesh.Vertices[0].Color, Is.EqualTo(color));
        Assert.That(batches[0].Mesh.Indices, Is.EqualTo(new uint[] { 2, 0, 1 }));
        Assert.That(definition.Groups[0].Meshes[0].Indices,
          Is.EqualTo(new uint[] { 0, 2, 1 }));
      }
    } finally {
      foreach (var batch in batches) batch.Mesh.Dispose();
    }
  }

  [Test]
  public void BuildBatches_EmptyGroupsProduceNoBatches() {
    var batches = ModelDefinitionMeshBuilder.BuildBatches(
      Definition([Group(), Group()]));

    Assert.That(batches, Is.Empty);
  }

  [TestCase(MalformedDefinition.MissingName)]
  [TestCase(MalformedDefinition.NullBones)]
  [TestCase(MalformedDefinition.BoneCountMismatch)]
  [TestCase(MalformedDefinition.NullGroups)]
  [TestCase(MalformedDefinition.NullGroup)]
  [TestCase(MalformedDefinition.NullMeshes)]
  [TestCase(MalformedDefinition.MeshCountMismatch)]
  [TestCase(MalformedDefinition.NullSurfaceRecords)]
  [TestCase(MalformedDefinition.SurfaceRecordCountMismatch)]
  [TestCase(MalformedDefinition.NullMesh)]
  [TestCase(MalformedDefinition.UnsupportedFvf)]
  [TestCase(MalformedDefinition.UnsupportedMultiplier)]
  [TestCase(MalformedDefinition.NullVertices)]
  [TestCase(MalformedDefinition.EmptyVertices)]
  [TestCase(MalformedDefinition.VertexCountMismatch)]
  [TestCase(MalformedDefinition.NullIndices)]
  [TestCase(MalformedDefinition.EmptyIndices)]
  [TestCase(MalformedDefinition.IndexCountMismatch)]
  [TestCase(MalformedDefinition.NonTriangleIndices)]
  [TestCase(MalformedDefinition.NullVertex)]
  [TestCase(MalformedDefinition.NonFinitePosition)]
  [TestCase(MalformedDefinition.NonFiniteNormal)]
  [TestCase(MalformedDefinition.NonFiniteTexCoord)]
  [TestCase(MalformedDefinition.NonFiniteColor)]
  [TestCase(MalformedDefinition.OutOfRangeIndex)]
  public void BuildBatches_MalformedGeometryFailsClosed(MalformedDefinition malformed) {
    var definition = MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() =>
      ModelDefinitionMeshBuilder.BuildBatches(definition)));
  }

  [Test]
  public void BuildBatches_LaterFailureDisposesEarlierMeshes() {
    var invalidIndices = new uint[] { 0, 2, 3 };
    var definition = Definition([Group(Mesh(), Mesh(indices: invalidIndices))]);
    var created = new List<Mesh>();
    var disposed = new List<Mesh>();
    var operations = new ModelDefinitionMeshBuilderOperations(
      (vertices, indices, name) => {
        var mesh = new Mesh(vertices, indices) { Name = name };
        created.Add(mesh);
        return mesh;
      },
      mesh => {
        disposed.Add(mesh);
        mesh.Dispose();
      });

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelDefinitionMeshBuilder.BuildBatches(definition, operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("references vertex 3"));
      Assert.That(created, Has.Count.EqualTo(1));
      Assert.That(disposed, Is.EqualTo(created));
      Assert.That(created[0].State, Is.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void BuildBatches_CleanupFailureRetainsBuildAndDisposalErrors() {
    var definition = Definition([Group(Mesh(), Mesh(indices: [0, 2, 3]))]);
    var cleanupError = new IOException("cleanup failed");
    var operations = new ModelDefinitionMeshBuilderOperations(
      (vertices, indices, name) => new Mesh(vertices, indices) { Name = name },
      _ => throw cleanupError);

    var exception = Assert.Throws<AggregateException>(new Action(() =>
      ModelDefinitionMeshBuilder.BuildBatches(definition, operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.InnerExceptions[0], Is.TypeOf<InvalidDataException>());
      Assert.That(exception.InnerExceptions[1], Is.SameAs(cleanupError));
    }
  }

  [Test]
  public void BuildBatches_DisposedFactoryMeshFailsClosed() {
    var definition = Definition([Group(Mesh())]);
    var disposedMesh = new Mesh([], []);
    disposedMesh.Dispose();
    var operations = new ModelDefinitionMeshBuilderOperations(
      (_, _, _) => disposedMesh,
      _ => throw new AssertionException("A pre-disposed factory mesh is not builder-owned."));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelDefinitionMeshBuilder.BuildBatches(definition, operations)));

    Assert.That(exception!.Message, Does.Contain("returned a disposed mesh"));
  }

  [Test]
  public void BuildBatches_ReusedFactoryMeshFailsClosedAndDisposesOnce() {
    var definition = Definition([Group(Mesh(), Mesh())]);
    var reusedMesh = new Mesh([], []);
    var disposeCount = 0;
    var operations = new ModelDefinitionMeshBuilderOperations(
      (_, _, _) => reusedMesh,
      mesh => {
        disposeCount++;
        mesh.Dispose();
      });

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelDefinitionMeshBuilder.BuildBatches(definition, operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("reused a mesh instance"));
      Assert.That(disposeCount, Is.EqualTo(1));
      Assert.That(reusedMesh.State, Is.EqualTo(State.Disposed));
    }
  }

  private static ModelDefinition MakeMalformed(MalformedDefinition malformed) {
    var definition = Definition([Group(Mesh())]);
    var group = definition.Groups[0];
    var mesh = group.Meshes[0];
    var vertices = mesh.Vertices.ToArray();
    return malformed switch {
      MalformedDefinition.MissingName => definition with { Name = "" },
      MalformedDefinition.NullBones => definition with { Bones = null! },
      MalformedDefinition.BoneCountMismatch => definition with { Count0 = 1 },
      MalformedDefinition.NullGroups => definition with { Groups = null! },
      MalformedDefinition.NullGroup => definition with {
        Groups = new ModelGroup[] { null! },
      },
      MalformedDefinition.NullMeshes => definition with {
        Groups = [group with { Meshes = null! }],
      },
      MalformedDefinition.MeshCountMismatch => definition with {
        Groups = [group with { MeshCount = 2 }],
      },
      MalformedDefinition.NullSurfaceRecords => definition with {
        Groups = [group with { SurfaceRecords = null! }],
      },
      MalformedDefinition.SurfaceRecordCountMismatch => definition with {
        Groups = [group with { SurfaceRecordCount = 1 }],
      },
      MalformedDefinition.NullMesh => definition with {
        Groups = [group with { Meshes = new ModelMesh[] { null! } }],
      },
      MalformedDefinition.UnsupportedFvf => WithMesh(definition, mesh with { Fvf = 0x1304 }),
      MalformedDefinition.UnsupportedMultiplier =>
        WithMesh(definition, mesh with { Multiplier = 2 }),
      MalformedDefinition.NullVertices =>
        WithMesh(definition, mesh with { Vertices = null! }),
      MalformedDefinition.EmptyVertices =>
        WithMesh(definition, mesh with { StoredVertexCount = 0, Vertices = [] }),
      MalformedDefinition.VertexCountMismatch =>
        WithMesh(definition, mesh with { StoredVertexCount = 2 }),
      MalformedDefinition.NullIndices =>
        WithMesh(definition, mesh with { Indices = null! }),
      MalformedDefinition.EmptyIndices => WithMesh(
        definition,
        mesh with { StoredIndexCount = 0, Indices = [] }),
      MalformedDefinition.IndexCountMismatch =>
        WithMesh(definition, mesh with { StoredIndexCount = 6 }),
      MalformedDefinition.NonTriangleIndices => WithMesh(
        definition,
        mesh with { StoredIndexCount = 2, Indices = new uint[] { 0, 1 } }),
      MalformedDefinition.NullVertex => WithMesh(
        definition,
        mesh with { Vertices = new BoneShapeVertex[] { null!, vertices[1], vertices[2] } }),
      MalformedDefinition.NonFinitePosition => WithVertex(
        definition,
        vertices[0] with { Position = new Vector3(float.NaN, 0, 0) }),
      MalformedDefinition.NonFiniteNormal => WithVertex(
        definition,
        vertices[0] with { Normal = new Vector3(0, float.PositiveInfinity, 0) }),
      MalformedDefinition.NonFiniteTexCoord => WithVertex(
        definition,
        vertices[0] with { TexCoord = new Vector2(0, float.NegativeInfinity) }),
      MalformedDefinition.NonFiniteColor => WithVertex(
        definition,
        vertices[0] with { Color = new Vector4(0, 0, float.NaN, 1) }),
      MalformedDefinition.OutOfRangeIndex =>
        WithMesh(definition, mesh with { Indices = new uint[] { 0, 2, 3 } }),
      _ => throw new ArgumentOutOfRangeException(nameof(malformed)),
    };
  }

  private static ModelDefinition WithVertex(
    ModelDefinition definition,
    BoneShapeVertex vertex
  ) {
    var mesh = definition.Groups[0].Meshes[0];
    var vertices = mesh.Vertices.ToArray();
    vertices[0] = vertex;
    return WithMesh(definition, mesh with { Vertices = vertices });
  }

  private static ModelDefinition WithMesh(
    ModelDefinition definition,
    ModelMesh mesh
  ) {
    var group = definition.Groups[0];
    return definition with { Groups = [group with { Meshes = [mesh] }] };
  }

  private static ModelDefinition Definition(
    IReadOnlyList<ModelGroup> groups,
    IReadOnlyList<ModelBone>? bones = null
  ) {
    bones ??= [];
    return new ModelDefinition(
      "animal",
      "fixture.common.ovl",
      100,
      200,
      Convert.ToUInt32(bones.Count),
      0,
      0,
      0) {
      Bones = bones,
      Groups = groups,
    };
  }

  private static ModelGroup Group(params ModelMesh[] meshes) => new(
    0,
    Convert.ToUInt16(meshes.Length),
    0,
    0,
    [],
    0,
    meshes,
    []);

  private static ModelMesh Mesh(
    BoneShapeVertex[]? vertices = null,
    uint[]? indices = null
  ) {
    vertices ??= TriangleVertices();
    indices ??= [0, 2, 1];
    return new ModelMesh(
      0x1305,
      Convert.ToUInt32(indices.Length),
      1,
      Convert.ToUInt16(vertices.Length),
      0,
      0,
      0,
      0,
      vertices,
      indices);
  }

  private static BoneShapeVertex[] TriangleVertices() => [
    Vertex(new Vector3(0, 0, 0)),
    Vertex(new Vector3(0, 0, -1)),
    Vertex(new Vector3(1, 0, 0)),
  ];

  private static BoneShapeVertex Vertex(Vector3 position) => new(
    position,
    Vector3.UnitY,
    Vector2.Zero,
    Vector4.One,
    new BoneShapeSkinning(255, 255, 255, 255, 0, 0, 0, 0));
}

public enum MalformedDefinition {
  MissingName,
  NullBones,
  BoneCountMismatch,
  NullGroups,
  NullGroup,
  NullMeshes,
  MeshCountMismatch,
  NullSurfaceRecords,
  SurfaceRecordCountMismatch,
  NullMesh,
  UnsupportedFvf,
  UnsupportedMultiplier,
  NullVertices,
  EmptyVertices,
  VertexCountMismatch,
  NullIndices,
  EmptyIndices,
  IndexCountMismatch,
  NonTriangleIndices,
  NullVertex,
  NonFinitePosition,
  NonFiniteNormal,
  NonFiniteTexCoord,
  NonFiniteColor,
  OutOfRangeIndex,
}
