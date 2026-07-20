// Model Animation Frame-Zero Mesh Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class ModelAnimationFrameZeroMeshBuilderTests {
  [Test]
  public void BuildBatches_SkinsDirectModelIndicesAndPreservesRenderAttributes() {
    var skin0 = Matrix4x4.CreateScale(2, 1, 1) *
      Matrix4x4.CreateTranslation(10, 0, 0);
    var skin1 = Matrix4x4.CreateScale(1, 3, 1) *
      Matrix4x4.CreateTranslation(0, 20, 0);
    var sourcePosition = new Vector3(1, 2, 3);
    var sourceNormal = Vector3.Normalize(new Vector3(1, 1, 0));
    var texCoord = new Vector2(0.25f, 0.75f);
    var color = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);
    var sourceVertices = TriangleVertices();
    sourceVertices[0] = new(
      sourcePosition,
      sourceNormal,
      texCoord,
      color,
      new BoneShapeSkinning(0, 1, 255, 255, 64, 191, 0, 0));
    var sourceIndices = new uint[] { 0, 2, 1 };
    var pose = Pose(
      [Group(SourceMesh(sourceVertices, sourceIndices))],
      [skin0, skin1],
      [500, 600]);

    var batches = ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose);

    try {
      var vertex = batches.Single().Mesh.Vertices[0];
      var nativePosition =
        (Vector3.Transform(sourcePosition, skin0) * 64f +
         Vector3.Transform(sourcePosition, skin1) * 191f) / 255f;
      var nativeNormal = Vector3.Normalize(
        (Vector3.TransformNormal(sourceNormal, skin0) * 64f +
         Vector3.TransformNormal(sourceNormal, skin1) * 191f) / 255f);
      using (Assert.EnterMultipleScope()) {
        Assert.That(batches, Has.Count.EqualTo(1));
        Assert.That(batches[0].SourceGroupIndex, Is.Zero);
        Assert.That(batches[0].SourceMeshIndex, Is.Zero);
        Assert.That(batches[0].SourceMeshName, Is.EqualTo("animal/group-0/mesh-0"));
        Assert.That(batches[0].Mesh.Name, Is.EqualTo(batches[0].SourceMeshName));
        AssertVector(vertex.Position,
          new Vector3(nativePosition.X, nativePosition.Z, nativePosition.Y));
        AssertVector(vertex.Normal,
          new Vector3(nativeNormal.X, nativeNormal.Z, nativeNormal.Y));
        Assert.That(vertex.TexCoord, Is.EqualTo(texCoord));
        Assert.That(vertex.Color, Is.EqualTo(color));
        Assert.That(batches[0].Mesh.Indices, Is.EqualTo(new uint[] { 2, 0, 1 }));
        Assert.That(sourceVertices[0].Position, Is.EqualTo(sourcePosition));
        Assert.That(sourceVertices[0].Normal, Is.EqualTo(sourceNormal));
        Assert.That(sourceIndices, Is.EqualTo(new uint[] { 0, 2, 1 }));
      }
    } finally {
      foreach (var batch in batches) batch.Mesh.Dispose();
    }
  }

  [Test]
  public void BuildBatches_ZeroWeightSentinelsDoNotIndexBone255() {
    var pose = Pose([Group(SourceMesh())], [Matrix4x4.CreateTranslation(1, 2, 3)]);

    var batches = ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose);

    try {
      Assert.That(batches.Single().Mesh.Vertices[0].Position,
        Is.EqualTo(new Vector3(1, 3, 2)));
    } finally {
      foreach (var batch in batches) batch.Mesh.Dispose();
    }
  }

  [TestCase(254)]
  [TestCase(256)]
  public void BuildBatches_RequiresExact255WeightTotal(int total) {
    var skinning = total == 254
      ? new BoneShapeSkinning(0, 255, 255, 255, 254, 0, 0, 0)
      : new BoneShapeSkinning(0, 1, 255, 255, 255, 1, 0, 0);
    var pose = Pose(
      [Group(SourceMesh(WithFirstSkinning(skinning)))],
      [Matrix4x4.Identity, Matrix4x4.Identity]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose)));

    Assert.That(exception!.Message, Does.Contain($"weights total {total}"));
  }

  [Test]
  public void BuildBatches_RejectsWeightedMinusOneSentinel() {
    var pose = Pose([Group(SourceMesh(WithFirstSkinning(
      new BoneShapeSkinning(255, 0, 0, 0, 255, 0, 0, 0))))]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose)));

    Assert.That(exception!.Message, Does.Contain("weighted -1 sentinel"));
  }

  [Test]
  public void BuildBatches_RejectsWeightedOutOfRangeModelIndex() {
    var pose = Pose([Group(SourceMesh(WithFirstSkinning(
      new BoneShapeSkinning(1, 0, 0, 0, 255, 0, 0, 0))))]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("bone slot 0 references 1"));
      Assert.That(exception.Message, Does.Contain("MDL has 1 bones"));
    }
  }

  [Test]
  public void BuildBatches_RejectsNonFinitePoseMatrix() {
    var skin = Matrix4x4.Identity;
    skin.M11 = float.NaN;
    var pose = Pose([Group(SourceMesh())], [skin]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose)));

    Assert.That(exception!.Message, Does.Contain("pose bone 0 contains a non-finite matrix"));
  }

  [Test]
  public void BuildBatches_RejectsNonFiniteSkinnedPosition() {
    var vertices = TriangleVertices();
    vertices[0] = vertices[0] with { Position = new Vector3(float.MaxValue, 0, 0) };
    var pose = Pose(
      [Group(SourceMesh(vertices))],
      [Matrix4x4.CreateScale(2)]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose)));

    Assert.That(exception!.Message, Does.Contain("produces a non-finite value"));
  }

  [Test]
  public void BuildBatches_RejectsZeroSkinnedNormal() {
    var pose = Pose(
      [Group(SourceMesh())],
      [Matrix4x4.CreateScale(1, 0, 1)]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose)));

    Assert.That(exception!.Message, Does.Contain("skins to a zero or invalid normal"));
  }

  [Test]
  public void BuildBatches_RejectsPoseBoneOrderMismatch() {
    var pose = Pose([Group(SourceMesh())]);
    pose = pose with {
      Bones = [pose.Bones[0] with { ModelBoneIndex = 1 }],
    };

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose)));

    Assert.That(exception!.Message, Does.Contain("pose bone 0 does not match MDL bone 0"));
  }

  [Test]
  public void BuildBatches_RejectsValueEqualButReplacedPoseBone() {
    var pose = Pose([Group(SourceMesh())]);
    pose = pose with {
      Bones = [pose.Bones[0] with { Bone = pose.Bones[0].Bone with { } }],
    };

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose)));

    Assert.That(exception!.Message, Does.Contain("pose bone 0 does not match MDL bone 0"));
  }

  [Test]
  public void BuildBatches_FactoryFailureDisposesEarlierMeshesInReverseOrder() {
    var pose = Pose([Group(SourceMesh(), SourceMesh(), SourceMesh())]);
    var factoryError = new IOException("factory failed");
    var created = new List<Mesh>();
    var disposed = new List<Mesh>();
    var operations = new ModelAnimationFrameZeroMeshBuilderOperations(
      (vertices, indices, name) => {
        if (created.Count == 2) throw factoryError;
        var mesh = new Mesh(vertices, indices) { Name = name };
        created.Add(mesh);
        return mesh;
      },
      mesh => {
        disposed.Add(mesh);
        mesh.Dispose();
      });

    var exception = Assert.Throws<IOException>(new Action(() =>
      ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose, operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception, Is.SameAs(factoryError));
      Assert.That(created, Has.Count.EqualTo(2));
      Assert.That(disposed, Is.EqualTo(created.AsEnumerable().Reverse()));
      Assert.That(created.All(mesh => mesh.State == State.Disposed), Is.True);
    }
  }

  [Test]
  public void BuildBatches_CleanupFailureRetainsFactoryAndDisposalErrors() {
    var pose = Pose([Group(SourceMesh(), SourceMesh())]);
    var factoryError = new IOException("factory failed");
    var cleanupError = new IOException("cleanup failed");
    var callCount = 0;
    var operations = new ModelAnimationFrameZeroMeshBuilderOperations(
      (vertices, indices, name) => {
        callCount++;
        if (callCount == 2) throw factoryError;
        return new Mesh(vertices, indices) { Name = name };
      },
      _ => throw cleanupError);

    var exception = Assert.Throws<AggregateException>(new Action(() =>
      ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose, operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(2));
      Assert.That(exception.InnerExceptions[0], Is.SameAs(factoryError));
      Assert.That(exception.InnerExceptions[1], Is.SameAs(cleanupError));
    }
  }

  [Test]
  public void BuildBatches_ReusedFactoryMeshFailsClosedAndDisposesOnce() {
    var pose = Pose([Group(SourceMesh(), SourceMesh())]);
    var reused = new Mesh([], []);
    var disposeCount = 0;
    var operations = new ModelAnimationFrameZeroMeshBuilderOperations(
      (_, _, _) => reused,
      mesh => {
        disposeCount++;
        mesh.Dispose();
      });

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      ModelAnimationFrameZeroMeshBuilder.BuildBatches(pose, operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("reused a mesh instance"));
      Assert.That(disposeCount, Is.EqualTo(1));
      Assert.That(reused.State, Is.EqualTo(State.Disposed));
    }
  }

  private static ModelAnimationFrameZeroPose Pose(
    IReadOnlyList<ModelGroup> groups,
    IReadOnlyList<Matrix4x4>? skinTransforms = null,
    IReadOnlyList<ushort>? boneNumbers = null
  ) {
    skinTransforms ??= [Matrix4x4.Identity];
    boneNumbers ??= Enumerable.Range(0, skinTransforms.Count)
      .Select(index => Convert.ToUInt16(index + 1))
      .ToArray();
    if (boneNumbers.Count != skinTransforms.Count)
      throw new ArgumentException("Test bone-number count must match skin transforms.");
    var bones = skinTransforms.Select((_, index) => new ModelBone(
      $"bone-{index}",
      new Vector4(0, 0, 0, 1),
      new Vector4(0, 0, 0, 1),
      Matrix4x4.Identity,
      ushort.MaxValue,
      boneNumbers[index])).ToArray();
    var model = new ModelDefinition(
      "animal",
      "fixture.common.ovl",
      100,
      200,
      Convert.ToUInt32(bones.Length),
      0,
      0,
      0) {
      Bones = bones,
      Groups = groups,
    };
    var names = bones.Select(bone => bone.Name).ToArray();
    var animation = new ModelAnimationDefinition(
      "idle",
      "fixture.common.ovl",
      300,
      0,
      1,
      0,
      0,
      Convert.ToUInt32(bones.Length),
      Convert.ToUInt32(bones.Length),
      0,
      0,
      [],
      [],
      bones.Select(_ => new ModelAnimationTriple(0, 0, 0)).ToArray(),
      bones.Select(_ => new ModelAnimationFourTuple(0, 0, 0, 1)).ToArray(),
      names,
      names);
    var bonePoses = bones.Select((bone, index) => new ModelAnimationFrameZeroBonePose(
      index,
      bone,
      index,
      index,
      Matrix4x4.Identity,
      Matrix4x4.Identity,
      skinTransforms[index])).ToArray();
    return new(
      model,
      animation,
      bonePoses,
      bones.Length,
      bones.Length);
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

  private static ModelMesh SourceMesh(
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

  private static BoneShapeVertex[] WithFirstSkinning(BoneShapeSkinning skinning) {
    var vertices = TriangleVertices();
    vertices[0] = vertices[0] with { Skinning = skinning };
    return vertices;
  }

  private static BoneShapeVertex Vertex(Vector3 position) => new(
    position,
    Vector3.UnitY,
    Vector2.Zero,
    Vector4.One,
    new BoneShapeSkinning(0, 255, 255, 255, 255, 0, 0, 0));

  private static void AssertVector(Vector3 actual, Vector3 expected) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.00001f));
      Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.00001f));
      Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.00001f));
    }
  }
}
