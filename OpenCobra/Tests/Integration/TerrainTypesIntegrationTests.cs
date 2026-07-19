using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;

namespace OpenCobra.Tests.Integration;

[TestFixture]
public class TerrainTypesIntegrationTests {
  [Test]
  [SkipIfEnvironmentMissing("RCT3_PATH")]
  public void TerrainRct3_DecodesTypedReferences() {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    var commonPath = Path.Combine(rct3Path, "terrain", "RCT3", "Terrain_RCT3.common.ovl");
    Assert.That(commonPath, Does.Exist);

    using var ovl = Ovl.Load(commonPath);
    var terrainEntries = ovl.Keys.Where(file => file.Type == FileType.TerrainType).ToList();
    var terrains = TerrainTypes.Extract(ovl);

    TestContext.Out.WriteLine(
      $"Terrain_RCT3: {terrainEntries.Count} TER entries, {terrains.Count} decoded");
    foreach (var terrain in terrains) {
      TestContext.Out.WriteLine(
        $"{terrain.Name}: number={terrain.Number}, type={terrain.Type}, addon={terrain.Addon}, " +
        $"txt={terrain.Description.QualifiedName}, gsi={terrain.Icon.QualifiedName}, " +
        $"tex={terrain.Texture.QualifiedName}");
    }

    Assert.That(terrainEntries, Is.Not.Empty);
    Assert.That(terrains, Has.Count.EqualTo(terrainEntries.Count));
    using (Assert.EnterMultipleScope()) {
      foreach (var terrain in terrains) {
        Assert.That(terrain.Name, Is.Not.Empty);
        Assert.That(terrain.Description.Type, Is.EqualTo(FileType.Text));
        Assert.That(terrain.Description.Name, Is.Not.Empty);
        Assert.That(terrain.Icon.Type, Is.EqualTo(FileType.GuiSkinItem));
        Assert.That(terrain.Icon.Name, Is.Not.Empty);
        Assert.That(terrain.Texture.Type, Is.EqualTo(FileType.Texture));
        Assert.That(terrain.Texture.Name, Is.Not.Empty);
        Assert.That(
          ovl.Keys.Any(file => file.Type == FileType.Texture && file.Name == terrain.Texture.Name),
          Is.True,
          $"Missing texture resource {terrain.Texture.QualifiedName} for {terrain.Name}");
      }
    }
  }
}
