// Ride Car Visual Scene Transform Transaction
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;

namespace OpenRCT3.Simulation;

/// <summary>Counts from one complete body and optional hierarchy transform transaction.</summary>
internal readonly record struct RideCarVisualSceneTransformTransactionResult(
  RideCarSceneTransformUpdateResult Body,
  RideCarVisualHierarchySceneTransformUpdateResult? Hierarchy
);

/// <summary>Atomically applies one target set across body and hierarchy scene layers.</summary>
/// <remarks>
/// Both layers are completely preflighted before either prepared update is applied. A malformed
/// hierarchy therefore cannot leave already-valid body models at a newer pose. The transaction
/// owns or disposes no scene, model, transform, mesh, or material resource.
/// </remarks>
internal static class RideCarVisualSceneTransformTransaction {
  public static RideCarVisualSceneTransformTransactionResult Update(
    RideCarStaticSceneBuildResult bodyScene,
    IReadOnlyList<RideCarSceneTransformTarget> bodyTargets,
    RideCarVisualHierarchySceneBuildResult? hierarchyScene = null
  ) {
    var body = RideCarSceneTransformUpdater.Prepare(bodyScene, bodyTargets);
    var hierarchy = hierarchyScene == null
      ? null
      : RideCarVisualHierarchySceneTransformUpdater.Prepare(hierarchyScene, bodyTargets);
    if (hierarchy != null) ValidateDistinctAssignments(body, hierarchy);

    var bodyResult = body.Apply();
    RideCarVisualHierarchySceneTransformUpdateResult? hierarchyResult = hierarchy?.Apply();
    return new(bodyResult, hierarchyResult);
  }

  private static void ValidateDistinctAssignments(
    RideCarSceneTransformUpdater.PreparedUpdate body,
    RideCarVisualHierarchySceneTransformUpdater.PreparedUpdate hierarchy
  ) {
    var bodyModels = new HashSet<OpenCobra.GDK.Model>(ReferenceEqualityComparer.Instance);
    var bodyTransforms = new HashSet<OpenCobra.GDK.Transform>(
      ReferenceEqualityComparer.Instance);
    foreach (var assignment in body.Assignments) {
      bodyModels.Add(assignment.Model);
      bodyTransforms.Add(assignment.Transform);
    }

    foreach (var assignment in hierarchy.Assignments) {
      if (bodyModels.Contains(assignment.Model))
        throw Invalid("body and hierarchy assignments reuse a model identity");
      if (bodyTransforms.Contains(assignment.Transform))
        throw Invalid("body and hierarchy assignments reuse a mutable transform identity");
    }
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-car visual transform transaction is invalid: {message}.");
}
