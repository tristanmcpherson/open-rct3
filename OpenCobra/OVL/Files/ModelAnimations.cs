// Model Animations
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Text;

namespace OpenCobra.OVL.Files;

/// <summary>One semantically opaque three-float group from offset +0x28.</summary>
public readonly record struct ModelAnimationTriple(float Field00, float Field04, float Field08);

/// <summary>One normalized absolute-local XYZW rotation from offset +0x2C.</summary>
/// <remarks><c>Field00..Field0C</c> are respectively X, Y, Z, and W.</remarks>
public readonly record struct ModelAnimationFourTuple(
  float Field00,
  float Field04,
  float Field08,
  float Field0C
);

/// <summary>The reference-proven structure of one <c>modelanim</c> resource.</summary>
/// <remarks>
/// <see cref="TriplesAt28"/> is flattened in frame-major order and keyed by
/// <see cref="AnimatedBoneNames"/>. <see cref="NormalizedFourTuplesAt2C"/> is independently
/// flattened in frame-major order and keyed by <see cref="FullBoneNames"/>. The full-name order is
/// not MDL bone order; consumers must map exact names. The +0x28 coordinate meaning remains
/// deliberately unspecified.
/// </remarks>
public sealed record ModelAnimationDefinition(
  string Name,
  string SourcePath,
  uint DataAddress,
  float DurationAt00,
  uint FrameCountAt04,
  uint ValuesAt08Address,
  uint ValuesAt0CAddress,
  uint AnimatedBoneCountAt18,
  uint FullBoneCountAt1C,
  uint TriplesAt28Address,
  uint NormalizedFourTuplesAt2CAddress,
  IReadOnlyList<uint> ValuesAt08,
  IReadOnlyList<uint> ValuesAt0C,
  IReadOnlyList<ModelAnimationTriple> TriplesAt28,
  IReadOnlyList<ModelAnimationFourTuple> NormalizedFourTuplesAt2C,
  IReadOnlyList<string> AnimatedBoneNames,
  IReadOnlyList<string> FullBoneNames
);

/// <summary>Decodes installed Complete Edition <c>modelanim</c> resources.</summary>
/// <remarks>
/// The pinned rct3-importer tree has no ModelAnim manager or resource structure. Its loader
/// structures still establish the version-five loader, relocation, and extra-data envelope used
/// here. The field layout is therefore intentionally limited to invariants independently shared by
/// all 108 installed Elephant and Ostrich clips: an 0x58-byte prefix, four relocated arrays, one
/// strict name chunk, zero SymbolRefs, finite animated-bone triples, and normalized full-bone
/// absolute-local XYZW rotations.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLDump/OVLDump.cpp#L586-L664">
/// Pinned version-five loader and extra-data reader
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/ovlstructs.h#L71-L90">
/// Pinned loader structure
/// </seealso>
public static class ModelAnimations {
  private const int HeaderSize = 0x58;
  private const int TripleSize = 3 * sizeof(float);
  private const int RotationSize = 4 * sizeof(float);
  private const int MaximumAnimationCount = 64 * 1024;
  private const uint MaximumFrameCount = 1_000_000;
  private const uint MaximumBoneCount = 64 * 1024;
  private const ulong MaximumTrackElementCount = 10_000_000;
  private const int MaximumNameBytes = 4 * 1024;
  private const int MaximumNameChunkBytes = 1024 * 1024;
  private const int MaximumResourceCount = 1_000_000;
  private const ulong MaximumDecodedBytes = 256UL * 1024 * 1024;
  private const ulong MaximumDecodedObjects = 20_000_000;
  private const double NormalizedLengthSquaredTolerance = 0.0001;
  private static readonly int[] ZeroHeaderOffsets = [
    0x10, 0x14, 0x20, 0x24,
    0x30, 0x34, 0x38, 0x3C, 0x40, 0x44, 0x48, 0x4C, 0x50, 0x54,
  ];

  /// <summary>Decodes all common-half ModelAnim resources in data-address order.</summary>
  public static IReadOnlyList<ModelAnimationDefinition> Extract(Ovl ovl) {
    ArgumentNullException.ThrowIfNull(ovl);
    ValidateResourceCount(ovl);

    var files = ovl.Keys.Where(file => file.Type == FileType.ModelAnim).ToList();
    if (files.Count > MaximumAnimationCount)
      throw new InvalidDataException(
        $"OVL ModelAnim count {files.Count} exceeds the decoder limit " +
        $"{MaximumAnimationCount}.");
    var source = new OvlModelAnimationDataSource(ovl);
    var inputs = new List<ModelAnimationDecodeInput>(files.Count);
    foreach (var file in files) {
      RequireCommonSource(file);
      if (!ovl.TryGetDataPointer(file, out var address))
        throw Invalid(file.Name, "resource data pointer is missing");
      inputs.Add(new ModelAnimationDecodeInput(
        file.Name,
        source.GetModelAnimationLoader(file, address)));
    }
    return DecodeAll(ovl.Version, inputs, source);
  }

  /// <summary>Decodes one exact, case-insensitively matched <c>Name:modelanim</c> identity.</summary>
  public static ModelAnimationDefinition Extract(Ovl ovl, string taggedReference) {
    ArgumentNullException.ThrowIfNull(ovl);
    ArgumentException.ThrowIfNullOrWhiteSpace(taggedReference);
    var name = ParseTaggedReference(taggedReference);
    var matches = Extract(ovl).Where(animation =>
      string.Equals(animation.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1)
      throw Invalid(name,
        $"reference resolves to {matches.Count} ModelAnim resources instead of one");
    return matches[0];
  }

  internal static IReadOnlyList<ModelAnimationDefinition> DecodeAll(
    Version archiveVersion,
    IReadOnlyList<ModelAnimationDecodeInput> inputs,
    IModelAnimationDataSource source
  ) {
    ArgumentNullException.ThrowIfNull(inputs);
    ArgumentNullException.ThrowIfNull(source);
    if (archiveVersion != Version.Five)
      throw new InvalidDataException(
        $"ModelAnim archive version {archiveVersion} is unsupported; installed evidence is " +
        "version five.");
    if (inputs.Count > MaximumAnimationCount)
      throw new InvalidDataException(
        $"ModelAnim input count {inputs.Count} exceeds the decoder limit " +
        $"{MaximumAnimationCount}.");

    ValidateLoaderCoverage(inputs, source.ModelAnimationLoaders);
    var budget = new DecodeBudget();
    var headers = new List<ModelAnimationHeader>(inputs.Count);
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var addresses = new HashSet<uint>();
    foreach (var input in inputs.OrderBy(input => input.Owner.DataAddress)) {
      ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
      ArgumentNullException.ThrowIfNull(input.Owner);
      if (!names.Add(input.Name))
        throw Invalid(input.Name, "resource name is duplicated case-insensitively");
      if (!addresses.Add(input.Owner.DataAddress))
        throw Invalid(input.Name,
          $"resource header aliases another ModelAnim at {input.Owner.DataAddress}");
      headers.Add(ReadHeader(input, source, budget));
    }
    ValidateNonOverlappingSpans(headers);

    var definitions = new List<ModelAnimationDefinition>(headers.Count);
    foreach (var header in headers)
      definitions.Add(ReadPayload(header, source, budget));
    return definitions.AsReadOnly();
  }

  private static ModelAnimationHeader ReadHeader(
    ModelAnimationDecodeInput input,
    IModelAnimationDataSource source,
    DecodeBudget budget
  ) {
    var name = input.Name;
    var owner = input.Owner;
    if (owner.Tag.ToFileType() != FileType.ModelAnim)
      throw Invalid(name, $"loader type '{owner.Tag}' is not modelanim");
    if (string.IsNullOrWhiteSpace(owner.SourcePath) ||
        !owner.SourcePath.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid(name, "loader is not owned by a common OVL archive");

    var regionLoaders = source.GetDataRegionLoaders(owner);
    if (regionLoaders.Count(loader => SameLoader(loader, owner)) != 1)
      throw Invalid(name, "loader is not present exactly once in its proven data region");
    if (regionLoaders.Count(loader => loader.DataAddress == owner.DataAddress) != 1)
      throw Invalid(name, $"data address {owner.DataAddress} is aliased by another loader");
    var loaderDataField = CheckedAdd(owner.StructAddress, sizeof(uint), name);
    if (!source.TryGetRelocationSource(loaderDataField, out var loaderDataAddress) ||
        loaderDataAddress != owner.DataAddress)
      throw Invalid(name, "loader data field is not relocated to its exact data address");
    if (source.ResourceReferences.ContainsKey(loaderDataField))
      throw Invalid(name, "loader data field conflicts with a SymbolRef");
    if (source.GetOwnedResourceReferences(owner).Count != 0)
      throw Invalid(name, "loader owns SymbolRefs, but installed ModelAnim loaders own none");

    budget.ReserveBytes(HeaderSize, name, "header");
    if (!source.TryReadBytes(owner.DataAddress, HeaderSize, out var bytes) ||
        bytes.Length != HeaderSize)
      throw Invalid(name, "0x58-byte header is outside the archive or truncated");

    var duration = ReadPlainFiniteSingle(bytes, 0, owner.DataAddress, name, source);
    if (duration < 0f) throw Invalid(name, "duration at +0x00 is negative");
    var frameCount = ReadPlainUInt32(bytes, 4, owner.DataAddress, name, source);
    var animatedBoneCount = ReadPlainUInt32(bytes, 0x18, owner.DataAddress, name, source);
    var fullBoneCount = ReadPlainUInt32(bytes, 0x1C, owner.DataAddress, name, source);
    if (frameCount == 0 || frameCount > MaximumFrameCount)
      throw Invalid(name,
        $"frame count {frameCount} is zero or exceeds the limit {MaximumFrameCount}");
    if (animatedBoneCount == 0 || animatedBoneCount > MaximumBoneCount)
      throw Invalid(name,
        $"animated-bone count {animatedBoneCount} is zero or exceeds the limit " +
        $"{MaximumBoneCount}");
    if (fullBoneCount == 0 || fullBoneCount > MaximumBoneCount)
      throw Invalid(name,
        $"full-bone count {fullBoneCount} is zero or exceeds the limit {MaximumBoneCount}");
    if (animatedBoneCount > fullBoneCount)
      throw Invalid(name, "animated-bone count exceeds full-bone count");

    foreach (var offset in ZeroHeaderOffsets) {
      var value = ReadPlainUInt32(bytes, offset, owner.DataAddress, name, source);
      if (value != 0)
        throw Invalid(name, $"opaque header field +0x{offset:X2} is {value} instead of zero");
    }

    var valuesAt08Address = ReadRequiredPointer(
      bytes, 0x08, owner.DataAddress, name, "+0x08 array", source);
    var valuesAt0CAddress = ReadRequiredPointer(
      bytes, 0x0C, owner.DataAddress, name, "+0x0C array", source);
    var triplesAt28Address = ReadRequiredPointer(
      bytes, 0x28, owner.DataAddress, name, "+0x28 triple array", source);
    var rotationsAt2CAddress = ReadRequiredPointer(
      bytes, 0x2C, owner.DataAddress, name, "+0x2C rotation array", source);

    var animatedValueBytes = CheckedByteLength(
      animatedBoneCount, sizeof(uint), name, "+0x08 array");
    var fullValueBytes = CheckedByteLength(
      fullBoneCount, sizeof(uint), name, "+0x0C array");
    if (valuesAt0CAddress != CheckedAdd(valuesAt08Address, animatedValueBytes, name))
      throw Invalid(name, "+0x0C array does not immediately follow the +0x08 array");
    if (triplesAt28Address != CheckedAdd(valuesAt0CAddress, fullValueBytes, name))
      throw Invalid(name, "+0x28 triples do not immediately follow the +0x0C array");

    var tripleCount = Convert.ToUInt64(frameCount) * Convert.ToUInt64(animatedBoneCount);
    if (tripleCount > MaximumTrackElementCount)
      throw Invalid(name,
        $"+0x28 triple count {tripleCount} exceeds the decoder limit " +
        $"{MaximumTrackElementCount}");
    var rotationCount = Convert.ToUInt64(frameCount) * Convert.ToUInt64(fullBoneCount);
    if (rotationCount > MaximumTrackElementCount)
      throw Invalid(name,
        $"+0x2C rotation count {rotationCount} exceeds the decoder limit " +
        $"{MaximumTrackElementCount}");
    var tripleBytes = CheckedByteLength(tripleCount, TripleSize, name, "+0x28 triples");
    var rotationBytes = CheckedByteLength(
      rotationCount, RotationSize, name, "+0x2C rotations");
    var tailBytes = checked(animatedValueBytes + fullValueBytes + tripleBytes);
    _ = CheckedAdd(valuesAt08Address, tailBytes - 1, name);
    _ = CheckedAdd(rotationsAt2CAddress, rotationBytes - 1, name);

    var inlineTailAddress = CheckedAdd(owner.DataAddress, HeaderSize, name);
    if (valuesAt08Address != inlineTailAddress) {
      var nextLoader = regionLoaders
        .Where(loader => loader.DataAddress > owner.DataAddress)
        .OrderBy(loader => loader.DataAddress)
        .FirstOrDefault();
      var endsAtExactBlockBoundary = nextLoader == null &&
        !source.TryReadBytes(owner.DataAddress, HeaderSize + 1, out _);
      if ((nextLoader == null || nextLoader.DataAddress != inlineTailAddress) &&
          !endsAtExactBlockBoundary)
        throw Invalid(name,
          $"out-of-line +0x08 array at {valuesAt08Address} lacks the exact expected " +
          $"next-loader or block-end header boundary {inlineTailAddress}; the next loader is " +
          $"{nextLoader?.DataAddress.ToString() ?? "absent"}");
    }

    budget.ReserveObjects(
      1 + tripleCount + rotationCount + animatedBoneCount + fullBoneCount,
      name,
      "definition, track elements, and names");
    return new ModelAnimationHeader(
      name,
      owner,
      duration,
      frameCount,
      valuesAt08Address,
      valuesAt0CAddress,
      animatedBoneCount,
      fullBoneCount,
      triplesAt28Address,
      rotationsAt2CAddress,
      Convert.ToInt32(tripleCount),
      Convert.ToInt32(rotationCount),
      animatedValueBytes,
      fullValueBytes,
      tripleBytes,
      rotationBytes,
      tailBytes);
  }

  private static ModelAnimationDefinition ReadPayload(
    ModelAnimationHeader header,
    IModelAnimationDataSource source,
    DecodeBudget budget
  ) {
    var name = header.Name;
    budget.ReserveBytes(
      Convert.ToUInt64(header.TailBytes + header.RotationBytes) * 2,
      name,
      "raw and decoded array storage");
    if (!source.TryReadBytes(header.ValuesAt08Address, header.TailBytes, out var tail) ||
        tail.Length != header.TailBytes)
      throw Invalid(name, "contiguous +0x08/+0x0C/+0x28 data is truncated or crosses a block");
    if (!source.TryReadBytes(
          header.RotationsAt2CAddress, header.RotationBytes, out var rotationBytes) ||
        rotationBytes.Length != header.RotationBytes)
      throw Invalid(name, "+0x2C rotation data is truncated or crosses a block");

    var valuesAt08 = ReadZeroArray(
      tail,
      0,
      header.ValuesAt08Address,
      header.AnimatedBoneCount,
      name,
      "+0x08",
      source);
    var valuesAt0C = ReadZeroArray(
      tail,
      header.AnimatedValueBytes,
      header.ValuesAt0CAddress,
      header.FullBoneCount,
      name,
      "+0x0C",
      source);
    var triples = ReadTriples(
      tail,
      header.AnimatedValueBytes + header.FullValueBytes,
      header.TriplesAt28Address,
      header.TripleCount,
      name,
      source);
    var rotations = ReadRotations(
      rotationBytes,
      header.RotationsAt2CAddress,
      header.RotationCount,
      name,
      source);

    if (!source.TryReadExtraData(header.Owner, out var chunks))
      throw Invalid(name, "loader has no owner-bound extra-data chunk");
    if (chunks.Count != 1)
      throw Invalid(name,
        $"loader has {chunks.Count} extra-data chunks instead of the installed count one");
    var chunk = chunks[0];
    if (chunk.Length > MaximumNameChunkBytes)
      throw Invalid(name, $"name chunk exceeds {MaximumNameChunkBytes} bytes");
    budget.ReserveBytes(Convert.ToUInt64(chunk.Length), name, "name chunk");
    var allNames = ReadExactNames(
      chunk,
      checked(Convert.ToInt32(header.AnimatedBoneCount + header.FullBoneCount)),
      name);
    var animatedNames = allNames[..Convert.ToInt32(header.AnimatedBoneCount)];
    var fullNames = allNames[Convert.ToInt32(header.AnimatedBoneCount)..];
    ValidateNames(animatedNames, fullNames, name);

    return new ModelAnimationDefinition(
      name,
      header.Owner.SourcePath,
      header.Owner.DataAddress,
      header.Duration,
      header.FrameCount,
      header.ValuesAt08Address,
      header.ValuesAt0CAddress,
      header.AnimatedBoneCount,
      header.FullBoneCount,
      header.TriplesAt28Address,
      header.RotationsAt2CAddress,
      Array.AsReadOnly(valuesAt08),
      Array.AsReadOnly(valuesAt0C),
      Array.AsReadOnly(triples),
      Array.AsReadOnly(rotations),
      Array.AsReadOnly(animatedNames),
      Array.AsReadOnly(fullNames));
  }

  private static uint[] ReadZeroArray(
    byte[] bytes,
    int sourceOffset,
    uint address,
    uint count,
    string name,
    string offsetName,
    IModelAnimationDataSource source
  ) {
    var values = new uint[Convert.ToInt32(count)];
    foreach (var index in Enumerable.Range(0, values.Length)) {
      var byteOffset = checked(index * sizeof(uint));
      RequirePlainField(
        CheckedAdd(address, byteOffset, name), name, $"{offsetName} value {index}", source);
      var value = BitConverter.ToUInt32(bytes, checked(sourceOffset + byteOffset));
      if (value != 0)
        throw Invalid(name, $"{offsetName} opaque value {index} is {value} instead of zero");
      values[index] = value;
    }
    return values;
  }

  private static ModelAnimationTriple[] ReadTriples(
    byte[] bytes,
    int sourceOffset,
    uint address,
    int count,
    string name,
    IModelAnimationDataSource source
  ) {
    var values = new ModelAnimationTriple[count];
    foreach (var index in Enumerable.Range(0, count)) {
      var byteOffset = checked(index * TripleSize);
      values[index] = new ModelAnimationTriple(
        ReadPlainFiniteSingle(
          bytes, sourceOffset + byteOffset, address, byteOffset, name, source),
        ReadPlainFiniteSingle(
          bytes, sourceOffset + byteOffset + 4, address, byteOffset + 4, name, source),
        ReadPlainFiniteSingle(
          bytes, sourceOffset + byteOffset + 8, address, byteOffset + 8, name, source));
    }
    return values;
  }

  private static ModelAnimationFourTuple[] ReadRotations(
    byte[] bytes,
    uint address,
    int count,
    string name,
    IModelAnimationDataSource source
  ) {
    var values = new ModelAnimationFourTuple[count];
    foreach (var index in Enumerable.Range(0, count)) {
      var byteOffset = checked(index * RotationSize);
      var value = new ModelAnimationFourTuple(
        ReadPlainFiniteSingle(bytes, byteOffset, address, byteOffset, name, source),
        ReadPlainFiniteSingle(bytes, byteOffset + 4, address, byteOffset + 4, name, source),
        ReadPlainFiniteSingle(bytes, byteOffset + 8, address, byteOffset + 8, name, source),
        ReadPlainFiniteSingle(bytes, byteOffset + 12, address, byteOffset + 12, name, source));
      var lengthSquared = Convert.ToDouble(value.Field00) * value.Field00 +
        Convert.ToDouble(value.Field04) * value.Field04 +
        Convert.ToDouble(value.Field08) * value.Field08 +
        Convert.ToDouble(value.Field0C) * value.Field0C;
      if (Math.Abs(lengthSquared - 1.0) > NormalizedLengthSquaredTolerance)
        throw Invalid(name,
          $"+0x2C rotation {index} is not normalized (length squared {lengthSquared})");
      values[index] = value;
    }
    return values;
  }

  private static float ReadPlainFiniteSingle(
    byte[] bytes,
    int offset,
    uint baseAddress,
    string name,
    IModelAnimationDataSource source
  ) => ReadPlainFiniteSingle(bytes, offset, baseAddress, offset, name, source);

  private static float ReadPlainFiniteSingle(
    byte[] bytes,
    int sourceOffset,
    uint baseAddress,
    int addressOffset,
    string name,
    IModelAnimationDataSource source
  ) {
    var fieldAddress = CheckedAdd(baseAddress, addressOffset, name);
    RequirePlainField(fieldAddress, name, $"float at {fieldAddress}", source);
    var value = BitConverter.ToSingle(bytes, sourceOffset);
    if (!float.IsFinite(value))
      throw Invalid(name, $"float at {fieldAddress} is non-finite");
    return value;
  }

  private static uint ReadPlainUInt32(
    byte[] bytes,
    int offset,
    uint baseAddress,
    string name,
    IModelAnimationDataSource source
  ) {
    var fieldAddress = CheckedAdd(baseAddress, offset, name);
    RequirePlainField(fieldAddress, name, $"field +0x{offset:X2}", source);
    return BitConverter.ToUInt32(bytes, offset);
  }

  private static uint ReadRequiredPointer(
    byte[] bytes,
    int offset,
    uint baseAddress,
    string name,
    string description,
    IModelAnimationDataSource source
  ) {
    var fieldAddress = CheckedAdd(baseAddress, offset, name);
    var storedPointer = BitConverter.ToUInt32(bytes, offset);
    if (source.ResourceReferences.ContainsKey(fieldAddress))
      throw Invalid(name, $"{description} conflicts with a SymbolRef");
    if (!source.TryGetRelocationSource(fieldAddress, out var target)) {
      if (storedPointer != 0)
        throw Invalid(name, $"{description} contains an unproven pointer {storedPointer}");
      throw Invalid(name, $"{description} is not a relocated pointer");
    }
    if (target == 0 || storedPointer != target)
      throw Invalid(name, $"{description} does not match its relocation target");
    return target;
  }

  private static string[] ReadExactNames(byte[] bytes, int count, string name) {
    var names = new string[count];
    var offset = 0;
    foreach (var index in Enumerable.Range(0, count)) {
      if (offset >= bytes.Length)
        throw Invalid(name, $"name chunk ends before name {index}");
      var end = Array.IndexOf(bytes, Convert.ToByte(0), offset);
      if (end < 0)
        throw Invalid(name, $"name {index} is not NUL-terminated");
      var length = end - offset;
      if (length == 0 || length > MaximumNameBytes)
        throw Invalid(name, $"name {index} is empty or exceeds {MaximumNameBytes} bytes");
      if (bytes.AsSpan(offset, length).ContainsAnyExceptInRange(
            Convert.ToByte(32), Convert.ToByte(126)))
        throw Invalid(name, $"name {index} contains non-ASCII bytes");
      names[index] = Encoding.ASCII.GetString(bytes, offset, length);
      offset = end + 1;
    }
    if (offset != bytes.Length)
      throw Invalid(name, "name chunk has trailing bytes after the exact name count");
    return names;
  }

  private static void ValidateNames(
    IReadOnlyList<string> animatedNames,
    IReadOnlyList<string> fullNames,
    string name
  ) {
    var animated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var boneName in animatedNames) {
      if (!animated.Add(boneName))
        throw Invalid(name, $"animated-bone name '{boneName}' is duplicated");
    }
    var full = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var boneName in fullNames) {
      if (!full.Add(boneName))
        throw Invalid(name, $"full-bone name '{boneName}' is duplicated");
    }
    foreach (var boneName in animatedNames) {
      if (!full.Contains(boneName))
        throw Invalid(name,
          $"animated-bone name '{boneName}' is absent from the full-bone names");
    }
  }

  private static void ValidateLoaderCoverage(
    IReadOnlyList<ModelAnimationDecodeInput> inputs,
    IReadOnlyList<OvlLoaderEntry> loaders
  ) {
    if (loaders.Count != inputs.Count)
      throw new InvalidDataException(
        $"ModelAnim resource count {inputs.Count} does not match exact loader count " +
        $"{loaders.Count}.");
    var loadersByStruct = new Dictionary<uint, OvlLoaderEntry>();
    foreach (var loader in loaders) {
      if (!loadersByStruct.TryAdd(loader.StructAddress, loader))
        throw new InvalidDataException(
          $"ModelAnim loader struct address {loader.StructAddress} is duplicated.");
    }
    var covered = new HashSet<uint>();
    foreach (var input in inputs) {
      if (!loadersByStruct.TryGetValue(input.Owner.StructAddress, out var loader) ||
          !SameLoader(loader, input.Owner))
        throw Invalid(input.Name, "owner is not one exact ModelAnim loader-table entry");
      if (!covered.Add(input.Owner.StructAddress))
        throw Invalid(input.Name, "loader is assigned to more than one resource");
    }
  }

  private static void ValidateNonOverlappingSpans(IReadOnlyList<ModelAnimationHeader> headers) {
    var spans = new List<ModelAnimationSpan>(headers.Count * 3);
    foreach (var header in headers) {
      spans.Add(CreateSpan(
        header.Owner.DataAddress, HeaderSize, header.Name, "header"));
      spans.Add(CreateSpan(
        header.ValuesAt08Address, header.TailBytes, header.Name, "+0x08/+0x0C/+0x28 data"));
      spans.Add(CreateSpan(
        header.RotationsAt2CAddress,
        header.RotationBytes,
        header.Name,
        "+0x2C rotation data"));
    }
    ModelAnimationSpan? previous = null;
    foreach (var span in spans.OrderBy(span => span.Start).ThenBy(span => span.End)) {
      if (previous != null && span.Start < previous.End)
        throw Invalid(span.Name,
          $"{span.Description} overlaps {previous.Name} {previous.Description}");
      previous = span;
    }
  }

  private static ModelAnimationSpan CreateSpan(
    uint address,
    int length,
    string name,
    string description
  ) => new(
    Convert.ToUInt64(address),
    Convert.ToUInt64(address) + Convert.ToUInt64(length),
    name,
    description);

  private static void RequirePlainField(
    uint fieldAddress,
    string name,
    string description,
    IModelAnimationDataSource source
  ) {
    if (source.TryGetRelocationSource(fieldAddress, out _))
      throw Invalid(name, $"{description} has an unproven relocation");
    if (source.ResourceReferences.ContainsKey(fieldAddress))
      throw Invalid(name, $"{description} conflicts with a SymbolRef");
  }

  private static int CheckedByteLength(
    uint count,
    int stride,
    string name,
    string description
  ) => CheckedByteLength(Convert.ToUInt64(count), stride, name, description);

  private static int CheckedByteLength(
    ulong count,
    int stride,
    string name,
    string description
  ) {
    var length = count * Convert.ToUInt64(stride);
    if (length > int.MaxValue)
      throw Invalid(name, $"{description} byte length exceeds the supported range");
    return Convert.ToInt32(length);
  }

  private static uint CheckedAdd(uint address, int offset, string name) {
    var result = Convert.ToUInt64(address) + Convert.ToUInt64(offset);
    if (result > uint.MaxValue)
      throw Invalid(name, "field address overflowed the OVL address space");
    return Convert.ToUInt32(result);
  }

  private static bool SameLoader(OvlLoaderEntry left, OvlLoaderEntry right) =>
    left.DataAddress == right.DataAddress &&
    left.StructAddress == right.StructAddress &&
    string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase);

  private static string ParseTaggedReference(string reference) {
    var separator = reference.IndexOf(':');
    if (separator <= 0 || separator != reference.LastIndexOf(':') ||
        !reference[(separator + 1)..].Equals(
          "modelanim", StringComparison.OrdinalIgnoreCase))
      throw new ArgumentException(
        $"'{reference}' is not an exact Name:modelanim identity.", nameof(reference));
    var name = reference[..separator];
    if (string.IsNullOrWhiteSpace(name) ||
        !name.Equals(name.Trim(), StringComparison.Ordinal) ||
        name.Contains('/') ||
        name.Contains('\\'))
      throw new ArgumentException(
        $"'{reference}' is not an exact Name:modelanim identity.", nameof(reference));
    return name;
  }

  private static void ValidateResourceCount(Ovl ovl) {
    if (ovl.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"OVL resource count {ovl.Count} exceeds the ModelAnim decoder limit " +
        $"{MaximumResourceCount}.");
  }

  private static void RequireCommonSource(OvlFile file) {
    if (!file.Path.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid(file.Name, "resource is not owned by a common OVL archive");
  }

  private static InvalidDataException Invalid(string name, string message) =>
    new($"Model animation '{name}' is malformed: {message}.");

  private sealed class DecodeBudget {
    private ulong bytes;
    private ulong objects;

    public void ReserveBytes(ulong count, string name, string description) {
      if (count > MaximumDecodedBytes || bytes > MaximumDecodedBytes - count)
        throw Invalid(name,
          $"aggregate decoded bytes exceed {MaximumDecodedBytes} while reading {description}");
      bytes += count;
    }

    public void ReserveObjects(ulong count, string name, string description) {
      if (count > MaximumDecodedObjects || objects > MaximumDecodedObjects - count)
        throw Invalid(name,
          $"aggregate decoded objects exceed {MaximumDecodedObjects} while reading " +
          description);
      objects += count;
    }
  }

  private sealed class OvlModelAnimationDataSource : IModelAnimationDataSource {
    private readonly Ovl ovl;
    private readonly IReadOnlyList<OvlLoaderEntry> loaders;
    private readonly IReadOnlyDictionary<uint, IReadOnlyList<OvlLoaderEntry>> loadersByDataAddress;
    private readonly IReadOnlyDictionary<uint,
      IReadOnlyList<KeyValuePair<uint, OvlSymbolReference>>> referencesByOwner;

    public OvlModelAnimationDataSource(Ovl ovl) {
      this.ovl = ovl;
      if (ovl.LoaderEntriesInOrder.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"OVL loader count {ovl.LoaderEntriesInOrder.Count} exceeds the ModelAnim decoder " +
          $"limit {MaximumResourceCount}.");
      loaders = ovl.LoaderEntriesInOrder.ToArray();
      ModelAnimationLoaders = loaders.Where(loader =>
        loader.Tag.ToFileType() == FileType.ModelAnim).ToArray();

      var mutableLoaders = new Dictionary<uint, List<OvlLoaderEntry>>();
      foreach (var loader in loaders) {
        if (!mutableLoaders.TryGetValue(loader.DataAddress, out var entries))
          mutableLoaders.Add(loader.DataAddress, entries = []);
        entries.Add(loader);
      }
      loadersByDataAddress = mutableLoaders.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<OvlLoaderEntry>)pair.Value.AsReadOnly());

      ResourceReferences = OvlSymbolReferenceIndex.Create(ovl).References;
      var mutableReferences = new Dictionary<uint,
        List<KeyValuePair<uint, OvlSymbolReference>>>();
      foreach (var reference in ResourceReferences) {
        var ownerAddress = reference.Value.Owner.StructAddress;
        if (!mutableReferences.TryGetValue(ownerAddress, out var references))
          mutableReferences.Add(ownerAddress, references = []);
        references.Add(reference);
      }
      referencesByOwner = mutableReferences.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<KeyValuePair<uint, OvlSymbolReference>>)pair.Value.AsReadOnly());
    }

    public IReadOnlyList<OvlLoaderEntry> ModelAnimationLoaders { get; }
    public IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }

    public OvlLoaderEntry GetModelAnimationLoader(OvlFile file, uint address) {
      if (!loadersByDataAddress.TryGetValue(address, out var candidates))
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact modelanim loader-table entry");
      var matches = candidates.Where(loader =>
        loader.Tag.ToFileType() == FileType.ModelAnim &&
        string.Equals(loader.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)).ToList();
      if (matches.Count != 1)
        throw Invalid(file.Name,
          $"address {address} is not owned by one exact modelanim loader-table entry");
      return matches[0];
    }

    public IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner) {
      if (!ovl.TryResolveRelocation(owner.DataAddress, out var ownerBlock, out _)) return [];
      var result = new List<OvlLoaderEntry>();
      foreach (var loader in loaders.Where(loader => string.Equals(
                 loader.SourcePath, owner.SourcePath, StringComparison.OrdinalIgnoreCase))) {
        if (!ovl.TryResolveRelocation(loader.DataAddress, out var candidateBlock, out _) ||
            !ReferenceEquals(ownerBlock, candidateBlock)) continue;
        result.Add(loader);
      }
      return result.AsReadOnly();
    }

    public IReadOnlyList<KeyValuePair<uint, OvlSymbolReference>> GetOwnedResourceReferences(
      OvlLoaderEntry owner
    ) => referencesByOwner.TryGetValue(owner.StructAddress, out var references)
      ? references
      : [];

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      if (ovl.TryReadBytes(address, length, out var resolved)) {
        bytes = resolved;
        return true;
      }
      bytes = [];
      return false;
    }

    public bool TryReadExtraData(
      OvlLoaderEntry owner,
      out IReadOnlyList<byte[]> chunks
    ) {
      if (ovl.TryReadExtraData(owner.DataAddress, out var resolved)) {
        chunks = resolved;
        return true;
      }
      chunks = [];
      return false;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      ovl.TryGetRelocationSource(address, out value);
  }

  private sealed record ModelAnimationHeader(
    string Name,
    OvlLoaderEntry Owner,
    float Duration,
    uint FrameCount,
    uint ValuesAt08Address,
    uint ValuesAt0CAddress,
    uint AnimatedBoneCount,
    uint FullBoneCount,
    uint TriplesAt28Address,
    uint RotationsAt2CAddress,
    int TripleCount,
    int RotationCount,
    int AnimatedValueBytes,
    int FullValueBytes,
    int TripleBytes,
    int RotationBytes,
    int TailBytes
  );

  private sealed record ModelAnimationSpan(
    ulong Start,
    ulong End,
    string Name,
    string Description
  );
}

internal sealed record ModelAnimationDecodeInput(string Name, OvlLoaderEntry Owner);

internal interface IModelAnimationDataSource {
  IReadOnlyList<OvlLoaderEntry> ModelAnimationLoaders { get; }
  IReadOnlyDictionary<uint, OvlSymbolReference> ResourceReferences { get; }
  IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner);
  IReadOnlyList<KeyValuePair<uint, OvlSymbolReference>> GetOwnedResourceReferences(
    OvlLoaderEntry owner);
  bool TryReadBytes(uint address, int length, out byte[] bytes);
  bool TryReadExtraData(OvlLoaderEntry owner, out IReadOnlyList<byte[]> chunks);
  bool TryGetRelocationSource(uint address, out uint value);
}
