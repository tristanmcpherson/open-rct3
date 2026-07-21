// Sounds Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class SoundsTests {
  [Test]
  public void Decode_StereoPcmPreservesLoopAndBuildsCanonicalInterleavedWave() {
    var sound = new SoundFixture(stereo: true).Decode();
    var wave = sound.ToWaveBytes();

    using (Assert.EnterMultipleScope()) {
      Assert.That(sound.Name, Is.EqualTo("synthetic"));
      Assert.That(sound.FormatTag, Is.EqualTo(1));
      Assert.That(sound.ChannelCount, Is.EqualTo(2));
      Assert.That(sound.SampleRate, Is.EqualTo(4));
      Assert.That(sound.ByteRate, Is.EqualTo(16));
      Assert.That(sound.BlockAlign, Is.EqualTo(4));
      Assert.That(sound.BitsPerSample, Is.EqualTo(16));
      Assert.That(sound.Loops, Is.True);
      Assert.That(sound.Channel1, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
      Assert.That(sound.Channel2, Is.EqualTo(new byte[] { 11, 12, 13, 14 }));
      Assert.That(wave, Has.Length.EqualTo(52));
      Assert.That(wave.AsSpan(0, 4).ToArray(), Is.EqualTo("RIFF"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(4)), Is.EqualTo(44));
      Assert.That(wave.AsSpan(8, 8).ToArray(), Is.EqualTo("WAVEfmt "u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(16)), Is.EqualTo(16));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(20)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(22)), Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(24)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(28)), Is.EqualTo(16));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(32)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(wave.AsSpan(34)), Is.EqualTo(16));
      Assert.That(wave.AsSpan(36, 4).ToArray(), Is.EqualTo("data"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(40)), Is.EqualTo(8));
      Assert.That(wave.AsSpan(44).ToArray(), Is.EqualTo(new byte[] {
        1, 2, 11, 12, 3, 4, 13, 14
      }));
    }
  }

  [Test]
  public void Decode_MonoPcmPreservesItsOnlyChannel() {
    var sound = new SoundFixture(stereo: false).Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(sound.ChannelCount, Is.EqualTo(1));
      Assert.That(sound.Loops, Is.False);
      Assert.That(sound.Channel1, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
      Assert.That(sound.Channel2, Is.Empty);
      Assert.That(sound.ToWaveBytes().AsSpan(44).ToArray(),
        Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
    }
  }

  [Test]
  public void Decode_EnforcesAggregateDecodeBudgets() {
    var fixture = new SoundFixture(stereo: true);

    using (Assert.EnterMultipleScope()) {
      Assert.Throws<InvalidDataException>(new Action(() =>
        fixture.Decode(new SoundDecodeLimits(87, 100))));
      Assert.Throws<InvalidDataException>(new Action(() =>
        fixture.Decode(new SoundDecodeLimits(100, 0))));
    }
  }

  [TestCase(MalformedSound.MissingExactOwner)]
  [TestCase(MalformedSound.TruncatedRecord)]
  [TestCase(MalformedSound.NonFiniteMetadata)]
  [TestCase(MalformedSound.UnexpectedMetadata)]
  [TestCase(MalformedSound.UnsupportedFormat)]
  [TestCase(MalformedSound.InvalidBlockAlign)]
  [TestCase(MalformedSound.InvalidByteRate)]
  [TestCase(MalformedSound.MissingChannel1Relocation)]
  [TestCase(MalformedSound.NegativeChannel1Size)]
  [TestCase(MalformedSound.TruncatedChannel1)]
  [TestCase(MalformedSound.MissingChannel2Relocation)]
  [TestCase(MalformedSound.MismatchedStereoChannels)]
  [TestCase(MalformedSound.MonoSecondChannelPointer)]
  public void Decode_RejectsMalformedOrUnsupportedSnd(MalformedSound malformed) {
    var fixture = new SoundFixture(stereo: malformed != MalformedSound.MonoSecondChannelPointer);
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  [Explicit("Requires installed RCT3 Rain assets via RCT3_PATH.")]
  public void Extract_InstalledMainRain_RecordsExactLoopingPcmEvidence() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(root!, "Main.common.ovl");
    Assert.That(path, Does.Exist, $"Installed Main OVL is missing: {path}");

    using var ovl = Ovl.Load(path);
    var sounds = Sounds.Extract(ovl).Where(sound => sound.Name.StartsWith(
      "Rain", StringComparison.OrdinalIgnoreCase)).ToArray();
    var sound = sounds.Single(sound => string.Equals(
      sound.Name, "Rain1", StringComparison.OrdinalIgnoreCase));
    var wave = sound.ToWaveBytes();
    var hash = Convert.ToHexString(SHA256.HashData(wave));
    TestContext.Progress.WriteLine(
      $"Installed SND evidence: path={path}, names={string.Join(',', sounds.Select(value => value.Name))}, " +
      $"channel1Bytes={string.Join(',', sounds.Select(value => value.Channel1.Length))}, " +
      $"name={sound.Name}, resources={sounds.Length}, " +
      $"format={sound.FormatTag}, channels={sound.ChannelCount}, sampleRate={sound.SampleRate}, " +
      $"byteRate={sound.ByteRate}, blockAlign={sound.BlockAlign}, bits={sound.BitsPerSample}, " +
      $"loop={sound.Loops}, channel1={sound.Channel1.Length}, channel2={sound.Channel2.Length}, " +
      $"wavBytes={wave.Length}, sha256={hash}");

    using (Assert.EnterMultipleScope()) {
      Assert.That(sounds.Select(candidate => candidate.Name), Is.EqualTo(new[] {
        "Rain1", "Rain2", "Rain3", "Rain4", "Rain5"
      }));
      Assert.That(sounds.Select(candidate => candidate.FormatTag), Is.EqualTo(new ushort[] {
        1, 1, 1, 1, 1
      }));
      Assert.That(sounds.Select(candidate => candidate.ChannelCount), Is.EqualTo(new ushort[] {
        1, 1, 1, 1, 1
      }));
      Assert.That(sounds.Select(candidate => candidate.SampleRate), Is.EqualTo(new uint[] {
        22_050, 22_050, 22_050, 22_050, 22_050
      }));
      Assert.That(sounds.Select(candidate => candidate.ByteRate), Is.EqualTo(new uint[] {
        44_100, 44_100, 44_100, 44_100, 44_100
      }));
      Assert.That(sounds.Select(candidate => candidate.BlockAlign), Is.EqualTo(new ushort[] {
        2, 2, 2, 2, 2
      }));
      Assert.That(sounds.Select(candidate => candidate.BitsPerSample), Is.EqualTo(new ushort[] {
        16, 16, 16, 16, 16
      }));
      Assert.That(sounds.All(candidate => candidate.Loops), Is.True);
      Assert.That(sounds.Select(candidate => candidate.Channel1.Length), Is.EqualTo(new[] {
        151_940, 93_622, 166_684, 201_646, 124_972
      }));
      Assert.That(sounds.All(candidate => candidate.Channel2.Length == 0), Is.True);
      Assert.That(wave.Length, Is.EqualTo(151_984));
      Assert.That(hash, Is.EqualTo(
        "231488D3C7B0EBD49FF1B02178B505CAA86DAF15252F30A3CB63D507E2313DCD"));
      Assert.That(wave.AsSpan(0, 4).ToArray(), Is.EqualTo("RIFF"u8.ToArray()));
      Assert.That(wave.AsSpan(8, 4).ToArray(), Is.EqualTo("WAVE"u8.ToArray()));
    }
  }

  public enum MalformedSound {
    MissingExactOwner,
    TruncatedRecord,
    NonFiniteMetadata,
    UnexpectedMetadata,
    UnsupportedFormat,
    InvalidBlockAlign,
    InvalidByteRate,
    MissingChannel1Relocation,
    NegativeChannel1Size,
    TruncatedChannel1,
    MissingChannel2Relocation,
    MismatchedStereoChannels,
    MonoSecondChannelPointer,
  }

  private sealed class SoundFixture {
    private const uint RecordAddress = 100;
    private const uint Channel1Address = 200;
    private const uint Channel2Address = 300;
    private readonly FakeSoundDataSource source = new();
    private readonly OvlLoaderEntry owner = new("snd", RecordAddress, "fixture.unique.ovl", 900);

    public SoundFixture(bool stereo) {
      source.Loaders.Add(owner);
      var record = source.AddBlock(RecordAddress, Sounds.RecordSize);
      WriteUInt16(record, 0, 1);
      WriteUInt16(record, 2, Convert.ToUInt16(stereo ? 2 : 1));
      WriteUInt32(record, 4, 4);
      WriteUInt32(record, 8, Convert.ToUInt32(stereo ? 16 : 8));
      WriteUInt16(record, 12, Convert.ToUInt16(stereo ? 4 : 2));
      WriteUInt16(record, 14, 16);
      var metadata = new[] {
        0f, 0f, 0.05f, 0.00004f, 1f, 0f, 1_500_000f, 0f, 0.05f, 2f, 30f
      };
      foreach (var index in Enumerable.Range(0, metadata.Length))
        WriteSingle(record, 16 + index * sizeof(float), metadata[index]);
      WriteInt32(record, 60, stereo ? 1 : 0);
      WritePointer(record, 64, Channel1Address);
      WriteInt32(record, 68, 4);
      if (stereo) {
        WritePointer(record, 72, Channel2Address);
        WriteInt32(record, 76, 4);
      }
      source.AddBlock(Channel1Address, new byte[] { 1, 2, 3, 4 });
      if (stereo) source.AddBlock(Channel2Address, new byte[] { 11, 12, 13, 14 });
    }

    public Sound Decode() => Sounds.Decode("synthetic", owner, source);
    public Sound Decode(SoundDecodeLimits limits) => Sounds.Decode("synthetic", owner, source, limits);

    public void MakeMalformed(MalformedSound malformed) {
      var record = source.Blocks[RecordAddress];
      switch (malformed) {
        case MalformedSound.MissingExactOwner:
          source.Loaders.Clear();
          break;
        case MalformedSound.TruncatedRecord:
          source.Blocks[RecordAddress] = record[..^1];
          break;
        case MalformedSound.NonFiniteMetadata:
          WriteSingle(record, 16, float.NaN);
          break;
        case MalformedSound.UnexpectedMetadata:
          WriteSingle(record, 16, 1f);
          break;
        case MalformedSound.UnsupportedFormat:
          WriteUInt16(record, 0, 3);
          break;
        case MalformedSound.InvalidBlockAlign:
          WriteUInt16(record, 12, 1);
          break;
        case MalformedSound.InvalidByteRate:
          WriteUInt32(record, 8, 1);
          break;
        case MalformedSound.MissingChannel1Relocation:
          source.Relocations.Remove(RecordAddress + 64);
          break;
        case MalformedSound.NegativeChannel1Size:
          WriteInt32(record, 68, -1);
          break;
        case MalformedSound.TruncatedChannel1:
          source.Blocks[Channel1Address] = new byte[] { 1, 2, 3 };
          break;
        case MalformedSound.MissingChannel2Relocation:
          source.Relocations.Remove(RecordAddress + 72);
          break;
        case MalformedSound.MismatchedStereoChannels:
          WriteInt32(record, 76, 2);
          source.Blocks[Channel2Address] = new byte[] { 11, 12 };
          break;
        case MalformedSound.MonoSecondChannelPointer:
          WritePointer(record, 72, Channel2Address);
          break;
      }
    }

    private void WritePointer(byte[] record, int offset, uint value) {
      WriteUInt32(record, offset, value);
      source.Relocations[RecordAddress + Convert.ToUInt32(offset)] = value;
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
      BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
      BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
      BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WriteSingle(byte[] bytes, int offset, float value) =>
      BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
  }

  private sealed class FakeSoundDataSource : ISoundDataSource {
    public Dictionary<uint, byte[]> Blocks { get; } = [];
    public Dictionary<uint, uint> Relocations { get; } = [];
    public List<OvlLoaderEntry> Loaders { get; } = [];

    public byte[] AddBlock(uint address, int length) {
      var bytes = new byte[length];
      Blocks.Add(address, bytes);
      return bytes;
    }

    public void AddBlock(uint address, byte[] bytes) => Blocks.Add(address, bytes);

    public bool HasExactLoader(OvlLoaderEntry owner) => Loaders.Any(loader =>
      loader.DataAddress == owner.DataAddress &&
      loader.StructAddress == owner.StructAddress &&
      string.Equals(loader.Tag, owner.Tag, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(loader.SourcePath, owner.SourcePath, StringComparison.OrdinalIgnoreCase));

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      foreach (var block in Blocks) {
        if (address < block.Key) continue;
        var offset = Convert.ToUInt64(address - block.Key);
        if (offset + Convert.ToUInt64(length) > Convert.ToUInt64(block.Value.Length)) continue;
        bytes = block.Value.AsSpan(Convert.ToInt32(offset), length).ToArray();
        return true;
      }
      bytes = [];
      return false;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      Relocations.TryGetValue(address, out value);
  }
}
