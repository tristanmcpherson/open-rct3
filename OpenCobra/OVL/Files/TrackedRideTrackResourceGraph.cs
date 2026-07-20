// Tracked Ride Track Resource Graph
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenCobra.OVL.Files;

/// <summary>The serialized role of one <c>tks</c> SymbolRef in a tracked ride.</summary>
public enum TrackedRideTrackSectionRole {
  Construction,
  OtherTop,
  OtherMiddle,
  TowerTop,
  TowerMiddle,
  OtherTopFlipped,
  OtherMiddleFlipped,
}

/// <summary>The serialized role of one <c>spl</c> SymbolRef in a tracked ride.</summary>
public enum TrackedRideTrackSplineRole {
  Track,
  TrackBig,
  Car,
  CarSwing,
}

/// <summary>A decoded TRR resource paired with its exact source OVL entry.</summary>
public sealed record TrackedRideTrackResourceSource(OvlFile File, TrackedRide Resource);

/// <summary>An exact <c>name:tks</c> edge and its decoded target when available.</summary>
public sealed record TrackedRideTrackSectionLink(
  TrackedRideTrackSectionRole Role,
  int? Index,
  string Reference,
  TrackedRideTrackSection? Metadata,
  TrackSectionResourceSource? Source
) {
  public bool IsResolved => Source != null;
}

/// <summary>An exact <c>name:spl</c> edge and its decoded, provenance-backed target.</summary>
public sealed record TrackedRideTrackSplineLink(
  TrackedRideTrackSplineRole Role,
  string Reference,
  SplineResourceSource? Source
) {
  public bool IsResolved => Source != null;
}

/// <summary>A tracked ride and its serialized TKS/SPL resource edges.</summary>
public sealed record TrackedRideTrackResourceLink(
  TrackedRideTrackResourceSource Source,
  IReadOnlyList<TrackedRideTrackSectionLink> TrackSections,
  IReadOnlyList<TrackedRideTrackSplineLink> Splines
);

/// <summary>
/// A bounded semantic graph over decoded TRR/TKS and provenance-backed SPL resources.
/// </summary>
public sealed record TrackedRideTrackResourceGraph(
  IReadOnlyList<TrackedRideTrackResourceLink> Rides,
  int UnresolvedReferenceCount
);

/// <summary>
/// Matches one TRR construction key to its exact referenced TKS identity.
/// </summary>
/// <remarks>
/// The pinned ManagerTRR writer lowercases the TKS resource name for ordinary
/// construction metadata. Complete Edition's shipped WoodenWildMine TRR also uses that base
/// metadata key for terminal <c>chain</c> TKS variants. The TKS SymbolRef remains exact and
/// authoritative; this compatibility rule never aliases target-resource lookup.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerTRR.cpp">
/// rct3-importer tracked-ride serializer
/// </seealso>
public static class TrackedRideTrackSectionIdentity {
  private const string ChainSuffix = "chain";

  public static bool MatchesConstructionMetadata(
    string resourceName,
    string metadataInternalName
  ) {
    ArgumentNullException.ThrowIfNull(resourceName);
    ArgumentNullException.ThrowIfNull(metadataInternalName);
    if (string.Equals(
      resourceName,
      metadataInternalName,
      StringComparison.OrdinalIgnoreCase)) return true;
    if (resourceName.Length <= ChainSuffix.Length ||
        !resourceName.EndsWith(ChainSuffix, StringComparison.OrdinalIgnoreCase))
      return false;
    return string.Equals(
      resourceName[..^ChainSuffix.Length],
      metadataInternalName,
      StringComparison.OrdinalIgnoreCase);
  }
}

/// <summary>Resolves exact TRR track SymbolRefs against any loaded OVL catalogs.</summary>
/// <remarks>
/// ManagerTRR writes the construction-section array and optional tower variants as TKS SymbolRefs,
/// plus four optional track/car spline roles as SPL SymbolRefs. Callers provide the raw archive-path
/// dependency closure for each referring TRR archive; only targets inside that closure may bind.
/// Ambiguous in-scope keys fail closed, while absent targets remain explicit unresolved links.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerTRR.cpp">
/// rct3-importer tracked-ride serializer
/// </seealso>
public static class TrackedRideTrackResourceGraphResolver {
  /// <summary>Links decoded TRR/TKS and provenance-backed SPL resources.</summary>
  public static TrackedRideTrackResourceGraph Resolve(
    IReadOnlyList<TrackedRideTrackResourceSource> rides,
    IReadOnlyList<TrackSectionResourceSource> trackSections,
    IReadOnlyList<SplineResourceSource> splines,
    IReadOnlyList<OvlResourceDependencyClosure> dependencyClosures
  ) => Resolve(
    rides,
    trackSections,
    splines,
    dependencyClosures,
    TrackedRideTrackResourceGraphLimits.Default);

  internal static TrackedRideTrackResourceGraph Resolve(
    IReadOnlyList<TrackedRideTrackResourceSource> rides,
    IReadOnlyList<TrackSectionResourceSource> trackSections,
    IReadOnlyList<SplineResourceSource> splines,
    IReadOnlyList<OvlResourceDependencyClosure> dependencyClosures,
    TrackedRideTrackResourceGraphLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(rides);
    ArgumentNullException.ThrowIfNull(trackSections);
    ArgumentNullException.ThrowIfNull(splines);
    ArgumentNullException.ThrowIfNull(dependencyClosures);
    ValidateCount(rides.Count, limits, "tracked rides");
    ValidateCount(trackSections.Count, limits, "track sections");
    ValidateCount(splines.Count, limits, "splines");
    ValidateCount(dependencyClosures.Count, limits, "dependency closures");

    var budget = new ResolutionBudget(limits);
    budget.ReserveResources(rides.Count, "TRR resource index");
    var sectionsByName = BuildSectionIndex(trackSections, budget, limits);
    var splinesByName = BuildSplineIndex(splines, budget, limits);
    var closuresBySource = BuildDependencyClosureIndex(
      dependencyClosures,
      budget,
      limits);
    var rideIdentities = new Dictionary<string, HashSet<string>>(
      StringComparer.OrdinalIgnoreCase);
    var linkedRides = new List<TrackedRideTrackResourceLink>(rides.Count);
    foreach (var source in rides) {
      if (source is null)
        throw new ArgumentException("TRR resources cannot contain null.", nameof(rides));
      ValidateSource(
        source.File,
        source.Resource,
        item => item.Name,
        FileType.TrackedRide,
        "TRR",
        limits);
      AddReferringIdentity(rideIdentities, source.File, "TRR");
      var allowedTargetPaths = RequiredClosure(
        source.File.Path,
        closuresBySource,
        "TRR",
        source.Resource.Name);
      linkedRides.Add(new TrackedRideTrackResourceLink(
        source,
        LinkSections(
          source.Resource,
          sectionsByName,
          allowedTargetPaths,
          budget,
          limits),
        LinkSplines(
          source.Resource,
          splinesByName,
          allowedTargetPaths,
          budget,
          limits)));
    }
    return new TrackedRideTrackResourceGraph(
      linkedRides,
      budget.UnresolvedReferenceCount);
  }

  private static IReadOnlyList<TrackedRideTrackSectionLink> LinkSections(
    TrackedRide ride,
    IReadOnlyDictionary<string, IReadOnlyList<TrackSectionResourceSource>> sectionsByName,
    IReadOnlySet<string> allowedTargetPaths,
    ResolutionBudget budget,
    TrackedRideTrackResourceGraphLimits limits
  ) {
    if (ride.TrackSections is null)
      throw new InvalidDataException(
        $"Tracked-ride track graph TRR '{ride.Name}' construction-section list is null.");
    ValidateCount(
      ride.TrackSections.Count,
      limits,
      $"TRR '{ride.Name}' construction sections");
    var links = new List<TrackedRideTrackSectionLink>();
    foreach (var index in Enumerable.Range(0, ride.TrackSections.Count)) {
      var metadata = ride.TrackSections[index];
      if (metadata is null)
        throw new InvalidDataException(
          $"Tracked-ride track graph TRR '{ride.Name}' construction section {index} is null.");
      ValidateBareName(
        metadata.InternalName,
        $"TRR '{ride.Name}' construction section {index} internal metadata",
        limits);
      Add(
        TrackedRideTrackSectionRole.Construction,
        index,
        metadata.Resource,
        metadata,
        $"construction section {index}");
    }

    if (ride.References is null)
      throw new InvalidDataException(
        $"Tracked-ride track graph TRR '{ride.Name}' fixed references are null.");
    AddOptional(TrackedRideTrackSectionRole.OtherTop,
      ride.References.OtherTop, "other top section");
    AddOptional(TrackedRideTrackSectionRole.OtherMiddle,
      ride.References.OtherMiddle, "other middle section");
    AddOptional(TrackedRideTrackSectionRole.TowerTop,
      ride.References.TowerTop, "tower top section");
    AddOptional(TrackedRideTrackSectionRole.TowerMiddle,
      ride.References.TowerMiddle, "tower middle section");
    if (ride.Expansion is { } expansion) {
      AddOptional(TrackedRideTrackSectionRole.OtherTopFlipped,
        expansion.OtherTopFlipped, "flipped other top section");
      AddOptional(TrackedRideTrackSectionRole.OtherMiddleFlipped,
        expansion.OtherMiddleFlipped, "flipped other middle section");
    }
    return links;

    void AddOptional(
      TrackedRideTrackSectionRole role,
      string? reference,
      string description
    ) {
      if (reference == null) return;
      Add(role, null, reference, null, description);
    }

    void Add(
      TrackedRideTrackSectionRole role,
      int? index,
      string reference,
      TrackedRideTrackSection? metadata,
      string description
    ) {
      var fullDescription = $"TRR '{ride.Name}' {description}";
      var name = ParseTaggedName(reference, "tks", fullDescription, limits);
      if (metadata != null &&
          !TrackedRideTrackSectionIdentity.MatchesConstructionMetadata(
            name,
            metadata.InternalName))
        throw new InvalidDataException(
          $"Tracked-ride track graph {fullDescription} metadata name " +
          $"'{metadata.InternalName}' does not match reference key '{name}'.");
      budget.ReserveRelationships(1, fullDescription);
      var source = ResolveTarget(
        name,
        sectionsByName,
        allowedTargetPaths,
        item => item.File.Path,
        fullDescription);
      if (source != null) {
        links.Add(new TrackedRideTrackSectionLink(
          role,
          index,
          reference,
          metadata,
          source));
        return;
      }
      links.Add(new TrackedRideTrackSectionLink(role, index, reference, metadata, null));
      budget.MarkUnresolved();
    }
  }

  private static IReadOnlyList<TrackedRideTrackSplineLink> LinkSplines(
    TrackedRide ride,
    IReadOnlyDictionary<string, IReadOnlyList<SplineResourceSource>> splinesByName,
    IReadOnlySet<string> allowedTargetPaths,
    ResolutionBudget budget,
    TrackedRideTrackResourceGraphLimits limits
  ) {
    if (ride.References is null)
      throw new InvalidDataException(
        $"Tracked-ride track graph TRR '{ride.Name}' fixed references are null.");
    var links = new List<TrackedRideTrackSplineLink>(4);
    Add(TrackedRideTrackSplineRole.Track, ride.References.TrackSpline, "track spline");
    Add(TrackedRideTrackSplineRole.TrackBig,
      ride.References.TrackBigSpline, "large track spline");
    Add(TrackedRideTrackSplineRole.Car, ride.References.CarSpline, "car spline");
    Add(TrackedRideTrackSplineRole.CarSwing,
      ride.References.CarSwingSpline, "car swing spline");
    return links;

    void Add(TrackedRideTrackSplineRole role, string? reference, string description) {
      if (reference == null) return;
      var fullDescription = $"TRR '{ride.Name}' {description}";
      var name = ParseTaggedName(reference, "spl", fullDescription, limits);
      budget.ReserveRelationships(1, fullDescription);
      var source = ResolveTarget(
        name,
        splinesByName,
        allowedTargetPaths,
        item => item.File.Path,
        fullDescription);
      if (source != null) {
        links.Add(new TrackedRideTrackSplineLink(role, reference, source));
        return;
      }
      links.Add(new TrackedRideTrackSplineLink(role, reference, null));
      budget.MarkUnresolved();
    }
  }

  private static Dictionary<string, IReadOnlyList<TrackSectionResourceSource>>
    BuildSectionIndex(
    IReadOnlyList<TrackSectionResourceSource> sections,
    ResolutionBudget budget,
    TrackedRideTrackResourceGraphLimits limits
  ) {
    budget.ReserveResources(sections.Count, "TKS resource index");
    var mutable = new Dictionary<string, List<TrackSectionResourceSource>>(
      sections.Count,
      StringComparer.OrdinalIgnoreCase);
    foreach (var source in sections) {
      if (source is null)
        throw new ArgumentException("TKS resources cannot contain null.", nameof(sections));
      ValidateSource(
        source.File,
        source.Resource,
        item => item.Name,
        FileType.TrackSection,
        "TKS",
        limits);
      if (!mutable.TryGetValue(source.Resource.Name, out var candidates))
        mutable.Add(source.Resource.Name, candidates = []);
      candidates.Add(source);
    }
    return mutable.ToDictionary(
      pair => pair.Key,
      pair => (IReadOnlyList<TrackSectionResourceSource>)pair.Value,
      StringComparer.OrdinalIgnoreCase);
  }

  private static Dictionary<string, IReadOnlyList<SplineResourceSource>> BuildSplineIndex(
    IReadOnlyList<SplineResourceSource> splines,
    ResolutionBudget budget,
    TrackedRideTrackResourceGraphLimits limits
  ) {
    budget.ReserveResources(splines.Count, "SPL resource index");
    var mutable = new Dictionary<string, List<SplineResourceSource>>(
      splines.Count,
      StringComparer.OrdinalIgnoreCase);
    foreach (var source in splines) {
      if (source is null)
        throw new ArgumentException("SPL resources cannot contain null.", nameof(splines));
      if (source.File is null || source.Resource is null)
        throw new ArgumentException(
          "SPL resource sources require an OVL file and decoded resource.",
          nameof(splines));
      ValidateSource(
        source.File,
        source.Resource,
        item => item.Name,
        FileType.Spline,
        "SPL",
        limits);
      if (!mutable.TryGetValue(source.Resource.Name, out var candidates))
        mutable.Add(source.Resource.Name, candidates = []);
      candidates.Add(source);
    }
    return mutable.ToDictionary(
      pair => pair.Key,
      pair => (IReadOnlyList<SplineResourceSource>)pair.Value,
      StringComparer.OrdinalIgnoreCase);
  }

  private static Dictionary<string, IReadOnlySet<string>> BuildDependencyClosureIndex(
    IReadOnlyList<OvlResourceDependencyClosure> closures,
    ResolutionBudget budget,
    TrackedRideTrackResourceGraphLimits limits
  ) {
    var index = new Dictionary<string, IReadOnlySet<string>>(
      closures.Count,
      StringComparer.OrdinalIgnoreCase);
    foreach (var closure in closures) {
      if (closure is null)
        throw new ArgumentException(
          "Dependency closures cannot contain null.",
          nameof(closures));
      ValidateSourcePath(closure.SourcePath, "dependency source", limits);
      if (closure.AllowedTargetPaths is null)
        throw new ArgumentException(
          "Dependency closure target paths cannot be null.",
          nameof(closures));
      ValidateCount(
        closure.AllowedTargetPaths.Count,
        limits,
        $"dependency targets for '{closure.SourcePath}'");
      budget.ReserveResources(
        closure.AllowedTargetPaths.Count,
        $"dependency targets for '{closure.SourcePath}'");
      var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var path in closure.AllowedTargetPaths) {
        ValidateSourcePath(path, "allowed dependency target", limits);
        if (!allowed.Add(path))
          throw new InvalidDataException(
            $"Tracked-ride track graph dependency closure for " +
            $"'{closure.SourcePath}' repeats target path '{path}'.");
      }
      if (!index.TryAdd(closure.SourcePath, allowed))
        throw new InvalidDataException(
          $"Tracked-ride track graph has duplicate dependency closure for " +
          $"'{closure.SourcePath}'.");
    }
    return index;
  }

  private static IReadOnlySet<string> RequiredClosure(
    string sourcePath,
    IReadOnlyDictionary<string, IReadOnlySet<string>> closuresBySource,
    string tag,
    string resourceName
  ) {
    if (closuresBySource.TryGetValue(sourcePath, out var closure)) return closure;
    throw new InvalidDataException(
      $"Tracked-ride track graph {tag} resource '{resourceName}' from " +
      $"'{sourcePath}' has no explicit dependency closure.");
  }

  private static T? ResolveTarget<T>(
    string name,
    IReadOnlyDictionary<string, IReadOnlyList<T>> resourcesByName,
    IReadOnlySet<string> allowedTargetPaths,
    Func<T, string> getPath,
    string description
  ) where T : class {
    if (!resourcesByName.TryGetValue(name, out var candidates)) return null;
    T? resolved = null;
    foreach (var candidate in candidates) {
      if (!allowedTargetPaths.Contains(getPath(candidate))) continue;
      if (resolved != null)
        throw new InvalidDataException(
          $"Tracked-ride track graph {description} reference '{name}' has multiple " +
          "targets inside its explicit dependency closure.");
      resolved = candidate;
    }
    return resolved;
  }

  private static void AddReferringIdentity(
    IDictionary<string, HashSet<string>> identities,
    OvlFile file,
    string tag
  ) {
    if (!identities.TryGetValue(file.Path, out var names)) {
      names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      identities.Add(file.Path, names);
    }
    if (!names.Add(file.Name))
      throw new InvalidDataException(
        $"Tracked-ride track graph has duplicate {tag} identity " +
        $"'{file.Name}' in '{file.Path}'.");
  }

  private static void ValidateSource<T>(
    OvlFile? file,
    T? resource,
    Func<T, string> getName,
    FileType expectedType,
    string tag,
    TrackedRideTrackResourceGraphLimits limits
  ) where T : class {
    if (file is null || resource is null)
      throw new ArgumentException(
        $"{tag} resource sources require an OVL file and decoded resource.");
    var decodedName = getName(resource);
    ValidateBareName(file.Name, $"{tag} OVL file", limits);
    ValidateBareName(decodedName, $"decoded {tag} resource", limits);
    ValidateSourcePath(file.Path, $"{tag} source", limits);
    if (file.Type != expectedType)
      throw new InvalidDataException(
        $"Tracked-ride track graph {tag} resource '{file.Name}' has OVL type " +
        $"'{file.Type.ToTagString()}' instead of '{expectedType.ToTagString()}'.");
    if (!string.Equals(file.Name, decodedName, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Tracked-ride track graph cannot disambiguate {tag} OVL file name " +
        $"'{file.Name}' from decoded name '{decodedName}'.");
  }

  private static void ValidateSourcePath(
    string path,
    string description,
    TrackedRideTrackResourceGraphLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(path) || path.Length > limits.MaximumStringCharacters)
      throw new InvalidDataException(
        $"Tracked-ride track graph {description} path is empty or exceeds " +
        $"{limits.MaximumStringCharacters} characters.");
  }

  private static string ParseTaggedName(
    string reference,
    string expectedTag,
    string description,
    TrackedRideTrackResourceGraphLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(reference) ||
        reference.Length > limits.MaximumStringCharacters)
      throw new InvalidDataException(
        $"Tracked-ride track graph {description} is empty or exceeds " +
        $"{limits.MaximumStringCharacters} characters.");
    var separator = reference.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals(
          expectedTag,
          StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Tracked-ride track graph {description} reference '{reference}' is not an exact " +
        $"name:{expectedTag} key.");
    var name = reference[..separator];
    ValidateBareName(name, description, limits);
    return name;
  }

  private static void ValidateBareName(
    string name,
    string description,
    TrackedRideTrackResourceGraphLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(name) || name.Length > limits.MaximumStringCharacters)
      throw new InvalidDataException(
        $"Tracked-ride track graph {description} name is empty or exceeds " +
        $"{limits.MaximumStringCharacters} characters.");
  }

  private static void ValidateCount(
    int count,
    TrackedRideTrackResourceGraphLimits limits,
    string description
  ) {
    if (count < 0 || Convert.ToUInt64(count) > limits.MaximumResourcesPerType)
      throw new InvalidDataException(
        $"Tracked-ride track graph {description} count {count} exceeds the limit " +
        $"{limits.MaximumResourcesPerType}.");
  }

  private sealed class ResolutionBudget(TrackedRideTrackResourceGraphLimits limits) {
    private ulong resources;
    private ulong relationships;
    private int unresolvedReferences;

    public int UnresolvedReferenceCount => unresolvedReferences;

    public void ReserveResources(int count, string description) {
      var converted = Convert.ToUInt64(count);
      if (converted > limits.MaximumResources ||
          resources > limits.MaximumResources - converted)
        throw new InvalidDataException(
          $"Tracked-ride track graph aggregate resources exceed the limit " +
          $"{limits.MaximumResources} while indexing {description}.");
      resources += converted;
    }

    public void ReserveRelationships(ulong count, string description) {
      if (count > limits.MaximumRelationships ||
          relationships > limits.MaximumRelationships - count)
        throw new InvalidDataException(
          $"Tracked-ride track graph relationships exceed the limit " +
          $"{limits.MaximumRelationships} while linking {description}.");
      relationships += count;
    }

    public void MarkUnresolved() {
      if (unresolvedReferences == int.MaxValue)
        throw new InvalidDataException(
          "Tracked-ride track graph unresolved reference count exceeds the decoder range.");
      unresolvedReferences++;
    }
  }
}

internal readonly record struct TrackedRideTrackResourceGraphLimits(
  ulong MaximumResourcesPerType,
  ulong MaximumResources,
  ulong MaximumRelationships,
  int MaximumStringCharacters
) {
  public static TrackedRideTrackResourceGraphLimits Default { get; } =
    new(64 * 1024, 256 * 1024, 1_000_000, 4 * 1024);
}
