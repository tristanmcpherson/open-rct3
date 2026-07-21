using OpenCobra.OVL.Files;
using OpenRCT3.Audio;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace OpenRCT3.Tests.Audio;

[TestFixture]
public class WinMmLoopingWavePlayerTests {
  [Test]
  public void TryStartLoop_OwnsPinnedCopyUntilStopReturns() {
    var api = new FakeWinMmPlaySoundApi();
    using var player = new WinMmLoopingWavePlayer(api);
    var waveBytes = CreateWave();
    var expected = waveBytes.ToArray();

    var started = player.TryStartLoop(waveBytes, out var unavailableReason);
    waveBytes.AsSpan().Fill(0);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var nativeSnapshot = new byte[expected.Length];
    Marshal.Copy(api.WaveAddress, nativeSnapshot, 0, nativeSnapshot.Length);

    using (Assert.EnterMultipleScope()) {
      Assert.That(started, Is.True);
      Assert.That(unavailableReason, Is.Null);
      Assert.That(api.StartCalls, Is.EqualTo(1));
      Assert.That(nativeSnapshot, Is.EqualTo(expected));
      Assert.That(player.OwnsPinnedBuffer, Is.True);
    }

    player.Stop();

    using (Assert.EnterMultipleScope()) {
      Assert.That(api.StopCalls, Is.EqualTo(1));
      Assert.That(player.OwnsPinnedBuffer, Is.False);
    }
  }

  [Test]
  public void TryStartLoop_NativeRejectionReleasesPinnedCopy() {
    var api = new FakeWinMmPlaySoundApi { StartResult = false };
    using var player = new WinMmLoopingWavePlayer(api);

    var started = player.TryStartLoop(CreateWave(), out var unavailableReason);

    using (Assert.EnterMultipleScope()) {
      Assert.That(started, Is.False);
      Assert.That(unavailableReason, Does.Contain("rejected"));
      Assert.That(api.StartCalls, Is.EqualTo(1));
      Assert.That(player.OwnsPinnedBuffer, Is.False);
    }
  }

  [Test]
  public void TryStartLoop_NativeFailureReleasesPinnedCopy() {
    var api = new FakeWinMmPlaySoundApi {
      StartError = new InvalidOperationException("Injected WinMM failure.")
    };
    using var player = new WinMmLoopingWavePlayer(api);

    Assert.Throws<InvalidOperationException>(new Action(() =>
      player.TryStartLoop(CreateWave(), out _)));

    Assert.That(player.OwnsPinnedBuffer, Is.False);
  }

  [Test]
  public void TryStartLoop_RejectsOutOfBoundsRiffSizeBeforeNativeCall() {
    var api = new FakeWinMmPlaySoundApi();
    using var player = new WinMmLoopingWavePlayer(api);
    var waveBytes = CreateWave();
    BinaryPrimitives.WriteUInt32LittleEndian(waveBytes.AsSpan(4), uint.MaxValue);

    var started = player.TryStartLoop(waveBytes, out var unavailableReason);

    using (Assert.EnterMultipleScope()) {
      Assert.That(started, Is.False);
      Assert.That(unavailableReason, Does.Contain("canonical PCM"));
      Assert.That(api.StartCalls, Is.Zero);
      Assert.That(player.OwnsPinnedBuffer, Is.False);
    }
  }

  [Test]
  public void Dispose_StopsActiveLoopAndCannotBeRepeated() {
    var api = new FakeWinMmPlaySoundApi();
    var player = new WinMmLoopingWavePlayer(api);
    Assert.That(player.TryStartLoop(CreateWave(), out _), Is.True);

    player.Dispose();
    player.Dispose();

    using (Assert.EnterMultipleScope()) {
      Assert.That(api.StopCalls, Is.EqualTo(1));
      Assert.That(player.OwnsPinnedBuffer, Is.False);
    }
    Assert.Throws<ObjectDisposedException>(new Action(() =>
      player.TryStartLoop(CreateWave(), out _)));
  }

  private static byte[] CreateWave() => new Sound(
    "Rain1",
    1,
    1,
    22_050,
    44_100,
    2,
    16,
    1,
    [1, 0, 2, 0],
    []).ToWaveBytes();

  private sealed class FakeWinMmPlaySoundApi : IWinMmPlaySoundApi {
    public bool StartResult { get; init; } = true;
    public Exception? StartError { get; init; }
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }
    public nint WaveAddress { get; private set; }

    public bool TryStartLoop(nint waveAddress) {
      StartCalls++;
      WaveAddress = waveAddress;
      if (StartError != null) throw StartError;
      return StartResult;
    }

    public void Stop() => StopCalls++;
  }
}
