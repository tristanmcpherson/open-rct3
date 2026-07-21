using OpenCobra.OVL.Files;
using OpenRCT3.Audio;

namespace OpenRCT3.Tests.Audio;

[TestFixture]
public class InstalledRainAudioLoaderTests {
  private string installRoot = null!;

  [SetUp]
  public void SetUp() {
    installRoot = Path.Combine(
      Path.GetTempPath(),
      $"OpenRCT3-RainAudio-{Guid.NewGuid():N}");
    Directory.CreateDirectory(installRoot);
  }

  [TearDown]
  public void TearDown() {
    if (Directory.Exists(installRoot)) Directory.Delete(installRoot, true);
  }

  [Test]
  public void Load_UsesOneExactLoopingRain1Resource() {
    AddInstalledArchive();
    var source = new FakeInstalledSoundSource([CreateSound("Rain1", 1)]);
    var loader = new InstalledRainAudioLoader(source);

    var asset = loader.Load(installRoot);

    using (Assert.EnterMultipleScope()) {
      Assert.That(source.ReadPaths, Is.EqualTo(new[] {
        Path.Combine(Path.GetFullPath(installRoot), InstalledRainAudioLoader.ArchiveFileName)
      }));
      Assert.That(asset.ArchivePath, Is.EqualTo(source.ReadPaths[0]));
      Assert.That(asset.ResourceIdentity, Is.EqualTo("Rain1:snd"));
      Assert.That(asset.WaveBytes.AsSpan(0, 4).ToArray(), Is.EqualTo("RIFF"u8.ToArray()));
      Assert.That(asset.WaveBytes.AsSpan(8, 4).ToArray(), Is.EqualTo("WAVE"u8.ToArray()));
    }
  }

  [Test]
  public void Load_MissingArchiveDoesNotReadSource() {
    var source = new FakeInstalledSoundSource([CreateSound("Rain1", 1)]);
    var loader = new InstalledRainAudioLoader(source);

    Assert.Throws<FileNotFoundException>(new Action(() => loader.Load(installRoot)));
    Assert.That(source.ReadPaths, Is.Empty);
  }

  [Test]
  public void Load_DuplicateExactResourcesFailClosed() {
    AddInstalledArchive();
    var loader = new InstalledRainAudioLoader(new FakeInstalledSoundSource([
      CreateSound("Rain1", 1),
      CreateSound("Rain1", 1)
    ]));

    var error = Assert.Throws<InvalidDataException>(new Action(() => loader.Load(installRoot)));

    Assert.That(error?.Message, Does.Contain("found 2"));
  }

  [Test]
  public void Load_NonLoopingExactResourceFailsClosed() {
    AddInstalledArchive();
    var loader = new InstalledRainAudioLoader(
      new FakeInstalledSoundSource([CreateSound("Rain1", 0)]));

    var error = Assert.Throws<InvalidDataException>(new Action(() => loader.Load(installRoot)));

    Assert.That(error?.Message, Does.Contain("does not authorize looping"));
  }

  [Test]
  public void Load_WaveOverConfiguredLimitFailsClosed() {
    AddInstalledArchive();
    var loader = new InstalledRainAudioLoader(
      new FakeInstalledSoundSource([CreateSound("Rain1", 1)]),
      45);

    var error = Assert.Throws<InvalidDataException>(new Action(() => loader.Load(installRoot)));

    Assert.That(error?.Message, Does.Contain("playback limit is 45"));
  }

  private void AddInstalledArchive() => File.WriteAllBytes(
    Path.Combine(installRoot, InstalledRainAudioLoader.ArchiveFileName),
    []);

  private static Sound CreateSound(string name, int loopFlag) => new(
    name,
    1,
    1,
    22_050,
    44_100,
    2,
    16,
    loopFlag,
    [1, 0],
    []);

  private sealed class FakeInstalledSoundSource(IReadOnlyList<Sound> sounds)
    : IInstalledSoundSource {
    public List<string> ReadPaths { get; } = [];

    public IReadOnlyList<Sound> Read(string commonPath) {
      ReadPaths.Add(commonPath);
      return sounds;
    }
  }
}
