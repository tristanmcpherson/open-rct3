// Path Surface Resource Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class PathSurfaceResourceResolverTests {
  [Test]
  public void TryResolve_UsesCaseInsensitiveExactInternalNameAndTypedNamespace() {
    var pathType = PathResource(name: "Asphalt", internalName: "Asphalt");
    var queueType = QueueResource(name: "QueueSet1", internalName: "QueueSet1");
    var resolver = new PathSurfaceResourceResolver([pathType], [queueType]);

    var foundPath = resolver.TryResolve(
      new PathTile { SurfaceSystemName = "aSpHaLt" },
      out var resolvedPath);
    var foundQueue = resolver.TryResolve(
      new PathTile { IsQueue = true, SurfaceSystemName = "qUeUeSeT1" },
      out var resolvedQueue);

    using (Assert.EnterMultipleScope()) {
      Assert.That(foundPath, Is.True);
      Assert.That(resolvedPath, Is.EqualTo(new ResolvedPathTypeResource(pathType)));
      Assert.That(foundQueue, Is.True);
      Assert.That(resolvedQueue, Is.EqualTo(new ResolvedQueueTypeResource(queueType)));
    }
  }

  [Test]
  public void Constructor_RejectsUnprovenDivergentLoaderAndInternalNames() {
    var pathType = PathResource(name: "LoaderAlias", internalName: "Asphalt");

    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new PathSurfaceResourceResolver([pathType], [])));
  }

  [Test]
  public void TryResolve_PreservesUnknownExternalSystemName() {
    var resolver = new PathSurfaceResourceResolver(
      [PathResource(internalName: "Asphalt")],
      []);

    var found = resolver.TryResolve(
      new PathTile { SurfaceSystemName = "CustomPathPack" },
      out var resource);

    using (Assert.EnterMultipleScope()) {
      Assert.That(found, Is.False);
      Assert.That(resource, Is.Null);
    }
  }

  [TestCase(false, "QueueSet1")]
  [TestCase(true, "Asphalt")]
  public void TryResolve_RejectsResourceTypeMismatch(bool isQueue, string systemName) {
    var resolver = new PathSurfaceResourceResolver(
      [PathResource(internalName: "Asphalt")],
      [QueueResource(internalName: "QueueSet1")]);
    var tile = new PathTile { IsQueue = isQueue, SurfaceSystemName = systemName };

    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.TryResolve(tile, out _)));
  }

  [Test]
  public void Constructor_RejectsCaseInsensitiveDuplicatePtdInternalNames() {
    var first = PathResource(name: "Asphalt", internalName: "Asphalt");
    var second = PathResource(name: "ASPHALT", internalName: "ASPHALT");

    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new PathSurfaceResourceResolver([first, second], [])));
  }

  [Test]
  public void Constructor_RejectsCaseInsensitiveDuplicateQtdInternalNames() {
    var first = QueueResource(name: "QueueSet1", internalName: "QueueSet1");
    var second = QueueResource(name: "QUEUESET1", internalName: "QUEUESET1");

    Assert.Throws<InvalidDataException>(new Action(() =>
      _ = new PathSurfaceResourceResolver([], [first, second])));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void TryResolve_InstalledAsphaltAndQueueSet1MatchDatSystemNames() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(root),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(root),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");

    var parkPath = Path.GetFullPath(Path.Combine(
      TestContext.CurrentContext.TestDirectory,
      "..",
      "..",
      "..",
      "..",
      "OpenCobra",
      "Tests",
      "Fixtures",
      "Parks",
      "Fun Valley Amusment park.dat"));
    Assert.That(File.Exists(parkPath), Is.True, $"Fun Valley fixture is missing: {parkPath}");
    var data = DatTerrainReader.Read(parkPath);
    var pathDatabaseEntry = data.PathTypeDatabaseEntries.Single(entry =>
      string.Equals(entry.SystemName, "Asphalt", StringComparison.OrdinalIgnoreCase));
    var queueDatabaseEntry = data.QueueTypeDatabaseEntries.Single(entry =>
      string.Equals(entry.SystemName, "QueueSet1", StringComparison.OrdinalIgnoreCase));

    var pathArchive = Path.Combine(root!, "Path", "Asphalt", "Asphalt_Stub.common.ovl");
    var queueArchive = Path.Combine(root!, "Queue", "QueueSet1", "QueueSet1_Stub.common.ovl");
    using var pathOvl = Ovl.Load(pathArchive);
    using var queueOvl = Ovl.Load(queueArchive);
    var pathType = PathTypes.Extract(pathOvl).Single();
    var queueType = QueueTypes.Extract(queueOvl).Single();
    var resolver = new PathSurfaceResourceResolver([pathType], [queueType]);

    var foundPath = resolver.TryResolve(
      new PathTile { SurfaceSystemName = pathDatabaseEntry.SystemName },
      out var resolvedPath);
    var foundQueue = resolver.TryResolve(
      new PathTile {
        IsQueue = true,
        SurfaceSystemName = queueDatabaseEntry.SystemName,
      },
      out var resolvedQueue);

    using (Assert.EnterMultipleScope()) {
      Assert.That(pathDatabaseEntry.SystemName, Is.EqualTo(pathType.InternalName).IgnoreCase);
      Assert.That(queueDatabaseEntry.SystemName, Is.EqualTo(queueType.InternalName).IgnoreCase);
      Assert.That(pathType.Name, Is.EqualTo(pathType.InternalName).IgnoreCase);
      Assert.That(queueType.Name, Is.EqualTo(queueType.InternalName).IgnoreCase);
      Assert.That(foundPath, Is.True);
      Assert.That(((ResolvedPathTypeResource)resolvedPath!).Resource, Is.SameAs(pathType));
      Assert.That(foundQueue, Is.True);
      Assert.That(((ResolvedQueueTypeResource)resolvedQueue!).Resource, Is.SameAs(queueType));
    }
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void LoadInstalled_ResolvesStockOrdinaryAndRecolouredQueueTextures() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(root),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(root),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");
    var queueColours = new PathSurfaceColours(4, 5, 6);
    var path = new PathTile { SurfaceSystemName = "Asphalt" };
    var queue = new PathTile {
      IsQueue = true,
      SurfaceSystemName = "QueueSet1",
      SurfaceColours = queueColours,
    };

    using var resolver = PathSurfaceResourceResolver.LoadInstalled(root!, [path, queue]);
    var foundPath = resolver.TryResolveTexture(path, out var pathTexture);
    var foundQueue = resolver.TryResolveTexture(queue, out var queueTexture);

    using (Assert.EnterMultipleScope()) {
      Assert.That(foundPath, Is.True);
      Assert.That(pathTexture, Is.Not.Null);
      Assert.That(pathTexture!.Name, Is.EqualTo("Path_Asphalt_GroundA"));
      Assert.That(foundQueue, Is.True);
      Assert.That(queueTexture, Is.Not.Null);
      Assert.That(queueTexture!.Name, Is.EqualTo("GroundQueueSet1"));
      Assert.That(queueTexture.IsRecolorable, Is.True);
    }
  }

  private static PathType PathResource(
    string name = "Asphalt",
    string internalName = "Asphalt"
  ) => new(
    name,
    Flags: 1,
    internalName,
    DisplayNameRef: "PathName:txt",
    IconRef: "PathIcon:gsi",
    Texture1Ref: "Path_Texture1",
    Texture2Ref: "Path_Texture2",
    ShapeOwners: [],
    ResearchCategories: [],
    Extended: null);

  private static QueueType QueueResource(
    string name = "QueueSet1",
    string internalName = "QueueSet1"
  ) => new(
    name,
    internalName,
    DisplayNameRef: "QueueName:txt",
    IconRef: "QueueIcon:gsi",
    FlexiTextureRef: "Queue_Texture:ftx",
    Straight: "Straight",
    TurnLeft: "TurnL",
    TurnRight: "TurnR",
    SlopeUp: "SlopeUp",
    SlopeDown: "SlopeDown",
    SlopeStraight1: "SlopeStraight1",
    SlopeStraight2: "SlopeStraight2",
    ResearchCategories: []);
}
