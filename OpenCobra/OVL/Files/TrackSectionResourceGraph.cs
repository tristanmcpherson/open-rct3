// Track Section Resource Graph
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;

namespace OpenCobra.OVL.Files;

/// <summary>The serialized role of one <c>spl</c> SymbolRef in a track section.</summary>
public enum TrackSectionSplineRole {
  CarLeft,
  CarRight,
  JoinLeft,
  JoinRight,
  ExtraLeft,
  ExtraRight,
  WaterLeft,
  WaterRight,
  Loop,
  Path,
  SpeedLeft,
  SpeedRight,
}

/// <summary>A decoded SPL resource paired with its exact source OVL entry.</summary>
public sealed record SplineResourceSource(OvlFile File, Spline Resource);

/// <summary>A decoded TKS resource paired with its exact source OVL entry.</summary>
public sealed record TrackSectionResourceSource(OvlFile File, TrackSection Resource);

/// <summary>A decoded SID resource paired with its exact source OVL entry.</summary>
public sealed record SceneryItemResourceSource(OvlFile File, SceneryItem Resource);

/// <summary>Raw archive paths one referring archive is explicitly allowed to target.</summary>
public sealed record OvlResourceDependencyClosure(
  string SourcePath,
  IReadOnlyList<string> AllowedTargetPaths);

/// <summary>An exact <c>name:sid</c> edge and its decoded target when available.</summary>
public sealed record TrackSectionSceneryLink(
  string Reference,
  SceneryItemResourceSource? Source
) {
  public bool IsResolved => Source != null;
}

/// <summary>An exact <c>name:spl</c> edge and its decoded, provenance-backed target.</summary>
public sealed record TrackSectionSplineLink(
  TrackSectionSplineRole Role,
  int? Index,
  string Reference,
  SplineResourceSource? Source
) {
  public bool IsResolved => Source != null;
}

/// <summary>A track section and the SID/SPL resource edges serialized from it.</summary>
public sealed record TrackSectionResourceLink(
  TrackSectionResourceSource Source,
  TrackSectionSceneryLink Scenery,
  IReadOnlyList<TrackSectionSplineLink> Splines
);

/// <summary>
/// A bounded semantic graph over decoded TKS, SID, and provenance-backed SPL resources.
/// </summary>
public sealed record TrackSectionResourceGraph(
  IReadOnlyList<TrackSectionResourceLink> Sections,
  int UnresolvedReferenceCount
);

/// <summary>Resolves exact TKS SymbolRefs against resources from any loaded OVL catalogs.</summary>
/// <remarks>
/// ManagerTKS writes the required SID, car, and join references, the optional extra pair, and the
/// Soaked/Wild loop, path, and speed-spline references as tagged SymbolRefs. Callers may combine
/// decoded resources from the section archive and its external archives. Callers provide the raw
/// archive-path dependency closure for each referring TKS archive; only targets inside that closure
/// may bind. Ambiguous in-scope keys fail closed, while absent targets remain explicit unresolved
/// links.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerTKS.cpp">
/// rct3-importer track-section serializer
/// </seealso>
public static class TrackSectionResourceGraphResolver {
  /// <summary>
  /// Links already-decoded TKS/SID resources and provenance-backed SPL resources.
  /// </summary>
  public static TrackSectionResourceGraph Resolve(
    IReadOnlyList<TrackSectionResourceSource> sections,
    IReadOnlyList<SceneryItemResourceSource> sceneryItems,
    IReadOnlyList<SplineResourceSource> splines,
    IReadOnlyList<OvlResourceDependencyClosure> dependencyClosures
  ) => Resolve(
    sections,
    sceneryItems,
    splines,
    dependencyClosures,
    TrackSectionResourceGraphLimits.Default);

  internal static TrackSectionResourceGraph Resolve(
    IReadOnlyList<TrackSectionResourceSource> sections,
    IReadOnlyList<SceneryItemResourceSource> sceneryItems,
    IReadOnlyList<SplineResourceSource> splines,
    IReadOnlyList<OvlResourceDependencyClosure> dependencyClosures,
    TrackSectionResourceGraphLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(sections);
    ArgumentNullException.ThrowIfNull(sceneryItems);
    ArgumentNullException.ThrowIfNull(splines);
    ArgumentNullException.ThrowIfNull(dependencyClosures);
    ValidateCount(sections.Count, limits, "track sections");
    ValidateCount(sceneryItems.Count, limits, "scenery items");
    ValidateCount(splines.Count, limits, "splines");
    ValidateCount(dependencyClosures.Count, limits, "dependency closures");

    var budget = new ResolutionBudget(limits);
    budget.ReserveResources(sections.Count, "TKS resource index");
    var sceneryByName = BuildSceneryIndex(sceneryItems, budget, limits);
    var splinesByName = BuildSplineIndex(splines, budget, limits);
    var closuresBySource = BuildDependencyClosureIndex(
      dependencyClosures,
      budget,
      limits);
    var sectionIdentities = new Dictionary<string, HashSet<string>>(
      StringComparer.OrdinalIgnoreCase);
    var linkedSections = new List<TrackSectionResourceLink>(sections.Count);
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
      AddReferringIdentity(sectionIdentities, source.File, "TKS");
      var allowedTargetPaths = RequiredClosure(
        source.File.Path,
        closuresBySource,
        "TKS",
        source.Resource.Name);
      var scenery = LinkScenery(
        source.Resource,
        sceneryByName,
        allowedTargetPaths,
        budget,
        limits);
      var linkedSplines = LinkSplines(
        source.Resource,
        splinesByName,
        allowedTargetPaths,
        budget,
        limits);
      linkedSections.Add(new TrackSectionResourceLink(source, scenery, linkedSplines));
    }
    return new TrackSectionResourceGraph(linkedSections, budget.UnresolvedReferenceCount);
  }

  private static TrackSectionSceneryLink LinkScenery(
    TrackSection section,
    IReadOnlyDictionary<string, IReadOnlyList<SceneryItemResourceSource>> sceneryByName,
    IReadOnlySet<string> allowedTargetPaths,
    ResolutionBudget budget,
    TrackSectionResourceGraphLimits limits
  ) {
    var description = $"TKS '{section.Name}' scenery item";
    var name = ParseTaggedName(section.SceneryItem, "sid", description, limits);
    budget.ReserveRelationships(1, description);
    var source = ResolveTarget(
      name,
      sceneryByName,
      allowedTargetPaths,
      item => item.File.Path,
      description);
    if (source != null)
      return new TrackSectionSceneryLink(section.SceneryItem, source);
    budget.MarkUnresolved();
    return new TrackSectionSceneryLink(section.SceneryItem, null);
  }

  private static IReadOnlyList<TrackSectionSplineLink> LinkSplines(
    TrackSection section,
    IReadOnlyDictionary<string, IReadOnlyList<SplineResourceSource>> splinesByName,
    IReadOnlySet<string> allowedTargetPaths,
    ResolutionBudget budget,
    TrackSectionResourceGraphLimits limits
  ) {
    var links = new List<TrackSectionSplineLink>();
    AddPair(TrackSectionSplineRole.CarLeft, TrackSectionSplineRole.CarRight,
      section.CarSplines, null, "car splines");
    AddPair(TrackSectionSplineRole.JoinLeft, TrackSectionSplineRole.JoinRight,
      section.JoinSplines, null, "join splines");
    if (section.ExtraSplines is { } extra)
      AddPair(TrackSectionSplineRole.ExtraLeft, TrackSectionSplineRole.ExtraRight,
        extra, null, "extra splines");
    if (section.WaterSplines is { } water)
      AddPair(TrackSectionSplineRole.WaterLeft, TrackSectionSplineRole.WaterRight,
        water, null, "water splines");

    if (section.Expansion is { } expansion) {
      if (expansion.LoopSpline is { } loop)
        Add(TrackSectionSplineRole.Loop, null, loop, "loop spline");
      if (expansion.PathSplines is null)
        throw new InvalidDataException(
          $"Track-section resource graph TKS '{section.Name}' path spline list is null.");
      ValidateCount(
        expansion.PathSplines.Count,
        limits,
        $"TKS '{section.Name}' path splines");
      foreach (var index in Enumerable.Range(0, expansion.PathSplines.Count))
        Add(
          TrackSectionSplineRole.Path,
          index,
          expansion.PathSplines[index],
          $"path spline {index}");
      if (expansion.SpeedSplines is null)
        throw new InvalidDataException(
          $"Track-section resource graph TKS '{section.Name}' speed spline list is null.");
      ValidateCount(
        expansion.SpeedSplines.Count,
        limits,
        $"TKS '{section.Name}' speed splines");
      foreach (var index in Enumerable.Range(0, expansion.SpeedSplines.Count)) {
        var speed = expansion.SpeedSplines[index];
        if (speed is null)
          throw new InvalidDataException(
            $"Track-section resource graph TKS '{section.Name}' speed spline {index} is null.");
        AddPair(
          TrackSectionSplineRole.SpeedLeft,
          TrackSectionSplineRole.SpeedRight,
          speed.Splines,
          index,
          $"speed spline {index}");
      }
    }
    return links;

    void AddPair(
      TrackSectionSplineRole leftRole,
      TrackSectionSplineRole rightRole,
      TrackSectionSplinePair pair,
      int? index,
      string description
    ) {
      if (pair is null)
        throw new InvalidDataException(
          $"Track-section resource graph TKS '{section.Name}' {description} pair is null.");
      Add(leftRole, index, pair.Left, $"{description} left");
      Add(rightRole, index, pair.Right, $"{description} right");
    }

    void Add(
      TrackSectionSplineRole role,
      int? index,
      string reference,
      string description
    ) {
      var fullDescription = $"TKS '{section.Name}' {description}";
      var name = ParseTaggedName(reference, "spl", fullDescription, limits);
      budget.ReserveRelationships(1, fullDescription);
      var source = ResolveTarget(
        name,
        splinesByName,
        allowedTargetPaths,
        item => item.File.Path,
        fullDescription);
      if (source != null) {
        links.Add(new TrackSectionSplineLink(role, index, reference, source));
        return;
      }
      links.Add(new TrackSectionSplineLink(role, index, reference, null));
      budget.MarkUnresolved();
    }
  }

  private static Dictionary<string, IReadOnlyList<SceneryItemResourceSource>>
    BuildSceneryIndex(
    IReadOnlyList<SceneryItemResourceSource> resources,
    ResolutionBudget budget,
    TrackSectionResourceGraphLimits limits
  ) {
    budget.ReserveResources(resources.Count, "SID resource index");
    var mutable = new Dictionary<string, List<SceneryItemResourceSource>>(
      resources.Count,
      StringComparer.OrdinalIgnoreCase);
    foreach (var source in resources) {
      if (source is null)
        throw new ArgumentException("SID resources cannot contain null.", nameof(resources));
      ValidateSource(
        source.File,
        source.Resource,
        item => item.Name,
        FileType.SceneryItem,
        "SID",
        limits);
      if (!mutable.TryGetValue(source.Resource.Name, out var candidates))
        mutable.Add(source.Resource.Name, candidates = []);
      candidates.Add(source);
    }
    return mutable.ToDictionary(
      pair => pair.Key,
      pair => (IReadOnlyList<SceneryItemResourceSource>)pair.Value,
      StringComparer.OrdinalIgnoreCase);
  }

  private static Dictionary<string, IReadOnlyList<SplineResourceSource>> BuildSplineIndex(
    IReadOnlyList<SplineResourceSource> splines,
    ResolutionBudget budget,
    TrackSectionResourceGraphLimits limits
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
    TrackSectionResourceGraphLimits limits
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
            $"Track-section resource graph dependency closure for " +
            $"'{closure.SourcePath}' repeats target path '{path}'.");
      }
      if (!index.TryAdd(closure.SourcePath, allowed))
        throw new InvalidDataException(
          $"Track-section resource graph has duplicate dependency closure for " +
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
      $"Track-section resource graph {tag} resource '{resourceName}' from " +
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
          $"Track-section resource graph {description} reference '{name}' has multiple " +
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
        $"Track-section resource graph has duplicate {tag} identity " +
        $"'{file.Name}' in '{file.Path}'.");
  }

  private static void ValidateSource<T>(
    OvlFile? file,
    T? resource,
    Func<T, string> getName,
    FileType expectedType,
    string tag,
    TrackSectionResourceGraphLimits limits
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
        $"Track-section resource graph {tag} resource '{file.Name}' has OVL type " +
        $"'{file.Type.ToTagString()}' instead of '{expectedType.ToTagString()}'.");
    if (!string.Equals(file.Name, decodedName, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Track-section resource graph cannot disambiguate {tag} OVL file name " +
        $"'{file.Name}' from decoded name '{decodedName}'.");
  }

  private static void ValidateSourcePath(
    string path,
    string description,
    TrackSectionResourceGraphLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(path) || path.Length > limits.MaximumStringCharacters)
      throw new InvalidDataException(
        $"Track-section resource graph {description} path is empty or exceeds " +
        $"{limits.MaximumStringCharacters} characters.");
  }

  private static string ParseTaggedName(
    string reference,
    string expectedTag,
    string description,
    TrackSectionResourceGraphLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(reference) ||
        reference.Length > limits.MaximumStringCharacters)
      throw new InvalidDataException(
        $"Track-section resource graph {description} is empty or exceeds " +
        $"{limits.MaximumStringCharacters} characters.");
    var separator = reference.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals(
          expectedTag,
          StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Track-section resource graph {description} reference '{reference}' is not an exact " +
        $"name:{expectedTag} key.");
    var name = reference[..separator];
    ValidateBareName(name, description, limits);
    return name;
  }

  private static void ValidateBareName(
    string name,
    string description,
    TrackSectionResourceGraphLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(name) || name.Length > limits.MaximumStringCharacters)
      throw new InvalidDataException(
        $"Track-section resource graph {description} name is empty or exceeds " +
        $"{limits.MaximumStringCharacters} characters.");
  }

  private static void ValidateCount(
    int count,
    TrackSectionResourceGraphLimits limits,
    string description
  ) {
    if (count < 0 || Convert.ToUInt64(count) > limits.MaximumResourcesPerType)
      throw new InvalidDataException(
        $"Track-section resource graph {description} count {count} exceeds the limit " +
        $"{limits.MaximumResourcesPerType}.");
  }

  private sealed class ResolutionBudget(TrackSectionResourceGraphLimits limits) {
    private ulong resources;
    private ulong relationships;
    private int unresolvedReferences;

    public int UnresolvedReferenceCount => unresolvedReferences;

    public void ReserveResources(int count, string description) {
      var converted = Convert.ToUInt64(count);
      if (converted > limits.MaximumResources ||
          resources > limits.MaximumResources - converted)
        throw new InvalidDataException(
          $"Track-section resource graph aggregate resources exceed the limit " +
          $"{limits.MaximumResources} while indexing {description}.");
      resources += converted;
    }

    public void ReserveRelationships(ulong count, string description) {
      if (count > limits.MaximumRelationships ||
          relationships > limits.MaximumRelationships - count)
        throw new InvalidDataException(
          $"Track-section resource graph relationships exceed the limit " +
          $"{limits.MaximumRelationships} while linking {description}.");
      relationships += count;
    }

    public void MarkUnresolved() {
      if (unresolvedReferences == int.MaxValue)
        throw new InvalidDataException(
          "Track-section resource graph unresolved reference count exceeds the decoder range.");
      unresolvedReferences++;
    }
  }
}

internal readonly record struct TrackSectionResourceGraphLimits(
  ulong MaximumResourcesPerType,
  ulong MaximumResources,
  ulong MaximumRelationships,
  int MaximumStringCharacters
) {
  public static TrackSectionResourceGraphLimits Default { get; } =
    new(64 * 1024, 256 * 1024, 1_000_000, 4 * 1024);
}
