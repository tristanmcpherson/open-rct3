// Ride Track Resource Catalog Load Context Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideTrackResourceCatalogLoadContextTests {
  [Test]
  public void FindExactResource_SearchesOnlyWhitelistAndRejectsDuplicateDefinitions() {
    var root = Path.Combine(Path.GetTempPath(), $"openrct3-ride-materials-{Guid.NewGuid():N}");
    var allowedPath = Path.Combine(root, "allowed.unique.ovl");
    var outsidePath = Path.Combine(root, "outside.unique.ovl");
    using var archive = new Ovl("fixture");
    var allowed = Add(archive, "Body", allowedPath);
    _ = Add(archive, "Body", outsidePath);
    _ = Add(archive, "BodyExtra", allowedPath);
    using var context = Context(archive);

    var exact = context.FindExactResource(
      [allowedPath],
      "body:FTX",
      FileType.FlexibleTexture);
    var missing = context.FindExactResource(
      [allowedPath],
      "Bod:ftx",
      FileType.FlexibleTexture);

    using (Assert.EnterMultipleScope()) {
      Assert.That(exact, Is.Not.Null);
      Assert.That(exact!.Archive, Is.SameAs(archive));
      Assert.That(exact.File, Is.SameAs(allowed));
      Assert.That(missing, Is.Null);
    }
    Assert.Throws<InvalidDataException>(new Action(() =>
      context.FindExactResource(
        [allowedPath, outsidePath],
        "Body:ftx",
        FileType.FlexibleTexture)));
  }

  [TestCase("Body")]
  [TestCase("Body:ftx:ftx")]
  [TestCase("Body:shs")]
  [TestCase(" Body:ftx")]
  public void FindExactResource_RejectsMalformedOrWrongTaggedReferences(string reference) {
    var path = Path.Combine(Path.GetTempPath(), "openrct3-context.unique.ovl");
    using var archive = new Ovl("fixture");
    using var context = Context(archive);

    Assert.Throws<InvalidDataException>(new Action(() =>
      context.FindExactResource([path], reference, FileType.FlexibleTexture)));
  }

  [TestCase("")]
  [TestCase("padded.unique.ovl ")]
  [TestCase("wild*.unique.ovl")]
  [TestCase("not-an-ovl.bin")]
  public void FindExactResource_RejectsMalformedWhitelistPaths(string path) {
    using var archive = new Ovl("fixture");
    using var context = Context(archive);

    Assert.Throws<InvalidDataException>(new Action(() =>
      context.FindExactResource([path], "Body:ftx", FileType.FlexibleTexture)));
  }

  [Test]
  public void FindExactResource_RejectsRepeatedWhitelistPathAndDisposedContext() {
    var path = Path.Combine(Path.GetTempPath(), "openrct3-context.unique.ovl");
    using var archive = new Ovl("fixture");
    var context = Context(archive);

    Assert.Throws<InvalidDataException>(new Action(() =>
      context.FindExactResource([path, path], "Body:ftx", FileType.FlexibleTexture)));
    context.Dispose();
    Assert.Throws<ObjectDisposedException>(new Action(() =>
      context.FindExactResource([path], "Body:ftx", FileType.FlexibleTexture)));
  }

  private static OvlFile Add(Ovl archive, string name, string path) {
    var file = new OvlFile(name, FileType.FlexibleTexture, path);
    archive.Add(file, new OvlEntry(0, 1));
    return file;
  }

  private static RideTrackResourceCatalogLoadContext Context(Ovl archive) => new(
    ["fixture.common.ovl"],
    [archive],
    _ => { });
}
