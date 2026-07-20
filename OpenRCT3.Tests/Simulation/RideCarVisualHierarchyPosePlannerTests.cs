// Ride Car Visual Hierarchy Pose Planner Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarVisualHierarchyPosePlannerTests {
  [Test]
  public void Resolve_ComposesAxisConvertedAbsoluteAnchorsInRowVectorOrder() {
    var fixture = Fixture();
    var bodyWorld = Matrix4x4.CreateRotationY(0.37f) *
      Matrix4x4.CreateTranslation(100f, 200f, 300f);

    var result = RideCarVisualHierarchyPosePlanner.Resolve(
      bodyWorld,
      fixture.Hierarchy);
    var expectedFrontAxle = fixture.Local[RideVisualRole.FrontAxle] * bodyWorld;
    var expectedRearAxle = fixture.Local[RideVisualRole.RearAxle] * bodyWorld;
    var expected = new Dictionary<RideVisualRole, Matrix4x4> {
      [RideVisualRole.FrontAxle] = expectedFrontAxle,
      [RideVisualRole.RearAxle] = expectedRearAxle,
      [RideVisualRole.FrontRightWheel] =
        fixture.Local[RideVisualRole.FrontRightWheel] * expectedFrontAxle,
      [RideVisualRole.FrontLeftWheel] =
        fixture.Local[RideVisualRole.FrontLeftWheel] * bodyWorld,
      [RideVisualRole.BackRightWheel] =
        fixture.Local[RideVisualRole.BackRightWheel] * expectedRearAxle,
      [RideVisualRole.BackLeftWheel] =
        fixture.Local[RideVisualRole.BackLeftWheel] * expectedRearAxle,
    };

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Hierarchy, Is.SameAs(fixture.Hierarchy));
      Assert.That(result.BodyWorldTransform, Is.EqualTo(bodyWorld));
      Assert.That(result.Parts.Select(pose => pose.Role), Is.EqualTo(new[] {
        RideVisualRole.FrontAxle,
        RideVisualRole.RearAxle,
        RideVisualRole.FrontRightWheel,
        RideVisualRole.FrontLeftWheel,
        RideVisualRole.BackRightWheel,
        RideVisualRole.BackLeftWheel,
      }));
      Assert.That(result.Parts.Select(pose => pose.Type),
        Is.EqualTo(new uint[] { uint.MaxValue, 2, 11, 12, 13, 14 }));
      Assert.That(
        fixture.Hierarchy.FrontAxle.Anchor!.Bone.Position2.Translation,
        Is.Not.EqualTo(fixture.Local[RideVisualRole.FrontAxle].Translation));
    }
    foreach (var pose in result.Parts)
      AssertMatrix(pose.WorldTransform, expected[pose.Role]);
  }

  [Test]
  public void Resolve_RejectsForeignAnchorIdentityAndNonFiniteBodyTransform() {
    var fixture = Fixture();
    var wheel = fixture.Hierarchy.FrontRightWheel;
    var foreignAnchor = wheel.Anchor! with {
      Visual = fixture.ShapeVisuals[RideVisualRole.RearAxle],
    };
    var foreign = fixture.Hierarchy with {
      FrontRightWheel = wheel with { Anchor = foreignAnchor },
    };
    var nonFinite = Matrix4x4.Identity with { M42 = float.NaN };

    var foreignError = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualHierarchyPosePlanner.Resolve(Matrix4x4.Identity, foreign)));
    var finiteError = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualHierarchyPosePlanner.Resolve(nonFinite, fixture.Hierarchy)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(foreignError!.Message, Does.Contain("foreign shape owner"));
      Assert.That(finiteError!.Message, Does.Contain("non-finite"));
    }
  }

  private static HierarchyFixture Fixture() {
    var local = new Dictionary<RideVisualRole, Matrix4x4> {
      [RideVisualRole.FrontAxle] = Matrix4x4.CreateRotationZ(0.23f) *
        Matrix4x4.CreateTranslation(10f, 20f, 30f),
      [RideVisualRole.RearAxle] = Matrix4x4.CreateRotationX(-0.31f) *
        Matrix4x4.CreateTranslation(-10f, 40f, 50f),
      [RideVisualRole.FrontRightWheel] = Matrix4x4.CreateRotationY(0.41f) *
        Matrix4x4.CreateTranslation(1f, 2f, 3f),
      [RideVisualRole.FrontLeftWheel] = Matrix4x4.CreateRotationX(0.17f) *
        Matrix4x4.CreateTranslation(4f, 5f, 6f),
      [RideVisualRole.BackRightWheel] = Matrix4x4.CreateRotationZ(-0.29f) *
        Matrix4x4.CreateTranslation(7f, 8f, 9f),
      [RideVisualRole.BackLeftWheel] = Matrix4x4.CreateRotationY(-0.13f) *
        Matrix4x4.CreateTranslation(11f, 12f, 13f),
    };
    var definitions = new Dictionary<RideVisualRole, VisualDefinition> {
      [RideVisualRole.Body] = Visual(RideVisualRole.Body, [
        Bone("AxleF", local[RideVisualRole.FrontAxle]),
        Bone("AxleR", local[RideVisualRole.RearAxle]),
        Bone("WheelFL", local[RideVisualRole.FrontLeftWheel]),
      ]),
      [RideVisualRole.FrontAxle] = Visual(RideVisualRole.FrontAxle, [
        Bone("WheelR", local[RideVisualRole.FrontRightWheel]),
      ]),
      [RideVisualRole.RearAxle] = Visual(RideVisualRole.RearAxle, [
        Bone("WheelR", local[RideVisualRole.BackRightWheel]),
        Bone("WheelL", local[RideVisualRole.BackLeftWheel]),
      ]),
      [RideVisualRole.FrontRightWheel] = Visual(RideVisualRole.FrontRightWheel, []),
      [RideVisualRole.FrontLeftWheel] = Visual(RideVisualRole.FrontLeftWheel, []),
      [RideVisualRole.BackRightWheel] = Visual(RideVisualRole.BackRightWheel, []),
      [RideVisualRole.BackLeftWheel] = Visual(RideVisualRole.BackLeftWheel, []),
    };
    var car = new RideCarLink(
      RideTrainCarRole.Front,
      "car:ric",
      Source: null,
      definitions.Values.Select(value => value.Visual).ToArray());
    var train = new RideTrainLink("train:rit", Source: null, [car]);
    var ride = new TrackedRideResourceLink(Source: null!, [train], WildSplitter: null);
    var shapeVisuals = definitions.ToDictionary(
      pair => pair.Key,
      pair => new RideCarVisualShapeLink(
        ride,
        train,
        car,
        pair.Value.Visual,
        [pair.Value.Lod]));

    RideCarVisualHierarchyPart Part(
      RideVisualRole role,
      uint type,
      RideCarVisualHierarchyAnchorSource source,
      RideVisualRole ownerRole,
      string boneName
    ) {
      var owner = definitions[ownerRole];
      var boneIndex = owner.ShapeSource.Resource.Bones
        .Select((bone, index) => (bone, index))
        .Single(item => item.bone.Name == boneName).index;
      var anchor = new RideCarVisualHierarchyAnchor(
        source,
        shapeVisuals[ownerRole],
        owner.Lod,
        owner.ShapeSource,
        boneIndex,
        owner.ShapeSource.Resource.Bones[boneIndex]);
      return new(
        role,
        definitions[role].Visual.Reference,
        type,
        definitions[role].Visual,
        shapeVisuals[role],
        RideCarVisualHierarchyPartStatus.Resolved,
        anchor);
    }

    var hierarchy = new RideCarVisualHierarchyResolution(
      ride,
      train,
      car,
      RideVisualRole.Body,
      RideCarVisualHierarchyPartStatus.Resolved,
      definitions[RideVisualRole.Body].Visual,
      shapeVisuals[RideVisualRole.Body],
      Part(
        RideVisualRole.FrontAxle,
        uint.MaxValue,
        RideCarVisualHierarchyAnchorSource.Body,
        RideVisualRole.Body,
        "AxleF"),
      Part(
        RideVisualRole.RearAxle,
        2,
        RideCarVisualHierarchyAnchorSource.Body,
        RideVisualRole.Body,
        "AxleR"),
      Part(
        RideVisualRole.FrontRightWheel,
        11,
        RideCarVisualHierarchyAnchorSource.FrontAxle,
        RideVisualRole.FrontAxle,
        "WheelR"),
      Part(
        RideVisualRole.FrontLeftWheel,
        12,
        RideCarVisualHierarchyAnchorSource.Body,
        RideVisualRole.Body,
        "WheelFL"),
      Part(
        RideVisualRole.BackRightWheel,
        13,
        RideCarVisualHierarchyAnchorSource.RearAxle,
        RideVisualRole.RearAxle,
        "WheelR"),
      Part(
        RideVisualRole.BackLeftWheel,
        14,
        RideCarVisualHierarchyAnchorSource.RearAxle,
        RideVisualRole.RearAxle,
        "WheelL"));
    return new(hierarchy, shapeVisuals, local);
  }

  private static VisualDefinition Visual(
    RideVisualRole role,
    IReadOnlyList<BoneShapeBone> bones
  ) {
    var name = role.ToString();
    var shape = new BoneShape(
      $"{name}Shape",
      Vector3.Zero,
      Vector3.One,
      [],
      bones);
    var shapeSource = new RideBoneShapeResourceSource(
      new OvlFile(shape.Name, FileType.BoneShape, "shapes.unique.ovl"),
      shape);
    var lod = new SceneryItemVisualLod(
      "near",
      SvdLodType.BoneShape,
      null,
      $"{shape.Name}:bsh",
      null,
      null,
      new SceneryVisualBillboardSettings(1f, 1f, 0f, 0f, 1f, 1f),
      10f,
      []);
    var visual = new SceneryItemVisual(name, 0, 0f, 0f, 0f, 0f, [lod], null);
    var link = new RideVisualLink(
      role,
      $"{name}:svd",
      new RideVisualResourceSource(
        new OvlFile(name, FileType.SceneryItemVisual, "visuals.unique.ovl"),
        visual));
    return new(
      link,
      new RideVisualShapeLodLink(lod, null, shapeSource),
      shapeSource);
  }

  private static BoneShapeBone Bone(string name, Matrix4x4 parkAbsolute) => new(
    name,
    -1,
    Matrix4x4.Identity,
    NativeFromPark(parkAbsolute));

  private static Matrix4x4 NativeFromPark(Matrix4x4 value) {
    var basis = new Matrix4x4(
      1f, 0f, 0f, 0f,
      0f, 0f, 1f, 0f,
      0f, 1f, 0f, 0f,
      0f, 0f, 0f, 1f);
    return basis * value * basis;
  }

  private static void AssertMatrix(Matrix4x4 actual, Matrix4x4 expected) {
    var actualValues = Values(actual);
    var expectedValues = Values(expected);
    foreach (var index in Enumerable.Range(0, actualValues.Length))
      Assert.That(actualValues[index], Is.EqualTo(expectedValues[index]).Within(0.00001f),
        $"matrix element {index}");
  }

  private static float[] Values(Matrix4x4 value) => [
    value.M11, value.M12, value.M13, value.M14,
    value.M21, value.M22, value.M23, value.M24,
    value.M31, value.M32, value.M33, value.M34,
    value.M41, value.M42, value.M43, value.M44,
  ];

  private sealed record VisualDefinition(
    RideVisualLink Visual,
    RideVisualShapeLodLink Lod,
    RideBoneShapeResourceSource ShapeSource
  );

  private sealed record HierarchyFixture(
    RideCarVisualHierarchyResolution Hierarchy,
    IReadOnlyDictionary<RideVisualRole, RideCarVisualShapeLink> ShapeVisuals,
    IReadOnlyDictionary<RideVisualRole, Matrix4x4> Local
  );
}
