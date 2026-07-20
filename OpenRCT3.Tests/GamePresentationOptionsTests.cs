// GamePresentationOptionsTests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Tests;

[TestFixture]
public class GamePresentationOptionsTests {
  [TestCase(null, true)]
  [TestCase("", true)]
  [TestCase("0", true)]
  [TestCase("true", true)]
  [TestCase("1", false)]
  public void ShouldShowUserInterface_OnlyHidesForExplicitOne(string? value, bool expected) {
    Assert.That(GamePresentationOptions.ShouldShowUserInterface(value), Is.EqualTo(expected));
  }

  [TestCase(null, false)]
  [TestCase("", false)]
  [TestCase("0", false)]
  [TestCase("true", false)]
  [TestCase("1", true)]
  public void ShouldShowRideTrackDiagnostics_OnlyEnablesForExplicitOne(
    string? value,
    bool expected
  ) {
    Assert.That(
      GamePresentationOptions.ShouldShowRideTrackDiagnostics(value),
      Is.EqualTo(expected));
  }
}
