// Ride Car Visual Variant Selector
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The two saved ride-car visual sets understood by the native Wild layout.</summary>
internal enum RideCarVisualVariant {
  Normal,
  WildFlipped,
}

/// <summary>The typed, fail-closed outcome for one saved car's visual selection.</summary>
internal enum RideCarVisualVariantSelectionStatus {
  Selected,
  UnresolvedCarResource,
  ChangedCarIdentity,
  UnsupportedVariant,
  UnavailableSerializedVariant,
  MissingRequiredBodyVisual,
  DuplicateExactVisualOccurrence,
  ChangedSerializedVisualIdentity,
  UnresolvedRequiredVisual,
  ChangedRequiredLodIdentity,
  UnresolvedRequiredLod,
}

/// <summary>Typed evidence explaining why a saved car has no usable visual selection.</summary>
internal sealed record RideCarVisualVariantSelectionIssue(
  RideCarVisualVariantSelectionStatus Status,
  RideVisualRole? Role,
  int MatchingOccurrenceCount,
  int UnresolvedLodCount
);

/// <summary>
/// Exact metadata directing moving-part control to the already-selected body visual.
/// </summary>
/// <remarks>
/// This record borrows the body link. It does not clone the link or duplicate its shape geometry.
/// </remarks>
internal sealed record RideCarBodyControlVisualFallback(
  RideCarLink Car,
  RideCarVisualShapeLink Body,
  RideVisualRole MissingMovingRole
);

/// <summary>One saved car composed with an atomic normal or Wild-flipped visual pair.</summary>
internal sealed record RideCarVisualVariantSelectionEntry(
  int RegistryIndex,
  RideCarInstanceRuntimeEntry CarRuntime,
  int? SavedVisualVariant,
  RideCarVisualVariant? SelectedVariant,
  RideVisualRole? RequiredBodyRole,
  RideVisualRole? RequiredMovingRole,
  RideCarLink? Car,
  RideCarVisualShapeLink? Body,
  RideCarVisualShapeLink? Moving,
  RideCarBodyControlVisualFallback? BodyControlFallback,
  RideCarVisualVariantSelectionIssue? Issue
) {
  public ulong CarInstanceEntryId => CarRuntime.CarInstanceEntryId;
  public RideCarVisualVariantSelectionStatus Status =>
    Issue?.Status ?? RideCarVisualVariantSelectionStatus.Selected;
  public bool IsSelected => Issue == null;
  public bool UsesBodyControlFallback => BodyControlFallback != null;
}

/// <summary>Bounded immutable selections in exact saved-car runtime order.</summary>
internal sealed class RideCarVisualVariantSelectionRegistry {
  private readonly HashSet<RideCarVisualShapeLink> authorizedOccurrences;

  internal RideCarVisualVariantSelectionRegistry(
    RideCarVisualVariantSelectionEntry[] entries,
    IReadOnlyList<RideCarVisualShapeLink> authorizedOccurrences
  ) {
    ArgumentNullException.ThrowIfNull(entries);
    ArgumentNullException.ThrowIfNull(authorizedOccurrences);
    entries = (RideCarVisualVariantSelectionEntry[])entries.Clone();
    this.authorizedOccurrences = new(ReferenceEqualityComparer.Instance);
    foreach (var occurrence in authorizedOccurrences) {
      if (occurrence == null)
        throw new ArgumentException(
          "Authorized visual occurrences cannot contain null.",
          nameof(authorizedOccurrences));
      this.authorizedOccurrences.Add(occurrence);
    }
    Entries = Array.AsReadOnly(entries);
    SelectedCount = entries.Count(entry => entry.IsSelected);
    BodyControlFallbackCount = entries.Count(entry => entry.UsesBodyControlFallback);
  }

  public IReadOnlyList<RideCarVisualVariantSelectionEntry> Entries { get; }
  public int SelectedCount { get; }
  public int FailedCount => Entries.Count - SelectedCount;
  public int BodyControlFallbackCount { get; }

  /// <summary>Whether the exact link object came from the selector's bridge input.</summary>
  internal bool IsAuthorizedOccurrence(RideCarVisualShapeLink occurrence) =>
    occurrence != null && authorizedOccurrences.Contains(occurrence);
}

/// <summary>Selects the exact RIC/SVD visual pair named by each saved train variant.</summary>
/// <remarks>
/// <para>
/// A missing saved value and value zero select the normal body and moving-part SymbolRefs. Value
/// one selects the two Wild flipped SymbolRefs as one atomic set. The selector never substitutes
/// normal geometry when a Wild body or LOD is unavailable.
/// </para>
/// <para>
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/car.h">
/// car.h</see> stores the flipped body and moving-part pointers together in <c>RideCar_Wext</c>.
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerRIC.cpp">
/// ManagerRIC</see> reserves and assigns those pointers as distinct optional SVD SymbolRefs. Saved
/// car <c>Reversed</c> is wheel-contact orientation state and does not participate in this choice.
/// </para>
/// </remarks>
internal static class RideCarVisualVariantSelector {
  public static RideCarVisualVariantSelectionRegistry Build(
    RideCarInstanceRuntimeRegistry carRuntime,
    RideCarVisualResourceBridgeResult visualResources
  ) => Build(
    carRuntime,
    visualResources,
    RideCarVisualVariantSelectorLimits.Default);

  internal static RideCarVisualVariantSelectionRegistry Build(
    RideCarInstanceRuntimeRegistry carRuntime,
    RideCarVisualResourceBridgeResult visualResources,
    RideCarVisualVariantSelectorLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(carRuntime);
    ArgumentNullException.ThrowIfNull(visualResources);
    ValidateLimits(limits);
    if (carRuntime.Entries == null)
      throw Invalid("saved-car runtime entry list is null");
    if (visualResources.Visuals == null)
      throw Invalid("visual occurrence list is null");
    ValidateCount(carRuntime.Entries.Count, limits.MaximumCarCount, "saved car");
    ValidateCount(
      visualResources.Visuals.Count,
      limits.MaximumVisualOccurrenceCount,
      "visual occurrence");

    var visualIndex = IndexVisuals(visualResources.Visuals, limits);
    var entries = new RideCarVisualVariantSelectionEntry[carRuntime.Entries.Count];
    foreach (var index in Enumerable.Range(0, entries.Length)) {
      var car = carRuntime.Entries[index];
      if (car == null) throw Invalid($"saved-car runtime entry {index} is null");
      entries[index] = Select(index, car, visualIndex, limits);
    }
    return new(entries, visualResources.Visuals);
  }

  private static RideCarVisualVariantSelectionEntry Select(
    int registryIndex,
    RideCarInstanceRuntimeEntry runtime,
    VisualIndex visuals,
    RideCarVisualVariantSelectorLimits limits
  ) {
    if (runtime.RegistryIndex != registryIndex)
      return Failed(
        registryIndex,
        runtime,
        selectedVariant: null,
        bodyRole: null,
        movingRole: null,
        runtime.CarResource,
        RideCarVisualVariantSelectionStatus.ChangedCarIdentity,
        role: null);

    var savedVariant = runtime.TrainRuntime.SavedVisualVariant;
    var variant = savedVariant switch {
      null or 0 => RideCarVisualVariant.Normal,
      1 => RideCarVisualVariant.WildFlipped,
      _ => (RideCarVisualVariant?)null,
    };
    if (variant == null)
      return Failed(
        registryIndex,
        runtime,
        selectedVariant: null,
        bodyRole: null,
        movingRole: null,
        runtime.CarResource,
        RideCarVisualVariantSelectionStatus.UnsupportedVariant,
        role: null);

    var bodyRole = variant == RideCarVisualVariant.Normal
      ? RideVisualRole.Body
      : RideVisualRole.WildFlippedBody;
    var movingRole = variant == RideCarVisualVariant.Normal
      ? RideVisualRole.Moving
      : RideVisualRole.WildFlippedMoving;
    var car = runtime.CarResource;
    if (!runtime.HasResolvedResource || car == null || runtime.ConsistCar == null ||
        runtime.TrainConsist == null)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.UnresolvedCarResource,
        bodyRole);
    if (!ReferenceEquals(runtime.ConsistCar.CarResource, car) ||
        runtime.TrainConsist.RideGraph == null ||
        runtime.TrainConsist.TrainGraph == null)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.ChangedCarIdentity,
        bodyRole);

    var decodedCar = car.Car;
    if (decodedCar == null)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.ChangedCarIdentity,
        bodyRole);
    string? serializedBody;
    string? serializedMoving;
    if (variant == RideCarVisualVariant.Normal) {
      serializedBody = decodedCar.Visual;
      serializedMoving = decodedCar.MovingVisual;
    } else {
      if (decodedCar.Version != RideCarVersion.Wild || decodedCar.Wild == null)
        return Failed(
          registryIndex,
          runtime,
          variant,
          bodyRole,
          movingRole,
          car,
          RideCarVisualVariantSelectionStatus.UnavailableSerializedVariant,
          bodyRole);
      serializedBody = decodedCar.Wild.FlippedVisual;
      serializedMoving = decodedCar.Wild.FlippedMovingVisual;
    }

    var declaredBodies = DeclaredVisuals(car, bodyRole);
    if (serializedBody == null) {
      var status = declaredBodies.Count == 0
        ? RideCarVisualVariantSelectionStatus.MissingRequiredBodyVisual
        : RideCarVisualVariantSelectionStatus.ChangedSerializedVisualIdentity;
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        status,
        bodyRole,
        declaredBodies.Count);
    }
    if (declaredBodies.Count == 0)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.ChangedSerializedVisualIdentity,
        bodyRole);
    if (declaredBodies.Count != 1)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.DuplicateExactVisualOccurrence,
        bodyRole,
        declaredBodies.Count);
    if (!string.Equals(
          declaredBodies[0].Reference,
          serializedBody,
          StringComparison.OrdinalIgnoreCase))
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.ChangedSerializedVisualIdentity,
        bodyRole,
        matchingOccurrenceCount: 1);
    if (!declaredBodies[0].IsResolved)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.UnresolvedRequiredVisual,
        bodyRole,
        matchingOccurrenceCount: 1);

    var declaredMovingParts = DeclaredVisuals(car, movingRole);
    if (serializedMoving == null && declaredMovingParts.Count != 0)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.ChangedSerializedVisualIdentity,
        movingRole,
        declaredMovingParts.Count);
    if (serializedMoving != null && declaredMovingParts.Count == 0)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.ChangedSerializedVisualIdentity,
        movingRole);
    if (declaredMovingParts.Count > 1)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.DuplicateExactVisualOccurrence,
        movingRole,
        declaredMovingParts.Count);
    if (declaredMovingParts.Count == 1 && !string.Equals(
          declaredMovingParts[0].Reference,
          serializedMoving,
          StringComparison.OrdinalIgnoreCase))
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.ChangedSerializedVisualIdentity,
        movingRole,
        matchingOccurrenceCount: 1);
    if (declaredMovingParts.Count == 1 && !declaredMovingParts[0].IsResolved)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.UnresolvedRequiredVisual,
        movingRole,
        matchingOccurrenceCount: 1);

    if (!visuals.ByExactCar.TryGetValue(car, out var carVisuals)) {
      var status = visuals.LogicalCars.Contains(new(car.Role, car.Reference))
        ? RideCarVisualVariantSelectionStatus.ChangedCarIdentity
        : RideCarVisualVariantSelectionStatus.UnresolvedRequiredVisual;
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        status,
        bodyRole);
    }
    if (carVisuals.HasChangedIdentity ||
        !ReferenceEquals(carVisuals.Ride, runtime.TrainConsist.RideGraph) ||
        !ReferenceEquals(carVisuals.Train, runtime.TrainConsist.TrainGraph))
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.ChangedCarIdentity,
        bodyRole);

    var bodies = carVisuals.Find(bodyRole);
    if (bodies.Count == 0)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.UnresolvedRequiredVisual,
        bodyRole);
    if (bodies.Count != 1)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.DuplicateExactVisualOccurrence,
        bodyRole,
        bodies.Count);
    if (!ReferenceEquals(bodies[0].Visual, declaredBodies[0]))
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.ChangedSerializedVisualIdentity,
        bodyRole,
        matchingOccurrenceCount: 1);

    var movingParts = carVisuals.Find(movingRole);
    if (movingParts.Count > 1)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.DuplicateExactVisualOccurrence,
        movingRole,
        movingParts.Count);
    if (declaredMovingParts.Count == 1 && movingParts.Count == 0)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.UnresolvedRequiredVisual,
        movingRole);
    if (declaredMovingParts.Count == 1 &&
        !ReferenceEquals(movingParts[0].Visual, declaredMovingParts[0]))
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        RideCarVisualVariantSelectionStatus.ChangedSerializedVisualIdentity,
        movingRole,
        matchingOccurrenceCount: 1);

    var bodyLods = ValidateRequiredLods(bodies[0], car, limits);
    if (!bodyLods.IsSelected)
      return Failed(
        registryIndex,
        runtime,
        variant,
        bodyRole,
        movingRole,
        car,
        bodyLods.Status,
        bodyRole,
        matchingOccurrenceCount: 1,
        unresolvedLodCount: bodyLods.UnresolvedLodCount);

    var moving = movingParts.Count == 0 ? null : movingParts[0];
    if (moving != null) {
      var movingLods = ValidateRequiredLods(moving, car, limits);
      if (!movingLods.IsSelected)
        return Failed(
          registryIndex,
          runtime,
          variant,
          bodyRole,
          movingRole,
          car,
          movingLods.Status,
          movingRole,
          matchingOccurrenceCount: 1,
          unresolvedLodCount: movingLods.UnresolvedLodCount);
    }

    var fallback = moving == null
      ? new RideCarBodyControlVisualFallback(car, bodies[0], movingRole)
      : null;
    return new(
      registryIndex,
      runtime,
      savedVariant,
      variant,
      bodyRole,
      movingRole,
      car,
      bodies[0],
      moving,
      fallback,
      Issue: null);
  }

  private static VisualIndex IndexVisuals(
    IReadOnlyList<RideCarVisualShapeLink> visuals,
    RideCarVisualVariantSelectorLimits limits
  ) {
    var byExactCar = new Dictionary<RideCarLink, ExactCarVisualIndex>(
      ReferenceEqualityComparer.Instance);
    var logicalCars = new HashSet<CarIdentity>(CarIdentityComparer.Instance);
    var aggregateLodCount = 0;
    foreach (var link in visuals) {
      if (link == null || link.Car == null || link.Visual == null)
        throw Invalid("visual occurrence list contains an incomplete link");
      if (link.Lods == null)
        throw Invalid("visual occurrence has a null LOD list");
      ValidateCount(link.Lods.Count, limits.MaximumLodsPerVisual, "visual LOD");
      Reserve(
        ref aggregateLodCount,
        link.Lods.Count,
        limits.MaximumAggregateLodCount,
        "aggregate visual LOD");
      if (!byExactCar.TryGetValue(link.Car, out var carVisuals)) {
        carVisuals = new(link.Car);
        byExactCar.Add(link.Car, carVisuals);
      }
      carVisuals.Add(link);
      logicalCars.Add(new(link.Car.Role, link.Car.Reference));
    }
    return new(byExactCar, logicalCars);
  }

  private static RequiredLodValidation ValidateRequiredLods(
    RideCarVisualShapeLink link,
    RideCarLink car,
    RideCarVisualVariantSelectorLimits limits
  ) {
    if (link.Visual.Source?.Resource == null || car.Source?.Resource == null ||
        link.Lods == null)
      return RequiredLodValidation.Changed;
    var visualSource = link.Visual.Source;
    var visual = visualSource.Resource;
    if (!TryParseTaggedReference(link.Visual.Reference, "svd", out var visualName) ||
        visualSource.File.Type != FileType.SceneryItemVisual ||
        !string.Equals(visualSource.File.Name, visual.Name, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(visualName, visual.Name, StringComparison.OrdinalIgnoreCase))
      return RequiredLodValidation.Changed;
    if (!TryClosure(car, out var allowedPaths) ||
        !allowedPaths.Contains(visualSource.File.Path))
      return RequiredLodValidation.Changed;
    if (visual.Lods == null || visual.Lods.Count > limits.MaximumLodsPerVisual)
      return RequiredLodValidation.Changed;

    var linkedIndex = 0;
    var supportedLodCount = 0;
    var unresolvedLodCount = 0;
    foreach (var serializedLod in visual.Lods) {
      if (serializedLod == null) return RequiredLodValidation.Changed;
      switch (serializedLod.Type) {
        case SvdLodType.StaticShape:
        case SvdLodType.BoneShape:
          supportedLodCount = checked(supportedLodCount + 1);
          if (linkedIndex >= link.Lods.Count)
            return RequiredLodValidation.Changed;
          var linkedLod = link.Lods[linkedIndex++];
          if (linkedLod == null || !ReferenceEquals(linkedLod.Lod, serializedLod))
            return RequiredLodValidation.Changed;
          var target = ValidateLodTarget(linkedLod, allowedPaths);
          if (target == LodTargetStatus.Changed)
            return RequiredLodValidation.Changed;
          if (target == LodTargetStatus.Unresolved)
            unresolvedLodCount = checked(unresolvedLodCount + 1);
          break;
        case SvdLodType.Billboard:
          if (serializedLod.StaticShapeRef != null || serializedLod.BoneShapeRef != null)
            return RequiredLodValidation.Changed;
          break;
        default:
          return RequiredLodValidation.Changed;
      }
    }
    if (linkedIndex != link.Lods.Count)
      return RequiredLodValidation.Changed;
    if (supportedLodCount == 0)
      return new(
        RideCarVisualVariantSelectionStatus.UnresolvedRequiredLod,
        UnresolvedLodCount: 1);
    return unresolvedLodCount == 0
      ? RequiredLodValidation.Selected
      : new(
        RideCarVisualVariantSelectionStatus.UnresolvedRequiredLod,
        unresolvedLodCount);
  }

  private static LodTargetStatus ValidateLodTarget(
    RideVisualShapeLodLink linked,
    IReadOnlySet<string> allowedPaths
  ) {
    var lod = linked.Lod;
    if (linked.StaticShapeSource != null && linked.BoneShapeSource != null)
      return LodTargetStatus.Changed;
    switch (lod.Type) {
      case SvdLodType.StaticShape:
        if (lod.BoneShapeRef != null || linked.BoneShapeSource != null ||
            !TryParseTaggedReference(lod.StaticShapeRef, "shs", out var staticName))
          return LodTargetStatus.Changed;
        if (linked.StaticShapeSource == null) return LodTargetStatus.Unresolved;
        return ValidShapeSource(
          linked.StaticShapeSource.File,
          linked.StaticShapeSource.Resource?.Name,
          FileType.StaticShape,
          staticName,
          allowedPaths)
          ? LodTargetStatus.Resolved
          : LodTargetStatus.Changed;
      case SvdLodType.BoneShape:
        if (lod.StaticShapeRef != null || linked.StaticShapeSource != null ||
            !TryParseTaggedReference(lod.BoneShapeRef, "bsh", out var boneName))
          return LodTargetStatus.Changed;
        if (linked.BoneShapeSource == null) return LodTargetStatus.Unresolved;
        return ValidShapeSource(
          linked.BoneShapeSource.File,
          linked.BoneShapeSource.Resource?.Name,
          FileType.BoneShape,
          boneName,
          allowedPaths)
          ? LodTargetStatus.Resolved
          : LodTargetStatus.Changed;
      default:
        return LodTargetStatus.Changed;
    }
  }

  private static bool ValidShapeSource(
    OvlFile? file,
    string? resourceName,
    FileType expectedType,
    string referencedName,
    IReadOnlySet<string> allowedPaths
  ) => file != null &&
    resourceName != null &&
    file.Type == expectedType &&
    string.Equals(file.Name, resourceName, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(referencedName, resourceName, StringComparison.OrdinalIgnoreCase) &&
    allowedPaths.Contains(file.Path);

  private static bool TryClosure(
    RideCarLink car,
    out IReadOnlySet<string> allowedPaths
  ) {
    var source = car.Source;
    if (source?.Resource == null || source.AllowedArchivePaths == null ||
        source.File.Type != FileType.RideCar ||
        !string.Equals(source.File.Name, source.Resource.Name,
          StringComparison.OrdinalIgnoreCase) ||
        !TryParseTaggedReference(car.Reference, "ric", out var carName) ||
        !string.Equals(carName, source.Resource.Name, StringComparison.OrdinalIgnoreCase)) {
      allowedPaths = null!;
      return false;
    }
    var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in source.AllowedArchivePaths)
      if (string.IsNullOrWhiteSpace(path) || !paths.Add(path)) {
        allowedPaths = null!;
        return false;
      }
    if (!paths.Contains(source.File.Path)) {
      allowedPaths = null!;
      return false;
    }
    allowedPaths = paths;
    return true;
  }

  private static bool TryParseTaggedReference(
    string? reference,
    string expectedTag,
    out string name
  ) {
    name = string.Empty;
    if (string.IsNullOrWhiteSpace(reference) ||
        !string.Equals(reference, reference.Trim(), StringComparison.Ordinal))
      return false;
    var separator = reference.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals(expectedTag, StringComparison.OrdinalIgnoreCase))
      return false;
    name = reference[..separator];
    return !string.IsNullOrWhiteSpace(name) &&
      string.Equals(name, name.Trim(), StringComparison.Ordinal);
  }

  private static IReadOnlyList<RideVisualLink> DeclaredVisuals(
    RideCarLink car,
    RideVisualRole role
  ) {
    if (car.Visuals == null) throw Invalid($"RIC '{car.Reference}' visual list is null");
    return car.Visuals.Where(visual => visual != null && visual.Role == role).ToArray();
  }

  private static RideCarVisualVariantSelectionEntry Failed(
    int registryIndex,
    RideCarInstanceRuntimeEntry runtime,
    RideCarVisualVariant? selectedVariant,
    RideVisualRole? bodyRole,
    RideVisualRole? movingRole,
    RideCarLink? car,
    RideCarVisualVariantSelectionStatus status,
    RideVisualRole? role,
    int matchingOccurrenceCount = 0,
    int unresolvedLodCount = 0
  ) => new(
    registryIndex,
    runtime,
    runtime.TrainRuntime.SavedVisualVariant,
    selectedVariant,
    bodyRole,
    movingRole,
    car,
    Body: null,
    Moving: null,
    BodyControlFallback: null,
    new(status, role, matchingOccurrenceCount, unresolvedLodCount));

  private static bool ContainsReference<T>(IReadOnlyList<T> values, T target)
    where T : class {
    foreach (var value in values)
      if (ReferenceEquals(value, target)) return true;
    return false;
  }

  private static void ValidateLimits(RideCarVisualVariantSelectorLimits limits) {
    if (limits.MaximumCarCount < 0 ||
        limits.MaximumVisualOccurrenceCount < 0 ||
        limits.MaximumLodsPerVisual < 0 ||
        limits.MaximumAggregateLodCount < 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count > maximum)
      throw new InvalidOperationException(
        $"Ride-car visual variant {description} count exceeds the limit {maximum}.");
  }

  private static void Reserve(
    ref int total,
    int addition,
    int maximum,
    string description
  ) {
    if (addition > maximum || total > maximum - addition)
      throw new InvalidOperationException(
        $"Ride-car visual variant {description} count exceeds the limit {maximum}.");
    total += addition;
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-car visual variant selection input is invalid: {message}.");

  private readonly record struct CarIdentity(RideTrainCarRole Role, string Reference);

  private sealed class CarIdentityComparer : IEqualityComparer<CarIdentity> {
    public static CarIdentityComparer Instance { get; } = new();

    public bool Equals(CarIdentity left, CarIdentity right) =>
      left.Role == right.Role &&
      string.Equals(left.Reference, right.Reference, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(CarIdentity value) => HashCode.Combine(
      value.Role,
      StringComparer.OrdinalIgnoreCase.GetHashCode(value.Reference ?? string.Empty));
  }

  private readonly record struct RequiredLodValidation(
    RideCarVisualVariantSelectionStatus Status,
    int UnresolvedLodCount
  ) {
    public static RequiredLodValidation Selected { get; } = new(
      RideCarVisualVariantSelectionStatus.Selected,
      0);
    public static RequiredLodValidation Changed { get; } = new(
      RideCarVisualVariantSelectionStatus.ChangedRequiredLodIdentity,
      0);
    public bool IsSelected => Status == RideCarVisualVariantSelectionStatus.Selected;
  }

  private enum LodTargetStatus {
    Resolved,
    Unresolved,
    Changed,
  }

  private sealed record VisualIndex(
    IReadOnlyDictionary<RideCarLink, ExactCarVisualIndex> ByExactCar,
    IReadOnlySet<CarIdentity> LogicalCars);

  private sealed class ExactCarVisualIndex {
    private static readonly IReadOnlyList<RideCarVisualShapeLink> Empty =
      Array.AsReadOnly(Array.Empty<RideCarVisualShapeLink>());
    private readonly Dictionary<RideVisualRole, List<RideCarVisualShapeLink>> byRole = [];
    private readonly RideCarLink car;

    public ExactCarVisualIndex(RideCarLink car) {
      this.car = car;
    }

    public TrackedRideResourceLink? Ride { get; private set; }
    public RideTrainLink? Train { get; private set; }
    public bool HasChangedIdentity { get; private set; }

    public void Add(RideCarVisualShapeLink link) {
      if (Ride == null) {
        Ride = link.Ride;
        Train = link.Train;
      } else if (!ReferenceEquals(Ride, link.Ride) || !ReferenceEquals(Train, link.Train)) {
        HasChangedIdentity = true;
      }
      if (!ReferenceEquals(link.Car, car) || !ContainsReference(car.Visuals, link.Visual))
        HasChangedIdentity = true;
      if (!byRole.TryGetValue(link.Visual.Role, out var matches)) {
        matches = [];
        byRole.Add(link.Visual.Role, matches);
      }
      matches.Add(link);
    }

    public IReadOnlyList<RideCarVisualShapeLink> Find(RideVisualRole role) =>
      byRole.TryGetValue(role, out var matches) ? matches : Empty;
  }
}

/// <summary>Allocation and traversal ceilings for visual-variant selection.</summary>
internal readonly record struct RideCarVisualVariantSelectorLimits(
  int MaximumCarCount,
  int MaximumVisualOccurrenceCount,
  int MaximumLodsPerVisual,
  int MaximumAggregateLodCount
) {
  public static RideCarVisualVariantSelectorLimits Default { get; } = new(
    MaximumCarCount: 1_000_000,
    MaximumVisualOccurrenceCount: 1_000_000,
    MaximumLodsPerVisual: 4 * 1024,
    MaximumAggregateLodCount: 4_000_000);
}
