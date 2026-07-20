// DAT Path Surface Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;

namespace OpenRCT3.Tests.Serialization;

[TestFixture]
public class DatPathSurfaceResolverTests {
  [Test]
  public void Resolve_LinksOrdinaryAndWrappedQueueSurfacesWithoutDiscardingRawReferences() {
    var path = Ground(surface: 101);
    var queue = Queue(surface: 202);
    var pathType = new DatPathTypeDatabaseEntryData(101, true, false, true, "Asphalt");
    var queueType = new DatQueueTypeDatabaseEntryData(303, true, false, true, "QueueSet1");
    var queueGround = new DatQueueTypeGroundSurfaceData(
      202,
      new DatPathSurfaceColours(4, 5, 6),
      queueType.EntryId);

    DatPathSurfaceResolver.Resolve([path, queue], [pathType, queueGround, queueType]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(path.Surface, Is.EqualTo(101));
      Assert.That(path.ResolvedSurface, Is.SameAs(pathType));
      Assert.That(queue.Surface, Is.EqualTo(202));
      Assert.That(queue.ResolvedSurface, Is.SameAs(queueGround));
      Assert.That(queueGround.QueueType, Is.EqualTo(303));
      Assert.That(queueGround.ResolvedQueueType, Is.SameAs(queueType));
    }
  }

  [Test]
  public void Resolve_LinksQueueDirectlyToQueueTypeDatabaseEntry() {
    var queue = Queue(surface: 17);
    var queueType = new DatQueueTypeDatabaseEntryData(17, true, false, false, "QueueSet2");

    DatPathSurfaceResolver.Resolve([queue], [queueType]);

    Assert.That(queue.ResolvedSurface, Is.SameAs(queueType));
  }

  [Test]
  public void Resolve_LeavesUnavailableExternalReferencesUnresolved() {
    var path = Ground(surface: 99);
    var queueGround = new DatQueueTypeGroundSurfaceData(
      12,
      new DatPathSurfaceColours(1, 2, 3),
      queueType: 88);

    DatPathSurfaceResolver.Resolve([path], [queueGround]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(path.ResolvedSurface, Is.Null);
      Assert.That(queueGround.ResolvedQueueType, Is.Null);
    }
  }

  [Test]
  public void Resolve_DuplicateSurfaceEntryIdsFailClosed() {
    var first = new DatPathTypeDatabaseEntryData(4, true, false, true, "Asphalt");
    var second = new DatPathTypeDatabaseEntryData(4, true, false, true, "Brick");

    Assert.Throws<InvalidDataException>(new Action(() =>
      DatPathSurfaceResolver.Resolve([], [first, second])));
  }

  [Test]
  public void Resolve_IncompatiblePathSurfaceKindFailsClosed() {
    var path = Ground(surface: 7);
    var queueType = new DatQueueTypeDatabaseEntryData(7, true, false, true, "QueueSet1");

    Assert.Throws<InvalidDataException>(new Action(() =>
      DatPathSurfaceResolver.Resolve([path], [queueType])));
  }

  [Test]
  public void Resolve_QueueGroundReferenceToOrdinaryPathTypeFailsClosed() {
    var pathType = new DatPathTypeDatabaseEntryData(9, true, false, true, "Asphalt");
    var queueGround = new DatQueueTypeGroundSurfaceData(
      8,
      new DatPathSurfaceColours(1, 2, 3),
      pathType.EntryId);

    Assert.Throws<InvalidDataException>(new Action(() =>
      DatPathSurfaceResolver.Resolve([], [queueGround, pathType])));
  }

  private static DatPathGroundData Ground(ulong surface) => new(
    entryId: 1,
    colIndex: 0,
    direction: byte.MaxValue,
    pathType: 0,
    rowIndex: 0,
    surface,
    surfaceType: byte.MaxValue,
    boolValue: 0);

  private static DatPathQueueData Queue(ulong surface) => new(
    entryId: 2,
    baseHeight: 0,
    colIndex: 0,
    direction: byte.MaxValue,
    endDirection: byte.MaxValue,
    fenceEntry: 0,
    fenceFlexiColours: default,
    pathType: 2,
    quantisedHeight: 0,
    queueLine: 0,
    rowIndex: 0,
    sceneryItem: 0,
    slopeType: 3,
    startDirection: byte.MaxValue,
    surface,
    surfaceType: byte.MaxValue,
    undergroundFlag: null,
    boolValue: 0);
}
