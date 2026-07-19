// ExtractResources
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using System.Collections.Generic;
using DotNetEnv;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenCobra.Tests.Integration;

[TestFixture]
public class ExtractResources {
  private static string? Rct3Path() => Environment.GetEnvironmentVariable("RCT3_PATH");

  [SetUp]
  public void Setup() {
    if (File.Exists(Constants.EnvFilePath))
      Env.NoClobber().Load(Constants.EnvFilePath);
  }

  [Test]
  [SkipIfEnvironmentMissing("RCT3_PATH", "Cannot find RCT3. Skipping integration test.")]
  public void Load_NullbmpFtx_ExtractsFlexibleTexture() {
    var rct3 = Rct3Path()!;

    var commonPath = Path.Combine(rct3, "nullbmp.common.ovl");
    Assert.That(File.Exists(commonPath), Is.True, $"nullbmp.common.ovl not found at: {commonPath}");

    var resources = Ovl.Load(commonPath);
    Assert.That(resources, Is.Not.Empty, "No resources found in nullbmp.common.ovl");

    var ftxEntry = resources.FirstOrDefault(e => e.Key.Type == FileType.FlexibleTexture);
    Assert.That(ftxEntry, Is.Not.Default, "No FlexibleTexture (ftx) resource found");

    var bytes = resources.ReadResource(ftxEntry.Key);
    Assert.That(bytes, Is.Not.Null);

    var asString = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 16));
    using (Assert.EnterMultipleScope()) {
      // Must NOT be the symbol name string
      Assert.That(asString, Does.Not.Contain("nullbmp:"),
          "Load returned the symbol name string instead of texture data");

      // FlexiTextureInfoStruct: scale(u32), width(u32), height(u32), ...
      // Valid FTX textures are square powers of two (width == height, both power-of-two)
      Assert.That(bytes, Has.Length.GreaterThanOrEqualTo(12), "Too short to contain FTX width/height");
      var width  = BitConverter.ToUInt32(bytes, 4);
      var height = BitConverter.ToUInt32(bytes, 8);
      Assert.That(width, Is.EqualTo(height), "FTX width and height must be equal (square texture)");
      Assert.That(width == 0 || (width & (width - 1)) == 0,
        $"FTX width {width} is not a power of two");
    }

    // `offset1` and `fts2` in FlexiTextureInfoStruct are relocated pointers, not inline data, so
    // fully decoding the texture (palette, indexed pixels, alpha mask) requires following them
    // through Ovl.TryResolveRelocation rather than reading the resource's raw bytes sequentially.
    var flexiTexture = FlexiTextureList.Load(resources, ftxEntry.Key);
    using (Assert.EnterMultipleScope()) {
      Assert.That(flexiTexture.Length, Is.GreaterThan(0), "FlexiTexture must have at least one frame");
      Assert.That(flexiTexture.Width, Is.GreaterThan(0).And.EqualTo(flexiTexture.Height));

      var frame = flexiTexture[0];
      Assert.That(frame.Texture.Width, Is.EqualTo(flexiTexture.Width));
      Assert.That(frame.Texture.Height, Is.EqualTo(flexiTexture.Height));

      // Pixels must vary; a resolver bug landing on the wrong block tends to produce either a
      // crash, a single repeated color, or the symbol name string reinterpreted as pixel data.
      var distinctPixels = new HashSet<Rgba32>();
      for (var y = 0; y < frame.Texture.Height && distinctPixels.Count < 2; y++)
        for (var x = 0; x < frame.Texture.Width && distinctPixels.Count < 2; x++)
          distinctPixels.Add(frame.Texture[x, y]);
      Assert.That(distinctPixels, Has.Count.GreaterThan(1), "Decoded FTX texture has no pixel variation");
    }
  }

  [Test]
  [SkipIfEnvironmentMissing("RCT3_PATH", "Cannot find RCT3. Skipping integration test.")]
  public void Load_StyleCommon_ClassifiesFltAsFloat() {
    var stylePath = Path.Combine(Rct3Path()!, "Style", "Vanilla", "Style.common.ovl");
    Assert.That(File.Exists(stylePath), Is.True, $"Style.common.ovl not found at: {stylePath}");

    using var ovl = Ovl.Load(stylePath);
    var mapColourRed = ovl.Keys.SingleOrDefault(key => key.Name == "MapColourRed");
    Assert.That(mapColourRed, Is.Not.Null, "MapColourRed.flt not found in Style.common.ovl");
    var bytes = ovl.ReadResource(mapColourRed!);
    using (Assert.EnterMultipleScope()) {
      Assert.That(mapColourRed!.Type, Is.EqualTo(FileType.Float));
      Assert.That(mapColourRed.ToString(), Is.EqualTo("MapColourRed.flt"));
      Assert.That(bytes, Is.Not.Null.And.Length.GreaterThanOrEqualTo(sizeof(float)));
    }
  }

  [Test]
  public void Load_ShapesCommon_HasResources() {
    var rct3 = Rct3Path();
    if (rct3 == null)
      Assert.Ignore("Cannot find RCT3. Skipping integration test.");

    var shapesPath = Path.Combine(rct3, "test", "Shapes.common.ovl");
    Assert.That(File.Exists(shapesPath), Is.True, $"Shapes.common.ovl not found at: {shapesPath}");

    var resources = Ovl.Load(shapesPath);
    Assert.That(resources, Is.Not.Empty, "No resources found in Shapes.common.ovl");
  }

  private static IEnumerable<TestCaseData> GetOvlFixtures() {
    if (File.Exists(Constants.EnvFilePath))
      Env.NoClobber().Load(Constants.EnvFilePath);

    var rct3Path = Rct3Path();
    if (string.IsNullOrEmpty(rct3Path)) {
      yield return new TestCaseData(string.Empty)
        .Explicit("Cannot find RCT3. Skipping integration test.")
        .Ignore("Cannot find RCT3. Skipping integration test.");
      yield break;
    }

    var files = Directory.GetFiles(rct3Path, "*.ovl", SearchOption.AllDirectories);
    if (files.Length == 0) {
      yield return new TestCaseData(string.Empty).Ignore("No OVL fixtures found.");
      yield break;
    }

    foreach (var file in files)
      yield return new TestCaseData(file);
  }

  /// <remarks>
  /// Float symbols such as "MapColourRed" in Style.common.ovl and "PlatformHeight" in
  /// tracks/Platforms/Vanilla/*.ovl use the distinct "flt" tag and are classified as
  /// FileType.Float. Every entry reaching this test is therefore tagged "ftx" and must decode;
  /// malformed entries fail instead of being filtered by a header plausibility check.
  /// </remarks>
  [TestCaseSource(nameof(GetOvlFixtures))]
  public void FtxResources_AreDecodable(string ovlPath) {
    Assert.That(ovlPath, Does.Exist, $"OVL not found: {ovlPath}");

    using var ovl = Ovl.Load(ovlPath);
    var ftxEntries = ovl.Keys.Where(key => key.Type == FileType.FlexibleTexture).ToList();
    if (ftxEntries.Count == 0)
      Assert.Ignore($"No FlexibleTexture (ftx) resources found in {Path.GetFileName(ovlPath)}.");

    using (Assert.EnterMultipleScope()) {
      foreach (var entry in ftxEntries) {
        var bytes = ovl.ReadResource(entry) ??
          throw new AssertionException($"{entry.Name} in {Path.GetFileName(ovlPath)} has no data");
        Assert.That(bytes, Has.Length.GreaterThanOrEqualTo(36),
          $"{entry.Name} in {Path.GetFileName(ovlPath)}: truncated FTX header");

        var width = BitConverter.ToUInt32(bytes, 4);
        var height = BitConverter.ToUInt32(bytes, 8);

        var flexiTexture = FlexiTextureList.Load(ovl, entry);
        Assert.That(flexiTexture.Width, Is.EqualTo(Convert.ToInt32(width)),
          $"{entry.Name} in {Path.GetFileName(ovlPath)}: decoded width does not match header");
        Assert.That(flexiTexture.Height, Is.EqualTo(Convert.ToInt32(height)),
          $"{entry.Name} in {Path.GetFileName(ovlPath)}: decoded height does not match header");
        Assert.That(flexiTexture.Length, Is.GreaterThan(0),
          $"{entry.Name} in {Path.GetFileName(ovlPath)}: expected at least one decoded frame");
      }
    }
  }
}
