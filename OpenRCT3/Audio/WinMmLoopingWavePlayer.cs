// WinMM Looping Wave Player
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Buffers.Binary;
using System.Linq;
using System.Runtime.InteropServices;

namespace OpenRCT3.Audio;

internal interface IWinMmPlaySoundApi {
  bool TryStartLoop(nint waveAddress);
  void Stop();
}

/// <summary>Keeps an owned WAV image pinned for WinMM's asynchronous looping lifetime.</summary>
internal sealed class WinMmLoopingWavePlayer : ILoopingWavePlayer {
  private readonly object gate = new();
  private readonly IWinMmPlaySoundApi api;
  private byte[]? ownedWaveBytes;
  private GCHandle pinnedWaveBytes;
  private bool playing;
  private bool disposed;

  public WinMmLoopingWavePlayer() : this(new WinMmPlaySoundApi()) { }

  internal WinMmLoopingWavePlayer(IWinMmPlaySoundApi api) {
    ArgumentNullException.ThrowIfNull(api);
    this.api = api;
  }

  internal bool OwnsPinnedBuffer {
    get {
      lock (gate) return pinnedWaveBytes.IsAllocated;
    }
  }

  public bool TryStartLoop(byte[] waveBytes, out string? unavailableReason) {
    ArgumentNullException.ThrowIfNull(waveBytes);
    lock (gate) {
      ObjectDisposedException.ThrowIf(disposed, this);
      var candidate = waveBytes.ToArray();
      if (!HasCanonicalWaveLayout(candidate)) {
        unavailableReason = "The playback buffer is not a canonical PCM RIFF/WAVE image.";
        return false;
      }
      if (playing) StopCore();

      ownedWaveBytes = candidate;
      pinnedWaveBytes = GCHandle.Alloc(ownedWaveBytes, GCHandleType.Pinned);
      try {
        if (api.TryStartLoop(pinnedWaveBytes.AddrOfPinnedObject())) {
          playing = true;
          unavailableReason = null;
          return true;
        }
        unavailableReason = "WinMM PlaySound rejected the in-memory WAV image.";
        ReleaseBuffer();
        return false;
      }
      catch {
        ReleaseBuffer();
        throw;
      }
    }
  }

  public void Stop() {
    lock (gate) {
      if (disposed || !playing) return;
      StopCore();
    }
  }

  public void Dispose() {
    lock (gate) {
      if (disposed) return;
      try {
        if (playing) StopCore();
      }
      finally {
        ReleaseBuffer();
        disposed = true;
      }
    }
  }

  private void StopCore() {
    try {
      api.Stop();
    }
    finally {
      playing = false;
      ReleaseBuffer();
    }
  }

  private void ReleaseBuffer() {
    if (pinnedWaveBytes.IsAllocated) pinnedWaveBytes.Free();
    ownedWaveBytes = null;
  }

  private static bool HasCanonicalWaveLayout(byte[] waveBytes) {
    const int headerSize = 44;
    const ushort pcmFormat = 1;
    const ushort bitsPerSample = 16;
    if (waveBytes.Length < headerSize ||
        !waveBytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
        !waveBytes.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8) ||
        !waveBytes.AsSpan(36, 4).SequenceEqual("data"u8)) return false;

    var expectedRiffSize = Convert.ToUInt64(waveBytes.Length - 8);
    var expectedDataSize = Convert.ToUInt64(waveBytes.Length - headerSize);
    if (BinaryPrimitives.ReadUInt32LittleEndian(waveBytes.AsSpan(4)) != expectedRiffSize ||
        BinaryPrimitives.ReadUInt32LittleEndian(waveBytes.AsSpan(16)) != 16 ||
        BinaryPrimitives.ReadUInt16LittleEndian(waveBytes.AsSpan(20)) != pcmFormat ||
        BinaryPrimitives.ReadUInt16LittleEndian(waveBytes.AsSpan(34)) != bitsPerSample ||
        BinaryPrimitives.ReadUInt32LittleEndian(waveBytes.AsSpan(40)) != expectedDataSize)
      return false;

    var channelCount = BinaryPrimitives.ReadUInt16LittleEndian(waveBytes.AsSpan(22));
    var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(waveBytes.AsSpan(24));
    var byteRate = BinaryPrimitives.ReadUInt32LittleEndian(waveBytes.AsSpan(28));
    var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(waveBytes.AsSpan(32));
    var expectedBlockAlign = Convert.ToUInt32(channelCount) * (bitsPerSample / 8);
    return channelCount is 1 or 2 &&
      sampleRate > 0 &&
      blockAlign == expectedBlockAlign &&
      byteRate == Convert.ToUInt64(sampleRate) * blockAlign &&
      expectedDataSize > 0 &&
      expectedDataSize % blockAlign == 0;
  }

  private sealed class WinMmPlaySoundApi : IWinMmPlaySoundApi {
    private const uint Async = 1;
    private const uint NoDefault = 2;
    private const uint Memory = 4;
    private const uint Loop = 8;

    public bool TryStartLoop(nint waveAddress) =>
      PlaySound(waveAddress, 0, Async | NoDefault | Memory | Loop);

    public void Stop() => PlaySound(0, 0, 0);

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(nint sound, nint module, uint flags);
  }
}
