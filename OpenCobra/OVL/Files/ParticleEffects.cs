// ParticleEffects
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
//
// Decodes particle sprite textures from "psi" (FileType.ParticleSpriteItem) records - e.g. the
// FWFlares02_* firework/particle sprite symbols in Particles/Particles.ovl. A version-four/five psi
// is a 28-byte record whose exact owning SymbolRef points to its tex at +8. The psi is not itself a
// tex/flic/btbl payload, so CharacterSkins' shared resolver follows that reference before decoding.
//
// The prior mms/prt/psi/fct failure documented in .agents/plans/fix/ovl-texture-decoding.md came
// from guessing a payload shape from the psi tag. Resolution is now exact and installed-data proven.

namespace OpenCobra.OVL.Files;

public static class ParticleEffects {
  private const int ParticleRecordSize = 28;

  private static readonly ReferencedTextureField[] ParticleFields = [
    new(8, "tex", true),
  ];

  // Extract all particle sprite textures referenced by exact psi owners from an OVL.
  public static TextureCollection Extract(Ovl ovl) =>
    ReferencedTextureResolver.Extract(
      ovl,
      "particle sprite item",
      "psi",
      ParticleRecordSize,
      ParticleFields);
}
