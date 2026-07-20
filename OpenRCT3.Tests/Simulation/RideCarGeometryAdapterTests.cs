// Ride Car Geometry Adapter Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarGeometryAdapterTests {
  [Test]
  public void RideCarLongitudinalGeometry_PreservesOriginalPositionalContract() {
    var geometry = new RideCarLongitudinalGeometry(
      CarFrontPosition: new Vector3(2f, 0f, 0f),
      CarRearPosition: Vector3.Zero,
      FrontWheelCenterPosition: new Vector3(1.5f, 0f, 0f),
      RearWheelCenterPosition: new Vector3(0.5f, 0f, 0f),
      LongitudinalAxis: Vector3.UnitX,
      CarLength: 2f,
      FrontWheelCenterLongitudinalPosition: 1.5f,
      RearWheelCenterLongitudinalPosition: 0.5f,
      Wheelbase: 1f,
      FrontWheelSpan: 2f,
      RearWheelSpan: 1.5f);

    var (_, _, _, _, _, _, _, _, wheelbase, frontSpan, rearSpan) = geometry;

    using (Assert.EnterMultipleScope()) {
      Assert.That(wheelbase, Is.EqualTo(1f));
      Assert.That(frontSpan, Is.EqualTo(2f));
      Assert.That(rearSpan, Is.EqualTo(1.5f));
      Assert.That(geometry.FrontWheelCenterOffsetFromCarFront, Is.EqualTo(-0.5f));
      Assert.That(geometry.FrontWheelCenterOffsetFromCarRear, Is.EqualTo(1.5f));
      Assert.That(geometry.RearWheelCenterOffsetFromFrontWheelCenter, Is.EqualTo(-1f));
    }
  }

  [Test]
  public void RideCarLongitudinalGeometry_WithCopyRecomputesSignedOffsets() {
    var geometry = new RideCarLongitudinalGeometry(
      new Vector3(2f, 0f, 0f),
      Vector3.Zero,
      new Vector3(1.5f, 0f, 0f),
      new Vector3(0.5f, 0f, 0f),
      Vector3.UnitX,
      2f,
      1.5f,
      0.5f,
      1f,
      2f,
      1.5f);

    var updated = geometry with {
      CarFrontPosition = new Vector3(4f, 0f, 0f),
      FrontWheelCenterPosition = new Vector3(3f, 0f, 0f),
      RearWheelCenterPosition = new Vector3(1f, 0f, 0f),
      CarLength = 4f,
      FrontWheelCenterLongitudinalPosition = 3f,
      RearWheelCenterLongitudinalPosition = 1f,
      Wheelbase = 2f,
    };

    using (Assert.EnterMultipleScope()) {
      Assert.That(updated.FrontWheelCenterOffsetFromCarFront, Is.EqualTo(-1f));
      Assert.That(updated.FrontWheelCenterOffsetFromCarRear, Is.EqualTo(3f));
      Assert.That(updated.RearWheelCenterOffsetFromFrontWheelCenter, Is.EqualTo(-2f));
    }
  }

  [Test]
  public void CreateLongitudinalGeometry_UsesNativeLookupAndPairedWheelMidpoints() {
    var shape = Shape(
      Bone("carfront", new Vector3(6f, 8f, 0f)),
      Bone("CARREAR", new Vector3(3f, 4f, 0f)),
      Bone("wheelfr", new Vector3(5.4f, 7.2f, 1f)),
      Bone("WHEELFL", new Vector3(5.4f, 7.2f, -1f)),
      Bone("wheelrr", new Vector3(3.6f, 4.8f, 0.75f)),
      Bone("WHEELRL", new Vector3(3.6f, 4.8f, -0.75f)));

    var geometry = RideCarGeometryAdapter.CreateLongitudinalGeometry(shape);

    using (Assert.EnterMultipleScope()) {
      AssertVector(geometry.CarFrontPosition, new Vector3(6f, 0f, 8f));
      AssertVector(geometry.CarRearPosition, new Vector3(3f, 0f, 4f));
      AssertVector(geometry.FrontWheelCenterPosition, new Vector3(5.4f, 0f, 7.2f));
      AssertVector(geometry.RearWheelCenterPosition, new Vector3(3.6f, 0f, 4.8f));
      AssertVector(geometry.LongitudinalAxis, new Vector3(0.6f, 0f, 0.8f));
      Assert.That(geometry.CarLength, Is.EqualTo(5f).Within(0.0001f));
      Assert.That(
        geometry.FrontWheelCenterLongitudinalPosition,
        Is.EqualTo(9f).Within(0.0001f));
      Assert.That(
        geometry.RearWheelCenterLongitudinalPosition,
        Is.EqualTo(6f).Within(0.0001f));
      Assert.That(
        geometry.FrontWheelCenterOffsetFromCarFront,
        Is.EqualTo(-1f).Within(0.0001f));
      Assert.That(
        geometry.FrontWheelCenterOffsetFromCarRear,
        Is.EqualTo(4f).Within(0.0001f));
      Assert.That(
        geometry.RearWheelCenterOffsetFromFrontWheelCenter,
        Is.EqualTo(-3f).Within(0.0001f));
      Assert.That(geometry.Wheelbase, Is.EqualTo(3f).Within(0.0001f));
      Assert.That(geometry.FrontWheelSpan, Is.EqualTo(2f).Within(0.0001f));
      Assert.That(geometry.RearWheelSpan, Is.EqualTo(1.5f).Within(0.0001f));
    }
  }

  [Test]
  public void CreateLongitudinalGeometry_BodyWheelPairsTakePrecedenceOverAxleAnchors() {
    var shape = Shape(
      [
        .. ValidBones(),
        Bone("AxleF", new Vector3(100f, 0f, 0f)),
        Bone("AxelF", new Vector3(200f, 0f, 0f)),
        Bone("AxleR", new Vector3(-100f, 0f, 0f)),
        Bone("AxelR", new Vector3(-200f, 0f, 0f)),
      ]);

    var geometry = RideCarGeometryAdapter.CreateLongitudinalGeometry(shape);

    using (Assert.EnterMultipleScope()) {
      AssertVector(geometry.FrontWheelCenterPosition, new Vector3(1.5f, 0f, 0f));
      AssertVector(geometry.RearWheelCenterPosition, new Vector3(0.5f, 0f, 0f));
      Assert.That(geometry.FrontWheelCenterOffsetFromCarFront, Is.EqualTo(-0.5f));
      Assert.That(geometry.FrontWheelCenterOffsetFromCarRear, Is.EqualTo(1.5f));
      Assert.That(geometry.RearWheelCenterOffsetFromFrontWheelCenter, Is.EqualTo(-1f));
      Assert.That(geometry.Wheelbase, Is.EqualTo(1f));
    }
  }

  [Test]
  public void CreateLongitudinalGeometry_AxleAnchorsDoNotSubstituteForBodyWheels() {
    var shape = Shape(
      Bone("CarFront", new Vector3(2f, 0f, 0f)),
      Bone("CarRear", Vector3.Zero),
      Bone("AxleF", new Vector3(1.5f, 0f, 0f)),
      Bone("AxleR", new Vector3(0.5f, 0f, 0f)));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarGeometryAdapter.CreateLongitudinalGeometry(shape)));

    Assert.That(exception!.Message, Does.Contain("is missing required 'WheelFR' bone"));
  }

  [Test]
  public void CreateLongitudinalGeometry_UsesFirstCaseInsensitiveNativeLookupMatch() {
    var shape = Shape([
      .. ValidBones(),
      Bone("CARFRONT", new Vector3(100f, 0f, 0f)),
      Bone("WHEELFR", new Vector3(100f, 100f, 100f)),
    ]);

    var geometry = RideCarGeometryAdapter.CreateLongitudinalGeometry(shape);

    using (Assert.EnterMultipleScope()) {
      Assert.That(geometry.CarLength, Is.EqualTo(2f));
      AssertVector(geometry.FrontWheelCenterPosition, new Vector3(1.5f, 0f, 0f));
    }
  }

  [TestCase("CarFront")]
  [TestCase("CarRear")]
  [TestCase("WheelFR")]
  [TestCase("WheelFL")]
  [TestCase("WheelRR")]
  [TestCase("WheelRL")]
  public void CreateLongitudinalGeometry_MissingRequiredBoneFailsClosed(string omittedName) {
    var bones = ValidBones()
      .Where(bone => !string.Equals(bone.Name, omittedName, StringComparison.Ordinal))
      .ToArray();

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarGeometryAdapter.CreateLongitudinalGeometry(Shape(bones))));

    Assert.That(exception!.Message, Does.Contain("is missing required"));
  }

  [Test]
  public void CreateLongitudinalGeometry_NonFinitePosition2FailsClosed() {
    var invalidPosition = Matrix4x4.Identity;
    invalidPosition.M41 = float.NaN;
    var bones = ValidBones();
    bones[2] = bones[2] with { Position2 = invalidPosition };

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarGeometryAdapter.CreateLongitudinalGeometry(Shape(bones))));

    Assert.That(exception!.Message, Does.Contain("position2 matrix contains a non-finite value"));
  }

  [Test]
  public void CreateLongitudinalGeometry_DegenerateLongitudinalAxisFailsClosed() {
    var bones = ValidBones();
    bones[0] = Bone("CarFront", Vector3.Zero);
    bones[1] = Bone("CarRear", Vector3.Zero);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarGeometryAdapter.CreateLongitudinalGeometry(Shape(bones))));

    Assert.That(exception!.Message, Does.Contain("degenerate longitudinal axis"));
  }

  [Test]
  public void CreateLongitudinalGeometry_DegenerateWheelSpanFailsClosed() {
    var bones = ValidBones();
    bones[3] = Bone("WheelFL", new Vector3(1.5f, 0f, 1f));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarGeometryAdapter.CreateLongitudinalGeometry(Shape(bones))));

    Assert.That(exception!.Message, Does.Contain("front wheel markers define a degenerate span"));
  }

  [Test]
  public void CreateLongitudinalGeometry_DegenerateProjectedWheelbaseFailsClosed() {
    var bones = ValidBones();
    bones[2] = Bone("WheelFR", new Vector3(1f, 1f, 1f));
    bones[3] = Bone("WheelFL", new Vector3(1f, 1f, -1f));
    bones[4] = Bone("WheelRR", new Vector3(1f, -1f, 1f));
    bones[5] = Bone("WheelRL", new Vector3(1f, -1f, -1f));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarGeometryAdapter.CreateLongitudinalGeometry(Shape(bones))));

    Assert.That(exception!.Message, Does.Contain("degenerate wheelbase"));
  }

  [Test]
  public void CreateLongitudinalGeometry_RejectsOversizedBoneListBeforeLookup() {
    var shape = new BoneShape(
      "car-body",
      Vector3.Zero,
      Vector3.One,
      [],
      new CountOnlyReadOnlyList<BoneShapeBone>(65_537));

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarGeometryAdapter.CreateLongitudinalGeometry(shape)));

    Assert.That(exception!.Message, Does.Contain(
      "bone count 65537 is outside the supported range"));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets and the BoxOffice DAT.")]
  public void BoxOffice_StreamlinedMonoBodiesExposeExactModelSpaceGeometry() {
    var installRoot = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(installRoot),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(installRoot),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");
    var mapPath = Path.Combine(installRoot!, "Campaigns", "Base", "BoxOffice.dat");
    Assert.That(File.Exists(mapPath), Is.True, mapPath);

    var data = DatTerrainReader.Read(mapPath);
    var terrain = Terrain.FromData(data);
    try {
      var park = new Park(terrain);
      SceneryManagerLoader.Load(
        park,
        terrain,
        data.SceneryItems,
        data.SceneryItemPlacements);
      RideTrackManagerLoader.Load(park, terrain, data.TrackPieces);
      using var loaded = RideTrackResourceCatalogLoader.Load(
        installRoot!,
        park.RideTrackPlacements,
        data.TrackedRideInstances,
        data.RideTrainInstances);
      var expectedRoles = new[] {
        RideTrainCarRole.Front,
        RideTrainCarRole.Middle,
        RideTrainCarRole.Rear,
        RideTrainCarRole.Link,
      };
      var expectedGeometry = new[] {
        new ExpectedInstalledBodyGeometry(
          RideTrainCarRole.Front,
          "StreamlinedMonoFront",
          "StreamLinedMonorailFrontHI",
          6.789252f,
          1.418196f,
          1.418197f,
          2.9714994f),
        new ExpectedInstalledBodyGeometry(
          RideTrainCarRole.Middle,
          "StreamlinedMonoMiddle",
          "StreamLinedMonorailMiddleHI",
          6.863065f,
          1.418196f,
          1.418196f,
          1.8054994f),
        new ExpectedInstalledBodyGeometry(
          RideTrainCarRole.Rear,
          "StreamlinedMonoRear",
          "StreamLinedMonorailRearHI",
          6.3483458f,
          1.418196f,
          1.418196f,
          1.8054935f),
        new ExpectedInstalledBodyGeometry(
          RideTrainCarRole.Link,
          "MonoLink",
          "MonoLink",
          1.44484f,
          1.418196f,
          1.418196f,
          1.455626f),
      };
      var bodyGeometry = loaded.RideResources.CarVisuals.Visuals
        .Where(link => link.Visual.Role == RideVisualRole.Body)
        .Where(link => string.Equals(
          link.Train.Train?.Name,
          "StreamlinedMono",
          StringComparison.OrdinalIgnoreCase))
        .Where(link => expectedRoles.Contains(link.Car.Role))
        .SelectMany(link => link.Lods
          .Where(lod => lod.BoneShape != null)
          .Select(lod => new {
            link.Car.Role,
            Car = link.Car.Car!.Name,
            Lod = lod.Lod.Name,
            Distance = lod.Lod.Distance,
            Shape = lod.BoneShape!,
          }))
        .Where(item => HasRequiredBodyMarkers(item.Shape))
        .GroupBy(item => item.Role)
        .Select(group => group.OrderBy(item => item.Distance).First())
        .OrderBy(item => item.Role)
        .Select(item => new {
          item.Role,
          item.Car,
          item.Lod,
          item.Shape,
          Geometry = RideCarGeometryAdapter.CreateLongitudinalGeometry(item.Shape),
        })
        .ToArray();

      foreach (var item in bodyGeometry)
        TestContext.Progress.WriteLine(
          $"StreamlinedMono {item.Role}: car={item.Car}, lod={item.Lod}, " +
          $"shape={item.Shape.Name}, length={item.Geometry.CarLength:R}, " +
          $"frontSpan={item.Geometry.FrontWheelSpan:R}, " +
          $"rearSpan={item.Geometry.RearWheelSpan:R}, " +
          $"wheelbase={item.Geometry.Wheelbase:R}");

      Assert.That(bodyGeometry.Select(item => item.Role), Is.EqualTo(expectedRoles));
      for (var index = 0; index < expectedGeometry.Length; index++) {
        var actual = bodyGeometry[index];
        var expected = expectedGeometry[index];
        using (Assert.EnterMultipleScope()) {
          Assert.That(actual.Role, Is.EqualTo(expected.Role));
          Assert.That(actual.Car, Is.EqualTo(expected.Car));
          Assert.That(actual.Lod, Is.EqualTo(expected.Shape));
          Assert.That(actual.Shape.Name, Is.EqualTo(expected.Shape));
          Assert.That(actual.Geometry.CarLength, Is.EqualTo(expected.Length).Within(0.00001f));
          Assert.That(
            actual.Geometry.FrontWheelSpan,
            Is.EqualTo(expected.FrontSpan).Within(0.00001f));
          Assert.That(
            actual.Geometry.RearWheelSpan,
            Is.EqualTo(expected.RearSpan).Within(0.00001f));
          Assert.That(
            actual.Geometry.Wheelbase,
            Is.EqualTo(expected.Wheelbase).Within(0.00001f));
        }
      }
    } finally {
      terrain.TextureCatalog?.Dispose();
    }
  }

  private static bool HasRequiredBodyMarkers(BoneShape shape) {
    var names = shape.Bones.Select(bone => bone.Name).ToHashSet(
      StringComparer.OrdinalIgnoreCase);
    return names.Contains("CarFront") &&
      names.Contains("CarRear") &&
      names.Contains("WheelFR") &&
      names.Contains("WheelFL") &&
      names.Contains("WheelRR") &&
      names.Contains("WheelRL");
  }

  private static BoneShape Shape(params BoneShapeBone[] bones) => new(
    "car-body",
    Vector3.Zero,
    Vector3.One,
    [],
    bones);

  private static BoneShapeBone[] ValidBones() => [
    Bone("CarFront", new Vector3(2f, 0f, 0f)),
    Bone("CarRear", Vector3.Zero),
    Bone("WheelFR", new Vector3(1.5f, 0f, 1f)),
    Bone("WheelFL", new Vector3(1.5f, 0f, -1f)),
    Bone("WheelRR", new Vector3(0.5f, 0f, 0.75f)),
    Bone("WheelRL", new Vector3(0.5f, 0f, -0.75f)),
  ];

  private static BoneShapeBone Bone(string name, Vector3 position) => new(
    name,
    -1,
    Matrix4x4.CreateTranslation(100f, 200f, 300f),
    Matrix4x4.CreateTranslation(position));

  private static void AssertVector(Vector3 actual, Vector3 expected) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.0001f));
      Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.0001f));
      Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.0001f));
    }
  }

  private sealed class CountOnlyReadOnlyList<T>(int count) : IReadOnlyList<T> {
    public int Count => count;
    public T this[int index] => throw new InvalidOperationException(
      $"Indexer must not be read for oversized fixture at {index}.");
    public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException(
      "Enumerator must not be read for oversized fixture.");
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
  }

  private sealed record ExpectedInstalledBodyGeometry(
    RideTrainCarRole Role,
    string Car,
    string Shape,
    float Length,
    float FrontSpan,
    float RearSpan,
    float Wheelbase
  );
}
