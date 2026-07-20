// Game Presentation Options
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3;

/// <summary>One deterministic initial camera target after normal terrain framing.</summary>
internal enum GameDiagnosticCameraTarget {
  Terrain,
  RideTrackGeometry,
  RideCars,
  WildAnimals,
}

/// <summary>Resolves process-level presentation choices used by native visual verification.</summary>
internal static class GamePresentationOptions {
  internal const string HideUiEnvironmentVariable = "OPENRCT3_HIDE_UI";
  internal const string ShowRideTrackDiagnosticsEnvironmentVariable =
    "OPENRCT3_SHOW_RIDE_TRACK_DIAGNOSTICS";
  internal const string ShowWildAnimalDiagnosticsEnvironmentVariable =
    "OPENRCT3_SHOW_WILD_ANIMAL_DIAGNOSTICS";

  public static bool ShowUserInterface => ShouldShowUserInterface(
    Environment.GetEnvironmentVariable(HideUiEnvironmentVariable));

  public static bool ShowRideTrackDiagnostics => ShouldShowRideTrackDiagnostics(
    Environment.GetEnvironmentVariable(ShowRideTrackDiagnosticsEnvironmentVariable));

  public static bool ShowWildAnimalDiagnostics => ShouldShowWildAnimalDiagnostics(
    Environment.GetEnvironmentVariable(ShowWildAnimalDiagnosticsEnvironmentVariable));

  internal static bool ShouldShowUserInterface(string? hideUi)
    => !string.Equals(hideUi, "1", StringComparison.Ordinal);

  internal static bool ShouldShowRideTrackDiagnostics(string? showDiagnostics)
    => string.Equals(showDiagnostics, "1", StringComparison.Ordinal);

  internal static bool ShouldShowWildAnimalDiagnostics(string? showDiagnostics)
    => string.Equals(showDiagnostics, "1", StringComparison.Ordinal);

  /// <summary>
  /// Keeps existing ride diagnostic precedence, then uses animals only when explicitly requested.
  /// </summary>
  internal static GameDiagnosticCameraTarget SelectDiagnosticCameraTarget(
    bool showRideTrackDiagnostics,
    bool showWildAnimalDiagnostics,
    bool hasRideTrackGeometryFrame,
    bool hasRideCarFrame,
    bool hasWildAnimalFrame
  ) {
    if (showRideTrackDiagnostics && hasRideCarFrame)
      return GameDiagnosticCameraTarget.RideCars;
    if (showRideTrackDiagnostics && hasRideTrackGeometryFrame)
      return GameDiagnosticCameraTarget.RideTrackGeometry;
    if (showWildAnimalDiagnostics && hasWildAnimalFrame)
      return GameDiagnosticCameraTarget.WildAnimals;
    return GameDiagnosticCameraTarget.Terrain;
  }
}
