// Launch Options
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;

namespace OpenRCT3.Platforms;

/// <summary>One-shot process launch options that are never written to app config.</summary>
internal sealed record LaunchOptions(string? MapPath) {
  private const string MapOption = "--map";
  private const string MapPathEnvironmentVariable = "OPENRCT3_MAP_PATH";

  public static LaunchOptions Parse(
    IReadOnlyList<string> arguments,
    bool allowPositionalDat
  ) {
    ArgumentNullException.ThrowIfNull(arguments);

    string? mapPath = null;
    var hasMap = false;
    for (var index = 0; index < arguments.Count; index++) {
      var argument = arguments[index]
        ?? throw new ArgumentException("Launch arguments cannot contain null values.");
      if (string.Equals(argument, MapOption, StringComparison.Ordinal)) {
        if (index + 1 >= arguments.Count ||
            string.IsNullOrWhiteSpace(arguments[index + 1]) ||
            arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
          throw MissingMapPath();
        SelectMap(arguments[++index]);
        continue;
      }
      if (argument.StartsWith($"{MapOption}=", StringComparison.Ordinal)) {
        var value = argument[(MapOption.Length + 1)..];
        if (string.IsNullOrWhiteSpace(value)) throw MissingMapPath();
        SelectMap(value);
        continue;
      }
      if (allowPositionalDat && arguments.Count == 1 &&
          argument.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
        SelectMap(argument);
      // Platform launchers may append unrelated flags such as macOS -psn_*.
    }
    return new LaunchOptions(mapPath);

    void SelectMap(string path) {
      if (hasMap)
        throw new ArgumentException("The map path was specified more than once.");
      hasMap = true;
      mapPath = path;
    }
  }

  /// <summary>
  /// Applies explicit launch state to this process only. Terrain already treats this environment
  /// override as higher priority than app config, and app config saves cannot persist it.
  /// </summary>
  public void Apply() {
    if (MapPath != null)
      Environment.SetEnvironmentVariable(MapPathEnvironmentVariable, MapPath);
  }

  private static ArgumentException MissingMapPath() =>
    new("Option '--map' requires a non-empty map path.");
}
