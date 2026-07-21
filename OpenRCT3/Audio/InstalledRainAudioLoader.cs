// Installed Rain Audio Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Audio;

internal sealed record RainAudioAsset(
  string ArchivePath,
  string ResourceIdentity,
  byte[] WaveBytes
);

internal interface IRainAudioAssetLoader {
  RainAudioAsset Load(string installRoot);
}

internal interface IInstalledSoundSource {
  IReadOnlyList<Sound> Read(string commonPath);
}

/// <summary>Loads one exact, loop-authorized installed <c>Rain1:snd</c> WAV payload.</summary>
internal sealed class InstalledRainAudioLoader : IRainAudioAssetLoader {
  internal const string ArchiveFileName = "Main.common.ovl";
  internal const string ResourceName = "Rain1";
  internal const string ResourceIdentity = "Rain1:snd";
  internal const int DefaultMaximumWaveBytes = 16 * 1024 * 1024;

  private readonly IInstalledSoundSource source;
  private readonly int maximumWaveBytes;

  public InstalledRainAudioLoader()
    : this(new OvlInstalledSoundSource(), DefaultMaximumWaveBytes) { }

  internal InstalledRainAudioLoader(
    IInstalledSoundSource source,
    int maximumWaveBytes = DefaultMaximumWaveBytes
  ) {
    ArgumentNullException.ThrowIfNull(source);
    if (maximumWaveBytes < 44)
      throw new ArgumentOutOfRangeException(
        nameof(maximumWaveBytes),
        maximumWaveBytes,
        "The WAV limit must fit a canonical PCM header.");
    this.source = source;
    this.maximumWaveBytes = maximumWaveBytes;
  }

  public RainAudioAsset Load(string installRoot) {
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    var root = Path.GetFullPath(installRoot);
    var archivePath = Path.Combine(root, ArchiveFileName);
    if (!File.Exists(archivePath))
      throw new FileNotFoundException(
        $"Installed rain archive is missing: {archivePath}", archivePath);

    var sounds = source.Read(archivePath)
      ?? throw new InvalidDataException("Installed sound source returned no resource list.");
    var matches = sounds.Where(sound => string.Equals(
      sound.Name, ResourceName, StringComparison.Ordinal)).ToArray();
    if (matches.Length != 1)
      throw new InvalidDataException(
        $"Expected one exact {ResourceIdentity} in {archivePath}, found {matches.Length}.");

    var sound = matches[0];
    if (!sound.Loops)
      throw new InvalidDataException(
        $"Installed {ResourceIdentity} does not authorize looping playback.");
    var projectedWaveBytes = Convert.ToUInt64(44) +
      Convert.ToUInt64(sound.Channel1.Length) +
      Convert.ToUInt64(sound.Channel2.Length);
    if (projectedWaveBytes > Convert.ToUInt64(maximumWaveBytes))
      throw new InvalidDataException(
        $"Installed {ResourceIdentity} WAV requires {projectedWaveBytes} bytes; " +
        $"the playback limit is {maximumWaveBytes}.");
    var waveBytes = sound.ToWaveBytes();
    if (waveBytes.Length > maximumWaveBytes)
      throw new InvalidDataException(
        $"Installed {ResourceIdentity} WAV is {waveBytes.Length} bytes; " +
        $"the playback limit is {maximumWaveBytes}.");
    return new RainAudioAsset(archivePath, ResourceIdentity, waveBytes);
  }

  private sealed class OvlInstalledSoundSource : IInstalledSoundSource {
    public IReadOnlyList<Sound> Read(string commonPath) {
      using var archive = Ovl.Load(commonPath);
      return Sounds.Extract(archive);
    }
  }
}
