// Scenery Resource Catalog
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenRCT3.Simulation;

/// <summary>A scenery resource and the OVL archive that owns its data.</summary>
/// <remarks>
/// The containing <see cref="SceneryResourceCatalog"/> owns the archive. The resource must not be
/// used after the catalog is disposed.
/// </remarks>
public sealed record SceneryResourceEntry(Ovl Archive, OvlFile File);

/// <summary>
/// Lazily searches the OVL pair named by a DAT entry and nearby owner pairs.
/// </summary>
/// <remarks>
/// Search order is the named pair and every dependency reachable from already-loaded owners, exact
/// owner filenames inside the installation root, sibling common archives, then a deterministic
/// fallback over the remaining overlay descendants. Each newly probed owner brings its transitive
/// declared dependencies into the same resolution set. All fallbacks remain inside the installation
/// root. Loaded archives are cached and disposed with the catalog.
/// </remarks>
public sealed class SceneryResourceCatalog : IDisposable {
  private const string CommonSuffix = ".common.ovl";
  private const string UniqueSuffix = ".unique.ovl";
  private const int MaximumDependencyDepth = 64;
  private const int MaximumDependencyPairs = 4_096;
  private const int MaximumDependenciesPerPair = 4_096;

  private readonly object syncRoot = new();
  private readonly string installRoot;
  private readonly string exactCommonPath;
  private readonly string exactDirectory;
  private readonly ISceneryResourceCatalogSource source;
  private readonly Dictionary<string, Ovl> loadedPairs =
    new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<Ovl, string> loadedPathsByArchive =
    new(ReferenceEqualityComparer.Instance);
  private readonly List<string> loadedPairOrder = [];
  private readonly Dictionary<string, IReadOnlyList<string>> targetedCommonPaths =
    new(StringComparer.OrdinalIgnoreCase);
  private IReadOnlyList<string>? siblingCommonPaths;
  private IReadOnlyList<string>? descendantCommonPaths;
  private bool disposed;

  /// <summary>Creates a catalog rooted at an RCT3 installation.</summary>
  /// <param name="installRoot">RCT3 installation directory.</param>
  /// <param name="overlayFilename">
  /// The DAT <c>OVERLAYFILENAME</c> value, such as <c>Style\Vanilla\style</c>.
  /// </param>
  public SceneryResourceCatalog(string installRoot, string overlayFilename)
    : this(installRoot, overlayFilename, new FileSystemSceneryResourceCatalogSource()) { }

  internal SceneryResourceCatalog(
    string installRoot,
    string overlayFilename,
    ISceneryResourceCatalogSource source
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    ArgumentException.ThrowIfNullOrWhiteSpace(overlayFilename);
    ArgumentNullException.ThrowIfNull(source);

    this.installRoot = NormalizeRoot(installRoot);
    exactCommonPath = ResolveExactCommonPath(this.installRoot, overlayFilename);
    exactDirectory = Path.GetDirectoryName(exactCommonPath)
      ?? throw new ArgumentException(
        "The overlay filename has no directory.",
        nameof(overlayFilename));
    this.source = source;
  }

  /// <summary>Finds an exact resource name and type, ignoring case.</summary>
  public SceneryResourceEntry? Find(string resourceName, FileType type) {
    ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
    if (type == FileType.Unknown || !Enum.IsDefined(type))
      throw new ArgumentOutOfRangeException(
        nameof(type),
        type,
        "A known resource type is required.");

    lock (syncRoot) {
      ObjectDisposedException.ThrowIf(disposed, this);

      _ = GetOrLoadPair(exactCommonPath, required: true)
        ?? throw new InvalidOperationException("The required OVL pair was not loaded.");

      // A nested owner pair often contains the resources referenced by the SVD that caused the pair
      // to be loaded. Resolve the declared graph of every already-open owner before guessing another
      // filename. A graph is fully checked so duplicate exact definitions fail closed.
      var loadedResult = FindInReachableSet(
        loadedPairOrder.ToArray(), resourceName, type);
      if (loadedResult != null) return loadedResult;

      var targetedResult = FindInTargetedOwners(
        GetTargetedCommonPaths(resourceName), resourceName, type);
      if (targetedResult != null) return targetedResult;

      var siblingResult = FindInFallbackCandidates(
        GetSiblingCommonPaths(), resourceName, type);
      if (siblingResult != null) return siblingResult;

      // Stock dependencies do not always share their resource name. Examples include
      // PineTree_01:ftx in PineTreeTexture.ovl and SnakelghtScenery:svd in Snakelight.ovl.
      // Search the remaining pairs in deterministic path order and still require an exact symbol.
      return FindInFallbackCandidates(GetDescendantCommonPaths(), resourceName, type);
    }
  }

  /// <summary>
  /// Finds a resource from the archive graph that owns <paramref name="owner"/> before using the
  /// deterministic overlay fallbacks.
  /// </summary>
  public SceneryResourceEntry? FindFrom(
    SceneryResourceEntry owner,
    string resourceName,
    FileType type
  ) {
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
    if (type == FileType.Unknown || !Enum.IsDefined(type))
      throw new ArgumentOutOfRangeException(
        nameof(type),
        type,
        "A known resource type is required.");

    lock (syncRoot) {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (!loadedPathsByArchive.TryGetValue(owner.Archive, out var ownerPath))
        throw new ArgumentException(
          "The owner resource does not belong to this catalog.",
          nameof(owner));

      var ownerResult = FindInReachableSet([ownerPath], resourceName, type);
      if (ownerResult != null) return ownerResult;

      var targetedResult = FindInTargetedOwners(
        GetTargetedCommonPaths(resourceName), resourceName, type);
      if (targetedResult != null) return targetedResult;

      var siblingResult = FindInFallbackCandidates(
        GetSiblingCommonPaths(), resourceName, type);
      if (siblingResult != null) return siblingResult;

      return FindInFallbackCandidates(GetDescendantCommonPaths(), resourceName, type);
    }
  }

  /// <summary>Finds a resource only inside one exact owner's dependency closure.</summary>
  internal SceneryResourceEntry? FindWithinOwnerClosure(
    SceneryResourceEntry owner,
    string resourceName,
    FileType type
  ) {
    ArgumentNullException.ThrowIfNull(owner);
    ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
    if (type == FileType.Unknown || !Enum.IsDefined(type))
      throw new ArgumentOutOfRangeException(
        nameof(type),
        type,
        "A known resource type is required.");

    lock (syncRoot) {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (!loadedPathsByArchive.TryGetValue(owner.Archive, out var ownerPath))
        throw new ArgumentException(
          "The owner resource does not belong to this catalog.",
          nameof(owner));
      return FindInReachableSet([ownerPath], resourceName, type);
    }
  }

  /// <summary>
  /// Loads one exact sibling owner pair and returns every resource of <paramref name="type"/> from
  /// that pair. PTD/QTD shape fields name these owner pairs, not necessarily the contained SHS.
  /// </summary>
  internal IReadOnlyList<SceneryResourceEntry> FindInSiblingOwnerPair(
    string ownerName,
    FileType type
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(ownerName);
    if (type == FileType.Unknown || !Enum.IsDefined(type))
      throw new ArgumentOutOfRangeException(nameof(type), type, "A known resource type is required.");
    if (!CanProbeAsFileName(ownerName) ||
        !string.Equals(ownerName, ownerName.Trim(), StringComparison.Ordinal))
      throw new ArgumentException(
        "An OVL owner name must be one safe filename without an extension.",
        nameof(ownerName));

    lock (syncRoot) {
      ObjectDisposedException.ThrowIf(disposed, this);
      var commonPath = Path.GetFullPath(Path.Combine(exactDirectory, ownerName + CommonSuffix));
      if (!IsInsideInstallRoot(commonPath) || !IsInsideOverlayTree(commonPath))
        throw new ArgumentException(
          "The OVL owner name must stay inside the catalog overlay tree.",
          nameof(ownerName));
      var archive = GetOrLoadPair(commonPath, required: true)
        ?? throw new InvalidOperationException($"The required owner pair '{ownerName}' was not loaded.");
      return archive.Keys
        .Where(file => file.Type == type)
        .Select(file => new SceneryResourceEntry(archive, file))
        .ToArray();
    }
  }

  /// <summary>
  /// Finds an exact resource name and a case-insensitive enum name or OVL type tag.
  /// </summary>
  public SceneryResourceEntry? Find(string resourceName, string type) =>
    Find(resourceName, ParseFileType(type));

  /// <summary>Finds an exact resource from a case-insensitive <c>name:tag</c> reference.</summary>
  public SceneryResourceEntry? Find(string taggedReference) {
    var reference = ParseTaggedReference(taggedReference);
    return Find(reference.Name, reference.Type);
  }

  /// <inheritdoc />
  public void Dispose() {
    lock (syncRoot) {
      if (disposed) return;

      foreach (var archive in loadedPairs.Values)
        archive.Dispose();
      loadedPairs.Clear();
      loadedPathsByArchive.Clear();
      loadedPairOrder.Clear();
      targetedCommonPaths.Clear();
      siblingCommonPaths = null;
      descendantCommonPaths = null;
      disposed = true;
    }

    GC.SuppressFinalize(this);
  }

  private Ovl? GetOrLoadPair(string commonPath, bool required) {
    if (loadedPairs.TryGetValue(commonPath, out var archive)) return archive;

    var uniquePath = ToUniquePath(commonPath);
    var commonExists = source.FileExists(commonPath);
    var uniqueExists = source.FileExists(uniquePath);
    if (!commonExists || !uniqueExists) {
      if (required)
        throw new FileNotFoundException(
          $"The OVL pair is incomplete: '{commonPath}' and '{uniquePath}' must both exist.",
          !commonExists ? commonPath : uniquePath);
      return null;
    }

    archive = source.LoadPair(commonPath)
      ?? throw new InvalidOperationException($"The OVL loader returned null for '{commonPath}'.");
    loadedPairs.Add(commonPath, archive);
    loadedPathsByArchive.Add(archive, commonPath);
    loadedPairOrder.Add(commonPath);
    return archive;
  }

  private SceneryResourceEntry? FindInFallbackCandidates(
    IEnumerable<string> commonPaths,
    string resourceName,
    FileType type
  ) => FindInReachableSet(commonPaths, resourceName, type);

  private SceneryResourceEntry? FindInTargetedOwners(
    IReadOnlyList<string> commonPaths,
    string resourceName,
    FileType type
  ) {
    var localPaths = commonPaths.Where(IsInsideOverlayTree).ToArray();
    var localResult = FindInReachableSet(
      localPaths,
      resourceName,
      type,
      requireRootPairs: true);
    if (localResult != null) return localResult;

    var globalPaths = commonPaths.Where(path => !IsInsideOverlayTree(path)).ToArray();
    return FindInReachableSet(
      globalPaths,
      resourceName,
      type,
      requireRootPairs: true);
  }

  private SceneryResourceEntry? FindInReachableSet(
    IEnumerable<string> rootPaths,
    string resourceName,
    FileType type,
    bool requireRootPairs = false
  ) {
    var queue = new Queue<DependencyNode>();
    var scheduled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var rootPath in rootPaths) {
      if (!scheduled.Add(rootPath)) continue;
      if (scheduled.Count > MaximumDependencyPairs)
        throw InvalidDependency(
          $"reachable pair count exceeds {MaximumDependencyPairs}");
      queue.Enqueue(new DependencyNode(rootPath, 0, requireRootPairs));
    }

    SceneryResourceEntry? match = null;
    string? matchPath = null;
    foreach (var _ in Enumerable.Range(0, MaximumDependencyPairs)) {
      if (queue.Count == 0) break;
      var node = queue.Dequeue();
      var archive = GetOrLoadPair(node.CommonPath, required: node.IsDeclaredDependency);
      if (archive == null) continue;

      var resource = FindExact(archive, node.CommonPath, resourceName, type);
      if (resource != null) {
        var candidate = new SceneryResourceEntry(archive, resource);
        if (match != null && !AreEquivalentDefinitions(match, candidate))
          throw InvalidDependency(
            $"resource '{resourceName}:{type.ToTagString()}' is defined by both " +
            $"'{matchPath}' and '{node.CommonPath}'");
        if (match == null) {
          match = candidate;
          matchPath = node.CommonPath;
        }
      }

      var references = source.GetExternalReferences(archive)
        ?? throw InvalidDependency(
          $"archive '{node.CommonPath}' returned a null dependency list");
      if (references.Count > MaximumDependenciesPerPair)
        throw InvalidDependency(
          $"archive '{node.CommonPath}' dependency count {references.Count} exceeds " +
          $"{MaximumDependenciesPerPair}");

      foreach (var reference in references) {
        var dependencyPath = ResolveDependencyCommonPath(node.CommonPath, reference);
        if (!HasCompleteDependencyPair(dependencyPath)) continue;
        if (!scheduled.Add(dependencyPath)) continue;
        if (node.Depth >= MaximumDependencyDepth)
          throw InvalidDependency(
            $"dependency depth exceeds {MaximumDependencyDepth} at '{reference}'");
        if (scheduled.Count > MaximumDependencyPairs)
          throw InvalidDependency(
            $"reachable pair count exceeds {MaximumDependencyPairs}");
        queue.Enqueue(new DependencyNode(dependencyPath, node.Depth + 1, true));
      }
    }

    if (queue.Count > 0)
      throw InvalidDependency($"reachable pair count exceeds {MaximumDependencyPairs}");
    return match;
  }

  private bool HasCompleteDependencyPair(string commonPath) {
    if (loadedPairs.ContainsKey(commonPath)) return true;

    var uniquePath = ToUniquePath(commonPath);
    var commonExists = source.FileExists(commonPath);
    var uniqueExists = source.FileExists(uniquePath);
    if (commonExists && uniqueExists) return true;
    if (!commonExists && !uniqueExists) return false;
    throw new FileNotFoundException(
      $"Declared OVL dependency is incomplete: '{commonPath}' and '{uniquePath}' " +
      "must both exist.",
      !commonExists ? commonPath : uniquePath);
  }

  private string ResolveDependencyCommonPath(
    string declaringCommonPath,
    string reference
  ) {
    if (string.IsNullOrWhiteSpace(reference))
      throw InvalidDependency(
        $"archive '{declaringCommonPath}' declares an empty dependency");
    if (!string.Equals(reference, reference.Trim(), StringComparison.Ordinal))
      throw InvalidDependency(
        $"archive '{declaringCommonPath}' dependency '{reference}' has outer whitespace");
    if (reference.IndexOfAny(['*', '?']) >= 0)
      throw InvalidDependency(
        $"archive '{declaringCommonPath}' dependency '{reference}' contains a wildcard");

    try {
      var normalized = reference
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
        .Replace('\\', Path.DirectorySeparatorChar)
        .Replace('/', Path.DirectorySeparatorChar);
      if (normalized.EndsWith(CommonSuffix, StringComparison.OrdinalIgnoreCase))
        normalized = normalized[..^CommonSuffix.Length];
      else if (normalized.EndsWith(UniqueSuffix, StringComparison.OrdinalIgnoreCase))
        normalized = normalized[..^UniqueSuffix.Length];
      else if (normalized.EndsWith(".ovl", StringComparison.OrdinalIgnoreCase))
        throw InvalidDependency(
          $"archive '{declaringCommonPath}' dependency '{reference}' has an unknown OVL suffix");
      if (string.IsNullOrWhiteSpace(Path.GetFileName(normalized)))
        throw InvalidDependency(
          $"archive '{declaringCommonPath}' dependency '{reference}' names no OVL pair");

      var declaringDirectory = Path.GetDirectoryName(declaringCommonPath)
        ?? throw InvalidDependency(
          $"archive path '{declaringCommonPath}' has no directory");
      var commonPath = Path.GetFullPath(
        Path.Combine(declaringDirectory, normalized + CommonSuffix));
      if (!IsInsideInstallRoot(commonPath))
        throw InvalidDependency(
          $"archive '{declaringCommonPath}' dependency '{reference}' leaves the install root");
      return commonPath;
    } catch (ArgumentException error) {
      throw InvalidDependency(
        $"archive '{declaringCommonPath}' dependency '{reference}' is not a valid path",
        error);
    } catch (NotSupportedException error) {
      throw InvalidDependency(
        $"archive '{declaringCommonPath}' dependency '{reference}' is not a valid path",
        error);
    }
  }

  private static InvalidDataException InvalidDependency(
    string message,
    Exception? inner = null
  ) => new($"Invalid OVL dependency graph: {message}.", inner);

  private IReadOnlyList<string> GetSiblingCommonPaths() {
    if (siblingCommonPaths != null) return siblingCommonPaths;

    siblingCommonPaths = source.EnumerateCommonOvls(exactDirectory)
      .Select(NormalizeEnumeratedPath)
      .Where(path => path != null)
      .Select(path => path!)
      .Where(IsCommonOvl)
      .Where(IsDirectSibling)
      .Where(IsInsideInstallRoot)
      .Where(path => !string.Equals(path, exactCommonPath, StringComparison.OrdinalIgnoreCase))
      .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
      .ThenBy(Path.GetFileName, StringComparer.Ordinal)
      .ThenBy(path => path, StringComparer.Ordinal)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    return siblingCommonPaths;
  }

  private IReadOnlyList<string> GetTargetedCommonPaths(string resourceName) {
    if (targetedCommonPaths.TryGetValue(resourceName, out var cached)) return cached;
    if (!CanProbeAsFileName(resourceName)) {
      targetedCommonPaths.Add(resourceName, []);
      return [];
    }

    var ownerFileName = resourceName + CommonSuffix;
    var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var candidate in source.EnumerateMatchingCommonOvls(
               installRoot,
               ownerFileName)) {
      var path = NormalizeEnumeratedPath(candidate, installRoot);
      if (path == null || !IsCommonOvl(path) || !IsInsideInstallRoot(path) ||
          string.Equals(path, exactCommonPath, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(
            Path.GetFileName(path),
            ownerFileName,
            StringComparison.OrdinalIgnoreCase)) continue;
      if (!candidates.Add(path)) continue;
      if (candidates.Count > MaximumDependencyPairs)
        throw InvalidDependency(
          $"targeted owner pair count exceeds {MaximumDependencyPairs} for " +
          $"'{ownerFileName}'");
    }
    var paths = candidates
      .OrderBy(path => Path.GetRelativePath(installRoot, path), StringComparer.OrdinalIgnoreCase)
      .ThenBy(path => Path.GetRelativePath(installRoot, path), StringComparer.Ordinal)
      .ToArray();
    targetedCommonPaths.Add(resourceName, paths);
    return paths;
  }

  private IReadOnlyList<string> GetDescendantCommonPaths() {
    if (descendantCommonPaths != null) return descendantCommonPaths;

    descendantCommonPaths = source.EnumerateDescendantCommonOvls(exactDirectory)
      .Select(NormalizeEnumeratedPath)
      .Where(path => path != null)
      .Select(path => path!)
      .Where(IsCommonOvl)
      .Where(IsInsideOverlayTree)
      .Where(IsInsideInstallRoot)
      .Where(path => !string.Equals(path, exactCommonPath, StringComparison.OrdinalIgnoreCase))
      .OrderBy(path => Path.GetRelativePath(exactDirectory, path), StringComparer.OrdinalIgnoreCase)
      .ThenBy(path => Path.GetRelativePath(exactDirectory, path), StringComparer.Ordinal)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    return descendantCommonPaths;
  }

  private string? NormalizeEnumeratedPath(string? path) =>
    NormalizeEnumeratedPath(path, exactDirectory);

  private static string? NormalizeEnumeratedPath(
    string? path,
    string relativeRoot
  ) {
    if (string.IsNullOrWhiteSpace(path)) return null;

    try {
      return Path.GetFullPath(
        Path.IsPathRooted(path) ? path : Path.Combine(relativeRoot, path));
    } catch (ArgumentException) {
      return null;
    } catch (NotSupportedException) {
      return null;
    }
  }

  private bool IsDirectSibling(string path) => string.Equals(
    Path.GetDirectoryName(path),
    exactDirectory,
    StringComparison.OrdinalIgnoreCase);

  private bool IsInsideOverlayTree(string path) => IsContainedBy(exactDirectory, path);

  private bool IsInsideInstallRoot(string path) => IsContainedBy(installRoot, path);

  private static OvlFile? FindExact(
    Ovl archive,
    string commonPath,
    string resourceName,
    FileType type
  ) {
    OvlFile? match = null;
    foreach (var file in archive.Keys.Where(file =>
      file.Type == type &&
      string.Equals(file.Name, resourceName, StringComparison.OrdinalIgnoreCase))) {
      if (match != null && !AreEquivalentDefinitions(
        new SceneryResourceEntry(archive, match),
        new SceneryResourceEntry(archive, file)))
        throw InvalidDependency(
          $"archive '{commonPath}' contains duplicate exact definitions for " +
          $"'{resourceName}:{type.ToTagString()}'");
      match ??= file;
    }
    return match;
  }

  private static bool AreEquivalentDefinitions(
    SceneryResourceEntry first,
    SceneryResourceEntry second
  ) {
    if (ReferenceEquals(first.Archive, second.Archive) && first.File == second.File)
      return true;
    if (first.File.Type != second.File.Type) return false;
    if (first.File.Type == FileType.FlexibleTexture)
      return AreEquivalentFlexibleTextures(first, second);

    // SID, SVD, and SHS resources keep semantic targets in relocation and SymbolRef tables outside
    // their raw resource bytes. Until those decoded graphs have canonical comparers, duplicate
    // definitions must fail closed even when their inline bytes happen to match.
    return false;
  }

  private static bool AreEquivalentFlexibleTextures(
    SceneryResourceEntry first,
    SceneryResourceEntry second
  ) {
    FlexiTextureList firstTexture = default;
    FlexiTextureList secondTexture = default;
    var firstLoaded = false;
    var secondLoaded = false;
    try {
      firstTexture = FlexiTextureList.Load(first.Archive, first.File);
      firstLoaded = true;
      secondTexture = FlexiTextureList.Load(second.Archive, second.File);
      secondLoaded = true;
      return AreEquivalentFlexibleTextures(firstTexture, secondTexture);
    } finally {
      if (firstLoaded) DisposeFrames(firstTexture.Frames);
      if (secondLoaded) DisposeFrames(secondTexture.Frames);
    }
  }

  internal static bool AreEquivalentFlexibleTextures(
    FlexiTextureList firstTexture,
    FlexiTextureList secondTexture
  ) {
    if (firstTexture.Fps != secondTexture.Fps ||
        firstTexture.Frames.Length != secondTexture.Frames.Length) return false;

    foreach (var index in Enumerable.Range(0, firstTexture.Frames.Length)) {
      var firstFrame = firstTexture.Frames[index];
      var secondFrame = secondTexture.Frames[index];
      if (firstFrame.Recolorable != secondFrame.Recolorable ||
          firstFrame.Texture.Width != secondFrame.Texture.Width ||
          firstFrame.Texture.Height != secondFrame.Texture.Height) return false;
      if (firstFrame.Recolorable != Recolorable.None &&
          (!firstFrame.PaletteBgra.Span.SequenceEqual(secondFrame.PaletteBgra.Span) ||
           !firstFrame.IndexedPixels.Span.SequenceEqual(secondFrame.IndexedPixels.Span)))
        return false;

      var firstPixels = new Rgba32[
        firstFrame.Texture.Width * firstFrame.Texture.Height];
      var secondPixels = new Rgba32[
        secondFrame.Texture.Width * secondFrame.Texture.Height];
      firstFrame.Texture.CopyPixelDataTo(firstPixels);
      secondFrame.Texture.CopyPixelDataTo(secondPixels);
      if (!firstPixels.AsSpan().SequenceEqual(secondPixels)) return false;
    }
    return true;
  }

  private static void DisposeFrames(IEnumerable<FlexiTexture> frames) {
    foreach (var frame in frames)
      frame.Texture.Dispose();
  }

  private static FileType ParseFileType(string type) {
    ArgumentException.ThrowIfNullOrWhiteSpace(type);
    var normalized = type.Trim().TrimStart('.');
    if (Enum.TryParse<FileType>(normalized, ignoreCase: true, out var namedType) &&
        namedType != FileType.Unknown && Enum.IsDefined(namedType))
      return namedType;

    var taggedType = normalized.ToLowerInvariant().ToFileType();
    if (taggedType != FileType.Unknown) return taggedType;

    throw new ArgumentException($"'{type}' is not a known OVL resource type.", nameof(type));
  }

  internal static (string Name, FileType Type) ParseTaggedReference(
    string taggedReference
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(taggedReference);
    var separator = taggedReference.LastIndexOf(':');
    if (separator <= 0 || separator == taggedReference.Length - 1)
      throw new ArgumentException(
        "An OVL resource reference must use the exact 'name:tag' form.",
        nameof(taggedReference));

    var name = taggedReference[..separator];
    var tag = taggedReference[(separator + 1)..];
    if (!string.Equals(name, name.Trim(), StringComparison.Ordinal) ||
        !string.Equals(tag, tag.Trim(), StringComparison.Ordinal))
      throw new ArgumentException(
        "An OVL resource reference cannot contain surrounding whitespace.",
        nameof(taggedReference));

    var type = tag.ToLowerInvariant().ToFileType();
    if (type == FileType.Unknown)
      throw new ArgumentException(
        $"'{tag}' is not a known OVL resource tag.",
        nameof(taggedReference));
    return (name, type);
  }

  private static string NormalizeRoot(string root) =>
    Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

  private static string ResolveExactCommonPath(string root, string overlayFilename) {
    var normalizedOverlay = overlayFilename.Trim()
      .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
      .Replace('\\', Path.DirectorySeparatorChar)
      .Replace('/', Path.DirectorySeparatorChar);
    if (normalizedOverlay.EndsWith(CommonSuffix, StringComparison.OrdinalIgnoreCase))
      normalizedOverlay = normalizedOverlay[..^CommonSuffix.Length];
    else if (normalizedOverlay.EndsWith(UniqueSuffix, StringComparison.OrdinalIgnoreCase))
      normalizedOverlay = normalizedOverlay[..^UniqueSuffix.Length];

    if (string.IsNullOrWhiteSpace(Path.GetFileName(normalizedOverlay)))
      throw new ArgumentException(
        "The overlay filename must name an OVL pair.",
        nameof(overlayFilename));

    var commonPath = Path.GetFullPath(Path.Combine(root, normalizedOverlay + CommonSuffix));
    if (!IsContainedBy(root, commonPath))
      throw new ArgumentException(
        "The overlay filename must stay inside the installation root.",
        nameof(overlayFilename));
    return commonPath;
  }

  private static bool IsContainedBy(string root, string path) {
    var relative = Path.GetRelativePath(root, path);
    return !Path.IsPathRooted(relative) &&
      !string.Equals(relative, "..", StringComparison.Ordinal) &&
      !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
      !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
  }

  private static bool IsCommonOvl(string path) =>
    path.EndsWith(CommonSuffix, StringComparison.OrdinalIgnoreCase);

  private static bool CanProbeAsFileName(string resourceName) =>
    resourceName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
    resourceName.IndexOfAny(['*', '?']) < 0 &&
    string.Equals(Path.GetFileName(resourceName), resourceName, StringComparison.Ordinal);

  private static string ToUniquePath(string commonPath) =>
    commonPath[..^CommonSuffix.Length] + UniqueSuffix;

  private sealed record DependencyNode(
    string CommonPath,
    int Depth,
    bool IsDeclaredDependency
  );
}

internal interface ISceneryResourceCatalogSource {
  bool FileExists(string path);
  IEnumerable<string> EnumerateCommonOvls(string directory);
  IEnumerable<string> EnumerateMatchingCommonOvls(string directory, string fileName);
  IEnumerable<string> EnumerateDescendantCommonOvls(string directory);
  IReadOnlyList<string> GetExternalReferences(Ovl archive) => archive.ExternalReferences;
  Ovl LoadPair(string commonOvlPath);
}

internal sealed class FileSystemSceneryResourceCatalogSource : ISceneryResourceCatalogSource {
  public bool FileExists(string path) => File.Exists(path);

  public IEnumerable<string> EnumerateCommonOvls(string directory) =>
    Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
      .Where(path => path.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase));

  public IEnumerable<string> EnumerateMatchingCommonOvls(string directory, string fileName) =>
    Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
      .Where(path => string.Equals(
        Path.GetFileName(path),
        fileName,
        StringComparison.OrdinalIgnoreCase));

  public IEnumerable<string> EnumerateDescendantCommonOvls(string directory) =>
    Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
      .Where(path => path.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase));

  public IReadOnlyList<string> GetExternalReferences(Ovl archive) =>
    archive.ExternalReferences;

  public Ovl LoadPair(string commonOvlPath) => Ovl.Load(commonOvlPath);
}
