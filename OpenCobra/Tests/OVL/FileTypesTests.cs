using NUnit.Framework;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class FileTypesTests {
  [TestCase("was", FileType.WildAnimalSpecies, "Wild Animal Species")]
  [TestCase("mdl", FileType.Model, "Model")]
  [TestCase("wad", FileType.WildAnimalAnimData, "Wild Animal Animation Data")]
  [TestCase("modelanim", FileType.ModelAnim, "Model Animation")]
  public void SafariResourceTag_RoundTrips(
    string tag,
    FileType expectedType,
    string expectedDisplayName
  ) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(tag.ToFileType(), Is.EqualTo(expectedType));
      Assert.That(expectedType.ToTagString(), Is.EqualTo(tag));
      Assert.That(expectedType.ToTagString(asExtension: true), Is.EqualTo($".{tag}"));
      Assert.That(expectedType.ToDisplayName(), Is.EqualTo(expectedDisplayName));
    }
  }
}
