// DAT Scenery Animation Info
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>One raw element from a scenery item's <c>AnimInfoList</c>.</summary>
internal readonly record struct DatSceneryAnimationInfo(
  bool AutoLoop,
  int CurrentAnimation,
  float CurrentAnimationTime,
  bool MarkedForDeletion);
