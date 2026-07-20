// Ride Car Peep Slot Evidence
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>Exact body-shape evidence for one RIC occurrence's passenger-slot capacity.</summary>
internal sealed record RideCarPeepSlotEvidence(
  RideCarLink Car,
  RideCarVisualShapeLink BodyVisual,
  int PeepSlotCount,
  IReadOnlyList<RideVisualShapeLodLink> MarkerLods
);

/// <summary>Indexes native <c>Peep01</c>, <c>Peep02</c>, ... body-bone markers by RIC edge.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> at <c>0x00A4BF60</c> formats sequential Peep names and asks
/// the ride-car body bone lookup for each one until the first miss. The pinned
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/car.h">
/// RIC layout</see> instead defines <c>seating</c> as a seating-mode enum and
/// <c>seat_type_count</c> as alternate seating positions. The corresponding
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerRIC.cpp">
/// serializer</see> writes a zero alternate count for a single seating mode, so neither field is a
/// passenger-slot count. Lower-detail body LODs may omit Peep markers; positive counts must agree.
/// </remarks>
internal sealed class RideCarPeepSlotEvidenceIndex {
  private readonly IReadOnlyDictionary<RideCarLink, RideCarPeepSlotEvidence> byCar;

  public IReadOnlyList<RideCarPeepSlotEvidence> Evidence { get; }

  private RideCarPeepSlotEvidenceIndex(RideCarPeepSlotEvidence[] evidence) {
    Evidence = Array.AsReadOnly(evidence);
    var indexed = new Dictionary<RideCarLink, RideCarPeepSlotEvidence>(
      ReferenceEqualityComparer.Instance);
    foreach (var item in evidence) indexed.Add(item.Car, item);
    byCar = indexed;
  }

  public static RideCarPeepSlotEvidenceIndex Build(
    RideCarVisualResourceBridgeResult visuals
  ) => Build(visuals, RideCarPeepSlotEvidenceLimits.Default);

  internal static RideCarPeepSlotEvidenceIndex Build(
    RideCarVisualResourceBridgeResult visuals,
    RideCarPeepSlotEvidenceLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(visuals);
    ValidateLimits(limits);
    if (visuals.Visuals == null) throw Invalid("visual list is null");
    if (visuals.Visuals.Count > limits.MaximumVisualCount)
      throw Invalid($"visual count exceeds the limit {limits.MaximumVisualCount}");
    if (visuals.UnresolvedShapeReferenceCount < 0)
      throw Invalid("unresolved shape-reference count is negative");

    var bodyCars = new HashSet<RideCarLink>(ReferenceEqualityComparer.Instance);
    var evidence = new List<RideCarPeepSlotEvidence>();
    foreach (var visual in visuals.Visuals) {
      if (visual == null) throw Invalid("visual list contains null");
      if (visual.Visual == null || visual.Visual.Role != RideVisualRole.Body) continue;
      ValidateBodyIdentity(visual);
      if (!bodyCars.Add(visual.Car))
        throw Invalid($"RIC '{visual.Car.Reference}' has duplicate body-visual evidence");
      var resolved = Resolve(visual, limits);
      if (resolved != null) evidence.Add(resolved);
    }
    return new RideCarPeepSlotEvidenceIndex(evidence.ToArray());
  }

  public bool TryGet(
    RideCarLink car,
    out RideCarPeepSlotEvidence evidence
  ) {
    ArgumentNullException.ThrowIfNull(car);
    return byCar.TryGetValue(car, out evidence!);
  }

  private static RideCarPeepSlotEvidence? Resolve(
    RideCarVisualShapeLink body,
    RideCarPeepSlotEvidenceLimits limits
  ) {
    if (body.Lods == null) throw Invalid($"RIC '{body.Car.Reference}' body LOD list is null");
    if (body.Lods.Count == 0) return null;
    if (body.Lods.Count > limits.MaximumLodCount)
      throw Invalid(
        $"RIC '{body.Car.Reference}' body LOD count exceeds the limit {limits.MaximumLodCount}");

    int? positiveCount = null;
    var markerLods = new List<RideVisualShapeLodLink>();
    foreach (var lod in body.Lods) {
      if (lod == null) throw Invalid($"RIC '{body.Car.Reference}' body LOD list contains null");
      if (!lod.IsResolved) return null;
      if (lod.StaticShapeSource != null && lod.BoneShapeSource != null)
        throw Invalid($"RIC '{body.Car.Reference}' body LOD resolves both SHS and BSH");
      if (lod.BoneShapeSource == null) continue;

      ValidateBoneSource(body.Car, lod.BoneShapeSource);
      var count = CountMarkers(lod.BoneShapeSource.Resource, limits);
      if (count == 0) continue;
      if (positiveCount != null && positiveCount.Value != count)
        throw Invalid(
          $"RIC '{body.Car.Reference}' body LODs disagree on Peep-slot capacity " +
          $"{positiveCount.Value} versus {count}");
      positiveCount = count;
      markerLods.Add(lod);
    }

    return new RideCarPeepSlotEvidence(
      body.Car,
      body,
      positiveCount ?? 0,
      Array.AsReadOnly(markerLods.ToArray()));
  }

  private static int CountMarkers(
    BoneShape shape,
    RideCarPeepSlotEvidenceLimits limits
  ) {
    if (shape.Bones == null) throw Invalid($"BSH '{shape.Name}' bone list is null");
    if (shape.Bones.Count > limits.MaximumBoneCount)
      throw Invalid($"BSH '{shape.Name}' bone count exceeds the limit {limits.MaximumBoneCount}");

    var markers = new HashSet<int>();
    foreach (var bone in shape.Bones) {
      if (bone == null) throw Invalid($"BSH '{shape.Name}' bone list contains null");
      if (string.IsNullOrWhiteSpace(bone.Name))
        throw Invalid($"BSH '{shape.Name}' contains a nameless bone");
      if (!TryParsePeepMarker(bone.Name, limits, out var number)) continue;
      if (!markers.Add(number))
        throw Invalid($"BSH '{shape.Name}' duplicates Peep marker {number}");
    }
    if (markers.Count == 0) return 0;

    var maximum = markers.Max();
    if (markers.Count != maximum)
      throw Invalid($"BSH '{shape.Name}' Peep markers are not contiguous from Peep01");
    return maximum;
  }

  private static bool TryParsePeepMarker(
    string name,
    RideCarPeepSlotEvidenceLimits limits,
    out int number
  ) {
    number = 0;
    if (!name.StartsWith("Peep", StringComparison.OrdinalIgnoreCase)) return false;
    var suffix = name.AsSpan(4);
    if (suffix.Length == 0) return false;
    foreach (var character in suffix)
      if (character is < '0' or > '9') return false;
    if (!int.TryParse(suffix, out number) || number <= 0 || number > limits.MaximumPeepSlotCount)
      throw Invalid($"Peep marker '{name}' is outside 1 through {limits.MaximumPeepSlotCount}");

    var canonical = number < 10 ? $"Peep0{number}" : $"Peep{number}";
    if (!string.Equals(name, canonical, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"Peep marker '{name}' is not the native canonical name '{canonical}'");
    return true;
  }

  private static void ValidateBodyIdentity(RideCarVisualShapeLink body) {
    if (body.Ride == null || body.Train == null || body.Car == null)
      throw Invalid("body visual has an incomplete graph identity");
    if (!body.Car.IsResolved || body.Car.Source?.Resource == null)
      throw Invalid($"RIC '{body.Car.Reference}' body visual has no resolved car source");
    if (!body.Visual.IsResolved || body.Visual.Source?.Resource == null)
      throw Invalid($"RIC '{body.Car.Reference}' body visual has no resolved SVD source");
    if (!body.Car.Visuals.Any(visual => ReferenceEquals(visual, body.Visual)))
      throw Invalid($"RIC '{body.Car.Reference}' body visual changed graph object identity");
    if (!string.Equals(
      body.Car.Car!.Visual,
      body.Visual.Reference,
      StringComparison.OrdinalIgnoreCase))
      throw Invalid($"RIC '{body.Car.Reference}' body SVD reference changed identity");
  }

  private static void ValidateBoneSource(
    RideCarLink car,
    RideBoneShapeResourceSource source
  ) {
    if (source.File == null || source.Resource == null)
      throw Invalid($"RIC '{car.Reference}' has incomplete BSH evidence");
    if (source.File.Type != FileType.BoneShape)
      throw Invalid($"RIC '{car.Reference}' body shape is not a BSH resource");
    if (!string.Equals(
      source.File.Name,
      source.Resource.Name,
      StringComparison.OrdinalIgnoreCase))
      throw Invalid($"RIC '{car.Reference}' BSH file and decoded names disagree");
    if (car.Source?.AllowedArchivePaths == null ||
        !car.Source.AllowedArchivePaths.Contains(
          source.File.Path,
          StringComparer.OrdinalIgnoreCase))
      throw Invalid($"RIC '{car.Reference}' BSH is outside its exact archive closure");
  }

  private static void ValidateLimits(RideCarPeepSlotEvidenceLimits limits) {
    if (limits.MaximumVisualCount <= 0 ||
        limits.MaximumLodCount <= 0 ||
        limits.MaximumBoneCount <= 0 ||
        limits.MaximumPeepSlotCount <= 0 ||
        limits.MaximumPeepSlotCount > limits.MaximumBoneCount)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid ride-car Peep-slot evidence: {message}.");
}

internal readonly record struct RideCarPeepSlotEvidenceLimits(
  int MaximumVisualCount,
  int MaximumLodCount,
  int MaximumBoneCount,
  int MaximumPeepSlotCount
) {
  public static RideCarPeepSlotEvidenceLimits Default { get; } =
    new(1_000_000, 1_024, 65_536, 16_384);
}
