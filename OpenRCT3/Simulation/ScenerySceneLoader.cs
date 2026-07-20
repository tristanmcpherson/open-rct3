// Scenery Scene Loader
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using System.Collections.Generic;

using Texture = OpenCobra.GDK.Materials.Texture;

namespace OpenRCT3.Simulation;

/// <summary>Owned scenery models and the explicit outcomes of building them.</summary>
/// <remarks>
/// The caller owns every returned <see cref="Model"/>. Geometry batches skipped for a missing
/// nonblank FTX reference have already had their meshes disposed.
/// </remarks>
public sealed record ScenerySceneLoadResult(
  IReadOnlyList<Model> Models,
  SceneryGeometryBuildResult Geometry,
  int MissingOverlayPlacementCount,
  int MissingTextureBatchCount
);

internal interface IScenerySceneResourceContext : IDisposable {
  bool IsMissingOverlay { get; }
  ResolvedSceneryObject? Resolve(string objectKey);
  bool TryResolveTexture(
    string taggedReference,
    SceneryFlexiColours flexiColours,
    out Texture? texture
  );
}

/// <summary>Builds render-owned models from decoded park scenery.</summary>
public static class ScenerySceneLoader {
  /// <summary>
  /// Resolves placed scenery against its exact DAT overlay roots and transfers every successfully
  /// materialized mesh and texture lease to a returned model.
  /// </summary>
  public static ScenerySceneLoadResult Load(
    Park park,
    Terrain terrain,
    string installRoot
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    return Load(
      park,
      terrain,
      overlayPath => CreateResourceContext(installRoot, overlayPath),
      SceneryGeometryBuilder.Build);
  }

  internal static ScenerySceneLoadResult Load(
    Park park,
    Terrain terrain,
    Func<string, IScenerySceneResourceContext> createContext,
    Func<
      Park,
      Terrain,
      Func<SceneryPlacement, ResolvedSceneryObject?>,
      SceneryGeometryBuildResult> buildGeometry,
    Func<Mesh, Model>? createModel = null
  ) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(createContext);
    ArgumentNullException.ThrowIfNull(buildGeometry);
    createModel ??= mesh => new Model(mesh);

    var contextsByOverlay = new Dictionary<string, IScenerySceneResourceContext>(
      StringComparer.Ordinal);
    var activeContexts = new List<IScenerySceneResourceContext>();
    var models = new List<Model>();
    HashSet<Mesh>? unownedMeshes = null;

    try {
      var missingOverlayCount = 0;

      ResolvedSceneryObject? ResolvePlacement(SceneryPlacement placement) {
        if (string.IsNullOrWhiteSpace(placement.OverlayPath)) {
          missingOverlayCount++;
          return null;
        }

        var context = GetOrCreateContext(
          placement.OverlayPath,
          contextsByOverlay,
          activeContexts,
          createContext);
        if (context.IsMissingOverlay) {
          missingOverlayCount++;
          return null;
        }
        return context.Resolve(placement.ObjectKey);
      }

      var geometry = buildGeometry(park, terrain, ResolvePlacement)
        ?? throw new InvalidDataException("The scenery geometry builder returned null.");
      if (geometry.Batches == null)
        throw new InvalidDataException("The scenery geometry builder returned null batches.");

      unownedMeshes = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
      ValidateAndCollectMeshes(geometry.Batches, unownedMeshes);
      var missingTextureCount = 0;
      foreach (var batch in geometry.Batches) {
        if (batch == null)
          throw new InvalidDataException("The scenery geometry builder returned a null batch.");
        if (batch.Mesh == null)
          throw new InvalidDataException("The scenery geometry builder returned a null mesh.");

        var cullBackFaces = batch.Key.Sides switch {
          1 => false,
          3 => true,
          _ => throw new InvalidDataException(
            $"Scenery batch '{batch.Key.FtxRef}' has invalid sides {batch.Key.Sides}."),
        };

        Material material;
        var engineGlobalMaterial = CreateEngineGlobalMaterial(batch.Key);
        if (engineGlobalMaterial != null) {
          material = engineGlobalMaterial;
        } else if (string.IsNullOrWhiteSpace(batch.Key.FtxRef)) {
          material = new Flat();
        } else {
          if (string.IsNullOrWhiteSpace(batch.Key.OverlayPath))
            throw new InvalidDataException(
              $"Textured scenery batch '{batch.Key.FtxRef}' has no overlay path.");
          if (!contextsByOverlay.TryGetValue(batch.Key.OverlayPath, out var context) ||
              context.IsMissingOverlay)
            throw new InvalidDataException(
              $"Textured scenery batch '{batch.Key.FtxRef}' has no resolved overlay context.");

          if (!context.TryResolveTexture(
                batch.Key.FtxRef,
                batch.Key.FlexiColours,
                out var texture)) {
            missingTextureCount++;
            batch.Mesh.Dispose();
            unownedMeshes.Remove(batch.Mesh);
            continue;
          }

          var blendMode = batch.Key.Transparency switch {
            0 => MaterialBlendMode.Opaque,
            1 => MaterialBlendMode.AlphaMask,
            2 => MaterialBlendMode.Alpha,
            _ => throw new InvalidDataException(
              $"Scenery batch '{batch.Key.FtxRef}' has invalid transparency " +
              $"{batch.Key.Transparency}."),
          };
          var alphaReference = ResolveAlphaReference(batch.Key.TxsRef);
          var textured = alphaReference.HasValue
            ? new Textured(blendMode, alphaReference.Value)
            : new Textured(blendMode);
          try {
            textured.AlbedoTexture = texture;
          } catch {
            textured.Dispose();
            throw;
          }
          material = textured;
        }
        material.CullBackFaces = cullBackFaces;

        var model = TransferToModel(batch.Mesh, material, createModel);
        models.Add(model);
        unownedMeshes.Remove(batch.Mesh);
      }

      var releaseErrors = ReleaseContexts(activeContexts);
      if (releaseErrors.Count > 0)
        throw new AggregateException("Scenery resource cleanup failed.", releaseErrors);

      return new ScenerySceneLoadResult(
        models.ToArray(),
        geometry,
        missingOverlayCount,
        missingTextureCount);
    } catch (Exception primaryError) {
      var cleanupErrors = new List<Exception>();
      ReleaseModels(models, cleanupErrors);
      if (unownedMeshes != null) ReleaseMeshes(unownedMeshes, cleanupErrors);
      cleanupErrors.AddRange(ReleaseContexts(activeContexts));
      if (cleanupErrors.Count == 0) throw;
      throw new AggregateException(
        "Scenery scene construction failed and cleanup also reported errors.",
        [primaryError, .. cleanupErrors]);
    }
  }

  private static IScenerySceneResourceContext GetOrCreateContext(
    string overlayPath,
    Dictionary<string, IScenerySceneResourceContext> contexts,
    List<IScenerySceneResourceContext> activeContexts,
    Func<string, IScenerySceneResourceContext> createContext
  ) {
    if (contexts.TryGetValue(overlayPath, out var context)) return context;

    context = createContext(overlayPath)
      ?? throw new InvalidOperationException(
        $"The scenery resource factory returned null for '{overlayPath}'.");
    contexts.Add(overlayPath, context);
    activeContexts.Add(context);
    return context;
  }

  private static Material? CreateEngineGlobalMaterial(SceneryMaterialKey key) {
    if (string.Equals(key.TxsRef, "SIWater:txs", StringComparison.OrdinalIgnoreCase)) {
      if (key.Transparency != 2)
        throw new InvalidDataException(
          $"SIWater scenery batch '{key.FtxRef}' must use alpha transparency 2, " +
          $"got {key.Transparency}.");
      return new Water();
    }

    if (string.Equals(
          key.TxsRef,
          "SIOpaqueChrome:txs",
          StringComparison.OrdinalIgnoreCase)) {
      if (key.Transparency != 0)
        throw new InvalidDataException(
          $"SIOpaqueChrome scenery batch '{key.FtxRef}' must be opaque, " +
          $"got transparency {key.Transparency}.");
      return new Chrome();
    }

    return null;
  }

  private static byte? ResolveAlphaReference(string? taggedReference) {
    if (IsTextureStyleFamily(taggedReference, "SIAlphaMaskLow")) return 100;
    if (IsTextureStyleFamily(taggedReference, "SIAlphaMask"))
      return Textured.DefaultAlphaMaskReference;
    if (IsTextureStyleFamily(taggedReference, "SIAlpha")) return 8;
    return null;
  }

  private static bool IsTextureStyleFamily(
    string? taggedReference,
    string familyName
  ) {
    const string tag = ":txs";
    if (string.IsNullOrEmpty(taggedReference) ||
        !taggedReference.EndsWith(tag, StringComparison.OrdinalIgnoreCase))
      return false;
    var resourceName = taggedReference[..^tag.Length];
    return resourceName.StartsWith(familyName, StringComparison.OrdinalIgnoreCase);
  }

  private static void ValidateAndCollectMeshes(
    IReadOnlyList<SceneryGeometryBatch> batches,
    HashSet<Mesh> meshes
  ) {
    InvalidDataException? validationError = null;
    foreach (var batch in batches) {
      if (batch == null) {
        validationError ??= new InvalidDataException(
          "The scenery geometry builder returned a null batch.");
        continue;
      }
      if (batch.Mesh == null) {
        validationError ??= new InvalidDataException(
          "The scenery geometry builder returned a null mesh.");
        continue;
      }
      if (!meshes.Add(batch.Mesh))
        validationError ??= new InvalidDataException(
          "The scenery geometry builder returned one mesh in multiple batches.");
    }
    if (validationError != null) throw validationError;
  }

  private static Model TransferToModel(
    Mesh mesh,
    Material material,
    Func<Mesh, Model> createModel
  ) {
    Model? model = null;
    try {
      model = createModel(mesh)
        ?? throw new InvalidOperationException("The scenery model factory returned null.");
      if (!ReferenceEquals(model.Mesh, mesh))
        throw new InvalidOperationException(
          "The scenery model factory returned a model owning a different mesh.");
      if (model.Material != null)
        throw new InvalidOperationException(
          "The scenery model factory returned a model that already owns a material.");
      model.Material = material;
      return model;
    } catch (Exception primaryError) {
      var cleanupErrors = new List<Exception>();
      var materialOwnedByModel = model != null && ReferenceEquals(model.Material, material);
      if (model != null) {
        try {
          model.Dispose();
        } catch (Exception error) {
          cleanupErrors.Add(error);
        }
      }
      if (!materialOwnedByModel) {
        try {
          material.Dispose();
        } catch (Exception error) {
          cleanupErrors.Add(error);
        }
      }
      if (cleanupErrors.Count == 0) throw;
      throw new AggregateException(
        "Scenery model ownership transfer failed and cleanup also reported errors.",
        [primaryError, .. cleanupErrors]);
    }
  }

  private static IScenerySceneResourceContext CreateResourceContext(
    string installRoot,
    string overlayPath
  ) {
    var (commonPath, uniquePath) = ResolveOverlayPairPaths(installRoot, overlayPath);
    if (!File.Exists(commonPath) && !File.Exists(uniquePath))
      return MissingScenerySceneResourceContext.Instance;
    return new OvlScenerySceneResourceContext(installRoot, overlayPath);
  }

  private static (string CommonPath, string UniquePath) ResolveOverlayPairPaths(
    string installRoot,
    string overlayPath
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    ArgumentException.ThrowIfNullOrWhiteSpace(overlayPath);
    var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
    var normalizedOverlay = overlayPath.Trim()
      .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
      .Replace('\\', Path.DirectorySeparatorChar)
      .Replace('/', Path.DirectorySeparatorChar);
    if (normalizedOverlay.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase))
      normalizedOverlay = normalizedOverlay[..^".common.ovl".Length];
    else if (normalizedOverlay.EndsWith(".unique.ovl", StringComparison.OrdinalIgnoreCase))
      normalizedOverlay = normalizedOverlay[..^".unique.ovl".Length];

    if (string.IsNullOrWhiteSpace(Path.GetFileName(normalizedOverlay)))
      throw new ArgumentException("The overlay path must name an OVL pair.", nameof(overlayPath));

    var commonPath = Path.GetFullPath(Path.Combine(root, normalizedOverlay + ".common.ovl"));
    if (!IsContainedBy(root, commonPath))
      throw new ArgumentException(
        "The overlay path must stay inside the installation root.",
        nameof(overlayPath));
    return (commonPath, commonPath[..^".common.ovl".Length] + ".unique.ovl");
  }

  private static bool IsContainedBy(string root, string path) {
    var relative = Path.GetRelativePath(root, path);
    return !Path.IsPathRooted(relative) &&
      !string.Equals(relative, "..", StringComparison.Ordinal) &&
      !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
      !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
  }

  private static List<Exception> ReleaseContexts(
    List<IScenerySceneResourceContext> contexts
  ) {
    var errors = new List<Exception>();
    while (contexts.Count > 0) {
      var lastIndex = contexts.Count - 1;
      var context = contexts[lastIndex];
      contexts.RemoveAt(lastIndex);
      try {
        context.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private static void ReleaseModels(List<Model> models, List<Exception> errors) {
    while (models.Count > 0) {
      var lastIndex = models.Count - 1;
      var model = models[lastIndex];
      models.RemoveAt(lastIndex);
      try {
        model.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
    }
  }

  private static void ReleaseMeshes(IEnumerable<Mesh> meshes, List<Exception> errors) {
    foreach (var mesh in meshes) {
      try {
        mesh.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
    }
  }

  private sealed class OvlScenerySceneResourceContext : IScenerySceneResourceContext {
    private readonly SceneryResourceCatalog catalog;
    private readonly SceneryVisualResolver visuals;
    private readonly SceneryTextureResolver textures;
    private bool disposed;

    public bool IsMissingOverlay => false;

    public OvlScenerySceneResourceContext(string installRoot, string overlayPath) {
      catalog = new SceneryResourceCatalog(installRoot, overlayPath);
      try {
        visuals = new SceneryVisualResolver(catalog);
        textures = new SceneryTextureResolver(catalog);
      } catch {
        catalog.Dispose();
        throw;
      }
    }

    public ResolvedSceneryObject? Resolve(string objectKey) {
      ObjectDisposedException.ThrowIf(disposed, this);
      return visuals.Resolve(objectKey);
    }

    public bool TryResolveTexture(
      string taggedReference,
      SceneryFlexiColours flexiColours,
      out Texture? texture
    ) {
      ObjectDisposedException.ThrowIf(disposed, this);
      return textures.TryResolve(taggedReference, flexiColours, out texture);
    }

    public void Dispose() {
      if (disposed) return;
      disposed = true;
      var errors = new List<Exception>();
      try {
        textures.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
      try {
        catalog.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
      if (errors.Count > 0) throw new AggregateException(errors);
    }
  }

  private sealed class MissingScenerySceneResourceContext : IScenerySceneResourceContext {
    public static MissingScenerySceneResourceContext Instance { get; } = new();

    public bool IsMissingOverlay => true;

    public ResolvedSceneryObject? Resolve(string objectKey) => null;

    public bool TryResolveTexture(
      string taggedReference,
      SceneryFlexiColours flexiColours,
      out Texture? texture
    ) {
      texture = null;
      return false;
    }

    public void Dispose() { }
  }
}
