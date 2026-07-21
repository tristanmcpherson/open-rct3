// Animated Ride Resource Catalog Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenRCT3.Simulation;

internal enum AnimatedRideResourceLinkStatus {
  Resolved,
  MissingSceneryItem,
  AmbiguousSceneryItem,
  WrongSceneryType,
}

internal enum AnimatedRideResourceCatalogIssueKind {
  MissingRootPair,
  MissingDependencyPair,
}

internal sealed record AnimatedRideResourceCatalogIssue(
  AnimatedRideResourceCatalogIssueKind Kind,
  string RootOverlayName,
  string? DeclaringCommonPath,
  string Reference,
  string CommonPath,
  string UniquePath
);

internal sealed record AnimatedRideResourceSource(OvlFile File, AnimatedRide Resource);

internal sealed record AnimatedRideSceneryResourceSource(
  OvlFile File,
  SceneryItem Resource
);

internal sealed record AnimatedRideResourceLink(
  string RootOverlayName,
  AnimatedRideResourceSource Ride,
  AnimatedRideSceneryResourceSource? Scenery,
  AnimatedRideResourceLinkStatus Status
) {
  public bool IsResolved => Status == AnimatedRideResourceLinkStatus.Resolved;
}

internal sealed record AnimatedRideArchiveSnapshot(
  string CommonPath,
  string UniquePath,
  IReadOnlyList<string> ExternalReferences,
  IReadOnlyList<AnimatedRideResourceSource> AnimatedRides,
  IReadOnlyList<AnimatedRideSceneryResourceSource> SceneryItems,
  IDisposable? Owner = null
);

internal sealed class AnimatedRideResourceCatalogLoadResult : IDisposable {
  private readonly IReadOnlyList<AnimatedRideArchiveSnapshot> snapshots;
  private readonly Action<AnimatedRideArchiveSnapshot> disposeSnapshot;
  private bool disposed;

  public IReadOnlyList<AnimatedRideResourceLink> Links { get; }
  public IReadOnlyList<AnimatedRideResourceCatalogIssue> Issues { get; }
  public bool IsComplete => Issues.Count == 0 && Links.All(link => link.IsResolved);

  internal AnimatedRideResourceCatalogLoadResult(
    IReadOnlyList<AnimatedRideResourceLink> links,
    IReadOnlyList<AnimatedRideResourceCatalogIssue> issues,
    IReadOnlyList<AnimatedRideArchiveSnapshot> snapshots,
    Action<AnimatedRideArchiveSnapshot> disposeSnapshot
  ) {
    Links = Array.AsReadOnly(links.ToArray());
    Issues = Array.AsReadOnly(issues.ToArray());
    this.snapshots = Array.AsReadOnly(snapshots.ToArray());
    this.disposeSnapshot = disposeSnapshot;
  }

  public void Dispose() {
    if (disposed) return;
    disposed = true;
    GC.SuppressFinalize(this);
    var errors = new List<Exception>();
    foreach (var snapshot in snapshots.Reverse()) {
      try {
        disposeSnapshot(snapshot);
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    if (errors.Count > 0)
      throw new AggregateException(
        "Animated-ride resource catalog disposal reported errors.",
        errors);
  }
}

/// <summary>
/// Loads exact DAT-named OVL pairs and resolves each root ANR to one SID inside that root's declared
/// dependency closure. Missing or ambiguous resources remain typed and never fall back by bare name.
/// </summary>
internal static class AnimatedRideResourceCatalogLoader {
  private const string CommonSuffix = ".common.ovl";
  private const string UniqueSuffix = ".unique.ovl";

  public static AnimatedRideResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<SceneryPlacement> placements
  ) => Load(
    installRoot,
    placements,
    new FileSystemAnimatedRideResourceCatalogLoaderSource(),
    AnimatedRideResourceCatalogLoaderLimits.Default);

  internal static AnimatedRideResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<SceneryPlacement> placements,
    IAnimatedRideResourceCatalogLoaderSource source
  ) => Load(
    installRoot,
    placements,
    source,
    AnimatedRideResourceCatalogLoaderLimits.Default);

  internal static AnimatedRideResourceCatalogLoadResult Load(
    string installRoot,
    IReadOnlyList<SceneryPlacement> placements,
    IAnimatedRideResourceCatalogLoaderSource source,
    AnimatedRideResourceCatalogLoaderLimits limits
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    ArgumentNullException.ThrowIfNull(placements);
    ArgumentNullException.ThrowIfNull(source);
    ValidateLimits(limits);
    if (placements.Count > limits.MaximumPlacements)
      throw Invalid($"placement count exceeds {limits.MaximumPlacements}");

    var root = NormalizeRoot(installRoot);
    var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var placement in placements) {
      if (string.IsNullOrWhiteSpace(placement.OverlayPath)) continue;
      ValidateIdentifier(placement.OverlayPath, "placement overlay", limits);
      ValidateBareName(placement.ObjectKey, "placement object key", limits);
      var commonPath = ResolveExactCommonPath(root, placement.OverlayPath);
      if (roots.Count >= limits.MaximumPairs && !roots.ContainsKey(placement.OverlayPath))
        throw Invalid($"root pair count exceeds {limits.MaximumPairs}");
      roots.TryAdd(placement.OverlayPath, commonPath);
    }

    var loaded = new Dictionary<string, AnimatedRideArchiveSnapshot>(
      StringComparer.OrdinalIgnoreCase);
    var issues = new List<AnimatedRideResourceCatalogIssue>();
    var closures = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    try {
      foreach (var rootPair in roots) {
        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        closures.Add(rootPair.Key, closure);
        LoadClosure(root, rootPair.Key, rootPair.Value, source, limits, loaded, closure, issues);
      }

      var links = new List<AnimatedRideResourceLink>();
      foreach (var rootPair in roots) {
        if (!loaded.TryGetValue(rootPair.Value, out var rootSnapshot)) continue;
        var closure = closures[rootPair.Key];
        foreach (var ride in rootSnapshot.AnimatedRides) {
          ValidateRideSource(ride, rootSnapshot.UniquePath, limits);
          var sceneryName = ParseTaggedReference(
            ride.Resource.SceneryItemReference,
            "sid",
            $"ANR '{ride.Resource.Name}' scenery reference",
            limits);
          var candidates = loaded.Values
            .Where(snapshot => closure.Contains(snapshot.CommonPath))
            .SelectMany(snapshot => snapshot.SceneryItems)
            .Where(sourceItem =>
              sourceItem.File.Type == FileType.SceneryItem &&
              sourceItem.Resource.Name.Equals(
                sceneryName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
          foreach (var candidate in candidates) ValidateScenerySource(candidate, limits);
          var status = candidates.Length switch {
            0 => AnimatedRideResourceLinkStatus.MissingSceneryItem,
            > 1 => AnimatedRideResourceLinkStatus.AmbiguousSceneryItem,
            _ when candidates[0].Resource.Type != SidType.Ride =>
              AnimatedRideResourceLinkStatus.WrongSceneryType,
            _ => AnimatedRideResourceLinkStatus.Resolved,
          };
          links.Add(new AnimatedRideResourceLink(
            rootPair.Key,
            ride,
            candidates.Length == 1 ? candidates[0] : null,
            status));
        }
      }
      return new AnimatedRideResourceCatalogLoadResult(
        links,
        issues,
        loaded.Values.ToArray(),
        source.DisposePair);
    } catch {
      foreach (var snapshot in loaded.Values.Reverse()) source.DisposePair(snapshot);
      throw;
    }
  }

  private static void LoadClosure(
    string installRoot,
    string rootOverlay,
    string rootCommonPath,
    IAnimatedRideResourceCatalogLoaderSource source,
    AnimatedRideResourceCatalogLoaderLimits limits,
    IDictionary<string, AnimatedRideArchiveSnapshot> loaded,
    ISet<string> closure,
    ICollection<AnimatedRideResourceCatalogIssue> issues
  ) {
    var queue = new Queue<(string Path, string? Declaring, string Reference, int Depth)>();
    queue.Enqueue((rootCommonPath, null, rootOverlay, 0));
    while (queue.Count > 0) {
      var node = queue.Dequeue();
      if (node.Depth > limits.MaximumDependencyDepth)
        throw Invalid($"dependency depth exceeds {limits.MaximumDependencyDepth}");
      if (!closure.Add(node.Path)) continue;
      var uniquePath = ToUniquePath(node.Path);
      if (!source.FileExists(node.Path) || !source.FileExists(uniquePath)) {
        issues.Add(new AnimatedRideResourceCatalogIssue(
          node.Declaring == null
            ? AnimatedRideResourceCatalogIssueKind.MissingRootPair
            : AnimatedRideResourceCatalogIssueKind.MissingDependencyPair,
          rootOverlay,
          node.Declaring,
          node.Reference,
          node.Path,
          uniquePath));
        continue;
      }
      if (!loaded.TryGetValue(node.Path, out var snapshot)) {
        if (loaded.Count >= limits.MaximumPairs)
          throw Invalid($"loaded pair count exceeds {limits.MaximumPairs}");
        snapshot = source.LoadPair(node.Path);
        ValidateSnapshot(snapshot, node.Path, uniquePath, limits);
        loaded.Add(node.Path, snapshot);
      }
      if (snapshot.ExternalReferences.Count > limits.MaximumDependenciesPerPair)
        throw Invalid(
          $"archive '{node.Path}' dependency count exceeds " +
          $"{limits.MaximumDependenciesPerPair}");
      foreach (var reference in snapshot.ExternalReferences) {
        ValidateIdentifier(reference, $"archive '{node.Path}' dependency", limits);
        queue.Enqueue((
          ResolveDependencyCommonPath(installRoot, node.Path, reference),
          node.Path,
          reference,
          checked(node.Depth + 1)));
      }
    }
  }

  private static void ValidateSnapshot(
    AnimatedRideArchiveSnapshot snapshot,
    string commonPath,
    string uniquePath,
    AnimatedRideResourceCatalogLoaderLimits limits
  ) {
    if (snapshot == null) throw Invalid($"archive '{commonPath}' loaded as null");
    if (!snapshot.CommonPath.Equals(commonPath, StringComparison.OrdinalIgnoreCase) ||
        !snapshot.UniquePath.Equals(uniquePath, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"archive '{commonPath}' returned a foreign pair identity");
    if (snapshot.ExternalReferences == null || snapshot.AnimatedRides == null ||
        snapshot.SceneryItems == null)
      throw Invalid($"archive '{commonPath}' returned null resource collections");
    if (snapshot.AnimatedRides.Count > limits.MaximumResourcesPerPair ||
        snapshot.SceneryItems.Count > limits.MaximumResourcesPerPair)
      throw Invalid(
        $"archive '{commonPath}' resource count exceeds {limits.MaximumResourcesPerPair}");
  }

  private static void ValidateRideSource(
    AnimatedRideResourceSource source,
    string expectedUniquePath,
    AnimatedRideResourceCatalogLoaderLimits limits
  ) {
    if (source == null || source.File == null || source.Resource == null)
      throw Invalid("ANR source is incomplete");
    ValidateIdentifier(source.File.Path, "ANR archive path", limits);
    ValidateBareName(source.File.Name, "ANR OVL name", limits);
    ValidateBareName(source.Resource.Name, "decoded ANR name", limits);
    if (source.File.Type != FileType.AnimatedRide ||
        !source.File.Path.Equals(expectedUniquePath, StringComparison.OrdinalIgnoreCase) ||
        !source.File.Name.Equals(source.Resource.Name, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"ANR '{source.Resource.Name}' has a foreign or mismatched OVL identity");
  }

  private static void ValidateScenerySource(
    AnimatedRideSceneryResourceSource source,
    AnimatedRideResourceCatalogLoaderLimits limits
  ) {
    if (source == null || source.File == null || source.Resource == null)
      throw Invalid("SID source is incomplete");
    ValidateIdentifier(source.File.Path, "SID archive path", limits);
    ValidateBareName(source.File.Name, "SID OVL name", limits);
    ValidateBareName(source.Resource.Name, "decoded SID name", limits);
    if (source.File.Type != FileType.SceneryItem ||
        !source.File.Name.Equals(source.Resource.Name, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"SID '{source.Resource.Name}' has a mismatched OVL identity");
  }

  private static string ParseTaggedReference(
    string reference,
    string tag,
    string description,
    AnimatedRideResourceCatalogLoaderLimits limits
  ) {
    ValidateIdentifier(reference, description, limits);
    var separator = reference.IndexOf(':');
    if (separator <= 0 || separator != reference.LastIndexOf(':') ||
        !reference[(separator + 1)..].Equals(tag, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"{description} '{reference}' is not one exact name:{tag} identity");
    var name = reference[..separator];
    ValidateBareName(name, description, limits);
    return name;
  }

  private static string ResolveExactCommonPath(string installRoot, string overlayPath) {
    var normalized = NormalizeSeparators(overlayPath);
    if (normalized.EndsWith(CommonSuffix, StringComparison.OrdinalIgnoreCase))
      normalized = normalized[..^CommonSuffix.Length];
    else if (normalized.EndsWith(UniqueSuffix, StringComparison.OrdinalIgnoreCase))
      normalized = normalized[..^UniqueSuffix.Length];
    else if (normalized.EndsWith(".ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid($"overlay '{overlayPath}' has an unknown OVL suffix");
    var path = Path.GetFullPath(Path.Combine(installRoot, normalized + CommonSuffix));
    if (!IsContainedBy(installRoot, path))
      throw Invalid($"overlay '{overlayPath}' leaves the installation root");
    return path;
  }

  private static string ResolveDependencyCommonPath(
    string installRoot,
    string declaringCommonPath,
    string reference
  ) {
    var normalized = NormalizeSeparators(reference);
    if (normalized.EndsWith(CommonSuffix, StringComparison.OrdinalIgnoreCase))
      normalized = normalized[..^CommonSuffix.Length];
    else if (normalized.EndsWith(UniqueSuffix, StringComparison.OrdinalIgnoreCase))
      normalized = normalized[..^UniqueSuffix.Length];
    else if (normalized.EndsWith(".ovl", StringComparison.OrdinalIgnoreCase))
      throw Invalid($"dependency '{reference}' has an unknown OVL suffix");
    var directory = Path.GetDirectoryName(declaringCommonPath)
      ?? throw Invalid($"archive '{declaringCommonPath}' has no directory");
    var path = Path.GetFullPath(Path.Combine(directory, normalized + CommonSuffix));
    if (!IsContainedBy(installRoot, path))
      throw Invalid($"dependency '{reference}' leaves the installation root");
    return path;
  }

  private static string NormalizeSeparators(string value) => value
    .Replace('\\', Path.DirectorySeparatorChar)
    .Replace('/', Path.DirectorySeparatorChar);

  private static string NormalizeRoot(string value) =>
    Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));

  private static bool IsContainedBy(string root, string path) {
    var relative = Path.GetRelativePath(root, path);
    return !Path.IsPathRooted(relative) && relative != ".." &&
      !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
      !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
  }

  private static string ToUniquePath(string commonPath) =>
    commonPath[..^CommonSuffix.Length] + UniqueSuffix;

  private static void ValidateBareName(
    string value,
    string description,
    AnimatedRideResourceCatalogLoaderLimits limits
  ) {
    ValidateIdentifier(value, description, limits);
    if (value.Contains(':', StringComparison.Ordinal))
      throw Invalid($"{description} '{value}' is tagged or ambiguous");
  }

  private static void ValidateIdentifier(
    string? value,
    string description,
    AnimatedRideResourceCatalogLoaderLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(value) || value.Length > limits.MaximumIdentifierLength ||
        !value.Equals(value.Trim(), StringComparison.Ordinal) ||
        value.IndexOfAny(['*', '?']) >= 0)
      throw Invalid(
        $"{description} is empty, padded, wildcarded, or exceeds " +
        $"{limits.MaximumIdentifierLength} characters");
  }

  private static void ValidateLimits(AnimatedRideResourceCatalogLoaderLimits limits) {
    if (limits.MaximumPlacements <= 0 || limits.MaximumPairs <= 0 ||
        limits.MaximumDependenciesPerPair <= 0 || limits.MaximumDependencyDepth < 0 ||
        limits.MaximumResourcesPerPair <= 0 || limits.MaximumIdentifierLength <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid animated-ride resource catalog: {message}.");
}

internal readonly record struct AnimatedRideResourceCatalogLoaderLimits(
  int MaximumPlacements,
  int MaximumPairs,
  int MaximumDependenciesPerPair,
  int MaximumDependencyDepth,
  int MaximumResourcesPerPair,
  int MaximumIdentifierLength
) {
  public static AnimatedRideResourceCatalogLoaderLimits Default { get; } = new(
    100_000,
    4_096,
    4_096,
    64,
    100_000,
    4_096);
}

internal interface IAnimatedRideResourceCatalogLoaderSource {
  bool FileExists(string path);
  AnimatedRideArchiveSnapshot LoadPair(string commonPath);
  void DisposePair(AnimatedRideArchiveSnapshot snapshot);
}

internal sealed class FileSystemAnimatedRideResourceCatalogLoaderSource
  : IAnimatedRideResourceCatalogLoaderSource {
  public bool FileExists(string path) => File.Exists(path);

  public AnimatedRideArchiveSnapshot LoadPair(string commonPath) {
    var archive = Ovl.Load(commonPath);
    try {
      var rides = AnimatedRides.Extract(archive).Select(ride => new AnimatedRideResourceSource(
        FindFile(archive, ride.Name, FileType.AnimatedRide),
        ride)).ToArray();
      var scenery = SceneryItems.Extract(archive).Select(item =>
        new AnimatedRideSceneryResourceSource(
          FindFile(archive, item.Name, FileType.SceneryItem),
          item)).ToArray();
      return new AnimatedRideArchiveSnapshot(
        commonPath,
        commonPath[..^".common.ovl".Length] + ".unique.ovl",
        archive.ExternalReferences,
        rides,
        scenery,
        archive);
    } catch {
      archive.Dispose();
      throw;
    }
  }

  public void DisposePair(AnimatedRideArchiveSnapshot snapshot) => snapshot.Owner?.Dispose();

  private static OvlFile FindFile(Ovl archive, string name, FileType type) {
    var matches = archive.Keys.Where(file =>
      file.Type == type && file.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (matches.Length != 1)
      throw new InvalidDataException(
        $"Decoded '{name}:{type.ToTagString()}' does not have one exact OVL symbol.");
    return matches[0];
  }
}
