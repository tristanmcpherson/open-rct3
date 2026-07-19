// GdkIngestionTests
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using NUnit.Framework;
using OpenCobra.GDK.Assets;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;
using System;
using System.IO;

namespace OpenCobra.Tests.Integration;

[TestFixture]
public class IngestionTests {
  [Test]
  [SkipIfEnvironmentMissing("RCT3_PATH")]
  public void LoadTerrainTexture_Succeeds() {
    using var _ = Assert.EnterMultipleScope();
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    var terrainOvl = Path.Combine(rct3Path, "terrain", "RCT3", "Terrain_RCT3.common.ovl");

    if (!File.Exists(terrainOvl))
      Assert.Fail("Terrain OVL not found at: " + terrainOvl);

    // This now verifies the actual texture names identified in the terrain OVL instead of only
    // proving the archive exists.
    using var ovl = Ovl.Load(terrainOvl);
    var entries = ovl.Keys.Where(entry => entry.Type == FileType.Texture).ToList();
    using var textures = Textures.Extract(ovl);

    TestContext.Out.WriteLine(
      $"Terrain_RCT3: {entries.Count} Texture entries, {textures.Count} decoded: " +
      string.Join(", ", textures.Names));
    Assert.That(entries, Has.Count.EqualTo(32));
    Assert.That(textures.Names, Does.Contain("Terrain_00.tex"));
    var decodedGrass = textures["Terrain_00.tex"];
    Assert.That(decodedGrass.MipLevels[0], Is.Not.Null);
    TestContext.Out.WriteLine(
      $"Terrain_00: {decodedGrass.Format} {decodedGrass.Width}x{decodedGrass.Height}, " +
      $"sample={decodedGrass.MipLevels[0][0, 0]}");

    using var grass = TextureLoader.LoadTexture(terrainOvl, "Terrain_00");
    Assert.That(grass.Name, Is.EqualTo("Terrain_00"));
    Assert.That(grass.Width, Is.GreaterThan(0));
    Assert.That(grass.Height, Is.GreaterThan(0));
    Assert.That(grass.Pixels, Is.Not.Null);
  }
}
