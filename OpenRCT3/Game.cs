// Game
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using DryIoc;
using NLog;
using OpenCobra.GDK;
using OpenCobra.GDK.Game;
using OpenCobra.GDK.GUI;
using OpenCobra.GDK.Materials;
using OpenCobra.GDK.Meshes;
using OpenCobra.GDK.Platform;
using OpenRCT3.OpenGL;
using OpenRCT3.Platforms;
using OpenRCT3.Scenario;
using OpenRCT3.Simulation;
using Silk.NET.Input;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;


#if WINDOWS
using System.Windows.Forms;
#elif OSX
using AppKit;
#endif

namespace OpenRCT3;

/// <summary>
/// The game world.
/// </summary>
public class Game : IGame {
  /// <summary>The maximum number of simulation ticks to process in one instant.</summary>
  private const int MaxSimulationTicks = 8;
  /// <summary>The minimum time between lag warning messages.</summary>
  /// <remarks>This prevents spamming the log with warnings about lag.</remarks>
  private readonly TimeSpan lagWarningDebounceInterval = TimeSpan.FromSeconds(10);

  private readonly static Logger logger = LogManager.GetCurrentClassLogger();
  private readonly GameRunLifecycle lifecycle = new();
  private bool isPaused = false;
  private readonly ManualResetEvent resumeSignal = new(true);
  private readonly Stopwatch stopwatch = new();
  private DateTime lastLagWarning = DateTime.Now;
  private IRenderer? renderer = ResolveRenderer(Game.IoC);
  private Scene? ownedScene;
  private Simulation.World? ownedWorld;
  private readonly List<IDisposable> ownedRideCarVisualTemplateOwners = [];
  private readonly object cameraControllerGate = new();
  private IInputContext? cameraInput;
  private CameraController? cameraController;
  private bool disposed;

  public static Container IoC => IGame.IoC;
  public static Game? Instance { get; private set; }
  public static bool IsRunning => Instance?.lifecycle.IsRunning ?? false;

  internal static Game? DetachInstance() {
    var instance = Instance;
    Instance = null;
    return instance;
  }

  internal static IRenderer ResolveRenderer(IResolverContext resolver) =>
    resolver.Resolve<IRenderer>();

  internal IRenderer? BoundRenderer => Volatile.Read(ref renderer);

  internal void BindRenderer(IRenderer replacement) =>
    Volatile.Write(ref renderer, replacement);

  internal void UnbindRenderer(IRenderer ownedRenderer) =>
    Interlocked.CompareExchange(ref renderer, null, ownedRenderer);

  internal void BindCameraInput(IInputContext replacement) {
    ArgumentNullException.ThrowIfNull(replacement);

    CameraController? previous;
    lock (cameraControllerGate) {
      if (disposed) return;
      previous = cameraController;
      cameraController = new CameraController(
        Scene.Camera,
        replacement,
        static () => Controller.CaptureKeyboard,
        static () => Controller.CaptureMouse
      );
      cameraInput = replacement;
    }
    previous?.Dispose();
  }

  internal void UnbindCameraInput(IInputContext ownedInput) {
    ArgumentNullException.ThrowIfNull(ownedInput);

    CameraController? controller;
    lock (cameraControllerGate) {
      if (!ReferenceEquals(cameraInput, ownedInput)) return;
      cameraInput = null;
      controller = cameraController;
      cameraController = null;
    }
    controller?.Dispose();
  }

  /// <summary>
  /// Default frame rate of the game loop, in frames per second.
  /// </summary>
  public readonly static int DefaultFrameRate = 60;

  /// <summary>
  /// Raised once the game has started and the game loop is running.
  /// </summary>
  /// <remarks>
  /// The game is started via <see cref="Run"/>.
  /// </remarks>
  public event Action? Started;
  /// <summary>
  /// <para>Raised when the game ends, i.e. when the user quits.</para>
  /// <para>See <see cref="Quit"/>.</para>
  /// </summary>
  public event Action? Exited;

  public AppConfig Config { get; } = AppConfig.Instance;

  public bool IsPaused => isPaused;

  /// <summary>
  /// <para>The time taken to render the last frame, or null if no frame has been rendered yet.</para>
  /// <para>Use <see cref="TargetFrameRate"/> to set the frame rate.</para>
  /// </summary>
  public TimeSpan FrameTime { get; private set; } = TimeSpan.Zero;

  /// <summary>
  /// Target frame rate of the game loop, in frames per second.
  /// </summary>
  public int TargetFrameRate {
    get => Convert.ToInt32(1.0 / TargetFrameTime.TotalSeconds);
    set => TargetFrameTime = TimeSpan.FromSeconds(1.0 / value);
  }

  /// <summary>
  /// Target frame time of the game loop.
  /// </summary>
  public TimeSpan TargetFrameTime { get; private set; } = TimeSpan.FromSeconds(1.0 / 60.0);

  /// <summary>
  /// Target simulation tick rate.
  /// </summary>
  public TimeSpan TargetUpdateRate { get; set; } = TimeSpan.FromSeconds(1.0 / 60.0);

  /// <summary>
  /// Whether the game should use vertical sync (VSync) to limit the frame rate.
  /// </summary>
  public bool VSync { get; set; } = false;

  public Simulation.World World { get; } = new();
  public Scene Scene { get; } = new();

  public Game() {
    ownedScene = Scene;
    ownedWorld = World;

    InitializeOwnedGame(
      Initialize,
      Dispose,
      () => Instance = this);
  }

  private void Initialize() {

    logger.Trace("Creating game world...");
    logger.Warn("Simulation features are unimplemented!");

    // Load the game world
    // TODO: Show a progress bar while loading
    World.Load();
    logger.Debug("Game world loaded");

    // Build texture-batched meshes from the loaded terrain's corner-height grid. Each DAT cell's
    // decoded surface/cliff indices select the matching texture from the terrain catalog.
    Debug.Assert(World.Terrain != null);
    Debug.Assert(World.Terrain.TextureCatalog != null);
    foreach (var batch in TerrainMeshBuilder.BuildBatches(World.Terrain, Vector4.One)) {
      var texture = batch.Kind switch {
        TerrainMaterialKind.Surface => World.Terrain.TextureCatalog.GetSurface(batch.Index),
        TerrainMaterialKind.Cliff => World.Terrain.TextureCatalog.GetCliff(batch.Index),
        _ => throw new ArgumentOutOfRangeException(nameof(batch.Kind), batch.Kind, null),
      };
      var terrainModel = new Model(batch.Mesh) {
        Material = new Textured { AlbedoTexture = texture }
      };
      Scene.Models.Add(terrainModel);
    }
    logger.Debug("Added terrain meshes");

    Debug.Assert(World.Park != null);
    var installPath = Config.InstallPath
      ?? throw new InvalidOperationException("RCT3 installation path is not configured.");
    if (World.Park.PathPlacements.Count > 0) {
      using var pathSurfaces = PathSurfaceResourceResolver.LoadInstalled(
        installPath,
        World.Park.PathPlacements.Select(placement => placement.Tile).ToArray());
      var pathVisuals = PathVisualSceneLoader.Load(World.Park, World.Terrain, pathSurfaces);
      Scene.Models.AddRange(pathVisuals.Models);
      // Unknown custom and legacy DAT surfaces keep the established kind-specific vertex tint in
      // the fallback batches; resolved PTD/QTD surfaces use their native owner SHS and FTX materials.
      logger.Debug(
        "Added {ModelCount} native path models for {RenderedCount} placements; " +
        "{FallbackCount} placements kept flat fallback geometry " +
        "({UnsupportedTopologyCount} isolated ordinary); shapes: {ShapeKinds}",
        pathVisuals.ShapeModelCount,
        pathVisuals.RenderedPlacementCount,
        pathVisuals.FallbackPlacementCount,
        pathVisuals.UnsupportedTopologyPlacementCount,
        string.Join(", ", pathVisuals.ShapeKinds.Select(pair => $"{pair.Key}={pair.Value}")));
    }
    logger.Debug("Added {Count} path tiles", World.Park.PathPlacements.Count);

    var scenery = ScenerySceneLoader.Load(World.Park, World.Terrain, installPath);
    Scene.Models.AddRange(scenery.Models);
    logger.Debug(
      "Added {ModelCount} scenery models from {RenderedCount} placements; " +
      "skipped {SkippedCount} placements ({MissingOverlayCount} missing overlays) and " +
      "{MissingTextureCount} missing-texture batches",
      scenery.Models.Count,
      scenery.Geometry.RenderedPlacementCount,
      scenery.Geometry.SkippedPlacementCount,
      scenery.MissingOverlayPlacementCount,
      scenery.MissingTextureBatchCount);

    WildAnimalCameraFramingResult? wildAnimalDiagnosticFraming = null;
    if (World.Park.WildAnimalPlacements.Count > 0) {
      try {
        var animals = WildAnimalSceneLoader.Load(World.Park, installPath);
        var publishedAnimals = false;
        try {
          var animalFraming = GamePresentationOptions.ShowWildAnimalDiagnostics
            ? WildAnimalCameraFraming.Calculate(animals.Scene)
            : null;
          Scene.Models.AddRange(animals.Scene.Models);
          publishedAnimals = true;
          wildAnimalDiagnosticFraming = animalFraming;
        }
        finally {
          if (!publishedAnimals)
            ResourceReleaser.Run(animals.Scene.Models.Reverse()
              .Select(model => new Action(model.Dispose)));
        }
        World.Park.WildAnimalResources = animals.Resources;
        World.Park.WildAnimalScene = animals.Scene;
        logger.Debug(
          "Added {ModelCount} static Wild-animal models for {BuiltCount} of " +
          "{PlacementCount} saved animals across {SpeciesCount} species and " +
          "{MaterialCount} exact TEX materials; skipped {HiddenCount} hidden animals and " +
          "{MissingMaterialCount} missing-material batches",
          animals.Scene.ModelCount,
          animals.Scene.BuiltPlacementCount,
          animals.Scene.SourcePlacementCount,
          animals.SpeciesCount,
          animals.MaterialCount,
          animals.Scene.HiddenPlacementCount,
          animals.Scene.MissingMaterialBatchCount);
      }
      catch (Exception error) when (
        error is InvalidDataException or IOException or UnauthorizedAccessException or
          ArgumentException or InvalidOperationException or AggregateException or
          OverflowException) {
        // Wild animals are additive while the decoded terrain and scenery remain authoritative.
        // Unsupported custom WAS/MDL/TXS data must not make an otherwise valid park unloadable.
        logger.Warn(error, "Wild-animal scene could not be built");
      }
    }

    Vector3? rideCarDiagnosticTarget = null;
    float? rideCarDiagnosticDistance = null;
    IReadOnlyList<Model> rideTrackDiagnosticModels = [];
    if (World.Park.RideTrackPlacements.Count > 0 && World.Park.RideTracks.Count > 0) {
      try {
        using var loadedTrackResources = RideTrackResourceCatalogLoader.Load(
          installPath,
          World.Park.RideTrackPlacements,
          World.Park.TrackedRideInstances,
          World.Park.RideTrainInstances);
        World.Park.RideResources = loadedTrackResources.RideResources;
        var trackResources = loadedTrackResources.Catalog.ResolveAll(
          World.Park.RideTrackPlacements);
        var trackGeometry = RideTrackGeometryResolver.Resolve(
          World.Terrain,
          World.Park.RideTracks,
          trackResources);
        var trackSpatialIndex = RideTrackGeometrySpatialIndex.Build(trackGeometry);
        World.Park.RideTrackGeometry = trackGeometry;
        World.Park.RideTrackSpatialIndex = trackSpatialIndex;

        logger.Debug(
          "Resolved {TrackCount} ride tracks from {PlacementCount} placements and " +
          "{PairCount} exact OVL pairs: {OpenCount} open, {CircuitCount} circuits, " +
          "{UnresolvedCount} unresolved, {UnsupportedGeometryCount} unsupported geometry, " +
          "{UnsupportedTopologyCount} unsupported topology, {IssueCount} resource issues",
          trackGeometry.Tracks.Count,
          trackResources.Placements.Count,
          loadedTrackResources.Context.LoadedCommonPaths.Count,
          trackGeometry.Tracks.Count(track =>
            track.Status == RideTrackGeometryStatus.OpenTrack),
          trackGeometry.Tracks.Count(track =>
            track.Status == RideTrackGeometryStatus.Circuit),
          trackGeometry.UnresolvedResourceTrackCount,
          trackGeometry.UnsupportedGeometryTrackCount,
          trackGeometry.UnsupportedTopologyTrackCount,
          loadedTrackResources.Issues.Count);
        foreach (var outcome in trackGeometry.Tracks.Where(track => !track.IsResolved))
          logger.Debug(
            "Ride track {TrackId} outcome {Status}: {Detail}",
            outcome.Track.SourceEntryId,
            outcome.Status,
            outcome.Detail);
        logger.Debug(
          "Indexed {ResolvedCount} of {TrackCount} ride-track bounds",
          trackSpatialIndex.ResolvedTrackCount,
          trackSpatialIndex.TrackCount);
        try {
          var trackVisuals = RideTrackVisualResourceBridge.Resolve(
            loadedTrackResources.TrackVisualResources);
          using var trackVisualMaterials = new RideCarVisualMaterialResolver(
            loadedTrackResources.Context);
          var trackVisualScene = RideTrackVisualSceneBuilder.Build(
            trackResources,
            trackVisuals,
            World.Terrain,
            trackVisualMaterials);
          var publishedTrackVisuals = false;
          try {
            Scene.Models.AddRange(trackVisualScene.Models);
            publishedTrackVisuals = true;
          }
          finally {
            if (!publishedTrackVisuals)
              ResourceReleaser.Run(trackVisualScene.Models.Reverse()
                .Select(model => new Action(model.Dispose)));
          }
          logger.Debug(
            "Added {ModelCount} ride-track visual models from {RenderedCount} placements; " +
            "skipped {SkippedCount} placements and {MissingMaterialCount} missing-material " +
            "batches",
            trackVisualScene.ModelCount,
            trackVisualScene.RenderedPlacementCount,
            trackVisualScene.SkippedPlacementCount,
            trackVisualScene.MissingMaterialBatchCount);
        }
        catch (Exception error) when (
          error is InvalidDataException or IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or AggregateException or
            OverflowException) {
          // Exact visual composition is additive. Keep the proven track graph and optional contact
          // rail diagnostics when unsupported or malformed visual resources cannot be rendered.
          logger.Warn(error, "Ride-track visual scene could not be built");
        }
        var rideResourceCounts = loadedTrackResources.RideResources.DecodedCounts;
        logger.Debug(
          "Resolved {ResolvedCount} of {InstanceCount} ride-instance resources; decoded " +
          "{RideCount} TRR, {TrainCount} RIT, {CarCount} RIC, and {VisualCount} SVD with " +
          "{UnresolvedEdgeCount} unresolved graph edges",
          loadedTrackResources.RideResources.ResolvedInstanceCount,
          loadedTrackResources.RideResources.Instances.Count,
          rideResourceCounts.TrackedRides,
          rideResourceCounts.RideTrains,
          rideResourceCounts.RideCars,
          rideResourceCounts.SceneryItemVisuals,
          loadedTrackResources.RideResources.Graph.UnresolvedReferenceCount);
        var carVisuals = loadedTrackResources.RideResources.CarVisuals;
        logger.Debug(
          "Linked {VisualCount} exact ride-car visuals to {ResolvedShapeLodCount} decoded " +
          "shape LODs with {UnresolvedShapeReferenceCount} unresolved shape references",
          carVisuals.Visuals.Count,
          carVisuals.ResolvedShapeLodCount,
          carVisuals.UnresolvedShapeReferenceCount);
        try {
          var carVisualHierarchy = RideCarVisualHierarchyResolver.Resolve(
            loadedTrackResources.RideResources.Graph,
            carVisuals);
          World.Park.RideCarVisualHierarchy = carVisualHierarchy;
          logger.Debug(
            "Resolved {ResolvedPartCount} of {PartCount} ride-car axle/wheel hierarchy parts " +
            "with {AmbiguousPartCount} ambiguous anchors",
            carVisualHierarchy.ResolvedPartCount,
            carVisualHierarchy.Cars.Count * 6,
            carVisualHierarchy.AmbiguousPartCount);
        }
        catch (Exception error) when (
          error is InvalidDataException or ArgumentException or InvalidOperationException) {
          logger.Warn(error, "Ride-car visual hierarchy could not be resolved");
        }

        try {
          var instanceTracks = RideInstanceTrackGraph.Build(
            World.Park.TrackedRideInstances,
            World.Park.RideTracks);
          var trackRuntime = RideInstanceTrackRuntimeRegistry.Build(
            instanceTracks,
            trackGeometry);
          var trackSpatialQuery = RideInstanceTrackSpatialQuery.Build(
            trackRuntime,
            trackSpatialIndex);
          World.Park.RideTrackRuntime = trackRuntime;
          World.Park.RideTrackSpatialQuery = trackSpatialQuery;
          logger.Debug(
            "Composed {InstanceCount} ride instances with runtime track outcomes: " +
            "{ResolvedCount} resolved and {SkippedCount} skipped",
            trackRuntime.InstanceCount,
            trackRuntime.ResolvedTrackCount,
            trackRuntime.InstanceCount - trackRuntime.ResolvedTrackCount);

          try {
            var trainRuntime = RideInstanceTrainRuntimeRegistry.Build(
              trackRuntime,
              loadedTrackResources.RideResources.TrainInstances);
            World.Park.RideTrainRuntime = trainRuntime;
            logger.Debug(
              "Composed {LinkedTrainCount} of {SavedTrainCount} saved trains with exact ride " +
              "runtime identities: {ResolvedTrackCount} on resolved tracks, " +
              "{ResolvedResourceCount} with exact RITs",
              trainRuntime.LinkedTrainCount,
              trainRuntime.SavedTrainCount,
              trainRuntime.ResolvedTrackTrainCount,
              trainRuntime.ResolvedResourceTrainCount);

            var consistRuntime = RideInstanceTrainConsistRuntimeRegistry.Build(
              trainRuntime,
              loadedTrackResources.RideResources,
              World.Park.RideCarInstances);
            World.Park.RideTrainConsistRuntime = consistRuntime;
            logger.Debug(
              "Composed {ResolvedConsistCount} of {ConsistCount} saved train consists from " +
              "exact graph-backed car roles",
              consistRuntime.ResolvedCount,
              consistRuntime.Entries.Count);

            var carRuntime = RideCarInstanceRuntimeRegistry.Build(
              trainRuntime,
              World.Park.RideCarInstances,
              consistRuntime);
            World.Park.RideCarRuntime = carRuntime;
            logger.Debug(
              "Composed {LinkedCarCount} of {SavedCarCount} saved cars: " +
              "{ResolvedTrackPieceCarCount} with exact track-piece pairs and " +
              "{ResolvedResourceCarCount} with exact RIC resources",
              carRuntime.LinkedCarCount,
              carRuntime.SavedCarCount,
              carRuntime.ResolvedTrackPieceCarCount,
              carRuntime.ResolvedResourceCarCount);

            RideCarVisualVariantSelectionRegistry? carVisualVariants = null;
            try {
              carVisualVariants = RideCarVisualVariantSelector.Build(carRuntime, carVisuals);
              World.Park.RideCarVisualVariants = carVisualVariants;
              logger.Debug(
                "Selected exact visual variants for {SelectedCarCount} of {CarCount} saved cars " +
                "with {BodyFallbackCount} body-control fallbacks",
                carVisualVariants.SelectedCount,
                carVisualVariants.Entries.Count,
                carVisualVariants.BodyControlFallbackCount);
            }
            catch (Exception error) when (
              error is InvalidDataException or ArgumentException or InvalidOperationException or
                OverflowException) {
              logger.Warn(error, "Saved ride-car visual variants could not be selected");
            }

            var wheelCursors = RideCarSavedWheelCursorRegistry.Build(
              carRuntime,
              World.Park.RideTrackPieceRecords);
            World.Park.RideCarWheelCursors = wheelCursors;
            logger.Debug(
              "Resolved {ResolvedCarCount} of {CarCount} saved cars to " +
              "{ResolvedContactCount} exact static wheel contacts",
              wheelCursors.ResolvedCarCount,
              wheelCursors.CarCount,
              wheelCursors.ResolvedContactCount);

            using var visualMaterials = new RideCarVisualMaterialResolver(
              loadedTrackResources.Context);
            RideCarStaticSceneBuildResult carScene;
            RideCarVisualHierarchySceneBuildResult? hierarchyScene = null;
            if (carVisualVariants != null) {
              RideCarVariantVisualTemplateRegistry? variantTemplates =
                RideCarVariantVisualTemplateRegistry.Build(carVisualVariants);
              try {
                var variantCars = RideCarVariantStaticInstanceRegistry.Build(
                  carRuntime,
                  wheelCursors,
                  carVisualVariants,
                  variantTemplates);
                carScene = RideCarStaticSceneBuilder.Build(
                  variantCars,
                  (entry, batch) => ResolveRideCarVariantMaterial(
                    visualMaterials,
                    entry,
                    batch));
                if (World.Park.RideCarVisualHierarchy != null) {
                  try {
                    RideCarVisualTemplateRegistry? hierarchyTemplates =
                      RideCarVisualTemplateRegistry.Build(carVisuals);
                    try {
                      var hierarchyInstances =
                        RideCarVisualHierarchyStaticInstanceRegistry.Build(
                          variantCars,
                          World.Park.RideCarVisualHierarchy,
                          hierarchyTemplates);
                      hierarchyScene = RideCarVisualHierarchySceneBuilder.Build(
                        hierarchyInstances,
                        visualMaterials.ResolveMaterial);
                      RetainRideCarVisualTemplateOwner(hierarchyTemplates);
                      hierarchyTemplates = null;
                      World.Park.PublishRideCarVisualHierarchyScene(
                        hierarchyInstances,
                        hierarchyScene);
                    }
                    finally {
                      hierarchyTemplates?.Dispose();
                    }
                  }
                  catch (Exception error) when (
                    error is InvalidDataException or ArgumentException or
                      InvalidOperationException or AggregateException) {
                    logger.Warn(error, "Ride-car axle/wheel scene could not be built");
                  }
                }
                RetainRideCarVisualTemplateOwner(variantTemplates);
                variantTemplates = null;
              }
              finally {
                variantTemplates?.Dispose();
              }
            } else {
              RideCarVisualTemplateRegistry? visualTemplates =
                RideCarVisualTemplateRegistry.Build(carVisuals);
              try {
                var staticCars = RideCarStaticInstanceRegistry.Build(
                  carRuntime,
                  wheelCursors,
                  visualTemplates);
                carScene = RideCarStaticSceneBuilder.Build(
                  staticCars,
                  visualMaterials.ResolveMaterial);
                RetainRideCarVisualTemplateOwner(visualTemplates);
                visualTemplates = null;
              }
              finally {
                visualTemplates?.Dispose();
              }
            }
            World.Park.RideCarScene = carScene;
            Scene.Models.AddRange(carScene.Models);
            if (hierarchyScene != null) {
              Scene.Models.AddRange(hierarchyScene.Models);
              logger.Debug(
                "Added {ModelCount} axle/wheel models for {BuiltPartCount} of " +
                "{SourcePartCount} resolved hierarchy parts; skipped " +
                "{MissingMaterialBatchCount} missing-material batches",
                hierarchyScene.ModelCount,
                hierarchyScene.BuiltPartCount,
                hierarchyScene.SourcePartCount,
                hierarchyScene.MissingMaterialBatchCount);
            }
            try {
              var trainSceneMotion = RideTrainSceneMotionController.Build(
                trainRuntime.Entries,
                carScene,
                hierarchyScene);
              World.Park.RideTrainSceneMotion = trainSceneMotion;
              logger.Debug(
                "Authorized {AnimatedTrainCount} of {TrainCount} saved trains and " +
                "{AnimatedCarCount} rendered cars for exact scene motion",
                trainSceneMotion.AnimatedTrainCount,
                trainSceneMotion.TrainCount,
                trainSceneMotion.AnimatedCarCount);
            }
            catch (Exception error) when (
              error is InvalidDataException or ArgumentException or InvalidOperationException) {
              // Static saved-car placement remains valid when later motion composition cannot
              // prove every train, circuit, and rendered-car identity.
              logger.Warn(error, "Ride-train scene motion could not be composed");
            }
            var renderedCarCenters = carScene.ModelBindings
              .GroupBy(binding => binding.RegistryIndex)
              .Select(bindings => bindings.First().Entry.Pose!.ContactMidpoint)
              .ToArray();
            if (renderedCarCenters.Length > 0) {
              rideCarDiagnosticTarget = renderedCarCenters.Aggregate(
                Vector3.Zero,
                (sum, center) => sum + center) / renderedCarCenters.Length;
              var trainRadius = renderedCarCenters.Max(center =>
                Vector3.Distance(rideCarDiagnosticTarget.Value, center));
              rideCarDiagnosticDistance = Math.Clamp((trainRadius * 2f) + 8f, 18f, 80f);
            }
            logger.Debug(
              "Added {ModelCount} {BuildMode} ride-car models for {BuiltCarCount} of " +
              "{SourceCarCount} saved cars; skipped {MissingMaterialBatchCount} " +
              "missing-material batches",
              carScene.ModelCount,
              carScene.BuildMode,
              carScene.BuiltCarCount,
              carScene.SourceCarCount,
              carScene.MissingMaterialBatchCount);
          }
          catch (Exception error) when (
            error is InvalidDataException or ArgumentException or InvalidOperationException) {
            // Retain proven runtime track identity when malformed saved trains or cars cannot
            // compose the remaining resource-backed runtime layers.
            logger.Warn(error, "Ride train/car runtime composition could not be built");
          }
        }
        catch (Exception error) when (
          error is InvalidDataException or ArgumentException or InvalidOperationException) {
          // Retain proven per-track geometry even when malformed instance links cannot compose it.
          logger.Warn(error, "Ride-instance runtime track registry could not be built");
        }

        if (GamePresentationOptions.ShowRideTrackDiagnostics) {
          var diagnostics = RideTrackDiagnosticSceneBuilder.Build(trackGeometry);
          Scene.Models.AddRange(diagnostics.Models);
          rideTrackDiagnosticModels = diagnostics.Models;
          logger.Debug(
            "Added {ModelCount} diagnostic ride-track contact-rail models ({Detail})",
            diagnostics.Models.Count,
            diagnostics.Detail);
        }
      }
      catch (Exception error) when (
        error is InvalidDataException or IOException or UnauthorizedAccessException or
          ArgumentException or InvalidOperationException or AggregateException) {
        // Exact ride geometry is additive while the linked scenery models remain authoritative.
        // Keep the park loadable when custom or malformed OVL resources cannot enter this subset.
        logger.Warn(error, "Ride-track runtime geometry could not be resolved");
      }
    }

    // Water is a separate overlay over the terrain. Each decoded DAT WaterManager pool keeps its
    // exact triangle masks and surface height while rendering independently from the terrain mesh.
    foreach (var pool in World.Park.WaterPools) {
      var waterModel = new Model(WaterMeshBuilder.Build(
        World.Terrain,
        pool,
        new Vector4(0.12f, 0.42f, 0.72f, 1f))) {
        Material = new Water()
      };
      Scene.Models.Add(waterModel);
    }
    logger.Debug("Added {Count} water meshes", World.Park.WaterPools.Count);

    // Frame the camera on the loaded terrain's full 3D bounds. Camera's default framing (a small
    // fixed offset from the origin) only suits a toy scene; it doesn't scale to an actual map, so
    // most or all of the terrain otherwise ends up outside the view frustum.
    //
    // TerrainCameraFraming includes the OOB border and scans the real corner-height range. Centering
    // on XYZ keeps elevated maps aimed correctly, while the full 3D diagonal bounds the 45°-azimuth
    // "diamond" without the old buildable-area-only 1.8x heuristic (see CameraFramingTests).
    var framing = TerrainCameraFraming.Calculate(World.Terrain);
    Scene.Camera.Frame(framing.Target, framing.Distance, framing.MinimumDistance);
    logger.Trace("Framed camera on terrain");

    // Native diagnostics need a close enough view to inspect actual emitted geometry. Existing saved
    // ride-car framing stays authoritative; contact rails and explicitly requested animals are
    // fallbacks when no saved-car frame is available.
    var diagnosticFraming = TryCalculateModelFraming(rideTrackDiagnosticModels);
    var diagnosticTarget = GamePresentationOptions.SelectDiagnosticCameraTarget(
      GamePresentationOptions.ShowRideTrackDiagnostics,
      GamePresentationOptions.ShowWildAnimalDiagnostics,
      diagnosticFraming.HasValue,
      rideCarDiagnosticTarget.HasValue && rideCarDiagnosticDistance.HasValue,
      wildAnimalDiagnosticFraming.HasValue);
    switch (diagnosticTarget) {
      case GameDiagnosticCameraTarget.RideTrackGeometry:
        Scene.Camera.Frame(
          diagnosticFraming!.Value.Target,
          diagnosticFraming.Value.Distance,
          Camera.NearPlaneDistance * 2f);
        logger.Trace("Framed diagnostic camera on emitted ride-track geometry");
        break;
      case GameDiagnosticCameraTarget.RideCars:
        Scene.Camera.Frame(
          rideCarDiagnosticTarget!.Value,
          rideCarDiagnosticDistance!.Value,
          Camera.NearPlaneDistance * 2f);
        logger.Trace("Framed diagnostic camera on saved ride cars");
        break;
      case GameDiagnosticCameraTarget.WildAnimals:
        Scene.Camera.Frame(
          wildAnimalDiagnosticFraming!.Value.Target,
          wildAnimalDiagnosticFraming.Value.Distance,
          wildAnimalDiagnosticFraming.Value.MinimumDistance);
        logger.Trace("Framed diagnostic camera on static Wild animals");
        break;
      case GameDiagnosticCameraTarget.Terrain:
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(diagnosticTarget), diagnosticTarget, null);
    }

    // Keep normal gameplay unchanged while allowing native visual verification to capture the map
    // without an incidental editor panel obscuring it.
    if (GamePresentationOptions.ShowUserInterface) Scene.Windows.Add(new Editor());

    BindCameraInput(IoC.Resolve<IInputContext>());
  }

  private static (Vector3 Target, float Distance)? TryCalculateModelFraming(
    IReadOnlyList<Model> models
  ) {
    if (models.Count == 0) return null;
    var minimum = new Vector3(float.PositiveInfinity);
    var maximum = new Vector3(float.NegativeInfinity);
    var vertexCount = 0;
    foreach (var model in models) {
      foreach (var vertex in model.Mesh.Vertices) {
        var world = Vector3.Transform(vertex.Position, model.Transform.Matrix);
        if (!float.IsFinite(world.X) || !float.IsFinite(world.Y) || !float.IsFinite(world.Z))
          continue;
        minimum = Vector3.Min(minimum, world);
        maximum = Vector3.Max(maximum, world);
        vertexCount++;
      }
    }
    if (vertexCount == 0) return null;
    var target = (minimum + maximum) * 0.5f;
    var distance = Math.Clamp(Vector3.Distance(minimum, maximum) * 1.25f, 18f, 80f);
    return (target, distance);
  }

  /// <summary>
  /// Starts the game loop.
  /// </summary>
  /// <remarks>
  /// The game loop runs at a fixed frame rate, sleeping when ahead of schedule to reduce CPU usage.
  /// </remarks>
  /// <seealso cref="TargetFrameRate"/>
  /// <seealso cref="TargetFrameTime"/>
  /// <seealso href="https://gameprogrammingpatterns.com/game-loop.html"/>
  public void Run() {
    if (!lifecycle.TryStart()) return;

    // Run the game loop
    Started?.Invoke();
    stopwatch.Start();
    var previousTime = stopwatch.Elapsed;
    // Measures wall time that has elapsed since the last frame
    var lag = TimeSpan.Zero;

    // Implements the fixed-update-time-step, variable-rendering pattern to decouple
    // simulation stability (fixed step for physics/AI determinism) from visual
    // smoothness (variable render rate).
    //
    // See https://gameprogrammingpatterns.com/game-loop.html
    while (lifecycle.IsRunning) {
      // Wait for the resume signal if the game is paused
      if (isPaused) {
        resumeSignal.WaitOne();
        logger.Trace("Game resumed");
      }

      var frameStartTime = stopwatch.Elapsed;
      var elapsed = FrameTime = frameStartTime - previousTime;
      previousTime = frameStartTime;
      // FIXME: Ought the game NOT accumulate lag if the game was paused?
      lag += elapsed;

      // Process any pending window events, e.g. input events
#if WINDOWS
      if (!ProcessEventsAndCheckRunning(Application.DoEvents, static () => IsRunning)) break;
#elif OSX
      // FIXME: Pump macOS windowing events
      // See https://duckduckgo.com/?q=osx+how+to+pump+windowing+events+in+a+game+loop&ia=web
      if (!ProcessEventsAndCheckRunning(NSApplication.EnsureUIThread, static () => IsRunning)) break;
#endif

      // Simulation ticks are fixed steps to aid physics/AI determinism
      // For example, a 60Hz target update rate processes one tick 60 times per second
      var simulation = GameLoopTiming.PlanSimulation(
        lag,
        TargetUpdateRate,
        MaxSimulationTicks);
      LogLagWarning(lag, simulation.TickDelta);
      for (var tickCount = 0; tickCount < simulation.TickCount; tickCount++) {
        Tick(
          delta: simulation.TickDelta,
          // A fixed tick completes one whole simulation step. Residual lag can later be
          // normalized against the update interval when render interpolation is implemented.
          interpolation: simulation.TickInterpolation);
      }
      lag = simulation.RemainingLag;

      // Rendering can happen at arbitrary points between updates, and frames can
      // be dropped if the machine is slow.
      lock (cameraControllerGate) cameraController?.Update(elapsed);
      Scene.Update(delta: elapsed);
      Volatile.Read(ref renderer)?.Render(Scene);

      // Reduce CPU usage by sleeping when ahead of schedule
      var frameWorkDuration = stopwatch.Elapsed - frameStartTime;
      var remaining = GameLoopTiming.CalculateFrameSleep(
        TargetFrameTime,
        frameWorkDuration);
      if (remaining > TimeSpan.Zero) Thread.Sleep(remaining);
    }

    Exited?.Invoke();
    logger.Info("Game exited");
  }

  internal static bool ProcessEventsAndCheckRunning(
    Action processEvents,
    Func<bool> isRunning
  ) {
    processEvents();
    return isRunning();
  }

  private static Material? ResolveRideCarVariantMaterial(
    RideCarVisualMaterialResolver resolver,
    RideCarVariantStaticInstanceEntry car,
    StaticShapeMeshBatch batch
  ) {
    if (!car.IsResolved || car.BodyTemplate?.Link?.Car?.Source == null ||
        car.MaterialBatches == null)
      throw new InvalidDataException("Selected ride-car variant is not render-resolved.");
    if (!ReferenceEquals(car.CarRuntime.CarResource, car.BodyTemplate.Link.Car) ||
        !car.MaterialBatches.Any(candidate => ReferenceEquals(candidate, batch)))
      throw new InvalidDataException("Selected ride-car variant changed exact body identity.");

    var colours = RideCarVisualMaterialResolver.ResolveSavedCarColours(car.CarRuntime);
    return resolver.Resolve(
      batch,
      car.BodyTemplate.Link.Car.Source.AllowedArchivePaths,
      colours).Material;
  }

  public void Pause() {
    isPaused = true;
    resumeSignal.Reset();
  }

  public void Resume() {
    isPaused = false;
    resumeSignal.Set();
  }

  /// <summary>
  /// Try to quit the game.
  /// </summary>
  /// <returns>Whether the game stopped running.</returns>
  public bool Quit() {
    // TODO: Check for unsaved changes and prevent closure
    lifecycle.Stop();
    resumeSignal.Set();

    if (!lifecycle.IsRunning) logger.Info("Exiting game...");
    return !lifecycle.IsRunning;
  }

  public void Dispose() {
    CameraController? controller;
    Scene? scene;
    Simulation.World? world;
    IDisposable[] rideCarVisualTemplateOwners;
    lock (cameraControllerGate) {
      if (disposed) return;
      disposed = true;
      scene = ownedScene;
      world = ownedWorld;
      rideCarVisualTemplateOwners = ownedRideCarVisualTemplateOwners?.ToArray() ?? [];
      controller = cameraController;
      ownedScene = null;
      ownedWorld = null;
      ownedRideCarVisualTemplateOwners?.Clear();
      cameraInput = null;
      cameraController = null;
    }

    // Dispose GPU-backed scene resources while the graphics context is still alive, then release
    // the world-owned texture catalog and simulation systems.
    try {
      controller?.Dispose();
    }
    finally {
      DisposeOwnedResources(
        scene == null ? null : scene.Dispose,
        rideCarVisualTemplateOwners.Length == 0
          ? null
          : () => DisposeRideCarVisualTemplateOwners(rideCarVisualTemplateOwners),
        world == null ? null : world.Dispose,
        () => {
          lifecycle.Stop();
          resumeSignal.Set();
          if (ReferenceEquals(Instance, this)) Instance = null;
          GC.SuppressFinalize(this);
        });
    }
  }

  internal static void InitializeOwnedGame(
    Action initialize,
    Action cleanup,
    Action publish
  ) {
    try {
      initialize();
    }
    catch (Exception primaryError) {
      try {
        cleanup();
      }
      catch (Exception cleanupError) {
        throw new AggregateException(
          "Game initialization failed and cleanup also reported an error.",
          primaryError,
          cleanupError);
      }
      throw;
    }

    publish();
  }

  internal static void DisposeOwnedResources(
    Action? disposeScene,
    Action? disposeRideCarVisualTemplates,
    Action? disposeWorld,
    Action clearState
  ) {
    try {
      var releases = new List<Action>();
      if (disposeScene != null) releases.Add(disposeScene);
      if (disposeRideCarVisualTemplates != null)
        releases.Add(disposeRideCarVisualTemplates);
      if (disposeWorld != null) releases.Add(disposeWorld);
      ResourceReleaser.Run(releases);
    }
    finally {
      clearState();
    }
  }

  private void RetainRideCarVisualTemplateOwner(IDisposable owner) {
    ArgumentNullException.ThrowIfNull(owner);
    lock (cameraControllerGate) {
      ObjectDisposedException.ThrowIf(disposed, this);
      ownedRideCarVisualTemplateOwners.Add(owner);
    }
  }

  private static void DisposeRideCarVisualTemplateOwners(
    IEnumerable<IDisposable> owners
  ) => ResourceReleaser.Run(
    owners.Reverse().Select(owner => new Action(owner.Dispose)));

  /// <summary>
  /// Advances the simulation.
  /// </summary>
  /// <param name="delta">The time between ticks.</param>
  /// <param name="interpolation">The interpolation fraction.</param>
  private void Tick(TimeSpan delta, double interpolation) {
    var park = World.Park;
    var rideTrainMotion = park?.RideTrainSceneMotion;
    if (rideTrainMotion != null &&
        !rideTrainMotion.TryUpdate(delta, out _, out var motionError)) {
      park!.RideTrainSceneMotion = null;
      logger.Warn(
        motionError,
        "Ride-train scene motion stopped after a later pose could not be proven");
    }
    // TODO: Advance the simulation logic by a fixed time step
    // TODO: Scheduler.Execute(delta);
  }

  [Conditional("DEBUG")]
  private void LogLagWarning(TimeSpan lag, TimeSpan targetUpdateRate) {
    // TODO: Detect excessive lag and lower the user's target frame-rate
    // TODO: Maybe even show a modal to the user:
    // "You are experiencing excessive lag. Lowering frame-rate to prevent stuttering."
    // "Consider lowering your target frame-rate in the game settings."
    if (lag <= targetUpdateRate ||
        DateTime.Now - lastLagWarning <= lagWarningDebounceInterval) return;

    var details = $"{lag.TotalMilliseconds}ms (target: {targetUpdateRate.TotalMilliseconds}ms)";
    logger.Warn($"Lag has exceeded target simulation update interval: {details}");
    lastLagWarning = DateTime.Now;
  }
}

internal readonly record struct GameLoopSimulationPlan(
  int TickCount,
  TimeSpan TickDelta,
  double TickInterpolation,
  TimeSpan RemainingLag
);

internal static class GameLoopTiming {
  private readonly static TimeSpan MinimumInterval = TimeSpan.FromTicks(1);

  internal static GameLoopSimulationPlan PlanSimulation(
    TimeSpan lag,
    TimeSpan targetUpdateRate,
    int maxTicks
  ) {
    if (lag < TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(nameof(lag), lag, "Lag cannot be negative.");
    ValidatePositiveInterval(targetUpdateRate, nameof(targetUpdateRate));
    if (maxTicks < 0)
      throw new ArgumentOutOfRangeException(
        nameof(maxTicks),
        maxTicks,
        "Maximum ticks cannot be negative.");

    var remainingLag = lag;
    var tickCount = 0;
    for (; tickCount < maxTicks && remainingLag >= targetUpdateRate; tickCount++)
      remainingLag -= targetUpdateRate;
    return new(tickCount, targetUpdateRate, 1.0, remainingLag);
  }

  internal static TimeSpan CalculateFrameSleep(
    TimeSpan targetFrameTime,
    TimeSpan frameWorkDuration
  ) {
    ValidatePositiveInterval(targetFrameTime, nameof(targetFrameTime));
    if (frameWorkDuration < TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(
        nameof(frameWorkDuration),
        frameWorkDuration,
        "Frame work duration cannot be negative.");
    return frameWorkDuration < targetFrameTime
      ? targetFrameTime - frameWorkDuration
      : TimeSpan.Zero;
  }

  internal static double UpdatesPerSecond(TimeSpan targetUpdateRate) {
    ValidatePositiveInterval(targetUpdateRate, nameof(targetUpdateRate));
    return 1.0 / targetUpdateRate.TotalSeconds;
  }

  internal static TimeSpan UpdateInterval(double updatesPerSecond) {
    if (!double.IsFinite(updatesPerSecond) || updatesPerSecond <= 0)
      throw new ArgumentOutOfRangeException(
        nameof(updatesPerSecond),
        updatesPerSecond,
        "Updates per second must be finite and positive.");

    var secondsPerUpdate = 1.0 / updatesPerSecond;
    if (!double.IsFinite(secondsPerUpdate) ||
        secondsPerUpdate > TimeSpan.MaxValue.TotalSeconds ||
        secondsPerUpdate < MinimumInterval.TotalSeconds)
      throw new ArgumentOutOfRangeException(
        nameof(updatesPerSecond),
        updatesPerSecond,
        "Updates per second must produce a representable positive interval.");
    return TimeSpan.FromSeconds(secondsPerUpdate);
  }

  private static void ValidatePositiveInterval(TimeSpan interval, string parameterName) {
    if (interval <= TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(
        parameterName,
        interval,
        "Timing intervals must be positive.");
  }
}

internal sealed class GameRunLifecycle {
  private const int Created = 0;
  private const int Running = 1;
  private const int Stopped = 2;
  private int state = Created;

  public bool IsRunning => Volatile.Read(ref state) == Running;

  public bool TryStart() =>
    Interlocked.CompareExchange(ref state, Running, Created) == Created;

  public void Stop() => Interlocked.Exchange(ref state, Stopped);
}
