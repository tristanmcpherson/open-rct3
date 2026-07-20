// Ride Track Resource Catalog Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
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

/// <summary>An exact resource symbol in one retained ride-track OVL pair.</summary>
internal sealed record RideTrackResourceCatalogEntry(Ovl Archive, OvlFile File);

/// <summary>Owns every paired OVL retained by a loaded ride-track resource catalog.</summary>
internal sealed class RideTrackResourceCatalogLoadContext : IDisposable {
  private const int MaximumAllowedArchivePaths = 4_096;
  private const int MaximumIdentifierLength = 4_096;
  private const int MaximumScannedResources = 1_000_000;
  private readonly object syncRoot = new();
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

  /// <summary>
  /// Finds one exact tagged resource only within a caller-proven archive-path whitelist.
  /// </summary>
  /// <remarks>
  /// Paths are compared directly with retained <see cref="OvlFile.Path"/> values. No filesystem
  /// probing, path guessing, dependency expansion, or substring matching is performed. Multiple
  /// exact definitions inside the whitelist are rejected as ambiguous.
  /// </remarks>
  internal RideTrackResourceCatalogEntry? FindExactResource(
    IReadOnlyList<string> allowedArchivePaths,
    string taggedReference,
    FileType expectedType
  ) {
    ArgumentNullException.ThrowIfNull(allowedArchivePaths);
    ValidateKnownType(expectedType);
    var resourceName = ParseExactTaggedReference(taggedReference, expectedType);
    var allowedPaths = ValidateAllowedPaths(allowedArchivePaths);

    lock (syncRoot) {
      ObjectDisposedException.ThrowIf(disposed, this);
      RideTrackResourceCatalogEntry? match = null;
      var scannedResources = 0;
      foreach (var archive in archives) {
        if (archive == null)
          throw InvalidLookup("retained archive list contains null");
        foreach (var file in archive.Keys) {
          if (!allowedPaths.Contains(file.Path)) continue;
          scannedResources = checked(scannedResources + 1);
          if (scannedResources > MaximumScannedResources)
            throw InvalidLookup(
              $"resource scan exceeds the limit {MaximumScannedResources}");
          if (file.Type != expectedType ||
              !string.Equals(file.Name, resourceName, StringComparison.OrdinalIgnoreCase))
            continue;

          if (match != null)
            throw InvalidLookup(
              $"resource '{taggedReference}' is defined more than once inside the exact " +
              "archive whitelist");
          match = new RideTrackResourceCatalogEntry(archive, file);
        }
      }
      return match;
    }
  }

  public void Dispose() {
    lock (syncRoot) {
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

  private static HashSet<string> ValidateAllowedPaths(
    IReadOnlyList<string> allowedArchivePaths
  ) {
    if (allowedArchivePaths.Count == 0)
      throw InvalidLookup("archive whitelist is empty");
    if (allowedArchivePaths.Count > MaximumAllowedArchivePaths)
      throw InvalidLookup(
        $"archive whitelist exceeds the limit {MaximumAllowedArchivePaths}");

    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in allowedArchivePaths) {
      ValidateIdentifier(path, "archive whitelist path");
      if (path.IndexOfAny(['*', '?']) >= 0)
        throw InvalidLookup($"archive whitelist path '{path}' contains a wildcard");
      if (!path.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase) &&
          !path.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))
        throw InvalidLookup(
          $"archive whitelist path '{path}' is not one exact OVL pair half");
      if (!result.Add(path))
        throw InvalidLookup($"archive whitelist repeats path '{path}'");
    }
    return result;
  }

  private static string ParseExactTaggedReference(
    string taggedReference,
    FileType expectedType
  ) {
    ValidateIdentifier(taggedReference, "tagged resource reference");
    var separator = taggedReference.IndexOf(':');
    if (separator <= 0 ||
        separator != taggedReference.LastIndexOf(':') ||
        separator == taggedReference.Length - 1)
      throw InvalidLookup(
        $"resource reference '{taggedReference}' is not one exact name:tag identity");

    var name = taggedReference[..separator];
    var tag = taggedReference[(separator + 1)..];
    ValidateIdentifier(name, "resource name");
    var expectedTag = expectedType.ToTagString();
    if (!tag.Equals(expectedTag, StringComparison.OrdinalIgnoreCase))
      throw InvalidLookup(
        $"resource reference '{taggedReference}' identifies '{tag}' instead of " +
        $"'{expectedTag}'");
    return name;
  }

  private static void ValidateKnownType(FileType type) {
    if (type == FileType.Unknown || !Enum.IsDefined(type))
      throw new ArgumentOutOfRangeException(
        nameof(type),
        type,
        "An exact known OVL resource type is required.");
  }

  private static void ValidateIdentifier(string? value, string description) {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > MaximumIdentifierLength ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw InvalidLookup(
        $"{description} is empty, padded, or exceeds {MaximumIdentifierLength} characters");
  }

  private static InvalidDataException InvalidLookup(string message) =>
    new($"Invalid ride-track resource lookup: {message}.");
}

/// <summary>
/// A usable exact catalog over every pair that loaded, plus typed evidence for missing pairs.
/// </summary>
internal sealed class RideTrackResourceCatalogLoadResult : IDisposable {
  public RideTrackResourceCatalog Catalog { get; }
  public RideInstanceResourceLoadResult RideResources { get; }
  public RideTrackResourceCatalogLoadContext Context { get; }
  public IReadOnlyList<RideTrackResourceCatalogLoadIssue> Issues { get; }
  public bool IsComplete => Issues.Count == 0;

  internal RideTrackResourceCatalogLoadResult(
    RideTrackResourceCatalog catalog,
    RideTrackResourceCatalogLoadContext context,
    IReadOnlyList<RideTrackResourceCatalogLoadIssue> issues
  ) : this(
    catalog,
    RideInstanceResourceLoadResult.Empty,
    context,
    issues) { }

  internal RideTrackResourceCatalogLoadResult(
    RideTrackResourceCatalog catalog,
    RideInstanceResourceLoadResult rideResources,
    RideTrackResourceCatalogLoadContext context,
    IReadOnlyList<RideTrackResourceCatalogLoadIssue> issues
  ) {
    ArgumentNullException.ThrowIfNull(catalog);
    ArgumentNullException.ThrowIfNull(rideResources);
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(issues);
    Catalog = catalog;
    RideResources = rideResources;
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
/// Missing declared pairs remain typed issues. Compatible TRR train names use only the original
/// runtime's three bounded exact overlay conventions; the loader never enumerates directories,
/// so an absent or misplaced resource cannot bind to an unrelated archive.
/// </remarks>
internal static class RideTrackResourceCatalogLoader {
  private const string CommonSuffix = ".common.ovl";
  private const string UniqueSuffix = ".unique.ovl";
  private static readonly string[] RideTrainCandidateBases = [
    "Cars",
    @"Cars\CoasterCars",
    @"Cars\TrackedRideCars",
  ];

  public static RideTrackResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements
  ) => Load(
    installRoot,
    placements,
    new FileSystemRideTrackResourceCatalogLoaderSource(),
    RideTrackResourceCatalogLoaderLimits.Default);

  public static RideTrackResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements,
    IReadOnlyList<DatTrackedRideInstanceData> rideInstances
  ) => Load(
    installRoot,
    placements,
    rideInstances,
    [],
    new FileSystemRideTrackResourceCatalogLoaderSource(),
    RideTrackResourceCatalogLoaderLimits.Default);

  public static RideTrackResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements,
    IReadOnlyList<DatTrackedRideInstanceData> rideInstances,
    IReadOnlyList<DatRideTrainInstanceData> rideTrainInstances
  ) => Load(
    installRoot,
    placements,
    rideInstances,
    rideTrainInstances,
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
    IReadOnlyList<DatTrackedRideInstanceData> rideInstances,
    IRideTrackResourceCatalogLoaderSource source
  ) => Load(
    installRoot,
    placements,
    rideInstances,
    [],
    source,
    RideTrackResourceCatalogLoaderLimits.Default);

  internal static RideTrackResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements,
    IReadOnlyList<DatTrackedRideInstanceData> rideInstances,
    IReadOnlyList<DatRideTrainInstanceData> rideTrainInstances,
    IRideTrackResourceCatalogLoaderSource source
  ) => Load(
    installRoot,
    placements,
    rideInstances,
    rideTrainInstances,
    source,
    RideTrackResourceCatalogLoaderLimits.Default);

  internal static RideTrackResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements,
    IRideTrackResourceCatalogLoaderSource source,
    RideTrackResourceCatalogLoaderLimits limits
  ) => LoadCore(
    installRoot,
    placements,
    [],
    [],
    source,
    limits,
    loadRideResources: false);

  internal static RideTrackResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements,
    IReadOnlyList<DatTrackedRideInstanceData> rideInstances,
    IRideTrackResourceCatalogLoaderSource source,
    RideTrackResourceCatalogLoaderLimits limits
  ) => LoadCore(
    installRoot,
    placements,
    rideInstances,
    [],
    source,
    limits,
    loadRideResources: true);

  internal static RideTrackResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements,
    IReadOnlyList<DatTrackedRideInstanceData> rideInstances,
    IReadOnlyList<DatRideTrainInstanceData> rideTrainInstances,
    IRideTrackResourceCatalogLoaderSource source,
    RideTrackResourceCatalogLoaderLimits limits
  ) => LoadCore(
    installRoot,
    placements,
    rideInstances,
    rideTrainInstances,
    source,
    limits,
    loadRideResources: true);

  private static RideTrackResourceCatalogLoadResult LoadCore(
    string installRoot,
    IReadOnlyList<RideTrackPlacement> placements,
    IReadOnlyList<DatTrackedRideInstanceData> rideInstances,
    IReadOnlyList<DatRideTrainInstanceData> rideTrainInstances,
    IRideTrackResourceCatalogLoaderSource source,
    RideTrackResourceCatalogLoaderLimits limits,
    bool loadRideResources
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    ArgumentNullException.ThrowIfNull(placements);
    ArgumentNullException.ThrowIfNull(rideInstances);
    ArgumentNullException.ThrowIfNull(rideTrainInstances);
    ArgumentNullException.ThrowIfNull(source);
    ValidateLimits(limits);
    if (placements.Count > limits.MaximumPlacements)
      throw Invalid(
        $"DAT placement count {placements.Count} exceeds {limits.MaximumPlacements}");
    if (rideInstances.Count > limits.MaximumRideInstances)
      throw Invalid(
        $"DAT ride-instance count {rideInstances.Count} exceeds " +
        $"{limits.MaximumRideInstances}");
    if (rideTrainInstances.Count > limits.MaximumRideInstances)
      throw Invalid(
        $"DAT ride-train-instance count {rideTrainInstances.Count} exceeds " +
        $"{limits.MaximumRideInstances}");

    var root = NormalizeRoot(installRoot);
    var roots = BuildRootRequests(
      root,
      placements,
      rideInstances,
      rideTrainInstances,
      limits);
    var issues = new List<RideTrackResourceCatalogLoadIssue>();
    var loaded = new List<LoadedPair>();
    try {
      LoadClosure(root, roots, source, limits, loaded, issues);
      var decoded = DecodePairs(loaded, source, loadRideResources);
      if (loadRideResources) {
        LoadCompatibleRideTrainRoots(
          root,
          roots,
          decoded,
          rideInstances,
          rideTrainInstances,
          source,
          limits,
          loaded,
          issues);
        decoded = DecodePairs(loaded, source, decodeRideResources: true);
      }
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
      var rideResources = loadRideResources
        ? BuildRideResourceResult(
          root,
          roots,
          decoded,
          closures,
          rideInstances,
          rideTrainInstances,
          source,
          limits)
        : RideInstanceResourceLoadResult.Empty;
      var context = new RideTrackResourceCatalogLoadContext(
        loaded.Select(pair => pair.CommonPath).ToArray(),
        loaded.Select(pair => pair.Archive).ToArray(),
        source.DisposePair);
      return new RideTrackResourceCatalogLoadResult(
        catalog,
        rideResources,
        context,
        issues);
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
    IReadOnlyList<DatTrackedRideInstanceData> rideInstances,
    IReadOnlyList<DatRideTrainInstanceData> rideTrainInstances,
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
      request.AddTrack(placement.OverlayPath, placement.ObjectKey);
    }
    var instanceIds = new HashSet<ulong>();
    foreach (var instance in rideInstances) {
      if (instance is null)
        throw new ArgumentException(
          "Ride instances cannot contain null.",
          nameof(rideInstances));
      if (instance.EntryId == 0 || !instanceIds.Add(instance.EntryId))
        throw Invalid(
          $"DAT ride-instance entry ID {instance.EntryId} is missing or duplicated");
      ValidateIdentifier(
        instance.TrackedRideOverlayName,
        "DAT tracked-ride overlay path",
        limits);
      var resourceName = ParseTaggedName(
        instance.TrackedRideSymbolName,
        "trr",
        "DAT tracked-ride symbol",
        limits);
      var commonPath = ResolveExactCommonPath(
        installRoot,
        instance.TrackedRideOverlayName);
      if (!roots.TryGetValue(commonPath, out var request)) {
        if (roots.Count >= limits.MaximumPairs)
          throw Invalid($"root pair count exceeds {limits.MaximumPairs}");
        request = new RootRequest(commonPath);
        roots.Add(commonPath, request);
      }
      request.AddRide(instance.TrackedRideOverlayName, resourceName);
    }
    AddSavedRideTrainRoots(
      installRoot,
      roots,
      rideInstances,
      rideTrainInstances,
      limits);
    return roots.Values.ToArray();
  }

  private static void AddSavedRideTrainRoots(
    string installRoot,
    IDictionary<string, RootRequest> roots,
    IReadOnlyList<DatTrackedRideInstanceData> rideInstances,
    IReadOnlyList<DatRideTrainInstanceData> rideTrainInstances,
    RideTrackResourceCatalogLoaderLimits limits
  ) {
    if (rideTrainInstances.Count == 0) return;

    var trainsById = new Dictionary<ulong, DatRideTrainInstanceData>();
    foreach (var train in rideTrainInstances) {
      if (train is null)
        throw new ArgumentException(
          "Ride-train instances cannot contain null.",
          nameof(rideTrainInstances));
      if (train.EntryId == 0 || !trainsById.TryAdd(train.EntryId, train))
        throw Invalid(
          $"DAT ride-train-instance entry ID {train.EntryId} is missing or duplicated");
    }

    var referencedTrainIds = new HashSet<ulong>();
    foreach (var ride in rideInstances) {
      foreach (var trainId in ride.Trains) {
        if (!referencedTrainIds.Add(trainId))
          throw Invalid($"DAT ride-train-instance entry ID {trainId} is referenced more than once");
        if (!trainsById.TryGetValue(trainId, out var train))
          throw Invalid(
            $"DAT ride instance {ride.EntryId} references missing ride-train instance {trainId}");
        if (train.TrackedRideInstance != ride.EntryId)
          throw Invalid(
            $"DAT ride instance {ride.EntryId} references ride-train instance {trainId}, " +
            $"whose reciprocal owner is {train.TrackedRideInstance}");

        ValidateIdentifier(
          train.RideTrainOverlayName,
          "DAT ride-train overlay path",
          limits);
        var resourceName = ParseTaggedName(
          train.RideTrainSymbolName,
          "rit",
          "DAT ride-train symbol",
          limits);
        var commonPath = ResolveExactCommonPath(
          installRoot,
          train.RideTrainOverlayName);
        var request = GetOrAddRootRequest(roots, commonPath, limits);
        request.AddTrain(train.RideTrainOverlayName, resourceName);
      }
    }
  }

  private static void LoadCompatibleRideTrainRoots(
    string installRoot,
    IReadOnlyList<RootRequest> initialRoots,
    IReadOnlyList<DecodedPair> decoded,
    IReadOnlyList<DatTrackedRideInstanceData> rideInstances,
    IReadOnlyList<DatRideTrainInstanceData> rideTrainInstances,
    IRideTrackResourceCatalogLoaderSource source,
    RideTrackResourceCatalogLoaderLimits limits,
    ICollection<LoadedPair> loaded,
    ICollection<RideTrackResourceCatalogLoadIssue> issues
  ) {
    var rideSources = BuildRideInstanceSources(initialRoots, decoded);
    var decodedByPath = decoded.ToDictionary(
      pair => pair.Pair.CommonPath,
      StringComparer.OrdinalIgnoreCase);
    var rideLinks = new RideInstanceResourceResolver(rideSources).ResolveAll(rideInstances);
    var savedTrainsById = rideTrainInstances.ToDictionary(train => train.EntryId);
    var savedTrainNamesByRide = new Dictionary<
      RideInstanceResourceSource,
      HashSet<string>>();
    foreach (var rideLink in rideLinks) {
      if (rideLink.Source == null) continue;
      foreach (var trainId in rideLink.Instance.Trains) {
        if (!savedTrainsById.TryGetValue(trainId, out var savedTrain)) continue;
        var trainName = ParseTaggedName(
          savedTrain.RideTrainSymbolName,
          "rit",
          "DAT ride-train symbol",
          limits);
        if (!savedTrainNamesByRide.TryGetValue(rideLink.Source, out var names)) {
          names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          savedTrainNamesByRide.Add(rideLink.Source, names);
        }
        names.Add(trainName);
      }
    }

    var trainNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var rideSource in rideSources) {
      foreach (var trainName in rideSource.Resource.TrainNames) {
        ValidatePathSegment(trainName, "TRR compatible ride-train name", limits);
        if (savedTrainNamesByRide.TryGetValue(rideSource, out var savedNames) &&
            savedNames.Contains(trainName)) continue;
        trainNames.Add(trainName);
      }
    }

    foreach (var trainName in trainNames) {
      foreach (var candidateBase in RideTrainCandidateBases) {
        var overlayPath = $@"{candidateBase}\{trainName}\{trainName}";
        var commonPath = ResolveExactCommonPath(installRoot, overlayPath);
        if (decodedByPath.TryGetValue(commonPath, out var existingPair)) {
          if (HasExactRideTrain(existingPair, trainName, overlayPath)) break;
          continue;
        }

        var commonExists = source.FileExists(commonPath);
        var uniqueExists = source.FileExists(ToUniquePath(commonPath));
        if (!commonExists || !uniqueExists) continue;

        var request = new RootRequest(commonPath);
        request.AddTrain(overlayPath, trainName);
        LoadClosure(installRoot, [request], source, limits, loaded, issues);
        var loadedPair = loaded.Single(pair =>
          string.Equals(pair.CommonPath, commonPath, StringComparison.OrdinalIgnoreCase));
        var decodedPair = DecodePairs(
          [loadedPair],
          source,
          decodeRideResources: true).Single();
        decodedByPath.Add(commonPath, decodedPair);
        if (HasExactRideTrain(decodedPair, trainName, overlayPath)) break;
      }
    }
  }

  private static bool HasExactRideTrain(
    DecodedPair pair,
    string trainName,
    string overlayPath
  ) {
    var matches = pair.RideTrains.Count(train =>
      string.Equals(
        train.Resource.Name,
        trainName,
        StringComparison.OrdinalIgnoreCase));
    if (matches > 1)
      throw Invalid(
        $"candidate ride-train identity '{overlayPath}|{trainName}:rit' has multiple " +
        "exact decoded targets");
    return matches == 1;
  }

  private static RootRequest GetOrAddRootRequest(
    IDictionary<string, RootRequest> roots,
    string commonPath,
    RideTrackResourceCatalogLoaderLimits limits
  ) {
    if (roots.TryGetValue(commonPath, out var request)) return request;
    if (roots.Count >= limits.MaximumPairs)
      throw Invalid($"root pair count exceeds {limits.MaximumPairs}");
    request = new RootRequest(commonPath);
    roots.Add(commonPath, request);
    return request;
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
    var loadedByPath = loaded.ToDictionary(
      pair => pair.CommonPath,
      StringComparer.OrdinalIgnoreCase);
    var scheduled = new HashSet<string>(
      loadedByPath.Keys,
      StringComparer.OrdinalIgnoreCase);
    foreach (var root in roots) {
      if (!scheduled.Add(root.CommonPath)) continue;
      if (scheduled.Count > limits.MaximumPairs)
        throw Invalid($"reachable pair count exceeds {limits.MaximumPairs}");
      queue.Enqueue(new LoadNode(root.CommonPath, 0, null, root.FirstOverlayPath, true));
    }

    var dependencyEdgeCount = loaded.Sum(pair => pair.DependencyCommonPaths.Count);
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
    IRideTrackResourceCatalogLoaderSource source,
    bool decodeRideResources
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
          "TRR"),
        decodeRideResources ? Associate(
          pair,
          FileType.RideTrain,
          pair.UniquePath,
          source.ExtractRideTrains(pair.Archive),
          resource => resource.Name,
          (file, resource) => new DecodedResource<RideTrain>(file, resource),
          "RIT") : [],
        decodeRideResources ? Associate(
          pair,
          FileType.RideCar,
          pair.UniquePath,
          source.ExtractRideCars(pair.Archive),
          resource => resource.Name,
          (file, resource) => new DecodedResource<RideCar>(file, resource),
          "RIC") : [],
        decodeRideResources ? Associate(
          pair,
          FileType.SceneryItemVisual,
          pair.UniquePath,
          source.ExtractSceneryItemVisuals(pair.Archive),
          resource => resource.Name,
          (file, resource) => new RideVisualResourceSource(file, resource),
          "SVD") : []));
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
        .Concat(pair.TrackedRides.Select(source => source.File.Path))
        .Concat(pair.RideTrains.Select(source => source.File.Path))
        .Concat(pair.RideCars.Select(source => source.File.Path)))
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
      foreach (var overlay in root.TrackResourcesByOverlay) {
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

  private static RideInstanceResourceLoadResult BuildRideResourceResult(
    string installRoot,
    IReadOnlyList<RootRequest> roots,
    IReadOnlyList<DecodedPair> decoded,
    IReadOnlyList<OvlResourceDependencyClosure> closures,
    IReadOnlyList<DatTrackedRideInstanceData> instances,
    IReadOnlyList<DatRideTrainInstanceData> rideTrainInstances,
    IRideTrackResourceCatalogLoaderSource source,
    RideTrackResourceCatalogLoaderLimits limits
  ) {
    var instanceSources = BuildRideInstanceSources(roots, decoded);
    var instanceLinks = new RideInstanceResourceResolver(instanceSources)
      .ResolveAll(instances);
    var closuresByPath = closures.ToDictionary(
      closure => closure.SourcePath,
      StringComparer.OrdinalIgnoreCase);
    var decodedByPath = decoded.ToDictionary(
      pair => pair.Pair.CommonPath,
      StringComparer.OrdinalIgnoreCase);
    var rideTrainsById = rideTrainInstances.ToDictionary(train => train.EntryId);
    var trains = decoded.SelectMany(pair => pair.RideTrains).Select(source =>
      new RideTrainResourceSource(
        source.File,
        source.Resource,
        GetAllowedPaths(source.File.Path, closuresByPath))).ToArray();
    var savedTrainSources = BuildSavedRideTrainInstanceSources(
      roots,
      decoded,
      closuresByPath);
    var trainInstances = rideTrainInstances.Count == 0
      ? RideTrainInstanceResourceRegistry.Empty
      : RideTrainInstanceResourceRegistry.Build(
        instances,
        rideTrainInstances,
        savedTrainSources);
    var rides = instanceSources.Select(source => new TrackedRideResourceSource(
      source.File,
      source.Resource,
      BuildRideAllowedPaths(
        installRoot,
        source,
        instanceLinks,
        rideTrainsById,
        decodedByPath,
        closuresByPath,
        limits))).ToArray();
    var cars = decoded.SelectMany(pair => pair.RideCars).Select(source =>
      new RideCarResourceSource(
        source.File,
        source.Resource,
        GetAllowedPaths(source.File.Path, closuresByPath))).ToArray();
    var visuals = decoded.SelectMany(pair => pair.RideVisuals).ToArray();
    var graph = RideResourceGraphResolver.Resolve(rides, trains, cars, visuals);
    var shapeResources = RideVisualShapeResourceDecoder.Decode(
      decoded.Select(pair => pair.Pair.Archive).ToArray(),
      source.ExtractStaticShapes,
      source.ExtractBoneShapes,
      RideCarVisualResourceBridgeLimits.Default);
    var carVisuals = RideCarVisualResourceBridge.Resolve(graph, shapeResources);
    return new RideInstanceResourceLoadResult(
      instanceLinks,
      trainInstances,
      graph,
      carVisuals,
      new RideResourceDecodeCounts(
        decoded.Sum(pair => pair.TrackedRides.Count),
        trains.Length,
        cars.Length,
        visuals.Length));
  }

  private static IReadOnlyList<string> BuildRideAllowedPaths(
    string installRoot,
    RideInstanceResourceSource source,
    IReadOnlyList<RideInstanceResourceLink> instanceLinks,
    IReadOnlyDictionary<ulong, DatRideTrainInstanceData> rideTrainsById,
    IReadOnlyDictionary<string, DecodedPair> decodedByPath,
    IReadOnlyDictionary<string, OvlResourceDependencyClosure> closuresByPath,
    RideTrackResourceCatalogLoaderLimits limits
  ) {
    var allowed = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    AddClosure(source.File.Path);

    var savedTrainNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var link in instanceLinks) {
      if (!ReferenceEquals(link.Source, source)) continue;
      foreach (var trainId in link.Instance.Trains) {
        if (!rideTrainsById.TryGetValue(trainId, out var savedTrain)) continue;
        var trainName = ParseTaggedName(
          savedTrain.RideTrainSymbolName,
          "rit",
          "DAT ride-train symbol",
          limits);
        savedTrainNames.Add(trainName);
        var commonPath = ResolveExactCommonPath(
          installRoot,
          savedTrain.RideTrainOverlayName);
        if (!decodedByPath.TryGetValue(commonPath, out var pair)) continue;
        var matches = pair.RideTrains.Where(train =>
          string.Equals(
            train.Resource.Name,
            trainName,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1)
          throw Invalid(
            $"saved ride-train identity '{savedTrain.RideTrainOverlayName}|" +
            $"{savedTrain.RideTrainSymbolName}' has {matches.Length} exact decoded targets");
        if (matches.Length == 0) continue;
        AddClosure(matches[0].File.Path);
      }
    }

    foreach (var trainName in source.Resource.TrainNames) {
      ValidatePathSegment(trainName, "TRR compatible ride-train name", limits);
      if (savedTrainNames.Contains(trainName)) continue;
      var candidate = FindCompatibleRideTrain(
        installRoot,
        trainName,
        decodedByPath);
      if (candidate != null) AddClosure(candidate.File.Path);
    }
    return Array.AsReadOnly(allowed.ToArray());

    void AddClosure(string sourcePath) {
      foreach (var path in GetAllowedPaths(sourcePath, closuresByPath)) {
        if (seen.Add(path)) allowed.Add(path);
      }
    }
  }

  private static DecodedResource<RideTrain>? FindCompatibleRideTrain(
    string installRoot,
    string trainName,
    IReadOnlyDictionary<string, DecodedPair> decodedByPath
  ) {
    foreach (var candidateBase in RideTrainCandidateBases) {
      var overlayPath = $@"{candidateBase}\{trainName}\{trainName}";
      var commonPath = ResolveExactCommonPath(installRoot, overlayPath);
      if (!decodedByPath.TryGetValue(commonPath, out var pair)) continue;
      var matches = pair.RideTrains.Where(train =>
        string.Equals(
          train.Resource.Name,
          trainName,
          StringComparison.OrdinalIgnoreCase)).ToArray();
      if (matches.Length > 1)
        throw Invalid(
          $"candidate ride-train identity '{overlayPath}|{trainName}:rit' has multiple " +
          "exact decoded targets");
      if (matches.Length == 1) return matches[0];
    }
    return null;
  }

  private static IReadOnlyList<RideTrainInstanceResourceSource>
    BuildSavedRideTrainInstanceSources(
      IReadOnlyList<RootRequest> roots,
      IReadOnlyList<DecodedPair> decoded,
      IReadOnlyDictionary<string, OvlResourceDependencyClosure> closuresByPath
    ) {
    var decodedByPath = decoded.ToDictionary(
      pair => pair.Pair.CommonPath,
      StringComparer.OrdinalIgnoreCase);
    var sources = new List<RideTrainInstanceResourceSource>();
    foreach (var root in roots) {
      if (!decodedByPath.TryGetValue(root.CommonPath, out var pair)) continue;
      var trainsByName = pair.RideTrains.GroupBy(
        source => source.Resource.Name,
        StringComparer.OrdinalIgnoreCase).ToDictionary(
          group => group.Key,
          group => group.ToArray(),
          StringComparer.OrdinalIgnoreCase);
      foreach (var overlay in root.TrainResourcesByOverlay) {
        foreach (var resourceName in overlay.Value) {
          if (!trainsByName.TryGetValue(resourceName, out var candidates)) continue;
          if (candidates.Length != 1)
            throw Invalid(
              $"overlay '{overlay.Key}' RIT '{resourceName}' has multiple exact root targets");
          var candidate = candidates[0];
          sources.Add(new RideTrainInstanceResourceSource(
            overlay.Key,
            new RideTrainResourceSource(
              candidate.File,
              candidate.Resource,
              GetAllowedPaths(candidate.File.Path, closuresByPath))));
        }
      }
    }
    return Array.AsReadOnly(sources.ToArray());
  }

  private static IReadOnlyList<RideInstanceResourceSource> BuildRideInstanceSources(
    IReadOnlyList<RootRequest> roots,
    IReadOnlyList<DecodedPair> decoded
  ) {
    var decodedByPath = decoded.ToDictionary(
      pair => pair.Pair.CommonPath,
      StringComparer.OrdinalIgnoreCase);
    var sources = new List<RideInstanceResourceSource>();
    foreach (var root in roots) {
      if (!decodedByPath.TryGetValue(root.CommonPath, out var pair)) continue;
      var ridesByName = pair.TrackedRides.GroupBy(
        source => source.Resource.Name,
        StringComparer.OrdinalIgnoreCase).ToDictionary(
          group => group.Key,
          group => group.ToArray(),
          StringComparer.OrdinalIgnoreCase);
      foreach (var overlay in root.RideResourcesByOverlay) {
        foreach (var resourceName in overlay.Value) {
          if (!ridesByName.TryGetValue(resourceName, out var candidates)) continue;
          if (candidates.Length != 1)
            throw Invalid(
              $"overlay '{overlay.Key}' TRR '{resourceName}' has multiple exact root targets");
          var candidate = candidates[0];
          sources.Add(new RideInstanceResourceSource(
            overlay.Key,
            candidate.File,
            candidate.Resource));
        }
      }
    }
    return sources;
  }

  private static IReadOnlyList<string> GetAllowedPaths(
    string sourcePath,
    IReadOnlyDictionary<string, OvlResourceDependencyClosure> closures
  ) => closures.TryGetValue(sourcePath, out var closure)
    ? closure.AllowedTargetPaths
    : throw Invalid($"referring archive '{sourcePath}' has no dependency closure");

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

  private static void ValidatePathSegment(
    string value,
    string description,
    RideTrackResourceCatalogLoaderLimits limits
  ) {
    ValidateBareName(value, description, limits);
    if (value is "." or ".." || value.IndexOfAny(['\\', '/']) >= 0)
      throw Invalid($"{description} '{value}' is not one exact path segment");
  }

  private static string ParseTaggedName(
    string reference,
    string expectedTag,
    string description,
    RideTrackResourceCatalogLoaderLimits limits
  ) {
    ValidateIdentifier(reference, description, limits);
    var separator = reference.IndexOf(':');
    if (separator <= 0 ||
        separator != reference.LastIndexOf(':') ||
        separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals(
          expectedTag,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{description} '{reference}' is not one exact name:{expectedTag} identity");
    var name = reference[..separator];
    ValidateBareName(name, description, limits);
    return name;
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
    if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw Invalid($"{description} has outer whitespace");
  }

  private static void ValidateLimits(RideTrackResourceCatalogLoaderLimits limits) {
    if (limits.MaximumPlacements <= 0 ||
        limits.MaximumRideInstances <= 0 ||
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
    private readonly Dictionary<string, HashSet<string>> trackResourcesByOverlay =
      new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> rideResourcesByOverlay =
      new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> trainResourcesByOverlay =
      new(StringComparer.OrdinalIgnoreCase);

    public string CommonPath { get; } = commonPath;
    public IReadOnlyDictionary<string, HashSet<string>> TrackResourcesByOverlay =>
      trackResourcesByOverlay;
    public IReadOnlyDictionary<string, HashSet<string>> RideResourcesByOverlay =>
      rideResourcesByOverlay;
    public IReadOnlyDictionary<string, HashSet<string>> TrainResourcesByOverlay =>
      trainResourcesByOverlay;
    public string FirstOverlayPath => trackResourcesByOverlay.Keys
      .Concat(rideResourcesByOverlay.Keys)
      .Concat(trainResourcesByOverlay.Keys)
      .First();

    public void AddTrack(string overlayPath, string resourceName) =>
      Add(trackResourcesByOverlay, overlayPath, resourceName);

    public void AddRide(string overlayPath, string resourceName) =>
      Add(rideResourcesByOverlay, overlayPath, resourceName);

    public void AddTrain(string overlayPath, string resourceName) =>
      Add(trainResourcesByOverlay, overlayPath, resourceName);

    private static void Add(
      IDictionary<string, HashSet<string>> resourcesByOverlay,
      string overlayPath,
      string resourceName
    ) {
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
    IReadOnlyList<TrackedRideTrackResourceSource> TrackedRides,
    IReadOnlyList<DecodedResource<RideTrain>> RideTrains,
    IReadOnlyList<DecodedResource<RideCar>> RideCars,
    IReadOnlyList<RideVisualResourceSource> RideVisuals
  );

  private sealed record DecodedResource<T>(OvlFile File, T Resource);

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
  int MaximumRideInstances,
  int MaximumPairs,
  int MaximumDependenciesPerPair,
  int MaximumDependencyEdges,
  int MaximumDependencyDepth,
  int MaximumIdentifierLength
) {
  public static RideTrackResourceCatalogLoaderLimits Default { get; } = new(
    MaximumPlacements: 100_000,
    MaximumRideInstances: 100_000,
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
  IReadOnlyList<RideTrain> ExtractRideTrains(Ovl archive);
  IReadOnlyList<RideCar> ExtractRideCars(Ovl archive);
  IReadOnlyList<SceneryItemVisual> ExtractSceneryItemVisuals(Ovl archive);
  IReadOnlyList<StaticShape> ExtractStaticShapes(Ovl archive);
  IReadOnlyList<BoneShape> ExtractBoneShapes(Ovl archive);
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
  public IReadOnlyList<RideTrain> ExtractRideTrains(Ovl archive) =>
    RideTrains.Extract(archive);
  public IReadOnlyList<RideCar> ExtractRideCars(Ovl archive) => RideCars.Extract(archive);
  public IReadOnlyList<SceneryItemVisual> ExtractSceneryItemVisuals(Ovl archive) =>
    SceneryItemVisuals.Extract(archive);
  public IReadOnlyList<StaticShape> ExtractStaticShapes(Ovl archive) =>
    StaticShapes.Extract(archive);
  public IReadOnlyList<BoneShape> ExtractBoneShapes(Ovl archive) =>
    BoneShapes.Extract(archive);
  public void DisposePair(Ovl archive) => archive.Dispose();
}
