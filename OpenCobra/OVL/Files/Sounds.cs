// Sounds
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Buffers.Binary;
using System.Linq;

namespace OpenCobra.OVL.Files;

/// <summary>One relocation-backed RCT3 <c>snd</c> resource with canonical PCM WAV output.</summary>
public sealed record Sound(
  string Name,
  ushort FormatTag,
  ushort ChannelCount,
  uint SampleRate,
  uint ByteRate,
  ushort BlockAlign,
  ushort BitsPerSample,
  int LoopFlag,
  byte[] Channel1,
  byte[] Channel2
) {
  /// <summary>Whether the serialized sound-loop flag requests repeating playback.</summary>
  public bool Loops => LoopFlag != 0;

  /// <summary>Builds the exact little-endian RIFF/WAVE payload represented by this sound.</summary>
  public byte[] ToWaveBytes() => Sounds.BuildWaveBytes(this);
}

/// <summary>Decodes exact RCT3 <c>snd</c> records and builds PCM WAV payloads.</summary>
/// <remarks>
/// The record layout and stereo channel interleaving are ported directly from
/// <see href="../../../assets/reference/ovl/snd.rs">the checked-in snd.rs reference</see>.
/// </remarks>
public static class Sounds {
  internal const int RecordSize = 80;
  private const int MetadataOffset = 16;
  private const int MetadataCount = 11;
  private const int LoopFlagOffset = 60;
  private const int Channel1PointerOffset = 64;
  private const int Channel1SizeOffset = 68;
  private const int Channel2PointerOffset = 72;
  private const int Channel2SizeOffset = 76;
  private const ushort PcmFormatTag = 1;
  private const ushort PcmBitsPerSample = 16;
  private const int BytesPerSample = PcmBitsPerSample / 8;
  private const int WaveHeaderSize = 44;
  private const int MaximumResourceCount = 1_000_000;
  private const int MaximumChannelBytes = 256 * 1024 * 1024;
  private static readonly float[] ExpectedMetadata = [
    0f, 0f, 0.05f, 0.00004f, 1f, 0f, 1_500_000f, 0f, 0.05f, 2f, 30f
  ];

  /// <summary>Extracts every relocation-backed RCT3 sound resource in an OVL pair.</summary>
  public static IReadOnlyList<Sound> Extract(Ovl ovl) =>
    Extract(ovl, SoundDecodeLimits.Default);

  internal static IReadOnlyList<Sound> Extract(Ovl ovl, SoundDecodeLimits limits) {
    ArgumentNullException.ThrowIfNull(ovl);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the SND decoder limit {MaximumResourceCount}.");

    var context = new DecodeContext(limits);
    var files = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file => file.Type == FileType.Sound)) {
      if (files.Count >= MaximumResourceCount)
        throw Invalid(file.Name, $"sound count exceeds the decoder limit {MaximumResourceCount}");
      context.ReserveObjects(1, file.Name, "sound resource index");
      files.Add(file);
    }

    if (files.Count == 0) return [];

    var source = new OvlSoundDataSource(ovl, context);
    var sounds = new List<Sound>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier SND at {address}");
      sounds.Add(Decode(file.Name, source.GetSoundLoader(file, address), source, context));
    }
    return sounds;
  }

  internal static Sound Decode(string name, OvlLoaderEntry owner, ISoundDataSource source) =>
    Decode(name, owner, source, new DecodeContext(SoundDecodeLimits.Default));

  internal static Sound Decode(
    string name,
    OvlLoaderEntry owner,
    ISoundDataSource source,
    SoundDecodeLimits limits
  ) => Decode(name, owner, source, new DecodeContext(limits));

  internal static byte[] BuildWaveBytes(Sound sound) {
    ArgumentNullException.ThrowIfNull(sound);
    ValidateWaveHeader(sound, sound.Name);
    if (sound.Channel1.Length > MaximumChannelBytes || sound.Channel2.Length > MaximumChannelBytes)
      throw Invalid(sound.Name, "channel payload exceeds the WAV builder limit");

    var pcm = BuildPcm(sound);
    var riffSize = Convert.ToUInt64(36) + Convert.ToUInt64(pcm.Length);
    if (riffSize > uint.MaxValue)
      throw Invalid(sound.Name, "WAV RIFF size exceeds 32 bits");
    var output = new byte[checked(WaveHeaderSize + pcm.Length)];
    "RIFF"u8.CopyTo(output);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), Convert.ToUInt32(riffSize));
    "WAVEfmt "u8.CopyTo(output.AsSpan(8));
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(16), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(20), sound.FormatTag);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(22), sound.ChannelCount);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(24), sound.SampleRate);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(28), sound.ByteRate);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(32), sound.BlockAlign);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(34), sound.BitsPerSample);
    "data"u8.CopyTo(output.AsSpan(36));
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(40), Convert.ToUInt32(pcm.Length));
    pcm.CopyTo(output, WaveHeaderSize);
    return output;
  }

  private static Sound Decode(
    string name,
    OvlLoaderEntry owner,
    ISoundDataSource source,
    DecodeContext context
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.Sound || !source.HasExactLoader(owner))
      throw Invalid(name, "header is not owned by one exact snd loader-table entry");

    var address = owner.DataAddress;
    var record = ReadExact(source, address, RecordSize, name, "80-byte SND record", context);
    var formatTag = BinaryPrimitives.ReadUInt16LittleEndian(record);
    var channelCount = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(2));
    var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4));
    var byteRate = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8));
    var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(12));
    var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(14));
    ValidateMetadata(record, name);
    var loopFlag = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(LoopFlagOffset));

    var channel1Size = ReadPayloadSize(record, Channel1SizeOffset, name, "channel 1");
    var channel2Size = ReadPayloadSize(record, Channel2SizeOffset, name, "channel 2");
    var channel1Address = ReadRequiredPointer(
      source, address, record, Channel1PointerOffset, name, "channel 1");
    var channel2Address = ReadChannel2Pointer(
      source, address, record, channelCount, channel2Size, name);

    var header = new Sound(
      name,
      formatTag,
      channelCount,
      sampleRate,
      byteRate,
      blockAlign,
      bitsPerSample,
      loopFlag,
      [],
      []);
    ValidateWaveHeader(header, name);
    ValidatePayloadShape(channel1Size, channel2Size, channelCount, name);
    context.ReserveObjects(1, name, "sound model");
    var channel1 = ReadExact(source, channel1Address, channel1Size, name, "channel 1", context);
    var channel2 = channel2Size == 0
      ? []
      : ReadExact(source, channel2Address, channel2Size, name, "channel 2", context);
    return header with { Channel1 = channel1, Channel2 = channel2 };
  }

  private static void ValidateMetadata(byte[] record, string name) {
    foreach (var index in Enumerable.Range(0, MetadataCount)) {
      var value = BinaryPrimitives.ReadSingleLittleEndian(
        record.AsSpan(MetadataOffset + index * sizeof(float)));
      if (!float.IsFinite(value)) throw Invalid(name, $"metadata value {index} is not finite");
      if (value != ExpectedMetadata[index])
        throw Invalid(name, $"metadata value {index} is {value}, not {ExpectedMetadata[index]}");
    }
  }

  private static void ValidateWaveHeader(Sound sound, string name) {
    if (sound.FormatTag != PcmFormatTag)
      throw Invalid(name, $"format tag {sound.FormatTag} is not PCM ({PcmFormatTag})");
    if (sound.ChannelCount is not 1 and not 2)
      throw Invalid(name, $"channel count {sound.ChannelCount} is not mono or stereo");
    if (sound.SampleRate == 0) throw Invalid(name, "sample rate is zero");
    if (sound.BitsPerSample != PcmBitsPerSample)
      throw Invalid(name, $"bits per sample {sound.BitsPerSample} is not {PcmBitsPerSample}");
    var expectedBlockAlign = Convert.ToUInt32(sound.ChannelCount) * BytesPerSample;
    if (sound.BlockAlign != expectedBlockAlign)
      throw Invalid(name, $"block align {sound.BlockAlign} does not match PCM channel layout");
    var expectedByteRate = Convert.ToUInt64(sound.SampleRate) * sound.BlockAlign;
    if (sound.ByteRate != expectedByteRate)
      throw Invalid(name, $"byte rate {sound.ByteRate} does not match sample rate and block align");
  }

  private static void ValidatePayloadShape(
    int channel1Size,
    int channel2Size,
    ushort channelCount,
    string name
  ) {
    if (channel1Size == 0) throw Invalid(name, "channel 1 has no PCM payload");
    if (channel1Size % BytesPerSample != 0)
      throw Invalid(name, "channel 1 payload does not contain complete PCM samples");
    if (channelCount == 1) {
      if (channel2Size != 0) throw Invalid(name, "mono sound has a second channel payload");
      return;
    }
    if (channel2Size == 0) throw Invalid(name, "stereo sound has no second channel payload");
    if (channel2Size % BytesPerSample != 0)
      throw Invalid(name, "channel 2 payload does not contain complete PCM samples");
    if (channel1Size != channel2Size)
      throw Invalid(name, "stereo channel payload sizes do not match");
  }

  private static byte[] BuildPcm(Sound sound) {
    ValidatePayloadShape(sound.Channel1.Length, sound.Channel2.Length, sound.ChannelCount, sound.Name);
    if (sound.ChannelCount == 1) return sound.Channel1.ToArray();

    var pcm = new byte[checked(sound.Channel1.Length * 2)];
    foreach (var sampleOffset in Enumerable.Range(0, sound.Channel1.Length / BytesPerSample)) {
      var sourceOffset = sampleOffset * BytesPerSample;
      var targetOffset = sampleOffset * sound.BlockAlign;
      sound.Channel1.AsSpan(sourceOffset, BytesPerSample).CopyTo(pcm.AsSpan(targetOffset));
      sound.Channel2.AsSpan(sourceOffset, BytesPerSample).CopyTo(
        pcm.AsSpan(targetOffset + BytesPerSample));
    }
    return pcm;
  }

  private static int ReadPayloadSize(byte[] record, int offset, string name, string description) {
    var size = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(offset));
    if (size < 0) throw Invalid(name, $"{description} payload size is negative");
    if (size > MaximumChannelBytes)
      throw Invalid(name, $"{description} payload size {size} exceeds {MaximumChannelBytes}");
    return size;
  }

  private static uint ReadRequiredPointer(
    ISoundDataSource source,
    uint recordAddress,
    byte[] record,
    int offset,
    string name,
    string description
  ) {
    var fieldAddress = CheckedAdd(recordAddress, offset, name);
    var rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(offset));
    if (!source.TryGetRelocationSource(fieldAddress, out var target) || target != rawPointer)
      throw Invalid(name, $"{description} pointer is not a matching relocation");
    if (target == 0) throw Invalid(name, $"{description} pointer is zero");
    return target;
  }

  private static uint ReadChannel2Pointer(
    ISoundDataSource source,
    uint recordAddress,
    byte[] record,
    ushort channelCount,
    int channel2Size,
    string name
  ) {
    var fieldAddress = CheckedAdd(recordAddress, Channel2PointerOffset, name);
    var rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(Channel2PointerOffset));
    if (channelCount == 1) {
      if (rawPointer != 0 || source.TryGetRelocationSource(fieldAddress, out _))
        throw Invalid(name, "mono sound has a second-channel pointer");
      return 0;
    }
    if (channel2Size == 0)
      throw Invalid(name, "stereo sound has no second channel payload");
    return ReadRequiredPointer(source, recordAddress, record, Channel2PointerOffset, name, "channel 2");
  }

  private static byte[] ReadExact(
    ISoundDataSource source,
    uint address,
    int length,
    string name,
    string description,
    DecodeContext context
  ) {
    context.ReserveBytes(Convert.ToUInt64(length), name, description);
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(name, $"{description} is truncated or outside relocated data");
    return bytes;
  }

  private static uint CheckedAdd(uint address, int offset, string name) {
    var fieldAddress = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (fieldAddress > uint.MaxValue)
      throw Invalid(name, "record field address exceeds the OVL address space");
    return Convert.ToUInt32(fieldAddress);
  }

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Sound '{name}' is malformed: {message}.");

  private sealed class DecodeContext(SoundDecodeLimits limits) {
    private ulong decodedBytes;
    private ulong decodedObjects;

    public void ReserveBytes(ulong count, string soundName, string description) =>
      Reserve(count, limits.MaximumBytes, ref decodedBytes, soundName, description, "bytes");

    public void ReserveObjects(ulong count, string soundName, string description) =>
      Reserve(count, limits.MaximumObjects, ref decodedObjects, soundName, description, "objects");

    private static void Reserve(
      ulong count,
      ulong maximum,
      ref ulong used,
      string soundName,
      string description,
      string kind
    ) {
      if (count > maximum || used > maximum - count)
        throw Invalid(soundName,
          $"aggregate decoded {kind} exceed the limit {maximum} while reading {description}");
      used += count;
    }
  }

  private sealed class OvlSoundDataSource : ISoundDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyList<OvlLoaderEntry> loaders;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;

    public OvlSoundDataSource(Ovl ovl, DecodeContext context) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the SND decoder limit " +
          $"{MaximumResourceCount}.");
      context.ReserveObjects(
        OvlLoaderIndexBudget.Calculate(ovl.LoaderEntriesInOrder.Count),
        "OVL",
        "sound loader metadata index");
      loaders = ovl.LoaderEntriesInOrder.ToArray();
      var mutableLoaders = new Dictionary<uint, List<OvlLoaderEntry>>();
      foreach (var loader in loaders) {
        if (!mutableLoaders.TryGetValue(loader.DataAddress, out var candidates))
          mutableLoaders.Add(loader.DataAddress, candidates = []);
        candidates.Add(loader);
      }
      loadersByDataAddress = mutableLoaders.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<OvlLoaderEntry>)pair.Value);
    }

    public OvlLoaderEntry GetSoundLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name, $"address {address} is not owned by one exact snd loader");
      var matches = candidates.Where(entry =>
        entry.Tag.ToFileType() == FileType.Sound &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1)
        throw Invalid(file.Name, $"address {address} is not owned by one exact snd loader");
      return matches[0];
    }

    public bool HasExactLoader(OvlLoaderEntry owner) => loaders.Any(loader => SameLoader(loader, owner));

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      if (ovl.TryReadBytes(address, length, out var resolved)) {
        bytes = resolved;
        return true;
      }
      bytes = [];
      return false;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      ovl.TryGetRelocationSource(address, out value);
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct SoundDecodeLimits(ulong MaximumBytes, ulong MaximumObjects) {
  public static SoundDecodeLimits Default { get; } =
    new(512UL * 1024 * 1024, 4_000_000);
}

internal interface ISoundDataSource {
  bool HasExactLoader(OvlLoaderEntry owner);
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
}
