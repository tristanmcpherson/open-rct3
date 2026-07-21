// Animated Ride Placement Registry Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class AnimatedRidePlacementRegistryTests {
  [Test]
  public void Build_ResolvesOnlyExactOverlayAndSidIdentity() {
    var resource = Resource(@"Rides\Roundup", "Roundup");
    var placements = new[] {
      Placement(1, @"rides\roundup", "roundup"),
      Placement(2, @"Rides\Other", "Roundup"),
      Placement(3, @"Rides\Roundup", "Other"),
    };

    var registry = AnimatedRidePlacementRegistry.Build(placements, [resource]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.Links[0].Status, Is.EqualTo(AnimatedRidePlacementStatus.Resolved));
      Assert.That(registry.Links[0].Resource, Is.SameAs(resource));
      Assert.That(registry.Links[1].Status,
        Is.EqualTo(AnimatedRidePlacementStatus.MissingOverlayCatalog));
      Assert.That(registry.Links[2].Status,
        Is.EqualTo(AnimatedRidePlacementStatus.MissingAnimatedRide));
    }
  }

  [Test]
  public void Build_RetainsMultipleAnrsForOneSidAsAmbiguous() {
    var placement = Placement(1, @"Rides\Roundup", "Roundup");
    var first = Resource(@"Rides\Roundup", "Roundup", "FirstAnr");
    var second = Resource(@"Rides\Roundup", "Roundup", "SecondAnr");

    var registry = AnimatedRidePlacementRegistry.Build([placement], [first, second]);

    Assert.That(registry.Links[0].Status,
      Is.EqualTo(AnimatedRidePlacementStatus.AmbiguousAnimatedRide));
  }

  [Test]
  public void Build_PreservesTypedAnrToSidFailure() {
    var resource = Resource(
      @"Rides\Roundup",
      "Roundup",
      status: AnimatedRideResourceLinkStatus.MissingSceneryItem);

    var registry = AnimatedRidePlacementRegistry.Build(
      [Placement(1, @"Rides\Roundup", "Roundup")],
      [resource]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(registry.Links[0].Status,
        Is.EqualTo(AnimatedRidePlacementStatus.UnresolvedRideScenery));
      Assert.That(registry.Links[0].Resource, Is.SameAs(resource));
    }
  }

  [Test]
  public void Build_RejectsDuplicatePlacementEntryIds() {
    var placements = new[] {
      Placement(1, @"Rides\Roundup", "Roundup"),
      Placement(1, @"Rides\Roundup", "Roundup"),
    };

    Assert.Throws<InvalidDataException>(new Action(() =>
      AnimatedRidePlacementRegistry.Build(placements, [])));
  }

  private static AnimatedRideResourceLink Resource(
    string overlay,
    string sceneryName,
    string rideName = "Roundup",
    AnimatedRideResourceLinkStatus status = AnimatedRideResourceLinkStatus.Resolved
  ) {
    const string path = "Roundup.unique.ovl";
    var ride = new AnimatedRideResourceSource(
      new OvlFile(rideName, FileType.AnimatedRide, path),
      new AnimatedRide(
        rideName,
        AnimatedRideVersion.Vanilla,
        new AnimatedRideAttraction(0, "Name:txt", "Description:txt", "", 0, "", [], 0,
          0, null, null, new Dictionary<int, uint>()),
        new AnimatedRideSettings(0, 0, [], 0, 0, 0, [], new Dictionary<int, uint>()),
        sceneryName + ":sid",
        [],
        new Dictionary<int, uint>()));
    AnimatedRideSceneryResourceSource? scenery = status == AnimatedRideResourceLinkStatus.Resolved
      ? new(
        new OvlFile(sceneryName, FileType.SceneryItem, path),
        new SceneryItem(
          sceneryName, 0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 1, SidType.Ride, []))
      : null;
    return new AnimatedRideResourceLink(overlay, ride, scenery, status);
  }

  private static SceneryPlacement Placement(
    ulong entryId,
    string overlay,
    string objectKey
  ) {
    var placement = new SceneryPlacement(objectKey, 0, 0) {
      SourceEntryId = entryId,
      OverlayPath = overlay,
    };
    return placement;
  }
}
