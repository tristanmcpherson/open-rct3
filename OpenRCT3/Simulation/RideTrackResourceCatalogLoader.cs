// Ride Track Resource Catalog Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>Why an exact archive pair could not be included in a catalog load.</summary>
internal enum RideTrackResourceCatalogLoadIssueKind {
  MissingRootPair,
  MissingDependencyPair,
}

/// <summary>
/// An exact root or serialized dependency whose common/unique pair is incomplete.
/// </summary>
internal sealed record RideTrackResourceCatalogLoadIssue(
  RideTrackResourceCatalogLoadIssueKind Kind,
  string? DeclaringCommonPath,
  string Reference,
  string CommonPath,
  string UniquePath,
  bool CommonExists,
  bool UniqueExists
);

/// <summary>Owns every paired OVL retained by a loaded ride-track resource catalog.</summary>
internal sealed class RideTrackResourceCatalogLoadContext : IDisposable {
  private readonly IReadOnlyList<Ovl> archives;
  private readonly Action<Ovl> disposeArchive;
  private bool disposed;

  public IReadOnlyList<string> LoadedCommonPaths { get; }
  public bool IsDisposed => disposed;

  internal RideTrackResourceCatalogLoadContext(
    IReadOnlyList<string> loadedCommonPaths,
    IReadOnlyList<Ovl> archives,
    Action<Ovl> disposeArchive
  ) {
    ArgumentNullException.ThrowIfNull(loadedCommonPaths);
    ArgumentNullException.ThrowIfNull(archives);
    ArgumentNullException.ThrowIfNull(disposeArchive);
    if (loadedCommonPaths.Count != archives.Count)
      throw new ArgumentException(
        "Loaded archive paths and archive instances must have the same count.",
        nameof(archives));

    LoadedCommonPaths = Array.AsReadOnly(loadedCommonPaths.ToArray());
    this.archives = Array.AsReadOnly(archives.ToArray());
    this.disposeArchive = disposeArchive;
  }

  public void Dispose() {
    if (disposed) return;

    disposed = true;
    GC.SuppressFinalize(this);
    var errors = new List<Exception>();
    foreach (var archive in archives.Reverse()) {
      try {
        disposeArchive(archive);
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    if (errors.Count > 0)
      throw new AggregateException(
        "Ride-track resource catalog archive disposal reported errors.",
        errors);
  }
}

/// <summary>
/// A usable exact catalog over every pair that loaded, plus typed evidence for missing pairs.
/// </summary>
internal sealed class RideTrackResourceCatalogLoadResult : IDisposable {
  public RideTrackResourceCatalog Catalog { get; }
  public RideTrackResourceCatalogLoadContext Context { get; }
  public IReadOnlyList<RideTrackResourceCatalogLoadIssue> Issues { get; }
  public bool IsComplete => Issues.Count == 0;

  internal RideTrackResourceCatalogLoadResult(
    RideTrackResourceCatalog catalog,
    RideTrackResourceCatalogLoadContext context,
    IReadOnlyList<RideTrackResourceCatalogLoadIssue> issues
  ) {
    ArgumentNullException.ThrowIfNull(catalog);
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(issues);
    Catalog = catalog;
    Context = context;
    Issues = Array.AsReadOnly(issues.ToArray());
  }

  public void Dispose() => Context.Dispose();
}

/// <summary>
/// Loads only DAT-named ride-track OVL pairs and their serialized dependency closures.
/// </summary>
/// <remarks>
/// External references are resolved relative to the declaring pair, matching paired OVL loading.
/// Missing pairs remain typed issues. The loader never enumerates directories or searches by a bare
/// resource name, so an absent or misplaced dependency cannot bind to an unrelated archive.
/// </remarks>
internal static class RideTrackResourceCatalogLoader {
  private const string CommonSuffix = ".common.ovl";
  private const string UniqueSuffix = ".unique.ovl";

  public static RideTrackResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements
  ) => Load(
    installRoot,
    placements,
    new FileSystemRideTrackResourceCatalogLoaderSource(),
    RideTrackResourceCatalogLoaderLimits.Default);

  internal static RideTrackResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements,
    IRideTrackResourceCatalogLoaderSource source
  ) => Load(
    installRoot,
    placements,
    source,
    RideTrackResourceCatalogLoaderLimits.Default);

  internal static RideTrackResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements,
    IRideTrackResourceCatalogLoaderSource source,
    RideTrackResourceCatalogLoaderLimits limits
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    ArgumentNullException.ThrowIfNull(placements);
    ArgumentNullException.ThrowIfNull(source);
    ValidateLimits(limits);
    if (placements.Count > limits.MaximumPlacements)
      throw Invalid(
        $"DAT placement count {placements.Count} exceeds {limits.MaximumPlacements}");

    var root = NormalizeRoot(installRoot);
    var roots = BuildRootRequests(root, placements, limits);
    var issues = new List<RideTrackResourceCatalogLoadIssue>();
    var loaded = new List<LoadedPair>();
    try {
      LoadClosure(root, roots, source, limits, loaded, issues);
      var decoded = DecodePairs(loaded, source);
      var closures = BuildDependencyClosures(loaded, decoded, limits);
      var sections = decoded.SelectMany(pair => pair.TrackSections).ToArray();
      var scenery = decoded.SelectMany(pair => pair.SceneryItems).ToArray();
      var splines = decoded.SelectMany(pair => pair.Splines).ToArray();
      var rides = decoded.SelectMany(pair => pair.TrackedRides).ToArray();
      var sectionGraph = TrackSectionResourceGraphResolver.Resolve(
        sections,
        scenery,
        splines,
        closures);
      var rideGraph = TrackedRideTrackResourceGraphResolver.Resolve(
        rides,
        sections,
        splines,
        closures);
      var placementSources = BuildPlacementSources(roots, decoded);
      var catalog = new RideTrackResourceCatalog(
        placementSources,
        sectionGraph,
        rideGraph);
      var context = new RideTrackResourceCatalogLoadContext(
        loaded.Select(pair => pair.CommonPath).ToArray(),
        loaded.Select(pair => pair.Archive).ToArray(),
        source.DisposePair);
      return new RideTrackResourceCatalogLoadResult(catalog, context, issues);
    } catch (Exception primaryError) {
      var cleanupErrors = DisposeLoadedPairs(loaded, source);
      if (cleanupErrors.Count == 0) throw;
      throw new AggregateException(
        "Ride-track resource catalog load failed and cleanup also reported errors.",
        [primaryError, .. cleanupErrors]);
    }
  }

  private static IReadOnlyList<RootRequest> BuildRootRequests(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements,
    RideTrackResourceCatalogLoaderLimits limits
  ) {
    var roots = new Dictionary<string, RootRequest>(StringComparer.OrdinalIgnoreCase);
    foreach (var placement in placements) {
      if (placement is null)
        throw new ArgumentException(
          "Ride-track placements cannot contain null.",
          nameof(placements));
      ValidateIdentifier(placement.OverlayPath, "DAT overlay path", limits);
      ValidateBareName(placement.ObjectKey, "DAT track object key", limits);
      var commonPath = ResolveExactCommonPath(installRoot, placement.OverlayPath);
      if (!roots.TryGetValue(commonPath, out var request)) {
        if (roots.Count >= limits.MaximumPairs)
          throw Invalid($"root pair count exceeds {limits.MaximumPairs}");
        request = new RootRequest(commonPath);
        roots.Add(commonPath, request);
      }
      request.Add(placement.OverlayPath, placement.ObjectKey);
    }
    return roots.Values.ToArray();
  }

  private static void LoadClosure(
    string installRoot,
    IReadOnlyList<RootRequest> roots,
    IRideTrackResourceCatalogLoaderSource source,
    RideTrackResourceCatalogLoaderLimits limits,
    ICollection<LoadedPair> loaded,
    ICollection<RideTrackResourceCatalogLoadIssue> issues
  ) {
    var queue = new Queue<LoadNode>();
    var scheduled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var root in roots) {
      if (!scheduled.Add(root.CommonPath)) continue;
      queue.Enqueue(new LoadNode(root.CommonPath, 0, null, root.OverlayPaths[0], true));
    }

    var loadedByPath = new Dictionary<string, LoadedPair>(StringComparer.OrdinalIgnoreCase);
    var dependencyEdgeCount = 0;
    while (queue.Count > 0) {
      var node = queue.Dequeue();
      var uniquePath = ToUniquePath(node.CommonPath);
      var commonExists = source.FileExists(node.CommonPath);
      var uniqueExists = source.FileExists(uniquePath);
      if (!commonExists || !uniqueExists) {
        issues.Add(new RideTrackResourceCatalogLoadIssue(
          node.IsRoot
            ? RideTrackResourceCatalogLoadIssueKind.MissingRootPair
            : RideTrackResourceCatalogLoadIssueKind.MissingDependencyPair,
          node.DeclaringCommonPath,
          node.Reference,
          node.CommonPath,
          uniquePath,
          commonExists,
          uniqueExists));
        continue;
      }

      var archive = source.LoadPair(node.CommonPath)
        ?? throw Invalid($"OVL source returned null for '{node.CommonPath}'");
      var pair = new LoadedPair(node.CommonPath, uniquePath, archive);
      loaded.Add(pair);
      loadedByPath.Add(node.CommonPath, pair);

      var references = source.GetExternalReferences(archive)
        ?? throw Invalid($"archive '{node.CommonPath}' returned a null dependency list");
      if (references.Count > limits.MaximumDependenciesPerPair)
        throw Invalid(
          $"archive '{node.CommonPath}' dependency count {references.Count} exceeds " +
          $"{limits.MaximumDependenciesPerPair}");
      dependencyEdgeCount = checked(dependencyEdgeCount + references.Count);
      if (dependencyEdgeCount > limits.MaximumDependencyEdges)
        throw Invalid(
          $"dependency edge count exceeds {limits.MaximumDependencyEdges}");

      foreach (var reference in references) {
        var dependencyPath = ResolveDependencyCommonPath(
          installRoot,
          node.CommonPath,
          reference);
        if (!pair.DependencyCommonPaths.Contains(
          dependencyPath,
          StringComparer.OrdinalIgnoreCase))
          pair.DependencyCommonPaths.Add(dependencyPath);
        if (!scheduled.Add(dependencyPath)) continue;
        if (scheduled.Count > limits.MaximumPairs)
          throw Invalid($"reachable pair count exceeds {limits.MaximumPairs}");
        if (node.Depth >= limits.MaximumDependencyDepth)
          throw Invalid(
            $"dependency depth exceeds {limits.MaximumDependencyDepth} at '{reference}'");
        queue.Enqueue(new LoadNode(
          dependencyPath,
          node.Depth + 1,
          node.CommonPath,
          reference,
          false));
      }
    }

    foreach (var pair in loadedByPath.Values) {
      pair.DependencyCommonPaths.RemoveAll(path => !loadedByPath.ContainsKey(path));
    }
  }

  private static IReadOnlyList<DecodedPair> DecodePairs(
    IReadOnlyCollection<LoadedPair> loaded,
    IRideTrackResourceCatalogLoaderSource source
  ) {
    var decoded = new List<DecodedPair>(loaded.Count);
    foreach (var pair in loaded) {
      decoded.Add(new DecodedPair(
        pair,
        Associate(
          pair,
          FileType.TrackSection,
          pair.UniquePath,
          source.ExtractTrackSections(pair.Archive),
          resource => resource.Name,
          (file, resource) => new TrackSectionResourceSource(file, resource),
          "TKS"),
        Associate(
          pair,
          FileType.SceneryItem,
          pair.UniquePath,
          source.ExtractSceneryItems(pair.Archive),
          resource => resource.Name,
          (file, resource) => new SceneryItemResourceSource(file, resource),
          "SID"),
        Associate(
          pair,
          FileType.Spline,
          pair.CommonPath,
          source.ExtractSplines(pair.Archive),
          resource => resource.Name,
          (file, resource) => new SplineResourceSource(file, resource),
          "SPL"),
        Associate(
          pair,
          FileType.TrackedRide,
          pair.UniquePath,
          source.ExtractTrackedRides(pair.Archive),
          resource => resource.Name,
          (file, resource) => new TrackedRideTrackResourceSource(file, resource),
          "TRR")));
    }
    return decoded;
  }

  private static IReadOnlyList<TSource> Associate<TResource, TSource>(
    LoadedPair pair,
    FileType type,
    string expectedPath,
    IReadOnlyList<TResource> resources,
    Func<TResource, string> getName,
    Func<OvlFile, TResource, TSource> create,
    string tag
  ) {
    if (resources is null)
      throw Invalid($"{tag} decoder returned null for '{pair.CommonPath}'");
    var expectedSuffix = type == FileType.Spline ? CommonSuffix : UniqueSuffix;
    var files = pair.Archive.Keys.Where(file =>
      file.Type == type &&
      file.Path.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (files.Length != resources.Count)
      throw Invalid(
        $"{tag} decoder returned {resources.Count} resources for {files.Length} OVL entries " +
        $"in '{pair.CommonPath}'");

    var sources = new TSource[files.Length];
    foreach (var index in Enumerable.Range(0, files.Length)) {
      var file = files[index];
      var resource = resources[index];
      if (file is null || resource is null)
        throw Invalid($"{tag} decoder returned a null identity in '{pair.CommonPath}'");
      if (!string.Equals(file.Path, expectedPath, StringComparison.OrdinalIgnoreCase))
        throw Invalid(
          $"{tag} resource '{file.Name}' claims archive '{file.Path}' outside exact pair " +
          $"'{pair.CommonPath}'");
      var resourceName = getName(resource);
      if (!string.Equals(file.Name, resourceName, StringComparison.OrdinalIgnoreCase))
        throw Invalid(
          $"{tag} OVL identity '{file.Name}' does not match decoded name '{resourceName}' " +
          $"in '{pair.CommonPath}'");
      sources[index] = create(file, resource);
    }
    return Array.AsReadOnly(sources);
  }

  private static IReadOnlyList<OvlResourceDependencyClosure> BuildDependencyClosures(
    IReadOnlyList<LoadedPair> loaded,
    IReadOnlyList<DecodedPair> decoded,
    RideTrackResourceCatalogLoaderLimits limits
  ) {
    var loadedByPath = loaded.ToDictionary(
      pair => pair.CommonPath,
      StringComparer.OrdinalIgnoreCase);
    var sourcePaths = decoded
      .SelectMany(pair => pair.TrackSections.Select(source => source.File.Path)
        .Concat(pair.TrackedRides.Select(source => source.File.Path)))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    var owningPairs = new Dictionary<string, LoadedPair>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in loaded) {
      owningPairs.Add(pair.CommonPath, pair);
      owningPairs.Add(pair.UniquePath, pair);
    }

    var closures = new List<OvlResourceDependencyClosure>(sourcePaths.Length);
    foreach (var sourcePath in sourcePaths) {
      if (!owningPairs.TryGetValue(sourcePath, out var owner))
        throw Invalid($"referring archive '{sourcePath}' is not an exact loaded pair half");
      var queue = new Queue<string>();
      var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var targets = new List<string>();
      queue.Enqueue(owner.CommonPath);
      while (queue.Count > 0) {
        var commonPath = queue.Dequeue();
        if (!reachable.Add(commonPath)) continue;
        if (reachable.Count > limits.MaximumPairs)
          throw Invalid($"dependency closure exceeds {limits.MaximumPairs} pairs");
        var pair = loadedByPath[commonPath];
        targets.Add(pair.CommonPath);
        targets.Add(pair.UniquePath);
        foreach (var dependency in pair.DependencyCommonPaths)
          queue.Enqueue(dependency);
      }
      closures.Add(new OvlResourceDependencyClosure(sourcePath, targets));
    }
    return closures;
  }

  private static IReadOnlyList<RideTrackSectionResourceSource> BuildPlacementSources(
    IReadOnlyList<RootRequest> roots,
    IReadOnlyList<DecodedPair> decoded
  ) {
    var decodedByPath = decoded.ToDictionary(
      pair => pair.Pair.CommonPath,
      StringComparer.OrdinalIgnoreCase);
    var sources = new List<RideTrackSectionResourceSource>();
    foreach (var root in roots) {
      if (!decodedByPath.TryGetValue(root.CommonPath, out var pair)) continue;
      var sectionsByName = pair.TrackSections.GroupBy(
        source => source.Resource.Name,
        StringComparer.OrdinalIgnoreCase).ToDictionary(
          group => group.Key,
          group => group.ToArray(),
          StringComparer.OrdinalIgnoreCase);
      foreach (var overlay in root.ResourcesByOverlay) {
        foreach (var resourceName in overlay.Value) {
          if (!sectionsByName.TryGetValue(resourceName, out var candidates)) continue;
          if (candidates.Length != 1)
            throw Invalid(
              $"overlay '{overlay.Key}' TKS '{resourceName}' has multiple exact root targets");
          var candidate = candidates[0];
          sources.Add(new RideTrackSectionResourceSource(
            overlay.Key,
            candidate.File,
            candidate.Resource));
        }
      }
    }
    return sources;
  }

  private static string ResolveExactCommonPath(string installRoot, string overlayPath) {
    if (!string.Equals(overlayPath, overlayPath.Trim(), StringComparison.Ordinal))
      throw Invalid($"DAT overlay path '{overlayPath}' has outer whitespace");
    if (overlayPath.IndexOfAny(['*', '?']) >= 0)
      throw Invalid($"DAT overlay path '{overlayPath}' contains a wildcard");

    var normalized = NormalizeSeparators(overlayPath);
    if (normalized.EndsWith(CommonSuffix, StringComparison.OrdinalIgnoreCase))
      normalized = normalized[..^CommonSuffix.Length];
    else if (normalized.EndsWith(UniqueSuffix, StringComparison.OrdinalIgnoreCase))
      normalized = normalized[..^UniqueSuffix.Length];
    else if (normalized.EndsWith(".ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid($"DAT overlay path '{overlayPath}' has an unknown OVL suffix");
    if (string.IsNullOrWhiteSpace(Path.GetFileName(normalized)))
      throw Invalid($"DAT overlay path '{overlayPath}' names no OVL pair");

    var commonPath = Path.GetFullPath(
      Path.Combine(installRoot, normalized + CommonSuffix));
    if (!IsContainedBy(installRoot, commonPath))
      throw Invalid($"DAT overlay path '{overlayPath}' leaves the installation root");
    return commonPath;
  }

  private static string ResolveDependencyCommonPath(
    string installRoot,
    string declaringCommonPath,
    string reference
  ) {
    if (string.IsNullOrWhiteSpace(reference))
      throw Invalid($"archive '{declaringCommonPath}' declares an empty dependency");
    if (!string.Equals(reference, reference.Trim(), StringComparison.Ordinal))
      throw Invalid(
        $"archive '{declaringCommonPath}' dependency '{reference}' has outer whitespace");
    if (reference.IndexOfAny(['*', '?']) >= 0)
      throw Invalid(
        $"archive '{declaringCommonPath}' dependency '{reference}' contains a wildcard");

    var normalized = NormalizeSeparators(reference);
    if (normalized.EndsWith(CommonSuffix, StringComparison.OrdinalIgnoreCase))
      normalized = normalized[..^CommonSuffix.Length];
    else if (normalized.EndsWith(UniqueSuffix, StringComparison.OrdinalIgnoreCase))
      normalized = normalized[..^UniqueSuffix.Length];
    else if (normalized.EndsWith(".ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"archive '{declaringCommonPath}' dependency '{reference}' has an unknown OVL suffix");
    if (string.IsNullOrWhiteSpace(Path.GetFileName(normalized)))
      throw Invalid(
        $"archive '{declaringCommonPath}' dependency '{reference}' names no OVL pair");

    var declaringDirectory = Path.GetDirectoryName(declaringCommonPath)
      ?? throw Invalid($"archive path '{declaringCommonPath}' has no directory");
    var commonPath = Path.GetFullPath(
      Path.Combine(declaringDirectory, normalized + CommonSuffix));
    if (!IsContainedBy(installRoot, commonPath))
      throw Invalid(
        $"archive '{declaringCommonPath}' dependency '{reference}' leaves the install root");
    return commonPath;
  }

  private static string NormalizeSeparators(string path) => path
    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
    .Replace('\\', Path.DirectorySeparatorChar)
    .Replace('/', Path.DirectorySeparatorChar);

  private static string NormalizeRoot(string root) =>
    Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

  private static bool IsContainedBy(string root, string path) {
    var relative = Path.GetRelativePath(root, path);
    return !Path.IsPathRooted(relative) &&
      !string.Equals(relative, "..", StringComparison.Ordinal) &&
      !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
      !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
  }

  private static string ToUniquePath(string commonPath) =>
    commonPath[..^CommonSuffix.Length] + UniqueSuffix;

  private static void ValidateBareName(
    string value,
    string description,
    RideTrackResourceCatalogLoaderLimits limits
  ) {
    ValidateIdentifier(value, description, limits);
    if (value.Contains(':', StringComparison.Ordinal))
      throw Invalid($"{description} '{value}' is tagged or type-ambiguous");
  }

  private static void ValidateIdentifier(
    string value,
    string description,
    RideTrackResourceCatalogLoaderLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(value)) throw Invalid($"{description} is empty");
    if (value.Length > limits.MaximumIdentifierLength)
      throw Invalid(
        $"{description} exceeds {limits.MaximumIdentifierLength} characters");
  }

  private static void ValidateLimits(RideTrackResourceCatalogLoaderLimits limits) {
    if (limits.MaximumPlacements <= 0 ||
        limits.MaximumPairs <= 0 ||
        limits.MaximumDependenciesPerPair <= 0 ||
        limits.MaximumDependencyEdges <= 0 ||
        limits.MaximumDependencyDepth < 0 ||
        limits.MaximumIdentifierLength <= 0)
      throw new ArgumentOutOfRangeException(
        nameof(limits),
        "Ride-track catalog loader limits must be positive.");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid ride-track resource catalog load: {message}.");

  private static List<Exception> DisposeLoadedPairs(
    IEnumerable<LoadedPair> loaded,
    IRideTrackResourceCatalogLoaderSource source
  ) {
    var errors = new List<Exception>();
    foreach (var pair in loaded.Reverse()) {
      try {
        source.DisposePair(pair.Archive);
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private sealed class RootRequest(string commonPath) {
    private readonly Dictionary<string, HashSet<string>> resourcesByOverlay =
      new(StringComparer.OrdinalIgnoreCase);

    public string CommonPath { get; } = commonPath;
    public IReadOnlyDictionary<string, HashSet<string>> ResourcesByOverlay =>
      resourcesByOverlay;
    public IReadOnlyList<string> OverlayPaths => resourcesByOverlay.Keys.ToArray();

    public void Add(string overlayPath, string resourceName) {
      if (!resourcesByOverlay.TryGetValue(overlayPath, out var names)) {
        names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        resourcesByOverlay.Add(overlayPath, names);
      }
      names.Add(resourceName);
    }
  }

  private sealed class LoadedPair(
    string commonPath,
    string uniquePath,
    Ovl archive
  ) {
    public string CommonPath { get; } = commonPath;
    public string UniquePath { get; } = uniquePath;
    public Ovl Archive { get; } = archive;
    public List<string> DependencyCommonPaths { get; } = [];
  }

  private sealed record DecodedPair(
    LoadedPair Pair,
    IReadOnlyList<TrackSectionResourceSource> TrackSections,
    IReadOnlyList<SceneryItemResourceSource> SceneryItems,
    IReadOnlyList<SplineResourceSource> Splines,
    IReadOnlyList<TrackedRideTrackResourceSource> TrackedRides
  );

  private sealed record LoadNode(
    string CommonPath,
    int Depth,
    string? DeclaringCommonPath,
    string Reference,
    bool IsRoot
  );
}

internal readonly record struct RideTrackResourceCatalogLoaderLimits(
  int MaximumPlacements,
  int MaximumPairs,
  int MaximumDependenciesPerPair,
  int MaximumDependencyEdges,
  int MaximumDependencyDepth,
  int MaximumIdentifierLength
) {
  public static RideTrackResourceCatalogLoaderLimits Default { get; } = new(
    MaximumPlacements: 100_000,
    MaximumPairs: 4_096,
    MaximumDependenciesPerPair: 4_096,
    MaximumDependencyEdges: 100_000,
    MaximumDependencyDepth: 64,
    MaximumIdentifierLength: 4_096);
}

internal interface IRideTrackResourceCatalogLoaderSource {
  bool FileExists(string path);
  Ovl LoadPair(string commonOvlPath);
  IReadOnlyList<string> GetExternalReferences(Ovl archive);
  IReadOnlyList<TrackSection> ExtractTrackSections(Ovl archive);
  IReadOnlyList<SceneryItem> ExtractSceneryItems(Ovl archive);
  IReadOnlyList<Spline> ExtractSplines(Ovl archive);
  IReadOnlyList<TrackedRide> ExtractTrackedRides(Ovl archive);
  void DisposePair(Ovl archive);
}

internal sealed class FileSystemRideTrackResourceCatalogLoaderSource
  : IRideTrackResourceCatalogLoaderSource {
  public bool FileExists(string path) => File.Exists(path);
  public Ovl LoadPair(string commonOvlPath) => Ovl.Load(commonOvlPath);
  public IReadOnlyList<string> GetExternalReferences(Ovl archive) =>
    archive.ExternalReferences;
  public IReadOnlyList<TrackSection> ExtractTrackSections(Ovl archive) =>
    TrackSections.Extract(archive);
  public IReadOnlyList<SceneryItem> ExtractSceneryItems(Ovl archive) =>
    SceneryItems.Extract(archive);
  public IReadOnlyList<Spline> ExtractSplines(Ovl archive) => Splines.Extract(archive);
  public IReadOnlyList<TrackedRide> ExtractTrackedRides(Ovl archive) =>
    TrackedRides.Extract(archive);
  public void DisposePair(Ovl archive) => archive.Dispose();
}
