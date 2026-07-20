// Ride Car Visual Hierarchy Pose Planner
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One exact optional axle or wheel visual placed in world space.</summary>
internal sealed record RideCarVisualHierarchyPartPose(
  RideVisualRole Role,
  uint Type,
  RideCarVisualHierarchyPart Part,
  Matrix4x4 WorldTransform
);

/// <summary>Immutable resource-static hierarchy placement for one selected ride-car body.</summary>
internal sealed record RideCarVisualHierarchyPosePlan(
  RideCarVisualHierarchyResolution Hierarchy,
  Matrix4x4 BodyWorldTransform,
  IReadOnlyList<RideCarVisualHierarchyPartPose> Parts
);

/// <summary>Places exact resolved axle and wheel visuals under one selected body transform.</summary>
/// <remarks>
/// The pinned importer computes each child-local <c>Position1</c> as shape-absolute
/// <c>Position2 * inverse(parent Position2)</c> in
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/lib3DHelp/3DLoader.cpp#L575-L581">
/// <c>calculateBonePos1</c></see>. Its
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/lib3DHelp/matrix.cpp#L529-L534">
/// vector application</see> proves row-vector order, while
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerBSH.cpp#L149-L162">
/// <c>ManagerBSH</c></see> writes both arrays independently. OpenRCT3 converts every BSH vertex
/// from native <c>(X,Y,Z)</c> to park <c>(X,Z,Y)</c>, so the same basis swap is conjugated around
/// <c>Position2</c> before child-before-parent world composition.
///
/// This is resource-static placement only. Complete Edition's pinned <c>0x00A525BB</c> and
/// <c>0x00A52811</c> paths retain marker translations, but no full native animated axle/wheel
/// matrix path is yet proven. Serialized <see cref="RideCarVisualHierarchyPart.Type"/> values are
/// therefore retained unchanged and never interpreted as visibility, parity, or animation state.
/// </remarks>
internal static class RideCarVisualHierarchyPosePlanner {
  private static Matrix4x4 NativeToParkBasis { get; } = new(
    1f, 0f, 0f, 0f,
    0f, 0f, 1f, 0f,
    0f, 1f, 0f, 0f,
    0f, 0f, 0f, 1f);

  public static RideCarVisualHierarchyPosePlan Resolve(
    Matrix4x4 bodyWorldTransform,
    RideCarVisualHierarchyResolution hierarchy
  ) {
    ArgumentNullException.ThrowIfNull(hierarchy);
    if (!TrackMath.IsFinite(bodyWorldTransform))
      throw Invalid("selected body world transform is non-finite");
    ValidateBody(hierarchy);

    var poses = new List<RideCarVisualHierarchyPartPose>(6);
    var frontAxleWorld = ResolveAxle(
      hierarchy,
      hierarchy.FrontAxle,
      RideVisualRole.FrontAxle,
      bodyWorldTransform,
      poses);
    var rearAxleWorld = ResolveAxle(
      hierarchy,
      hierarchy.RearAxle,
      RideVisualRole.RearAxle,
      bodyWorldTransform,
      poses);
    ResolveWheel(
      hierarchy,
      hierarchy.FrontRightWheel,
      RideVisualRole.FrontRightWheel,
      frontAxleWorld,
      bodyWorldTransform,
      poses);
    ResolveWheel(
      hierarchy,
      hierarchy.FrontLeftWheel,
      RideVisualRole.FrontLeftWheel,
      frontAxleWorld,
      bodyWorldTransform,
      poses);
    ResolveWheel(
      hierarchy,
      hierarchy.BackRightWheel,
      RideVisualRole.BackRightWheel,
      rearAxleWorld,
      bodyWorldTransform,
      poses);
    ResolveWheel(
      hierarchy,
      hierarchy.BackLeftWheel,
      RideVisualRole.BackLeftWheel,
      rearAxleWorld,
      bodyWorldTransform,
      poses);

    return new(
      hierarchy,
      bodyWorldTransform,
      Array.AsReadOnly(poses.ToArray()));
  }

  private static Matrix4x4? ResolveAxle(
    RideCarVisualHierarchyResolution hierarchy,
    RideCarVisualHierarchyPart part,
    RideVisualRole role,
    Matrix4x4 bodyWorldTransform,
    ICollection<RideCarVisualHierarchyPartPose> poses
  ) {
    if (!ValidateDeclaredPart(hierarchy, part, role)) return null;
    var anchor = ValidateAnchor(
      hierarchy,
      part.Anchor!,
      hierarchy.BodyShapeVisual!,
      RideCarVisualHierarchyAnchorSource.Body,
      role);
    var world = Compose(anchor.Bone, bodyWorldTransform, role);
    poses.Add(new(role, part.Type, part, world));
    return world;
  }

  private static void ResolveWheel(
    RideCarVisualHierarchyResolution hierarchy,
    RideCarVisualHierarchyPart part,
    RideVisualRole role,
    Matrix4x4? axleWorldTransform,
    Matrix4x4 bodyWorldTransform,
    ICollection<RideCarVisualHierarchyPartPose> poses
  ) {
    if (!ValidateDeclaredPart(hierarchy, part, role)) return;

    RideCarVisualShapeLink owner;
    Matrix4x4 parent;
    RideCarVisualHierarchyAnchorSource expectedSource;
    switch (part.Anchor!.Source) {
      case RideCarVisualHierarchyAnchorSource.Body:
        owner = hierarchy.BodyShapeVisual!;
        parent = bodyWorldTransform;
        expectedSource = RideCarVisualHierarchyAnchorSource.Body;
        break;
      case RideCarVisualHierarchyAnchorSource.FrontAxle
        when role is RideVisualRole.FrontRightWheel or RideVisualRole.FrontLeftWheel:
        owner = ResolvedAxleOwner(hierarchy.FrontAxle, role);
        parent = axleWorldTransform ??
          throw Invalid($"{role} resolved against a missing front-axle world transform");
        expectedSource = RideCarVisualHierarchyAnchorSource.FrontAxle;
        break;
      case RideCarVisualHierarchyAnchorSource.RearAxle
        when role is RideVisualRole.BackRightWheel or RideVisualRole.BackLeftWheel:
        owner = ResolvedAxleOwner(hierarchy.RearAxle, role);
        parent = axleWorldTransform ??
          throw Invalid($"{role} resolved against a missing rear-axle world transform");
        expectedSource = RideCarVisualHierarchyAnchorSource.RearAxle;
        break;
      default:
        throw Invalid($"{role} has a foreign body or axle anchor source");
    }

    var anchor = ValidateAnchor(hierarchy, part.Anchor, owner, expectedSource, role);
    var world = Compose(anchor.Bone, parent, role);
    poses.Add(new(role, part.Type, part, world));
  }

  private static RideCarVisualShapeLink ResolvedAxleOwner(
    RideCarVisualHierarchyPart axle,
    RideVisualRole wheelRole
  ) {
    if (!axle.IsResolved || axle.ShapeVisual == null)
      throw Invalid($"{wheelRole} resolved against an unavailable axle visual");
    return axle.ShapeVisual;
  }

  private static bool ValidateDeclaredPart(
    RideCarVisualHierarchyResolution hierarchy,
    RideCarVisualHierarchyPart? part,
    RideVisualRole expectedRole
  ) {
    if (part == null || part.Role != expectedRole)
      throw Invalid($"{expectedRole} changed deterministic role identity");
    if (part.Status == RideCarVisualHierarchyPartStatus.VisualNotDeclared) {
      if (part.SerializedVisualReference != null || part.Visual != null ||
          part.ShapeVisual != null || part.Anchor != null)
        throw Invalid($"undeclared {expectedRole} retains foreign visual or anchor evidence");
      return false;
    }
    if (!part.IsResolved || part.Status != RideCarVisualHierarchyPartStatus.Resolved ||
        part.Visual == null || part.ShapeVisual == null || part.Anchor == null)
      throw Invalid($"declared {expectedRole} hierarchy is unresolved with status {part.Status}");
    if (!string.Equals(
          part.SerializedVisualReference,
          part.Visual.Reference,
          StringComparison.Ordinal) ||
        part.Visual.Role != expectedRole)
      throw Invalid($"{expectedRole} changed its exact serialized visual identity");
    ValidateShapeVisual(
      hierarchy,
      part.ShapeVisual,
      part.Visual,
      expectedRole);
    return true;
  }

  private static RideCarVisualHierarchyAnchor ValidateAnchor(
    RideCarVisualHierarchyResolution hierarchy,
    RideCarVisualHierarchyAnchor anchor,
    RideCarVisualShapeLink expectedOwner,
    RideCarVisualHierarchyAnchorSource expectedSource,
    RideVisualRole partRole
  ) {
    if (anchor.Source != expectedSource || !ReferenceEquals(anchor.Visual, expectedOwner))
      throw Invalid($"{partRole} anchor has a foreign shape owner or source");
    ValidateShapeVisual(
      hierarchy,
      anchor.Visual,
      anchor.Visual.Visual,
      anchor.Visual.Visual.Role);

    var lods = anchor.Visual.Lods;
    var maximumLods = RideCarVisualHierarchyResolverLimits.Default.MaximumLodsPerVisual;
    if (lods == null || lods.Count > maximumLods ||
        !lods.Any(lod => ReferenceEquals(lod, anchor.Lod)) ||
        !ReferenceEquals(anchor.Lod.BoneShapeSource, anchor.ShapeSource))
      throw Invalid($"{partRole} anchor changed its exact BSH LOD identity");
    var bones = anchor.ShapeSource?.Resource?.Bones;
    var maximumBones = RideCarVisualHierarchyResolverLimits.Default.MaximumBonesPerShape;
    if (bones == null || bones.Count > maximumBones ||
        anchor.BoneIndex < 0 || anchor.BoneIndex >= bones.Count ||
        !ReferenceEquals(bones[anchor.BoneIndex], anchor.Bone))
      throw Invalid($"{partRole} anchor changed its exact BSH bone identity");
    if (!TrackMath.IsFinite(anchor.Bone.Position2))
      throw Invalid($"{partRole} anchor Position2 matrix is non-finite");
    return anchor;
  }

  private static void ValidateBody(RideCarVisualHierarchyResolution hierarchy) {
    if (hierarchy.Ride == null || hierarchy.Train == null || hierarchy.Car == null ||
        hierarchy.BodyVisual == null || hierarchy.BodyShapeVisual == null)
      throw Invalid("selected hierarchy has an incomplete body identity");
    if (hierarchy.BodyRole is not RideVisualRole.Body and
        not RideVisualRole.WildFlippedBody)
      throw Invalid($"unsupported selected body role {hierarchy.BodyRole}");
    if (hierarchy.BodyStatus != RideCarVisualHierarchyPartStatus.Resolved ||
        !hierarchy.BodyVisual.IsResolved || hierarchy.BodyVisual.Role != hierarchy.BodyRole)
      throw Invalid("selected body hierarchy is unresolved or changed role");
    ValidateShapeVisual(
      hierarchy,
      hierarchy.BodyShapeVisual,
      hierarchy.BodyVisual,
      hierarchy.BodyRole);
  }

  private static void ValidateShapeVisual(
    RideCarVisualHierarchyResolution hierarchy,
    RideCarVisualShapeLink visual,
    RideVisualLink expectedVisual,
    RideVisualRole expectedRole
  ) {
    if (!ReferenceEquals(visual.Ride, hierarchy.Ride) ||
        !ReferenceEquals(visual.Train, hierarchy.Train) ||
        !ReferenceEquals(visual.Car, hierarchy.Car) ||
        !ReferenceEquals(visual.Visual, expectedVisual) ||
        visual.Visual.Role != expectedRole || visual.Lods == null)
      throw Invalid($"{expectedRole} shape visual has a foreign keyed identity");
  }

  private static Matrix4x4 Compose(
    BoneShapeBone anchor,
    Matrix4x4 parentWorldTransform,
    RideVisualRole role
  ) {
    var local = NativeToParkBasis * anchor.Position2 * NativeToParkBasis;
    var world = local * parentWorldTransform;
    if (!TrackMath.IsFinite(local) || !TrackMath.IsFinite(world))
      throw Invalid($"{role} anchor composition produced a non-finite matrix");
    return world;
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Cannot plan ride-car visual hierarchy poses: {message}.");
}
