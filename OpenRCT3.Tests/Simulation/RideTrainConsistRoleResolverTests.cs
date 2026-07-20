// Ride Train Consist Role Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrainConsistRoleResolverTests {
  [Test]
  public void Resolve_ReproducesBoxOfficeZeroSlotLinkSequence() {
    var cars = Cars(
      front: "front",
      middle: "middle",
      rear: "rear",
      link: "link",
      minimum: 1,
      maximum: 8,
      @default: 4);

    var result = Resolve(cars, 4, ("front", 3), ("middle", 12), ("rear", 3),
      ("link", 0));

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.EffectiveCarCount, Is.EqualTo(4));
      Assert.That(result.Entries.Select(entry => entry.RuntimeIndex),
        Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5, 6 }));
      Assert.That(result.Entries.Select(entry => entry.NonLinkIndex),
        Is.EqualTo(new int?[] { 0, null, 1, null, 2, null, 3 }));
      Assert.That(result.Entries.Select(entry => entry.Role), Is.EqualTo(new[] {
        RideTrainCarRole.Front,
        RideTrainCarRole.Link,
        RideTrainCarRole.Middle,
        RideTrainCarRole.Link,
        RideTrainCarRole.Middle,
        RideTrainCarRole.Link,
        RideTrainCarRole.Rear,
      }));
      Assert.That(result.Entries.Select(entry => entry.CountsTowardConfiguredCarCount),
        Is.EqualTo(new[] { true, false, true, false, true, false, true }));
    }
  }

  [Test]
  public void Resolve_UsesEveryDistinctOrdinaryRoleForFiveCars() {
    var cars = Cars("front", "second", "middle", "penultimate", "rear");

    var result = Resolve(cars, 5,
      ("front", 1), ("second", 1), ("middle", 1), ("penultimate", 1),
      ("rear", 1));

    Assert.That(result.Entries.Select(entry => entry.Role), Is.EqualTo(new[] {
      RideTrainCarRole.Front,
      RideTrainCarRole.Second,
      RideTrainCarRole.Middle,
      RideTrainCarRole.Penultimate,
      RideTrainCarRole.Rear,
    }));
  }

  [Test]
  public void Resolve_DoesNotInsertLinkWithPeepSlots() {
    var cars = Cars("front", middle: "middle", rear: "rear", link: "link");

    var result = Resolve(cars, 4,
      ("front", 1), ("middle", 1), ("rear", 1), ("link", 1));

    Assert.That(result.Entries.Select(entry => entry.Role), Is.EqualTo(new[] {
      RideTrainCarRole.Front,
      RideTrainCarRole.Middle,
      RideTrainCarRole.Middle,
      RideTrainCarRole.Rear,
    }));
  }

  [TestCase(1, new[] { RideTrainCarRole.Rear })]
  [TestCase(2, new[] { RideTrainCarRole.Front, RideTrainCarRole.Rear })]
  [TestCase(3, new[] {
    RideTrainCarRole.Front,
    RideTrainCarRole.Second,
    RideTrainCarRole.Rear,
  })]
  public void Resolve_PreservesSmallConsistRolePrecedence(
    int count,
    RideTrainCarRole[] expected
  ) {
    var cars = Cars("front", "second", "middle", "penultimate", "rear");

    var result = Resolve(cars, count,
      ("front", 1), ("second", 1), ("middle", 1), ("penultimate", 1),
      ("rear", 1));

    Assert.That(result.Entries.Select(entry => entry.Role), Is.EqualTo(expected));
  }

  [Test]
  public void Resolve_AddsReferencedZeroSlotOrdinaryRolesOutsideConfiguredCount() {
    var cars = Cars("front", "second", "middle", rear: "rear");

    var result = Resolve(cars, 2,
      ("front", 0), ("second", 0), ("middle", 2), ("rear", 2));

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.EffectiveCarCount, Is.EqualTo(2));
      Assert.That(result.Entries.Select(entry => entry.Role), Is.EqualTo(new[] {
        RideTrainCarRole.Front,
        RideTrainCarRole.Second,
        RideTrainCarRole.Middle,
        RideTrainCarRole.Rear,
      }));
      Assert.That(result.Entries.Select(entry => entry.CountsTowardConfiguredCarCount),
        Is.EqualTo(new[] { false, false, true, true }));
    }
  }

  [TestCase(0, 3)]
  [TestCase(1, 2)]
  [TestCase(99, 4)]
  public void Resolve_UsesDefaultThenClampsToRitLimits(int savedCount, int expected) {
    var cars = Cars("front", middle: "middle", rear: "rear",
      minimum: 2, maximum: 4, @default: 3);

    var result = Resolve(cars, savedCount, ("front", 1), ("middle", 1), ("rear", 1));

    Assert.That(result.EffectiveCarCount, Is.EqualTo(expected));
    Assert.That(result.Entries, Has.Count.EqualTo(expected));
  }

  [Test]
  public void Resolve_FailsClosedForMalformedCountsSlotsAndBounds() {
    var valid = Cars("front");
    var invalidLimits = Cars("front", minimum: 4, maximum: 2, @default: 3);

    Assert.Throws<InvalidDataException>(new Action(() =>
      Resolve(invalidLimits, 3, ("front", 1))));
    Assert.Throws<InvalidDataException>(new Action(() =>
      Resolve(valid, 1, ("front", -1))));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideTrainConsistRoleResolver.Resolve(
        valid,
        2,
        _ => 0,
        new RideTrainConsistRoleLimits(1))));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideTrainConsistRoleResolver.Resolve(
        valid,
        1,
        _ => 1,
        new RideTrainConsistRoleLimits(0))));
  }

  private static RideTrainConsistRoleResolution Resolve(
    RideTrainCars cars,
    int savedCount,
    params (string ResourceName, int PeepSlotCount)[] peepSlots
  ) {
    var counts = peepSlots.ToDictionary(item => item.ResourceName,
      item => item.PeepSlotCount, StringComparer.Ordinal);
    return RideTrainConsistRoleResolver.Resolve(cars, savedCount, name => counts[name]);
  }

  private static RideTrainCars Cars(
    string front,
    string? second = null,
    string? middle = null,
    string? penultimate = null,
    string? rear = null,
    string? link = null,
    uint minimum = 1,
    uint maximum = 8,
    uint @default = 1
  ) => new(
    front,
    second,
    middle,
    penultimate,
    rear,
    link,
    minimum,
    maximum,
    @default,
    null);
}
