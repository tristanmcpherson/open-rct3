using OpenRCT3.Platforms;

namespace OpenRCT3.Tests.Platforms;

[TestFixture]
[NonParallelizable]
public class LaunchOptionsTests {
  private const string MapPathEnvironmentVariable = "OPENRCT3_MAP_PATH";

  [TestCase("C:\\Parks\\Box Office.dat")]
  [TestCase("relative/VanillaHills.dat")]
  public void Parse_MapOptionWithSeparateValueSelectsPath(string path) {
    var options = LaunchOptions.Parse(["--map", path], allowPositionalDat: false);

    Assert.That(options.MapPath, Is.EqualTo(path));
  }

  [Test]
  public void Parse_MapOptionWithEqualsSelectsTheCompleteValue() {
    var options = LaunchOptions.Parse(
      ["--map=C:\\Parks\\Box=Office.dat"],
      allowPositionalDat: false);

    Assert.That(options.MapPath, Is.EqualTo("C:\\Parks\\Box=Office.dat"));
  }

  [TestCase("C:\\Parks\\BoxOffice.dat")]
  [TestCase("C:\\Parks\\BoxOffice.DAT")]
  public void Parse_WindowsPositionalDatSelectsDragDropPath(string path) {
    var options = LaunchOptions.Parse([path], allowPositionalDat: true);

    Assert.That(options.MapPath, Is.EqualTo(path));
  }

  [Test]
  public void Parse_PositionalDatWithOtherArgumentsIsIgnored() {
    var options = LaunchOptions.Parse(
      ["--launcher-flag", "C:\\Parks\\BoxOffice.dat"],
      allowPositionalDat: true);

    Assert.That(options.MapPath, Is.Null);
  }

  [Test]
  public void Parse_PositionalDatIsIgnoredWhenPlatformDisallowsIt() {
    var options = LaunchOptions.Parse(
      ["-psn_0_12345", "C:\\Parks\\BoxOffice.dat"],
      allowPositionalDat: false);

    Assert.That(options.MapPath, Is.Null);
  }

  [Test]
  public void Parse_UnrelatedLauncherFlagsDoNotAffectExplicitMap() {
    var options = LaunchOptions.Parse(
      ["-psn_0_12345", "--launcher-flag", "--map", "park.dat"],
      allowPositionalDat: false);

    Assert.That(options.MapPath, Is.EqualTo("park.dat"));
  }

  [TestCaseSource(nameof(MissingMapArguments))]
  public void Parse_MissingMapValueFailsClearly(string[] arguments) {
    var error = Assert.Throws<ArgumentException>(new Action(() =>
      LaunchOptions.Parse(arguments, allowPositionalDat: false)));

    Assert.That(error!.Message, Is.EqualTo("Option '--map' requires a non-empty map path."));
  }

  [TestCaseSource(nameof(DuplicateMapArguments))]
  public void Parse_DuplicateMapSelectionFailsClearly(
    string[] arguments,
    bool allowPositionalDat
  ) {
    var error = Assert.Throws<ArgumentException>(new Action(() =>
      LaunchOptions.Parse(arguments, allowPositionalDat)));

    Assert.That(error!.Message, Is.EqualTo("The map path was specified more than once."));
  }

  [Test]
  public void Apply_NoMapPreservesExistingProcessOverride() {
    var original = Environment.GetEnvironmentVariable(MapPathEnvironmentVariable);
    var options = LaunchOptions.Parse([], allowPositionalDat: true);

    options.Apply();

    Assert.That(
      Environment.GetEnvironmentVariable(MapPathEnvironmentVariable),
      Is.EqualTo(original)
    );
  }

  [Test]
  public void Apply_SelectedMapUsesProcessOverrideWithoutMutatingConfig() {
    var original = Environment.GetEnvironmentVariable(MapPathEnvironmentVariable);
    var config = new AppConfig { MapPath = "configured.dat" };
    var options = LaunchOptions.Parse(
      ["--map", "one-shot.dat"],
      allowPositionalDat: false);

    try {
      options.Apply();

      Assert.Multiple(new Action(() => {
        Assert.That(config.MapPath, Is.EqualTo("configured.dat"));
        Assert.That(
          Environment.GetEnvironmentVariable(MapPathEnvironmentVariable),
          Is.EqualTo("one-shot.dat")
        );
      }));
    } finally {
      Environment.SetEnvironmentVariable(MapPathEnvironmentVariable, original);
    }
  }

  private static IEnumerable<string[]> MissingMapArguments() {
    yield return ["--map"];
    yield return ["--map", ""];
    yield return ["--map", "   "];
    yield return ["--map", "--other-option"];
    yield return ["--map="];
    yield return ["--map=   "];
  }

  private static IEnumerable<object[]> DuplicateMapArguments() {
    yield return [new[] { "--map", "first.dat", "--map=second.dat" }, false];
    yield return [new[] { "--map", "first.dat", "--map", "second.dat" }, true];
  }
}
