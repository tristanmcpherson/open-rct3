// Wild Animal World Transform Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalWorldTransformTests {
  [Test]
  public void ToPark_ConjugatesRotationShearAndTranslationInRowVectorOrder() {
    var native = new Matrix4x4(
      0f, 1f, 0f, 0f,
      -1f, 0f, 0.5f, 0f,
      0.25f, 0f, 1f, 0f,
      10f, 20f, 30f, 1f);
    var expected = new Matrix4x4(
      0f, 0f, 1f, 0f,
      0.25f, 1f, 0f, 0f,
      -1f, 0.5f, 0f, 0f,
      10f, 30f, 20f, 1f);

    var actual = WildAnimalWorldTransform.ToPark(native);

    Assert.That(actual, Is.EqualTo(expected));
  }

  [Test]
  public void ToPark_RejectsEveryNonFiniteMatrixElement() {
    foreach (var index in Enumerable.Range(0, 16)) {
      var values = Values(Matrix4x4.Identity);
      values[index] = index % 2 == 0 ? float.NaN : float.PositiveInfinity;
      var native = Matrix(values);

      var error = Assert.Throws<InvalidDataException>(new Action(() =>
        WildAnimalWorldTransform.ToPark(native)));

      Assert.That(error!.Message, Does.Contain("non-finite"), $"matrix element {index}");
    }
  }

  private static Matrix4x4 Matrix(IReadOnlyList<float> values) => new(
    values[0], values[1], values[2], values[3],
    values[4], values[5], values[6], values[7],
    values[8], values[9], values[10], values[11],
    values[12], values[13], values[14], values[15]);

  private static float[] Values(Matrix4x4 value) => [
    value.M11, value.M12, value.M13, value.M14,
    value.M21, value.M22, value.M23, value.M24,
    value.M31, value.M32, value.M33, value.M34,
    value.M41, value.M42, value.M43, value.M44,
  ];
}
