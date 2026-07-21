// Rain Audio Playback
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using NLog;

namespace OpenRCT3.Audio;

/// <summary>Resolves the explicit process-level opt-in for installed rain playback.</summary>
internal static class RainAudioPlaybackOptions {
  internal const string EnvironmentVariable = "OPENRCT3_PLAY_RAIN_AUDIO";

  public static bool Enabled => ShouldPlay(
    Environment.GetEnvironmentVariable(EnvironmentVariable));

  internal static bool ShouldPlay(string? value) =>
    string.Equals(value, "1", StringComparison.Ordinal);
}

/// <summary>Owns the exact installed rain asset and its platform playback lifecycle.</summary>
internal sealed class RainAudioPlayback : IDisposable {
  private readonly object gate = new();
  private readonly IRainAudioAssetLoader loader;
  private readonly ILoopingWavePlayer player;
  private readonly Action<string> logInfo;
  private readonly Action<Exception, string> logWarning;
  private RainAudioAsset? activeAsset;
  private bool started;
  private bool disposed;

  internal RainAudioPlayback(
    IRainAudioAssetLoader loader,
    ILoopingWavePlayer player,
    Action<string>? logInfo = null,
    Action<Exception, string>? logWarning = null
  ) {
    ArgumentNullException.ThrowIfNull(loader);
    ArgumentNullException.ThrowIfNull(player);
    this.loader = loader;
    this.player = player;
    this.logInfo = logInfo ?? (_ => { });
    this.logWarning = logWarning ?? ((_, _) => { });
  }

  public static RainAudioPlayback CreateDefault(Logger logger) {
    ArgumentNullException.ThrowIfNull(logger);
    return new RainAudioPlayback(
      new InstalledRainAudioLoader(),
      LoopingWavePlayerFactory.Create(),
      message => logger.Info(message),
      (error, message) => logger.Warn(error, message));
  }

  internal bool IsStarted {
    get {
      lock (gate) return started;
    }
  }

  /// <summary>Starts once, or records one additive failure without disrupting game startup.</summary>
  public bool TryStart(string installRoot) {
    lock (gate) {
      if (disposed) return false;
      if (started) return true;

      try {
        var asset = loader.Load(installRoot);
        if (!player.TryStartLoop(asset.WaveBytes, out var unavailableReason))
          throw new InvalidOperationException(
            unavailableReason ?? "The looping-WAV backend rejected the installed rain asset.");
        activeAsset = asset;
        started = true;
        logInfo(
          $"Started looping installed rain audio {asset.ResourceIdentity} from " +
          $"{asset.ArchivePath}");
        return true;
      }
      catch (Exception error) {
        if (started) StopCore();
        Warn(error, "Installed rain audio could not be started");
        return false;
      }
    }
  }

  public void Stop() {
    lock (gate) {
      if (disposed || !started) return;
      StopCore();
    }
  }

  public void Dispose() {
    lock (gate) {
      if (disposed) return;
      if (started) StopCore();
      try {
        player.Dispose();
      }
      catch (Exception error) {
        Warn(error, "Installed rain audio player could not be disposed cleanly");
      }
      disposed = true;
    }
  }

  private void StopCore() {
    var asset = activeAsset;
    try {
      player.Stop();
      if (asset != null)
        logInfo($"Stopped looping installed rain audio {asset.ResourceIdentity}");
    }
    catch (Exception error) {
      Warn(error, "Installed rain audio could not be stopped cleanly");
    }
    finally {
      activeAsset = null;
      started = false;
    }
  }

  private void Warn(Exception error, string message) {
    try {
      logWarning(error, message);
    }
    catch {
      // Optional audio logging cannot become a second startup or shutdown failure.
    }
  }
}
