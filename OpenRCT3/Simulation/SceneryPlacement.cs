// Scenery Placement
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
namespace OpenRCT3.Simulation;

/// <summary>
/// A single placed scenery instance: object reference, anchor position, elevation, and orientation. Stored
/// directly on <see cref="Park.SceneryPlacements"/> — no wrapper "layer" type.
/// </summary>
/// <remarks>
/// Loaded RCT3 DAT entries retain both height forms: absolute objects use
/// <see cref="SerializedHeight"/>, while terrain-relative objects use <see cref="HeightOffset"/>.
/// <see cref="HeightAdjust"/> supplies the final fractional world-space adjustment for either form.
/// </remarks>
public struct SceneryPlacement {
  /// <summary>The raw OVL <c>sid</c>/<c>svd</c> symbol name identifying the placed object.</summary>
  public string ObjectKey;

  /// <summary>The anchor tile's X index in the OOB-inclusive grid.</summary>
  public int TileX;

  /// <summary>The anchor tile's Y index in the OOB-inclusive grid.</summary>
  public int TileY;

  /// <summary>
  /// The object's quarter-turn orientation (0/90/180/270, per RCT series convention), reusing
  /// <see cref="Simulation.Edge"/> as the four-state rotation value.
  /// </summary>
  /// <remarks>
  /// For edge-mounted <see cref="Simulation.Placement"/> values (<c>PathEdgeInner</c>/
  /// <c>PathEdgeOuter</c>/<c>PathEdgeJoin</c>/<c>Wall</c>), this directly names the tile edge the
  /// object sits on. For <c>FullTile</c> multi-tile footprints, it's a plain 4-state orientation: the
  /// West/East pair leaves <see cref="SceneryDefinition.FootprintWidth"/>/
  /// <see cref="SceneryDefinition.FootprintHeight"/> as-is, the North/South pair swaps them — this holds
  /// regardless of rotation winding direction, since swap-or-not only depends on which axis pair the
  /// object's local "forward" now aligns with.
  /// </remarks>
  public Edge Rotation;

  /// <summary>The source DAT entry ID, or zero for a newly placed gameplay object.</summary>
  public ulong SourceEntryId;

  /// <summary>The source DAT database-entry ID used to resolve the object's SID resource.</summary>
  public ulong DatabaseEntryReference;

  /// <summary>The DAT catalog/dependency OVL base path used to begin resource resolution.</summary>
  public string? OverlayPath;

  /// <summary>The serialized corner selector used by sub-tile placement rules.</summary>
  public int Corner;

  /// <summary>
  /// The raw RCT3 four-way direction ordinal retained alongside <see cref="Rotation"/>.
  /// </summary>
  public int SerializedDirection;

  /// <summary>The DAT data-field HEIGHT used by effective-absolute placements, in world units.</summary>
  public int? SerializedHeight;

  /// <summary>The DAT outer HEIGHTOFFSET added to sampled terrain for relative placements.</summary>
  public int HeightOffset;

  /// <summary>The Soaked/Wild fractional world-space adjustment applied after either height path.</summary>
  public float HeightAdjust;

  /// <summary>Whether the DAT record explicitly forces absolute-height interpretation.</summary>
  public bool ForceAbsoluteHeight;

  /// <summary>Whether the source DAT record marks this instance as hidden.</summary>
  public bool IsHidden;

  /// <summary>The source object's owner reference, which may remain unresolved.</summary>
  public ulong OwnerReference;

  /// <summary>The first serialized flexi-colour palette index.</summary>
  public int FlexiColour0;

  /// <summary>The second serialized flexi-colour palette index.</summary>
  public int FlexiColour1;

  /// <summary>The third serialized flexi-colour palette index.</summary>
  public int FlexiColour2;

  /// <summary>The source object's serialized animation-frame offset.</summary>
  public int FrameOffset;

  public SceneryPlacement(
    string objectKey,
    int tileX,
    int tileY,
    Edge rotation = Edge.South,
    int? serializedHeight = null
  ) {
    ObjectKey = objectKey;
    TileX = tileX;
    TileY = tileY;
    Rotation = rotation;
    SourceEntryId = 0;
    DatabaseEntryReference = 0;
    OverlayPath = null;
    Corner = 0;
    SerializedDirection = rotation switch {
      Edge.West => 0,
      Edge.North => 1,
      Edge.East => 2,
      Edge.South => 3,
      _ => throw new ArgumentOutOfRangeException(nameof(rotation)),
    };
    SerializedHeight = serializedHeight;
    HeightOffset = 0;
    HeightAdjust = 0f;
    ForceAbsoluteHeight = false;
    IsHidden = false;
    OwnerReference = 0;
    FlexiColour0 = 0;
    FlexiColour1 = 0;
    FlexiColour2 = 0;
    FrameOffset = 0;
  }
}
