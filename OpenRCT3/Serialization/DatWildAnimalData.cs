// DAT Wild Animal Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Numerics;

namespace OpenRCT3.Serialization;

/// <summary>One exact saved <c>WASDatabaseEntry</c> resource identity.</summary>
internal sealed record DatWildAnimalSpeciesDatabaseEntryData(
  ulong EntryId,
  bool IsUnlocked,
  string OverlayFilename,
  string SymbolName
);

/// <summary>One persisted Wild-animal animation blend entry, without inferred WAD semantics.</summary>
internal readonly record struct DatWildAnimalAnimationData(
  float Time,
  int Type,
  float Weight
);

/// <summary>One exact saved <c>WildAnimalVisual</c> state and native RCT3 world matrix.</summary>
internal sealed record DatWildAnimalVisualData(
  ulong EntryId,
  IReadOnlyList<DatWildAnimalAnimationData> AnimationData,
  bool DoShadows,
  bool Visible,
  Matrix4x4 WorldMatrix
);

/// <summary>Rendering identity retained from one saved <c>WildAnimal</c> entry.</summary>
internal sealed record DatWildAnimalData(
  ulong EntryId,
  ulong SpeciesDatabaseEntryId,
  ulong VisualEntryId,
  bool IsAdult,
  bool IsMale,
  int Type
);

/// <summary>The current evidence status for mapping saved animal state to one WAS variant.</summary>
internal enum DatWildAnimalVariantSelectionStatus {
  Unsupported,
}

/// <summary>
/// One saved animal linked to its exact species database entry and visual state.
/// </summary>
/// <remarks>
/// The native matrix, adult/sex fields, and all four WAS variants remain separate evidence. This
/// record deliberately does not infer a variant index or convert the matrix into renderer space.
/// </remarks>
internal sealed record DatWildAnimalPlacementData(
  DatWildAnimalData Animal,
  DatWildAnimalSpeciesDatabaseEntryData Species,
  DatWildAnimalVisualData Visual,
  DatWildAnimalVariantSelectionStatus VariantSelectionStatus
);
