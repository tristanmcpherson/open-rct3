// Looping Wave Player
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Audio;

/// <summary>Owns asynchronous playback of one in-memory WAV payload.</summary>
internal interface ILoopingWavePlayer : IDisposable {
  /// <summary>
  /// Starts looping one WAV image. A successful player owns any native buffer until
  /// <see cref="Stop"/> or <see cref="IDisposable.Dispose"/> returns.
  /// </summary>
  bool TryStartLoop(byte[] waveBytes, out string? unavailableReason);

  /// <summary>Stops the active loop and releases its native buffer.</summary>
  void Stop();
}

internal static class LoopingWavePlayerFactory {
  public static ILoopingWavePlayer Create() {
#if WINDOWS
    return new WinMmLoopingWavePlayer();
#else
    return new UnsupportedLoopingWavePlayer();
#endif
  }
}

/// <summary>Explicit no-op for platforms without an owned looping-WAV backend.</summary>
internal sealed class UnsupportedLoopingWavePlayer : ILoopingWavePlayer {
  public bool TryStartLoop(byte[] waveBytes, out string? unavailableReason) {
    ArgumentNullException.ThrowIfNull(waveBytes);
    unavailableReason = "Looping in-memory WAV playback is unavailable on this platform.";
    return false;
  }

  public void Stop() { }

  public void Dispose() { }
}
