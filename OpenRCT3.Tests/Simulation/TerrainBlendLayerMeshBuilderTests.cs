// Terrain Blend Layer Mesh Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class TerrainBlendLayerMeshBuilderTests {
  [Test]
  public void BuildBatches_DecodedTerrain_EmitsExactWeightedTopGeometry() {
    var terrain = Terrain.FromData(new DatTerrainData(
      1,
      1,
      -12f,
      7f,
      4f,
      5f,
      [new DatTerrainCell(-1.25f, 2.5f, 3.75f, -4f, 11, 6)]));
    var color = new Vector4(0.2f, 0.3f, 0.4f, 0.6f);

    var batches = Build(
      terrain,
      color,
      _ => TerrainTypeKind.GroundUnblended,
      _ => new Vector2(0.1f, 0.2f));

    try {
      var batch = batches.Single();
      Assert.That(batch.Role, Is.EqualTo(TerrainBlendLayerPassRole.Base));
      Assert.That(batch.SurfaceIndex, Is.EqualTo(11));
      Assert.That(batch.Mesh.Name, Is.EqualTo("Test Blend Base Surface 11"));
      Assert.That(batch.Mesh.Vertices.Select(vertex => vertex.Position), Is.EqualTo(new[] {
        new Vector3(-12f, 7f, -1.25f),
        new Vector3(-8f, 7f, 2.5f),
        new Vector3(-8f, 12f, -4f),
        new Vector3(-12f, 12f, 3.75f),
      }));
      Assert.That(batch.Mesh.Indices, Is.EqualTo(new uint[] { 0, 1, 3, 1, 2, 3 }));
      Assert.That(batch.Mesh.Vertices.Select(vertex => vertex.TexCoord), Is.EqualTo(new[] {
        new Vector2(0f, 0f),
        new Vector2(0.4f, 0f),
        new Vector2(0.4f, 1f),
        new Vector2(0f, 1f),
      }));

      var sw = batch.Mesh.Vertices[0].Position;
      var se = batch.Mesh.Vertices[1].Position;
      var ne = batch.Mesh.Vertices[2].Position;
      var nw = batch.Mesh.Vertices[3].Position;
      var southWestNormal = Vector3.Normalize(Vector3.Cross(se - sw, nw - sw));
      var northEastNormal = Vector3.Normalize(Vector3.Cross(nw - ne, se - ne));
      var sharedNormal = Vector3.Normalize(southWestNormal + northEastNormal);
      Assert.That(batch.Mesh.Vertices.Select(vertex => vertex.Normal), Is.EqualTo(new[] {
        southWestNormal,
        sharedNormal,
        northEastNormal,
        sharedNormal,
      }));
      Assert.That(batch.Mesh.Vertices.Select(vertex => vertex.Color), Is.EqualTo(new[] {
        new Vector4(color.X, color.Y, color.Z, 1f),
        new Vector4(color.X, color.Y, color.Z, 1f),
        new Vector4(color.X, color.Y, color.Z, 1f),
        new Vector4(color.X, color.Y, color.Z, 1f),
      }));
    } finally {
      Dispose(batches);
    }
  }

  [Test]
  public void BuildBatches_BlendedNeighbors_SeparatesAndSortsPassRolesAndSurfaces() {
    var terrain = NewTerrain(
      2, 1, (x, _) => x == 0 ? Convert.ToByte(30) : Convert.ToByte(8));

    var batches = Build(
      terrain,
      new Vector4(0.2f, 0.3f, 0.4f, 0.9f),
      _ => TerrainTypeKind.GroundBlended,
      surface => surface == 8
        ? new Vector2(0.1f, 0.2f)
        : new Vector2(0.25f, 0.5f));

    try {
      Assert.That(batches.Select(batch => (batch.Role, batch.SurfaceIndex)),
        Is.EqualTo(new[] {
          (TerrainBlendLayerPassRole.Base, Convert.ToByte(8)),
          (TerrainBlendLayerPassRole.Base, Convert.ToByte(30)),
          (TerrainBlendLayerPassRole.Contribution, Convert.ToByte(8)),
          (TerrainBlendLayerPassRole.Contribution, Convert.ToByte(30)),
        }));
      foreach (var batch in batches) {
        Assert.That(batch.Mesh.Vertices, Has.Count.EqualTo(4));
        Assert.That(batch.Mesh.Indices, Has.Count.EqualTo(6));
        Assert.That(batch.Mesh.Vertices.Select(vertex => vertex.Color.X),
          Is.All.EqualTo(0.2f));
        Assert.That(batch.Mesh.Vertices.Select(vertex => vertex.Color.Y),
          Is.All.EqualTo(0.3f));
        Assert.That(batch.Mesh.Vertices.Select(vertex => vertex.Color.Z),
          Is.All.EqualTo(0.4f));
      }

      AssertWeights(batches[0].Mesh, 127, 255, 255, 127);
      AssertWeights(batches[1].Mesh, 255, 127, 127, 255);
      AssertWeights(batches[2].Mesh, 0, 127, 127, 0);
      AssertWeights(batches[3].Mesh, 127, 0, 0, 127);
      Assert.That(batches[0].Mesh.Vertices.Select(vertex => vertex.TexCoord),
        Is.EqualTo(new[] {
          new Vector2(0.4f, 0f),
          new Vector2(0.8f, 0f),
          new Vector2(0.8f, 0.8f),
          new Vector2(0.4f, 0.8f),
        }));
      Assert.That(batches[3].Mesh.Vertices.Select(vertex => vertex.TexCoord),
        Is.EqualTo(new[] {
          new Vector2(1f, 0f),
          new Vector2(2f, 0f),
          new Vector2(2f, 2f),
          new Vector2(1f, 2f),
        }));
    } finally {
      Dispose(batches);
    }
  }

  [Test]
  public void BuildBatches_DetachedSharedEdge_OmitsZeroContributionBatches() {
    var terrain = NewTerrain(2, 1, (x, _) => Convert.ToByte(x + 1));
    terrain.SetCornerHeight(1, 0, TerrainCornerSlot.SouthWest, 1);

    var batches = Build(
      terrain,
      Vector4.One,
      _ => TerrainTypeKind.GroundBlended,
      _ => Vector2.One);

    try {
      Assert.That(batches.Select(batch => (batch.Role, batch.SurfaceIndex)),
        Is.EqualTo(new[] {
          (TerrainBlendLayerPassRole.Base, Convert.ToByte(1)),
          (TerrainBlendLayerPassRole.Base, Convert.ToByte(2)),
        }));
      AssertWeights(batches[0].Mesh, 255, 255, 255, 255);
      AssertWeights(batches[1].Mesh, 255, 255, 255, 255);
    } finally {
      Dispose(batches);
    }
  }

  [Test]
  public void BuildBatches_InvalidColorNameOrTextureScale_FailsBeforeMeshCreation() {
    var terrain = NewTerrain(1, 1, (_, _) => 8);
    var factoryCalls = 0;
    var operations = new TerrainBlendLayerMeshBuilderOperations(
      (vertices, indices, _) => {
        factoryCalls++;
        return new Mesh(vertices, indices);
      },
      mesh => mesh.Dispose());

    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      TerrainBlendLayerMeshBuilder.BuildBatches(
        terrain,
        new Vector4(float.NaN),
        _ => TerrainTypeKind.GroundUnblended,
        _ => Vector2.One,
        operations)));
    Assert.Throws<ArgumentException>(new Action(() =>
      TerrainBlendLayerMeshBuilder.BuildBatches(
        terrain,
        Vector4.One,
        _ => TerrainTypeKind.GroundUnblended,
        _ => Vector2.One,
        operations,
        " ")));
    Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendLayerMeshBuilder.BuildBatches(
        terrain,
        Vector4.One,
        _ => TerrainTypeKind.GroundUnblended,
        _ => new Vector2(0f, float.PositiveInfinity),
        operations)));
    Assert.That(factoryCalls, Is.Zero);
  }

  [Test]
  public void BuildBatches_LaterFactoryFailure_DisposesOwnedMeshesInReverseOrder() {
    var terrain = NewTerrain(
      2, 1, (x, _) => x == 0 ? Convert.ToByte(30) : Convert.ToByte(8));
    var buildError = new InvalidOperationException("factory failed");
    var created = new List<Mesh>();
    var disposed = new List<Mesh>();
    var operations = new TerrainBlendLayerMeshBuilderOperations(
      (vertices, indices, _) => {
        if (created.Count == 3) throw buildError;
        var mesh = new Mesh(vertices, indices);
        created.Add(mesh);
        return mesh;
      },
      mesh => disposed.Add(mesh));

    var exception = Assert.Throws<InvalidOperationException>(new Action(() =>
      TerrainBlendLayerMeshBuilder.BuildBatches(
        terrain,
        Vector4.One,
        _ => TerrainTypeKind.GroundBlended,
        _ => Vector2.One,
        operations)));

    Assert.That(exception, Is.SameAs(buildError));
    Assert.That(disposed, Is.EqualTo(created.AsEnumerable().Reverse()));
  }

  [Test]
  public void BuildBatches_BuildAndCleanupFailures_AreBothReported() {
    var terrain = NewTerrain(
      2, 1, (x, _) => x == 0 ? Convert.ToByte(30) : Convert.ToByte(8));
    var buildError = new InvalidOperationException("factory failed");
    var cleanupError = new IOException("cleanup failed");
    var factoryCalls = 0;
    var operations = new TerrainBlendLayerMeshBuilderOperations(
      (vertices, indices, _) => {
        factoryCalls++;
        if (factoryCalls == 2) throw buildError;
        return new Mesh(vertices, indices);
      },
      _ => throw cleanupError);

    var exception = Assert.Throws<AggregateException>(new Action(() =>
      TerrainBlendLayerMeshBuilder.BuildBatches(
        terrain,
        Vector4.One,
        _ => TerrainTypeKind.GroundBlended,
        _ => Vector2.One,
        operations)));

    Assert.That(exception!.InnerExceptions, Is.EqualTo(new Exception[] {
      buildError,
      cleanupError,
    }));
  }

  [Test]
  public void BuildBatches_DisposedFactoryMesh_IsRejectedWithoutTakingOwnership() {
    var terrain = NewTerrain(1, 1, (_, _) => 8);
    var disposedMesh = new Mesh([], []);
    disposedMesh.Dispose();
    var cleanupCalls = 0;
    var operations = new TerrainBlendLayerMeshBuilderOperations(
      (_, _, _) => disposedMesh,
      _ => cleanupCalls++);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendLayerMeshBuilder.BuildBatches(
        terrain,
        Vector4.One,
        _ => TerrainTypeKind.GroundUnblended,
        _ => Vector2.One,
        operations)));

    Assert.That(exception!.Message, Does.Contain("disposed mesh"));
    Assert.That(cleanupCalls, Is.Zero);
  }

  [Test]
  public void BuildBatches_ReusedFactoryMesh_IsRejectedAndDisposedOnce() {
    var terrain = NewTerrain(
      2, 1, (x, _) => x == 0 ? Convert.ToByte(30) : Convert.ToByte(8));
    var reusedMesh = new Mesh([], []);
    var cleanupCalls = 0;
    var operations = new TerrainBlendLayerMeshBuilderOperations(
      (_, _, _) => reusedMesh,
      _ => cleanupCalls++);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TerrainBlendLayerMeshBuilder.BuildBatches(
        terrain,
        Vector4.One,
        _ => TerrainTypeKind.GroundBlended,
        _ => Vector2.One,
        operations)));

    Assert.That(exception!.Message, Does.Contain("reused a mesh"));
    Assert.That(cleanupCalls, Is.EqualTo(1));
  }

  private static IReadOnlyList<TerrainBlendLayerMeshBatch> Build(
    Terrain terrain,
    Vector4 color,
    Func<byte, TerrainTypeKind> getSurfaceKind,
    Func<byte, Vector2> getTextureScale
  ) => TerrainBlendLayerMeshBuilder.BuildBatches(
    terrain,
    color,
    getSurfaceKind,
    getTextureScale,
    TerrainBlendLayerMeshBuilderOperations.Default,
    "Test Blend");

  private static Terrain NewTerrain(
    int width,
    int height,
    Func<int, int, byte> getSurface
  ) {
    var cells = new List<DatTerrainCell>(width * height);
    for (var y = 0; y < height; y++) {
      for (var x = 0; x < width; x++)
        cells.Add(new DatTerrainCell(0, 0, 0, 0, getSurface(x, y), 0));
    }
    return Terrain.FromData(new DatTerrainData(
      width, height, 0, 0, 4, 4, cells.ToArray()));
  }

  private static void AssertWeights(Mesh mesh, params byte[] weights) {
    Assert.That(mesh.Vertices, Has.Count.EqualTo(weights.Length));
    for (var index = 0; index < weights.Length; index++) {
      var expected = Convert.ToSingle(weights[index]) / byte.MaxValue;
      Assert.That(mesh.Vertices[index].Color.W, Is.EqualTo(expected));
    }
  }

  private static void Dispose(IEnumerable<TerrainBlendLayerMeshBatch> batches) {
    foreach (var batch in batches) batch.Mesh.Dispose();
  }
}
