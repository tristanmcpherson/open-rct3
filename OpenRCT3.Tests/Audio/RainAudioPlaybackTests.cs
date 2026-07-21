using OpenRCT3.Audio;

namespace OpenRCT3.Tests.Audio;

[TestFixture]
public class RainAudioPlaybackTests {
  [TestCase(null, false)]
  [TestCase("", false)]
  [TestCase("0", false)]
  [TestCase("true", false)]
  [TestCase("01", false)]
  [TestCase("1 ", false)]
  [TestCase("1", true)]
  public void OptIn_RequiresExactOne(string? value, bool expected) {
    Assert.That(RainAudioPlaybackOptions.ShouldPlay(value), Is.EqualTo(expected));
  }

  [Test]
  public void Lifecycle_StartsOnceThenStopsAndDisposesOnce() {
    var loader = new FakeRainAudioAssetLoader();
    var player = new FakeLoopingWavePlayer();
    var messages = new List<string>();
    var playback = new RainAudioPlayback(loader, player, messages.Add);

    var firstStart = playback.TryStart("C:\\RCT3");
    var secondStart = playback.TryStart("C:\\RCT3");

    using (Assert.EnterMultipleScope()) {
      Assert.That(firstStart, Is.True);
      Assert.That(secondStart, Is.True);
      Assert.That(playback.IsStarted, Is.True);
      Assert.That(loader.LoadCalls, Is.EqualTo(1));
      Assert.That(player.StartCalls, Is.EqualTo(1));
      Assert.That(messages, Has.Count.EqualTo(1));
    }

    playback.Dispose();
    playback.Dispose();

    using (Assert.EnterMultipleScope()) {
      Assert.That(playback.IsStarted, Is.False);
      Assert.That(player.StopCalls, Is.EqualTo(1));
      Assert.That(player.DisposeCalls, Is.EqualTo(1));
      Assert.That(playback.TryStart("C:\\RCT3"), Is.False);
    }
  }

  [Test]
  public void TryStart_BackendUnavailableIsAdditiveAndLeavesGameStartupAvailable() {
    var loader = new FakeRainAudioAssetLoader();
    var player = new FakeLoopingWavePlayer {
      StartResult = false,
      UnavailableReason = "Injected unsupported platform."
    };
    var warnings = new List<(Exception Error, string Message)>();
    using var playback = new RainAudioPlayback(
      loader,
      player,
      logWarning: (error, message) => warnings.Add((error, message)));

    var started = playback.TryStart("C:\\RCT3");

    using (Assert.EnterMultipleScope()) {
      Assert.That(started, Is.False);
      Assert.That(playback.IsStarted, Is.False);
      Assert.That(loader.LoadCalls, Is.EqualTo(1));
      Assert.That(player.StartCalls, Is.EqualTo(1));
      Assert.That(player.StopCalls, Is.Zero);
      Assert.That(warnings, Has.Count.EqualTo(1));
      Assert.That(warnings[0].Error.Message, Is.EqualTo("Injected unsupported platform."));
      Assert.That(warnings[0].Message, Does.Contain("could not be started"));
    }
  }

  [Test]
  public void TryStart_LoaderFailureDoesNotReachPlayer() {
    var primaryError = new InvalidDataException("Injected installed asset failure.");
    var loader = new FakeRainAudioAssetLoader { LoadError = primaryError };
    var player = new FakeLoopingWavePlayer();
    var warnings = new List<Exception>();
    using var playback = new RainAudioPlayback(
      loader,
      player,
      logWarning: (error, _) => warnings.Add(error));

    var started = playback.TryStart("C:\\RCT3");

    using (Assert.EnterMultipleScope()) {
      Assert.That(started, Is.False);
      Assert.That(player.StartCalls, Is.Zero);
      Assert.That(warnings, Is.EqualTo(new[] { primaryError }));
    }
  }

  [Test]
  public void UnsupportedPlayer_IsExplicitNoOp() {
    using var player = new UnsupportedLoopingWavePlayer();

    var started = player.TryStartLoop([1, 2, 3], out var unavailableReason);
    player.Stop();

    using (Assert.EnterMultipleScope()) {
      Assert.That(started, Is.False);
      Assert.That(unavailableReason, Does.Contain("unavailable on this platform"));
    }
  }

  private sealed class FakeRainAudioAssetLoader : IRainAudioAssetLoader {
    public Exception? LoadError { get; init; }
    public int LoadCalls { get; private set; }

    public RainAudioAsset Load(string installRoot) {
      LoadCalls++;
      if (LoadError != null) throw LoadError;
      return new RainAudioAsset(
        Path.Combine(installRoot, InstalledRainAudioLoader.ArchiveFileName),
        InstalledRainAudioLoader.ResourceIdentity,
        [1, 2, 3]);
    }
  }

  private sealed class FakeLoopingWavePlayer : ILoopingWavePlayer {
    public bool StartResult { get; init; } = true;
    public string? UnavailableReason { get; init; }
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }
    public int DisposeCalls { get; private set; }

    public bool TryStartLoop(byte[] waveBytes, out string? unavailableReason) {
      StartCalls++;
      unavailableReason = UnavailableReason;
      return StartResult;
    }

    public void Stop() => StopCalls++;

    public void Dispose() => DisposeCalls++;
  }
}
