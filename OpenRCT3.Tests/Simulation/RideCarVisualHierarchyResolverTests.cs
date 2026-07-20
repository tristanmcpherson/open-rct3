using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Collections;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarVisualHierarchyResolverTests {
  [Test]
  public void Resolve_UsesCanonicalBodyAxleAnchorsAndRetainsOpaqueTypes() {
    var frontAxle = Bone("AxleF");
    var rearAxle = Bone("AxleR");
    var fixture = Fixture(new FixtureOptions([
      frontAxle,
      rearAxle,
      Bone("WheelFR"),
      Bone("WheelFL"),
      Bone("WheelRR"),
      Bone("WheelRL"),
    ]) {
      FrontAxleType = 4,
      RearAxleType = 2,
      FrontRightWheelType = 11,
      FrontLeftWheelType = 12,
      BackRightWheelType = 13,
      BackLeftWheelType = 14,
    });

    var registry = RideCarVisualHierarchyResolver.Resolve(fixture.Resources);
    var result = registry.Cars.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Ride, Is.SameAs(fixture.Ride));
      Assert.That(result.Train, Is.SameAs(fixture.Train));
      Assert.That(result.Car, Is.SameAs(fixture.Car));
      Assert.That(result.FrontAxle.Type, Is.EqualTo(4u));
      Assert.That(result.RearAxle.Type, Is.EqualTo(2u));
      Assert.That(result.FrontRightWheel.Type, Is.EqualTo(11u));
      Assert.That(result.FrontLeftWheel.Type, Is.EqualTo(12u));
      Assert.That(result.BackRightWheel.Type, Is.EqualTo(13u));
      Assert.That(result.BackLeftWheel.Type, Is.EqualTo(14u));
      Assert.That(result.FrontAxle.Anchor!.Bone, Is.SameAs(frontAxle));
      Assert.That(result.RearAxle.Anchor!.Bone, Is.SameAs(rearAxle));
      Assert.That(
        result.FrontAxle.Anchor.ShapeSource,
        Is.SameAs(fixture.ShapeSources[RideVisualRole.Body]));
      Assert.That(registry.ResolvedPartCount, Is.EqualTo(6));
      Assert.That(registry.UnavailablePartCount, Is.Zero);
    }
  }

  [Test]
  public void Resolve_UsesLegacyAxelNamesOnlyWhenCanonicalNamesAreAbsent() {
    var legacyFront = Bone("AxelF");
    var legacyRear = Bone("AxelR");
    var fixture = Fixture(new FixtureOptions([
      legacyFront,
      legacyRear,
      Bone("WheelFR"),
      Bone("WheelFL"),
      Bone("WheelRR"),
      Bone("WheelRL"),
    ]));

    var result = RideCarVisualHierarchyResolver.Resolve(fixture.Resources).Cars.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.FrontAxle.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.Resolved));
      Assert.That(result.RearAxle.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.Resolved));
      Assert.That(result.FrontAxle.Anchor!.Bone, Is.SameAs(legacyFront));
      Assert.That(result.RearAxle.Anchor!.Bone, Is.SameAs(legacyRear));
      Assert.That(result.FrontAxle.Anchor.BoneIndex, Is.EqualTo(0));
      Assert.That(result.RearAxle.Anchor.BoneIndex, Is.EqualTo(1));
    }
  }

  [Test]
  public void Resolve_KeysNormalAndWildFlippedBodiesByExactRoleIdentity() {
    var normalFrontAxle = Bone("AxleF");
    var normalRearRight = Bone("WheelRR");
    var wildFrontAxle = Bone("AxleF");
    var wildRearRight = Bone("WheelRR");
    var fixture = Fixture(new FixtureOptions([
      normalFrontAxle,
      Bone("WheelFR"),
      Bone("WheelFL"),
      normalRearRight,
      Bone("WheelRL"),
    ]) {
      WildFlippedBodyBones = [
        wildFrontAxle,
        Bone("WheelFR"),
        Bone("WheelFL"),
        wildRearRight,
        Bone("WheelRL"),
      ],
      UndeclaredVisuals = new HashSet<RideVisualRole> {
        RideVisualRole.RearAxle,
      },
    });

    var registry = RideCarVisualHierarchyResolver.Resolve(fixture.Resources);
    Assert.That(registry.TryGet(
      fixture.Car,
      RideVisualRole.Body,
      out var normal), Is.True);
    Assert.That(registry.TryGet(
      fixture.Car,
      RideVisualRole.WildFlippedBody,
      out var wild), Is.True);
    Assert.That(registry.TryGet(fixture.Car, out var defaultBody), Is.True);

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.Cars, Has.Count.EqualTo(2));
      Assert.That(normal.BodyRole, Is.EqualTo(RideVisualRole.Body));
      Assert.That(wild.BodyRole, Is.EqualTo(RideVisualRole.WildFlippedBody));
      Assert.That(normal.BodyVisual,
        Is.SameAs(fixture.VisualLinks[RideVisualRole.Body]));
      Assert.That(wild.BodyVisual,
        Is.SameAs(fixture.VisualLinks[RideVisualRole.WildFlippedBody]));
      Assert.That(normal.FrontAxle.Anchor!.Bone, Is.SameAs(normalFrontAxle));
      Assert.That(wild.FrontAxle.Anchor!.Bone, Is.SameAs(wildFrontAxle));
      Assert.That(normal.BackRightWheel.Anchor!.Bone, Is.SameAs(normalRearRight));
      Assert.That(wild.BackRightWheel.Anchor!.Bone, Is.SameAs(wildRearRight));
      Assert.That(normal.BodyShapeVisual,
        Is.Not.SameAs(wild.BodyShapeVisual));
      Assert.That(defaultBody, Is.SameAs(normal));
    }
  }

  [Test]
  public void Resolve_UnresolvedWildBodyNeverBorrowsNormalBodyAnchors() {
    var fixture = Fixture(new FixtureOptions([
      Bone("AxleF"),
      Bone("AxleR"),
      Bone("WheelFR"),
      Bone("WheelFL"),
      Bone("WheelRR"),
      Bone("WheelRL"),
    ]) {
      WildFlippedBodyBones = [
        Bone("AxleF"),
        Bone("AxleR"),
        Bone("WheelFR"),
        Bone("WheelFL"),
        Bone("WheelRR"),
        Bone("WheelRL"),
      ],
      UnresolvedVisuals = new HashSet<RideVisualRole> {
        RideVisualRole.WildFlippedBody,
      },
    });

    var registry = RideCarVisualHierarchyResolver.Resolve(fixture.Resources);
    Assert.That(registry.TryGet(
      fixture.Car,
      RideVisualRole.WildFlippedBody,
      out var wild), Is.True);

    using (Assert.EnterMultipleScope()) {
      Assert.That(wild.BodyStatus, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.VisualUnresolved));
      Assert.That(wild.FrontAxle.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AnchorShapeUnavailable));
      Assert.That(wild.FrontAxle.Anchor, Is.Null);
      Assert.That(wild.FrontRightWheel.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AxleAnchorUnavailable));
      Assert.That(wild.FrontRightWheel.Anchor, Is.Null);
    }
  }

  [Test]
  public void Resolve_PrefersExactAxleShapeWheelAnchors() {
    var bodyFrontRight = Bone("WheelFR");
    var bodyFrontLeft = Bone("WheelFL");
    var bodyRearRight = Bone("WheelRR");
    var bodyRearLeft = Bone("WheelRL");
    var frontRight = Bone("WheelR");
    var frontLeft = Bone("WheelL");
    var rearRight = Bone("WheelR");
    var rearLeft = Bone("WheelL");
    var fixture = Fixture(new FixtureOptions([
      Bone("AxleF"),
      Bone("AxleR"),
      bodyFrontRight,
      bodyFrontLeft,
      bodyRearRight,
      bodyRearLeft,
    ]) {
      FrontAxleBones = [frontRight, frontLeft],
      RearAxleBones = [rearRight, rearLeft],
    });

    var result = RideCarVisualHierarchyResolver.Resolve(fixture.Resources).Cars.Single();

    AssertWheel(
      result.FrontRightWheel,
      RideCarVisualHierarchyAnchorSource.FrontAxle,
      frontRight,
      fixture.ShapeSources[RideVisualRole.FrontAxle]);
    AssertWheel(
      result.FrontLeftWheel,
      RideCarVisualHierarchyAnchorSource.FrontAxle,
      frontLeft,
      fixture.ShapeSources[RideVisualRole.FrontAxle]);
    AssertWheel(
      result.BackRightWheel,
      RideCarVisualHierarchyAnchorSource.RearAxle,
      rearRight,
      fixture.ShapeSources[RideVisualRole.RearAxle]);
    AssertWheel(
      result.BackLeftWheel,
      RideCarVisualHierarchyAnchorSource.RearAxle,
      rearLeft,
      fixture.ShapeSources[RideVisualRole.RearAxle]);
  }

  [Test]
  public void Resolve_UsesBodyWheelFallbackOnlyWhenAxleVisualOrAnchorIsAbsent() {
    var frontRight = Bone("WheelFR");
    var frontLeft = Bone("WheelFL");
    var rearRight = Bone("WheelRR");
    var rearLeft = Bone("WheelRL");
    var fixture = Fixture(new FixtureOptions([
      Bone("AxleR"),
      frontRight,
      frontLeft,
      rearRight,
      rearLeft,
    ]) {
      FrontAxleBones = [],
      RearAxleBones = [Bone("NotAWheel")],
      UndeclaredVisuals = new HashSet<RideVisualRole> {
        RideVisualRole.FrontAxle,
      },
    });

    var result = RideCarVisualHierarchyResolver.Resolve(fixture.Resources).Cars.Single();

    Assert.That(result.FrontAxle.Status, Is.EqualTo(
      RideCarVisualHierarchyPartStatus.VisualNotDeclared));
    Assert.That(result.RearAxle.Status, Is.EqualTo(
      RideCarVisualHierarchyPartStatus.Resolved));
    AssertWheel(
      result.FrontRightWheel,
      RideCarVisualHierarchyAnchorSource.Body,
      frontRight,
      fixture.ShapeSources[RideVisualRole.Body]);
    AssertWheel(
      result.FrontLeftWheel,
      RideCarVisualHierarchyAnchorSource.Body,
      frontLeft,
      fixture.ShapeSources[RideVisualRole.Body]);
    AssertWheel(
      result.BackRightWheel,
      RideCarVisualHierarchyAnchorSource.Body,
      rearRight,
      fixture.ShapeSources[RideVisualRole.Body]);
    AssertWheel(
      result.BackLeftWheel,
      RideCarVisualHierarchyAnchorSource.Body,
      rearLeft,
      fixture.ShapeSources[RideVisualRole.Body]);
  }

  [Test]
  public void Resolve_DeclaredUnresolvedAxleFailsClosedWithoutBodyFallback() {
    var fixture = Fixture(new FixtureOptions([
      Bone("AxleF"),
      Bone("AxleR"),
      Bone("WheelFR"),
      Bone("WheelFL"),
      Bone("WheelRR"),
      Bone("WheelRL"),
    ]) {
      UnresolvedVisuals = new HashSet<RideVisualRole> {
        RideVisualRole.FrontAxle,
      },
    });

    var result = RideCarVisualHierarchyResolver.Resolve(fixture.Resources).Cars.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.FrontAxle.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.VisualUnresolved));
      Assert.That(result.FrontRightWheel.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AxleVisualUnresolved));
      Assert.That(result.FrontLeftWheel.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AxleVisualUnresolved));
      Assert.That(result.FrontRightWheel.Anchor, Is.Null);
      Assert.That(result.FrontLeftWheel.Anchor, Is.Null);
    }
  }

  [Test]
  public void Resolve_AmbiguousCaseInsensitiveNamesFailClosedWithoutBodyFallback() {
    var fixture = Fixture(new FixtureOptions([
      Bone("AxleF"),
      Bone("aXlEf"),
      Bone("AxleR"),
      Bone("WheelFR"),
      Bone("WheelFL"),
      Bone("WheelRR"),
      Bone("WheelRL"),
    ]) {
      RearAxleBones = [Bone("WheelR"), Bone("wHeElR"), Bone("WheelL")],
    });

    var registry = RideCarVisualHierarchyResolver.Resolve(fixture.Resources);
    var result = registry.Cars.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.FrontAxle.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AnchorAmbiguous));
      Assert.That(result.FrontRightWheel.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AnchorAmbiguous));
      Assert.That(result.FrontLeftWheel.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AnchorAmbiguous));
      Assert.That(result.FrontRightWheel.Anchor, Is.Null);
      Assert.That(result.FrontLeftWheel.Anchor, Is.Null);
      Assert.That(result.BackRightWheel.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AnchorAmbiguous));
      Assert.That(result.BackRightWheel.Anchor, Is.Null);
      Assert.That(result.BackLeftWheel.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.Resolved));
      Assert.That(registry.AmbiguousPartCount, Is.EqualTo(4));
    }
  }

  [Test]
  public void Resolve_MissingOptionalVisualsAndAnchorsReturnTypedOutcomes() {
    var fixture = Fixture(new FixtureOptions([]) {
      FrontAxleBones = [],
      RearAxleBones = [],
      UndeclaredVisuals = new HashSet<RideVisualRole> {
        RideVisualRole.FrontLeftWheel,
      },
      UnresolvedVisuals = new HashSet<RideVisualRole> {
        RideVisualRole.BackLeftWheel,
      },
    });

    var result = RideCarVisualHierarchyResolver.Resolve(fixture.Resources).Cars.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.FrontAxle.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AnchorMissing));
      Assert.That(result.RearAxle.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AnchorMissing));
      Assert.That(result.FrontRightWheel.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AxleAnchorUnavailable));
      Assert.That(result.FrontLeftWheel.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.VisualNotDeclared));
      Assert.That(result.BackRightWheel.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AxleAnchorUnavailable));
      Assert.That(result.BackLeftWheel.Status, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.VisualUnresolved));
      Assert.That(result.Parts, Has.All.Property("Anchor").Null);
    }
  }

  [Test]
  public void Resolve_WithGraphRetainsCarsWhoseEveryVisualIsUnresolved() {
    var unresolved = new HashSet<RideVisualRole> {
      RideVisualRole.Body,
      RideVisualRole.FrontAxle,
      RideVisualRole.RearAxle,
      RideVisualRole.FrontRightWheel,
      RideVisualRole.FrontLeftWheel,
      RideVisualRole.BackRightWheel,
      RideVisualRole.BackLeftWheel,
    };
    var fixture = Fixture(new FixtureOptions([]) {
      UnresolvedVisuals = unresolved,
    });

    var bridgeOnly = RideCarVisualHierarchyResolver.Resolve(fixture.Resources);
    var complete = RideCarVisualHierarchyResolver.Resolve(
      fixture.Graph,
      fixture.Resources);
    var result = complete.Cars.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(bridgeOnly.Cars, Is.Empty);
      Assert.That(bridgeOnly.CoversAllDecodedCarOccurrences, Is.False);
      Assert.That(complete.CoversAllDecodedCarOccurrences, Is.True);
      Assert.That(complete.UnresolvedCarReferenceCount, Is.Zero);
      Assert.That(result.BodyStatus, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.VisualUnresolved));
      Assert.That(result.BodyVisual,
        Is.SameAs(fixture.VisualLinks[RideVisualRole.Body]));
      Assert.That(result.Parts, Has.All.Property("Status").EqualTo(
        RideCarVisualHierarchyPartStatus.VisualUnresolved));
      Assert.That(complete.TryGet(fixture.Car, out var indexed), Is.True);
      Assert.That(indexed, Is.SameAs(result));
    }
  }

  [Test]
  public void Resolve_WithGraphReturnsTypedUnsupportedAnimalSpeciesBodyWithoutSvd() {
    var fixture = Fixture(new FixtureOptions([]) {
      OmitBodyVisual = true,
      AnimalSpecies = "Elephant:was",
      UndeclaredVisuals = new HashSet<RideVisualRole> {
        RideVisualRole.FrontAxle,
        RideVisualRole.RearAxle,
        RideVisualRole.FrontRightWheel,
        RideVisualRole.FrontLeftWheel,
        RideVisualRole.BackRightWheel,
        RideVisualRole.BackLeftWheel,
      },
    });

    var registry = RideCarVisualHierarchyResolver.Resolve(
      fixture.Graph,
      fixture.Resources);
    var result = registry.Cars.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(fixture.Resources.Visuals, Is.Empty);
      Assert.That(registry.CoversAllDecodedCarOccurrences, Is.True);
      Assert.That(registry.UnresolvedCarReferenceCount, Is.Zero);
      Assert.That(result.BodyStatus, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.AnimalSpeciesBodyUnsupported));
      Assert.That(result.BodyVisual, Is.Null);
      Assert.That(result.BodyShapeVisual, Is.Null);
      Assert.That(result.Parts, Has.All.Property("Status").EqualTo(
        RideCarVisualHierarchyPartStatus.VisualNotDeclared));
      Assert.That(registry.TryGet(fixture.Car, out var indexed), Is.True);
      Assert.That(indexed, Is.SameAs(result));
    }
  }

  [Test]
  public void Resolve_WithGraphRejectsNullBodyWithoutAnimalSpecies() {
    var fixture = Fixture(new FixtureOptions([]) {
      OmitBodyVisual = true,
      UndeclaredVisuals = new HashSet<RideVisualRole> {
        RideVisualRole.FrontAxle,
        RideVisualRole.RearAxle,
        RideVisualRole.FrontRightWheel,
        RideVisualRole.FrontLeftWheel,
        RideVisualRole.BackRightWheel,
        RideVisualRole.BackLeftWheel,
      },
    });

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualHierarchyResolver.Resolve(fixture.Graph, fixture.Resources)));

    Assert.That(error?.Message, Does.Contain("required Body is null"));
  }

  [Test]
  public void Resolve_WithGraphKeepsOrdinaryBodySvdIdentityUnchanged() {
    var fixture = Fixture(new FixtureOptions([]));

    var result = RideCarVisualHierarchyResolver.Resolve(
      fixture.Graph,
      fixture.Resources).Cars.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.BodyStatus, Is.EqualTo(
        RideCarVisualHierarchyPartStatus.Resolved));
      Assert.That(result.BodyVisual,
        Is.SameAs(fixture.VisualLinks[RideVisualRole.Body]));
      Assert.That(result.BodyShapeVisual, Is.Not.Null);
    }
  }

  [Test]
  public void Resolve_RejectsOversizedInputBeforeEnumeratingIt() {
    var resources = new RideCarVisualResourceBridgeResult(
      new CountOnlyReadOnlyList<RideCarVisualShapeLink>(2),
      0);
    var limits = new RideCarVisualHierarchyResolverLimits(1, 1, 8, 8, 8, 128);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualHierarchyResolver.Resolve(resources, limits)));

    Assert.That(error!.Message, Does.Contain("visual occurrence count exceeds"));
  }

  [Test]
  public void Resolve_WithGraphRejectsOversizedRideListBeforeEnumeratingIt() {
    var graph = new RideResourceGraph(
      new CountOnlyReadOnlyList<TrackedRideResourceLink>(2),
      0);
    var resources = new RideCarVisualResourceBridgeResult([], 0);
    var limits = new RideCarVisualHierarchyResolverLimits(8, 1, 8, 8, 8, 128);

    var error = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarVisualHierarchyResolver.Resolve(graph, resources, limits)));

    Assert.That(error!.Message, Does.Contain("ride count exceeds"));
  }

  private static void AssertWheel(
    RideCarVisualHierarchyPart actual,
    RideCarVisualHierarchyAnchorSource expectedSource,
    BoneShapeBone expectedBone,
    RideBoneShapeResourceSource expectedShapeSource
  ) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(actual.Status, Is.EqualTo(RideCarVisualHierarchyPartStatus.Resolved));
      Assert.That(actual.Anchor, Is.Not.Null);
      Assert.That(actual.Anchor!.Source, Is.EqualTo(expectedSource));
      Assert.That(actual.Anchor.Bone, Is.SameAs(expectedBone));
      Assert.That(actual.Anchor.ShapeSource, Is.SameAs(expectedShapeSource));
      Assert.That(actual.Anchor.ShapeSource.Resource.Bones[actual.Anchor.BoneIndex],
        Is.SameAs(expectedBone));
    }
  }

  private static HierarchyFixture Fixture(FixtureOptions options) {
    var undeclared = options.UndeclaredVisuals ?? new HashSet<RideVisualRole>();
    var unresolved = options.UnresolvedVisuals ?? new HashSet<RideVisualRole>();
    var shapeBones = new Dictionary<RideVisualRole, IReadOnlyList<BoneShapeBone>> {
      [RideVisualRole.Body] = options.BodyBones,
      [RideVisualRole.FrontAxle] = options.FrontAxleBones ??
        [Bone("WheelR"), Bone("WheelL")],
      [RideVisualRole.RearAxle] = options.RearAxleBones ??
        [Bone("WheelR"), Bone("WheelL")],
      [RideVisualRole.FrontRightWheel] = [],
      [RideVisualRole.FrontLeftWheel] = [],
      [RideVisualRole.BackRightWheel] = [],
      [RideVisualRole.BackLeftWheel] = [],
    };
    if (options.WildFlippedBodyBones != null)
      shapeBones.Add(
        RideVisualRole.WildFlippedBody,
        options.WildFlippedBodyBones);
    var definitions = new Dictionary<RideVisualRole, VisualDefinition>();
    foreach (var role in shapeBones.Keys) {
      if (undeclared.Contains(role) ||
          (role == RideVisualRole.Body && options.OmitBodyVisual)) continue;
      var isResolved = !unresolved.Contains(role);
      definitions.Add(role, Visual(role, shapeBones[role], isResolved));
    }

    var carResource = Car(
      Reference(definitions, RideVisualRole.Body),
      new RideCarWheelSettings(
        new RideCarVisualPart(
          Reference(definitions, RideVisualRole.FrontRightWheel),
          options.FrontRightWheelType),
        new RideCarVisualPart(
          Reference(definitions, RideVisualRole.FrontLeftWheel),
          options.FrontLeftWheelType),
        new RideCarVisualPart(
          Reference(definitions, RideVisualRole.BackRightWheel),
          options.BackRightWheelType),
        new RideCarVisualPart(
          Reference(definitions, RideVisualRole.BackLeftWheel),
          options.BackLeftWheelType)),
      new RideCarAxleSettings(
        new RideCarVisualPart(
          Reference(definitions, RideVisualRole.FrontAxle),
          options.FrontAxleType),
        new RideCarVisualPart(
          Reference(definitions, RideVisualRole.RearAxle),
          options.RearAxleType)),
      Reference(definitions, RideVisualRole.WildFlippedBody),
      options.AnimalSpecies);
    var carSource = new RideCarResourceSource(
      new OvlFile(carResource.Name, FileType.RideCar, "cars.unique.ovl"),
      carResource,
      [
        "ride.unique.ovl",
        "train.unique.ovl",
        "cars.unique.ovl",
        "visuals.unique.ovl",
        "shapes.unique.ovl",
      ]);
    var car = new RideCarLink(
      RideTrainCarRole.Front,
      $"{carResource.Name}:ric",
      carSource,
      definitions.Values.Select(definition => definition.Link).ToArray());
    var trainResource = Train(carResource.Name);
    var train = new RideTrainLink(
      trainResource.Name,
      new RideTrainResourceSource(
        new OvlFile(trainResource.Name, FileType.RideTrain, "train.unique.ovl"),
        trainResource,
        carSource.AllowedArchivePaths),
      [car]);
    var rideResource = Ride(trainResource.Name);
    var ride = new TrackedRideResourceLink(
      new TrackedRideResourceSource(
        new OvlFile(rideResource.Name, FileType.TrackedRide, "ride.unique.ovl"),
        rideResource,
        carSource.AllowedArchivePaths),
      [train],
      null);
    var shapeVisuals = definitions.Values
      .Where(definition => definition.Link.IsResolved)
      .Select(definition => new RideCarVisualShapeLink(
        ride,
        train,
        car,
        definition.Link,
        [definition.Lod!]))
      .ToArray();
    var shapeSources = definitions
      .Where(pair => pair.Value.ShapeSource != null)
      .ToDictionary(pair => pair.Key, pair => pair.Value.ShapeSource!);
    var graph = new RideResourceGraph([ride], unresolved.Count);
    return new HierarchyFixture(
      new RideCarVisualResourceBridgeResult(shapeVisuals, 0),
      graph,
      ride,
      train,
      car,
      shapeSources,
      definitions.ToDictionary(pair => pair.Key, pair => pair.Value.Link));
  }

  private static VisualDefinition Visual(
    RideVisualRole role,
    IReadOnlyList<BoneShapeBone> bones,
    bool resolved
  ) {
    var name = role.ToString();
    var reference = $"{name}:svd";
    if (!resolved)
      return new VisualDefinition(
        new RideVisualLink(role, reference, null),
        null,
        null);

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
      reference,
      new RideVisualResourceSource(
        new OvlFile(name, FileType.SceneryItemVisual, "visuals.unique.ovl"),
        visual));
    return new VisualDefinition(
      link,
      new RideVisualShapeLodLink(lod, null, shapeSource),
      shapeSource);
  }

  private static string? Reference(
    IReadOnlyDictionary<RideVisualRole, VisualDefinition> definitions,
    RideVisualRole role
  ) => definitions.TryGetValue(role, out var definition)
    ? definition.Link.Reference
    : null;

  private static BoneShapeBone Bone(string name) => new(
    name,
    -1,
    Matrix4x4.Identity,
    Matrix4x4.Identity);

  private static RideCar Car(
    string? bodyVisual,
    RideCarWheelSettings wheels,
    RideCarAxleSettings axles,
    string? wildFlippedBodyVisual,
    string? animalSpecies
  ) {
    var hasWildSettings = wildFlippedBodyVisual != null || animalSpecies != null;
    return new(
      "Car",
      hasWildSettings ? RideCarVersion.Wild : RideCarVersion.Vanilla,
      "Car",
      "Car",
      0,
      0,
      bodyVisual,
      1f,
      null,
      -1f,
      new RideCarAxisSettings(0, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
      new RideCarBobbingSettings(0, 0f, 0f, 0f),
      new RideCarAnimationSettings(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
      [],
      wheels,
      axles,
      new RideCarBaseUnknownSettings(0, 0f, 0, 0f, 0f, 0f, 0f),
      !hasWildSettings
        ? null
        : new RideCarSoakedSettings(
          0f, 0, 0, 0f, 0, 0, 0f, 0, 0, 0, 0f, 0, 0f),
      !hasWildSettings
        ? null
        : new RideCarWildSettings(
          1,
          wildFlippedBodyVisual,
          null,
          0,
          1f,
          1f,
          animalSpecies,
          4.1f,
          0,
          -1,
          0));
  }

  private static RideTrain Train(string carName) => new(
    "Train",
    RideTrainVersion.Vanilla,
    "Train",
    "Train",
    new RideTrainCars($"{carName}:ric", null, null, null, null, null, 1, 1, 1, null),
    new RideTrainSpeedSettings(0f, 0f, 0f),
    new RideTrainCameraSettings(
      0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    new RideTrainWaterSettings(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    0,
    new RideTrainUnknownSettings(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    null,
    null,
    null,
    null,
    null);

  private static TrackedRide Ride(string trainName) => new(
    "Ride",
    TrackedRideVersion.Vanilla,
    [],
    [trainName],
    null,
    null,
    null,
    null!,
    null!,
    null!,
    null!,
    null!,
    null,
    null);

  private sealed record FixtureOptions(IReadOnlyList<BoneShapeBone> BodyBones) {
    public IReadOnlyList<BoneShapeBone>? FrontAxleBones { get; init; }
    public IReadOnlyList<BoneShapeBone>? RearAxleBones { get; init; }
    public IReadOnlyList<BoneShapeBone>? WildFlippedBodyBones { get; init; }
    public IReadOnlySet<RideVisualRole>? UndeclaredVisuals { get; init; }
    public IReadOnlySet<RideVisualRole>? UnresolvedVisuals { get; init; }
    public bool OmitBodyVisual { get; init; }
    public string? AnimalSpecies { get; init; }
    public uint FrontAxleType { get; init; } = 1;
    public uint RearAxleType { get; init; } = 3;
    public uint FrontRightWheelType { get; init; }
    public uint FrontLeftWheelType { get; init; } = 1;
    public uint BackRightWheelType { get; init; } = 2;
    public uint BackLeftWheelType { get; init; } = 3;
  }

  private sealed record VisualDefinition(
    RideVisualLink Link,
    RideVisualShapeLodLink? Lod,
    RideBoneShapeResourceSource? ShapeSource
  );

  private sealed record HierarchyFixture(
    RideCarVisualResourceBridgeResult Resources,
    RideResourceGraph Graph,
    TrackedRideResourceLink Ride,
    RideTrainLink Train,
    RideCarLink Car,
    IReadOnlyDictionary<RideVisualRole, RideBoneShapeResourceSource> ShapeSources,
    IReadOnlyDictionary<RideVisualRole, RideVisualLink> VisualLinks
  );

  private sealed class CountOnlyReadOnlyList<T>(int count) : IReadOnlyList<T> {
    public int Count => count;
    public T this[int index] => throw new InvalidOperationException(
      $"Indexer must not be read for oversized fixture at {index}.");
    public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException(
      "Enumerator must not be read for oversized fixture.");
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
  }
}
