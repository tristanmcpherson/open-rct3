// Ride Train Scene Motion Controller
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The typed scene-motion outcome for one exact saved ride train.</summary>
internal enum RideTrainSceneMotionStatus {
  Animated,
  MissingSavedMotionState,
  MotionNotAuthorized,
  CircuitNotAuthorized,
  IncompleteRenderedConsist,
  UnavailableInitialPose,
}

/// <summary>Current motion state and immutable evidence for one saved train.</summary>
internal sealed class RideTrainSceneMotionEntry {
  public RideInstanceTrainRuntimeEntry TrainRuntime { get; }
  public RideTrainMotionAdvanceAuthorizationResult Authorization { get; }
  public RideTrainCircuitMotionAuthorizationResult CircuitAuthorization { get; }
  public RideTrainSceneMotionStatus Status { get; }
  public IReadOnlyList<RideTrainOrdinaryScenePoseCarInput> Cars { get; }
  public string? Detail { get; }
  public RideTrainMotionState? MotionState { get; internal set; }
  public bool IsAnimated => Status == RideTrainSceneMotionStatus.Animated;

  internal TrackCircuitTraversal? Traversal { get; }

  internal RideTrainSceneMotionEntry(
    RideInstanceTrainRuntimeEntry trainRuntime,
    RideTrainMotionAdvanceAuthorizationResult authorization,
    RideTrainCircuitMotionAuthorizationResult circuitAuthorization,
    RideTrainSceneMotionStatus status,
    TrackCircuitTraversal? traversal,
    IReadOnlyList<RideTrainOrdinaryScenePoseCarInput>? cars,
    RideTrainMotionState? motionState,
    string? detail = null
  ) {
    TrainRuntime = trainRuntime;
    Authorization = authorization;
    CircuitAuthorization = circuitAuthorization;
    Status = status;
    Traversal = traversal;
    Cars = cars ?? Array.Empty<RideTrainOrdinaryScenePoseCarInput>();
    MotionState = motionState;
    Detail = detail;
  }
}

/// <summary>Counts from one atomic saved-train scene update.</summary>
internal readonly record struct RideTrainSceneMotionUpdateResult(
  int AnimatedTrainCount,
  int AnimatedCarCount,
  int UpdatedBodyModelCount,
  int UpdatedHierarchyModelCount
);

/// <summary>Bounded seams for testing orchestration without duplicating native algorithms.</summary>
internal sealed record RideTrainSceneMotionControllerOperations(
  Func<RideInstanceTrainRuntimeEntry, RideTrainMotionAdvanceAuthorizationResult> Authorize,
  Func<
    RideInstanceTrainRuntimeEntry,
    RideInstanceTrackRuntimeEntry,
    IReadOnlyList<RideCarInstanceRuntimeEntry>,
    IReadOnlyList<RideCarStaticInstanceEntry>,
    RideTrainCircuitMotionAuthorizationResult> AuthorizeCircuit,
  Func<RideTrainMotionState, float, float, RideTrainMotionState> Advance,
  Func<
    TrackCircuitTraversal,
    RideTrainMotionState,
    IReadOnlyList<RideTrainOrdinaryScenePoseCarInput>,
    RideTrainOrdinaryScenePosePlan> Plan,
  Func<
    RideCarStaticSceneBuildResult,
    IReadOnlyList<RideCarSceneTransformTarget>,
    RideCarVisualHierarchySceneBuildResult?,
    RideCarVisualSceneTransformTransactionResult> Apply
) {
  public static RideTrainSceneMotionControllerOperations Default { get; } = new(
    RideTrainMotionAdvanceAuthorization.Authorize,
    RideTrainCircuitMotionAuthorization.Authorize,
    RideTrainCircuitMotionStepper.Advance,
    RideTrainOrdinaryScenePosePlanner.Resolve,
    RideCarVisualSceneTransformTransaction.Update);
}

/// <summary>Allocation ceilings for saved-train scene-motion composition.</summary>
internal readonly record struct RideTrainSceneMotionControllerLimits(
  int MaximumTrainCount,
  int MaximumCarCount
) {
  public static RideTrainSceneMotionControllerLimits Default { get; } = new(
    MaximumTrainCount: 100_000,
    MaximumCarCount: 1_000_000);
}

/// <summary>Advances authorized saved trains and atomically publishes every rendered car pose.</summary>
/// <remarks>
/// The controller composes existing executable-backed layers rather than introducing a new vehicle
/// model. It advances only states authorized by <see cref="RideTrainMotionAdvanceAuthorization"/>,
/// uses only one exact saved-cursor-selected <see cref="TrackCircuitTraversal"/>, and keeps
/// incomplete or cross-circuit trains static. Every next train state and car target is planned
/// before the body and hierarchy transaction mutates a model. Motion states are committed only
/// after that transaction succeeds, so a failed frame exposes neither partial poses nor partially
/// advanced train state.
/// </remarks>
internal sealed class RideTrainSceneMotionController {
  private readonly RideCarStaticSceneBuildResult bodyScene;
  private readonly RideCarVisualHierarchySceneBuildResult? hierarchyScene;
  private readonly RideTrainSceneMotionControllerOperations operations;
  private readonly IReadOnlyList<RideCarSceneTransformTarget> baselineTargets;
  private readonly IReadOnlyDictionary<int, int> targetPositions;

  public IReadOnlyList<RideTrainSceneMotionEntry> Entries { get; }
  public int TrainCount => Entries.Count;
  public int AnimatedTrainCount => Entries.Count(entry => entry.IsAnimated);
  public int AnimatedCarCount => Entries
    .Where(entry => entry.IsAnimated)
    .Sum(entry => entry.Cars.Count(car => car.IsRendered));

  private RideTrainSceneMotionController(
    RideCarStaticSceneBuildResult bodyScene,
    RideCarVisualHierarchySceneBuildResult? hierarchyScene,
    RideTrainSceneMotionEntry[] entries,
    RideCarSceneTransformTarget[] baselineTargets,
    IReadOnlyDictionary<int, int> targetPositions,
    RideTrainSceneMotionControllerOperations operations
  ) {
    this.bodyScene = bodyScene;
    this.hierarchyScene = hierarchyScene;
    this.operations = operations;
    Entries = Array.AsReadOnly(entries);
    this.baselineTargets = Array.AsReadOnly(baselineTargets);
    this.targetPositions = targetPositions;
  }

  public static RideTrainSceneMotionController Build(
    IReadOnlyList<RideInstanceTrainRuntimeEntry> trains,
    RideCarStaticSceneBuildResult bodyScene,
    RideCarVisualHierarchySceneBuildResult? hierarchyScene = null
  ) => Build(
    trains,
    bodyScene,
    hierarchyScene,
    RideTrainSceneMotionControllerLimits.Default,
    RideTrainSceneMotionControllerOperations.Default);

  public static RideTrainSceneMotionController Build(
    IReadOnlyList<RideInstanceTrainRuntimeEntry> trains,
    IReadOnlyList<RideCarInstanceRuntimeEntry> cars,
    RideCarStaticSceneBuildResult bodyScene,
    RideCarVisualHierarchySceneBuildResult? hierarchyScene = null
  ) {
    ArgumentNullException.ThrowIfNull(cars);
    return Build(
      trains,
      cars,
      bodyScene,
      hierarchyScene,
      RideTrainSceneMotionControllerLimits.Default,
      RideTrainSceneMotionControllerOperations.Default);
  }

  internal static RideTrainSceneMotionController Build(
    IReadOnlyList<RideInstanceTrainRuntimeEntry> trains,
    RideCarStaticSceneBuildResult bodyScene,
    RideCarVisualHierarchySceneBuildResult? hierarchyScene,
    RideTrainSceneMotionControllerLimits limits,
    RideTrainSceneMotionControllerOperations operations
  ) => Build(trains, null, bodyScene, hierarchyScene, limits, operations);

  internal static RideTrainSceneMotionController Build(
    IReadOnlyList<RideInstanceTrainRuntimeEntry> trains,
    IReadOnlyList<RideCarInstanceRuntimeEntry>? runtimeCarEntries,
    RideCarStaticSceneBuildResult bodyScene,
    RideCarVisualHierarchySceneBuildResult? hierarchyScene,
    RideTrainSceneMotionControllerLimits limits,
    RideTrainSceneMotionControllerOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(trains);
    ArgumentNullException.ThrowIfNull(bodyScene);
    ArgumentNullException.ThrowIfNull(operations);
    ValidateLimits(limits);
    ValidateOperations(operations);
    ValidateCount(trains.Count, limits.MaximumTrainCount, "train");

    var sceneCars = SnapshotSceneCars(bodyScene, limits);
    var runtimeCars = SnapshotRuntimeCars(runtimeCarEntries, sceneCars, limits);
    var baselineTargets = sceneCars.Values
      .OrderBy(car => car.Entry.RegistryIndex)
      .Select(car => new RideCarSceneTransformTarget(
        car.Entry.RegistryIndex,
        car.Entry.CarInstanceEntryId,
        car.Transform))
      .ToArray();
    var allTargetPositions = baselineTargets
      .Select((target, index) => (target.RegistryIndex, index))
      .ToDictionary(pair => pair.RegistryIndex, pair => pair.index);
    var initialTargets = baselineTargets.ToArray();
    var initializedCars = new HashSet<int>();
    var renderedCarsByTrain = IndexSceneCarsByTrain(sceneCars.Values, limits);
    var runtimeCarsByTrain = IndexRuntimeCarsByTrain(runtimeCars.Values, limits);
    var trainSet = new HashSet<RideInstanceTrainRuntimeEntry>(
      ReferenceEqualityComparer.Instance);
    var entries = new RideTrainSceneMotionEntry[trains.Count];

    foreach (var index in Enumerable.Range(0, trains.Count)) {
      var train = trains[index]
        ?? throw Invalid($"train list contains null at index {index}");
      if (train.SavedTrainIndex != index || !trainSet.Add(train))
        throw Invalid($"train {index} changed or duplicated exact saved-train order");

      var authorization = operations.Authorize(train)
        ?? throw Invalid($"train {index} authorization returned null");
      if (!ReferenceEquals(authorization.TrainRuntime, train))
        throw Invalid($"train {index} authorization changed exact runtime identity");
      var renderedCars = renderedCarsByTrain.TryGetValue(train, out var trainCars)
        ? trainCars
        : Array.Empty<RideCarStaticInstanceEntry>();
      var exactRuntimeCars = runtimeCarsByTrain.TryGetValue(train, out var trainRuntimeCars)
        ? trainRuntimeCars
        : Array.Empty<RideCarInstanceRuntimeEntry>();
      var circuitAuthorization = operations.AuthorizeCircuit(
        train,
        train.TrackRuntime,
        exactRuntimeCars,
        renderedCars)
        ?? throw Invalid($"train {index} circuit authorization returned null");
      if (train.SavedMotionState is not { } motionState) {
        entries[index] = Skipped(
          train,
          authorization,
          circuitAuthorization,
          RideTrainSceneMotionStatus.MissingSavedMotionState);
        continue;
      }
      if (!authorization.IsAuthorized) {
        entries[index] = Skipped(
          train,
          authorization,
          circuitAuthorization,
          RideTrainSceneMotionStatus.MotionNotAuthorized,
          authorization.Status.ToString());
        continue;
      }
      if (!circuitAuthorization.IsAuthorized ||
          circuitAuthorization.Traversal is not { } traversal ||
          !OwnsExactTraversal(train.TrackRuntime, traversal)) {
        entries[index] = Skipped(
          train,
          authorization,
          circuitAuthorization,
          RideTrainSceneMotionStatus.CircuitNotAuthorized,
          circuitAuthorization.Status.ToString());
        continue;
      }
      if (!TryBuildConsist(
          train,
          traversal,
          runtimeCarsByTrain,
          renderedCarsByTrain,
          limits,
          out var cars,
          out var detail)) {
        entries[index] = Skipped(
          train,
          authorization,
          circuitAuthorization,
          RideTrainSceneMotionStatus.IncompleteRenderedConsist,
          detail);
        continue;
      }

      try {
        var initialPlan = operations.Plan(traversal, motionState, cars);
        ValidatePlan(train, traversal, motionState, cars, initialPlan);
        foreach (var target in initialPlan.Targets) {
          if (!allTargetPositions.TryGetValue(target.RegistryIndex, out var position) ||
              !initializedCars.Add(target.RegistryIndex))
            throw Invalid(
              $"initial target car {target.RegistryIndex} is missing or duplicated");
          initialTargets[position] = target;
        }
        entries[index] = new(
          train,
          authorization,
          circuitAuthorization,
          RideTrainSceneMotionStatus.Animated,
          traversal,
          cars,
          motionState);
      }
      catch (Exception error) when (
        error is InvalidDataException or ArgumentException or InvalidOperationException) {
        entries[index] = Skipped(
          train,
          authorization,
          circuitAuthorization,
          RideTrainSceneMotionStatus.UnavailableInitialPose,
          error.Message);
      }
    }

    foreach (var group in renderedCarsByTrain)
      if (!trainSet.Contains(group.Key))
        throw Invalid("rendered saved car belongs to a foreign train runtime");
    foreach (var group in runtimeCarsByTrain)
      if (!trainSet.Contains(group.Key))
        throw Invalid("saved car belongs to a foreign train runtime");

    var animatedCarCount = entries
      .Where(entry => entry.IsAnimated)
      .Sum(entry => entry.Cars.Count(car => car.IsRendered));
    if (initializedCars.Count != animatedCarCount)
      throw Invalid("initial target count changed from the authorized consists");
    var motionTargets = initialTargets
      .Where(target => initializedCars.Contains(target.RegistryIndex))
      .ToArray();
    var motionTargetPositions = motionTargets
      .Select((target, index) => (target.RegistryIndex, index))
      .ToDictionary(pair => pair.RegistryIndex, pair => pair.index);
    var motionSceneCars = sceneCars
      .Where(pair => initializedCars.Contains(pair.Key))
      .ToDictionary(pair => pair.Key, pair => pair.Value);
    var motionBodyScene = animatedCarCount == 0
      ? bodyScene
      : FilterBodyScene(bodyScene, initializedCars);
    var motionHierarchyScene = animatedCarCount == 0
      ? null
      : FilterHierarchyScene(hierarchyScene, motionSceneCars);
    if (animatedCarCount > 0)
      operations.Apply(motionBodyScene, motionTargets, motionHierarchyScene);

    return new(
      motionBodyScene,
      motionHierarchyScene,
      entries,
      motionTargets,
      motionTargetPositions,
      operations);
  }

  /// <summary>Runs one frame without allowing a malformed later pose to escape the game loop.</summary>
  public bool TryUpdate(
    TimeSpan delta,
    out RideTrainSceneMotionUpdateResult result,
    out Exception? error
  ) {
    try {
      result = Update(delta);
      error = null;
      return true;
    }
    catch (Exception caught) when (IsRecoverableMotionFailure(caught)) {
      result = default;
      error = caught;
      return false;
    }
  }

  public RideTrainSceneMotionUpdateResult Update(TimeSpan delta) {
    var elapsedSeconds = delta.TotalSeconds;
    if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0d)
      throw new ArgumentOutOfRangeException(
        nameof(delta),
        "Ride-train scene-motion time step must be finite and nonnegative.");
    if (elapsedSeconds == 0d || AnimatedTrainCount == 0)
      return new(AnimatedTrainCount, AnimatedCarCount, 0, 0);

    var elapsed = Convert.ToSingle(elapsedSeconds);
    if (!float.IsFinite(elapsed))
      throw new ArgumentOutOfRangeException(
        nameof(delta),
        "Ride-train scene-motion time step exceeds finite float range.");

    var targets = baselineTargets.ToArray();
    var pendingStates = new Dictionary<
      RideTrainSceneMotionEntry,
      RideTrainMotionState>(ReferenceEqualityComparer.Instance);
    var updatedCars = new HashSet<int>();
    foreach (var entry in Entries) {
      if (!entry.IsAnimated) continue;
      if (entry.Traversal == null || entry.MotionState is not { } currentState)
        throw Invalid("animated train lost its circuit traversal or motion state");

      var nextState = operations.Advance(
        currentState,
        elapsed,
        entry.Traversal.Circuit.Length);
      var plan = operations.Plan(entry.Traversal, nextState, entry.Cars);
      ValidatePlan(
        entry.TrainRuntime,
        entry.Traversal,
        nextState,
        entry.Cars,
        plan);
      foreach (var target in plan.Targets) {
        if (!targetPositions.TryGetValue(target.RegistryIndex, out var position) ||
            !updatedCars.Add(target.RegistryIndex))
          throw Invalid(
            $"animated target car {target.RegistryIndex} is missing or duplicated");
        targets[position] = target;
      }
      pendingStates.Add(entry, nextState);
    }

    if (updatedCars.Count != AnimatedCarCount)
      throw Invalid("animated target count changed from the authorized consists");
    var applied = operations.Apply(bodyScene, targets, hierarchyScene);
    foreach (var pending in pendingStates) pending.Key.MotionState = pending.Value;
    return new(
      AnimatedTrainCount,
      updatedCars.Count,
      applied.Body.UpdatedModelCount,
      applied.Hierarchy?.UpdatedModelCount ?? 0);
  }

  private static IReadOnlyDictionary<int, SceneCar> SnapshotSceneCars(
    RideCarStaticSceneBuildResult scene,
    RideTrainSceneMotionControllerLimits limits
  ) {
    if (scene.Models == null || scene.ModelBindings == null ||
        scene.Models.Count != scene.ModelBindings.Count ||
        scene.ModelCount != scene.Models.Count)
      throw Invalid("body scene model and binding counts are inconsistent");

    var result = new Dictionary<int, SceneCar>();
    foreach (var index in Enumerable.Range(0, scene.ModelBindings.Count)) {
      var model = scene.Models[index]
        ?? throw Invalid($"body scene model {index} is null");
      var binding = scene.ModelBindings[index]
        ?? throw Invalid($"body scene binding {index} is null");
      var entry = binding.Entry
        ?? throw Invalid($"body scene binding {index} has no saved-car entry");
      if (!ReferenceEquals(binding.Model, model) ||
          binding.RegistryIndex != entry.RegistryIndex ||
          entry.CarRuntime == null || entry.CarRuntime.RegistryIndex != entry.RegistryIndex ||
          entry.CarInstanceEntryId == 0 || !entry.IsResolved ||
          !TrackMath.IsFinite(model.Transform.Matrix))
        throw Invalid($"body scene binding {index} changed exact car or transform identity");

      if (result.TryGetValue(entry.RegistryIndex, out var prior)) {
        if (!ReferenceEquals(prior.Entry, entry) ||
            !ReferenceEquals(prior.VariantEntry, binding.VariantEntry) ||
            prior.Entry.CarInstanceEntryId != entry.CarInstanceEntryId ||
            prior.Transform != model.Transform.Matrix)
          throw Invalid($"body scene car {entry.RegistryIndex} changed between material batches");
      } else {
        ValidateCount(result.Count + 1, limits.MaximumCarCount, "rendered car");
        result.Add(entry.RegistryIndex, new(
          entry,
          binding.VariantEntry,
          model.Transform.Matrix));
      }
    }
    if (result.Count != scene.BuiltCarCount)
      throw Invalid("body scene built-car count changed from its exact bindings");
    return result;
  }

  private static IReadOnlyDictionary<int, RideCarInstanceRuntimeEntry> SnapshotRuntimeCars(
    IReadOnlyList<RideCarInstanceRuntimeEntry>? cars,
    IReadOnlyDictionary<int, SceneCar> sceneCars,
    RideTrainSceneMotionControllerLimits limits
  ) {
    var source = cars ?? sceneCars.Values
      .Select(sceneCar => sceneCar.Entry.CarRuntime)
      .OrderBy(car => car.RegistryIndex)
      .ToArray();
    ValidateCount(source.Count, limits.MaximumCarCount, "saved car");

    var result = new Dictionary<int, RideCarInstanceRuntimeEntry>();
    foreach (var index in Enumerable.Range(0, source.Count)) {
      var car = source[index]
        ?? throw Invalid($"saved car list contains null at index {index}");
      if (car.RegistryIndex < 0 || car.TrainRuntime == null || car.CarInstance == null ||
          car.CarInstanceEntryId == 0 || !result.TryAdd(car.RegistryIndex, car))
        throw Invalid($"saved car {index} changed or duplicated exact runtime identity");
    }

    foreach (var sceneCar in sceneCars.Values)
      if (!result.TryGetValue(sceneCar.Entry.RegistryIndex, out var runtime) ||
          !ReferenceEquals(runtime, sceneCar.Entry.CarRuntime))
        throw Invalid(
          $"rendered car {sceneCar.Entry.RegistryIndex} changed exact runtime identity");
    return result;
  }

  private static IReadOnlyDictionary<
    RideInstanceTrainRuntimeEntry,
    IReadOnlyList<RideCarStaticInstanceEntry>> IndexSceneCarsByTrain(
      IEnumerable<SceneCar> sceneCars,
      RideTrainSceneMotionControllerLimits limits
    ) {
    var mutable = new Dictionary<
      RideInstanceTrainRuntimeEntry,
      List<RideCarStaticInstanceEntry>>(ReferenceEqualityComparer.Instance);
    var count = 0;
    foreach (var sceneCar in sceneCars) {
      var entry = sceneCar.Entry;
      var train = entry.CarRuntime.TrainRuntime
        ?? throw Invalid($"rendered car {entry.RegistryIndex} has no train runtime");
      if (!mutable.TryGetValue(train, out var cars)) {
        cars = [];
        mutable.Add(train, cars);
      }
      cars.Add(entry);
      count++;
      ValidateCount(count, limits.MaximumCarCount, "rendered car");
    }
    var result = new Dictionary<
      RideInstanceTrainRuntimeEntry,
      IReadOnlyList<RideCarStaticInstanceEntry>>(ReferenceEqualityComparer.Instance);
    foreach (var pair in mutable)
      result.Add(pair.Key, Array.AsReadOnly(pair.Value.ToArray()));
    return result;
  }

  private static IReadOnlyDictionary<
    RideInstanceTrainRuntimeEntry,
    IReadOnlyList<RideCarInstanceRuntimeEntry>> IndexRuntimeCarsByTrain(
      IEnumerable<RideCarInstanceRuntimeEntry> runtimeCars,
      RideTrainSceneMotionControllerLimits limits
    ) {
    var mutable = new Dictionary<
      RideInstanceTrainRuntimeEntry,
      List<RideCarInstanceRuntimeEntry>>(ReferenceEqualityComparer.Instance);
    var count = 0;
    foreach (var entry in runtimeCars) {
      var train = entry.TrainRuntime
        ?? throw Invalid($"saved car {entry.RegistryIndex} has no train runtime");
      if (!mutable.TryGetValue(train, out var cars)) {
        cars = [];
        mutable.Add(train, cars);
      }
      cars.Add(entry);
      count++;
      ValidateCount(count, limits.MaximumCarCount, "saved car");
    }
    var result = new Dictionary<
      RideInstanceTrainRuntimeEntry,
      IReadOnlyList<RideCarInstanceRuntimeEntry>>(ReferenceEqualityComparer.Instance);
    foreach (var pair in mutable)
      result.Add(pair.Key, Array.AsReadOnly(pair.Value.ToArray()));
    return result;
  }

  private static RideCarVisualHierarchySceneBuildResult? FilterHierarchyScene(
    RideCarVisualHierarchySceneBuildResult? scene,
    IReadOnlyDictionary<int, SceneCar> bodyCars
  ) {
    if (scene == null) return null;
    if (scene.Models == null || scene.ModelBindings == null ||
        scene.Models.Count != scene.ModelBindings.Count ||
        scene.ModelCount != scene.Models.Count || scene.SourcePartCount < 0)
      throw Invalid("hierarchy scene model and binding counts are inconsistent");

    var models = new List<OpenCobra.GDK.Model>();
    var bindings = new List<RideCarVisualHierarchySceneModelBinding>();
    var parts = new HashSet<int>();
    foreach (var index in Enumerable.Range(0, scene.ModelBindings.Count)) {
      var model = scene.Models[index]
        ?? throw Invalid($"hierarchy scene model {index} is null");
      var binding = scene.ModelBindings[index]
        ?? throw Invalid($"hierarchy scene binding {index} is null");
      var instance = binding.Instance
        ?? throw Invalid($"hierarchy scene binding {index} has no part instance");
      var savedCar = instance.SavedCar
        ?? throw Invalid($"hierarchy scene binding {index} has no saved car");
      if (!ReferenceEquals(binding.Model, model))
        throw Invalid($"hierarchy scene binding {index} changed exact model identity");
      if (!bodyCars.TryGetValue(savedCar.RegistryIndex, out var bodyCar)) continue;
      if (savedCar.CarInstanceEntryId != bodyCar.Entry.CarInstanceEntryId ||
          !ReferenceEquals(savedCar, bodyCar.VariantEntry))
        throw Invalid(
          $"hierarchy car {savedCar.RegistryIndex} changed exact selected-car identity");
      models.Add(model);
      bindings.Add(binding);
      parts.Add(instance.RegistryIndex);
    }

    if (bindings.Count == 0) return null;
    if (parts.Count > scene.SourcePartCount)
      throw Invalid("filtered hierarchy part count exceeds its source count");
    return scene with {
      Models = Array.AsReadOnly(models.ToArray()),
      ModelBindings = Array.AsReadOnly(bindings.ToArray()),
      BuiltPartCount = parts.Count,
      SkippedPartCount = scene.SourcePartCount - parts.Count,
      ModelCount = models.Count,
    };
  }

  private static RideCarStaticSceneBuildResult FilterBodyScene(
    RideCarStaticSceneBuildResult scene,
    IReadOnlySet<int> includedCars
  ) {
    var models = new List<OpenCobra.GDK.Model>();
    var bindings = new List<RideCarStaticSceneModelBinding>();
    var cars = new HashSet<int>();
    foreach (var index in Enumerable.Range(0, scene.ModelBindings.Count)) {
      var binding = scene.ModelBindings[index];
      if (!includedCars.Contains(binding.RegistryIndex)) continue;
      models.Add(scene.Models[index]);
      bindings.Add(binding);
      cars.Add(binding.RegistryIndex);
    }
    if (!cars.SetEquals(includedCars))
      throw Invalid("filtered body scene does not cover every animated car");
    return scene with {
      Models = Array.AsReadOnly(models.ToArray()),
      ModelBindings = Array.AsReadOnly(bindings.ToArray()),
      BuiltCarCount = cars.Count,
      SkippedCarCount = scene.SourceCarCount - cars.Count,
      ModelCount = models.Count,
    };
  }

  private static bool TryBuildConsist(
    RideInstanceTrainRuntimeEntry train,
    TrackCircuitTraversal traversal,
    IReadOnlyDictionary<
      RideInstanceTrainRuntimeEntry,
      IReadOnlyList<RideCarInstanceRuntimeEntry>> runtimeCarsByTrain,
    IReadOnlyDictionary<
      RideInstanceTrainRuntimeEntry,
      IReadOnlyList<RideCarStaticInstanceEntry>> renderedCarsByTrain,
    RideTrainSceneMotionControllerLimits limits,
    out IReadOnlyList<RideTrainOrdinaryScenePoseCarInput> cars,
    out string? detail
  ) {
    var savedCars = train.TrainResource.TrainInstance.Cars;
    ValidateCount(savedCars.Count, limits.MaximumCarCount, "saved consist car");
    if (!runtimeCarsByTrain.TryGetValue(train, out var runtimeCars) ||
        runtimeCars.Count != savedCars.Count || savedCars.Count == 0) {
      cars = Array.Empty<RideTrainOrdinaryScenePoseCarInput>();
      detail = $"retained {runtimeCars?.Count ?? 0} of {savedCars.Count} saved cars";
      return false;
    }

    var orderedRuntime = new RideCarInstanceRuntimeEntry[savedCars.Count];
    var occupiedRuntime = new bool[savedCars.Count];
    foreach (var runtime in runtimeCars) {
      var ordinal = runtime.WhichCar;
      if (!ReferenceEquals(runtime.TrainRuntime, train) || ordinal < 0 ||
          ordinal >= savedCars.Count || occupiedRuntime[ordinal] ||
          savedCars[ordinal] != runtime.CarInstanceEntryId ||
          runtime.CarInstance.RideTrainInstance != train.TrainInstanceEntryId) {
        cars = Array.Empty<RideTrainOrdinaryScenePoseCarInput>();
        detail = $"saved car {runtime.RegistryIndex} changed exact consist identity";
        return false;
      }
      occupiedRuntime[ordinal] = true;
      orderedRuntime[ordinal] = runtime;
    }
    if (occupiedRuntime.Any(value => !value)) {
      cars = Array.Empty<RideTrainOrdinaryScenePoseCarInput>();
      detail = "retained consist has a missing saved-car ordinal";
      return false;
    }

    var rendered = renderedCarsByTrain.TryGetValue(train, out var trainCars)
      ? trainCars
      : Array.Empty<RideCarStaticInstanceEntry>();
    var orderedRendered = new RideCarStaticInstanceEntry?[savedCars.Count];
    var ordered = new RideTrainOrdinaryScenePoseCarInput[savedCars.Count];
    foreach (var entry in rendered) {
      var runtime = entry.CarRuntime;
      var ordinal = runtime.WhichCar;
      if (!ReferenceEquals(runtime.TrainRuntime, train) || ordinal < 0 ||
          ordinal >= savedCars.Count || orderedRendered[ordinal] != null ||
          !ReferenceEquals(orderedRuntime[ordinal], runtime) ||
          savedCars[ordinal] != entry.CarInstanceEntryId) {
        cars = Array.Empty<RideTrainOrdinaryScenePoseCarInput>();
        detail = $"rendered car {entry.RegistryIndex} changed exact consist identity";
        return false;
      }
      orderedRendered[ordinal] = entry;
    }

    foreach (var ordinal in Enumerable.Range(0, savedCars.Count)) {
      var runtime = orderedRuntime[ordinal];
      var renderedEntry = orderedRendered[ordinal];
      if (runtime.SavedRole == RideTrainCarRole.Link) {
        if (ordinal == 0 || ordinal == savedCars.Count - 1 ||
            !RideTrainSpacingOnlyLinkEvidence.IsExact(runtime) || renderedEntry != null ||
            !runtime.HasSavedPhysicalState || !float.IsFinite(runtime.SavedLength) ||
            runtime.SavedLength < 0f) {
          cars = Array.Empty<RideTrainOrdinaryScenePoseCarInput>();
          detail = $"saved Link car {runtime.RegistryIndex} lacks exact spacing-only evidence";
          return false;
        }
        ordered[ordinal] = new(
          runtime,
          StaticEntry: null,
          Geometry: null,
          Length: runtime.SavedLength,
          HasRearGeometry: false);
        continue;
      }

      if (renderedEntry?.Geometry is not { } geometry ||
          renderedEntry.SavedCursor?.IsResolved != true ||
          renderedEntry.SavedCursor.RegistryIndex != runtime.RegistryIndex ||
          !ReferenceEquals(renderedEntry.SavedCursor.CarRuntime, runtime) ||
          !ContactUsesTraversal(renderedEntry.SavedCursor.Front, traversal) ||
          !ContactUsesTraversal(renderedEntry.SavedCursor.Rear, traversal)) {
        cars = Array.Empty<RideTrainOrdinaryScenePoseCarInput>();
        detail = $"body car {runtime.RegistryIndex} lacks rendered geometry or saved contacts";
        return false;
      }
      ordered[ordinal] = new(
        runtime,
        renderedEntry,
        geometry,
        HasRearGeometry: true);
    }

    cars = Array.AsReadOnly(ordered);
    detail = null;
    return true;
  }

  private static bool OwnsExactTraversal(
    RideInstanceTrackRuntimeEntry trackRuntime,
    TrackCircuitTraversal traversal
  ) => ReferenceEquals(trackRuntime.CircuitTraversal, traversal) ||
    (trackRuntime.SegmentCircuitTraversals?.Any(segment =>
      segment is not null && ReferenceEquals(segment.Traversal, traversal) &&
      ReferenceEquals(segment.Traversal.Circuit, segment.Circuit)) ?? false);

  private static bool ContactUsesTraversal(
    RideCarSavedWheelContactCursor? contact,
    TrackCircuitTraversal traversal
  ) => contact?.IsResolved == true && contact.Cursor is { } cursor &&
    ReferenceEquals(cursor.Traversal, traversal);

  private static void ValidatePlan(
    RideInstanceTrainRuntimeEntry train,
    TrackCircuitTraversal traversal,
    RideTrainMotionState motionState,
    IReadOnlyList<RideTrainOrdinaryScenePoseCarInput> cars,
    RideTrainOrdinaryScenePosePlan? plan
  ) {
    var renderedCars = cars.Where(car => car.IsRendered).ToArray();
    if (plan == null || !ReferenceEquals(plan.Traversal, traversal) ||
        plan.MotionState != motionState || plan.Targets == null ||
        plan.Targets.Count != renderedCars.Length)
      throw Invalid(
        $"train {train.TrainInstanceEntryId} pose plan changed its traversal, state, or count");
    foreach (var index in Enumerable.Range(0, renderedCars.Length)) {
      var car = renderedCars[index];
      var target = plan.Targets[index];
      if (target.RegistryIndex != car.StaticEntry!.RegistryIndex ||
          target.CarInstanceEntryId != car.StaticEntry.CarInstanceEntryId ||
          !TrackMath.IsFinite(target.Transform))
        throw Invalid(
          $"train {train.TrainInstanceEntryId} target {index} changed exact car identity");
    }
  }

  private static RideTrainSceneMotionEntry Skipped(
    RideInstanceTrainRuntimeEntry train,
    RideTrainMotionAdvanceAuthorizationResult authorization,
    RideTrainCircuitMotionAuthorizationResult circuitAuthorization,
    RideTrainSceneMotionStatus status,
    string? detail = null
  ) => new(
    train,
    authorization,
    circuitAuthorization,
    status,
    null,
    null,
    null,
    detail);

  private static void ValidateOperations(
    RideTrainSceneMotionControllerOperations operations
  ) {
    if (operations.Authorize == null || operations.AuthorizeCircuit == null ||
        operations.Advance == null || operations.Plan == null || operations.Apply == null)
      throw new ArgumentException(
        "Ride-train scene-motion operations must be complete.",
        nameof(operations));
  }

  private static void ValidateLimits(RideTrainSceneMotionControllerLimits limits) {
    if (limits.MaximumTrainCount < 0 || limits.MaximumCarCount < 0)
      throw new ArgumentOutOfRangeException(nameof(limits));
  }

  private static void ValidateCount(int count, int maximum, string description) {
    if (count > maximum)
      throw new InvalidOperationException(
        $"Ride-train scene-motion {description} count exceeds the limit {maximum}.");
  }

  private static bool IsRecoverableMotionFailure(Exception error) =>
    error is InvalidDataException or ArgumentException or InvalidOperationException or
      OverflowException;

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-train scene-motion input is invalid: {message}.");

  private sealed record SceneCar(
    RideCarStaticInstanceEntry Entry,
    RideCarVariantStaticInstanceEntry? VariantEntry,
    System.Numerics.Matrix4x4 Transform
  );
}
