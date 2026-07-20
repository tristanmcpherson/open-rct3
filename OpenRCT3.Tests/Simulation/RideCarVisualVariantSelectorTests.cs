// Ride Car Visual Variant Selector Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarVisualVariantSelectorTests {
  [Test]
  public void Build_ExplicitNormalSelectsExactBodyAndMovingIndependentOfReversed() {
    var fixture = Create(visualVariant: 0, reversed: true);

    var result = RideCarVisualVariantSelector.Build(fixture.Runtime, fixture.Bridge);
    var entry = result.Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.IsSelected, Is.True);
      Assert.That(entry.SavedVisualVariant, Is.Zero);
      Assert.That(entry.SelectedVariant, Is.EqualTo(RideCarVisualVariant.Normal));
      Assert.That(entry.CarRuntime.SavedReversed, Is.True);
      Assert.That(entry.Car, Is.SameAs(fixture.Car));
      Assert.That(entry.Body, Is.SameAs(fixture.Occurrences[RideVisualRole.Body]));
      Assert.That(entry.Moving, Is.SameAs(fixture.Occurrences[RideVisualRole.Moving]));
      Assert.That(entry.BodyControlFallback, Is.Null);
      Assert.That(entry.Issue, Is.Null);
      Assert.That(result.SelectedCount, Is.EqualTo(1));
      Assert.That(result.FailedCount, Is.Zero);
    }
  }

  [Test]
  public void Build_AbsentSavedVariantSelectsNormalPair() {
    var fixture = Create(visualVariant: null);

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      fixture.Bridge).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.SavedVisualVariant, Is.Null);
      Assert.That(entry.SelectedVariant, Is.EqualTo(RideCarVisualVariant.Normal));
      Assert.That(entry.Body, Is.SameAs(fixture.Occurrences[RideVisualRole.Body]));
      Assert.That(entry.Moving, Is.SameAs(fixture.Occurrences[RideVisualRole.Moving]));
    }
  }

  [Test]
  public void Build_SerializedVisualReferenceComparisonIsCaseInsensitive() {
    var fixture = Create(
      visualVariant: 0,
      normalBodyGraphReference: "bODY:SVD");

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      fixture.Bridge).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.IsSelected, Is.True);
      Assert.That(entry.Body, Is.SameAs(fixture.Occurrences[RideVisualRole.Body]));
      Assert.That(fixture.Car.Car!.Visual, Is.EqualTo("Body:svd"));
      Assert.That(entry.Body!.Visual.Reference, Is.EqualTo("bODY:SVD"));
    }
  }

  [Test]
  public void Build_WildVariantSelectsFlippedBodyAndMovingAsOnePair() {
    var fixture = Create(visualVariant: 1);

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      fixture.Bridge).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.IsSelected, Is.True);
      Assert.That(entry.SelectedVariant, Is.EqualTo(RideCarVisualVariant.WildFlipped));
      Assert.That(entry.RequiredBodyRole, Is.EqualTo(RideVisualRole.WildFlippedBody));
      Assert.That(entry.RequiredMovingRole, Is.EqualTo(RideVisualRole.WildFlippedMoving));
      Assert.That(
        entry.Body,
        Is.SameAs(fixture.Occurrences[RideVisualRole.WildFlippedBody]));
      Assert.That(
        entry.Moving,
        Is.SameAs(fixture.Occurrences[RideVisualRole.WildFlippedMoving]));
      Assert.That(entry.Body, Is.Not.SameAs(fixture.Occurrences[RideVisualRole.Body]));
      Assert.That(entry.Moving, Is.Not.SameAs(fixture.Occurrences[RideVisualRole.Moving]));
    }
  }

  [Test]
  public void Build_MissingMovingExposesExactBodyControlFallback() {
    var fixture = Create(visualVariant: 0, includeNormalMoving: false);

    var result = RideCarVisualVariantSelector.Build(fixture.Runtime, fixture.Bridge);
    var entry = result.Entries.Single();
    var fallback = entry.BodyControlFallback!;

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.IsSelected, Is.True);
      Assert.That(entry.Moving, Is.Null);
      Assert.That(entry.UsesBodyControlFallback, Is.True);
      Assert.That(fallback.Car, Is.SameAs(fixture.Car));
      Assert.That(fallback.Body, Is.SameAs(entry.Body));
      Assert.That(fallback.MissingMovingRole, Is.EqualTo(RideVisualRole.Moving));
      Assert.That(result.BodyControlFallbackCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Build_DeclaredButUnresolvedMovingFailsWithoutBodyFallback() {
    var fixture = Create(visualVariant: 0, resolveNormalMoving: false);

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      fixture.Bridge).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        fixture.Car.Visuals.Single(visual => visual.Role == RideVisualRole.Moving).IsResolved,
        Is.False);
      Assert.That(
        fixture.Visuals.Any(link => link.Visual.Role == RideVisualRole.Moving),
        Is.False);
      Assert.That(entry.Body, Is.Null);
      Assert.That(entry.Moving, Is.Null);
      Assert.That(entry.BodyControlFallback, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.UnresolvedRequiredVisual));
      Assert.That(entry.Issue.Role, Is.EqualTo(RideVisualRole.Moving));
    }
  }

  [Test]
  public void Build_DeclaredButUnresolvedBodyIsTypedBeforeBridgeLookup() {
    var fixture = Create(visualVariant: 0, resolveNormalBody: false);

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      fixture.Bridge).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(
        fixture.Car.Visuals.Single(visual => visual.Role == RideVisualRole.Body).IsResolved,
        Is.False);
      Assert.That(
        fixture.Visuals.Any(link => link.Visual.Role == RideVisualRole.Body),
        Is.False);
      Assert.That(entry.Body, Is.Null);
      Assert.That(entry.Moving, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.UnresolvedRequiredVisual));
      Assert.That(entry.Issue.Role, Is.EqualTo(RideVisualRole.Body));
    }
  }

  [Test]
  public void Build_UnsupportedVariantIsTypedAndDoesNotSelectNormalVisuals() {
    var fixture = Create(visualVariant: 2);

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      fixture.Bridge).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.IsSelected, Is.False);
      Assert.That(entry.SelectedVariant, Is.Null);
      Assert.That(entry.Body, Is.Null);
      Assert.That(entry.Moving, Is.Null);
      Assert.That(entry.BodyControlFallback, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.UnsupportedVariant));
    }
  }

  [Test]
  public void Build_MissingFlippedBodyFailsWithoutFallingBackToNormal() {
    var fixture = Create(visualVariant: 1, includeWildBody: false);

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      fixture.Bridge).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.IsSelected, Is.False);
      Assert.That(entry.Body, Is.Null);
      Assert.That(entry.Moving, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.MissingRequiredBodyVisual));
      Assert.That(entry.Issue.Role, Is.EqualTo(RideVisualRole.WildFlippedBody));
      Assert.That(fixture.Occurrences.ContainsKey(RideVisualRole.Body), Is.True);
    }
  }

  [Test]
  public void Build_InjectedBodyDeclarationCannotChangeSerializedIdentity() {
    var fixture = Create(
      visualVariant: 0,
      normalBodyGraphReference: "InjectedBody:svd");

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      fixture.Bridge).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(fixture.Car.Car!.Visual, Is.EqualTo("Body:svd"));
      Assert.That(
        fixture.Car.Visuals.Single(visual => visual.Role == RideVisualRole.Body).Reference,
        Is.EqualTo("InjectedBody:svd"));
      Assert.That(entry.Body, Is.Null);
      Assert.That(entry.Moving, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.ChangedSerializedVisualIdentity));
      Assert.That(entry.Issue.Role, Is.EqualTo(RideVisualRole.Body));
    }
  }

  [Test]
  public void Build_FakeWildGraphRolesCannotCreateVariantOnVanillaRic() {
    var fixture = Create(
      visualVariant: 1,
      includeDecodedWildExtension: false);

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      fixture.Bridge).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(fixture.Car.Car!.Version, Is.EqualTo(RideCarVersion.Vanilla));
      Assert.That(fixture.Car.Car.Wild, Is.Null);
      Assert.That(
        fixture.Car.Visuals.Any(visual => visual.Role == RideVisualRole.WildFlippedBody),
        Is.True);
      Assert.That(entry.Body, Is.Null);
      Assert.That(entry.Moving, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.UnavailableSerializedVariant));
    }
  }

  [Test]
  public void Build_DuplicateExactBodyOccurrenceIsTypedAmbiguity() {
    var fixture = Create(visualVariant: 0);
    var body = fixture.Occurrences[RideVisualRole.Body];
    var bridge = new RideCarVisualResourceBridgeResult([body, body], 0);

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      bridge).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.Body, Is.Null);
      Assert.That(entry.Moving, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.DuplicateExactVisualOccurrence));
      Assert.That(entry.Issue.Role, Is.EqualTo(RideVisualRole.Body));
      Assert.That(entry.Issue.MatchingOccurrenceCount, Is.EqualTo(2));
    }
  }

  [Test]
  public void Build_ValueEqualButDifferentCarLinkIsTypedIdentityChange() {
    var fixture = Create(visualVariant: 0);
    var changedCar = fixture.Car with { };
    var changedBody = fixture.Occurrences[RideVisualRole.Body] with { Car = changedCar };

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      new RideCarVisualResourceBridgeResult([changedBody], 0)).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(changedCar, Is.EqualTo(fixture.Car));
      Assert.That(changedCar, Is.Not.SameAs(fixture.Car));
      Assert.That(entry.Car, Is.SameAs(fixture.Car));
      Assert.That(entry.Body, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.ChangedCarIdentity));
    }
  }

  [Test]
  public void Build_CaseVariantDifferentCarLinkStillDetectsChangedIdentity() {
    var fixture = Create(visualVariant: 0);
    var changedCar = fixture.Car with { Reference = "cAR:RIC" };
    var changedBody = fixture.Occurrences[RideVisualRole.Body] with { Car = changedCar };

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      new RideCarVisualResourceBridgeResult([changedBody], 0)).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(changedCar.Reference, Is.Not.EqualTo(fixture.Car.Reference));
      Assert.That(
        changedCar.Reference,
        Is.EqualTo(fixture.Car.Reference).IgnoreCase);
      Assert.That(entry.Body, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.ChangedCarIdentity));
    }
  }

  [Test]
  public void Build_UnresolvedRequiredBodyLodIsTypedAndFailClosed() {
    var fixture = Create(visualVariant: 0);
    var body = fixture.Occurrences[RideVisualRole.Body];
    var unresolvedBody = body with {
      Lods = [new RideVisualShapeLodLink(body.Lods.Single().Lod, null, null)],
    };
    var visuals = fixture.Visuals
      .Select(link => ReferenceEquals(link, body) ? unresolvedBody : link)
      .ToArray();

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      new RideCarVisualResourceBridgeResult(visuals, 1)).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.Body, Is.Null);
      Assert.That(entry.Moving, Is.Null);
      Assert.That(entry.BodyControlFallback, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.UnresolvedRequiredLod));
      Assert.That(entry.Issue.Role, Is.EqualTo(RideVisualRole.Body));
      Assert.That(entry.Issue.UnresolvedLodCount, Is.EqualTo(1));
    }
  }

  [Test]
  public void Build_UnresolvedPresentMovingDoesNotBecomeBodyFallback() {
    var fixture = Create(visualVariant: 1);
    var moving = fixture.Occurrences[RideVisualRole.WildFlippedMoving];
    var unresolvedMoving = moving with {
      Lods = [new RideVisualShapeLodLink(moving.Lods.Single().Lod, null, null)],
    };
    var visuals = fixture.Visuals
      .Select(link => ReferenceEquals(link, moving) ? unresolvedMoving : link)
      .ToArray();

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      new RideCarVisualResourceBridgeResult(visuals, 1)).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.Body, Is.Null);
      Assert.That(entry.Moving, Is.Null);
      Assert.That(entry.BodyControlFallback, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.UnresolvedRequiredLod));
      Assert.That(entry.Issue.Role, Is.EqualTo(RideVisualRole.WildFlippedMoving));
    }
  }

  [Test]
  public void Build_CopiedSerializedLodObjectFailsClosed() {
    var fixture = Create(visualVariant: 0);
    var body = fixture.Occurrences[RideVisualRole.Body];
    var linked = body.Lods.Single();
    var copied = linked with { Lod = linked.Lod with { } };

    AssertChangedLodIdentity(fixture, body with { Lods = [copied] });
  }

  [Test]
  public void Build_WrongShapeTargetIdentityFailsClosed() {
    var fixture = Create(visualVariant: 0);
    var body = fixture.Occurrences[RideVisualRole.Body];
    var linked = body.Lods.Single();
    var wrongShape = new StaticShape(
      "WrongShape",
      Vector3.Zero,
      Vector3.One,
      Meshes: [],
      Effects: []);
    var wrongSource = new RideStaticShapeResourceSource(
      new OvlFile("WrongShape", FileType.StaticShape, linked.StaticShapeSource!.File.Path),
      wrongShape);

    AssertChangedLodIdentity(
      fixture,
      body with { Lods = [linked with { StaticShapeSource = wrongSource }] });
  }

  [Test]
  public void Build_ShapeOutsideExactRicClosureFailsClosed() {
    var fixture = Create(visualVariant: 0);
    var body = fixture.Occurrences[RideVisualRole.Body];
    var linked = body.Lods.Single();
    var source = linked.StaticShapeSource!;
    var outside = source with {
      File = new OvlFile(source.File.Name, FileType.StaticShape, "outside.unique.ovl"),
    };

    AssertChangedLodIdentity(
      fixture,
      body with { Lods = [linked with { StaticShapeSource = outside }] });
  }

  [Test]
  public void Build_LodWithBothStaticAndBoneTargetsFailsClosed() {
    var fixture = Create(visualVariant: 0);
    var body = fixture.Occurrences[RideVisualRole.Body];
    var linked = body.Lods.Single();
    var bone = new BoneShape(
      "InjectedBone",
      Vector3.Zero,
      Vector3.One,
      Meshes: [],
      Bones: []);
    var boneSource = new RideBoneShapeResourceSource(
      new OvlFile("InjectedBone", FileType.BoneShape, "shape.unique.ovl"),
      bone);

    AssertChangedLodIdentity(
      fixture,
      body with { Lods = [linked with { BoneShapeSource = boneSource }] });
  }

  [Test]
  public void Build_EnforcesCarVisualAndLodBounds() {
    var fixture = Create(visualVariant: 0);

    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarVisualVariantSelector.Build(
        fixture.Runtime,
        fixture.Bridge,
        new RideCarVisualVariantSelectorLimits(0, 10, 10, 10))));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarVisualVariantSelector.Build(
        fixture.Runtime,
        fixture.Bridge,
        new RideCarVisualVariantSelectorLimits(10, 0, 10, 10))));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarVisualVariantSelector.Build(
        fixture.Runtime,
        fixture.Bridge,
        new RideCarVisualVariantSelectorLimits(10, 10, 0, 10))));
    Assert.Throws<InvalidOperationException>(new Action(() =>
      RideCarVisualVariantSelector.Build(
        fixture.Runtime,
        fixture.Bridge,
        new RideCarVisualVariantSelectorLimits(10, 10, 10, 0))));
  }

  [Test]
  public void Build_DoesNotMutateInputsAndSnapshotsRegistryOrder() {
    var fixture = Create(visualVariant: 0);
    var originalVisualOrder = fixture.Visuals.ToArray();
    var originalRuntimeEntry = fixture.Runtime.Entries.Single();

    var result = RideCarVisualVariantSelector.Build(fixture.Runtime, fixture.Bridge);

    using (Assert.EnterMultipleScope()) {
      Assert.That(fixture.Visuals, Is.EqualTo(originalVisualOrder));
      Assert.That(fixture.Runtime.Entries.Single(), Is.SameAs(originalRuntimeEntry));
    }
    fixture.Visuals.Clear();
    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Entries, Has.Count.EqualTo(1));
      Assert.That(result.Entries.Single().CarRuntime, Is.SameAs(originalRuntimeEntry));
      Assert.That(
        result.Entries.Single().Body,
        Is.SameAs(fixture.Occurrences[RideVisualRole.Body]));
      Assert.Throws<NotSupportedException>(new Action(() =>
        ((IList<RideCarVisualVariantSelectionEntry>)result.Entries).Clear()));
    }
  }

  [Test]
  public void Build_RetainsOnlyExactBridgeOccurrenceAuthority() {
    var fixture = Create(visualVariant: 0);
    var body = fixture.Occurrences[RideVisualRole.Body];
    var moving = fixture.Occurrences[RideVisualRole.Moving];

    var result = RideCarVisualVariantSelector.Build(fixture.Runtime, fixture.Bridge);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.IsAuthorizedOccurrence(body), Is.True);
      Assert.That(result.IsAuthorizedOccurrence(moving), Is.True);
      Assert.That(result.IsAuthorizedOccurrence(body with { }), Is.False);
      Assert.That(result.IsAuthorizedOccurrence(moving with { }), Is.False);
    }
  }

  private static void AssertChangedLodIdentity(
    Fixture fixture,
    RideCarVisualShapeLink changedBody
  ) {
    var visuals = fixture.Visuals
      .Select(link => link.Visual.Role == RideVisualRole.Body ? changedBody : link)
      .ToArray();

    var entry = RideCarVisualVariantSelector.Build(
      fixture.Runtime,
      new RideCarVisualResourceBridgeResult(visuals, 0)).Entries.Single();

    using (Assert.EnterMultipleScope()) {
      Assert.That(entry.Body, Is.Null);
      Assert.That(entry.Moving, Is.Null);
      Assert.That(entry.BodyControlFallback, Is.Null);
      Assert.That(
        entry.Issue!.Status,
        Is.EqualTo(RideCarVisualVariantSelectionStatus.ChangedRequiredLodIdentity));
      Assert.That(entry.Issue.Role, Is.EqualTo(RideVisualRole.Body));
    }
  }

  private static Fixture Create(
    int? visualVariant,
    bool reversed = false,
    bool includeNormalMoving = true,
    bool includeWildBody = true,
    bool includeWildMoving = true,
    bool resolveNormalBody = true,
    bool resolveNormalMoving = true,
    bool resolveWildBody = true,
    bool resolveWildMoving = true,
    bool includeDecodedWildExtension = true,
    string normalBodyGraphReference = "Body:svd"
  ) {
    const string ridePath = "ride.unique.ovl";
    const string trainPath = "train.unique.ovl";
    const string carPath = "car.unique.ovl";
    const string visualPath = "visual.unique.ovl";
    const string shapePath = "shape.unique.ovl";
    var closure = new[] { carPath, visualPath, shapePath };
    var visualParts = new List<VisualFixture> {
      Visual(
        RideVisualRole.Body,
        "Body",
        visualPath,
        shapePath,
        resolveNormalBody,
        normalBodyGraphReference),
    };
    if (includeNormalMoving)
      visualParts.Add(
        Visual(RideVisualRole.Moving, "Moving", visualPath, shapePath, resolveNormalMoving));
    if (includeWildBody)
      visualParts.Add(
        Visual(
          RideVisualRole.WildFlippedBody,
          "WildBody",
          visualPath,
          shapePath,
          resolveWildBody));
    if (includeWildMoving)
      visualParts.Add(
        Visual(
          RideVisualRole.WildFlippedMoving,
          "WildMoving",
          visualPath,
          shapePath,
          resolveWildMoving));

    var carResource = CarResource(
      includeNormalMoving ? "Moving:svd" : null,
      includeDecodedWildExtension,
      includeWildBody ? "WildBody:svd" : null,
      includeWildMoving ? "WildMoving:svd" : null);
    var car = new RideCarLink(
      RideTrainCarRole.Front,
      "Car:ric",
      new RideCarResourceSource(
        new OvlFile(carResource.Name, FileType.RideCar, carPath),
        carResource,
        closure),
      Array.AsReadOnly(visualParts.Select(part => part.Link).ToArray()));
    var trainResource = TrainResource();
    var trainSource = new RideTrainResourceSource(
      new OvlFile(trainResource.Name, FileType.RideTrain, trainPath),
      trainResource,
      [trainPath, carPath, visualPath, shapePath]);
    var trainGraph = new RideTrainLink(trainResource.Name, trainSource, [car]);
    var trackedRide = TrackedRide();
    var rideGraph = new TrackedRideResourceLink(
      new TrackedRideResourceSource(
        new OvlFile(trackedRide.Name, FileType.TrackedRide, ridePath),
        trackedRide,
        [ridePath, trainPath, carPath, visualPath, shapePath]),
      [trainGraph],
      WildSplitter: null);
    var occurrences = visualParts.ToDictionary(
      part => part.Link.Role,
      part => new RideCarVisualShapeLink(
        rideGraph,
        trainGraph,
        car,
        part.Link,
        [part.Lod]));
    var visuals = occurrences.Values
      .Where(link => link.Visual.IsResolved)
      .ToList();

    var ride = RideInstance();
    var train = TrainInstance(ride, visualVariant);
    var savedCar = CarInstance(train, reversed);
    var trainRuntime = TrainRuntime(ride, train, trainSource);
    var role = new RideTrainConsistRoleEntry(
      RuntimeIndex: 0,
      NonLinkIndex: 0,
      RideTrainCarRole.Front,
      ResourceName: car.Reference,
      PeepSlotCount: 1);
    var consistCar = new RideInstanceTrainConsistCarRuntimeEntry(
      role,
      car,
      new RideCarPeepSlotEvidence(
        car,
        occurrences[RideVisualRole.Body],
        PeepSlotCount: 1,
        MarkerLods: []));
    var consist = new RideInstanceTrainConsistRuntimeEntry(
      trainRuntime.Entries.Single(),
      rideGraph,
      trainGraph,
      new RideTrainConsistRoleResolution(1, [role]),
      [consistCar],
      RideInstanceTrainConsistRuntimeStatus.Resolved);
    var runtime = RideCarInstanceRuntimeRegistry.Build(
      trainRuntime,
      [savedCar],
      new RideInstanceTrainConsistRuntimeRegistry([consist]));
    return new(
      runtime,
      car,
      occurrences,
      visuals,
      new RideCarVisualResourceBridgeResult(visuals, 0));
  }

  private static VisualFixture Visual(
    RideVisualRole role,
    string name,
    string visualPath,
    string shapePath,
    bool resolved,
    string? reference = null
  ) {
    var lod = new SceneryItemVisualLod(
      "near",
      SvdLodType.StaticShape,
      $"{name}Shape:shs",
      null,
      null,
      null,
      new SceneryVisualBillboardSettings(1f, 1f, 0f, 0f, 1f, 1f),
      10f,
      []);
    var resource = new SceneryItemVisual(name, 0, 0f, 0f, 0f, 0f, [lod], null);
    var link = new RideVisualLink(
      role,
      reference ?? $"{name}:svd",
      resolved
        ? new RideVisualResourceSource(
          new OvlFile(name, FileType.SceneryItemVisual, visualPath),
          resource)
        : null);
    var shape = new StaticShape(
      $"{name}Shape",
      Vector3.Zero,
      Vector3.One,
      Meshes: [],
      Effects: []);
    var linkedLod = new RideVisualShapeLodLink(
      lod,
      new RideStaticShapeResourceSource(
        new OvlFile(shape.Name, FileType.StaticShape, shapePath),
        shape),
      BoneShapeSource: null);
    return new(link, linkedLod);
  }

  private static RideInstanceTrainRuntimeRegistry TrainRuntime(
    DatTrackedRideInstanceData ride,
    DatRideTrainInstanceData train,
    RideTrainResourceSource trainSource
  ) {
    var track = Track(ride);
    var identity = RideInstanceTrackGraph.Build([ride], [track]);
    var geometry = new RideTrackGeometryResolution(
      [new RideTrackGeometryLink(
        track,
        RideTrackGeometryStatus.UnsupportedGeometry,
        Graph: null,
        Circuit: null)],
      UnresolvedResourceTrackCount: 0,
      UnsupportedGeometryTrackCount: 1,
      UnsupportedTopologyTrackCount: 0);
    var trackRuntime = RideInstanceTrackRuntimeRegistry.Build(identity, geometry);
    var trainResources = RideTrainInstanceResourceRegistry.Build(
      [ride],
      [train],
      [new RideTrainInstanceResourceSource(@"Cars\Synthetic\Train", trainSource)]);
    return RideInstanceTrainRuntimeRegistry.Build(trackRuntime, trainResources);
  }

  private static DatTrackedRideInstanceData RideInstance() => new(
    entryId: 900,
    name: "Ride",
    track: 700,
    trackedRideOverlayName: @"Tracks\Synthetic",
    trackedRideSymbolName: "Ride:trr",
    nTrains: 1,
    nCarsPerTrain: 1,
    trainSelection: 0,
    trains: [1_000]);

  private static DatRideTrainInstanceData TrainInstance(
    DatTrackedRideInstanceData ride,
    int? visualVariant
  ) => new(
    entryId: 1_000,
    rideTrainOverlayName: @"Cars\Synthetic\Train",
    rideTrainSymbolName: "Train:rit",
    trackedRideInstance: ride.EntryId,
    whichTrain: 0,
    length: 4f,
    mass: 1_000f,
    distance: 2f,
    reversed: false,
    speed: 3f,
    cars: [2_000],
    whichRideCarSivVariant: visualVariant);

  private static DatRideCarInstanceData CarInstance(
    DatRideTrainInstanceData train,
    bool reversed
  ) => new(
    entryId: 2_000,
    rideTrainInstance: train.EntryId,
    whichCar: 0,
    whichRideTrainCar: Convert.ToInt32(RideTrainCarRole.Front),
    trackPiece: 0,
    rearTrackPiece: 0,
    distance: 2f,
    reversed,
    speed: 3f);

  private static RideTrack Track(DatTrackedRideInstanceData ride) => new(
    sourceEntryId: ride.Track,
    direction: 0,
    firstSegmentSourceEntryId: 800,
    lastSegmentSourceEntryId: 800,
    isCircuit: false,
    prototype: false,
    hasSerializedTrackPieceOrder: true,
    trackPieceSourceEntryIds: [],
    segmentSourceEntryIds: [800],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: ride.EntryId,
    flippedTrackSections: null,
    tunnelLightColour: null);

  private static RideTrain TrainResource() => new(
    "Train",
    RideTrainVersion.Vanilla,
    "Train",
    "Train",
    new RideTrainCars("Car:ric", null, null, null, null, null, 1, 1, 1, null),
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

  private static RideCar CarResource(
    string? moving,
    bool includeWildExtension,
    string? wildBody,
    string? wildMoving
  ) => new(
    "Car",
    includeWildExtension ? RideCarVersion.Wild : RideCarVersion.Vanilla,
    "Car",
    "Car",
    0,
    0,
    "Body:svd",
    1f,
    moving,
    -1f,
    new RideCarAxisSettings(0, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
    new RideCarBobbingSettings(0, 0f, 0f, 0f),
    new RideCarAnimationSettings(
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
    [],
    new RideCarWheelSettings(
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarAxleSettings(
      new RideCarVisualPart(null, 0),
      new RideCarVisualPart(null, 0)),
    new RideCarBaseUnknownSettings(0, 0f, 0, 0f, 0f, 0f, 0f),
    Soaked: null,
    includeWildExtension
      ? new RideCarWildSettings(
        1,
        wildBody,
        wildMoving,
        0,
        1f,
        1f,
        null,
        4.1f,
        0,
        0,
        0)
      : null);

  private static TrackedRide TrackedRide() => new(
    "Ride",
    TrackedRideVersion.Vanilla,
    [],
    ["Train"],
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

  private sealed record VisualFixture(
    RideVisualLink Link,
    RideVisualShapeLodLink Lod);

  private sealed record Fixture(
    RideCarInstanceRuntimeRegistry Runtime,
    RideCarLink Car,
    IReadOnlyDictionary<RideVisualRole, RideCarVisualShapeLink> Occurrences,
    List<RideCarVisualShapeLink> Visuals,
    RideCarVisualResourceBridgeResult Bridge);
}
