// Bone Animations
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Numerics;
using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>One time-stamped translation or Euler-rotation value from a BAN track.</summary>
public readonly record struct BoneAnimationKeyframe(float Time, Vector3 Value);

/// <summary>The translation and rotation tracks for one named bone.</summary>
public sealed record BoneAnimationBone(
  string Name,
  IReadOnlyList<BoneAnimationKeyframe> Translations,
  IReadOnlyList<BoneAnimationKeyframe> Rotations
);

/// <summary>A decoded RCT3 bone-animation resource.</summary>
public sealed record BoneAnimation(
  string Name,
  float TotalTime,
  IReadOnlyList<BoneAnimationBone> Bones
);

/// <summary>Decodes relocation-backed <c>ban</c> resources without renderer dependencies.</summary>
public static class BoneAnimations {
  // See boneanim.h, vertex.h, and ManagerBAN.cpp in rct3-importer's libOVLng. Frontier's
  // pointers are 32-bit: BoneAnim is 12 bytes, BoneAnimBone is 20, and txyz is 16.
  private const int HeaderSize = 12;
  private const int BoneSize = 20;
  private const int KeyframeSize = 16;
  private const int MaximumArrayBytes = 256 * 1024 * 1024;
  private const int MaximumAnimationCount = 64 * 1024;
  private const int MaximumBoneCount = 64 * 1024;
  private const int MaximumTrackKeyframeCount = 1_000_000;
  private const int MaximumBoneNameBytes = 4 * 1024;
  private const int MaximumResourceCount = 1_000_000;

  /// <summary>Decodes every bone animation from the common half of an OVL pair.</summary>
  public static IReadOnlyList<BoneAnimation> Extract(Ovl ovl) =>
    Extract(ovl, BoneAnimationDecodeLimits.Default);

  internal static IReadOnlyList<BoneAnimation> Extract(
    Ovl ovl,
    BoneAnimationDecodeLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(ovl);
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the BAN decoder limit {MaximumResourceCount}.");

    var context = new DecodeContext(limits);
    var files = new List<OvlFile>();
    foreach (var file in ovl.Keys.Where(file =>
      file.Type == FileType.BoneAnim &&
      file.Path.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase))) {
      if (files.Count >= MaximumAnimationCount)
        throw Invalid(file.Name,
          $"animation count exceeds the decoder limit {MaximumAnimationCount}");
      context.ReserveObjects(1, file.Name, "animation resource index");
      files.Add(file);
    }

    var source = new OvlBoneAnimationDataSource(ovl, context);
    var animations = new List<BoneAnimation>(files.Count);
    var addresses = new HashSet<uint>();
    foreach (var file in files) {
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      if (!addresses.Add(address))
        throw Invalid(file.Name, $"resource header aliases an earlier BAN at {address}");
      var owner = source.GetBoneAnimationLoader(file, address);
      animations.Add(Decode(file.Name, owner, source, context));
    }
    return animations;
  }

  internal static BoneAnimation Decode(
    string name,
    OvlLoaderEntry owner,
    IBoneAnimationDataSource source
  ) => Decode(
    name,
    owner,
    source,
    new DecodeContext(BoneAnimationDecodeLimits.Default));

  internal static BoneAnimation Decode(
    string name,
    OvlLoaderEntry owner,
    IBoneAnimationDataSource source,
    BoneAnimationDecodeLimits limits
  ) => Decode(name, owner, source, new DecodeContext(limits));

  private static BoneAnimation Decode(
    string name,
    OvlLoaderEntry owner,
    IBoneAnimationDataSource source,
    DecodeContext context
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentNullException.ThrowIfNull(source);
    if (owner.Tag.ToFileType() != FileType.BoneAnim || !source.HasExactLoader(owner))
      throw Invalid(name,
        $"address {owner.DataAddress} is not owned by one exact ban loader-table entry");

    var address = owner.DataAddress;
    var header = ReadExact(source, address, HeaderSize, name, "animation header", context);
    var boneCount = ReadUInt32(header, 0);
    if (boneCount > MaximumBoneCount)
      throw Invalid(name, $"bone count {boneCount} exceeds the decoder limit {MaximumBoneCount}");
    var totalTime = ReadFiniteSingle(header, 8, name, "total time");
    if (totalTime < 0f) throw Invalid(name, "total time is negative");
    context.ReserveObjects(1 + Convert.ToUInt64(boneCount), name, "animation and bone objects");

    if (boneCount == 0) return new BoneAnimation(name, totalTime, []);
    var bonesAddress = ReadMatchingRequiredPointer(
      source,
      CheckedAdd(address, 4, name),
      ReadUInt32(header, 4),
      name,
      "bone array");
    var boneBytes = ReadArray(
      source, bonesAddress, boneCount, BoneSize, name, "bone array", context);
    var bones = new BoneAnimationBone[ToCount(boneCount, name, "bone count")];
    var boneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    for (var index = 0; index < bones.Length; index++) {
      var offset = index * BoneSize;
      var boneAddress = CheckedAdd(
        bonesAddress, Convert.ToUInt32(offset), name);
      var boneNameAddress = ReadMatchingRelocatedPointer(
        source,
        boneAddress,
        ReadUInt32(boneBytes, offset),
        allowZero: true,
        name,
        $"bone {index} name");
      var boneName = ReadBoneName(name, index, boneNameAddress, source, context);
      if (!boneNames.Add(boneName))
        throw Invalid(name, $"bone {index} duplicates name '{boneName}'");

      var translations = ReadTrack(
        name,
        index,
        "translation",
        ReadUInt32(boneBytes, offset + 4),
        CheckedAdd(boneAddress, 8, name),
        ReadUInt32(boneBytes, offset + 8),
        source,
        context);
      var rotations = ReadTrack(
        name,
        index,
        "rotation",
        ReadUInt32(boneBytes, offset + 12),
        CheckedAdd(boneAddress, 16, name),
        ReadUInt32(boneBytes, offset + 16),
        source,
        context);
      bones[index] = new BoneAnimationBone(boneName, translations, rotations);
    }

    return new BoneAnimation(name, totalTime, bones);
  }

  private static IReadOnlyList<BoneAnimationKeyframe> ReadTrack(
    string animationName,
    int boneIndex,
    string trackName,
    uint count,
    uint pointerFieldAddress,
    uint storedPointer,
    IBoneAnimationDataSource source,
    DecodeContext context
  ) {
    if (count == 0) {
      if (storedPointer != 0 || source.TryGetRelocationSource(pointerFieldAddress, out _))
        throw Invalid(animationName,
          $"bone {boneIndex} empty {trackName} track has a pointer");
      return [];
    }
    if (count > MaximumTrackKeyframeCount)
      throw Invalid(animationName,
        $"bone {boneIndex} {trackName} count {count} exceeds the decoder limit " +
        MaximumTrackKeyframeCount);

    context.ReserveObjects(count, animationName, $"bone {boneIndex} {trackName} keyframes");
    var address = ReadMatchingRequiredPointer(
      source,
      pointerFieldAddress,
      storedPointer,
      animationName,
      $"bone {boneIndex} {trackName} track");
    var bytes = ReadArray(
      source,
      address,
      count,
      KeyframeSize,
      animationName,
      $"bone {boneIndex} {trackName} track",
      context);
    var frames = new BoneAnimationKeyframe[
      ToCount(count, animationName, $"bone {boneIndex} {trackName} count")];
    var previousTime = -1f;
    for (var index = 0; index < frames.Length; index++) {
      var offset = index * KeyframeSize;
      var time = ReadFiniteSingle(
        bytes, offset, animationName, $"bone {boneIndex} {trackName} key {index} time");
      var value = new Vector3(
        ReadFiniteSingle(
          bytes, offset + 4, animationName, $"bone {boneIndex} {trackName} key {index} X"),
        ReadFiniteSingle(
          bytes, offset + 8, animationName, $"bone {boneIndex} {trackName} key {index} Y"),
        ReadFiniteSingle(
          bytes, offset + 12, animationName, $"bone {boneIndex} {trackName} key {index} Z"));
      if (time < 0f)
        throw Invalid(animationName,
          $"bone {boneIndex} {trackName} key {index} has negative time {time}");
      if (index > 0 && time <= previousTime)
        throw Invalid(animationName,
          $"bone {boneIndex} {trackName} key times are not strictly increasing");
      frames[index] = new BoneAnimationKeyframe(time, value);
      previousTime = time;
    }
    return frames;
  }

  private static string ReadBoneName(
    string animationName,
    int boneIndex,
    uint address,
    IBoneAnimationDataSource source,
    DecodeContext context
  ) {
    if (context.TryGetBoneName(address, out var cached)) return cached;
    if (!source.TryGetNullTerminatedStringByteLength(
          address, MaximumBoneNameBytes, out var byteLength) || byteLength == 0)
      throw Invalid(animationName,
        $"bone {boneIndex} name is missing, unterminated, or exceeds {MaximumBoneNameBytes} bytes");
    context.ReserveBytes(
      Convert.ToUInt64(byteLength) + 1, animationName, $"bone {boneIndex} name");
    context.ReserveObjects(1, animationName, $"bone {boneIndex} name cache");
    if (!source.TryReadNullTerminatedString(address, byteLength + 1, out var value) ||
        string.IsNullOrEmpty(value))
      throw Invalid(animationName, $"bone {boneIndex} name changed while it was being decoded");
    context.AddBoneName(address, value, animationName, boneIndex);
    return value;
  }

  private static byte[] ReadArray(
    IBoneAnimationDataSource source,
    uint address,
    uint count,
    int stride,
    string animationName,
    string description,
    DecodeContext context
  ) {
    var length = ByteCount(count, stride, animationName, description);
    return ReadExact(source, address, length, animationName, description, context);
  }

  private static byte[] ReadExact(
    IBoneAnimationDataSource source,
    uint address,
    int length,
    string animationName,
    string description,
    DecodeContext context
  ) {
    context.ReserveBytes(Convert.ToUInt64(length), animationName, description);
    if (!source.TryReadBytes(address, length, out var bytes) || bytes.Length != length)
      throw Invalid(animationName, $"{description} is outside the archive or truncated");
    return bytes;
  }

  private static uint ReadMatchingRequiredPointer(
    IBoneAnimationDataSource source,
    uint sourceAddress,
    uint rawValue,
    string animationName,
    string description
  ) => ReadMatchingRelocatedPointer(
    source, sourceAddress, rawValue, allowZero: false, animationName, description);

  private static uint ReadMatchingRelocatedPointer(
    IBoneAnimationDataSource source,
    uint sourceAddress,
    uint rawValue,
    bool allowZero,
    string animationName,
    string description
  ) {
    if (!source.TryGetRelocationSource(sourceAddress, out var target) ||
        (!allowZero && target == 0))
      throw Invalid(animationName,
        allowZero
          ? $"{description} is not a relocated pointer"
          : $"{description} is not a non-null relocated pointer");
    if (target != rawValue)
      throw Invalid(animationName, $"{description} does not match its relocation target");
    return target;
  }

  private static int ByteCount(
    uint count,
    int stride,
    string animationName,
    string description
  ) {
    var length = Convert.ToUInt64(count) * Convert.ToUInt64(stride);
    if (length == 0 || length > MaximumArrayBytes)
      throw Invalid(animationName,
        $"{description} byte length {length} is outside the decoder limit");
    return Convert.ToInt32(length);
  }

  private static int ToCount(uint count, string animationName, string description) {
    if (count > int.MaxValue)
      throw Invalid(animationName, $"{description} {count} exceeds the decoder limit");
    return Convert.ToInt32(count);
  }

  private static uint CheckedAdd(uint address, uint offset, string animationName) {
    var result = Convert.ToUInt64(address) + offset;
    if (result > uint.MaxValue)
      throw Invalid(animationName, "pointer address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static uint ReadUInt32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);

  private static float ReadFiniteSingle(
    byte[] bytes,
    int offset,
    string animationName,
    string description
  ) {
    var value = BitConverter.ToSingle(bytes, offset);
    if (!float.IsFinite(value))
      throw Invalid(animationName, $"{description} is non-finite");
    return value;
  }

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Bone animation '{name}' is malformed: {message}.");

  private sealed class DecodeContext(BoneAnimationDecodeLimits limits) {
    private readonly Dictionary<uint, string> boneNames = [];
    private ulong decodedBytes;
    private ulong decodedObjects;

    public void ReserveBytes(ulong count, string animationName, string description) {
      if (count > limits.MaximumBytes || decodedBytes > limits.MaximumBytes - count)
        throw Invalid(animationName,
          $"aggregate decode bytes exceed the limit {limits.MaximumBytes} while reading " +
          description);
      decodedBytes += count;
    }

    public void ReserveObjects(ulong count, string animationName, string description) {
      if (count > limits.MaximumObjects || decodedObjects > limits.MaximumObjects - count)
        throw Invalid(animationName,
          $"aggregate decoded objects exceed the limit {limits.MaximumObjects} while reading " +
          description);
      decodedObjects += count;
    }

    public bool TryGetBoneName(uint address, out string value) =>
      boneNames.TryGetValue(address, out value!);

    public void AddBoneName(uint address, string value, string animationName, int boneIndex) {
      if (!boneNames.TryAdd(address, value))
        throw Invalid(animationName, $"bone {boneIndex} name alias changed during decoding");
    }
  }

  private sealed class OvlBoneAnimationDataSource : IBoneAnimationDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;
    private readonly IReadOnlyList<OvlLoaderEntry> loaders;
    private readonly OvlCommonStringTable stringTable;

    public OvlBoneAnimationDataSource(Ovl ovl, DecodeContext context) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the BAN decoder limit " +
          $"{MaximumResourceCount}.");
      context.ReserveObjects(
        OvlLoaderIndexBudget.Calculate(ovl.LoaderEntriesInOrder.Count),
        "OVL",
        "loader metadata index");

      loaders = ovl.LoaderEntriesInOrder.ToArray();
      var mutableLoaders = new Dictionary<uint, List<OvlLoaderEntry>>();
      foreach (var entry in ovl.LoaderEntriesInOrder) {
        if (!mutableLoaders.TryGetValue(entry.DataAddress, out var candidates))
          mutableLoaders.Add(entry.DataAddress, candidates = []);
        candidates.Add(entry);
      }
      loadersByDataAddress = mutableLoaders.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<OvlLoaderEntry>)pair.Value);
      stringTable = new OvlCommonStringTable(ovl);
    }

    public OvlLoaderEntry GetBoneAnimationLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact ban loader-table entry");
      var matches = candidates.Where(entry =>
        entry.Tag.ToFileType() == FileType.BoneAnim &&
        string.Equals(entry.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact ban loader-table entry");
      return matches[0];
    }

    public bool HasExactLoader(OvlLoaderEntry owner) =>
      loaders.Any(loader => SameLoader(loader, owner));

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

    public bool TryGetNullTerminatedStringByteLength(
      uint address,
      int maximumLength,
      out int length
    ) {
      if (!stringTable.TryReadString(address, maximumLength, out var value)) {
        length = 0;
        return false;
      }
      length = Encoding.ASCII.GetByteCount(value);
      return true;
    }

    public bool TryReadNullTerminatedString(uint address, int maximumLength, out string value) =>
      stringTable.TryReadString(address, maximumLength, out value);

    private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
      left.DataAddress == right.DataAddress &&
      left.StructAddress == right.StructAddress &&
      string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);
  }
}

internal readonly record struct BoneAnimationDecodeLimits(ulong MaximumBytes, ulong MaximumObjects) {
  public static BoneAnimationDecodeLimits Default { get; } =
    new(256UL * 1024 * 1024, 1_000_000);
}

internal interface IBoneAnimationDataSource {
  bool HasExactLoader(OvlLoaderEntry owner);
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryGetRelocationSource(uint address, out uint value);
  bool TryGetNullTerminatedStringByteLength(uint address, int maximumLength, out int length);
  bool TryReadNullTerminatedString(uint address, int maximumLength, out string value);
}
