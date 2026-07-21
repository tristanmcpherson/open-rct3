// Ride Car Visual Hierarchy Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The exact decoded shape that owns one native ride-car hierarchy anchor.</summary>
internal enum RideCarVisualHierarchyAnchorSource {
  Body,
  FrontAxle,
  RearAxle,
}

/// <summary>A typed result for one optional RIC axle or wheel visual.</summary>
internal enum RideCarVisualHierarchyPartStatus {
  Resolved,
  VisualNotDeclared,
  AnimalSpeciesBodyUnsupported,
  VisualUnresolved,
  AxleVisualUnresolved,
  AxleAnchorUnavailable,
  AnchorShapeUnavailable,
  AnchorMissing,
  AnchorAmbiguous,
}

/// <summary>An exact named BSH bone plus the SVD LOD and OVL source that own it.</summary>
internal sealed record RideCarVisualHierarchyAnchor(
  RideCarVisualHierarchyAnchorSource Source,
  RideCarVisualShapeLink Visual,
  RideVisualShapeLodLink Lod,
  RideBoneShapeResourceSource ShapeSource,
  int BoneIndex,
  BoneShapeBone Bone
);

/// <summary>One optional RIC visual and its exact native hierarchy anchor when available.</summary>
/// <remarks>
/// <see cref="Type"/> retains the serialized axle mode or wheel parity value. It is deliberately
/// not interpreted as a visibility flag or transformed by this resource-only layer.
/// </remarks>
internal sealed record RideCarVisualHierarchyPart(
  RideVisualRole Role,
  string? SerializedVisualReference,
  uint Type,
  RideVisualLink? Visual,
  RideCarVisualShapeLink? ShapeVisual,
  RideCarVisualHierarchyPartStatus Status,
  RideCarVisualHierarchyAnchor? Anchor
) {
  public bool IsResolved =>
    Status == RideCarVisualHierarchyPartStatus.Resolved && Anchor != null;
}

/// <summary>
/// Resolved visual hierarchy evidence for one exact ride/train/car/body-role occurrence.
/// </summary>
internal sealed record RideCarVisualHierarchyResolution(
  TrackedRideResourceLink Ride,
  RideTrainLink Train,
  RideCarLink Car,
  RideVisualRole BodyRole,
  RideCarVisualHierarchyPartStatus BodyStatus,
  RideVisualLink? BodyVisual,
  RideCarVisualShapeLink? BodyShapeVisual,
  RideCarVisualHierarchyPart FrontAxle,
  RideCarVisualHierarchyPart RearAxle,
  RideCarVisualHierarchyPart FrontRightWheel,
  RideCarVisualHierarchyPart FrontLeftWheel,
  RideCarVisualHierarchyPart BackRightWheel,
  RideCarVisualHierarchyPart BackLeftWheel
) {
  public IReadOnlyList<RideCarVisualHierarchyPart> Parts => [
    FrontAxle,
    RearAxle,
    FrontRightWheel,
    FrontLeftWheel,
    BackRightWheel,
    BackLeftWheel,
  ];
}

/// <summary>
/// Bounded visual-hierarchy evidence indexed by exact RIC and selected body-role identity.
/// </summary>
internal sealed class RideCarVisualHierarchyRegistry {
  private const int PartSlotsPerBodyRole = 6;
  private readonly IReadOnlyDictionary<
    RideCarLink,
    IReadOnlyDictionary<RideVisualRole, RideCarVisualHierarchyResolution>> byCarAndBodyRole;

  internal RideCarVisualHierarchyRegistry(
    RideCarVisualHierarchyResolution[] cars,
    int resolvedPartCount,
    int ambiguousPartCount,
    bool coversAllDecodedCarOccurrences,
    int unresolvedCarReferenceCount
  ) {
    ArgumentNullException.ThrowIfNull(cars);
    var countedResolvedPartCount = 0;
    var countedAmbiguousPartCount = 0;
    var declaredBodyRolePartCount = 0;
    foreach (var hierarchy in cars) {
      if (hierarchy == null)
        throw new InvalidDataException(
          "Ride-car hierarchy registry contains a null body-role resolution");
      if (hierarchy.Parts.Count != PartSlotsPerBodyRole)
        throw new InvalidDataException(
          $"Ride-car hierarchy has {hierarchy.Parts.Count} parts instead of " +
          $"{PartSlotsPerBodyRole}");
      foreach (var part in hierarchy.Parts) {
        if (part == null)
          throw new InvalidDataException("Ride-car hierarchy registry contains a null part");
        if (part.SerializedVisualReference == null) {
          if (part.Status != RideCarVisualHierarchyPartStatus.VisualNotDeclared ||
              part.Visual != null ||
              part.ShapeVisual != null ||
              part.Anchor != null)
            throw new InvalidDataException(
              $"Undeclared ride-car {part.Role} part retains resolution evidence");
          continue;
        }
        if (part.Status == RideCarVisualHierarchyPartStatus.VisualNotDeclared)
          throw new InvalidDataException(
            $"Declared ride-car {part.Role} part is marked undeclared");
        declaredBodyRolePartCount = checked(declaredBodyRolePartCount + 1);
        if (part.IsResolved)
          countedResolvedPartCount = checked(countedResolvedPartCount + 1);
        if (part.Status == RideCarVisualHierarchyPartStatus.AnchorAmbiguous)
          countedAmbiguousPartCount = checked(countedAmbiguousPartCount + 1);
      }
    }
    if (resolvedPartCount != countedResolvedPartCount)
      throw new InvalidDataException(
        "Ride-car hierarchy resolved-part count does not match its typed results");
    if (ambiguousPartCount != countedAmbiguousPartCount)
      throw new InvalidDataException(
        "Ride-car hierarchy ambiguous-part count does not match its typed results");

    Cars = Array.AsReadOnly(cars);
    ResolvedPartCount = resolvedPartCount;
    AmbiguousPartCount = ambiguousPartCount;
    BodyRolePartSlotCount = checked(cars.Length * PartSlotsPerBodyRole);
    DeclaredBodyRolePartCount = declaredBodyRolePartCount;
    UndeclaredBodyRolePartCount = checked(
      BodyRolePartSlotCount - DeclaredBodyRolePartCount);
    UnresolvedDeclaredBodyRolePartCount = checked(
      DeclaredBodyRolePartCount - ResolvedPartCount);
    CoversAllDecodedCarOccurrences = coversAllDecodedCarOccurrences;
    UnresolvedCarReferenceCount = unresolvedCarReferenceCount;
    var mutableIndex = new Dictionary<
      RideCarLink,
      Dictionary<RideVisualRole, RideCarVisualHierarchyResolution>>(
      ReferenceEqualityComparer.Instance);
    foreach (var car in cars) {
      if (!mutableIndex.TryGetValue(car.Car, out var bodyRoles)) {
        bodyRoles = [];
        mutableIndex.Add(car.Car, bodyRoles);
      }
      bodyRoles.Add(car.BodyRole, car);
    }
    var index = new Dictionary<
      RideCarLink,
      IReadOnlyDictionary<RideVisualRole, RideCarVisualHierarchyResolution>>(
        ReferenceEqualityComparer.Instance);
    foreach (var pair in mutableIndex) index.Add(pair.Key, pair.Value);
    byCarAndBodyRole = index;
  }

  public IReadOnlyList<RideCarVisualHierarchyResolution> Cars { get; }
  /// <summary>
  /// Total optional axle and wheel slots across the exact car/body-role resolutions in
  /// <see cref="Cars"/>. A RIC with both normal and wild-flipped bodies contributes two six-slot
  /// sets; this is not a unique serialized-RIC count.
  /// </summary>
  public int BodyRolePartSlotCount { get; }
  /// <summary>
  /// Body-role part slots backed by an explicit serialized SVD reference. One serialized reference
  /// is counted once for each body-role resolution that consumes it.
  /// </summary>
  public int DeclaredBodyRolePartCount { get; }
  /// <summary>Declared body-role part slots resolved through their exact resource evidence.</summary>
  public int ResolvedPartCount { get; }
  /// <summary>Declared body-role part slots that could not be resolved.</summary>
  public int UnresolvedDeclaredBodyRolePartCount { get; }
  /// <summary>Optional body-role part slots for which the RIC serialized no visual reference.</summary>
  public int UndeclaredBodyRolePartCount { get; }
  /// <summary>Body-role part slots whose exact anchor lookup was ambiguous.</summary>
  public int AmbiguousPartCount { get; }
  /// <summary>
  /// Whether a supplied ride graph proved coverage of every decoded RIC occurrence, including cars
  /// whose SVD edges are all unresolved.
  /// </summary>
  public bool CoversAllDecodedCarOccurrences { get; }
  /// <summary>
  /// Number of exact car edges whose RIC resource itself was unresolved and therefore exposed no
  /// trustworthy visual references or type values to resolve.
  /// </summary>
  public int UnresolvedCarReferenceCount { get; }
  /// <summary>Unresolved declared plus optional undeclared body-role part slots.</summary>
  public int UnavailablePartCount =>
    checked(UnresolvedDeclaredBodyRolePartCount + UndeclaredBodyRolePartCount);

  public bool TryGet(
    RideCarLink car,
    out RideCarVisualHierarchyResolution resolution
  ) {
    ArgumentNullException.ThrowIfNull(car);
    return TryGet(car, RideVisualRole.Body, out resolution);
  }

  public bool TryGet(
    RideCarLink car,
    RideVisualRole bodyRole,
    out RideCarVisualHierarchyResolution resolution
  ) {
    ArgumentNullException.ThrowIfNull(car);
    if (bodyRole is not RideVisualRole.Body and not RideVisualRole.WildFlippedBody)
      throw new ArgumentOutOfRangeException(nameof(bodyRole));
    if (byCarAndBodyRole.TryGetValue(car, out var bodyRoles) &&
        bodyRoles.TryGetValue(bodyRole, out resolution!))
      return true;
    resolution = null!;
    return false;
  }
}

/// <summary>
/// Resolves native axle and wheel anchor relationships without creating render objects.
/// </summary>
/// <remarks>
/// The pinned
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/car.h#L135-L154">
/// RIC layout</see> stores four independent wheel SVDs, two axle SVDs, two axle integer modes, and
/// four wheel parity values. Its
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerRIC.cpp#L323-L338">
/// serializer</see> assigns each reference independently. Complete Edition <c>RCT3.exe</c> first
/// seeks body <c>AxleF</c>/<c>AxleR</c> anchors, with legacy <c>AxelF</c>/<c>AxelR</c> fallbacks.
/// Wheel visuals prefer <c>WheelR</c>/<c>WheelL</c> on a usable axle shape and otherwise use the
/// corresponding body <c>WheelFR</c>/<c>WheelFL</c>/<c>WheelRR</c>/<c>WheelRL</c> anchor. Ambiguous
/// case-insensitive names and declared-but-unresolved axle identities fail closed and never
/// authorize the body fallback. Normal and Wild-flipped body SVDs produce separate keyed results;
/// their anchors are never substituted for one another.
/// Wild animal-species cars without a body SVD retain a typed unsupported body result; their WAS
/// reference is never represented as an SVD or shape hierarchy.
///
/// Anchor evidence uses the first resolved BSH LOD in serialized order. This preserves an exact
/// source without pretending to select the renderer's distance-dependent LOD; the retained
/// <see cref="RideCarVisualHierarchyAnchor.Lod"/> lets that later layer verify the same identity.
/// </remarks>
internal static class RideCarVisualHierarchyResolver {
  private const string FrontAxleName = "AxleF";
  private const string LegacyFrontAxleName = "AxelF";
  private const string RearAxleName = "AxleR";
  private const string LegacyRearAxleName = "AxelR";
  private const string RightWheelName = "WheelR";
  private const string LeftWheelName = "WheelL";
  private const string FrontRightWheelName = "WheelFR";
  private const string FrontLeftWheelName = "WheelFL";
  private const string RearRightWheelName = "WheelRR";
  private const string RearLeftWheelName = "WheelRL";

  /// <summary>
  /// Resolves occurrences represented by at least one resolved bridge SVD. Use the graph overload
  /// when complete decoded-car coverage is required.
  /// </summary>
  public static RideCarVisualHierarchyRegistry Resolve(
    RideCarVisualResourceBridgeResult visuals
  ) => Resolve(visuals, RideCarVisualHierarchyResolverLimits.Default);

  /// <summary>
  /// Resolves every decoded RIC occurrence in the exact graph, including occurrences with no
  /// resolved bridge visual. Unresolved RIC references remain counted because their fields and
  /// integer values cannot be fabricated safely.
  /// </summary>
  public static RideCarVisualHierarchyRegistry Resolve(
    RideResourceGraph graph,
    RideCarVisualResourceBridgeResult visuals
  ) => Resolve(graph, visuals, RideCarVisualHierarchyResolverLimits.Default);

  internal static RideCarVisualHierarchyRegistry Resolve(
    RideCarVisualResourceBridgeResult visuals,
    RideCarVisualHierarchyResolverLimits limits
  ) => ResolveCore(null, visuals, limits);

  internal static RideCarVisualHierarchyRegistry Resolve(
    RideResourceGraph graph,
    RideCarVisualResourceBridgeResult visuals,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(graph);
    return ResolveCore(graph, visuals, limits);
  }

  private static RideCarVisualHierarchyRegistry ResolveCore(
    RideResourceGraph? graph,
    RideCarVisualResourceBridgeResult visuals,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(visuals);
    ValidateLimits(limits);
    if (visuals.Visuals == null) throw Invalid("visual occurrence list is null");
    if (visuals.Visuals.Count < 0 ||
        visuals.Visuals.Count > limits.MaximumVisualOccurrences)
      throw Invalid(
        $"visual occurrence count exceeds the limit {limits.MaximumVisualOccurrences}");
    if (visuals.UnresolvedShapeReferenceCount < 0)
      throw Invalid("unresolved shape-reference count is negative");

    var occurrences = new List<Occurrence>();
    var byCar = new Dictionary<RideCarLink, Occurrence>(ReferenceEqualityComparer.Instance);
    var unresolvedCarReferenceCount = graph == null
      ? 0
      : AddGraphOccurrences(graph, occurrences, byCar, limits);
    foreach (var visual in visuals.Visuals) {
      ValidateVisualOccurrence(visual, limits);
      if (!byCar.TryGetValue(visual.Car, out var occurrence)) {
        if (graph != null)
          throw Invalid(
            $"RIC '{visual.Car.Reference}' visual occurrence is outside the supplied graph");
        if (occurrences.Count >= limits.MaximumCarOccurrences)
          throw Invalid(
            $"car occurrence count exceeds the limit {limits.MaximumCarOccurrences}");
        occurrence = new Occurrence(visual.Ride, visual.Train, visual.Car);
        occurrences.Add(occurrence);
        byCar.Add(visual.Car, occurrence);
      }
      else if (!ReferenceEquals(occurrence.Ride, visual.Ride) ||
                 !ReferenceEquals(occurrence.Train, visual.Train)) {
        throw Invalid("one RIC link is reused by multiple ride/train occurrences");
      }
      if (!occurrence.ShapeVisuals.TryAdd(visual.Visual, visual))
        throw Invalid(
          $"RIC '{visual.Car.Reference}' repeats one exact SVD shape occurrence");
    }

    var results = new List<RideCarVisualHierarchyResolution>(occurrences.Count);
    var resolvedPartCount = 0;
    var ambiguousPartCount = 0;
    foreach (var occurrence in occurrences) {
      foreach (var result in ResolveOccurrence(occurrence, limits)) {
        results.Add(result);
        foreach (var part in result.Parts) {
          if (part.IsResolved) resolvedPartCount = checked(resolvedPartCount + 1);
          if (part.Status == RideCarVisualHierarchyPartStatus.AnchorAmbiguous)
            ambiguousPartCount = checked(ambiguousPartCount + 1);
        }
      }
    }

    return new RideCarVisualHierarchyRegistry(
      results.ToArray(),
      resolvedPartCount,
      ambiguousPartCount,
      graph != null,
      unresolvedCarReferenceCount);
  }

  private static IReadOnlyList<RideCarVisualHierarchyResolution> ResolveOccurrence(
    Occurrence occurrence,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    var car = occurrence.Car.Car ??
      throw Invalid($"RIC '{occurrence.Car.Reference}' has no decoded resource");
    var graphVisuals = IndexGraphVisuals(occurrence.Car, limits);
    var frontAxleVisual = ResolveVisual(
      occurrence,
      graphVisuals,
      RideVisualRole.FrontAxle,
      car.Axles.Front.Visual,
      required: false);
    var rearAxleVisual = ResolveVisual(
      occurrence,
      graphVisuals,
      RideVisualRole.RearAxle,
      car.Axles.Rear.Visual,
      required: false);

    var results = new List<RideCarVisualHierarchyResolution>(2) {
      ResolveBodyOccurrence(
        occurrence,
        graphVisuals,
        RideVisualRole.Body,
        car.Visual,
        frontAxleVisual,
        rearAxleVisual,
        limits),
    };
    if (car.Wild?.FlippedVisual != null)
      results.Add(ResolveBodyOccurrence(
        occurrence,
        graphVisuals,
        RideVisualRole.WildFlippedBody,
        car.Wild.FlippedVisual,
        frontAxleVisual,
        rearAxleVisual,
        limits));
    return Array.AsReadOnly(results.ToArray());
  }

  private static RideCarVisualHierarchyResolution ResolveBodyOccurrence(
    Occurrence occurrence,
    IReadOnlyDictionary<RideVisualRole, RideVisualLink> graphVisuals,
    RideVisualRole bodyRole,
    string? bodyReference,
    VisualResolution frontAxleVisual,
    VisualResolution rearAxleVisual,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    var car = occurrence.Car.Car!;
    var body = ResolveBodyVisual(
      occurrence,
      graphVisuals,
      bodyRole,
      bodyReference);

    var frontAxle = ResolveAxle(
      RideVisualRole.FrontAxle,
      car.Axles.Front.Visual,
      car.Axles.Front.Type,
      frontAxleVisual,
      body,
      FrontAxleName,
      LegacyFrontAxleName,
      limits);
    var rearAxle = ResolveAxle(
      RideVisualRole.RearAxle,
      car.Axles.Rear.Visual,
      car.Axles.Rear.Type,
      rearAxleVisual,
      body,
      RearAxleName,
      LegacyRearAxleName,
      limits);

    var frontRightWheel = ResolveWheel(
      occurrence,
      graphVisuals,
      RideVisualRole.FrontRightWheel,
      car.Wheels.FrontRight,
      body,
      frontAxleVisual,
      frontAxle,
      RideCarVisualHierarchyAnchorSource.FrontAxle,
      RightWheelName,
      FrontRightWheelName,
      limits);
    var frontLeftWheel = ResolveWheel(
      occurrence,
      graphVisuals,
      RideVisualRole.FrontLeftWheel,
      car.Wheels.FrontLeft,
      body,
      frontAxleVisual,
      frontAxle,
      RideCarVisualHierarchyAnchorSource.FrontAxle,
      LeftWheelName,
      FrontLeftWheelName,
      limits);
    var backRightWheel = ResolveWheel(
      occurrence,
      graphVisuals,
      RideVisualRole.BackRightWheel,
      car.Wheels.BackRight,
      body,
      rearAxleVisual,
      rearAxle,
      RideCarVisualHierarchyAnchorSource.RearAxle,
      RightWheelName,
      RearRightWheelName,
      limits);
    var backLeftWheel = ResolveWheel(
      occurrence,
      graphVisuals,
      RideVisualRole.BackLeftWheel,
      car.Wheels.BackLeft,
      body,
      rearAxleVisual,
      rearAxle,
      RideCarVisualHierarchyAnchorSource.RearAxle,
      LeftWheelName,
      RearLeftWheelName,
      limits);

    return new RideCarVisualHierarchyResolution(
      occurrence.Ride,
      occurrence.Train,
      occurrence.Car,
      bodyRole,
      body.Status,
      body.Visual,
      body.ShapeVisual,
      frontAxle,
      rearAxle,
      frontRightWheel,
      frontLeftWheel,
      backRightWheel,
      backLeftWheel);
  }

  private static VisualResolution ResolveBodyVisual(
    Occurrence occurrence,
    IReadOnlyDictionary<RideVisualRole, RideVisualLink> graphVisuals,
    RideVisualRole bodyRole,
    string? serializedReference
  ) {
    if (serializedReference != null)
      return ResolveVisual(
        occurrence,
        graphVisuals,
        bodyRole,
        serializedReference,
        required: true);

    var car = occurrence.Car.Car!;
    if (bodyRole != RideVisualRole.Body || car.Wild?.AnimalSpecies == null)
      throw Invalid($"RIC '{occurrence.Car.Reference}' required {bodyRole} is null");
    if (graphVisuals.ContainsKey(bodyRole))
      throw Invalid(
        $"RIC '{occurrence.Car.Reference}' exposes {bodyRole} without a serialized reference");
    return VisualResolution.Unavailable(
      RideCarVisualHierarchyPartStatus.AnimalSpeciesBodyUnsupported);
  }

  private static int AddGraphOccurrences(
    RideResourceGraph graph,
    ICollection<Occurrence> occurrences,
    IDictionary<RideCarLink, Occurrence> byCar,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    if (graph.Rides == null) throw Invalid("ride graph has a null ride list");
    if (graph.Rides.Count < 0 || graph.Rides.Count > limits.MaximumCarOccurrences)
      throw Invalid(
        $"ride count exceeds the limit {limits.MaximumCarOccurrences}");
    if (graph.UnresolvedReferenceCount < 0)
      throw Invalid("ride graph unresolved-reference count is negative");

    var unresolvedCars = 0;
    foreach (var ride in graph.Rides) {
      if (ride == null) throw Invalid("ride graph contains a null ride");
      if (ride.Trains == null) throw Invalid("ride graph contains a null train list");
      if (ride.Trains.Count < 0 || ride.Trains.Count > limits.MaximumCarOccurrences)
        throw Invalid(
          $"ride train count exceeds the limit {limits.MaximumCarOccurrences}");
      foreach (var train in ride.Trains) {
        if (train == null) throw Invalid("ride graph contains a null train");
        if (train.Cars == null) throw Invalid("ride graph contains a null car list");
        if (train.Cars.Count < 0 || train.Cars.Count > limits.MaximumCarOccurrences)
          throw Invalid(
            $"train car count exceeds the limit {limits.MaximumCarOccurrences}");
        if (!train.IsResolved) {
          if (train.Cars.Count != 0)
            throw Invalid("unresolved RIT exposes decoded car occurrences");
          continue;
        }
        foreach (var car in train.Cars) {
          if (car == null) throw Invalid("ride graph contains a null car");
          if (!car.IsResolved) {
            unresolvedCars = checked(unresolvedCars + 1);
            continue;
          }
          if (occurrences.Count >= limits.MaximumCarOccurrences)
            throw Invalid(
              $"car occurrence count exceeds the limit {limits.MaximumCarOccurrences}");
          var occurrence = new Occurrence(ride, train, car);
          if (!byCar.TryAdd(car, occurrence))
            throw Invalid("one RIC link is reused by multiple ride/train occurrences");
          occurrences.Add(occurrence);
        }
      }
    }
    return unresolvedCars;
  }

  private static RideCarVisualHierarchyPart ResolveAxle(
    RideVisualRole role,
    string? serializedReference,
    uint type,
    VisualResolution visual,
    VisualResolution body,
    string canonicalAnchor,
    string legacyAnchor,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    if (visual.Status != RideCarVisualHierarchyPartStatus.Resolved)
      return Part(role, serializedReference, type, visual, visual.Status, null);

    var anchor = ResolveAnchor(
      body,
      RideCarVisualHierarchyAnchorSource.Body,
      canonicalAnchor,
      legacyAnchor,
      limits);
    return Part(role, serializedReference, type, visual, anchor.Status, anchor.Anchor);
  }

  private static RideCarVisualHierarchyPart ResolveWheel(
    Occurrence occurrence,
    IReadOnlyDictionary<RideVisualRole, RideVisualLink> graphVisuals,
    RideVisualRole role,
    RideCarVisualPart settings,
    VisualResolution body,
    VisualResolution axleVisual,
    RideCarVisualHierarchyPart axle,
    RideCarVisualHierarchyAnchorSource axleSource,
    string axleAnchorName,
    string bodyAnchorName,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    var wheelVisual = ResolveVisual(
      occurrence,
      graphVisuals,
      role,
      settings.Visual,
      required: false);
    if (wheelVisual.Status != RideCarVisualHierarchyPartStatus.Resolved)
      return Part(role, settings.Visual, settings.Type, wheelVisual, wheelVisual.Status, null);

    if (axle.Status == RideCarVisualHierarchyPartStatus.VisualUnresolved)
      return Part(
        role,
        settings.Visual,
        settings.Type,
        wheelVisual,
        RideCarVisualHierarchyPartStatus.AxleVisualUnresolved,
        null);
    if (axle.Status == RideCarVisualHierarchyPartStatus.AnchorAmbiguous)
      return Part(
        role,
        settings.Visual,
        settings.Type,
        wheelVisual,
        RideCarVisualHierarchyPartStatus.AnchorAmbiguous,
        null);
    if (axle.Status != RideCarVisualHierarchyPartStatus.VisualNotDeclared &&
        !axle.IsResolved)
      return Part(
        role,
        settings.Visual,
        settings.Type,
        wheelVisual,
        RideCarVisualHierarchyPartStatus.AxleAnchorUnavailable,
        null);

    if (axle.IsResolved) {
      var axleAnchor = ResolveAnchor(
        axleVisual,
        axleSource,
        axleAnchorName,
        legacyName: null,
        limits);
      if (axleAnchor.Status == RideCarVisualHierarchyPartStatus.Resolved)
        return Part(
          role,
          settings.Visual,
          settings.Type,
          wheelVisual,
          axleAnchor.Status,
          axleAnchor.Anchor);
      if (axleAnchor.Status == RideCarVisualHierarchyPartStatus.AnchorAmbiguous)
        return Part(
          role,
          settings.Visual,
          settings.Type,
          wheelVisual,
          axleAnchor.Status,
          null);
      if (axleAnchor.Status != RideCarVisualHierarchyPartStatus.AnchorMissing)
        return Part(
          role,
          settings.Visual,
          settings.Type,
          wheelVisual,
          RideCarVisualHierarchyPartStatus.AxleAnchorUnavailable,
          null);
    }

    var bodyAnchor = ResolveAnchor(
      body,
      RideCarVisualHierarchyAnchorSource.Body,
      bodyAnchorName,
      legacyName: null,
      limits);
    return Part(
      role,
      settings.Visual,
      settings.Type,
      wheelVisual,
      bodyAnchor.Status,
      bodyAnchor.Anchor);
  }

  private static RideCarVisualHierarchyPart Part(
    RideVisualRole role,
    string? serializedReference,
    uint type,
    VisualResolution visual,
    RideCarVisualHierarchyPartStatus status,
    RideCarVisualHierarchyAnchor? anchor
  ) => new(
    role,
    serializedReference,
    type,
    visual.Visual,
    visual.ShapeVisual,
    status,
    anchor);

  private static AnchorResolution ResolveAnchor(
    VisualResolution visual,
    RideCarVisualHierarchyAnchorSource anchorSource,
    string canonicalName,
    string? legacyName,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    if (visual.Status != RideCarVisualHierarchyPartStatus.Resolved ||
        visual.ShapeVisual == null)
      return AnchorResolution.Unavailable(
        RideCarVisualHierarchyPartStatus.AnchorShapeUnavailable);

    var selection = SelectBoneShape(visual.ShapeVisual, limits);
    if (selection == null)
      return AnchorResolution.Unavailable(
        RideCarVisualHierarchyPartStatus.AnchorShapeUnavailable);
    var bones = selection.Source.Resource.Bones;
    if (bones == null)
      throw Invalid($"BSH '{selection.Source.Resource.Name}' has a null bone list");
    if (bones.Count < 0 || bones.Count > limits.MaximumBonesPerShape)
      throw Invalid(
        $"BSH '{selection.Source.Resource.Name}' bone count exceeds the limit " +
        limits.MaximumBonesPerShape);

    var canonicalIndex = -1;
    var canonicalCount = 0;
    var legacyIndex = -1;
    var legacyCount = 0;
    for (var index = 0; index < bones.Count; index++) {
      var bone = bones[index];
      if (bone == null)
        throw Invalid($"BSH '{selection.Source.Resource.Name}' bone list contains null");
      ValidateName(
        bone.Name,
        $"BSH '{selection.Source.Resource.Name}' bone {index} name",
        limits);
      if (Matches(bone.Name, canonicalName)) {
        canonicalIndex = index;
        canonicalCount++;
      }
      else if (legacyName != null && Matches(bone.Name, legacyName)) {
        legacyIndex = index;
        legacyCount++;
      }
    }

    if (canonicalCount > 1 || (canonicalCount == 0 && legacyCount > 1))
      return AnchorResolution.Unavailable(
        RideCarVisualHierarchyPartStatus.AnchorAmbiguous);
    var selectedIndex = canonicalCount == 1 ? canonicalIndex : legacyIndex;
    if (selectedIndex < 0)
      return AnchorResolution.Unavailable(
        RideCarVisualHierarchyPartStatus.AnchorMissing);

    return new AnchorResolution(
      RideCarVisualHierarchyPartStatus.Resolved,
      new RideCarVisualHierarchyAnchor(
        anchorSource,
        visual.ShapeVisual,
        selection.Lod,
        selection.Source,
        selectedIndex,
        bones[selectedIndex]));
  }

  private static BoneShapeSelection? SelectBoneShape(
    RideCarVisualShapeLink visual,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    if (visual.Lods == null)
      throw Invalid($"SVD '{visual.Visual.Reference}' linked LOD list is null");
    if (visual.Lods.Count < 0 || visual.Lods.Count > limits.MaximumLodsPerVisual)
      throw Invalid(
        $"SVD '{visual.Visual.Reference}' LOD count exceeds the limit " +
        limits.MaximumLodsPerVisual);

    foreach (var lod in visual.Lods) {
      if (lod == null)
        throw Invalid($"SVD '{visual.Visual.Reference}' linked LOD list contains null");
      if (lod.StaticShapeSource != null && lod.BoneShapeSource != null)
        throw Invalid(
          $"SVD '{visual.Visual.Reference}' LOD resolves both SHS and BSH targets");
      if (lod.BoneShapeSource == null) continue;
      ValidateBoneSource(visual.Car, lod.BoneShapeSource, limits);
      return new BoneShapeSelection(lod, lod.BoneShapeSource);
    }
    return null;
  }

  private static IReadOnlyDictionary<RideVisualRole, RideVisualLink> IndexGraphVisuals(
    RideCarLink car,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    if (car.Visuals == null) throw Invalid($"RIC '{car.Reference}' visual list is null");
    if (car.Visuals.Count < 0 || car.Visuals.Count > limits.MaximumVisualsPerCar)
      throw Invalid(
        $"RIC '{car.Reference}' visual count exceeds the limit {limits.MaximumVisualsPerCar}");
    var indexed = new Dictionary<RideVisualRole, RideVisualLink>();
    foreach (var visual in car.Visuals) {
      if (visual == null) throw Invalid($"RIC '{car.Reference}' visual list contains null");
      if (!Enum.IsDefined(visual.Role))
        throw Invalid($"RIC '{car.Reference}' has unsupported visual role {visual.Role}");
      ValidateName(visual.Reference, $"RIC '{car.Reference}' visual reference", limits);
      if (!indexed.TryAdd(visual.Role, visual))
        throw Invalid($"RIC '{car.Reference}' repeats visual role {visual.Role}");
    }
    return indexed;
  }

  private static VisualResolution ResolveVisual(
    Occurrence occurrence,
    IReadOnlyDictionary<RideVisualRole, RideVisualLink> graphVisuals,
    RideVisualRole role,
    string? serializedReference,
    bool required
  ) {
    if (serializedReference == null) {
      if (required) throw Invalid($"RIC '{occurrence.Car.Reference}' required {role} is null");
      if (graphVisuals.ContainsKey(role))
        throw Invalid(
          $"RIC '{occurrence.Car.Reference}' exposes {role} without a serialized reference");
      return VisualResolution.Unavailable(
        RideCarVisualHierarchyPartStatus.VisualNotDeclared);
    }
    if (!graphVisuals.TryGetValue(role, out var visual))
      throw Invalid(
        $"RIC '{occurrence.Car.Reference}' omits its serialized {role} visual edge");
    if (!string.Equals(
          visual.Reference,
          serializedReference,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"RIC '{occurrence.Car.Reference}' {role} visual changed serialized identity");
    if (!visual.IsResolved)
      return new VisualResolution(
        RideCarVisualHierarchyPartStatus.VisualUnresolved,
        visual,
        null);
    if (!occurrence.ShapeVisuals.TryGetValue(visual, out var shapeVisual))
      throw Invalid(
        $"RIC '{occurrence.Car.Reference}' resolved {role} SVD has no shape occurrence");
    return new VisualResolution(
      RideCarVisualHierarchyPartStatus.Resolved,
      visual,
      shapeVisual);
  }

  private static void ValidateVisualOccurrence(
    RideCarVisualShapeLink? visual,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    if (visual == null) throw Invalid("visual occurrence list contains null");
    if (visual.Ride == null || visual.Train == null || visual.Car == null ||
        visual.Visual == null)
      throw Invalid("visual occurrence has an incomplete graph identity");
    if (!visual.Car.IsResolved || visual.Car.Source?.Resource == null)
      throw Invalid($"RIC '{visual.Car.Reference}' occurrence has no decoded resource");
    if (!visual.Visual.IsResolved || visual.Visual.Source?.Resource == null)
      throw Invalid($"RIC '{visual.Car.Reference}' occurrence has no decoded SVD resource");
    if (visual.Car.Visuals == null ||
        !visual.Car.Visuals.Any(item => ReferenceEquals(item, visual.Visual)))
      throw Invalid(
        $"RIC '{visual.Car.Reference}' visual changed exact graph object identity");
    if (visual.Ride.Trains == null ||
        !visual.Ride.Trains.Any(item => ReferenceEquals(item, visual.Train)))
      throw Invalid("visual occurrence train is not owned by its exact ride link");
    if (visual.Train.Cars == null ||
        !visual.Train.Cars.Any(item => ReferenceEquals(item, visual.Car)))
      throw Invalid("visual occurrence car is not owned by its exact train link");
    ValidateName(visual.Car.Reference, "RIC reference", limits);
    ValidateName(visual.Visual.Reference, "SVD reference", limits);
    if (visual.Lods == null)
      throw Invalid($"SVD '{visual.Visual.Reference}' linked LOD list is null");
    if (visual.Lods.Count < 0 || visual.Lods.Count > limits.MaximumLodsPerVisual)
      throw Invalid(
        $"SVD '{visual.Visual.Reference}' LOD count exceeds the limit " +
        limits.MaximumLodsPerVisual);
  }

  private static void ValidateBoneSource(
    RideCarLink car,
    RideBoneShapeResourceSource source,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    if (source.File == null || source.Resource == null)
      throw Invalid($"RIC '{car.Reference}' has incomplete BSH source identity");
    if (source.File.Type != FileType.BoneShape)
      throw Invalid($"RIC '{car.Reference}' hierarchy source is not a BSH resource");
    ValidateName(source.File.Name, "BSH symbol name", limits);
    ValidateName(source.Resource.Name, "decoded BSH name", limits);
    if (!string.Equals(
          source.File.Name,
          source.Resource.Name,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid($"RIC '{car.Reference}' BSH symbol and decoded names disagree");
    if (car.Source?.AllowedArchivePaths == null ||
        !car.Source.AllowedArchivePaths.Contains(
          source.File.Path,
          StringComparer.OrdinalIgnoreCase))
      throw Invalid($"RIC '{car.Reference}' BSH is outside its exact archive closure");
  }

  private static void ValidateName(
    string? name,
    string description,
    RideCarVisualHierarchyResolverLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(name) ||
        name.Length > limits.MaximumNameCharacters ||
        !string.Equals(name, name.Trim(), StringComparison.Ordinal))
      throw Invalid(
        $"{description} is empty, padded, or exceeds " +
        $"{limits.MaximumNameCharacters} characters");
  }

  private static bool Matches(string actual, string expected) =>
    string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

  private static void ValidateLimits(RideCarVisualHierarchyResolverLimits limits) {
    if (limits.MaximumVisualOccurrences <= 0 ||
        limits.MaximumCarOccurrences <= 0 ||
        limits.MaximumVisualsPerCar <= 0 ||
        limits.MaximumLodsPerVisual <= 0 ||
        limits.MaximumBonesPerShape <= 0 ||
        limits.MaximumNameCharacters <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid ride-car visual hierarchy: {message}.");

  private sealed class Occurrence(
    TrackedRideResourceLink ride,
    RideTrainLink train,
    RideCarLink car
  ) {
    public TrackedRideResourceLink Ride { get; } = ride;
    public RideTrainLink Train { get; } = train;
    public RideCarLink Car { get; } = car;
    public Dictionary<RideVisualLink, RideCarVisualShapeLink> ShapeVisuals { get; } =
      new(ReferenceEqualityComparer.Instance);
  }

  private sealed record VisualResolution(
    RideCarVisualHierarchyPartStatus Status,
    RideVisualLink? Visual,
    RideCarVisualShapeLink? ShapeVisual
  ) {
    public static VisualResolution Unavailable(RideCarVisualHierarchyPartStatus status) =>
      new(status, null, null);
  }

  private sealed record AnchorResolution(
    RideCarVisualHierarchyPartStatus Status,
    RideCarVisualHierarchyAnchor? Anchor
  ) {
    public static AnchorResolution Unavailable(RideCarVisualHierarchyPartStatus status) =>
      new(status, null);
  }

  private sealed record BoneShapeSelection(
    RideVisualShapeLodLink Lod,
    RideBoneShapeResourceSource Source
  );
}

internal readonly record struct RideCarVisualHierarchyResolverLimits(
  int MaximumVisualOccurrences,
  int MaximumCarOccurrences,
  int MaximumVisualsPerCar,
  int MaximumLodsPerVisual,
  int MaximumBonesPerShape,
  int MaximumNameCharacters
) {
  public static RideCarVisualHierarchyResolverLimits Default { get; } =
    new(1_000_000, 100_000, 16, 1_024, 65_536, 4_096);
}
