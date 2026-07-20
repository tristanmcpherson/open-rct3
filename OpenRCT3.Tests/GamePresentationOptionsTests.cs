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

  [TestCase(null, false)]
  [TestCase("", false)]
  [TestCase("0", false)]
  [TestCase("true", false)]
  [TestCase("1", true)]
  public void ShouldShowWildAnimalDiagnostics_OnlyEnablesForExplicitOne(
    string? value,
    bool expected
  ) {
    Assert.That(
      GamePresentationOptions.ShouldShowWildAnimalDiagnostics(value),
      Is.EqualTo(expected));
  }

  [TestCase(false, false, true, true, true, "Terrain")]
  [TestCase(true, false, true, true, true, "RideCars")]
  [TestCase(true, false, false, true, true, "RideCars")]
  [TestCase(true, false, true, false, true, "RideTrackGeometry")]
  [TestCase(false, true, true, true, true, "WildAnimals")]
  [TestCase(true, true, true, true, true, "RideCars")]
  [TestCase(true, true, false, true, true, "RideCars")]
  [TestCase(true, true, false, false, true, "WildAnimals")]
  [TestCase(true, false, false, false, true, "Terrain")]
  public void SelectDiagnosticCameraTarget_PreservesRidePrecedenceThenUsesAnimalFallback(
    bool showRideTrackDiagnostics,
    bool showWildAnimalDiagnostics,
    bool hasRideTrackGeometryFrame,
    bool hasRideCarFrame,
    bool hasWildAnimalFrame,
    string expected
  ) {
    Assert.That(
      GamePresentationOptions.SelectDiagnosticCameraTarget(
        showRideTrackDiagnostics,
        showWildAnimalDiagnostics,
        hasRideTrackGeometryFrame,
        hasRideCarFrame,
        hasWildAnimalFrame),
      Is.EqualTo(Enum.Parse<GameDiagnosticCameraTarget>(expected)));
  }
}
